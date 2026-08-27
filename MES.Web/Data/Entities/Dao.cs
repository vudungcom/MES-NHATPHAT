using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("Daos")]
public class Dao
{
    [Key]
    public int DaoId { get; set; }

    [MaxLength(50)]
    public string? MaDao { get; set; }

    [Required]
    [MaxLength(255)]
    public string TenDao { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LoaiDao { get; set; }

    [MaxLength(100)]
    public string? QuyCach { get; set; }

    [MaxLength(100)]
    public string? HangSX { get; set; }

    [MaxLength(100)]
    public string? VatLieuDao { get; set; }

    [MaxLength(100)]
    public string? ViTriKho { get; set; }

    public int? SoLuongTon { get; set; }

    [MaxLength(500)]
    public string? GhiChu { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public virtual User? CreatedByUser { get; set; }

    [ForeignKey(nameof(UpdatedBy))]
    public virtual User? UpdatedByUser { get; set; }
}