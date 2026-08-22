using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class ThietBiService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public ThietBiService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<ThietBi>> GetAllActiveAsync()
    {
        var list = await _db.ThietBis
            .Where(t => t.IsActive)
            .OrderBy(t => t.BoPhan).ThenBy(t => t.SoMay)
            .ToListAsync();

        // Lấy danh sách ID các máy đang không ở trạng thái 'Hoạt động'
        var inactiveIds = list.Where(t => t.TrangThai != "Hoạt động").Select(t => t.ThietBiId).ToList();
        if (inactiveIds.Any())
        {
            var logs = await _db.ThietBiChangeLogs
                .Where(x => inactiveIds.Contains(x.ThietBiId))
                .OrderByDescending(x => x.ChangedAt)
                .ToListAsync();

            var logDict = logs
                .GroupBy(x => x.ThietBiId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var may in list)
            {
                if (logDict.TryGetValue(may.ThietBiId, out var log) && log != null)
                {
                    may.LyDoHienTai = log.Reason;
                    may.ThoiGianDungTu = log.ThoiGianBatDau ?? log.ChangedAt;
                }
            }
        }

        return list;
    }

    public async Task<ThietBi?> GetByIdAsync(int thietBiId)
    {
        return await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == thietBiId);
    }

    public async Task<ThietBiChangeLog?> GetLatestOpenIncidentAsync(int thietBiId)
    {
        return await _db.ThietBiChangeLogs
            .Where(x => x.ThietBiId == thietBiId && x.ThoiGianBatDau != null && x.ThoiGianKetThuc == null)
            .OrderByDescending(x => x.ChangedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<(bool Success, string? Error)> CreateAsync(ThietBi thietBi, int currentUserId)
    {
        if (string.IsNullOrWhiteSpace(thietBi.SoMay)) return (false, "Số máy không được để trống.");

        var isDuplicate = await _db.ThietBis.AnyAsync(t => t.SoMay == thietBi.SoMay.Trim() && t.IsActive);
        if (isDuplicate) return (false, $"Số máy '{thietBi.SoMay}' đã tồn tại trong hệ thống.");

        thietBi.SoMay = thietBi.SoMay.Trim();
        thietBi.CreatedAt = DateTime.Now;
        thietBi.CreatedBy = currentUserId;
        thietBi.IsActive = true;

        _db.ThietBis.Add(thietBi);
        await _db.SaveChangesAsync();

        _db.ThietBiChangeLogs.Add(new ThietBiChangeLog
        {
            ThietBiId = thietBi.ThietBiId,
            FieldName = "Tạo mới",
            OldValue = null,
            NewValue = $"{thietBi.SoMay} - {thietBi.TenMay} ({thietBi.BoPhan})",
            Reason = "Khởi tạo thiết bị",
            ChangedAt = DateTime.Now,
            ChangedBy = currentUserId
        });
        await _db.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        ThietBi updatedModel, 
        string? reason, 
        DateTime? thoiGianBatDau, 
        DateTime? thoiGianKetThuc, 
        int currentUserId, 
        string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN không chính xác.");

        var existing = await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == updatedModel.ThietBiId);
        if (existing == null) return (false, "Không tìm thấy thiết bị.");

        if (existing.SoMay != updatedModel.SoMay.Trim())
        {
            var isDuplicate = await _db.ThietBis.AnyAsync(t => t.SoMay == updatedModel.SoMay.Trim() && t.ThietBiId != updatedModel.ThietBiId && t.IsActive);
            if (isDuplicate) return (false, $"Số máy '{updatedModel.SoMay}' đã tồn tại ở thiết bị khác.");
        }

        var now = DateTime.Now;

        // Chặn thời gian tương lai
        if (thoiGianBatDau.HasValue && thoiGianBatDau.Value > now)
        {
            return (false, $"Thời gian bắt đầu ({thoiGianBatDau.Value:HH:mm:ss}) không được lớn hơn hiện tại ({now:HH:mm:ss}).");
        }

        if (thoiGianKetThuc.HasValue && thoiGianKetThuc.Value > now)
        {
            return (false, $"Thời gian khắc phục ({thoiGianKetThuc.Value:HH:mm:ss}) không được lớn hơn hiện tại ({now:HH:mm:ss}).");
        }

        int? soPhutDung = null;
        if (updatedModel.TrangThai != "Hoạt động")
        {
            thoiGianKetThuc = null;
            soPhutDung = null;
            if (!thoiGianBatDau.HasValue) thoiGianBatDau = now;
        }
        else
        {
            if (existing.TrangThai != "Hoạt động")
            {
                var openIncident = await GetLatestOpenIncidentAsync(existing.ThietBiId);
                thoiGianBatDau = openIncident?.ThoiGianBatDau ?? thoiGianBatDau ?? now;
                thoiGianKetThuc = thoiGianKetThuc ?? now;

                if (thoiGianKetThuc.Value < thoiGianBatDau.Value)
                {
                    return (false, "Thời gian khắc phục xong không được nhỏ hơn thời gian bắt đầu hỏng.");
                }
                soPhutDung = (int)Math.Round((thoiGianKetThuc.Value - thoiGianBatDau.Value).TotalMinutes);

                if (openIncident != null)
                {
                    openIncident.ThoiGianKetThuc = thoiGianKetThuc;
                    openIncident.SoPhutDung = soPhutDung;
                }
            }
            else
            {
                thoiGianBatDau = null;
                thoiGianKetThuc = null;
                soPhutDung = null;
            }
        }

        var changeLogs = new List<ThietBiChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal, bool isStatusChange = false)
        {
            oldVal = oldVal ?? "";
            newVal = newVal ?? "";
            if (oldVal.Trim() != newVal.Trim())
            {
                var log = new ThietBiChangeLog
                {
                    ThietBiId = existing.ThietBiId,
                    FieldName = fieldName,
                    OldValue = oldVal.Trim(),
                    NewValue = newVal.Trim(),
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                };

                if (isStatusChange)
                {
                    log.ThoiGianBatDau = thoiGianBatDau;
                    log.ThoiGianKetThuc = thoiGianKetThuc;
                    log.SoPhutDung = soPhutDung;
                }

                changeLogs.Add(log);
            }
        }

        CheckAndLog("Số máy", existing.SoMay, updatedModel.SoMay);
        CheckAndLog("Bộ phận", existing.BoPhan, updatedModel.BoPhan);
        CheckAndLog("Mã máy", existing.MaMay, updatedModel.MaMay);
        CheckAndLog("Tên máy", existing.TenMay, updatedModel.TenMay);
        CheckAndLog("Loại máy", existing.LoaiMay, updatedModel.LoaiMay);
        CheckAndLog("Trạng thái", existing.TrangThai, updatedModel.TrangThai, isStatusChange: true);

        if (!changeLogs.Any() && thoiGianBatDau.HasValue)
        {
            changeLogs.Add(new ThietBiChangeLog
            {
                ThietBiId = existing.ThietBiId,
                FieldName = "Nhật ký dừng máy",
                OldValue = existing.TrangThai,
                NewValue = updatedModel.TrangThai,
                Reason = reason,
                ThoiGianBatDau = thoiGianBatDau,
                ThoiGianKetThuc = thoiGianKetThuc,
                SoPhutDung = soPhutDung,
                ChangedAt = now,
                ChangedBy = currentUserId
            });
        }

        if (changeLogs.Any())
        {
            existing.SoMay = updatedModel.SoMay.Trim();
            existing.BoPhan = updatedModel.BoPhan?.Trim() ?? "";
            existing.MaMay = updatedModel.MaMay?.Trim() ?? "";
            existing.TenMay = updatedModel.TenMay?.Trim() ?? "";
            existing.LoaiMay = updatedModel.LoaiMay?.Trim() ?? "";
            existing.TrangThai = updatedModel.TrangThai?.Trim() ?? "Hoạt động";
            existing.UpdatedAt = now;
            existing.UpdatedBy = currentUserId;

            _db.ThietBiChangeLogs.AddRange(changeLogs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int thietBiId, int currentUserId, string pin)
    {
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN không chính xác.");

        var thietBi = await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == thietBiId);
        if (thietBi == null) return (false, "Không tìm thấy thiết bị.");
        if (!thietBi.IsActive) return (false, "Thiết bị này đã bị xóa.");

        var now = DateTime.Now;
        thietBi.IsActive = false;
        thietBi.TrangThai = "Đã xóa";
        thietBi.UpdatedAt = now;
        thietBi.UpdatedBy = currentUserId;

        _db.ThietBiChangeLogs.Add(new ThietBiChangeLog
        {
            ThietBiId = thietBi.ThietBiId,
            FieldName = "Trạng thái",
            OldValue = "Hoạt động",
            NewValue = "Đã xóa (Soft Delete)",
            Reason = "Xóa thiết bị khỏi hệ thống",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ThietBiChangeLog>> GetChangeLogsAsync(int thietBiId)
    {
        return await _db.ThietBiChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.ThietBiId == thietBiId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }
}