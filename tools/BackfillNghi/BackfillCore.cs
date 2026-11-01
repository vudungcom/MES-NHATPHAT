// [ML17-XN][CA-NGHI-HIST 2026-11-01] Phần tính của BackfillNghi — dùng chung bản chạy SQL thật (RealHost.cs) và bộ kiểm
//   tools/GiaoNhanFuzz (kịch bản nghi, DB giả). Chỉ tính bằng WorkMinutes (cùng hàm trang WTS / máy chủ lúc lưu).
//   Luật:
//   - Dòng cha CHƯA có dòng nghỉ nào và tạo TRƯỚC mốc (mốc = dòng nghỉ đầu tiên do code mới ghi lúc lưu, IsBackfill = 0;
//     chưa có thì mọi dòng) → chép giờ nghỉ HIỆN TẠI của ca vào (Thông = 0, IsBackfill = 1). Cả dòng đã huỷ (để truy).
//     Dòng tạo sau mốc do code mới lưu: không chạm nghỉ thì đúng là không có dòng nghỉ → không đụng.
//   - Dòng WTS: ca = cột CaId của dòng; CaId trống → không biết giờ nghỉ, giữ phút thô (in số dòng).
//   - Việc không ra SP (bảng không có cột ca): lấy ca của dòng WTS CÙNG người, CÙNG ngày (còn hiệu lực); 0 hoặc ≥ 2 ca → giữ phút thô.
//   - Chạy lại không sao: dòng đã có dòng nghỉ thì bỏ qua (thêm chỉ mục không trùng (cha, BreakStart) ở SQL).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BackfillNghi;

public static class BackfillCore
{
    public sealed class Item
    {
        public long? WtsLogId; public long? ActivityLogId; public int WorkerId; public DateTime Start; public DateTime End;
        public int CaId; public List<LineBreak> Rows = new();
        public int OldPageMin;   // phút trang WTS bản cũ (ngày của giờ bắt đầu + giờ nghỉ hiện tại của ca)
        public int NewMin;       // phút sau chép bù (WorkMinutes.Line, Thông = 0)
    }
    public sealed class Plan
    {
        public DateTime? Cutoff;
        public int WtsTotal, ActTotal, WtsHasBreaks, ActHasBreaks, WtsAfterCutoff, ActAfterCutoff;
        public int WtsNoCa, ActNoCa, ActManyCa, WtsNoTouch, ActNoTouch;
        public List<Item> Items = new();
        public int BreakRows => Items.Sum(i => i.Rows.Count);
    }

    // Công thức trang WTS bản cũ (HieuSuat_Index.CalcBreakMinutes trước ML17-XN): khoảng nghỉ theo ngày của giờ bắt đầu dòng
    static int OldPageMinutes(DateTime s, DateTime e, CaLamViec ca)
    {
        if (e <= s) return 0;
        var date = s.Date; double brk = 0;
        foreach (var n in ca.NghiGiaiLaos)
        {
            var ns = date.Add(n.NghiBatDau.ToTimeSpan()); var ne = date.Add(n.NghiKetThuc.ToTimeSpan());
            if (n.NghiKetThuc < n.NghiBatDau) ne = ne.AddDays(1);
            var a = ns < s ? s : ns; var b = ne > e ? e : ne;
            if (b > a) brk += (b - a).TotalMinutes;
        }
        return (int)Math.Round(Math.Max(0, (e - s).TotalMinutes - brk));
    }

    public static async Task<Plan> BuildAsync(AppDbContext db)
    {
        var plan = new Plan();
        var cas = await db.CaLamViecs.AsNoTracking().Include(c => c.NghiGiaiLaos).ToListAsync();
        var caById = cas.ToDictionary(c => c.CaId);
        var brk = await db.WtsBreakWorkLogs.AsNoTracking()
            .Select(x => new { x.WtsLogId, x.ActivityLogId, x.IsBackfill, x.CreatedAt }).ToListAsync();
        var hasW = brk.Where(x => x.WtsLogId != null).Select(x => x.WtsLogId!.Value).ToHashSet();
        var hasA = brk.Where(x => x.ActivityLogId != null).Select(x => x.ActivityLogId!.Value).ToHashSet();
        plan.Cutoff = brk.Where(x => !x.IsBackfill).Select(x => (DateTime?)x.CreatedAt).Min();

        var wts = await db.WtsProductionLogs.AsNoTracking()
            .Select(x => new { x.WtsLogId, x.WorkerId, x.StartTime, x.EndTime, x.CaId, x.CreatedAt, x.IsVoided }).ToListAsync();
        var acts = await db.WorkerActivityLogs.AsNoTracking()
            .Select(x => new { x.ActivityLogId, x.WorkerId, x.WorkDate, x.StartTime, x.EndTime, x.CreatedAt }).ToListAsync();
        plan.WtsTotal = wts.Count; plan.ActTotal = acts.Count;

        // ca của việc không ra SP = ca các dòng WTS còn hiệu lực cùng người cùng ngày (ngày của giờ bắt đầu, như trang nạp)
        var dayCa = wts.Where(w => !w.IsVoided && w.CaId != null)
            .GroupBy(w => (w.WorkerId, Day: DateOnly.FromDateTime(w.StartTime)))
            .ToDictionary(g => g.Key, g => g.Select(w => w.CaId!.Value).Distinct().ToList());

        foreach (var w in wts.OrderBy(w => w.WtsLogId))
        {
            if (hasW.Contains(w.WtsLogId)) { plan.WtsHasBreaks++; continue; }
            if (plan.Cutoff != null && w.CreatedAt >= plan.Cutoff) { plan.WtsAfterCutoff++; continue; }
            if (w.CaId == null || !caById.TryGetValue(w.CaId.Value, out var ca)) { plan.WtsNoCa++; continue; }
            var rows = WorkMinutes.Touching(ca, w.StartTime, w.EndTime);
            if (rows.Count == 0) { plan.WtsNoTouch++; continue; }
            plan.Items.Add(new Item
            {
                WtsLogId = w.WtsLogId, WorkerId = w.WorkerId, Start = w.StartTime, End = w.EndTime, CaId = ca.CaId, Rows = rows,
                OldPageMin = OldPageMinutes(w.StartTime, w.EndTime, ca), NewMin = WorkMinutes.Line(w.StartTime, w.EndTime, rows).Work,
            });
        }
        foreach (var a in acts.OrderBy(a => a.ActivityLogId))
        {
            if (hasA.Contains(a.ActivityLogId)) { plan.ActHasBreaks++; continue; }
            if (plan.Cutoff != null && a.CreatedAt >= plan.Cutoff) { plan.ActAfterCutoff++; continue; }
            if (!dayCa.TryGetValue((a.WorkerId, a.WorkDate), out var list) || list.Count == 0)
            {
                // (rà độc lập 01/11) ca qua đêm: việc sau nửa đêm có ngày khác ngày bắt đầu các dòng WTS của ca → xét dòng WTS cùng người trong ±12 giờ
                list = wts.Where(w => !w.IsVoided && w.CaId != null && w.WorkerId == a.WorkerId
                                   && w.EndTime > a.StartTime.AddHours(-12) && w.StartTime < a.EndTime.AddHours(12))
                          .Select(w => w.CaId!.Value).Distinct().ToList();
            }
            if (list.Count == 0) { plan.ActNoCa++; continue; }
            if (list.Count > 1) { plan.ActManyCa++; continue; }
            if (!caById.TryGetValue(list[0], out var ca)) { plan.ActNoCa++; continue; }
            var rows = WorkMinutes.Touching(ca, a.StartTime, a.EndTime);
            if (rows.Count == 0) { plan.ActNoTouch++; continue; }
            plan.Items.Add(new Item
            {
                ActivityLogId = a.ActivityLogId, WorkerId = a.WorkerId, Start = a.StartTime, End = a.EndTime, CaId = ca.CaId, Rows = rows,
                OldPageMin = OldPageMinutes(a.StartTime, a.EndTime, ca), NewMin = WorkMinutes.Line(a.StartTime, a.EndTime, rows).Work,
            });
        }
        return plan;
    }

    /// <summary>Ghi các dòng nghỉ của plan (gọi trong giao dịch). Trả số dòng đã thêm.</summary>
    public static async Task<int> WriteAsync(AppDbContext db, Plan plan)
    {
        int n = 0;
        foreach (var it in plan.Items)
        {
            var ents = WorkMinutes.ToEntities(it.Rows, it.WtsLogId, it.ActivityLogId, isBackfill: true).ToList();
            db.WtsBreakWorkLogs.AddRange(ents);
            n += ents.Count;
        }
        await db.SaveChangesAsync();
        return n;
    }

    public static void Write(Plan p, TextWriter w, string dbName, bool wrote)
    {
        w.WriteLine($"BackfillNghi — DB {dbName} — {DateTime.Now:dd/MM/yyyy HH:mm:ss} — {(wrote ? "ĐÃ GHI" : "CHỈ ĐỌC (chưa ghi gì)")}");
        w.WriteLine($"Mốc: {(p.Cutoff == null ? "chưa có dòng nghỉ nào do code mới ghi → xét mọi dòng" : "dòng tạo trước " + p.Cutoff.Value.ToString("dd/MM/yyyy HH:mm:ss"))}");
        w.WriteLine($"WTS: tổng {p.WtsTotal} | đã có giờ nghỉ {p.WtsHasBreaks} | tạo sau mốc {p.WtsAfterCutoff} | không có ca (giữ phút thô) {p.WtsNoCa} | không chạm nghỉ {p.WtsNoTouch} | CHÉP BÙ {p.Items.Count(i => i.WtsLogId != null)}");
        w.WriteLine($"Việc không ra SP: tổng {p.ActTotal} | đã có giờ nghỉ {p.ActHasBreaks} | tạo sau mốc {p.ActAfterCutoff} | không tìm được ca (giữ phút thô) {p.ActNoCa} | ≥ 2 ca cùng ngày (giữ phút thô) {p.ActManyCa} | không chạm nghỉ {p.ActNoTouch} | CHÉP BÙ {p.Items.Count(i => i.ActivityLogId != null)}");
        w.WriteLine($"Số dòng nghỉ sẽ / đã thêm: {p.BreakRows}");
        var diff = p.Items.Where(i => i.OldPageMin != i.NewMin).ToList();
        w.WriteLine($"Dòng có phút làm KHÁC trang WTS bản cũ (ca qua đêm / ngày khác ngày bắt đầu): {diff.Count}");
        foreach (var i in diff.Take(100))
            w.WriteLine($"  {(i.WtsLogId != null ? "WTS #" + i.WtsLogId : "Việc #" + i.ActivityLogId)} người {i.WorkerId} {i.Start:dd/MM HH:mm}–{i.End:dd/MM HH:mm} ca {i.CaId}: trang cũ {i.OldPageMin} → mới {i.NewMin}");
    }
}
