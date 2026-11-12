# [GÓI 16 11/11/2026][SUA-QUA-KCS][SUA-HO-TRA-NGUOC][XL-TRA-NOILOI][XL-CHU-KCS] Khách 11/11 — bấm trang thật (harness EF giả).
#   Nguồn: KHÁCH 11/11 ("OK trả bộ phận báo lỗi, NG trả bộ phận làm lỗi", "KCS chỉ là trung gian đánh giá tạm hàng sửa", "bộ phận khác sửa hộ xong thì chuyển trả thẳng
#     bộ phận báo lỗi", "KCS kiểm lại nhưng WTS ghi Việc không làm ra SP"); HÙNG 11/11 15:21 (ẩn "Sửa xong, trả nơi gửi" khi Workflow ON) + 15:34 (B1).
#   Dữ liệu: POST /harness/g16-seed (HarnessG16 — PO-G16: KCS phát hiện 2 lỗi do Gia công, GC giữ ô Hàng lỗi 2; PO-G16B: GC WTS NC01 NG 2).
#   Y1 form Gia công, Hàng lỗi, Tồn hàng lỗi, Giao: tuỳ chọn ô bên nhận KHÔNG có Hàng lỗi đã xử lý / Hàng mới (Workflow ON); gửi HTSP nhờ sửa 2 → phiếu LOI→SUA.
#   Y2 trang Nhận, HTSP: phiếu ô Hàng sửa — nút ô nhận chỉ Hàng lỗi / Hàng sửa (không Hàng lỗi đã xử lý / Hàng mới); nhận (endpoint).
#   Y3 form HTSP, Hàng lỗi, Tồn hàng sửa: việc có "Sửa tại nhóm" + "Trả lại nơi gửi", KHÔNG có "Sửa xong, trả nơi gửi"; Sửa tại nhóm 2 → Kết quả sửa @XONG.
#   Y4 KCS xác nhận → HTSP Tồn hàng lỗi đã xử lý 2. Y5 form HTSP, Hàng tốt, hàng đã sửa: nhóm nhận chỉ KCS, chữ "KCS kiểm lại … Việc không làm ra SP"; giao 2 → KCS nhận:
#      Đã làm chưa giao KCS +2, không WTS. Y6 PO-G16B: GC gửi HTSP sửa hộ 2 (vụ NC01 NG) → HTSP Kết quả sửa → KCS xác nhận → form HTSP hàng đã sửa: nhóm nhận chỉ
#      Gia công, chữ "tính là xong NC đó" → giao 2 (phiếu không NC) → GC nhận: OK NC01 +2, chờ NC02 +2, không WTS. Y30 Workflow OFF: "Sửa xong, trả nơi gửi" còn.
#   [G16-NHAN-CHONSAN 12/11] Y7 PO-G16C: phiếu GC → HTSP lập lúc OFF từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý, nay Workflow ON → trang Nhận chọn sẵn ô Hàng lỗi,
#      ẩn ô chỉ định, cảnh báo, lý do điền sẵn, nhận vào ô Hàng lỗi. Y8 PO-G16D: như Y7 nhưng phiếu không ghi FromCell (phiếu "Trả hàng đã sửa" rất cũ — ô đọc như sổ).
#      Y9 PO-G16E: Workflow OFF giữ ô bên giao chỉ định, không cảnh báo. Bản a3a3af4: Y7 / Y8 10 SAI là đúng. Nguồn: CLAUDE (sổ G16-NHAN-CHONSAN); HÙNG 12/11 08:11.
#   Bản fc55777 (trước Gói 16): Y1 có ô Hàng lỗi đã xử lý, Y3 có nút "Sửa xong", Y6 HTSP sửa hộ vào Chưa làm → SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_g16.py <port> <nhãn>
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
    await modal(pg).locator("select").first.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def btn_texts(m):
    return [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def confirm(m, pg):
    b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
    dis = await b.is_disabled()
    if not dis: await b.click(); await pg.wait_for_timeout(2500)
    return not dis
async def qty(m, pg, n, idx=0):
    q = m.locator("input[type=number]").nth(idx); await q.fill(str(n)); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
async def dest_opts(m):
    sel = m.locator("select:has(option:has-text('Chọn nhóm nhận'))").first
    return sel, ([o.strip() for o in await sel.locator("option").all_inner_texts()] if await sel.count() else [])
def sane(d, name):
    ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", d)
async def pick_case(m, pg):
    cbs = m.locator("button.gn-cellbtn", has_text="VL")
    if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)

async def recv_must_loi(pg, yy, po, nm):
    print(f"[Y{yy}] [G16-NHAN-CHONSAN] {po}: {nm}, từ ô Hàng lỗi chỉ định Hàng lỗi đã xử lý, nay Workflow ON — trang Nhận chọn sẵn ô Hàng lỗi")
    try:
        sc = post("/harness/g16c-seed?po=" + po); C = "/harness/g16?po=" + po; c0 = get(C)
        pend = [t for t in c0["txs"] if t["status"] != "COMPLETED" and t["toGroupCode"] == "HTSP"]
        ok(len(pend) == 1 and pend[0]["fromCell"] == (None if po == "PO-G16D" else "LOI") and pend[0]["toCell"] == "XL", f"Y{yy}0 dữ liệu: 1 phiếu GC → HTSP ô Hàng lỗi → Hàng lỗi đã xử lý đang chờ", c0["txs"])
        await pg.reload(); await pg.wait_for_timeout(4000)
        await row(pg, po).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
        m = modal(pg)
        if await m.locator("select").count():
            await m.locator("select").first.select_option(label="HTSP"); await pg.wait_for_timeout(1500)
        hdr = m.locator("div[style*='cursor:pointer']").first
        if await hdr.count(): await hdr.click(); await pg.wait_for_timeout(900)
        cells = [x.split(" bên giao")[0].strip() for x in await btn_texts(m)]
        on = [(await x.inner_text()).split("\n")[0].strip() for x in await m.locator("button.gn-cellbtn.on").all()]
        ok(on == ["Hàng lỗi"], f"Y{yy}1 ô nhận chọn sẵn: Hàng lỗi", {"on": on, "cells": cells})
        ok(not any(c.startswith("Hàng lỗi đã xử lý") or c.startswith("Hàng mới") for c in cells), f"Y{yy}2 không còn nút ô Hàng lỗi đã xử lý / Hàng mới", cells)
        t = await m.inner_text()
        ok("chưa qua KCS xác nhận" in t and "đã chọn sẵn ô Hàng lỗi" in t, f"Y{yy}3 dòng cảnh báo", t[:900])
        rs = m.locator("input.border-warning").first
        rv = await rs.input_value() if await rs.count() else ""
        ok("Workflow ON" in rv, f"Y{yy}4 lý do đổi ô điền sẵn", rv)
        await qty(m, pg, 2, 1)
        await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
        c1 = get(C); t1 = [t for t in c1["txs"] if t["handoverTxId"] == pend[0]["handoverTxId"]] if pend else []
        ok(t1 and t1[0]["status"] == "COMPLETED" and c1["htsp"]["loiGiu"] == c0["htsp"]["loiGiu"] + 2 and c1["htsp"]["xlGiu"] == c0["htsp"]["xlGiu"],
           f"Y{yy}5 DB: nhận đủ 2 vào ô Hàng lỗi HTSP (không vào Hàng lỗi đã xử lý)", {"tx": t1, "truoc": c0["htsp"], "sau": c1["htsp"]})
        sane(c1, "Y" + yy); await close_modal(pg)
    except Exception as e:
        ok(False, "Y" + yy + "! lỗi bấm", e); await close_modal(pg)


async def main():
    seed = post("/harness/g16-seed"); print("seed", seed)
    A = "/harness/g16?po=PO-G16"; B = "/harness/g16?po=PO-G16B"
    a0 = get(A); b0 = get(B); sane(a0, "seed A"); sane(b0, "seed B")
    async with async_playwright() as p:
        br = await p.chromium.launch()
        pg = await (await br.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[Y1] [SUA-QUA-KCS] Gia công giao Tồn hàng lỗi: không có ô bên nhận Hàng lỗi đã xử lý / Hàng mới (Workflow ON)")
        try:
            m = await open_form(pg, "PO-G16", "Gia công")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi"); await pick_case(m, pg)
            await click(m, pg, "Giao cho nhóm khác")
            nho = m.get_by_role("button", name="Gửi nhờ sửa", exact=True)
            if await nho.count(): await nho.first.click(); await pg.wait_for_timeout(600)
            sel, opts = await dest_opts(m)
            if "HTSP" in opts: await sel.select_option(label="HTSP"); await pg.wait_for_timeout(600)
            tg = m.locator("button:has-text('Tuỳ chọn: đổi ô bên nhận')")
            if await tg.count(): await tg.first.click(); await pg.wait_for_timeout(600)
            cells = [x for x in await btn_texts(m) if x in ("Hàng mới", "Hàng lỗi", "Hàng sửa", "Hàng lỗi đã xử lý")]
            ok("Hàng lỗi đã xử lý" not in cells and "Hàng mới" not in cells and "Hàng sửa" in cells, "Y11 ô bên nhận chỉ Hàng lỗi / Hàng sửa", cells)
            await qty(m, pg, 2)
            done = await confirm(m, pg)
            a1 = get(A); nt = a1["txs"][-1]
            ok(done and nt["fromGroupCode"] == "GC" and nt["toGroupCode"] == "HTSP" and nt["fromCell"] == "LOI" and nt["toCell"] == "SUA" and nt["qtyIssued"] == 2,
               "Y12 DB: phiếu GC → HTSP 2, ô Hàng lỗi → ô Hàng sửa (nhờ sửa)", nt)
            sane(a1, "Y1"); await close_modal(pg)
        except Exception as e:
            ok(False, "Y1! lỗi bấm", e); await close_modal(pg)

        print("[Y2] [SUA-QUA-KCS] Trang Nhận HTSP: phiếu ô Hàng sửa — không đổi được sang Hàng lỗi đã xử lý / Hàng mới")
        try:
            await row(pg, "PO-G16").locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
            m = modal(pg)
            if await m.locator("select").count():
                await m.locator("select").first.select_option(label="HTSP"); await pg.wait_for_timeout(1500)
            hdr = m.locator("div[style*='cursor:pointer']").first
            if await hdr.count(): await hdr.click(); await pg.wait_for_timeout(900)
            cells = [x.split(" bên giao")[0].strip() for x in await btn_texts(m)]
            ok("Hàng sửa" in " ".join(cells) and not any(c.startswith("Hàng lỗi đã xử lý") or c.startswith("Hàng mới") for c in cells), "Y21 nút ô nhận: không có Hàng lỗi đã xử lý / Hàng mới", cells)
            await close_modal(pg)
            rv = post("/harness/g16-recv?po=PO-G16"); a2 = get(A)
            ok(rv["ok"] and a2["htsp"]["suaGiu"] == 2, "Y22 HTSP nhận 2 vào Tồn hàng sửa", rv)
        except Exception as e:
            ok(False, "Y2! lỗi bấm", e); await close_modal(pg)

        print("[Y3] [SUA-QUA-KCS] HTSP Tồn hàng sửa: không có 'Sửa xong, trả nơi gửi' (Workflow ON) → Sửa tại nhóm")
        try:
            post("/harness/g16-wf?po=PO-G16&on=0")   # Y30: Workflow OFF — nút vẫn còn (họ tự kiểm soát, N7)
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16", "HTSP")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng sửa"); await pick_case(m, pg)
            bt0 = await btn_texts(m)
            ok(any("Sửa xong, trả nơi gửi" in x for x in bt0), "Y30 Workflow OFF: còn 'Sửa xong, trả nơi gửi'", bt0)
            await close_modal(pg)
            post("/harness/g16-wf?po=PO-G16&on=1")
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16", "HTSP")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng sửa"); await pick_case(m, pg)
            bt = await btn_texts(m)
            ok(not any("Sửa xong, trả nơi gửi" in x for x in bt) and any("Sửa tại nhóm" in x for x in bt) and any("Trả lại nơi gửi" in x for x in bt),
               "Y31 việc: Sửa tại nhóm + Trả lại nơi gửi, KHÔNG có 'Sửa xong, trả nơi gửi'", bt)
            await click(m, pg, "Sửa tại nhóm")
            await qty(m, pg, 2)
            done = await confirm(m, pg)
            a3 = get(A)
            ok(done and a3["reps"] and a3["reps"][-1]["groupCode"] == "HTSP" and a3["reps"][-1]["toNC"] == "@XONG" and a3["reps"][-1]["qtyPass"] == 2,
               "Y32 DB: Kết quả sửa HTSP đạt 2, dấu @XONG", a3["reps"])
            sane(a3, "Y3"); await close_modal(pg)
        except Exception as e:
            ok(False, "Y3! lỗi bấm", e); await close_modal(pg)

        print("[Y4..Y5] KCS xác nhận → HTSP giao hàng đã sửa về KCS: KCS kiểm lại, giờ ghi Việc không làm ra SP")
        try:
            xn = post("/harness/g16-xn?po=PO-G16"); a4 = get(A)
            ok(xn["ok"] and a4["htsp"]["xlGiu"] == 2, "Y41 HTSP Tồn hàng lỗi đã xử lý 2", {"xn": xn, "htsp": a4["htsp"]})
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16", "HTSP")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Hàng đã sửa — chờ trả nơi phát hiện lỗi"); await pick_case(m, pg)
            sel, opts = await dest_opts(m)
            real = [o for o in opts if "Chọn nhóm nhận" not in o]
            ok(real == ["KCS"], "Y51 nhóm nhận chỉ KCS (nơi phát hiện)", opts)
            if "KCS" in real: await sel.select_option(label="KCS"); await pg.wait_for_timeout(700)
            await qty(m, pg, 2)
            t = await m.inner_text()
            ok("KCS kiểm lại" in t and "Việc không làm ra SP" in t, "Y52 chữ: 'KCS kiểm lại, giờ ghi Việc không làm ra SP'", t[-600:])
            done = await confirm(m, pg)
            rv = post("/harness/g16-recv?po=PO-G16"); a5 = get(A)
            ok(done and rv["ok"] and a5["kcs"]["conGiao"] == a0["kcs"]["conGiao"] + 2 and a5["wts"] == a0["wts"] and a5["htsp"]["xlGiu"] == 0,
               "Y53 KCS nhận: Đã làm chưa giao +2, không thêm dòng WTS", {"truoc": a0["kcs"], "sau": a5["kcs"], "wts": [a0["wts"], a5["wts"]]})
            sane(a5, "Y5"); await close_modal(pg)
        except Exception as e:
            ok(False, "Y5! lỗi bấm", e); await close_modal(pg)

        print("[Y6] [SUA-HO-TRA-NGUOC] PO-G16B: Gia công làm hỏng ở NC01, HTSP sửa hộ → trả NGƯỢC về Gia công: tính là xong NC01")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16B", "Gia công")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi"); await pick_case(m, pg)
            await click(m, pg, "Giao cho nhóm khác")
            nho = m.get_by_role("button", name="Gửi nhờ sửa", exact=True)
            if await nho.count(): await nho.first.click(); await pg.wait_for_timeout(600)
            sel, opts = await dest_opts(m)
            if "HTSP" in opts: await sel.select_option(label="HTSP"); await pg.wait_for_timeout(600)
            await qty(m, pg, 2); await confirm(m, pg); await close_modal(pg)
            post("/harness/g16-recv?po=PO-G16B")
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16B", "HTSP")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng sửa"); await pick_case(m, pg)
            await click(m, pg, "Sửa tại nhóm"); await qty(m, pg, 2); await confirm(m, pg); await close_modal(pg)
            b6 = get(B)
            ok(b6["reps"] and b6["reps"][-1]["toNC"] == "@XONG", "Y61 Kết quả sửa HTSP (đứng sau nơi phát hiện Gia công): dấu @XONG", b6["reps"])
            post("/harness/g16-xn?po=PO-G16B"); b6b = get(B)
            ok(b6b["htsp"]["xlGiu"] == 2 and b6b["htsp"]["chuaLam"] == b0["htsp"]["chuaLam"], "Y62 KCS xác nhận → HTSP Tồn hàng lỗi đã xử lý 2, Chưa làm HTSP không đổi", b6b["htsp"])
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "PO-G16B", "HTSP")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Hàng đã sửa — chờ trả nơi phát hiện lỗi"); await pick_case(m, pg)
            sel, opts = await dest_opts(m)
            real = [o for o in opts if "Chọn nhóm nhận" not in o]
            ok(real == ["Gia công"], "Y63 nhóm nhận chỉ Gia công (trả ngược về nơi phát hiện)", opts)
            if "Gia công" in real: await sel.select_option(label="Gia công"); await pg.wait_for_timeout(700)
            await qty(m, pg, 2)
            t = await m.inner_text()
            ok("tính là xong NC đó" in t, "Y64 chữ: Gia công nhận tính là xong NC của chiếc lỗi", t[-600:])
            done = await confirm(m, pg)
            b7 = get(B); nt = b7["txs"][-1]
            ok(done and nt["fromGroupCode"] == "HTSP" and nt["toGroupCode"] == "GC" and nt["fromCell"] == "XL" and nt["toCell"] == "XL" and nt["toNC"] is None,
               "Y65 DB: phiếu HTSP → GC ô Hàng lỗi đã xử lý, không NC", nt)
            rv = post("/harness/g16-recv?po=PO-G16B"); b8 = get(B)
            ok(rv["ok"] and b8["ncok"]["NC01"] == b0["ncok"]["NC01"] + 2 and b8["nc"]["NC02"] == b0["nc"]["NC02"] + 2 and b8["wts"] == b0["wts"],
               "Y66 GC nhận: OK NC01 +2 (= 10), chờ NC02 +2 (= 10), không thêm WTS", {"truoc": [b0["ncok"], b0["nc"]], "sau": [b8["ncok"], b8["nc"]]})
            sane(b8, "Y6"); await close_modal(pg)
        except Exception as e:
            ok(False, "Y6! lỗi bấm", e); await close_modal(pg)

        for (yy, po, nm) in (("7", "PO-G16C", "phiếu lập lúc OFF"), ("8", "PO-G16D", "phiếu 'Trả hàng đã sửa' cũ không ghi ô bên giao")):
            await recv_must_loi(pg, yy, po, nm)

        print("[Y9] [G16-NHAN-CHONSAN] PO-G16E: cùng phiếu nhưng Workflow OFF — giữ ô bên giao chỉ định (Hàng lỗi đã xử lý), không cảnh báo (N7)")
        try:
            post("/harness/g16c-seed?po=PO-G16E"); E = "/harness/g16?po=PO-G16E"; e0 = get(E)
            await pg.reload(); await pg.wait_for_timeout(4000)
            await row(pg, "PO-G16E").locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
            m = modal(pg)
            if await m.locator("select").count():
                await m.locator("select").first.select_option(label="HTSP"); await pg.wait_for_timeout(1500)
            hdr = m.locator("div[style*='cursor:pointer']").first
            if await hdr.count(): await hdr.click(); await pg.wait_for_timeout(900)
            on = [(await x.inner_text()).split("\n")[0].strip() for x in await m.locator("button.gn-cellbtn.on").all()]
            t = await m.inner_text()
            ok(on == ["Hàng lỗi đã xử lý"] and "đã chọn sẵn ô Hàng lỗi" not in t, "Y91 OFF: chọn sẵn ô bên giao chỉ định, không cảnh báo", on)
            await qty(m, pg, 2, 0)
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
            e1 = get(E)
            ok(e1["htsp"]["loiGiu"] == e0["htsp"]["loiGiu"], "Y92 OFF: nhận như cũ (không vào ô Hàng lỗi)", {"truoc": e0["htsp"], "sau": e1["htsp"]})
            await close_modal(pg)
        except Exception as e:
            ok(False, "Y9! lỗi bấm", e); await close_modal(pg)

        await br.close()
    print(f"\nKẾT QUẢ drive_g16 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
