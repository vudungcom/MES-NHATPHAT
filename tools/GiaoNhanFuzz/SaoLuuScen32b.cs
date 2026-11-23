// [G32B-SCEN 2026-11-23] Kịch bản `saoluu` phần Gói 32b (cùng lớp SaoLuuScen — partial).
//   S12 Bộ trước nâng cấp: thư mục truoc-nang-cap, trọn bộ + khoi-phuc\, manifest Loai/phiên bản phần mềm ĐƯỢC LƯU, không đụng lịch bộ ngày,
//       luật giữ N bộ (tay + ngẫu nhiên × đáp án độc lập), xem trước khi giảm số giữ
//   S13 Xoá tay 1 bản: chỉ Admin, không xoá bản ĐẠT mới nhất, tên lạ, đang chạy, ghi nhật ký; ổ đầy → bản LỖI có chữ "Ổ ĐẦY",
//       hàng rào giữ mọi bản, xoá tay gỡ được
//   S14 Bộ nhớ đệm manifest bỏ mục của folder đã xoá (qua dọn / xoá tay / xoá ngoài)
//   S15 Băng ổ đầy: < 2 lần cỡ bộ ngày → vàng, < 1 lần → đỏ, đủ / không đo được → không báo
//   S16 Script PowerShell (MES.Web\SaoLuuMau — chạy powershell.exe 5.1 trên trạm Windows / pwsh trên sandbox; SQL + dịch vụ ở CHẾ ĐỘ THỬ
//       MES_SAOLUU_GIA, chép / giải nén / băm file chạy thật): khoi-phuc -ChiKiem (nguyên vẹn / sửa 1 byte / thiếu file), -XemTruoc không ghi gì,
//       chạy thật (thứ tự lệnh, MOVE đúng chỗ file cũ, phần mềm + cấu hình + ảnh, bản quyền cùng máy / máy mới), -ChiDuLieu bản nhanh,
//       SQL cũ hơn bản lưu → dừng; cai-dich-vu (tạo / đã có / -ChiCapQuyen); nang-cap (đạt / sao lưu lỗi / bản mới không lên → trả bản cũ)
//   Nguồn: HÙNG 23/11 13:20 OK Gói 32b (câu B1–B4).
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MES.Web.Services;

public static partial class SaoLuuScen
{
    // ------------------------------------------------------------------ S12
    static async Task S12()
    {
        Console.WriteLine("[S12] Bộ trước nâng cấp");
        var e = MkEnv();
        var mau = TimSaoLuuMau();
        Ok(mau != null, "S12 tiền đề: thấy MES.Web/SaoLuuMau trong repo", Directory.GetCurrentDirectory());
        if (mau != null) ChepThu(mau, Path.Combine(e.ContentRoot, "SaoLuuMau"));
        var kq = await e.Svc.ChayAsync(SaoLuuService.Loai.TruocNangCap, "truoc-nang-cap:thu", CancellationToken.None);
        var dir = Path.Combine(e.Goc, "MES", "truoc-nang-cap", "2026-11-22_100000");
        Ok(kq.Dat && Directory.Exists(dir) && !Directory.Exists(Path.Combine(e.Goc, "MES", "bo")), "S12a chạy → folder truoc-nang-cap\\<tên> ĐẠT, không tạo bộ ngày", kq.ThongBao);
        var m = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")))!;
        bool F(string rel) => File.Exists(Path.Combine(dir, rel));
        Ok(m.Loai == "truoc-nang-cap" && F("phan-mem/MES.Web.zip") && F("cau-hinh/appsettings.json") && F("uploads/uploads.zip") && F("khoi-phuc/khoi-phuc.ps1")
           && F("khoi-phuc/khoi-phuc.bat") && F("khoi-phuc/HUONG-DAN-KHOI-PHUC.txt") && !m.CanhBao.Any(x => x.Contains("SaoLuuMau")),
           "S12b manifest Loai = truoc-nang-cap, trọn bộ + khoi-phuc\\ (script + hướng dẫn)", string.Join(" | ", m.CanhBao));
        Ok(kq.ThongBao.StartsWith("Đã tạo bộ trước nâng cấp") && e.Svc.LanThuBo == null && e.Svc.DenHan(e.Svc.Store.Doc("MES"), "MES", e.Svc.Clock()) == SaoLuuService.Loai.Bo,
           "S12c không tính vào lịch bộ ngày (LanThuBo trống, lịch vẫn đòi bộ ngày)", $"{e.Svc.LanThuBo} {e.Svc.DenHan(e.Svc.Store.Doc("MES"), "MES", e.Svc.Clock())}");
        Ok(m.Files.All(f => Sha(Path.Combine(dir, f.Duong)) == f.Sha256), "S12d mã băm từng file khớp manifest");
        // mẫu khôi phục chỉ có ở thư mục exe (nâng cấp: exe mới + --contentRoot thư mục cũ chưa có SaoLuuMau)
        var e2 = MkEnv();
        if (mau != null) ChepThu(mau, Path.Combine(e2.Svc.BaseDir, "SaoLuuMau"));
        var dllTk = typeof(JsonSerializer).Assembly.Location;   // 1 dll thật có ProductVersion (giả "bản cũ" ở ContentRoot)
        File.Copy(dllTk, Path.Combine(e2.ContentRoot, "MES.Web.dll"), true);
        var k2 = await e2.Svc.ChayAsync(SaoLuuService.Loai.TruocNangCap, "x", CancellationToken.None);
        var d2 = Path.Combine(e2.Goc, "MES", "truoc-nang-cap", "2026-11-22_100000");
        var m2 = JsonSerializer.Deserialize<SaoLuuManifest>(File.ReadAllText(Path.Combine(d2, "manifest.json")))!;
        var verCu = FileVersionInfo.GetVersionInfo(dllTk).ProductVersion;
        Ok(k2.Dat && File.Exists(Path.Combine(d2, "khoi-phuc", "khoi-phuc.ps1")), "S12e ContentRoot chưa có SaoLuuMau → lấy ở thư mục exe", k2.ThongBao);
        Ok(!string.IsNullOrEmpty(verCu) && m2.PhienBanPhanMem == verCu && m2.PhienBanPhanMem != SaoLuuService.PhienBan(),
           "S12f phiên bản ghi manifest = bản ĐƯỢC NÉN (MES.Web.dll ở ContentRoot), không phải exe đang chạy", $"{m2.PhienBanPhanMem} / {verCu} / {SaoLuuService.PhienBan()}");

        // luật giữ: tay
        var c = new SaoLuuCaiDat { GiuTruocNangCap = 3 };
        var now = new DateTime(2026, 11, 22, 12, 0, 0);
        var ds = Enumerable.Range(1, 5).Select(i => new SaoLuuLuatDon.Ban($"t{i}", now.AddDays(-10 + i), true)).ToList();
        ds.Add(new SaoLuuLuatDon.Ban("t6", now.AddDays(-1), false));
        ds.Add(new SaoLuuLuatDon.Ban("tKhongRo", now.AddDays(-30), false, KhongRo: true));
        ds.Add(new SaoLuuLuatDon.Ban("tTuongLai", now.AddDays(3), true));
        var r = SaoLuuLuatDon.TinhTruocNangCap(ds, c, now).ToDictionary(x => x.Ban.Ten, x => x.Giu);
        Ok(!r["t1"] && !r["t2"] && r["t3"] && r["t4"] && r["t5"] && r["t6"] && r["tKhongRo"] && r["tTuongLai"],
           "S12g giữ 3 bộ đạt mới nhất + bộ lỗi trong 3 bộ mới nhất + không rõ + giờ tương lai; xoá 2 bộ đạt cũ", string.Join(",", r.Select(x => $"{x.Key}={x.Value}")));
        // ngẫu nhiên × đáp án độc lập
        var rnd = new Random(32023); int bad = 0;
        for (int it = 0; it < 1500; it++)
        {
            var cc = new SaoLuuCaiDat { GiuTruocNangCap = rnd.Next(1, 6) };
            var n = rnd.Next(0, 14);
            var dd = Enumerable.Range(0, n).Select(i => new SaoLuuLuatDon.Ban($"b{i:00}", now.AddHours(-rnd.Next(-30, 2000)), rnd.NextDouble() < 0.8, rnd.NextDouble() < 0.07)).ToList();
            var kqr = SaoLuuLuatDon.TinhTruocNangCap(dd, cc, now);
            foreach (var x in kqr)
            {
                var b = x.Ban; bool giu;
                if (b.KhongRo || b.ThoiDiem > now) giu = true;
                else
                {
                    // đáp án: số bản (không rõ / tương lai bỏ) mới hơn hẳn b, tính riêng bản đạt — tên lớn hơn = mới hơn khi cùng giờ
                    bool MoiHon(SaoLuuLuatDon.Ban y) => !y.KhongRo && y.ThoiDiem <= now && (y.ThoiDiem > b.ThoiDiem || (y.ThoiDiem == b.ThoiDiem && string.CompareOrdinal(y.Ten, b.Ten) > 0));
                    int datMoiHon = dd.Count(y => y.Dat && MoiHon(y)), moiHon = dd.Count(MoiHon);
                    giu = b.Dat ? datMoiHon < cc.GiuTruocNangCap : moiHon < cc.GiuTruocNangCap;
                }
                if (giu != x.Giu) { bad++; if (bad <= 3) Console.WriteLine($"     lệch: {b} N={cc.GiuTruocNangCap} → {x.Giu}"); }
            }
            var datQuaKhu = dd.Count(y => y.Dat && !y.KhongRo && y.ThoiDiem <= now);
            if (kqr.Count(x => x.Giu && x.Ban.Dat && !x.Ban.KhongRo && x.Ban.ThoiDiem <= now) != Math.Min(cc.GiuTruocNangCap, datQuaKhu)) bad++;
        }
        Ok(bad == 0, "S12h 1.500 lượt ngẫu nhiên: luật giữ = đáp án độc lập; số bộ đạt giữ = min(N, số bộ đạt)", $"{bad} lệch");
        // dọn thật trên đĩa + xem trước khi giảm số giữ
        var e3 = MkEnv(now: now);
        for (int i = 1; i <= 5; i++) MkSetDisk(e3.Goc, "truoc-nang-cap", now.AddDays(-10 + i), MfDat);
        var c3 = e3.Svc.Store.Doc("MES");
        var xt = await e3.Svc.XemTruocXoaAsync(new SaoLuuCaiDat { ThuMucTheoMaySql = e3.Goc, GiuTruocNangCap = 2 });
        Ok(xt.Count(x => x.StartsWith("Bộ trước nâng cấp")) == 3 && xt.Count == 3, "S12i giảm Giữ bộ trước nâng cấp 10 → 2: xem trước báo 3 bộ sẽ xoá (hỏi PIN)", string.Join(" | ", xt));
        c3.GiuTruocNangCap = 2; e3.Svc.Store.Ghi("MES", c3, "thu", "x", now);
        var don = e3.Svc.DonChiTiet(c3, "MES", now);
        var con = Directory.GetDirectories(Path.Combine(e3.Goc, "MES", "truoc-nang-cap")).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Ok(don.Xoa == 3 && con.SequenceEqual(new[] { now.AddDays(-6).ToString("yyyy-MM-dd_HHmmss"), now.AddDays(-5).ToString("yyyy-MM-dd_HHmmss") }) && don.TamDung == null,
           "S12j dọn giữ đúng 2 bộ mới nhất, không tạm dừng (luật tạm dừng chỉ áp bộ ngày)", $"xoá {don.Xoa} còn {string.Join(",", con)}");
        // cài đặt: kiểm lỗi + nhật ký
        var cx = new SaoLuuCaiDat { ThuMucTheoMaySql = e3.Goc, GiuTruocNangCap = 0 };
        Ok(cx.KiemLoi().Any(x => x.Contains("trước nâng cấp")) && SaoLuuCaiDat.MoTaThayDoi(new SaoLuuCaiDat(), new SaoLuuCaiDat { GiuTruocNangCap = 4 }).Contains("Giữ bộ trước nâng cấp: 10 → 4"),
           "S12k ô Giữ bộ trước nâng cấp: 0 bị chặn, đổi ghi nhật ký, mặc định 10");
    }

    // ------------------------------------------------------------------ S13
    static async Task S13()
    {
        Console.WriteLine("[S13] Xoá tay 1 bản");
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None);
        e.Svc.Clock = () => now.AddDays(1);   // ngày sau (cùng ngày thì luật bộ ngày chỉ giữ bộ mới nhất của ngày)
        await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None);
        string Bo(DateTime t) => Path.Combine(e.Goc, "MES", "bo", t.ToString("yyyy-MM-dd_HHmmss"));
        var t0 = now.ToString("yyyy-MM-dd_HHmmss"); var t1 = now.AddDays(1).ToString("yyyy-MM-dd_HHmmss");
        var a = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, t0, 2, "khong-admin");
        Ok(a == SaoLuuService.KhongPhaiAdmin && Directory.Exists(Bo(now)), "S13a không phải Admin → chặn, không xoá", a);
        var b = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, t1, 1, "admin");
        Ok(b != null && b.Contains("ĐẠT mới nhất") && Directory.Exists(Bo(now.AddDays(1))), "S13b bộ ĐẠT mới nhất → không xoá được", b);
        var x = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, "../../x", 1, "admin");
        var y = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, "2026-01-01_000000", 1, "admin");
        Ok(x == "Tên bản không hợp lệ." && y != null && y.Contains("Không thấy"), "S13c tên lạ / không có → chặn", $"{x} | {y}");
        var ok = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, t0, 1, "admin");
        var nk = e.Svc.Store.DocNhatKy("MES", 5);
        Ok(ok == null && !Directory.Exists(Bo(now)) && !Directory.Exists(Bo(now) + SaoLuuService.DuoiXoa) && nk.Any(l => l.Contains("admin") && l.Contains("Xoá tay bộ ngày " + t0)),
           "S13d Admin xoá bộ cũ → folder mất hẳn, nhật ký ghi người + bản + cỡ", $"{ok} | {string.Join(" / ", nk)}");
        // đang chạy → không xoá
        e.Svc.Clock = () => now.AddDays(1).AddHours(2);
        e.Sql.ChamMs = 1500;
        var chay = e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "x", CancellationToken.None);
        await Task.Delay(300);
        var z = await e.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, t1, 1, "admin");
        await chay; e.Sql.ChamMs = 0;
        Ok(z != null && z.Contains("Đang có 1 lượt"), "S13e đang có lượt sao lưu chạy → không xoá", z);
        // ổ đầy: bản LỖI ghi rõ, hàng rào giữ mọi bản, xoá tay gỡ được
        var e2 = MkEnv(now: now);
        for (int i = 0; i < 6; i++) { e2.Svc.Clock = () => now.AddDays(-20 + i); await e2.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None); }
        e2.Svc.Clock = () => now; e2.Sql.FailODay = 1;
        var kl = await e2.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None);
        var c2 = e2.Svc.Store.Doc("MES");
        var dk = e2.Svc.TinhDon(c2, "MES", now);
        Ok(!kl.Dat && kl.ThongBao.Contains("Ổ ĐẦY") && dk.Bo.All(q => q.Giu), "S13f ổ đầy → bản LỖI có chữ \"Ổ ĐẦY\" + gợi ý xoá; bản mới nhất lỗi → hàng rào không dọn gì (đường cụt cũ)", kl.ThongBao);
        var ds = e2.Svc.Quet(c2, "MES", SaoLuuService.Loai.Bo);
        var moiDat = SaoLuuService.MoiNhatDat(ds, now)!;
        int xoa = 0;
        foreach (var q in ds.Where(q => q.Ten != moiDat.Ten)) if (await e2.Svc.XoaBanAsync(SaoLuuService.Loai.Bo, q.Ten, 1, "admin") == null) xoa++;
        var con = e2.Svc.Quet(c2, "MES", SaoLuuService.Loai.Bo);
        Ok(xoa == 6 && con.Count == 1 && con[0].Ten == moiDat.Ten && con[0].Dat, "S13g Admin xoá tay 5 bộ cũ + bản LỖI → còn đúng bộ ĐẠT mới nhất", $"xoá {xoa} còn {con.Count}");
        Ok(SaoLuuService.LaODay(new IOException("x", unchecked((int)0x80070027))) && !SaoLuuService.LaODay(new IOException("Access denied", unchecked((int)0x80070005)))
           && SaoLuuService.LaODay(new InvalidOperationException("BACKUP failed", new Exception("There is insufficient free space on disk volume"))),
           "S13h nhận ra lỗi ổ đầy (HRESULT 112 / 39, câu SQL) — lỗi quyền thì không");
    }

    // ------------------------------------------------------------------ S14
    static async Task S14()
    {
        Console.WriteLine("[S14] Bộ nhớ đệm manifest");
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        for (int i = 0; i < 4; i++) { e.Svc.Clock = () => now.AddMinutes(i); await e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "x", CancellationToken.None); }
        var c = e.Svc.Store.Doc("MES");
        var q = e.Svc.Quet(c, "MES", SaoLuuService.Loai.Nhanh);
        int n0 = e.Svc.SoMucCache;
        await e.Svc.XoaBanAsync(SaoLuuService.Loai.Nhanh, q[3].Ten, 1, "admin");
        e.Svc.Quet(c, "MES", SaoLuuService.Loai.Nhanh);
        int n1 = e.Svc.SoMucCache;
        Directory.Delete(q[2].DuongDan, true);
        var q2 = e.Svc.Quet(c, "MES", SaoLuuService.Loai.Nhanh);
        int n2 = e.Svc.SoMucCache;
        Ok(n0 == 4 && n1 == 3 && n2 == 2 && q2.Count == 2, "S14a xoá tay / xoá ngoài → lần quét sau bỏ mục khỏi bộ nhớ đệm (trước: nằm mãi)", $"{n0} → {n1} → {n2}");
        e.Svc.Quet(c, "MES", SaoLuuService.Loai.Bo);
        Ok(e.Svc.SoMucCache == 2, "S14b quét loại khác (bộ ngày trống) không xoá nhầm mục của bản nhanh", e.Svc.SoMucCache.ToString());
    }

    // ------------------------------------------------------------------ S15
    static void S15()
    {
        Console.WriteLine("[S15] Băng ổ đầy");
        // rà độc lập 23/11 (Thấp 10): nhận "ổ đầy" chỉ khi đúng là hết chỗ — "Insufficient system resources / memory" không phải ổ đầy
        Ok(SaoLuuService.LaODay(new Exception("There is insufficient free space on disk volume 'D:\\' to create the database."))
           && SaoLuuService.LaODay(new Exception("Write on \"D:\\x.bak\" failed: 112(There is not enough space on the disk.)"))
           && SaoLuuService.LaODay(new IOException("x", unchecked((int)0x80070070)))
           && !SaoLuuService.LaODay(new Exception("Insufficient system resources exist to complete the requested service."))
           && !SaoLuuService.LaODay(new Exception("There is insufficient system memory in resource pool 'internal' to run this query.")),
           "S15e nhận ổ đầy: SQL \"insufficient free space\" / \"not enough space\" / HRESULT 112 → có; \"insufficient system resources / memory\" → KHÔNG");
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None).GetAwaiter().GetResult();
        var c = e.Svc.Store.Doc("MES");
        var bo = SaoLuuService.MoiNhatDat(e.Svc.Quet(c, "MES", SaoLuuService.Loai.Bo), now)!;
        List<SaoLuuService.CanhBao> V(long? trong) { e.Svc.DoODia = _ => trong == null ? null : (trong.Value, 1L << 40); return e.Svc.TinhCanhBao(c, "MES", now.AddMinutes(1)); }
        var du = V(bo.Bytes * 3); var vang = V(bo.Bytes * 3 / 2); var doo = V(bo.Bytes / 2); var khong = V(null);
        Ok(!du.Any(x => x.NoiDung.Contains("còn trống")) && !khong.Any(x => x.NoiDung.Contains("còn trống")), "S15a ổ còn ≥ 2 lần cỡ bộ / không đo được → không báo");
        Ok(vang.Any(x => !x.Do && x.NoiDung.Contains("còn trống") && x.NoiDung.Contains("2 lần")), "S15b còn < 2 lần cỡ bộ ngày → vàng", string.Join("|", vang.Select(x => x.NoiDung)));
        Ok(doo.Any(x => x.Do && x.NoiDung.Contains("còn trống") && x.NoiDung.Contains("nút Xoá")), "S15c còn < cỡ bộ ngày → đỏ + chỉ nút Xoá", string.Join("|", doo.Select(x => x.NoiDung)));
        Ok(e.Svc.DungLuong(new SaoLuuCaiDat()) == null && SaoLuuService.DoODiaMacDinh(@"\\may\share\x") == null && SaoLuuService.DoODiaMacDinh(e.Goc) is { Tong: > 0 },
           "S15d đo ổ: chưa cài / thư mục mạng → không đo; thư mục máy → có số");
    }

    // ------------------------------------------------------------------ S16 PowerShell
    static string? TimSaoLuuMau()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "MES.Web", "SaoLuuMau");
                if (File.Exists(Path.Combine(p, "khoi-phuc.ps1"))) return p;
            }
        return null;
    }
    static void ChepThu(string src, string dst)
    {
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var d = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            File.Copy(f, d, true);
        }
    }
    static string? TimPs()
    {
        var ten = OperatingSystem.IsWindows() ? new[] { "powershell.exe" } : new[] { "pwsh" };
        foreach (var t in ten)
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var p = Path.Combine(dir.Trim(), t); if (File.Exists(p)) return p; } catch { }
            }
        if (OperatingSystem.IsWindows())
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
    static (int Ma, string Ra) ChayPs(string ps, string script, string? gia, params string[] ts)
    {
        var psi = new ProcessStartInfo(ps) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
                                             StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(ts)) psi.ArgumentList.Add(a);
        if (gia != null) psi.Environment["MES_SAOLUU_GIA"] = gia; else psi.Environment.Remove("MES_SAOLUU_GIA");
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var o = p.StandardOutput.ReadToEndAsync(); var er = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(180_000)) { try { p.Kill(true); } catch { } return (-99, "TREO > 180 giây"); }
        return (p.ExitCode, o.Result + er.Result);
    }
    // file thiếu (đột biến làm mất thư mục) → chuỗi rỗng, phép kiểm ra SAI thay vì làm vỡ cả kịch bản
    static string Doc(string f) => File.Exists(f) ? File.ReadAllText(f) : "";
    static string Lenh(string gia) { var f = Path.Combine(gia, "lenh.log"); return File.Exists(f) ? File.ReadAllText(f) : ""; }
    static string GiaDir(object gia)
    {
        var d = NewDir("gia");
        File.WriteAllText(Path.Combine(d, "gia.json"), JsonSerializer.Serialize(gia), new UTF8Encoding(false));
        return d;
    }

    static async Task S16()
    {
        Console.WriteLine("[S16] Script PowerShell khôi phục / cài dịch vụ / nâng cấp");
        var mau = TimSaoLuuMau(); var ps = TimPs();
        Ok(mau != null && ps != null, "S16 tiền đề: có MES.Web/SaoLuuMau + " + (OperatingSystem.IsWindows() ? "powershell.exe (Windows PowerShell 5.1)" : "pwsh"), $"{mau} | {ps}");
        if (mau == null || ps == null) return;
        // bộ thật (SQL giả của kịch bản) có khoi-phuc\
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var e = MkEnv(now: now);
        ChepThu(mau, Path.Combine(e.ContentRoot, "SaoLuuMau"));
        var caddy = Path.Combine(NewDir("caddy"), "Caddyfile"); File.WriteAllText(caddy, "x");
        var cf = e.Svc.Store.Doc("MES"); cf.FileCauHinhThem = new() { caddy }; e.Svc.Store.Ghi("MES", cf, "thu", "x", now);
        var kb = await e.Svc.ChayAsync(SaoLuuService.Loai.Bo, "x", CancellationToken.None);
        e.Svc.Clock = () => now.AddMinutes(30);
        var kn = await e.Svc.ChayAsync(SaoLuuService.Loai.Nhanh, "x", CancellationToken.None);
        var bo = Path.Combine(e.Goc, "MES", "bo", "2026-11-22_100000");
        var nh = Path.Combine(e.Goc, "MES", "nhanh", "2026-11-22_103000");
        var kp = Path.Combine(bo, "khoi-phuc", "khoi-phuc.ps1");
        Ok(kb.Dat && kn.Dat && File.Exists(kp), "S16 tiền đề: bộ ngày có khoi-phuc\\khoi-phuc.ps1 + bản nhanh", kb.ThongBao);

        // (a) -ChiKiem
        var k1 = ChayPs(ps, kp, null, "-ChiKiem");
        Ok(k1.Ma == 0 && k1.Ra.Contains("nguyên vẹn") && k1.Ra.Contains("đúng cỡ + đúng mã băm"), "S16a khoi-phuc -ChiKiem bộ nguyên vẹn → mã 0", k1.Ra);
        var hong = NewDir("hong"); ChepThu(bo, hong);
        var up = Path.Combine(hong, "uploads", "uploads.zip"); var bytes = File.ReadAllBytes(up); bytes[bytes.Length / 2] ^= 0x55; File.WriteAllBytes(up, bytes);
        var k2 = ChayPs(ps, Path.Combine(hong, "khoi-phuc", "khoi-phuc.ps1"), null, "-ChiKiem");
        var thieu = NewDir("thieu"); ChepThu(bo, thieu); File.Delete(Path.Combine(thieu, "cau-hinh", "appsettings.json"));
        var k3 = ChayPs(ps, Path.Combine(thieu, "khoi-phuc", "khoi-phuc.ps1"), null, "-ChiKiem");
        Ok(k2.Ma == 1 && k2.Ra.Contains("uploads/uploads.zip") && k2.Ra.Contains("mã băm") && k3.Ma == 1 && k3.Ra.Contains("thiếu file cau-hinh/appsettings.json"),
           "S16b sửa 1 byte / thiếu 1 file → mã 1, chỉ đúng file hỏng", k2.Ra + "\n" + k3.Ra);

        // chuẩn bị "máy": thư mục app đang chạy (dịch vụ MES), thư mục sao lưu mặc định SQL
        string MkApp()
        {
            var app = NewDir("app-may");
            File.WriteAllText(Path.Combine(app, "MES.Web.exe"), "exe-cu"); File.WriteAllText(Path.Combine(app, "MES.Web.dll"), "dll-cu");
            File.WriteAllText(Path.Combine(app, "appsettings.json"), "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=.;Database=MES\"},\"Kestrel\":{\"Endpoints\":{\"Http\":{\"Url\":\"http://0.0.0.0:5077\"}}}}");
            Directory.CreateDirectory(Path.Combine(app, "wwwroot", "uploads")); File.WriteAllText(Path.Combine(app, "wwwroot", "uploads", "cu.png"), "anh-cu");
            Directory.CreateDirectory(Path.Combine(app, "logs")); File.WriteAllText(Path.Combine(app, "logs", "mes-1.log"), "log-cu");
            Directory.CreateDirectory(Path.Combine(app, "wwwroot", "css"));
            return app;
        }
        object Gia(string app, string bak, string tenMay, string verSql = "16.0.4135.4 · Express", object? trangLen = null, string startName = "LocalSystem") => new Dictionary<string, object?>
        {
            ["tenMay"] = tenMay,
            ["dichVu"] = new Dictionary<string, object> { ["MES"] = new { State = "Running", PathName = "\"" + Path.Combine(app, "MES.Web.exe") + "\"", StartName = startName }, ["MSSQLSERVER"] = new { State = "Running", PathName = "sqlservr.exe" } },
            ["sql_phienBan"] = verSql, ["sql_coDb"] = 1, ["sql_thuMucBackup"] = bak, ["sql_thuMucData"] = "D:\\SQLData", ["sql_thuMucLog"] = "D:\\SQLLog",
            ["sql_fileCu"] = new[] { new { type_desc = "ROWS", physical_name = "E:\\data\\MES.mdf" }, new { type_desc = "LOG", physical_name = "E:\\log\\MES_log.ldf" } },
            ["sql_fileList"] = new[] { new { LogicalName = "MES", Type = "D" }, new { LogicalName = "MES_log", Type = "L" } },
            ["sql_nguoiMoCoi"] = new[] { new { name = "mes_app" } }, ["sql_coLogin"] = 1,
            ["sql_soDong"] = new[] { new { bang = "dbo.A", so = 5 }, new { bang = "dbo.B", so = 7 } },
            ["trangLen"] = trangLen ?? true, ["exeMa"] = 0, ["exeRa"] = "SAO-LUU-TRUOC-NANG-CAP: DAT — Đã tạo bộ trước nâng cấp (giả)",
        };

        // (c) -XemTruoc: không ghi gì
        {
            var app = MkApp(); var bak = NewDir("sqlbak");
            var gia = GiaDir(Gia(app, bak, Environment.MachineName));
            var r = ChayPs(ps, kp, gia, "-XemTruoc");
            var l = Lenh(gia);
            Ok(r.Ma == 0 && r.Ra.Contains("Các bước sẽ làm") && r.Ra.Contains("RESTORE DATABASE MES") && r.Ra.Contains("KHÔNG đụng gì") && r.Ra.Contains("Caddyfile")
               && !l.Contains("BACKUP DATABASE") && !l.Contains("RESTORE DATABASE") && !l.Contains("ALTER DATABASE") && !l.Contains("DICH-VU-DUNG")
               && File.Exists(Path.Combine(app, "MES.Web.exe")) && Directory.GetFiles(bak).Length == 0,
               "S16c -XemTruoc: in kế hoạch (cả file cấu hình thêm), chỉ lệnh đọc SQL, không dừng dịch vụ / không đụng file", r.Ra + "\n---\n" + l);
        }
        // (d) chạy thật, cùng máy
        {
            var app = MkApp(); var bak = NewDir("sqlbak");
            var gia = GiaDir(Gia(app, bak, Environment.MachineName));
            var r = ChayPs(ps, kp, gia, "-KhongHoi");
            var l = Lenh(gia);
            int iDung = l.IndexOf("DICH-VU-DUNG: MES"), iBk = l.IndexOf("BACKUP DATABASE @db"), iOff = l.IndexOf("SET OFFLINE WITH ROLLBACK IMMEDIATE"), iRes = l.IndexOf("RESTORE DATABASE @db"),
                iUser = l.IndexOf("ALTER USER"), iBat = l.IndexOf("DICH-VU-BAT: MES");
            // rà độc lập 23/11 (Trung 5): OFFLINE thay SINGLE_USER (kết nối khác không chen vào giữa được); RESTORE … RECOVERY tự ONLINE
            Ok(r.Ma == 0 && iDung >= 0 && iDung < iBk && iBk < iOff && iOff < iRes && iRes < iUser && iUser < iBat && !l.Contains("SINGLE_USER") && !l.Contains("icacls"),
               "S16d chạy thật: dừng dịch vụ → sao lưu DB đang có → OFFLINE → RESTORE → nối người dùng SQL → bật dịch vụ (đúng thứ tự); dịch vụ LocalSystem không cấp quyền thư mục", r.Ra + "\n---\n" + l);
            var resLine = l.Split('\n').FirstOrDefault(x => x.Contains("RESTORE DATABASE @db")) ?? "";
            Ok(resLine.Contains("MOVE @l1 TO @f1") && resLine.Contains("@f1=E:\\data\\MES.mdf") && resLine.Contains("@f2=E:\\log\\MES_log.ldf") && resLine.Contains("REPLACE, CHECKSUM")
               && l.Contains("COPY_ONLY") && l.Contains("MES_truoc-khoi-phuc_"),
               "S16e RESTORE đè đúng chỗ file DB hiện tại (MOVE theo sys.master_files), REPLACE + CHECKSUM; bản an toàn COPY_ONLY", resLine);
            var cu = Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".truoc-khoi-phuc-*").FirstOrDefault();
            bool phanMem = File.Exists(Path.Combine(app, "MES.Web.dll")) && Doc(Path.Combine(app, "MES.Web.dll")) == "dll" && File.Exists(Path.Combine(app, "Other.dll"));
            bool cauHinh = Doc(Path.Combine(app, "appsettings.json")) == "{\"ConnectionStrings\":{}}" && File.Exists(Path.Combine(app, "appsettings.Production.json"));
            bool anh = File.Exists(Path.Combine(app, "wwwroot", "uploads", "avatars", "a.png"));
            Ok(cu != null && Doc(Path.Combine(cu, "MES.Web.exe")) == "exe-cu" && phanMem && cauHinh && anh && !File.Exists(Path.Combine(app, "logs", "mes-1.log")),
               "S16f phần mềm = bản trong bộ, cấu hình = cau-hinh\\, ảnh = uploads.zip; thư mục app cũ đổi tên giữ lại", $"cũ {cu} pm {phanMem} ch {cauHinh} anh {anh}");
            var pd = Path.Combine(gia, "ProgramData", "MES_Server");
            Ok(File.Exists(Path.Combine(pd, "License", "mes_cfg.dat")) && File.Exists(Path.Combine(pd, "SaoLuu", "cai-dat-MES.json")) && Directory.GetFiles(bak, "*_khoi-phuc_*.bak").Length == 0
               && r.Ra.Contains("số dòng 2 bảng khớp manifest") && Directory.GetFiles(Path.Combine(pd, "khoi-phuc-log")).Length == 1,
               "S16g cùng máy: bản quyền + cài đặt sao lưu (máy chưa có) chép lại; file .bak tạm đã xoá; số dòng khớp; có nhật ký", r.Ra);
        }
        // (e) máy mới: không chép bản quyền; cài đặt sao lưu đã có thì giữ
        {
            var app = MkApp(); var bak = NewDir("sqlbak");
            var gia = GiaDir(Gia(app, bak, "MAY-MOI-123"));
            var sl = Path.Combine(gia, "ProgramData", "MES_Server", "SaoLuu"); Directory.CreateDirectory(sl); File.WriteAllText(Path.Combine(sl, "cai-dat-MES.json"), "dang-co");
            var r = ChayPs(ps, kp, gia, "-KhongHoi");
            Ok(r.Ma == 0 && !Directory.Exists(Path.Combine(gia, "ProgramData", "MES_Server", "License")) && r.Ra.Contains("máy KHÁC") && r.Ra.Contains("/about")
               && Doc(Path.Combine(sl, "cai-dat-MES.json")) == "dang-co",
               "S16h máy mới: KHÔNG chép bản quyền (nhắc kích hoạt lại /about); cài đặt sao lưu đang có giữ nguyên", r.Ra);
        }
        // (f) bản nhanh -ChiDuLieu: chỉ DB
        {
            var app = MkApp(); var bak = NewDir("sqlbak");
            var gia = GiaDir(Gia(app, bak, Environment.MachineName));
            var r = ChayPs(ps, kp, gia, "-Bo", nh, "-KhongHoi");
            var l = Lenh(gia);
            Ok(r.Ma == 0 && l.Contains("RESTORE DATABASE @db") && r.Ra.Contains("CHỈ DỮ LIỆU") && Doc(Path.Combine(app, "MES.Web.exe")) == "exe-cu"
               && !Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".truoc-khoi-phuc-*").Any(),
               "S16i bản nhanh (-Bo nhanh\\…): chỉ khôi phục DB, phần mềm đang chạy giữ nguyên", r.Ra);
        }
        // (g) SQL máy này cũ hơn bản lưu → dừng trước khi đụng gì
        {
            var app = MkApp(); var bak = NewDir("sqlbak");
            var gia = GiaDir(Gia(app, bak, Environment.MachineName, verSql: "13.0.5026.0 · Express"));
            var r = ChayPs(ps, kp, gia, "-KhongHoi");
            Ok(r.Ma == 4 && r.Ra.Contains("không khôi phục được bản mới") && !Lenh(gia).Contains("DICH-VU-DUNG"), "S16j SQL máy này (13) cũ hơn bản lưu (16) → mã 4, chưa dừng gì", r.Ra);
        }
        // [G32B-KHOIPHUC] rà độc lập 23/11 — DB hỏng / RESTORE lỗi giữa chừng / dựng phần mềm lỗi / tài khoản ảo
        Dictionary<string, object?> GiaD(string app, string bak, string startName = "LocalSystem") => (Dictionary<string, object?>)Gia(app, bak, Environment.MachineName, startName: startName);
        int Dem(string l, string x) { int n = 0, i = 0; while ((i = l.IndexOf(x, i, StringComparison.Ordinal)) >= 0) { n++; i += x.Length; } return n; }
        {   // (Cao 1) DB SUSPECT: không BACKUP, không OFFLINE, RESTORE đè thẳng
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["sql_trangThaiDb"] = "SUSPECT"; var gia = GiaDir(d);
            var x = ChayPs(ps, kp, gia, "-XemTruoc");
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            Ok(x.Ma == 0 && x.Ra.Contains("trạng thái SUSPECT") && x.Ra.Contains("KHÔNG sao lưu được DB MES") && r.Ma == 0 && !l.Contains("BACKUP DATABASE") && !l.Contains("SET OFFLINE")
               && l.Contains("RESTORE DATABASE @db FROM DISK = @p WITH MOVE") && r.Ra.Contains("Bỏ sao lưu DB đang có") && l.Contains("DICH-VU-BAT: MES"),
               "S16q DB đang SUSPECT (hỏng): xem trước báo rõ; chạy thật bỏ sao lưu + OFFLINE, RESTORE đè thẳng → mã 0 (trước: BACKUP lỗi → mã 4, không khôi phục được)", r.Ra + "\n---\n" + l);
        }
        {   // (Cao 2) RESTORE lỗi giữa chừng, DB ở RESTORING → tự trả lại DB cũ từ bản bước 6, bật dịch vụ
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["sqlLoi"] = new[] { "WITH MOVE" }; d["sql_trangThaiSauLoi"] = "RESTORING"; var gia = GiaDir(d);
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            var back = l.Split('\n').FirstOrDefault(z => z.Contains("RESTORE DATABASE @db FROM DISK = @p WITH REPLACE, CHECKSUM, RECOVERY")) ?? "";
            Ok(r.Ma == 4 && back.Contains("_truoc-khoi-phuc_") && l.LastIndexOf("DICH-VU-BAT: MES") > l.IndexOf(back) && r.Ra.Contains("đã trả lại DB MES") && r.Ra.Contains("đã BẬT lại dịch vụ")
               && !l.Contains("ALTER USER") && Doc(Path.Combine(app, "MES.Web.exe")) == "exe-cu" && !r.Ra.Contains("bật lại dịch vụ để dùng DB cũ"),
               "S16r RESTORE lỗi khi đã đè dở (RESTORING) → TỰ trả lại DB cũ từ bản trước-khôi-phục rồi bật dịch vụ, không đụng phần mềm, mã 4", r.Ra + "\n---\n" + l);
        }
        {   // RESTORE lỗi trước khi ghi (DB còn OFFLINE) → SET ONLINE, không cần trả lại
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["sqlLoi"] = new[] { "WITH MOVE" }; d["sql_trangThaiSauLoi"] = "OFFLINE"; d["sql_trangThaiSauOnline"] = "ONLINE"; var gia = GiaDir(d);
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 4 && l.Contains("SET ONLINE") && Dem(l, "RESTORE DATABASE @db") == 1 && r.Ra.Contains("DB MES cũ còn nguyên") && l.LastIndexOf("DICH-VU-BAT: MES") > l.IndexOf("SET ONLINE"),
               "S16s RESTORE lỗi trước khi ghi (DB còn OFFLINE) → đưa ONLINE lại, không RESTORE lại, bật dịch vụ, mã 4", r.Ra + "\n---\n" + l);
        }
        {   // trả lại cũng lỗi → KHÔNG bật dịch vụ, in lệnh tay
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["sqlLoi"] = new[] { "WITH MOVE", "WITH REPLACE, CHECKSUM, RECOVERY" }; d["sql_trangThaiSauLoi"] = "RESTORING"; var gia = GiaDir(d);
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 4 && !l.Contains("DICH-VU-BAT") && r.Ra.Contains("KHÔNG dùng được") && r.Ra.Contains("RESTORE DATABASE [MES] FROM DISK = N'") && r.Ra.Contains("giữ DỪNG"),
               "S16t RESTORE lỗi + trả lại DB cũ cũng lỗi → giữ dịch vụ DỪNG, in đúng lệnh trả tay, mã 4", r.Ra + "\n---\n" + l);
        }
        {   // BACKUP DB đang có lỗi (chưa đụng gì) → bật lại dịch vụ
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["sqlLoi"] = new[] { "BACKUP DATABASE" }; var gia = GiaDir(d);
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 4 && !l.Contains("RESTORE DATABASE @db") && !l.Contains("SET OFFLINE") && l.LastIndexOf("DICH-VU-BAT: MES") > l.IndexOf("BACKUP DATABASE") && r.Ra.Contains("CHƯA khôi phục gì"),
               "S16u sao lưu DB đang có lỗi → chưa đè gì, bật lại dịch vụ với DB cũ, mã 4", r.Ra + "\n---\n" + l);
        }
        {   // (Trung 6) dựng lại phần mềm lỗi giữa chừng → trả thư mục app cũ, bật dịch vụ, mã 6
            var app = MkApp(); var d = GiaD(app, NewDir("sqlbak")); d["giaiNenLoi"] = new[] { "MES.Web.zip" }; var gia = GiaDir(d);
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            var loi = Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".loi-*").FirstOrDefault();
            Ok(r.Ma == 6 && Doc(Path.Combine(app, "MES.Web.exe")) == "exe-cu" && loi != null && !Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".truoc-khoi-phuc-*").Any()
               && l.LastIndexOf("DICH-VU-BAT: MES") > l.IndexOf("RESTORE DATABASE @db") && r.Ra.Contains("ĐÃ là bản khôi phục"),
               "S16v giải nén phần mềm lỗi giữa chừng → thư mục dở đổi tên .loi-*, trả thư mục app cũ, bật dịch vụ, báo DB đã khôi phục, mã 6", r.Ra + "\n---\n" + l);
        }
        {   // (Trung 7) dịch vụ chạy tài khoản ảo → cấp lại quyền ghi thư mục app mới
            var app = MkApp(); var gia = GiaDir(GiaD(app, NewDir("sqlbak"), "NT SERVICE\\MES"));
            var r = ChayPs(ps, kp, gia, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 0 && l.Contains("icacls.exe " + app + " /grant NT SERVICE\\MES:(OI)(CI)M"),
               "S16w khôi phục trọn bộ, dịch vụ chạy NT SERVICE\\MES → cấp lại quyền Sửa thư mục app mới cho tài khoản đó", r.Ra + "\n---\n" + l);
        }

        // (h) cai-dich-vu
        var cdv = Path.Combine(mau, "cai-dich-vu.ps1");
        {
            var app = MkApp(); var gia0 = Gia(app, NewDir("x"), Environment.MachineName);
            var d = (Dictionary<string, object?>)gia0; d["dichVu"] = new Dictionary<string, object> { ["MSSQLSERVER"] = new { State = "Running", PathName = "x" } };
            var gia = GiaDir(d);
            var sl = Path.Combine(gia, "ProgramData", "MES_Server", "SaoLuu"); Directory.CreateDirectory(sl);
            File.WriteAllText(Path.Combine(sl, "cai-dat-MES.json"), "{\"ThuMucTheoMaySql\":\"D:\\\\MES-Backup\"}");
            var r = ChayPs(ps, cdv, gia, "-ThuMucApp", app);
            var l = Lenh(gia);
            Ok(r.Ma == 0 && l.Contains("NEW-SERVICE: MES") && l.Contains("phu-thuoc=MSSQLSERVER") && l.Contains("start= delayed-auto") && l.Contains("restart/60000")
               && l.Contains("ASPNETCORE_ENVIRONMENT=Production") && l.Contains("icacls.exe D:\\MES-Backup /grant NT SERVICE\\MSSQLSERVER:(OI)(CI)M") && l.Contains("FIREWALL: MES Nhat Phat (TCP 5077)")
               && l.Contains("DICH-VU-BAT: MES") && l.Contains("localhost:5077/login"),
               "S16k cai-dich-vu: tạo dịch vụ chờ SQL, tự chạy lại, Production, quyền ghi thư mục sao lưu cho SQL, tường lửa cổng app, bật + chờ /login", r.Ra + "\n---\n" + l);
            var g2 = GiaDir(Gia(app, NewDir("x"), Environment.MachineName));
            var r2 = ChayPs(ps, cdv, g2, "-ThuMucApp", app);
            var r3 = ChayPs(ps, cdv, g2, "-ThuMucApp", app, "-ChiCapQuyen", "-ThuMucSaoLuu", "F:\\BK");
            var noApp = NewDir("trong");
            var r4 = ChayPs(ps, cdv, gia, "-ThuMucApp", noApp);
            Ok(r2.Ma == 2 && r2.Ra.Contains("đã có dịch vụ") && !Lenh(g2).Contains("NEW-SERVICE") && r3.Ma == 0 && Lenh(g2).Contains("icacls.exe F:\\BK /grant NT SERVICE\\MSSQLSERVER:(OI)(CI)M")
               && r4.Ma == 2 && r4.Ra.Contains("bản publish"),
               "S16l đã có dịch vụ → mã 2 không tạo; -ChiCapQuyen chỉ cấp quyền; thư mục không phải bản publish → mã 2", r2.Ra + "\n" + r3.Ra + "\n" + r4.Ra);
        }

        // (i) nang-cap
        var ncp = Path.Combine(mau, "nang-cap.ps1");
        string MkBanMoi()
        {
            var b = NewDir("ban-moi");
            File.WriteAllText(Path.Combine(b, "MES.Web.exe"), "exe-moi"); File.WriteAllText(Path.Combine(b, "MES.Web.dll"), "dll-moi");
            File.WriteAllText(Path.Combine(b, "appsettings.json"), "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=192.168.1.211;Database=MES\"}}");
            Directory.CreateDirectory(Path.Combine(b, "wwwroot", "css")); File.WriteAllText(Path.Combine(b, "wwwroot", "css", "moi.css"), "css-moi");
            return b;
        }
        {
            var app = MkApp(); var moi = MkBanMoi();
            var gia = GiaDir(Gia(app, NewDir("x"), Environment.MachineName));
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi");
            var l = Lenh(gia);
            var cu = Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".cu-*").FirstOrDefault();
            Ok(r.Ma == 0 && l.IndexOf("DICH-VU-DUNG") < l.IndexOf("--sao-luu-truoc-nang-cap") && l.IndexOf("--sao-luu-truoc-nang-cap") < l.IndexOf("DICH-VU-BAT")
               && l.Contains("--contentRoot \"" + app + "\"") && Doc(Path.Combine(app, "MES.Web.dll")) == "dll-moi" && File.Exists(Path.Combine(app, "wwwroot", "css", "moi.css"))
               && Doc(Path.Combine(app, "appsettings.json")).Contains("Server=.;") && File.Exists(Path.Combine(app, "wwwroot", "uploads", "cu.png")) && File.Exists(Path.Combine(app, "logs", "mes-1.log"))
               && cu != null && Doc(Path.Combine(cu, "MES.Web.dll")) == "dll-cu"
               && l.IndexOf("sc.exe failure MES reset= 86400 actions= \"\"") >= 0 && l.IndexOf("sc.exe failure MES reset= 86400 actions= \"\"") < l.IndexOf("DICH-VU-DUNG")
               && l.LastIndexOf("actions= restart/60000") > l.LastIndexOf("DICH-VU-BAT") && !l.Contains("icacls"),
               "S16m nâng cấp: dừng → bản MỚI sao lưu trọn bộ bản cũ (--contentRoot thư mục cũ) → thay phần mềm, GIỮ appsettings + ảnh + logs của máy → bật; bản cũ giữ .cu-*", r.Ra + "\n---\n" + l);
        }
        {
            var app = MkApp(); var moi = MkBanMoi();
            var d = (Dictionary<string, object?>)Gia(app, NewDir("x"), Environment.MachineName); d["exeMa"] = 2; d["exeRa"] = "SAO-LUU-TRUOC-NANG-CAP: LOI — Chưa cài thư mục sao lưu";
            var gia = GiaDir(d);
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi");
            Ok(r.Ma == 4 && Doc(Path.Combine(app, "MES.Web.dll")) == "dll-cu" && !Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".cu-*").Any()
               && Lenh(gia).Contains("DICH-VU-BAT"), "S16n sao lưu trước nâng cấp lỗi → mã 4, CHƯA thay gì, bật lại bản cũ", r.Ra);
        }
        {
            var app = MkApp(); var moi = MkBanMoi();
            var gia = GiaDir(Gia(app, NewDir("x"), Environment.MachineName, trangLen: new[] { false, true }));
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi");
            var loi = Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".loi-*").FirstOrDefault();
            Ok(r.Ma == 5 && Doc(Path.Combine(app, "MES.Web.dll")) == "dll-cu" && loi != null && Doc(Path.Combine(loi, "MES.Web.dll")) == "dll-moi"
               && r.Ra.Contains("đã trả bản cũ"), "S16o bản mới không lên → TỰ TRẢ bản cũ (bản mới đổi tên .loi-*), mã 5", r.Ra);
            var r2 = ChayPs(ps, ncp, GiaDir(Gia(app, NewDir("x"), Environment.MachineName)), app, "-KhongHoi");
            Ok(r2.Ma == 2 && r2.Ra.Contains("thư mục riêng"), "S16p bản mới = thư mục app đang chạy → mã 2, không đụng", r2.Ra);
        }
        // [G32B-TRUOC-NANG-CAP] rà độc lập 23/11 — đổi tên lỗi / -ChoChaySql / tài khoản ảo
        {   // (Cao 3) không đổi tên được thư mục app → bật lại bản cũ, mã 7
            var app = MkApp(); var moi = MkBanMoi(); var d = GiaD(app, NewDir("x")); d["doiTenLoi"] = new[] { ".cu-" }; var gia = GiaDir(d);
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 7 && Doc(Path.Combine(app, "MES.Web.dll")) == "dll-cu" && !Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".cu-*").Any()
               && l.LastIndexOf("DICH-VU-BAT: MES") > l.LastIndexOf("DICH-VU-DUNG") && r.Ra.Contains("CHƯA thay gì") && l.LastIndexOf("actions= restart/60000") > l.LastIndexOf("DICH-VU-BAT"),
               "S16x nâng cấp: không đổi tên được thư mục app (cửa sổ đang mở) → bật lại bản cũ, đặt lại tự chạy lại, mã 7 (trước: mã 9, dịch vụ DỪNG)", r.Ra + "\n---\n" + l);
        }
        {   // trả bản cũ: đổi tên bản mới lỗi → mã 6, báo DỪNG, không bật
            var app = MkApp(); var moi = MkBanMoi(); var d = GiaD(app, NewDir("x")); d["trangLen"] = new[] { false }; d["doiTenLoi"] = new[] { ".loi-" }; var gia = GiaDir(d);
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 6 && r.Ra.Contains("đang DỪNG") && r.Ra.Contains("Tự làm") && l.LastIndexOf("DICH-VU-DUNG") > l.LastIndexOf("DICH-VU-BAT"),
               "S16y bản mới không lên + không đổi tên được thư mục bản mới → mã 6, nói rõ dịch vụ đang DỪNG + cách làm tay (trước: lỗi văng ra mã 9)", r.Ra + "\n---\n" + l);
        }
        {   // (Trung 4) -ChoChaySql + bản mới không lên → KHÔNG tự trả bản cũ, mã 8
            var app = MkApp(); var moi = MkBanMoi(); var d = GiaD(app, NewDir("x")); d["trangLen"] = new[] { false }; var gia = GiaDir(d);
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi", "-ChoChaySql");
            Ok(r.Ma == 8 && Doc(Path.Combine(app, "MES.Web.dll")) == "dll-moi" && !Directory.GetDirectories(Path.GetDirectoryName(app)!, Path.GetFileName(app) + ".loi-*").Any()
               && r.Ra.Contains("KHÔNG tự trả bản cũ") && r.Ra.Contains("truoc-nang-cap"),
               "S16z đã chạy SQL nâng cấp (-ChoChaySql) + bản mới không lên → KHÔNG tự trả bản cũ (bản cũ không hợp DB mới), chỉ đường khôi phục bộ trước nâng cấp, mã 8", r.Ra);
        }
        {   // (Trung 7) tài khoản ảo → cấp lại quyền thư mục app mới
            var app = MkApp(); var moi = MkBanMoi(); var gia = GiaDir(GiaD(app, NewDir("x"), "NT SERVICE\\MES"));
            var r = ChayPs(ps, ncp, gia, moi, "-KhongHoi"); var l = Lenh(gia);
            Ok(r.Ma == 0 && l.Contains("icacls.exe " + app + " /grant NT SERVICE\\MES:(OI)(CI)M") && l.IndexOf("icacls") < l.LastIndexOf("DICH-VU-BAT"),
               "S16za nâng cấp, dịch vụ chạy NT SERVICE\\MES → cấp lại quyền Sửa thư mục app mới trước khi bật", r.Ra + "\n---\n" + l);
        }
        {   // (Thấp 14) biến MES_SAOLUU_GIA lỡ đặt trên máy thật (thư mục không có gia.json) → dừng mã 2, không báo ĐẠT
            var r = ChayPs(ps, kp, NewDir("khong-gia"), "-ChiKiem");
            Ok(r.Ma == 2 && r.Ra.Contains("MES_SAOLUU_GIA") && r.Ra.Contains("không có gia.json"), "S16zb MES_SAOLUU_GIA trỏ thư mục không có gia.json → dừng mã 2 (trước: chạy giả, báo ĐẠT)", r.Ra);
        }
    }
}
