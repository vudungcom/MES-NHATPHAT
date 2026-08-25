using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data.Entities;

namespace MES.Web.Services;

public enum PartMasterArea
{
    A_KeHoach = 1,
    B_Machining = 2,
    C_HoanThienSP = 3,
    D_KiemTra = 4,
    E_DongGoi = 5
}

public static class PartMasterPermissionHelper
{
    public const string GroupAdmin = "ADMIN";
    public const string GroupViewer = "VIEWER";
    public const string GroupPlanning = "PLANNING";
    public const string GroupWarehouse = "WAREHOUSE";
    public const string GroupProduction = "PRODUCTION";
    public const string GroupQc = "QC";
    public const string GroupSupplier = "SUPPLIER";
    public const string GroupTechnical = "TECHNICAL";
    public const string GroupFinishing = "FINISHING";
    public const string GroupInspection = "INSPECTION";
    public const string GroupPackaging = "DONG_GOI";

    public const string RoleLeader = "Leader";
    public const string RoleNormal = "Normal";

    public static string ToSectionCode(PartMasterArea area) => area switch
    {
        PartMasterArea.A_KeHoach     => "PART_TAO_MOI",
        PartMasterArea.B_Machining   => "PART_GC",
        PartMasterArea.C_HoanThienSP => "PART_HTSP",
        PartMasterArea.D_KiemTra     => "PART_KCS",
        PartMasterArea.E_DongGoi     => "PART_DONGGOI",
        _ => "PART_TAO_MOI"
    };

    public static string GetAreaName(PartMasterArea area) => area switch
    {
        PartMasterArea.A_KeHoach     => "Kế hoạch nhập (Thông tin chung)",
        PartMasterArea.B_Machining   => "Quy trình gia công CNC",
        PartMasterArea.C_HoanThienSP => "Hoàn thiện SP (Taro/Bavia/Rửa)",
        PartMasterArea.D_KiemTra     => "Kiểm tra chất lượng (KCS)",
        PartMasterArea.E_DongGoi     => "Quy trình Đóng gói",
        _ => "Không xác định"
    };

    public static bool CanView(string? groupCode) => !string.IsNullOrEmpty(groupCode);

    /// <summary>
    /// Kiểm tra quyền động từ chuỗi JSON Permissions của Nhóm người dùng
    /// </summary>
    public static bool CheckPermission(UserGroup? group, string sectionCode, bool requireEdit = false)
    {
        if (group == null || !group.IsActive) return false;
        if (group.GroupCode == GroupAdmin) return true;

        if (string.IsNullOrWhiteSpace(group.Permissions)) return false;
        try
        {
            var perms = JsonSerializer.Deserialize<List<GroupPermissionSetting>>(group.Permissions);
            var setting = perms?.FirstOrDefault(p => p.SectionCode == sectionCode);
            if (setting == null) return false;
            return requireEdit ? setting.CanEdit : setting.CanView;
        }
        catch
        {
            return false;
        }
    }

    public static bool CanEditArea(UserGroup? group, PartMasterArea area)
    {
        return CheckPermission(group, ToSectionCode(area), requireEdit: true);
    }

    public static bool CanViewArea(UserGroup? group, PartMasterArea area)
    {
        return CheckPermission(group, ToSectionCode(area), requireEdit: false);
    }

    // Tương thích ngược: Hàm cho Service gọi kiểu cũ
    public static bool CanEditArea(string? groupCode, string? role, PartMasterArea area)
    {
        if (groupCode == GroupAdmin) return true;
        return false;
    }

    // KHÔI PHỤC LẠI HÀM BỊ THIẾU Ở CREATE.RAZOR
    /// <summary>
    /// Có được tạo Part Master mới không?
    /// ADMIN: được. Leader của 1 trong các group chuyên môn: được. Còn lại: không.
    /// </summary>
    public static bool CanCreatePartMaster(string? groupCode, string? role)
    {
        if (groupCode == GroupAdmin) return true;
        if (role != RoleLeader) return false;
        return groupCode == GroupPlanning
            || groupCode == GroupTechnical
            || groupCode == GroupFinishing
            || groupCode == GroupInspection
            || groupCode == GroupPackaging;
    }
}