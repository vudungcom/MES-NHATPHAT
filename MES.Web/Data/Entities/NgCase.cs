using System;

namespace MES.Web.Data.Entities
{
    /// <summary>
    /// [VULOI 2026-10-28] Vụ lỗi = 1 lần PHÁT SINH lỗi (không phải 1 phiếu). Phần mềm tự đặt mã (VL000123).
    /// Bảng này CHỈ là nhãn (loại lỗi, nơi phát hiện, nơi gây lỗi) — KHÔNG lưu số lượng (L6):
    /// số của vụ do HandoverLedgerService tính lại từ chứng từ mang cột NgCaseId
    /// (WtsProductionLogs, HandoverReceives, HandoverNgReports, HandoverTransactions).
    /// 1 vụ chỉ 1 loại lỗi. 1 phiếu giao hàng lỗi = 1 vụ.
    /// </summary>
    public class NgCase
    {
        public long NgCaseId { get; set; }
        /// <summary>Cột tính sẵn trên SQL ('VL' + 6 số) — chỉ để truy vấn SQL. Code luôn dùng NgCaseNo.Format(NgCaseId).</summary>
        public string? CaseNo { get; set; }
        public int KhPlanDetailId { get; set; }
        public int NgCategoryId { get; set; }

        /// <summary>WTS | NHAN (NG đếm lúc nhận) | NHANLOI (nhận vào ô Hàng lỗi / Hàng sửa) | BAOLOI (báo lỗi sau khi nhận) | TRALOI (phiếu hàng lỗi công đoạn trước)</summary>
        public string OriginKind { get; set; } = string.Empty;
        /// <summary>Nơi phát hiện: KHO | GC | HTSP | KCS | PKG</summary>
        public string DetectGroupCode { get; set; } = string.Empty;
        /// <summary>NC (Gia công) / mã CV nơi phát hiện, nếu có.</summary>
        public string? DetectNC { get; set; }
        /// <summary>Nơi gây lỗi: KHACH | KHO | GC | HTSP | KCS | PKG (null = chưa rõ).</summary>
        public string? CauseGroupCode { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public KhPlanDetail KhPlanDetail { get; set; } = null!;
        public NgCategory NgCategory { get; set; } = null!;
    }

    /// <summary>[VULOI] Mã vụ hiển thị.</summary>
    public static class NgCaseNo
    {
        /// <summary>Tên gọi hiện cho xưởng — đổi chữ ở đây là đổi mọi màn.</summary>
        public const string Term = "vụ lỗi";
        public const string TermCap = "Vụ lỗi";
        public static string Format(long id) => id > 0 ? "VL" + id.ToString("D6") : id == 0 ? "chưa có mã" : "vụ mới";
    }
}
