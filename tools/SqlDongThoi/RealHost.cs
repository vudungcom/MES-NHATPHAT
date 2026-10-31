// [T1 2026-10-31] Phần nối SQL Server THẬT của SqlDongThoi (EF Core SqlServer qua MES.Web).
//   Chuỗi kết nối: tham số --cs "<chuỗi>"  hoặc  biến môi trường MES_TEST_CS  hoặc  ../../MES.Web/appsettings.json (ConnectionStrings:DefaultConnection).
//   Dù lấy từ đâu, Core luôn kiểm SELECT DB_NAME() = MES_TEST trước khi ghi — trỏ nhầm MES là dừng ngay.
using System.Text;
using System.Text.Json;
using MES.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace SqlDongThoi;

public static class Host
{
    static string _cs = "";

    public static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_cs).Options);

    public static ValueTask ReleaseAsync(AppDbContext db) => db.DisposeAsync();

    public static async Task<string> DbNameAsync(AppDbContext db)
        => await db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").SingleAsync();

    /// <summary>[READ-DL] 1 = DB đang bật READ_COMMITTED_SNAPSHOT, 0 = tắt.</summary>
    public static async Task<int> RcsiAsync(AppDbContext db)
        => await db.Database.SqlQueryRaw<int>("SELECT CAST(is_read_committed_snapshot_on AS int) AS [Value] FROM sys.databases WHERE name = DB_NAME()").SingleAsync();

    /// <summary>[READ-DL] Mức cô lập của phiên SQL mà câu đọc kế tiếp đang dùng (1..5, xem Core.IsoSeen).</summary>
    public static async Task<int> IsoLevelAsync(AppDbContext db)
        => await db.Database.SqlQueryRaw<int>("SELECT CAST(transaction_isolation_level AS int) AS [Value] FROM sys.dm_exec_sessions WHERE session_id = @@SPID").SingleAsync();

    static string? ReadAppSettings()
    {
        foreach (var path in new[] { "../../MES.Web/appsettings.json", "../MES.Web/appsettings.json", "MES.Web/appsettings.json" })
        {
            if (!File.Exists(path)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var c) && c.TryGetProperty("DefaultConnection", out var d))
            {
                Console.WriteLine("Chuỗi kết nối lấy từ " + Path.GetFullPath(path));
                return d.GetString();
            }
        }
        return null;
    }

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cs" && i + 1 < args.Length) _cs = args[++i];
            else rest.Add(args[i]);
        }
        if (_cs.Length == 0) _cs = Environment.GetEnvironmentVariable("MES_TEST_CS") ?? ReadAppSettings() ?? "";
        if (_cs.Length == 0) { Console.WriteLine("Không tìm thấy chuỗi kết nối. Chạy trong thư mục tools/SqlDongThoi của bản MES_TEST_APP, hoặc thêm --cs \"...Database=MES_TEST...\""); return 2; }
        if (rest.Count > 0) Core.Workers  = int.Parse(rest[0]);
        if (rest.Count > 1) Core.Seconds  = int.Parse(rest[1]);
        if (rest.Count > 2) Core.PoCount  = int.Parse(rest[2]);
        if (rest.Count > 3) Core.Phoi     = int.Parse(rest[3]);
        try { return await Core.MainAsync(); }
        catch (Exception ex)
        {
            Console.WriteLine("LỖI CHẠY: " + ex);
            return 3;
        }
    }
}
