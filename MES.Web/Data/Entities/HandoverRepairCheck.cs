using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [GC-SUA-TAI-CHO 2026-11-04] Một lần KCS xác nhận hàng sửa đạt của 1 "Kết quả sửa" (xác nhận từng phần được).
    /// QtyOk = KCS xác nhận đạt → ô Hàng lỗi đã xử lý của nhóm sửa (vào Chưa làm, nhập WTS được; Gia công vào NC đã chọn).
    /// QtyNg = KCS loại → ô Hàng lỗi của nhóm sửa, cùng vụ.
    /// Người xác nhận phải khác người lập (Hùng chốt 04/11 16:11). Chỉ ghi thêm: sai thì huỷ.
    /// </summary>
    public class HandoverRepairCheck
    {
        public long HandoverRepairCheckId { get; set; }
        public long HandoverRepairResultId { get; set; }

        public decimal QtyOk { get; set; }
        public decimal QtyNg { get; set; }

        public string? Notes { get; set; }

        public int CheckedBy { get; set; }
        public DateTime CheckedAt { get; set; }

        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        public HandoverRepairResult RepairResult { get; set; } = null!;
        public User CheckedByUser { get; set; } = null!;
        public User? VoidedByUser { get; set; }
    }
}
