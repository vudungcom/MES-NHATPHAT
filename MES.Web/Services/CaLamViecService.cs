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

        foreach (var n in nghiList)
        {
            if (n.NghiKetThuc <= n.NghiBatDau)
                return (false, $"Nghỉ giải lao: giờ kết thúc phải sau giờ bắt đầu ({n.NghiBatDau:HH\\:mm}).");
        }

        // [CA-NGHI-OVERLAP 2026-11-01] 2 khoảng nghỉ chồng nhau → trước: trang WTS trừ phần chồng 2 lần (phút làm âm / thiếu).
        //   Nay chặn lưu ca (WorkMinutes.BreakSegments vẫn gộp khoảng chồng của dữ liệu ca cũ cho an toàn).
        var _sorted = nghiList.OrderBy(x => x.NghiBatDau).ToList();
        for (int i = 1; i < _sorted.Count; i++)
            if (_sorted[i].NghiBatDau < _sorted[i - 1].NghiKetThuc)
                return (false, $"Nghỉ giải lao {_sorted[i - 1].NghiBatDau:HH\\:mm}–{_sorted[i - 1].NghiKetThuc:HH\\:mm} và {_sorted[i].NghiBatDau:HH\\:mm}–{_sorted[i].NghiKetThuc:HH\\:mm} chồng nhau — sửa cho tách rời.");

        if (ca.CaId == 0)
        {
            _db.CaLamViecs.Add(ca);
            await _db.SaveChangesAsync();
            foreach (var n in nghiList) { n.CaId = ca.CaId; _db.CaLamViec_NghiGiaiLaos.Add(n); }
        }
        else
        {
            // [CA-SAVE-TRACK 2026-11-04] Trước: _db.CaLamViecs.Update(ca) với bản sao của trang. DbContext dùng chung cả phiên trang
            //   (Blazor) mà Ẩn / Hiện (FindAsync) đã giữ bản EF của ca này → bấm Ẩn rồi Sửa cùng ca = lỗi "đã có bản cùng khoá" (trang chết).
            //   Nay đọc dòng ca từ DB rồi chép ô của form; Bật / Tắt giữ theo DB (form không có ô này, chỉ nút Ẩn đổi).
            var cur = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.CaId == ca.CaId);
            if (cur == null) return (false, "Ca này không còn (có thể vừa bị xoá) — tải lại trang.");
            await _db.Entry(cur).ReloadAsync();   // bản EF đang giữ có thể cũ (người khác vừa sửa) → đọc lại để EF so đúng ô đổi
            cur.TenCa      = ca.TenCa;
            cur.GioBatDau  = ca.GioBatDau;
            cur.GioKetThuc = ca.GioKetThuc;
            cur.SortOrder  = ca.SortOrder;
            // Xóa nghỉ cũ, thêm mới
            var old = await _db.CaLamViec_NghiGiaiLaos.Where(x => x.CaId == ca.CaId).ToListAsync();
            _db.CaLamViec_NghiGiaiLaos.RemoveRange(old);
            foreach (var n in nghiList) { n.CaId = ca.CaId; n.NghiId = 0; _db.CaLamViec_NghiGiaiLaos.Add(n); }
        }

        await _db.SaveChangesAsync();
        return (true, "");
    }

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
        try
        {
            _db.CaLamViecs.Remove(ca);
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
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
        ca.IsActive = active;
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
