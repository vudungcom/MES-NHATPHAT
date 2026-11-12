// [T1 2026-10-31] SqlDongThoi — nhiều "người" (luồng, mỗi luồng 1 DbContext riêng như 1 phiên Blazor) cùng lưu
//   giao / nhận / WTS / huỷ trên CÙNG PO, CÙNG máy, chạy CODE THẬT (HandoverService, WtsSaveService, KhoVatLieuService,
//   KhPlanService) trên SQL Server THẬT (DB bản sao MES_TEST). Đo: khoá có chặn đúng không, deadlock có tự thử lại không,
//   sổ in/out cuối cùng có cân, có ô âm không, có lần nhận vượt phiếu không.
//   KHÔNG BAO GIỜ chạy trên MES: Host kiểm DB_NAME() = MES_TEST trước khi làm gì.
using System.Collections.Concurrent;
using System.Diagnostics;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace SqlDongThoi;

public static class Core
{
    // ── cấu hình chạy ─────────────────────────────────────────────────────────
    public static int Workers = 6, Seconds = 60, PoCount = 4, Phoi = 100;
    // [DONGTHOI-MES 2026-11-05] Hùng 05/11 10:28: dữ liệu DB MES đang là test → cho chạy thẳng trên MES khi có cờ --mes (trạm: lệnh dongthoi).
    //   Không cờ thì y như cũ: chỉ MES_TEST. Dữ liệu thử: phiếu KH ghi chú "T1 SqlDongThoi <giờ>", PO "T1-<giờ>-…", Part "T1-PM-<giờ>" (Ngưng SX).
    public static bool AllowMes;
    static int _admin, _customer, _partId;
    static string _stamp = "";
    static readonly List<string> Report = new();
    static int _fails;
    static int _readDeadlocks, _readOther;
    // [READ-DL 31/10 11:47] Đo mức cô lập thật của câu đọc (sys.dm_exec_sessions): nghi Serializable của giao dịch lập phiếu
    //   "rò" sang câu sau trên kết nối dùng lại từ pool → RCSI không có tác dụng. 1 ReadUncommitted · 2 ReadCommitted · 3 RepeatableRead · 4 Serializable · 5 Snapshot
    static readonly ConcurrentDictionary<int, int> IsoSeen = new();   // câu đọc của công cụ bị deadlock / lỗi khác (không phải hàm lưu của phần mềm)

    static void Log(string s) { Console.WriteLine(s); lock (Report) Report.Add(s); }
    static void Ok(bool cond, string name, string? info = null)
    {
        Log((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info : ""));
        if (!cond) Interlocked.Increment(ref _fails);
    }

    // ── thống kê từng loại thao tác ──────────────────────────────────────────
    sealed class Stat { public int Ok, Rej, Busy, LockWait, Err; public ConcurrentBag<long> Ms = new(); }
    static readonly ConcurrentDictionary<string, Stat> Stats = new();
    static readonly ConcurrentDictionary<string, int> ErrSamples = new();
    static readonly ConcurrentDictionary<string, int> RejSamples = new();

    static async Task<(bool Ok, string? Err)> Run(string op, Func<Task<(bool Ok, string? Err)>> f)
    {
        var st = Stats.GetOrAdd(op, _ => new Stat());
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await f();
            st.Ms.Add(sw.ElapsedMilliseconds);
            if (r.Ok) Interlocked.Increment(ref st.Ok);
            else
            {
                var e = r.Err ?? "";
                if (e == HandoverService.DeadlockMsg) Interlocked.Increment(ref st.Busy);
                else if (e.Contains("đang có người khác")) Interlocked.Increment(ref st.LockWait);
                else
                {
                    Interlocked.Increment(ref st.Rej);
                    var k = op + " · " + Short(e);
                    RejSamples.AddOrUpdate(k, 1, (_, n) => n + 1);
                }
            }
            return r;
        }
        catch (Exception ex)
        {
            st.Ms.Add(sw.ElapsedMilliseconds);
            Interlocked.Increment(ref st.Err);
            var k = op + " · " + ex.GetType().Name + ": " + Short(Inner(ex));
            ErrSamples.AddOrUpdate(k, 1, (_, n) => n + 1);
            return (false, "EXC " + Inner(ex));
        }
    }
    static string Inner(Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; return ex.Message; }
    static string Short(string s) { s = s.Replace("\n", " "); return s.Length > 110 ? s[..110] + "…" : s; }

    // ── mỗi "người" = 1 DbContext riêng (như 1 phiên Blazor) ─────────────────
    sealed class Who : IAsyncDisposable
    {
        public readonly AppDbContext Db;
        public readonly HandoverService Gn;
        public readonly HandoverLedgerService Led;
        public readonly WtsSaveService Wts;
        public readonly KhoVatLieuService Kho;
        public Who()
        {
            Db = Host.NewDb();
            Gn = new HandoverService(Db);
            Led = new HandoverLedgerService(Db);
            Wts = new WtsSaveService(Db, Led);
            Kho = new KhoVatLieuService(Db, null!);   // người thử là Admin → KhoVatLieuService không gọi UserService
        }
        public ValueTask DisposeAsync() => Host.ReleaseAsync(Db);
    }

    static int _mach;
    static string NewMachine() => $"T1M{_stamp[^4..]}{Interlocked.Increment(ref _mach)}";

    static WtsSaveService.WtsSaveGroup WtsGc(int detailId, string po, string nc, decimal ok, string machine)
    {
        var end = DateTime.Now.AddMinutes(-1); var start = end.AddMinutes(-1);
        var g = new WtsSaveService.WtsSaveGroup
        {
            IsGc = true, IsHtsp = false, HandoverGroupCode = "GC", NC = nc, MachineUsed = machine, ProductionMode = "serial",
            UserId = _admin, CaId = null, WorkDate = DateTime.Today,
        };
        g.Rows.Add(new WtsSaveService.WtsSaveRow
        {
            KhPlanDetailId = detailId, PO = po, StartTime = start, EndTime = end, NC = nc,
            QtyDone = ok, QtyOk = ok, QtyNg = 0, SetupTime_Actual = 1, MachiningTime_Actual = 1, InspectionTime_Actual = 1, ProductionMode = "serial",
        });
        return g;
    }
    static WtsSaveService.WtsSaveGroup WtsTaro(int detailId, string po, decimal ok)
    {
        var end = DateTime.Now.AddMinutes(-1); var start = end.AddMinutes(-1);
        var g = new WtsSaveService.WtsSaveGroup
        {
            IsGc = false, IsHtsp = true, HandoverGroupCode = "HTSP", HtspSubGroup = "Taro", ProductionMode = "serial",
            UserId = _admin, CaId = null, WorkDate = DateTime.Today,
        };
        g.Rows.Add(new WtsSaveService.WtsSaveRow
        {
            KhPlanDetailId = detailId, PO = po, StartTime = start, EndTime = end, NC = "T1", WtsCode = "T1",
            QtyDone = ok, QtyOk = ok, QtyNg = 0, ActualTimePerPc = 1, ProductionMode = "serial",
        });
        return g;
    }

    // ── chuẩn bị: phiếu KH mới, n PO, quy trình NC01 → NC02 → Taro T1, Kho nhận phôi ─────────
    static async Task<List<(int Id, string Po)>> NewPosAsync(int n, decimal phoi, string tag)
    {
        await using var w = new Who();
        var kh = new KhPlanService(w.Db);
        var plan = new KhPlan { CustomerId = _customer, PlanDate = DateTime.Today, ApplyWorkflow = true, Notes = "T1 SqlDongThoi " + _stamp };
        for (int i = 0; i < n; i++)
            plan.Details.Add(new KhPlanDetail { PartNo = "T1-PART", PurchaseOrder = $"T1-{_stamp}-{tag}{i + 1}", Quantity = phoi, Unit = "EA", STD = DateTime.Today.AddDays(7) });
        await kh.CreateAsync(plan, _admin);
        var res = new List<(int, string)>();
        foreach (var d in plan.Details)
        {
            w.Db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, SourcePartId = _partId, SnapshotBy = _admin, SnapshotAt = DateTime.Now, StepOrder = 1, NC = "NC01", MachineRegistered = "T1A", SetupTime = 5, MachiningTime = 2, InspectionTime = 1 });
            w.Db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, SourcePartId = _partId, SnapshotBy = _admin, SnapshotAt = DateTime.Now, StepOrder = 2, NC = "NC02", MachineRegistered = "T1B", SetupTime = 4, MachiningTime = 3, InspectionTime = 1 });
            w.Db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, SourcePartId = _partId, SnapshotBy = _admin, SnapshotAt = DateTime.Now, StepOrder = 1, NC = "T1", WtsTaskCode = "T1", StepName = "Taro T1", StandardTime = 1 });
            await w.Db.SaveChangesAsync();
            var k = await w.Kho.SaveKhoInputAsync(d.KhPlanDetailId, DateTime.Now, (int)phoi, "T1", "T1", null, null, null, "T1 nhan phoi", _admin);
            if (!k.Success) throw new Exception("Kho nhận phôi lỗi: " + k.Error);
            res.Add((d.KhPlanDetailId, d.PurchaseOrder));
        }
        w.Db.ChangeTracker.Clear();
        return res;
    }

    // ── chạy n việc cùng lúc (chờ đủ rồi mới bấm "Lưu" một lượt) ────────────
    public static bool Sequential;   // chỉ dùng khi tự kiểm trên DB giả (1 DbContext dùng chung) — chạy thật luôn false
    static async Task<List<T>> AllAtOnce<T>(int n, Func<int, Who, Task<T>> job)
    {
        if (Sequential)
        {
            var seq = new List<T>();
            for (int i = 0; i < n; i++) { await using var w0 = new Who(); seq.Add(await job(i, w0)); }
            return seq;
        }
        var gate = new TaskCompletionSource();
        var ready = 0;
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            await using var w = new Who();
            await w.Db.Users.AsNoTracking().AnyAsync();   // mở kết nối trước
            if (Interlocked.Increment(ref ready) == n) gate.TrySetResult();
            await gate.Task;
            return await job(i, w);
        }).ToList();
        return (await Task.WhenAll(tasks)).ToList();
    }

    static async Task<LedgerRow> LedgerAsync(int id)
    {
        await using var w = new Who();
        return (await w.Led.GetLedgerForDetailAsync(id))!;
    }

    static async Task<string?> CheckPoAsync(int id, string po)
    {
        var row = await LedgerAsync(id);
        var neg = HandoverLedgerService.NegativeCells(row);
        var bad = new List<string>();
        if (!row.Balanced) bad.Add($"không cân (phôi {row.Phoi} ≠ {row.BalanceExplain.Result})");
        if (neg.Count > 0) bad.Add("ô âm: " + string.Join("; ", neg.Values.Select(v => $"{v.Text} = {v.Value:0.##}")));
        await using var w = new Who();
        var over = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == id && !t.IsVoided)
            .Select(t => new { t.HandoverTxId, t.QtyIssued, Rec = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg) })
            .Where(x => x.Rec > x.QtyIssued).ToListAsync();
        if (over.Any()) bad.Add("nhận vượt phiếu: " + string.Join(", ", over.Select(x => $"#{x.HandoverTxId} {x.Rec}>{x.QtyIssued}")));
        return bad.Count == 0 ? null : $"PO {po}: " + string.Join(" · ", bad);
    }

    // ═════════════════════════════════════════════════════════════════════════
    public static async Task<int> MainAsync()
    {
        _stamp = DateTime.Now.ToString("MMddHHmmss");
        Log($"SqlDongThoi (T1) — {DateTime.Now:dd/MM/yyyy HH:mm:ss} · {Workers} người · {Seconds} giây hỗn hợp · {PoCount} PO × {Phoi} phôi");
        await using (var w = new Who())
        {
            var dbName = await Host.DbNameAsync(w.Db);
            Log($"DB đang kết nối: {dbName} · READ_COMMITTED_SNAPSHOT = {(await Host.RcsiAsync(w.Db) switch { 1 => "BẬT", 0 => "tắt", _ => "?" })}");   // [READ-DL] để so kết quả trước / sau khi bật
            if (!string.Equals(dbName, "MES_TEST", StringComparison.OrdinalIgnoreCase)
                && !(AllowMes && string.Equals(dbName, "MES", StringComparison.OrdinalIgnoreCase)))   // [DONGTHOI-MES]
            {
                Log("DỪNG: chỉ chạy trên DB MES_TEST (bản sao), hoặc DB MES khi chạy với cờ --mes (Hùng duyệt 05/11). Không ghi gì.");
                return 2;
            }
            _admin = await w.Db.Users.AsNoTracking().Where(u => u.IsActive && u.Group != null && u.Group.IsActive && u.Group.GroupCode == "ADMIN")
                .OrderBy(u => u.UserId).Select(u => u.UserId).FirstOrDefaultAsync();
            _customer = await w.Db.Customers.AsNoTracking().OrderBy(c => c.CustomerId).Select(c => c.CustomerId).FirstOrDefaultAsync();
            _partId = await w.Db.PartMasters.AsNoTracking().OrderBy(x => x.PartId).Select(x => x.PartId).FirstOrDefaultAsync();   // chỉ để điền SourcePartId của snapshot thử
            if (_admin == 0 || _customer == 0) { Log("DỪNG: MES_TEST chưa có tài khoản nhóm ADMIN đang hoạt động hoặc chưa có khách hàng."); return 2; }
            Log($"Người thử: UserId {_admin} (nhóm ADMIN) · khách CustomerId {_customer} · số PO thử bắt đầu bằng T1-{_stamp}-");
        }

        // ── S1: 8 người cùng lập phiếu Kho → GC 20 trên PO 100 phôi → đúng 5 phiếu ─────
        Log("\n[S1] 8 người CÙNG LÚC lập phiếu Kho → Gia công 20 chiếc, PO có 100 phôi");
        var s1 = (await NewPosAsync(1, 100, "S1A"))[0];
        var r1 = await AllAtOnce(8, (i, w) => Run("S1 giao Kho→GC", async () =>
        {
            var r = await w.Gn.IssueByCellAsync(s1.Id, "KHO", "GC", 20, _admin, "MOI", false, "MOI", null, "NC01", "T1 S1");
            return (r.Ok, r.Error);
        }));
        var led1 = await LedgerAsync(s1.Id);
        Ok(r1.Count(x => x.Ok) == 5 && led1.G("KHO").ConGiao == 0, "Đúng 5 phiếu lưu được, Kho còn 0 (không vượt phôi)",
           $"lưu được {r1.Count(x => x.Ok)} · Kho còn {led1.G("KHO").ConGiao} · bị chặn: {string.Join(" | ", r1.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");
        var c1 = await CheckPoAsync(s1.Id, s1.Po); Ok(c1 == null, "Sổ PO S1 cân, không ô âm", c1);

        // ── S2: 4 người cùng bấm Nhận đủ 1 phiếu → đúng 1 lần nhận ─────────────
        Log("\n[S2] 4 người CÙNG LÚC bấm Nhận đủ 20 của cùng 1 phiếu");
        long tx2;
        await using (var w = new Who())
            tx2 = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == s1.Id && !t.IsVoided).OrderBy(t => t.HandoverTxId).Select(t => t.HandoverTxId).FirstAsync();
        var r2 = await AllAtOnce(4, (i, w) => Run("S2 nhận", async () =>
        {
            var r = await w.Gn.ReceiveAsync(tx2, 20, 0, null, _admin);
            return (r.Ok, r.Error);
        }));
        int nRec2;
        await using (var w = new Who()) nRec2 = await w.Db.HandoverReceives.AsNoTracking().CountAsync(r => r.HandoverTxId == tx2 && !r.IsVoided);
        Ok(r2.Count(x => x.Ok) == 1 && nRec2 == 1, "Đúng 1 lần nhận lưu được, 3 người còn lại bị chặn",
           $"lưu được {r2.Count(x => x.Ok)} · lần nhận trong DB {nRec2} · {string.Join(" | ", r2.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");

        // ── S3: nhận hết 100 vào GC, 8 người cùng nhập WTS NC01 20 (máy khác nhau) → đúng 5 ───
        Log("\n[S3] Gia công đã nhận 100; 8 người CÙNG LÚC nhập WTS NC01 20 chiếc (8 máy khác nhau)");
        await using (var w = new Who())
        {
            var pend = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == s1.Id && !t.IsVoided && t.HandoverTxId != tx2).Select(t => t.HandoverTxId).ToListAsync();
            foreach (var t in pend) { var rr = await w.Gn.ReceiveAsync(t, 20, 0, null, _admin); if (!rr.Ok) Log("  (chuẩn bị) nhận #" + t + ": " + rr.Error); }
        }
        var r3 = await AllAtOnce(8, (i, w) => Run("S3 WTS NC01", async () =>
        {
            var res = await w.Wts.SaveGroupAsync(WtsGc(s1.Id, s1.Po, "NC01", 20, NewMachine()));
            return (res.Ok, res.ErrorMsg);
        }));
        decimal ok3;
        await using (var w = new Who()) ok3 = await w.Db.WtsProductionLogs.AsNoTracking().Where(x => x.KhPlanDetailId == s1.Id && !x.IsVoided && x.NC == "NC01").SumAsync(x => (x.QtyOk ?? 0) + (x.QtyNg ?? 0));
        Ok(r3.Count(x => x.Ok) == 5 && ok3 == 100, "Đúng 5 dòng WTS lưu được, NC01 đã làm 100 (không vượt hàng nhận)",
           $"lưu được {r3.Count(x => x.Ok)} · NC01 tổng {ok3} · {string.Join(" | ", r3.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");
        c1 = await CheckPoAsync(s1.Id, s1.Po); Ok(c1 == null, "Sổ PO S1 cân, không ô âm sau WTS", c1);

        // ── S4: 2 người nhập WTS CÙNG MÁY, cùng giờ, 2 PO khác nhau → đúng 1 ─────
        Log("\n[S4] 2 người CÙNG LÚC nhập WTS cùng 1 máy, cùng khung giờ, 2 PO khác nhau");
        var s4 = await NewPosAsync(2, 20, "S4");
        foreach (var p in s4)
        {
            await using var w = new Who();
            var g = await w.Gn.IssueByCellAsync(p.Id, "KHO", "GC", 20, _admin, "MOI", false, "MOI", null, "NC01", "T1 S4");
            var t = await w.Db.HandoverTransactions.AsNoTracking().Where(x => x.KhPlanDetailId == p.Id && !x.IsVoided).Select(x => x.HandoverTxId).FirstAsync();
            var rr = await w.Gn.ReceiveAsync(t, 20, 0, null, _admin);
            if (!g.Ok || !rr.Ok) Log("  (chuẩn bị) " + g.Error + rr.Error);
        }
        var m4 = NewMachine();
        var r4 = await AllAtOnce(2, (i, w) => Run("S4 WTS cùng máy", async () =>
        {
            var res = await w.Wts.SaveGroupAsync(WtsGc(s4[i].Id, s4[i].Po, "NC01", 5, m4));
            return (res.Ok, res.ErrorMsg);
        }));
        Ok(r4.Count(x => x.Ok) == 1, "Đúng 1 người lưu được, người kia bị báo máy trùng giờ / đang có người lưu",
           $"lưu được {r4.Count(x => x.Ok)} · {string.Join(" | ", r4.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")))}");

        // ── S6 [GC-SUA-TAI-CHO][K4 05/11] nhiều người KCS CÙNG LÚC xác nhận 1 Kết quả sửa (Kho sửa 4 phôi lỗi) → tổng xác nhận đúng 4 ───
        Log("\n[S6] 8 người KCS CÙNG LÚC xác nhận đạt 1 chiếc của cùng 1 Kết quả sửa còn chờ 4 chiếc");
        {
            int kcsUser = 0, cat = 0;
            await using (var w = new Who())
            {
                foreach (var u in await w.Db.Users.AsNoTracking().Where(u => u.IsActive && u.UserId != _admin).OrderBy(u => u.UserId).Select(u => u.UserId).ToListAsync())
                    if (await PermCheck.CanEditAsync(w.Db, u, MES.Web.Constants.SystemPermissions.HandoverKcs)) { kcsUser = u; break; }
                cat = await w.Db.NgCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.NgCategoryId).Select(x => x.NgCategoryId).FirstOrDefaultAsync();
            }
            if (kcsUser == 0 || cat == 0) Log($"  BỎ QUA S6: {(kcsUser == 0 ? "không có tài khoản khác ADMIN thử có quyền Sửa Giao nhận KCS" : "DS Lỗi chưa có loại lỗi đang dùng")}");
            else
            {
                var p6 = (await NewPosAsync(1, 20, "S6"))[0];
                long rep6;
                await using (var w = new Who())
                {
                    var g = await w.Gn.IssueByCellAsync(p6.Id, "KHO", "GC", 20, _admin, "MOI", false, "MOI", null, "NC01", "T1 S6");
                    var tx = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == p6.Id && t.SourceReceiveId == null).OrderByDescending(t => t.HandoverTxId).Select(t => t.HandoverTxId).FirstAsync();
                    var r = await w.Gn.ReceiveAsync(tx, 16, 4, null, _admin, null, null, null, cat);   // 4 phôi lỗi lúc nhận → phiếu trả tự lập
                    w.Db.ChangeTracker.Clear();
                    var back = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == p6.Id && t.SourceReceiveId != null).Select(t => new { t.HandoverTxId, t.NgCaseId }).FirstAsync();
                    var rb = await w.Gn.ReceiveAsync(back.HandoverTxId, 0, 4, null, _admin);   // Kho nhận lại 4 vào ô Hàng lỗi
                    var k = await w.Gn.CreateRepairResultAsync(p6.Id, "KHO", HandoverCell.Loi, back.NgCaseId, 4, 0, null, "T1 S6 Kho sua", _admin);
                    if (!g.Ok || !r.Ok || !rb.Ok || !k.Ok) throw new Exception($"S6 chuẩn bị lỗi: {g.Error} | {r.Error} | {rb.Error} | {k.Error}");
                    w.Db.ChangeTracker.Clear();
                    rep6 = await w.Db.HandoverRepairResults.AsNoTracking().Where(x => x.KhPlanDetailId == p6.Id).Select(x => x.HandoverRepairResultId).FirstAsync();
                }
                var r6 = await AllAtOnce(8, (i, w) => Run("KCS xác nhận cùng lúc", async () => { var x = await w.Gn.ConfirmRepairAsync(rep6, 1, 0, "T1 S6", kcsUser); return (x.Ok, x.Error); }));
                decimal done6;
                await using (var w = new Who())
                    done6 = (await w.Db.HandoverRepairChecks.AsNoTracking().Where(c => c.HandoverRepairResultId == rep6 && !c.IsVoided).Select(c => c.QtyOk + c.QtyNg).ToListAsync()).Sum();
                Ok(r6.Count(x => x.Ok) == 4 && done6 == 4, "Đúng 4 lần xác nhận lưu được (= số chờ), tổng đã xác nhận 4 — không vượt",
                   $"lưu được {r6.Count(x => x.Ok)} · DB {done6} · {string.Join(" | ", r6.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");
                var e6 = await CheckPoAsync(p6.Id, p6.Po);
                Ok(e6 == null, "Sổ PO S6 cân, không ô âm sau xác nhận", e6);
            }
        }

        // ── S9 [G14-KIEM-THEM 2026-11-12] nhiều người Kho CÙNG LÚC nhận phôi khách gửi bù cho 1 lần lỗi đã trả khách 4 → tổng bù đúng 4, không vượt trần ───
        //   Trần đọc lại dưới khoá MES_PO_ (HandoverService.KhachBu.cs). Nguồn: CLAUDE (rà độc lập Gói 14 L5, sổ G14-KIEM-THEM); HÙNG 12/11 08:11 OK (Hùng chạy trên SQL thật).
        Log("\n[S9] 8 người Kho CÙNG LÚC nhận 1 phôi khách gửi bù cho cùng 1 lần lỗi đã trả khách 4");
        {
            int cat9;
            await using (var w = new Who())
                cat9 = await w.Db.NgCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.NgCategoryId).Select(x => x.NgCategoryId).FirstOrDefaultAsync();
            if (cat9 == 0) Log("  BỎ QUA S9: DS Lỗi chưa có loại lỗi đang dùng");
            else
            {
                var p9 = (await NewPosAsync(1, 20, "S9"))[0];
                long vu9;
                await using (var w = new Who())
                {
                    var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
                    var b = await w.Gn.ReportNgAfterReceiveAsync(p9.Id, "KHO", HandoverNgSource.Phoi, null, 4, HandoverNgSource.DauVao, "KHACH", "T1 S9 phoi nut", null, _admin, all, cat9);
                    w.Db.ChangeTracker.Clear();
                    vu9 = await w.Db.NgCases.AsNoTracking().Where(v => v.KhPlanDetailId == p9.Id).OrderByDescending(v => v.NgCaseId).Select(v => v.NgCaseId).FirstAsync();
                    var k = await w.Gn.IssueSpecialAsync(p9.Id, "KHO", "KHACH", 4, _admin, HandoverPurpose.CustReturn, reason: "T1 S9", ngCaseId: vu9);
                    if (!b.Ok || !k.Ok) throw new Exception($"S9 chuẩn bị lỗi: {b.Error} | {k.Error}");
                }
                var r9 = await AllAtOnce(8, (i, w) => Run("Kho nhận bù cùng lúc", async () => { var x = await w.Gn.ReceiveReplacementAsync(p9.Id, vu9, 1, "T1 S9", _admin); return (x.Ok, x.Error); }));
                decimal bu9;
                await using (var w = new Who())
                    bu9 = (await w.Db.HandoverReplacements.AsNoTracking().Where(x => x.KhPlanDetailId == p9.Id && !x.IsVoided).Select(x => x.Qty).ToListAsync()).Sum();
                Ok(r9.Count(x => x.Ok) == 4 && bu9 == 4, "Đúng 4 lần nhận bù lưu được (= trần trả khách 4), tổng bù trong DB 4 — không vượt",
                   $"lưu được {r9.Count(x => x.Ok)} · DB {bu9} · {string.Join(" | ", r9.Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");
                var e9 = await CheckPoAsync(p9.Id, p9.Po);
                Ok(e9 == null, "Sổ PO S9 cân (phôi + bù), không ô âm sau nhận bù", e9);
            }
        }

        // ── S7 [PM-NC-DUP-THAP K4 05/11] nhiều người CÙNG LÚC thêm cùng 1 mã chính vào Part Master (khoá MES_PM_STEPS_<Part>) → đúng 1 dòng ───
        Log("\n[S7] 6 người CÙNG LÚC thêm Đóng gói P9 (và 6 người thêm KCS K9) vào cùng 1 Part");
        {
            int pid7;
            await using (var w = new Who())
            {
                var pm = new PartMaster { PartNo = $"T1-PM-{_stamp}", PartName = "T1 SqlDongThoi (thử khoá Part Master)", CustomerId = _customer, IsActive = true, IsObsolete = true, CreatedAt = DateTime.Now };
                w.Db.PartMasters.Add(pm); await w.Db.SaveChangesAsync(); pid7 = pm.PartId; w.Db.ChangeTracker.Clear();
            }
            async Task<(bool Ok, string? Err)> Try(Func<Task> f) { try { await f(); return (true, null); } catch (PartStepCodeException ex) { return (false, ex.Message); } catch (InvalidOperationException ex) when (ex.Message.Contains("Đang có người khác lưu quy trình")) { return (false, ex.Message); } }
            var a7 = await AllAtOnce(6, (i, w) => Run("Part Master thêm P9 cùng lúc", () => Try(() => new PartPackagingService(w.Db).AddAsync(pid7, new PartPackagingStep { NC = "P9", StepName = "T1 " + i, StandardTime = 1 }, _admin, "T1 thu dong thoi"))));
            var b7 = await AllAtOnce(6, (i, w) => Run("Part Master thêm K9 cùng lúc", () => Try(() => new PartInspectionService(w.Db).AddAsync(pid7, new PartInspectionStep { NC = "K9", StepName = "T1 " + i, StandardTime = 1 }, _admin, "T1 thu dong thoi"))));
            int nP9, nK9;
            await using (var w = new Who())
            {
                nP9 = await w.Db.PartPackagingSteps.AsNoTracking().CountAsync(x => x.PartId == pid7 && x.IsActive && !x.IsBackup && x.NC == "P9");
                nK9 = await w.Db.PartInspectionSteps.AsNoTracking().CountAsync(x => x.PartId == pid7 && x.IsActive && !x.IsBackup && x.NC == "K9");
            }
            Ok(a7.Count(x => x.Ok) == 1 && nP9 == 1 && b7.Count(x => x.Ok) == 1 && nK9 == 1, "Đúng 1 người thêm được mỗi mã, DB đúng 1 dòng chính P9 / K9 (người sau bị báo trùng / đang có người lưu)",
               $"P9: lưu {a7.Count(x => x.Ok)} · DB {nP9} · K9: lưu {b7.Count(x => x.Ok)} · DB {nK9} · {string.Join(" | ", a7.Concat(b7).Where(x => !x.Ok).Select(x => Short(x.Err ?? "")).Distinct())}");
        }

        // ── S8 [PM-SNAP-MID 06/11] (A1) khoá lượt lưu Part Master MES_PM_SAVE_<Part> (khoá phiên) với chụp quy trình (tạo KH) trên SQL THẬT ──
        //   (a) A đang lưu → B tạo KH phải CHỜ tới khi A nhả rồi chụp được; (b) A giữ quá 15 giây → B báo "đang được lưu", không tạo PO;
        //   (c) 2 người cùng lưu 1 Part → người sau chờ tối đa 10 giây rồi báo; (d) A bị bỏ dở (DbContext huỷ, kết nối về pool, KHÔNG nhả) → đo khoá
        //   còn giữ không (APPLOCK_TEST từ kết nối khác) ngay lúc đó / sau khi kết nối pool được dùng lại — để chốt cách dự phòng nếu khoá treo.
        Log("\n[S8] Khoá lượt lưu Part Master (MES_PM_SAVE_) với tạo KH chụp quy trình — chờ / báo, không chụp dở; kết nối pool có nhả khoá phiên không");
        if (Sequential) Log("  (bỏ qua trên DB giả — cần SQL Server thật)");
        else
        {
            int pid8; string pno8;
            await using (var w = new Who())
            {
                var pm = new PartMaster { PartNo = $"T1-PM8-{_stamp}", PartName = "T1 SqlDongThoi (khoá lượt lưu)", CustomerId = _customer, IsActive = true, IsObsolete = true, CreatedAt = DateTime.Now };
                w.Db.PartMasters.Add(pm); await w.Db.SaveChangesAsync(); pid8 = pm.PartId; pno8 = pm.PartNo; w.Db.ChangeTracker.Clear();
                await new PartPackagingService(w.Db).AddAsync(pid8, new PartPackagingStep { NC = "P1", StepName = "T1 dong goi", StandardTime = 1 }, _admin, "T1 thu khoa luu");
            }
            string lk8 = PartStepGuard.SaveLockName(pid8);
            int nKh = 0;
            async Task<(bool Ok, string? Err, long Ms, int DetailId)> CreateKh()
            {
                var sw = Stopwatch.StartNew();
                await using var w = new Who();
                var kh = new KhPlanService(w.Db);
                var plan = new KhPlan { CustomerId = _customer, PlanDate = DateTime.Today, ApplyWorkflow = true, Notes = "T1 SqlDongThoi " + _stamp };
                plan.Details.Add(new KhPlanDetail { PartNo = pno8, PurchaseOrder = $"T1-{_stamp}-S8-{Interlocked.Increment(ref nKh)}", Quantity = 5, Unit = "EA", STD = DateTime.Today.AddDays(7) });
                await using var tx = await w.Db.Database.BeginTransactionAsync();
                try
                {
                    await PartStepGuard.LockSnapshotReadAsync(w.Db, pid8, pno8);   // như trang Create_Khplan: khoá đọc trước cấp số KH
                    await kh.CreateAsync(plan, _admin);
                    foreach (var d in plan.Details) await kh.CopyRouteSnapshotAsync(d.KhPlanDetailId, pid8, _admin);   // như trang Create_Khplan
                    await tx.CommitAsync();
                    return (true, null, sw.ElapsedMilliseconds, plan.Details[0].KhPlanDetailId);
                }
                catch (Exception ex) { await tx.RollbackAsync(); return (false, ex.GetBaseException().Message, sw.ElapsedMilliseconds, 0); }
            }
            async Task<bool> Free() { await using var w = new Who(); return await Host.AppLockFreeAsync(w.Db, lk8); }
            // (a)
            await using (var a = new Who())
            {
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                var tb = CreateKh();
                await Task.Delay(1500);
                // A đang lưu dở: thêm dòng Đóng gói P1B GIỮA lượt (B đã bắt đầu tạo KH) → B phải chụp được bản trọn có P1B
                await new PartPackagingService(a.Db).AddAsync(pid8, new PartPackagingStep { NC = "P1B", StepName = "T1 them giua luot", StandardTime = 1 }, _admin, "T1 thu khoa luu");
                await Task.Delay(1500);
                bool waiting = !tb.IsCompleted;
                if (la != null) await la.DisposeAsync();
                var rb = await tb;
                List<string> snapNc;
                await using (var w = new Who()) snapNc = await w.Db.KhPlanRouteSnapshotPackaging.AsNoTracking().Where(x => x.KhPlanDetailId == rb.DetailId).Select(x => x.NC ?? "").ToListAsync();
                Ok(la != null && waiting && rb.Ok && rb.Ms >= 2500 && snapNc.Contains("P1") && snapNc.Contains("P1B"),
                   "S8a A đang lưu Part (thêm P1B giữa lượt) → B tạo KH CHỜ (≥ 2,5 giây) tới khi A nhả, chụp bản TRỌN (có P1 + P1B)",
                   $"A lấy khoá={la != null} · B còn chờ lúc 3 giây={waiting} · B {(rb.Ok ? "lưu được" : rb.Err)} sau {rb.Ms} ms · snapshot Đóng gói [{string.Join(",", snapNc)}]");
            }
            // (b)
            await using (var a = new Who())
            {
                int nPo0, nPo;
                await using (var w = new Who()) nPo0 = await w.Db.KhPlanDetails.AsNoTracking().CountAsync(d => d.PartNo == pno8);
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                var rb = await CreateKh();
                if (la != null) await la.DisposeAsync();
                await using (var w = new Who()) nPo = await w.Db.KhPlanDetails.AsNoTracking().CountAsync(d => d.PartNo == pno8);
                Ok(la != null && !rb.Ok && (rb.Err ?? "").Contains("đang được lưu") && rb.Ms >= 14000 && nPo == nPo0,
                   "S8b A giữ quá 15 giây → B báo \"đang được lưu — thử lại\" sau ~15 giây, không tạo PO", $"B: {Short(rb.Err ?? "lưu được")} sau {rb.Ms} ms · PO của Part trong DB {nPo}");
            }
            // (c)
            await using (var a = new Who())
            await using (var c = new Who())
            {
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                var sw = Stopwatch.StartNew();
                var lc = await PartStepGuard.AcquireSaveLockAsync(c.Db, pid8);
                long msC = sw.ElapsedMilliseconds;
                if (la != null) await la.DisposeAsync();
                var lc2 = lc ?? await PartStepGuard.AcquireSaveLockAsync(c.Db, pid8);
                bool got2 = lc2 != null;
                if (lc2 != null) await lc2.DisposeAsync();
                Ok(la != null && lc == null && msC >= 9000 && got2, "S8c 2 người cùng lưu 1 Part: người sau chờ ~10 giây rồi báo (không lưu); người trước nhả xong thì lấy được",
                   $"người sau: {(lc == null ? "không lấy được" : "lấy được")} sau {msC} ms · lấy lại sau khi nhả={got2}");
            }
            Ok(await Free(), "S8 sau các bài: khoá MES_PM_SAVE_ không còn ai giữ");
            // (e) [rà độc lập gói 2 T8b] trên CÙNG DbContext đang giữ khoá lượt lưu: service Part Master mở giao dịch + khoá MES_PM_STEPS_ ghi được;
            //   nhả xong khoá rảnh. Rồi đo: cùng phiên đang giữ khoá phiên Exclusive mà xin khoá đọc (Shared / Transaction) thì có tự chờ không
            //   (trang nhả khoá TRƯỚC bước tự cập nhật PO chính vì chỗ này — đo để chốt).
            await using (var a = new Who())
            {
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                string? eAdd = null;
                try { await new PartPackagingService(a.Db).AddAsync(pid8, new PartPackagingStep { NC = "P8E", StepName = "T1 khoa luu", StandardTime = 1 }, _admin, "T1 thu khoa luu"); }
                catch (Exception ex) { eAdd = ex.GetBaseException().Message; }
                bool heldWhile = !await Free();
                if (la != null) await la.DisposeAsync();
                bool freeAfter = await Free();
                Ok(la != null && eAdd == null && heldWhile && freeAfter, "S8e đang giữ khoá lượt lưu, service Part Master trên cùng kết nối thêm dòng được (giao dịch + khoá dòng); nhả xong khoá rảnh",
                   $"thêm: {eAdd ?? "được"} · đang giữ={heldWhile} · rảnh sau nhả={freeAfter}");
                var opE = PartStepGuard.SessionLockOpener; PartStepGuard.SessionLockOpener = null;   // [S8D-KHOA 07/11] bài đo này là CÙNG phiên → cách cũ (kết nối DbContext)
                var la2 = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                PartStepGuard.SessionLockOpener = opE;
                var sw = Stopwatch.StartNew(); string? eSelf = null;
                await using (var tx = await a.Db.Database.BeginTransactionAsync())
                {
                    try { await PartStepGuard.LockSnapshotReadAsync(a.Db, pid8); } catch (Exception ex) { eSelf = ex.Message; }
                    await tx.RollbackAsync();
                }
                if (la2 != null) await la2.DisposeAsync();
                Log($"  ĐO   S8e cùng phiên giữ khoá lượt lưu (Session Exclusive) rồi xin khoá đọc (Shared / Transaction): {(eSelf == null ? "ĐƯỢC NGAY" : "BỊ CHẶN: " + Short(eSelf))} sau {sw.ElapsedMilliseconds} ms (trang luôn nhả trước bước tự cập nhật PO nên không gặp)");
            }
            // (f) [S8D-KHOA 07/11] khoá lượt lưu trên kết nối RIÊNG không pool (MES.Web gắn PartStepGuard.SessionLockOpener = PmSaveLockSql.OpenAsync):
            //   DbContext trang bị huỷ giữa lượt lưu → khoá vẫn giữ tới khi lượt lưu chạy hết (await using) → nhả bằng kết nối riêng → rảnh NGAY,
            //   không chờ pool dùng lại kết nối (trước: "rảnh ngay sau khi huỷ = False").
            if (PartStepGuard.SessionLockOpener != null)
            {
                var a = new Who();
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                await a.DisposeAsync();   // DbContext trang huỷ giữa lượt lưu
                bool heldMid = !await Free();
                if (la != null) await la.DisposeAsync();   // lượt lưu chạy hết (await using) → nhả
                bool freeNow = await Free();
                var rb = await CreateKh();
                Ok(la != null && heldMid && freeNow && rb.Ok, "S8f [S8D-KHOA] DbContext trang huỷ giữa lượt lưu: khoá giữ tới hết lượt rồi nhả NGAY (kết nối riêng, không chờ pool); tạo KH sau đó lưu được",
                   $"lấy={la != null} · còn giữ giữa lượt={heldMid} · rảnh ngay sau khi nhả={freeNow} · tạo KH: {(rb.Ok ? "lưu được" : Short(rb.Err ?? ""))} sau {rb.Ms} ms");
                Host.ClearPools();
            }
            // (d) bỏ dở: DbContext huỷ khi khoá phiên còn giữ (không gọi nhả) — đo CÁCH CŨ (khoá trên kết nối DbContext), tạm tắt SessionLockOpener
            {
                var opener = PartStepGuard.SessionLockOpener; PartStepGuard.SessionLockOpener = null;   // [S8D-KHOA 07/11]
                var a = new Who();
                var la = await PartStepGuard.AcquireSaveLockAsync(a.Db, pid8);
                PartStepGuard.SessionLockOpener = opener;   // [S8D-KHOA]
                await a.DisposeAsync();   // kết nối trả về pool, KHÔNG sp_releaseapplock
                bool freeNow = await Free();
                var reuse = new List<Who>();
                for (int i = 0; i < 8; i++) { var w = new Who(); await w.Db.Users.AsNoTracking().AnyAsync(); reuse.Add(w); }
                bool freeAfterReuse = await Free();
                foreach (var w in reuse) await w.DisposeAsync();
                var rb = await CreateKh();
                Log($"  ĐO   S8d khoá phiên khi DbContext huỷ giữa lượt lưu (không nhả): lấy={la != null} · rảnh ngay sau khi huỷ={freeNow} · rảnh sau khi dùng lại 8 kết nối pool={freeAfterReuse} · tạo KH sau đó: {(rb.Ok ? "lưu được" : Short(rb.Err ?? ""))} sau {rb.Ms} ms");
                Ok(la != null && (freeNow || freeAfterReuse), "S8d khoá không treo mãi khi lượt lưu bị bỏ dở (nhả ngay hoặc khi kết nối pool được dùng lại) — xem dòng ĐO",
                   $"rảnh ngay={freeNow} · sau dùng lại={freeAfterReuse}");
                Host.ClearPools();
            }
        }

        // ── S5: hỗn hợp ngẫu nhiên nhiều người × nhiều PO trong N giây ──────────
        Log($"\n[S5] {Workers} người làm ngẫu nhiên {Seconds} giây trên {PoCount} PO (giao, nhận, WTS NC01 / NC02 / Taro, giao GC → HTSP, huỷ nhận, huỷ phiếu, xoá WTS)");
        var pos = await NewPosAsync(PoCount, Phoi, "S5");
        var until = DateTime.Now.AddSeconds(Seconds);
        var opsTotal = 0;
        var workers = Enumerable.Range(0, Workers).Select(async wi =>
        {
            var rnd = new Random(wi * 7919 + Environment.TickCount);
            await using var w = new Who();
            while (DateTime.Now < until)
            {
                w.Db.ChangeTracker.Clear();
                var p = pos[rnd.Next(pos.Count)];
                var q = rnd.Next(1, 16);
                Interlocked.Increment(ref opsTotal);
                // [T1] câu ĐỌC của chính công cụ (tìm phiếu / lần nhận / dòng WTS để thao tác) cũng có thể bị SQL chọn làm nạn nhân deadlock
                //   khi người khác đang lưu (lần chạy đầu 31/10 11:27 dừng vì lỗi này) → đếm riêng, chạy tiếp; đây cũng là dấu hiệu trang ĐỌC ngoài đời có thể gặp.
                try
                {
                if (rnd.Next(3) == 0)
                {
                    var lv = await Host.IsoLevelAsync(w.Db);
                    IsoSeen.AddOrUpdate(lv, 1, (_, n) => n + 1);
                }
                switch (rnd.Next(10))
                {
                    case 0:
                    case 1:
                        await Run("giao Kho→GC", async () => { var r = await w.Gn.IssueByCellAsync(p.Id, "KHO", "GC", q, _admin, "MOI", false, "MOI", null, "NC01", "T1"); return (r.Ok, r.Error); });
                        break;
                    case 2:
                    case 3:
                    {
                        var pend = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == p.Id && !t.IsVoided && t.Status != "COMPLETED"
                                                                                          && (t.ToGroupCode == "GC" || t.ToGroupCode == "HTSP"))
                            .Select(t => new { t.HandoverTxId, Con = t.QtyIssued - t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg) }).ToListAsync();
                        if (!pend.Any()) break;
                        var t0 = pend[rnd.Next(pend.Count)];
                        var nq = rnd.NextDouble() < 0.7 ? t0.Con : Math.Max(1, Math.Min(t0.Con, q));
                        await Run("nhận", async () => { var r = await w.Gn.ReceiveAsync(t0.HandoverTxId, nq, 0, null, _admin); return (r.Ok, r.Error); });
                        break;
                    }
                    case 4:
                        await Run("WTS NC01", async () => { var r = await w.Wts.SaveGroupAsync(WtsGc(p.Id, p.Po, "NC01", q, NewMachine())); return (r.Ok, r.ErrorMsg); });
                        break;
                    case 5:
                        await Run("WTS NC02", async () => { var r = await w.Wts.SaveGroupAsync(WtsGc(p.Id, p.Po, "NC02", q, NewMachine())); return (r.Ok, r.ErrorMsg); });
                        break;
                    case 6:
                        if (rnd.NextDouble() < 0.5)
                            await Run("giao GC→HTSP", async () => { var r = await w.Gn.IssueByCellAsync(p.Id, "GC", "HTSP", q, _admin, "MOI", false, "MOI", null, null, "T1"); return (r.Ok, r.Error); });
                        else
                            await Run("WTS Taro", async () => { var r = await w.Wts.SaveGroupAsync(WtsTaro(p.Id, p.Po, q)); return (r.Ok, r.ErrorMsg); });
                        break;
                    case 7:
                    {
                        var rs = await w.Db.HandoverReceives.AsNoTracking().Where(r => r.Transaction.KhPlanDetailId == p.Id && !r.IsVoided && !r.Transaction.IsVoided)
                            .Select(r => r.ReceiveId).ToListAsync();
                        if (!rs.Any()) break;
                        var rid = rs[rnd.Next(rs.Count)];
                        await Run("huỷ nhận", async () => { var r = await w.Gn.VoidReceiveAsync(rid, "T1 huy thu", _admin, Array.Empty<string>()); return (r.Ok, r.Error); });
                        break;
                    }
                    case 8:
                    {
                        var ts = await w.Db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == p.Id && !t.IsVoided).Select(t => t.HandoverTxId).ToListAsync();
                        if (!ts.Any()) break;
                        var tid = ts[rnd.Next(ts.Count)];
                        await Run("huỷ phiếu", async () => { var r = await w.Gn.VoidIssueAsync(tid, "T1 huy thu", _admin, Array.Empty<string>()); return (r.Ok, r.Error); });
                        break;
                    }
                    default:
                    {
                        var ws = await w.Db.WtsProductionLogs.AsNoTracking().Where(x => x.KhPlanDetailId == p.Id && !x.IsVoided).Select(x => x.WtsLogId).ToListAsync();
                        if (!ws.Any()) break;
                        var lid = ws[rnd.Next(ws.Count)];
                        await Run("xoá WTS", async () =>
                        {
                            var r = await w.Wts.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = lid, KhPlanDetailId = p.Id, UserId = _admin });
                            return (r.Ok, r.ErrorMsg);
                        });
                        break;
                    }
                }
                }
                catch (Exception ex) when (HandoverService.IsDeadlock(ex))
                {
                    Interlocked.Increment(ref _readDeadlocks);
                }
                catch (Exception ex)
                {
                    var k = "đọc (công cụ) · " + ex.GetType().Name + ": " + Short(Inner(ex));
                    ErrSamples.AddOrUpdate(k, 1, (_, n) => n + 1);
                    Interlocked.Increment(ref _readOther);
                }
            }
        }).ToList();
        await Task.WhenAll(workers);
        Log($"  đã chạy {opsTotal} thao tác");
        foreach (var p in pos.Concat(s4).Append(s1))
        {
            var c = await CheckPoAsync(p.Id, p.Po);
            Ok(c == null, $"Sổ PO {p.Po}: cân, không ô âm, không lần nhận vượt phiếu", c);
        }

        // ── thống kê ────────────────────────────────────────────────────────
        Log("\n[THỐNG KÊ] thao tác · lưu được · bị chặn (luật) · bận-hết 3 lần thử (deadlock) · chờ khoá quá 10 s · lỗi lạ · ms trung vị / p95 / lớn nhất");
        foreach (var (k, s) in Stats.OrderBy(x => x.Key))
        {
            var ms = s.Ms.OrderBy(x => x).ToList();
            long P(double f) => ms.Count == 0 ? 0 : ms[Math.Min(ms.Count - 1, (int)(ms.Count * f))];
            Log($"  {k,-18} {s.Ok,5} {s.Rej,6} {s.Busy,5} {s.LockWait,5} {s.Err,5}   {P(0.5),6} {P(0.95),6} {(ms.Count == 0 ? 0 : ms[^1]),6}");
        }
        if (RejSamples.Any())
        {
            Log("  Lời báo bị chặn hay gặp (đúng luật — chỉ để xem):");
            foreach (var (k, n) in RejSamples.OrderByDescending(x => x.Value).Take(12)) Log($"    {n,4} × {k}");
        }
        var totalErr = Stats.Values.Sum(s => s.Err);
        var totalBusy = Stats.Values.Sum(s => s.Busy);
        if (ErrSamples.Any())
        {
            Log("  LỖI LẠ (ném ra ngoài — gửi Claude):");
            foreach (var (k, n) in ErrSamples.OrderByDescending(x => x.Value).Take(20)) Log($"    {n,4} × {k}");
        }
        Ok(totalErr == 0, "Không có lỗi lạ ném ra ngoài từ hàm LƯU của phần mềm (deadlock đã tự thử lại bên trong)", $"{totalErr} lỗi lạ");
        Ok(_readOther == 0, "Câu đọc của công cụ không gặp lỗi nào khác ngoài deadlock", $"{_readOther} lỗi");
        Log($"  Câu ĐỌC thường (không giao dịch) bị chọn làm nạn nhân deadlock: {_readDeadlocks} lần — không sai số; cho biết trang chỉ đọc (nạp phiếu chờ nhận, nạp sổ…) ngoài đời cũng có thể gặp khi nhiều người cùng lưu");
        static string IsoName(int v) => v switch { 1 => "ReadUncommitted", 2 => "ReadCommitted", 3 => "RepeatableRead", 4 => "Serializable", 5 => "Snapshot", _ => "?" + v };
        Log("  Mức cô lập câu ĐỌC thường đang chạy (lấy mẫu): " + string.Join(" · ", IsoSeen.OrderBy(x => x.Key).Select(x => $"{IsoName(x.Key)} {x.Value}")));
        var isoBad = IsoSeen.Where(x => x.Key != 2).Sum(x => x.Value);
        Ok(isoBad == 0, "Câu đọc thường chạy đúng mức ReadCommitted (không bị Serializable của giao dịch trước rò sang)",
           isoBad == 0 ? null : $"{isoBad} lần câu đọc chạy ở mức khác ReadCommitted — Serializable rò qua kết nối dùng lại → RCSI không có tác dụng, trang đọc + lưu sau đó chạy Serializable ngoài ý muốn (báo Claude)");
        Log($"  Bận hết 3 lần thử (báo 'Máy chủ đang bận'): {totalBusy} lần — không sai số, chỉ cần bấm lại; nhiều quá thì báo Claude");

        Log($"\nKẾT QUẢ: {(_fails == 0 ? "ĐẠT hết" : _fails + " SAI")}");
        var file = $"ketqua_T1_{_stamp}.txt";
        await File.WriteAllLinesAsync(file, Report);
        Console.WriteLine($"Đã ghi {Path.GetFullPath(file)} — gửi file này cho Claude.");
        return _fails == 0 ? 0 : 1;
    }
}
