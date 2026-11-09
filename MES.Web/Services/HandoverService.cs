using Microsoft.EntityFrameworkCore;

using MES.Web.Data;

using MES.Web.Data.Entities;

namespace MES.Web.Services

{

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class HandoverTxPending

    {

        public long    HandoverTxId      { get; set; }

        public string  FromGroupCode     { get; set; } = string.Empty;

        public decimal QtyIssued         { get; set; }

        public decimal QtyReceivedSoFar  { get; set; }

        public decimal QtyRemaining      => QtyIssued - QtyReceivedSoFar;

        public DateTime IssuedAt         { get; set; }

        public string  IssuedByName      { get; set; } = string.Empty;

        // v0.9 — thông tin NG bên giao khai báo, hiển thị cho bên nhận biết trước

        public decimal NgQty             { get; set; }

        public string? NgReason          { get; set; }

        /// <summary>[GN-LEDGER 2026-10-22] Loại phiếu (NULL = phiếu cũ)</summary>
        public string? Purpose           { get; set; }

        public bool    IsReturnNg        { get; set; }
        /// <summary>[GN-CELL] Ô bên giao chọn (MOI/LOI/SUA, NULL = phiếu cũ)</summary>
        public string? FromCell          { get; set; }

        /// <summary>[GN-RECV-NG] mục 38: phiếu trả hàng lỗi tự lập khi bên nhận đếm NG — Id lần nhận gốc / phiếu gốc. Null = phiếu người dùng lập.</summary>
        public long?   SourceReceiveId   { get; set; }
        public long?   SourceTxId        { get; set; }
        public bool    IsAutoReturn      => SourceReceiveId != null;

        /// <summary>[GN-CELL2] Ô bên giao chỉ định cho bên nhận (phiếu cũ: suy theo loại phiếu; phiếu theo NC: null).</summary>
        public string? ToCell            { get; set; }
        /// <summary>[VULOI] Vụ lỗi phiếu mang theo (bên nhận không chọn — vụ đi theo phiếu).</summary>
        public long?   NgCaseId          { get; set; }
        /// <summary>[NHAN-DOI-O-NC 2026-11-06] NC nhận bên giao chọn (Gia công). Trống + nhận ô Hàng lỗi đã xử lý / Hàng mới → bên nhận chọn NC theo vụ.</summary>
        public string? ToNC              { get; set; }
    }

    /// <summary>[GN-NG-AFTER] Dòng "Báo hàng lỗi phát hiện sau khi nhận" cho Lịch sử phiếu.</summary>
    public class HandoverNgReportItem
    {
        public long     Id             { get; set; }
        public string   GroupCode      { get; set; } = string.Empty;
        public string   SourceCell     { get; set; } = string.Empty;
        public string?  NC             { get; set; }
        public decimal  Qty            { get; set; }
        public string   Origin         { get; set; } = string.Empty;
        public string?  CauseGroupCode { get; set; }
        public string   NgReason       { get; set; } = string.Empty;
        public string?  Notes          { get; set; }
        public long?    NgCaseId       { get; set; }   // [VULOI]
        public int      CreatedBy      { get; set; }
        public string   CreatedByName  { get; set; } = string.Empty;
        public DateTime CreatedAt      { get; set; }
        public bool     IsVoided       { get; set; }
        public string?  VoidReason     { get; set; }
        public string   VoidedByName   { get; set; } = string.Empty;
        public DateTime? VoidedAt      { get; set; }
    }

    /// <summary>[K5-CLOSE-NHIEU 2026-11-06] 1 lần huỷ phần chưa nhận để hiện ở Lịch sử phiếu.</summary>
    public class HandoverCloseItem
    {
        public decimal  Qty      { get; set; }
        public DateTime ClosedAt { get; set; }
        public string?  Reason   { get; set; }
        public string   ByName   { get; set; } = string.Empty;
        public bool     IsMigrated { get; set; }
    }

    public class HandoverLogItem

    {

        public long    HandoverTxId  { get; set; }

        public string  FromGroupCode { get; set; } = string.Empty;

        public string  ToGroupCode   { get; set; } = string.Empty;

        public decimal QtyIssued     { get; set; }

        public string? FromNC        { get; set; }

        public string  Status        { get; set; } = string.Empty;

        public DateTime IssuedAt     { get; set; }

        public string  IssuedByName  { get; set; } = string.Empty;

        /// <summary>[GN-VOID-RULE] Id người lập phiếu (luật huỷ: người lập hoặc nhóm giao).</summary>
        public int     IssuedBy      { get; set; }

        /// <summary>[GN-VOID-RULE] Đã huỷ phần chưa nhận (phiếu gốc = QtyIssued + QtyClosed).</summary>
        public decimal QtyClosed     { get; set; }
        public DateTime? ClosedAt    { get; set; }
        public string? ClosedReason  { get; set; }
        public string  ClosedByName  { get; set; } = string.Empty;
        /// <summary>[K5-CLOSE-NHIEU 2026-11-06] Từng lần huỷ phần chưa nhận (bảng HandoverTxCloses), cũ → mới. Rỗng = phiếu chưa có dòng (dữ liệu trước SQL 10) → trang hiện bộ cột cũ.</summary>
        public List<HandoverCloseItem> Closes { get; set; } = new();

        public string? Notes         { get; set; }

        // v0.9 — NG bên giao khai báo

        public decimal NgQty         { get; set; }

        public string? NgReason      { get; set; }

        public bool    IsVoided      { get; set; }

        public string? VoidReason    { get; set; }

        /// <summary>[GN-LEDGER 2026-10-22] Loại phiếu (NULL = phiếu cũ)</summary>
        public string? Purpose       { get; set; }

        public bool    IsReturnNg    { get; set; }
        /// <summary>[GN-CELL] Ô bên giao chọn (MOI/LOI/SUA, NULL = phiếu cũ)</summary>
        public string? FromCell      { get; set; }

        /// <summary>[GN-RECV-NG] mục 38: phiếu trả tự lập (Id lần nhận gốc). Không huỷ riêng được.</summary>
        public long?   SourceReceiveId { get; set; }

        /// <summary>[GN-CELL2] Ô bên giao chỉ định cho bên nhận.</summary>
        public string? ToCell        { get; set; }
        public string? ToNC          { get; set; }
        public long?   NgCaseId      { get; set; }   // [VULOI]
        public List<HandoverReceiveItem> Receives { get; set; } = new();

    }

    public class HandoverReceiveItem

    {

        public long    ReceiveId      { get; set; }

        public decimal QtyOk          { get; set; }

        public decimal QtyNg          { get; set; }

        public string? NgReason       { get; set; }

        public DateTime ReceivedAt    { get; set; }

        public string  ReceivedByName { get; set; } = string.Empty;

        /// <summary>[GN-VOID-RULE] Id người bấm Nhận (luật huỷ lần nhận: người nhận hoặc nhóm nhận).</summary>
        public int     ReceivedBy     { get; set; }

        /// <summary>[GN-CELL2] Ô bên nhận thực nhận + lý do nếu đổi khác chỉ định.</summary>
        public string? ToCell         { get; set; }
        public string? ToCellReason   { get; set; }
        public long?   NgCaseId       { get; set; }   // [VULOI] vụ lỗi sinh ra ở lần nhận (hoặc vụ của phiếu)
        public string? ToNC           { get; set; }   // [NHAN-DOI-O-NC 2026-11-06] NC nhận vào Gia công của lần nhận (đổi ô, phiếu không có NC)
        public bool    IsVoided       { get; set; }

        public string? VoidReason     { get; set; }

    }

    // ── Internal DTO ──────────────────────────────────────────────────────────

    internal record WtsDoneItem(int KhPlanDetailId, string ProcessGroup, string? NC, int StepOrder, int MaxSnapshotStep, decimal QtyDone);

    // ── DTO cho GetNcFlowAsync — dòng chảy SL per NC ──────────────────────────

    public class NcFlowItem

    {

        public string   GroupCode         { get; set; } = string.Empty;

        public string   SubGroup          { get; set; } = string.Empty;

        public int      StepOrder         { get; set; }

        public string   NC                { get; set; } = string.Empty;

        public string?  ParentNC          { get; set; }

        public bool     IsBackup          { get; set; }

        public string?  StepName          { get; set; }

        public string?  MachineRegistered { get; set; }

        public bool     IsExcluded        { get; set; }

        public bool     IsFirstInChain    { get; set; }

        public bool     IsLastInChain     { get; set; }

        public decimal  WtsDone           { get; set; }

        public decimal  HandoverIn        { get; set; }

        public decimal  HandoverOutConfirmed { get; set; }

        public decimal  HandoverOutPending   { get; set; }

        public decimal  HandoverOut       => HandoverOutConfirmed + HandoverOutPending;

        public decimal  PulledByNext      { get; set; }

        public decimal  Remaining         { get; set; }

        public decimal  AvailableInput    { get; set; }

    }

    // ── Service ───────────────────────────────────────────────────────────

    public partial class HandoverService   // [GC-SUA-TAI-CHO] partial: phần Kết quả sửa ở HandoverService.KetQuaSua.cs

    {

        private readonly AppDbContext _db;

        public HandoverService(AppDbContext db) => _db = db;

        // [RA-SOCHUNG 2026-11-01] Đã xoá cụm hàm không còn ai gọi (sổ tự cộng / lập phiếu kiểu cũ, trước sổ chung HandoverLedgerService):
        //   GetSummaryAsync + CalcGroupQty + CalcNgReturned + CalcNgReceived, GetReceivedOkAsync, GetNcOptionsAsync + GetNcOptionsBaseAsync
        //   + GetNcIssueAvailableAsync, IssueAsync (lập phiếu cũ) + GetIssueMaxAsync + GetNonGcForwardCapAsync + IsForwardIssue
        //   + GroupRank / IsFlatTimeCode, GetRemainingAsync, GetRemainingForNgReturnAsync, 4 mảng GcGroups… và 3 lớp DTO chỉ cụm này dùng
        //   (HandoverSummaryRow, HandoverGroupQty, NcOption). Lập phiếu chỉ còn IssueByCellAsync / IssueSpecialAsync (trần IssueCap của sổ).
        //   Bản cũ còn trong git (trước commit này).

        // ----------------------------------------------------------------

        // QUERY: Log chi tiết của 1 KhPlanDetail

        // ----------------------------------------------------------------

        public async Task<List<HandoverLogItem>> GetLogsAsync(int khPlanDetailId)

        {

            var txs = await _db.HandoverTransactions.AsNoTracking()   // [B1-STALE] chỉ đọc để hiện: luôn lấy số mới từ DB, không lấy bản EF đang giữ

                .Where(t => t.KhPlanDetailId == khPlanDetailId)

                .Include(t => t.IssuedByUser)

                .Include(t => t.Receives).ThenInclude(r => r.ReceivedByUser)

                .OrderByDescending(t => t.IssuedAt)

                .ToListAsync();

            // [GN-VOID-RULE] tên người huỷ phần chưa nhận (không có navigation cho ClosedBy)
            // [K5-CLOSE-NHIEU 2026-11-06] từng lần huỷ (bảng HandoverTxCloses)
            var closedIds = txs.Where(t => (t.QtyClosed ?? 0) > 0).Select(t => t.HandoverTxId).ToList();
            var closeRows = closedIds.Count == 0 ? new List<HandoverTxClose>()
                : await _db.HandoverTxCloses.AsNoTracking().Where(c => closedIds.Contains(c.HandoverTxId)).ToListAsync();
            var closerIds = txs.Where(t => t.ClosedBy != null).Select(t => t.ClosedBy!.Value)
                .Concat(closeRows.Select(c => c.ClosedBy)).Distinct().ToList();
            var closers = closerIds.Count == 0 ? new Dictionary<int, string>()
                : (await _db.Users.Where(u => closerIds.Contains(u.UserId)).AsNoTracking().ToListAsync())
                    .ToDictionary(u => u.UserId, u => string.IsNullOrEmpty(u.FullName) ? u.Username : u.FullName);
            // [DS-LOI-LIVE 2026-10-28] chữ lỗi của phiếu / lần nhận có mã vụ → tên loại lỗi hiện tại (DS Lỗi)
            var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db,
                txs.Select(t => t.NgCaseId).Concat(txs.SelectMany(t => t.Receives).Select(r => r.NgCaseId)));

            return txs.Select(t => new HandoverLogItem

            {

                HandoverTxId  = t.HandoverTxId,

                FromGroupCode = t.FromGroupCode,

                ToGroupCode   = t.ToGroupCode,

                QtyIssued     = t.QtyIssued,

                FromNC        = t.FromNC,

                Status        = t.Status,

                IssuedAt      = t.IssuedAt,

                IssuedByName  = t.IssuedByUser?.FullName ?? t.IssuedByUser?.Username ?? "",
                IssuedBy      = t.IssuedBy,                                    // [GN-VOID-RULE]
                QtyClosed     = t.QtyClosed ?? 0,                              // [GN-VOID-RULE]
                ClosedAt      = t.ClosedAt,
                ClosedReason  = t.ClosedReason,
                ClosedByName  = t.ClosedBy != null && closers.TryGetValue(t.ClosedBy.Value, out var cn) ? cn : "",
                Closes        = closeRows.Where(c => c.HandoverTxId == t.HandoverTxId).OrderBy(c => c.ClosedAt).ThenBy(c => c.CloseId)   // [K5-CLOSE-NHIEU]
                                    .Select(c => new HandoverCloseItem { Qty = c.Qty, ClosedAt = c.ClosedAt, Reason = c.Reason, IsMigrated = c.IsMigrated,
                                                                         ByName = closers.TryGetValue(c.ClosedBy, out var cb) ? cb : "" }).ToList(),

                Notes         = t.Notes,

                NgQty         = t.NgQty,       // v0.9

                NgReason      = NgCaseService.LiveReason(t.NgCaseId, t.NgReason, caseLbl),    // v0.9; [DS-LOI-LIVE]

                IsVoided      = t.IsVoided,

                VoidReason    = t.VoidReason,
                Purpose       = t.Purpose,     // [GN-LEDGER]
                IsReturnNg    = t.IsReturnNg,  // [GN-LEDGER]
                FromCell      = t.FromCell,    // [GN-CELL]
                SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                ToCell        = HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell : (t.Purpose ?? (t.IsReturnNg ? HandoverPurpose.NgReturn : null)) is string lp ? HandoverCell.ReceiveCellOf(lp) : null, // [GN-CELL2]
                ToNC          = t.ToNC,        // [GN-LEDGER]
                NgCaseId      = t.NgCaseId,    // [VULOI]
                Receives      = t.Receives.OrderBy(r => r.ReceivedAt).Select(r => new HandoverReceiveItem

                {

                    ReceiveId      = r.ReceiveId,

                    QtyOk          = r.QtyOk,

                    QtyNg          = r.QtyNg,

                    NgReason       = NgCaseService.LiveReason(r.NgCaseId, r.NgReason, caseLbl),   // [DS-LOI-LIVE]

                    ReceivedAt     = r.ReceivedAt,

                    ReceivedByName = r.ReceivedByUser?.FullName ?? r.ReceivedByUser?.Username ?? "",
                    ReceivedBy     = r.ReceivedBy,     // [GN-VOID-RULE]
                    ToCell         = r.ToCell,         // [GN-CELL2]
                    ToCellReason   = r.ToCellReason,   // [GN-CELL2]
                    NgCaseId       = r.NgCaseId,       // [VULOI]
                    ToNC           = r.ToNC,           // [NHAN-DOI-O-NC]
                    IsVoided       = r.IsVoided,

                    VoidReason     = r.VoidReason,

                }).ToList()

            }).ToList();

        }

        // ----------------------------------------------------------------

        // QUERY: Phiếu PENDING chờ nhận

        // ----------------------------------------------------------------

        public async Task<List<HandoverTxPending>> GetPendingForGroupAsync(

            int khPlanDetailId,

            string toGroupCode)

        {

            var txs = await _db.HandoverTransactions.AsNoTracking()   // [B1-STALE] chỉ đọc để hiện danh sách chờ nhận

                .Where(t => t.KhPlanDetailId == khPlanDetailId

                         && t.ToGroupCode == toGroupCode

                         && t.Status != "COMPLETED"

                         && !t.IsVoided)

                .Include(t => t.IssuedByUser)

                .Include(t => t.Receives)

                .OrderBy(t => t.IssuedAt)

                .ToListAsync();

            // [GN-RECV-NG] Phiếu trả tự lập: tra phiếu gốc để hiện "trả NG lúc nhận phiếu #…"
            var srcIds = txs.Where(t => t.SourceReceiveId != null).Select(t => t.SourceReceiveId!.Value).ToList();
            var srcTx  = srcIds.Count == 0 ? new Dictionary<long, long>()
                       : await _db.HandoverReceives.AsNoTracking().Where(r => srcIds.Contains(r.ReceiveId))
                             .ToDictionaryAsync(r => r.ReceiveId, r => r.HandoverTxId);
            var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db, txs.Select(t => t.NgCaseId));   // [DS-LOI-LIVE 2026-10-28]

            return txs.Select(t =>

            {

                var received = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);

                return new HandoverTxPending

                {

                    HandoverTxId     = t.HandoverTxId,

                    FromGroupCode    = t.FromGroupCode,

                    QtyIssued        = t.QtyIssued,

                    QtyReceivedSoFar = received,

                    IssuedAt         = t.IssuedAt,

                    IssuedByName     = t.IssuedByUser?.FullName ?? t.IssuedByUser?.Username ?? "",

                    NgQty            = t.NgQty,     // v0.9

                    NgReason         = NgCaseService.LiveReason(t.NgCaseId, t.NgReason, caseLbl),  // v0.9; [DS-LOI-LIVE] tên lỗi hiện tại
                    Purpose          = t.Purpose,     // [GN-LEDGER]
                    IsReturnNg       = t.IsReturnNg,  // [GN-LEDGER]
                    FromCell         = t.FromCell,    // [GN-CELL]
                    SourceReceiveId  = t.SourceReceiveId,   // [GN-RECV-NG]
                    SourceTxId       = t.SourceReceiveId != null && srcTx.TryGetValue(t.SourceReceiveId.Value, out var stx) ? stx : null,
                    ToCell           = t.SourceReceiveId != null ? HandoverCell.Loi   // [GN-CELL2] ô bên giao chỉ định (phiếu cũ: theo loại phiếu)
                                     : HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell
                                     : (t.Purpose ?? (t.IsReturnNg ? HandoverPurpose.NgReturn : null)) is string pp2 ? HandoverCell.ReceiveCellOf(pp2) : null,
                    NgCaseId         = t.NgCaseId,    // [VULOI]
                    ToNC             = t.ToNC,        // [NHAN-DOI-O-NC]
                };

            }).ToList();

        }

        // ----------------------------------------------------------------

        // QUERY: Batch check nhiều detail

        // ----------------------------------------------------------------

        public async Task<HashSet<int>> GetDetailIdsWithReceivedAsync(

            IEnumerable<int> detailIds,

            string groupCode)

        {

            var ids = detailIds.ToList();

            if (groupCode == "KHO")

            {

                var detailIdsLong = ids.Select(i => (long?)i).ToList();

                var khoIds = await _db.KhoVatLieus

                    .Where(k => k.PlanDetailId != null

                             && detailIdsLong.Contains(k.PlanDetailId)

                             && k.IsActive

                             && k.SoLuongThucNhan > 0)

                    .Select(k => (int)k.PlanDetailId!.Value)

                    .Distinct()

                    .ToListAsync();

                return khoIds.ToHashSet();

            }

            var result = await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && ids.Contains(r.Transaction.KhPlanDetailId)

                         && r.Transaction.ToGroupCode == groupCode

                         && r.QtyOk > 0)

                .Select(r => r.Transaction.KhPlanDetailId)

                .Distinct()

                .ToListAsync();

            // [GC-SUA-TAI-CHO 2026-11-04] Nhóm sửa tại chỗ: hàng KCS xác nhận đạt vào Chưa làm của nhóm mà không có lần nhận OK nào (rà độc lập T4)
            var rep = await _db.HandoverRepairChecks
                .Where(c => !c.IsVoided && c.QtyOk > 0 && !c.RepairResult.IsVoided
                         && c.RepairResult.GroupCode == groupCode && ids.Contains(c.RepairResult.KhPlanDetailId))
                .Select(c => c.RepairResult.KhPlanDetailId)
                .Distinct()
                .ToListAsync();
            result.AddRange(rep);
            return result.ToHashSet();

        }

        // ----------------------------------------------------------------
        // [GN-LEDGER 2026-10-22] WRITE: Giao hàng theo loại phiếu (trang Giao nhận mới)
        //   Trần lấy từ HandoverLedgerService.IssueCap — cùng một hàm với số hiển thị trên trang.
        //   K8: kiểm trần + ghi phiếu trong 1 giao dịch, dưới khoá PO MES_PO_ → 2 người giao cùng lúc không vượt trần. ([ISO-FIX 2026-10-31] trước: Serializable)
        // ----------------------------------------------------------------
        /// <summary>
        /// [GN-CELL 2026-10-23] Giao theo Ô Excel (form Giao mới): người giao chọn ô (Hàng mới / Hàng lỗi / Hàng sửa),
        /// tích "Hàng lỗi công đoạn trước", với ô Hàng lỗi không tích thì chọn "gửi nhờ sửa" hay "đã sửa xong trả về".
        /// Phần mềm suy loại phiếu (Purpose) + ô bên nhận, rồi kiểm trần / tuyến như IssueWithPurposeAsync.
        /// </summary>
        public async Task<(bool Ok, string Error)> IssueByCellAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued, int issuedByUserId,
            string cell, bool prevStageNg, string toCell,   // [GN-CELL2] trước: bool repaired — nay bên giao chọn thẳng ô bên nhận
            string? fromNC = null, string? toNC = null, string? notes = null, string? reason = null,
            int? ngCategoryId = null,   // [VULOI] Hàng lỗi công đoạn trước: loại lỗi (DS Lỗi) — sinh vụ mới
            long? ngCaseId = null)      // [VULOI] Hàng lỗi / Hàng sửa đang giữ: vụ lấy hàng (null = hàng lỗi cũ chưa có mã)
        {
            var (purpose, err) = HandoverCell.ResolveCells(cell, prevStageNg, toCell);   // [GN-CELL2]
            if (purpose == null) return (false, err ?? "Phân loại hàng không hợp lệ."); // [GN-PLH] mục 36
            return await IssueWithPurposeAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                issuedByUserId, purpose, fromNC, toNC, notes, reason, cell, toCell, ngCategoryId, ngCaseId);
        }

        /// <summary>[GN-CELL] Phiếu ngoài 4 ô Excel (Làm hộ gửi / trả, Huỷ, Trả khách) — vẫn đi qua cùng một hàm kiểm.</summary>
        public async Task<(bool Ok, string Error)> IssueSpecialAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued, int issuedByUserId,
            string purpose, string? fromNC = null, string? toNC = null, string? notes = null, string? reason = null,
            long? ngCaseId = null)   // [VULOI] Huỷ / Trả khách: vụ lấy hàng (null = hàng lỗi cũ chưa có mã)
        {
            if (purpose is not (HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn or HandoverPurpose.Scrap or HandoverPurpose.CustReturn))
                return (false, "Loại phiếu này phải lập theo ô Excel (Hàng mới / Hàng lỗi / Hàng sửa).");
            var cell = purpose is HandoverPurpose.Scrap or HandoverPurpose.CustReturn ? HandoverCell.Loi : null;
            return await IssueWithPurposeAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                issuedByUserId, purpose, fromNC, toNC, notes, reason, cell, null, null, ngCaseId);
        }

        // ----------------------------------------------------------------
        // [RS-D10 2026-10-30] SQL Server chọn giao dịch này làm "nạn nhân deadlock" (lỗi 1205) — gặp ở 2 chỗ mở giao dịch Serializable
        //   (lập phiếu, báo hàng lỗi sau nhận) khi 2 người lưu 2 PO khác nhau cùng lúc (khoá vùng chỉ mục chồng nhau).
        //   SQL đã tự rollback cả giao dịch → không có gì ghi dở; trước: trang báo lỗi SQL, người dùng phải bấm lại.
        //   Nay: tự thử lại tối đa 3 lần (mỗi lần là giao dịch mới: khoá PO lại, đọc lại sổ, kiểm trần lại). Lỗi khác ném ra như cũ.
        // ----------------------------------------------------------------
        public const string DeadlockMsg = "Máy chủ đang bận (nhiều người lưu cùng lúc) — đã tự thử lại 3 lần chưa được. Chưa lưu gì, bấm lưu lại sau vài giây.";

        /// <summary>[RS-D10] Lỗi (hoặc lỗi bên trong) là SqlException số 1205 — đọc thuộc tính Number, không cần tham chiếu Microsoft.Data.SqlClient.</summary>
        public static bool IsDeadlock(Exception? e)
        {
            for (int depth = 0; e != null && depth < 10; e = e.InnerException, depth++)
            {
                var p = e.GetType().GetProperty("Number");
                if (p != null && p.PropertyType == typeof(int)
                    && ((int)p.GetValue(e)! == 1205 || (int)p.GetValue(e)! == PoGuard.ApplockDeadlockNumber)) return true;   // [RS-D10c] + sp_getapplock −3
            }
            return false;
        }

        /// <summary>[LAMHO-NHO-SUA 2026-11-08] Lời chặn gửi vụ Taro làm hộ bị lỗi vào ô Hàng sửa nhóm khác Gia công (lập phiếu / nhận đổi ô dùng chung).
        ///   Nguồn: CLAUDE (sổ dòng LAMHO-NHO-SUA); Hùng 08/11 11:24 OK Gói 10.</summary>
        internal static string LamHoNhoSuaMsg(long caseId, string toGroupCode, string ten = "hàng Taro làm hộ bị lỗi")   // [LAMHO-NHAN-NG] + tên loại vụ làm hộ
            => $"{NgCaseNo.Format(caseId)} là {ten}: chỉ Gia công sửa — không gửi vào ô Hàng sửa của {HandoverLedgerService.GroupLabel(toGroupCode)}"
             + " (nhóm đó không ghi được Kết quả sửa). Gia công sửa tại nhóm, hoặc giao ô Hàng lỗi để trả về.";

        async Task<(bool Ok, string Error)> RetryDeadlockAsync(Func<Task<(bool Ok, string Error)>> once)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return await once(); }
                catch (Exception ex) when (IsDeadlock(ex))
                {
                    _db.ChangeTracker.Clear();   // bỏ mọi thứ đã Add / sửa trong lần hỏng (SQL đã rollback)
                    if (attempt >= 3) return (false, DeadlockMsg);
                    await Task.Delay(150 * attempt);
                }
            }
        }

        private Task<(bool Ok, string Error)> IssueWithPurposeAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued,
            int issuedByUserId, string purpose, string? fromNC, string? toNC, string? notes, string? reason,
            string? fromCell = null, string? toCell = null, int? ngCategoryId = null, long? ngCaseId = null)
            => RetryDeadlockAsync(() => IssueWithPurposeOnceAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                   issuedByUserId, purpose, fromNC, toNC, notes, reason, fromCell, toCell, ngCategoryId, ngCaseId));   // [RS-D10]

        private async Task<(bool Ok, string Error)> IssueWithPurposeOnceAsync(   // [RS-D10] thân cũ của IssueWithPurposeAsync, không đổi
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued,
            int issuedByUserId, string purpose, string? fromNC, string? toNC, string? notes, string? reason,
            string? fromCell = null,   // [GN-CELL] ô bên giao chọn (null = gọi từ chỗ cũ → suy từ purpose)
            string? toCell = null,     // [GN-CELL2] ô bên giao chỉ định cho bên nhận (null = theo loại phiếu)
            int? ngCategoryId = null,  // [VULOI] Hàng lỗi công đoạn trước: loại lỗi → sinh vụ mới
            long? ngCaseId = null,     // [VULOI] Hàng lỗi / Hàng sửa đang giữ, Huỷ, Trả khách: vụ lấy hàng
            FormTx? ftx = null)        // [GN-FORM1] chạy trong giao dịch của form "Giao / xử lý hàng" (đã khoá PO; K5 + Commit ở SaveFormAsync). null = như cũ
        {
            fromNC = HandoverLedgerService.TrimCode(fromNC); toNC = HandoverLedgerService.TrimCode(toNC);   // [WTS-MA-TRIM] NC so với quy trình trong sổ (đã bỏ khoảng trắng)
            fromCell ??= HandoverCell.IssueCellOf(purpose, null);
            bool cellPurpose = purpose is HandoverPurpose.New or HandoverPurpose.NgReturn or HandoverPurpose.RepairSend or HandoverPurpose.RepairReturn;
            if (toCell != null && (!cellPurpose || !HandoverCell.IsReceiveCell(toCell)))
                return (false, "Ô nhận không hợp lệ.");   // [GN-CELL2]
            if (cellPurpose) toCell ??= HandoverCell.ReceiveCellOf(purpose);
            // [TARO-LAMHO-NC] Trả lỗi hàng làm hộ (Trả về Gia công theo NC từ ô Hàng lỗi, mang vụ): Gia công nhận vào ô Hàng lỗi, không chọn NC.
            //   Nguồn: HÙNG 07/11 10:18 OK kế hoạch câu 2 (chiếc làm hộ bị lỗi nằm trong Tồn hàng giao theo NC tới khi trả Gia công dạng hàng lỗi).
            bool lhLoi = HandoverCell.IsLamHoLoi(purpose, fromCell);
            if (lhLoi) { toCell = HandoverCell.Loi; toNC = null; }
            if (qtyIssued <= 0) return (false, "Số lượng giao phải lớn hơn 0.");
            if (QtyRule.CheckWhole(qtyIssued) is string eWhole) return (false, eWhole);   // [B3-SO-NGUYEN 2026-11-06] chỉ nhận số nguyên (QtyRule)
            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
            bool isOut = HandoverPurpose.IsOut(purpose);   // [GN-NG-OUT] Huỷ / Trả khách: không có nhóm nhận
            if (isOut) toGroupCode = HandoverPurpose.OutTarget(purpose)!;
            if (!validGroups.Contains(fromGroupCode) || (!isOut && !validGroups.Contains(toGroupCode)))
                return (false, "Mã nhóm không hợp lệ.");
            // [RS-C2 2026-10-29] Máy chủ kiểm người lập phiếu có quyền SỬA nhóm giao (ma trận HANDOVER_*, Admin = mọi nhóm).
            //   Trước: chỉ trang lọc ô chọn nhóm — máy chủ không kiểm. Số vẫn cân, nhưng sai người lập là mất truy vết.
            //   Áp cho mọi đường lập phiếu: IssueByCellAsync, IssueSpecialAsync, IssueAsync (có loại phiếu).
            var denyIssue = await DenyGroupAsync(issuedByUserId, fromGroupCode, "lập phiếu giao");
            if (denyIssue != null) return (false, denyIssue);
            if (!HandoverPurpose.All.Contains(purpose)) return (false, "Loại phiếu không hợp lệ.");
            var routeErr = HandoverLedgerService.CheckRoute(fromGroupCode, toGroupCode, purpose);
            if (routeErr != null) return (false, routeErr);
            bool isNg = purpose is HandoverPurpose.NgReturn or HandoverPurpose.RepairSend || isOut || lhLoi;   // [GN-NG-OUT] huỷ / trả khách bắt buộc ghi lỗi · [TARO-LAMHO-NC] + trả lỗi hàng làm hộ
            // [VULOI 2026-10-28] Lỗi không tự gõ nữa: Hàng lỗi công đoạn trước chọn loại lỗi (DS Lỗi) → sinh vụ;
            //   lấy từ ô Hàng lỗi / Hàng sửa, Huỷ, Trả khách: chọn vụ đang giữ → lỗi theo vụ. (Trước: bắt buộc gõ lỗi cụ thể.)
            bool takesCase = HandoverLedgerService.TakesHeldCase(purpose, fromCell);
            NgCategory? ngCat = null;
            if (purpose == HandoverPurpose.NgReturn)
            {
                ngCat = await NgCaseService.GetActiveCategoryAsync(_db, ngCategoryId);
                if (ngCat == null) return (false, NgCaseService.MsgPickCategory);
                reason = ngCat.Label;
                ngCaseId = null;
            }
            else if (!takesCase) ngCaseId = null;
            // [GN-CELL] Ô giao phải khớp loại phiếu (chặn gọi sai từ code)
            if (fromCell != null && !HandoverCell.IssueCells.Contains(fromCell))
                return (false, "Phân loại hàng không hợp lệ."); // [GN-PLH] mục 36
            if (fromCell != null && HandoverCell.IssueCellOf(purpose, fromCell) != fromCell)
                return (false, $"Ô {HandoverCell.Label(fromCell)} không giao được theo kiểu {HandoverPurpose.Label(purpose)}.");
            if (purpose == HandoverPurpose.HelpSend && string.IsNullOrEmpty(fromNC))
                return (false, "Giao theo NC: bắt buộc chọn NC nguồn.");
            if (purpose == HandoverPurpose.HelpSend && string.IsNullOrWhiteSpace(notes))
                return (false, "Giao theo NC: bắt buộc ghi chú (N4).");

            // [ISO-FIX 2026-10-31] Bỏ IsolationLevel.Serializable, giữ khoá PO (LockPoAsync ngay dưới, trước mọi câu đọc sổ).
            //   Trước: giao dịch Serializable để lại mức Serializable trên kết nối pool → câu đọc / giao dịch sau trên kết nối đó
            //   (BeginTransactionAsync() không chỉ định mức) chạy Serializable ngoài ý muốn: T1 MES_TEST đo ReadCommitted 902 · Serializable 396,
            //   RCSI mất tác dụng, deadlock / "Máy chủ đang bận" tăng. Serializable không bảo vệ thêm: mọi chỗ ghi sổ PO đều khoá MES_PO_
            //   trước khi đọc, mã phiếu / mã vụ là identity (không max+1).
            // [GN-FORM1] ftx != null: giao dịch + khoá PO do SaveFormAsync mở sẵn (2 chứng từ 1 lần lưu) — không mở / khoá lại
            using var trx = ftx == null ? await _db.Database.BeginTransactionAsync() : null;
            if (ftx == null)
            {
                var lockErr = await LockPoAsync(khPlanDetailId);   // [GN-K8] khoá chung với trang WTS
                if (lockErr != null) return (false, lockErr);
            }
            var ledger = new HandoverLedgerService(_db);
            var row = await ledger.GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return (false, "Không tìm thấy PO.");

            // [TARO-LAMHO-NC] (rà độc lập gói 6 mục 2, 4) PO có Taro làm hộ (Workflow ON):
            //   giao theo NC từ NC ngay trước đoạn có Taro → chỉ cho HTSP (nhóm khác nhận thì Taro không bao giờ được làm);
            //   HTSP chỉ có mã Taro làm hộ (không có công đoạn HTSP của mình) → không giao Hàng mới cho HTSP (hàng vào đó không làm tiếp được).
            if (row.ApplyWorkflow && purpose == HandoverPurpose.HelpSend && toGroupCode != "HTSP"
                && row.LamHoRuns.FirstOrDefault(r => r.HasTaro && string.Equals(r.FromNc, fromNC, StringComparison.OrdinalIgnoreCase)) is { } lhR)
                return (false, $"Đoạn sau {HandoverLedgerService.NcName(fromNC)} ({string.Join(", ", lhR.Ncs.Select(HandoverLedgerService.NcName))}) có Taro làm hộ: giao theo NC cho HTSP.");
            if (row.ApplyWorkflow && purpose == HandoverPurpose.New && toGroupCode == "HTSP" && HandoverLedgerService.HtspOnlyLamHo(row))
                return (false, "PO này HTSP chỉ làm Taro làm hộ (giao theo NC), không có công đoạn HTSP của mình — giao Hàng mới cho nhóm sau (KCS / Đóng gói).");
            // K3: hàng làm hộ trả về Gia công phải chọn NC nhận khi PO có snapshot NC
            bool gcHasNc = row.G("GC").Steps.Any();
            if (toGroupCode == "GC" && gcHasNc && string.IsNullOrEmpty(toNC) && purpose == HandoverPurpose.HelpReturn && !lhLoi)   // [TARO-LAMHO-NC] trả lỗi: vào ô Hàng lỗi, không NC
                return (false, "Trả về Gia công: bắt buộc chọn NC nhận vào (K3).");
            // [N3-TONC 2026-10-30] NC nhận vào phải là NC thường có trong snapshot của PO (trang chỉ cho chọn các NC này; máy chủ kiểm lại).
            //   Trước: NC máy loại trừ / NC không có trong quy trình vẫn lưu được → sổ dồn số đó vào NC đầu, không vào NC đã ghi trên phiếu (lệch truy vết).
            //   [N3-FIRST 2026-11-04] dòng "Nhận vào (chưa qua NC)" (@VAO) không phải NC nhận: hàng vào Gia công không chọn NC đã tự nằm ở đó
            //   (sổ chỉ trừ "in:" của NC thường khỏi dòng này — cho nhận vào @VAO sẽ đếm 2 lần).
            if (toGroupCode == "GC" && gcHasNc && !string.IsNullOrEmpty(toNC) && !isOut
                && (HandoverLedgerService.IsVao(toNC) || !row.G("GC").Steps.Any(s => !s.IsExcluded && s.Code == toNC)))   // so đúng chữ như sổ ("in:" + NC)
                return (false, HandoverLedgerService.IsVao(toNC)
                    ? $"\"{HandoverLedgerService.VaoLabel}\" không phải NC nhận hàng: hàng vào Gia công không chọn NC đã tự nằm ở đó, hàng trả về chọn NC sau máy loại trừ. Chọn lại NC nhận vào."   // [N3-FIRST]
                    : $"NC {toNC} không phải NC nhận hàng của PO này (không có trong quy trình hoặc là máy loại trừ). Chọn lại NC nhận vào.");

            // [GN-NC-PIN] Gia công trả hàng lỗi công đoạn trước: bắt buộc chọn NC phát hiện lỗi (chốt NC, không trôi theo dữ liệu sau)
            if (purpose == HandoverPurpose.NgReturn && fromGroupCode == "GC" && row.G("GC").Steps.Any(s => !s.IsExcluded))
            {
                if (string.IsNullOrEmpty(fromNC))
                    return (false, "Gia công trả hàng lỗi: bắt buộc chọn NC phát hiện lỗi.");
                if (!row.G("GC").Steps.Any(s => s.Code == fromNC && !s.IsExcluded))
                    return (false, $"{HandoverLedgerService.NcText(fromNC)} không có trong snapshot của PO (hoặc là máy loại trừ).");   // [N3-FIRST] chữ @VAO
            }
            // [VULOI] Vụ lấy hàng phải thuộc PO này và đang có hàng ở ô bên giao; không chọn vụ = hàng lỗi cũ chưa có mã (chỉ khi còn)
            if (takesCase)
            {
                var cellTake = HandoverLedgerService.HeldCellOf(purpose, fromCell);   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ: vụ giữ ô LAMHO
                if (ngCaseId != null)
                {
                    if (!row.Cases.TryGetValue(ngCaseId.Value, out var csSel) || ngCaseId.Value <= 0)
                        return (false, $"{NgCaseNo.Format(ngCaseId.Value)} không thuộc PO này.");
                    reason = csSel.CatLabel;
                }
                else
                {
                    if (row.CaseHeld(0, fromGroupCode, cellTake) <= 0)
                        return (false, $"Chọn {NgCaseNo.Term} đang giữ ở ô {HandoverCell.Label(cellTake)} của {HandoverLedgerService.GroupLabel(fromGroupCode)}.");
                    reason = "Hàng lỗi cũ (chưa có mã vụ)";
                }
            }
            // [TARO-LAMHO-NC] (rà vòng 3 T2) vụ hàng Taro làm hộ bị lỗi: chiếc của chuỗi Gia công — chỉ vào ô hàng đạt (Hàng mới / Hàng lỗi đã xử lý) ở Gia công
            if (takesCase && ngCaseId is long lhId && row.Cases.TryGetValue(lhId, out var lhCase) && lhCase.IsLamHo && toGroupCode != "GC" && toCell is HandoverCell.Xl or HandoverCell.Moi)
                return (false, $"{NgCaseNo.Format(lhId)} là {lhCase.LamHoTen}: sửa xong trả về Gia công (làm tiếp theo NC của vụ), không giao vào ô hàng đạt của {HandoverLedgerService.GroupLabel(toGroupCode)}.");
            // [LAMHO-NHO-SUA 2026-11-08] Vụ làm hộ chỉ SỬA ở Gia công (Kết quả sửa nhóm khác bị chặn — KetQuaSua [TARO-LAMHO-NC]) → không gửi nhờ sửa
            //   vào ô Hàng sửa của nhóm khác (trước: lưu được, nhóm nhận chỉ trả lại được — đường cụt, số vẫn cân). Ô Hàng lỗi nhóm khác vẫn cho (để trả về).
            //   Nguồn: CLAUDE (kiem trạm Gói 9 seed 23980 PO19, sổ dòng LAMHO-NHO-SUA); Hùng 08/11 11:24 OK Gói 10.
            if (takesCase && ngCaseId is long lhId2 && row.Cases.TryGetValue(lhId2, out var lhCase2) && lhCase2.IsLamHo && toGroupCode != "GC" && toCell == HandoverCell.Sua)
                return (false, LamHoNhoSuaMsg(lhId2, toGroupCode, lhCase2.LamHoTen));   // [LAMHO-NHAN-NG] chữ theo loại vụ
            var (max, ex) = HandoverLedgerService.IssueCapCase(row, fromGroupCode, purpose, fromNC, fromCell, ngCaseId, toNC);   // [GN-CELL] trần theo ô; [VULOI] + theo vụ; [TARO-LAMHO-NC] + NC nhận (câu 1)
            if (qtyIssued > max)
                return (false, $"{(fromCell != null ? "Ô " + HandoverCell.Label(fromCell) : HandoverPurpose.Label(purpose))}: tối đa {max:0.##} ({ex.Title}{(string.IsNullOrEmpty(ex.Arithmetic) ? "" : ": " + ex.Arithmetic)}). Không thể giao {qtyIssued:0.##}.");
            // [VULOI] Hàng lỗi công đoạn trước: vụ sinh ra cùng phiếu (nơi phát hiện = bên giao, nơi gây lỗi = bên nhận)
            if (ngCat != null)
            {
                var vu = await NgCaseService.CreateCaseAsync(_db, khPlanDetailId, ngCat, NgOrigin.TraLoi, fromGroupCode,
                    fromGroupCode == "GC" ? fromNC : null, toGroupCode, issuedByUserId);
                ngCaseId = vu.NgCaseId;
            }
            var newTx = new HandoverTransaction   // [GN-FORM1] giữ biến để form lấy mã phiếu
            {
                KhPlanDetailId = khPlanDetailId,
                FromGroupCode  = fromGroupCode,
                ToGroupCode    = toGroupCode,
                QtyIssued      = qtyIssued,
                FromNC         = purpose == HandoverPurpose.HelpSend || (purpose == HandoverPurpose.NgReturn && fromGroupCode == "GC") ? fromNC : null,   // [GN-NC-PIN] + NC phát hiện lỗi của GC
                ToNC           = isOut || toGroupCode == "KHO" || string.IsNullOrEmpty(toNC) ? null : toNC,
                NgQty          = isNg ? qtyIssued : 0,
                NgReason       = isNg ? reason?.Trim() : null,
                IsReturnNg     = purpose == HandoverPurpose.NgReturn,   // giữ cờ cũ cho các trang còn đọc IsReturnNg = tích "Hàng lỗi công đoạn trước"
                Purpose        = purpose,
                FromCell       = fromCell,                               // [GN-CELL] ô bên giao chọn (đúng cột Excel)
                ToCell         = toCell,                                 // [GN-CELL2] ô bên giao chỉ định cho bên nhận
                NgCaseId       = ngCaseId,                               // [VULOI] vụ lỗi của phiếu
                Status         = isOut ? "COMPLETED" : "PENDING",   // [GN-NG-OUT] ra khỏi PO ngay, không chờ nhận
                IssuedBy       = issuedByUserId,
                IssuedAt       = DateTime.Now,
                Notes          = notes,
                CreatedAt      = DateTime.Now,
            };
            _db.HandoverTransactions.Add(newTx);
            await _db.SaveChangesAsync();
            if (ftx != null) { ftx.TxIds.Add(newTx.HandoverTxId); if (ngCaseId is long fc) ftx.CaseId = fc; return (true, string.Empty); }   // [GN-FORM1] K5 + Commit ở SaveFormAsync
            // [GN-K8] Tính lại sổ trong cùng giao dịch — ô nào mới âm thì không lưu
            var k8 = await CheckNoNewNegativeAsync(khPlanDetailId, row);
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }   // không Commit → rollback
            await trx!.CommitAsync();
            HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE] báo mọi màn Giao nhận đang mở tính lại dòng PO này
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // [GN-K8] Khoá theo PO + kiểm "lưu xong không ô nào mới âm" — dùng chung cho mọi thao tác ghi giao nhận.
        //   Khoá "MES_PO_<id>" trùng tên với khoá trang WTS (HieuSuat_Index) → WTS và Giao nhận của cùng 1 PO
        //   không ghi chen nhau. LockOwner = Transaction: tự nhả khi Commit / Rollback.
        // ----------------------------------------------------------------
        // [RS-D10c 2026-10-31] −3 (khoá bị chọn làm nạn nhân deadlock) ném lỗi 51205 → RetryDeadlockAsync tự thử lại như 1205
        //   (mọi nơi gọi LockPoAsync đều nằm trong 7 hàm *OnceAsync có vòng thử lại). Trước: báo "PO đang có người khác lưu".
        private async Task<string?> LockPoAsync(int khPlanDetailId)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; " +
                    PoGuard.ApplockDeadlockSql +   // [RS-D10c]
                    "IF @r < 0 THROW 50001, N'PO đang có người khác lưu', 1;",
                    "MES_PO_" + khPlanDetailId);
                // [B1-STALE 2026-10-28] Có khoá rồi mới đọc số: bỏ mọi phiếu / lần nhận EF đang giữ trong bộ nhớ trang (DbContext sống suốt phiên).
                //   Trước: đọc lại phiếu sau khoá vẫn ra bản cũ (vd lần nhận đã bị người khác huỷ vẫn tính là còn) → Nhận / Huỷ ghi sai trạng thái phiếu.
                //   Trước khoá các hàm ghi chỉ đọc AsNoTracking (không có gì đang thêm / sửa dở) nên dọn ở đây an toàn.
                _db.ChangeTracker.Clear();
                return null;
            }
            catch (Exception ex) when (ex.Message.Contains("PO đang có người khác lưu"))
            {
                // Chỉ bắt đúng lỗi hết giờ chờ khoá (THROW 50001 ở trên); lỗi SQL khác để nổi lên như cũ
                return "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.";
            }
        }

        /// <summary>[GN-K8][K5] So sổ trước / sau (sau = đọc lại trong cùng giao dịch). Trả lời báo nếu có ô mới âm (chỉ Workflow ON — N7).</summary>
        private async Task<string?> CheckNoNewNegativeAsync(int khPlanDetailId, LedgerRow before, LedgerInput? beforeIn = null)
        {
            if (!before.ApplyWorkflow) return null;
            // [K5-VONG-SUA 2026-11-05] beforeIn != null (huỷ lần nhận / phiếu / phần chưa nhận / KCS xác nhận): xét thêm theo thời gian hàng sửa trả về
            var afterIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(khPlanDetailId);
            if (afterIn == null) return null;
            var after = HandoverLedgerService.Compute(afterIn);
            var bad = HandoverLedgerService.NewNegatives(before, after);
            if (beforeIn != null) bad.AddRange(HandoverLedgerService.NewLoopKeys(beforeIn, before, afterIn).Select(x => $"{x.Text} = {x.Value:0.##}"));
            return bad.Any()
                ? $"Không lưu: làm xong sẽ sai số ở {string.Join("; ", bad)}. Hàng này đã được dùng ở bước sau — phải huỷ / sửa từ cuối dòng ngược lên (K5, K8)."
                : null;
        }

        // ----------------------------------------------------------------

        // WRITE: Nhận hàng

        // ----------------------------------------------------------------

        public Task<(bool Ok, string Error)> ReceiveAsync(long handoverTxId, decimal qtyOk, decimal qtyNg, string? ngReason, int receivedByUserId, string? notes = null, string? toCell = null, string? toCellReason = null, int? ngCategoryId = null,
                                                        string? toNc = null, string? toNcReason = null)   // [NHAN-DOI-O-NC] NC nhận khi đổi ô vào Gia công (null = mặc định theo vụ, "@NEW" = như hàng mới)
            => RetryDeadlockAsync(() => ReceiveOnceAsync(handoverTxId, qtyOk, qtyNg, ngReason, receivedByUserId, notes, toCell, toCellReason, ngCategoryId, toNc, toNcReason));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> ReceiveOnceAsync(

            long handoverTxId,

            decimal qtyOk,

            decimal qtyNg,

            string? ngReason,

            int receivedByUserId,

            string? notes = null,

            string? toCell = null,          // [GN-CELL2] ô bên nhận chọn (null = theo ô bên giao chỉ định)
            string? toCellReason = null,    // [GN-CELL2] bắt buộc khi đổi khác ô chỉ định
            int? ngCategoryId = null,       // [VULOI] loại lỗi khi lần nhận SINH vụ mới (NG đếm lúc nhận / nhận vào ô Hàng lỗi, Hàng sửa)
            string? toNc = null,            // [NHAN-DOI-O-NC] NC nhận (xem ReceiveNcAsync)
            string? toNcReason = null)      // [NHAN-DOI-O-NC] lý do khi lùi về NC trước NC của vụ
        {
            toNc = HandoverLedgerService.TrimCode(toNc);   // [WTS-MA-TRIM]

            if (qtyOk < 0 || qtyNg < 0)

                return (false, "Số lượng không được âm.");

            if (qtyOk + qtyNg <= 0)

                return (false, "Phải nhập ít nhất 1 chiếc OK hoặc NG.");
            if (QtyRule.CheckWhole(qtyOk, qtyNg) is string eWholeR) return (false, eWholeR);   // [B3-SO-NGUYEN 2026-11-06] chỉ nhận số nguyên (QtyRule)

            // [GN-RECV-NG2] Kiểm lý do NG dời xuống sau khi đọc phiếu: phiếu Hàng lỗi công đoạn trước lấy lỗi theo phiếu

            var txHead = await _db.HandoverTransactions.AsNoTracking()
                .Where(t => t.HandoverTxId == handoverTxId).Select(t => new { t.KhPlanDetailId, t.Purpose, t.IsReturnNg, t.NgReason, t.ToCell, t.SourceReceiveId, t.NgCaseId, t.ToGroupCode, t.FromCell }).FirstOrDefaultAsync();   // [TARO-LAMHO-NC] + FromCell   // [RS-C2] + nhóm nhận   // [VULOI] + vụ // [GN-RECV-NG2] + loại phiếu, lỗi; [GN-CELL2] + ô chỉ định
            if (txHead == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            // [RS-C2 2026-10-29] Máy chủ kiểm người bấm Nhận có quyền SỬA nhóm nhận (ma trận HANDOVER_*, Admin = mọi nhóm).
            //   Trước: trang lấy nhóm từ ô chọn, máy chủ không kiểm (huỷ thì đã kiểm qua editGroups).
            //   Phiếu Huỷ / Trả khách không có bên nhận: bỏ qua bước này để lời báo cũ ("không có bên nhận") hiện đúng.
            if (!HandoverPurpose.IsOut(txHead.Purpose))
            {
                var denyRecv = await DenyGroupAsync(receivedByUserId, txHead.ToGroupCode, "nhận hàng");
                if (denyRecv != null) return (false, denyRecv);
            }
            // [GN-CELL2] Ô bên nhận: mặc định ô bên giao chỉ định (phiếu cũ: theo loại phiếu); bên nhận đổi được, đổi thì bắt buộc lý do.
            //   Phiếu theo NC / phiếu cũ không rõ loại: không có ô để chọn. Phiếu trả tự lập (NG lúc nhận): luôn ô Hàng lỗi.
            var headPurpose = txHead.Purpose ?? (txHead.IsReturnNg ? HandoverPurpose.NgReturn : null);
            string? desigCell = txHead.SourceReceiveId != null ? HandoverCell.Loi
                              : HandoverCell.IsReceiveCell(txHead.ToCell) ? txHead.ToCell
                              : headPurpose == null ? null : HandoverCell.ReceiveCellOf(headPurpose);
            if (toCell != null)
            {
                if (desigCell == null || !HandoverCell.IsReceiveCell(toCell))
                    return (false, "Phiếu này không chọn ô nhận được.");
                if (txHead.SourceReceiveId != null && toCell != HandoverCell.Loi)
                    return (false, "Phiếu trả hàng lỗi do phần mềm tự lập: chỉ nhận vào ô Hàng lỗi.");
                if (HandoverCell.IsLamHoLoi(txHead.Purpose, txHead.FromCell) && toCell != HandoverCell.Loi)   // [TARO-LAMHO-NC]
                    return (false, "Hàng làm hộ bị lỗi trả về: chỉ nhận vào ô Hàng lỗi (cùng vụ).");
                if (toCell != desigCell && string.IsNullOrWhiteSpace(toCellReason))
                    return (false, $"Đổi phân loại khác bên giao chỉ định ({HandoverCell.Label(desigCell)} → {HandoverCell.Label(toCell)}): bắt buộc ghi lý do.");
            }
            var effCell = toCell ?? desigCell;
            bool cellChanged = toCell != null && toCell != desigCell;
            // [GN-RECV-NG2] Nhận vào ô Hàng lỗi (phiếu hàng lỗi lập tay / tự lập, hoặc bên nhận chọn ô Hàng lỗi): toàn bộ là hàng lỗi → không có số "đạt";
            // số nhận nhập ở ô Hàng lỗi, lỗi lấy theo phiếu nếu không ghi thêm. [GN-CELL2] trước: xét theo loại phiếu Trả hàng lỗi.
            var isNgRet = effCell == HandoverCell.Loi;
            if (isNgRet && qtyOk > 0)
                return (false, "Phiếu hàng lỗi: không có hàng đạt — nhập số nhận lại ở ô Hàng lỗi.");
            // [GN-RECV-SUA 2026-10-24] Nhận vào ô Hàng sửa = nhận hàng lỗi về để sửa: không có "đạt / NG đếm thấy", chỉ đếm số chiếc tới nơi
            //   (ghi ở QtyOk → Nhận · Hàng sửa). Không tự lập phiếu trả. Khách báo: HTSP nhận hàng Kho gửi sửa gõ NG 1 → phần mềm trả ngược về Kho (sai).
            //   Nhận rồi thấy không sửa được → giao lại bên gửi bằng phiếu riêng (có chứng từ).
            if (effCell == HandoverCell.Sua && qtyNg > 0)
                return (false, "Nhận vào ô Hàng sửa: chỉ nhập số chiếc nhận, không có NG lúc nhận (hàng gửi sửa vốn là hàng lỗi). Không sửa được thì giao trả lại bên gửi.");
            if (isNgRet && string.IsNullOrWhiteSpace(ngReason))
                ngReason = !string.IsNullOrWhiteSpace(txHead.NgReason) ? txHead.NgReason
                         : cellChanged && !string.IsNullOrWhiteSpace(toCellReason) ? toCellReason!.Trim()   // [GN-CELL2] bên nhận đổi sang ô Hàng lỗi: lỗi = lý do đổi
                         : "Hàng lỗi trả về";
            // [VULOI] Kiểm lý do NG dời vào trong giao dịch (cần sổ để biết phiếu có mang vụ không)
            // [GN-K8] Khoá PO + giao dịch: nhận xong tính lại sổ, ô nào mới âm thì không lưu
            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(txHead.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(txHead.KhPlanDetailId);

            var tx = await _db.HandoverTransactions

                .Include(t => t.Receives)

                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);

            if (tx == null)

                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");

            if (HandoverPurpose.IsOut(tx.Purpose))
                return (false, "Phiếu Huỷ / Trả khách không có bên nhận.");   // [GN-NG-OUT]

            if (tx.Status == "COMPLETED")

                return (false, "Phiếu giao đã nhận đủ.");

            var alreadyReceived = tx.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);
            var remaining       = tx.QtyIssued - alreadyReceived;
            if (qtyOk + qtyNg > remaining)
                return (false, $"Tổng nhận ({qtyOk + qtyNg}) vượt số còn lại của phiếu ({remaining:0.##}).");

            // [VULOI 2026-10-28] Vụ lỗi của lần nhận (đúng cách sổ tính — HandoverLedgerService.Compute):
            //   - Phiếu trả tự lập / phiếu lấy từ ô Hàng lỗi, Hàng sửa (mang vụ): vụ đi theo phiếu, bên nhận KHÔNG chọn; NG đếm lúc nhận vẫn là vụ đó.
            //   - Phiếu không mang vụ: nhận vào ô Hàng lỗi / Hàng sửa, hoặc có NG đếm lúc nhận → vụ MỚI, bắt buộc chọn loại lỗi (DS Lỗi).
            // [TARO-LAMHO-NC] (rà vòng 2 L1) HTSP chỉ làm Taro làm hộ: không nhận hàng vào ô Hàng mới / Hàng lỗi đã xử lý (vào đó không làm tiếp được)
            if (before != null && before.ApplyWorkflow && tx.ToGroupCode == "HTSP" && effCell is HandoverCell.Moi or HandoverCell.Xl
                && tx.Purpose is not (HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn) && HandoverLedgerService.HtspOnlyLamHo(before))
                return (false, "PO này HTSP chỉ làm Taro làm hộ (giao theo NC), không có công đoạn HTSP của mình — không nhận vào ô Hàng mới / Hàng lỗi đã xử lý.");
            var txFromCell = before != null && before.TxFromCell.TryGetValue(handoverTxId, out var tfc) ? tfc : null;
            bool autoTx  = txHead.SourceReceiveId != null;
            bool carryTx = !autoTx && txFromCell is HandoverCell.Loi or HandoverCell.Sua;
            // [TARO-LAMHO-NC] (rà vòng 3 T2) vụ hàng Taro làm hộ bị lỗi chỉ vào ô hàng đạt ở Gia công
            if (before != null && carryTx && tx.ToGroupCode != "GC" && effCell is HandoverCell.Moi or HandoverCell.Xl
                && tx.NgCaseId is long rlh && before.Cases.TryGetValue(rlh, out var rlhC) && rlhC.IsLamHo)
                return (false, $"{NgCaseNo.Format(rlh)} là {rlhC.LamHoTen}: chỉ nhận vào ô Hàng lỗi ở {HandoverLedgerService.GroupLabel(tx.ToGroupCode)} rồi trả về Gia công (Gia công sửa).");   // [LAMHO-NHO-SUA] chữ: bỏ "/ Hàng sửa"
            // [LAMHO-NHO-SUA 2026-11-08] … và không vào ô Hàng sửa của nhóm khác Gia công (chỉ Gia công sửa được vụ làm hộ). Phiếu cũ đã chỉ định ô Hàng sửa:
            //   bên nhận đổi sang ô Hàng lỗi (kèm lý do) rồi trả về Gia công, hoặc bên giao huỷ phiếu. Nguồn: CLAUDE (sổ LAMHO-NHO-SUA); Hùng 08/11 11:24.
            if (before != null && carryTx && tx.ToGroupCode != "GC" && effCell == HandoverCell.Sua
                && tx.NgCaseId is long rlh2 && before.Cases.TryGetValue(rlh2, out var rlhC2) && rlhC2.IsLamHo)
                return (false, LamHoNhoSuaMsg(rlh2, tx.ToGroupCode, rlhC2.LamHoTen) + " Phiếu này: nhận vào ô Hàng lỗi (ghi lý do đổi ô) rồi trả về Gia công, hoặc bên giao huỷ phiếu.");
            bool newCase = !autoTx && !carryTx && (effCell is HandoverCell.Loi or HandoverCell.Sua || qtyNg > 0);
            long? rcvCaseId = autoTx || carryTx ? tx.NgCaseId : null;
            NgCategory? rcvCat = null;
            if (newCase)
            {
                rcvCat = await NgCaseService.GetActiveCategoryAsync(_db, ngCategoryId);
                if (rcvCat == null) return (false, NgCaseService.MsgPickCategory);
                ngReason = rcvCat.Label;
            }
            else if (qtyNg > 0 && string.IsNullOrWhiteSpace(ngReason))
                ngReason = !string.IsNullOrWhiteSpace(txHead.NgReason) ? txHead.NgReason : "Hàng lỗi trả về";   // phiếu mang vụ: lỗi theo phiếu
            if (qtyNg > 0 && string.IsNullOrWhiteSpace(ngReason))
                return (false, "Bắt buộc nhập lý do khi có hàng NG.");
            // [NHAN-DOI-O-NC 2026-11-06] NC nhận của lần nhận này (đổi ô vào Gia công, phiếu không có NC) — theo vụ, K2
            var (eNc, rcvToNc, ncNote) = ReceiveNc(before, tx, desigCell, effCell, carryTx, toNc, toNcReason);
            if (eNc != null) return (false, eNc);
            if (ncNote != null) { notes = string.IsNullOrWhiteSpace(notes) ? ncNote : ncNote + " | " + notes; if (notes.Length > 500) notes = notes[..500]; }
            if (rcvCat != null)
            {
                var vu = await NgCaseService.CreateCaseAsync(_db, tx.KhPlanDetailId, rcvCat,
                    effCell is HandoverCell.Loi or HandoverCell.Sua ? NgOrigin.NhanLoi : NgOrigin.Nhan,
                    tx.ToGroupCode, tx.ToGroupCode == "GC" ? (rcvToNc ?? tx.ToNC) : null, tx.FromGroupCode, receivedByUserId);   // [NHAN-DOI-O-NC] NG lúc nhận ở NC lần nhận
                rcvCaseId = vu.NgCaseId;
            }

            // [GN-RECV-NG2] (bỏ luật riêng phiếu tự lập "chặn NG" — nay mọi phiếu hàng lỗi nhập ở ô Hàng lỗi, xem đầu hàm)

            var rcv = new HandoverReceive   // [GN-RECV-NG] giữ biến để lấy ReceiveId cho phiếu trả tự lập

            {

                HandoverTxId = handoverTxId,

                QtyOk        = qtyOk,

                QtyNg        = qtyNg,

                NgReason     = qtyNg > 0 ? ngReason : null,

                ToCell       = desigCell != null ? effCell : null,                 // [GN-CELL2] ô thực nhận
                ToCellReason = cellChanged ? toCellReason!.Trim() : null,          // [GN-CELL2]
                NgCaseId     = rcvCaseId,                                          // [VULOI]
                ToNC         = rcvToNc,                                            // [NHAN-DOI-O-NC]
                ReceivedBy   = receivedByUserId,

                ReceivedAt   = DateTime.Now,

                Notes        = notes,

                CreatedAt    = DateTime.Now,

            };
            _db.HandoverReceives.Add(rcv);

            var newTotal = alreadyReceived + qtyOk + qtyNg;

            tx.Status = newTotal >= tx.QtyIssued ? "COMPLETED" : "PARTIAL";

            await _db.SaveChangesAsync();

            // [GN-RECV-NG] Master list mục 38: NG đếm lúc nhận → tự lập phiếu TRẢ HÀNG LỖI về bên giao (cùng giao dịch, cùng khoá PO).
            // Bên giao phải bấm Nhận phiếu này thì hàng lỗi mới vào ô Nhận · Hàng lỗi của họ (L2: giao thì có nhận).
            // Trong lúc chờ: tính ở ô Chờ nhận → Tồn của bên giao (mục 37). Phiếu Trả hàng lỗi thì không trả lại lần nữa.
            // [GN-CELL2] Nhận vào ô Hàng lỗi thì toàn bộ là hàng lỗi bên nhận giữ — không trả lại. Trước: xét loại phiếu Trả hàng lỗi.
            if (qtyNg > 0 && effCell != HandoverCell.Loi)
            {
                var now = rcv.ReceivedAt;
                _db.HandoverTransactions.Add(new HandoverTransaction
                {
                    KhPlanDetailId  = tx.KhPlanDetailId,
                    FromGroupCode   = tx.ToGroupCode,
                    ToGroupCode     = tx.FromGroupCode,
                    QtyIssued       = qtyNg,
                    NgQty           = qtyNg,
                    NgReason        = ngReason,
                    IsReturnNg      = true,
                    Purpose         = HandoverPurpose.NgReturn,
                    FromCell        = HandoverCell.Loi,
                    SourceReceiveId = rcv.ReceiveId,
                    NgCaseId        = rcvCaseId,   // [VULOI] phiếu trả mang vụ của phần NG
                    Status          = "PENDING",
                    IssuedBy        = receivedByUserId,
                    IssuedAt        = now,
                    CreatedAt       = now,
                    Notes           = $"Tự lập khi {HandoverLedgerService.GroupLabel(tx.ToGroupCode)} nhận phiếu #{tx.HandoverTxId}: NG đếm lúc nhận",
                });
                await _db.SaveChangesAsync();
            }

            if (before != null)
            {
                var k8 = await CheckNoNewNegativeAsync(txHead.KhPlanDetailId, before);   // [GN-K8]
                if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(txHead.KhPlanDetailId);   // [GN-LIVE] báo mọi màn Giao nhận đang mở tính lại dòng PO này

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        /// <summary>
        /// [NHAN-DOI-O-NC 2026-11-06] NC nhận vào Gia công của 1 lần nhận. Áp khi: nhận vào Gia công ô Hàng lỗi đã xử lý / Hàng mới, phiếu KHÔNG có NC
        ///   nhận (vd phiếu mang vụ giao ô Hàng lỗi / Hàng sửa, bên nhận đổi ô), không phải phiếu theo NC, PO có NC thường. Trước SQL 14: luôn vào NC đầu
        ///   (lùi không lý do, trái K2). Nay theo luật NC làm tiếp như form Giao (HandoverForm.CaseNextNc / CheckNextNc): mặc định NC của vụ, chỉ lùi về
        ///   NC trước + lý do (ghi vào Ghi chú), NC sau chặn; hàng lỗi cũ chưa mã vụ (vụ 0) / phiếu không mang vụ = như hàng mới, được chọn NC khác + lý do.
        ///   Trả (lỗi, NC lưu vào HandoverReceives.ToNC — null = như hàng mới / không áp, chữ thêm vào Ghi chú). Nguồn: HÙNG 06/11 06:56 + 18:11 (ô chọn NC).
        /// </summary>
        static (string? Error, string? ToNc, string? Note) ReceiveNc(LedgerRow? before, HandoverTransaction tx, string? desigCell, string? effCell, bool carryTx, string? toNc, string? reason)
        {
            var raw = string.IsNullOrWhiteSpace(toNc) ? null : toNc.Trim();
            // [NHAN-DOI-O-NC] rà độc lập 06/11 C1 (Cao): CHỈ khi bên nhận đổi từ ô bên giao chỉ định Hàng lỗi / Hàng sửa sang ô đạt. Phiếu chỉ định
            //   sẵn ô Hàng lỗi đã xử lý / Hàng mới: bên giao đã qua luật NC (form Giao — CaseToNcAsync), ToNC trống ở đó là CHỦ Ý ("như hàng mới — làm lại
            //   từ đầu" kèm lý do) → không được tự điền NC vụ đè lên (trước bản vá: hàng phải làm lại từ đầu nằm chờ NC cuối, bỏ qua NC). Cũng hết T4
            //   (phôi Kho → GC Hàng mới không NC hiện ô chọn NC).
            bool applies = before != null && tx.ToGroupCode == "GC" && effCell is HandoverCell.Xl or HandoverCell.Moi
                        && desigCell is HandoverCell.Loi or HandoverCell.Sua
                        && string.IsNullOrWhiteSpace(tx.ToNC)
                        && tx.Purpose is not (HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn)
                        && before.G("GC").Steps.Any(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code));
            if (!applies)
                return raw == null ? (null, null, null) : ("Lần nhận này không chọn NC nhận được (phiếu đã có NC nhận, hoặc không vào Gia công ô Hàng lỗi đã xử lý / Hàng mới).", null, null);
            var caseKey = carryTx ? (tx.NgCaseId ?? 0) : 0;
            var def = HandoverForm.CaseNextNc(before!, caseKey, "GC");
            var chosen = raw == null ? def : raw == FormNewNc ? null : raw;
            // [NHAN-DOI-O-NC] rà độc lập T3: đưa về đúng chữ mã NC như sổ (khoá "in:"+NC phân biệt hoa thường) — trước: gửi "nc2" lưu "nc2", sổ cho vào NC đầu
            if (chosen != null)
                chosen = before!.G("GC").Steps.FirstOrDefault(s => s.Pg == "GC" && string.Equals(s.Code, chosen, StringComparison.OrdinalIgnoreCase))?.Code ?? chosen;
            var (err, add) = HandoverForm.CheckNextNc(before!, def, chosen, reason, free: caseKey == 0);
            if (err != null) return (err, null, null);
            return (null, chosen, add);
        }

        // ----------------------------------------------------------------

        // WRITE: Void RECEIVE

        // ----------------------------------------------------------------

        public Task<(bool Ok, string Error)> VoidReceiveAsync(long receiveId, string voidReason, int voidedByUserId, IReadOnlyCollection<string> editGroups)
            => RetryDeadlockAsync(() => VoidReceiveOnceAsync(receiveId, voidReason, voidedByUserId, editGroups));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> VoidReceiveOnceAsync(

            long receiveId,

            string voidReason,

            int voidedByUserId,

            IReadOnlyCollection<string> editGroups)   // [GN-VOID-RULE] nhóm người huỷ có quyền sửa (Admin: mọi nhóm)

        {

            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)

                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");

            var head = await _db.HandoverReceives.AsNoTracking().Where(r => r.ReceiveId == receiveId)
                .Select(r => new { r.Transaction.KhPlanDetailId, r.ReceivedBy, r.Transaction.ToGroupCode }).FirstOrDefaultAsync();
            if (head == null)
                return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");

            // [VOID-PERM 2026-10-31] Quyền đọc ma trận từ DB (PermCheck), KHÔNG tin danh sách nhóm trang gửi lên;
            //   tài khoản / nhóm đã ngưng hoạt động thì không huỷ / báo được (kể cả chứng từ do chính mình lập).
            var vpGroups = await VoidRightsAsync(voidedByUserId);
            if (vpGroups == null) return (false, VoidInactiveMsg);
            editGroups = vpGroups;
            // [GN-VOID-RULE] Chỉ người bấm Nhận hoặc người có quyền sửa nhóm nhận mới huỷ được lần nhận (máy chủ kiểm, không chỉ dựa vào nút)
            if (!CanVoidReceive(head.ReceivedBy, head.ToGroupCode, voidedByUserId, editGroups))
                return (false, $"Chỉ người đã bấm Nhận hoặc nhóm {HandoverLedgerService.GroupLabel(head.ToGroupCode)} mới huỷ được lần nhận này.");

            // [GN-K8][K5] Huỷ lần nhận mà bên nhận đã làm / đã giao tiếp số đó → chặn (huỷ từ cuối dòng ngược lên)
            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét thêm theo thời gian hàng sửa trả về
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);

            var receive = await _db.HandoverReceives

                .Include(r => r.Transaction).ThenInclude(t => t.Receives)

                .FirstOrDefaultAsync(r => r.ReceiveId == receiveId && !r.IsVoided);

            if (receive == null)

                return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");

            // [GN-RECV-NG] Lần nhận có NG đã sinh phiếu trả tự lập: huỷ lần nhận thì huỷ luôn phiếu trả.
            // [H10 2026-10-30] Bên giao đã nhận lại hàng lỗi ở phiếu trả → huỷ DÂY CHUYỀN trong cùng giao dịch này:
            //   lần nhận phiếu trả + phiếu trả + lần nhận gốc (trước: chặn "Huỷ lần nhận đó trước" → có lúc kẹt vòng: huỷ lần nhận phiếu trả
            //   bị K5 chặn mà chứng từ gây vướng lại chỉ huỷ được sau nó). Sổ kiểm K5 trên trạng thái CUỐI (như chưa từng nhận) — cùng cách
            //   HandoverWhy.OpVoidReceive tính thử. Quyền: người huỷ phải huỷ được cả lần nhận phiếu trả (người bấm Nhận / nhóm nhận phiếu trả).
            var autoBack = await _db.HandoverTransactions.Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.SourceReceiveId == receiveId && !t.IsVoided);
            var cascadeIds = new List<long>();   // [H10] lần nhận phiếu trả huỷ kèm
            if (autoBack != null)
            {
                var backRecs = autoBack.Receives.Where(r => !r.IsVoided).OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.ReceiveId).ToList();
                // [H10] kiểm quyền MỌI lần nhận phiếu trả trước khi đổi gì (bị từ chối thì chưa sửa dòng nào trong bộ nhớ)
                var noRight = backRecs.FirstOrDefault(br => !CanVoidReceive(br.ReceivedBy, autoBack.ToGroupCode, voidedByUserId, editGroups));
                if (noRight != null)
                    return (false, $"{HandoverLedgerService.GroupLabel(autoBack.ToGroupCode)} đã nhận lại hàng lỗi ở phiếu trả #{autoBack.HandoverTxId} (lần nhận #{noRight.ReceiveId}). "
                                 + $"Bạn không có quyền huỷ lần nhận đó — nhờ người đã bấm Nhận hoặc nhóm {HandoverLedgerService.GroupLabel(autoBack.ToGroupCode)} huỷ trước, hoặc người có quyền cả 2 nhóm huỷ một lần.");
                foreach (var br in backRecs)
                {
                    br.IsVoided   = true;
                    br.VoidReason = $"Huỷ theo lần nhận #{receiveId}: {voidReason.Trim()}";
                    if (br.VoidReason.Length > 200) br.VoidReason = br.VoidReason[..200];
                    br.VoidedBy   = voidedByUserId;
                    br.VoidedAt   = DateTime.Now;
                    cascadeIds.Add(br.ReceiveId);
                }
                autoBack.IsVoided   = true;
                autoBack.VoidReason = $"Huỷ theo lần nhận #{receiveId}: {voidReason.Trim()}";
                if (autoBack.VoidReason.Length > 200) autoBack.VoidReason = autoBack.VoidReason[..200];
                autoBack.VoidedBy   = voidedByUserId;
                autoBack.VoidedAt   = DateTime.Now;
            }

            receive.IsVoided   = true;

            receive.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();   // [D10-REASON 29/10] cột 200 ký tự: dài hơn thì cắt (trước: lỗi SQL, trang sập)

            receive.VoidedBy   = voidedByUserId;

            receive.VoidedAt   = DateTime.Now;

            var tx          = receive.Transaction;

            var remainTotal = tx.Receives

                .Where(r => !r.IsVoided && r.ReceiveId != receiveId)

                .Sum(r => r.QtyOk + r.QtyNg);

            tx.Status = remainTotal <= 0            ? "PENDING"

                      : remainTotal >= tx.QtyIssued ? "COMPLETED"

                      : "PARTIAL";

            await _db.SaveChangesAsync();

            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before, beforeIn);   // [GN-K8][K5]
                if (k5 != null)
                {
                    // [GN-WHY] nhả khoá PO (rollback) rồi mới tìm gốc rễ — chỉ đọc, không ghi
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch đã rollback NGAY (trước khi tìm gốc rễ tới 5 s): Dispose lúc thoát hàm gỡ giao dịch hiện tại của kết nối — lúc đó có thể là giao dịch của thao tác khác vừa mở trên cùng DbContext
                    var opDocsR = new HashSet<string> { "R:" + receiveId };   // [H10] + phiếu trả tự lập và các lần nhận huỷ kèm
                    if (autoBack != null) opDocsR.Add("T:" + autoBack.HandoverTxId);
                    foreach (var cid in cascadeIds) opDocsR.Add("R:" + cid);
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidReceive(receiveId), opDocsR, loopOp: true);
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);   // [GN-LIVE] báo mọi màn Giao nhận đang mở tính lại dòng PO này

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        // ----------------------------------------------------------------

        // WRITE: Void ISSUE

        // ----------------------------------------------------------------

        public Task<(bool Ok, string Error)> VoidIssueAsync(long handoverTxId, string voidReason, int voidedByUserId, IReadOnlyCollection<string> editGroups)
            => RetryDeadlockAsync(() => VoidIssueOnceAsync(handoverTxId, voidReason, voidedByUserId, editGroups));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> VoidIssueOnceAsync(

            long handoverTxId,

            string voidReason,

            int voidedByUserId,

            IReadOnlyCollection<string> editGroups)   // [GN-VOID-RULE] nhóm người huỷ có quyền sửa (Admin: mọi nhóm)

        {

            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)

                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");

            var head = await _db.HandoverTransactions.AsNoTracking().Where(t => t.HandoverTxId == handoverTxId && !t.IsVoided)
                .Select(t => new { t.KhPlanDetailId, t.IssuedBy, t.FromGroupCode, t.SourceReceiveId }).FirstOrDefaultAsync();
            if (head == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");

            // [VOID-PERM 2026-10-31] Quyền đọc ma trận từ DB (PermCheck), KHÔNG tin danh sách nhóm trang gửi lên;
            //   tài khoản / nhóm đã ngưng hoạt động thì không huỷ / báo được (kể cả chứng từ do chính mình lập).
            var vpGroups = await VoidRightsAsync(voidedByUserId);
            if (vpGroups == null) return (false, VoidInactiveMsg);
            editGroups = vpGroups;
            // [GN-VOID-RULE] Luật huỷ (Hùng nhắc lại 26/10): chỉ người lập phiếu hoặc nhóm giao (người có quyền sửa nhóm đó) — máy chủ kiểm
            if (!CanVoidIssue(head.IssuedBy, head.FromGroupCode, voidedByUserId, editGroups))
                return (false, $"Chỉ người lập phiếu hoặc nhóm {HandoverLedgerService.GroupLabel(head.FromGroupCode)} mới huỷ được phiếu này.");

            // [GN-VOID-RULE] Phiếu trả hàng lỗi tự lập (NG đếm lúc nhận): huỷ phiếu = huỷ lần nhận gốc (phiếu này huỷ theo).
            //   Không huỷ riêng phiếu: sổ sẽ coi như bên giao gốc đã nhận lại hàng lỗi ngay — sai số.
            //   Lần nhận gốc do chính người / nhóm này bấm (IssuedBy = người nhận, FromGroupCode = nhóm nhận) nên cùng luật quyền.
            if (head.SourceReceiveId != null)
                return await VoidReceiveOnceAsync(head.SourceReceiveId.Value, voidReason, voidedByUserId, editGroups);   // [RS-D10] gọi thân (vòng thử lại ở ngoài)

            // [GN-K8][K5] Huỷ phiếu xong mà ô nào mới âm → chặn
            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét thêm theo thời gian hàng sửa trả về
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);

            var tx = await _db.HandoverTransactions

                .Include(t => t.Receives)

                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);

            if (tx == null)

                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");

            if (tx.Receives.Any(r => !r.IsVoided))
            {
                // [GN-WHY] Phiếu đã có lần nhận: liệt kê đúng các lần nhận phải huỷ trước (mới nhất trước) + chứng từ phía sau đang dùng số đó
                var recs = tx.Receives.Where(r => !r.IsVoided).OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.ReceiveId)
                    .Select(r => new { r.ReceiveId, Qty = r.QtyOk + r.QtyNg, r.ReceivedAt }).ToList();
                var recIds = recs.Select(r => (long?)r.ReceiveId).ToList();
                var backs = await _db.HandoverTransactions.AsNoTracking().Include(t => t.Receives)
                    .Where(t => recIds.Contains(t.SourceReceiveId) && !t.IsVoided).ToListAsync();
                _db.ChangeTracker.Clear();
                await trx.RollbackAsync();   // nhả khoá PO trước khi tìm gốc rễ
                await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch đã rollback NGAY (trước khi tìm gốc rễ tới 5 s): Dispose lúc thoát hàm gỡ giao dịch hiện tại của kết nối — lúc đó có thể là giao dịch của thao tác khác vừa mở trên cùng DbContext
                var tail = new List<string>();
                var opDocs = new HashSet<string> { "T:" + handoverTxId };
                foreach (var r in recs)
                {
                    opDocs.Add("R:" + r.ReceiveId);
                    var kem = new List<string>();   // [H10] phiếu trả tự lập đã được nhận lại: huỷ kèm trong cùng lần huỷ (không còn bước riêng)
                    foreach (var b in backs.Where(b => b.SourceReceiveId == r.ReceiveId))
                    {
                        opDocs.Add("T:" + b.HandoverTxId);
                        var bq = b.Receives.Where(x => !x.IsVoided).ToList();
                        foreach (var br in bq) opDocs.Add("R:" + br.ReceiveId);
                        if (bq.Any()) kem.Add($"huỷ kèm phiếu trả hàng lỗi #{b.HandoverTxId} và lần nhận của nó — {HandoverLedgerService.GroupLabel(b.ToGroupCode)} đã nhận lại {bq.Sum(x => x.QtyOk + x.QtyNg):0.##}, cần quyền {HandoverLedgerService.GroupLabel(b.ToGroupCode)}");
                    }
                    tail.Add($"Huỷ lần nhận #{r.ReceiveId} của phiếu này (nhận {r.Qty:0.##} lúc {r.ReceivedAt:dd/MM HH:mm})" + (kem.Any() ? " — " + string.Join("; ", kem) : ""));
                }
                var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidIssue(handoverTxId), opDocs, tail, loopOp: true);
                return (false, "Phiếu đã có bên nhận xác nhận. Huỷ từng lần nhận trước, hoặc dùng Huỷ phần chưa nhận." + whyTxt);   // [GN-VOID-RULE] chỉ đổi chữ
            }

            // [GN-RECV-NG] Phiếu trả tự lập không huỷ riêng (huỷ lần nhận gốc thì phiếu này huỷ theo)
            if (tx.SourceReceiveId != null)
                return (false, $"Phiếu #{tx.HandoverTxId} do phần mềm tự lập khi nhận hàng có NG. Muốn huỷ thì huỷ lần nhận gốc.");

            tx.IsVoided   = true;

            tx.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();   // [D10-REASON 29/10]

            tx.VoidedBy   = voidedByUserId;

            tx.VoidedAt   = DateTime.Now;

            await _db.SaveChangesAsync();

            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before, beforeIn);   // [GN-K8][K5]
                if (k5 != null)
                {
                    // [GN-WHY] nhả khoá PO (rollback) rồi mới tìm gốc rễ — chỉ đọc, không ghi
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch đã rollback NGAY (trước khi tìm gốc rễ tới 5 s): Dispose lúc thoát hàm gỡ giao dịch hiện tại của kết nối — lúc đó có thể là giao dịch của thao tác khác vừa mở trên cùng DbContext
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidIssue(handoverTxId),
                        new HashSet<string> { "T:" + handoverTxId }, loopOp: true);
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);   // [GN-LIVE] báo mọi màn Giao nhận đang mở tính lại dòng PO này

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        // ----------------------------------------------------------------
        // [GN-VOID-RULE 2026-10-26] Luật huỷ dùng chung (trang + service)
        //   Huỷ phiếu giao / huỷ phần chưa nhận: người lập phiếu, hoặc người có quyền sửa nhóm giao.
        //   Huỷ lần nhận: người bấm Nhận, hoặc người có quyền sửa nhóm nhận.
        //   Admin: mọi nhóm. [VOID-PERM 2026-10-31] máy chủ tự đọc ma trận (VoidRightsAsync), tham số editGroups trang gửi không còn dùng.
        // ----------------------------------------------------------------
        /// <summary>[RS-C2 2026-10-29] null = được; còn lại = lời báo không có quyền sửa nhóm (đọc ma trận từ DB, không tin trang).</summary>
        private async Task<string?> DenyGroupAsync(int userId, string groupCode, string viec)
        {
            var eg = await PermCheck.HandoverEditGroupsAsync(_db, userId);
            return eg.Contains(groupCode) ? null
                : $"Bạn không có quyền sửa nhóm {HandoverLedgerService.GroupLabel(groupCode)} — không {viec} thay nhóm khác được.";
        }

        // [VOID-PERM 2026-10-31] Huỷ phiếu / lần nhận / phần chưa nhận / báo lỗi: nhóm có quyền sửa đọc từ DB.
        //   null = tài khoản hoặc nhóm không hoạt động (không được huỷ gì). Trang vẫn dùng CanVoid* + _myEditGroups chỉ để ẩn / hiện nút.
        private async Task<HashSet<string>?> VoidRightsAsync(int userId)
            => await PermCheck.HandoverEditGroupsOrNullAsync(_db, userId);

        private const string VoidInactiveMsg = "Tài khoản chưa có nhóm hoặc tài khoản / nhóm đang ngưng hoạt động — không huỷ được. Nhờ Admin kiểm tra.";

        public static bool CanVoidIssue(int issuedBy, string fromGroupCode, int userId, IReadOnlyCollection<string> editGroups)
            => issuedBy == userId || editGroups.Contains(fromGroupCode);

        public static bool CanVoidReceive(int receivedBy, string toGroupCode, int userId, IReadOnlyCollection<string> editGroups)
            => receivedBy == userId || editGroups.Contains(toGroupCode);

        // ================================================================
        // [GN-NG-AFTER 2026-10-27] Báo hàng lỗi phát hiện SAU khi nhận (chứng từ nội bộ của 1 nhóm)
        //   Ô hàng tốt của nhóm → Tồn hàng lỗi của nhóm. Không phải phiếu giao (không có bên nhận).
        //   Trần = số đang có ở ô lấy (NgReportCap); khoá PO chung WTS + Giao nhận; lưu xong ô nào mới âm thì không lưu (K5/K8).
        //   Chưa cần PIN (Hùng chốt 27/10). Huỷ: người báo hoặc người có quyền sửa nhóm đó; huỷ cũng qua K5.
        // ================================================================

        /// <summary>Người báo hoặc người có quyền sửa nhóm phát hiện mới huỷ được phiếu báo lỗi.</summary>
        public static bool CanVoidNgReport(int createdBy, string groupCode, int userId, IReadOnlyCollection<string> editGroups)
            => createdBy == userId || editGroups.Contains(groupCode);

        public Task<(bool Ok, string Error)> ReportNgAfterReceiveAsync(
            int khPlanDetailId, string groupCode, string sourceCell, string? nc, decimal qty,
            string origin, string? causeGroupCode, string ngReason, string? notes,
            int userId, IReadOnlyCollection<string> editGroups,
            int? ngCategoryId = null)
            => RetryDeadlockAsync(() => ReportNgAfterReceiveOnceAsync(khPlanDetailId, groupCode, sourceCell, nc, qty,
                   origin, causeGroupCode, ngReason, notes, userId, editGroups, ngCategoryId));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> ReportNgAfterReceiveOnceAsync(   // [RS-D10] thân cũ của ReportNgAfterReceiveAsync, không đổi
            int khPlanDetailId, string groupCode, string sourceCell, string? nc, decimal qty,
            string origin, string? causeGroupCode, string ngReason, string? notes,
            int userId, IReadOnlyCollection<string> editGroups,
            int? ngCategoryId = null,   // [VULOI] loại lỗi (DS Lỗi) — bắt buộc; ngReason giữ tham số cũ, không còn dùng chữ gõ tay
            FormTx? ftx = null)         // [GN-FORM1] chạy trong giao dịch của form "Giao / xử lý hàng" (đã khoá PO; K5 + Commit ở SaveFormAsync)
        {
            nc = HandoverLedgerService.TrimCode(nc);   // [WTS-MA-TRIM]
            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
            if (!validGroups.Contains(groupCode)) return (false, "Mã nhóm không hợp lệ.");
            // [VOID-PERM 2026-10-31] Quyền nhóm đọc ma trận từ DB (như lập phiếu / nhận [RS-C2]), không tin editGroups trang gửi lên (tham số giữ cho trang cũ biên dịch, không dùng)
            var denyRep = await DenyGroupAsync(userId, groupCode, "báo lỗi");
            if (denyRep != null) return (false, denyRep);
            if (qty <= 0) return (false, "Số lượng lỗi phải lớn hơn 0.");
            if (QtyRule.CheckWhole(qty) is string eWholeB) return (false, eWholeB);   // [B3-SO-NGUYEN 2026-11-06] chỉ nhận số nguyên (QtyRule)
            if (groupCode == "KHO" ? sourceCell != HandoverNgSource.Phoi : sourceCell is not (HandoverNgSource.Chua or HandoverNgSource.Da))
                return (false, groupCode == "KHO" ? "Kho báo lỗi từ phôi còn." : "Chọn hàng lỗi đang nằm ở: chưa làm, hay đã làm chưa giao.");
            if (origin is not (HandoverNgSource.DauVao or HandoverNgSource.DauRa))
                return (false, "Chọn nguồn lỗi: đầu vào (lỗi có sẵn) hay đầu ra (nhóm mình làm hỏng).");
            if (origin == HandoverNgSource.DauVao && (string.IsNullOrEmpty(causeGroupCode) || !HandoverNgSource.Causes.Contains(causeGroupCode)))
                return (false, "Lỗi đầu vào: chọn nơi gây lỗi (phôi khách / nhóm trước).");
            if (origin == HandoverNgSource.DauVao && causeGroupCode == groupCode)
                return (false, "Lỗi do chính nhóm mình gây ra là lỗi đầu ra.");
            // [VULOI 2026-10-28] Lỗi chọn từ DS Lỗi (trước: gõ tay ít nhất 3 ký tự)
            var repCat = await NgCaseService.GetActiveCategoryAsync(_db, ngCategoryId);
            if (repCat == null) return (false, NgCaseService.MsgPickCategory);
            ngReason = repCat.Label;
            using var trx = ftx == null ? await _db.Database.BeginTransactionAsync() : null;   // [ISO-FIX 2026-10-31] bỏ Serializable (rò qua kết nối pool), giữ khoá PO ngay dưới — xem IssueWithPurposeOnceAsync
            if (ftx == null)   // [GN-FORM1] form đã mở giao dịch + khoá PO
            {
                var lockErr = await LockPoAsync(khPlanDetailId);   // [GN-K8]
                if (lockErr != null) return (false, lockErr);
            }
            var row = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return (false, "Không tìm thấy PO.");

            bool gcPin = groupCode == "GC" && sourceCell == HandoverNgSource.Chua && row.G("GC").Steps.Any(s => !s.IsExcluded);
            if (gcPin && string.IsNullOrEmpty(nc)) return (false, "Gia công: bắt buộc chọn NC đang chứa hàng lỗi.");
            if (!gcPin) nc = null;
            var (max, ex) = HandoverLedgerService.NgReportCap(row, groupCode, sourceCell, nc);
            if (qty > max)
                return (false, $"Báo lỗi tối đa {max:0.##} ({ex.Title}{(string.IsNullOrEmpty(ex.Arithmetic) ? "" : ": " + ex.Arithmetic)}). Không thể báo {qty:0.##}.");

            // [VULOI] vụ sinh ra: nơi phát hiện = nhóm báo; nơi gây lỗi = nhóm chọn (đầu vào) / chính nhóm (đầu ra)
            var repCase = await NgCaseService.CreateCaseAsync(_db, khPlanDetailId, repCat, NgOrigin.BaoLoi, groupCode, nc,
                origin == HandoverNgSource.DauVao ? causeGroupCode : groupCode, userId);
            _db.HandoverNgReports.Add(new HandoverNgReport
            {
                NgCaseId       = repCase.NgCaseId,   // [VULOI]
                KhPlanDetailId = khPlanDetailId,
                GroupCode      = groupCode,
                SourceCell     = sourceCell,
                NC             = nc,
                Qty            = qty,
                Origin         = origin,
                CauseGroupCode = origin == HandoverNgSource.DauVao ? causeGroupCode : null,
                NgReason       = ngReason.Trim(),
                Notes          = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedBy      = userId,
                CreatedAt      = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            if (ftx != null) { ftx.CaseId = repCase.NgCaseId; ftx.CaseNc = nc; return (true, string.Empty); }   // [GN-FORM1] K5 + Commit ở SaveFormAsync
            var k8 = await CheckNoNewNegativeAsync(khPlanDetailId, row);   // [GN-K8][K5]
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }   // không Commit → rollback
            await trx!.CommitAsync();
            HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE]
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        public Task<(bool Ok, string Error)> VoidNgReportAsync(long reportId, string voidReason, int userId, IReadOnlyCollection<string> editGroups)
            => RetryDeadlockAsync(() => VoidNgReportOnceAsync(reportId, voidReason, userId, editGroups));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> VoidNgReportOnceAsync(long reportId, string voidReason, int userId, IReadOnlyCollection<string> editGroups)
        {
            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)
                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");
            var head = await _db.HandoverNgReports.AsNoTracking().Where(b => b.HandoverNgReportId == reportId && !b.IsVoided)
                .Select(b => new { b.KhPlanDetailId, b.CreatedBy, b.GroupCode }).FirstOrDefaultAsync();
            if (head == null) return (false, "Phiếu báo lỗi không tồn tại hoặc đã huỷ.");
            // [VOID-PERM 2026-10-31] Quyền đọc ma trận từ DB (PermCheck), KHÔNG tin danh sách nhóm trang gửi lên;
            //   tài khoản / nhóm đã ngưng hoạt động thì không huỷ / báo được (kể cả chứng từ do chính mình lập).
            var vpGroups = await VoidRightsAsync(userId);
            if (vpGroups == null) return (false, VoidInactiveMsg);
            editGroups = vpGroups;
            if (!CanVoidNgReport(head.CreatedBy, head.GroupCode, userId, editGroups))
                return (false, $"Chỉ người đã báo hoặc nhóm {HandoverLedgerService.GroupLabel(head.GroupCode)} mới huỷ được phiếu báo lỗi này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(head.KhPlanDetailId);
            var rep = await _db.HandoverNgReports.FirstOrDefaultAsync(b => b.HandoverNgReportId == reportId && !b.IsVoided);
            if (rep == null) return (false, "Phiếu báo lỗi không tồn tại hoặc đã huỷ.");
            rep.IsVoided   = true;
            rep.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();
            rep.VoidedBy   = userId;
            rep.VoidedAt   = DateTime.Now;
            await _db.SaveChangesAsync();
            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before);   // [K5] hàng lỗi đã chuyển / huỷ / trả khách thì không huỷ được
                if (k5 != null)
                {
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch đã rollback NGAY (trước khi tìm gốc rễ tới 5 s): Dispose lúc thoát hàm gỡ giao dịch hiện tại của kết nối — lúc đó có thể là giao dịch của thao tác khác vừa mở trên cùng DbContext
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidNgRep(reportId),
                        new HashSet<string> { "B:" + reportId }, new[] { $"Huỷ phiếu báo lỗi #{reportId}" });
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        public async Task<List<HandoverNgReportItem>> GetNgReportsAsync(int khPlanDetailId)
        {
            var list = await _db.HandoverNgReports.AsNoTracking()
                .Where(b => b.KhPlanDetailId == khPlanDetailId)
                .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.HandoverNgReportId)
                .Select(b => new HandoverNgReportItem
                {
                    Id = b.HandoverNgReportId, GroupCode = b.GroupCode, SourceCell = b.SourceCell, NC = b.NC, Qty = b.Qty,
                    Origin = b.Origin, CauseGroupCode = b.CauseGroupCode, NgReason = b.NgReason, Notes = b.Notes,
                    NgCaseId = b.NgCaseId,   // [VULOI]
                    CreatedBy = b.CreatedBy,
                    CreatedByName = b.CreatedByUser != null ? (b.CreatedByUser.FullName ?? b.CreatedByUser.Username) : "",
                    CreatedAt = b.CreatedAt, IsVoided = b.IsVoided, VoidReason = b.VoidReason,
                    VoidedByName = b.VoidedByUser != null ? (b.VoidedByUser.FullName ?? b.VoidedByUser.Username) : "",
                    VoidedAt = b.VoidedAt
                })
                .ToListAsync();
            // [DS-LOI-LIVE 2026-10-28] báo lỗi có mã vụ → tên loại lỗi hiện tại (DS Lỗi); cột NgReason trong DB giữ nguyên
            var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db, list.Select(b => b.NgCaseId));
            foreach (var b in list) b.NgReason = NgCaseService.LiveReason(b.NgCaseId, b.NgReason, caseLbl) ?? b.NgReason;
            return list;
        }

        // ----------------------------------------------------------------
        // [GN-VOID-RULE] WRITE: Huỷ phần chưa nhận của phiếu nhận dở (PARTIAL)
        //   Phiếu còn lại đúng bằng số đã nhận (COMPLETED); phần chưa nhận về lại ô bên giao (sổ: Giao = số đã nhận).
        //   Ghi lại số đã huỷ / người / lúc / lý do (QtyClosed…) để truy vết; phiếu gốc = QtyIssued + QtyClosed.
        //   Không áp dụng phiếu trả tự lập (hàng lỗi đó đã rời bên nhận gốc ngay lúc nhận — bớt phiếu là mất hàng).
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> CloseRemainderAsync(long handoverTxId, string reason, int userId, IReadOnlyCollection<string> editGroups)
            => RetryDeadlockAsync(() => CloseRemainderOnceAsync(handoverTxId, reason, userId, editGroups));   // [RS-D10] tự thử lại khi bị chọn làm nạn nhân deadlock

        private async Task<(bool Ok, string Error)> CloseRemainderOnceAsync(
            long handoverTxId,
            string reason,
            int userId,
            IReadOnlyCollection<string> editGroups)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");

            var head = await _db.HandoverTransactions.AsNoTracking().Where(t => t.HandoverTxId == handoverTxId && !t.IsVoided)
                .Select(t => new { t.KhPlanDetailId, t.IssuedBy, t.FromGroupCode }).FirstOrDefaultAsync();
            if (head == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            // [VOID-PERM 2026-10-31] Quyền đọc ma trận từ DB (PermCheck), KHÔNG tin danh sách nhóm trang gửi lên;
            //   tài khoản / nhóm đã ngưng hoạt động thì không huỷ / báo được (kể cả chứng từ do chính mình lập).
            var vpGroups = await VoidRightsAsync(userId);
            if (vpGroups == null) return (false, VoidInactiveMsg);
            editGroups = vpGroups;
            if (!CanVoidIssue(head.IssuedBy, head.FromGroupCode, userId, editGroups))
                return (false, $"Chỉ người lập phiếu hoặc nhóm {HandoverLedgerService.GroupLabel(head.FromGroupCode)} mới huỷ được phần chưa nhận.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);   // [GN-K8] khoá chung với WTS / giao nhận
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét thêm theo thời gian hàng sửa trả về
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);

            var tx = await _db.HandoverTransactions
                .Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);
            if (tx == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            if (HandoverPurpose.IsOut(tx.Purpose))
                return (false, "Phiếu Huỷ / Trả khách không có phần chờ nhận.");
            if (tx.SourceReceiveId != null)
                return (false, $"Phiếu #{tx.HandoverTxId} do phần mềm tự lập (NG lúc nhận): không huỷ phần còn lại được. Bên kia nhận nốt, hoặc huỷ lần nhận gốc.");
            var received = tx.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);
            if (received <= 0)
                return (false, "Phiếu chưa có lần nhận nào: dùng Huỷ phiếu giao.");
            var closed = tx.QtyIssued - received;
            if (closed <= 0)
                return (false, "Phiếu đã nhận đủ, không còn phần chưa nhận.");

            var closedAt = DateTime.Now;   // [K5-CLOSE-NHIEU] 1 giờ chung cho dòng huỷ + bộ cột cũ
            tx.QtyIssued    = received;
            tx.QtyClosed    = (tx.QtyClosed ?? 0) + closed;
            tx.ClosedBy     = userId;
            tx.ClosedAt     = closedAt;
            var why = string.IsNullOrEmpty(tx.ClosedReason) ? reason.Trim() : tx.ClosedReason + " | " + reason.Trim();
            tx.ClosedReason = why.Length > 200 ? why[..200] : why;
            // [K5-CLOSE-NHIEU 2026-11-06] mỗi lần huỷ phần chưa nhận = 1 dòng HandoverTxCloses (số, người, giờ, lý do lần này), cùng giao dịch.
            //   Bộ cột cũ ở trên vẫn ghi tổng (trang cũ / báo cáo); sổ đọc từng dòng (HandoverLedgerService.LoadInputsAsync).
            //   Trước: chỉ bộ cột cũ → lần 2 ghi đè giờ / người lần 1 + chặn oan theo thời gian (fuzz seed 602). Nguồn: HÙNG 05/11 19:33 chốt A.
            var why1 = reason.Trim();
            _db.HandoverTxCloses.Add(new HandoverTxClose
            {
                HandoverTxId = tx.HandoverTxId, Qty = closed, ClosedBy = userId, ClosedAt = closedAt,
                Reason = why1.Length > 200 ? why1[..200] : why1, IsMigrated = false, CreatedAt = closedAt,
            });
            if (tx.NgQty > tx.QtyIssued) tx.NgQty = tx.QtyIssued;   // phiếu hàng lỗi: số NG khai báo theo phiếu còn lại
            tx.Status       = "COMPLETED";
            await _db.SaveChangesAsync();

            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before, beforeIn);   // [GN-K8][K5]
                if (k5 != null)
                {
                    // [GN-WHY] nhả khoá PO (rollback) rồi mới tìm gốc rễ — chỉ đọc, không ghi
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch đã rollback NGAY (trước khi tìm gốc rễ tới 5 s): Dispose lúc thoát hàm gỡ giao dịch hiện tại của kết nối — lúc đó có thể là giao dịch của thao tác khác vừa mở trên cùng DbContext
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpCloseRemainder(handoverTxId),
                        new HashSet<string> { "T:" + handoverTxId }, loopOp: true);
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);   // [GN-LIVE]
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------

        // QUERY: Batch summary SL đã giao đi từ KHO

        // ----------------------------------------------------------------

        public async Task<List<KhoIssueSummary>> GetKhoIssueSummaryAsync(

            IEnumerable<int> detailIds)

        {

            var ids = detailIds.ToList();

            var txs = await _db.HandoverTransactions

                .Where(t => ids.Contains(t.KhPlanDetailId)

                         && t.FromGroupCode == "KHO"

                         && !t.IsVoided
                         && t.SourceReceiveId == null   // [GN-RECV-NG] phiếu trả thành phẩm NG tự lập (Kho → Đóng gói) không phải phôi Kho giao
                         // [D11-KHO-SL 2026-10-28] "SL đã giao" của Kho = chỉ phôi tốt (phiếu Hàng mới). Trước: đếm cả gửi sửa / chuyển hàng lỗi /
                         //   huỷ / trả khách → Kho gửi sửa 10 phôi lỗi hiện "Đã giao 10, Giao một phần" dù chưa giao phôi tốt nào.
                         && (t.Purpose == HandoverPurpose.New || (t.Purpose == null && !t.IsReturnNg)))

                .Include(t => t.Receives)

                .ToListAsync();

            return txs

                .GroupBy(t => t.KhPlanDetailId)

                .Select(g =>

                {

                    var confirmedOk = g

                        .SelectMany(t => t.Receives.Where(r => !r.IsVoided))

                        .Sum(r => r.QtyOk);

                    var pending = g

                        .Where(t => t.Status != "COMPLETED")

                        .Sum(t => t.QtyIssued

                            - t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg));

                    return new KhoIssueSummary

                    {

                        KhPlanDetailId = g.Key,

                        TotalConfirmed = confirmedOk,

                        TotalPending   = Math.Max(0, pending),

                        FirstIssuedAt  = g.Min(t => (DateTime?)t.IssuedAt),

                    };

                })

                .ToList();

        }

        // ----------------------------------------------------------------

        // QUERY: Đếm số phiếu PENDING/PARTIAL đang chờ nhóm nhận

        // ----------------------------------------------------------------

        public async Task<Dictionary<int, int>> GetPendingCountPerDetailAsync(

            IEnumerable<int> detailIds,

            IEnumerable<string> toGroupCodes)

        {

            var ids   = detailIds.ToList();

            var codes = toGroupCodes.ToList();

            var counts = await _db.HandoverTransactions

                .Where(t => ids.Contains(t.KhPlanDetailId)

                         && codes.Contains(t.ToGroupCode)

                         && t.Status != "COMPLETED"

                         && !t.IsVoided)

                .GroupBy(t => t.KhPlanDetailId)

                .Select(g => new { KhPlanDetailId = g.Key, Count = g.Count() })

                .ToListAsync();

            return counts.ToDictionary(x => x.KhPlanDetailId, x => x.Count);

        }

        // ----------------------------------------------------------------

        // QUERY: Đếm phiếu PENDING mình đã giao chưa được nhận

        // ----------------------------------------------------------------

        public async Task<Dictionary<int, int>> GetPendingIssuedCountPerDetailAsync(

            IEnumerable<int> detailIds,

            IEnumerable<string> fromGroupCodes)

        {

            var ids   = detailIds.ToList();

            var codes = fromGroupCodes.ToList();

            var counts = await _db.HandoverTransactions

                .Where(t => ids.Contains(t.KhPlanDetailId)

                         && codes.Contains(t.FromGroupCode)

                         && t.Status != "COMPLETED"

                         && !t.IsVoided)

                .GroupBy(t => t.KhPlanDetailId)

                .Select(g => new { KhPlanDetailId = g.Key, Count = g.Count() })

                .ToListAsync();

            return counts.ToDictionary(x => x.KhPlanDetailId, x => x.Count);

        }

        // ----------------------------------------------------------------

        // QUERY: Dòng chảy SL per NC cho 1 nhóm handover

        // ----------------------------------------------------------------

        public async Task<List<NcFlowItem>> GetNcFlowAsync(int khPlanDetailId, string groupCode)

        {

            var items      = new List<NcFlowItem>();

            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };

            if (!validGroups.Contains(groupCode) || groupCode == "KHO")

                return items;

            string[] processGroups;

            switch (groupCode)

            {

                case "GC":

                {

                    var machining = await _db.KhPlanRouteSnapshotMachining

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder)

                        .AsNoTracking()

                        .ToListAsync();

                    var excludedMachines = await _db.KhPlanRouteSnapshotMachineExclude

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .Select(s => s.SoMay)

                        .AsNoTracking()

                        .ToListAsync();

                    var exclSet = excludedMachines

                        .Where(m => !string.IsNullOrWhiteSpace(m))

                        .Select(m => m!.Trim())

                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var m in machining)

                    {

                        items.Add(new NcFlowItem

                        {

                            GroupCode         = "GC",

                            SubGroup          = "GC",

                            StepOrder         = m.StepOrder,

                            NC                = m.NC ?? string.Empty,

                            ParentNC          = m.ParentNC,

                            IsBackup          = m.IsBackup,

                            MachineRegistered = m.MachineRegistered,

                            IsExcluded        = !string.IsNullOrWhiteSpace(m.MachineRegistered)

                                                && exclSet.Contains(m.MachineRegistered.Trim()),

                        });

                    }

                    processGroups = new[] { "GC" };

                    break;

                }

                case "HTSP":

                {

                    var taro = await _db.KhPlanRouteSnapshotTaro

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync();

                    var bavia = await _db.KhPlanRouteSnapshotBavia

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync();

                    var washing = await _db.KhPlanRouteSnapshotWashing

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync();

                    foreach (var s in taro)

                        items.Add(new NcFlowItem { GroupCode = "HTSP", SubGroup = "TARO", StepOrder = s.StepOrder, NC = s.NC ?? string.Empty, ParentNC = s.ParentNC, IsBackup = s.IsBackup, StepName = s.StepName });

                    foreach (var s in bavia)

                        items.Add(new NcFlowItem { GroupCode = "HTSP", SubGroup = "BAVIA", StepOrder = s.StepOrder, NC = s.NC ?? string.Empty, ParentNC = s.ParentNC, IsBackup = s.IsBackup, StepName = s.StepName });

                    foreach (var s in washing)

                        items.Add(new NcFlowItem { GroupCode = "HTSP", SubGroup = "WASHING", StepOrder = s.StepOrder, NC = s.NC ?? string.Empty, ParentNC = s.ParentNC, IsBackup = s.IsBackup, StepName = s.StepName });

                    processGroups = new[] { "TARO", "BAVIA", "WASHING" };

                    break;

                }

                case "KCS":

                {

                    var kcs = await _db.KhPlanRouteSnapshotInspection

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync();

                    foreach (var s in kcs)

                        items.Add(new NcFlowItem { GroupCode = "KCS", SubGroup = "KCS", StepOrder = s.StepOrder, NC = s.NC ?? string.Empty, ParentNC = s.ParentNC, IsBackup = s.IsBackup, StepName = s.StepName });

                    processGroups = new[] { "KCS" };

                    break;

                }

                case "PKG":

                {

                    var pkg = await _db.KhPlanRouteSnapshotPackaging

                        .Where(s => s.KhPlanDetailId == khPlanDetailId)

                        .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync();

                    foreach (var s in pkg)

                        items.Add(new NcFlowItem { GroupCode = "PKG", SubGroup = "PKG", StepOrder = s.StepOrder, NC = s.NC ?? string.Empty, ParentNC = s.ParentNC, IsBackup = s.IsBackup, StepName = s.StepName });

                    processGroups = new[] { "PKG" };

                    break;

                }

                default:

                    return items;

            }

            if (!items.Any()) return items;

            static int SubGroupRank(string sg) => sg switch

            {

                "TARO" => 1, "BAVIA" => 2, "WASHING" => 3, _ => 0

            };

            var mainChain = items

                .Where(i => !i.IsBackup)

                .OrderBy(i => SubGroupRank(i.SubGroup))

                .ThenBy(i => i.StepOrder)

                .ToList();

            for (int i = 0; i < mainChain.Count; i++)

            {

                mainChain[i].IsFirstInChain = (i == 0);

                mainChain[i].IsLastInChain  = (i == mainChain.Count - 1);

            }

            var wtsLogs = await _db.WtsProductionLogs

                .Where(w => w.KhPlanDetailId == khPlanDetailId

                         && processGroups.Contains(w.ProcessGroup)

                         && !w.IsVoided)

                .Select(w => new { w.ProcessGroup, w.NC, w.WtsCode, w.QtyDone })

                .ToListAsync();

            decimal SumWtsForNc(string subGroup, string ncCode) =>

                wtsLogs.Where(w => w.ProcessGroup == subGroup

                                && ((w.NC ?? w.WtsCode ?? string.Empty) == ncCode))

                       .Sum(w => w.QtyDone);

            foreach (var item in items.Where(i => !i.IsBackup))

            {

                var mainDone = SumWtsForNc(item.SubGroup, item.NC);

                var dpCodes = items

                    .Where(d => d.IsBackup && d.SubGroup == item.SubGroup && d.ParentNC == item.NC)

                    .Select(d => d.NC).ToList();

                var dpDone = dpCodes.Sum(code => SumWtsForNc(item.SubGroup, code));

                item.WtsDone = mainDone + dpDone;

            }

            foreach (var item in items.Where(i => i.IsBackup))

                item.WtsDone = SumWtsForNc(item.SubGroup, item.NC);

            var receivesIn = await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && r.Transaction.KhPlanDetailId == khPlanDetailId

                         && r.Transaction.ToGroupCode == groupCode)

                .Select(r => new { r.QtyOk, ToNC = r.ToNC ?? r.Transaction.ToNC })   // [NHAN-DOI-O-NC] NC của lần nhận nếu có

                .ToListAsync();

            var inByNc = receivesIn

                .Where(r => !string.IsNullOrEmpty(r.ToNC))

                .GroupBy(r => r.ToNC!)

                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyOk));

            var inNullTotal = receivesIn.Where(r => string.IsNullOrEmpty(r.ToNC)).Sum(r => r.QtyOk);

            foreach (var item in items.Where(i => !i.IsBackup))

                item.HandoverIn = inByNc.TryGetValue(item.NC, out var v) ? v : 0m;

            if (inNullTotal > 0)

            {

                var firstOverall = mainChain.FirstOrDefault();

                if (firstOverall != null)

                    firstOverall.HandoverIn += inNullTotal;

            }

            var outConfirmed = await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && r.Transaction.KhPlanDetailId == khPlanDetailId

                         && r.Transaction.FromGroupCode == groupCode

                         && !string.IsNullOrEmpty(r.Transaction.FromNC))

                .Select(r => new { r.QtyOk, r.QtyNg, r.Transaction.FromNC })

                .ToListAsync();

            var outConfirmedByNc = outConfirmed

                .GroupBy(r => r.FromNC!)

                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyOk + x.QtyNg));

            var pendingTxs = await _db.HandoverTransactions

                .Where(t => !t.IsVoided

                         && t.KhPlanDetailId == khPlanDetailId

                         && t.FromGroupCode == groupCode

                         && !string.IsNullOrEmpty(t.FromNC)

                         && t.Status != "COMPLETED")

                .Include(t => t.Receives)

                .Select(t => new

                {

                    t.FromNC,

                    t.QtyIssued,

                    ReceivedSoFar = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg)

                })

                .ToListAsync();

            var pendingOutByNc = pendingTxs

                .GroupBy(t => t.FromNC!)

                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, x.QtyIssued - x.ReceivedSoFar)));

            foreach (var item in items.Where(i => !i.IsBackup))

            {

                item.HandoverOutConfirmed = outConfirmedByNc.TryGetValue(item.NC, out var oc) ? oc : 0m;

                item.HandoverOutPending   = pendingOutByNc.TryGetValue(item.NC, out var op)   ? op : 0m;

            }

            foreach (var item in mainChain)

            {

                item.PulledByNext   = 0m;

                item.Remaining      = item.WtsDone + item.HandoverIn

                                    - item.HandoverOutConfirmed - item.HandoverOutPending;

                item.AvailableInput = item.Remaining;

            }

            return items;

        }

    }

    // ── DTO cho Kho/Index ──────────────────────────────────────────────

    public class KhoIssueSummary

    {

        public int       KhPlanDetailId { get; set; }

        public decimal   TotalConfirmed { get; set; }

        public decimal   TotalPending   { get; set; }

        public DateTime? FirstIssuedAt  { get; set; }

    }

}

