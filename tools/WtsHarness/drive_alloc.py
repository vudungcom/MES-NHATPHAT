# [ALLOC-BREAK][ALLOC-GC 30/10/2026] Mục 23 — chia giờ trong nhóm nhiều dòng khi giờ cắt rơi vào đầu giờ nghỉ trưa (Ca 1 nghỉ 12:00–13:00).
#   HTSP PO3: 2 dòng Taro T1 × 5 (cùng T.Thực tế) · 08:10 → 17:00 = 530 ph − 60 nghỉ = 470 ph → mỗi dòng 235 ph.
#   Đúng: dòng 1 08:10–13:05, dòng 2 13:05–17:00.  Bản cũ: dòng 1 08:10–12:20 (230 ph), dòng 2 13:00–17:00 (240 ph).
#   Chạy: python3 drive_alloc.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]; BASE = f"http://127.0.0.1:{PORT}"
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page()
        await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "htsp01"); await pg.fill("#password", "123456")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(1500)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
        g = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
        await g.locator("select").nth(0).select_option("Taro"); await pg.wait_for_timeout(500)
        async def fill_row(i):
            row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
            inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO3"); await inp.press("Tab"); await pg.wait_for_timeout(700)
            cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("T1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
            n = row.locator("input[type=number]").nth(0); await n.fill("5"); await pg.wait_for_timeout(300)
            return row
        r1 = await fill_row(0)
        t = r1.locator("input[placeholder='HH:mm']"); await t.nth(0).fill("08:10"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(300)
        await pg.locator("button:has-text('Thêm số KH')").click(); await pg.wait_for_timeout(500)
        r2 = await fill_row(1)
        t2 = r2.locator("input[placeholder='HH:mm']"); await t2.last.fill("17:00"); await t2.last.dispatch_event("change"); await pg.wait_for_timeout(500)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
        rows = [w for w in dump()["wts"] if w["khPlanDetailId"] == 3 and w["wtsCode"] == "T1" and not w["isVoided"] and w["qtyOk"] == 5]
        got = [(w["start"][-5:], w["end"][-5:]) for w in rows]
        print(OUT, "lỗi:", errs, "dòng:", got)
        ok = got == [("08:10", "13:05"), ("13:05", "17:00")]
        print(("  ĐẠT  " if ok else "  SAI  ") + "2 dòng mỗi dòng 235 ph: 08:10–13:05 · 13:05–17:00")
        await b.close()
asyncio.run(main())
