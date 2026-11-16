using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MES.Web.Services;

// =====================================================================
// [UI-SAFE 2026-11-16] Bọc sự kiện người dùng trên trang Blazor Server (Gói 23, nhóm B2 quét Fable)
//   Trước: nhiều handler `await` đọc / ghi DB không try/catch (Giao nhận mở Lịch sử / đổi tháng, WTS Lưu nhóm / Sửa / Xoá dòng,
//   Kế hoạch bung "Đang ở đâu", Kho Lịch sử, Khách hàng Sửa, Cài đặt thêm User, Ca làm việc Sửa, WTS tiêu chuẩn…) → SQL timeout /
//   mất kết nối / chữ dài quá cột → ngoại lệ thoát handler → mạch Blazor chết "An unhandled error has occurred", mất phiên + ô đang gõ.
//   Nay: trang gọi UiSafe.RunAsync(...) → lỗi nghiệp vụ (service ném InvalidOperationException chữ Việt) hiện nguyên;
//   lỗi hệ thống (SqlException, DbUpdateException, EF "second operation", timeout…) → câu Việt chung + ghi ILogger, KHÔNG hiện
//   ex.Message (SqlException lộ tên bảng / cột — C-11). Không đổi luật, không đổi số: chỉ đổi cách báo lỗi.
//   Nguồn: CLAUDE quét Fable 15/11 (B-H BH-03, B-W1 BW1-03, B-W2 BW2-01, B-K BK-05, B-M2 BM2-01/03/05, B-P1 BP1-06, B-P2 BP2-01,
//   B-M1 BM1-21 — nhóm B2 Trung); Hùng 16/11 05:15 "không ảnh hưởng khách thì làm theo ý bạn".
// [C-11 2026-11-16] Gói 24: 35 chỗ trang + 5 chỗ service CŨ còn ghép ex.Message / InnerException.Message vào câu báo → đổi hết sang UiSafe.Text
//   (grep "ex.Message" trong Components chỉ còn chỗ lọc lỗi nghiệp vụ). Nguồn: CLAUDE quét Fable 15/11 (A3C C-11 Thấp); Hùng 16/11 11:34 OK Gói 24.
// =====================================================================
public static class UiSafe
{
    /// <summary>Chạy <paramref name="work"/>; xong trả null, lỗi trả câu báo cho người dùng (xem <see cref="Text"/>).</summary>
    public static async Task<string?> RunAsync(Func<Task> work, ILogger? log = null, string viec = "thực hiện thao tác")
    {
        try { await work(); return null; }
        catch (Exception ex) { return Text(ex, log, viec); }
    }

    /// <summary>Câu báo cho người dùng từ 1 ngoại lệ: lỗi nghiệp vụ (chữ Việt do service ném) hiện nguyên; lỗi hệ thống → câu chung + log.</summary>
    /// <summary>[C-11 2026-11-16] Logger dùng khi chỗ gọi không có ILogger (service tạo bằng new, không qua DI) — Program.cs gán; null thì chỉ bỏ qua log.</summary>
    public static ILogger? Fallback { get; set; }

    public static string Text(Exception ex, ILogger? log = null, string viec = "thực hiện thao tác")
    {
        log ??= Fallback;   // [C-11]
        if (ex is AggregateException ag && ag.InnerException != null) ex = ag.InnerException;
        if (IsBusiness(ex)) return ex.Message;
        if (HandoverService.IsDeadlock(ex)) return $"Chưa {viec} được — dữ liệu đang có người khác lưu cùng lúc (máy chủ chọn huỷ lượt này). Chưa ghi gì, thử lại sau vài giây.";   // rà độc lập 16/11 L5: không dùng câu "đã tự thử lại 3 lần" của Giao nhận (chỗ này không có vòng thử lại)
        try { log?.LogError(ex, "[UI-SAFE] Lỗi hệ thống khi {Viec}", viec); } catch { /* log không được cũng không làm chết trang */ }
        return $"Chưa {viec} được — máy chủ đang bận hoặc lỗi kết nối dữ liệu. Thử lại sau vài giây; vẫn lỗi thì báo quản trị (đã ghi nhật ký máy chủ lúc {DateTime.Now:HH:mm:ss}).";
    }

    /// <summary>Lỗi nghiệp vụ = ngoại lệ do code MES ném với câu chữ Việt: InvalidOperationException (PermCheck.NoEdit, LenCheck.Max, luật…),
    /// UnauthorizedAccessException (PartMasterService "Bạn không có quyền sửa vùng…"), ArgumentException có chữ Việt (ValidateReason, "Part No không được để trống").
    /// Ngoại lệ EF cũng là InvalidOperationException ("A second operation was started…") nhưng ném từ Microsoft.* và không có dấu tiếng Việt → lỗi hệ thống.
    /// Rà độc lập 16/11 T3: trước chỉ xét InvalidOperationException → lỗi quyền / lỗi tham số của Part Master bị nuốt thành câu "máy chủ bận".</summary>
    public static bool IsBusiness(Exception ex)
    {
        if (ex is not (InvalidOperationException or UnauthorizedAccessException or ArgumentException)) return false;
        if (ex is ArgumentNullException) return false;
        var ns = ex.TargetSite?.DeclaringType?.Namespace ?? "";
        if (ex is InvalidOperationException && ns.StartsWith("MES.Web", StringComparison.Ordinal)) return true;
        return VietRx.IsMatch(ex.Message ?? "");
    }

    static readonly Regex VietRx = new("[ăâđêôơưĂÂĐÊÔƠƯàáảãạèéẻẽẹìíỉĩịòóỏõọùúủũụỳýỷỹỵ]", RegexOptions.Compiled);
}
