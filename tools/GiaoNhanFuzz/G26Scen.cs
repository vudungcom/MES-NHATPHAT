// [G26-SCEN 2026-11-20] Kịch bản `g26` — Gói 26 (chuẩn bị lên máy khách, không đổi số / luật xưởng):
//   G26-1…5 [SEED-DEMO] SeedData.EnsureSeeded trên DB giả trống / đã có dữ liệu, có / không MES_SEED_DEMO, MES_ADMIN_INIT_PW;
//   G26-6 [C-10-GRACE] ServerLicenseHelper.ApplyOfflineGrace 5 / 10 / 31 ngày (chỉ khoá giao diện, không đụng dữ liệu);
//   G26-7 [LOG-FILE] FileLoggerProvider ghi file theo ngày, từ Warning, xoá file quá 30 ngày.
//   Bản a57f112 (Gói 25): SeedData luôn seed 4 tài khoản + khách + Part mẫu; chưa có ApplyOfflineGrace / FileLoggerProvider → không biên dịch / SAI.
//   Nguồn: CLAUDE quét Fable 14–15/11 (C-07, C-10, QF-F log ra file); HÙNG 20/11 18:12 + 18:32 OK (B3–B6).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public static class G26Scen
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!cond) fails++;
    }

    static (AppDbContext Db, List<string> Warns) Seed(string? demo, string? pw, bool hasUser = false)
    {
        Environment.SetEnvironmentVariable(SeedData.DemoEnv, demo);
        Environment.SetEnvironmentVariable(SeedData.AdminPwEnv, pw);
        var db = new AppDbContext();
        if (hasUser)
        {
            var g = new UserGroup { GroupId = 50, GroupCode = "ADMIN", GroupName = "Quản trị" };
            db.UserGroups.List.Add(g);
            db.Users.List.Add(new User { UserId = 50, Username = "admin", FullName = "Admin thật", PasswordHash = PasswordHasher.Hash("matkhauthat"), Group = g, GroupId = 50, IsActive = true });
            db.Fixup();
        }
        var warns = new List<string>();
        SeedData.Warn = m => warns.Add(m);
        try { SeedData.EnsureSeeded(db); }
        finally { SeedData.Warn = null; Environment.SetEnvironmentVariable(SeedData.DemoEnv, null); Environment.SetEnvironmentVariable(SeedData.AdminPwEnv, null); }
        db.Fixup();
        return (db, warns);
    }

    public static async Task<int> Run()
    {
        await Task.CompletedTask;
        Console.WriteLine("[G26-1] SEED-DEMO — DB trống, KHÔNG đặt MES_SEED_DEMO: chỉ 7 nhóm + admin (admin123 + cảnh báo), không khách / Part / tài khoản mẫu");
        {
            var (db, w) = Seed(null, null);
            var users = db.Users.List.Select(u => u.Username).ToList();
            var adm = db.Users.List.FirstOrDefault(u => u.Username == "admin");
            Ok(db.UserGroups.List.Count == 7, "G26-1 7 nhóm quyền", db.UserGroups.List.Count.ToString());
            Ok(users.Count == 1 && adm != null && adm.IsActive && PasswordHasher.Verify("admin123", adm.PasswordHash), "G26-1 chỉ 1 tài khoản admin, mật khẩu admin123", string.Join(",", users));
            Ok(!users.Contains("kh01") && !users.Contains("vt01") && !users.Contains("kho01"), "G26-1 KHÔNG có kh01 / vt01 / kho01 (C-07)");
            Ok(db.Customers.List.Count == 0 && db.PartMasters.List.Count == 0 && db.PartMasterAttributes.List.Count == 0, "G26-1 không khách / Part mẫu", $"{db.Customers.List.Count}/{db.PartMasters.List.Count}");
            Ok(db.FileStorages.List.Count == 1, "G26-1 ổ lưu file mặc định vẫn tạo (cấu hình, không phải mẫu)");
            Ok(w.Count == 1 && w[0].Contains("admin123") && w[0].Contains(SeedData.AdminPwEnv), "G26-1 ghi 1 cảnh báo mật khẩu admin mặc định", string.Join(" | ", w));
        }
        Console.WriteLine("[G26-2] SEED-DEMO — MES_ADMIN_INIT_PW ≥ 6 ký tự: admin lấy mật khẩu đó, không cảnh báo; < 6 ký tự: về admin123 + cảnh báo");
        {
            var (db, w) = Seed(null, "  Kho@2026  ");
            var adm = db.Users.List.First(u => u.Username == "admin");
            Ok(PasswordHasher.Verify("Kho@2026", adm.PasswordHash) && !PasswordHasher.Verify("admin123", adm.PasswordHash) && w.Count == 0, "G26-2 mật khẩu admin = biến môi trường (đã Trim), không cảnh báo", string.Join(" | ", w));
            (db, w) = Seed(null, "abc");
            adm = db.Users.List.First(u => u.Username == "admin");
            Ok(PasswordHasher.Verify("admin123", adm.PasswordHash) && w.Count == 1, "G26-2 biến < 6 ký tự: dùng admin123 + cảnh báo");
        }
        Console.WriteLine("[G26-3] SEED-DEMO — MES_SEED_DEMO=1: như trước Gói 26 (4 tài khoản, 3 khách, 5 Part)");
        {
            var (db, _) = Seed("1", null);
            var users = db.Users.List.Select(u => u.Username).OrderBy(x => x).ToList();
            Ok(users.SequenceEqual(new[] { "admin", "kh01", "kho01", "vt01" }), "G26-3 4 tài khoản admin / kh01 / vt01 / kho01", string.Join(",", users));
            Ok(db.Customers.List.Count == 3 && db.PartMasters.List.Count == 5 && db.PartMasterAttributes.List.Count > 0, "G26-3 3 khách + 5 Part mẫu + thuộc tính", $"{db.Customers.List.Count}/{db.PartMasters.List.Count}");
            Ok(PasswordHasher.Verify("123456", db.Users.List.First(u => u.Username == "kh01").PasswordHash), "G26-3 tài khoản mẫu mật khẩu 123456 (chỉ khi bật mẫu)");
        }
        Console.WriteLine("[G26-4] SEED-DEMO — DB ĐÃ có tài khoản (máy Hùng / máy khách restore): không thêm tài khoản, không cảnh báo");
        {
            var (db, w) = Seed("1", null, hasUser: true);
            Ok(db.Users.List.Count == 1 && db.Users.List[0].FullName == "Admin thật" && w.Count == 0 && db.Customers.List.Count == 0 && db.PartMasters.List.Count == 0,
               "G26-4 giữ nguyên 1 tài khoản có sẵn, KHÔNG seed tài khoản / khách / Part mẫu (kể cả MES_SEED_DEMO=1 — rà độc lập L1)", $"{string.Join(",", db.Users.List.Select(u => u.Username))} kh {db.Customers.List.Count} part {db.PartMasters.List.Count}");
        }
        Console.WriteLine("[G26-5] SEED-DEMO — bật mẫu khi DB đã có khách (không đủ khách mẫu QM / KOSTAL): không seed Part mẫu (trước: First() ném lỗi lúc khởi động)");
        {
            Environment.SetEnvironmentVariable(SeedData.DemoEnv, "1");
            var db = new AppDbContext();
            db.Customers.List.Add(new Customer { CustomerId = 9, CustomerCode = "THAT", CustomerName = "Khách thật", IsActive = true }); db.Fixup();
            bool threw = false;
            try { SeedData.EnsureSeeded(db); } catch { threw = true; }
            finally { Environment.SetEnvironmentVariable(SeedData.DemoEnv, null); }
            db.Fixup();
            Ok(!threw && db.PartMasters.List.Count == 0 && db.Customers.List.Count == 1, "G26-5 có khách thật: không thêm khách / Part mẫu, không lỗi khởi động");
        }

        Console.WriteLine("[G26-6] C-10-GRACE — mất mạng: ≤ 7 ngày bình thường, 8–30 băng vàng (vẫn Active), > 30 khoá giao diện; chưa từng xác minh / không Active: không đụng");
        {
            var now = new DateTime(2026, 11, 20, 9, 0, 0);
            ServerLicenseHelper.LicenseInfo L(double days, ServerLicenseHelper.LicenseStatus st = ServerLicenseHelper.LicenseStatus.Active)
            {
                var i = new ServerLicenseHelper.LicenseInfo { Status = st, LastOnlineVerify = days < 0 ? DateTime.MinValue : now.AddDays(-days), Message = "cũ" };
                ServerLicenseHelper.ApplyOfflineGrace(i, now); return i;
            }
            var a7 = L(7); var a8 = L(7.5);
            Ok(a7.Status == ServerLicenseHelper.LicenseStatus.Active && a7.OfflineWarning == null && a8.OfflineWarning != null && a8.OfflineWarning.Contains("8 ngày"),
               "G26-6 biên: đúng 7 ngày không băng vàng; 7,5 ngày băng vàng ghi '8 ngày' (rà độc lập L4)", a8.OfflineWarning);
            var a = L(5);
            Ok(a.Status == ServerLicenseHelper.LicenseStatus.Active && a.OfflineWarning == null && a.OfflineDays == 5, "G26-6 5 ngày: Active, không băng vàng");
            var b = L(10);
            Ok(b.Status == ServerLicenseHelper.LicenseStatus.Active && (b.OfflineWarning ?? "").Contains("10 ngày") && (b.OfflineWarning ?? "").Contains("còn khoảng 20 ngày"), "G26-6 10 ngày: VẪN Active + băng vàng 'còn khoảng 20 ngày' (trước Gói 26: khoá cả xưởng)", b.Status + " | " + b.OfflineWarning);
            var c30 = L(30);
            Ok(c30.Status == ServerLicenseHelper.LicenseStatus.Active && c30.OfflineWarning != null, "G26-6 đúng 30 ngày: vẫn Active (băng vàng)");
            var d = L(31);
            Ok(d.Status == ServerLicenseHelper.LicenseStatus.Error && d.Message.Contains("30 ngày") && d.Message.Contains("Dữ liệu vẫn giữ nguyên"), "G26-6 31 ngày: khoá (Error), lời nói rõ dữ liệu giữ nguyên", d.Message);
            var e = L(-1);
            Ok(e.Status == ServerLicenseHelper.LicenseStatus.Active && e.OfflineWarning == null && e.Message == "cũ", "G26-6 chưa từng xác minh online (MinValue): không đụng");
            var f = L(40, ServerLicenseHelper.LicenseStatus.Inactive);
            Ok(f.Status == ServerLicenseHelper.LicenseStatus.Inactive && f.Message == "cũ", "G26-6 bản quyền không Active: không đổi (khoá như cũ)");
        }

        Console.WriteLine("[G26-7] LOG-FILE — ghi file mes-yyyyMMdd.log từ Warning, bỏ Information, xoá file quá 30 ngày lúc ghi đầu tiên trong ngày");
        {
            var dir = Path.Combine(Path.GetTempPath(), "mes_g26_log_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var today = DateTime.Now.Date;
            var oldF = Path.Combine(dir, $"mes-{today.AddDays(-31):yyyyMMdd}.log");
            var keepF = Path.Combine(dir, $"mes-{today.AddDays(-5):yyyyMMdd}.log");
            var f29 = Path.Combine(dir, $"mes-{today.AddDays(-29):yyyyMMdd}.log");
            var f30 = Path.Combine(dir, $"mes-{today.AddDays(-30):yyyyMMdd}.log");
            var other = Path.Combine(dir, "khac.txt");
            File.WriteAllText(oldF, "cu"); File.WriteAllText(keepF, "giu"); File.WriteAllText(f29, "29"); File.WriteAllText(f30, "30"); File.WriteAllText(other, "x");
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:File:Path"] = dir, ["Logging:File:MinLevel"] = "Warning", ["Logging:File:KeepDays"] = "30" }).Build();
            using var prov = FileLoggerProvider.FromConfig(cfg, "/khong-dung");
            var lg = prov.CreateLogger("MES.Thu");
            lg.LogInformation("thong tin khong ghi");
            lg.LogWarning("canh bao {N}", 7);
            lg.LogError(new InvalidOperationException("loi thu"), "loi that");
            var todayF = Path.Combine(dir, $"mes-{today:yyyyMMdd}.log");
            var txt = File.Exists(todayF) ? File.ReadAllText(todayF) : "";
            Ok(txt.Contains("canh bao 7") && txt.Contains("loi that") && txt.Contains("loi thu") && !txt.Contains("thong tin khong ghi"), "G26-7 file hôm nay có Warning + Error (kèm ngoại lệ), không có Information", txt.Length > 300 ? txt[..300] : txt);
            Ok(!File.Exists(oldF) && !File.Exists(f30) && File.Exists(f29) && File.Exists(keepF) && File.Exists(other), "G26-7 giữ ĐÚNG 30 file (hôm nay + 29 ngày trước), xoá file 30 / 31 ngày, giữ file khác tên");
            var bad = new FileLoggerProvider(Path.Combine(dir, "khong-tao-duoc\0:<>|"), LogLevel.Warning, 30);
            bool threwL = false; try { bad.CreateLogger("x").LogError("ghi vào đường lỗi"); } catch { threwL = true; }
            Ok(!threwL, "G26-7 thư mục log sai / không ghi được: không ném lỗi (không sập app)");
            var rel = FileLoggerProvider.FromConfig(new ConfigurationBuilder().Build(), dir);
            var rl = rel.CreateLogger("x"); rl.LogWarning("mac dinh");
            Ok(File.Exists(Path.Combine(dir, "logs", $"mes-{today:yyyyMMdd}.log")), "G26-7 không cấu hình: mặc định thư mục logs cạnh app");
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine($"\nPhép kiểm: {checks}");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }
}
