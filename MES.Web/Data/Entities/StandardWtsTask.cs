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

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
