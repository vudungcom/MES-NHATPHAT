// [W1N-SCEN 2026-11-26] Kịch bản `w1n` — Gói WTS-1NGUON (hiệu suất công nhân chỉ lấy có ích / vô ích từ cờ mã WTS tiêu chuẩn). Đáp án TÍNH TAY.
//   R  [WTS-1NGUON] báo cáo: Gia công theo "Mã dùng cho Gia công" (chưa chọn → Chưa có mã); HTSP / KCS / Đóng gói theo mã CV (dòng cũ mã ở cột NC,
//      mã dự phòng → mã cha theo quy trình PO đã chụp); Việc không ra SP mã lạ / trống → Chưa có mã (trước: có ích); phát sinh tách theo mã phát sinh,
//      trừ khỏi phần dòng, không vượt; giờ trùng: có ích > vô ích > chưa có mã; cờ đọc lúc xem; Σ có ích + vô ích + chưa có mã = phút làm.
//   G  [WTS-1NGUON] WTS tiêu chuẩn "Mã dùng cho Gia công": quyền Sửa WTS_MASTER + PIN, đúng 1 mã, ChangeLog, mã đã xoá không chọn được, mã đang dùng
//      cho Gia công không xoá được, bỏ chọn được.
//   S  [WTS-1NGUON] lưu WTS: phát sinh phút > 0 bắt buộc mã có trong WTS tiêu chuẩn thuộc nhóm người nhập (Admin mọi mã); mục giữ y dòng cũ khi sửa không chặn.
//   M  [WTS-1NGUON] Part Master C–E: mã CV dòng chính phải có trong WTS tiêu chuẩn khi lưu mới / đổi mã; dự phòng gõ mã riêng; dòng cũ sửa ô khác được;
//      Taro lưu mới bắt buộc Mã CV (bỏ đường lùi trống mã → NC).
//   Nguồn: HÙNG 26/11 22:09 + 22:21 + 22:34 + 22:54 + 23:00 "chốt lại làm theo bạn" + 23:08 "Ok làm đi" (bản em hiểu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class W1NScen
{
    static int fails = 0, checks = 0;
    static void Say(bool cond, string name, string? info = null)
    {
        checks++;
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!cond) fails++;
    }

    static readonly DateTime D1 = DateTime.Today.AddDays(-10);
    static readonly DateTime D2 = D1.AddDays(1);
    static long wid = 77000, aid = 88000, eid = 99000;

    static StandardWtsTask Task(AppDbContext db, string code, bool pro, string cat = "CHUNG", bool active = true, bool gc = false)
    {
        var t = new StandardWtsTask { TaskId = 700 + db.StandardWtsTasks.List.Count, CategoryCode = cat, CategoryName = cat, TaskCode = code, TaskName = "Việc " + code, IsActive = active, IsProductiveTask = pro, IsGcTask = gc };
        db.StandardWtsTasks.List.Add(t);
        return t;
    }
    static WtsProductionLog P(AppDbContext db, int w, string pg, DateTime s, DateTime e, string? nc, string? code)
    {
        var l = new WtsProductionLog { WtsLogId = ++wid, KhPlanDetailId = 1, ProcessGroup = pg, NC = nc, WtsCode = code, MachineUsed = pg == "GC" ? "M1" : null, ProductionMode = "serial",
            WorkerId = w, QtyDone = 1, QtyOk = 1, StartTime = s, EndTime = e, CreatedAt = s, Worker = db.Users.List.First(u => u.UserId == w) };
        db.WtsProductionLogs.List.Add(l);
        return l;
    }
    static WorkerActivityLog A(AppDbContext db, int w, string? code, DateTime s, DateTime e)
    {
        var a = new WorkerActivityLog { ActivityLogId = ++aid, WorkerId = w, WorkDate = DateOnly.FromDateTime(s), StartTime = s, EndTime = e, WtsCode = code, WtsName = code,
            CreatedAt = s, Worker = db.Users.List.First(u => u.UserId == w) };
        db.WorkerActivityLogs.List.Add(a);
        return a;
    }
    static void X(AppDbContext db, long w, string? code, int min) => db.WtsExtraItems.List.Add(new WtsExtraItemLog { Id = ++eid, WtsLogId = w, WtsCode = code, Minutes = min });
    static string Parts(WtsReportRow r) => string.Join(",", r.Parts.Select(p => $"{(p.IsExtra ? p.TaskCode : "dòng")}:{p.Kind}:{p.Min}"));

    public static async Task<int> Run()
    {
        await R(); await G(); await S(); await M();
        Console.WriteLine($"Tổng phép kiểm: {checks}");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }

    // ── R báo cáo ───────────────────────────────────────────────────────────────────────────────────────────
    static async Task R()
    {
        Console.WriteLine("[R] WTS-1NGUON — báo cáo: 3 loại theo cờ mã, phát sinh tách theo mã, giờ trùng có ích > vô ích > chưa có mã");
        var db = await WtsScen.SeedAsync();
        var gcT = Task(db, "GCX", true, "GIA_CONG", gc: true);
        var cho = Task(db, "CHO", false); Task(db, "VS", false); Task(db, "C1", true);
        Task(db, "B1", false, "BAVIA"); Task(db, "K1", true, "KCS");
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 77, KhPlanDetailId = 1, StepOrder = 2, NC = "B1-DP-01", IsBackup = true, ParentNC = "B1", StandardTime = 0.5m });
        // dòng dự phòng Taro CŨ (rà độc lập L3): mã sổ = NC "T1-DP" (Mã CV trống), cha theo NC "T1" (dòng chính Mã CV T1 — vô ích)
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 77, KhPlanDetailId = 1, StepOrder = 3, NC = "T1-DP", WtsTaskCode = null, IsBackup = true, ParentNC = "T1", StandardTime = 1 });
        Task(db, "T1", false, "TARO");
        // R1 u2: GC 08–10 (120) + phát sinh CHO 20 → Gia công 100 có ích + CHO 20 vô ích
        var r1 = P(db, 2, "GC", D1.AddHours(8), D1.AddHours(10), "NC1", null); X(db, r1.WtsLogId, "CHO", 20);
        // R2 u3: GC 08–10 + phát sinh không mã 15 + CHO 200 (vượt) → không mã 15, CHO 105, dòng 0
        var r2 = P(db, 3, "GC", D1.AddHours(8), D1.AddHours(10), "NC1", null); X(db, r2.WtsLogId, null, 15); X(db, r2.WtsLogId, "CHO", 200);
        // R3 u4: việc LA (mã lạ) 08–09, việc trống 09–09:30, VS 10–11
        A(db, 4, "LA", D1.AddHours(8), D1.AddHours(9)); A(db, 4, "", D1.AddHours(9), D1.AddHours(9.5)); A(db, 4, "VS", D1.AddHours(10), D1.AddHours(11));
        // R4 u5: Bavia B1 (vô ích) 08–09; Bavia mã dự phòng B1-DP-01 09–10 (→ cha B1); KCS dòng cũ mã ở cột NC K1 (có ích) 10–11; Đóng gói mã lạ PX 11–12
        P(db, 5, "BAVIA", D1.AddHours(8), D1.AddHours(9), null, "B1"); P(db, 5, "BAVIA", D1.AddHours(9), D1.AddHours(10), null, "B1-DP-01");
        P(db, 5, "KCS", D1.AddHours(10), D1.AddHours(11), "K1", null); P(db, 5, "PKG", D1.AddHours(11), D1.AddHours(12), null, "PX");
        P(db, 5, "TARO", D1.AddHours(12), D1.AddHours(13), null, "T1-DP");
        db.Fixup();
        var svc = new WtsReportService(db);
        var res = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        WtsReportRow Row(long? w = null, int worker = 0, DateTime? s = null) => res.Rows.First(x => (w != null && x.WtsLogId == w) || (w == null && x.WorkerId == worker && x.StartTime == s));
        WtsReportDay Day(int w) => res.Days.First(d => d.WorkerId == w);
        var x1 = Row(r1.WtsLogId);
        Say(x1.Kind == WtsTaskKind.CoIch && x1.AllocMin == 120 && Parts(x1) == "dòng:CoIch:100,CHO:VoIch:20", "[R1] Gia công 120 (mã GCX có ích) + PS CHO 20 → dòng 100 có ích + CHO 20 vô ích", Parts(x1));
        Say(Day(2).ProductiveMin == 100 && Day(2).WasteMin == 20 && Day(2).NoCodeMin == 0 && Day(2).WorkMin == 120, "[R1] người-ngày u2: 100 / 20 / 0", $"{Day(2).ProductiveMin}/{Day(2).WasteMin}/{Day(2).NoCodeMin}");
        var x2 = Row(r2.WtsLogId);
        Say(Parts(x2) == "dòng:CoIch:0,:ChuaMa:15,CHO:VoIch:105" && Day(3).ProductiveMin == 0 && Day(3).WasteMin == 105 && Day(3).NoCodeMin == 15,
            "[R2] PS không mã 15 → chưa có mã; PS CHO 200 vượt → chỉ lấy 105 (phần còn lại của dòng); dòng còn 0", Parts(x2));
        Say(Row(worker: 4, s: D1.AddHours(8)).Kind == WtsTaskKind.ChuaMa && Row(worker: 4, s: D1.AddHours(9)).Kind == WtsTaskKind.ChuaMa && Row(worker: 4, s: D1.AddHours(10)).Kind == WtsTaskKind.VoIch
            && Day(4).ProductiveMin == 0 && Day(4).WasteMin == 60 && Day(4).NoCodeMin == 90, "[R3] việc mã lạ / trống → chưa có mã 90 (trước: có ích); VS vô ích 60",
            $"{Day(4).ProductiveMin}/{Day(4).WasteMin}/{Day(4).NoCodeMin}");
        Say(Row(worker: 5, s: D1.AddHours(8)).Kind == WtsTaskKind.VoIch && Row(worker: 5, s: D1.AddHours(9)).Kind == WtsTaskKind.VoIch
            && Row(worker: 5, s: D1.AddHours(10)).Kind == WtsTaskKind.CoIch && Row(worker: 5, s: D1.AddHours(11)).Kind == WtsTaskKind.ChuaMa
            && Row(worker: 5, s: D1.AddHours(12)).Kind == WtsTaskKind.VoIch,
            "[R4] HTSP B1 vô ích; mã dự phòng B1-DP-01 → cha B1 vô ích; KCS dòng cũ mã ở NC K1 có ích; Đóng gói mã lạ → chưa có mã; dự phòng Taro cũ T1-DP → cha NC T1 (mã T1) vô ích",
            string.Join(",", res.Rows.Where(r => r.WorkerId == 5).Select(r => r.Kind)));
        Say(res.ProductiveMin == 160 && res.WasteMin == 365 && res.NoCodeMin == 165 && res.TotalMinutes == 690 && res.ProductiveMin + res.WasteMin + res.NoCodeMin == res.TotalMinutes,
            "R tổng: có ích 160 (u2 100 + u5 K1 60) · vô ích 365 (u2 20 + u3 105 + u4 60 + u5 180) · chưa có mã 165 (u3 15 + u4 90 + u5 60) = phút làm 690",
            $"{res.ProductiveMin}/{res.WasteMin}/{res.NoCodeMin} tổng {res.TotalMinutes}");
        // cờ đọc lúc xem: đổi CHO thành có ích → R1 có ích 120
        cho.IsProductiveTask = true;
        var res2 = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        Say(res2.Days.First(d => d.WorkerId == 2).ProductiveMin == 120, "[R6] khách đổi cờ CHO → có ích: % kỳ cũ đổi theo (u2 có ích 120)", $"{res2.Days.First(d => d.WorkerId == 2).ProductiveMin}");
        cho.IsProductiveTask = false;
        // mã Gia công vô ích → Gia công vô ích; bỏ chọn → chưa có mã
        gcT.IsProductiveTask = false;
        var res3 = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        Say(res3.Rows.First(r => r.WtsLogId == r1.WtsLogId).Parts[0].Kind == WtsTaskKind.VoIch && res3.Days.First(d => d.WorkerId == 2).WasteMin == 120, "[R7] mã Gia công đặt Vô ích → Gia công vô ích (u2 vô ích 120)");
        gcT.IsProductiveTask = true; gcT.IsGcTask = false;
        var res4 = await svc.GetReportAsync(DateOnly.FromDateTime(D1), DateOnly.FromDateTime(D1), null, 1);
        Say(res4.Days.First(d => d.WorkerId == 2).NoCodeMin == 100 && res4.Days.First(d => d.WorkerId == 2).WasteMin == 20, "[R8] chưa chọn mã Gia công → Gia công chưa có mã 100, PS CHO vẫn vô ích 20",
            $"{res4.Days.First(d => d.WorkerId == 2).NoCodeMin}/{res4.Days.First(d => d.WorkerId == 2).WasteMin}");

        // R5 giờ trùng (u2 ngày 2, KHÔNG có mã Gia công): GC 08–10 + VS 09–10 + C1 09:30–10
        var g5 = P(db, 2, "GC", D2.AddHours(8), D2.AddHours(10), "NC1", null);
        var v5 = A(db, 2, "VS", D2.AddHours(9), D2.AddHours(10)); var c5 = A(db, 2, "C1", D2.AddHours(9.5), D2.AddHours(10));
        db.Fixup();
        var r5 = await svc.GetReportAsync(DateOnly.FromDateTime(D2), DateOnly.FromDateTime(D2), null, 1);
        int Al(long? w, long? a) => r5.Rows.First(x => (w != null && x.WtsLogId == w) || (a != null && x.ActivityLogId == a)).AllocMin;
        var d5 = r5.Days.First(d => d.WorkerId == 2);
        Say(Al(g5.WtsLogId, null) == 60 && Al(null, v5.ActivityLogId) == 30 && Al(null, c5.ActivityLogId) == 30 && d5.ProductiveMin == 30 && d5.WasteMin == 30 && d5.NoCodeMin == 60,
            "[R5] giờ trùng: 08–09 chỉ Gia công (chưa có mã) 60; 09–09:30 Gia công + VS → VS (vô ích thắng chưa có mã) 30; 09:30–10 → C1 (có ích thắng) 30",
            $"GC {Al(g5.WtsLogId, null)} VS {Al(null, v5.ActivityLogId)} C1 {Al(null, c5.ActivityLogId)} · {d5.ProductiveMin}/{d5.WasteMin}/{d5.NoCodeMin}");
        // [R9] rà độc lập 26/11 T1: dòng chưa có mã (Đóng gói mã lạ PX) 08–10 có PS C1 (có ích) 60 + việc VS (vô ích) 08–10 chồng giờ (u3 ngày 2)
        //   → phút có ích 60 (PS C1) tính trước, 60 còn lại: VS vô ích thắng phần Gia công chưa có mã → có ích 60 · vô ích 60 · chưa có mã 0
        var g9 = P(db, 3, "PKG", D2.AddHours(8), D2.AddHours(10), null, "PX"); X(db, g9.WtsLogId, "C1", 60);   // dòng Đóng gói mã lạ = chưa có mã
        var v9 = A(db, 3, "VS", D2.AddHours(8), D2.AddHours(10));
        // [R10] đứng 2 máy, mã Gia công có ích, M1 08–10 có PS CHO (vô ích) 90; M2 08–10 (u4 ngày 2) → phút có ích thắng: có ích 120, CHO 0
        gcT.IsGcTask = true;
        var m1 = P(db, 4, "GC", D2.AddHours(8), D2.AddHours(10), "NC1", null); X(db, m1.WtsLogId, "CHO", 90);
        var m2 = P(db, 4, "GC", D2.AddHours(8), D2.AddHours(10), "NC2", null);
        // [R11] rà độc lập L4: dòng cũ có T.PS 15 mà không có mục phát sinh chi tiết (u5 ngày 2, Gia công có ích 08–09) → 15 phút "phát sinh không mã"
        var r11 = P(db, 5, "GC", D2.AddHours(8), D2.AddHours(9), "NC1", null); r11.ExtraMinutes = 15;
        db.Fixup();
        var r9 = await svc.GetReportAsync(DateOnly.FromDateTime(D2), DateOnly.FromDateTime(D2), null, 1);
        var d9 = r9.Days.First(d => d.WorkerId == 3); var d10 = r9.Days.First(d => d.WorkerId == 4);
        var x9 = r9.Rows.First(r => r.WtsLogId == g9.WtsLogId);
        Say(d9.ProductiveMin == 60 && d9.WasteMin == 60 && d9.NoCodeMin == 0 && x9.AllocMin == 60 && r9.Rows.First(r => r.ActivityLogId == v9.ActivityLogId).AllocMin == 60,
            "[R9] PS có ích trên dòng chưa có mã, chồng việc vô ích: có ích 60 (PS C1) · vô ích 60 (VS) · chưa có mã 0 — chia theo phần, không theo cả dòng",
            $"{d9.ProductiveMin}/{d9.WasteMin}/{d9.NoCodeMin} · {Parts(x9)}");
        Say(d10.ProductiveMin == 120 && d10.WasteMin == 0 && r9.Rows.Where(r => r.WorkerId == 4).Sum(r => r.AllocMin) == 120,
            "[R10] đứng 2 máy, PS CHO 90 ở M1 trong lúc M2 vẫn chạy có ích → có ích 120, vô ích 0 (có ích tính trước)",
            $"{d10.ProductiveMin}/{d10.WasteMin}/{d10.NoCodeMin} · M1 {Parts(r9.Rows.First(r => r.WtsLogId == m1.WtsLogId))} · M2 {Parts(r9.Rows.First(r => r.WtsLogId == m2.WtsLogId))}");
        var d11 = r9.Days.First(d => d.WorkerId == 5);
        Say(d11.ProductiveMin == 45 && d11.NoCodeMin == 15 && Parts(r9.Rows.First(r => r.WtsLogId == r11.WtsLogId)) == "dòng:CoIch:45,:ChuaMa:15",
            "[R11] dòng cũ T.PS 15 không có mục chi tiết → 15 phút chưa có mã (phát sinh không mã), Gia công có ích 45", $"{d11.ProductiveMin}/{d11.WasteMin}/{d11.NoCodeMin}");
    }

    // ── G WTS tiêu chuẩn: Mã dùng cho Gia công ─────────────────────────────────────────────────────────────
    static async Task G()
    {
        Console.WriteLine("[G] WTS-1NGUON — \"Mã dùng cho Gia công\": quyền + PIN, đúng 1 mã, ChangeLog, mã đã xoá, chặn xoá, bỏ chọn");
        var db = LenScen.Seed();
        var none = new UserGroup { GroupId = 77, GroupCode = "NONE", IsActive = true };
        db.UserGroups.List.Add(none);
        db.Users.List.Add(new User { UserId = 77, Username = "u77", FullName = "U77", IsActive = true, Group = none, GroupId = 77, PIN = "1111" });
        var a = Task(db, "GA", true); var b = Task(db, "GB", false); var c = Task(db, "GC0", true, active: false);
        db.Fixup();
        var wts = new StandardWtsTaskService(db, new AuthService(db));
        int Cnt() => db.StandardWtsTasks.List.Count(t => t.IsGcTask);
        int Logs() => db.StandardWtsTaskChangeLogs.List.Count(l => l.FieldName == StandardWtsTaskService.GcFieldName);
        var r = await wts.SetGcTaskAsync(a.TaskId, 77, "1111");
        Say(!r.Success && r.Error!.Contains("WTS tiêu chuẩn") && Cnt() == 0, "[G1] không quyền Sửa WTS tiêu chuẩn → chặn", r.Error);
        r = await wts.SetGcTaskAsync(a.TaskId, 1, "9999");
        Say(!r.Success && Cnt() == 0, "[G2] sai PIN → chặn", r.Error);
        r = await wts.SetGcTaskAsync(a.TaskId, 1, "1111");
        Say(r.Success && a.IsGcTask && Cnt() == 1 && Logs() == 1, "[G3] chọn GA → GA là mã Gia công, 1 dòng lịch sử", r.Error);
        r = await wts.SetGcTaskAsync(b.TaskId, 1, "1111");
        Say(r.Success && !a.IsGcTask && b.IsGcTask && Cnt() == 1 && Logs() == 3, "[G4] đổi sang GB → GA bỏ, GB chọn (vẫn đúng 1 mã), +2 dòng lịch sử", $"{r.Error} logs {Logs()}");
        r = await wts.SetGcTaskAsync(c.TaskId, 1, "1111");
        Say(!r.Success && b.IsGcTask && !c.IsGcTask && Cnt() == 1, "[G5] chọn mã đã xoá khỏi danh mục → chặn, giữ GB", r.Error);
        var gg = await wts.GetGcTaskAsync();
        Say(gg?.TaskId == b.TaskId, "[G6] GetGcTaskAsync = GB");
        r = await wts.SoftDeleteAsync(b.TaskId, 1, "1111");
        Say(!r.Success && r.Error == StandardWtsTaskService.GcDeleteMsg("GB") && b.IsActive, "[G7] xoá mã đang dùng cho Gia công → chặn (chọn mã khác trước)", r.Error);
        r = await wts.SetGcTaskAsync(b.TaskId, 1, "1111");
        Say(r.Success && Logs() == 3, "[G8] chọn lại đúng mã đang chọn → không đổi gì, không ghi lịch sử", $"logs {Logs()}");
        r = await wts.SetGcTaskAsync(null, 1, "1111");
        Say(r.Success && Cnt() == 0 && Logs() == 4 && await wts.GetGcTaskAsync() == null, "[G9] bỏ chọn → không còn mã Gia công, +1 dòng lịch sử", r.Error);
        r = await wts.SoftDeleteAsync(b.TaskId, 1, "1111");
        Say(r.Success && !b.IsActive, "[G10] bỏ chọn rồi thì xoá GB được", r.Error);
    }

    // ── S lưu WTS: mã phát sinh ─────────────────────────────────────────────────────────────────────────────
    static async Task S()
    {
        Console.WriteLine("[S] WTS-1NGUON — lưu WTS: phát sinh phút > 0 bắt buộc mã trong WTS tiêu chuẩn (nhóm người nhập); mục cũ giữ y khi sửa không chặn");
        var db = await WtsScen.SeedAsync(); var s = WtsScen.Svc(db);
        WtsScen.PsTask(db, "PS1");
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 777, CategoryCode = "NGOAI", CategoryName = "Ngoài nhóm", TaskCode = "OUT1", TaskName = "Ngoài nhóm", IsActive = true });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 778, CategoryCode = "PSTEST", CategoryName = "Phát sinh thử", TaskCode = "OLD9", TaskName = "Đã xoá", IsActive = false });
        db.Fixup();
        var T = DateTime.Today.AddHours(8);
        int hr = 0;
        async Task<(WtsSaveService.WtsSaveResult R, WtsSaveService.WtsSaveRow Row)> Save(int user, params (string? Code, int Min)[] ps)
        {
            var g = WtsScen.Gc(); g.UserId = user;
            var row = WtsScen.Row(1, 1, 0, T.AddHours(hr), T.AddHours(hr + 1)); hr++;
            foreach (var p in ps) row.ExtraItems.Add(new WtsSaveService.WtsSaveExtra { WtsCode = p.Code, Minutes = p.Min });
            g.Rows.Add(row);
            return (await s.SaveGroupAsync(g), row);
        }
        int n0 = db.WtsProductionLogs.List.Count;
        var (r1, w1) = await Save(2, (null, 10));
        Say(!r1.Ok && w1.ErrorMsg == WtsSaveService.PsNoCodeMsg(10) && db.WtsProductionLogs.List.Count == n0, "[S1] PS 10 phút không mã → chặn, không ghi", w1.ErrorMsg);
        var (r2, w2) = await Save(2, ("ZZ", 5));
        Say(!r2.Ok && w2.ErrorMsg == WtsSaveService.PsBadCodeMsg("ZZ"), "[S2] mã PS không có trong WTS tiêu chuẩn → chặn", w2.ErrorMsg);
        var (r2b, w2b) = await Save(2, ("OLD9", 5));
        Say(!r2b.Ok && w2b.ErrorMsg == WtsSaveService.PsBadCodeMsg("OLD9"), "[S3] mã PS đã ngưng dùng → chặn", w2b.ErrorMsg);
        var (r3, w3) = await Save(2, ("OUT1", 5));
        Say(!r3.Ok && w3.ErrorMsg == WtsSaveService.PsNotGroupMsg("OUT1"), "[S4] mã PS không thuộc nhóm WTS của người nhập (u2) → chặn", w3.ErrorMsg);
        var (r4, w4) = await Save(1, ("OUT1", 5));
        Say(r4.Ok, "[S5] Admin dùng mã ngoài nhóm → được", r4.ErrorMsg);
        var (r5, w5) = await Save(2, (" ps1 ", 12), (null, 0));
        var ex5 = db.WtsExtraItems.List.Where(e => e.WtsLogId == w5.WtsLogId).ToList();
        Say(r5.Ok && ex5.Count == 1 && ex5[0].WtsCode == "PS1" && ex5[0].Minutes == 12 && ex5[0].WtsName == "Phát sinh PS1",
            "[S6] mã ' ps1 ' → lưu đúng chữ danh mục PS1 + tên; mục 0 phút không mã bỏ qua (như cũ)", r5.ErrorMsg + " " + string.Join(",", ex5.Select(e => e.WtsCode + ":" + e.WtsName)));
        // dòng cũ có phát sinh không mã (dữ liệu trước gói) → sửa giữ y mục đó: được; đổi phút mục không mã: chặn
        db.WtsExtraItems.List.Add(new WtsExtraItemLog { Id = 99901, WtsLogId = w5.WtsLogId, WtsCode = null, Minutes = 7 });
        db.Fixup();
        async Task<(WtsSaveService.WtsSaveResult R, WtsSaveService.WtsSaveRow Row)> Edit(long id, DateTime st, params (string? Code, int Min)[] ps)
        {
            var g = WtsScen.Gc(); var e = WtsScen.Row(1, 1, 0, st, st.AddHours(1)); e.IsEditing = true; e.WtsLogId = id;
            foreach (var p in ps) e.ExtraItems.Add(new WtsSaveService.WtsSaveExtra { WtsCode = p.Code, Minutes = p.Min });
            g.Rows.Add(e);
            return (await s.SaveGroupAsync(g), e);
        }
        var st5 = db.WtsProductionLogs.List.First(x => x.WtsLogId == w5.WtsLogId).StartTime;
        var (r6, w6) = await Edit(w5.WtsLogId!.Value, st5, ("PS1", 12), (null, 8));
        Say(!r6.Ok && w6.ErrorMsg == WtsSaveService.PsNoCodeMsg(8), "[S7] sửa dòng: mục không mã đổi phút 7 → 8 → chặn", w6.ErrorMsg);
        var (r7, w7) = await Edit(w5.WtsLogId!.Value, st5, ("PS1", 12), (null, 7));
        Say(r7.Ok && db.WtsExtraItems.List.Count(e => e.WtsLogId == w7.WtsLogId && e.WtsCode == null && e.Minutes == 7) == 1,
            "[S8] sửa dòng: mục không mã giữ y dòng cũ (7 phút) → lưu được, chép sang dòng mới", r7.ErrorMsg);
        var (r8, w8) = await Edit(w7.WtsLogId!.Value, st5, ("PS1", 12), (null, 7), (null, 7));
        Say(!r8.Ok && w8.ErrorMsg == WtsSaveService.PsNoCodeMsg(7), "[S9] sửa dòng: thêm 1 mục không mã trùng mục cũ → chặn (mỗi mục cũ chỉ giữ 1 lần)", w8.ErrorMsg);
    }

    // ── M Part Master C–E ───────────────────────────────────────────────────────────────────────────────────
    static async Task M()
    {
        Console.WriteLine("[M] WTS-1NGUON — Part Master: mã CV dòng chính phải có trong WTS tiêu chuẩn khi lưu mới / đổi mã; Taro lưu mới bắt buộc Mã CV");
        var db = LenScen.Seed();
        Task(db, "B1", false, "BAVIA"); Task(db, "B2", false, "BAVIA"); Task(db, "3T", true, "TARO"); Task(db, "BOLD", false, "BAVIA", active: false);
        var pm = new PartMaster { PartId = 77, PartNo = "PM-W1N", IsActive = true };
        db.PartMasters.List.Add(pm);
        db.Fixup();
        const int U = 1; const string Rs = "w1n";
        static async Task<string?> E(Func<Task> f) { try { await f(); return null; } catch (Exception ex) { return ex.Message; } }
        var bav = new PartBaviaService(db); var taro = new PartTaroService(db); var kcs = new PartInspectionService(db);
        var e1 = await E(() => bav.AddAsync(77, new PartBaviaStep { NC = "BX", StepName = "x", StandardTime = 1 }, U, Rs));
        Say(e1 == PartStepGuard.WtsCodeBadMsg("Bavia", "BX") && !db.PartBaviaSteps.List.Any(), "[M1] Bavia thêm mã gõ tay BX (không có trong WTS tiêu chuẩn) → chặn", e1);
        var e1b = await E(() => bav.AddAsync(77, new PartBaviaStep { NC = "BOLD", StepName = "x", StandardTime = 1 }, U, Rs));
        Say(e1b == PartStepGuard.WtsCodeBadMsg("Bavia", "BOLD"), "[M2] mã đã ngưng dùng → chặn", e1b);
        var e2 = await E(() => kcs.AddAsync(77, new PartInspectionStep { NC = "B1", StepName = "x", StandardTime = 1 }, U, Rs));
        var e3 = await E(() => bav.AddAsync(77, new PartBaviaStep { NC = " b1 ", StepName = "x", StandardTime = 1 }, U, Rs));
        Say(e2 == null && e3 == null && db.PartBaviaSteps.List.Count(x => x.IsActive) == 1, "[M3] mã có trong danh mục (không phân biệt hoa thường) → thêm được", e2 + " | " + e3);
        var e4 = await E(() => bav.AddAsync(77, new PartBaviaStep { NC = "B1-DP-01", IsBackup = true, ParentNC = "B1", StepName = "dp", StandardTime = 1 }, U, Rs));
        Say(e4 == null, "[M4] dòng dự phòng gõ mã riêng B1-DP-01 → được (không đòi có trong danh mục)", e4);
        // dòng cũ mã gõ tay (dữ liệu trước gói)
        db.PartBaviaSteps.Add(new PartBaviaStep { PartId = 77, StepOrder = 9, NC = "CU9", StepName = "cũ", StandardTime = 1, IsActive = true, CreatedBy = U, CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();
        var cu = db.PartBaviaSteps.List.First(x => x.NC == "CU9");
        var e5 = await E(() => bav.UpdateAsync(cu.StepId, new PartBaviaStep { NC = "CU9", StepName = "cũ", StandardTime = 2 }, U, Rs));
        Say(e5 == null && cu.StandardTime == 2, "[M5] dòng cũ mã gõ tay: sửa ô khác (T.Chuẩn) → được", e5);
        var e6 = await E(() => bav.UpdateAsync(cu.StepId, new PartBaviaStep { NC = "CU8", StepName = "cũ", StandardTime = 2 }, U, Rs));
        var e7 = await E(() => bav.UpdateAsync(cu.StepId, new PartBaviaStep { NC = "B2", StepName = "cũ", StandardTime = 2 }, U, Rs));
        Say(e6 == PartStepGuard.WtsCodeBadMsg("Bavia", "CU8") && e7 == null && cu.NC == "B2", "[M6] đổi mã sang mã ngoài danh mục → chặn; đổi sang B2 → được", e6 + " | " + e7);
        var e8 = await E(() => taro.AddAsync(77, new PartTaroStep { NC = "3", WtsTaskCode = null, StepName = "t", StandardTime = 1 }, U, Rs));
        Say(e8 == PartStepGuard.WtsCodeBlankMsg("Taro") && !db.PartTaroSteps.List.Any(), "[M7] Taro lưu mới trống Mã CV → chặn (bỏ đường lùi mã = NC)", e8);
        var e9 = await E(() => taro.AddAsync(77, new PartTaroStep { NC = "3", WtsTaskCode = "3T", StepName = "t", StandardTime = 1 }, U, Rs));
        Say(e9 == null, "[M8] Taro Mã CV 3T có trong danh mục → được", e9);
        db.PartTaroSteps.Add(new PartTaroStep { PartId = 77, StepOrder = 9, NC = "5", WtsTaskCode = null, StepName = "cũ", StandardTime = 1, IsActive = true, CreatedBy = U, CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();
        var tcu = db.PartTaroSteps.List.First(x => x.NC == "5");
        var e10 = await E(() => taro.UpdateAsync(tcu.StepId, new PartTaroStep { NC = "5", WtsTaskCode = null, StepName = "cũ", StandardTime = 3 }, U, Rs));
        var e11 = await E(() => taro.UpdateAsync(tcu.StepId, new PartTaroStep { NC = "6", WtsTaskCode = null, StepName = "cũ", StandardTime = 3 }, U, Rs));
        Say(e10 == null && e11 == PartStepGuard.WtsCodeBlankMsg("Taro"), "[M9] Taro dòng cũ trống mã: sửa T.Chuẩn được; đổi NC (mã sổ đổi) → bắt chọn Mã CV", e10 + " | " + e11);
    }
}
