// [G29-SCEN 2026-11-21] Kịch bản `rpt` — Gói 29 báo cáo theo kỳ (chỉ đọc, không đổi số in/out):
//   P1 [G29-RPT]       bộ số cố định (giống tools/WtsHarness/Stubs/HarnessG29.cs) — đáp án TÍNH TAY: phút làm / có ích / vô ích / chia phút từng dòng /
//                      kế hoạch / chưa khai báo từng người-ngày; dòng việc vô ích chồng dòng sản xuất → 0 phút (có ích thắng).
//   P2 [G29-RPT]       ngẫu nhiên 300 người-ngày (dòng chồng, làm thông nghỉ, có / không ca, việc có ích / vô ích): so với đếm TỪNG PHÚT độc lập —
//                      Σ AllocMin = phút làm người-ngày = số cũ TotalMinutes; Σ dòng có ích = phút có ích; AllocMin ≤ phút làm của dòng;
//                      kế hoạch = phút khung ca ngoài nghỉ; chưa khai báo = phút kế hoạch không dòng nào phủ.
//   P3 [G29-HSM-KY][G29-HSM-MAUSO] hiệu suất máy theo kỳ = cộng từng ngày; OK / NG mỗi dòng 1 lần; người đứng máy = đếm từng phút;
//                      Chạy + Dừng + Rảnh = kế hoạch, % trên kế hoạch. [G31-DON-BO-KIEM 2026-11-23] đáp án đổi sang 24h (GetPeriod24Async / GetDay24Async —
//                      hàm theo ca GetAsync / GetPeriodAsync đã bỏ): ngày tính = mọi ngày lịch, kế hoạch = 24h, OK / NG theo ngày bắt đầu dòng.
//   P4 [HSM-QUYEN][RPT-4] không quyền → rỗng; khoảng > 366 ngày / Từ > Đến → lời báo (đáp án đổi: 24h, trước 93 ngày theo ca).
//   Nguồn: HÙNG 21/11 09:46 OK mockup WTS Report + Hiệu suất máy (câu B theo đề xuất CLAUDE).
//   [G30-SCEN 2026-11-21] P1 / P2 thêm giờ chuẩn BaseMin (= phút làm + chưa khai báo) + phút làm thông nghỉ của người-ngày; P2 ca dòng việc không ra SP
//   lấy từ khoảng nghỉ đã lưu; P1c [G30-RPT-MAUSO] đáp án tính tay: ngày chỉ có việc không ra SP chạm nghỉ ca → có ca, giờ chuẩn 480;
//   không chạm nghỉ → không rõ ca, giờ chuẩn = phút khai báo; làm thông nghỉ 60 → giờ chuẩn 540 (> ca 480), % không vượt 100.
//   P1d [G30-RPT-MAUSO] rà độc lập 21/11: ca đêm ngày CUỐI kỳ — dòng sau 0h (ngoài kỳ) vẫn phủ chưa khai báo; ngày đuôi ca đêm = có ca (IsTail), không "không rõ ca".
//   Nguồn: HÙNG 21/11 16:03 + 16:10 + 16:19 "ok làm đi".
//   [G31-SCEN 2026-11-22] P5 [G31-HSM-24H] máy 24h / ngày, không ca — đáp án tính tay (chạy / dừng / rảnh lúc xưởng có người / xưởng không ai khai
//   WTS, % 24h, % lúc có người, kỳ = mọi ngày lịch, Năm ≤ 366 ngày); P6 ngẫu nhiên đếm TỪNG PHÚT (4 phần, kỳ = Σ ngày, ngày tràn 0h);
//   P7 [G31-LOC-NHOM] nhóm công nhân trên dòng WTS Report. Nguồn: HÙNG 22/11 08:58 + 09:03 + 11:58.
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
        await P1(); await P1b(); await P1c(); await P1d(); await P2(); await P3(); await P4(); await P5(); await P5b(); await P6(); await P7();
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
        Say(dA1.BaseMin == 480 && dB1.BaseMin == 480 && dA2.BaseMin == 480, "P1 [G30] giờ chuẩn mỗi người-ngày = 480 (A1 330 + 150, B1 240 + 240, A2 480 + 0)",
            $"{dA1.BaseMin}/{dB1.BaseMin}/{dA2.BaseMin}");
        var hl = res.Rows.Where(x => x.WorkerId == 2 && x.TaskKey == "GC|serial").ToList();
        Say(hl.Count == 4 && hl.Sum(x => x.AllocMin) == 660 && hl.All(x => x.TaskLabel == "Gia công · Hàng loạt") && res.Rows.Count(x => x.TaskKey == "GC|trial") == 1
            && res.Rows.First(x => x.ActivityLogId == a1.ActivityLogId).TaskKey == "ACT|HOP29" && res.Rows.First(x => x.ActivityLogId == a1.ActivityLogId).GroupLabel == "Việc không ra SP",
            "P1 việc: A Hàng loạt 4 dòng 660; Chạy thử 1 dòng; HOP29 nhóm Việc không ra SP", $"{hl.Count}/{hl.Sum(x => x.AllocMin)}");
    }

    // ── P1c [G30-RPT-MAUSO] giờ chuẩn theo ca: ca từ nghỉ đã lưu, không rõ ca, làm thông nghỉ ──────────────────────
    static async Task P1c()
    {
        Console.WriteLine("[P1c] G30-RPT-MAUSO — giờ chuẩn theo ca (đáp án tính tay)");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        Task(db, "SUA30", true); Task(db, "HOP30", false);
        A(db, 4, "SUA30", D1.AddHours(8), D1.AddHours(13), 9);              // chỉ việc không ra SP, chạm nghỉ trưa (đã lưu ca 9) → làm 08–12 = 240
        A(db, 5, "HOP30", D1.AddHours(9), D1.AddHours(10), 9);              // không chạm nghỉ → không có khoảng nghỉ đã lưu → không rõ ca
        P(db, 2, "M30", "serial", D1.AddHours(8), D1.AddHours(17), 80, 0, "NC01", 9, 60);   // làm thông nghỉ trưa 60
        AddCa(db, 7, new TimeOnly(7, 0), new TimeOnly(16, 0), (11, 0, 12, 0));
        A(db, 3, "SUA30", D1.AddHours(7), D1.AddHours(12), 7);              // ca 7 CHỈ có trên dòng việc không ra SP (không dòng sản xuất nào trong kỳ dùng) → vẫn nạp ca
        db.Fixup();
        var res = await new WtsReportService(db).GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        WtsReportDay Dd(int w) => res.Days.First(d => d.WorkerId == w);
        var a = Dd(4); var b = Dd(5); var c = Dd(2); var e = Dd(3);
        Say(a.CaId == 9 && a.WorkMin == 240 && a.PlanMin == 480 && a.UnaccountedMin == 240 && a.BaseMin == 480,
            "P1c chỉ việc không ra SP chạm nghỉ: ca lấy từ nghỉ đã lưu → kế hoạch 480, chưa khai báo 240, giờ chuẩn 480 (trước: không ca)",
            $"ca {a.CaId} làm {a.WorkMin} kh {a.PlanMin} ckb {a.UnaccountedMin} chuẩn {a.BaseMin}");
        Say(e.CaId == 7 && e.WorkMin == 240 && e.PlanMin == 480 && e.UnaccountedMin == 240 && e.BaseMin == 480,
            "P1c ca chỉ có trên dòng việc không ra SP (07–16, nghỉ 11–12): vẫn nạp ca → kế hoạch 480, chưa khai báo 240, giờ chuẩn 480",
            $"ca {e.CaId} làm {e.WorkMin} kh {e.PlanMin} ckb {e.UnaccountedMin} chuẩn {e.BaseMin}");
        A(db, 3, "SUA30", D2.AddHours(7), D2.AddHours(12), 7);                                   // 240 phút, nghỉ ca 7
        A(db, 3, "HOP30", D2.AddHours(11) + TimeSpan.FromMinutes(55), D2.AddHours(12) + TimeSpan.FromMinutes(5), 9);   // 2 dòng ngắn chạm nghỉ ca 9
        A(db, 3, "HOP30", D2.AddHours(12) + TimeSpan.FromMinutes(55), D2.AddHours(13) + TimeSpan.FromMinutes(5), 9);
        db.Fixup();
        var r2 = await new WtsReportService(db).GetReportAsync(DateOnly.FromDateTime(D2), DateOnly.FromDateTime(D2), null, 1);
        var f = r2.Days.First(d => d.WorkerId == 3);
        Say(f.CaId == 7, "P1c ca dự phòng theo PHÚT (1 dòng 240 phút ca 7 thắng 2 dòng ngắn ca 9; rà độc lập Thấp 3)", $"ca {f.CaId}");
        Say(b.CaId == null && b.PlanMin == null && b.UnaccountedMin == null && b.WorkMin == 60 && b.BaseMin == 60,
            "P1c không chạm nghỉ → không rõ ca: giờ chuẩn = phút khai báo 60", $"ca {b.CaId} kh {b.PlanMin} ckb {b.UnaccountedMin} chuẩn {b.BaseMin}");
        Say(c.WorkMin == 540 && c.PlanMin == 480 && c.UnaccountedMin == 0 && c.BaseMin == 540 && c.BreakWorkedMin == 60 && c.ProductiveMin <= c.BaseMin,
            "P1c làm thông nghỉ 60: làm 540, kế hoạch 480, chưa khai báo 0 → giờ chuẩn 540, có ích 540 = 100% (không vượt)",
            $"làm {c.WorkMin} kh {c.PlanMin} ckb {c.UnaccountedMin} chuẩn {c.BaseMin} thông {c.BreakWorkedMin}");
    }

    // ── P1d [G30-RPT-MAUSO] ca đêm ngày cuối kỳ + ngày đuôi ca ───────────────────────────────────────────────
    static async Task P1d()
    {
        Console.WriteLine("[P1d] G30-RPT-MAUSO — ca đêm 22:00–06:00 ở ngày cuối kỳ; ngày đuôi ca đêm");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 6, new TimeOnly(22, 0), new TimeOnly(6, 0));
        P(db, 2, "M31", "serial", D1.AddHours(22), D2.AddHours(2), 10, 0, "NC01", 6);   // ngày 1 22:00 → 02:00 (240)
        P(db, 2, "M31", "serial", D2.AddHours(2), D2.AddHours(6), 10, 0, "NC01", 6);    // bắt đầu ngày 2 02:00 → 06:00 (240)
        db.Fixup();
        var svc = new WtsReportService(db);
        var one = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        var d1 = one.Days.Single();
        Say(one.Rows.Count == 1 && d1.WorkMin == 240 && d1.PlanMin == 480 && d1.UnaccountedMin == 0 && d1.BaseMin == 240,
            "P1d xem riêng ngày 1 (cuối kỳ): dòng 02–06 ngày 2 (ngoài kỳ) vẫn phủ → chưa khai báo 0, giờ chuẩn 240 = 100% (trước: chưa khai báo 240 giả)",
            $"dòng {one.Rows.Count} làm {d1.WorkMin} kh {d1.PlanMin} ckb {d1.UnaccountedMin} chuẩn {d1.BaseMin}");
        var two = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D2), null, 1);
        var a = two.Days.First(d => d.Day == DateOnly.FromDateTime(D1)); var b = two.Days.First(d => d.Day == DateOnly.FromDateTime(D2));
        Say(a.UnaccountedMin == 0 && a.BaseMin == 240 && b.IsTail && b.CaId == 6 && b.UnaccountedMin == null && b.BaseMin == 240 && !a.IsTail,
            "P1d xem 2 ngày: ngày 1 giống xem riêng (chưa KB 0); ngày 2 = đuôi ca đêm (có ca 6, không kế hoạch riêng), giờ chuẩn 240",
            $"D1 ckb {a.UnaccountedMin} chuẩn {a.BaseMin} · D2 đuôi {b.IsTail} ca {b.CaId} ckb {b.UnaccountedMin} chuẩn {b.BaseMin}");
    }


    // ── P5 [G31-HSM-24H] máy 24h — đáp án tính tay ───────────────────────────────────────────────────────────
    static void Down(AppDbContext db, int machineId, DateTime s, DateTime? e, bool maint, ref int cid)
        => db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = ++cid, ThietBiId = machineId, FieldName = "TrangThai", NewValue = maint ? "Bảo trì" : "Hỏng",
            Reason = maint ? "bảo trì" : "Kẹt dao", ThoiGianBatDau = s, ThoiGianKetThuc = e, ChangedAt = s, ChangedBy = 1 });
    static async Task P5()
    {
        Console.WriteLine("[P5] G31-HSM-24H — máy 24h / ngày, không ca (đáp án tính tay)");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true, TenMay = "Máy 1" });
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M2", BoPhan = "TARO", IsActive = true, TenMay = "Máy 2" });
        P(db, 2, "M1", "serial", D1.AddHours(8), D1.AddHours(12), 10, 0, "NC01", 9);          // M1 chạy 08–12 (240)
        A(db, 3, "HOP", D1.AddHours(7), D1.AddHours(17));                                     // người khác 07–17 → xưởng có người 07–17 (600)
        int cid = 0; Down(db, 1, D1.AddHours(13), D1.AddHours(14), false, ref cid);           // M1 hỏng 13–14 (60, lúc xưởng có người)
        P(db, 2, "M1", "serial", D1.AddHours(22), D2.AddHours(2), 5, 0, "NC01", 9);           // M1 chạy 22:00 → 02:00 hôm sau (tràn 0h): D1 120, D2 120
        db.Fixup();
        var svc = new MachineEfficiencyService(db);
        var d1 = await svc.GetDay24Async(DateOnly.FromDateTime(D1), 1);
        var m1 = d1.Rows.First(x => x.Machine.SoMay == "M1"); var m2 = d1.Rows.First(x => x.Machine.SoMay == "M2");
        // M1 D1: chạy 240 + 120 = 360; dừng 60; xưởng có người = 07–17 ∪ 22–24 = 720 → rảnh có người = 720 − 360 − 60 = 300; không ai = 1440 − 720 = 720
        Say(Math.Abs(m1.TotalMinutes - 1440) < 0.01 && Math.Abs(m1.RunMinutes - 360) < 0.01 && Math.Abs(m1.DownMinutes - 60) < 0.01
            && Math.Abs(m1.IdleCrewMinutes - 300) < 0.01 && Math.Abs(m1.NoCrewMinutes - 720) < 0.01 && m1.BreakMinutes == 0,
            "P5 M1 ngày 1: 24h = chạy 360 + dừng 60 + rảnh lúc có người 300 + không ai khai 720 (không nghỉ ca)",
            $"tổng {m1.TotalMinutes} chạy {m1.RunMinutes} dừng {m1.DownMinutes} rảnh có người {m1.IdleCrewMinutes} không ai {m1.NoCrewMinutes} nghỉ {m1.BreakMinutes}");
        Say(Math.Abs(m1.RunPct - 25.0) < 0.001 && Math.Abs(m1.RunPctCrew - 50.0) < 0.001 && Math.Abs(m1.NoCrewPct - 50.0) < 0.001,
            "P5 M1 ngày 1: % chạy 24h 25,0; % lúc xưởng có người 360 / 720 = 50,0", $"{m1.RunPct} / {m1.RunPctCrew} / {m1.NoCrewPct}");
        // M2 không ai đứng: rảnh có người 720, không ai 720
        Say(m2.RunMinutes == 0 && Math.Abs(m2.IdleCrewMinutes - 720) < 0.01 && Math.Abs(m2.NoCrewMinutes - 720) < 0.01,
            "P5 M2 không ai đứng: rảnh lúc xưởng có người 720, không ai khai 720", $"{m2.IdleCrewMinutes}/{m2.NoCrewMinutes}");
        Say(m1.Segments.Any(s => s.Type == SegmentType.NoCrew && s.Start == D1 && s.End == D1.AddHours(7)) && m1.Segments.Any(s => s.Type == SegmentType.Idle && s.Start == D1.AddHours(7) && s.End == D1.AddHours(8)),
            "P5 dòng thời gian M1: 00–07 không ai khai, 07–08 rảnh lúc có người", string.Join(" ", m1.Segments.Select(s => $"{s.Type}:{s.Start:HH:mm}-{s.End:HH:mm}")));
        // kỳ 3 ngày (D1, D2, D3 không WTS) = mọi ngày lịch
        var per = await svc.GetPeriod24Async(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1).AddDays(2), 1);
        var p1 = per.Rows.First(x => x.Machine.SoMay == "M1");
        // D2: chạy 00–02 = 120, xưởng có người 00–02 = 120 → rảnh có người 0, không ai 1320; D3: không ai 1440
        Say(per.Error == null && per.Days.Count == 3 && Math.Abs(p1.TotalMinutes - 4320) < 0.01 && Math.Abs(p1.RunMinutes - 480) < 0.01
            && Math.Abs(p1.NoCrewMinutes - (720 + 1320 + 1440)) < 0.01 && Math.Abs(p1.IdleCrewMinutes - 300) < 0.01 && p1.PerDay.Count == 3,
            "P5 kỳ 3 ngày (có ngày không WTS): tổng 4320, chạy 480, không ai 3480, rảnh có người 300 — ngày không WTS vẫn tính", $"ngày {per.Days.Count} tổng {p1.TotalMinutes} chạy {p1.RunMinutes} không ai {p1.NoCrewMinutes} rảnh {p1.IdleCrewMinutes}");
        Say(p1.Ok == 15 && Math.Abs(p1.RunPctCrew - Math.Round(480.0 / (4320 - 3480) * 100, 1)) < 0.001, "P5 kỳ: OK mỗi dòng 1 lần (10 + 5), % lúc có người 480 / 840", $"{p1.Ok} {p1.RunPctCrew}");
        var y = await svc.GetPeriod24Async(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), 1);
        var big = await svc.GetPeriod24Async(new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), 1);
        var no = await svc.GetPeriod24Async(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), 0);
        var noD = await svc.GetDay24Async(DateOnly.FromDateTime(D1), 0);
        Say(y.Error == null && y.Days.Count == 366 && big.Error != null && no.Rows.Count == 0 && noD.Rows.Count == 0,
            "P5 Năm 2024 = 366 ngày được; 367 ngày → lời báo; không quyền → rỗng", $"{y.Error} {y.Days.Count} | {big.Error} | {no.Rows.Count}/{noD.Rows.Count}");
    }

    // ── P5b [G31-HSM-24H] rà độc lập 22/11: không tính thời gian chưa tới; OK / NG theo ngày bắt đầu dòng ────────
    static async Task P5b()
    {
        Console.WriteLine("[P5b] G31-HSM-24H — giờ hiện tại cắt ngày; hỏng chưa đóng; OK / NG ở kỳ chứa ngày bắt đầu dòng");
        var db = await WtsScen.SeedAsync();
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true, TenMay = "Máy 1" });
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M2", BoPhan = "GC", IsActive = true, TenMay = "Máy 2" });
        P(db, 2, "M1", "serial", D1.AddHours(8), D1.AddHours(12), 10, 0, "NC01", null);       // 08–12
        A(db, 3, "HOP", D1.AddHours(7), D1.AddHours(17));                                     // xưởng có người 07–17
        int cid = 0; Down(db, 2, D1.AddHours(10), null, false, ref cid);                      // M2 hỏng từ 10:00, CHƯA đóng
        P(db, 2, "M1", "serial", D1.AddHours(22), D2.AddHours(2), 5, 0, "NC01", null);        // bắt đầu ngày 1, tràn sang ngày 2
        db.Fixup();
        var svc = new MachineEfficiencyService(db) { Clock = () => D1.AddHours(12) };       // "bây giờ" = ngày 1 12:00
        var d1 = await svc.GetDay24Async(DateOnly.FromDateTime(D1), 1);
        var m1 = d1.Rows.First(x => x.Machine.SoMay == "M1"); var m2 = d1.Rows.First(x => x.Machine.SoMay == "M2");
        Say(Math.Abs(m1.TotalMinutes - 720) < 0.01 && Math.Abs(m1.RunMinutes - 240) < 0.01 && Math.Abs(m1.IdleCrewMinutes - 60) < 0.01 && Math.Abs(m1.NoCrewMinutes - 420) < 0.01,
            "P5b hôm nay lúc 12:00: tổng 720 (không tính 12–24h), chạy 240, rảnh có người 07–08 = 60, không ai 00–07 = 420", $"{m1.TotalMinutes} {m1.RunMinutes} {m1.IdleCrewMinutes} {m1.NoCrewMinutes}");
        Say(Math.Abs(m2.DownMinutes - 120) < 0.01, "P5b hỏng chưa đóng từ 10:00 → dừng 120 tới bây giờ (trước: tới 24:00 = 840)", $"{m2.DownMinutes}");
        var per = await svc.GetPeriod24Async(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1).AddDays(6), 1);
        var p2 = per.Rows.First(x => x.Machine.SoMay == "M2");
        Say(per.Days.Count == 1 && Math.Abs(p2.DownMinutes - 120) < 0.01 && Math.Abs(p2.TotalMinutes - 720) < 0.01,
            "P5b kỳ 7 ngày mà hôm nay là ngày 1: chỉ tính 1 ngày tới 12:00; hỏng chưa đóng không cộng ngày tương lai", $"ngày {per.Days.Count} dừng {p2.DownMinutes} tổng {p2.TotalMinutes}");
        var svc2 = new MachineEfficiencyService(db) { Clock = () => D1.AddDays(5) };
        var w1 = (await svc2.GetPeriod24Async(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), 1)).Rows.First(x => x.Machine.SoMay == "M1");
        var w2 = (await svc2.GetPeriod24Async(DateOnly.FromDateTime(D2), DateOnly.FromDateTime(D2), 1)).Rows.First(x => x.Machine.SoMay == "M1");
        Say(w1.Ok == 15 && w2.Ok == 0 && Math.Abs(w2.RunMinutes - 120) < 0.01,
            "P5b dòng 22:00 → 02:00: OK vào kỳ chứa ngày bắt đầu (ngày 1: 10 + 5), ngày 2 OK 0 nhưng vẫn có 120 phút chạy (trước: cộng OK ở cả 2 kỳ)", $"{w1.Ok} / {w2.Ok} / {w2.RunMinutes}");
    }

    // ── P6 [G31-HSM-24H] ngẫu nhiên — đếm từng phút ─────────────────────────────────────────────────────────
    static async Task P6()
    {
        Console.WriteLine("[P6] G31-HSM-24H — ngẫu nhiên, đếm từng phút 4 phần, kỳ = Σ ngày");
        int seed = int.TryParse(Environment.GetEnvironmentVariable("RPT_SEED"), out var sd) ? sd + 31 : 29231;
        var rnd = new Random(seed);
        int bad0 = fails;
        for (int round = 0; round < 3; round++)
        {
            var db = await WtsScen.SeedAsync();
            AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
            for (int m = 1; m <= 3; m++) db.ThietBis.List.Add(new ThietBi { ThietBiId = m, SoMay = "M" + m, BoPhan = m == 3 ? "TARO" : "GC", IsActive = true, TenMay = "Máy " + m });
            for (int k = 0; k < 35; k++)
            {
                var s = D1.AddDays(rnd.Next(0, 4)).AddMinutes(rnd.Next(0, 1440)); var e = s.AddMinutes(10 + rnd.Next(0, 400));
                int? ca = rnd.Next(2) == 0 ? 9 : null; int worked = rnd.Next(3) == 0 ? rnd.Next(0, 61) : 0;
                if (rnd.Next(4) == 0) A(db, 2 + rnd.Next(4), "HOP", s, e, ca, worked);
                else P(db, 2 + rnd.Next(4), rnd.Next(5) == 0 ? null : "M" + rnd.Next(1, 4), "serial", s, e, rnd.Next(1, 20), rnd.Next(0, 2), "NC1", ca, worked);
            }
            int cid = 0;
            for (int k = 0; k < 5; k++) { var s = D1.AddDays(rnd.Next(0, 4)).AddMinutes(rnd.Next(0, 1440)); Down(db, 1 + rnd.Next(3), s, rnd.Next(6) == 0 ? null : s.AddMinutes(20 + rnd.Next(0, 300)), k % 2 == 1, ref cid); }
            db.Fixup();
            var svc = new MachineEfficiencyService(db);
            var f = DateOnly.FromDateTime(D1); var t = f.AddDays(3);
            var per = await svc.GetPeriod24Async(f, t, 1);
            // phút làm (WorkPieces) từng dòng — đếm độc lập
            bool IsWork(DateTime s, DateTime e, IReadOnlyList<LineBreak> brk, DateTime m)
            {
                if (m < s || m >= e) return false;
                foreach (var b in brk)
                {
                    if (m < b.BreakStart || m >= b.BreakEnd) continue;
                    var cs = b.BreakStart > s ? b.BreakStart : s;
                    return b.WorkedMin > 0 && m >= cs && m < cs.AddMinutes(b.WorkedMin);
                }
                return true;
            }
            var bw = await WorkMinutesStore.ForWtsAsync(db, db.WtsProductionLogs.List.Select(x => x.WtsLogId));
            var ba = await WorkMinutesStore.ForActivityAsync(db, db.WorkerActivityLogs.List.Select(x => x.ActivityLogId));
            foreach (var d in Enumerable.Range(0, 4).Select(i => f.AddDays(i)))
            {
                var day = await svc.GetDay24Async(d, 1);
                var s0 = d.ToDateTime(TimeOnly.MinValue);
                foreach (var row in day.Rows)
                {
                    int run = 0, down = 0, idle = 0, nc = 0;
                    for (int i = 0; i < 1440; i++)
                    {
                        var m = s0.AddMinutes(i);
                        bool dn = db.ThietBiChangeLogs.List.Any(x => x.ThietBiId == row.Machine.ThietBiId && x.ThoiGianBatDau <= m && (x.ThoiGianKetThuc == null || x.ThoiGianKetThuc > m));
                        bool rn = db.WtsProductionLogs.List.Any(x => !x.IsVoided && x.MachineUsed == row.Machine.SoMay && IsWork(x.StartTime, x.EndTime, WorkMinutesStore.Get(bw, x.WtsLogId), m));
                        bool cr = db.WtsProductionLogs.List.Any(x => !x.IsVoided && IsWork(x.StartTime, x.EndTime, WorkMinutesStore.Get(bw, x.WtsLogId), m))
                               || db.WorkerActivityLogs.List.Any(x => !x.IsVoided && IsWork(x.StartTime, x.EndTime, WorkMinutesStore.Get(ba, x.ActivityLogId), m));
                        if (dn) down++; else if (rn) run++; else if (cr) idle++; else nc++;
                    }
                    Ok(Math.Abs(row.RunMinutes - run) < 0.01 && Math.Abs(row.DownMinutes - down) < 0.01 && Math.Abs(row.IdleCrewMinutes - idle) < 0.01 && Math.Abs(row.NoCrewMinutes - nc) < 0.01 && Math.Abs(row.TotalMinutes - 1440) < 0.01,
                        $"P6 r{round} {row.Machine.SoMay} {d}: chạy / dừng / rảnh có người / không ai = đếm phút {run}/{down}/{idle}/{nc}",
                        $"{row.RunMinutes}/{row.DownMinutes}/{row.IdleCrewMinutes}/{row.NoCrewMinutes}");
                    var pd = per.Rows.First(x => x.Machine.ThietBiId == row.Machine.ThietBiId).PerDay.First(x => x.Day == d);
                    Ok(Math.Abs(pd.Run - row.RunMinutes) < 0.01 && Math.Abs(pd.NoCrew - row.NoCrewMinutes) < 0.01 && Math.Abs(pd.IdleCrew - row.IdleCrewMinutes) < 0.01,
                        $"P6 r{round} {row.Machine.SoMay} {d}: ngày trong kỳ = trang ngày");
                }
            }
            foreach (var r in per.Rows)
                Ok(Math.Abs(r.RunMinutes + r.DownMinutes + r.IdleCrewMinutes + r.NoCrewMinutes - r.TotalMinutes) < 0.01 && Math.Abs(r.TotalMinutes - 4 * 1440) < 0.01
                   && Math.Abs(r.RunMinutes - r.PerDay.Sum(x => x.Run)) < 0.01 && Math.Abs(r.NoCrewMinutes - r.PerDay.Sum(x => x.NoCrew)) < 0.01,
                    $"P6 r{round} {r.Machine.SoMay}: 4 phần = 24h × 4 ngày, kỳ = Σ ngày");
        }
        Say(fails == bad0, $"P6 3 vòng ngẫu nhiên (seed {seed}): 4 phần mỗi máy-ngày khớp đếm từng phút; kỳ = Σ ngày");
    }

    // ── P7 [G31-LOC-NHOM] nhóm công nhân trên dòng báo cáo ─────────────────────────────────────────────────
    static async Task P7()
    {
        Console.WriteLine("[P7] G31-LOC-NHOM — nhóm của công nhân trên dòng WTS Report");
        var db = await WtsScen.SeedAsync();
        AddCa(db, 9, new TimeOnly(8, 0), new TimeOnly(17, 0), (12, 0, 13, 0));
        var g1 = new UserGroup { GroupId = 901, GroupCode = "G901", GroupName = "Nhóm thử 1", IsActive = true }; db.UserGroups.List.Add(g1);
        var u2 = db.Users.List.First(u => u.UserId == 2); var u3 = db.Users.List.First(u => u.UserId == 3);
        u2.GroupId = 901; u3.GroupId = null;
        P(db, 2, "M1", "serial", D1.AddHours(8), D1.AddHours(10), 1, 0, "NC01", 9);
        A(db, 3, "HOP", D1.AddHours(8), D1.AddHours(9));
        db.Fixup();
        var res = await new WtsReportService(db).GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        var r2 = res.Rows.First(r => r.WorkerId == 2); var r3 = res.Rows.First(r => r.WorkerId == 3);
        Say(r2.WorkerGroupId == 901 && r2.WorkerGroupName == "Nhóm thử 1" && r3.WorkerGroupId == null && r3.WorkerGroupName == "(chưa có nhóm)",
            "P7 dòng mang nhóm của công nhân (Users.GroupId → UserGroups); không nhóm → \"(chưa có nhóm)\"", $"{r2.WorkerGroupId} {r2.WorkerGroupName} · {r3.WorkerGroupId} {r3.WorkerGroupName}");
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
        // [G31-DON-BO-KIEM 2026-11-23] đáp án đổi: máy tính 24h (hàm theo ca đã bỏ) — mọi ngày lịch là ngày tính, phút chia đúng ngày lịch
        var pr = await new MachineEfficiencyService(db2) { Clock = () => D1.AddDays(30) }.GetPeriod24Async(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D2), 1);
        var pm1 = pr.Rows.First();
        Say(pr.Days.Count == 2 && Math.Abs(pm1.PerDay.First(x => x.Day == DateOnly.FromDateTime(D1)).Run - 180) < 0.01 && Math.Abs(pm1.PerDay.First(x => x.Day == DateOnly.FromDateTime(D2)).Run - 90) < 0.01 && pm1.Ok == 15,
            "P1b máy (24h): ngày D1 chạy 60 + 120 = 180, ngày sau 90 (00:30–02:00); OK 3 dòng = 15", $"{string.Join(",", pr.Days)} · {string.Join(",", pm1.PerDay.Select(x => x.Run))} · {pm1.Ok}");
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
                var normal = new HashSet<DateTime>();   // [G30] phút ngoài nghỉ của ít nhất 1 dòng → làm thông nghỉ = phút làm − phút ngoài nghỉ
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
                        if (!inBrk) normal.Add(m);
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
                Ok(d != null && d.BreakWorkedMin == dAll - normal.Count, $"P2 r{round} [G30] làm thông nghỉ người-ngày = đếm phút {dAll - normal.Count}", $"{d?.BreakWorkedMin}");
                // kế hoạch / chưa khai báo: ca = ca nhiều phút nhất trên dòng sản xuất; [G30] không có → ca khoảng nghỉ đã lưu nhiều dòng nhất
                var caId = g.Where(r => r.CaId != null).GroupBy(r => r.CaId!.Value).Select(x => (x.Key, x.Sum(r => r.Minutes))).OrderByDescending(x => x.Item2).ThenBy(x => x.Key).Select(x => (int?)x.Key).FirstOrDefault();
                int? BrkCa(WtsReportRow r) => r.Breaks.Select(b => b.CaId).FirstOrDefault(c => c != null);
                caId ??= g.Where(r => BrkCa(r) != null).GroupBy(r => BrkCa(r)!.Value).Select(x => (x.Key, x.Sum(r => r.Minutes))).OrderByDescending(x => x.Item2).ThenBy(x => x.Key).Select(x => (int?)x.Key).FirstOrDefault();
                if (caId == null) { Ok(d?.PlanMin == null && d?.UnaccountedMin == null && d?.BaseMin == dAll, $"P2 r{round} không ca → kế hoạch / chưa khai báo trống, giờ chuẩn = phút làm"); continue; }
                var caE = db.CaLamViecs.List.First(c => c.CaId == caId);
                if (caE.IsOvernight && g.Where(r => (r.CaId ?? BrkCa(r)) == caId).All(r => TimeOnly.FromDateTime(r.StartTime) < caE.GioKetThuc))
                { Ok(d?.PlanMin == null && d?.BaseMin == dAll && d?.IsTail == true && d?.CaId == caId, $"P2 r{round} đuôi ca đêm (dòng sau 0h) → không tính kế hoạch lần 2, giờ chuẩn = phút làm, có ca (đuôi)", $"{d?.PlanMin}/{d?.BaseMin}/{d?.IsTail}"); continue; }
                var ws = g.Key.WorkDate.ToDateTime(caE.GioBatDau); var we = (caE.IsOvernight ? g.Key.WorkDate.AddDays(1) : g.Key.WorkDate).ToDateTime(caE.GioKetThuc);
                var brks = WorkMinutes.BreakSegments(caE, ws, we);
                var saved = res.Rows.Where(r => r.WorkerId == g.Key.WorkerId && r.StartTime < we && r.EndTime > ws).SelectMany(r => r.Breaks).ToList();
                int plan = 0, gap = 0;
                for (var m = ws; m < we; m = m.AddMinutes(1))
                {
                    if (brks.Any(b => m >= b.Start && m < b.End) || saved.Any(b => m >= b.BreakStart && m < b.BreakEnd)) continue;
                    plan++; if (!wMins[g.Key.WorkerId].Contains(m)) gap++;
                }
                Ok(!d!.IsTail && d.CaId == caId, $"P2 r{round} ngày có kế hoạch: ca {caId}, không phải đuôi", $"{d.CaId}/{d.IsTail}");
                Ok(d!.PlanMin == plan && d.UnaccountedMin == gap, $"P2 r{round} ca {caId}: kế hoạch {plan}, chưa khai báo {gap}", $"{d.PlanMin}/{d.UnaccountedMin}");
                Ok(d.BaseMin == dAll + gap && d.ProductiveMin <= d.BaseMin, $"P2 r{round} [G30] giờ chuẩn = làm {dAll} + chưa khai báo {gap}", $"{d.BaseMin}");
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
            var svc = new MachineEfficiencyService(db) { Clock = () => D1.AddDays(30) };   // [G31-DON-BO-KIEM] 24h; đồng hồ sau kỳ (không cắt giờ chưa tới)
            var per = await svc.GetPeriod24Async(f, t, 1);
            var days = Enumerable.Range(0, 7).Select(i => f.AddDays(i)).ToList();   // 24h: mọi ngày lịch
            Ok(per.Error == null && per.Days.SequenceEqual(days), $"P3 r{round} ngày tính = mọi ngày lịch trong kỳ (24h)", $"{string.Join(",", per.Days)} vs {string.Join(",", days)}");
            foreach (var row in per.Rows)
            {
                double run = 0, down = 0, brk = 0, idle = 0, tot = 0;
                foreach (var d in per.Days)
                {
                    var one = await svc.GetDay24Async(d, 1);
                    var r1 = one.Rows.First(x => x.Machine.ThietBiId == row.Machine.ThietBiId);
                    run += r1.RunMinutes; down += r1.DownMinutes; brk += r1.BreakMinutes; idle += r1.IdleMinutes; tot += r1.TotalMinutes;
                    var pd = row.PerDay.First(x => x.Day == d);
                    Ok(Math.Abs(pd.Run - r1.RunMinutes) < 0.01 && Math.Abs(pd.Down - r1.DownMinutes) < 0.01 && Math.Abs(pd.Idle - r1.IdleMinutes) < 0.01,
                        $"P3 r{round} {row.Machine.SoMay} {d}: ngày trong kỳ = trang ngày");
                }
                Ok(Math.Abs(row.RunMinutes - run) < 0.01 && Math.Abs(row.DownMinutes - down) < 0.01 && Math.Abs(row.IdleMinutes - idle) < 0.01 && Math.Abs(row.BreakMinutes - brk) < 0.01,
                    $"P3 r{round} {row.Machine.SoMay}: kỳ = Σ ngày", $"{row.RunMinutes}/{run}");
                Ok(Math.Abs(row.PlanMinutes - (row.RunMinutes + row.DownMinutes + row.IdleMinutes)) < 0.01 && Math.Abs(row.PlanMinutes - (tot - brk)) < 0.01 && brk == 0 && Math.Abs(tot - 1440 * days.Count) < 0.01,
                    $"P3 r{round} {row.Machine.SoMay}: chạy + dừng + rảnh = kế hoạch = 24h × số ngày (không nghỉ ca)", $"{row.PlanMinutes} vs {row.RunMinutes + row.DownMinutes + row.IdleMinutes}");
                Ok(row.PlanMinutes <= 0 || Math.Abs(row.RunPct - Math.Round(row.RunMinutes / row.PlanMinutes * 100, 1)) < 0.001, $"P3 r{round} % chạy trên kế hoạch");
                Ok(Math.Abs(row.Downs.Sum(x => x.Minutes) - row.DownMinutes) < 0.01, $"P3 r{round} {row.Machine.SoMay}: Σ các lần dừng = phút dừng");
                Ok(Math.Abs(row.ByPo.Sum(x => x.RunMinutes) - row.RunMinutes) < 0.01, $"P3 r{round} {row.Machine.SoMay}: Σ theo PO = phút chạy");
                // OK / NG: mỗi dòng 1 lần, vào kỳ chứa NGÀY BẮT ĐẦU dòng (24h)
                var rows = db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.MachineUsed == row.Machine.SoMay
                    && DateOnly.FromDateTime(x.StartTime) >= f && DateOnly.FromDateTime(x.StartTime) <= t).ToList();
                Ok(row.Ok == rows.Sum(x => x.QtyOk ?? 0) && row.Ng == rows.Sum(x => x.QtyNg ?? 0) && row.WorkerCount == rows.Select(x => x.WorkerId).Distinct().Count(),
                    $"P3 r{round} {row.Machine.SoMay}: OK / NG / số người — mỗi dòng 1 lần", $"{row.Ok}/{rows.Sum(x => x.QtyOk ?? 0)}");
                // người đứng máy: đếm từng phút (cả ngày 00:00–24:00, ngoài nghỉ thật của dòng)
                foreach (var w in row.ByWorker)
                {
                    int cnt = 0;
                    foreach (var d in per.Days)
                    {
                        var ws = d.ToDateTime(TimeOnly.MinValue); var we = d.AddDays(1).ToDateTime(TimeOnly.MinValue);
                        var mine = rows.Where(x => x.WorkerId == w.WorkerId).ToList();
                        var brk2 = await WorkMinutesStore.ForWtsAsync(db, mine.Select(x => x.WtsLogId));
                        for (var m = ws; m < we; m = m.AddMinutes(1))
                            if (mine.Any(x => WorkMinutes.WorkPieces(x.StartTime, x.EndTime, WorkMinutesStore.Get(brk2, x.WtsLogId)).Any(pc => m >= pc.S && m < pc.E))) cnt++;
                    }
                    Ok(Math.Abs(w.Minutes - cnt) < 0.01, $"P3 r{round} {row.Machine.SoMay} người {w.WorkerId}: phút đứng máy = đếm phút {cnt}", $"{w.Minutes}");
                }
            }
        }
        Say(fails == bad0, $"P3 4 vòng ngẫu nhiên (seed {seed}): kỳ 24h = Σ ngày, chạy + dừng + rảnh = kế hoạch, OK / NG mỗi dòng 1 lần, người đứng máy đếm phút");
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
        var svc = new MachineEfficiencyService(db) { Clock = () => D1.AddDays(30) };   // [G31-DON-BO-KIEM] 24h
        var f = DateOnly.FromDateTime(D1);
        var no = await svc.GetPeriod24Async(f, f.AddDays(6), 77);
        var ad = await svc.GetPeriod24Async(f, f.AddDays(6), 1);
        Say(no.Rows.Count == 0 && no.Days.Count == 0 && ad.Rows.Count == 1 && ad.Days.Count == 7 && ad.Rows[0].Ok == 5, "P4 máy: không quyền Xem → rỗng; admin → 1 máy, 7 ngày lịch, OK 5", $"{no.Rows.Count}/{ad.Rows.Count}/{ad.Days.Count}");
        var big = await svc.GetPeriod24Async(f, f.AddDays(400), 1);
        var rev = await svc.GetPeriod24Async(f.AddDays(3), f, 1);
        Say(big.Error != null && big.Rows.Count == 0 && rev.Error != null, "P4 máy: > 366 ngày / Từ > Đến → lời báo, không tính", $"{big.Error} | {rev.Error}");
        var rN = await new WtsReportService(db).GetReportAsync(f, f, null, 77);
        Say(rN.Rows.Count == 0 && rN.Days.Count == 0, "P4 WTS Report: không quyền → không dòng, không người-ngày");

        // P4b dòng vắt qua nửa đêm (24h): dòng chạm 2 ngày → OK / NG chỉ tính 1 lần (ngày bắt đầu); phút chạy chia đúng 2 ngày
        var db2 = await WtsScen.SeedAsync();
        db2.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        P(db2, 2, "M1", "serial", D1.AddHours(23), D2.AddHours(1), 7, 1, "NC1", null);
        P(db2, 3, "M1", "serial", D2.AddHours(8), D2.AddHours(9), 3, 0, "NC1", null);
        db2.Fixup();
        var pr = await new MachineEfficiencyService(db2) { Clock = () => D1.AddDays(30) }.GetPeriod24Async(f, f.AddDays(1), 1);
        var m1 = pr.Rows.First();
        Say(pr.Days.Count == 2 && m1.Ok == 10 && m1.Ng == 1 && Math.Abs(m1.RunMinutes - 180) < 0.01 && m1.WorkerCount == 2,
            "P4b dòng 23:00 → 01:00 hôm sau: OK 7 + 3 = 10, NG 1 (mỗi dòng 1 lần), chạy 60 + 60 + 60 = 180 (24h), 2 người",
            $"ngày {pr.Days.Count} OK {m1.Ok} NG {m1.Ng} chạy {m1.RunMinutes} người {m1.WorkerCount}");
    }
}
