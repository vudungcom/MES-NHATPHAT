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
            }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}

/// <summary>[RS-D10] Giả SqlException (thuộc tính Number) — sandbox không có Microsoft.Data.SqlClient.</summary>
public class FakeSqlException : Exception
{
    public int Number { get; }
    public FakeSqlException(int n) : base(n == 1205 ? "Transaction (Process ID 61) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction." : "SQL error " + n) { Number = n; }
}
