# =====================================================================
# [G32B-KHOIPHUC 2026-11-23] Gói 32b — hàm dùng chung cho khoi-phuc.ps1 / cai-dich-vu.ps1 / nang-cap.ps1 (dot-source: . "$PSScriptRoot\chung.ps1").
#   Chạy được trên Windows PowerShell 5.1 (có sẵn trên Windows 10 / 11 / Server 2016+) — KHÔNG dùng cú pháp chỉ có ở PowerShell 7.
#   CHẾ ĐỘ THỬ (chỉ bộ kiểm tools/GiaoNhanFuzz kịch bản saoluu dùng): biến môi trường MES_SAOLUU_GIA = 1 thư mục →
#     mọi lệnh SQL / dịch vụ / quyền thư mục / tường lửa / chạy exe chỉ GHI vào <thư mục>\lenh.log, trả lời giả theo <thư mục>\gia.json;
#     ProgramData = <thư mục>\ProgramData. Chép / giải nén / băm file vẫn chạy thật (để kiểm). KHÔNG đặt biến này khi chạy thật.
#   Nguồn: HÙNG 22/11 14:30 (sau này có sự cố tự xử lý được) + 23/11 13:20 OK Gói 32b.
# =====================================================================
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:GiaDir = $env:MES_SAOLUU_GIA
$script:LaGia = -not [string]::IsNullOrWhiteSpace($script:GiaDir)
$script:GiaJson = $null
$script:GiaLanCho = 0
if ($script:LaGia) {
    # rà độc lập 23/11 (Thấp 14): biến lỡ đặt trên máy thật → script không làm gì mà vẫn báo ĐẠT. Nay: thư mục phải có gia.json (bộ kiểm luôn tạo),
    #   thiếu → dừng mã 2; có thì in băng đỏ ở đầu mỗi lần chạy.
    $gf = Join-Path $script:GiaDir 'gia.json'
    if (-not (Test-Path -LiteralPath $gf)) {
        Write-Host "  [LOI] Biến môi trường MES_SAOLUU_GIA = $($script:GiaDir) (chế độ THỬ của bộ kiểm) nhưng không có gia.json — máy thật: xoá biến này (System Properties › Environment Variables) rồi chạy lại." -ForegroundColor Red
        [Environment]::Exit(2)   # file này được dot-source: "exit" chỉ thoát chung.ps1, script gọi vẫn chạy tiếp
    }
    $script:GiaJson = Get-Content -LiteralPath $gf -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "  [!!!] CHẾ ĐỘ THỬ (MES_SAOLUU_GIA) — KHÔNG làm gì thật với SQL / dịch vụ. Máy thật: xoá biến MES_SAOLUU_GIA." -ForegroundColor Red
}
$script:GiaDaLoi = @{}
# Chế độ thử: mục gia.json <khoa> = danh sách chuỗi; lần gọi nào có chứa 1 chuỗi trong đó thì LỖI (mỗi chuỗi lỗi 1 lần, ghi "LOI-GIA: …" vào lenh.log)
function GiaLoiNeu([string]$khoa, [string]$noiDung) {
    if (-not $script:LaGia) { return }
    $ds = @(GiaTri $khoa)
    $i = 0
    foreach ($x in $ds) {
        $dk = "$khoa#$i"; $i++
        if ($null -eq $x -or $script:GiaDaLoi.ContainsKey($dk)) { continue }
        if ($noiDung.Contains([string]$x)) { $script:GiaDaLoi[$dk] = $true; GhiLenhGia ("LOI-GIA: $khoa $x"); throw "lỗi giả ($khoa): $x" }
    }
}

function Ghi([string]$m, [string]$mau = 'Gray') { Write-Host $m -ForegroundColor $mau }
function GhiOk([string]$m) { Ghi ('  [OK]  ' + $m) 'Green' }
function GhiLoi([string]$m) { Ghi ('  [LOI] ' + $m) 'Red' }
function GhiCanh([string]$m) { Ghi ('  [!]   ' + $m) 'Yellow' }
function GhiBuoc([string]$m) { Ghi ''; Ghi ('== ' + $m) 'Cyan' }

function GhiLenhGia([string]$s) { Add-Content -LiteralPath (Join-Path $script:GiaDir 'lenh.log') -Value $s -Encoding UTF8 }
function GiaTri([string]$k) {
    if ($null -eq $script:GiaJson) { return $null }
    $p = $script:GiaJson.PSObject.Properties[$k]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function ThuMucProgramData {
    if ($script:LaGia) { return (Join-Path $script:GiaDir 'ProgramData') }
    if ([string]::IsNullOrWhiteSpace($env:ProgramData)) { return [IO.Path]::GetTempPath() }   # không phải Windows (chỉ khi thử)
    return $env:ProgramData
}
function TenMay {
    if ($script:LaGia -and $null -ne (GiaTri 'tenMay')) { return [string](GiaTri 'tenMay') }
    return $env:COMPUTERNAME
}

function LaAdmin {
    if ($script:LaGia) { return $true }
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function GioTen { return (Get-Date).ToString('yyyyMMdd_HHmmss') }

# Nối đường dẫn tương đối kiểu manifest ("du-lieu/MES_x.bak.zip") vào thư mục gốc
function NoiDuong([string]$goc, [string]$rel) {
    $p = $goc
    foreach ($x in ($rel -split '[\\/]')) { if ($x -ne '') { $p = Join-Path $p $x } }
    return $p
}

function DocJson([string]$f) { return (Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json) }

function DocAppsettings([string]$thuMuc) {
    $f = Join-Path $thuMuc 'appsettings.json'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    try { return (DocJson $f) } catch { return $null }
}

function ChuoiKetNoi($app) {
    if ($null -eq $app -or $null -eq $app.ConnectionStrings) { return $null }
    return [string]$app.ConnectionStrings.DefaultConnection
}

# "Server=.\SQLEXPRESS;Database=MES;..." → giá trị Server
function ServerTuChuoi([string]$cs) {
    if ([string]::IsNullOrWhiteSpace($cs)) { return $null }
    foreach ($part in ($cs -split ';')) {
        $kv = $part -split '=', 2
        if ($kv.Count -eq 2 -and @('server', 'data source', 'address', 'addr', 'network address') -contains $kv[0].Trim().ToLowerInvariant()) { return $kv[1].Trim() }
    }
    return $null
}

function CongTuAppsettings($app) {
    $u = $null
    try { $u = [string]$app.Kestrel.Endpoints.Http.Url } catch { }
    if ($u -match ':(\d+)\s*/?\s*$') { return [int]$Matches[1] }
    return 5000
}

function LaSqlCungMay([string]$server) {
    if ([string]::IsNullOrWhiteSpace($server)) { return $true }
    $h = ($server -split '\\')[0].Trim().ToLowerInvariant()
    $h = ($h -split ',')[0]
    if ($h -like 'tcp:*') { $h = $h.Substring(4) }
    return @('.', '(local)', 'localhost', '127.0.0.1', '::1', (TenMay).ToLowerInvariant()) -contains $h
}

# Phiên bản chính SQL Server từ chuỗi "13.0.5026.0 · Enterprise" / "16.0.1000.6"
function SoChinhSql([string]$v) {
    if ($v -match '^\s*(\d+)\.') { return [int]$Matches[1] }
    return 0
}

# ------------------------------------------------------------------ SQL (đăng nhập Windows của người chạy script — câu B4 Hùng 23/11)
function SqlMo([string]$server) {
    if ($script:LaGia) { GhiLenhGia ('SQL-MO: ' + $server); return $null }
    $cs = "Server=$server;Database=master;Integrated Security=SSPI;Connect Timeout=15;TrustServerCertificate=True;Application Name=MES-khoi-phuc"
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    $cn.add_InfoMessage({ param($s, $e) Write-Host ('  SQL: ' + $e.Message) -ForegroundColor DarkGray })
    $cn.Open()
    return $cn
}

# Chạy 1 câu SQL. -Bang: trả các dòng (DataRow); không: trả 1 giá trị. $khoaGia: tên mục trả lời giả trong gia.json (chế độ thử).
function SqlChay($cn, [string]$sql, [hashtable]$ts = @{}, [switch]$Bang, [string]$khoaGia = '') {
    if ($script:LaGia) {
        $tsTxt = ''
        if ($ts.Count -gt 0) { $tsTxt = ' | ' + ((($ts.Keys | Sort-Object) | ForEach-Object { "$_=$($ts[$_])" }) -join ', ') }
        GhiLenhGia ('SQL: ' + ($sql -replace '\s+', ' ').Trim() + $tsTxt)
        GiaLoiNeu 'sqlLoi' $sql
        $v = GiaTri ('sql_' + $khoaGia)
        if ($Bang) { if ($null -eq $v) { return } ; foreach ($r in @($v)) { $r } ; return }
        return $v
    }
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 0
    foreach ($k in $ts.Keys) { [void]$cmd.Parameters.AddWithValue($k, $ts[$k]) }
    if ($Bang) {
        $dt = New-Object System.Data.DataTable
        $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        [void]$da.Fill($dt)
        foreach ($r in $dt.Rows) { $r }
        return
    }
    $x = $cmd.ExecuteScalar()
    if ($x -is [System.DBNull]) { return $null }
    return $x
}

# ------------------------------------------------------------------ dịch vụ Windows
function DichVu([string]$ten) {
    if ($script:LaGia) {
        $dv = GiaTri 'dichVu'
        if ($null -eq $dv) { return $null }
        $p = $dv.PSObject.Properties[$ten]
        if ($null -eq $p) { return $null }
        return $p.Value
    }
    return (Get-CimInstance -ClassName Win32_Service -Filter ("Name='" + $ten.Replace("'", "''") + "'") -ErrorAction SilentlyContinue)
}

# Thư mục chứa exe của dịch vụ (PathName "C:\MES\app\MES.Web.exe" có thể có ngoặc kép / tham số)
function ThuMucCuaDichVu($dv) {
    if ($null -eq $dv) { return $null }
    $p = [string]$dv.PathName
    if ($p.StartsWith('"')) { $p = $p.Substring(1, $p.IndexOf('"', 1) - 1) }
    else { $i = $p.ToLowerInvariant().IndexOf('.exe'); if ($i -gt 0) { $p = $p.Substring(0, $i + 4) } }
    return (Split-Path -Parent $p)
}

# Tài khoản chạy dịch vụ (Win32_Service.StartName: LocalSystem / NT SERVICE\MES …)
function TaiKhoanDichVu($dv) {
    if ($null -eq $dv) { return '' }
    $p = $dv.PSObject.Properties['StartName']
    if ($null -eq $p) { return '' }
    return [string]$p.Value
}

# [G32B-KHOIPHUC] rà độc lập 23/11 (Trung 7): thư mục app TẠO MỚI (nâng cấp / khôi phục trọn bộ) chỉ thừa hưởng quyền thư mục cha → dịch vụ chạy tài khoản
#   ảo (cai-dich-vu -TaiKhoanAo: NT SERVICE\MES) mất quyền ghi logs / ảnh. Cấp lại quyền Sửa cho tài khoản đó; LocalSystem / quản trị: không cần.
function CapQuyenThuMucApp($dv, [string]$thuMuc) {
    $tk = TaiKhoanDichVu $dv
    if ($tk -notlike 'NT SERVICE\*') { return }
    [void](ChayLenh 'icacls.exe' @($thuMuc, '/grant', "$($tk):(OI)(CI)M"))
    GhiOk "quyền ghi $thuMuc cho $tk"
}

# rà độc lập 23/11 (Thấp 9): lúc đang thay / trả thư mục app, Windows không được tự bật lại dịch vụ (cài đặt "lỗi tự chạy lại sau 1 phút" của cai-dich-vu)
function TatTuChayLai([string]$ten) { [void](ChayLenh 'sc.exe' @('failure', $ten, 'reset=', '86400', 'actions=', '""')) }
function DatTuChayLai([string]$ten) { [void](ChayLenh 'sc.exe' @('failure', $ten, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/60000')) }

function DungDichVu([string]$ten) {
    if ($script:LaGia) { GhiLenhGia ('DICH-VU-DUNG: ' + $ten); return }
    $s = Get-Service -Name $ten
    if ($s.Status -ne 'Stopped') {
        Stop-Service -Name $ten -Force
        $s.WaitForStatus('Stopped', (New-TimeSpan -Seconds 120))
    }
}

function BatDichVu([string]$ten) {
    if ($script:LaGia) { GhiLenhGia ('DICH-VU-BAT: ' + $ten); return }
    Start-Service -Name $ten
}

# Có tiến trình đang nghe cổng (app chạy tay bằng chay_app_5000.bat)?
function CongDangNghe([int]$cong) {
    if ($script:LaGia) { return [bool](GiaTri 'congDangNghe') }
    try { return [bool](Get-NetTCPConnection -LocalPort $cong -State Listen -ErrorAction SilentlyContinue) } catch { return $false }
}

# Chờ trang /login trả 200 (tối đa $giay giây)
function ChoTrang([int]$cong, [int]$giay = 120) {
    if ($script:LaGia) {
        GhiLenhGia ("CHO-TRANG: http://localhost:$cong/login")
        $v = @(GiaTri 'trangLen')   # thử: true / false hoặc mảng theo thứ tự từng lần chờ
        if ($v.Count -eq 0 -or $null -eq $v[0]) { return $true }
        $i = [Math]::Min($script:GiaLanCho, $v.Count - 1); $script:GiaLanCho++
        return [bool]$v[$i]
    }
    $het = (Get-Date).AddSeconds($giay)
    while ((Get-Date) -lt $het) {
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:$cong/login" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return $true }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $false
}

# Chạy 1 lệnh ngoài (sc.exe, icacls…) — chế độ thử chỉ ghi lại
function ChayLenh([string]$exe, [string[]]$thamSo) {
    if ($script:LaGia) { GhiLenhGia ('LENH: ' + $exe + ' ' + ($thamSo -join ' ')); return 0 }
    $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'   # PowerShell 5.1: stderr của lệnh ngoài không thành lỗi dừng
    try { & $exe @thamSo 2>&1 | ForEach-Object { Write-Host ('    ' + $_) -ForegroundColor DarkGray }; $ma = $LASTEXITCODE } finally { $ErrorActionPreference = $eap }
    if ($ma -ne 0) { throw "$exe $($thamSo -join ' ') lỗi (mã $ma)" }
    return $ma
}

# ------------------------------------------------------------------ file
function BamFile([string]$f) { return (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }

# Giải nén file DUY NHẤT trong .zip ra đúng đường dẫn đích
function GiaiNen1File([string]$zip, [string]$dich) {
    $za = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $e = @($za.Entries | Where-Object { $_.Name -ne '' })
        if ($e.Count -ne 1) { throw "File nén $zip phải có đúng 1 file (có $($e.Count))." }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e[0], $dich, $true)
    } finally { $za.Dispose() }
}

function GiaiNenThuMuc([string]$zip, [string]$dich) {
    New-Item -ItemType Directory -Path $dich -Force | Out-Null
    GiaLoiNeu 'giaiNenLoi' $zip   # chế độ thử: giả lỗi giữa chừng (thư mục đích đã tạo dở)
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $dich)
}

# Đổi tên thư mục, thử lại vài lần (antivirus / Explorer giữ file)
function DoiTenThuMuc([string]$tu, [string]$den) {
    if ($script:LaGia) { try { GiaLoiNeu 'doiTenLoi' "$tu → $den" } catch { throw "Không đổi tên được `"$tu`" → `"$den`" (đang mở trong Explorer / cửa sổ lệnh?): $($_.Exception.Message)" } }
    for ($i = 1; $i -le 6; $i++) {
        try { Move-Item -LiteralPath $tu -Destination $den; return }
        catch { if ($i -eq 6) { throw "Không đổi tên được `"$tu`" → `"$den`" (đang mở trong Explorer / cửa sổ lệnh?): $($_.Exception.Message)" } ; Start-Sleep -Seconds (2 * $i) }
    }
}

# Kiểm bộ sao lưu theo manifest: mọi file có, đúng cỡ, đúng mã băm SHA-256. Trả số lỗi (0 = nguyên vẹn).
function KiemBo([string]$bo, $mf) {
    $loi = 0
    if ([string]$mf.KetQua -ne 'DAT') { GhiLoi "manifest ghi KetQua = $($mf.KetQua) (bộ LỖI: $($mf.Loi)) — không dùng bộ này để khôi phục."; $loi++ }
    $ds = @($mf.Files)
    if ($ds.Count -eq 0) { GhiLoi 'manifest không có danh sách file.'; return ($loi + 1) }
    foreach ($f in $ds) {
        $p = NoiDuong $bo ([string]$f.Duong)
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { GhiLoi "thiếu file $($f.Duong)"; $loi++; continue }
        $len = (Get-Item -LiteralPath $p).Length
        if ($len -ne [long]$f.Bytes) { GhiLoi "$($f.Duong): cỡ $len ≠ manifest $($f.Bytes)"; $loi++; continue }
        $h = BamFile $p
        if ($h -ne ([string]$f.Sha256).ToUpperInvariant()) { GhiLoi "$($f.Duong): mã băm khác manifest (file hỏng / bị sửa)"; $loi++; continue }
    }
    if ($loi -eq 0) { GhiOk "$($ds.Count) file đúng cỡ + đúng mã băm SHA-256" }
    return $loi
}
