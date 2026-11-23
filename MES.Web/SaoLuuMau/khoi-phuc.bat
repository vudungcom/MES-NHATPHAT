@echo off
rem [G32B 2026-11-23] Goi 32b: Khoi phuc MES tu bo sao luu chua file nay. Chuot phai -> Run as administrator. Chi xem truoc: khoi-phuc.bat -XemTruoc ; chi kiem bo: khoi-phuc.bat -ChiKiem
rem   Xem HUONG-DAN-KHOI-PHUC.txt cung thu muc. Nguon: HUNG 23/11 13:20 OK Goi 32b.
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0khoi-phuc.ps1" %*
set MA=%errorlevel%
echo.
echo Ma thoat: %MA%   (0 = dat)
pause
exit /b %MA%
