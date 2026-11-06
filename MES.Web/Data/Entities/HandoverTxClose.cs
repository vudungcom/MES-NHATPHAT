using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [K5-CLOSE-NHIEU 2026-11-06] Một lần "Huỷ phần chưa nhận" của phiếu giao (bảng HandoverTxCloses — SQL 10, Hùng chạy 06/11 17:40).
    /// Trước: phiếu chỉ có 1 bộ QtyClosed / ClosedAt / ClosedBy / ClosedReason cho MỌI lần huỷ → lần 2 ghi đè giờ + người lần 1
    /// (mất truy vết) và sổ coi số huỷ lần 1 là còn giao ở các mốc cũ (chặn oan theo thời gian). Nay mỗi lần huỷ = 1 dòng; sổ đọc từng dòng.
    /// Bộ cột cũ trên phiếu VẪN ghi tổng (cùng giao dịch). Chỉ ghi thêm, không sửa / xoá.
    /// IsMigrated = 1: dòng SQL 10 chuyển từ bộ cột cũ (giờ = lần huỷ cuối). Nguồn: HÙNG 05/11 19:33 chốt A.
    /// </summary>
    public class HandoverTxClose
    {
        public long CloseId { get; set; }
        public long HandoverTxId { get; set; }
        public decimal Qty { get; set; }
        public int ClosedBy { get; set; }
        public DateTime ClosedAt { get; set; }
        public string? Reason { get; set; }
        public bool IsMigrated { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
