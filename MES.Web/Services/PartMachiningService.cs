using System.Globalization;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service quản lý bảng công đoạn "Quy trình gia công" (PartMachiningSteps).
/// Vùng B — Được bảo vệ độc lập bằng quyền PART_GC trên Ma trận phân quyền.
/// </summary>
public class PartMachiningService
{
    private readonly AppDbContext _db;
    private const string TableTag = ProcessStepTable.Machining;
    private const PartMasterArea Area = PartMasterArea.B_Machining;

    public PartMachiningService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<PartMachiningStep>> GetActiveByPartAsync(int partId)
    {
        return await _db.PartMachiningSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.Timings.Where(t => t.IsActive))
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<PartMachiningStep>> GetAllByPartAsync(int partId)
    {
        return await _db.PartMachiningSteps
            .Where(s => s.PartId == partId)
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.Timings.Where(t => t.IsActive))
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<PartMachiningStep?> GetByIdAsync(long stepId)
    {
        return await _db.PartMachiningSteps
            .Include(s => s.Part)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StepId == stepId);
    }

    public async Task<List<PartProcessStepChangeLog>> GetHistoryAsync(long stepId)
    {
        return await _db.PartProcessStepChangeLogs
            .Where(l => l.StepTable == TableTag && l.StepId == stepId)
            .OrderByDescending(l => l.ChangedAt)
            .Include(l => l.ChangedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<long> AddAsync(int partId, PartMachiningStep newStep, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var maxOrder = await _db.PartMachiningSteps
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

        _db.PartMachiningSteps.Add(newStep);
        await _db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(new
        {
            newStep.StepOrder, newStep.NC, newStep.Drawing,
            newStep.MachineRegistered,            newStep.FixtureType,
            newStep.ToolType, newStep.TimingMachine, newStep.IsBackup, newStep.ParentNC,
            newStep.SetupTime, newStep.MachiningTime, newStep.InspectionTime,
            newStep.PreparationTime, newStep.TrialRunTime
        });
        _db.PartProcessStepChangeLogs.Add(BuildLog(
            newStep.StepId, partId, ProcessStepAction.Created,
            oldValue: null, newValue: snapshot, userId, reason));
        await _db.SaveChangesAsync();

        return newStep.StepId;
    }

    public async Task UpdateAsync(long stepId, PartMachiningStep updated, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        if (!existing.IsActive)
            throw new InvalidOperationException("Không thể sửa dòng đã xóa.");

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;

        changedCount += LogStringDiff("NC",                 existing.NC,                 updated.NC,                 stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("Drawing",            existing.Drawing,            updated.Drawing,            stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("MachineRegistered",  existing.MachineRegistered,  updated.MachineRegistered,  stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("FixtureType",        existing.FixtureType,        updated.FixtureType,        stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ToolType",           existing.ToolType,           updated.ToolType,           stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("TimingMachine",      existing.TimingMachine,      updated.TimingMachine,      stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ParentNC",           existing.ParentNC,           updated.ParentNC,           stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("SetupTime",         existing.SetupTime,          updated.SetupTime,          stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("MachiningTime",     existing.MachiningTime,      updated.MachiningTime,      stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("InspectionTime",    existing.InspectionTime,     updated.InspectionTime,     stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("PreparationTime",   existing.PreparationTime,    updated.PreparationTime,    stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("TrialRunTime",      existing.TrialRunTime,       updated.TrialRunTime,       stepId, pid, userId, reason, now);

        if (changedCount == 0) return;

        existing.NC = updated.NC;
        existing.Drawing = updated.Drawing;
        existing.MachineRegistered = updated.MachineRegistered;        existing.FixtureType = updated.FixtureType;
        existing.ToolType = updated.ToolType;
        existing.TimingMachine = updated.TimingMachine;
        existing.IsBackup = updated.IsBackup;
        existing.ParentNC = updated.ParentNC;
        existing.SetupTime = updated.SetupTime;
        existing.MachiningTime = updated.MachiningTime;
        existing.InspectionTime = updated.InspectionTime;
        existing.PreparationTime = updated.PreparationTime;
        existing.TrialRunTime = updated.TrialRunTime;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// XÓA HẲN dòng công đoạn khỏi bảng chính. Snapshot toàn bộ field vào ChangeLog
    /// trước khi xóa để truy vết được. Không thể khôi phục — Restore đã bị bỏ.
    /// </summary>
    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException("Không tìm thấy dòng công đoạn");

        var snapshot = JsonSerializer.Serialize(new
        {
            existing.StepOrder, existing.NC, existing.Drawing,
            existing.MachineRegistered,            existing.FixtureType,
            existing.ToolType, existing.TimingMachine, existing.IsBackup, existing.ParentNC,
            existing.SetupTime, existing.MachiningTime, existing.InspectionTime,
            existing.PreparationTime, existing.TrialRunTime,
            existing.CreatedBy, existing.CreatedAt, existing.UpdatedBy, existing.UpdatedAt
        });
        var partId = existing.PartId;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, partId, ProcessStepAction.Deleted,
            oldValue: snapshot, newValue: null, userId, reason));

        _db.PartMachiningSteps.Remove(existing);
        await _db.SaveChangesAsync();
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new ArgumentException("Lý do phải có ít nhất 3 ký tự", nameof(reason));
    }

    private async Task EnsurePermissionAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || !user.IsActive)
            throw new UnauthorizedAccessException("User không tồn tại hoặc đã bị khóa");

        if (!PartMasterPermissionHelper.CanEditArea(user.Group, Area))
        {
            throw new UnauthorizedAccessException(
                $"Bạn không có quyền sửa vùng {PartMasterPermissionHelper.GetAreaName(Area)}. " +
                $"Vui lòng liên hệ Admin để được cấp quyền trên Ma trận RBAC.");
        }
    }

    private int LogStringDiff(string field, string? oldVal, string? newVal,
        long stepId, int partId, int userId, string reason, DateTime now)
    {
        if (string.Equals(oldVal ?? "", newVal ?? "", StringComparison.Ordinal)) return 0;
        _db.PartProcessStepChangeLogs.Add(new PartProcessStepChangeLog
        {
            StepTable = TableTag,
            StepId = stepId,
            PartId = partId,
            FieldName = field,
            OldValue = oldVal,
            NewValue = newVal,
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason
        });
        return 1;
    }

    private int LogDecimalDiff(string field, decimal? oldVal, decimal? newVal,
        long stepId, int partId, int userId, string reason, DateTime now)
    {
        if (oldVal == newVal) return 0;
        _db.PartProcessStepChangeLogs.Add(new PartProcessStepChangeLog
        {
            StepTable = TableTag,
            StepId = stepId,
            PartId = partId,
            FieldName = field,
            OldValue = oldVal?.ToString(CultureInfo.InvariantCulture),
            NewValue = newVal?.ToString(CultureInfo.InvariantCulture),
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason
        });
        return 1;
    }

    private static PartProcessStepChangeLog BuildLog(long stepId, int partId, string fieldName,
        string? oldValue, string? newValue, int userId, string reason)
    {
        return new PartProcessStepChangeLog
        {
            StepTable = TableTag,
            StepId = stepId,
            PartId = partId,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = userId,
            ChangedAt = DateTime.Now,
            Reason = reason
        };
    }
}
