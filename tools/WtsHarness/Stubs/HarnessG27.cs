// [GÓI 27 20/11/2026] HarnessG27 — dữ liệu cho drive_g27.py ([PO-DL-NHE] datalist Số PO trang WTS: ô trống 50 PO ưu tiên, gõ chữ tối đa 100).
//   POST /harness/g27-seed → KH-G27 (Workflow OFF → không qua cổng "đã nhận hàng") 130 PO "G27-000…129" Part PART-G27, mỗi PO 1 dòng snapshot NC01
//   (qua cổng "có quy trình" Gia công); PO G27-129 Gấp (phải đứng đầu danh sách). GET /harness/g27 → { n }.
//   Bản cũ (a57f112): datalist đổ đủ mọi PO (> 130 option) → SAI là đúng.
using MES.Web.Data;
using MES.Web.Data.Entities;

public static class HarnessG27
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G27"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddDays(-2);
            var plan = new KhPlan { PlanNo = "KH-G27", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = false, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            for (int i = 0; i < 130; i++)
            {
                var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = i + 1, PartNo = "PART-G27", PurchaseOrder = $"G27-{i:000}", Quantity = 10, Unit = "EA",
                    STD = t0.AddDays(10 + i), CreatedAt = t0, Priority = i == 129 ? "Gấp" : null };
                db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 1, MachiningTime = 1, InspectionTime = 1 });
            }
            await db.SaveChangesAsync(); db.Fixup();
        }
        return State(db);
    }
    public static object State(AppDbContext db) => new { ok = true, n = db.KhPlanDetails.List.Count(d => d.PurchaseOrder.StartsWith("G27-")) };
}
