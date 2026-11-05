using System;
using System.Collections.Generic;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [GC-SUA-TAI-CHO 2026-11-04] "Kết quả sửa" — chứng từ NỘI BỘ của nhóm đang giữ hàng lỗi (Kho / GC / HTSP / KCS / Đóng gói).
    /// Khách chốt 04/11 14:53: nhóm nào cũng có thể sửa tại chỗ; hàng sửa ĐẠT phải có KCS xác nhận rồi mới làm tiếp;
    /// hàng KHÔNG ĐẠT theo quy trình lỗi bình thường (ở lại ô Hàng lỗi của nhóm, cùng vụ).
    /// Lấy từ ô Hàng sửa (SUA: nhóm khác gửi nhờ sửa) hoặc ô Hàng lỗi (LOI: hàng lỗi của mình), 1 chứng từ = 1 vụ lỗi.
    /// QtyPass = đạt (chờ KCS xác nhận — bảng HandoverRepairChecks); QtyFail = không đạt (chỉ khi lấy từ SUA → ô Hàng lỗi cùng vụ ngay).
    /// Không lưu số tồn: sổ chung HandoverLedgerService tính lại (L6). Chỉ ghi thêm: sai thì huỷ, không sửa / xoá.
    /// </summary>
    public class HandoverRepairResult
    {
        public long HandoverRepairResultId { get; set; }
        public int KhPlanDetailId { get; set; }

        /// <summary>Nhóm sửa = nhóm đang giữ hàng: KHO | GC | HTSP | KCS | PKG</summary>
        public string GroupCode { get; set; } = string.Empty;

        /// <summary>Lấy từ ô: SUA (Hàng sửa) | LOI (Hàng lỗi)</summary>
        public string SourceCell { get; set; } = string.Empty;

        /// <summary>Vụ lỗi lấy hàng. NULL = hàng lỗi cũ chưa có mã vụ.</summary>
        public long? NgCaseId { get; set; }

        /// <summary>Số sửa đạt — chờ KCS xác nhận.</summary>
        public decimal QtyPass { get; set; }

        /// <summary>Số sửa không đạt — chỉ khi lấy từ ô Hàng sửa: vào ô Hàng lỗi của nhóm, cùng vụ.</summary>
        public decimal QtyFail { get; set; }

        /// <summary>Gia công: NC làm tiếp cho hàng đạt (sau khi KCS xác nhận). NULL = như hàng mới (NC đầu / Nhận vào).</summary>
        public string? ToNC { get; set; }

        public string? Notes { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        public KhPlanDetail KhPlanDetail { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public User? VoidedByUser { get; set; }
        public ICollection<HandoverRepairCheck> Checks { get; set; } = new List<HandoverRepairCheck>();
    }
}
