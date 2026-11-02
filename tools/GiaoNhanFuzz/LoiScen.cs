// [GN-NG-TRACE 2026-10-28] Kịch bản cố định "loi": hàng lỗi đi qua nhiều nơi, số phải ra ở từng bước.
//   PO 40 · Kho → GC (2 phôi lỗi đếm lúc nhận, trả Kho) · GC làm hỏng 5 · gửi HTSP sửa 3 → sửa xong trả GC
//   · KCS phát hiện 1 lỗi công đoạn trước trả GC · GC huỷ 2 · Kho trả khách 2.
//   Cuối: phát sinh 8 = đã xử lý 3 + huỷ 2 + trả khách 2 + đang giữ 1 (GC) + đang chuyển 0.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class LoiScen
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
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 40, NgayNhanPhoi = DateTime.Now, SoPO = "PO1", PartNo = "P" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);   // [VULOI] 1 NUT · 2 XUOC · 3 SKT · 4 BAVIA · 5 MOP
        db.Fixup();
        return db;
    }

    // [VULOI] WTS như trang WTS: NG chọn loại lỗi (catId) → tạo vụ lỗi
    static Task<WtsProductionLog> Wts(AppDbContext db, string pg, string code, decimal ok, decimal ng, int catId, int worker)
        => VuHelp.Wts(db, pg, pg == "GC" ? code : null, pg == "GC" ? null : code, ok, ng, worker, catId);

    static long LastTx(AppDbContext db) => db.HandoverTransactions.List.Max(t => t.HandoverTxId);

    public static async Task<int> Run()
    {
        var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        async Task<NgTrace> Tr() => (await led.GetNgTraceAsync(1))!;
        string Info(NgTrace t) => $"phát sinh {t.PhatSinh} · xử lý {t.VeSx} · huỷ {t.DaHuy} · trả khách {t.TraKhach} · giữ {t.DangGiu} · chuyển {t.DangChuyen}"
                                  + (t.Problems.Any() ? "\n" + string.Join("\n", t.Problems) : "");

        Console.WriteLine("[L0] PO mới chưa có lỗi");
        {
            var t = await Tr();
            Ok(!t.HasAny && t.Balanced && !HandoverNgTrace.EverHadNg((await led.GetLedgerForDetailAsync(1))!), "Không có dòng lỗi, lọc 'có phát sinh lỗi' không bắt");
        }

        Console.WriteLine("[L1] Kho giao GC 40, GC nhận 38 đạt + 2 lỗi phôi → phiếu trả tự lập → Kho nhận lại 2");
        {
            await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI");
            var r = await svc.ReceiveAsync(LastTx(db), 38, 2, null, 2, ngCategoryId: 1);   // [VULOI] chọn loại lỗi NUT
            var t = await Tr();
            var e = t.Events.SingleOrDefault(x => x.Type == "NHAN_NG");
            Ok(r.Ok && e != null && e.Qty == 2 && e.DetectedAt == "GC" && e.CausedBy == "KHO" && e.Reason == "NUT · Phôi nứt" && e.Who == "U2",
               "1 dòng NG lúc nhận: 2, phát hiện Gia công, do Kho, lỗi 'NUT · Phôi nứt', người U2", Info(t));
            Ok(t.PhatSinh == 2 && t.DangGiu == 0 && t.DangChuyen == 2 && t.Balanced, "Phiếu trả chưa nhận: đang chuyển 2, đang giữ 0, cân", Info(t));
            var back = db.HandoverTransactions.List.Single(x => x.SourceReceiveId != null);
            var r2 = await svc.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1);   // [VULOI] phiếu trả mang vụ: không chọn lỗi
            t = await Tr();
            Ok(r2.Ok && t.Events.Any(x => x.Type == "NHAN_TRA" && x.Group == "KHO" && x.Qty == 2), "Kho nhận lại 2: có dòng 'Nhận lại hàng lỗi bị trả'", Info(t));
            Ok(t.DangGiu == 2 && t.DangChuyen == 0 && t.Groups.Single(g => g.Code == "KHO").LoiGiu == 2 && t.Balanced, "Kho đang giữ 2 hàng lỗi, đang chuyển 0, cân", Info(t));
        }

        Console.WriteLine("[L2] GC làm NC1 30 OK + 3 NG (xước), NC2 28 OK + 2 NG");
        {
            await Wts(db, "GC", "NC1", 30, 3, 2, 2);   // XUOC
            await Wts(db, "GC", "NC2", 28, 2, 3, 2);   // SKT
            var t = await Tr();
            var ws = t.Events.Where(x => x.Type == "WTS_NG").ToList();
            Ok(ws.Count == 2 && ws[0].Code == "NC1" && ws[0].Qty == 3 && ws[0].Reason == "XUOC · Xước" && ws[1].Code == "NC2" && ws[1].Qty == 2,
               "2 dòng NG (WTS): NC1 3 'XUOC · Xước', NC2 2 'SKT · Sai kích thước'", Info(t));
            var gc = t.Groups.Single(g => g.Code == "GC");
            Ok(t.PhatSinh == 7 && gc.LoiGiu == 5 && gc.PhatHien == 7 && gc.GayRa == 5 && t.Balanced, "Phát sinh 7; GC giữ 5, phát hiện 7 (2 lúc nhận + 5 WTS), gây ra 5", Info(t));
        }

        Console.WriteLine("[L3] GC gửi HTSP nhờ sửa 3 → HTSP nhận ô Hàng sửa → sửa xong trả GC ô Hàng lỗi đã xử lý");
        {
            var vuNc1 = db.WtsProductionLogs.List.First(w => w.NC == "NC1" && w.QtyNg > 0).NgCaseId;   // [VULOI] vụ xước NC1
            var r = await svc.IssueByCellAsync(1, "GC", "HTSP", 3, 2, "LOI", false, "SUA", ngCaseId: vuNc1);
            var t = await Tr();
            Ok(r.Ok && t.DangChuyen == 3 && t.Groups.Single(g => g.Code == "GC").LoiGiu == 2 && t.Balanced, "Đã giao chưa nhận: GC giữ 2, đang chuyển 3", Info(t));
            Ok(t.Events.Last().Type == "GIAO_LOI" && t.Events.Last().Text.Contains("gửi nhờ sửa"), "Dòng 'Giao hàng lỗi (gửi nhờ sửa)'", t.Events.Last().Text);
            await svc.ReceiveAsync(LastTx(db), 3, 0, null, 3);
            t = await Tr();
            Ok(t.Groups.Single(g => g.Code == "HTSP").SuaGiu == 3 && t.DangChuyen == 0 && t.Balanced, "HTSP nhận: HTSP giữ hàng sửa 3", Info(t));
            await svc.IssueByCellAsync(1, "HTSP", "GC", 3, 3, "SUA", false, "XL", ngCaseId: vuNc1);
            await svc.ReceiveAsync(LastTx(db), 3, 0, null, 2);
            t = await Tr();
            var xl = t.Events.LastOrDefault(x => x.Type == "VE_SX");
            Ok(xl != null && xl.Qty == 3 && xl.Group == "GC" && t.VeSx == 3, "GC nhận 3 ô Hàng lỗi đã xử lý: dòng 'đã xử lý' 3", Info(t));
            Ok(t.DangGiu == 4 && t.Balanced, "Đang giữ 4 (Kho 2 + GC 2), cân", Info(t));
            var row = (await led.GetLedgerForDetailAsync(1))!;
            // Chưa GC = nhận 40 + đã xử lý 3 − đã làm 28 − NG 5 − trả lỗi lúc nhận 2 = 8 (5 chờ NC1 + 3 chiếc sửa xong)
            Ok(row.G("GC").ChuaLam == 8 && row.Balanced, $"Sổ: Chưa GC = 8 (thêm 3 chiếc sửa xong), cân PO", $"Chưa GC {row.G("GC").ChuaLam}");
        }

        Console.WriteLine("[L4] GC giao HTSP 28 → HTSP làm 28 → giao KCS 28 → KCS phát hiện 1 lỗi Gia công, trả GC");
        {
            await svc.IssueByCellAsync(1, "GC", "HTSP", 28, 2, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 28, 0, null, 3);
            await Wts(db, "TARO", "T1", 28, 0, 0, 3);
            await svc.IssueByCellAsync(1, "HTSP", "KCS", 28, 3, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 28, 0, null, 4);
            var r = await svc.IssueByCellAsync(1, "KCS", "GC", 1, 4, "LOI", true, "LOI", ngCategoryId: 4);   // [VULOI] BAVIA
            var t = await Tr();
            var e = t.Events.LastOrDefault(x => x.Type == "TRA_TRUOC");
            Ok(r.Ok && e != null && e.DetectedAt == "KCS" && e.CausedBy == "GC" && e.Reason == "BAVIA · Bavia sót" && t.DangChuyen == 1 && t.Balanced,
               "Dòng 'Hàng lỗi công đoạn trước': KCS phát hiện, do Gia công, đang chuyển 1", Info(t) + r.Error);
            await svc.ReceiveAsync(LastTx(db), 0, 1, null, 2);
            t = await Tr();
            Ok(t.PhatSinh == 8 && t.Groups.Single(g => g.Code == "GC").LoiGiu == 3 && t.Groups.Single(g => g.Code == "KCS").PhatHien == 1 && t.Balanced,
               "Phát sinh 8; GC giữ 3; KCS phát hiện 1", Info(t));
        }

        Console.WriteLine("[L5] GC huỷ 2 hàng lỗi, Kho trả khách 2 phôi lỗi");
        {
            var vuNc2 = db.WtsProductionLogs.List.First(w => w.NC == "NC2" && w.QtyNg > 0).NgCaseId;   // [VULOI]
            var vuPhoi = db.HandoverReceives.List.First(x => x.QtyNg > 0).NgCaseId;
            var a = await svc.IssueSpecialAsync(1, "GC", "HUY", 2, 2, HandoverPurpose.Scrap, ngCaseId: vuNc2);
            var b = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 2, 1, HandoverPurpose.CustReturn, ngCaseId: vuPhoi);
            var t = await Tr();
            Ok(a.Ok && b.Ok && t.DaHuy == 2 && t.TraKhach == 2, "Đã huỷ 2, trả khách 2", Info(t) + a.Error + b.Error);
            Ok(t.PhatSinh == 8 && t.VeSx == 3 && t.DangGiu == 1 && t.DangChuyen == 0 && t.Balanced,
               "Cuối: phát sinh 8 = xử lý 3 + huỷ 2 + trả khách 2 + đang giữ 1 + đang chuyển 0", Info(t));
            var gc = t.Groups.Single(g => g.Code == "GC"); var kho = t.Groups.Single(g => g.Code == "KHO");
            Ok(gc.LoiGiu == 1 && gc.DaHuy == 2 && kho.LoiGiu == 0 && kho.TraKhach == 2 && kho.GayRa == 2 && gc.GayRa == 6,
               "Theo nơi: GC giữ 1, huỷ 2, gây ra 6 (5 WTS + 1 KCS trả); Kho trả khách 2, gây ra 2", $"GC {gc.LoiGiu}/{gc.DaHuy}/{gc.GayRa} Kho {kho.LoiGiu}/{kho.TraKhach}/{kho.GayRa}");
            var row = (await led.GetLedgerForDetailAsync(1))!;
            Ok(HandoverNgTrace.EverHadNg(row) && row.Balanced, "Lọc 'có phát sinh lỗi' bắt PO này; cân PO");
            // Lọc theo nơi: mọi dòng có dính Gia công
            Ok(t.Events.Count(x => x.Group == "GC" || x.Other == "GC" || x.DetectedAt == "GC" || x.CausedBy == "GC") >= 8, $"Lọc Gia công ra các lần dính Gia công ({t.Events.Count} dòng tổng)");
        }

        Console.WriteLine("[L6] Huỷ lần nhận hàng sửa xong (GC) → hàng về lại 'đang chuyển', khung lỗi vẫn cân");
        {
            var rcv = db.HandoverReceives.List.Where(x => !x.IsVoided && x.ToCell == HandoverCell.Xl).OrderByDescending(x => x.ReceiveId).FirstOrDefault()
                   ?? db.HandoverReceives.List.Where(x => !x.IsVoided && x.Transaction.FromCell == HandoverCell.Sua).OrderByDescending(x => x.ReceiveId).First();
            var v = await svc.VoidReceiveAsync(rcv.ReceiveId, "nhan nham", 2, new List<string> { "GC" });
            var t = await Tr();
            Ok(!v.Ok || (t.VeSx == 0 && t.DangChuyen == 3 && t.Balanced), v.Ok ? "Huỷ được: đã xử lý 0, đang chuyển 3, cân" : "Bị chặn (đã dùng số) — khung lỗi không đổi", v.Error + "\n" + Info(t));
        }

        // [H10 2026-10-30] Huỷ DÂY CHUYỀN: lần nhận gốc có NG → phiếu trả tự lập đã được bên giao nhận lại.
        //   Trước: chặn "Huỷ lần nhận đó trước". Nay: 1 giao dịch huỷ lần nhận phiếu trả + phiếu trả + lần nhận gốc; quyền cả 2 nhóm; K5 trên kết quả cuối.
        Console.WriteLine("[L7] Huỷ dây chuyền lần nhận gốc + phiếu trả tự lập + lần nhận phiếu trả (H10)");
        {
            var db7 = Seed(); var svc7 = new HandoverService(db7); var led7 = new HandoverLedgerService(db7);
            var all5 = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
            await svc7.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            var t1 = LastTx(db7);
            var sig0 = ChuoiHelp.LedgerSig((await led7.GetLedgerForDetailAsync(1))!);   // sổ lúc phiếu chưa nhận
            await svc7.ReceiveAsync(t1, 8, 2, null, 2, ngCategoryId: 1);
            var r1 = db7.HandoverReceives.List.Last().ReceiveId;
            var back = db7.HandoverTransactions.List.Single(x => x.SourceReceiveId == r1);
            await svc7.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1);
            var r2 = db7.HandoverReceives.List.Last().ReceiveId;
            var row0 = (await led7.GetLedgerForDetailAsync(1))!;
            Ok(row0.G("KHO").LoiGiu == 2 && row0.G("GC").NhanTong == 10 && row0.Balanced, "Chuẩn bị: Gia công nhận 8 + NG 2, Kho đã nhận lại 2 ở phiếu trả tự lập");
            // (a) người Gia công (chỉ quyền GC) huỷ lần nhận gốc: lần nhận phiếu trả do Kho bấm → không đủ quyền, KHÔNG đổi gì
            var a = await svc7.VoidReceiveAsync(r1, "nhan nham", FuzzPerm.GcOnlyUser(db7), new List<string> { "GC" });   // [VOID-PERM] người chỉ Sửa GC trong DB (trước u2 nhóm XUONG — nay máy chủ đọc quyền DB)
            Ok(!a.Ok && a.Error.Contains("không có quyền huỷ lần nhận đó") && db7.HandoverReceives.List.Count(x => x.IsVoided) == 0 && !back.IsVoided,
               "Người chỉ có quyền Gia công: bị chặn (cần quyền Kho cho lần nhận phiếu trả), không chứng từ nào bị huỷ", a.Error);
            // (b) Kho đã huỷ 2 hàng lỗi (phiếu Huỷ) → huỷ dây chuyền làm Kho Tồn hàng lỗi âm → K5 chặn, gợi ý phiếu Huỷ, rollback
            var vuK = row0.CasesHeldAt("KHO", HandoverCell.Loi).First().Id;
            var sc = await svc7.IssueSpecialAsync(1, "KHO", HandoverPurpose.OutTarget(HandoverPurpose.Scrap)!, 2, 1, HandoverPurpose.Scrap, null, null, "huy", null, vuK == 0 ? null : vuK);
            var scId = LastTx(db7);
            var b = await svc7.VoidReceiveAsync(r1, "nhan nham", 1, all5);
            Ok(sc.Ok && !b.Ok && b.Error.StartsWith("Không lưu") && b.Error.Contains($"Phiếu #{scId}") && db7.HandoverReceives.List.Count(x => x.IsVoided) == 0 && !db7.HandoverTransactions.List.Any(x => x.IsVoided),
               "Kho đã huỷ 2 hàng lỗi đó: huỷ dây chuyền bị K5 chặn, lời báo chỉ phiếu Huỷ #" + scId + ", không chứng từ nào bị huỷ", sc.Error + " | " + b.Error);
            var plan = HandoverWhy.Plan((await led7.GetInputForDetailAsync(1))!, HandoverWhy.OpVoidReceive(r1), new HashSet<string> { "R:" + r1, "T:" + back.HandoverTxId, "R:" + r2 });
            Ok(plan.Complete && plan.Steps.Count == 1 && plan.Steps[0].Id == scId, "Gợi ý gỡ đủ: 1 bước = huỷ phiếu Huỷ hàng lỗi", string.Join(",", plan.Steps.Select(s => s.Kind + "#" + s.Id)));
            await svc7.VoidIssueAsync(scId, "huy nham", 1, all5);
            // (c) Admin (đủ quyền) huỷ lần nhận gốc → 1 giao dịch huỷ cả 3, PO về như lúc chưa nhận
            var c = await svc7.VoidReceiveAsync(r1, "nhan nham", 1, all5);
            var row = (await led7.GetLedgerForDetailAsync(1))!;
            var rr1 = db7.HandoverReceives.List.Single(x => x.ReceiveId == r1); var rr2 = db7.HandoverReceives.List.Single(x => x.ReceiveId == r2);
            Ok(c.Ok && rr1.IsVoided && rr2.IsVoided && back.IsVoided && rr2.VoidReason!.StartsWith($"Huỷ theo lần nhận #{r1}") && back.VoidReason!.StartsWith($"Huỷ theo lần nhận #{r1}")
               && db7.HandoverTransactions.List.Single(x => x.HandoverTxId == t1).Status == "PENDING",
               "Đủ quyền: huỷ được 1 lần — lần nhận gốc + phiếu trả + lần nhận phiếu trả đều huỷ, lý do ghi 'Huỷ theo lần nhận #…', phiếu gốc về chờ nhận", c.Error);
            Ok(row.Balanced && HandoverLedgerService.NegativeCells(row).Count == 0 && row.G("KHO").LoiGiu == 0 && row.G("GC").NhanTong == 0 && row.G("GC").ChoNhan == 10,
               "Sổ sau huỷ: cân, không ô âm, Kho không còn hàng lỗi, Gia công chờ nhận 10", $"KHO lỗi {row.G("KHO").LoiGiu} · GC nhận {row.G("GC").NhanTong} chờ {row.G("GC").ChoNhan}");
            var sig1 = ChuoiHelp.LedgerSig(row);
            Ok(System.Text.RegularExpressions.Regex.Replace(sig1, @"\|vu=.*$", "") == System.Text.RegularExpressions.Regex.Replace(sig0, @"\|vu=.*$", "")
               && row.Cases.Values.All(x => x.SinhRa == 0 && x.Transit == 0 && x.HeldTotal == 0),
               "Toàn bộ sổ (mọi nhóm, mọi NC / mã CV, vụ lỗi) giống hệt lúc phiếu chưa nhận", sig0 + "\n" + sig1);
            // (d) Huỷ từ phía phiếu trả tự lập (nút "Huỷ phiếu (huỷ lần nhận gốc)") cũng đi dây chuyền
            await svc7.ReceiveAsync(t1, 9, 1, null, 2, ngCategoryId: 2);
            var r3 = db7.HandoverReceives.List.Last().ReceiveId;
            var back2 = db7.HandoverTransactions.List.Single(x => x.SourceReceiveId == r3 && !x.IsVoided);
            await svc7.ReceiveAsync(back2.HandoverTxId, 0, 1, null, 1);
            var d = await svc7.VoidIssueAsync(back2.HandoverTxId, "tra nham", 2, all5);
            row = (await led7.GetLedgerForDetailAsync(1))!;
            Ok(d.Ok && back2.IsVoided && db7.HandoverReceives.List.Single(x => x.ReceiveId == r3).IsVoided && row.Balanced && HandoverLedgerService.NegativeCells(row).Count == 0,
               "Huỷ phiếu trả tự lập đã được nhận lại (người lập + quyền 2 nhóm) = huỷ dây chuyền, sổ cân", d.Error);
        }

        // [RS-D10 2026-10-30] SQL Server chọn giao dịch làm nạn nhân deadlock (1205) → tự thử lại tối đa 3 lần, không ghi dở / trùng.
        Console.WriteLine("[L8] Lập phiếu / báo lỗi gặp deadlock 1205 → tự thử lại (RS-D10)");
        {
            var db8 = Seed(); var svc8 = new HandoverService(db8); var led8 = new HandoverLedgerService(db8);
            int left = 0; int code = 1205; int skip = 0;   // skip: bỏ qua n lần SaveChanges đầu rồi mới ném (giả deadlock ở lần lưu thứ n+1 trong giao dịch)
            // EF thật: SaveChanges ra InvalidOperationException (execution strategy) bọc DbUpdateException bọc SqlException 1205 — giả đủ 3 lớp
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => skip-- > 0 ? null : left-- > 0
                ? new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
                      new Exception("An error occurred while saving the entity changes.", new FakeSqlException(code))) : null;
            try
            {
                Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear(); left = 2;
                var g = await svc8.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
                var locks = Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Count(x => x == "MES_PO_1");
                Ok(g.Ok && db8.HandoverTransactions.List.Count == 1 && locks == 3 && db8.Database.Current == null && (await led8.GetLedgerForDetailAsync(1))!.Balanced,
                   "Deadlock 2 lần rồi được: lưu đúng 1 phiếu (không trùng), khoá PO lấy lại 3 lần, không treo giao dịch, sổ cân", $"{g.Error} · phiếu {db8.HandoverTransactions.List.Count} · khoá {locks}");
                left = 99;
                var g2 = await svc8.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
                left = 0;
                Ok(!g2.Ok && g2.Error == HandoverService.DeadlockMsg && db8.HandoverTransactions.List.Count == 1 && db8.Database.Current == null,
                   "Deadlock mãi: sau 3 lần báo 'Máy chủ đang bận … chưa lưu gì', không thêm phiếu", g2.Error);
                left = 1;
                var rc = await svc8.ReceiveAsync(LastTx(db8), 10, 0, null, 2);
                Ok(rc.Ok && db8.HandoverReceives.List.Count == 1, "Nhận phiếu gặp deadlock 1 lần: tự thử lại, đúng 1 lần nhận", rc.Error);
                left = 1; skip = 1;   // ném ở lần lưu thứ 2 (sau khi vụ lỗi đã được lưu trong giao dịch)
                var b = await svc8.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauRa, null, "", null, 2, new List<string> { "GC" }, 2);
                Ok(b.Ok && db8.HandoverNgReports.List.Count == 1 && db8.NgCases.List.Count == 1 && (await led8.GetLedgerForDetailAsync(1))!.Balanced,
                   "Báo hàng lỗi sau nhận gặp deadlock 1 lần: tự thử lại, đúng 1 phiếu báo lỗi + 1 vụ (không vụ mồ côi)", $"{b.Error} · báo lỗi {db8.HandoverNgReports.List.Count} · vụ {db8.NgCases.List.Count}");
                left = 1; code = 547;
                Exception? other = null;
                try { await svc8.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); } catch (Exception ex) { other = ex; }
                left = 0;
                Ok(other != null && !HandoverService.IsDeadlock(other) && db8.Database.Current == null, "Lỗi SQL khác (547) không thử lại — ném ra như cũ", other?.Message);
                // [RS-D10c 2026-10-31] khoá PO bị sp_getapplock trả −3 (nạn nhân deadlock) → tự thử lại như 1205 (trước: "PO đang có người khác lưu")
                int dlLeft = 1;
                Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = name => name == "MES_PO_1" && dlLeft-- > 0 ? -3 : 0;
                int nTx = db8.HandoverTransactions.List.Count;
                var g3 = await svc8.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI");
                Ok(g3.Ok && db8.HandoverTransactions.List.Count == nTx + 1 && db8.Database.Current == null, "Khoá PO −3 1 lần: tự thử lại, đúng 1 phiếu", g3.Error);
                dlLeft = 99;
                var g4 = await svc8.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
                Ok(!g4.Ok && g4.Error == HandoverService.DeadlockMsg && db8.HandoverTransactions.List.Count == nTx + 1, "Khoá PO −3 mãi: 'Máy chủ đang bận', không thêm phiếu", g4.Error);
                Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = name => name == "MES_PO_1" ? -1 : 0;
                var g5 = await svc8.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
                Ok(!g5.Ok && g5.Error == "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.", "Hết giờ chờ khoá (−1): lời báo cũ, không thử lại", g5.Error);
                Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = null;
            }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = null; }
        }

        // [WHY-TRAN 2026-10-31] PO nhiều chứng từ (8 lô × 10 chiếc đi hết dây chuyền, mỗi việc chia 5 dòng WTS × 2 chiếc = 32 phiếu, 320 dòng WTS): huỷ lần nhận ĐẦU TIÊN (Kho → GC lô 1)
        //   cần gỡ ~35 chứng từ. Trước (trần 80 lần tính / 1,5 s, 40 bước / tầng): dừng giữa chừng → lời báo "Phần mềm dừng tìm ở đây".
        Console.WriteLine("[L9] PO nhiều chứng từ: gợi ý gỡ huỷ lần nhận đầu tiên tìm đủ (WHY-TRAN)");
        {
            var (db9, svc9, led9) = await Bench.BuildAsync(8, 5);   // 8 lô, mỗi việc chia 5 dòng WTS × 2 chiếc
            var inp = (await led9.GetInputForDetailAsync(1))!;
            var sw9 = System.Diagnostics.Stopwatch.StartNew();
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidReceive(1), new HashSet<string> { "R:1" });
            sw9.Stop();
            Ok(plan.Complete && plan.Steps.Count >= 30, $"Tìm đủ: {plan.Steps.Count} bước · {plan.Computes} lần tính sổ · {sw9.ElapsedMilliseconds} ms", $"đủ={plan.Complete} bước={plan.Steps.Count} tính={plan.Computes} {sw9.ElapsedMilliseconds} ms");
            // làm đúng theo gợi ý trên bản sao: từng bước tự nó không bị K5, cuối cùng huỷ lần nhận #1 lưu được
            var sim = HandoverWhyTest.CloneInput(inp); string? bad = null;
            foreach (var st in plan.Steps)
            {
                var bs = HandoverLedgerService.Compute(sim); var s2 = HandoverWhyTest.CloneInput(sim);
                (st.Kind switch { "WTS" => HandoverWhy.OpWts(new[] { st.Id }), "NHAN" => HandoverWhy.OpVoidReceive(st.Id), "BAOLOI" => HandoverWhy.OpVoidNgRep(st.Id), _ => HandoverWhy.OpVoidIssue(st.Id) })(s2);
                if (HandoverLedgerService.NewNegativeKeys(bs, HandoverLedgerService.Compute(s2)).Any()) { bad = $"bước {st.Kind}#{st.Id} bị K5 chặn"; break; }
                sim = s2;
            }
            if (bad == null)
            {
                var bs = HandoverLedgerService.Compute(sim); var s3 = HandoverWhyTest.CloneInput(sim); HandoverWhy.OpVoidReceive(1)(s3);
                if (HandoverLedgerService.NewNegativeKeys(bs, HandoverLedgerService.Compute(s3)).Any()) bad = "làm hết gợi ý vẫn bị K5 chặn";
            }
            Ok(bad == null, "Làm đúng theo gợi ý (trên bản sao) thì huỷ lần nhận #1 lưu được", bad);
            var v = await svc9.VoidReceiveAsync(1, "nhan nham", 1, new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" });
            Ok(!v.Ok && v.Error.Contains("… và ") && !v.Error.Contains("Phần mềm dừng tìm ở đây"), "Lời báo thật: 8 bước đầu + '… và N chứng từ nữa', không còn 'dừng tìm'", v.Error);
        }

        Console.WriteLine("[L10] Gợi ý gỡ: sau khi Plan bắt đầu (luồng nền, trang mở cho thao tác khác) KHÔNG còn câu DB nào trên DbContext của trang (WHY-TRAN-DB)");
        {
            var (db10, _, _) = await Bench.BuildAsync(3, 2);
            int atPlan = -1;
            var baseOp = HandoverWhy.OpVoidReceive(1);
            Action<LedgerInput> op10 = x =>
            {
                if (atPlan < 0) atPlan = System.Threading.Volatile.Read(ref Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsyncQueries);
                baseOp(x);
            };
            var txt10 = await HandoverWhy.ExplainAsync(db10, 1, op10, new HashSet<string> { "R:1" });
            var after10 = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsyncQueries;
            Ok(atPlan >= 0 && after10 == atPlan, "Từ lúc Plan chạy tới khi trả lời báo: 0 câu DB", $"câu DB lúc Plan bắt đầu = {atPlan}, lúc xong = {after10}");
            Ok(txt10.Contains("nên kiểm tra theo các bước sau") && (txt10.Contains(" nhận — ") || txt10.Contains(" lập — ")),
               "Lời báo vẫn đủ bước + tên người nhận / người lập (đọc sẵn trước Task.Run)", txt10);
        }

        Console.WriteLine("[L11] Huỷ lần nhận bị K5 chặn: trong lúc tìm gốc rễ, thao tác khác mở giao dịch trên cùng DbContext → lúc trả lời báo KHÔNG được gỡ giao dịch của thao tác kia (WHY-TRAN-DB)");
        {
            var (db11, svc11, _) = await Bench.BuildAsync(3, 2);
            Microsoft.EntityFrameworkCore.StubTransaction? other = null; bool armed = false;
            Microsoft.EntityFrameworkCore.StubTransaction.OnRollback = () => armed = true;
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.OnQuery = () =>
            {
                if (armed && other == null) { armed = false; other = db11.Database.BeginTransactionAsync().GetAwaiter().GetResult(); }
            };
            (bool Ok, string Error) v11;
            try { v11 = await svc11.VoidReceiveAsync(1, "nhan nham", 1, new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" }); }
            finally { Microsoft.EntityFrameworkCore.StubTransaction.OnRollback = null; Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.OnQuery = null; }
            Ok(!v11.Ok && v11.Error.Contains("nên kiểm tra theo các bước sau"), "Huỷ lần nhận #1 bị chặn K5 + có gợi ý gỡ", v11.Error);
            Ok(other != null && db11.Database.CurrentTransaction == other, "Giao dịch của thao tác kia vẫn còn sau khi lời báo trả về",
               other == null ? "không giả được thao tác chen vào" : "giao dịch của thao tác kia đã bị gỡ khỏi DbContext (Dispose giao dịch cũ lúc thoát hàm)");
            other?.Dispose();
        }

        // [GN-RECV-NG2 2026-11-02] Nhận vào ô Hàng lỗi (phiếu trả tự lập / phiếu hàng lỗi lập tay / bên nhận chọn ô Hàng lỗi): toàn bộ là hàng lỗi —
        //   không có số "đạt" (OK > 0 bị chặn), lỗi lấy theo phiếu khi không ghi, KHÔNG tự lập phiếu trả lần nữa, sổ không ghi "NG lúc nhận".
        //   Phiếu trả tự lập chỉ nhận vào ô Hàng lỗi. (Luật cũ "phiếu tự lập chặn NG" đã bỏ — nhận NG vào ô Hàng lỗi là đúng.)
        Console.WriteLine("[L12] GN-RECV-NG2 — nhận vào ô Hàng lỗi: không có hàng đạt, lỗi theo phiếu, không tự lập phiếu trả lần 2");
        {
            var dbn = Seed(); var svn = new HandoverService(dbn); var ledn = new HandoverLedgerService(dbn);
            int Backs() => dbn.HandoverTransactions.List.Count(t => t.SourceReceiveId != null);
            string StepText(long receiveId) => HandoverLedgerService.ComputeSteps(ledn.GetInputForDetailAsync(1).GetAwaiter().GetResult()!)
                .FirstOrDefault(x => x.Kind == "NHAN" && x.RefId == receiveId)?.Text ?? "(không thấy bước)";
            await svn.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            var r0 = await svn.ReceiveAsync(LastTx(dbn), 8, 2, null, 2, ngCategoryId: 1);
            var back = dbn.HandoverTransactions.List.Single(t => t.SourceReceiveId != null);
            Ok(r0.Ok && Backs() == 1 && back.NgReason == "NUT · Phôi nứt", "Dựng: GC nhận 8 + NG 2 → 1 phiếu trả tự lập (lỗi 'NUT · Phôi nứt')", r0.Error + " · " + back.NgReason);
            int nRecv = dbn.HandoverReceives.List.Count;
            var a = await svn.ReceiveAsync(back.HandoverTxId, 1, 1, null, 1);
            Ok(!a.Ok && a.Error == "Phiếu hàng lỗi: không có hàng đạt — nhập số nhận lại ở ô Hàng lỗi." && dbn.HandoverReceives.List.Count == nRecv,
               "Kho nhận phiếu trả tự lập có OK 1: chặn đúng lời báo, không ghi", a.Error);
            var b = await svn.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1, toCell: HandoverCell.Moi, toCellReason: "thu");
            Ok(!b.Ok && b.Error == "Phiếu trả hàng lỗi do phần mềm tự lập: chỉ nhận vào ô Hàng lỗi.", "Phiếu trả tự lập chọn ô Hàng mới: chặn", b.Error);
            var c = await svn.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1);
            var rc = dbn.HandoverReceives.List.Last();
            Ok(c.Ok && rc.QtyNg == 2 && rc.QtyOk == 0 && rc.ToCell == HandoverCell.Loi && rc.NgReason == "NUT · Phôi nứt" && Backs() == 1,
               "Kho nhận 0 + NG 2 (không ghi lỗi): lưu, ô Hàng lỗi, lỗi theo phiếu, KHÔNG tự lập phiếu trả lần 2", $"{c.Error} · ô {rc.ToCell} lỗi '{rc.NgReason}' · phiếu trả tự lập {Backs()}");
            var tc = StepText(rc.ReceiveId);
            Ok(!tc.Contains("NG lúc nhận") && tc.Contains("Hàng lỗi"), "Sổ: bước nhận vào ô Hàng lỗi không ghi 'NG lúc nhận'", tc);

            // phiếu hàng lỗi lập tay: GC giao ô Hàng lỗi (WTS NG 2) về Kho
            await Wts(dbn, "GC", "NC1", 3, 2, 2, 2);
            var g = await svn.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", false, "LOI", reason: "xuoc nang", ngCaseId: VuHelp.LastCase(dbn));
            var mt = dbn.HandoverTransactions.List.Last();
            var d = await svn.ReceiveAsync(mt.HandoverTxId, 2, 0, null, 1);
            Ok(g.Ok && !d.Ok && d.Error == "Phiếu hàng lỗi: không có hàng đạt — nhập số nhận lại ở ô Hàng lỗi.", "Phiếu hàng lỗi lập tay, Kho nhập 2 ở ô đạt: chặn", g.Error + " · " + d.Error);
            var e = await svn.ReceiveAsync(mt.HandoverTxId, 0, 2, null, 1);
            var re = dbn.HandoverReceives.List.Last();
            Ok(e.Ok && re.NgReason == mt.NgReason && !string.IsNullOrEmpty(re.NgReason) && Backs() == 1, "Nhận 0 + NG 2: lỗi theo phiếu, không tự lập phiếu trả", $"{e.Error} · '{re.NgReason}' vs phiếu '{mt.NgReason}' · tự lập {Backs()}");
            Ok(!StepText(re.ReceiveId).Contains("NG lúc nhận"), "Sổ: không ghi 'NG lúc nhận'", StepText(re.ReceiveId));

            // phiếu Hàng mới, bên nhận đổi sang ô Hàng lỗi (có lý do): cũng không có hàng đạt, không tự lập phiếu trả
            await svn.IssueByCellAsync(1, "KHO", "GC", 3, 1, "MOI", false, "MOI");
            var nt = LastTx(dbn);
            var f = await svn.ReceiveAsync(nt, 1, 2, null, 2, toCell: HandoverCell.Loi, toCellReason: "phoi nut", ngCategoryId: 1);
            Ok(!f.Ok && f.Error == "Phiếu hàng lỗi: không có hàng đạt — nhập số nhận lại ở ô Hàng lỗi.", "Phiếu Hàng mới, bên nhận chọn ô Hàng lỗi mà có OK 1: chặn", f.Error);
            var h = await svn.ReceiveAsync(nt, 0, 3, null, 2, toCell: HandoverCell.Loi, toCellReason: "phoi nut", ngCategoryId: 1);
            var rh = dbn.HandoverReceives.List.Last();
            var row = (await ledn.GetLedgerForDetailAsync(1))!;
            Ok(h.Ok && rh.ToCell == HandoverCell.Loi && rh.QtyNg == 3 && Backs() == 1 && row.Balanced, "Nhận 0 + NG 3 vào ô Hàng lỗi: lưu, không tự lập phiếu trả, sổ cân", $"{h.Error} · tự lập {Backs()}");
        }

        // [D10-REASON 2026-11-02] Lý do huỷ: cột DB 200 ký tự — dài hơn thì cắt còn 200 (đã Trim), ngắn thì giữ nguyên (đã Trim). Trước: lỗi SQL, trang sập.
        Console.WriteLine("[L13] D10-REASON — lý do huỷ lần nhận / huỷ phiếu dài quá 200 ký tự: cắt còn 200");
        {
            var dbn = Seed(); var svn = new HandoverService(dbn);
            string Long(char ch, int n) => "  " + string.Concat(Enumerable.Range(0, n).Select(i => (char)(ch + i % 20))) + "  ";
            await svn.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            var tx = LastTx(dbn);
            await svn.ReceiveAsync(tx, 10, 0, null, 2);
            var rcv = dbn.HandoverReceives.List.Last();
            var why1 = Long('a', 250);
            var v1 = await svn.VoidReceiveAsync(rcv.ReceiveId, why1, 1, new List<string>());
            rcv = dbn.HandoverReceives.List.Single(x => x.ReceiveId == rcv.ReceiveId);
            Ok(v1.Ok && rcv.IsVoided && rcv.VoidReason == why1.Trim()[..200], "Huỷ lần nhận, lý do 250 ký tự (+ dấu cách 2 đầu): lưu, lý do = 200 ký tự đầu (đã Trim)",
               $"{v1.Error} · dài {rcv.VoidReason?.Length}");
            var why2 = Long('A', 320);
            var v2 = await svn.VoidIssueAsync(tx, why2, 1, new List<string>());
            var t = dbn.HandoverTransactions.List.Single(x => x.HandoverTxId == tx);
            Ok(v2.Ok && t.IsVoided && t.VoidReason == why2.Trim()[..200], "Huỷ phiếu, lý do 320 ký tự: lưu, lý do = 200 ký tự đầu", $"{v2.Error} · dài {t.VoidReason?.Length}");
            await svn.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
            var tx2 = LastTx(dbn);
            var v3 = await svn.VoidIssueAsync(tx2, "  nhap nham so  ", 1, new List<string>());
            var t2 = dbn.HandoverTransactions.List.Single(x => x.HandoverTxId == tx2);
            Ok(v3.Ok && t2.VoidReason == "nhap nham so", "Lý do ngắn: giữ nguyên (đã Trim)", t2.VoidReason);
            var why4 = "  " + new string('x', 200) + "  ";
            await svn.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
            var tx3 = LastTx(dbn);
            var v4 = await svn.VoidIssueAsync(tx3, why4, 1, new List<string>());
            Ok(v4.Ok && dbn.HandoverTransactions.List.Single(x => x.HandoverTxId == tx3).VoidReason == new string('x', 200), "Lý do đúng 200 ký tự (sau Trim): giữ đủ 200");
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}

// [RS-D10c] FakeSqlException dời sang EfStub.cs (harness WtsHarness dùng EfStub mà không có file này)

