# [GN-FORM1 05/11/2026] Trang Giao nhận thật (harness EF giả) — form "Giao / xử lý hàng" 1 form 4 bước (khách anh Ngọc duyệt mockup 05/11).
#   Dữ liệu: POST /harness/sua-seed (PO-SUA: GC NC01 → NC02, GC đã nhận 8 Hàng mới; Kho báo 2 phôi lỗi → gửi GC nhờ sửa, GC giữ ô Hàng sửa 2).
#   D1 [K1][K3][K4] nút Giao mở form mới (không còn link đỏ Báo lỗi / link Kết quả sửa / mục Khác); bước 2 Gia công theo NC:
#       hàng lỗi "Vừa nhận, chưa làm NC01" có 8 · "Đã làm xong NC01 · chưa làm NC02" 0 · "Đã làm xong NC02 (NC cuối) · chưa giao" 0 · Tồn hàng sửa 2
#   D2 [K1] lỗi công đoạn trước (Kho), chiếc chưa làm NC01 → Trả nơi gây lỗi → DB 1 phiếu Hàng lỗi công đoạn trước (FromNC NC01), không có báo lỗi
#   D3 [K2] lỗi đầu ra NC01 → Sửa tại nhóm đạt 1: NC làm tiếp chỉ "NC01 — mặc định" → DB báo lỗi 1 + Kết quả sửa ToNC NC01 cùng vụ
#   D4 nút Xác nhận khoá khi vượt tối đa / thiếu loại lỗi
#   D5 Tồn hàng sửa → Trả lại nơi gửi (chưa sửa được): nhóm nhận mặc định Kho → DB phiếu REPAIR_RETURN ô Hàng sửa → Hàng lỗi
#   D6 [K2] rà độc lập V3: Kho nhận phiếu trả (D2) → Tồn hàng lỗi vụ phát hiện ở Gia công NC01 → giao Gia công, đổi ô bên nhận "Hàng lỗi đã xử lý":
#       hiện "Gia công nhận vào NC (theo vụ)" mặc định NC01 → DB phiếu ToNC NC01 cùng vụ (trước: không NC → Gia công nhận như hàng mới)
#   Mọi bước: sổ cân, 0 ô âm.
# Chạy trên harness VỪA khởi động: python3 drive_form.py <port> <old|new>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg): return pg.locator("tr", has_text="PO-SUA").first
async def btn_texts(m):
    return [ (await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all() ]
async def open_form(pg, grp):
    await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    await modal(pg).locator("select").first.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def pick_cat(m, pg, code):
    inp = m.locator("input[list^='ngcat_']").first
    await inp.fill(code); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
async def confirm(m, pg):
    b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
    dis = await b.is_disabled()
    if not dis: await b.click(); await pg.wait_for_timeout(2500)
    return not dis
def sane(d, name):
    ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", d.get("groups"))

async def main():
    post("/harness/sua-seed")
    d0 = get("/harness/form")
    cat = d0["cats"][0]
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[D1] form mới + chữ bước 2 theo NC")
        m = await open_form(pg, "Gia công")
        t = await m.inner_text()
        ok("Giao / xử lý hàng" in t and "Hàng tốt" in t and "Hàng lỗi" in t, "D11 nút Giao mở form 'Giao / xử lý hàng' (bước 1 Hàng tốt / Hàng lỗi)", t[:200])
        ok(all(x not in t for x in ["Báo hàng lỗi phát hiện sau khi nhận", "Ghi Kết quả sửa", "Khác: giao theo NC"]), "D12 [K4] không còn link đỏ Báo lỗi / link Kết quả sửa / mục Khác", t[:400])
        await click(m, pg, "Hàng lỗi")
        bt = await btn_texts(m)
        def has(lbl, n): return any(x.startswith(lbl) and x.endswith(f"có {n}") for x in bt)
        ok(has("Vừa nhận, chưa làm NC01", 8) and has("Đã làm xong NC01 · chưa làm NC02", 0) and has("Đã làm xong NC02 (NC cuối) · chưa giao", 0)
           and has("Đang ở Tồn hàng lỗi (đã có vụ)", 0) and has("Đang ở Tồn hàng sửa (nhóm khác gửi nhờ sửa)", 2),
           "D13 [K2][K3] bước 2 Gia công: Vừa nhận chưa làm NC01 có 8 · Đã làm xong NC01 · chưa làm NC02 có 0 · Đã làm xong NC02 (NC cuối) · chưa giao có 0 · Tồn hàng lỗi 0 · Tồn hàng sửa 2", bt)
        await click(m, pg, "Hàng tốt")
        bt2 = await btn_texts(m)
        ok(any(x.startswith("Đã làm xong NC01 · chưa làm NC02") and "làm hộ" in x for x in bt2) and any(x.startswith("Đã làm xong NC02 (NC cuối) · chưa giao") for x in bt2),
           "D14 [K2](a) hàng tốt Gia công: giao theo NC (làm hộ) từ NC01 + Đã làm xong NC cuối", bt2)
        await close_modal(pg)

        print("[D2] lỗi công đoạn trước, chiếc chưa làm NC01 → trả Kho (1 phiếu Hàng lỗi công đoạn trước)")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Vừa nhận, chưa làm NC01")
        await m.locator("input#gxlPrev").check(); await pg.wait_for_timeout(700)
        # [CAUSE-BAT-BUOC 2026-11-10] tích xong: ô Nơi gây lỗi trống (không điền sẵn), nhắc bắt buộc, chưa có "Trả nơi gây lỗi". Nguồn: HÙNG 10/11 16:52
        cs = m.locator("select:has(option:text-is('-- Chọn nơi gây lỗi --'))")
        t0 = await m.inner_text()
        ok(await cs.count() == 1 and await cs.input_value() == "" and "Chọn nơi gây lỗi (bắt buộc)" in t0, "D20a [CAUSE-BAT-BUOC] tích Lỗi công đoạn trước: nơi gây lỗi trống, nhắc bắt buộc", t0[-400:])
        await click(m, pg, "Giao cho nhóm khác")
        t0 = await m.inner_text()
        ok("Trả nơi gây lỗi" not in t0 and "Chọn nơi gây lỗi ở trên trước" in t0 and "sẽ nhận vào ô" not in t0,
           "D20b chưa chọn nơi gây lỗi → chưa có nút 'Trả nơi gây lỗi', chưa xem trước chứng từ / ô nhận (rà Thấp 2)", t0[-400:])
        q0 = m.locator("input[type=number]").first; await q0.fill("1"); await q0.dispatch_event("change"); await pg.wait_for_timeout(400)
        await pick_cat(m, pg, cat)
        ok(await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").is_disabled(), "D20c đủ số lượng + loại lỗi nhưng chưa chọn nơi gây lỗi → nút Xác nhận tắt")
        await cs.select_option("KHO"); await pg.wait_for_timeout(700)
        t = await m.inner_text()
        ok("Trả nơi gây lỗi (Kho)" in t and "Kho sẽ nhận vào ô Hàng lỗi" in t, "D21 (b) chọn Kho gây lỗi → có 'Trả nơi gây lỗi (Kho)', Kho sẽ nhận vào ô Hàng lỗi", t[-500:])
        q = m.locator("input[type=number]").first; await q.fill("1"); await q.dispatch_event("change"); await pg.wait_for_timeout(400)
        await pick_cat(m, pg, cat)
        t = await m.inner_text()
        ok("phiếu Hàng lỗi công đoạn trước" in t and "1 chứng từ" in t, "D22 dòng 'Sẽ ghi: phiếu Hàng lỗi công đoạn trước … (1 chứng từ)'", t[-300:])
        sent = await confirm(m, pg)
        d2 = get("/harness/form")
        nt = [x for x in d2["txs"] if x["purpose"] == "NG_RETURN"]
        ok(sent and len(nt) == 1 and nt[0]["fromNC"] == "NC01" and nt[0]["toGroupCode"] == "KHO" and nt[0]["qtyIssued"] == 1 and len(d2["reports"]) == len(d0["reports"]),
           "D23 DB: 1 phiếu Hàng lỗi công đoạn trước GC → Kho 1 chiếc chốt NC01, không thêm báo lỗi", nt)
        ok(await pg.locator(".mes-modal").count() == 0, "D24 lưu xong form đóng")
        sane(d2, "D25")

        print("[D3] [K2] lỗi đầu ra ở NC01 → sửa tại nhóm, NC làm tiếp đúng NC của vụ")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Vừa nhận, chưa làm NC01"); await click(m, pg, "Sửa tại nhóm")
        nums = m.locator("input[type=number]")
        await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change"); await pg.wait_for_timeout(600)
        sel = m.locator("select", has=pg.locator("option", has_text="mặc định")).first
        opts = [ (await o.inner_text()).strip() for o in await sel.locator("option").all() ] if await sel.count() else []
        ok(opts == ["NC01 — mặc định"], "D31 NC làm tiếp: chỉ 'NC01 — mặc định' (vụ ở NC01, không có NC sau)", opts)
        await pick_cat(m, pg, cat)
        sent = await confirm(m, pg)
        d3 = get("/harness/form")
        nr = [x for x in d3["reports"] if x not in d2["reports"]]; nk = [x for x in d3["reps"] if x not in d2["reps"]]
        ok(sent and len(nr) == 1 and nr[0]["nc"] == "NC01" and nr[0]["origin"] == "DAU_RA" and len(nk) == 1 and nk[0]["toNC"] == "NC01" and nk[0]["qtyPass"] == 1
           and nk[0]["sourceCell"] == "LOI" and nk[0]["ngCaseId"] == nr[0]["ngCaseId"],
           "D32 DB: báo lỗi 1 (NC01, đầu ra) + Kết quả sửa đạt 1 làm tiếp NC01, cùng vụ — 1 lần lưu", {"bl": nr, "kq": nk})
        ok(d3["groups"]["GC"]["choKcs"] == 1, "D33 Gia công: Chờ KCS 1")
        sane(d3, "D34")

        print("[D4] nút Xác nhận khoá khi vượt tối đa / thiếu loại lỗi")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Vừa nhận, chưa làm NC01"); await click(m, pg, "Chỉ báo lỗi")
        q = m.locator("input[type=number]").first; await q.fill("99"); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
        await pick_cat(m, pg, cat)
        b1 = m.locator(".mes-modal-footer button.btn-primary")
        ok(await b1.is_disabled() and "Vượt tối đa" in (await m.inner_text()), "D41 vượt tối đa: báo đỏ + nút Xác nhận khoá")
        await q.fill("1"); await q.dispatch_event("change"); await pg.wait_for_timeout(400)
        inp = m.locator("input[list^='ngcat_']").first; await inp.fill("zzz"); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
        ok(await b1.is_disabled(), "D42 loại lỗi gõ không có trong DS Lỗi: nút Xác nhận khoá")
        await close_modal(pg)
        ok(get("/harness/form")["reports"] == d3["reports"], "D43 không ghi gì khi đóng form")

        print("[D5] Tồn hàng sửa → trả lại nơi gửi (chưa sửa được)")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng sửa")
        bt = await btn_texts(m)
        # [SUA-QUA-KCS 2026-11-11] Workflow ON ẩn "Sửa xong, trả nơi gửi" (Hùng 11/11 15:21) — mọi hàng sửa qua Kết quả sửa + KCS xác nhận
        ok(any(x == "Trả lại nơi gửi (chưa sửa được)" for x in bt) and not any(x == "Sửa xong, trả nơi gửi" for x in bt) and any(x.startswith("Sửa tại nhóm") for x in bt),
           "D51 [SUA-QUA-KCS] Tồn hàng sửa: Sửa tại nhóm · Trả lại nơi gửi (chưa sửa được) — không có 'Sửa xong, trả nơi gửi' (Workflow ON)", bt)
        await click(m, pg, "Trả lại nơi gửi")
        dsel = m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first
        cur = await dsel.evaluate("s => s.options[s.selectedIndex].text")
        ok(cur == "Kho", "D52 nhóm nhận mặc định = Kho (nơi gửi nhờ sửa)", cur)
        q = m.locator("input[type=number]").first; await q.fill("1"); await q.dispatch_event("change"); await pg.wait_for_timeout(400)
        sent = await confirm(m, pg)
        d5 = get("/harness/form")
        rr = [x for x in d5["txs"] if x["purpose"] == "REPAIR_RETURN"]
        ok(sent and len(rr) == 1 and rr[0]["fromCell"] == "SUA" and rr[0]["toCell"] == "LOI" and rr[0]["toGroupCode"] == "KHO" and rr[0]["ngCaseId"],
           "D53 DB: phiếu Gia công → Kho 1, ô Hàng sửa → ô Hàng lỗi, cùng vụ", rr)
        sane(d5, "D54")

        print("[D6] [K2] V3: hàng của vụ giao vào Gia công ô Hàng lỗi đã xử lý → NC theo vụ")
        rv = post("/harness/form-recv")
        ok(all(x.endswith(" ok") for x in rv["done"]) and len(rv["done"]) == 2, "D61 Kho nhận 2 phiếu đang chờ (phiếu trả D2 + D5)", rv)
        d6a = get("/harness/form")
        ret = [x["ngCaseId"] for x in d6a["txs"] if x["purpose"] == "NG_RETURN" and x["toGroupCode"] == "KHO"]
        vu3 = [c for c in d6a["cases"] if ret and c["ngCaseId"] == ret[0] and c["detectGroupCode"] == "GC" and c["detectNC"] == "NC01"]
        await pg.reload(); await pg.wait_for_timeout(4000)
        m = await open_form(pg, "Kho")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi")
        cbs = m.locator("button.gn-cellbtn", has_text="Gia công")
        n_c = await cbs.count()
        ok(n_c >= 1 and len(vu3) == 1, "D62 Tồn hàng lỗi Kho có vụ phát hiện ở Gia công NC01", [await cbs.nth(i).inner_text() for i in range(n_c)])
        if n_c >= 1:
            await cbs.first.click(); await pg.wait_for_timeout(700)
        await click(m, pg, "Giao cho nhóm khác")
        dsel = m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first
        await dsel.select_option(label="Gia công"); await pg.wait_for_timeout(800)
        t0 = await m.inner_text()
        ok("Gia công nhận vào NC (theo vụ)" not in t0, "D63 ô Hàng lỗi (mặc định): không hỏi NC", t0[-400:])
        await m.locator("button:has-text('Tuỳ chọn: đổi ô bên nhận')").click(); await pg.wait_for_timeout(500)
        # [SUA-QUA-KCS 2026-11-11] Workflow ON: không có ô Hàng lỗi đã xử lý / Hàng mới bên nhận (Hùng 11/11 15:34 B1) — luật NC theo vụ (D64–D65) kiểm lúc Workflow OFF
        cells_on = [(await x.inner_text()).strip() for x in await m.locator("button.gn-cellbtn").all()]
        ok("Hàng lỗi đã xử lý" not in cells_on and "Hàng mới" not in cells_on, "D63b [SUA-QUA-KCS] Workflow ON: ô bên nhận không có Hàng lỗi đã xử lý / Hàng mới", cells_on)
        await close_modal(pg)
        post("/harness/g16-wf?po=PO-SUA&on=0")
        await pg.reload(); await pg.wait_for_timeout(4000)
        m = await open_form(pg, "Kho")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi")
        cbs = m.locator("button.gn-cellbtn", has_text="Gia công")
        if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
        await click(m, pg, "Giao cho nhóm khác")
        dsel = m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first
        await dsel.select_option(label="Gia công"); await pg.wait_for_timeout(800)
        await m.locator("button:has-text('Tuỳ chọn: đổi ô bên nhận')").click(); await pg.wait_for_timeout(500)
        await m.locator("button.gn-cellbtn", has_text="Hàng lỗi đã xử lý").first.click(); await pg.wait_for_timeout(800)
        nsel = m.locator("select", has=pg.locator("option", has_text="mặc định")).last
        opts = await nsel.evaluate("s => Array.from(s.options).map(o => o.text)") if await nsel.count() else []
        t1 = await m.inner_text()
        ok("Gia công nhận vào NC (theo vụ)" in t1 and opts[:1] == ["NC01 — mặc định"], "D64 Workflow OFF: đổi sang Hàng lỗi đã xử lý: hiện 'Gia công nhận vào NC (theo vụ)' mặc định NC01", opts)
        q = m.locator("input[type=number]").first; await q.fill("1"); await q.dispatch_event("change"); await pg.wait_for_timeout(400)
        sent = await confirm(m, pg)
        d6 = get("/harness/form")
        nt = [x for x in d6["txs"] if x["fromGroupCode"] == "KHO" and x["toGroupCode"] == "GC" and x["toCell"] == "XL"]
        ok(sent and len(nt) == 1 and nt[0]["toNC"] == "NC01" and vu3 and nt[0]["ngCaseId"] == vu3[0]["ngCaseId"],
           "D65 DB: phiếu Kho → Gia công ô Hàng lỗi đã xử lý mang NC01, cùng vụ", nt)
        sane(d6, "D66")
        await b.close()
    print(f"\nKẾT QUẢ drive_form ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
