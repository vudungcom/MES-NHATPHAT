using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

public class PartMaster
{
    public int PartId { get; set; }

    [Required, MaxLength(100)]
    public string PartNo { get; set; } = "";

    [MaxLength(200)]
    public string? PartName { get; set; }

    // ==== Legacy fields (giữ để tương thích data cũ) ====
    // Từ v0.3 trở đi, các thuộc tính này lấy từ bảng PartMasterAttribute.
    // Các field dưới đây chỉ dùng cho:
    // (1) Data cũ chưa migrate sang PartMasterAttribute
    // (2) Fallback nếu PartMasterAttribute rỗng
    // Migration script sẽ copy các giá trị này vào PartMasterAttribute làm default.
    [MaxLength(100)]
    public string? MaterialConfig { get; set; }

    [MaxLength(100)]
    public string? Material { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [MaxLength(50)]
    public string? ProcessNo { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    [MaxLength(50)]
    public string? LocationDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public List<PartMasterAttribute> Attributes { get; set; } = new();
}
