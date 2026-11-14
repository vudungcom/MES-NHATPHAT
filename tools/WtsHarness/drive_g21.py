# [GÓI 21 14/11/2026][G17-ON-DOI-O] Trang Nhận: Workflow vừa bật ON (tự cập nhật [GN-LIVE]) khi đang nhập dở phiếu chỉ định ô đạt lấy từ ô Hàng lỗi.
#   Nguồn: CLAUDE (rà độc lập Gói 17 Thấp b — sổ G17-GHI-NHO); HÙNG 14/11 08:28 OK Gói 21.
#   Dữ liệu: POST /harness/g16-seed + /harness/g16c-seed?po=PO-G21W (phiếu GC → HTSP 2 từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý), rồi /harness/g16-wf?on=0 (OFF).
#   W1 OFF: mở phiếu, ô nhận = ô chỉ định Hàng lỗi đã xử lý, gõ "Số lượng nhận đạt" 2.
#   W1b tự cập nhật lúc vẫn OFF → giữ nguyên ô + số (đột biến bỏ điều kiện RecvMustLoi phải SAI).
#   W2 bật ON thẳng DB + /harness/g21-notify (máy thật: lưới tự cập nhật 2 phút — Kế hoạch đổi Workflow không báo notifier) → trang tự cập nhật: ô nhận Hàng lỗi, số 2 dồn sang ô "Hàng lỗi nhận", ô đạt 0, lý do điền sẵn, cảnh báo.
#   W3 bấm Xác nhận nhận → DB: nhận đủ 2 vào ô Hàng lỗi HTSP (bản cũ: số ở ô đạt bị khoá → gửi 0 → báo lỗi, không lưu).
#   W4 phiếu khác (PO-G21X) đã TỰ chọn ô Hàng sửa lúc OFF (vẫn hợp lệ khi ON) rồi bật ON → giữ nguyên (đột biến bỏ dòng "giữ ô đã chọn" phải SAI — rà độc lập Gói 21 Thấp 6).
#   Bản 4f446b8 (Gói 20): W2 / W3 SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_g21.py <port> <nhãn>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg, po): return pg.locator("tr", has_text=po).first
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def on_cells(m): return [(await x.inner_text()).split("\n")[0].strip() for x in await m.locator("button.gn-cellbtn.on").all()]
async def nums(m):
    ins = m.locator("input[type=number]")
    return [await ins.nth(i).input_value() for i in range(min(2, await ins.count()))]

async def open_recv(pg, po):
    await pg.reload(); await pg.wait_for_timeout(4000)
    await row(pg, po).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
    m = modal(pg)
    if await m.locator("select").count():
        await m.locator("select").first.select_option(label="HTSP"); await pg.wait_for_timeout(1500)
    hdr = m.locator("div[style*='cursor:pointer']").first
    if await hdr.count(): await hdr.click(); await pg.wait_for_timeout(900)
    return m

async def main():
    post("/harness/g16-seed")
    for po in ("PO-G21W", "PO-G21X"):
        post("/harness/g16c-seed?po=" + po); post(f"/harness/g16-wf?po={po}&on=0")
    W = "/harness/g16?po=PO-G21W"; X = "/harness/g16?po=PO-G21X"
    w0 = get(W); x0 = get(X)
    pend = [t for t in w0["txs"] if t["status"] != "COMPLETED" and t["toGroupCode"] == "HTSP"]
    ok(len(pend) == 1 and pend[0]["fromCell"] == "LOI" and pend[0]["toCell"] == "XL", "W0 dữ liệu: PO-G21W 1 phiếu GC → HTSP 2 ô Hàng lỗi → Hàng lỗi đã xử lý chờ nhận, Workflow OFF", w0["txs"])
    async with async_playwright() as p:
        br = await p.chromium.launch()
        pg = await (await br.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[W] PO-G21W: đang nhập dở (OFF, ô Hàng lỗi đã xử lý, đạt 2) → Workflow bật ON")
        try:
            m = await open_recv(pg, "PO-G21W")
            on1 = await on_cells(m)
            q = m.locator("input[type=number]").nth(0); await q.fill("2"); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
            n1 = await nums(m)
            ok(on1 == ["Hàng lỗi đã xử lý"] and n1[:1] == ["2"], "W1 OFF: ô nhận = ô chỉ định Hàng lỗi đã xử lý, gõ đạt 2", {"on": on1, "so": n1})
            post("/harness/g21-notify?po=PO-G21W"); await pg.wait_for_timeout(2500)   # tự cập nhật lúc VẪN OFF → giữ nguyên
            m = modal(pg); on1b = await on_cells(m); n1b = await nums(m)
            ok(on1b == ["Hàng lỗi đã xử lý"] and n1b == ["2", "0"], "W1b tự cập nhật lúc Workflow vẫn OFF: giữ ô Hàng lỗi đã xử lý, đạt 2 (không tự chuyển ô)", {"on": on1b, "so": n1b})
            post("/harness/g16-wf?po=PO-G21W&on=1"); post("/harness/g21-notify?po=PO-G21W"); await pg.wait_for_timeout(2500)
            m = modal(pg)
            on2 = await on_cells(m); n2 = await nums(m); t = await m.inner_text()
            rs = m.locator("input.border-warning").first
            rv = await rs.input_value() if await rs.count() else ""
            ok(on2 == ["Hàng lỗi"] and n2 == ["0", "2"] and "Workflow ON" in rv and "đã chọn sẵn ô Hàng lỗi" in t,
               "W2 sau tự cập nhật: ô nhận Hàng lỗi, số 2 dồn sang ô Hàng lỗi nhận (đạt 0), lý do điền sẵn, có cảnh báo", {"on": on2, "so": n2, "lydo": rv})
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
            w1 = get(W); t1 = [x for x in w1["txs"] if pend and x["handoverTxId"] == pend[0]["handoverTxId"]]
            ok(t1 and t1[0]["status"] == "COMPLETED" and w1["htsp"]["loiGiu"] == w0["htsp"]["loiGiu"] + 2 and w1["htsp"]["xlGiu"] == w0["htsp"]["xlGiu"] and w1["balanced"] and w1["neg"] == 0,
               "W3 DB: nhận đủ 2 vào ô Hàng lỗi HTSP (không vào Hàng lỗi đã xử lý), sổ cân, 0 ô âm", {"tx": t1, "truoc": w0["htsp"], "sau": w1["htsp"]})
            await close_modal(pg)
        except Exception as e:
            ok(False, "W! lỗi bấm", repr(e)[:400]); await close_modal(pg)

        print("[W4] PO-G21X: lúc OFF đã TỰ chọn ô Hàng sửa (còn hợp lệ khi ON), số ở ô nhận → bật ON: giữ nguyên ô Hàng sửa, không tự chuyển sang Hàng lỗi")
        try:
            m = await open_recv(pg, "PO-G21X")
            await m.locator("button.gn-cellbtn", has_text="Hàng sửa").first.click(); await pg.wait_for_timeout(700)
            rs = m.locator("input.border-warning").first
            if await rs.count(): await rs.fill("tu chon o sua"); await rs.dispatch_event("change")
            q = m.locator("input[type=number]").nth(0); await q.fill("2"); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
            on4a = await on_cells(m); n4a = await nums(m)
            post("/harness/g16-wf?po=PO-G21X&on=1"); post("/harness/g21-notify?po=PO-G21X"); await pg.wait_for_timeout(2500)
            m = modal(pg)
            on4 = await on_cells(m); n4 = await nums(m)
            rv4 = await m.locator("input.border-warning").first.input_value() if await m.locator("input.border-warning").count() else ""
            ok(on4a == ["Hàng sửa"] and on4 == ["Hàng sửa"] and n4[:1] == ["2"] and n4 == n4a and rv4 == "tu chon o sua",
               "W4 giữ ô Hàng sửa đã chọn (vẫn hợp lệ khi ON), số 2 không đổi, lý do người dùng giữ nguyên", {"truoc": [on4a, n4a], "sau": [on4, n4], "lydo": rv4})
            await close_modal(pg)
        except Exception as e:
            ok(False, "W4! lỗi bấm", repr(e)[:400]); await close_modal(pg)
        await br.close()
    print(f"\nKẾT QUẢ drive_g21 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
