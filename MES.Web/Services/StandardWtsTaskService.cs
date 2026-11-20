using ClosedXML.Excel;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class StandardWtsTaskService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public StandardWtsTaskService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<StandardWtsTask>> GetAllActiveAsync()
    {
        return await _db.StandardWtsTasks
            .Where(t => t.IsActive)
            .OrderBy(t => t.CategoryCode)
            .ThenBy(t => t.DisplayOrder)
            .ThenBy(t => t.TaskId)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy danh sách các nhóm công đoạn động đang có trong CSDL
    /// </summary>
    public async Task<List<(string Code, string Name)>> GetDistinctCategoriesAsync()
    {
        var list = await _db.StandardWtsTasks
            .Where(t => t.IsActive)
            .Select(t => new { t.CategoryCode, t.CategoryName })
            .ToListAsync();   // không Distinct: đếm tần suất THẬT của tên (rà độc lập 16/11 — Distinct làm "tên gặp nhiều nhất" thành tên đầu ABC, lệch GroupNameMismatchAsync)

        // [WTS-NHOM-TEN 2026-11-16] (BP2-02) 1 mã nhóm = 1 dòng (lấy tên gặp nhiều nhất) — trước: distinct theo cặp (mã, tên) → dữ liệu cũ 1 mã 2 tên ra 2 dòng
        //   → tab Cấu hình nhóm `@key` trùng → Blazor ném "same key value", tab chết; select nhóm 2 option cùng value. Nguồn: CLAUDE quét Fable 15/11 (B-P2 BP2-02 Trung).
        return list.GroupBy(x => (x.CategoryCode ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.GroupBy(x => (x.CategoryName ?? "").Trim()).OrderByDescending(n => n.Count()).ThenBy(n => n.Key).First().Key))
            .ToList();
    }

    public async Task<List<StandardWtsTask>> GetByCategoryAsync(string categoryCode)
    {
        return await _db.StandardWtsTasks
            .Where(t => t.IsActive && t.CategoryCode == categoryCode)
            .OrderBy(t => t.DisplayOrder)
            .ToListAsync();
    }

    private static string Cap500(string s) => s.Length <= 500 ? s : s.Substring(0, 500);   // [WTS-NHOM-TEN] cột Reason 500

    /// <summary>[WTS-NHOM-TEN 2026-11-16] Mã nhóm đã có công việc đang dùng mà tên gửi lên khác tên đang có → câu báo; null = khớp / nhóm mới.</summary>
    private async Task<string?> GroupNameMismatchAsync(string? categoryCode, string? categoryName, long? exceptTaskId = null)
    {
        var code = (categoryCode ?? "").Trim().ToUpper(); var name = (categoryName ?? "").Trim();
        if (code.Length == 0) return null;
        var names = await _db.StandardWtsTasks.Where(x => x.IsActive && x.CategoryCode == code && (exceptTaskId == null || x.TaskId != exceptTaskId))
            .Select(x => x.CategoryName).ToListAsync();
        var cur = names.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0).GroupBy(n => n).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
        if (cur == null || string.Equals(cur, name, StringComparison.Ordinal)) return null;
        return $"Mã nhóm '{code}' đã có tên '{cur}' — dùng đúng tên đó (bỏ tích \"Tạo nhóm công đoạn mới\" và chọn nhóm). Muốn đổi tên nhóm: Sửa 1 công việc trong nhóm, đổi Tên nhóm → đổi cho cả nhóm.";
    }

    public async Task<(bool Success, string? Error)> CreateAsync(StandardWtsTask model, int currentUserId, string pin)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa WTS_MASTER (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.WtsMaster)) return (false, PermCheck.NoEdit("WTS tiêu chuẩn"));
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN xác nhận không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá

        if (string.IsNullOrWhiteSpace(model.CategoryCode)) return (false, "Mã nhóm công đoạn không được để trống.");
        if (string.IsNullOrWhiteSpace(model.CategoryName)) return (false, "Tên nhóm công đoạn không được để trống.");
        if (string.IsNullOrWhiteSpace(model.TaskCode)) return (false, "Mã công việc không được để trống.");
        if (string.IsNullOrWhiteSpace(model.TaskName)) return (false, "Tên công việc không được để trống.");
        if (TaskLenError(model) is string eLen) return (false, eLen);   // [LEN-CHECK 2026-11-16] (BP2-01)

        model.CategoryCode = model.CategoryCode.Trim().ToUpper();
        model.CategoryName = model.CategoryName.Trim();
        model.TaskCode = model.TaskCode.Trim().ToUpper();
        model.TaskName = model.TaskName.Trim();
        model.DefaultUnit = string.IsNullOrWhiteSpace(model.DefaultUnit) ? "Chi tiết" : model.DefaultUnit.Trim();
        // IsProductiveTask giữ nguyên giá trị từ form (default true)

        bool exists = await _db.StandardWtsTasks.AnyAsync(x => x.TaskCode == model.TaskCode && x.IsActive);
        if (exists) return (false, $"Mã công việc '{model.TaskCode}' đã tồn tại trong danh mục.");
        // [WTS-NHOM-TEN 2026-11-16] (BP2-02) mã nhóm đã có thì tên nhóm phải đúng tên đang có (tên nhóm lưu theo từng công việc; lệch tên → 2 dòng cùng mã)
        if (await GroupNameMismatchAsync(model.CategoryCode, model.CategoryName) is string eNhom) return (false, eNhom);

        // [RS-E8 2026-10-29] Mã đã xoá (IsActive = 0) mà thêm lại cùng mã → KHÔI PHỤC dòng cũ thay vì thêm dòng mới.
        //   Chỉ mục TaskCode trong DB là unique KHÔNG lọc IsActive → trước đây thêm lại báo lỗi SQL trùng khoá.
        //   Giữ 1 mã = 1 dòng: Part Master / snapshot PO / WTS / báo cáo đều nối theo CHỮ mã, nên không tách 2 dòng cùng mã.
        var deleted = await _db.StandardWtsTasks.Where(x => x.TaskCode == model.TaskCode && !x.IsActive)
            .OrderByDescending(x => x.TaskId).FirstOrDefaultAsync();
        if (deleted != null)
        {
            var nowR = DateTime.Now;
            var oldText = $"[{deleted.TaskCode}] {deleted.TaskName} ({deleted.CategoryName}) | Có ích: {(deleted.IsProductiveTask ? "Có" : "Không")} | Đã xóa";
            deleted.CategoryCode = model.CategoryCode;
            deleted.CategoryName = model.CategoryName;
            deleted.TaskName = model.TaskName;
            deleted.DefaultUnit = model.DefaultUnit;
            deleted.StandardTimeSec = model.StandardTimeSec;
            deleted.DisplayOrder = model.DisplayOrder;
            deleted.GhiChu = model.GhiChu?.Trim();
            deleted.IsProductiveTask = model.IsProductiveTask;
            deleted.IsActive = true;
            deleted.UpdatedAt = nowR;
            deleted.UpdatedBy = currentUserId;
            _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
            {
                TaskId = deleted.TaskId,
                FieldName = "Khôi phục mã đã xóa",
                OldValue = oldText,
                NewValue = $"[{deleted.TaskCode}] {deleted.TaskName} ({deleted.CategoryName}) | Có ích: {(deleted.IsProductiveTask ? "Có" : "Không")}",
                Reason = "Thêm lại mã đã xóa → khôi phục dòng cũ (1 mã = 1 dòng)",
                ChangedAt = nowR,
                ChangedBy = currentUserId
            });
            // [WTS-TAO-1TX] rà độc lập G27N L7: nhánh khôi phục cũng gỡ bản EF khi lỗi (1 lần SaveChanges = 1 giao dịch)
            try { await _db.SaveChangesAsync(); }
            catch (Exception exR)
            {
                foreach (var en in _db.ChangeTracker.Entries<StandardWtsTask>().Where(x => x.State != EntityState.Unchanged || ReferenceEquals(x.Entity, deleted)).ToList()) en.State = EntityState.Detached;
                foreach (var en in _db.ChangeTracker.Entries<StandardWtsTaskChangeLog>().Where(x => x.State == EntityState.Added).ToList()) en.State = EntityState.Detached;
                return (false, UiSafe.Text(exR, null, "khôi phục công việc tiêu chuẩn"));
            }
            return (true, null);
        }

        var now = DateTime.Now;
        model.IsActive = true;
        model.CreatedAt = now;
        model.CreatedBy = currentUserId;

        // [WTS-TAO-1TX 2026-11-20] gói nhỏ G24-GHI-NHO: dòng công việc + dòng lịch sử "Tạo mới" trong 1 giao dịch (trước 2 lần SaveChanges rời —
        //   lỗi lần 2 thì có công việc mà không có lịch sử). Lỗi → quay lại + gỡ bản EF (DbContext dùng chung cả phiên trang). Nguồn: CLAUDE rà độc lập Gói 24; Hùng 20/11 18:12.
        await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
        _db.StandardWtsTasks.Add(model);
        // [WTS-LOG-ID 2026-11-16] lưu trước để có TaskId — trước: log "Tạo mới" ghi TaskId = 0 (mồ côi, lịch sử không có dòng Tạo) — cùng lỗi BM1-04 Đồ gá.
        //   Nguồn: CLAUDE tự phát hiện khi làm Gói 24 (16/11, lúc sửa WTS-NHOM-TEN); vá theo mục 0 CLAUDE-RULES (vênh truy vết, không đổi số).
        await _db.SaveChangesAsync();

        _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
        {
            TaskId = model.TaskId,
            FieldName = "Tạo mới công việc",
            OldValue = null,
            NewValue = $"[{model.TaskCode}] {model.TaskName} ({model.CategoryName}) | Có ích: {(model.IsProductiveTask ? "Có" : "Không")}",
            Reason = "Khởi tạo công việc tiêu chuẩn",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return (true, null);
        }
        catch (Exception ex)
        {
            if (tx != null) { try { await tx.RollbackAsync(); } catch { } }
            foreach (var en in _db.ChangeTracker.Entries<StandardWtsTask>().Where(x => x.State == EntityState.Added || ReferenceEquals(x.Entity, model)).ToList()) en.State = EntityState.Detached;
            foreach (var en in _db.ChangeTracker.Entries<StandardWtsTaskChangeLog>().Where(x => x.State == EntityState.Added).ToList()) en.State = EntityState.Detached;
            model.TaskId = 0;
            return (false, UiSafe.Text(ex, null, "thêm công việc tiêu chuẩn"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // [TARO-3T-GOP 2026-11-06] Mã GỘP: 1 mã làm THAY cho các mã thành phần (vd 3T thay 3T1 + 3T2 + 3T3) — bảng StandardWtsTaskGops (SQL 07).
    //   Luật (để sổ không lẫn): mã thành phần cùng nhóm với mã gộp, đang dùng, không phải mã chuẩn bị (1T…); không lồng (mã gộp không là
    //   thành phần của mã khác, mã thành phần không là mã gộp); 1 mã thành phần chỉ thuộc 1 mã gộp. Bỏ khai = IsActive 0 (giữ ai / lúc nào),
    //   có dòng lịch sử. PO đã tạo giữ khai báo lúc chụp quy trình; PO chưa chạy lấy bản mới khi làm mới quy trình.
    //   Nguồn: KHÁCH 02/11 09:14 (3T là cách làm gộp THAY 3T1–3T3); HÙNG 02/11 10:34 (khai rõ trong WTS tiêu chuẩn, không suy theo tên mã).
    // ─────────────────────────────────────────────────────────────────────
    public async Task<List<StandardWtsTaskGop>> GetActiveGopsAsync()
        => await _db.StandardWtsTaskGops.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.GopTaskCode).ThenBy(g => g.PartTaskCode).ToListAsync();

    static string GopText(IEnumerable<string> parts) => string.Join(" + ", parts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    /// <summary>[TARO-3T-GOP] Kiểm + ghi khai báo gộp của 1 mã (gọi trong UpdateAsync, chưa SaveChanges). Trả lời báo lỗi (null = qua).</summary>
    private async Task<string?> ApplyGopAsync(StandardWtsTask task, string newCode, string newCategory, List<string> wantParts, int userId, DateTime now,
                                               List<StandardWtsTaskChangeLog> logs, string? reason)
    {
        var names = await _db.StandardWtsTasks.AsNoTracking().Where(t => t.IsActive).Select(t => t.TaskCode).ToListAsync();
        var want = wantParts.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Select(x => (names.FirstOrDefault(n => string.Equals(n?.Trim(), x, StringComparison.OrdinalIgnoreCase)) ?? x).Trim())   // chữ đúng như danh mục
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var allGops = await _db.StandardWtsTaskGops.Where(g => g.IsActive).ToListAsync();
        var mine = allGops.Where(g => string.Equals(g.GopTaskCode, task.TaskCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var curParts = mine.Select(g => g.PartTaskCode.Trim()).ToList();
        bool same = curParts.Count == want.Count && curParts.All(c => want.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (same) return null;
        if (want.Count > 0)
        {
            if (HandoverLedgerService.IsPrepCode(newCode)) return $"Mã {newCode} là mã chuẩn bị (chỉ tính giờ) — không khai mã gộp được.";
            if (allGops.Any(g => string.Equals(g.PartTaskCode, task.TaskCode, StringComparison.OrdinalIgnoreCase)))
                return $"Mã {newCode} đang là mã thành phần của mã gộp {string.Join(", ", allGops.Where(g => string.Equals(g.PartTaskCode, task.TaskCode, StringComparison.OrdinalIgnoreCase)).Select(g => g.GopTaskCode).Distinct())} — không khai lồng nhau được.";
            var tasks = await _db.StandardWtsTasks.AsNoTracking().Where(t => t.IsActive).Select(t => new { t.TaskCode, t.CategoryCode }).ToListAsync();
            foreach (var pc in want)
            {
                var t = tasks.FirstOrDefault(x => string.Equals(x.TaskCode, pc, StringComparison.OrdinalIgnoreCase));
                if (t == null) return $"Mã thành phần {pc} không có trong danh mục WTS tiêu chuẩn (hoặc đã xoá).";
                if (string.Equals(t.TaskCode, newCode, StringComparison.OrdinalIgnoreCase) || string.Equals(t.TaskCode, task.TaskCode, StringComparison.OrdinalIgnoreCase))
                    return "Mã gộp không làm thay chính nó.";
                if (!string.Equals(t.CategoryCode, newCategory, StringComparison.OrdinalIgnoreCase)) return $"Mã thành phần {pc} khác nhóm với mã {newCode} — chỉ gộp các mã cùng nhóm.";
                if (HandoverLedgerService.IsPrepCode(pc)) return $"Mã {pc} là mã chuẩn bị (chỉ tính giờ) — không làm mã thành phần được.";
                if (allGops.Any(g => string.Equals(g.GopTaskCode, pc, StringComparison.OrdinalIgnoreCase)))
                    return $"Mã {pc} đang là mã gộp — không khai lồng nhau được.";
                var other = allGops.FirstOrDefault(g => string.Equals(g.PartTaskCode, pc, StringComparison.OrdinalIgnoreCase)
                                                     && !string.Equals(g.GopTaskCode, task.TaskCode, StringComparison.OrdinalIgnoreCase));
                if (other != null) return $"Mã {pc} đã là mã thành phần của mã gộp {other.GopTaskCode} — mỗi mã thành phần chỉ thuộc 1 mã gộp.";
            }
        }
        foreach (var g in mine.Where(g => !want.Contains(g.PartTaskCode.Trim(), StringComparer.OrdinalIgnoreCase)))
        { g.IsActive = false; g.RemovedBy = userId; g.RemovedAt = now; }
        foreach (var pc in want.Where(pc => !curParts.Contains(pc, StringComparer.OrdinalIgnoreCase)))
            _db.StandardWtsTaskGops.Add(new StandardWtsTaskGop
            {
                // [TARO-3T-GOP] rà độc lập 06/11 mục 3: ghi theo MÃ MỚI — đổi mã + khai gộp cùng 1 lần Lưu trước ghi mã cũ (task.TaskCode đổi SAU hàm này)
                //   → khai báo mồ côi, PO không chụp được. (Đổi mã khi ĐANG có khai báo gộp đã bị chặn ở đầu UpdateAsync nên "mine" theo mã cũ vẫn đúng.)
                GopTaskCode = newCode.Trim(),
                PartTaskCode = pc,
                IsActive = true, CreatedBy = userId, CreatedAt = now,
            });
        logs.Add(new StandardWtsTaskChangeLog
        {
            TaskId = task.TaskId, FieldName = "Mã gộp (làm thay)",
            OldValue = curParts.Count == 0 ? "" : GopText(curParts), NewValue = want.Count == 0 ? "" : GopText(want),
            Reason = reason, ChangedAt = now, ChangedBy = userId
        });
        return null;
    }

    /// <param name="gopParts">[TARO-3T-GOP] Mã thành phần mà mã này làm thay (null = không đổi khai báo gộp; rỗng = bỏ khai gộp).</param>
    /// <summary>[LEN-CHECK 2026-11-16] Độ dài ô chữ theo cột StandardWtsTasks (Entities/StandardWtsTask.cs) — trước: SqlException không bắt → trang chết.</summary>
    /// <summary>[WTS-MA-20 2026-11-20] gói nhỏ G23-GHI-NHO L10: mã công việc ≤ 20 ký tự — cột WorkerActivityLogs.WtsCode / PartTaroSteps.WtsTaskCode 20
    ///   (StandardWtsTasks.TaskCode 50): mã 21–50 trước lưu được ở danh mục rồi nổ ở khâu sau. Nguồn: CLAUDE rà độc lập Gói 23 (L10); Hùng 20/11 18:12.</summary>
    public const int TaskCodeMax = 20;
    public static string? TaskLenError(StandardWtsTask m)
        => LenCheck.FirstOver((m.CategoryCode, 50, "Mã nhóm công đoạn"), (m.CategoryName, 150, "Tên nhóm công đoạn"), (m.TaskCode, TaskCodeMax, "Mã công việc"),
                              (m.TaskName, 250, "Tên công việc"), (m.DefaultUnit, 50, "Đơn vị"), (m.GhiChu, 500, "Ghi chú"));

    public async Task<(bool Success, string? Error)> UpdateAsync(StandardWtsTask updatedModel, string? reason, int currentUserId, string pin, List<string>? gopParts = null)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa WTS_MASTER (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.WtsMaster)) return (false, PermCheck.NoEdit("WTS tiêu chuẩn"));
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN xác nhận không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá

        // [TARO-3T-GOP 2026-11-06] khoá chung MES_WTS_GOP từ TRƯỚC khi đọc mã / khai báo gộp (rà độc lập 06/11 vòng 1 mục 5 + vòng 2 mục 2):
        //   luật "1 mã thành phần thuộc 1 mã gộp" / "không lồng" / "mã đang khai gộp không đổi mã, đổi nhóm, xoá lặng" đọc rồi ghi —
        //   2 người cùng lưu (khai 3T1 cho 3T và 4T; đổi mã 3T1 đúng lúc người khác khai 3T ← 3T1) thì người sau chờ, đọc lại trong khoá.
        //   Nguồn: CLAUDE (rà độc lập gói 4 06/11).
        await using var gopTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        if (!await PoGuard.TryLockInTransactionAsync(_db, GopLock))
            return (false, "Đang có người khác lưu danh mục WTS tiêu chuẩn / khai báo mã gộp — chờ vài giây rồi bấm lưu lại.");
        if (TaskLenError(updatedModel) is string eLenU) return (false, eLenU);   // [LEN-CHECK 2026-11-16] (BP2-01)
        if (LenCheck.Over(reason, 500, "Lý do sửa") is string eRs) return (false, eRs);   // [LEN-CHECK] StandardWtsTaskChangeLogs.Reason 500
        var task = await _db.StandardWtsTasks.FirstOrDefaultAsync(x => x.TaskId == updatedModel.TaskId && x.IsActive);
        if (task == null) return (false, "Không tìm thấy công việc.");

        var newCode = updatedModel.TaskCode.Trim().ToUpper();
        bool duplicate = await _db.StandardWtsTasks.AnyAsync(x => x.TaskCode == newCode && x.TaskId != task.TaskId && x.IsActive);
        if (duplicate) return (false, $"Mã công việc '{newCode}' đã được sử dụng bởi một mục khác.");
        // [RS-E8 2026-10-29] Đổi MÃ: (1) mã mới trùng dòng đã xoá → SQL báo trùng khoá (unique không lọc IsActive);
        //   (2) mã cũ đã dùng ở Part Master / snapshot PO / WTS → đổi chữ mã làm các nơi đó treo mã cũ (mọi nơi nối theo chữ) → chặn.
        //   Đổi tên / nhóm / đơn vị / ghi chú vẫn được.
        if (string.IsNullOrWhiteSpace(newCode)) return (false, "Mã công việc không được để trống.");
        // Mã cũ lưu chữ thường / có khoảng trắng ("1t", "1T ") mà người dùng không đổi mã → không coi là đổi mã, giữ nguyên chữ đã lưu
        if (string.Equals(newCode, (task.TaskCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) newCode = task.TaskCode!;
        if (newCode != task.TaskCode)
        {
            bool dupDeleted = await _db.StandardWtsTasks.AnyAsync(x => x.TaskCode == newCode && x.TaskId != task.TaskId && !x.IsActive);
            if (dupDeleted) return (false, $"Mã công việc '{newCode}' thuộc một mục đã xóa — muốn dùng lại mã này thì bấm \"Thêm công việc mới\" với mã '{newCode}' (phần mềm khôi phục mục cũ).");
            var used = await UsedWhereAsync(task.TaskCode);
            if (used != null) return (false, $"Mã công việc '{task.TaskCode}' đã dùng ở {used} — không đổi mã được (đổi tên / nhóm / ghi chú thì được). Cần mã khác thì thêm công việc mới.");
            // [TARO-3T-GOP 2026-11-06] mã đang có khai báo gộp (là mã gộp hoặc thành phần) → bỏ khai gộp trước rồi mới đổi mã (khai báo nối theo chữ mã)
            if (await _db.StandardWtsTaskGops.AnyAsync(g => g.IsActive && (g.GopTaskCode == task.TaskCode || g.PartTaskCode == task.TaskCode)))
                return (false, $"Mã công việc '{task.TaskCode}' đang có khai báo mã gộp — bỏ khai gộp trước rồi mới đổi mã.");
        }
        // [TARO-3T-GOP] đổi nhóm khi mã đang có khai báo gộp → khai báo sai nhóm
        if (!string.Equals((updatedModel.CategoryCode ?? "").Trim(), (task.CategoryCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            && (gopParts == null || gopParts.Count > 0)
            && await _db.StandardWtsTaskGops.AnyAsync(g => g.IsActive && (g.GopTaskCode == task.TaskCode || g.PartTaskCode == task.TaskCode)))
            return (false, $"Mã công việc '{task.TaskCode}' đang có khai báo mã gộp — bỏ khai gộp trước rồi mới đổi nhóm.");

        // [WTS-NHOM-TEN 2026-11-16] (BP2-02) chuyển sang mã nhóm KHÁC đang có → tên nhóm phải đúng tên đang có; cùng mã mà đổi tên → đổi tên cho CẢ nhóm (dưới)
        var catNew = (updatedModel.CategoryCode ?? "").Trim().ToUpper(); var catNameNew = (updatedModel.CategoryName ?? "").Trim();
        bool sameGroup = string.Equals(catNew, (task.CategoryCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        if (!sameGroup && await GroupNameMismatchAsync(catNew, catNameNew, task.TaskId) is string eNhomU) return (false, eNhomU);
        List<StandardWtsTask> renameGroup = new();
        if (sameGroup && !string.Equals(catNameNew, (task.CategoryName ?? "").Trim(), StringComparison.Ordinal))
            renameGroup = await _db.StandardWtsTasks.Where(x => x.IsActive && x.TaskId != task.TaskId && x.CategoryCode == task.CategoryCode).ToListAsync();

        var now = DateTime.Now;
        var logs = new List<StandardWtsTaskChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal)
        {
            oldVal = oldVal?.Trim() ?? "";
            newVal = newVal?.Trim() ?? "";
            if (oldVal != newVal)
            {
                logs.Add(new StandardWtsTaskChangeLog
                {
                    TaskId = task.TaskId,
                    FieldName = fieldName,
                    OldValue = oldVal,
                    NewValue = newVal,
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                });
            }
        }

        CheckAndLog("Mã công việc", task.TaskCode, newCode);
        CheckAndLog("Tên công việc", task.TaskName, updatedModel.TaskName);
        CheckAndLog("Mã nhóm", task.CategoryCode, updatedModel.CategoryCode);
        CheckAndLog("Tên nhóm", task.CategoryName, updatedModel.CategoryName);
        CheckAndLog("Đơn vị tính", task.DefaultUnit, updatedModel.DefaultUnit);
        CheckAndLog("Thời gian chuẩn (s)", task.StandardTimeSec?.ToString(), updatedModel.StandardTimeSec?.ToString());
        CheckAndLog("Ghi chú", task.GhiChu, updatedModel.GhiChu);
        // [WTS-ORDER-SAVE 2026-10-30] Mục 14: chỉ đổi Thứ tự hiển thị trước đây không lưu mà vẫn báo thành công (không có log → bỏ qua cả lượt)
        CheckAndLog("Thứ tự hiển thị", task.DisplayOrder.ToString(), updatedModel.DisplayOrder.ToString());
        // Log thay đổi IsProductiveTask
        CheckAndLog("Phân loại có ích",
            task.IsProductiveTask ? "Có ích" : "Vô ích",
            updatedModel.IsProductiveTask ? "Có ích" : "Vô ích");
        // [TARO-3T-GOP 2026-11-06] khai báo mã gộp (chỉ khi trang gửi danh sách)
        //   (chạy dưới khoá MES_WTS_GOP lấy ở đầu hàm)
        if (gopParts != null)
        {
            var gErr = await ApplyGopAsync(task, newCode, updatedModel.CategoryCode.Trim().ToUpper(), gopParts, currentUserId, now, logs, reason);
            if (gErr != null) { _db.ChangeTracker.Clear(); return (false, gErr); }
        }

        if (logs.Any())
        {
            task.TaskCode = newCode;
            task.TaskName = updatedModel.TaskName.Trim();
            task.CategoryCode = updatedModel.CategoryCode.Trim().ToUpper();
            task.CategoryName = updatedModel.CategoryName.Trim();
            task.DefaultUnit = string.IsNullOrWhiteSpace(updatedModel.DefaultUnit) ? "Chi tiết" : updatedModel.DefaultUnit.Trim();
            task.StandardTimeSec = updatedModel.StandardTimeSec;
            task.DisplayOrder = updatedModel.DisplayOrder;
            task.GhiChu = updatedModel.GhiChu?.Trim();
            task.IsProductiveTask = updatedModel.IsProductiveTask;
            task.UpdatedAt = now;
            task.UpdatedBy = currentUserId;

            // [WTS-NHOM-TEN] đổi Tên nhóm → mọi công việc cùng mã nhóm nhận tên mới (+ log từng dòng) để 1 mã nhóm chỉ có 1 tên
            foreach (var o in renameGroup.Where(o => !string.Equals((o.CategoryName ?? "").Trim(), catNameNew, StringComparison.Ordinal)))
            {
                logs.Add(new StandardWtsTaskChangeLog { TaskId = o.TaskId, FieldName = "Tên nhóm", OldValue = o.CategoryName?.Trim() ?? "", NewValue = catNameNew,
                                                        Reason = Cap500((reason ?? "") + $" (đổi tên nhóm {catNew} cho cả nhóm qua mã {task.TaskCode})"), ChangedAt = now, ChangedBy = currentUserId });
                o.CategoryName = catNameNew; o.UpdatedAt = now; o.UpdatedBy = currentUserId;
            }

            _db.StandardWtsTaskChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }
        if (gopTx != null) await gopTx.CommitAsync();   // [TARO-3T-GOP] nhả khoá MES_WTS_GOP (không đổi gì: Commit rỗng)

        return (true, null);
    }

    /// <summary>[TARO-3T-GOP] Khoá chung khai báo mã gộp (sp_getapplock, theo giao dịch).</summary>
    public const string GopLock = "MES_WTS_GOP";

    /// <summary>
    /// Lưu tổng nhiều thay đổi IsProductiveTask cùng 1 lần (bulk save từ UI).
    /// Xác thực PIN 1 lần duy nhất, ghi ChangeLog cho từng item thực sự thay đổi.
    /// </summary>
    public async Task<(bool Success, string? Error)> BulkUpdateProductiveAsync(
        List<(int TaskId, bool NewValue)> changes, int currentUserId, string pin)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa WTS_MASTER (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.WtsMaster)) return (false, PermCheck.NoEdit("WTS tiêu chuẩn"));
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN xác nhận không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá
        if (!changes.Any()) return (true, null);

        var taskIds = changes.Select(c => c.TaskId).ToList();
        var tasks = await _db.StandardWtsTasks
            .Where(x => taskIds.Contains(x.TaskId) && x.IsActive)
            .ToListAsync();

        var now = DateTime.Now;
        var logs = new List<StandardWtsTaskChangeLog>();

        foreach (var (taskId, newValue) in changes)
        {
            var task = tasks.FirstOrDefault(x => x.TaskId == taskId);
            if (task == null) continue;
            if (task.IsProductiveTask == newValue) continue; // không thay đổi thực sự

            var oldLabel = task.IsProductiveTask ? "Có ích" : "Vô ích";
            var newLabel = newValue ? "Có ích" : "Vô ích";

            task.IsProductiveTask = newValue;
            task.UpdatedAt = now;
            task.UpdatedBy = currentUserId;

            logs.Add(new StandardWtsTaskChangeLog
            {
                TaskId    = task.TaskId,
                FieldName = "Phân loại có ích",
                OldValue  = oldLabel,
                NewValue  = newLabel,
                Reason    = "Cập nhật hàng loạt từ bảng danh mục",
                ChangedAt = now,
                ChangedBy = currentUserId
            });
        }

        if (logs.Any())
        {
            _db.StandardWtsTaskChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int taskId, int currentUserId, string pin)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa WTS_MASTER (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.WtsMaster)) return (false, PermCheck.NoEdit("WTS tiêu chuẩn"));
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN xác nhận không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá

        // [TARO-3T-GOP 2026-11-06] khoá chung MES_WTS_GOP từ TRƯỚC khi đọc mã / khai báo gộp (rà độc lập 06/11 vòng 1 mục 5 + vòng 2 mục 2):
        //   luật "1 mã thành phần thuộc 1 mã gộp" / "không lồng" / "mã đang khai gộp không đổi mã, đổi nhóm, xoá lặng" đọc rồi ghi —
        //   2 người cùng lưu (khai 3T1 cho 3T và 4T; đổi mã 3T1 đúng lúc người khác khai 3T ← 3T1) thì người sau chờ, đọc lại trong khoá.
        //   Nguồn: CLAUDE (rà độc lập gói 4 06/11).
        await using var gopTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        if (!await PoGuard.TryLockInTransactionAsync(_db, GopLock))
            return (false, "Đang có người khác lưu danh mục WTS tiêu chuẩn / khai báo mã gộp — chờ vài giây rồi bấm lưu lại.");
        var task = await _db.StandardWtsTasks.FirstOrDefaultAsync(x => x.TaskId == taskId && x.IsActive);
        if (task == null) return (false, "Không tìm thấy công việc cần xóa.");

        // [RS-E8 2026-10-29] Mã đang dùng trong quy trình Part Master (bước đang dùng, Part chưa ngưng) → không xoá:
        //   xoá thì Part Master không chọn lại được mã, PO mới chụp quy trình có mã không còn trong danh mục.
        //   PO cũ đã chụp (snapshot) / WTS cũ không cản: WTS vẫn ghi được mã có trong snapshot; cần lại mã thì "Thêm công việc mới" cùng mã = khôi phục.
        var parts = await PartsUsingAsync(task.TaskCode);
        if (parts.Count > 0)
        {
            var list = string.Join(", ", parts.Take(5)) + (parts.Count > 5 ? $" … (+{parts.Count - 5})" : "");
            return (false, $"Mã công việc '{task.TaskCode}' đang dùng trong quy trình của {parts.Count} Part ({list}) — không xóa được. Bỏ mã này khỏi Part Master trước.");
        }

        var now = DateTime.Now;
        task.IsActive = false;
        task.UpdatedAt = now;
        task.UpdatedBy = currentUserId;
        // [TARO-3T-GOP 2026-11-06] mã xoá khỏi danh mục → bỏ khai báo gộp có mã này (là mã gộp hoặc thành phần), giữ dòng (IsActive 0) để truy
        var gopsOff = await _db.StandardWtsTaskGops.Where(g => g.IsActive && (g.GopTaskCode == task.TaskCode || g.PartTaskCode == task.TaskCode)).ToListAsync();
        foreach (var g in gopsOff) { g.IsActive = false; g.RemovedBy = currentUserId; g.RemovedAt = now; }
        if (gopsOff.Count > 0)
            _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
            {
                TaskId = task.TaskId, FieldName = "Mã gộp (làm thay)",
                OldValue = string.Join("; ", gopsOff.Select(g => $"{g.GopTaskCode} thay {g.PartTaskCode}")), NewValue = "",
                Reason = "Xóa công việc tiêu chuẩn — bỏ khai báo gộp", ChangedAt = now, ChangedBy = currentUserId
            });

        _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
        {
            TaskId = task.TaskId,
            FieldName = "Trạng thái",
            OldValue = "Đang áp dụng",
            NewValue = "Đã xóa khỏi danh mục tiêu chuẩn",
            Reason = "Xóa công việc tiêu chuẩn",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        if (gopTx != null) await gopTx.CommitAsync();   // [TARO-3T-GOP] nhả khoá MES_WTS_GOP
        return (true, null);
    }

    // ─────────────────────────────────────────────────────────────────────
    // [RS-E8 2026-10-29] Mã CV đang được dùng ở đâu (mọi nơi nối theo CHỮ mã)
    //   Part Master: Taro dùng WtsTaskCode (trống thì NC); Bavia / Rửa / KCS / Đóng gói dùng NC = mã CV.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Part No (đang dùng, chưa ngưng) có bước đang dùng mã này.</summary>
    private async Task<List<string>> PartsUsingAsync(string code)
    {
        var ids = new List<int>();
        ids.AddRange(await _db.PartTaroSteps.AsNoTracking()
            .Where(s => s.IsActive && (s.WtsTaskCode == code || ((s.WtsTaskCode == null || s.WtsTaskCode == "") && s.NC == code)))
            .Select(s => s.PartId).ToListAsync());
        ids.AddRange(await _db.PartBaviaSteps.AsNoTracking().Where(s => s.IsActive && s.NC == code).Select(s => s.PartId).ToListAsync());
        ids.AddRange(await _db.PartWashingSteps.AsNoTracking().Where(s => s.IsActive && s.NC == code).Select(s => s.PartId).ToListAsync());
        ids.AddRange(await _db.PartInspectionSteps.AsNoTracking().Where(s => s.IsActive && s.NC == code).Select(s => s.PartId).ToListAsync());
        ids.AddRange(await _db.PartPackagingSteps.AsNoTracking().Where(s => s.IsActive && s.NC == code).Select(s => s.PartId).ToListAsync());
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0) return new List<string>();
        var parts = await _db.PartMasters.AsNoTracking()
            .Where(p => distinct.Contains(p.PartId) && p.IsActive && !p.IsObsolete)
            .Select(p => p.PartNo).ToListAsync();
        return parts.OrderBy(x => x).ToList();
    }

    /// <summary>Mã đã từng dùng ở bất kỳ đâu (Part Master kể cả bước cũ, snapshot PO, WTS, hoạt động, phát sinh) → chữ mô tả; chưa dùng → null.</summary>
    private async Task<string?> UsedWhereAsync(string code)
    {
        if (await _db.PartTaroSteps.AsNoTracking().AnyAsync(s => s.WtsTaskCode == code || s.NC == code)
            || await _db.PartBaviaSteps.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.PartWashingSteps.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.PartInspectionSteps.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.PartPackagingSteps.AsNoTracking().AnyAsync(s => s.NC == code))
            return "Part Master";
        if (await _db.KhPlanRouteSnapshotTaro.AsNoTracking().AnyAsync(s => s.WtsTaskCode == code || s.NC == code)
            || await _db.KhPlanRouteSnapshotBavia.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.KhPlanRouteSnapshotWashing.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.KhPlanRouteSnapshotInspection.AsNoTracking().AnyAsync(s => s.NC == code)
            || await _db.KhPlanRouteSnapshotPackaging.AsNoTracking().AnyAsync(s => s.NC == code))
            return "quy trình (snapshot) của PO";
        if (await _db.WtsProductionLogs.AsNoTracking().AnyAsync(w => w.WtsCode == code || (w.ProcessGroup != "GC" && w.NC == code))
            || await _db.WorkerActivityLogs.AsNoTracking().AnyAsync(w => w.WtsCode == code)
            || await _db.WtsExtraItems.AsNoTracking().AnyAsync(w => w.WtsCode == code))
            return "WTS đã ghi";
        return null;
    }

    public async Task<List<StandardWtsTaskChangeLog>> GetChangeLogsAsync(int taskId)
    {
        return await _db.StandardWtsTaskChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }

    public async Task<byte[]> ExportExcelAsync(List<StandardWtsTask> data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("DS WTS tieu chuan");

        ws.Cell("A1").Value = "DANH MỤC CÔNG VIỆC TIÊU CHUẨN (WORK TIME SHEET)";
        ws.Range("A1:G1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(XLColor.FromHtml("#1e3a8a"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell("A2").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Tổng số: {data.Count} công việc tiêu chuẩn";
        ws.Range("A2:G2").Merge().Style
            .Font.SetItalic(true)
            .Font.SetFontSize(10)
            .Font.SetFontColor(XLColor.FromHtml("#64748b"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "STT", "Nhóm công đoạn", "Mã công việc", "Tên công việc tiêu chuẩn", "Đơn vị tính", "Phân loại", "Ghi chú" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2b7bc4"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        ws.Row(4).Height = 25;

        int r = 5;
        int stt = 1;
        foreach (var t in data)
        {
            ws.Cell(r, 1).SetValue(stt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 2).SetValue(t.CategoryName).Style.Font.SetBold(true);
            ws.Cell(r, 3).SetValue(t.TaskCode).Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#dc2626")).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 4).SetValue(t.TaskName);
            ws.Cell(r, 5).SetValue(t.DefaultUnit).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 6).SetValue(t.IsProductiveTask ? "Có ích" : "Vô ích").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 7).SetValue(t.GhiChu ?? "");

            ws.Row(r).Height = 20;
            r++;
            stt++;
        }

        var dataRange = ws.Range(4, 1, r - 1, 7);
        dataRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
