# [NHAN-DOI-O-NC][K5-CLOSE-NHIEU 06/11/2026] Trang Giao nhận thật (harness EF giả).
#   Dữ liệu: POST /harness/nhannc-seed (Stubs/HarnessSua.cs SeedNhanNcAsync): PO-NNC, vụ GC ở NC02 đi Kho rồi Kho giao lại GC ô Hàng lỗi (phiếu không NC);
#   phiếu Kho → GC 6 đã huỷ phần chưa nhận 2 lần.
#   N1 admin: Nhận · Gia công → phiếu ô Hàng lỗi → chọn ô "Hàng lỗi đã xử lý" → hiện ô "Nhận vào NC" mặc định NC02 (theo vụ), KHÔNG có NC sau
#   [SUA-KHONG-LUI 2026-11-10] N12 Workflow ON: chỉ NC02 theo vụ (không làm lại NC01 — trước đây N2/N3 lùi NC01 + lý do; nay chỉ Workflow OFF, kiểm ở GiaoNhanFuzz sua:S45)
#   N3 Xác nhận → DB lần nhận ToNC NC02; sổ chờ NC02 +1; cân, 0 ô âm
#   N4 Lịch sử phiếu: phiếu 6 hiện "2 lần" huỷ phần chưa nhận + lý do từng lần; lần nhận hiện "vào NC NC02"
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
        # [SUA-QUA-KCS 2026-11-11] Workflow ON: không đổi ô Hàng lỗi → Hàng lỗi đã xử lý lúc nhận (Hùng 11/11 15:34 B1) — luật NC theo vụ (NHAN-DOI-O-NC) chỉ còn khi OFF
        xl0 = m.locator("button.gn-cellbtn", has_text="Hàng lỗi đã xử lý")
        ok(opened and await xl0.count() == 0, "N10 [SUA-QUA-KCS] Workflow ON: phiếu ô Hàng lỗi không có nút ô 'Hàng lỗi đã xử lý'", (await m.inner_text())[:500])
        await close_modal(pg)
        post("/harness/g16-wf?po=PO-NNC&on=0")
        await pg.reload(); await pg.wait_for_timeout(4000)
        await row(pg).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
        m = modal(pg)
        if await m.locator("select").count():
            await m.locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1500)
        hdr = m.locator("div[style*='cursor:pointer']").first
        if await hdr.count():
            await hdr.click(); await pg.wait_for_timeout(900)
        xl = m.locator("button.gn-cellbtn", has_text="Hàng lỗi đã xử lý").first
        ok(opened and await xl.count() == 1, "N11 Workflow OFF: mở phiếu Kho → Gia công ô Hàng lỗi, có nút ô 'Hàng lỗi đã xử lý'", (await m.inner_text())[:500])
        if await xl.count():
            await xl.click(); await pg.wait_for_timeout(900)
            await m.locator("input[type=text].border-warning").first.fill("da sua xong")
            sel = m.locator("select", has=pg.locator("option", has_text="mặc định")).first
            opts = [(await o.inner_text()).strip() for o in await sel.locator("option").all()] if await sel.count() else []
            # [SUA-KHONG-LUI 2026-11-10] Workflow ON: hàng sửa đạt không làm lại NC trước — chỉ NC theo vụ. Nguồn: KHÁCH 10/11 vòng 1 câu 2.1 A; HÙNG 10/11 19:22
            ok(len(opts) >= 1 and opts[0].startswith("NC02") and "mặc định" in opts[0] and not any("NC03" in o for o in opts), "N12 Workflow OFF: ô 'Nhận vào NC': NC02 mặc định (theo vụ), không có NC sau", opts)
            print("[N3] nhận vào NC02 (theo vụ)")
            nums = m.locator("input[type=number]")
            if await nums.count():
                await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change"); await pg.wait_for_timeout(300)
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
            s3 = get("/harness/nhannc")
            rn = [r for r in s3["recs"] if r["toCell"] == "XL"]
            ok(len(rn) == 1 and rn[0]["toNC"] == "NC02", "N31 DB: lần nhận ô Hàng lỗi đã xử lý, ToNC NC02 (theo vụ)", s3["recs"])
            ok(s3["nc"].get("NC02") == s0["nc"].get("NC02") + 1 and s3["nc"].get("NC01") == s0["nc"].get("NC01") and s3["balanced"] and s3["neg"] == 0,
               "N32 sổ: chờ NC02 +1, NC01 không đổi, cân, 0 ô âm", {"truoc": s0["nc"], "sau": s3["nc"]})
        await close_modal(pg)
        print("[N4] Lịch sử phiếu")
        await row(pg).locator("button[title='Lịch sử phiếu']").click(); await pg.wait_for_timeout(2000)
        tl = await modal(pg).inner_text()
        ok("Đã huỷ phần chưa nhận 5 chiếc" in tl and "2 lần" in tl and "huy lan dau" in tl and "huy lan hai" in tl,
           "N41 phiếu 6: 'Đã huỷ phần chưa nhận 5 chiếc … 2 lần', lý do từng lần", tl[:1500])
        ok("vào NC NC02" in tl, "N42 lần nhận đổi ô hiện 'vào NC NC02'", "")
        await b.close()
    print(f"KẾT QUẢ drive_nhannc ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
