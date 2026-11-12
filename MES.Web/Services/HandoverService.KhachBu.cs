using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [KHACH-GUI-LAI 2026-11-11] KHÁCH GỬI BÙ PHÔI (Gói 14)
    //   Nguồn: KHÁCH 06/11 14:12 (hàng Trả khách / Huỷ khách gửi lại) → vòng 1 câu 1a A: phôi bù vào Kho ô Hàng lỗi đã xử lý (= phôi còn),
    //          gắn lần lỗi cũ, giao Gia công vào NC đầu như phôi thường; câu 1b A: chặn bù vượt, khách gửi dư thì sửa Số lượng thực nhận.
    //          HÙNG 11/11 14:39 duyệt thiết kế + SQL 15_KHBU (bảng HandoverReplacements), trần bù chặn CẢ khi PO tắt Workflow.
    //   Trước: không có chứng từ bù — cách tạm là tăng Số lượng thực nhận của phôi (không biết bù cho lần lỗi nào, cân PO không thấy bù).
    //   Số: chỉ sổ chung HandoverLedgerService tính (Kho "Khách gửi bù" trong Nhận · Hàng lỗi đã xử lý, cân PO phôi + bù, LedgerCase.DaBu).
    //   Nhận / huỷ: quyền Sửa nhóm Kho đọc ma trận DB (huỷ: người nhập hoặc quyền Sửa Kho), khoá PO MES_PO_ + K5 cùng giao dịch,
    //   tự thử lại khi deadlock, giờ máy chủ, chỉ ghi thêm (huỷ = IsVoided + người / giờ / lý do).
    // =====================================================================

    /// <summary>[KHACH-GUI-LAI] 1 phiếu khách gửi bù cho Lịch sử phiếu.</summary>
    public class HandoverReplacementItem
    {
        public long      Id             { get; set; }
        public int       KhPlanDetailId { get; set; }
        public long      NgCaseId       { get; set; }
        public decimal   Qty            { get; set; }
        public string?   Notes          { get; set; }
        public int       ReceivedBy     { get; set; }
        public string    ReceivedByName { get; set; } = string.Empty;
        public DateTime  ReceivedAt     { get; set; }
        public bool      IsVoided       { get; set; }
        public string?   VoidReason     { get; set; }
        public string    VoidedByName   { get; set; } = string.Empty;
        public DateTime? VoidedAt       { get; set; }
    }

    public partial class HandoverService
    {
        /// <summary>[KHACH-GUI-LAI] Nhóm nhận phôi khách gửi bù.</summary>
        public const string BuGroup = "KHO";

        /// <summary>[KHACH-GUI-LAI] Huỷ phiếu bù: người nhập, hoặc người có quyền Sửa nhóm Kho.</summary>
        public static bool CanVoidBu(int receivedBy, int userId, IReadOnlyCollection<string> editGroups)
            => receivedBy == userId || editGroups.Contains(BuGroup);

        /// <summary>[KHACH-GUI-LAI] Lời chặn nhập bù vượt trần (dùng chung máy chủ + form).</summary>
        public static string BuOverMsg(long ngCaseId, decimal max, string text, decimal qty)
            => $"{NgCaseNo.Format(ngCaseId)}: khách gửi bù tối đa {max:0.##} ({text}) — không nhận {qty:0.##}. "
             + "Khách gửi dư thì sửa Số lượng thực nhận của phôi ở trang Kho.";

        // ----------------------------------------------------------------
        // Kho nhận phôi khách gửi bù
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> ReceiveReplacementAsync(int khPlanDetailId, long ngCaseId, decimal qty, string? notes, int userId)
            => RetryDeadlockAsync(() => ReceiveReplacementOnceAsync(khPlanDetailId, ngCaseId, qty, notes, userId));

        private async Task<(bool Ok, string Error)> ReceiveReplacementOnceAsync(int khPlanDetailId, long ngCaseId, decimal qty, string? notes, int userId)
        {
            if (qty <= 0) return (false, "Nhập số phôi khách gửi bù (lớn hơn 0).");
            if (QtyRule.CheckWhole(qty) is string eWhole) return (false, eWhole);
            if (ngCaseId <= 0) return (false, $"Chọn {NgCaseNo.Term} được bù (lần lỗi đã trả khách / huỷ).");
            var deny = await DenyGroupAsync(userId, BuGroup, "nhận phôi khách gửi bù");
            if (deny != null) return (false, deny);
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (notes != null && notes.Length > 500) notes = notes[..500];

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(khPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var row = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return (false, "Không tìm thấy PO.");
            // Trần đọc lại dưới khoá (người khác có thể vừa nhận bù / huỷ phiếu trả khách). Chặn cả Workflow OFF (Hùng 11/11 14:39).
            var (max, text) = HandoverLedgerService.BuCap(row, ngCaseId);
            if (qty > max) return (false, BuOverMsg(ngCaseId, max, text, qty));

            _db.HandoverReplacements.Add(new HandoverReplacement
            {
                KhPlanDetailId = khPlanDetailId,
                NgCaseId       = ngCaseId,
                Qty            = qty,
                ReceivedBy     = userId,
                ReceivedAt     = DateTime.Now,   // giờ máy chủ
                Notes          = notes,
            });
            await _db.SaveChangesAsync();
            var k8 = await CheckNoNewNegativeAsync(khPlanDetailId, row);   // [GN-K8][K5] (thêm phôi không làm ô nào âm — kiểm như mọi lần lưu)
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE]
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // Huỷ phiếu bù (phôi bù đã giao đi thì K5 chặn — như huỷ phôi / phiếu thường)
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> VoidReplacementAsync(long replacementId, string voidReason, int userId)
            => RetryDeadlockAsync(() => VoidReplacementOnceAsync(replacementId, voidReason, userId));

        private async Task<(bool Ok, string Error)> VoidReplacementOnceAsync(long replacementId, string voidReason, int userId)
        {
            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)
                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");
            var head = await _db.HandoverReplacements.AsNoTracking().Where(x => x.ReplacementId == replacementId && !x.IsVoided)
                .Select(x => new { x.KhPlanDetailId, x.ReceivedBy }).FirstOrDefaultAsync();
            if (head == null) return (false, "Phiếu khách gửi bù không tồn tại hoặc đã huỷ.");
            var rights = await VoidRightsAsync(userId);
            if (rights == null) return (false, VoidInactiveMsg);
            if (!CanVoidBu(head.ReceivedBy, userId, rights))
                return (false, "Chỉ người đã nhập hoặc nhóm Kho mới huỷ được phiếu khách gửi bù này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét cả theo thời gian
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);
            var bu = await _db.HandoverReplacements.FirstOrDefaultAsync(x => x.ReplacementId == replacementId && !x.IsVoided);
            if (bu == null) return (false, "Phiếu khách gửi bù không tồn tại hoặc đã huỷ.");
            bu.IsVoided   = true;
            bu.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();
            bu.VoidedBy   = userId;
            bu.VoidedAt   = DateTime.Now;
            await _db.SaveChangesAsync();
            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before, beforeIn);   // [K5] phôi bù đã giao Gia công thì không huỷ được
                if (k5 != null)
                {
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch ngay trước khi tìm gốc rễ
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidBu(replacementId),
                        new HashSet<string> { "U:" + replacementId }, new[] { $"Huỷ phiếu khách gửi bù #{replacementId}" }, loopOp: true);   // huỷ chứng từ nguồn: cả theo thời gian (như K5 ở trên)
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        /// <summary>[KHACH-GUI-LAI] Lời chặn huỷ phiếu Trả khách / Huỷ làm số đã bù của vụ vượt (trả khách + huỷ). null = được. Chặn cả Workflow OFF.</summary>
        internal static string? BuVoidOutBlock(LedgerRow? before, long? ngCaseId, decimal issued, long txId)
        {
            if (before == null || ngCaseId is not long cid || cid <= 0 || !before.Cases.TryGetValue(cid, out var c) || c.DaBu <= 0) return null;
            var left = c.TraKhach + c.Huy - issued;
            if (c.DaBu <= left) return null;
            return $"Không huỷ được phiếu #{txId}: khách đã gửi bù {c.DaBu:0.##} cho {NgCaseNo.Format(cid)} "
                 + $"(phiếu bù {string.Join(", ", c.BuDocs.Select(b => "#" + b.Id))}); huỷ phiếu này thì trả khách + huỷ còn {left:0.##} < số đã bù. "
                 + "Huỷ phiếu khách gửi bù trước (Lịch sử phiếu) rồi thử lại.";
        }

        // ----------------------------------------------------------------
        // Đọc cho Lịch sử phiếu
        // ----------------------------------------------------------------
        public async Task<List<HandoverReplacementItem>> GetReplacementsAsync(int khPlanDetailId)
        {
            var rows = await _db.HandoverReplacements.AsNoTracking().Where(x => x.KhPlanDetailId == khPlanDetailId)
                .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.ReplacementId).ToListAsync();
            var uids = rows.Select(r => r.ReceivedBy).Concat(rows.Where(r => r.VoidedBy != null).Select(r => r.VoidedBy!.Value)).Distinct().ToList();
            var names = uids.Any()
                ? await _db.Users.AsNoTracking().Where(u => uids.Contains(u.UserId)).Select(u => new { u.UserId, Name = u.FullName ?? u.Username }).ToListAsync()
                : new();
            string N(int? id) => id == null ? "" : names.FirstOrDefault(n => n.UserId == id)?.Name ?? "";
            return rows.Select(r => new HandoverReplacementItem
            {
                Id = r.ReplacementId, KhPlanDetailId = r.KhPlanDetailId, NgCaseId = r.NgCaseId, Qty = r.Qty, Notes = r.Notes,
                ReceivedBy = r.ReceivedBy, ReceivedByName = N(r.ReceivedBy), ReceivedAt = r.ReceivedAt,
                IsVoided = r.IsVoided, VoidReason = r.VoidReason, VoidedByName = N(r.VoidedBy), VoidedAt = r.VoidedAt,
            }).ToList();
        }
    }
}
