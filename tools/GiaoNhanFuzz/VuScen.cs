// [VULOI 2026-10-28] Kịch bản cố định "vu": vụ lỗi đi qua nhiều nơi — đúng ví dụ Hùng chốt 28/10.
//   PO 60 · Kho → GC 60.
//   Lỗi A (VL1): GC phát hiện 2 lỗi phôi (công đoạn trước) → Kho nhận lỗi, tự xử lý → trả GC.          GC → Kho → GC
//   Lỗi B (VL2): GC phát hiện 3 lỗi phôi → Kho nhận lỗi → gửi HTSP sửa → HTSP trả GC.               GC → Kho → HTSP → GC
//   Lỗi C (VL3): GC NC2 làm hỏng 3 (WTS) → 2 gửi HTSP sửa → trả GC, 1 huỷ  (tách nhánh, vẫn 1 dòng).
//   Lỗi D (VL4): HTSP nhận Hàng mới, đếm 1 NG lúc nhận → phiếu trả tự lập → GC nhận → trả khách.
//   Thêm: K5 theo vụ (xoá WTS gốc / huỷ lần nhận gốc khi vụ đã đi tiếp → chặn), huỷ phiếu huỷ hàng → vụ về lại,
//         chọn vụ không có hàng → chặn, loại lỗi bỏ trống / đã ngưng → chặn, hàng lỗi cũ chưa có mã vẫn giao được.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class VuScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static AppDbContext Seed()
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : null, GroupId = u == 1 ? 1 : null });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-VU", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO-VU", Quantity = 60, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 60, NgayNhanPhoi = DateTime.Now, SoPO = "PO-VU", PartNo = "P" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);   // 1 NUT · 2 XUOC · 3 SKT · 4 BAVIA · 5 MOP · 6 CU (đã ngưng)
        db.Fixup();
        return db;
    }

    static long LastTx(AppDbContext db) => db.HandoverTransactions.List.Max(t => t.HandoverTxId);
    static string D(Dictionary<string, decimal> d) => string.Join(", ", d.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value:0.##}"));
    static string Info(LedgerCase c) => $"{c.No} {c.CatLabel}: sinh {c.SinhRa} · nhận lỗi [{D(c.NhanLoi)}] · sửa [{D(c.NhanSua)}] · đã xử lý [{D(c.DaXuLy)}] · huỷ {c.Huy} · khách {c.TraKhach} · giữ [{D(c.Held)}] · chuyển {c.Transit} · tự xử lý [{string.Join(",", c.TuXuLy)}]";

    public static async Task<int> Run()
    {
        var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        var All = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        async Task<LedgerRow> Row() => (await led.GetLedgerForDetailAsync(1))!;
        async Task Can(string when)
        {
            var r = await Row();
            var bad = r.Cases.Values.Where(c => c.Lech != 0).Select(Info).ToList();
            foreach (var g in r.Groups.Values)
            {
                if (r.Cases.Values.Sum(c => c.HeldAt(g.Code, HandoverCell.Loi)) != g.LoiGiu) bad.Add($"{g.Code} ô Hàng lỗi ≠ Σ vụ");
                if (r.Cases.Values.Sum(c => c.HeldAt(g.Code, HandoverCell.Sua)) != g.SuaGiu) bad.Add($"{g.Code} ô Hàng sửa ≠ Σ vụ");
            }
            Ok(!bad.Any() && r.Balanced && !HandoverLedgerService.NegativeCells(r).Any(), $"   cân từng vụ + khớp sổ ({when})", string.Join("\n", bad));
        }

        Console.WriteLine("[V0] Kho giao GC 60, GC nhận đủ");
        await svc.IssueByCellAsync(1, "KHO", "GC", 60, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 60, 0, null, 2);
        Ok((await Row()).Cases.Count == 0, "Chưa có vụ lỗi nào");

        Console.WriteLine("[V1] Lỗi A: GC phát hiện 2 phôi nứt ở NC1 → Kho nhận lỗi → Kho tự xử lý → GC nhận Hàng lỗi đã xử lý");
        {
            var noCat = await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", true, "LOI", "NC1");
            Ok(!noCat.Ok && noCat.Error == NgCaseService.MsgPickCategory, "Không chọn loại lỗi: chặn", noCat.Error);
            var oldCat = await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", true, "LOI", "NC1", ngCategoryId: 6);
            Ok(!oldCat.Ok, "Loại lỗi đã ngưng dùng: chặn", oldCat.Error);
            var r = await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", true, "LOI", "NC1", ngCategoryId: 1);
            var t = db.HandoverTransactions.List.Last();
            Ok(r.Ok && t.NgCaseId == 1 && t.NgReason == "NUT · Phôi nứt", "Lập phiếu: sinh VL000001, lỗi ghi 'NUT · Phôi nứt'", r.Error);
            var c = db.NgCases.List.Single(x => x.NgCaseId == 1);
            Ok(c.OriginKind == NgOrigin.TraLoi && c.DetectGroupCode == "GC" && c.DetectNC == "NC1" && c.CauseGroupCode == "KHO", "Nhãn vụ: phát hiện GC NC1, gây lỗi Kho");
            var rk = await svc.ReceiveAsync(LastTx(db), 0, 2, null, 1);
            Ok(rk.Ok && db.HandoverReceives.List.Last().NgCaseId == 1, "Kho nhận ô Hàng lỗi: không chọn lỗi, lần nhận theo vụ phiếu", rk.Error);
            var row = await Row();
            Ok(row.CasesHeldAt("KHO", HandoverCell.Loi).Select(x => x.Id).SequenceEqual(new long[] { 1 }), "Form Giao của Kho: chỉ 1 vụ đang giữ (VL000001) → tự chọn");
            var tu = await svc.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "XL", ngCaseId: 1);
            await svc.ReceiveAsync(LastTx(db), 2, 0, null, 2);
            var a = (await Row()).Cases[1];
            Ok(tu.Ok && a.SinhRa == 2 && a.NhanLoi.GetValueOrDefault("KHO") == 2 && a.TuXuLy.Contains("KHO") && a.DaXuLy.GetValueOrDefault("GC") == 2 && a.HeldTotal == 0 && a.Transit == 0 && a.Lech == 0,
               "Lỗi A: GC → Kho (tự xử lý) → GC: sinh 2 · nhận lỗi Kho 2 · đã xử lý GC 2", Info(a));
            await Can("A");
        }

        Console.WriteLine("[V2] Lỗi B: GC phát hiện 3 → Kho nhận lỗi → gửi HTSP sửa → HTSP trả GC");
        {
            await svc.IssueByCellAsync(1, "GC", "KHO", 3, 2, "LOI", true, "LOI", "NC1", ngCategoryId: 1);
            await svc.ReceiveAsync(LastTx(db), 0, 3, null, 1);
            var g1 = await svc.IssueByCellAsync(1, "KHO", "HTSP", 3, 1, "LOI", false, "SUA", ngCaseId: 2);
            var mid = (await Row()).Cases[2];
            Ok(g1.Ok && mid.Transit == 3 && mid.HeldTotal == 0, "Đã gửi HTSP chưa nhận: vụ đang chuyển 3", Info(mid));
            await svc.ReceiveAsync(LastTx(db), 3, 0, null, 3);
            var g2 = await svc.IssueByCellAsync(1, "HTSP", "GC", 3, 3, "SUA", false, "XL", ngCaseId: 2);
            await svc.ReceiveAsync(LastTx(db), 3, 0, null, 2);
            var b = (await Row()).Cases[2];
            Ok(g2.Ok && b.NhanLoi.GetValueOrDefault("KHO") == 3 && b.NhanSua.GetValueOrDefault("HTSP") == 3 && b.DaXuLy.GetValueOrDefault("GC") == 3 && !b.TuXuLy.Any() && b.Lech == 0,
               "Lỗi B: GC → Kho → HTSP (sửa) → GC: nhận lỗi Kho 3 · sửa HTSP 3 · đã xử lý GC 3", Info(b));
            await Can("B");
        }

        Console.WriteLine("[V3] Lỗi C: GC NC2 làm hỏng 3 (WTS) → 2 gửi HTSP sửa → trả GC; 1 huỷ — vẫn 1 dòng");
        {
            await VuHelp.Wts(db, "GC", "NC1", null, 50, 0, 2, 2);
            var w = await VuHelp.Wts(db, "GC", "NC2", null, 45, 3, 2, 2);
            Ok(w.NgCaseId == 3 && w.NgReason == "XUOC · Xước", "WTS NG 3 → VL000003 'XUOC · Xước'");
            var c0 = (await Row()).Cases[3];
            Ok(c0.HeldAt("GC", HandoverCell.Loi) == 3 && c0.NhanLoi.GetValueOrDefault("GC") == 3, "GC tự giữ 3 ở ô Hàng lỗi", Info(c0));
            var over = await svc.IssueByCellAsync(1, "GC", "HTSP", 4, 2, "LOI", false, "SUA", ngCaseId: 3);
            Ok(!over.Ok && over.Error.Contains("tối đa 3"), "Gửi 4 > vụ còn 3: chặn", over.Error);
            var wrong = await svc.IssueByCellAsync(1, "GC", "HTSP", 1, 2, "LOI", false, "SUA", ngCaseId: 1);
            Ok(!wrong.Ok && wrong.Error.Contains("tối đa 0"), "Chọn VL000001 (đã xử lý xong, GC không giữ): chặn", wrong.Error);
            var none = await svc.IssueByCellAsync(1, "GC", "HTSP", 1, 2, "LOI", false, "SUA");
            Ok(!none.Ok, "Không chọn vụ (không có hàng lỗi cũ chưa có mã): chặn", none.Error);
            await svc.IssueByCellAsync(1, "GC", "HTSP", 2, 2, "LOI", false, "SUA", ngCaseId: 3);
            await svc.ReceiveAsync(LastTx(db), 2, 0, null, 3);
            await svc.IssueByCellAsync(1, "HTSP", "GC", 2, 3, "SUA", false, "XL", ngCaseId: 3);
            await svc.ReceiveAsync(LastTx(db), 2, 0, null, 2);
            var h = await svc.IssueSpecialAsync(1, "GC", "HUY", 1, 2, HandoverPurpose.Scrap, ngCaseId: 3);
            var c = (await Row()).Cases[3];
            Ok(h.Ok && c.SinhRa == 3 && c.NhanSua.GetValueOrDefault("HTSP") == 2 && c.DaXuLy.GetValueOrDefault("GC") == 2 && c.Huy == 1 && c.HuyBy.GetValueOrDefault("GC") == 1 && c.HeldTotal == 0 && c.Lech == 0,
               "Lỗi C 1 dòng: sinh 3 = đã xử lý GC 2 + huỷ 1", Info(c));
            await Can("C");
        }

        Console.WriteLine("[V4] Lỗi D: GC giao HTSP Hàng mới 10, HTSP đếm 1 móp lúc nhận → phiếu trả tự lập → GC nhận → trả khách");
        {
            await svc.IssueByCellAsync(1, "GC", "HTSP", 10, 2, "MOI", false, "MOI");
            var txD = LastTx(db);
            var noCat = await svc.ReceiveAsync(txD, 9, 1, null, 3);
            Ok(!noCat.Ok && noCat.Error == NgCaseService.MsgPickCategory, "NG lúc nhận không chọn loại lỗi: chặn", noCat.Error);
            var rr = await svc.ReceiveAsync(txD, 9, 1, null, 3, ngCategoryId: 5);
            var rcv = db.HandoverReceives.List.Last(); var back = db.HandoverTransactions.List.Single(x => x.SourceReceiveId == rcv.ReceiveId);
            var nd = db.NgCases.List.Single(x => x.NgCaseId == 4);
            Ok(rr.Ok && rcv.NgCaseId == 4 && back.NgCaseId == 4 && nd.OriginKind == NgOrigin.Nhan && nd.DetectGroupCode == "HTSP" && nd.CauseGroupCode == "GC",
               "Sinh VL000004 (phát hiện HTSP, gây lỗi GC), phiếu trả tự lập mang vụ", rr.Error);
            var dMid = (await Row()).Cases[4];
            Ok(dMid.Transit == 1 && dMid.HeldTotal == 0, "Phiếu trả chưa nhận: vụ đang chuyển 1", Info(dMid));
            await svc.ReceiveAsync(back.HandoverTxId, 0, 1, null, 2);
            var k = await svc.IssueSpecialAsync(1, "GC", "KHACH", 1, 2, HandoverPurpose.CustReturn, ngCaseId: 4);
            var d = (await Row()).Cases[4];
            Ok(k.Ok && d.NhanLoi.GetValueOrDefault("GC") == 1 && d.TraKhach == 1 && d.Lech == 0, "Lỗi D: HTSP → GC → trả khách 1", Info(d));
            await Can("D");
        }

        Console.WriteLine("[V5] K5 theo vụ");
        {
            var r0 = await Row();
            var wC = db.WtsProductionLogs.List.Single(x => x.NgCaseId == 3);
            var after = (await led.GetLedgerForDetailAsync(1, new List<long> { wC.WtsLogId }))!;
            var bad = HandoverLedgerService.NewNegatives(r0, after);
            Ok(bad.Any(b => b.Contains("VL000003")), "Xoá WTS gốc của Lỗi C (đã gửi sửa, đã huỷ): K5 chặn ở VL000003", string.Join("; ", bad));
            var rcvD = db.HandoverReceives.List.Single(x => x.NgCaseId == 4 && x.QtyNg > 0 && x.Transaction.SourceReceiveId == null);
            var v = await svc.VoidReceiveAsync(rcvD.ReceiveId, "nham", 3, All);
            Ok(!v.Ok, "Huỷ lần nhận gốc Lỗi D (GC đã nhận lại + trả khách): chặn", v.Error);
            // gỡ đúng thứ tự: huỷ phiếu trả khách → huỷ lần nhận phiếu trả → huỷ lần nhận gốc: được, vụ D biến mất
            var kD = db.HandoverTransactions.List.Single(x => x.NgCaseId == 4 && x.Purpose == HandoverPurpose.CustReturn);
            var backD = db.HandoverTransactions.List.Single(x => x.SourceReceiveId == rcvD.ReceiveId);
            var s1 = await svc.VoidIssueAsync(kD.HandoverTxId, "nham", 2, All);
            var s2 = await svc.VoidReceiveAsync(backD.Receives.Single(x => !x.IsVoided).ReceiveId, "nham", 2, All);
            var s3 = await svc.VoidReceiveAsync(rcvD.ReceiveId, "nham", 3, All);
            Ok(s1.Ok && s2.Ok && s3.Ok && !(await Row()).Cases.ContainsKey(4), "Gỡ ngược 3 bước: được, VL000004 không còn số", s1.Error + s2.Error + s3.Error);
            var scrapC = db.HandoverTransactions.List.Single(x => x.NgCaseId == 3 && x.Purpose == HandoverPurpose.Scrap);
            var vi = await svc.VoidIssueAsync(scrapC.HandoverTxId, "huy nham", 2, All);
            var c = (await Row()).Cases[3];
            Ok(vi.Ok && c.Huy == 0 && c.HeldAt("GC", HandoverCell.Loi) == 1 && c.Lech == 0, "Huỷ phiếu huỷ hàng Lỗi C: vụ về lại GC 1", vi.Error + " " + Info(c));
            var chg = await NgCaseService.ChangeCaseCategoryAsync(db, 3, 4);
            Ok(chg != null, "Đổi loại lỗi vụ đã đi phiếu: chặn", chg);
            await Can("K5");
        }

        Console.WriteLine("[V6] Hàng lỗi cũ chưa có mã vụ (dữ liệu trước bản này) vẫn giao được");
        {
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC2", WorkerId = 2,
                QtyDone = 1, QtyOk = 0, QtyNg = 1, NgReason = "loi cu", StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            db.Fixup();
            var row = await Row();
            Ok(row.CasesHeldAt("GC", HandoverCell.Loi).Select(x => x.Id).OrderBy(x => x).SequenceEqual(new long[] { 0, 3 }), "GC giữ: VL000003 + hàng cũ chưa có mã");
            var h = await svc.IssueSpecialAsync(1, "GC", "HUY", 1, 2, HandoverPurpose.Scrap);
            Ok(h.Ok && db.HandoverTransactions.List.Last().NgCaseId == null && db.HandoverTransactions.List.Last().NgReason == "Hàng lỗi cũ (chưa có mã vụ)", "Huỷ 1 hàng cũ (không chọn vụ): được", h.Error);
            var h2 = await svc.IssueSpecialAsync(1, "GC", "HUY", 1, 2, HandoverPurpose.Scrap);
            Ok(!h2.Ok, "Hết hàng cũ: không chọn vụ thì chặn", h2.Error);
            await Can("cũ");
        }

        Console.WriteLine("[V7] Đường đi theo từng vụ (bảng trên Kế hoạch ▸ / Giao nhận)");
        {
            var tr = (await led.GetNgTraceAsync(1))!;
            var rows = tr.Cases;
            Ok(rows.Count == 4, $"4 dòng (VL1–VL3 + hàng cũ; VL4 đã gỡ hết chứng từ nên ẩn): {rows.Count}");
            var A = rows.FirstOrDefault(x => x.Id == 1); var B = rows.FirstOrDefault(x => x.Id == 2);
            Ok(A != null && A.PhatHien == "Gia công NC1" && A.NhanLoi == "Kho 2 (tự xử lý)" && A.Sua == "" && A.DaXuLy == "Gia công 2", "Lỗi A: Gia công NC1 → Kho 2 (tự xử lý) → Gia công 2",
               A == null ? "" : $"{A.PhatHien} | {A.NhanLoi} | {A.Sua} | {A.DaXuLy}");
            Ok(B != null && B.NhanLoi == "Kho 3" && B.Sua == "HTSP 3" && B.DaXuLy == "Gia công 3", "Lỗi B: → Kho 3 → HTSP 3 → Gia công 3",
               B == null ? "" : $"{B.PhatHien} | {B.NhanLoi} | {B.Sua} | {B.DaXuLy}");
            var C = rows.FirstOrDefault(x => x.Id == 3);
            Ok(C != null && C.Sua == "HTSP 2" && C.DaXuLy == "Gia công 2" && C.DangGiu == "Gia công 1" && C.Balanced, "Lỗi C (1 dòng): sửa HTSP 2 · đã xử lý GC 2 · đang giữ GC 1",
               C == null ? "" : $"{C.Sua} | {C.DaXuLy} | {C.DangGiu}");
            Ok(rows.All(x => x.Balanced), "Mọi dòng cân");
        }

        // [DS-LOI-LIVE 2026-10-28] Đổi tên loại lỗi trên DS Lỗi → mọi chỗ hiện tên mới; chữ lưu trong DB + số không đổi
        Console.WriteLine("[V8] Đổi tên XUOC 'Xước' → 'Xước cạnh': hiện tên mới khắp nơi, DB + số giữ nguyên");
        {
            var before = await Row();
            var cat = db.NgCategories.List.First(x => x.Code == "XUOC");
            var (sv, se) = await new NgCaseService(db).SaveCategoryAsync(cat.NgCategoryId, "XUOC", "Xước cạnh", cat.Description, cat.SortOrder, 1);
            Ok(sv, "Lưu tên mới trên DS Lỗi", se);
            const string NewLbl = "XUOC · Xước cạnh";
            var w3 = db.WtsProductionLogs.List.First(x => x.NgCaseId == 3 && !x.IsVoided);
            Ok(w3.NgReason == "XUOC · Xước", "Cột NgReason trong DB của WTS VL000003 giữ chữ cũ", w3.NgReason);
            var tr = (await led.GetNgTraceAsync(1))!;
            var c3 = tr.Cases.FirstOrDefault(x => x.Id == 3);
            Ok(c3 != null && c3.Loai == NewLbl, "Bảng theo từng vụ: VL000003 hiện tên mới", c3?.Loai);
            var ev3 = tr.Events.Where(e => e.Reason != null && e.Reason.StartsWith("XUOC")).Select(e => e.Reason).Distinct().ToList();
            Ok(ev3.Count > 0 && ev3.All(x => x == NewLbl), "Bảng từng lần: mọi dòng lỗi XUOC hiện tên mới", string.Join(" | ", ev3));
            var logs = await svc.GetLogsAsync(1);
            var lt = logs.Where(t => t.NgCaseId == 3 && !string.IsNullOrEmpty(t.NgReason)).Select(t => t.NgReason).Distinct().ToList();
            Ok(lt.Count > 0 && lt.All(x => x == NewLbl), "Lịch sử phiếu: phiếu VL000003 hiện tên mới", string.Join(" | ", lt));
            var old = logs.Where(t => t.NgCaseId == null && t.NgReason == "Hàng lỗi cũ (chưa có mã vụ)").ToList();
            Ok(old.Count > 0, "Phiếu hàng lỗi cũ (không mã vụ) giữ chữ cũ");
            // dòng B + chứng từ trong diễn giải ô: không còn chữ tên cũ (chỉ phiếu có in chữ lỗi mới có, nên kiểm "không còn tên cũ")
            var rowNow = await Row();
            var txts = (await led.GetStepsForDetailAsync(1)).Select(s => s.Text)
                .Concat(rowNow.Groups.Values.SelectMany(g => g.Explains.Values).SelectMany(e => e.Terms).SelectMany(t => t.Docs).Select(d => d.Text))
                .Where(x => x != null && x.Contains("XUOC")).Distinct().ToList();
            Ok(!txts.Any(x => x.Replace(NewLbl, "").Contains("XUOC · Xước")), $"Dòng B + diễn giải ô: không còn tên cũ ({txts.Count} dòng có XUOC)", string.Join(" | ", txts.Take(3)));
            var after = await Row();
            Ok(after.Cases.Count == before.Cases.Count && after.Cases.All(kv => before.Cases.TryGetValue(kv.Key, out var b0) && b0.SinhRa == kv.Value.SinhRa && b0.HeldTotal == kv.Value.HeldTotal),
               "Số từng vụ không đổi sau khi đổi tên");
            await Can("đổi tên");
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
