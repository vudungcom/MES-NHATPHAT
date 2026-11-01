// [RA-SOCHUNG 2026-10-31] Phần nối SQL Server THẬT của QuetSo — CHỈ ĐỌC.
//   Chuỗi kết nối: --cs "<chuỗi>"  hoặc  biến môi trường MES_CS  hoặc  ../../MES.Web/appsettings.json (ConnectionStrings:DefaultConnection).
//   An toàn: mọi câu đọc chạy trong 1 giao dịch và luôn ROLLBACK ở cuối; không gọi SaveChanges → không ghi gì vào DB.
//   Chạy được trên MES_TEST và MES (in tên DB ở đầu để chắc đang quét đúng DB).
using System.Text;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace QuetSo;

public static class Host
{
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
        string cs = "";
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--cs" && i + 1 < args.Length) cs = args[++i];
        if (cs.Length == 0) cs = Environment.GetEnvironmentVariable("MES_CS") ?? ReadAppSettings() ?? "";
        if (cs.Length == 0) { Console.WriteLine("Không tìm thấy chuỗi kết nối. Chạy trong thư mục tools/QuetSo, hoặc thêm --cs \"Server=...;Database=MES_TEST;...\""); return 2; }
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
            await db.Database.OpenConnectionAsync();
            await using var tx = await db.Database.BeginTransactionAsync();   // chỉ đọc: luôn Rollback bên dưới
            var dbName = await db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").SingleAsync();
            Console.WriteLine($"Đang quét DB: {dbName} (chỉ đọc)");
            var led = new HandoverLedgerService(db);
            var res = await QuetCore.ScanAsync(db, led);
            await tx.RollbackAsync();
            var file = $"ketqua_quet_{dbName}_{DateTime.Now:MMddHHmmss}.txt";
            await using (var fw = new StreamWriter(file, false, new UTF8Encoding(true)))
                QuetCore.Write(res, fw, dbName);
            QuetCore.Write(res, Console.Out, dbName);
            Console.WriteLine();
            Console.WriteLine("Đã ghi: " + Path.GetFullPath(file));
            return res.Findings.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("LỖI CHẠY: " + ex);
            return 3;
        }
    }
}
