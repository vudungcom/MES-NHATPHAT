using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class Scen
{
    static AppDbContext Seed()
    {
        var db = new AppDbContext();
        foreach (var (id, n) in new[] { (1, "Kho Lan"), (2, "GC Minh"), (3, "HTSP Hoa"), (4, "KCS Tuan"), (5, "PKG Nga") })
            db.Users.List.Add(new User { UserId = id, Username = "u" + id, FullName = n });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        db.KhPlans.List.Add(plan);
        db.KhPlanDetails.List.Add(new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 10 });
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 10, NgayNhanPhoi = DateTime.Now, SoPO = "PO", PartNo = "P" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 2, NC = "B1" });
        db.Fixup();
        return db;
    }
    static void Wts(AppDbContext db, string pg, string code, decimal ok, int worker)
    {
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = pg,
            NC = pg == "GC" ? code : null, WtsCode = pg == "GC" ? null : code, WorkerId = worker, QtyDone = ok, QtyOk = ok, QtyNg = 0,
            StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
        db.Fixup();
    }
    static readonly List<string> All = new() { "KHO", "GC", "HTSP", "KCS", "PKG" };

    public static async Task Run()
    {
        // TH-A: GC đã nhập WTS NC1 + NC2, giờ Kho huỷ lần nhận / GC huỷ lần nhận phiếu Kho→GC
        {
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "GC", "NC1", 10, 2); Wts(db, "GC", "NC2", 6, 2);
            var r = await svc.VoidReceiveAsync(1, "nhan nham", 2, All);
            Console.WriteLine("TH-A  Huỷ lần nhận Kho→GC sau khi GC đã nhập WTS NC1 10, NC2 6:\n   " + r.Error + "\n");
        }
        // TH-B: HTSP nhận 10, Taro WTS 8, giờ huỷ lần nhận
        {
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "GC", "NC1", 10, 2); Wts(db, "GC", "NC2", 10, 2);
            await svc.IssueByCellAsync(1, "GC", "HTSP", 10, 2, "MOI", false, "MOI");
            await svc.ReceiveAsync(2, 10, 0, null, 3);
            Wts(db, "TARO", "T1", 8, 3);
            var r = await svc.VoidReceiveAsync(2, "nhan nham", 3, All);
            Console.WriteLine("TH-B  Huỷ lần nhận GC→HTSP sau khi Taro nhập WTS 8:\n   " + r.Error + "\n");
        }
        // TH-C: GC đã giao tiếp HTSP (HTSP đã nhận), giờ huỷ lần nhận Kho→GC
        {
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "GC", "NC1", 10, 2); Wts(db, "GC", "NC2", 10, 2);
            await svc.IssueByCellAsync(1, "GC", "HTSP", 10, 2, "MOI", false, "MOI");
            var r = await svc.VoidIssueAsync(1, "giao nham", 1, All);
            Console.WriteLine("TH-C  Kho huỷ phiếu Kho→GC khi GC đã nhận, làm, giao tiếp:\n   " + r.Error + "\n");
        }
    }
}
