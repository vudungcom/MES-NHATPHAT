# [WTS-SO-CULTURE 06/11/2026] Trang WTS đọc số ô type=number theo InvariantCulture (ô luôn gửi dấu chấm) — không theo culture máy chủ.
#   Trước: decimal.TryParse(string) theo culture máy chủ → máy chủ vi-VN đọc "2.5" ra 25 (dấu chấm là dấu nghìn) → OK 2.5 ghi 25 chiếc,
#   T.GC 2.5 phút ghi 25 phút; B3 (chỉ số nguyên) không bắt được vì 25 là số nguyên. Nguồn: rà độc lập gói 1 06/11 (mục 4, ca P8).
# Chạy: bật harness với LC_ALL=vi_VN.UTF-8 LANG=vi_VN.UTF-8 (máy chủ culture vi-VN) rồi python3 drive_culture.py <port> <nhãn>
#   C1 OK "2.5" → máy chủ chặn "số nguyên", không ghi dòng nào (trước, máy chủ vi-VN: ghi OK 25)
#   C2 OK 3 + T.GC "2.5" → ghi T.GC 2.5 (trước, máy chủ vi-VN: 25)
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if info not in (None, "") else ""))
def live(d): return [w for w in d["wts"] if not w["isVoided"]]

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page()
        await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(1500)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        cul = await pg.evaluate("1")  # chỉ để chắc trang sống
        if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
        g = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO1"); await inp.press("Tab"); await pg.wait_for_timeout(700)
        await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        await row.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(400)
        t = row.locator("input[placeholder='HH:mm']")
        await t.nth(0).fill("08:00"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
        await t.nth(1).fill("09:00"); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
        nums = row.locator("input[type=number]")
        async def setn(i, v): await nums.nth(i).fill(str(v)); await pg.wait_for_timeout(250)
        await setn(0, "2.5"); await setn(2, 5); await setn(3, 2); await setn(4, 1)
        n0 = len(live(dump()))
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = " | ".join(x.strip() for x in await pg.locator(".alert-danger").all_inner_texts())
        body = await pg.inner_text("body")
        d = dump(); new = live(d)[n0:]
        ok(len(new) == 0 and ("số nguyên" in errs or "số nguyên" in body), "C1 OK \"2.5\" → chặn \"số nguyên\", không ghi (trước, máy chủ vi-VN: ghi OK 25)",
           f"ghi={[(w['qtyOk'], w['machiningTime_Actual']) for w in new]} · lỗi={errs[:200]}")
        await setn(0, 3); await setn(3, "2.5")
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = " | ".join(x.strip() for x in await pg.locator(".alert-danger").all_inner_texts())
        d = dump(); new = live(d)[n0:]
        ok(len(new) == 1 and new[0]["qtyOk"] == 3 and new[0]["machiningTime_Actual"] == 2.5, "C2 OK 3 + T.GC \"2.5\" → ghi OK 3, T.GC 2.5 (trước, máy chủ vi-VN: 25)",
           f"ghi={[(w['qtyOk'], w['machiningTime_Actual']) for w in new]} · lỗi={errs[:200]}")
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
