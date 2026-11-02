# [ML41-NC-RESET][RA-CASCADE][WTS-VOID-PO 01/11/2026] Trang WTS thật: đổi ô gốc (NC / máy / PO / mã CV / nhóm nhỏ) thì ô phụ thuộc
#   (giờ thực tế, T.Thực tế/pc) không giữ số của lựa chọn cũ; Sửa dòng dùng bản sao (bảng dòng đã lưu giữ đúng số DB).
# Chạy: python3 drive_ml41.py <port> <old|new>   (harness dựng bằng mk.sh, DB giả mới tinh)
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info is not None and info != "" else ""))

async def login(ctx, user, pw):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg
def grp(pg, i=0): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
def subrow(pg, i=0): return grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)
async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
async def set_po(row, po):
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await row.page.wait_for_timeout(800)
async def num(row, i): return (await row.locator("input[type=number]").nth(i).input_value()).strip()
async def set_num(row, i, v): await row.locator("input[type=number]").nth(i).fill(str(v)); await row.page.wait_for_timeout(250)
async def times(row): return [await num(row, i) for i in (2, 3, 4)]
def f(v):
    try: return float(v)
    except: return None
async def use_std(pg):
    cb = pg.locator("input[id^='grpStd_']").first
    return await cb.is_checked() if await cb.count() else None
async def set_use_std(pg, on):
    cb = pg.locator("input[id^='grpStd_']").first
    if await cb.is_checked() != on: await cb.click(); await pg.wait_for_timeout(500)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        pg = await login(ctx, "admin", "admin123")
        await ensure_group(pg); row = subrow(pg)
        await set_use_std(pg, True)
        # 7) lưu 1 dòng PO1 NC1 M1 OK 3, rồi Sửa dòng: form giữ giờ đã lưu; đổi NC trong form không đổi bảng dòng đã lưu
        row = subrow(pg)
        await set_po(row, "PO1")
        await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        await row.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(400)
        await set_num(row, 0, 3)
        tt = row.locator("input[placeholder='HH:mm']")
        await tt.nth(0).fill("13:00"); await tt.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
        await tt.nth(1).fill("13:30"); await tt.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
        for i, v in zip((2, 3, 4), (5, 2, 1)): await set_num(row, i, v)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        d = dump(); w = [x for x in d["wts"] if not x.get("isVoided") and x.get("nc") == "NC1" and x.get("qtyOk") == 3]
        ok(len(w) == 1, "Lưu PO1 NC1 OK 3", len(w))
        saved = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']")).filter(has_text="13:00").first
        await remove_groups(pg); await ensure_group(pg); row = subrow(pg)
        # 1) PO1 NC1 M1 → chuẩn 5 / 2 / 1
        await set_po(row, "PO1")
        await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        await row.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(500)
        t = await times(row); ok([f(x) for x in t] == [5, 2, 1], "PO1 NC1 M1 (Tham khảo chuẩn bật): giờ thực tế = chuẩn 5 / 2 / 1", t)
        # 2) đổi NC → NC2: phải xoá (bản cũ giữ 5 / 2 / 1 của NC1)
        await row.locator("select").nth(0).select_option("NC2"); await pg.wait_for_timeout(500)
        t = await times(row); ok(all(x == "" for x in t), "[ML41] đổi NC1 → NC2: giờ thực tế NC1 bị xoá (máy về trống)", t)
        # [ML41-SELECT-STALE 02/11] ô Máy trên MÀN HÌNH phải về "--" (trước: phần mềm bỏ máy nhưng ô vẫn hiện máy cùng vị trí của NC mới)
        msel = await row.locator("select").nth(1).input_value()
        ok(msel == "", "[ML41-SELECT-STALE] đổi NC1 → NC2: ô Máy trên màn hình về '--' (không hiện máy của NC mới khi chưa chọn)", repr(msel))
        await pg.screenshot(path=f"ml41_{OUT}_nc.png", full_page=False)
        # 3) chọn M2 → chuẩn NC2 M2 = 4 / 3 / 1
        await row.locator("select").nth(1).select_option("M2"); await pg.wait_for_timeout(500)
        t = await times(row); ok([f(x) for x in t] == [4, 3, 1], "Chọn máy M2: điền chuẩn NC2 M2 = 4 / 3 / 1", t)
        # 4) tắt Tham khảo chuẩn, gõ tay 9/9/9 trên NC1 M1 rồi đổi máy M3 → xoá (bản cũ giữ 9 / 9 / 9 của M1)
        await set_use_std(pg, False)
        await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        await row.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(400)
        for i in (2, 3, 4): await set_num(row, i, 9)
        await row.locator("select").nth(1).select_option("M3"); await pg.wait_for_timeout(500)
        t = await times(row); ok(all(x == "" for x in t), "[ML41] tắt chuẩn, đổi máy M1 → M3: giờ gõ cho M1 bị xoá", t)
        # 5) bật lại chuẩn: M3 (máy đồng dạng) → 6 / 2.5 / 1
        await set_use_std(pg, True)
        t = await times(row); ok([f(x) for x in t] == [6, 2.5, 1], "Bật lại chuẩn trên M3: 6 / 2.5 / 1", t)
        # 6) đổi PO → PO2 (cùng Part): giờ thực tế của PO1 xoá rồi điền chuẩn PO2
        await set_po(row, "PO2"); await pg.wait_for_timeout(500)
        t = await times(row); ok([f(x) for x in t] == [6, 2.5, 1], "[RA-CASCADE] đổi PO1 → PO2: điền lại chuẩn theo snapshot PO2", t)
        await remove_groups(pg); await pg.wait_for_timeout(400)
        await saved.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        row = subrow(pg)
        t = await times(row); ok([f(x) for x in t] == [5, 2, 1], "Mở Sửa dòng: form giữ giờ đã lưu 5 / 2 / 1 (không xoá khi mở)", t)
        await saved.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(800)
        ok(await pg.locator("input[placeholder='Gõ PO...']").count() == 1, "[WTS-VOID-PO] bấm Sửa lần 2 cùng dòng: không mở thêm nhóm", await pg.locator("input[placeholder='Gõ PO...']").count())
        await set_use_std(pg, False)
        await row.locator("select").nth(1).select_option("M3"); await pg.wait_for_timeout(500)
        txt = await saved.inner_text()
        ok("M1" in txt.split() and "M3" not in txt.split(), "[WTS-VOID-PO] đổi máy trong form Sửa: bảng dòng đã lưu vẫn hiện M1 (số DB)", txt.replace("\n", " ")[:160])
        t = await times(row); ok(all(x == "" for x in t), "[ML41] Sửa dòng đổi máy (tắt chuẩn): giờ thực tế máy cũ bị xoá", t)
        await set_num(row, 0, 7); await pg.wait_for_timeout(300)
        await remove_groups(pg); await pg.wait_for_timeout(400)
        txt = await saved.inner_text()
        ok("M1" in txt.split() and "M3" not in txt.split(), "Bỏ nhóm sửa (X): dòng đã lưu không mang số đang gõ (máy / SL)", txt.replace("\n", " ")[:160])
        await set_use_std(pg, True) if await pg.locator("input[id^='grpStd_']").count() else None
        await pg.screenshot(path=f"ml41_{OUT}_edit.png", full_page=False)
        await b.close()

        # 8) HTSP: Taro T1 chuẩn 1.5 → đổi mã 1T / đổi nhóm nhỏ
        b = await p.chromium.launch(); ctx = await b.new_context()
        pg = await login(ctx, "htsp01", "123456")
        await ensure_group(pg); await set_use_std(pg, True); row = subrow(pg)
        await set_po(row, "PO1")
        sel = pg.locator("select").filter(has=pg.locator("option[value='Taro']")).first
        await sel.select_option("Taro"); await pg.wait_for_timeout(500)
        code = row.locator("input[list]").filter(has_not=pg.locator("xx")).nth(1)
        inputs = row.locator("input[list]")
        n = await inputs.count()
        cin = None
        for i in range(n):
            ph = await inputs.nth(i).get_attribute("placeholder") or ""
            if "PO" not in ph: cin = inputs.nth(i); break
        await cin.fill("T1"); await cin.dispatch_event("change"); await pg.wait_for_timeout(500)
        async def actual():
            a = row.locator("input[type=number]")
            vals = [ (await a.nth(i).input_value()).strip() for i in range(await a.count()) ]
            return vals
        v1 = await actual()
        ok("1.5" in v1, "HTSP Taro T1: T.Thực tế/pc = chuẩn 1.5", v1)
        await set_use_std(pg, False)
        await cin.fill("1T"); await cin.dispatch_event("change"); await pg.wait_for_timeout(500)
        v2 = await actual()
        ok("1.5" not in v2, "[RA-CASCADE] tắt chuẩn, đổi mã T1 → 1T: T.Thực tế/pc của T1 bị xoá", v2)
        await set_use_std(pg, True); await cin.fill("T1"); await cin.dispatch_event("change"); await pg.wait_for_timeout(500)
        await sel.select_option("Bavia"); await pg.wait_for_timeout(600)
        v3 = await actual()
        ok("1.5" not in v3, "[RA-CASCADE] đổi nhóm nhỏ Taro → Bavia: T.Thực tế/pc của mã Taro bị xoá", v3)
        await pg.screenshot(path=f"ml41_{OUT}_htsp.png", full_page=False)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
