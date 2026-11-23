<#
=====================================================================
 [G32B-KHOIPHUC 2026-11-23] Gói 32b — KHÔI PHỤC MES từ 1 bộ sao lưu (1 lệnh). Đọc HUONG-DAN-KHOI-PHUC.txt trước.
   Chạy trên MÁY CÀI SQL SERVER (phần mềm + SQL chung máy), bằng tài khoản Administrator của Windows
   (dễ nhất: chuột phải khoi-phuc.bat → Run as administrator). SQL đăng nhập bằng tài khoản Windows đó (phải là sysadmin của SQL Server).

   3 chế độ:
     -ChiKiem   : chỉ kiểm bộ sao lưu còn nguyên (mọi file đúng cỡ + mã băm SHA-256 trong manifest). Không đụng gì.
     -XemTruoc  : kiểm + đọc SQL Server + in ra từng bước SẼ làm (DB nào, file nào, đè vào đâu). Không đụng gì.
     (không có) : làm thật — dừng dịch vụ MES → sao lưu DB đang có (phòng khi cần quay lại) → khôi phục DB → (bộ trọn bộ) dựng lại
                  phần mềm + cấu hình + ảnh → bật lại dịch vụ → kiểm trang đăng nhập. Phải gõ CO để bắt đầu.

   Ví dụ (cửa sổ PowerShell chạy quyền Administrator):
     powershell -ExecutionPolicy Bypass -File khoi-phuc.ps1 -XemTruoc
     powershell -ExecutionPolicy Bypass -File khoi-phuc.ps1
     powershell -ExecutionPolicy Bypass -File khoi-phuc.ps1 -Bo "D:\MES-Backup\MES\nhanh\2026-11-23_140000" -ChiDuLieu

   Mặc định: bộ = thư mục cha của thư mục chứa script (script nằm trong <bộ>\khoi-phuc\); SQL Server = Server= trong appsettings của bộ
   (bộ nhanh: của thư mục app); DB = DB ghi trong manifest; thư mục app = thư mục của dịch vụ MES (chưa cài dịch vụ: C:\MES\app).
   Bản quyền: CÙNG máy (tên máy trùng manifest) thì chép lại nếu đang thiếu; máy MỚI thì KHÔNG chép (bản quyền gắn với máy) — kích hoạt lại
   ở trang /about (câu B4 Hùng 23/11). Cài đặt sao lưu: chỉ chép khi máy chưa có (cài đặt ở ngoài DB, cố ý không quay về bản cũ).
   DB đang HỎNG (SUSPECT / RECOVERY_PENDING / RESTORING / OFFLINE…): không sao lưu được DB đó → bỏ bước sao lưu, khôi phục đè luôn (có cảnh báo).
   RESTORE lỗi giữa chừng: DB cũ còn nguyên → bật lại dịch vụ; DB đã bị đè dở → TỰ TRẢ LẠI DB cũ từ bản vừa lưu ở bước 6 rồi bật lại dịch vụ.
   Mã thoát: 0 đạt · 1 bộ hỏng · 2 sai tham số / người dùng không gõ CO · 3 không phải Administrator · 4 lỗi SQL (đọc dòng [LOI] / [!] cuối: DB đang ở đâu,
             dịch vụ đang bật hay dừng) · 5 phần mềm không lên · 6 DB đã khôi phục nhưng dựng lại phần mềm lỗi (đã trả thư mục phần mềm cũ) · 9 lỗi khác.
   Nguồn: HÙNG 22/11 14:30 (sự cố tự xử lý được) + 23/11 13:20 OK Gói 32b (câu B4: đăng nhập Windows, máy mới không chép bản quyền).
=====================================================================
#>
[CmdletBinding()]
param(
    [string]$Bo = '',
    [switch]$ChiKiem,
    [switch]$XemTruoc,
    [string]$SqlServer = '',
    [string]$TenDb = '',
    [string]$ThuMucApp = '',
    [string]$TenDichVu = 'MES',
    [string]$ThuMucTam = '',
    [switch]$ChiDuLieu,
    [switch]$KhongHoi
)
. (Join-Path $PSScriptRoot 'chung.ps1')

$gio = GioTen
$logDir = Join-Path (Join-Path (ThuMucProgramData) 'MES_Server') 'khoi-phuc-log'
$dangGhiLog = $false
if (-not $ChiKiem) {
    try { New-Item -ItemType Directory -Path $logDir -Force | Out-Null; Start-Transcript -Path (Join-Path $logDir "khoi-phuc_$gio.txt") | Out-Null; $dangGhiLog = $true } catch { }
}
function Thoat([int]$ma) {
    if ($dangGhiLog) { try { Stop-Transcript | Out-Null } catch { } }
    exit $ma
}

try {
    # ---------------------------------------------------------------- 1. bộ sao lưu
    if ([string]::IsNullOrWhiteSpace($Bo)) { $Bo = Split-Path -Parent $PSScriptRoot }
    $Bo = (Resolve-Path -LiteralPath $Bo).Path
    $mfFile = Join-Path $Bo 'manifest.json'
    GhiBuoc "1. Bộ sao lưu: $Bo"
    if (-not (Test-Path -LiteralPath $mfFile)) { GhiLoi "không thấy manifest.json trong $Bo — chọn đúng folder 1 bộ (vd ...\MES\bo\2026-11-23_235500) bằng -Bo."; Thoat 2 }
    $mf = DocJson $mfFile
    $loai = [string]$mf.Loai
    Ghi "  Loại: $loai · thời điểm $($mf.ThoiDiem) · DB $($mf.Db) · máy $($mf.May) · phần mềm $($mf.PhienBanPhanMem)"
    Ghi "  SQL lúc lưu: $($mf.SqlServer)"
    if ((KiemBo $Bo $mf) -gt 0) { GhiLoi 'BỘ SAO LƯU KHÔNG NGUYÊN VẸN — không khôi phục từ bộ này. Chọn bộ khác (bộ ngày trước / bản trên Google).'; Thoat 1 }
    if ($ChiKiem) { GhiOk 'Bộ sao lưu nguyên vẹn (chỉ kiểm, không đụng gì).'; Thoat 0 }

    $coPhanMem = ($loai -ne 'nhanh') -and (-not $ChiDuLieu)
    $bakZip = @($mf.Files | Where-Object { ([string]$_.Duong) -like 'du-lieu/*.bak.zip' })
    if ($bakZip.Count -ne 1) { GhiLoi 'bộ không có đúng 1 file du-lieu\*.bak.zip.'; Thoat 1 }
    $bakZipPath = NoiDuong $Bo ([string]$bakZip[0].Duong)

    # ---------------------------------------------------------------- 2. thông số
    GhiBuoc '2. Thông số'
    $dv = DichVu $TenDichVu
    if ([string]::IsNullOrWhiteSpace($ThuMucApp)) {
        $ThuMucApp = ThuMucCuaDichVu $dv
        if ([string]::IsNullOrWhiteSpace($ThuMucApp)) { $ThuMucApp = 'C:\MES\app' }
    }
    $appBo = $null
    $chBo = Join-Path $Bo 'cau-hinh'
    if (Test-Path -LiteralPath $chBo) { $appBo = DocAppsettings $chBo }
    $appNay = DocAppsettings $ThuMucApp
    if ([string]::IsNullOrWhiteSpace($SqlServer)) {
        $SqlServer = ServerTuChuoi (ChuoiKetNoi $appBo)
        if ([string]::IsNullOrWhiteSpace($SqlServer)) { $SqlServer = ServerTuChuoi (ChuoiKetNoi $appNay) }
        if ([string]::IsNullOrWhiteSpace($SqlServer)) { $SqlServer = '.' }
    }
    if ([string]::IsNullOrWhiteSpace($TenDb)) { $TenDb = [string]$mf.Db }
    $appSau = $appNay
    if ($coPhanMem -and $null -ne $appBo) { $appSau = $appBo }
    $cong = CongTuAppsettings $appSau
    Ghi "  SQL Server: $SqlServer · DB khôi phục vào: $TenDb · thư mục app: $ThuMucApp · dịch vụ: $TenDichVu ($(if ($null -eq $dv) { 'CHƯA cài' } else { [string]$dv.State })) · cổng $cong"
    Ghi "  Khôi phục: $(if ($coPhanMem) { 'DỮ LIỆU + PHẦN MỀM + CẤU HÌNH + ẢNH (trọn bộ)' } else { 'CHỈ DỮ LIỆU' })"
    if (-not (LaSqlCungMay $SqlServer)) { GhiLoi "SQL Server `"$SqlServer`" không ở máy này — chạy script trên máy cài SQL Server (hoặc -SqlServer . nếu SQL ở máy này)."; Thoat 2 }
    if ($coPhanMem -and $PSScriptRoot.StartsWith($ThuMucApp.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        GhiLoi "Script đang nằm trong thư mục app ($ThuMucApp) — khôi phục cả phần mềm sẽ đổi tên thư mục này. Chạy script trong folder bộ sao lưu (<bộ>\khoi-phuc\), hoặc thêm -ChiDuLieu."
        Thoat 2
    }
    if (-not $XemTruoc -and -not (LaAdmin)) { GhiLoi 'Phải chạy bằng quyền Administrator (chuột phải khoi-phuc.bat → Run as administrator).'; Thoat 3 }

    # ---------------------------------------------------------------- 3. đọc SQL Server (chỉ đọc)
    GhiBuoc '3. Đọc SQL Server (chỉ đọc)'
    $cn = $null; $sqlDoc = $false
    try {
        $cn = SqlMo $SqlServer
        $verNay = [string](SqlChay $cn "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) + N' · ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))" -khoaGia 'phienBan')
        $coDb = [bool](SqlChay $cn 'SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END' @{ '@db' = $TenDb } -khoaGia 'coDb')
        $bakDir = [string](SqlChay $cn @"
DECLARE @d nvarchar(4000);
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @d OUTPUT;
SELECT COALESCE(@d, CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)));
"@ -khoaGia 'thuMucBackup')
        $dataDir = [string](SqlChay $cn "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))" -khoaGia 'thuMucData')
        $logDirSql = [string](SqlChay $cn "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))" -khoaGia 'thuMucLog')
        $ttDb = 'ONLINE'
        if ($coDb) { $t0 = SqlChay $cn 'SELECT state_desc FROM sys.databases WHERE name = @db' @{ '@db' = $TenDb } -khoaGia 'trangThaiDb'; if ($null -ne $t0) { $ttDb = [string]$t0 } }
        $fileCu = @()
        if ($coDb) { $fileCu = @(SqlChay $cn 'SELECT type_desc, physical_name FROM sys.master_files WHERE database_id = DB_ID(@db) ORDER BY file_id' @{ '@db' = $TenDb } -Bang -khoaGia 'fileCu') }
        $sqlDoc = $true
        Ghi "  SQL Server máy này: $verNay"
        Ghi "  DB $TenDb $(if (-not $coDb) { 'chưa có → tạo mới' } elseif ($ttDb -eq 'ONLINE') { 'ĐANG CÓ → sẽ bị ĐÈ (sao lưu trước khi đè)' } else { "ĐANG CÓ nhưng ở trạng thái $ttDb (hỏng / dở) → sẽ bị ĐÈ" })"
        Ghi "  Thư mục sao lưu mặc định của SQL: $bakDir · dữ liệu: $dataDir · log: $logDirSql"
        $vb = SoChinhSql ([string]$mf.SqlServer); $vn = SoChinhSql $verNay
        if ($vb -gt 0 -and $vn -gt 0 -and $vb -gt $vn) { GhiLoi "Bộ lưu từ SQL Server bản $vb, máy này bản $vn (cũ hơn) — SQL Server không khôi phục được bản mới về bản cũ. Cài SQL Server ≥ bản $vb."; Thoat 4 }
    } catch {
        if (-not $XemTruoc) { GhiLoi "Không đọc được SQL Server `"$SqlServer`": $($_.Exception.Message) — kiểm dịch vụ SQL Server đang chạy, tài khoản Windows này là sysadmin."; Thoat 4 }
        GhiCanh "Không đọc được SQL Server `"$SqlServer`": $($_.Exception.Message) (xem trước vẫn in các bước, chỗ '?' là chưa biết)."
        $coDb = $true; $ttDb = 'ONLINE'; $bakDir = '?'; $dataDir = '?'; $logDirSql = '?'; $fileCu = @()
    }
    # [G32B-KHOIPHUC] rà độc lập 23/11 (Cao 1): DB không ONLINE (SUSPECT / RECOVERY_PENDING / RESTORING / OFFLINE / EMERGENCY) → BACKUP + đổi chế độ
    #   người dùng đều lỗi → trước: script dừng mã 4, hỏng đúng ca cần khôi phục nhất. Nay bỏ 2 bước đó, RESTORE … REPLACE đè thẳng (SQL cho phép).
    $luuDbCu = $coDb -and $ttDb -eq 'ONLINE'
    if ([string]::IsNullOrWhiteSpace($ThuMucTam)) { $ThuMucTam = $bakDir }
    $bakTam = Join-Path $ThuMucTam ("$($TenDb)_khoi-phuc_$gio.bak")
    $bakAnToan = Join-Path $ThuMucTam ("$($TenDb)_truoc-khoi-phuc_$gio.bak")

    # ---------------------------------------------------------------- 4. in kế hoạch
    GhiBuoc '4. Các bước sẽ làm'
    $buoc = New-Object System.Collections.Generic.List[string]
    if ($null -ne $dv) { $buoc.Add("Dừng dịch vụ $TenDichVu") } else { $buoc.Add("Kiểm cổng $cong không có phần mềm MES chạy tay (cửa sổ chay_app_5000.bat phải tắt)") }
    if ($luuDbCu) { $buoc.Add("Sao lưu DB $TenDb đang có → $bakAnToan (COPY_ONLY — để quay lại nếu cần)") }
    elseif ($coDb) { $buoc.Add("KHÔNG sao lưu được DB $TenDb đang có (trạng thái $ttDb — DB hỏng / dở) → đè luôn") }
    $buoc.Add("Giải nén $($bakZip[0].Duong) → $bakTam, kiểm mã băm .bak = manifest")
    $buoc.Add("$(if ($luuDbCu) { "Đặt DB $TenDb OFFLINE (đẩy mọi kết nối ra) → " })RESTORE DATABASE $TenDb (REPLACE, CHECKSUM) — file dữ liệu: $(if (@($fileCu).Count -gt 0) { 'giữ chỗ file hiện tại' } else { "$dataDir / $logDirSql" })")
    $buoc.Add('Nối lại người dùng SQL của phần mềm (nếu tài khoản đăng nhập cùng tên đã có), so số dòng từng bảng với manifest')
    if ($coPhanMem) {
        $buoc.Add("Đổi tên thư mục app hiện có → $ThuMucApp.truoc-khoi-phuc-$gio (giữ lại, xoá tay khi chạy ổn)")
        $buoc.Add("Giải nén phan-mem\MES.Web.zip → $ThuMucApp; chép cau-hinh\appsettings*.json; giải nén uploads\uploads.zip → wwwroot\uploads")
        $buoc.Add('Cài đặt sao lưu (chỉ khi máy chưa có) · bản quyền: ' + $(if ((TenMay) -ieq [string]$mf.May) { 'cùng máy — chép lại nếu đang thiếu' } else { "máy KHÁC ($(TenMay) ≠ $($mf.May)) — KHÔNG chép, kích hoạt lại ở /about" }))
    }
    if ($null -ne $dv) { $buoc.Add("Bật dịch vụ $TenDichVu, chờ http://localhost:$cong/login") } else { $buoc.Add('Chưa có dịch vụ: chạy cai-dich-vu.bat (thư mục app\SaoLuuMau) sau khi khôi phục') }
    $n = 0
    foreach ($b in $buoc) { $n++; Ghi "  $n. $b" }
    if ($null -ne $mf.FileCauHinhThem -and @($mf.FileCauHinhThem).Count -gt 0) {
        GhiCanh 'File cấu hình thêm (KHÔNG tự chép đè — chép tay nếu máy mới):'
        foreach ($x in @($mf.FileCauHinhThem)) { Ghi "      $x" }
    }
    if ($XemTruoc) { GhiOk 'Xem trước xong — KHÔNG đụng gì. Chạy lại không có -XemTruoc để làm thật.'; Thoat 0 }
    if (-not $sqlDoc) { Thoat 4 }

    if (-not $KhongHoi) {
        Ghi ''
        if ($coDb -and -not $luuDbCu) { GhiCanh "DB $TenDb đang $ttDb — KHÔNG có bản sao lưu DB hiện tại trước khi đè (DB hỏng không lưu được)." }
        $tl = Read-Host "Gõ CO (chữ hoa) để khôi phục — DB $TenDb hiện tại sẽ bị ĐÈ bằng bộ $($mf.ThoiDiem)"
        if ($tl -cne 'CO') { GhiCanh 'Không gõ CO — dừng, không đụng gì.'; Thoat 2 }
    }

    # ---------------------------------------------------------------- 5. làm thật
    GhiBuoc '5. Dừng phần mềm'
    if ($null -ne $dv) { DungDichVu $TenDichVu; GhiOk "đã dừng dịch vụ $TenDichVu" }
    elseif (CongDangNghe $cong) { GhiLoi "Cổng $cong đang có phần mềm chạy (cửa sổ chay_app_5000.bat?) — tắt nó rồi chạy lại."; Thoat 2 }

    $daLuuCu = $false; $daOffline = $false; $daChayRestore = $false
    try {
        if ($luuDbCu) {
            GhiBuoc '6. Sao lưu DB đang có (phòng khi cần quay lại)'
            [void](SqlChay $cn 'BACKUP DATABASE @db TO DISK = @p WITH COPY_ONLY, CHECKSUM, INIT, FORMAT, NAME = N''MES truoc khi khoi phuc''' @{ '@db' = $TenDb; '@p' = $bakAnToan })
            $daLuuCu = $true
            GhiOk "đã lưu $bakAnToan"
        } elseif ($coDb) { GhiCanh "6. Bỏ sao lưu DB đang có — DB $TenDb ở trạng thái $ttDb (không lưu được)." }

        GhiBuoc '7. Giải nén + kiểm file .bak'
        GiaiNen1File $bakZipPath $bakTam
        $hb = BamFile $bakTam
        if ($hb -ne ([string]$mf.BakSha256).ToUpperInvariant()) { throw "file .bak giải nén ra có mã băm khác manifest — bộ hỏng" }
        GhiOk "$bakTam đúng mã băm"

        GhiBuoc "8. RESTORE DATABASE $TenDb"
        $ds = @(SqlChay $cn 'RESTORE FILELISTONLY FROM DISK = @p' @{ '@p' = $bakTam } -Bang -khoaGia 'fileList')
        if ($ds.Count -eq 0) { throw 'RESTORE FILELISTONLY không trả file nào.' }
        $ts = @{ '@db' = $TenDb; '@p' = $bakTam }
        $move = New-Object System.Collections.Generic.List[string]
        $cuD = @($fileCu | Where-Object { [string]$_.type_desc -eq 'ROWS' } | ForEach-Object { [string]$_.physical_name })
        $cuL = @($fileCu | Where-Object { [string]$_.type_desc -eq 'LOG' } | ForEach-Object { [string]$_.physical_name })
        $nD = @($ds | Where-Object { ([string]$_.Type) -ne 'L' }).Count; $nL = @($ds | Where-Object { ([string]$_.Type) -eq 'L' }).Count
        $giuChoCu = ($cuD.Count -eq $nD -and $cuL.Count -eq $nL)   # DB đang có cùng số file → đè đúng chỗ file hiện tại; khác → thư mục mặc định của SQL
        $iD = 0; $iL = 0; $k = 0
        foreach ($f in $ds) {
            $k++
            if (([string]$f.Type) -eq 'L') {
                if ($giuChoCu) { $dich = $cuL[$iL] } else { $dich = Join-Path $logDirSql ($(if ($iL -eq 0) { "$($TenDb)_log.ldf" } else { "$($TenDb)_log$iL.ldf" })) }
                $iL++
            } else {
                if ($giuChoCu) { $dich = $cuD[$iD] } else { $dich = Join-Path $dataDir ($(if ($iD -eq 0) { "$TenDb.mdf" } else { "$($TenDb)_$iD.ndf" })) }
                $iD++
            }
            $ts["@l$k"] = [string]$f.LogicalName; $ts["@f$k"] = $dich
            $move.Add("MOVE @l$k TO @f$k")
            Ghi "  $($f.LogicalName) → $dich"
        }
        # rà độc lập 23/11 (Trung 5): trước SINGLE_USER rồi RESTORE là 2 lệnh rời — kết nối khác (SSMS, app máy khác, SQL Agent) chen vào giữa
        #   chiếm "1 chỗ" → RESTORE "Exclusive access could not be obtained". Nay đặt OFFLINE (không ai nối được nữa); RESTORE … RECOVERY tự đưa DB ONLINE.
        if ($luuDbCu) {
            [void](SqlChay $cn "DECLARE @s nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET OFFLINE WITH ROLLBACK IMMEDIATE'; EXEC (@s);" @{ '@db' = $TenDb })
            $daOffline = $true
        }
        $daChayRestore = $true
        [void](SqlChay $cn ("RESTORE DATABASE @db FROM DISK = @p WITH " + ($move -join ', ') + ", REPLACE, CHECKSUM, RECOVERY, STATS = 10") $ts)
        GhiOk "đã khôi phục DB $TenDb từ bộ $($mf.ThoiDiem)"
    } catch {
        GhiLoi "Khôi phục DB lỗi: $($_.Exception.Message)"
        # [G32B-KHOIPHUC] rà độc lập 23/11 (Cao 2): trước luôn khuyên "bật lại dịch vụ để dùng DB cũ" — sai khi RESTORE đã đè dở (DB ở RESTORING).
        #   Nay xét DB đang ở đâu: còn nguyên → bật lại dịch vụ; đè dở + có bản bước 6 → tự RESTORE lại bản đó; không có → nói rõ, KHÔNG bật dịch vụ.
        $dbCuCon = -not $daChayRestore -and ($luuDbCu -or -not $coDb)
        if ($daChayRestore) {
            $tt = $null
            try { $tt = [string](SqlChay $cn 'SELECT state_desc FROM sys.databases WHERE name = @db' @{ '@db' = $TenDb } -khoaGia 'trangThaiSauLoi') } catch { }
            if ($daOffline -and $tt -eq 'OFFLINE') {
                # RESTORE dừng trước khi ghi (kiểm đầu file, đường dẫn, quyền) → file DB cũ còn nguyên, đưa ONLINE lại
                try {
                    [void](SqlChay $cn "DECLARE @s nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET ONLINE'; EXEC (@s);" @{ '@db' = $TenDb })
                    $tt = [string](SqlChay $cn 'SELECT state_desc FROM sys.databases WHERE name = @db' @{ '@db' = $TenDb } -khoaGia 'trangThaiSauOnline')
                } catch { $tt = '?' }
                if ($tt -eq 'ONLINE') { $dbCuCon = $true; GhiOk "RESTORE chưa ghi gì — DB $TenDb cũ còn nguyên, đã đưa ONLINE lại" }
            }
            if (-not $dbCuCon -and $daLuuCu) {
                GhiCanh "DB $TenDb bị đè dở (trạng thái $tt) — TRẢ LẠI DB cũ từ $bakAnToan …"
                try {
                    [void](SqlChay $cn 'RESTORE DATABASE @db FROM DISK = @p WITH REPLACE, CHECKSUM, RECOVERY, STATS = 10' @{ '@db' = $TenDb; '@p' = $bakAnToan })
                    $dbCuCon = $true
                    GhiOk "đã trả lại DB $TenDb như lúc trước khi khôi phục"
                } catch {
                    GhiLoi "trả lại DB cũ lỗi: $($_.Exception.Message)"
                    GhiLoi "DB $TenDb KHÔNG dùng được. Dịch vụ $TenDichVu giữ DỪNG. Tự trả tay (SSMS, quyền sysadmin): RESTORE DATABASE [$TenDb] FROM DISK = N'$bakAnToan' WITH REPLACE, RECOVERY; — hoặc chạy lại script với bộ sao lưu khác."
                }
            } elseif (-not $dbCuCon) {
                GhiLoi "DB $TenDb đang $tt$(if ($coDb) { ' (DB trước đó hỏng, không có bản lưu)' }) — KHÔNG dùng được. Dịch vụ $TenDichVu giữ DỪNG. Chạy lại script với bộ sao lưu khác (bộ ngày trước / bản trên Google)."
            }
        }
        if ($dbCuCon) {
            if ($coDb -and $null -ne $dv) {
                try { BatDichVu $TenDichVu; GhiCanh "đã BẬT lại dịch vụ $TenDichVu với DB cũ — CHƯA khôi phục gì. Sửa lỗi trên rồi chạy lại." }
                catch { GhiLoi "bật lại dịch vụ $TenDichVu lỗi: $($_.Exception.Message) — bật tay trong services.msc." }
            } elseif ($null -ne $dv) { GhiCanh "Dịch vụ $TenDichVu đang DỪNG (máy chưa có DB $TenDb) — sửa lỗi trên rồi chạy lại." }
            if ($daLuuCu) { Ghi "  Bản DB trước khi khôi phục: $bakAnToan (xoá tay khi không cần)." }
        } elseif (-not $daChayRestore) {
            GhiCanh "CHƯA khôi phục gì — DB $TenDb vẫn ở trạng thái $ttDb như trước. Dịch vụ $TenDichVu giữ DỪNG. Sửa lỗi trên rồi chạy lại (hoặc chọn bộ khác)."
        }
        Thoat 4
    } finally {
        if (Test-Path -LiteralPath $bakTam) { try { Remove-Item -LiteralPath $bakTam -Force } catch { } }
    }

    GhiBuoc '9. Kiểm DB sau khôi phục'
    $mo = @(SqlChay $cn @"
DECLARE @s nvarchar(max) = N'SELECT dp.name FROM ' + QUOTENAME(@db) + N'.sys.database_principals dp LEFT JOIN sys.server_principals sp ON sp.sid = dp.sid
WHERE dp.type = ''S'' AND dp.authentication_type = 1 AND sp.sid IS NULL AND dp.name NOT IN (''dbo'', ''guest'', ''INFORMATION_SCHEMA'', ''sys'')';
EXEC (@s);
"@ @{ '@db' = $TenDb } -Bang -khoaGia 'nguoiMoCoi')
    foreach ($u in $mo) {
        $ten = [string]$u.name
        $coLogin = [bool](SqlChay $cn 'SELECT CASE WHEN SUSER_ID(@n) IS NULL THEN 0 ELSE 1 END' @{ '@n' = $ten } -khoaGia 'coLogin')
        if ($coLogin) {
            [void](SqlChay $cn "DECLARE @s nvarchar(max) = N'USE ' + QUOTENAME(@db) + N'; ALTER USER ' + QUOTENAME(@n) + N' WITH LOGIN = ' + QUOTENAME(@n); EXEC (@s);" @{ '@db' = $TenDb; '@n' = $ten })
            GhiOk "nối lại người dùng SQL $ten với tài khoản đăng nhập cùng tên"
        } else {
            GhiCanh "người dùng SQL $ten chưa có tài khoản đăng nhập trên máy này (máy mới) — tạo login $ten (SSMS › Security › Logins, mật khẩu như appsettings) rồi chạy: USE [$TenDb]; ALTER USER [$ten] WITH LOGIN = [$ten];"
        }
    }
    $dem = @(SqlChay $cn @"
DECLARE @s nvarchar(max) = N'SELECT s.name + N''.'' + t.name AS bang, SUM(p.rows) AS so FROM ' + QUOTENAME(@db) + N'.sys.tables t
JOIN ' + QUOTENAME(@db) + N'.sys.schemas s ON s.schema_id = t.schema_id JOIN ' + QUOTENAME(@db) + N'.sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0 GROUP BY s.name, t.name';
EXEC (@s);
"@ @{ '@db' = $TenDb } -Bang -khoaGia 'soDong')
    $khop = 0; $lech = 0
    foreach ($p in $mf.SoDong.PSObject.Properties) {
        $r = $dem | Where-Object { [string]$_.bang -eq $p.Name } | Select-Object -First 1
        if ($null -ne $r -and [long]$r.so -eq [long]$p.Value) { $khop++ } else { $lech++; if ($lech -le 5) { GhiCanh "bảng $($p.Name): manifest $($p.Value) · sau khôi phục $(if ($null -eq $r) { 'không có' } else { $r.so })" } }
    }
    if ($lech -eq 0) { GhiOk "số dòng $khop bảng khớp manifest" } else { GhiCanh "$lech bảng lệch số dòng (manifest đếm NGAY TRƯỚC lệnh BACKUP — lệch vài dòng do người đang nhập là bình thường; lệch nhiều thì báo IT)" }

    # ---------------------------------------------------------------- 10. phần mềm + cấu hình
    if ($coPhanMem) {
        GhiBuoc '10. Dựng lại phần mềm + cấu hình + ảnh'
        # rà độc lập 23/11 (Trung 6): lỗi ở đây (ổ đầy, zip hỏng, file bị giữ) trước rơi xuống "Lỗi không lường trước" mã 9, thư mục app dở, dịch vụ dừng.
        #   Nay: thư mục dở đổi tên .loi-<giờ>, trả thư mục app cũ, bật dịch vụ, báo rõ DB ĐÃ là bản khôi phục còn phần mềm vẫn bản cũ (mã 6).
        $appTruoc = "$ThuMucApp.truoc-khoi-phuc-$gio"; $daDoiTen = $false
        try {
            if (Test-Path -LiteralPath $ThuMucApp) { DoiTenThuMuc $ThuMucApp $appTruoc; $daDoiTen = $true; GhiOk "thư mục app cũ → $appTruoc" }
            GiaiNenThuMuc (Join-Path (Join-Path $Bo 'phan-mem') 'MES.Web.zip') $ThuMucApp
            GhiOk "phần mềm → $ThuMucApp ($($mf.PhienBanPhanMem))"
            Get-ChildItem -LiteralPath $chBo -Filter 'appsettings*.json' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $ThuMucApp $_.Name) -Force; GhiOk "cấu hình $($_.Name)" }
            $upZip = Join-Path (Join-Path $Bo 'uploads') 'uploads.zip'
            if (Test-Path -LiteralPath $upZip) {
                $tmpUp = Join-Path ([IO.Path]::GetTempPath()) "mes-uploads-$gio"
                GiaiNenThuMuc $upZip $tmpUp
                $upDich = Join-Path (Join-Path $ThuMucApp 'wwwroot') 'uploads'
                New-Item -ItemType Directory -Path $upDich -Force | Out-Null
                Get-ChildItem -LiteralPath $tmpUp -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $upDich -Recurse -Force }
                Remove-Item -LiteralPath $tmpUp -Recurse -Force
                GhiOk 'ảnh (uploads) → wwwroot\uploads'
            }
            CapQuyenThuMucApp $dv $ThuMucApp
        } catch {
            GhiLoi "dựng lại phần mềm lỗi: $($_.Exception.Message)"
            if ($daDoiTen) {
                try {
                    if (Test-Path -LiteralPath $ThuMucApp) { DoiTenThuMuc $ThuMucApp "$ThuMucApp.loi-$gio"; Ghi "  thư mục dở → $ThuMucApp.loi-$gio (xoá tay)" }
                    DoiTenThuMuc $appTruoc $ThuMucApp
                    GhiOk "đã trả thư mục phần mềm cũ → $ThuMucApp"
                } catch {
                    GhiLoi "trả thư mục phần mềm cũ lỗi: $($_.Exception.Message) — đổi tên tay $appTruoc → $ThuMucApp rồi bật dịch vụ $TenDichVu. DB $TenDb ĐÃ là bản khôi phục."
                    Thoat 6
                }
            }
            if ($null -ne $dv) { try { BatDichVu $TenDichVu; GhiOk "đã bật lại dịch vụ $TenDichVu" } catch { GhiLoi "bật dịch vụ lỗi: $($_.Exception.Message) — bật tay trong services.msc." } }
            GhiCanh "DB $TenDb ĐÃ là bản khôi phục ($($mf.ThoiDiem)); PHẦN MỀM + cấu hình vẫn là bản cũ trên máy. Sửa lỗi trên rồi chạy lại (DB sẽ khôi phục lại lần nữa — vô hại), hoặc dùng tiếp nếu phần mềm cũ chạy ổn."
            Thoat 6
        }
        $appMoi = DocAppsettings $ThuMucApp
        $mesData = Join-Path (ThuMucProgramData) 'MES_Server'
        $slDir = Join-Path $mesData 'SaoLuu'
        if ($null -ne $appMoi -and $null -ne $appMoi.SaoLuu -and -not [string]::IsNullOrWhiteSpace([string]$appMoi.SaoLuu.ThuMucCaiDat)) { $slDir = [string]$appMoi.SaoLuu.ThuMucCaiDat }
        $slBo = Join-Path $chBo 'sao-luu'
        if (Test-Path -LiteralPath $slBo) {
            New-Item -ItemType Directory -Path $slDir -Force | Out-Null
            foreach ($f in (Get-ChildItem -LiteralPath $slBo -File)) {
                $d = Join-Path $slDir $f.Name
                if (Test-Path -LiteralPath $d) { Ghi "  cài đặt sao lưu $($f.Name): máy đã có — giữ bản đang có" }
                else { Copy-Item -LiteralPath $f.FullName -Destination $d; GhiOk "cài đặt sao lưu $($f.Name) (máy chưa có)" }
            }
        }
        $licBo = Join-Path $chBo 'ban-quyen'
        $licDich = Join-Path $mesData 'License'
        if ((TenMay) -ieq [string]$mf.May) {
            $thieu = -not (Test-Path -LiteralPath $licDich) -or @(Get-ChildItem -LiteralPath $licDich -File -ErrorAction SilentlyContinue).Count -eq 0
            if ($thieu -and (Test-Path -LiteralPath $licBo)) {
                New-Item -ItemType Directory -Path $licDich -Force | Out-Null
                Get-ChildItem -LiteralPath $licBo -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licDich -Recurse -Force }
                GhiOk 'bản quyền: cùng máy, đang thiếu → đã chép lại'
            } else { Ghi '  bản quyền: cùng máy, đang có — giữ nguyên' }
        } else {
            GhiCanh "bản quyền: máy KHÁC máy lúc lưu ($(TenMay) ≠ $($mf.May)) — KHÔNG chép (bản quyền gắn với máy). Mở phần mềm → trang /about → Kích hoạt lại."
        }
        $svBo = ServerTuChuoi (ChuoiKetNoi $appMoi)
        if (-not [string]::IsNullOrWhiteSpace($svBo) -and -not (LaSqlCungMay $svBo)) {
            GhiCanh "appsettings.json trỏ SQL Server `"$svBo`" (không phải máy này) — mở $ThuMucApp\appsettings.json sửa Server= cho đúng (vd Server=.\SQLEXPRESS) trước khi bật."
        }
    }

    # ---------------------------------------------------------------- 11. bật lại
    GhiBuoc '11. Bật phần mềm'
    if ($null -ne $dv) {
        BatDichVu $TenDichVu
        if (ChoTrang $cong 120) { GhiOk "dịch vụ $TenDichVu đã chạy, trang http://localhost:$cong/login mở được" }
        else { GhiLoi "dịch vụ $TenDichVu không mở được trang sau 120 giây — xem $ThuMucApp\logs và Event Viewer › Windows Logs › Application."; Thoat 5 }
    } else {
        GhiCanh "Máy chưa có dịch vụ $TenDichVu — chạy $ThuMucApp\SaoLuuMau\cai-dich-vu.bat (chuột phải → Run as administrator)."
    }

    GhiBuoc 'XONG'
    GhiOk "Đã khôi phục $TenDb từ bộ $($mf.ThoiDiem)."
    Ghi '  Việc tiếp: đăng nhập kiểm vài PO gần nhất (Kế hoạch, Giao nhận, WTS); vào Cài đặt › Sao lưu bấm "Tạo trọn bộ ngay".'
    if ($coDb) { Ghi "  DB trước khi khôi phục giữ ở $bakAnToan — xoá tay khi chắc chắn không cần." }
    Thoat 0
} catch {
    GhiLoi ("Lỗi không lường trước: " + $_.Exception.Message)
    Thoat 9
}
