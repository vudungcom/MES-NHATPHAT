using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

// ================================================================
// [STD-UPDATE] Theo dõi thời gian tiêu chuẩn WTS
//   - WTS ăn số chuẩn theo SNAPSHOT của phiếu KH (1 phiếu = 1 nguồn)
//   - Mỗi dòng WTS đóng băng số chuẩn lúc LƯU (WtsProductionLogStds)
//   - Nút "Update TG chuẩn" ở Part Master: chép Part Master → snapshot của mọi PO có part đó,
//     và ghi đè số chuẩn của các dòng WTS lưu (CreatedAt) từ mốc trở đi.
//     Lịch sử: StdUpdateBatches + StdUpdateBatchDetails.
// SQL tạo bảng: SQL_StdUpdate_01_Schema.sql
// ================================================================

/// <summary>Hằng số dùng chung — KHÔNG gõ chuỗi tay ở nơi khác.</summary>
public static class StdTrack
{
    /// <summary>Giá trị cột SnapshotTable</summary>
    public static class Table
    {
        public const string Machining       = "Machining";        // KhPlanRouteSnapshotMachining.SnapshotId
        public const string MachiningTiming = "MachiningTiming";  // KhPlanRouteSnapshotMachiningTimings.TimingSnapshotId
        public const string Taro            = "Taro";             // KhPlanRouteSnapshotTaro.SnapshotId
        public const string Bavia           = "Bavia";            // KhPlanRouteSnapshotBavia.SnapshotId
        public const string Washing         = "Washing";          // KhPlanRouteSnapshotWashing.SnapshotId
        public const string Inspection      = "Inspection";       // KhPlanRouteSnapshotInspection.SnapshotId
        public const string Packaging       = "Packaging";        // KhPlanRouteSnapshotPackaging.SnapshotId
    }

    /// <summary>Giá trị cột FieldName. GC: 5 trường (trùng PartMachiningService.TimingField); C/D/E: StandardTime.</summary>
    public static class Field
    {
        public const string Setup       = "SetupTime";
        public const string Machining   = "MachiningTime";
        public const string Inspection  = "InspectionTime";
        public const string Preparation = "PreparationTime";
        public const string TrialRun    = "TrialRunTime";
        public const string Standard    = "StandardTime";
        public static readonly string[] Gc = { Setup, Machining, Inspection, Preparation, TrialRun };
    }

    /// <summary>Giá trị cột SyncSource</summary>
    public static class Source
    {
        public const string Copy      = "Copy";       // tạo / làm mới snapshot phiếu KH
        public const string Save      = "Save";       // lưu dòng WTS
        public const string StdUpdate = "StdUpdate";  // nút "Update TG chuẩn"
        public const string Backfill  = "Backfill";   // script SQL dữ liệu cũ
    }

    /// <summary>Giá trị cột StdUpdateBatchDetails.Target</summary>
    public static class Target
    {
        public const string Snapshot = "Snapshot";
        public const string WtsLog   = "WtsLog";
    }
}

/// <summary>
/// Trạng thái xác nhận từng ô thời gian chuẩn của snapshot phiếu KH.
/// Mỗi ô (dòng snapshot × trường) = 1 dòng — lưu cả ô chưa xác nhận (IsConfirmed = false).
/// Không FK tới bảng snapshot (polymorphic theo SnapshotTable) — dọn theo KhPlanDetailId
/// khi xóa / làm mới snapshot.
/// </summary>
[Table("KhPlanRouteSnapshotStdConfirms")]
public class KhPlanRouteSnapshotStdConfirm
{
    [Key]
    public long Id { get; set; }

    public int KhPlanDetailId { get; set; }

    /// <summary>StdTrack.Table.*</summary>
    [Required, MaxLength(20)]
    public string SnapshotTable { get; set; } = "";

    /// <summary>Id dòng snapshot (SnapshotId hoặc TimingSnapshotId tuỳ SnapshotTable)</summary>
    public long SnapshotRowId { get; set; }

    /// <summary>StdTrack.Field.*</summary>
    [Required, MaxLength(30)]
    public string FieldName { get; set; } = "";

    public bool IsConfirmed { get; set; }
    public int? ConfirmedBy { get; set; }
    [MaxLength(100)] public string? ConfirmedByName { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>StdTrack.Source.* (Copy | StdUpdate | Backfill)</summary>
    [Required, MaxLength(20)]
    public string SyncSource { get; set; } = "";
    public long? SyncBatchId { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.Now;
    public int? SyncedBy { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
}

/// <summary>
/// Số chuẩn đóng băng của từng dòng WTS (ghi lúc lưu WTS).
/// WtsProductionLogs vẫn append-only — chỉ bảng này được nút "Update TG chuẩn" ghi đè,
/// lịch sử cũ → mới nằm ở StdUpdateBatchDetails.
/// GC: 5 dòng / 1 log. Non-GC: 1 dòng (StandardTime) / 1 log.
/// </summary>
[Table("WtsProductionLogStds")]
public class WtsProductionLogStd
{
    [Key]
    public long Id { get; set; }

    public long WtsLogId { get; set; }
    [ForeignKey(nameof(WtsLogId))]
    public WtsProductionLog? WtsProductionLog { get; set; }

    public int KhPlanDetailId { get; set; }

    /// <summary>StdTrack.Field.*</summary>
    [Required, MaxLength(30)]
    public string FieldName { get; set; } = "";

    [Column(TypeName = "decimal(10,2)")]
    public decimal? StdValue { get; set; }

    public bool IsConfirmed { get; set; }
    public int? ConfirmedBy { get; set; }
    [MaxLength(100)] public string? ConfirmedByName { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>Dòng snapshot đã lấy số (tham khảo — làm mới snapshot sẽ đổi Id)</summary>
    [MaxLength(20)] public string? SnapshotTable { get; set; }
    public long? SnapshotRowId { get; set; }

    /// <summary>StdTrack.Source.* (Save | StdUpdate | Backfill)</summary>
    [Required, MaxLength(20)]
    public string SyncSource { get; set; } = "";
    public long? SyncBatchId { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.Now;
    public int? SyncedBy { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
}

/// <summary>
/// Mỗi lần bấm "Update TG chuẩn" ở Part Master = 1 dòng.
/// Không FK — lịch sử giữ lại kể cả khi phiếu KH / Part bị xóa.
/// </summary>
[Table("StdUpdateBatches")]
public class StdUpdateBatch
{
    [Key]
    public long BatchId { get; set; }

    public int PartId { get; set; }
    [Required, MaxLength(100)]
    public string PartNo { get; set; } = "";

    /// <summary>Mốc: dòng WTS lưu (CreatedAt) từ thời điểm này trở đi được cập nhật số chuẩn mới</summary>
    public DateTime EffectiveFrom { get; set; }

    public int RunBy { get; set; }
    [MaxLength(100)] public string? RunByName { get; set; }
    public DateTime RunAt { get; set; } = DateTime.Now;
    [MaxLength(500)] public string? Reason { get; set; }

    /// <summary>Số phiếu KH (PO) đã quét</summary>
    public int PlanCount { get; set; }
    /// <summary>Tổng ô snapshot đã quét</summary>
    public int SnapshotCellCount { get; set; }
    /// <summary>Ô snapshot có thay đổi (giá trị hoặc xác nhận)</summary>
    public int SnapshotCellChanged { get; set; }
    /// <summary>Dòng WTS sau mốc đã quét</summary>
    public int WtsLogCount { get; set; }
    /// <summary>Ô chuẩn WTS có thay đổi</summary>
    public int WtsCellChanged { get; set; }

    public virtual ICollection<StdUpdateBatchDetail> Details { get; set; } = new List<StdUpdateBatchDetail>();
}

/// <summary>
/// Chi tiết từng ô THAY ĐỔI trong 1 lần Update (chỉ ghi ô có khác biệt).
/// Target = Snapshot → SnapshotTable + SnapshotRowId; Target = WtsLog → WtsLogId (+ dòng snapshot nguồn).
/// </summary>
[Table("StdUpdateBatchDetails")]
public class StdUpdateBatchDetail
{
    [Key]
    public long Id { get; set; }

    public long BatchId { get; set; }
    [ForeignKey(nameof(BatchId))]
    public StdUpdateBatch? Batch { get; set; }

    public int KhPlanDetailId { get; set; }
    [MaxLength(50)] public string? PurchaseOrder { get; set; }

    /// <summary>StdTrack.Target.* (Snapshot | WtsLog)</summary>
    [Required, MaxLength(10)]
    public string Target { get; set; } = "";

    [MaxLength(20)] public string? SnapshotTable { get; set; }
    public long? SnapshotRowId { get; set; }
    public long? WtsLogId { get; set; }

    [MaxLength(20)] public string? NC { get; set; }
    /// <summary>GC: SoMay; non-GC: mã WTS</summary>
    [MaxLength(50)] public string? MachineOrCode { get; set; }

    /// <summary>StdTrack.Field.*</summary>
    [Required, MaxLength(30)]
    public string FieldName { get; set; } = "";

    [Column(TypeName = "decimal(10,2)")] public decimal? OldValue { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? NewValue { get; set; }

    public bool? OldIsConfirmed { get; set; }
    public bool NewIsConfirmed { get; set; }
    [MaxLength(100)] public string? OldConfirmedByName { get; set; }
    [MaxLength(100)] public string? NewConfirmedByName { get; set; }
    public DateTime? NewConfirmedAt { get; set; }
}
