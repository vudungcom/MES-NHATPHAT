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

        // [WTS-1NGUON 2026-11-26] Có ích / vô ích CHỈ từ cờ mã WTS tiêu chuẩn (code không tự gán ở đâu cả); không mã / mã không có trong
        //   danh mục / Gia công chưa chọn "Mã dùng cho Gia công" → "Chưa có mã" (phần riêng trong %, không có ích, không vô ích). Cờ đọc LÚC XEM
        //   (khách đổi cờ → % kỳ cũ đổi theo). Phút phát sinh (WtsExtraItems) nằm TRONG giờ dòng → tách theo mã phát sinh, trừ khỏi phần của dòng.
        //   Nguồn: HÙNG 26/11 22:09 ("ép dùng chỉ 1 nơi WTS tiêu chuẩn… khách tự đặt ra ntn thì ra như thế") + 22:21 + 22:34 + 23:00 + 23:08 "Ok làm đi".
        var gcTask = allWtsTasks.Where(t => t.IsGcTask).OrderByDescending(t => t.IsActive).ThenByDescending(t => t.TaskId).FirstOrDefault();
        var prodIds = prodLogs.Select(x => x.WtsLogId).ToList();
        var actIds  = actLogs.Select(x => x.ActivityLogId).ToList();
        var extras = new List<WtsExtraItemLog>();
        foreach (var ch in prodIds.Chunk(1000)) { var c = ch.ToList(); extras.AddRange(await _db.WtsExtraItems.AsNoTracking().Where(e => e.WtsLogId != null && c.Contains(e.WtsLogId.Value) && e.Minutes > 0).ToListAsync()); }
        foreach (var ch in actIds.Chunk(1000)) { var c = ch.ToList(); extras.AddRange(await _db.WtsExtraItems.AsNoTracking().Where(e => e.ActivityLogId != null && c.Contains(e.ActivityLogId.Value) && e.Minutes > 0).ToListAsync()); }
        var exW = extras.Where(e => e.WtsLogId != null).GroupBy(e => e.WtsLogId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).ToList());
        var exA = extras.Where(e => e.ActivityLogId != null).GroupBy(e => e.ActivityLogId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).ToList());
        var bkParent = await BackupParentsAsync(prodLogs);
        WtsTaskKind KindOfCode(string? code)
        {
            var c = (code ?? "").Trim();
            return c.Length > 0 && wtsLookup.TryGetValue(c, out var t) ? (t.IsProductiveTask ? WtsTaskKind.CoIch : WtsTaskKind.VoIch) : WtsTaskKind.ChuaMa;
        }
        WtsTaskKind KindOfProd(WtsProductionLog p)
        {
            var pg = (p.ProcessGroup ?? "").ToUpperInvariant();
            if (pg == "GC") return gcTask == null ? WtsTaskKind.ChuaMa : (gcTask.IsProductiveTask ? WtsTaskKind.CoIch : WtsTaskKind.VoIch);
            var c = (p.WtsCode ?? "").Trim();
            if (c.Length == 0) c = (p.NC ?? "").Trim();   // dòng HTSP / KCS / Đóng gói cũ chở mã CV ở cột NC (như SetTask)
            var k = KindOfCode(c);
            // mã dòng dự phòng (vd 3V1-DP-01 — không có trong WTS tiêu chuẩn) → cờ của mã cha theo quy trình PO đã chụp
            if (k == WtsTaskKind.ChuaMa && c.Length > 0 && bkParent.TryGetValue((p.KhPlanDetailId, pg, c.ToUpperInvariant()), out var par)) k = KindOfCode(par);
            return k;
        }

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
                Kind            = KindOfProd(p),   // [WTS-1NGUON] trước: production logs luôn có ích
                RowType         = WtsRowType.Production,
                Extras          = exW.GetValueOrDefault(p.WtsLogId) ?? new(),   // [WTS-1NGUON]
                WtsLogId        = p.WtsLogId, ProcessGroup = p.ProcessGroup ?? "", ProductionMode = p.ProductionMode, CaId = p.CaId,   // [G29-RPT]
            });
        }

        foreach (var a in actLogs)
        {
            var aBrk = WorkMinutesStore.Get(brkA, a.ActivityLogId);
            var am = WorkMinutes.Line(a.StartTime, a.EndTime, aBrk);   // [ML17-XN]
            var kindA = KindOfCode(a.WtsCode);   // [WTS-1NGUON] mã trống / không tra được → Chưa có mã (trước: có ích)

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
                Kind            = kindA,
                RowType         = WtsRowType.Activity,
                Extras          = exA.GetValueOrDefault(a.ActivityLogId) ?? new(),   // [WTS-1NGUON] dòng việc cũ (Loại 2) có phát sinh
                ActivityLogId   = a.ActivityLogId,   // [G29-RPT]
            });
        }

        rows = rows.OrderBy(r => r.WorkDate).ThenBy(r => r.WorkerName).ThenBy(r => r.StartTime).ToList();

        // ── 5. Summary cards ──────────────────────────────────────────────
        // [ML17-XN 2026-11-01] Câu (b) Hùng chốt: % hiệu suất theo PHÚT LÀM (trước: phút thô, khác cột Phút trang WTS).
        //   Gộp theo người-ngày như "Tổng hôm nay" của trang WTS (WorkMinutes.Day): giờ chồng nhau (GC đứng 2 máy) chỉ tính 1 lần
        //   (trước: cộng 2 lần). Phút có ích = phút làm phủ bởi dòng có ích (phút có cả dòng có ích lẫn vô ích phủ → có ích).
        int totalMin = 0, rawMin = 0, workedMin = 0;
        foreach (var g in rows.GroupBy(r => (r.WorkerId, r.WorkDate)))
        {
            var all = WorkMinutes.Day(g.Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            totalMin += all.Work; rawMin += all.Raw; workedMin += all.Worked;
        }
        // [WTS-1NGUON] có ích / vô ích / chưa có mã tổng = Σ người-ngày (sau khi tách phát sinh) — xem BuildDaysAsync

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
        foreach (var r in rows) SetExtraParts(r, taskNames, KindOfCode);   // [WTS-1NGUON]
        // [G31-LOC-NHOM 2026-11-22] nhóm của công nhân (Users.GroupId → UserGroups) — ô Nhóm WTS Report lọc theo NGƯỜI, giữ giờ chuẩn.
        //   Nguồn: HÙNG 22/11 08:29 ("hiệu suất công nhân có tìm theo nhóm được ko") + 11:58 "code luôn"; CLAUDE đề xuất ô Nhóm.
        var wIds = rows.Select(r => r.WorkerId).Distinct().ToList();
        var wGrp = wIds.Count == 0 ? new Dictionary<int, int?>()
            : (await _db.Users.AsNoTracking().Where(u => wIds.Contains(u.UserId)).Select(u => new { u.UserId, u.GroupId }).ToListAsync()).ToDictionary(x => x.UserId, x => x.GroupId);
        var gIds = wGrp.Values.Where(g => g != null).Select(g => g!.Value).Distinct().ToList();
        var gName = gIds.Count == 0 ? new Dictionary<int, string>()
            : (await _db.UserGroups.AsNoTracking().Where(g => gIds.Contains(g.GroupId)).Select(g => new { g.GroupId, g.GroupName }).ToListAsync()).ToDictionary(x => x.GroupId, x => x.GroupName);
        foreach (var r in rows)
        {
            r.WorkerGroupId = wGrp.TryGetValue(r.WorkerId, out var gid) ? gid : null;
            r.WorkerGroupName = r.WorkerGroupId != null && gName.TryGetValue(r.WorkerGroupId.Value, out var gn) ? gn : "(chưa có nhóm)";
        }
        var days = await BuildDaysAsync(rows, await LoadCoverAfterAsync(dateTo, endDt, workerId));

        return new WtsReportResult
        {
            Rows          = rows,
            TotalMinutes  = totalMin,
            ProductiveMin = days.Sum(d => d.ProductiveMin),   // [WTS-1NGUON] = Σ phần có ích (dòng + phát sinh)
            WasteMin      = days.Sum(d => d.WasteMin),
            NoCodeMin     = days.Sum(d => d.NoCodeMin),
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
            AllocateParts(list, ivs);   // [WTS-1NGUON] chia phút theo PHẦN (phần dòng + từng mục phát sinh), ưu tiên có ích > vô ích > chưa có mã
            var all = WorkMinutes.Day(list.Select(r => new DayLine(r.StartTime, r.EndTime, r.Breaks)));
            var parts = list.SelectMany(r => r.Parts).ToList();
            var d = new WtsReportDay
            {
                WorkerId = g.Key.WorkerId, WorkerName = list[0].WorkerName, Day = g.Key.WorkDate,
                WorkMin = all.Work,
                ProductiveMin = parts.Where(x => x.Kind == WtsTaskKind.CoIch).Sum(x => x.Min),   // [WTS-1NGUON]
                WasteMin      = parts.Where(x => x.Kind == WtsTaskKind.VoIch).Sum(x => x.Min),
                NoCodeMin     = parts.Where(x => x.Kind == WtsTaskKind.ChuaMa).Sum(x => x.Min),
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

    // ── [WTS-1NGUON 2026-11-26] Chia phút người-ngày theo PHẦN ─────────────────────────────────────────────────
    //   Mỗi dòng = phần việc của dòng + từng mục phát sinh (phút phát sinh nằm TRONG giờ dòng, không biết vị trí). Sức chứa: phát sinh lấy dần theo
    //   thứ tự mục (không vượt phút làm của dòng), phần dòng = phút làm của dòng − phát sinh. Mỗi khoảng giờ (giữa 2 mốc) giao cho các phần của
    //   các dòng phủ khoảng đó; ưu tiên có ích > vô ích > chưa có mã: lượt k chỉ thêm phút cho phần loại k, không bớt phút loại trước (luồng cực đại
    //   từng lượt: rải đều cho các phần cùng loại — đứng 2 máy chia đều như cũ — rồi tìm đường tăng luồng). Σ phần dòng = AllocMin, Σ ngày = phút làm.
    //   Rà độc lập 26/11 (T1): bản đầu chia theo loại CẢ DÒNG rồi mới tách phát sinh → phát sinh có ích trên dòng chưa có mã bị dòng vô ích chồng
    //   giờ "ăn mất", phát sinh vô ích trên dòng đứng 2 máy lấn phút máy kia vẫn có ích.
    //   Nguồn: CLAUDE (chi tiết số bên trong, không đổi giao diện / in-out — preferences Hùng 23/11 19:05); HÙNG 26/11 23:08 "Ok làm đi" (luật giờ trùng).
    const double Eps = 1e-7;
    static void AllocateParts(List<WtsReportRow> list, List<List<(DateTime S, DateTime E)>> ivs)
    {
        // phần + sức chứa
        var parts = new List<(WtsReportRow R, WtsReportPart P, double Cap)>();
        var rowParts = new List<List<int>>();
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var own = (int)Math.Round(Measure(ivs[i]));
            var mine = new List<int>();
            int left = own;
            var ex = new List<(WtsReportPart P, int Cap)>();
            foreach (var x in r.ExtraParts) { var c = Math.Max(0, Math.Min(x.ReqMin, left)); left -= c; ex.Add((x, c)); }
            var main = new WtsReportPart { Kind = r.Kind, IsExtra = false, GroupLabel = r.GroupLabel, TaskKey = r.TaskKey, TaskCode = r.TaskCode, TaskLabel = r.TaskLabel };
            mine.Add(parts.Count); parts.Add((r, main, left));
            foreach (var e in ex) { mine.Add(parts.Count); parts.Add((r, e.P, e.Cap)); }
            rowParts.Add(mine);
        }
        // khoảng giờ + dòng phủ
        var cuts = ivs.SelectMany(x => x).SelectMany(x => new[] { x.S, x.E }).Distinct().OrderBy(x => x).ToList();
        var segLen = new List<double>(); var segParts = new List<List<int>>();
        for (int k = 0; k + 1 < cuts.Count; k++)
        {
            var a = cuts[k]; var b = cuts[k + 1];
            var len = (b - a).TotalMinutes;
            if (len <= 0) continue;
            var ps = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (ivs[i].Any(x => x.S <= a && x.E >= b)) ps.AddRange(rowParts[i]);
            if (ps.Count == 0) continue;
            segLen.Add(len); segParts.Add(ps);
        }
        int nS = segLen.Count, nP = parts.Count;
        var f = new Dictionary<(int S, int P), double>();
        var rem = segLen.ToArray();
        var capLeft = parts.Select(x => x.Cap).ToArray();
        double F(int sI, int pI) => f.TryGetValue((sI, pI), out var v) ? v : 0;
        void Add(int sI, int pI, double v) { f[(sI, pI)] = F(sI, pI) + v; rem[sI] -= v; capLeft[pI] -= v; }
        for (int kind = 0; kind <= 2; kind++)
        {
            bool Elig(int pI) => (int)parts[pI].P.Kind <= kind;
            // (1) rải đều từng khoảng cho các phần loại này còn chỗ (đứng 2 máy chia đều)
            for (int sI = 0; sI < nS; sI++)
            {
                var ps = segParts[sI].Where(pI => (int)parts[pI].P.Kind == kind && capLeft[pI] > Eps).ToList();
                while (rem[sI] > Eps && ps.Count > 0)
                {
                    var share = rem[sI] / ps.Count;
                    var give = Math.Min(share, ps.Min(pI => capLeft[pI]));
                    foreach (var pI in ps) Add(sI, pI, give);
                    ps = ps.Where(pI => capLeft[pI] > Eps).ToList();
                }
            }
            // (2) đường tăng luồng: khoảng còn phút → phần → (đẩy phần đó sang khoảng khác) → … → phần loại ≤ k còn chỗ
            while (true)
            {
                var prevS = new int[nS]; var prevP = new int[nP]; Array.Fill(prevS, -2); Array.Fill(prevP, -2);
                var q = new Queue<(bool IsSeg, int I)>();
                for (int sI = 0; sI < nS; sI++) if (rem[sI] > Eps) { prevS[sI] = -1; q.Enqueue((true, sI)); }
                int end = -1;
                while (q.Count > 0 && end < 0)
                {
                    var (isSeg, idx) = q.Dequeue();
                    if (isSeg)
                    {
                        foreach (var pI in segParts[idx])
                        {
                            if (prevP[pI] != -2 || !Elig(pI)) continue;
                            prevP[pI] = idx;
                            if (capLeft[pI] > Eps) { end = pI; break; }
                            q.Enqueue((false, pI));
                        }
                    }
                    else
                    {
                        for (int sI = 0; sI < nS; sI++)
                            if (prevS[sI] == -2 && F(sI, idx) > Eps && segParts[sI].Contains(idx)) { prevS[sI] = idx; q.Enqueue((true, sI)); }
                    }
                }
                if (end < 0) break;
                // nút cổ chai
                double bott = capLeft[end]; int pp = end;
                while (true) { int sI = prevP[pp]; if (prevS[sI] == -1) { bott = Math.Min(bott, rem[sI]); break; } int pPrev = prevS[sI]; bott = Math.Min(bott, F(sI, pPrev)); pp = pPrev; }
                pp = end;
                while (true)
                {
                    int sI = prevP[pp]; f[(sI, pp)] = F(sI, pp) + bott;
                    if (prevS[sI] == -1) { rem[sI] -= bott; break; }
                    int pPrev = prevS[sI]; f[(sI, pPrev)] = F(sI, pPrev) - bott; pp = pPrev;
                }
                capLeft[end] -= bott;
                if (bott <= Eps) break;
            }
        }
        // làm tròn theo loại (phần dư lớn nhất) → Σ đúng phút làm người-ngày
        var share0 = new double[nP];
        foreach (var kv in f) share0[kv.Key.P] += kv.Value;
        double T(int k) => Enumerable.Range(0, nP).Where(pI => (int)parts[pI].P.Kind == k).Sum(pI => share0[pI]);
        DayLine Ln(WtsReportRow r) => new(r.StartTime, r.EndTime, r.Breaks);
        var allWork = WorkMinutes.Day(list.Select(Ln)).Work;
        var pT = Math.Min(allWork, (int)Math.Round(T(0), MidpointRounding.AwayFromZero));
        var cT = Math.Min(allWork, Math.Max(pT, (int)Math.Round(T(0) + T(1), MidpointRounding.AwayFromZero)));
        var mins = new int[nP];
        RoundIdx(share0, mins, Enumerable.Range(0, nP).Where(pI => parts[pI].P.Kind == WtsTaskKind.CoIch).ToList(), pT);
        RoundIdx(share0, mins, Enumerable.Range(0, nP).Where(pI => parts[pI].P.Kind == WtsTaskKind.VoIch).ToList(), cT - pT);
        RoundIdx(share0, mins, Enumerable.Range(0, nP).Where(pI => parts[pI].P.Kind == WtsTaskKind.ChuaMa).ToList(), allWork - cT);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            foreach (var pI in rowParts[i]) { parts[pI].P.Min = mins[pI]; if (!parts[pI].P.IsExtra) parts[pI].P.ReqMin = mins[pI]; }
            r.Parts = rowParts[i].Where(pI => !parts[pI].P.IsExtra || mins[pI] > 0).Select(pI => parts[pI].P).ToList();
            r.AllocMin = rowParts[i].Sum(pI => mins[pI]);
        }
    }

    /// <summary>Làm tròn phần dư lớn nhất: Σ phút các phần chọn = đúng target.</summary>
    static void RoundIdx(double[] share, int[] outMin, List<int> idx, int target)
    {
        if (idx.Count == 0) return;
        foreach (var i in idx) outMin[i] = (int)Math.Floor(share[i] + 1e-6);
        var left = target - idx.Sum(i => outMin[i]);
        var order = idx.OrderByDescending(i => share[i] - Math.Floor(share[i] + 1e-6)).ThenByDescending(i => share[i]).ThenBy(i => i).ToList();
        for (int k = 0; left > 0 && order.Count > 0; k++, left--) outMin[order[k % order.Count]]++;
        for (int k = 0; left < 0 && order.Count > 0; k++)
        {
            var i = order[order.Count - 1 - (k % order.Count)];
            if (outMin[i] > 0) { outMin[i]--; left++; }
            if (k > 10 * order.Count) break;
        }
    }

    // ── [WTS-1NGUON 2026-11-26] Phát sinh tách theo mã ────────────────────────────────────────────────────────
    //   Mục phát sinh của dòng (WtsExtraItems, phút > 0); dòng cũ có T.PS mà không có mục chi tiết → 1 mục "phát sinh không mã" (rà độc lập L4).
    static void SetExtraParts(WtsReportRow r, Dictionary<string, string> taskNames, Func<string?, WtsTaskKind> kindOf)
    {
        r.ExtraParts = r.Extras.Select(e =>
        {
            var c = (e.WtsCode ?? "").Trim();
            var lbl = c.Length > 0 && taskNames.TryGetValue(c, out var tn) ? tn : (e.WtsName ?? "").Trim();
            return new WtsReportPart
            {
                Kind = kindOf(c), IsExtra = true, ReqMin = e.Minutes, GroupLabel = "Phát sinh",
                TaskKey = c.Length > 0 ? "ACT|" + c.ToUpperInvariant() : "PS|#", TaskCode = c, TaskLabel = c.Length > 0 ? lbl : "(phát sinh không mã)",
            };
        }).ToList();
        if (r.ExtraParts.Count == 0 && (r.ExtraMinutes ?? 0) > 0)
            r.ExtraParts.Add(new WtsReportPart { Kind = WtsTaskKind.ChuaMa, IsExtra = true, ReqMin = r.ExtraMinutes!.Value, GroupLabel = "Phát sinh", TaskKey = "PS|#", TaskCode = "", TaskLabel = "(phát sinh không mã)" });
    }

    /// <summary>[WTS-1NGUON] Mã dòng dự phòng → mã cha theo quy trình PO đã chụp (HTSP / KCS / Đóng gói). Khoá (PO, nhóm WTS, MÃ HOA).</summary>
    async Task<Dictionary<(int, string, string), string>> BackupParentsAsync(List<WtsProductionLog> prod)
    {
        var res = new Dictionary<(int, string, string), string>();
        var need = prod.Where(p => !string.Equals(p.ProcessGroup, "GC", StringComparison.OrdinalIgnoreCase)).Select(p => p.KhPlanDetailId).Distinct().ToList();
        if (need.Count == 0) return res;
        void Add(IEnumerable<(int Det, string Nc, string? Par)> src, params string[] groups)
        {
            foreach (var x in src)
                if (!string.IsNullOrWhiteSpace(x.Par) && !string.IsNullOrWhiteSpace(x.Nc))
                    foreach (var g in groups) res.TryAdd((x.Det, g, x.Nc.Trim().ToUpperInvariant()), x.Par.Trim());
        }
        foreach (var ch in need.Chunk(1000))
        {
            var c = ch.ToList();
            Add((await _db.KhPlanRouteSnapshotBavia.AsNoTracking().Where(s => c.Contains(s.KhPlanDetailId) && s.IsBackup).Select(s => new { s.KhPlanDetailId, s.NC, s.ParentNC }).ToListAsync()).Select(s => (s.KhPlanDetailId, s.NC, s.ParentNC)), "BAVIA");
            Add((await _db.KhPlanRouteSnapshotWashing.AsNoTracking().Where(s => c.Contains(s.KhPlanDetailId) && s.IsBackup).Select(s => new { s.KhPlanDetailId, s.NC, s.ParentNC }).ToListAsync()).Select(s => (s.KhPlanDetailId, s.NC, s.ParentNC)), "WASHING");
            Add((await _db.KhPlanRouteSnapshotInspection.AsNoTracking().Where(s => c.Contains(s.KhPlanDetailId) && s.IsBackup).Select(s => new { s.KhPlanDetailId, s.NC, s.ParentNC }).ToListAsync()).Select(s => (s.KhPlanDetailId, s.NC, s.ParentNC)), "KCS");
            Add((await _db.KhPlanRouteSnapshotPackaging.AsNoTracking().Where(s => c.Contains(s.KhPlanDetailId) && s.IsBackup).Select(s => new { s.KhPlanDetailId, s.NC, s.ParentNC }).ToListAsync()).Select(s => (s.KhPlanDetailId, s.NC, s.ParentNC)), "PKG");
            // rà độc lập 26/11 L3: dòng dự phòng Taro CŨ (Taro không còn thêm dự phòng) — mã sổ = Mã CV (trống thì NC); cha trỏ theo NC dòng chính → mã của dòng chính đó
            var tr = await _db.KhPlanRouteSnapshotTaro.AsNoTracking().Where(s => c.Contains(s.KhPlanDetailId)).Select(s => new { s.KhPlanDetailId, s.NC, s.WtsTaskCode, s.IsBackup, s.ParentNC }).ToListAsync();
            string TCode(string? w, string? nc) => string.IsNullOrWhiteSpace(w) ? (nc ?? "") : w;
            Add(tr.Where(s => s.IsBackup).Select(s => (s.KhPlanDetailId, TCode(s.WtsTaskCode, s.NC),
                (string?)tr.Where(m => !m.IsBackup && m.KhPlanDetailId == s.KhPlanDetailId && string.Equals((m.NC ?? "").Trim(), (s.ParentNC ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(m => TCode(m.WtsTaskCode, m.NC)).FirstOrDefault())), "TARO");
        }
        return res;
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

/// <summary>[WTS-1NGUON 2026-11-26] Thứ tự = ưu tiên khi giờ trùng (số nhỏ tính trước).</summary>
public enum WtsTaskKind { CoIch = 0, VoIch = 1, ChuaMa = 2 }

/// <summary>[WTS-1NGUON] 1 phần phút của dòng: phần việc của dòng, hoặc 1 mục phát sinh. Σ Min các phần = AllocMin của dòng.</summary>
public class WtsReportPart
{
    public WtsTaskKind Kind     { get; set; }
    public bool      IsExtra    { get; set; }
    public int       Min        { get; set; }
    /// <summary>Phút phát sinh đã khai (mục phát sinh); phần dòng = Min.</summary>
    public int       ReqMin     { get; set; }
    public string    GroupLabel { get; set; } = "";
    public string    TaskKey    { get; set; } = "";
    public string    TaskCode   { get; set; } = "";
    public string    TaskLabel  { get; set; } = "";
}

public class WtsReportRow
{
    public DateOnly  WorkDate         { get; set; }
    public int       WorkerId         { get; set; }
    public string    WorkerName       { get; set; } = "";
    public int?      WorkerGroupId    { get; set; }   // [G31-LOC-NHOM] nhóm của công nhân (Users.GroupId)
    public string    WorkerGroupName  { get; set; } = "";
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
    /// <summary>[WTS-1NGUON] Loại của dòng theo cờ mã WTS tiêu chuẩn (Gia công: "Mã dùng cho Gia công"). Phần phát sinh có loại riêng (Parts).</summary>
    public WtsTaskKind Kind           { get; set; } = WtsTaskKind.ChuaMa;
    public bool      IsProductiveTask => Kind == WtsTaskKind.CoIch;
    /// <summary>[WTS-1NGUON] Mục phát sinh của dòng (WtsExtraItems, phút > 0) + phần đã tách (ExtraParts) + mọi phần sau chia (Parts: phần dòng đầu tiên).</summary>
    public List<WtsExtraItemLog> Extras { get; set; } = new();
    public List<WtsReportPart> ExtraParts { get; set; } = new();
    public List<WtsReportPart> Parts  { get; set; } = new();
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
    /// <summary>[WTS-1NGUON] Phút "Chưa có mã" (dòng / phát sinh không mã, mã không có trong danh mục, Gia công chưa chọn mã). WorkMin = có ích + vô ích + chưa có mã.</summary>
    public int       NoCodeMin      { get; set; }
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
    /// <summary>[WTS-1NGUON] Phút chưa có mã — TotalMinutes = có ích + vô ích + chưa có mã.</summary>
    public int                NoCodeMin     { get; set; }
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
