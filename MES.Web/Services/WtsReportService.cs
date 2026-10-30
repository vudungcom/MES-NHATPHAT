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
        // [RPT-ACTIVE 2026-10-30] Đọc cả mã đã NGƯNG dùng + so mã không phân biệt hoa thường (như SQL Server).
        //   Trước: chỉ mã đang dùng → mã "vô ích" bị ngưng dùng thì mọi dòng cũ của mã đó thành "Có ích" (hiệu suất các tháng trước đổi).
        //   Trùng mã (1 mã đang dùng + 1 bản cũ đã ngưng) → lấy bản đang dùng.
        var allWtsTasks = await _db.StandardWtsTasks
            .AsNoTracking()
            .ToListAsync();

        var wtsLookup = allWtsTasks
            .GroupBy(t => t.TaskCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.IsActive).ThenByDescending(t => t.TaskId).First(), StringComparer.OrdinalIgnoreCase);

        // [ML16-ACT 2026-10-30] Dòng "Việc không ra sản phẩm" có PO tham chiếu (giờ sửa hàng) + vụ lỗi → hiện PO / Part / loại lỗi của vụ
        var actRefIds = actLogs.Where(a => a.RefKhPlanDetailId != null).Select(a => a.RefKhPlanDetailId!.Value).Distinct().ToList();
        var actRefPo  = actRefIds.Count == 0 ? new Dictionary<int, (string Po, string Part)>()
            : (await _db.KhPlanDetails.AsNoTracking().Where(d => actRefIds.Contains(d.KhPlanDetailId))
                   .Select(d => new { d.KhPlanDetailId, d.PurchaseOrder, d.PartNo }).ToListAsync())
              .ToDictionary(x => x.KhPlanDetailId, x => (x.PurchaseOrder ?? "", x.PartNo ?? ""));
        var actCaseLbl = await NgCaseService.GetCaseLabelsAsync(_db, actLogs.Select(a => a.NgCaseId));

        // ── 4. Gộp thành WtsReportRow ─────────────────────────────────────
        var rows = new List<WtsReportRow>();
        // [DS-LOI-LIVE 2026-10-28] dòng NG có mã vụ → tên loại lỗi hiện tại (DS Lỗi)
        var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db, prodLogs.Select(x => x.NgCaseId));

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
                WtsCode         = p.NC == null ? (p.WtsCode ?? "") : "",   // [RS-E7 2026-10-30] HTSP / KCS / Đóng gói: mã CV nằm ở WtsCode (trước: luôn "", báo cáo mất mã)
                WtsName         = "",
                MachineUsed     = p.MachineUsed ?? "",
                QtyDone         = p.QtyDone,
                QtyOk           = p.QtyOk,
                QtyNg           = p.QtyNg,
                NgReason        = NgCaseService.LiveReason(p.NgCaseId, p.NgReason, caseLbl),   // [DS-LOI-LIVE]
                Notes           = p.Notes ?? "",
                ExtraMinutes    = p.ExtraMinutes,
                IsOvertime      = p.IsOvertime,
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

            var refPo = a.RefKhPlanDetailId != null && actRefPo.TryGetValue(a.RefKhPlanDetailId.Value, out var rp) ? rp : ("", "");
            rows.Add(new WtsReportRow
            {
                WorkDate        = a.WorkDate,
                WorkerId        = a.WorkerId,
                WorkerName      = a.Worker?.FullName ?? "?",
                StartTime       = a.StartTime,
                EndTime         = a.EndTime,
                Minutes         = minutes,
                PurchaseOrder   = refPo.Item1,   // [ML16-ACT] PO tham chiếu (giờ sửa) — "" = việc chung
                PartNo          = refPo.Item2,
                NgReason        = a.NgCaseId != null
                    ? NgCaseNo.Format(a.NgCaseId.Value) + (actCaseLbl.TryGetValue(a.NgCaseId.Value, out var cl) ? " · " + cl : "")
                    : null,                       // [ML16-ACT] vụ lỗi đang sửa
                RefKhPlanDetailId = a.RefKhPlanDetailId,
                NgCaseId        = a.NgCaseId,
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

    /// <summary>
    /// Lấy TẤT CẢ users active trong hệ thống — dùng cho dropdown report Admin.
    /// Không lọc theo khoảng ngày, vì admin cần xem bất kỳ người nào.
    /// </summary>
    public async Task<List<(int Id, string Name)>> GetAllActiveWorkersAsync()
    {
        return await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.UserId, u.FullName })
            .AsNoTracking()
            .ToListAsync()
            .ContinueWith(t => t.Result
                .Select(u => (u.UserId, u.FullName))
                .ToList());
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
    public decimal?  QtyOk            { get; set; }
    public decimal?  QtyNg            { get; set; }
    public string?   NgReason         { get; set; }
    public string    Notes            { get; set; } = "";
    public int?      ExtraMinutes     { get; set; }  // T.Phát sinh (phút)
    public bool      IsOvertime       { get; set; }  // Tăng ca
    public bool      IsProductiveTask { get; set; }
    public WtsRowType RowType         { get; set; }
    public int?      RefKhPlanDetailId { get; set; }   // [ML16-ACT] dòng việc không ra SP: PO tham chiếu (giờ sửa)
    public long?     NgCaseId          { get; set; }   // [ML16-ACT] vụ lỗi đang sửa
}

public class WtsReportResult
{
    public List<WtsReportRow> Rows          { get; set; } = new();
    public int                TotalMinutes  { get; set; }
    public int                ProductiveMin { get; set; }
    public int                WasteMin      { get; set; }
    public double EfficiencyPct => TotalMinutes == 0 ? 0 : Math.Round((double)ProductiveMin / TotalMinutes * 100, 1);

    /// <summary>[ML16-ACT 2026-10-30] Master list 11: cộng giờ "Việc không ra sản phẩm" có ghi PO (giờ sửa hàng) theo từng PO.</summary>
    public List<(string Po, string Part, int Minutes, int Lines, int Cases)> RepairByPo => Rows
        .Where(r => r.RowType == WtsRowType.Activity && r.RefKhPlanDetailId != null)
        .GroupBy(r => r.RefKhPlanDetailId!.Value)
        .Select(g => (g.First().PurchaseOrder, g.First().PartNo, g.Sum(x => x.Minutes), g.Count(), g.Where(x => x.NgCaseId != null).Select(x => x.NgCaseId).Distinct().Count()))
        .OrderByDescending(x => x.Item3)
        .ToList();
}
