using System;

namespace MES.Web.Data.Entities
{
    public class HandoverReceive
    {
        public long ReceiveId { get; set; }
        public long HandoverTxId { get; set; }

        public decimal QtyOk { get; set; }
        public decimal QtyNg { get; set; }

        /// <summary>Bắt buộc nếu QtyNg > 0 — validate ở service</summary>
        public string? NgReason { get; set; }

        /// <summary>[GN-CELL2 2026-10-24] Ô bên nhận THỰC NHẬN lần này (MOI | LOI | SUA | XL). NULL = lần nhận cũ / phiếu theo NC → theo phiếu.</summary>
        public string? ToCell { get; set; }

        /// <summary>[GN-CELL2] Lý do khi bên nhận đổi khác ô bên giao chỉ định (bắt buộc khi đổi).</summary>
        public string? ToCellReason { get; set; }

        /// <summary>[VULOI 2026-10-28] Vụ lỗi SINH RA ở lần nhận này (NG đếm lúc nhận, hoặc nhận phiếu chưa mang vụ vào ô Hàng lỗi / Hàng sửa).
        /// Phiếu đã mang vụ: cột này = vụ của phiếu (không sinh vụ mới).</summary>
        public long? NgCaseId { get; set; }

        public int ReceivedBy { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        // Navigation
        public HandoverTransaction Transaction { get; set; } = null!;
        public User ReceivedByUser { get; set; } = null!;
        public User? VoidedByUser { get; set; }
    }
}
