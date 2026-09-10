using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Lịch sử công nhân thực hiện WTS cho từng NC/công đoạn của KhPlanDetail.
/// Append-only — không UPDATE/DELETE. Sai thì Void + tạo dòng mới đúng.
/// </summary>
[Table("WtsProductionLogs")]
public class WtsProductionLog
{
    public long WtsLogId { get; set; }

    [Required]
    public int KhPlanDetailId { get; set; }
    [ForeignKey(nameof(KhPlanDetailId))]
    public KhPlanDetail? KhPlanDetail { get; set; }

    /// <summary>Nhóm công đoạn: GC | TARO | BAVIA | WASHING | KCS | PKG</summary>
    [Required, MaxLength(10)]
    public string ProcessGroup { get; set; } = "";

    /// <summary>Mã nguyên công (NC1, NC2...). NULL với công đoạn C/D/E.</summary>
    [MaxLength(20)]
    public string? NC { get; set; }

    /// <summary>Mã WTS tiêu chuẩn (1T, 2V, 1K...). NULL với GC.</summary>
    [MaxLength(20)]
    public string? WtsCode { get; set; }

    /// <summary>Nhóm nhỏ HTSP: "Taro" | "Bavia" | "Rua". NULL với các nhóm khác.</summary>
    [MaxLength(20)]
    public string? HtspSubGroup { get; set; }

    [Required]
    public int WorkerId { get; set; }
    [ForeignKey(nameof(WorkerId))]
    public User? Worker { get; set; }

    /// <summary>Máy thực tế dùng — có thể là máy đồng dạng, khác máy đăng ký.</summary>
    [MaxLength(50)]
    public string? MachineUsed { get; set; }

    /// <summary>Số lượng hoàn thành trong lần submit này.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal QtyDone { get; set; }
	public decimal? QtyOk    { get; set; }
public decimal? QtyNg    { get; set; }
public string?  NgReason { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    // Thời gian thực tế (phút) — chỉ áp dụng cho GC
    [Column(TypeName = "decimal(10,2)")] public decimal? SetupTime_Actual { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? MachiningTime_Actual { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? InspectionTime_Actual { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PreparationTime_Actual { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? TrialRunTime_Actual { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Void — nhập sai thì void dòng này + tạo dòng mới đúng
    public bool IsVoided { get; set; } = false;
    [MaxLength(200)] public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    [ForeignKey(nameof(VoidedBy))]
    public User? VoidedByUser { get; set; }
    public DateTime? VoidedAt { get; set; }
}
