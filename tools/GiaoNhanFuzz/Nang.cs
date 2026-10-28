// [GN-NG-TRACE 2026-10-28] Lệnh "nang": kiểm nặng — chạy 2 kịch bản cố định (kh, loi) rồi nhiều lượt ngẫu nhiên
// với các cấu hình khác nhau, SONG SONG theo số nhân máy. Mỗi lượt là một tiến trình riêng (chính tool này),
// kết quả từng lượt ghi vào thư mục nang_ketqua\ để gửi lại khi có SAI.
//
//   dotnet bin2\GiaoNhanFuzz.dll nang [số PO mỗi lượt = 60] [thao tác mỗi PO = 120] [seed đầu = 1]
//   set SONG_SONG=2   → số lượt chạy cùng lúc (mặc định: số nhân máy − 1)
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
        ("thuong",      "như lệnh thường (không đặt biến)",            Array.Empty<(string, string)>()),
        ("loaitru",     "lệnh thường + 70% PO có máy loại trừ",        new[] { ("EXCL", "0.7") }),
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

        Console.WriteLine($"KIỂM NẶNG: kịch bản kh + loi, rồi {Configs.Length} lượt ngẫu nhiên × {po} PO × {ops} thao tác · chạy cùng lúc {par} lượt");
        Console.WriteLine($"Kết quả từng lượt: {dir}");
        var sw = Stopwatch.StartNew();
        var results = new List<(string Name, int Seed, int Sai, string Checks, double Sec, string File)>();
        int totalSai = 0;

        // 1. Kịch bản cố định (chạy trước, tuần tự)
        foreach (var sc in new[] { "kh", "loi" })
        {
            var (code, outFile, _) = await RunChild(dll, sc, Array.Empty<(string, string)>(), Path.Combine(dir, $"kichban_{sc}.txt"));
            var txt = File.ReadAllText(outFile);
            var dat = txt.Contains("KẾT QUẢ: ĐẠT hết");
            Console.WriteLine($"  {(dat ? "ĐẠT" : "SAI")}  kịch bản {sc}" + (dat ? "" : $" → xem {outFile}"));
            if (!dat) totalSai++;
        }

        // 2. Lượt ngẫu nhiên, song song
        var sem = new SemaphoreSlim(par);
        var lockObj = new object();
        int done = 0;
        var tasks = Configs.Select((c, i) => Task.Run(async () =>
        {
            await sem.WaitAsync();
            try
            {
                var seed = seed0 + i;
                var file = Path.Combine(dir, $"{c.Name}_seed{seed}.txt");
                var (code, outFile, sec) = await RunChild(dll, $"{po} {ops} {seed}", c.Env, file);
                var lines = File.ReadAllLines(outFile);
                var saiLine = lines.LastOrDefault(l => l.StartsWith("SAI:"));
                var sai = saiLine != null && int.TryParse(saiLine.Substring(4).Trim(), out var n) ? n : -1;   // -1 = không chạy xong
                var checks = lines.LastOrDefault(l => l.StartsWith("Phép kiểm:"))?.Substring(10).Trim() ?? "?";
                lock (lockObj)
                {
                    done++;
                    results.Add((c.Name, seed, sai, checks, sec, outFile));
                    if (sai != 0) totalSai += sai < 0 ? 1 : sai;
                    var env = c.Env.Length == 0 ? "" : " (" + string.Join(" ", c.Env.Select(e => $"{e.K}={e.V}")) + ")";
                    Console.WriteLine($"  {(sai == 0 ? "ĐẠT" : "SAI")}  [{done}/{Configs.Length}] {c.Name}{env} · seed {seed} · {checks} phép kiểm · SAI {(sai < 0 ? "?(dừng giữa chừng)" : sai.ToString())} · {sec:0} giây"
                                      + (sai == 0 ? "" : $" → xem {outFile}"));
                }
            }
            finally { sem.Release(); }
        })).ToList();

        // Báo còn chạy ~30 giây một lần
        while (!Task.WhenAll(tasks).IsCompleted)
        {
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(30000));
            if (!Task.WhenAll(tasks).IsCompleted)
                lock (lockObj) Console.WriteLine($"  … đang chạy · xong {done}/{Configs.Length} lượt · {sw.Elapsed.TotalSeconds:0} giây");
        }

        Console.WriteLine();
        Console.WriteLine($"TỔNG: {Configs.Length} lượt × {po} PO × {ops} thao tác + 2 kịch bản · {sw.Elapsed.TotalMinutes:0.0} phút");
        Console.WriteLine(totalSai == 0 ? "SAI: 0" : $"SAI: {totalSai} — gửi lại các file ghi 'SAI' ở trên");
        return totalSai == 0 ? 0 : 1;
    }

    static async Task<(int Code, string File, double Sec)> RunChild(string dll, string args, (string K, string V)[] env, string outFile)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var k in new[] { "LOI", "EXCL", "NGREP", "PHOI", "DUMP", "WHYDBG" }) psi.Environment.Remove(k);   // không lẫn biến của cửa sổ cmd
        foreach (var (k, v) in env) psi.Environment[k] = v;
        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var text = await outTask + await errTask;
        await File.WriteAllTextAsync(outFile, $"lệnh: GiaoNhanFuzz.dll {args} {string.Join(" ", env.Select(e => $"{e.K}={e.V}"))}\n" + text, Encoding.UTF8);
        return (proc.ExitCode, outFile, sw.Elapsed.TotalSeconds);
    }
}
