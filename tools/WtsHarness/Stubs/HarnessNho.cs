// [GÓI NHỎ 20/11/2026] HarnessNho — dữ liệu cho drive_nho.py ([HOME-THE-QUYEN] thẻ Trang chủ theo quyền, [NAV-XEM-SUA] menu Xem ∨ Sửa,
//   [VIEW-DOI-HOI] hỏi trước khi đổi "Xem WTS của" lúc form gõ dở). Nguồn: CLAUDE rà độc lập Gói 23 (L2, L3) + Gói 25 (R-09); Hùng 20/11 18:12 / 18:32.
//   POST /harness/nho-seed → nhóm G26NHO: chỉ ô SỬA Đồ gá + SỬA Kho (không tick Xem) + Xem Hiệu suất máy; user nho01 / 123456 "Nho sửa đồ gá".
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using MES.Web.Constants;

public static class HarnessNho
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var g = db.UserGroups.List.FirstOrDefault(x => x.GroupCode == "G26NHO");
        if (g == null)
        {
            var perms = JsonSerializer.Serialize(new List<GroupPermissionSetting>
            {
                new() { SectionCode = "DO_GA", CanView = false, CanEdit = true },
                new() { SectionCode = "HIEU_SUAT_MAY", CanView = true, CanEdit = false },
                new() { SectionCode = "KHO", CanView = false, CanEdit = true },
            });
            g = new UserGroup { GroupCode = "G26NHO", GroupName = "Nhỏ: chỉ Sửa đồ gá", IsActive = true, Permissions = perms };
            db.UserGroups.Add(g); await db.SaveChangesAsync(); db.Fixup();
        }
        if (!db.Users.List.Any(x => x.Username == "nho01"))
        {
            db.Users.Add(new User { Username = "nho01", FullName = "Nho sửa đồ gá", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { ok = true, group = g.GroupId };
    }
}
