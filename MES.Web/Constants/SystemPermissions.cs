namespace MES.Web.Constants;

public static class SystemPermissions
{
    // Danh sách mã quyền hằng số (Section Codes)
    public const string KeHoach = "KE_HOACH";
    public const string PartTaoMoi = "PART_TAO_MOI";
    public const string PartGc = "PART_GC";
    public const string PartHtsp = "PART_HTSP";
    public const string PartKcs = "PART_KCS";
    public const string PartDongGoi = "PART_DONGGOI";
    public const string Kho = "KHO";
    public const string SoDoTc = "SO_DO_TC";
    public const string KhachHang = "KHACH_HANG";
    public const string ThietBi = "THIET_BI";
    public const string DoGa = "DO_GA";
    public const string Dao = "DAO";
    public const string WtsMaster = "WTS_MASTER";
    public const string SettingAdmin = "SETTING_ADMIN";

    // Danh sách các vùng dữ liệu hiển thị trên bảng điều khiển Ma trận Phân quyền
    public static readonly List<PermissionSection> Sections = new()
    {
        new PermissionSection(KeHoach, "Kế hoạch", "Toàn quyền trên trang Kế hoạch"),
        
        // 5 Vùng phân quyền Part Master
        new PermissionSection(PartTaoMoi, "Part Master: A. Kế hoạch nhập", "Tạo, sửa thông tin chung Part No & Quy cách phôi"),
        new PermissionSection(PartGc, "Part Master: B. Quy trình Gia công", "Quyền sửa bước Tiện/Phay NC, dao, đồ gá, thời gian máy"),
        new PermissionSection(PartHtsp, "Part Master: C. Taro/Bavia/Rửa", "Quyền chọn WTS & sửa bước HTSP"),
        new PermissionSection(PartKcs, "Part Master: D. Kiểm tra (KCS)", "Quyền chọn WTS & sửa thông số đo kiểm"),
        new PermissionSection(PartDongGoi, "Part Master: E. Đóng gói", "Quyền chọn WTS & sửa quy cách đóng gói"),
        
        new PermissionSection(Kho, "Quản lý Kho", "Quản lý xuất nhập tồn, vị trí, tình trạng & cấp phát phôi"),
        new PermissionSection(SoDoTc, "Sơ đồ tổ chức", "Quản lý sơ đồ nhân sự & phòng ban"),
        new PermissionSection(KhachHang, "Khách hàng", "Quản lý danh mục đối tác & khách hàng"),
        new PermissionSection(ThietBi, "Danh sách thiết bị", "Quản lý danh sách máy móc"),
        new PermissionSection(DoGa, "Quản lý Đồ gá", "Quản lý danh mục & mượn trả đồ gá"),
        new PermissionSection(Dao, "Quản lý dao", "Quản lý danh mục dao cụ CNC, mũi khoan, taro và tồn kho"),
        new PermissionSection(WtsMaster, "WTS Tiêu chuẩn", "Thêm/sửa danh mục WTS chuẩn"),
        new PermissionSection(SettingAdmin, "Cài đặt & Phân quyền", "Toàn quyền quản trị hệ thống (Chỉ Admin)")
    };
}

public class PermissionSection
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public PermissionSection(string code, string name, string desc)
    {
        Code = code;
        Name = name;
        Description = desc;
    }
}

// Model chuyển đổi JSON phân quyền trong CSDL
public class GroupPermissionSetting
{
    public string SectionCode { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanEdit { get; set; }
}