using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Xác nhận thời gian chuẩn per-field.
/// TimingId = null  → NC chính (PartMachiningSteps)
/// TimingId != null → máy đồng dạng (PartMachiningTimings)
/// Append-only: toggle = thêm bản ghi mới với IsConfirmed đảo ngược.
/// </summary>
[Table("PartMachiningTimingConfirms")]
public class PartMachiningTimingConfirm
{
    [Key]
    public long Id { get; set; }

    public long StepId { get; set; }

    [ForeignKey(nameof(StepId))]
    public PartMachiningStep? Step { get; set; }

    /// <summary>null = NC chính; có giá trị = máy đồng dạng cụ thể</summary>
    public long? TimingId { get; set; }

    [ForeignKey(nameof(TimingId))]
    public PartMachiningTiming? Timing { get; set; }

    public int PartId { get; set; }

    [Required]
    [MaxLength(30)]
    public string FieldName { get; set; } = "";

    public bool IsConfirmed { get; set; } = true;

    public int ConfirmedBy { get; set; }

    [ForeignKey(nameof(ConfirmedBy))]
    public User? ConfirmedByUser { get; set; }

    public DateTime ConfirmedAt { get; set; } = DateTime.Now;
}
