using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class Department
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int DepartmentId { get; set; }

    [Required, MaxLength(50)]
    public string DepartmentCode { get; set; } = "";

    [Required, MaxLength(150)]
    public string DepartmentName { get; set; } = "";

    [MaxLength(100)]
    public string? RoleTitle { get; set; }

    [MaxLength(150)]
    public string? ManagerName { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    public int? ParentId { get; set; }

    public int Level { get; set; } = 1;

    public int DisplayOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}