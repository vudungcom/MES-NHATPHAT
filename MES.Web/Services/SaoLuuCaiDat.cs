using System.Text;
using System.Text.Json;

namespace MES.Web.Services;

// =====================================================================
// [G32-SAOLUU 2026-11-22] Gói 32a — CÀI ĐẶT SAO LƯU (đọc / ghi file, KHÔNG để trong DB).
//   Vì sao không để trong DB: khôi phục 1 DB cũ thì cài đặt sao lưu không bị quay về bản cũ theo.
//   File: <ThuMucCaiDat>\cai-dat-<tên DB>.json — mặc định C:\ProgramData\MES_Server\SaoLuu (cùng chỗ thư mục bản quyền),
//   đổi bằng appsettings "SaoLuu:ThuMucCaiDat". Mỗi DB 1 file → 2 app trỏ 2 DB khác nhau (MES / MES_TEST) không lẫn cài đặt.
//   Mỗi lần lưu ghi thêm 1 dòng vào nhat-ky-cai-dat-<DB>.log (ai đổi, lúc nào, đổi gì) — chỉ ghi thêm, không xoá.
//   Mọi mốc (chu kỳ, số bản giữ) sửa được ở tab Sao lưu — không cài cứng trong code (HÙNG 22/11 14:50).
//   Nguồn: HÙNG 22/11 14:34–14:52 (lưu mỗi giờ, bộ ngày trọn bộ, luật dọn ngày / tuần / tháng, Google 10 năm, chỉ Admin, phương án A).
// =====================================================================
public class SaoLuuCaiDat
{
    public bool BatTuDong { get; set; } = true;
    /// <summary>Thư mục sao lưu nhìn từ máy chạy SQL Server (lệnh BACKUP ghi file vào đây). Trống = chưa cài.</summary>
    public string ThuMucTheoMaySql { get; set; } = "";
    /// <summary>Cùng thư mục đó nhìn từ máy chạy app (vd \\192.168.1.211\MES-Backup). Trống = giống ThuMucTheoMaySql (SQL + app cùng máy).</summary>
    public string ThuMucTheoMayApp { get; set; } = "";
    public int PhutLuuNhanh { get; set; } = 60;
    public string GioChotBoNgay { get; set; } = "23:55";
    public int GiuBanNhanhGio { get; set; } = 48;
    public int GiuBoNgay { get; set; } = 7;
    public int GiuBoTuan { get; set; } = 4;
    public DayOfWeek ThuChotTuan { get; set; } = DayOfWeek.Sunday;
    /// <summary>0 = giữ mãi trên máy.</summary>
    public int GiuBoThang { get; set; } = 0;
    /// <summary>Script đẩy Google (Gói 32b) đọc: bản tháng trên Drive giữ bao nhiêu năm.</summary>
    public int GiuGoogleNam { get; set; } = 10;
    /// <summary>[G32B-TRUOC-NANG-CAP 2026-11-23] Giữ bao nhiêu bộ trọn bộ tạo lúc nâng cấp phần mềm (thư mục truoc-nang-cap). Nguồn: HÙNG 23/11 13:20 OK câu B2.</summary>
    public int GiuTruocNangCap { get; set; } = 10;
    /// <summary>File / thư mục cấu hình khác chép kèm bộ ngày (vd C:\caddy\Caddyfile) — mỗi dòng 1 đường dẫn.</summary>
    public List<string> FileCauHinhThem { get; set; } = new();

    public const int PhutNhanhMin = 15, PhutNhanhMax = 1440;

    /// <summary>Thư mục nhìn từ app (đã áp luật "trống = giống máy SQL").</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ThuMucApp => string.IsNullOrWhiteSpace(ThuMucTheoMayApp) ? ThuMucTheoMaySql.Trim() : ThuMucTheoMayApp.Trim();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DaCaiThuMuc => !string.IsNullOrWhiteSpace(ThuMucTheoMaySql);

    public bool TryGioChot(out TimeSpan t)
    {
        t = default;
        var s = (GioChotBoNgay ?? "").Trim();
        if (!TimeSpan.TryParseExact(s, @"hh\:mm", null, out t)) return false;
        return t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
    }

    /// <summary>Danh sách lỗi nhập (trống = hợp lệ).</summary>
    public List<string> KiemLoi()
    {
        var e = new List<string>();
        if (PhutLuuNhanh < PhutNhanhMin || PhutLuuNhanh > PhutNhanhMax) e.Add($"Lưu nhanh mỗi … phút: từ {PhutNhanhMin} đến {PhutNhanhMax}.");
        if (!TryGioChot(out _)) e.Add("Giờ chốt bộ ngày phải dạng HH:mm (vd 23:55).");
        if (GiuBanNhanhGio < 1 || GiuBanNhanhGio > 720) e.Add("Giữ bản nhanh … giờ: từ 1 đến 720.");
        if (GiuBoNgay < 1 || GiuBoNgay > 365) e.Add("Giữ bộ ngày … ngày: từ 1 đến 365.");
        if (GiuBoTuan < 0 || GiuBoTuan > 260) e.Add("Giữ bộ tuần … tuần: từ 0 đến 260.");
        if (GiuBoThang < 0 || GiuBoThang > 1200) e.Add("Giữ bộ tháng … tháng: 0 (giữ mãi) đến 1200.");
        if (GiuGoogleNam < 1 || GiuGoogleNam > 100) e.Add("Giữ trên Google … năm: từ 1 đến 100.");
        if (GiuTruocNangCap < 1 || GiuTruocNangCap > 100) e.Add("Giữ bộ trước nâng cấp … bộ: từ 1 đến 100.");   // [G32B-TRUOC-NANG-CAP]
        if (!Enum.IsDefined(typeof(DayOfWeek), ThuChotTuan)) e.Add("Thứ chốt tuần không hợp lệ.");
        if (BatTuDong && !DaCaiThuMuc) e.Add("Bật tự động thì phải nhập thư mục sao lưu (theo máy SQL).");
        foreach (var p in new[] { ThuMucTheoMaySql, ThuMucTheoMayApp })
            if (!string.IsNullOrWhiteSpace(p) && !LaDuongDanTuyetDoi(p.Trim())) e.Add($"Thư mục \"{p}\" phải là đường dẫn đầy đủ (vd D:\\MES-Backup hoặc \\\\192.168.1.211\\MES-Backup).");
        foreach (var f in FileCauHinhThem.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!LaDuongDanTuyetDoi(f.Trim())) e.Add($"File cấu hình thêm \"{f}\" phải là đường dẫn đầy đủ.");
            // [G32-RA 2026-11-22] trỏ vào / trùm thư mục sao lưu → mỗi bộ chép cả các bộ trước (dung lượng dồn). Nguồn: CLAUDE rà độc lập Gói 32a (mục 10).
            else foreach (var g in new[] { ThuMucTheoMaySql, ThuMucTheoMayApp }.Where(x => !string.IsNullOrWhiteSpace(x)))
                if (LongNhau(f, g)) { e.Add($"File cấu hình thêm \"{f}\" không được nằm trong / chứa thư mục sao lưu \"{g}\"."); break; }
        }
        return e;
    }

    private static string ChuanDuong(string p) => p.Trim().Replace('/', '\\').TrimEnd('\\') + "\\";
    /// <summary>a nằm trong b hoặc b nằm trong a (so chữ không phân biệt hoa thường, / = \\).</summary>
    public static bool LongNhau(string a, string b)
    {
        var x = ChuanDuong(a); var y = ChuanDuong(b);
        return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Đường dẫn tuyệt đối kiểu Windows (C:\…, \\máy\…) hoặc kiểu máy đang chạy (dùng khi thử trên Linux).</summary>
    public static bool LaDuongDanTuyetDoi(string p)
    {
        if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) return true;
        if (p.StartsWith(@"\\") && p.Length > 3) return true;
        return Path.IsPathFullyQualified(p);
    }

    public SaoLuuCaiDat Clone()
    {
        var c = (SaoLuuCaiDat)MemberwiseClone();
        c.FileCauHinhThem = FileCauHinhThem.ToList();
        return c;
    }

    /// <summary>Câu mô tả các ô đã đổi (ghi nhật ký cài đặt).</summary>
    public static string MoTaThayDoi(SaoLuuCaiDat cu, SaoLuuCaiDat moi)
    {
        var d = new List<string>();
        void Cmp(string ten, object? a, object? b) { if (!Equals(a, b)) d.Add($"{ten}: {a} → {b}"); }
        Cmp("Tự động", cu.BatTuDong, moi.BatTuDong);
        Cmp("Thư mục (máy SQL)", cu.ThuMucTheoMaySql, moi.ThuMucTheoMaySql);
        Cmp("Thư mục (máy app)", cu.ThuMucTheoMayApp, moi.ThuMucTheoMayApp);
        Cmp("Lưu nhanh (phút)", cu.PhutLuuNhanh, moi.PhutLuuNhanh);
        Cmp("Giờ chốt bộ ngày", cu.GioChotBoNgay, moi.GioChotBoNgay);
        Cmp("Giữ bản nhanh (giờ)", cu.GiuBanNhanhGio, moi.GiuBanNhanhGio);
        Cmp("Giữ bộ ngày", cu.GiuBoNgay, moi.GiuBoNgay);
        Cmp("Giữ bộ tuần", cu.GiuBoTuan, moi.GiuBoTuan);
        Cmp("Thứ chốt tuần", cu.ThuChotTuan, moi.ThuChotTuan);
        Cmp("Giữ bộ tháng (0=mãi)", cu.GiuBoThang, moi.GiuBoThang);
        Cmp("Giữ Google (năm)", cu.GiuGoogleNam, moi.GiuGoogleNam);
        Cmp("Giữ bộ trước nâng cấp", cu.GiuTruocNangCap, moi.GiuTruocNangCap);   // [G32B-TRUOC-NANG-CAP]
        Cmp("File cấu hình thêm", string.Join(" | ", cu.FileCauHinhThem), string.Join(" | ", moi.FileCauHinhThem));
        return d.Count == 0 ? "(không đổi)" : string.Join("; ", d);
    }
}

/// <summary>[G32-SAOLUU] Đọc / ghi file cài đặt + nhật ký đổi cài đặt.</summary>
public class SaoLuuCaiDatStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    public string ThuMuc { get; }

    public SaoLuuCaiDatStore(string thuMuc) { ThuMuc = thuMuc; }

    /// <summary>Thư mục mặc định: appsettings "SaoLuu:ThuMucCaiDat", không có thì C:\ProgramData\MES_Server\SaoLuu.</summary>
    public static string ThuMucMacDinh(IConfiguration cfg)
    {
        var s = cfg["SaoLuu:ThuMucCaiDat"];
        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MES_Server", "SaoLuu");
    }

    public static string TenAnToan(string db)
    {
        var sb = new StringBuilder();
        foreach (var ch in db) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        var t = sb.Length == 0 ? "DB" : sb.ToString();
        // [G32-RA 2026-11-22] tên phải đổi ký tự (vd "MES.1" / "MES 1") → thêm 6 ký tự mã băm tên gốc, 2 DB khác nhau không dùng chung thư mục / cài đặt.
        //   Nguồn: CLAUDE rà độc lập Gói 32a (mục 11).
        if (t != db) t += "_" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(db)))[..6];
        return t;
    }

    public string FileCaiDat(string db) => Path.Combine(ThuMuc, $"cai-dat-{TenAnToan(db)}.json");
    public string FileNhatKy(string db) => Path.Combine(ThuMuc, $"nhat-ky-cai-dat-{TenAnToan(db)}.log");

    /// <summary>Đọc cài đặt; chưa có file → mặc định. File hỏng → ném lỗi (không âm thầm về mặc định, tránh lỡ tắt / đổi luật giữ).</summary>
    public SaoLuuCaiDat Doc(string db)
    {
        lock (_gate)
        {
            var f = FileCaiDat(db);
            if (!File.Exists(f)) return new SaoLuuCaiDat();
            var txt = File.ReadAllText(f, Encoding.UTF8);
            var c = JsonSerializer.Deserialize<SaoLuuCaiDat>(txt, Json) ?? throw new InvalidOperationException($"File cài đặt sao lưu rỗng: {f}");
            c.FileCauHinhThem ??= new();
            // [G32-RA 2026-11-22] sửa tay ra số sai (vd 0 phút → lưu mỗi 30 giây) → coi như hỏng, không chạy theo. Nguồn: CLAUDE rà độc lập (mục 16).
            var loi = c.KiemLoi();
            if (loi.Count > 0) throw new InvalidOperationException($"File cài đặt sao lưu {f} có giá trị sai: {string.Join(" ", loi)}");
            return c;
        }
    }

    /// <summary>[G32-RA] File cài đặt hỏng: đổi tên thành .hong-&lt;giờ&gt; (giữ lại để xem), để Admin lưu bản mới từ tab. Trả tên file mới (null = không có file).</summary>
    public string? DoiTenHong(string db, DateTime luc)
    {
        lock (_gate)
        {
            var f = FileCaiDat(db);
            if (!File.Exists(f)) return null;
            var dich = $"{f}.hong-{luc:yyyyMMddHHmmss}";
            File.Move(f, dich, overwrite: true);
            return dich;
        }
    }

    /// <summary>[G32-RA] Thêm 1 dòng nhật ký (vd Admin bấm "Cho dọn lại").</summary>
    public void GhiNhatKy(string db, string nguoi, string moTa, DateTime luc)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(ThuMuc);
            File.AppendAllText(FileNhatKy(db), $"{luc:yyyy-MM-dd HH:mm:ss} | {nguoi} | {moTa}{Environment.NewLine}", new UTF8Encoding(false));
        }
    }

    public void Ghi(string db, SaoLuuCaiDat moi, string nguoi, string moTa, DateTime luc)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(ThuMuc);
            var f = FileCaiDat(db);
            var tmp = f + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))   // [G32-RA] ghi hẳn xuống đĩa trước khi đổi tên (mất điện không để file rỗng)
            {
                var b = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(moi, Json));
                fs.Write(b, 0, b.Length);
                fs.Flush(true);
            }
            File.Move(tmp, f, overwrite: true);
            File.AppendAllText(FileNhatKy(db), $"{luc:yyyy-MM-dd HH:mm:ss} | {nguoi} | {moTa}{Environment.NewLine}", new UTF8Encoding(false));
        }
    }

    public List<string> DocNhatKy(string db, int soDong)
    {
        lock (_gate)
        {
            var f = FileNhatKy(db);
            if (!File.Exists(f)) return new();
            var all = File.ReadAllLines(f, Encoding.UTF8);
            return all.Skip(Math.Max(0, all.Length - soDong)).Reverse().ToList();
        }
    }
}
