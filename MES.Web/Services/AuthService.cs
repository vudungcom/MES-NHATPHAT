using System.Collections.Concurrent;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class AuthService
{
    private readonly AppDbContext _db;

    public AuthService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> VerifyCredentialsAsync(string username, string password)
    {
        var user = await _db.Users
            .Include(u => u.Group)
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null) return null;
        // [LOGIN-TRIM 2026-11-20] Mật khẩu khởi tạo / đặt lại được Trim khi lưu (BM2-04, ADMIN-RESET-MK) nhưng đăng nhập so nguyên văn → người gõ đúng
        //   bản Admin đưa (có dấu cách đầu / cuối) bị báo sai. Nay: so nguyên văn trước, sai mà bản Trim khác thì so thêm bản Trim (mật khẩu cũ có
        //   dấu cách đầu / cuối vẫn đăng nhập được bằng nguyên văn). Vẫn tính 1 lần sai (LoginThrottle ở Program.cs). Không đổi mật khẩu trong DB.
        //   Nguồn: CLAUDE rà độc lập Gói 22 15/11 (G22-GHI-NHO 3); HÙNG 20/11 22:51 OK Gói 28 (câu B3).
        var pwOk = PasswordHasher.Verify(password, user.PasswordHash);
        if (!pwOk && password != null && password.Trim() != password && password.Trim().Length > 0)
            pwOk = PasswordHasher.Verify(password.Trim(), user.PasswordHash);
        if (!pwOk) return null;

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return user;
    }

    // =====================================================================
    // [PIN-HASH 2026-11-16] (P-05 / C-02) PIN lưu BĂM (PasswordHasher PBKDF2, cùng cách mật khẩu) — trước: chữ rõ ở Users.PIN, ai đọc DB / file .bak
    //   thấy PIN mọi người. PIN cũ còn chữ rõ (4–6 chữ số) vẫn so được; gõ đúng lần đầu thì tự nâng lên hash (cần cột PIN ≥ 95 ký tự — SQL 16;
    //   Nguồn: CLAUDE quét Fable 15/11 (A3C P-05 / C-02 Trung); Hùng 16/11 11:34 OK Gói 24 (câu B1 SQL 16 chờ duyệt).
    //   cột còn 20 thì ghi hash lỗi → giữ chữ rõ, ghi log, không làm hỏng thao tác). SetPinAsync luôn ghi hash. Reset PIN (UserService) giữ như cũ (= null).
    // [PIN-THROTTLE 2026-11-16] (P-05) Đếm sai theo userId trong bộ nhớ: 5 lần sai / 15 phút → khoá 15 phút (y LoginThrottle); gõ đúng xoá đếm.
    //   Trước: thử PIN không giới hạn → ngồi máy bỏ quên dò hết 10^4 trong vài phút. Áp cho mọi chỗ gọi VerifyPinAsync + ô "PIN" Xoá PO / sửa Kho
    //   Nguồn: CLAUDE quét Fable 15/11 (A3C P-05 / C-02 Trung); Hùng 16/11 11:34 OK Gói 24.
    //   (so mật khẩu đăng nhập — VerifyPasswordForActionAsync; chữ "PIN" giữ nguyên, chờ KHO-PIN).
    // =====================================================================

    /// <summary>[PIN-HASH] Chuỗi PIN trong DB là hash (PasswordHasher) hay chữ rõ cũ.</summary>
    public static bool IsPinHashed(string? stored) => !string.IsNullOrEmpty(stored) && stored.StartsWith("PBKDF2.", StringComparison.Ordinal);

    /// <summary>[PIN-HASH] So PIN người gõ với chuỗi trong DB (hash → PasswordHasher.Verify; chữ rõ cũ → so chữ). Dùng chung cho app + bộ kiểm.</summary>
    public static bool PinMatches(string? stored, string? pin)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrWhiteSpace(pin)) return false;
        var p = pin.Trim();
        return IsPinHashed(stored) ? PasswordHasher.Verify(p, stored) : stored == p;
    }

    /// <summary>[PIN-THROTTLE] Lời báo của lần VerifyPinAsync / VerifyPasswordForActionAsync gần nhất khi SAI hoặc đang khoá (null khi đúng / chưa gọi).
    /// Chỗ gọi hiện lời này thay câu "PIN không đúng" cứng để người dùng biết còn mấy lần / khoá tới khi nào.</summary>
    public string? LastPinError { get; private set; }

    /// <summary>[PIN-THROTTLE] Câu báo PIN sai cho chỗ gọi: ưu tiên lời throttle (còn N lần / đang khoá), không có thì câu cũ của trang / service.</summary>
    public string PinErrorText(string fallback) => LastPinError ?? fallback;

    /// <summary>
    /// Xác thực PIN của user (dùng cho các thao tác nhạy cảm như đổi default PartMaster).
    /// [PIN-HASH] PIN lưu hash (PBKDF2); PIN cũ chữ rõ vẫn nhận rồi nâng lên hash. [PIN-THROTTLE] 5 lần sai / 15 phút theo userId → khoá 15 phút.
    /// Trả về true nếu PIN đúng, false nếu sai / đang khoá / user chưa set PIN (xem LastPinError).
    /// </summary>
    public async Task<bool> VerifyPinAsync(int userId, string pin)
    {
        LastPinError = null;
        if (string.IsNullOrWhiteSpace(pin)) { LastPinError = "Nhập PIN."; return false; }
        var now = DateTime.Now;
        if (PinThrottle.LockedMinutes(userId, now) is int wait)
        { LastPinError = $"PIN sai quá {PinThrottle.MaxFails} lần — đang khoá, thử lại sau {wait} phút."; return false; }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null) { LastPinError = "Tài khoản không còn hoạt động."; return false; }
        if (string.IsNullOrEmpty(user.PIN)) { LastPinError = "Tài khoản chưa đặt PIN (vào Hồ sơ để đặt)."; return false; }

        if (!PinMatches(user.PIN, pin))
        {
            PinThrottle.Fail(userId, now);
            LastPinError = PinThrottle.LockedMinutes(userId, now) is int w2
                ? $"PIN sai {PinThrottle.MaxFails} lần — khoá {w2} phút."
                : $"PIN không đúng (còn {PinThrottle.Remaining(userId, now)} lần thử).";
            return false;
        }
        PinThrottle.Success(userId);
        if (!IsPinHashed(user.PIN)) await TryUpgradePinAsync(user, pin.Trim());   // [PIN-HASH] nâng chữ rõ → hash sau lần gõ đúng đầu tiên
        return true;
    }

    /// <summary>[PIN-THROTTLE] Ô "PIN" Xoá PO / sửa Kho đang so MẬT KHẨU đăng nhập (KHO-PIN chờ khách) — qua cùng bộ đếm sai theo userId.
    /// Trả true khi đúng; false + LastPinError khi sai / đang khoá.</summary>
    public async Task<bool> VerifyPasswordForActionAsync(int userId, string? password)
    {
        LastPinError = null;
        if (string.IsNullOrWhiteSpace(password)) { LastPinError = "Nhập PIN."; return false; }
        var now = DateTime.Now;
        // rà độc lập 16/11: đếm RIÊNG (khoá -userId) — gõ nhầm PIN thật vào ô này 5 lần không khoá PIN ở Thiết bị / Đồ gá / Part Master
        var key = -userId;
        if (PinThrottle.LockedMinutes(key, now) is int wait)
        { LastPinError = $"Sai quá {PinThrottle.MaxFails} lần — đang khoá, thử lại sau {wait} phút."; return false; }
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null || !PasswordHasher.Verify(password.Trim(), user.PasswordHash))
        {
            PinThrottle.Fail(key, now);
            LastPinError = PinThrottle.LockedMinutes(key, now) is int w2
                ? $"Sai {PinThrottle.MaxFails} lần — khoá {w2} phút."
                : $"PIN không đúng. Vui lòng thử lại (còn {PinThrottle.Remaining(key, now)} lần).";
            return false;
        }
        PinThrottle.Success(key);
        return true;
    }

    /// <summary>[PIN-HASH] Ghi hash thay chữ rõ. Cột PIN còn 20 ký tự (chưa chạy SQL 16) → DB từ chối → giữ chữ rõ, tách bản EF, không ném.
    /// Trả true khi đã nâng.</summary>
    public async Task<bool> TryUpgradePinAsync(User user, string plainPin)
    {
        var old = user.PIN;
        if (_db.ChangeTracker.HasChanges()) return false;   // rà độc lập 16/11: DbContext dùng chung đang có sửa đổi chưa lưu của trang → không SaveChanges "ké", nâng ở lần gõ sau
        try
        {
            user.PIN = PasswordHasher.Hash(plainPin);
            await _db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            user.PIN = old;
            try { _db.Entry(user).State = EntityState.Unchanged; } catch { }
            PinUpgradeLog?.Invoke($"[PIN-HASH] Chưa nâng PIN sang hash cho user {user.UserId} (cột Users.PIN còn hẹp? chạy tools/sql/16_PIN_HASH.sql): {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>[PIN-HASH] Nơi ghi log khi nâng hash thất bại (Program.cs gán Console / ILogger); null = im lặng (bộ kiểm).</summary>
    public static Action<string>? PinUpgradeLog { get; set; } = Console.WriteLine;

    /// <summary>[PIN-HASH] Lúc khởi động: nâng mọi PIN còn chữ rõ sang hash (mỗi user 1 lần lưu; lỗi 1 user thì dừng — cột chưa đủ rộng). Trả số user đã nâng.</summary>
    public static async Task<int> UpgradeAllPlainPinsAsync(AppDbContext db)
    {
        var users = await db.Users.Where(u => u.PIN != null && u.PIN != "" && !u.PIN.StartsWith("PBKDF2.")).ToListAsync();
        int n = 0;
        var svc = new AuthService(db);
        foreach (var u in users)
        {
            if (!await svc.TryUpgradePinAsync(u, u.PIN!.Trim())) break;   // lỗi cột hẹp sẽ lặp y nhau → dừng sớm, log 1 dòng
            n++;
        }
        return n;
    }

    /// <summary>
    /// Kiểm tra user đã set PIN chưa.
    /// </summary>
    public async Task<bool> HasPinAsync(int userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        return user != null && !string.IsNullOrEmpty(user.PIN);
    }

    /// <summary>
    /// Đổi PIN. Nếu user chưa có PIN, chỉ cần newPin. Nếu đã có, phải verify oldPin trước.
    /// Trả về (success, errorMessage).
    /// </summary>
    public async Task<(bool Success, string? Error)> SetPinAsync(int userId, string? oldPin, string newPin)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null) return (false, "User không tồn tại");

        // Validate newPin
        if (string.IsNullOrWhiteSpace(newPin)) return (false, "Nhập PIN mới");
        newPin = newPin.Trim();
        if (newPin.Length < 4 || newPin.Length > 6) return (false, "PIN phải 4-6 chữ số");
        if (!newPin.All(char.IsDigit)) return (false, "PIN chỉ chứa chữ số");

        // Nếu đã có PIN → verify oldPin
        if (!string.IsNullOrEmpty(user.PIN))
        {
            if (string.IsNullOrWhiteSpace(oldPin)) return (false, "Nhập PIN cũ");
            // [PIN-THROTTLE] đổi PIN cũng đếm sai theo userId (đoán PIN cũ qua ô Hồ sơ)
            var now = DateTime.Now;
            if (PinThrottle.LockedMinutes(userId, now) is int wait) return (false, $"PIN sai quá {PinThrottle.MaxFails} lần — đang khoá, thử lại sau {wait} phút.");
            if (!PinMatches(user.PIN, oldPin)) { PinThrottle.Fail(userId, now); return (false, "PIN cũ không đúng"); }
            PinThrottle.Success(userId);
        }

        // [PIN-HASH] ghi hash; cột chưa đủ rộng (chưa chạy SQL 16) → lưu chữ rõ như trước để người dùng vẫn đặt được PIN
        var hashed = PasswordHasher.Hash(newPin);
        user.PIN = hashed;
        try { await _db.SaveChangesAsync(); }
        catch (Exception ex)
        {
            try { _db.Entry(user).State = EntityState.Unchanged; } catch { }
            PinUpgradeLog?.Invoke($"[PIN-HASH] Chưa ghi được PIN dạng hash cho user {userId} (chạy tools/sql/16_PIN_HASH.sql): {ex.GetType().Name} — lưu dạng cũ.");
            user.PIN = newPin;
            await _db.SaveChangesAsync();
        }
        return (true, null);
    }

    /// <summary>
    /// Đổi password. Phải verify password cũ trước.
    /// </summary>
    public async Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null) return (false, "User không tồn tại");

        if (string.IsNullOrWhiteSpace(oldPassword)) return (false, "Nhập password cũ");
        if (!PasswordHasher.Verify(oldPassword, user.PasswordHash))
            return (false, "Password cũ không đúng");

        if (string.IsNullOrWhiteSpace(newPassword)) return (false, "Nhập password mới");
        newPassword = newPassword.Trim();
        if (newPassword.Length < 6) return (false, "Password mới tối thiểu 6 ký tự");
        if (newPassword == oldPassword) return (false, "Password mới trùng password cũ");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.SaveChangesAsync();
        return (true, null);
    }
}

/// <summary>[PIN-THROTTLE 2026-11-16] Giới hạn gõ PIN sai theo userId (bộ nhớ, mất khi khởi động lại app — như LoginThrottle):
/// 5 lần sai / 15 phút → khoá 15 phút; gõ đúng xoá đếm. Dùng cho VerifyPinAsync, SetPinAsync (PIN cũ) và ô "PIN" so mật khẩu (Xoá PO / sửa Kho).</summary>
public static class PinThrottle
{
    public const int MaxFails = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15), LockFor = TimeSpan.FromMinutes(15);

    private sealed class Entry { public readonly List<DateTime> Fails = new(); public DateTime? LockedUntil; }
    private static readonly ConcurrentDictionary<int, Entry> _map = new();

    /// <summary>Đang khoá → số phút còn lại (≥ 1); không → null.</summary>
    public static int? LockedMinutes(int userId, DateTime now)
    {
        if (!_map.TryGetValue(userId, out var e)) return null;
        lock (e) return e.LockedUntil > now ? (int)Math.Ceiling((e.LockedUntil.Value - now).TotalMinutes) : null;
    }

    /// <summary>Số lần còn được sai trước khi khoá (0 khi đang khoá).</summary>
    public static int Remaining(int userId, DateTime now)
    {
        if (!_map.TryGetValue(userId, out var e)) return MaxFails;
        lock (e)
        {
            if (e.LockedUntil > now) return 0;
            e.Fails.RemoveAll(t => now - t > Window);
            return Math.Max(0, MaxFails - e.Fails.Count);
        }
    }

    public static void Fail(int userId, DateTime now)
    {
        var e = _map.GetOrAdd(userId, _ => new Entry());
        lock (e)
        {
            e.Fails.RemoveAll(t => now - t > Window);
            e.Fails.Add(now);
            if (e.Fails.Count >= MaxFails) { e.LockedUntil = now + LockFor; e.Fails.Clear(); }
        }
    }

    public static void Success(int userId) => _map.TryRemove(userId, out _);

    /// <summary>Bộ kiểm: xoá hết đếm.</summary>
    public static void ResetAll() => _map.Clear();
}
