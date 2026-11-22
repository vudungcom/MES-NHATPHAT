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
        CaLamViec? selectedCa = null, int viewerUserId = 0)
    {
        // [HSM-QUYEN 2026-11-16] Máy chủ kiểm quyền Xem HIEU_SUAT_MAY (ma trận DB, không tin claim) — trước: ai đăng nhập cũng lấy được
        //   WTS mọi máy. Không quyền → kết quả rỗng (trang đã chặn + ẩn menu; đây là hàng rào thứ hai, như WtsReportService [RPT-4]).
        //   viewerUserId = 0 chỉ còn ở bộ kiểm cũ → coi như không quyền. Nguồn: CLAUDE quét Fable 15/11 (BM1-09); Hùng 15/11 15:10 OK câu 9.
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.HieuSuatMay))
            return EmptyResult(date, fromTime, toTime);
        // [G29-HSM-KY 2026-11-21] Tách phần ĐỌC DB (LoadAsync, theo khoảng ngày) khỏi phần TÍNH 1 ngày (ComputeDay) để báo cáo tuần / tháng
        //   (GetPeriodAsync) dùng đúng cùng cách tính với trang ngày. Ngày lẻ: đọc đúng khung 1 ngày như trước → số không đổi.
        var data = await LoadAsync(date, date, fromTime, toTime);
        if (data == null) return EmptyResult(date, fromTime, toTime);
        return ComputeDay(data, date, fromTime, toTime, selectedCa);
    }

    /// <summary>[G29-HSM-KY] Dữ liệu đọc 1 lần cho cả khoảng ngày.</summary>
    sealed class LoadedData
    {
        public List<ThietBi> Machines = new();
        public List<WtsProductionLog> Wts = new();
        public Dictionary<long, List<LineBreak>> Brk = new();
        public List<ThietBiChangeLog> Down = new();
        public Dictionary<int, string> Reporter = new();
        /// <summary>[G31-HSM-24H] Hợp phút "xưởng có người làm" (mọi dòng WTS sản xuất + việc không ra SP của mọi người, đã trừ nghỉ thật của dòng);
        ///   null = không chia phần Rảnh (khung ca cũ).</summary>
        public List<(DateTime S, DateTime E)>? Crew;
        /// <summary>[G31-HSM-24H] dòng WTS theo từng ngày lịch dòng chạm (chỉ khi 24h) — không quét cả năm mỗi ngày.</summary>
        public Dictionary<DateOnly, List<WtsProductionLog>>? WtsByDay;
    }

    async Task<LoadedData?> LoadAsync(DateOnly fromDate, DateOnly toDate, TimeOnly fromTime, TimeOnly toTime, bool full24 = false)
    {
        var dayStart = fromDate.ToDateTime(fromTime);
        // [RPT-3 2026-10-30] Ca vắt đêm (vd 20:00–05:00): giờ kết thúc sang ngày hôm sau — trước: chỉ queryEnd được nới,
        //   dayEnd vẫn cùng ngày → tổng phút âm, mọi đoạn WTS bị cắt hết, hiệu suất 0 %.
        var dayEnd   = toTime < fromTime ? toDate.AddDays(1).ToDateTime(toTime) : toDate.ToDateTime(toTime);
        if (full24) { dayStart = fromDate.ToDateTime(TimeOnly.MinValue); dayEnd = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue); }   // [G31-HSM-24H]

        // ── 1. Danh sách máy active ─────────────────────────────────────
        var machines = await _db.ThietBis
            .Where(t => t.IsActive)
            .OrderBy(t => t.BoPhan).ThenBy(t => t.SoMay)
            .AsNoTracking().ToListAsync();

        if (!machines.Any()) return null;

        var allSoMay      = machines.Select(m => m.SoMay).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allMachineIds = machines.Select(m => m.ThietBiId).ToList();

        // ── 2. WTS logs (tận dụng filtered index MachineUsed+StartTime+EndTime) ──
        // Ca vắt đêm: khung đã nới sang ngày hôm sau ở dayEnd (queryEnd = dayEnd)
        var queryEnd = dayEnd;

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

        // [G29-HSM-KY] người báo (bảng Các lần dừng): tra tên riêng — không Include (ChangedBy int bắt buộc → INNER JOIN làm mất lần dừng
        //   có người báo không còn trong Users, ảnh hưởng cả trang ngày; rà độc lập 21/11 L5)
        var byIds = downLogs.Select(x => x.ChangedBy).Distinct().ToList();
        var names = byIds.Count == 0 ? new Dictionary<int, string>()
            : (await _db.Users.AsNoTracking().Where(u => byIds.Contains(u.UserId)).Select(u => new { u.UserId, u.FullName }).ToListAsync()).ToDictionary(x => x.UserId, x => x.FullName);
        var res = new LoadedData { Machines = machines, Wts = filteredWts, Brk = brkW, Down = downLogs, Reporter = names };
        if (full24)
        {
            res.Crew = await LoadCrewAsync(dayStart, dayEnd);
            res.WtsByDay = new();
            foreach (var w in filteredWts)
                for (var d = DateOnly.FromDateTime(w.StartTime); d.ToDateTime(TimeOnly.MinValue) < w.EndTime; d = d.AddDays(1))
                {
                    if (!res.WtsByDay.TryGetValue(d, out var l)) res.WtsByDay[d] = l = new();
                    l.Add(w);
                }
        }
        return res;
    }

    /// <summary>[G31-HSM-24H] Phút xưởng có người làm trong [s, e): hợp đoạn làm (WorkMinutes.WorkPieces — giờ nghỉ đã lưu của dòng bị bỏ ra,
    ///   khúc làm thông nghỉ giữ) của MỌI dòng WTS sản xuất (cả dòng không ghi máy) + việc không ra SP, mọi người, chưa huỷ.</summary>
    async Task<List<(DateTime S, DateTime E)>> LoadCrewAsync(DateTime s, DateTime e)
    {
        var p = await _db.WtsProductionLogs.Where(x => !x.IsVoided && x.StartTime < e && x.EndTime > s)
            .AsNoTracking().Select(x => new { x.WtsLogId, x.StartTime, x.EndTime }).ToListAsync();
        var a = await _db.WorkerActivityLogs.Where(x => !x.IsVoided && x.StartTime < e && x.EndTime > s)
            .AsNoTracking().Select(x => new { x.ActivityLogId, x.StartTime, x.EndTime }).ToListAsync();
        var bw = await WorkMinutesStore.ForWtsAsync(_db, p.Select(x => x.WtsLogId));
        var ba = await WorkMinutesStore.ForActivityAsync(_db, a.Select(x => x.ActivityLogId));
        var iv = p.SelectMany(x => WorkMinutes.WorkPieces(x.StartTime, x.EndTime, WorkMinutesStore.Get(bw, x.WtsLogId)))
            .Concat(a.SelectMany(x => WorkMinutes.WorkPieces(x.StartTime, x.EndTime, WorkMinutesStore.Get(ba, x.ActivityLogId))))
            .Select(x => (x.S < s ? s : x.S, x.E > e ? e : x.E)).Where(x => x.Item2 > x.Item1);
        return UnionIv(iv);
    }

    static List<(DateTime S, DateTime E)> UnionIv(IEnumerable<(DateTime S, DateTime E)> iv)
    {
        var res = new List<(DateTime S, DateTime E)>();
        foreach (var x in iv.Where(x => x.E > x.S).OrderBy(x => x.S))
        {
            if (res.Count > 0 && x.S <= res[^1].E) { if (x.E > res[^1].E) res[^1] = (res[^1].S, x.E); }
            else res.Add(x);
        }
        return res;
    }

    /// <summary>[G31-HSM-24H] Cắt 1 đoạn Rảnh theo phút xưởng có người: phần có người giữ Idle, phần không ai khai WTS → NoCrew.</summary>
    static IEnumerable<TimelineSegment> SplitIdle(TimelineSegment seg, List<(DateTime S, DateTime E)> crew)
    {
        var pos = seg.Start;
        int lo = 0, hi = crew.Count;   // tìm đoạn đầu tiên có E > seg.Start (crew đã hợp + sắp xếp)
        while (lo < hi) { var mid = (lo + hi) / 2; if (crew[mid].E <= seg.Start) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < crew.Count && crew[i].S < seg.End; i++)
        {
            var cs = crew[i].S < seg.Start ? seg.Start : crew[i].S;
            var ce = crew[i].E > seg.End ? seg.End : crew[i].E;
            if (cs > pos) yield return seg with { Start = pos, End = cs, Type = SegmentType.NoCrew };
            if (ce > cs) yield return seg with { Start = cs, End = ce, Type = SegmentType.Idle };
            pos = ce > pos ? ce : pos;
        }
        if (pos < seg.End) yield return seg with { Start = pos, End = seg.End, Type = SegmentType.NoCrew };
    }

    /// <summary>[G29-HSM-KY] Tính 1 ngày từ dữ liệu đã đọc (giữ nguyên cách tính cũ của GetAsync; lọc lại đúng khung ngày đó).</summary>
    MachineEfficiencyResult ComputeDay(LoadedData data, DateOnly date, TimeOnly fromTime, TimeOnly toTime, CaLamViec? selectedCa, bool full24 = false)
    {
        var dayStart = date.ToDateTime(fromTime);
        var dayEnd   = toTime < fromTime ? date.AddDays(1).ToDateTime(toTime) : date.ToDateTime(toTime);
        if (full24)
        {   // [G31-HSM-24H] 00:00–24:00, không ca; rà độc lập 22/11 (Cao 1): KHÔNG tính thời gian chưa tới — hôm nay cắt ở giờ hiện tại
            //   (trước: phần còn lại của hôm nay / ngày sau thành "không ai khai WTS", lần hỏng chưa đóng cộng dừng cả ngày tương lai)
            dayStart = date.ToDateTime(TimeOnly.MinValue); dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue); selectedCa = null;
            var now = Clock(); if (dayEnd > now) dayEnd = now < dayStart ? dayStart : now;
        }
        var machines = data.Machines;
        var filteredWts = (data.WtsByDay != null ? data.WtsByDay.GetValueOrDefault(date) ?? new() : data.Wts).Where(x => x.StartTime < dayEnd && x.EndTime > dayStart).ToList();   // [G31-HSM-24H] chỉ mục theo ngày
        var brkW = data.Brk;
        var downLogs = data.Down.Where(x => x.ThoiGianBatDau < dayEnd && (x.ThoiGianKetThuc == null || x.ThoiGianKetThuc > dayStart)).ToList();

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
                    DownLogId = x.ChangeId,   // [G29-HSM-KY]
                };
            }).Where(s => s.End > s.Start).ToList();

            var segments = BuildTimeline(dayStart, dayEnd, wtsSegs, downSegs, machineBreaks);   // [ML17-XN]
            if (data.Crew != null)   // [G31-HSM-24H] Rảnh → lúc xưởng có người làm (Idle) / xưởng không ai khai WTS (NoCrew)
                segments = segments.SelectMany(s => s.Type == SegmentType.Idle ? SplitIdle(s, data.Crew) : new[] { s }).ToList();

            var runMin   = segments.Where(s => s.Type == SegmentType.Running).Sum(s => s.Minutes);
            var downMin  = segments.Where(s => s.Type is SegmentType.Breakdown or SegmentType.Maintenance).Sum(s => s.Minutes);
            var breakMin = segments.Where(s => s.Type == SegmentType.Break).Sum(s => s.Minutes);
            var totalMin = (dayEnd - dayStart).TotalMinutes;
            var idleMin  = Math.Max(0, totalMin - runMin - downMin - breakMin);
            var noCrewMin = Math.Min(idleMin, segments.Where(s => s.Type == SegmentType.NoCrew).Sum(s => s.Minutes));   // [G31-HSM-24H]

            rows.Add(new MachineRow
            {
                LogMinutes   = wtsForMachine.ToDictionary(x => x.WtsLogId, x => WorkMinutes.Line(x.StartTime, x.EndTime, WorkMinutesStore.Get(brkW, x.WtsLogId))),   // [ML17-XN]
                Machine      = m,
                Segments     = segments,
                RawRunSegments = wtsSegs,   // [G29-HSM-KY] đoạn làm của từng dòng (chưa cắt dừng / gộp người) — phút đứng máy từng người
                WtsDetails   = wtsForMachine,
                TotalMinutes = totalMin,
                RunMinutes   = runMin,
                DownMinutes  = downMin,
                BreakMinutes = breakMin,
                IdleMinutes  = idleMin,
                NoCrewMinutes = noCrewMin,
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
            NoCrewMinutes = rows.Sum(r => r.NoCrewMinutes),
            Full24       = full24,
        };
    }

    // ═══ [G31-HSM-24H 2026-11-22] Máy tính theo 24h / ngày, KHÔNG chia ca (Hùng 22/11 09:03 + 11:58 "code luôn").
    //   Nguồn: HÙNG 22/11 08:58 (máy tính theo 24h) + 09:03 (không chia ca, chia rõ thời gian khác) + 11:58; CLAUDE đề xuất 4 phần.
    //   1 ngày = 00:00–24:00 = Chạy (có WTS trên máy) + Dừng (hỏng / bảo trì đã báo) + Rảnh lúc xưởng có người làm (có người khác đang có
    //   dòng WTS mà máy không chạy, không dừng) + Xưởng không ai khai WTS (đêm, ngày nghỉ, giờ nghỉ, quên nhập). % chạy chính = Chạy / 24h;
    //   phụ: % chạy lúc xưởng có người = Chạy / (24h − không ai khai WTS). Kỳ: cộng MỌI ngày lịch (≤ 366 ngày). Chỉ đọc.
    public async Task<MachineEfficiencyResult> GetDay24Async(DateOnly date, int viewerUserId)
    {
        var t0 = TimeOnly.MinValue;
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.HieuSuatMay)) return EmptyResult(date, t0, t0);   // [HSM-QUYEN]
        var data = await LoadAsync(date, date, t0, t0, full24: true);
        if (data == null) return EmptyResult(date, t0, t0);
        return ComputeDay(data, date, t0, t0, null, full24: true);
    }

    public const int PeriodMaxDays24 = 366;
    /// <summary>[G31-HSM-24H] giờ hiện tại (bộ kiểm thay được); ngày / giờ chưa tới không tính.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public async Task<MachinePeriodResult> GetPeriod24Async(DateOnly from, DateOnly to, int viewerUserId)
    {
        var t0 = TimeOnly.MinValue;
        var res = new MachinePeriodResult { From = from, To = to, FromTime = t0, ToTime = t0, Full24 = true };
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.HieuSuatMay)) return res;   // [HSM-QUYEN]
        if (to < from) { res.Error = "Ngày \"Từ\" phải trước hoặc bằng ngày \"Đến\"."; return res; }
        if (to.DayNumber - from.DayNumber + 1 > PeriodMaxDays24) { res.Error = $"Khoảng xem tối đa {PeriodMaxDays24} ngày."; return res; }
        var today = DateOnly.FromDateTime(Clock());
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).Where(d => d <= today).ToList();   // mọi ngày lịch tới hôm nay
        res.Days = days;
        if (days.Count == 0) return res;
        var data = await LoadAsync(from, days[^1], t0, t0, full24: true);
        if (data == null) return res;
        // OK / NG: dòng tính vào kỳ chứa NGÀY BẮT ĐẦU của dòng (rà độc lập 22/11 Trung 2: dòng 22:00 CN → 06:00 T2 không cộng ở cả 2 tuần)
        Accumulate(res, data, days, d => ComputeDay(data, d, t0, t0, null, full24: true), l => DateOnly.FromDateTime(l.StartTime) >= from && DateOnly.FromDateTime(l.StartTime) <= to);
        return res;
    }

    // ═══ [G29-HSM-KY 2026-11-21] Báo cáo tuần / tháng / tự chọn (mockup https://claude.ai/artifact/CeAoFry7m7H8wT5z6PZ1wp) ═══
    //   Nguồn: HÙNG 21/11 08:17 (báo cáo máy lấy theo WTS) + 09:46 OK mockup + câu B theo đề xuất CLAUDE.
    //   Mỗi ngày tính y trang ngày (ComputeDay, cùng khung giờ + ca đang chọn) rồi cộng. Ngày được tính = ngày trong kỳ xưởng có ít nhất
    //   1 dòng WTS (sản xuất hoặc việc không ra SP) — phần mềm chưa có lịch nghỉ / lễ (câu B2 Hùng OK 21/11). % trên phút kế hoạch
    //   (khung − nghỉ, câu B1). OK / NG: mỗi dòng WTS tính 1 lần, vào ngày đầu tiên dòng đó chạm khung. Chỉ đọc.
    public const int PeriodMaxDays = 93;

    public async Task<MachinePeriodResult> GetPeriodAsync(DateOnly from, DateOnly to, TimeOnly fromTime, TimeOnly toTime,
        CaLamViec? selectedCa, int viewerUserId)
    {
        var res = new MachinePeriodResult { From = from, To = to, FromTime = fromTime, ToTime = toTime };
        if (!await PermCheck.CanViewAsync(_db, viewerUserId, MES.Web.Constants.SystemPermissions.HieuSuatMay)) return res;   // [HSM-QUYEN]
        if (to < from) { res.Error = "Ngày \"Từ\" phải trước hoặc bằng ngày \"Đến\"."; return res; }
        if (to.DayNumber - from.DayNumber + 1 > PeriodMaxDays) { res.Error = $"Khoảng xem tối đa {PeriodMaxDays} ngày."; return res; }
        if (fromTime == toTime) { res.Error = "Khung giờ trống."; return res; }

        // ngày d được tính khi có dòng WTS / việc không ra SP CHẠM khung giờ của d (rà độc lập 21/11 Trung 2: trước theo ngày giờ bắt đầu →
        //   đuôi ca đêm sau 0h làm Chủ nhật thành ngày kế hoạch rảnh giả)
        DateTime WinS(DateOnly d) => d.ToDateTime(fromTime);
        DateTime WinE(DateOnly d) => toTime < fromTime ? d.AddDays(1).ToDateTime(toTime) : d.ToDateTime(toTime);
        var qS = WinS(from); var qE = WinE(to);
        var iv = (await _db.WtsProductionLogs.Where(x => !x.IsVoided && x.StartTime < qE && x.EndTime > qS).Select(x => new { x.StartTime, x.EndTime }).ToListAsync())
            .Select(x => (x.StartTime, x.EndTime)).ToList();
        iv.AddRange((await _db.WorkerActivityLogs.Where(x => !x.IsVoided && x.StartTime < qE && x.EndTime > qS).Select(x => new { x.StartTime, x.EndTime }).ToListAsync())
            .Select(x => (x.StartTime, x.EndTime)));
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i))
            .Where(d => iv.Any(x => x.StartTime < WinE(d) && x.EndTime > WinS(d))).ToList();
        res.Days = days;
        if (days.Count == 0) return res;

        var data = await LoadAsync(days[0], days[^1], fromTime, toTime);
        if (data == null) return res;
        Accumulate(res, data, days, d => ComputeDay(data, d, fromTime, toTime, selectedCa));
        return res;
    }

    /// <summary>[G31-HSM-24H] Cộng từng ngày (dùng chung kỳ theo ca cũ + kỳ 24h).</summary>
    void Accumulate(MachinePeriodResult res, LoadedData data, List<DateOnly> days, Func<DateOnly, MachineEfficiencyResult> computeDay, Func<WtsProductionLog, bool>? qtyIn = null)
    {
        var acc = data.Machines.ToDictionary(m => m.ThietBiId, m => new MachinePeriodRow { Machine = m });
        // [G31-HSM-24H] tra nhanh (xem Năm: 366 ngày × mọi máy) — rà độc lập 22/11 Trung 3
        var evIx = new Dictionary<(int, int), MachineDownEvent>();
        var poIx = new Dictionary<(int, string, string, string), MachinePoStat>();
        var wkIx = new Dictionary<(int, int), MachineWorkerStat>();
        var qtyDone = new HashSet<long>();
        var workerIds = data.Machines.ToDictionary(m => m.ThietBiId, _ => new HashSet<int>());
        var downInfo = data.Down.ToDictionary(x => x.ChangeId);
        foreach (var d in days)
        {
            var day = computeDay(d);
            foreach (var r in day.Rows)
            {
                var a = acc[r.Machine.ThietBiId];
                var pd = new MachinePeriodDay { Day = d, Total = r.TotalMinutes, Run = r.RunMinutes, Down = r.DownMinutes, Break = r.BreakMinutes, Idle = r.IdleMinutes, NoCrew = r.NoCrewMinutes };
                a.PerDay.Add(pd);
                a.TotalMinutes += r.TotalMinutes; a.RunMinutes += r.RunMinutes; a.DownMinutes += r.DownMinutes; a.BreakMinutes += r.BreakMinutes; a.IdleMinutes += r.IdleMinutes;
                a.NoCrewMinutes += r.NoCrewMinutes;   // [G31-HSM-24H]
                // dừng: theo lần dừng (ChangeId)
                foreach (var g in r.Segments.Where(x => x.Type is SegmentType.Breakdown or SegmentType.Maintenance).GroupBy(x => x.DownLogId))
                {
                    evIx.TryGetValue((r.Machine.ThietBiId, g.Key), out var ev);
                    if (ev == null)
                    {
                        downInfo.TryGetValue(g.Key, out var lg);
                        ev = new MachineDownEvent
                        {
                            ChangeId = g.Key, Start = lg?.ThoiGianBatDau ?? g.Min(x => x.Start), End = lg?.ThoiGianKetThuc,
                            Reason = string.IsNullOrWhiteSpace(g.First().Reason) ? "(không ghi lý do)" : g.First().Reason.Trim(),
                            IsMaintenance = g.First().Type == SegmentType.Maintenance, ReportedBy = lg != null && data.Reporter.TryGetValue(lg.ChangedBy, out var rn) ? rn : "",
                        };
                        a.Downs.Add(ev); evIx[(r.Machine.ThietBiId, g.Key)] = ev;
                    }
                    ev.Minutes += g.Sum(x => x.Minutes);
                }
                // chạy theo PO / Part / NC (đoạn Chạy đã cắt dừng + nghỉ, 2 người cùng máy tính 1 lần)
                foreach (var g in r.Segments.Where(x => x.Type == SegmentType.Running).GroupBy(x => (x.PurchaseOrder, x.PartNo, x.NC)))
                {
                    poIx.TryGetValue((r.Machine.ThietBiId, g.Key.PurchaseOrder, g.Key.PartNo, g.Key.NC), out var po);
                    if (po == null) { po = new MachinePoStat { Po = g.Key.PurchaseOrder, Part = g.Key.PartNo, NC = g.Key.NC }; a.ByPo.Add(po); poIx[(r.Machine.ThietBiId, po.Po, po.Part, po.NC)] = po; }
                    po.RunMinutes += g.Sum(x => x.Minutes);
                }
                // người đứng máy: mỗi người đủ phút của mình (hợp đoạn làm các dòng của người đó)
                foreach (var g in r.RawRunSegments.GroupBy(x => (x.WorkerId, x.WorkerName)))
                {
                    wkIx.TryGetValue((r.Machine.ThietBiId, g.Key.WorkerId), out var w);
                    if (w == null) { w = new MachineWorkerStat { WorkerId = g.Key.WorkerId, WorkerName = g.Key.WorkerName }; a.ByWorker.Add(w); wkIx[(r.Machine.ThietBiId, w.WorkerId)] = w; }
                    w.Minutes += UnionMinutes(g.Select(x => (x.Start, x.End)));
                }
                // OK / NG: dòng WTS tính 1 lần (ngày đầu tiên chạm khung)
                foreach (var l in r.WtsDetails)
                {
                    if (qtyIn != null && !qtyIn(l)) continue;   // [G31-HSM-24H] OK / NG theo ngày bắt đầu dòng
                    if (!qtyDone.Add(l.WtsLogId)) continue;
                    var ok = l.QtyOk ?? 0; var ng = l.QtyNg ?? 0;
                    a.Ok += ok; a.Ng += ng; workerIds[r.Machine.ThietBiId].Add(l.WorkerId);
                    var pk = (r.Machine.ThietBiId, l.KhPlanDetail?.PurchaseOrder ?? "", l.KhPlanDetail?.PartNo ?? "", l.NC ?? "");
                    poIx.TryGetValue(pk, out var po);
                    if (po == null) { po = new MachinePoStat { Po = pk.Item2, Part = pk.Item3, NC = pk.Item4 }; a.ByPo.Add(po); poIx[pk] = po; }
                    po.Ok += ok; po.Ng += ng;
                }
            }
        }
        foreach (var a in acc.Values)
        {
            a.WorkerCount = workerIds[a.Machine.ThietBiId].Count;
            a.Downs = a.Downs.OrderBy(x => x.Start).ToList();
            a.ByPo = a.ByPo.OrderByDescending(x => x.RunMinutes).ThenBy(x => x.Po).ToList();
            a.ByWorker = a.ByWorker.OrderByDescending(x => x.Minutes).ThenBy(x => x.WorkerName).ToList();
            a.SearchText = string.Join(" ", new[] { a.Machine.SoMay, a.Machine.TenMay, a.Machine.MaMay, a.Machine.BoPhan, a.Machine.LoaiMay }
                .Concat(a.ByPo.SelectMany(x => new[] { x.Po, x.Part, x.NC })).Concat(a.ByWorker.Select(x => x.WorkerName)).Concat(a.Downs.Select(x => x.Reason))
                .Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
        }
        res.Rows = acc.Values.ToList();
    }

    static double UnionMinutes(IEnumerable<(DateTime S, DateTime E)> iv)
    {
        double tot = 0; DateTime? cs = null, ce = null;
        foreach (var x in iv.Where(x => x.E > x.S).OrderBy(x => x.S))
        {
            if (cs == null) { cs = x.S; ce = x.E; continue; }
            if (x.S <= ce) { if (x.E > ce) ce = x.E; continue; }
            tot += (ce!.Value - cs.Value).TotalMinutes; cs = x.S; ce = x.E;
        }
        if (cs != null) tot += (ce!.Value - cs.Value).TotalMinutes;
        return tot;
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

public enum SegmentType { Running, Breakdown, Maintenance, Break, Idle, NoCrew }   // [G31-HSM-24H] NoCrew = xưởng không ai khai WTS (Idle = rảnh lúc xưởng có người)

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
    public int         DownLogId     { get; init; }   // [G29-HSM-KY] ThietBiChangeLogs.ChangeId của đoạn dừng
    public double Minutes => (End - Start).TotalMinutes;
}

public class MachineRow
{
    public ThietBi                Machine      { get; set; } = null!;
    public List<TimelineSegment>  Segments     { get; set; } = new();
    /// <summary>[G29-HSM-KY] Đoạn làm từng dòng WTS trên máy (cắt theo khung, trước khi dừng / nghỉ che) — phút đứng máy từng người.</summary>
    public List<TimelineSegment>  RawRunSegments { get; set; } = new();
    public List<WtsProductionLog> WtsDetails   { get; set; } = new();
    /// <summary>[ML17-XN] Phút từng dòng WTS (WorkMinutes.Line với giờ nghỉ đã lưu) — cột Phút bảng chi tiết máy.</summary>
    public Dictionary<long, LineMin> LogMinutes { get; set; } = new();
    public double TotalMinutes  { get; set; }
    public double RunMinutes    { get; set; }
    public double DownMinutes   { get; set; }
    public double BreakMinutes  { get; set; }
    public double IdleMinutes   { get; set; }

    // [G29-HSM-MAUSO 2026-11-21] % chạy / dừng / rảnh tính trên phút KẾ HOẠCH = khung giờ − nghỉ ca (trước: trên cả khung kể cả nghỉ →
    //   % chạy bị kéo thấp). Chạy + Dừng + Rảnh = 100 %. BreakPct vẫn trên cả khung (chỉ để hiện phút nghỉ).
    //   Nguồn: CLAUDE đề xuất câu B1 mockup Hiệu suất máy; HÙNG 21/11 09:46 OK.
    public double PlanMinutes => Math.Max(0, TotalMinutes - BreakMinutes);
    public double RunPct   => PlanMinutes <= 0 ? 0 : Math.Round(RunMinutes   / PlanMinutes * 100, 1);
    public double DownPct  => PlanMinutes <= 0 ? 0 : Math.Round(DownMinutes  / PlanMinutes * 100, 1);
    public double BreakPct => TotalMinutes == 0 ? 0 : Math.Round(BreakMinutes / TotalMinutes * 100, 1);
    public double IdlePct  => PlanMinutes <= 0 ? 0 : Math.Round(IdleMinutes  / PlanMinutes * 100, 1);
    // [G31-HSM-24H] Rảnh chia 2: lúc xưởng có người làm (IdleCrew) / xưởng không ai khai WTS (NoCrew; chỉ khi xem 24h, khung ca cũ = 0)
    public double NoCrewMinutes { get; set; }
    public double IdleCrewMinutes => Math.Max(0, IdleMinutes - NoCrewMinutes);
    public double CrewMinutes => Math.Max(0, PlanMinutes - NoCrewMinutes);   // giờ xưởng có người (+ máy chạy / dừng)
    public double IdleCrewPct => PlanMinutes <= 0 ? 0 : Math.Round(IdleCrewMinutes / PlanMinutes * 100, 1);
    public double NoCrewPct   => PlanMinutes <= 0 ? 0 : Math.Round(NoCrewMinutes   / PlanMinutes * 100, 1);
    public double RunPctCrew  => CrewMinutes <= 0 ? 0 : Math.Round(RunMinutes / CrewMinutes * 100, 1);
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

    // [G29-HSM-MAUSO 2026-11-21] như MachineRow: % trên phút kế hoạch (khung − nghỉ)
    public double PlanMinutes => Math.Max(0, TotalMinutes - BreakMinutes);
    public double RunPct   => PlanMinutes <= 0 ? 0 : Math.Round(RunMinutes   / PlanMinutes * 100, 1);
    public double DownPct  => PlanMinutes <= 0 ? 0 : Math.Round(DownMinutes  / PlanMinutes * 100, 1);
    public double BreakPct => TotalMinutes == 0 ? 0 : Math.Round(BreakMinutes / TotalMinutes * 100, 1);
    public double IdlePct  => PlanMinutes <= 0 ? 0 : Math.Round(IdleMinutes  / PlanMinutes * 100, 1);
    // [G31-HSM-24H] Rảnh chia 2: lúc xưởng có người làm (IdleCrew) / xưởng không ai khai WTS (NoCrew; chỉ khi xem 24h, khung ca cũ = 0)
    public double NoCrewMinutes { get; set; }
    public double IdleCrewMinutes => Math.Max(0, IdleMinutes - NoCrewMinutes);
    public double CrewMinutes => Math.Max(0, PlanMinutes - NoCrewMinutes);   // giờ xưởng có người (+ máy chạy / dừng)
    public double IdleCrewPct => PlanMinutes <= 0 ? 0 : Math.Round(IdleCrewMinutes / PlanMinutes * 100, 1);
    public double NoCrewPct   => PlanMinutes <= 0 ? 0 : Math.Round(NoCrewMinutes   / PlanMinutes * 100, 1);
    public double RunPctCrew  => CrewMinutes <= 0 ? 0 : Math.Round(RunMinutes / CrewMinutes * 100, 1);
    /// <summary>[G31-HSM-24H] true = khung 00:00–24:00, không ca.</summary>
    public bool Full24 { get; set; }
}

// ── [G29-HSM-KY 2026-11-21] DTO báo cáo theo kỳ ─────────────────────────────────────────────────────────────
public class MachinePeriodResult
{
    public DateOnly From { get; set; }
    public DateOnly To   { get; set; }
    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime   { get; set; }
    public string?  Error { get; set; }
    public bool     Full24 { get; set; }   // [G31-HSM-24H]
    /// <summary>Ngày được tính (khung ca cũ: xưởng có WTS; 24h: mọi ngày lịch).</summary>
    public List<DateOnly> Days { get; set; } = new();
    public List<MachinePeriodRow> Rows { get; set; } = new();
}

public class MachinePeriodRow
{
    public ThietBi Machine { get; set; } = null!;
    public double TotalMinutes { get; set; }
    public double RunMinutes   { get; set; }
    public double DownMinutes  { get; set; }
    public double BreakMinutes { get; set; }
    public double IdleMinutes  { get; set; }
    public double PlanMinutes => Math.Max(0, TotalMinutes - BreakMinutes);
    public double RunPct  => PlanMinutes <= 0 ? 0 : Math.Round(RunMinutes  / PlanMinutes * 100, 1);
    public double DownPct => PlanMinutes <= 0 ? 0 : Math.Round(DownMinutes / PlanMinutes * 100, 1);
    public double IdlePct => PlanMinutes <= 0 ? 0 : Math.Round(IdleMinutes / PlanMinutes * 100, 1);
    // [G31-HSM-24H] Rảnh chia 2: lúc xưởng có người làm (IdleCrew) / xưởng không ai khai WTS (NoCrew; chỉ khi xem 24h, khung ca cũ = 0)
    public double NoCrewMinutes { get; set; }
    public double IdleCrewMinutes => Math.Max(0, IdleMinutes - NoCrewMinutes);
    public double CrewMinutes => Math.Max(0, PlanMinutes - NoCrewMinutes);   // giờ xưởng có người (+ máy chạy / dừng)
    public double IdleCrewPct => PlanMinutes <= 0 ? 0 : Math.Round(IdleCrewMinutes / PlanMinutes * 100, 1);
    public double NoCrewPct   => PlanMinutes <= 0 ? 0 : Math.Round(NoCrewMinutes   / PlanMinutes * 100, 1);
    public double RunPctCrew  => CrewMinutes <= 0 ? 0 : Math.Round(RunMinutes / CrewMinutes * 100, 1);
    public decimal Ok { get; set; }
    public decimal Ng { get; set; }
    public int WorkerCount { get; set; }
    public List<MachinePeriodDay>   PerDay   { get; set; } = new();
    public List<MachineDownEvent>   Downs    { get; set; } = new();
    public List<MachinePoStat>      ByPo     { get; set; } = new();
    public List<MachineWorkerStat>  ByWorker { get; set; } = new();
    /// <summary>Chữ thường gộp (máy, nhóm, loại, PO, Part, NC, người, lý do dừng) — ô Tìm nhanh.</summary>
    public string SearchText { get; set; } = "";
    public MachineDownEvent? TopDown => Downs.GroupBy(x => x.Reason).Select(g => new MachineDownEvent { Reason = g.Key, Minutes = g.Sum(x => x.Minutes) })
        .OrderByDescending(x => x.Minutes).FirstOrDefault();
}

public class MachinePeriodDay
{
    public DateOnly Day { get; set; }
    public double Total { get; set; }
    public double Run   { get; set; }
    public double Down  { get; set; }
    public double Break { get; set; }
    public double Idle  { get; set; }
    public double Plan => Math.Max(0, Total - Break);
    public double RunPct => Plan <= 0 ? 0 : Math.Round(Run / Plan * 100, 1);
    public double NoCrew { get; set; }                               // [G31-HSM-24H]
    public double IdleCrew => Math.Max(0, Idle - NoCrew);
    public double RunPctCrew => Plan - NoCrew <= 0 ? 0 : Math.Round(Run / (Plan - NoCrew) * 100, 1);
}

public class MachineDownEvent
{
    public int ChangeId { get; set; }
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public string Reason { get; set; } = "";
    public bool IsMaintenance { get; set; }
    public string ReportedBy { get; set; } = "";
    /// <summary>Phút dừng tính trong khung giờ các ngày được tính.</summary>
    public double Minutes { get; set; }
}

public class MachinePoStat
{
    public string Po { get; set; } = "";
    public string Part { get; set; } = "";
    public string NC { get; set; } = "";
    public double RunMinutes { get; set; }
    public decimal Ok { get; set; }
    public decimal Ng { get; set; }
}

public class MachineWorkerStat
{
    public int WorkerId { get; set; }
    public string WorkerName { get; set; } = "";
    public double Minutes { get; set; }
}
