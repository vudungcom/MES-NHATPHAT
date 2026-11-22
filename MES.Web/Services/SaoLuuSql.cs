using Microsoft.Data.SqlClient;

namespace MES.Web.Services;

// =====================================================================
// [G32-SAOLUU 2026-11-22] Gói 32a — phần nói chuyện với SQL Server của sao lưu (interface ISaoLuuSql ở SaoLuuService.cs — kịch bản thử / harness thay bằng bản giả).
//   Chỉ có: đọc tên DB / phiên bản, đếm dòng từng bảng (sys.partitions — chỉ đọc), lệnh BACKUP, khoá chạy 1 nơi (sp_getapplock).
//   KHÔNG ghi / sửa dòng dữ liệu nào. BACKUP dùng COPY_ONLY: không đổi chuỗi sao lưu khác (nếu máy chủ đang có lịch sao lưu riêng)
//   + CHECKSUM: SQL kiểm checksum từng trang khi đọc, trang hỏng → lệnh BACKUP báo lỗi ngay.
// =====================================================================
public class SaoLuuSql : ISaoLuuSql
{
    private readonly string _cs;
    public SaoLuuSql(string connectionString) { _cs = connectionString; }

    private async Task<SqlConnection> MoAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(_cs);
        await cn.OpenAsync(ct);
        return cn;
    }

    public async Task<string> TenDbAsync(CancellationToken ct)
    {
        await using var cn = await MoAsync(ct);
        await using var cmd = new SqlCommand("SELECT DB_NAME()", cn);
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<string> PhienBanSqlAsync(CancellationToken ct)
    {
        await using var cn = await MoAsync(ct);
        await using var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) + N' · ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))", cn);
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<SortedDictionary<string, long>> DemDongAsync(CancellationToken ct)
    {
        var res = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var cn = await MoAsync(ct);
        const string sql = @"SELECT s.name + N'.' + t.name, SUM(p.rows)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) res[rd.GetString(0)] = rd.GetInt64(1);
        return res;
    }

    public async Task BackupAsync(string db, string duongDanTheoMaySql, string nhan, CancellationToken ct)
    {
        await using var cn = await MoAsync(ct);
        // BACKUP nhận biến cho tên DB và đường dẫn → không ghép chuỗi SQL.
        await using var cmd = new SqlCommand("BACKUP DATABASE @db TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT, FORMAT, NAME = @name", cn)
        {
            CommandTimeout = 3600
        };
        cmd.Parameters.AddWithValue("@db", db);
        cmd.Parameters.AddWithValue("@path", duongDanTheoMaySql);
        cmd.Parameters.AddWithValue("@name", nhan.Length > 120 ? nhan[..120] : nhan);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IAsyncDisposable?> ThuKhoaAsync(CancellationToken ct)
    {
        // Kết nối riêng KHÔNG pool: khoá Session gắn với kết nối này, đóng là nhả hẳn (không trả về pool còn giữ khoá).
        var cn = new SqlConnection(new SqlConnectionStringBuilder(_cs) { Pooling = false }.ConnectionString);
        await cn.OpenAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(@"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = N'MES_SAO_LUU', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;
SELECT @r;", cn);
            var r = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (r < 0) { await cn.DisposeAsync(); return null; }
            return new Khoa(cn);   // đóng kết nối = nhả khoá
        }
        catch
        {
            await cn.DisposeAsync();
            throw;
        }
    }

    private sealed class Khoa : IAsyncDisposable
    {
        private readonly SqlConnection _cn;
        public Khoa(SqlConnection cn) { _cn = cn; }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new SqlCommand("EXEC sp_releaseapplock @Resource = N'MES_SAO_LUU', @LockOwner = N'Session'", _cn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* đóng kết nối bên dưới cũng nhả khoá */ }
            await _cn.DisposeAsync();
        }
    }
}
