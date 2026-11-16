# [GÓI 24 16/11/2026] drive_g24.py — bấm trang thật cho các sửa Gói 24 (quét Fable 15/11: P-05 / C-05 / C-11, BK-03 / BK-06, BM1-01, BM2-04, BP1-05, câu 10 / 11).
#   Nguồn: CLAUDE quét Fable 15/11; HÙNG 16/11 11:34 "ok làm đi" Gói 24. Dữ liệu: Stubs/HarnessG24.cs (POST /harness/g24-seed).
#   STEP P1 [PIN-HASH]      g24pin có PIN chữ rõ "1234" (dữ liệu cũ) → VerifyPinAsync đúng → ok + PIN tự nâng lên hash (PBKDF2.…); gõ lại 1234 vẫn ok, 9999 sai.
#                            Bản cũ: PIN còn chữ rõ sau khi gõ đúng → SAI là đúng.
#   STEP P2 [PIN-THROTTLE]  g24kho gõ PIN sai 5 lần → lần 6 gõ ĐÚNG vẫn bị từ chối, lời có "phút"; ResetAll → gõ đúng ok. Bản cũ: lần 6 ok → SAI là đúng.
#   STEP P3 [PIN-THROTTLE]  trang thật: admin mở /thiet-bi, sửa máy, nhập PIN sai 5 lần → lời khoá hiện trong cửa sổ PIN (chữ "phút"). (bản cũ: chỉ "PIN không đúng")
#   STEP L1 [LOGOUT-POST]   admin đăng nhập; GET /logout KHÔNG đăng xuất (vẫn vào /kh-plan); bấm nút "Đăng xuất" (form POST + AntiforgeryToken) → về /login,
#                            /kh-plan bị đưa về /login. Bản cũ: GET /logout đăng xuất → SAI là đúng.
#   STEP L2 [LOGOUT-POST]   POST /logout trống (không token, không cookie) → 302 về /login (token lệch không đăng xuất; không 404 / 405 / 200).
#   STEP U1 [UPLOADS-AUTH]  file wwwroot/uploads/g24.png: chưa đăng nhập GET → 401; đã đăng nhập (cookie Playwright) → 200. Bản cũ: 200 cả hai → SAI là đúng.
#   STEP K1 [KHO-NGAY-NHAN] /kho: SL thực nhận 5 + xoá trống ngày nhận → báo lỗi trước khi hỏi PIN (chữ "ngày nhận"); ngày nhận mai → báo lỗi. Bản cũ: lưu / hỏi PIN → SAI.
#   STEP C1 [KH-NGAY-TRONG] /kh-plan/create: xoá trống STD → "STD" trong lỗi; xoá trống Ngày lập PO → lỗi. Bản cũ: lưu PlanNo rác → SAI là đúng.
#   STEP C2 [KH-PART-RONG]  Part P-RONG24 (không công đoạn) → máy chủ CHẶN, lời có "công đoạn"; không có PO trong dump.
#   STEP C3 [KH-PART-CHUA-OK] Part P-NHAP24 (mục B chưa OK) → bấm 1: cảnh báo "chưa Xác nhận OK" + nút đổi "Vẫn lập Kế hoạch", chưa lưu; bấm 2 → lưu, dump có PO.
#   STEP E1 [EXCL-LOI]      /part-master/detail?partNo=P-NHAP24 → "Cài đặt" máy loại trừ → giả lỗi lưu (harness/fail save) → Lưu → cửa sổ CÒN mở + alert
#                            "Chưa lưu được danh sách máy loại trừ"; tắt giả lỗi → Lưu lại → đóng. Bản cũ: không alert → SAI là đúng.
#   STEP A1 [ADMIN-RESET-MK] /settings: dòng g24kho có nút "Đặt lại mật khẩu" (dòng admin đang đăng nhập KHÔNG có) → đặt "matkhau9" → g24-checkpw ok; 2 ô lệch → lỗi.
#   STEP M1 [BM1-01]        máy C24 Báo hỏng có 2 dòng sự cố mở (dữ liệu cũ); sửa tên (vẫn Báo hỏng) → vẫn 2 dòng mở (không thêm); về Hoạt động → 0 dòng mở.
#                            Bản cũ: 3 rồi 2 → SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_g24.py <port> <nhãn> [<thư mục bin2 của harness — để đặt ảnh thử wwwroot/uploads/g24.png>]
import sys, os, asyncio, json, urllib.request, urllib.error, urllib.parse
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]; ROOT = sys.argv[3] if len(sys.argv) > 3 else os.environ.get("HARNESS_ROOT", "")
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}
def status(path, method="GET"):
    try:
        r = urllib.request.urlopen(urllib.request.Request(BASE + path, data=(b"" if method == "POST" else None), method=method)); return r.status
    except urllib.error.HTTPError as e: return e.code
def status_noredir(path, method="POST"):   # không theo 302: trả (mã, Location)
    import http.client
    c = http.client.HTTPConnection("127.0.0.1", int(PORT)); c.request(method, path, body=b""); r = c.getresponse(); loc = r.getheader("Location") or ""; c.close()
    return r.status, loc
def q(s): return urllib.parse.quote(s)

BS = os.environ.get("KHO_BS", "/home/claude/bs_shared")
async def cdn(ctx):
    async def h(route):
        u = route.request.url
        f = BS + ("/bootstrap-icons-1.11.3/" + u.split("bootstrap-icons@1.11.3/")[1] if "bootstrap-icons@" in u else "/bootstrap-5.3.3/" + u.split("bootstrap@5.3.3/")[1])
        await (route.fulfill(path=f) if os.path.exists(f) else route.abort())
    await ctx.route("https://cdn.jsdelivr.net/npm/**", h)

async def login(ctx, user, pw, goto, wait=3000):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + goto); await pg.wait_for_timeout(wait)
    return pg

async def text(pg): return await pg.locator("body").inner_text()
async def set_val(pg, sel, val):   # gán giá trị + change (như người gõ xong)
    await pg.locator(sel).first.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }", val)
    await pg.wait_for_timeout(250)

async def main():
    seed = post("/harness/g24-seed")
    ok(seed.get("ok"), "Seed G24 (g24pin / g24kho / P-RONG24 / P-NHAP24 / máy C24)", seed)

    print("STEP P1 PIN-HASH — PIN chữ rõ gõ đúng → tự nâng hash")
    st = get("/harness/g24")
    ok(st["pinUser"] and not st["pinUser"]["pinHashed"], "P1 trước: g24pin PIN còn chữ rõ (dữ liệu cũ)", st["pinUser"])
    post("/harness/g24-pin-reset")
    r = post("/harness/g24-pin?user=g24pin&pin=1234")
    ok(r.get("ok"), "P1 gõ PIN 1234 (chữ rõ) → đúng", r)
    st = get("/harness/g24")
    ok(st["pinUser"]["pinHashed"] and st["pinUser"]["pinLen"] > 60, "P1 sau khi gõ đúng: PIN đã là hash PBKDF2 (dài > 60)", st["pinUser"])
    r = post("/harness/g24-pin?user=g24pin&pin=1234"); ok(r.get("ok"), "P1 gõ lại 1234 trên PIN hash → vẫn đúng", r)
    r = post("/harness/g24-pin?user=g24pin&pin=9999"); ok(not r.get("ok"), "P1 gõ 9999 → sai", r)

    print("STEP P2 PIN-THROTTLE — 5 lần sai → khoá 15 phút")
    post("/harness/g24-pin-reset")
    for i in range(5): post("/harness/g24-pin?user=g24kho&pin=0000")
    r = post("/harness/g24-pin?user=g24kho&pin=2222")
    ok(not r.get("ok") and "phút" in (r.get("error") or ""), "P2 lần 6 gõ ĐÚNG 2222 vẫn bị từ chối, lời có 'phút'", r)
    rr = post("/harness/g24-pin-reset")
    r = post("/harness/g24-pin?user=g24kho&pin=2222")
    ok(rr.get("ok") and r.get("ok"), "P2 ResetAll → gõ đúng → ok (và PIN g24kho đã nâng hash)", (rr, r, get("/harness/g24")["khoUser"]))
    st_before = get("/harness/g24")

    # ảnh thử /uploads
    has_img = False
    if ROOT:
        up = os.path.join(ROOT, "wwwroot", "uploads")
        try:
            os.makedirs(up, exist_ok=True)
            with open(os.path.join(up, "g24.png"), "wb") as f: f.write(b"\x89PNG\r\n\x1a\n" + b"\0" * 32)
            has_img = True
        except Exception as ex: print("  (không tạo được ảnh thử:", ex, ")")
    else: print("  (không có tham số thư mục harness → bỏ ca 'đã đăng nhập → 200')")

    print("STEP U1 UPLOADS-AUTH — chưa đăng nhập 401")
    ok(status("/uploads/g24.png") == 401, "U1 GET /uploads/g24.png chưa đăng nhập → 401", status("/uploads/g24.png"))
    ok(status("/uploads/g24.txt") in (401, 404), "U1 /uploads/g24.txt (không phải ảnh) → 401 / 404", status("/uploads/g24.txt"))
    ok(status("/css/app.css") == 200 or status("/app.css") == 200, "U1 file tĩnh khác (css) vẫn 200 không cần đăng nhập", (status("/css/app.css"), status("/app.css")))

    print("STEP L2 LOGOUT-POST — POST trống (không token) → 302 /login, không 404 / 405")
    s = status_noredir("/logout"); ok(s[0] == 302 and "/login" in s[1], "L2 POST /logout không token → 302 về /login (bản cũ: 404 / 405)", s)

    async with async_playwright() as p:
        br = await p.chromium.launch()

        print("STEP L1 LOGOUT-POST — GET không đăng xuất, nút Đăng xuất (form POST) mới đăng xuất")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kh-plan")
        ok("/login" not in pg.url, "L1 admin đăng nhập, mở /kh-plan", pg.url)
        if has_img:
            rs = await ctx.request.get(BASE + "/uploads/g24.png")
            ok(rs.status == 200, "U1 đã đăng nhập → /uploads/g24.png 200", rs.status)
        await pg.goto(BASE + "/logout"); await pg.wait_for_timeout(1200)
        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(2500)
        ok("/login" not in pg.url, "L1 GET /logout rồi mở /kh-plan: VẪN đăng nhập (GET không đăng xuất)", pg.url)
        frm = pg.locator("form[action='/logout']")
        ok(await frm.count() == 1 and await frm.locator("input[name='__RequestVerificationToken']").count() == 1, "L1 nút Đăng xuất là form POST có AntiforgeryToken", await frm.count())
        if await frm.count():
            await frm.locator("button[type=submit]").click(); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(800)
        else:   # bản cũ: link GET
            await pg.goto(BASE + "/logout"); await pg.wait_for_timeout(1000)
        ok("/login" in pg.url, "L1 bấm Đăng xuất → về /login", pg.url)
        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(2000)
        ok("/login" in pg.url, "L1 sau đăng xuất mở /kh-plan → bị đưa về /login", pg.url)
        rs = await ctx.request.get(BASE + "/uploads/g24.png")
        ok(rs.status == 401, "U1 sau đăng xuất → /uploads 401", rs.status)
        await ctx.close()

        print("STEP K1 KHO-NGAY-NHAN — SL > 0 phải có ngày; ngày không quá hôm nay")
        ks = post("/harness/kho-seed"); k = get("/harness/kho")
        po_rows = [r for r in k["kho"] if r["isActive"]]
        pid = po_rows[0]["planDetailId"] if po_rows else None
        po = next((x["purchaseOrder"] for x in k["po"] if x["id"] == pid), None)
        ok(po is not None, "K1 có PO Kho để sửa", (ks, list(k.keys())))
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kho")
        await pg.locator(f"#kho-table tr:has(td[title='{po}']) button[title='Cập nhật thông tin Kho']").click(); await pg.wait_for_timeout(700)
        await set_val(pg, ".modal-content input[placeholder^='Nhập SL thực nhận']", "5")
        await set_val(pg, ".modal-content input[type=date]", "")
        await pg.click(".modal-content button:has-text('Lưu dữ liệu kho')"); await pg.wait_for_timeout(1200)
        err = pg.locator(".modal-content .alert-danger"); t = (await err.first.inner_text()) if await err.count() else ""
        ok("ngày nhận" in t.lower() and await pg.locator(".modal-content").count() == 1, "K1 SL 5 + ngày trống → báo lỗi 'ngày nhận' ngay trong cửa sổ (chưa lưu)", t)
        import datetime
        tomorrow = (datetime.date.today() + datetime.timedelta(days=1)).isoformat()
        await set_val(pg, ".modal-content input[type=date]", tomorrow)
        await pg.click(".modal-content button:has-text('Lưu dữ liệu kho')"); await pg.wait_for_timeout(1200)
        t = (await err.first.inner_text()) if await err.count() else ""
        ok("hôm nay" in t.lower() or "tương lai" in t.lower() or "quá" in t.lower(), "K1 ngày nhận = mai → báo lỗi", t)
        await ctx.close()

        print("STEP C1-C3 Tạo KH — ngày trống / Part rỗng / Part chưa OK")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kh-plan/create")
        ok("/kh-plan/create" in pg.url, "C1 mở /kh-plan/create", pg.url)
        async def form(po, part, qty="3"):   # trang Tạo KH mới tinh, 1 dòng Part
            if "/kh-plan/create" not in pg.url or True:
                await pg.goto(BASE + "/kh-plan/create"); await pg.wait_for_timeout(2500)
            await pg.locator("select.form-select").first.select_option(index=1); await pg.wait_for_timeout(300)
            await set_val(pg, "input[maxlength='50'].font-monospace", po)
            await pg.locator("input[list='partmaster-list']").first.fill(part); await pg.wait_for_timeout(600)
            await set_val(pg, "input[type=number][min='1']", qty)
        async def clear_date(n):
            await pg.locator("input[type=date]").nth(n).evaluate("e => { e.value = ''; e.dispatchEvent(new Event('change', {bubbles: true})); }"); await pg.wait_for_timeout(300)
        async def save():
            await pg.click("button:has-text('Xác nhận Kế hoạch'), button:has-text('Vẫn lập Kế hoạch')"); await pg.wait_for_timeout(2500)
            return await text(pg)
        # [0] Ngày lập PO · [1] Ngày nhận PO · [2] STD dòng 1
        await form("PO-G24-STD", "P-NHAP24"); await clear_date(2); t = await save()
        ok("STD" in t and "/kh-plan/create" in pg.url and "PO-G24-STD" not in get("/harness/g24")["pos"], "C1 STD xoá trống → lỗi nêu STD, không lưu", (pg.url, t[:300]))
        await form("PO-G24-PD", "P-NHAP24"); await clear_date(0); t = await save()
        ok("Ngày lập PO" in t and "/kh-plan/create" in pg.url and "PO-G24-PD" not in get("/harness/g24")["pos"], "C1 Ngày lập PO xoá trống → lỗi, không lưu (PlanNo không rác)", (pg.url, t[:300]))
        await form("PO-G24-RONG", "P-RONG24"); t = await save()
        ok("công đoạn" in t and "/kh-plan/create" in pg.url, "C2 Part P-RONG24 không công đoạn → máy chủ chặn, lời có 'công đoạn'", (pg.url, t[:400]))
        pos = get("/harness/g24")["pos"]
        ok("PO-G24-RONG" not in pos, "C2 dump không có PO-G24-RONG", pos)
        await form("PO-G24-NHAP", "P-NHAP24"); t = await save()
        btn = pg.locator("button:has-text('Vẫn lập Kế hoạch')")
        ok("chưa Xác nhận OK" in t and await btn.count() == 1 and "/kh-plan/create" in pg.url, "C3 bấm 1: cảnh báo Part chưa Xác nhận OK + nút 'Vẫn lập Kế hoạch', chưa lưu", (pg.url, t[:400]))
        ok("PO-G24-NHAP" not in get("/harness/g24")["pos"], "C3 bấm 1 chưa có PO trong dump")
        if await btn.count(): await btn.click(); await pg.wait_for_timeout(3000)
        pos = get("/harness/g24")["pos"]
        ok("PO-G24-NHAP" in pos, "C3 bấm 2 'Vẫn lập Kế hoạch' → đã lưu PO-G24-NHAP", (pg.url, pos))
        await ctx.close()

        print("STEP E1 EXCL-LOI — lỗi lưu máy loại trừ hiện trong cửa sổ")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/part-master/detail?partNo=P-NHAP24", 4000)
        b = pg.locator("button[title='Cài đặt danh sách máy loại trừ (toàn hệ thống)']")
        ok(await b.count() == 1, "E1 có nút Cài đặt máy loại trừ", await b.count())
        if await b.count(): await b.click(); await pg.wait_for_timeout(800)
        opened = await pg.locator(".modal-content:has-text('Cài đặt máy loại trừ')").count() == 1
        ok(opened, "E1 cửa sổ mở")
        post("/harness/fail?kind=save&n=1")
        if opened: await pg.locator(".modal-content button.btn-danger:has-text('Lưu')").click(); await pg.wait_for_timeout(1500)
        al = pg.locator(".modal-content .alert-danger"); t = (await al.first.inner_text()) if await al.count() else ""
        ok("Chưa lưu được danh sách máy loại trừ" in t and "GIA-LOI" not in t and await pg.locator(".modal-content:has-text('Cài đặt máy loại trừ')").count() == 1,
           "E1 giả lỗi lưu → cửa sổ CÒN mở + alert 'Chưa lưu được…' (không lộ lời SQL)", (t, await pg.locator('.modal-content').count()))
        post("/harness/fail?kind=off&n=0")
        if await pg.locator(".modal-content button.btn-danger:has-text('Lưu')").count(): await pg.locator(".modal-content button.btn-danger:has-text('Lưu')").click(); await pg.wait_for_timeout(1500)
        ok(await pg.locator(".modal-content:has-text('Cài đặt máy loại trừ')").count() == 0, "E1 tắt giả lỗi → Lưu → cửa sổ đóng (bản EF không kẹt lệnh lỗi)")
        await ctx.close()

        print("STEP A1 ADMIN-RESET-MK — Admin đặt lại mật khẩu người khác")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/settings", 3500)
        await pg.click("button.nav-link:has-text('Danh sách User')"); await pg.wait_for_timeout(800)
        row = pg.locator("tr:has(td:text-is('g24kho'))")
        if await row.count() == 0: row = pg.locator("tr:has-text('g24kho')")
        rb = row.locator("button:has-text('Đặt lại mật khẩu')")
        ok(await rb.count() == 1, "A1 dòng g24kho có nút Đặt lại mật khẩu", await rb.count())
        if await rb.count() == 0:
            ok(False, "A1 bản cũ không có nút → bỏ các bước sau của A1"); await ctx.close()
        else:
          if True:
            me = pg.locator("tr:has-text('admin') button:has-text('Đặt lại mật khẩu')")
            ok(await pg.locator("tr:has(td:text-is('admin')) button:has-text('Đặt lại mật khẩu')").count() == 0, "A1 dòng admin (chính mình) KHÔNG có nút")
            await rb.first.click(); await pg.wait_for_timeout(700)
            md = pg.locator(".mes-modal:has-text('Đặt lại mật khẩu')")
            ok(await md.count() == 1, "A1 cửa sổ đặt lại mở")
            await set_val(pg, ".mes-modal input[placeholder='Mật khẩu mới...']", "matkhau9")
            await set_val(pg, ".mes-modal input[placeholder='Gõ lại...']", "khacnhau")
            await md.locator("button.btn-danger:has-text('Đặt lại mật khẩu')").click(); await pg.wait_for_timeout(1000)
            t = (await md.locator(".alert-danger").first.inner_text()) if await md.locator(".alert-danger").count() else ""
            ok(t != "" and not post("/harness/g24-checkpw?user=g24kho&pw=matkhau9")["ok"], "A1 2 ô lệch → lỗi, mật khẩu chưa đổi", t)
            await set_val(pg, ".mes-modal input[placeholder='Gõ lại...']", "matkhau9")
            await md.locator("button.btn-danger:has-text('Đặt lại mật khẩu')").click(); await pg.wait_for_timeout(1500)
            ok(post("/harness/g24-checkpw?user=g24kho&pw=matkhau9")["ok"] and not post("/harness/g24-checkpw?user=g24kho&pw=123456")["ok"], "A1 đặt 'matkhau9' → đăng nhập bằng mật khẩu mới được, cũ không", await text(pg))
            await ctx.close()

        print("STEP P3 PIN-THROTTLE trang thật — cửa sổ PIN Thiết bị báo khoá")
        post("/harness/g24-pin-reset")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "g24pin", "123456", "/thiet-bi", 3500)
        rowm = pg.locator("tr:has-text('C24')")
        eb = rowm.locator("button[title*='Sửa'], button:has(i.bi-pencil)").first
        has = await eb.count() > 0
        ok(has, "P3 có nút sửa máy C24", await rowm.count())
        last = ""
        if has:
            await eb.click(); await pg.wait_for_timeout(800)
            for i in range(6):
                pin = pg.locator(".modal-content input[type=password]")
                if await pin.count() == 0: break
                await set_val(pg, ".modal-content input[type=password]", "0000")
                await pg.locator(".modal-content button:has-text('Lưu thay đổi')").click(); await pg.wait_for_timeout(900)
                al = pg.locator(".modal-content div.text-danger.fw-bold")
                last = (await al.first.inner_text()) if await al.count() else ""
            ok("phút" in last, "P3 sau 5 lần PIN sai, lần 6 báo khoá có 'phút' trong cửa sổ", last)
        await ctx.close()
        await br.close()

    print("STEP M1 BM1-01 — sổ sự cố máy: không thêm dòng mở khi đang dừng; về Hoạt động chốt hết")
    post("/harness/g24-pin-reset")
    st = get("/harness/g24"); ok(st["may"] and st["may"]["open"] == 2 and st["may"]["trangThai"] == "Báo hỏng", "M1 trước: C24 Báo hỏng, 2 dòng mở (dữ liệu cũ)", st["may"])
    r = post("/harness/g24-tb?status=" + q("Báo hỏng") + "&name=" + q("May C24 doi ten"))
    ok(r.get("ok") and r.get("open") == 2, "M1 sửa tên khi đang Báo hỏng → vẫn 2 dòng mở (không thêm dòng)", r)
    r = post("/harness/g24-tb?status=" + q("Hoạt động"))
    ok(r.get("ok") and r.get("open") == 0 and r.get("trangThai") == "Hoạt động", "M1 về Hoạt động → chốt MỌI dòng mở (0 còn mở)", r)

    print(f"KẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
