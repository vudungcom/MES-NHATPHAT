# [RS-E5 31/10/2026] Trang "Sửa phiếu KH" hiện trạng thái theo sổ Giao nhận (trước: luôn "WaitingSupplierOrder")
#   + ghi nhỏ Order VL / Vị trí bên Quản lý Kho khi khác ô của Kế hoạch; danh sách Kế hoạch tìm được theo Order VL bên Kho
#   (cột Order VL / Vị trí trên danh sách hiện số của Kho, ô tìm trước chỉ tìm số của Kế hoạch).
# Chạy: python3 drive_rse5.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0

def dump():
    return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())

def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info else ""))

async def main():
    d0 = dump(); plan = d0["khplan"][0]["khPlanId"]
    det = [x for x in d0["khdet"] if x["khPlanId"] == plan][0]
    async with async_playwright() as p:
        b = await p.chromium.launch()
        ctx = await b.new_context(); pg = await ctx.new_page()
        await pg.set_viewport_size({"width": 1600, "height": 1100})
        await pg.goto(BASE + "/login")
        await pg.fill("#username", "khe01"); await pg.fill("#password", "123456")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")

        await pg.goto(BASE + f"/kh-plan/{plan}/detail/{det['khPlanDetailId']}/edit"); await pg.wait_for_timeout(2500)
        badge = await pg.locator("strong:has-text('Chi tiết Part') + span.badge").first.inner_text()
        ok("WaitingSupplierOrder" not in badge and badge.strip() not in ("", "—"), "Trạng thái theo sổ (không còn WaitingSupplierOrder)", badge)
        body = await pg.inner_text("body")
        ok("Quản lý Kho ghi:" in body and "OVL-" in body, "Order VL bên Kho khác ô Kế hoạch: có dòng nhỏ 'Quản lý Kho ghi: …'", "")
        await pg.screenshot(path=f"rse5_edit_{OUT}.png", full_page=False)

        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(2500)
        po = "PO1"   # chi tiết đầu của KH-TEST (HarnessSeed)
        await pg.fill("input[placeholder*='Tìm']", "OVL-" + po); await pg.wait_for_timeout(1200)
        n = await pg.locator("table tbody tr:has-text('" + po + "')").count()
        ok(n >= 1, f"Tìm 'OVL-{po}' (Order VL bên Kho, đang hiện ở cột) ra PO {po}", n)
        await pg.screenshot(path=f"rse5_list_{OUT}.png", full_page=False)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
