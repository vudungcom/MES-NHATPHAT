using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service tổng hợp hiệu suất máy theo timeline ngày.
/// Nguồn data:
///   - WtsProductionLogs (MachineUsed + StartTime/EndTime) → đoạn máy đang chạy (xanh)
///   - ThietBiChangeLogs (ThoiGianBatDau / ThoiGianKetThuc) → đoạn dừng máy (đỏ/vàng)
///   - Phần còn lại → rảnh (xám)
/// Không có bảng SQL mới — chỉ đọc, tận dụng index đã thêm.
/// </summary>
public class MachineEfficiencyService
{
    private readonly AppDbContext _db;

    public MachineEfficiencyService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MachineEfficiencyResult> GetAsync(DateOnly date, TimeOnly fromTime, TimeOnly toTime)
    {
        var dayStart = date.ToDateTime(fromTime);
        var dayEnd   = date.ToDateTime(toTime);

        // ── 1. Danh sách máy active ─────────────────────────────────────────
        var machines = await _db.ThietBis
            .Where(t => t.IsActive)
            .OrderBy(t => t.BoPhan).ThenBy(t => t.SoMay)
            .AsNoTracking()
            .ToListAsync();

        if (!machines.Any())
            return EmptyResult(date, fromTime, toTime);

        var allSoMay    = machines.Select(m => m.SoMay).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allMachineIds = machines.Select(m => m.ThietBiId).ToList();

        // ── 2. WTS logs: chỉ lấy dòng có MachineUsed, không void, overlap khung giờ ──
        // Tận dụng index: (MachineUsed, StartTime, EndTime) với filter IsVoided=0, MachineUsed NOT NULL
        var wtsLogs = await _db.WtsProductionLogs
            .Include(x => x.Worker)
            .Include(x => x.KhPlanDetail)
                .ThenInclude(d => d!.KhPlan)
            .Where(x => !x.IsVoided
                     && x.MachineUsed != null
                     && x.StartTime < dayEnd
                     && x.EndTime   > dayStart)
            .AsNoTracking()
            .ToListAsync();

        // Bỏ máy không có trong danh sách ThietBis (dữ liệu nhập tự do)
        var filteredWts = wtsLogs
            .Where(x => allSoMay.Contains(x.MachineUsed!))
            .ToList();

        // ── 3. Downtime: chỉ lấy log có ThoiGianBatDau và overlap khung giờ ─
        // Tận dụng index: (ThietBiId, ThoiGianBatDau, ThoiGianKetThuc)
        var downLogs = await _db.ThietBiChangeLogs
            .Where(x => allMachineIds.Contains(x.ThietBiId)
                     && x.ThoiGianBatDau != null
                     && x.ThoiGianBatDau < dayEnd
                     && (x.ThoiGianKetThuc == null || x.ThoiGianKetThuc > dayStart))
            .AsNoTracking()
            .ToListAsync();

        // ── 4. Group in-memory để tránh N+1 ───────────────────────────────
        var wtsByMachine   = filteredWts.GroupBy(x => x.MachineUsed!, StringComparer.OrdinalIgnoreCase)
                                         .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var downByMachine  = downLogs.GroupBy(x => x.ThietBiId)
                                      .ToDictionary(g => g.Key, g => g.ToList());

        // ── 5. Build per-machine rows ───────────────────────────────────────
        var rows = new List<MachineRow>();

        foreach (var m in machines)
        {
            var wtsForMachine  = wtsByMachine.TryGetValue(m.SoMay, out var w) ? w : new List<WtsProductionLog>();
            var downForMachine = downByMachine.TryGetValue(m.ThietBiId, out var d) ? d : new List<ThietBiChangeLog>();

            var wtsSegs = wtsForMachine.Select(x => new TimelineSegment
            {
                Start         = x.StartTime < dayStart ? dayStart : x.StartTime,
                End           = x.EndTime   > dayEnd   ? dayEnd   : x.EndTime,
                Type          = SegmentType.Running,
                WorkerName    = x.Worker?.FullName ?? "?",
                WorkerId      = x.WorkerId,
                NC            = x.NC ?? "",
                PurchaseOrder = x.KhPlanDetail?.PurchaseOrder ?? "",
                PartNo        = x.KhPlanDetail?.PartNo ?? "",
                WtsLogId      = x.WtsLogId,
            }).Where(s => s.End > s.Start).ToList();

            var downSegs = downForMachine.Select(x =>
            {
                var start = x.ThoiGianBatDau!.Value < dayStart ? dayStart : x.ThoiGianBatDau!.Value;
                var end   = x.ThoiGianKetThuc.HasValue
                                ? (x.ThoiGianKetThuc.Value > dayEnd ? dayEnd : x.ThoiGianKetThuc.Value)
                                : dayEnd; // đang hỏng chưa khắc phục

                var isMaintenance = ContainsIgnoreCase(x.NewValue, "bảo trì")
                                 || ContainsIgnoreCase(x.OldValue, "bảo trì")
                                 || ContainsIgnoreCase(x.Reason,   "bảo trì");

                return new TimelineSegment
                {
                    Start  = start,
                    End    = end,
                    Type   = isMaintenance ? SegmentType.Maintenance : SegmentType.Breakdown,
                    Reason = x.Reason ?? "",
                    IsOpen = !x.ThoiGianKetThuc.HasValue,
                };
            }).Where(s => s.End > s.Start).ToList();

            var segments = BuildTimeline(dayStart, dayEnd, wtsSegs, downSegs);

            var runMin  = segments.Where(s => s.Type == SegmentType.Running).Sum(s => s.Minutes);
            var downMin = segments.Where(s => s.Type is SegmentType.Breakdown or SegmentType.Maintenance).Sum(s => s.Minutes);
            var totalMin = (dayEnd - dayStart).TotalMinutes;
            var idleMin  = Math.Max(0, totalMin - runMin - downMin);

            rows.Add(new MachineRow
            {
                Machine      = m,
                Segments     = segments,
                WtsDetails   = wtsForMachine,
                TotalMinutes = totalMin,
                RunMinutes   = runMin,
                DownMinutes  = downMin,
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
            IdleMinutes  = rows.Sum(r => r.IdleMinutes),
        };
    }

    /// <summary>
    /// Merge WTS + downtime segments, fill khoảng trống bằng Idle.
    /// Ưu tiên: Downtime > WTS > Idle (downtime đưa vào trước để override khi overlap)
    /// </summary>
    private static List<TimelineSegment> BuildTimeline(
        DateTime dayStart, DateTime dayEnd,
        List<TimelineSegment> wts,
        List<TimelineSegment> down)
    {
        // Downtime có độ ưu tiên cao hơn WTS nếu overlap
        // Strategy: đưa downtime vào trước, sau đó chen WTS vào phần không bị downtime che
        var blocked = down.OrderBy(s => s.Start).ToList();

        var effective = new List<TimelineSegment>();

        // Trải WTS theo khoảng không bị downtime che
        foreach (var wSeg in wts.OrderBy(s => s.Start))
        {
            var cursor = wSeg.Start;
            foreach (var dSeg in blocked.Where(d => d.End > wSeg.Start && d.Start < wSeg.End))
            {
                if (dSeg.Start > cursor)
                    effective.Add(wSeg with { Start = cursor, End = dSeg.Start });
                cursor = dSeg.End > cursor ? dSeg.End : cursor;
            }
            if (cursor < wSeg.End)
                effective.Add(wSeg with { Start = cursor, End = wSeg.End });
        }

        // Gộp tất cả: effective WTS + downtime, sort
        var all = effective.Concat(down).OrderBy(s => s.Start).ToList();

        // Fill idle vào khoảng trống
        var result = new List<TimelineSegment>();
        var pos = dayStart;

        foreach (var seg in all)
        {
            if (seg.Start > pos)
                result.Add(new TimelineSegment { Start = pos, End = seg.Start, Type = SegmentType.Idle });

            var sStart = seg.Start < pos    ? pos    : seg.Start;
            var sEnd   = seg.End   > dayEnd ? dayEnd : seg.End;
            if (sEnd > sStart)
            {
                result.Add(seg with { Start = sStart, End = sEnd });
                pos = sEnd;
            }
        }

        if (pos < dayEnd)
            result.Add(new TimelineSegment { Start = pos, End = dayEnd, Type = SegmentType.Idle });

        return result;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => source != null && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static MachineEfficiencyResult EmptyResult(DateOnly date, TimeOnly from, TimeOnly to)
        => new() { Date = date, FromTime = from, ToTime = to };
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public enum SegmentType { Running, Breakdown, Maintenance, Idle }

public record TimelineSegment
{
    public DateTime    Start         { get; init; }
    public DateTime    End           { get; init; }
    public SegmentType Type          { get; init; }
    // Running
    public string      WorkerName    { get; init; } = "";
    public int         WorkerId      { get; init; }
    public string      NC            { get; init; } = "";
    public string      PurchaseOrder { get; init; } = "";
    public string      PartNo        { get; init; } = "";
    public long        WtsLogId      { get; init; }
    // Downtime
    public string      Reason        { get; init; } = "";
    public bool        IsOpen        { get; init; }

    public double Minutes => (End - Start).TotalMinutes;
}

public class MachineRow
{
    public ThietBi               Machine      { get; set; } = null!;
    public List<TimelineSegment> Segments     { get; set; } = new();
    public List<WtsProductionLog> WtsDetails  { get; set; } = new();
    public double TotalMinutes { get; set; }
    public double RunMinutes   { get; set; }
    public double DownMinutes  { get; set; }
    public double IdleMinutes  { get; set; }

    public double RunPct  => TotalMinutes == 0 ? 0 : Math.Round(RunMinutes  / TotalMinutes * 100, 1);
    public double DownPct => TotalMinutes == 0 ? 0 : Math.Round(DownMinutes / TotalMinutes * 100, 1);
    public double IdlePct => TotalMinutes == 0 ? 0 : Math.Round(IdleMinutes / TotalMinutes * 100, 1);
}

public class MachineEfficiencyResult
{
    public DateOnly         Date         { get; set; }
    public TimeOnly         FromTime     { get; set; }
    public TimeOnly         ToTime       { get; set; }
    public List<MachineRow> Rows         { get; set; } = new();
    public double TotalMinutes { get; set; }
    public double RunMinutes   { get; set; }
    public double DownMinutes  { get; set; }
    public double IdleMinutes  { get; set; }

    public double RunPct  => TotalMinutes == 0 ? 0 : Math.Round(RunMinutes  / TotalMinutes * 100, 1);
    public double DownPct => TotalMinutes == 0 ? 0 : Math.Round(DownMinutes / TotalMinutes * 100, 1);
    public double IdlePct => TotalMinutes == 0 ? 0 : Math.Round(IdleMinutes / TotalMinutes * 100, 1);
}
