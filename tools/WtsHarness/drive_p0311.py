# [PHAT-HIEN-0311 04/11/2026] Bấm trang thật (harness EF giả): Cài đặt ca làm việc / Sơ đồ tổ chức / DS Lỗi — quyền đọc DB, máy chủ kiểm lại.
#   Dữ liệu: POST /harness/p0311-seed (xem0311 = chỉ Xem 3 mục, sua0311 = Sửa 3 mục, mật khẩu 123456, PIN 1111; Ca 1 đã có WTS; "Ca thử xoá" chưa dùng;
#   Sơ đồ Giám đốc + Sản xuất). Đọc DB: GET /harness/p0311. Luật máy chủ kiểm kỹ ở GiaoNhanFuzz auth U8 / U9 / U10.
#   C1 người chỉ Xem: nút Thêm / Sửa / Ẩn / Xoá ca đều khoá · C2 người Sửa: Ẩn ca rồi Sửa ngay ca đó → lưu, ca vẫn Ẩn
#   C3 Xoá Ca 1 (đã có WTS) → hộp xác nhận báo "đã được dùng", ca còn · C4 Xoá ca chưa dùng → mất khỏi DB
#   S1 gc01 (không quyền SO_DO_TC) → báo không có quyền Xem, không thấy sơ đồ · S2 người chỉ Xem: thấy sơ đồ, nút Thêm khoá, bấm khối → báo không quyền Sửa
#   S3 người Sửa tải x.html → chặn, không ghi file · S4 HTML đổi tên .png → chặn · S5 tải .png thật + PIN → lưu, ảnh mới vào DB
#   D1 người chỉ Xem DS Lỗi: không có nút Thêm loại lỗi · D2 người Sửa: có nút
# Chạy: python3 drive_p0311.py <port> <nhãn>   (harness VỪA khởi động, dựng bằng mk.sh)
import sys, asyncio, json, os, tempfile, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if (not cond and info not in (None, "")) else ""))
def http(path, post=False):
    req = urllib.request.Request(BASE + path, data=b"" if post else None, method="POST" if post else "GET")
    with urllib.request.urlopen(req, timeout=30) as r: return json.loads(r.read().decode())
def ca_of(d, ten): return next((c for c in d["ca"] if c["tenCa"] == ten), None)

async def login(b, user, pw):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1600, "height": 1100})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def go(pg, path, wait=4000):
    await pg.goto(BASE + path); await pg.wait_for_timeout(wait)   # trang Blazor nạp quyền xong mới đọc (bài học 01/11)
    return await pg.inner_text("body")
def card(pg, ten): return pg.locator("div.ca-card", has=pg.locator("h6", has_text=ten)).first

async def main():
    tmp = tempfile.mkdtemp()
    fhtml = os.path.join(tmp, "x.html"); open(fhtml, "wb").write(b"<html><script>alert(1)</script></html>")
    ffake = os.path.join(tmp, "gia.png"); open(ffake, "wb").write(b"<html><script>alert(2)</script></html>")
    fpng = os.path.join(tmp, "that.png"); open(fpng, "wb").write(bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52]))
    d0 = http("/harness/p0311-seed", post=True)
    async with async_playwright() as p:
        b = await p.chromium.launch()
        try:
            # ── Ca làm việc ──
            px = await login(b, "xem0311", "123456")
            t = await go(px, "/cai-dat/ca-lam-viec")
            btns = card(px, "Ca thử xoá").locator(".card-footer button")
            dis = [await btns.nth(i).is_disabled() for i in range(await btns.count())]
            add_dis = await px.locator("button:has-text('Thêm ca mới')").is_disabled()
            ok("Ca thử xoá" in t and len(dis) == 3 and all(dis) and add_dis, "C1 người chỉ Xem mở trang ca: thấy ca, nút Thêm / Sửa / Ẩn / Xoá đều khoá", f"{dis} thêm khoá={add_dis}")

            ps = await login(b, "sua0311", "123456")
            await go(ps, "/cai-dat/ca-lam-viec")
            await card(ps, "Ca thử xoá").locator(".card-footer button").nth(1).click(); await ps.wait_for_timeout(1200)
            d = http("/harness/p0311"); c = ca_of(d, "Ca thử xoá")
            ok(c is not None and c["isActive"] is False, "C2a người Sửa bấm Ẩn 'Ca thử xoá' → DB tắt", c)
            await card(ps, "Ca thử xoá").locator(".card-footer button").nth(0).click(); await ps.wait_for_timeout(800)
            await ps.locator(".modal-body input.form-control").first.fill("Ca thử xoá 2"); await ps.wait_for_timeout(200)
            await ps.locator(".modal-footer button:has-text('Lưu')").click(); await ps.wait_for_timeout(1500)
            t = await ps.inner_text("body"); d = http("/harness/p0311"); c = ca_of(d, "Ca thử xoá 2")
            ok(c is not None and c["isActive"] is False and "Sửa ca làm việc" not in t, "C2b Ẩn xong Sửa ngay ca đó: lưu tên mới, ca vẫn Ẩn, hộp sửa đóng", [c, t[:200]])

            await card(ps, "Ca 1").locator(".card-footer button").nth(2).click(); await ps.wait_for_timeout(600)
            await ps.locator(".modal-footer button.btn-danger").click(); await ps.wait_for_timeout(1200)
            t = await ps.inner_text("body"); d = http("/harness/p0311")
            ok(ca_of(d, "Ca 1") is not None and "đã được dùng" in t and "bấm Ẩn" in t, "C3 Xoá 'Ca 1' (đã có dòng WTS) → hộp xác nhận báo đã được dùng, gợi ý Ẩn; ca còn trong DB",
               [l for l in t.split("\n") if "dùng" in l][:3])
            if await ps.locator(".modal-footer button:has-text('Hủy')").count(): await ps.locator(".modal-footer button:has-text('Hủy')").click(); await ps.wait_for_timeout(500)
            await card(ps, "Ca thử xoá 2").locator(".card-footer button").nth(2).click(); await ps.wait_for_timeout(600)
            await ps.locator(".modal-footer button.btn-danger").click(); await ps.wait_for_timeout(1200)
            d = http("/harness/p0311")
            ok(ca_of(d, "Ca thử xoá 2") is None and ca_of(d, "Ca 1") is not None, "C4 Xoá ca chưa ai dùng → mất khỏi DB (như cũ)", [c["tenCa"] for c in d["ca"]])

            # ── Sơ đồ tổ chức ──
            pg = await login(b, "gc01", "123456")
            t = await go(pg, "/so-do-to-chuc")
            ok("không có quyền Xem mục Sơ đồ tổ chức" in t and "Anh A" not in t, "S1 gc01 (không quyền SO_DO_TC) gõ URL → báo không có quyền Xem, không thấy sơ đồ", t[:200])
            t = await go(px, "/so-do-to-chuc")
            add_dis = await px.locator("button:has-text('Thêm vị trí mới')").is_disabled()
            ok("Anh A" in t and add_dis, "S2a người chỉ Xem: thấy sơ đồ, nút Thêm vị trí mới khoá", f"thêm khoá={add_dis}")
            await px.locator(".org-node", has_text="Giám đốc").first.click(); await px.wait_for_timeout(800)
            t = await px.inner_text("body")
            ok("không có quyền Sửa mục Sơ đồ tổ chức" in t and await px.locator("input[type=password]").count() == 0, "S2b người chỉ Xem bấm khối Giám đốc → báo không có quyền Sửa, không mở form sửa", t[:300])

            await go(ps, "/so-do-to-chuc")
            await ps.locator(".org-node", has_text="Giám đốc").first.click(); await ps.wait_for_timeout(800)
            n0 = len(http("/harness/p0311")["avatarFiles"])
            await ps.locator("input[type=file]").first.set_input_files(fhtml); await ps.wait_for_timeout(1500)
            t = await ps.inner_text("body"); n1 = len(http("/harness/p0311")["avatarFiles"])
            ok("chỉ nhận file .jpg, .jpeg, .png hoặc .webp" in t and n1 == n0, "S3 người Sửa tải x.html làm ảnh đại diện → chặn, không ghi file", [n0, n1, [l for l in t.split("\n") if "nhận" in l][:2]])
            await ps.locator("input[type=file]").first.set_input_files(ffake); await ps.wait_for_timeout(1500)
            t = await ps.inner_text("body"); n2 = len(http("/harness/p0311")["avatarFiles"])
            ok("không khớp đuôi" in t and n2 == n0, "S4 file HTML đổi tên thành gia.png → chặn (nội dung không phải ảnh), không ghi file", [n2, [l for l in t.split("\n") if "khớp" in l][:2]])
            await ps.locator("input[type=file]").first.set_input_files(fpng); await ps.wait_for_timeout(1500)
            n3 = len(http("/harness/p0311")["avatarFiles"])
            await ps.locator("input[placeholder*='Bổ nhiệm mới']").fill("Thay ảnh"); await ps.locator("input[type=password]").fill("1111"); await ps.wait_for_timeout(200)
            await ps.locator(".modal-footer button.btn-primary").click(); await ps.wait_for_timeout(1500)
            d = http("/harness/p0311"); g = next(x for x in d["dept"] if x["departmentId"] == 1)
            ok(n3 == n0 + 1 and (g["avatarUrl"] or "").startswith("/uploads/avatars/") and g["avatarUrl"].endswith(".png") and d["deptLog"] >= 1,
               "S5 tải ảnh .png thật + PIN → lưu, ảnh mới vào DB, có lịch sử", [n3, g, d["deptLog"]])

            # S6 [UPLOAD-STATIC] /uploads chỉ phát ảnh: ảnh vừa tải → 200 + nosniff; file .html cũ (tải lên trước bản sửa) → 404
            import urllib.error
            def st(path):
                try:
                    with urllib.request.urlopen(BASE + path, timeout=20) as r: return r.status, r.headers.get("X-Content-Type-Options")
                except urllib.error.HTTPError as e: return e.code, None
            hdir = os.environ.get("HARNESS_BIN")   # thư mục bin2 của harness (runh.sh đặt) — chép 1 file .html "cũ" vào uploads
            if hdir:
                os.makedirs(os.path.join(hdir, "wwwroot", "uploads", "avatars"), exist_ok=True)
                open(os.path.join(hdir, "wwwroot", "uploads", "avatars", "cu_doc.html"), "wb").write(b"<script>alert(3)</script>")
            s_png = st(g["avatarUrl"] or "/x.png"); s_html = st("/uploads/avatars/cu_doc.html"); s_js = st("/uploads/avatars/a.js")
            ok(s_png == (200, "nosniff") and s_html[0] == 404 and s_js[0] == 404 and hdir is not None,
               "S6 /uploads: ảnh .png → 200 + nosniff; file .html có sẵn trong thư mục / .js → 404", [s_png, s_html, s_js, hdir])

            # S7 [SODO-NULL] form Sửa → nút Lịch sử mở lịch sử (trước: NullReference, trang chết); khung xem ảnh → Đổi ảnh mở form Sửa
            await go(ps, "/so-do-to-chuc")
            await ps.locator(".org-node", has_text="Giám đốc").first.click(); await ps.wait_for_timeout(800)
            await ps.locator(".modal-footer button:has-text('Lịch sử')").click(); await ps.wait_for_timeout(1500)
            t = await ps.inner_text("body")
            ok("Lịch sử điều động: Giám đốc" in t, "S7a form Sửa → bấm Lịch sử: mở lịch sử, trang không chết", t[:200])
            await ps.locator(".modal-header .btn-close").first.click(); await ps.wait_for_timeout(500)
            await ps.locator(".org-avatar-container").first.click(); await ps.wait_for_timeout(800)
            await ps.locator("button:has-text('Đổi ảnh / Cập nhật')").click(); await ps.wait_for_timeout(1000)
            ok(await ps.locator("input[type=password]").count() == 1 and await ps.locator("input[placeholder*='Bổ nhiệm mới']").count() == 1,
               "S7b khung xem ảnh → Đổi ảnh / Cập nhật: mở form Sửa (trước: NullReference)")

            # ── DS Lỗi ──
            t = await go(px, "/ds-loi")
            ok("KT-SAI" in t and await px.locator("button:has-text('Thêm loại lỗi')").count() == 0, "D1 người chỉ Xem DS Lỗi: thấy danh mục, không có nút Thêm loại lỗi", t[:200])
            t = await go(ps, "/ds-loi")
            ok(await ps.locator("button:has-text('Thêm loại lỗi')").count() >= 1, "D2 người Sửa DS Lỗi: có nút Thêm loại lỗi", t[:200])
            await ps.screenshot(path=f"p0311_{OUT}.png", full_page=False)
        except Exception as e:
            ok(False, "!! dừng giữa chừng — lỗi bấm trang", repr(e)[:300])
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
