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

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
