# [RS-E11 02/11/2026] App thật (harness), bản quyền KHÔNG hợp lệ:
#   E1–E6 khoá bản quyền: chỉ ĐÚNG trang /about qua được khoá — "?x=/about", "#/about" không lách được (trước: Uri.Contains("/about")).
#   E7    DetailedErrors chỉ bật khi Development: chạy Production thì lỗi mạch Blazor không lộ chi tiết code cho trình duyệt.
#   E8    cookie đăng nhập HttpOnly (JS không đọc được).
#   E9    sau Caddy (X-Forwarded-Proto: https từ 127.0.0.1) → cookie Secure; gọi thẳng http → không Secure (UseForwardedHeaders).
# Chạy: python3 drive_rse11.py <port> <thư mục bin harness> [nhãn]
#   Tự bật harness (Production) trong mount namespace riêng: thư mục bản quyền /usr/share/MES_Server/License được thay bằng 1 thư mục tạm
#   chứa file dùng thử hỏng → bản quyền Inactive. Thư mục bản quyền chung của máy KHÔNG bị đụng (harness khác vẫn chạy bình thường). Cổng phải trống.
import sys, os, asyncio, tempfile, urllib.request, urllib.parse, http.client, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hk_launch
from playwright.async_api import async_playwright
PORT = sys.argv[1]; BIN = sys.argv[2]; OUT = sys.argv[3] if len(sys.argv) > 3 else "new"
BASE = f"http://127.0.0.1:{PORT}"
LOCK = "HỆ THỐNG ĐÃ BỊ KHÓA"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""))

def login_post(fwd_https):
    c = http.client.HTTPConnection("127.0.0.1", int(PORT), timeout=10)
    body = urllib.parse.urlencode({"username": "admin", "password": "admin123"})
    h = {"Content-Type": "application/x-www-form-urlencoded"}
    if fwd_https: h["X-Forwarded-Proto"] = "https"
    c.request("POST", "/login-submit", body, h); r = c.getresponse()
    sc = [v for k, v in r.getheaders() if k.lower() == "set-cookie" and v.startswith("MES.Auth")]
    c.close(); return r.status, sc

async def blocked(pg, path):
    await pg.goto(BASE + path); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(1200)
    return LOCK in await pg.inner_text("body")

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page()
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        ok("/login" not in pg.url, "E0 đăng nhập được khi bản quyền khoá (trang đăng nhập không bị khoá)", pg.url)
        ok(await blocked(pg, "/handover"), "E1 bản quyền không hợp lệ: /handover hiện khoá")
        ok(await blocked(pg, "/handover?x=/about"), "E2 /handover?x=/about: VẪN khoá (trước: thêm '/about' vào chuỗi truy vấn là qua khoá)")
        ok(await blocked(pg, "/handover#/about"), "E3 /handover#/about: VẪN khoá (chỉ phần neo chứa '/about')")
        ok(not await blocked(pg, "/about"), "E4 /about: không khoá (trang kích hoạt bản quyền)")
        ok(not await blocked(pg, "/About/"), "E5 /About/ (hoa thường, dấu / cuối): không khoá")
        ok(await blocked(pg, "/kh-plan?returnUrl=/about"), "E6 /kh-plan?returnUrl=/about: VẪN khoá")
        # E7 DetailedErrors: gọi .NET từ JS một hàm không có → lời lỗi trả về trình duyệt
        await pg.goto(BASE + "/about"); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(1500)
        msg = await pg.evaluate("""async () => { try { await DotNet.invokeMethodAsync('MES.Web', 'KhongCoHamNay'); return 'KHONG LOI'; }
                                                 catch (e) { return String(e && e.message || e); } }""")
        ok("DetailedErrors" in msg and "KhongCoHamNay" not in msg.replace("'KhongCoHamNay'", "") and "JSInvokable" not in msg,
           "E7 Production: lỗi gọi .NET chỉ báo chung 'bật DetailedErrors để xem', không lộ chi tiết code", msg)
        # E8 cookie HttpOnly
        ck = [c for c in await ctx.cookies() if c["name"] == "MES.Auth"]
        js = await pg.evaluate("() => document.cookie")
        ok(len(ck) == 1 and ck[0]["httpOnly"] and "MES.Auth" not in js, "E8 cookie MES.Auth HttpOnly, JS không đọc được", f"{ck and ck[0].get('httpOnly')} · document.cookie='{js}'")
        await b.close()
    # E9 Secure theo X-Forwarded-Proto (proxy cùng máy)
    st1, sc1 = login_post(True); st2, sc2 = login_post(False)
    ok(st1 == 302 and sc1 and "secure" in sc1[0].lower(), "E9 sau Caddy (X-Forwarded-Proto: https từ 127.0.0.1): cookie đăng nhập có Secure", f"{st1} {sc1}")
    ok(st2 == 302 and sc2 and "secure" not in sc2[0].lower() and "httponly" in sc2[0].lower(), "E9 gọi thẳng http: cookie không Secure (vẫn HttpOnly)", f"{st2} {sc2}")

def lic_snapshot():
    d = "/usr/share/MES_Server/License"
    if not os.path.isdir(d): return None
    return sorted((f, hashlib.md5(open(os.path.join(d, f), "rb").read()).hexdigest()) for f in os.listdir(d) if f != "mes_log.txt")

before = lic_snapshot()
lic = tempfile.mkdtemp(prefix="rse11_lic_")
open(os.path.join(lic, "mes_init.dat"), "w").write("khong-giai-ma-duoc")   # file dùng thử hỏng → không reset dùng thử → Inactive
proc = hk_launch.start(BIN, PORT, env={"ASPNETCORE_ENVIRONMENT": "Production"}, lic_dir=lic, log="run_rse11.log")
try: asyncio.run(main())
finally: hk_launch.stop(proc)
ok(lic_snapshot() == before, "E10 thư mục bản quyền chung của máy không bị đổi")
print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
sys.exit(0 if fails == 0 else 1)
