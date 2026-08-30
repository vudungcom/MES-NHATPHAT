using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// Danh sách máy loại trừ toàn hệ thống (global setting).
    /// Khi tính thời gian sản xuất nhóm B (Gia công), các nguyên công
    /// chạy trên máy thuộc list này sẽ không được tính vào chu kỳ chính.
    /// Áp dụng cho tất cả Part — không per-part.
    /// </summary>
    public class MachineExcludeSetting
    {
        [Key]
        public int Id { get; set; }

        /// <summary>SoMay từ bảng ThietBi — unique trên DB</summary>
        [Required]
        [MaxLength(50)]
        public string SoMay { get; set; } = "";

        public DateTime AddedAt { get; set; } = DateTime.Now;

        public int? AddedByUserId { get; set; }

        [ForeignKey(nameof(AddedByUserId))]
        public User? AddedByUser { get; set; }
    }
}
