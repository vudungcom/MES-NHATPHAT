using MES.Web.Data.Entities;    // ← THÊM DÒNG NÀY
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng audit log chung cho 5 bảng công đoạn Part Master.
/// APPEND-ONLY: KHÔNG bao giờ UPDATE hoặc DELETE dòng trong bảng này.
/// LOOSE REFERENCE: cột StepId trỏ tới bảng tương ứng theo StepTable
/// nhưng KHÔNG có FK constraint (giống ChangeLog v0.5.1) — để khi soft/hard delete
/// vẫn giữ được audit trail.
/// </summary>
[Table("PartProcessStepChangeLogs")]
public class PartProcessStepChangeLog
{
    // PK khai báo trong AppDbContext.OnModelCreating bằng HasKey(x => x.ChangeId)
    public long ChangeId { get; set; }

    /// <summary>Phân biệt log thuộc bảng nào. Giá trị: 'Machining' | 'Taro' | 'Bavia' | 'Washing' | 'Inspection'.</summary>
    [Required]
    [MaxLength(20)]
    public string StepTable { get; set; } = string.Empty;

    /// <summary>ID của dòng trong bảng tương ứng. LOOSE reference — KHÔNG có FK.</summary>
    [Required]
    public long StepId { get; set; }

    /// <summary>PartId — redundant để query nhanh mọi log liên quan 1 Part.</summary>
    [Required]
    public int PartId { get; set; }

    /// <summary>
    /// Tên field bị sửa (VD: 'NC', 'Drawing', 'StepName').
    /// Hoặc action đặc biệt: 'Created' | 'Deleted' (soft) | 'Restored'.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    [Required]
    public int ChangedBy { get; set; }

    [ForeignKey(nameof(ChangedBy))]
    public User? ChangedByUser { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    /// <summary>Bắt buộc, min 3 ký tự — enforce ở service layer.</summary>
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Hằng số giá trị hợp lệ cho PartProcessStepChangeLog.StepTable.
/// Dùng trong service để tránh magic string.
/// </summary>
public static class ProcessStepTable
{
    public const string Machining = "Machining";
    public const string Taro = "Taro";
    public const string Bavia = "Bavia";
    public const string Washing = "Washing";
    public const string Inspection = "Inspection";
}

/// <summary>
/// Hằng số cho FieldName đặc biệt (action, không phải field thực).
/// </summary>
public static class ProcessStepAction
{
    public const string Created = "Created";
    public const string Deleted = "Deleted";
    public const string Restored = "Restored";
}
