// [GÓI 25 19/11/2026] HarnessG25 — dữ liệu cho drive_g25.py ([L7-WTS-CU] Admin / Sửa WTS_MASTER sửa-xoá WTS ngày cũ / của người khác).
//   POST /harness/g25-seed → KH-G25 / PO-G25 (Workflow ON, 20 phôi Kho → GC NC01 đã nhận) + 2 dòng WTS của gc01 ghi thẳng DB (ngày cũ không lưu được qua service):
//                            Y1 = NC01 OK 5 HÔM QUA 08:00–09:00 (M1); T1 = NC01 OK 3 HÔM NAY 00:30–01:00 (M1).
//   GET  /harness/g25      → { gc01 = UserId, admin = UserId, det, y1, t1, wts[] (dòng PO-G25: id, workerId, start, qtyOk, isVoided, voidReason, voidedBy) }.
//   Bản cũ (7439e3e): trang không có ô "Xem WTS của", nút Sửa / Xoá ẩn ngày cũ, máy chủ chặn Admin → các bước D1 / D2 / E1 SAI là đúng.
//   Nguồn: CLAUDE quét Fable 15/11 (A2-01); HÙNG 15/11 15:32 chốt A → Gói 25.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG25
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G25"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var gc = db.Users.List.First(u => u.Username == "gc01");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddDays(-2);
            var plan = new KhPlan { PlanNo = "KH-G25", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G25", PurchaseOrder = "PO-G25", Quantity = 20, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1", StandardTime = 1 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 20, SoLuongKeHoach = 20, NgayNhanPhoi = t0, OrderVatLieu = "OVL-G25", ViTriDePhoi = "G25", SoPO = "PO-G25", PartNo = "PART-G25", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var svc = new HandoverService(db);
            var i = await svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 20, admin.UserId, "MOI", false, "MOI", toNC: "NC01");
            if (!i.Ok) throw new Exception("g25-seed giao: " + i.Error); db.Fixup();
            var r = await svc.ReceiveAsync(db.HandoverTransactions.List.Max(x => x.HandoverTxId), 20, 0, null, admin.UserId);
            if (!r.Ok) throw new Exception("g25-seed nhận: " + r.Error); db.Fixup();
            var y = DateTime.Today.AddDays(-1);
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", MachineUsed = "M1", WorkerId = gc.UserId, QtyDone = 5, QtyOk = 5, QtyNg = 0,
                StartTime = y.AddHours(8), EndTime = y.AddHours(9), SetupTime_Actual = 5, MachiningTime_Actual = 2, InspectionTime_Actual = 1, ProductionMode = "serial", CreatedAt = y.AddHours(9) });
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", MachineUsed = "M1", WorkerId = gc.UserId, QtyDone = 3, QtyOk = 3, QtyNg = 0,
                StartTime = DateTime.Today.AddMinutes(30), EndTime = DateTime.Today.AddMinutes(60), SetupTime_Actual = 5, MachiningTime_Actual = 2, InspectionTime_Actual = 1, ProductionMode = "serial", CreatedAt = DateTime.Today.AddMinutes(60) });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return State(db);
    }

    public static object State(AppDbContext db)
    {
        var d = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G25");
        var gc = db.Users.List.First(u => u.Username == "gc01"); var admin = db.Users.List.First(u => u.Username == "admin");
        var rows = d == null ? new List<WtsProductionLog>() : db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == d.KhPlanDetailId).OrderBy(w => w.WtsLogId).ToList();
        return new
        {
            ok = d != null, gc01 = gc.UserId, admin = admin.UserId, det = d?.KhPlanDetailId,
            y1 = rows.FirstOrDefault(w => w.StartTime < DateTime.Today && w.WorkerId == gc.UserId)?.WtsLogId,
            t1 = rows.FirstOrDefault(w => w.StartTime >= DateTime.Today && w.WorkerId == gc.UserId && w.CreatedAt == DateTime.Today.AddMinutes(60))?.WtsLogId,
            wts = rows.Select(w => new { id = w.WtsLogId, w.WorkerId, start = w.StartTime.ToString("yyyy-MM-dd HH:mm"), w.NC, w.MachineUsed, w.QtyOk, w.QtyNg, w.IsVoided, w.VoidReason, w.VoidedBy }).ToList(),
        };
    }
}
