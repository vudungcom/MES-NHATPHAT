# [LAMHO-WTS-DSPO 07/11/2026] Gói 8 — trang WTS: HTSP chỉ có hàng Gia công giao theo NC cho Taro làm hộ (Nhận sản xuất HTSP = 0) vẫn thấy PO trong ô "Gõ PO..."
#   (cổng lọc mặc định "đã nhận hàng" — HandoverLedgerService.HasWtsInput). Trước: phải bật "Hiện phiếu chưa nhận hàng" (thử live 07/11 PO TEST-0711-TARO).
#   Dữ liệu: POST /harness/g6-seed (HarnessSua.SeedG6Async — PO-LAMHO: HTSP nhận 10 hàng giao theo NC từ NC02 cho Taro NC 3, chưa nhận Hàng mới nào).
#   P1 htsp01 (nhóm HTSP), tắt "Hiện phiếu chưa nhận hàng": gõ "PO-LAMHO" → danh sách gợi ý có PO-LAMHO. Bản becb81e: P1 SAI (danh sách rỗng).
#   P2 cùng PO, nhóm nhỏ Taro, mã 2T, NC 3: dòng hiện trần "… / nhận 10" (hàng làm hộ vào 10), không khoá "chưa nhận hàng".
#   P3 gc01 (nhóm Gia công): PO-LAMHO vẫn hiện (Gia công nhận Hàng mới — không đổi).
#   Nguồn: CLAUDE (thử live 07/11 22:4x); Hùng 07/11 22:38 giao Gói 8.
# Chạy trên harness VỪA khởi động: python3 drive_g8.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))

async def login(ctx, user, pw):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(600)
    return pg

async def po_options(pg, text):
    row = pg.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
    inp = row.locator("input[placeholder='Gõ PO...']")
    await inp.fill(text); await pg.wait_for_timeout(1200)
    return await pg.evaluate("""() => { const i = document.querySelector("input[placeholder='Gõ PO...']"); const d = i && i.list;
                                        return d ? [...d.options].map(o => o.value) : []; }""")

async def main():
    print("seed", post("/harness/g6-seed"))
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        print("[P1] HTSP chỉ có hàng làm hộ: PO hiện trong ô Gõ PO (không bật Hiện phiếu chưa nhận hàng)")
        pg = await login(ctx, "htsp01", "123456")
        tog = pg.locator("input[type=checkbox]").nth(1)
        ok(not await tog.is_checked(), "P1a ô 'Hiện phiếu chưa nhận hàng' đang tắt (mặc định)")
        opts = await po_options(pg, "PO-LAMHO")
        ok("PO-LAMHO" in opts, "P1b htsp01 gõ PO-LAMHO → danh sách có PO-LAMHO (trước: rỗng, phải bật Hiện phiếu chưa nhận hàng)", opts)
        print("[P2] Taro 2T NC 3: trần theo hàng làm hộ")
        row = pg.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
        await row.locator("input[placeholder='Gõ PO...']").press("Tab"); await pg.wait_for_timeout(1500)
        await row.locator("select").first.select_option("Taro"); await pg.wait_for_timeout(800)
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("2T"); await cv.dispatch_event("change"); await pg.wait_for_timeout(900)
        nc = row.locator("select[title*='nhiều NC Taro']")
        if await nc.count(): await nc.select_option("3"); await pg.wait_for_timeout(900)
        txt = (await row.inner_text()).replace("\n", " ")
        ok("/ nhận 10" in txt and "chưa nhận hàng" not in txt, "P2 dòng 2T NC 3: trần hiện '… / nhận 10' (hàng làm hộ vào 10), không khoá chưa nhận hàng", txt[:400])
        await pg.close()
        print("[P3] Gia công vẫn thấy PO")
        pg = await login(ctx, "gc01", "123456")
        opts = await po_options(pg, "PO-LAMHO")
        ok("PO-LAMHO" in opts, "P3 gc01 gõ PO-LAMHO → danh sách có PO-LAMHO", opts)
        await b.close()
    print(f"\nSAI {fails}")
    sys.exit(1 if fails else 0)

asyncio.run(main())
