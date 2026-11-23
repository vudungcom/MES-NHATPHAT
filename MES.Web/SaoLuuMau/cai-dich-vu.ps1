<#
=====================================================================
 [G32B-DICH-VU 2026-11-23] Gói 32b — CÀI phần mềm MES thành Windows Service (tự chạy khi bật máy, tự chạy lại khi lỗi, có lịch sao lưu liên tục).
   Chạy quyền Administrator (chuột phải cai-dich-vu.bat → Run as administrator) trên máy chủ, SAU KHI đã có bản publish:
     dotnet publish MES.Web -c Release -o C:\MES\app      (Hùng làm trên máy dev rồi chép thư mục C:\MES\app sang máy khách)
   Mặc định: thư mục app = thư mục cha của SaoLuuMau (nơi có MES.Web.exe); tên dịch vụ MES; tài khoản chạy = LocalSystem
   (đọc / ghi được bản quyền HKLM + ProgramData như khi chạy tay quyền Administrator — tài khoản ảo NT SERVICE\MES dùng -TaiKhoanAo).
   Làm: tạo dịch vụ (khởi động "tự động — trễ" để SQL Server lên trước, phụ thuộc dịch vụ SQL nếu cùng máy) · ASPNETCORE_ENVIRONMENT=Production ·
        lỗi thì tự chạy lại sau 1 phút · cấp quyền ghi thư mục sao lưu cho dịch vụ SQL Server (lệnh BACKUP ghi file) · mở tường lửa cổng app
        (mạng Domain / Private) · bật dịch vụ, chờ trang /login.
   -Go: gỡ dịch vụ (dừng + xoá, không xoá thư mục / dữ liệu).
   -ChiCapQuyen [-ThuMucSaoLuu D:\MES-Backup]: chỉ cấp quyền ghi thư mục sao lưu cho dịch vụ SQL Server (sau khi đổi thư mục ở tab Sao lưu).
   Mã thoát: 0 đạt · 2 sai điều kiện · 3 không phải Administrator · 5 dịch vụ không lên · 9 lỗi khác.
   Nguồn: HÙNG 22/11 14:42 (máy khách chạy app dạng Windows Service) + 23/11 13:20 OK Gói 32b.
=====================================================================
#>
[CmdletBinding()]
param(
    [string]$ThuMucApp = '',
    [string]$TenDichVu = 'MES',
    [switch]$TaiKhoanAo,
    [string]$ThuMucSaoLuu = '',
    [switch]$KhongMoTuongLua,
    [switch]$ChiCapQuyen,
    [switch]$Go
)
. (Join-Path $PSScriptRoot 'chung.ps1')

try {
    if (-not (LaAdmin)) { GhiLoi 'Phải chạy bằng quyền Administrator (chuột phải cai-dich-vu.bat → Run as administrator).'; exit 3 }
    $dv = DichVu $TenDichVu

    if ($Go) {
        GhiBuoc "Gỡ dịch vụ $TenDichVu"
        if ($null -eq $dv) { GhiCanh "không có dịch vụ $TenDichVu"; exit 0 }
        DungDichVu $TenDichVu
        [void](ChayLenh 'sc.exe' @('delete', $TenDichVu))
        GhiOk "đã gỡ dịch vụ $TenDichVu (thư mục app + dữ liệu giữ nguyên)"
        exit 0
    }

    if ([string]::IsNullOrWhiteSpace($ThuMucApp)) { $ThuMucApp = Split-Path -Parent $PSScriptRoot }
    $ThuMucApp = (Resolve-Path -LiteralPath $ThuMucApp).Path
    $exe = Join-Path $ThuMucApp 'MES.Web.exe'
    GhiBuoc "1. Kiểm thư mục app: $ThuMucApp"
    if (-not (Test-Path -LiteralPath $exe)) { GhiLoi "không có $exe — cần bản publish: dotnet publish MES.Web -c Release -o <thư mục> (chép cả thư mục sang máy này)."; exit 2 }
    if (-not (Test-Path -LiteralPath (Join-Path $ThuMucApp 'wwwroot'))) { GhiLoi "không có thư mục wwwroot trong $ThuMucApp — không phải bản publish (bản build bin\ thiếu file giao diện)."; exit 2 }
    $app = DocAppsettings $ThuMucApp
    if ($null -eq $app -or [string]::IsNullOrWhiteSpace((ChuoiKetNoi $app))) { GhiLoi "thiếu $ThuMucApp\appsettings.json hoặc chuỗi kết nối DefaultConnection."; exit 2 }
    $cong = CongTuAppsettings $app
    GhiOk "MES.Web.exe · cổng $cong · SQL $(ServerTuChuoi (ChuoiKetNoi $app))"
    # dịch vụ SQL Server cùng máy (MSSQLSERVER hoặc MSSQL$<tên>) → phụ thuộc + cấp quyền thư mục sao lưu
    $sqlDv = $null
    if (LaSqlCungMay (ServerTuChuoi (ChuoiKetNoi $app))) {
        $inst = ((ServerTuChuoi (ChuoiKetNoi $app)) -split '\\', 2)
        if ($inst.Count -eq 2 -and $inst[1].Trim() -ne '') { $sqlDv = 'MSSQL$' + $inst[1].Trim().ToUpperInvariant() } else { $sqlDv = 'MSSQLSERVER' }
        if ($null -eq (DichVu $sqlDv)) { GhiCanh "không thấy dịch vụ SQL Server $sqlDv trên máy này — bỏ phụ thuộc."; $sqlDv = $null }
    }

    $mesData = Join-Path (ThuMucProgramData) 'MES_Server'
    if ([string]::IsNullOrWhiteSpace($ThuMucSaoLuu)) {   # thư mục sao lưu: tham số, không có thì đọc từ file cài đặt sao lưu (tab Cài đặt › Sao lưu)
        $slDir = Join-Path $mesData 'SaoLuu'
        if ($null -ne $app.SaoLuu -and -not [string]::IsNullOrWhiteSpace([string]$app.SaoLuu.ThuMucCaiDat)) { $slDir = [string]$app.SaoLuu.ThuMucCaiDat }
        $cfg = Get-ChildItem -LiteralPath $slDir -Filter 'cai-dat-*.json' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $cfg) { try { $ThuMucSaoLuu = [string](DocJson $cfg.FullName).ThuMucTheoMaySql } catch { } }
    }
    if ($ChiCapQuyen) {
        GhiBuoc 'Cấp quyền thư mục sao lưu cho dịch vụ SQL Server'
        if ([string]::IsNullOrWhiteSpace($ThuMucSaoLuu)) { GhiLoi 'chưa biết thư mục sao lưu — thêm -ThuMucSaoLuu <thư mục> hoặc nhập ở tab Cài đặt › Sao lưu trước.'; exit 2 }
        if ($null -eq $sqlDv) { GhiLoi 'không thấy dịch vụ SQL Server cùng máy.'; exit 2 }
        if (-not $script:LaGia) { New-Item -ItemType Directory -Path $ThuMucSaoLuu -Force | Out-Null }
        [void](ChayLenh 'icacls.exe' @($ThuMucSaoLuu, '/grant', "NT SERVICE\$($sqlDv):(OI)(CI)M"))
        GhiOk "dịch vụ SQL ($sqlDv) ghi được $ThuMucSaoLuu"
        exit 0
    }
    if ($null -ne $dv) { GhiLoi "đã có dịch vụ $TenDichVu ($(ThuMucCuaDichVu $dv)) — thay bản mới dùng nang-cap.bat; cài lại thì chạy cai-dich-vu.ps1 -Go trước."; exit 2 }
    if (CongDangNghe $cong) { GhiLoi "cổng $cong đang có phần mềm chạy (cửa sổ chay_app_5000.bat?) — tắt nó trước."; exit 2 }

    GhiBuoc "2. Tạo dịch vụ $TenDichVu"
    if ($script:LaGia) { GhiLenhGia ("NEW-SERVICE: $TenDichVu `"$exe`" phu-thuoc=$sqlDv") }
    else {
        $tham = @{ Name = $TenDichVu; BinaryPathName = ('"' + $exe + '"'); DisplayName = 'MES Nhat Phat'; StartupType = 'Automatic'; Description = 'MES Nhat Phat - phan mem theo doi san xuat (Blazor). Thu muc: ' + $ThuMucApp }
        if ($null -ne $sqlDv) { $tham['DependsOn'] = $sqlDv }
        New-Service @tham | Out-Null
    }
    [void](ChayLenh 'sc.exe' @('config', $TenDichVu, 'start=', 'delayed-auto'))
    [void](ChayLenh 'sc.exe' @('failure', $TenDichVu, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/60000'))
    if ($TaiKhoanAo) { [void](ChayLenh 'sc.exe' @('config', $TenDichVu, 'obj=', "NT SERVICE\$TenDichVu")) }
    if ($script:LaGia) { GhiLenhGia ("REG: HKLM\SYSTEM\CurrentControlSet\Services\$TenDichVu Environment = ASPNETCORE_ENVIRONMENT=Production") }
    else { New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$TenDichVu" -Name 'Environment' -PropertyType MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production') -Force | Out-Null }
    GhiOk "dịch vụ ${TenDichVu}: tự động (trễ)$(if ($null -ne $sqlDv) { ", chờ $sqlDv" }), lỗi tự chạy lại sau 1 phút, Production, tài khoản $(if ($TaiKhoanAo) { "NT SERVICE\$TenDichVu" } else { 'LocalSystem' })"

    GhiBuoc '3. Quyền thư mục'
    if (-not $script:LaGia) { New-Item -ItemType Directory -Path $mesData -Force | Out-Null }
    $ghiQuyen = New-Object System.Collections.Generic.List[string]
    if ($TaiKhoanAo) { $ghiQuyen.Add($ThuMucApp); $ghiQuyen.Add($mesData) }
    foreach ($d in $ghiQuyen) {
        [void](ChayLenh 'icacls.exe' @($d, '/grant', "NT SERVICE\$($TenDichVu):(OI)(CI)M"))
        GhiOk "NT SERVICE\$TenDichVu ghi được $d"
    }
    if (-not [string]::IsNullOrWhiteSpace($ThuMucSaoLuu)) {
        if (-not $script:LaGia) { New-Item -ItemType Directory -Path $ThuMucSaoLuu -Force | Out-Null }
        if ($null -ne $sqlDv) { [void](ChayLenh 'icacls.exe' @($ThuMucSaoLuu, '/grant', "NT SERVICE\$($sqlDv):(OI)(CI)M")); GhiOk "dịch vụ SQL ($sqlDv) ghi được thư mục sao lưu $ThuMucSaoLuu" }
        if ($TaiKhoanAo) { [void](ChayLenh 'icacls.exe' @($ThuMucSaoLuu, '/grant', "NT SERVICE\$($TenDichVu):(OI)(CI)M")) }
    } else {
        GhiCanh 'chưa cài thư mục sao lưu (tab Cài đặt › Sao lưu) — nhập thư mục ở tab rồi chạy: cai-dich-vu.ps1 -ChiCapQuyen'
    }

    if (-not $KhongMoTuongLua) {
        GhiBuoc "4. Tường lửa cổng $cong"
        $tenLuat = "MES Nhat Phat (TCP $cong)"
        if ($script:LaGia) { GhiLenhGia ("FIREWALL: $tenLuat") }
        elseif ($null -eq (Get-NetFirewallRule -DisplayName $tenLuat -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $tenLuat -Direction Inbound -Protocol TCP -LocalPort $cong -Action Allow -Profile Domain, Private | Out-Null
        }
        GhiOk "máy khác trong mạng (Domain / Private) vào được cổng $cong"
    }

    GhiBuoc "5. Bật dịch vụ $TenDichVu"
    BatDichVu $TenDichVu
    if (-not (ChoTrang $cong 120)) { GhiLoi "dịch vụ đã cài nhưng trang http://localhost:$cong/login không lên sau 120 giây — xem $ThuMucApp\logs + Event Viewer › Windows Logs › Application."; exit 5 }
    GhiOk "http://localhost:$cong/login mở được"
    GhiBuoc 'XONG'
    Ghi "  Dừng / bật: services.msc → MES Nhat Phat (hoặc lệnh: sc stop $TenDichVu / sc start $TenDichVu). Nâng cấp bản mới: nang-cap.bat."
    Ghi '  KHÔNG chạy thêm chay_app_5000.bat trên máy này (trùng cổng).'
    exit 0
} catch {
    GhiLoi ('Lỗi: ' + $_.Exception.Message)
    exit 9
}
