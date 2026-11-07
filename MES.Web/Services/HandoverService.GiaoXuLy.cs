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
    }

    public partial class HandoverService
    {
        /// <summary>[GN-FORM1] Mã gửi lên khi chọn "như hàng mới" ở NC làm tiếp.</summary>
        public const string FormNewNc = "@NEW";

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

            // ── Hàng tốt ─────────────────────────────────────────────────────
            if (r.Kind == HandoverForm.Tot)
            {
                if (act != HandoverForm.AGiao) return (false, BadAct);
                if (r.Qty <= 0) return (false, "Số lượng phải lớn hơn 0.");
                if (pos.LamHo)   // hàng Gia công giao theo NC đang giữ → trả về Gia công (K3: bắt buộc NC nhận)
                    return await IssueWithPurposeOnceAsync(id, g, "GC", r.Qty, uid, HandoverPurpose.HelpReturn, null, ToNcFor("GC"), notes, null, ftx: ftx);
                if (to == null) return (false, "Chọn nhóm nhận.");
                if (pos.HelpFrom != null)   // (a) Gia công giao theo NC cho nhóm khác làm hộ (N3, N4: ghi chú bắt buộc)
                    return await IssueWithPurposeOnceAsync(id, g, to, r.Qty, uid, HandoverPurpose.HelpSend, pos.HelpFrom, null, notes, null, ftx: ftx);
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
                            return await RepairInFormAsync(r, g, HandoverCell.Loi, cs, r.Pass, 0, notes, ftx);
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
                    case HandoverForm.ASua:
                        return await RepairInFormAsync(r, g, HandoverCell.Sua, cs, r.Pass, r.Fail, notes, ftx);
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

            // ── Hàng lỗi mới phát hiện ──────────────────────────────────────
            if (!pos.Fresh || pos.NgSource == null) return (false, BadAct);
            if (act is not (HandoverForm.AGiao or HandoverForm.ASua or HandoverForm.AGiu or HandoverForm.AHuy or HandoverForm.AKhach)) return (false, BadAct);
            decimal n = act == HandoverForm.ASua ? r.Pass + r.Fail : r.Qty;
            if (n <= 0) return (false, act == HandoverForm.ASua ? "Nhập số sửa đạt hoặc không đạt." : "Số lượng phải lớn hơn 0.");
            string origin = r.Prev ? HandoverNgSource.DauVao : HandoverNgSource.DauRa;
            string? cause = !r.Prev ? null : g == "KHO" ? "KHACH" : (string.IsNullOrWhiteSpace(r.Cause) ? HandoverForm.PrevGroup(g) : r.Cause.Trim());   // (c) Kho: lỗi có sẵn = phôi khách

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
            var (err, add) = HandoverForm.CheckNextNc(now, def, chosen, r.NcReason, free: caseKey == 0);   // [GN-FORM1] vòng 3 C1b
            if (err != null) return (err, null, notes);
            if (add != null) { notes = notes == null ? add : add + " | " + notes; if (notes.Length > 500) notes = notes[..500]; }
            return (null, chosen, notes);
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
                    toNc = now == null ? null : HandoverForm.CaseNextNc(now, cs ?? 0, g);
                }
                else toNc = r.NcNext.Trim();
            }
            return await CreateRepairResultOnceAsync(r.KhPlanDetailId, g, cell, cs, pass, fail, toNc, notes, r.UserId, r.NcReason, ftx);
        }
    }
}
