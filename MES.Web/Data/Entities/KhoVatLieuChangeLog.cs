using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("KhoVatLieuChangeLogs")]
public class KhoVatLieuChangeLog
{
    [Key]
    public long LogId { get; set; }

    public long KhoVatLieuId { get; set; }

    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public int ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [ForeignKey(nameof(KhoVatLieuId))]
    public virtual KhoVatLieu? KhoVatLieu { get; set; }

    [ForeignKey(nameof(ChangedBy))]
    public virtual User? ChangedByUser { get; set; }
}

public class KhoVatLieuChangeLogItem
{
    public long LogId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}