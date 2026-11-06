# [B6-DP-MA][B7-TIMING-MAY 06/11/2026] Bấm trang Part Master thật (harness EF giả, Part PART-ML9 của HarnessSeed.SeedPmAsync) — sửa sau rà độc lập gói 1:
#   D (B6): nút "Tạo dự phòng" đặt mã KHÔNG trùng dòng đang có: NC 1 có 1-DP-01 + 1-DP-02, xoá 1-DP-01, tạo dự phòng mới → 1-DP-03
#       (trước: lấy số đếm + 1 → ra lại 1-DP-02 đang có → máy chủ B6 chặn "đã có ở dòng dự phòng khác", người dùng không gỡ được).
#   T (B7): NC 1 ghi Máy bấm giờ "MTIM" mà máy MTIM đã xoá (B7 cho xoá) → sửa NC 1 / tạo dự phòng NC 1 vẫn LƯU được
#       (trước: trang chặn cả lượt Lưu "Máy bấm giờ 'MTIM' không có trong danh sách thiết bị", ô này không sửa được trên trang).
#   Nguồn: rà độc lập gói 1 06/11 mục 1 (CAO) + mục 2 (VỪA).
# Chạy: python3 drive_dpma.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — chạy 1 lần / 1 lần khởi động)
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
PART = "/part-master/detail?partNo=PART-ML9"

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump2").read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if (not cond and info not in (None, "")) else ""))
def bk(d): return sorted(s["nc"] for s in d["steps"] if s["part"] == "PART-ML9" and s["isActive"] and s["isBackup"] and s["nc"].startswith("1-DP-"))
def main1(d): return [s for s in d["steps"] if s["part"] == "PART-ML9" and s["isActive"] and not s["isBackup"] and s["nc"] == "1"][0]
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
async def save(pg, reason="kiem dpma", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    t = (await al.last.inner_text()) if await al.count() else ""
    body = await pg.inner_text("body")
    return t + "\n" + "\n".join(l for l in body.split("\n") if "Không thể lưu" in l or "Máy bấm giờ" in l or "✗" in l)
# dòng bảng Gia công có ô mã NC (ô input đầu) = nc
JS_ROWS = """() => { const out = []; document.querySelectorAll('tr').forEach((tr, i) => {
    if (tr.querySelector("input[list^='dl-thietbi']")) { const inp = tr.querySelector('input'); out.push({i, nc: inp ? inp.value : ''}); } }); return out; }"""
async def row(pg, nc):
    for r in await pg.evaluate(JS_ROWS):
        if r["nc"] == nc: return pg.locator("tr").nth(r["i"])
    raise Exception(f"không thấy dòng Gia công {nc}")
async def gc_codes(pg): return [r["nc"] for r in await pg.evaluate(JS_ROWS)]
async def add_backup(pg, nc="1"):
    await (await row(pg, nc)).locator("button[title='Tạo dự phòng']").click(); await pg.wait_for_timeout(500)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await login(b)

        print("[D] [B6-DP-MA] nút \"Tạo dự phòng\" không đặt lại mã dự phòng đang có")
        try:
            await open_part(pg)
            await add_backup(pg); await add_backup(pg)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-01", "1-DP-02"], "D1 NC 1 tạo 2 dự phòng → lưu được 1-DP-01 + 1-DP-02", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
            await open_part(pg)
            await (await row(pg, "1-DP-01")).locator("button[title='Xóa']").click(); await pg.wait_for_timeout(400)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-02"], "D2 xoá 1-DP-01 → còn 1-DP-02", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
            await open_part(pg)
            before = await gc_codes(pg); await add_backup(pg)
            new = [c for c in await gc_codes(pg) if c not in before]
            ok(new == ["1-DP-03"], "D3 tạo dự phòng mới → mã 1-DP-03 (trước: 1-DP-02 trùng dòng đang có)", new)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-02", "1-DP-03"] and "✗" not in t, "D4 lưu được, DB còn 1-DP-02 + 1-DP-03, không lời báo trùng", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
        except Exception as e:
            ok(False, "D! pha [D] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[T] [B7-TIMING-MAY] Máy bấm giờ đã xoá không chặn lưu Part Master")
        try:
            r = post("/harness/b7-seed")
            await open_part(pg)
            s0 = main1(dump())
            rw = await row(pg, "1")
            await rw.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(400)
            nums = rw.locator("input[type=number]")
            newv = str(int(s0["setupTime"] or 0) + 1)
            await nums.first.fill(newv); await nums.first.press("Tab"); await pg.wait_for_timeout(400)
            t = await save(pg); s1 = main1(dump())
            ok("Máy bấm giờ" not in t and "Không thể lưu" not in t and s1["setupTime"] != s0["setupTime"],
               "T1 sửa giờ NC 1 (Máy bấm giờ MTIM đã xoá) → LƯU được (trước: \"Không thể lưu — Máy bấm giờ 'MTIM' không có trong danh sách thiết bị\")",
               f"seed={r} T.Gá {s0['setupTime']}→{s1['setupTime']} · {lines(t, 'Máy bấm giờ', 'Không thể', '✗')}")
            await open_part(pg)
            before = bk(dump()); await add_backup(pg)
            t = await save(pg); after = bk(dump())
            ok("Máy bấm giờ" not in t and "Không thể lưu" not in t and len(after) == len(before) + 1,
               "T2 tạo dự phòng NC 1 (chép Máy bấm giờ MTIM sang dòng mới) → LƯU được", f"{before} → {after} · {lines(t, 'Máy bấm giờ', 'Không thể', '✗')}")
        except Exception as e:
            ok(False, "T! pha [T] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        await pg.screenshot(path=f"dpma_{OUT}.png", full_page=True)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
