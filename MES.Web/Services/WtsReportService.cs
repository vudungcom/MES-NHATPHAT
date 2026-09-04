using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service tổng hợp WTS cho trang báo cáo quản lý.
/// Đọc từ WtsProductionLogs + WorkerActivityLogs + StandardWtsTasks.
/// </summary>
public class WtsReportService
{
    private readonly AppDbContext _db;

    public WtsReportService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lấy toàn bộ WTS logs (production + activity) trong khoảng ngày,
    /// có thể lọc thêm theo workerId (null = tất cả công nhân).
    /// </summary>
    public async Task<WtsReportResult> GetReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int? workerId = null)
    {
        var startDt = dateFrom.ToDateTime(TimeOnly.MinValue);
        var endDt   = dateTo.ToDateTime(TimeOnly.MaxValue);

        // ── 1. WtsProductionLogs (có PO/NC) ──────────────────────────────
        var prodQuery = _db.WtsProductionLogs
            .Include(x => x.Worker)
            .Include(x => x.KhPlanDetail)
                .ThenInclude(d => d.KhPlan)
            .Where(x => !x.IsVoided
                     && x.StartTime >= startDt
                     && x.StartTime <= endDt);

        if (workerId.HasValue)
            prodQuery = prodQuery.Where(x => x.WorkerId == workerId.Value);

        var prodLogs = await prodQuery
            .OrderBy(x => x.StartTime)
            .AsNoTracking()
            .ToListAsync();

        // ── 2. WorkerActivityLogs (không có PO) ──────────────────────────
        var actQuery = _db.WorkerActivityLogs
            .Include(x => x.Worker)
            .Where(x => !x.IsVoided
                     && x.WorkDate >= dateFrom
                     && x.WorkDate <= dateTo);

        if (workerId.HasValue)
            actQuery = actQuery.Where(x => x.WorkerId == workerId.Value);

        var actLogs = await actQuery
            .OrderBy(x => x.StartTime)
            .AsNoTracking()
            .ToListAsync();

        // ── 3. Danh sách mã WTS để biết IsProductiveTask ─────────────────
        var allWtsTasks = await _db.StandardWtsTasks
            .Where(t => t.IsActive)
            .AsNoTracking()
            .ToListAsync();

        var wtsLookup = allWtsTasks.ToDictionary(t => t.TaskCode, t => t);

        // ── 4. Gộp thành WtsReportRow ─────────────────────────────────────
        var rows = new List<WtsReportRow>();

        foreach (var p in prodLogs)
        {
            var minutes = (int)Math.Round((p.EndTime - p.StartTime).TotalMinutes);
            rows.Add(new WtsReportRow
            {
                WorkDate        = DateOnly.FromDateTime(p.StartTime),
                WorkerId        = p.WorkerId,
                WorkerName      = p.Worker?.FullName ?? "?",
                StartTime       = p.StartTime,
                EndTime         = p.EndTime,
                Minutes         = minutes,
                PurchaseOrder   = p.KhPlanDetail?.PurchaseOrder ?? "",
                PartNo          = p.KhPlanDetail?.PartNo ?? "",
                NC              = p.NC ?? "",
                WtsCode         = "",
                WtsName         = "",
                MachineUsed     = p.MachineUsed ?? "",
                QtyDone         = p.QtyDone,
                Notes           = p.Notes ?? "",
                IsProductiveTask = true, // production logs luôn là có ích
                RowType         = WtsRowType.Production,
            });
        }

        foreach (var a in actLogs)
        {
            var minutes = (int)Math.Round((a.EndTime - a.StartTime).TotalMinutes);
            var isProductive = true;
            if (a.WtsCode != null && wtsLookup.TryGetValue(a.WtsCode, out var task))
                isProductive = task.IsProductiveTask;

            rows.Add(new WtsReportRow
            {
                WorkDate        = a.WorkDate,
                WorkerId        = a.WorkerId,
                WorkerName      = a.Worker?.FullName ?? "?",
                StartTime       = a.StartTime,
                EndTime         = a.EndTime,
                Minutes         = minutes,
                PurchaseOrder   = "",
                PartNo          = "",
                NC              = "",
                WtsCode         = a.WtsCode ?? "",
                WtsName         = a.WtsName ?? "",
                MachineUsed     = "",
                QtyDone         = 0,
                Notes           = a.Notes ?? "",
                IsProductiveTask = isProductive,
                RowType         = WtsRowType.Activity,
            });
        }

        rows = rows.OrderBy(r => r.WorkDate).ThenBy(r => r.WorkerName).ThenBy(r => r.StartTime).ToList();

        // ── 5. Summary cards ──────────────────────────────────────────────
        var totalMin      = rows.Sum(r => r.Minutes);
        var productiveMin = rows.Where(r => r.IsProductiveTask).Sum(r => r.Minutes);
        var wasteMin      = totalMin - productiveMin;

        return new WtsReportResult
        {
            Rows          = rows,
            TotalMinutes  = totalMin,
            ProductiveMin = productiveMin,
            WasteMin      = wasteMin,
        };
    }

    /// <summary>Danh sách công nhân có WTS log trong khoảng ngày (để populate dropdown).</summary>
    public async Task<List<(int Id, string Name)>> GetWorkersWithLogsAsync(
        DateOnly dateFrom, DateOnly dateTo)
    {
        var startDt = dateFrom.ToDateTime(TimeOnly.MinValue);
        var endDt   = dateTo.ToDateTime(TimeOnly.MaxValue);

        var fromProd = await _db.WtsProductionLogs
            .Include(x => x.Worker)
            .Where(x => !x.IsVoided && x.StartTime >= startDt && x.StartTime <= endDt)
            .Select(x => new { x.WorkerId, Name = x.Worker!.FullName })
            .Distinct()
            .ToListAsync();

        var fromAct = await _db.WorkerActivityLogs
            .Include(x => x.Worker)
            .Where(x => !x.IsVoided && x.WorkDate >= dateFrom && x.WorkDate <= dateTo)
            .Select(x => new { x.WorkerId, Name = x.Worker!.FullName })
            .Distinct()
            .ToListAsync();

        return fromProd.Union(fromAct)
            .Select(x => (x.WorkerId, x.Name))
            .DistinctBy(x => x.WorkerId)
            .OrderBy(x => x.Name)
            .ToList();
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public enum WtsRowType { Production, Activity }

public class WtsReportRow
{
    public DateOnly  WorkDate         { get; set; }
    public int       WorkerId         { get; set; }
    public string    WorkerName       { get; set; } = "";
    public DateTime  StartTime        { get; set; }
    public DateTime  EndTime          { get; set; }
    public int       Minutes          { get; set; }
    public string    PurchaseOrder    { get; set; } = "";
    public string    PartNo           { get; set; } = "";
    public string    NC               { get; set; } = "";
    public string    WtsCode          { get; set; } = "";
    public string    WtsName          { get; set; } = "";
    public string    MachineUsed      { get; set; } = "";
    public decimal   QtyDone          { get; set; }
    public string    Notes            { get; set; } = "";
    public bool      IsProductiveTask { get; set; }
    public WtsRowType RowType         { get; set; }
}

public class WtsReportResult
{
    public List<WtsReportRow> Rows          { get; set; } = new();
    public int                TotalMinutes  { get; set; }
    public int                ProductiveMin { get; set; }
    public int                WasteMin      { get; set; }
    public double EfficiencyPct => TotalMinutes == 0 ? 0 : Math.Round((double)ProductiveMin / TotalMinutes * 100, 1);
}
