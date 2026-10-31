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

    // [KHO-5 2026-10-31] Cột N / O / Còn lại trên trang đọc sổ Giao nhận (HandoverService.GetKhoIssueSummaryAsync + HandoverLedgerService),
    //   trang điền sau khi nạp — chỉ hiển thị / lọc / Excel, KHÔNG ghi DB. Trước: lọc + sắp xếp + Excel cột N / O dùng 2 trường cũ
    //   NgayCapPhoi / SoLuongCap (không ai nhập nữa) → lọc "SL đã giao" ra trống dù bảng hiện số, Excel cột N / O luôn "No" / rỗng.
    public DateTime? GiaoGcNgayDau { get; set; }
    public decimal GiaoGcXacNhan { get; set; }
    public decimal GiaoGcChoNhan { get; set; }
    public decimal? ConLaiKho { get; set; }
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
                join k in _db.KhoVatLieus.Where(x => x.IsActive) on d.KhPlanDetailId equals k.PlanDetailId into kGroup   // [KHO-ACTIVE 2026-10-30] chỉ dòng Kho đang dùng (như sổ)
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
                join k in _db.KhoVatLieus.Where(x => x.IsActive) on d.KhPlanDetailId equals (int?)k.PlanDetailId into kGroup   // [KHO-ACTIVE 2026-10-30]
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
        int userId,
        bool expectFirstEntry = false)   // [KHO-STALE 2026-10-30] trang đang ở chế độ "nhập lần đầu" (không hỏi lý do / xác nhận)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            return (false, "Lý do thay đổi là bắt buộc (tối thiểu 3 ký tự).");
        // [KHO-LEN 2026-10-30] Máy chủ kiểm độ dài (cột DB) — trước: chữ dài quá làm lỗi SQL, trang sập "An unhandled error"
        if ((orderVl?.Trim().Length ?? 0) > 100) return (false, "Order vật liệu dài quá 100 ký tự.");
        if ((viTri?.Trim().Length ?? 0) > 100)   return (false, "Vị trí để phôi dài quá 100 ký tự.");
        if ((tinhTrang?.Trim().Length ?? 0) > 50) return (false, "Tình trạng phôi dài quá 50 ký tự.");

        // [E4-KHO-NEG 2026-10-28] Máy chủ chặn số âm (trước chỉ form chặn)
        if (slNhan < 0 || slCap < 0)
            return (false, "Số lượng không được âm.");

        var canEdit = await HasPermissionAsync(userId, SystemPermissions.Kho, requireEdit: true);
        if (!canEdit)
            return (false, "Bạn không có quyền chỉnh sửa dữ liệu kho (KHO).");

        var planDetail = await _db.KhPlanDetails.FirstOrDefaultAsync(x => x.KhPlanDetailId == khPlanDetailId);
        if (planDetail == null)
            return (false, "Không tìm thấy dòng Kế hoạch tương ứng.");

        // [KH-GUARD 2026-10-27] Phôi nhận là đầu vào gốc của cả PO: sửa dưới khoá PO (chung WTS + Giao nhận)
        //   và lưu xong không được làm ô nào mới âm (K5/K8) — vd đã giao 40 cho GC mà sửa phôi nhận còn 30.
        //   Không commit = tự rollback khi thoát hàm (using).
        using var trx = await _db.Database.BeginTransactionAsync();
        var lockErr = await PoGuard.LockAsync(_db, khPlanDetailId);
        if (lockErr != null) return (false, lockErr);
        var ledgerBefore = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);

        var existing = await _db.KhoVatLieus.FirstOrDefaultAsync(k => k.PlanDetailId == khPlanDetailId && k.IsActive);   // [KHO-ACTIVE] sổ chỉ đọc dòng đang dùng
        var now = DateTime.Now;
        // [KHO-STALE 2026-10-30] Trang mở từ lúc dòng còn trống (nhập lần đầu, không lý do / xác nhận) nhưng người khác đã nhập trước
        //   → không ghi đè lặng lẽ (trước: ghi đè với lý do "Nhập lần đầu").
        if (expectFirstEntry && existing != null
            && (existing.NgayNhanPhoi != null || existing.SoLuongThucNhan != null || !string.IsNullOrEmpty(existing.OrderVatLieu)
                || !string.IsNullOrEmpty(existing.ViTriDePhoi) || !string.IsNullOrEmpty(existing.TinhTrangPhoi)))
            return (false, "Dòng Kho của PO này vừa được người khác nhập — đóng cửa sổ, tải lại trang rồi sửa (cần lý do + xác nhận).");

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
            var (guardNew, warnNew) = await FinishPhoiGuardAsync(trx, ledgerBefore, khPlanDetailId, 0, slNhan);   // [KH-GUARD]
            if (guardNew != null) return (false, guardNew);
            HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE] báo màn Giao nhận đang mở tính lại dòng PO này
            return (true, warnNew);   // [KHO-4] đã lưu; Error ≠ null lúc Success = cảnh báo (Workflow OFF)
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
        var (guardErr, warnErr) = await FinishPhoiGuardAsync(trx, ledgerBefore, khPlanDetailId, existing.KhoVatLieuId, slNhan);   // [KH-GUARD]
        if (guardErr != null) return (false, guardErr);
        HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE] báo màn Giao nhận đang mở tính lại dòng PO này
        return (true, warnErr);   // [KHO-4] đã lưu; Error ≠ null lúc Success = cảnh báo (Workflow OFF)
    }

    /// <summary>
    /// [KH-GUARD 2026-10-27] Kiểm K5 sau khi đã SaveChanges trong giao dịch: ô nào mới âm thì rollback + báo gốc rễ ([GN-WHY]);
    /// không thì commit. Trả null = đã lưu.
    /// </summary>
    private async Task<(string? Err, string? Warn)> FinishPhoiGuardAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction trx,
        LedgerRow? before, int khPlanDetailId, long khoVatLieuId, int? slNhan)
    {
        string? warn = null;
        if (before != null)
        {
            var k5 = await PoGuard.CheckNoNewNegativeAsync(_db, khPlanDetailId, before);
            if (k5 != null)
            {
                _db.ChangeTracker.Clear();
                await trx.RollbackAsync();
                var why = await HandoverWhy.ExplainAsync(_db, khPlanDetailId,
                    HandoverWhy.OpSetPhoi(khoVatLieuId, slNhan ?? 0), new HashSet<string>());
                return ("Sửa SL phôi nhận: " + k5 + why, null);
            }
            // [KHO-4 2026-10-31] Workflow OFF (N7): vẫn lưu nhưng CẢNH BÁO nếu số phôi mới làm ô nào mới âm (vd đã giao GC 40 mà sửa phôi
            //   nhận còn 30) — như trang Giao nhận / WTS chỉ cảnh báo khi OFF. Trước: lưu im lặng, người sửa không biết số đã lệch.
            if (!before.ApplyWorkflow)
            {
                var after = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
                var bad = after == null ? new List<string>() : HandoverLedgerService.NewNegatives(before, after);
                if (bad.Any())
                    warn = $"Đã lưu — nhưng PO đang tắt Workflow (chỉ cảnh báo, không chặn): số phôi mới làm sai số ở {string.Join("; ", bad)}. "
                         + "Kiểm tra lại SL phôi nhận và các phiếu đã giao từ Kho.";
            }
        }
        await trx.CommitAsync();
        return (null, warn);
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
        ws.Range("I1:Q1").Merge().Value = "Kho nhập";   // [KHO-5] + 2 cột Chờ GC nhận / Còn lại (như bảng trên trang)
        ws.Range("A1:Q1").Style.Font.Bold = true;
        ws.Range("A1:Q1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A1:H1").Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2FE");
        ws.Range("I1:Q1").Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF08A");

        var headers = new[]
        {
            "Số PO (PO)", "Mã chi tiết (Part No)", "Đơn vị tính", "Số lượng", "Thời hạn",
            "Mã vật liệu", "Cấu hình phôi", "Ghi chú phôi",
            "Ngày nhận phôi", "Số lượng thực nhận", "Order vật liệu", "Vị trí để phôi", "Tình trạng phôi",
            "Ngày giao GC", "SL đã giao", "Chờ GC nhận", "Còn lại"   // [KHO-5] cùng chữ + cùng số với bảng trên trang (đọc sổ Giao nhận)
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
            // [KHO-5] N / O / P / Q đọc sổ Giao nhận (trang điền), trước: 2 trường cũ NgayCapPhoi / SoLuongCap
            ws.Cell(rowIdx, 14).SetValue(item.GiaoGcNgayDau.HasValue ? item.GiaoGcNgayDau.Value.ToString("dd/MM/yyyy") : "No");
            if (item.GiaoGcXacNhan > 0 || item.GiaoGcChoNhan > 0) ws.Cell(rowIdx, 15).SetValue(item.GiaoGcXacNhan);
            if (item.GiaoGcChoNhan > 0) ws.Cell(rowIdx, 16).SetValue(item.GiaoGcChoNhan);
            if (item.ConLaiKho.HasValue) ws.Cell(rowIdx, 17).SetValue(item.ConLaiKho.Value);

            ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
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
