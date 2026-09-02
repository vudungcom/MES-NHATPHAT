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

        /// <summary>SL công nhân đã làm xong (WtsProductionLogs.QtyDone)</summary>
        public decimal WtsDone { get; set; }

        /// <summary>SL đã giao đi từ nhóm này sang nhóm tiếp</summary>
        public decimal IssuedOut { get; set; }

        /// <summary>
        /// Còn tồn tại nhóm = ReceivedOk - NgReturned - IssuedOut
        /// Đây là SL đã nhận nhưng chưa xử lý / chưa giao đi
        /// </summary>
        public decimal Remaining => Math.Max(0, ReceivedOk - NgReturned - IssuedOut);
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
    }

    public class HandoverLogItem
    {
        public long    HandoverTxId  { get; set; }
        public string  FromGroupCode { get; set; } = string.Empty;
        public string  ToGroupCode   { get; set; } = string.Empty;
        public decimal QtyIssued     { get; set; }
        public string? FromNC        { get; set; }  // NC nguồn nếu là giao inter-group
        public string  Status        { get; set; } = string.Empty;
        public DateTime IssuedAt     { get; set; }
        public string  IssuedByName  { get; set; } = string.Empty;
        public string? Notes         { get; set; }
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
    internal record WtsDoneItem(int KhPlanDetailId, string ProcessGroup, string? NC, int StepOrder, decimal QtyDone);

    /// <summary>NC option cho modal Giao — chọn NC nguồn khi giao inter-group</summary>
    public class NcOption
    {
        public string   NC          { get; set; } = string.Empty;
        public int      StepOrder   { get; set; }
        public decimal  QtyDone     { get; set; }  // SL công nhân đã làm xong NC này
        public string   Label       => $"NC{StepOrder} ({NC}) — đã làm: {QtyDone:0.##}";
    }

    // ── Service ───────────────────────────────────────────────────────────

    public class HandoverService
    {
        private readonly AppDbContext _db;

        // ProcessGroup values trong WtsProductionLogs theo nhóm Handover
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

            // Batch load HandoverTransactions
            var txList = await _db.HandoverTransactions
                .Where(t => detailIds.Contains(t.KhPlanDetailId) && !t.IsVoided)
                .Include(t => t.IssuedByUser)
                .ToListAsync();

            var txIds = txList.Select(t => t.HandoverTxId).ToList();

            // Batch load HandoverReceives
            var receiveList = txIds.Any()
                ? await _db.HandoverReceives
                    .Where(r => txIds.Contains(r.HandoverTxId) && !r.IsVoided)
                    .ToListAsync()
                : new List<HandoverReceive>();

            var receivesByTx = receiveList
                .GroupBy(r => r.HandoverTxId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Batch load KhoVatLieu (Kho OK/NG)
            var detailIdsLong = detailIds.Select(id => (long?)id).ToList();
            var khoList = await _db.KhoVatLieus
                .Where(k => k.PlanDetailId != null && detailIdsLong.Contains(k.PlanDetailId))
                .ToListAsync();
            var khoByDetail = khoList
                .Where(k => k.PlanDetailId.HasValue)
                .GroupBy(k => (int)k.PlanDetailId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Batch load WtsProductionLogs — QtyDone theo nhóm
            // Load WTS logs kèm NC để biết đây là NC mấy
            var wtsRaw = await _db.WtsProductionLogs
                .Where(w => detailIds.Contains(w.KhPlanDetailId) && !w.IsVoided)
                .Select(w => new { w.KhPlanDetailId, w.ProcessGroup, w.NC, w.QtyDone })
                .ToListAsync();

            // Load snapshot GC để biết StepOrder của từng NC (chỉ cần GC vì nhóm khác ko có NC concept)
            var gcSnapshots = await _db.KhPlanRouteSnapshotMachining
                .Where(s => detailIds.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.NC, s.StepOrder })
                .ToListAsync();

            var ncStepOrder = gcSnapshots
                .GroupBy(s => new { s.KhPlanDetailId, s.NC })
                .ToDictionary(g => (g.Key.KhPlanDetailId, g.Key.NC ?? ""), g => g.Max(x => x.StepOrder));

            var wtsLogs = wtsRaw.Select(w => new WtsDoneItem(
                w.KhPlanDetailId, w.ProcessGroup, w.NC,
                // Lấy StepOrder từ snapshot, nếu không có thì 0
                ncStepOrder.TryGetValue((w.KhPlanDetailId, w.NC ?? ""), out var so) ? so : 0,
                w.QtyDone
            )).ToList();

            // Group WTS theo detailId + processGroup
            // var wtsByDetail typed as Dictionary<int, List<WtsDoneItem>>
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

                // Kho: SL thực nhận từ KhoVatLieu
                var khoOk = khoByDetail.TryGetValue(d.KhPlanDetailId, out var khoItems)
                    ? (decimal)khoItems.Where(k => k.IsActive).Sum(k => k.SoLuongThucNhan ?? 0)
                    : 0m;
                // Kho NG = NG bên nhận (GC) báo lại cho tx từ KHO
                var khoNg = CalcNgReturned("KHO", detailTxs, receivesByTx);

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

        /// <summary>
        /// Tính số liệu cho 1 nhóm sản xuất
        /// groupCode: nhóm đang tính (GC/HTSP/KCS/PKG)
        /// fromGroup: nhóm giao hàng sang groupCode (KHO/GC/HTSP/KCS)
        /// wtsProcessGroups: các ProcessGroup trong WtsProductionLogs thuộc nhóm này
        /// </summary>
        private static HandoverGroupQty CalcGroupQty(
            string groupCode,
            string _fromGroup,
            List<HandoverTransaction> txs,
            Dictionary<long, List<HandoverReceive>> receivesByTx,
            List<WtsDoneItem>? wts,
            string[] wtsProcessGroups)
        {
            var qty = new HandoverGroupQty();

            // Nhận vào nhóm này (OK từ bên nhận xác nhận)
            foreach (var tx in txs.Where(t => t.ToGroupCode == groupCode))
            {
                if (receivesByTx.TryGetValue(tx.HandoverTxId, out var rxs))
                {
                    qty.ReceivedOk += rxs.Sum(r => r.QtyOk);
                    var totalRx = rxs.Sum(r => r.QtyOk + r.QtyNg);
                    if (tx.Status != "COMPLETED")
                        qty.IssuedOut += 0; // pending chưa tính vào IssuedOut
                }
            }

            // NG bên nhận báo lại — hiện ở nhóm này (từ tx giao đi)
            qty.NgReturned = CalcNgReturned(groupCode, txs, receivesByTx);

            // IssuedOut = SL bên nhận đã xác nhận (QtyOk + QtyNg) từ tx của nhóm này
            // KHÔNG dùng QtyIssued vì phiếu PENDING chưa được nhận không nên trừ vào Remaining
            qty.IssuedOut = txs
                .Where(t => t.FromGroupCode == groupCode)
                .SelectMany(t => receivesByTx.TryGetValue(t.HandoverTxId, out var rxs) ? rxs : new List<HandoverReceive>())
                .Sum(r => r.QtyOk + r.QtyNg);

            // WTS Done:
            // - Nhóm GC: chỉ tính NC có StepOrder cao nhất (NC cuối cùng hoàn thành)
            //   vì NC1=5, NC2=100 → chỉ NC2 mới là "xong gia công"
            // - Nhóm khác (HTSP/KCS/PKG): Sum bình thường theo WtsCode
            if (wts != null)
            {
                var groupLogs = wts.Where(w => wtsProcessGroups.Contains(w.ProcessGroup)).ToList();

                if (groupCode == "GC" && groupLogs.Any())
                {
                    // Lấy NC có StepOrder cao nhất
                    var maxStep = groupLogs.Max(w => w.StepOrder);
                    qty.WtsDone = groupLogs
                        .Where(w => w.StepOrder == maxStep)
                        .Sum(w => w.QtyDone);
                }
                else
                {
                    qty.WtsDone = groupLogs.Sum(w => w.QtyDone);
                }
            }

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
                };
            }).ToList();
        }

        // ----------------------------------------------------------------
        // QUERY: SL đã nhận vào 1 nhóm cho 1 detail — dùng cho WTS gate
        // Công nhân chỉ được chọn PO khi nhóm mình đã nhận > 0
        // ----------------------------------------------------------------
        public async Task<decimal> GetReceivedOkAsync(int khPlanDetailId, string groupCode)
        {
            if (groupCode == "GC")
            {
                // GC nhận từ KHO qua HandoverReceives
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
        // QUERY: Batch check nhiều detail — dùng cho PriorityRows filter
        // Trả về Set<KhPlanDetailId> có SL nhận > 0 tại nhóm groupCode
        // ----------------------------------------------------------------
        public async Task<HashSet<int>> GetDetailIdsWithReceivedAsync(
            IEnumerable<int> detailIds,
            string groupCode)
        {
            var ids = detailIds.ToList();

            if (groupCode == "KHO")
            {
                // Kho nhận từ KH → KhoVatLieu
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
        // QUERY: Lấy danh sách NC + QtyDone theo nhóm — dùng cho modal Giao NC
        // ----------------------------------------------------------------
        public async Task<List<NcOption>> GetNcOptionsAsync(
            int khPlanDetailId,
            string fromGroupCode)
        {
            // Lấy snapshot GC (NC list)
            var snapshots = await _db.KhPlanRouteSnapshotMachining
                .Where(s => s.KhPlanDetailId == khPlanDetailId && !s.IsBackup)
                .OrderBy(s => s.StepOrder)
                .Select(s => new { s.NC, s.StepOrder })
                .ToListAsync();

            if (!snapshots.Any()) return new List<NcOption>();

            // Map ProcessGroup từ fromGroupCode
            var processGroups = fromGroupCode switch
            {
                "GC"   => GcGroups,
                "HTSP" => HtspGroups,
                "KCS"  => KcsGroups,
                "PKG"  => PkgGroups,
                _      => Array.Empty<string>()
            };

            // Lấy WTS logs của nhóm này
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
                .Where(o => o.QtyDone > 0)  // chỉ hiện NC đã có sản lượng
                .ToList();
        }

    // ----------------------------------------------------------------
        // WRITE: Giao hàng
        // ----------------------------------------------------------------
        public async Task<(bool Ok, string Error)> IssueAsync(
            int khPlanDetailId,
            string fromGroupCode,
            string toGroupCode,
            decimal qtyIssued,
            int issuedByUserId,
            string? fromNC  = null,
            string? notes   = null)
        {
            if (qtyIssued <= 0)
                return (false, "Số lượng giao phải lớn hơn 0.");
            if (fromGroupCode == toGroupCode)
                return (false, "Nhóm giao và nhóm nhận không được trùng nhau.");

            var validGroups = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
            if (!validGroups.Contains(fromGroupCode) || !validGroups.Contains(toGroupCode))
                return (false, "Mã nhóm không hợp lệ.");

            decimal maxAllowed;
            if (!string.IsNullOrEmpty(fromNC))
            {
                // Giao NC cụ thể — tối đa = QtyDone của NC đó chưa giao đi
                var ncDone = await _db.WtsProductionLogs
                    .Where(w => w.KhPlanDetailId == khPlanDetailId
                             && w.NC == fromNC
                             && !w.IsVoided)
                    .SumAsync(w => (decimal?)w.QtyDone) ?? 0m;

                // Trừ đi SL đã giao từ NC này trước đó
                var alreadyIssuedFromNC = await _db.HandoverTransactions
                    .Where(t => t.KhPlanDetailId == khPlanDetailId
                             && t.FromGroupCode == fromGroupCode
                             && t.FromNC == fromNC
                             && !t.IsVoided)
                    .SumAsync(t => (decimal?)t.QtyIssued) ?? 0m;

                maxAllowed = Math.Max(0, ncDone - alreadyIssuedFromNC);
                if (qtyIssued > maxAllowed)
                    return (false, $"NC {fromNC} đã làm {ncDone:0.##}, đã giao {alreadyIssuedFromNC:0.##}, còn có thể giao: {maxAllowed:0.##}.");
            }
            else
            {
                var remaining = await GetRemainingAsync(khPlanDetailId, fromGroupCode);
                if (qtyIssued > remaining)
                    return (false, $"Số lượng giao ({qtyIssued}) vượt quá còn lại tại {fromGroupCode} ({remaining:0.##}).");
            }

            _db.HandoverTransactions.Add(new HandoverTransaction
            {
                KhPlanDetailId = khPlanDetailId,
                FromGroupCode  = fromGroupCode,
                ToGroupCode    = toGroupCode,
                QtyIssued      = qtyIssued,
                FromNC         = fromNC,
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
        // QUERY: Batch summary SL đã giao đi từ KHO — dùng cho Kho/Index
        // ----------------------------------------------------------------
        public async Task<List<KhoIssueSummary>> GetKhoIssueSummaryAsync(
            IEnumerable<int> detailIds)
        {
            var ids = detailIds.ToList();

            // Load tất cả tx từ KHO kèm receives
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
                    // SL bên nhận đã xác nhận OK
                    var confirmedOk = g
                        .SelectMany(t => t.Receives.Where(r => !r.IsVoided))
                        .Sum(r => r.QtyOk);

                    // SL đang PENDING/PARTIAL chưa được xác nhận
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
        // Dùng để hiển thị badge đỏ trên nút Nhận
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
        // HELPER: Tính SL còn lại tại 1 nhóm để validate khi giao
        // ----------------------------------------------------------------
        public async Task<decimal> GetRemainingAsync(int khPlanDetailId, string groupCode)
        {
            decimal receivedOk;

            if (groupCode == "KHO")
            {
                receivedOk = (decimal)(await _db.KhoVatLieus
                    .Where(k => k.PlanDetailId == (long)khPlanDetailId && k.IsActive)
                    .SumAsync(k => (int?)k.SoLuongThucNhan) ?? 0);
            }
            else
            {
                receivedOk = await _db.HandoverReceives
                    .Where(r => !r.IsVoided
                             && !r.Transaction.IsVoided
                             && r.Transaction.KhPlanDetailId == khPlanDetailId
                             && r.Transaction.ToGroupCode == groupCode)
                    .SumAsync(r => (decimal?)r.QtyOk) ?? 0m;
            }

            // NG bên nhận trả về → trừ khỏi remaining
            var ngReturned = await _db.HandoverReceives
                .Where(r => !r.IsVoided
                         && !r.Transaction.IsVoided
                         && r.Transaction.KhPlanDetailId == khPlanDetailId
                         && r.Transaction.FromGroupCode == groupCode)
                .SumAsync(r => (decimal?)r.QtyNg) ?? 0m;

            // issuedOut chỉ tính SL đã được bên nhận xác nhận (QtyOk + QtyNg trong Receives)
            // Phiếu PENDING chưa được nhận → không trừ vào remaining
            // (tránh trừ nhầm khi phiếu giao chưa được xác nhận)
            var issuedOut = await _db.HandoverReceives
                .Where(r => !r.IsVoided
                         && !r.Transaction.IsVoided
                         && r.Transaction.KhPlanDetailId == khPlanDetailId
                         && r.Transaction.FromGroupCode == groupCode)
                .SumAsync(r => (decimal?)(r.QtyOk + r.QtyNg)) ?? 0m;

            return Math.Max(0, receivedOk - ngReturned - issuedOut);
        }
    }

    // ── DTO cho Kho/Index ──────────────────────────────────────────────
    public class KhoIssueSummary
    {
        public int       KhPlanDetailId { get; set; }
        /// <summary>SL bên nhận đã xác nhận OK thực sự</summary>
        public decimal   TotalConfirmed { get; set; }
        /// <summary>SL đang PENDING/PARTIAL chưa được nhận xác nhận</summary>
        public decimal   TotalPending   { get; set; }
        public DateTime? FirstIssuedAt  { get; set; }
    }
}
