using MES.Web.Data.Entities;    // ← THÊM DÒNG NÀY
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng công đoạn "Quy trình gia công" (Machining) trong Part Master.
/// Nhóm Kỹ thuật (TECHNICAL) sửa. Leader thêm quyền xóa (soft delete IsActive=0).
/// Mọi thao tác sửa cần PIN + ghi PartProcessStepChangeLog.
/// </summary>
[Table("PartMachiningSteps")]
public class PartMachiningStep
{
    // PK khai báo trong AppDbContext.OnModelCreating bằng HasKey(x => x.StepId)
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    /// <summary>Thứ tự hiển thị (1, 2, 3...). Server tự tính = MAX + 1 khi thêm mới.</summary>
    [Required]
    public int StepOrder { get; set; }

    /// <summary>Mã NC: 'NC01', 'NC02', 'NC03'...</summary>
    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    /// <summary>Bản vẽ (text). Sau này có thể upload file qua FileAttachment.</summary>
    [MaxLength(100)]
    public string? Drawing { get; set; }

    /// <summary>Máy đăng ký (VD: PHAY08, CAT HOI, TARO, VIA).</summary>
    [MaxLength(50)]
    public string? MachineRegistered { get; set; }

    /// <summary>Máy đồng dạng — có thể thay thế (VD: P04, P05, P07).</summary>
    [MaxLength(200)]
    public string? MachineAlternative { get; set; }

    /// <summary>Loại đồ gá (VD: VJ45).</summary>
    [MaxLength(50)]
    public string? FixtureType { get; set; }

    /// <summary>Thời gian gá lắp (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? SetupTime { get; set; }

    /// <summary>Thời gian gia công (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? MachiningTime { get; set; }

    /// <summary>Thời gian kiểm tra SP (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? InspectionTime { get; set; }

    /// <summary>Thời gian chuẩn bị (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? PreparationTime { get; set; }

    /// <summary>Thời gian chạy thử (phút).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? TrialRunTime { get; set; }

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

    /// <summary>Soft delete: false = đã xóa (chỉ Leader được set về false, có thể khôi phục).</summary>
    public bool IsActive { get; set; } = true;
}
