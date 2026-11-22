using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MES.Web.Data;

namespace MES.Web.Services;

// =====================================================================
// [G32-SAOLUU 2026-11-22] Gói 32a — SAO LƯU trong phần mềm (singleton; lịch chạy ở SaoLuuWorker, tab Cài đặt › Sao lưu gọi trực tiếp).
//   2 loại bản, đều là 1 FOLDER (tên = thời điểm yyyy-MM-dd_HHmmss) có manifest.json:
//     • nhanh\<tên>\  : du-lieu\<DB>_<tên>.bak.zip + manifest      (mỗi N phút — HÙNG 22/11 14:34 "tự lưu 1 h")
//     • bo\<tên>\     : TRỌN BỘ — du-lieu\ + cau-hinh\ (appsettings, cài đặt sao lưu, bản quyền, file thêm vd Caddyfile)
//                       + uploads\uploads.zip + phan-mem\MES.Web.zip + khoi-phuc\ (nếu có mẫu) + DOC-TRUOC.txt + manifest
//                       (mỗi ngày 1 lần ở giờ chốt — HÙNG 22/11 14:34 "folder để trọn 1 bộ cả phần mềm lẫn dữ liệu")
//   Thư mục: <ThuMuc>\<DB>\nhanh|bo\… — <ThuMuc> nhìn từ máy SQL (lệnh BACKUP ghi) và nhìn từ máy app (app nén / băm / dọn) là CÙNG 1 chỗ.
//   An toàn:
//     • Đang tạo thì nằm trong "<tên>.dang-tao", xong hết mới đổi tên → bản dở (mất điện) không bao giờ bị tính là bản đạt.
//     • Lỗi giữa chừng → vẫn để folder <tên> với manifest KetQua=LOI + câu lỗi (xoá phần dữ liệu dở) → tab / băng đỏ thấy được.
//     • .bak: SQL kiểm CHECKSUM khi lưu; app băm SHA-256 file gốc rồi đọc lại từ file zip so băm → zip hỏng là báo lỗi.
//     • Chỉ 1 lượt chạy cùng lúc trong app (SemaphoreSlim) + giữa các app cùng trỏ 1 DB (sp_getapplock 'MES_SAO_LUU').
//     • Dọn chỉ xoá folder đúng mẫu tên + có manifest.json (hoặc folder ".dang-tao" bỏ dở > 6 giờ) — không đụng file khác trong thư mục.
//   Chỉ đọc DB (đếm dòng + BACKUP COPY_ONLY) — không ghi dòng nào, không đụng luật xưởng / số in/out.
// =====================================================================
public class SaoLuuService
{
    public const string ThuMucNhanh = "nhanh", ThuMucBo = "bo", DuoiDangTao = ".dang-tao", TenManifest = "manifest.json";
    private const string MauTen = "yyyy-MM-dd_HHmmss";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SaoLuuService> _log;
    private readonly SemaphoreSlim _chay = new(1, 1);

    public ISaoLuuSql Sql { get; set; }
    public SaoLuuCaiDatStore Store { get; set; }
    /// <summary>Giờ máy chủ (thay được trong kịch bản thử).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
    public string ContentRoot { get; set; }
    public string BaseDir { get; set; } = AppContext.BaseDirectory;
    public string ThuMucBanQuyen { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MES_Server", "License");
    /// <summary>Kịch bản thử: gọi ngay sau khi nén .bak (giả file nén hỏng). Code chạy thật không gán.</summary>
    public Action<string>? ThuSauKhiNen { get; set; }
    /// <summary>Kịch bản thử: không kiểm Admin qua DB (dùng hàm giả).</summary>
    public Func<int, Task<bool>>? KiemAdminThu { get; set; }

    // Trạng thái trong bộ nhớ (lần thử gần nhất — kể cả lỗi; dùng cho lịch thử lại + tab)
    public DateTime? LanThuNhanh { get; private set; }
    public DateTime? LanThuBo { get; private set; }
    public string? DangChay { get; private set; }
    public KetQuaChay? KetQuaGanNhat { get; private set; }
    private string? _db;

    public SaoLuuService(IServiceScopeFactory scopes, ISaoLuuSql sql, SaoLuuCaiDatStore store, string contentRoot, ILogger<SaoLuuService> log)
    {
        _scopes = scopes;
        _log = log;
        Sql = sql;
        Store = store;
        ContentRoot = contentRoot;
    }

    public enum Loai { Nhanh, Bo }

    /// <summary>BoQua = lượt không chạy vì đang có lượt khác (trong app / app khác cùng DB) — không phải lỗi, băng không báo đỏ ([G32-RA] mục 21).</summary>
    public record KetQuaChay(bool Dat, Loai Loai, string Ten, DateTime BatDau, DateTime KetThuc, string ThongBao, long Bytes, bool BoQua = false);

    /// <summary>KhongRo = manifest không đọc được / sai kiểu → luôn giữ, không dọn, băng báo ([G32-RA] mục 2).</summary>
    public record BanInfo(string Ten, Loai Loai, DateTime ThoiDiem, bool Dat, long Bytes, string? Loi, string DuongDan, string? Nguon, string? PhienBan, bool KhongRo = false);

    /// <summary>[G32-RA] Bản có giờ sau giờ máy quá mức này = "tương lai" (đồng hồ bị chỉnh) — không tính là bản gần nhất.</summary>
    public static readonly TimeSpan LechGioChoPhep = TimeSpan.FromMinutes(5);
    /// <summary>[G32-RA2 2026-11-22] 1 lượt dọn muốn xoá NHIỀU HƠN số bộ ngày này (bình thường ≤ 2–3: ngày thứ 8, tuần thứ 5, tháng cũ) → TẠM DỪNG
    /// dọn bộ ngày, băng vàng, chờ Admin bấm "Cho dọn lại" (PIN) hoặc lưu cài đặt có PIN. Bắt cả đồng hồ nhảy tới, đổi cài đặt lách bước xem trước,
    /// máy tắt lâu. Thay luật cũ "bản mới cách bản trước > 3 ngày" (rà độc lập lượt 2: bỏ sót nhảy &lt; 3 ngày, kẹt khi chỉ bấm tay mỗi tuần).</summary>
    public const int NguongXoaBo = 3;
    public const string TenTamDung = "tam-dung-don.txt", TenChoPhep = "cho-phep-don.txt";

    // ----------------------------------------------------------------- cài đặt
    public async Task<string> DbAsync(CancellationToken ct = default) => _db ??= await Sql.TenDbAsync(ct);

    public async Task<SaoLuuCaiDat> CaiDatAsync(CancellationToken ct = default) => Store.Doc(await DbAsync(ct));

    private async Task<bool> LaAdminAsync(int userId)
    {
        if (KiemAdminThu != null) return await KiemAdminThu(userId);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await PermCheck.IsAdminAsync(db, userId);
    }

    public const string KhongPhaiAdmin = "Chỉ tài khoản nhóm ADMIN mới được thao tác sao lưu.";

    /// <summary>Lưu cài đặt (chỉ Admin — máy chủ tự kiểm). Trả câu lỗi hoặc null.</summary>
    /// <param name="daXacNhanXoaBo">Số bộ ngày tab đã báo trước sẽ xoá và Admin đã nhập PIN xác nhận (0 = không có) → cho phép lần dọn tới xoá chừng đó.</param>
    public async Task<string?> LuuCaiDatAsync(SaoLuuCaiDat moi, int actorUserId, string actorName, int daXacNhanXoaBo = 0)
    {
        if (!await LaAdminAsync(actorUserId)) return KhongPhaiAdmin;
        moi = ChuanHoa(moi);
        var loi = moi.KiemLoi();
        if (loi.Count > 0) return string.Join(" ", loi);
        var db = await DbAsync();
        // [G32-RA] thư mục phải tạo / mở được từ app ngay lúc lưu (không đợi tới lượt sao lưu mới biết)
        if (moi.DaCaiThuMuc)
        {
            try { Directory.CreateDirectory(moi.ThuMucApp); }
            catch (Exception ex) { return $"Không tạo / mở được thư mục \"{moi.ThuMucApp}\" từ máy chạy app: {ex.Message}"; }
        }
        SaoLuuCaiDat cu; string tien = "";
        try { cu = Store.Doc(db); }
        catch (IOException ex) { return "Không đọc được file cài đặt sao lưu (đang bị chương trình khác giữ?): " + ex.Message + " — thử lại sau ít phút."; }   // [G32-RA2] lỗi đọc tạm thời ≠ file hỏng
        catch (UnauthorizedAccessException ex) { return "Không có quyền đọc file cài đặt sao lưu: " + ex.Message; }
        catch (Exception ex)
        {
            // [G32-RA] file cài đặt hỏng: giữ lại (đổi tên) rồi ghi bản mới — trước: không lưu được, phải vào máy xoá tay
            var ten = Store.DoiTenHong(db, Clock());
            cu = new SaoLuuCaiDat();
            tien = $"File cài đặt cũ hỏng ({ex.Message}) — đã đổi tên giữ lại: {ten}. ";
        }
        Store.Ghi(db, moi, actorName, tien + SaoLuuCaiDat.MoTaThayDoi(cu, moi) + (daXacNhanXoaBo > 0 ? $" · PIN xác nhận xoá {daXacNhanXoaBo} bộ ngày" : ""), Clock());
        if (daXacNhanXoaBo > 0 && moi.DaCaiThuMuc) GhiChoPhep(moi, db, daXacNhanXoaBo + NguongXoaBo);
        _canhBaoCache = null;
        return null;
    }

    // [G32-RA2] "cho phép" = lần dọn trong 24 giờ tới được xoá tối đa N bộ ngày (sau khi Admin đã xem số bộ sẽ xoá + nhập PIN)
    private void GhiChoPhep(SaoLuuCaiDat c, string db, int n)
    {
        var dir = ThuMucDb(c, db);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, TenChoPhep), $"{n};{Clock().AddHours(24).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
    }

    private int DocChoPhep(SaoLuuCaiDat c, string db, DateTime now)
    {
        try
        {
            var f = Path.Combine(ThuMucDb(c, db), TenChoPhep);
            if (!File.Exists(f)) return 0;
            var p = File.ReadAllText(f).Trim().Split(';');
            if (p.Length == 2 && int.TryParse(p[0], out var n) && DateTime.TryParseExact(p[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var het) && now <= het) return n;
        }
        catch { }
        return 0;
    }

    /// <summary>[G32-RA2] Số bộ ngày lần dọn tới sẽ xoá theo luật (tab hiện ở nút "Cho dọn lại").</summary>
    public int SoBoSeXoa(SaoLuuCaiDat c, string db, DateTime now) => TinhDon(c, db, now).Bo.Count(x => !x.Giu);

    /// <summary>[G32-RA] Admin (đã nhập PIN ở tab) xác nhận số bộ sẽ xoá là đúng (đồng hồ đúng / máy tắt lâu / vừa giảm số giữ) → bỏ "tạm dừng dọn",
    /// cho phép lần dọn trong 24 giờ tới xoá tối đa số bộ đang tính + NguongXoaBo.</summary>
    public async Task<string?> ChoDonLaiAsync(int actorUserId, string actorName)
    {
        if (!await LaAdminAsync(actorUserId)) return KhongPhaiAdmin;
        var db = await DbAsync();
        var c = Store.Doc(db);
        var f = Path.Combine(ThuMucDb(c, db), TenTamDung);
        var nd = File.Exists(f) ? File.ReadAllText(f).Trim() : "(không có tạm dừng)";
        int n = SoBoSeXoa(c, db, Clock());
        GhiChoPhep(c, db, n + NguongXoaBo);
        if (File.Exists(f)) File.Delete(f);
        Store.GhiNhatKy(db, actorName, $"Cho dọn lại — PIN xác nhận xoá {n} bộ ngày (trước đó: {nd})", Clock());
        _canhBaoCache = null;
        return null;
    }

    public string? DocTamDung(SaoLuuCaiDat c, string db)
    {
        if (!c.DaCaiThuMuc) return null;
        var f = Path.Combine(ThuMucDb(c, db), TenTamDung);
        try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch { return null; }
    }

    public static SaoLuuCaiDat ChuanHoa(SaoLuuCaiDat c)
    {
        var x = c.Clone();
        x.ThuMucTheoMaySql = (x.ThuMucTheoMaySql ?? "").Trim();
        x.ThuMucTheoMayApp = (x.ThuMucTheoMayApp ?? "").Trim();
        x.GioChotBoNgay = (x.GioChotBoNgay ?? "").Trim();
        x.FileCauHinhThem = (x.FileCauHinhThem ?? new()).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Distinct().ToList();
        return x;
    }

    // ----------------------------------------------------------------- quét thư mục
    public static string ThuMucDb(SaoLuuCaiDat c, string db) => Path.Combine(c.ThuMucApp, SaoLuuCaiDatStore.TenAnToan(db));
    private static string ThuMucLoai(SaoLuuCaiDat c, string db, Loai l) => Path.Combine(ThuMucDb(c, db), l == Loai.Nhanh ? ThuMucNhanh : ThuMucBo);

    public static bool TryTen(string ten, out DateTime t) =>
        DateTime.TryParseExact(ten, MauTen, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

    /// <summary>Đọc các bản đang có (folder đúng mẫu tên + có manifest). Thư mục gốc không mở được → ném lỗi (băng báo đỏ, không coi là "chưa có bản").</summary>
    public List<BanInfo> Quet(SaoLuuCaiDat c, string db, Loai l)
    {
        var res = new List<BanInfo>();
        if (!c.DaCaiThuMuc) return res;
        if (!Directory.Exists(c.ThuMucApp))   // [G32-RA] mục 12: mất quyền / máy kia tắt — trước: báo nhầm "chưa có bản nào"
            throw new SaoLuuException($"Không mở được thư mục sao lưu \"{c.ThuMucApp}\" (mất kết nối / mất quyền / ổ chưa cắm?).");
        var dir = ThuMucLoai(c, db, l);
        if (!Directory.Exists(dir)) return res;
        foreach (var d in Directory.GetDirectories(dir))
        {
            var ten = Path.GetFileName(d);
            if (!TryTen(ten, out var t)) continue;
            var mf = Path.Combine(d, TenManifest);
            if (!File.Exists(mf)) continue;
            res.Add(DocBan(mf, ten, l, t, d));
        }
        return res.OrderByDescending(b => b.ThoiDiem).ToList();
    }

    // [G32-RA] mục 2 + 8: đọc manifest CHỊU LỖI (chỉ lấy vài trường; đọc / phân tích lỗi = KHÔNG RÕ, không phải LỖI) + nhớ theo (giờ sửa, cỡ file)
    private readonly Dictionary<string, (DateTime M, long N, BanInfo B)> _mfCache = new();
    private BanInfo DocBan(string mf, string ten, Loai l, DateTime t, string d)
    {
        DateTime mt; long n;
        try { var fi = new FileInfo(mf); mt = fi.LastWriteTimeUtc; n = fi.Length; }
        catch (Exception ex) { return new BanInfo(ten, l, t, false, 0, "Không đọc được manifest: " + ex.Message, d, null, null, KhongRo: true); }
        lock (_mfCache) if (_mfCache.TryGetValue(mf, out var ca) && ca.M == mt && ca.N == n) return ca.B;
        BanInfo b;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(mf, Encoding.UTF8));
            var r = doc.RootElement;
            string? Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            long bytes = r.TryGetProperty("TongBytes", out var tb) && tb.ValueKind == JsonValueKind.Number && tb.TryGetInt64(out var x) ? x : 0;
            var kq = Str("KetQua");
            if (kq == SaoLuuManifest.KqDat) b = new BanInfo(ten, l, t, true, bytes, null, d, Str("Nguon"), Str("PhienBanPhanMem"));
            else if (kq == SaoLuuManifest.KqLoi) b = new BanInfo(ten, l, t, false, bytes, Str("Loi") ?? "(không ghi câu lỗi)", d, Str("Nguon"), Str("PhienBanPhanMem"));
            else b = new BanInfo(ten, l, t, false, bytes, $"manifest không có KetQua DAT / LOI (\"{kq}\")", d, Str("Nguon"), Str("PhienBanPhanMem"), KhongRo: true);
        }
        catch (Exception ex) { return new BanInfo(ten, l, t, false, 0, "Không đọc được manifest: " + ex.Message, d, null, null, KhongRo: true); }
        lock (_mfCache) _mfCache[mf] = (mt, n, b);
        return b;
    }

    // ----------------------------------------------------------------- dọn
    public record DuKienDon(List<SaoLuuLuatDon.KetQua> Bo, List<SaoLuuLuatDon.KetQua> Nhanh)
    {
        public int SoXoa => Bo.Count(x => !x.Giu) + Nhanh.Count(x => !x.Giu);
    }

    public DuKienDon TinhDon(SaoLuuCaiDat c, string db, DateTime now, bool boQuaHangRao = false, List<BanInfo>? boDs = null, List<BanInfo>? nhDs = null)
    {
        var bo = (boDs ?? Quet(c, db, Loai.Bo)).Select(b => new SaoLuuLuatDon.Ban(b.Ten, b.ThoiDiem, b.Dat, b.KhongRo)).ToList();
        var nh = (nhDs ?? Quet(c, db, Loai.Nhanh)).Select(b => new SaoLuuLuatDon.Ban(b.Ten, b.ThoiDiem, b.Dat, b.KhongRo)).ToList();
        return new DuKienDon(SaoLuuLuatDon.TinhBoNgay(bo, c, now, boQuaHangRao), SaoLuuLuatDon.TinhNhanh(nh, c, now, boQuaHangRao));
    }

    /// <summary>Cài đặt mới sẽ làm xoá thêm những bản nào — tab hiện trước, bắt nhập PIN.
    /// [G32-RA 2026-11-22] mục 5: tính CẢ cài đặt cũ lẫn mới trên thư mục MỚI (đổi thư mục / viết đường dẫn khác kiểu không lách được) và bỏ
    /// hàng rào "bản mới nhất lỗi" khi xem trước (đang lỗi thì lần lưu đạt kế tiếp vẫn xoá hàng loạt). Nguồn: CLAUDE rà độc lập Gói 32a.</summary>
    public async Task<List<string>> XemTruocXoaAsync(SaoLuuCaiDat moi)
    {
        var db = await DbAsync();
        SaoLuuCaiDat cu;
        try { cu = Store.Doc(db); } catch { cu = new SaoLuuCaiDat(); }   // file hỏng: so với mặc định
        moi = ChuanHoa(moi);
        if (!moi.DaCaiThuMuc || !Directory.Exists(moi.ThuMucApp)) return new();
        var cuTrenMoi = cu.Clone();
        cuTrenMoi.ThuMucTheoMaySql = moi.ThuMucTheoMaySql;
        cuTrenMoi.ThuMucTheoMayApp = moi.ThuMucTheoMayApp;
        var now = Clock();
        var bo = Quet(moi, db, Loai.Bo); var nh = Quet(moi, db, Loai.Nhanh);
        var a = TinhDon(cuTrenMoi, db, now, true, bo, nh);
        var b = TinhDon(moi, db, now, true, bo, nh);
        var giuCu = a.Bo.Where(x => x.Giu).Select(x => "B/" + x.Ban.Ten).Concat(a.Nhanh.Where(x => x.Giu).Select(x => "N/" + x.Ban.Ten)).ToHashSet();
        return b.Bo.Select(x => ("Bộ ngày", "B/", x)).Concat(b.Nhanh.Select(x => ("Bản nhanh", "N/", x)))
            .Where(p => !p.x.Giu && giuCu.Contains(p.Item2 + p.x.Ban.Ten))
            .Select(p => $"{p.Item1} {p.x.Ban.ThoiDiem:dd/MM/yyyy HH:mm}{(p.x.Ban.Dat ? "" : " (bản lỗi)")}")   // [G32-RA2] mục 7
            .ToList();
    }

    public const string DuoiXoa = ".xoa";

    /// <summary>Xoá theo luật. Trả số folder đã xoá. Chỉ gọi khi đang giữ khoá chạy (ChayAsync) hoặc trong kịch bản thử.
    /// [G32-RA2 2026-11-22] 1 lượt muốn xoá &gt; NguongXoaBo bộ ngày mà chưa có "cho phép" (PIN) → ghi tam-dung-don.txt, KHÔNG xoá bộ ngày
    /// (bản nhanh vẫn dọn — luôn giữ 6 bản nhanh đạt mới nhất; folder .dang-tao / .xoa bỏ dở vẫn dọn). Mục 3: .dang-tao bỏ dở &gt; 6 giờ được dọn
    /// ở đường chạy thật. Mục 14: đổi tên ".xoa" rồi mới xoá (xoá dở không còn folder mang manifest ĐẠT thiếu dữ liệu).</summary>
    public int Don(SaoLuuCaiDat c, string db, DateTime now) => DonChiTiet(c, db, now).Xoa;

    public record KetQuaDon(int Xoa, string? TamDung);

    public KetQuaDon DonChiTiet(SaoLuuCaiDat c, string db, DateTime now)
    {
        int n = 0;
        string? tamDungMoi = null;
        var bo = Quet(c, db, Loai.Bo); var nh = Quet(c, db, Loai.Nhanh);
        var dk = TinhDon(c, db, now, false, bo, nh);
        var fTam = Path.Combine(ThuMucDb(c, db), TenTamDung);
        var fCho = Path.Combine(ThuMucDb(c, db), TenChoPhep);
        var boXoa = dk.Bo.Where(x => !x.Giu).ToList();
        bool choXoaBo = true;
        if (boXoa.Count > NguongXoaBo)
        {
            var cho = DocChoPhep(c, db, now);
            if (cho >= boXoa.Count)
            {
                try { File.Delete(fCho); } catch { }   // cho phép dùng 1 lần
                try { File.Delete(fTam); } catch { }
            }
            else
            {
                choXoaBo = false;
                tamDungMoi = $"lần dọn lúc {now:dd/MM/yyyy HH:mm} muốn xoá {boXoa.Count} bộ ngày (bình thường ≤ {NguongXoaBo}; cũ nhất {boXoa.Min(x => x.Ban.ThoiDiem):dd/MM/yyyy}) — đồng hồ máy chủ có đúng không, hay vừa đổi số giữ / máy tắt lâu?";
                try { File.WriteAllText(fTam, tamDungMoi); } catch (Exception ex) { _log.LogWarning(ex, "[G32-SAOLUU] Không ghi được {F}", fTam); }
                _log.LogWarning("[G32-SAOLUU] Tạm dừng dọn bộ ngày: {Msg}", tamDungMoi);
            }
        }
        else if (File.Exists(fTam)) { try { File.Delete(fTam); } catch { } }   // trở lại bình thường (≤ ngưỡng) → bỏ tạm dừng
        foreach (var (l, list) in new[] { (Loai.Bo, dk.Bo), (Loai.Nhanh, dk.Nhanh) })
        {
            var dir = ThuMucLoai(c, db, l);
            if (l == Loai.Nhanh || choXoaBo)
                foreach (var k in list.Where(x => !x.Giu))
                {
                    var d = Path.Combine(dir, k.Ban.Ten);
                    if (!TryTen(k.Ban.Ten, out _) || !File.Exists(Path.Combine(d, TenManifest))) continue;   // hàng rào: đúng mẫu + có manifest
                    if (XoaThuMuc(d)) n++;
                }
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var ten = Path.GetFileName(d);
                var goc = ten.EndsWith(DuoiXoa) ? ten[..^DuoiXoa.Length] : null;
                if (goc != null && goc.EndsWith(DuoiDangTao)) goc = goc[..^DuoiDangTao.Length];   // [G32-RA2] mục 10: "….dang-tao.xoa"
                // folder ".dang-tao" bỏ dở (mất điện / tắt app giữa chừng) quá 6 giờ; folder ".xoa" xoá dở lần trước
                if (ten.EndsWith(DuoiDangTao) && TryTen(ten[..^DuoiDangTao.Length], out var t) && t < now.AddHours(-6)) { if (XoaThuMuc(d)) n++; }
                else if (goc != null && TryTen(goc, out _)) { if (XoaThuMuc(d, daDoiTen: true)) n++; }
            }
        }
        return new KetQuaDon(n, tamDungMoi);
    }

    private bool XoaThuMuc(string d, bool daDoiTen = false)
    {
        try
        {
            var x = d;
            if (!daDoiTen)
            {
                x = d + DuoiXoa;
                if (Directory.Exists(x)) Directory.Delete(x, true);
                Directory.Move(d, x);   // từ đây folder không còn đúng mẫu tên → không ai đọc nhầm nếu xoá dở
            }
            Directory.Delete(x, true);
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "[G32-SAOLUU] Không xoá được {Dir}", d); return false; }
    }

    // ----------------------------------------------------------------- chạy
    /// <summary>Bấm tay ở tab (chỉ Admin).</summary>
    public async Task<KetQuaChay> ChayTayAsync(Loai l, int actorUserId, string actorName)
    {
        if (!await LaAdminAsync(actorUserId))
        {
            var now = Clock();
            return new KetQuaChay(false, l, "", now, now, KhongPhaiAdmin, 0);
        }
        return await ChayAsync(l, "bam-tay:" + actorName, CancellationToken.None);
    }

    public async Task<KetQuaChay> ChayAsync(Loai l, string nguon, CancellationToken ct)
    {
        var batDau = Clock();
        if (!await _chay.WaitAsync(0, ct))
            return new KetQuaChay(false, l, "", batDau, batDau, "Đang có 1 lượt sao lưu khác chạy — chờ xong rồi bấm lại.", 0, BoQua: true);
        IAsyncDisposable? khoa = null;
        string? tmp = null;
        string ten = "";
        try
        {
            if (l == Loai.Nhanh) LanThuNhanh = batDau; else LanThuBo = batDau;
            DangChay = l == Loai.Nhanh ? "Đang lưu nhanh (chỉ dữ liệu)…" : "Đang tạo bộ trọn bộ…";
            var db = await DbAsync(ct);
            var c = Store.Doc(db);
            if (!c.DaCaiThuMuc) return Ghi(new KetQuaChay(false, l, "", batDau, Clock(), "Chưa cài thư mục sao lưu (tab Cài đặt › Sao lưu).", 0));
            khoa = await Sql.ThuKhoaAsync(ct);
            if (khoa == null) return Ghi(new KetQuaChay(false, l, "", batDau, Clock(), "Một app khác (cùng DB) đang sao lưu — bỏ lượt này.", 0, BoQua: true));

            var dirLoai = ThuMucLoai(c, db, l);
            Directory.CreateDirectory(dirLoai);
            var t = batDau;
            ten = t.ToString(MauTen, CultureInfo.InvariantCulture);
            while (Directory.Exists(Path.Combine(dirLoai, ten)) || Directory.Exists(Path.Combine(dirLoai, ten + DuoiDangTao)))
            { t = t.AddSeconds(1); ten = t.ToString(MauTen, CultureInfo.InvariantCulture); }
            tmp = Path.Combine(dirLoai, ten + DuoiDangTao);
            Directory.CreateDirectory(Path.Combine(tmp, "du-lieu"));

            var m = new SaoLuuManifest
            {
                Loai = l == Loai.Nhanh ? "nhanh" : "bo-ngay",
                Nguon = nguon,
                ThoiDiem = t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Db = db,
                May = Environment.MachineName,
                PhienBanPhanMem = PhienBan(),
                ThuMucTheoMaySql = c.ThuMucTheoMaySql,
            };
            m.Commit = m.PhienBanPhanMem.Contains('+') ? m.PhienBanPhanMem[(m.PhienBanPhanMem.IndexOf('+') + 1)..] : null;
            try { m.SqlServer = await Sql.PhienBanSqlAsync(ct); } catch (Exception ex) { m.CanhBao.Add("Không đọc được phiên bản SQL: " + ex.Message); }

            // 1) Dữ liệu
            m.SoDong = await Sql.DemDongAsync(ct);
            m.GhiChuSoDong = "Số dòng XẤP XỈ (sys.partitions), đếm ngay TRƯỚC lệnh BACKUP — có người đang nhập thì lệch vài dòng là bình thường; chỉ để tham khảo.";
            var bakTen = $"{SaoLuuCaiDatStore.TenAnToan(db)}_{ten}.bak";
            var dbDir = SaoLuuCaiDatStore.TenAnToan(db);
            var bakSql = NoiSql(c.ThuMucTheoMaySql, dbDir, l == Loai.Nhanh ? ThuMucNhanh : ThuMucBo, ten + DuoiDangTao, "du-lieu", bakTen);
            try { await Sql.BackupAsync(db, bakSql, $"MES {m.Loai} {ten}", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // [G32-RA] mục 9: lỗi SQL thật (vd Operating system error 3 / 5) kèm gợi ý cụ thể
                throw new SaoLuuException($"Lệnh BACKUP của SQL Server lỗi: {ex.Message} — kiểm: (1) thư mục \"{c.ThuMucTheoMaySql}\" có thật trên máy SQL Server; " +
                    "(2) tài khoản chạy dịch vụ SQL Server có quyền ghi vào đó; (3) 2 ô thư mục (theo máy SQL / theo máy app) trỏ cùng 1 chỗ.");
            }
            var bakApp = Path.Combine(tmp, "du-lieu", bakTen);
            if (!File.Exists(bakApp))
                throw new SaoLuuException($"SQL Server đã chạy lệnh lưu vào \"{bakSql}\" nhưng app không thấy file ở \"{bakApp}\" — 2 ô thư mục (theo máy SQL / theo máy app) phải trỏ cùng 1 chỗ.");
            m.BakBytes = new FileInfo(bakApp).Length;
            m.BakSha256 = Bam(bakApp);
            var zipBak = bakApp + ".zip";
            NenFile(bakApp, zipBak);
            ThuSauKhiNen?.Invoke(zipBak);
            if (BamTrongZip(zipBak, bakTen) != m.BakSha256)
                throw new SaoLuuException("Đọc lại file nén .bak.zip không khớp mã băm file gốc — file nén hỏng.");
            File.Delete(bakApp);

            // 2) Trọn bộ
            if (l == Loai.Bo) TaoPhanBo(tmp, c, db, m);

            // 3) Băm từng file + manifest + đổi tên
            foreach (var f in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(tmp, f).Replace('\\', '/');
                var len = new FileInfo(f).Length;
                m.Files.Add(new SaoLuuManifest.FileMuc { Duong = rel, Bytes = len, Sha256 = Bam(f) });
                m.TongBytes += len;
            }
            m.KetQua = SaoLuuManifest.KqDat;
            m.KetThuc = Clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            GhiFileXuongDia(Path.Combine(tmp, TenManifest), JsonSerializer.Serialize(m, Json));
            var dich = Path.Combine(dirLoai, ten);
            DoiTenThuLai(tmp, dich);   // [G32-RA] mục 14: antivirus / indexer giữ file → thử lại vài lần
            tmp = null;

            var kq = new KetQuaChay(true, l, ten, batDau, Clock(), (l == Loai.Nhanh ? "Đã lưu nhanh " : "Đã tạo bộ trọn bộ ") + $"{ten} ({DocBytes(m.TongBytes)}).", m.TongBytes);
            try
            {
                var don = DonChiTiet(c, db, Clock());
                if (don.Xoa > 0) kq = kq with { ThongBao = kq.ThongBao + $" Dọn {don.Xoa} bản cũ theo luật giữ." };
                if (don.TamDung != null) kq = kq with { ThongBao = kq.ThongBao + " (TẠM DỪNG dọn bộ ngày: " + don.TamDung + ")" };
            }
            catch (Exception ex) { _log.LogWarning(ex, "[G32-SAOLUU] Lỗi khi dọn"); kq = kq with { ThongBao = kq.ThongBao + " (dọn bản cũ lỗi: " + ex.Message + ")" }; }
            _log.LogInformation("[G32-SAOLUU] {Msg}", kq.ThongBao);
            return Ghi(kq);
        }
        catch (Exception ex)
        {
            var msg = ex is SaoLuuException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "[G32-SAOLUU] Sao lưu {Loai} lỗi", l);
            if (tmp != null) GhiBanLoi(tmp, ten, l, nguon, batDau, msg);
            return Ghi(new KetQuaChay(false, l, ten, batDau, Clock(), "Sao lưu lỗi: " + msg, 0));
        }
        finally
        {
            DangChay = null;
            if (khoa != null) { try { await khoa.DisposeAsync(); } catch { } }
            _chay.Release();
            _canhBaoCache = null;
        }
    }

    private KetQuaChay Ghi(KetQuaChay k) { KetQuaGanNhat = k; return k; }

    /// <summary>Lỗi giữa chừng: xoá phần dở, để lại folder có manifest KetQua=LOI (đổi tên khỏi .dang-tao).</summary>
    private void GhiBanLoi(string tmp, string ten, Loai l, string nguon, DateTime batDau, string loi)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(tmp)) Directory.Delete(d, true);
            foreach (var f in Directory.GetFiles(tmp)) File.Delete(f);
            var m = new SaoLuuManifest
            {
                Loai = l == Loai.Nhanh ? "nhanh" : "bo-ngay", Nguon = nguon,
                ThoiDiem = batDau.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                KetThuc = Clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                KetQua = SaoLuuManifest.KqLoi, Loi = loi, Db = _db ?? "", May = Environment.MachineName, PhienBanPhanMem = PhienBan(),
            };
            GhiFileXuongDia(Path.Combine(tmp, TenManifest), JsonSerializer.Serialize(m, Json));
            var dir = Path.GetDirectoryName(tmp)!;
            DoiTenThuLai(tmp, Path.Combine(dir, ten));
        }
        catch (Exception ex) { _log.LogWarning(ex, "[G32-SAOLUU] Không ghi được bản lỗi {Tmp}", tmp); }
    }

    // ----------------------------------------------------------------- phần trọn bộ
    private void TaoPhanBo(string tmp, SaoLuuCaiDat c, string db, SaoLuuManifest m)
    {
        // cau-hinh: appsettings*.json + cài đặt sao lưu + nhật ký cài đặt + bản quyền + file thêm
        var ch = Path.Combine(tmp, "cau-hinh");
        Directory.CreateDirectory(ch);
        foreach (var f in Directory.GetFiles(ContentRoot, "appsettings*.json")) ChepFile(f, Path.Combine(ch, Path.GetFileName(f)));
        foreach (var f in new[] { Store.FileCaiDat(db), Store.FileNhatKy(db) })
            if (File.Exists(f)) ChepFile(f, Path.Combine(ch, "sao-luu", Path.GetFileName(f)));
        var boQua = new List<string>();
        if (Directory.Exists(ThuMucBanQuyen)) ChepThuMuc(ThuMucBanQuyen, Path.Combine(ch, "ban-quyen"), boQua);
        else m.CanhBao.Add($"Không thấy thư mục bản quyền {ThuMucBanQuyen}.");
        int i = 0;
        foreach (var f in c.FileCauHinhThem)
        {
            i++;
            var dich = Path.Combine(ch, "them", $"{i:00}_{SaoLuuCaiDatStore.TenAnToan(Path.GetFileName(f.TrimEnd('\\', '/')))}");
            if (File.Exists(f)) { try { ChepFile(f, dich); } catch (IOException ex) { boQua.Add($"{f}: {ex.Message}"); } catch (UnauthorizedAccessException ex) { boQua.Add($"{f}: {ex.Message}"); } }
            else if (Directory.Exists(f)) ChepThuMuc(f, dich, boQua);
            else m.CanhBao.Add($"File cấu hình thêm không tồn tại: {f}");
            m.FileCauHinhThem.Add($"{Path.GetRelativePath(tmp, dich).Replace('\\', '/')} ← {f}");
        }

        // uploads (ảnh)
        var up = Path.Combine(ContentRoot, "wwwroot", "uploads");
        Directory.CreateDirectory(Path.Combine(tmp, "uploads"));
        NenThuMuc(new[] { (up, "") }, Path.Combine(tmp, "uploads", "uploads.zip"), _ => true, c.ThuMucApp);

        // phần mềm
        Directory.CreateDirectory(Path.Combine(tmp, "phan-mem"));
        var zipPm = Path.Combine(tmp, "phan-mem", "MES.Web.zip");
        bool laPublish = File.Exists(Path.Combine(ContentRoot, "MES.Web.dll"));
        if (laPublish)
        {
            m.NguonPhanMem = "publish: " + ContentRoot;
            NenThuMuc(new[] { (ContentRoot, "") }, zipPm, rel => !BoQuaPhanMem(rel), c.ThuMucApp);
        }
        else
        {
            m.NguonPhanMem = $"build (chạy dotnet run): {BaseDir} + wwwroot";
            NenThuMuc(new[] { (BaseDir, ""), (Path.Combine(ContentRoot, "wwwroot"), "wwwroot/") }, zipPm, rel => !BoQuaPhanMem(rel), c.ThuMucApp);
        }

        // khoi-phuc (script + hướng dẫn — mẫu đặt ở <ContentRoot>\SaoLuuMau, Gói 32b)
        var mau = Path.Combine(ContentRoot, "SaoLuuMau");
        if (Directory.Exists(mau)) ChepThuMuc(mau, Path.Combine(tmp, "khoi-phuc"), boQua);
        else m.CanhBao.Add("Chưa có script khôi phục mẫu (SaoLuuMau) — Gói 32b.");
        foreach (var b in boQua) m.CanhBao.Add("Không chép được (đang khoá?): " + b);

        GhiFileXuongDia(Path.Combine(tmp, "DOC-TRUOC.txt"), DocTruoc(m));
    }

    private static bool BoQuaPhanMem(string rel)
    {
        var r = rel.Replace('\\', '/');
        return r.StartsWith("logs/", StringComparison.OrdinalIgnoreCase)                 // log ghi liên tục (Program.cs [LOG-FILE])
            || r.StartsWith("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase);       // ảnh — đã nén riêng ở uploads\
    }

    private static string DocTruoc(SaoLuuManifest m) =>
$@"BỘ SAO LƯU TRỌN BỘ MES — {m.ThoiDiem}
DB: {m.Db}   ·   Máy app: {m.May}   ·   Phần mềm: {m.PhienBanPhanMem}
SQL Server: {m.SqlServer}

du-lieu\   : file sao lưu DB (.bak nén zip). Giải nén ra .bak rồi khôi phục bằng SQL Server (RESTORE DATABASE).
cau-hinh\  : appsettings*.json (chuỗi kết nối, cổng), sao-luu\ (cài đặt sao lưu), ban-quyen\, them\ (file thêm, vd Caddyfile).
uploads\   : uploads.zip — ảnh (ảnh đại diện Sơ đồ tổ chức…), giải nén vào wwwroot\uploads.
phan-mem\  : MES.Web.zip — đúng bản phần mềm đang chạy lúc lưu (dữ liệu + phần mềm phải đi cùng nhau).
khoi-phuc\ : script + hướng dẫn khôi phục (nếu có).
manifest.json : thời điểm, phiên bản, số dòng từng bảng lúc lưu, mã băm SHA-256 từng file (kiểm file còn nguyên).

Lưu ý: bản quyền gắn với máy — dựng lại trên máy MỚI phải kích hoạt lại bản quyền.
";

    // ----------------------------------------------------------------- tiện ích file
    /// <summary>[G32-RA] mục 13: ghi hẳn xuống đĩa (Flush(true)) trước khi đổi tên folder thành bản ĐẠT.</summary>
    private static void GhiFileXuongDia(string f, string text)
    {
        using var fs = new FileStream(f, FileMode.Create, FileAccess.Write);
        var b = new UTF8Encoding(false).GetBytes(text);
        fs.Write(b, 0, b.Length);
        fs.Flush(true);
    }

    private static void DoiTenThuLai(string src, string dst)
    {
        for (int i = 0; ; i++)
        {
            try { Directory.Move(src, dst); return; }
            catch (IOException) when (i < 4) { Thread.Sleep(500 * (i + 1)); }
            catch (UnauthorizedAccessException) when (i < 4) { Thread.Sleep(500 * (i + 1)); }
        }
    }

    public static string NoiSql(string goc, params string[] phan)
    {
        char sep = goc.Contains('/') && !goc.Contains('\\') ? '/' : '\\';
        var sb = new StringBuilder(goc.TrimEnd('\\', '/'));
        foreach (var p in phan) sb.Append(sep).Append(p);
        return sb.ToString();
    }

    public static string Bam(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static string BamTrongZip(string zip, string entry)
    {
        using var za = ZipFile.OpenRead(zip);
        var e = za.GetEntry(entry) ?? throw new SaoLuuException("File nén không có " + entry);
        using var s = e.Open();
        return Convert.ToHexString(SHA256.HashData(s));
    }

    private static void NenFile(string src, string zip)
    {
        using var fs = new FileStream(zip, FileMode.CreateNew);
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = za.CreateEntry(Path.GetFileName(src), CompressionLevel.Optimal);
            using var es = e.Open();
            using var ss = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            ss.CopyTo(es);
        }
        fs.Flush(true);   // [G32-RA] mục 13
    }

    /// <summary>Nén nhiều thư mục nguồn (kèm tiền tố đường dẫn trong zip). File đang bị khoá đọc không được → bỏ qua, ghi vào zip 1 file _BO-QUA.txt.</summary>
    private static void NenThuMuc(IEnumerable<(string Dir, string TienTo)> nguon, string zip, Func<string, bool> lay, string? boQuaGoc = null)
    {
        var bq = string.IsNullOrWhiteSpace(boQuaGoc) ? null : Path.GetFullPath(boQuaGoc).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var boQua = new List<string>();
        using var fs = new FileStream(zip, FileMode.CreateNew);
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
        foreach (var (dir, tt) in nguon)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var rel = tt + Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (!lay(rel)) continue;
                if (bq != null && Path.GetFullPath(f).StartsWith(bq, StringComparison.OrdinalIgnoreCase)) continue;   // thư mục sao lưu nằm trong thư mục app → không nén chính nó
                try
                {
                    using var ss = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var e = za.CreateEntry(rel, CompressionLevel.Optimal);
                    using var es = e.Open();
                    ss.CopyTo(es);
                }
                catch (IOException) { boQua.Add(rel); }
                catch (UnauthorizedAccessException) { boQua.Add(rel); }
            }
        }
        if (boQua.Count > 0)
        {
            var e = za.CreateEntry("_BO-QUA.txt");
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(true));
            w.WriteLine("File không đọc được lúc nén (đang bị khoá):");
            foreach (var b in boQua) w.WriteLine(b);
        }
        }
        fs.Flush(true);   // [G32-RA] mục 13
    }

    private static void ChepFile(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        using var ss = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ds = new FileStream(dst, FileMode.CreateNew);
        ss.CopyTo(ds);
        ds.Flush(true);   // [G32-RA2] mục 12
    }

    /// <summary>[G32-RA] mục 10: file đang khoá / không đọc được → bỏ qua, ghi vào <paramref name="boQua"/> (trước: 1 file khoá làm hỏng cả bộ).</summary>
    private static void ChepThuMuc(string src, string dst, List<string> boQua)
    {
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            try { ChepFile(f, Path.Combine(dst, Path.GetRelativePath(src, f))); }
            catch (IOException ex) { boQua.Add($"{f}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { boQua.Add($"{f}: {ex.Message}"); }
        }
        Directory.CreateDirectory(dst);
    }

    public static string DocBytes(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB" : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";

    public static string PhienBan() =>
        typeof(SaoLuuService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SaoLuuService).Assembly.GetName().Version?.ToString() ?? "?";

    // ----------------------------------------------------------------- lịch (SaoLuuWorker gọi mỗi 30 giây)
    /// <summary>Đến hạn lượt nào (null = chưa). Bộ ngày: chưa có bộ ĐẠT từ mốc chốt gần nhất (lỗi thì thử lại sau 30 phút).
    /// Nhanh: quá N phút từ bản đạt gần nhất (nhanh hoặc bộ), lỗi thì thử lại sau min(N, 15) phút.
    /// [G32-RA 2026-11-22] mục 1: bản có giờ SAU giờ máy (+5 phút) và bản không rõ không được tính là "gần nhất" — trước: 1 bản ghi giờ tương lai
    /// (đồng hồ từng bị chỉnh sai rồi chỉnh lại) làm lịch dừng hẳn mà băng không báo.</summary>
    public Loai? DenHan(SaoLuuCaiDat c, string db, DateTime now)
    {
        if (!c.BatTuDong || !c.DaCaiThuMuc || !c.TryGioChot(out var gio)) return null;
        var han = now + LechGioChoPhep;
        // KHÔNG RÕ (đọc manifest lỗi thoáng qua) không tính là đạt nhưng tính là "đã thử" (không tạo thêm trọn bộ vì 1 lần đọc lỗi — [G32-RA2] mục 11)
        var bo = Quet(c, db, Loai.Bo).Where(b => b.ThoiDiem <= han).ToList();
        var nh = Quet(c, db, Loai.Nhanh).Where(b => b.ThoiDiem <= han).ToList();
        var chotNay = now.Date + gio;
        var chotGanNhat = now >= chotNay ? chotNay : chotNay.AddDays(-1);
        var boDatGanNhat = bo.Where(b => b.Dat).Select(b => (DateTime?)b.ThoiDiem).Max();
        var thuBo = Max(LanThuBo is { } lb && lb <= han ? lb : null, bo.Select(b => (DateTime?)b.ThoiDiem).Max());
        if ((boDatGanNhat == null || boDatGanNhat < chotGanNhat)
            && (thuBo == null || thuBo < chotGanNhat || now - thuBo.Value >= TimeSpan.FromMinutes(30)))
            return Loai.Bo;

        var datGanNhat = Max(boDatGanNhat, nh.Where(b => b.Dat).Select(b => (DateTime?)b.ThoiDiem).Max());
        var thuNhanh = Max(LanThuNhanh is { } ln && ln <= han ? ln : null, nh.Select(b => (DateTime?)b.ThoiDiem).Max());
        var chuKy = TimeSpan.FromMinutes(c.PhutLuuNhanh);
        var thuLai = TimeSpan.FromMinutes(Math.Min(c.PhutLuuNhanh, 15));
        if ((datGanNhat == null || now - datGanNhat.Value >= chuKy)
            && (thuNhanh == null || now - thuNhanh.Value >= thuLai))
            return Loai.Nhanh;
        return null;
    }

    private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);

    // ----------------------------------------------------------------- băng cảnh báo (Admin)
    public record CanhBao(bool Do, string NoiDung);
    private sealed record CacheCanhBao(DateTime At, List<CanhBao> V);   // [G32-RA] mục 18: kiểu tham chiếu — gán / đọc nguyên khối giữa các luồng
    private CacheCanhBao? _canhBaoCache;

    /// <summary>Các cảnh báo cho băng đầu trang (cache 60 giây). Băng gọi qua Task.Run (quét thư mục mạng chậm không làm đơ trang).</summary>
    public async Task<List<CanhBao>> CanhBaoAsync()
    {
        var now = Clock();
        var cc = _canhBaoCache;
        if (cc != null && (now - cc.At) < TimeSpan.FromSeconds(60) && now >= cc.At) return cc.V;
        var v = new List<CanhBao>();
        try
        {
            var db = await DbAsync();
            var c = Store.Doc(db);
            v = TinhCanhBao(c, db, now);
        }
        catch (Exception ex) { v.Add(new CanhBao(true, "Không đọc được cài đặt sao lưu — " + ex.Message + " (mở Cài đặt › Sao lưu, nhập lại rồi Lưu: file cũ được giữ lại)")); }
        _canhBaoCache = new CacheCanhBao(now, v);
        return v;
    }

    public List<CanhBao> TinhCanhBao(SaoLuuCaiDat c, string db, DateTime now, List<BanInfo>? boDs = null, List<BanInfo>? nhDs = null)
    {
        var v = new List<CanhBao>();
        if (!c.DaCaiThuMuc) { v.Add(new CanhBao(true, "Chưa cài thư mục sao lưu — dữ liệu CHƯA được sao lưu tự động (Cài đặt › Sao lưu).")); return v; }
        if (!c.BatTuDong) v.Add(new CanhBao(false, "Sao lưu tự động đang TẮT (Cài đặt › Sao lưu)."));
        List<BanInfo> bo, nh;
        try { bo = boDs ?? Quet(c, db, Loai.Bo); nh = nhDs ?? Quet(c, db, Loai.Nhanh); }
        catch (Exception ex) { v.Add(new CanhBao(true, ex is SaoLuuException ? ex.Message : $"Không đọc được thư mục sao lưu {ThuMucDb(c, db)} — {ex.Message}")); return v; }
        var han = now + LechGioChoPhep;
        // [G32-RA] mục 1 + 2: bản "tương lai" / không rõ → báo riêng, không tính là gần nhất
        var tuongLai = bo.Concat(nh).Where(b => b.ThoiDiem > han).OrderBy(b => b.ThoiDiem).ToList();
        if (tuongLai.Count > 0)   // [G32-RA2] vàng: lịch không còn bị ảnh hưởng, chỉ để Admin biết đồng hồ từng sai
            v.Add(new CanhBao(false, $"Có {tuongLai.Count} bản sao lưu ghi giờ SAU giờ máy hiện tại (vd {tuongLai[0].ThoiDiem:dd/MM/yyyy HH:mm}) — kiểm đồng hồ máy chủ; các bản này được giữ, không tính là bản mới nhất."));
        var khongRo = bo.Concat(nh).Where(b => b.KhongRo).ToList();
        if (khongRo.Count > 0)
            v.Add(new CanhBao(true, $"{khongRo.Count} bản không đọc được manifest (vd {khongRo[0].Ten}: {khongRo[0].Loi}) — được giữ, không dọn; kiểm thư mục sao lưu."));
        var tamDung = DocTamDung(c, db);
        if (tamDung != null) v.Add(new CanhBao(false, $"Tạm dừng dọn bộ ngày: {tamDung} Đồng hồ sai thì chỉnh giờ trước; số bộ sẽ xoá đúng thì bấm \"Cho dọn lại\" (nhập PIN) ở tab Sao lưu."));
        // cùng giây (tên folder tính tới giây): bản LỖI xếp trước — báo lỗi thay vì che bởi bản đạt cùng giây (drive_g32 T5b bắt 22/11)
        var tatCa = bo.Concat(nh).Where(b => b.ThoiDiem <= han && !b.KhongRo).OrderByDescending(b => b.ThoiDiem).ThenBy(b => b.Dat).ToList();
        var moi = tatCa.FirstOrDefault();
        if (moi != null && !moi.Dat) v.Add(new CanhBao(true, $"Lần sao lưu lúc {moi.ThoiDiem:dd/MM HH:mm} bị LỖI: {moi.Loi}"));
        else if (KetQuaGanNhat is { Dat: false, BoQua: false } k && k.Ten == "" && k.KetThuc > (moi?.ThoiDiem ?? DateTime.MinValue))
            v.Add(new CanhBao(true, $"Lần sao lưu lúc {k.BatDau:dd/MM HH:mm} không chạy được: {k.ThongBao}"));
        if (!c.BatTuDong) return v;
        var datGanNhat = tatCa.Where(b => b.Dat).Select(b => (DateTime?)b.ThoiDiem).FirstOrDefault();
        var nguong = TimeSpan.FromMinutes(c.PhutLuuNhanh * 2 + 5);
        if (datGanNhat == null) v.Add(new CanhBao(true, "Chưa có bản sao lưu nào đạt."));
        else if (now - datGanNhat.Value > nguong)
            v.Add(new CanhBao(true, $"Đã {KhoangThoiGian(now - datGanNhat.Value)} chưa có bản sao lưu mới (gần nhất {datGanNhat:dd/MM HH:mm})."));
        var boDat = tatCa.Where(b => b.Dat && b.Loai == Loai.Bo).Select(b => (DateTime?)b.ThoiDiem).FirstOrDefault();
        if (datGanNhat != null && (boDat == null || now - boDat.Value > TimeSpan.FromHours(26)))
            v.Add(new CanhBao(true, boDat == null ? "Chưa có bộ ngày (trọn bộ) nào đạt." : $"Quá 26 giờ chưa có bộ ngày trọn bộ (gần nhất {boDat:dd/MM HH:mm})."));
        return v;
    }

    public static string KhoangThoiGian(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} ngày {t.Hours} giờ" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} giờ {t.Minutes} phút" : $"{(int)t.TotalMinutes} phút";
}

public interface ISaoLuuSql
{
    Task<string> TenDbAsync(CancellationToken ct);
    Task<string> PhienBanSqlAsync(CancellationToken ct);
    /// <summary>Số dòng từng bảng (schema.bảng → số dòng).</summary>
    Task<SortedDictionary<string, long>> DemDongAsync(CancellationToken ct);
    /// <summary>BACKUP DATABASE … TO DISK = <paramref name="duongDanTheoMaySql"/> (đường dẫn nhìn từ máy SQL Server).</summary>
    Task BackupAsync(string db, string duongDanTheoMaySql, string nhan, CancellationToken ct);
    /// <summary>Khoá "chỉ 1 nơi sao lưu DB này cùng lúc" (2 app cùng trỏ 1 DB). Không lấy được → null.</summary>
    Task<IAsyncDisposable?> ThuKhoaAsync(CancellationToken ct);
}

public class SaoLuuException : Exception { public SaoLuuException(string m) : base(m) { } }

/// <summary>[G32-SAOLUU] manifest.json của mỗi bản (đọc được bằng mắt; script khôi phục / đẩy Google đọc).</summary>
public class SaoLuuManifest
{
    public const string KqDat = "DAT", KqLoi = "LOI";
    public int PhienBanManifest { get; set; } = 1;
    public string Loai { get; set; } = "";
    public string? Nguon { get; set; }
    public string ThoiDiem { get; set; } = "";
    public string? KetThuc { get; set; }
    public string KetQua { get; set; } = KqLoi;
    public string? Loi { get; set; }
    public string Db { get; set; } = "";
    public string May { get; set; } = "";
    public string PhienBanPhanMem { get; set; } = "";
    public string? Commit { get; set; }
    public string? SqlServer { get; set; }
    public string? ThuMucTheoMaySql { get; set; }
    public string? NguonPhanMem { get; set; }
    public long BakBytes { get; set; }
    public string? BakSha256 { get; set; }
    public long TongBytes { get; set; }
    public SortedDictionary<string, long> SoDong { get; set; } = new(StringComparer.Ordinal);
    public string? GhiChuSoDong { get; set; }
    public List<string> FileCauHinhThem { get; set; } = new();
    public List<string> CanhBao { get; set; } = new();
    public List<FileMuc> Files { get; set; } = new();
    public class FileMuc { public string Duong { get; set; } = ""; public long Bytes { get; set; } public string Sha256 { get; set; } = ""; }
}
