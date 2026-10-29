using MES.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services
{
    // ================================================================
    // [KH-GUARD 2026-10-27] Canh số lượng cho các thao tác NGOÀI trang Giao nhận / WTS
    //   (Kho sửa phôi nhận, Kế hoạch xoá / tách PO, cập nhật quy trình, bật Workflow).
    //   - LockAsync / CheckNoNewNegativeAsync: cùng cách với HandoverService.LockPoAsync / CheckNoNewNegativeAsync
    //     (khoá "MES_PO_<id>" chung với WTS + Giao nhận → không ghi chen nhau; K5/K8: lưu xong không ô nào mới âm).
    //     Không sửa HandoverService — chỉ chép cùng logic sang đây để Kho / Kế hoạch dùng.
    //   - CountDocsAsync: PO đã có chứng từ số lượng chưa (phôi nhận / WTS / phiếu giao nhận).
    // ================================================================
    public static class PoGuard
    {
        /// <summary>Số chứng từ số lượng của 1 PO. "Tất cả" gồm cả dòng đã huỷ (vẫn là lịch sử, không được xoá).</summary>
        public sealed class PoDocCount
        {
            public decimal PhoiNhan    { get; set; }   // Σ SL thực nhận các dòng Kho đang dùng
            public int     WtsAll      { get; set; }
            public int     WtsActive   { get; set; }
            public int     PhieuAll    { get; set; }
            public int     PhieuActive { get; set; }
            public int     BaoLoiAll    { get; set; }   // [E3-NGREP 2026-10-28] phiếu báo hàng lỗi sau nhận (HandoverNgReports)
            public int     BaoLoiActive { get; set; }

            /// <summary>Có bất kỳ chứng từ nào (kể cả đã huỷ) — dùng cho Xoá / Tách PO.</summary>
            public bool HasAny => PhoiNhan > 0 || WtsAll > 0 || PhieuAll > 0 || BaoLoiAll > 0;   // [E3-NGREP] + báo lỗi
            /// <summary>Đang có số chạy (WTS / phiếu chưa huỷ) — dùng cho Cập nhật quy trình (K6).</summary>
            public bool HasActiveFlow => WtsActive > 0 || PhieuActive > 0 || BaoLoiActive > 0;   // [E3-NGREP] + báo lỗi

            public string Text()
            {
                var parts = new List<string>();
                if (PhoiNhan > 0) parts.Add($"phôi Kho đã nhận {PhoiNhan:0.##}");
                if (WtsAll > 0)   parts.Add($"{WtsAll} dòng WTS" + (WtsAll != WtsActive ? $" ({WtsAll - WtsActive} dòng đã huỷ)" : ""));
                if (PhieuAll > 0) parts.Add($"{PhieuAll} phiếu giao nhận" + (PhieuAll != PhieuActive ? $" ({PhieuAll - PhieuActive} phiếu đã huỷ)" : ""));
                if (BaoLoiAll > 0) parts.Add($"{BaoLoiAll} phiếu báo hàng lỗi" + (BaoLoiAll != BaoLoiActive ? $" ({BaoLoiAll - BaoLoiActive} phiếu đã huỷ)" : ""));   // [E3-NGREP]
                return string.Join(", ", parts);
            }
        }

        public static async Task<PoDocCount> CountDocsAsync(AppDbContext db, int khPlanDetailId)
        {
            long? idL = khPlanDetailId;
            var phoi = await db.KhoVatLieus
                .Where(k => k.PlanDetailId == idL && k.IsActive)
                .Select(k => k.SoLuongThucNhan ?? 0)
                .ToListAsync();
            return new PoDocCount
            {
                PhoiNhan    = phoi.Sum(x => (decimal)x),
                WtsAll      = await db.WtsProductionLogs.CountAsync(w => w.KhPlanDetailId == khPlanDetailId),
                WtsActive   = await db.WtsProductionLogs.CountAsync(w => w.KhPlanDetailId == khPlanDetailId && !w.IsVoided),
                PhieuAll    = await db.HandoverTransactions.CountAsync(t => t.KhPlanDetailId == khPlanDetailId),
                PhieuActive = await db.HandoverTransactions.CountAsync(t => t.KhPlanDetailId == khPlanDetailId && !t.IsVoided),
                BaoLoiAll    = await db.HandoverNgReports.CountAsync(b => b.KhPlanDetailId == khPlanDetailId),                 // [E3-NGREP]
                BaoLoiActive = await db.HandoverNgReports.CountAsync(b => b.KhPlanDetailId == khPlanDetailId && !b.IsVoided),
            };
        }

        /// <summary>[GN-K8] Khoá PO trong giao dịch đang mở (cùng tên khoá với WTS + Giao nhận). null = khoá được.</summary>
        public static async Task<string?> LockAsync(AppDbContext db, int khPlanDetailId)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; " +
                    "IF @r < 0 THROW 50001, N'PO đang có người khác lưu', 1;",
                    "MES_PO_" + khPlanDetailId);
                db.ChangeTracker.Clear();   // [B1-STALE 2026-10-28] có khoá rồi mới đọc: bỏ bản cũ EF đang giữ (như HandoverService.LockPoAsync)
                return null;
            }
            catch (Exception ex) when (ex.Message.Contains("PO đang có người khác lưu"))
            {
                return "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.";
            }
        }

        /// <summary>[GN-K8][K5] So sổ trước / sau (sau = đọc lại trong cùng giao dịch). Chỉ Workflow ON (N7, H7 — giữ như trang Giao nhận).</summary>
        public static async Task<string?> CheckNoNewNegativeAsync(AppDbContext db, int khPlanDetailId, LedgerRow before)
        {
            if (!before.ApplyWorkflow) return null;
            var after = await new HandoverLedgerService(db).GetLedgerForDetailAsync(khPlanDetailId);
            if (after == null) return null;
            var bad = HandoverLedgerService.NewNegatives(before, after);
            return bad.Any()
                ? $"Không lưu: làm xong sẽ sai số ở {string.Join("; ", bad)}. Hàng này đã được dùng ở bước sau — phải huỷ / sửa từ cuối dòng ngược lên (K5, K8)."
                : null;
        }
    }
}
