// [G32-SCEN 2026-11-22] Kịch bản `saoluu` — Gói 32a sao lưu trong phần mềm (không SQL thật: ISaoLuuSql giả ghi file .bak vào thư mục tạm).
//   S1  Luật dọn bộ ngày: ca tay theo đúng luật HÙNG chốt (ngày / tuần / tháng, thiếu ngày, bản lỗi, chỉnh đồng hồ)
//   S2  Luật dọn bộ ngày: ngẫu nhiên × đáp án viết lại độc lập (duyệt từng bản, so với mọi bản cùng ô) + tính chất an toàn
//   S3  Luật dọn bản nhanh (tay + ngẫu nhiên)
//   S4  Chạy lưu nhanh: folder đúng tên, manifest ĐẠT, zip chứa .bak đúng mã băm, không còn .bak rời / .dang-tao
//   S5  Chạy trọn bộ: cau-hinh / bản quyền / file thêm / uploads / phần mềm (bỏ logs + uploads) / DOC-TRUOC / mã băm từng file khớp
//   S6  Lỗi giữa chừng: bản LỖI có manifest, không còn dữ liệu dở; 2 ô thư mục lệch → câu báo rõ; đang chạy / khoá DB → không chạy chồng
//   S7  Dọn chỉ xoá folder đúng mẫu + có manifest; .dang-tao bỏ dở > 6 giờ mới xoá; manifest hỏng = LỖI
//   S8  Lịch (DenHan) chạy giả 40 ngày đồng hồ giả: mỗi ngày đúng 1 bộ sau giờ chốt, khoảng cách 2 bản đạt ≤ chu kỳ + 1 nhịp, số bản giữ đúng luật
//   S9  Cài đặt: kiểm lỗi nhập, chỉ Admin lưu được, ghi nhật ký, file hỏng không âm thầm về mặc định, xem trước bản sẽ xoá khi giảm số giữ
//   S10 Băng cảnh báo: chưa cài / tắt / lỗi / quá hạn / bình thường
//   S11 [G32-RA] các lỗi rà độc lập 22/11: giờ tương lai, manifest không rõ, .dang-tao trên đường chạy thật, đồng hồ nhảy tới (tạm dừng dọn),
//       xem trước không lách được, cài đặt hỏng lưu đè được, file thêm trùm thư mục sao lưu, tên DB trùng sau đổi ký tự, thư mục không mở được
//   S12–S16 [G32B-SCEN 2026-11-23] Gói 32b (file SaoLuuScen32b.cs): bộ trước nâng cấp, xoá tay, bộ nhớ đệm manifest, ổ đầy, script PowerShell
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MES.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

public static partial class SaoLuuScen   // [G32B-SCEN] partial: S12–S16 ở SaoLuuScen32b.cs
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        if (!cond || Verbose) Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!cond) fails++;
    }
    static bool Verbose = true;

    // ------------------------------------------------------------------ SQL giả
    class FakeSql : ISaoLuuSql
    {
        public string? DoiGocSql;   // != null: BACKUP ghi vào gốc khác (giả 2 ô thư mục lệch)
        public string GocSql = "", GocApp = "";
        public int FailNext;
        public bool KhoaBan;
        public int SoLan;
        public int ChamMs;
        public int FailODay;   // [G32B-SCEN] lần BACKUP sau ném IOException ổ đầy (HRESULT 0x80070070)
        private readonly SemaphoreSlim _k = new(1, 1);
        public Task<string> TenDbAsync(CancellationToken ct) => Task.FromResult("MES");
        public Task<string> PhienBanSqlAsync(CancellationToken ct) => Task.FromResult("16.0 · Thử");
        public Task<SortedDictionary<string, long>> DemDongAsync(CancellationToken ct) =>
            Task.FromResult(new SortedDictionary<string, long>(StringComparer.Ordinal) { ["dbo.A"] = 5, ["dbo.B"] = 7 });
        public async Task BackupAsync(string db, string path, string nhan, CancellationToken ct)
        {
            if (ChamMs > 0) await Task.Delay(ChamMs, ct);
            if (FailNext > 0) { FailNext--; throw new InvalidOperationException("Giả lỗi BACKUP"); }
            if (FailODay > 0) { FailODay--; throw new IOException("There is not enough space on the disk. (giả)", unchecked((int)0x80070070)); }
            SoLan++;
            var p = DoiGocSql != null ? path.Replace(GocSql, DoiGocSql) : path;
            // như SQL thật: thư mục không có → lỗi (không tự tạo) — [G32-RA] mục 9
            if (!Directory.Exists(Path.GetDirectoryName(p)!)) throw new InvalidOperationException("Cannot open backup device. Operating system error 3 (giả)");
            var sb = new StringBuilder();
            for (int i = 0; i < 3000; i++) sb.Append(nhan).Append(i).Append('\n');
            await File.WriteAllTextAsync(p, sb.ToString(), ct);
        }
        public async Task<IAsyncDisposable?> ThuKhoaAsync(CancellationToken ct)
        {
            if (KhoaBan) return null;
            if (!await _k.WaitAsync(0, ct)) return null;
            return new R(_k);
        }
        sealed class R : IAsyncDisposable { readonly SemaphoreSlim s; public R(SemaphoreSlim s) { this.s = s; } public ValueTask DisposeAsync() { s.Release(); return ValueTask.CompletedTask; } }
    }

    static string TmpRoot = "";
    static string NewDir(string name)
    {
        var d = Path.Combine(TmpRoot, name + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    record Env(SaoLuuService Svc, FakeSql Sql, string Goc, string ContentRoot, string CfgDir, string Lic);

    static Env MkEnv(bool publish = true, DateTime? now = null)
    {
        var goc = NewDir("bk");
        var cr = NewDir("app");
        var cfg = NewDir("cfg");
        var lic = NewDir("lic");
        File.WriteAllText(Path.Combine(cr, "appsettings.json"), "{\"ConnectionStrings\":{}}");
        File.WriteAllText(Path.Combine(cr, "appsettings.Production.json"), "{\"Logging\":{}}");
        if (publish) { File.WriteAllText(Path.Combine(cr, "MES.Web.dll"), "dll"); File.WriteAllText(Path.Combine(cr, "Other.dll"), new string('x', 5000)); }
        Directory.CreateDirectory(Path.Combine(cr, "logs")); File.WriteAllText(Path.Combine(cr, "logs", "mes-20261122.log"), "log");
        Directory.CreateDirectory(Path.Combine(cr, "wwwroot", "css")); File.WriteAllText(Path.Combine(cr, "wwwroot", "css", "app.css"), "css");
        Directory.CreateDirectory(Path.Combine(cr, "wwwroot", "uploads", "avatars")); File.WriteAllText(Path.Combine(cr, "wwwroot", "uploads", "avatars", "a.png"), "png");
        File.WriteAllText(Path.Combine(lic, "mes_cfg.dat"), "lic");
        var sql = new FakeSql { GocSql = goc, GocApp = goc };
        var svc = new SaoLuuService(null!, sql, new SaoLuuCaiDatStore(cfg), cr, NullLogger<SaoLuuService>.Instance)
        {
            ThuMucBanQuyen = lic,
            BaseDir = NewDir("bin"),
            KiemAdminThu = id => Task.FromResult(id == 1),
        };
        File.WriteAllText(Path.Combine(svc.BaseDir, "MES.Web.dll"), "devdll");
        var t = now ?? new DateTime(2026, 11, 22, 10, 0, 0);
        svc.Clock = () => t;
        var c = new SaoLuuCaiDat { ThuMucTheoMaySql = goc };
        svc.Store.Ghi("MES", c, "thu", "khởi tạo", t);
        return new Env(svc, sql, goc, cr, cfg, lic);
    }

    static string Sha(string f) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
    static string ShaStream(Stream s) => Convert.ToHexString(SHA256.HashData(s));

    // ------------------------------------------------------------------ đáp án độc lập (luật dọn bộ ngày)
    static DateTime CuoiTuanOracle(DateTime d, DayOfWeek thu)
    {
        var x = d.Date;
        while (x.DayOfWeek != thu) x = x.AddDays(1);
        return x;
    }

    static bool GiuOracle(SaoLuuLuatDon.Ban b, List<SaoLuuLuatDon.Ban> all, SaoLuuCaiDat c, DateTime now)
    {
        if (b.ThoiDiem > now || b.KhongRo) return true;
        var past = all.Where(x => x.ThoiDiem <= now && !x.KhongRo).ToList();
        var ok = past.Where(x => x.Dat).ToList();
        if (ok.Count == 0) return true;
        bool Later(SaoLuuLuatDon.Ban o, SaoLuuLuatDon.Ban p) => o.ThoiDiem > p.ThoiDiem || (o.ThoiDiem == p.ThoiDiem && string.CompareOrdinal(o.Ten, p.Ten) > 0);
        var newestAll = past.Aggregate((a, x) => Later(x, a) ? x : a);
        if (!newestAll.Dat) return true;
        var newestOk = ok.Aggregate((a, x) => Later(x, a) ? x : a);
        if (b.Ten == newestOk.Ten) return true;
        if (!b.Dat) return b.ThoiDiem.Date > now.Date.AddDays(-c.GiuBoNgay);
        bool LatestIn(Func<SaoLuuLuatDon.Ban, bool> same) => !ok.Any(o => o.Ten != b.Ten && same(o) && Later(o, b));
        int daysAgo = (now.Date - b.ThoiDiem.Date).Days;
        if (daysAgo >= 0 && daysAgo < c.GiuBoNgay && LatestIn(o => o.ThoiDiem.Date == b.ThoiDiem.Date)) return true;
        var we = CuoiTuanOracle(b.ThoiDiem, c.ThuChotTuan);
        int weeksAgo = (CuoiTuanOracle(now, c.ThuChotTuan) - we).Days / 7;
        if (weeksAgo >= 0 && weeksAgo < c.GiuBoTuan && LatestIn(o => CuoiTuanOracle(o.ThoiDiem, c.ThuChotTuan) == we)) return true;
        int monthsAgo = (now.Year - b.ThoiDiem.Year) * 12 + now.Month - b.ThoiDiem.Month;
        if (monthsAgo >= 0 && (c.GiuBoThang == 0 || monthsAgo < c.GiuBoThang) && LatestIn(o => o.ThoiDiem.Year == b.ThoiDiem.Year && o.ThoiDiem.Month == b.ThoiDiem.Month)) return true;
        return false;
    }

    static bool GiuNhanhOracle(SaoLuuLuatDon.Ban b, List<SaoLuuLuatDon.Ban> all, SaoLuuCaiDat c, DateTime now)
    {
        if (b.KhongRo) return true;
        var past = all.Where(x => x.ThoiDiem <= now && !x.KhongRo).ToList();
        var ok = past.Where(x => x.Dat).ToList();
        if (ok.Count == 0) return true;
        bool Later(SaoLuuLuatDon.Ban o, SaoLuuLuatDon.Ban p) => o.ThoiDiem > p.ThoiDiem || (o.ThoiDiem == p.ThoiDiem && string.CompareOrdinal(o.Ten, p.Ten) > 0);
        var newestAll = past.Aggregate((a, x) => Later(x, a) ? x : a);
        if (!newestAll.Dat) return true;
        if (b.Ten == ok.Aggregate((a, x) => Later(x, a) ? x : a).Ten) return true;
        if (b.ThoiDiem > now) return true;
        if (b.Dat && ok.Count(o => Later(o, b)) < SaoLuuLuatDon.LuonGiuNhanh) return true;   // luôn giữ 6 bản đạt mới nhất
        return b.ThoiDiem > now.AddHours(-c.GiuBanNhanhGio);
    }

    static SaoLuuLuatDon.Ban B(DateTime t, bool dat = true) => new(t.ToString("yyyy-MM-dd_HHmmss"), t, dat);

    static HashSet<string> Giu(List<SaoLuuLuatDon.KetQua> kq) => kq.Where(x => x.Giu).Select(x => x.Ban.Ten).ToHashSet();

    public static async Task<int> Run(string[]? args = null)
    {
        Console.OutputEncoding = Encoding.UTF8;
        TmpRoot = Path.Combine(Path.GetTempPath(), "saoluu_scen_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TmpRoot);
        int nRand = args != null && args.Length > 1 && int.TryParse(args[1], out var nr) ? nr : 4000;
        try
        {
            S1(); S2(nRand); S3(nRand);
            await S4(); await S5(); await S6(); await S7(); await S8(); await S9(); await S10(); await S11();
            await S12(); await S13(); await S14(); S15(); await S16();   // [G32B-SCEN]
        }
        finally { try { Directory.Delete(TmpRoot, true); } catch { } }
        Console.WriteLine($"\n[saoluu] {checks} phép kiểm, {fails} SAI");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");   // dòng lệnh nang đọc để chấm ĐẠT (trạm kiem 22/11 18:36 bắt thiếu)
        return fails == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ S1
    static void S1()
    {
        Console.WriteLine("[S1] Luật dọn bộ ngày — ca tay");
        var c = new SaoLuuCaiDat();   // 7 ngày / 4 tuần (CN) / tháng giữ mãi
        var now = new DateTime(2026, 11, 22, 23, 58, 0);   // Chủ nhật
        var ds = new List<SaoLuuLuatDon.Ban>();
        for (var d = new DateTime(2025, 9, 1); d <= now.Date; d = d.AddDays(1)) ds.Add(B(d.AddHours(23).AddMinutes(55)));
        var g = Giu(SaoLuuLuatDon.TinhBoNgay(ds, c, now));
        var exp = new HashSet<string>();
        for (int i = 0; i < 7; i++) exp.Add(B(now.Date.AddDays(-i).AddHours(23).AddMinutes(55)).Ten);
        foreach (var d in new[] { 22, 15, 8, 1 }) exp.Add(B(new DateTime(2026, 11, d, 23, 55, 0)).Ten);
        for (var m = new DateTime(2025, 9, 1); m < new DateTime(2026, 11, 1); m = m.AddMonths(1)) exp.Add(B(m.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(55)).Ten);
        Ok(g.SetEquals(exp), "S1a mỗi ngày 1 bộ, 448 ngày: giữ 7 ngày + CN 4 tuần + cuối mỗi tháng (giữ mãi)", $"giữ {g.Count}, đáp án {exp.Count}; thừa {string.Join(",", g.Except(exp).Take(5))}; thiếu {string.Join(",", exp.Except(g).Take(5))}");
        Ok(g.Count == 7 + 3 + 14, "S1b tổng 24 bộ (7 + 3 tuần ngoài 7 ngày + 14 tháng cũ)", g.Count.ToString());

        // thiếu Chủ nhật 15/11 → giữ thứ Bảy 14/11; thiếu 31/10 → giữ 30/10
        var ds2 = ds.Where(b => b.ThoiDiem.Date != new DateTime(2026, 11, 15) && b.ThoiDiem.Date != new DateTime(2026, 10, 31)).ToList();
        var g2 = Giu(SaoLuuLuatDon.TinhBoNgay(ds2, c, now));
        Ok(g2.Contains(B(new DateTime(2026, 11, 14, 23, 55, 0)).Ten) && g2.Contains(B(new DateTime(2026, 10, 30, 23, 55, 0)).Ten), "S1c thiếu bộ CN / cuối tháng → giữ bộ mới nhất còn có của tuần / tháng");

        // 2 bộ cùng ngày (bấm tay buổi sáng + tự động tối) → ngày đó giữ bộ tối; ngày cũ ngoài 7 ngày không giữ bản sáng
        var ds3 = ds.ToList(); ds3.Add(B(new DateTime(2026, 11, 20, 9, 0, 0))); ds3.Add(B(new DateTime(2026, 11, 3, 9, 0, 0)));
        var g3 = Giu(SaoLuuLuatDon.TinhBoNgay(ds3, c, now));
        Ok(!g3.Contains(B(new DateTime(2026, 11, 20, 9, 0, 0)).Ten) && !g3.Contains(B(new DateTime(2026, 11, 3, 9, 0, 0)).Ten), "S1d 2 bộ / ngày: chỉ giữ bộ mới nhất của ngày");

        // bộ mới nhất LỖI → không xoá gì
        var ds4 = ds.ToList(); ds4.Add(B(now.AddMinutes(1).AddMinutes(-2), false));
        var r4 = SaoLuuLuatDon.TinhBoNgay(ds4, c, now);
        Ok(r4.All(x => x.Giu), "S1e bộ mới nhất bị lỗi → tạm không xoá gì", r4.Count(x => !x.Giu).ToString());
        // lỗi cũ → bản lỗi trong 7 ngày giữ để xem, lỗi cũ hơn xoá; bản lỗi không chiếm chỗ ngày
        var ds5 = ds.ToList(); ds5.Add(B(new DateTime(2026, 11, 21, 23, 59, 0), false)); ds5.Add(B(new DateTime(2026, 11, 1, 23, 59, 0), false));
        var g5 = Giu(SaoLuuLuatDon.TinhBoNgay(ds5, c, now));
        Ok(g5.Contains(B(new DateTime(2026, 11, 21, 23, 59, 0)).Ten) && !g5.Contains(B(new DateTime(2026, 11, 1, 23, 59, 0)).Ten) && g5.Contains(B(new DateTime(2026, 11, 21, 23, 55, 0)).Ten),
            "S1f bộ lỗi: trong 7 ngày giữ để xem, cũ hơn xoá; không thay bộ đạt của ngày");
        // chưa có bộ đạt
        var r6 = SaoLuuLuatDon.TinhBoNgay(new List<SaoLuuLuatDon.Ban> { B(now.AddDays(-30), false), B(now.AddDays(-1), false) }, c, now);
        Ok(r6.All(x => x.Giu), "S1g chưa có bộ đạt → không xoá");
        // giữ tháng 3 tháng
        var c7 = new SaoLuuCaiDat { GiuBoThang = 3 };
        var g7 = Giu(SaoLuuLuatDon.TinhBoNgay(ds, c7, now));
        Ok(g7.Count == 7 + 3 + 2, "S1h giữ 3 tháng (tháng này + 2 tháng trước)", g7.Count.ToString());
        // đồng hồ chỉnh lùi: bản "tương lai" giữ, không chiếm chỗ bộ hôm nay
        var ds8 = ds.ToList(); var fut = B(now.AddHours(1)); ds8.Add(fut);
        var g8 = Giu(SaoLuuLuatDon.TinhBoNgay(ds8, c, now));
        Ok(g8.Contains(fut.Ten) && g8.Contains(B(now.Date.AddHours(23).AddMinutes(55)).Ten), "S1i bản có giờ sau giờ máy: giữ, bộ thật hôm nay vẫn giữ");
        // tuần chốt thứ Bảy
        var c9 = new SaoLuuCaiDat { ThuChotTuan = DayOfWeek.Saturday, GiuBoNgay = 1, GiuBoTuan = 2, GiuBoThang = 1 };
        var g9 = Giu(SaoLuuLuatDon.TinhBoNgay(ds, c9, now));
        var e9 = new[] { B(new DateTime(2026, 11, 22, 23, 55, 0)).Ten, B(new DateTime(2026, 11, 21, 23, 55, 0)).Ten };
        Ok(g9.SetEquals(e9), "S1j chốt thứ Bảy, 1 ngày / 2 tuần / 1 tháng: giữ CN 22 (ngày + tuần này + tháng) + T7 21 (tuần trước)", string.Join(",", g9));
    }

    // ------------------------------------------------------------------ S2
    static void S2(int n)
    {
        Console.WriteLine($"[S2] Luật dọn bộ ngày — ngẫu nhiên {n} lượt × đáp án độc lập");
        var rnd = new Random(32);
        int sai = 0; string? vd = null; int saiAnToan = 0; string? vdAt = null;
        var save = Verbose; Verbose = false;
        for (int it = 0; it < n; it++)
        {
            var c = new SaoLuuCaiDat
            {
                GiuBoNgay = rnd.Next(1, 15), GiuBoTuan = rnd.Next(0, 9), GiuBoThang = rnd.Next(0, 3) == 0 ? 0 : rnd.Next(1, 30),
                ThuChotTuan = (DayOfWeek)rnd.Next(7),
            };
            var now = new DateTime(2024, 1, 1).AddMinutes(rnd.Next(0, 3 * 365 * 1440));
            int k = rnd.Next(0, 120);
            var set = new Dictionary<string, SaoLuuLuatDon.Ban>();
            for (int i = 0; i < k; i++)
            {
                var t = now.AddMinutes(-rnd.Next(-600, 900 * 1440));
                t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, rnd.Next(60));
                var b = B(t, rnd.Next(10) != 0) with { KhongRo = rnd.Next(30) == 0 };
                set[b.Ten] = b;
            }
            var ds = set.Values.ToList();
            var kq = SaoLuuLuatDon.TinhBoNgay(ds, c, now);
            foreach (var r in kq)
            {
                var o = GiuOracle(r.Ban, ds, c, now);
                if (o != r.Giu) { sai++; vd ??= $"it {it}: {r.Ban.Ten} dat={r.Ban.Dat} code={r.Giu} ({r.LyDo}) oracle={o} now={now:yyyy-MM-dd HH:mm} cfg N{c.GiuBoNgay} W{c.GiuBoTuan}/{c.ThuChotTuan} M{c.GiuBoThang}"; }
            }
            // an toàn: luôn giữ bản đạt mới nhất (≤ now); số bộ đạt giữ (không tương lai) ≤ 1 + N + W + tháng
            var okPast = ds.Where(x => x.Dat && !x.KhongRo && x.ThoiDiem <= now).OrderByDescending(x => x.ThoiDiem).ThenByDescending(x => x.Ten, StringComparer.Ordinal).FirstOrDefault();
            var g = Giu(kq);
            if (okPast != null && !g.Contains(okPast.Ten)) { saiAnToan++; vdAt ??= "mất bản đạt mới nhất " + okPast.Ten; }
            int months = ds.Where(x => x.Dat && !x.KhongRo && x.ThoiDiem <= now).Select(x => x.ThoiDiem.Year * 12 + x.ThoiDiem.Month).Distinct().Count();
            int keptOk = kq.Count(x => x.Giu && x.Ban.Dat && !x.Ban.KhongRo && x.Ban.ThoiDiem <= now);
            bool noDel = kq.All(x => x.Giu);
            if (!noDel && keptOk > 1 + c.GiuBoNgay + c.GiuBoTuan + months) { saiAnToan++; vdAt ??= $"giữ {keptOk} > trần"; }
            // không bao giờ xoá khi bản mới nhất lỗi
            var newest = ds.Where(x => x.ThoiDiem <= now && !x.KhongRo).OrderByDescending(x => x.ThoiDiem).ThenByDescending(x => x.Ten, StringComparer.Ordinal).FirstOrDefault();
            if (newest != null && !newest.Dat && !noDel) { saiAnToan++; vdAt ??= "bản mới nhất lỗi mà vẫn xoá"; }
            if (ds.Any(x => (x.ThoiDiem > now || x.KhongRo) && !g.Contains(x.Ten))) { saiAnToan++; vdAt ??= "xoá bản tương lai / không rõ"; }
        }
        Verbose = save;
        Ok(sai == 0, $"S2a {n} lượt: từng bản giữ / xoá khớp đáp án độc lập", vd);
        Ok(saiAnToan == 0, "S2b tính chất an toàn (giữ bản đạt mới nhất, trần số bộ, bản mới nhất lỗi không xoá, bản tương lai giữ)", vdAt);
    }

    // ------------------------------------------------------------------ S3
    static void S3(int n)
    {
        Console.WriteLine("[S3] Luật dọn bản nhanh");
        var c = new SaoLuuCaiDat();   // 48 giờ
        var now = new DateTime(2026, 11, 22, 10, 30, 0);
        var ds = new List<SaoLuuLuatDon.Ban>();
        for (int h = 0; h < 100; h++) ds.Add(B(now.AddHours(-h).AddMinutes(-5)));
        var g = Giu(SaoLuuLuatDon.TinhNhanh(ds, c, now));
        Ok(g.Count == 48, "S3a mỗi giờ 1 bản, 100 giờ: giữ 48", g.Count.ToString());
        var ds2 = ds.ToList(); ds2.Add(B(now.AddMinutes(-1), false));
        Ok(SaoLuuLuatDon.TinhNhanh(ds2, c, now).All(x => x.Giu), "S3b bản nhanh mới nhất lỗi → không xoá");
        var old = new List<SaoLuuLuatDon.Ban> { B(now.AddDays(-10)), B(now.AddDays(-9)) };
        var g3 = Giu(SaoLuuLuatDon.TinhNhanh(old, c, now));
        Ok(g3.SetEquals(new[] { old[0].Ten, old[1].Ten }), "S3c mọi bản quá hạn → vẫn giữ 6 bản đạt mới nhất (ở đây 2)");
        var rnd = new Random(33); int sai = 0; string? vd = null;
        var save = Verbose; Verbose = false;
        for (int it = 0; it < n; it++)
        {
            var cc = new SaoLuuCaiDat { GiuBanNhanhGio = rnd.Next(1, 100) };
            var t0 = new DateTime(2026, 1, 1).AddMinutes(rnd.Next(0, 300 * 1440));
            var set = new Dictionary<string, SaoLuuLuatDon.Ban>();
            for (int i = 0; i < rnd.Next(0, 80); i++) { var b = B(t0.AddMinutes(-rnd.Next(-120, 200 * 60)).AddSeconds(rnd.Next(60)), rnd.Next(8) != 0) with { KhongRo = rnd.Next(30) == 0 }; set[b.Ten] = b; }
            var l = set.Values.ToList();
            foreach (var r in SaoLuuLuatDon.TinhNhanh(l, cc, t0))
                if (GiuNhanhOracle(r.Ban, l, cc, t0) != r.Giu) { sai++; vd ??= $"{r.Ban.Ten} {r.Giu} {r.LyDo}"; }
        }
        Verbose = save;
        Ok(sai == 0, $"S3d {n} lượt ngẫu nhiên khớp đáp án độc lập", vd);
    }

    // ------------------------------------------------------------------ S4
    static async Task S4()
    {
        Console.WriteLine("[S4] Lưu nhanh");
        var e = MkEnv();
        var kq = await e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        var dir = Path.Combine(e.Goc, "MES", "nhanh", "2026-11-22_100000");
        Ok(kq.Dat && Directory.Exists(dir), "S4a lưu nhanh ĐẠT, folder nhanh/2026-11-22_100000", kq.ThongBao);
        var m = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")))!;
        var zip = Path.Combine(dir, "du-lieu", "MES_2026-11-22_100000.bak.zip");
        string? shaZip = null;
        if (File.Exists(zip)) { using var za = ZipFile.OpenRead(zip); using var s = za.GetEntry("MES_2026-11-22_100000.bak")!.Open(); shaZip = ShaStream(s); }
        Ok(m.KetQua == "DAT" && m.BakSha256 == shaZip && m.SoDong["dbo.B"] == 7 && m.Loai == "nhanh", "S4b manifest ĐẠT, mã băm .bak trong zip khớp, có số dòng", $"{m.KetQua} {m.BakSha256} {shaZip}");
        Ok(!File.Exists(Path.Combine(dir, "du-lieu", "MES_2026-11-22_100000.bak")) && !Directory.GetDirectories(Path.Combine(e.Goc, "MES", "nhanh")).Any(d => d.EndsWith(".dang-tao")),
            "S4c không còn .bak rời / folder .dang-tao");
        Ok(m.Files.All(f => Sha(Path.Combine(dir, f.Duong)) == f.Sha256) && m.Files.Count == Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length - 1,
            "S4d mã băm từng file trong manifest khớp file thật, đủ file");
        // bấm lần 2 cùng giây → tên +1 giây, không đè
        var kq2 = await e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(kq2.Dat && kq2.Ten == "2026-11-22_100001", "S4e 2 lần cùng giây → tên mới, không đè bản trước", kq2.Ten);
        // file nén hỏng (giả: sửa 1 byte dữ liệu trong zip sau khi nén) → bản LỖI, không bao giờ thành ĐẠT
        var e4 = MkEnv();
        e4.Svc.ThuSauKhiNen = z =>
        {
            using (var za = ZipFile.Open(z, ZipArchiveMode.Update))
            {
                var en = za.Entries[0]; var nm = en.FullName; string txt; using (var rr = new StreamReader(en.Open())) txt = rr.ReadToEnd();
                en.Delete(); var ne = za.CreateEntry(nm); using var ww = new StreamWriter(ne.Open()); ww.Write(txt + "x");
            }
        };
        var kz = await e4.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        var dz = e4.Svc.Quet(e4.Svc.Store.Doc("MES"), "MES", SaoLuuService.Loai.Nhanh);
        Ok(!kz.Dat && kz.ThongBao.Contains("file nén hỏng") && dz.Count == 1 && !dz[0].Dat, "S4g file nén không khớp mã băm gốc → bản LỖI", kz.ThongBao);
        var r3 = await e.Svc.ChayTayAsync(SaoLuuService.Loai.Nhanh, 2, "sx");
        Ok(!r3.Dat && r3.ThongBao == SaoLuuService.KhongPhaiAdmin && e.Sql.SoLan == 2, "S4f bấm tay không phải Admin → từ chối, không lưu");
    }

    // ------------------------------------------------------------------ S5
    static async Task S5()
    {
        Console.WriteLine("[S5] Trọn bộ");
        var e = MkEnv();
        var caddy = Path.Combine(NewDir("caddy"), "Caddyfile"); File.WriteAllText(caddy, "mes.nhatphat-ltd.vn { reverse_proxy localhost:5000 }");
        var c = e.Svc.Store.Doc("MES"); var khongCo = Path.Combine(TmpRoot, "khong", "co", "file.txt"); c.FileCauHinhThem = new() { caddy, khongCo }; e.Svc.Store.Ghi("MES", c, "thu", "them", e.Svc.Clock());
        var kq = await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        var dir = Path.Combine(e.Goc, "MES", "bo", "2026-11-22_100000");
        Ok(kq.Dat && Directory.Exists(dir), "S5a trọn bộ ĐẠT", kq.ThongBao);
        var m = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")))!;
        bool F(string rel) => File.Exists(Path.Combine(dir, rel));
        Ok(F("cau-hinh/appsettings.json") && F("cau-hinh/appsettings.Production.json") && F("cau-hinh/sao-luu/cai-dat-MES.json") && F("cau-hinh/sao-luu/nhat-ky-cai-dat-MES.log")
           && F("cau-hinh/ban-quyen/mes_cfg.dat") && F("cau-hinh/them/01_Caddyfile") && F("DOC-TRUOC.txt") && F("uploads/uploads.zip") && F("phan-mem/MES.Web.zip"),
            "S5b đủ phần: cấu hình (appsettings, cài đặt + nhật ký sao lưu, bản quyền, Caddyfile), uploads, phần mềm, DOC-TRUOC");
        Ok(m.CanhBao.Any(x => x.Contains(khongCo)) && m.CanhBao.Any(x => x.Contains("SaoLuuMau")), "S5c file thêm không có / chưa có mẫu khôi phục → ghi cảnh báo, bộ vẫn ĐẠT", string.Join(" | ", m.CanhBao));
        List<string> Entries(string z) { using var za = ZipFile.OpenRead(Path.Combine(dir, z)); return za.Entries.Select(x => x.FullName).ToList(); }
        var pm = Entries("phan-mem/MES.Web.zip");
        Ok(pm.Contains("MES.Web.dll") && pm.Contains("Other.dll") && pm.Contains("wwwroot/css/app.css") && !pm.Any(x => x.StartsWith("logs/")) && !pm.Any(x => x.StartsWith("wwwroot/uploads/")),
            "S5d phần mềm (publish): có dll + wwwroot, bỏ logs/ và wwwroot/uploads/", string.Join(",", pm));
        var up = Entries("uploads/uploads.zip");
        Ok(up.SequenceEqual(new[] { "avatars/a.png" }), "S5e uploads.zip = ảnh", string.Join(",", up));
        Ok(m.Files.All(f => Sha(Path.Combine(dir, f.Duong)) == f.Sha256) && m.TongBytes == m.Files.Sum(f => f.Bytes) && m.Loai == "bo-ngay", "S5f mã băm + tổng dung lượng khớp");
        // chạy dotnet run (không có MES.Web.dll ở ContentRoot) → lấy BaseDir + wwwroot
        var e2 = MkEnv(publish: false);
        var kq2 = await e2.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        var pm2 = new List<string>();
        { using var za = ZipFile.OpenRead(Path.Combine(e2.Goc, "MES", "bo", "2026-11-22_100000", "phan-mem", "MES.Web.zip")); pm2 = za.Entries.Select(x => x.FullName).ToList(); }
        Ok(kq2.Dat && pm2.Contains("MES.Web.dll") && pm2.Contains("wwwroot/css/app.css") && !pm2.Any(x => x.Contains("uploads")), "S5g chạy dotnet run: phần mềm = thư mục build + wwwroot", string.Join(",", pm2));
        // thư mục sao lưu nằm TRONG thư mục app → không nén chính nó
        var e3 = MkEnv();
        var inner = Path.Combine(e3.ContentRoot, "backup-trong-app"); Directory.CreateDirectory(inner);
        var c3 = e3.Svc.Store.Doc("MES"); c3.ThuMucTheoMaySql = inner; e3.Svc.Store.Ghi("MES", c3, "thu", "x", e3.Svc.Clock()); e3.Sql.GocSql = inner;
        var k31 = await e3.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        e3.Svc.Clock = () => new DateTime(2026, 11, 22, 11, 0, 0);
        var k32 = await e3.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        List<string> pm3; { using var za = ZipFile.OpenRead(Path.Combine(inner, "MES", "bo", "2026-11-22_110000", "phan-mem", "MES.Web.zip")); pm3 = za.Entries.Select(x => x.FullName).ToList(); }
        Ok(k31.Dat && k32.Dat && !pm3.Any(x => x.StartsWith("backup-trong-app/")), "S5h thư mục sao lưu nằm trong thư mục app: bộ sau không nén bộ trước", string.Join(",", pm3.Where(x => x.Contains("backup"))));
    }

    // ------------------------------------------------------------------ S6
    static async Task S6()
    {
        Console.WriteLine("[S6] Lỗi / chạy chồng");
        var e = MkEnv();
        e.Sql.FailNext = 1;
        var kq = await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        var dir = Path.Combine(e.Goc, "MES", "bo", "2026-11-22_100000");
        var m = File.Exists(Path.Combine(dir, "manifest.json")) ? JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json"))) : null;
        Ok(!kq.Dat && m != null && m.KetQua == "LOI" && m.Loi!.Contains("Giả lỗi BACKUP") && Directory.GetFileSystemEntries(dir).Length == 1,
            "S6a BACKUP lỗi → folder bản LỖI chỉ còn manifest (có câu lỗi), không còn dữ liệu dở", kq.ThongBao);
        var ds = e.Svc.Quet(e.Svc.Store.Doc("MES"), "MES", SaoLuuService.Loai.Bo);
        Ok(ds.Count == 1 && !ds[0].Dat, "S6b Quét: bản lỗi không tính là đạt");
        // 2 ô thư mục lệch
        var e2 = MkEnv(); e2.Sql.DoiGocSql = NewDir("khac");
        var k2 = await e2.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(!k2.Dat && k2.ThongBao.Contains("Lệnh BACKUP của SQL Server lỗi") && k2.ThongBao.Contains("trỏ cùng 1 chỗ") && k2.ThongBao.Contains("quyền ghi"),
            "S6c ô theo máy SQL trỏ chỗ chưa có thư mục → câu lỗi SQL kèm 3 gợi ý (thư mục / quyền / 2 ô)", k2.ThongBao);
        // SQL ghi được nhưng vào chỗ khác chỗ app đọc
        var e2b = MkEnv(); var khac = NewDir("khac2"); e2b.Sql.DoiGocSql = khac;
        Directory.CreateDirectory(Path.Combine(khac, "MES", "nhanh", "2026-11-22_100000.dang-tao", "du-lieu"));
        var k2b = await e2b.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(!k2b.Dat && k2b.ThongBao.Contains("phải trỏ cùng 1 chỗ"), "S6c2 SQL ghi được nhưng app không thấy file → báo rõ 2 ô thư mục", k2b.ThongBao);
        // chạy chồng trong app
        var e3 = MkEnv(); e3.Sql.ChamMs = 300;
        var t1 = e3.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "a", CancellationToken.None);
        await Task.Delay(50);
        var k3 = await e3.Svc.ChayAsync(SaoLuuService.Loai.Bo, "b", CancellationToken.None);
        var k1 = await t1;
        Ok(k1.Dat && !k3.Dat && k3.ThongBao.Contains("Đang có 1 lượt") && e3.Sql.SoLan == 1, "S6d bấm khi đang chạy → không chạy chồng", k3.ThongBao);
        // app khác (cùng DB) đang giữ khoá
        e3.Sql.KhoaBan = true; e3.Sql.ChamMs = 0; e3.Svc.Clock = () => new DateTime(2026, 11, 22, 10, 10, 0);   // lượt bỏ SAU bản đạt gần nhất
        var k4 = await e3.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "c", CancellationToken.None);
        Ok(!k4.Dat && k4.BoQua && k4.ThongBao.Contains("app khác") && e3.Sql.SoLan == 1, "S6e app khác cùng DB đang sao lưu → bỏ lượt (BoQua)", k4.ThongBao);
        Ok(!e3.Svc.TinhCanhBao(e3.Svc.Store.Doc("MES"), "MES", e3.Svc.Clock()).Any(x => x.NoiDung.Contains("không chạy được")), "S6e2 bỏ lượt vì app khác → băng KHÔNG báo đỏ");
        e3.Sql.KhoaBan = false; e3.Sql.FailNext = 0;
        var e3b = MkEnv(); e3b.Svc.Store.Ghi("MES", new SaoLuuCaiDat { BatTuDong = false }, "thu", "x", e3b.Svc.Clock());
        await e3b.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "c", CancellationToken.None);
        var cb3 = e3b.Svc.TinhCanhBao(new SaoLuuCaiDat { ThuMucTheoMaySql = e3b.Goc }, "MES", e3b.Svc.Clock());
        Ok(cb3.Any(x => x.Do && x.NoiDung.Contains("không chạy được")), "S6e3 lượt không chạy được THẬT (vd chưa cài thư mục) → băng vẫn đỏ", string.Join("|", cb3.Select(x => x.NoiDung)));
        // chưa cài thư mục
        var e5 = MkEnv(); e5.Svc.Store.Ghi("MES", new SaoLuuCaiDat { BatTuDong = false }, "thu", "x", e5.Svc.Clock());
        var k5 = await e5.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "c", CancellationToken.None);
        Ok(!k5.Dat && k5.ThongBao.Contains("Chưa cài thư mục") && e5.Sql.SoLan == 0, "S6f chưa cài thư mục → không chạy, báo rõ");
        // lỗi rồi lưu được → bản lỗi vẫn giữ để xem (trong N ngày)
        e.Svc.Clock = () => new DateTime(2026, 11, 22, 10, 31, 0);
        var k6 = await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        var ds6 = e.Svc.Quet(e.Svc.Store.Doc("MES"), "MES", SaoLuuService.Loai.Bo);
        Ok(k6.Dat && ds6.Count == 2, "S6g lần sau đạt: bộ lỗi vẫn giữ để xem", string.Join(",", ds6.Select(x => x.Ten + x.Dat)));
    }

    // ------------------------------------------------------------------ S7
    static async Task S7()
    {
        Console.WriteLine("[S7] Dọn an toàn");
        var e = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        var c = e.Svc.Store.Doc("MES");
        var bo = Path.Combine(e.Goc, "MES", "bo"); Directory.CreateDirectory(bo);
        void MkSet(string ten, bool dat)
        {
            var d = Path.Combine(bo, ten); Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "manifest.json"), JsonSerializer.Serialize(new SaoLuuManifest { KetQua = dat ? "DAT" : "LOI" }));
        }
        for (int i = 0; i < 60; i++) MkSet(new DateTime(2026, 11, 22, 23, 55, 0).AddDays(-i).ToString("yyyy-MM-dd_HHmmss"), true);
        Directory.CreateDirectory(Path.Combine(bo, "ghi-chu-cua-anh")); File.WriteAllText(Path.Combine(bo, "ghi-chu.txt"), "x");
        var khongMf = Path.Combine(bo, "2026-08-01_235500"); Directory.CreateDirectory(khongMf);
        var dtCu = Path.Combine(bo, "2026-11-22_100000.dang-tao"); Directory.CreateDirectory(dtCu);
        var dtMoi = Path.Combine(bo, "2026-11-22_200000.dang-tao"); Directory.CreateDirectory(dtMoi);
        var hong = Path.Combine(bo, "2026-11-20_120000"); Directory.CreateDirectory(hong); File.WriteAllText(Path.Combine(hong, "manifest.json"), "{hỏng");
        var r0 = e.Svc.DonChiTiet(c, "MES", e.Svc.Clock());
        Ok(r0.TamDung != null && e.Svc.Quet(c, "MES", SaoLuuService.Loai.Bo).Count(x => x.Dat) == 60, "S7e 1 lượt muốn xoá 48 bộ (> 3) → TẠM DỪNG, chưa xoá bộ nào", r0.TamDung);
        Ok(await e.Svc.ChoDonLaiAsync(1, "admin") == null, "S7f Admin cho dọn lại (PIN ở tab)");
        int n = e.Svc.Don(c, "MES", e.Svc.Clock());
        var con = Directory.GetDirectories(bo).Select(Path.GetFileName).ToHashSet();
        Ok(con.Contains("ghi-chu-cua-anh") && File.Exists(Path.Combine(bo, "ghi-chu.txt")) && con.Contains("2026-08-01_235500"), "S7a không đụng folder / file lạ, folder đúng tên nhưng không có manifest");
        Ok(!con.Contains("2026-11-22_100000.dang-tao") && con.Contains("2026-11-22_200000.dang-tao"), "S7b .dang-tao bỏ dở > 6 giờ xoá, < 6 giờ giữ");
        var ds = e.Svc.Quet(c, "MES", SaoLuuService.Loai.Bo);
        var hongInfo = ds.FirstOrDefault(x => x.Ten == "2026-11-20_120000");
        Ok(hongInfo != null && !hongInfo.Dat && hongInfo.KhongRo, "S7c manifest hỏng = KHÔNG RÕ (không tính là đạt, luôn giữ)", hongInfo?.Loi);
        Ok(ds.Count(x => x.Dat) == 7 + 3 + 2, "S7d 60 bộ / ngày (24/9 → 22/11) → còn 7 ngày + 3 CN + cuối tháng 10 + cuối tháng 9", ds.Count(x => x.Dat).ToString());
    }

    // ------------------------------------------------------------------ S8
    static async Task S8()
    {
        Console.WriteLine("[S8] Lịch giả 40 ngày (nhịp 1 phút)");
        var t = new DateTime(2026, 10, 1, 7, 13, 0);
        var e = MkEnv(now: t);
        e.Svc.Clock = () => t;
        var c = e.Svc.Store.Doc("MES");
        var log = new List<(DateTime T, SaoLuuService.Loai L, bool Dat)>();
        var rnd = new Random(8);
        var end = t.AddDays(40);
        while (t < end)
        {
            var l = e.Svc.DenHan(e.Svc.Store.Doc("MES"), "MES", t);
            if (l != null)
            {
                if (rnd.Next(25) == 0) e.Sql.FailNext = 1;   // thỉnh thoảng lỗi
                var k = await e.Svc.ChayAsync(l.Value, "tu-dong", CancellationToken.None);
                log.Add((t, l.Value, k.Dat));
            }
            t = t.AddMinutes(1);
        }
        var bo = log.Where(x => x.L == SaoLuuService.Loai.Bo).ToList();
        // mỗi "ngày chốt" (từ 23:55 hôm trước tới 23:54 hôm nay) có đúng 1 bộ ĐẠT; bộ đầu chạy ngay lúc mở
        var boDat = bo.Where(x => x.Dat).Select(x => x.T).ToList();
        var thieu = new List<string>();
        for (var d = new DateTime(2026, 10, 1, 23, 55, 0); d.AddDays(1) <= end; d = d.AddDays(1))
        {
            int cnt = boDat.Count(x => x >= d && x < d.AddDays(1));
            if (cnt != 1) thieu.Add($"{d:dd/MM}:{cnt}");
        }
        Ok(thieu.Count == 0, "S8a mỗi kỳ chốt đúng 1 bộ ĐẠT (lỗi thì thử lại sau 30 phút)", string.Join(" ", thieu.Take(10)));
        Ok(boDat.Count > 0 && boDat[0] == new DateTime(2026, 10, 1, 7, 13, 0), "S8b lần đầu mở app chưa có bộ nào → tạo trọn bộ ngay", boDat.FirstOrDefault().ToString());
        var dat = log.Where(x => x.Dat).Select(x => x.T).OrderBy(x => x).ToList();
        var gap = dat.Zip(dat.Skip(1), (a, b) => (b - a).TotalMinutes).Max();
        // lỗi → thử lại sau 15 phút → khoảng cách tối đa 60 + 15 (1 lần lỗi) — 2 lần lỗi liền có xác suất nhỏ
        Ok(gap <= 60 + 15 * 2, "S8c khoảng cách 2 bản đạt liền nhau ≤ chu kỳ 60 phút (+ thử lại khi lỗi)", gap.ToString());
        var loi = log.Where(x => !x.Dat).ToList();
        var thuLai = loi.Select(x => (x.L, Sau: log.FirstOrDefault(y => y.T > x.T && y.L == x.L).T - x.T)).Where(x => x.Sau > TimeSpan.Zero).ToList();
        Ok(loi.Count > 0 && thuLai.All(x => x.Sau.TotalMinutes >= (x.L == SaoLuuService.Loai.Bo ? 30 : 15)),
            "S8d có lần lỗi giả; thử lại cùng loại sau ≥ 15 phút (nhanh) / ≥ 30 phút (bộ) — không chạy dồn mỗi phút", $"{loi.Count} lỗi; thử lại sau {string.Join(",", thuLai.Select(x => x.L + ":" + x.Sau.TotalMinutes))}");
        var bNow = e.Svc.Quet(c, "MES", SaoLuuService.Loai.Bo);
        var nNow = e.Svc.Quet(c, "MES", SaoLuuService.Loai.Nhanh);
        var luat = SaoLuuLuatDon.TinhBoNgay(bNow.Select(x => new SaoLuuLuatDon.Ban(x.Ten, x.ThoiDiem, x.Dat)).ToList(), c, t);
        Ok(luat.All(x => x.Giu), "S8e sau mỗi lần lưu đã dọn: các bộ còn lại đều thuộc luật giữ", string.Join(",", luat.Where(x => !x.Giu).Select(x => x.Ban.Ten)));
        Ok(nNow.All(x => x.ThoiDiem > t.AddHours(-49)) && nNow.Count(x => x.Dat) <= 49, "S8f bản nhanh còn trong 48 giờ", nNow.Count.ToString());
        Ok(bNow.Count(x => x.Dat) <= 7 + 4 + 2, "S8g số bộ ngày đạt ≤ 7 + 4 + 2 tháng", bNow.Count(x => x.Dat).ToString());
        // tắt tự động / đổi chu kỳ
        var c2 = c.Clone(); c2.BatTuDong = false; e.Svc.Store.Ghi("MES", c2, "thu", "tat", t);
        Ok(e.Svc.DenHan(c2, "MES", t.AddDays(3)) == null, "S8h tắt tự động → không đến hạn");
        Ok(e.Svc.DocTamDung(c, "MES") == null, "S8i chạy bình thường 40 ngày → không bao giờ tạm dừng dọn nhầm");
    }

    // ------------------------------------------------------------------ S9
    static async Task S9()
    {
        Console.WriteLine("[S9] Cài đặt");
        var bad = new SaoLuuCaiDat { ThuMucTheoMaySql = "tuong-doi", PhutLuuNhanh = 5, GioChotBoNgay = "25:00", GiuBoNgay = 0, GiuBoTuan = -1, GiuBoThang = -1, GiuGoogleNam = 0, GiuBanNhanhGio = 0, FileCauHinhThem = new() { "caddy" } };
        var loi = bad.KiemLoi();
        Ok(loi.Count == 9, "S9a 9 ô sai → 9 câu báo", string.Join(" | ", loi));
        Ok(new SaoLuuCaiDat { BatTuDong = true }.KiemLoi().Any(x => x.Contains("thư mục")), "S9b bật tự động mà trống thư mục → báo");
        Ok(new SaoLuuCaiDat { BatTuDong = false }.KiemLoi().Count == 0 && new SaoLuuCaiDat { ThuMucTheoMaySql = @"D:\MES-Backup", ThuMucTheoMayApp = @"\\192.168.1.211\MES-Backup" }.KiemLoi().Count == 0,
            "S9c mặc định + đường dẫn Windows / UNC hợp lệ");
        var e = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        var c = e.Svc.Store.Doc("MES");
        var moi = c.Clone(); moi.GiuBoNgay = 3;
        var r1 = await e.Svc.LuuCaiDatAsync(moi, 2, "sx");
        Ok(r1 == SaoLuuService.KhongPhaiAdmin && e.Svc.Store.Doc("MES").GiuBoNgay == 7, "S9d không phải Admin → không lưu");
        var r2 = await e.Svc.LuuCaiDatAsync(moi, 1, "admin");
        var nk = e.Svc.Store.DocNhatKy("MES", 5);
        Ok(r2 == null && e.Svc.Store.Doc("MES").GiuBoNgay == 3 && nk[0].Contains("admin") && nk[0].Contains("Giữ bộ ngày: 7 → 3"), "S9e Admin lưu được, nhật ký ghi ai / đổi gì", string.Join(" / ", nk));
        var r3 = await e.Svc.LuuCaiDatAsync(new SaoLuuCaiDat { ThuMucTheoMaySql = "x" }, 1, "admin");
        Ok(r3 != null && e.Svc.Store.Doc("MES").GiuBoNgay == 3, "S9f nhập sai → không lưu", r3);
        File.WriteAllText(e.Svc.Store.FileCaiDat("MES"), "{hỏng");
        bool nem = false; try { e.Svc.Store.Doc("MES"); } catch { nem = true; }
        Ok(nem, "S9g file cài đặt hỏng → báo lỗi, không âm thầm về mặc định");
        // xem trước bản sẽ xoá khi giảm số giữ
        var e2 = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        var bo = Path.Combine(e2.Goc, "MES", "bo");
        for (int i = 0; i < 10; i++)
        {
            var d = Path.Combine(bo, new DateTime(2026, 11, 22, 23, 55, 0).AddDays(-i).ToString("yyyy-MM-dd_HHmmss")); Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "manifest.json"), JsonSerializer.Serialize(new SaoLuuManifest { KetQua = "DAT" }));
        }
        var c2 = e2.Svc.Store.Doc("MES");
        var giam = c2.Clone(); giam.GiuBoNgay = 2; giam.GiuBoTuan = 0;
        var xoa = await e2.Svc.XemTruocXoaAsync(giam);
        // đang giữ: 22..16 (7 ngày) + 15 (tuần) ; mới: 22, 21 + tháng (22 đã) → xoá 20,19,18,17,16,15
        Ok(xoa.Count == 6 && xoa.All(x => x.StartsWith("Bộ ngày")), "S9h giảm 7 → 2 ngày, 4 → 0 tuần: báo trước 6 bộ sẽ xoá", string.Join(", ", xoa));
        var tang = c2.Clone(); tang.GiuBoNgay = 30;
        Ok((await e2.Svc.XemTruocXoaAsync(tang)).Count == 0, "S9i tăng số giữ → không xoá gì, không hỏi PIN");
        var doiThuMuc = giam.Clone(); doiThuMuc.ThuMucTheoMaySql = NewDir("moi");
        Ok((await e2.Svc.XemTruocXoaAsync(doiThuMuc)).Count == 0, "S9j đổi sang thư mục khác → bản ở thư mục cũ không bị xoá");
    }

    // ------------------------------------------------------------------ S10
    static async Task S10()
    {
        Console.WriteLine("[S10] Băng cảnh báo");
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        var c0 = new SaoLuuCaiDat();
        var v0 = e.Svc.TinhCanhBao(c0, "MES", now);
        Ok(v0.Count == 1 && v0[0].Do && v0[0].NoiDung.Contains("Chưa cài thư mục"), "S10a chưa cài thư mục → đỏ");
        var c = e.Svc.Store.Doc("MES");
        var v1 = e.Svc.TinhCanhBao(c, "MES", now);
        Ok(v1.Any(x => x.NoiDung.Contains("Chưa có bản sao lưu nào")), "S10b chưa có bản nào → đỏ");
        await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        Ok(e.Svc.TinhCanhBao(c, "MES", now.AddMinutes(30)).Count == 0, "S10c vừa lưu trọn bộ → không cảnh báo");
        var v2 = e.Svc.TinhCanhBao(c, "MES", now.AddMinutes(130));
        Ok(v2.Count == 1 && v2[0].NoiDung.Contains("chưa có bản sao lưu mới"), "S10d quá 2 chu kỳ + 5 phút → đỏ", string.Join("|", v2.Select(x => x.NoiDung)));
        Ok(!e.Svc.TinhCanhBao(c, "MES", now.AddMinutes(124)).Any(), "S10e trong 2 chu kỳ + 5 phút → chưa báo");
        var v3 = e.Svc.TinhCanhBao(c, "MES", now.AddHours(27));
        Ok(v3.Any(x => x.NoiDung.Contains("Quá 26 giờ")), "S10f quá 26 giờ không có bộ ngày → đỏ");
        e.Svc.Clock = () => now.AddMinutes(61);
        e.Sql.FailNext = 1;
        await e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        var v4 = e.Svc.TinhCanhBao(c, "MES", now.AddMinutes(62));
        Ok(v4.Any(x => x.Do && x.NoiDung.Contains("LỖI") && x.NoiDung.Contains("Giả lỗi")), "S10g lần lưu gần nhất lỗi → đỏ kèm câu lỗi", string.Join("|", v4.Select(x => x.NoiDung)));
        // bản nhanh LỖI cùng giây với bộ ĐẠT → vẫn báo lỗi
        var e11 = MkEnv(now: now);
        await e11.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        e11.Sql.FailNext = 1;
        await e11.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        var v11 = e11.Svc.TinhCanhBao(e11.Svc.Store.Doc("MES"), "MES", now.AddMinutes(1));
        Ok(v11.Any(x => x.Do && x.NoiDung.Contains("LỖI")), "S10i bản nhanh LỖI cùng giây với bộ ĐẠT → vẫn báo lỗi", string.Join("|", v11.Select(x => x.NoiDung)));
        var off = c.Clone(); off.BatTuDong = false;
        var v5 = e.Svc.TinhCanhBao(off, "MES", now.AddHours(30));
        Ok(v5.Any(x => !x.Do && x.NoiDung.Contains("TẮT")) && !v5.Any(x => x.NoiDung.Contains("Quá 26")), "S10h tắt tự động → vàng, không báo quá hạn");
    }

    // ------------------------------------------------------------------ S11 [G32-RA]
    static void MkSetDisk(string goc, string kind, DateTime t, string mfJson)
    {
        var d = Path.Combine(goc, "MES", kind, t.ToString("yyyy-MM-dd_HHmmss")); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "manifest.json"), mfJson);
    }
    const string MfDat = "{\"KetQua\":\"DAT\",\"TongBytes\":10}";

    static async Task S11()
    {
        Console.WriteLine("[S11] Lỗi rà độc lập (G32-RA)");
        // a) bản ghi giờ tương lai không chặn lịch, băng báo
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        MkSetDisk(e.Goc, "bo", now.AddYears(1), MfDat); MkSetDisk(e.Goc, "nhanh", now.AddYears(1), MfDat);
        var c = e.Svc.Store.Doc("MES");
        Ok(e.Svc.DenHan(c, "MES", now.AddDays(3)) == SaoLuuService.Loai.Bo, "S11a bản giờ +1 năm không làm lịch dừng (vẫn đến hạn bộ ngày)");
        var cb = e.Svc.TinhCanhBao(c, "MES", now.AddDays(3));
        Ok(cb.Any(x => !x.Do && x.NoiDung.Contains("SAU giờ máy")) && cb.Any(x => x.Do && x.NoiDung.Contains("Chưa có bản sao lưu nào đạt")), "S11a2 băng: vàng 'bản giờ sau giờ máy' + đỏ 'chưa có bản đạt thật'", string.Join(" | ", cb.Select(x => x.NoiDung)));

        // b) manifest sai kiểu / rỗng: không bị coi là LỖI để xoá; bộ tháng cũ giữ
        var e2 = MkEnv(now: now);
        MkSetDisk(e2.Goc, "bo", new DateTime(2026, 8, 31, 23, 55, 0), "{\"KetQua\":\"DAT\",\"TongBytes\":\"10\"}");
        MkSetDisk(e2.Goc, "bo", new DateTime(2026, 9, 30, 23, 55, 0), "");
        MkSetDisk(e2.Goc, "bo", new DateTime(2026, 10, 15, 23, 55, 0), "{\"KetQua\":\"???\"}");
        var k = await e2.Svc.ChayAsync(SaoLuuService.Loai.Bo, "tu-dong", CancellationToken.None);
        var ds = e2.Svc.Quet(e2.Svc.Store.Doc("MES"), "MES", SaoLuuService.Loai.Bo);
        Ok(k.Dat && ds.Count == 4 && ds.Single(x => x.Ten.StartsWith("2026-08")).Dat && ds.Count(x => x.KhongRo) == 2,
            "S11b manifest sai kiểu số vẫn đọc ĐẠT; rỗng / KetQua lạ = KHÔNG RÕ, không bị dọn", string.Join(",", ds.Select(x => $"{x.Ten}:{x.Dat}:{x.KhongRo}")));
        Ok(e2.Svc.TinhCanhBao(e2.Svc.Store.Doc("MES"), "MES", now.AddMinutes(1)).Any(x => x.NoiDung.Contains("không đọc được manifest")), "S11b2 băng báo bản không đọc được manifest");

        // c) .dang-tao bỏ dở 2 ngày → dọn ngay trong lượt lưu đạt
        var e3 = MkEnv(now: now);
        var dt = Path.Combine(e3.Goc, "MES", "nhanh", now.AddDays(-2).ToString("yyyy-MM-dd_HHmmss") + ".dang-tao", "du-lieu");
        Directory.CreateDirectory(dt); File.WriteAllText(Path.Combine(dt, "MES_x.bak"), new string('x', 1000));
        var k3 = await e3.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(k3.Dat && !Directory.Exists(Path.GetDirectoryName(dt)), "S11c folder .dang-tao bỏ dở được dọn trong lượt lưu thật", k3.ThongBao);

        // d) đồng hồ nhảy tới 1 năm: tạm dừng dọn tới khi Admin cho dọn lại
        var e4 = MkEnv(now: now);
        var c4 = e4.Svc.Store.Doc("MES"); c4.GiuBoThang = 3; e4.Svc.Store.Ghi("MES", c4, "thu", "x", now);
        for (int i = 0; i < 30; i++) MkSetDisk(e4.Goc, "bo", now.Date.AddDays(-i).AddHours(23).AddMinutes(55).AddDays(-1), MfDat);
        for (int i = 0; i < 20; i++) MkSetDisk(e4.Goc, "nhanh", now.AddHours(-i - 1), MfDat);
        var t4 = now.AddYears(1); e4.Svc.Clock = () => t4;
        var k4 = await e4.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        int n4() => e4.Svc.Quet(c4, "MES", SaoLuuService.Loai.Bo).Count + e4.Svc.Quet(c4, "MES", SaoLuuService.Loai.Nhanh).Count;
        Ok(k4.Dat && n4() == 36 && e4.Svc.DocTamDung(c4, "MES") != null && k4.ThongBao.Contains("TẠM DỪNG"),
            "S11d đồng hồ +1 năm: lưu được; bộ ngày TẠM DỪNG dọn (30 còn nguyên), bản nhanh còn 6 mới nhất (luôn giữ 6)", $"{n4()} {k4.ThongBao}");
        Ok(e4.Svc.TinhCanhBao(c4, "MES", t4).Any(x => !x.Do && x.NoiDung.Contains("Tạm dừng dọn")), "S11d2 băng vàng 'Tạm dừng dọn'");
        t4 = t4.AddHours(1);
        await e4.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(n4() == 36 && e4.Svc.Quet(c4, "MES", SaoLuuService.Loai.Bo).Count == 30, "S11d3 lượt sau vẫn không dọn bộ ngày khi chưa cho phép", n4().ToString());
        Ok(await e4.Svc.ChoDonLaiAsync(2, "sx") == SaoLuuService.KhongPhaiAdmin && e4.Svc.DocTamDung(c4, "MES") != null, "S11d4 người thường không bấm 'Cho dọn lại' được");
        Ok(await e4.Svc.ChoDonLaiAsync(1, "admin") == null && e4.Svc.DocTamDung(c4, "MES") == null && e4.Svc.Store.DocNhatKy("MES", 1)[0].Contains("Cho dọn lại"), "S11d5 Admin cho dọn lại → bỏ tạm dừng, ghi nhật ký");
        t4 = t4.AddHours(1);
        var k4b = await e4.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(k4b.Dat && n4() == 7 && !File.Exists(Path.Combine(e4.Goc, "MES", SaoLuuService.TenChoPhep)), "S11d6 sau khi cho phép: dọn theo luật (1 bộ + 6 bản nhanh), cho phép dùng 1 lần", $"{n4()} {k4b.ThongBao}");
        // d7) đồng hồ +60 giờ (< 3 ngày): bản nhanh không mất sạch
        var e4b = MkEnv(now: now);
        for (int i = 0; i < 48; i++) MkSetDisk(e4b.Goc, "nhanh", now.AddHours(-i - 1), MfDat);
        var t4b = now.AddHours(60); e4b.Svc.Clock = () => t4b;
        await e4b.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(e4b.Svc.Quet(e4b.Svc.Store.Doc("MES"), "MES", SaoLuuService.Loai.Nhanh).Count == 6, "S11d7 đồng hồ +60 giờ: còn 6 bản nhanh mới nhất (không xoá sạch)");
        // d8) tắt tự động, bấm tay mỗi tuần → không tạm dừng nhầm
        var e4c = MkEnv(now: now); var tc = now; e4c.Svc.Clock = () => tc;
        var cc4 = e4c.Svc.Store.Doc("MES"); cc4.BatTuDong = false; e4c.Svc.Store.Ghi("MES", cc4, "thu", "x", now);
        for (int w = 0; w < 10; w++) { tc = now.AddDays(7 * w); await e4c.Svc.ChayAsync(SaoLuuService.Loai.Bo, "bam-tay:admin", CancellationToken.None); }
        Ok(e4c.Svc.DocTamDung(cc4, "MES") == null, "S11d8 chỉ bấm tay trọn bộ mỗi tuần (10 tuần) → không tạm dừng nhầm");
        // d9) folder ".dang-tao.xoa" xoá dở được dọn
        var e4d = MkEnv(now: now);
        var dx = Path.Combine(e4d.Goc, "MES", "nhanh", now.AddDays(-1).ToString("yyyy-MM-dd_HHmmss") + ".dang-tao.xoa"); Directory.CreateDirectory(dx);
        await e4d.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(!Directory.Exists(dx), "S11d9 folder '.dang-tao.xoa' xoá dở lần trước được dọn");

        // e) xem trước không lách được
        var e5 = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        for (int i = 0; i < 10; i++) MkSetDisk(e5.Goc, "bo", new DateTime(2026, 11, 22, 23, 55, 0).AddDays(-i), MfDat);
        var c5 = e5.Svc.Store.Doc("MES");
        var g5 = c5.Clone(); g5.GiuBoNgay = 2; g5.GiuBoTuan = 0;
        var x1 = await e5.Svc.XemTruocXoaAsync(g5);
        var khac = c5.Clone(); khac.ThuMucTheoMaySql = NewDir("khac5");
        await e5.Svc.LuuCaiDatAsync(khac, 1, "admin");
        var x2 = await e5.Svc.XemTruocXoaAsync(g5);
        var g5b = g5.Clone(); g5b.ThuMucTheoMaySql = e5.Goc + "/";
        await e5.Svc.LuuCaiDatAsync(c5, 1, "admin");
        var x3 = await e5.Svc.XemTruocXoaAsync(g5b);
        Ok(x1.Count == 6 && x2.Count == 6 && x3.Count == 6, "S11e đổi thư mục rồi đổi lại / viết đường dẫn khác kiểu → vẫn báo trước 6 bộ sẽ xoá", $"{x1.Count} {x2.Count} {x3.Count}");
        MkSetDisk(e5.Goc, "bo", new DateTime(2026, 11, 22, 23, 57, 0), "{\"KetQua\":\"LOI\",\"Loi\":\"x\"}");
        var x4 = await e5.Svc.XemTruocXoaAsync(g5);
        Ok(x4.Count == 6, "S11e2 bộ mới nhất đang LỖI → xem trước vẫn báo 6 (lần đạt kế tiếp sẽ xoá)", x4.Count.ToString());
        // e3) lách 2 lần lưu (thư mục khác + giảm số giữ, rồi sửa lại thư mục) → lần dọn vẫn TẠM DỪNG (cần PIN)
        var e5b = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        for (int i = 0; i < 10; i++) MkSetDisk(e5b.Goc, "bo", new DateTime(2026, 11, 22, 23, 55, 0).AddDays(-i), MfDat);
        var lach = e5b.Svc.Store.Doc("MES").Clone(); lach.ThuMucTheoMaySql = NewDir("trong"); lach.GiuBoNgay = 2; lach.GiuBoTuan = 0;
        await e5b.Svc.LuuCaiDatAsync(lach, 1, "admin");
        var lach2 = lach.Clone(); lach2.ThuMucTheoMaySql = e5b.Goc;
        var xl = await e5b.Svc.XemTruocXoaAsync(lach2);
        await e5b.Svc.LuuCaiDatAsync(lach2, 1, "admin");
        var kl = await e5b.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(xl.Count == 0 && kl.ThongBao.Contains("TẠM DỪNG") && e5b.Svc.Quet(lach2, "MES", SaoLuuService.Loai.Bo).Count == 10,
            "S11e3 lách bước xem trước bằng 2 lần lưu → lần dọn tạm dừng, 10 bộ còn nguyên (cần PIN 'Cho dọn lại')", kl.ThongBao);
        // e4) lưu cài đặt CÓ PIN xác nhận → lần dọn tới được xoá đúng số đó
        var e5c = MkEnv(now: new DateTime(2026, 11, 22, 23, 58, 0));
        for (int i = 0; i < 10; i++) MkSetDisk(e5c.Goc, "bo", new DateTime(2026, 11, 22, 23, 55, 0).AddDays(-i), MfDat);
        var gc = e5c.Svc.Store.Doc("MES").Clone(); gc.GiuBoNgay = 2; gc.GiuBoTuan = 0;
        var xc = await e5c.Svc.XemTruocXoaAsync(gc);
        await e5c.Svc.LuuCaiDatAsync(gc, 1, "admin", xc.Count(x => x.StartsWith("Bộ ngày")));
        var kc = await e5c.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "tu-dong", CancellationToken.None);
        Ok(!kc.ThongBao.Contains("TẠM DỪNG") && e5c.Svc.Quet(gc, "MES", SaoLuuService.Loai.Bo).Count == 2 && e5c.Svc.Store.DocNhatKy("MES", 1)[0].Contains("PIN xác nhận xoá 6"),
            "S11e4 lưu cài đặt có PIN xác nhận 6 bộ (+2 bộ vốn ngoài luật) → lần dọn tới xoá 8, không tạm dừng; nhật ký ghi", kc.ThongBao);
        // e5) xem trước ghi rõ bản lỗi
        MkSetDisk(e5c.Goc, "bo", new DateTime(2026, 11, 21, 23, 57, 0), "{\"KetQua\":\"LOI\",\"Loi\":\"x\"}");
        var g1 = gc.Clone(); g1.GiuBoNgay = 1;
        var xe = await e5c.Svc.XemTruocXoaAsync(g1);
        Ok(xe.Any(x => x.Contains("(bản lỗi)")), "S11e5 xem trước ghi '(bản lỗi)' cho bản lỗi", string.Join(";", xe));

        // f) file cài đặt hỏng → lưu đè được, file cũ đổi tên; số sai sửa tay = hỏng
        var e6 = MkEnv(now: now);
        File.WriteAllText(e6.Svc.Store.FileCaiDat("MES"), "{hỏng");
        var r6 = await e6.Svc.LuuCaiDatAsync(new SaoLuuCaiDat { ThuMucTheoMaySql = e6.Goc }, 1, "admin");
        Ok(r6 == null && e6.Svc.Store.Doc("MES").ThuMucTheoMaySql == e6.Goc && Directory.GetFiles(e6.CfgDir, "cai-dat-MES.json.hong-*").Length == 1
           && e6.Svc.Store.DocNhatKy("MES", 1)[0].Contains("hỏng"), "S11f file cài đặt hỏng → Admin lưu đè được, file cũ đổi tên giữ lại, nhật ký ghi", r6);
        File.WriteAllText(e6.Svc.Store.FileCaiDat("MES"), "{\"BatTuDong\":true,\"ThuMucTheoMaySql\":\"" + e6.Goc.Replace("\\", "\\\\") + "\",\"PhutLuuNhanh\":0}");
        bool nem = false; try { e6.Svc.Store.Doc("MES"); } catch { nem = true; }
        Ok(nem, "S11f2 sửa tay PhutLuuNhanh = 0 → coi như hỏng (không chạy lưu mỗi 30 giây)");
        var tepChan = Path.Combine(e6.Goc, "la-file"); File.WriteAllText(tepChan, "x");   // thư mục con của 1 FILE → không tạo được (Windows lẫn Linux)
        var r6b = await e6.Svc.LuuCaiDatAsync(new SaoLuuCaiDat { ThuMucTheoMaySql = Path.Combine(tepChan, "con") }, 1, "admin");
        Ok(r6b != null && r6b.Contains("Không tạo / mở được thư mục"), "S11f3 thư mục không tạo / mở được từ app → báo ngay lúc lưu", r6b);

        // g) file thêm nằm trong / trùm thư mục sao lưu
        var g7 = new SaoLuuCaiDat { ThuMucTheoMaySql = @"D:\MES-Backup", FileCauHinhThem = new() { @"D:\", @"d:/mes-backup/x.txt", @"D:\MES-Backup2\a" } };
        var l7 = g7.KiemLoi();
        Ok(l7.Count(x => x.Contains("không được nằm trong / chứa")) == 2, "S11g file thêm là thư mục cha / nằm trong thư mục sao lưu → chặn (thư mục tên gần giống thì cho)", string.Join(" | ", l7));

        // h) tên DB đổi ký tự không trùng
        Ok(SaoLuuCaiDatStore.TenAnToan("MES") == "MES" && SaoLuuCaiDatStore.TenAnToan("MES.1") != SaoLuuCaiDatStore.TenAnToan("MES 1"), "S11h 'MES.1' và 'MES 1' không dùng chung thư mục", SaoLuuCaiDatStore.TenAnToan("MES.1") + " / " + SaoLuuCaiDatStore.TenAnToan("MES 1"));

        // i) thư mục không mở được → băng nói rõ
        var e9 = MkEnv(now: now);
        var c9 = e9.Svc.Store.Doc("MES"); Directory.Delete(e9.Goc, true);
        var cb9 = e9.Svc.TinhCanhBao(c9, "MES", now);
        Ok(cb9.Count == 1 && cb9[0].Do && cb9[0].NoiDung.Contains("Không mở được thư mục sao lưu"), "S11i thư mục mất → đỏ 'Không mở được thư mục' (không phải 'chưa có bản')", string.Join("|", cb9.Select(x => x.NoiDung)));
    }
}
