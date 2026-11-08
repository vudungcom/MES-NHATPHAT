# [GOI8-THAP-F 08/11/2026] Gói 10 — trang WTS: PO mà HTSP mới chỉ có hàng Gia công giao theo NC cho Taro làm hộ (Nhận sản xuất HTSP = 0) vẫn hiện trong
#   danh sách PO cho cả người làm Bavia / Rửa (danh sách dùng chung cả trang, nhóm nhỏ chọn theo từng dòng) → dòng chọn nhóm nhỏ Bavia / Rửa với PO đó
#   hiện ngay "🔒 PO này HTSP mới có hàng Taro làm hộ — Bavia / Rửa chưa có hàng nhận vào" (đỏ). Trước: không báo gì tới khi bấm Lưu (máy chủ chặn).
#   Dữ liệu: POST /harness/g6-seed (HarnessSua.SeedG6Async — PO-LAMHO: HTSP nhận 10 hàng giao theo NC từ NC02 cho Taro NC 3, chưa nhận Hàng mới nào).
#   P1 htsp01 · dòng PO-LAMHO · nhóm nhỏ Bavia → có cảnh báo (chữ đỏ). P2 đổi nhóm nhỏ Rửa → có cảnh báo (chữ Rửa). P3 đổi Taro → KHÔNG cảnh báo.
#   Bản a32c59a: P1 / P2 SAI (không có dòng cảnh báo).
#   Nguồn: CLAUDE (rà độc lập Gói 8, sổ GOI8-THAP (f)); Hùng 08/11 11:24 OK Gói 10.
# Chạy trên harness VỪA khởi động: python3 drive_g10.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))

WARN = "PO này HTSP mới có hàng Taro làm hộ"

async def main():
    print("seed", post("/harness/g6-seed"))
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "htsp01"); await pg.fill("#password", "123456")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
            await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(600)
        row = pg.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
        inp = row.locator("input[placeholder='Gõ PO...']")
        await inp.fill("PO-LAMHO"); await pg.wait_for_timeout(1200); await inp.press("Tab"); await pg.wait_for_timeout(1500)
        async def pick(sub):
            await row.locator("select").first.select_option(sub); await pg.wait_for_timeout(1000)
            cell = row.locator("td").first
            txt = (await cell.inner_text()).replace("\n", " ")
            red = await cell.locator("div.text-danger").count() > 0
            return txt, red
        print("[P1] nhóm nhỏ Bavia")
        txt, red = await pick("Bavia")
        ok(WARN in txt and "Bavia chưa có hàng nhận vào" in txt and red, "P1 PO-LAMHO + Bavia: hiện ngay cảnh báo đỏ (trước: không báo tới lúc Lưu)", txt[:300])
        print("[P2] nhóm nhỏ Rửa")
        txt, red = await pick("Rua")
        ok(WARN in txt and "Rửa chưa có hàng nhận vào" in txt and red, "P2 PO-LAMHO + Rửa: hiện cảnh báo đỏ", txt[:300])
        print("[P3] nhóm nhỏ Taro")
        txt, red = await pick("Taro")
        ok(WARN not in txt, "P3 PO-LAMHO + Taro: không cảnh báo (Taro làm hộ ghi được)", txt[:300])
        await b.close()
    print(f"\nSAI {fails}")
    sys.exit(1 if fails else 0)

asyncio.run(main())
