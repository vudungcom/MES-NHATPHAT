using ClosedXML.Excel;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class DoGaService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public DoGaService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<DoGa>> GetAllActiveAsync()
    {
        return await _db.DoGas
            .Where(x => x.IsActive)
            .OrderBy(x => x.DoGaId)
            .ToListAsync();
    }

    public async Task<DoGa?> GetByIdAsync(int id)
    {
        return await _db.DoGas.FirstOrDefaultAsync(x => x.DoGaId == id && x.IsActive);
    }

    /// <summary>
    /// Thêm mới đồ gá
    /// </summary>
    public async Task<(bool Success, string? Error)> CreateAsync(DoGa model, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        if (string.IsNullOrWhiteSpace(model.TenDoGa)) return (false, "Tên đồ gá không được để trống.");

        var now = DateTime.Now;
        model.TenDoGa = model.TenDoGa.Trim();
        model.SoLuong = string.IsNullOrWhiteSpace(model.SoLuong) ? "1" : model.SoLuong.Trim();
        model.TrangThai = "Sẵn sàng";
        model.IsActive = true;
        model.CreatedAt = now;
        model.CreatedBy = currentUserId;

        _db.DoGas.Add(model);

        _db.DoGaChangeLogs.Add(new DoGaChangeLog
        {
            DoGaId = model.DoGaId,
            FieldName = "Tạo mới đồ gá",
            OldValue = null,
            NewValue = model.TenDoGa,
            Reason = "Khởi tạo dữ liệu đồ gá",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Chỉnh sửa thông tin đồ gá & phụ kiện
    /// </summary>
    public async Task<(bool Success, string? Error)> UpdateAsync(DoGa updatedModel, string? reason, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var item = await _db.DoGas.FirstOrDefaultAsync(x => x.DoGaId == updatedModel.DoGaId && x.IsActive);
        if (item == null) return (false, "Không tìm thấy đồ gá.");

        var now = DateTime.Now;
        var logs = new List<DoGaChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal)
        {
            oldVal = oldVal?.Trim() ?? "";
            newVal = newVal?.Trim() ?? "";
            if (oldVal != newVal)
            {
                logs.Add(new DoGaChangeLog
                {
                    DoGaId = item.DoGaId,
                    FieldName = fieldName,
                    OldValue = oldVal,
                    NewValue = newVal,
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                });
            }
        }

        CheckAndLog("Tên đồ gá", item.TenDoGa, updatedModel.TenDoGa);
        CheckAndLog("Số lượng", item.SoLuong, updatedModel.SoLuong);
        CheckAndLog("Bu lông", item.BuLong, updatedModel.BuLong);
        CheckAndLog("Bu lông định vị", item.BuLongDinhVi, updatedModel.BuLongDinhVi);
        CheckAndLog("Chốt", item.Chot, updatedModel.Chot);
        CheckAndLog("Đệm", item.Dem, updatedModel.Dem);
        CheckAndLog("Kẹp", item.Kep, updatedModel.Kep);
        CheckAndLog("Long đen", item.LongDen, updatedModel.LongDen);
        CheckAndLog("Sản phẩm sử dụng", item.SanPhamSuDung, updatedModel.SanPhamSuDung);
        CheckAndLog("Ghi chú", item.GhiChu, updatedModel.GhiChu);

        if (logs.Any())
        {
            item.TenDoGa = updatedModel.TenDoGa.Trim();
            item.SoLuong = updatedModel.SoLuong?.Trim() ?? "1";
            item.BuLong = updatedModel.BuLong?.Trim();
            item.BuLongDinhVi = updatedModel.BuLongDinhVi?.Trim();
            item.Chot = updatedModel.Chot?.Trim();
            item.Dem = updatedModel.Dem?.Trim();
            item.Kep = updatedModel.Kep?.Trim();
            item.LongDen = updatedModel.LongDen?.Trim();
            item.SanPhamSuDung = updatedModel.SanPhamSuDung?.Trim();
            item.GhiChu = updatedModel.GhiChu?.Trim();
            item.UpdatedAt = now;
            item.UpdatedBy = currentUserId;

            _db.DoGaChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    /// <summary>
    /// Thao tác MƯỢN ĐỒ GÁ (Ghi log Transaction mượn, cập nhật trạng thái Đang mượn)
    /// </summary>
    public async Task<(bool Success, string? Error)> MuonDoGaAsync(
        int doGaId, 
        string nguoiMuon, 
        DateTime ngayMuon, 
        DateTime? ngayTraDuKien, 
        string? lyDo, 
        int currentUserId, 
        string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        if (string.IsNullOrWhiteSpace(nguoiMuon)) return (false, "Vui lòng nhập tên người mượn.");

        var item = await _db.DoGas.FirstOrDefaultAsync(x => x.DoGaId == doGaId && x.IsActive);
        if (item == null) return (false, "Không tìm thấy đồ gá.");

        var now = DateTime.Now;

        // 1. Thêm bản ghi giao dịch mượn mới (Append-only)
        var muonLog = new DoGaMuonTraLog
        {
            DoGaId = item.DoGaId,
            NguoiMuon = nguoiMuon.Trim(),
            NgayMuon = ngayMuon,
            NgayTraDuKien = ngayTraDuKien,
            LyDoMuon = lyDo?.Trim(),
            TrangThai = "Đang mượn",
            CreatedAt = now,
            CreatedBy = currentUserId
        };
        _db.DoGaMuonTraLogs.Add(muonLog);

        // 2. Cập nhật trạng thái hiển thị trên Master Table
        item.TrangThai = "Đang mượn";
        item.NguoiMuonHienTai = nguoiMuon.Trim();
        item.NgayMuonHienTai = ngayMuon;
        item.NgayTraDuKien = ngayTraDuKien;
        item.LyDoMuonHienTai = lyDo?.Trim();
        item.UpdatedAt = now;
        item.UpdatedBy = currentUserId;

        // 3. Ghi ChangeLog
        _db.DoGaChangeLogs.Add(new DoGaChangeLog
        {
            DoGaId = item.DoGaId,
            FieldName = "Mượn đồ gá",
            OldValue = "Sẵn sàng",
            NewValue = $"Người mượn: {nguoiMuon.Trim()} (Ngày: {ngayMuon:dd/MM/yyyy HH:mm})",
            Reason = lyDo,
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Thao tác TRẢ ĐỒ GÁ (Chốt Transaction mượn, đưa đồ gá về Sẵn sàng, xóa trắng thông tin mượn)
    /// </summary>
    public async Task<(bool Success, string? Error)> TraDoGaAsync(
        int doGaId, 
        string? ghiChuTra, 
        int currentUserId, 
        string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var item = await _db.DoGas.FirstOrDefaultAsync(x => x.DoGaId == doGaId && x.IsActive);
        if (item == null) return (false, "Không tìm thấy đồ gá.");

        var now = DateTime.Now;

        // 1. Chốt bản ghi mượn gần nhất đang mở
        var activeLog = await _db.DoGaMuonTraLogs
            .Where(x => x.DoGaId == doGaId && x.TrangThai == "Đang mượn")
            .OrderByDescending(x => x.NgayMuon)
            .FirstOrDefaultAsync();

        if (activeLog != null)
        {
            activeLog.TrangThai = "Đã trả";
            activeLog.NgayTraThucTe = now;
            activeLog.GhiChuTra = ghiChuTra?.Trim();
            activeLog.ReturnedAt = now;
            activeLog.ReturnedBy = currentUserId;
        }

        var oldNguoiMuon = item.NguoiMuonHienTai;

        // 2. Trả đồ gá về trạng thái Sẵn sàng và XÓA TRẮNG các ô mượn
        item.TrangThai = "Sẵn sàng";
        item.NguoiMuonHienTai = null;
        item.NgayMuonHienTai = null;
        item.NgayTraDuKien = null;
        item.LyDoMuonHienTai = null;
        item.UpdatedAt = now;
        item.UpdatedBy = currentUserId;

        // 3. Ghi ChangeLog
        _db.DoGaChangeLogs.Add(new DoGaChangeLog
        {
            DoGaId = item.DoGaId,
            FieldName = "Trả đồ gá",
            OldValue = $"Đang mượn bởi: {oldNguoiMuon}",
            NewValue = "Đã trả về kho (Sẵn sàng)",
            Reason = ghiChuTra,
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Xóa đồ gá (Soft delete)
    /// </summary>
    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int doGaId, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var item = await _db.DoGas.FirstOrDefaultAsync(x => x.DoGaId == doGaId && x.IsActive);
        if (item == null) return (false, "Không tìm thấy đồ gá.");

        if (item.TrangThai == "Đang mượn")
        {
            return (false, $"Đồ gá đang được mượn bởi '{item.NguoiMuonHienTai}'. Hãy hoàn tất việc trả đồ gá trước khi xóa.");
        }

        var now = DateTime.Now;
        item.IsActive = false;
        item.UpdatedAt = now;
        item.UpdatedBy = currentUserId;

        _db.DoGaChangeLogs.Add(new DoGaChangeLog
        {
            DoGaId = item.DoGaId,
            FieldName = "Trạng thái",
            OldValue = "Đang sử dụng",
            NewValue = "Đã xóa khỏi danh mục",
            Reason = "Xóa đồ gá",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<DoGaMuonTraLog>> GetMuonTraLogsAsync(int doGaId)
    {
        return await _db.DoGaMuonTraLogs
            .Include(x => x.CreatedByUser)
            .Include(x => x.ReturnedByUser)
            .Where(x => x.DoGaId == doGaId)
            .OrderByDescending(x => x.NgayMuon)
            .ToListAsync();
    }

    public async Task<List<DoGaChangeLog>> GetChangeLogsAsync(int doGaId)
    {
        return await _db.DoGaChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.DoGaId == doGaId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Xuất danh sách Đồ gá ra Excel chuẩn 2 tầng Header
    /// </summary>
    public async Task<byte[]> ExportExcelAsync(List<DoGa> data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Theo dõi đồ gá");

        // 1. Tiêu đề chính
        ws.Cell("A1").Value = "BẢNG THEO DÕI ĐỒ GÁ + PHỤ KIỆN ĐI KÈM & TÌNH TRẠNG MƯỢN TRẢ";
        ws.Range("A1:N1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(XLColor.FromHtml("#1e3a8a"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell("A2").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Tổng số: {data.Count} bộ đồ gá";
        ws.Range("A2:N2").Merge().Style
            .Font.SetItalic(true)
            .Font.SetFontSize(10)
            .Font.SetFontColor(XLColor.FromHtml("#64748b"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        // 2. Header 2 tầng (Row 3 & 4)
        // Cột STT, Tên Đồ Gá, Số Lượng (Merge dọc)
        ws.Range("A3:A4").Merge().Value = "STT";
        ws.Range("B3:B4").Merge().Value = "Tên Đồ Gá";
        ws.Range("C3:C4").Merge().Value = "Số Lượng";

        // Nhóm Phụ Kiện (Merge ngang D3:I3)
        ws.Range("D3:I3").Merge().Value = "Phụ Kiện Đi Kèm";
        ws.Cell("D4").Value = "Bu lông";
        ws.Cell("E4").Value = "Bu lông định vị";
        ws.Cell("F4").Value = "Chốt";
        ws.Cell("G4").Value = "Đệm";
        ws.Cell("H4").Value = "Kẹp";
        ws.Cell("I4").Value = "Long đen";

        // Các cột thông tin bổ sung (Merge dọc)
        ws.Range("J3:J4").Merge().Value = "Sản phẩm sử dụng";
        ws.Range("K3:K4").Merge().Value = "Ghi chú";
        ws.Range("L3:L4").Merge().Value = "Người mượn";
        ws.Range("M3:M4").Merge().Value = "Ngày mượn";
        ws.Range("N3:N4").Merge().Value = "Ngày trả";

        // Định dạng Header
        var headerRange = ws.Range("A3:N4");
        headerRange.Style
            .Font.SetBold(true)
            .Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#2b7bc4"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText(true);

        ws.Row(3).Height = 22;
        ws.Row(4).Height = 22;

        // 3. Đổ dữ liệu
        int r = 5;
        int stt = 1;
        foreach (var item in data)
        {
            ws.Cell(r, 1).SetValue(stt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 2).SetValue(item.TenDoGa).Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#d81b60"));
            ws.Cell(r, 3).SetValue(item.SoLuong).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            
            ws.Cell(r, 4).SetValue(item.BuLong ?? "");
            ws.Cell(r, 5).SetValue(item.BuLongDinhVi ?? "");
            ws.Cell(r, 6).SetValue(item.Chot ?? "");
            ws.Cell(r, 7).SetValue(item.Dem ?? "");
            ws.Cell(r, 8).SetValue(item.Kep ?? "");
            ws.Cell(r, 9).SetValue(item.LongDen ?? "");

            ws.Cell(r, 10).SetValue(item.SanPhamSuDung ?? "");
            ws.Cell(r, 11).SetValue(item.GhiChu ?? "");

            // Thông tin mượn trả (Nếu đã trả thì để trống)
            if (item.TrangThai == "Đang mượn")
            {
                ws.Cell(r, 12).SetValue(item.NguoiMuonHienTai ?? "").Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#c2410c"));
                ws.Cell(r, 13).SetValue(item.NgayMuonHienTai.HasValue ? item.NgayMuonHienTai.Value.ToString("dd/MM/yyyy HH:mm") : "");
                ws.Cell(r, 14).SetValue(item.NgayTraDuKien.HasValue ? item.NgayTraDuKien.Value.ToString("dd/MM/yyyy HH:mm") : "");
                ws.Range(r, 1, r, 14).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#fffbeb"));
            }
            else
            {
                ws.Cell(r, 12).SetValue("-").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 13).SetValue("-").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Cell(r, 14).SetValue("-").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }

            ws.Row(r).Height = 20;
            r++;
            stt++;
        }

        var dataRange = ws.Range(3, 1, r - 1, 14);
        dataRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}