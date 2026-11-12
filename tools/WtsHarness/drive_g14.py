# [GÓI 14 11/11/2026][KHACH-GUI-LAI] Khách gửi bù phôi — bấm trang thật (harness EF giả).
#   Nguồn: KHÁCH 10/11 vòng 1 câu 1a A (phôi bù vào Kho ô Hàng lỗi đã xử lý = phôi còn, gắn lần lỗi cũ) + 1b A (chặn bù vượt);
#     HÙNG 11/11 14:39 duyệt thiết kế + SQL 15_KHBU (trần chặn cả Workflow OFF).
#   Dữ liệu: POST /harness/g14-seed (HarnessG14 — PO-G14: Kho trả khách 2 phôi lỗi = vụ A; Gia công huỷ 1 = vụ B; Kho phôi còn 0).
#   Z1 form Kho: nút "Khách gửi bù" → 2 lần lỗi (A còn bù 2, B còn bù 1) + chữ trần; chọn A: số điền sẵn 2; nhập 3 → báo vượt, nút Xác nhận khoá;
#      nhập 2 + ghi chú → lưu: DB 1 phiếu bù, Kho phôi còn 2 (Nhận · Hàng lỗi đã xử lý 2), sổ cân.
#   Z2 mở lại: chỉ còn vụ B. Z3 dòng B "Khách gửi bù 2 cho VL…", khung Đang ở đâu "Khách gửi bù 2", bảng hàng lỗi cột "Đã bù".
#   Z4 Lịch sử phiếu: phiếu bù + nút "Huỷ phiếu bù" → huỷ (lý do) → DB đã huỷ, Kho phôi còn 0. Z5 form Gia công: không có nút "Khách gửi bù".
#   Bản cd105df (trước Gói 14): không có nút "Khách gửi bù" → Z1 SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_g14.py <port> <nhãn>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if (not cond and info not in (None, "")) else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg, po): return pg.locator("tr", has_text=po).first
async def open_form(pg, po, grp):
    await row(pg, po).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    sel = modal(pg).locator("select").first
    if await sel.count(): await sel.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def btn_texts(m):
    return [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def qty(m, pg, n):
    q = m.locator("input[type=number]").first; await q.fill(str(n)); await q.dispatch_event("change"); await pg.wait_for_timeout(600)

async def main():
    seed = post("/harness/g14-seed"); print("seed", seed)
    vA = seed["vA"]; vB = seed["vB"]
    d0 = get("/harness/g14")
    ok(d0["balanced"] and d0["neg"] == 0 and d0["khoConGiao"] == 0 and d0["bu"] == [], "seed: sổ cân, Kho phôi còn 0, chưa có bù", d0)
    async with async_playwright() as p:
        br = await p.chromium.launch()
        pg = await (await br.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[Z1] [KHACH-GUI-LAI] Kho: Khách gửi bù — chọn lần lỗi, trần, lưu")
        try:
            m = await open_form(pg, "PO-G14", "Kho")
            kb = m.locator("button.gn-cellbtn", has_text="Khách gửi bù")
            ok(await kb.count() == 1, "Z11 form Kho có nút 'Khách gửi bù'", await btn_texts(m))
            await kb.first.click(); await pg.wait_for_timeout(900)
            cs = [x for x in await btn_texts(m) if x.startswith("VL")]
            ok(len(cs) == 2 and "còn bù 2" in cs[0] and "trả khách 2 + huỷ 0 − đã bù 0" in cs[0] and "còn bù 1" in cs[1] and "trả khách 0 + huỷ 1 − đã bù 0" in cs[1],
               "Z12 2 lần lỗi: A (trả khách 2) còn bù 2, B (huỷ 1) còn bù 1", cs)
            await m.locator("button.gn-cellbtn", has_text="còn bù 2").first.click(); await pg.wait_for_timeout(800)
            v = await m.locator("input[type=number]").first.input_value()
            ok(v == "2", "Z13 số điền sẵn = còn bù 2", v)
            await qty(m, pg, 3)
            t = await m.inner_text()
            b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
            ok("Vượt tối đa 2" in t and await b.is_disabled(), "Z14 nhập 3 > 2: báo vượt + nút Xác nhận khoá", t[-400:])
            await qty(m, pg, 2)
            note = m.locator("input[type=text]").last; await note.fill("PX-G14"); await note.dispatch_event("change"); await pg.wait_for_timeout(400)
            t = await m.inner_text()
            ok("Sẽ ghi: phiếu khách gửi bù 2" in t and "Hàng lỗi đã xử lý (phôi còn)" in t, "Z15 chữ 'Sẽ ghi' đúng", t[-400:])
            dis = await b.is_disabled()
            if not dis: await b.click(); await pg.wait_for_timeout(2500)
            d1 = get("/harness/g14")
            bu = d1["bu"]
            ok(not dis and len(bu) == 1 and bu[0]["qty"] == 2 and bu[0]["ngCaseId"] == vA and bu[0]["notes"] == "PX-G14" and not bu[0]["isVoided"],
               "Z16 DB: 1 phiếu bù 2 cho vụ A, ghi chú PX-G14", d1)
            ok(d1["khoConGiao"] == 2 and d1["khoNhanXl"] == 2 and d1["rowBu"] == 2 and d1["balanced"] and d1["neg"] == 0 and d1["daBu"].get(str(vA)) == 2,
               "Z17 sổ: Kho phôi còn 2, Nhận · Hàng lỗi đã xử lý 2, bù 2, cân", d1)
            await close_modal(pg)
        except Exception as e:
            ok(False, "Z1! lỗi bấm", e); await close_modal(pg)

        print("[Z2] Mở lại: chỉ còn vụ B được bù")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G14", "Kho")
            await m.locator("button.gn-cellbtn", has_text="Khách gửi bù").first.click(); await pg.wait_for_timeout(900)
            cs = [x for x in await btn_texts(m) if x.startswith("VL")]
            ok(len(cs) == 1 and "còn bù 1" in cs[0], "Z21 chỉ còn vụ B (còn bù 1) — tự chọn sẵn khi chỉ 1 vụ", cs)
            v = await m.locator("input[type=number]").first.input_value()
            ok(v == "1", "Z22 số điền sẵn 1", v)
            await close_modal(pg)
        except Exception as e:
            ok(False, "Z2! lỗi bấm", e); await close_modal(pg)

        print("[Z3] Dòng B + Đang ở đâu + bảng hàng lỗi")
        try:
            await row(pg, "PO-G14").locator("button[title^='Xem từng bước']").first.click(); await pg.wait_for_timeout(3500)
            body = await pg.locator("body").inner_text()
            ok(f"Khách gửi bù 2 cho VL" in body and "phiếu bù #" in body, "Z31 dòng B: 'Khách gửi bù 2 cho VL… · phiếu bù #'", "")
            ok("Khách gửi bù 2" in body and "+ bù 2" in body, "Z32 Đang ở đâu: 'Khách gửi bù 2', cân = phôi 10 + bù 2", "")
            ok("Đã bù" in body, "Z33 bảng hàng lỗi có cột 'Đã bù'", "")
            await pg.screenshot(path=f"g14_{OUT}_z3.png", full_page=False)
        except Exception as e:
            ok(False, "Z3! lỗi bấm", e)

        print("[Z4] Lịch sử phiếu: huỷ phiếu bù")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            await row(pg, "PO-G14").locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2000)
            m = modal(pg)
            t = await m.inner_text()
            ok("Phiếu bù #" in t and "Kho · khách gửi bù" in t and "2 phôi" in t, "Z41 Lịch sử: phiếu bù 2 phôi", t[:500])
            await m.locator("button:has-text('Huỷ phiếu bù')").first.click(); await pg.wait_for_timeout(800)
            note = await pg.locator(".mes-modal .alert-warning").first.inner_text()
            ok("Huỷ phiếu bù #" in note and "Kho bớt 2 phôi còn" in note, "Z42 ghi chú huỷ", note)
            await pg.fill(".mes-modal input[placeholder^='Nhập lý do']", "khach gui nham")
            await pg.locator(".mes-modal-footer button.btn-danger").click(); await pg.wait_for_timeout(2000)
            d4 = get("/harness/g14")
            ok(d4["bu"][0]["isVoided"] and d4["bu"][0]["voidReason"] == "khach gui nham" and d4["khoConGiao"] == 0 and d4["rowBu"] == 0 and d4["balanced"],
               "Z43 DB: phiếu bù đã huỷ (lý do), Kho phôi còn 0, cân", d4)
            t2 = await modal(pg).inner_text()
            ok("ĐÃ HUỶ" in t2, "Z44 Lịch sử hiện ĐÃ HUỶ", t2[:400])
            await close_modal(pg)
        except Exception as e:
            ok(False, "Z4! lỗi bấm", e); await close_modal(pg)

        print("[Z5] Form Gia công: không có 'Khách gửi bù'")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G14", "Gia công")
            ok(await m.locator("button.gn-cellbtn", has_text="Khách gửi bù").count() == 0, "Z51 chỉ Kho có nút 'Khách gửi bù'", await btn_texts(m))
            await close_modal(pg)
        except Exception as e:
            ok(False, "Z5! lỗi bấm", e); await close_modal(pg)

        await br.close()
    print(f"\nKẾT QUẢ drive_g14 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
