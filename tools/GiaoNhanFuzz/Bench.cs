using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
public static class Bench
{
    public static async Task Run()
    {
        // 1 PO lớn: 200 chiếc, nhiều phiếu + WTS nhỏ lẻ (giống PO chạy nhiều ngày)
        var db = new AppDbContext();
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" }; db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH", Customer = cust, CustomerId = 1, ApplyWorkflow = true }; db.KhPlans.List.Add(plan);
        db.KhPlanDetails.List.Add(new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO", Quantity = 200 });
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 200, NgayNhanPhoi = DateTime.Now, SoPO = "PO", PartNo = "P" });
        for (int i = 0; i < 4; i++) db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = i + 1, KhPlanDetailId = 1, StepOrder = i + 1, NC = "NC" + (i + 1), MachineRegistered = "M" + i });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 2, NC = "B1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        db.Fixup();
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        int wid = 0;
        void W(string pg, string? nc, string? code, decimal q) { db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = ++wid, KhPlanDetailId = 1, ProcessGroup = pg, NC = nc, WtsCode = code, WorkerId = 2, QtyDone = q, QtyOk = q, QtyNg = 0, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now }); db.Fixup(); }
        long tx = 0;
        for (int lot = 0; lot < 20; lot++)   // 20 lô x 10 chiếc đi hết dây chuyền
        {
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI"); tx++; await svc.ReceiveAsync(tx, 10, 0, null, 2);
            for (int n = 1; n <= 4; n++) { W("GC", "NC" + n, null, 5); W("GC", "NC" + n, null, 5); }
            await svc.IssueByCellAsync(1, "GC", "HTSP", 10, 2, "MOI", false, "MOI"); tx++; await svc.ReceiveAsync(tx, 10, 0, null, 3);
            W("TARO", null, "T1", 10); W("BAVIA", null, "B1", 10);
            await svc.IssueByCellAsync(1, "HTSP", "KCS", 10, 3, "MOI", false, "MOI"); tx++; await svc.ReceiveAsync(tx, 10, 0, null, 4);
            W("KCS", null, "K1", 10);
            await svc.IssueByCellAsync(1, "KCS", "PKG", 10, 4, "MOI", false, "MOI"); tx++; await svc.ReceiveAsync(tx, 10, 0, null, 5);
            W("PKG", null, "P1", 10);
        }
        Console.WriteLine($"PO: {db.HandoverTransactions.List.Count} phiếu, {db.HandoverReceives.List.Count} lần nhận, {db.WtsProductionLogs.List.Count} dòng WTS");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n2 = 200;
        for (int i = 0; i < n2; i++) await led.GetLedgerForDetailAsync(1);
        sw.Stop();
        Console.WriteLine($"Tính sổ 1 PO: {sw.Elapsed.TotalMilliseconds / n2:0.00} ms / lần (máy sandbox 2 nhân, dữ liệu trong bộ nhớ)");
        var excl = db.WtsProductionLogs.List.Select(w => w.WtsLogId).Take(1).ToList();
        sw.Restart();
        for (int i = 0; i < n2; i++) await led.GetLedgerForDetailAsync(1, excl);
        Console.WriteLine($"Tính sổ thử bỏ 1 chứng từ: {sw.Elapsed.TotalMilliseconds / n2:0.00} ms / lần");
        // [GN-WHY] PO lớn: huỷ lần nhận lô đầu (Kho→GC) khi cả 20 lô đã đi hết dây chuyền
        var input = (await led.GetInputForDetailAsync(1))!;
        foreach (var rid in new long[] { 1, 41, 79 })
        {
            sw.Restart();
            var wp = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid), new HashSet<string> { "R:" + rid });
            Console.WriteLine($"Tìm gốc rễ huỷ lần nhận #{rid}: {sw.Elapsed.TotalMilliseconds:0} ms · {wp.Computes} lần tính sổ · {wp.Steps.Count} bước · đủ={wp.Complete}");
        }
        var r1 = await svc.VoidReceiveAsync(79, "thu", 1, new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" });
        Console.WriteLine("Lời báo thật (lần nhận #79):\n" + r1.Error);
    }
}
