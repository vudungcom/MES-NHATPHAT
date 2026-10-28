using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [DS-LOI][VULOI 2026-10-28] DANH MỤC LỖI + VỤ LỖI
    //   - Danh mục lỗi (trang DS Lỗi): thêm / sửa / ngưng dùng / xoá (chỉ khi chưa dùng). Quyền DS_LOI (CanEdit), Admin luôn được.
    //   - Vụ lỗi = 1 lần PHÁT SINH lỗi. Bảng NgCases chỉ là NHÃN (loại lỗi, nơi phát hiện, nơi gây lỗi) — không lưu số (L6).
    //     Số của vụ do HandoverLedgerService tính từ chứng từ mang cột NgCaseId.
    //   - Vụ được tạo TRONG giao dịch của chứng từ sinh ra nó (cùng DbContext): lưu chứng từ không được thì vụ cũng không còn.
    // =====================================================================
    public static class NgOrigin
    {
        public const string Wts     = "WTS";      // dòng WTS có NG (nhóm làm hỏng)
        public const string Nhan    = "NHAN";     // NG đếm lúc nhận phiếu chưa mang vụ
        public const string NhanLoi = "NHANLOI";  // nhận phiếu chưa mang vụ vào ô Hàng lỗi / Hàng sửa
        public const string BaoLoi  = "BAOLOI";   // báo hàng lỗi phát hiện sau khi nhận
        public const string TraLoi  = "TRALOI";   // phiếu Hàng lỗi công đoạn trước (lập tay)

        public static string Label(string? o) => o switch
        {
            Wts => "WTS (NG làm hỏng)",
            Nhan => "NG đếm lúc nhận",
            NhanLoi => "Nhận vào ô Hàng lỗi",
            BaoLoi => "Báo lỗi sau khi nhận",
            TraLoi => "Hàng lỗi công đoạn trước",
            _ => o ?? ""
        };
    }

    public class NgCaseService
    {
        private readonly AppDbContext _db;
        public NgCaseService(AppDbContext db) => _db = db;

        public const string MsgPickCategory = "Chọn loại lỗi trong danh sách (DS Lỗi). Chưa có loại lỗi phù hợp thì nhờ người có quyền DS Lỗi thêm trước.";

        // ── Danh mục lỗi ─────────────────────────────────────────────────

        public async Task<List<NgCategory>> GetCategoriesAsync(bool activeOnly)
        {
            var q = _db.NgCategories.AsNoTracking();
            if (activeOnly) q = q.Where(x => x.IsActive);
            var list = await q.ToListAsync();
            return list.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToList();
        }

        /// <summary>Số vụ lỗi đã dùng mỗi loại lỗi (để biết xoá được hay chỉ ngưng dùng).</summary>
        public async Task<Dictionary<int, int>> GetUsageCountAsync()
        {
            var ids = await _db.NgCases.AsNoTracking().Select(x => x.NgCategoryId).ToListAsync();
            return ids.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        }

        public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

        /// <summary>Thêm (id null) hoặc sửa loại lỗi. Mã duy nhất, viết hoa, ≤ 20 ký tự; tên ≤ 200.</summary>
        public async Task<(bool Ok, string Error)> SaveCategoryAsync(int? id, string? code, string? name, string? description, int sortOrder, int userId)
        {
            var c = NormalizeCode(code);
            var n = (name ?? "").Trim();
            var d = string.IsNullOrWhiteSpace(description) ? null : description!.Trim();
            if (c.Length == 0) return (false, "Nhập mã lỗi.");
            if (c.Length > 20) return (false, "Mã lỗi tối đa 20 ký tự.");
            if (c.Contains('·') || c.Contains('|')) return (false, "Mã lỗi không dùng ký tự · hoặc |.");
            if (n.Length == 0) return (false, "Nhập tên lỗi.");
            if (n.Length > 200) return (false, "Tên lỗi tối đa 200 ký tự.");
            if (d != null && d.Length > 500) return (false, "Mô tả tối đa 500 ký tự.");
            var dup = await _db.NgCategories.AsNoTracking().AnyAsync(x => x.Code == c && (id == null || x.NgCategoryId != id));
            if (dup) return (false, $"Mã lỗi {c} đã có.");
            var now = DateTime.Now;
            if (id == null)
            {
                var e = new NgCategory { Code = c, Name = n, Description = d, SortOrder = sortOrder, IsActive = true, CreatedBy = userId, CreatedAt = now };
                _db.NgCategories.Add(e);
                await _db.SaveChangesAsync();
                _db.NgCategoryChangeLogs.Add(new NgCategoryChangeLog { NgCategoryId = e.NgCategoryId, Action = "ADD", NewValue = Snap(e), ChangedBy = userId, ChangedAt = now });
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                return (true, "");
            }
            var cur = await _db.NgCategories.FirstOrDefaultAsync(x => x.NgCategoryId == id.Value);
            if (cur == null) return (false, "Loại lỗi không còn.");
            var old = Snap(cur);
            // Đổi mã khi đã dùng: chữ cũ trên chứng từ (cột NgReason) vẫn là mã cũ — cho đổi tên / mô tả / thứ tự, KHÔNG cho đổi mã.
            if (cur.Code != c && await _db.NgCases.AsNoTracking().AnyAsync(x => x.NgCategoryId == cur.NgCategoryId))
                return (false, $"Loại lỗi {cur.Code} đã dùng trong vụ lỗi — không đổi mã được (đổi tên / mô tả thì được).");
            cur.Code = c; cur.Name = n; cur.Description = d; cur.SortOrder = sortOrder;
            cur.UpdatedBy = userId; cur.UpdatedAt = now;
            _db.NgCategoryChangeLogs.Add(new NgCategoryChangeLog { NgCategoryId = cur.NgCategoryId, Action = "EDIT", OldValue = old, NewValue = Snap(cur), ChangedBy = userId, ChangedAt = now });
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            return (true, "");
        }

        /// <summary>Ngưng dùng / dùng lại (ẩn / hiện trong danh sách chọn).</summary>
        public async Task<(bool Ok, string Error)> SetActiveAsync(int id, bool active, int userId)
        {
            var cur = await _db.NgCategories.FirstOrDefaultAsync(x => x.NgCategoryId == id);
            if (cur == null) return (false, "Loại lỗi không còn.");
            if (cur.IsActive == active) return (true, "");
            cur.IsActive = active; cur.UpdatedBy = userId; cur.UpdatedAt = DateTime.Now;
            _db.NgCategoryChangeLogs.Add(new NgCategoryChangeLog { NgCategoryId = id, Action = active ? "ON" : "OFF", NewValue = Snap(cur), ChangedBy = userId, ChangedAt = DateTime.Now });
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            return (true, "");
        }

        /// <summary>Xoá hẳn — chỉ khi chưa dùng trong vụ lỗi nào (đã dùng thì Ngưng dùng).</summary>
        public async Task<(bool Ok, string Error)> DeleteCategoryAsync(int id, int userId)
        {
            var cur = await _db.NgCategories.FirstOrDefaultAsync(x => x.NgCategoryId == id);
            if (cur == null) return (false, "Loại lỗi không còn.");
            if (await _db.NgCases.AsNoTracking().AnyAsync(x => x.NgCategoryId == id))
                return (false, $"Loại lỗi {cur.Code} đã dùng trong vụ lỗi — không xoá được, chỉ Ngưng dùng.");
            _db.NgCategoryChangeLogs.Add(new NgCategoryChangeLog { NgCategoryId = id, Action = "DEL", OldValue = Snap(cur), ChangedBy = userId, ChangedAt = DateTime.Now });
            _db.NgCategories.Remove(cur);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            return (true, "");
        }

        /// <summary>Lịch sử sửa 1 loại lỗi (mới nhất trước) kèm tên người sửa.</summary>
        public async Task<List<(NgCategoryChangeLog Log, string Who)>> GetChangeLogsAsync(int id)
        {
            var logs = await _db.NgCategoryChangeLogs.AsNoTracking().Where(x => x.NgCategoryId == id).ToListAsync();
            var uids = logs.Select(x => x.ChangedBy).Distinct().ToList();
            var users = uids.Any()
                ? await _db.Users.AsNoTracking().Where(u => uids.Contains(u.UserId)).Select(u => new { u.UserId, Name = u.FullName ?? u.Username }).ToListAsync()
                : new();
            return logs.OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.ChangeId)
                       .Select(x => (x, users.FirstOrDefault(u => u.UserId == x.ChangedBy)?.Name ?? "")).ToList();
        }

        static string Snap(NgCategory e) => $"{e.Code} | {e.Name} | {e.Description} | thứ tự {e.SortOrder} | {(e.IsActive ? "đang dùng" : "ngưng dùng")}";

        // ── Vụ lỗi ───────────────────────────────────────────────────────

        /// <summary>Loại lỗi đang dùng (null nếu không có / đã ngưng).</summary>
        public static async Task<NgCategory?> GetActiveCategoryAsync(AppDbContext db, int? id)
        {
            if (id == null) return null;
            return await db.NgCategories.AsNoTracking().FirstOrDefaultAsync(x => x.NgCategoryId == id.Value && x.IsActive);
        }

        /// <summary>Tạo vụ lỗi mới (gọi TRONG giao dịch của chứng từ sinh ra nó). Trả về vụ đã có Id.</summary>
        public static async Task<NgCase> CreateCaseAsync(AppDbContext db, int khPlanDetailId, NgCategory cat, string origin,
            string detectGroup, string? detectNc, string? causeGroup, int userId)
        {
            var e = new NgCase
            {
                KhPlanDetailId = khPlanDetailId, NgCategoryId = cat.NgCategoryId, OriginKind = origin,
                DetectGroupCode = detectGroup, DetectNC = string.IsNullOrWhiteSpace(detectNc) ? null : detectNc!.Trim(),
                CauseGroupCode = string.IsNullOrWhiteSpace(causeGroup) ? null : causeGroup,
                CreatedBy = userId, CreatedAt = DateTime.Now,
            };
            db.NgCases.Add(e);
            await db.SaveChangesAsync();
            return e;
        }

        /// <summary>Vụ lỗi có thuộc PO này không (chống chọn nhầm vụ PO khác).</summary>
        public static async Task<NgCase?> GetCaseAsync(AppDbContext db, long id)
            => await db.NgCases.AsNoTracking().FirstOrDefaultAsync(x => x.NgCaseId == id);

        /// <summary>
        /// [VULOI] Sửa dòng WTS có NG: dòng mới giữ vụ cũ. Đổi loại lỗi chỉ được khi vụ chưa đi phiếu nào (chưa ai nhận / chuyển tiếp).
        /// Trả lời báo nếu không được đổi; null = được (đã đổi nếu khác).
        /// </summary>
        public static async Task<string?> ChangeCaseCategoryAsync(AppDbContext db, long caseId, int newCategoryId)
        {
            var blk = await CategoryChangeBlockedAsync(db, caseId, newCategoryId);
            if (blk != null) return blk;
            var cs = await db.NgCases.FirstOrDefaultAsync(x => x.NgCaseId == caseId);
            if (cs == null || cs.NgCategoryId == newCategoryId) return null;
            cs.NgCategoryId = newCategoryId;
            await db.SaveChangesAsync();
            return null;
        }

        /// <summary>[VULOI] Chỉ kiểm (không ghi): đổi loại lỗi của vụ có được không. Null = được / không đổi.</summary>
        public static async Task<string?> CategoryChangeBlockedAsync(AppDbContext db, long caseId, int newCategoryId)
        {
            var cs = await db.NgCases.AsNoTracking().FirstOrDefaultAsync(x => x.NgCaseId == caseId);
            if (cs == null || cs.NgCategoryId == newCategoryId) return null;
            var moved = await db.HandoverTransactions.AsNoTracking().AnyAsync(t => t.NgCaseId == caseId && !t.IsVoided);
            return moved ? $"{NgCaseNo.Format(caseId)} đã giao / huỷ / trả khách — không đổi loại lỗi được (huỷ các phiếu đó trước, hoặc giữ loại lỗi cũ)." : null;
        }

        /// <summary>[VULOI] Loại lỗi dùng khi lưu: phải đang dùng; riêng vụ cũ giữ nguyên loại lỗi thì loại đã ngưng vẫn giữ được.</summary>
        public static async Task<NgCategory?> GetCategoryForCaseAsync(AppDbContext db, int? categoryId, long? keepCaseId)
        {
            if (categoryId == null) return null;
            if (keepCaseId != null)
            {
                var cs = await db.NgCases.AsNoTracking().FirstOrDefaultAsync(x => x.NgCaseId == keepCaseId.Value);
                if (cs != null && cs.NgCategoryId == categoryId)
                    return await db.NgCategories.AsNoTracking().FirstOrDefaultAsync(x => x.NgCategoryId == categoryId.Value);
            }
            return await GetActiveCategoryAsync(db, categoryId);
        }
    }
}
