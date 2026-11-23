@echo off
rem [G32B 2026-11-23] Goi 32b: Nang cap phan mem MES dang chay (dich vu) len ban moi = thu muc cha cua SaoLuuMau nay; tu sao luu tron bo truoc khi thay. Chuot phai -> Run as administrator
rem   Xem HUONG-DAN-KHOI-PHUC.txt cung thu muc. Nguon: HUNG 23/11 13:20 OK Goi 32b.
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0nang-cap.ps1" %*
set MA=%errorlevel%
echo.
echo Ma thoat: %MA%   (0 = dat)
pause
exit /b %MA%
