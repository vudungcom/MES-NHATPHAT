// [GN-NG-TRACE 2026-10-28] Lệnh "nang": kiểm nặng — chạy 4 kịch bản cố định (kh, loi, vu — [VULOI], chuoi — [CHUOI]) rồi nhiều lượt ngẫu nhiên
// với các cấu hình khác nhau, SONG SONG theo số nhân máy. Mỗi lượt là một tiến trình riêng (chính tool này),
// kết quả từng lượt ghi vào thư mục nang_ketqua\ để gửi lại khi có SAI.
//
//   dotnet bin2\GiaoNhanFuzz.dll nang [số PO mỗi lượt = 60] [thao tác mỗi PO = 120] [seed đầu = 1]
//   set SONG_SONG=2   → số tiến trình chạy cùng lúc (mặc định: số nhân máy − 1)
//   set KHUC=10       → số PO mỗi khúc (mặc định tự tính ≈ 4 khúc / nhân) [KIEM-NHAN]
using System.Diagnostics;
using System.Text;

public static class Nang
{
    static readonly (string Name, string Desc, (string K, string V)[] Env)[] Configs =
    {
        ("loi",         "thử nặng hàng lỗi",                           new[] { ("LOI", "1") }),
        ("loi_loaitru", "hàng lỗi + 70% PO có máy loại trừ",           new[] { ("LOI", "1"), ("EXCL", "0.7") }),
        ("loi_baoloi",  "hàng lỗi + nhiều báo lỗi sau khi nhận / huỷ", new[] { ("LOI", "1"), ("NGREP", "40") }),
        ("loi_phoi",    "hàng lỗi + Kho sửa phôi nhận",                new[] { ("LOI", "1"), ("PHOI", "8") }),
        ("vu_suawts",   "hàng lỗi + nhiều sửa dòng WTS (giữ vụ lỗi)",  new[] { ("LOI", "1"), ("WTSEDIT", "20") }),   // [VULOI]
        ("thuong",      "như lệnh thường (không đặt biến)",            Array.Empty<(string, string)>()),
        ("loaitru",     "lệnh thường + 70% PO có máy loại trừ",        new[] { ("EXCL", "0.7") }),
        // [CHUOI 2026-10-29] PO dựng bằng hàm thật đi đủ chuỗi Part Master → KH → snapshot → Kho; sửa Part Master giữa chừng; PO hàng xóm
        ("chuoi_loi",   "đi đủ chuỗi Part Master → KH + hàng lỗi",      new[] { ("CHUOI", "1"), ("LOI", "1") }),
        ("chuoi_loaitru", "đi đủ chuỗi + 70% PO có máy loại trừ",       new[] { ("CHUOI", "1"), ("LOI", "1"), ("EXCL", "0.7") }),
        ("chuoi_thuong", "đi đủ chuỗi, lệnh thường",                    new[] { ("CHUOI", "1") }),
        // [TARO-NC-MA][TARO-3T-GOP 2026-11-06] đi đủ chuỗi, Taro mã dùng chung 2 NC + bộ gộp 8G ← 8G1 + 8G2 (75% PO), hàng lỗi
        ("chuoi_taro",  "đi đủ chuỗi + Taro mã chung 2 NC / bộ gộp",   new[] { ("CHUOI", "1"), ("LOI", "1"), ("TARO", "1") }),
    };

    public static async Task<int> Run(string[] args)
    {
        int po    = args.Length > 1 ? int.Parse(args[1]) : 60;
        int ops   = args.Length > 2 ? int.Parse(args[2]) : 120;
        int seed0 = args.Length > 3 ? int.Parse(args[3]) : 1;
        int par   = int.TryParse(Environment.GetEnvironmentVariable("SONG_SONG"), out var p) && p > 0 ? p : Math.Max(1, Environment.ProcessorCount - 1);
        var dll   = typeof(Nang).Assembly.Location;
        var dir   = Path.Combine(Environment.CurrentDirectory, "nang_ketqua");
        Directory.CreateDirectory(dir);

        // [KIEM-NHAN 2026-11-01] Tận dụng hết nhân máy: mỗi lượt (1 seed) chia thành nhiều "khúc" PO liên tiếp, mỗi khúc 1 tiến trình
        //   (tham số thứ 4 = PO bắt đầu). Mỗi PO tự có Random(seed*100000 + po) + DB riêng → chia khúc chạy ĐÚNG Y NHỮNG PO / thao tác
        //   như chạy liền 1 tiến trình (cùng phép kiểm, chỉ nhanh hơn). Trước: 10 lượt = 10 tiến trình → máy 20 nhân chỉ dùng 10,
        //   thời gian = lượt chậm nhất (~9 phút). 8 kịch bản cố định cũng chạy song song trong cùng hàng đợi.
        //   KHUC = số PO mỗi khúc (mặc định tự tính để có ≈ 4 khúc / nhân).
        int khuc = int.TryParse(Environment.GetEnvironmentVariable("KHUC"), out var kq) && kq > 0 ? kq
                 : Math.Max(1, (int)Math.Ceiling(po * Configs.Length / (double)(par * 4)));
        var scens = new[] { "kh", "loi", "vu", "chuoi", "wts", "quyen", "iso", "quet", "nghi", "viec", "auth", "std", "sua", "form", "taro" };   // [TARO-NC-MA] taro   // [GN-FORM1] form   // [GC-SUA-TAI-CHO] sua   // [STD-UPDATE] std · [TOOLS-PUSH] viec   // [ML17-XN] nghi · [RA-SOCHUNG] quet · [RS-C1][RS-C2] quyen · [WTS-SVC] wts · [VULOI] vu · [CHUOI] chuoi
        Console.WriteLine($"KIỂM NẶNG: {scens.Length} kịch bản + {Configs.Length} lượt ngẫu nhiên × {po} PO × {ops} thao tác · {Environment.ProcessorCount} nhân · chạy cùng lúc {par} tiến trình · mỗi khúc {khuc} PO");
        Console.WriteLine($"Kết quả từng khúc: {dir}");
        var sw = Stopwatch.StartNew();
        int totalSai = 0;
        var lockObj = new object();
        double cpuSum = 0, wallSum = 0;

        // Hàng đợi việc: kịch bản (nhẹ, cần báo sớm) + khúc của các lượt, lượt hàng lỗi (nặng hơn) trước
        var jobs = new List<(int Cfg, string Args, string File, (string K, string V)[] Env, string Scen)>();
        foreach (var sc in scens) jobs.Add((-1, sc, Path.Combine(dir, $"kichban_{sc}.txt"), Array.Empty<(string, string)>(), sc));
        var cfgOrder = Enumerable.Range(0, Configs.Length).OrderByDescending(i => Configs[i].Env.Any(e => e.K == "LOI")).ToList();
        foreach (var i in cfgOrder)
        {
            var c = Configs[i]; var seed = seed0 + i;
            foreach (var f in Directory.GetFiles(dir, $"{c.Name}_seed{seed}*.txt")) { try { File.Delete(f); } catch { } }
            for (int st = 0; st < po; st += khuc)
            {
                int n = Math.Min(khuc, po - st);
                jobs.Add((i, $"{n} {ops} {seed} {st}", Path.Combine(dir, $"{c.Name}_seed{seed}_po{st}-{st + n - 1}.txt"), c.Env, ""));
            }
        }
        var left   = Configs.Select((c, i) => jobs.Count(j => j.Cfg == i)).ToArray();
        var cfgSai = new int[Configs.Length]; var cfgChecks = new long[Configs.Length]; var cfgBad = new bool[Configs.Length]; var cfgTreo = new bool[Configs.Length];
        var cfgCpu = new double[Configs.Length]; var cfgFiles = Configs.Select(_ => new List<string>()).ToArray();
        int cfgDone = 0;

        var sem = new SemaphoreSlim(par);
        var tasks = jobs.Select(j => Task.Run(async () =>
        {
            await sem.WaitAsync();
            try
            {
                var (code, outFile, sec, cpu) = await RunChild(dll, j.Args, j.Env, j.File);
                lock (lockObj) { cpuSum += cpu; wallSum += sec; }
                if (j.Cfg < 0)
                {
                    var dat = File.ReadAllText(outFile).Contains("KẾT QUẢ: ĐẠT hết");
                    lock (lockObj)
                    {
                        Console.WriteLine($"  {(dat ? "ĐẠT" : code == -999 ? "TREO" : "SAI")}  kịch bản {j.Scen} · {sec:0} giây" + (dat ? "" : $" → xem {outFile}"));   // [KIEM-TREO]
                        if (!dat) totalSai++;
                    }
                    return;
                }
                var lines = File.ReadAllLines(outFile);
                var saiLine = lines.LastOrDefault(l => l.StartsWith("SAI:"));
                var sai = saiLine != null && int.TryParse(saiLine.Substring(4).Trim(), out var n) ? n : -1;   // -1 = không chạy xong
                long.TryParse(lines.LastOrDefault(l => l.StartsWith("Phép kiểm:"))?.Substring(10).Trim(), out var chk);
                lock (lockObj)
                {
                    var i = j.Cfg;
                    if (sai > 0) cfgSai[i] += sai;
                    if (sai < 0) cfgBad[i] = true;
                    if (code == -999) cfgTreo[i] = true;
                    if (sai != 0) cfgFiles[i].Add(outFile);
                    cfgChecks[i] += chk; cfgCpu[i] += cpu;
                    if (--left[i] == 0)
                    {
                        cfgDone++;
                        var c = Configs[i];
                        bool ok = cfgSai[i] == 0 && !cfgBad[i];
                        if (!ok) totalSai += cfgSai[i] + (cfgBad[i] ? 1 : 0);
                        var env = c.Env.Length == 0 ? "" : " (" + string.Join(" ", c.Env.Select(e => $"{e.K}={e.V}")) + ")";
                        Console.WriteLine($"  {(ok ? "ĐẠT" : cfgTreo[i] ? "TREO" : "SAI")}  [{cfgDone}/{Configs.Length}] {c.Name}{env} · seed {seed0 + i} · {cfgChecks[i]} phép kiểm · SAI {(cfgTreo[i] ? "?(TREO — khúc quá giờ, đã tự tắt)" : cfgBad[i] ? "?(dừng giữa chừng)" : cfgSai[i].ToString())} · CPU {cfgCpu[i]:0} giây · xong lúc {sw.Elapsed.TotalSeconds:0} giây"
                                          + (ok ? "" : " → xem " + string.Join(" ; ", cfgFiles[i])));
                    }
                }
            }
            finally { sem.Release(); }
        })).ToList();

        var all = Task.WhenAll(tasks);
        while (!all.IsCompleted)
        {
            await Task.WhenAny(all, Task.Delay(30000));
            if (!all.IsCompleted)
                lock (lockObj) Console.WriteLine($"  … đang chạy · xong {cfgDone}/{Configs.Length} lượt · {sw.Elapsed.TotalSeconds:0} giây");
        }

        Console.WriteLine();
        // [KIEM-NHAN] đo để biết chậm vì đâu (không đoán): CPU / thực mỗi tiến trình ≈ 1 = được chạy đủ; thấp = máy đang bận việc khác
        var wall = sw.Elapsed.TotalSeconds;
        var ratio = wallSum > 0 ? cpuSum / wallSum : 0;
        Console.WriteLine($"ĐO MÁY: tổng CPU {cpuSum / 60:0.0} phút · thực {wall / 60:0.0} phút · dùng trung bình {cpuSum / Math.Max(1, wall):0.0} / {Environment.ProcessorCount} nhân · mỗi tiến trình được CPU {ratio:P0} thời gian"
                          + (ratio < 0.75 ? " → MÁY ĐANG BẬN VIỆC KHÁC (tiến trình phải chờ CPU), thời gian lần này không so được" : ""));
        Console.WriteLine($"TỔNG: {Configs.Length} lượt × {po} PO × {ops} thao tác + {scens.Length} kịch bản · {wall / 60:0.0} phút");
        Console.WriteLine(totalSai == 0 ? "SAI: 0" : $"SAI: {totalSai} — gửi lại các file ghi 'SAI' ở trên");
        return totalSai == 0 ? 0 : 1;
    }

    static readonly object TreoLock = new();   // [KIEM-TREO]
    static async Task<(int Code, string File, double Sec, double Cpu)> RunChild(string dll, string args, (string K, string V)[] env, string outFile)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var k in new[] { "LOI", "EXCL", "NGREP", "PHOI", "DUMP", "WHYDBG", "CHUOI", "WTSEDIT", "VU", "TRACEPO" }) psi.Environment.Remove(k);   // không lẫn biến của cửa sổ cmd
        foreach (var (k, v) in env) psi.Environment[k] = v;
        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        // [KIEM-TREO 2026-11-01] mỗi lượt / kịch bản quá TREO_PHUT phút (mặc định 20) → tự tắt cả cây tiến trình, ghi TREO + seed
        //   vào file kết quả + tools\claude_trangthai.txt (chat đọc trạng thái, không chờ quá thời gian này).
        var treoPhut = double.TryParse(Environment.GetEnvironmentVariable("TREO_PHUT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tp) && tp > 0 ? tp : 20;
        bool treo = false;
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(treoPhut)))
        {
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                treo = true;
                try { proc.Kill(entireProcessTree: true); } catch { }
                await proc.WaitForExitAsync();
            }
        }
        var text = await outTask + await errTask;
        var envTxt = string.Join(" ", env.Select(e => $"{e.K}={e.V}"));
        if (treo)
        {
            text += $"\nTREO: quá {treoPhut} phút — tự tắt ({DateTime.Now:HH:mm:ss}). Chạy lại riêng: dotnet bin2\\GiaoNhanFuzz.dll {args} (biến: {envTxt})\n";
            try
            {
                var st = Path.Combine(Environment.CurrentDirectory, "..", "claude_trangthai.txt");
                lock (TreoLock) File.AppendAllText(st, $"TREO {DateTime.Now:dd/MM HH:mm:ss} · lệnh {args} {envTxt} · quá {treoPhut} phút, đã tự tắt\r\n", Encoding.UTF8);
            }
            catch { }
        }
        await File.WriteAllTextAsync(outFile, $"lệnh: GiaoNhanFuzz.dll {args} {envTxt}\n" + text, Encoding.UTF8);
        double cpu = 0;   // [KIEM-NHAN] tiến trình con tự in "CPU tiến trình: x" lúc thoát (đã thoát thì không đọc TotalProcessorTime được)
        var cpuLine = text.Split('\n').LastOrDefault(l => l.StartsWith("CPU tiến trình:"));
        if (cpuLine != null) double.TryParse(cpuLine.Substring(15).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cpu);
        return (treo ? -999 : proc.ExitCode, outFile, sw.Elapsed.TotalSeconds, cpu);
    }
}
