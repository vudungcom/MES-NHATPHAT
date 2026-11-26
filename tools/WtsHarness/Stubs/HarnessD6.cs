// [D6-LS-VIEC 2026-11-26] Dữ liệu cho drive_d6.py (mockup dòng 6 — Lịch sử phiếu ghi việc theo tên nút mới).
//   POST /harness/d6-seed: KH-D6, PO-D6 10 chiếc, ghi THẲNG các phiếu vào bảng (Lịch sử chỉ đọc phiếu — không cần sổ cân), giờ tăng dần:
//     #1 Kho → Gia công Hàng mới 10 · #2 Gia công → Kho Hàng lỗi công đoạn trước (NC01) · #3 Kho → HTSP gửi nhờ sửa (ô Hàng lỗi → Hàng sửa)
//     #4 HTSP → Gia công "Sửa đạt → giao đến" (ô Hàng lỗi đã xử lý → Hàng lỗi đã xử lý) · #5 HTSP → Kho phần mềm tự trả chiếc sửa chưa đạt (LOIV)
//     #6 HTSP → Gia công chuyển tiếp nhờ sửa (ô Hàng sửa → Hàng sửa) · #7 Kho huỷ · #8 Kho trả khách
//   Trả { ids } = mã phiếu theo thứ tự trên để drive dò từng thẻ.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessD6
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-D6");
        if (det == null)
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-D6", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            det = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "4S-PN1114510", PurchaseOrder = "PO-D6", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(det); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = det.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = det.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M2", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = det.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-D6", ViTriDePhoi = "D6", SoPO = "PO-D6", PartNo = "4S-PN1114510", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            int k = 0;
            void Tx(string from, string to, decimal q, string purpose, string? fromCell, string? toCell, string? fromNc = null, string? toNc = null)
            {
                var at = t0.AddMinutes(10 * ++k);
                db.HandoverTransactions.Add(new HandoverTransaction
                {
                    KhPlanDetailId = det!.KhPlanDetailId, FromGroupCode = from, ToGroupCode = to, QtyIssued = q, Purpose = purpose, FromCell = fromCell, ToCell = toCell,
                    FromNC = fromNc, ToNC = toNc, Status = HandoverPurpose.IsOut(purpose) ? "COMPLETED" : "PENDING", IssuedBy = admin.UserId, IssuedAt = at, CreatedAt = at,
                    IsReturnNg = purpose == HandoverPurpose.NgReturn, Notes = "d6-" + k,
                });
            }
            Tx("KHO", "GC", 10, HandoverPurpose.New, HandoverCell.Moi, HandoverCell.Moi, toNc: "NC01");
            Tx("GC", "KHO", 1, HandoverPurpose.NgReturn, HandoverCell.Loi, HandoverCell.Loi, fromNc: "NC01");
            Tx("KHO", "HTSP", 1, HandoverPurpose.RepairSend, HandoverCell.Loi, HandoverCell.Sua);
            Tx("HTSP", "GC", 1, HandoverPurpose.RepairReturn, HandoverCell.Xl, HandoverCell.Xl);
            Tx("HTSP", "KHO", 1, HandoverPurpose.RepairSend, HandoverCell.Loi, HandoverCell.LoiVe);
            Tx("HTSP", "GC", 1, HandoverPurpose.RepairReturn, HandoverCell.Sua, HandoverCell.Sua);
            Tx("KHO", "HUY", 1, HandoverPurpose.Scrap, HandoverCell.Loi, null);
            Tx("KHO", "KHACH", 1, HandoverPurpose.CustReturn, HandoverCell.Loi, null);
            await db.SaveChangesAsync(); db.Fixup();
        }
        var ids = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId).OrderBy(t => t.HandoverTxId).Select(t => t.HandoverTxId).ToList();
        return new { ok = true, ids };
    }
}
