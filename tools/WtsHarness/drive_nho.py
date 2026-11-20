# [GÓI NHỎ 20/11/2026] drive_nho.py — bấm trang thật cho gói nhỏ ghi nhớ G23 / G25. Nguồn: CLAUDE rà độc lập Gói 23 (L2, L3) + Gói 25 (R-09); Hùng 20/11 18:12 / 18:32.
#   H1 [HOME-THE-QUYEN] Trang chủ: admin thấy đủ 9 thẻ; g23xem (chỉ Xem Giao nhận Kho) chỉ còn thẻ "Hiệu suất công nhân"; g23hsm (Xem Kế hoạch + Hiệu suất máy)
#      thấy 2 thẻ đó + Hiệu suất công nhân, KHÔNG thấy Kho / Thiết bị / Đồ gá. Bản cũ: mọi người thấy đủ 9 thẻ → SAI là đúng.
#   H2 [NAV-XEM-SUA][HOME-THE-QUYEN] nho01 (chỉ tick SỬA Đồ gá, không tick Xem): menu trái có "Quản lý Đồ gá", thẻ Trang chủ có, trang /do-ga mở được.
#      Bản cũ: menu ẩn → SAI là đúng.
#   V1 [VIEW-DOI-HOI] admin gõ PO vào form WTS (chưa lưu) rồi đổi "Xem WTS của" sang nho01 → modal trên trang "Đang có dòng gõ dở"; "Ở lại" → vẫn xem
#      chính mình, PO còn; V1d để modal mở 66 giây (quá hạn JS 1 phút) vẫn không lỗi; "Vẫn đổi" → xem WTS của nho01. Form trống → đổi không hỏi.
#      Bản cũ: không hỏi, xoá form → SAI là đúng.
#   H3 [NAV-XEM-SUA] nho01 chỉ Sửa Kho: /kho mở được (trang xét Xem ∨ Sửa).
# Chạy trên harness VỪA khởi động: python3 drive_nho.py <port> <nhãn>
import sys, os, asyncio, json, urllib.request, urllib.error
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}

async def login(ctx, user, pw, goto, wait=3000):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + goto); await pg.wait_for_timeout(wait)
    return pg
async def set_val(pg, loc, val):
    await loc.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }", val)
    await pg.wait_for_timeout(1800)
CARDS = ["Kế Hoạch", "Part Master", "Kho", "Danh sách thiết bị", "Hiệu suất máy", "Hiệu suất công nhân", "Sơ đồ tổ chức", "Quản lý Đồ gá", "WTS Tiêu chuẩn"]
async def cards(pg):
    hs = await pg.locator("div.container-fluid h5").all_inner_texts()
    return [h.strip() for h in hs]

async def main():
    s1 = post("/harness/g23-seed"); s2 = post("/harness/nho-seed"); s3 = post("/harness/g27-seed")
    ok(s1.get("ok") and s2.get("ok") and s3.get("ok"), "Seed g23 (g23xem / g23hsm) + nho01 + PO G27", [s1, s2, s3])
    async with async_playwright() as p:
        br = await p.chromium.launch()
        print("STEP H1 HOME-THE-QUYEN — thẻ Trang chủ theo quyền")
        ctx = await br.new_context(); pg = await login(ctx, "admin", "admin123", "/")
        c = await cards(pg); ok(all(x in c for x in CARDS), "H1 admin: đủ 9 thẻ", c); await ctx.close()
        ctx = await br.new_context(); pg = await login(ctx, "g23xem", "123456", "/")
        c = await cards(pg); ok(c == ["Hiệu suất công nhân"], "H1 g23xem (chỉ Xem Giao nhận Kho): chỉ thẻ Hiệu suất công nhân", c); await ctx.close()
        ctx = await br.new_context(); pg = await login(ctx, "g23hsm", "123456", "/")
        c = await cards(pg)
        ok("Kế Hoạch" in c and "Hiệu suất máy" in c and "Hiệu suất công nhân" in c and not any(x in c for x in ["Kho", "Danh sách thiết bị", "Quản lý Đồ gá", "Part Master", "WTS Tiêu chuẩn"]),
           "H1 g23hsm (Xem KH + HSM): thẻ Kế Hoạch / Hiệu suất máy / Hiệu suất công nhân, không Kho / Thiết bị / Đồ gá / Part Master / WTS", c)
        await ctx.close()

        print("STEP H2 NAV-XEM-SUA — chỉ tick Sửa vẫn thấy menu + thẻ, trang mở được")
        ctx = await br.new_context(); pg = await login(ctx, "nho01", "123456", "/")
        nav = await pg.locator("nav.mes-sidebar").inner_text()
        c = await cards(pg)
        ok("Quản lý Đồ gá" in nav, "H2 nho01 (chỉ Sửa DO_GA): menu trái có 'Quản lý Đồ gá'", nav[:400])
        ok("Quản lý Đồ gá" in c, "H2 nho01: thẻ Trang chủ 'Quản lý Đồ gá'", c)
        await pg.goto(BASE + "/do-ga"); await pg.wait_for_timeout(2500)
        body = await pg.locator("body").inner_text()
        ok("Truy cập bị từ chối" not in body, "H2 nho01 mở /do-ga: không bị từ chối (trang xét Xem ∨ Sửa)", body[:300])
        await pg.goto(BASE + "/kho"); await pg.wait_for_timeout(2500)
        body = await pg.locator("body").inner_text()
        ok(BASE + "/kho" in pg.url and "Truy cập bị từ chối" not in body, "H3 nho01 (chỉ Sửa KHO) mở /kho: ở lại trang, không bị từ chối", pg.url + " | " + body[:200])
        await ctx.close()

        print("STEP V1 VIEW-DOI-HOI — đổi 'Xem WTS của' khi form gõ dở → modal hỏi trên trang")
        ctx = await br.new_context(); pg = await login(ctx, "admin", "admin123", "/hieu-suat-cong-nhan")
        if await pg.locator("button:has-text('Ca 1')").count(): await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        dialogs = []
        pg.on("dialog", lambda d: (dialogs.append(d.message), asyncio.ensure_future(d.dismiss())))
        view = pg.locator("input[list='cuViewUsers']").first
        modal = pg.locator("#viewSwitchMsg")
        await set_val(pg, view, "Nho sửa đồ gá (nho01)")
        txt = await pg.locator("body").inner_text()
        ok(await modal.count() == 0 and "WTS của Nho sửa đồ gá" in txt, "V1a form trống: đổi sang nho01 không hỏi, đang xem WTS của nho01")
        await set_val(pg, view, "")
        txt = await pg.locator("body").inner_text()
        ok(await modal.count() == 0 and "WTS của Nho sửa đồ gá" not in txt, "V1a về chính mình không hỏi")
        po = pg.locator("input[placeholder='Gõ PO...']").first
        await set_val(pg, po, "G27-001")
        pov = await po.input_value()
        await set_val(pg, view, "Nho sửa đồ gá (nho01)")
        txt = await pg.locator("body").inner_text()
        ok(await modal.count() == 1 and "gõ dở" in txt, "V1b có PO đang gõ: đổi người xem → modal 'Đang có dòng gõ dở'", txt[:300])
        ok("WTS của Nho sửa đồ gá — chỉ Sửa" not in txt and (await view.input_value()) == "", "V1b lúc hỏi: vẫn xem chính mình, ô 'Xem WTS của' về trống", await view.input_value())
        await (pg.click("#viewSwitchStay") if await pg.locator("#viewSwitchStay").count() else asyncio.sleep(0)); await pg.wait_for_timeout(1200)
        ok(await modal.count() == 0 and (await pg.locator("input[placeholder='Gõ PO...']").first.input_value()) == pov and pov != "", "V1b bấm 'Ở lại': modal đóng, PO đang gõ còn nguyên", pov)
        # V1d chờ lâu (> 65 giây, quá hạn gọi JS 1 phút của Blazor) rồi mới bấm — trang vẫn sống
        await set_val(pg, view, "Nho sửa đồ gá (nho01)")
        await pg.wait_for_timeout(66000)
        await (pg.click("#viewSwitchStay") if await pg.locator("#viewSwitchStay").count() else asyncio.sleep(0)); await pg.wait_for_timeout(1500)
        err_vis = await pg.locator("#blazor-error-ui").is_visible() if await pg.locator("#blazor-error-ui").count() else False
        ok(not err_vis and (await pg.locator("input[placeholder='Gõ PO...']").first.input_value()) == pov, "V1d để modal mở 66 giây rồi 'Ở lại': trang không lỗi, PO còn", err_vis)
        await set_val(pg, view, "Nho sửa đồ gá (nho01)")
        await (pg.click("#viewSwitchGo") if await pg.locator("#viewSwitchGo").count() else asyncio.sleep(0)); await pg.wait_for_timeout(2500)
        txt = await pg.locator("body").inner_text()
        ok(await modal.count() == 0 and "WTS của Nho sửa đồ gá" in txt, "V1c bấm 'Vẫn đổi': xem WTS của nho01 (form gõ dở bị xoá như cũ)")
        ok(len(dialogs) == 0, "V1 không dùng hộp hỏi của trình duyệt", dialogs)
        await ctx.close(); await br.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
