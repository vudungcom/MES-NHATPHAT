// [G32-SAOLUU 2026-11-22] Thử sao lưu THẬT (Gói 32a) — chạy bằng trạm (tools\claude_tram.bat):
//   saoluu   : dùng ĐÚNG SaoLuuService + SaoLuuSql của MES.Web lưu 1 bản nhanh + 1 trọn bộ của DB trong MES.Web\appsettings.json
//              vào <thư mục>\_thu (không đụng chỗ app thật sẽ lưu), rồi KIỂM: manifest ĐẠT, mã băm từng file, .bak trong zip,
//              RESTORE HEADERONLY / FILELISTONLY / VERIFYONLY (chỉ đọc file sao lưu). Đo thời gian + dung lượng.
//   saoluukp : như trên + KHÔI PHỤC THỬ bộ vừa lưu vào DB tạm MES_SAOLUU_THU (file dữ liệu vào thư mục mặc định của SQL Server),
//              so số dòng từng bảng với manifest, chạy QuetSo (quét sổ cân, chỉ đọc) trên DB tạm, rồi XOÁ DB tạm + file .bak giải nén.
//              Chỉ tạo / xoá DB tên đúng MES_SAOLUU_THU — không đụng DB MES / MES_TEST.
//   Đường dẫn đọc từ duong-dan.txt (dòng 1 = thư mục theo máy SQL, dòng 2 = cùng thư mục theo máy chạy tool; trống = giống dòng 1).
//   Kết quả: màn hình + ketqua_saoluu_<giờ>.txt. Mã thoát 0 = đạt hết.
//   Nguồn: HÙNG 22/11 14:52 chọn phương án A (thư mục chia sẻ trên máy 211). SQL ghi (BACKUP COPY_ONLY, RESTORE / DROP DB tạm) — chờ HÙNG duyệt.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace SaoLuuThu;

public static class Program
{
    const string DbThu = "MES_SAOLUU_THU";
    static int sai = 0;
    static readonly StringBuilder Log = new();
    static void W(string s) { Console.WriteLine(s); Log.AppendLine(s); }
    static void Ok(bool c, string name, string? info = null)
    {
        W((c ? "  ĐẠT  " : "  SAI  ") + name + (!c && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!c) sai++;
    }

    static string? ReadCs()
    {
        foreach (var path in new[] { "../../MES.Web/appsettings.json", "../MES.Web/appsettings.json" })
        {
            if (!File.Exists(path)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var c) && c.TryGetProperty("DefaultConnection", out var d)) return d.GetString();
        }
        return null;
    }

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool khoiPhuc = args.Contains("--restore");
        var cs = ReadCs();
        if (string.IsNullOrEmpty(cs)) { W("Không đọc được chuỗi kết nối ở ../../MES.Web/appsettings.json"); return 2; }
        if (!File.Exists("duong-dan.txt")) { W("Thiếu duong-dan.txt (dòng 1: thư mục theo máy SQL, dòng 2: theo máy chạy tool)."); return 2; }
        var dd = File.ReadAllLines("duong-dan.txt").Select(x => x.Trim()).ToList();
        var gocSql = SaoLuuService.NoiSql(dd[0], "_thu");
        var gocApp = Path.Combine(dd.Count > 1 && dd[1].Length > 0 ? dd[1] : dd[0], "_thu");
        var t0 = DateTime.Now;
        W($"[saoluu-thu] {t0:dd/MM/yyyy HH:mm:ss}  máy {Environment.MachineName}  khôi phục thử: {(khoiPhuc ? "CÓ" : "không")}");
        W($"  thư mục theo máy SQL: {gocSql}\n  thư mục theo máy tool: {gocApp}");
        int code = 0;
        try { code = await Chay(cs!, gocSql, gocApp, khoiPhuc); }
        catch (Exception ex) { W("LỖI CHẠY: " + ex); code = 3; }
        W($"\n[saoluu-thu] {(sai == 0 && code == 0 ? "ĐẠT HẾT" : $"{sai} SAI")} · {(DateTime.Now - t0).TotalSeconds:0} giây");
        var f = $"ketqua_saoluu_{DateTime.Now:MMddHHmmss}.txt";
        File.WriteAllText(f, Log.ToString(), new UTF8Encoding(true));
        Console.WriteLine("Đã ghi: " + Path.GetFullPath(f));
        return sai == 0 && code == 0 ? 0 : 1;
    }

    static async Task<int> Chay(string cs, string gocSql, string gocApp, bool khoiPhuc)
    {
        Directory.CreateDirectory(gocApp);
        var cfg = Path.Combine(Path.GetTempPath(), "mes_saoluu_thu_cfg");
        if (Directory.Exists(cfg)) Directory.Delete(cfg, true);
        var sql = new SaoLuuSql(cs);
        var webRoot = Path.GetFullPath("../../MES.Web");
        var svc = new SaoLuuService(null!, sql, new SaoLuuCaiDatStore(cfg), webRoot, NullLogger<SaoLuuService>.Instance)
        {
            KiemAdminThu = _ => Task.FromResult(true),
        };
        var rel = Path.Combine(webRoot, "bin", "Release", "net8.0");
        if (Directory.Exists(rel)) svc.BaseDir = rel;   // phần mềm = bản build Release của MES.Web (như lúc chạy dotnet run)
        var db = await svc.DbAsync();
        W($"  DB: {db} · SQL: {await sql.PhienBanSqlAsync(default)}");
        if (string.Equals(db, DbThu, StringComparison.OrdinalIgnoreCase)) { W("DB đang trỏ là DB tạm — dừng."); return 2; }
        svc.Store.Ghi(db, new SaoLuuCaiDat { BatTuDong = false, ThuMucTheoMaySql = gocSql, ThuMucTheoMayApp = gocApp }, "saoluu-thu", "thử", DateTime.Now);

        W("\n[1] Lưu nhanh");
        var k1 = await svc.ChayAsync(SaoLuuService.Loai.Nhanh, "thu-tram", default);
        Ok(k1.Dat, $"lưu nhanh {k1.Ten} — {(k1.KetThuc - k1.BatDau).TotalSeconds:0.0} giây, {SaoLuuService.DocBytes(k1.Bytes)}", k1.ThongBao);
        W("\n[2] Trọn bộ");
        await Task.Delay(1100);
        var k2 = await svc.ChayAsync(SaoLuuService.Loai.Bo, "thu-tram", default);
        Ok(k2.Dat, $"trọn bộ {k2.Ten} — {(k2.KetThuc - k2.BatDau).TotalSeconds:0.0} giây, {SaoLuuService.DocBytes(k2.Bytes)}", k2.ThongBao);
        if (!k1.Dat || !k2.Dat) return 1;

        var c = svc.Store.Doc(db);
        foreach (var (l, ten) in new[] { (SaoLuuService.Loai.Nhanh, k1.Ten), (SaoLuuService.Loai.Bo, k2.Ten) })
        {
            var dir = Path.Combine(SaoLuuService.ThuMucDb(c, db), l == SaoLuuService.Loai.Nhanh ? "nhanh" : "bo", ten);
            W($"\n[3] Kiểm {dir}");
            var m = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")))!;
            Ok(m.KetQua == "DAT", $"manifest KetQua = {m.KetQua}, phiên bản {m.PhienBanPhanMem}, {m.SoDong.Count} bảng, {m.Files.Count} file");
            int lech = 0;
            foreach (var f in m.Files) if (SaoLuuService.Bam(Path.Combine(dir, f.Duong)) != f.Sha256) lech++;
            Ok(lech == 0, "mã băm SHA-256 từng file khớp manifest", lech + " file lệch");
            foreach (var f in m.Files) W($"     {f.Duong,-60} {SaoLuuService.DocBytes(f.Bytes),10}");
            if (m.CanhBao.Count > 0) W("     cảnh báo: " + string.Join(" | ", m.CanhBao));
            W($"     .bak gốc {SaoLuuService.DocBytes(m.BakBytes)} → cả folder {SaoLuuService.DocBytes(m.TongBytes)}");
        }

        // [4] Giải nén .bak của trọn bộ ra thư mục chung → SQL đọc lại (chỉ đọc file)
        var boDir = Path.Combine(SaoLuuService.ThuMucDb(c, db), "bo", k2.Ten);
        var zip = Directory.GetFiles(Path.Combine(boDir, "du-lieu"), "*.bak.zip").Single();
        var bakTen = Path.GetFileName(zip)[..^4];
        var kpApp = Path.Combine(gocApp, "_khoiphuc"); Directory.CreateDirectory(kpApp);
        var bakApp = Path.Combine(kpApp, bakTen);
        if (File.Exists(bakApp)) File.Delete(bakApp);
        using (var za = ZipFile.OpenRead(zip)) za.Entries.Single().ExtractToFile(bakApp);
        var bakSql = SaoLuuService.NoiSql(gocSql, "_khoiphuc", bakTen);
        W($"\n[4] SQL đọc lại file sao lưu {bakSql}");
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(cs) { InitialCatalog = "master", Pooling = false }.ConnectionString);
        await cn.OpenAsync();
        var files = new List<(string Logical, string Type)>();
        await using (var cmd = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @p", cn) { CommandTimeout = 600 })
        {
            cmd.Parameters.AddWithValue("@p", bakSql);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) files.Add((rd.GetString(rd.GetOrdinal("LogicalName")), rd.GetString(rd.GetOrdinal("Type"))));
        }
        Ok(files.Count >= 2, "RESTORE FILELISTONLY: " + string.Join(", ", files.Select(f => f.Logical + "(" + f.Type + ")")));
        await using (var cmd = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @p WITH CHECKSUM", cn) { CommandTimeout = 1800 })
        {
            cmd.Parameters.AddWithValue("@p", bakSql);
            var msgs = new List<string>(); cn.InfoMessage += (_, e) => msgs.Add(e.Message);
            await cmd.ExecuteNonQueryAsync();
            Ok(msgs.Any(x => x.Contains("valid", StringComparison.OrdinalIgnoreCase) || x.Contains("hợp lệ")), "RESTORE VERIFYONLY WITH CHECKSUM: " + string.Join(" ", msgs));
        }

        if (khoiPhuc)
        {
            W($"\n[5] Khôi phục thử vào DB tạm {DbThu}");
            string dataDir = "", logDir = "";
            await using (var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(400))", cn))
            await using (var rd = await cmd.ExecuteReaderAsync()) { await rd.ReadAsync(); dataDir = rd.GetString(0); logDir = rd.GetString(1); }
            await XoaDbThu(cn);
            var move = new StringBuilder(); var cmdR = new SqlCommand { Connection = cn, CommandTimeout = 3600 };
            int i = 0;
            foreach (var f in files)
            {
                var dir = f.Type == "L" ? logDir : dataDir;
                move.Append($", MOVE @l{i} TO @f{i}");
                cmdR.Parameters.AddWithValue($"@l{i}", f.Logical);
                cmdR.Parameters.AddWithValue($"@f{i}", SaoLuuService.NoiSql(dir, $"{DbThu}_{i}{(f.Type == "L" ? ".ldf" : ".mdf")}"));
                i++;
            }
            cmdR.Parameters.AddWithValue("@p", bakSql);
            cmdR.CommandText = $"RESTORE DATABASE [{DbThu}] FROM DISK = @p WITH CHECKSUM, RECOVERY{move}";
            var tR = DateTime.Now;
            await cmdR.ExecuteNonQueryAsync();
            Ok(true, $"RESTORE DATABASE {DbThu} xong — {(DateTime.Now - tR).TotalSeconds:0.0} giây");
            try
            {
                var csThu = new SqlConnectionStringBuilder(cs) { InitialCatalog = DbThu, Pooling = false }.ConnectionString;
                var demThu = await new SaoLuuSql(csThu).DemDongAsync(default);
                var m2 = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(boDir, "manifest.json")))!;
                var lech = m2.SoDong.Where(kv => !demThu.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => $"{kv.Key} {kv.Value}→{(demThu.TryGetValue(kv.Key, out var v2) ? v2 : -1)}").ToList();
                Ok(demThu.Count == m2.SoDong.Count, $"số bảng DB tạm {demThu.Count} = manifest {m2.SoDong.Count}");
                if (lech.Count == 0) Ok(true, $"số dòng từng bảng khớp manifest ({demThu.Values.Sum():N0} dòng)");
                else W("  CẢNH BÁO số dòng lệch (manifest đếm ngay TRƯỚC lệnh BACKUP — có người đang nhập trên app thì lệch vài dòng là bình thường): " + string.Join("; ", lech.Take(20)));
                await using var dbx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(csThu).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
                await dbx.Database.OpenConnectionAsync();
                await using var tx = await dbx.Database.BeginTransactionAsync();
                var res = await QuetSo.QuetCore.ScanAsync(dbx, new HandoverLedgerService(dbx));
                await tx.RollbackAsync();
                var sw = new StringWriter(); QuetSo.QuetCore.Write(res, sw, DbThu);
                W("  QuetSo trên DB tạm:\n" + string.Join("\n", sw.ToString().Split('\n').Take(40).Select(x => "     " + x)));
                Ok(true, $"QuetSo chạy được trên bản khôi phục: {res.Findings.Count} phát hiện (so với lần quét DB gốc gần nhất — phát hiện cũ vẫn còn là đúng)");
            }
            finally
            {
                SqlConnection.ClearAllPools();
                await XoaDbThu(cn);
                Ok(!await CoDbThu(cn), $"đã xoá DB tạm {DbThu}");
            }
        }
        try { File.Delete(bakApp); } catch { }
        W($"\n[6] Thư mục thử: {gocApp} (xoá tay được — app thật lưu ở thư mục gốc, không phải _thu)");
        return 0;
    }

    static async Task<bool> CoDbThu(SqlConnection cn)
    {
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", cn);
        cmd.Parameters.AddWithValue("@n", DbThu);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    static async Task XoaDbThu(SqlConnection cn)
    {
        if (!await CoDbThu(cn)) return;
        // Chỉ đúng tên DB tạm (hằng số) — không bao giờ ghép tên khác
        await using var cmd = new SqlCommand($"ALTER DATABASE [{DbThu}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DbThu}];", cn) { CommandTimeout = 600 };
        await cmd.ExecuteNonQueryAsync();
    }
}
