@echo off
rem ===========================================================================================
rem  [CLAUDE-TRAM 2026-11-01] Tram chay kiem tra cho Claude. Hung mo 1 LAN, de cua so do (thu nho duoc).
rem  Claude chi ghi file tools\claude_lenh.txt chua DUNG 1 tu khoa trong danh sach duoi.
rem  Tram KHONG chay lenh tu do: noi dung khac danh sach -> bo qua, ghi "KHONG HOP LE".
rem    kiemgap = kiemtra.bat gap   (8 kich ban + 15 PO x 60, ~1 phut)
rem    kiem    = kiemtra.bat       (8 kich ban + 60 PO x 120, ~10 phut)
rem    tong    = kiemtra.bat tong  (KIEM TOAN HE THONG: danh muc chan + 2 luot 120 PO x 160, ~30-40 phut)   [KIEM-TONG 2026-11-02]
rem    danhmuc = chi doi chieu tools\DanhMucKiem.txt voi code (vai giay) -> ketqua_danhmuc.txt        [KIEM-TONG 2026-11-02]
rem    quet    = QuetSo CHI DOC DB trong MES.Web\appsettings.json -> QuetSo\ketqua_quet_*.txt
rem    build   = dotnet build MES.Web cau hinh Release (bin\Release, KHONG dung bin\Debug cua dotnet watch)
rem  Luu y: KHONG nhap chuot vao cua so tram (cmd QuickEdit dung chuong trinh, tieu de hien "Select"); lo nhap thi bam Esc.
rem  KHONG chay / tat app, KHONG dung SQL ghi, KHONG sua file code. Dong cua so = tat tram.
rem ===========================================================================================
setlocal EnableDelayedExpansion
chcp 65001 >nul
title CLAUDE TRAM - de mo, Claude dung de chay kiem tra
cd /d "%~dp0"
echo SAN SANG %date% %time%> claude_trangthai.txt
echo Tram dang cho lenh tu Claude (tools\claude_lenh.txt). Dong cua so de tat.
:vong
cd /d "%~dp0"
if not exist claude_lenh.txt ( timeout /t 4 /nobreak >nul & goto vong )
set "L="
set /p L=<claude_lenh.txt
del /q claude_lenh.txt
echo [%time%] Nhan lenh: !L!
if /i "!L!"=="kiemgap" ( call :bat dau & call kiemtra.bat gap & goto xong )
if /i "!L!"=="kiem"    ( call :bat dau & call kiemtra.bat & goto xong )
if /i "!L!"=="tong"    ( call :bat dau & call kiemtra.bat tong & goto xong )
if /i "!L!"=="danhmuc" ( call :bat dau & pushd GiaoNhanFuzz & dotnet build GiaoNhanFuzz.csproj -c Release -o bin2 -v q -nologo > ..\ketqua_danhmuc.txt 2>&1 & dotnet bin2\GiaoNhanFuzz.dll danhmuc >> ..\ketqua_danhmuc.txt 2>&1 & popd & goto xong )
if /i "!L!"=="quet"    ( call :bat dau & pushd QuetSo & dotnet run -c Release > ketqua_quet_console.txt 2>&1 & popd & goto xong )
if /i "!L!"=="build"   ( call :bat dau & dotnet build ..\MES.Web\MES.Web.csproj -c Release -v q -nologo > ketqua_build_mesweb.txt 2>&1 & goto xong )
echo KHONG HOP LE %date% %time%> claude_trangthai.txt
echo   -^> khong hop le, bo qua
goto vong
:xong
echo XONG %date% %time%>> claude_trangthai.txt
echo [%time%] Xong.
goto vong
:bat
echo DANG CHAY !L! %date% %time%> claude_trangthai.txt
exit /b 0
