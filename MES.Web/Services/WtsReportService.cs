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
    // [RPT-4 2026-10-31] Máy chủ tự kiểm quyền Xem "WTS Report" (ma trận DB, PermCheck) — trước: chỉ trang ẩn tab.
    //   Không có quyền → kết quả rỗng (trang vốn không hiện tab cho người này nên không có chữ báo mới).
    public async Task<WtsReportResult> GetReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int? workerId,
        int viewerUserId)
    {
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.WtsReport))
            return new WtsReportResult();   // [RPT-4]
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

        // [ML17-XN][CA-NGHI-HIST 2026-11-01] Khoảng nghỉ ĐÃ LƯU của từng dòng (giờ nghỉ ca lúc lưu + phút làm thông nghỉ) —
        //   phút dòng tính bằng WorkMinutes (cùng hàm trang WTS). Trước: Minutes = giờ thô (End − Start), không trừ nghỉ.
        var brkW = await WorkMinutesStore.ForWtsAsync(_db, prodLogs.Select(x => x.WtsLogId));
        var brkA = await WorkMinutesStore.ForActivityAsync(_db, actLogs.Select(x => x.ActivityLogId));

        // ── 4. Gộp thành WtsReportRow ─────────────────────────────────────
        var rows = new List<WtsReportRow>();
        // [DS-LOI-LIVE 2026-10-28] dòng NG có mã vụ → tên loại lỗi hiện tại (DS Lỗi)
        var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db, prodLogs.Select(x => x.NgCaseId));

        foreach (var p in prodLogs)
        {
            var pBrk = WorkMinutesStore.Get(brkW, p.WtsLogId);
            var pm = WorkMinutes.Line(p.StartTime, p.EndTime, pBrk);   // [ML17-XN]
            rows.Add(new WtsReportRow
            {
                WorkDate        = DateOnly.FromDateTime(p.StartTime),
                WorkerId        = p.WorkerId,
                WorkerName      = p.Worker?.FullName ?? "?",
                StartTime       = p.StartTime,
                EndTime         = p.EndTime,
                Minutes         = pm.Work,        // [ML17-XN] phút LÀM (đã trừ nghỉ, cộng làm thông nghỉ) — câu (b) Hùng 01/11
                RawMinutes      = pm.Raw,
                BreakWorkedMin  = pm.Worked,
                BreakDeductMin  = pm.Deducted,
                Breaks          = pBrk.ToList(),
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
            var aBrk = WorkMinutesStore.Get(brkA, a.ActivityLogId);
            var am = WorkMinutes.Line(a.StartTime, a.EndTime, aBrk);   // [ML17-XN]
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
                Minutes         = am.Work,        // [ML17-XN] phút làm
                RawMinutes      = am.Raw,
                BreakWorkedMin  = am.Worked,
                BreakDeductMin  = am.Deducted,
                Breaks          = aBrk.ToList(),
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
        // [ML17-XN 2026-11-01] Câu (b) Hùng chốt: % hiệu suất theo PHÚT LÀM (trước: phút thô, khác cột Phút trang WTS).
        //   Gộp theo người-ngày như "Tổng hôm nay" của trang WTS (WorkMinutes.Day): giờ chồng nhau (GC đứng 2 máy) chỉ tính 1 lần
        //   (trước: cộng 2 lần). Phút có ích = phút làm phủ bởi dòng có ích (phút có cả dòng có ích lẫn vô ích phủ → có ích).
        int totalMin = 0, productiveMin = 0, rawMin = 0, workedMin = 0;
        foreach (var g in rows.GroupBy(r => (r.WorkerId, r.WorkDate)))
        {
            var all = WorkMinutes.Day(g.Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            var pro = WorkMinutes.Day(g.Where(r => r.IsProductiveTask).Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            totalMin += all.Work; rawMin += all.Raw; workedMin += all.Worked;
            productiveMin += Math.Min(pro.Work, all.Work);
        }
        var wasteMin      = totalMin - productiveMin;

        return new WtsReportResult
        {
            Rows          = rows,
            TotalMinutes  = totalMin,
            ProductiveMin = productiveMin,
            WasteMin      = wasteMin,
            RawMinutes    = rawMin,
            BreakWorkedMinutes = workedMin,
        };
    }

    /// <summary>Danh sách công nhân có WTS log trong khoảng ngày (để populate dropdown).</summary>
    public async Task<List<(int Id, string Name)>> GetWorkersWithLogsAsync(
        DateOnly dateFrom, DateOnly dateTo, int viewerUserId)
    {
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.WtsReport))
            return new List<(int Id, string Name)>();   // [RPT-4]
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
    public async Task<List<(int Id, string Name)>> GetAllActiveWorkersAsync(int viewerUserId)
    {
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.WtsReport))
            return new List<(int Id, string Name)>();   // [RPT-4]
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
    // [ML17-XN 2026-11-01] Minutes = phút LÀM; Thô = Thường + Thông + Trừ (cùng WorkMinutes.Line trang WTS)
    public int       RawMinutes       { get; set; }
    public int       BreakWorkedMin   { get; set; }  // phút làm thông nghỉ
    public int       BreakDeductMin   { get; set; }  // phút nghỉ bị trừ
    public List<LineBreak> Breaks     { get; set; } = new();   // giờ nghỉ chép lúc lưu
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
    /// <summary>[ML17-XN] Giờ thô gộp người-ngày + phút làm thông nghỉ gộp (TotalMinutes = phút làm).</summary>
    public int                RawMinutes    { get; set; }
    public int                BreakWorkedMinutes { get; set; }
    public double EfficiencyPct => TotalMinutes == 0 ? 0 : Math.Round((double)ProductiveMin / TotalMinutes * 100, 1);

    /// <summary>[ML17-XN 2026-11-01] Phút làm thông nghỉ theo ngày · người · khoảng nghỉ · PO (mỗi dòng WTS / việc × khoảng nghỉ có làm).</summary>
    public List<(DateOnly Day, string Worker, string Break, string Po, string Code, int Minutes)> BreakWorkRows => Rows
        .SelectMany(r => r.Breaks.Where(b => b.WorkedMin > 0).Select(b => new
        {
            Day = r.WorkDate, Worker = r.WorkerName, Break = b.Label, Po = r.PurchaseOrder,
            Code = string.IsNullOrEmpty(r.NC) ? r.WtsCode : r.NC, Min = b.WorkedMin,
        }))
        .GroupBy(x => (x.Day, x.Worker, x.Break, x.Po, x.Code))
        .Select(g => (g.Key.Day, g.Key.Worker, g.Key.Break, g.Key.Po, g.Key.Code, g.Sum(x => x.Min)))
        .OrderBy(x => x.Day).ThenBy(x => x.Worker).ThenBy(x => x.Break)
        .ToList();

    /// <summary>[ML16-ACT 2026-10-30] Master list 11: cộng giờ "Việc không ra sản phẩm" có ghi PO (giờ sửa hàng) theo từng PO.</summary>
    public List<(string Po, string Part, int Minutes, int Lines, int Cases)> RepairByPo => Rows
        .Where(r => r.RowType == WtsRowType.Activity && r.RefKhPlanDetailId != null)
        .GroupBy(r => r.RefKhPlanDetailId!.Value)
        .Select(g => (g.First().PurchaseOrder, g.First().PartNo, g.Sum(x => x.Minutes), g.Count(), g.Where(x => x.NgCaseId != null).Select(x => x.NgCaseId).Distinct().Count()))
        .OrderByDescending(x => x.Item3)
        .ToList();
}
