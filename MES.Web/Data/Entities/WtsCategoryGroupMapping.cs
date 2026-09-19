using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

/// <summary>
/// Bảng mapping: 1 CategoryCode (nhóm công đoạn WTS) ↔ nhiều ProcessGroup (GC/HTSP/KCS/PKG).
/// Cho phép 1 nhóm xuất hiện ở nhiều process group (VD: "Công việc phát sinh" thuộc cả GC lẫn HTSP).
/// Admin tự cấu hình qua trang Setting — không hardcode trong code.
/// </summary>
[Table("WtsCategoryGroupMappings")]
public class WtsCategoryGroupMapping
{
    [Key]
    public int MappingId { get; set; }

    /// <summary>
    /// Mã nhóm công đoạn WTS, khớp với StandardWtsTask.CategoryCode
    /// VD: "BAVIA", "TARO", "GIA_CONG", "DONG_GOI"...
    /// </summary>
    [Required, MaxLength(50)]
    public string CategoryCode { get; set; } = "";

    /// <summary>
    /// Tên nhóm (cache lại để hiển thị, không cần join StandardWtsTasks)
    /// </summary>
    [Required, MaxLength(150)]
    public string CategoryName { get; set; } = "";

    /// <summary>
    /// Nhóm sản xuất: "GC" | "HTSP" | "KCS" | "PKG"
    /// Khớp với ProcessGroup trong WtsProductionLog và quyền Wts* trong SystemPermissions
    /// </summary>
    [Required, MaxLength(10)]
    public string ProcessGroup { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }
    [ForeignKey(nameof(CreatedBy))]
    public User? CreatedByUser { get; set; }
}
