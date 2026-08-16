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
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;

        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Xác thực PIN của user (dùng cho các thao tác nhạy cảm như đổi default PartMaster).
    /// PIN lưu dạng plaintext ở field User.PIN (chỉ 4-6 số, so sánh trực tiếp).
    /// Không hash vì PIN ngắn, hash không tăng bảo mật.
    /// Trả về true nếu PIN đúng, false nếu sai hoặc user chưa set PIN.
    /// </summary>
    public async Task<bool> VerifyPinAsync(int userId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null) return false;
        if (string.IsNullOrEmpty(user.PIN)) return false;

        return user.PIN == pin.Trim();
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
            if (user.PIN != oldPin.Trim()) return (false, "PIN cũ không đúng");
        }

        user.PIN = newPin;
        await _db.SaveChangesAsync();
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