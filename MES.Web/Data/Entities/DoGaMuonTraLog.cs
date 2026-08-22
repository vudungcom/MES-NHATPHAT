using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class DoGaMuonTraLog
{
    [Key]
    public int TransactionId { get; set; }

    public int DoGaId { get; set; }

    [Required, MaxLength(150)]
    public string NguoiMuon { get; set; } = "";

    public DateTime NgayMuon { get; set; } = DateTime.Now;

    public DateTime? NgayTraDuKien { get; set; }

    public DateTime? NgayTraThucTe { get; set; }

    [MaxLength(500)]
    public string? LyDoMuon { get; set; }

    [MaxLength(500)]
    public string? GhiChuTra { get; set; }

    [Required, MaxLength(50)]
    public string TrangThai { get; set; } = "Đang mượn"; // "Đang mượn", "Đã trả"

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    [ForeignKey("CreatedBy")]
    public virtual User? CreatedByUser { get; set; }

    public DateTime? ReturnedAt { get; set; }
    public int? ReturnedBy { get; set; }

    [ForeignKey("ReturnedBy")]
    public virtual User? ReturnedByUser { get; set; }
}