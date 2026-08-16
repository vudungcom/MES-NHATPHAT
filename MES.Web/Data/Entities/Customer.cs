using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

public class Customer
{
    public int CustomerId { get; set; }

    [Required, MaxLength(20)]
    public string CustomerCode { get; set; } = "";

    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = "";

    [MaxLength(20)]
    public string? SupplierCode { get; set; }  // Mã của Nhật Phát bên hệ thống khách

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
