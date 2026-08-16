namespace MES.Web.Services;

/// <summary>
/// 4 vùng của Part Master. Mỗi vùng thuộc quản lý của 1 nhóm chuyên môn.
/// </summary>
public enum PartMasterArea
{
    /// <summary>A. Kế hoạch nhập: Tên chi tiết, Mã VL, Cấu hình phôi, Ghi chú phôi. Nhóm PLANNING quản.</summary>
    A_KeHoach = 1,

    /// <summary>B. Quy trình gia công (bảng PartMachiningSteps). Nhóm TECHNICAL quản.</summary>
    B_Machining = 2,

    /// <summary>C. Hoàn thiện SP (Taro + Bavia + Rửa). Nhóm FINISHING quản.</summary>
    C_HoanThienSP = 3,

    /// <summary>D. Kiểm tra (bảng PartInspectionSteps). Nhóm INSPECTION quản.</summary>
    D_KiemTra = 4
}

/// <summary>
/// Helper phân quyền Part Master — static, không cần DI, gọi được từ Service / Razor / Layout.
///
/// Quy tắc chốt ở Lượt 6B:
///  - XEM: mọi user đã login đều xem được (kể cả VIEWER, WAREHOUSE, PRODUCTION, QC, SUPPLIER).
///  - TẠO Part Master mới: chỉ ADMIN hoặc Leader của 1 trong 4 group (PLANNING/TECHNICAL/FINISHING/INSPECTION).
///  - SỬA/THÊM DÒNG/XÓA/KHÔI PHỤC dòng công đoạn: chỉ ADMIN hoặc Leader của group **đúng area**.
///    VD: bảng Machining chỉ TECHNICAL Leader (hoặc ADMIN) sửa được.
///
/// User Normal (không phải Leader): chỉ xem, không sửa/thêm/xóa được gì.
/// User các group ngoài (VIEWER, WAREHOUSE, PRODUCTION, QC, SUPPLIER): chỉ xem.
/// </summary>
public static class PartMasterPermissionHelper
{
    // ==== Group codes ====
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

    // ==== Roles ====
    public const string RoleLeader = "Leader";
    public const string RoleNormal = "Normal";

    /// <summary>Có được xem Part Master (list + detail) không? Mọi user đã login đều xem được.</summary>
    public static bool CanView(string? groupCode)
    {
        return !string.IsNullOrEmpty(groupCode);
    }

    /// <summary>
    /// Có được tạo Part Master mới không?
    /// ADMIN: được. Leader của 1 trong 4 group chuyên môn: được. Còn lại: không.
    /// </summary>
    public static bool CanCreatePartMaster(string? groupCode, string? role)
    {
        if (groupCode == GroupAdmin) return true;
        if (role != RoleLeader) return false;
        return groupCode == GroupPlanning
            || groupCode == GroupTechnical
            || groupCode == GroupFinishing
            || groupCode == GroupInspection;
    }

    /// <summary>
    /// Có được sửa/thêm/xóa/khôi phục trong vùng cụ thể của Part Master không?
    /// ADMIN: được mọi vùng. Leader của group đúng area: được. Còn lại: không.
    /// </summary>
    public static bool CanEditArea(string? groupCode, string? role, PartMasterArea area)
    {
        if (groupCode == GroupAdmin) return true;
        if (role != RoleLeader) return false;

        return area switch
        {
            PartMasterArea.A_KeHoach     => groupCode == GroupPlanning,
            PartMasterArea.B_Machining   => groupCode == GroupTechnical,
            PartMasterArea.C_HoanThienSP => groupCode == GroupFinishing,
            PartMasterArea.D_KiemTra     => groupCode == GroupInspection,
            _ => false
        };
    }

    /// <summary>Chuỗi mô tả tiếng Việt của vùng (dùng cho thông báo lỗi).</summary>
    public static string GetAreaName(PartMasterArea area) => area switch
    {
        PartMasterArea.A_KeHoach     => "Kế hoạch nhập",
        PartMasterArea.B_Machining   => "Quy trình gia công",
        PartMasterArea.C_HoanThienSP => "Hoàn thiện SP",
        PartMasterArea.D_KiemTra     => "Kiểm tra",
        _ => "Không xác định"
    };
}
