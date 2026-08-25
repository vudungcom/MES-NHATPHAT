using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class User
{
    [Key]
    public int UserId { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = "";

    [Required, MaxLength(100)]
    public string FullName { get; set; } = "";

    [Required, MaxLength(200)]
    public string PasswordHash { get; set; } = "";

    [MaxLength(20)]
    public string? PIN { get; set; }  

    public int? GroupId { get; set; }
    
    [ForeignKey("GroupId")]
    public virtual UserGroup? Group { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
}

public class UserGroup
{
    [Key]
    public int GroupId { get; set; }

    [Required, MaxLength(50)]
    public string GroupCode { get; set; } = "";  // ADMIN, KY_THUAT, LEADER_KT...

    [Required, MaxLength(100)]
    public string GroupName { get; set; } = "";

    /// <summary>
    /// Chuỗi JSON lưu trữ danh sách GroupPermissionSetting
    /// </summary>
    public string? Permissions { get; set; }

    public bool IsActive { get; set; } = true;
}