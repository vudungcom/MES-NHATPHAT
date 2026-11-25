using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [GN-FORM1 2026-11-05] LƯU FORM "GIAO / XỬ LÝ HÀNG" (1 form 4 bước)
    //   Nguồn: KHÁCH 05/11 15:43 (gộp mọi trường hợp về 1 form, chọn từng bước), 15:39 (Gia công chọn chiếc lỗi theo NC);
    //          HÙNG 15:52 ("chỉ đổi form, nhập đủ thông tin như cũ, không sai logic"), 16:12 (trần số bắt buộc), 17:35 (kế hoạch + (a)–(d)).
    //   Máy chủ tự ánh xạ lựa chọn → chứng từ ĐÃ CÓ (không chứng từ mới, không bảng mới, không công thức số mới):
    //     Hàng tốt                    → phiếu Hàng mới (Kho / đã làm chưa giao) · Giao theo NC (GC làm hộ) · Trả về Gia công theo NC (hàng làm hộ)
    //     Lỗi mới + tích công đoạn trước + chiếc chưa làm + trả đúng nơi gây lỗi → 1 phiếu Hàng lỗi công đoạn trước (như cũ; GC chốt NC = dòng chọn)
    //     Lỗi mới khác                → Báo hàng lỗi sau khi nhận + chứng từ kèm (phiếu Hàng lỗi / Kết quả sửa / Huỷ / Trả khách;
    //                                   "chỉ báo lỗi, giữ lại" = chỉ báo lỗi) — vụ lỗi của chứng từ kèm = vụ vừa sinh
    //     Tồn hàng lỗi / Tồn hàng sửa → 1 chứng từ theo vụ đang chọn
    //   Mọi chứng từ của 1 lần bấm: 1 giao dịch, khoá PO MES_PO_ (LockOwner=Transaction), mỗi chứng từ tự kiểm trần + quyền như cũ
    //   (đọc lại sổ sau chứng từ trước), K5 không ô âm mới 1 lần cuối so với sổ trước khi lưu; lỗi ở bất kỳ bước nào → không ghi gì;
    //   tự thử lại khi deadlock [RS-D10]. Nhận + KCS xác nhận vẫn là thao tác riêng (người khác).
    // =====================================================================

    /// <summary>[GN-FORM1] Ghi chung giữa các chứng từ của 1 lần lưu form (chạy trong giao dịch có sẵn).</summary>
    public class FormTx
    {
        /// <summary>Vụ lỗi vừa sinh (báo lỗi / phiếu Hàng lỗi công đoạn trước) hoặc vụ phiếu lấy hàng.</summary>
        public long?   CaseId { get; set; }
        /// <summary>NC chốt khi báo lỗi ở Gia công.</summary>
        public string? CaseNc { get; set; }
        public List<long> TxIds { get; } = new();
    }

    /// <summary>[GN-FORM1] Lựa chọn trên form (trang gửi lên — máy chủ kiểm lại hết, không tin số / quyền trang).</summary>
    public class FormGiaoRequest
    {
        public int     KhPlanDetailId { get; set; }
        /// <summary>Nhóm đang giữ hàng (nhóm làm chứng từ).</summary>
        public string  Group    { get; set; } = "";
        public int     UserId   { get; set; }
        /// <summary>Bước 1: TOT (hàng tốt) | LOI (hàng lỗi).</summary>
        public string  Kind     { get; set; } = "";
        /// <summary>Bước 2: mã dòng FormPos.Id.</summary>
        public string  Pos      { get; set; } = "";
        /// <summary>Tồn hàng lỗi / sửa: vụ (0 = hàng lỗi cũ chưa có mã).</summary>
        public long?   CaseId   { get; set; }
        /// <summary>Lỗi mới: tích "Lỗi công đoạn trước" (Kho: "Lỗi có sẵn (phôi khách)").</summary>
        public bool    Prev     { get; set; }
        /// <summary>Nơi gây lỗi khi tích (KHACH | mã nhóm).</summary>
        public string? Cause    { get; set; }
        /// <summary>Bước 3: HandoverForm.AGiao / ASua / AGiu / AHuy / AKhach / ATraLai / ASuaXong.</summary>
        public string  Action   { get; set; } = "";
        public string? ToGroup  { get; set; }
        /// <summary>Giao hàng lỗi: TRA (trả nơi gây lỗi → ô Hàng lỗi) | NHO (gửi nhờ sửa → ô Hàng sửa).</summary>
        public string? Purpose  { get; set; }
        /// <summary>Tuỳ chọn: ô bên nhận (bỏ trống = theo lựa chọn).</summary>
        public string? ToCell   { get; set; }
        /// <summary>Giao vào Gia công: NC nhận (Hàng mới / Hàng lỗi đã xử lý: tuỳ chọn; trả hàng làm hộ: bắt buộc).</summary>
        public string? ToNC     { get; set; }
        public decimal Qty      { get; set; }
        /// <summary>Sửa tại nhóm: số đạt / không đạt.</summary>
        public decimal Pass     { get; set; }
        public decimal Fail     { get; set; }
        /// <summary>Gia công, sửa đạt: NC làm tiếp (null = mặc định của vụ; "@NEW" = như hàng mới).</summary>
        public string? NcNext   { get; set; }
        public string? NcReason { get; set; }
        public int?    CatId    { get; set; }
        public string? Notes    { get; set; }
        /// <summary>[N23-SUA-GIAO] "Sửa tại bộ phận mình / XXN KH": nhóm nhận phần sửa đạt ("Đạt → giao đến", vào ô Hàng lỗi đã xử lý). null = chưa giao / không giao.</summary>
        public string? SuaTo    { get; set; }
        /// <summary>[N23-SUA-CHUADAT] Sửa ở ô Hàng sửa: chiếc sửa chưa đạt tự trả nơi làm lỗi (ô Hàng lỗi). false = như cũ (về ô Hàng lỗi của nhóm sửa).</summary>
        public bool    SuaFailTra { get; set; }
    }

    public partial class HandoverService
    {
        /// <summary>[GN-FORM1] Mã gửi lên khi chọn "như hàng mới" ở NC làm tiếp.</summary>
        public const string FormNewNc = "@NEW";
        /// <summary>[CAUSE-BAT-BUOC 2026-11-10] Lời báo khi tích "Lỗi công đoạn trước" mà chưa chọn nơi gây lỗi (Nguồn: HÙNG 10/11 16:52).</summary>
        public const string CauseRequiredMsg = "\"Lỗi công đoạn trước\": chọn nơi gây lỗi (bắt buộc).";

        public Task<(bool Ok, string Error)> SaveFormAsync(FormGiaoRequest r)
            => RetryDeadlockAsync(() => SaveFormOnceAsync(r));   // [RS-D10]

        private async Task<(bool Ok, string Error)> SaveFormOnceAsync(FormGiaoRequest r)
        {
            var g = (r.Group ?? "").Trim();
            if (!HandoverLedgerService.GroupCodes.Contains(g)) return (false, "Chọn nhóm đang giữ hàng.");
            if (r.Kind is not (HandoverForm.Tot or HandoverForm.Loi)) return (false, "Chọn hàng gì (bước 1).");
            if (string.IsNullOrWhiteSpace(r.Pos)) return (false, "Chọn hàng đang ở đâu (bước 2).");
            if (string.IsNullOrWhiteSpace(r.Action)) return (false, "Chọn làm gì (bước 3).");
            var deny = await DenyGroupAsync(r.UserId, g, "giao / xử lý hàng");
            if (deny != null) return (false, deny);
            foreach (var v in new[] { r.Qty, r.Pass, r.Fail })
            {
                if (v < 0) return (false, "Số lượng không được âm.");
                if (!QtyRule.IsWhole(v)) return (false, QtyRule.Msg);   // [B3-SO-NGUYEN 2026-11-06] chỉ số nguyên (trước: tối đa 2 số lẻ)
            }
            var notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
            if (notes != null && notes.Length > 500) notes = notes[..500];

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(r.KhPlanDetailId);   // [GN-K8] khoá chung WTS + giao nhận, giữ tới Commit
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(r.KhPlanDetailId);
            if (before == null) return (false, "Không tìm thấy PO.");
            var pos = HandoverForm.FormPositions(before, g, r.Kind == HandoverForm.Tot).FirstOrDefault(p => p.Id == r.Pos);
            if (pos == null) return (false, "Dòng hàng đã chọn không còn (sổ vừa đổi) — chọn lại hàng đang ở đâu (bước 2).");

            var ftx = new FormTx();
            var (ok, err) = await SaveFormDocsAsync(r, g, pos, before, notes, ftx);
            if (!ok) { _db.ChangeTracker.Clear(); return (false, err); }   // không Commit → rollback cả các chứng từ đã ghi trong lần này

            var k5 = await CheckNoNewNegativeAsync(r.KhPlanDetailId, before);   // [GN-K8][K5] 1 lần cuối, so với sổ trước khi lưu
            if (k5 != null) { _db.ChangeTracker.Clear(); return (false, k5); }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(r.KhPlanDetailId);   // [GN-LIVE]
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        /// <summary>[GN-FORM1] Ánh xạ lựa chọn → chứng từ (đã trong giao dịch + khoá PO). Mỗi chứng từ dùng đúng hàm lập cũ (trần, quyền, vụ).</summary>
        private async Task<(bool Ok, string Error)> SaveFormDocsAsync(FormGiaoRequest r, string g, FormPos pos, LedgerRow before, string? notes, FormTx ftx)
        {
            int id = r.KhPlanDetailId, uid = r.UserId;
            var act = r.Action;
            var to = string.IsNullOrWhiteSpace(r.ToGroup) ? null : r.ToGroup.Trim();
            string? ToNcFor(string? toGroup) => toGroup == "GC" && !string.IsNullOrWhiteSpace(r.ToNC) ? r.ToNC.Trim() : null;
            string? toCellOpt = string.IsNullOrWhiteSpace(r.ToCell) ? null : r.ToCell.Trim();
            const string BadAct = "Việc chọn ở bước 3 không hợp lệ với dòng hàng đã chọn — chọn lại.";

            // ── [SUA-XL-GIU][XL-TRA-LAI 2026-11-10] Hàng đã sửa đang giữ ở Tồn hàng lỗi đã xử lý (theo vụ) ──
            //   Hàng tốt → trả tiếp về nơi phát hiện (ô Hàng lỗi đã xử lý); Hàng lỗi (hỏng lại khi làm lại) → trả lại ô Hàng lỗi, cùng vụ.
            //   Nguồn: KHÁCH 10/11 vòng 1 (docx câu 2, hỏi tiếp 2.1 A); HÙNG 10/11 19:22 OK thiết kế Gói 15.
            if (pos.HeldCell == HandoverCell.Xl)
            {
                if (act != HandoverForm.AGiao) return (false, BadAct);
                if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                if (r.CaseId is not long xv || xv <= 0 || !before.Cases.ContainsKey(xv))
                    return (false, $"Chọn {NgCaseNo.Term} của hàng đã sửa (bước 2).");
                if (to == null) return (false, "Chọn nhóm nhận.");
                var xtc = r.Kind == HandoverForm.Tot ? HandoverCell.Xl : HandoverCell.Loi;
                return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.RepairReturn, null, null, notes, null, HandoverCell.Xl, xtc, null, xv, ftx);
            }

            // ── Hàng tốt ─────────────────────────────────────────────────────
            if (r.Kind == HandoverForm.Tot)
            {
                if (act != HandoverForm.AGiao) return (false, BadAct);
                if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                if (pos.LamHo)   // hàng Gia công giao theo NC đang giữ → trả về Gia công (K3: bắt buộc NC nhận)
                    return await IssueWithPurposeOnceAsync(id, g, "GC", r.Qty, uid, HandoverPurpose.HelpReturn, null, ToNcFor("GC"), notes, null, ftx: ftx);
                if (to == null) return (false, "Chọn nhóm nhận.");
                if (pos.HelpFrom != null)   // (a) Gia công giao theo NC cho nhóm khác làm hộ (N3, N4; [N20-GHICHU] ghi chú không bắt buộc)
                    return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.HelpSend, pos.HelpFrom, null, notes, null, ftx: ftx);
                // [KHO-TARO-THANG 2026-11-12] Kho, phôi còn → HTSP khi NC đầu Gia công là Taro (máy loại trừ): phiếu Kho giao thẳng Taro (giao theo NC từ "Nhận vào").
                //   Nguồn: KHÁCH 12/11 (3.3 b = A — chọn sẵn Taro NC đầu); HÙNG 12/11 14:00.
                if (g == "KHO" && pos.Id == "PHOI" && to == "HTSP" && HandoverLedgerService.KhoTaroRun(before) != null)
                {
                    if (toCellOpt != null) return (false, "Kho giao thẳng Taro: không chọn ô nhận (Taro nhận theo NC).");
                    return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.HelpSend, HandoverLedgerService.VaoCode, null, notes, null, ftx: ftx);
                }
                var tcN = toCellOpt ?? HandoverCell.Moi;
                var (pN, eN) = HandoverCell.ResolveCells(HandoverCell.Moi, false, tcN);
                if (pN == null) return (false, eN ?? "Ô nhận không hợp lệ.");
                return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, pN, null, ToNcFor(to), notes, null, HandoverCell.Moi, tcN, ftx: ftx);
            }

            // ── Hàng lỗi đang giữ theo vụ ────────────────────────────────────
            if (pos.HeldCell != null)
            {
                if (r.CaseId == null) return (false, $"Chọn {NgCaseNo.Term}.");
                // [A7-VU-AM 2026-11-06] Mã vụ âm / không có trong sổ PO: trước rơi xuống kiểm NC làm tiếp (K2) và báo nhầm lời K2, hoặc lời "vụ mới không thuộc PO"
                //   (chỉ sai chữ, không ghi gì). Nay báo thẳng trước mọi bước. Nguồn: CLAUDE (rà độc lập 06/11 gói GN-FORM1), Hùng 06/11 06:56 OK làm.
                if (r.CaseId < 0 || (r.CaseId > 0 && !before.Cases.ContainsKey(r.CaseId.Value)))
                    return (false, $"{NgCaseNo.TermCap} đã chọn không thuộc PO này — chọn lại {NgCaseNo.Term} ở bước 2.");
                long? cs = r.CaseId == 0 ? null : r.CaseId;
                if (pos.HeldCell == HandoverCell.LamHo)   // [TARO-LAMHO-NC] hàng làm hộ bị lỗi → trả Gia công ô Hàng lỗi, cùng vụ
                {
                    if (act != HandoverForm.AGiao) return (false, BadAct);
                    if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                    if (to != null && to != "GC") return (false, "Hàng làm hộ bị lỗi chỉ trả về Gia công.");
                    return await IssueWithPurposeOnceAsync(id, g, "GC", r.Qty, uid, HandoverPurpose.HelpReturn, null, null, notes, null, HandoverCell.Loi, null, null, cs, ftx);
                }
                if (pos.HeldCell == HandoverCell.Loi)
                {
                    switch (act)
                    {
                        case HandoverForm.AGiao:
                        {
                            if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                            if (to == null) return (false, "Chọn nhóm nhận.");
                            var tc = toCellOpt ?? (r.Purpose == HandoverForm.PTra ? HandoverCell.Loi : HandoverCell.Sua);
                            var (p, e) = HandoverCell.ResolveCells(HandoverCell.Loi, false, tc);
                            if (p == null) return (false, e ?? "Ô nhận không hợp lệ.");
                            var (eNc, tnc, nts) = await CaseToNcAsync(r, to, tc, cs ?? 0, notes);   // [K2] hàng đã xử lý của vụ vào Gia công
                            if (eNc != null) return (false, eNc);
                            return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, p, null, tnc, nts, null, HandoverCell.Loi, tc, null, cs, ftx);
                        }
                        case HandoverForm.ASua:
                            if (r.Fail != 0) return (false, "Lấy từ Tồn hàng lỗi: chiếc sửa không đạt vẫn nằm ở Tồn hàng lỗi — chỉ ghi số đạt.");
                            return await RepairGiveInFormAsync(r, g, HandoverCell.Loi, cs, r.Pass, 0, notes, ftx, before);   // [N23-SUA-GIAO]
                        case HandoverForm.AHuy:
                        case HandoverForm.AKhach:
                        {
                            if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                            var p = act == HandoverForm.AHuy ? HandoverPurpose.Scrap : HandoverPurpose.CustReturn;
                            return await IssueWithPurposeOnceAsync(id, g, HandoverPurpose.OutTarget(p)!, r.Qty, uid, p, null, null, notes, null, HandoverCell.Loi, null, null, cs, ftx);
                        }
                        default: return (false, BadAct);
                    }
                }
                switch (act)   // Tồn hàng sửa (nhóm khác gửi nhờ sửa)
                {
                    case HandoverForm.AGiao when r.Purpose == HandoverForm.PNho:
                    {   // [N23-FORM6 rà độc lập T3] "Gửi bộ phận khác sửa" từ ô Hàng sửa (mockup TH3 bước 5 nút không xám): chuyển tiếp nhờ bộ phận khác sửa —
                        //   phiếu trả hàng sửa (ô Hàng sửa → ô Hàng sửa bên nhận), cùng vụ; sửa không đạt bên đó tự trả nơi làm lỗi. Nguồn: KHÁCH 23/11 (mockup); CLAUDE (rà độc lập G-A2).
                        if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                        if (to == null) return (false, "Chọn nhóm nhận.");
                        if (toCellOpt != null && toCellOpt != HandoverCell.Sua) return (false, "Gửi bộ phận khác sửa: bên nhận vào ô Hàng sửa.");
                        return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.RepairReturn, null, null, notes, null, HandoverCell.Sua, HandoverCell.Sua, null, cs, ftx);
                    }
                    case HandoverForm.ASua:
                        return await RepairGiveInFormAsync(r, g, HandoverCell.Sua, cs, r.Pass, r.Fail, notes, ftx, before);   // [N23-SUA-GIAO][N23-SUA-CHUADAT]
                    case HandoverForm.ATraLai:    // chưa sửa được → trả nơi gửi, vào ô Hàng lỗi bên kia (cùng vụ)
                    case HandoverForm.ASuaXong:   // (a) sửa xong trả nơi gửi → ô Hàng lỗi đã xử lý bên kia
                    {
                        if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                        if (to == null) return (false, "Chọn nhóm nhận.");
                        var tc = act == HandoverForm.ATraLai ? HandoverCell.Loi : HandoverCell.Xl;
                        var (eNc, tnc, nts) = await CaseToNcAsync(r, to, tc, cs ?? 0, notes);   // [K2] sửa xong trả Gia công: NC theo vụ
                        if (eNc != null) return (false, eNc);
                        return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.RepairReturn, null, tnc, nts, null, HandoverCell.Sua, tc, null, cs, ftx);
                    }
                    default: return (false, BadAct);
                }
            }

            // ── [KTT-PHOILOI 2026-11-12] Taro phát hiện phôi lỗi khi đang làm hàng đoạn đầu chuỗi (Kho giao thẳng) → 1 phiếu trả thẳng Kho ô Hàng lỗi ──
            //   Nơi gây lỗi Khách hàng (máy chủ tự ghi). Chỉ việc "Giao" về Kho. Nguồn: KHÁCH 12/11 (3.3 a = A); HÙNG 12/11 14:00 (B2: chỉ trả Kho).
            if (pos.TaroPhoi)
            {
                if (act != HandoverForm.AGiao) return (false, "Phôi lỗi Taro phát hiện khi đang làm: chỉ trả về Kho (bước 3 chọn Giao).");
                if (!r.Prev) return (false, "Phôi lỗi Taro phát hiện khi đang làm: tích \"Lỗi công đoạn trước\" (phôi lỗi, nơi gây lỗi Khách hàng). Chiếc Taro làm hỏng ghi NG ở WTS Taro.");
                if (!string.IsNullOrWhiteSpace(r.Cause) && r.Cause.Trim() != "KHACH") return (false, "Phôi lỗi: nơi gây lỗi là Khách hàng.");
                if (to != null && to != "KHO") return (false, "Taro trả phôi lỗi (hàng giao theo NC chưa làm xong Taro NC đầu): chỉ trả về Kho.");
                if (toCellOpt != null && toCellOpt != HandoverCell.Loi) return (false, "Taro trả phôi lỗi về Kho: vào ô Hàng lỗi của Kho.");
                if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                return await IssueWithPurposeOnceAsync(id, g, "KHO", r.Qty, uid, HandoverPurpose.NgReturn, HandoverLedgerService.VaoCode, null, notes, null,
                    HandoverCell.Loi, HandoverCell.Loi, r.CatId, null, ftx);
            }

            // ── Hàng lỗi mới phát hiện ──────────────────────────────────────
            if (!pos.Fresh || pos.NgSource == null) return (false, BadAct);
            if (act is not (HandoverForm.AGiao or HandoverForm.ASua or HandoverForm.AGiu or HandoverForm.AHuy or HandoverForm.AKhach)) return (false, BadAct);
            decimal n = act == HandoverForm.ASua ? r.Pass + r.Fail : r.Qty;
            if (n <= 0) return (false, act == HandoverForm.ASua ? "Nhập số sửa đạt hoặc không đạt." : "Số lượng phải lớn hơn 0.");
            // [GC-LOI-TRUOC-NCDAU 2026-11-10] dòng không cho tích "Lỗi công đoạn trước" (Gia công, Workflow ON: hàng đã gia công) → chặn trước khi ghi
            if (r.Prev && !pos.PrevOk) return (false, HandoverForm.GcPrevMsg);
            string origin = r.Prev ? HandoverNgSource.DauVao : HandoverNgSource.DauRa;
            // [CAUSE-BAT-BUOC 2026-11-10] "Lỗi công đoạn trước": bắt buộc chọn nơi gây lỗi, không điền sẵn (trước: trống = nhóm trước).
            //   Nguồn: HÙNG 10/11 16:52 "họ bắt buộc phải chọn mà, mình cũng ko cần gợi ý sẵn đâu, kệ họ". Kho: phôi khách (KHACH) như cũ.
            if (r.Prev && g != "KHO" && string.IsNullOrWhiteSpace(r.Cause)) return (false, CauseRequiredMsg);
            string? cause = !r.Prev ? null : g == "KHO" ? "KHACH" : r.Cause!.Trim();   // (c) Kho: lỗi có sẵn = phôi khách · nơi khác: người dùng chọn ([CAUSE-BAT-BUOC]; trước [LAMHO-L2-KCS] trống = nhóm trước theo PO)

            // Lỗi công đoạn trước + chiếc chưa làm + trả đúng nơi gây lỗi → 1 phiếu Hàng lỗi công đoạn trước (như cũ)
            if (act == HandoverForm.AGiao && r.Prev && pos.Unworked && g != "KHO" && r.Purpose == HandoverForm.PTra
                && to != null && to == cause && (toCellOpt == null || toCellOpt == HandoverCell.Loi))
                return await IssueWithPurposeOnceAsync(id, g, to, n, uid, HandoverPurpose.NgReturn,
                    g == "GC" ? pos.NgNc : null, null, notes, null, HandoverCell.Loi, HandoverCell.Loi, r.CatId, null, ftx);

            if (act == HandoverForm.AGiao && to == null) return (false, "Chọn nhóm nhận.");   // kiểm trước khi ghi báo lỗi
            // Bước 1: báo hàng lỗi phát hiện sau khi nhận (sinh vụ, hàng sang Tồn hàng lỗi của nhóm)
            var (okB, errB) = await ReportNgAfterReceiveOnceAsync(id, g, pos.NgSource, pos.NgNc, n, origin, cause, "", notes, uid,
                Array.Empty<string>(), r.CatId, ftx);
            if (!okB) return (false, errB);
            var vu = ftx.CaseId;
            // Bước 2: chứng từ kèm, lấy từ Tồn hàng lỗi đúng vụ vừa sinh
            switch (act)
            {
                case HandoverForm.AGiu:
                    return (true, string.Empty);
                case HandoverForm.AGiao:
                {
                    var tc = toCellOpt ?? (r.Purpose == HandoverForm.PTra && g != "KHO" ? HandoverCell.Loi : HandoverCell.Sua);
                    var (p, e) = HandoverCell.ResolveCells(HandoverCell.Loi, false, tc);
                    if (p == null) return (false, e ?? "Ô nhận không hợp lệ.");
                    var (eNc, tnc, nts) = await CaseToNcAsync(r, to!, tc, vu ?? 0, notes);   // [K2]
                    if (eNc != null) return (false, eNc);
                    return await IssueWithPurposeOnceAsync(id, g, to!, n, uid, p, null, tnc, nts, null, HandoverCell.Loi, tc, null, vu, ftx);
                }
                case HandoverForm.ASua:
                    if (!string.IsNullOrWhiteSpace(r.SuaTo)) return (false, SuaToFreshMsg);   // [N23-SUA-GIAO] (kiểm sau báo lỗi: lỗi → không Commit, không ghi gì)
                    if (r.Pass <= 0) return (true, string.Empty);   // chỉ có chiếc không đạt: vẫn ở Tồn hàng lỗi (= chỉ báo lỗi)
                    return await RepairInFormAsync(r, g, HandoverCell.Loi, vu, r.Pass, 0, notes, ftx);
                default:   // Huỷ / Trả khách
                {
                    var p = act == HandoverForm.AHuy ? HandoverPurpose.Scrap : HandoverPurpose.CustReturn;
                    return await IssueWithPurposeOnceAsync(id, g, HandoverPurpose.OutTarget(p)!, n, uid, p, null, null, notes, null, HandoverCell.Loi, null, null, vu, ftx);
                }
            }
        }

        /// <summary>
        /// [GN-FORM1][K2] rà độc lập V3: phiếu MANG VỤ vào Gia công ô Hàng lỗi đã xử lý / Hàng mới (nhóm khác sửa xong trả về) cũng theo luật NC làm tiếp
        ///   như Gia công tự sửa: mặc định NC của vụ (ToNC trống = mặc định, "@NEW" = như hàng mới), chỉ lùi NC trước + lý do (NcReason, ghi vào
        ///   Ghi chú phiếu), NC sau bị chặn. Trước: chọn NC nào cũng lưu (vd vụ ở NC2, HTSP sửa xong trả vào NC3 → bỏ qua NC2).
        ///   Phiếu không vào Gia công / không vào 2 ô đó / PO chưa có NC: giữ như cũ (ToNC theo trang, chỉ áp cho Gia công).
        /// </summary>
        private async Task<(string? Error, string? ToNc, string? Notes)> CaseToNcAsync(FormGiaoRequest r, string to, string toCell, long caseKey, string? notes)
        {
            var raw = string.IsNullOrWhiteSpace(r.ToNC) ? null : r.ToNC.Trim();
            if (to != "GC" || toCell is not (HandoverCell.Xl or HandoverCell.Moi)) return (null, null, notes);
            var now = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(r.KhPlanDetailId);   // sổ sau bước báo lỗi (vụ vừa sinh đã có)
            if (now == null || !now.G("GC").Steps.Any(x => !x.IsExcluded && !HandoverLedgerService.IsVao(x.Code)))
                return (null, raw == FormNewNc ? null : raw, notes);
            var def = HandoverForm.CaseNextNc(now, caseKey, r.Group);   // [GN-FORM1] vòng 2 C1: vụ 0 theo nhóm giữ
            var chosen = raw == null ? def : raw == FormNewNc ? null : raw;
            var (err, add) = HandoverForm.CheckNextNc(now, def, chosen, r.NcReason, free: caseKey == 0, allowEarlier: !now.ApplyWorkflow);   // [GN-FORM1] vòng 3 C1b · [SUA-KHONG-LUI] ON: không làm lại NC trước
            if (err != null) return (err, null, notes);
            if (add != null) { notes = notes == null ? add : add + " | " + notes; if (notes.Length > 500) notes = notes[..500]; }
            return (null, chosen, notes);
        }

        // =================================================================
        // [N23-SUA-GIAO 2026-11-25] Nút "Sửa tại bộ phận mình / XXN KH" (form 6 nút, luật N23): phần sửa đạt có ô "Đạt → giao đến" (mặc định nơi báo lỗi).
        //   Nguồn: KHÁCH 23/11 (mockup Form chung bảng 6 nút + Bước 4; TH4 bước 3; TH10 bước 3); HÙNG 23/11 16:24 (N23) — chi tiết dưới.
        //   Cùng 1 lần lưu: Kết quả sửa (N17 tự xác nhận) → hàng đạt vào Tồn hàng lỗi đã xử lý của nhóm sửa (đích "GIU" sẵn có — SUA-XL-GIU) → phiếu trả hàng đã sửa
        //   từ ô đó tới nhóm nhận, ô Hàng lỗi đã xử lý, cùng vụ (đúng chứng từ cũ "Hàng đã sửa — chờ trả nơi phát hiện"; mọi luật XL-VUOT-CHAN / K2 / trần kiểm như cũ).
        //   Không có đích giữ ô (vụ do chính nhóm phát hiện, Gia công làm hỏng ở NC k, vụ làm hộ…) → không giao kèm được (báo lỗi, không ghi gì).
        //   Nguồn: KHÁCH 23/11 (mockup Form chung bảng 6 nút + Bước 4 "Giao đến — cho phần sửa đạt, mặc định nơi báo lỗi"; TH4 bước 3; TH10 bước 3); HÙNG 23/11 16:24 (N23).
        // [N23-SUA-CHUADAT 2026-11-25] Sửa ở ô Hàng sửa (bộ phận khác gửi sửa): chiếc sửa chưa đạt / không sửa được → phần mềm tự lập phiếu trả nơi làm lỗi, vào ô Hàng lỗi,
        //   cùng vụ (Kết quả sửa ghi cả số chưa đạt → ô Hàng lỗi nhóm sửa, rồi phiếu từ ô Hàng lỗi). Nơi làm lỗi là khách / không rõ → trả nơi gửi; không có cả hai → như cũ (ở ô Hàng lỗi của nhóm sửa).
        //   Cách đếm ô riêng "Hàng sửa trả về chưa đạt" (N18 Cách Y) làm ở gói G-E. Nguồn: KHÁCH 23/11 16:03 ("sửa không đạt nhập ô Sửa chưa đạt, phần mềm tự trả về ô Hàng lỗi
        //   của nơi làm lỗi"; Form chung dòng 2 "phần mềm tự tạo sẵn phiếu giao từ bộ phận sửa về bộ phận làm lỗi"); HÙNG 23/11 16:24 (N23).
        // =================================================================
        public const string SuaToFreshMsg = "Hàng lỗi do chính bộ phận mình phát hiện: sửa đạt tính là đã làm (không giao kèm) — giao tiếp như hàng tốt.";
        public const string SuaToNoGiuMsg = "Hàng sửa đạt của vụ này không vào Tồn hàng lỗi đã xử lý (vụ do bộ phận mình phát hiện / làm tiếp theo NC) — bỏ ô \"Đạt → giao đến\".";

        /// <summary>[N23-SUA-CHUADAT] Nơi trả chiếc sửa chưa đạt (ô Hàng sửa): nơi làm lỗi (nhóm khác mình) → nơi gửi → null (như cũ).</summary>
        public static string? SuaFailReturnGroup(LedgerRow row, long caseKey, string g)
            => row.Cases.TryGetValue(caseKey, out var c) ? HandoverForm.CaseCauseGroup(c, g) ?? HandoverForm.CaseSender(c, g) : null;

        private async Task<(bool Ok, string Error)> RepairGiveInFormAsync(FormGiaoRequest r, string g, string cell, long? cs, decimal pass, decimal fail, string? notes, FormTx ftx, LedgerRow before)
        {
            var suaTo = string.IsNullOrWhiteSpace(r.SuaTo) ? null : r.SuaTo.Trim();
            if (suaTo != null)
            {
                if (!HandoverLedgerService.GroupCodes.Contains(suaTo) || suaTo == g) return (false, "\"Đạt → giao đến\": chọn bộ phận nhận khác bộ phận mình.");
                if (pass <= 0) return (false, "\"Đạt → giao đến\" chỉ cho phần sửa đạt — nhập số sửa đạt.");
            }
            string? failTo = cell == HandoverCell.Sua && fail > 0 && r.SuaFailTra ? SuaFailReturnGroup(before, cs ?? 0, g) : null;
            if (failTo == g) failTo = null;
            // [N23-SUA-CHUADAT rà độc lập T4] chiếc chưa đạt vẫn ghi vào Kết quả sửa (truy vết: ai sửa, đạt / chưa đạt — như N17) → về ô Hàng lỗi của nhóm sửa, cùng vụ;
            //   rồi phiếu từ ô Hàng lỗi đó trả nơi làm lỗi (ô Hàng lỗi). Trước bản rà: phiếu thẳng từ ô Hàng sửa, Kết quả sửa không ghi số chưa đạt.
            var (ok, err) = await RepairInFormAsync(r, g, cell, cs, pass, fail, notes, ftx);
            if (!ok) return (false, err);
            if (suaTo != null)
            {
                var now = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(r.KhPlanDetailId);   // sổ sau Kết quả sửa (cùng giao dịch)
                // [N23-SUA-GIAO rà độc lập T1] so phần TĂNG của lần sửa này (không lấy hàng đã sửa giữ từ trước khi lần này không vào giữ ô — vd Workflow OFF chọn làm lại NC)
                decimal xl0 = cs is long v0 && before.Cases.TryGetValue(v0, out var c0) ? c0.HeldAt(g, HandoverCell.Xl) : 0;
                if (now != null && cs is long vu && now.Cases.TryGetValue(vu, out var c) && c.HeldAt(g, HandoverCell.Xl) - xl0 >= pass)
                {   // phần đạt đang giữ ô Tồn hàng lỗi đã xử lý → phiếu trả hàng đã sửa, bên nhận ô Hàng lỗi đã xử lý (mockup TH4 / TH10)
                    var (okG, errG) = await IssueWithPurposeOnceAsync(r.KhPlanDetailId, g, suaTo, pass, r.UserId, HandoverPurpose.RepairReturn, null, null, notes, null,
                        HandoverCell.Xl, HandoverCell.Xl, null, vu, ftx);
                    if (!okG) return (false, errG);
                }
                else if (g == "KHO")
                {   // [N23-KHO-SUA] Kho: phần đạt thành phôi còn (chiếc bên kia chưa từng nhận — NG lúc nhận) → phiếu Hàng mới (mockup TH3 bước 3); NC nhận theo luật Kho → Gia công
                    var (okN, errN) = await IssueWithPurposeOnceAsync(r.KhPlanDetailId, g, suaTo, pass, r.UserId, HandoverPurpose.New, null, null, notes, null,
                        HandoverCell.Moi, HandoverCell.Moi, ftx: ftx);
                    if (!okN) return (false, errN);
                }
                else return (false, SuaToNoGiuMsg);
            }
            if (failTo != null)
            {
                var (okF, errF) = await IssueWithPurposeOnceAsync(r.KhPlanDetailId, g, failTo, fail, r.UserId, HandoverPurpose.RepairSend, null, null, notes, null,
                    HandoverCell.Loi, HandoverCell.Loi, null, cs, ftx);
                if (!okF) return (false, errF);
            }
            return (true, string.Empty);
        }

        /// <summary>[GN-FORM1] Kết quả sửa trong form: NC làm tiếp (Gia công) — null = mặc định của vụ, "@NEW" = như hàng mới; máy chủ kiểm luật NC (K2).</summary>
        private async Task<(bool Ok, string Error)> RepairInFormAsync(FormGiaoRequest r, string g, string cell, long? cs, decimal pass, decimal fail, string? notes, FormTx ftx)
        {
            string? toNc = null;
            if (g == "GC" && pass > 0)
            {
                if (r.NcNext == FormNewNc) toNc = null;
                else if (string.IsNullOrWhiteSpace(r.NcNext))
                {
                    var now = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(r.KhPlanDetailId);   // sổ sau bước báo lỗi (vụ vừa sinh đã có)
                    toNc = now == null ? null : HandoverForm.RepairTarget(now, cs ?? 0, g);   // [SUA-XL-GIU][SUA-OK-NC] đích theo vụ (dấu / NC); trước: CaseNextNc
                }
                else toNc = r.NcNext.Trim();
            }
            return await CreateRepairResultOnceAsync(r.KhPlanDetailId, g, cell, cs, pass, fail, toNc, notes, r.UserId, r.NcReason, ftx);
        }
    }
}
