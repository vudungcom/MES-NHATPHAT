using System;

namespace MES.Web.Services
{
    /// <summary>
    /// [GN-LIVE 2026-10-24] Báo "PO vừa đổi số liệu Giao nhận" cho mọi màn Giao nhận đang mở (mọi người dùng, mọi máy).
    /// Khách báo: GC đã nhận nhưng màn của Kho vẫn "chờ nhận" tới khi F5 — vì trang chỉ đọc sổ lúc mở và khi chính người đó bấm.
    /// Ứng dụng chạy 1 tiến trình máy chủ (Blazor Server) nên dùng sự kiện trong bộ nhớ là đủ: không hỏi DB liên tục.
    /// Gọi SAU khi lưu xong (đã Commit). Trang nhận sự kiện tự gom các PO trong ~0,3 giây rồi chỉ tính lại đúng các dòng đó.
    /// Không ảnh hưởng in/out: chỉ là màn hình đọc lại sổ; mọi lần lưu vẫn kiểm trần / K5 ở máy chủ như cũ.
    /// </summary>
    public static class HandoverChangeNotifier
    {
        /// <summary>Tham số = KhPlanDetailId vừa đổi; null = nhiều PO đổi (VD nhập WTS hàng loạt) → tính lại cả bảng.</summary>
        public static event Action<int?>? Changed;

        public static void Notify(int khPlanDetailId) => Raise(khPlanDetailId);

        public static void NotifyAll() => Raise(null);

        static void Raise(int? id)
        {
            var h = Changed;
            if (h == null) return;
            foreach (Action<int?> sub in h.GetInvocationList())
            {
                try { sub(id); } catch { /* một trang lỗi không được làm hỏng thao tác lưu */ }
            }
        }
    }
}
