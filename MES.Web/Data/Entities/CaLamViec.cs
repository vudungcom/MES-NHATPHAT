using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Ca làm việc. GioKetThuc < GioBatDau = ca vắt qua nửa đêm (VD Ca 3: 22:00->06:20).
/// Thời gian nghỉ giải lao lưu trong CaLamViec_NghiGiaiLaos.
/// </summary>
public class CaLamViec
{
    [Key]
    public int CaId { get; set; }

    [Required, MaxLength(100)]
    public string TenCa { get; set; } = "";

    public TimeOnly GioBatDau  { get; set; }

    /// <summary>
    /// Nếu GioKetThuc &lt; GioBatDau => ca vắt qua nửa đêm.
    /// VD: GioBatDau=22:00, GioKetThuc=06:20
    /// </summary>
    public TimeOnly GioKetThuc { get; set; }

    public bool IsActive  { get; set; } = true;
    public int  SortOrder { get; set; } = 0;

    public List<CaLamViec_NghiGiaiLao> NghiGiaiLaos { get; set; } = new();

    [NotMapped] public bool IsOvernight => GioKetThuc < GioBatDau;

    [NotMapped]
    public string Label => IsOvernight
        ? $"{GioBatDau:HH\\:mm}-{GioKetThuc:HH\\:mm}(+1)"
        : $"{GioBatDau:HH\\:mm}-{GioKetThuc:HH\\:mm}";

    [NotMapped]
    public double TotalSpanMinutes
    {
        get
        {
            double span = IsOvernight
                ? 24 * 60 - GioBatDau.ToTimeSpan().TotalMinutes + GioKetThuc.ToTimeSpan().TotalMinutes
                : (GioKetThuc - GioBatDau).TotalMinutes;
            double rest = NghiGiaiLaos.Sum(n => (n.NghiKetThuc - n.NghiBatDau).TotalMinutes);
            return Math.Max(0, span - rest);
        }
    }
}

/// <summary>Khoảng nghỉ giải lao — nhiều record cho 1 ca</summary>
public class CaLamViec_NghiGiaiLao
{
    [Key]
    public int NghiId { get; set; }

    public int     CaId        { get; set; }
    public TimeOnly NghiBatDau  { get; set; }
    public TimeOnly NghiKetThuc { get; set; }

    [MaxLength(100)]
    public string GhiChu { get; set; } = "";

    [ForeignKey(nameof(CaId))]
    public CaLamViec? Ca { get; set; }
}
