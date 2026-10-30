using System.Collections.Concurrent;
using System.Security.Claims;
using MES.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

// =====================================================================
// [RS-C3][RS-C4 2026-10-30] Bảo mật đăng nhập trước khi mở ra Internet
//   RS-C3: khoá tài khoản / khoá nhóm / đổi nhóm phải có hiệu lực ngay — trước: cookie 8 giờ (gia hạn trượt) giữ nguyên quyền,
//          chỉ trang đọc ma trận từ DB mới chặn; trang chỉ [Authorize] / IsInRole("ADMIN") (Part Master, Kho, Cài đặt) vẫn vào được.
//          Nay: (1) mỗi request HTTP kiểm lại cookie với DB (CookieEvents.ValidatePrincipal), không còn hợp lệ → đăng xuất;
//               (2) mạch Blazor đang mở kiểm lại mỗi 1 phút (MesRevalidatingAuthStateProvider) → về trang đăng nhập.
//          "Còn hợp lệ" = user có thật + đang hoạt động + nhóm đang hoạt động + tên đăng nhập / nhóm (role) KHÔNG đổi so với lúc đăng nhập.
//          (Tên "admin" luôn là Admin giữ nguyên theo CLAUDE-RULES mục 7 — đổi luật này cần Hùng chốt.)
//   RS-C4: giới hạn đăng nhập sai (LoginThrottle) + chỉ chuyển hướng về đường dẫn NỘI BỘ sau đăng nhập (SafeReturnUrl).
// =====================================================================
public static class AuthGuard
{
    /// <summary>[RS-C3] Tài khoản + nhóm đang hoạt động (dùng cả lúc đăng nhập để báo rõ, không cho vào rồi đá ra).</summary>
    public static bool AccountUsable(MES.Web.Data.Entities.User? u) => u != null && u.IsActive && u.Group != null && u.Group.IsActive;

    /// <summary>Phiên đăng nhập (claims) còn khớp DB không.</summary>
    public static async Task<bool> IsStillValidAsync(AppDbContext db, ClaimsPrincipal? p)
    {
        if (p?.Identity?.IsAuthenticated != true) return true;   // khách chưa đăng nhập: không có gì để kiểm
        if (!int.TryParse(p.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return false;
        var u = await db.Users.AsNoTracking().Include(x => x.Group).FirstOrDefaultAsync(x => x.UserId == uid);
        if (!AccountUsable(u)) return false;
        var name = p.FindFirst(ClaimTypes.Name)?.Value;
        if (!string.Equals(name, u.Username, StringComparison.OrdinalIgnoreCase)) return false;
        var role = p.FindFirst(ClaimTypes.Role)?.Value;
        if (role != null && !string.Equals(role, u.Group.GroupCode ?? "VIEWER", StringComparison.Ordinal)) return false;   // đổi nhóm → đăng nhập lại để lấy quyền mới
        return true;
    }

    /// <summary>[RS-C4] Chỉ cho chuyển hướng về đường dẫn trong MES (bắt đầu bằng "/", không "//" hay "/\") — chặn chuyển sang trang lạ.</summary>
    public static string SafeReturnUrl(string? u)
        => !string.IsNullOrWhiteSpace(u) && u.StartsWith('/') && !u.StartsWith("//") && !u.StartsWith("/\\") && !u.Contains("://")
           && !u.Any(char.IsControl) && !u.Contains('\\') ? u : "/";   // [RS-C4] + ký tự điều khiển (tab / xuống dòng trình duyệt bỏ qua → "//evil") và "\\"

}

/// <summary>[RS-C4 2026-10-30] Giới hạn đăng nhập sai: 5 lần sai / 15 phút cho 1 tên đăng nhập trên 1 máy (IP) → khoá 15 phút;
/// 50 lần sai / 15 phút từ 1 IP (thử nhiều tên) → khoá IP 15 phút. Đăng nhập đúng xoá đếm của tên đó. Lưu trong bộ nhớ (khởi động lại app là xoá).</summary>
public static class LoginThrottle
{
    public const int MaxPerUser = 5, MaxPerIp = 50;   // IP: cả xưởng có thể dùng chung 1 IP (NAT / tunnel) → ngưỡng cao
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15), LockFor = TimeSpan.FromMinutes(15);

    private sealed class Entry { public readonly List<DateTime> Fails = new(); public DateTime? LockedUntil; }
    private static readonly ConcurrentDictionary<string, Entry> _map = new();

    private static string KeyUser(string user, string ip) => "u|" + (user ?? "").Trim().ToLowerInvariant() + "|" + ip;
    private static string KeyIp(string ip) => "ip|" + ip;

    /// <summary>Đang bị khoá thì trả số phút còn lại (≥ 1), không thì null.</summary>
    public static int? LockedMinutes(string user, string ip, DateTime now)
    {
        int? Left(string k)
        {
            if (!_map.TryGetValue(k, out var e)) return null;
            lock (e) return e.LockedUntil > now ? (int)Math.Ceiling((e.LockedUntil.Value - now).TotalMinutes) : null;
        }
        var a = Left(KeyUser(user, ip)); var b = Left(KeyIp(ip));
        return a == null ? b : b == null ? a : Math.Max(a.Value, b.Value);
    }

    public static void Fail(string user, string ip, DateTime now)
    {
        void Add(string k, int max)
        {
            var e = _map.GetOrAdd(k, _ => new Entry());
            lock (e)
            {
                e.Fails.RemoveAll(t => now - t > Window);
                e.Fails.Add(now);
                if (e.Fails.Count >= max) { e.LockedUntil = now + LockFor; e.Fails.Clear(); }
            }
        }
        Add(KeyUser(user, ip), MaxPerUser);
        Add(KeyIp(ip), MaxPerIp);
        if (_map.Count > 5000)   // dọn mục cũ (không để bộ nhớ phình)
            foreach (var kv in _map)
                lock (kv.Value)
                    if (kv.Value.LockedUntil is not DateTime lu || lu < now) { kv.Value.Fails.RemoveAll(t => now - t > Window); if (kv.Value.Fails.Count == 0) _map.TryRemove(kv.Key, out _); }
    }

    public static void Success(string user, string ip) => _map.TryRemove(KeyUser(user, ip), out _);
}

/// <summary>[RS-C3 2026-10-30] Mạch Blazor đang mở: mỗi 1 phút đọc lại DB; tài khoản bị khoá / đổi nhóm → phiên hết hiệu lực
/// (AuthorizeRouteView đưa về trang đăng nhập) — không phải chờ cookie 8 giờ hết hạn.</summary>
public sealed class MesRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopes;
    public MesRevalidatingAuthStateProvider(ILoggerFactory lf, IServiceScopeFactory scopes) : base(lf) { _scopes = scopes; }
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await AuthGuard.IsStillValidAsync(db, state.User);
        }
        catch { return true; }   // lỗi đọc DB tạm thời: không đá người dùng ra giữa chừng (lần kiểm sau sẽ kiểm lại)
    }
}
