@echo off
rem [G32B 2026-11-23] Goi 32b: Cai phan mem MES (thu muc cha cua SaoLuuMau) thanh Windows Service. Chuot phai -> Run as administrator. Go: cai-dich-vu.bat -Go
rem   Xem HUONG-DAN-KHOI-PHUC.txt cung thu muc. Nguon: HUNG 23/11 13:20 OK Goi 32b.
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0cai-dich-vu.ps1" %*
set MA=%errorlevel%
echo.
echo Ma thoat: %MA%   (0 = dat)
pause
exit /b %MA%
