namespace MES.Web.Services;

// ================================================================
// [B3-SO-NGUYEN 2026-11-06] Số lượng chỉ nhận SỐ NGUYÊN ở máy chủ (SL PO, phiếu giao, lần nhận, báo lỗi, Kết quả sửa, KCS xác nhận,
//   form Giao / xử lý hàng, WTS OK / NG).
//   Vì sao: RS-E9 / KHO-7 — SL PO 4 số lẻ, phiếu / WTS 2 số lẻ nhưng phôi Kho (KhoVatLieus) là số nguyên → số lẻ ghi vào phiếu / WTS
//   thì Kho cắt mất phần lẻ, sổ in/out lệch phần lẻ. Xưởng đếm theo chiếc.
//   Nguồn: CLAUDE 31/10 (RS-E9), Hùng 05/11 15:37 chốt "kiểm DB trước: không có số lẻ thì máy chủ chỉ nhận số nguyên";
//   QuetSo (mục SO_LE) trên DB MES 06/11 07:03: không có dòng số lẻ nào → làm.
//   Dữ liệu cũ không đụng (chỉ kiểm lúc ghi mới / sửa).
// ================================================================
public static class QtyRule
{
    public const string Msg = "Số lượng phải là số nguyên (xưởng đếm theo chiếc).";

    public static bool IsWhole(decimal v) => v == decimal.Truncate(v);

    /// <summary>null = mọi số đều nguyên; còn lại = lời báo.</summary>
    public static string? CheckWhole(params decimal[] values) => values.All(IsWhole) ? null : Msg;

    // [WTS-AM 2026-11-06] WTS: OK / NG không được âm. Trước máy chủ chỉ chặn SL hoàn thành <= 0 → OK 5 + NG -2 lưu được khi Workflow OFF
    //   (DB ghi Done 3, OK 5, NG -2; Workflow ON thì K5 chặn vì tồn hàng lỗi âm). Ô nhập type=number min=0 vẫn gõ được "-2".
    //   Nguồn: rà độc lập gói 1 06/11 (mục 3, ca P9 chạy thật trên harness).
    public const string MsgAm = "Số lượng không được âm.";
    public static string? CheckNonNegative(params decimal[] values) => values.All(v => v >= 0) ? null : MsgAm;

    // [KH-SL-MAY-CHU 2026-11-07] SL PO ở máy chủ: số nguyên từ 1 tới PoMax (phôi Kho là cột int; Workflow OFF dùng SL PO làm trần WTS).
    //   Nguồn: rà độc lập toàn hệ thống 07/11 (Thấp) — trước: tạo / sửa KH chỉ kiểm số nguyên, ô SL > 0 chỉ có ở trang (Edit_Khplan).
    public const decimal PoMax = 1_000_000_000m;
    public static string? CheckPoQty(decimal v) => !IsWhole(v) ? Msg : v <= 0 ? "SL PO phải > 0." : v > PoMax ? "SL PO quá lớn (tối đa 1.000.000.000)." : null;

    // [WTS-GIO-AM 2026-11-16] (BW1-02) Giờ thực tế WTS (T.Gá / T.GC / T.Ktra / T.CB / T.Chạy, T.Thực tế/pc) ở máy chủ: không âm, không quá
    //   cột decimal(10,2) = 99.999.999,99 (trần kỹ thuật của cột, không phải trần nghiệp vụ). Trước: ô bắt buộc chỉ bị bắt "<= 0", ô không bắt buộc
    //   (T.Chạy khi Chạy thử…) nhận âm ghi vào DB → hiệu suất / tổng giờ chuẩn sai; số quá lớn → SqlException overflow tiếng Anh.
    //   Nguồn: CLAUDE quét Fable 15/11 (B-W1 BW1-02 Trung); Hùng 16/11 11:34 OK Gói 24.
    public const decimal TimeMax = 99_999_999.99m;
    public static string? CheckTimeRange(params (decimal? Value, string TenO)[] items)
    {
        foreach (var (v, ten) in items)
        {
            if (!v.HasValue) continue;
            if (v.Value < 0) return $"{ten} không được âm.";
            if (v.Value > TimeMax) return $"{ten} quá lớn (tối đa 99.999.999,99 phút).";
        }
        return null;
    }
    /// <summary>[WTS-GIO-AM] Kẹp giá trị ô giờ ở trang về [0, TimeMax] (ô type=number vẫn gõ được "-3" / "1e9").</summary>
    public static decimal? ClampTime(decimal? v) => v.HasValue ? Math.Min(TimeMax, Math.Max(0m, v.Value)) : null;
    public static (decimal?, string)[] WtsTimeItems(decimal? setup, decimal? mach, decimal? insp, decimal? prep, decimal? trial, decimal? perPc)
        => new[] { (setup, "T.Gá"), (mach, "T.GC"), (insp, "T.Ktra"), (prep, "T.CB"), (trial, "T.Chạy"), (perPc, "T.Thực tế/pc") };
}
