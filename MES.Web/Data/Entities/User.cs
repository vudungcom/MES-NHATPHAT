using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

public class User
{
    public int UserId { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = "";

    [Required, MaxLength(100)]
    public string FullName { get; set; } = "";

    [Required, MaxLength(200)]
    public string PasswordHash { get; set; } = "";

    [MaxLength(20)]
    public string? PIN { get; set; }  // 4 số, dùng sau khi có barcode scanner

    public int? GroupId { get; set; }
    public UserGroup? Group { get; set; }

    public bool IsActive { get; set; } = true;
	/// <summary>
    /// Vai trò trong group: 'Leader' | 'Normal'. NULL nếu thuộc ADMIN hoặc VIEWER.
    /// - ADMIN: NULL (đã full quyền theo GroupCode)
    /// - VIEWER: NULL (chỉ xem)
    /// - 5 group còn lại (PLANNING, WAREHOUSE, TECHNICAL, FINISHING, INSPECTION):
    ///   phải là 'Leader' hoặc 'Normal'
    /// Enforce ở service layer, không phải DB constraint.
    /// </summary>
    [MaxLength(20)]
    public string? Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }
}

public class UserGroup
{
    public int GroupId { get; set; }

    [Required, MaxLength(50)]
    public string GroupCode { get; set; } = "";  // ADMIN, PLANNING, WAREHOUSE, WORKER...

    [Required, MaxLength(100)]
    public string GroupName { get; set; } = "";

    /// <summary>JSON string chứa permissions</summary>
    public string? Permissions { get; set; }

    public bool IsActive { get; set; } = true;
}
