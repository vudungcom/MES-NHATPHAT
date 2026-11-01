# [ACT-PODUP 01/11/2026] Form "Việc không ra sản phẩm": gợi ý PO = "PO · Part", chọn gợi ý → lưu đúng dòng KH; gõ số PO trơn (1 dòng) vẫn được.
# Chạy: python3 drive_actpo.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]; BASE = f"http://127.0.0.1:{PORT}"; fails = 0
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(c, n, i=""):
    global fails
    if not c: fails += 1
    print(("  ĐẠT  " if c else "  SAI  ") + n + (("\n         → " + str(i)[:300]) if i != "" else ""))
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); pg = await (await b.new_context()).new_page()
        await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(600)
        await pg.click("button:has-text('Việc không ra sản phẩm')"); await pg.wait_for_timeout(800)
        po = pg.locator("input[list='actPoList']")
        await po.fill("PO1"); await pg.wait_for_timeout(400)
        opts = await pg.locator("#actPoList option").evaluate_all("els => els.map(e => e.value)")
        ok(any(v.startswith("PO1 · ") for v in opts), "Gợi ý PO dạng 'PO · Part'", opts[:5])
        await po.fill("PO1 · PART-A"); await po.dispatch_event("change"); await pg.wait_for_timeout(600)
        opts2 = await pg.locator("#actPoList option").evaluate_all("els => els.map(e => e.value)")
        ok(len(opts2) >= 1, "Đã chọn 'PO1 · PART-A': gợi ý không rỗng", opts2[:3])
        err = pg.locator(".text-danger.small")
        t = (await err.first.inner_text()) if await err.count() else ""
        ok("dòng trong Kế hoạch" not in t, "PO1 · PART-A: không báo trùng", t)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
