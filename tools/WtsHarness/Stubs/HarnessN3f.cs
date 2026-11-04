// [N3-FIRST 2026-11-04] Dữ liệu riêng cho drive_n3f.py (dựng khi drive gọi POST /harness/n3f-seed — không nằm trong seed khởi động,
//   để /harness/dump của drive.py không đổi): KH-N3F / PO-N3F 10 chiếc, quy trình GC NC01 = máy H04 (loại trừ, ĐẦU chuỗi) → NC02 (M1),
//   Kho nhận 10 phôi, Kho → Gia công 8 (không chọn NC) đã nhận đủ — bằng HandoverService thật.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessN3f
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-N3F"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-2);
            var plan = new KhPlan { PlanNo = "KH-N3F", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-N3F", PurchaseOrder = "PO-N3F", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "H04", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude { KhPlanDetailId = d.KhPlanDetailId, SoMay = "H04" });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-N3F", ViTriDePhoi = "N3F", SoPO = "PO-N3F", PartNo = "PART-N3F", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync();
            db.Fixup();
            var svc = new HandoverService(db);
            var g = await svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 8, admin.UserId, "MOI", false, "MOI");
            if (!g.Ok) throw new Exception("n3f-seed giao: " + g.Error);
            var r = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 8, 0, null, admin.UserId);
            if (!r.Ok) throw new Exception("n3f-seed nhận: " + r.Error);
            db.Fixup();
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-N3F");
        return new { det.KhPlanDetailId };
    }

    /// <summary>Phiếu giao nhận của PO-N3F (có FromNC / ToNC — /harness/dump không có 2 cột này).</summary>
    public static object Dump(AppDbContext db)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-N3F");
        if (det == null) return new object[0];
        return db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId)
            .Select(t => new { t.HandoverTxId, t.FromGroupCode, t.ToGroupCode, t.Purpose, t.FromNC, t.ToNC, t.QtyIssued, t.IsVoided }).ToList();
    }
}
