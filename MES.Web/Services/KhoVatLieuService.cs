using System.Globalization;
using ClosedXML.Excel;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class KhoVatLieuViewItem
{
    public int KhPlanDetailId { get; set; }
    public long KhoVatLieuId { get; set; }
    public int KhPlanId { get; set; }
    public string PlanNo { get; set; } = "";
    public DateTime PlanDate { get; set; }
    public string CustomerName { get; set; } = "";

    // Cột A -> H: Link trực tiếp từ KhPlanDetail
    public string SoPO { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string DonViTinh { get; set; } = "PC";
    public decimal SoLuongKeHoach { get; set; }
    public DateTime ThoiHan { get; set; }
    public string? MaVatLieu { get; set; }
    public string? CauHinhPhoi { get; set; }
    public string? GhiChuPhoi { get; set; }

    // Cột I -> O: Dữ liệu Kho nhập trong bảng KhoVatLieus
    public DateTime? NgayNhanPhoi { get; set; }
    public int? SoLuongThucNhan { get; set; }
    public string? OrderVatLieu { get; set; }
    public string? ViTriDePhoi { get; set; }
    public string? TinhTrangPhoi { get; set; }
    public DateTime? NgayCapPhoi { get; set; }
    public int? SoLuongCap { get; set; }
}

public class KhoVatLieuService
{
    private readonly AppDbContext _db;
    private readonly UserService _userSvc;

    public KhoVatLieuService(AppDbContext db, UserService userSvc)
    {
        _db = db;
        _userSvc = userSvc;
    }

    /// <summary>
    /// Lấy toàn bộ danh sách Kho: Cột A-H link tự động từ Kế hoạch (KhPlanDetails), Cột I-O lấy từ KhoVatLieus
    /// </summary>
    public async Task<List<KhoVatLieuViewItem>> GetAllAsync()
    {
        var q = from d in _db.KhPlanDetails
                join p in _db.KhPlans on d.KhPlanId equals p.KhPlanId
                join c in _db.Customers on p.CustomerId equals c.CustomerId into cGroup
                from c in cGroup.DefaultIfEmpty()
                join k in _db.KhoVatLieus on d.KhPlanDetailId equals k.PlanDetailId into kGroup
                from k in kGroup.DefaultIfEmpty()
                orderby p.PlanDate descending, d.LineNo ascending
                select new KhoVatLieuViewItem
                {
                    KhPlanDetailId = d.KhPlanDetailId,
                    KhoVatLieuId = k != null ? k.KhoVatLieuId : 0,
                    KhPlanId = p.KhPlanId,
                    PlanNo = p.PlanNo,
                    PlanDate = p.PlanDate,
                    CustomerName = c != null ? c.CustomerName : "",

                    // Cột A -> H link trực tiếp từ Kế hoạch
                    SoPO = d.PurchaseOrder,
                    PartNo = d.PartNo,
                    DonViTinh = d.Unit,
                    SoLuongKeHoach = d.Quantity,
                    ThoiHan = d.STD,
                    MaVatLieu = d.Material,
                    CauHinhPhoi = d.MaterialConfig,
                    GhiChuPhoi = d.MaterialNotes,

                    // Cột I -> O từ bảng Kho
                    NgayNhanPhoi = k != null ? k.NgayNhanPhoi : null,
                    SoLuongThucNhan = k != null ? k.SoLuongThucNhan : null,
                    OrderVatLieu = k != null ? k.OrderVatLieu : null,
                    ViTriDePhoi = k != null ? k.ViTriDePhoi : null,
                    TinhTrangPhoi = k != null ? k.TinhTrangPhoi : null,
                    NgayCapPhoi = k != null ? k.NgayCapPhoi : null,
                    SoLuongCap = k != null ? k.SoLuongCap : null
                };

        return await q.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// Lấy dữ liệu kho của 1 KhPlanDetail cụ thể — dùng cho View.razor thay GetAllAsync
    /// </summary>
    public async Task<KhoVatLieuViewItem?> GetByDetailIdAsync(int khPlanDetailId)
    {
        var q = from d in _db.KhPlanDetails
                join p in _db.KhPlans on d.KhPlanId equals p.KhPlanId
                join c in _db.Customers on p.CustomerId equals c.CustomerId into cGroup
                from c in cGroup.DefaultIfEmpty()
                join k in _db.KhoVatLieus on d.KhPlanDetailId equals (int?)k.PlanDetailId into kGroup
                from k in kGroup.DefaultIfEmpty()
                where d.KhPlanDetailId == khPlanDetailId
                select new KhoVatLieuViewItem
                {
                    KhPlanDetailId    = d.KhPlanDetailId,
                    KhoVatLieuId      = k != null ? k.KhoVatLieuId : 0,
                    KhPlanId          = p.KhPlanId,
                    PlanNo            = p.PlanNo,
                    PlanDate          = p.PlanDate,
                    CustomerName      = c != null ? c.CustomerName : "",
                    SoPO              = d.PurchaseOrder,
                    PartNo            = d.PartNo,
                    DonViTinh         = d.Unit,
                    SoLuongKeHoach    = d.Quantity,
                    ThoiHan           = d.STD,
                    MaVatLieu         = d.Material,
                    CauHinhPhoi       = d.MaterialConfig,
                    GhiChuPhoi        = d.MaterialNotes,
                    NgayNhanPhoi      = k != null ? k.NgayNhanPhoi      : null,
                    SoLuongThucNhan   = k != null ? k.SoLuongThucNhan   : null,
                    OrderVatLieu      = k != null ? k.OrderVatLieu      : null,
                    ViTriDePhoi       = k != null ? k.ViTriDePhoi       : null,
                    TinhTrangPhoi     = k != null ? k.TinhTrangPhoi     : null,
                    NgayCapPhoi       = k != null ? k.NgayCapPhoi       : null,
                    SoLuongCap        = k != null ? k.SoLuongCap        : null
                };

        return await q.AsNoTracking().FirstOrDefaultAsync();
    }

    /// <summary>
    /// Kho cập nhật dữ liệu Cột I -> O (Lưu vào bảng KhoVatLieus & ghi Audit Log vào KhoVatLieuChangeLogs)
    /// </summary>
    public async Task<(bool Success, string? Error)> SaveKhoInputAsync(
        int khPlanDetailId,
        DateTime? ngayNhan,
        int? slNhan,
        string? orderVl,
        string? viTri,
        string? tinhTrang,
        DateTime? ngayCap,
        int? slCap,
        string reason,
        int userId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            return (false, "Lý do thay đổi là bắt buộc (tối thiểu 3 ký tự).");

        var canEdit = await HasPermissionAsync(userId, SystemPermissions.Kho, requireEdit: true);
        if (!canEdit)
            return (false, "Bạn không có quyền chỉnh sửa dữ liệu kho (KHO).");

        var planDetail = await _db.KhPlanDetails.FirstOrDefaultAsync(x => x.KhPlanDetailId == khPlanDetailId);
        if (planDetail == null)
            return (false, "Không tìm thấy dòng Kế hoạch tương ứng.");

        var existing = await _db.KhoVatLieus.FirstOrDefaultAsync(k => k.PlanDetailId == khPlanDetailId);
        var now = DateTime.Now;

        if (existing == null)
        {
            var newItem = new KhoVatLieu
            {
                PlanDetailId = khPlanDetailId,
                SoPO = planDetail.PurchaseOrder,
                PartNo = planDetail.PartNo,
                DonViTinh = planDetail.Unit,
                SoLuongKeHoach = (int)planDetail.Quantity,
                ThoiHan = planDetail.STD,
                MaVatLieu = planDetail.Material,
                CauHinhPhoi = planDetail.MaterialConfig,
                GhiChuPhoi = planDetail.MaterialNotes,
                NgayNhanPhoi = ngayNhan,
                SoLuongThucNhan = slNhan,
                OrderVatLieu = orderVl?.Trim(),
                ViTriDePhoi = viTri?.Trim(),
                TinhTrangPhoi = tinhTrang?.Trim(),
                NgayCapPhoi = ngayCap,
                SoLuongCap = slCap,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = userId
            };
            _db.KhoVatLieus.Add(newItem);
            await _db.SaveChangesAsync();

            _db.KhoVatLieuChangeLogs.Add(new KhoVatLieuChangeLog
            {
                KhoVatLieuId = newItem.KhoVatLieuId,
                FieldName = "Khởi tạo Kho",
                OldValue = null,
                NewValue = $"Nhận: {slNhan}, Vị trí: {viTri}",
                ChangedBy = userId,
                ChangedAt = now,
                Reason = reason
            });
            await _db.SaveChangesAsync();
            return (true, null);
        }

        // Cập nhật và ghi vết thay đổi
        var diffCount = 0;
        diffCount += LogDateDiff("Ngày nhận phôi", existing.NgayNhanPhoi, ngayNhan, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogIntDiff("Số lượng thực nhận", existing.SoLuongThucNhan, slNhan, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogStringDiff("Order vật liệu", existing.OrderVatLieu, orderVl, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogStringDiff("Vị trí để phôi", existing.ViTriDePhoi, viTri, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogStringDiff("Tình trạng phôi", existing.TinhTrangPhoi, tinhTrang, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogDateDiff("Ngày cấp phôi", existing.NgayCapPhoi, ngayCap, existing.KhoVatLieuId, userId, reason, now);
        diffCount += LogIntDiff("Số lượng cấp", existing.SoLuongCap, slCap, existing.KhoVatLieuId, userId, reason, now);

        if (diffCount == 0)
            return (true, null);

        // Đồng bộ lại snapshot nếu kế hoạch có đổi
        existing.SoPO = planDetail.PurchaseOrder;
        existing.PartNo = planDetail.PartNo;
        existing.DonViTinh = planDetail.Unit;
        existing.SoLuongKeHoach = (int)planDetail.Quantity;
        existing.ThoiHan = planDetail.STD;
        existing.MaVatLieu = planDetail.Material;
        existing.CauHinhPhoi = planDetail.MaterialConfig;
        existing.GhiChuPhoi = planDetail.MaterialNotes;

        existing.NgayNhanPhoi = ngayNhan;
        existing.SoLuongThucNhan = slNhan;
        existing.OrderVatLieu = orderVl?.Trim();
        existing.ViTriDePhoi = viTri?.Trim();
        existing.TinhTrangPhoi = tinhTrang?.Trim();
        existing.NgayCapPhoi = ngayCap;
        existing.SoLuongCap = slCap;
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<KhoVatLieuChangeLogItem>> GetChangeLogsAsync(long khoVatLieuId)
    {
        if (khoVatLieuId == 0) return new();

        return await _db.KhoVatLieuChangeLogs
            .Where(l => l.KhoVatLieuId == khoVatLieuId)
            .OrderByDescending(l => l.ChangedAt)
            .Select(l => new KhoVatLieuChangeLogItem
            {
                LogId = l.LogId,
                FieldName = l.FieldName,
                OldValue = l.OldValue,
                NewValue = l.NewValue,
                ChangedAt = l.ChangedAt,
                ChangedByName = l.ChangedByUser != null ? l.ChangedByUser.FullName : $"User #{l.ChangedBy}",
                Reason = l.Reason
            })
            .AsNoTracking()
            .ToListAsync();
    }

    public byte[] ExportToExcel(List<KhoVatLieuViewItem> items)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("DULIEUKHO");

        ws.Range("A1:H1").Merge().Value = "Dữ liệu này link từ kế hoạch";
        ws.Range("I1:O1").Merge().Value = "Kho nhập";
        ws.Range("A1:O1").Style.Font.Bold = true;
        ws.Range("A1:O1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A1:H1").Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2FE");
        ws.Range("I1:O1").Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF08A");

        var headers = new[]
        {
            "Số PO (PO)", "Mã chi tiết (Part No)", "Đơn vị tính", "Số lượng", "Thời hạn",
            "Mã vật liệu", "Cấu hình phôi", "Ghi chú phôi",
            "Ngày nhận phôi", "Số lượng thực nhận", "Order vật liệu", "Vị trí để phôi", "Tình trạng phôi", "Ngày cấp phôi", "Số lượng cấp"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(2, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Fill.BackgroundColor = (i < 8) ? XLColor.FromHtml("#BAE6FD") : XLColor.FromHtml("#FDE047");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int rowIdx = 3;
        foreach (var item in items)
        {
            ws.Cell(rowIdx, 1).SetValue(item.SoPO);
            ws.Cell(rowIdx, 2).SetValue(item.PartNo);
            ws.Cell(rowIdx, 3).SetValue(item.DonViTinh);
            ws.Cell(rowIdx, 4).SetValue(item.SoLuongKeHoach);
            ws.Cell(rowIdx, 5).SetValue(item.ThoiHan.ToString("dd/MM/yyyy"));
            ws.Cell(rowIdx, 6).SetValue(item.MaVatLieu ?? "");
            ws.Cell(rowIdx, 7).SetValue(item.CauHinhPhoi ?? "");
            ws.Cell(rowIdx, 8).SetValue(item.GhiChuPhoi ?? "");

            ws.Cell(rowIdx, 9).SetValue(item.NgayNhanPhoi.HasValue ? item.NgayNhanPhoi.Value.ToString("dd/MM/yyyy") : "No");
            if (item.SoLuongThucNhan.HasValue) ws.Cell(rowIdx, 10).SetValue(item.SoLuongThucNhan.Value);
            ws.Cell(rowIdx, 11).SetValue(item.OrderVatLieu ?? "");
            ws.Cell(rowIdx, 12).SetValue(item.ViTriDePhoi ?? "");
            ws.Cell(rowIdx, 13).SetValue(item.TinhTrangPhoi ?? "");
            ws.Cell(rowIdx, 14).SetValue(item.NgayCapPhoi.HasValue ? item.NgayCapPhoi.Value.ToString("dd/MM/yyyy") : "No");
            if (item.SoLuongCap.HasValue) ws.Cell(rowIdx, 15).SetValue(item.SoLuongCap.Value);

            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rowIdx++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<bool> HasPermissionAsync(int userId, string sectionCode, bool requireEdit = false)
    {
        var user = await _db.Users.Include(u => u.Group).AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive) return false;
        if (user.Group?.GroupCode == "ADMIN") return true;

        var perms = await _userSvc.GetAllUserPermissionsAsync(userId);
        if (perms != null && perms.TryGetValue(sectionCode, out var p))
        {
            return requireEdit ? p.CanEdit : p.CanView;
        }
        return false;
    }

    private int LogStringDiff(string field, string? oldVal, string? newVal, long id, int userId, string reason, DateTime now)
    {
        if (string.Equals(oldVal ?? "", newVal ?? "", StringComparison.Ordinal)) return 0;
        _db.KhoVatLieuChangeLogs.Add(new KhoVatLieuChangeLog
        {
            KhoVatLieuId = id,
            FieldName = field,
            OldValue = oldVal,
            NewValue = newVal,
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason
        });
        return 1;
    }

    private int LogIntDiff(string field, int? oldVal, int? newVal, long id, int userId, string reason, DateTime now)
    {
        if (oldVal == newVal) return 0;
        _db.KhoVatLieuChangeLogs.Add(new KhoVatLieuChangeLog
        {
            KhoVatLieuId = id,
            FieldName = field,
            OldValue = oldVal?.ToString(CultureInfo.InvariantCulture),
            NewValue = newVal?.ToString(CultureInfo.InvariantCulture),
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason
        });
        return 1;
    }

    private int LogDateDiff(string field, DateTime? oldVal, DateTime? newVal, long id, int userId, string reason, DateTime now)
    {
        if (oldVal?.Date == newVal?.Date) return 0;
        _db.KhoVatLieuChangeLogs.Add(new KhoVatLieuChangeLog
        {
            KhoVatLieuId = id,
            FieldName = field,
            OldValue = oldVal?.ToString("dd/MM/yyyy"),
            NewValue = newVal?.ToString("dd/MM/yyyy"),
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason
        });
        return 1;
    }
}