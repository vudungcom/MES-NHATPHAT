// [GÓI 23 16/11/2026] HarnessG23 — dữ liệu + điểm gọi cho drive_g23.py (quyền trang / PermHash / HIEU_SUAT_MAY / LenCheck / UiSafe).
//   POST /harness/g23-seed                 → tạo nhóm G23XEM (chỉ Xem Giao nhận Kho) + user g23xem; nhóm G23GC (Xem+Sửa WTS GC) + user g23gc;
//                                            nhóm G23HSM (Xem HIEU_SUAT_MAY + Xem Kế hoạch) + user g23hsm. Mật khẩu 123456. Gọi nhiều lần không tạo trùng.
//   POST /harness/g23-perm?group=G23GC&edit=0|1 → đổi ma trận quyền của nhóm NGOÀI trang Cài đặt (giả Admin lưu ma trận) — PermHash phải đá phiên cũ (bản mới).
//   POST /harness/g23-create-kh?user=g23xem → gọi KhPlanService.CreateAsync (DI thật) với user đó → trả { ok, error } — [KH-PERM-SVC] bản cũ ok=true là SAI.
//   Chỉ dùng chuỗi "HIEU_SUAT_MAY" (không SystemPermissions.HieuSuatMay) để bản cũ 84d9b9e vẫn biên dịch (ở bản cũ mục này không có → trang/menu không hiện: SAI là đúng).
//   Nguồn: CLAUDE quét Fable 15/11 (A3c P-01…P-05, A4 HIEU_SUAT_MAY); HÙNG 16/11 05:15 "không ảnh hưởng khách thì làm theo ý bạn".
using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class HarnessG23
{
    static string PermsVE(params (string Code, bool V, bool E)[] p)
        => JsonSerializer.Serialize(p.Select(x => new GroupPermissionSetting { SectionCode = x.Code, CanView = x.V, CanEdit = x.E }).ToList());

    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var hash = PasswordHasher.Hash("123456");
        async Task<UserGroup> G(string code, string name, string perms)
        {
            var g = db.UserGroups.List.FirstOrDefault(x => x.GroupCode == code);
            if (g == null) { g = new UserGroup { GroupCode = code, GroupName = name, IsActive = true, Permissions = perms }; db.UserGroups.Add(g); await db.SaveChangesAsync(); db.Fixup(); }
            return g;
        }
        async Task U(string user, string full, UserGroup g)
        {
            if (db.Users.List.Any(x => x.Username == user)) return;
            db.Users.Add(new User { Username = user, FullName = full, PasswordHash = hash, GroupId = g.GroupId, IsActive = true }); await db.SaveChangesAsync(); db.Fixup();
        }
        var gXem = await G("G23XEM", "G23 chỉ xem Giao nhận", PermsVE((SystemPermissions.HandoverKho, true, false)));
        var gGc  = await G("G23GC",  "G23 WTS GC",            PermsVE((SystemPermissions.WtsGc, true, true), (SystemPermissions.HandoverGc, true, true)));
        var gHsm = await G("G23HSM", "G23 xem hiệu suất máy", PermsVE(("HIEU_SUAT_MAY", true, false), (SystemPermissions.KeHoach, true, false)));
        await U("g23xem", "G23 xem", gXem); await U("g23gc", "G23 GC", gGc); await U("g23hsm", "G23 HSM", gHsm);
        return new { ok = true, groups = db.UserGroups.List.Where(g => g.GroupCode.StartsWith("G23")).Select(g => new { g.GroupId, g.GroupCode, g.Permissions }).ToList() };
    }

    public static async Task<object> SetPermAsync(AppDbContext db, string group, int edit)
    {
        var g = db.UserGroups.List.First(x => x.GroupCode == group);
        var old = g.Permissions;
        g.Permissions = group == "G23GC"
            ? PermsVE((SystemPermissions.WtsGc, true, edit == 1), (SystemPermissions.HandoverGc, true, edit == 1))
            : PermsVE((SystemPermissions.KeHoach, true, edit == 1));   // ADMIN (hoặc nhóm khác): đổi json để thử "không bị đá"
        db.UserGroups.Update(g); await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true, group, changed = old != g.Permissions, perms = g.Permissions };
    }

    // [UI-SAFE] giả lỗi ĐỌC DB chỉ khi câu đọc nằm trong KhPlanService (trang KH tải danh sách) — n lần; không đụng câu đọc của AuthGuard / quyền.
    static int _readN, _readThrown; static readonly object _l = new();
    public static object FailRead(int n)
    {
        lock (_l) { _readN = n; }
        if (n <= 0) { EntityFrameworkQueryableExtensions.OnQuery = null; return new { readLeft = 0, readThrown = _readThrown }; }
        EntityFrameworkQueryableExtensions.OnQuery = () =>
        {
            bool t = false;
            lock (_l) { if (_readN > 0 && Environment.StackTrace.Contains("KhPlanService")) { _readN--; _readThrown++; t = true; } }
            if (t) throw new InvalidOperationException("GIA-LOI-SQL-DOC-KH");
        };
        return new { readLeft = n, readThrown = _readThrown };
    }

    public static async Task<object> CreateKhAsync(AppDbContext db, KhPlanService kh, string user)
    {
        var u = db.Users.List.First(x => x.Username == user);
        var cust = db.Customers.List.First();
        var plan = new KhPlan { PlanDate = DateTime.Today, CustomerId = cust.CustomerId };
        plan.Details.Add(new KhPlanDetail { PartNo = "P-G23", PurchaseOrder = "PO-G23-" + user, Quantity = 3, STD = DateTime.Today });
        try { var id = await kh.CreateAsync(plan, u.UserId); return new { ok = true, id, error = "" }; }
        catch (Exception ex) { return new { ok = false, id = 0, error = ex.Message }; }
    }
}
