# [WTS-OK-FALLBACK 02/11/2026] Trang WTS thật: dòng WTS CŨ (trước khi có cột QtyOk: QtyOk NULL, QtyDone 10, NG 2) — bảng dòng đã lưu hiện
#   OK 8 (= QtyDone − NG, cùng công thức sổ); bấm "Sửa lại" thì form mang OK 8 / NG 2; Lưu không đổi gì → dòng mới OK 8, NG 2, tổng 10
#   (trước: form lấy OK = QtyDone 10 → OK + NG = 12 → ghi OK dư / vượt trần).
# Chạy: python3 drive_okfb.py <port> <thư mục bin harness> [nhãn]
#   Tự bật harness với HARNESS_OKF=1 (HarnessSeed thêm PO-OKF + dòng cũ hôm nay của admin) rồi tự tắt — cổng phải đang trống.
import sys, os, asyncio, json, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hk_launch
from playwright.async_api import async_playwright
PORT = sys.argv[1]; BIN = sys.argv[2]; OUT = sys.argv[3] if len(sys.argv) > 3 else "new"
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""))
def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
def subrow(pg): return grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)

async def main():
    d = dump()
    old = [w for w in d["wts"] if w["nc"] == "NC1" and w["qtyOk"] is None and w["qtyNg"] == 2 and not w["isVoided"]]
    ok(len(old) == 1 and old[0]["qtyDone"] == 10, "O0 seed: 1 dòng WTS cũ PO-OKF NC1 QtyDone 10, QtyOk NULL, NG 2 (hôm nay, admin)", old)
    if len(old) != 1: return
    old = old[0]
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        saved = pg.locator("tr", has=pg.locator("button[title='Sửa lại']")).filter(has_text="PO-OKF").first
        cells = [c.strip() for c in (await saved.inner_text()).split("\t")]
        ok("8" in cells and "2" in cells and "10" in cells, "O1 bảng dòng đã lưu: OK 8 · NG 2 · SL PO 10", cells)
        await remove_groups(pg)
        await saved.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        row = subrow(pg)
        okv = (await row.locator("input[type=number]").nth(0).input_value()).strip()
        ngv = (await row.locator("input[type=number]").nth(1).input_value()).strip()
        ok(float(okv or -1) == 8 and float(ngv or -1) == 2, "O2 bấm Sửa lại: form OK 8 / NG 2 (OK = QtyDone − NG như sổ, không phải QtyDone 10)", f"OK '{okv}' NG '{ngv}'")
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        await pg.screenshot(path=f"okfb_{OUT}.png", full_page=False)
        d = dump()
        o2 = next(w for w in d["wts"] if w["wtsLogId"] == old["wtsLogId"])
        new = [w for w in d["wts"] if not w["isVoided"] and w["khPlanDetailId"] == old["khPlanDetailId"] and w["wtsLogId"] != old["wtsLogId"]]
        ok(o2["isVoided"] and len(new) == 1, "O3 Lưu (không đổi gì): dòng cũ huỷ 'Sửa lại', đúng 1 dòng mới", f"cũ huỷ {o2['isVoided']} ({o2['voidReason']}) · mới {len(new)}")
        if new:
            n = new[0]
            ok(n["qtyOk"] == 8 and n["qtyNg"] == 2 and n["qtyDone"] == 10 and n["ngCaseId"] == old["ngCaseId"],
               "O4 dòng mới OK 8 · NG 2 · tổng 10, giữ vụ lỗi cũ (trước: OK 10 → OK dư 2)", {k: n[k] for k in ("qtyOk", "qtyNg", "qtyDone", "ngCaseId")})
        await b.close()

proc = hk_launch.start(BIN, PORT, env={"HARNESS_OKF": "1"}, log="run_okfb.log")
try: asyncio.run(main())
finally: hk_launch.stop(proc)
print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
sys.exit(0 if fails == 0 else 1)
