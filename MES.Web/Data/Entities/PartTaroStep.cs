using MES.Web.Data.Entities;    // ← THÊM DÒNG NÀY
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng công đoạn "Quy trình Taro" trong Part Master.
/// Nhóm Hoàn thiện SP (FINISHING) sửa. Leader thêm quyền xóa (soft delete).
/// Mọi thao tác sửa cần PIN + ghi PartProcessStepChangeLog.
/// </summary>
[Table("PartTaroSteps")]
public class PartTaroStep
{
    // PK khai báo trong AppDbContext.OnModelCreating bằng HasKey(x => x.StepId)
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    [Required]
    public int StepOrder { get; set; }

    /// <summary>Mã NC: '1T', '2T', '3T'...</summary>
    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    /// <summary>Tên công đoạn (VD: Chuẩn bị dao + máy, Kiểm tra bề mặt, Làm vát đón...).</summary>
    [MaxLength(500)]
    public string? StepName { get; set; }

    /// <summary>Thời gian chuẩn (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? StandardTime { get; set; }

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
