// [G32-SAOLUU 2026-11-22] harness: SaoLuuSql giả (không có SQL Server) — mk.sh bỏ Services/SaoLuuSql.cs thật, chép file này.
//   BACKUP giả = ghi 1 file .bak (nội dung tất định theo tên + số dòng) vào đúng đường dẫn SQL (harness: máy SQL = máy app).
//   /harness/saoluu?fail=1 → lần BACKUP sau ném lỗi (thử bản LỖI + băng đỏ); ?fail=0 bỏ.
using System.Text;
namespace MES.Web.Services;

public class SaoLuuSql : ISaoLuuSql
{
    public static int FailNext;
    public static int SoLanBackup;
    private static readonly SemaphoreSlim Lock = new(1, 1);
    public SaoLuuSql(string cs) { }
    public Task<string> TenDbAsync(CancellationToken ct) => Task.FromResult("MES_HARNESS");
    public Task<string> PhienBanSqlAsync(CancellationToken ct) => Task.FromResult("harness · fake");
    public Task<SortedDictionary<string, long>> DemDongAsync(CancellationToken ct) =>
        Task.FromResult(new SortedDictionary<string, long>(StringComparer.Ordinal) { ["dbo.Users"] = 9, ["dbo.WtsProductionLogs"] = 123 });
    public Task BackupAsync(string db, string path, string nhan, CancellationToken ct)
    {
        if (FailNext > 0) { FailNext--; throw new InvalidOperationException("BACKUP giả lỗi (harness)"); }
        SoLanBackup++;
        var sb = new StringBuilder();
        for (int i = 0; i < 20000; i++) sb.Append(nhan).Append(' ').Append(i).Append('\n');
        File.WriteAllText(path, sb.ToString());
        return Task.CompletedTask;
    }
    public async Task<IAsyncDisposable?> ThuKhoaAsync(CancellationToken ct)
    {
        if (!await Lock.WaitAsync(0, ct)) return null;
        return new Rel();
    }
    private sealed class Rel : IAsyncDisposable { public ValueTask DisposeAsync() { Lock.Release(); return ValueTask.CompletedTask; } }
}
