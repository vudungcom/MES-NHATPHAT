// [ML17-XN][CA-NGHI-HIST 2026-11-01] BackfillNghi — nối SQL Server THẬT.
//   Chuỗi kết nối: --cs "<chuỗi>"  hoặc  biến môi trường MES_CS  hoặc  ../../MES.Web/appsettings.json (như QuetSo).
//   Mặc định CHỈ ĐỌC: đọc trong 1 giao dịch rồi ROLLBACK. Ghi thật: thêm --ghi, rồi gõ đúng tên DB khi được hỏi;
//   toàn bộ ghi trong 1 giao dịch (lỗi giữa chừng = không ghi gì). Kết quả: màn hình + ketqua_backfill_<DB>_<giờ>.txt
using System.Text;
using System.Text.Json;
using MES.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BackfillNghi;

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
        string cs = ""; bool ghi = false; string? xacNhan = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cs" && i + 1 < args.Length) cs = args[++i];
            else if (args[i] == "--ghi") ghi = true;
            else if (args[i] == "--xacnhan" && i + 1 < args.Length) xacNhan = args[++i];   // gõ tên DB sẵn (chạy từ .bat)
        }
        if (cs.Length == 0) cs = Environment.GetEnvironmentVariable("MES_CS") ?? ReadAppSettings() ?? "";
        if (cs.Length == 0) { Console.WriteLine("Không tìm thấy chuỗi kết nối. Chạy trong thư mục tools/BackfillNghi, hoặc thêm --cs \"Server=...;Database=MES;...\""); return 2; }
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
            await db.Database.OpenConnectionAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            var dbName = await db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").SingleAsync();
            Console.WriteLine($"DB: {dbName} — {(ghi ? "CHẾ ĐỘ GHI" : "chỉ đọc")}");
            // khoá ứng dụng: 2 lần chạy cùng lúc không ghi trùng (giữ tới hết giao dịch)
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'MES_BACKFILL_NGHI', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @r < 0 THROW 51300, N'Đang có BackfillNghi khác chạy', 1;");
            var plan = await BackfillCore.BuildAsync(db);
            bool wrote = false;
            if (ghi && plan.Items.Count > 0)
            {
                BackfillCore.Write(plan, Console.Out, dbName, false);
                Console.WriteLine();
                if (xacNhan == null) { Console.Write($"Gõ đúng tên DB ({dbName}) để GHI, Enter để thôi: "); xacNhan = Console.ReadLine(); }
                if (!string.Equals((xacNhan ?? "").Trim(), dbName, StringComparison.Ordinal))
                { Console.WriteLine("Không khớp tên DB — KHÔNG ghi."); await tx.RollbackAsync(); return 4; }
                var n = await BackfillCore.WriteAsync(db, plan);
                await tx.CommitAsync(); wrote = true;
                Console.WriteLine($"ĐÃ GHI {n} dòng nghỉ.");
            }
            else await tx.RollbackAsync();
            var file = $"ketqua_backfill_{dbName}_{DateTime.Now:MMddHHmmss}.txt";
            await using (var fw = new StreamWriter(file, false, new UTF8Encoding(true)))
                BackfillCore.Write(plan, fw, dbName, wrote);
            if (!ghi) BackfillCore.Write(plan, Console.Out, dbName, false);
            Console.WriteLine("Đã ghi kết quả: " + Path.GetFullPath(file));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("LỖI CHẠY (không ghi gì): " + ex);
            return 3;
        }
    }
}
