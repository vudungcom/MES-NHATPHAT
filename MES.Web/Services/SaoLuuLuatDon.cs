namespace MES.Web.Services;

// =====================================================================
// [G32-SAOLUU 2026-11-22] Gói 32a — LUẬT DỌN bản sao lưu (hàm thuần, không đụng file / DB → kiểm được bằng kịch bản + ngẫu nhiên).
//   Luật HÙNG chốt 22/11 14:39 ("càng gần càng dày"), số giữ lấy từ cài đặt (HÙNG 22/11 14:50 — không cài cứng):
//     • Bản NHANH (chỉ DB, mỗi N phút): giữ các bản trong GiuBanNhanhGio giờ gần nhất.
//     • BỘ NGÀY (trọn bộ): GiuBoNgay ngày gần nhất → mỗi ngày giữ bộ MỚI NHẤT của ngày đó;
//       GiuBoTuan tuần gần nhất → mỗi tuần (tuần kết thúc vào ThuChotTuan) giữ bộ mới nhất của tuần;
//       tháng: GiuBoThang tháng gần nhất (0 = mọi tháng, giữ mãi) → mỗi tháng giữ bộ mới nhất của tháng.
//     • Giữ = hợp của các luật trên ("Giữ vì" ghi đủ lý do). Thiếu ngày (máy tắt) vẫn đúng: lấy bộ mới nhất CÒN CÓ trong khoảng.
//   Hàng rào an toàn (không phụ thuộc số cài):
//     • Chưa có bản đạt nào, hoặc bản MỚI NHẤT bị lỗi → KHÔNG xoá gì (tránh vừa hỏng bản mới vừa mất bản cũ).
//     • Luôn giữ bản đạt mới nhất.
//     • Bản lỗi không bao giờ được chọn làm bản giữ của ngày / tuần / tháng; chỉ để lại GiuBoNgay ngày (hoặc GiuBanNhanhGio giờ) để xem rồi xoá.
//     • Bản có thời điểm SAU "bây giờ" (đồng hồ máy bị chỉnh lùi) → giữ, không xoá, và không tính vào luật ngày / tuần / tháng / mới nhất.
//     • [G32-RA 2026-11-22] Bản KHÔNG RÕ (manifest không đọc được / sai kiểu — vd mạng chập chờn khi đọc thư mục chung) → LUÔN giữ, không tính
//       vào luật nào (trước: coi là LỖI → bộ tháng "giữ mãi" bị xoá). boQuaHangRao = true chỉ dùng để XEM TRƯỚC (tab hỏi PIN), không dùng khi xoá.
//       Nguồn: CLAUDE rà độc lập Gói 32a 22/11 (mục 2, 5c).
// =====================================================================
public static class SaoLuuLuatDon
{
    public record Ban(string Ten, DateTime ThoiDiem, bool Dat, bool KhongRo = false);

    /// <summary>[G32-RA2 2026-11-22] Luôn giữ ít nhất chừng này bản nhanh ĐẠT mới nhất, dù quá số giờ giữ (đồng hồ nhảy tới / máy tắt lâu không xoá
    /// sạch bản nhanh). Nguồn: CLAUDE rà độc lập lượt 2 Gói 32a (mục 2).</summary>
    public const int LuonGiuNhanh = 6;

    public record KetQua(Ban Ban, bool Giu, string LyDo);

    /// <summary>Ngày kết thúc tuần chứa <paramref name="d"/> (tuần kết thúc vào <paramref name="thuChot"/>).</summary>
    public static DateTime CuoiTuan(DateTime d, DayOfWeek thuChot)
    {
        var day = d.Date;
        int lech = ((int)thuChot - (int)day.DayOfWeek + 7) % 7;
        return day.AddDays(lech);
    }

    private static int SoThang(DateTime d) => d.Year * 12 + d.Month - 1;

    /// <summary>Luật dọn BỘ NGÀY.</summary>
    public static List<KetQua> TinhBoNgay(IReadOnlyList<Ban> ds, SaoLuuCaiDat c, DateTime now, bool boQuaHangRao = false)
    {
        var lyDo = ds.ToDictionary(b => b.Ten, _ => new List<string>());
        // Chỉ xét bản có thời điểm ≤ bây giờ (bản "tương lai" do chỉnh đồng hồ luôn giữ, không chiếm chỗ bản thật của ngày / tuần / tháng)
        var datSorted = ds.Where(b => b.Dat && !b.KhongRo && b.ThoiDiem <= now).OrderByDescending(b => b.ThoiDiem).ThenByDescending(b => b.Ten, StringComparer.Ordinal).ToList();
        var moiNhat = ds.Where(b => !b.KhongRo && b.ThoiDiem <= now).OrderByDescending(b => b.ThoiDiem).ThenByDescending(b => b.Ten, StringComparer.Ordinal).FirstOrDefault();

        bool khongXoa = !boQuaHangRao && (datSorted.Count == 0 || (moiNhat != null && !moiNhat.Dat));
        if (khongXoa)
            return ds.Select(b => new KetQua(b, true, datSorted.Count == 0 ? "Chưa có bộ đạt nào — không dọn" : "Bộ mới nhất bị lỗi — tạm không dọn")).ToList();

        if (datSorted.Count > 0) lyDo[datSorted[0].Ten].Add("Mới nhất");

        var today = now.Date;
        // Ngày
        for (int i = 0; i < c.GiuBoNgay; i++)
        {
            var d = today.AddDays(-i);
            var b = datSorted.FirstOrDefault(x => x.ThoiDiem.Date == d);
            if (b != null) lyDo[b.Ten].Add($"Ngày {d:dd/MM}");
        }
        // Tuần
        var cuoiTuanNay = CuoiTuan(today, c.ThuChotTuan);
        for (int i = 0; i < c.GiuBoTuan; i++)
        {
            var end = cuoiTuanNay.AddDays(-7 * i);
            var b = datSorted.FirstOrDefault(x => CuoiTuan(x.ThoiDiem, c.ThuChotTuan) == end);
            if (b != null) lyDo[b.Ten].Add($"Tuần đến {end:dd/MM}");
        }
        // Tháng
        int thangNay = SoThang(today);
        foreach (var g in datSorted.GroupBy(x => SoThang(x.ThoiDiem)))
        {
            if (c.GiuBoThang > 0 && (thangNay - g.Key) >= c.GiuBoThang) continue;
            var b = g.First();
            lyDo[b.Ten].Add($"Tháng {b.ThoiDiem:MM/yyyy}");
        }
        // Sau bây giờ (đồng hồ chỉnh lùi) → giữ
        foreach (var b in ds.Where(x => x.ThoiDiem > now)) lyDo[b.Ten].Add("Thời điểm sau giờ máy hiện tại — giữ");
        // Bản lỗi: giữ trong GiuBoNgay ngày để xem
        foreach (var b in ds.Where(x => !x.Dat && !x.KhongRo && x.ThoiDiem.Date > today.AddDays(-c.GiuBoNgay)))
            lyDo[b.Ten].Add("Bộ lỗi — giữ để xem");
        // [G32-RA] Không rõ (manifest không đọc được) → luôn giữ
        foreach (var b in ds.Where(x => x.KhongRo)) lyDo[b.Ten].Add("Không đọc được manifest — giữ, không dọn");

        return ds.Select(b => new KetQua(b, lyDo[b.Ten].Count > 0, lyDo[b.Ten].Count > 0 ? string.Join(" · ", lyDo[b.Ten]) : "Ngoài luật giữ")).ToList();
    }

    /// <summary>Luật dọn BẢN NHANH.</summary>
    public static List<KetQua> TinhNhanh(IReadOnlyList<Ban> ds, SaoLuuCaiDat c, DateTime now, bool boQuaHangRao = false)
    {
        var datSorted = ds.Where(b => b.Dat && !b.KhongRo && b.ThoiDiem <= now).OrderByDescending(b => b.ThoiDiem).ThenByDescending(b => b.Ten, StringComparer.Ordinal).ToList();
        var moiNhat = ds.Where(b => !b.KhongRo && b.ThoiDiem <= now).OrderByDescending(b => b.ThoiDiem).ThenByDescending(b => b.Ten, StringComparer.Ordinal).FirstOrDefault();
        if (!boQuaHangRao && (datSorted.Count == 0 || (moiNhat != null && !moiNhat.Dat)))
            return ds.Select(b => new KetQua(b, true, datSorted.Count == 0 ? "Chưa có bản đạt nào — không dọn" : "Bản mới nhất bị lỗi — tạm không dọn")).ToList();
        var moc = now.AddHours(-c.GiuBanNhanhGio);
        return ds.Select(b =>
        {
            if (b.KhongRo) return new KetQua(b, true, "Không đọc được manifest — giữ, không dọn");   // [G32-RA]
            if (datSorted.Count > 0 && b.Ten == datSorted[0].Ten) return new KetQua(b, true, "Mới nhất");
            if (datSorted.Take(LuonGiuNhanh).Any(x => x.Ten == b.Ten) && b.ThoiDiem <= moc) return new KetQua(b, true, $"Trong {LuonGiuNhanh} bản đạt mới nhất");
            if (b.ThoiDiem > now) return new KetQua(b, true, "Thời điểm sau giờ máy hiện tại — giữ");
            if (b.ThoiDiem > moc) return new KetQua(b, true, b.Dat ? $"Trong {c.GiuBanNhanhGio} giờ" : "Bản lỗi — giữ để xem");
            return new KetQua(b, false, "Quá thời gian giữ");
        }).ToList();
    }
}
