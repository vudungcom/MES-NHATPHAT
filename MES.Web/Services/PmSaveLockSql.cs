using System.Data;
using MES.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

// ================================================================
// [S8D-KHOA 2026-11-07] Khoá lượt lưu Part Master "MES_PM_SAVE_<PartId>" giữ trên 1 KẾT NỐI RIÊNG, KHÔNG QUA POOL (Pooling = false).
//   Nguồn: CLAUDE (rà độc lập gói 2, sổ dòng S8d); Hùng 07/11 07:55 giao Gói 5 sửa.
//   Program.cs gắn PartStepGuard.SessionLockOpener = PmSaveLockSql.OpenAsync. Lý do + cách cũ: xem PartStepGuard [S8D-KHOA].
//   - Lấy: sp_getapplock Exclusive, LockOwner = Session, chờ tối đa 10 giây (như cũ). Hết giờ → null (trang báo "đang có người khác lưu").
//   - Nhả: sp_releaseapplock trên chính kết nối đó rồi đóng kết nối. Kết nối không pool → đóng = phiên SQL kết thúc → khoá chắc chắn nhả,
//     kể cả khi lệnh nhả hỏng; tiến trình app tắt ngang thì TCP đóng, SQL Server tự kết thúc phiên.
//   - Không đụng DbContext của trang (DbContext có bị huỷ giữa lượt lưu vẫn nhả được).
// ================================================================
public static class PmSaveLockSql
{
    public static async Task<IAsyncDisposable?> OpenAsync(AppDbContext db, string name)
    {
        var cs = db.Database.GetConnectionString() ?? throw new InvalidOperationException("Không đọc được chuỗi kết nối để lấy khoá lượt lưu Part Master.");
        var b = new SqlConnectionStringBuilder(cs) { Pooling = false, ApplicationName = "MES khoa luu Part Master" };
        var conn = new SqlConnection(b.ConnectionString);
        try
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @n, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 10000; SELECT @r;";
            cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.NVarChar, 255) { Value = name });
            cmd.CommandTimeout = 30;
            var r = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (r < 0) { await conn.DisposeAsync(); return null; }   // -1 hết giờ / -2 huỷ / -3 deadlock: coi như người khác đang lưu (không lưu gì)
            return new Holder(conn, name);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private sealed class Holder : IAsyncDisposable
    {
        private readonly SqlConnection _conn; private readonly string _name; private bool _done;
        public Holder(SqlConnection conn, string name) { _conn = conn; _name = name; }
        public async ValueTask DisposeAsync()
        {
            if (_done) return;
            _done = true;
            try
            {
                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = "EXEC sp_releaseapplock @Resource = @n, @LockOwner = 'Session';";
                cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.NVarChar, 255) { Value = _name });
                cmd.CommandTimeout = 15;
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* kết nối hỏng: đóng kết nối không pool bên dưới = phiên kết thúc, SQL Server tự nhả khoá */ }
            finally { await _conn.DisposeAsync(); }
        }
    }
}
