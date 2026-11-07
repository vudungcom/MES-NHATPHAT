using MES.Web.Data;
using Microsoft.EntityFrameworkCore;

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

    // [TARO-NC-MA 2026-11-06] Taro: mã CV dùng CHUNG nhiều NC là hợp lệ (khách chốt B 02/11 09:14 — vd 1T / 2T / 3T1 ở cả NC 3 lẫn NC 5);
    //   trùng là CÙNG mã CV ở CÙNG NC (2 dòng chính) → sổ đếm theo cặp (mã, NC) không phân biệt được 2 dòng. Mã CV = WtsTaskCode, trống thì NC
    //   (như sổ). Đọc DB dưới khoá MES_PM_STEPS_. Sửa dòng: chỉ kiểm khi mã CV / NC / dự phòng đổi. Nguồn: KHÁCH 02/11 09:14; HÙNG 05/11 15:37.
    public static string TaroCode(string? wtsTaskCode, string? nc) => Norm(string.IsNullOrWhiteSpace(wtsTaskCode) ? nc : wtsTaskCode);

    public static void CheckTaroMain(string label, string? wtsTaskCode, string? nc, IEnumerable<(string? WtsTaskCode, string? Nc)> otherMains)
    {
        var code = TaroCode(wtsTaskCode, nc);
        if (IsBlank(code))
            throw new PartStepCodeException($"{label}: dòng chính chưa nhập NC / mã CV — nhập mã hoặc xoá dòng.");
        if (otherMains.Any(o => SameCode(TaroCode(o.WtsTaskCode, o.Nc), code) && SameCode(o.Nc, nc)))
            throw new PartStepCodeException($"{label}: mã CV {code} ở NC {Norm(nc)} đã có ở dòng chính khác của Part này (có thể vừa được người khác lưu) — "
                + "mỗi NC chỉ 1 dòng mỗi mã CV (cùng mã ở NC khác thì được). "
                // [TARO-DOI-CHEO-NC 2026-11-07] lời báo khi 2 dòng cùng mã đổi chéo NC trong 1 lần lưu (trang đã chặn trước; còn lọt thì máy chủ nói rõ cách làm)
                + $"Nếu đang đổi chéo NC giữa 2 dòng cùng mã {code} trong 1 lần lưu: đổi 1 dòng sang NC khác, lưu, rồi đổi tiếp. Tải lại trang rồi kiểm tra.");
    }

    // [TARO-DUPHONG 2026-11-07] Taro KHÔNG dùng dòng dự phòng: dự phòng chỉ nhớ dòng cha bằng NC, mà 1 NC Taro có nhiều mã (1T, 2T, 3T1…) → sổ gộp
    //   WTS dự phòng nhầm về mã đầu tiên của NC đó, xoá / đổi NC dòng chính kéo theo dự phòng của mã khác. Chặn thêm dòng dự phòng + đổi dòng chính
    //   thành dự phòng (trang bỏ nút, máy chủ chặn); dòng dự phòng cũ giữ nguyên (sửa tên / giờ chuẩn được, đổi thành dòng chính được).
    //   Nguồn: HÙNG 07/11 09:11 "bỏ dòng dự phòng taro đi nhé, ko cần nữa" (DB MES 07/11: 1 dòng dự phòng Taro, Part test "sssssssss").
    public const string TaroNoBackupMsg = "Taro: không dùng dòng dự phòng (mỗi mã CV mỗi NC 1 dòng chính) — thêm dòng chính cho mã / NC đó, hoặc xoá dòng.";

    // [B6-DP-MA 2026-11-06] Luật mã dòng dự phòng (T3 PM-NC-DUP-THAP). Nguồn: CLAUDE (rà độc lập 02/11 T3), Hùng 05/11 15:37 chốt làm theo đề xuất.
    //   Trước: dự phòng không bị kiểm mã — trống vẫn lưu (trang chặn, service không), hoặc mang đúng mã 1 dòng chính khác / dự phòng của dòng cha khác
    //   (vd dự phòng "P2" của P1 khi đã có dòng chính P2) → sổ (CodeMain "nhóm|mã" → mã chính) gộp WTS mã đó về 1 cha, cha kia mất số.
    //   Luật: dự phòng phải có mã (không trống / "-"); mã dự phòng bằng mã CHA thì được (cùng gộp về cha); khác mã cha thì không được trùng mã dòng chính
    //   khác, không trùng mã dự phòng khác. Chiều ngược: mã dòng chính không trùng mã dự phòng của dòng cha khác. Đọc DB dưới khoá MES_PM_STEPS_.
    //   Sửa dòng: chỉ kiểm khi mã / dự phòng / mã cha đổi — dữ liệu cũ không đụng tới mã vẫn sửa được ô khác. Taro không xét (gói TARO-NC-MA).
    // [B6-DP-THAP 2026-11-06] (rà độc lập gói 1 mục 5 + 7). Nguồn: CLAUDE; Hùng 06/11 10:54 OK kế hoạch A1 (mục 5, 6 làm cùng A1);
    //   mục 7 = vá chặn trùng mã ở máy chủ (luật Hùng 29/10: vá vênh dữ liệu tự làm rồi báo):
    //   (7) dự phòng phải trỏ về 1 DÒNG CHÍNH đang dùng của Part (trước: cha "ZZ" không có / cha là mã của dự phòng khác vẫn lưu → nhánh "mã = mã cha"
    //       thoát sớm, ra 2 dòng cùng mã). Chỉ kiểm khi thêm / đổi mã / đổi cha (dữ liệu cũ mồ côi không đụng tới vẫn sửa được ô khác).
    //   (5) dòng chính đổi tên sang đúng mã dự phòng của CHÍNH NÓ (selfOldCode = mã cũ) không bị chặn oan: sau khi đổi, service kéo cha của dự phòng
    //       theo tên mới → dự phòng có mã = mã cha (hợp lệ).
    public static void CheckBackupRules(string label, string? code, bool isBackup, string? parentCode, IEnumerable<(string? Code, bool Backup, string? Parent)> others, string? selfOldCode = null)
    {
        var n = Norm(code);
        var list = others.ToList();
        if (isBackup)
        {
            if (IsBlank(code))
                throw new PartStepCodeException($"{label}: dòng dự phòng chưa có NC / mã — nhập mã (vd {(IsBlank(parentCode) ? "<mã cha>" : Norm(parentCode))}-DP-01) hoặc xoá dòng.");
            if (IsBlank(parentCode) || !list.Any(o => !o.Backup && SameCode(o.Code, parentCode)))   // [B6-DP-THAP] (7)
                throw new PartStepCodeException($"{label}: dòng dự phòng {n} trỏ về dòng cha {(IsBlank(parentCode) ? "(trống)" : Norm(parentCode))} nhưng Part này không có dòng chính đang dùng mã đó (có thể vừa bị người khác đổi / xoá). Tải lại trang rồi kiểm tra.");
            if (SameCode(n, parentCode)) return;
            if (list.Any(o => !o.Backup && SameCode(o.Code, n)))
                throw new PartStepCodeException($"{label}: mã dự phòng {n} trùng mã dòng chính khác của Part này — WTS mã này sẽ bị gộp nhầm. Đặt mã khác (vd {(IsBlank(parentCode) ? "<mã cha>" : Norm(parentCode))}-DP-01).");
            if (list.Any(o => o.Backup && SameCode(o.Code, n)))
                throw new PartStepCodeException($"{label}: mã dự phòng {n} đã có ở dòng dự phòng khác của Part này (có thể vừa được người khác lưu) — mỗi dòng dự phòng 1 mã riêng. Tải lại trang rồi kiểm tra.");
            return;
        }
        if (!IsBlank(code) && list.Any(o => o.Backup && SameCode(o.Code, n) && !SameCode(o.Parent, n) && !(selfOldCode != null && SameCode(o.Parent, selfOldCode))))   // [B6-DP-THAP] (5)
            throw new PartStepCodeException($"{label}: NC / mã {n} đang là mã dòng dự phòng của dòng khác trong Part này — đặt mã khác.");
    }

    /// <summary>[B6-DP-MA] Lúc sửa: kiểm luật dự phòng khi mã, dự phòng hay mã cha đổi.</summary>
    public static bool NeedBackupCheckOnUpdate(string? oldCode, bool oldIsBackup, string? oldParent, string? newCode, bool newIsBackup, string? newParent)
        => oldIsBackup != newIsBackup || !SameCode(oldCode, newCode) || (newIsBackup && !SameCode(oldParent, newParent));

    /// <summary>Lúc sửa: chỉ kiểm khi dòng (sau sửa) là dòng chính VÀ (đổi mã hoặc từ dự phòng thành chính).</summary>
    public static bool NeedCheckOnUpdate(string? oldCode, bool oldIsBackup, string? newCode, bool newIsBackup)
        => !newIsBackup && (oldIsBackup || !SameCode(oldCode, newCode));

    // ================================================================
    // [PM-SNAP-MID 2026-11-06] (A1) Khoá CẢ LƯỢT LƯU trang Part Master "MES_PM_SAVE_<PartId>".
    //   Vì sao: 1 lần bấm Lưu = nhiều giao dịch nhỏ (mỗi dòng B–E 1 giao dịch, khoá MES_PM_STEPS_ chỉ giữ trong từng dòng). Người khác tạo KH /
    //   bấm "Cập nhật từ Part Master" / tự cập nhật PO chưa chạy / "Update TG chuẩn" chen vào giữa có thể chụp quy trình DỞ (vd đã xoá P9 cũ,
    //   chưa thêm P9 mới) → PO mang quy trình thiếu bước từ đầu. Không dùng 1 giao dịch dài được: chuỗi kết nối MultipleActiveResultSets=true
    //   → EF không có savepoint, lỗi 1 dòng giữa chừng không huỷ riêng dòng đó được.
    //   Cách: trang mở 1 kết nối giữ suốt lượt lưu, sp_getapplock Exclusive LockOwner = Session (chờ tối đa 10 giây); chỗ chụp quy trình lấy cùng
    //   tên khoá Shared LockOwner = Transaction trong giao dịch của nó (chờ tối đa 15 giây) → chờ tới khi lượt lưu xong, thấy bản trọn.
    //   Thứ tự khoá: người lưu giữ MES_PM_SAVE_ rồi mới lấy MES_PM_STEPS_ (không lấy MES_PO_); người chụp lấy MES_PO_ rồi MES_PM_SAVE_ (không lấy
    //   MES_PM_STEPS_) → không vòng chờ. Trang nhả khoá TRƯỚC bước tự cập nhật PO chưa chạy (K6-NOTRUN), để không tự chờ chính mình.
    //   Nguồn: CLAUDE (rà độc lập 02/11 PM-SNAP-MID); Hùng 06/11 10:54 OK kế hoạch A1.
    // ================================================================
    public static string SaveLockName(int partId) => "MES_PM_SAVE_" + partId;
    public const string SaveBusyMsg = "Đang có người khác lưu Part Master của Part này — chờ vài giây rồi bấm lưu lại (chưa lưu gì).";
    public const string SnapBusyMsg = "Part Master của Part này đang được lưu — thử lại sau vài giây (chưa chụp quy trình, không ghi gì).";

    // [S8D-KHOA 2026-11-07] Khoá lượt lưu giữ trên 1 KẾT NỐI RIÊNG KHÔNG QUA POOL (MES.Web gắn SessionLockOpener = PmSaveLockSql.OpenAsync lúc khởi động).
    //   Vì sao: trước, khoá phiên nằm trên kết nối của DbContext trang. Nếu DbContext bị huỷ giữa lượt lưu (mạch Blazor đóng / trang chết đúng lúc
    //   lưu) thì lệnh nhả chạy trên context đã huỷ → hỏng, kết nối về pool vẫn giữ khoá tới khi pool dùng lại kết nối đó (SqlDongThoi S8d 06/11
    //   đo trên SQL thật: "rảnh ngay sau khi huỷ = False") → người khác lưu Part đó bị báo "đang có người lưu" một lúc. Nay khoá không phụ thuộc
    //   DbContext trang: nhả bằng chính kết nối riêng; kết nối không pool nên đóng là phiên SQL kết thúc, khoá tự nhả (cả khi tiến trình app tắt
    //   ngang — TCP đóng). null (bộ kiểm DB giả GiaoNhanFuzz / harness) = cách cũ trên kết nối DbContext.
    //   Nguồn: CLAUDE (rà độc lập gói 2, sổ dòng S8d); Hùng 07/11 07:55 giao Gói 5 sửa.
    public static Func<AppDbContext, string, Task<IAsyncDisposable?>>? SessionLockOpener;

    /// <summary>Khoá lượt lưu đang giữ. DisposeAsync nhả khoá (sp_releaseapplock) rồi đóng kết nối — gọi được nhiều lần.</summary>
    public sealed class SaveLock : IAsyncDisposable
    {
        private readonly AppDbContext? _db; private readonly string _name; private bool _done;
        private readonly IAsyncDisposable? _own;   // [S8D-KHOA] khoá trên kết nối riêng (không pool)
        internal SaveLock(AppDbContext db, string name) { _db = db; _name = name; }
        internal SaveLock(IAsyncDisposable own, string name) { _own = own; _name = name; }   // [S8D-KHOA]
        public async ValueTask DisposeAsync()
        {
            if (_done) return;
            _done = true;
            if (_own != null) { await _own.DisposeAsync(); return; }   // [S8D-KHOA] nhả + đóng kết nối riêng, không đụng DbContext trang
            if (_db == null) return;
            try
            {
                // nhả cả khi còn giao dịch sót (sp_releaseapplock LockOwner = Session chạy được trong giao dịch) — rà độc lập gói 2 T3
                await _db.Database.ExecuteSqlRawAsync("EXEC sp_releaseapplock @Resource = {0}, @LockOwner = 'Session';", _name);
            }
            catch { /* kết nối đã hỏng: SQL Server tự nhả khoá phiên khi phiên đóng (SqlDongThoi S8d đo trên SQL thật) */ }
            finally { await _db.Database.CloseConnectionAsync(); }
        }
    }

    /// <summary>Lấy khoá lượt lưu. null = người khác đang lưu cùng Part quá 10 giây (không lưu gì, báo SaveBusyMsg).</summary>
    public static async Task<SaveLock?> AcquireSaveLockAsync(AppDbContext db, int partId)
    {
        var name = SaveLockName(partId);
        if (SessionLockOpener != null)   // [S8D-KHOA] kết nối riêng không pool (null = người khác giữ quá 10 giây)
        {
            var own = await SessionLockOpener(db, name);
            return own == null ? null : new SaveLock(own, name);
        }
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 10000; " +
                "IF @r < 0 THROW 50002, N'MES_PM_SAVE_BUSY', 1;", name);
            return new SaveLock(db, name);
        }
        catch (Exception ex) when (ex.GetBaseException().Message.Contains("MES_PM_SAVE_BUSY"))
        {
            await db.Database.CloseConnectionAsync();
            return null;
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    /// <summary>Chỗ chụp quy trình Part Master: chờ lượt lưu đang chạy của Part xong (khoá Shared trong giao dịch đang mở, chờ tối đa 15 giây).
    /// Quá giờ → ném InvalidOperationException(SnapBusyMsg) — nơi gọi rollback, không chụp dở.</summary>
    public static async Task LockSnapshotReadAsync(AppDbContext db, int partId, string? partNo = null)   // partNo: ghi tên Part vào lời báo (phiếu KH nhiều Part)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Shared', @LockOwner = 'Transaction', @LockTimeout = 15000; " +
                "IF @r < 0 THROW 50003, N'MES_PM_SAVE_WAIT', 1;", SaveLockName(partId));
        }
        catch (Exception ex) when (ex.GetBaseException().Message.Contains("MES_PM_SAVE_WAIT"))
        {
            throw new InvalidOperationException(partNo == null ? SnapBusyMsg : SnapBusyMsg.Replace("của Part này", "của Part " + partNo));
        }
    }
}
