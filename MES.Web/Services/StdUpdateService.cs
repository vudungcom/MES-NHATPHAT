using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// [STD-UPDATE] Nút "Update TG chuẩn" ở Part Master.
///
/// Khi chạy cho 1 Part + 1 mốc thời gian (EffectiveFrom):
///   1. Snapshot của MỌI phiếu KH lấy từ Part này: chép số + trạng thái xác nhận HIỆN TẠI của Part Master
///      vào đúng dòng snapshot (nối qua SourceStepId / SourceTimingId). Chỉ đổi số + xác nhận,
///      KHÔNG thêm / xóa dòng — khác biệt cấu trúc chỉ cảnh báo "cần Làm mới quy trình".
///   2. Dòng WTS (chưa void) có CreatedAt >= mốc: ghi đè số chuẩn đóng băng (WtsProductionLogStds)
///      theo đúng dòng snapshot nó đã dùng. Dòng WTS trước mốc giữ nguyên.
///   3. Lịch sử: StdUpdateBatches (1 dòng / lần chạy) + StdUpdateBatchDetails (từng ô thay đổi).
///
/// Ghi chú: bảng snapshot vốn "không update" — đây là ngoại lệ có chủ đích, có quyền riêng
/// (SystemPermissions.PartStdUpdate) + PIN + lịch sử đầy đủ.
/// WtsProductionLogs KHÔNG bị sửa (vẫn append-only) — chỉ bảng WtsProductionLogStds được ghi đè.
/// </summary>
public class StdUpdateService
{
    private readonly AppDbContext _db;
    private readonly KhPlanService _kh;

    public StdUpdateService(AppDbContext db, KhPlanService kh)
    {
        _db = db;
        _kh = kh;
    }

    // ════════════════════════════════════════════════════════════════════
    // DTOs
    // ════════════════════════════════════════════════════════════════════

    public class CellChange
    {
        public string Target { get; set; } = "";          // StdTrack.Target.*
        public int KhPlanDetailId { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? SnapshotTable { get; set; }
        public long? SnapshotRowId { get; set; }
        public long? WtsLogId { get; set; }
        public DateTime? WtsCreatedAt { get; set; }
        public string? NC { get; set; }
        public string? MachineOrCode { get; set; }
        public string FieldName { get; set; } = "";
        public decimal? OldValue { get; set; }
        public decimal? NewValue { get; set; }
        public bool? OldIsConfirmed { get; set; }
        public bool NewIsConfirmed { get; set; }
        public string? OldConfirmedByName { get; set; }
        public int? NewConfirmedBy { get; set; }
        public string? NewConfirmedByName { get; set; }
        public DateTime? NewConfirmedAt { get; set; }
        public bool ValueChanged => OldValue != NewValue;
    }

    public class PlanSummary
    {
        public int KhPlanDetailId { get; set; }
        public string PurchaseOrder { get; set; } = "";
        public int SnapshotCellCount { get; set; }
        public int SnapshotCellChanged { get; set; }
        public int WtsLogCount { get; set; }
        public int WtsCellChanged { get; set; }
        public List<string> Warnings { get; set; } = new();
        /// <summary>[STD-UPDATE-V2] Phiếu đã có WTS (chưa void) → "đang dở dang": WTS lưu trước mốc giữ nguyên,
        /// WTS lưu từ mốc + phiếu KH cập nhật theo Part Master — phải tích chọn mới Update.
        /// Chưa có WTS → "chưa làm gì": tự đồng bộ toàn bộ số + xác nhận theo Part Master.</summary>
        public bool HasWts { get; set; }
        /// <summary>Tổng số dòng WTS (chưa void) của phiếu, mọi thời điểm</summary>
        public int WtsTotal { get; set; }
    }

    public class Preview
    {
        public int PartId { get; set; }
        public string PartNo { get; set; } = "";
        public DateTime EffectiveFrom { get; set; }
        public List<PlanSummary> Plans { get; set; } = new();
        public List<CellChange> Changes { get; set; } = new();
        public int SnapshotCellCount => Plans.Sum(p => p.SnapshotCellCount);
        public int SnapshotCellChanged => Changes.Count(c => c.Target == StdTrack.Target.Snapshot);
        public int WtsLogCount => Plans.Sum(p => p.WtsLogCount);
        public int WtsCellChanged => Changes.Count(c => c.Target == StdTrack.Target.WtsLog);
        public int WarningCount => Plans.Sum(p => p.Warnings.Count);
        public bool HasChanges => Changes.Count > 0;
    }

    public class RunResult
    {
        public long BatchId { get; set; }
        public Preview Applied { get; set; } = new();
    }

    // Trạng thái 1 ô snapshot SAU khi update (dùng cho bước WTS)
    private sealed record CellState(decimal? Value, bool IsConfirmed, int? By, string? ByName, DateTime? At);

    // ════════════════════════════════════════════════════════════════════
    // Public API
    // ════════════════════════════════════════════════════════════════════

    /// <summary>[STD-5 2026-10-31] Dấu các ô SNAPSHOT sẽ đổi (PO được chọn): bảng · dòng · ô · số cũ → mới · xác nhận. Trang gửi dấu của bản
    /// Xem trước khi bấm Chạy; RunAsync tính lại dưới khoá rồi so — khác thì dừng.</summary>
    public static string SnapshotFingerprint(Preview pv, IReadOnlyCollection<int> selected)
    {
        var set = new HashSet<int>(selected ?? Array.Empty<int>());
        return string.Join(";", pv.Changes
            .Where(c => c.Target == StdTrack.Target.Snapshot && set.Contains(c.KhPlanDetailId))
            .Select(c => $"{c.KhPlanDetailId}|{c.SnapshotTable}|{c.SnapshotRowId}|{c.FieldName}|{c.OldValue:0.####}>{c.NewValue:0.####}|{c.OldIsConfirmed}>{c.NewIsConfirmed}")
            .OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Xem trước — KHÔNG ghi gì vào DB.</summary>
    public Task<Preview> PreviewAsync(int partId, DateTime effectiveFrom)
        => ComputeAsync(partId, effectiveFrom);

    /// <summary>
    /// Chạy thật — tính lại từ đầu trong transaction, ghi snapshot + WTS + lịch sử.
    /// [STD-UPDATE-V3] selectedPlans = các phiếu được tích chọn (đang dở dang + chưa có WTS).
    /// </summary>
    public async Task<RunResult> RunAsync(int partId, DateTime effectiveFrom, string reason, int userId,
        IReadOnlyCollection<int> selectedPlans,
        string? previewFingerprint = null)   // [STD-5] dấu của bản Xem trước (SnapshotFingerprint); null = không so (gọi cũ)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Bắt buộc nhập lý do Update TG chuẩn.");
        // [STD-UPD 2026-10-30] Máy chủ tự kiểm quyền ma trận (trước: chỉ trang ẩn nút, trang lại tin claim ADMIN)
        if (!await PermCheck.CanEditAsync(_db, userId, MES.Web.Constants.SystemPermissions.PartStdUpdate))
            throw new InvalidOperationException(PermCheck.NoEdit("Part Master: Update TG chuẩn"));

        var selected  = new HashSet<int>(selectedPlans ?? Array.Empty<int>());
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // [STD-UPD 2026-10-30] Khoá các PO được chọn (cùng khoá MES_PO_<id> với WTS / Giao nhận), theo id tăng dần, TRƯỚC khi tính:
            //   WTS lưu chen giữa lúc tính và lúc ghi sẽ phải chờ → không bị bỏ sót / ghi đè số chuẩn. Trước: không khoá.
            foreach (var pid in selected.OrderBy(x => x))
            {
                var lockErr = await PoGuard.LockAsync(_db, pid);
                if (lockErr != null) throw new InvalidOperationException(lockErr);
            }
            var pv  = await ComputeAsync(partId, effectiveFrom);

            // [STD-UPDATE-V3] Chỉ giữ thay đổi của các phiếu ĐƯỢC TÍCH CHỌN (cả đang dở dang lẫn chưa có WTS — mặc định chọn tất cả ở UI)
            pv.Changes = pv.Changes.Where(c => selected.Contains(c.KhPlanDetailId)).ToList();
            pv.Plans   = pv.Plans.Where(p => selected.Contains(p.KhPlanDetailId)).ToList();   // [STD-UPD] số phiếu / ô / WTS trong lời báo + lịch sử chỉ tính PO được chọn (RS-E13 · STD-4)
            // [STD-5 2026-10-31] Part Master / snapshot đổi giữa lúc Xem trước và lúc Chạy → không áp thay đổi chưa ai xem: so dấu các ô snapshot
            //   (số cũ → mới + xác nhận) của PO được chọn, tính lại dưới khoá PO, khác bản Xem trước thì dừng (rollback), bắt Xem trước lại.
            //   Dòng WTS mới lưu sau lúc Xem trước không tính (số chuẩn của chúng đi theo đúng các ô snapshot người dùng đã xem).
            if (previewFingerprint != null && SnapshotFingerprint(pv, selected) != previewFingerprint)
                throw new InvalidOperationException("Part Master / quy trình PO vừa thay đổi sau lúc bấm Xem trước — chưa Update gì. Bấm Xem trước lại, xem bảng mới rồi Chạy.");
            if (pv.Changes.Count == 0)
                throw new InvalidOperationException("Không có ô nào để Update (chưa chọn PO nào?).");
            var now = DateTime.Now;
            var runByName = await _db.Users.Where(u => u.UserId == userId).Select(u => u.FullName).FirstOrDefaultAsync();

            // ── 1. Batch ──
            var batch = new StdUpdateBatch
            {
                PartId              = pv.PartId,
                PartNo              = pv.PartNo,
                EffectiveFrom       = effectiveFrom,
                RunBy               = userId,
                RunByName           = runByName,
                RunAt               = now,
                Reason              = reason.Trim(),
                PlanCount           = pv.Plans.Count,
                SnapshotCellCount   = pv.SnapshotCellCount,
                SnapshotCellChanged = pv.SnapshotCellChanged,
                WtsLogCount         = pv.WtsLogCount,
                WtsCellChanged      = pv.WtsCellChanged,
            };
            _db.StdUpdateBatches.Add(batch);
            await _db.SaveChangesAsync();

            // ── 2. Snapshot: số + xác nhận ──
            var snapChanges = pv.Changes.Where(c => c.Target == StdTrack.Target.Snapshot).ToList();
            await ApplySnapshotValuesAsync(snapChanges);
            await ApplySnapshotConfirmsAsync(snapChanges, batch.BatchId, userId, now);

            // ── 3. WTS: số chuẩn đóng băng ──
            var wtsChanges = pv.Changes.Where(c => c.Target == StdTrack.Target.WtsLog).ToList();
            if (wtsChanges.Count > 0)
            {
                var logIds = wtsChanges.Select(c => c.WtsLogId!.Value).Distinct().ToList();
                var stds = await _db.WtsProductionLogStds.Where(x => logIds.Contains(x.WtsLogId)).ToListAsync();
                var byKey = stds.ToDictionary(x => (x.WtsLogId, x.FieldName));
                foreach (var c in wtsChanges)
                {
                    if (!byKey.TryGetValue((c.WtsLogId!.Value, c.FieldName), out var row)) continue;
                    row.StdValue        = c.NewValue;
                    row.IsConfirmed     = c.NewIsConfirmed;
                    row.ConfirmedBy     = c.NewIsConfirmed ? c.NewConfirmedBy     : null;
                    row.ConfirmedByName = c.NewIsConfirmed ? c.NewConfirmedByName : null;
                    row.ConfirmedAt     = c.NewIsConfirmed ? c.NewConfirmedAt     : null;
                    row.SyncSource      = StdTrack.Source.StdUpdate;
                    row.SyncBatchId     = batch.BatchId;
                    row.SyncedAt        = now;
                    row.SyncedBy        = userId;
                    row.Note            = null;
                }
            }

            // ── 4. Lịch sử chi tiết ──
            foreach (var c in pv.Changes)
            {
                _db.StdUpdateBatchDetails.Add(new StdUpdateBatchDetail
                {
                    BatchId            = batch.BatchId,
                    KhPlanDetailId     = c.KhPlanDetailId,
                    PurchaseOrder      = c.PurchaseOrder,
                    Target             = c.Target,
                    SnapshotTable      = c.SnapshotTable,
                    SnapshotRowId      = c.SnapshotRowId,
                    WtsLogId           = c.WtsLogId,
                    NC                 = Trunc(c.NC, 20),
                    MachineOrCode      = Trunc(c.MachineOrCode, 50),
                    FieldName          = c.FieldName,
                    OldValue           = c.OldValue,
                    NewValue           = c.NewValue,
                    OldIsConfirmed     = c.OldIsConfirmed,
                    NewIsConfirmed     = c.NewIsConfirmed,
                    OldConfirmedByName = c.OldConfirmedByName,
                    NewConfirmedByName = c.NewConfirmedByName,
                    NewConfirmedAt     = c.NewConfirmedAt,
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            _db.ChangeTracker.Clear();

            return new RunResult { BatchId = batch.BatchId, Applied = pv };
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Tính toán (không ghi)
    // ════════════════════════════════════════════════════════════════════

    private async Task<Preview> ComputeAsync(int partId, DateTime effectiveFrom)
    {
        var part = await _db.PartMasters.AsNoTracking().FirstOrDefaultAsync(p => p.PartId == partId)
                   ?? throw new InvalidOperationException("Không tìm thấy Part.");

        var pv = new Preview { PartId = partId, PartNo = part.PartNo, EffectiveFrom = effectiveFrom };

        // ── Part Master hiện tại ──
        var pmMach = await _db.PartMachiningSteps
            .Include(s => s.Timings.Where(t => t.IsActive))
            .Where(s => s.PartId == partId && s.IsActive)
            .AsNoTracking().ToListAsync();
        var pmMachById   = pmMach.ToDictionary(s => s.StepId);
        var pmTimingById = pmMach.SelectMany(s => s.Timings).ToDictionary(t => t.TimingId);

        var pmCde = new Dictionary<string, Dictionary<long, (string NC, int StepOrder, decimal? Std)>>
        {
            [StdTrack.Table.Taro]       = (await _db.PartTaroSteps.Where(s => s.PartId == partId && s.IsActive).AsNoTracking().ToListAsync())
                                            .ToDictionary(s => s.StepId, s => (s.NC, s.StepOrder, s.StandardTime)),
            [StdTrack.Table.Bavia]      = (await _db.PartBaviaSteps.Where(s => s.PartId == partId && s.IsActive).AsNoTracking().ToListAsync())
                                            .ToDictionary(s => s.StepId, s => (s.NC, s.StepOrder, s.StandardTime)),
            [StdTrack.Table.Washing]    = (await _db.PartWashingSteps.Where(s => s.PartId == partId && s.IsActive).AsNoTracking().ToListAsync())
                                            .ToDictionary(s => s.StepId, s => (s.NC, s.StepOrder, s.StandardTime)),
            [StdTrack.Table.Inspection] = (await _db.PartInspectionSteps.Where(s => s.PartId == partId && s.IsActive).AsNoTracking().ToListAsync())
                                            .ToDictionary(s => s.StepId, s => (s.NC, s.StepOrder, s.StandardTime)),
            [StdTrack.Table.Packaging]  = (await _db.PartPackagingSteps.Where(s => s.PartId == partId && s.IsActive).AsNoTracking().ToListAsync())
                                            .ToDictionary(s => s.StepId, s => (s.NC, s.StepOrder, s.StandardTime)),
        };

        var (pmConfGc, pmConfCde) = await _kh.LoadPmStdConfirmsAsync(partId);


        // ── Phiếu KH có snapshot lấy từ Part này ──
        var detailIds = (await _db.KhPlanRouteSnapshotMachining.Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Concat(await _db.KhPlanRouteSnapshotTaro      .Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Concat(await _db.KhPlanRouteSnapshotBavia     .Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Concat(await _db.KhPlanRouteSnapshotWashing   .Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Concat(await _db.KhPlanRouteSnapshotInspection.Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Concat(await _db.KhPlanRouteSnapshotPackaging .Where(s => s.SourcePartId == partId).Select(s => s.KhPlanDetailId).ToListAsync())
            .Distinct().ToList();
        if (detailIds.Count == 0) return pv;

        // [STD-UPDATE-V2] Số dòng WTS (chưa void) mỗi phiếu — phân loại "đang dở dang" / "chưa làm gì"
        var wtsTotalByDetail = await _db.WtsProductionLogs
            .Where(l => detailIds.Contains(l.KhPlanDetailId) && !l.IsVoided)
            .GroupBy(l => l.KhPlanDetailId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

        var poById = await _db.KhPlanDetails.Where(d => detailIds.Contains(d.KhPlanDetailId))
            .Select(d => new { d.KhPlanDetailId, d.PurchaseOrder }).AsNoTracking()
            .ToDictionaryAsync(d => d.KhPlanDetailId, d => d.PurchaseOrder);

        var snapMach = await _db.KhPlanRouteSnapshotMachining.Include(s => s.TimingRows)
            .Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();
        var snapTaro = await _db.KhPlanRouteSnapshotTaro      .Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();
        var snapBav  = await _db.KhPlanRouteSnapshotBavia     .Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();
        var snapWash = await _db.KhPlanRouteSnapshotWashing   .Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();
        var snapInsp = await _db.KhPlanRouteSnapshotInspection.Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();
        var snapPkg  = await _db.KhPlanRouteSnapshotPackaging .Where(s => detailIds.Contains(s.KhPlanDetailId)).AsNoTracking().ToListAsync();

        var oldConf = (await _db.KhPlanRouteSnapshotStdConfirms.Where(x => detailIds.Contains(x.KhPlanDetailId)).AsNoTracking().ToListAsync())
            .ToDictionary(x => (x.SnapshotTable, x.SnapshotRowId, x.FieldName));

        // Trạng thái MỚI của mọi ô snapshot (kể cả ô không đổi) — dùng cho bước WTS
        var newState = new Dictionary<(string Table, long RowId, string Field), CellState>();

        foreach (var detailId in detailIds.OrderBy(id => poById.GetValueOrDefault(id)))
        {
            var po = poById.GetValueOrDefault(detailId) ?? $"#{detailId}";
            var ps = new PlanSummary { KhPlanDetailId = detailId, PurchaseOrder = po };
            ps.WtsTotal = wtsTotalByDetail.GetValueOrDefault(detailId);
            ps.HasWts   = ps.WtsTotal > 0;
            pv.Plans.Add(ps);

            // ── B. Gia công ──
            var mRows = snapMach.Where(s => s.KhPlanDetailId == detailId).ToList();
            foreach (var s in mRows)
            {
                PartMachiningStep? pm = null;
                if (!s.SourceStepId.HasValue)
                    ps.Warnings.Add($"Gia công NC {s.NC}: dòng snapshot chưa nối được Part Master — bỏ qua");
                else if (!pmMachById.TryGetValue(s.SourceStepId.Value, out pm))
                    ps.Warnings.Add($"Gia công NC {s.NC}: đã xóa ở Part Master — cần Làm mới quy trình");

                foreach (var f in StdTrack.Field.Gc)
                {
                    var oldVal = GcVal(s, f);
                    ps.SnapshotCellCount++;
                    if (pm == null) { KeepState(newState, oldConf, StdTrack.Table.Machining, s.SnapshotId, f, oldVal); continue; }
                    pmConfGc.TryGetValue((pm.StepId, null, f), out var pc);
                    AddSnapCell(pv, ps, newState, oldConf, detailId, po, StdTrack.Table.Machining, s.SnapshotId,
                        s.NC, s.MachineRegistered, f, oldVal, GcVal(pm, f), pc);
                }

                foreach (var t in s.TimingRows)
                {
                    PartMachiningTiming? pt = null;
                    if (!t.SourceTimingId.HasValue)
                        ps.Warnings.Add($"Gia công NC {s.NC} máy {t.SoMay}: dòng snapshot chưa nối được Part Master — bỏ qua");
                    else if (!pmTimingById.TryGetValue(t.SourceTimingId.Value, out pt))
                        ps.Warnings.Add($"Gia công NC {s.NC} máy {t.SoMay}: đã xóa ở Part Master — cần Làm mới quy trình");

                    foreach (var f in StdTrack.Field.Gc)
                    {
                        var oldVal = GcVal(t, f);
                        ps.SnapshotCellCount++;
                        if (pt == null) { KeepState(newState, oldConf, StdTrack.Table.MachiningTiming, t.TimingSnapshotId, f, oldVal); continue; }
                        pmConfGc.TryGetValue((pt.StepId, pt.TimingId, f), out var pc);
                        AddSnapCell(pv, ps, newState, oldConf, detailId, po, StdTrack.Table.MachiningTiming, t.TimingSnapshotId,
                            s.NC, t.SoMay, f, oldVal, GcVal(pt, f), pc);
                    }
                }
            }

            // Cấu trúc: NC / máy đồng dạng mới ở Part Master mà snapshot chưa có
            if (mRows.Count > 0 || pmMach.Count > 0)
            {
                var snapStepIds   = mRows.Where(s => s.SourceStepId.HasValue).Select(s => s.SourceStepId!.Value).ToHashSet();
                var snapTimingIds = mRows.SelectMany(s => s.TimingRows).Where(t => t.SourceTimingId.HasValue).Select(t => t.SourceTimingId!.Value).ToHashSet();
                foreach (var pm in pmMach.Where(p => !snapStepIds.Contains(p.StepId)))
                    ps.Warnings.Add($"Gia công NC {pm.NC}: mới thêm ở Part Master — cần Làm mới quy trình");
                foreach (var pm in pmMach.Where(p => snapStepIds.Contains(p.StepId)))
                    foreach (var pt in pm.Timings.Where(t => !snapTimingIds.Contains(t.TimingId)))
                        ps.Warnings.Add($"Gia công NC {pm.NC} máy {pt.SoMay}: mới thêm ở Part Master — cần Làm mới quy trình");
            }

            // ── C/D/E ──
            CompareCde(pv, ps, newState, oldConf, pmCde, pmConfCde, detailId, po, StdTrack.Table.Taro, "Taro",
                snapTaro.Where(s => s.KhPlanDetailId == detailId).Select(s => (s.SnapshotId, s.SourceStepId, s.NC, (string?)(s.WtsTaskCode ?? s.NC), s.StandardTime)).ToList());
            CompareCde(pv, ps, newState, oldConf, pmCde, pmConfCde, detailId, po, StdTrack.Table.Bavia, "Bavia",
                snapBav.Where(s => s.KhPlanDetailId == detailId).Select(s => (s.SnapshotId, s.SourceStepId, s.NC, (string?)s.NC, s.StandardTime)).ToList());
            CompareCde(pv, ps, newState, oldConf, pmCde, pmConfCde, detailId, po, StdTrack.Table.Washing, "Rửa",
                snapWash.Where(s => s.KhPlanDetailId == detailId).Select(s => (s.SnapshotId, s.SourceStepId, s.NC, (string?)s.NC, s.StandardTime)).ToList());
            CompareCde(pv, ps, newState, oldConf, pmCde, pmConfCde, detailId, po, StdTrack.Table.Inspection, "KCS",
                snapInsp.Where(s => s.KhPlanDetailId == detailId).Select(s => (s.SnapshotId, s.SourceStepId, s.NC, (string?)s.NC, s.StandardTime)).ToList());
            CompareCde(pv, ps, newState, oldConf, pmCde, pmConfCde, detailId, po, StdTrack.Table.Packaging, "Đóng gói",
                snapPkg.Where(s => s.KhPlanDetailId == detailId).Select(s => (s.SnapshotId, s.SourceStepId, s.NC, (string?)s.NC, s.StandardTime)).ToList());
        }

        // ── WTS lưu từ mốc trở đi ──
        var logs = await _db.WtsProductionLogs
            .Where(l => detailIds.Contains(l.KhPlanDetailId) && !l.IsVoided && l.CreatedAt >= effectiveFrom)
            .Select(l => new { l.WtsLogId, l.KhPlanDetailId, l.NC, l.WtsCode, l.MachineUsed, l.CreatedAt })
            .AsNoTracking().ToListAsync();
        var logIds = logs.Select(l => l.WtsLogId).ToList();
        var stdRows = logIds.Count == 0
            ? new List<WtsProductionLogStd>()
            : await _db.WtsProductionLogStds.Where(x => logIds.Contains(x.WtsLogId)).AsNoTracking().ToListAsync();
        var stdByLog = stdRows.GroupBy(x => x.WtsLogId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var l in logs)
        {
            var ps = pv.Plans.First(p => p.KhPlanDetailId == l.KhPlanDetailId);
            ps.WtsLogCount++;
            if (!stdByLog.TryGetValue(l.WtsLogId, out var cells) || cells.Count == 0)
            {
                ps.Warnings.Add($"WTS #{l.WtsLogId} ({l.CreatedAt:dd/MM HH:mm}): chưa có số chuẩn — chạy SQL backfill rồi Update lại");
                continue;
            }
            foreach (var cell in cells)
            {
                if (string.IsNullOrEmpty(cell.SnapshotTable) || !cell.SnapshotRowId.HasValue)
                {
                    ps.Warnings.Add($"WTS #{l.WtsLogId} {cell.FieldName}: lúc lưu không tìm thấy dòng snapshot — bỏ qua");
                    continue;
                }
                if (!newState.TryGetValue((cell.SnapshotTable, cell.SnapshotRowId.Value, cell.FieldName), out var ns))
                {
                    ps.Warnings.Add($"WTS #{l.WtsLogId} {cell.FieldName}: dòng snapshot đã bị Làm mới (không còn) — bỏ qua");
                    continue;
                }
                // Chỉ đổi khi số khác hoặc trạng thái xác nhận khác (bỏ qua khác người / thời điểm)
                var same = cell.StdValue == ns.Value && cell.IsConfirmed == ns.IsConfirmed;
                if (same) continue;

                pv.Changes.Add(new CellChange
                {
                    Target             = StdTrack.Target.WtsLog,
                    KhPlanDetailId     = l.KhPlanDetailId,
                    PurchaseOrder      = ps.PurchaseOrder,
                    SnapshotTable      = cell.SnapshotTable,
                    SnapshotRowId      = cell.SnapshotRowId,
                    WtsLogId           = l.WtsLogId,
                    WtsCreatedAt       = l.CreatedAt,
                    NC                 = l.NC ?? l.WtsCode,
                    MachineOrCode      = l.MachineUsed ?? l.WtsCode,
                    FieldName          = cell.FieldName,
                    OldValue           = cell.StdValue,
                    NewValue           = ns.Value,
                    OldIsConfirmed     = cell.IsConfirmed,
                    NewIsConfirmed     = ns.IsConfirmed,
                    OldConfirmedByName = cell.ConfirmedByName,
                    NewConfirmedBy     = ns.By,
                    NewConfirmedByName = ns.ByName,
                    NewConfirmedAt     = ns.At,
                });
                ps.WtsCellChanged++;
            }
        }

        return pv;
    }

    // ── So sánh 1 bảng C/D/E của 1 phiếu ──
    private static void CompareCde(Preview pv, PlanSummary ps,
        Dictionary<(string, long, string), CellState> newState,
        Dictionary<(string, long, string), KhPlanRouteSnapshotStdConfirm> oldConf,
        Dictionary<string, Dictionary<long, (string NC, int StepOrder, decimal? Std)>> pmCde,
        Dictionary<(string Table, long StepId), KhPlanService.PmStdConfirm> pmConfCde,
        int detailId, string po, string table, string label,
        List<(long SnapshotId, long? SourceStepId, string NC, string? Code, decimal? Std)> rows)
    {
        var pmRows = pmCde[table];
        foreach (var r in rows)
        {
            ps.SnapshotCellCount++;
            if (!r.SourceStepId.HasValue)
            {
                ps.Warnings.Add($"{label} {r.Code}: dòng snapshot chưa nối được Part Master — bỏ qua");
                KeepState(newState, oldConf, table, r.SnapshotId, StdTrack.Field.Standard, r.Std);
                continue;
            }
            if (!pmRows.TryGetValue(r.SourceStepId.Value, out var pm))
            {
                ps.Warnings.Add($"{label} {r.Code}: đã xóa ở Part Master — cần Làm mới quy trình");
                KeepState(newState, oldConf, table, r.SnapshotId, StdTrack.Field.Standard, r.Std);
                continue;
            }
            pmConfCde.TryGetValue((table, r.SourceStepId.Value), out var pc);
            AddSnapCell(pv, ps, newState, oldConf, detailId, po, table, r.SnapshotId,
                r.NC, r.Code, StdTrack.Field.Standard, r.Std, pm.Std, pc);
        }

        var snapIds = rows.Where(r => r.SourceStepId.HasValue).Select(r => r.SourceStepId!.Value).ToHashSet();
        if (rows.Count > 0 || pmRows.Count > 0)
            foreach (var kv in pmRows.Where(kv => !snapIds.Contains(kv.Key)))
                ps.Warnings.Add($"{label} {kv.Value.NC} (thứ tự {kv.Value.StepOrder}): mới thêm ở Part Master — cần Làm mới quy trình");
    }

    // ── 1 ô snapshot: so sánh cũ/mới, ghi trạng thái mới, thêm thay đổi nếu khác ──
    private static void AddSnapCell(Preview pv, PlanSummary ps,
        Dictionary<(string, long, string), CellState> newState,
        Dictionary<(string, long, string), KhPlanRouteSnapshotStdConfirm> oldConf,
        int detailId, string po, string table, long rowId, string? nc, string? machineOrCode,
        string field, decimal? oldVal, decimal? newVal, KhPlanService.PmStdConfirm? pmConf)
    {
        oldConf.TryGetValue((table, rowId, field), out var oc);
        var newConfirmed = pmConf?.IsConfirmed == true;
        newState[(table, rowId, field)] = new CellState(newVal, newConfirmed,
            newConfirmed ? pmConf!.ConfirmedBy : null,
            newConfirmed ? pmConf!.ConfirmedByName : null,
            newConfirmed ? pmConf!.ConfirmedAt : null);

        var oldConfirmed = oc?.IsConfirmed == true;
        // Chỉ tính là ĐỔI khi số khác hoặc trạng thái xác nhận khác (✓ ↔ ○).
        // Khác người / thời điểm xác nhận (Part Master ghi lại bản ghi khi Lưu) → KHÔNG đổi, giữ người/thời điểm cũ.
        var same = oldVal == newVal && oldConfirmed == newConfirmed;
        if (same && oc != null)
        {
            // Bước WTS dùng đúng trạng thái snapshot đang giữ (không ghi đè người/thời điểm)
            newState[(table, rowId, field)] = new CellState(oldVal, oldConfirmed,
                oldConfirmed ? oc.ConfirmedBy : null, oldConfirmed ? oc.ConfirmedByName : null, oldConfirmed ? oc.ConfirmedAt : null);
            return;
        }
        // oc == null (chưa có dòng xác nhận) → vẫn ghi để tạo dòng

        pv.Changes.Add(new CellChange
        {
            Target             = StdTrack.Target.Snapshot,
            KhPlanDetailId     = detailId,
            PurchaseOrder      = po,
            SnapshotTable      = table,
            SnapshotRowId      = rowId,
            NC                 = nc,
            MachineOrCode      = machineOrCode,
            FieldName          = field,
            OldValue           = oldVal,
            NewValue           = newVal,
            OldIsConfirmed     = oc?.IsConfirmed,
            NewIsConfirmed     = newConfirmed,
            OldConfirmedByName = oc?.ConfirmedByName,
            NewConfirmedBy     = newConfirmed ? pmConf!.ConfirmedBy : null,
            NewConfirmedByName = newConfirmed ? pmConf!.ConfirmedByName : null,
            NewConfirmedAt     = newConfirmed ? pmConf!.ConfirmedAt : null,
        });
        ps.SnapshotCellChanged++;
    }

    // Ô không update được (không nối / đã xóa) → giữ nguyên trạng thái cũ cho bước WTS
    private static void KeepState(Dictionary<(string, long, string), CellState> newState,
        Dictionary<(string, long, string), KhPlanRouteSnapshotStdConfirm> oldConf,
        string table, long rowId, string field, decimal? oldVal)
    {
        oldConf.TryGetValue((table, rowId, field), out var oc);
        var ok = oc?.IsConfirmed == true;
        newState[(table, rowId, field)] = new CellState(oldVal, ok,
            ok ? oc!.ConfirmedBy : null, ok ? oc!.ConfirmedByName : null, ok ? oc!.ConfirmedAt : null);
    }

    // ════════════════════════════════════════════════════════════════════
    // Ghi snapshot
    // ════════════════════════════════════════════════════════════════════

    private async Task ApplySnapshotValuesAsync(List<CellChange> changes)
    {
        var valueChanges = changes.Where(c => c.ValueChanged).ToList();
        if (valueChanges.Count == 0) return;

        List<long> Ids(string table) => valueChanges.Where(c => c.SnapshotTable == table)
            .Select(c => c.SnapshotRowId!.Value).Distinct().ToList();

        var idM = Ids(StdTrack.Table.Machining);
        if (idM.Count > 0)
        {
            var rows = await _db.KhPlanRouteSnapshotMachining.Where(s => idM.Contains(s.SnapshotId)).ToDictionaryAsync(s => s.SnapshotId);
            foreach (var c in valueChanges.Where(c => c.SnapshotTable == StdTrack.Table.Machining))
                SetGc(rows[c.SnapshotRowId!.Value], c.FieldName, c.NewValue);
        }
        var idT = Ids(StdTrack.Table.MachiningTiming);
        if (idT.Count > 0)
        {
            var rows = await _db.KhPlanRouteSnapshotMachiningTimings.Where(s => idT.Contains(s.TimingSnapshotId)).ToDictionaryAsync(s => s.TimingSnapshotId);
            foreach (var c in valueChanges.Where(c => c.SnapshotTable == StdTrack.Table.MachiningTiming))
                SetGc(rows[c.SnapshotRowId!.Value], c.FieldName, c.NewValue);
        }
        var idTaro = Ids(StdTrack.Table.Taro);
        if (idTaro.Count > 0)
            foreach (var s in await _db.KhPlanRouteSnapshotTaro.Where(s => idTaro.Contains(s.SnapshotId)).ToListAsync())
                s.StandardTime = valueChanges.First(c => c.SnapshotTable == StdTrack.Table.Taro && c.SnapshotRowId == s.SnapshotId).NewValue;
        var idBav = Ids(StdTrack.Table.Bavia);
        if (idBav.Count > 0)
            foreach (var s in await _db.KhPlanRouteSnapshotBavia.Where(s => idBav.Contains(s.SnapshotId)).ToListAsync())
                s.StandardTime = valueChanges.First(c => c.SnapshotTable == StdTrack.Table.Bavia && c.SnapshotRowId == s.SnapshotId).NewValue;
        var idWash = Ids(StdTrack.Table.Washing);
        if (idWash.Count > 0)
            foreach (var s in await _db.KhPlanRouteSnapshotWashing.Where(s => idWash.Contains(s.SnapshotId)).ToListAsync())
                s.StandardTime = valueChanges.First(c => c.SnapshotTable == StdTrack.Table.Washing && c.SnapshotRowId == s.SnapshotId).NewValue;
        var idInsp = Ids(StdTrack.Table.Inspection);
        if (idInsp.Count > 0)
            foreach (var s in await _db.KhPlanRouteSnapshotInspection.Where(s => idInsp.Contains(s.SnapshotId)).ToListAsync())
                s.StandardTime = valueChanges.First(c => c.SnapshotTable == StdTrack.Table.Inspection && c.SnapshotRowId == s.SnapshotId).NewValue;
        var idPkg = Ids(StdTrack.Table.Packaging);
        if (idPkg.Count > 0)
            foreach (var s in await _db.KhPlanRouteSnapshotPackaging.Where(s => idPkg.Contains(s.SnapshotId)).ToListAsync())
                s.StandardTime = valueChanges.First(c => c.SnapshotTable == StdTrack.Table.Packaging && c.SnapshotRowId == s.SnapshotId).NewValue;
    }

    private async Task ApplySnapshotConfirmsAsync(List<CellChange> changes, long batchId, int userId, DateTime now)
    {
        if (changes.Count == 0) return;
        var detailIds = changes.Select(c => c.KhPlanDetailId).Distinct().ToList();
        var existing = (await _db.KhPlanRouteSnapshotStdConfirms.Where(x => detailIds.Contains(x.KhPlanDetailId)).ToListAsync())
            .ToDictionary(x => (x.SnapshotTable, x.SnapshotRowId, x.FieldName));

        foreach (var c in changes)
        {
            var key = (c.SnapshotTable!, c.SnapshotRowId!.Value, c.FieldName);
            if (!existing.TryGetValue(key, out var row))
            {
                row = new KhPlanRouteSnapshotStdConfirm
                {
                    KhPlanDetailId = c.KhPlanDetailId,
                    SnapshotTable  = c.SnapshotTable!,
                    SnapshotRowId  = c.SnapshotRowId!.Value,
                    FieldName      = c.FieldName,
                };
                _db.KhPlanRouteSnapshotStdConfirms.Add(row);
                existing[key] = row;
            }
            row.IsConfirmed     = c.NewIsConfirmed;
            row.ConfirmedBy     = c.NewIsConfirmed ? c.NewConfirmedBy     : null;
            row.ConfirmedByName = c.NewIsConfirmed ? c.NewConfirmedByName : null;
            row.ConfirmedAt     = c.NewIsConfirmed ? c.NewConfirmedAt     : null;
            row.SyncSource      = StdTrack.Source.StdUpdate;
            row.SyncBatchId     = batchId;
            row.SyncedAt        = now;
            row.SyncedBy        = userId;
            row.Note            = null;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers đọc/ghi 5 trường GC
    // ════════════════════════════════════════════════════════════════════

    private static decimal? GcVal(KhPlanRouteSnapshotMachining s, string f) => f switch
    {
        StdTrack.Field.Setup => s.SetupTime, StdTrack.Field.Machining => s.MachiningTime,
        StdTrack.Field.Inspection => s.InspectionTime, StdTrack.Field.Preparation => s.PreparationTime,
        StdTrack.Field.TrialRun => s.TrialRunTime, _ => null
    };
    private static decimal? GcVal(KhPlanRouteSnapshotMachiningTiming s, string f) => f switch
    {
        StdTrack.Field.Setup => s.SetupTime, StdTrack.Field.Machining => s.MachiningTime,
        StdTrack.Field.Inspection => s.InspectionTime, StdTrack.Field.Preparation => s.PreparationTime,
        StdTrack.Field.TrialRun => s.TrialRunTime, _ => null
    };
    private static decimal? GcVal(PartMachiningStep s, string f) => f switch
    {
        StdTrack.Field.Setup => s.SetupTime, StdTrack.Field.Machining => s.MachiningTime,
        StdTrack.Field.Inspection => s.InspectionTime, StdTrack.Field.Preparation => s.PreparationTime,
        StdTrack.Field.TrialRun => s.TrialRunTime, _ => null
    };
    private static decimal? GcVal(PartMachiningTiming s, string f) => f switch
    {
        StdTrack.Field.Setup => s.SetupTime, StdTrack.Field.Machining => s.MachiningTime,
        StdTrack.Field.Inspection => s.InspectionTime, StdTrack.Field.Preparation => s.PreparationTime,
        StdTrack.Field.TrialRun => s.TrialRunTime, _ => null
    };

    private static void SetGc(KhPlanRouteSnapshotMachining s, string f, decimal? v)
    {
        switch (f)
        {
            case StdTrack.Field.Setup:       s.SetupTime       = v; break;
            case StdTrack.Field.Machining:   s.MachiningTime   = v; break;
            case StdTrack.Field.Inspection:  s.InspectionTime  = v; break;
            case StdTrack.Field.Preparation: s.PreparationTime = v; break;
            case StdTrack.Field.TrialRun:    s.TrialRunTime    = v; break;
        }
    }
    private static void SetGc(KhPlanRouteSnapshotMachiningTiming s, string f, decimal? v)
    {
        switch (f)
        {
            case StdTrack.Field.Setup:       s.SetupTime       = v; break;
            case StdTrack.Field.Machining:   s.MachiningTime   = v; break;
            case StdTrack.Field.Inspection:  s.InspectionTime  = v; break;
            case StdTrack.Field.Preparation: s.PreparationTime = v; break;
            case StdTrack.Field.TrialRun:    s.TrialRunTime    = v; break;
        }
    }

    private static string? Trunc(string? s, int max) => s == null ? null : (s.Length <= max ? s : s[..max]);
}
