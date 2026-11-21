// [GÓI 28 20/11/2026] HarnessG28 — điểm gọi cho drive_g28.py (sửa lỗi ngầm):
//   POST /harness/g28-wf?po=&on=0|1        → đổi Workflow qua KhPlanService.SaveEditAsync THẬT (đường trang Sửa phiếu KH, user admin) — bản mới tự báo HandoverChangeNotifier
//                                            [G21-WF-NOTIFY]; bản cũ không báo → trang Giao nhận đang mở không tự cập nhật (SAI là đúng).
//   POST /harness/g28-fail-login?n=        → n lần câu đọc DB trong AuthService.VerifyCredentialsAsync ném lỗi (giả mất kết nối SQL lúc đăng nhập) [LOGIN-LOI-DB].
//   POST /harness/g28-seed                 → nhóm G28M (chỉ Sửa WTS_MASTER) + user g28m (mật khẩu 123456); gọi nhiều lần không tạo trùng.
//   POST /harness/g28-perm?edit=0|1        → đổi quyền WTS_MASTER của nhóm G28M ngoài trang (giả Admin rút / cấp quyền) [R08-CANCU].
//   Nguồn: CLAUDE rà độc lập Gói 21 / 22 / 25 (G21-GHI-NHO 1 + 3, G22-GHI-NHO 6, G25-GHI-NHO R-08); HÙNG 20/11 22:51 OK Gói 28.
using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class HarnessG28
{
    public static async Task<object> SetWfAsync(AppDbContext db, KhPlanService kh, string po, int on)
    {
        var d = db.KhPlanDetails.List.First(x => x.PurchaseOrder == po);
        var p = db.KhPlans.List.First(x => x.KhPlanId == d.KhPlanId);
        var admin = db.Users.List.First(x => x.Username == "admin");
        // rà độc lập Gói 28 L8: đi ĐÚNG đường trang Sửa phiếu KH dùng (SaveEditAsync), không phải SetApplyWorkflowAsync (không trang nào gọi)
        KhPlanService.KhEditValues EV() => new()
        {
            PlanNotes = p.Notes, ReceivedDate = p.ReceivedDate, ApplyWorkflow = p.ApplyWorkflow, Quantity = d.Quantity, Unit = d.Unit, STD = d.STD, Type = d.Type,
            Priority = d.Priority, Material = d.Material, MaterialConfig = d.MaterialConfig, Location = d.Location, MaterialNotes = d.MaterialNotes, OrderVL = d.OrderVL,
            MaterialCondition = d.MaterialCondition, PartNotes = d.PartNotes
        };
        var o0 = EV(); var n0 = EV(); n0.ApplyWorkflow = on == 1;
        var err = await kh.SaveEditAsync(d.KhPlanId, d.KhPlanDetailId, o0, n0, admin.UserId, "harness g28 đổi Workflow");
        db.Fixup();
        return new { ok = err == null, error = err ?? "", on = db.KhPlans.List.First(p => p.KhPlanId == d.KhPlanId).ApplyWorkflow };
    }

    static int _loginN, _loginThrown; static readonly object _l = new();
    public static object FailLogin(int n)
    {
        lock (_l) { _loginN = n; }
        if (n <= 0) { EntityFrameworkQueryableExtensions.OnQuery = null; return new { left = 0, thrown = _loginThrown }; }
        EntityFrameworkQueryableExtensions.OnQuery = () =>
        {
            bool t = false;
            lock (_l) { if (_loginN > 0 && Environment.StackTrace.Contains("VerifyCredentialsAsync")) { _loginN--; _loginThrown++; t = true; } }
            if (t) throw new InvalidOperationException("GIA-LOI-SQL-DANG-NHAP");
        };
        return new { left = n, thrown = _loginThrown };
    }

    static string Perms(bool edit) => JsonSerializer.Serialize(new List<GroupPermissionSetting> { new() { SectionCode = SystemPermissions.WtsMaster, CanView = true, CanEdit = edit } });

    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var g = db.UserGroups.List.FirstOrDefault(x => x.GroupCode == "G28M");
        if (g == null) { g = new UserGroup { GroupCode = "G28M", GroupName = "G28 quản WTS", IsActive = true, Permissions = Perms(true) }; db.UserGroups.Add(g); await db.SaveChangesAsync(); db.Fixup(); }
        if (!db.Users.List.Any(x => x.Username == "g28m"))
        { db.Users.Add(new User { Username = "g28m", FullName = "G28 quản WTS", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true }); await db.SaveChangesAsync(); db.Fixup(); }
        return new { ok = true, g.GroupId };
    }

    public static async Task<object> SetPermAsync(AppDbContext db, int edit)
    {
        var g = db.UserGroups.List.First(x => x.GroupCode == "G28M");
        g.Permissions = Perms(edit == 1);
        db.UserGroups.Update(g); await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true, perms = g.Permissions };
    }
}
