using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services
{
    // ================================================================
    // [WTS-SVC 2026-10-29] Phần LƯU / XOÁ dòng WTS tách từ trang HieuSuat_Index.razor ra service.
    //   Dời NGUYÊN VĂN các hàm cũ của trang (chữ báo lỗi giữ y cũ, quy tắc không đổi):
    //     SaveGroupAsync (6622) → CheckGroupQtyAsync (6800) → SaveRowAsync (7286) → SaveWtsLogStdsAsync (7970),
    //     CheckMachineOverlapAsync (6139), VoidSavedRow (5153) + VoidWtsLogRawAsync (5207).
    //   Trang chỉ còn: gom ô nhập thành WtsSaveGroup / WtsSaveRow (kèm số chuẩn đã tra sẵn từ snapshot) → gọi → đổ kết quả.
    //   3 điểm KHÁC bản cũ (Hùng duyệt 29/10 theo PLAN_WTS-SAVE-SERVICE_2026-10-29.md):
    //     (1) Khoá máy + khoá PO: sp_getapplock LockOwner = Transaction qua PoGuard, trong 1 giao dịch mở từ đầu
    //         (trước: LockOwner = Session, tự mở / đóng kết nối, giao dịch chỉ bọc phần ghi). Tự nhả khi Commit / Rollback → không
    //         còn trường hợp khoá treo theo kết nối; cùng cách với HandoverService / PoGuard.
    //     (2) [RS-E14] Chặn mã WTS không có trong snapshot quy trình của PO (NC với GC, mã CV với HTSP/KCS/PKG) khi PO đã có
    //         snapshot cho nhóm — trước: WtsCap cho lưu với trần "Chưa làm cả nhóm" nhưng sổ bỏ qua dòng đó → vênh in/out.
    //     (3) Gói riêng, không đổi giao diện.
    //   Các cảnh báo chỉ hiển thị (CheckQtyWarning, "Chưa chọn máy", tổng giờ vượt ca, NC máy loại trừ) đọc cache của trang
    //   nên vẫn ở trang (HieuSuat_Index.ApplyLegacySaveWarnings) — không ghi DB, không đổi kết quả lưu.
    //   Bộ kiểm tools/GiaoNhanFuzz gọi thẳng service này (kịch bản `wts` + thao tác wts / wtsedit / wtsvoid).
    // ================================================================
    public class WtsSaveService
    {
        private readonly AppDbContext _db;
        private readonly HandoverLedgerService _ledger;
        public WtsSaveService(AppDbContext db, HandoverLedgerService ledger) { _db = db; _ledger = ledger; }

        // ── DTO: trang gom ô nhập vào đây ─────────────────────────────────────

        /// <summary>Số chuẩn GC đã tra sẵn từ snapshot phiếu KH (trang: GetStdTiming) — ghi WtsProductionLogStds.</summary>
        public sealed class WtsSaveStd
        {
            public decimal? SetupTime       { get; set; }
            public decimal? MachiningTime   { get; set; }
            public decimal? InspectionTime  { get; set; }
            public decimal? PreparationTime { get; set; }
            public decimal? TrialRunTime    { get; set; }
            /// <summary>StdTrack.Table.* của dòng snapshot đã lấy số; null = lấy từ Part Master live.</summary>
            public string?  SrcTable        { get; set; }
            public long?    SrcRowId        { get; set; }
        }

        public sealed class WtsSaveExtra
        {
            public string? WtsCode { get; set; }
            public string? WtsName { get; set; }
            public int     Minutes { get; set; }
            public string? Notes   { get; set; }
        }

        public sealed class WtsSaveRow
        {
            public long?    WtsLogId       { get; set; }
            public long?    ActivityLogId  { get; set; }
            public int?     KhPlanDetailId { get; set; }
            public DateTime StartTime      { get; set; }
            public DateTime EndTime        { get; set; }
            public int      Minutes => EndTime > StartTime ? (int)(EndTime - StartTime).TotalMinutes : -1;
            public string?  PO             { get; set; }
            public string?  NC             { get; set; }
            public string?  WtsCode        { get; set; }
            public string?  WtsName        { get; set; }
            public string?  HtspSubGroup   { get; set; }
            public string?  MachineUsed    { get; set; }
            public decimal  QtyDone        { get; set; }
            public decimal  QtyOk          { get; set; }
            public decimal  QtyNg          { get; set; }
            public int?     NgCategoryId   { get; set; }
            public long?    NgCaseId       { get; set; }
            public string?  Notes          { get; set; }
            public bool     IsOvertime     { get; set; }
            public List<WtsSaveExtra> ExtraItems { get; set; } = new();
            public int      ExtraMinutes => ExtraItems.Sum(x => x.Minutes);
            public decimal? SetupTime_Actual       { get; set; }
            public decimal? MachiningTime_Actual   { get; set; }
            public decimal? InspectionTime_Actual  { get; set; }
            public decimal? PreparationTime_Actual { get; set; }
            public decimal? TrialRunTime_Actual    { get; set; }
            public decimal? ActualTimePerPc        { get; set; }
            public string   ProductionMode         { get; set; } = "serial";
            public bool     IsEditing              { get; set; }

            // Trang tra sẵn từ snapshot (cùng hàm với phần hiển thị): GC → GcStd; non-GC → StdTimePerPc + StdSrcTable/StdSrcRowId
            public WtsSaveStd? GcStd        { get; set; }
            public decimal?    StdTimePerPc { get; set; }
            public string?     StdSrcTable  { get; set; }
            public long?       StdSrcRowId  { get; set; }

            // Kết quả
            public string?  ErrorMsg   { get; set; }
            public bool     HasWarning { get; set; }
            public string?  WarningMsg { get; set; }
            public bool     Saved      { get; set; }
        }

        public sealed class WtsSaveGroup
        {
            /// <summary>Nhóm công nhân (trang: IsGcGroup — ADMIN hoặc quyền WTS_GC).</summary>
            public bool    IsGc              { get; set; }
            /// <summary>Trang: IsHtspGroup.</summary>
            public bool    IsHtsp            { get; set; }
            /// <summary>Trang: HandoverGroupCode (GC / HTSP / KCS / PKG).</summary>
            public string  HandoverGroupCode { get; set; } = "GC";
            public string? NC                { get; set; }
            public string? MachineUsed       { get; set; }
            public string  ProductionMode    { get; set; } = "serial";
            public string  HtspSubGroup      { get; set; } = "";
            public int     UserId            { get; set; }
            public int?    CaId              { get; set; }
            public DateTime WorkDate         { get; set; }
            public List<WtsSaveRow> Rows     { get; set; } = new();
            /// <summary>Kết quả: lời báo của nhóm (null = lưu xong).</summary>
            public string? ErrorMsg          { get; set; }
        }

        public sealed class WtsSaveResult
        {
            public bool    Ok       { get; set; }
            public string? ErrorMsg { get; set; }
            /// <summary>PO đã ghi (hoặc đã ghi dở trước khi rollback) — trang nạp lại trần / số đã làm cho các PO này.</summary>
            public List<int> DetailIds { get; set; } = new();
        }

        public sealed class WtsVoidRequest
        {
            public long? WtsLogId       { get; set; }
            public long? ActivityLogId  { get; set; }
            public int?  KhPlanDetailId { get; set; }
            public int   UserId         { get; set; }
        }

        // ── Helper thuần (chép từ trang, cùng quy tắc) ────────────────────────

        // [FLAT-QTY-ZERO] Mã CHUẨN BỊ (không có khối lượng): 1V Bavia · 1T Taro · 1R Rửa · 1K KCS · 1D Đóng gói (+ 1G giữ như cũ)
        private static readonly string[] _flatTimeCodes = { "1V", "1T", "1R", "1K", "1D", "1G" };
        private static bool IsFlatTimeCode(WtsSaveRow sub)
        {
            var code = !string.IsNullOrEmpty(sub.WtsCode) ? sub.WtsCode : sub.NC;
            return !string.IsNullOrEmpty(code)
                && _flatTimeCodes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        // Dòng nhập WTS non-GC đang chọn mã chuẩn bị → không có số lượng
        private static bool IsPrepNoQty(WtsSaveGroup grp, WtsSaveRow sub) => !grp.IsGc && IsFlatTimeCode(sub);
        // [TRIAL-QTY-OPTIONAL] Không bắt buộc SL > 0 khi: mã chuẩn bị (non-GC) HOẶC GC đang "Chạy thử"
        private static bool IsQtyOptional(WtsSaveGroup grp, WtsSaveRow sub) => IsPrepNoQty(grp, sub) || (grp.IsGc && sub.ProductionMode == "trial");

        // [SERIAL-TIME-REQUIRED] / [ACTUAL-REQUIRED-NONGC] / [TRIAL-PREP-REQUIRED] — nguyên văn GetMissingSerialTimesMsg của trang
        private const bool RequireActualForPrepCodes = true;
        private static string? GetMissingSerialTimesMsg(WtsSaveGroup grp, WtsSaveRow r)
        {
            // [ACTUAL-REQUIRED-NONGC]
            if (!grp.IsGc)
            {
                if (!r.KhPlanDetailId.HasValue) return null;
                if (IsPrepNoQty(grp, r) && !RequireActualForPrepCodes) return null;
                return (r.ActualTimePerPc ?? 0) <= 0
                    ? (IsPrepNoQty(grp, r) ? "Chưa nhập T.Thực tế (tổng phút chuẩn bị)." : "Chưa nhập T.Thực tế.")
                    : null;
            }
            if (!r.KhPlanDetailId.HasValue) return null;
            // [TRIAL-PREP-REQUIRED] Chạy thử: bắt buộc T.CB (thời gian chuẩn bị) > 0; T.Chạy + SL vẫn không bắt buộc (yêu cầu KH 19/10)
            if (r.ProductionMode == "trial")
                return (r.PreparationTime_Actual ?? 0) <= 0 ? "Chạy thử: chưa nhập T.CB (thời gian chuẩn bị)." : null;
            if (r.ProductionMode != "serial") return null;
            var missing = new List<string>();
            if ((r.SetupTime_Actual      ?? 0) <= 0) missing.Add("T.Gá");
            if ((r.MachiningTime_Actual  ?? 0) <= 0) missing.Add("T.GC");
            if ((r.InspectionTime_Actual ?? 0) <= 0) missing.Add("T.Ktra");
            return missing.Count == 0 ? null : $"Hàng loạt: chưa nhập {string.Join(", ", missing)}.";
        }

        // ProcessGroup ghi trên WTS theo nhóm công nhân (trang: CurrentPg — khớp SaveRowAsync)
        private static string CurrentPg(WtsSaveGroup grp)
        {
            if (grp.IsGc) return "GC";
            if (grp.IsHtsp)
                return grp.HtspSubGroup switch { "Taro" => "TARO", "Bavia" => "BAVIA", "Rua" => "WASHING", _ => "" };
            return grp.HandoverGroupCode == "KCS" ? "KCS" : "PKG";
        }

        // [GN-WTS-CAP] Phép tính có tên từng số hạng: "Hàng vào 60 − OK đã làm 0 − NG đã làm 0 = 60" (trang GetWfNcCeiling cũng gọi)
        public static string CapDetail(HandoverLedgerService.WtsCapInfo cap)
        {
            var e = cap.Explain;
            var detail = e.Terms.Count > 0
                ? string.Join(" ", e.Terms.Select((t, i) => (i == 0 ? "" : (t.Sign < 0 ? "− " : "+ ")) + $"{t.Label} {t.Value:0.##}")) + $" = {e.Result:0.##}"
                : $"{e.Result:0.##}";
            // [GN-TEXT] bỏ phần ghi chú công thức (chữ kỹ thuật) — xem đủ ở trang Giao nhận, nút Hiện công thức
            return detail;
        }

        // ── Lưu toàn bộ nhóm (nguyên văn SaveGroupAsync của trang) ───────────────────

        public async Task<WtsSaveResult> SaveGroupAsync(WtsSaveGroup grp)
        {
            grp.ErrorMsg = null;
            var result = new WtsSaveResult();
            WtsSaveResult Fail() { result.Ok = false; result.ErrorMsg = grp.ErrorMsg; return result; }

            // Validate nhóm
            if (grp.IsGc && string.IsNullOrEmpty(grp.NC))
            { grp.ErrorMsg = "Chưa chọn NC cho nhóm."; return Fail(); }
            if (grp.IsGc && string.IsNullOrEmpty(grp.MachineUsed))
            { grp.ErrorMsg = "Chưa chọn máy sử dụng."; return Fail(); }
            // [FIX-NONGC-SUBGROUP] HTSP bắt buộc chọn nhóm nhỏ (Taro/Bavia/Rửa) — không để rơi về mặc định TARO
            if (grp.IsHtsp && string.IsNullOrEmpty(grp.HtspSubGroup))
            { grp.ErrorMsg = "Chưa chọn nhóm nhỏ (Taro / Bavia / Rửa)."; return Fail(); }
            if (!grp.Rows.Any())
            { grp.ErrorMsg = "Nhóm trống."; return Fail(); }

            // Validate từng sub-row
            foreach (var sub in grp.Rows)
            {
                sub.ErrorMsg = null;
                // [FIX-NONGC-NC] non-GC lấy NC = Mã CV của chính dòng đó (SaveRowAsync lưu row.NC vào cột WtsCode). GC giữ nguyên.
                sub.NC          = grp.IsGc ? grp.NC : sub.WtsCode;
                if (grp.IsHtsp) sub.HtspSubGroup = grp.HtspSubGroup;   // [FIX-NONGC-SUBGROUP]
                sub.MachineUsed = grp.MachineUsed;
                sub.ProductionMode = grp.ProductionMode;

                if (!sub.KhPlanDetailId.HasValue)
                { sub.ErrorMsg = "Chưa chọn số kế hoạch."; grp.ErrorMsg = "Có hàng chưa chọn KH."; return Fail(); }
                if (sub.QtyDone <= 0 && !IsQtyOptional(grp, sub))   // [FLAT-QTY-ZERO] + [TRIAL-QTY-OPTIONAL]
                { sub.ErrorMsg = "SL (OK+NG) phải > 0."; grp.ErrorMsg = "Có hàng SL = 0."; return Fail(); }

                // [SERIAL-TIME-REQUIRED] GC Hàng loạt: bắt buộc đủ T.Gá / T.GC / T.Ktra | [ACTUAL-REQUIRED-NONGC] Non-GC: bắt buộc T.Thực tế
                var _missSerial = GetMissingSerialTimesMsg(grp, sub);
                if (_missSerial != null)
                { sub.ErrorMsg = _missSerial; grp.ErrorMsg = grp.IsGc ? $"Có hàng thiếu thời gian — {_missSerial}" : "Có hàng chưa nhập T.Thực tế."; return Fail(); }   // [ACTUAL-REQUIRED-NONGC]   // [TRIAL-PREP-REQUIRED] msg chung Hàng loạt / Chạy thử

                // [VULOI][DS-LOI] NG: bắt buộc CHỌN loại lỗi trong DS Lỗi (trước: gõ lý do). Sửa dòng đã có vụ lỗi đã đi phiếu: không đổi loại lỗi.
                if (sub.QtyNg > 0 && sub.NgCategoryId == null)
                { sub.ErrorMsg = "Chọn loại lỗi (DS Lỗi)."; grp.ErrorMsg = "Có hàng NG chưa chọn loại lỗi. " + NgCaseService.MsgPickCategory; return Fail(); }
                if (sub.QtyNg > 0 && (await NgCaseService.GetCategoryForCaseAsync(_db, sub.NgCategoryId, sub.IsEditing ? sub.NgCaseId : null)) == null)
                { sub.ErrorMsg = "Loại lỗi đã ngưng dùng — chọn loại khác."; grp.ErrorMsg = sub.ErrorMsg; return Fail(); }
                if (sub.QtyNg > 0 && sub.IsEditing && sub.NgCaseId != null && sub.NgCategoryId != null
                    && await NgCaseService.CategoryChangeBlockedAsync(_db, sub.NgCaseId.Value, sub.NgCategoryId.Value) is string _catBlk)
                { sub.ErrorMsg = _catBlk; grp.ErrorMsg = _catBlk; return Fail(); }

                if (sub.EndTime <= sub.StartTime)
                { sub.ErrorMsg = "T.Kết thúc phải sau T.Bắt đầu."; grp.ErrorMsg = "Lỗi thời gian."; return Fail(); }
                // OT removed 2026-09: bỏ validate khung giờ ca — CN tự do nhập thời gian
            }

            // [WTS-SVC] (1) Mở giao dịch TRƯỚC khi khoá: khoá máy + khoá PO đều LockOwner = Transaction (PoGuard), tự nhả khi Commit / Rollback.
            //   Trước: khoá Session + tự mở / đóng kết nối, giao dịch chỉ bọc phần ghi ([B2B3-WTS-TRX]). Thứ tự kiểm giữ nguyên.
            (int DetailId, List<long> RemoveIds, List<InWts> Add)? _pendingWhy = null;   // [GN-WHY] Lưu bị chặn K5: tìm gốc rễ SAU khi nhả khoá
            var _notify = new List<int>();   // [B2B3-WTS-TRX] PO cần báo màn Giao nhận — báo sau khi ghi hẳn
            bool _allOk = false;
            await using (var _wtsTrx = await _db.Database.BeginTransactionAsync())
            {
                // [MACHINE-OVERLAP] Mục 19: 1 máy không được chạy 2 việc trùng giờ (mọi công nhân, cả Hàng loạt + Chạy thử).
                // Trùng = Bắt đầu A < Kết thúc B và Bắt đầu B < Kết thúc A (chạm mép 08:15→08:15 không tính). Trùng → chặn lưu.
                if (grp.IsGc && !string.IsNullOrEmpty(grp.MachineUsed))
                {
                    // [MACHINE-LOCK] Khoá theo máy (sp_getapplock) trước khi kiểm tra trùng → giữ đến khi ghi xong.
                    // 2 người cùng lưu 1 máy: người sau phải chờ người trước ghi xong rồi mới kiểm tra → thấy log mới → bị chặn.
                    // Máy khác nhau không chờ nhau. Logic lưu bên dưới giữ nguyên.
                    if (!await PoGuard.TryLockInTransactionAsync(_db, "WTS_MACHINE_" + grp.MachineUsed))
                    {
                        grp.ErrorMsg = $"Máy {grp.MachineUsed} đang có người khác lưu WTS. Vui lòng bấm Lưu lại sau vài giây.";
                        return Fail();
                    }
                    var _overlapMsg = await CheckMachineOverlapAsync(grp);
                    if (_overlapMsg != null) { grp.ErrorMsg = _overlapMsg; return Fail(); }
                }

                // [GN-WTS-CAP] Kiểm SL theo sổ giao nhận — MỌI nhóm (GC / HTSP / KCS / Đóng gói), từng PO,
                // TRƯỚC khi void dòng cũ hay lưu dòng nào.
                var _capErr = await CheckGroupQtyAsync(grp, r => _pendingWhy = r);
                if (_capErr != null) { grp.ErrorMsg = _capErr; }
                else
                {
                    // Lưu từng sub-row
                    // [B2B3-WTS-TRX 2026-10-28] Cả nhóm lưu trong 1 giao dịch: 1 dòng lỗi → không dòng nào được ghi (kể cả vụ lỗi vừa tạo),
                    //   dòng đang sửa giữ nguyên dòng cũ. Trạng thái từng dòng trên form trả về như trước khi bấm Lưu.
                    var _rowSnap = grp.Rows.Select(s => (Row: s, s.WtsLogId, s.ActivityLogId, s.IsEditing, s.NgCaseId, s.HasWarning, s.WarningMsg)).ToList();   // [29/10] + cảnh báo (lưu lại không cộng dồn)
                    _allOk = true;
                    foreach (var sub in grp.Rows)
                    {
                        await SaveRowAsync(grp, sub, _notify);
                        if (sub.ErrorMsg != null)
                        { grp.ErrorMsg = $"Lỗi hàng KH {sub.PO}: {sub.ErrorMsg} — chưa dòng nào trong nhóm được lưu, sửa xong bấm Lưu lại."; _allOk = false; break; }
                    }
                    if (_allOk) await _wtsTrx.CommitAsync();
                    else
                    {
                        await _wtsTrx.RollbackAsync();
                        _db.ChangeTracker.Clear();
                        foreach (var sn in _rowSnap)
                        { sn.Row.WtsLogId = sn.WtsLogId; sn.Row.ActivityLogId = sn.ActivityLogId; sn.Row.IsEditing = sn.IsEditing; sn.Row.NgCaseId = sn.NgCaseId; sn.Row.HasWarning = sn.HasWarning; sn.Row.WarningMsg = sn.WarningMsg; }
                    }
                }
            }   // chưa Commit → Dispose = Rollback, khoá máy + khoá PO nhả theo giao dịch

            result.DetailIds = _notify.Distinct().ToList();
            if (_pendingWhy is { } pw)
            {
                // [GN-WHY] Lưu bị chặn K5: chỉ đọc + tính thử sau khi đã nhả khoá
                var opDocs = new HashSet<string>(pw.RemoveIds.Select(x => "W:" + x));
                grp.ErrorMsg += await HandoverWhy.ExplainAsync(_db, pw.DetailId, HandoverWhy.OpWts(pw.RemoveIds, pw.Add), opDocs);
            }
            if (!_allOk) return Fail();

            foreach (var id in result.DetailIds) HandoverChangeNotifier.Notify(id);   // [GN-LIVE] báo sau khi đã ghi hẳn
            _db.ChangeTracker.Clear();
            result.Ok = true;
            return result;
        }

        // ── [MACHINE-OVERLAP] Kiểm tra máy trùng giờ khi lưu nhóm GC (nguyên văn CheckMachineOverlapAsync của trang) ──
        // 1) Các dòng trong cùng form với nhau (cùng máy của nhóm).
        // 2) Với log đã lưu (chưa void) của MỌI công nhân trên cùng máy — bỏ qua log cũ của dòng đang sửa (sẽ bị void).
        // So theo DateTime đầy đủ (ngày + giờ). Trả thông báo lỗi hoặc null nếu không trùng.
        public async Task<string?> CheckMachineOverlapAsync(WtsSaveGroup grp)
        {
            var machine = grp.MachineUsed!;
            var subs = grp.Rows.Where(s => s.EndTime > s.StartTime).ToList();
            if (!subs.Any()) return null;

            // 1) Trong cùng form
            for (int i = 0; i < subs.Count; i++)
            {
                for (int j = i + 1; j < subs.Count; j++)
                {
                    var a = subs[i];
                    var b = subs[j];
                    if (a.StartTime < b.EndTime && b.StartTime < a.EndTime)
                    {
                        b.ErrorMsg = $"Máy {machine}: trùng giờ với dòng {a.StartTime:HH:mm}–{a.EndTime:HH:mm} trong form.";
                        return $"Máy {machine} trùng giờ giữa các dòng đang nhập ({a.StartTime:HH:mm}–{a.EndTime:HH:mm} và {b.StartTime:HH:mm}–{b.EndTime:HH:mm}).";
                    }
                }
            }

            // 2) Với log đã lưu trong DB
            var minStart  = subs.Min(s => s.StartTime);
            var maxEnd    = subs.Max(s => s.EndTime);
            var skipIds   = subs.Where(s => s.IsEditing && s.WtsLogId.HasValue)
                                .Select(s => s.WtsLogId!.Value).ToList();
            var savedLogs = await _db.WtsProductionLogs
                .AsNoTracking()
                .Include(l => l.Worker)
                .Include(l => l.KhPlanDetail)   // [WTS-SVC] số PO đọc từ DB (trước: _allRows của trang, chỉ 1000 PO mới nhất)
                .Where(l => !l.IsVoided
                         && l.MachineUsed == machine
                         && l.StartTime < maxEnd
                         && l.EndTime   > minStart
                         && !skipIds.Contains(l.WtsLogId))
                .ToListAsync();
            if (!savedLogs.Any()) return null;

            foreach (var sub in subs)
            {
                var hit = savedLogs
                    .Where(l => l.StartTime < sub.EndTime && sub.StartTime < l.EndTime)
                    .OrderBy(l => l.StartTime)
                    .FirstOrDefault();
                if (hit == null) continue;

                var ovStart = hit.StartTime > sub.StartTime ? hit.StartTime : sub.StartTime;
                var ovEnd   = hit.EndTime   < sub.EndTime   ? hit.EndTime   : sub.EndTime;
                var po      = hit.KhPlanDetail?.PurchaseOrder ?? $"KH#{hit.KhPlanDetailId}";
                var worker  = hit.Worker?.FullName ?? $"#{hit.WorkerId}";
                var fmt     = hit.StartTime.Date == sub.StartTime.Date ? "HH:mm" : "dd/MM HH:mm";
                var msg     = $"Máy {machine} đã có log {hit.StartTime.ToString(fmt)}–{hit.EndTime.ToString(fmt)} "
                            + $"(PO {po}, NC {hit.NC}, {worker}) — trùng {ovStart:HH:mm}–{ovEnd:HH:mm}. Không cho lưu.";
                sub.ErrorMsg = msg;
                return msg;
            }
            return null;
        }

        // ── [GN-WTS-CAP] Kiểm SL 1 nhóm WTS theo sổ giao nhận, từng PO (nguyên văn CheckGroupQtyAsync của trang) ─────
        // Workflow OFF (N7): chỉ chặn mỗi dòng ≤ SL PO (giữ như cũ).
        // Workflow ON:
        //   1) Khoá theo PO (sp_getapplock "MES_PO_<id>") đến khi lưu xong — 2 người lưu cùng PO thì người sau chờ,
        //      rồi kiểm lại trên số mới (K8).
        //   2) Trần L3: tổng OK + NG trong form của mỗi NC / mã CV ≤ Còn chờ làm (sổ đọc MỚI từ DB, bỏ dòng đang sửa).
        //   3) K5/K8: thử "lưu xong" (bỏ dòng đang sửa + thêm dòng mới) — ô nào mới âm thì chặn
        //      (vd sửa giảm OK NC1 khi NC2 / nhóm sau đã dùng số đó).
        // [RS-E14] Mọi Workflow: mã không có trong snapshot quy trình của PO (khi PO đã có snapshot cho nhóm) → chặn.
        // Trả null = qua; khác null = lời báo, chưa có gì bị void / lưu.
        private async Task<string?> CheckGroupQtyAsync(WtsSaveGroup grp, Action<(int, List<long>, List<InWts>)> setPendingWhy)
        {
            var pg = CurrentPg(grp);
            string? CodeOf(WtsSaveRow s) => grp.IsGc ? grp.NC : (s.WtsCode ?? s.NC);

            // Dòng đang sửa: lấy PO gốc của log cũ từ DB (người dùng có thể đã đổi PO trên dòng)
            var editIdsAll = grp.Rows.Where(s => s.IsEditing && s.WtsLogId.HasValue).Select(s => s.WtsLogId!.Value).ToList();
            var editLogs = editIdsAll.Any()
                ? await _db.WtsProductionLogs.AsNoTracking().Where(l => editIdsAll.Contains(l.WtsLogId))
                          .Select(l => new { l.WtsLogId, l.KhPlanDetailId }).ToListAsync()
                : new();

            var detIds = grp.Rows.Where(s => s.KhPlanDetailId.HasValue).Select(s => s.KhPlanDetailId!.Value)
                            .Concat(editLogs.Select(l => l.KhPlanDetailId))
                            .Distinct().OrderBy(x => x).ToList();   // thứ tự cố định → khoá không chéo nhau

            foreach (var id in detIds)
            {
                // [B4-WF-DB 2026-10-28] Khoá PO trước, rồi đọc PO + cờ Workflow MỚI từ DB.
                // [WTS-SVC] Số PO cho lời báo "đang có người khác lưu" đọc từ DB (trước: _allRows của trang).
                if (!await PoGuard.TryLockInTransactionAsync(_db, "MES_PO_" + id))
                {
                    var poName = await _db.KhPlanDetails.AsNoTracking().Where(d => d.KhPlanDetailId == id).Select(d => d.PurchaseOrder).FirstOrDefaultAsync();
                    return $"PO {poName ?? $"KH#{id}"} đang có người khác lưu WTS / giao nhận. Bấm Lưu lại sau vài giây.";
                }
                _db.ChangeTracker.Clear();   // [B1-STALE] có khoá rồi mới đọc (như PoGuard.LockAsync)
                var det  = await _db.KhPlanDetails.AsNoTracking().Where(d => d.KhPlanDetailId == id)
                    .Select(d => new { d.PurchaseOrder, d.Quantity, d.Unit, ApplyWorkflow = d.KhPlan != null && d.KhPlan.ApplyWorkflow })
                    .FirstOrDefaultAsync();
                var po   = det?.PurchaseOrder ?? $"KH#{id}";
                var subs = grp.Rows.Where(s => s.KhPlanDetailId == id && s.QtyDone > 0 && !IsPrepNoQty(grp, s)).ToList();

                var editIds = editLogs.Where(l => l.KhPlanDetailId == id).Select(l => l.WtsLogId).ToList();
                var before  = await _ledger.GetLedgerForDetailAsync(id);
                var basis   = editIds.Any() ? await _ledger.GetLedgerForDetailAsync(id, editIds) : before;

                // [RS-E14 2026-10-29] Mã WTS phải có trong snapshot quy trình của PO (NC với GC — kể cả máy đồng dạng đã gộp về NC chính;
                //   mã CV với HTSP/KCS/PKG). PO đã có snapshot cho nhóm mà mã không có → sổ giao nhận bỏ qua dòng này (OK không tính,
                //   NG vẫn tính) → vênh in/out. Trước: WtsCap cho lưu với trần "Chưa làm cả nhóm". PO chưa có snapshot cho nhóm: giữ như cũ.
                //   Áp dụng cả Workflow OFF (không phải luật số lượng — mã không tồn tại trong quy trình thì không có chỗ ghi).
                if (basis != null)
                {
                    var G = basis.G(HandoverLedgerService_GroupOf(pg));
                    if (G.Steps.Any())
                        foreach (var s in grp.Rows.Where(s => s.KhPlanDetailId == id && !IsPrepNoQty(grp, s) && !string.IsNullOrEmpty(CodeOf(s))))
                        {   // (mã trống → SaveRowAsync báo "Chọn NC (nếu có PO) hoặc Mã WTS khác" như cũ)
                            var cp = HandoverLedgerService.WtsCap(basis, pg, CodeOf(s));
                            if (cp.Max == null) continue;   // mã chuẩn bị
                            if (!G.Steps.Any(l => l.Pg == pg && string.Equals(l.Code, cp.Code, StringComparison.OrdinalIgnoreCase)))
                            {
                                var what = grp.IsGc ? $"NC {cp.Code}" : $"mã {cp.Code}";
                                // HTSP: PO có snapshot Taro nhưng không có Bavia/Rửa (hoặc ngược lại) → nói rõ "chưa có quy trình nhóm nhỏ" thay vì "sai mã"
                                var msg = !G.Steps.Any(l => l.Pg == pg)
                                    ? $"PO {po} · {what}: PO chưa có quy trình {HtspSubLabel(pg)} trong snapshot — không lưu được. Nhờ Kế hoạch cập nhật quy trình."
                                    : $"PO {po} · {what}: không có trong quy trình (snapshot) của PO — không lưu được. Chọn lại {(grp.IsGc ? "NC" : "mã CV")} hoặc nhờ Kế hoạch cập nhật quy trình.";
                                s.ErrorMsg = msg;
                                return msg;
                            }
                        }
                }

                if (det?.ApplyWorkflow != true)
                {
                    var over = subs.FirstOrDefault(s => det != null && s.QtyDone > det.Quantity);
                    if (over != null)
                    {
                        over.ErrorMsg = $"Tổng OK+NG ({over.QtyDone:0.##}) vượt KH ({det!.Quantity:0.##} {det.Unit}).";
                        return $"PO {po}: dòng {over.QtyDone:0.##} vượt SL PO {det.Quantity:0.##} (Workflow OFF chỉ chặn mỗi dòng ≤ PO).";
                    }
                    continue;
                }

                if (before == null || basis == null) continue;

                // 2) Trần từng NC / mã CV
                foreach (var g in subs.GroupBy(s => HandoverLedgerService.WtsCap(basis, pg, CodeOf(s)).Code, StringComparer.OrdinalIgnoreCase))
                {
                    var cap = HandoverLedgerService.WtsCap(basis, pg, CodeOf(g.First()));
                    if (cap.Max == null) continue;   // mã chuẩn bị
                    var tot = g.Sum(s => s.QtyDone);
                    var what = grp.IsGc ? $"NC {cap.Code}" : $"mã {cap.Code}";
                    if (cap.Locked || tot > cap.Max.Value)
                    {
                        var msg = cap.Locked
                            ? $"PO {po} · {what}: chưa nhận hàng — chưa nhập số lượng được."   // [GN-TEXT]
                            : $"PO {po} · {what}: nhập {tot:0.##} (OK + NG) vượt Chưa làm {cap.Max.Value:0.##}.";   // [GN-TEXT]
                        msg += $" ({CapDetail(cap)})";
                        foreach (var s in g) s.ErrorMsg = msg;
                        return msg;
                    }
                }

                // 3) Thử lưu xong — ô nào mới âm thì chặn (K5, K8)
                // [VULOI] dòng sửa giữ vụ cũ (NG giảm mà vụ đã đi phiếu → ô vụ âm → chặn); dòng mới = vụ mới (Id âm)
                var extra = subs.Select(s => new InWts(-1, DateTime.Now, pg, grp.IsGc ? grp.NC : null, grp.IsGc ? null : CodeOf(s), s.QtyOk, s.QtyNg, null,
                                                       s.QtyNg > 0 ? (s.IsEditing ? s.NgCaseId : null) ?? -1 : null)).ToList();
                var after = await _ledger.GetLedgerForDetailAsync(id, editIds, extra);
                if (after == null) continue;
                var bad = HandoverLedgerService.NewNegatives(before, after);
                if (bad.Any())
                {
                    setPendingWhy((id, editIds.ToList(), extra));   // [GN-WHY] tìm gốc rễ sau khi nhả khoá (SaveGroupAsync)
                    return $"PO {po}: lưu xong sẽ làm sai số ở {string.Join("; ", bad)}. Kiểm lại số lượng (nhóm sau / NC sau có thể đã dùng số này — K5).";
                }
            }
            return null;
        }

        private static string HtspSubLabel(string pg) => pg switch { "TARO" => "Taro", "BAVIA" => "Bavia", "WASHING" => "Rửa", "KCS" => "KCS", "PKG" => "Đóng gói", _ => pg };

        // Nhóm giao nhận của ProcessGroup (GC | TARO/BAVIA/WASHING → HTSP | KCS | PKG)
        private static string HandoverLedgerService_GroupOf(string pg) => pg switch
        {
            "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", "PKG" => "PKG", _ => ""
        };

        // ── Lưu 1 dòng (nguyên văn SaveRowAsync của trang; cảnh báo chỉ hiển thị để lại trang) ───────
        private async Task SaveRowAsync(WtsSaveGroup grp, WtsSaveRow row, List<int> notify)
        {
            row.ErrorMsg = null;
            if (grp.WorkDate.Date != DateTime.Today)
            { row.ErrorMsg = "Chỉ được nhập WTS cho ngày hôm nay. Ngày cũ cần quyền cao hơn."; return; }

            // Validate chung
            if (row.Minutes <= 0)
            { row.ErrorMsg = "T.Kết thúc phải sau T.Bắt đầu"; return; }
            if (string.IsNullOrEmpty(row.NC) && string.IsNullOrEmpty(row.WtsCode))
            { row.ErrorMsg = "Chọn NC (nếu có PO) hoặc Mã WTS khác"; return; }

            // Validate SL > 0 — áp dụng cho TẤT CẢ nhóm (GC, HTSP, KCS, PKG)
            if (row.QtyDone <= 0 && row.KhPlanDetailId.HasValue && !IsQtyOptional(grp, row))   // [FLAT-QTY-ZERO] + [TRIAL-QTY-OPTIONAL]
            { row.ErrorMsg = "SL (OK + NG) phải lớn hơn 0."; return; }

            // [SERIAL-TIME-REQUIRED] GC Hàng loạt: bắt buộc đủ T.Gá / T.GC / T.Ktra | [ACTUAL-REQUIRED-NONGC] Non-GC: bắt buộc T.Thực tế — kiểm tra TRƯỚC khi void bản ghi cũ
            var _missSerialRow = GetMissingSerialTimesMsg(grp, row);
            if (_missSerialRow != null)
            { row.ErrorMsg = _missSerialRow; return; }

            // Nếu đang sửa dòng cũ → void record cũ trước
            if (row.IsEditing)
            {
                // [D8-WTS-VOID 2026-10-28] Chỉ huỷ dòng còn hiệu lực; không huỷ được (tab khác đã sửa / xoá dòng này) thì dừng, không ghi dòng mới
                //   (trước: 2 tab cùng sửa 1 dòng → cả 2 bản mới cùng hiệu lực, số nhân đôi).
                if (row.WtsLogId.HasValue)
                {
                    var _nVoid = await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE WtsProductionLogs SET IsVoided=1, VoidReason=N'Sửa lại', VoidedBy={0}, VoidedAt={1} WHERE WtsLogId={2} AND IsVoided=0",
                        grp.UserId, DateTime.Now, row.WtsLogId.Value);
                    if (_nVoid != 1)
                    { row.ErrorMsg = "Dòng WTS này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại."; return; }
                }
                if (row.ActivityLogId.HasValue)
                {
                    var _nVoidA = await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE WorkerActivityLogs SET IsVoided=1, VoidReason=N'Sửa lại', VoidedBy={0}, VoidedAt={1} WHERE ActivityLogId={2} AND IsVoided=0",
                        grp.UserId, DateTime.Now, row.ActivityLogId.Value);
                    if (_nVoidA != 1)
                    { row.ErrorMsg = "Dòng này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại."; return; }
                }
                row.WtsLogId      = null;
                row.ActivityLogId = null;
                row.IsEditing     = false;
            }

            // ── Loại 1: Có NC → link PO → WtsProductionLog ──────
            if (!string.IsNullOrEmpty(row.NC))
            {
                if (row.KhPlanDetailId == null)
                { row.ErrorMsg = "Chọn PO để link với NC"; return; }

                // (Cảnh báo "Chưa chọn máy", tổng giờ vượt ca, CheckQtyWarning, NC máy loại trừ: chỉ hiển thị — tính ở trang)

                // v0.8b: Không block theo cascade. Chỉ block khi QtyDone > PlanQty.
                if (row.QtyDone <= 0 && !IsQtyOptional(grp, row))   // [FLAT-QTY-ZERO] + [TRIAL-QTY-OPTIONAL]
                { row.ErrorMsg = "SL (OK + NG) phải lớn hơn 0."; return; }

                if (row.QtyNg > 0 && row.NgCategoryId == null)   // [VULOI][DS-LOI] trước: kiểm lý do NG gõ tay
                { row.ErrorMsg = "Chọn loại lỗi (DS Lỗi) khi có số lượng NG."; return; }

                // [GN-WTS-CAP] Kiểm SL đã làm 1 lần trong SaveGroupAsync → CheckGroupQtyAsync, trước khi void / lưu bất kỳ dòng nào.

                try
                {
                    // Xác định ProcessGroup và NC/WtsCode theo nhóm công nhân
                    string processGroup;
                    string? ncValue;
                    string? wtsCodeValue;
                    if (grp.IsGc)
                    {
                        processGroup  = "GC";
                        ncValue       = row.NC;
                        wtsCodeValue  = null;
                    }
                    else if (grp.IsHtsp)
                    {
                        processGroup = row.HtspSubGroup switch
                        {
                            "Taro"  => "TARO",
                            "Bavia" => "BAVIA",
                            "Rua"   => "WASHING",
                            _       => "TARO"
                        };
                        ncValue      = null;
                        wtsCodeValue = row.NC;  // NC chọn từ WtsItem lưu vào WtsCode
                    }
                    else if (grp.HandoverGroupCode == "KCS")
                    {
                        processGroup  = "KCS";
                        ncValue       = null;
                        wtsCodeValue  = row.NC;
                    }
                    else // PKG
                    {
                        processGroup  = "PKG";
                        ncValue       = null;
                        wtsCodeValue  = row.NC;
                    }

                    // [VULOI 2026-10-28] NG → vụ lỗi: dòng mới tạo vụ (nơi phát hiện = nơi gây lỗi = nhóm này);
                    //   sửa dòng (void + dòng mới) thì GIỮ vụ cũ (đổi loại lỗi chỉ khi vụ chưa đi phiếu — đã kiểm ở SaveGroupAsync).
                    string? _ngLabel = null; long? _ngCaseId = null;
                    if (row.QtyNg > 0)
                    {
                        var _cat = await NgCaseService.GetCategoryForCaseAsync(_db, row.NgCategoryId, row.NgCaseId);
                        if (_cat == null) { row.ErrorMsg = NgCaseService.MsgPickCategory; return; }
                        _ngLabel = _cat.Label;
                        if (row.NgCaseId != null)
                        {
                            var _chg = await NgCaseService.ChangeCaseCategoryAsync(_db, row.NgCaseId.Value, _cat.NgCategoryId);
                            if (_chg != null) { row.ErrorMsg = _chg; return; }
                            _ngCaseId = row.NgCaseId;
                        }
                        else
                        {
                            var _ngGrp = processGroup switch { "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", _ => "PKG" };
                            var _vu = await NgCaseService.CreateCaseAsync(_db, row.KhPlanDetailId!.Value, _cat, NgOrigin.Wts,
                                _ngGrp, ncValue ?? wtsCodeValue, _ngGrp, grp.UserId);
                            _ngCaseId = _vu.NgCaseId;
                        }
                    }

                    await RefreshStdFromSnapshotAsync(grp, row, row.KhPlanDetailId.Value);   // [STD-SNAP] số chuẩn mới nhất trước khi tính IsActualManual + ghi số chuẩn
                    var log = new WtsProductionLog
                    {
                        KhPlanDetailId          = row.KhPlanDetailId.Value,
                        ProcessGroup            = processGroup,
                        NC                      = ncValue,
                        WtsCode                 = wtsCodeValue,
                        HtspSubGroup            = grp.IsHtsp ? row.HtspSubGroup : null,
                        WorkerId                = grp.UserId,
                        // [FIX-NONGC-MACHINE] Non-GC (HTSP/KCS/PKG) chưa có khái niệm máy → để NULL, cột Máy hiện "—".
                        MachineUsed             = grp.IsGc ? row.MachineUsed : null,
                        QtyDone                 = row.QtyDone,
                        QtyOk                   = row.QtyOk,
                        QtyNg                   = row.QtyNg,
                        NgReason                = row.QtyNg > 0 ? _ngLabel : null,   // [VULOI] tên loại lỗi (DS Lỗi)
                        NgCaseId                = row.QtyNg > 0 ? _ngCaseId : null,  // [VULOI] vụ lỗi (sửa dòng: giữ vụ cũ)
                        StartTime               = row.StartTime,
                        EndTime                 = row.EndTime,
                        Notes                   = row.Notes,
                        ExtraMinutes            = row.ExtraMinutes > 0 ? row.ExtraMinutes : (int?)null,
                        CaId                    = grp.CaId,  // Ca làm việc — dùng cho báo cáo
                        IsOvertime              = row.IsOvertime,  // Tăng ca flag
                        SetupTime_Actual        = row.SetupTime_Actual,
                        MachiningTime_Actual    = row.MachiningTime_Actual,
                        InspectionTime_Actual   = row.InspectionTime_Actual,
                        PreparationTime_Actual  = row.PreparationTime_Actual,
                        TrialRunTime_Actual     = row.TrialRunTime_Actual,
                        // [SERIAL-TIME-REQUIRED] Lưu chế độ Hàng loạt / Chạy thử (chỉ GC; non-GC = NULL)
                        ProductionMode          = grp.IsGc ? row.ProductionMode : null,
                        // [ACTUAL-PC] Non-GC: lưu T.Thực tế/pc + đánh dấu có khác chuẩn không (GC: NULL)
                        ActualTimePerPc         = grp.IsGc ? null : row.ActualTimePerPc,
                        IsActualManual          = grp.IsGc || !row.ActualTimePerPc.HasValue ? null
                                                  : row.ActualTimePerPc != row.StdTimePerPc,
                        CreatedAt               = DateTime.Now,
                        IsVoided                = false,
                    };
                    _db.WtsProductionLogs.Add(log);
                    await _db.SaveChangesAsync();
                    row.WtsLogId = log.WtsLogId;
                    row.NgCaseId = log.NgCaseId;   // [VULOI]

                    // Lưu từng dòng phát sinh chi tiết vào WtsExtraItems
                    if (row.ExtraItems.Any())
                    {
                        foreach (var ei in row.ExtraItems.Where(e => e.Minutes > 0))
                        {
                            _db.WtsExtraItems.Add(new WtsExtraItemLog
                            {
                                WtsLogId   = log.WtsLogId,
                                WtsCode    = ei.WtsCode,
                                WtsName    = ei.WtsName,
                                Minutes    = ei.Minutes,
                                Notes      = ei.Notes,
                                CreatedAt  = DateTime.Now,
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    // [STD-UPDATE] Đóng băng số chuẩn + trạng thái xác nhận (theo snapshot phiếu KH) cho dòng WTS vừa lưu.
                    // Lỗi ở bước này KHÔNG làm hỏng dòng WTS (đã lưu) — chỉ báo cảnh báo, chạy lại SQL backfill để bù.
                    await SaveWtsLogStdsAsync(grp, log, row);

                    _db.ChangeTracker.Clear(); // clear EF cache để reload mới nhất
                    notify.Add(row.KhPlanDetailId.Value);   // [B2B3-WTS-TRX] đang trong giao dịch nhóm: báo sau khi ghi hẳn (trang nạp lại trần + số đã làm)
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException?.Message ?? ex.Message;
                    row.ErrorMsg = "Lỗi khi lưu: " + inner;
                    return;
                }
            }
            // ── Loại 2: Mã WTS khác (không link PO) → WorkerActivityLog ──
            else
            {
                try
                {
                    var actLog = new WorkerActivityLog
                    {
                        WorkerId  = grp.UserId,
                        WorkDate  = DateOnly.FromDateTime(grp.WorkDate),
                        StartTime = row.StartTime,
                        EndTime   = row.EndTime,
                        WtsCode   = row.WtsCode,
                        WtsName   = row.WtsName,
                        Notes     = row.Notes,
                        CreatedAt = DateTime.Now,
                        IsVoided  = false,
                    };
                    _db.WorkerActivityLogs.Add(actLog);
                    await _db.SaveChangesAsync();
                    row.ActivityLogId = actLog.ActivityLogId;

                    // Lưu từng dòng phát sinh chi tiết
                    if (row.ExtraItems.Any())
                    {
                        foreach (var ei in row.ExtraItems.Where(e => e.Minutes > 0))
                        {
                            _db.WtsExtraItems.Add(new WtsExtraItemLog
                            {
                                ActivityLogId = actLog.ActivityLogId,
                                WtsCode       = ei.WtsCode,
                                WtsName       = ei.WtsName,
                                Minutes       = ei.Minutes,
                                Notes         = ei.Notes,
                                CreatedAt     = DateTime.Now,
                            });
                        }
                        await _db.SaveChangesAsync();
                    }
                    _db.ChangeTracker.Clear();
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException?.Message ?? ex.Message;
                    row.ErrorMsg = "Lỗi khi lưu: " + inner;
                    return;
                }
            }

            row.Saved = true;
        }

        // ── [STD-UPDATE] Ghi WtsProductionLogStds cho 1 dòng WTS vừa lưu (nguyên văn SaveWtsLogStdsAsync của trang) ──
        // Dùng ĐÚNG số + dòng snapshot trang đã tra (GcStd / StdTimePerPc + StdSrc) — cùng GetStdTiming / GetStdTimeForSubRow /
        // GetStdSourceForSubRow như phần hiển thị. Trạng thái xác nhận đọc mới từ KhPlanRouteSnapshotStdConfirms
        // (trước: cache _snapStdConfirmCache của trang, nạp bằng cùng câu đọc KhPlanService.GetSnapshotStdConfirmsAsync).
        // [STD-SNAP 2026-10-30] Gọi TRƯỚC khi dựng dòng WTS (IsActualManual so với số chuẩn mới) và trước SaveWtsLogStdsAsync.
        private async Task RefreshStdFromSnapshotAsync(WtsSaveGroup grp, WtsSaveRow row, int dId)
        {
                // [STD-SNAP 2026-10-30] SỐ chuẩn đọc lại từ đúng dòng snapshot (bảng + id trang đã tra) trong giao dịch lưu — trước: số lấy từ
                //   cache trang (nạp lúc chọn PO). Trang mở từ trước khi "Update TG chuẩn" chạy sẽ lưu số CŨ kèm trạng thái xác nhận MỚI
                //   (đã đọc DB từ [WTS-SVC]) và Update đã chạy xong không ai sửa lại. Không tìm thấy dòng snapshot → giữ số trang như cũ.
                if (grp.IsGc && row.GcStd is { } gs0 && !string.IsNullOrEmpty(gs0.SrcTable) && gs0.SrcRowId.HasValue)
                {
                    var sid = gs0.SrcRowId.Value;
                    if (gs0.SrcTable == StdTrack.Table.Machining)
                    {
                        var m = await _db.KhPlanRouteSnapshotMachining.AsNoTracking().Where(x => x.SnapshotId == sid && x.KhPlanDetailId == dId)
                            .Select(x => new { x.SetupTime, x.MachiningTime, x.InspectionTime, x.PreparationTime, x.TrialRunTime }).FirstOrDefaultAsync();
                        if (m != null) row.GcStd = new WtsSaveStd { SetupTime = m.SetupTime, MachiningTime = m.MachiningTime, InspectionTime = m.InspectionTime, PreparationTime = m.PreparationTime, TrialRunTime = m.TrialRunTime, SrcTable = gs0.SrcTable, SrcRowId = gs0.SrcRowId };
                    }
                    else if (gs0.SrcTable == StdTrack.Table.MachiningTiming)
                    {
                        var t = await _db.KhPlanRouteSnapshotMachiningTimings.AsNoTracking().Where(x => x.TimingSnapshotId == sid && x.KhPlanDetailId == dId)
                            .Select(x => new { x.SetupTime, x.MachiningTime, x.InspectionTime, x.PreparationTime, x.TrialRunTime }).FirstOrDefaultAsync();
                        if (t != null) row.GcStd = new WtsSaveStd { SetupTime = t.SetupTime, MachiningTime = t.MachiningTime, InspectionTime = t.InspectionTime, PreparationTime = t.PreparationTime, TrialRunTime = t.TrialRunTime, SrcTable = gs0.SrcTable, SrcRowId = gs0.SrcRowId };
                    }
                }
                else if (!grp.IsGc && !string.IsNullOrEmpty(row.StdSrcTable) && row.StdSrcRowId.HasValue)
                {
                    var sid = row.StdSrcRowId.Value;
                    decimal? v = null; bool found = false;
                    switch (row.StdSrcTable)
                    {
                        case StdTrack.Table.Taro:       { var x = await _db.KhPlanRouteSnapshotTaro.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == sid && r.KhPlanDetailId == dId);       if (x != null) { v = x.StandardTime; found = true; } break; }
                        case StdTrack.Table.Bavia:      { var x = await _db.KhPlanRouteSnapshotBavia.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == sid && r.KhPlanDetailId == dId);      if (x != null) { v = x.StandardTime; found = true; } break; }
                        case StdTrack.Table.Washing:    { var x = await _db.KhPlanRouteSnapshotWashing.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == sid && r.KhPlanDetailId == dId);    if (x != null) { v = x.StandardTime; found = true; } break; }
                        case StdTrack.Table.Inspection: { var x = await _db.KhPlanRouteSnapshotInspection.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == sid && r.KhPlanDetailId == dId); if (x != null) { v = x.StandardTime; found = true; } break; }
                        case StdTrack.Table.Packaging:  { var x = await _db.KhPlanRouteSnapshotPackaging.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == sid && r.KhPlanDetailId == dId);  if (x != null) { v = x.StandardTime; found = true; } break; }
                    }
                    if (found) row.StdTimePerPc = v;
                }

        }

        private async Task SaveWtsLogStdsAsync(WtsSaveGroup grp, WtsProductionLog log, WtsSaveRow row)
        {
            try
            {
                var detailId = log.KhPlanDetailId;
                var confirms = (await _db.KhPlanRouteSnapshotStdConfirms
                        .Where(x => x.KhPlanDetailId == detailId).AsNoTracking().ToListAsync())
                    .ToDictionary(x => (x.SnapshotTable, x.SnapshotRowId, x.FieldName));

                KhPlanRouteSnapshotStdConfirm? GetSnapConfirm(string? table, long? rowId, string field)
                {
                    if (string.IsNullOrEmpty(table) || !rowId.HasValue) return null;
                    return confirms.TryGetValue((table, rowId.Value, field), out var c) ? c : null;
                }

                var now  = DateTime.Now;
                var list = new List<WtsProductionLogStd>();

                WtsProductionLogStd Make(string field, decimal? value, string? table, long? rowId, string? note)
                {
                    var cf = GetSnapConfirm(table, rowId, field);
                    var ok = cf?.IsConfirmed == true;
                    return new WtsProductionLogStd
                    {
                        WtsLogId        = log.WtsLogId,
                        KhPlanDetailId  = detailId,
                        FieldName       = field,
                        StdValue        = value,
                        IsConfirmed     = ok,
                        ConfirmedBy     = ok ? cf!.ConfirmedBy     : null,
                        ConfirmedByName = ok ? cf!.ConfirmedByName : null,
                        ConfirmedAt     = ok ? cf!.ConfirmedAt     : null,
                        SnapshotTable   = table,
                        SnapshotRowId   = rowId,
                        SyncSource      = StdTrack.Source.Save,
                        SyncedAt        = now,
                        SyncedBy        = grp.UserId,
                        Note            = note,
                    };
                }

                if (grp.IsGc)
                {
                    var std  = row.GcStd;
                    var note = std == null                       ? "Không tìm thấy chuẩn trong snapshot phiếu KH"
                             : string.IsNullOrEmpty(std.SrcTable) ? "Chuẩn lấy từ Part Master live (không có trong snapshot)"
                             : null;
                    list.Add(Make(StdTrack.Field.Setup,       std?.SetupTime,       std?.SrcTable, std?.SrcRowId, note));
                    list.Add(Make(StdTrack.Field.Machining,   std?.MachiningTime,   std?.SrcTable, std?.SrcRowId, note));
                    list.Add(Make(StdTrack.Field.Inspection,  std?.InspectionTime,  std?.SrcTable, std?.SrcRowId, note));
                    list.Add(Make(StdTrack.Field.Preparation, std?.PreparationTime, std?.SrcTable, std?.SrcRowId, note));
                    list.Add(Make(StdTrack.Field.TrialRun,    std?.TrialRunTime,    std?.SrcTable, std?.SrcRowId, note));
                }
                else
                {
                    var hasSrc = !string.IsNullOrEmpty(row.StdSrcTable) && row.StdSrcRowId.HasValue;
                    list.Add(Make(StdTrack.Field.Standard, row.StdTimePerPc, hasSrc ? row.StdSrcTable : null, hasSrc ? row.StdSrcRowId : null,
                        hasSrc ? null : "Không tìm thấy chuẩn trong snapshot phiếu KH"));
                }

                _db.WtsProductionLogStds.AddRange(list);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                row.HasWarning = true;
                row.WarningMsg = (row.WarningMsg ?? "") + " Đã lưu WTS nhưng chưa ghi được số chuẩn: "
                               + (ex.InnerException?.Message ?? ex.Message);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // [ML16-ACT 2026-10-30] Master list 16 + 11 — "Việc không ra sản phẩm" (Hùng duyệt kế hoạch 30/10, cách B)
        //   1 dòng = Mã CV (WTS tiêu chuẩn, theo cấu hình nhóm của người nhập — không gõ tự do) + Bắt đầu + Kết thúc + Ghi chú,
        //   tuỳ chọn PO + vụ lỗi khi là giờ sửa hàng. Ghi vào bảng có sẵn WorkerActivityLogs (append-only: sửa = huỷ + dòng mới).
        //   KHÔNG mang số lượng, KHÔNG đụng sổ Giao nhận / trần WTS / K5 → không cần khoá PO. Hiệu suất theo cờ Có ích / Vô ích
        //   của mã (WtsReportService đọc sẵn). Xoá dòng: VoidRowAsync (ActivityLogId) có sẵn.
        //   Kiểm ở máy chủ: người nhập đang hoạt động; mã đang dùng + thuộc nhóm WTS người đó được nhập (Admin: mọi mã);
        //   chỉ ngày hôm nay; Kết thúc sau Bắt đầu, trong cùng ngày; không trùng giờ với dòng WTS / việc khác CỦA CHÍNH NGƯỜI ĐÓ;
        //   PO phải có thật; vụ lỗi phải thuộc đúng PO; sửa dòng: dòng cũ phải của người đó, còn hiệu lực, của hôm nay.
        // ════════════════════════════════════════════════════════════════════════════════════════════════
        public sealed class WtsActivityRequest
        {
            /// <summary>Đang sửa dòng nào (null = dòng mới).</summary>
            public long?    ActivityLogId     { get; set; }
            public int      UserId            { get; set; }
            public string?  WtsCode           { get; set; }
            public DateTime StartTime         { get; set; }
            public DateTime EndTime           { get; set; }
            public string?  Notes             { get; set; }
            public int?     RefKhPlanDetailId { get; set; }
            public long?    NgCaseId          { get; set; }
        }

        public const int ActivityNotesMax = 500;

        public async Task<WtsSaveResult> SaveActivityAsync(WtsActivityRequest req)
        {
            var result = new WtsSaveResult();
            WtsSaveResult Fail(string msg) { result.Ok = false; result.ErrorMsg = msg; return result; }

            var code = (req.WtsCode ?? "").Trim();
            if (code.Length == 0) return Fail("Chưa chọn Mã CV.");
            var today = DateTime.Now.Date;   // giờ máy chủ (CLAUDE-RULES 8.5)
            if (req.StartTime.Date != today)
                return Fail("Việc không ra sản phẩm chỉ nhập cho ngày hôm nay.");
            if (req.EndTime <= req.StartTime)
                return Fail("T.Kết thúc phải sau T.Bắt đầu.");
            if (req.EndTime > today.AddDays(1))
                return Fail("T.Kết thúc phải trong ngày hôm nay.");
            var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
            if (notes != null && notes.Length > ActivityNotesMax)
                return Fail($"Ghi chú dài quá {ActivityNotesMax} ký tự.");
            if (req.NgCaseId != null && req.RefKhPlanDetailId == null)
                return Fail($"Chọn PO trước khi chọn {NgCaseNo.Term}.");

            // Quyền + mã: cùng luật danh sách mã trên trang (WtsTasksForGroup), đọc từ DB
            var wg = await PermCheck.WtsProcessGroupsAsync(_db, req.UserId);
            if (wg == null) return Fail("Tài khoản không hoạt động — đăng nhập lại.");
            var task = (await _db.StandardWtsTasks.AsNoTracking().Where(t => t.IsActive).ToListAsync())
                .FirstOrDefault(t => string.Equals((t.TaskCode ?? "").Trim(), code, StringComparison.OrdinalIgnoreCase));   // so mã không phân biệt hoa thường (như SQL Server)
            if (task == null) return Fail($"Mã CV {code} không có trong WTS tiêu chuẩn hoặc đã ngưng dùng.");
            if (task.TaskCode.Trim().Length > 20) return Fail($"Mã CV {task.TaskCode} dài quá 20 ký tự (cột WtsCode) — nhờ Admin đặt mã ngắn hơn.");
            if (!wg.Value.IsAdmin)
            {
                var allowed = wg.Value.Groups.Count > 0 && await _db.WtsCategoryGroupMappings.AsNoTracking()
                    .AnyAsync(m => m.CategoryCode == task.CategoryCode && wg.Value.Groups.Contains(m.ProcessGroup));
                if (!allowed)
                    return Fail($"Mã CV {code} không thuộc nhóm WTS của bạn (WTS tiêu chuẩn → Cấu hình nhóm) — nhờ Admin thêm.");
            }

            // PO + vụ lỗi (tuỳ chọn) — chỉ nhãn tra cứu
            if (req.RefKhPlanDetailId != null
                && !await _db.KhPlanDetails.AsNoTracking().AnyAsync(d => d.KhPlanDetailId == req.RefKhPlanDetailId.Value))
                return Fail("PO đã chọn không còn trong Kế hoạch — chọn lại.");
            if (req.NgCaseId != null
                && !await _db.NgCases.AsNoTracking().AnyAsync(c => c.NgCaseId == req.NgCaseId.Value && c.KhPlanDetailId == req.RefKhPlanDetailId!.Value))
                return Fail($"{NgCaseNo.TermCap} {NgCaseNo.Format(req.NgCaseId.Value)} không thuộc PO đã chọn — chọn lại.");

            await using var trx = await _db.Database.BeginTransactionAsync();
            // Khoá theo người nhập: 2 tab cùng lưu không lọt trùng giờ (LockOwner = Transaction, nhả khi Commit / Rollback)
            if (!await PoGuard.TryLockInTransactionAsync(_db, "WTS_WORKER_" + req.UserId))
                return Fail("Bạn đang lưu WTS ở tab khác — bấm Lưu lại sau vài giây.");

            if (req.ActivityLogId != null)
            {
                var old = await _db.WorkerActivityLogs.AsNoTracking().FirstOrDefaultAsync(a => a.ActivityLogId == req.ActivityLogId.Value);
                if (old == null || old.WorkerId != req.UserId)
                    return Fail("Không tìm thấy dòng cần sửa của bạn — tải lại trang.");
                if (old.IsVoided)
                    return Fail("Dòng này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại.");
                if (old.WorkDate != DateOnly.FromDateTime(today))
                    return Fail("Chỉ sửa được dòng của ngày hôm nay.");
            }

            // Trùng giờ với dòng WTS / việc khác của chính người này (chạm mép không tính)
            var s0 = req.StartTime; var e0 = req.EndTime;
            var prodHit = await _db.WtsProductionLogs.AsNoTracking()
                .Where(w => w.WorkerId == req.UserId && !w.IsVoided && w.StartTime < e0 && w.EndTime > s0)
                .OrderBy(w => w.StartTime)
                .Select(w => new { w.StartTime, w.EndTime, Code = w.NC ?? w.WtsCode, w.KhPlanDetailId })
                .FirstOrDefaultAsync();
            if (prodHit != null)
            {
                var po = await _db.KhPlanDetails.AsNoTracking().Where(d => d.KhPlanDetailId == prodHit.KhPlanDetailId)
                    .Select(d => d.PurchaseOrder).FirstOrDefaultAsync();
                return Fail($"Trùng giờ với dòng WTS của bạn {prodHit.StartTime:HH:mm}–{prodHit.EndTime:HH:mm} (PO {po} · {prodHit.Code}) — sửa giờ cho khỏi chồng nhau.");
            }
            var actHit = await _db.WorkerActivityLogs.AsNoTracking()
                .Where(a => a.WorkerId == req.UserId && !a.IsVoided && a.StartTime < e0 && a.EndTime > s0
                         && (req.ActivityLogId == null || a.ActivityLogId != req.ActivityLogId.Value))
                .OrderBy(a => a.StartTime)
                .Select(a => new { a.StartTime, a.EndTime, a.WtsCode })
                .FirstOrDefaultAsync();
            if (actHit != null)
                return Fail($"Trùng giờ với việc {actHit.WtsCode} {actHit.StartTime:HH:mm}–{actHit.EndTime:HH:mm} đã lưu — sửa giờ cho khỏi chồng nhau.");

            if (req.ActivityLogId != null)
            {
                var n = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE WorkerActivityLogs SET IsVoided=1, VoidReason=N'Sửa lại', VoidedBy={0}, VoidedAt={1} WHERE ActivityLogId={2} AND IsVoided=0 AND WorkerId={0}",
                    req.UserId, DateTime.Now, req.ActivityLogId.Value);
                if (n != 1)
                    return Fail("Dòng này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại.");
            }

            var log = new WorkerActivityLog
            {
                WorkerId          = req.UserId,
                WorkDate          = DateOnly.FromDateTime(req.StartTime),
                StartTime         = req.StartTime,
                EndTime           = req.EndTime,
                WtsCode           = task.TaskCode.Trim(),
                WtsName           = task.TaskName.Length > 250 ? task.TaskName[..250] : task.TaskName,
                Notes             = notes,
                RefKhPlanDetailId = req.RefKhPlanDetailId,
                NgCaseId          = req.NgCaseId,
                CreatedAt         = DateTime.Now,
                IsVoided          = false,
            };
            try
            {
                _db.WorkerActivityLogs.Add(log);
                await _db.SaveChangesAsync();
                if (req.ActivityLogId != null)
                {
                    // dòng cũ (Loại 2 bản trước) có mục phát sinh → chép sang dòng mới (dòng cũ đã huỷ, không mất dữ liệu)
                    var oldExtras = await _db.WtsExtraItems.AsNoTracking().Where(e => e.ActivityLogId == req.ActivityLogId.Value).ToListAsync();
                    foreach (var e in oldExtras)
                        _db.WtsExtraItems.Add(new WtsExtraItemLog { ActivityLogId = log.ActivityLogId, WtsCode = e.WtsCode, WtsName = e.WtsName, Minutes = e.Minutes, Notes = e.Notes, CreatedAt = DateTime.Now });
                    if (oldExtras.Count > 0) await _db.SaveChangesAsync();
                }
                await trx.CommitAsync();
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();   // không để dòng lỗi ở lại DbContext dùng chung với trang
                return Fail("Lỗi khi lưu: " + (ex.InnerException?.Message ?? ex.Message));
            }
            _db.ChangeTracker.Clear();
            result.Ok = true;
            return result;
        }

        // ── Xoá dòng đã lưu (nguyên văn VoidSavedRow + VoidWtsLogRawAsync của trang) ─────────────────
        // Trả Ok = đã xoá; Ok = false = bị chặn (lời báo ở ErrorMsg). Trang gọi LoadSavedRowsAsync khi Ok.
        public async Task<WtsSaveResult> VoidRowAsync(WtsVoidRequest row)
        {
            var result = new WtsSaveResult();
            // [GN-WTS-CAP][K5] Workflow ON: xoá dòng WTS mà làm ô nào mới âm thì chặn
            // (vd xoá WTS NC1 khi NC2 / nhóm sau đã làm từ số đó). Trước: xoá luôn, không kiểm.
            // [B4-WF-DB 2026-10-28] Luôn khoá PO và đọc cờ Workflow từ DB (sổ).
            // [WTS-SVC] Khoá PO LockOwner = Transaction (PoGuard.LockAsync) + 2 câu UPDATE trong cùng giao dịch (trước: khoá Session, UPDATE ngoài giao dịch).
            if (row.WtsLogId.HasValue && row.KhPlanDetailId.HasValue)
            {
                bool blocked = false;   // [GN-WHY] bị chặn K5 → tìm gốc rễ sau khi nhả khoá
                string? blockMsg = null;
                await using (var trx = await _db.Database.BeginTransactionAsync())
                {
                    var lockErr = await PoGuard.LockAsync(_db, row.KhPlanDetailId.Value);
                    if (lockErr != null)
                    { result.ErrorMsg = lockErr; return result; }   // = "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây." (chữ cũ của trang, nay 1 nguồn ở PoGuard)
                    var before = await _ledger.GetLedgerForDetailAsync(row.KhPlanDetailId.Value);
                    var after  = before?.ApplyWorkflow == true
                        ? await _ledger.GetLedgerForDetailAsync(row.KhPlanDetailId.Value, new List<long> { row.WtsLogId.Value })
                        : null;   // Workflow OFF (N7): không kiểm ô âm
                    if (before != null && after != null)
                    {
                        var bad = HandoverLedgerService.NewNegatives(before, after);
                        if (bad.Any())
                        {
                            blockMsg = $"Không xoá được: xoá xong sẽ làm sai số ở {string.Join("; ", bad)}. "
                                     + "Số của dòng này đã được dùng ở NC sau / nhóm sau hoặc đã giao đi — phải xoá / sửa từ cuối dòng ngược lên (K5).";
                            blocked = true;
                        }
                    }
                    if (!blocked)
                    {
                        await VoidWtsLogRawAsync(row);
                        await trx.CommitAsync();
                    }
                }   // bị chặn → Dispose = Rollback, khoá PO nhả theo giao dịch
                if (blocked)
                {
                    // [GN-WHY] chỉ đọc + tính thử, khoá PO đã nhả
                    result.ErrorMsg = blockMsg + await HandoverWhy.ExplainAsync(_db, row.KhPlanDetailId.Value,
                        HandoverWhy.OpWts(new List<long> { row.WtsLogId.Value }), new HashSet<string> { "W:" + row.WtsLogId.Value });
                    return result;
                }
                HandoverChangeNotifier.Notify(row.KhPlanDetailId.Value);   // [GN-LIVE] báo màn Giao nhận đang mở tính lại dòng PO này (huỷ WTS đổi Đã làm/Chưa làm)
                result.Ok = true;
                result.DetailIds.Add(row.KhPlanDetailId.Value);
                return result;
            }

            if (row.ActivityLogId.HasValue && !row.WtsLogId.HasValue)
            {
                // [ML16-ACT 2026-10-30] Xoá dòng "Việc không ra sản phẩm": chỉ dòng của chính người đó, còn hiệu lực (trước: không kiểm,
                //   0 dòng đổi vẫn báo xoá xong). Không số lượng → không khoá PO / K5.
                var nA = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE WorkerActivityLogs SET IsVoided=1, VoidReason=N'Xóa dòng', VoidedBy={0}, VoidedAt={1} WHERE ActivityLogId={2} AND IsVoided=0 AND WorkerId={0}",
                    row.UserId, DateTime.Now, row.ActivityLogId.Value);
                if (nA != 1)
                { result.ErrorMsg = "Dòng này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang."; return result; }
                result.Ok = true;
                return result;
            }
            await VoidWtsLogRawAsync(row);
            if (row.KhPlanDetailId.HasValue) HandoverChangeNotifier.Notify(row.KhPlanDetailId.Value);   // [GN-LIVE]
            result.Ok = true;
            return result;
        }

        private async Task VoidWtsLogRawAsync(WtsVoidRequest row)
        {
            if (row.WtsLogId.HasValue)
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE WtsProductionLogs SET IsVoided=1, VoidReason=N'Xóa dòng', VoidedBy={0}, VoidedAt={1} WHERE WtsLogId={2} AND IsVoided=0",   // [D8-WTS-VOID] không ghi đè người huỷ trước
                    row.UserId, DateTime.Now, row.WtsLogId.Value);
            if (row.ActivityLogId.HasValue)
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE WorkerActivityLogs SET IsVoided=1, VoidReason=N'Xóa dòng', VoidedBy={0}, VoidedAt={1} WHERE ActivityLogId={2} AND IsVoided=0",
                    row.UserId, DateTime.Now, row.ActivityLogId.Value);
            // [GN-LIVE] Notify: chuyển ra VoidRowAsync, báo sau khi Commit
        }
    }
}
