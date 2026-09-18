using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// 1 dòng phát sinh chi tiết gắn với 1 WtsProductionLog hoặc WorkerActivityLog.
/// Append-only — không UPDATE/DELETE (theo kiến trúc WTS).
/// Quan hệ: WtsLogId XOR ActivityLogId (1 trong 2 phải có).
/// </summary>
[Table("WtsExtraItems")]
public class WtsExtraItemLog
{
    public long Id { get; set; }

    /// <summary>FK về WtsProductionLogs — null nếu là WorkerActivityLog</summary>
    public long? WtsLogId { get; set; }
    [ForeignKey(nameof(WtsLogId))]
    public WtsProductionLog? WtsProductionLog { get; set; }

    /// <summary>FK về WorkerActivityLogs — null nếu là WtsProductionLog</summary>
    public long? ActivityLogId { get; set; }
    [ForeignKey(nameof(ActivityLogId))]
    public WorkerActivityLog? WorkerActivityLog { get; set; }

    /// <summary>Mã WTS tiêu chuẩn (TaskCode từ StandardWtsTasks) — gõ tự do, sau chuẩn hóa</summary>
    [MaxLength(50)]
    public string? WtsCode { get; set; }

    /// <summary>Tên công việc — snapshot lúc lưu, không link live về StandardWtsTasks</summary>
    [MaxLength(250)]
    public string? WtsName { get; set; }

    /// <summary>Thời gian phát sinh (phút)</summary>
    public int Minutes { get; set; }

    /// <summary>Nội dung ghi chú của công nhân</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
