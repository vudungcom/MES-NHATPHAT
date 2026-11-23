<#
=====================================================================
 [G32B-TRUOC-NANG-CAP 2026-11-23] Gói 32b — NÂNG CẤP phần mềm MES (đã cài dạng dịch vụ) lên bản mới, TỰ SAO LƯU TRƯỚC khi thay.
   Chạy quyền Administrator trên máy chủ: chép thư mục publish bản mới vào vd D:\ban-moi, rồi chuột phải D:\ban-moi\SaoLuuMau\nang-cap.bat →
   Run as administrator (không ghi thư mục thì bản mới = thư mục cha của SaoLuuMau). Hoặc: nang-cap.bat "D:\ban-moi"
   Các bước: kiểm bản mới → dừng dịch vụ → bản MỚI chạy "MES.Web.exe --contentRoot <thư mục app đang chạy> --sao-luu-truoc-nang-cap"
             = 1 bộ trọn bộ của bản ĐANG CHẠY (dữ liệu + phần mềm + cấu hình) vào <sao lưu>\<DB>\truoc-nang-cap (lỗi → dừng, bật lại bản cũ)
             → đổi tên thư mục app thành <app>.cu-<giờ> → chép bản mới vào → chép lại từ bản cũ: appsettings*.json (chuỗi kết nối máy này),
             wwwroot\uploads (ảnh), logs → (-ChoChaySql: dừng chờ chạy SQL nâng cấp) → bật dịch vụ, chờ /login
             → không lên trong 120 giây: TỰ TRẢ bản cũ (thư mục bản mới đổi tên <app>.loi-<giờ>) rồi bật lại.
   DB: nâng cấp KHÔNG tự chạy SQL. Bản mới cần SQL (tools\sql\NN_*.sql) → thêm -ChoChaySql, chạy SQL bằng SSMS lúc script dừng chờ.
   -BoQuaSaoLuu: bỏ bước sao lưu (KHÔNG khuyên — chỉ khi chưa cài thư mục sao lưu và đã tự lưu cách khác). Phải gõ CO.
   -ChoChaySql + bản mới không lên: KHÔNG tự trả bản cũ (DB đã đổi theo bản mới, bản cũ không chạy được) → hướng dẫn khôi phục bộ trước nâng cấp.
   Trong lúc thay thư mục: tạm tắt "lỗi tự chạy lại" của dịch vụ (Windows không tự bật giữa chừng), xong đặt lại.
   Mã thoát: 0 đạt · 2 sai điều kiện / không gõ CO · 3 không phải Administrator · 4 sao lưu trước nâng cấp lỗi (chưa thay gì, bản cũ đã bật lại) ·
             5 bản mới không lên (đã trả bản cũ) · 6 trả bản cũ cũng lỗi (xem dòng [LOI] cuối) · 7 không đổi tên được thư mục app (chưa thay gì,
             bản cũ đã bật lại) · 8 đã chạy SQL nâng cấp + bản mới không lên (KHÔNG tự trả — khôi phục bộ trước nâng cấp) · 9 lỗi khác
             (dòng cuối ghi dịch vụ đang BẬT hay DỪNG).
   Nguồn: HÙNG 23/11 13:20 OK Gói 32b (câu B2: bộ trước nâng cấp thư mục riêng, giữ 10 bộ).
=====================================================================
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$BanMoi = '',
    [string]$TenDichVu = 'MES',
    [string]$ThuMucApp = '',
    [switch]$BoQuaSaoLuu,
    [switch]$ChoChaySql,
    [switch]$KhongHoi
)
. (Join-Path $PSScriptRoot 'chung.ps1')

function PhienBanExe([string]$thuMuc) {
    $f = Join-Path $thuMuc 'MES.Web.dll'
    if (-not (Test-Path -LiteralPath $f)) { return '?' }
    try { $v = (Get-Item -LiteralPath $f).VersionInfo.ProductVersion; if ([string]::IsNullOrWhiteSpace($v)) { return '?' } ; return $v } catch { return '?' }
}

$gio = GioTen
$logDir = Join-Path (Join-Path (ThuMucProgramData) 'MES_Server') 'nang-cap-log'
try { New-Item -ItemType Directory -Path $logDir -Force | Out-Null; Start-Transcript -Path (Join-Path $logDir "nang-cap_$gio.txt") | Out-Null } catch { }
$script:DaTatTuChay = $false; $script:DaDung = $false
function Thoat([int]$ma) {
    if ($script:DaTatTuChay) { try { DatTuChayLai $TenDichVu } catch { GhiCanh "đặt lại 'lỗi tự chạy lại' cho dịch vụ lỗi: $($_.Exception.Message) — chạy cai-dich-vu.bat không cần (chỉ thiếu tự chạy lại)." } }
    try { Stop-Transcript | Out-Null } catch { } ; exit $ma
}
# [G32B-TRUOC-NANG-CAP] rà độc lập 23/11 (Cao 3): trả bản cũ — mỗi lần đổi tên có thể lỗi (Explorer / Notepad++ mở logs, cmd đứng trong thư mục,
#   diệt virus giữ file). Trước: lỗi văng ra ngoài → mã 9, dịch vụ DỪNG không ai bật. Nay: trả lời $true / $false, luôn in rõ dịch vụ đang ra sao.
function TraBanCu([string]$cu, [string]$loi) {
    try {
        if (Test-Path -LiteralPath $ThuMucApp) { DoiTenThuMuc $ThuMucApp $loi; Ghi "  bản mới → $loi" }
    } catch {
        GhiLoi "không đổi tên được thư mục bản mới: $($_.Exception.Message)"
        GhiLoi "Dịch vụ $TenDichVu đang DỪNG. Tự làm: đóng cửa sổ đang mở $ThuMucApp, đổi tên $ThuMucApp → $loi, đổi tên $cu → $ThuMucApp, bật dịch vụ (services.msc)."
        return $false
    }
    try { DoiTenThuMuc $cu $ThuMucApp; GhiOk "bản cũ → $ThuMucApp" }
    catch {
        GhiLoi "không đổi tên được bản cũ: $($_.Exception.Message)"
        GhiLoi "Dịch vụ $TenDichVu đang DỪNG, thư mục $ThuMucApp ĐANG TRỐNG. Tự làm: đổi tên $cu → $ThuMucApp rồi bật dịch vụ (services.msc)."
        return $false
    }
    try { BatDichVu $TenDichVu } catch { GhiLoi "bật dịch vụ lỗi: $($_.Exception.Message) — bật tay trong services.msc."; return $false }
    return $true
}

try {
    if (-not (LaAdmin)) { GhiLoi 'Phải chạy bằng quyền Administrator (chuột phải nang-cap.bat → Run as administrator).'; Thoat 3 }
    GhiBuoc '1. Kiểm'
    $dv = DichVu $TenDichVu
    if ($null -eq $dv) { GhiLoi "chưa có dịch vụ $TenDichVu — lần đầu cài dùng cai-dich-vu.bat."; Thoat 2 }
    if ([string]::IsNullOrWhiteSpace($ThuMucApp)) { $ThuMucApp = ThuMucCuaDichVu $dv }
    $ThuMucApp = $ThuMucApp.TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($BanMoi)) { $BanMoi = Split-Path -Parent $PSScriptRoot }
    $BanMoi = (Resolve-Path -LiteralPath $BanMoi).Path.TrimEnd('\', '/')
    $exeMoi = Join-Path $BanMoi 'MES.Web.exe'
    if (-not (Test-Path -LiteralPath $exeMoi) -or -not (Test-Path -LiteralPath (Join-Path $BanMoi 'wwwroot'))) { GhiLoi "$BanMoi không phải bản publish (thiếu MES.Web.exe / wwwroot)."; Thoat 2 }
    if (-not (Test-Path -LiteralPath (Join-Path $ThuMucApp 'MES.Web.exe'))) { GhiLoi "thư mục app của dịch vụ $ThuMucApp không có MES.Web.exe."; Thoat 2 }
    $sep = [IO.Path]::DirectorySeparatorChar
    if ($BanMoi -ieq $ThuMucApp -or $BanMoi.StartsWith($ThuMucApp + $sep, [StringComparison]::OrdinalIgnoreCase) -or $ThuMucApp.StartsWith($BanMoi + $sep, [StringComparison]::OrdinalIgnoreCase)) {
        GhiLoi "bản mới ($BanMoi) phải ở thư mục riêng, không nằm trong / chứa thư mục app ($ThuMucApp)."; Thoat 2
    }
    if ($PSScriptRoot.StartsWith($ThuMucApp + $sep, [StringComparison]::OrdinalIgnoreCase)) {
        GhiLoi "đang chạy script trong thư mục app ($ThuMucApp) — thư mục này sẽ bị đổi tên. Chạy nang-cap.bat trong thư mục BẢN MỚI: $BanMoi\SaoLuuMau\nang-cap.bat"; Thoat 2
    }
    $app = DocAppsettings $ThuMucApp
    $cong = CongTuAppsettings $app
    Ghi "  Dịch vụ $TenDichVu ($([string]$dv.State)) · thư mục app $ThuMucApp · cổng $cong"
    Ghi "  Bản đang chạy: $(PhienBanExe $ThuMucApp)"
    Ghi "  Bản mới:       $(PhienBanExe $BanMoi)  ($BanMoi)"
    if (-not $KhongHoi) {
        $tl = Read-Host 'Gõ CO (chữ hoa) để nâng cấp — phần mềm sẽ tạm dừng vài phút'
        if ($tl -cne 'CO') { GhiCanh 'Không gõ CO — dừng, không đụng gì.'; Thoat 2 }
    }

    GhiBuoc "2. Dừng dịch vụ $TenDichVu"
    try { TatTuChayLai $TenDichVu; $script:DaTatTuChay = $true } catch { GhiCanh "không tạm tắt được 'lỗi tự chạy lại' ($($_.Exception.Message)) — vẫn làm tiếp." }
    DungDichVu $TenDichVu
    $script:DaDung = $true
    GhiOk 'đã dừng'

    GhiBuoc '3. Sao lưu trọn bộ bản đang chạy (trước nâng cấp)'
    if ($BoQuaSaoLuu) { GhiCanh 'BỎ QUA sao lưu theo -BoQuaSaoLuu.' }
    else {
        $ra = ''
        if ($script:LaGia) {
            GhiLenhGia ("EXE: `"$exeMoi`" --contentRoot `"$ThuMucApp`" --sao-luu-truoc-nang-cap")
            $ma = [int](GiaTri 'exeMa'); $ra = [string](GiaTri 'exeRa')
        } else {
            $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'   # PowerShell 5.1: dòng stderr của exe không được thành lỗi dừng script
            try { $ra = (& $exeMoi --contentRoot $ThuMucApp --sao-luu-truoc-nang-cap 2>&1 | Out-String); $ma = $LASTEXITCODE } finally { $ErrorActionPreference = $eap }
        }
        Ghi ('  ' + $ra.Trim())
        if ($ma -ne 0 -or $ra -notmatch 'SAO-LUU-TRUOC-NANG-CAP: DAT') {
            GhiLoi "sao lưu trước nâng cấp LỖI (mã $ma) — CHƯA thay gì, bật lại bản cũ. Sửa lỗi (tab Cài đặt › Sao lưu: thư mục, quyền ghi cho dịch vụ SQL) rồi chạy lại."
            BatDichVu $TenDichVu; $script:DaDung = $false
            Thoat 4
        }
        GhiOk 'đã có bộ trước nâng cấp (tab Cài đặt › Sao lưu › Bộ trước nâng cấp)'
    }

    GhiBuoc '4. Thay phần mềm'
    $cu = "$ThuMucApp.cu-$gio"
    try { DoiTenThuMuc $ThuMucApp $cu }
    catch {
        GhiLoi "$($_.Exception.Message)"
        try { BatDichVu $TenDichVu; $script:DaDung = $false; GhiCanh "CHƯA thay gì — đã BẬT lại bản cũ. Đóng các cửa sổ / chương trình đang mở thư mục $ThuMucApp rồi chạy lại nâng cấp." }
        catch { GhiLoi "bật lại dịch vụ lỗi: $($_.Exception.Message) — bật tay trong services.msc (chưa thay gì)." }
        Thoat 7
    }
    GhiOk "bản cũ → $cu"
    try {
        Copy-Item -LiteralPath $BanMoi -Destination $ThuMucApp -Recurse
        Get-ChildItem -LiteralPath $cu -Filter 'appsettings*.json' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $ThuMucApp $_.Name) -Force; GhiOk "giữ cấu hình $($_.Name) của máy này" }
        foreach ($rel in @(@('wwwroot', 'uploads'), @('logs'))) {
            $src = $cu; $dst = $ThuMucApp
            foreach ($x in $rel) { $src = Join-Path $src $x; $dst = Join-Path $dst $x }
            if (Test-Path -LiteralPath $src) {
                New-Item -ItemType Directory -Path $dst -Force | Out-Null
                Get-ChildItem -LiteralPath $src -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dst -Recurse -Force }
                GhiOk "giữ $($rel -join '\')"
            }
        }
        CapQuyenThuMucApp $dv $ThuMucApp
    } catch {
        GhiLoi "chép bản mới lỗi: $($_.Exception.Message) — trả bản cũ."
        if (TraBanCu $cu "$ThuMucApp.loi-$gio") { $script:DaDung = $false; GhiCanh 'đã trả bản cũ + bật lại — CHƯA nâng cấp.'; Thoat 5 }
        Thoat 6
    }
    if ($ChoChaySql) {
        GhiBuoc '5. Chạy SQL nâng cấp'
        if ($KhongHoi) { GhiCanh '-KhongHoi: không dừng chờ — SQL nâng cấp phải chạy XONG trước khi gọi script.' }
        else { Read-Host 'Mở SSMS chạy file SQL đi kèm bản mới (nếu có), xong bấm Enter để bật phần mềm' | Out-Null }
    }

    GhiBuoc "6. Bật dịch vụ $TenDichVu (bản mới)"
    BatDichVu $TenDichVu; $script:DaDung = $false
    if (ChoTrang $cong 120) {
        GhiOk "bản mới chạy, http://localhost:$cong/login mở được"
        GhiBuoc 'XONG'
        Ghi "  Bản cũ giữ ở $cu — xoá tay sau vài ngày chạy ổn. Bộ trước nâng cấp ở tab Cài đặt › Sao lưu."
        Thoat 0
    }
    if ($ChoChaySql) {
        # rà độc lập 23/11 (Trung 4): đã chạy SQL nâng cấp → DB theo bản mới, bản cũ chạy trên DB này có thể hỏng / ghi sai → KHÔNG tự trả bản cũ
        GhiLoi "bản mới không lên sau 120 giây — ĐÃ CHẠY SQL nâng cấp nên KHÔNG tự trả bản cũ (bản cũ không hợp DB mới). Dịch vụ để nguyên (bản mới)."
        Ghi "  Cách 1: xem $ThuMucApp\logs + Event Viewer › Windows Logs › Application, sửa rồi bật lại dịch vụ."
        Ghi "  Cách 2 (quay hẳn về trước nâng cấp — cả DB lẫn phần mềm): mở thư mục sao lưu › <DB> › truoc-nang-cap › bộ mới nhất › khoi-phuc › khoi-phuc.bat"
        Ghi "         (chuột phải → Run as administrator). Bản cũ còn ở $cu."
        Thoat 8
    }
    GhiLoi "bản mới không lên sau 120 giây — TRẢ BẢN CŨ (log bản mới ở $ThuMucApp.loi-$gio\logs)."
    try { DungDichVu $TenDichVu } catch { GhiCanh "dừng dịch vụ lỗi: $($_.Exception.Message)" }
    if (-not (TraBanCu $cu "$ThuMucApp.loi-$gio")) { Thoat 6 }
    $script:DaDung = $false
    if (ChoTrang $cong 120) { GhiOk 'đã trả bản cũ, phần mềm chạy lại như trước nâng cấp.'; Thoat 5 }
    GhiLoi 'trả bản cũ rồi mà phần mềm vẫn không lên — xem Event Viewer; bộ trước nâng cấp ở thư mục sao lưu (khôi phục bằng khoi-phuc.bat trong bộ đó).'
    Thoat 6
} catch {
    GhiLoi ('Lỗi: ' + $_.Exception.Message)
    if ($script:DaDung) { GhiLoi "Dịch vụ $TenDichVu đang DỪNG — xem lỗi trên; thư mục app: $ThuMucApp$(if ($cu -and (Test-Path -LiteralPath $cu)) { " · bản cũ: $cu" }). Bật tay trong services.msc khi thư mục app đúng." }
    Thoat 9
}
