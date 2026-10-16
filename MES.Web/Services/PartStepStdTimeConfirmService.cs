using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Xác nhận T.Chuẩn per-dòng cho Vùng C (Taro/Bavia/Washing), D (Inspection), E (Packaging).
/// Append-only trên bảng PartStepStdTimeConfirms.
/// </summary>
public class PartStepStdTimeConfirmService
{
    private readonly AppDbContext _db;

    public static readonly string[] Tables = { "Taro", "Bavia", "Washing", "Inspection", "Packaging" };

    public PartStepStdTimeConfirmService(AppDbContext db) { _db = db; }

    private static PartMasterArea AreaOf(string table) => table switch
    {
        "Taro" or "Bavia" or "Washing" => PartMasterArea.C_HoanThienSP,
        "Inspection"                   => PartMasterArea.D_KiemTra,
        "Packaging"                    => PartMasterArea.E_DongGoi,
        _ => throw new ArgumentException($"StepTable không hợp lệ: {table}")
    };

    /// <summary>
    /// Trạng thái xác nhận hiện tại (bản ghi mới nhất, chỉ trả về những dòng đang IsConfirmed)
    /// của TOÀN BỘ step thuộc 1 Part — 1 truy vấn duy nhất.
    /// Key = (StepTable, StepId).
    /// </summary>
    public async Task<Dictionary<(string Table, long StepId), (string? ByName, DateTime At)>> GetConfirmsAsync(int partId)
    {
        var rows = await _db.PartStepStdTimeConfirms
            .Where(x => x.PartId == partId)
            .Include(x => x.ConfirmedByUser)
            .AsNoTracking()
            .ToListAsync();

        var result = new Dictionary<(string, long), (string?, DateTime)>();
        foreach (var g in rows.GroupBy(x => (x.StepTable, x.StepId)))
        {
            var latest = g.OrderByDescending(x => x.ConfirmedAt).ThenByDescending(x => x.Id).First();
            if (latest.IsConfirmed)
                result[g.Key] = (latest.ConfirmedByUser?.FullName, latest.ConfirmedAt);
        }
        return result;
    }

    /// <summary>
    /// Đặt trạng thái xác nhận mong muốn. Chỉ ghi bản ghi mới khi khác trạng thái hiện tại.
    /// Trả về trạng thái sau cùng.
    /// </summary>
    public async Task<bool> SetConfirmAsync(string table, long stepId, int partId, bool want, int userId)
    {
        var area = AreaOf(table);

        var user = await _db.Users.Include(u => u.Group).AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive)
            throw new UnauthorizedAccessException("User không hợp lệ hoặc đã bị khóa");
        if (!PartMasterPermissionHelper.CanEditArea(user.Group, area))
            throw new UnauthorizedAccessException(
                $"Bạn không có quyền sửa vùng {PartMasterPermissionHelper.GetAreaName(area)}.");

        var latest = await _db.PartStepStdTimeConfirms
            .Where(x => x.StepTable == table && x.StepId == stepId)
            .OrderByDescending(x => x.ConfirmedAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        var current = latest?.IsConfirmed ?? false;
        if (current == want) return current;

        _db.PartStepStdTimeConfirms.Add(new PartStepStdTimeConfirm
        {
            StepTable = table, StepId = stepId, PartId = partId,
            IsConfirmed = want, ConfirmedBy = userId, ConfirmedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return want;
    }

    /// <summary>
    /// Dùng cho trang Hiệu suất (WTS): tập các dòng T.Chuẩn ĐÃ được xác nhận của 1 Part,
    /// khoá theo "Bảng|Mã" với Mã = NC và (nếu có) WtsTaskCode của step đang active.
    /// </summary>
    public async Task<HashSet<string>> GetConfirmedCodeKeysAsync(int partId)
    {
        var confirms = await GetConfirmsAsync(partId);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (confirms.Count == 0) return keys;

        void Add(string table, long id, string? nc, string? wts = null)
        {
            if (!confirms.ContainsKey((table, id))) return;
            if (!string.IsNullOrEmpty(nc))  keys.Add($"{table}|{nc}");
            if (!string.IsNullOrEmpty(wts)) keys.Add($"{table}|{wts}");
        }

        foreach (var s in await _db.PartTaroSteps.AsNoTracking().Where(s => s.PartId == partId && s.IsActive).ToListAsync())
            Add("Taro", s.StepId, s.NC, s.WtsTaskCode);
        foreach (var s in await _db.PartBaviaSteps.AsNoTracking().Where(s => s.PartId == partId && s.IsActive).ToListAsync())
            Add("Bavia", s.StepId, s.NC);
        foreach (var s in await _db.PartWashingSteps.AsNoTracking().Where(s => s.PartId == partId && s.IsActive).ToListAsync())
            Add("Washing", s.StepId, s.NC);
        foreach (var s in await _db.PartInspectionSteps.AsNoTracking().Where(s => s.PartId == partId && s.IsActive).ToListAsync())
            Add("Inspection", s.StepId, s.NC);
        foreach (var s in await _db.PartPackagingSteps.AsNoTracking().Where(s => s.PartId == partId && s.IsActive).ToListAsync())
            Add("Packaging", s.StepId, s.NC);
        return keys;
    }
}
