using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

/// <summary>
/// Log lịch sử thay đổi từng field của KhPlanDetail.
/// Không UPDATE/DELETE, chỉ INSERT.
/// Dùng để: (1) xem lịch sử sửa 1 field, (2) rollback batch import Excel.
/// </summary>
public class KhPlanDetailChangeLog
{
    public long ChangeId { get; set; }

    public int KhPlanDetailId { get; set; }
    public KhPlanDetail? KhPlanDetail { get; set; }

    [Required, MaxLength(50)]
    public string FieldName { get; set; } = "";  // "STD", "Quantity", "OrderVL", ...

    [MaxLength(500)]
    public string? OldValue { get; set; }

    [MaxLength(500)]
    public string? NewValue { get; set; }

    public int ChangedBy { get; set; }
    public User? ChangedByUser { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [MaxLength(30)]
    public string ChangeSource { get; set; } = "Manual";
    // Manual / ExcelImport / API

    public long? ImportBatchId { get; set; }  // Nhóm các dòng cùng 1 lần import

    [MaxLength(500)]
    public string? Reason { get; set; }
}
