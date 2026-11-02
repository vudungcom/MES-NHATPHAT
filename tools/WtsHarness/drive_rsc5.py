# [RS-C5 02/11/2026] Trang đăng nhập thật: "Nhớ tên đăng nhập" CHỈ nhớ tên — không lưu mật khẩu trong trình duyệt
#   (máy dùng chung là lộ); mật khẩu bản cũ đã lưu (mes_saved_password) bị xoá ngay khi mở trang.
# Chạy: python3 drive_rsc5.py <port> <nhãn>   (harness dựng bằng mk.sh, đang chạy ở <port>)
import sys, asyncio
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2] if len(sys.argv) > 2 else "new"
BASE = f"http://127.0.0.1:{PORT}"
PW = "admin123"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""))

async def storage(pg):
    return await pg.evaluate("() => { const o = {}; for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); o[k] = localStorage.getItem(k); } return o; }")
async def session(pg):
    return await pg.evaluate("() => { const o = {}; for (let i = 0; i < sessionStorage.length; i++) { const k = sessionStorage.key(i); o[k] = sessionStorage.getItem(k); } return o; }")

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page()
        # R1 bản cũ đã lưu mật khẩu → mở trang đăng nhập: xoá mật khẩu, điền sẵn tên
        await pg.goto(BASE + "/login"); await pg.wait_for_load_state("networkidle")
        await pg.evaluate("() => { localStorage.setItem('mes_saved_username', 'admin'); localStorage.setItem('mes_saved_password', 'matkhaucu'); localStorage.setItem('mes_remember_me', '1'); }")
        await pg.reload(); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(300)
        st = await storage(pg)
        ok("mes_saved_password" not in st and "matkhaucu" not in st.values(), "R1 mật khẩu bản cũ để lại trong trình duyệt: mở trang đăng nhập là xoá", st)
        u = await pg.input_value("#username"); pw = await pg.input_value("#password"); cb = await pg.is_checked("#rememberMe")
        ok(u == "admin" and pw == "" and cb, "R1 tên đăng nhập điền sẵn 'admin', ô mật khẩu TRỐNG, ô Nhớ đã tích", f"tên '{u}' mật khẩu '{pw}' nhớ {cb}")
        lbl = (await pg.inner_text("label[for=rememberMe]")).strip()
        ok(lbl == "Nhớ tên đăng nhập", "R1 nhãn ô tích nói rõ chỉ nhớ TÊN", lbl)
        # R2 tích Nhớ + đăng nhập → chỉ tên được lưu
        await pg.fill("#password", PW)
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(500)
        st = await storage(pg); ss = await session(pg)
        leak = [k for k, v in list(st.items()) + list(ss.items()) if v is not None and PW in v]
        ok("/login" not in pg.url, "R2 đăng nhập được", pg.url)
        ok(st.get("mes_saved_username") == "admin" and st.get("mes_remember_me") == "1", "R2 tích Nhớ: lưu tên 'admin' + cờ nhớ", st)
        ok(not leak and "mes_saved_password" not in st, "R2 KHÔNG có giá trị nào chứa mật khẩu trong localStorage / sessionStorage", leak or st)
        # R3 đăng xuất → mở lại: tên điền sẵn, mật khẩu trống
        await pg.goto(BASE + "/logout"); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(300)
        if "/login" not in pg.url: await pg.goto(BASE + "/login"); await pg.wait_for_load_state("networkidle")
        u = await pg.input_value("#username"); pw = await pg.input_value("#password")
        ok(u == "admin" and pw == "", "R3 đăng xuất rồi mở lại: tên điền sẵn, mật khẩu trống", f"tên '{u}' mật khẩu '{pw}'")
        # R4 bỏ tích Nhớ + đăng nhập → xoá tên đã nhớ
        await pg.uncheck("#rememberMe"); await pg.fill("#password", PW)
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(500)
        st = await storage(pg)
        ok("mes_saved_username" not in st and "mes_remember_me" not in st and "mes_saved_password" not in st, "R4 bỏ tích Nhớ rồi đăng nhập: xoá tên đã nhớ, không còn khoá nào của trang đăng nhập", st)
        await pg.goto(BASE + "/logout"); await pg.wait_for_load_state("networkidle")
        if "/login" not in pg.url: await pg.goto(BASE + "/login"); await pg.wait_for_load_state("networkidle")
        u = await pg.input_value("#username")
        ok(u == "", "R4 mở lại trang đăng nhập: ô tên trống", u)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)
asyncio.run(main())
