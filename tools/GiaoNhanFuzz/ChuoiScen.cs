// [CHUOI 2026-10-29] Kịch bản cố định: đi đủ chuỗi Part Master → tạo KH (snapshot) → Kho nhận phôi → giao nhận + WTS bằng HÀM THẬT.
//   Số phải ra từng bước; sửa Part Master sau không được đổi PO cũ; PO mới tạo sau theo Part Master mới.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class ChuoiScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static ChuoiHelp.Shape Full() => new() { Gc = 3, ExclAt = 2, DongDang = true, DuPhong = true, Taro = 1, Bavia = 1, Rua = 1, Kcs = 2, Pkg = 1, TaroTaskCode = true, Prep = true, HtspDuPhong = true };

    public static async Task<int> Run()
    {
        Console.WriteLine("[C1] Part Master đầy đủ → tạo KH → snapshot khớp từng ô");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-001", Full());
            var pic = ChuoiHelp.Picture(db, pid);
            Ok(pic.Gc.Count == 4 && pic.Gc.Count(x => x.Split('|')[3] == "1") == 1 && pic.Gc[0].EndsWith("1:M1b:11:6:1:2:3:VJ-M1b:D8") && pic.Taro.Count == 3 && pic.Bavia.Count == 2 && pic.Rua.Count == 2 && pic.Kcs.Count == 2 && pic.Pkg.Count == 1 && pic.Excl.SequenceEqual(new[] { "H04" }),
               "Part Master dựng đúng: 3 NC + 1 dự phòng, NC01 có máy đồng dạng M1b, NC03 máy H04 (loại trừ), Taro T1(3T)+DP+1T, Bavia B1+1V, Rửa R1+1R, KCS 2, ĐG 1", pic.Sig);
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-001", new decimal[] { 40 }, "PO-1");
            Ok(ids.Count == 1 && ids[0] == 1 && db.KhPlans.List.Count == 1 && db.KhPlans.List[0].PlanNo == $"KH-{DateTime.Today:yyyyMMdd}-0001" && db.KhPlanDetails.List.Single().PurchaseOrder == "PO-1",
               "Tạo KH bằng CreateAsync: 1 phiếu, số KH đúng dạng, 1 dòng PO-1");
            var diff = ChuoiHelp.Diff(db, 1, pic, pid);
            Ok(diff == null, "CopyRouteSnapshotAsync: snapshot = Part Master lúc tạo (đủ cột 7 bảng + máy đồng dạng + cột nối nguồn + xác nhận TG chuẩn trỏ đúng dòng)", diff);
            var kho = new KhoVatLieuService(db, new UserService(null));
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, 40, "OVL-1", "A1", null, null, null, "nhan phoi", 1);
            var led = new HandoverLedgerService(db);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(r.Success && row.Phoi == 40 && row.Balanced, "Kho nhận 40 phôi bằng SaveKhoInputAsync (tự tạo dòng Kho): sổ Phôi = 40, cân", r.Error);
            var gcSteps = row.G("GC").Steps;
            Ok(gcSteps.Count == 3 && gcSteps.Select(s => s.Code).SequenceEqual(new[] { "NC01", "NC02", "NC03" }) && gcSteps[2].IsExcluded && !gcSteps.Any(s => s.Code.Contains("DP")),
               "Sổ GC đọc từ snapshot: 3 NC, NC03 là máy loại trừ, dòng dự phòng không thành NC riêng", string.Join(",", gcSteps.Select(s => s.Code + (s.IsExcluded ? "(X)" : ""))));
            var htsp = row.G("HTSP").Steps.Select(s => s.Pg + "." + s.Code).ToList();
            Ok(htsp.Contains("TARO.3T") && !htsp.Contains("TARO.T1") && htsp.Contains("BAVIA.B1") && htsp.Contains("WASHING.R1"),
               "HTSP: Taro tính theo mã CV 3T (không phải NC T1), Bavia B1, Rửa R1", string.Join(",", htsp));
        }

        Console.WriteLine("[C2] Sửa Part Master SAU khi tạo PO → PO cũ y nguyên; PO mới theo Part Master mới");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-002", Full());
            var pic0 = ChuoiHelp.Picture(db, pid);
            await ChuoiHelp.CreateKhAsync(db, pid, "P-002", new decimal[] { 30 }, "PO-2");
            var snap0 = ChuoiHelp.SnapPicture(db, 1).Sig;
            var rnd = new Random(7); var done = new List<string>();
            for (int k = 0; k < 9; k++) done.Add(await ChuoiHelp.MutateAsync(db, rnd, pid, k));
            var pic1 = ChuoiHelp.Picture(db, pid);
            Ok(pic1.Sig != pic0.Sig, "Đã sửa Part Master 9 kiểu bằng hàm thật: đổi tên NC, thêm NC, xoá NC, thêm/bỏ máy đồng dạng, bỏ máy loại trừ, Taro đổi mã, thêm dự phòng, thêm KCS/ĐG", string.Join(" · ", done));
            Ok(ChuoiHelp.SnapPicture(db, 1).Sig == snap0, "PO cũ: snapshot KHÔNG đổi sau mọi kiểu sửa Part Master");
            var ids2 = await ChuoiHelp.CreateKhAsync(db, pid, "P-002", new decimal[] { 20 }, "PO-2N");
            var diff2 = ChuoiHelp.Diff(db, ids2[0], pic1, pid);
            Ok(diff2 == null, "PO mới tạo sau: snapshot = Part Master MỚI", diff2);
            Ok(ChuoiHelp.SnapPicture(db, 1).Sig == snap0, "Tạo PO mới xong, PO cũ vẫn y nguyên");
            Ok(!db.KhPlanRouteSnapshotMachineExclude.List.Any(m => m.KhPlanDetailId == ids2[0]) && db.KhPlanRouteSnapshotMachineExclude.List.Any(m => m.KhPlanDetailId == 1 && m.SoMay == "H04"),
               "Máy loại trừ chụp theo bảng chung lúc tạo: PO cũ vẫn có H04, PO mới (đã bỏ H04) không có");
        }

        Console.WriteLine("[C3] Sổ / WTS của PO cũ đọc snapshot cũ, không đọc Part Master hiện tại");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-003", new ChuoiHelp.Shape { Gc = 2, DongDang = false, DuPhong = false, Taro = 1, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 1, Prep = false });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-003", new decimal[] { 20 }, "PO-3");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(1, DateTime.Now, 20, "OVL", "A1", null, null, null, "nhan phoi", 1);
            // Part Master đổi tên NC02 → NC02X (và bảng máy loại trừ thêm H04) SAU khi PO đã tạo
            await ChuoiHelp.MutateAsync(db, new Random(1), pid, 0);
            db.MachineExcludeSettings.Add(new MachineExcludeSetting { SoMay = "M1", AddedAt = DateTime.Now }); await db.SaveChangesAsync();
            var i1 = await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI", null, "NC01");
            var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 20, 0, null, 2);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var renamed = db.PartMachiningSteps.List.Single(s => s.NC.EndsWith("X")).NC;   // Random(1) đổi tên NC nào thì lấy NC đó
            Ok(i1.Ok && r1.Ok && row.G("GC").Steps.Select(s => s.Code).SequenceEqual(new[] { "NC01", "NC02" }) && !row.G("GC").Steps.Any(s => s.IsExcluded),
               $"PO cũ: sổ vẫn NC01, NC02 (Part Master đã thành {renamed}); M1 vào bảng loại trừ sau cũng không làm NC01 thành máy loại trừ", string.Join(",", row.G("GC").Steps.Select(s => s.Code + (s.IsExcluded ? "(X)" : ""))));
            var cap1 = HandoverLedgerService.WtsCap(row, "GC", "NC01"); var cap2a = HandoverLedgerService.WtsCap(row, "GC", "NC02"); var capNew = HandoverLedgerService.WtsCap(row, "GC", renamed);
            Ok(cap1.Max == 20 && !cap1.Locked && cap2a.Max == 0 && cap2a.Locked, "Trần WTS theo snapshot cũ: NC01 = 20 (vừa nhận), NC02 khoá (chưa có hàng)", $"NC01 max={cap1.Max} · NC02 max={cap2a.Max} locked={cap2a.Locked}");
            Console.WriteLine($"         (ghi nhận) WTS mã {renamed} (tên MỚI, không có trong snapshot PO): Max={capNew.Max} Locked={capNew.Locked} — {capNew.Explain?.Note}");
            // WTS NC01 10 → NC02 mở 10
            await VuHelp.Wts(db, "GC", "NC01", null, 10, 0, 2, 1);
            row = (await led.GetLedgerForDetailAsync(1))!;
            var cap2 = HandoverLedgerService.WtsCap(row, "GC", "NC02");
            Ok(cap2.Max == 10 && !cap2.Locked && row.Balanced, "WTS NC01 = 10 → trần NC02 = 10, sổ cân", $"max={cap2.Max}");
            // (ghi nhận) nếu có dòng WTS mang mã KHÔNG có trong snapshot (chỉ xảy ra khi dữ liệu bị sửa ngoài phần mềm) thì sổ xử lý ra sao
            await VuHelp.Wts(db, "GC", renamed, null, 3, 0, 2, 1);
            row = (await led.GetLedgerForDetailAsync(1))!;
            var gcG = row.G("GC"); var sumWait = gcG.Steps.Where(s => s.Waiting.HasValue).Sum(s => s.Waiting!.Value);
            Console.WriteLine($"         (ghi nhận) sau WTS {renamed}=3: GC Chưa làm={gcG.ChuaLam} Σchờ NC={sumWait} ĐãLàmChưaGiao={gcG.ConGiao} cân={row.Balanced} cảnh báo=[{string.Join(" | ", gcG.Warnings)}]");
            Ok(row.Balanced, "Dòng WTS mã lạ không làm lệch cân PO (phần mềm vẫn cân, chỉ cảnh báo)");
        }

        Console.WriteLine("[C4] Cập nhật quy trình (K6, luật [K6-NOTRUN] 30/10): PO chỉ mới cấp phôi Kho → GC vẫn làm mới được; có WTS thì chặn");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-004", Full());
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-004", new decimal[] { 10, 10 }, "PO-4");
            var kh = new KhPlanService(db); var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db);
            await ChuoiHelp.MutateAsync(db, new Random(3), pid, 1);   // thêm NC mới ở Part Master
            await ChuoiHelp.MutateAsync(db, new Random(3), pid, 3);   // thêm máy đồng dạng
            var picNow = ChuoiHelp.Picture(db, pid);
            await kho.SaveKhoInputAsync(ids[0], DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(ids[0], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            var ledA0 = await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[0]);
            string? err = null; try { await kh.RefreshRouteSnapshotAsync(ids[0], pid, 1, "cap nhat"); } catch (Exception ex) { err = ex.Message; }
            var diffA = ChuoiHelp.Diff(db, ids[0], picNow, pid);
            Ok(err == null && diffA == null, "PO A mới có phiếu Kho → GC (NC01 vẫn còn): làm mới được, snapshot A = Part Master hiện tại", err ?? diffA);
            var ledA1 = await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[0]);
            Ok(ledA1!.Balanced && ledA1.G("GC").ChoNhan == ledA0!.G("GC").ChoNhan && ledA1.G("KHO").ConGiao == ledA0.G("KHO").ConGiao, "PO A: sổ giữ nguyên (Gia công chờ nhận 10, Kho còn giao như trước), cân");
            var snapA = ChuoiHelp.SnapPicture(db, ids[0]).Sig;
            var txA = db.HandoverTransactions.List.Last(t => t.KhPlanDetailId == ids[0]).HandoverTxId;
            await svc.ReceiveAsync(txA, 10, 0, null, 2);
            var w = await WtsSvcHelp.SaveAsync(db, ids[0], "GC", "NC01", null, 3, 0, 2);
            Ok(w.Ok, "PO A: GC nhận 10 + ghi WTS NC01 = 3", w.Err);
            await ChuoiHelp.MutateAsync(db, new Random(5), pid, 1);
            err = null; try { await kh.RefreshRouteSnapshotAsync(ids[0], pid, 1, "cap nhat"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("K6") && err.Contains("WTS") && ChuoiHelp.SnapPicture(db, ids[0]).Sig == snapA, "PO A đã có WTS: K6 chặn, snapshot A giữ nguyên", err);
            var picNow2 = ChuoiHelp.Picture(db, pid);
            err = null; try { await kh.RefreshRouteSnapshotAsync(ids[1], pid, 1, "cap nhat"); } catch (Exception ex) { err = ex.Message; }
            var diffB = ChuoiHelp.Diff(db, ids[1], picNow2, pid);
            Ok(err == null && diffB == null, "PO B trắng: làm mới được, snapshot B = Part Master hiện tại (xoá sạch cũ + chép mới, xác nhận TG chuẩn viết lại)", err ?? diffB);
            Ok(ChuoiHelp.SnapPicture(db, ids[0]).Sig == snapA, "Làm mới PO B không đụng snapshot PO A");
            Ok(db.KhPlanDetailChangeLogs.List.Any(l => l.KhPlanDetailId == ids[1] && l.FieldName == "RouteSnapshot" && l.Reason == "cap nhat"), "Có dòng log RouteSnapshot cho PO B");
        }

        Console.WriteLine("[C12] [K6-NOTRUN] Lưu Part Master → PO chưa chạy tự lấy quy trình mới (Master list mục 13)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-012", Full());
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-012", new decimal[] { 10, 10, 10, 10, 10 }, "PO-12");
            var kh = new KhPlanService(db); var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db);
            // A trắng · B Kho → GC NC01 đã nhận · C có WTS · D Kho → GC vào NC02 (sẽ bị xoá ở Part Master) · E nhận có NG (phát sinh chứng từ lỗi)
            foreach (var i in new[] { 1, 2, 3, 4 }) await kho.SaveKhoInputAsync(ids[i], DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(ids[1], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
            await svc.IssueByCellAsync(ids[2], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
            var wc = await WtsSvcHelp.SaveAsync(db, ids[2], "GC", "NC01", null, 2, 0, 2);
            await svc.IssueByCellAsync(ids[3], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC02");
            await svc.IssueByCellAsync(ids[4], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            var re = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 9, 1, null, 2, ngCategoryId: db.NgCategories.List.First().NgCategoryId);
            Ok(wc.Ok && re.Ok, "Dựng 5 PO (C ghi WTS, E nhận có 1 NG)", wc.Err ?? re.Error);
            var ledB0 = await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[1]);
            var snapC = ChuoiHelp.SnapPicture(db, ids[2]).Sig; var snapD = ChuoiHelp.SnapPicture(db, ids[3]).Sig; var snapE = ChuoiHelp.SnapPicture(db, ids[4]).Sig;
            // Part Master: thêm NC mới + xoá NC02
            await ChuoiHelp.MutateAsync(db, new Random(1), pid, 1);
            var nc02 = db.PartMachiningSteps.List.First(s => s.PartId == pid && s.IsActive && !s.IsBackup && s.NC == "NC02");
            foreach (var b in db.PartMachiningSteps.List.Where(x => x.PartId == pid && x.IsActive && x.IsBackup && x.ParentNC == "NC02").ToList()) await new PartMachiningService(db).SoftDeleteAsync(b.StepId, 1, "xoa NC02");
            await new PartMachiningService(db).SoftDeleteAsync(nc02.StepId, 1, "xoa NC02"); db.Fixup();
            var picNow = ChuoiHelp.Picture(db, pid);
            var logs0 = db.KhPlanDetailChangeLogs.List.Count;
            // quy trình chưa chốt (mục B còn NG) → không đẩy
            var pmC = db.PartMasters.List.First(x => x.PartId == pid);
            pmC.IsMachiningConfirmed = false;
            var res0 = await kh.AutoRefreshRoutesForPartAsync(pid, 1, "luu nhap");
            Ok(res0.All(r => r.Status == KhPlanService.AutoRouteResult.Kept && (r.Note ?? "").Contains("chưa Xác nhận OK")) && ChuoiHelp.Diff(db, ids[0], picNow, pid) != null,
               "Mục B Part Master chưa Xác nhận OK: không đẩy quy trình sang PO nào", res0.FirstOrDefault()?.Note);
            pmC.IsPlanConfirmed = pmC.IsMachiningConfirmed = pmC.IsHtspConfirmed = pmC.IsKcsConfirmed = pmC.IsPkgConfirmed = true;   // như bấm "Lưu & Xác nhận OK"
            var res = await kh.AutoRefreshRoutesForPartAsync(pid, 1, "luu part master");
            string St(int i) => res.First(r => r.KhPlanDetailId == ids[i]).Status;
            string? Nt(int i) => res.First(r => r.KhPlanDetailId == ids[i]).Note;
            Ok(St(0) == KhPlanService.AutoRouteResult.Updated && ChuoiHelp.Diff(db, ids[0], picNow, pid) == null, "A trắng: tự cập nhật = Part Master mới", St(0));
            Ok(St(1) == KhPlanService.AutoRouteResult.Updated && ChuoiHelp.Diff(db, ids[1], picNow, pid) == null, "B mới cấp phôi Kho → GC NC01 (đã nhận, chưa WTS): tự cập nhật (khách: vẫn là PO chưa chạy)", St(1) + " " + Nt(1));
            var ledB1 = await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[1]);
            Ok(ledB1!.Balanced && ledB1.G("GC").ChuaLam == ledB0!.G("GC").ChuaLam && ledB1.G("GC").ChuaLam == 10, "B: sổ giữ nguyên (GC chưa làm 10), cân", $"{ledB0.G("GC").ChuaLam} → {ledB1.G("GC").ChuaLam}");
            Ok(St(2) == KhPlanService.AutoRouteResult.Kept && (Nt(2) ?? "").Contains("WTS") && ChuoiHelp.SnapPicture(db, ids[2]).Sig == snapC, "C có WTS: giữ quy trình cũ, báo lý do", Nt(2));
            Ok(St(3) == KhPlanService.AutoRouteResult.Kept && (Nt(3) ?? "").Contains("NC02") && ChuoiHelp.SnapPicture(db, ids[3]).Sig == snapD, "D đã cấp phôi vào NC02 mà Part Master xoá NC02: giữ quy trình cũ, báo NC02", Nt(3));
            Ok(St(4) == KhPlanService.AutoRouteResult.Kept && ChuoiHelp.SnapPicture(db, ids[4]).Sig == snapE, "E nhận có NG (đã có chứng từ lỗi): giữ quy trình cũ", Nt(4));
            Ok(db.KhPlanDetailChangeLogs.List.Count(l => l.FieldName == "RouteSnapshot") - 0 >= 2 && db.KhPlanDetailChangeLogs.List.Any(l => l.KhPlanDetailId == ids[1] && l.FieldName == "RouteSnapshot" && (l.Reason ?? "").StartsWith("Tự cập nhật")), "Có log RouteSnapshot 'Tự cập nhật theo Part Master' cho A, B");
            var logs1 = db.KhPlanDetailChangeLogs.List.Count;
            var res2 = await kh.AutoRefreshRoutesForPartAsync(pid, 1, "luu lai");
            Ok(res2.Where(r => r.KhPlanDetailId == ids[0] || r.KhPlanDetailId == ids[1]).All(r => r.Status == KhPlanService.AutoRouteResult.Same) && db.KhPlanDetailChangeLogs.List.Count == logs1,
               "Lưu lại Part Master không đổi gì: A, B 'không đổi', không ghi log thừa", string.Join(",", res2.Select(r => r.Status)));
            foreach (var i in Enumerable.Range(0, 5))
            {
                var l = await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[i]);
                Ok(l!.Balanced, $"PO {i + 1}: sổ cân sau tự cập nhật");
            }
        }

        Console.WriteLine("[C13] [K6-NOTRUN] phiếu Kho → GC không chỉ định NC (mặc định NC đầu) + NC chỉ định đổi sang máy loại trừ");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-013", new ChuoiHelp.Shape { Gc = 3, ExclAt = -1, DongDang = false, DuPhong = false, Taro = 1, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 1, Prep = false });
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-013", new decimal[] { 10, 10 }, "PO-13");
            var kh = new KhPlanService(db); var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db);
            foreach (var i in new[] { 0, 1 }) await kho.SaveKhoInputAsync(ids[i], DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            var g0 = await svc.IssueByCellAsync(ids[0], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, null);   // mặc định NC đầu
            var g1 = await svc.IssueByCellAsync(ids[1], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC02");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
            Ok(g0.Ok && g1.Ok, "PO A: Kho → GC mặc định NC đầu (chờ nhận) · PO B: Kho → GC vào NC02 (đã nhận)", g0.Error ?? g1.Error);
            var ledA0 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[0]))!; var ledB0 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[1]))!;
            var snapB = ChuoiHelp.SnapPicture(db, ids[1]).Sig;
            // Part Master: thêm NC + NC02 đổi máy đăng ký sang máy loại trừ H04
            await ChuoiHelp.MutateAsync(db, new Random(2), pid, 1);
            db.PartMachiningSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "NC02").MachineRegistered = "H04"; db.Fixup();
            var pm = db.PartMasters.List.First(x => x.PartId == pid); pm.IsPlanConfirmed = pm.IsMachiningConfirmed = pm.IsHtspConfirmed = pm.IsKcsConfirmed = pm.IsPkgConfirmed = true;
            var picNow = ChuoiHelp.Picture(db, pid);
            var res = await kh.AutoRefreshRoutesForPartAsync(pid, 1, "luu part master");
            var rA = res.First(r => r.KhPlanDetailId == ids[0]); var rB = res.First(r => r.KhPlanDetailId == ids[1]);
            Ok(rA.Status == KhPlanService.AutoRouteResult.Updated && ChuoiHelp.Diff(db, ids[0], picNow, pid) == null, "A (phiếu mặc định NC đầu): tự cập nhật", rA.Status + " " + rA.Note);
            var ledA1 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[0]))!;
            Ok(ledA1.Balanced && ledA1.G("GC").ChoNhan == ledA0.G("GC").ChoNhan && ledA1.G("KHO").ConGiao == ledA0.G("KHO").ConGiao, "A: sổ giữ nguyên, cân");
            Ok(rB.Status == KhPlanService.AutoRouteResult.Kept && (rB.Note ?? "").Contains("NC02") && ChuoiHelp.SnapPicture(db, ids[1]).Sig == snapB,
               "B (hàng đã ở NC02, NC02 thành máy loại trừ): giữ quy trình cũ, không dời hàng", rB.Note);
            var ledB1 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[1]))!;
            Ok(ChuoiHelp.LedgerSig(ledB1) == ChuoiHelp.LedgerSig(ledB0), "B: sổ không đổi");
        }

        Console.WriteLine("[C5] Dòng dự phòng (N2): WTS ghi NC dự phòng gộp vào NC chính");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-005", new ChuoiHelp.Shape { Gc = 2, ExclAt = -1, DongDang = true, DuPhong = true, Taro = 0, Bavia = 0, Rua = 0, Kcs = 0, Pkg = 0, Prep = false });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-005", new decimal[] { 12 }, "PO-5");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(1, DateTime.Now, 12, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(1, "KHO", "GC", 12, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(1, 12, 0, null, 2);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var capDp = HandoverLedgerService.WtsCap(row, "GC", "NC01-DP-01");
            Ok(capDp.Max == 12 && capDp.Code == "NC01", "Trần WTS của NC01-DP-01 = trần NC01 (12), mã chính = NC01", $"max={capDp.Max} code={capDp.Code}");
            await VuHelp.Wts(db, "GC", "NC01-DP-01", null, 5, 0, 2, 1);
            await VuHelp.Wts(db, "GC", "NC01", null, 4, 0, 2, 1);
            row = (await led.GetLedgerForDetailAsync(1))!;
            var nc1 = row.G("GC").Steps.First(s => s.Code == "NC01"); var nc2 = row.G("GC").Steps.First(s => s.Code == "NC02");
            Ok(nc1.Ok == 9 && nc1.Waiting == 3 && nc2.Waiting == 9 && row.Balanced, "WTS 5 (dự phòng) + 4 (NC01) → NC01 OK 9, chờ 3; NC02 chờ 9; cân", $"NC01 ok={nc1.Ok} chờ={nc1.Waiting} NC02 chờ={nc2.Waiting}");
            var over = HandoverLedgerService.WtsCap(row, "GC", "NC01-DP-01");
            Ok(over.Max == 3, "Trần còn lại của NC01-DP-01 = 3 (đã gộp 9)", $"max={over.Max}");
        }

        Console.WriteLine("[C6] Tạo KH: số PO trùng bị chặn (B5), lỗi giữa chừng không để lại PO nửa vời (E1)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-006", Full());
            await ChuoiHelp.CreateKhAsync(db, pid, "P-006", new decimal[] { 5 }, "PO-6");
            int plans = db.KhPlans.List.Count, dets = db.KhPlanDetails.List.Count, snaps = db.KhPlanRouteSnapshotMachining.List.Count;
            string? e1 = null; try { await ChuoiHelp.CreateKhAsync(db, pid, "P-006", new decimal[] { 5 }, "PO-6"); } catch (Exception ex) { e1 = ex.Message; }
            Ok(e1 != null && e1.Contains("đã có trong hệ thống") && db.KhPlans.List.Count == plans && db.KhPlanDetails.List.Count == dets && db.KhPlanRouteSnapshotMachining.List.Count == snaps,
               "PO-6 đã có: CreateAsync chặn, không sinh phiếu / dòng / snapshot nào", e1);
            string? e2 = null; try { var kh = new KhPlanService(db); var p = new KhPlan { CustomerId = 1, PlanDate = DateTime.Today }; p.Details.Add(new KhPlanDetail { PartNo = "P-006", PurchaseOrder = "PO-7", Quantity = 1 }); p.Details.Add(new KhPlanDetail { PartNo = "P-006", PurchaseOrder = "po-7", Quantity = 1 }); await kh.CreateAsync(p, 1); } catch (Exception ex) { e2 = ex.Message; }
            Ok(e2 != null && e2.Contains("trùng trong phiếu") && db.KhPlans.List.Count == plans, "Cùng phiếu 2 dòng PO-7 / po-7: chặn (không phân biệt hoa thường)", e2);
            // Lỗi giữa chừng: CopyRouteSnapshot với partId không tồn tại → không có bảng nào nhận dòng → vẫn commit được (không ném) → kiểm riêng: ném trong giao dịch thì rollback hết
            var db2 = new AppDbContext(); ChuoiHelp.SeedBase(db2, exclH04: false);
            var pid2 = await ChuoiHelp.BuildPartAsync(db2, "P-008", Full());
            string? e3 = null;
            try
            {
                var kh = new KhPlanService(db2); var p = new KhPlan { CustomerId = 1, PlanDate = DateTime.Today }; p.Details.Add(new KhPlanDetail { PartNo = "P-008", PurchaseOrder = "PO-8", Quantity = 3 });
                await using var trx = await db2.Database.BeginTransactionAsync();
                try { await kh.CreateAsync(p, 1); await kh.CopyRouteSnapshotAsync(p.Details[0].KhPlanDetailId, pid2, 1); throw new InvalidOperationException("giả lỗi lúc lưu IsPlanConfirmed"); }
                catch { await trx.RollbackAsync(); db2.ChangeTracker.Clear(); throw; }
            }
            catch (Exception ex) { e3 = ex.Message; }
            Ok(e3 != null && db2.KhPlans.List.Count == 0 && db2.KhPlanDetails.List.Count == 0 && db2.KhPlanRouteSnapshotMachining.List.Count == 0 && db2.KhPlanRouteSnapshotStdConfirms.List.Count == 0,
               "Khuôn gọi như trang (1 giao dịch: CreateAsync + CopyRouteSnapshot) gặp lỗi thì rollback sạch phiếu + dòng + snapshot + xác nhận (chứng minh giao dịch bọc đủ các bước; stub mô phỏng rollback)", e3);
        }

        Console.WriteLine("[C7] 2 PO cùng phiếu KH: thao tác trên PO này không đổi sổ PO kia");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-009", Full());
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-009", new decimal[] { 10, 20 }, "PO-9");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(ids[0], DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await kho.SaveKhoInputAsync(ids[1], DateTime.Now, 20, "OVL", "A2", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(ids[1], "KHO", "GC", 20, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 18, 2, "xuoc", 2, null, null, null, 2);
            var sigB = ChuoiHelp.LedgerSig((await led.GetLedgerForDetailAsync(ids[1]))!);
            var sigA0 = ChuoiHelp.LedgerSig((await led.GetLedgerForDetailAsync(ids[0]))!);
            await svc.IssueByCellAsync(ids[0], "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
            var w = new WtsProductionLog { KhPlanDetailId = ids[0], ProcessGroup = "GC", NC = "NC01", WorkerId = 2, QtyDone = 4, QtyOk = 4, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now };
            db.WtsProductionLogs.Add(w); await db.SaveChangesAsync();
            var rowA = (await led.GetLedgerForDetailAsync(ids[0]))!; var rowB = (await led.GetLedgerForDetailAsync(ids[1]))!;
            Ok(ChuoiHelp.LedgerSig(rowB) == sigB && rowB.Balanced, "Giao / nhận / WTS trên PO A: sổ PO B không đổi, vẫn cân");
            Ok(ChuoiHelp.LedgerSig(rowA) != sigA0 && rowA.Balanced && rowA.G("GC").Steps.First(s => s.Code == "NC01").Ok == 4 && rowA.Phoi == 10, "Sổ PO A đổi đúng (NC01 OK 4, phôi 10), cân");
            var trB = await led.GetNgTraceAsync(ids[1]); var trA = await led.GetNgTraceAsync(ids[0]);
            Ok(trB!.PhatSinh == 2 && trA!.PhatSinh == 0, "Hàng lỗi PO B (2) không lẫn sang PO A (0)");
        }

        Console.WriteLine("[C8] Workflow OFF từ lúc tạo KH + Part Master không có HTSP / KCS");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-010", new ChuoiHelp.Shape { Gc = 1, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 0, Pkg = 1, Prep = false });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-010", new decimal[] { 8 }, "PO-10", workflow: false);
            var led = new HandoverLedgerService(db); var kho = new KhoVatLieuService(db, new UserService(null));
            await kho.SaveKhoInputAsync(1, DateTime.Now, 8, "OVL", "A1", null, null, null, "nhan phoi", 1);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(!row.ApplyWorkflow && row.G("HTSP").Steps.Count == 0 && row.G("KCS").Steps.Count == 0 && row.G("PKG").Steps.Count == 1 && row.Balanced, "Sổ đọc ApplyWorkflow=OFF từ phiếu; HTSP / KCS không có mã, ĐG 1 mã; cân");
        }

        Console.WriteLine("[C9] Đổi tên NC chính khi đã có dòng dự phòng → dự phòng phải vẫn gộp vào NC chính (N2)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-011", new ChuoiHelp.Shape { Gc = 2, ExclAt = -1, DongDang = false, DuPhong = true, Taro = 0, Bavia = 0, Rua = 0, Kcs = 0, Pkg = 0, Prep = false });
            var gc = new PartMachiningService(db);
            var s1 = db.PartMachiningSteps.List.Single(s => s.PartId == pid && s.NC == "NC01");
            await gc.UpdateAsync(s1.StepId, new PartMachiningStep { NC = "NC01X", MachineRegistered = s1.MachineRegistered, Drawing = s1.Drawing, FixtureType = s1.FixtureType, ToolType = s1.ToolType, TimingMachine = s1.TimingMachine, SetupTime = s1.SetupTime, MachiningTime = s1.MachiningTime, InspectionTime = s1.InspectionTime, PreparationTime = s1.PreparationTime, TrialRunTime = s1.TrialRunTime }, ChuoiHelp.Admin, "doi ten");
            var dp = db.PartMachiningSteps.List.Single(s => s.PartId == pid && s.IsBackup);
            Console.WriteLine($"         (ghi nhận) sau đổi tên NC01 → NC01X: dòng dự phòng {dp.NC} có ParentNC = «{dp.ParentNC}»");
            await ChuoiHelp.CreateKhAsync(db, pid, "P-011", new decimal[] { 10 }, "PO-11");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(1, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01X");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var capDp = HandoverLedgerService.WtsCap(row, "GC", dp.NC);
            Ok(capDp.Code == "NC01X" && capDp.Max == 10, $"PO tạo sau đổi tên: trần WTS của dự phòng {dp.NC} phải gộp về NC01X (=10)", $"code={capDp.Code} max={capDp.Max} locked={capDp.Locked}");
        }

        Console.WriteLine("[C10] NC máy loại trừ đứng CUỐI chuỗi GC: giao theo NC từ NC trước nó không được làm Chưa GC âm");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-012", new ChuoiHelp.Shape { Gc = 2, ExclAt = 1, DongDang = false, DuPhong = false, Taro = 1, Bavia = 0, Rua = 0, Kcs = 0, Pkg = 0, Prep = false, TaroTaskCode = false });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-012", new decimal[] { 10 }, "PO-12");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(1, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            await VuHelp.Wts(db, "GC", "NC01", null, 6, 0, 2, 1);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var gcS = row.G("GC").Steps.Select(s => s.Code + (s.IsExcluded ? "(X)" : "") + "=" + s.Ok + "/" + s.Waiting).ToList();
            Ok(row.G("GC").Steps.Count == 2 && row.G("GC").Steps[1].IsExcluded && row.G("GC").ConGiao == 6, "Chuỗi GC = NC01, NC02(H04 loại trừ, cuối): OK NC01 = 6 đã là Đã làm chưa giao của Gia công", string.Join(",", gcS) + $" ConGiao={row.G("GC").ConGiao}");
            var (capHelp, exHelp) = HandoverLedgerService.IssueCapCase(row, "GC", HandoverPurpose.HelpSend, "NC01", null, null);
            Ok(capHelp == 0, "Giao theo NC từ NC01 (NC cuối thật, sau nó chỉ còn máy loại trừ) phải bị chặn như [GN-HELP-LAST] — 1 chiếc không thể vừa là Đã làm chưa giao vừa giao theo NC", $"trần={capHelp} · {exHelp.Note}");
            var r = await svc.IssueSpecialAsync(1, "GC", "HTSP", 3, 2, HandoverPurpose.HelpSend, "NC01", null, "lam ho");
            row = (await led.GetLedgerForDetailAsync(1))!;
            var wh = HandoverLedgerService.WhereIs(row);
            var neg = wh.Items.Where(i => i.Qty < 0).Select(i => $"{i.Place}/{i.Label}={i.Qty}").ToList();
            Ok(!r.Ok || (!neg.Any() && row.Balanced && HandoverLedgerService.NegativeCells(row).Count == 0), "Sau khi thử giao theo NC 3 chiếc: hoặc bị chặn, hoặc không ô nào âm", (r.Ok ? "ĐÃ LƯU ĐƯỢC · " : "chặn: " + r.Error + " · ") + string.Join(", ", neg));
        }

        Console.WriteLine("[C11] Xoá NC chính đang có dòng dự phòng → dự phòng xoá kèm (PM-DEL-BACKUP), PO cũ không đổi");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-013", new ChuoiHelp.Shape { Gc = 3, ExclAt = -1, DongDang = true, DuPhong = true, Taro = 1, Bavia = 0, Rua = 0, Kcs = 0, Pkg = 0, Prep = false, HtspDuPhong = true });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-013", new decimal[] { 5 }, "PO-13");
            var snap0 = ChuoiHelp.SnapPicture(db, 1).Sig;
            var gc = new PartMachiningService(db);
            var main = db.PartMachiningSteps.List.Single(s => s.PartId == pid && s.NC == "NC01");
            var dp = db.PartMachiningSteps.List.Single(s => s.PartId == pid && s.IsBackup);
            var tim = db.PartMachiningTimings.List.Count(t => t.StepId == main.StepId && t.IsActive);
            await gc.SoftDeleteAsync(main.StepId, ChuoiHelp.Admin, "xoa NC01");
            var logs = db.PartProcessStepChangeLogs.List.Where(l => l.FieldName == "Deleted" && (l.StepId == main.StepId || l.StepId == dp.StepId)).ToList();
            Ok(!db.PartMachiningSteps.List.Any(s => s.StepId == main.StepId || s.StepId == dp.StepId) && !db.PartMachiningTimings.List.Any(t => t.StepId == main.StepId),
               $"Xoá NC01 (có {tim} máy đồng dạng + dự phòng NC01-DP-01): cả 2 dòng + máy đồng dạng đã xoá khỏi bảng chính");
            Ok(logs.Count(l => l.StepId == dp.StepId && l.TimingId == null) == 1 && logs.First(l => l.StepId == dp.StepId && l.TimingId == null).Reason!.Contains("xoá theo NC chính NC01"),
               "Dòng dự phòng có log Xoá riêng, lý do ghi 'xoá theo NC chính NC01'", string.Join(" | ", logs.Select(l => $"#{l.StepId}:{l.Reason}")));
            Ok(!db.PartMachiningSteps.List.Any(s => s.PartId == pid && s.IsBackup && !db.PartMachiningSteps.List.Any(m => !m.IsBackup && m.NC == s.ParentNC)), "Không còn dự phòng mồ côi ở Part");
            Ok(ChuoiHelp.SnapPicture(db, 1).Sig == snap0, "Snapshot PO cũ không đổi (vẫn đủ NC01 + dự phòng)");
            var taro = new PartTaroService(db);
            var t1 = db.PartTaroSteps.List.Single(s => s.PartId == pid && s.NC == "T1"); var tdp = db.PartTaroSteps.List.Single(s => s.PartId == pid && s.IsBackup);
            await taro.SoftDeleteAsync(t1.StepId, ChuoiHelp.Admin, "xoa T1");
            Ok(!db.PartTaroSteps.List.Any(s => s.StepId == t1.StepId || s.StepId == tdp.StepId) && db.PartProcessStepChangeLogs.List.Any(l => l.StepId == tdp.StepId && l.FieldName == "Deleted"),
               "Taro: xoá T1 thì T1-DP xoá kèm + có log");
        }

        // [N3-FIRST 2026-10-30] Kiểm lại N3 sau [GN-ROUTE-FREE]: NC máy loại trừ đứng ĐẦU chuỗi GC (NC01 = H04).
        //   Không có NC nguồn trước nó → không giao theo NC được; sổ cho NC02 nhận thẳng hàng vào Gia công.
        //   Kịch bản canh in/out (cân, không ô âm, trần WTS khớp sổ) trên cả 2 đường xưởng có thể đi; dòng GHI NHẬN in ra cách sổ đang tính.
        Console.WriteLine("[C14] [N3-FIRST] NC máy loại trừ đứng ĐẦU chuỗi GC (N3): in/out cân trên mọi đường đi, không ô âm");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-014", new ChuoiHelp.Shape { Gc = 3, ExclAt = 0, DongDang = false, DuPhong = false, Taro = 1, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 1, Prep = false, TaroTaskCode = false });
            await ChuoiHelp.CreateKhAsync(db, pid, "P-014", new decimal[] { 10 }, "PO-14");
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await kho.SaveKhoInputAsync(1, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            bool Clean(LedgerRow rw) => rw.Balanced && HandoverLedgerService.NegativeCells(rw).Count == 0 && HandoverLedgerService.WhereIs(rw).Items.All(i => i.Qty >= 0);
            string Gc(LedgerRow rw) => string.Join(",", rw.G("GC").Steps.Select(s => s.Code + (s.IsExcluded ? "(X)" : "") + $" vào {s.In} chờ {s.Waiting}"));
            // Đường 1: Kho → Gia công 6, rồi Gia công thử giao theo NC cho nhóm làm H04
            var g1 = await svc.IssueByCellAsync(1, "KHO", "GC", 6, 1, "MOI", false, "MOI", null, "NC02");
            var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 6, 0, null, 2);
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var st = row.G("GC").Steps;
            Ok(g1.Ok && r1.Ok && st.Count == 3 && st[0].IsExcluded && st[1].In == 6 && st[2].In == 0 && Clean(row),
               "Kho → Gia công 6: NC01 (H04) loại trừ đầu chuỗi, NC02 nhận vào 6, NC03 0; cân, không ô âm", Gc(row));
            // [N3-TONC] máy chủ chỉ nhận NC thường có trong snapshot: NC loại trừ / NC lạ bị chặn (trước: lưu được, sổ dồn vào NC đầu)
            var bx = await svc.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI", null, "NC01");
            var bu = await svc.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI", null, "NC99");
            Ok(!bx.Ok && !bu.Ok && bx.Error.Contains("không phải NC nhận hàng") && bu.Error.Contains("không phải NC nhận hàng") && db.HandoverTransactions.List.Count(t => !t.IsVoided) == 1,
               "Giao vào NC01 (máy loại trừ) hoặc NC99 (không có) → máy chủ chặn, không lập phiếu", bx.Error + " | " + bu.Error);
            var capX = HandoverLedgerService.IssueCapCase(row, "GC", HandoverPurpose.HelpSend, "NC01", null, null).Max;
            var capN2 = HandoverLedgerService.IssueCapCase(row, "GC", HandoverPurpose.HelpSend, "NC02", null, null).Max;
            Ok(capX == 0 && capN2 == 0, "Giao theo NC: NC01 là máy loại trừ, NC02 chưa có OK → trần 0 (không có đường gửi làm hộ cho NC đầu chuỗi)", $"NC01={capX} NC02={capN2}");
            var capW = HandoverLedgerService.WtsCap(row, "GC", "NC02").Max;
            Console.WriteLine($"  GHI NHẬN  NC01 loại trừ ở đầu: WTS NC02 được nhập tới {capW} (= hàng vào Gia công) — công đoạn H04 không có chứng từ (chờ Hùng chốt luật)");
            await VuHelp.Wts(db, "GC", "NC02", null, 4, 0, 2, 1);
            row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(Clean(row) && row.G("GC").Steps[2].In == 4, "WTS NC02 OK 4 → NC03 nhận vào 4; cân, không ô âm", Gc(row));
            // Đường 2: Kho giao thẳng 4 phôi Hàng mới cho HTSP (nhóm làm H04), HTSP giao lại Gia công → vào NC02
            var g2 = await svc.IssueByCellAsync(1, "KHO", "HTSP", 4, 1, "MOI", false, "MOI");
            var r2 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 4, 0, null, 3);
            row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(g2.Ok && r2.Ok && row.G("HTSP").ChuaLam == 4 && Clean(row), "Kho → HTSP 4 (Hàng mới): vào Chưa làm HTSP 4; cân", $"HTSP chưa làm {row.G("HTSP").ChuaLam}");
            var hr = await svc.IssueSpecialAsync(1, "HTSP", "GC", 4, 3, HandoverPurpose.HelpReturn, null, "NC02", "tra ve");
            Ok(!hr.Ok, "HTSP trả về Gia công theo NC khi chưa nhận hàng giao theo NC → chặn (Tồn theo NC = 0)", hr.Error);
            var tcode = row.G("HTSP").Steps.First(s => s.Pg == "TARO").Code;
            await VuHelp.Wts(db, "TARO", null, tcode, 4, 0, 3, 1);
            row = (await led.GetLedgerForDetailAsync(1))!;
            var g3 = await svc.IssueByCellAsync(1, "HTSP", "GC", 4, 3, "MOI", false, "MOI", null, "NC02");
            var r3 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 4, 0, null, 2);
            row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(g3.Ok && r3.Ok && row.G("GC").Steps[1].In == 10 && Clean(row),
               "HTSP làm xong giao Hàng mới 4 cho Gia công vào NC02 → NC02 nhận vào 10; cân, không ô âm", Gc(row) + " · " + g3.Error + r3.Error);
            Console.WriteLine($"  GHI NHẬN  đường 2: 4 chiếc tính vào 4 số của HTSP (Nhận HTSP = {row.G("HTSP").NhanTong}), không phải 'hàng làm hộ' (K4)");
            // Huỷ ngược cả 2 đường: không ô âm, phôi về Kho
            var vr3 = await svc.VoidReceiveAsync(db.HandoverReceives.List.Last().ReceiveId, "huy thu", 1, new[] { "KHO", "GC", "HTSP", "KCS", "PKG" });
            row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(vr3.Ok && Clean(row), "Huỷ lần nhận HTSP → Gia công: lưu được, cân, không ô âm", vr3.Error);
        }

        Console.WriteLine("[C15] [PM-NC-DUP-SVC] Service Part Master chặn mã chính trùng / trống (Gia công, Bavia, Rửa, KCS, Đóng gói) dưới khoá MES_PM_STEPS_<Part>");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-015", Full());
            var lk = "MES_PM_STEPS_" + pid;
            var pkg = new PartPackagingService(db); var kcs = new PartInspectionService(db); var gc = new PartMachiningService(db);
            var bav = new PartBaviaService(db); var rua = new PartWashingService(db); var taro = new PartTaroService(db);
            async Task<string?> Err(Func<Task> a) { try { await a(); return null; } catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; } }
            int Pk(string nc) => db.PartPackagingSteps.List.Count(x => x.PartId == pid && x.IsActive && !x.IsBackup && PartStepGuard.SameCode(x.NC, nc));
            int nPkg0 = db.PartPackagingSteps.List.Count(x => x.PartId == pid);

            // (1) thêm trùng / trống
            var e1 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P1", StepName = "dup", StandardTime = 1 }, 1, "thu dup"));
            var e2 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = " p1 ", StepName = "dup", StandardTime = 1 }, 1, "thu dup"));
            var e3 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "-", StepName = "trong", StandardTime = 1 }, 1, "thu trong"));
            var e4 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "  ", StepName = "trong", StandardTime = 1 }, 1, "thu trong"));
            Ok(e1 != null && e1.StartsWith("PartStepCodeException") && e1.Contains("P1") && e2 != null && e2.StartsWith("PartStepCodeException") && e3 != null && e3.Contains("chưa nhập") && e4 != null && e4.Contains("chưa nhập")
               && db.PartPackagingSteps.List.Count(x => x.PartId == pid) == nPkg0 && Pk("P1") == 1 && db.Database.CurrentTransaction == null,
               "Đóng gói: thêm P1 (đã có) / ' p1 ' (khác hoa thường + khoảng trắng) / '-' / trống → chặn, không thêm dòng, giao dịch đã đóng", $"{e1} | {e2} | {e3} | {e4}");
            var e5 = await Err(() => gc.AddAsync(pid, new PartMachiningStep { NC = "nc02", MachineRegistered = "M9", SetupTime = 1 }, 1, "thu dup"));
            var e6 = await Err(() => bav.AddAsync(pid, new PartBaviaStep { NC = "B1", StepName = "dup", StandardTime = 1 }, 1, "thu dup"));
            var e7 = await Err(() => rua.AddAsync(pid, new PartWashingStep { NC = "1R", StepName = "dup", StandardTime = 1 }, 1, "thu dup"));
            var e8 = await Err(() => kcs.AddAsync(pid, new PartInspectionStep { NC = "K2", StepName = "dup", StandardTime = 1 }, 1, "thu dup"));
            Ok(new[] { e5, e6, e7, e8 }.All(e => e != null && e.StartsWith("PartStepCodeException")) && e5!.StartsWith("PartStepCodeException: Gia công") && e6!.Contains("Bavia") && e7!.Contains("Rửa") && e8!.Contains("KCS"),
               "Gia công 'nc02' / Bavia B1 / Rửa 1R (mã chuẩn bị cũng là dòng chính) / KCS K2 trùng → chặn, lời báo nêu đúng mục", $"{e5} | {e6} | {e7} | {e8}");
            // (2) dòng dự phòng không xét; mã mới không trùng thì thêm được
            var e9 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P1", IsBackup = true, ParentNC = "P1", StepName = "DP", StandardTime = 1 }, 1, "du phong"));
            var e10 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P2", StepName = "moi", StandardTime = 1 }, 1, "them P2"));
            Ok(e9 == null && e10 == null && Pk("P2") == 1, "Thêm dòng dự phòng mã P1 + dòng chính P2 (chưa có) → lưu được (không chặn oan)", e9 ?? e10);
            // (3) sửa
            var k2 = db.PartInspectionSteps.List.Single(x => x.PartId == pid && x.IsActive && x.NC == "K2");
            var u1 = await Err(() => kcs.UpdateAsync(k2.StepId, new PartInspectionStep { NC = "K1", StepName = k2.StepName, StandardTime = k2.StandardTime }, 1, "doi ma"));
            var u2 = await Err(() => kcs.UpdateAsync(k2.StepId, new PartInspectionStep { NC = "k2", StepName = k2.StepName, StandardTime = 3 }, 1, "doi hoa thuong"));
            var u3 = await Err(() => kcs.UpdateAsync(k2.StepId, new PartInspectionStep { NC = "K3", StepName = k2.StepName, StandardTime = 3 }, 1, "doi ma K3"));
            Ok(u1 != null && u1.StartsWith("PartStepCodeException") && u2 == null && u3 == null && k2.NC == "K3" && db.Database.CurrentTransaction == null,
               "KCS: đổi K2 → K1 (đã có) chặn; K2 → k2 (chỉ đổi hoa thường) + K2 → K3 lưu được", $"{u1} | {u2} | {u3} | NC={k2.NC}");
            var nc2 = db.PartMachiningSteps.List.Single(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "NC02");
            var u4 = await Err(() => gc.UpdateAsync(nc2.StepId, new PartMachiningStep { NC = "NC01", MachineRegistered = nc2.MachineRegistered, SetupTime = 7 }, 1, "doi ten"));
            Ok(u4 != null && u4.StartsWith("PartStepCodeException: Gia công") && nc2.NC == "NC02" && nc2.SetupTime != 7, "Gia công: đổi NC02 → NC01 (đã có) chặn, dòng giữ nguyên", u4);
            // dữ liệu cũ đã trùng (trước khi có chặn) → sửa ô khác vẫn được; đổi sang mã trùng khác vẫn chặn
            var old = new PartPackagingStep { StepId = 9001, PartId = pid, StepOrder = 99, NC = "P1", StepName = "cu trung", StandardTime = 1, IsActive = true, CreatedBy = 1, CreatedAt = DateTime.Now };
            db.PartPackagingSteps.List.Add(old); db.Fixup();
            var u5 = await Err(() => pkg.UpdateAsync(9001, new PartPackagingStep { NC = "P1", StepName = "cu trung", StandardTime = 4 }, 1, "sua gio"));
            var u6 = await Err(() => pkg.UpdateAsync(9001, new PartPackagingStep { NC = "P2", StepName = "cu trung", StandardTime = 4 }, 1, "doi ma"));
            Ok(u5 == null && old.StandardTime == 4 && u6 != null && u6.StartsWith("PartStepCodeException"), "Dòng cũ đã trùng P1 (dữ liệu trước 02/11): sửa giờ vẫn lưu; đổi sang P2 (đã có) chặn", $"{u5} | {u6}");
            // dự phòng → chính với mã đã có: chặn
            var dp = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && x.IsBackup && x.NC == "P1");
            var u7 = await Err(() => pkg.UpdateAsync(dp.StepId, new PartPackagingStep { NC = "P1", IsBackup = false, StepName = "DP", StandardTime = 2 }, 1, "thanh chinh"));
            Ok(u7 != null && u7.StartsWith("PartStepCodeException") && dp.IsBackup, "Dòng dự phòng P1 chuyển thành dòng chính (P1 đã có dòng chính) → chặn", u7);
            // (4) khoá: thêm / sửa đều khoá đúng tên, trong giao dịch, nhả xong
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "P3", StepName = "moi", StandardTime = 1 }, 1, "them P3");
            var p3 = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && x.NC == "P3");
            await pkg.UpdateAsync(p3.StepId, new PartPackagingStep { NC = "P4", StepName = "moi", StandardTime = 2 }, 1, "doi P4");
            Ok(string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == lk + "," + lk && db.Database.CurrentTransaction == null && p3.NC == "P4",
               $"Thêm + sửa: khoá {lk} trong giao dịch (2 lần), giao dịch đóng sau khi lưu", string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            // (5) người khác vừa lưu P5 đúng lúc mình chờ khoá → mình phải thấy (kiểm SAU khoá)
            bool fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => { if (!fired && n == lk) { fired = true; db.PartPackagingSteps.List.Add(new PartPackagingStep { StepId = 9002, PartId = pid, StepOrder = 98, NC = "P5", StepName = "nguoi khac", StandardTime = 1, IsActive = true, CreatedBy = 2, CreatedAt = DateTime.Now }); db.Fixup(); } };
            var r1 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P5", StepName = "minh", StandardTime = 1 }, 1, "them P5"));
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(fired && r1 != null && r1.StartsWith("PartStepCodeException") && !db.PartPackagingSteps.List.Any(x => x.PartId == pid && x.NC == "P5" && x.StepName == "minh"),
               "2 người cùng thêm P5: người kia lưu xong ngay lúc mình chờ khoá → mình bị chặn (kiểm đọc DB SAU khoá)", r1);
            // (5b) cùng kiểu cho SỬA (Gia công) và THÊM (Bavia): người khác lưu mã đích đúng lúc mình chờ khoá (rà độc lập K3)
            var nc3 = db.PartMachiningSteps.List.Single(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "NC03");
            fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => { if (!fired && n == lk) { fired = true; db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 9003, PartId = pid, StepOrder = 97, NC = "NC77", MachineRegistered = "M7", IsActive = true, CreatedBy = 2, CreatedAt = DateTime.Now }); db.Fixup(); } };
            var r1b = await Err(() => gc.UpdateAsync(nc3.StepId, new PartMachiningStep { NC = "NC77", MachineRegistered = nc3.MachineRegistered, SetupTime = nc3.SetupTime }, 1, "doi NC77"));
            fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => { if (!fired && n == lk) { fired = true; db.PartBaviaSteps.List.Add(new PartBaviaStep { StepId = 9004, PartId = pid, StepOrder = 96, NC = "B7", StepName = "nguoi khac", StandardTime = 1, IsActive = true, CreatedBy = 2, CreatedAt = DateTime.Now }); db.Fixup(); } };
            var r1c = await Err(() => bav.AddAsync(pid, new PartBaviaStep { NC = "B7", StepName = "minh", StandardTime = 1 }, 1, "them B7"));
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(r1b != null && r1b.StartsWith("PartStepCodeException") && nc3.NC == "NC03" && r1c != null && r1c.StartsWith("PartStepCodeException") && db.Database.CurrentTransaction == null,
               "Như trên cho SỬA Gia công (NC03 → NC77 vừa có) và THÊM Bavia B7: đều bị chặn, giao dịch đóng", $"{r1b} | {r1c}");
            // (5c) thao tác ngẫu nhiên của nang (ChuoiHelp.MutateAsync kiểu 8: thêm KCS K9 / ĐG P9): lần lặp lại trên cùng Part PHẢI ra "bị chặn" (rà độc lập K2)
            var rm = new Random(5); var got = new List<string>();
            for (int k = 0; k < 8 && !got.Any(g => g.StartsWith("bị chặn")); k++) got.Add(await ChuoiHelp.MutateAsync(db, rm, pid, 8));
            Ok(got.Any(g => g.StartsWith("bị chặn")) && db.PartPackagingSteps.List.Count(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "P9") <= 1 && db.PartInspectionSteps.List.Count(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "K9") <= 1,
               "Thao tác ngẫu nhiên pmedit (thêm K9 / P9) lặp lại: lần 2 ra \"bị chặn\", Part chỉ 1 dòng K9 / P9", string.Join(" · ", got));
            // (6) không lấy được khoá → không lưu, báo thử lại
            Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = n => n == lk ? -1 : 0;
            var r2 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P6", StepName = "moi", StandardTime = 1 }, 1, "them P6"));
            Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = null;
            Ok(r2 != null && r2.Contains("Đang có người khác lưu quy trình") && !db.PartPackagingSteps.List.Any(x => x.PartId == pid && x.NC == "P6") && db.Database.CurrentTransaction == null,
               "Khoá đang bị giữ quá 10 giây → không lưu, báo chờ rồi lưu lại, giao dịch đóng", r2);
            // (7) có giao dịch ngoài → dùng giao dịch đó (không mở lồng), khoá nhả khi giao dịch ngoài kết thúc
            var outer = await db.Database.BeginTransactionAsync();
            var r3 = await Err(() => pkg.AddAsync(pid, new PartPackagingStep { NC = "P7", StepName = "moi", StandardTime = 1 }, 1, "them P7"));
            var still = db.Database.CurrentTransaction != null;
            await outer.RollbackAsync();
            Ok(r3 == null && still && !db.PartPackagingSteps.List.Any(x => x.PartId == pid && x.NC == "P7"), "Đang có giao dịch ngoài: thêm P7 chạy trong giao dịch đó; giao dịch ngoài Rollback → P7 không còn", r3);
            // (8) Taro chưa chặn (khách chốt 02/11 giữ mã CV chung nhiều NC — gói TARO-NC-MA)
            var r4 = await Err(() => taro.AddAsync(pid, new PartTaroStep { NC = "T9", WtsTaskCode = "3T", StepName = "Taro NC khac", StandardTime = 1 }, 1, "taro chung ma"));
            Ok(r4 == null, "Taro: thêm dòng dùng chung mã CV 3T (NC khác) vẫn lưu — chờ gói TARO-NC-MA, không chặn ở service", r4);
        }

        Console.WriteLine("[C17] [PM-NC-DUP-THAP] T2 đổi dự phòng ↔ chính có lưu + ChangeLog; T5 xoá dòng dưới khoá MES_PM_STEPS_<Part>");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-017", Full());
            var lk = "MES_PM_STEPS_" + pid;
            var pkg = new PartPackagingService(db); var gc = new PartMachiningService(db); var bav = new PartBaviaService(db);
            var rua = new PartWashingService(db); var kcs = new PartInspectionService(db);
            async Task<string?> Err(Func<Task> a) { try { await a(); return null; } catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; } }
            // T2: chỉ đổi IsBackup (chính → dự phòng) — trước: changedCount = 0 → không lưu
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "P8", StepName = "doi", StandardTime = 1 }, 1, "them P8");
            var p8 = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && x.NC == "P8");
            var nLog0 = db.PartProcessStepChangeLogs.List.Count(l => l.StepId == p8.StepId);
            var t2a = await Err(() => pkg.UpdateAsync(p8.StepId, new PartPackagingStep { NC = "P8", IsBackup = true, ParentNC = "P1", StepName = "doi", StandardTime = 1 }, 1, "thanh du phong"));
            var logs = db.PartProcessStepChangeLogs.List.Where(l => l.StepId == p8.StepId).Skip(nLog0).ToList();
            Ok(t2a == null && p8.IsBackup && p8.ParentNC == "P1" && logs.Any(l => l.FieldName == "IsBackup" && l.OldValue == "0" && l.NewValue == "1"),
               "T2 Đóng gói: chỉ đổi dòng chính P8 → dự phòng của P1 (giờ, tên giữ nguyên) → lưu được, ChangeLog ghi IsBackup 0 → 1", $"{t2a} · IsBackup={p8.IsBackup} ParentNC={p8.ParentNC} · log={string.Join(",", logs.Select(l => l.FieldName + ":" + l.OldValue + "→" + l.NewValue))}");
            var t2b = await Err(() => pkg.UpdateAsync(p8.StepId, new PartPackagingStep { NC = "P8", IsBackup = false, ParentNC = "P1", StepName = "doi", StandardTime = 1 }, 1, "lai thanh chinh"));
            Ok(t2b == null && !p8.IsBackup, "T2: dự phòng P8 → lại dòng chính (P8 chưa có dòng chính khác) → lưu được", t2b);
            // T3: dòng chính có dự phòng con → không cho đổi thành dự phòng (con mồ côi)
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "PC", StepName = "chinh", StandardTime = 1 }, 1, "them PC");
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "PC", IsBackup = true, ParentNC = "PC", StepName = "dp PC", StandardTime = 1 }, 1, "them dp PC");
            var pc = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "PC");
            var t3 = await Err(() => pkg.UpdateAsync(pc.StepId, new PartPackagingStep { NC = "PC", IsBackup = true, ParentNC = "P1", StepName = "chinh", StandardTime = 1 }, 1, "thanh du phong"));
            Ok(t3 != null && t3.StartsWith("PartStepCodeException") && t3.Contains("đang có dòng dự phòng") && !pc.IsBackup && db.Database.CurrentTransaction == null,
               "T3: dòng chính PC còn dự phòng con → đổi thành dự phòng bị chặn, dòng giữ nguyên", t3);
            var nLogAll = db.PartProcessStepChangeLogs.List.Count;
            var t2c = await Err(() => kcs.UpdateAsync(db.PartInspectionSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup).StepId,
                new PartInspectionStep { NC = db.PartInspectionSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup).NC, IsBackup = false,
                    StepName = db.PartInspectionSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup).StepName,
                    StandardTime = db.PartInspectionSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup).StandardTime }, 1, "khong doi"));
            Ok(t2c == null && db.PartProcessStepChangeLogs.List.Count == nLogAll, "T2: lưu không đổi gì (KCS) → không lỗi, không ghi ChangeLog", t2c);
            // T5: xoá dòng của mỗi bảng đều khoá đúng tên, trong giao dịch, nhả xong; dự phòng đi theo vẫn xoá
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "P9", StepName = "xoa", StandardTime = 1 }, 1, "them P9");
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "P9", IsBackup = true, ParentNC = "P9", StepName = "dp P9", StandardTime = 1 }, 1, "them dp P9");
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var p9 = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "P9");
            var d1 = await Err(() => pkg.SoftDeleteAsync(p9.StepId, 1, "xoa P9"));
            Ok(d1 == null && !db.PartPackagingSteps.List.Any(x => x.PartId == pid && x.NC == "P9") && string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == lk && db.Database.CurrentTransaction == null,
               $"T5 Đóng gói: xoá P9 (kéo theo dự phòng P9) khoá {lk} 1 lần, giao dịch đóng", $"{d1} · khoá={string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames)}");
            var idGc = db.PartMachiningSteps.List.Last(x => x.PartId == pid && x.IsActive && !x.IsBackup).StepId;
            var idBv = db.PartBaviaSteps.List.Last(x => x.PartId == pid && x.IsActive).StepId;
            var idRu = db.PartWashingSteps.List.Last(x => x.PartId == pid && x.IsActive).StepId;
            var idKc = db.PartInspectionSteps.List.Last(x => x.PartId == pid && x.IsActive).StepId;
            var del = new List<(string ten, string tb, long id, Func<Task> f, Func<bool> con)>
            {
                ("Gia công", ProcessStepTable.Machining, idGc, () => gc.SoftDeleteAsync(idGc, 1, "xoa"), () => db.PartMachiningSteps.List.Any(x => x.StepId == idGc)),
                ("Bavia", ProcessStepTable.Bavia, idBv, () => bav.SoftDeleteAsync(idBv, 1, "xoa"), () => db.PartBaviaSteps.List.Any(x => x.StepId == idBv)),
                ("Rửa", ProcessStepTable.Washing, idRu, () => rua.SoftDeleteAsync(idRu, 1, "xoa"), () => db.PartWashingSteps.List.Any(x => x.StepId == idRu)),
                ("KCS", ProcessStepTable.Inspection, idKc, () => kcs.SoftDeleteAsync(idKc, 1, "xoa"), () => db.PartInspectionSteps.List.Any(x => x.StepId == idKc)),
            };
            foreach (var (ten, tb, id, f, con) in del)
            {
                Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
                var de = await Err(f);
                var logDel = db.PartProcessStepChangeLogs.List.Any(l => l.StepTable == tb && l.StepId == id && l.FieldName == ProcessStepAction.Deleted && !string.IsNullOrEmpty(l.OldValue));
                Ok(de == null && !con() && logDel && string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == lk && db.Database.CurrentTransaction == null,
                   $"T5 {ten}: xoá dòng khoá {lk}, dòng mất khỏi bảng, ChangeLog có ảnh chụp dòng xoá, giao dịch đóng", $"{de} · còn={con()} · log={logDel} · khoá={string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames)}");
            }
            // T5: khoá đang bị giữ → không xoá, báo thử lại
            var pk1 = db.PartPackagingSteps.List.First(x => x.PartId == pid && x.IsActive && !x.IsBackup);
            Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = n => n == lk ? -1 : 0;
            var d2 = await Err(() => pkg.SoftDeleteAsync(pk1.StepId, 1, "xoa khi bi giu"));
            Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = null;
            Ok(d2 != null && d2.Contains("Đang có người khác lưu quy trình") && db.PartPackagingSteps.List.Any(x => x.StepId == pk1.StepId) && db.Database.CurrentTransaction == null,
               "T5: khoá quy trình đang bị giữ → không xoá, báo chờ rồi lưu lại, giao dịch đóng", d2);
            // T5: người khác đổi tên dòng chính đúng lúc mình chờ khoá xoá → mình xoá theo tên MỚI (dự phòng trỏ tên mới cũng đi theo, không mồ côi)
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "PA", StepName = "chinh", StandardTime = 1 }, 1, "them PA");
            await pkg.AddAsync(pid, new PartPackagingStep { NC = "PA", IsBackup = true, ParentNC = "PA", StepName = "dp", StandardTime = 1 }, 1, "them dp PA");
            var pa = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && !x.IsBackup && x.NC == "PA");
            var pad = db.PartPackagingSteps.List.Single(x => x.PartId == pid && x.IsActive && x.IsBackup && x.ParentNC == "PA");
            bool fired = false;
            // Giả "đọc lại DB": người khác lưu → thay bằng object MỚI mang tên PB (bản cũ trong tay mình không đổi) — chỉ code đọc dòng SAU khoá mới thấy PB
            //   (EF thật: dòng đang theo dõi phải ReloadAsync — stub không mô phỏng được theo dõi, xem DanhMucKiem PM-NC-DUP-THAP)
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => { if (!fired && n == lk) { fired = true;
                db.PartPackagingSteps.List.Remove(pa); db.PartPackagingSteps.List.Remove(pad);
                db.PartPackagingSteps.List.Add(new PartPackagingStep { StepId = pa.StepId, PartId = pid, StepOrder = pa.StepOrder, NC = "PB", StepName = pa.StepName, StandardTime = pa.StandardTime, IsActive = true, CreatedBy = 2, CreatedAt = DateTime.Now });
                db.PartPackagingSteps.List.Add(new PartPackagingStep { StepId = pad.StepId, PartId = pid, StepOrder = pad.StepOrder, NC = "PB", IsBackup = true, ParentNC = "PB", StepName = pad.StepName, StandardTime = pad.StandardTime, IsActive = true, CreatedBy = 2, CreatedAt = DateTime.Now });
                db.Fixup(); } };
            var d3 = await Err(() => pkg.SoftDeleteAsync(pa.StepId, 1, "xoa PA"));
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(fired && d3 == null && !db.PartPackagingSteps.List.Any(x => x.StepId == pa.StepId || x.StepId == pad.StepId),
               "T5: người khác đổi PA → PB (cả dự phòng) đúng lúc mình chờ khoá xoá → xoá theo dữ liệu SAU khoá: dòng chính + dự phòng PB đều xoá, không mồ côi", d3);
        }

        Console.WriteLine($"\nKẾT QUẢ: {(fails == 0 ? "ĐẠT hết" : fails + " SAI")}");
        return fails == 0 ? 0 : 1;
    }
}
