using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng công đoạn "Quy trình Kiểm tra" (Inspection - Vùng D) trong Part Master.
/// </summary>
[Table("PartInspectionSteps")]
public class PartInspectionStep
{
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    [Required]
    public int StepOrder { get; set; }

    /// <summary>Mã NC chuẩn từ WTS: '1K', '2K', '1K-DP-01'...</summary>
    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    /// <summary>Tên công đoạn chuẩn từ WTS Tiêu chuẩn.</summary>
    [MaxLength(500)]
    public string? StepName { get; set; }

    /// <summary>Thời gian chuẩn (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? StandardTime { get; set; }

    /// <summary>Đánh dấu là phương án dự phòng.</summary>
    public bool IsBackup { get; set; } = false;

    /// <summary>Mã NC gốc (VD: 1K).</summary>
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