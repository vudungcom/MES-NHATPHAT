using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class ThietBiService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public ThietBiService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<ThietBi>> GetAllActiveAsync()
    {
        var list = await _db.ThietBis
            .Where(t => t.IsActive)
            .OrderBy(t => t.BoPhan).ThenBy(t => t.SoMay)
            .ToListAsync();

        // Lấy danh sách ID các máy đang không ở trạng thái 'Hoạt động'
        var inactiveIds = list.Where(t => t.TrangThai != "Hoạt động").Select(t => t.ThietBiId).ToList();
        if (inactiveIds.Any())
        {
            var logs = await _db.ThietBiChangeLogs
                .Where(x => inactiveIds.Contains(x.ThietBiId))
                .OrderByDescending(x => x.ChangedAt)
                .ToListAsync();

            var logDict = logs
                .GroupBy(x => x.ThietBiId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var may in list)
            {
                if (logDict.TryGetValue(may.ThietBiId, out var log) && log != null)
                {
                    may.LyDoHienTai = log.Reason;
                    may.ThoiGianDungTu = log.ThoiGianBatDau ?? log.ChangedAt;
                }
            }
        }

        return list;
    }

    public async Task<ThietBi?> GetByIdAsync(int thietBiId)
    {
        return await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == thietBiId);
    }

    public async Task<ThietBiChangeLog?> GetLatestOpenIncidentAsync(int thietBiId)
    {
        return await _db.ThietBiChangeLogs
            .Where(x => x.ThietBiId == thietBiId && x.ThoiGianBatDau != null && x.ThoiGianKetThuc == null)
            .OrderByDescending(x => x.ChangedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>[LEN-CHECK 2026-11-16] (BM1-15) Độ dài ô chữ theo cột ThietBis — trước: gõ dài nổ SqlException không bắt.</summary>
    public static string? ThietBiLenError(ThietBi m)
        => LenCheck.FirstOver((m.SoMay, 50, "Số máy"), (m.BoPhan, 100, "Bộ phận"), (m.MaMay, 100, "Mã máy"), (m.TenMay, 100, "Tên máy"), (m.LoaiMay, 100, "Loại máy"));

    public async Task<(bool Success, string? Error)> CreateAsync(ThietBi thietBi, int currentUserId)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa THIET_BI (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.ThietBi)) return (false, PermCheck.NoEdit("Thiết bị"));
        if (string.IsNullOrWhiteSpace(thietBi.SoMay)) return (false, "Số máy không được để trống.");
        if (ThietBiLenError(thietBi) is string eLen) return (false, eLen);   // [LEN-CHECK 2026-11-16] (BM1-15)

        var isDuplicate = await _db.ThietBis.AnyAsync(t => t.SoMay == thietBi.SoMay.Trim() && t.IsActive);
        if (isDuplicate) return (false, $"Số máy '{thietBi.SoMay}' đã tồn tại trong hệ thống.");

        thietBi.SoMay = thietBi.SoMay.Trim();
        thietBi.CreatedAt = DateTime.Now;
        thietBi.CreatedBy = currentUserId;
        thietBi.IsActive = true;

        // [TB-TAO-1TX 2026-11-20] gói nhỏ G23-GHI-NHO L15: máy + dòng lịch sử "Tạo mới" 1 giao dịch; lỗi → quay lại + gỡ bản EF (trước: lỗi thì máy vẫn
        //   Added trong DbContext dùng chung cả phiên trang → bấm Lưu lại / thao tác khác chèn lại máy lỗi). Nguồn: CLAUDE rà độc lập Gói 23 (L15); Hùng 20/11 18:12.
        await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            _db.ThietBis.Add(thietBi);
            await _db.SaveChangesAsync();

            _db.ThietBiChangeLogs.Add(new ThietBiChangeLog
            {
                ThietBiId = thietBi.ThietBiId,
                FieldName = "Tạo mới",
                OldValue = null,
                NewValue = $"{thietBi.SoMay} - {thietBi.TenMay} ({thietBi.BoPhan})",
                Reason = "Khởi tạo thiết bị",
                ChangedAt = DateTime.Now,
                ChangedBy = currentUserId
            });
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            if (tx != null) { try { await tx.RollbackAsync(); } catch { } }
            foreach (var en in _db.ChangeTracker.Entries<ThietBi>().Where(x => x.State == EntityState.Added || ReferenceEquals(x.Entity, thietBi)).ToList()) en.State = EntityState.Detached;
            foreach (var en in _db.ChangeTracker.Entries<ThietBiChangeLog>().Where(x => x.State == EntityState.Added).ToList()) en.State = EntityState.Detached;
            thietBi.ThietBiId = 0;
            return (false, UiSafe.Text(ex, null, "thêm thiết bị"));
        }
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        ThietBi updatedModel, 
        string? reason, 
        DateTime? thoiGianBatDau, 
        DateTime? thoiGianKetThuc, 
        int currentUserId, 
        string pin)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa THIET_BI (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.ThietBi)) return (false, PermCheck.NoEdit("Thiết bị"));
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá

        if (ThietBiLenError(updatedModel) is string eLenU) return (false, eLenU);   // [LEN-CHECK 2026-11-16] (BM1-15)
        if (LenCheck.Over(reason, 500, "Lý do") is string eRs) return (false, eRs);   // [LEN-CHECK] ThietBiChangeLogs.Reason 500
        var existing = await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == updatedModel.ThietBiId);
        if (existing == null) return (false, "Không tìm thấy thiết bị.");

        if (existing.SoMay != updatedModel.SoMay.Trim())
        {
            var isDuplicate = await _db.ThietBis.AnyAsync(t => t.SoMay == updatedModel.SoMay.Trim() && t.ThietBiId != updatedModel.ThietBiId && t.IsActive);
            if (isDuplicate) return (false, $"Số máy '{updatedModel.SoMay}' đã tồn tại ở thiết bị khác.");
            // [RS-D9 2026-10-31] Số máy đã dùng ở đâu đó thì không đổi số (như mã CV [RS-E8])
            var used = await MachineInUseAsync(existing.SoMay, forDelete: false);
            if (used != null)
                return (false, $"Số máy '{existing.SoMay}' đã dùng ở {used} — không đổi số máy được (WTS, báo cáo máy, TG chuẩn, máy loại trừ nối theo số máy). Chỉ sửa tên / mã / loại; máy khác hẳn thì thêm máy mới.");
        }

        var now = DateTime.Now;

        // Chặn thời gian tương lai
        if (thoiGianBatDau.HasValue && thoiGianBatDau.Value > now)
        {
            return (false, $"Thời gian bắt đầu ({thoiGianBatDau.Value:HH:mm:ss}) không được lớn hơn hiện tại ({now:HH:mm:ss}).");
        }

        if (thoiGianKetThuc.HasValue && thoiGianKetThuc.Value > now)
        {
            return (false, $"Thời gian khắc phục ({thoiGianKetThuc.Value:HH:mm:ss}) không được lớn hơn hiện tại ({now:HH:mm:ss}).");
        }

        int? soPhutDung = null;
        // [BM1-01 2026-11-16] Sổ sự cố máy: 1 lần dừng = 1 dòng log mở (ThoiGianBatDau có, ThoiGianKetThuc null). Trước: máy ĐANG dừng mà lưu lại
        //   (đổi Báo hỏng → Bảo trì, hay chỉ sửa tên / nhập PIN) thêm 1 dòng mở mới, về Hoạt động chỉ chốt dòng MỚI NHẤT → dòng cũ mở vĩnh viễn →
        //   Nguồn: CLAUDE quét Fable 15/11 (B-M1 BM1-01 Trung nặng nhất); Hùng 16/11 11:34 OK Gói 24 (câu B2 SQL 17 chờ duyệt).
        //   Hiệu suất máy tô "Hỏng 100 %" mọi ngày sau, không nút nào chốt được. Nay: đang dừng mà lưu lại → CẬP NHẬT dòng mở đang có (loại dừng,
        //   lý do, giờ bắt đầu nếu nhập) — không thêm dòng mở; về Hoạt động → chốt MỌI dòng mở của máy. Dữ liệu cũ đã mở: tools/sql/17_THIETBI_LOG_MO.sql.
        ThietBiChangeLog? openToUpdate = null;      // [BM1-01] dòng mở sẽ cập nhật tại chỗ (đang dừng → vẫn dừng)
        bool statusLogIsIncident = true;             // [BM1-01] dòng log "Trạng thái" có mang giờ dừng (mở sự cố mới) không
        if (updatedModel.TrangThai != "Hoạt động")
        {
            thoiGianKetThuc = null;
            soPhutDung = null;
            if (existing.TrangThai != "Hoạt động")
            {
                openToUpdate = await GetLatestOpenIncidentAsync(existing.ThietBiId);
                if (openToUpdate != null && existing.TrangThai?.Trim() == updatedModel.TrangThai?.Trim())
                {
                    // cùng loại dừng (chỉ sửa tên / lý do / giờ bắt đầu): cập nhật ngay trên dòng mở đang có — không mở dòng thứ 2
                    statusLogIsIncident = false;
                    if (thoiGianBatDau.HasValue) openToUpdate.ThoiGianBatDau = thoiGianBatDau;
                    if (!string.IsNullOrWhiteSpace(reason)) openToUpdate.Reason = reason;
                    thoiGianBatDau = openToUpdate.ThoiGianBatDau;
                }
                else if (openToUpdate != null)
                {
                    // ĐỔI loại dừng (vd Báo hỏng → Bảo trì): chốt dòng mở tại `now`, dòng log "Trạng thái" mới mở sự cố loại mới từ `now`
                    //   (rà độc lập 16/11: ghi đè NewValue trên dòng cũ làm Hiệu suất máy đổi loại hồi tố cả khoảng đã dừng những ngày trước)
                    openToUpdate.ThoiGianKetThuc = now;
                    openToUpdate.SoPhutDung = Math.Max(0, (int)Math.Round((now - (openToUpdate.ThoiGianBatDau ?? now)).TotalMinutes));
                    thoiGianBatDau = thoiGianBatDau.HasValue && thoiGianBatDau.Value > now ? thoiGianBatDau : now;
                }
                else if (!thoiGianBatDau.HasValue) thoiGianBatDau = now;   // dữ liệu cũ không có dòng mở → mở 1 dòng như trước
            }
            else if (!thoiGianBatDau.HasValue) thoiGianBatDau = now;
        }
        else
        {
            if (existing.TrangThai != "Hoạt động")
            {
                // [BM1-01] chốt MỌI dòng sự cố còn mở của máy (trước: chỉ dòng mới nhất)
                var openAll = await _db.ThietBiChangeLogs
                    .Where(x => x.ThietBiId == existing.ThietBiId && x.ThoiGianBatDau != null && x.ThoiGianKetThuc == null)
                    .OrderByDescending(x => x.ChangedAt).ToListAsync();
                var openIncident = openAll.FirstOrDefault();
                thoiGianBatDau = openIncident?.ThoiGianBatDau ?? thoiGianBatDau ?? now;
                thoiGianKetThuc = thoiGianKetThuc ?? now;

                if (thoiGianKetThuc.Value < thoiGianBatDau.Value)
                {
                    return (false, "Thời gian khắc phục xong không được nhỏ hơn thời gian bắt đầu hỏng.");
                }
                soPhutDung = (int)Math.Round((thoiGianKetThuc.Value - thoiGianBatDau.Value).TotalMinutes);

                foreach (var o in openAll)
                {
                    o.ThoiGianKetThuc = thoiGianKetThuc;
                    o.SoPhutDung = (int)Math.Round((thoiGianKetThuc.Value - (o.ThoiGianBatDau ?? thoiGianKetThuc.Value)).TotalMinutes);
                    if (o.SoPhutDung < 0) o.SoPhutDung = 0;
                }
            }
            else
            {
                thoiGianBatDau = null;
                thoiGianKetThuc = null;
                soPhutDung = null;
            }
        }

        var changeLogs = new List<ThietBiChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal, bool isStatusChange = false)
        {
            oldVal = oldVal ?? "";
            newVal = newVal ?? "";
            if (oldVal.Trim() != newVal.Trim())
            {
                var log = new ThietBiChangeLog
                {
                    ThietBiId = existing.ThietBiId,
                    FieldName = fieldName,
                    OldValue = oldVal.Trim(),
                    NewValue = newVal.Trim(),
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                };

                if (isStatusChange && statusLogIsIncident)   // [BM1-01] đổi loại dừng khi đang dừng: không mang giờ (dòng mở đang có đã cập nhật)
                {
                    log.ThoiGianBatDau = thoiGianBatDau;
                    log.ThoiGianKetThuc = thoiGianKetThuc;
                    log.SoPhutDung = soPhutDung;
                }

                changeLogs.Add(log);
            }
        }

        CheckAndLog("Số máy", existing.SoMay, updatedModel.SoMay);
        CheckAndLog("Bộ phận", existing.BoPhan, updatedModel.BoPhan);
        CheckAndLog("Mã máy", existing.MaMay, updatedModel.MaMay);
        CheckAndLog("Tên máy", existing.TenMay, updatedModel.TenMay);
        CheckAndLog("Loại máy", existing.LoaiMay, updatedModel.LoaiMay);
        CheckAndLog("Trạng thái", existing.TrangThai, updatedModel.TrangThai, isStatusChange: true);

        if (!changeLogs.Any() && thoiGianBatDau.HasValue && openToUpdate == null)   // [BM1-01] đang có dòng mở thì không thêm "Nhật ký dừng máy" mở thứ 2
        {
            changeLogs.Add(new ThietBiChangeLog
            {
                ThietBiId = existing.ThietBiId,
                FieldName = "Nhật ký dừng máy",
                OldValue = existing.TrangThai,
                NewValue = updatedModel.TrangThai,
                Reason = reason,
                ThoiGianBatDau = thoiGianBatDau,
                ThoiGianKetThuc = thoiGianKetThuc,
                SoPhutDung = soPhutDung,
                ChangedAt = now,
                ChangedBy = currentUserId
            });
        }

        if (changeLogs.Any() || openToUpdate != null)   // [BM1-01] chỉ cập nhật dòng mở (lý do / giờ) cũng phải lưu
        {
            existing.SoMay = updatedModel.SoMay.Trim();
            existing.BoPhan = updatedModel.BoPhan?.Trim() ?? "";
            existing.MaMay = updatedModel.MaMay?.Trim() ?? "";
            existing.TenMay = updatedModel.TenMay?.Trim() ?? "";
            existing.LoaiMay = updatedModel.LoaiMay?.Trim() ?? "";
            existing.TrangThai = updatedModel.TrangThai?.Trim() ?? "Hoạt động";
            existing.UpdatedAt = now;
            existing.UpdatedBy = currentUserId;

            _db.ThietBiChangeLogs.AddRange(changeLogs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    // ════════════════════════════════════════════════════════════════════
    // [RS-D9 2026-10-31] Số máy là chữ nối giữa Thiết bị ↔ WTS (MachineUsed) ↔ Part Master / snapshot PO (máy đăng ký, máy đồng dạng CSV)
    //   ↔ danh sách máy loại trừ (N3). Trước: đổi / xoá số máy tự do → WTS cũ biến khỏi báo cáo máy, TG chuẩn trỏ máy không còn,
    //   máy loại trừ mất (NC loại trừ thành NC thường — đổi đường đi của hàng). Nay:
    //   - đổi số máy: chặn nếu số cũ đã có ở WTS / Part Master / snapshot PO / máy loại trừ;
    //   - xoá máy: chặn nếu máy có trong quy trình Part Master ĐANG DÙNG (Part chưa ngưng, công đoạn chưa xoá) hoặc danh sách máy loại trừ.
    //   Trả null = không dùng ở đâu; còn lại = mô tả chỗ dùng (vài Part đầu).
    // ════════════════════════════════════════════════════════════════════
    private async Task<string?> MachineInUseAsync(string soMay, bool forDelete)
    {
        var so = (soMay ?? "").Trim();
        if (so.Length == 0) return null;
        // so không phân biệt hoa / thường, bỏ khoảng trắng (như sổ + hiệu suất máy nối máy); CSV máy đồng dạng tách theo ',' và ';' (như Part Master)
        bool Eq(string? a) => string.Equals(a?.Trim(), so, StringComparison.OrdinalIgnoreCase);
        bool Tok(string? csv) => !string.IsNullOrEmpty(csv)
            && csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(Eq);
        var where = new List<string>();

        // [B7-TIMING-MAY 2026-11-06] Cột TimingMachine (Máy bấm giờ) chỉ ghi máy đã bấm giờ chuẩn — không phải máy trong quy trình (WTS / sổ không đọc cột này).
        //   XOÁ máy: không còn tính TimingMachine là "máy có trong quy trình" (trước: máy chỉ dùng để bấm giờ cũng bị chặn xoá).
        //   ĐỔI số máy vẫn xét TimingMachine như cũ: cột là chữ nối với số máy, đổi số làm chữ máy bấm giờ ở Part Master trỏ vào máy không còn (mất truy vết).
        //   Nguồn: CLAUDE 02/11 (PHAT-HIEN-0211 mục 6), Hùng 05/11 15:37 chốt (a); phạm vi "chỉ nhánh xoá" gửi Hùng 06/11 bản "em hiểu".
        var steps = await _db.PartMachiningSteps.AsNoTracking().Include(s => s.Timings)
            .Where(s => s.MachineRegistered == so || (!forDelete && s.TimingMachine != null && s.TimingMachine.Contains(so)) || s.Timings.Any(t => t.SoMay.Contains(so)))
            .ToListAsync();
        var hitSteps = steps
            .Where(s => Eq(s.MachineRegistered) || (!forDelete && Tok(s.TimingMachine)) || s.Timings.Any(t => (!forDelete || t.IsActive) && Tok(t.SoMay)))
            .Where(s => !forDelete || s.IsActive).ToList();
        var pIds = hitSteps.Select(s => s.PartId).Distinct().ToList();
        var parts = await _db.PartMasters.AsNoTracking().Where(p => pIds.Contains(p.PartId))
            .Select(p => new { p.PartId, p.PartNo, p.IsObsolete, p.IsActive }).ToListAsync();
        var partNos = parts.Where(p => !forDelete || (!p.IsObsolete && p.IsActive))   // xoá máy: chỉ Part đang dùng (chưa ngưng, chưa xoá)
            .Select(p => p.PartNo).Distinct().OrderBy(x => x).ToList();
        if (partNos.Count > 0)
            where.Add("Part Master " + string.Join(", ", partNos.Take(3)) + (partNos.Count > 3 ? $" (+{partNos.Count - 3})" : ""));
        if (await _db.MachineExcludeSettings.AnyAsync(x => x.SoMay == so))
            where.Add("danh sách máy loại trừ");
        if (!forDelete)
        {
            if (await _db.WtsProductionLogs.AnyAsync(w => w.MachineUsed == so))
                where.Add("WTS");
            var snapM = await _db.KhPlanRouteSnapshotMachining.AsNoTracking()
                .Where(x => x.MachineRegistered == so || (x.TimingMachine != null && x.TimingMachine.Contains(so)))
                .Select(x => new { x.MachineRegistered, x.TimingMachine }).ToListAsync();
            var snapT = await _db.KhPlanRouteSnapshotMachiningTimings.AsNoTracking()
                .Where(x => x.SoMay.Contains(so)).Select(x => x.SoMay).ToListAsync();
            if (snapM.Any(x => Eq(x.MachineRegistered) || Tok(x.TimingMachine)) || snapT.Any(Tok))
                where.Add("quy trình PO (snapshot)");
        }
        return where.Count == 0 ? null : string.Join(" · ", where);
    }

    /// <summary>[RS-D9-PO-MO 2026-11-16] Số PO chưa Đủ thành phẩm (sổ chung) có máy trong snapshot Gia công (máy đăng ký / máy đồng dạng) hoặc WTS;
    /// null = không có. Trả "PO1, PO2 (+n)".</summary>
    public async Task<string?> MachineInOpenPoAsync(string soMay)
    {
        var so = (soMay ?? "").Trim();
        if (so.Length == 0) return null;
        bool Eq(string? a) => string.Equals(a?.Trim(), so, StringComparison.OrdinalIgnoreCase);
        bool Tok(string? csv) => !string.IsNullOrEmpty(csv)
            && csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(Eq);
        var ids = new HashSet<int>();
        var snapM = await _db.KhPlanRouteSnapshotMachining.AsNoTracking().Where(x => x.MachineRegistered == so)
            .Select(x => new { x.KhPlanDetailId, x.MachineRegistered }).ToListAsync();
        foreach (var x in snapM.Where(x => Eq(x.MachineRegistered))) ids.Add(x.KhPlanDetailId);
        var snapT = await _db.KhPlanRouteSnapshotMachiningTimings.AsNoTracking().Where(x => x.SoMay.Contains(so))
            .Select(x => new { x.KhPlanDetailId, x.SoMay }).ToListAsync();
        foreach (var x in snapT.Where(x => Tok(x.SoMay))) ids.Add(x.KhPlanDetailId);
        var wts = await _db.WtsProductionLogs.AsNoTracking().Where(w => !w.IsVoided && w.MachineUsed == so)
            .Select(w => w.KhPlanDetailId).Distinct().ToListAsync();
        foreach (var id in wts) ids.Add(id);
        if (ids.Count == 0) return null;
        var ledger = await new HandoverLedgerService(_db).GetLedgerForDetailsAsync(ids);
        var open = ledger.Values.Where(r => HandoverLedgerService.WhereIs(r).Status != "DU").Select(r => r.PurchaseOrder).Distinct().OrderBy(x => x).ToList();
        if (open.Count == 0) return null;
        return string.Join(", ", open.Take(3)) + (open.Count > 3 ? $" (+{open.Count - 3})" : "");
    }

    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int thietBiId, int currentUserId, string pin)
    {
        // [RS-C1 2026-10-29] Máy chủ kiểm quyền Sửa THIET_BI (trước: chỉ cần đăng nhập)
        if (!await PermCheck.CanEditAsync(_db, currentUserId, MES.Web.Constants.SystemPermissions.ThietBi)) return (false, PermCheck.NoEdit("Thiết bị"));
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, _authSvc.PinErrorText("Mã PIN không chính xác."));   // [PIN-THROTTLE 2026-11-16] lời còn N lần / đang khoá

        var thietBi = await _db.ThietBis.FirstOrDefaultAsync(t => t.ThietBiId == thietBiId);
        if (thietBi == null) return (false, "Không tìm thấy thiết bị.");
        if (!thietBi.IsActive) return (false, "Thiết bị này đã bị xóa.");
        // [RS-D9 2026-10-31] Máy đang có trong quy trình Part Master đang dùng / danh sách máy loại trừ → không xoá (PO mới sẽ trỏ máy không còn)
        var usedDel = await MachineInUseAsync(thietBi.SoMay, forDelete: true);
        if (usedDel != null)
            return (false, $"Máy '{thietBi.SoMay}' đang dùng ở {usedDel} — không xoá được. Gỡ máy khỏi quy trình Part Master / danh sách máy loại trừ trước.");
        // [RS-D9-PO-MO 2026-11-16] Máy còn trong quy trình (snapshot) hoặc WTS của PO CHƯA đủ thành phẩm → không xoá (PO đang chạy trỏ máy không còn:
        //   WTS không chọn được máy, báo cáo máy / TG chuẩn mất đích). PO đã Đủ thành phẩm không chặn. Máy không dùng nữa → đổi Trạng thái "Tạm dừng"
        //   (giữ máy + lịch sử + báo cáo). Trạng thái PO đọc sổ chung (HandoverLedgerService.WhereIs, như cột Trạng thái trang Kế hoạch).
        //   Nguồn: CLAUDE quét Fable 15/11 (câu hỏi 10 — RS-D9 / BM1-03); Hùng 15/11 15:10 OK câu 10.
        var openPo = await MachineInOpenPoAsync(thietBi.SoMay);
        if (openPo != null)
            return (false, $"Máy '{thietBi.SoMay}' còn trong quy trình / WTS của PO chưa xong: {openPo} — không xoá được. "
                         + "Máy không dùng nữa thì sửa Trạng thái sang \"Tạm dừng\" (giữ lịch sử và báo cáo); xoá sau khi các PO này đủ thành phẩm.");

        var now = DateTime.Now;
        thietBi.IsActive = false;
        thietBi.TrangThai = "Đã xóa";
        thietBi.UpdatedAt = now;
        thietBi.UpdatedBy = currentUserId;

        _db.ThietBiChangeLogs.Add(new ThietBiChangeLog
        {
            ThietBiId = thietBi.ThietBiId,
            FieldName = "Trạng thái",
            OldValue = "Hoạt động",
            NewValue = "Đã xóa (Soft Delete)",
            Reason = "Xóa thiết bị khỏi hệ thống",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ThietBiChangeLog>> GetChangeLogsAsync(int thietBiId)
    {
        return await _db.ThietBiChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.ThietBiId == thietBiId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }
}