# [GÓI 22 15/11/2026] drive_g22.py — bấm trang thật cho các sửa giao diện Gói 22 (quét Fable 14–15/11). Nguồn: CLAUDE quét Fable; HÙNG 15/11 15:10 OK Gói 22.
#   B0 [BM2-07] Bootstrap cục bộ: GET /lib/bootstrap/css/bootstrap.min.css + /lib/bootstrap-icons/font/bootstrap-icons.min.css + font woff2 = 200;
#      HTML trang đăng nhập không còn link cdn.jsdelivr (bản cũ: 404 + có CDN → SAI là đúng).
#   P8 [P-08] GET /handover chưa đăng nhập → về /login?returnUrl=%2Fhandover (AuthorizeRouteView); bản cũ: về /login không returnUrl → SAI là đúng.
#   F1–F3 [BH-02] Giao nhận: lọc cột Part No (bỏ tích 1 giá trị) → POST /harness/g22-notifyall (= lưới 2 phút / NotifyAll tính lại cả bảng)
#      → bộ lọc còn (icon funnel-fill, nút "Xoá filter cột" còn, số dòng vẫn bị lọc); bản cũ: mất lọc → SAI là đúng.
#   T1 [BM1-02] Thiết bị: double-click máy → nút "Lịch sử" trong hộp Xem → hộp lịch sử hiện, không đứng "Đang tải lịch sử..." (DB giả trả ngay nên bản cũ cũng qua — chỉ hồi quy).
#   W1 [A1-01][BW2-02] WTS KCS lưu K1 OK 5 trên PO2 (cổng _dbGate + guard _anySaving không làm hỏng đường lưu thường): 1 dòng WTS KCS, không lỗi;
#      W2 gõ PO rồi bấm Lưu ngay (nạp PO + Lưu nối tiếp qua cổng) vẫn lưu đúng 1 dòng.
# Chạy trên harness VỪA khởi động: python3 drive_g22.py <port> <nhãn>
import sys, os, asyncio, json, urllib.request, urllib.error
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def status(path):
    try:
        r = urllib.request.urlopen(BASE + path); return r.status, r.read()
    except urllib.error.HTTPError as e: return e.code, b""

BS = os.environ.get("KHO_BS", "/home/claude/bs_shared")   # bản cũ còn lấy Bootstrap từ CDN (harness không có mạng) → trỏ về thư mục tải sẵn như drive_kho.py
async def cdn(ctx):
    async def h(route):
        u = route.request.url
        f = BS + ("/bootstrap-icons-1.11.3/" + u.split("bootstrap-icons@1.11.3/")[1] if "bootstrap-icons@" in u else "/bootstrap-5.3.3/" + u.split("bootstrap@5.3.3/")[1])
        await (route.fulfill(path=f) if os.path.exists(f) else route.abort())
    await ctx.route("https://cdn.jsdelivr.net/npm/**", h)

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl): return None

async def login(ctx, user, pw, goto):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + goto); await pg.wait_for_timeout(4000)
    return pg

async def main():
    print("[B0] BM2-07 Bootstrap cục bộ")
    for path in ("/lib/bootstrap/css/bootstrap.min.css", "/lib/bootstrap-icons/font/bootstrap-icons.min.css", "/lib/bootstrap-icons/font/fonts/bootstrap-icons.woff2"):
        st, body = status(path)
        ok(st == 200 and len(body) > 10000, f"B0 GET {path} = 200, > 10 KB", f"status {st}, {len(body)} byte")
    st, html = status("/login"); h = html.decode("utf-8", "ignore")
    ok("cdn.jsdelivr.net" not in h and "lib/bootstrap/css/bootstrap.min.css" in h and "lib/bootstrap-icons/font/bootstrap-icons.min.css" in h,
       "B0 HTML /login: link Bootstrap + icons cục bộ, không còn cdn.jsdelivr", h[h.find("<head"):h.find("</head>")][:600])

    print("[P8] P-08 /handover chưa đăng nhập")
    op = urllib.request.build_opener(NoRedirect)
    try:
        r = op.open(BASE + "/handover"); code, loc = r.status, r.headers.get("Location", "")
    except urllib.error.HTTPError as e: code, loc = e.code, e.headers.get("Location", "")
    ok(code in (301, 302, 307, 308) and "/login" in loc and "returnurl=%2fhandover" in loc.lower(),
       "P8 GET /handover (chưa đăng nhập) → chuyển /login?ReturnUrl=%2Fhandover (cookie challenge qua [Authorize], như mọi trang)", f"status {code} Location={loc}")

    async with async_playwright() as p:
        br = await p.chromium.launch()
        ctx = await br.new_context(); await cdn(ctx)

        print("[F] BH-02 Giao nhận: lọc cột Part No giữ qua lưới tự cập nhật cả bảng")
        pg = await login(ctx, "admin", "admin123", "/handover")
        try:
            rows0 = await pg.locator("tbody tr[style*='cursor']").count()
            if rows0 == 0: rows0 = await pg.locator("table tbody tr").count()
            btns = pg.locator("button.col-filter-btn")
            ok(await btns.count() >= 3, "F0 có 3 nút lọc cột (Số KH / PO / Part No)", await btns.count())
            await btns.nth(2).click(); await pg.wait_for_timeout(800)   # Part No
            cbs = pg.locator("input[type=checkbox]")
            n = await cbs.count()
            ok(n >= 2, "F0 menu lọc mở, có ô tích giá trị", n)
            # bỏ tích giá trị đầu (ô 0 = chọn tất cả → bỏ ô 1)
            await cbs.nth(1).uncheck(); await pg.wait_for_timeout(600)
            # đóng menu: bấm OK / Đóng nếu có, không thì Escape
            footer = pg.locator("button:has-text('OK')")
            if await footer.count(): await footer.first.click()
            else: await pg.keyboard.press("Escape")
            await pg.wait_for_timeout(800)
            act1 = await pg.locator("button.col-filter-btn.active").count()
            clr1 = await pg.locator("button:has-text('Xoá filter cột')").count()
            rows1 = await pg.locator("table tbody tr").count()
            ok(act1 >= 1 and clr1 >= 1, "F1 sau khi bỏ tích: icon lọc sáng + nút \"Xoá filter cột\" hiện", f"active={act1} clear={clr1}")
            post("/harness/g22-notifyall"); await pg.wait_for_timeout(3500)
            act2 = await pg.locator("button.col-filter-btn.active").count()
            clr2 = await pg.locator("button:has-text('Xoá filter cột')").count()
            rows2 = await pg.locator("table tbody tr").count()
            ok(act2 >= 1 and clr2 >= 1, "F2 sau NotifyAll (= lưới 2 phút tính lại cả bảng): bộ lọc cột CÒN (trước Gói 22: mất)", f"active={act2} clear={clr2}")
            ok(rows2 == rows1, "F3 số dòng sau tự cập nhật = số dòng đang lọc (không trả về đủ bảng)", f"trước lọc {rows0} · đang lọc {rows1} · sau cập nhật {rows2}")
            post("/harness/g22-notifyall"); await pg.wait_for_timeout(3000)
            ok(await pg.locator("button.col-filter-btn.active").count() >= 1, "F4 lần tự cập nhật thứ 2 vẫn giữ lọc")
        except Exception as ex: ok(False, "F! lỗi chạy pha F", repr(ex))
        await pg.screenshot(path=f"{OUT}_g22_handover.png", full_page=True); await pg.close()

        print("[T] BM1-02 Thiết bị: Lịch sử từ hộp Xem")
        pg = await login(ctx, "admin", "admin123", "/thiet-bi")
        try:
            r0 = pg.locator("tr[title='Double-click để xem chi tiết']").first
            ok(await r0.count() == 1, "T0 có dòng máy", await pg.locator("tr[title='Double-click để xem chi tiết']").count())
            await r0.dblclick(); await pg.wait_for_timeout(900)
            ok(await pg.locator(".modal-title:has-text('Chi tiết thiết bị')").count() == 1, "T0 hộp Xem chi tiết mở")
            await pg.locator(".modal-footer button:has-text('Lịch sử')").first.click(); await pg.wait_for_timeout(1500)
            title = pg.locator(".modal-title:has-text('Lịch sử thay đổi')")
            body = await pg.locator(".modal-body").last.inner_text()
            ok(await title.count() == 1 and await pg.locator(".modal-title:has-text('Chi tiết thiết bị')").count() == 0, "T1 hộp Xem đóng, hộp Lịch sử mở")
            ok("Đang tải lịch sử" not in body, "T1 hộp Lịch sử đã tải xong (không đứng \"Đang tải lịch sử...\")", body[:200])
        except Exception as ex: ok(False, "T! lỗi chạy pha T", repr(ex))
        await pg.close()

        print("[W] WTS KCS: đường lưu thường qua cổng _dbGate + guard _anySaving")
        pg = await login(ctx, "kcs01", "123456", "/hieu-suat-cong-nhan")
        try:
            base_ids = {w["wtsLogId"] for w in get("/harness/dump")["wts"]}   # dòng có sẵn trong seed (KCS PO2 10 OK ngày cũ)
            def new_kcs(): return [w for w in get("/harness/dump")["wts"] if w["wtsLogId"] not in base_ids and w["processGroup"] == "KCS" and not w["isVoided"]]
            await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
            if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
                await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
            g = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO2"); await inp.press("Tab"); await pg.wait_for_timeout(700)
            cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("K1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
            await row.locator("input[type=number]").nth(0).fill("5"); await pg.wait_for_timeout(200)
            t = row.locator("input[placeholder='HH:mm']")
            await t.nth(0).fill("08:00"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
            await t.nth(1).fill("08:20"); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
            await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
            errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
            wts = new_kcs()
            ok(len(errs) == 0 and len(wts) == 1 and wts[0]["qtyOk"] == 5, "W1 KCS lưu K1 OK 5 trên PO2: 1 dòng WTS KCS, không lỗi", f"loi={errs} wts={wts}")
            # W2: gõ PO (nạp qua cổng) rồi bấm Lưu ngay — lượt Lưu phải chờ lượt nạp xong, vẫn lưu đúng
            if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
                await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
            g = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO2"); await inp.press("Tab"); await pg.wait_for_timeout(700)
            cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("K1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
            await row.locator("input[type=number]").nth(0).fill("2"); await pg.wait_for_timeout(200)
            t = row.locator("input[placeholder='HH:mm']")
            await t.nth(0).fill("08:30"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
            await t.nth(1).fill("08:40"); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
            await inp.fill("PO2"); await inp.dispatch_event("input")   # gõ lại PO (nạp PO qua cổng) rồi bấm Lưu ngay, không chờ
            await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(3000)
            errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
            wts = new_kcs()
            ok(len(errs) == 0 and len(wts) == 2 and sorted(w["qtyOk"] for w in wts) == [2, 5], "W2 gõ PO rồi Lưu ngay (nạp + Lưu nối tiếp qua cổng): đúng 2 dòng (5, 2), không lỗi", f"loi={errs} wts={[(w['qtyOk'], w['wtsCode']) for w in wts]}")
        except Exception as ex: ok(False, "W! lỗi chạy pha W", repr(ex))
        print("[M] A1-01-NHOM-MA: mở 2 nhóm, lưu nhóm 1 → nhóm 2 KHÔNG bị nhân đôi; lưu nhóm 2 → không còn nhóm ma")
        try:
            async def fill_group(g, qty, t0, t1):
                row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
                inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO2"); await inp.press("Tab"); await pg.wait_for_timeout(700)
                cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("K1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(400)
                await row.locator("input[type=number]").nth(0).fill(str(qty)); await pg.wait_for_timeout(200)
                t = row.locator("input[placeholder='HH:mm']")
                await t.nth(0).fill(t0); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
                await t.nth(1).fill(t1); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
            def groups(): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']"))
            # dọn nhóm đang có, mở đúng 2 nhóm
            while await pg.locator("button.btn-outline-danger.ms-auto").count():
                await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(400)
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(400)
            ok(await groups().count() == 2, "M0 mở 2 nhóm", await groups().count())
            await fill_group(groups().nth(0), 1, "09:00", "09:10")
            await fill_group(groups().nth(1), 1, "09:30", "09:40")
            before = len(new_kcs())
            await pg.locator("button.btn-success:has-text('Lưu')").nth(0).click(); await pg.wait_for_timeout(2500)
            n1 = await groups().count(); errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
            ok(n1 == 1 and not errs and len(new_kcs()) == before + 1, "M1 lưu nhóm 1: còn đúng 1 nhóm trên form (trước Gói 22: nhóm 2 bị thêm lần 2 = 2 nhóm cùng nội dung)", f"nhóm={n1} loi={errs}")
            await pg.locator("button.btn-success:has-text('Lưu')").nth(0).click(); await pg.wait_for_timeout(2500)
            n2 = await groups().count(); errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
            ok(n2 == 0 and not errs and len(new_kcs()) == before + 2, "M2 lưu nhóm 2: form không còn nhóm (trước: còn 1 nhóm ma mang dòng đã lưu), tổng +2 dòng", f"nhóm={n2} loi={errs} rows={len(new_kcs()) - before}")
        except Exception as ex: ok(False, "M! lỗi chạy pha M", repr(ex))
        await pg.screenshot(path=f"{OUT}_g22_wts.png", full_page=True); await pg.close()
        await br.close()
    print(f"\nKẾT QUẢ drive_g22 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
