using MES.Web.Data;
using MES.Web.Data.Entities;

// [RS-C2 2026-10-29] Máy chủ nay kiểm quyền nhóm khi lập phiếu / nhận (đọc ma trận từ DB).
//   Người dùng thử u2..u6 trước không có nhóm (máy chủ không kiểm) → cho vào nhóm "XUONG" có quyền SỬA cả 5 nhóm
//   giao nhận để mọi kịch bản / lượt ngẫu nhiên giữ y hành vi cũ. Ca TỪ CHỐI kiểm riêng ở QuyenScen (lệnh `quyen`).
public static class FuzzPerm
{
    public const int XuongId = 90;

    public static string Json(params string[] editKeys)
        => System.Text.Json.JsonSerializer.Serialize(editKeys.Select(k => new MES.Web.Constants.GroupPermissionSetting { SectionCode = k, CanView = true, CanEdit = true }).ToList());

    public static readonly string[] AllHandover =
    {
        MES.Web.Constants.SystemPermissions.HandoverKho, MES.Web.Constants.SystemPermissions.HandoverGc,
        MES.Web.Constants.SystemPermissions.HandoverHtsp, MES.Web.Constants.SystemPermissions.HandoverKcs,
        MES.Web.Constants.SystemPermissions.HandoverPkg,
    };

    public static UserGroup Xuong(AppDbContext db)
    {
        var g = db.UserGroups.List.FirstOrDefault(x => x.GroupId == XuongId);
        if (g != null) return g;
        g = new UserGroup { GroupId = XuongId, GroupCode = "XUONG", GroupName = "Xưởng (thử)", IsActive = true, Permissions = Json(AllHandover) };
        db.UserGroups.List.Add(g);
        return g;
    }

    /// <summary>[VOID-PERM 2026-10-31] Máy chủ đọc quyền huỷ / báo lỗi từ ma trận DB, bỏ qua danh sách nhóm trang gửi lên.
    ///   Ca "người chỉ có quyền Sửa Gia công" phải là người có ĐÚNG quyền đó trong DB (u9, nhóm GCONLY) — u2..u6 ở nhóm XUONG sửa được cả 5 nhóm.</summary>
    public const int GcOnlyId = 9;
    public static int GcOnlyUser(AppDbContext db)
    {
        if (db.Users.List.Any(u => u.UserId == GcOnlyId)) return GcOnlyId;
        var g = new UserGroup { GroupId = 91, GroupCode = "GCONLY", GroupName = "Chỉ Gia công (thử)", IsActive = true, Permissions = Json(MES.Web.Constants.SystemPermissions.HandoverGc) };
        db.UserGroups.List.Add(g);
        db.Users.List.Add(new User { UserId = GcOnlyId, Username = "u9", FullName = "U9", IsActive = true, Group = g, GroupId = 91 });
        db.Fixup();
        return GcOnlyId;
    }
}
