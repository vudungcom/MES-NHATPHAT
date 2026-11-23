// [QF-P-03 23/11/2026] HarnessP03 — dữ liệu + điểm gọi cho drive_p03.py (ghi DB của trang Part Master qua service, máy chủ kiểm quyền).
//   POST /harness/p03-seed           → nhóm P03GC (Xem + Sửa PART_GC) + user p03gc (mật khẩu 123456). Gọi nhiều lần không tạo trùng.
//   POST /harness/p03-perm?edit=0|1  → đổi quyền Sửa PART_GC của nhóm P03GC NGOÀI trang (giả Admin rút quyền giữa phiên, trang vẫn mở với cờ cũ).
//   GET  /harness/p03                → máy loại trừ + máy đồng dạng đang hiệu lực của PART-ML9 + cờ Xác nhận OK.
//   Chỉ dùng entity / hằng có từ trước → biên dịch được cả bản cũ 040bc56 (ở bản cũ trang ghi thẳng Db: P1 / P3 SAI là đúng).
//   Nguồn: CLAUDE quét Fable 14/11 (P-03); HÙNG 23/11 13:20 OK.
using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessP03
{
    static string P(bool edit) => JsonSerializer.Serialize(new List<GroupPermissionSetting> { new GroupPermissionSetting { SectionCode = SystemPermissions.PartGc, CanView = true, CanEdit = edit } });

    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var g = db.UserGroups.List.FirstOrDefault(x => x.GroupCode == "P03GC");
        if (g == null) { g = new UserGroup { GroupCode = "P03GC", GroupName = "P03 Part GC", IsActive = true, Permissions = P(true) }; db.UserGroups.Add(g); await db.SaveChangesAsync(); db.Fixup(); }
        if (!db.Users.List.Any(x => x.Username == "p03gc"))
        { db.Users.Add(new User { Username = "p03gc", FullName = "P03 GC", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true }); await db.SaveChangesAsync(); db.Fixup(); }
        return new { ok = true, g.GroupId };
    }

    public static async Task<object> SetPermAsync(AppDbContext db, int edit)
    {
        var g = db.UserGroups.List.First(x => x.GroupCode == "P03GC");
        g.Permissions = P(edit == 1);
        db.UserGroups.Update(g); await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true, edit };
    }

    public static object Dump(AppDbContext db)
    {
        var part = db.PartMasters.List.First(p => p.PartNo == "PART-ML9");
        var stepIds = db.PartMachiningSteps.List.Where(s => s.PartId == part.PartId).Select(s => s.StepId).ToHashSet();
        return new
        {
            exclude = db.MachineExcludeSettings.List.Select(x => x.SoMay).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            timings = db.PartMachiningTimings.List.Where(t => stepIds.Contains(t.StepId) && t.IsActive).Select(t => new { t.TimingId, t.StepId, t.SoMay, t.SetupTime, t.UpdatedBy }).ToList(),
            logs = db.PartProcessStepChangeLogs.List.Count(l => l.PartId == part.PartId),
            machiningOk = part.IsMachiningConfirmed,
        };
    }
}
