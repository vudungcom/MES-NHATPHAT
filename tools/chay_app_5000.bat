@echo off
rem ===========================================================================================
rem  [RUN-5000 2026-11-02] Chay app MES cong 5000 bang "dotnet run" (KHONG dung dotnet watch nua).
rem  - App chay ban build luc bam file nay. Sua code trong thu muc chinh KHONG anh huong khach cho toi khi chay lai file nay.
rem  - Muon khach co ban moi: dong cua so app dang chay (Ctrl+C hoac X) roi nhap dup file nay.
rem  - Buoc 1 kiem bien dich ban Release (bin\Release, khong dung file app dang chay): co loi thi DUNG, khong chay.
rem  - Buoc 2 dotnet run (bin\Debug, giu nguyen MES_LicenseData + appsettings); moi truong = Production [C-01].
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
rem  [C-01 2026-11-15] Chay o moi truong Production: truoc day "dotnet run" nap Properties\launchSettings.json (Development) ->
rem    DetailedErrors bat + khong co UseExceptionHandler -> loi SQL / stack trace hien nguyen cho khach (Program.cs:18, :126).
rem    --no-launch-profile de launchSettings khong de lai bien moi truong; URL 0.0.0.0:5000 da co trong appsettings.json (Kestrel).
rem    Nguon: CLAUDE quet Fable 14/11 (C-01 Cao); Hung 15/11 15:10 OK cau 5 "cho sua va lam goi 22".
set ASPNETCORE_ENVIRONMENT=Production
echo [2/2] Chay app cong 5000 (dotnet run, Production). Neu bao file dang bi khoa: app cu chua tat - dong cua so app cu roi chay lai.
dotnet run --project MES.Web.csproj --no-launch-profile
echo.
echo APP DA DUNG.
pause
