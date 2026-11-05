@echo off
rem [CLAUDE-GIT 2026-11-01] Commit + push goi tich hop. Chi add dung thu muc code, KHONG add file ketqua / bin2 / nang_ketqua.
rem [GIT-SQLDONGTHOI 2026-11-05] them tools/SqlDongThoi (truoc thieu: sua Core.cs / TuKiem khong len git), bo bin / obj.
setlocal
chcp 65001 >nul
cd /d "%~dp0.."
set OUT=%~dp0ketqua_git.txt
echo GIT %date% %time%> "%OUT%"
git add MES.Web tools/GiaoNhanFuzz tools/QuetSo tools/WtsHarness tools/BackfillNghi tools/SqlDongThoi tools/sql tools/kiemtra.bat tools/DanhMucKiem.txt tools/claude_tram.bat tools/claude_day_git.bat tools/chay_app_5000.bat >> "%OUT%" 2>&1
git reset -q -- tools/GiaoNhanFuzz/bin2 tools/GiaoNhanFuzz/nang_ketqua tools/BackfillNghi/bin tools/BackfillNghi/obj tools/SqlDongThoi/bin tools/SqlDongThoi/obj tools/SqlDongThoi/TuKiem/bin tools/SqlDongThoi/TuKiem/obj >> "%OUT%" 2>&1
echo --- se commit:>> "%OUT%"
git diff --cached --stat >> "%OUT%" 2>&1
git commit -q -F tools/claude_commit_msg.txt >> "%OUT%" 2>&1
if errorlevel 1 ( echo COMMIT LOI>> "%OUT%" & echo COMMIT LOI - xem %OUT% & pause & exit /b 1 )
git push >> "%OUT%" 2>&1
if errorlevel 1 ( echo PUSH LOI>> "%OUT%" & echo PUSH LOI - xem %OUT% & pause & exit /b 1 )
git log --oneline -1 >> "%OUT%"
echo KET QUA GIT: XONG>> "%OUT%"
echo XONG - da commit + push
pause
