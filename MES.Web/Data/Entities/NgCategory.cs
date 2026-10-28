using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [DS-LOI 2026-10-28] Danh mục loại lỗi (trang "DS Lỗi"). Mọi chỗ ghi lỗi (WTS NG, NG lúc nhận, báo lỗi sau khi nhận,
    /// phiếu hàng lỗi công đoạn trước) bắt buộc CHỌN từ đây — không tự gõ.
    /// Đã dùng thì không xoá, chỉ Ngưng dùng (IsActive = 0): ẩn khỏi danh sách chọn, vụ lỗi cũ vẫn hiện tên.
    /// </summary>
    public class NgCategory
    {
        public int NgCategoryId { get; set; }
        /// <summary>Mã lỗi ngắn (duy nhất, viết hoa), vd MOP, REN.</summary>
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Chữ ghi vào cột NgReason của chứng từ (để các màn cũ / WTS Report vẫn đọc được).</summary>
        public string Label => $"{Code} · {Name}";
    }

    /// <summary>[DS-LOI] Lịch sử sửa danh mục lỗi. Action: ADD | EDIT | OFF | ON | DEL.</summary>
    public class NgCategoryChangeLog
    {
        public long ChangeId { get; set; }
        public int NgCategoryId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public int ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
