using System.Globalization;
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class PartPackagingService
{
    private readonly AppDbContext _db;
    private const string TableTag = "Packaging";
    private const string Label = "Đóng gói";   // [PM-NC-DUP-SVC] chữ trong lời báo mã trùng / trống (như trang)
    private const PartMasterArea Area = PartMasterArea.E_DongGoi;

    public PartPackagingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<PartPackagingStep>> GetActiveByPartAsync(int partId)
    {
        return await _db.PartPackagingSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<PartPackagingStep>> GetAllByPartAsync(int partId)
    {
        return await _db.PartPackagingSteps
            .Where(s => s.PartId == partId)
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.StepOrder)
            .ThenBy(s => s.NC)
            .Include(s => s.CreatedByUser)
            .Include(s => s.UpdatedByUser)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<long> AddAsync(int partId, PartPackagingStep newStep, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [PM-NC-DUP-SVC 2026-11-02] Thêm dòng = giao dịch + khoá MES_PM_STEPS_<PartId>; dòng chính: mã không trống / '-', không trùng dòng chính khác (đọc DB dưới khoá)
        return await PartStepGuard.RunLockedAsync(_db, partId, async () =>
        {
        if (!newStep.IsBackup)
            PartStepGuard.CheckMainCode(Label, newStep.NC, await _db.PartPackagingSteps
                .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup).Select(s => s.NC).ToListAsync());

        var maxOrder = await _db.PartPackagingSteps
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

        _db.PartPackagingSteps.Add(newStep);
        await _db.SaveChangesAsync();

        var snapshot = JsonSerializer.Serialize(new
        {
            newStep.StepOrder, newStep.NC, newStep.StepName, newStep.StandardTime, newStep.IsBackup, newStep.ParentNC
        });
        _db.PartProcessStepChangeLogs.Add(BuildLog(
            newStep.StepId, partId, ProcessStepAction.Created, null, snapshot, userId, reason));
        await _db.SaveChangesAsync();

        return newStep.StepId;
        });   // [PM-NC-DUP-SVC]
    }

    public async Task UpdateAsync(long stepId, PartPackagingStep updated, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [PM-NC-DUP-SVC 2026-11-02] Sửa = giao dịch + khoá MES_PM_STEPS_<PartId> (Part của dòng không đổi khi sửa → đọc trước khoá được);
        //   đổi mã / dự phòng thành dòng chính thì kiểm mã như lúc thêm, đọc DB dưới khoá
        var lockPid = await _db.PartPackagingSteps.Where(s => s.StepId == stepId).Select(s => (int?)s.PartId).FirstOrDefaultAsync();
        if (lockPid == null) throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        await PartStepGuard.RunLockedAsync(_db, lockPid.Value, async () =>
        {
        var existing = await _db.PartPackagingSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        // [PM-NC-DUP-THAP 2026-11-03] V1: đọc lại dòng từ DB dưới khoá — DbContext dùng chung cả phiên trang, FirstOrDefaultAsync trả bản EF đang giữ
        //   (giá trị cũ nếu người khác vừa đổi tên) → dò dự phòng theo mã cũ, dự phòng mồ côi. Nguồn: CLAUDE (rà độc lập 03/11).
        await _db.Entry(existing).ReloadAsync();
        if (!existing.IsActive) throw new InvalidOperationException("Không thể sửa dòng đã xóa.");
        // [PM-NC-DUP-SVC] mã / dự phòng đọc lại từ DB dưới khoá (existing có thể là bản EF trang đang giữ từ lần lưu trước — rà độc lập 02/11 T1)
        var dbNow = await _db.PartPackagingSteps.AsNoTracking().Where(s => s.StepId == stepId).Select(s => new { s.NC, s.IsBackup }).FirstAsync();
        if (PartStepGuard.NeedCheckOnUpdate(dbNow.NC, dbNow.IsBackup, updated.NC, updated.IsBackup))
            PartStepGuard.CheckMainCode(Label, updated.NC, await _db.PartPackagingSteps
                .Where(s => s.PartId == existing.PartId && s.IsActive && !s.IsBackup && s.StepId != stepId).Select(s => s.NC).ToListAsync());
        // [PM-NC-DUP-THAP 2026-11-03] T3: dòng chính đổi thành dự phòng mà còn dự phòng con (ParentNC = mã này) → con mồ côi, sổ không gộp WTS về đâu (N2) → chặn.
        //   Nguồn: CLAUDE (rà độc lập 03/11; trang chưa có nút đổi, chặn ở máy chủ cho chắc).
        if (!dbNow.IsBackup && updated.IsBackup && await _db.PartPackagingSteps.AsNoTracking()
                .AnyAsync(s => s.PartId == existing.PartId && s.IsActive && s.IsBackup && s.StepId != stepId && s.ParentNC == dbNow.NC))
            throw new PartStepCodeException($"{Label}: NC / mã CV {PartStepGuard.Norm(dbNow.NC)} đang có dòng dự phòng — xoá dòng dự phòng trước khi đổi dòng này thành dự phòng.");

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;
        var oldNc = existing.NC;   // [PM-PARENT 2026-10-29] tên NC trước khi sửa — để dò dòng dự phòng đang trỏ về nó

        changedCount += LogStringDiff("NC",           existing.NC,           updated.NC,           stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("StepName",     existing.StepName,     updated.StepName,     stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ParentNC",     existing.ParentNC,     updated.ParentNC,     stepId, pid, userId, reason, now);
        // [PM-NC-DUP-THAP 2026-11-03] T2: Đổi dự phòng ↔ chính cũng là thay đổi: trước không đếm → chỉ đổi ô này thì không lưu, ChangeLog không ghi.
        //   Nguồn: CLAUDE (rà độc lập gói PM-NC-DUP-SVC 02/11, dòng PM-NC-DUP-THAP). Kiểm mã khi dự phòng thành chính đã có ở NeedCheckOnUpdate.
        changedCount += LogStringDiff("IsBackup", existing.IsBackup ? "1" : "0", updated.IsBackup ? "1" : "0", stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("StandardTime", existing.StandardTime, updated.StandardTime, stepId, pid, userId, reason, now);

        if (changedCount == 0) return 0;

        existing.NC = updated.NC;
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
            var children = await _db.PartPackagingSteps
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
        });   // [PM-NC-DUP-SVC]
    }

    /// <summary>
    /// XÓA HẲN dòng khỏi bảng chính. Snapshot toàn bộ field vào ChangeLog trước khi xóa
    /// để truy vết được lịch sử. Không thể khôi phục.
    /// </summary>
    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [PM-NC-DUP-THAP 2026-11-03] T5: Xoá cũng chạy dưới khoá MES_PM_STEPS_<PartId> (như Thêm / Sửa [PM-NC-DUP-SVC]).
        //   Trước: người A xoá dòng chính (kéo theo dự phòng ParentNC = mã cũ) đúng lúc người B đổi tên mã đó → dự phòng mồ côi.
        //   Nguồn: CLAUDE (rà độc lập 02/11, dòng PM-NC-DUP-THAP).
        var lockPidDel = await _db.PartPackagingSteps.Where(s => s.StepId == stepId).Select(s => (int?)s.PartId).FirstOrDefaultAsync();
        if (lockPidDel == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        await PartStepGuard.RunLockedAsync(_db, lockPidDel.Value, async () =>
        {

        var existing = await _db.PartPackagingSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        // [PM-NC-DUP-THAP 2026-11-03] V1: đọc lại dòng từ DB dưới khoá — DbContext dùng chung cả phiên trang, FirstOrDefaultAsync trả bản EF đang giữ
        //   (giá trị cũ nếu người khác vừa đổi tên) → dò dự phòng theo mã cũ, dự phòng mồ côi. Nguồn: CLAUDE (rà độc lập 03/11).
        await _db.Entry(existing).ReloadAsync();

        var snapshot = JsonSerializer.Serialize(new
        {
            existing.StepOrder, existing.NC, existing.StepName, existing.StandardTime,
            existing.IsBackup, existing.ParentNC,
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
            var backups = await _db.PartPackagingSteps
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
                _db.PartPackagingSteps.Remove(b);
            }
        }

        _db.PartPackagingSteps.Remove(existing);
        await _db.SaveChangesAsync();
        return 0;
        });   // [PM-NC-DUP-THAP] T5
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
            throw new UnauthorizedAccessException($"Bạn không có quyền sửa vùng {PartMasterPermissionHelper.GetAreaName(Area)} .");
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
