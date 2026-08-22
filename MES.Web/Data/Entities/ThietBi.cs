using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class ThietBi
{
    [Key]
    public int ThietBiId { get; set; }

    [Required, MaxLength(50)]
    public string SoMay { get; set; } = "";

    [MaxLength(100)]
    public string BoPhan { get; set; } = "";

    [MaxLength(100)]
    public string MaMay { get; set; } = "";

    [MaxLength(100)]
    public string TenMay { get; set; } = "";

    [MaxLength(100)]
    public string LoaiMay { get; set; } = "";

    [MaxLength(50)]
    public string TrangThai { get; set; } = "Hoạt động";

    public bool IsActive { get; set; } = true; 
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }

    // Trường động phục vụ hiển thị trực tiếp trên danh sách
    [NotMapped]
    public string? LyDoHienTai { get; set; }

    [NotMapped]
    public DateTime? ThoiGianDungTu { get; set; }
}