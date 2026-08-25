namespace MES.Web.Constants;

public static class SystemPermissions
{
    // Danh sách các vùng dữ liệu để hiển thị lên bảng điều khiển phân quyền
    public static readonly List<PermissionSection> Sections = new()
    {
        new PermissionSection("KE_HOACH", "Kế hoạch", "Toàn quyền trên trang Kế hoạch"),
        
        // Chia nhỏ trang Part Master theo thiết kế của bạn
        new PermissionSection("PART_TAO_MOI", "Part Master: A. Tạo Part mới", "Tạo, sửa thông tin chung Part No"),
        new PermissionSection("PART_GC", "Part Master: B. Quy trình Gia công", "Quyền sửa bước Tiện/Phay NC"),
        new PermissionSection("PART_HTSP", "Part Master: C. Taro/Bavia/Rửa", "Quyền sửa bước HTSP"),
        new PermissionSection("PART_KCS", "Part Master: D. Kiểm tra (KCS)", "Quyền sửa thông số đo kiểm"),
        new PermissionSection("PART_DONGGOI", "Part Master: E. Đóng gói", "Quyền sửa quy cách đóng gói"),
        
        new PermissionSection("KHO", "Kho", "Quản lý xuất nhập tồn kho"),
        new PermissionSection("THIET_BI", "Thiết bị", "Quản lý danh sách máy móc"),
        new PermissionSection("DO_GA", "Đồ gá", "Quản lý danh mục & mượn trả đồ gá"),
        new PermissionSection("WTS_MASTER", "WTS Tiêu chuẩn", "Thêm/sửa danh mục WTS chuẩn"),
        new PermissionSection("SO_DO_TC", "Sơ đồ tổ chức", "Quản lý sơ đồ nhân sự"),
        new PermissionSection("SETTING_ADMIN", "Cài đặt & Phân quyền", "Toàn quyền quản trị hệ thống (Chỉ Admin)")
    };
}

public class PermissionSection
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public PermissionSection(string code, string name, string desc)
    {
        Code = code;
        Name = name;
        Description = desc;
    }
}

// Class dùng để chuyển đổi JSON phân quyền trong CSDL
public class GroupPermissionSetting
{
    public string SectionCode { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanEdit { get; set; }
}