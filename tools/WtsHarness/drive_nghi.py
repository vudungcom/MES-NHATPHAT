# [ML17-XN][CA-NGHI-HIST 01/11/2026] Trang WTS thật: ô "Làm thông nghỉ" (nhóm GC + Việc không ra SP), phút làm cột Phút / Tổng hôm nay,
#   Sửa dòng mở lại đúng phần làm thông, báo cáo theo phút làm. Ca 1 (HarnessSeed) 08:00–17:00, nghỉ trưa 12:00–13:00.
# Chạy: python3 drive_nghi.py <port> new   (chỉ bản mới — bản cũ không có tính năng; dòng không chạm nghỉ so cũ / mới bằng drive.py)
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if (not cond and info != "") else ""))

async def login(ctx, user, pw):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg
def grp(pg, i=0): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
def subrow(pg, i=0): return grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
async def set_po(row, po):
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await row.page.wait_for_timeout(800)
async def set_num(row, i, v): await row.locator("input[type=number]").nth(i).fill(str(v)); await row.page.wait_for_timeout(250)
async def set_time(row, a, b):
    tt = row.locator("input[placeholder='HH:mm']")
    await tt.nth(0).fill(a); await tt.nth(0).dispatch_event("change"); await row.page.wait_for_timeout(300)
    await tt.nth(1).fill(b); await tt.nth(1).dispatch_event("change"); await row.page.wait_for_timeout(400)
async def gc(pg, machine, ok_, a, b):
    await ensure_group(pg); row = subrow(pg)
    await set_po(row, "PO1")
    await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
    await row.locator("select").nth(1).select_option(machine); await pg.wait_for_timeout(400)
    await set_num(row, 0, ok_)
    await set_time(row, a, b)
    for i, v in zip((2, 3, 4), (5, 2, 1)): await set_num(row, i, v)
    return row
async def save(pg):
    await pg.click("button.btn-success:has-text('Lưu')"); await pg.wait_for_timeout(2500)
    return [t.strip() for t in await pg.locator(".alert-danger").all_inner_texts() if t.strip()]
def chk(pg): return pg.locator("label:has-text('Làm thông nghỉ') input[type=checkbox]").first
def brk_input(pg, label="12:00"): return pg.locator("span", has_text=label).locator("input[type=number]").first
def saved(pg, *texts):
    loc = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']"))
    for t in texts: loc = loc.filter(has_text=t)
    return loc.first
def brk_of(d, wts): return [x for x in d["brk"] if x["wts"] == wts]
def wts_by(d, start, voided=False): return [w for w in d["wts"] if w["start"].endswith(start) and w["isVoided"] == voided]

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        pg = await login(ctx, "admin", "admin123")
        print("[D1] GC 11:00–13:30 chạm trưa, KHÔNG tích → dòng nghỉ chồng 60 làm 0; cột Phút 90 /150")
        await gc(pg, "M1", 2, "11:00", "13:30")
        ok(await chk(pg).count() == 1 and not await chk(pg).is_checked(), "ô Làm thông nghỉ hiện, chưa tích")
        hint = await pg.locator("span:has-text('đang tính là nghỉ')").count()
        ok(hint >= 1, "chữ \"Nhóm chạm giờ nghỉ … đang tính là nghỉ\"")
        e = await save(pg); d = dump()
        w = wts_by(d, "11:00"); bw = brk_of(d, w[0]["wtsLogId"]) if w else []
        ok(not e and len(w) == 1 and len(bw) == 1 and bw[0]["ov"] == 60 and bw[0]["wk"] == 0 and bw[0]["bs"].endswith("12:00") and bw[0]["ca"] == 1, "DB: 1 dòng nghỉ 12:00–13:00 chồng 60 làm 0", (e, bw))
        cell = (await saved(pg, "11:00").inner_text())
        ok("90" in cell and "/150" in cell, "cột Phút: 90 /150", cell)

        print("[D2] GC máy M3 08:30–12:30 tích → mặc định 30 (đủ phần chồng); sửa 20 → lưu làm 20; cột Phút 230 ☕20")
        await gc(pg, "M3", 1, "08:30", "12:30")
        await chk(pg).check(); await pg.wait_for_timeout(500)
        v = await brk_input(pg).input_value()
        ok(v == "30", "mặc định = đủ phần chồng 30", v)
        await brk_input(pg).fill("20"); await brk_input(pg).dispatch_event("change"); await pg.wait_for_timeout(400)
        await pg.screenshot(path=f"{OUT}_nghi_d2.png", full_page=True)
        e = await save(pg); d = dump()
        w2 = wts_by(d, "08:30"); bw2 = brk_of(d, w2[0]["wtsLogId"]) if w2 else []
        ok(not e and len(bw2) == 1 and bw2[0]["ov"] == 30 and bw2[0]["wk"] == 20, "DB: chồng 30 làm 20", (e, bw2))
        cell = (await saved(pg, "08:30").inner_text())
        ok("230" in cell and "/240" in cell and "☕20" in cell, "cột Phút 230 /240 ☕20", cell)

        print("[D3] Sửa dòng D1: form mở chưa tích; tích → 60; lưu → dòng mới làm 60, dòng cũ (huỷ) GIỮ dòng nghỉ làm 0")
        r1 = saved(pg, "11:00")
        await r1.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        ok(await chk(pg).count() == 1 and not await chk(pg).is_checked(), "Sửa dòng: ô làm thông chưa tích (đúng dòng đã lưu)")
        await chk(pg).check(); await pg.wait_for_timeout(500)
        e = await save(pg); d = dump()
        wn = wts_by(d, "11:00"); wo = wts_by(d, "11:00", True)
        ok(not e and len(wn) == 1 and len(wo) == 1 and brk_of(d, wn[0]["wtsLogId"])[0]["wk"] == 60 and brk_of(d, wo[0]["wtsLogId"])[0]["wk"] == 0,
           "DB: dòng mới làm 60, dòng đã huỷ giữ làm 0", (e, d["brk"]))
        print("[D3b] Sửa lại dòng D2: form mở ĐÃ tích, ô = 20")
        r2 = saved(pg, "08:30")
        await r2.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        ok(await chk(pg).is_checked() and await brk_input(pg).input_value() == "20", "Sửa dòng: mở lại đúng tích + 20 phút")
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(400)

        print("[D4] Tổng hôm nay: 08:30–13:30 gộp: thô 300; nghỉ 12:00–13:00 có khúc thông M1 60 (dòng 11:00) → làm 300")
        tot = await pg.locator("span.fw-bold[title^='Giờ thô gộp']").first.inner_text()
        tip = await pg.locator("span.fw-bold[title^='Giờ thô gộp']").first.get_attribute("title")
        ok(tot.strip().startswith("300"), "Tổng hôm nay = 300", (tot, tip))

        print("[D5] Việc không ra SP 13:30–14:00 không chạm nghỉ → không ô; 11:50–12:10 không lưu được (trùng giờ WTS) — kiểm ô hiện + phút")
        await pg.click("button:has-text('Việc không ra sản phẩm')"); await pg.wait_for_timeout(600)
        form = pg.locator("#wts-act-form")
        await form.locator("input[type=time]").nth(0).fill("11:50"); await form.locator("input[type=time]").nth(0).dispatch_event("change"); await pg.wait_for_timeout(300)
        await form.locator("input[type=time]").nth(1).fill("12:10"); await form.locator("input[type=time]").nth(1).dispatch_event("change"); await pg.wait_for_timeout(400)
        ok(await form.locator("label:has-text('Làm thông nghỉ') input").count() == 1, "form việc: ô Làm thông nghỉ hiện khi chạm trưa")
        await form.locator("label:has-text('Làm thông nghỉ') input").check(); await pg.wait_for_timeout(400)
        txt = await form.inner_text()
        ok("20 phút" in txt and "10" in (await form.locator("span", has_text="12:00").locator("input[type=number]").first.input_value() or ""), "form việc: mặc định 10 (đủ chồng), thời gian 20 phút", txt[-300:])
        await form.locator("button:has-text('Hủy')").click(); await pg.wait_for_timeout(300)

        print("[D6] Báo cáo: Tổng phút làm = phút làm, bảng Làm thông nghỉ")
        await pg.click("button:has-text('WTS Report')"); await pg.wait_for_timeout(1500)
        await pg.click("button:has-text('Xem báo cáo')"); await pg.wait_for_timeout(2500)
        body = await pg.locator("body").inner_text()
        await pg.screenshot(path=f"{OUT}_nghi_d6.png", full_page=True)
        ok("Tổng phút làm" in body and "Làm thông nghỉ (phút)" in body, "báo cáo có thẻ Tổng phút làm + bảng Làm thông nghỉ", body[:300])
        await pg.close()

        print("[D7] HTSP nhóm 2 dòng 11:30–13:30 qua trưa, tích làm thông 40 → chia khúc 12:00–12:40 cho dòng con theo thứ tự thời gian")
        pg = await login(ctx, "htsp01", "123456")
        await ensure_group(pg); g = grp(pg)
        await g.locator("select").nth(0).select_option("Taro"); await pg.wait_for_timeout(500)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO3")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("T1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 10)
        await row.locator("input[type=number][step='0.1']").fill("3"); await pg.wait_for_timeout(300)
        await set_time(row, "11:30", "12:30")
        await pg.locator("button:has-text('Thêm số KH')").click(); await pg.wait_for_timeout(500)
        row2 = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(1)
        await set_po(row2, "PO3")
        cv2 = row2.locator("input[placeholder='Mã CV...']"); await cv2.fill("1T"); await cv2.dispatch_event("change"); await pg.wait_for_timeout(500)
        await row2.locator("input[type=number][step='0.1']").fill("30"); await pg.wait_for_timeout(300)
        t2 = row2.locator("input[placeholder='HH:mm']").last; await t2.fill("13:30"); await t2.dispatch_event("change"); await pg.wait_for_timeout(500)
        await chk(pg).check(); await pg.wait_for_timeout(500)
        await brk_input(pg).fill("40"); await brk_input(pg).dispatch_event("change"); await pg.wait_for_timeout(500)
        await pg.screenshot(path=f"{OUT}_nghi_d7.png", full_page=True)
        e = await save(pg); d = dump()
        hs = sorted([w for w in d["wts"] if w["processGroup"] == "TARO" and not w["isVoided"] and w["start"] >= d["wts"][0]["start"][:10] + " 11:30"], key=lambda w: w["start"])
        bb = [brk_of(d, w["wtsLogId"]) for w in hs]
        sum_wk = sum(x["wk"] for l in bb for x in l); sum_ov = sum(x["ov"] for l in bb for x in l)
        # phần làm cả nhóm = 120 − nghỉ hiệu lực 12:40–13:00 (20) = 100; dòng 1 khối lượng OK10 × 3 = 30, dòng 2 (1T) 30 → mỗi dòng 50 phút làm
        # → dòng 1 11:30–12:20 (chồng 20, thông 20 = khúc 12:00–12:20), dòng 2 12:20–13:30 (chồng 40, thông 20 = khúc 12:20–12:40)
        ok(not e and len(hs) == 2 and sum_wk == 40 and sum_ov == 60 and all(x["wk"] <= x["ov"] for l in bb for x in l)
           and hs[0]["end"].endswith("12:20") and bb[0][0]["wk"] == 20 and bb[0][0]["ov"] == 20 and bb[1][0]["wk"] == 20 and bb[1][0]["ov"] == 40,
           "DB: 2 dòng con 11:30–12:20 / 12:20–13:30, khúc làm thông chia 20 + 20 = 40, chồng 20 + 40 = 60 (giờ chia theo nghỉ hiệu lực)", (e, [(w["start"], w["end"]) for w in hs], bb))
        await b.close()
    print(f"KẾT QUẢ drive_nghi: {'ĐẠT hết' if fails == 0 else f'SAI {fails}'}")

asyncio.run(main())
