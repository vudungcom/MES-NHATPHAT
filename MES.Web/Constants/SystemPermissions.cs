namespace MES.Web.Constants;

public static class SystemPermissions
{
    // Danh sách mã quyền hằng số (Section Codes)
    public const string KeHoach     = "KE_HOACH";
    public const string PartTaoMoi  = "PART_TAO_MOI";
    public const string PartGc      = "PART_GC";
    public const string PartHtsp    = "PART_HTSP";
    public const string PartKcs     = "PART_KCS";
    public const string PartDongGoi = "PART_DONGGOI";
    public const string Kho         = "KHO";
    public const string SoDoTc      = "SO_DO_TC";
    public const string KhachHang   = "KHACH_HANG";
    public const string ThietBi     = "THIET_BI";
    public const string DoGa        = "DO_GA";
    public const string Dao         = "DAO";
    public const string WtsMaster   = "WTS_MASTER";
    public const string SettingAdmin = "SETTING_ADMIN";

    // Nhập WTS theo nhóm công việc
    public const string WtsGc      = "WTS_GC";
    public const string WtsHtsp    = "WTS_HTSP";
    public const string WtsKcs     = "WTS_KCS";
    public const string WtsDongGoi = "WTS_DONG_GOI";
    public const string WtsReport  = "WTS_REPORT";

    // Giao nhận hàng theo nhóm công đoạn
    public const string HandoverKho  = "HANDOVER_KHO";
    public const string HandoverGc   = "HANDOVER_GC";
    public const string HandoverHtsp = "HANDOVER_HTSP";
    public const string HandoverKcs  = "HANDOVER_KCS";
    public const string HandoverPkg  = "HANDOVER_PKG";

    public static readonly List<PermissionSection> Sections = new()
    {
        new PermissionSection(KeHoach, "Kế hoạch", "Toàn quyền trên trang Kế hoạch"),

        // 5 Vùng phân quyền Part Master
        new PermissionSection(PartTaoMoi,  "Part Master: A. Kế hoạch nhập",    "Tạo, sửa thông tin chung Part No & Quy cách phôi"),
        new PermissionSection(PartGc,      "Part Master: B. Quy trình Gia công","Quyền sửa bước Tiện/Phay NC, dao, đồ gá, thời gian máy"),
        new PermissionSection(PartHtsp,    "Part Master: C. Taro/Bavia/Rửa",   "Quyền chọn WTS & sửa bước HTSP"),
        new PermissionSection(PartKcs,     "Part Master: D. Kiểm tra (KCS)",   "Quyền chọn WTS & sửa thông số đo kiểm"),
        new PermissionSection(PartDongGoi, "Part Master: E. Đóng gói",         "Quyền chọn WTS & sửa quy cách đóng gói"),

        new PermissionSection(Kho,         "Quản lý Kho",          "Quản lý xuất nhập tồn, vị trí, tình trạng & cấp phát phôi"),
        new PermissionSection(SoDoTc,      "Sơ đồ tổ chức",        "Quản lý sơ đồ nhân sự & phòng ban"),
        new PermissionSection(KhachHang,   "Khách hàng",            "Quản lý danh mục đối tác & khách hàng"),
        new PermissionSection(ThietBi,     "Danh sách thiết bị",   "Quản lý danh sách máy móc"),
        new PermissionSection(DoGa,        "Quản lý Đồ gá",        "Quản lý danh mục & mượn trả đồ gá"),
        new PermissionSection(Dao,         "Quản lý dao",           "Quản lý danh mục dao cụ CNC, mũi khoan, taro và tồn kho"),
        new PermissionSection(WtsMaster,   "WTS Tiêu chuẩn",        "Thêm/sửa danh mục WTS chuẩn"),

        // 4 Vùng phân quyền Nhập WTS công nhân
        new PermissionSection(WtsGc,      "Nhập WTS: Gia công",        "Công nhân gia công ghi nhận NC, thời gian, sản lượng thực tế"),
        new PermissionSection(WtsHtsp,    "Nhập WTS: Hoàn thiện SP",   "Công nhân HTSP ghi nhận Taro/Bavia/Rửa thực tế"),
        new PermissionSection(WtsKcs,     "Nhập WTS: Kiểm tra (KCS)",  "Công nhân KCS ghi nhận công việc kiểm tra thực tế"),
        new PermissionSection(WtsDongGoi, "Nhập WTS: Đóng gói",        "Công nhân đóng gói ghi nhận công việc thực tế"),

        new PermissionSection(WtsReport,  "WTS Report",                "Xem báo cáo tổng hợp WTS — tra cứu theo công nhân, ngày, tháng"),

        // 5 Vùng phân quyền Giao nhận hàng
        new PermissionSection(HandoverKho,  "Giao nhận: Kho",       "Giao/nhận hàng tại Kho — cấp phát phôi cho Gia công"),
        new PermissionSection(HandoverGc,   "Giao nhận: Gia công",  "Giao/nhận hàng tại nhóm Gia công"),
        new PermissionSection(HandoverHtsp, "Giao nhận: HTSP",      "Giao/nhận hàng tại nhóm Hoàn thiện SP"),
        new PermissionSection(HandoverKcs,  "Giao nhận: KCS",       "Giao/nhận hàng tại nhóm Kiểm tra KCS"),
        new PermissionSection(HandoverPkg,  "Giao nhận: Đóng gói",  "Giao/nhận hàng tại nhóm Đóng gói"),

        new PermissionSection(SettingAdmin, "Cài đặt & Phân quyền", "Toàn quyền quản trị hệ thống (Chỉ Admin)")
    };
}

public class PermissionSection
{
    public string Code        { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public PermissionSection(string code, string name, string desc)
    {
        Code        = code;
        Name        = name;
        Description = desc;
    }
}

// Model chuyển đổi JSON phân quyền trong CSDL
public class GroupPermissionSetting
{
    public string SectionCode { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanEdit { get; set; }
    /// <summary>
    /// Quyền xóa (ngưng sử dụng). Hiện chỉ áp dụng cho PART_TAO_MOI.
    /// ADMIN luôn có quyền này bất kể cấu hình.
    /// </summary>
    public bool CanDelete { get; set; }
}
