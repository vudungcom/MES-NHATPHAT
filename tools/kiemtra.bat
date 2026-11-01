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
if not "%~1"=="" set "PO=%~1"
if not "%~2"=="" set "OPS=%~2"
if not "%~3"=="" set "SEED=%~3"
goto chay
:gap
set "PO=15"
set "OPS=60"
:chay

echo KIEM TRA %date% %time%  ^|  nang %PO% PO x %OPS% thao tac, seed %SEED%  ^|  %NUMBER_OF_PROCESSORS% nhan > "%OUT%"
git -C "%~dp0.." log --oneline -1 >> "%OUT%" 2>nul

echo [1/2] Bien dich bo kiem...
dotnet build GiaoNhanFuzz.csproj -c Release -o bin2 -v q -nologo >> "%OUT%" 2>&1
if errorlevel 1 ( echo BIEN DICH LOI - xem %OUT% & echo KET QUA: BIEN DICH LOI>> "%OUT%" & exit /b 2 )

echo [2/2] Chay 8 kich ban + %PO% PO x %OPS% thao tac (song song theo so nhan may)...
if "%TREO_PHUT%"=="" set "TREO_PHUT=20"
dotnet bin2\GiaoNhanFuzz.dll nang %PO% %OPS% %SEED% >> "%OUT%" 2>&1
set RC=%errorlevel%
findstr /c:"  TREO  " "%OUT%" >nul && set RC=TREO

echo.>> "%OUT%"
if "%RC%"=="TREO" ( echo KET QUA CUOI: CO TREO - luot qua %TREO_PHUT% phut da tu tat, xem claude_trangthai.txt>> "%OUT%" & echo CO TREO & goto het )
if "%RC%"=="0" ( echo KET QUA CUOI: DAT HET>> "%OUT%" & echo DAT HET ) else ( echo KET QUA CUOI: CO SAI - gui file cho Claude>> "%OUT%" & echo CO SAI )
:het
echo File ket qua: %OUT%
endlocal
