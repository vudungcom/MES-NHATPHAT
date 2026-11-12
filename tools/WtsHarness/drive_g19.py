# [GÓI 19 12/11/2026][LS-THOIGIAN][WTS-3T-BAO][WTS-BAO-CU][WTS-VITRI] Lỗi tự vá trước (khách test giao nhận 12/11, không cần hỏi khách) — bấm trang thật (harness EF giả).
#   Nguồn: KHÁCH 12/11 (sheet test giao nhận TH4, TH6 ý 2, TH9); HÙNG 12/11 17:50 ("cái nào sửa được chưa cần khách thì sửa").
#   W (htsp01, trang WTS, dữ liệu POST /harness/taro-seed — PO-TARO, 3T gộp 3T1 ở NC 3):
#     W1 ghi 3T1 NC 3 = 10 (lưu được) → W2 ghi 3T NC 3 = 1 → chặn, lời báo "vượt Chưa làm 0 … chỉ ghi cho chiếc chưa làm mã lẻ … đã làm đủ" (trước: "chưa nhận hàng")
#     W3 đổi nhóm nhỏ sang Rửa → câu báo mã 3T (dòng + nhóm) mất (trước: dòng Rửa còn báo "mã 3T")
#     W4 nút "hàng đang ở đâu" dưới ô PO → khung "Hàng đang ở đâu — PO PO-TARO" (PoViTri: Phôi nhận, cân) → Đóng
#   L (admin, trang Giao nhận, dữ liệu POST /harness/g19-seed — PO-G19):
#     L1 Lịch sử phiếu: thanh "Xếp theo giờ" mặc định Cũ → mới — phiếu giao nhận + báo lỗi + Kết quả sửa xếp chung đúng thứ tự giờ lập (seq của seed)
#     L2 nhãn phiếu HTSP trả lại GC ô Hàng lỗi: "Giao · Hàng Sửa (trả lại — chưa sửa được)" (trước: "Hàng Sửa (sửa hộ xong)"), "nhận vào ô Hàng lỗi" (trước: ô Hàng lỗi đã xử lý)
#     L3 bấm "Cũ → mới" → "Mới → cũ", thứ tự đảo ngược đúng
#     L4 lọc Mã vụ lỗi = vụ B → chỉ còn mục có vụ B; vụ A → không mục nào vụ B; Tất cả → đủ lại
# Chạy trên harness VỪA khởi động: python3 drive_g19.py <port> <nhãn>   (bản cũ — trước Gói 19 — phải SAI ở W2 / W3 / W4 / L1–L4)
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if (not cond and info not in (None, "")) else ""))

async def login(b, user, pw, url):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + url); await pg.wait_for_timeout(1800)
    return pg

def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)
async def taro_row(pg, po, code, okq, start, end, nc):
    await ensure_group(pg); g = grp(pg)
    row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await pg.wait_for_timeout(800)
    await g.locator("select").nth(0).select_option("Taro"); await pg.wait_for_timeout(500)
    cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill(code); await cv.dispatch_event("change"); await pg.wait_for_timeout(600)
    sel = row.locator("select[title^='Mã này dùng ở nhiều NC Taro']")
    if await sel.count(): await sel.select_option(nc); await pg.wait_for_timeout(500)
    n = row.locator("input[type=number]").nth(0); await n.fill(str(okq)); await pg.wait_for_timeout(250)
    t = row.locator("input[placeholder='HH:mm']")
    await t.nth(0).fill(start); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
    await t.nth(1).fill(end); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
    a = row.locator("input[type=number][step='0.1']")
    if await a.count(): await a.first.fill("1"); await pg.wait_for_timeout(250)
    return row
async def save(pg):
    await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
    return " | ".join(t.strip() for t in await pg.locator(".alert-danger").all_inner_texts() if t.strip())
async def err_texts(pg):
    body = await pg.inner_text("body")
    return [l for l in body.split("\n") if "3T (NC 3)" in l or "chưa nhận hàng —" in l]   # "chưa nhận hàng —": câu báo trần cũ (không lấy nút "Hiện phiếu chưa nhận hàng")

LOG_JS = """m => { const c = m.querySelector('div[style="display:flex;flex-direction:column"]'); if (!c) return null;
  return Array.from(c.children).map(e => ({ y: e.getBoundingClientRect().top, t: e.innerText.replace(/\\s+/g, ' ').slice(0, 400) })).sort((a, b) => a.y - b.y).map(x => x.t); }"""

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        post("/harness/taro-seed"); sd = post("/harness/g19-seed")
        print(f"seed {sd}")

        print("[W] trang WTS (htsp01): mã gộp 3T, câu báo cũ, hàng đang ở đâu")
        try:
            pg = await login(b, "htsp01", "123456", "/hieu-suat-cong-nhan")
            await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
            await taro_row(pg, "PO-TARO", "3T1", 10, "08:00", "08:30", "3")
            t = await save(pg)
            ok(t == "" or "3T1" not in t, "W1 ghi 3T1 NC 3 = 10 → lưu được", t)
            await remove_groups(pg)
            await taro_row(pg, "PO-TARO", "3T", 1, "09:00", "09:10", "3")
            t = await save(pg); et = await err_texts(pg)
            j = " ".join(et)
            ok("vượt Chưa làm 0" in j and "chỉ ghi cho chiếc chưa làm mã lẻ nào trong bộ" in j and "10 chiếc đã làm đủ các mã lẻ trong bộ" in j and "chưa nhận hàng —" not in j,
               "W2 ghi 3T NC 3 = 1 → chặn: \"vượt Chưa làm 0 … chỉ ghi cho chiếc chưa làm mã lẻ … 10 chiếc đã làm đủ\" (trước: \"chưa nhận hàng\")", f"{t} · {et}")
            await grp(pg).locator("select").nth(0).select_option("Rua"); await pg.wait_for_timeout(900)
            et2 = await err_texts(pg)
            ok(len(et) > 0 and len(et2) == 0, "W3 đổi nhóm nhỏ sang Rửa → câu báo mã 3T (dòng + nhóm) mất", et2)
            row = grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            vb = row.locator("button:has-text('hàng đang ở đâu')")
            n = await vb.count()
            if n: await vb.first.click(); await pg.wait_for_timeout(1500)
            mt = await pg.locator(".mes-modal").last.inner_text() if await pg.locator(".mes-modal").count() else ""
            ok(n == 1 and "Hàng đang ở đâu — PO PO-TARO" in mt and "Phôi nhận" in mt and "cân" in mt, "W4 nút \"hàng đang ở đâu\" → khung Đang ở đâu của PO (Phôi nhận, cân)", f"nút {n} · {mt[:400]}")
            if n:
                await pg.locator(".mes-modal").last.locator("button:has-text('Đóng')").click(); await pg.wait_for_timeout(600)
                ok(await pg.locator(".mes-modal").count() == 0, "W5 Đóng → khung tắt, form WTS giữ nguyên")
        except Exception as e:
            ok(False, "W! pha [W] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[L] Lịch sử phiếu (admin): xếp theo giờ, nhãn sửa đạt / chưa đạt, lọc theo mã vụ")
        try:
            pg = await login(b, "admin", "admin123", "/handover"); await pg.wait_for_timeout(3500)
            await pg.locator("tr", has_text="PO-G19").first.locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
            m = pg.locator(".mes-modal").last
            items = await m.evaluate(LOG_JS)
            seq = sd["seq"]
            ok(items is not None and len(items) == len(seq) and all(x.startswith(k) for x, k in zip(items, seq)) and "Kho" in items[0]
               and any(k.startswith("Báo lỗi") for k in seq) and any(k.startswith("Kết quả sửa") for k in seq),
               "L1 mặc định Cũ → mới: phiếu giao nhận + báo lỗi + Kết quả sửa xếp chung đúng thứ tự giờ lập", {"seq": seq, "trang": [x[:40] for x in (items or [])]})
            mt = await m.inner_text()
            tra = [x for x in (items or []) if "chưa sửa được" in x]
            ok("Giao · Hàng Sửa (trả lại — chưa sửa được)" in mt and tra and "nhận vào ô Hàng lỗi" in tra[0] and "nhận vào ô Hàng lỗi đã xử lý" not in tra[0],
               "L2 phiếu trả lại GC ô Hàng lỗi: nhãn \"Hàng Sửa (trả lại — chưa sửa được)\", \"nhận vào ô Hàng lỗi\" (trước: ô Hàng lỗi đã xử lý)",
               [l for l in mt.split("\n") if "Hàng Sửa" in l][:4] + tra[:1])
            tg = m.locator("button:has-text('Cũ → mới')")
            if await tg.count(): await tg.first.click(); await pg.wait_for_timeout(900)
            items2 = await m.evaluate(LOG_JS)
            ok(items2 is not None and await m.locator("button:has-text('Mới → cũ')").count() == 1 and items2 == list(reversed(items)),
               "L3 bấm \"Cũ → mới\" → \"Mới → cũ\", thứ tự đảo ngược", (items2 or [])[:1])
            sel = m.locator("select", has=pg.locator("option", has_text="Tất cả")).first
            opts = await sel.evaluate("s => Array.from(s.options).map(o => o.text)") if await sel.count() else []
            itB = itA = itAll = None
            if sd["caseB"] in opts:
                await sel.select_option(label=sd["caseB"]); await pg.wait_for_timeout(900); itB = await m.evaluate(LOG_JS)
                await sel.select_option(label=sd["caseA"]); await pg.wait_for_timeout(900); itA = await m.evaluate(LOG_JS)
                await sel.select_option(label="Tất cả"); await pg.wait_for_timeout(900); itAll = await m.evaluate(LOG_JS)
            ok(opts == ["Tất cả", sd["caseA"], sd["caseB"], sd["caseC"]], "L4 ô Mã vụ lỗi: Tất cả + vụ A + vụ B + vụ C (báo lỗi)", opts)
            ok(itB is not None and len(itB) == sd["nTxB"] and all(sd["caseB"] in x for x in itB),
               f"L5 lọc vụ B → {sd['nTxB']} mục (phiếu + Kết quả sửa), mục nào cũng có mã vụ B", itB)
            ok(itA is not None and len(itA) == sd["nTxA"] and all(sd["caseA"] in x and sd["caseB"] not in x for x in itA),
               f"L6 lọc vụ A → {sd['nTxA']} mục, không mục nào vụ B", itA)
            ok(itAll is not None and len(itAll) == len(items), "L7 Tất cả → đủ lại", len(itAll or []))
        except Exception as e:
            ok(False, "L! pha [L] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])
        await b.close()
    print("KẾT QUẢ: ĐẠT hết" if fails == 0 else f"KẾT QUẢ: {fails} SAI")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
