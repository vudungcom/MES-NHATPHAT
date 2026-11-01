using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service tổng hợp hiệu suất máy theo timeline ngày.
/// Segment priority: Downtime > Break > Running > Idle
/// Break = nghỉ giải lao theo ca — không tính vào idle, không tính vào running.
/// Ca vắt đêm (GioKetThuc &lt; GioBatDau): hiển thị trên ngày bắt đầu ca,
///   phần qua nửa đêm vẫn nằm trên timeline 0h-24h cùng ngày (wrap).
/// </summary>
public class MachineEfficiencyService
{
    private readonly AppDbContext _db;
    public MachineEfficiencyService(AppDbContext db) { _db = db; }

    public async Task<MachineEfficiencyResult> GetAsync(
        DateOnly date, TimeOnly fromTime, TimeOnly toTime,
        CaLamViec? selectedCa = null)
    {
        var dayStart = date.ToDateTime(fromTime);
        // [RPT-3 2026-10-30] Ca vắt đêm (vd 20:00–05:00): giờ kết thúc sang ngày hôm sau — trước: chỉ queryEnd được nới,
        //   dayEnd vẫn cùng ngày → tổng phút âm, mọi đoạn WTS bị cắt hết, hiệu suất 0 %.
        var dayEnd   = toTime < fromTime ? date.AddDays(1).ToDateTime(toTime) : date.ToDateTime(toTime);

        // ── 1. Danh sách máy active ─────────────────────────────────────
        var machines = await _db.ThietBis
            .Where(t => t.IsActive)
            .OrderBy(t => t.BoPhan).ThenBy(t => t.SoMay)
            .AsNoTracking().ToListAsync();

        if (!machines.Any()) return EmptyResult(date, fromTime, toTime);

        var allSoMay      = machines.Select(m => m.SoMay).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allMachineIds = machines.Select(m => m.ThietBiId).ToList();

        // ── 2. WTS logs (tận dụng filtered index MachineUsed+StartTime+EndTime) ──
        // Ca vắt đêm: query mở rộng thêm 1 ngày để bắt phần sang ngày hôm sau
        var queryEnd = toTime < fromTime
            ? date.AddDays(1).ToDateTime(toTime)
            : dayEnd;

        var wtsLogs = await _db.WtsProductionLogs
            .Include(x => x.Worker)
            .Include(x => x.KhPlanDetail).ThenInclude(d => d!.KhPlan)
            .Where(x => !x.IsVoided
                     && x.MachineUsed != null
                     && x.StartTime < queryEnd
                     && x.EndTime   > dayStart)
            .AsNoTracking().ToListAsync();

        var filteredWts = wtsLogs
            .Where(x => allSoMay.Contains(x.MachineUsed!)).ToList();

        // [ML17-XN][CA-NGHI-HIST 2026-11-01] Khoảng nghỉ ĐÃ LƯU của từng dòng WTS (giờ nghỉ ca lúc lưu + khúc làm thông nghỉ):
        //   đoạn Chạy = giờ dòng trừ phần nghỉ thật của CHÍNH dòng đó (WorkMinutes.WorkPieces); khoảng nghỉ của ca đang xem không còn
        //   đè lên khúc làm thông nghỉ. Trước: giờ nghỉ ca (hiện tại) che mọi đoạn chạy → làm thông nghỉ thành "Nghỉ".
        var brkW = await WorkMinutesStore.ForWtsAsync(_db, filteredWts.Select(x => x.WtsLogId));

        // ── 3. Downtime logs ─────────────────────────────────────────────
        var downLogs = await _db.ThietBiChangeLogs
            .Where(x => allMachineIds.Contains(x.ThietBiId)
                     && x.ThoiGianBatDau != null
                     && x.ThoiGianBatDau < queryEnd
                     && (x.ThoiGianKetThuc == null || x.ThoiGianKetThuc > dayStart))
            .AsNoTracking().ToListAsync();

        // ── 4. Nghỉ giải lao — convert sang DateTime segments ────────────
        var breakSegs = selectedCa != null
            ? CaLamViecService.GetBreakSegments(selectedCa, dayStart, dayEnd)
            : new List<(DateTime, DateTime)>();

        var breakSegments = breakSegs.Select(b => new TimelineSegment
        {
            Start = b.Item1, End = b.Item2, Type = SegmentType.Break,
        }).ToList();

        // ── 5. Group in-memory ───────────────────────────────────────────
        var wtsByMachine  = filteredWts
            .GroupBy(x => x.MachineUsed!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var downByMachine = downLogs
            .GroupBy(x => x.ThietBiId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── 6. Build per-machine rows ────────────────────────────────────
        var rows = new List<MachineRow>();

        foreach (var m in machines)
        {
            var wtsForMachine  = wtsByMachine.TryGetValue(m.SoMay, out var w) ? w : new();
            var downForMachine = downByMachine.TryGetValue(m.ThietBiId, out var d) ? d : new();

            var wtsSegs = wtsForMachine.SelectMany(x => WorkMinutes.WorkPieces(x.StartTime, x.EndTime, WorkMinutesStore.Get(brkW, x.WtsLogId))
                .Select(pc => new TimelineSegment   // [ML17-XN] mỗi phần giờ làm của dòng (nghỉ thật của dòng bị bỏ ra)
            {
                Start         = pc.S < dayStart ? dayStart : pc.S,
                End           = pc.E > dayEnd   ? dayEnd   : pc.E,
                Type          = SegmentType.Running,
                WorkerName    = x.Worker?.FullName ?? "?",
                WorkerId      = x.WorkerId,
                NC            = x.NC ?? "",
                PurchaseOrder = x.KhPlanDetail?.PurchaseOrder ?? "",
                PartNo        = x.KhPlanDetail?.PartNo ?? "",
                WtsLogId      = x.WtsLogId,
            })).Where(s => s.End > s.Start).ToList();
            // [ML17-XN] khoảng nghỉ ca đang xem trừ đi giờ máy đang chạy (khúc làm thông nghỉ giữ là Chạy)
            var runIv = wtsSegs.Select(w => (w.Start, w.End)).ToList();
            var machineBreaks = runIv.Count == 0 ? breakSegments
                : breakSegments.SelectMany(b => WorkMinutes.Minus((b.Start, b.End), runIv).Select(pc => b with { Start = pc.S, End = pc.E })).ToList();

            var downSegs = downForMachine.Select(x =>
            {
                var s = x.ThoiGianBatDau!.Value < dayStart ? dayStart : x.ThoiGianBatDau!.Value;
                var e = x.ThoiGianKetThuc.HasValue
                    ? (x.ThoiGianKetThuc.Value > dayEnd ? dayEnd : x.ThoiGianKetThuc.Value)
                    : dayEnd;
                var isMaint = ContainsIc(x.NewValue, "bảo trì") || ContainsIc(x.OldValue, "bảo trì") || ContainsIc(x.Reason, "bảo trì");
                return new TimelineSegment
                {
                    Start = s, End = e,
                    Type  = isMaint ? SegmentType.Maintenance : SegmentType.Breakdown,
                    Reason = x.Reason ?? "", IsOpen = !x.ThoiGianKetThuc.HasValue,
                };
            }).Where(s => s.End > s.Start).ToList();

            var segments = BuildTimeline(dayStart, dayEnd, wtsSegs, downSegs, machineBreaks);   // [ML17-XN]

            var runMin   = segments.Where(s => s.Type == SegmentType.Running).Sum(s => s.Minutes);
            var downMin  = segments.Where(s => s.Type is SegmentType.Breakdown or SegmentType.Maintenance).Sum(s => s.Minutes);
            var breakMin = segments.Where(s => s.Type == SegmentType.Break).Sum(s => s.Minutes);
            var totalMin = (dayEnd - dayStart).TotalMinutes;
            var idleMin  = Math.Max(0, totalMin - runMin - downMin - breakMin);

            rows.Add(new MachineRow
            {
                LogMinutes   = wtsForMachine.ToDictionary(x => x.WtsLogId, x => WorkMinutes.Line(x.StartTime, x.EndTime, WorkMinutesStore.Get(brkW, x.WtsLogId))),   // [ML17-XN]
                Machine      = m,
                Segments     = segments,
                WtsDetails   = wtsForMachine,
                TotalMinutes = totalMin,
                RunMinutes   = runMin,
                DownMinutes  = downMin,
                BreakMinutes = breakMin,
                IdleMinutes  = idleMin,
            });
        }

        return new MachineEfficiencyResult
        {
            Date         = date,
            FromTime     = fromTime,
            ToTime       = toTime,
            Rows         = rows,
            TotalMinutes = rows.Sum(r => r.TotalMinutes),
            RunMinutes   = rows.Sum(r => r.RunMinutes),
            DownMinutes  = rows.Sum(r => r.DownMinutes),
            BreakMinutes = rows.Sum(r => r.BreakMinutes),
            IdleMinutes  = rows.Sum(r => r.IdleMinutes),
        };
    }

    /// <summary>
    /// Priority: Downtime > Break > Running > Idle
    /// Break segments áp dụng toàn bộ máy (không phân biệt từng máy).
    /// </summary>
    private static List<TimelineSegment> BuildTimeline(
        DateTime dayStart, DateTime dayEnd,
        List<TimelineSegment> wts,
        List<TimelineSegment> down,
        List<TimelineSegment> breaks)
    {
        // 1. Downtime che WTS
        var blockedByDown = down.OrderBy(s => s.Start).ToList();
        var effectiveWts  = new List<TimelineSegment>();

        foreach (var wSeg in wts.OrderBy(s => s.Start))
        {
            var cursor = wSeg.Start;
            foreach (var dSeg in blockedByDown.Where(d => d.End > wSeg.Start && d.Start < wSeg.End))
            {
                if (dSeg.Start > cursor)
                    effectiveWts.Add(wSeg with { Start = cursor, End = dSeg.Start });
                if (dSeg.End > cursor) cursor = dSeg.End;
            }
            if (cursor < wSeg.End)
                effectiveWts.Add(wSeg with { Start = cursor, End = wSeg.End });
        }

        // 2. Break che WTS (nghỉ giải lao override running)
        var blockedByBreak = breaks.OrderBy(s => s.Start).ToList();
        var effectiveWts2  = new List<TimelineSegment>();

        foreach (var wSeg in effectiveWts.OrderBy(s => s.Start))
        {
            var cursor = wSeg.Start;
            foreach (var bSeg in blockedByBreak.Where(b => b.End > wSeg.Start && b.Start < wSeg.End))
            {
                if (bSeg.Start > cursor)
                    effectiveWts2.Add(wSeg with { Start = cursor, End = bSeg.Start });
                if (bSeg.End > cursor) cursor = bSeg.End;
            }
            if (cursor < wSeg.End)
                effectiveWts2.Add(wSeg with { Start = cursor, End = wSeg.End });
        }

        // 3. Merge tất cả (downtime > break > wts) rồi fill idle
        var all = effectiveWts2.Concat(down).Concat(breaks)
                               .OrderBy(s => s.Start).ToList();

        var result = new List<TimelineSegment>();
        var pos = dayStart;

        foreach (var seg in all)
        {
            if (seg.Start > pos)
                result.Add(new TimelineSegment { Start = pos, End = seg.Start, Type = SegmentType.Idle });

            var sStart = seg.Start < pos    ? pos    : seg.Start;
            var sEnd   = seg.End   > dayEnd ? dayEnd : seg.End;
            if (sEnd > sStart) { result.Add(seg with { Start = sStart, End = sEnd }); pos = sEnd; }
        }

        if (pos < dayEnd)
            result.Add(new TimelineSegment { Start = pos, End = dayEnd, Type = SegmentType.Idle });

        return result;
    }

    private static bool ContainsIc(string? s, string v)
        => s != null && s.Contains(v, StringComparison.OrdinalIgnoreCase);

    private static MachineEfficiencyResult EmptyResult(DateOnly d, TimeOnly f, TimeOnly t)
        => new() { Date = d, FromTime = f, ToTime = t };
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public enum SegmentType { Running, Breakdown, Maintenance, Break, Idle }

public record TimelineSegment
{
    public DateTime    Start         { get; init; }
    public DateTime    End           { get; init; }
    public SegmentType Type          { get; init; }
    public string      WorkerName    { get; init; } = "";
    public int         WorkerId      { get; init; }
    public string      NC            { get; init; } = "";
    public string      PurchaseOrder { get; init; } = "";
    public string      PartNo        { get; init; } = "";
    public long        WtsLogId      { get; init; }
    public string      Reason        { get; init; } = "";
    public bool        IsOpen        { get; init; }
    public double Minutes => (End - Start).TotalMinutes;
}

public class MachineRow
{
    public ThietBi                Machine      { get; set; } = null!;
    public List<TimelineSegment>  Segments     { get; set; } = new();
    public List<WtsProductionLog> WtsDetails   { get; set; } = new();
    /// <summary>[ML17-XN] Phút từng dòng WTS (WorkMinutes.Line với giờ nghỉ đã lưu) — cột Phút bảng chi tiết máy.</summary>
    public Dictionary<long, LineMin> LogMinutes { get; set; } = new();
    public double TotalMinutes  { get; set; }
    public double RunMinutes    { get; set; }
    public double DownMinutes   { get; set; }
    public double BreakMinutes  { get; set; }
    public double IdleMinutes   { get; set; }

    public double RunPct   => TotalMinutes == 0 ? 0 : Math.Round(RunMinutes   / TotalMinutes * 100, 1);
    public double DownPct  => TotalMinutes == 0 ? 0 : Math.Round(DownMinutes  / TotalMinutes * 100, 1);
    public double BreakPct => TotalMinutes == 0 ? 0 : Math.Round(BreakMinutes / TotalMinutes * 100, 1);
    public double IdlePct  => TotalMinutes == 0 ? 0 : Math.Round(IdleMinutes  / TotalMinutes * 100, 1);
}

public class MachineEfficiencyResult
{
    public DateOnly         Date         { get; set; }
    public TimeOnly         FromTime     { get; set; }
    public TimeOnly         ToTime       { get; set; }
    public List<MachineRow> Rows         { get; set; } = new();
    public double TotalMinutes  { get; set; }
    public double RunMinutes    { get; set; }
    public double DownMinutes   { get; set; }
    public double BreakMinutes  { get; set; }
    public double IdleMinutes   { get; set; }

    public double RunPct   => TotalMinutes == 0 ? 0 : Math.Round(RunMinutes   / TotalMinutes * 100, 1);
    public double DownPct  => TotalMinutes == 0 ? 0 : Math.Round(DownMinutes  / TotalMinutes * 100, 1);
    public double BreakPct => TotalMinutes == 0 ? 0 : Math.Round(BreakMinutes / TotalMinutes * 100, 1);
    public double IdlePct  => TotalMinutes == 0 ? 0 : Math.Round(IdleMinutes  / TotalMinutes * 100, 1);
}
