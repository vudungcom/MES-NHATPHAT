using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

// ================================================================
// Các bảng Snapshot quy trình (B→E) cho KhPlanDetail.
// Được copy từ PartMaster tại thời điểm tạo phiếu KH.
// Sau khi lưu → KHÔNG tự update theo PartMaster.
// Mục đích: freeze quy trình, so sánh lịch sử, truy vết.
// ================================================================

// ── B. Machining ────────────────────────────────────────────────
[Table("KhPlanRouteSnapshotMachining")]
public class KhPlanRouteSnapshotMachining
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    /// <summary>PartId lúc copy — dùng để so sánh với PartMaster sau này</summary>
    public int SourcePartId { get; set; }

    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartMachiningStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)] public string NC { get; set; } = "";
    [MaxLength(100)] public string? Drawing { get; set; }
    [MaxLength(50)]  public string? MachineRegistered { get; set; }
    [MaxLength(200)] public string? MachineAlternative { get; set; }
    [MaxLength(50)]  public string? FixtureType { get; set; }
    [MaxLength(100)] public string? ToolType { get; set; }
    [MaxLength(50)]  public string? TimingMachine { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? SetupTime { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? MachiningTime { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? InspectionTime { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PreparationTime { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? TrialRunTime { get; set; }
}

// ── C.1 Taro ────────────────────────────────────────────────────
[Table("KhPlanRouteSnapshotTaro")]
public class KhPlanRouteSnapshotTaro
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    public int SourcePartId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartTaroStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)]  public string NC { get; set; } = "";
    [MaxLength(500)] public string? StepName { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? StandardTime { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
}

// ── C.2 Bavia ───────────────────────────────────────────────────
[Table("KhPlanRouteSnapshotBavia")]
public class KhPlanRouteSnapshotBavia
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    public int SourcePartId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartBaviaStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)]  public string NC { get; set; } = "";
    [MaxLength(500)] public string? StepName { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? StandardTime { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
}

// ── C.3 Rửa ─────────────────────────────────────────────────────
[Table("KhPlanRouteSnapshotWashing")]
public class KhPlanRouteSnapshotWashing
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    public int SourcePartId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartWashingStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)]  public string NC { get; set; } = "";
    [MaxLength(500)] public string? StepName { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? StandardTime { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
}

// ── D. Inspection (KCS) ─────────────────────────────────────────
[Table("KhPlanRouteSnapshotInspection")]
public class KhPlanRouteSnapshotInspection
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    public int SourcePartId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartInspectionStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)]  public string NC { get; set; } = "";
    [MaxLength(500)] public string? StepName { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? StandardTime { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
}

// ── E. Packaging (Đóng gói) ─────────────────────────────────────
[Table("KhPlanRouteSnapshotPackaging")]
public class KhPlanRouteSnapshotPackaging
{
    public long SnapshotId { get; set; }

    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    public int SourcePartId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.Now;
    public int SnapshotBy { get; set; }
    [ForeignKey(nameof(SnapshotBy))]
    public User? SnapshotByUser { get; set; }

    // ── Copy từ PartPackagingStep ──
    public int StepOrder { get; set; }
    [Required, MaxLength(20)]  public string NC { get; set; } = "";
    [MaxLength(500)] public string? StepName { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? StandardTime { get; set; }
    public bool IsBackup { get; set; } = false;
    [MaxLength(20)]  public string? ParentNC { get; set; }
}
