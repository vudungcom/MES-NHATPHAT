# [N21-SCEN 25/11/2026] Trang thật (harness EF giả) — luật N21 (Gói G-D): WTS có NG KHÔNG tự sinh mã vụ → sổ hiện "NG chưa báo lỗi";
#   báo lỗi riêng ở form Giao / xử lý hàng (Hàng lỗi → "NG ở WTS chưa báo lỗi"), được báo vượt NG WTS (trừ vào hàng đã làm xong chưa giao).
#   Nguồn: KHÁCH 23/11 (mockup PGi3QtkmwVSZFCXZXWrGiZ TH10 Cách 2); HÙNG 23/11 16:24 (N21) + 25/11 16:27.
#   Dữ liệu: seed khởi động PO1 (Kho → GC 40 ở NC1, GC NC1 → NC2). Đọc DB: GET /harness/n21?po=PO1 (Stubs/HarnessN21.cs).
#   STEP W trang WTS (admin = GC): PO1 NC1 M1 OK 10 NG 3, KHÔNG chọn loại lỗi (ô loại lỗi ẩn) → lưu được, dòng không mã vụ / không loại lỗi,
#       lời nhắc "chưa báo lỗi"; sổ GC: NG chưa báo lỗi 3, Tồn hàng lỗi 0.  Bản cũ (a3a3035) phải SAI ở đây (bắt chọn loại lỗi).
#   STEP G bảng Giao nhận: ô Tồn GC có dòng "NG chưa báo lỗi 3".
#   STEP F form Giao / xử lý hàng · Gia công · Hàng lỗi: dòng "NG ở WTS chưa báo lỗi · NC1" có 3, ghi chú báo được tối đa 13 (3 + 10 hàng NC1 xong chờ NC2);
#       Giữ lại, xử lý sau · số lượng 4 (báo dư 1) + loại lỗi → "Sẽ ghi: báo lỗi 4 chiếc (mã vụ mới …) … về 0; báo dư 1 trừ vào hàng đã làm xong chưa giao"
#       → DB: 1 báo lỗi ô NGW 4 (NC1), vụ OriginKind WTSBL; sổ GC: NG chưa báo 0, Tồn hàng lỗi 4, NC2 chờ làm 10 → 9; sổ cân, 0 ô âm.
#   STEP X trang WTS: xoá dòng NG đã báo lỗi → bị chặn có lời giải thích, dòng còn nguyên.
# Chạy trên harness VỪA khởi động: python3 drive_n21.py <port> <old|new>
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
SIX = ["Sửa tại bộ phận mình / XXN KH", "Gửi bộ phận khác sửa", "Trả nơi làm lỗi", "Giữ lại, xử lý sau", "Huỷ", "Trả khách"]
CAT = "KT-SAI · Sai kích thước"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def st(): return get("/harness/n21?po=PO1")
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
def porow(pg): return pg.locator("tr", has=pg.locator("td", has_text=re.compile(r"^\s*PO1\s*$"))).first
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def num(m, pg, i, v):
    x = m.locator("input[type=number]").nth(i); await x.fill(str(v)); await x.dispatch_event("change"); await pg.wait_for_timeout(500)
async def pick_cat(m, pg, label):
    inp = m.locator("input[list^='ngcat_']").first
    await inp.fill(label); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
async def confirm(m, pg):
    b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
    dis = await b.is_disabled()
    if not dis: await b.click(); await pg.wait_for_timeout(2500)
    return not dis
def sane(d, name):
    ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", d.get("groups"))
def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
async def wts_page(pg):
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(1500)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)

async def main():
    d0 = st()
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1900, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")

        print("[STEP W] trang WTS: GC PO1 NC1 OK 10 NG 3, không chọn loại lỗi")
        await wts_page(pg)
        if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
        r = grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        inp = r.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO1"); await inp.press("Tab"); await pg.wait_for_timeout(700)
        await r.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        await r.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(400)
        for i, v in [(0, 10), (1, 3)]:
            await r.locator("input[type=number]").nth(i).fill(str(v)); await pg.wait_for_timeout(250)
        t = r.locator("input[placeholder='HH:mm']")
        await t.nth(0).fill("08:00"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
        await t.nth(1).fill("09:00"); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
        for i, v in enumerate([5, 2, 1]):
            await r.locator("input[type=number]").nth(2 + i).fill(str(v)); await pg.wait_for_timeout(200)
        await pg.wait_for_timeout(500)
        catVis = await r.locator("input[placeholder='Loại lỗi…']").is_visible() if await r.locator("input[placeholder='Loại lỗi…']").count() else False
        ok(not catVis, "W1 dòng mới có NG: ô 'Loại lỗi' ẩn (WTS không tự sinh mã vụ)", {"catVisible": catVis})
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = [x.strip() for x in await pg.locator(".alert-danger").all_inner_texts() if x.strip() and not x.strip().startswith("Sao lưu")]   # bỏ băng cảnh báo sao lưu (harness chưa cài thư mục)
        d1 = st()
        nw = [x for x in d1["wts"] if x not in d0["wts"]]
        ok(not errs and len(nw) == 1 and nw[0]["qtyNg"] == 3 and nw[0]["ngCaseId"] is None and not nw[0]["ngReason"],
           "W2 lưu được: dòng WTS NG 3, không mã vụ, không loại lỗi", {"errs": errs, "new": nw})
        if fails:
            await b.close(); print(f"KẾT QUẢ drive_n21 ({OUT}): {fails} SAI"); return
        body = await pg.inner_text("body")
        ok("chưa báo lỗi" in body, "W3 lời nhắc sau lưu: NG chưa báo lỗi — báo lỗi ở trang Giao nhận", body[-400:])
        g1 = d1["groups"]["GC"]
        ok(g1["ngcb"] == 3 and g1["loi"] == 0 and len(d1["cases"]) == len(d0["cases"]), "W4 sổ GC: NG chưa báo lỗi 3, Tồn hàng lỗi 0, không thêm vụ", g1)
        sane(d1, "W5")

        print("[STEP G] bảng Giao nhận: ô Tồn GC có 'NG chưa báo lỗi 3'")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
        rt = await porow(pg).inner_text()
        ok("NG chưa báo lỗi 3" in rt, "G1 dòng PO1: ô Tồn Gia công ghi 'NG chưa báo lỗi 3'", rt[:600])

        print("[STEP F] form Giao / xử lý hàng · Gia công · Hàng lỗi · NG ở WTS chưa báo lỗi · NC1")
        await porow(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(900)
        m = modal(pg)
        await click(m, pg, "Hàng lỗi")
        btns = [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
        nb = [x for x in btns if "NG ở WTS chưa báo lỗi" in x]
        ok(len(nb) == 1 and "NC1" in nb[0] and "3" in nb[0], "F1 bước 2 có dòng 'NG ở WTS chưa báo lỗi · NC1' có 3 (mockup TH10 bước 2)", btns)
        await click(m, pg, "NG ở WTS chưa báo lỗi")
        s6 = [((await x.inner_text()).strip(), await x.is_disabled()) for x in await m.locator("div.row.g-1 button.gn-cellbtn").all()]
        ok([x[0] for x in s6] == SIX, "F2 bước 3 đủ 6 nút như mọi hàng lỗi mới", s6)
        await click(m, pg, SIX[3])
        await num(m, pg, 0, 4); await pick_cat(m, pg, CAT)
        t = await m.inner_text()
        ok("3 NG chưa báo" in t and "tối đa 13" in t, "F3 chữ cạnh Số lượng: 3 NG chưa báo; báo thêm trừ hàng đã làm xong — tối đa 13", t[-900:])
        ok("Sẽ ghi: báo lỗi 4 chiếc (mã vụ mới" in t and "báo dư 1 trừ vào hàng đã làm xong chưa giao" in t, "F4 Sẽ ghi: báo lỗi 4 (mã vụ mới) … về 0; báo dư 1 trừ vào hàng đã làm xong", t[-700:])
        sent = await confirm(m, pg)
        d2 = st()
        nr = [x for x in d2["reports"] if x not in d1["reports"]]
        nc = [x for x in d2["cases"] if x not in d1["cases"]]
        ok(sent and len(nr) == 1 and nr[0]["sourceCell"] == "NGW" and nr[0]["qty"] == 4 and nr[0]["nc"] == "NC1" and nr[0]["groupCode"] == "GC",
           "F5 DB: 1 báo lỗi Gia công ô NGW 4 (NC1)", nr)
        ok(len(nc) == 1 and nc[0]["originKind"] == "WTSBL" and nr and nr[0]["ngCaseId"] == nc[0]["ngCaseId"] and nc[0]["ngCategoryId"] is not None,
           "F6 DB: vụ mới OriginKind WTSBL (Báo lỗi NG ở WTS), có loại lỗi, gắn với báo lỗi", nc)
        g2 = d2["groups"]["GC"]
        ok(g2["ngcb"] == 0 and g2["loi"] == 4 and d1["nc"].get("NC2") == 10 and d2["nc"].get("NC2") == 9,
           "F7 sổ GC: NG chưa báo 0, Tồn hàng lỗi 4; NC2 chờ làm 10 → 9 (báo dư 1 trừ hàng NC1 xong)", {"GC": g2, "nc1": d1["nc"], "nc2": d2["nc"]})
        sane(d2, "F8")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        rt = await porow(pg).inner_text()
        ok("NG chưa báo lỗi" not in rt, "F9 bảng: hết dòng 'NG chưa báo lỗi' ở PO1", rt[:500])

        print("[STEP X] trang WTS: xoá dòng NG đã báo lỗi → chặn có lời giải thích")
        await wts_page(pg)
        sr = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']")).filter(has_text="PO1").filter(has_text="08:00").first
        await sr.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(500)
        await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        msg = ""
        vb = pg.locator(".alert-warning").filter(has_text=re.compile(r"\S"))
        if await vb.count(): msg = (await vb.first.inner_text()).strip()
        d3 = st()
        ok(msg != "" and "Báo lỗi #" in msg and any(x["wtsLogId"] == nw[0]["wtsLogId"] for x in d3["wts"]) and d3["groups"]["GC"]["loi"] == 4,
           "X1 xoá bị chặn, lời giải thích chỉ ra phiếu Báo lỗi (NG ở WTS) cần gỡ; dòng WTS còn nguyên, Tồn hàng lỗi GC vẫn 4", {"msg": msg[:400], "GC": d3["groups"]["GC"]})
        sane(d3, "X2")
        await b.close()
    print(f"KẾT QUẢ drive_n21 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
