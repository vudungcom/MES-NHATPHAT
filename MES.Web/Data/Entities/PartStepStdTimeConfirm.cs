using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Data.Entities;

/// <summary>
/// Xác nhận T.Chuẩn (StandardTime) per-dòng cho các bước Taro / Bavia / Washing / Inspection / Packaging.
/// Tương đương PartMachiningTimingConfirms của Vùng B nhưng dùng chung cho 5 bảng bước còn lại,
/// nên StepId là LOOSE reference (KHÔNG có FK) và phân biệt bảng bằng StepTable.
/// Append-only: toggle = thêm bản ghi mới với IsConfirmed đảo ngược; bản ghi mới nhất là trạng thái hiện tại.
/// </summary>
[Table("PartStepStdTimeConfirms")]
[Index(nameof(StepTable), nameof(StepId))]
[Index(nameof(PartId))]
public class PartStepStdTimeConfirm
{
    [Key]
    public long Id { get; set; }

    /// <summary>'Taro' | 'Bavia' | 'Washing' | 'Inspection' | 'Packaging'</summary>
    [Required]
    [MaxLength(20)]
    public string StepTable { get; set; } = string.Empty;

    /// <summary>StepId trong bảng tương ứng. LOOSE reference — không FK.</summary>
    public long StepId { get; set; }

    public int PartId { get; set; }

    public bool IsConfirmed { get; set; } = true;

    public int ConfirmedBy { get; set; }

    [ForeignKey(nameof(ConfirmedBy))]
    public User? ConfirmedByUser { get; set; }

    public DateTime ConfirmedAt { get; set; } = DateTime.Now;
}
