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
            public int     GioSuaAll    { get; set; }   // [ML16-DOCS 2026-10-30] dòng "Việc không ra sản phẩm" ghi PO này (kể cả đã huỷ) — có khoá ngoại, không xoá PO được
            public int     KqSuaAll     { get; set; }   // [GC-SUA-TAI-CHO 2026-11-04] Kết quả sửa ghi PO này (kể cả đã huỷ) — có khoá ngoại, không xoá / tách PO được

            /// <summary>Có bất kỳ chứng từ nào (kể cả đã huỷ) — dùng cho Xoá / Tách PO.</summary>
            public bool HasAny => PhoiNhan > 0 || WtsAll > 0 || PhieuAll > 0 || BaoLoiAll > 0 || GioSuaAll > 0 || KqSuaAll > 0;   // [E3-NGREP] + báo lỗi · [ML16-DOCS] + giờ việc không ra SP · [GC-SUA-TAI-CHO] + Kết quả sửa
            // [PG-DEAD 2026-11-03] Đã bỏ HasActiveFlow (WtsActive / PhieuActive / BaoLoiActive > 0): không còn chỗ nào gọi (tìm cả MES.Web + tools 03/11;
            //   Cập nhật quy trình nay theo K6-NOTRUN ở KhPlanService). Các số đếm Active vẫn dùng cho Text(). Nguồn: CLAUDE (PHAT-HIEN-0211 mục 3)

            public string Text()
            {
                var parts = new List<string>();
                if (PhoiNhan > 0) parts.Add($"phôi Kho đã nhận {PhoiNhan:0.##}");
                if (WtsAll > 0)   parts.Add($"{WtsAll} dòng WTS" + (WtsAll != WtsActive ? $" ({WtsAll - WtsActive} dòng đã huỷ)" : ""));
                if (PhieuAll > 0) parts.Add($"{PhieuAll} phiếu giao nhận" + (PhieuAll != PhieuActive ? $" ({PhieuAll - PhieuActive} phiếu đã huỷ)" : ""));
                if (BaoLoiAll > 0) parts.Add($"{BaoLoiAll} phiếu báo hàng lỗi" + (BaoLoiAll != BaoLoiActive ? $" ({BaoLoiAll - BaoLoiActive} phiếu đã huỷ)" : ""));   // [E3-NGREP]
                if (GioSuaAll > 0) parts.Add($"{GioSuaAll} dòng việc không ra sản phẩm (giờ sửa) ghi PO này");   // [ML16-DOCS]
                if (KqSuaAll > 0) parts.Add($"{KqSuaAll} Kết quả sửa");   // [GC-SUA-TAI-CHO]
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
                GioSuaAll    = await db.WorkerActivityLogs.CountAsync(a => a.RefKhPlanDetailId == khPlanDetailId),   // [ML16-DOCS]
                KqSuaAll     = await db.HandoverRepairResults.CountAsync(r => r.KhPlanDetailId == khPlanDetailId),   // [GC-SUA-TAI-CHO]
            };
        }

        /// <summary>[GN-K8] Khoá PO trong giao dịch đang mở (cùng tên khoá với WTS + Giao nhận). null = khoá được.</summary>
        public static async Task<string?> LockAsync(AppDbContext db, int khPlanDetailId, bool deadlockThrows = false)   // [RS-D10c] deadlockThrows: xem TryLockInTransactionAsync
        {
            // [WTS-SVC 2026-10-29] Phần sp_getapplock tách ra TryLockInTransactionAsync để trang WTS (WtsSaveService) khoá máy
            //   "WTS_MACHINE_<máy>" cùng kiểu LockOwner = Transaction. Cách khoá PO ở đây giữ nguyên.
            if (!await TryLockInTransactionAsync(db, "MES_PO_" + khPlanDetailId, deadlockThrows))
                return "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.";
            db.ChangeTracker.Clear();   // [B1-STALE 2026-10-28] có khoá rồi mới đọc: bỏ bản cũ EF đang giữ (như HandoverService.LockPoAsync)
            return null;
        }

        /// <summary>
        /// [WTS-SVC 2026-10-29] Khoá 1 tài nguyên bất kỳ (sp_getapplock, LockOwner = Transaction, chờ tối đa 10 giây) trong giao dịch
        /// đang mở — tự nhả khi Commit / Rollback. true = khoá được; false = hết giờ chờ (người khác đang giữ).
        /// Dùng cho khoá PO "MES_PO_&lt;id&gt;" (LockAsync) và khoá máy "WTS_MACHINE_&lt;máy&gt;" của WtsSaveService.
        /// </summary>
        // [RS-D10c 2026-10-31] sp_getapplock trả −3 = yêu cầu khoá bị SQL Server chọn làm "nạn nhân deadlock" (người khác đang chờ
        //   khoá mình giữ, mình chờ khoá họ giữ). Trước: gộp chung với hết giờ chờ (−1) → báo "PO đang có người khác lưu", người dùng
        //   phải bấm lại. Nay: −3 ném lỗi số 51205 (ApplockDeadlockNumber) — HandoverService.IsDeadlock nhận như 1205, nên chỗ nào có
        //   vòng tự thử lại (WtsSaveService: lưu nhóm / xoá dòng / việc không ra SP) truyền deadlockThrows = true: giao dịch Dispose =
        //   Rollback (nhả mọi khoá đang giữ) rồi tự lưu lại. Chỗ chưa có vòng thử lại (Kho, Kế hoạch, Update TG chuẩn, Đồ gá) giữ y cũ:
        //   trả false → "đang có người khác lưu" (an toàn, chưa ghi gì). −1 / −2 / −999 giữ y cũ.
        public const int ApplockDeadlockNumber = 51205;
        internal const string ApplockDeadlockSql = "IF @r = -3 THROW 51205, N'MES_APPLOCK_DEADLOCK', 1; ";

        public static async Task<bool> TryLockInTransactionAsync(AppDbContext db, string resource, bool deadlockThrows = false)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; " +
                    ApplockDeadlockSql +   // [RS-D10c]
                    "IF @r < 0 THROW 50001, N'PO đang có người khác lưu', 1;",
                    resource);
                return true;
            }
            catch (Exception ex) when (ex.Message.Contains("PO đang có người khác lưu"))
            {
                return false;
            }
            catch (Exception ex) when (!deadlockThrows && IsApplockDeadlock(ex))   // [RS-D10c] nơi gọi không tự thử lại → như cũ (chỉ −3; 1205 thật vẫn nổi lên như cũ)
            {
                return false;
            }
        }

        /// <summary>[RS-D10c] Lỗi (hoặc lỗi bên trong) là THROW 51205 của nhánh −3 ở trên.</summary>
        public static bool IsApplockDeadlock(Exception? e)
        {
            for (int depth = 0; e != null && depth < 10; e = e.InnerException, depth++)
            {
                var p = e.GetType().GetProperty("Number");
                if (p != null && p.PropertyType == typeof(int) && (int)p.GetValue(e)! == ApplockDeadlockNumber) return true;
            }
            return false;
        }

        /// <summary>[GN-K8][K5] So sổ trước / sau (sau = đọc lại trong cùng giao dịch). Chỉ Workflow ON (N7, H7 — giữ như trang Giao nhận).</summary>
        public static async Task<string?> CheckNoNewNegativeAsync(AppDbContext db, int khPlanDetailId, LedgerRow before, LedgerInput? beforeIn = null)
        {
            if (!before.ApplyWorkflow) return null;
            var afterIn = await new HandoverLedgerService(db).GetInputForDetailAsync(khPlanDetailId);
            if (afterIn == null) return null;
            var after = HandoverLedgerService.Compute(afterIn);
            var bad = HandoverLedgerService.NewNegatives(before, after);
            // [K5-VONG-SUA 2026-11-05] beforeIn != null (Kho sửa / huỷ phôi): xét thêm theo thời gian hàng sửa trả về (như HandoverService)
            if (beforeIn != null) bad.AddRange(HandoverLedgerService.NewLoopKeys(beforeIn, before, afterIn).Select(x => $"{x.Text} = {x.Value:0.##}"));
            return bad.Any()
                ? $"Không lưu: làm xong sẽ sai số ở {string.Join("; ", bad)}. Hàng này đã được dùng ở bước sau — phải huỷ / sửa từ cuối dòng ngược lên (K5, K8)."
                : null;
        }
    }
}
