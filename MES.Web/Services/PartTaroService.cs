using System.Globalization;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class PartTaroService
{
    private readonly AppDbContext _db;
    private const string TableTag = ProcessStepTable.Taro;
    private const PartMasterArea Area = PartMasterArea.C_HoanThienSP;

    public PartTaroService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<PartTaroStep>> GetActiveByPartAsync(int partId)
    {
        return await _db.PartTaroSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<PartTaroStep>> GetAllByPartAsync(int partId)
    {
        return await _db.PartTaroSteps
            .Where(s => s.PartId == partId)
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<long> AddAsync(int partId, PartTaroStep newStep, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var maxOrder = await _db.PartTaroSteps
            .Where(s => s.PartId == partId)
            .MaxAsync(s => (int?)s.StepOrder) ?? 0;

        newStep.StepId = 0;
        newStep.PartId = partId;
        newStep.StepOrder = maxOrder + 1;
        newStep.CreatedBy = userId;
        newStep.CreatedAt = DateTime.Now;
        newStep.UpdatedBy = null;
        newStep.UpdatedAt = null;
        newStep.IsActive = true;

        _db.PartTaroSteps.Add(newStep);
        await _db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(new
        {
            newStep.StepOrder, newStep.NC, newStep.StepName, newStep.StandardTime, newStep.IsBackup, newStep.ParentNC
        });
        _db.PartProcessStepChangeLogs.Add(BuildLog(
            newStep.StepId, partId, ProcessStepAction.Created, null, snapshot, userId, reason));
        await _db.SaveChangesAsync();

        return newStep.StepId;
    }

    public async Task UpdateAsync(long stepId, PartTaroStep updated, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartTaroSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        if (!existing.IsActive) throw new InvalidOperationException("Không thể sửa dòng đã xóa. Khôi phục trước rồi mới sửa.");

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;

        changedCount += LogStringDiff("NC",           existing.NC,           updated.NC,           stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("StepName",     existing.StepName,     updated.StepName,     stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ParentNC",     existing.ParentNC,     updated.ParentNC,     stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("StandardTime", existing.StandardTime, updated.StandardTime, stepId, pid, userId, reason, now);

        if (changedCount == 0) return;

        existing.NC = updated.NC;
        existing.StepName = updated.StepName;
        existing.StandardTime = updated.StandardTime;
        existing.IsBackup = updated.IsBackup;
        existing.ParentNC = updated.ParentNC;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartTaroSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        if (!existing.IsActive) throw new InvalidOperationException("Dòng này đã bị xóa trước đó");

        existing.IsActive = false;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = DateTime.Now;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, existing.PartId, ProcessStepAction.Deleted, null, null, userId, reason));

        await _db.SaveChangesAsync();
    }

    public async Task RestoreAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartTaroSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        if (existing.IsActive) throw new InvalidOperationException("Dòng đang active, không cần khôi phục");

        existing.IsActive = true;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = DateTime.Now;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, existing.PartId, ProcessStepAction.Restored, null, null, userId, reason));

        await _db.SaveChangesAsync();
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new ArgumentException("Lý do phải có ít nhất 3 ký tự", nameof(reason));
    }

    private async Task EnsurePermissionAsync(int userId)
    {
        var user = await _db.Users.Include(u => u.Group).AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive) throw new UnauthorizedAccessException("User không hợp lệ hoặc đã bị khóa");

        if (!PartMasterPermissionHelper.CanEditArea(user.Group, Area))
            throw new UnauthorizedAccessException($"Bạn không có quyền sửa vùng {PartMasterPermissionHelper.GetAreaName(Area)} (PART_HTSP).");
    }

    private int LogStringDiff(string field, string? oldVal, string? newVal, long stepId, int partId, int userId, string reason, DateTime now)
    {
        if (string.Equals(oldVal ?? "", newVal ?? "", StringComparison.Ordinal)) return 0;
        _db.PartProcessStepChangeLogs.Add(new PartProcessStepChangeLog
        {
            StepTable = TableTag, StepId = stepId, PartId = partId,
            FieldName = field, OldValue = oldVal, NewValue = newVal,
            ChangedBy = userId, ChangedAt = now, Reason = reason
        });
        return 1;
    }

    private int LogDecimalDiff(string field, decimal? oldVal, decimal? newVal, long stepId, int partId, int userId, string reason, DateTime now)
    {
        if (oldVal == newVal) return 0;
        _db.PartProcessStepChangeLogs.Add(new PartProcessStepChangeLog
        {
            StepTable = TableTag, StepId = stepId, PartId = partId,
            FieldName = field, OldValue = oldVal?.ToString(CultureInfo.InvariantCulture),
            NewValue = newVal?.ToString(CultureInfo.InvariantCulture),
            ChangedBy = userId, ChangedAt = now, Reason = reason
        });
        return 1;
    }

    private static PartProcessStepChangeLog BuildLog(long stepId, int partId, string fieldName, string? oldValue, string? newValue, int userId, string reason)
    {
        return new PartProcessStepChangeLog
        {
            StepTable = TableTag, StepId = stepId, PartId = partId,
            FieldName = fieldName, OldValue = oldValue, NewValue = newValue,
            ChangedBy = userId, ChangedAt = DateTime.Now, Reason = reason
        };
    }
}