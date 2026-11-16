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

    // [PHAT-HIEN-0311 2026-11-04] Lưu / Ẩn-Hiện / Xoá ca: máy chủ tự kiểm quyền SỬA "Cài đặt ca làm việc" (CA_LAM_VIEC) theo DB
    //   (PermCheck — cùng luật RS-C1). Trước: service không kiểm, trang chỉ khoá nút Thêm → người chỉ có quyền Xem vẫn bấm
    //   Sửa / Ẩn / Xoá được, đổi giờ ca / giờ nghỉ (ảnh hưởng phút WTS, hiệu suất). Nguồn: CLAUDE rà độc lập 03/11, Hùng duyệt 04/11 08:24.
    public const string TenMuc = "Cài đặt ca làm việc";

    public async Task<(bool Ok, string Error)> SaveAsync(CaLamViec ca, List<CaLamViec_NghiGiaiLao> nghiList, int actorUserId)
    {
        if (!await PermCheck.CanEditAsync(_db, actorUserId, MES.Web.Constants.SystemPermissions.CaLamViec))   // [PHAT-HIEN-0311]
            return (false, PermCheck.NoEdit(TenMuc));

        if (string.IsNullOrWhiteSpace(ca.TenCa))
            return (false, "Tên ca không được để trống.");
        // [LEN-CHECK 2026-11-16] (BM2-05) Tên ca 100, tên giờ nghỉ 100 (cột DB) — trước: SqlException không bắt ở nhánh Sửa → trang chết
        if (LenCheck.Over(ca.TenCa, 100, "Tên ca") is string eTen) return (false, eTen);
        foreach (var n in nghiList)
            if (LenCheck.Over(n.GhiChu, 100, "Ghi chú giờ nghỉ") is string eNghi) return (false, eNghi);
        // [CA-0-PHUT 2026-11-16] (BM2-06) ca 0 phút (bắt đầu = kết thúc): IsOvernight = false, tổng phút 0 → WTS chọn ca này tính phút sai. Chặn.
        //   Nguồn: CLAUDE quét Fable 15/11 (B-M2 BM2-06); HÙNG 16/11 05:15 "không ảnh hưởng khách thì làm theo ý bạn" (Gói 23).
        if (ca.GioBatDau == ca.GioKetThuc) return (false, "Giờ bắt đầu và giờ kết thúc ca trùng nhau (ca 0 phút) — sửa lại giờ.");

        foreach (var n in nghiList)
        {
            if (n.NghiKetThuc <= n.NghiBatDau)
                return (false, $"Nghỉ giải lao: giờ kết thúc phải sau giờ bắt đầu ({n.NghiBatDau:HH\\:mm}).");
        }

        // [CA-NGHI-NGOAI 2026-11-16] (BM2-06 phần còn) Giờ nghỉ phải nằm trong khung ca (tính cả ca đêm qua 0h: đo theo phút kể từ giờ bắt đầu ca) —
        //   trước: ca 07:00–17:00 với nghỉ 20:00–21:00 lưu được (không trừ phút, nhưng gây hiểu nhầm / dữ liệu rác). Trùng tên ca đang bật cũng chặn.
        //   Nguồn: CLAUDE quét Fable 15/11 (B-M2 BM2-06 Trung); Hùng 16/11 11:34 OK Gói 24 (đề xuất B3: chặn).
        {
            int Off(TimeOnly t) => (int)(((t - ca.GioBatDau).TotalMinutes % 1440 + 1440) % 1440);
            int lenCa = Off(ca.GioKetThuc) == 0 ? 1440 : Off(ca.GioKetThuc);
            foreach (var n in nghiList)
            {
                int sB = Off(n.NghiBatDau); int sE = sB + (int)(n.NghiKetThuc - n.NghiBatDau).TotalMinutes;
                if (sE > lenCa)
                    return (false, $"Nghỉ giải lao {n.NghiBatDau:HH\\:mm}–{n.NghiKetThuc:HH\\:mm} nằm ngoài khung ca {ca.GioBatDau:HH\\:mm}–{ca.GioKetThuc:HH\\:mm} — sửa giờ nghỉ hoặc giờ ca.");
            }
            var ten = ca.TenCa.Trim();
            var trung = await _db.CaLamViecs.AsNoTracking().Where(c => c.IsActive && c.CaId != ca.CaId).Select(c => c.TenCa).ToListAsync();
            if (trung.Any(t => string.Equals((t ?? "").Trim(), ten, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Tên ca \"{ten}\" đã có ca khác đang dùng — đặt tên khác (hoặc Ẩn ca cũ).");
        }

        // [CA-NGHI-OVERLAP 2026-11-01] 2 khoảng nghỉ chồng nhau → trước: trang WTS trừ phần chồng 2 lần (phút làm âm / thiếu).
        //   Nay chặn lưu ca (WorkMinutes.BreakSegments vẫn gộp khoảng chồng của dữ liệu ca cũ cho an toàn).
        var _sorted = nghiList.OrderBy(x => x.NghiBatDau).ToList();
        for (int i = 1; i < _sorted.Count; i++)
            if (_sorted[i].NghiBatDau < _sorted[i - 1].NghiKetThuc)
                return (false, $"Nghỉ giải lao {_sorted[i - 1].NghiBatDau:HH\\:mm}–{_sorted[i - 1].NghiKetThuc:HH\\:mm} và {_sorted[i].NghiBatDau:HH\\:mm}–{_sorted[i].NghiKetThuc:HH\\:mm} chồng nhau — sửa cho tách rời.");

        // [CA-LOG 2026-11-06] ca + giờ nghỉ + dòng log trong 1 giao dịch (trước: tạo ca lưu 2 lần rời nhau — lỗi lần 2 còn ca không giờ nghỉ)
        using var trx = await _db.Database.BeginTransactionAsync();
        if (ca.CaId == 0)
        {
            try
            {
                _db.CaLamViecs.Add(ca);
                await _db.SaveChangesAsync();
                foreach (var n in nghiList) { n.CaId = ca.CaId; _db.CaLamViec_NghiGiaiLaos.Add(n); }
                AddLog(ca.CaId, "TAO", null, Snap(ca, nghiList), actorUserId);   // [CA-LOG]
                await _db.SaveChangesAsync();
                await trx.CommitAsync();
                return (true, "");
            }
            catch (Exception ex)
            {
                // [CA-LOG] rà độc lập T8: quay lại + bỏ các bản EF vừa thêm (DbContext sống cả phiên trang) — không thì lần lưu sau chèn lại dòng mồ côi
                await trx.RollbackAsync();
                foreach (var en in _db.ChangeTracker.Entries<CaLamViec>().Where(x => x.State == EntityState.Added || ReferenceEquals(x.Entity, ca)).ToList()) en.State = EntityState.Detached;
                foreach (var en in _db.ChangeTracker.Entries<CaLamViec_NghiGiaiLao>().Where(x => x.State == EntityState.Added).ToList()) en.State = EntityState.Detached;
                foreach (var en in _db.ChangeTracker.Entries<CaLamViecChangeLog>().Where(x => x.State == EntityState.Added).ToList()) en.State = EntityState.Detached;
                ca.CaId = 0;
                ca.NghiGiaiLaos.Clear();   // rà độc lập vòng 2: EF đã tự nối navigation ca ↔ giờ nghỉ — gỡ, không thì bấm Lưu lại chèn giờ nghỉ 2 lần
                foreach (var n in nghiList) { n.CaId = 0; n.NghiId = 0; n.Ca = null; }
                return (false, "Không lưu được ca mới: " + UiSafe.Text(ex, null, "lưu ca"));   // [C-11 2026-11-16]
            }
        }
        else
        {
            // [CA-SAVE-TRACK 2026-11-04] Trước: _db.CaLamViecs.Update(ca) với bản sao của trang. DbContext dùng chung cả phiên trang
            //   (Blazor) mà Ẩn / Hiện (FindAsync) đã giữ bản EF của ca này → bấm Ẩn rồi Sửa cùng ca = lỗi "đã có bản cùng khoá" (trang chết).
            //   Nay đọc dòng ca từ DB rồi chép ô của form; Bật / Tắt giữ theo DB (form không có ô này, chỉ nút Ẩn đổi).
            var cur = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.CaId == ca.CaId);
            if (cur == null) return (false, "Ca này không còn (có thể vừa bị xoá) — tải lại trang.");
            await _db.Entry(cur).ReloadAsync();   // bản EF đang giữ có thể cũ (người khác vừa sửa) → đọc lại để EF so đúng ô đổi
            var old = await _db.CaLamViec_NghiGiaiLaos.Where(x => x.CaId == ca.CaId).ToListAsync();
            var before = Snap(cur, old);   // [CA-LOG] bản trên DB trước khi chép ô của form
            cur.TenCa      = ca.TenCa;
            cur.GioBatDau  = ca.GioBatDau;
            cur.GioKetThuc = ca.GioKetThuc;
            cur.SortOrder  = ca.SortOrder;
            // Xóa nghỉ cũ, thêm mới
            _db.CaLamViec_NghiGiaiLaos.RemoveRange(old);
            foreach (var n in nghiList) { n.CaId = ca.CaId; n.NghiId = 0; _db.CaLamViec_NghiGiaiLaos.Add(n); }
            var after = Snap(cur, nghiList);
            if (after != before) AddLog(ca.CaId, "SUA", before, after, actorUserId);   // [CA-LOG] bấm Lưu mà không đổi gì → không ghi
            // [UI-SAFE 2026-11-16] (BM2-05) nhánh Sửa trước không try/catch: SaveChanges lỗi (DB tạm, chữ dài) → trx rollback nhưng bản EF còn
            //   Modified / Deleted / Added trong DbContext dùng chung → lần lưu sau ghi lại lệnh lỗi; ngoại lệ lên trang → trang chết. Nay: bắt + Detach như nhánh Tạo.
            try
            {
                await _db.SaveChangesAsync();
                await trx.CommitAsync();
                return (true, "");
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync();
                foreach (var en in _db.ChangeTracker.Entries<CaLamViec>().Where(x => x.State != EntityState.Unchanged).ToList()) en.State = EntityState.Detached;
                foreach (var en in _db.ChangeTracker.Entries<CaLamViec_NghiGiaiLao>().Where(x => x.State != EntityState.Unchanged).ToList()) en.State = EntityState.Detached;
                foreach (var en in _db.ChangeTracker.Entries<CaLamViecChangeLog>().Where(x => x.State != EntityState.Unchanged).ToList()) en.State = EntityState.Detached;
                foreach (var n in nghiList) { n.NghiId = 0; n.Ca = null; }
                return (false, UiSafe.Text(ex, null, "lưu ca"));
            }
        }
    }

    // ================================================================
    // [CA-LOG 2026-11-06] Lịch sử sửa ca (CaLamViecChangeLogs — SQL 13). Nguồn: HÙNG 05/11 15:37 (B5).
    //   JSON = tên, giờ bắt đầu / kết thúc (HH:mm), bật / tắt, thứ tự + giờ nghỉ (tăng dần) — đủ để biết phút làm WTS tính theo ca nào lúc nào.
    // ================================================================
    static string Snap(CaLamViec ca, IEnumerable<CaLamViec_NghiGiaiLao> nghi)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            ca.CaId, ca.TenCa,
            GioBatDau = ca.GioBatDau.ToString("HH:mm"), GioKetThuc = ca.GioKetThuc.ToString("HH:mm"),
            ca.IsActive, ca.SortOrder,
            Nghi = nghi.OrderBy(n => n.NghiBatDau).ThenBy(n => n.NghiKetThuc)
                       .Select(n => new { BatDau = n.NghiBatDau.ToString("HH:mm"), KetThuc = n.NghiKetThuc.ToString("HH:mm"), n.GhiChu }).ToList(),   // + ghi chú (rà độc lập T9)
        }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    void AddLog(int caId, string action, string? before, string? after, int by)
        => _db.CaLamViecChangeLogs.Add(new CaLamViecChangeLog
        {
            CaId = caId, Action = action, BeforeJson = before, AfterJson = after, ChangedBy = by, ChangedAt = DateTime.Now,
        });

    // [PHAT-HIEN-0311 2026-11-04] Xoá ca: kiểm quyền + CHẶN xoá ca đã được dùng (dòng WTS ghi CaId, hoặc giờ nghỉ đã chép
    //   WtsBreakWorkLogs) — chỉ cho Ẩn. Trước: xoá cứng luôn → WtsProductionLogs.CaId khoá ngoại làm SQL báo lỗi, trang chết;
    //   WtsBreakWorkLogs.CaId không khoá ngoại thì mất dấu ca của phút làm đã lưu. Ca chưa ai dùng vẫn xoá được như cũ.
    //   Nguồn: CLAUDE phát hiện 03/11, Hùng duyệt 04/11 08:24 ("chặn xoá ca đang dùng, chỉ cho Ẩn").
    public async Task<(bool Ok, string Error)> DeleteAsync(int id, int actorUserId)
    {
        if (!await PermCheck.CanEditAsync(_db, actorUserId, MES.Web.Constants.SystemPermissions.CaLamViec))
            return (false, PermCheck.NoEdit(TenMuc));
        var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.CaId == id);
        if (ca == null) return (false, "Ca này không còn (có thể vừa bị xoá) — tải lại trang.");
        int nWts = await _db.WtsProductionLogs.AsNoTracking().CountAsync(x => x.CaId == id);
        int nNghi = await _db.WtsBreakWorkLogs.AsNoTracking().CountAsync(x => x.CaId == id);
        if (nWts > 0 || nNghi > 0)
            return (false, $"Ca {ca.TenCa} đã được dùng ({nWts} dòng WTS"
                         + (nNghi > 0 ? $", {nNghi} dòng giờ nghỉ đã lưu" : "")
                         + ") — không xoá được để giữ lịch sử phút làm. Muốn thôi dùng ca này thì bấm Ẩn.");
        // [CA-LOG] chụp ca + giờ nghỉ trước khi xoá; xoá + log cùng 1 giao dịch (lỗi → quay lại cả 2)
        await _db.Entry(ca).ReloadAsync();   // [CA-LOG] rà độc lập T9: chụp bản trên DB (người khác có thể vừa sửa)
        var nghiDel = await _db.CaLamViec_NghiGiaiLaos.AsNoTracking().Where(x => x.CaId == id).ToListAsync();
        var trx = await _db.Database.BeginTransactionAsync();
        CaLamViecChangeLog? logDel = null;
        try
        {
            logDel = new CaLamViecChangeLog { CaId = id, Action = "XOA", BeforeJson = Snap(ca, nghiDel), AfterJson = null, ChangedBy = actorUserId, ChangedAt = DateTime.Now };
            _db.CaLamViecChangeLogs.Add(logDel);
            _db.CaLamViecs.Remove(ca);
            await _db.SaveChangesAsync();
            await trx.CommitAsync();
            await trx.DisposeAsync();
        }
        catch (Exception)
        {
            await trx.RollbackAsync();   // [CA-LOG]
            await trx.DisposeAsync();
            if (logDel != null) _db.Entry(logDel).State = EntityState.Detached;   // [CA-LOG] lần lưu sau trong cùng phiên trang không ghi lại dòng log này
            // thường là có dòng WTS ghi ca này đúng lúc đang xoá (khoá ngoại chặn) — bỏ thao tác xoá để lần lưu sau trong cùng phiên trang
            //   không xoá lại: trả ca VÀ các dòng nghỉ đang giữ (EF đánh dấu xoá theo ca — Cascade) về Unchanged (rà độc lập M1 04/11)
            _db.Entry(ca).State = EntityState.Unchanged;
            foreach (var en in _db.ChangeTracker.Entries<CaLamViec_NghiGiaiLao>().Where(x => x.State == EntityState.Deleted && x.Entity.CaId == id).ToList())
                en.State = EntityState.Unchanged;
            if (!await _db.CaLamViecs.AsNoTracking().AnyAsync(x => x.CaId == id))
                return (false, "Ca này vừa bị người khác xoá — tải lại trang.");
            return (false, $"Không xoá được ca {ca.TenCa} (có thể vừa có dòng WTS dùng ca này). Muốn thôi dùng ca này thì bấm Ẩn.");
        }
        return (true, "");
    }

    // [PHAT-HIEN-0311] trang gửi TRẠNG THÁI MUỐN (Bật / Tắt) thay vì "đảo": bản EF đang giữ có thể cũ (người khác vừa Ẩn / Hiện)
    //   → trước đảo theo bản cũ = bấm Hiện mà ca vẫn tắt (rà độc lập M3 04/11). Đọc lại dòng rồi gán.
    public async Task<(bool Ok, string Error)> ToggleActiveAsync(int id, bool active, int actorUserId)
    {
        if (!await PermCheck.CanEditAsync(_db, actorUserId, MES.Web.Constants.SystemPermissions.CaLamViec))   // [PHAT-HIEN-0311]
            return (false, PermCheck.NoEdit(TenMuc));
        var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.CaId == id);
        if (ca == null) return (false, "Ca này không còn (có thể vừa bị xoá) — tải lại trang.");
        await _db.Entry(ca).ReloadAsync();
        if (ca.IsActive == active) return (true, "");   // [CA-LOG] đã đúng trạng thái (bấm 2 lần / người khác vừa đổi) → không ghi log trùng
        var nghi = await _db.CaLamViec_NghiGiaiLaos.AsNoTracking().Where(x => x.CaId == id).ToListAsync();
        var before = Snap(ca, nghi);
        ca.IsActive = active;
        AddLog(id, active ? "HIEN" : "AN", before, Snap(ca, nghi), actorUserId);   // [CA-LOG] cùng lần lưu
        await _db.SaveChangesAsync();
        return (true, "");
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
