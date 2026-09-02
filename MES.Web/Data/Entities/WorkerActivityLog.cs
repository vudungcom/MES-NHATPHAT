using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Lưu thời gian hoạt động KHÔNG link PO của công nhân.
/// VD: nghỉ trưa, họp, chờ vật tư, bảo trì máy, vệ sinh xưởng...
/// Append-only — void + tạo dòng mới nếu sai.
///
/// Kết hợp với WtsProductionLogs để tính hiệu suất ca:
///   Tổng ca = SUM(WtsProductionLogs) + SUM(WorkerActivityLogs)
///   Hiệu suất = WtsProductionLogs / Tổng × 100%
/// </summary>
[Table("WorkerActivityLogs")]
public class WorkerActivityLog
{
    public long ActivityLogId { get; set; }

    [Required]
    public int WorkerId { get; set; }
    [ForeignKey(nameof(WorkerId))]
    public User? Worker { get; set; }

    /// <summary>Ngày làm việc — dùng để query theo ngày nhanh hơn</summary>
    public DateOnly WorkDate { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime   { get; set; }

    /// <summary>Mã từ StandardWtsTasks — nullable vì có thể nhập tự do</summary>
    [MaxLength(20)]
    public string? WtsCode { get; set; }

    /// <summary>Tên activity — copy lúc nhập, không link live với StandardWtsTasks</summary>
    [MaxLength(250)]
    public string? WtsName { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Void
    public bool IsVoided { get; set; } = false;
    [MaxLength(200)] public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    [ForeignKey(nameof(VoidedBy))]
    public User? VoidedByUser { get; set; }
    public DateTime? VoidedAt { get; set; }
}
