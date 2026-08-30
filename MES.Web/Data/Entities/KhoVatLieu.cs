using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("KhoVatLieus")]
public class KhoVatLieu
{
    [Key]
    public long KhoVatLieuId { get; set; }

    public long? PlanDetailId { get; set; }

    // Cột A -> H: Dữ liệu link từ Kế hoạch
    [Required]
    [MaxLength(100)]
    public string SoPO { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string PartNo { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DonViTinh { get; set; } = "PC";

    public int SoLuongKeHoach { get; set; }

    public DateTime? ThoiHan { get; set; }

    [MaxLength(150)]
    public string? MaVatLieu { get; set; }

    [MaxLength(250)]
    public string? CauHinhPhoi { get; set; }

    [MaxLength(500)]
    public string? GhiChuPhoi { get; set; }

    // Cột I -> O: Kho tự nhập theo quyền
    public DateTime? NgayNhanPhoi { get; set; }

    public int? SoLuongThucNhan { get; set; }

    [MaxLength(100)]
    public string? OrderVatLieu { get; set; }

    [MaxLength(100)]
    public string? ViTriDePhoi { get; set; }

    [MaxLength(50)]
    public string? TinhTrangPhoi { get; set; }

    public DateTime? NgayCapPhoi { get; set; }

    public int? SoLuongCap { get; set; }

    // System Audit
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public virtual User? CreatedByUser { get; set; }

    [ForeignKey(nameof(UpdatedBy))]
    public virtual User? UpdatedByUser { get; set; }
}