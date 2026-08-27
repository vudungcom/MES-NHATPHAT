using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class DaoService
{
    private readonly AppDbContext _db;

    public DaoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Dao>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Daos.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }
        return await query
            .OrderBy(d => d.LoaiDao)
            .ThenBy(d => d.TenDao)
            .ToListAsync();
    }

    public async Task<List<string>> GetAllActiveTenDaoAsync()
    {
        return await _db.Daos
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Select(d => d.TenDao)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctLoaiDaoAsync()
    {
        return await _db.Daos
            .AsNoTracking()
            .Where(d => d.IsActive && !string.IsNullOrEmpty(d.LoaiDao))
            .Select(d => d.LoaiDao!)
            .Distinct()
            .OrderBy(l => l)
            .ToListAsync();
    }

    public async Task<Dao?> GetByIdAsync(int id)
    {
        return await _db.Daos.FirstOrDefaultAsync(d => d.DaoId == id);
    }

    public async Task<(bool Success, string? Error, Dao? Data)> CreateAsync(Dao model, int userId)
    {
        if (string.IsNullOrWhiteSpace(model.TenDao))
            return (false, "Tên dao không được để trống.", null);

        var name = model.TenDao.Trim();
        var isDuplicateName = await _db.Daos
            .AnyAsync(d => d.TenDao.ToLower() == name.ToLower() && d.IsActive);
        if (isDuplicateName)
            return (false, $"Tên dao '{name}' đã tồn tại trong hệ thống.", null);

        var code = model.MaDao?.Trim();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var isDuplicateCode = await _db.Daos
                .AnyAsync(d => d.MaDao != null && d.MaDao.ToLower() == code.ToLower() && d.IsActive);
            if (isDuplicateCode)
                return (false, $"Mã dao '{code}' đã tồn tại.", null);
        }

        var now = DateTime.Now;
        model.MaDao = code;
        model.TenDao = name;
        model.LoaiDao = model.LoaiDao?.Trim();
        model.QuyCach = model.QuyCach?.Trim();
        model.HangSX = model.HangSX?.Trim();
        model.VatLieuDao = model.VatLieuDao?.Trim();
        model.ViTriKho = model.ViTriKho?.Trim();
        model.GhiChu = model.GhiChu?.Trim();
        model.IsActive = true;
        model.CreatedBy = userId;
        model.CreatedAt = now;

        try
        {
            _db.Daos.Add(model);
            await _db.SaveChangesAsync();

            _db.DaoChangeLogs.Add(new DaoChangeLog
            {
                DaoId = model.DaoId,
                FieldName = "Tạo mới",
                OldValue = null,
                NewValue = model.TenDao,
                ChangedBy = userId,
                ChangedAt = now,
                Reason = "Khởi tạo thông tin dao"
            });
            await _db.SaveChangesAsync();

            return (true, null, model);
        }
        catch (Exception ex)
        {
            return (false, ex.InnerException?.Message ?? ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Dao updated, string? reason, int userId)
    {
        if (string.IsNullOrWhiteSpace(updated.TenDao))
            return (false, "Tên dao không được để trống.");

        var existing = await _db.Daos.FirstOrDefaultAsync(d => d.DaoId == updated.DaoId);
        if (existing == null) return (false, "Không tìm thấy dao trong hệ thống.");

        var name = updated.TenDao.Trim();
        var isDuplicateName = await _db.Daos
            .AnyAsync(d => d.DaoId != updated.DaoId && d.TenDao.ToLower() == name.ToLower() && d.IsActive);
        if (isDuplicateName)
            return (false, $"Tên dao '{name}' đã được sử dụng bởi mã dao khác.");

        var code = updated.MaDao?.Trim();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var isDuplicateCode = await _db.Daos
                .AnyAsync(d => d.DaoId != updated.DaoId && d.MaDao != null && d.MaDao.ToLower() == code.ToLower() && d.IsActive);
            if (isDuplicateCode)
                return (false, $"Mã dao '{code}' đã được sử dụng.");
        }

        var now = DateTime.Now;
        var r = string.IsNullOrWhiteSpace(reason) ? "Cập nhật thông tin dao" : reason.Trim();

        void LogDiff(string field, string? oldVal, string? newVal)
        {
            oldVal = oldVal?.Trim() ?? "";
            newVal = newVal?.Trim() ?? "";
            if (oldVal != newVal)
            {
                _db.DaoChangeLogs.Add(new DaoChangeLog
                {
                    DaoId = existing.DaoId,
                    FieldName = field,
                    OldValue = oldVal,
                    NewValue = newVal,
                    ChangedBy = userId,
                    ChangedAt = now,
                    Reason = r
                });
            }
        }

        LogDiff("Mã dao", existing.MaDao, code);
        LogDiff("Tên dao", existing.TenDao, name);
        LogDiff("Phân loại", existing.LoaiDao, updated.LoaiDao);
        LogDiff("Quy cách", existing.QuyCach, updated.QuyCach);
        LogDiff("Hãng SX", existing.HangSX, updated.HangSX);
        LogDiff("Vật liệu/Lớp phủ", existing.VatLieuDao, updated.VatLieuDao);
        LogDiff("Vị trí tủ/kho", existing.ViTriKho, updated.ViTriKho);
        LogDiff("SL tồn", existing.SoLuongTon?.ToString(), updated.SoLuongTon?.ToString());
        LogDiff("Ghi chú", existing.GhiChu, updated.GhiChu);

        existing.MaDao = code;
        existing.TenDao = name;
        existing.LoaiDao = updated.LoaiDao?.Trim();
        existing.QuyCach = updated.QuyCach?.Trim();
        existing.HangSX = updated.HangSX?.Trim();
        existing.VatLieuDao = updated.VatLieuDao?.Trim();
        existing.ViTriKho = updated.ViTriKho?.Trim();
        existing.SoLuongTon = updated.SoLuongTon;
        existing.GhiChu = updated.GhiChu?.Trim();
        existing.UpdatedBy = userId;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleActiveAsync(int id, bool isActive, string? reason, int userId)
    {
        var existing = await _db.Daos.FirstOrDefaultAsync(d => d.DaoId == id);
        if (existing == null) return (false, "Không tìm thấy dao.");

        var now = DateTime.Now;
        var oldStatus = existing.IsActive ? "Hoạt động" : "Ngừng";
        var newStatus = isActive ? "Hoạt động" : "Đã xóa/Ngừng";

        existing.IsActive = isActive;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = now;

        _db.DaoChangeLogs.Add(new DaoChangeLog
        {
            DaoId = existing.DaoId,
            FieldName = "Trạng thái",
            OldValue = oldStatus,
            NewValue = newStatus,
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason ?? (isActive ? "Khôi phục hoạt động" : "Ngừng sử dụng dao")
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<DaoChangeLogItem>> GetChangeLogsAsync(int daoId)
    {
        return await _db.DaoChangeLogs
            .Where(l => l.DaoId == daoId)
            .OrderByDescending(l => l.ChangedAt)
            .Include(l => l.ChangedByUser)
            .Select(l => new DaoChangeLogItem
            {
                LogId = l.LogId,
                DaoId = l.DaoId,
                FieldName = l.FieldName,
                OldValue = l.OldValue,
                NewValue = l.NewValue,
                Reason = l.Reason,
                ChangedAt = l.ChangedAt,
                ChangedByName = l.ChangedByUser != null ? l.ChangedByUser.FullName : l.ChangedBy.ToString()
            })
            .AsNoTracking()
            .ToListAsync();
    }
}

public class DaoChangeLogItem
{
    public long LogId { get; set; }
    public int DaoId { get; set; }
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
}