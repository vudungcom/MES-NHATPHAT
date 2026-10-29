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
}
