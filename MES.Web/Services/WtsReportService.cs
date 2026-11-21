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
                WtsLogId        = p.WtsLogId, ProcessGroup = p.ProcessGroup ?? "", ProductionMode = p.ProductionMode, CaId = p.CaId,   // [G29-RPT]
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
                ActivityLogId   = a.ActivityLogId,   // [G29-RPT]
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

        // [G29-RPT 2026-11-21] Báo cáo theo kỳ (mockup https://claude.ai/artifact/HH5sobEdA8pFNjGCaDGJKR, Hùng 21/11 09:46 "ok làm"):
        //   mỗi dòng mang Việc (nhóm + mã) + phút CHIA (AllocMin) để bảng theo việc / theo người / theo ngày cộng ĐÚNG bằng tổng trên;
        //   mỗi người-ngày có phút kế hoạch theo ca + "chưa khai báo". Chỉ đọc — không đổi các số tổng cũ ở trên (TotalMinutes / ProductiveMin…).
        //   Nguồn: CLAUDE đề xuất 21/11 (câu B 1–5, Hùng OK 09:46).
        // [G30-RPT-MAUSO 2026-11-21] % có ích / vô ích / chưa khai báo tính trên GIỜ CHUẨN theo ca mỗi người-ngày (WtsReportDay.BaseMin =
        //   phút làm + giờ ca chưa khai báo), không còn trên phút đã khai báo; chỉ ngày có dòng WTS; dòng không ghi ca lấy ca từ khoảng nghỉ đã lưu.
        //   Nguồn: HÙNG 21/11 16:03 (35 phút khai báo không thể là 100%) + 16:10 (chỉ ngày có WTS) + 16:19 "ok làm đi" mockup HH5sobEdA8pFNjGCaDGJKR.
        var taskNames = allWtsTasks.GroupBy(t => t.TaskCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.IsActive).ThenByDescending(t => t.TaskId).First().TaskName ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) SetTask(r, taskNames);
        var days = await BuildDaysAsync(rows, await LoadCoverAfterAsync(dateTo, endDt, workerId));

        return new WtsReportResult
        {
            Rows          = rows,
            TotalMinutes  = totalMin,
            ProductiveMin = productiveMin,
            WasteMin      = wasteMin,
            RawMinutes    = rawMin,
            BreakWorkedMinutes = workedMin,
            Days          = days,   // [G29-RPT]
        };
    }

    // ── [G29-RPT 2026-11-21] Việc của 1 dòng: nhóm + mã + chữ hiện ─────────────────────────────────────────────
    //   Gia công: gom theo Hàng loạt / Chạy thử (KHÔNG theo NC — NC mỗi PO một khác, cộng lại không có nghĩa; câu B5 Hùng OK 21/11).
    //   HTSP / KCS / Đóng gói: theo mã CV (không phân biệt hoa thường). Việc không ra SP: theo mã việc (không mã → theo tên).
    public static string GroupLabelOf(string processGroup) => (processGroup ?? "").ToUpperInvariant() switch
    {
        "GC" => "Gia công", "TARO" => "Taro", "BAVIA" => "Bavia", "WASHING" => "Rửa", "KCS" => "KCS", "PKG" => "Đóng gói",
        "" => "Việc không ra SP", var x => x,
    };

    static void SetTask(WtsReportRow r, Dictionary<string, string> taskNames)
    {
        if (r.RowType == WtsRowType.Activity)
        {
            r.GroupLabel = GroupLabelOf("");
            var code = (r.WtsCode ?? "").Trim();
            r.TaskKey = "ACT|" + (code.Length > 0 ? code.ToUpperInvariant() : "#" + (r.WtsName ?? "").Trim().ToUpperInvariant());
            r.TaskCode = code;
            r.TaskLabel = (r.WtsName ?? "").Trim();
            if (r.TaskLabel.Length == 0 && code.Length > 0 && taskNames.TryGetValue(code, out var tn0)) r.TaskLabel = tn0;
            return;
        }
        var pg = (r.ProcessGroup ?? "").ToUpperInvariant();
        r.GroupLabel = GroupLabelOf(pg);
        if (pg == "GC")
        {
            var mode = r.ProductionMode == "trial" ? "trial" : r.ProductionMode == "serial" ? "serial" : "";
            r.TaskKey = "GC|" + mode;
            r.TaskCode = "";
            r.TaskLabel = mode == "trial" ? "Gia công · Chạy thử" : mode == "serial" ? "Gia công · Hàng loạt" : "Gia công";
            return;
        }
        var c = (r.WtsCode ?? "").Trim();
        if (c.Length == 0) c = (r.NC ?? "").Trim();   // [G29-RPT] dòng HTSP / KCS / Đóng gói cũ (trước [FIX-NONGC-NC]) chở mã CV ở cột NC (rà độc lập 21/11 L9)
        r.TaskKey = pg + "|" + c.ToUpperInvariant();
        r.TaskCode = c;
        r.TaskLabel = c.Length > 0 && taskNames.TryGetValue(c, out var tn) ? tn : "";
    }

    // ── [G29-RPT] Người-ngày: chia phút + kế hoạch theo ca + chưa khai báo ─────────────────────────────────────
    //   Tập phút LÀM của 1 dòng = [BĐ, KT) trừ các khoảng nghỉ đã lưu của dòng, cộng khúc làm thông (đúng cách WorkMinutes.Day gộp) →
    //   hợp các dòng = phút làm người-ngày (= TotalMinutes cũ). Khoảng có nhiều dòng phủ (đứng 2 máy): chia ĐỀU cho các dòng phủ; có dòng
    //   có ích phủ thì chỉ chia cho dòng có ích (giữ luật cũ "phút có cả có ích lẫn vô ích → có ích"). Làm tròn theo phần dư lớn nhất để
    //   Σ phút chia dòng có ích = phút có ích người-ngày, Σ dòng vô ích = phút vô ích (câu B2 Hùng OK 21/11).
    //   Kế hoạch: ca của ngày = ca có nhiều phút nhất trên các dòng sản xuất hôm đó (dòng Việc không ra SP không lưu ca); khung ca trừ giờ nghỉ
    //   HIỆN TẠI của ca. Chưa khai báo = phút kế hoạch không có dòng nào phủ. Ngày không có ca → null (hiện "—") (câu B1 Hùng OK 21/11).
    /// <summary>[G30-RPT-MAUSO] rà độc lập 21/11 (Cao 1): dòng bắt đầu SAU ngày cuối kỳ (tới 24h sau) — CHỈ dùng để phủ "chưa khai báo" của ca qua
    ///   đêm ngày cuối kỳ (không vào bảng, không vào số tổng). Trước: phần ca sau 0h của ngày cuối kỳ thành "chưa khai báo" giả → % có ích tụt
    ///   (xem 1 Ngày ca đêm luôn sai, khác thanh của ngày đó ở xem Tuần).</summary>
    async Task<List<WtsReportRow>> LoadCoverAfterAsync(DateOnly dateTo, DateTime endDt, int? workerId)
    {
        var lim = endDt.AddDays(1);
        var pq = _db.WtsProductionLogs.Where(x => !x.IsVoided && x.StartTime > endDt && x.StartTime <= lim);
        if (workerId.HasValue) pq = pq.Where(x => x.WorkerId == workerId.Value);
        var p = await pq.AsNoTracking().Select(x => new { x.WtsLogId, x.WorkerId, x.StartTime, x.EndTime }).ToListAsync();
        var aq = _db.WorkerActivityLogs.Where(x => !x.IsVoided && x.WorkDate > dateTo && x.StartTime <= lim);
        if (workerId.HasValue) aq = aq.Where(x => x.WorkerId == workerId.Value);
        var a = await aq.AsNoTracking().Select(x => new { x.ActivityLogId, x.WorkerId, x.StartTime, x.EndTime }).ToListAsync();
        var bw = await WorkMinutesStore.ForWtsAsync(_db, p.Select(x => x.WtsLogId));
        var ba = await WorkMinutesStore.ForActivityAsync(_db, a.Select(x => x.ActivityLogId));
        return p.Select(x => new WtsReportRow { WorkerId = x.WorkerId, StartTime = x.StartTime, EndTime = x.EndTime, Breaks = WorkMinutesStore.Get(bw, x.WtsLogId).ToList() })
            .Concat(a.Select(x => new WtsReportRow { WorkerId = x.WorkerId, StartTime = x.StartTime, EndTime = x.EndTime, Breaks = WorkMinutesStore.Get(ba, x.ActivityLogId).ToList() }))
            .ToList();
    }

    async Task<List<WtsReportDay>> BuildDaysAsync(List<WtsReportRow> rows, List<WtsReportRow> coverAfter)
    {
        // [G30-RPT-MAUSO] ca của dòng việc không ra SP (dòng này không lưu ca) = ca của khoảng nghỉ ĐÃ LƯU dòng chạm (câu a — Hùng 21/11 16:10 không phản đối đề xuất)
        var caIds = rows.Select(r => r.CaId ?? BreakCaOf(r)).Where(c => c != null).Select(c => c!.Value).Distinct().ToList();
        var cas = caIds.Count == 0 ? new List<CaLamViec>()
            : await _db.CaLamViecs.Include(c => c.NghiGiaiLaos).AsNoTracking().Where(c => caIds.Contains(c.CaId)).ToListAsync();
        var caMap = cas.ToDictionary(c => c.CaId);
        var res = new List<WtsReportDay>();
        // [G29-RPT] rà độc lập 21/11 (Cao 1, Trung 3): phần phủ của "chưa khai báo" lấy MỌI dòng của người đó chạm khung ca (cả dòng xếp sang ngày
        //   khác — ca qua đêm: dòng sau 0h đang xếp theo ngày giờ bắt đầu, câu RPT-1 chờ khách); kế hoạch trừ thêm khoảng nghỉ ĐÃ LƯU của các dòng
        //   (ca đổi giờ nghỉ sau khi lưu không sinh "chưa khai báo" giả).
        var ivByRow = rows.Concat(coverAfter).ToDictionary(r => r, RowWorkIv);
        var byWorker = rows.Concat(coverAfter).GroupBy(r => r.WorkerId).ToDictionary(g => g.Key, g => g.ToList());   // [G30-RPT-MAUSO] + dòng sau kỳ (chỉ phủ)
        foreach (var g in rows.GroupBy(r => (r.WorkerId, r.WorkDate)).OrderBy(g => g.Key.WorkDate).ThenBy(g => g.First().WorkerName))
        {
            var list = g.ToList();
            var ivs = list.Select(r => ivByRow[r]).ToList();
            Allocate(list, ivs);
            var all = WorkMinutes.Day(list.Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            var pro = WorkMinutes.Day(list.Where(r => r.IsProductiveTask).Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            var d = new WtsReportDay
            {
                WorkerId = g.Key.WorkerId, WorkerName = list[0].WorkerName, Day = g.Key.WorkDate,
                WorkMin = all.Work, ProductiveMin = Math.Min(pro.Work, all.Work), WasteMin = all.Work - Math.Min(pro.Work, all.Work),
                BreakWorkedMin = all.Worked,
            };
            var caId = list.Where(r => r.CaId != null).GroupBy(r => r.CaId!.Value)
                .Select(x => (Id: x.Key, Min: x.Sum(r => r.Minutes))).OrderByDescending(x => x.Min).ThenBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault();
            // [G30-RPT-MAUSO] không có dòng sản xuất ghi ca → lấy ca từ khoảng nghỉ đã lưu của các dòng (dòng việc không ra SP); vẫn không có → "không rõ ca"
            //   rà độc lập 21/11 (Thấp 3): cùng cách ca chính — ca nhiều PHÚT nhất (trước: nhiều dòng nhất)
            caId ??= list.Select(r => (Ca: BreakCaOf(r), r.Minutes)).Where(x => x.Ca != null).GroupBy(x => x.Ca!.Value)
                .Select(x => (Id: x.Key, Min: x.Sum(y => y.Minutes))).OrderByDescending(x => x.Min).ThenBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault();
            // ca qua đêm mà mọi dòng sản xuất của ca đó trong nhóm bắt đầu sau 0h (trước giờ kết thúc ca) = đuôi ca của hôm trước → không tính kế hoạch lần 2
            var tail = caId != null && caMap.TryGetValue(caId.Value, out var ca0) && ca0.IsOvernight
                && list.Where(r => (r.CaId ?? BreakCaOf(r)) == caId).All(r => TimeOnly.FromDateTime(r.StartTime) < ca0.GioKetThuc);
            if (tail && caMap.TryGetValue(caId!.Value, out var caT))
            {   // [G30-RPT-MAUSO] rà độc lập 21/11 (Trung 2): đuôi ca đêm hôm trước — ca đã biết (giờ ca nằm ở kế hoạch hôm trước), KHÔNG phải "không rõ ca"
                d.IsTail = true; d.CaId = caT.CaId; d.CaName = caT.TenCa; d.CaLabel = caT.Label;
            }
            if (!tail && caId != null && caMap.TryGetValue(caId.Value, out var ca))
            {
                var s = g.Key.WorkDate.ToDateTime(ca.GioBatDau);
                var e = (ca.IsOvernight ? g.Key.WorkDate.AddDays(1) : g.Key.WorkDate).ToDateTime(ca.GioKetThuc);
                if (e > s)
                {
                    var mine = byWorker[g.Key.WorkerId].Where(r => r.StartTime < e && r.EndTime > s).ToList();
                    var plan = Sub((s, e), WorkMinutes.BreakSegments(ca, s, e).Select(b => (b.Start, b.End))
                        .Concat(mine.SelectMany(r => r.Breaks).Select(b => (b.BreakStart, b.BreakEnd))));
                    var gap = new List<(DateTime S, DateTime E)>();
                    var cover = mine.SelectMany(r => ivByRow[r]).ToList();
                    foreach (var p in plan) gap.AddRange(Sub(p, cover));
                    d.CaId = ca.CaId; d.CaName = ca.TenCa; d.CaLabel = ca.Label;
                    d.PlanMin = RoundMin(Measure(plan));
                    d.UnaccountedMin = RoundMin(Measure(gap));
                    d.PlanStart = s; d.PlanEnd = e;
                    d.Breaks = WorkMinutes.BreakSegments(ca, s, e).Select(b => (b.Start, b.End)).ToList();
                }
            }
            d.BaseMin = d.WorkMin + (d.UnaccountedMin ?? 0);   // [G30-RPT-MAUSO] giờ chuẩn = phút làm (đã gồm làm thông nghỉ + làm ngoài ca) + giờ ca chưa khai báo
            res.Add(d);
        }
        return res;
    }

    /// <summary>[G30-RPT-MAUSO] Ca của 1 dòng theo khoảng nghỉ đã lưu (WtsBreakWorkLogs.CaId) — dùng khi dòng không có CaId (việc không ra SP).</summary>
    static int? BreakCaOf(WtsReportRow r) => (r.Breaks ?? new()).Select(b => b.CaId).FirstOrDefault(c => c != null);

    /// <summary>[G29-RPT] Tập phút làm của 1 dòng (cùng cách WorkMinutes.Day): ngoài khoảng nghỉ dòng chạm + khúc làm thông.</summary>
    static List<(DateTime S, DateTime E)> RowWorkIv(WtsReportRow r)
    {
        if (r.EndTime <= r.StartTime) return new();
        var brk = (r.Breaks ?? new()).Select(b => (b.BreakStart, b.BreakEnd)).ToList();
        var iv = Sub((r.StartTime, r.EndTime), brk);
        foreach (var b in r.Breaks ?? new())
        {
            if (b.WorkedMin <= 0) continue;
            var c = WorkMinutes.WorkedChunk(r.StartTime, b);
            var ce = c.E < r.EndTime ? c.E : r.EndTime;
            var be = ce < b.BreakEnd ? ce : b.BreakEnd;
            if (be > c.S) iv.Add((c.S, be));
        }
        return Union(iv);
    }

    static void Allocate(List<WtsReportRow> list, List<List<(DateTime S, DateTime E)>> ivs)
    {
        var share = new double[list.Count];
        var cuts = ivs.SelectMany(x => x).SelectMany(x => new[] { x.S, x.E }).Distinct().OrderBy(x => x).ToList();
        for (int k = 0; k + 1 < cuts.Count; k++)
        {
            var a = cuts[k]; var b = cuts[k + 1];
            var len = (b - a).TotalMinutes;
            if (len <= 0) continue;
            var cov = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (ivs[i].Any(x => x.S <= a && x.E >= b)) cov.Add(i);
            if (cov.Count == 0) continue;
            var pro = cov.Where(i => list[i].IsProductiveTask).ToList();
            var who = pro.Count > 0 ? pro : cov;
            foreach (var i in who) share[i] += len / who.Count;
        }
        var all = WorkMinutes.Day(list.Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
        var proDay = WorkMinutes.Day(list.Where(r => r.IsProductiveTask).Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
        var pT = Math.Min(proDay.Work, all.Work);
        RoundTo(list, share, i => list[i].IsProductiveTask, pT);
        RoundTo(list, share, i => !list[i].IsProductiveTask, all.Work - pT);
    }

    /// <summary>Làm tròn phần dư lớn nhất: Σ AllocMin các dòng chọn = đúng target (không lệch 1 phút khi cộng bảng).</summary>
    static void RoundTo(List<WtsReportRow> list, double[] share, Func<int, bool> pick, int target)
    {
        var idx = Enumerable.Range(0, list.Count).Where(pick).ToList();
        if (idx.Count == 0) return;
        foreach (var i in idx) list[i].AllocMin = (int)Math.Floor(share[i] + 1e-9);
        var left = target - idx.Sum(i => list[i].AllocMin);
        var order = idx.OrderByDescending(i => share[i] - Math.Floor(share[i] + 1e-9)).ThenByDescending(i => share[i]).ThenBy(i => i).ToList();
        for (int k = 0; left > 0 && order.Count > 0; k++, left--) list[order[k % order.Count]].AllocMin++;
        for (int k = 0; left < 0 && order.Count > 0; k++)
        {
            var i = order[order.Count - 1 - (k % order.Count)];
            if (list[i].AllocMin > 0) { list[i].AllocMin--; left++; }
            if (k > 10 * order.Count) break;
        }
    }

    static int RoundMin(double m) => (int)Math.Round(m, MidpointRounding.AwayFromZero);
    static List<(DateTime S, DateTime E)> Union(IEnumerable<(DateTime S, DateTime E)> iv)
    {
        var res = new List<(DateTime S, DateTime E)>();
        foreach (var x in iv.Where(x => x.E > x.S).OrderBy(x => x.S))
        {
            if (res.Count > 0 && x.S <= res[^1].E) { if (x.E > res[^1].E) res[^1] = (res[^1].S, x.E); }
            else res.Add(x);
        }
        return res;
    }
    static List<(DateTime S, DateTime E)> Sub((DateTime S, DateTime E) a, IEnumerable<(DateTime S, DateTime E)> cut) => WorkMinutes.Minus(a, cut);
    static double Measure(IEnumerable<(DateTime S, DateTime E)> iv) => Union(iv).Sum(x => (x.E - x.S).TotalMinutes);

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
    // [G29-RPT 2026-11-21] báo cáo theo kỳ: id dòng gốc, nhóm việc, việc (mã), ca, phút chia (Σ theo người-ngày = phút làm)
    public long?     WtsLogId          { get; set; }
    public long?     ActivityLogId     { get; set; }
    public string    ProcessGroup      { get; set; } = "";
    public string?   ProductionMode    { get; set; }
    public int?      CaId              { get; set; }
    public string    GroupLabel        { get; set; } = "";
    public string    TaskKey           { get; set; } = "";
    public string    TaskCode          { get; set; } = "";
    public string    TaskLabel         { get; set; } = "";
    /// <summary>[G29-RPT] Phút làm CHIA cho dòng: khoảng nhiều dòng phủ chia đều (ưu tiên dòng có ích) — Σ người-ngày = phút làm gộp.</summary>
    public int       AllocMin          { get; set; }
}

/// <summary>[G29-RPT 2026-11-21] 1 người-ngày: phút làm / có ích / vô ích (= Σ AllocMin) + kế hoạch theo ca + chưa khai báo.</summary>
public class WtsReportDay
{
    public int       WorkerId       { get; set; }
    public string    WorkerName     { get; set; } = "";
    public DateOnly  Day            { get; set; }
    public int       WorkMin        { get; set; }
    public int       ProductiveMin  { get; set; }
    public int       WasteMin       { get; set; }
    public int?      CaId           { get; set; }
    public string?   CaName         { get; set; }
    public string?   CaLabel        { get; set; }
    /// <summary>Phút kế hoạch = khung ca trừ giờ nghỉ ca; null = ngày không có ca trên dòng WTS.</summary>
    public int?      PlanMin        { get; set; }
    /// <summary>Phút kế hoạch không có dòng WTS nào phủ; null = không có ca.</summary>
    public int?      UnaccountedMin { get; set; }
    public DateTime? PlanStart      { get; set; }
    public DateTime? PlanEnd        { get; set; }
    public List<(DateTime S, DateTime E)> Breaks { get; set; } = new();
    /// <summary>[G30-RPT-MAUSO] Ngày chỉ là đuôi ca qua đêm của hôm trước (dòng sau 0h): có ca, không kế hoạch riêng (giờ ca tính ở hôm trước).</summary>
    public bool      IsTail         { get; set; }
    /// <summary>[G30-RPT-MAUSO] Phút làm thông nghỉ trong ngày (đã nằm trong WorkMin).</summary>
    public int       BreakWorkedMin { get; set; }
    /// <summary>[G30-RPT-MAUSO] Giờ chuẩn = WorkMin + UnaccountedMin (= giờ ca − nghỉ + làm thông nghỉ + làm ngoài ca). Không rõ ca → = WorkMin.
    ///   Mẫu số của mọi % có ích / vô ích / chưa khai báo. Chỉ ngày có dòng WTS (Hùng 21/11 16:10: ngày không có WTS bỏ, không cần lịch nghỉ).</summary>
    public int       BaseMin        { get; set; }
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
    /// <summary>[G29-RPT] Từng người-ngày (kế hoạch ca, chưa khai báo, có ích / vô ích).</summary>
    public List<WtsReportDay> Days          { get; set; } = new();
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
