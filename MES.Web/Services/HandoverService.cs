using Microsoft.EntityFrameworkCore;

using MES.Web.Data;

using MES.Web.Data.Entities;

namespace MES.Web.Services

{

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class HandoverSummaryRow

    {

        public int    KhPlanDetailId { get; set; }

        public string PlanNo         { get; set; } = string.Empty;

        public string PurchaseOrder  { get; set; } = string.Empty;

        public string PartNo         { get; set; } = string.Empty;

        public string CustomerName   { get; set; } = string.Empty;

        public decimal PlanQty       { get; set; }

        // Kho: chỉ 2 cột (OK nhận từ KH + NG)

        public decimal KhoOk { get; set; }

        public decimal KhoNg { get; set; }

        /// <summary>Số phiếu KHO đã giao đi chưa được xác nhận (PENDING/PARTIAL)</summary>

        public int KhoPendingToIssue { get; set; }

        // Các nhóm sản xuất: 4 cột mỗi nhóm

        public HandoverGroupQty Gc   { get; set; } = new();

        public HandoverGroupQty Htsp { get; set; } = new();

        public HandoverGroupQty Kcs  { get; set; } = new();

        public HandoverGroupQty Pkg  { get; set; } = new();

        public bool HasNg => KhoNg > 0

                          || Gc.NgReturned > 0  || Htsp.NgReturned > 0

                          || Kcs.NgReturned > 0 || Pkg.NgReturned > 0;

    }

    public class HandoverGroupQty

    {

        /// <summary>SL OK nhận vào nhóm này (bên nhận xác nhận)</summary>

        public decimal ReceivedOk { get; set; }

        /// <summary>NG bên nhận báo lại — hiện ở cột NG của nhóm giao</summary>

        public decimal NgReturned { get; set; }

        /// <summary>SL công nhân đã làm xong NC cuối (WtsProductionLogs.QtyDone)</summary>

        public decimal WtsDone { get; set; }

        /// <summary>SL đã giao đi từ nhóm này sang nhóm tiếp (đã được xác nhận nhận)</summary>

        public decimal IssuedOut { get; set; }

        /// <summary>

        /// Còn lại để giao sang nhóm tiếp:

        /// - GC: WtsDone (NC cuối) - IssuedOut — chỉ giao được số đã hoàn thành NC cuối

        /// - Nhóm khác: ReceivedOk - NgReturned - IssuedOut

        /// Cờ IsGc được set bởi CalcGroupQty

        /// </summary>

        public bool    IsGc      { get; set; }

        public decimal Remaining => IsGc

            ? Math.Max(0, WtsDone - IssuedOut)

            : Math.Max(0, ReceivedOk - NgReturned - IssuedOut);

        /// <summary>

        /// v1.0 — SL NG nhóm này đã trả ngược về nhóm trước (IsReturnNg tx).

        /// Dùng để hiển thị badge/tooltip giải thích tại sao Remaining giảm.

        /// </summary>

        public decimal NgReturnedOut { get; set; }

        /// <summary>Số phiếu nhóm này đã giao đi nhưng bên nhận chưa xác nhận</summary>

        public int PendingToIssue   { get; set; }

        /// <summary>Số phiếu đang chờ nhóm này nhận vào</summary>

        public int PendingToReceive { get; set; }

    }

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

        /// <summary>[GN-CELL2] Ô bên nhận thực nhận + lý do nếu đổi khác chỉ định.</summary>
        public string? ToCell         { get; set; }
        public string? ToCellReason   { get; set; }

        public bool    IsVoided       { get; set; }

        public string? VoidReason     { get; set; }

    }

    // ── Internal DTO ──────────────────────────────────────────────────────────

    internal record WtsDoneItem(int KhPlanDetailId, string ProcessGroup, string? NC, int StepOrder, int MaxSnapshotStep, decimal QtyDone);

    /// <summary>NC option cho modal Giao — chọn NC nguồn khi giao inter-group</summary>

    public class NcOption

    {

        public string   NC          { get; set; } = string.Empty;

        public int      StepOrder   { get; set; }

        public decimal  QtyDone     { get; set; }

        // [WF-C2 2026-10-17] SL còn giao được từ NC này. Workflow ON = OK đã làm (gồm NC backup) − đã giao từ NC này
        //   (phiếu đã nhận + đang chờ nhận, không tính phiếu void). Workflow OFF = QtyDone như cũ.
        public decimal  Available   { get; set; }
        public string   Label       => $"NC{StepOrder} ({NC}) — đã làm: {QtyDone:0.##} | còn giao: {Available:0.##}";

    }

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

    public class HandoverService

    {

        private readonly AppDbContext _db;

        private static readonly string[] GcGroups   = { "GC" };

        private static readonly string[] HtspGroups = { "TARO", "BAVIA", "WASHING" };

        private static readonly string[] KcsGroups  = { "KCS" };

        private static readonly string[] PkgGroups  = { "PKG" };

        public HandoverService(AppDbContext db) => _db = db;

        // ----------------------------------------------------------------

        // QUERY: Tóm tắt giao nhận cho trang Index

        // ----------------------------------------------------------------

        public async Task<List<HandoverSummaryRow>> GetSummaryAsync(

            int? customerId = null,

            int? month      = null,

            int? year       = null)

        {

            var q = _db.KhPlanDetails

                .Include(d => d.KhPlan).ThenInclude(p => p!.Customer)

                .AsQueryable();

            if (customerId.HasValue)

                q = q.Where(d => d.KhPlan!.CustomerId == customerId.Value);

            if (month.HasValue && year.HasValue)

                q = q.Where(d => d.KhPlan!.PlanDate.Month == month.Value

                              && d.KhPlan.PlanDate.Year  == year.Value);

            else if (year.HasValue)

                q = q.Where(d => d.KhPlan!.PlanDate.Year == year.Value);

            var details = await q

                .OrderBy(d => d.KhPlan!.PlanNo)

                .ThenBy(d => d.PartNo)

                .ToListAsync();

            if (!details.Any()) return new List<HandoverSummaryRow>();

            var detailIds = details.Select(d => d.KhPlanDetailId).ToList();

            var txList = await _db.HandoverTransactions

                .Where(t => detailIds.Contains(t.KhPlanDetailId) && !t.IsVoided)

                .Include(t => t.IssuedByUser)

                .ToListAsync();

            var txIds = txList.Select(t => t.HandoverTxId).ToList();

            var receiveList = txIds.Any()

                ? await _db.HandoverReceives

                    .Where(r => txIds.Contains(r.HandoverTxId) && !r.IsVoided)

                    .ToListAsync()

                : new List<HandoverReceive>();

            var receivesByTx = receiveList

                .GroupBy(r => r.HandoverTxId)

                .ToDictionary(g => g.Key, g => g.ToList());

            var detailIdsLong = detailIds.Select(id => (long?)id).ToList();

            var khoList = await _db.KhoVatLieus

                .Where(k => k.PlanDetailId != null && detailIdsLong.Contains(k.PlanDetailId))

                .ToListAsync();

            var khoByDetail = khoList

                .Where(k => k.PlanDetailId.HasValue)

                .GroupBy(k => (int)k.PlanDetailId!.Value)

                .ToDictionary(g => g.Key, g => g.ToList());

            var wtsRaw = await _db.WtsProductionLogs

                .Where(w => detailIds.Contains(w.KhPlanDetailId) && !w.IsVoided)

                .Select(w => new { w.KhPlanDetailId, w.ProcessGroup, w.NC, w.QtyDone })

                .ToListAsync();

            var gcSnapshots = await _db.KhPlanRouteSnapshotMachining

                .Where(s => detailIds.Contains(s.KhPlanDetailId))

                .Select(s => new { s.KhPlanDetailId, s.NC, s.StepOrder })

                .ToListAsync();

            var ncStepOrder = gcSnapshots

                .GroupBy(s => new { s.KhPlanDetailId, s.NC })

                .ToDictionary(g => (g.Key.KhPlanDetailId, g.Key.NC ?? ""), g => g.Max(x => x.StepOrder));

            var maxSnapshotStepPerDetail = gcSnapshots

                .GroupBy(s => s.KhPlanDetailId)

                .ToDictionary(g => g.Key, g => g.Max(x => x.StepOrder));

            var wtsLogs = wtsRaw.Select(w => new WtsDoneItem(

                w.KhPlanDetailId, w.ProcessGroup, w.NC,

                ncStepOrder.TryGetValue((w.KhPlanDetailId, w.NC ?? ""), out var so) ? so : 0,

                maxSnapshotStepPerDetail.TryGetValue(w.KhPlanDetailId, out var ms) ? ms : 0,

                w.QtyDone

            )).ToList();

            var wtsByDetail = wtsLogs

                .GroupBy(w => w.KhPlanDetailId)

                .ToDictionary(g => g.Key, g => g.ToList());

            var rows = new List<HandoverSummaryRow>();

            foreach (var d in details)

            {

                var detailTxs = txList

                    .Where(t => t.KhPlanDetailId == d.KhPlanDetailId)

                    .ToList();

                List<WtsDoneItem>? wts = wtsByDetail.TryGetValue(d.KhPlanDetailId, out var wl) ? wl : null;

                var khoOk = khoByDetail.TryGetValue(d.KhPlanDetailId, out var khoItems)

                    ? (decimal)khoItems.Where(k => k.IsActive).Sum(k => k.SoLuongThucNhan ?? 0)

                    : 0m;

                // KhoNg = NG GC báo lại từ tx KHO→GC (GC nhận rồi báo hỏng)

                //       + NG KHO nhận lại từ tx GC→KHO (GC trả ngược hàng NG về)

                var khoNg = CalcNgReturned("KHO", detailTxs, receivesByTx)

                          + CalcNgReceived("KHO", detailTxs, receivesByTx);

                var row = new HandoverSummaryRow

                {

                    KhPlanDetailId    = d.KhPlanDetailId,

                    PlanNo            = d.KhPlan?.PlanNo ?? "",

                    PurchaseOrder     = d.PurchaseOrder,

                    PartNo            = d.PartNo,

                    CustomerName      = d.KhPlan?.Customer?.CustomerName ?? "",

                    PlanQty           = d.Quantity,

                    KhoOk             = khoOk,

                    KhoNg             = khoNg,

                    KhoPendingToIssue = detailTxs.Count(t => t.FromGroupCode == "KHO"

                                                          && t.Status != "COMPLETED"),

                    Gc   = CalcGroupQty("GC",   "KHO", detailTxs, receivesByTx, wts, GcGroups),

                    Htsp = CalcGroupQty("HTSP", "GC",  detailTxs, receivesByTx, wts, HtspGroups),

                    Kcs  = CalcGroupQty("KCS",  "HTSP",detailTxs, receivesByTx, wts, KcsGroups),

                    Pkg  = CalcGroupQty("PKG",  "KCS", detailTxs, receivesByTx, wts, PkgGroups),

                };

                rows.Add(row);

            }

            return rows;

        }

        private static HandoverGroupQty CalcGroupQty(

            string groupCode,

            string _fromGroup,

            List<HandoverTransaction> txs,

            Dictionary<long, List<HandoverReceive>> receivesByTx,

            List<WtsDoneItem>? wts,

            string[] wtsProcessGroups)

        {

            var qty = new HandoverGroupQty();

            foreach (var tx in txs.Where(t => t.ToGroupCode == groupCode))

            {

                if (receivesByTx.TryGetValue(tx.HandoverTxId, out var rxs))

                {

                    qty.ReceivedOk += rxs.Sum(r => r.QtyOk);

                    var totalRx = rxs.Sum(r => r.QtyOk + r.QtyNg);

                    if (tx.Status != "COMPLETED")

                        qty.IssuedOut += 0;

                }

            }

            qty.NgReturned = CalcNgReturned(groupCode, txs, receivesByTx);

            // v1.0: trừ ngay SL NG đang trả ngược (cả PENDING) khỏi "Đã nhận"
            // → ReceivedOk phản ánh hàng OK thực sự đang giữ, không cần đợi bên nhận xác nhận
            var ngReturnPending = txs
                .Where(t => t.FromGroupCode == groupCode && t.IsReturnNg && t.Status != "COMPLETED")
                .Sum(t =>
                {
                    var recv = receivesByTx.TryGetValue(t.HandoverTxId, out var rxs2)
                        ? rxs2.Sum(r => r.QtyOk + r.QtyNg) : 0m;
                    return Math.Max(0m, t.QtyIssued - recv);
                });
            var ngReturnConfirmed = txs
                .Where(t => t.FromGroupCode == groupCode && t.IsReturnNg)
                .SelectMany(t => receivesByTx.TryGetValue(t.HandoverTxId, out var rxs3) ? rxs3 : new List<HandoverReceive>())
                .Sum(r => r.QtyOk + r.QtyNg);
            qty.ReceivedOk = Math.Max(0, qty.ReceivedOk - ngReturnPending - ngReturnConfirmed);

            // IssuedOut = SL bên nhận đã confirmed (QtyOk + QtyNg)

            //           + SL đang PENDING/PARTIAL chưa được nhận (tránh double-giao và phản ánh đúng tồn kho)

            // v1.0: IsReturnNg tx vẫn tính vào IssuedOut — hàng NG đã ra khỏi tay nhóm rồi

            var confirmedOut = txs

                .Where(t => t.FromGroupCode == groupCode)

                .SelectMany(t => receivesByTx.TryGetValue(t.HandoverTxId, out var rxs) ? rxs : new List<HandoverReceive>())

                .Sum(r => r.QtyOk + r.QtyNg);

            var pendingOut = txs

                .Where(t => t.FromGroupCode == groupCode && t.Status != "COMPLETED")

                .Sum(t =>

                {

                    var received = receivesByTx.TryGetValue(t.HandoverTxId, out var rxs)

                        ? rxs.Sum(r => r.QtyOk + r.QtyNg) : 0m;

                    return Math.Max(0m, t.QtyIssued - received);

                });

            qty.IssuedOut = confirmedOut + pendingOut;

            // v1.0: tính riêng SL NG đã trả ngược để hiển thị trên bảng

            qty.NgReturnedOut = txs

                .Where(t => t.FromGroupCode == groupCode && t.IsReturnNg)

                .SelectMany(t => receivesByTx.TryGetValue(t.HandoverTxId, out var rxs2) ? rxs2 : new List<HandoverReceive>())

                .Sum(r => r.QtyOk + r.QtyNg)

              + txs

                .Where(t => t.FromGroupCode == groupCode && t.IsReturnNg && t.Status != "COMPLETED")

                .Sum(t =>

                {

                    var recv = receivesByTx.TryGetValue(t.HandoverTxId, out var rxs3)

                        ? rxs3.Sum(r => r.QtyOk + r.QtyNg) : 0m;

                    return Math.Max(0m, t.QtyIssued - recv);

                });

            if (wts != null)

            {

                var groupLogs = wts.Where(w => wtsProcessGroups.Contains(w.ProcessGroup)).ToList();

                if (groupCode == "GC")

                {

                    if (groupLogs.Any())

                    {

                        var maxSnapshotStep = groupLogs.Max(w => w.MaxSnapshotStep);

                        var lastNcLogs = groupLogs.Where(w => w.StepOrder == maxSnapshotStep).ToList();

                        qty.WtsDone = lastNcLogs.Any() ? lastNcLogs.Sum(w => w.QtyDone) : 0;

                    }

                }

                else

                {

                    qty.WtsDone = groupLogs.Sum(w => w.QtyDone);

                }

            }

            // Số phiếu nhóm này đã giao đi chưa COMPLETED

            qty.PendingToIssue = txs

                .Count(t => t.FromGroupCode == groupCode && t.Status != "COMPLETED");

            // Số phiếu đang chờ nhóm này nhận (PENDING/PARTIAL)

            qty.PendingToReceive = txs

                .Count(t => t.ToGroupCode == groupCode && t.Status != "COMPLETED");

            if (groupCode == "GC") qty.IsGc = true;

            return qty;

        }

        private static decimal CalcNgReturned(

            string groupCode,

            List<HandoverTransaction> txs,

            Dictionary<long, List<HandoverReceive>> receivesByTx)

        {

            decimal ng = 0;

            foreach (var tx in txs.Where(t => t.FromGroupCode == groupCode))

            {

                if (receivesByTx.TryGetValue(tx.HandoverTxId, out var rxs))

                    ng += rxs.Sum(r => r.QtyNg);

            }

            return ng;

        }

        /// <summary>

        /// NG nhận VÀO nhóm này — nhóm khác giao hàng NG ngược về nhóm này,

        /// nhóm này nhận và nhập QtyNg.

        /// VD: GC→KHO trả 5 NG, KHO nhận QtyNg=5 → CalcNgReceived("KHO") = 5

        /// </summary>

        private static decimal CalcNgReceived(

            string groupCode,

            List<HandoverTransaction> txs,

            Dictionary<long, List<HandoverReceive>> receivesByTx)

        {

            decimal ng = 0;

            foreach (var tx in txs.Where(t => t.ToGroupCode == groupCode))

            {

                if (receivesByTx.TryGetValue(tx.HandoverTxId, out var rxs))

                    ng += rxs.Sum(r => r.QtyNg);

            }

            return ng;

        }

        // ----------------------------------------------------------------

        // QUERY: Log chi tiết của 1 KhPlanDetail

        // ----------------------------------------------------------------

        public async Task<List<HandoverLogItem>> GetLogsAsync(int khPlanDetailId)

        {

            var txs = await _db.HandoverTransactions

                .Where(t => t.KhPlanDetailId == khPlanDetailId)

                .Include(t => t.IssuedByUser)

                .Include(t => t.Receives).ThenInclude(r => r.ReceivedByUser)

                .OrderByDescending(t => t.IssuedAt)

                .ToListAsync();

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

                Notes         = t.Notes,

                NgQty         = t.NgQty,       // v0.9

                NgReason      = t.NgReason,    // v0.9

                IsVoided      = t.IsVoided,

                VoidReason    = t.VoidReason,
                Purpose       = t.Purpose,     // [GN-LEDGER]
                IsReturnNg    = t.IsReturnNg,  // [GN-LEDGER]
                FromCell      = t.FromCell,    // [GN-CELL]
                SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                ToCell        = HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell : (t.Purpose ?? (t.IsReturnNg ? HandoverPurpose.NgReturn : null)) is string lp ? HandoverCell.ReceiveCellOf(lp) : null, // [GN-CELL2]
                ToNC          = t.ToNC,        // [GN-LEDGER]

                Receives      = t.Receives.OrderBy(r => r.ReceivedAt).Select(r => new HandoverReceiveItem

                {

                    ReceiveId      = r.ReceiveId,

                    QtyOk          = r.QtyOk,

                    QtyNg          = r.QtyNg,

                    NgReason       = r.NgReason,

                    ReceivedAt     = r.ReceivedAt,

                    ReceivedByName = r.ReceivedByUser?.FullName ?? r.ReceivedByUser?.Username ?? "",
                    ToCell         = r.ToCell,         // [GN-CELL2]
                    ToCellReason   = r.ToCellReason,   // [GN-CELL2]

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

            var txs = await _db.HandoverTransactions

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

                    NgReason         = t.NgReason,  // v0.9
                    Purpose          = t.Purpose,     // [GN-LEDGER]
                    IsReturnNg       = t.IsReturnNg,  // [GN-LEDGER]
                    FromCell         = t.FromCell,    // [GN-CELL]
                    SourceReceiveId  = t.SourceReceiveId,   // [GN-RECV-NG]
                    SourceTxId       = t.SourceReceiveId != null && srcTx.TryGetValue(t.SourceReceiveId.Value, out var stx) ? stx : null,
                    ToCell           = t.SourceReceiveId != null ? HandoverCell.Loi   // [GN-CELL2] ô bên giao chỉ định (phiếu cũ: theo loại phiếu)
                                     : HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell
                                     : (t.Purpose ?? (t.IsReturnNg ? HandoverPurpose.NgReturn : null)) is string pp2 ? HandoverCell.ReceiveCellOf(pp2) : null,

                };

            }).ToList();

        }

        // ----------------------------------------------------------------

        // QUERY: SL đã nhận vào 1 nhóm cho 1 detail — dùng cho WTS gate

        // ----------------------------------------------------------------

        public async Task<decimal> GetReceivedOkAsync(int khPlanDetailId, string groupCode)

        {

            if (groupCode == "GC")

            {

                return await _db.HandoverReceives

                    .Where(r => !r.IsVoided

                             && !r.Transaction.IsVoided

                             && r.Transaction.KhPlanDetailId == khPlanDetailId

                             && r.Transaction.ToGroupCode == "GC")

                    .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

            }

            return await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && r.Transaction.KhPlanDetailId == khPlanDetailId

                         && r.Transaction.ToGroupCode == groupCode)

                .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

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

            return result.ToHashSet();

        }

        // ----------------------------------------------------------------

        // QUERY: Lấy danh sách NC + QtyDone theo nhóm

        // ----------------------------------------------------------------

        // [WF-C2 2026-10-17] Đổi tên thành ...BaseAsync (logic giữ nguyên) — GetNcOptionsAsync mới bọc thêm Available
        private async Task<List<NcOption>> GetNcOptionsBaseAsync(

            int khPlanDetailId,

            string fromGroupCode)

        {

            var snapshots = await _db.KhPlanRouteSnapshotMachining

                .Where(s => s.KhPlanDetailId == khPlanDetailId && !s.IsBackup)

                .OrderBy(s => s.StepOrder)

                .Select(s => new { s.NC, s.StepOrder })

                .ToListAsync();

            if (!snapshots.Any()) return new List<NcOption>();

            var processGroups = fromGroupCode switch

            {

                "GC"   => GcGroups,

                "HTSP" => HtspGroups,

                "KCS"  => KcsGroups,

                "PKG"  => PkgGroups,

                _      => Array.Empty<string>()

            };

            var wtsLogs = await _db.WtsProductionLogs

                .Where(w => w.KhPlanDetailId == khPlanDetailId

                         && processGroups.Contains(w.ProcessGroup)

                         && !w.IsVoided)

                .Select(w => new { w.NC, w.QtyDone })

                .ToListAsync();

            var qtyByNc = wtsLogs

                .Where(w => w.NC != null)

                .GroupBy(w => w.NC!)

                .ToDictionary(g => g.Key, g => g.Sum(w => w.QtyDone));

            return snapshots

                .Select(s => new NcOption

                {

                    NC        = s.NC ?? "",

                    StepOrder = s.StepOrder,

                    QtyDone   = qtyByNc.TryGetValue(s.NC ?? "", out var q) ? q : 0m,

                })

                .Where(o => o.QtyDone > 0)

                .ToList();

        }

        // [WF-C2 2026-10-17] Tách phần return để gắn Available cho từng NC (xem NcOption.Available)
        public async Task<List<NcOption>> GetNcOptionsAsync(
            int khPlanDetailId,
            string fromGroupCode)
        {
            var options = await GetNcOptionsBaseAsync(khPlanDetailId, fromGroupCode);
            if (!options.Any()) return options;
            var applyWf = await _db.KhPlanDetails
                .Where(d => d.KhPlanDetailId == khPlanDetailId)
                .Select(d => d.KhPlan.ApplyWorkflow)
                .FirstOrDefaultAsync();
            if (!applyWf)
            {
                foreach (var o in options) o.Available = o.QtyDone; // Workflow OFF: giữ như cũ
                return options;
            }
            var flow  = await GetNcFlowAsync(khPlanDetailId, fromGroupCode);
            var avail = await GetNcIssueAvailableAsync(khPlanDetailId, fromGroupCode, flow);
            foreach (var o in options)
                o.Available = avail.TryGetValue(o.NC, out var a) ? a : 0m;
            return options;
        }

        // [WF-C2 2026-10-17] SL còn giao được theo NC chính = OK đã làm (NC chính + NC backup) − đã giao từ NC đó.
        //   "Đã giao" lấy đúng cách GetNcFlowAsync tính (HandoverOutConfirmed + HandoverOutPending).
        //   Log cũ chưa có QtyOk → lấy QtyDone.
        private async Task<Dictionary<string, decimal>> GetNcIssueAvailableAsync(
            int khPlanDetailId, string fromGroupCode, List<NcFlowItem> flow)
        {
            var result = new Dictionary<string, decimal>();
            var processGroups = fromGroupCode switch
            {
                "GC"   => GcGroups,
                "HTSP" => HtspGroups,
                "KCS"  => KcsGroups,
                "PKG"  => PkgGroups,
                _      => Array.Empty<string>()
            };
            var logs = await _db.WtsProductionLogs
                .Where(w => w.KhPlanDetailId == khPlanDetailId
                         && processGroups.Contains(w.ProcessGroup)
                         && !w.IsVoided)
                .Select(w => new { w.NC, w.WtsCode, w.QtyOk, w.QtyDone })
                .ToListAsync();
            foreach (var item in flow.Where(f => !f.IsBackup))
            {
                var codes = flow.Where(f => f.IsBackup && f.SubGroup == item.SubGroup && f.ParentNC == item.NC)
                                .Select(f => f.NC).ToHashSet();
                codes.Add(item.NC);
                var okDone = logs.Where(w => codes.Contains(w.NC ?? w.WtsCode ?? string.Empty))
                                 .Sum(w => w.QtyOk ?? w.QtyDone);
                result[item.NC] = Math.Max(0m, okDone - item.HandoverOutConfirmed - item.HandoverOutPending);
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════
        // [WF-C1 2026-10-17] Giao XUÔI từ HTSP / KCS / Đóng gói (Workflow ON)
        //   Dây chuyền: KHO → GC → HTSP → KCS → PKG → KHO (nhập kho thành phẩm).
        //   Giao xuôi : tối đa = min(SL OK đã làm của TỪNG Mã CV trong nhóm) − đã giao xuôi,
        //               và không vượt tồn nhóm (GetRemainingAsync). Mã CV làm thứ tự nào cũng được.
        //   Giao ngược: (lên nhóm trước, VD HTSP làm hộ xong trả GC) → GIỮ logic cũ (GetRemainingAsync).
        //   Bỏ qua Mã CV chuẩn bị 1V/1T/1K/1G (không có SL). NC backup cộng vào NC chính.
        //   GC / KHO giữ nguyên logic cũ (GC đã chặn theo NC cuối trong GetRemainingAsync).
        // ═══════════════════════════════════════════════════════════════════
        private static int GroupRank(string? g) => g switch
        {
            "KHO" => 0, "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1
        };

        /// <summary>true = giao xuôi dây chuyền; PKG → KHO là nhập kho thành phẩm (xuôi).</summary>
        public static bool IsForwardIssue(string? fromGroupCode, string? toGroupCode)
            => toGroupCode == "KHO" ? fromGroupCode == "PKG" : GroupRank(toGroupCode) > GroupRank(fromGroupCode);

        private static readonly string[] FlatTimeCodes = { "1V", "1T", "1K", "1G" };
        private static bool IsFlatTimeCode(string? code)
            => !string.IsNullOrWhiteSpace(code) && FlatTimeCodes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>SL tối đa được giao (không theo NC) từ nhóm from sang nhóm to.</summary>
        public async Task<decimal> GetIssueMaxAsync(int khPlanDetailId, string fromGroupCode, string? toGroupCode)
        {
            var remaining = await GetRemainingAsync(khPlanDetailId, fromGroupCode);
            if (fromGroupCode is not ("HTSP" or "KCS" or "PKG")) return remaining;
            if (string.IsNullOrEmpty(toGroupCode) || !IsForwardIssue(fromGroupCode, toGroupCode)) return remaining;
            var applyWf = await _db.KhPlanDetails
                .Where(d => d.KhPlanDetailId == khPlanDetailId)
                .Select(d => d.KhPlan.ApplyWorkflow)
                .FirstOrDefaultAsync();
            if (!applyWf) return remaining;
            var cap = await GetNonGcForwardCapAsync(khPlanDetailId, fromGroupCode);
            return cap.HasValue ? Math.Min(remaining, cap.Value) : remaining;
        }

        // min(SL OK từng Mã CV) − đã giao xuôi. null = nhóm không có Mã CV tính SL → không giới hạn thêm.
        private async Task<decimal?> GetNonGcForwardCapAsync(int khPlanDetailId, string groupCode)
        {
            var steps = new List<(string Pg, string Code, string Nc, bool IsBackup, string? ParentNC)>();
            string[] processGroups;
            if (groupCode == "HTSP")
            {
                var taro = await _db.KhPlanRouteSnapshotTaro.Where(s => s.KhPlanDetailId == khPlanDetailId)
                    .Select(s => new { s.NC, s.WtsTaskCode, s.IsBackup, s.ParentNC }).AsNoTracking().ToListAsync();
                var bavia = await _db.KhPlanRouteSnapshotBavia.Where(s => s.KhPlanDetailId == khPlanDetailId)
                    .Select(s => new { s.NC, s.IsBackup, s.ParentNC }).AsNoTracking().ToListAsync();
                var washing = await _db.KhPlanRouteSnapshotWashing.Where(s => s.KhPlanDetailId == khPlanDetailId)
                    .Select(s => new { s.NC, s.IsBackup, s.ParentNC }).AsNoTracking().ToListAsync();
                // Mã CV Taro trên WTS = WtsTaskCode ?? NC (khớp HieuSuat_Index); Bavia/Rửa = NC
                steps.AddRange(taro.Select(s => ("TARO", s.WtsTaskCode ?? s.NC, s.NC, s.IsBackup, s.ParentNC)));
                steps.AddRange(bavia.Select(s => ("BAVIA", s.NC, s.NC, s.IsBackup, s.ParentNC)));
                steps.AddRange(washing.Select(s => ("WASHING", s.NC, s.NC, s.IsBackup, s.ParentNC)));
                processGroups = HtspGroups;
            }
            else if (groupCode == "KCS")
            {
                var kcs = await _db.KhPlanRouteSnapshotInspection.Where(s => s.KhPlanDetailId == khPlanDetailId)
                    .Select(s => new { s.NC, s.IsBackup, s.ParentNC }).AsNoTracking().ToListAsync();
                steps.AddRange(kcs.Select(s => ("KCS", s.NC, s.NC, s.IsBackup, s.ParentNC)));
                processGroups = KcsGroups;
            }
            else if (groupCode == "PKG")
            {
                var pkg = await _db.KhPlanRouteSnapshotPackaging.Where(s => s.KhPlanDetailId == khPlanDetailId)
                    .Select(s => new { s.NC, s.IsBackup, s.ParentNC }).AsNoTracking().ToListAsync();
                steps.AddRange(pkg.Select(s => ("PKG", s.NC, s.NC, s.IsBackup, s.ParentNC)));
                processGroups = PkgGroups;
            }
            else return null;

            var mains = steps.Where(s => !s.IsBackup && !IsFlatTimeCode(s.Code)).ToList();
            if (!mains.Any()) return null;

            var logs = await _db.WtsProductionLogs
                .Where(w => w.KhPlanDetailId == khPlanDetailId
                         && processGroups.Contains(w.ProcessGroup)
                         && !w.IsVoided)
                .Select(w => new { w.ProcessGroup, w.WtsCode, w.NC, w.QtyOk, w.QtyDone })
                .ToListAsync();

            decimal minOk = decimal.MaxValue;
            foreach (var m in mains)
            {
                var codes = steps.Where(b => b.IsBackup && b.Pg == m.Pg && b.ParentNC == m.Nc)
                                 .Select(b => b.Code).ToHashSet();
                codes.Add(m.Code);
                var ok = logs.Where(w => string.Equals(w.ProcessGroup, m.Pg, StringComparison.OrdinalIgnoreCase)
                                      && codes.Contains(w.WtsCode ?? w.NC ?? string.Empty))
                             .Sum(w => w.QtyOk ?? w.QtyDone);
                if (ok < minOk) minOk = ok;
            }

            // Đã giao xuôi (không tính phiếu void, phiếu trả NG ngược, phiếu giao ngược)
            var txs = await _db.HandoverTransactions
                .Where(t => !t.IsVoided
                         && t.KhPlanDetailId == khPlanDetailId
                         && t.FromGroupCode == groupCode
                         && !t.IsReturnNg)
                .Include(t => t.Receives)
                .AsNoTracking()
                .ToListAsync();
            var fwdOut = txs.Where(t => IsForwardIssue(groupCode, t.ToGroupCode)).Sum(t =>
            {
                var rec = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);
                return t.Status == "COMPLETED" ? rec : Math.Max(rec, t.QtyIssued);
            });

            return Math.Max(0m, minOk - fwdOut);
        }

        // ----------------------------------------------------------------
        // WRITE: Giao hàng

        // v0.9: thêm ngQty, ngReason — bên giao khai báo NG ngay khi tạo phiếu

        // ----------------------------------------------------------------

        public async Task<(bool Ok, string Error)> IssueAsync(

            int khPlanDetailId,

            string fromGroupCode,

            string toGroupCode,

            decimal qtyIssued,

            int issuedByUserId,

            string? fromNC      = null,

            string? notes       = null,

            string? toNC        = null,

            decimal ngQty       = 0,

            string? ngReason    = null,

            bool isReturnNg     = false,   // v1.0 — phiếu trả hàng NG ngược chiều
            string? purpose     = null)    // [GN-LEDGER 2026-10-22] loại phiếu mới — có thì dùng trần của sổ giao nhận

        {
            // [GN-LEDGER 2026-10-22] Trang Giao nhận mới luôn gửi purpose → kiểm theo hàm tính chung, bỏ qua nhánh cũ
            if (!string.IsNullOrEmpty(purpose))
                return await IssueWithPurposeAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                    issuedByUserId, purpose, fromNC, toNC, notes, ngReason);


            if (qtyIssued <= 0)

                return (false, "Số lượng giao phải lớn hơn 0.");

            if (fromGroupCode == toGroupCode)

                return (false, "Nhóm giao và nhóm nhận không được trùng nhau.");

            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };

            if (!validGroups.Contains(fromGroupCode) || !validGroups.Contains(toGroupCode))

                return (false, "Mã nhóm không hợp lệ.");

            // v0.9: validate NgQty (tổng số lượng giao = OK + NG)

            if (ngQty < 0)

                return (false, "Số lượng NG không được âm.");

            if (ngQty > qtyIssued)

                return (false, $"Số lượng NG ({ngQty}) không thể lớn hơn tổng số lượng giao ({qtyIssued}).");

            if (ngQty > 0 && string.IsNullOrWhiteSpace(ngReason))

                return (false, "Bắt buộc nhập lý do khi có hàng NG.");

            // v1.0 — Giao NG ngược: toàn bộ QtyIssued là NG, lấy từ SL đang giữ (ReceivedOk chưa xử lý)

            if (isReturnNg)

            {

                if (string.IsNullOrWhiteSpace(ngReason))

                    return (false, "Bắt buộc nhập lý do khi trả hàng NG ngược.");

                var ngRemain = await GetRemainingForNgReturnAsync(khPlanDetailId, fromGroupCode);

                if (qtyIssued > ngRemain)

                    return (false, $"Số lượng NG trả về ({qtyIssued}) vượt quá SL đang giữ tại {fromGroupCode} ({ngRemain:0.##}).");

            }

            else if (!string.IsNullOrEmpty(fromNC))

            {

                var flow   = await GetNcFlowAsync(khPlanDetailId, fromGroupCode);

                var ncItem = flow.FirstOrDefault(f => !f.IsBackup && f.NC == fromNC);

                if (ncItem == null)

                    return (false, $"NC {fromNC} không tìm thấy trong snapshot nhóm {fromGroupCode}.");

                // [WF-C2 2026-10-17] Workflow ON + NC đã có WTS: SL giao ≤ OK đã làm − đã giao từ NC này.
                //   Trước đây so với TỔNG đã làm (không trừ phần đã giao) → giao trùng nhiều lần được.
                //   Workflow OFF hoặc NC chưa có WTS → giữ nguyên kiểm tra cũ bên dưới.
                var applyWfNc = await _db.KhPlanDetails
                    .Where(d => d.KhPlanDetailId == khPlanDetailId)
                    .Select(d => d.KhPlan.ApplyWorkflow)
                    .FirstOrDefaultAsync();
                if (applyWfNc && ncItem.WtsDone > 0)
                {
                    var availNc = await GetNcIssueAvailableAsync(khPlanDetailId, fromGroupCode, flow);
                    var canIssue = availNc.TryGetValue(fromNC, out var av) ? av : 0m;
                    if (qtyIssued > canIssue)
                        return (false, $"NC {fromNC}: còn giao được {canIssue:0.##} (= OK đã làm − đã giao từ NC này) — không thể giao {qtyIssued:0.##}.");
                }
                else if (qtyIssued > ncItem.WtsDone && ncItem.WtsDone > 0)

                    return (false, $"NC {fromNC}: đã làm {ncItem.WtsDone:0.##} — không thể giao {qtyIssued:0.##} lần này.");

                if (ncItem.WtsDone == 0)

                {

                    var groupRemain = await GetRemainingAsync(khPlanDetailId, fromGroupCode);

                    if (qtyIssued > groupRemain)

                        return (false, $"NC {fromNC} chưa có WTS — còn tồn nhóm {fromGroupCode} ({groupRemain:0.##}) không đủ để giao {qtyIssued:0.##}.");

                }

            }

            else

            {

                var remaining = await GetRemainingAsync(khPlanDetailId, fromGroupCode);

                if (qtyIssued > remaining)

                    return (false, $"Tổng số lượng giao ({qtyIssued}) vượt quá còn lại tại {fromGroupCode} ({remaining:0.##}).");
                // [WF-C1 2026-10-17] Giao xuôi từ HTSP/KCS/PKG (Workflow ON): ≤ min SL OK các Mã CV − đã giao xuôi
                var fwdMax = await GetIssueMaxAsync(khPlanDetailId, fromGroupCode, toGroupCode);
                if (fwdMax < remaining && qtyIssued > fwdMax)
                    return (false, $"Workflow: {fromGroupCode} chỉ giao xuôi được {fwdMax:0.##} (= SL OK nhỏ nhất trong các Mã CV đã làm − đã giao xuôi). Không thể giao {qtyIssued:0.##}.");

            }

            // v1.0: giao ngược NG — không gắn NC, không gắn ToNC

            var effectiveToNc   = (toGroupCode == "KHO" || isReturnNg) ? null : toNC;

            var effectiveFromNc = isReturnNg ? null : fromNC;

            _db.HandoverTransactions.Add(new HandoverTransaction

            {

                KhPlanDetailId = khPlanDetailId,

                FromGroupCode  = fromGroupCode,

                ToGroupCode    = toGroupCode,

                QtyIssued      = qtyIssued,

                FromNC         = effectiveFromNc,

                ToNC           = effectiveToNc,

                NgQty          = isReturnNg ? qtyIssued : ngQty,          // v1.0: ReturnNg → toàn bộ là NG

                NgReason       = ngReason?.Trim(),

                IsReturnNg     = isReturnNg,                               // v1.0

                Status         = "PENDING",

                IssuedBy       = issuedByUserId,

                IssuedAt       = DateTime.Now,

                Notes          = notes,

                CreatedAt      = DateTime.Now,

            });

            await _db.SaveChangesAsync();

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        // ----------------------------------------------------------------
        // [GN-LEDGER 2026-10-22] WRITE: Giao hàng theo loại phiếu (trang Giao nhận mới)
        //   Trần lấy từ HandoverLedgerService.IssueCap — cùng một hàm với số hiển thị trên trang.
        //   K8: kiểm trần + ghi phiếu trong 1 giao dịch Serializable → 2 người giao cùng lúc không vượt trần.
        // ----------------------------------------------------------------
        /// <summary>
        /// [GN-CELL 2026-10-23] Giao theo Ô Excel (form Giao mới): người giao chọn ô (Hàng mới / Hàng lỗi / Hàng sửa),
        /// tích "Hàng lỗi công đoạn trước", với ô Hàng lỗi không tích thì chọn "gửi nhờ sửa" hay "đã sửa xong trả về".
        /// Phần mềm suy loại phiếu (Purpose) + ô bên nhận, rồi kiểm trần / tuyến như IssueWithPurposeAsync.
        /// </summary>
        public async Task<(bool Ok, string Error)> IssueByCellAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued, int issuedByUserId,
            string cell, bool prevStageNg, string toCell,   // [GN-CELL2] trước: bool repaired — nay bên giao chọn thẳng ô bên nhận
            string? fromNC = null, string? toNC = null, string? notes = null, string? reason = null)
        {
            var (purpose, err) = HandoverCell.ResolveCells(cell, prevStageNg, toCell);   // [GN-CELL2]
            if (purpose == null) return (false, err ?? "Phân loại hàng không hợp lệ."); // [GN-PLH] mục 36
            return await IssueWithPurposeAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                issuedByUserId, purpose, fromNC, toNC, notes, reason, cell, toCell);
        }

        /// <summary>[GN-CELL] Phiếu ngoài 4 ô Excel (Làm hộ gửi / trả, Huỷ, Trả khách) — vẫn đi qua cùng một hàm kiểm.</summary>
        public async Task<(bool Ok, string Error)> IssueSpecialAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued, int issuedByUserId,
            string purpose, string? fromNC = null, string? toNC = null, string? notes = null, string? reason = null)
        {
            if (purpose is not (HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn or HandoverPurpose.Scrap or HandoverPurpose.CustReturn))
                return (false, "Loại phiếu này phải lập theo ô Excel (Hàng mới / Hàng lỗi / Hàng sửa).");
            var cell = purpose is HandoverPurpose.Scrap or HandoverPurpose.CustReturn ? HandoverCell.Loi : null;
            return await IssueWithPurposeAsync(khPlanDetailId, fromGroupCode, toGroupCode, qtyIssued,
                issuedByUserId, purpose, fromNC, toNC, notes, reason, cell);
        }

        private async Task<(bool Ok, string Error)> IssueWithPurposeAsync(
            int khPlanDetailId, string fromGroupCode, string toGroupCode, decimal qtyIssued,
            int issuedByUserId, string purpose, string? fromNC, string? toNC, string? notes, string? reason,
            string? fromCell = null,   // [GN-CELL] ô bên giao chọn (null = gọi từ chỗ cũ → suy từ purpose)
            string? toCell = null)     // [GN-CELL2] ô bên giao chỉ định cho bên nhận (null = theo loại phiếu)
        {
            fromCell ??= HandoverCell.IssueCellOf(purpose, null);
            bool cellPurpose = purpose is HandoverPurpose.New or HandoverPurpose.NgReturn or HandoverPurpose.RepairSend or HandoverPurpose.RepairReturn;
            if (toCell != null && (!cellPurpose || !HandoverCell.IsReceiveCell(toCell)))
                return (false, "Ô nhận không hợp lệ.");   // [GN-CELL2]
            if (cellPurpose) toCell ??= HandoverCell.ReceiveCellOf(purpose);
            if (qtyIssued <= 0) return (false, "Số lượng giao phải lớn hơn 0.");
            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
            bool isOut = HandoverPurpose.IsOut(purpose);   // [GN-NG-OUT] Huỷ / Trả khách: không có nhóm nhận
            if (isOut) toGroupCode = HandoverPurpose.OutTarget(purpose)!;
            if (!validGroups.Contains(fromGroupCode) || (!isOut && !validGroups.Contains(toGroupCode)))
                return (false, "Mã nhóm không hợp lệ.");
            if (!HandoverPurpose.All.Contains(purpose)) return (false, "Loại phiếu không hợp lệ.");
            var routeErr = HandoverLedgerService.CheckRoute(fromGroupCode, toGroupCode, purpose);
            if (routeErr != null) return (false, routeErr);
            bool isNg = purpose is HandoverPurpose.NgReturn or HandoverPurpose.RepairSend || isOut;   // [GN-NG-OUT] huỷ / trả khách bắt buộc ghi lỗi
            if (isNg && string.IsNullOrWhiteSpace(reason))
                return (false, "Bắt buộc ghi lỗi cụ thể khi giao hàng lỗi.");
            // [GN-CELL] Ô giao phải khớp loại phiếu (chặn gọi sai từ code)
            if (fromCell != null && !HandoverCell.IssueCells.Contains(fromCell))
                return (false, "Phân loại hàng không hợp lệ."); // [GN-PLH] mục 36
            if (fromCell != null && HandoverCell.IssueCellOf(purpose, fromCell) != fromCell)
                return (false, $"Ô {HandoverCell.Label(fromCell)} không giao được theo kiểu {HandoverPurpose.Label(purpose)}.");
            if (purpose == HandoverPurpose.HelpSend && string.IsNullOrEmpty(fromNC))
                return (false, "Giao theo NC: bắt buộc chọn NC nguồn.");
            if (purpose == HandoverPurpose.HelpSend && string.IsNullOrWhiteSpace(notes))
                return (false, "Giao theo NC: bắt buộc ghi chú (N4).");

            using var trx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var lockErr = await LockPoAsync(khPlanDetailId);   // [GN-K8] khoá chung với trang WTS
            if (lockErr != null) return (false, lockErr);
            var ledger = new HandoverLedgerService(_db);
            var row = await ledger.GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return (false, "Không tìm thấy PO.");

            // K3: hàng làm hộ trả về Gia công phải chọn NC nhận khi PO có snapshot NC
            bool gcHasNc = row.G("GC").Steps.Any();
            if (toGroupCode == "GC" && gcHasNc && string.IsNullOrEmpty(toNC) && purpose == HandoverPurpose.HelpReturn)
                return (false, "Trả về Gia công: bắt buộc chọn NC nhận vào (K3).");

            // [GN-NC-PIN] Gia công trả hàng lỗi công đoạn trước: bắt buộc chọn NC phát hiện lỗi (chốt NC, không trôi theo dữ liệu sau)
            if (purpose == HandoverPurpose.NgReturn && fromGroupCode == "GC" && row.G("GC").Steps.Any(s => !s.IsExcluded))
            {
                if (string.IsNullOrEmpty(fromNC))
                    return (false, "Gia công trả hàng lỗi: bắt buộc chọn NC phát hiện lỗi.");
                if (!row.G("GC").Steps.Any(s => s.Code == fromNC && !s.IsExcluded))
                    return (false, $"NC {fromNC} không có trong snapshot của PO (hoặc là máy loại trừ).");
            }
            var (max, ex) = HandoverLedgerService.IssueCap(row, fromGroupCode, purpose, fromNC, fromCell);   // [GN-CELL] trần theo ô
            if (qtyIssued > max)
                return (false, $"{(fromCell != null ? "Ô " + HandoverCell.Label(fromCell) : HandoverPurpose.Label(purpose))}: tối đa {max:0.##} ({ex.Title}: {ex.Arithmetic}). Không thể giao {qtyIssued:0.##}.");

            _db.HandoverTransactions.Add(new HandoverTransaction
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
                Status         = isOut ? "COMPLETED" : "PENDING",   // [GN-NG-OUT] ra khỏi PO ngay, không chờ nhận
                IssuedBy       = issuedByUserId,
                IssuedAt       = DateTime.Now,
                Notes          = notes,
                CreatedAt      = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            // [GN-K8] Tính lại sổ trong cùng giao dịch — ô nào mới âm thì không lưu
            var k8 = await CheckNoNewNegativeAsync(khPlanDetailId, row);
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }   // không Commit → rollback
            await trx.CommitAsync();
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // [GN-K8] Khoá theo PO + kiểm "lưu xong không ô nào mới âm" — dùng chung cho mọi thao tác ghi giao nhận.
        //   Khoá "MES_PO_<id>" trùng tên với khoá trang WTS (HieuSuat_Index) → WTS và Giao nhận của cùng 1 PO
        //   không ghi chen nhau. LockOwner = Transaction: tự nhả khi Commit / Rollback.
        // ----------------------------------------------------------------
        private async Task<string?> LockPoAsync(int khPlanDetailId)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; " +
                    "IF @r < 0 THROW 50001, N'PO đang có người khác lưu', 1;",
                    "MES_PO_" + khPlanDetailId);
                return null;
            }
            catch (Exception ex) when (ex.Message.Contains("PO đang có người khác lưu"))
            {
                // Chỉ bắt đúng lỗi hết giờ chờ khoá (THROW 50001 ở trên); lỗi SQL khác để nổi lên như cũ
                return "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.";
            }
        }

        /// <summary>[GN-K8][K5] So sổ trước / sau (sau = đọc lại trong cùng giao dịch). Trả lời báo nếu có ô mới âm (chỉ Workflow ON — N7).</summary>
        private async Task<string?> CheckNoNewNegativeAsync(int khPlanDetailId, LedgerRow before)
        {
            if (!before.ApplyWorkflow) return null;
            var after = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
            if (after == null) return null;
            var bad = HandoverLedgerService.NewNegatives(before, after);
            return bad.Any()
                ? $"Không lưu: làm xong sẽ sai số ở {string.Join("; ", bad)}. Hàng này đã được dùng ở bước sau — phải huỷ / sửa từ cuối dòng ngược lên (K5, K8)."
                : null;
        }

        // ----------------------------------------------------------------

        // WRITE: Nhận hàng

        // ----------------------------------------------------------------

        public async Task<(bool Ok, string Error)> ReceiveAsync(

            long handoverTxId,

            decimal qtyOk,

            decimal qtyNg,

            string? ngReason,

            int receivedByUserId,

            string? notes = null,

            string? toCell = null,          // [GN-CELL2] ô bên nhận chọn (null = theo ô bên giao chỉ định)

            string? toCellReason = null)    // [GN-CELL2] bắt buộc khi đổi khác ô chỉ định

        {

            if (qtyOk < 0 || qtyNg < 0)

                return (false, "Số lượng không được âm.");

            if (qtyOk + qtyNg <= 0)

                return (false, "Phải nhập ít nhất 1 chiếc OK hoặc NG.");

            // [GN-RECV-NG2] Kiểm lý do NG dời xuống sau khi đọc phiếu: phiếu Hàng lỗi công đoạn trước lấy lỗi theo phiếu

            var txHead = await _db.HandoverTransactions.AsNoTracking()
                .Where(t => t.HandoverTxId == handoverTxId).Select(t => new { t.KhPlanDetailId, t.Purpose, t.IsReturnNg, t.NgReason, t.ToCell, t.SourceReceiveId }).FirstOrDefaultAsync(); // [GN-RECV-NG2] + loại phiếu, lỗi; [GN-CELL2] + ô chỉ định
            if (txHead == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
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
            if (isNgRet && string.IsNullOrWhiteSpace(ngReason))
                ngReason = !string.IsNullOrWhiteSpace(txHead.NgReason) ? txHead.NgReason
                         : cellChanged && !string.IsNullOrWhiteSpace(toCellReason) ? toCellReason!.Trim()   // [GN-CELL2] bên nhận đổi sang ô Hàng lỗi: lỗi = lý do đổi
                         : "Hàng lỗi trả về";
            if (qtyNg > 0 && string.IsNullOrWhiteSpace(ngReason))
                return (false, "Bắt buộc nhập lý do khi có hàng NG.");

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

            // [GN-RECV-NG2] (bỏ luật riêng phiếu tự lập "chặn NG" — nay mọi phiếu hàng lỗi nhập ở ô Hàng lỗi, xem đầu hàm)

            var rcv = new HandoverReceive   // [GN-RECV-NG] giữ biến để lấy ReceiveId cho phiếu trả tự lập

            {

                HandoverTxId = handoverTxId,

                QtyOk        = qtyOk,

                QtyNg        = qtyNg,

                NgReason     = qtyNg > 0 ? ngReason : null,

                ToCell       = desigCell != null ? effCell : null,                 // [GN-CELL2] ô thực nhận
                ToCellReason = cellChanged ? toCellReason!.Trim() : null,          // [GN-CELL2]

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

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        // ----------------------------------------------------------------

        // WRITE: Void RECEIVE

        // ----------------------------------------------------------------

        public async Task<(bool Ok, string Error)> VoidReceiveAsync(

            long receiveId,

            string voidReason,

            int voidedByUserId)

        {

            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)

                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");

            var head = await _db.HandoverReceives.AsNoTracking().Where(r => r.ReceiveId == receiveId)
                .Select(r => new { r.Transaction.KhPlanDetailId }).FirstOrDefaultAsync();
            if (head == null)
                return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");

            // [GN-K8][K5] Huỷ lần nhận mà bên nhận đã làm / đã giao tiếp số đó → chặn (huỷ từ cuối dòng ngược lên)
            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(head.KhPlanDetailId);

            var receive = await _db.HandoverReceives

                .Include(r => r.Transaction).ThenInclude(t => t.Receives)

                .FirstOrDefaultAsync(r => r.ReceiveId == receiveId && !r.IsVoided);

            if (receive == null)

                return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");

            // [GN-RECV-NG] Lần nhận có NG đã sinh phiếu trả tự lập: huỷ lần nhận thì huỷ luôn phiếu trả,
            // nhưng nếu bên giao đã nhận lại hàng lỗi thì phải huỷ lần nhận đó trước (K5: huỷ từ cuối dòng ngược lên).
            var autoBack = await _db.HandoverTransactions.Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.SourceReceiveId == receiveId && !t.IsVoided);
            if (autoBack != null)
            {
                if (autoBack.Receives.Any(r => !r.IsVoided))
                    return (false, $"{HandoverLedgerService.GroupLabel(autoBack.ToGroupCode)} đã nhận lại hàng lỗi ở phiếu trả #{autoBack.HandoverTxId}. Huỷ lần nhận đó trước.");
                autoBack.IsVoided   = true;
                autoBack.VoidReason = $"Huỷ theo lần nhận #{receiveId}: {voidReason.Trim()}";
                if (autoBack.VoidReason.Length > 200) autoBack.VoidReason = autoBack.VoidReason[..200];
                autoBack.VoidedBy   = voidedByUserId;
                autoBack.VoidedAt   = DateTime.Now;
            }

            receive.IsVoided   = true;

            receive.VoidReason = voidReason.Trim();

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
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before);   // [GN-K8][K5]
                if (k5 != null) { _db.ChangeTracker.Clear(); return (false, k5); }
            }
            await trx.CommitAsync();

            _db.ChangeTracker.Clear();

            return (true, string.Empty);

        }

        // ----------------------------------------------------------------

        // WRITE: Void ISSUE

        // ----------------------------------------------------------------

        public async Task<(bool Ok, string Error)> VoidIssueAsync(

            long handoverTxId,

            string voidReason,

            int voidedByUserId)

        {

            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)

                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");

            var head = await _db.HandoverTransactions.AsNoTracking().Where(t => t.HandoverTxId == handoverTxId)
                .Select(t => new { t.KhPlanDetailId }).FirstOrDefaultAsync();
            if (head == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");

            // [GN-K8][K5] Huỷ phiếu xong mà ô nào mới âm → chặn
            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(head.KhPlanDetailId);

            var tx = await _db.HandoverTransactions

                .Include(t => t.Receives)

                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);

            if (tx == null)

                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");

            if (tx.Receives.Any(r => !r.IsVoided))

                return (false, "Phiếu đã có bên nhận xác nhận. Hãy void từng lần nhận trước.");

            // [GN-RECV-NG] Phiếu trả tự lập không huỷ riêng (huỷ lần nhận gốc thì phiếu này huỷ theo)
            if (tx.SourceReceiveId != null)
                return (false, $"Phiếu #{tx.HandoverTxId} do phần mềm tự lập khi nhận hàng có NG. Muốn huỷ thì huỷ lần nhận gốc.");

            tx.IsVoided   = true;

            tx.VoidReason = voidReason.Trim();

            tx.VoidedBy   = voidedByUserId;

            tx.VoidedAt   = DateTime.Now;

            await _db.SaveChangesAsync();

            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before);   // [GN-K8][K5]
                if (k5 != null) { _db.ChangeTracker.Clear(); return (false, k5); }
            }
            await trx.CommitAsync();

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
                         && t.SourceReceiveId == null)   // [GN-RECV-NG] phiếu trả thành phẩm NG tự lập (Kho → Đóng gói) không phải phôi Kho giao

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

                .Select(r => new { r.QtyOk, r.Transaction.ToNC })

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

        // ----------------------------------------------------------------

        // HELPER: Tính SL còn lại tại 1 nhóm để validate khi giao

        // ----------------------------------------------------------------

        public async Task<decimal> GetRemainingAsync(int khPlanDetailId, string groupCode)

        {

            decimal baseQty;

            if (groupCode == "KHO")

            {

                baseQty = (decimal)(await _db.KhoVatLieus

                    .Where(k => k.PlanDetailId == (long)khPlanDetailId && k.IsActive)

                    .SumAsync(k => (int?)k.SoLuongThucNhan) ?? 0);

            }

            else if (groupCode == "GC")

            {

                // [WORKFLOW] Nếu ApplyWorkflow = true:

                //   ceiling = WtsDone của NC cuối cùng (main, không backup) trong snapshot

                //   NC cuối phải có WtsDone > 0 thì mới được giao

                // Nếu ApplyWorkflow = false: giữ nguyên = SL nhận từ KHO

                var applyWf = await _db.KhPlanDetails

                    .Where(d => d.KhPlanDetailId == khPlanDetailId)

                    .Select(d => d.KhPlan.ApplyWorkflow)

                    .FirstOrDefaultAsync();

                if (applyWf)

                {

                    // Tìm NC chính cuối cùng không loại trừ trong snapshot
                    // NC loại trừ = giao ngoài, không tính là NC cuối GC
                    var exclMachinesLast = await _db.KhPlanRouteSnapshotMachineExclude
                        .Where(s => s.KhPlanDetailId == khPlanDetailId)
                        .Select(s => s.SoMay).ToListAsync();
                    var exclSetLast = exclMachinesLast
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Select(m => m!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Load tất cả NC chính rồi filter IsExcluded trong memory
                    var allMainNCs = await _db.KhPlanRouteSnapshotMachining
                        .Where(s => s.KhPlanDetailId == khPlanDetailId && !s.IsBackup)
                        .OrderByDescending(s => s.StepOrder)
                        .Select(s => new { s.NC, s.MachineRegistered })
                        .ToListAsync();
                    var lastMainNC = allMainNCs
                        .Where(s => s.MachineRegistered == null
                                 || !exclSetLast.Contains(s.MachineRegistered.Trim()))
                        .Select(s => s.NC)
                        .FirstOrDefault();

                    if (lastMainNC == null)

                    {

                        // Không có snapshot → fallback về SL nhận từ KHO

                        baseQty = await _db.HandoverReceives

                            .Where(r => !r.IsVoided

                                     && !r.Transaction.IsVoided

                                     && r.Transaction.KhPlanDetailId == khPlanDetailId

                                     && r.Transaction.ToGroupCode == "GC")

                            .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

                    }

                    else

                    {

                        // WtsDone NC cuối = tổng WTS log của NC cuối + các backup của nó

                        var processGroups = new[] { "GC" };

                        var wtsLogs = await _db.WtsProductionLogs

                            .Where(w => w.KhPlanDetailId == khPlanDetailId

                                     && processGroups.Contains(w.ProcessGroup)

                                     && !w.IsVoided)

                            .Select(w => new { w.NC, w.WtsCode, w.QtyDone })

                            .ToListAsync();

                        // Tính WtsDone NC cuối (main + backup của nó)

                        var backupNCs = await _db.KhPlanRouteSnapshotMachining

                            .Where(s => s.KhPlanDetailId == khPlanDetailId

                                     && s.IsBackup && s.ParentNC == lastMainNC)

                            .Select(s => s.NC)

                            .ToListAsync();

                        decimal lastDone = wtsLogs

                            .Where(w => (w.NC ?? w.WtsCode ?? "") == lastMainNC

                                     || backupNCs.Contains(w.NC ?? w.WtsCode ?? ""))

                            .Sum(w => w.QtyDone);

                        baseQty = lastDone; // 0 nếu NC cuối chưa làm → không cho giao

                    }

                }

                else

                {

                    // Workflow OFF: giữ logic cũ = SL nhận từ KHO

                    baseQty = await _db.HandoverReceives

                        .Where(r => !r.IsVoided

                                 && !r.Transaction.IsVoided

                                 && r.Transaction.KhPlanDetailId == khPlanDetailId

                                 && r.Transaction.ToGroupCode == "GC")

                        .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

                }

            }

            else

            {

                baseQty = await _db.HandoverReceives

                    .Where(r => !r.IsVoided

                             && !r.Transaction.IsVoided

                             && r.Transaction.KhPlanDetailId == khPlanDetailId

                             && r.Transaction.ToGroupCode == groupCode)

                    .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

            }

            var confirmedOut = await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && r.Transaction.KhPlanDetailId == khPlanDetailId

                         && r.Transaction.FromGroupCode == groupCode)

                .SumAsync(r => (decimal?)(r.QtyOk + r.QtyNg)) ?? 0m;

            var pendingTxs = await _db.HandoverTransactions

                .Where(t => !t.IsVoided

                         && t.KhPlanDetailId == khPlanDetailId

                         && t.FromGroupCode == groupCode

                         && t.Status != "COMPLETED")

                .Include(t => t.Receives)

                .ToListAsync();

            var pendingOut = pendingTxs.Sum(t =>

            {

                var received = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);

                return Math.Max(0, t.QtyIssued - received);

            });

            return Math.Max(0, baseQty - confirmedOut - pendingOut);

        }

        // ----------------------------------------------------------------

        // HELPER: Tính SL đang giữ thực tế để validate giao NG ngược (v1.0)

        // Khác GetRemainingAsync: luôn dùng ReceivedOk làm base (bỏ qua WtsDone gate)

        // Logic: SL đã nhận vào nhóm - người đã xác nhận ra - đang pending ra

        // ----------------------------------------------------------------

        public async Task<decimal> GetRemainingForNgReturnAsync(int khPlanDetailId, string groupCode)

        {

            // BaseQty = SL đã nhận OK vào nhóm này (từ nhóm trước giao tới)

            decimal baseQty;

            if (groupCode == "KHO")

            {

                // KHO: lấy từ KhoVatLieu (nhận từ KH)

                baseQty = (decimal)(await _db.KhoVatLieus

                    .Where(k => k.PlanDetailId == (long)khPlanDetailId && k.IsActive)

                    .SumAsync(k => (int?)k.SoLuongThucNhan) ?? 0);

            }

            else

            {

                // Các nhóm khác: tổng QtyOk từ Handover nhận vào nhóm này

                baseQty = await _db.HandoverReceives

                    .Where(r => !r.IsVoided

                             && !r.Transaction.IsVoided

                             && r.Transaction.KhPlanDetailId == khPlanDetailId

                             && r.Transaction.ToGroupCode == groupCode)

                    .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;

            }

            // Trừ SL đã giao ra khỏi nhóm này (cả thuận lẫn ngược, cả OK lẫn NG)

            var confirmedOut = await _db.HandoverReceives

                .Where(r => !r.IsVoided

                         && !r.Transaction.IsVoided

                         && r.Transaction.KhPlanDetailId == khPlanDetailId

                         && r.Transaction.FromGroupCode == groupCode)

                .SumAsync(r => (decimal?)(r.QtyOk + r.QtyNg)) ?? 0m;

            // Trừ SL đang pending giao ra (chưa được xác nhận)

            var pendingTxs = await _db.HandoverTransactions

                .Where(t => !t.IsVoided

                         && t.KhPlanDetailId == khPlanDetailId

                         && t.FromGroupCode == groupCode

                         && t.Status != "COMPLETED")

                .Include(t => t.Receives)

                .ToListAsync();

            var pendingOut = pendingTxs.Sum(t =>

            {

                var received = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);

                return Math.Max(0, t.QtyIssued - received);

            });

            return Math.Max(0, baseQty - confirmedOut - pendingOut);

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

