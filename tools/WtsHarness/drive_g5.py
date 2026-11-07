# [TARO-DUPHONG][TARO-DOI-CHEO-NC][WTS-HTSP-DSMA 07/11/2026] Gói 5 — bấm trang thật (harness EF giả):
#   P (TARO-DUPHONG): bảng Taro Part Master KHÔNG còn nút "Tạo mã CV dự phòng" (Bavia vẫn có); dòng dự phòng Taro cũ vẫn hiện + sửa giờ chuẩn lưu được.
#   S (TARO-DOI-CHEO-NC): đổi chéo NC 2 dòng 2T (NC 3 ↔ NC 5) trong 1 lần Lưu → trang chặn trước khi lưu, báo "đổi chéo" + cách làm, DB không đổi
#       (trước: trang cho qua, máy chủ chặn dòng đầu với lời "có thể vừa được người khác lưu").
#   C (TARO-DOI-CHEO-NC): đổi mã nối chuỗi cùng NC 3 (4T → 5T, 2T → 4T) trong 1 lần Lưu → trang ghi đúng thứ tự, lưu đủ
#       (trước: thứ tự theo NC — 2T ghi trước, trùng 4T NC 3 đang còn → máy chủ chặn).
#   W (WTS-HTSP-DSMA): htsp01 chọn nhóm nhỏ Taro TRƯỚC rồi mới gõ PO → danh sách mã CV có mã của PO (trước: trống).
# Dữ liệu: POST /harness/g5-seed (HarnessSua.SeedG5Async, Part PART-ML9) + /harness/taro-seed (PO-TARO). Chạy: python3 drive_g5.py <port> <nhãn>
#   trên harness VỪA khởi động (DB giả mới tinh). Nguồn: Hùng 07/11 07:55 giao Gói 5; 09:11 "bỏ dòng dự phòng taro".
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
PART = "/part-master/detail?partNo=PART-ML9"
fails = 0

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump2").read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))
def taro(d): return sorted((t["nc"], t["wtsTaskCode"]) for t in d["taro"] if t["part"] == "PART-ML9" and t["isActive"])
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
def taro_card(pg): return pg.locator(".card", has=pg.locator(".card-header", has_text="C.1 Taro")).first
def bavia_card(pg): return pg.locator(".card", has=pg.locator(".card-header", has_text="C.2 Bavia")).first
JS_TARO = """(card) => { const out = []; card.querySelectorAll('tbody tr').forEach((tr, i) => { const ins = tr.querySelectorAll('input');
    if (ins.length >= 2) out.push({i, nc: ins[0].value, code: ins[1].value}); }); return out; }"""
async def taro_row(pg, nc, code):
    card = taro_card(pg)
    for r in await card.evaluate(JS_TARO):
        if r["nc"] == nc and r["code"] == code: return card.locator("tbody tr").nth(r["i"])
    raise Exception(f"không thấy dòng Taro NC {nc} mã {code}: {await card.evaluate(JS_TARO)}")
async def set_nc(row, v):
    i = row.locator("input").first; await i.fill(v); await i.press("Tab"); await row.page.wait_for_timeout(400)
async def set_code(row, code):
    await row.locator("button[title='Sửa WTS dòng này']").click(); await row.page.wait_for_timeout(400)
    await row.locator("select").first.select_option(code); await row.page.wait_for_timeout(500)
async def save(pg, reason="kiem goi 5", wait=3500):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    t = (await al.last.inner_text()) if await al.count() else ""
    body = await pg.inner_text("body")
    return t + "\n" + "\n".join(l for l in body.split("\n") if "Không thể lưu" in l or "✗" in l or "đổi chéo" in l)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        seed = post("/harness/g5-seed"); print("seed", seed)
        pg = await login(b)
        d0 = dump(); t0 = taro(d0)

        print("[P] [TARO-DUPHONG] Taro không còn nút tạo dòng dự phòng; dòng dự phòng cũ vẫn sửa được")
        try:
            await open_part(pg)
            nT = await taro_card(pg).locator("button[title='Tạo mã CV dự phòng cho dòng này']").count()
            nB = await bavia_card(pg).locator("button[title='Tạo mã CV dự phòng cho dòng này']").count()
            ok(nT == 0 and nB >= 1, "P1 bảng Taro không còn nút \"Tạo mã CV dự phòng\" (Bavia vẫn có)", f"Taro {nT} · Bavia {nB}")
            rows = await taro_card(pg).evaluate(JS_TARO)
            ok(any(r["nc"] == "3-DP-01" for r in rows), "P2 dòng dự phòng Taro CŨ (3-DP-01) vẫn hiện trên bảng", rows)
            r = await taro_row(pg, "3-DP-01", "...")
            st = r.locator("input[type=number]").first; await st.fill("4"); await st.press("Tab"); await pg.wait_for_timeout(400)
            t = await save(pg); d = dump()
            v = [x["standardTime"] for x in d["taro"] if x["part"] == "PART-ML9" and x["nc"] == "3-DP-01" and x["isActive"]]
            ok(v == [4] and "✗" not in t and "Không thể" not in t, "P3 sửa T.Chuẩn dòng dự phòng cũ → lưu được (4)", f"{v} · {lines(t, '✗', 'Không thể')}")
        except Exception as e:
            ok(False, "P! pha [P] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[S] [TARO-DOI-CHEO-NC] đổi chéo NC 2 dòng 2T (NC 3 ↔ NC 5) trong 1 lần Lưu → trang chặn trước, báo cách làm")
        try:
            await open_part(pg)
            a = await taro_row(pg, "3", "2T"); c = await taro_row(pg, "5", "2T")   # lấy cả 2 dòng TRƯỚC khi sửa (sửa xong 2 dòng cùng NC)
            await set_nc(a, "5"); await set_nc(c, "3")
            nlog0 = len(dump()["logs"])
            t = await save(pg); d = dump()
            ok("đổi chéo" in t and "2T · NC 3 → 2T · NC 5" in t and taro(d) == taro(d0) and len(d["logs"]) == nlog0,
               "S1 trang chặn: báo \"đổi chéo 2T · NC 3 → 2T · NC 5, 2T · NC 5 → 2T · NC 3\" + cách làm; DB không đổi, không ghi lịch sử",
               f"{lines(t, 'đổi chéo', '✗', 'Không thể', 'Sửa')} · {taro(d)}")
        except Exception as e:
            ok(False, "S! pha [S] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[S2] [TARO-DOI-CHEO-NC] vòng 3 dòng trộn đổi mã + đổi NC (2T·3 → 4T·3, 4T·3 → 2T·5, 2T·5 → 2T·3) → trang chặn, DB không đổi (rà độc lập gói 5 mục 6)")
        try:
            await open_part(pg)
            a = await taro_row(pg, "3", "2T"); b4 = await taro_row(pg, "3", "4T"); c5 = await taro_row(pg, "5", "2T")
            await set_code(a, "4T")
            await set_code(b4, "2T"); await set_nc(b4, "5")
            await set_nc(c5, "3")
            t = await save(pg); d = dump()
            ok("đổi chéo" in t and taro(d) == taro(d0), "S2 vòng 3 dòng trộn mã + NC: trang báo đổi chéo, DB không đổi", f"{lines(t, 'đổi chéo', '✗', 'Không thể', 'Sửa')} · {taro(d)}")
        except Exception as e:
            ok(False, "S2! pha [S2] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[C] [TARO-DOI-CHEO-NC] đổi mã nối chuỗi cùng NC 3 (4T → 5T, 2T → 4T) trong 1 lần Lưu → ghi đúng thứ tự, lưu đủ")
        try:
            await open_part(pg)
            x = await taro_row(pg, "3", "2T"); await set_code(x, "4T")
            y = await taro_row(pg, "3", "4T")
            # 2 dòng cùng hiện mã 4T NC 3 trên form: dòng thứ 2 (dòng 4T gốc) đổi sang 5T
            card = taro_card(pg); rows = await card.evaluate(JS_TARO)
            idx = [r["i"] for r in rows if r["nc"] == "3" and r["code"] == "4T"]
            await set_code(card.locator("tbody tr").nth(idx[-1]), "5T")
            t = await save(pg); d = dump()
            want = sorted([x for x in t0 if not (x[0] == "3" and x[1] in ("2T", "4T"))] + [("3", "4T"), ("3", "5T")])
            ok(taro(d) == want and "✗" not in t and "Không thể" not in t, "C1 lưu đủ: NC 3 có 4T (dòng 2T cũ) + 5T (dòng 4T cũ), NC 5 giữ 2T, không lời báo",
               f"{taro(d)} · {lines(t, '✗', 'Không thể', 'đổi chéo')}")
        except Exception as e:
            ok(False, "C! pha [C] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[W] [WTS-HTSP-DSMA] htsp01: chọn nhóm nhỏ Taro TRƯỚC rồi gõ PO → danh sách mã CV có mã của PO")
        try:
            post("/harness/taro-seed")
            ctx = await b.new_context(); ctx.set_default_timeout(15000); pw = await ctx.new_page(); await pw.set_viewport_size({"width": 1900, "height": 1200})
            await pw.goto(BASE + "/login"); await pw.fill("#username", "htsp01"); await pw.fill("#password", "123456")
            await pw.click("button[type=submit]"); await pw.wait_for_load_state("networkidle")
            await pw.goto(BASE + "/hieu-suat-cong-nhan"); await pw.wait_for_timeout(1800)
            await pw.click("button:has-text('Ca 1')"); await pw.wait_for_timeout(800)
            if await pw.locator("input[placeholder='Gõ PO...']").count() == 0:
                await pw.click("button:has-text('Thêm nhóm')"); await pw.wait_for_timeout(500)
            g = pw.locator("table", has=pw.locator("input[placeholder='Gõ PO...']")).first
            await g.locator("select").nth(0).select_option("Taro"); await pw.wait_for_timeout(500)
            row = g.locator("tr", has=pw.locator("input[placeholder='Gõ PO...']")).nth(0)
            inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO-TARO"); await inp.press("Tab"); await pw.wait_for_timeout(1500)
            dl = await row.locator("input[placeholder='Mã CV...']").evaluate("e => [...(e.list ? e.list.options : [])].map(o => o.value)")
            ok("2T" in dl and "3T" in dl and len(dl) == len(set(dl)), "W1 danh sách mã CV có 2T, 3T… (mã dùng ở 2 NC hiện 1 lần) — trước: trống", dl)
            # W3: gõ mã 2T TRƯỚC khi có PO (nhóm nhỏ Taro đã chọn) rồi mới gõ PO → tên công việc điền theo danh sách (rà độc lập gói 5 mục 5)
            await pw.goto(BASE + "/hieu-suat-cong-nhan"); await pw.wait_for_timeout(1800)
            await pw.click("button:has-text('Ca 1')"); await pw.wait_for_timeout(800)
            if await pw.locator("input[placeholder='Gõ PO...']").count() == 0:
                await pw.click("button:has-text('Thêm nhóm')"); await pw.wait_for_timeout(500)
            g = pw.locator("table", has=pw.locator("input[placeholder='Gõ PO...']")).first
            await g.locator("select").nth(0).select_option("Taro"); await pw.wait_for_timeout(500)
            row = g.locator("tr", has=pw.locator("input[placeholder='Gõ PO...']")).nth(0)
            cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("2T"); await cv.dispatch_event("change"); await pw.wait_for_timeout(600)
            inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO-TARO"); await inp.press("Tab"); await pw.wait_for_timeout(1500)
            txt = await row.inner_text(); cvv = await cv.input_value()
            ok(cvv == "2T" and "Taro 2T" in txt, "W3 mã 2T gõ trước PO giữ nguyên, tên công việc hiện \"Taro 2T\" sau khi gõ PO", f"mã={cvv} · {txt[:200]}")
            await g.locator("select").nth(0).select_option("Bavia"); await pw.wait_for_timeout(500)
            dl2 = await row.locator("input[placeholder='Mã CV...']").evaluate("e => [...(e.list ? e.list.options : [])].map(o => o.value)")
            ok("2T" not in dl2, "W2 đổi sang nhóm nhỏ Bavia → danh sách đổi theo (không còn mã Taro)", dl2)
        except Exception as e:
            ok(False, "W! pha [W] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        await b.close()
    print(f"KẾT QUẢ drive_g5 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
