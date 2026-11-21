// [G29-SCEN 2026-11-21] Kịch bản `rpt` — Gói 29 báo cáo theo kỳ (chỉ đọc, không đổi số in/out):
//   P1 [G29-RPT]       bộ số cố định (giống tools/WtsHarness/Stubs/HarnessG29.cs) — đáp án TÍNH TAY: phút làm / có ích / vô ích / chia phút từng dòng /
//                      kế hoạch / chưa khai báo từng người-ngày; dòng việc vô ích chồng dòng sản xuất → 0 phút (có ích thắng).
//   P2 [G29-RPT]       ngẫu nhiên 300 người-ngày (dòng chồng, làm thông nghỉ, có / không ca, việc có ích / vô ích): so với đếm TỪNG PHÚT độc lập —
//                      Σ AllocMin = phút làm người-ngày = số cũ TotalMinutes; Σ dòng có ích = phút có ích; AllocMin ≤ phút làm của dòng;
//                      kế hoạch = phút khung ca ngoài nghỉ; chưa khai báo = phút kế hoạch không dòng nào phủ.
//   P3 [G29-HSM-KY][G29-HSM-MAUSO] hiệu suất máy theo kỳ = cộng từng ngày GetAsync (cùng ca); ngày tính = ngày xưởng có WTS; OK / NG mỗi dòng 1 lần;
//                      người đứng máy = đếm từng phút; Chạy + Dừng + Rảnh = kế hoạch (khung − nghỉ), % trên kế hoạch.
//   P4 [HSM-QUYEN][RPT-4] không quyền → rỗng; khoảng > 93 ngày / Từ > Đến → lời báo.
//   Nguồn: HÙNG 21/11 09:46 OK mockup WTS Report + Hiệu suất máy (câu B theo đề xuất CLAUDE).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class RptScen
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        if (!cond || Environment.GetEnvironmentVariable("RPT_VERBOSE") == "1")
            Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!cond) fails++;
    }
    static void Say(bool cond, string name, string? info = null) { checks++; Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : "")); if (!cond) fails++; }

    static readonly DateTime D1 = DateTime.Today.AddDays(-14);   // thứ gì cũng được — báo cáo không phụ thuộc thứ
    static readonly DateTime D2 = D1.AddDays(1);

    static CaLamViec AddCa(AppDbContext db, int id, TimeOnly bd, TimeOnly kt, params (int h1, int m1, int h2, int m2)[] br)
    {
        var ca = new CaLamViec { CaId = id, TenCa = "Ca" + id, GioBatDau = bd, GioKetThuc = kt, IsActive = true };
        db.CaLamViecs.List.Add(ca);
        int k = 0;
        foreach (var b in br)
        {
            var n = new CaLamViec_NghiGiaiLao { NghiId = id * 100 + ++k, CaId = id, NghiBatDau = new TimeOnly(b.h1, b.m1), NghiKetThuc = new TimeOnly(b.h2, b.m2), GhiChu = "nghỉ" };
            ca.NghiGiaiLaos.Add(n); db.CaLamViec_NghiGiaiLaos.List.Add(n);
        }
        return ca;
    }

    static long wid = 70000, aid = 80000, bid = 90000;
    static WtsProductionLog P(AppDbContext db, int w, string? mach, string mode, DateTime s, DateTime e, decimal ok, decimal ng, string nc, int? ca, int worked = -1)
    {
        var l = new WtsProductionLog { WtsLogId = ++wid, KhPlanDetailId = 1, ProcessGroup = "GC", NC = nc, MachineUsed = mach, ProductionMode = mode, WorkerId = w,
            QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, StartTime = s, EndTime = e, CaId = ca, CreatedAt = s, Worker = db.Users.List.First(u => u.UserId == w) };
        db.WtsProductionLogs.List.Add(l);
        if (ca != null) AddBreaks(db, ca.Value, s, e, l.WtsLogId, null, worked);
        return l;
    }
    static WorkerActivityLog A(AppDbContext db, int w, string code, DateTime s, DateTime e, int? ca = null, int worked = -1)
    {
        var a = new WorkerActivityLog { ActivityLogId = ++aid, WorkerId = w, WorkDate = DateOnly.FromDateTime(s), StartTime = s, EndTime = e, WtsCode = code, WtsName = code,
            CreatedAt = s, Worker = db.Users.List.First(u => u.UserId == w) };
        db.WorkerActivityLogs.List.Add(a);
        if (ca != null) AddBreaks(db, ca.Value, s, e, null, a.ActivityLogId, worked);
        return a;
    }
    /// <summary>Khoảng nghỉ đã lưu của dòng (như WtsSaveService ghi): mọi khoảng nghỉ ca mà dòng chạm; worked = -1 → ngẫu nhiên đã chọn trước = 0.</summary>
    static void AddBreaks(AppDbContext db, int caId, DateTime s, DateTime e, long? w, long? a, int worked)
    {
        var ca = db.CaLamViecs.List.First(c => c.CaId == caId);
        foreach (var b in WorkMinutes.Touching(ca, s, e))
        {
            b.WorkedMin = worked < 0 ? 0 : Math.Min(worked, b.OverlapMin);
            foreach (var x in WorkMinutes.ToEntities(new[] { b }, w, a)) { x.Id = ++bid; db.WtsBreakWorkLogs.List.Add(x); }
        }
    }
    static void Task(AppDbContext db, string code, bool pro)
        => db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = db.StandardWtsTasks.List.Count + 1, CategoryCode = "CHUNG", TaskCode = code, TaskName = "Việc " + code, IsActive = true, IsProductiveTask = pro });

    public static async Task<int> Run()
    {
        await P1(); await P1b(); await P2(); await P3(); await P4();
        Console.WriteLine($"Tổng phép kiểm: {checks}");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }

    // ── P1 bộ số cố định ───────────────────────────────────────────────────────────────────────────────────
    static async Task P1()
    {
        Console.WriteLine("[P1] G29-RPT — bộ số cố định (như HarnessG29), đáp án tính tay");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        Task(db, "HOP29", false); Task(db, "VS29", false); Task(db, "SUA29", true);
        // A = u2, B = u3
        var r1 = P(db, 2, "M29A", "serial", D1.AddHours(8), D1.AddHours(10), 40, 1, "NC01", 9);
        var r2 = P(db, 2, "M29B", "serial", D1.AddHours(9), D1.AddHours(11), 30, 0, "NC02", 9);
        var a1 = A(db, 2, "HOP29", D1.AddHours(11), D1.AddHours(11.5));
        var r3 = P(db, 2, "M29A", "trial", D1.AddHours(13), D1.AddHours(14), 1, 0, "NC03", 9);
        var a2 = A(db, 2, "SUA29", D1.AddHours(14), D1.AddHours(15));
        var b1 = A(db, 3, "VS29", D1.AddHours(8), D1.AddHours(8.5));
        var r4 = P(db, 3, "M29B", "serial", D1.AddHours(8.5), D1.AddHours(12), 50, 0, "NC02", 9);
        var b2 = A(db, 3, "HOP29", D1.AddHours(11.5), D1.AddHours(12));
        var r5 = P(db, 2, "M29A", "serial", D2.AddHours(8), D2.AddHours(12), 60, 0, "NC01", 9);
        var r6 = P(db, 2, "M29B", "serial", D2.AddHours(13), D2.AddHours(17), 70, 0, "NC02", 9);
        db.Fixup();
        var res = await new WtsReportService(db).GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D2), null, 1);
        Say(res.TotalMinutes == 1050 && res.ProductiveMin == 990 && res.WasteMin == 60, "P1 tổng cũ không đổi: làm 1050, có ích 990, vô ích 60", $"{res.TotalMinutes}/{res.ProductiveMin}/{res.WasteMin}");
        int Al(long? w = null, long? a = null) => res.Rows.First(x => (w != null && x.WtsLogId == w) || (a != null && x.ActivityLogId == a)).AllocMin;
        Say(Al(r1.WtsLogId) == 90 && Al(r2.WtsLogId) == 90 && Al(r3.WtsLogId) == 60 && Al(a: a1.ActivityLogId) == 30 && Al(a: a2.ActivityLogId) == 60,
            "P1 A ngày 1 chia phút: 2 máy chồng 09–10 → 90 / 90; chạy thử 60; HOP29 30; SUA29 60",
            string.Join(",", Al(r1.WtsLogId), Al(r2.WtsLogId), Al(r3.WtsLogId), Al(a: a1.ActivityLogId), Al(a: a2.ActivityLogId)));
        Say(Al(a: b1.ActivityLogId) == 30 && Al(r4.WtsLogId) == 210 && Al(a: b2.ActivityLogId) == 0,
            "P1 B ngày 1: VS29 30, M29B 210, HOP29 chồng dòng sản xuất → 0 (có ích thắng)", string.Join(",", Al(a: b1.ActivityLogId), Al(r4.WtsLogId), Al(a: b2.ActivityLogId)));
        var dA1 = res.Days.First(d => d.WorkerId == 2 && d.Day == DateOnly.FromDateTime(D1));
        var dB1 = res.Days.First(d => d.WorkerId == 3 && d.Day == DateOnly.FromDateTime(D1));
        var dA2 = res.Days.First(d => d.WorkerId == 2 && d.Day == DateOnly.FromDateTime(D2));
        Say(dA1.WorkMin == 330 && dA1.ProductiveMin == 300 && dA1.WasteMin == 30 && dA1.PlanMin == 480 && dA1.UnaccountedMin == 150,
            "P1 người-ngày A1: 330 / 300 / 30, kế hoạch 480, chưa khai báo 150", $"{dA1.WorkMin}/{dA1.ProductiveMin}/{dA1.WasteMin} kh {dA1.PlanMin} ckb {dA1.UnaccountedMin}");
        Say(dB1.WorkMin == 240 && dB1.ProductiveMin == 210 && dB1.WasteMin == 30 && dB1.UnaccountedMin == 240, "P1 B1: 240 / 210 / 30, chưa khai báo 240",
            $"{dB1.WorkMin}/{dB1.ProductiveMin}/{dB1.WasteMin} ckb {dB1.UnaccountedMin}");
        Say(dA2.WorkMin == 480 && dA2.UnaccountedMin == 0 && res.Days.Count == 3, "P1 A2: 480, chưa khai báo 0; 3 người-ngày", $"{dA2.WorkMin}/{dA2.UnaccountedMin} n {res.Days.Count}");
        var hl = res.Rows.Where(x => x.WorkerId == 2 && x.TaskKey == "GC|serial").ToList();
        Say(hl.Count == 4 && hl.Sum(x => x.AllocMin) == 660 && hl.All(x => x.TaskLabel == "Gia công · Hàng loạt") && res.Rows.Count(x => x.TaskKey == "GC|trial") == 1
            && res.Rows.First(x => x.ActivityLogId == a1.ActivityLogId).TaskKey == "ACT|HOP29" && res.Rows.First(x => x.ActivityLogId == a1.ActivityLogId).GroupLabel == "Việc không ra SP",
            "P1 việc: A Hàng loạt 4 dòng 660; Chạy thử 1 dòng; HOP29 nhóm Việc không ra SP", $"{hl.Count}/{hl.Sum(x => x.AllocMin)}");
    }

    // ── P1b rà độc lập 21/11: ca đêm (dòng sau 0h) + ca đổi giờ nghỉ sau khi lưu ─────────────────────────────
    static async Task P1b()
    {
        Console.WriteLine("[P1b] G29-RPT — ca đêm 20:00–05:00 khai đủ → chưa khai báo 0; ca đổi giờ nghỉ sau khi lưu → không sinh chưa khai báo giả");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 8, new TimeOnly(20, 0), new TimeOnly(5, 0), (0, 0, 0, 30));
        var ca9 = AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        // đêm: 20:00–24:00 (D1) + 00:30–05:00 (giờ bắt đầu D2) — đủ ca
        P(db, 2, "M1", "serial", D1.AddHours(20), D1.AddHours(24), 5, 0, "NC1", 8);
        P(db, 2, "M1", "serial", D1.AddHours(24.5), D1.AddHours(29), 5, 0, "NC1", 8);
        // ngày: 08:00–17:00 lưu nghỉ 12–13; SAU đó admin đổi nghỉ thành 11:30–12:30
        P(db, 3, "M2", "serial", D1.AddHours(8), D1.AddHours(17), 5, 0, "NC1", 9);
        ca9.NghiGiaiLaos.Clear(); db.CaLamViec_NghiGiaiLaos.List.RemoveAll(n => n.CaId == 9);
        var n9 = new CaLamViec_NghiGiaiLao { NghiId = 999, CaId = 9, NghiBatDau = new TimeOnly(11, 30), NghiKetThuc = new TimeOnly(12, 30), GhiChu = "mới" };
        ca9.NghiGiaiLaos.Add(n9); db.CaLamViec_NghiGiaiLaos.List.Add(n9);
        db.Fixup();
        var res = await new WtsReportService(db).GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D2), null, 1);
        var a1 = res.Days.First(d => d.WorkerId == 2 && d.Day == DateOnly.FromDateTime(D1));
        var a2 = res.Days.First(d => d.WorkerId == 2 && d.Day == DateOnly.FromDateTime(D2));
        Say(a1.PlanMin == 510 && a1.UnaccountedMin == 0 && a2.PlanMin == null && a2.UnaccountedMin == null,
            "P1b ca đêm khai đủ: ngày ca 510 kế hoạch, chưa khai báo 0; phần sau 0h không tính kế hoạch lần 2 (trước: 270 + 510)",
            $"D1 {a1.PlanMin}/{a1.UnaccountedMin} · D2 {a2.PlanMin}/{a2.UnaccountedMin}");
        var b1 = res.Days.First(d => d.WorkerId == 3);
        Say(b1.UnaccountedMin == 0 && b1.WorkMin == 480, "P1b ca đổi giờ nghỉ sau khi lưu: chưa khai báo 0 (trừ cả nghỉ đã lưu), làm 480", $"{b1.PlanMin}/{b1.UnaccountedMin} làm {b1.WorkMin}");
        // máy: đêm thứ Bảy tràn sang Chủ nhật — xem ca ngày 08–17 thì Chủ nhật không thành ngày kế hoạch
        var db2 = await WtsScen.SeedAsync();
        db2.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        var ca = AddCa(db2, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        P(db2, 2, "M1", "serial", D1.AddHours(9), D1.AddHours(10), 5, 0, "NC1", 9);
        P(db2, 2, "M1", "serial", D1.AddHours(22), D1.AddHours(24), 5, 0, "NC1", null);   // đêm 22:00 → 24:00
        P(db2, 2, "M1", "serial", D1.AddHours(24.5), D1.AddHours(26), 5, 0, "NC1", null);   // phần sau 0h: giờ bắt đầu thuộc hôm sau, ngoài khung 08–17
        db2.Fixup();
        var pr = await new MachineEfficiencyService(db2).GetPeriodAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D2), ca.GioBatDau, ca.GioKetThuc, ca, 1);
        Say(pr.Days.Count == 1 && pr.Days[0] == DateOnly.FromDateTime(D1), "P1b máy: dòng tràn sang 0h hôm sau (ngoài khung 08–17) không làm hôm sau thành ngày kế hoạch",
            string.Join(",", pr.Days));
    }

    // ── P2 ngẫu nhiên, đếm từng phút ───────────────────────────────────────────────────────────────────────
    static async Task P2()
    {
        Console.WriteLine("[P2] G29-RPT — ngẫu nhiên 300 người-ngày, đối chiếu đếm từng phút");
        int seed = int.TryParse(Environment.GetEnvironmentVariable("RPT_SEED"), out var sd) ? sd : 29211;
        var rnd = new Random(seed);
        int bad0 = fails;
        for (int round = 0; round < 6; round++)
        {
            var db = await WtsScen.SeedAsync();
            AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0), (15, 0, 15, 10));
            AddCa(db, 8, new TimeOnly(20, 0), new TimeOnly(5, 0), (0, 0, 0, 30));   // ca qua đêm
            Task(db, "V1", false); Task(db, "V2", false); Task(db, "C1", true);
            var codes = new[] { "V1", "V2", "C1", "LA" };   // LA: mã không có trong danh mục → có ích (luật cũ)
            for (int k = 0; k < 50; k++)
            {
                int w = 2 + rnd.Next(4); var day = D1.AddDays(rnd.Next(5));
                int? ca = rnd.Next(10) < 7 ? 9 : (rnd.Next(2) == 0 ? (int?)null : 8);
                var baseT = ca == 8 ? day.AddHours(19) : day.AddHours(7);
                int n = 1 + rnd.Next(6);
                for (int i = 0; i < n; i++)
                {
                    var s = baseT.AddMinutes(rnd.Next(0, 600)); var e = s.AddMinutes(5 + rnd.Next(0, 240));
                    int worked = rnd.Next(3) == 0 ? rnd.Next(0, 61) : 0;
                    if (rnd.Next(3) == 0) A(db, w, codes[rnd.Next(codes.Length)], s, e, rnd.Next(2) == 0 ? ca : null, worked);
                    else P(db, w, "M" + rnd.Next(1, 4), rnd.Next(4) == 0 ? "trial" : "serial", s, e, rnd.Next(0, 20), rnd.Next(0, 2), "NC" + rnd.Next(1, 3), ca, worked);
                }
            }
            db.Fixup();
            var f = DateOnly.FromDateTime(D1); var t = f.AddDays(5);
            var res = await new WtsReportService(db).GetReportAsync(f, t, null, 1);
            // ── đếm từng phút độc lập ──
            var proCodes = db.StandardWtsTasks.List.Where(x => !x.IsProductiveTask).Select(x => x.TaskCode.ToUpperInvariant()).ToHashSet();
            int totAll = 0, totPro = 0;
            // phút làm của từng người trên MỌI dòng (phủ "chưa khai báo" xét mọi dòng chạm khung ca — rà độc lập 21/11 Cao 1)
            var wMins = new Dictionary<int, HashSet<DateTime>>();
            foreach (var r in res.Rows)
            {
                if (!wMins.TryGetValue(r.WorkerId, out var hs)) wMins[r.WorkerId] = hs = new();
                for (var m = r.StartTime; m < r.EndTime; m = m.AddMinutes(1))
                {
                    bool inBrk = false, inChunk = false;
                    foreach (var b in r.Breaks)
                    {
                        if (m >= b.BreakStart && m < b.BreakEnd) inBrk = true;
                        var cs = b.BreakStart > r.StartTime ? b.BreakStart : r.StartTime;
                        if (b.WorkedMin > 0 && m >= cs && m < cs.AddMinutes(b.WorkedMin) && m < b.BreakEnd) inChunk = true;
                    }
                    if (!inBrk || inChunk) hs.Add(m);
                }
            }
            foreach (var g in res.Rows.GroupBy(r => (r.WorkerId, r.WorkDate)))
            {
                var mins = new Dictionary<DateTime, (bool any, bool pro)>();
                var own = new Dictionary<WtsReportRow, int>();
                foreach (var r in g)
                {
                    int c = 0;
                    for (var m = r.StartTime; m < r.EndTime; m = m.AddMinutes(1))
                    {
                        bool inBrk = false, inChunk = false;
                        foreach (var b in r.Breaks)
                        {
                            if (m >= b.BreakStart && m < b.BreakEnd) inBrk = true;
                            var cs = b.BreakStart > r.StartTime ? b.BreakStart : r.StartTime;
                            if (b.WorkedMin > 0 && m >= cs && m < cs.AddMinutes(b.WorkedMin) && m < b.BreakEnd && m < r.EndTime) inChunk = true;
                        }
                        if (inBrk && !inChunk) continue;
                        c++;
                        mins.TryGetValue(m, out var v);
                        mins[m] = (true, v.pro || r.IsProductiveTask);
                    }
                    own[r] = c;
                }
                int dAll = mins.Count, dPro = mins.Values.Count(v => v.pro);
                totAll += dAll; totPro += dPro;
                Ok(g.Sum(r => r.AllocMin) == dAll, $"P2 r{round} người {g.Key.WorkerId} {g.Key.WorkDate}: Σ chia = phút làm {dAll}", $"{g.Sum(r => r.AllocMin)}");
                Ok(g.Where(r => r.IsProductiveTask).Sum(r => r.AllocMin) == dPro, $"P2 r{round} Σ chia dòng có ích = phút có ích {dPro}", $"{g.Where(r => r.IsProductiveTask).Sum(r => r.AllocMin)}");
                Ok(g.All(r => r.AllocMin >= 0 && r.AllocMin <= own[r]), $"P2 r{round} mỗi dòng 0 ≤ chia ≤ phút làm của dòng", string.Join(",", g.Select(r => $"{r.AllocMin}/{own[r]}")));
                var d = res.Days.FirstOrDefault(x => x.WorkerId == g.Key.WorkerId && x.Day == g.Key.WorkDate);
                Ok(d != null && d.WorkMin == dAll && d.ProductiveMin == dPro && d.WasteMin == dAll - dPro, $"P2 r{round} người-ngày {g.Key.WorkerId} {g.Key.WorkDate}: số ngày = đếm phút");
                // kế hoạch / chưa khai báo: ca = ca nhiều phút nhất trên dòng sản xuất
                var caId = g.Where(r => r.CaId != null).GroupBy(r => r.CaId!.Value).Select(x => (x.Key, x.Sum(r => r.Minutes))).OrderByDescending(x => x.Item2).ThenBy(x => x.Key).Select(x => (int?)x.Key).FirstOrDefault();
                if (caId == null) { Ok(d?.PlanMin == null && d?.UnaccountedMin == null, $"P2 r{round} không ca → kế hoạch / chưa khai báo trống"); continue; }
                var caE = db.CaLamViecs.List.First(c => c.CaId == caId);
                if (caE.IsOvernight && g.Where(r => r.CaId == caId).All(r => TimeOnly.FromDateTime(r.StartTime) < caE.GioKetThuc))
                { Ok(d?.PlanMin == null, $"P2 r{round} đuôi ca đêm (dòng sau 0h) → không tính kế hoạch lần 2", $"{d?.PlanMin}"); continue; }
                var ws = g.Key.WorkDate.ToDateTime(caE.GioBatDau); var we = (caE.IsOvernight ? g.Key.WorkDate.AddDays(1) : g.Key.WorkDate).ToDateTime(caE.GioKetThuc);
                var brks = WorkMinutes.BreakSegments(caE, ws, we);
                var saved = res.Rows.Where(r => r.WorkerId == g.Key.WorkerId && r.StartTime < we && r.EndTime > ws).SelectMany(r => r.Breaks).ToList();
                int plan = 0, gap = 0;
                for (var m = ws; m < we; m = m.AddMinutes(1))
                {
                    if (brks.Any(b => m >= b.Start && m < b.End) || saved.Any(b => m >= b.BreakStart && m < b.BreakEnd)) continue;
                    plan++; if (!wMins[g.Key.WorkerId].Contains(m)) gap++;
                }
                Ok(d!.PlanMin == plan && d.UnaccountedMin == gap, $"P2 r{round} ca {caId}: kế hoạch {plan}, chưa khai báo {gap}", $"{d.PlanMin}/{d.UnaccountedMin}");
            }
            Ok(res.TotalMinutes == totAll && res.ProductiveMin == totPro, $"P2 r{round} tổng cũ (TotalMinutes / ProductiveMin) = đếm phút", $"{res.TotalMinutes}/{totAll} {res.ProductiveMin}/{totPro}");
        }
        Say(fails == bad0, $"P2 6 vòng × 50 người-ngày ngẫu nhiên (seed {seed}): chia phút / có ích / kế hoạch / chưa khai báo khớp đếm từng phút");
    }

    // ── P3 hiệu suất máy theo kỳ ───────────────────────────────────────────────────────────────────────────
    static async Task P3()
    {
        Console.WriteLine("[P3] G29-HSM-KY / G29-HSM-MAUSO — hiệu suất máy theo kỳ = cộng từng ngày; đếm từng phút");
        int seed = int.TryParse(Environment.GetEnvironmentVariable("RPT_SEED"), out var sd) ? sd + 7 : 29218;
        var rnd = new Random(seed);
        int bad0 = fails;
        for (int round = 0; round < 4; round++)
        {
            var db = await WtsScen.SeedAsync();
            var ca = AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
            for (int m = 1; m <= 3; m++) db.ThietBis.List.Add(new ThietBi { ThietBiId = m, SoMay = "M" + m, BoPhan = "GC", IsActive = true, TenMay = "Máy " + m });
            var used = new HashSet<DateOnly>();
            for (int k = 0; k < 40; k++)
            {
                var day = D1.AddDays(rnd.Next(0, 6));
                if (day.DayOfWeek == DayOfWeek.Sunday && rnd.Next(2) == 0) continue;
                var s = day.AddHours(7).AddMinutes(rnd.Next(0, 600)); var e = s.AddMinutes(10 + rnd.Next(0, 200));
                P(db, 2 + rnd.Next(3), "M" + rnd.Next(1, 4), "serial", s, e, rnd.Next(1, 30), rnd.Next(0, 3), "NC" + rnd.Next(1, 3), 9, rnd.Next(3) == 0 ? rnd.Next(0, 61) : 0);
                used.Add(DateOnly.FromDateTime(s));
            }
            // 1 việc không ra SP ngày không có WTS sản xuất → ngày đó vẫn được tính (máy rảnh cả ngày)
            var actDay = D1.AddDays(6); A(db, 4, "HOP", actDay.AddHours(9), actDay.AddHours(10)); used.Add(DateOnly.FromDateTime(actDay));
            // dừng máy
            int cid = 0;
            for (int k = 0; k < 4; k++)
            {
                var s = D1.AddDays(rnd.Next(0, 7)).AddHours(8 + rnd.Next(0, 8)); var e = s.AddMinutes(20 + rnd.Next(0, 300));
                db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = ++cid, ThietBiId = 1 + rnd.Next(3), FieldName = "TrangThai", NewValue = k % 2 == 0 ? "Hỏng" : "Bảo trì", Reason = k % 2 == 0 ? "Kẹt dao" : "bảo trì định kỳ",
                    ThoiGianBatDau = s, ThoiGianKetThuc = e, ChangedAt = s, ChangedBy = 1 });
            }
            db.Fixup();
            var f = DateOnly.FromDateTime(D1); var t = f.AddDays(6);
            var svc = new MachineEfficiencyService(db);
            var per = await svc.GetPeriodAsync(f, t, ca.GioBatDau, ca.GioKetThuc, ca, 1);
            // ngày tính = ngày có dòng / việc CHẠM khung ca của ngày đó (rà độc lập 21/11 Trung 2)
            var allIv = db.WtsProductionLogs.List.Where(x => !x.IsVoided).Select(x => (x.StartTime, x.EndTime))
                .Concat(db.WorkerActivityLogs.List.Where(x => !x.IsVoided).Select(x => (x.StartTime, x.EndTime))).ToList();
            var days = Enumerable.Range(0, 7).Select(i => f.AddDays(i))
                .Where(d => allIv.Any(x => x.StartTime < d.ToDateTime(ca.GioKetThuc) && x.EndTime > d.ToDateTime(ca.GioBatDau))).ToList();
            Ok(per.Error == null && per.Days.SequenceEqual(days), $"P3 r{round} ngày tính = ngày có WTS chạm khung ca (gồm ngày chỉ có việc không ra SP)", $"{string.Join(",", per.Days)} vs {string.Join(",", days)}");
            foreach (var row in per.Rows)
            {
                double run = 0, down = 0, brk = 0, idle = 0, tot = 0;
                foreach (var d in per.Days)
                {
                    var one = await svc.GetAsync(d, ca.GioBatDau, ca.GioKetThuc, ca, 1);
                    var r1 = one.Rows.First(x => x.Machine.ThietBiId == row.Machine.ThietBiId);
                    run += r1.RunMinutes; down += r1.DownMinutes; brk += r1.BreakMinutes; idle += r1.IdleMinutes; tot += r1.TotalMinutes;
                    var pd = row.PerDay.First(x => x.Day == d);
                    Ok(Math.Abs(pd.Run - r1.RunMinutes) < 0.01 && Math.Abs(pd.Down - r1.DownMinutes) < 0.01 && Math.Abs(pd.Idle - r1.IdleMinutes) < 0.01,
                        $"P3 r{round} {row.Machine.SoMay} {d}: ngày trong kỳ = trang ngày");
                }
                Ok(Math.Abs(row.RunMinutes - run) < 0.01 && Math.Abs(row.DownMinutes - down) < 0.01 && Math.Abs(row.IdleMinutes - idle) < 0.01 && Math.Abs(row.BreakMinutes - brk) < 0.01,
                    $"P3 r{round} {row.Machine.SoMay}: kỳ = Σ ngày", $"{row.RunMinutes}/{run}");
                Ok(Math.Abs(row.PlanMinutes - (row.RunMinutes + row.DownMinutes + row.IdleMinutes)) < 0.01 && Math.Abs(row.PlanMinutes - (tot - brk)) < 0.01,
                    $"P3 r{round} {row.Machine.SoMay}: chạy + dừng + rảnh = kế hoạch = khung − nghỉ", $"{row.PlanMinutes} vs {row.RunMinutes + row.DownMinutes + row.IdleMinutes}");
                Ok(row.PlanMinutes <= 0 || Math.Abs(row.RunPct - Math.Round(row.RunMinutes / row.PlanMinutes * 100, 1)) < 0.001, $"P3 r{round} % chạy trên kế hoạch");
                Ok(Math.Abs(row.Downs.Sum(x => x.Minutes) - row.DownMinutes) < 0.01, $"P3 r{round} {row.Machine.SoMay}: Σ các lần dừng = phút dừng");
                Ok(Math.Abs(row.ByPo.Sum(x => x.RunMinutes) - row.RunMinutes) < 0.01, $"P3 r{round} {row.Machine.SoMay}: Σ theo PO = phút chạy");
                // OK / NG: mỗi dòng 1 lần (dòng chạm khung ca của ngày được tính)
                var rows = db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.MachineUsed == row.Machine.SoMay
                    && per.Days.Any(d => x.StartTime < d.ToDateTime(ca.GioKetThuc) && x.EndTime > d.ToDateTime(ca.GioBatDau))).ToList();
                Ok(row.Ok == rows.Sum(x => x.QtyOk ?? 0) && row.Ng == rows.Sum(x => x.QtyNg ?? 0) && row.WorkerCount == rows.Select(x => x.WorkerId).Distinct().Count(),
                    $"P3 r{round} {row.Machine.SoMay}: OK / NG / số người — mỗi dòng 1 lần", $"{row.Ok}/{rows.Sum(x => x.QtyOk ?? 0)}");
                // người đứng máy: đếm từng phút (trong khung, ngoài nghỉ thật của dòng)
                foreach (var w in row.ByWorker)
                {
                    int cnt = 0;
                    foreach (var d in per.Days)
                    {
                        var ws = d.ToDateTime(ca.GioBatDau); var we = d.ToDateTime(ca.GioKetThuc);
                        var mine = rows.Where(x => x.WorkerId == w.WorkerId).ToList();
                        var brk2 = await WorkMinutesStore.ForWtsAsync(db, mine.Select(x => x.WtsLogId));
                        for (var m = ws; m < we; m = m.AddMinutes(1))
                            if (mine.Any(x => WorkMinutes.WorkPieces(x.StartTime, x.EndTime, WorkMinutesStore.Get(brk2, x.WtsLogId)).Any(pc => m >= pc.S && m < pc.E))) cnt++;
                    }
                    Ok(Math.Abs(w.Minutes - cnt) < 0.01, $"P3 r{round} {row.Machine.SoMay} người {w.WorkerId}: phút đứng máy = đếm phút {cnt}", $"{w.Minutes}");
                }
            }
        }
        Say(fails == bad0, $"P3 4 vòng ngẫu nhiên (seed {seed}): kỳ = Σ ngày, chạy + dừng + rảnh = kế hoạch, OK / NG mỗi dòng 1 lần, người đứng máy đếm phút");
    }

    // ── P4 quyền + lời báo ─────────────────────────────────────────────────────────────────────────────────
    static async Task P4()
    {
        Console.WriteLine("[P4] HSM-QUYEN / RPT-4 — không quyền → rỗng; khoảng sai → lời báo");
        var db = await WtsScen.SeedAsync();
        var ca = AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        P(db, 2, "M1", "serial", D1.AddHours(8), D1.AddHours(9), 5, 0, "NC1", 9);
        var gN = new UserGroup { GroupId = 77, GroupCode = "NOVIEW", IsActive = true, Permissions = FuzzPerm.Json(MES.Web.Constants.SystemPermissions.WtsGc) };
        db.UserGroups.List.Add(gN); db.Users.List.Add(new User { UserId = 77, Username = "nv", FullName = "Không xem", IsActive = true, Group = gN, GroupId = 77 });
        db.Fixup();
        var svc = new MachineEfficiencyService(db);
        var f = DateOnly.FromDateTime(D1);
        var no = await svc.GetPeriodAsync(f, f.AddDays(6), ca.GioBatDau, ca.GioKetThuc, ca, 77);
        var ad = await svc.GetPeriodAsync(f, f.AddDays(6), ca.GioBatDau, ca.GioKetThuc, ca, 1);
        Say(no.Rows.Count == 0 && no.Days.Count == 0 && ad.Rows.Count == 1 && ad.Days.Count == 1, "P4 máy: không quyền Xem → rỗng; admin → 1 máy 1 ngày", $"{no.Rows.Count}/{ad.Rows.Count}");
        var big = await svc.GetPeriodAsync(f, f.AddDays(100), ca.GioBatDau, ca.GioKetThuc, ca, 1);
        var rev = await svc.GetPeriodAsync(f.AddDays(3), f, ca.GioBatDau, ca.GioKetThuc, ca, 1);
        Say(big.Error != null && big.Rows.Count == 0 && rev.Error != null, "P4 máy: > 93 ngày / Từ > Đến → lời báo, không tính", $"{big.Error} | {rev.Error}");
        var rN = await new WtsReportService(db).GetReportAsync(f, f, null, 77);
        Say(rN.Rows.Count == 0 && rN.Days.Count == 0, "P4 WTS Report: không quyền → không dòng, không người-ngày");

        // P4b dòng vắt qua nửa đêm, xem "toàn ngày" 00:00–23:59: dòng chạm khung 2 ngày → OK / NG chỉ tính 1 lần; phút chạy chia đúng 2 ngày
        var db2 = await WtsScen.SeedAsync();
        db2.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        P(db2, 2, "M1", "serial", D1.AddHours(23), D2.AddHours(1), 7, 1, "NC1", null);
        P(db2, 3, "M1", "serial", D2.AddHours(8), D2.AddHours(9), 3, 0, "NC1", null);
        db2.Fixup();
        var pr = await new MachineEfficiencyService(db2).GetPeriodAsync(f, f.AddDays(1), new TimeOnly(0, 0), new TimeOnly(23, 59), null, 1);
        var m1 = pr.Rows.First();
        Say(pr.Days.Count == 2 && m1.Ok == 10 && m1.Ng == 1 && Math.Abs(m1.RunMinutes - 179) < 0.01 && m1.WorkerCount == 2,
            "P4b dòng 23:00 → 01:00 hôm sau: OK 7 + 3 = 10, NG 1 (mỗi dòng 1 lần), chạy 59 + 60 + 60 = 179 (khung 00:00–23:59), 2 người",
            $"ngày {pr.Days.Count} OK {m1.Ok} NG {m1.Ng} chạy {m1.RunMinutes} người {m1.WorkerCount}");
    }
}
