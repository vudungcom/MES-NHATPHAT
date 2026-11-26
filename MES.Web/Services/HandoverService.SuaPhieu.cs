using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [N22-SUA 2026-11-25] SỬA CHỨNG TỪ NHẬP NHẦM (Gói G-B, luật N22)
    //   Nguồn: KHÁCH 23/11 (mockup D7 bước 1 / 2a / 2b / 3 — tích Đúng 3 ý; ví dụ chat 16:19–16:20 "Kho giao GC 30, nhận 25 OK + 5 NG
    //          → 24 OK + 6 NG khi GC chưa WTS NC1; đã WTS thì giao ngược báo lỗi Kho 1 chiếc"); HÙNG 23/11 16:24 (ghi N22) + 25/11 21:39 OK
    //          bản "em hiểu" (3 nhóm: khách tích / Claude tự chốt / làm bên trong, Lịch sử gộp 2 dòng thành 1 "24 OK + 6 NG").
    //   Chi tiết Claude tự chốt (khách chưa tích — ghi project doc claude/tu-chot-bao-khach.md): NG thêm = phiếu trả MỚI cùng vụ (không sửa
    //          phiếu trả cũ); giảm NG khi phiếu trả đã được nhận → chặn; bên nhận đã lập phiếu giao / báo lỗi / Kết quả sửa sau lần nhận → chặn
    //          như đã WTS; Workflow OFF chỉ cảnh báo phần "đã dùng"; quyền như luật huỷ.
    //   Cách làm: KHÔNG sửa đè chứng từ — huỷ (IsVoided + người / giờ / lý do) + lập lại trong 1 giao dịch, khoá PO MES_PO_ + K5 (cả theo thời gian),
    //          số chỉ do sổ chung HandoverLedgerService tính lại. Không SQL.
    //   - Sửa phiếu (bên giao): phiếu chưa có lần nhận → huỷ + lập lại qua đúng hàm lập phiếu (mọi trần / tuyến / luật ON-OFF như lập mới).
    //   - Sửa lần nhận (bên nhận): huỷ lần nhận (cả nhóm dòng [N22#gốc]) + ghi lần nhận mới, GIỮ người nhận + giờ nhận gốc (như sửa WTS giữ
    //          người làm + ngày — L7). Mỗi lần nhận vẫn tối đa 1 phiếu trả tự lập (sổ / huỷ dây chuyền / GN-WHY dựa vào 1–1): NG tăng mà phiếu trả
    //          cũ giữ nguyên → tách 2 dòng cùng dấu [N22#gốc] (dòng 1: OK + NG cũ, nối phiếu trả cũ; dòng 2: NG thêm + phiếu trả mới cùng vụ);
    //          Lịch sử gộp nhóm thành 1 dòng; huỷ 1 dòng = huỷ cả nhóm (VoidReceiveOnceAsync).
    //   - Đổi loại lỗi: NgCaseService.CategoryChangeBlockedAsync luật mới (chưa giao / giao chưa nhận) + ghi KhPlanDetailChangeLogs (lịch sử PO).
    // =====================================================================

    /// <summary>[N22-SUA] Thông tin cho nút Sửa / nút xám ở Lịch sử phiếu (chỉ đọc).</summary>
    public class HandoverEditInfo
    {
        public bool WorkflowOn { get; set; }
        /// <summary>ReceiveId → chữ "bên nhận đã dùng sau lần nhận" (WTS / phiếu giao / báo lỗi / Kết quả sửa). Không có = chưa dùng.</summary>
        public Dictionary<long, string> RecvUsed { get; set; } = new();
        /// <summary>NgCaseId → lý do không đổi loại lỗi được (null / không có = đổi được).</summary>
        public Dictionary<long, string> CaseCatBlock { get; set; } = new();
        /// <summary>Lịch sử đổi loại lỗi (mới nhất trước).</summary>
        public List<HandoverCaseCatLog> CatLogs { get; set; } = new();
        /// <summary>NgCaseId → (người tạo, nhóm phát hiện, loại lỗi hiện tại) — để ẩn / hiện nút Đổi loại lỗi.</summary>
        public Dictionary<long, (int CreatedBy, string DetectGroup, int CategoryId)> Cases { get; set; } = new();
        /// <summary>NgCaseId → nhãn loại lỗi hiện tại ("MÃ · Tên", cả loại đã ngưng dùng).</summary>
        public Dictionary<long, string> CaseCatLabel { get; set; } = new();
        /// <summary>NC nhận chọn được khi phiếu vào Gia công (NC thường trong quy trình PO).</summary>
        public List<string> GcNcs { get; set; } = new();
    }

    public class HandoverCaseCatLog
    {
        public long     CaseId  { get; set; }
        public string   Old     { get; set; } = "";
        public string   New     { get; set; } = "";
        public string?  Reason  { get; set; }
        public string   ByName  { get; set; } = "";
        public DateTime At      { get; set; }
    }

    public partial class HandoverService
    {
        /// <summary>[N22-SUA] Dấu nhóm lần nhận đã sửa ở đầu Ghi chú: "[N22#&lt;mã lần nhận gốc&gt;]". Chỉ máy chủ ghi (ReceiveOnceAsync bỏ dấu khỏi ghi chú người gõ).</summary>
        internal const string N22Tag = "[N22#";
        static readonly Regex N22Rx = new(@"^\[N22#(\d+)\]", RegexOptions.Compiled);
        public static long? N22Root(string? notes)
            => notes != null && N22Rx.Match(notes) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var v) ? v : null;
        /// <summary>[N22-SUA] Ghi chú người gõ không được giả dấu nhóm: mọi chữ "[N22#" (kể cả gõ 2 lần liền) đổi thành "[N22 #" — N22Root không nhận ra
        ///   (rà độc lập G-B T1: trước chỉ bỏ 1 dấu đầu chuỗi → gõ "[N22#5][N22#5] x" vẫn giả được nhóm).</summary>
        internal static string? StripN22(string? notes)
        {
            if (notes == null) return null;
            var t = notes.Trim();
            while (t.Contains(N22Tag)) t = t.Replace(N22Tag, "[N22 #");
            return t.Length > 0 ? t : null;
        }
        /// <summary>[N22-SUA] Phần ghi chú người gõ của 1 lần nhận (dòng đã sửa: sau " | " của dòng máy chủ ghi) — không lồng chữ "Sửa lần nhận …" qua nhiều lần sửa.</summary>
        static string? UserNoteOf(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return null;
            if (N22Root(notes) == null) return StripN22(notes);
            var i = notes.IndexOf(" | ", StringComparison.Ordinal);
            return i < 0 ? null : StripN22(notes[(i + 3)..]);
        }
        static readonly Regex SuaTuRx = new(@"(\s·\s)?sửa từ phiếu #\d+", RegexOptions.Compiled);

        static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

        public const string N22ReasonMsg = "Nhập lý do sửa (ít nhất 3 ký tự).";
        public const string N22NoChangeMsg = "Không có gì thay đổi.";

        // ─────────────────────────────────────────────────────────────
        // 1) SỬA PHIẾU (bên giao) — D7 bước 1
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// [N22-SUA] Sửa phiếu chưa nhận: bộ phận nhận, số lượng, loại lỗi (vụ của phiếu), NC nhận (vào Gia công), ghi chú.
        /// = huỷ phiếu cũ + lập phiếu mới ("sửa từ phiếu #…") trong 1 giao dịch, cùng mã vụ. Trả (Ok, lỗi | cảnh báo khi Ok).
        /// </summary>
        public Task<(bool Ok, string Error)> EditIssueAsync(long handoverTxId, string toGroupCode, decimal qty, int? ngCategoryId,
            string? toNC, string? notes, string reason, int userId)
            => RetryDeadlockAsync(() => EditIssueOnceAsync(handoverTxId, toGroupCode, qty, ngCategoryId, toNC, notes, reason, userId));

        private async Task<(bool Ok, string Error)> EditIssueOnceAsync(long handoverTxId, string toGroupCode, decimal qty, int? ngCategoryId,
            string? toNC, string? notes, string reason, int userId)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length < 3) return (false, N22ReasonMsg);
            if (LenCheck.Over(reason, 150, "Lý do sửa") is string lenErr) return (false, lenErr);
            notes = StripN22(string.IsNullOrWhiteSpace(notes) ? null : SuaTuRx.Replace(notes, "").Trim());   // [rà độc lập L10] không lồng "sửa từ phiếu #…"
            if (notes == "") notes = null;
            toNC = HandoverLedgerService.TrimCode(toNC);
            var head = await _db.HandoverTransactions.AsNoTracking().Where(t => t.HandoverTxId == handoverTxId && !t.IsVoided)
                .Select(t => new { t.KhPlanDetailId, t.IssuedBy, t.FromGroupCode }).FirstOrDefaultAsync();
            if (head == null) return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            var eg = await VoidRightsAsync(userId);
            if (eg == null) return (false, VoidInactiveMsg);
            if (!CanVoidIssue(head.IssuedBy, head.FromGroupCode, userId, eg))
                return (false, $"Chỉ người lập phiếu hoặc nhóm {HandoverLedgerService.GroupLabel(head.FromGroupCode)} mới sửa được phiếu này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét cả theo thời gian
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);
            if (before == null) return (false, "Không tìm thấy PO.");

            var tx = await _db.HandoverTransactions.Include(t => t.Receives)
                .FirstOrDefaultAsync(t => t.HandoverTxId == handoverTxId && !t.IsVoided);
            if (tx == null) return (false, "Phiếu giao không tồn tại hoặc đã bị huỷ.");
            if (EditIssueBlock(tx.Purpose, tx.SourceReceiveId, tx.ToCell, tx.Status, tx.Receives.Any(r => !r.IsVoided), tx.QtyClosed) is string blk)
                return (false, blk);
            toGroupCode = (toGroupCode ?? "").Trim().ToUpperInvariant();
            bool sameTo = toGroupCode == tx.ToGroupCode;
            if (sameTo && toNC == null) toNC = HandoverLedgerService.TrimCode(tx.ToNC);
            NgCase? cs = tx.NgCaseId is long cid ? await _db.NgCases.FirstOrDefaultAsync(x => x.NgCaseId == cid) : null;
            bool catChange = ngCategoryId != null && cs != null && cs.NgCategoryId != ngCategoryId;
            if (sameTo && qty == tx.QtyIssued && !catChange && string.Equals(toNC ?? "", HandoverLedgerService.TrimCode(tx.ToNC) ?? "", StringComparison.OrdinalIgnoreCase)
                && (notes ?? "") == (StripN22(SuaTuRx.Replace(tx.Notes ?? "", "").Trim()) ?? ""))
                return (false, N22NoChangeMsg);
            if (notes != null && notes.Length > 450 && notes != StripN22(SuaTuRx.Replace(tx.Notes ?? "", "").Trim()))
                return (false, LenCheck.Over(notes, 450, "Ghi chú")!);

            // [rà độc lập G-B T2] đổi loại lỗi qua Sửa phiếu: cùng quyền nút Đổi loại lỗi (người tạo vụ / quyền Sửa nhóm phát hiện) — trước chỉ quyền huỷ phiếu
            if (catChange && cs!.CreatedBy != userId && !eg.Contains(cs.DetectGroupCode))
                return (false, $"Chỉ người tạo {NgCaseNo.Format(cs.NgCaseId)} hoặc nhóm {HandoverLedgerService.GroupLabel(cs.DetectGroupCode)} (nơi phát hiện) mới đổi loại lỗi được — sửa phiếu giữ loại lỗi cũ.");

            // ① huỷ phiếu cũ (ghi trước để sổ lập phiếu mới tính trần như chưa có phiếu cũ)
            tx.IsVoided   = true;
            tx.VoidReason = Cut("Sửa phiếu: " + reason, 200);
            tx.VoidedBy   = userId;
            tx.VoidedAt   = DateTime.Now;
            await _db.SaveChangesAsync();

            // ② loại lỗi của vụ (cùng mã vụ) — luật đổi loại lỗi N22 (chưa giao / giao chưa nhận); Workflow OFF chỉ cảnh báo
            var warns = new List<string>();
            if (catChange)
            {
                var cat = await NgCaseService.GetActiveCategoryAsync(_db, ngCategoryId);
                if (cat == null) { _db.ChangeTracker.Clear(); return (false, NgCaseService.MsgPickCategory); }
                var oldLbl = await CatLabelAsync(cs!.NgCategoryId);
                var cErr = await NgCaseService.ChangeCaseCategoryAsync(_db, cs.NgCaseId, cat.NgCategoryId, before.ApplyWorkflow ? null : warns);
                if (cErr != null) { _db.ChangeTracker.Clear(); return (false, cErr); }
                LogCatChange(tx.KhPlanDetailId, cs.NgCaseId, oldLbl, cat.Label, $"Sửa phiếu #{tx.HandoverTxId}: {reason}", userId);
            }
            // ③ phiếu Hàng lỗi công đoạn trước: vụ sinh cùng phiếu → nơi gây lỗi đổi theo bộ phận nhận (vụ mang theo từ báo lỗi: giữ nơi gây lỗi)
            bool ngRet = tx.Purpose == HandoverPurpose.NgReturn;
            if (ngRet && cs != null && !sameTo && cs.OriginKind == NgOrigin.TraLoi && cs.CauseGroupCode == tx.ToGroupCode)
                cs.CauseGroupCode = toGroupCode;
            await _db.SaveChangesAsync();

            // ④ lập phiếu mới qua đúng hàm lập phiếu (trần / tuyến / luật ON-OFF như lập mới), cùng giao dịch + khoá
            var ftx = new FormTx();
            var newNotes = Cut((notes == null ? "" : notes + " · ") + $"sửa từ phiếu #{tx.HandoverTxId}", 500);
            var (ok, err) = await IssueWithPurposeOnceAsync(tx.KhPlanDetailId, tx.FromGroupCode, toGroupCode, qty, userId, tx.Purpose!,
                tx.FromNC, toNC, newNotes, tx.NgReason, tx.FromCell,
                // [N22-SUA 2026-11-26] phiếu "Trả lỗi hàng làm hộ" (Trả về theo NC từ ô Hàng lỗi): ô nhận do máy chủ tự đặt (TARO-LAMHO-NC) — không truyền lại
                //   (hàm lập phiếu chỉ nhận ô chỉ định ở phiếu Hàng mới / hàng lỗi / sửa → "Ô nhận không hợp lệ" oan). Nguồn: CLAUDE (trạm kiem 26/11 08:39 chuoi_ktt seed 28794 PO8).
                HandoverCell.IsLamHoLoi(tx.Purpose, tx.FromCell) ? null : tx.ToCell,
                ngRet ? (ngCategoryId ?? cs?.NgCategoryId) : null,
                ngRet ? null : tx.NgCaseId, ftx, reuseCaseId: ngRet ? cs?.NgCaseId : null);
            if (!ok) { _db.ChangeTracker.Clear(); return (false, err); }
            if (ftx.TxIds.Count == 1)
            {
                var nt = await _db.HandoverTransactions.FirstAsync(t => t.HandoverTxId == ftx.TxIds[0]);
                tx.VoidReason = Cut($"Sửa phiếu → #{nt.HandoverTxId}: " + reason, 200);
                await _db.SaveChangesAsync();
            }
            var k5 = await CheckNoNewNegativeAsync(tx.KhPlanDetailId, before, beforeIn);
            if (k5 != null) { _db.ChangeTracker.Clear(); return (false, k5); }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(tx.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Join("\n", warns));
        }

        /// <summary>[N22-SUA] Phiếu có sửa được không (trang + máy chủ cùng 1 luật). Null = được.</summary>
        public static string? EditIssueBlock(string? purpose, long? sourceReceiveId, string? toCell, string status, bool hasReceive, decimal? qtyClosed)
        {
            if (sourceReceiveId != null) return "Phiếu trả hàng lỗi do phần mềm tự lập (NG lúc nhận): sửa ở lần nhận gốc (Sửa lần nhận).";
            if (HandoverCell.IsTraVe(toCell)) return "Phiếu phần mềm tự trả chiếc sửa chưa đạt: sửa / huỷ Kết quả sửa gốc.";
            if (HandoverCell.IsBuVu(toCell)) return "Phiếu phôi khách gửi bù theo mã vụ: huỷ phiếu rồi lập lại (form Giao / xử lý hàng của Kho).";   // [N19-BU-VU 2026-11-26] Claude tự chốt (G-F)
            if (HandoverPurpose.IsOut(purpose)) return "Phiếu Huỷ / Trả khách không có bên nhận — huỷ phiếu rồi lập lại.";
            if (string.IsNullOrEmpty(purpose)) return "Phiếu cũ (chưa có phân loại hàng) — huỷ phiếu rồi lập lại.";
            if (hasReceive || status != "PENDING" || (qtyClosed ?? 0) > 0)
                return "Bên nhận đã nhận (một phần hoặc hết) — không sửa phiếu được. Bên nhận Sửa lần nhận, hoặc Huỷ phần chưa nhận.";
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        // 2) SỬA LẦN NHẬN (bên nhận) — D7 bước 2a / 2b
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// [N22-SUA] Sửa số OK / NG lúc nhận của 1 lần nhận (cả nhóm dòng [N22#gốc]). Trả (Ok, lỗi | cảnh báo khi Ok — Workflow OFF).
        /// ngCategoryId: chỉ cần khi NG từ 0 lên &gt; 0 mà lần nhận chưa có vụ (sinh vụ mới như lúc nhận).
        /// </summary>
        public Task<(bool Ok, string Error)> EditReceiveAsync(long receiveId, decimal newOk, decimal newNg, int? ngCategoryId, string reason, int userId)
            => RetryDeadlockAsync(() => EditReceiveOnceAsync(receiveId, newOk, newNg, ngCategoryId, reason, userId));

        private async Task<(bool Ok, string Error)> EditReceiveOnceAsync(long receiveId, decimal newOk, decimal newNg, int? ngCategoryId, string reason, int userId)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length < 3) return (false, N22ReasonMsg);
            if (LenCheck.Over(reason, 150, "Lý do sửa") is string lenErr) return (false, lenErr);
            if (newOk < 0 || newNg < 0) return (false, "Số lượng không được âm.");
            if (newOk + newNg <= 0) return (false, "Phải còn ít nhất 1 chiếc OK hoặc NG — muốn bỏ cả lần nhận thì Huỷ lần nhận.");
            if (QtyRule.CheckWhole(newOk, newNg) is string eWhole) return (false, eWhole);
            var head = await _db.HandoverReceives.AsNoTracking().Where(r => r.ReceiveId == receiveId && !r.IsVoided)
                .Select(r => new { r.Transaction.KhPlanDetailId, r.ReceivedBy, r.Transaction.ToGroupCode }).FirstOrDefaultAsync();
            if (head == null) return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");
            var eg = await VoidRightsAsync(userId);
            if (eg == null) return (false, VoidInactiveMsg);
            if (!CanVoidReceive(head.ReceivedBy, head.ToGroupCode, userId, eg))
                return (false, $"Chỉ người đã bấm Nhận hoặc nhóm {HandoverLedgerService.GroupLabel(head.ToGroupCode)} mới sửa được lần nhận này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét cả theo thời gian
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);
            if (before == null) return (false, "Không tìm thấy PO.");

            var r0 = await _db.HandoverReceives.Include(r => r.Transaction).ThenInclude(t => t.Receives)
                .FirstOrDefaultAsync(r => r.ReceiveId == receiveId && !r.IsVoided);
            if (r0 == null) return (false, "Bản ghi nhận không tồn tại hoặc đã bị huỷ.");
            var tx = r0.Transaction;
            if (tx.IsVoided) return (false, "Phiếu giao đã bị huỷ.");
            if (HandoverPurpose.IsOut(tx.Purpose)) return (false, "Phiếu Huỷ / Trả khách không có bên nhận.");
            var root = N22Root(r0.Notes) ?? r0.ReceiveId;
            var grp = tx.Receives.Where(x => !x.IsVoided && (x.ReceiveId == r0.ReceiveId || (N22Root(x.Notes) is long rr && rr == root)))
                .OrderBy(x => x.ReceiveId).ToList();
            var grpIds = grp.Select(x => (long?)x.ReceiveId).ToList();
            var backs = await _db.HandoverTransactions.Include(t => t.Receives)
                .Where(t => grpIds.Contains(t.SourceReceiveId) && !t.IsVoided).ToListAsync();

            // ô thực nhận (như ReceiveOnceAsync: ToCell lưu ô thực nhận; NULL = phiếu không có ô → hàng đạt + NG lúc nhận)
            string? desig = tx.SourceReceiveId != null ? HandoverCell.Loi
                          : HandoverCell.IsReceiveCell(HandoverCell.DesigOf(tx.ToCell)) ? HandoverCell.DesigOf(tx.ToCell)
                          : (tx.Purpose ?? (tx.IsReturnNg ? HandoverPurpose.NgReturn : null)) is string pp ? HandoverCell.ReceiveCellOf(pp) : null;
            var eff = r0.ToCell ?? desig;
            decimal o0 = grp.Sum(x => x.QtyOk), n0 = grp.Sum(x => x.QtyNg);
            if (eff == HandoverCell.Loi && newOk > 0) return (false, "Lần nhận vào ô Hàng lỗi: không có hàng đạt — nhập số ở ô NG (Hàng lỗi).");
            if (eff == HandoverCell.Sua && newNg > 0) return (false, "Lần nhận vào ô Hàng sửa: chỉ có số chiếc nhận, không có NG lúc nhận.");
            if (newOk == o0 && newNg == n0) return (false, N22NoChangeMsg);
            // [rà độc lập G-B L2] phiếu đã "Huỷ phần chưa nhận": không giảm số nhận (sẽ mở lại phần đã đóng) — huỷ lần nhận rồi nhận lại
            if ((tx.QtyClosed ?? 0) > 0 && newOk + newNg < o0 + n0)
                return (false, $"Phiếu #{tx.HandoverTxId} đã huỷ phần chưa nhận — không giảm số nhận được (sẽ mở lại phần đã đóng). Huỷ lần nhận rồi nhận lại nếu cần.");
            // [rà độc lập G-B L1] luật lúc nhận [SUA-QUA-KCS] (phụ thuộc số OK): Workflow ON, phiếu lấy từ ô Hàng lỗi / Hàng sửa (chưa ghi Kết quả sửa) không nhận OK vào ô hàng đạt
            if (before.ApplyWorkflow && tx.SourceReceiveId == null && newOk > 0 && before.TxFromCell.TryGetValue(tx.HandoverTxId, out var tfc0)
                && tfc0 is HandoverCell.Loi or HandoverCell.Sua && eff is HandoverCell.Xl or HandoverCell.Moi)
                return (false, $"Workflow ON: phiếu này lấy từ ô {HandoverCell.Label(tfc0)} (chưa ghi Kết quả sửa) — không nhận OK vào ô {HandoverCell.Label(eff)}.");
            var others = tx.Receives.Where(x => !x.IsVoided && !grp.Contains(x)).Sum(x => x.QtyOk + x.QtyNg);
            var maxTotal = tx.QtyIssued - others;
            if (newOk + newNg > maxTotal)
                return (false, $"Số nhận ({newOk + newNg:0.##}) vượt số giao còn lại của phiếu ({maxTotal:0.##} = phiếu {tx.QtyIssued:0.##} − lần nhận khác {others:0.##}).");

            // bên nhận đã dùng hàng sau lần nhận (WTS / phiếu giao / báo lỗi / Kết quả sửa) → ON chặn, OFF cảnh báo (N7)
            var at0 = grp.Min(x => x.ReceivedAt);
            var used = UsedAfter(await UsedDocsAsync(tx.KhPlanDetailId), tx.ToGroupCode, at0);
            var warns = new List<string>();
            if (used != null)
            {
                if (before.ApplyWorkflow)
                    return (false, $"Không sửa lần nhận được: {used} — tránh nhầm số liệu. Phát hiện thêm hàng lỗi thì báo bằng form Giao / xử lý hàng (Trả nơi làm lỗi).");
                warns.Add($"Workflow OFF: {used} — vẫn sửa lần nhận, số liệu sau đó tự kiểm lại.");
            }

            // phần NG — mỗi dòng tối đa 1 phiếu trả tự lập
            var items = new List<(decimal Ok, decimal Ng, HandoverTransaction? Back, bool NewBack)>();
            var voidBacks = new List<HandoverTransaction>();
            bool withBack = eff != HandoverCell.Loi && eff != HandoverCell.Sua;
            if (!withBack) items.Add((newOk, newNg, null, false));
            else
            {
                var kept = new List<(decimal Ng, HandoverTransaction? Back)>();
                var pend = new List<(decimal Ng, HandoverTransaction Back)>();
                foreach (var g in grp.Where(x => x.QtyNg > 0))
                {
                    var b = backs.FirstOrDefault(x => x.SourceReceiveId == g.ReceiveId);
                    if (b == null || b.Receives.Any(x => !x.IsVoided) || (b.QtyClosed ?? 0) > 0) kept.Add((g.QtyNg, b));
                    else pend.Add((g.QtyNg, b));
                }
                decimal k = kept.Sum(x => x.Ng);
                decimal extra;
                if (newNg >= n0)
                {
                    items.AddRange(kept.Select(x => (0m, x.Ng, x.Back, false)));
                    items.AddRange(pend.Select(x => (0m, x.Ng, (HandoverTransaction?)x.Back, false)));
                    extra = newNg - n0;
                }
                else
                {
                    if (newNg < k)
                    {
                        var kb = kept.FirstOrDefault(x => x.Back != null).Back;
                        return (false, kb != null
                            ? $"{HandoverLedgerService.GroupLabel(kb.ToGroupCode)} đã nhận phiếu trả hàng lỗi #{kb.HandoverTxId} ({k:0.##} chiếc) — không giảm NG xuống dưới {k:0.##} được. "
                              + "Nếu chiếc đó thật ra không lỗi: bên đó giao trả lại."
                            : $"Không giảm NG xuống dưới {k:0.##} được.");
                    }
                    items.AddRange(kept.Select(x => (0m, x.Ng, x.Back, false)));
                    voidBacks.AddRange(pend.Select(x => x.Back));
                    extra = newNg - k;
                }
                if (extra > 0) items.Add((0m, extra, null, true));
                if (items.Count == 0) items.Add((0m, 0m, null, false));
                items[0] = (newOk, items[0].Ng, items[0].Back, items[0].NewBack);
            }

            // vụ của phần NG: vụ đang có của lần nhận; phiếu mang vụ / phiếu trả tự lập → vụ của phiếu; NG từ 0 → vụ mới (bắt chọn loại lỗi)
            bool autoTx = tx.SourceReceiveId != null;
            bool carryTx = !autoTx && before.TxFromCell.TryGetValue(tx.HandoverTxId, out var tfc) && tfc is HandoverCell.Loi or HandoverCell.Sua or HandoverCell.Xl;
            long? caseId = autoTx || carryTx ? tx.NgCaseId : grp.Select(x => x.NgCaseId).FirstOrDefault(x => x != null);
            string? ngReason = grp.Select(x => x.NgReason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            bool needCase = withBack && newNg > 0 && caseId == null && !autoTx && !carryTx;
            NgCategory? newCat = null;
            if (needCase)
            {
                newCat = await NgCaseService.GetActiveCategoryAsync(_db, ngCategoryId);
                if (newCat == null) return (false, NgCaseService.MsgPickCategory);
            }

            // ① huỷ các dòng cũ + phiếu trả tự lập chưa ai nhận (giảm NG)
            var now = DateTime.Now;
            foreach (var g in grp)
            {
                g.IsVoided = true; g.VoidReason = Cut("Sửa lần nhận: " + reason, 200); g.VoidedBy = userId; g.VoidedAt = now;
            }
            foreach (var b in voidBacks)
            {
                b.IsVoided = true; b.VoidReason = Cut($"Sửa lần nhận (giảm NG): {reason}", 200); b.VoidedBy = userId; b.VoidedAt = now;
            }
            if (needCase)
            {
                var vu = await NgCaseService.CreateCaseAsync(_db, tx.KhPlanDetailId, newCat!, NgOrigin.Nhan, tx.ToGroupCode,
                    tx.ToGroupCode == "GC" ? (r0.ToNC ?? tx.ToNC) : null, tx.FromGroupCode, userId);
                caseId = vu.NgCaseId; ngReason = newCat!.Label;
            }
            if (newNg > 0 && string.IsNullOrWhiteSpace(ngReason))
                ngReason = !string.IsNullOrWhiteSpace(tx.NgReason) ? tx.NgReason : "Hàng lỗi trả về";
            if (caseId != null && (await NgCaseService.GetCaseLabelsAsync(_db, new long?[] { caseId })).TryGetValue(caseId.Value, out var lbl)) ngReason = newNg > 0 ? lbl : ngReason;
            await _db.SaveChangesAsync();

            // ② lần nhận mới (giữ người nhận + giờ nhận gốc), nối / lập phiếu trả tự lập
            var oldIds = string.Join(", ", grp.Select(x => "#" + x.ReceiveId));
            var userNote = grp.Select(x => UserNoteOf(x.Notes)).FirstOrDefault(x => x != null);   // [rà độc lập L10] chỉ phần người gõ
            foreach (var it in items)
            {
                var nr = new HandoverReceive
                {
                    HandoverTxId = tx.HandoverTxId,
                    QtyOk        = it.Ok,
                    QtyNg        = it.Ng,
                    NgReason     = it.Ng > 0 ? ngReason : null,
                    ToCell       = r0.ToCell,
                    ToCellReason = r0.ToCellReason,
                    NgCaseId     = autoTx || carryTx || it.Ng > 0 || eff is HandoverCell.Loi or HandoverCell.Sua ? caseId : null,
                    ToNC         = r0.ToNC,
                    ReceivedBy   = r0.ReceivedBy,
                    ReceivedAt   = at0,
                    Notes        = Cut($"{N22Tag}{root}] Sửa lần nhận {oldIds}: {reason}" + (userNote == null ? "" : " | " + userNote), 500),
                    CreatedAt    = now,
                };
                _db.HandoverReceives.Add(nr);
                await _db.SaveChangesAsync();
                if (it.Back != null)
                {
                    var oldSrc = it.Back.SourceReceiveId;
                    it.Back.SourceReceiveId = nr.ReceiveId;   // phiếu trả cũ giữ nguyên, chỉ nối sang lần nhận sửa (1 lần nhận – 1 phiếu trả)
                    it.Back.Notes = Cut((it.Back.Notes ?? "") + $" · nối theo lần nhận sửa #{nr.ReceiveId} (từ #{oldSrc})", 500);
                }
                else if (it.NewBack)   // NG thêm: phiếu trả MỚI cùng vụ (dòng cũ không có phiếu trả — dữ liệu rất cũ — giữ như cũ)
                {
                    _db.HandoverTransactions.Add(new HandoverTransaction
                    {
                        KhPlanDetailId  = tx.KhPlanDetailId,
                        FromGroupCode   = tx.ToGroupCode,
                        ToGroupCode     = tx.FromGroupCode,
                        QtyIssued       = it.Ng,
                        NgQty           = it.Ng,
                        NgReason        = ngReason,
                        IsReturnNg      = true,
                        Purpose         = HandoverPurpose.NgReturn,
                        FromCell        = HandoverCell.Loi,
                        SourceReceiveId = nr.ReceiveId,
                        NgCaseId        = caseId,
                        Status          = "PENDING",
                        IssuedBy        = userId,
                        IssuedAt        = now,
                        CreatedAt       = now,
                        Notes           = $"Tự lập khi sửa lần nhận phiếu #{tx.HandoverTxId} ({oldIds}): NG lúc nhận",
                    });
                }
                await _db.SaveChangesAsync();
            }
            var tot = tx.Receives.Where(x => !x.IsVoided).Sum(x => x.QtyOk + x.QtyNg);
            tx.Status = tot <= 0 ? "PENDING" : tot >= tx.QtyIssued ? "COMPLETED" : "PARTIAL";
            await _db.SaveChangesAsync();

            var k5 = await CheckNoNewNegativeAsync(tx.KhPlanDetailId, before, beforeIn);
            if (k5 != null) { _db.ChangeTracker.Clear(); return (false, k5); }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(tx.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Join("\n", warns));
        }

        /// <summary>[N22-SUA] Chứng từ của 1 PO dùng để xét "bên nhận đã dùng hàng sau lần nhận" (đọc 1 lần cho cả Lịch sử).</summary>
        sealed record UsedDoc(string Group, DateTime At, string Text);

        async Task<List<UsedDoc>> UsedDocsAsync(int po)
        {
            var list = new List<UsedDoc>();
            var ws = await _db.WtsProductionLogs.AsNoTracking().Where(x => x.KhPlanDetailId == po && !x.IsVoided)
                .Select(x => new { x.ProcessGroup, x.CreatedAt, x.NC }).ToListAsync();
            foreach (var x in ws)
            {
                var g = x.ProcessGroup switch { "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", "PKG" => "PKG", _ => "" };
                if (g != "") list.Add(new UsedDoc(g, x.CreatedAt, $"{HandoverLedgerService.GroupLabel(g)} đã nhập WTS cho PO này sau lần nhận (dòng lưu {x.CreatedAt:dd/MM HH:mm}"
                                                           + (string.IsNullOrEmpty(x.NC) ? "" : " · " + HandoverLedgerService.NcText(x.NC)) + ")"));
            }
            // phiếu do nhóm lập (trừ phiếu trả tự lập — NG lúc nhận phiếu khác, không lấy hàng của lần nhận này)
            var ts = await _db.HandoverTransactions.AsNoTracking().Where(x => x.KhPlanDetailId == po && !x.IsVoided && x.SourceReceiveId == null)
                .Select(x => new { x.HandoverTxId, x.FromGroupCode, x.IssuedAt }).ToListAsync();
            list.AddRange(ts.Select(x => new UsedDoc(x.FromGroupCode, x.IssuedAt, $"{HandoverLedgerService.GroupLabel(x.FromGroupCode)} đã lập phiếu #{x.HandoverTxId} ({x.IssuedAt:dd/MM HH:mm}) sau lần nhận")));
            var bs = await _db.HandoverNgReports.AsNoTracking().Where(x => x.KhPlanDetailId == po && !x.IsVoided)
                .Select(x => new { x.HandoverNgReportId, x.GroupCode, x.CreatedAt }).ToListAsync();
            list.AddRange(bs.Select(x => new UsedDoc(x.GroupCode, x.CreatedAt, $"{HandoverLedgerService.GroupLabel(x.GroupCode)} đã báo lỗi #{x.HandoverNgReportId} ({x.CreatedAt:dd/MM HH:mm}) sau lần nhận")));
            var ks = await _db.HandoverRepairResults.AsNoTracking().Where(x => x.KhPlanDetailId == po && !x.IsVoided)
                .Select(x => new { x.HandoverRepairResultId, x.GroupCode, x.CreatedAt }).ToListAsync();
            list.AddRange(ks.Select(x => new UsedDoc(x.GroupCode, x.CreatedAt, $"{HandoverLedgerService.GroupLabel(x.GroupCode)} đã ghi Kết quả sửa #{x.HandoverRepairResultId} ({x.CreatedAt:dd/MM HH:mm}) sau lần nhận")));
            return list;
        }

        /// <summary>[N22-SUA] Bên nhận đã dùng hàng sau lần nhận: chứng từ đầu tiên của nhóm nhận (cùng PO) ghi SAU giờ nhận. Null = chưa.
        ///   WTS (giờ lưu), phiếu giao do nhóm lập (trừ phiếu trả tự lập), báo lỗi, Kết quả sửa.</summary>
        static string? UsedAfter(List<UsedDoc> docs, string grp, DateTime at)
            => docs.Where(d => d.Group == grp && d.At > at).OrderBy(d => d.At).FirstOrDefault()?.Text;

        // ─────────────────────────────────────────────────────────────
        // 3) ĐỔI LOẠI LỖI — D7 bước 3
        // ─────────────────────────────────────────────────────────────
        /// <summary>[N22-SUA] Đổi loại lỗi của 1 vụ (nút ở Lịch sử khi lọc theo mã vụ). Người tạo vụ hoặc quyền Sửa nhóm phát hiện; lý do bắt buộc;
        ///   luật NgCaseService.CategoryChangeBlockedAsync (Workflow OFF chỉ cảnh báo); ghi lịch sử PO (KhPlanDetailChangeLogs). Trả (Ok, lỗi | cảnh báo).</summary>
        public Task<(bool Ok, string Error)> ChangeCaseCategoryAsync(long caseId, int newCategoryId, string reason, int userId)
            => RetryDeadlockAsync(() => ChangeCaseCategoryOnceAsync(caseId, newCategoryId, reason, userId));

        private async Task<(bool Ok, string Error)> ChangeCaseCategoryOnceAsync(long caseId, int newCategoryId, string reason, int userId)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length < 3) return (false, N22ReasonMsg);
            if (LenCheck.Over(reason, 150, "Lý do") is string lenErr) return (false, lenErr);
            var cs0 = await _db.NgCases.AsNoTracking().FirstOrDefaultAsync(x => x.NgCaseId == caseId);
            if (cs0 == null) return (false, "Không tìm thấy mã vụ.");
            var eg = await VoidRightsAsync(userId);
            if (eg == null) return (false, VoidInactiveMsg);
            if (cs0.CreatedBy != userId && !eg.Contains(cs0.DetectGroupCode))
                return (false, $"Chỉ người tạo {NgCaseNo.Format(caseId)} hoặc nhóm {HandoverLedgerService.GroupLabel(cs0.DetectGroupCode)} (nơi phát hiện) mới đổi loại lỗi được.");
            var cat = await NgCaseService.GetActiveCategoryAsync(_db, newCategoryId);
            if (cat == null) return (false, NgCaseService.MsgPickCategory);
            if (cat.NgCategoryId == cs0.NgCategoryId) return (false, N22NoChangeMsg);

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(cs0.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            // [rà độc lập G-B L7] đọc lại vụ SAU khoá PO (người khác vừa đổi → lịch sử ghi đúng loại cũ / báo không đổi)
            cs0 = await _db.NgCases.AsNoTracking().FirstAsync(x => x.NgCaseId == caseId);
            if (cat.NgCategoryId == cs0.NgCategoryId) return (false, N22NoChangeMsg);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(cs0.KhPlanDetailId);
            if (before == null) return (false, "Không tìm thấy PO.");
            var oldLbl = await CatLabelAsync(cs0.NgCategoryId);
            var warns = new List<string>();
            var err = await NgCaseService.ChangeCaseCategoryAsync(_db, caseId, cat.NgCategoryId, before.ApplyWorkflow ? null : warns);
            if (err != null) { _db.ChangeTracker.Clear(); return (false, err); }
            LogCatChange(cs0.KhPlanDetailId, caseId, oldLbl, cat.Label, reason, userId);
            await _db.SaveChangesAsync();
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(cs0.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Join("\n", warns));
        }

        /// <summary>[N22-SUA] Tên trường lịch sử PO cho lần đổi loại lỗi của 1 vụ.</summary>
        public static string CatLogField(long caseId) => "Loại lỗi " + NgCaseNo.Format(caseId);

        async Task<string> CatLabelAsync(int catId)
            => await _db.NgCategories.AsNoTracking().Where(c => c.NgCategoryId == catId)
                   .Select(c => c.Code + " · " + c.Name).FirstOrDefaultAsync() ?? ("#" + catId);   // như NgCategory.Label

        void LogCatChange(int po, long caseId, string oldLbl, string newLbl, string reason, int userId)
            => _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
            {
                KhPlanDetailId = po,
                FieldName      = CatLogField(caseId),
                OldValue       = Cut(oldLbl, 500),
                NewValue       = Cut(newLbl, 500),
                ChangedBy      = userId,
                ChangedAt      = DateTime.Now,
                ChangeSource   = "GiaoNhan",
                Reason         = Cut(reason, 500),
            });

        // ─────────────────────────────────────────────────────────────
        // Đọc cho Lịch sử phiếu
        // ─────────────────────────────────────────────────────────────
        /// <summary>[N22-SUA] Thông tin nút Sửa lần nhận (đã dùng sau lần nhận), Đổi loại lỗi (luật + người tạo / nơi phát hiện), lịch sử đổi loại lỗi, NC Gia công.</summary>
        public async Task<HandoverEditInfo> GetEditInfoAsync(int khPlanDetailId)
        {
            var info = new HandoverEditInfo();
            var row = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return info;
            info.WorkflowOn = row.ApplyWorkflow;
            info.GcNcs = row.G("GC").Steps.Where(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)).Select(s => s.Code).Distinct().ToList();
            var recs = await _db.HandoverReceives.AsNoTracking()
                .Where(r => !r.IsVoided && r.Transaction.KhPlanDetailId == khPlanDetailId && !r.Transaction.IsVoided)
                .Select(r => new { r.ReceiveId, r.ReceivedAt, r.Notes, r.Transaction.ToGroupCode, r.HandoverTxId }).ToListAsync();
            var docs = recs.Count == 0 ? new List<UsedDoc>() : await UsedDocsAsync(khPlanDetailId);
            foreach (var r in recs)
            {
                var root = N22Root(r.Notes) ?? r.ReceiveId;
                var grp = recs.Where(x => x.HandoverTxId == r.HandoverTxId && (x.ReceiveId == r.ReceiveId || (N22Root(x.Notes) is long rr && rr == root))).ToList();
                var used = UsedAfter(docs, r.ToGroupCode, grp.Min(x => x.ReceivedAt));
                if (used != null) info.RecvUsed[r.ReceiveId] = used;
            }
            var cases = await _db.NgCases.AsNoTracking().Where(c => c.KhPlanDetailId == khPlanDetailId)
                .Select(c => new { c.NgCaseId, c.CreatedBy, c.DetectGroupCode, c.NgCategoryId }).ToListAsync();
            var lbls = await NgCaseService.GetCaseLabelsAsync(_db, cases.Select(c => (long?)c.NgCaseId));
            foreach (var kv in lbls) info.CaseCatLabel[kv.Key] = kv.Value;
            foreach (var c in cases)
            {
                info.Cases[c.NgCaseId] = (c.CreatedBy, c.DetectGroupCode, c.NgCategoryId);
                var blk = await NgCaseService.CategoryChangeBlockedAsync(_db, c.NgCaseId, int.MaxValue);   // id giả ≠ loại hiện tại: chỉ để biết vụ có đổi được không
                if (blk != null) info.CaseCatBlock[c.NgCaseId] = blk;
            }
            var logs = await _db.KhPlanDetailChangeLogs.AsNoTracking()
                .Where(l => l.KhPlanDetailId == khPlanDetailId && l.ChangeSource == "GiaoNhan" && l.FieldName.StartsWith("Loại lỗi "))
                .OrderByDescending(l => l.ChangedAt).ThenByDescending(l => l.ChangeId).ToListAsync();
            if (logs.Count > 0)
            {
                var uids = logs.Select(l => l.ChangedBy).Distinct().ToList();
                var names = (await _db.Users.AsNoTracking().Where(u => uids.Contains(u.UserId)).ToListAsync())
                    .ToDictionary(u => u.UserId, u => string.IsNullOrEmpty(u.FullName) ? u.Username : u.FullName);
                foreach (var l in logs)
                {
                    var id = cases.Select(c => c.NgCaseId).FirstOrDefault(x => CatLogField(x) == l.FieldName);
                    if (id == 0) continue;
                    info.CatLogs.Add(new HandoverCaseCatLog { CaseId = id, Old = l.OldValue ?? "", New = l.NewValue ?? "", Reason = l.Reason,
                                                              ByName = names.TryGetValue(l.ChangedBy, out var n) ? n : "", At = l.ChangedAt });
                }
            }
            return info;
        }
    }
}
