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

    /// <summary>
    /// [TARO-NC-MA 2026-11-06] NC của dòng quy trình Taro mà dòng WTS này làm (SQL 07). Chỉ ghi khi mã CV Taro dùng ở nhiều NC của PO
    /// (vd 3T1 ở NC 3 và NC 5) — form WTS bắt chọn NC. NULL = dòng cũ / mã chỉ ở 1 NC. KHÔNG dùng lại cột NC (cột NC của dòng HTSP
    /// đang chở mã CV ở báo cáo, Update TG chuẩn). Nguồn: KHÁCH 02/11 09:14 chọn B (giữ mã chung, form WTS Taro thêm ô NC).
    /// </summary>
    [MaxLength(20)]
    public string? StepNC { get; set; }

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

    /// <summary>[SERIAL-TIME-REQUIRED] Chế độ GC: "serial" = Hàng loạt, "trial" = Chạy thử. NULL = non-GC.</summary>
    [MaxLength(10)] public string? ProductionMode { get; set; }

    // [ACTUAL-PC] Non-GC (HTSP/KCS/Đóng gói): T.Thực tế / sản phẩm (phút) công nhân nhập.
    // Mã chuẩn bị (1V/1T/1R/1K/1D) = tổng phút. GC: NULL (dùng các cột *_Actual ở trên).
    [Column(TypeName = "decimal(10,2)")] public decimal? ActualTimePerPc { get; set; }
    /// <summary>[ACTUAL-PC] true = công nhân gõ số KHÁC chuẩn; false = để mặc định bằng chuẩn; NULL = GC / dữ liệu cũ</summary>
    public bool? IsActualManual { get; set; }

    /// <summary>Thời gian phát sinh ngoài kế hoạch (phút). NULL = không có.</summary>
    public int? ExtraMinutes { get; set; }

    // / <summary>Tăng ca — ghi ngoài khung giờ ca thông thường.</summary>
    public bool IsOvertime { get; set; } = false;

    /// <summary>Ca làm việc — dùng cho báo cáo ngày/ca/công nhân. NULL = không chọn ca.</summary>
    public int? CaId { get; set; }
    [ForeignKey(nameof(CaId))]
    public CaLamViec? Ca { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>[VULOI 2026-10-28] Vụ lỗi của phần NG (QtyNg &gt; 0). Sửa dòng (void + dòng mới) thì dòng mới giữ vụ cũ.</summary>
    public long? NgCaseId { get; set; }

    // Void — nhập sai thì void dòng này + tạo dòng mới đúng
    public bool IsVoided { get; set; } = false;
    [MaxLength(200)] public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    [ForeignKey(nameof(VoidedBy))]
    public User? VoidedByUser { get; set; }
    public DateTime? VoidedAt { get; set; }
}
