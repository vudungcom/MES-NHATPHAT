using MES.Web.Data;

namespace MES.Web.Services;

// ================================================================
// [PM-NC-DUP-SVC 2026-11-02] Chặn mã chính trùng / trống ở MÁY CHỦ cho quy trình Part Master
//   (Gia công, Bavia, Rửa, KCS, Đóng gói — Taro làm riêng trong gói TARO-NC-MA vì khách chốt 02/11 giữ mã CV chung nhiều NC).
//   Nguồn: CLAUDE (lỗi ngầm kiem 01/11 17:19, seed 27253 chuoi_loaitru PO16) — Hùng duyệt 02/11 13:16.
//   Trước: chỉ trang Detail_Partmaster chặn ([B6-NC-DUP], xét danh sách đang hiện trên màn hình). 2 người cùng mở 1 Part,
//   mỗi người thêm P9 → 2 dòng chính P9 → snapshot PO mới có mã trùng → sổ trừ NG mã đó 2 lần, trần WTS chỉ thấy 1 dòng → lệch số.
//   Nay: mỗi lần THÊM / SỬA dòng chính chạy trong giao dịch + khoá "MES_PM_STEPS_<PartId>" (sp_getapplock, LockOwner = Transaction),
//   đọc lại DB dưới khoá rồi mới kiểm → 2 người lưu cùng lúc thì người sau thấy dòng người trước vừa ghi.
//   Luật kiểm (như trang): mã = NC đã Trim; trống hoặc "-" = chưa nhập; so không phân biệt hoa thường; dòng dự phòng (IsBackup) không xét.
//   Sửa: chỉ kiểm khi đổi mã hoặc dòng dự phòng thành dòng chính — dữ liệu cũ đã trùng mà không đụng tới mã thì vẫn sửa được ô khác.
// ================================================================

/// <summary>[PM-NC-DUP-SVC] Lỗi mã chính trống / trùng — trang hiện nguyên lời báo.</summary>
public sealed class PartStepCodeException : InvalidOperationException
{
    public PartStepCodeException(string message) : base(message) { }
}

public static class PartStepGuard
{
    public static string LockName(int partId) => "MES_PM_STEPS_" + partId;

    public static string Norm(string? code) => (code ?? "").Trim();

    public static bool IsBlank(string? code)
    {
        var n = Norm(code);
        return n.Length == 0 || n == "-";
    }

    public static bool SameCode(string? a, string? b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    // [PM-NC-AT 2026-11-05] Mã NC / mã CV bắt đầu "@" dành cho mã hệ thống (dòng ảo "@VAO" = "Nhận vào (chưa qua NC)" [N3-FIRST]).
    //   Nguồn: CLAUDE (lỗi ngầm tự phát hiện khi làm N3-FIRST 04/11; Hùng 05/11 10:11 cho làm). Trước: sổ chỉ dựa máy chủ chặn WTS / phiếu
    //   vào "@VAO", Part Master vẫn cho đặt NC "@VAO" → NC thật trùng dòng ảo, sổ nhầm. Áp cho MỌI dòng (cả dự phòng), cả mã CV Taro.
    //   Sửa dòng: chỉ kiểm khi mã đổi — dòng cũ đã có "@" (QuetSo PM_MA_A báo) vẫn sửa được ô khác.
    public static bool IsReserved(string? code) => Norm(code).StartsWith("@", StringComparison.Ordinal);

    public static void CheckReservedCode(string label, params string?[] codes)
    {
        foreach (var c in codes)
            if (IsReserved(c))
                throw new PartStepCodeException($"{label}: mã \"{Norm(c)}\" bắt đầu bằng \"@\" — ký tự @ đầu mã dành cho mã hệ thống (vd \"{HandoverLedgerService.VaoCode}\" = \"{HandoverLedgerService.VaoLabel}\"), đặt mã khác.");
    }

    /// <summary>
    /// Chạy <paramref name="body"/> dưới khoá quy trình của Part. Có giao dịch ngoài thì dùng (khoá nhả khi giao dịch đó kết thúc),
    /// không thì tự mở + Commit / Rollback (như KhPlanService.CreateAsync [RS-E16]).
    /// Lưu ý (rà độc lập 02/11): EF thật không trả trạng thái entity khi Rollback — nơi gọi bắt lỗi rồi dùng tiếp DbContext phải
    /// ChangeTracker.Clear() (trang Detail_Partmaster đã làm [SAVE-HANG-FIX]), không thì SaveChanges sau ghi lại phần dở.
    /// </summary>
    public static async Task<T> RunLockedAsync<T>(AppDbContext db, int partId, Func<Task<T>> body)
    {
        var ownTx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            if (!await PoGuard.TryLockInTransactionAsync(db, LockName(partId)))
                throw new InvalidOperationException("Đang có người khác lưu quy trình của Part này — chờ vài giây rồi bấm lưu lại.");
            var r = await body();
            if (ownTx != null) await ownTx.CommitAsync();
            return r;
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync();
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    /// <summary>Mã dòng chính phải có và không trùng dòng chính khác (đọc từ DB dưới khoá).</summary>
    public static void CheckMainCode(string label, string? code, IEnumerable<string?> otherMainCodes)
    {
        if (IsBlank(code))
            throw new PartStepCodeException($"{label}: dòng chính chưa nhập NC / mã CV — nhập mã hoặc xoá dòng.");
        var n = Norm(code);
        if (otherMainCodes.Any(o => SameCode(o, n)))
            throw new PartStepCodeException($"{label}: NC / mã CV {n} đã có ở dòng chính khác của Part này (có thể vừa được người khác lưu) — mỗi mã chỉ 1 dòng chính (máy khác dùng dòng đồng dạng / dự phòng). Tải lại trang rồi kiểm tra.");
    }

    // [B6-DP-MA 2026-11-06] Luật mã dòng dự phòng (T3 PM-NC-DUP-THAP). Nguồn: CLAUDE (rà độc lập 02/11 T3), Hùng 05/11 15:37 chốt làm theo đề xuất.
    //   Trước: dự phòng không bị kiểm mã — trống vẫn lưu (trang chặn, service không), hoặc mang đúng mã 1 dòng chính khác / dự phòng của dòng cha khác
    //   (vd dự phòng "P2" của P1 khi đã có dòng chính P2) → sổ (CodeMain "nhóm|mã" → mã chính) gộp WTS mã đó về 1 cha, cha kia mất số.
    //   Luật: dự phòng phải có mã (không trống / "-"); mã dự phòng bằng mã CHA thì được (cùng gộp về cha); khác mã cha thì không được trùng mã dòng chính
    //   khác, không trùng mã dự phòng khác. Chiều ngược: mã dòng chính không trùng mã dự phòng của dòng cha khác. Đọc DB dưới khoá MES_PM_STEPS_.
    //   Sửa dòng: chỉ kiểm khi mã / dự phòng / mã cha đổi — dữ liệu cũ không đụng tới mã vẫn sửa được ô khác. Taro không xét (gói TARO-NC-MA).
    public static void CheckBackupRules(string label, string? code, bool isBackup, string? parentCode, IEnumerable<(string? Code, bool Backup, string? Parent)> others)
    {
        var n = Norm(code);
        var list = others.ToList();
        if (isBackup)
        {
            if (IsBlank(code))
                throw new PartStepCodeException($"{label}: dòng dự phòng chưa có NC / mã — nhập mã (vd {(IsBlank(parentCode) ? "<mã cha>" : Norm(parentCode))}-DP-01) hoặc xoá dòng.");
            if (SameCode(n, parentCode)) return;
            if (list.Any(o => !o.Backup && SameCode(o.Code, n)))
                throw new PartStepCodeException($"{label}: mã dự phòng {n} trùng mã dòng chính khác của Part này — WTS mã này sẽ bị gộp nhầm. Đặt mã khác (vd {(IsBlank(parentCode) ? "<mã cha>" : Norm(parentCode))}-DP-01).");
            if (list.Any(o => o.Backup && SameCode(o.Code, n)))
                throw new PartStepCodeException($"{label}: mã dự phòng {n} đã có ở dòng dự phòng khác của Part này (có thể vừa được người khác lưu) — mỗi dòng dự phòng 1 mã riêng. Tải lại trang rồi kiểm tra.");
            return;
        }
        if (!IsBlank(code) && list.Any(o => o.Backup && SameCode(o.Code, n) && !SameCode(o.Parent, n)))
            throw new PartStepCodeException($"{label}: NC / mã {n} đang là mã dòng dự phòng của dòng khác trong Part này — đặt mã khác.");
    }

    /// <summary>[B6-DP-MA] Lúc sửa: kiểm luật dự phòng khi mã, dự phòng hay mã cha đổi.</summary>
    public static bool NeedBackupCheckOnUpdate(string? oldCode, bool oldIsBackup, string? oldParent, string? newCode, bool newIsBackup, string? newParent)
        => oldIsBackup != newIsBackup || !SameCode(oldCode, newCode) || (newIsBackup && !SameCode(oldParent, newParent));

    /// <summary>Lúc sửa: chỉ kiểm khi dòng (sau sửa) là dòng chính VÀ (đổi mã hoặc từ dự phòng thành chính).</summary>
    public static bool NeedCheckOnUpdate(string? oldCode, bool oldIsBackup, string? newCode, bool newIsBackup)
        => !newIsBackup && (oldIsBackup || !SameCode(oldCode, newCode));
}
