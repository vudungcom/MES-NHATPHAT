using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities;

public class DoGa
{
    [Key]
    public int DoGaId { get; set; }

    [Required, MaxLength(150)]
    public string TenDoGa { get; set; } = ""; // Cột B: Tên đồ gá (Key chuẩn để link sang bảng khác)

    [MaxLength(50)]
    public string? SoLuong { get; set; } = "1";

    // Phụ kiện kèm theo
    [MaxLength(250)]
    public string? BuLong { get; set; }

    [MaxLength(250)]
    public string? BuLongDinhVi { get; set; }

    [MaxLength(250)]
    public string? Chot { get; set; }

    [MaxLength(250)]
    public string? Dem { get; set; }

    [MaxLength(250)]
    public string? Kep { get; set; }

    [MaxLength(250)]
    public string? LongDen { get; set; }

    [MaxLength(250)]
    public string? SanPhamSuDung { get; set; }

    [MaxLength(500)]
    public string? GhiChu { get; set; }

    // Trạng thái vận hành & mượn trả hiện tại
    [Required, MaxLength(50)]
    public string TrangThai { get; set; } = "Sẵn sàng"; // "Sẵn sàng", "Đang mượn", "Bảo trì", "Hỏng"

    [MaxLength(150)]
    public string? NguoiMuonHienTai { get; set; }

    public DateTime? NgayMuonHienTai { get; set; }

    public DateTime? NgayTraDuKien { get; set; }

    [MaxLength(500)]
    public string? LyDoMuonHienTai { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}