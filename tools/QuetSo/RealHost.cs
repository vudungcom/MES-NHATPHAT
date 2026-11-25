// [RA-SOCHUNG 2026-10-31] Phần nối SQL Server THẬT của QuetSo — CHỈ ĐỌC.
//   Chuỗi kết nối: --cs "<chuỗi>"  hoặc  biến môi trường MES_CS  hoặc  ../../MES.Web/appsettings.json (ConnectionStrings:DefaultConnection).
//   An toàn: mọi câu đọc chạy trong 1 giao dịch và luôn ROLLBACK ở cuối; không gọi SaveChanges → không ghi gì vào DB.
//   Chạy được trên MES_TEST và MES (in tên DB ở đầu để chắc đang quét đúng DB).
using System.Text;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
            await CauTrucN21Async(db);   // [QUET-N21-CK]
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

    // [QUET-N21-CK 2026-11-25] Gói G-D (luật N21): in ràng buộc CHECK của HandoverNgReports / NgCases (để biết giá trị nguồn mới có cần SQL không)
    //   + số dòng WTS có NG mà không có vụ (dòng rất cũ trước vụ lỗi). CHỈ ĐỌC, chạy trong giao dịch luôn Rollback như phần quét.
    //   Nguồn: CLAUDE 25/11 (bản em hiểu G-D, câu B); HÙNG 25/11 16:27 "ok tự làm đi".
    static async Task CauTrucN21Async(AppDbContext db)
    {
        var cn = db.Database.GetDbConnection();
        var tx = db.Database.CurrentTransaction?.GetDbTransaction();
        async Task Q(string title, string sql)
        {
            await using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            Console.WriteLine("[QUET-N21-CK] " + title);
            await using var rd = await cmd.ExecuteReaderAsync();
            int n = 0;
            while (await rd.ReadAsync())
            {
                var cols = new List<string>();
                for (int i = 0; i < rd.FieldCount; i++) cols.Add(rd.GetName(i) + "=" + (rd.IsDBNull(i) ? "NULL" : Convert.ToString(rd.GetValue(i))));
                Console.WriteLine("   " + string.Join(" | ", cols));
                n++;
            }
            if (n == 0) Console.WriteLine("   (không có dòng nào)");
        }
        try
        {
            await Q("Rang buoc CHECK", "SELECT OBJECT_NAME(parent_object_id) AS Bang, name AS Ten, definition AS DieuKien FROM sys.check_constraints "
                + "WHERE OBJECT_NAME(parent_object_id) IN (N'HandoverNgReports', N'NgCases')");
            await Q("Dong WTS co NG khong vu", "SELECT COUNT(*) AS SoDong, SUM(CASE WHEN NgReason IS NULL THEN 1 ELSE 0 END) AS KhongChuLyDo, MIN(StartTime) AS SomNhat, MAX(StartTime) AS MuonNhat "
                + "FROM dbo.WtsProductionLogs WHERE IsVoided = 0 AND QtyNg > 0 AND NgCaseId IS NULL");
            await Q("Cot nguon", "SELECT TABLE_NAME AS Bang, COLUMN_NAME AS Cot, CHARACTER_MAXIMUM_LENGTH AS DoDai FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE (TABLE_NAME = N'HandoverNgReports' AND COLUMN_NAME IN (N'SourceCell', N'Origin')) OR (TABLE_NAME = N'NgCases' AND COLUMN_NAME = N'OriginKind')");
        }
        catch (Exception ex) { Console.WriteLine("[QUET-N21-CK] LỖI ĐỌC: " + ex.Message); }
    }
}
