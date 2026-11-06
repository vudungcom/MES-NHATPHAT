# [NHAN-DOI-O-NC][K5-CLOSE-NHIEU 06/11/2026] Trang Giao nhận thật (harness EF giả).
#   Dữ liệu: POST /harness/nhannc-seed (Stubs/HarnessSua.cs SeedNhanNcAsync): PO-NNC, vụ GC ở NC02 đi Kho rồi Kho giao lại GC ô Hàng lỗi (phiếu không NC);
#   phiếu Kho → GC 6 đã huỷ phần chưa nhận 2 lần.
#   N1 admin: Nhận · Gia công → phiếu ô Hàng lỗi → chọn ô "Hàng lỗi đã xử lý" → hiện ô "Nhận vào NC" mặc định NC02 (theo vụ), có "Làm lại từ NC01", KHÔNG có NC sau
#   N2 chọn NC01 → hiện ô lý do; bấm Xác nhận không lý do → báo, DB không thêm lần nhận
#   N3 ghi lý do → Xác nhận → DB lần nhận ToNC NC01 + lý do trong Ghi chú; sổ chờ NC01 +1; cân, 0 ô âm
#   N4 Lịch sử phiếu: phiếu 6 hiện "2 lần" huỷ phần chưa nhận + lý do từng lần; lần nhận hiện "vào NC 01"
#   Bản cũ (trước gói 3) không có ô NC + chỉ 1 dòng huỷ → SAI ở N1 / N4 là đúng
# Chạy trên harness VỪA khởi động: python3 drive_nhannc.py <port> <old|new>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
async def login(pg, user, pw):
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
def row(pg): return pg.locator("tr", has_text="PO-NNC").first
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".btn-close").first.click(); await pg.wait_for_timeout(600)

async def main():
    post("/harness/nhannc-seed")
    s0 = get("/harness/nhannc")
    ok(s0["balanced"] and s0["neg"] == 0, "N0 dữ liệu dựng xong, cân, 0 ô âm", s0)
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1100})
        await login(pg, "admin", "admin123")
        print("[N1] Nhận · Gia công, đổi ô sang Hàng lỗi đã xử lý → ô NC theo vụ")
        await row(pg).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
        m = modal(pg)
        if await m.locator("select").count():
            await m.locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1500)
        hdr = m.locator("div[style*='cursor:pointer']").first
        opened = await hdr.count() > 0
        if opened:
            await hdr.click(); await pg.wait_for_timeout(900)
        xl = m.locator("button.gn-cellbtn", has_text="Hàng lỗi đã xử lý").first
        ok(opened and await xl.count() == 1, "N11 mở phiếu Kho → Gia công ô Hàng lỗi, có nút ô 'Hàng lỗi đã xử lý'", (await m.inner_text())[:500])
        if await xl.count():
            await xl.click(); await pg.wait_for_timeout(900)
            await m.locator("input[type=text].border-warning").first.fill("da sua xong")
            sel = m.locator("select", has=pg.locator("option", has_text="mặc định")).first
            opts = [(await o.inner_text()).strip() for o in await sel.locator("option").all()] if await sel.count() else []
            ok(len(opts) >= 2 and opts[0].startswith("NC02") and "mặc định" in opts[0] and any("Làm lại từ NC01" in o for o in opts) and len(opts) == 2,
               "N12 ô 'Nhận vào NC': mặc định NC02 (theo vụ), có 'Làm lại từ NC01', không có lựa chọn khác", opts)
            print("[N2] chọn NC01, không lý do → báo")
            if await sel.count():
                await sel.select_option(index=1); await pg.wait_for_timeout(700)
            nums = m.locator("input[type=number]")
            n_before = len(get("/harness/nhannc")["recs"])
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(1500)
            t2 = await m.inner_text()
            ok("lý do" in t2.lower() and len(get("/harness/nhannc")["recs"]) == n_before, "N21 thiếu lý do: báo, DB không thêm lần nhận", t2[-300:])
            print("[N3] ghi lý do → lưu NC01")
            await m.locator("input[maxlength='200']").last.fill("kiem lai kich thuoc"); await pg.wait_for_timeout(300)
            if await nums.count():
                await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change"); await pg.wait_for_timeout(300)
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
            s3 = get("/harness/nhannc")
            rn = [r for r in s3["recs"] if r["toCell"] == "XL"]
            ok(len(rn) == 1 and rn[0]["toNC"] == "NC01" and "kiem lai kich thuoc" in (rn[0]["notes"] or ""), "N31 DB: lần nhận ô Hàng lỗi đã xử lý, ToNC NC01, lý do trong Ghi chú", s3["recs"])
            ok(s3["nc"].get("NC01") == s0["nc"].get("NC01") + 1 and s3["nc"].get("NC02") == s0["nc"].get("NC02") and s3["balanced"] and s3["neg"] == 0,
               "N32 sổ: chờ NC01 +1, NC02 không đổi, cân, 0 ô âm", {"truoc": s0["nc"], "sau": s3["nc"]})
        await close_modal(pg)
        print("[N4] Lịch sử phiếu")
        await row(pg).locator("button[title='Lịch sử phiếu']").click(); await pg.wait_for_timeout(2000)
        tl = await modal(pg).inner_text()
        ok("Đã huỷ phần chưa nhận 5 chiếc" in tl and "2 lần" in tl and "huy lan dau" in tl and "huy lan hai" in tl,
           "N41 phiếu 6: 'Đã huỷ phần chưa nhận 5 chiếc … 2 lần', lý do từng lần", tl[:1500])
        ok("vào NC NC01" in tl, "N42 lần nhận đổi ô hiện 'vào NC NC01'", "")
        await b.close()
    print(f"KẾT QUẢ drive_nhannc ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
