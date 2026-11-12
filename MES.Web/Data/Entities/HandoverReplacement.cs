using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [KHACH-GUI-LAI 2026-11-11] Một lần KHÁCH GỬI BÙ phôi cho hàng lỗi đã Trả khách / Huỷ (bảng HandoverReplacements — SQL 15_KHBU, Hùng chạy 11/11).
    /// Nguồn: KHÁCH 10/11 vòng 1 câu 1a A (phôi bù vào Kho ô Hàng lỗi đã xử lý = phôi còn, gắn lần lỗi cũ, giao Gia công vào NC đầu như phôi thường)
    ///        + 1b A (chặn bù vượt; gửi dư thì sửa Số lượng thực nhận); HÙNG 11/11 14:39 duyệt thiết kế + SQL, trần bù chặn cả khi PO tắt Workflow.
    /// Trước: không có chứng từ — cách tạm là tăng Số lượng thực nhận của phôi (không biết bù cho lần lỗi nào).
    /// Mỗi dòng gắn đúng 1 lần lỗi (NgCaseId). Sổ chung HandoverLedgerService tính: Kho Nhận · Hàng lỗi đã xử lý + số bù, cân PO phôi + bù = …
    /// Không lưu số tồn. Chỉ ghi thêm: sai thì huỷ (IsVoided + người / giờ / lý do, K5 kiểm trước khi huỷ), không sửa / xoá.
    /// </summary>
    public class HandoverReplacement
    {
        public long ReplacementId { get; set; }
        public int KhPlanDetailId { get; set; }
        /// <summary>Lần lỗi được bù (đã có Trả khách / Huỷ).</summary>
        public long NgCaseId { get; set; }
        public decimal Qty { get; set; }
        /// <summary>Người Kho bấm nhận phôi bù.</summary>
        public int ReceivedBy { get; set; }
        /// <summary>Giờ máy chủ lúc lưu.</summary>
        public DateTime ReceivedAt { get; set; }
        /// <summary>Số phiếu / ghi chú của khách.</summary>
        public string? Notes { get; set; }
        public bool IsVoided { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
