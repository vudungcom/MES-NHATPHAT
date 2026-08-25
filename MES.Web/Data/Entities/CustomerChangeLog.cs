using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("CustomerChangeLogs")]
public class CustomerChangeLog
{
    [Key]
    public long LogId { get; set; }

    public int CustomerId { get; set; }

    [MaxLength(100)]
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public int ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string? Reason { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(ChangedBy))]
    public virtual User? ChangedByUser { get; set; }
}