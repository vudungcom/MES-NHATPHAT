using System.Globalization;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service quản lý bảng công đoạn "Quy trình gia công" (PartMachiningSteps).
/// Vùng B — nhóm TECHNICAL Leader (hoặc ADMIN) mới được sửa/thêm/xóa.
/// Mọi thao tác ghi log vào PartProcessStepChangeLogs với Reason bắt buộc.
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

    // ================================================================
    // READ
    // ================================================================

    /// <summary>Lấy tất cả dòng đang active (IsActive=1) của 1 Part, sắp theo StepOrder.</summary>
    public async Task<List<PartMachiningStep>> GetActiveByPartAsync(int partId)
    {
        return await _db.PartMachiningSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>Lấy tất cả dòng của 1 Part (cả đã xóa), active lên trên.</summary>
    public async Task<List<PartMachiningStep>> GetAllByPartAsync(int partId)
    {
        return await _db.PartMachiningSteps
            .Where(s => s.PartId == partId)
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
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

    /// <summary>Lấy lịch sử thay đổi của 1 dòng công đoạn.</summary>
    public async Task<List<PartProcessStepChangeLog>> GetHistoryAsync(long stepId)
    {
        return await _db.PartProcessStepChangeLogs
            .Where(l => l.StepTable == TableTag && l.StepId == stepId)
            .OrderByDescending(l => l.ChangedAt)
            .Include(l => l.ChangedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    // ================================================================
    // WRITE — enforce permission + ghi log
    // ================================================================

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

        // Ghi log snapshot dữ liệu
        var snapshot = JsonSerializer.Serialize(new
        {
            newStep.StepOrder, newStep.NC, newStep.Drawing,
            newStep.MachineRegistered, newStep.MachineAlternative, newStep.FixtureType,
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
            throw new InvalidOperationException("Không thể sửa dòng đã xóa. Khôi phục trước rồi mới sửa.");

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;

        changedCount += LogStringDiff("NC",                 existing.NC,                 updated.NC,                 stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("Drawing",            existing.Drawing,            updated.Drawing,            stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("MachineRegistered",  existing.MachineRegistered,  updated.MachineRegistered,  stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("MachineAlternative", existing.MachineAlternative, updated.MachineAlternative, stepId, pid, userId, reason, now);
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
        existing.MachineRegistered = updated.MachineRegistered;
        existing.MachineAlternative = updated.MachineAlternative;
        existing.FixtureType = updated.FixtureType;
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

    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        if (!existing.IsActive)
            throw new InvalidOperationException("Dòng này đã bị xóa trước đó");

        existing.IsActive = false;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = DateTime.Now;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, existing.PartId, ProcessStepAction.Deleted,
            oldValue: null, newValue: null, userId, reason));

        await _db.SaveChangesAsync();
    }

    public async Task RestoreAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        if (existing.IsActive)
            throw new InvalidOperationException("Dòng đang active, không cần khôi phục");

        existing.IsActive = true;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = DateTime.Now;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, existing.PartId, ProcessStepAction.Restored,
            oldValue: null, newValue: null, userId, reason));

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

        if (user == null)
            throw new UnauthorizedAccessException("User không tồn tại");
        if (!user.IsActive)
            throw new UnauthorizedAccessException("User đã bị khóa");

        var groupCode = user.Group?.GroupCode;
        if (!PartMasterPermissionHelper.CanEditArea(groupCode, null, Area))
            throw new UnauthorizedAccessException(
                $"Bạn không có quyền sửa vùng {PartMasterPermissionHelper.GetAreaName(Area)}. " +
                $"Chỉ ADMIN hoặc nhóm Kỹ thuật (PART_GC) mới được sửa.");
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