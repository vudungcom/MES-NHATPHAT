# [USER-PERM-SVC 03/11/2026] Bấm trang Cài đặt & Phân quyền thật (/settings, harness EF giả): trang đọc quyền ADMIN từ DB
#   (không claim) và truyền người thao tác cho UserService (máy chủ tự kiểm ADMIN). Luật máy chủ kiểm ở GiaoNhanFuzz auth U6.
#   G1 gc01 (không phải ADMIN) mở trang → "Truy cập bị từ chối" · G2 admin thấy danh sách người dùng
#   G3 admin sửa họ tên gc01 → lưu được, tải lại thấy tên mới · G4 admin Lưu ma trận phân quyền → báo thành công
# Chạy: python3 drive_settings.py <port> <nhãn>   (harness dựng bằng mk.sh)
import sys, asyncio
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if (not cond and info not in (None, "")) else ""))

async def login(b, user, pw):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1600, "height": 1100})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_settings(pg, users=False):
    await pg.goto(BASE + "/settings"); await pg.wait_for_timeout(3000)
    if users and await pg.locator("text=Danh sách User").count():   # thẻ mặc định là Ma trận Phân quyền
        await pg.locator("text=Danh sách User").first.click(); await pg.wait_for_timeout(800)
    return await pg.inner_text("body")

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        try:
            pg = await login(b, "gc01", "123456")
            t = await open_settings(pg)
            ok("Truy cập bị từ chối" in t and "Lưu thông tin" not in t, "G1 gc01 (nhóm Gia công) mở /settings → Truy cập bị từ chối", t[:200])
            pa = await login(b, "admin", "admin123")
            t = await open_settings(pa, users=True)
            ok("Cài đặt & Phân quyền" in t and "gc01" in t, "G2 admin mở /settings → thấy danh sách người dùng (quyền đọc DB)", t[:200])
            row = pa.locator("tr", has=pa.locator("td", has_text="gc01")).first
            await row.locator("button[title='Sửa thông tin']").click(); await pa.wait_for_timeout(500)
            await pa.locator("input[placeholder='VD: Nguyễn Văn A...']").fill("Cong nhan GC sua"); await pa.wait_for_timeout(200)
            await pa.locator("button:has-text('Lưu thông tin')").click(); await pa.wait_for_timeout(1500)
            t = await pa.inner_text("body")
            ok("Lưu thông tin người dùng thành công" in t, "G3 admin sửa họ tên gc01 → lưu được (máy chủ nhận đúng người thao tác)", [l for l in t.split("\n") if "Lỗi" in l or "ADMIN" in l][:4])
            t = await open_settings(pa, users=True)
            ok("Cong nhan GC sua" in t, "G3b tải lại trang thấy họ tên mới", t[:200])
            await open_settings(pa)
            await pa.locator("button:has-text('Lưu Ma Trận')").click()
            await pa.wait_for_timeout(1500)
            t = await pa.inner_text("body")
            ok("Đã lưu ma trận phân quyền thành công" in t, "G4 admin bấm Lưu ma trận phân quyền → báo thành công", [l for l in t.split("\n") if "Lỗi" in l or "ma trận" in l][:4])
            # [SET-MATRIX-1TX 05/11] (PHAT-HIEN-0311 mục 5) ma trận không còn cột "Cài đặt & Phân quyền" cho nhóm thường; cột khác vẫn có
            heads = await pa.locator("thead th").all_inner_texts()
            ok(not any("Cài đặt & Phân quyền" in h for h in heads) and any("Giao nhận: Gia công" in h for h in heads) and any("Cài đặt ca làm việc" in h for h in heads),
               "G5 ma trận không còn cột \"Cài đặt & Phân quyền\" (trang Cài đặt chỉ ADMIN); cột Giao nhận / Ca làm việc vẫn có", heads[:6])
            # [SET-POPUP-ERR 05/11] (mục 4) lỗi lưu hiện ngay trong popup, không bị popup che
            await open_settings(pa, users=True)
            await pa.locator("button:has-text('Thêm User')").click(); await pa.wait_for_timeout(500)
            await pa.locator("button:has-text('Lưu thông tin')").click(); await pa.wait_for_timeout(800)
            pop = await pa.locator(".mes-modal").inner_text() if await pa.locator(".mes-modal").count() else ""
            ok("Vui lòng nhập đủ" in pop, "G6 Thêm User bỏ trống bấm Lưu → lời báo hiện TRONG popup (trước: ở đầu trang, bị popup che)", pop[:200])
            await pa.locator("input[placeholder='Nhập username viết liền không dấu...']").fill("gc01")
            await pa.locator("input[placeholder='VD: Nguyễn Văn A...']").fill("Trung ten")
            await pa.locator("input[placeholder='Mật khẩu...']").fill("123456")
            await pa.locator(".mes-modal select").select_option(index=1); await pa.wait_for_timeout(300)
            await pa.locator("button:has-text('Lưu thông tin')").click(); await pa.wait_for_timeout(1200)
            pop = await pa.locator(".mes-modal").inner_text() if await pa.locator(".mes-modal").count() else ""
            ok(pop != "" and "Lưu thông tin" in pop and ("tồn tại" in pop or "trùng" in pop or "đã có" in pop),
               "G7 Thêm User trùng tên gc01 → popup vẫn mở, lời báo máy chủ hiện trong popup", pop[:250])
            await pa.screenshot(path=f"settings_{OUT}.png", full_page=False)
        except Exception as e:
            ok(False, "G! dừng giữa chừng — lỗi bấm trang", repr(e)[:300])
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
