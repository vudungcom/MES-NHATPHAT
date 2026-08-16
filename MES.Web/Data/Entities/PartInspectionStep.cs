using MES.Web.Data.Entities;    // ← THÊM DÒNG NÀY
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng công đoạn "Quy trình Kiểm tra" trong Part Master.
/// Nhóm Kiểm tra (INSPECTION) sửa. Leader thêm quyền xóa (soft delete).
/// Mọi thao tác sửa cần PIN + ghi PartProcessStepChangeLog.
/// </summary>
[Table("PartInspectionSteps")]
public class PartInspectionStep
{
    // PK khai báo trong AppDbContext.OnModelCreating bằng HasKey(x => x.StepId)
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    [Required]
    public int StepOrder { get; set; }

    /// <summary>Mã NC: '1K', '2K', '4K1', '4K2'...</summary>
    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    /// <summary>Tên công đoạn (VD: Chuẩn bị, Kiểm tra bề mặt, Kiểm tra ren...).</summary>
    [MaxLength(500)]
    public string? StepName { get; set; }

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
