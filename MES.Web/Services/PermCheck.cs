using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

// =====================================================================
// [RS-C1 2026-10-29] KIỂM QUYỀN MA TRẬN Ở MÁY CHỦ (service) cho các trang danh mục
//   Trước: Thiết bị / Khách hàng / Dao / Đồ gá / WTS tiêu chuẩn chỉ cần đăng nhập là thêm / sửa / xoá
//   (trang chỉ có [Authorize], service không kiểm) → công nhân gõ URL là đổi được số máy / mã CV.
//   Nay: service tự đọc quyền của người thao tác từ DB (cùng luật UserService.GetAllUserPermissionsAsync:
//   user + nhóm phải đang hoạt động; nhóm ADMIN hoặc tên "admin" = toàn quyền; còn lại theo ma trận JSON
//   của nhóm), trang chỉ ẩn nút cho dễ nhìn. Không đọc claim (claim có thể cũ — CLAUDE-RULES mục 7).
// =====================================================================
public static class PermCheck
{
    /// <summary>Người dùng có quyền SỬA ít nhất 1 trong các mục <paramref name="sections"/> không.</summary>
    public static async Task<bool> CanEditAsync(AppDbContext db, int userId, params string[] sections)
    {
        var p = await ReadAsync(db, userId);
        if (p == null) return false;
        if (p.Value.IsAdmin) return true;
        return sections.Any(s => p.Value.Perms.Any(x => x.SectionCode == s && x.CanEdit));
    }

    /// <summary>Người dùng có quyền XEM (hoặc Sửa) ít nhất 1 trong các mục.</summary>
    public static async Task<bool> CanViewAsync(AppDbContext db, int userId, params string[] sections)
    {
        var p = await ReadAsync(db, userId);
        if (p == null) return false;
        if (p.Value.IsAdmin) return true;
        return sections.Any(s => p.Value.Perms.Any(x => x.SectionCode == s && (x.CanView || x.CanEdit)));
    }

    /// <summary>[RS-C2 2026-10-29] Nhóm giao nhận (KHO / GC / HTSP / KCS / PKG) người dùng có quyền SỬA theo ma trận
    /// (HANDOVER_KHO … HANDOVER_PKG; Admin = cả 5) — cùng cách trang Giao nhận tính _myEditGroups.</summary>
    public static async Task<HashSet<string>> HandoverEditGroupsAsync(AppDbContext db, int userId)
    {
        var res = new HashSet<string>();
        var p = await ReadAsync(db, userId);
        if (p == null) return res;
        foreach (var (grp, key) in HandoverGroupKey)
            if (p.Value.IsAdmin || p.Value.Perms.Any(x => x.SectionCode == key && x.CanEdit)) res.Add(grp);
        return res;
    }

    /// <summary>[VOID-PERM 2026-10-31] Như HandoverEditGroupsAsync nhưng trả null khi tài khoản / nhóm không hoạt động
    /// (để luật "người lập / người nhận tự huỷ" không áp cho tài khoản đã khoá).</summary>
    public static async Task<HashSet<string>?> HandoverEditGroupsOrNullAsync(AppDbContext db, int userId)
    {
        var p = await ReadAsync(db, userId);
        if (p == null) return null;
        var res = new HashSet<string>();
        foreach (var (grp, key) in HandoverGroupKey)
            if (p.Value.IsAdmin || p.Value.Perms.Any(x => x.SectionCode == key && x.CanEdit)) res.Add(grp);
        return res;
    }

    /// <summary>[RS-C2] Nhóm giao nhận → mục quyền (giống Handover_Index.GroupPermKey).</summary>
    public static readonly (string Group, string Key)[] HandoverGroupKey =
    {
        ("KHO",  SystemPermissions.HandoverKho),
        ("GC",   SystemPermissions.HandoverGc),
        ("HTSP", SystemPermissions.HandoverHtsp),
        ("KCS",  SystemPermissions.HandoverKcs),
        ("PKG",  SystemPermissions.HandoverPkg),
    };

    /// <summary>[ML16-ACT 2026-10-30] Nhóm WTS người dùng được nhập (GC / HTSP / KCS / PKG theo quyền Xem WTS_GC … WTS_DONG_GOI,
    /// cùng luật trang WTS WtsTasksForGroup). IsAdmin = thấy mọi mã. null = tài khoản / nhóm không hoạt động.</summary>
    public static async Task<(bool IsAdmin, HashSet<string> Groups)?> WtsProcessGroupsAsync(AppDbContext db, int userId)
    {
        var p = await ReadAsync(db, userId);
        if (p == null) return null;
        var res = new HashSet<string>();
        foreach (var (grp, key) in WtsGroupKey)
            if (p.Value.Perms.Any(x => x.SectionCode == key && (x.CanView || x.CanEdit))) res.Add(grp);
        return (p.Value.IsAdmin, res);
    }

    /// <summary>[ML16-ACT] ProcessGroup của cấu hình nhóm WTS → mục quyền nhập WTS.</summary>
    public static readonly (string Group, string Key)[] WtsGroupKey =
    {
        ("GC",   SystemPermissions.WtsGc),
        ("HTSP", SystemPermissions.WtsHtsp),
        ("KCS",  SystemPermissions.WtsKcs),
        ("PKG",  SystemPermissions.WtsDongGoi),
    };

    /// <summary>Lời báo khi không có quyền sửa (tên mục theo chữ trên menu).</summary>
    public static string NoEdit(string ten) => $"Bạn không có quyền Sửa mục {ten} (ma trận phân quyền) — nhờ Admin cấp quyền.";

    /// <summary>Lời báo khi không có quyền xem.</summary>
    public static string NoView(string ten) => $"Bạn không có quyền Xem mục {ten} (ma trận phân quyền) — nhờ Admin cấp quyền.";

    private static async Task<(bool IsAdmin, List<GroupPermissionSetting> Perms)?> ReadAsync(AppDbContext db, int userId)
    {
        if (userId <= 0) return null;
        var user = await db.Users.AsNoTracking().Include(u => u.Group).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive || user.Group == null || !user.Group.IsActive) return null;
        bool isAdmin = user.Group.GroupCode == "ADMIN" || (user.Username ?? "").ToLower() == "admin";
        List<GroupPermissionSetting> perms;
        try
        {
            perms = string.IsNullOrWhiteSpace(user.Group.Permissions)
                ? new List<GroupPermissionSetting>()
                : JsonSerializer.Deserialize<List<GroupPermissionSetting>>(user.Group.Permissions) ?? new();
        }
        catch { perms = new List<GroupPermissionSetting>(); }
        return (isAdmin, perms);
    }
}
