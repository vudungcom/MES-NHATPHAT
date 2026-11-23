#!/bin/bash
# [G31-DON-BO-KIEM 23/11/2026] rundrv.sh <thư mục harness> <cổng> <driver.py> <nhãn> — khởi động harness MỚI TINH (DB giả mới), chạy 1 driver, tắt.
#   Đặt sẵn HARNESS_BIN (drive_p0311 S6 / drive_g24 cần — thiếu thì SAI giả, trước ghi nhầm "SAI cả bản cũ"), PLAYWRIGHT_BROWSERS_PATH, Bootstrap
#   /home/claude/bs_shared (drive_kho / drive_ncw: npm pack bootstrap@5.3.3 bootstrap-icons@1.11.3 — xem đầu drive_ncw.py). Giết theo TÊN tiến trình.
#   Nhãn ngắn "new" / "old" (vài driver ghi file theo nhãn). Chỉ chạy sandbox Linux.
H="$1"; P="$2"; D="$3"; L="$4"; HERE="$(cd "$(dirname "$0")" && pwd)"
case "$(basename "$D")" in   # driver tự bật harness (tham số 2 = thư mục bin2, 3 = nhãn)
  drive_okfb.py|drive_reconnect.py|drive_rse11.py)
    pkill -f "dotnet MES.Web.dll" 2>/dev/null; sleep 1
    cd "$HERE" && PLAYWRIGHT_BROWSERS_PATH=${PLAYWRIGHT_BROWSERS_PATH:-/opt/pw-browsers} timeout 1500 python3 "$D" "$P" "$H/bin2" "$L" 2>&1; RC=$?
    pkill -f "dotnet MES.Web.dll" 2>/dev/null; exit $RC;;
esac
pkill -f "dotnet MES.Web.dll" 2>/dev/null; sleep 1
cd "$H/bin2" && ( ASPNETCORE_ENVIRONMENT=Production setsid dotnet MES.Web.dll > "$H/run.log" 2>&1 & )
for i in $(seq 1 120); do curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:$P/login" 2>/dev/null | grep -q 200 && break; sleep 1; done
cd "$HERE" && PLAYWRIGHT_BROWSERS_PATH=${PLAYWRIGHT_BROWSERS_PATH:-/opt/pw-browsers} HARNESS_BIN="$H/bin2" timeout 1500 python3 "$D" "$P" "$L" "$H/bin2" 2>&1
RC=$?
pkill -f "dotnet MES.Web.dll" 2>/dev/null; sleep 1
exit $RC
