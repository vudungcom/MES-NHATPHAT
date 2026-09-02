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
