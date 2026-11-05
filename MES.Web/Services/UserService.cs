using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace MES.Web.Services;

public class UserService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public UserService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    // ==========================================
    // QUẢN LÝ NHÓM & PHÂN QUYỀN
    // ==========================================
    public async Task<List<UserGroup>> GetAllGroupsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserGroups.OrderBy(x => x.GroupId).ToListAsync();
    }

    // [USER-PERM-SVC 2026-11-03] Tạo / sửa nhóm, ma trận quyền, người dùng: máy chủ tự kiểm người thao tác là ADMIN (đọc DB qua PermCheck).
    //   Trước: chỉ trang Cài đặt kiểm bằng claim (claim cũ — Admin bị hạ nhóm vẫn còn quyền tới lần đăng nhập sau; CLAUDE-RULES mục 7),
    //   5 hàm dưới không kiểm gì — cùng loại lỗ RS-C1. Nguồn: CLAUDE phát hiện khi viết bộ kiểm 02/11 (sổ PHAT-HIEN-0211 mục 2).
    public const string NotAdminMsg = "Chỉ ADMIN (đang hoạt động) mới được quản lý người dùng / nhóm / phân quyền — nhờ Admin thực hiện.";

    /// <summary>[USER-PERM-SVC] Trang Cài đặt hỏi quyền theo DB (không theo claim).</summary>
    public async Task<bool> IsAdminAsync(int userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PermCheck.IsAdminAsync(db, userId);
    }

    public async Task<(bool Success, string? Error)> CreateGroupAsync(UserGroup group, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);   // [USER-PERM-SVC]
        group.GroupCode = group.GroupCode.Trim().ToUpper();
        if (await db.UserGroups.AnyAsync(x => x.GroupCode == group.GroupCode))
            return (false, "Mã nhóm đã tồn tại.");

        db.UserGroups.Add(group);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateGroupAsync(UserGroup group, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);   // [USER-PERM-SVC]
        var dbGroup = await db.UserGroups.FindAsync(group.GroupId);
        if (dbGroup == null) return (false, "Không tìm thấy Nhóm.");
        // [USER-PERM-SVC] (rà độc lập T7) tắt nhóm ADMIN = không còn ai quản trị (PermCheck đòi nhóm đang hoạt động) → máy chủ chặn
        if (dbGroup.GroupCode == "ADMIN" && !group.IsActive) return (false, "Không được ngưng nhóm ADMIN (sẽ không còn ai quản trị hệ thống).");

        dbGroup.GroupName = group.GroupName.Trim();
        dbGroup.IsActive = group.IsActive;

        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateGroupPermissionsAsync(int groupId, List<GroupPermissionSetting> perms, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);   // [USER-PERM-SVC]
        var group = await db.UserGroups.FindAsync(groupId);
        if (group == null) return (false, "Không tìm thấy Nhóm.");

        group.Permissions = JsonSerializer.Serialize(perms);
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// [SET-MATRIX-1TX 2026-11-05] Lưu ma trận quyền MỌI nhóm trong 1 giao dịch: lỗi ở nhóm nào thì không nhóm nào đổi.
    ///   Trước: trang Cài đặt gọi UpdateGroupPermissionsAsync từng nhóm, mỗi lần 1 lần lưu → lỗi giữa chừng thì nửa trên đã lưu, nửa dưới chưa
    ///   (PHAT-HIEN-0311 mục 4). Nhóm ADMIN không lưu ở đây (ADMIN luôn toàn quyền). Ô "Cài đặt &amp; Phân quyền" (SETTING_ADMIN) bị bỏ khỏi
    ///   ma trận nhóm thường (mục 5: trang Cài đặt chỉ cho ADMIN, ô này làm menu hiện mà trang từ chối) — ghi đè thì bỏ luôn ô cũ còn trong DB.
    ///   Nguồn: CLAUDE (rà độc lập gói USER-PERM-SVC 03/11; Hùng 05/11 10:11 cho làm).
    /// </summary>
    public async Task<(bool Success, string? Error)> UpdateAllGroupPermissionsAsync(Dictionary<int, List<GroupPermissionSetting>> permsByGroup, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);
        using var trx = await db.Database.BeginTransactionAsync();
        var ids = permsByGroup.Keys.ToList();
        var groups = await db.UserGroups.Where(g => ids.Contains(g.GroupId)).ToListAsync();
        foreach (var id in ids)
        {
            var g = groups.FirstOrDefault(x => x.GroupId == id);
            if (g == null) return (false, $"Không tìm thấy nhóm (GroupId {id}) — có thể vừa bị xoá. Tải lại trang rồi lưu lại; chưa nhóm nào được đổi.");
            if (g.GroupCode == "ADMIN") continue;
            var perms = permsByGroup[id].Where(x => x.SectionCode != SystemPermissions.SettingAdmin).ToList();
            g.Permissions = JsonSerializer.Serialize(perms);
        }
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return (true, null);
    }

    public List<GroupPermissionSetting> GetGroupPermissions(string? jsonPermissions)
    {
        if (string.IsNullOrWhiteSpace(jsonPermissions)) return new List<GroupPermissionSetting>();
        try
        {
            return JsonSerializer.Deserialize<List<GroupPermissionSetting>>(jsonPermissions) ?? new();
        }
        catch
        {
            return new List<GroupPermissionSetting>();
        }
    }

    /// <summary>
    /// Đọc ma trận quyền an toàn đa luồng cho User
    /// </summary>
    public async Task<Dictionary<string, (bool CanView, bool CanEdit)>> GetAllUserPermissionsAsync(int userId)
    {
        var result = new Dictionary<string, (bool CanView, bool CanEdit)>();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var user = await db.Users.Include(u => u.Group).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive || user.Group == null || !user.Group.IsActive)
            return result;

        bool isAdmin = user.Group.GroupCode == "ADMIN" || user.Username.ToLower() == "admin";
        var perms = GetGroupPermissions(user.Group.Permissions);

        foreach (var sec in SystemPermissions.Sections)
        {
            if (isAdmin)
            {
                result[sec.Code] = (true, true);
            }
            else
            {
                var p = perms.FirstOrDefault(x => x.SectionCode == sec.Code);
                result[sec.Code] = (p?.CanView ?? false, p?.CanEdit ?? false);
            }
        }
        return result;
    }

    // ==========================================
    // QUẢN LÝ USER
    // ==========================================
    public async Task<List<User>> GetAllUsersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Include(u => u.Group).OrderBy(u => u.UserId).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(User user, string plainPassword, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);   // [USER-PERM-SVC]
        if (await db.Users.AnyAsync(x => x.Username == user.Username.Trim()))
            return (false, "Tên đăng nhập đã tồn tại.");

        user.Username = user.Username.Trim();
        user.PasswordHash = PasswordHasher.Hash(plainPassword);
        user.CreatedAt = DateTime.Now;
        user.IsActive = true;

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateUserAsync(User user, int actorUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await PermCheck.IsAdminAsync(db, actorUserId)) return (false, NotAdminMsg);   // [USER-PERM-SVC]
        var dbUser = await db.Users.FindAsync(user.UserId);
        if (dbUser == null) return (false, "Không tìm thấy User.");
        // [USER-PERM-SVC] (rà độc lập V1) khoá / đổi nhóm làm hệ thống không còn ADMIN đang hoạt động nào → chặn (trước: chỉ cứu được bằng SQL)
        var adminGroupIds = await db.UserGroups.Where(g => g.GroupCode == "ADMIN" && g.IsActive).Select(g => g.GroupId).ToListAsync();
        bool IsAdminRow(int? groupId, string? username, bool active)
            => active && ((groupId.HasValue && adminGroupIds.Contains(groupId.Value)) || (username ?? "").ToLower() == "admin");
        if (IsAdminRow(dbUser.GroupId, dbUser.Username, dbUser.IsActive) && !IsAdminRow(user.GroupId, dbUser.Username, user.IsActive))
        {
            var others = await db.Users.Where(u => u.UserId != dbUser.UserId && u.IsActive).Select(u => new { u.GroupId, u.Username }).ToListAsync();
            if (!others.Any(o => IsAdminRow(o.GroupId, o.Username, true)))
                return (false, "Không lưu: đây là tài khoản ADMIN đang hoạt động cuối cùng — khoá / đổi nhóm sẽ không còn ai quản trị. Tạo / bật ADMIN khác trước.");
            if (dbUser.UserId == actorUserId)
                return (false, "Không tự khoá hoặc tự rời nhóm ADMIN của chính mình — nhờ ADMIN khác thực hiện.");
        }

        dbUser.FullName = user.FullName;
        dbUser.GroupId = user.GroupId;
        // [C6-PIN 2026-10-28] Không ghi đè PIN khi sửa thông tin user (form sửa không có ô PIN → trước: đổi nhóm / họ tên là mất PIN).
        //   Đổi PIN: user tự đặt ở Hồ sơ; xoá PIN: nút Reset PIN (ResetPinAsync).
        dbUser.IsActive = user.IsActive;

        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Kiểm tra user có CanDelete = true cho 1 section cụ thể không.
    /// Dùng cho các quyền đặc biệt chưa có trong tuple (CanView, CanEdit).
    /// ADMIN luôn trả về true.
    /// </summary>
    public async Task<bool> GetCanDeleteAsync(int userId, string sectionCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.Include(u => u.Group)
            .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        if (user == null || user.Group == null) return false;

        // ADMIN luôn có toàn quyền
        if (user.Group.GroupCode == "ADMIN" || user.Username.ToLower() == "admin")
            return true;

        if (string.IsNullOrEmpty(user.Group.Permissions)) return false;
        try
        {
            var perms = System.Text.Json.JsonSerializer
                .Deserialize<List<GroupPermissionSetting>>(user.Group.Permissions);
            var p = perms?.FirstOrDefault(x => x.SectionCode == sectionCode);
            return p?.CanDelete ?? false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Admin reset PIN của user về null.
    /// User sẽ cần vào trang Profile để tự đặt PIN mới.
    /// Chỉ ADMIN mới được gọi method này (kiểm tra ở UI + service).
    /// </summary>
    public async Task<(bool Success, string? Error)> ResetPinAsync(int targetUserId, int requestedByUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify người yêu cầu phải là ADMIN
        var requester = await db.Users.Include(u => u.Group)
            .FirstOrDefaultAsync(u => u.UserId == requestedByUserId && u.IsActive);
        if (requester == null) return (false, "Không xác định được người thực hiện.");
        // [USER-PERM-SVC] (rà độc lập T6) cùng luật PermCheck (tài khoản + nhóm đang hoạt động) — trước: nhóm bị tắt mà tên "admin" vẫn qua
        if (!await PermCheck.IsAdminAsync(db, requestedByUserId)) return (false, "Chỉ ADMIN mới có thể reset PIN.");

        var target = await db.Users.FirstOrDefaultAsync(u => u.UserId == targetUserId);
        if (target == null) return (false, "Không tìm thấy User.");

        target.PIN = null;
        await db.SaveChangesAsync();
        return (true, null);
    }
}
