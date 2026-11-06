// [ML17-XN][CA-NGHI-HIST 2026-11-01] Kịch bản `nghi`: làm thông nghỉ + giờ nghỉ chép lúc lưu + ca qua đêm.
//   N1  hàm phút (WorkMinutes) đối chiếu ĐÁP ÁN ĐỘC LẬP đếm từng phút: khoảng nghỉ theo giờ trong ngày (ca qua đêm), bất biến
//       Thô = Thường + Thông + Trừ, gộp người-ngày, Dự kiến (AddEffective), chia khúc làm thông cho dòng con — ngẫu nhiên nhiều ca.
//   N2  lưu WTS bằng service thật: ghi dòng nghỉ đúng, máy chủ chặn bộ nghỉ sai / phút ngoài [0, chồng] / ca vừa đổi giờ nghỉ,
//       sửa dòng giữ dòng nghỉ của dòng đã huỷ, dòng WTS (sổ số lượng) giống hệt có / không làm thông nghỉ.
//   N3  việc không ra sản phẩm: ca + giờ nghỉ lúc lưu, chặn tương tự.
//   N4  báo cáo = trang (WtsReportService phút làm = WorkMinutes; tổng gộp người-ngày; % theo phút làm), hiệu suất máy
//       (khúc làm thông là Chạy), đổi giờ nghỉ ca SAU khi lưu → số ngày cũ KHÔNG đổi (CA-NGHI-HIST).
//   N5  BackfillNghi: chép bù dòng cũ, suy ca việc không ra SP, chạy lại không thêm, dòng sau mốc không đụng; ca 2 nghỉ chồng bị chặn.
//   N6  ngẫu nhiên: nhiều nhóm × khoảng nghỉ × phút làm thông qua service → mọi dòng nghỉ trong DB khớp tính lại, bất biến dòng + ngày.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class NghiScen
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        if (!cond) { fails++; Console.WriteLine("  SAI  " + name + (info != null ? "\n         → " + info : "")); }
    }
    static void Say(bool cond, string name, string? info = null) { Ok(cond, name, info); if (cond) Console.WriteLine("  ĐẠT  " + name); }

    static readonly DateTime D0 = DateTime.Today;

    // ── Đáp án độc lập: phút t nằm trong khoảng nghỉ khi GIỜ TRONG NGÀY của t thuộc [BĐ nghỉ, KT nghỉ) của 1 khoảng (nghỉ lặp mỗi ngày,
    //    không vắt nửa đêm) — đúng với cả ca qua đêm mà không cần biết "ngày ca".
    static bool InBreak(CaLamViec ca, DateTime t)
    {
        var tod = TimeOnly.FromDateTime(t);
        return ca.NghiGiaiLaos.Any(n => tod >= n.NghiBatDau && tod < n.NghiKetThuc);
    }
    static int OracleOverlap(CaLamViec ca, DateTime s, DateTime e)
    {
        int c = 0; for (var t = s; t < e; t = t.AddMinutes(1)) if (InBreak(ca, t)) c++; return c;
    }

    static CaLamViec RandCa(Random rnd, int id)
    {
        bool night = rnd.Next(3) == 0;
        var ca = new CaLamViec { CaId = id, TenCa = "C" + id, IsActive = true,
            GioBatDau = night ? new TimeOnly(20 + rnd.Next(3), 0) : new TimeOnly(6 + rnd.Next(3), 0),
            GioKetThuc = night ? new TimeOnly(4 + rnd.Next(3), 0) : new TimeOnly(15 + rnd.Next(3), 30) };
        int k = 1 + rnd.Next(3); var used = new List<(int S, int E)>();
        for (int i = 0; i < k * 4 && used.Count < k; i++)
        {
            int s = rnd.Next(0, 24 * 60 - 70) / 5 * 5, len = 5 * (1 + rnd.Next(12));
            if (s + len >= 24 * 60) continue;
            if (used.Any(u => s < u.E && u.S < s + len)) continue;   // không chồng (CaLamViecService chặn)
            used.Add((s, s + len));
            ca.NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { NghiId = id * 10 + used.Count, CaId = id, NghiBatDau = new TimeOnly(s / 60, s % 60), NghiKetThuc = new TimeOnly((s + len) / 60, (s + len) % 60), GhiChu = "N" + used.Count });
        }
        // hay gặp: nghỉ trưa / nghỉ đêm 02:00
        if (rnd.Next(2) == 0 && !used.Any(u => u.S < 13 * 60 && 12 * 60 < u.E))
            ca.NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { NghiId = id * 10 + 9, CaId = id, NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0), GhiChu = "Trưa" });
        return ca;
    }

    static void N1(int seed, int n)
    {
        var rnd = new Random(seed);
        int oldWrong = 0;
        for (int it = 0; it < n; it++)
        {
            var ca = RandCa(rnd, 1);
            var s = D0.AddMinutes(rnd.Next(0, 36 * 60));
            var e = s.AddMinutes(1 + rnd.Next(18 * 60));
            var touch = WorkMinutes.Touching(ca, s, e);
            var oracle = OracleOverlap(ca, s, e);
            Ok(touch.Sum(t => t.OverlapMin) == oracle, $"N1 chồng nghỉ = đếm từng phút (ca {ca.Label}, {s:dd HH:mm}–{e:dd HH:mm})", $"hàm {touch.Sum(t => t.OverlapMin)} đáp án {oracle}");
            // công thức trang cũ (ngày của giờ bắt đầu) — chỉ đếm để báo, ca qua đêm hay sai
            double oldBrk = 0; foreach (var nn in ca.NghiGiaiLaos)
            { var ns = s.Date.Add(nn.NghiBatDau.ToTimeSpan()); var ne = s.Date.Add(nn.NghiKetThuc.ToTimeSpan()); var a = ns < s ? s : ns; var b = ne > e ? e : ne; if (b > a) oldBrk += (b - a).TotalMinutes; }
            if ((int)oldBrk != oracle) oldWrong++;
            // phút làm thông ngẫu nhiên
            foreach (var t in touch) t.WorkedMin = rnd.Next(3) == 0 ? 0 : rnd.Next(0, t.OverlapMin + 1);
            var lm = WorkMinutes.Line(s, e, touch);
            Ok(lm.Raw == lm.Normal + lm.Worked + lm.Deducted && lm.Normal >= 0 && lm.Worked >= 0 && lm.Deducted >= 0, "N1 bất biến dòng", lm.ToString());
            Ok(lm.Raw == (int)(e - s).TotalMinutes, "N1 thô = KT − BĐ");
            // đáp án độc lập phút làm: ngoài nghỉ, hoặc trong khúc làm thông [max(BĐ nghỉ, BĐ dòng), + Thông)
            int work = 0;
            for (var t = s; t < e; t = t.AddMinutes(1))
            {
                var b = touch.FirstOrDefault(x => t >= x.BreakStart && t < x.BreakEnd);
                if (b == null) { work++; continue; }
                var cs = b.BreakStart > s ? b.BreakStart : s;
                if (t >= cs && t < cs.AddMinutes(b.WorkedMin)) work++;
            }
            Ok(lm.Work == work, "N1 phút làm = đếm từng phút", $"hàm {lm.Work} đáp án {work}");
            var dm = WorkMinutes.Day(new[] { new DayLine(s, e, touch) });
            Ok(dm.Raw == lm.Raw && dm.Work == lm.Work && dm.Worked == lm.Worked, "N1 gộp 1 dòng = dòng", $"{dm} / {lm}");
            var dm2 = WorkMinutes.Day(new[] { new DayLine(s, e, touch), new DayLine(s, e, touch) });
            Ok(dm2 == dm, "N1 dòng trùng 2 lần tính 1 lần");
            // Verify máy chủ: bộ đúng qua, thiếu 1 khoảng / phút vượt → chặn
            var sent = touch.Select(t => new WorkMinutes.SentBreak { BreakStart = t.BreakStart, BreakEnd = t.BreakEnd, WorkedMin = t.WorkedMin }).ToList();
            var v = WorkMinutes.Verify(ca, s, e, sent);
            Ok(v.Error == null && v.Rows.Select(x => x.WorkedMin).SequenceEqual(touch.Select(x => x.WorkedMin)), "N1 máy chủ nhận bộ đúng", v.Error);
            if (sent.Count > 0)
            {
                Ok(WorkMinutes.Verify(ca, s, e, sent.Skip(1).ToList()).Error == WorkMinutes.MsgCaChanged, "N1 thiếu khoảng nghỉ → chặn");
                var bad = sent.Select(x => new WorkMinutes.SentBreak { BreakStart = x.BreakStart, BreakEnd = x.BreakEnd, WorkedMin = x.WorkedMin }).ToList();
                bad[0].WorkedMin = touch[0].OverlapMin + 1;
                Ok(WorkMinutes.Verify(ca, s, e, bad).Error?.Contains("phải từ 0 đến") == true, "N1 phút làm thông > chồng → chặn");
                bad[0].WorkedMin = -1;
                Ok(WorkMinutes.Verify(ca, s, e, bad).Error?.Contains("phải từ 0 đến") == true, "N1 phút âm → chặn");
                var shifted = sent.Select(x => new WorkMinutes.SentBreak { BreakStart = x.BreakStart.AddMinutes(5), BreakEnd = x.BreakEnd, WorkedMin = 0 }).ToList();
                Ok(WorkMinutes.Verify(ca, s, e, shifted).Error == WorkMinutes.MsgCaChanged, "N1 giờ nghỉ khác (ca vừa đổi) → chặn");
            }
            else Ok(WorkMinutes.Verify(ca, s, e, new List<WorkMinutes.SentBreak> { new() { BreakStart = s, BreakEnd = e, WorkedMin = 0 } }).Error == WorkMinutes.MsgCaChanged, "N1 gửi khoảng không có → chặn");

            // Dự kiến: AddEffective = bước từng phút trên khoảng nghỉ hiệu lực
            var worked = touch.ToDictionary(t => t.BreakStart, t => t.WorkedMin);
            var eff = WorkMinutes.EffectiveBreaks(WorkMinutes.BreakSegments(ca, s, s.AddDays(2)), s, worked);
            int prod = rnd.Next(0, 600);
            var end = WorkMinutes.AddEffective(s, prod, eff);
            var cur = s; int done = 0;
            while (done < prod) { if (!eff.Any(b => cur >= b.S && cur < b.E)) done++; cur = cur.AddMinutes(1); }
            Ok(end == cur,
               "N1 Dự kiến = bước từng phút", $"prod {prod} hàm {end:dd HH:mm} đáp án {cur:dd HH:mm}");
            Ok(Math.Abs(WorkMinutes.EffectiveMinutes(s, e, eff) - lm.Work) < 0.001, "N1 phút làm theo nghỉ hiệu lực = phút dòng", $"{WorkMinutes.EffectiveMinutes(s, e, eff)} / {lm.Work}");

            // chia khúc làm thông cho dòng con liên tiếp (nhóm HTSP / KCS / Đóng gói): tổng Thông dòng con = Thông của nhóm, mỗi dòng ≤ chồng
            int cuts = rnd.Next(1, 4); var pts = new List<DateTime> { s };
            for (int c = 0; c < cuts; c++) pts.Add(s.AddMinutes(rnd.Next(0, (int)(e - s).TotalMinutes + 1)));
            pts.Add(e); pts = pts.OrderBy(x => x).ToList();
            var segs = WorkMinutes.BreakSegments(ca, s, e);
            var subs = new List<List<LineBreak>>();
            for (int c = 0; c + 1 < pts.Count; c++)
                if (pts[c + 1] > pts[c]) subs.Add(WorkMinutes.SplitForLine(segs, s, worked, pts[c], pts[c + 1], 1));
            foreach (var t in touch)
            {
                var sumW = subs.Sum(l => l.Where(x => x.BreakStart == t.BreakStart).Sum(x => x.WorkedMin));
                var sumO = subs.Sum(l => l.Where(x => x.BreakStart == t.BreakStart).Sum(x => x.OverlapMin));
                Ok(sumW == t.WorkedMin && sumO == t.OverlapMin, "N1 chia khúc làm thông cho dòng con = cả nhóm", $"{t.Label}: thông {sumW}/{t.WorkedMin}, chồng {sumO}/{t.OverlapMin}");
            }
            Ok(subs.All(l => l.All(x => x.WorkedMin >= 0 && x.WorkedMin <= x.OverlapMin)), "N1 dòng con 0 ≤ thông ≤ chồng");
            // gộp các dòng con (liên tiếp) = cả nhóm
            int k2 = 0; var lines = new List<DayLine>();
            for (int c = 0; c + 1 < pts.Count; c++) if (pts[c + 1] > pts[c]) lines.Add(new DayLine(pts[c], pts[c + 1], subs[k2++]));
            var dsub = WorkMinutes.Day(lines);
            Ok(dsub.Raw == lm.Raw && dsub.Work == lm.Work && dsub.Worked == lm.Worked, "N1 gộp dòng con = nhóm", $"{dsub} / {lm}");
        }
        Console.WriteLine($"  N1: {n} lượt ngẫu nhiên — công thức trang cũ sai {oldWrong} lượt (ca qua đêm / dòng qua ngày), hàm mới khớp đáp án");
    }

    static CaLamViec AddCa(AppDbContext db, int id, TimeOnly bd, TimeOnly kt, params (int h1, int m1, int h2, int m2, string name)[] br)
    {
        var ca = db.CaLamViecs.List.FirstOrDefault(c => c.CaId == id);
        if (ca == null) { ca = new CaLamViec { CaId = id, TenCa = "Ca" + id, IsActive = true }; db.CaLamViecs.List.Add(ca); }
        ca.GioBatDau = bd; ca.GioKetThuc = kt;
        ca.NghiGiaiLaos.Clear(); db.CaLamViec_NghiGiaiLaos.List.RemoveAll(n => n.CaId == id);
        int k = 0;
        foreach (var b in br)
        {
            var n = new CaLamViec_NghiGiaiLao { NghiId = id * 100 + ++k, CaId = id, NghiBatDau = new TimeOnly(b.h1, b.m1), NghiKetThuc = new TimeOnly(b.h2, b.m2), GhiChu = b.name };
            ca.NghiGiaiLaos.Add(n); db.CaLamViec_NghiGiaiLaos.List.Add(n);
        }
        return ca;
    }

    static List<WorkMinutes.SentBreak> Sent(CaLamViec ca, DateTime s, DateTime e, Func<LineBreak, int>? w = null)
        => WorkMinutes.Touching(ca, s, e).Select(t => new WorkMinutes.SentBreak { BreakStart = t.BreakStart, BreakEnd = t.BreakEnd, WorkedMin = w?.Invoke(t) ?? 0 }).ToList();

    static List<WtsBreakWorkLog> BrkOf(AppDbContext db, long? wts = null, long? act = null)
        => db.WtsBreakWorkLogs.List.Where(x => (wts != null && x.WtsLogId == wts) || (act != null && x.ActivityLogId == act)).OrderBy(x => x.BreakStart).ToList();

    static object WtsKey(WtsProductionLog w) => (w.KhPlanDetailId, w.ProcessGroup, w.NC, w.WtsCode, w.QtyDone, w.QtyOk, w.QtyNg, w.StartTime, w.EndTime, w.MachineUsed, w.CaId, w.IsVoided, w.NgCaseId);

    static async Task N2()
    {
        Console.WriteLine("[N2] Lưu WTS: dòng nghỉ đúng, máy chủ chặn bộ nghỉ sai, sửa dòng giữ dòng nghỉ cũ, sổ số lượng không đổi");
        var db = await WtsScen.SeedAsync(); var s = WtsScen.Svc(db);
        var ca = AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (9, 30, 9, 40, "Giải lao"), (12, 0, 13, 0, "Nghỉ trưa"));
        var st = D0.AddHours(9); var en = D0.AddHours(13).AddMinutes(30);   // chạm 9:30–9:40 (10) + trưa (60)

        // (a) không gửi bộ nghỉ → chặn, không ghi gì
        var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 1, 0, st, en));
        var r = await s.SaveGroupAsync(g);
        Say(!r.Ok && r.ErrorMsg == WorkMinutes.MsgCaChanged && db.WtsProductionLogs.List.Count == 0 && db.WtsBreakWorkLogs.List.Count == 0, "Dòng chạm nghỉ mà trang không gửi khoảng nghỉ → chặn \"giờ nghỉ ca vừa đổi\", không ghi gì", r.ErrorMsg);
        // (b) phút làm thông vượt chồng → chặn
        g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 1, 0, st, en)); g.Rows[0].Breaks = Sent(ca, st, en, t => t.OverlapMin + 1);
        r = await s.SaveGroupAsync(g);
        Say(!r.Ok && r.ErrorMsg!.Contains("phải từ 0 đến 10") && db.WtsProductionLogs.List.Count == 0, "Phút làm thông > phần chồng → chặn", r.ErrorMsg);
        // (c) đúng: giải lao làm 10, trưa làm 20
        g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 1, 0, st, en)); g.Rows[0].Breaks = Sent(ca, st, en, t => t.OverlapMin == 10 ? 10 : 20);
        r = await s.SaveGroupAsync(g); db.Fixup();
        var w1 = db.WtsProductionLogs.List.Single();
        var b1 = BrkOf(db, w1.WtsLogId);
        Say(r.Ok && b1.Count == 2 && b1[0].OverlapMin == 10 && b1[0].WorkedMin == 10 && b1[1].OverlapMin == 60 && b1[1].WorkedMin == 20
            && b1[1].BreakStart == D0.AddHours(12) && b1[1].BreakEnd == D0.AddHours(13) && b1.All(x => x.CaId == 1 && x.ActivityLogId == null && !x.IsBackfill)
            && b1[1].BreakName == "Nghỉ trưa",
            "Lưu: 2 dòng nghỉ (giờ tuyệt đối, chồng 10 / 60, làm 10 / 20, tên khoảng, ca)", r.ErrorMsg ?? string.Join("; ", b1.Select(x => $"{x.BreakStart:HH:mm} {x.OverlapMin}/{x.WorkedMin}")));
        var lm = WorkMinutes.Line(w1.StartTime, w1.EndTime, b1.Select(WorkMinutes.FromEntity));
        Say(lm.Raw == 270 && lm.Work == 230 && lm.Worked == 30 && lm.Deducted == 40, "Phút dòng: thô 270 = thường 200 + thông 30 + trừ 40", lm.ToString());

        // (d) sửa dòng (huỷ + mới): dòng nghỉ của dòng đã huỷ giữ nguyên, dòng mới có bộ riêng (làm thông 0)
        var ge = WtsScen.Gc(); var re = WtsScen.Row(1, 1, 0, st, en); re.IsEditing = true; re.WtsLogId = w1.WtsLogId; re.Breaks = Sent(ca, st, en); ge.Rows.Add(re);
        r = await s.SaveGroupAsync(ge); db.Fixup();
        var w2 = db.WtsProductionLogs.List.Single(x => !x.IsVoided);
        Say(r.Ok && db.WtsProductionLogs.List.Single(x => x.WtsLogId == w1.WtsLogId).IsVoided && BrkOf(db, w1.WtsLogId).Count == 2
            && BrkOf(db, w1.WtsLogId).Sum(x => x.WorkedMin) == 30 && BrkOf(db, w2.WtsLogId).Count == 2 && BrkOf(db, w2.WtsLogId).Sum(x => x.WorkedMin) == 0,
            "Sửa dòng: dòng cũ huỷ GIỮ dòng nghỉ (thông 30), dòng mới bộ nghỉ mới (thông 0) — không UPDATE / DELETE", r.ErrorMsg);

        // (e) ca vừa đổi giờ nghỉ (trang để cũ) → chặn
        var sentOld = Sent(ca, D0.AddHours(14), D0.AddHours(15)); // không chạm gì theo ca cũ
        AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (9, 30, 9, 40, "Giải lao"), (12, 0, 13, 0, "Nghỉ trưa"), (14, 30, 14, 45, "Chiều"));
        g = WtsScen.Gc(machine: "M3"); g.Rows.Add(WtsScen.Row(1, 1, 0, D0.AddHours(14), D0.AddHours(15))); g.Rows[0].Breaks = sentOld;
        r = await s.SaveGroupAsync(g);
        Say(!r.Ok && r.ErrorMsg == WorkMinutes.MsgCaChanged, "Admin vừa thêm giờ nghỉ 14:30 → trang cũ gửi bộ không có → chặn", r.ErrorMsg);
        // (f) ca không còn → chặn
        g = WtsScen.Gc(machine: "M3"); g.CaId = 99; g.Rows.Add(WtsScen.Row(1, 1, 0, D0.AddHours(14), D0.AddHours(15)));
        r = await s.SaveGroupAsync(g);
        Say(!r.Ok && r.ErrorMsg!.StartsWith("Ca làm việc đã chọn không còn"), "Ca không còn trong DB → chặn", r.ErrorMsg);
        // (g) giờ lẻ giây → làm tròn phút ([ML17-MIN])
        var odd0 = D0.AddHours(15).AddSeconds(29); var odd1 = D0.AddHours(15).AddMinutes(20).AddSeconds(31);
        g = WtsScen.Gc(machine: "M2", nc: "NC1"); g.Rows.Add(WtsScen.Row(1, 1, 0, odd0, odd1));
        r = await s.SaveGroupAsync(g); db.Fixup();
        var w3 = db.WtsProductionLogs.List.OrderBy(x => x.WtsLogId).Last();
        Say(r.Ok && w3.StartTime == D0.AddHours(15) && w3.EndTime == D0.AddHours(15).AddMinutes(21), "Giờ lẻ giây làm tròn phút (15:00:29 → 15:00, 15:20:31 → 15:21)", $"{r.ErrorMsg} {w3.StartTime:HH:mm:ss}–{w3.EndTime:HH:mm:ss}");

        // (h) dòng WTS / sổ số lượng giống hệt có hay không làm thông nghỉ
        var dbA = await WtsScen.SeedAsync(); var dbB = await WtsScen.SeedAsync();
        foreach (var x in new[] { dbA, dbB }) AddCa(x, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (12, 0, 13, 0, "Trưa"));
        var caA = dbA.CaLamViecs.List[0];
        var ga = WtsScen.Gc(); ga.Rows.Add(WtsScen.Row(1, 3, 0, st, en)); ga.Rows[0].Breaks = Sent(caA, st, en, t => t.OverlapMin);
        var gb = WtsScen.Gc(); gb.Rows.Add(WtsScen.Row(1, 3, 0, st, en)); gb.Rows[0].Breaks = Sent(caA, st, en);
        var ra = await WtsScen.Svc(dbA).SaveGroupAsync(ga); var rb = await WtsScen.Svc(dbB).SaveGroupAsync(gb);
        var la = new HandoverLedgerService(dbA); var lb = new HandoverLedgerService(dbB);
        // bỏ mốc giờ (2 DB seed ở 2 thời điểm khác nhau) — so số
        static string NoTime(string j) => System.Text.RegularExpressions.Regex.Replace(j, "\"\\d{4}-\\d\\d-\\d\\dT[^\"]*\"", "\"t\"");
        var sa = NoTime(System.Text.Json.JsonSerializer.Serialize(await la.GetStepsForDetailAsync(1))); var sb = NoTime(System.Text.Json.JsonSerializer.Serialize(await lb.GetStepsForDetailAsync(1)));
        Say(ra.Ok && rb.Ok && dbA.WtsProductionLogs.List.Select(WtsKey).SequenceEqual(dbB.WtsProductionLogs.List.Select(WtsKey)) && sa == sb,
            "Có / không làm thông nghỉ: dòng WTS + sổ chung PO giống hệt (chỉ khác dòng nghỉ)",
            $"ok {ra.Ok}/{rb.Ok} {ra.ErrorMsg}{rb.ErrorMsg} wts {dbA.WtsProductionLogs.List.Select(WtsKey).SequenceEqual(dbB.WtsProductionLogs.List.Select(WtsKey))} sổ {sa == sb}\n{(sa == sb ? "" : sa[..Math.Min(600, sa.Length)] + "\n" + sb[..Math.Min(600, sb.Length)])}");
    }

    static async Task N3()
    {
        Console.WriteLine("[N3] Việc không ra sản phẩm: ca + giờ nghỉ lúc lưu");
        var db = await WtsScen.SeedAsync(); var s = WtsScen.Svc(db);
        var ca = AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (12, 0, 13, 0, "Nghỉ trưa"));
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", TaskCode = "HOP", TaskName = "Họp", IsActive = true, IsProductiveTask = false });
        var a0 = D0.AddHours(11).AddMinutes(30); var a1 = D0.AddHours(13).AddMinutes(30);
        var r = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "HOP", StartTime = a0, EndTime = a1, CaId = 1 });
        Say(!r.Ok && r.ErrorMsg == WorkMinutes.MsgCaChanged && db.WorkerActivityLogs.List.Count == 0, "Chạm trưa mà không gửi khoảng nghỉ → chặn", r.ErrorMsg);
        r = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "HOP", StartTime = a0, EndTime = a1, CaId = 1, Breaks = Sent(ca, a0, a1, t => 25) });
        db.Fixup();
        var act = db.WorkerActivityLogs.List.SingleOrDefault();
        var b = act == null ? new() : BrkOf(db, act: act.ActivityLogId);
        Say(r.Ok && b.Count == 1 && b[0].OverlapMin == 60 && b[0].WorkedMin == 25 && b[0].WtsLogId == null && b[0].CaId == 1,
            "Lưu: 1 dòng nghỉ (chồng 60, làm 25) gắn ActivityLogId", r.ErrorMsg);
        // sửa dòng: huỷ + mới, dòng nghỉ cũ giữ
        var a2 = D0.AddHours(12).AddMinutes(30);
        r = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { ActivityLogId = act!.ActivityLogId, UserId = 1, WtsCode = "HOP", StartTime = a0, EndTime = a2, CaId = 1, Breaks = Sent(ca, a0, a2) });
        db.Fixup();
        var actNew = db.WorkerActivityLogs.List.Single(x => !x.IsVoided);
        Say(r.Ok && BrkOf(db, act: act.ActivityLogId).Single().WorkedMin == 25 && BrkOf(db, act: actNew.ActivityLogId).Single().OverlapMin == 30,
            "Sửa việc: dòng cũ giữ dòng nghỉ, dòng mới chồng 30", r.ErrorMsg);
        // không có ca (CaId trống) → không dòng nghỉ, giữ phút thô
        var a3 = D0.AddHours(14); var a4 = D0.AddHours(15);
        r = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "HOP", StartTime = a3, EndTime = a4, CaId = null });
        Say(r.Ok, "Không chọn ca, không chạm nghỉ → lưu được", r.ErrorMsg);
    }

    static async Task N4()
    {
        Console.WriteLine("[N4] Báo cáo = trang, hiệu suất máy, đổi giờ nghỉ ca SAU khi lưu không đổi số ngày cũ");
        var db = await WtsScen.SeedAsync(); var s = WtsScen.Svc(db);
        var ca = AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (12, 0, 13, 0, "Nghỉ trưa"));
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", TaskCode = "HOP", TaskName = "Họp", IsActive = true, IsProductiveTask = false });
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M2", BoPhan = "GC", IsActive = true });
        // u2: GC M1 11:00–13:30 làm thông trưa 40; GC M2 12:30–14:00 (chồng giờ với dòng trên, đứng 2 máy) không làm thông
        var t0 = D0.AddHours(11); var t1 = D0.AddHours(13).AddMinutes(30); var t2 = D0.AddHours(12).AddMinutes(30); var t3 = D0.AddHours(14);
        var g = WtsScen.Gc(machine: "M1"); g.Rows.Add(WtsScen.Row(1, 2, 0, t0, t1)); g.Rows[0].Breaks = Sent(ca, t0, t1, t => 40);
        var r1 = await s.SaveGroupAsync(g);
        g = WtsScen.Gc(machine: "M2", nc: "NC1"); g.Rows.Add(WtsScen.Row(1, 2, 0, t2, t3)); g.Rows[0].Breaks = Sent(ca, t2, t3);
        var r2 = await s.SaveGroupAsync(g);
        // u1 (admin): việc HOP vô ích 11:30–12:30 làm thông 30 phút trưa
        var aS = D0.AddHours(11).AddMinutes(30); var aE = D0.AddHours(12).AddMinutes(30);
        var r3 = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "HOP", StartTime = aS, EndTime = aE, CaId = 1, Breaks = Sent(ca, aS, aE, t => 30) });
        db.Fixup();
        Say(r1.Ok && r2.Ok && r3.Ok, "Lưu 2 dòng GC + 1 việc", (r1.ErrorMsg ?? "") + (r2.ErrorMsg ?? "") + (r3.ErrorMsg ?? ""));

        var rep = new WtsReportService(db);
        var res = await rep.GetReportAsync(DateOnly.FromDateTime(D0), DateOnly.FromDateTime(D0), null, 1);
        // dòng: M1 thô 150, chồng 60, làm 40 → 130; M2 thô 90, chồng 30 → 60; việc thô 60 chồng 30 làm 30 → 60
        var m1 = res.Rows.FirstOrDefault(x => x.MachineUsed == "M1"); var m2 = res.Rows.FirstOrDefault(x => x.MachineUsed == "M2"); var ac = res.Rows.FirstOrDefault(x => x.RowType == WtsRowType.Activity);
        Say(m1?.Minutes == 130 && m1.RawMinutes == 150 && m1.BreakWorkedMin == 40 && m2?.Minutes == 60 && m2.RawMinutes == 90 && ac?.Minutes == 60 && ac.BreakWorkedMin == 30,
            "Báo cáo dòng = phút làm (WorkMinutes): 130 / 60 / 60", $"{m1?.Minutes}/{m2?.Minutes}/{ac?.Minutes}");
        // u2 gộp: 11:00–14:00 thô 180; phút làm: 11:00–12:00 (60) + 12:00–12:40 khúc thông M1 (40) + 13:00–14:00 (60) = 160 (12:40–13:00 cả 2 dòng nghỉ)
        // u1: 60. Tổng = 220. Có ích: u2 160 (GC có ích), u1 0 → 160; %: 160/220
        Say(res.TotalMinutes == 220 && res.ProductiveMin == 160 && res.WasteMin == 60 && res.RawMinutes == 240 && res.BreakWorkedMinutes == 70
            && Math.Abs(res.EfficiencyPct - Math.Round(160.0 / 220 * 100, 1)) < 0.001,
            "Báo cáo tổng gộp người-ngày theo phút làm: 220 (có ích 160, vô ích 60), thô 240, thông 70 — giờ chồng tính 1 lần",
            $"tổng {res.TotalMinutes} có ích {res.ProductiveMin} vô ích {res.WasteMin} thô {res.RawMinutes} thông {res.BreakWorkedMinutes}");
        Say(res.BreakWorkRows.Count == 2 && res.BreakWorkRows.Sum(x => x.Minutes) == 70 && res.BreakWorkRows.All(x => x.Break.StartsWith("12:00–13:00")),
            "Bảng làm thông nghỉ: 2 dòng (M1 40, việc 30)", string.Join("; ", res.BreakWorkRows));
        // trang: Tổng hôm nay u2 = WorkMinutes.Day các dòng của u2 với dòng nghỉ đã lưu
        var u2 = db.WtsProductionLogs.List.Where(x => x.WorkerId == 2 && !x.IsVoided).ToList();
        var mapW = await WorkMinutesStore.ForWtsAsync(db, u2.Select(x => x.WtsLogId));
        var day = WorkMinutes.Day(u2.Select(x => new DayLine(x.StartTime, x.EndTime, WorkMinutesStore.Get(mapW, x.WtsLogId))));
        Say(day.Work == 160 && day.Raw == 180 && day.Worked == 40 && day.Deducted == 20, "Tổng hôm nay (trang) u2 = 160 = báo cáo", day.ToString());

        // hiệu suất máy: M1 chạy 11:00–12:40 + 13:00–13:30 = 130; nghỉ 12:40–13:00 = 20; M2 chạy 13:00–14:00 = 60, nghỉ 12:30–13:00
        var eff = new MachineEfficiencyService(db);
        var me = await eff.GetAsync(DateOnly.FromDateTime(D0), new TimeOnly(7, 0), new TimeOnly(16, 0), ca);
        var rM1 = me.Rows.First(x => x.Machine.SoMay == "M1"); var rM2 = me.Rows.First(x => x.Machine.SoMay == "M2");
        Say(Math.Abs(rM1.RunMinutes - 130) < 0.01 && Math.Abs(rM1.BreakMinutes - 20) < 0.01 && Math.Abs(rM2.RunMinutes - 60) < 0.01 && Math.Abs(rM2.BreakMinutes - 60) < 0.01
            && Math.Abs(rM1.TotalMinutes - rM1.RunMinutes - rM1.BreakMinutes - rM1.IdleMinutes - rM1.DownMinutes) < 0.01,
            "Hiệu suất máy: khúc làm thông là Chạy (M1 130 chạy / 20 nghỉ; M2 60 / 60), tổng cân", $"M1 {rM1.RunMinutes}/{rM1.BreakMinutes} M2 {rM2.RunMinutes}/{rM2.BreakMinutes}");

        // CA-NGHI-HIST: admin đổi giờ nghỉ trưa 12:00–13:00 → 11:30–12:15 SAU khi lưu → báo cáo / trang ngày đó không đổi
        AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (11, 30, 12, 15, "Trưa mới"));
        var res2 = await rep.GetReportAsync(DateOnly.FromDateTime(D0), DateOnly.FromDateTime(D0), null, 1);
        Say(res2.TotalMinutes == res.TotalMinutes && res2.ProductiveMin == res.ProductiveMin && res2.Rows.Select(x => x.Minutes).SequenceEqual(res.Rows.Select(x => x.Minutes)),
            "Đổi giờ nghỉ ca sau khi lưu → báo cáo ngày đó KHÔNG đổi (giờ nghỉ chép lúc lưu)", $"{res2.TotalMinutes} vs {res.TotalMinutes}");
    }

    static async Task N5()
    {
        Console.WriteLine("[N5] BackfillNghi + ca có 2 nghỉ chồng");
        var db = await WtsScen.SeedAsync();
        var ca = AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (12, 0, 13, 0, "Trưa"));
        var caN = AddCa(db, 2, new TimeOnly(22, 0), new TimeOnly(6, 0), (2, 0, 2, 30, "Đêm"));
        var y = D0.AddDays(-1);
        long id = 1000;
        WtsProductionLog W(int worker, DateTime s, DateTime e, int? caId, bool voided = false)
        {
            var w = new WtsProductionLog { WtsLogId = ++id, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = worker, StartTime = s, EndTime = e, CaId = caId, IsVoided = voided, CreatedAt = y, QtyDone = 0 };
            db.WtsProductionLogs.List.Add(w); return w;
        }
        var w1 = W(2, y.AddHours(11), y.AddHours(14), 1);                 // chạm trưa 60
        var w2 = W(2, y.AddHours(8), y.AddHours(10), 1);                  // không chạm
        var w3 = W(3, y.AddHours(23), y.AddHours(30), 2);                 // ca đêm 23:00 → 06:00 hôm sau, chạm 02:00–02:30 (trang cũ: 0)
        var w4 = W(4, y.AddHours(11), y.AddHours(13), null);              // không có ca
        var w5 = W(2, y.AddHours(12), y.AddHours(12).AddMinutes(30), 1, voided: true);   // đã huỷ: vẫn chép để truy
        db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 501, WorkerId = 2, WorkDate = DateOnly.FromDateTime(y), StartTime = y.AddHours(12).AddMinutes(30), EndTime = y.AddHours(13).AddMinutes(30), CreatedAt = y });   // ca suy từ WTS u2 = 1
        db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 502, WorkerId = 4, WorkDate = DateOnly.FromDateTime(y), StartTime = y.AddHours(12), EndTime = y.AddHours(13), CreatedAt = y });   // u4 không có ca
        var plan = await BackfillNghi.BackfillCore.BuildAsync(db);
        var byW = plan.Items.Where(i => i.WtsLogId != null).ToDictionary(i => i.WtsLogId!.Value);
        Say(byW.ContainsKey(w1.WtsLogId) && byW[w1.WtsLogId].Rows.Single().OverlapMin == 60 && !byW.ContainsKey(w2.WtsLogId)
            && byW.ContainsKey(w3.WtsLogId) && byW[w3.WtsLogId].Rows.Single().BreakStart == D0.AddHours(2) && byW[w3.WtsLogId].OldPageMin == 420 && byW[w3.WtsLogId].NewMin == 390
            && !byW.ContainsKey(w4.WtsLogId) && byW.ContainsKey(w5.WtsLogId) && plan.WtsNoCa == 1,
            "Chép bù WTS: trưa 60, ca đêm 02:00 hôm sau (trang cũ 420 → 390), không ca giữ thô, dòng huỷ vẫn chép",
            string.Join("; ", plan.Items.Select(i => $"{i.WtsLogId}/{i.ActivityLogId}: {string.Join(",", i.Rows.Select(r => r.OverlapMin))} {i.OldPageMin}->{i.NewMin}")));
        Say(plan.Items.Any(i => i.ActivityLogId == 501 && i.CaId == 1 && i.Rows.Single().OverlapMin == 30) && !plan.Items.Any(i => i.ActivityLogId == 502) && plan.ActNoCa == 1,
            "Việc không ra SP: ca suy từ WTS cùng người cùng ngày; không có → giữ thô");
        await using (var tx = await db.Database.BeginTransactionAsync()) { await BackfillNghi.BackfillCore.WriteAsync(db, plan); await tx.CommitAsync(); }
        int n1 = db.WtsBreakWorkLogs.List.Count;
        Say(n1 == plan.BreakRows && db.WtsBreakWorkLogs.List.All(x => x.IsBackfill && x.WorkedMin == 0), "Ghi: đúng số dòng, IsBackfill = 1, thông 0", $"{n1}/{plan.BreakRows}");
        var plan2 = await BackfillNghi.BackfillCore.BuildAsync(db);
        Say(plan2.Items.Count == 0, "Chạy lại: không thêm gì", plan2.Items.Count.ToString());
        // code mới lưu 1 dòng (mốc) → dòng tạo SAU mốc không bị chép bù kể cả khi ca đổi giờ nghỉ
        var s = WtsScen.Svc(db);
        var st = D0.AddHours(11); var en = D0.AddHours(13);
        var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 1, 0, st, en)); g.Rows[0].Breaks = Sent(ca, st, en);
        var r = await s.SaveGroupAsync(g); db.Fixup();
        var g2 = WtsScen.Gc(machine: "M2", nc: "NC1"); g2.Rows.Add(WtsScen.Row(1, 1, 0, D0.AddHours(14), D0.AddHours(15)));
        var r2 = await s.SaveGroupAsync(g2); db.Fixup();
        AddCa(db, 1, new TimeOnly(7, 0), new TimeOnly(16, 0), (12, 0, 13, 0, "Trưa"), (14, 30, 14, 45, "Chiều"));
        var plan3 = await BackfillNghi.BackfillCore.BuildAsync(db);
        Say(r.Ok && r2.Ok && plan3.Items.Count == 0 && plan3.WtsAfterCutoff >= 1, "Dòng code mới lưu (sau mốc) không bị chép bù dù ca đổi giờ nghỉ", (r.ErrorMsg ?? "") + (r2.ErrorMsg ?? "") + $" items {plan3.Items.Count}");

        // CaLamViecService: 2 nghỉ chồng → chặn; tách rời → lưu
        var cs = new CaLamViecService(db);
        var bad = await cs.SaveAsync(new CaLamViec { CaId = 0, TenCa = "X", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) },
            new List<CaLamViec_NghiGiaiLao> { new() { NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0) }, new() { NghiBatDau = new TimeOnly(12, 30), NghiKetThuc = new TimeOnly(12, 45) } }, 1);   // [PHAT-HIEN-0311] người thao tác = user 1 (ADMIN)
        var good = await cs.SaveAsync(new CaLamViec { CaId = 0, TenCa = "Y", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) },
            new List<CaLamViec_NghiGiaiLao> { new() { NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0) }, new() { NghiBatDau = new TimeOnly(13, 0), NghiKetThuc = new TimeOnly(13, 15) } }, 1);
        Say(!bad.Ok && bad.Error.Contains("chồng nhau") && good.Ok, "Ca 2 nghỉ chồng → chặn; nghỉ liền nhau → lưu", bad.Error + " | " + good.Error);
        // dữ liệu ca cũ có 2 nghỉ chồng → gộp, không trừ 2 lần
        var ov = new CaLamViec { CaId = 77, GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) };
        ov.NghiGiaiLaos.Add(new() { NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0) });
        ov.NghiGiaiLaos.Add(new() { NghiBatDau = new TimeOnly(12, 30), NghiKetThuc = new TimeOnly(13, 30) });
        var tt = WorkMinutes.Touching(ov, D0.AddHours(11), D0.AddHours(15));
        Say(tt.Count == 1 && tt[0].OverlapMin == 90 && WorkMinutes.Touching(ov, D0.AddHours(11), D0.AddHours(12).AddMinutes(10)).Single().BreakEnd == D0.AddHours(13).AddMinutes(30),
            "Ca cũ 2 nghỉ chồng: gộp 12:00–13:30 (90), không trừ 2 lần, khoảng gộp như nhau dù dòng chỉ chạm 1 phần");
    }

    static async Task N6(int seed, int n)
    {
        Console.WriteLine($"[N6] Ngẫu nhiên qua service: {n} nhóm");
        var rnd = new Random(seed);
        var db = await WtsScen.SeedAsync(1000); var s = WtsScen.Svc(db);   // [B3-SO-NGUYEN 06/11] PO1 1000 chiếc: mỗi dòng ghi 1 (trước 0.01)
        var ca = AddCa(db, 1, new TimeOnly(6, 0), new TimeOnly(22, 0), (9, 30, 9, 45, "G1"), (12, 0, 13, 0, "Trưa"), (15, 0, 15, 10, "G2"), (18, 0, 18, 30, "Tối"));
        int saved = 0;
        var curM = new Dictionary<string, DateTime> { ["M1"] = D0.AddHours(6), ["M2"] = D0.AddHours(6).AddMinutes(rnd.Next(60)), ["M3"] = D0.AddHours(6).AddMinutes(rnd.Next(60)) };
        int errs = 0;
        for (int i = 0; i < n; i++)
        {
            var machine = "M" + (1 + rnd.Next(3)); var nc = "NC1";   // mỗi máy 1 dòng thời gian riêng (không trùng máy); người đứng nhiều máy → giờ chồng
            var st = curM[machine].AddMinutes(rnd.Next(0, 20)); var en = st.AddMinutes(10 + rnd.Next(150));
            if (en > D0.AddHours(23)) continue;
            curM[machine] = en;
            var g = WtsScen.Gc(machine: machine, nc: nc); g.UserId = 2 + rnd.Next(2);
            int rows = 1 + rnd.Next(3);
            var pts = new List<DateTime> { st };
            for (int k = 1; k < rows; k++) pts.Add(st.AddMinutes(rnd.Next(1, Math.Max(2, (int)(en - st).TotalMinutes))));
            pts.Add(en); pts = pts.Distinct().OrderBy(x => x).ToList();
            var segs = WorkMinutes.BreakSegments(ca, st, en);
            var worked = WorkMinutes.Touching(ca, st, en).ToDictionary(t => t.BreakStart, t => rnd.Next(3) == 0 ? 0 : rnd.Next(0, t.OverlapMin + 1));
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                var row = WtsScen.Row(1, 1m, 0, pts[k], pts[k + 1]);
                row.Breaks = WorkMinutes.SplitForLine(segs, st, worked, pts[k], pts[k + 1], 1)
                    .Select(t => new WorkMinutes.SentBreak { BreakStart = t.BreakStart, BreakEnd = t.BreakEnd, WorkedMin = t.WorkedMin }).ToList();
                g.Rows.Add(row);
            }
            var r = await s.SaveGroupAsync(g); db.Fixup();
            if (r.Ok) saved++;
            else { errs++; Ok(false, "N6 lưu nhóm hợp lệ", r.ErrorMsg); }
        }
        // mọi dòng WTS còn hiệu lực: dòng nghỉ = tính lại từ ca + giờ dòng; bất biến
        foreach (var w in db.WtsProductionLogs.List)
        {
            var b = BrkOf(db, w.WtsLogId);
            var calc = WorkMinutes.Touching(ca, w.StartTime, w.EndTime);
            Ok(b.Count == calc.Count && b.Zip(calc).All(z => z.First.BreakStart == z.Second.BreakStart && z.First.BreakEnd == z.Second.BreakEnd && z.First.OverlapMin == z.Second.OverlapMin
                                                          && z.First.WorkedMin >= 0 && z.First.WorkedMin <= z.First.OverlapMin),
               $"N6 dòng nghỉ WTS #{w.WtsLogId} khớp ca + giờ dòng");
            var lm = WorkMinutes.Line(w.StartTime, w.EndTime, b.Select(WorkMinutes.FromEntity));
            Ok(lm.Raw == lm.Normal + lm.Worked + lm.Deducted && lm.Raw == (int)(w.EndTime - w.StartTime).TotalMinutes, $"N6 bất biến dòng #{w.WtsLogId}");
        }
        foreach (var grp in db.WtsProductionLogs.List.Where(w => !w.IsVoided).GroupBy(w => w.WorkerId))
        {
            var map = await WorkMinutesStore.ForWtsAsync(db, grp.Select(x => x.WtsLogId));
            var d = WorkMinutes.Day(grp.Select(x => new DayLine(x.StartTime, x.EndTime, WorkMinutesStore.Get(map, x.WtsLogId))));
            // đáp án từng phút
            int raw = 0, work = 0;
            var lines = grp.Select(x => (x.StartTime, x.EndTime, B: WorkMinutesStore.Get(map, x.WtsLogId))).ToList();
            for (var t = D0; t < D0.AddDays(1); t = t.AddMinutes(1))
            {
                var cov = lines.Where(l => t >= l.StartTime && t < l.EndTime).ToList();
                if (cov.Count == 0) continue;
                raw++;
                if (cov.Any(l => { var b = l.B.FirstOrDefault(x => t >= x.BreakStart && t < x.BreakEnd); if (b == null) return true; var cs = b.BreakStart > l.StartTime ? b.BreakStart : l.StartTime; return t >= cs && t < cs.AddMinutes(b.WorkedMin); })) work++;
            }
            Ok(d.Raw == raw && d.Work == work && d.Raw == d.Work + d.Deducted, $"N6 người {grp.Key}: gộp ngày = đếm từng phút", $"{d} / thô {raw} làm {work}");
        }
        Console.WriteLine($"  N6: {saved} nhóm lưu được, {db.WtsBreakWorkLogs.List.Count} dòng nghỉ");
    }

    // [RPT-4][RPT-ACTIVE][RS-E7 2026-11-02] Báo cáo WTS (WtsReportService):
    //   RPT-4  máy chủ kiểm quyền Xem "WTS Report" (ma trận DB) ở cả 3 hàm — không quyền / tài khoản khoá → rỗng; chỉ Xem (không Sửa) → đọc được.
    //   RPT-ACTIVE cờ Có ích / Vô ích đọc cả mã đã NGƯNG dùng, so mã không phân biệt hoa thường; trùng mã → lấy bản đang dùng.
    //   RS-E7  dòng HTSP / KCS / Đóng gói hiện mã CV (WtsCode); dòng GC: NC, mã CV trống.
    static async Task N7()
    {
        Console.WriteLine("[N7] Báo cáo WTS: quyền máy chủ (RPT-4), mã CV ngưng dùng giữ cờ có ích (RPT-ACTIVE), hiện mã CV HTSP / KCS (RS-E7)");
        var db = await WtsScen.SeedAsync(); var s = WtsScen.Svc(db);
        string PermsV(bool view, bool edit) => System.Text.Json.JsonSerializer.Serialize(new[] { new MES.Web.Constants.GroupPermissionSetting { SectionCode = MES.Web.Constants.SystemPermissions.WtsReport, CanView = view, CanEdit = edit } });
        var gV = new UserGroup { GroupId = 61, GroupCode = "RPTV", IsActive = true, Permissions = PermsV(true, false) };
        var gN = new UserGroup { GroupId = 62, GroupCode = "NORPT", IsActive = true, Permissions = FuzzPerm.Json(MES.Web.Constants.SystemPermissions.WtsGc) };
        db.UserGroups.List.AddRange(new[] { gV, gN });
        db.Users.List.Add(new User { UserId = 61, Username = "rv", FullName = "Xem BC", IsActive = true, Group = gV, GroupId = 61 });
        db.Users.List.Add(new User { UserId = 62, Username = "rn", FullName = "Không BC", IsActive = true, Group = gN, GroupId = 62 });
        db.Users.List.Add(new User { UserId = 63, Username = "rk", FullName = "Xem BC khoá", IsActive = false, Group = gV, GroupId = 61 });
        // mã CV: HOP vô ích đang dùng · CU vô ích ĐÃ NGƯNG · DUP: bản đang dùng có ích + bản cũ đã ngưng vô ích
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", TaskCode = "HOP", TaskName = "Họp", IsActive = true, IsProductiveTask = false });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 2, CategoryCode = "CHUNG", TaskCode = "CU", TaskName = "Việc cũ", IsActive = false, IsProductiveTask = false });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 3, CategoryCode = "CHUNG", TaskCode = "DUP", TaskName = "Trùng mới", IsActive = true, IsProductiveTask = true });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 4, CategoryCode = "CHUNG", TaskCode = "dup", TaskName = "Trùng cũ", IsActive = false, IsProductiveTask = false });
        db.Fixup();
        // WTS thật: GC NC1 (u2) + HTSP Taro mã chuẩn bị 1T (u3); KCS K1 + việc không ra SP ghi thẳng (dòng cũ — mã CU đã ngưng thì service không cho lưu mới)
        var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 5, 0, D0.AddHours(8), D0.AddHours(9))); var r1 = await s.SaveGroupAsync(g);
        var h = WtsScen.NonGc("HTSP", "Taro"); var p = WtsScen.Row(1, 0, 0, D0.AddHours(9), D0.AddHours(9).AddMinutes(15), code: "1T", setup: null, mach: null, insp: null, actualPc: 15); p.StdTimePerPc = 15; h.Rows.Add(p);
        var r2 = await s.SaveGroupAsync(h);
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 900, KhPlanDetailId = 1, ProcessGroup = "KCS", WtsCode = "K1", WorkerId = 4, QtyDone = 2, QtyOk = 2, QtyNg = 0, StartTime = D0.AddHours(10), EndTime = D0.AddHours(10.5), CreatedAt = D0, Worker = db.Users.List.First(u => u.UserId == 4) });
        long aid = 800;
        void Act(int w, string code, double h0, double h1) => db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = ++aid, WorkerId = w, WorkDate = DateOnly.FromDateTime(D0), StartTime = D0.AddHours(h0), EndTime = D0.AddHours(h1), WtsCode = code, WtsName = code, CreatedAt = D0, Worker = db.Users.List.First(u => u.UserId == w) });
        Act(5, "HOP", 8, 9); Act(5, "cu", 9, 10); Act(5, "DUP", 10, 11); Act(5, "dup", 11, 12);
        db.Fixup();
        Say(r1.Ok && r2.Ok, "Dựng: 1 dòng GC + 1 dòng HTSP qua service, 1 dòng KCS + 4 việc không ra SP", (r1.ErrorMsg ?? "") + (r2.ErrorMsg ?? ""));
        var rep = new WtsReportService(db); var day = DateOnly.FromDateTime(D0);

        // RPT-4
        var rA = await rep.GetReportAsync(day, day, null, 1);
        var rV = await rep.GetReportAsync(day, day, null, 61);
        var rN = await rep.GetReportAsync(day, day, null, 62);
        var rK = await rep.GetReportAsync(day, day, null, 63);
        Say(rA.Rows.Count == 7 && rV.Rows.Count == 7, "Admin + người chỉ có quyền XEM WTS Report: đọc đủ 7 dòng", $"admin {rA.Rows.Count} · xem {rV.Rows.Count}");
        Say(rN.Rows.Count == 0 && rN.TotalMinutes == 0 && rK.Rows.Count == 0, "Không có quyền WTS Report / tài khoản đã khoá: kết quả rỗng (máy chủ chặn)", $"không quyền {rN.Rows.Count} · khoá {rK.Rows.Count}");
        var wV = await rep.GetWorkersWithLogsAsync(day, day, 61); var wN = await rep.GetWorkersWithLogsAsync(day, day, 62);
        var aV = await rep.GetAllActiveWorkersAsync(61); var aN = await rep.GetAllActiveWorkersAsync(62);
        Say(wV.Count == 4 && wN.Count == 0 && aV.Count > 0 && aN.Count == 0, "Danh sách công nhân (có log / tất cả): có quyền → có, không quyền → rỗng", $"{wV.Count}/{wN.Count} · {aV.Count}/{aN.Count}");

        // RPT-ACTIVE
        bool? Pro(string code) => rA.Rows.FirstOrDefault(x => x.RowType == WtsRowType.Activity && x.WtsCode == code)?.IsProductiveTask;
        Say(Pro("HOP") == false, "Mã HOP vô ích đang dùng: vô ích", Pro("HOP")?.ToString());
        Say(Pro("cu") == false, "Mã CU vô ích ĐÃ NGƯNG, dòng cũ ghi 'cu' (chữ thường): vẫn vô ích (trước: thành có ích)", Pro("cu")?.ToString());
        Say(Pro("DUP") == true && Pro("dup") == true, "Mã trùng (đang dùng có ích + bản cũ đã ngưng vô ích): lấy bản đang dùng → có ích", $"{Pro("DUP")}/{Pro("dup")}");
        // phút: u5 4 giờ việc: có ích DUP + dup = 120, vô ích HOP + cu = 120
        var u5 = rA.Rows.Where(x => x.WorkerId == 5).ToList();
        Say(u5.Count == 4 && u5.Where(x => x.IsProductiveTask).Sum(x => x.Minutes) == 120 && u5.Where(x => !x.IsProductiveTask).Sum(x => x.Minutes) == 120, "u5: 120 phút có ích, 120 phút vô ích");

        // RS-E7
        var gc = rA.Rows.FirstOrDefault(x => x.RowType == WtsRowType.Production && x.NC == "NC1");
        var ht = rA.Rows.FirstOrDefault(x => x.RowType == WtsRowType.Production && x.WorkerId == 3);
        var kc = rA.Rows.FirstOrDefault(x => x.RowType == WtsRowType.Production && x.WorkerId == 4);
        Say(gc != null && gc.WtsCode == "" && ht != null && ht.NC == "" && ht.WtsCode == "1T" && kc != null && kc.WtsCode == "K1",
            "Dòng GC: NC1, mã CV trống · dòng HTSP: mã CV 1T · dòng KCS: mã CV K1 (trước: luôn trống)", $"GC '{gc?.NC}'/'{gc?.WtsCode}' · HTSP '{ht?.NC}'/'{ht?.WtsCode}' · KCS '{kc?.WtsCode}'");
    }

    // [RPT-3 2026-11-02] Hiệu suất máy ca vắt đêm 20:00–05:00: giờ kết thúc sang ngày hôm sau → tổng 540 phút, đoạn WTS 21:00–03:00 qua nửa đêm là Chạy 360.
    //   Trước: dayEnd cùng ngày → tổng âm, mọi đoạn bị cắt, hiệu suất 0 %.
    static async Task N8()
    {
        Console.WriteLine("[N8] RPT-3 — hiệu suất máy ca vắt đêm (20:00–05:00, WTS qua nửa đêm)");
        var db = await WtsScen.SeedAsync();
        var ca = AddCa(db, 2, new TimeOnly(20, 0), new TimeOnly(5, 0));   // không giờ nghỉ: ca này chỉ kiểm khung giờ vắt đêm (giờ nghỉ đã kiểm ở N4)
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", BoPhan = "GC", IsActive = true });
        db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M2", BoPhan = "GC", IsActive = true });
        // M1: 21:00 → 03:00 hôm sau (qua nửa đêm); M2: 04:00 → 06:00 hôm sau (vượt cuối ca 1 giờ); dòng ngày khác (hôm sau 21:00) không tính
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 901, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", MachineUsed = "M1", WorkerId = 2, QtyDone = 5, QtyOk = 5, StartTime = D0.AddHours(21), EndTime = D0.AddHours(27), CreatedAt = D0, CaId = 2 });
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 902, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC2", MachineUsed = "M2", WorkerId = 3, QtyDone = 2, QtyOk = 2, StartTime = D0.AddHours(28), EndTime = D0.AddHours(30), CreatedAt = D0, CaId = 2 });
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 903, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", MachineUsed = "M1", WorkerId = 2, QtyDone = 1, QtyOk = 1, StartTime = D0.AddHours(45), EndTime = D0.AddHours(46), CreatedAt = D0, CaId = 2 });
        db.Fixup();
        var me = await new MachineEfficiencyService(db).GetAsync(DateOnly.FromDateTime(D0), new TimeOnly(20, 0), new TimeOnly(5, 0), ca);
        var m1 = me.Rows.First(x => x.Machine.SoMay == "M1"); var m2 = me.Rows.First(x => x.Machine.SoMay == "M2");
        string Inf(dynamic r) => $"tổng {r.TotalMinutes} chạy {r.RunMinutes} nghỉ {r.BreakMinutes} chờ {r.IdleMinutes} dừng {r.DownMinutes}";
        Say(Math.Abs(m1.TotalMinutes - 540) < 0.01 && Math.Abs(m2.TotalMinutes - 540) < 0.01, "Tổng phút ca vắt đêm = 540 (20:00 → 05:00 hôm sau), không âm", Inf(m1));
        Say(Math.Abs(m1.RunMinutes - 360) < 0.01 && Math.Abs(m1.IdleMinutes - 180) < 0.01,
            "M1 WTS 21:00 → 03:00 (qua nửa đêm): chạy 360, chờ 180", Inf(m1));
        Say(Math.Abs(m2.RunMinutes - 60) < 0.01 && Math.Abs(m2.IdleMinutes - 480) < 0.01,
            "M2 WTS 04:00 → 06:00 hôm sau: chỉ tính 04:00–05:00 = 60 chạy, chờ 480; dòng 21:00 hôm sau không tính", Inf(m2));
        Say(Math.Abs(m1.TotalMinutes - m1.RunMinutes - m1.BreakMinutes - m1.IdleMinutes - m1.DownMinutes) < 0.01 && m1.RunPct > 0, "Tổng cân, % chạy > 0 (trước: 0 %)", m1.RunPct.ToString());
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[N1] Hàm phút WorkMinutes đối chiếu đếm từng phút (ngẫu nhiên 3000 lượt, cả ca qua đêm)");
        N1(17, 3000);
        Console.WriteLine($"  {(fails == 0 ? "ĐẠT" : "SAI")} N1 ({checks} phép kiểm)");
        await N2(); await N3(); await N4(); await N5(); await N6(29, 60);
        await N7(); await N8();   // [RPT-4][RPT-ACTIVE][RS-E7][RPT-3] báo cáo WTS + hiệu suất máy ca vắt đêm
        Console.WriteLine($"Tổng phép kiểm: {checks}");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }
}
