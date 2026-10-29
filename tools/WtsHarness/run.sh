#!/bin/bash
# run.sh old|new <thư mục harness> — khởi động lại harness (DB giả mới tinh) rồi bấm kịch bản Playwright
# vd:  ./run.sh old /home/claude/harness_old   (port 5100)   ·   ./run.sh new /home/claude/harness_new (port 5101)
V=$1; DIR=$2; PORT=$([ "$V" = old ] && echo 5100 || echo 5101); HERE="$(cd "$(dirname "$0")" && pwd)"
kill $(cat $DIR/pid 2>/dev/null) 2>/dev/null; sleep 1
cd $DIR/bin2 && (nohup dotnet MES.Web.dll > ../run.log 2>&1 & echo $! > ../pid); sleep 7
cd "$HERE" && PLAYWRIGHT_BROWSERS_PATH=${PLAYWRIGHT_BROWSERS_PATH:-/opt/pw-browsers} timeout 1200 python3 drive.py $PORT $V 2>&1 | grep -v "^\s*File\|^\s*\^\|^\s*~\|^\s*\.\.\."
