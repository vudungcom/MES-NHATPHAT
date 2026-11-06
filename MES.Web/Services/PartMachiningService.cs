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
    private const string Label = "Gia công";   // [PM-NC-DUP-SVC] chữ trong lời báo mã trùng / trống (như trang)

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

        PartStepGuard.CheckReservedCode(Label, newStep.NC);   // [PM-NC-AT 2026-11-05] mã đầu "@" dành cho hệ thống (mọi dòng, cả dự phòng)
        // [PM-NC-DUP-SVC 2026-11-02] Thêm dòng = giao dịch + khoá MES_PM_STEPS_<PartId>; NC chính: không trống / '-', không trùng NC chính khác (đọc DB dưới khoá)
        return await PartStepGuard.RunLockedAsync(_db, partId, async () =>
        {
        if (!newStep.IsBackup)
            PartStepGuard.CheckMainCode(Label, newStep.NC, await _db.PartMachiningSteps
                .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup).Select(s => s.NC).ToListAsync());
        // [B6-DP-MA 2026-11-06] luật mã dòng dự phòng + mã chính không trùng mã dự phòng của dòng cha khác (đọc DB dưới khoá)
        PartStepGuard.CheckBackupRules(Label, newStep.NC, newStep.IsBackup, newStep.ParentNC, (await _db.PartMachiningSteps.AsNoTracking()
            .Where(s => s.PartId == partId && s.IsActive).Select(s => new { s.NC, s.IsBackup, s.ParentNC }).ToListAsync()).Select(s => (s.NC, s.IsBackup, s.ParentNC)));

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
        });   // [PM-NC-DUP-SVC]
    }

    public async Task UpdateAsync(long stepId, PartMachiningStep updated, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [PM-NC-DUP-SVC 2026-11-02] Sửa = giao dịch + khoá MES_PM_STEPS_<PartId> (Part của dòng không đổi khi sửa → đọc trước khoá được);
        //   đổi NC / dự phòng thành NC chính thì kiểm như lúc thêm, đọc DB dưới khoá
        var lockPid = await _db.PartMachiningSteps.Where(s => s.StepId == stepId).Select(s => (int?)s.PartId).FirstOrDefaultAsync();
        if (lockPid == null)
            throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        await PartStepGuard.RunLockedAsync(_db, lockPid.Value, async () =>
        {
        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException($"Không tìm thấy dòng công đoạn StepId={stepId}");
        // [PM-NC-DUP-THAP 2026-11-03] V1: đọc lại dòng từ DB dưới khoá — DbContext dùng chung cả phiên trang, FirstOrDefaultAsync trả bản EF đang giữ
        //   (giá trị cũ nếu người khác vừa đổi tên) → dò dự phòng theo mã cũ, dự phòng mồ côi. Nguồn: CLAUDE (rà độc lập 03/11).
        await _db.Entry(existing).ReloadAsync();
        if (!existing.IsActive)
            throw new InvalidOperationException("Không thể sửa dòng đã xóa.");
        // [PM-NC-DUP-SVC] mã / dự phòng đọc lại từ DB dưới khoá (existing có thể là bản EF trang đang giữ từ lần lưu trước — rà độc lập 02/11 T1)
        var dbNow = await _db.PartMachiningSteps.AsNoTracking().Where(s => s.StepId == stepId).Select(s => new { s.NC, s.IsBackup, s.ParentNC }).FirstAsync();   // [B6-DP-MA] + ParentNC
        if (!PartStepGuard.SameCode(dbNow.NC, updated.NC)) PartStepGuard.CheckReservedCode(Label, updated.NC);   // [PM-NC-AT 2026-11-05] chỉ kiểm khi đổi mã
        if (PartStepGuard.NeedCheckOnUpdate(dbNow.NC, dbNow.IsBackup, updated.NC, updated.IsBackup))
            PartStepGuard.CheckMainCode(Label, updated.NC, await _db.PartMachiningSteps
                .Where(s => s.PartId == existing.PartId && s.IsActive && !s.IsBackup && s.StepId != stepId).Select(s => s.NC).ToListAsync());
        // [PM-NC-DUP-THAP 2026-11-03] T3: dòng chính đổi thành dự phòng mà còn dự phòng con (ParentNC = mã này) → con mồ côi, sổ không gộp WTS về đâu (N2) → chặn.
        //   Nguồn: CLAUDE (rà độc lập 03/11; trang chưa có nút đổi, chặn ở máy chủ cho chắc).
        if (!dbNow.IsBackup && updated.IsBackup && await _db.PartMachiningSteps.AsNoTracking()
                .AnyAsync(s => s.PartId == existing.PartId && s.IsActive && s.IsBackup && s.StepId != stepId && s.ParentNC == dbNow.NC))
            throw new PartStepCodeException($"{Label}: NC / mã CV {PartStepGuard.Norm(dbNow.NC)} đang có dòng dự phòng — xoá dòng dự phòng trước khi đổi dòng này thành dự phòng.");
        // [B6-DP-MA 2026-11-06] luật mã dòng dự phòng — chỉ khi mã / dự phòng / mã cha đổi (đọc DB dưới khoá)
        if (PartStepGuard.NeedBackupCheckOnUpdate(dbNow.NC, dbNow.IsBackup, dbNow.ParentNC, updated.NC, updated.IsBackup, updated.ParentNC))
            PartStepGuard.CheckBackupRules(Label, updated.NC, updated.IsBackup, updated.ParentNC, (await _db.PartMachiningSteps.AsNoTracking()
                .Where(s => s.PartId == existing.PartId && s.IsActive && s.StepId != stepId).Select(s => new { s.NC, s.IsBackup, s.ParentNC }).ToListAsync()).Select(s => (s.NC, s.IsBackup, s.ParentNC)));

        var pid = existing.PartId;
        var changedCount = 0;
        var now = DateTime.Now;
        var oldNc = existing.NC;   // [PM-PARENT 2026-10-29] tên NC trước khi sửa — để dò dòng dự phòng đang trỏ về nó

        changedCount += LogStringDiff("NC",                 existing.NC,                 updated.NC,                 stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("Drawing",            existing.Drawing,            updated.Drawing,            stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("MachineRegistered",  existing.MachineRegistered,  updated.MachineRegistered,  stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("FixtureType",        existing.FixtureType,        updated.FixtureType,        stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ToolType",           existing.ToolType,           updated.ToolType,           stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("TimingMachine",      existing.TimingMachine,      updated.TimingMachine,      stepId, pid, userId, reason, now);
        changedCount += LogStringDiff("ParentNC",           existing.ParentNC,           updated.ParentNC,           stepId, pid, userId, reason, now);
        // [PM-NC-DUP-THAP 2026-11-03] T2: Đổi dự phòng ↔ chính cũng là thay đổi: trước không đếm → chỉ đổi ô này thì không lưu, ChangeLog không ghi.
        //   Nguồn: CLAUDE (rà độc lập gói PM-NC-DUP-SVC 02/11, dòng PM-NC-DUP-THAP). Kiểm mã khi dự phòng thành chính đã có ở NeedCheckOnUpdate.
        changedCount += LogStringDiff("IsBackup", existing.IsBackup ? "1" : "0", updated.IsBackup ? "1" : "0", stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("SetupTime",         existing.SetupTime,          updated.SetupTime,          stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("MachiningTime",     existing.MachiningTime,      updated.MachiningTime,      stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("InspectionTime",    existing.InspectionTime,     updated.InspectionTime,     stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("PreparationTime",   existing.PreparationTime,    updated.PreparationTime,    stepId, pid, userId, reason, now);
        changedCount += LogDecimalDiff("TrialRunTime",      existing.TrialRunTime,       updated.TrialRunTime,       stepId, pid, userId, reason, now);

        if (changedCount == 0) return 0;

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

        // [PM-PARENT 2026-10-29] Đổi tên NC / mã chính → dòng dự phòng (IsBackup, ParentNC = tên cũ) phải trỏ theo tên mới.
        //   Trước: ParentNC con giữ tên cũ → PO tạo sau chép nguyên, sổ gộp dự phòng về mã không còn (N2 không gộp),
        //   WTS ghi NC dự phòng bị sổ bỏ qua → Chưa làm không giảm (bộ kiểm chuỗi C9). Snapshot PO cũ không đổi (đúng luật).
        if (!existing.IsBackup && !string.Equals(oldNc, updated.NC, StringComparison.Ordinal))
        {
            var children = await _db.PartMachiningSteps
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
    /// XÓA HẲN dòng công đoạn khỏi bảng chính. Snapshot toàn bộ field vào ChangeLog
    /// trước khi xóa để truy vết được. Không thể khôi phục — Restore đã bị bỏ.
    /// </summary>
    public async Task SoftDeleteAsync(long stepId, int userId, string reason)
    {
        ValidateReason(reason);
        await EnsurePermissionAsync(userId);

        // [PM-NC-DUP-THAP 2026-11-03] T5: Xoá cũng chạy dưới khoá MES_PM_STEPS_<PartId> (như Thêm / Sửa [PM-NC-DUP-SVC]).
        //   Trước: người A xoá dòng chính (kéo theo dự phòng ParentNC = mã cũ) đúng lúc người B đổi tên mã đó → dự phòng mồ côi.
        //   Nguồn: CLAUDE (rà độc lập 02/11, dòng PM-NC-DUP-THAP).
        var lockPidDel = await _db.PartMachiningSteps.Where(s => s.StepId == stepId).Select(s => (int?)s.PartId).FirstOrDefaultAsync();
        if (lockPidDel == null) throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        await PartStepGuard.RunLockedAsync(_db, lockPidDel.Value, async () =>
        {

        var existing = await _db.PartMachiningSteps.FirstOrDefaultAsync(s => s.StepId == stepId);
        if (existing == null)
            throw new InvalidOperationException("Không tìm thấy dòng công đoạn");
        // [PM-NC-DUP-THAP 2026-11-03] V1: đọc lại dòng từ DB dưới khoá — DbContext dùng chung cả phiên trang, FirstOrDefaultAsync trả bản EF đang giữ
        //   (giá trị cũ nếu người khác vừa đổi tên) → dò dự phòng theo mã cũ, dự phòng mồ côi. Nguồn: CLAUDE (rà độc lập 03/11).
        await _db.Entry(existing).ReloadAsync();

        // [PM-DEL-BACKUP 2026-10-29] Xoá NC chính = xoá luôn các dòng dự phòng của nó (IsBackup, ParentNC = NC này), mỗi dòng
        //   xoá đủ như một NC (máy đồng dạng + xác nhận + snapshot vào ChangeLog). Trước: dự phòng ở lại mồ côi → PO tạo sau
        //   không gộp WTS dự phòng về NC chính (N2), sổ bỏ qua dòng WTS đó.
        if (!existing.IsBackup)
        {
            var backups = await _db.PartMachiningSteps
                .Where(s => s.PartId == existing.PartId && s.IsActive && s.IsBackup && s.ParentNC == existing.NC)
                .ToListAsync();
            foreach (var b in backups)
                await DeleteStepCoreAsync(b, userId, reason + $" (xoá theo NC chính {existing.NC})");
        }
        await DeleteStepCoreAsync(existing, userId, reason);
        await _db.SaveChangesAsync();
        return 0;
        });   // [PM-NC-DUP-THAP] T5
    }

    /// <summary>[DEL-NC-FULL][PM-DEL-BACKUP] Thân xoá 1 dòng NC (không SaveChanges): Confirms → Timings → Step + snapshot đủ vào ChangeLog.</summary>
    private async Task DeleteStepCoreAsync(PartMachiningStep existing, int userId, string reason)
    {
        var stepId = existing.StepId;
        // [DEL-NC-FULL] Nghiệp vụ chốt 17/10/2026: xóa dòng chính = xóa luôn toàn bộ máy đồng dạng của NC.
        // DB: FK_PartMachiningTimings_Steps = CASCADE, nhưng FK_TimingConfirms_Step / FK_TimingConfirms_Timing = NO ACTION
        // → nếu NC đã có tích xác nhận, DELETE step bị SQL chặn (lỗi treo "Đang xử lý..." trước đây).
        // Nên phải xóa tường minh: Confirms → Timings → Step, và snapshot đủ cả 3 vào ChangeLog trước khi xóa.
        var timings = await _db.PartMachiningTimings
            .Where(t => t.StepId == stepId)
            .ToListAsync();
        var confirms = await _db.PartMachiningTimingConfirms
            .Where(c => c.StepId == stepId)
            .ToListAsync();

        var snapshot = JsonSerializer.Serialize(new
        {
            existing.StepOrder, existing.NC, existing.Drawing,
            existing.MachineRegistered,            existing.FixtureType,
            existing.ToolType, existing.TimingMachine, existing.IsBackup, existing.ParentNC,
            existing.SetupTime, existing.MachiningTime, existing.InspectionTime,
            existing.PreparationTime, existing.TrialRunTime,
            existing.CreatedBy, existing.CreatedAt, existing.UpdatedBy, existing.UpdatedAt,
            // [DEL-NC-FULL] Máy đồng dạng (cả dòng đã ẩn IsActive=0) + lịch sử tích xác nhận — để truy vết được sau khi xóa
            Timings = timings.Select(t => new
            {
                t.TimingId, t.SoMay, t.DisplayOrder, t.FixtureType, t.ToolType,
                t.SetupTime, t.MachiningTime, t.InspectionTime, t.PreparationTime, t.TrialRunTime,
                t.IsActive, t.CreatedBy, t.CreatedAt, t.UpdatedBy, t.UpdatedAt
            }),
            Confirms = confirms.Select(c => new
            {
                c.Id, c.TimingId, c.FieldName, c.IsConfirmed, c.ConfirmedBy, c.ConfirmedAt
            })
        });
        var partId = existing.PartId;
        var now = DateTime.Now;

        _db.PartProcessStepChangeLogs.Add(BuildLog(
            stepId, partId, ProcessStepAction.Deleted,
            oldValue: snapshot, newValue: null, userId, reason));

        // [DEL-NC-FULL] Log riêng cho từng máy đồng dạng đang hoạt động → nút lịch sử theo máy vẫn thấy dòng "Xóa"
        foreach (var t in timings.Where(t => t.IsActive))
        {
            _db.PartProcessStepChangeLogs.Add(new PartProcessStepChangeLog
            {
                StepTable = TableTag, StepId = stepId, TimingId = t.TimingId, PartId = partId,
                FieldName = ProcessStepAction.Deleted,
                OldValue = $"Xóa máy {t.SoMay} (theo NC chính) | GL={t.SetupTime} GC={t.MachiningTime} KT={t.InspectionTime} CB={t.PreparationTime} CT={t.TrialRunTime}",
                NewValue = null, ChangedBy = userId, ChangedAt = now, Reason = reason
            });
        }

        _db.PartMachiningTimingConfirms.RemoveRange(confirms);
        _db.PartMachiningTimings.RemoveRange(timings);
        _db.PartMachiningSteps.Remove(existing);
    }

    // FieldName constants cho timing confirm
    public static class TimingField
    {
        public const string Setup       = "SetupTime";
        public const string Machining   = "MachiningTime";
        public const string Inspection  = "InspectionTime";
        public const string Preparation = "PreparationTime";
        public const string TrialRun    = "TrialRunTime";
        public static readonly string[] All = { Setup, Machining, Inspection, Preparation, TrialRun };
    }

    /// <summary>
    /// Lấy trạng thái confirm mới nhất của từng field cho một danh sách stepIds.
    /// Trả về Dictionary[stepId -> Dictionary[fieldName -> (isConfirmed, byName, at)]].
    /// </summary>
    /// <summary>
    /// Lấy trạng thái confirm mới nhất per (StepId, TimingId?, FieldName).
    /// Key = (StepId, TimingId) — TimingId=null cho NC chính, có giá trị cho máy đồng dạng.
    /// </summary>
    public async Task<Dictionary<(long StepId, long? TimingId), Dictionary<string, (bool IsConfirmed, string? ByName, DateTime At)>>>
        GetTimingConfirmsAsync(IEnumerable<long> stepIds)
    {
        var ids = stepIds.ToList();
        if (!ids.Any())
            return new();

        var rows = await _db.PartMachiningTimingConfirms
            .Where(x => ids.Contains(x.StepId))
            .Include(x => x.ConfirmedByUser)
            .AsNoTracking()
            .ToListAsync();

        // Group theo (StepId, TimingId), lấy bản ghi mới nhất per FieldName
        var result = new Dictionary<(long, long?), Dictionary<string, (bool, string?, DateTime)>>();
        foreach (var g in rows.GroupBy(x => (x.StepId, x.TimingId)))
        {
            var byField = new Dictionary<string, (bool, string?, DateTime)>();
            foreach (var fg in g.GroupBy(x => x.FieldName))
            {
                var latest = fg.OrderByDescending(x => x.ConfirmedAt).First();
                if (latest.IsConfirmed)
                    byField[fg.Key] = (true, latest.ConfirmedByUser?.FullName, latest.ConfirmedAt);
            }
            result[g.Key] = byField;
        }
        return result;
    }

    /// <summary>
    /// Toggle xác nhận 1 field. Append-only — không UPDATE, không DELETE.
    /// timingId = null → NC chính; timingId != null → máy đồng dạng.
    /// Trả về trạng thái IsConfirmed mới sau toggle.
    /// </summary>
    public async Task<bool> ToggleTimingConfirmAsync(long stepId, long? timingId, string fieldName, int userId)
    {
        if (!TimingField.All.Contains(fieldName))
            throw new ArgumentException($"FieldName không hợp lệ: {fieldName}");

        await EnsurePermissionAsync(userId);

        var step = await _db.PartMachiningSteps.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StepId == stepId);
        if (step == null || !step.IsActive)
            throw new InvalidOperationException("Không tìm thấy NC hoặc NC đã bị xóa.");

        // Lấy trạng thái hiện tại — filter theo cả timingId
        var latest = await _db.PartMachiningTimingConfirms
            .Where(x => x.StepId == stepId && x.TimingId == timingId && x.FieldName == fieldName)
            .OrderByDescending(x => x.ConfirmedAt)
            .FirstOrDefaultAsync();

        var newState = !(latest?.IsConfirmed ?? false);

        _db.PartMachiningTimingConfirms.Add(new PartMachiningTimingConfirm
        {
            StepId      = stepId,
            TimingId    = timingId,
            PartId      = step.PartId,
            FieldName   = fieldName,
            IsConfirmed = newState,
            ConfirmedBy = userId,
            ConfirmedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return newState;
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
