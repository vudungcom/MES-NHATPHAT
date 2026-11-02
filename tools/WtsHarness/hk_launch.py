# [WTS-OK-FALLBACK][RS-E11 02/11/2026] Khởi động / tắt harness cho kịch bản cần điều kiện khởi động riêng (biến môi trường,
#   thư mục bản quyền riêng). Giết theo pid của chính tiến trình mình bật — không pkill theo chuỗi.
import os, subprocess, time, urllib.request, socket

def port_busy(port):
    s = socket.socket(); s.settimeout(0.5)
    try: return s.connect_ex(("127.0.0.1", int(port))) == 0
    finally: s.close()

def start(bindir, port, env=None, lic_dir=None, log="run_hk.log"):
    """Bật `dotnet MES.Web.dll` trong bindir (harness mk.sh, cổng đã ghi trong appsettings.json).
    lic_dir: chạy trong mount namespace riêng (unshare -m, propagation private), bind thư mục này lên
    /usr/share/MES_Server/License → bản quyền của harness KHÔNG đụng thư mục bản quyền chung của máy (harness khác vẫn như cũ)."""
    if port_busy(port): raise SystemExit(f"Cổng {port} đang có tiến trình khác — tắt nó (theo pid) trước khi chạy.")
    e = dict(os.environ); e.update(env or {}); e.setdefault("ASPNETCORE_ENVIRONMENT", "Production")
    logf = open(os.path.join(bindir, "..", log), "w")
    if lic_dir:
        cmd = ["unshare", "-m", "--propagation", "private", "sh", "-c",
               f"mkdir -p /usr/share/MES_Server/License && mount --bind '{lic_dir}' /usr/share/MES_Server/License && exec dotnet MES.Web.dll"]
    else:
        cmd = ["dotnet", "MES.Web.dll"]
    p = subprocess.Popen(cmd, cwd=bindir, env=e, stdout=logf, stderr=subprocess.STDOUT)
    for _ in range(240):
        if p.poll() is not None: raise SystemExit(f"Harness thoát sớm (mã {p.returncode}) — xem {log}")
        try:
            urllib.request.urlopen(f"http://127.0.0.1:{port}/login", timeout=2); return p
        except Exception: time.sleep(0.5)
    stop(p); raise SystemExit("Harness không lên sau 120 giây")

def stop(p):
    if p and p.poll() is None:
        p.terminate()
        try: p.wait(15)
        except subprocess.TimeoutExpired: p.kill(); p.wait(5)
