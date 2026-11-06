using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [CA-LOG 2026-11-06] Lịch sử sửa ca làm việc (bảng CaLamViecChangeLogs — SQL 13, Hùng chạy 06/11 17:40).
    /// Giờ ca / giờ nghỉ quyết định phút làm WTS và hiệu suất → mỗi lần Tạo / Sửa / Ẩn / Hiện / Xoá ghi 1 dòng: JSON trước / sau (kèm giờ nghỉ),
    /// ai, lúc nào. Không khoá ngoại vào CaLamViecs (giữ log cả khi ca chưa dùng bị xoá hẳn). Chỉ ghi thêm, không sửa / xoá.
    /// Nguồn: HÙNG 05/11 15:37 chốt (B5).
    /// </summary>
    public class CaLamViecChangeLog
    {
        public long LogId { get; set; }
        public int CaId { get; set; }
        /// <summary>TAO | SUA | AN | HIEN | XOA</summary>
        public string Action { get; set; } = "";
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public int ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
