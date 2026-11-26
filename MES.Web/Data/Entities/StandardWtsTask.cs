using System;
using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

public class StandardWtsTask
{
    [Key]
    public int TaskId { get; set; }

    [Required, MaxLength(50)]
    public string CategoryCode { get; set; } = "TARO"; // 'TARO', 'BAVIA', 'RUA', 'KCS', 'DONG_GOI', 'KT_NC'

    [Required, MaxLength(150)]
    public string CategoryName { get; set; } = "Công việc nhóm Taro";

    [Required, MaxLength(50)]
    public string TaskCode { get; set; } = ""; // '1T', '3V1', 'KM1'... (Khóa tham chiếu chuẩn)

    [Required, MaxLength(250)]
    public string TaskName { get; set; } = "";

    [MaxLength(50)]
    public string DefaultUnit { get; set; } = "Chi tiết";

    public int? StandardTimeSec { get; set; } = 0;

    [MaxLength(500)]
    public string? GhiChu { get; set; }

    public int DisplayOrder { get; set; } = 1;

    /// <summary>
    /// true = Công việc có ích (tính vào hiệu suất sản xuất)
    /// false = Công việc vô ích / thời gian chết (không tính hiệu suất)
    /// Mặc định true — admin tự đánh dấu những mã vô ích
    /// </summary>
    public bool IsProductiveTask { get; set; } = true;

    /// <summary>
    /// [WTS-1NGUON 2026-11-26] true = "Mã dùng cho Gia công": mọi dòng WTS Gia công (mọi NC, Hàng loạt lẫn Chạy thử) lấy cờ
    /// có ích / vô ích của mã này trong báo cáo hiệu suất. Cả bảng tối đa 1 mã (chỉ mục lọc duy nhất UX_StandardWtsTasks_IsGcTask, SQL 21).
    /// Nguồn: HÙNG 26/11 22:34 (Gia công dùng CHUNG 1 mã) + 23:00 "chốt lại làm theo bạn" (ô chọn ở trang WTS tiêu chuẩn).
    /// </summary>
    public bool IsGcTask { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
