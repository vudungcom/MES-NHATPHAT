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
        public string   Label       => $"NC{StepOrder} ({NC}) — đã làm: {QtyDone:0.##}";
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
                    KhPlanDetailId = d.KhPlanDetailId,
                    PlanNo         = d.KhPlan?.PlanNo ?? "",
                    PurchaseOrder  = d.PurchaseOrder,
                    PartNo         = d.PartNo,
                    CustomerName   = d.KhPlan?.Customer?.CustomerName ?? "",
                    PlanQty        = d.Quantity,
                    KhoOk          = khoOk,
                    KhoNg          = khoNg,
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

            // IssuedOut = SL bên nhận đã confirmed (QtyOk + QtyNg)
            //           + SL đang PENDING/PARTIAL chưa được nhận (tránh double-giao và phản ánh đúng tồn kho)
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
                Receives      = t.Receives.OrderBy(r => r.ReceivedAt).Select(r => new HandoverReceiveItem
                {
                    ReceiveId      = r.ReceiveId,
                    QtyOk          = r.QtyOk,
                    QtyNg          = r.QtyNg,
                    NgReason       = r.NgReason,
                    ReceivedAt     = r.ReceivedAt,
                    ReceivedByName = r.ReceivedByUser?.FullName ?? r.ReceivedByUser?.Username ?? "",
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
        public async Task<List<NcOption>> GetNcOptionsAsync(
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
            string? fromNC   = null,
            string? notes    = null,
            string? toNC     = null,
            decimal ngQty    = 0,
            string? ngReason = null)
        {
            if (qtyIssued <= 0)
                return (false, "Số lượng giao phải lớn hơn 0.");
            if (fromGroupCode == toGroupCode)
                return (false, "Nhóm giao và nhóm nhận không được trùng nhau.");

            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
            if (!validGroups.Contains(fromGroupCode) || !validGroups.Contains(toGroupCode))
                return (false, "Mã nhóm không hợp lệ.");

            // v0.9: validate NgQty
            if (ngQty < 0)
                return (false, "Số lượng NG không được âm.");
            if (ngQty > qtyIssued)
                return (false, $"Số lượng NG ({ngQty}) không thể lớn hơn số lượng giao ({qtyIssued}).");
            if (ngQty > 0 && string.IsNullOrWhiteSpace(ngReason))
                return (false, "Bắt buộc nhập lý do khi có hàng NG.");

            if (!string.IsNullOrEmpty(fromNC))
            {
                var flow   = await GetNcFlowAsync(khPlanDetailId, fromGroupCode);
                var ncItem = flow.FirstOrDefault(f => !f.IsBackup && f.NC == fromNC);
                if (ncItem == null)
                    return (false, $"NC {fromNC} không tìm thấy trong snapshot nhóm {fromGroupCode}.");

                if (qtyIssued > ncItem.WtsDone && ncItem.WtsDone > 0)
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
                    return (false, $"Số lượng giao ({qtyIssued}) vượt quá còn lại tại {fromGroupCode} ({remaining:0.##}).");
            }

            var effectiveToNc = toGroupCode == "KHO" ? null : toNC;

            _db.HandoverTransactions.Add(new HandoverTransaction
            {
                KhPlanDetailId = khPlanDetailId,
                FromGroupCode  = fromGroupCode,
                ToGroupCode    = toGroupCode,
                QtyIssued      = qtyIssued,
                FromNC         = fromNC,
                ToNC           = effectiveToNc,
                NgQty          = ngQty,                                    // v0.9
                NgReason       = ngQty > 0 ? ngReason?.Trim() : null,     // v0.9
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
        // WRITE: Nhận hàng
        // ----------------------------------------------------------------
        public async Task<(bool Ok, string Error)> ReceiveAsync(
            long handoverTxId,
            decimal qtyOk,
            decimal qtyNg,
            string? ngReason,
            int receivedByUserId,
            string? notes = null)
        {
            if (qtyOk < 0 || qtyNg < 0)
                return (false, "Số lượng không được âm.");
            if (qtyOk + qtyNg <= 0)
                return (false, "Phải nhập ít nhất 1 chiếc OK hoặc NG.");
            if (qtyNg > 0 && string.IsNullOrWhiteSpace(ngReason))
                return (false, "Bắt buộc nhập lý do khi có hàng NG.");

            var tx = await _db.HandoverTransactions
                .Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);

            if (tx == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            if (tx.Status == "COMPLETED")
                return (false, "Phiếu giao đã nhận đủ.");

            var alreadyReceived = tx.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);
            var remaining       = tx.QtyIssued - alreadyReceived;

            if (qtyOk + qtyNg > remaining)
                return (false, $"Tổng nhận ({qtyOk + qtyNg}) vượt số còn lại của phiếu ({remaining:0.##}).");

            _db.HandoverReceives.Add(new HandoverReceive
            {
                HandoverTxId = handoverTxId,
                QtyOk        = qtyOk,
                QtyNg        = qtyNg,
                NgReason     = qtyNg > 0 ? ngReason : null,
                ReceivedBy   = receivedByUserId,
                ReceivedAt   = DateTime.Now,
                Notes        = notes,
                CreatedAt    = DateTime.Now,
            });

            var newTotal = alreadyReceived + qtyOk + qtyNg;
            tx.Status = newTotal >= tx.QtyIssued ? "COMPLETED" : "PARTIAL";

            await _db.SaveChangesAsync();
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

            var receive = await _db.HandoverReceives
                .Include(r => r.Transaction).ThenInclude(t => t.Receives)
                .FirstOrDefaultAsync(r => r.ReceiveId == receiveId && !r.IsVoided);

            if (receive == null)
                return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");

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

            var tx = await _db.HandoverTransactions
                .Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);

            if (tx == null)
                return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            if (tx.Receives.Any(r => !r.IsVoided))
                return (false, "Phiếu đã có bên nhận xác nhận. Hãy void từng lần nhận trước.");

            tx.IsVoided   = true;
            tx.VoidReason = voidReason.Trim();
            tx.VoidedBy   = voidedByUserId;
            tx.VoidedAt   = DateTime.Now;

            await _db.SaveChangesAsync();
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
                         && !t.IsVoided)
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
                // GC: ceiling = SL đã nhận OK từ KHO vào GC
                // Không dùng WtsDone NC cuối vì hàng có thể chưa sản xuất
                // (VD: trả NG ngược về KHO trước khi gia công)
                baseQty = await _db.HandoverReceives
                    .Where(r => !r.IsVoided
                             && !r.Transaction.IsVoided
                             && r.Transaction.KhPlanDetailId == khPlanDetailId
                             && r.Transaction.ToGroupCode == "GC")
                    .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;
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
