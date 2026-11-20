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
    // [GÓI 25b 20/11/2026] dữ liệu cho drive_g25b.py ([WTS-SUA-NHANH] Sửa nhanh dòng nhóm khác + R-10 ca dòng gốc).
    //   POST /harness/g25b-seed → KH-G25B / PO-G25B (Workflow OFF), ca "Ca G25B" 07:00–16:00 nghỉ trưa 12:00–13:00, dòng WTS KCS K1 của kcs01
    //   HÔM QUA 11:30–13:30 OK 5 (ca G25B, làm thông nghỉ 20 phút) ghi thẳng DB. GET /harness/g25b → { k1, kcs01, admin, caB, wts[], brk[] }.
    //   Bản cũ (a57f112): bấm Sửa dòng KCS ở form Gia công của admin → báo "Dòng này của nhóm KCS…" (không modal) → SAI là đúng.
    public static async Task<object> Seed25bAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G25B"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var kcs = db.Users.List.First(u => u.Username == "kcs01");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddDays(-2);
            var plan = new KhPlan { PlanNo = "KH-G25B", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = false, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G25B", PurchaseOrder = "PO-G25B", Quantity = 20, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1 });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1", StandardTime = 1.5m });
            var ca = new CaLamViec { TenCa = "Ca G25B", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0), IsActive = true, SortOrder = 8 };
            db.CaLamViecs.Add(ca); await db.SaveChangesAsync();
            var ng = new CaLamViec_NghiGiaiLao { CaId = ca.CaId, NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0), GhiChu = "Trưa" };
            db.CaLamViec_NghiGiaiLaos.Add(ng); await db.SaveChangesAsync(); db.Fixup();
            var y = DateTime.Today.AddDays(-1);
            var w = new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "KCS", WtsCode = "K1", WorkerId = kcs.UserId, QtyDone = 5, QtyOk = 5, QtyNg = 0,
                StartTime = y.AddHours(11.5), EndTime = y.AddHours(13.5), ActualTimePerPc = 2, CaId = ca.CaId, CreatedAt = y.AddHours(14) };
            db.WtsProductionLogs.Add(w); await db.SaveChangesAsync(); db.Fixup();
            db.WtsBreakWorkLogs.Add(new WtsBreakWorkLog { WtsLogId = w.WtsLogId, CaId = ca.CaId, NghiId = ng.NghiId, BreakName = "Trưa", BreakStart = y.AddHours(12), BreakEnd = y.AddHours(13), OverlapMin = 60, WorkedMin = 20, CreatedAt = y });
            await db.SaveChangesAsync(); db.Fixup();
            // R25b-01: dòng GC của gc01 hôm qua 11:00–14:00 ca G25B (form thường của admin = Gia công, cùng nhóm → mở form, không modal)
            var gc = db.Users.List.First(u => u.Username == "gc01");
            var wg = new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", MachineUsed = "M1", WorkerId = gc.UserId, QtyDone = 3, QtyOk = 3, QtyNg = 0,
                StartTime = y.AddHours(11), EndTime = y.AddHours(14), SetupTime_Actual = 5, MachiningTime_Actual = 2, InspectionTime_Actual = 1, ProductionMode = "serial", CaId = ca.CaId, CreatedAt = y.AddHours(14) };
            db.WtsProductionLogs.Add(wg); await db.SaveChangesAsync(); db.Fixup();
            db.WtsBreakWorkLogs.Add(new WtsBreakWorkLog { WtsLogId = wg.WtsLogId, CaId = ca.CaId, NghiId = ng.NghiId, BreakName = "Trưa", BreakStart = y.AddHours(12), BreakEnd = y.AddHours(13), OverlapMin = 60, WorkedMin = 0, CreatedAt = y });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return State25b(db);
    }

    public static object State25b(AppDbContext db)
    {
        var d = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G25B");
        var kcs = db.Users.List.First(u => u.Username == "kcs01"); var admin = db.Users.List.First(u => u.Username == "admin");
        var rows = d == null ? new List<WtsProductionLog>() : db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == d.KhPlanDetailId).OrderBy(w => w.WtsLogId).ToList();
        var ids = rows.Select(r => r.WtsLogId).ToList();
        return new
        {
            ok = d != null, gc01 = db.Users.List.First(u => u.Username == "gc01").UserId, kcs01 = kcs.UserId, admin = admin.UserId, det = d?.KhPlanDetailId,
            caB = db.CaLamViecs.List.FirstOrDefault(c => c.TenCa == "Ca G25B")?.CaId,
            k1 = rows.FirstOrDefault(w => w.ProcessGroup == "KCS")?.WtsLogId,
            g1 = rows.FirstOrDefault(w => w.ProcessGroup == "GC")?.WtsLogId,
            wts = rows.Select(w => new { id = w.WtsLogId, w.WorkerId, w.ProcessGroup, w.WtsCode, start = w.StartTime.ToString("yyyy-MM-dd HH:mm"), end = w.EndTime.ToString("yyyy-MM-dd HH:mm"), w.QtyOk, w.QtyNg, w.CaId, w.ActualTimePerPc, w.IsVoided, w.VoidReason, w.VoidedBy }).ToList(),
            brk = db.WtsBreakWorkLogs.List.Where(b => b.WtsLogId != null && ids.Contains(b.WtsLogId.Value)).Select(b => new { b.WtsLogId, b.CaId, b.OverlapMin, b.WorkedMin }).ToList(),
        };
    }
}
