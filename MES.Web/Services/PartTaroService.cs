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
        PartStepGuard.CheckReservedCode("Taro", newStep.NC, newStep.WtsTaskCode);   // [PM-NC-AT 2026-11-05] NC / mã CV đầu "@" dành cho hệ thống (mọi dòng, cả dự phòng)
        // [TARO-NC-MA 2026-11-06] Thêm dòng = giao dịch + khoá MES_PM_STEPS_<PartId> (như PM-NC-DUP-SVC các nhóm khác); dòng chính: cặp (mã CV, NC)
        //   không trùng dòng chính khác (đọc DB dưới khoá) — cùng mã ở NC khác được (khách chốt B 02/11)
        return await PartStepGuard.RunLockedAsync(_db, partId, async () =>
        {
        if (!newStep.IsBackup)
            PartStepGuard.CheckTaroMain("Taro", newStep.WtsTaskCode, newStep.NC, (await _db.PartTaroSteps.AsNoTracking()
                .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup).Select(s => new { s.WtsTaskCode, s.NC }).ToListAsync())
                .Select(s => (s.WtsTaskCode, (string?)s.NC)));

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
            newStep.StepOrder, newStep.NC, newStep.WtsTaskCode, newStep.StepName,
            newStep.StandardTime, newStep.IsBackup, newStep.ParentNC
        });
        _db.PartProcessStepChangeLogs.Add(BuildLog(
            newStep.StepId, partId, ProcessStepAction.Created, null, snapshot, userId, reason));
        await _db.SaveChangesAsync();

        return newStep.StepId;
        });   // [TARO-NC-MA]
    }

    public async Task UpdateAsync(long stepId, PartTaroStep updated, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [TARO-NC-MA 2026-11-06] Sửa = giao dịch + khoá MES_PM_STEPS_<PartId> (như các nhóm khác); đổi mã CV / NC / dự phòng thành dòng chính
        //   thì kiểm cặp (mã CV, NC) không trùng dòng chính khác — đọc DB dưới khoá
        var lockPid = await _db.PartTaroSteps.Where(s => s.StepId == stepId).Select(s => (int?)s.PartId).FirstOrDefaultAsync();
        if (lockPid == null) throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        await PartStepGuard.RunLockedAsync(_db, lockPid.Value, async () =>
        {
        var existing = await _db.PartTaroSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        await _db.Entry(existing).ReloadAsync();   // [TARO-NC-MA] đọc lại dưới khoá (DbContext dùng chung cả phiên trang — như PM-NC-DUP-THAP V1)
        if (!existing.IsActive) throw new InvalidOperationException("Không thể sửa dòng đã xóa.");
        // [PM-NC-AT 2026-11-05] chỉ kiểm mã đổi (đọc DB, không dùng bản EF trang đang giữ) — dòng cũ có sẵn "@" vẫn sửa được ô khác
        var dbCode = await _db.PartTaroSteps.AsNoTracking().Where(s => s.StepId == stepId).Select(s => new { s.NC, s.WtsTaskCode, s.IsBackup }).FirstAsync();
        if (!PartStepGuard.SameCode(dbCode.NC, updated.NC)) PartStepGuard.CheckReservedCode("Taro", updated.NC);
        if (!PartStepGuard.SameCode(dbCode.WtsTaskCode, updated.WtsTaskCode)) PartStepGuard.CheckReservedCode("Taro", updated.WtsTaskCode);
        if (!updated.IsBackup && (dbCode.IsBackup || !PartStepGuard.SameCode(dbCode.NC, updated.NC)
                || !PartStepGuard.SameCode(PartStepGuard.TaroCode(dbCode.WtsTaskCode, dbCode.NC), PartStepGuard.TaroCode(updated.WtsTaskCode, updated.NC))))
            PartStepGuard.CheckTaroMain("Taro", updated.WtsTaskCode, updated.NC, (await _db.PartTaroSteps.AsNoTracking()
                .Where(s => s.PartId == existing.PartId && s.IsActive && !s.IsBackup && s.StepId != stepId).Select(s => new { s.WtsTaskCode, s.NC }).ToListAsync())
                .Select(s => (s.WtsTaskCode, (string?)s.NC)));

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;
        var oldNc = existing.NC;   // [PM-PARENT 2026-10-29] tên NC trước khi sửa — để dò dòng dự phòng đang trỏ về nó

        changedCount += LogStringDiff("NC",           existing.NC,           updated.NC,           stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("WtsTaskCode",  existing.WtsTaskCode,  updated.WtsTaskCode,  stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("StepName",     existing.StepName,     updated.StepName,     stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ParentNC",     existing.ParentNC,     updated.ParentNC,     stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("StandardTime", existing.StandardTime, updated.StandardTime, stepId, pid, userId, reason, now);

        if (changedCount == 0) return 0;   // [TARO-NC-MA] trong khoá (lambda trả số như các nhóm khác)

        existing.NC = updated.NC;
        existing.WtsTaskCode = updated.WtsTaskCode;
        existing.StepName = updated.StepName;
        existing.StandardTime = updated.StandardTime;
        existing.IsBackup = updated.IsBackup;
        existing.ParentNC = updated.ParentNC;
        existing.UpdatedBy = userId;
        existing.UpdatedAt = now;

        // [PM-PARENT 2026-10-29] Đổi tên NC / mã chính → dòng dự phòng (IsBackup, ParentNC = tên cũ) phải trỏ theo tên mới.
        //   Trước: ParentNC con giữ tên cũ → PO tạo sau chép nguyên, sổ gộp dự phòng về mã không còn (N2 không gộp),
        //   WTS ghi NC dự phòng bị sổ bỏ qua → Chưa làm không giảm (bộ kiểm chuỗi C9). Snapshot PO cũ không đổi (đúng luật).
        if (!existing.IsBackup && !string.Equals(oldNc, updated.NC, StringComparison.Ordinal))
        {
            var children = await _db.PartTaroSteps
                .Where(s => s.PartId == pid && s.IsActive && s.IsBackup && s.ParentNC == oldNc)
                .ToListAsync();
            foreach (var child in children)
            {
                LogStringDiff("ParentNC", child.ParentNC, updated.NC, child.StepId, pid, userId, reason + " (theo NC chính đổi tên)", now);
                child.ParentNC = updated.NC;
                child.UpdatedBy = userId;
                child.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();
        return 0;
        });   // [TARO-NC-MA]
    }

    /// <summary>
    /// XÓA HẲN dòng công đoạn khỏi bảng chính. Trước khi xóa, INSERT snapshot JSON
    /// của toàn bộ field vào PartProcessStepChangeLogs để truy vết được. Append-only ChangeLog.
    /// Không thể khôi phục — Restore đã bị bỏ.
    /// </summary>
    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        var existing = await _db.PartTaroSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");

        var snapshot = JsonSerializer.Serialize(new
        {
            existing.StepOrder, existing.NC, existing.WtsTaskCode, existing.StepName,
            existing.StandardTime, existing.IsBackup, existing.ParentNC,
            existing.CreatedBy, existing.CreatedAt, existing.UpdatedBy, existing.UpdatedAt
        });
        var partId = existing.PartId;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, partId, ProcessStepAction.Deleted,
            oldValue: snapshot, newValue: null, userId, reason));

        // [PM-DEL-BACKUP 2026-10-29] Xoá dòng chính = xoá luôn các dòng dự phòng của nó (IsBackup, ParentNC = NC này).
        //   Trước: dự phòng ở lại mồ côi → PO tạo sau không gộp WTS dự phòng về NC chính (N2), sổ bỏ qua dòng WTS đó.
        if (!existing.IsBackup)
        {
            var backups = await _db.PartTaroSteps
                .Where(s => s.PartId == partId && s.IsActive && s.IsBackup && s.ParentNC == existing.NC)
                .ToListAsync();
            foreach (var b in backups)
            {
                var bSnap = JsonSerializer.Serialize(new
                {
                    b.StepOrder, b.NC, b.StepName, b.StandardTime, b.IsBackup, b.ParentNC,
                    b.CreatedBy, b.CreatedAt, b.UpdatedBy, b.UpdatedAt
                });
                _db.PartProcessStepChangeLogs.Add(BuildLog(
                    b.StepId, partId, ProcessStepAction.Deleted,
                    oldValue: bSnap, newValue: null, userId, reason + $" (xoá theo NC chính {existing.NC})"));
                _db.PartTaroSteps.Remove(b);
            }
        }

        _db.PartTaroSteps.Remove(existing);
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
