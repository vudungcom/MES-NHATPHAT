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

        Console.WriteLine($"\nKẾT QUẢ: {(fails == 0 ? "ĐẠT hết" : fails + " SAI")}");
        return fails == 0 ? 0 : 1;
    }
}
