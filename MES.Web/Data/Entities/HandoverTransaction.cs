using System;
using System.Collections.Generic;

namespace MES.Web.Data.Entities
{
    public class HandoverTransaction
    {
        public long HandoverTxId { get; set; }
        public int KhPlanDetailId { get; set; }

        /// <summary>KHO | GC | HTSP | KCS | PKG</summary>
        public string FromGroupCode { get; set; } = string.Empty;

        /// <summary>KHO | GC | HTSP | KCS | PKG</summary>
        public string ToGroupCode { get; set; } = string.Empty;

        /// <summary>SL bên giao khai báo giao sang nhóm nhận</summary>
        public decimal QtyIssued { get; set; }

        /// <summary>
        /// NC nguồn — dùng khi giao inter-group (VD: GC→HTSP taro NC1, HTSP→GC sau khi taro xong).
        /// Null = giao thông thường (không gắn với NC cụ thể).
        /// </summary>
        public string? FromNC { get; set; }
		public string? ToNC { get; set; }   // v0.8 — NC đích ở nhóm nhận

        /// <summary>PENDING | PARTIAL | COMPLETED</summary>
        public string Status { get; set; } = "PENDING";

        public int IssuedBy { get; set; }
        public DateTime IssuedAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        // Navigation
        public KhPlanDetail KhPlanDetail { get; set; } = null!;
        public User IssuedByUser { get; set; } = null!;
        public User? VoidedByUser { get; set; }
        public ICollection<HandoverReceive> Receives { get; set; } = new List<HandoverReceive>();
    }
}
