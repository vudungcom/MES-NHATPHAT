using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [GN-NG-AFTER 2026-10-27] Báo hàng lỗi phát hiện SAU khi nhận — chứng từ NỘI BỘ của 1 nhóm (không phải phiếu giao, không có bên nhận).
    /// Chuyển số từ ô hàng tốt của nhóm (Kho: Phôi còn · nhóm SX: Chưa làm theo NC / mã CV, hoặc Đã làm chưa giao) sang Tồn hàng lỗi của nhóm.
    /// Tồn nhóm và Cân PO không đổi. Hàng lỗi ra khỏi nhóm vẫn bằng phiếu cũ (chuyển lỗi / gửi sửa / huỷ / trả khách).
    /// Chỉ ghi thêm: báo sai thì huỷ (IsVoided), không sửa / xoá.
    /// </summary>
    public class HandoverNgReport
    {
        public long HandoverNgReportId { get; set; }
        public int KhPlanDetailId { get; set; }

        /// <summary>Nhóm phát hiện: KHO | GC | HTSP | KCS | PKG</summary>
        public string GroupCode { get; set; } = string.Empty;

        /// <summary>Lấy từ ô: PHOI (Kho: phôi còn giao) | CHUA (Chưa làm) | DA (Đã làm chưa giao)</summary>
        public string SourceCell { get; set; } = string.Empty;

        /// <summary>Gia công + CHUA: NC đang chứa hàng lỗi (bắt buộc). Còn lại: null.</summary>
        public string? NC { get; set; }

        public decimal Qty { get; set; }

        /// <summary>DAU_VAO (lỗi có sẵn — do nhóm trước / phôi khách) | DAU_RA (nhóm mình làm hỏng)</summary>
        public string Origin { get; set; } = string.Empty;

        /// <summary>Đầu vào: nhóm gây lỗi — KHACH | KHO | GC | HTSP | KCS | PKG. Đầu ra: null (chính nhóm này).</summary>
        public string? CauseGroupCode { get; set; }

        public string NgReason { get; set; } = string.Empty;
        public string? Notes { get; set; }

        /// <summary>[VULOI 2026-10-28] Vụ lỗi sinh ra từ phiếu báo lỗi này.</summary>
        public long? NgCaseId { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        public KhPlanDetail KhPlanDetail { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public User? VoidedByUser { get; set; }
    }
}
