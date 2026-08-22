using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class DepartmentChangeLog
{
    [Key]
    public int ChangeId { get; set; }

    public int DepartmentId { get; set; }

    [Required, MaxLength(100)]
    public string FieldName { get; set; } = "";

    [MaxLength(500)]
    public string? OldValue { get; set; }

    [MaxLength(500)]
    public string? NewValue { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    public int ChangedBy { get; set; }

    [ForeignKey("ChangedBy")]
    public virtual User? ChangedByUser { get; set; }
}