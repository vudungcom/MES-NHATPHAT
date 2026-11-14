// [GÓI 21 14/11/2026][G17-ON-DOI-O] drive_g21.py — báo "PO vừa đổi" (HandoverChangeNotifier) sau khi harness đổi cờ Workflow thẳng DB,
//   để trang Giao nhận đang mở chạy vòng tự cập nhật [GN-LIVE] ngay. Máy thật: đổi Workflow ở Kế hoạch KHÔNG báo notifier → trang chỉ tự cập nhật
//   theo lưới 2 phút (hoặc khi PO có thao tác khác) — cùng hàm LiveApplyAsync, harness chỉ rút ngắn chờ (rà độc lập Gói 21 Thấp 3).
//   Nguồn: CLAUDE (sổ G17-GHI-NHO mục b); HÙNG 14/11 08:28 OK Gói 21.
using MES.Web.Data;
using MES.Web.Services;

public static class HarnessG21
{
    public static object Notify(AppDbContext db, string po)
    {
        var id = db.KhPlanDetails.List.First(x => x.PurchaseOrder == po).KhPlanDetailId;
        HandoverChangeNotifier.Notify(id);
        return new { ok = true, id };
    }
}
