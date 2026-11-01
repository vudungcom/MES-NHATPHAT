using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// [ML17-XN][CA-NGHI-HIST 2026-11-01] Mỗi khoảng nghỉ ca mà 1 dòng WTS (WtsLogId) hoặc 1 dòng "Việc không ra sản phẩm"
/// (ActivityLogId) chạm vào — chép GIỜ NGHỈ LÚC LƯU (giờ tuyệt đối, ca qua đêm đã tính đúng ngày) + số phút làm thông nghỉ.
/// SQL: tools/sql/06_ML17XN_WtsBreakWorkLogs.sql. Đúng 1 trong 2 khoá cha khác null.
/// Append-only theo dòng cha: sửa dòng = huỷ dòng cha + dòng cha mới + bộ dòng nghỉ mới; không UPDATE / DELETE.
/// Phút của dòng đã lưu CHỈ tính từ bảng này (Services/WorkMinutesService) — sửa giờ nghỉ ca sau đó không đổi số ngày cũ.
/// </summary>
[Table("WtsBreakWorkLogs")]
public class WtsBreakWorkLog
{
    public long Id { get; set; }

    public long? WtsLogId { get; set; }
    public long? ActivityLogId { get; set; }

    /// <summary>Ca lúc lưu — chỉ tham chiếu, không khoá ngoại.</summary>
    public int? CaId { get; set; }
    /// <summary>Id khoảng nghỉ của ca lúc lưu — chỉ tham chiếu (ca lưu lại thì id cũ mất).</summary>
    public int? NghiId { get; set; }
    /// <summary>Ghi chú khoảng nghỉ chép lúc lưu (vd "Nghỉ trưa").</summary>
    [MaxLength(100)] public string? BreakName { get; set; }

    /// <summary>Giờ bắt đầu / kết thúc khoảng nghỉ (tuyệt đối, không cắt theo dòng).</summary>
    public DateTime BreakStart { get; set; }
    public DateTime BreakEnd { get; set; }

    /// <summary>Phút chồng giữa dòng cha và khoảng nghỉ (&gt; 0).</summary>
    public int OverlapMin { get; set; }
    /// <summary>Phút làm thông nghỉ, 0..OverlapMin; khúc làm nằm ĐẦU phần chồng.</summary>
    public int WorkedMin { get; set; }

    /// <summary>true = chép bù dữ liệu cũ bằng tools/BackfillNghi (giờ nghỉ của ca ngày chạy công cụ).</summary>
    public bool IsBackfill { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
