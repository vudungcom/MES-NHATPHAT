# [ALLOC-GC 30/10/2026] python3 drive_trial.py <port> <old|new>\n# Harness trang WTS thật: bấm cùng một chuỗi thao tác trên bản cũ / bản mới, ghi lại lời báo + chụp DB sau mỗi bước.
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
    # [ALLOC-GC 30/10] Mục 23b — GC Chạy thử 2 dòng SL 0 trong 1 nhóm: chia theo T.CB + T.Chạy (13 · 26 phút) trong 08:00–08:40
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        pg = await login(ctx, "admin", "admin123")
        await ensure_group(pg); g = grp(pg)
        await pg.locator("button:has-text('Chạy thử')").click(); await pg.wait_for_timeout(400)
        row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        await gc_row(pg, row, "PO1", "NC1", "M1", trial=[10, 3])
        t = row.locator("input[placeholder='HH:mm']"); await t.nth(0).fill("08:00"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(300)
        await pg.locator("button:has-text('Thêm số KH')").click(); await pg.wait_for_timeout(500)
        row2 = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(1)
        await set_po(row2, "PO1")
        nums = row2.locator("input[type=number]"); print("row2 số ô:", await nums.count(), "row1 số ô:", await row.locator("input[type=number]").count())
        await set_num(row2, 5, 20); await set_num(row2, 6, 6)
        t2 = row2.locator("input[placeholder='HH:mm']"); await t2.last.fill("08:40"); await t2.last.dispatch_event("change"); await pg.wait_for_timeout(500)
        await set_num(row, 5, 10); await pg.wait_for_timeout(300)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = await read_errors(pg)
        rows = [w for w in dump()["wts"] if w["khPlanDetailId"] == 1 and w["productionMode"] == "trial" and not w["isVoided"]]
        got = [(w["start"][-5:], w["end"][-5:]) for w in rows]
        print(OUT, "lỗi:", [e.replace("\n"," | ")[:200] for e in errs], "dòng:", got)
        ok = len(got) == 2 and got[0][0] == "08:00" and got[1][1] == "08:40" and got[0][1] == got[1][0] and got[0][1] in ("08:13", "08:14")
        print(("  ĐẠT  " if ok else "  SAI  ") + "Chạy thử 2 dòng SL 0 lưu được, chia 13/26 phút")
        await b.close()
asyncio.run(main())
