using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng công đoạn "Quy trình Đóng gói" (Packaging - Vùng E) trong Part Master.
/// Phân quyền PART_DONGGOI quản lý.
/// </summary>
[Table("PartPackagingSteps")]
public class PartPackagingStep
{
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    [Required]
    public int StepOrder { get; set; }

    /// <summary>Mã NC chuẩn từ WTS: '1D', '2D', '1D-DP-01'...</summary>
    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    /// <summary>Tên công đoạn chuẩn từ WTS Tiêu chuẩn.</summary>
    [MaxLength(500)]
    public string? StepName { get; set; }

    /// <summary>Thời gian chuẩn (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? StandardTime { get; set; }

    /// <summary>Đánh dấu là phương án dự phòng (true = DP, false = Chính thức).</summary>
    public bool IsBackup { get; set; } = false;

    /// <summary>Mã NC gốc của phương án chính (VD: 1D nếu đây là 1D-DP-01).</summary>
    [MaxLength(20)]
    public string? ParentNC { get; set; }

    // Audit
    [Required]
    public int CreatedBy { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? UpdatedBy { get; set; }

    [ForeignKey(nameof(UpdatedBy))]
    public User? UpdatedByUser { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}