using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [TARO-3T-GOP 2026-11-06] Khai báo mã GỘP trong WTS tiêu chuẩn (bảng StandardWtsTaskGops — SQL 07, Hùng chạy 06/11 21:45).
    /// 1 dòng = 1 cặp: mã gộp (vd 3T) làm THAY cho mã thành phần (vd 3T1). Mã 3T gộp 3T1 + 3T2 + 3T3 = 3 dòng.
    /// Mỗi chiếc đi 1 trong 2 cách: làm mã gộp, hoặc làm đủ các mã thành phần (sổ: OK = OK mã gộp + min OK mã thành phần).
    /// Ngưng = IsActive 0 + RemovedBy / RemovedAt (không xoá cứng, giữ ai / lúc nào). Bảng con, không CSV.
    /// PO chụp khai báo lúc tạo / làm mới quy trình (KhPlanRouteSnapshotWtsGop) — PO đã chạy không đổi theo (K6).
    /// Nguồn: KHÁCH 02/11 09:14 (anh Ngọc: 3T là cách làm gộp THAY cho 3T1 + 3T2 + 3T3); HÙNG 02/11 10:34 (khai rõ trong WTS tiêu chuẩn,
    /// không suy theo tên mã); HÙNG 05/11 15:37 OK cách tính.
    /// </summary>
    public class StandardWtsTaskGop
    {
        public int Id { get; set; }
        public string GopTaskCode { get; set; } = "";
        public string PartTaskCode { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? RemovedBy { get; set; }
        public DateTime? RemovedAt { get; set; }
    }

    /// <summary>
    /// [TARO-3T-GOP 2026-11-06] Khai báo gộp CHỤP vào PO lúc tạo / làm mới quy trình (bảng KhPlanRouteSnapshotWtsGops — SQL 07).
    /// Chỉ chụp cặp mà CẢ mã gộp lẫn mã thành phần có trong Taro của PO. Sổ giao nhận đọc bảng này (không đọc khai báo sống).
    /// </summary>
    public class KhPlanRouteSnapshotWtsGop
    {
        public long SnapshotId { get; set; }
        public int KhPlanDetailId { get; set; }
        public string GopCode { get; set; } = "";
        public string PartCode { get; set; } = "";
        public DateTime SnapshotAt { get; set; }
        public int? SnapshotBy { get; set; }
    }
}
