#!/bin/bash
# [G31-DON-BO-KIEM 23/11/2026] mkh.sh <src MES.Web> <thư mục harness> <cổng> — dựng harness (mk.sh) + build bin2 + chép wwwroot (Production phục vụ file
#   tĩnh từ bin2/wwwroot) + tạo wwwroot/uploads/avatars. Trước: mỗi chat tự viết lại (sandbox mới mất /home/claude/scripts). Chỉ chạy sandbox Linux.
set -e
SRC="$1"; DST="$2"; PORT="$3"; H="$(cd "$(dirname "$0")" && pwd)"
bash "$H/mk.sh" "$SRC" "$DST" "$PORT" > "/tmp/mk_$PORT.log" 2>&1
cd "$DST" && dotnet build -c Release -o bin2 > "/tmp/build_$PORT.log" 2>&1 || { tail -30 "/tmp/build_$PORT.log"; exit 1; }
mkdir -p bin2/wwwroot && cp -rL wwwroot/. bin2/wwwroot/ && mkdir -p bin2/wwwroot/uploads/avatars
grep -E "Warn|Error\(s\)" "/tmp/build_$PORT.log" | tail -2
