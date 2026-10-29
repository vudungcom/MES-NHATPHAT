# Harness trang WTS thật: bấm cùng một chuỗi thao tác trên bản cũ / bản mới, ghi lại lời báo + chụp DB sau mỗi bước.
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
log = []   # (buoc, loi nhom, loi dong, void msg, dump)

def dump():
    return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())

async def login(ctx, user, pw):
    pg = await ctx.new_page()
    await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login")
    await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(1500)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg

def grp(pg, i=0):
    # khung nhóm đang nhập = bảng có ô "Gõ PO..."
    return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)

async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)

async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)

async def set_po(row, po):
    inp = row.locator("input[placeholder='Gõ PO...']")
    await inp.fill(po); await inp.press("Tab"); await row.page.wait_for_timeout(700)

async def set_time(row, start, end):
    t = row.locator("input[placeholder='HH:mm']")
    await t.nth(0).fill(start); await t.nth(0).dispatch_event("change"); await row.page.wait_for_timeout(250)
    await t.nth(1).fill(end);   await t.nth(1).dispatch_event("change"); await row.page.wait_for_timeout(250)

async def set_num(row, idx, val):
    n = row.locator("input[type=number]").nth(idx)
    await n.fill(str(val)); await row.page.wait_for_timeout(200)

async def set_ngcat(row, label):
    i = row.locator("input[placeholder='Loại lỗi…']")
    await i.fill(label); await i.dispatch_event("change"); await row.page.wait_for_timeout(300)

async def read_errors(pg):
    errs = [t.strip() for t in await pg.locator(".alert-danger").all_inner_texts()]
    return [e for e in errs if e]

async def save(pg, name, snap=True):
    await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
    errs = await read_errors(pg)
    log.append({"buoc": name, "loi": errs, "dump": dump() if snap else None})
    print(f"[{name}] loi={len(errs)}")
    for e in errs: print("   ", e.replace("\n", " | ")[:300])

async def saved_row(pg, *texts):
    loc = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']"))
    for t in texts: loc = loc.filter(has_text=t)
    return loc

async def void_row(pg, name, *texts):
    r = (await saved_row(pg, *texts)).first
    await r.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(500)
    await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
    msg = ""
    vb = pg.locator(".alert-warning").filter(has_text=re.compile(r"\S"))
    if await vb.count(): msg = (await vb.first.inner_text()).strip()
    if msg: await pg.click("button:has-text('Hủy')"); await pg.wait_for_timeout(300)
    log.append({"buoc": name, "loi": [msg] if msg else [], "dump": dump()})
    print(f"[{name}] void msg={'CHAN' if msg else 'ok'}")
    if msg: print("   ", msg.replace("\n", " | ")[:300])

async def gc_row(pg, row, po, nc, machine, ok=None, ng=None, ngcat=None, start=None, end=None, times=None, trial=None):
    await set_po(row, po)
    if nc: await row.locator("select").nth(0).select_option(nc); await pg.wait_for_timeout(400)
    if machine: await row.locator("select").nth(1).select_option(machine); await pg.wait_for_timeout(400)
    if ok is not None: await set_num(row, 0, ok)
    if ng is not None: await set_num(row, 1, ng)
    if ngcat: await set_ngcat(row, ngcat)
    if start: await set_time(row, start, end)
    if times:   # T.Gá, T.GC, T.Ktra (số thứ 2,3,4)
        for i, v in enumerate(times): await set_num(row, 2 + i, v)
    if trial:   # T.CB, T.Chạy (số thứ 5,6)
        for i, v in enumerate(trial): await set_num(row, 5 + i, v)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        # ── ADMIN = GC ──
        pg = await login(ctx, "admin", "admin123")
        log.append({"buoc": "0 mở trang", "loi": [], "dump": dump()})
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        # S1 GC Hàng loạt PO1 NC1 M1 OK 10 NG 2
        await gc_row(pg, row, "PO1", "NC1", "M1", ok=10, ng=2, ngcat="KT-SAI · Sai kích thước", start="08:00", end="09:00", times=[5, 2, 1])
        await pg.screenshot(path=f"{OUT}_s1.png", full_page=True)
        await save(pg, "S1 GC Hàng loạt PO1 NC1 OK10 NG2")
        # S2 GC Chạy thử PO1 NC1 M1, không SL, T.CB 10 T.Chạy 3
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await pg.locator("button:has-text('Chạy thử')").click(); await pg.wait_for_timeout(400)
        await gc_row(pg, row, "PO1", "NC1", "M1", start="09:05", end="09:20", trial=[10, 3])
        await save(pg, "S2 GC Chạy thử PO1 NC1 không SL")
        # S3 GC NC2 M2 OK 5 (NC1 OK 10 → trần 10)
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC2", "M2", ok=5, start="09:30", end="10:00", times=[4, 3, 1])
        ps = row.locator("input[placeholder='Mã WTS...']"); await ps.fill("PS1"); await ps.dispatch_event("change"); await pg.wait_for_timeout(300)
        await set_num(row, 7, 5)
        await row.locator("input[placeholder='Nội dung PS...']").fill("thử phát sinh"); await pg.wait_for_timeout(300)
        await save(pg, "S3 GC PO1 NC2 OK5 + PS 5 phút")
        # S4 vượt trần: PO1 NC2 OK 20 (chưa làm NC2 = 10 − 5 = 5)
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC2", "M2", ok=20, start="10:05", end="10:30", times=[4, 3, 1])
        await save(pg, "S4 vượt trần NC2 OK20")
        # S5 máy trùng giờ: PO1 NC1 M1 08:30–08:45 (đã có S1 08:00–09:00)
        await remove_groups(pg); await pg.wait_for_timeout(400)
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC1", "M1", ok=3, start="08:30", end="08:45", times=[5, 2, 1])
        await save(pg, "S5 máy M1 trùng giờ")
        # S6 thiếu T.Gá (Hàng loạt): PO1 NC1 M3 (máy đồng dạng) OK 3 chỉ T.GC
        await remove_groups(pg); await pg.wait_for_timeout(400)
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC1", "M3", ok=3, start="10:40", end="11:00", times=[0, 2, 0])
        await save(pg, "S6 thiếu T.Gá/T.Ktra")
        # S7 bổ sung đủ → lưu được (máy đồng dạng M3, số chuẩn từ TimingRow)
        await set_num(row, 2, 6); await set_num(row, 4, 1)
        await save(pg, "S7 M3 đủ giờ OK3")
        # S8 NG không chọn loại lỗi
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC1", "M1", ok=2, ng=1, start="11:05", end="11:30", times=[5, 2, 1])
        await save(pg, "S8 NG chưa chọn loại lỗi")
        await remove_groups(pg); await pg.wait_for_timeout(400)
        # S9 sửa dòng S1: OK 10 → 12
        r1 = (await saved_row(pg, "PO1", "NC1", "08:00")).first
        await r1.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1200)
        g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_num(row, 0, 12)
        await save(pg, "S9 sửa S1 OK 10→12")
        # S10 sửa dòng S1 lần 2: OK 12 → 3 (NC2 đã dùng 5) → K5 chặn
        r1 = (await saved_row(pg, "PO1", "NC1", "08:00")).first
        await r1.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1200)
        g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_num(row, 0, 1)
        await save(pg, "S10 sửa S1 OK 12→1 (K5)")
        await remove_groups(pg); await pg.wait_for_timeout(600)
        # S11 xoá S1 khi NC2 còn dùng → chặn
        await void_row(pg, "S11 xoá S1 (K5 chặn)", "PO1", "NC1", "08:00")
        # S12 xoá S3 (NC2) → được
        await void_row(pg, "S12 xoá S3 NC2", "PO1", "NC2", "09:30")
        # S13 xoá S1 → được
        await void_row(pg, "S13 xoá S1", "PO1", "NC1", "08:00")
        await pg.screenshot(path=f"{OUT}_gc_end.png", full_page=True)
        await pg.close()

        # ── HTSP ──
        pg = await login(ctx, "htsp01", "123456")
        await ensure_group(pg); g = grp(pg)
        await pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.locator("select").nth(0).select_option("Taro"); await pg.wait_for_timeout(500)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO3")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("T1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 10)
        await set_time(row, "08:00", "08:30")
        await pg.screenshot(path=f"{OUT}_htsp1.png", full_page=True)
        # dòng 2: mã chuẩn bị 1T (không SL), T.Thực tế 15
        await pg.locator("button:has-text('Thêm số KH')").click(); await pg.wait_for_timeout(500)
        row2 = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(1)
        await set_po(row2, "PO3")
        cv2 = row2.locator("input[placeholder='Mã CV...']"); await cv2.fill("1T"); await cv2.dispatch_event("change"); await pg.wait_for_timeout(500)
        t2 = row2.locator("input[placeholder='HH:mm']").last; await t2.fill("08:45"); await t2.dispatch_event("change"); await pg.wait_for_timeout(300)
        # T.Thực tế: ô số cuối cùng trước T.PS → tìm theo step=0.1
        await row2.locator("input[type=number][step='0.1']").fill("15"); await pg.wait_for_timeout(300)
        await save(pg, "H1 HTSP Taro T1 OK10 + 1T chuẩn bị")
        # H2 Bavia B1 OK 10 NG 2
        await ensure_group(pg); g = grp(pg)
        await pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.locator("select").nth(0).select_option("Bavia"); await pg.wait_for_timeout(500)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO3")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("B1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 10); await set_num(row, 1, 2); await set_ngcat(row, "BM-XUOC · Xước")
        await set_time(row, "09:00", "09:30")
        await save(pg, "H2 HTSP Bavia B1 OK10 NG2")
        # H3 Rửa R1 OK 5 thiếu T.Thực tế? (tham khảo chuẩn mặc định bật → có sẵn) → lưu
        await ensure_group(pg); g = grp(pg)
        await pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.locator("select").nth(0).select_option("Rua"); await pg.wait_for_timeout(500)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO3")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("R1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 5); await set_time(row, "09:40", "09:50")
        await save(pg, "H3 HTSP Rửa R1 OK5")
        # H4 PO2 R1 OK 25 (còn 20) → vượt trần
        await ensure_group(pg); g = grp(pg)
        await pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.locator("select").nth(0).select_option("Rua"); await pg.wait_for_timeout(500)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO2")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("R1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 25); await set_time(row, "10:00", "10:30")
        await save(pg, "H4 HTSP PO2 R1 OK25 vượt 20")
        await remove_groups(pg); await pg.wait_for_timeout(400)
        # H5 sửa dòng B1: OK 10 → 8
        rb = (await saved_row(pg, "PO3", "B1")).first
        await rb.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1200)
        g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_num(row, 0, 8)
        await save(pg, "H5 sửa B1 OK 10→8")
        await pg.screenshot(path=f"{OUT}_htsp_end.png", full_page=True)
        await pg.close()

        # ── KCS ──
        pg = await login(ctx, "kcs01", "123456")
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO2")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("K1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 5); await set_time(row, "08:00", "08:20")
        await save(pg, "K1 KCS PO2 K1 OK5")
        await pg.close()

        # ── PKG ──
        pg = await login(ctx, "pkg01", "123456")
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO2")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("P1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 10); await set_time(row, "08:00", "08:20")
        await save(pg, "P1 PKG PO2 P1 OK10")
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO2")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("P1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 1); await set_time(row, "08:30", "08:40")
        await save(pg, "P2 PKG PO2 P1 OK1 (hết hàng)")
        await pg.screenshot(path=f"{OUT}_pkg_end.png", full_page=True)
        await pg.close()
        # ── E14 cuối cùng (KCS đăng nhập lại) ──
        pg = await login(ctx, "kcs01", "123456")
        # ── E14: mã ngoài snapshot (bản cũ lưu, bản mới chặn — khác nhau có chủ đích) ──
        await ensure_group(pg); g = grp(pg); row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await set_po(row, "PO2")
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("XYZ"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 0, 1); await set_time(row, "09:00", "09:10")
        await row.locator("input[type=number][step='0.1']").fill("1"); await pg.wait_for_timeout(300)
        await save(pg, "E14 KCS mã XYZ ngoài snapshot (chưa làm K1 còn 5)")
        await pg.screenshot(path=f"{OUT}_e14.png", full_page=True)
        await pg.close()
        await b.close()
    json.dump(log, open(OUT + ".json", "w"), ensure_ascii=False, indent=1)

asyncio.run(main())
