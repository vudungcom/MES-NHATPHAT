using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [KHE-5 2026-11-06] Ảnh chụp 1 PO trắng TRƯỚC khi xoá (bảng KhPlanDeletedDetails — SQL 11, Hùng chạy 06/11 17:40).
    /// Xoá PO (KhPlanService.DeleteDetailAsync) phải DELETE lịch sử sửa KhPlanDetailChangeLogs (khoá ngoại không null) → trước đây mất hẳn,
    /// không biết ai xoá / lúc nào / vì sao. Nay chụp JSON dòng PO + mọi dòng lịch sử + người / giờ / lý do, cùng giao dịch với lệnh xoá.
    /// Không khoá ngoại (dòng PO đã xoá). Chỉ ghi thêm, không sửa / xoá. Nguồn: HÙNG 05/11 15:37 chốt A.
    /// </summary>
    public class KhPlanDeletedDetail
    {
        public long DeletedId { get; set; }
        public int KhPlanDetailId { get; set; }
        public int? KhPlanId { get; set; }
        public string? PlanNo { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? PartNo { get; set; }
        public string DetailJson { get; set; } = "";
        public string? ChangeLogsJson { get; set; }
        public string Reason { get; set; } = "";
        public int DeletedBy { get; set; }
        public DateTime DeletedAt { get; set; }
    }
}
