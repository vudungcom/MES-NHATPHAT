using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class CaLamViecService
{
    private readonly AppDbContext _db;
    public CaLamViecService(AppDbContext db) { _db = db; }

    public Task<List<CaLamViec>> GetAllAsync()
        => _db.CaLamViecs
              .Include(x => x.NghiGiaiLaos)
              .OrderBy(x => x.SortOrder).ThenBy(x => x.CaId)
              .AsNoTracking().ToListAsync();

    public Task<List<CaLamViec>> GetActiveAsync()
        => _db.CaLamViecs
              .Include(x => x.NghiGiaiLaos)
              .Where(x => x.IsActive)
              .OrderBy(x => x.SortOrder)
              .AsNoTracking().ToListAsync();

    public async Task<CaLamViec?> GetByIdAsync(int id)
        => await _db.CaLamViecs
                    .Include(x => x.NghiGiaiLaos)
                    .FirstOrDefaultAsync(x => x.CaId == id);

    public async Task<(bool Ok, string Error)> SaveAsync(CaLamViec ca, List<CaLamViec_NghiGiaiLao> nghiList)
    {
        if (string.IsNullOrWhiteSpace(ca.TenCa))
            return (false, "Tên ca không được để trống.");

        foreach (var n in nghiList)
        {
            if (n.NghiKetThuc <= n.NghiBatDau)
                return (false, $"Nghỉ giải lao: giờ kết thúc phải sau giờ bắt đầu ({n.NghiBatDau:HH\\:mm}).");
        }

        if (ca.CaId == 0)
        {
            _db.CaLamViecs.Add(ca);
            await _db.SaveChangesAsync();
            foreach (var n in nghiList) { n.CaId = ca.CaId; _db.CaLamViec_NghiGiaiLaos.Add(n); }
        }
        else
        {
            _db.CaLamViecs.Update(ca);
            // Xóa nghỉ cũ, thêm mới
            var old = await _db.CaLamViec_NghiGiaiLaos.Where(x => x.CaId == ca.CaId).ToListAsync();
            _db.CaLamViec_NghiGiaiLaos.RemoveRange(old);
            foreach (var n in nghiList) { n.CaId = ca.CaId; n.NghiId = 0; _db.CaLamViec_NghiGiaiLaos.Add(n); }
        }

        await _db.SaveChangesAsync();
        return (true, "");
    }

    public async Task DeleteAsync(int id)
    {
        var ca = await _db.CaLamViecs.FindAsync(id);
        if (ca != null) { _db.CaLamViecs.Remove(ca); await _db.SaveChangesAsync(); }
    }

    public async Task ToggleActiveAsync(int id)
    {
        var ca = await _db.CaLamViecs.FindAsync(id);
        if (ca == null) return;
        ca.IsActive = !ca.IsActive;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Chuyển danh sách nghỉ giải lao của ca thành các DateTime segment
    /// trong khung [dayStart, dayEnd], xử lý ca vắt đêm đúng cách.
    /// </summary>
    public static List<(DateTime Start, DateTime End)> GetBreakSegments(
        CaLamViec ca, DateTime dayStart, DateTime dayEnd)
    {
        var result = new List<(DateTime, DateTime)>();
        var date = DateOnly.FromDateTime(dayStart);

        foreach (var n in ca.NghiGiaiLaos)
        {
            DateTime s, e;

            if (ca.IsOvernight && n.NghiBatDau < ca.GioBatDau)
            {
                // Nghỉ thuộc phần sang ngày hôm sau
                s = date.AddDays(1).ToDateTime(n.NghiBatDau);
                e = date.AddDays(1).ToDateTime(n.NghiKetThuc);
            }
            else
            {
                s = date.ToDateTime(n.NghiBatDau);
                e = date.ToDateTime(n.NghiKetThuc);
            }

            // Clip vào khung giờ hiển thị
            if (e > dayStart && s < dayEnd)
                result.Add((s < dayStart ? dayStart : s, e > dayEnd ? dayEnd : e));
        }

        return result;
    }
}
