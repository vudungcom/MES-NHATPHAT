# [KIEM-CHUA 03/11/2026] Trang Giao nhận thật (harness EF giả) — lỗi máy chủ khi lưu / khi tải lại sau lưu:
#   [PAGE-ERR]     lỗi máy chủ lạ lúc Giao / Nhận → báo trong cửa sổ "Lỗi máy chủ: … xem đã lưu chưa", DB không đổi, trang không chết,
#                  bấm lại lần 2 ghi đúng 1 chứng từ (không ghi kèm dữ liệu dở của lần lỗi)
#   [PAGE-ERR2]    đã ghi xong, chỉ bước tải lại lỗi → KHÔNG báo "Lỗi máy chủ", báo "Đã lưu xong… Không bấm lưu lại" ở đầu trang
#                  (và trong cửa sổ Nhận còn mở), DB có đúng 1 chứng từ
#   [PAGE-ERR2-PO] lời báo trong cửa sổ chỉ hiện ở cửa sổ của ĐÚNG PO vừa lưu (Nhận / Lịch sử PO khác không hiện); lần lưu sau tải lại được → bỏ lời báo
# Dữ liệu: POST /harness/pe-seed (KH-PE: PO-PE1 / PO-PE2, Kho còn 20 phôi). Giả lỗi: POST /harness/fail?kind=save|reload&n=1 (Stubs/HarnessFail.cs).
# Chạy trên harness VỪA khởi động: python3 drive_pageerr.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""))

IDS = {}
def txs(po):
    d = get("/harness/dump"); return [t for t in d["gntx"] if t["khPlanDetailId"] == IDS[po] and not t.get("isVoided")]
def recs(po):
    d = get("/harness/dump"); tids = {t["handoverTxId"] for t in d["gntx"] if t["khPlanDetailId"] == IDS[po]}
    return [r for r in d["gnrec"] if r["handoverTxId"] in tids and not r.get("isVoided")]

async def login(b):
    pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1100})
    await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
    return pg
def modal(pg): return pg.locator(".mes-modal").first
async def modal_text(pg): return (await modal(pg).inner_text()) if await pg.locator(".mes-modal").count() else ""
async def alive(pg, kind):
    # Phiên Blazor còn sống = trang còn phản hồi thao tác (app không có khung #blazor-error-ui nên phải thử bấm thật):
    #   cửa sổ Giao: gõ số vượt trần → ô viền đỏ, rồi trả số cũ; cửa sổ Nhận: Thu gọn thẻ phiếu → ô số biến mất, mở lại
    m = modal(pg)
    if kind == "issue":
        q = m.locator("input[type=number]"); old = await q.input_value()
        await q.fill("999"); await q.dispatch_event("change"); await pg.wait_for_timeout(800)
        red = "border-danger" in (await q.get_attribute("class") or "")
        await q.fill(old); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
        return red
    await m.locator("button:has-text('Thu gọn')").first.click(); await pg.wait_for_timeout(800)
    gone = await m.locator("input.border-success").count() == 0
    await m.locator("text=Từ Kho").first.click(); await pg.wait_for_timeout(1000)
    return gone and await m.locator("input.border-success").count() == 1

async def open_issue(pg, po, qty):
    await pg.locator("tr", has_text=po).first.locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    m = modal(pg)
    await m.locator("select").first.select_option(label="Kho"); await pg.wait_for_timeout(900)
    await m.locator("button.gn-cellbtn", has_text="Hàng mới").first.click(); await pg.wait_for_timeout(700)
    await m.locator("select").nth(1).select_option(label="Gia công"); await pg.wait_for_timeout(700)
    await m.locator("input[type=number]").fill(str(qty)); await pg.wait_for_timeout(300)
async def confirm_issue(pg):
    await modal(pg).locator("button.btn-primary:has-text('Xác nhận giao')").click(); await pg.wait_for_timeout(2500)
async def close_modal(pg):
    if await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)

async def open_receive(pg, po):
    await pg.locator("tr", has_text=po).first.locator("button:has-text('Nhận')").first.click(); await pg.wait_for_timeout(1500)
    await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1500)
async def receive_first(pg, qty):
    m = modal(pg)
    q = m.locator("input.border-success")
    if await q.count() == 0:   # thẻ phiếu chưa mở → bấm để mở (đã mở thì bấm lại sẽ thu gọn)
        await m.locator("text=Từ Kho").first.click(); await pg.wait_for_timeout(1000)
    await q.fill(str(qty)); await q.dispatch_event("change"); await pg.wait_for_timeout(300)
    await m.locator("button.btn-success:has-text('Xác nhận')").first.click(); await pg.wait_for_timeout(2500)

async def open_log(pg, po):
    await pg.locator("tr", has_text=po).first.locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(1500)

async def top_banner(pg):
    # lời báo đầu trang (ngoài cửa sổ) có chữ "Đã lưu xong"
    loc = pg.locator(".alert-warning", has_text="Đã lưu xong")
    n = await loc.count(); inmodal = await pg.locator(".mes-modal .alert-warning", has_text="Đã lưu xong").count()
    return n - inmodal

async def main():
    global IDS
    for x in post("/harness/pe-seed"): IDS[x["purchaseOrder"]] = x["khPlanDetailId"]
    try:
        await run_all()
    finally:
        post("/harness/fail?kind=off")   # drive chết giữa chừng cũng gỡ giả lỗi
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

async def run_all():
    async with async_playwright() as p:
        b = await p.chromium.launch(); pg = await login(b)

        print("[P1] PAGE-ERR: lỗi máy chủ lúc Giao (SaveChanges ném)")
        post("/harness/fail?kind=save&n=1")
        await open_issue(pg, "PO-PE1", 5); await confirm_issue(pg)
        st = get("/harness/fail"); mt = await modal_text(pg)
        ok(st["saveThrown"] == 1, "P11 giả lỗi lưu đã chạy đúng 1 lần", st)
        ok("Lỗi máy chủ: GIA-LOI-SQL-LUU" in mt and "xem đã lưu chưa" in mt, "P12 [PAGE-ERR] cửa sổ Giao còn mở, báo 'Lỗi máy chủ: …' + dặn xem đã lưu chưa", mt[-300:])
        ok(await alive(pg, "issue"), "P13 [PAGE-ERR] trang không chết phiên (cửa sổ Giao còn phản hồi: gõ số vượt trần → viền đỏ)")
        ok(len(txs("PO-PE1")) == 0, "P14 [PAGE-ERR] DB không có phiếu giao nào của PO-PE1 (giao dịch lỗi không ghi)", txs("PO-PE1"))
        await confirm_issue(pg)
        t = txs("PO-PE1")
        ok(len(t) == 1 and float(t[0]["qtyIssued"]) == 5, "P15 [PAGE-ERR] bấm lại lần 2 (hết lỗi): đúng 1 phiếu 5 chiếc, không ghi kèm dữ liệu dở", t)
        ok(await pg.locator(".mes-modal").count() == 0 and await top_banner(pg) == 0, "P16 lưu được: cửa sổ đóng, không có lời báo 'Đã lưu xong…'")

        print("[P2] PAGE-ERR2: Giao đã ghi xong, tải lại lỗi")
        post("/harness/fail?kind=reload&n=1")
        await open_issue(pg, "PO-PE2", 4); await confirm_issue(pg)
        st = get("/harness/fail"); body = await pg.locator("body").inner_text()
        ok(st["reloadThrown"] == 1, "P21 giả lỗi tải lại đã chạy đúng 1 lần (sau khi giao dịch ghi xong)", st)
        t = txs("PO-PE2")
        ok(len(t) == 1 and float(t[0]["qtyIssued"]) == 4, "P22 [PAGE-ERR2] DB có đúng 1 phiếu 4 chiếc của PO-PE2", t)
        ok(await top_banner(pg) >= 1 and "Không bấm lưu lại" in body, "P23 [PAGE-ERR2] đầu trang báo 'Đã lưu xong… Không bấm lưu lại'", body[:200])
        ok("Lỗi máy chủ:" not in body, "P24 [PAGE-ERR2] không báo 'Lỗi máy chủ … xem đã lưu chưa' (đã lưu rồi)")
        ok(await pg.locator("tr", has_text="PO-PE1").count() > 0, "P25 [PAGE-ERR2] bảng PO vẫn hiện")

        print("[P3] PAGE-ERR2-PO: Nhận đã ghi xong, tải lại lỗi — lời báo trong cửa sổ chỉ ở đúng PO")
        await close_modal(pg)
        await open_receive(pg, "PO-PE2")
        post("/harness/fail?kind=reload&n=1")
        await receive_first(pg, 4)
        st = get("/harness/fail"); mt = await modal_text(pg)
        ok(st["reloadThrown"] == 2, "P31 giả lỗi tải lại lần 2 đã chạy", st)
        r = recs("PO-PE2")
        ok(len(r) == 1, "P32 [PAGE-ERR2] Nhận ghi đúng 1 lần nhận của PO-PE2", r)
        ok("Đã lưu xong" in mt and "Lỗi máy chủ:" not in mt, "P33 [PAGE-ERR2] cửa sổ Nhận PO-PE2 còn mở hiện 'Đã lưu xong…' (không 'Lỗi máy chủ')", mt[-300:])
        await close_modal(pg)
        await open_receive(pg, "PO-PE1"); mt = await modal_text(pg)
        ok("PO-PE1" in mt and "Đã lưu xong" not in mt, "P34 [PAGE-ERR2-PO] mở Nhận PO-PE1 (PO khác): cửa sổ KHÔNG hiện lời báo của PO-PE2", mt[:200])
        ok(await top_banner(pg) >= 1, "P35 [PAGE-ERR2-PO] lời báo đầu trang vẫn giữ (dưới lớp phủ)")
        await close_modal(pg)
        await open_log(pg, "PO-PE1"); mt = await modal_text(pg)
        ok("Đã lưu xong" not in mt, "P36 [PAGE-ERR2-PO] Lịch sử PO-PE1: không hiện lời báo của PO-PE2", mt[:200])
        await close_modal(pg)
        await open_log(pg, "PO-PE2"); mt = await modal_text(pg)
        ok("Đã lưu xong" in mt, "P37 [PAGE-ERR2-PO] Lịch sử PO-PE2: hiện lời báo (đúng PO vừa lưu)", mt[:200])
        await close_modal(pg)

        print("[P4] PAGE-ERR: lỗi máy chủ lúc Nhận")
        await open_receive(pg, "PO-PE1")
        post("/harness/fail?kind=save&n=1")
        await receive_first(pg, 5)
        st = get("/harness/fail"); mt = await modal_text(pg)
        ok(st["saveThrown"] == 2, "P41 giả lỗi lưu lần 2 đã chạy", st)
        ok("Lỗi máy chủ: GIA-LOI-SQL-LUU" in mt, "P42 [PAGE-ERR] cửa sổ Nhận báo 'Lỗi máy chủ: …'", mt[-300:])
        ok(await top_banner(pg) >= 1, "P42b lần lưu LỖI không xoá lời báo 'Đã lưu xong…' cũ ở đầu trang (chỉ lần lưu được mới bỏ)")
        ok(len(recs("PO-PE1")) == 0, "P43 [PAGE-ERR] Nhận lỗi: PO-PE1 chưa có lần nhận nào", recs("PO-PE1"))
        ok(await alive(pg, "recv"), "P44 [PAGE-ERR] trang không chết phiên sau lỗi Nhận (Thu gọn / mở lại thẻ phiếu được)")
        await receive_first(pg, 5)
        r = recs("PO-PE1")
        ok(len(r) == 1, "P45 [PAGE-ERR] Nhận lại (hết lỗi): đúng 1 lần nhận", r)
        ok(await top_banner(pg) == 0, "P46 [PAGE-ERR2] lần lưu sau tải lại được → bỏ lời báo 'Đã lưu xong…' cũ ở đầu trang")
        await pg.screenshot(path=f"pageerr_{OUT}.png", full_page=False)
        await b.close()
asyncio.run(main())
