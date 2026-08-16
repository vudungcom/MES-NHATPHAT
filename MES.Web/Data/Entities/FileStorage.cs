using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

/// <summary>
/// Master data quản lý các ổ/folder lưu file.
/// Khi ổ đầy: thêm StorageId mới với IsDefault=true, ổ cũ chuyển Status=ReadOnly.
/// File cũ KHÔNG cần di chuyển.
/// </summary>
public class FileStorage
{
    public int StorageId { get; set; }

    [Required, MaxLength(20)]
    public string StorageCode { get; set; } = "";  // 'STG01', 'STG02'

    [Required, MaxLength(500)]
    public string BasePath { get; set; } = "";  // '\\server\mes-files-01\' hoặc 'C:\\mes-files\\'

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Active";  // Active / ReadOnly / Archived

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class FileAttachment
{
    public long FileId { get; set; }

    public int StorageId { get; set; }
    public FileStorage? Storage { get; set; }

    [Required, MaxLength(500)]
    public string RelativePath { get; set; } = "";
    // 'receiving/2026/08/14/GR-20260814-0001/img01.jpg'

    [Required, MaxLength(200)]
    public string FileName { get; set; } = "";

    public long FileSize { get; set; }

    [MaxLength(50)]
    public string? MimeType { get; set; }

    [Required, MaxLength(30)]
    public string SourceType { get; set; } = "";  // 'KhPlan' / 'ReceivingSlip' / ...

    public long SourceId { get; set; }

    public int UploadedBy { get; set; }
    public User? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.Now;

    [MaxLength(64)]
    public string? Sha256Hash { get; set; }
}
