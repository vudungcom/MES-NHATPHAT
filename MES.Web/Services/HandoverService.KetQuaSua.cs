using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [GC-SUA-TAI-CHO 2026-11-04] KẾT QUẢ SỬA + KCS XÁC NHẬN HÀNG SỬA
    //   Khách chốt 04/11 14:53: nhóm nào cũng có thể sửa tại chỗ (kể cả Kho) hàng nhóm khác gửi nhờ sửa (ô Hàng sửa)
    //   hoặc hàng lỗi của mình (ô Hàng lỗi); hàng sửa ĐẠT phải có KCS xác nhận rồi mới làm tiếp; KHÔNG ĐẠT theo quy trình
    //   lỗi bình thường (ở lại ô Hàng lỗi của nhóm, cùng vụ). Hùng chốt 04/11 16:11: 2 bảng mới (SQL 08), người KCS xác nhận
    //   phải khác người ghi Kết quả sửa.
    //   Trước: nhóm không tự trả cho chính mình được (CheckRoute chặn nhóm giao = nhóm nhận) → phải vòng qua nhóm khác,
    //   hoặc đổi ô lúc nhận (mất vết vụ gốc khi sửa hỏng).
    //   Số: chỉ sổ chung HandoverLedgerService tính (xem Compute "Kết quả sửa"). Mọi lưu / huỷ: khoá PO MES_PO_ + K5 trong
    //   cùng giao dịch, tự thử lại khi deadlock, quyền đọc ma trận DB (không tin trang), giờ máy chủ, chỉ ghi thêm.
    // =====================================================================

    /// <summary>[GC-SUA-TAI-CHO] 1 dòng Kết quả sửa cho Lịch sử phiếu / modal KCS xác nhận.</summary>
    public class HandoverRepairItem
    {
        public long      Id            { get; set; }
        public int       KhPlanDetailId { get; set; }
        public string    GroupCode     { get; set; } = string.Empty;
        public string    SourceCell    { get; set; } = string.Empty;
        public long?     NgCaseId      { get; set; }
        public string    CaseLabel     { get; set; } = string.Empty;
        public decimal   QtyPass       { get; set; }
        public decimal   QtyFail       { get; set; }
        public string?   ToNC          { get; set; }
        public string?   Notes         { get; set; }
        public int       CreatedBy     { get; set; }
        public string    CreatedByName { get; set; } = string.Empty;
        public DateTime  CreatedAt     { get; set; }
        public bool      IsVoided      { get; set; }
        public string?   VoidReason    { get; set; }
        public string    VoidedByName  { get; set; } = string.Empty;
        public DateTime? VoidedAt      { get; set; }
        public List<HandoverRepairCheckItem> Checks { get; set; } = new();
        /// <summary>Số KCS đã xác nhận (đạt + loại) ở các lần chưa huỷ.</summary>
        public decimal   Checked       => Checks.Where(c => !c.IsVoided).Sum(c => c.QtyOk + c.QtyNg);
        /// <summary>Số đạt còn chờ KCS xác nhận (chứng từ đã huỷ: 0).</summary>
        public decimal   Waiting       => IsVoided ? 0 : QtyPass - Checked;
    }

    /// <summary>[GC-SUA-TAI-CHO] 1 lần KCS xác nhận.</summary>
    public class HandoverRepairCheckItem
    {
        public long      Id            { get; set; }
        public decimal   QtyOk         { get; set; }
        public decimal   QtyNg         { get; set; }
        public string?   Notes         { get; set; }
        public int       CheckedBy     { get; set; }
        public string    CheckedByName { get; set; } = string.Empty;
        public DateTime  CheckedAt     { get; set; }
        public bool      IsVoided      { get; set; }
        public string?   VoidReason    { get; set; }
        public string    VoidedByName  { get; set; } = string.Empty;
        public DateTime? VoidedAt      { get; set; }
    }

    public partial class HandoverService
    {
        /// <summary>[GC-SUA-TAI-CHO] Nhóm phải xác nhận hàng sửa đạt.</summary>
        public const string RepairCheckGroup = "KCS";

        /// <summary>[GC-SUA-TAI-CHO] Huỷ Kết quả sửa: người ghi, hoặc người có quyền Sửa nhóm sửa.</summary>
        public static bool CanVoidRepair(int createdBy, string groupCode, int userId, IReadOnlyCollection<string> editGroups)
            => createdBy == userId || editGroups.Contains(groupCode);

        /// <summary>[GC-SUA-TAI-CHO] Huỷ lần KCS xác nhận: người xác nhận, hoặc người có quyền Sửa nhóm KCS.</summary>
        public static bool CanVoidRepairCheck(int checkedBy, int userId, IReadOnlyCollection<string> editGroups)
            => checkedBy == userId || editGroups.Contains(RepairCheckGroup);

        // ----------------------------------------------------------------
        // Ghi Kết quả sửa
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> CreateRepairResultAsync(
            int khPlanDetailId, string groupCode, string sourceCell, long? ngCaseId,
            decimal qtyPass, decimal qtyFail, string? toNC, string? notes, int userId,
            string? ncReason = null)   // [GN-FORM1][K2] lý do khi làm lại từ NC đứng trước NC của vụ
            => RetryDeadlockAsync(() => CreateRepairResultOnceAsync(khPlanDetailId, groupCode, sourceCell, ngCaseId, qtyPass, qtyFail, toNC, notes, userId, ncReason));

        private async Task<(bool Ok, string Error)> CreateRepairResultOnceAsync(
            int khPlanDetailId, string groupCode, string sourceCell, long? ngCaseId,
            decimal qtyPass, decimal qtyFail, string? toNC, string? notes, int userId,
            string? ncReason = null,
            FormTx? ftx = null)   // [GN-FORM1] chạy trong giao dịch của form "Giao / xử lý hàng" (đã khoá PO; K5 + Commit ở SaveFormAsync)
        {
            toNC = HandoverLedgerService.TrimCode(toNC);   // [WTS-MA-TRIM]
            if (!HandoverLedgerService.GroupCodes.Contains(groupCode)) return (false, "Mã nhóm không hợp lệ.");
            var deny = await DenyGroupAsync(userId, groupCode, "ghi Kết quả sửa");
            if (deny != null) return (false, deny);
            if (sourceCell is not (HandoverCell.Sua or HandoverCell.Loi))
                return (false, "Chọn hàng sửa lấy từ ô Hàng sửa (nhóm khác gửi nhờ sửa) hay ô Hàng lỗi (hàng lỗi của nhóm).");
            if (qtyPass < 0 || qtyFail < 0) return (false, "Số lượng không được âm.");
            if (QtyRule.CheckWhole(qtyPass, qtyFail) is string eWhole) return (false, eWhole);   // [B3-SO-NGUYEN 2026-11-06] chỉ số nguyên (trước: tối đa 2 số lẻ — cột decimal(10,2), rà độc lập T5)
            if (qtyPass + qtyFail <= 0) return (false, "Nhập số sửa đạt hoặc không đạt.");
            if (sourceCell == HandoverCell.Loi && qtyFail > 0)
                return (false, "Lấy từ ô Hàng lỗi: chiếc sửa không đạt vẫn nằm ở ô Hàng lỗi — chỉ ghi số đạt.");
            toNC = string.IsNullOrWhiteSpace(toNC) ? null : toNC.Trim();
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (notes != null && notes.Length > 500) notes = notes[..500];

            using var trx = ftx == null ? await _db.Database.BeginTransactionAsync() : null;
            if (ftx == null)   // [GN-FORM1] form đã mở giao dịch + khoá PO
            {
                var lockErr = await LockPoAsync(khPlanDetailId);
                if (lockErr != null) return (false, lockErr);
            }
            var row = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
            if (row == null) return (false, "Không tìm thấy PO.");

            // Vụ lấy hàng: thuộc PO này; không chọn vụ = hàng lỗi cũ chưa có mã (chỉ khi còn)
            if (ngCaseId != null)
            {
                if (ngCaseId.Value <= 0 || !row.Cases.ContainsKey(ngCaseId.Value))
                    return (false, $"{NgCaseNo.Format(ngCaseId.Value)} không thuộc PO này.");
            }
            if (row.ApplyWorkflow && groupCode != "GC" && ngCaseId is long lhCs && row.Cases.TryGetValue(lhCs, out var lhC) && lhC.IsLamHo)   // [TARO-LAMHO-NC] vụ hàng làm hộ: chỉ sửa ở Gia công · [LAMHO-OFF] chỉ Workflow ON (OFF: sổ cảnh báo)
                return (false, $"{NgCaseNo.Format(lhCs)} là {lhC.LamHoTen} (chiếc của chuỗi Gia công): sửa ở Gia công — giao trả Gia công, không sửa tại {HandoverLedgerService.GroupLabel(groupCode)}.");
            if (ngCaseId == null && row.CaseHeld(0, groupCode, sourceCell) <= 0)
                return (false, $"Chọn {NgCaseNo.Term} đang giữ ở ô {HandoverCell.Label(sourceCell)} của {HandoverLedgerService.GroupLabel(groupCode)}.");

            // [SUA-XL-GIU][SUA-OK-NC][SUA-KHONG-LUI 2026-11-10] Đích hàng đạt theo vụ (HandoverForm.RepairTarget): nhóm sửa đã làm xong chiếc trước lúc phát hiện
            //   → "@XONG" (KCS xác nhận: nơi phát hiện = Đã làm; nhóm đứng trước nơi phát hiện = Tồn hàng lỗi đã xử lý); Gia công làm hỏng ở NC k → "@OK" (xong NC k).
            //   Workflow ON: không chọn khác đích (không làm lại NC trước); OFF: vẫn cho chọn NC như cũ (K2, lùi + lý do).
            //   Nguồn: KHÁCH 10/11 vòng 1 (hỏi lại 2.2 A, hỏi tiếp 2.1 A); HÙNG 10/11 19:22 OK thiết kế Gói 15.
            var tgt = qtyPass > 0 ? HandoverForm.RepairTarget(row, ngCaseId ?? 0, groupCode) : null;
            bool markOk = false;
            if (groupCode != "GC" || qtyPass <= 0)
                toNC = qtyPass > 0 && tgt == HandoverLedgerService.RepXong ? HandoverLedgerService.RepXong : null;   // nhóm khác: chỉ có dấu, không chọn NC
            else if (HandoverLedgerService.IsRepMark(tgt))
            {
                if (toNC == tgt) markOk = true;
                else if (row.ApplyWorkflow)
                    return (false, HandoverForm.SuaKhongLuiMsg(tgt == HandoverLedgerService.RepOk ? "NC của chiếc lỗi (tính là đã xong NC đó)" : "Gia công đã làm xong (không làm lại NC)"));
            }
            if (markOk || HandoverLedgerService.IsRepMark(toNC) && groupCode != "GC") { }   // dấu: không kiểm NC
            else if (HandoverLedgerService.IsRepMark(toNC))
                return (false, "Đích hàng sửa đạt không hợp lệ cho vụ này — chọn lại.");   // dấu gửi sai vụ (vd vụ đổi) — rà độc lập
            else if (toNC != null)
            {
                var gcSteps = row.G("GC").Steps;
                if (HandoverLedgerService.IsVao(toNC) || !gcSteps.Any(s => !s.IsExcluded && s.Code == toNC))
                    return (false, HandoverLedgerService.IsVao(toNC)
                        ? $"\"{HandoverLedgerService.VaoLabel}\" không phải NC làm tiếp — chọn \"như hàng mới\" nếu hàng đạt quay lại từ đầu."
                        : $"NC {toNC} không phải NC nhận hàng của PO này (không có trong quy trình hoặc là máy loại trừ). Chọn lại NC làm tiếp.");
            }
            // [GN-FORM1][K2 2026-11-05] Nguồn: KHÁCH 05/11 15:39 ("sửa xong làm tiếp NC2 chứ không phải NC1 hay NC3"), HÙNG 17:35 duyệt.
            //   NC làm tiếp của hàng đạt = NC của vụ (chiếc đang chờ NC nào thì làm tiếp NC đó); chỉ cho làm lại từ NC ĐỨNG TRƯỚC + lý do
            //   (ghi vào Ghi chú để truy); NC sau bị chặn (bỏ qua NC = mất truy vết công đoạn). Trước: chọn NC nào cũng lưu.
            if (groupCode == "GC" && qtyPass > 0 && !markOk)
            {
                var defNc = HandoverForm.CaseNextNc(row, ngCaseId ?? 0, groupCode);   // [GN-FORM1] vòng 2 C1: nhóm giữ (vụ 0) · [SUA-XL-GIU] dấu chỉ khi Workflow OFF chọn NC khác
                var (ncErr, ncNote) = HandoverForm.CheckNextNc(row, defNc, toNC, ncReason, free: (ngCaseId ?? 0) == 0,
                    allowEarlier: !row.ApplyWorkflow);   // [SUA-KHONG-LUI] ON: không làm lại NC trước
                if (ncErr != null) return (false, ncErr);
                if (ncNote != null)
                {
                    notes = notes == null ? ncNote : ncNote + " | " + notes;
                    if (notes.Length > 500) notes = notes[..500];
                }
            }

            var (max, ex) = HandoverLedgerService.RepairCap(row, groupCode, sourceCell, ngCaseId);
            if (qtyPass + qtyFail > max)
                return (false, $"Ô {HandoverCell.Label(sourceCell)}: tối đa {max:0.##} ({ex.Title}{(string.IsNullOrEmpty(ex.Arithmetic) ? "" : ": " + ex.Arithmetic)}). Không thể ghi {qtyPass + qtyFail:0.##}.");

            _db.HandoverRepairResults.Add(new HandoverRepairResult
            {
                KhPlanDetailId = khPlanDetailId,
                GroupCode      = groupCode,
                SourceCell     = sourceCell,
                NgCaseId       = ngCaseId,
                QtyPass        = qtyPass,
                QtyFail        = qtyFail,
                ToNC           = toNC,
                Notes          = notes,
                CreatedBy      = userId,
                CreatedAt      = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            if (ftx != null) return (true, string.Empty);   // [GN-FORM1] K5 + Commit ở SaveFormAsync
            var k8 = await CheckNoNewNegativeAsync(khPlanDetailId, row);   // [GN-K8][K5]
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }   // không Commit → rollback
            await trx!.CommitAsync();
            HandoverChangeNotifier.Notify(khPlanDetailId);   // [GN-LIVE]
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // KCS xác nhận (từng phần được)
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> ConfirmRepairAsync(long repairId, decimal qtyOk, decimal qtyNg, string? notes, int userId)
            => RetryDeadlockAsync(() => ConfirmRepairOnceAsync(repairId, qtyOk, qtyNg, notes, userId));

        private async Task<(bool Ok, string Error)> ConfirmRepairOnceAsync(long repairId, decimal qtyOk, decimal qtyNg, string? notes, int userId)
        {
            if (qtyOk < 0 || qtyNg < 0) return (false, "Số lượng không được âm.");
            if (QtyRule.CheckWhole(qtyOk, qtyNg) is string eWhole) return (false, eWhole);   // [B3-SO-NGUYEN 2026-11-06] chỉ số nguyên (trước: tối đa 2 số lẻ, rà độc lập T5)
            if (qtyOk + qtyNg <= 0) return (false, "Nhập số KCS xác nhận đạt hoặc loại.");
            var head = await _db.HandoverRepairResults.AsNoTracking().Where(x => x.HandoverRepairResultId == repairId && !x.IsVoided)
                .Select(x => new { x.KhPlanDetailId, x.CreatedBy }).FirstOrDefaultAsync();
            if (head == null) return (false, "Kết quả sửa không tồn tại hoặc đã huỷ.");
            var deny = await DenyGroupAsync(userId, RepairCheckGroup, "xác nhận hàng sửa");
            if (deny != null) return (false, deny);
            if (head.CreatedBy == userId)
                return (false, "Người KCS xác nhận phải khác người ghi Kết quả sửa.");
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (notes != null && notes.Length > 500) notes = notes[..500];

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(head.KhPlanDetailId);
            if (before == null) return (false, "Không tìm thấy PO.");
            // Đọc lại dưới khoá (người khác có thể vừa huỷ / xác nhận)
            var rep = await _db.HandoverRepairResults.AsNoTracking().Where(x => x.HandoverRepairResultId == repairId && !x.IsVoided)
                .Select(x => new { x.QtyPass }).FirstOrDefaultAsync();
            if (rep == null) return (false, "Kết quả sửa không tồn tại hoặc đã huỷ.");
            var done = await _db.HandoverRepairChecks.AsNoTracking().Where(c => c.HandoverRepairResultId == repairId && !c.IsVoided)
                .Select(c => c.QtyOk + c.QtyNg).ToListAsync();
            var waiting = rep.QtyPass - done.Sum();
            if (qtyOk + qtyNg > waiting)
                return (false, $"Còn chờ KCS xác nhận {Math.Max(0, waiting):0.##} chiếc — không thể xác nhận {qtyOk + qtyNg:0.##}.");

            _db.HandoverRepairChecks.Add(new HandoverRepairCheck
            {
                HandoverRepairResultId = repairId,
                QtyOk     = qtyOk,
                QtyNg     = qtyNg,
                Notes     = notes,
                CheckedBy = userId,
                CheckedAt = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            var k8 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before);   // [GN-K8][K5]
            if (k8 != null) { _db.ChangeTracker.Clear(); return (false, k8); }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // Huỷ Kết quả sửa (chỉ khi không còn lần KCS xác nhận chưa huỷ)
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> VoidRepairResultAsync(long repairId, string voidReason, int userId)
            => RetryDeadlockAsync(() => VoidRepairResultOnceAsync(repairId, voidReason, userId));

        private async Task<(bool Ok, string Error)> VoidRepairResultOnceAsync(long repairId, string voidReason, int userId)
        {
            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)
                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");
            var head = await _db.HandoverRepairResults.AsNoTracking().Where(x => x.HandoverRepairResultId == repairId && !x.IsVoided)
                .Select(x => new { x.KhPlanDetailId, x.CreatedBy, x.GroupCode }).FirstOrDefaultAsync();
            if (head == null) return (false, "Kết quả sửa không tồn tại hoặc đã huỷ.");
            var rights = await VoidRightsAsync(userId);
            if (rights == null) return (false, VoidInactiveMsg);
            if (!CanVoidRepair(head.CreatedBy, head.GroupCode, userId, rights))
                return (false, $"Chỉ người đã ghi hoặc nhóm {HandoverLedgerService.GroupLabel(head.GroupCode)} mới huỷ được Kết quả sửa này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var before = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(head.KhPlanDetailId);
            var rep = await _db.HandoverRepairResults.FirstOrDefaultAsync(x => x.HandoverRepairResultId == repairId && !x.IsVoided);
            if (rep == null) return (false, "Kết quả sửa không tồn tại hoặc đã huỷ.");
            var nCheck = await _db.HandoverRepairChecks.CountAsync(c => c.HandoverRepairResultId == repairId && !c.IsVoided);
            if (nCheck > 0)
                return (false, $"Kết quả sửa #{repairId} đã có {nCheck} lần KCS xác nhận — huỷ các lần xác nhận đó trước (Lịch sử phiếu).");
            rep.IsVoided   = true;
            rep.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();
            rep.VoidedBy   = userId;
            rep.VoidedAt   = DateTime.Now;
            await _db.SaveChangesAsync();
            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before);   // [K5] vd hàng không đạt đã đem đi / huỷ
                if (k5 != null)
                {
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB] trả giao dịch ngay trước khi tìm gốc rễ
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidRepair(repairId),
                        new HashSet<string> { "K:" + repairId }, new[] { $"Huỷ Kết quả sửa #{repairId}" });
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // Huỷ 1 lần KCS xác nhận
        // ----------------------------------------------------------------
        public Task<(bool Ok, string Error)> VoidRepairCheckAsync(long checkId, string voidReason, int userId)
            => RetryDeadlockAsync(() => VoidRepairCheckOnceAsync(checkId, voidReason, userId));

        private async Task<(bool Ok, string Error)> VoidRepairCheckOnceAsync(long checkId, string voidReason, int userId)
        {
            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 3)
                return (false, "Lý do huỷ phải có ít nhất 3 ký tự.");
            var head = await _db.HandoverRepairChecks.AsNoTracking().Where(c => c.HandoverRepairCheckId == checkId && !c.IsVoided)
                .Select(c => new { c.CheckedBy, c.RepairResult.KhPlanDetailId, c.HandoverRepairResultId }).FirstOrDefaultAsync();
            if (head == null) return (false, "Lần KCS xác nhận không tồn tại hoặc đã huỷ.");
            var rights = await VoidRightsAsync(userId);
            if (rights == null) return (false, VoidInactiveMsg);
            if (!CanVoidRepairCheck(head.CheckedBy, userId, rights))
                return (false, "Chỉ người đã xác nhận hoặc nhóm KCS mới huỷ được lần xác nhận này.");

            using var trx = await _db.Database.BeginTransactionAsync();
            var lockErr = await LockPoAsync(head.KhPlanDetailId);
            if (lockErr != null) return (false, lockErr);
            var beforeIn = await new HandoverLedgerService(_db).GetInputForDetailAsync(head.KhPlanDetailId);   // [K5-VONG-SUA] huỷ chứng từ nguồn: xét thêm theo thời gian hàng sửa trả về
            var before = beforeIn == null ? null : HandoverLedgerService.Compute(beforeIn);
            var chk = await _db.HandoverRepairChecks.FirstOrDefaultAsync(c => c.HandoverRepairCheckId == checkId && !c.IsVoided);
            if (chk == null) return (false, "Lần KCS xác nhận không tồn tại hoặc đã huỷ.");
            chk.IsVoided   = true;
            chk.VoidReason = voidReason.Trim().Length > 200 ? voidReason.Trim()[..200] : voidReason.Trim();
            chk.VoidedBy   = userId;
            chk.VoidedAt   = DateTime.Now;
            await _db.SaveChangesAsync();
            if (before != null)
            {
                var k5 = await CheckNoNewNegativeAsync(head.KhPlanDetailId, before, beforeIn);   // [K5] hàng đạt đã nhập WTS / đã giao đi thì không huỷ được
                if (k5 != null)
                {
                    _db.ChangeTracker.Clear();
                    await trx.RollbackAsync();
                    await trx.DisposeAsync();   // [WHY-TRAN-DB]
                    var whyTxt = await HandoverWhy.ExplainAsync(_db, head.KhPlanDetailId, HandoverWhy.OpVoidRepairCheck(checkId),
                        new HashSet<string> { "X:" + checkId }, new[] { $"Huỷ lần KCS xác nhận #{checkId}" }, loopOp: true);
                    return (false, k5 + whyTxt);
                }
            }
            await trx.CommitAsync();
            HandoverChangeNotifier.Notify(head.KhPlanDetailId);
            _db.ChangeTracker.Clear();
            return (true, string.Empty);
        }

        // ----------------------------------------------------------------
        // Đọc: Lịch sử / modal KCS
        // ----------------------------------------------------------------
        public async Task<List<HandoverRepairItem>> GetRepairsAsync(int khPlanDetailId)
        {
            var list = await _db.HandoverRepairResults.AsNoTracking()
                .Where(x => x.KhPlanDetailId == khPlanDetailId)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.HandoverRepairResultId)
                .Select(x => new HandoverRepairItem
                {
                    Id = x.HandoverRepairResultId, KhPlanDetailId = x.KhPlanDetailId, GroupCode = x.GroupCode, SourceCell = x.SourceCell,
                    NgCaseId = x.NgCaseId, QtyPass = x.QtyPass, QtyFail = x.QtyFail, ToNC = x.ToNC, Notes = x.Notes,
                    CreatedBy = x.CreatedBy,
                    CreatedByName = x.CreatedByUser != null ? (x.CreatedByUser.FullName ?? x.CreatedByUser.Username) : "",
                    CreatedAt = x.CreatedAt, IsVoided = x.IsVoided, VoidReason = x.VoidReason,
                    VoidedByName = x.VoidedByUser != null ? (x.VoidedByUser.FullName ?? x.VoidedByUser.Username) : "",
                    VoidedAt = x.VoidedAt
                })
                .ToListAsync();
            if (list.Count == 0) return list;
            var ids = list.Select(x => x.Id).ToList();
            var checks = await _db.HandoverRepairChecks.AsNoTracking()
                .Where(c => ids.Contains(c.HandoverRepairResultId))
                .OrderBy(c => c.CheckedAt).ThenBy(c => c.HandoverRepairCheckId)
                .Select(c => new
                {
                    c.HandoverRepairResultId,
                    Item = new HandoverRepairCheckItem
                    {
                        Id = c.HandoverRepairCheckId, QtyOk = c.QtyOk, QtyNg = c.QtyNg, Notes = c.Notes, CheckedBy = c.CheckedBy,
                        CheckedByName = c.CheckedByUser != null ? (c.CheckedByUser.FullName ?? c.CheckedByUser.Username) : "",
                        CheckedAt = c.CheckedAt, IsVoided = c.IsVoided, VoidReason = c.VoidReason,
                        VoidedByName = c.VoidedByUser != null ? (c.VoidedByUser.FullName ?? c.VoidedByUser.Username) : "",
                        VoidedAt = c.VoidedAt
                    }
                })
                .ToListAsync();
            foreach (var r in list) r.Checks = checks.Where(c => c.HandoverRepairResultId == r.Id).Select(c => c.Item).ToList();
            var caseLbl = await NgCaseService.GetCaseLabelsAsync(_db, list.Select(x => x.NgCaseId));
            foreach (var r in list)
                r.CaseLabel = r.NgCaseId is long v && v > 0
                    ? NgCaseNo.Format(v) + (caseLbl.TryGetValue(v, out var l) ? " · " + l : "")
                    : "Hàng lỗi cũ (chưa có mã vụ)";
            return list;
        }

        /// <summary>[GC-SUA-TAI-CHO] Số Kết quả sửa còn chờ KCS xác nhận theo PO (số trên nút Nhận của người có quyền KCS).</summary>
        public async Task<Dictionary<int, int>> GetRepairPendingCountPerDetailAsync(IEnumerable<int> detailIds)
        {
            var ids = detailIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, int>();
            var reps = await _db.HandoverRepairResults.AsNoTracking()
                .Where(x => ids.Contains(x.KhPlanDetailId) && !x.IsVoided && x.QtyPass > 0)
                .Select(x => new { x.HandoverRepairResultId, x.KhPlanDetailId, x.QtyPass })
                .ToListAsync();
            if (reps.Count == 0) return new Dictionary<int, int>();
            var rIds = reps.Select(x => x.HandoverRepairResultId).ToList();
            var done = (await _db.HandoverRepairChecks.AsNoTracking()
                    .Where(c => rIds.Contains(c.HandoverRepairResultId) && !c.IsVoided)
                    .Select(c => new { c.HandoverRepairResultId, Q = c.QtyOk + c.QtyNg })
                    .ToListAsync())
                .GroupBy(c => c.HandoverRepairResultId).ToDictionary(g => g.Key, g => g.Sum(x => x.Q));
            return reps.Where(r => r.QtyPass - (done.TryGetValue(r.HandoverRepairResultId, out var d) ? d : 0m) > 0)
                       .GroupBy(r => r.KhPlanDetailId).ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
