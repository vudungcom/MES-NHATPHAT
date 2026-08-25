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

    public async Task<(bool Success, string? Error)> CreateGroupAsync(UserGroup group)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        group.GroupCode = group.GroupCode.Trim().ToUpper();
        if (await db.UserGroups.AnyAsync(x => x.GroupCode == group.GroupCode))
            return (false, "Mã nhóm đã tồn tại.");

        db.UserGroups.Add(group);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateGroupAsync(UserGroup group)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbGroup = await db.UserGroups.FindAsync(group.GroupId);
        if (dbGroup == null) return (false, "Không tìm thấy Nhóm.");

        dbGroup.GroupName = group.GroupName.Trim();
        dbGroup.IsActive = group.IsActive;

        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<bool> UpdateGroupPermissionsAsync(int groupId, List<GroupPermissionSetting> perms)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var group = await db.UserGroups.FindAsync(groupId);
        if (group == null) return false;

        group.Permissions = JsonSerializer.Serialize(perms);
        await db.SaveChangesAsync();
        return true;
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

    public async Task<(bool Success, string? Error)> CreateUserAsync(User user, string plainPassword)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

    public async Task<(bool Success, string? Error)> UpdateUserAsync(User user)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbUser = await db.Users.FindAsync(user.UserId);
        if (dbUser == null) return (false, "Không tìm thấy User.");

        dbUser.FullName = user.FullName;
        dbUser.GroupId = user.GroupId;
        dbUser.PIN = user.PIN;
        dbUser.IsActive = user.IsActive;

        await db.SaveChangesAsync();
        return (true, null);
    }
}