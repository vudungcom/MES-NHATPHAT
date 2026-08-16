using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

/// <summary>
/// Lưu nhiều giá trị cho 1 thuộc tính của Part (versioning).
/// Mỗi Part có nhiều dòng attribute, mỗi AttributeType có tối đa 1 IsDefault=true.
/// 
/// Ví dụ Part 4A-BE7364810 có thể có:
///   - (Material, "MSP-12-A5052-MID", IsDefault=true)
///   - (Material, "AL6061-T6", IsDefault=false)
///   - (MaterialConfig, "B12x82x97", IsDefault=true)
///   - (MaterialConfig, "B12x85x100", IsDefault=false)
///   - (Location, "B1", IsDefault=true)
///   - (MaterialNote, "Ghi chú lần 1...", IsDefault=true)
/// 
/// Khi tạo phiếu KH và chọn Part cũ → auto-fill giá trị IsDefault của từng AttributeType.
/// User có thể chọn giá trị khác từ dropdown, hoặc nhập giá trị mới.
/// Nếu nhập giá trị mới KHÁC default → cảnh báo. Có option "đặt làm default mới" (cần PIN).
/// 
/// Sau này muốn thêm thuộc tính mới (VD "Coating", "Heat treatment") chỉ cần
/// thêm giá trị mới cho AttributeType, không cần đổi schema.
/// </summary>
public class PartMasterAttribute
{
    public long AttributeId { get; set; }

    public int PartId { get; set; }
    public PartMaster? Part { get; set; }

    /// <summary>
    /// Loại thuộc tính: "Material" / "MaterialConfig" / "Location" / "MaterialNote"
    /// Có thể thêm loại mới sau này không cần đổi schema.
    /// </summary>
    [Required, MaxLength(50)]
    public string AttributeType { get; set; } = "";

    [Required, MaxLength(500)]
    public string Value { get; set; } = "";

    /// <summary>
    /// Giá trị default để auto-fill khi tạo phiếu KH mới.
    /// Chỉ tối đa 1 giá trị IsDefault=true per (PartId, AttributeType).
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public int CreatedBy { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
