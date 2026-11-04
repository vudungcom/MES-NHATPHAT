using ClosedXML.Excel;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class DepartmentService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public DepartmentService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    // =====================================================================
    // [PHAT-HIEN-0311 2026-11-04] Sơ đồ tổ chức: máy chủ kiểm quyền ma trận SO_DO_TC theo DB (PermCheck, cùng luật RS-C1).
    //   Trước: Thêm / Sửa / Xoá chỉ hỏi PIN, cả trang lẫn service không kiểm quyền → ai đăng nhập + có PIN gõ URL là sửa được.
    //   Ảnh đại diện: chỉ nhận .jpg / .jpeg / .png / .webp (kiểm cả đuôi lẫn mấy byte đầu file), cần quyền Sửa — trước không kiểm
    //   đuôi, file .html / .js tải lên nằm trong wwwroot/uploads và chạy được. Ô AvatarUrl chỉ nhận đường dẫn ảnh do máy chủ cấp.
    //   Nguồn: CLAUDE rà độc lập 03/11, Hùng duyệt 04/11 08:24.
    // =====================================================================
    public const string TenMuc = "Sơ đồ tổ chức";
    public const long AvatarMaxBytes = 5 * 1024 * 1024;
    public static readonly string[] AvatarExts = { ".jpg", ".jpeg", ".png", ".webp" };

    public Task<bool> CanViewAsync(int userId) => PermCheck.CanViewAsync(_db, userId, SystemPermissions.SoDoTc);
    public Task<bool> CanEditAsync(int userId) => PermCheck.CanEditAsync(_db, userId, SystemPermissions.SoDoTc);

    /// <summary>Đuôi file ảnh hợp lệ (không phân biệt hoa thường).</summary>
    public static bool AvatarExtOk(string? fileName)
        => AvatarExts.Contains((Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant());

    /// <summary>Mấy byte đầu đúng kiểu ảnh của đuôi file (JPEG FF D8 FF · PNG 89 50 4E 47 0D 0A 1A 0A · WEBP "RIFF"....“WEBP”).</summary>
    public static bool AvatarBytesOk(string ext, byte[] b)
    {
        ext = (ext ?? "").ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg") return b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
        if (ext == ".png") return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
        if (ext == ".webp") return b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                                 && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';
        return false;
    }

    /// <summary>Đường dẫn ảnh do <see cref="SaveAvatarAsync"/> cấp: /uploads/avatars/&lt;tên chỉ chữ số, chữ, _&gt;.&lt;đuôi ảnh&gt;.</summary>
    public static bool AvatarUrlOk(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(url.Trim(), @"^/uploads/avatars/[A-Za-z0-9_]+\.(jpg|jpeg|png|webp)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public const string MsgAvatarType = "Ảnh đại diện chỉ nhận file .jpg, .jpeg, .png hoặc .webp.";

    /// <summary>Lưu ảnh đại diện vào wwwroot/uploads/avatars (cần quyền Sửa Sơ đồ tổ chức). Trả đường dẫn tương đối.</summary>
    public async Task<(bool Ok, string? Error, string? Url)> SaveAvatarAsync(Stream src, string? fileName, long size, int actorUserId, string webRootPath)
    {
        if (!await CanEditAsync(actorUserId)) return (false, PermCheck.NoEdit(TenMuc), null);
        if (size > AvatarMaxBytes) return (false, "Kích thước ảnh vượt quá 5MB!", null);
        var ext = (Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
        if (!AvatarExtOk(fileName)) return (false, MsgAvatarType, null);
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await src.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        if (bytes.Length > AvatarMaxBytes) return (false, "Kích thước ảnh vượt quá 5MB!", null);
        if (!AvatarBytesOk(ext, bytes)) return (false, "File không phải ảnh " + ext + " thật (nội dung không khớp đuôi file). " + MsgAvatarType, null);

        var folderPath = Path.Combine(webRootPath, "uploads", "avatars");
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        var randomCode = Guid.NewGuid().ToString("N").Substring(0, 8);
        var name = $"nv_{DateTime.Now:yyyyMMddHHmmss}_{randomCode}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(folderPath, name), bytes);
        return (true, null, $"/uploads/avatars/{name}");
    }

    public async Task<List<Department>> GetAllActiveAsync()
    {
        return await _db.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.Level)
            .ThenBy(d => d.DisplayOrder)
            .ThenBy(d => d.DepartmentId)
            .ToListAsync();
    }

    /// <summary>
    /// Xuất file Excel gồm 2 Sheet: Sơ đồ khối có đường nối & mũi tên đối xứng 100% + Bảng danh sách chi tiết
    /// </summary>
    public async Task<byte[]> ExportExcelAsync()
    {
        var list = await GetAllActiveAsync();

        using var workbook = new XLWorkbook();

        // =========================================================================
        // SHEET 1: SƠ ĐỒ KHỐI TRỰC QUAN ĐỐI XỨNG (VISUAL ORG CHART WITH CONNECTORS)
        // =========================================================================
        var wsChart = workbook.Worksheets.Add("Sơ đồ khối (Org Chart)");
        wsChart.ShowGridLines = false;

        var lineColor = XLColor.FromHtml("#1e70bf");
        var arrowColor = XLColor.FromHtml("#0284c7");

        // -------------------------------------------------------------------------
        // 1. TẦNG 1: BAN GIÁM ĐỐC (Cột 19-23, Trục tim: Cột 21)
        // -------------------------------------------------------------------------
        var gd = list.FirstOrDefault(d => d.DepartmentId == 1 || (d.ParentId == null && d.Level == 1));
        if (gd != null)
        {
            DrawBox(wsChart, 2, 19, 5, gd, "#1e40af");
        }

        // Đường nối từ Giám Đốc xuống 3 Phòng ban
        DrawStem(wsChart, 4, 21, lineColor);
        DrawBusLine(wsChart, 5, 9, 33, lineColor);
        DrawArrow(wsChart, 6, 9, arrowColor);   // Trỏ vào Hành chính
        DrawArrow(wsChart, 6, 21, arrowColor);  // Trỏ vào Sản xuất
        DrawArrow(wsChart, 6, 33, arrowColor);  // Trỏ vào Kế toán

        // -------------------------------------------------------------------------
        // 2. TẦNG 2: 3 PHÒNG BAN CHỨC NĂNG (Hàng 7-8)
        // -------------------------------------------------------------------------
        var hc = list.FirstOrDefault(d => d.DepartmentId == 2);
        var sx = list.FirstOrDefault(d => d.DepartmentId == 3);
        var kt = list.FirstOrDefault(d => d.DepartmentId == 4);

        if (hc != null) DrawBox(wsChart, 7, 7, 5, hc, "#2b7bc4");   // Trục tim: 9
        if (sx != null) DrawBox(wsChart, 7, 19, 5, sx, "#1e40af");  // Trục tim: 21
        if (kt != null) DrawBox(wsChart, 7, 31, 5, kt, "#2b7bc4");  // Trục tim: 33

        // Đường nối từ Phòng Sản Xuất xuống 3 Phó phòng
        DrawStem(wsChart, 9, 21, lineColor);
        DrawBusLine(wsChart, 10, 9, 33, lineColor);
        DrawArrow(wsChart, 11, 9, arrowColor);   // Trỏ vào PP Gia công
        DrawArrow(wsChart, 11, 21, arrowColor);  // Trỏ vào PP CL & HTSP
        DrawArrow(wsChart, 11, 33, arrowColor);  // Trỏ vào PP QLSX

        // -------------------------------------------------------------------------
        // 3. TẦNG 3: 3 PHÓ PHÒNG (Hàng 12-13)
        // -------------------------------------------------------------------------
        var ppGc = list.FirstOrDefault(d => d.DepartmentId == 5);
        var ppCl = list.FirstOrDefault(d => d.DepartmentId == 6);
        var ppQlsx = list.FirstOrDefault(d => d.DepartmentId == 7);

        if (ppGc != null) DrawBox(wsChart, 12, 7, 5, ppGc, "#0284c7");    // Trục tim: 9
        if (ppCl != null) DrawBox(wsChart, 12, 19, 5, ppCl, "#0284c7");  // Trục tim: 21
        if (ppQlsx != null) DrawBox(wsChart, 12, 31, 5, ppQlsx, "#0284c7");// Trục tim: 33

        // -------------------------------------------------------------------------
        // 4. ĐƯỜNG NỐI TỪ TỪNG PHÓ PHÒNG XUỐNG CÁC BỘ PHẬN TRỰC THUỘC
        // -------------------------------------------------------------------------
        // Nhánh 1: Phó phòng Gia công -> 4 BP (Trục tim: 3, 7, 11, 15)
        DrawStem(wsChart, 14, 9, lineColor);
        DrawBusLine(wsChart, 15, 3, 15, lineColor);
        DrawArrow(wsChart, 16, 3, arrowColor);
        DrawArrow(wsChart, 16, 7, arrowColor);
        DrawArrow(wsChart, 16, 11, arrowColor);
        DrawArrow(wsChart, 16, 15, arrowColor);

        // Nhánh 2: Phó phòng CL & HTSP -> 2 BP (Trục tim: 19, 23)
        DrawStem(wsChart, 14, 21, lineColor);
        DrawBusLine(wsChart, 15, 19, 23, lineColor);
        DrawArrow(wsChart, 16, 19, arrowColor);
        DrawArrow(wsChart, 16, 23, arrowColor);

        // Nhánh 3: Phó phòng QLSX -> 4 BP (Trục tim: 27, 31, 35, 39)
        DrawStem(wsChart, 14, 33, lineColor);
        DrawBusLine(wsChart, 15, 27, 39, lineColor);
        DrawArrow(wsChart, 16, 27, arrowColor);
        DrawArrow(wsChart, 16, 31, arrowColor);
        DrawArrow(wsChart, 16, 35, arrowColor);
        DrawArrow(wsChart, 16, 39, arrowColor);

        // -------------------------------------------------------------------------
        // 5. TẦNG 4 & 5: CÁC BỘ PHẬN & NHÓM CON (MỖI BP RỘNG 3 CỘT, TIM NẰM GIỮA)
        // -------------------------------------------------------------------------
        var bpGridMap = new Dictionary<int, int>
        {
            { 8, 2 },   // BP KỸ THUẬT: Cột 2..4 (Trục tim: 3)
            { 9, 6 },   // BP CƠ ĐIỆN: Cột 6..8 (Trục tim: 7)
            { 10, 10 }, // BP GC TIỆN: Cột 10..12 (Trục tim: 11)
            { 11, 14 }, // BP GC PHAY: Cột 14..16 (Trục tim: 15)
            { 18, 18 }, // BP HTSP: Cột 18..20 (Trục tim: 19)
            { 19, 22 }, // BP KCS: Cột 22..24 (Trục tim: 23)
            { 27, 26 }, // BP VPSX: Cột 26..28 (Trục tim: 27)
            { 28, 30 }, // BP KHO: Cột 30..32 (Trục tim: 31)
            { 29, 34 }, // BP KHSX: Cột 34..36 (Trục tim: 35)
            { 30, 38 }  // BP BẢO TRÌ: Cột 38..40 (Trục tim: 39)
        };

        foreach (var kvp in bpGridMap)
        {
            var bp = list.FirstOrDefault(d => d.DepartmentId == kvp.Key);
            if (bp != null)
            {
                int startCol = kvp.Value;
                int centerCol = startCol + 1; // Cột tim chính giữa của ô 3 cột

                // Vẽ ô Bộ phận Cấp 4 (Hàng 17-18)
                DrawBox(wsChart, 17, startCol, 3, bp, "#2b7bc4");

                // Vẽ các nhóm con Cấp 5 xếp dọc bên dưới, mũi tên tim thẳng tắp
                var nhomChildren = list.Where(d => d.ParentId == bp.DepartmentId).OrderBy(d => d.DisplayOrder).ToList();
                int currentSubRow = 19;
                foreach (var nhom in nhomChildren)
                {
                    DrawArrow(wsChart, currentSubRow, centerCol, arrowColor);
                    DrawBox(wsChart, currentSubRow + 1, startCol, 3, nhom, "#475569");
                    currentSubRow += 3;
                }
            }
        }

        // Định dạng độ rộng cột để toàn bộ sơ đồ hiển thị cân đối
        for (int c = 1; c <= 42; c++)
        {
            wsChart.Column(c).Width = 7.5;
        }

        // =========================================================================
        // SHEET 2: DẠNG DANH SÁCH CHI TIẾT (TABLE VIEW)
        // =========================================================================
        var wsTable = workbook.Worksheets.Add("Dạng danh sách (Table View)");

        wsTable.Cell("A1").Value = "DANH SÁCH CƠ CẤU TỔ CHỨC & NHÂN SỰ CHI TIẾT";
        wsTable.Range("A1:G1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(XLColor.FromHtml("#1e3a8a"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        wsTable.Cell("A2").Value = $"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Tổng số vị trí: {list.Count}";
        wsTable.Range("A2:G2").Merge().Style
            .Font.SetItalic(true)
            .Font.SetFontSize(10)
            .Font.SetFontColor(XLColor.FromHtml("#64748b"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "STT", "Cấp bậc", "Mã đơn vị", "Tên Phòng ban / Bộ phận / Nhóm", "Chức danh", "Người phụ trách", "Cấp trên trực tiếp" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = wsTable.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2b7bc4"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        wsTable.Row(4).Height = 25;

        int r = 5;
        int stt = 1;
        foreach (var d in list)
        {
            var parent = list.FirstOrDefault(p => p.DepartmentId == d.ParentId);

            wsTable.Cell(r, 1).SetValue(stt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            wsTable.Cell(r, 2).SetValue($"Cấp {d.Level}").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            wsTable.Cell(r, 3).SetValue(d.DepartmentCode).Style.Font.SetBold(true);

            var indent = new string(' ', (d.Level - 1) * 3);
            wsTable.Cell(r, 4).SetValue($"{indent}{d.DepartmentName}");

            wsTable.Cell(r, 5).SetValue(d.RoleTitle ?? "");
            wsTable.Cell(r, 6).SetValue(string.IsNullOrWhiteSpace(d.ManagerName) ? "(Chưa phân công)" : d.ManagerName)
                .Style.Font.SetFontColor(XLColor.FromHtml("#e11d48"))
                .Font.SetBold(!string.IsNullOrWhiteSpace(d.ManagerName));
            wsTable.Cell(r, 7).SetValue(parent != null ? parent.DepartmentName : "(Gốc)");

            if (d.Level == 1)
            {
                wsTable.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#dbeafe")).Font.SetBold(true);
            }
            else if (d.Level == 2)
            {
                wsTable.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#f8fafc"));
            }

            wsTable.Row(r).Height = 22;
            r++;
            stt++;
        }

        var dataRange = wsTable.Range(4, 1, r - 1, 7);
        dataRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        wsTable.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Vẽ 1 ô khối (Node)
    /// </summary>
    private static void DrawBox(IXLWorksheet ws, int startRow, int startCol, int colCount, Department dept, string headerBgHex)
    {
        int endCol = startCol + colCount - 1;

        // Dòng 1: Tên đơn vị + Chức danh
        var headerRange = ws.Range(startRow, startCol, startRow, endCol);
        headerRange.Merge();
        headerRange.Value = !string.IsNullOrWhiteSpace(dept.RoleTitle) 
            ? $"{dept.DepartmentName}\n{dept.RoleTitle}" 
            : dept.DepartmentName;
        headerRange.Style
            .Font.SetBold(true)
            .Font.SetFontSize(9.5)
            .Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml(headerBgHex))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText(true);
        ws.Row(startRow).Height = 26;

        // Dòng 2: Tên người phụ trách
        var bodyRange = ws.Range(startRow + 1, startCol, startRow + 1, endCol);
        bodyRange.Merge();
        bodyRange.Value = !string.IsNullOrWhiteSpace(dept.ManagerName) ? dept.ManagerName : "(Chưa phân công)";
        bodyRange.Style
            .Font.SetBold(!string.IsNullOrWhiteSpace(dept.ManagerName))
            .Font.SetFontSize(9.5)
            .Font.SetFontColor(string.IsNullOrWhiteSpace(dept.ManagerName) ? XLColor.FromHtml("#94a3b8") : XLColor.FromHtml("#e11d48"))
            .Fill.SetBackgroundColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(startRow + 1).Height = 20;

        // Viền khối
        var boxRange = ws.Range(startRow, startCol, startRow + 1, endCol);
        boxRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        boxRange.Style.Border.SetOutsideBorderColor(XLColor.FromHtml(headerBgHex));
        boxRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
        boxRange.Style.Border.SetInsideBorderColor(XLColor.FromHtml("#cbd5e1"));
    }

    /// <summary>
    /// Vẽ đường kẻ ngang bus bar
    /// </summary>
    private static void DrawBusLine(IXLWorksheet ws, int row, int startCol, int endCol, XLColor color)
    {
        var range = ws.Range(row, startCol, row, endCol);
        range.Style.Border.SetBottomBorder(XLBorderStyleValues.Medium);
        range.Style.Border.SetBottomBorderColor(color);
        ws.Row(row).Height = 8;
    }

    /// <summary>
    /// Vẽ đường kẻ trục thẳng đứng '│' tại đúng ô tim
    /// </summary>
    private static void DrawStem(IXLWorksheet ws, int row, int col, XLColor color)
    {
        var cell = ws.Cell(row, col);
        cell.Value = "│";
        cell.Style
            .Font.SetBold(true)
            .Font.SetFontSize(11)
            .Font.SetFontColor(color)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(row).Height = 13;
    }

    /// <summary>
    /// Vẽ mũi tên chỉ xuống '▼' tại đúng ô tim
    /// </summary>
    private static void DrawArrow(IXLWorksheet ws, int row, int col, XLColor color)
    {
        var cell = ws.Cell(row, col);
        cell.Value = "▼";
        cell.Style
            .Font.SetBold(true)
            .Font.SetFontSize(9)
            .Font.SetFontColor(color)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(row).Height = 14;
    }

    public async Task<(bool Success, string? Error)> CreateAsync(Department model, int currentUserId, string pin)
    {
        if (!await CanEditAsync(currentUserId)) return (false, PermCheck.NoEdit(TenMuc));   // [PHAT-HIEN-0311]
        if (!AvatarUrlOk(model.AvatarUrl)) return (false, MsgAvatarType + " Tải lại ảnh bằng nút chọn file.");   // [PHAT-HIEN-0311]
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        if (string.IsNullOrWhiteSpace(model.DepartmentName)) return (false, "Tên đơn vị / nhóm không được để trống.");
        if (string.IsNullOrWhiteSpace(model.DepartmentCode)) return (false, "Mã đơn vị không được để trống.");

        var now = DateTime.Now;

        int nextId = (await _db.Departments.MaxAsync(d => (int?)d.DepartmentId) ?? 0) + 1;
        model.DepartmentId = nextId;

        if (model.ParentId.HasValue && model.ParentId.Value > 0)
        {
            var parent = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == model.ParentId.Value);
            model.Level = parent != null ? parent.Level + 1 : 2;
        }
        else
        {
            model.ParentId = null;
            model.Level = 1;
        }

        model.DepartmentCode = model.DepartmentCode.Trim().ToUpper();
        model.DepartmentName = model.DepartmentName.Trim();
        model.RoleTitle = model.RoleTitle?.Trim() ?? "";
        model.ManagerName = model.ManagerName?.Trim() ?? "";
        model.AvatarUrl = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim();
        model.IsActive = true;
        model.CreatedAt = now;
        model.CreatedBy = currentUserId;

        _db.Departments.Add(model);

        _db.DepartmentChangeLogs.Add(new DepartmentChangeLog
        {
            DepartmentId = model.DepartmentId,
            FieldName = "Tạo mới vị trí",
            OldValue = null,
            NewValue = $"{model.DepartmentName} ({model.RoleTitle}: {model.ManagerName})",
            Reason = "Khởi tạo vị trí trong sơ đồ tổ chức",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        Department updatedModel, 
        string? reason, 
        int currentUserId, 
        string pin,
        bool isNewAvatarUploaded,
        string oldManagerNameOriginal)
    {
        if (!await CanEditAsync(currentUserId)) return (false, PermCheck.NoEdit(TenMuc));   // [PHAT-HIEN-0311]
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == updatedModel.DepartmentId);
        if (dept == null) return (false, "Không tìm thấy bộ phận.");
        // [PHAT-HIEN-0311] ảnh đổi thì phải là đường dẫn ảnh do máy chủ cấp (ảnh cũ giữ nguyên thì không xét)
        if ((updatedModel.AvatarUrl ?? "").Trim() != (dept.AvatarUrl ?? "").Trim() && !AvatarUrlOk(updatedModel.AvatarUrl))
            return (false, MsgAvatarType + " Tải lại ảnh bằng nút chọn file.");

        var now = DateTime.Now;
        var newManager = updatedModel.ManagerName?.Trim() ?? "";
        var oldManager = oldManagerNameOriginal.Trim();

        if (!string.IsNullOrWhiteSpace(newManager) && !string.Equals(newManager, oldManager, StringComparison.OrdinalIgnoreCase))
        {
            if (!isNewAvatarUploaded)
            {
                return (false, $"Bạn đã đổi người phụ trách thành '{newManager}'. Vui lòng tải ảnh đại diện mới tương ứng để tránh nhầm ảnh nhân sự cũ!");
            }
        }

        var logs = new List<DepartmentChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal)
        {
            oldVal = oldVal ?? "";
            newVal = newVal ?? "";
            if (oldVal.Trim() != newVal.Trim())
            {
                logs.Add(new DepartmentChangeLog
                {
                    DepartmentId = dept.DepartmentId,
                    FieldName = fieldName,
                    OldValue = oldVal.Trim(),
                    NewValue = newVal.Trim(),
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                });
            }
        }

        CheckAndLog("Tên đơn vị", dept.DepartmentName, updatedModel.DepartmentName);
        CheckAndLog("Mã đơn vị", dept.DepartmentCode, updatedModel.DepartmentCode);
        CheckAndLog("Chức danh", dept.RoleTitle, updatedModel.RoleTitle);
        CheckAndLog("Người phụ trách", dept.ManagerName, updatedModel.ManagerName);
        CheckAndLog("Ảnh đại diện", dept.AvatarUrl, updatedModel.AvatarUrl);

        if (dept.ParentId != updatedModel.ParentId)
        {
            logs.Add(new DepartmentChangeLog
            {
                DepartmentId = dept.DepartmentId,
                FieldName = "Thuộc đơn vị cấp trên",
                OldValue = dept.ParentId?.ToString() ?? "Gốc",
                NewValue = updatedModel.ParentId?.ToString() ?? "Gốc",
                Reason = reason,
                ChangedAt = now,
                ChangedBy = currentUserId
            });
            dept.ParentId = updatedModel.ParentId;
            if (dept.ParentId.HasValue && dept.ParentId.Value > 0)
            {
                var parent = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == dept.ParentId.Value);
                dept.Level = parent != null ? parent.Level + 1 : 2;
            }
            else
            {
                dept.Level = 1;
            }
        }

        if (logs.Any())
        {
            dept.DepartmentName = updatedModel.DepartmentName.Trim();
            dept.DepartmentCode = updatedModel.DepartmentCode.Trim().ToUpper();
            dept.RoleTitle = updatedModel.RoleTitle?.Trim() ?? "";
            dept.ManagerName = newManager;
            dept.AvatarUrl = string.IsNullOrWhiteSpace(newManager) || string.IsNullOrWhiteSpace(updatedModel.AvatarUrl) ? null : updatedModel.AvatarUrl.Trim();   // [PHAT-HIEN-0311] lưu bản đã kiểm (bỏ khoảng trắng)
            dept.UpdatedAt = now;
            dept.UpdatedBy = currentUserId;

            _db.DepartmentChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int departmentId, int currentUserId, string pin)
    {
        if (!await CanEditAsync(currentUserId)) return (false, PermCheck.NoEdit(TenMuc));   // [PHAT-HIEN-0311]
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == departmentId);
        if (dept == null) return (false, "Không tìm thấy vị trí cần xóa.");

        bool hasActiveChildren = await _db.Departments.AnyAsync(d => d.ParentId == departmentId && d.IsActive);
        if (hasActiveChildren)
        {
            return (false, "Không thể xóa vị trí này vì đang có các nhóm/bộ phận con trực thuộc. Hãy xóa hoặc chuyển cấp các nhóm con trước.");
        }

        var now = DateTime.Now;
        dept.IsActive = false;
        dept.UpdatedAt = now;
        dept.UpdatedBy = currentUserId;

        _db.DepartmentChangeLogs.Add(new DepartmentChangeLog
        {
            DepartmentId = dept.DepartmentId,
            FieldName = "Trạng thái",
            OldValue = "Đang hoạt động",
            NewValue = "Đã xóa khỏi sơ đồ",
            Reason = "Xóa vị trí khỏi sơ đồ tổ chức",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<DepartmentChangeLog>> GetChangeLogsAsync(int departmentId)
    {
        return await _db.DepartmentChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.DepartmentId == departmentId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }
}