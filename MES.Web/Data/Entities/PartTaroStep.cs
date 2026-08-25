using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("PartTaroSteps")]
public class PartTaroStep
{
    public long StepId { get; set; }

    [Required]
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public PartMaster? Part { get; set; }

    [Required]
    public int StepOrder { get; set; }

    [Required]
    [MaxLength(20)]
    public string NC { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? StepName { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? StandardTime { get; set; }

    public bool IsBackup { get; set; } = false;

    [MaxLength(20)]
    public string? ParentNC { get; set; }

    [Required]
    public int CreatedBy { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? UpdatedBy { get; set; }

    [ForeignKey(nameof(UpdatedBy))]
    public User? UpdatedByUser { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}