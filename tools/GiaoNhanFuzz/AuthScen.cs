// [RS-C3][RS-C4][TOOLS-PUSH 2026-11-01] Kịch bản cố định `auth`: hàm THẬT Services/AuthGuard.cs (dựng lại phần drive_auth của gói ML2 30/10,
//   bản cũ ở zip sandbox chưa lên master). Kiểm: chuyển hướng sau đăng nhập chỉ đường dẫn nội bộ; phiên (cookie / mạch Blazor) hết hiệu lực
//   ngay khi tài khoản / nhóm bị khoá, đổi tên, đổi nhóm; giới hạn đăng nhập sai 5 lần / tên + máy, 50 lần / IP, khoá 15 phút.
using System.Security.Claims;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class AuthScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static ClaimsPrincipal P(string? id, string name, string? role, bool auth = true)
    {
        var c = new List<Claim> { new(ClaimTypes.Name, name) };
        if (id != null) c.Add(new(ClaimTypes.NameIdentifier, id));
        if (role != null) c.Add(new(ClaimTypes.Role, role));
        return new ClaimsPrincipal(auth ? new ClaimsIdentity(c, "Cookies") : new ClaimsIdentity(c));
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[U1] RS-C4 — sau đăng nhập chỉ chuyển về đường dẫn nội bộ của MES");
        {
            foreach (var (u, want) in new (string?, string)[] {
                ("/kh-plan", "/kh-plan"), ("/handover?po=1", "/handover?po=1"),
                (null, "/"), ("", "/"), ("kh-plan", "/"), ("//evil.com", "/"), ("/\\evil.com", "/"), ("https://evil.com", "/"),
                ("/x?u=https://evil.com", "/"), ("/\t/evil.com", "/"), ("/a\\b", "/"), ("/\n/evil.com", "/") })
                Ok(AuthGuard.SafeReturnUrl(u) == want, $"returnUrl {(u == null ? "null" : "'" + u.Replace("\t", "\\t").Replace("\n", "\\n") + "'")} → '{want}'", AuthGuard.SafeReturnUrl(u));
        }

        Console.WriteLine("[U2] RS-C3 — phiên còn hiệu lực chỉ khi tài khoản + nhóm đang hoạt động, tên + nhóm không đổi so với lúc đăng nhập");
        {
            var db = new AppDbContext();
            var gA = new UserGroup { GroupId = 1, GroupCode = "GC", IsActive = true };
            var gB = new UserGroup { GroupId = 2, GroupCode = "KHO", IsActive = true };
            db.UserGroups.List.AddRange(new[] { gA, gB });
            var u = new User { UserId = 5, Username = "gc01", FullName = "GC", IsActive = true, Group = gA, GroupId = 1 };
            db.Users.List.Add(u);
            db.Users.List.Add(new User { UserId = 6, Username = "nogroup", IsActive = true });
            db.Fixup();
            Ok(await AuthGuard.IsStillValidAsync(db, P("5", "gc01", "GC")), "Tài khoản + nhóm đang hoạt động: còn hiệu lực");
            Ok(await AuthGuard.IsStillValidAsync(db, P("5", "GC01", "GC")), "Tên khác hoa / thường: vẫn là người đó");
            Ok(await AuthGuard.IsStillValidAsync(db, P(null, "x", null, auth: false)), "Khách chưa đăng nhập: không đá");
            Ok(!await AuthGuard.IsStillValidAsync(db, P("abc", "gc01", "GC")), "Mã người dùng hỏng: hết hiệu lực");
            Ok(!await AuthGuard.IsStillValidAsync(db, P("99", "gc01", "GC")), "Người dùng không còn: hết hiệu lực");
            Ok(!await AuthGuard.IsStillValidAsync(db, P("6", "nogroup", null)), "Chưa có nhóm: hết hiệu lực");
            Ok(!await AuthGuard.IsStillValidAsync(db, P("5", "gc01", "KHO")), "Đổi nhóm (role trong phiên ≠ nhóm DB): phải đăng nhập lại");
            Ok(!await AuthGuard.IsStillValidAsync(db, P("5", "gc02", "GC")), "Đổi tên đăng nhập: hết hiệu lực");
            gA.IsActive = false;
            Ok(!await AuthGuard.IsStillValidAsync(db, P("5", "gc01", "GC")) && !AuthGuard.AccountUsable(u), "Nhóm bị khoá: hết hiệu lực ngay (và không đăng nhập được)");
            gA.IsActive = true; u.IsActive = false;
            Ok(!await AuthGuard.IsStillValidAsync(db, P("5", "gc01", "GC")) && !AuthGuard.AccountUsable(u), "Tài khoản bị khoá: hết hiệu lực ngay (và không đăng nhập được)");
            u.IsActive = true; u.GroupId = 2; u.Group = gB;
            Ok(await AuthGuard.IsStillValidAsync(db, P("5", "gc01", "KHO")), "Đăng nhập lại sau khi đổi nhóm: hiệu lực với nhóm mới");
        }

        Console.WriteLine("[U3] RS-C4 — giới hạn đăng nhập sai: 5 lần / tên + máy → khoá 15 phút; máy khác không bị; đăng nhập đúng xoá đếm; 50 lần / IP khoá IP");
        {
            var t0 = new DateTime(2030, 1, 1, 8, 0, 0); var tag = Guid.NewGuid().ToString("N")[..6];
            string user = "u" + tag, ip = "10.0." + tag.Length + ".1", ip2 = "10.9.9." + tag.Length;
            for (int i = 0; i < 4; i++) LoginThrottle.Fail(user, ip, t0.AddSeconds(i));
            Ok(LoginThrottle.LockedMinutes(user, ip, t0.AddSeconds(5)) == null, "Sai 4 lần: chưa khoá");
            LoginThrottle.Fail(user, ip, t0.AddSeconds(6));
            Ok(LoginThrottle.LockedMinutes(user, ip, t0.AddSeconds(7)) == 15, "Sai lần 5: khoá 15 phút", LoginThrottle.LockedMinutes(user, ip, t0.AddSeconds(7))?.ToString());
            Ok(LoginThrottle.LockedMinutes(user.ToUpperInvariant(), ip, t0.AddSeconds(7)) == 15, "Gõ tên khác hoa / thường: vẫn khoá");
            Ok(LoginThrottle.LockedMinutes(user, ip2, t0.AddSeconds(7)) == null, "Cùng tên trên máy khác: không khoá");
            Ok(LoginThrottle.LockedMinutes(user, ip, t0.AddMinutes(16)) == null, "Sau 15 phút: hết khoá");
            string u2 = "v" + tag;
            for (int i = 0; i < 4; i++) LoginThrottle.Fail(u2, ip2, t0.AddSeconds(i));
            LoginThrottle.Success(u2, ip2);
            LoginThrottle.Fail(u2, ip2, t0.AddSeconds(10));
            Ok(LoginThrottle.LockedMinutes(u2, ip2, t0.AddSeconds(11)) == null, "4 lần sai → đăng nhập đúng (xoá đếm) → sai 1 lần: không khoá");
            string ip3 = "10.7." + tag[..3].Length + "." + tag.Length;
            for (int i = 0; i < LoginThrottle.MaxPerIp; i++) LoginThrottle.Fail("w" + tag + i, ip3, t0.AddSeconds(i));
            Ok(LoginThrottle.LockedMinutes("tenmoi" + tag, ip3, t0.AddSeconds(60)) == 15, $"{LoginThrottle.MaxPerIp} lần sai nhiều tên từ 1 IP: khoá cả IP (tên mới cũng bị)");
        }

        // [C6-PIN 2026-11-02] Admin sửa thông tin người dùng (form không có ô PIN → PIN gửi lên = null): KHÔNG được xoá PIN.
        //   Xoá PIN chỉ qua Reset PIN (chỉ ADMIN). Gọi UserService THẬT qua IServiceScopeFactory trỏ DbContext giả.
        Console.WriteLine("[U4] C6-PIN — sửa người dùng (họ tên / nhóm / khoá) không xoá PIN; Reset PIN chỉ ADMIN");
        {
            var db = new AppDbContext();
            var gAd = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
            var gA = new UserGroup { GroupId = 2, GroupCode = "GC", IsActive = true };
            var gB = new UserGroup { GroupId = 3, GroupCode = "KHO", IsActive = true };
            db.UserGroups.List.AddRange(new[] { gAd, gA, gB });
            db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = gAd, GroupId = 1, PIN = "9999" });
            var u = new User { UserId = 5, Username = "gc01", FullName = "Cong nhan", IsActive = true, Group = gA, GroupId = 2, PIN = "1234" };
            db.Users.List.Add(u);
            db.Users.List.Add(new User { UserId = 6, Username = "kho01", FullName = "Kho", IsActive = true, Group = gB, GroupId = 3, PIN = "5555" });
            db.Fixup();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            // form Sửa gửi đối tượng User mới: không có PIN
            var r = await us.UpdateUserAsync(new User { UserId = 5, Username = "gc01", FullName = "Nguyen Van A", GroupId = 3, IsActive = true, PIN = null }, 1);
            Ok(r.Success && u.FullName == "Nguyen Van A" && u.GroupId == 3, "Đổi họ tên + nhóm: lưu được", r.Error);
            Ok(u.PIN == "1234", "PIN giữ nguyên '1234' sau khi sửa (trước: mất PIN)", u.PIN ?? "null");
            r = await us.UpdateUserAsync(new User { UserId = 5, Username = "gc01", FullName = "Nguyen Van A", GroupId = 3, IsActive = false, PIN = "" }, 1);
            Ok(r.Success && !u.IsActive && u.PIN == "1234", "Khoá tài khoản (PIN gửi lên rỗng): PIN vẫn '1234'", u.PIN ?? "null");
            var rp = await us.ResetPinAsync(5, 6);
            Ok(!rp.Success && rp.Error == "Chỉ ADMIN mới có thể reset PIN." && u.PIN == "1234", "Người không phải ADMIN bấm Reset PIN: chặn, PIN còn", rp.Error);
            rp = await us.ResetPinAsync(5, 1);
            Ok(rp.Success && u.PIN == null, "ADMIN Reset PIN: PIN về trống (cách duy nhất xoá PIN)", rp.Error);
            Ok(db.Users.List.Single(x => x.UserId == 6).PIN == "5555", "Người khác không bị đụng PIN");
        }

        // [USER-PERM-SVC 2026-11-03] Tạo / sửa người dùng, nhóm, ma trận quyền: máy chủ (UserService THẬT) tự kiểm người thao tác là ADMIN theo DB.
        //   Trước: chỉ trang kiểm bằng claim → Admin bị hạ nhóm vẫn đổi được quyền tới lần đăng nhập sau; gọi service không cần quyền gì.
        Console.WriteLine("[U6] USER-PERM-SVC — chỉ ADMIN (theo DB, đang hoạt động) tạo / sửa người dùng, nhóm, ma trận quyền");
        {
            var db = new AppDbContext();
            var gAd = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
            var gK = new UserGroup { GroupId = 3, GroupCode = "KHO", IsActive = true, Permissions = "[{\"SectionCode\":\"SETTING_ADMIN\",\"CanView\":true,\"CanEdit\":true}]" };
            var gOff = new UserGroup { GroupId = 4, GroupCode = "ADMIN2", IsActive = true };
            db.UserGroups.List.AddRange(new[] { gAd, gK, gOff });
            db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = gAd, GroupId = 1 });
            db.Users.List.Add(new User { UserId = 2, Username = "ad2", FullName = "Admin 2", IsActive = true, Group = gAd, GroupId = 1 });
            db.Users.List.Add(new User { UserId = 6, Username = "kho01", FullName = "Kho", IsActive = true, Group = gK, GroupId = 3 });
            db.Users.List.Add(new User { UserId = 7, Username = "ad3", FullName = "Admin khoá", IsActive = false, Group = gAd, GroupId = 1 });
            db.Fixup();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            string Snap() => System.Text.Json.JsonSerializer.Serialize(new {
                u = db.Users.List.OrderBy(x => x.UserId).Select(x => new { x.UserId, x.Username, x.FullName, x.GroupId, x.IsActive }),
                g = db.UserGroups.List.OrderBy(x => x.GroupId).Select(x => new { x.GroupId, x.GroupCode, x.GroupName, x.IsActive, x.Permissions }) });
            async Task<List<string?>> TryAll(int actor)
            {
                var errs = new List<string?>();
                errs.Add((await us.CreateUserAsync(new User { Username = "moi" + actor, FullName = "Moi", GroupId = 3 }, "123456", actor)).Error);
                errs.Add((await us.UpdateUserAsync(new User { UserId = 6, Username = "kho01", FullName = "Kho đổi", GroupId = 1, IsActive = true }, actor)).Error);
                errs.Add((await us.CreateGroupAsync(new UserGroup { GroupCode = "g" + actor, GroupName = "G" }, actor)).Error);
                errs.Add((await us.UpdateGroupAsync(new UserGroup { GroupId = 3, GroupCode = "KHO", GroupName = "Kho đổi", IsActive = true }, actor)).Error);
                errs.Add((await us.UpdateGroupPermissionsAsync(3, new List<MES.Web.Constants.GroupPermissionSetting> { new() { SectionCode = "SETTING_ADMIN", CanView = true, CanEdit = true }, new() { SectionCode = "HANDOVER_GC", CanEdit = true } }, actor)).Error);
                return errs;
            }
            var s0 = Snap();
            var e6 = await TryAll(6);
            Ok(e6.All(e => e == UserService.NotAdminMsg) && Snap() == s0,
               "Người nhóm KHO (kể cả nhóm có tích SETTING_ADMIN trong ma trận) gọi 5 hàm: chặn hết, DB không đổi (trước: lưu được — tự cấp quyền / đổi mình sang nhóm ADMIN)", string.Join(" | ", e6));
            var e7 = await TryAll(7);
            Ok(e7.All(e => e == UserService.NotAdminMsg) && Snap() == s0, "Admin đã bị khoá tài khoản: chặn hết", string.Join(" | ", e7));
            var e0 = await TryAll(0);
            Ok(e0.All(e => e == UserService.NotAdminMsg) && Snap() == s0, "Không xác định người thao tác (id 0): chặn hết", string.Join(" | ", e0));
            // Admin 2 bị hạ xuống nhóm KHO (claim trên trình duyệt của họ vẫn ghi ADMIN tới lần đăng nhập sau) → máy chủ đọc DB: chặn ngay
            var r = await us.UpdateUserAsync(new User { UserId = 2, Username = "ad2", FullName = "Admin 2", GroupId = 3, IsActive = true }, 1);
            Ok(r.Success && db.Users.List.Single(x => x.UserId == 2).GroupId == 3, "Admin hạ ad2 xuống nhóm KHO: lưu được", r.Error);
            db.Users.List.Single(x => x.UserId == 2).Group = gK;   // DB giả không tự nối User.Group theo GroupId mới (EF thật đọc lại từ DB)
            var s1 = Snap();
            var e2 = await TryAll(2);
            Ok(e2.All(e => e == UserService.NotAdminMsg) && Snap() == s1 && !await us.IsAdminAsync(2), "ad2 vừa bị hạ nhóm gọi 5 hàm: chặn ngay (không chờ đăng nhập lại), IsAdminAsync = false", string.Join(" | ", e2));
            var e1 = await TryAll(1);
            Ok(e1.All(e => e == null) && db.Users.List.Any(x => x.Username == "moi1") && db.UserGroups.List.Any(x => x.GroupCode == "G1")
               && db.UserGroups.List.Single(x => x.GroupId == 3).Permissions!.Contains("HANDOVER_GC"),
               "Admin (nhóm ADMIN, đang hoạt động) gọi 5 hàm: lưu được", string.Join(" | ", e1));
            gAd.IsActive = false;
            Ok(!await us.IsAdminAsync(1), "Nhóm ADMIN bị tắt: IsAdminAsync = false (cùng luật PermCheck: nhóm phải đang hoạt động)");
        }

        // [USER-PERM-SVC 2026-11-03] (rà độc lập V1 / T7) không để hệ thống mất hết ADMIN: tự khoá / tự rời nhóm ADMIN, hạ ADMIN cuối cùng, tắt nhóm ADMIN
        Console.WriteLine("[U7] USER-PERM-SVC — không tự khoá mình, không hạ ADMIN cuối cùng, không ngưng nhóm ADMIN");
        {
            var db = new AppDbContext();
            var gAd = new UserGroup { GroupId = 1, GroupCode = "ADMIN", GroupName = "Admin", IsActive = true };
            var gK = new UserGroup { GroupId = 3, GroupCode = "KHO", GroupName = "Kho", IsActive = true };
            db.UserGroups.List.AddRange(new[] { gAd, gK });
            var boss = new User { UserId = 1, Username = "boss", FullName = "Boss", IsActive = true, Group = gAd, GroupId = 1 };
            var ad2 = new User { UserId = 2, Username = "ad2", FullName = "Admin 2", IsActive = true, Group = gAd, GroupId = 1 };
            db.Users.List.AddRange(new[] { boss, ad2, new User { UserId = 3, Username = "kho01", FullName = "Kho", IsActive = true, Group = gK, GroupId = 3 } });
            db.Fixup();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            var r = await us.UpdateUserAsync(new User { UserId = 1, Username = "boss", FullName = "Boss", GroupId = 1, IsActive = false }, 1);
            Ok(!r.Success && r.Error!.Contains("Không tự khoá") && boss.IsActive, "boss tự khoá tài khoản mình: chặn (còn ad2 nhưng không tự khoá)", r.Error);
            r = await us.UpdateUserAsync(new User { UserId = 1, Username = "boss", FullName = "Boss", GroupId = 3, IsActive = true }, 1);
            Ok(!r.Success && r.Error!.Contains("Không tự khoá") && boss.GroupId == 1, "boss tự chuyển mình sang nhóm KHO: chặn", r.Error);
            r = await us.UpdateUserAsync(new User { UserId = 2, Username = "ad2", FullName = "Admin 2", GroupId = 3, IsActive = true }, 1);
            Ok(r.Success && ad2.GroupId == 3, "boss hạ ad2 xuống KHO (boss vẫn là ADMIN): lưu được", r.Error);
            ad2.Group = gK;
            r = await us.UpdateUserAsync(new User { UserId = 1, Username = "boss", FullName = "Boss", GroupId = 1, IsActive = false }, 1);
            Ok(!r.Success && r.Error!.Contains("ADMIN đang hoạt động cuối cùng") && boss.IsActive, "boss là ADMIN cuối cùng tự khoá: chặn, báo \"ADMIN đang hoạt động cuối cùng\"", r.Error);
            r = await us.UpdateUserAsync(new User { UserId = 1, Username = "boss", FullName = "Boss Moi", GroupId = 1, IsActive = true }, 1);
            Ok(r.Success && boss.FullName == "Boss Moi", "ADMIN cuối cùng đổi họ tên (vẫn ADMIN): lưu được", r.Error);
            var g = await us.UpdateGroupAsync(new UserGroup { GroupId = 1, GroupCode = "ADMIN", GroupName = "Admin", IsActive = false }, 1);
            Ok(!g.Success && gAd.IsActive, "Ngưng nhóm ADMIN: chặn", g.Error);
            g = await us.UpdateGroupAsync(new UserGroup { GroupId = 3, GroupCode = "KHO", GroupName = "Kho", IsActive = false }, 1);
            Ok(g.Success && !gK.IsActive, "Ngưng nhóm thường (KHO): lưu được", g.Error);
        }

        // [PHAT-HIEN-0311 2026-11-04] Ca làm việc / Sơ đồ tổ chức / DS Lỗi: máy chủ (service THẬT) tự kiểm quyền Sửa theo DB (PermCheck), không theo claim.
        //   Người dùng: 1 = ADMIN · 2 = chỉ Xem mục · 3 = có Sửa mục · 4 = có Sửa nhưng tài khoản khoá · 5 = Admin cũ đã bị hạ sang nhóm chỉ Xem.
        static AppDbContext PermDb(string sec)
        {
            var db = new AppDbContext();
            var gAd = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
            var gV = new UserGroup { GroupId = 2, GroupCode = "XEM", IsActive = true, Permissions = "[{\"SectionCode\":\"" + sec + "\",\"CanView\":true,\"CanEdit\":false}]" };
            var gE = new UserGroup { GroupId = 3, GroupCode = "SUA", IsActive = true, Permissions = "[{\"SectionCode\":\"" + sec + "\",\"CanView\":true,\"CanEdit\":true}]" };
            db.UserGroups.List.AddRange(new[] { gAd, gV, gE });
            db.Users.List.Add(new User { UserId = 1, Username = "boss", FullName = "Boss", IsActive = true, Group = gAd, GroupId = 1, PIN = "1111" });
            db.Users.List.Add(new User { UserId = 2, Username = "xem", FullName = "Xem", IsActive = true, Group = gV, GroupId = 2, PIN = "2222" });
            db.Users.List.Add(new User { UserId = 3, Username = "sua", FullName = "Sua", IsActive = true, Group = gE, GroupId = 3, PIN = "3333" });
            db.Users.List.Add(new User { UserId = 4, Username = "khoa", FullName = "Khoa", IsActive = false, Group = gE, GroupId = 3, PIN = "4444" });
            db.Users.List.Add(new User { UserId = 5, Username = "adcu", FullName = "Admin cu", IsActive = true, Group = gV, GroupId = 2, PIN = "5555" });
            db.Fixup();
            return db;
        }

        Console.WriteLine("[U8] PHAT-HIEN-0311 — Ca làm việc: Lưu / Ẩn / Xoá cần quyền Sửa CA_LAM_VIEC (DB); ca đã dùng ở WTS không xoá được, chỉ Ẩn");
        {
            var db = PermDb(MES.Web.Constants.SystemPermissions.CaLamViec);
            var ca1 = new CaLamViec { CaId = 1, TenCa = "Hành chính", GioBatDau = new TimeOnly(7, 30), GioKetThuc = new TimeOnly(16, 30), IsActive = true };
            ca1.NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { NghiId = 1, CaId = 1, NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0) });
            var ca2 = new CaLamViec { CaId = 2, TenCa = "Ca 2", GioBatDau = new TimeOnly(14, 0), GioKetThuc = new TimeOnly(22, 0), IsActive = true };
            var ca3 = new CaLamViec { CaId = 3, TenCa = "Ca 3", GioBatDau = new TimeOnly(22, 0), GioKetThuc = new TimeOnly(6, 0), IsActive = true };
            var ca4 = new CaLamViec { CaId = 4, TenCa = "Ca thử", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(9, 0), IsActive = true };
            db.CaLamViecs.List.AddRange(new[] { ca1, ca2, ca3, ca4 });
            db.CaLamViec_NghiGiaiLaos.List.AddRange(ca1.NghiGiaiLaos);
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 1, CaId = 1, IsVoided = true });   // dòng đã huỷ vẫn giữ lịch sử ca
            db.WtsBreakWorkLogs.List.Add(new WtsBreakWorkLog { Id = 1, CaId = 2 });
            db.Fixup();
            var cs = new CaLamViecService(db);
            CaLamViec Form(int id, string ten) => new CaLamViec { CaId = id, TenCa = ten, GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0), IsActive = true };
            List<CaLamViec_NghiGiaiLao> Nghi() => new() { new() { NghiBatDau = new TimeOnly(11, 30), NghiKetThuc = new TimeOnly(12, 30) } };

            foreach (var (uid, who) in new[] { (2, "người chỉ có quyền Xem"), (4, "tài khoản bị khoá (nhóm có Sửa)"), (5, "Admin cũ đã bị hạ sang nhóm chỉ Xem"), (0, "không đăng nhập (id 0)") })
            {
                var sv = await cs.SaveAsync(Form(1, "Đổi giờ"), Nghi(), uid);
                var tg = await cs.ToggleActiveAsync(3, false, uid);
                var dl = await cs.DeleteAsync(4, uid);
                Ok(!sv.Ok && !tg.Ok && !dl.Ok && sv.Error.Contains("không có quyền Sửa mục Cài đặt ca làm việc")
                   && ca1.TenCa == "Hành chính" && ca1.GioBatDau == new TimeOnly(7, 30) && db.CaLamViec_NghiGiaiLaos.List.Count(x => x.CaId == 1) == 1
                   && db.CaLamViec_NghiGiaiLaos.List.Single(x => x.CaId == 1).NghiBatDau == new TimeOnly(12, 0)
                   && ca3.IsActive && db.CaLamViecs.List.Any(x => x.CaId == 4),
                   $"{who}: Lưu / Ẩn / Xoá đều bị chặn, ca + giờ nghỉ không đổi", $"{sv.Error} | {tg.Error} | {dl.Error}");
                var add = await cs.SaveAsync(Form(0, "Ca lạ " + uid), new(), uid);
                Ok(!add.Ok && !db.CaLamViecs.List.Any(x => x.TenCa == "Ca lạ " + uid), $"{who}: Thêm ca mới bị chặn", add.Error);
            }

            var s3 = await cs.SaveAsync(Form(1, "Hành chính mới"), Nghi(), 3);
            Ok(s3.Ok && ca1.TenCa == "Hành chính mới" && ca1.GioBatDau == new TimeOnly(7, 0)
               && db.CaLamViec_NghiGiaiLaos.List.Where(x => x.CaId == 1).Select(x => x.NghiBatDau).SequenceEqual(new[] { new TimeOnly(11, 30) }),
               "Người có quyền Sửa CA_LAM_VIEC: đổi giờ ca + giờ nghỉ lưu được", s3.Error);
            var t3 = await cs.ToggleActiveAsync(3, false, 3);
            var t3b = await cs.ToggleActiveAsync(3, false, 3);   // bấm Ẩn lần 2 (trang cũ / bản EF cũ): vẫn Tắt, không đảo lại thành Bật
            Ok(t3.Ok && t3b.Ok && !ca3.IsActive, "Người có quyền Sửa: Ẩn ca 3 được; gửi Ẩn lần nữa vẫn Tắt (gửi trạng thái muốn, không đảo)", t3.Error + t3b.Error);
            // [CA-SAVE-TRACK] Ẩn xong sửa ngay ca đó (bản sao của trang, Bật/Tắt trên form là bản cũ) → lưu đúng, Bật/Tắt giữ theo DB
            var f3 = Form(3, "Ca 3 đêm"); f3.GioBatDau = new TimeOnly(22, 0); f3.GioKetThuc = new TimeOnly(6, 0); f3.IsActive = true;
            var e3 = await cs.SaveAsync(f3, new(), 3);
            Ok(e3.Ok && ca3.TenCa == "Ca 3 đêm" && !ca3.IsActive && db.CaLamViecs.List.Count(x => x.CaId == 3) == 1,
               "Ẩn ca rồi Sửa ngay ca đó: lưu tên mới, ca vẫn Ẩn (form không đổi Bật/Tắt), không sinh dòng trùng", e3.Error);
            var s1 = await cs.SaveAsync(Form(0, "Ca mới"), new(), 1);
            Ok(s1.Ok && db.CaLamViecs.List.Any(x => x.TenCa == "Ca mới"), "ADMIN thêm ca mới: lưu được", s1.Error);
            var gone = await cs.SaveAsync(Form(99, "Không có"), new(), 1);
            Ok(!gone.Ok && gone.Error.Contains("không còn"), "Sửa ca đã bị xoá (id không còn): báo, không thêm ca mới", gone.Error);

            var d1 = await cs.DeleteAsync(1, 1);
            Ok(!d1.Ok && d1.Error.Contains("đã được dùng (1 dòng WTS") && d1.Error.Contains("bấm Ẩn") && db.CaLamViecs.List.Any(x => x.CaId == 1),
               "Xoá ca đã có dòng WTS (kể cả dòng đã huỷ): chặn, gợi ý Ẩn, ca còn", d1.Error);
            var d2 = await cs.DeleteAsync(2, 3);
            Ok(!d2.Ok && d2.Error.Contains("0 dòng WTS, 1 dòng giờ nghỉ đã lưu") && db.CaLamViecs.List.Any(x => x.CaId == 2),
               "Xoá ca chỉ có giờ nghỉ đã chép (WtsBreakWorkLogs): chặn, ca còn", d2.Error);
            var d4 = await cs.DeleteAsync(4, 3);
            Ok(d4.Ok && !db.CaLamViecs.List.Any(x => x.CaId == 4), "Xoá ca chưa ai dùng (người có quyền Sửa): xoá được như cũ", d4.Error);
            // EfStub Remove xoá ngay khỏi List → giả SQL "không xoá" bằng cách trả ca lại vào bảng lúc SaveChanges ném lỗi
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => { if (!db.CaLamViecs.List.Contains(ca3)) db.CaLamViecs.List.Add(ca3); return new InvalidOperationException("FK WtsProductionLogs.CaId"); };
            try
            {
                var dx = await cs.DeleteAsync(3, 1);
                Ok(!dx.Ok && dx.Error.Contains("Không xoá được ca Ca 3 đêm") && db.CaLamViecs.List.Contains(ca3), "Xoá ca mà SQL báo lỗi (vd dòng WTS vừa ghi ca này): báo, ca còn, trang không chết", dx.Error);
                Microsoft.EntityFrameworkCore.DbContext.OnSave = () => new InvalidOperationException("0 dòng bị xoá (người khác xoá trước)");
                var dy = await cs.DeleteAsync(3, 1);
                Ok(!dy.Ok && dy.Error.Contains("vừa bị người khác xoá"), "Xoá ca mà ca đã bị người khác xoá ngay trước: báo đúng (không đổ cho dòng WTS)", dy.Error);
            }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; db.ChangeTracker.Clear(); }
        }

        Console.WriteLine("[U9] PHAT-HIEN-0311 — Sơ đồ tổ chức: Thêm / Sửa / Xoá / tải ảnh cần quyền Sửa SO_DO_TC (DB); ảnh chỉ .jpg / .jpeg / .png / .webp thật");
        {
            var db = PermDb(MES.Web.Constants.SystemPermissions.SoDoTc);
            var root = new Department { DepartmentId = 1, DepartmentCode = "GD", DepartmentName = "Giám đốc", Level = 1, IsActive = true, ManagerName = "A", AvatarUrl = "/uploads/avatars/cu.gif" };
            db.Departments.List.Add(root);
            db.Departments.List.Add(new Department { DepartmentId = 2, DepartmentCode = "SX", DepartmentName = "Sản xuất", ParentId = 1, Level = 2, IsActive = true });
            db.Fixup();
            var ds = new DepartmentService(db, new AuthService(db));
            Ok(await ds.CanViewAsync(2) && !await ds.CanEditAsync(2) && await ds.CanEditAsync(3) && await ds.CanEditAsync(1) && !await ds.CanViewAsync(4) && !await ds.CanEditAsync(5),
               "Quyền đọc DB: Xem-only xem được không sửa; Sửa / ADMIN sửa được; tài khoản khoá không xem; Admin cũ bị hạ không sửa");
            foreach (var (uid, pin, who) in new[] { (2, "2222", "người chỉ có quyền Xem"), (4, "4444", "tài khoản bị khoá"), (5, "5555", "Admin cũ đã bị hạ") })
            {
                var c = await ds.CreateAsync(new Department { DepartmentCode = "X" + uid, DepartmentName = "Lạ", ParentId = 1 }, uid, pin);
                var u = await ds.UpdateAsync(new Department { DepartmentId = 2, DepartmentCode = "SX", DepartmentName = "Đổi tên", ParentId = 1 }, "thử", uid, pin, false, "");
                var d = await ds.SoftDeleteAsync(2, uid, pin);
                Ok(!c.Success && !u.Success && !d.Success && c.Error!.Contains("không có quyền Sửa mục Sơ đồ tổ chức")
                   && db.Departments.List.Count == 2 && db.Departments.List.Single(x => x.DepartmentId == 2).DepartmentName == "Sản xuất"
                   && db.Departments.List.Single(x => x.DepartmentId == 2).IsActive && db.DepartmentChangeLogs.List.Count == 0,
                   $"{who} (PIN đúng): Thêm / Sửa / Xoá đều bị chặn, sơ đồ không đổi", $"{c.Error} | {u.Error} | {d.Error}");
            }
            var c3 = await ds.CreateAsync(new Department { DepartmentCode = "kt", DepartmentName = "Kế toán", ParentId = 1, AvatarUrl = "/uploads/avatars/nv_20261104100000_ab12cd34.png" }, 3, "3333");
            Ok(c3.Success && db.Departments.List.Any(x => x.DepartmentCode == "KT"), "Người có quyền Sửa + PIN đúng: thêm được", c3.Error);
            var cBad = await ds.CreateAsync(new Department { DepartmentCode = "HACK", DepartmentName = "x", AvatarUrl = "/uploads/avatars/x.html" }, 3, "3333");
            Ok(!cBad.Success && !db.Departments.List.Any(x => x.DepartmentCode == "HACK"), "Thêm với đường dẫn ảnh .html (gửi thẳng, không qua nút tải): chặn", cBad.Error);
            var cPin = await ds.CreateAsync(new Department { DepartmentCode = "P1", DepartmentName = "x" }, 3, "0000");
            Ok(!cPin.Success && cPin.Error!.Contains("PIN"), "Có quyền nhưng PIN sai: vẫn chặn như cũ", cPin.Error);
            var uKeep = await ds.UpdateAsync(new Department { DepartmentId = 1, DepartmentCode = "GD", DepartmentName = "Ban giám đốc", ManagerName = "A", AvatarUrl = "/uploads/avatars/cu.gif" }, "đổi tên", 3, "3333", false, "A");
            Ok(uKeep.Success && root.DepartmentName == "Ban giám đốc" && root.AvatarUrl == "/uploads/avatars/cu.gif", "Sửa tên, giữ ảnh cũ (đuôi .gif có từ trước): lưu được, ảnh không đổi", uKeep.Error);
            var uBad = await ds.UpdateAsync(new Department { DepartmentId = 1, DepartmentCode = "GD", DepartmentName = "Ban giám đốc", ManagerName = "A", AvatarUrl = "javascript:alert(1)" }, "x", 3, "3333", true, "A");
            Ok(!uBad.Success && root.AvatarUrl == "/uploads/avatars/cu.gif", "Sửa đổi ảnh sang đường dẫn lạ (javascript:, .html…): chặn", uBad.Error);

            var dir = Path.Combine(Path.GetTempPath(), "mes_u9_" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
                byte[] jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 };
                byte[] webp = System.Text.Encoding.ASCII.GetBytes("RIFF\x10\0\0\0WEBPVP8 ");
                byte[] html = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
                async Task<(bool Ok, string? Error, string? Url)> Up(byte[] b, string name, int uid) { using var ms = new MemoryStream(b); return await ds.SaveAvatarAsync(ms, name, b.Length, uid, dir); }
                int Files() => Directory.Exists(Path.Combine(dir, "uploads", "avatars")) ? Directory.GetFiles(Path.Combine(dir, "uploads", "avatars")).Length : 0;
                var a1 = await Up(png, "anh.PNG", 3); var a2 = await Up(jpg, "a.jpeg", 3); var a3 = await Up(jpg, "b.jpg", 1); var a4 = await Up(webp, "c.webp", 3);
                Ok(a1.Ok && a2.Ok && a3.Ok && a4.Ok && Files() == 4 && a1.Url!.EndsWith(".png") && DepartmentService.AvatarUrlOk(a1.Url) && DepartmentService.AvatarUrlOk(a4.Url),
                   "Ảnh .PNG / .jpeg / .jpg / .webp thật (người có quyền Sửa / ADMIN): lưu được, đường dẫn hợp lệ", $"{a1.Error} {a2.Error} {a3.Error} {a4.Error} · file {Files()}");
                var b1 = await Up(html, "x.html", 3); var b2 = await Up(html, "x.js", 3); var b3 = await Up(png, "khongduoi", 3); var b4 = await Up(png, "a.svg", 3); var b5 = await Up(png, "a.gif", 3);
                Ok(!b1.Ok && !b2.Ok && !b3.Ok && !b4.Ok && !b5.Ok && b1.Error == DepartmentService.MsgAvatarType && Files() == 4,
                   "File .html / .js / không đuôi / .svg / .gif: chặn, không ghi file", $"{b1.Error} | {b3.Error} | file {Files()}");
                var b6 = await Up(html, "gia.png", 3);
                Ok(!b6.Ok && b6.Error!.Contains("không khớp đuôi") && Files() == 4, "File HTML đổi tên thành .png (nội dung không phải ảnh): chặn", b6.Error);
                var b7 = await Up(png, "a.png", 2); var b8 = await Up(png, "a.png", 5);
                Ok(!b7.Ok && !b8.Ok && b7.Error!.Contains("không có quyền Sửa") && Files() == 4, "Người chỉ có quyền Xem / Admin cũ bị hạ tải ảnh: chặn, không ghi file", b7.Error);
                var big = new byte[DepartmentService.AvatarMaxBytes + 1]; png.CopyTo(big, 0);
                var b9 = await Up(big, "to.png", 3);
                Ok(!b9.Ok && b9.Error!.Contains("5MB") && Files() == 4, "Ảnh > 5MB: chặn", b9.Error);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        Console.WriteLine("[U10] PHAT-HIEN-0311 — DS Lỗi: thêm / sửa / ngưng dùng / xoá loại lỗi cần quyền Sửa DS_LOI (DB)");
        {
            var db = PermDb(MES.Web.Constants.SystemPermissions.DsLoi);
            db.NgCategories.List.Add(new NgCategory { NgCategoryId = 1, Code = "XUOC", Name = "Xước", IsActive = true });
            db.NgCategories.List.Add(new NgCategory { NgCategoryId = 2, Code = "MOP", Name = "Móp", IsActive = true });
            db.Fixup();
            var ng = new NgCaseService(db);
            foreach (var (uid, who) in new[] { (2, "người chỉ có quyền Xem"), (4, "tài khoản bị khoá"), (5, "Admin cũ đã bị hạ"), (0, "id 0") })
            {
                var a = await ng.SaveCategoryAsync(null, "LA" + uid, "Lạ", null, 0, uid);
                var e = await ng.SaveCategoryAsync(1, "XUOC", "Đổi", null, 0, uid);
                var t = await ng.SetActiveAsync(1, false, uid);
                var d = await ng.DeleteCategoryAsync(2, uid);
                Ok(!a.Ok && !e.Ok && !t.Ok && !d.Ok && a.Error.Contains("không có quyền Sửa mục DS Lỗi")
                   && db.NgCategories.List.Count == 2 && db.NgCategories.List[0].Name == "Xước" && db.NgCategories.List[0].IsActive && db.NgCategoryChangeLogs.List.Count == 0,
                   $"{who}: Thêm / Sửa / Ngưng dùng / Xoá đều bị chặn, danh mục không đổi", $"{a.Error} | {e.Error} | {t.Error} | {d.Error}");
            }
            var a3 = await ng.SaveCategoryAsync(null, "ba via", "Ba via", null, 1, 3);
            var e3 = await ng.SaveCategoryAsync(1, "XUOC", "Xước cạnh", null, 0, 3);
            var t3 = await ng.SetActiveAsync(1, false, 3);
            var d3 = await ng.DeleteCategoryAsync(2, 1);
            Ok(a3.Ok && e3.Ok && t3.Ok && d3.Ok && db.NgCategories.List.Any(x => x.Code == "BA VIA") && db.NgCategories.List.Single(x => x.NgCategoryId == 1).Name == "Xước cạnh"
               && !db.NgCategories.List.Single(x => x.NgCategoryId == 1).IsActive && !db.NgCategories.List.Any(x => x.NgCategoryId == 2),
               "Người có quyền Sửa DS_LOI / ADMIN: thêm / sửa / ngưng dùng / xoá được như cũ", $"{a3.Error} | {e3.Error} | {t3.Error} | {d3.Error}");
        }

        // [SET-MATRIX-1TX 2026-11-05] (PHAT-HIEN-0311 mục 4, 5) Lưu ma trận MỌI nhóm 1 giao dịch; ô SETTING_ADMIN bị bỏ khỏi ma trận nhóm thường
        Console.WriteLine("[U11] SET-MATRIX-1TX — lưu ma trận mọi nhóm 1 giao dịch (lỗi 1 nhóm thì không nhóm nào đổi); bỏ ô SETTING_ADMIN; nhóm ADMIN không đổi; chỉ ADMIN");
        {
            var db = new AppDbContext();
            var gAd = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true, Permissions = "[]" };
            var gK = new UserGroup { GroupId = 3, GroupCode = "KHO", IsActive = true, Permissions = "[{\"SectionCode\":\"SETTING_ADMIN\",\"CanView\":true,\"CanEdit\":true}]" };
            var gG = new UserGroup { GroupId = 5, GroupCode = "GC", IsActive = true, Permissions = "[]" };
            db.UserGroups.List.AddRange(new[] { gAd, gK, gG });
            db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = gAd, GroupId = 1 });
            db.Users.List.Add(new User { UserId = 6, Username = "kho01", FullName = "Kho", IsActive = true, Group = gK, GroupId = 3 });
            db.Fixup();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            string Snap() => string.Join("|", db.UserGroups.List.OrderBy(x => x.GroupId).Select(x => x.GroupId + "=" + x.Permissions));
            List<MES.Web.Constants.GroupPermissionSetting> P(params string[] codes) => codes.Select(cd => new MES.Web.Constants.GroupPermissionSetting { SectionCode = cd, CanView = true, CanEdit = true }).ToList();
            var s0 = Snap();
            var r1 = await us.UpdateAllGroupPermissionsAsync(new() { [3] = P("KHO"), [5] = P("HANDOVER_GC") }, 6);
            Ok(!r1.Success && r1.Error == UserService.NotAdminMsg && Snap() == s0, "Người nhóm KHO (có ô SETTING_ADMIN cũ) lưu ma trận: chặn, DB không đổi", r1.Error);
            // nhóm thứ 2 không còn (vừa bị xoá) → không nhóm nào đổi, kể cả nhóm 3 đứng trước (trước: lưu từng nhóm → nhóm 3 đã đổi)
            var r2 = await us.UpdateAllGroupPermissionsAsync(new() { [3] = P("KHO"), [99] = P("GC") }, 1);
            Ok(!r2.Success && r2.Error!.Contains("chưa nhóm nào được đổi") && Snap() == s0 && db.Database.CurrentTransaction == null, "Có nhóm không tìm thấy: báo lỗi, KHÔNG nhóm nào đổi (1 giao dịch), giao dịch đã đóng", $"{r2.Error} | {Snap()}");
            var r3 = await us.UpdateAllGroupPermissionsAsync(new() { [1] = P("KHO"), [3] = P("KHO", "SETTING_ADMIN"), [5] = P("HANDOVER_GC") }, 1);
            Ok(r3.Success && gAd.Permissions == "[]" && gK.Permissions!.Contains("\"KHO\"") && !gK.Permissions.Contains("SETTING_ADMIN") && gG.Permissions!.Contains("HANDOVER_GC"),
               "Admin lưu: nhóm KHO + GC đổi, ô SETTING_ADMIN bị bỏ (kể cả ô cũ trong DB), nhóm ADMIN không đổi", $"{r3.Error} | {Snap()}");
            // lỗi khi ghi DB giữa chừng → rollback cả lô
            var s3 = Snap();
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => new InvalidOperationException("gia loi DB");
            string? ex4 = null; try { await us.UpdateAllGroupPermissionsAsync(new() { [3] = P("DAO"), [5] = P("DO_GA") }, 1); } catch (Exception ex) { ex4 = ex.Message; }
            Microsoft.EntityFrameworkCore.DbContext.OnSave = null;
            Ok(ex4 == "gia loi DB" && Snap() == s3 && db.Database.CurrentTransaction == null, "Lỗi DB khi lưu: không nhóm nào đổi, giao dịch đã đóng", $"{ex4} | {Snap()}");
        }

        // [C7-LICENSE 2026-11-02] Máy chủ bản quyền trả chữ KHÔNG phải JSON (trang lỗi HTML, "Service invoked too many times"…) = API chưa trả lời được:
        //   ParseApiResponse phải NÉM lỗi để PerformOnlineCheck giữ nguyên bản quyền đang có (như mất mạng) — trước: nuốt lỗi → Inactive → ghi đè cache → khoá xưởng.
        //   Chỉ gọi hàm đọc phản hồi (riêng tư) bằng phản chiếu — KHÔNG gọi API thật, không ghi cache / file bản quyền.
        Console.WriteLine("[U5] C7-LICENSE — phản hồi bản quyền không phải JSON: không coi là 'chưa kích hoạt' (ném lỗi → giữ bản quyền cũ)");
        {
            var parse = typeof(ServerLicenseHelper).GetMethod("ParseApiResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Ok(parse != null, "Có hàm ParseApiResponse(string)");
            if (parse != null)
            {
                (ServerLicenseHelper.LicenseInfo? Info, Exception? Err) P(string body)
                {
                    try { return ((ServerLicenseHelper.LicenseInfo)parse.Invoke(null, new object[] { body })!, null); }
                    catch (System.Reflection.TargetInvocationException ex) { return (null, ex.InnerException); }
                }
                foreach (var (body, what) in new[] {
                    ("<!DOCTYPE html><html><body>Service invoked too many times for one day: urlfetch.</body></html>", "trang lỗi HTML"),
                    ("Service invoked too many times for one day", "chữ thường"),
                    ("", "rỗng"),
                    ("{\"success\":true,\"status\":", "JSON cụt") })
                {
                    var (info, err) = P(body);
                    Ok(info == null && err is InvalidOperationException && err.Message.Contains("không phải JSON"), $"Phản hồi {what}: ném lỗi 'không phải JSON' (không trả Inactive)",
                       info != null ? $"trả Status={info.Status} Message='{info.Message}'" : err?.GetType().Name + ": " + err?.Message);
                }
                var ok = P("{\"success\":true,\"status\":\"Active\",\"expiration\":\"2027-12-31\",\"message\":\"OK\",\"name\":\"Nhat Phat\"}");
                Ok(ok.Err == null && ok.Info!.Status == ServerLicenseHelper.LicenseStatus.Active && ok.Info.ExpirationDate == "2027-12-31" && ok.Info.LicensedTo == "Nhat Phat",
                   "JSON hợp lệ Active: đọc đúng (Active, hạn 2027-12-31, tên)", ok.Err?.Message);
                var no = P("{\"success\":false,\"error\":\"Key chua kich hoat\"}");
                Ok(no.Err == null && no.Info!.Status == ServerLicenseHelper.LicenseStatus.Inactive && no.Info.Message == "Key chua kich hoat",
                   "JSON hợp lệ nói chưa kích hoạt: vẫn Inactive (API trả lời thật)", no.Err?.Message);
            }
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
