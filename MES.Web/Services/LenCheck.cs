namespace MES.Web.Services;

// =====================================================================
// [LEN-CHECK 2026-11-16] Máy chủ kiểm độ dài chữ theo cột DB (Gói 23, nhóm B3 quét Fable)
//   Trước: trang không maxlength, service không kiểm → chữ dài quá cột → SqlException "String or binary data would be truncated"
//   (tiếng Anh, có tên bảng) hoặc trang chết. Nay: service gọi LenCheck.Max(...) TRƯỚC SaveChanges → ném InvalidOperationException
//   chữ Việt (trang hiện nguyên); trang thêm maxlength cùng số. Mẫu theo [KHO-LEN] KhoVatLieuService. Không đổi luật, không đổi số.
//   Nguồn: CLAUDE quét Fable 15/11 (B-W1 BW1-01, B-W2 BW2-03, B-K BK-02, B-M2 BM2-01/03/05, B-P1 BP1-02/03, B-P2 BP2-01, B-M1 BM1-05);
//   Hùng 16/11 05:15 làm theo đề xuất (B3 / B4: chặn trước, nới cột = SQL gói sau nếu khách cần).
// =====================================================================
public static class LenCheck
{
    /// <summary>Trả lời báo (chữ Việt) nếu <paramref name="value"/> (sau Trim) dài quá <paramref name="max"/>; hợp lệ trả null.</summary>
    public static string? Over(string? value, int max, string tenO)
    {
        var n = value?.Trim().Length ?? 0;
        return n > max ? $"{tenO} dài quá {max} ký tự (đang {n} ký tự) — rút ngắn rồi lưu lại." : null;
    }

    /// <summary>Ném InvalidOperationException (lỗi nghiệp vụ, trang hiện nguyên) khi quá dài.</summary>
    public static void Max(string? value, int max, string tenO)
    {
        if (Over(value, max, tenO) is string s) throw new InvalidOperationException(s);
    }

    /// <summary>Kiểm nhiều ô một lượt; trả lời báo đầu tiên vi phạm hoặc null.</summary>
    public static string? FirstOver(params (string? Value, int Max, string TenO)[] items)
    {
        foreach (var (v, m, t) in items)
            if (Over(v, m, t) is string s) return s;
        return null;
    }
}
