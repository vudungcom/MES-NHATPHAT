# [W1N-SCEN 26/11/2026] drive_w1n.py — bấm trang thật Gói WTS-1NGUON. Dữ liệu Stubs/HarnessW1N.cs.
#   ĐÁP ÁN TÍNH TAY (Công nhân W1N, thứ Hai tuần này, không ca → giờ chuẩn = phút khai báo):
#     Gia công 08–10 = 120 phút, trong đó phát sinh CHO1N (vô ích) 20 + phát sinh không mã 10 → phần Gia công 90; việc LA1N (mã lạ) 10–11 = 60.
#     Chưa chọn mã Gia công: có ích 0 · vô ích 20 · chưa có mã 90 + 10 + 60 = 160 · giờ chuẩn 180 · chưa khai báo "—" · OK / NG 1 / 0.
#     Chọn GC1N (có ích) làm mã Gia công: có ích 90 · vô ích 20 · chưa có mã 70.
#   STEP W0 [WTS-1NGUON] WTS Report, chọn Công nhân W1N: ô số 180 / 0 / 20 / 160 / — / 1 / 0
#   STEP W1 [WTS-1NGUON] dm01 (Sửa WTS tiêu chuẩn, PIN 1111) → /wts-tieu-chuan: thanh "Mã dùng cho Gia công: Chưa chọn"; Đổi mã → chọn GC1N + PIN → thanh hiện GC1N, nhãn "Gia công" ở bảng
#   STEP W2 [WTS-1NGUON] WTS Report lại: 180 / 90 / 20 / 70 / — / 1 / 0; bảng theo việc có CHO1N (Phát sinh) 20, "(phát sinh không mã)" 10, Gia công · Hàng loạt 90
#   STEP W3 [WTS-1NGUON] Part Master PART-E2: ô Mã CV dòng chính Bavia / Rửa chỉ đọc (không gõ tay)
#   Bản cũ (7bf0cf5): không có ô "Chưa có mã" / thanh mã Gia công → SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_w1n.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}

async def login(ctx, user, pw, goto, wait=3000):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1300})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + goto); await pg.wait_for_timeout(wait)
    return pg

async def report(ctx):
    pg = await login(ctx, "admin", "admin123", "/hieu-suat-cong-nhan", 3000)
    await pg.click("button:has-text('WTS Report')"); await pg.wait_for_timeout(2500)
    await pg.fill("#rptWorker", "Công nhân W1N"); await pg.locator("#rptWorker").dispatch_event("change"); await pg.wait_for_timeout(1500)
    k = [x.strip() for x in await pg.locator("div.row-cols-md-6 .fs-4").all_inner_texts()]
    return pg, k

async def main():
    st = post("/harness/w1n-seed")
    ok(st.get("ok"), "Seed W1N", st)
    async with async_playwright() as p:
        br = await p.chromium.launch(); ctx = await br.new_context()
        try:
            print("STEP W0 WTS-1NGUON — chưa chọn mã Gia công")
            pg, k = await report(ctx)
            ok(k == ["180", "0", "20", "160", "—", "1 / 0"], "W0 ô số W1N: giờ chuẩn 180 / có ích 0 / vô ích 20 / chưa có mã 160 / — / 1 / 0", k)
            await pg.screenshot(path=f"{OUT}_w1n_w0.png", full_page=True); await pg.close()

            print("STEP W1 WTS-1NGUON — WTS tiêu chuẩn: chọn Mã dùng cho Gia công")
            pg = await login(ctx, "dm01", "123456", "/wts-tieu-chuan", 3000)
            body = await pg.locator("body").inner_text()
            ok("Mã dùng cho Gia công:" in body and "Chưa chọn" in body, "W1 thanh 'Mã dùng cho Gia công: Chưa chọn …'", body[:400])
            await pg.click("button:has-text('Đổi mã')"); await pg.wait_for_timeout(800)
            await pg.locator(".modal-body select").select_option(label="[GC1N] Gia công chung W1N — Có ích")
            await pg.locator(".modal-body input[type=password]").fill("1111")
            await pg.locator(".modal-footer button:has-text('Lưu')").click(); await pg.wait_for_timeout(2000)
            body = await pg.locator("body").inner_text()
            ok("GC1N — Gia công chung W1N" in body and await pg.locator("span.badge[title='Mã dùng cho Gia công']").count() == 1 and await pg.locator(".modal-body").count() == 0,
               "W1 lưu xong: thanh hiện GC1N — Gia công chung W1N, nhãn 'Gia công' ở bảng, modal đóng", body[:500])
            await pg.screenshot(path=f"{OUT}_w1n_w1.png", full_page=True); await pg.close()

            print("STEP W2 WTS-1NGUON — báo cáo sau khi chọn mã Gia công")
            pg, k = await report(ctx)
            ok(k == ["180", "90", "20", "70", "—", "1 / 0"], "W2 ô số W1N: 180 / có ích 90 / vô ích 20 / chưa có mã 70 / — / 1 / 0", k)
            body = await pg.locator("body").inner_text()
            ok("CHO1N Chờ vật tư W1N" in body and "(phát sinh không mã)" in body and "Gia công · Hàng loạt" in body and "Phát sinh" in body,
               "W2 bảng theo việc: CHO1N (Phát sinh), (phát sinh không mã), Gia công · Hàng loạt", body[:900])
            await pg.screenshot(path=f"{OUT}_w1n_w2.png", full_page=True); await pg.close()

            print("STEP W3 WTS-1NGUON — Part Master: ô Mã CV dòng chính chỉ đọc")
            pg = await login(ctx, "admin", "admin123", "/part-master/detail?partNo=PART-E2", 4000)
            loc = pg.locator("input[title^='Mã CV lấy từ ô chọn']")
            n = await loc.count()
            ro = [await loc.nth(i).get_attribute("readonly") for i in range(n)]
            ok(n >= 2 and all(r is not None for r in ro), "W3 PART-E2: ô Mã CV Bavia B1 / Rửa R1 (dòng chính) có readonly", (n, ro))
            # rà độc lập L6: dòng MỚI (chưa chọn mã) cũng không gõ tay được — thêm dòng Bavia, thử gõ, giá trị không đổi
            await pg.locator("button[title='Thêm dòng']").nth(2).click(); await pg.wait_for_timeout(600)
            inp = pg.locator("xpath=(//button[@title='Thêm dòng'])[3]/following::table[1]//tbody/tr[last()]//input").first
            ro_new = await inp.get_attribute("readonly"); v0 = await inp.input_value()
            try: await inp.type("ZZ9", timeout=2000)
            except Exception: pass
            v1 = await inp.input_value()
            ok(ro_new is not None and v1 == v0, "W3 dòng Bavia MỚI: ô Mã CV chỉ đọc, gõ 'ZZ9' không vào (chỉ chọn mã ở ô chọn công đoạn chuẩn)", (ro_new, v0, v1))
            await pg.close()
        finally:
            await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
