@echo off
rem ===========================================================================================
rem  [KIEMTRA 2026-11-01] Chay KIEM TRA DAY DU bo kiem GiaoNhanFuzz tren may Hung (nhanh hon may Claude 2 nhan)
rem  - KHONG dung SQL, KHONG dung du lieu server: DB gia trong bo nho.
rem  - Chay trong THU MUC CLONE RIENG (vd D:\MES\claude-work\mes-prototype\tools), KHONG chay trong thu muc
rem    app 5000 dang "dotnet watch" (sua / build o do = app that nap code ngay).
rem  - Ket qua: tools\ketqua_kiemtra_<ngay gio>.txt  -> gui file nay cho Claude.
rem  [KIEM-TREO 2026-11-01] Moi kich ban / luot qua TREO_PHUT phut (mac dinh 20) -> tu tat, ghi "TREO ... lenh <PO> <ops> <seed>"
rem    vao tools\claude_trangthai.txt + file ket qua. Doi: set TREO_PHUT=30 truoc khi chay.
rem  Dung:   kiemtra.bat            = 8 kich ban + nang 60 PO x 120 thao tac (mac dinh, du lop)
rem          kiemtra.bat gap        = 8 kich ban + nang 15 PO x 60 (sua gap cho khach, vai phut)
rem          kiemtra.bat 100 150 7  = tu chon so PO, thao tac / PO, seed dau
rem ===========================================================================================
setlocal
chcp 65001 >nul
cd /d "%~dp0GiaoNhanFuzz"
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyMMdd_HHmm"') do set TS=%%i
set OUT=%~dp0ketqua_kiemtra_%TS%.txt

set "PO=60"
set "OPS=120"
set "SEED=%RANDOM%"
if /i "%~1"=="gap" goto gap
if /i "%~1"=="tong" goto tong
if not "%~1"=="" set "PO=%~1"
if not "%~2"=="" set "OPS=%~2"
if not "%~3"=="" set "SEED=%~3"
goto chay
:gap
set "PO=15"
set "OPS=60"
goto chay
:tong
rem [KIEM-TONG 2026-11-01] Kiem toan he thong (lan chay cuoi / truoc ban giao): danh muc CHAN (thieu tag = SAI) + 2 luot nang lon 2 seed
set "PO=120"
set "OPS=160"
set "TONG=1"
set "DANHMUC_CHAN=1"
:chay

echo KIEM TRA %date% %time%  ^|  nang %PO% PO x %OPS% thao tac, seed %SEED%  ^|  %NUMBER_OF_PROCESSORS% nhan > "%OUT%"
git -C "%~dp0.." log --oneline -1 >> "%OUT%" 2>nul

rem [KIEM-NHAN 2026-11-01] chup tai may truoc khi chay: CPU dang dung + 6 tien trinh an CPU nhat (de biet cham vi may ban viec khac)
powershell -NoProfile -Command "$c=(Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average; 'May truoc khi chay: CPU dang dung ' + $c + '%%'; Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $_.Name -notin '_Total','Idle' } | Sort-Object PercentProcessorTime -Descending | Select-Object -First 6 | ForEach-Object { '  ' + $_.Name + ' ' + [math]::Round($_.PercentProcessorTime / $env:NUMBER_OF_PROCESSORS) + '%%' }" >> "%OUT%" 2>&1
rem [KIEM-NHAN] ten CPU + so nhan THAT (vat ly) / luong. Mac dinh chay cung luc = so nhan that (do 01/11 13:30: 19 tien trinh tren 20 luong CHAM hon 10)
for /f %%c in ('powershell -NoProfile -Command "Get-CimInstance Win32_Processor | Measure-Object NumberOfCores -Sum | Select-Object -ExpandProperty Sum"') do set NHAN_THAT=%%c
powershell -NoProfile -Command "Get-CimInstance Win32_Processor | ForEach-Object { 'CPU: ' + $_.Name + ' | nhan that ' + $_.NumberOfCores + ' | luong ' + $_.NumberOfLogicalProcessors + ' | xung toi da ' + $_.MaxClockSpeed + ' MHz' }; $m=Get-CimInstance Win32_OperatingSystem; 'RAM trong: ' + [math]::Round($m.FreePhysicalMemory/1MB,1) + ' / ' + [math]::Round($m.TotalVisibleMemorySize/1MB,1) + ' GB'" >> "%OUT%" 2>&1
if "%SONG_SONG%"=="" if not "%NHAN_THAT%"=="" set "SONG_SONG=%NHAN_THAT%"
echo Chay cung luc: %SONG_SONG% tien trinh (SONG_SONG; mac dinh = so nhan that %NHAN_THAT%)>> "%OUT%"
echo [1/2] Bien dich bo kiem...
dotnet build GiaoNhanFuzz.csproj -c Release -o bin2 -v q -nologo >> "%OUT%" 2>&1
if errorlevel 1 ( echo BIEN DICH LOI - xem %OUT% & echo KET QUA: BIEN DICH LOI>> "%OUT%" & exit /b 2 )

rem [KIEM-TONG 2026-11-01] So phep kiem: moi tag sua trong MES.Web phai co ca kiem trong tools\DanhMucKiem.txt; ca tro toi khong co = SAI
echo [1b] Doi chieu danh muc kiem (tools\DanhMucKiem.txt)...
dotnet bin2\GiaoNhanFuzz.dll danhmuc >> "%OUT%" 2>&1
set RC_DM=%errorlevel%
echo [2/2] Chay 8 kich ban + %PO% PO x %OPS% thao tac (song song theo so nhan may)...
if "%TREO_PHUT%"=="" set "TREO_PHUT=20"
dotnet bin2\GiaoNhanFuzz.dll nang %PO% %OPS% %SEED% >> "%OUT%" 2>&1
set RC=%errorlevel%
if not "%TONG%"=="1" goto sau_luot2
if not "%RC%"=="0" goto sau_luot2
set /a SEED2=%SEED%+5000
echo Luot 2: nang %PO% %OPS% seed %SEED2%>> "%OUT%"
dotnet bin2\GiaoNhanFuzz.dll nang %PO% %OPS% %SEED2% >> "%OUT%" 2>&1
set RC=%errorlevel%
:sau_luot2
if not "%RC_DM%"=="0" if "%RC%"=="0" set RC=%RC_DM%
findstr /c:"  TREO  " "%OUT%" >nul && set RC=TREO

echo.>> "%OUT%"
if "%RC%"=="TREO" ( echo KET QUA CUOI: CO TREO - luot qua %TREO_PHUT% phut da tu tat, xem claude_trangthai.txt>> "%OUT%" & echo CO TREO & goto het )
if "%RC%"=="0" ( echo KET QUA CUOI: DAT HET>> "%OUT%" & echo DAT HET ) else ( echo KET QUA CUOI: CO SAI - gui file cho Claude>> "%OUT%" & echo CO SAI )
:het
echo File ket qua: %OUT%
endlocal
