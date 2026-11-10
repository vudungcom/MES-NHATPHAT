// [GÓI 13 2026-11-10] Dữ liệu drive_g13.py (POST /harness/g13-seed) — không trong seed khởi động (drive.py không đổi).
//   KH-G13 (Workflow ON) / PO-G13 10 chiếc: GC NC01 (M1) → NC02 (M2) → NC03 (H01, máy loại trừ CUỐI — Taro ở HTSP như việc thường), Taro 9X ở NC 3
//   (mã 1 NC), KCS K1. Kho nhận 12 phôi, giao Gia công 10 (đã nhận); WTS NC01 10, NC02 4 → chờ NC02 6, Đã làm chưa giao 4; Gia công giao HTSP 2 (đã nhận),
//   WTS Taro 9X 2 → HTSP Đã làm xong 2. Kho còn 2 phôi.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG13
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G13"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-G13", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G13", PurchaseOrder = "PO-G13", Quantity = 12, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H01") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude { KhPlanDetailId = d.KhPlanDetailId, SoMay = "H01", SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "3", WtsTaskCode = "9X", StepName = "Taro 9X", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "9X"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "9X", TaskName = "Taro 9X", DisplayOrder = 59, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 12, SoLuongKeHoach = 12, NgayNhanPhoi = t0, OrderVatLieu = "OVL-G13", ViTriDePhoi = "G13", SoPO = "PO-G13", PartNo = "PART-G13", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g13-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "giao 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "nhận 10");
            foreach (var (nc, q, k) in new[] { ("NC01", 10m, 10), ("NC02", 4m, 20) })
                db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = nc, QtyDone = q, QtyOk = q, QtyNg = 0,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "HTSP", 2, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP 2");
            await Must(svc.ReceiveAsync(Last(), 2, 0, null, admin.UserId), "HTSP nhận 2");
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "TARO", WtsCode = "9X", NC = "9X", QtyDone = 2, QtyOk = 2, QtyNg = 0,
                StartTime = t0.AddMinutes(40), EndTime = t0.AddMinutes(45), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(45) });
            await db.SaveChangesAsync(); db.Fixup();
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G13");
        return new { det.KhPlanDetailId };
    }

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G13");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued }).ToList(),
            reports = db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided).Select(r => new { r.SourceCell, r.NC, r.Origin, r.Qty }).ToList(),
            gc = new { row.G("GC").ChuaLam, row.G("GC").ConGiao, row.G("GC").LoiGiu },
            htsp = new { row.G("HTSP").ChuaLam, row.G("HTSP").ConGiao },
            kho = row.G("KHO").ConGiao,
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
