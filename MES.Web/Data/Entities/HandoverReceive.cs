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

        /// <summary>[NHAN-DOI-O-NC 2026-11-06] Nguồn: HÙNG 06/11 06:56 (OK cột NC lần nhận) + 18:11 (OK ô chọn NC). CLAUDE rà độc lập 06/11 vòng 2 T1.
        /// NC nhận vào Gia công của LẦN NHẬN này (SQL 14, Hùng chạy 06/11 17:40): bên nhận đổi ô sang
        /// Hàng lỗi đã xử lý / Hàng mới với phiếu không có NC nhận (vd phiếu mang vụ vào ô Hàng lỗi). Máy chủ tự điền NC theo vụ (K2),
        /// chỉ lùi về NC trước kèm lý do. NULL = lần nhận cũ / phiếu đã có NC / như hàng mới → sổ tính theo phiếu như cũ.</summary>
        public string? ToNC { get; set; }

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
