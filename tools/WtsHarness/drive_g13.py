# [GÓI 13 10/11/2026] Khách 10/11 (vòng 1 + hỏi lại + hỏi tiếp) — bấm trang thật (harness EF giả).
#   Dữ liệu: POST /harness/g13-seed (HarnessG13.SeedAsync — PO-G13: GC NC01 → NC02 → NC03 H01 (máy loại trừ cuối, Taro 9X ở HTSP, mã 1 NC);
#     Kho còn 2 phôi; GC chờ NC02 6, Đã làm chưa giao 4; HTSP đã làm xong 2).
#   G1 [GC-LOI-TRUOC-NCDAU] form Giao, Gia công, Hàng lỗi, "Đã làm xong NC01 · chưa làm NC02" (6): không có ô tích "Lỗi công đoạn trước",
#      có dòng giải thích; "Đã làm xong NC02 (NC cuối)" cũng vậy.
#   G2 [KHO-NC-DAU] form Giao, Kho, Hàng tốt: không có ô "Nhận vào NC" (phôi luôn vào NC đầu); Xác nhận 1 → phiếu Kho → Gia công không NC.
#   G3 [GN-NGUOC-CHAN] form Giao, HTSP, Hàng tốt: ô "Giao đến nhóm" không có Kho / Gia công (nhóm trước); có KCS / Đóng gói.
#   G4 [GC-HTSP-NC-HIEN] form Giao, Gia công, Hàng tốt "Đã làm xong NC02 (NC cuối)", nhóm nhận HTSP: chữ "để làm NC03 (Taro)".
#   G5 [WTS-TARO-NC] trang Nhập WTS (htsp01), Taro, PO-G13, mã 9X (chỉ 1 NC): dưới ô Mã CV hiện "NC 3" (không phải ô chọn).
#   Bản 9bd3973: G1 / G2 / G3 / G4 / G5 SAI.
# Chạy trên harness VỪA khởi động: python3 drive_g13.py <port> <nhãn>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg): return pg.locator("tr", has_text="PO-G13").first
async def open_form(pg, grp):
    await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    await modal(pg).locator("select").first.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def confirm(m, pg):
    b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
    dis = await b.is_disabled()
    if not dis: await b.click(); await pg.wait_for_timeout(2500)
    return not dis
def sane(d, name):
    ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", d)

async def main():
    seed = post("/harness/g13-seed"); print("seed", seed)
    d0 = get("/harness/g13"); sane(d0, "seed")
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[G1] [GC-LOI-TRUOC-NCDAU] hàng đã gia công: không tích Lỗi công đoạn trước")
        try:
            m = await open_form(pg, "Gia công")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Đã làm xong NC01 · chưa làm NC02")
            t = await m.inner_text()
            ok(await m.locator("input#gxlPrev").count() == 0 and "Hàng đã qua NC" in t, "G11 chờ NC02 (đã gia công NC01): không có ô tích, có dòng giải thích", t[-400:])
            await click(m, pg, "Đã làm xong NC02 (NC cuối)")
            t = await m.inner_text()
            ok(await m.locator("input#gxlPrev").count() == 0 and "Hàng đã qua NC" in t, "G12 đã xong NC cuối: không có ô tích", t[-400:])
            await close_modal(pg)
        except Exception as e:
            ok(False, "G1! lỗi bấm", e); await close_modal(pg)

        print("[G2] [KHO-NC-DAU] Kho giao Gia công: không chọn NC")
        try:
            m = await open_form(pg, "Kho")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Phôi còn (chưa giao)")
            t = await m.inner_text()
            ok("Nhận vào NC" not in t and "Gia công sẽ nhận vào ô Hàng mới" in t, "G21 Kho → Gia công: không có ô 'Nhận vào NC'", t[-500:])
            q = m.locator("input[type=number]").first; await q.fill("1"); await pg.wait_for_timeout(400)
            done = await confirm(m, pg)
            d = get("/harness/g13"); nt = d["txs"][-1]
            ok(done and nt["fromGroupCode"] == "KHO" and nt["toGroupCode"] == "GC" and nt["toNC"] is None and nt["qtyIssued"] == 1,
               "G22 DB: phiếu Kho → Gia công 1, không NC (vào NC đầu)", nt)
            sane(d, "G2")
            await close_modal(pg)
        except Exception as e:
            ok(False, "G2! lỗi bấm", e); await close_modal(pg)

        print("[G3] [GN-NGUOC-CHAN] HTSP Hàng tốt: nhóm nhận không có nhóm trước")
        try:
            m = await open_form(pg, "HTSP")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Đã làm xong · chưa giao")
            sel = m.locator("select:has(option:has-text('-- Chọn nhóm nhận --'))").first
            opts = [o.strip() for o in await sel.locator("option").all_inner_texts()]
            ok("Kho" not in opts and "Gia công" not in opts and "KCS" in opts and "Đóng gói" in opts, "G31 HTSP Hàng tốt: nhóm nhận KCS / Đóng gói, không Kho / Gia công", opts)
            await close_modal(pg)
        except Exception as e:
            ok(False, "G3! lỗi bấm", e); await close_modal(pg)

        print("[G4] [GC-HTSP-NC-HIEN] Gia công giao HTSP: 'để làm NC03 (Taro)'")
        try:
            m = await open_form(pg, "Gia công")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Đã làm xong NC02 (NC cuối)")
            sel = m.locator("select:has(option:has-text('-- Chọn nhóm nhận --'))").first
            await sel.select_option(label="HTSP"); await pg.wait_for_timeout(700)
            t = await m.inner_text()
            ok("để làm NC03 (Taro)" in t, "G41 chữ 'HTSP sẽ nhận vào ô Hàng mới · để làm NC03 (Taro)'", t[-500:])
            opts = [o.strip() for o in await sel.locator("option").all_inner_texts()]
            ok("Kho" not in opts and "HTSP" in opts, "G42 Gia công Hàng tốt: nhóm nhận không có Kho", opts)
            await close_modal(pg)
        except Exception as e:
            ok(False, "G4! lỗi bấm", e); await close_modal(pg)

        print("[G5] [WTS-TARO-NC] WTS Taro mã 1 NC: hiện sẵn NC")
        try:
            ctx = await b.new_context(); ctx.set_default_timeout(15000); w = await ctx.new_page(); await w.set_viewport_size({"width": 1900, "height": 1200})
            await w.goto(BASE + "/login"); await w.fill("#username", "htsp01"); await w.fill("#password", "123456")
            await w.click("button[type=submit]"); await w.wait_for_load_state("networkidle")
            await w.goto(BASE + "/hieu-suat-cong-nhan"); await w.wait_for_timeout(1800)
            await w.click("button:has-text('Ca 1')"); await w.wait_for_timeout(800)
            if await w.locator("input[placeholder='Gõ PO...']").count() == 0:
                await w.click("button:has-text('Thêm nhóm')"); await w.wait_for_timeout(500)
            g = w.locator("table", has=w.locator("input[placeholder='Gõ PO...']")).first
            r = g.locator("tr", has=w.locator("input[placeholder='Gõ PO...']")).nth(0)
            inp = r.locator("input[placeholder='Gõ PO...']"); await inp.fill("PO-G13"); await inp.press("Tab"); await w.wait_for_timeout(800)
            await g.locator("select").nth(0).select_option("Taro"); await w.wait_for_timeout(500)
            cv = r.locator("input[placeholder='Mã CV...']"); await cv.fill("9X"); await cv.dispatch_event("change"); await w.wait_for_timeout(700)
            t = await r.inner_text()
            ok("NC 3" in t and await r.locator("select[title^='Mã này dùng ở nhiều NC Taro']").count() == 0, "G51 mã 9X (1 NC): dưới ô Mã CV hiện 'NC 3', không có ô chọn", t[:300])
        except Exception as e:
            ok(False, "G5! lỗi bấm", e)
        await b.close()
    print(f"\n{OUT}: {'ĐẠT hết' if fails == 0 else str(fails) + ' SAI'}")

asyncio.run(main())
