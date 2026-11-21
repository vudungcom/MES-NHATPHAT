# [GÓI 28 20/11/2026] drive_g28.py — sửa lỗi ngầm, phần chỉ thấy trên trang thật / đường HTTP:
#   L1 [LOGIN-LOI-DB] lỗi đọc DB lúc đăng nhập → 302 về /login?error=4 + chữ "Máy chủ lỗi khi đăng nhập" (bản cũ: lỗi thoát → /error POST → 400 / 500).
#   L2 [LOGIN-TRIM] gõ "admin123 " (dấu cách cuối) → vào được (bản cũ: error=1); "admin12" vẫn sai.
#   W1–W3 [G21-WF-NOTIFY][G21-OFF-LYDO] trang Nhận đang mở phiếu Workflow ON (tự chọn ô Hàng lỗi + lý do "Workflow ON…"), Kế hoạch TẮT Workflow qua
#         KhPlanService.SaveEditAsync thật — đường trang Sửa phiếu KH → trang tự cập nhật NGAY (không chờ lưới 2 phút): ô nhận về ô chỉ định Hàng lỗi đã xử lý, số về ô đạt, lý do tự điền mất;
#         Xác nhận nhận → DB nhận vào Hàng lỗi đã xử lý (OFF hợp lệ). Bản cũ: không báo → sau 3 giây vẫn ô Hàng lỗi (SAI là đúng).
#   R1–R2 [R08-CANCU] g28m (chỉ Sửa WTS_MASTER) mở trang WTS thấy ô "Xem WTS của"; Admin rút quyền ngoài trang → đổi ngày (nạp lại bảng) → ô ẩn,
#         nhãn "Ngày cũ — chỉ xem" (bản cũ: còn ô + "chỉ Sửa / Xoá kèm lý do" tới khi tải lại trang).
#   Nguồn: CLAUDE rà độc lập Gói 21 / 22 / 25; HÙNG 20/11 22:51 OK Gói 28.
# Chạy trên harness VỪA khởi động: python3 drive_g28.py <port> <nhãn>
import sys, asyncio, json, urllib.request, http.client, urllib.parse, datetime
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
def login_raw(user, pw):
    c = http.client.HTTPConnection("127.0.0.1", int(PORT), timeout=30)
    body = urllib.parse.urlencode({"username": user, "password": pw, "returnUrl": ""})
    c.request("POST", "/login-submit", body=body, headers={"Content-Type": "application/x-www-form-urlencoded"})
    r = c.getresponse(); loc = r.getheader("Location") or ""; st = r.status; r.read(); c.close()
    return st, loc
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
    print("[L] đăng nhập: lỗi đọc DB / mật khẩu có dấu cách")
    post("/harness/g28-fail-login?n=1")
    try: st, loc = login_raw("admin", "admin123")
    except Exception as e: st, loc = -1, repr(e)
    post("/harness/g28-fail-login?n=0")
    body = ""
    if "error=4" in loc:
        body = urllib.request.urlopen(BASE + loc).read().decode()
    ok(st == 302 and "/login?error=4" in loc and "Máy chủ lỗi khi đăng nhập" in body,
       "L1 lỗi đọc DB lúc đăng nhập → 302 /login?error=4 + chữ 'Máy chủ lỗi khi đăng nhập' (trước: lỗi thoát → /error 400 / 500)", f"{st} {loc}")
    st2, loc2 = login_raw("admin", "admin123 ")
    st3, loc3 = login_raw("admin", "admin12")
    ok(st2 == 302 and "error=" not in loc2 and st3 == 302 and "error=1" in loc3, "L2 'admin123 ' (dấu cách cuối) → vào; 'admin12' → error=1", f"{st2} {loc2} | {st3} {loc3}")

    post("/harness/g16-seed"); post("/harness/g16c-seed?po=PO-G28W"); post("/harness/g16-wf?po=PO-G28W&on=1")
    W = "/harness/g16?po=PO-G28W"
    w0 = get(W)
    pend = [t for t in w0["txs"] if t["status"] != "COMPLETED" and t["toGroupCode"] == "HTSP"]
    ok(len(pend) == 1 and pend[0]["fromCell"] == "LOI" and pend[0]["toCell"] == "XL", "W0 dữ liệu: PO-G28W 1 phiếu GC → HTSP 2 ô Hàng lỗi → Hàng lỗi đã xử lý chờ nhận, Workflow ON", w0["txs"])
    post("/harness/g28-seed")
    async with async_playwright() as p:
        br = await p.chromium.launch()
        pg = await (await br.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
        print("[W] PO-G28W: Workflow ON đang nhận dở (tự chọn ô Hàng lỗi) → Kế hoạch tắt Workflow")
        try:
            m = await open_recv(pg, "PO-G28W")
            on1 = await on_cells(m)
            rs = m.locator("input.border-warning").first
            rv1 = await rs.input_value() if await rs.count() else ""
            q = m.locator("input[type=number]").nth(1); await q.fill("2"); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
            n1 = await nums(m)
            ok(on1 == ["Hàng lỗi"] and "Workflow ON" in rv1 and n1 == ["0", "2"], "W1 ON: tự chọn ô Hàng lỗi + lý do 'Workflow ON…', gõ Hàng lỗi nhận 2", {"on": on1, "lydo": rv1, "so": n1})
            r = post("/harness/g28-wf?po=PO-G28W&on=0")
            await pg.wait_for_timeout(3000)   # bản mới: KhPlanService báo notifier → tự cập nhật ~0,3 giây; bản cũ: chờ lưới 2 phút
            m = modal(pg); on2 = await on_cells(m); n2 = await nums(m)
            rs2 = m.locator("input.border-warning")
            rv2 = await rs2.first.input_value() if await rs2.count() else ""
            ok(r["ok"] and r["on"] is False and on2 == ["Hàng lỗi đã xử lý"] and n2[:1] == ["2"] and "Workflow ON" not in rv2,
               "W2 Kế hoạch tắt Workflow (KhPlanService.SaveEditAsync thật — đường trang Sửa phiếu KH) → trang tự cập nhật NGAY: về ô chỉ định Hàng lỗi đã xử lý, số 2 về ô đạt, mất lý do 'Workflow ON'", {"wf": r, "on": on2, "so": n2, "lydo": rv2})
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
            w1 = get(W); t1 = [x for x in w1["txs"] if x["handoverTxId"] == pend[0]["handoverTxId"]]
            ok(t1 and t1[0]["status"] == "COMPLETED" and w1["htsp"]["loiGiu"] == w0["htsp"]["loiGiu"] and w1["balanced"] and w1["neg"] == 0,
               "W3 Xác nhận nhận (OFF): nhận đủ 2, KHÔNG vào ô Hàng lỗi (ô chỉ định), sổ cân, 0 ô âm", {"tx": t1, "truoc": w0["htsp"], "sau": w1["htsp"]})
            await close_modal(pg)
        except Exception as e:
            ok(False, "W! lỗi bấm", repr(e)[:400]); await close_modal(pg)

        print("[R] g28m (chỉ Sửa WTS_MASTER) — Admin rút quyền ngoài trang → nạp lại bảng (đổi ngày) thì ô 'Xem WTS của' ẩn")
        try:
            ctx2 = await br.new_context(); p2 = await ctx2.new_page(); await p2.set_viewport_size({"width": 1800, "height": 1200})
            await p2.goto(BASE + "/login"); await p2.fill("#username", "g28m"); await p2.fill("#password", "123456")
            await p2.click("button[type=submit]"); await p2.wait_for_load_state("networkidle")
            await p2.goto(BASE + "/hieu-suat-cong-nhan"); await p2.wait_for_timeout(5000)
            t0 = await p2.inner_text("body")
            ok("Xem WTS của" in t0, "R1 g28m có Sửa WTS_MASTER: thấy ô 'Xem WTS của'")
            post("/harness/g28-perm?edit=0")
            y = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
            d = p2.locator("input[type=date]").first
            await d.fill(y); await d.dispatch_event("change"); await p2.wait_for_timeout(3000)
            t1b = await p2.inner_text("body"); dv = await d.input_value()
            ok(dv == y and "Xem WTS của" not in t1b and "chỉ Sửa / Xoá kèm lý do" not in t1b,
               "R2 rút quyền rồi đổi ngày (nạp lại bảng): ô 'Xem WTS của' ẩn (trước: giữ quyền cũ tới khi tải lại trang)",
               {"ngay": dv, "url": p2.url, "dong": [x for x in t1b.split("\n") if "Ngày cũ" in x or "Xem WTS" in x][:4], "dau": t1b[:600]})
            post("/harness/g28-perm?edit=1")
            await ctx2.close()
        except Exception as e:
            ok(False, "R! lỗi bấm", repr(e)[:400])
        await br.close()
    print(f"\nKẾT QUẢ drive_g28 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
