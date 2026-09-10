using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Snapshot sub-row máy đồng dạng (PartMachiningTimings) tại thời điểm tạo/refresh phiếu KH.
/// Mỗi dòng trong PartMachiningTimings → 1 dòng ở đây.
/// Bảng con của KhPlanRouteSnapshotMachining (link qua SnapshotId).
/// KHÔNG UPDATE/DELETE — snapshot frozen cùng phiếu KH.
/// </summary>
[Table("KhPlanRouteSnapshotMachiningTimings")]
public class KhPlanRouteSnapshotMachiningTiming
{
    [Key]
    public long TimingSnapshotId { get; set; }

    /// <summary>FK → KhPlanRouteSnapshotMachining.SnapshotId (dòng NC cha)</summary>
    public long MachiningSnapshotId { get; set; }

    /// <summary>Lưu thêm để query trực tiếp không cần JOIN qua Machining snapshot</summary>
    public int KhPlanDetailId { get; set; }

    /// <summary>Thứ tự hiển thị (copy từ PartMachiningTiming.DisplayOrder)</summary>
    public int DisplayOrder { get; set; } = 1;

    /// <summary>Số máy đồng dạng (VD: C04, C06)</summary>
    [Required]
    [MaxLength(50)]
    public string SoMay { get; set; } = "";

    [MaxLength(500)]
    public string? FixtureType { get; set; }

    [MaxLength(500)]
    public string? ToolType { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? SetupTime { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? MachiningTime { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? InspectionTime { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? PreparationTime { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TrialRunTime { get; set; }

    // Navigation
    [ForeignKey(nameof(MachiningSnapshotId))]
    public KhPlanRouteSnapshotMachining? MachiningSnapshot { get; set; }

    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }
}
