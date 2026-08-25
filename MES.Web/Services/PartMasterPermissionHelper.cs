namespace MES.Web.Services;

/// <summary>
/// Các vùng của Part Master. Mỗi vùng thuộc quản lý của 1 nhóm chuyên môn.
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
    D_KiemTra = 4,

    /// <summary>E. Đóng gói thành phẩm.</summary>
    E_DongGoi = 5
}

/// <summary>
/// Helper phân quyền Part Master — static, không cần DI, tương thích toàn bộ Service/Layout/Razor.
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

    /// <summary>Có được xem Part Master (list + detail) không?</summary>
    public static bool CanView(string? groupCode)
    {
        return !string.IsNullOrEmpty(groupCode);
    }

    /// <summary>
    /// Có được tạo Part Master mới không?
    /// ADMIN: được. Leader của 1 trong các group chuyên môn: được.
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
    /// ADMIN: được mọi vùng. Leader của group đúng area: được.
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
        PartMasterArea.E_DongGoi     => "Đóng gói",
        _ => "Không xác định"
    };

    /// <summary>
    /// Chuyển đổi từ Area sang SectionCode chuẩn trong ma trận RBAC
    /// </summary>
    public static string ToSectionCode(PartMasterArea area) => area switch
    {
        PartMasterArea.A_KeHoach     => "PART_TAO_MOI",
        PartMasterArea.B_Machining   => "PART_GC",
        PartMasterArea.C_HoanThienSP => "PART_HTSP",
        PartMasterArea.D_KiemTra     => "PART_KCS",
        PartMasterArea.E_DongGoi     => "PART_DONGGOI",
        _ => "PART_TAO_MOI"
    };
}