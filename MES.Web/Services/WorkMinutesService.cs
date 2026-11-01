using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

// ════════════════════════════════════════════════════════════════════════════════════════════════════════════
// [ML17-XN][CA-NGHI-HIST 2026-11-01] MỘT bộ hàm tính phút làm cho mọi nơi (Master list 17.2 "làm thông nghỉ" + lịch sử giờ nghỉ ca)
//   Thiết kế chốt 01/11 (Sổ luật Giao nhận, tab "Lỗi cần sửa", mục "ML17-XN + CA-NGHI-HIST"); Hùng chốt 01/11 14:06:
//   (a) bảng WtsBreakWorkLogs + chép bù dữ liệu cũ, (b) báo cáo % hiệu suất theo PHÚT LÀM, (c) ca qua đêm tính đúng ngày.
//   Dùng ở: trang WTS (cột Phút, Tổng hôm nay, Dự kiến, chia giờ nhóm, giờ kết thúc theo chuẩn), WtsSaveService (máy chủ tự
//   tính lại khoảng nghỉ lúc lưu), WtsReportService (báo cáo), MachineEfficiencyService (hiệu suất máy), tools/BackfillNghi.
//   KHÔNG trang nào tự tính giờ nghỉ riêng nữa (trước: HieuSuat_Index.CalcBreakMinutes / SkipBreaks / CalcEffectiveMinutes /
//   AddEffectiveMinutes lấy ngày của giờ bắt đầu dòng → ca qua đêm sai; dòng đã lưu đọc giờ nghỉ HIỆN TẠI của ca → admin sửa
//   giờ nghỉ hôm nay thì số ngày cũ đổi theo).
//
//   Luật 1 dòng (phút nguyên):
//     Thô = Kết thúc − Bắt đầu; Chồng_k = phần giao của dòng với khoảng nghỉ k; Thông_k ∈ [0, Chồng_k], khúc làm thông nằm ĐẦU
//     phần chồng: [max(BĐ nghỉ, BĐ dòng), + Thông_k). Thường = Thô − ΣChồng; Nghỉ bị trừ = Σ(Chồng − Thông);
//     Phút làm = Thường + ΣThông.  Bất biến: Thô = Thường + ΣThông + Nghỉ bị trừ.
//   Luật người-ngày (giờ chồng chỉ tính 1 lần): 1 phút là "làm" khi có dòng phủ nó VÀ (không nằm trong khoảng nghỉ dòng đó
//     chạm, hoặc nằm trong khúc làm thông của dòng đó). Bất biến: Thô gộp = Làm gộp + Nghỉ bị trừ gộp.
// ════════════════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>1 khoảng nghỉ của ca, giờ tuyệt đối (không cắt theo dòng).</summary>
public sealed record BreakSeg(DateTime Start, DateTime End, int? NghiId, string? Name)
{
    public int Minutes => WorkMinutes.Min(End - Start);
}

/// <summary>1 khoảng nghỉ mà 1 dòng chạm (bản trong bộ nhớ của WtsBreakWorkLog).</summary>
public sealed class LineBreak
{
    public DateTime BreakStart { get; set; }
    public DateTime BreakEnd   { get; set; }
    public int      OverlapMin { get; set; }
    public int      WorkedMin  { get; set; }
    public int?     CaId       { get; set; }
    public int?     NghiId     { get; set; }
    public string?  BreakName  { get; set; }
    public LineBreak Clone() => (LineBreak)MemberwiseClone();
    public string Label => $"{BreakStart:HH:mm}–{BreakEnd:HH:mm}" + (string.IsNullOrWhiteSpace(BreakName) ? "" : " " + BreakName!.Trim());
}

/// <summary>Phút 1 dòng: Thô = Thường + Thông + Trừ; Làm = Thường + Thông.</summary>
public readonly record struct LineMin(int Raw, int Normal, int Worked, int Deducted)
{
    public int Work => Normal + Worked;
}

/// <summary>Phút gộp nhiều dòng (người-ngày): Thô = Làm + Trừ; Làm = Thường + Thông.</summary>
public readonly record struct DayMin(int Raw, int Work, int Worked, int Deducted)
{
    public int Normal => Work - Worked;
}

/// <summary>1 dòng đưa vào phép gộp người-ngày.</summary>
public readonly record struct DayLine(DateTime Start, DateTime End, IReadOnlyList<LineBreak> Breaks);

public static class WorkMinutes
{
    /// <summary>Đổi khoảng thời gian ra phút nguyên (làm tròn, 0,5 lên). Dòng mới luôn tròn phút; dòng cũ có thể lẻ giây.</summary>
    public static int Min(TimeSpan t) => (int)Math.Round(t.TotalMinutes, MidpointRounding.AwayFromZero);

    /// <summary>Làm tròn 1 mốc giờ về phút gần nhất (≥ 30 giây lên).</summary>
    public static DateTime RoundToMinute(DateTime t)
    {
        var baseT = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
        return (t - baseT).TotalSeconds >= 30 ? baseT.AddMinutes(1) : baseT;
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────
    // Khoảng nghỉ của ca trong 1 khung giờ — [ML17-XN] câu (c): ca qua đêm tính đúng ngày.
    //   Xét mọi "ngày ca" từ (ngày của from − 1) tới ngày của to: ca qua đêm thì khoảng nghỉ có giờ bắt đầu < giờ bắt đầu ca thuộc
    //   ngày hôm sau của ngày ca (như CaLamViecService.GetBreakSegments của hiệu suất máy). Trước (trang WTS): luôn lấy ngày của
    //   giờ bắt đầu dòng → dòng 22:00–06:00 không trừ nghỉ 02:00, dòng 01:00–03:00 sau nửa đêm trừ nhầm.
    //   Khoảng nghỉ chồng nhau (dữ liệu ca cũ) → gộp lại 1 khoảng (không trừ 2 lần).
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────
    public static List<BreakSeg> BreakSegments(CaLamViec? ca, DateTime from, DateTime to)
    {
        var raw = new List<BreakSeg>();
        if (ca == null || to <= from || ca.NghiGiaiLaos == null || ca.NghiGiaiLaos.Count == 0) return raw;
        if (from < DateTime.MinValue.AddDays(3) || to > DateTime.MaxValue.AddDays(-3)) return raw;   // giờ chưa nhập (default) — không ném lỗi
        // gộp khoảng chồng TRƯỚC khi lọc theo khung → khoảng gộp như nhau dù khung (dòng / nhóm) chỉ chạm 1 phần (máy chủ = trang)
        for (var d = from.Date.AddDays(-2); d <= to.Date.AddDays(1); d = d.AddDays(1))
        {
            foreach (var n in ca.NghiGiaiLaos)
            {
                if (n.NghiKetThuc <= n.NghiBatDau) continue;   // CaLamViecService chặn — dữ liệu lạ thì bỏ
                var day = ca.IsOvernight && n.NghiBatDau < ca.GioBatDau ? d.AddDays(1) : d;
                // giờ nghỉ tròn phút (form ca nhập giờ:phút; lẻ giây thì làm tròn) → khớp datetime2(0) + CHECK OverlapMin ≤ DATEDIFF(MINUTE)
                var s = RoundToMinute(day.Add(n.NghiBatDau.ToTimeSpan()));
                var e = RoundToMinute(day.Add(n.NghiKetThuc.ToTimeSpan()));
                if (e <= s) continue;
                raw.Add(new BreakSeg(s, e, n.NghiId == 0 ? null : n.NghiId, string.IsNullOrWhiteSpace(n.GhiChu) ? null : n.GhiChu.Trim()));
            }
        }
        var merged = new List<BreakSeg>();
        foreach (var b in raw.OrderBy(x => x.Start).ThenBy(x => x.End))
        {
            if (merged.Count > 0 && b.Start < merged[^1].End)
            {
                var last = merged[^1];
                if (b.End > last.End) merged[^1] = last with { End = b.End };
                continue;
            }
            merged.Add(b);
        }
        return merged.Where(b => b.End > from && b.Start < to).ToList();
    }

    /// <summary>Phút chồng giữa [s, e) và 1 khoảng nghỉ.</summary>
    public static int Overlap(DateTime s, DateTime e, DateTime bs, DateTime be)
    {
        var a = s > bs ? s : bs; var b = e < be ? e : be;
        return b > a ? Min(b - a) : 0;
    }

    /// <summary>Mọi khoảng nghỉ của ca mà dòng [s, e) chạm (Chồng &gt; 0), Thông = 0.</summary>
    public static List<LineBreak> Touching(CaLamViec? ca, DateTime s, DateTime e)
        => TouchingSegs(BreakSegments(ca, s, e), s, e, ca?.CaId);

    public static List<LineBreak> TouchingSegs(IEnumerable<BreakSeg> segs, DateTime s, DateTime e, int? caId)
    {
        var list = new List<LineBreak>();
        foreach (var b in segs)
        {
            var ov = Overlap(s, e, b.Start, b.End);
            if (ov <= 0) continue;
            list.Add(new LineBreak { BreakStart = b.Start, BreakEnd = b.End, OverlapMin = ov, WorkedMin = 0, CaId = caId, NghiId = b.NghiId, BreakName = b.Name });
        }
        return list;
    }

    /// <summary>Khúc làm thông của 1 khoảng nghỉ trong 1 dòng: đầu phần chồng.</summary>
    public static (DateTime S, DateTime E) WorkedChunk(DateTime lineStart, LineBreak b)
    {
        var s = b.BreakStart > lineStart ? b.BreakStart : lineStart;
        return (s, s.AddMinutes(Math.Max(0, b.WorkedMin)));
    }

    /// <summary>Phút 1 dòng từ bộ khoảng nghỉ đã lưu / đang nhập. Bất biến Thô = Thường + Thông + Trừ luôn đúng.</summary>
    public static LineMin Line(DateTime s, DateTime e, IEnumerable<LineBreak>? breaks)
    {
        var raw = e > s ? Min(e - s) : 0;
        int ov = 0, wk = 0;
        if (breaks != null)
            foreach (var b in breaks)
            {
                var o = Math.Max(0, b.OverlapMin);
                ov += o; wk += Math.Clamp(b.WorkedMin, 0, o);
            }
        var normal = raw - ov;
        if (normal < 0) { normal = 0; raw = ov; }   // chỉ dữ liệu cũ lẻ giây làm tròn lệch — giữ bất biến
        return new LineMin(raw, normal, wk, ov - wk);
    }

    // ── Phép tính khoảng trên trục thời gian (tick) ───────────────────────────────────────────────────────────
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
    static List<(DateTime S, DateTime E)> Subtract((DateTime S, DateTime E) a, IEnumerable<(DateTime S, DateTime E)> cut)
    {
        var res = new List<(DateTime S, DateTime E)>();
        var cur = a.S;
        foreach (var c in Union(cut))
        {
            if (c.E <= cur || c.S >= a.E) continue;
            if (c.S > cur) res.Add((cur, c.S));
            if (c.E > cur) cur = c.E;
            if (cur >= a.E) break;
        }
        if (cur < a.E) res.Add((cur, a.E));
        return res;
    }
    static double Measure(IEnumerable<(DateTime S, DateTime E)> iv) => Union(iv).Sum(x => (x.E - x.S).TotalMinutes);

    /// <summary>[a, b) trừ đi các khoảng cut (dùng ở hiệu suất máy).</summary>
    public static List<(DateTime S, DateTime E)> Minus((DateTime S, DateTime E) a, IEnumerable<(DateTime S, DateTime E)> cut) => Subtract(a, cut);

    /// <summary>
    /// Phần giờ LÀM của 1 dòng trên trục thời gian = [BĐ, KT) trừ (mỗi khoảng nghỉ đã lưu trừ khúc làm thông) — hiệu suất máy dùng
    /// để vẽ đoạn chạy (nghỉ không đè khúc làm thông).
    /// </summary>
    public static List<(DateTime S, DateTime E)> WorkPieces(DateTime s, DateTime e, IEnumerable<LineBreak>? breaks)
    {
        if (e <= s) return new();
        var deduct = new List<(DateTime S, DateTime E)>();
        foreach (var b in breaks ?? Array.Empty<LineBreak>())
        {
            if (b.WorkedMin <= 0) { deduct.Add((b.BreakStart, b.BreakEnd)); continue; }
            var c = WorkedChunk(s, b);
            deduct.AddRange(Subtract((b.BreakStart, b.BreakEnd), new[] { (c.S, c.E) }));
        }
        return Subtract((s, e), deduct);
    }

    /// <summary>
    /// Gộp người-ngày (Tổng hôm nay, báo cáo): giờ chồng tính 1 lần; phút "làm" = có dòng phủ và (ngoài khoảng nghỉ dòng đó chạm,
    /// hoặc trong khúc làm thông của dòng đó). Thường gộp = phần làm không nhờ khúc thông; Thông gộp = Làm − Thường.
    /// </summary>
    public static DayMin Day(IEnumerable<DayLine> lines)
    {
        var rawIv = new List<(DateTime, DateTime)>();
        var normalIv = new List<(DateTime, DateTime)>();
        var chunkIv = new List<(DateTime, DateTime)>();
        foreach (var l in lines)
        {
            if (l.End <= l.Start) continue;
            rawIv.Add((l.Start, l.End));
            var brk = (l.Breaks ?? Array.Empty<LineBreak>()).Select(b => (b.BreakStart, b.BreakEnd)).ToList();
            normalIv.AddRange(Subtract((l.Start, l.End), brk));
            foreach (var b in l.Breaks ?? Array.Empty<LineBreak>())
            {
                if (b.WorkedMin <= 0) continue;
                var c = WorkedChunk(l.Start, b);
                var ce = c.E < l.End ? c.E : l.End;
                var be = ce < b.BreakEnd ? ce : b.BreakEnd;
                if (be > c.S) chunkIv.Add((c.S, be));
            }
        }
        var raw = (int)Math.Round(Measure(rawIv), MidpointRounding.AwayFromZero);
        var normal = (int)Math.Round(Measure(normalIv), MidpointRounding.AwayFromZero);
        var work = (int)Math.Round(Measure(normalIv.Concat(chunkIv)), MidpointRounding.AwayFromZero);
        if (normal > work) normal = work;
        if (work > raw) work = raw;
        return new DayMin(raw, work, work - normal, raw - work);
    }

    // ── Dự kiến / chia giờ nhóm: khoảng nghỉ HIỆU LỰC = khoảng nghỉ trừ khúc làm thông ───────────────────────────
    /// <summary>
    /// Khoảng nghỉ còn hiệu lực khi có làm thông: khoảng k bỏ đi khúc [max(BĐ nghỉ, mốc bắt đầu), + Thông_k).
    /// worked: phút làm thông theo giờ bắt đầu khoảng nghỉ (null / thiếu = 0 = nghỉ thật).
    /// </summary>
    public static List<(DateTime S, DateTime E)> EffectiveBreaks(IEnumerable<BreakSeg> segs, DateTime anchorStart, IReadOnlyDictionary<DateTime, int>? worked)
    {
        var res = new List<(DateTime S, DateTime E)>();
        foreach (var b in segs)
        {
            var w = worked != null && worked.TryGetValue(b.Start, out var x) ? Math.Max(0, x) : 0;
            if (w <= 0) { res.Add((b.Start, b.End)); continue; }
            var cs = b.Start > anchorStart ? b.Start : anchorStart;
            var ce = cs.AddMinutes(w);
            if (cs > b.Start) res.Add((b.Start, cs));
            if (ce < b.End) res.Add((ce, b.End));
        }
        return Union(res);
    }

    /// <summary>Phút trong [s, e) nằm trong khoảng nghỉ hiệu lực.</summary>
    public static double BreakMinutesIn(DateTime s, DateTime e, IEnumerable<(DateTime S, DateTime E)> eff)
    {
        if (e <= s) return 0;
        double t = 0;
        foreach (var b in eff)
        {
            var a = s > b.S ? s : b.S; var z = e < b.E ? e : b.E;
            if (z > a) t += (z - a).TotalMinutes;
        }
        return t;
    }

    /// <summary>Phút làm trong [s, e) (trừ khoảng nghỉ hiệu lực).</summary>
    public static double EffectiveMinutes(DateTime s, DateTime e, IEnumerable<(DateTime S, DateTime E)> eff)
        => e <= s ? 0 : Math.Max(0, (e - s).TotalMinutes - BreakMinutesIn(s, e, eff));

    /// <summary>Mốc t rơi trong khoảng nghỉ hiệu lực → đẩy ra cuối khoảng đó (lặp tới khi ra ngoài).</summary>
    public static DateTime SkipBreaks(DateTime t, IEnumerable<(DateTime S, DateTime E)> eff)
    {
        var list = eff.OrderBy(b => b.S).ToList();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in list)
                if (t >= b.S && t < b.E) { t = b.E; changed = true; }
        }
        return t;
    }

    /// <summary>Giờ kết thúc sau khi làm đủ <paramref name="prodMin"/> phút, nhảy qua khoảng nghỉ hiệu lực (thay AddEffectiveMinutes của trang).</summary>
    public static DateTime AddEffective(DateTime start, double prodMin, IEnumerable<(DateTime S, DateTime E)> eff)
    {
        if (prodMin <= 0) return start;
        var cursor = start;
        var rem = prodMin;
        foreach (var brk in eff.Where(b => b.E > start).OrderBy(b => b.S))
        {
            if (rem <= 0) break;
            if (cursor >= brk.S && cursor < brk.E) { cursor = brk.E; continue; }
            var toBreak = (brk.S - cursor).TotalMinutes;
            if (toBreak <= 0) continue;
            if (rem <= toBreak) return cursor.AddMinutes(rem);
            rem -= toBreak;
            cursor = brk.E;
        }
        return cursor.AddMinutes(rem);
    }

    // ── Chia khúc làm thông của cả nhóm (HTSP / KCS / Đóng gói / GC nhiều PO) cho từng dòng con ───────────────────
    /// <summary>
    /// Nhóm tích "Làm thông nghỉ" ở cấp nhóm: khúc làm thông của khoảng k = [max(BĐ nghỉ, BĐ nhóm), + Thông_k).
    /// Dòng con nhận phần khúc đó nằm trong giờ của nó (theo thứ tự thời gian từ đầu khoảng nghỉ) → Thông_dòng = |khúc ∩ dòng|.
    /// </summary>
    public static List<LineBreak> SplitForLine(IEnumerable<BreakSeg> groupSegs, DateTime groupStart, IReadOnlyDictionary<DateTime, int>? worked,
                                              DateTime lineStart, DateTime lineEnd, int? caId)
    {
        var list = TouchingSegs(groupSegs, lineStart, lineEnd, caId);
        foreach (var b in list)
        {
            var w = worked != null && worked.TryGetValue(b.BreakStart, out var x) ? Math.Max(0, x) : 0;
            if (w <= 0) continue;
            var cs = b.BreakStart > groupStart ? b.BreakStart : groupStart;
            var ce = cs.AddMinutes(w);
            b.WorkedMin = Math.Clamp(Overlap(lineStart, lineEnd, cs, ce < b.BreakEnd ? ce : b.BreakEnd), 0, b.OverlapMin);
        }
        return list;
    }

    // ── Máy chủ: kiểm bộ khoảng nghỉ trang gửi với khoảng nghỉ tự tính lại từ ca + giờ dòng ───────────────────────
    /// <summary>Khoảng nghỉ trang gửi lên cho 1 dòng.</summary>
    public sealed class SentBreak
    {
        public DateTime BreakStart { get; set; }
        public DateTime BreakEnd   { get; set; }
        public int      WorkedMin  { get; set; }
    }

    public const string MsgCaChanged = "Giờ nghỉ của ca vừa đổi (khác lúc mở trang) — tải lại trang rồi nhập lại phần làm thông nghỉ.";

    /// <summary>
    /// Máy chủ tự tính khoảng nghỉ dòng chạm (ca + giờ dòng), so với bộ trang gửi: phải TRÙNG tập (giờ bắt đầu, giờ kết thúc)
    /// — khác = ca vừa đổi giờ nghỉ (trang để cũ) → chặn; phút làm thông phải nguyên trong [0, Chồng]. Trả (bộ để ghi, lời báo).
    /// </summary>
    public static (List<LineBreak> Rows, string? Error) Verify(CaLamViec? ca, DateTime s, DateTime e, IReadOnlyList<SentBreak>? sent)
    {
        var calc = Touching(ca, s, e);
        sent ??= Array.Empty<SentBreak>();
        if (sent.Count != calc.Count
            || sent.Any(x => !calc.Any(c => c.BreakStart == x.BreakStart && c.BreakEnd == x.BreakEnd))
            || sent.Select(x => x.BreakStart).Distinct().Count() != sent.Count)
            return (calc, MsgCaChanged);
        foreach (var c in calc)
        {
            var w = sent.First(x => x.BreakStart == c.BreakStart).WorkedMin;
            if (w < 0 || w > c.OverlapMin)
                return (calc, $"Phút làm thông nghỉ {c.Label}: {w} — phải từ 0 đến {c.OverlapMin} phút (phần giờ dòng chồng khoảng nghỉ).");
            c.WorkedMin = w;
        }
        return (calc, null);
    }

    /// <summary>Đổi bộ khoảng nghỉ trong bộ nhớ thành dòng DB của 1 dòng cha.</summary>
    public static IEnumerable<WtsBreakWorkLog> ToEntities(IEnumerable<LineBreak> rows, long? wtsLogId, long? activityLogId, bool isBackfill = false)
        => rows.Select(b => new WtsBreakWorkLog
        {
            WtsLogId = wtsLogId, ActivityLogId = activityLogId, CaId = b.CaId, NghiId = b.NghiId,
            BreakName = b.BreakName == null ? null : (b.BreakName.Length > 100 ? b.BreakName[..100] : b.BreakName),
            BreakStart = b.BreakStart, BreakEnd = b.BreakEnd, OverlapMin = b.OverlapMin, WorkedMin = Math.Clamp(b.WorkedMin, 0, b.OverlapMin),
            IsBackfill = isBackfill, CreatedAt = DateTime.Now,
        });

    public static LineBreak FromEntity(WtsBreakWorkLog x) => new()
    {
        BreakStart = x.BreakStart, BreakEnd = x.BreakEnd, OverlapMin = x.OverlapMin, WorkedMin = x.WorkedMin,
        CaId = x.CaId, NghiId = x.NghiId, BreakName = x.BreakName,
    };
}

/// <summary>[ML17-XN] Đọc khoảng nghỉ đã lưu của nhiều dòng (WTS / việc không ra SP) — dùng chung trang WTS, báo cáo, hiệu suất máy.</summary>
public static class WorkMinutesStore
{
    const int Chunk = 1000;

    public static async Task<Dictionary<long, List<LineBreak>>> ForWtsAsync(AppDbContext db, IEnumerable<long> ids)
    {
        var res = new Dictionary<long, List<LineBreak>>();
        foreach (var part in ids.Distinct().Chunk(Chunk))
        {
            var p = part.ToList();
            var rows = await db.WtsBreakWorkLogs.AsNoTracking().Where(x => x.WtsLogId != null && p.Contains(x.WtsLogId.Value)).ToListAsync();
            foreach (var x in rows.OrderBy(x => x.BreakStart))
            {
                if (!res.TryGetValue(x.WtsLogId!.Value, out var l)) res[x.WtsLogId.Value] = l = new();
                l.Add(WorkMinutes.FromEntity(x));
            }
        }
        return res;
    }

    public static async Task<Dictionary<long, List<LineBreak>>> ForActivityAsync(AppDbContext db, IEnumerable<long> ids)
    {
        var res = new Dictionary<long, List<LineBreak>>();
        foreach (var part in ids.Distinct().Chunk(Chunk))
        {
            var p = part.ToList();
            var rows = await db.WtsBreakWorkLogs.AsNoTracking().Where(x => x.ActivityLogId != null && p.Contains(x.ActivityLogId.Value)).ToListAsync();
            foreach (var x in rows.OrderBy(x => x.BreakStart))
            {
                if (!res.TryGetValue(x.ActivityLogId!.Value, out var l)) res[x.ActivityLogId.Value] = l = new();
                l.Add(WorkMinutes.FromEntity(x));
            }
        }
        return res;
    }

    public static IReadOnlyList<LineBreak> Get(Dictionary<long, List<LineBreak>> map, long? id)
        => id.HasValue && map.TryGetValue(id.Value, out var l) ? l : Array.Empty<LineBreak>();
}
