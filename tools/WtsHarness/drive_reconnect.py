# [BLZ-RECONNECT 06/11/2026] Tab mất kết nối máy chủ: dải báo nối lại, nối lại ĐÚNG phiên khi máy chủ chỉ chập chờn,
#   tự tải lại trang khi máy chủ đã khởi động lại (phiên cũ không còn).
#   Nguồn: CLAUDE 31/10 (BLZ-RECONNECT), Hùng 05/11 15:37 cho làm nhóm A.
# Chạy: python3 drive_reconnect.py <port> <thư mục bin harness> [nhãn] [LONG=1 trong môi trường: thêm R4 dừng 200 giây]
#   Tự bật / tắt harness (cổng phải trống). 06/11: bản mới ĐẠT hết (+ R4 với LONG=1); bản HEAD 2775213: R1 SAI (dải mặc định tiếng Anh),
#   R2 + R2b SAI (kẹt ở "rejected", phải bấm Reload), R3 ĐẠT.
import sys, os, asyncio, signal, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hk_launch
from playwright.async_api import async_playwright
PORT = sys.argv[1]; BIN = sys.argv[2]; OUT = sys.argv[3] if len(sys.argv) > 3 else "new"
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""), flush=True)

proc = None
async def wait_for(pg, js, sec):
    t = time.time()
    while time.time() - t < sec:
        try:
            if await pg.evaluate(js): return True
        except Exception: pass
        await asyncio.sleep(0.5)
    return False

BOX_CLASS = "(() => { const b = document.getElementById('components-reconnect-modal'); return b ? b.className : '(không có khung)'; })()"
BANNER_TXT = "(() => { const b = document.getElementById('components-reconnect-modal'); if (!b) return ''; return [...b.querySelectorAll('span')].filter(s => getComputedStyle(s).display !== 'none').map(s => s.textContent).join(' ') + (getComputedStyle(b).display === 'none' ? '' : ' [hiện]'); })()"

async def open_page(b):
    ctx = await b.new_context(); pg = await ctx.new_page()
    await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/part-master"); await pg.wait_for_timeout(2500)
    await pg.evaluate("window.__mesMark = 'phien-1'")
    box = pg.locator("input[placeholder^='Tìm theo Part No']")
    await box.fill("ABC-GIU"); await pg.wait_for_timeout(500)
    return ctx, pg, box

async def main():
    global proc
    async with async_playwright() as p:
        b = await p.chromium.launch()
        # ── R3: máy chủ treo 40 giây rồi chạy tiếp → nối lại đúng phiên (ô đang gõ còn, không tải lại)
        ctx, pg, box = await open_page(b)
        os.kill(proc.pid, signal.SIGSTOP)
        shown = await wait_for(pg, "document.getElementById('components-reconnect-modal') && /components-reconnect-show/.test(document.getElementById('components-reconnect-modal').className)", 45)
        txt = await pg.evaluate(BANNER_TXT)
        os.kill(proc.pid, signal.SIGCONT)
        back = await wait_for(pg, "(() => { const b = document.getElementById('components-reconnect-modal'); return b && !/components-reconnect-(show|failed|rejected)/.test(b.className); })()", 60)
        mark = await pg.evaluate("window.__mesMark || ''")
        val = await box.input_value() if await box.count() else "(mất ô)"
        ok(shown and "Mất kết nối máy chủ" in txt, "R1 máy chủ không trả lời: hiện dải \"Mất kết nối máy chủ — đang nối lại…\"", f"{await pg.evaluate(BOX_CLASS)} · {txt}")
        ok(back and mark == "phien-1" and val == "ABC-GIU", "R3 máy chủ chạy lại sau 40 giây: nối lại ĐÚNG phiên (không tải lại, ô đang gõ còn \"ABC-GIU\"), dải báo ẩn", f"back={back} mark='{mark}' ô='{val}' · {await pg.evaluate(BOX_CLASS)}")
        # Blazor còn sống: gõ tiếp vào ô vẫn được máy chủ nhận (không lỗi, không treo)
        await box.fill("ABC-GIU-2"); await pg.wait_for_timeout(800)
        ok(await box.input_value() == "ABC-GIU-2" and await pg.evaluate("window.__mesMark || ''") == "phien-1", "R3b sau khi nối lại trang vẫn dùng được")
        await ctx.close()

        if os.environ.get("LONG") == "1":
            # ── R4: treo 200 giây (quá 3 phút mặc định cũ) → vẫn nối lại đúng phiên (giữ phiên 30 phút)
            ctx, pg, box = await open_page(b)
            os.kill(proc.pid, signal.SIGSTOP); await asyncio.sleep(200); os.kill(proc.pid, signal.SIGCONT)
            back = await wait_for(pg, "(() => { const b = document.getElementById('components-reconnect-modal'); return b && !/components-reconnect-(show|failed|rejected)/.test(b.className); })()", 90)
            mark = await pg.evaluate("window.__mesMark || ''"); val = await box.input_value() if await box.count() else "(mất ô)"
            ok(back and mark == "phien-1" and val == "ABC-GIU", "R4 máy chủ treo 200 giây (quá 3 phút cũ): vẫn nối lại đúng phiên, ô đang gõ còn", f"back={back} mark='{mark}' ô='{val}' · {await pg.evaluate(BOX_CLASS)}")
            await ctx.close()

        # ── R2: máy chủ khởi động lại (phiên cũ mất hẳn) → trang tự tải lại, không kẹt ở "rejected"
        ctx, pg, box = await open_page(b)
        hk_launch.stop(proc)
        down = await wait_for(pg, "document.getElementById('components-reconnect-modal') && /components-reconnect-show/.test(document.getElementById('components-reconnect-modal').className)", 45)
        proc = hk_launch.start(BIN, PORT, log="run_reconnect2.log")
        reloaded = await wait_for(pg, "!window.__mesMark", 90)
        await pg.wait_for_timeout(2500)
        url = pg.url
        ok(down and reloaded, "R2 máy chủ khởi động lại: trang TỰ tải lại (không phải bấm Reload)", f"down={down} reloaded={reloaded} url={url} · {await pg.evaluate(BOX_CLASS) if not reloaded else ''}")
        ok(reloaded and ("/login" in url or "/part-master" in url), "R2b sau khi tải lại về trang đăng nhập / trang đang mở", url)
        await pg.screenshot(path=f"reconnect_{OUT}.png")
        await ctx.close()
        await b.close()

proc = hk_launch.start(BIN, PORT, log="run_reconnect.log")
try: asyncio.run(main())
finally:
    try: os.kill(proc.pid, signal.SIGCONT)
    except Exception: pass
    hk_launch.stop(proc)
print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
