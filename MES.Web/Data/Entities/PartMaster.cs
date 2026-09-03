using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

[Table("PartMasters")]
public class PartMaster
{
    [Key]
    public int PartId { get; set; }

    [Required, MaxLength(100)]
    public string PartNo { get; set; } = "";

    [MaxLength(200)]
    public string? PartName { get; set; }

    [MaxLength(100)]
    public string? MaterialConfig { get; set; }

    [MaxLength(100)]
    public string? Material { get; set; }

    public int? CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; set; }

    [MaxLength(50)]
    public string? ProcessNo { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    [MaxLength(50)]
    public string? LocationDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ngưng sử dụng — Part vẫn còn trong DB và hiện trong lịch sử,
    /// nhưng bị ẩn khỏi dropdown tạo mới và có badge "Ngưng" trong danh sách.
    /// Không ảnh hưởng các phiếu KhPlan/Kho đã tạo trước đó.
    /// </summary>
    public bool IsObsolete { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ==== CỜ TRẠNG THÁI XÁC NHẬN MASTER DATA ====
    public bool IsPlanConfirmed { get; set; } = false;      // Vùng A
    public bool IsMachiningConfirmed { get; set; } = false; // Vùng B
    public bool IsHtspConfirmed { get; set; } = false;      // Vùng C
    public bool IsKcsConfirmed { get; set; } = false;       // Vùng D
    public bool IsPkgConfirmed { get; set; } = false;       // Vùng E

    // Navigation
    public List<PartMasterAttribute> Attributes { get; set; } = new();
}
