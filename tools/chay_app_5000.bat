@echo off
rem ===========================================================================================
rem  [RUN-5000 2026-11-02] Chay app MES cong 5000 bang "dotnet run" (KHONG dung dotnet watch nua).
rem  - App chay ban build luc bam file nay. Sua code trong thu muc chinh KHONG anh huong khach cho toi khi chay lai file nay.
rem  - Muon khach co ban moi: dong cua so app dang chay (Ctrl+C hoac X) roi nhap dup file nay.
rem  - Buoc 1 kiem bien dich ban Release (bin\Release, khong dung file app dang chay): co loi thi DUNG, khong chay.
rem  - Buoc 2 dotnet run (bin\Debug, giu nguyen MES_LicenseData + appsettings + moi truong nhu truoc).
rem  Dong cua so nay = tat app.
rem ===========================================================================================
setlocal
chcp 65001 >nul
title MES APP 5000 - dotnet run (dong cua so = tat app)
cd /d "%~dp0..\MES.Web"
echo [1/2] Kiem bien dich (Release)...
dotnet build MES.Web.csproj -c Release -v q -nologo
if errorlevel 1 (
  echo.
  echo LOI BIEN DICH - KHONG CHAY APP. Chup man hinh loi gui Claude.
  pause
  exit /b 1
)
echo.
echo [2/2] Chay app cong 5000 (dotnet run). Neu bao file dang bi khoa: app cu chua tat - dong cua so app cu roi chay lai.
dotnet run --project MES.Web.csproj
echo.
echo APP DA DUNG.
pause
