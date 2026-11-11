# [GÓI 15 10/11/2026][SUA-XL-GIU][SUA-KHONG-LUI][XL-VUOT-CHAN][XL-TRA-LAI] Khách 10/11 vòng 1 câu 2.1 A + 2.2 A — bấm trang thật (harness EF giả).
#   Nguồn: KHÁCH 10/11 (ví dụ trong doc: sửa xong không quay lại NC; nhóm đã làm xong chiếc đó nhận vào ô Hàng lỗi đã xử lý = đã làm); HÙNG 10/11 19:22.
#   Dữ liệu: POST /harness/g15-seed (HarnessG15.SeedAsync — PO-G15 10: GC NC01 → NC02, HTSP Taro T15, KCS K1; KCS phát hiện 2 chiếc lỗi do Gia công,
#     GC đã nhận ô Hàng lỗi 2, chưa sửa).
#   X1 [SUA-KHONG-LUI] form Giao, Gia công, Hàng lỗi, Tồn hàng lỗi, vụ KCS phát hiện, Sửa tại nhóm: KHÔNG có ô chọn NC; chữ "không làm lại công đoạn …
#      Tồn hàng lỗi đã xử lý … nơi phát hiện (KCS)"; Xác nhận 2 → DB Kết quả sửa ToNC "@XONG"; sổ GC chờ NC không đổi.
#   X2 KCS xác nhận (POST /harness/g15-xn) → GC Tồn hàng lỗi đã xử lý 2 (Đã làm chưa giao không đổi); bảng chi tiết hiện cột "Đã sửa chờ trả".
#   X3 [XL-VUOT-CHAN] form Gia công, Hàng tốt, "Hàng đã sửa — chờ trả nơi phát hiện lỗi" có 2: nhóm nhận chỉ HTSP / KCS (không Kho / Đóng gói);
#      chọn HTSP → chữ "HTSP nhận vào Tồn hàng lỗi đã xử lý"; Xác nhận 2 → phiếu GC → HTSP ô XL → XL cùng vụ.
#   X4 HTSP nhận (POST /harness/g15-recv) → HTSP Tồn hàng lỗi đã xử lý 2, Chưa làm không đổi.
#   X5 [XL-TRA-LAI] form HTSP, Hàng lỗi, "Hàng đã sửa bị hỏng lại": "Trả lại vào ô Hàng lỗi", nhóm nhận mặc định Gia công; 1 → phiếu HTSP → GC ô XL → Hàng lỗi cùng vụ.
#   X6 form HTSP, Hàng tốt, hàng đã sửa: nhóm nhận chỉ KCS; chữ "KCS là nơi phát hiện … đã kiểm xong"; 1 → KCS nhận → KCS Đã làm chưa giao +1, HTSP hết hàng đã sửa.
#   X7 [SUA-OFF-LUI] tắt Workflow (POST /harness/g15-off): Gia công sửa vụ còn lại → ô chọn 'Không làm lại NC (mặc định)' + 'Làm lại NC02'; chọn làm lại → ToNC NC02.
#   Mỗi bước: sổ cân, 0 ô âm. Bản 86dc606 (trước Gói 15): X1 có ô chọn NC / ToNC NC02 → SAI là đúng.
# Chạy trên harness VỪA khởi động: python3 drive_g15.py <port> <nhãn>
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
def row(pg): return pg.locator("tr", has_text="PO-G15").first
async def open_form(pg, grp):
    await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
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

async def main():
    seed = post("/harness/g15-seed"); print("seed", seed)
    d0 = get("/harness/g15"); sane(d0, "seed")
    vu = d0["txs"][-1]["ngCaseId"]
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[X1] [SUA-KHONG-LUI] Gia công sửa vụ KCS phát hiện: không chọn NC, không làm lại công đoạn")
        try:
            m = await open_form(pg, "Gia công")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi")
            cbs = m.locator("button.gn-cellbtn", has_text="KCS")
            if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
            await click(m, pg, "Sửa tại nhóm")
            await qty(m, pg, 2)
            t = await m.inner_text()
            nsel = m.locator("select", has=pg.locator("option", has_text="mặc định"))
            ok(await nsel.count() == 0, "X11 không có ô chọn NC làm tiếp", t[-600:])
            ok("không làm lại công đoạn" in t and "Tồn hàng lỗi đã xử lý" in t and "nơi phát hiện (KCS)" in t,
               "X12 chữ: sửa đạt vào Tồn hàng lỗi đã xử lý của Gia công, không làm lại công đoạn, giao về nơi phát hiện (KCS)", t[-600:])
            done = await confirm(m, pg)
            d1 = get("/harness/g15")
            ok(done and len(d1["reps"]) == 1 and d1["reps"][0]["toNC"] == "@XONG" and d1["reps"][0]["qtyPass"] == 2 and d1["reps"][0]["ngCaseId"] == vu,
               "X13 DB: Kết quả sửa đạt 2, ToNC '@XONG' (không làm lại NC), cùng vụ", d1["reps"])
            ok(d1["nc"] == d0["nc"] and d1["gc"]["conGiao"] == d0["gc"]["conGiao"], "X14 sổ GC: chờ NC / Đã làm chưa giao không đổi (chờ KCS xác nhận)", {"truoc": d0["gc"], "sau": d1["gc"], "nc": d1["nc"]})
            sane(d1, "X1")
            await close_modal(pg)
        except Exception as e:
            ok(False, "X1! lỗi bấm", e); await close_modal(pg)

        print("[X2] KCS xác nhận → Gia công Tồn hàng lỗi đã xử lý 2")
        try:
            xn = post("/harness/g15-xn")
            d2 = get("/harness/g15")
            ok(xn["ok"] and d2["gc"]["xlGiu"] == 2 and d2["gc"]["conGiao"] == d0["gc"]["conGiao"] and d2["gc"]["choKcs"] == 0,
               "X21 GC: Tồn hàng lỗi đã xử lý 2, Đã làm chưa giao không đổi, Chờ KCS 0", {"xn": xn, "gc": d2["gc"]})
            sane(d2, "X2")
            await pg.reload(); await pg.wait_for_timeout(4000)
            if await pg.locator("button:has-text('Hiện công thức')").count():   # bảng Tồn theo loại hàng chỉ hiện khi bật Hiện công thức
                await pg.locator("button:has-text('Hiện công thức')").first.click(); await pg.wait_for_timeout(1200)
            await row(pg).locator("button[title^='Xem từng bước']").click(); await pg.wait_for_timeout(2000)
            body = await pg.locator("body").inner_text()
            ok("Đã sửa chờ trả" in body, "X22 bảng chi tiết PO có cột 'Đã sửa chờ trả'", body[-1500:])
        except Exception as e:
            ok(False, "X2! lỗi", e); await close_modal(pg)

        print("[X3] [XL-VUOT-CHAN] Gia công giao hàng đã sửa: chỉ xuôi về nơi phát hiện")
        try:
            await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "Gia công")
            await click(m, pg, "Hàng tốt")
            bt = await btn_texts(m)
            ok(any(x.startswith("Hàng đã sửa — chờ trả nơi phát hiện lỗi") and x.endswith("có 2") for x in bt), "X31 Hàng tốt: nút 'Hàng đã sửa — chờ trả nơi phát hiện lỗi' có 2", bt)
            await click(m, pg, "Hàng đã sửa — chờ trả nơi phát hiện lỗi")
            cbs = m.locator("button.gn-cellbtn", has_text="KCS")
            if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
            sel, opts = await dest_opts(m)
            real = [o for o in opts if "Chọn nhóm nhận" not in o]
            ok(sorted(real) == ["HTSP", "KCS"], "X32 nhóm nhận chỉ HTSP / KCS (không Kho, không Đóng gói)", opts)
            if "HTSP" in real:
                await sel.select_option(label="HTSP"); await pg.wait_for_timeout(700)
            await qty(m, pg, 2)
            t = await m.inner_text()
            ok("HTSP nhận vào Tồn hàng lỗi đã xử lý" in t and "không ghi WTS số lượng" in t, "X33 chữ: HTSP nhận vào Tồn hàng lỗi đã xử lý, không ghi WTS số lượng", t[-600:])
            done = await confirm(m, pg)
            d3 = get("/harness/g15"); nt = d3["txs"][-1]
            ok(done and nt["fromGroupCode"] == "GC" and nt["toGroupCode"] == "HTSP" and nt["fromCell"] == "XL" and nt["toCell"] == "XL" and nt["qtyIssued"] == 2 and nt["ngCaseId"] == vu,
               "X34 DB: phiếu GC → HTSP 2, ô Hàng lỗi đã xử lý → ô Hàng lỗi đã xử lý, cùng vụ", nt)
            ok(d3["gc"]["xlGiu"] == 0, "X35 GC hết hàng đã sửa chờ trả", d3["gc"])
            sane(d3, "X3")
            await close_modal(pg)
        except Exception as e:
            ok(False, "X3! lỗi bấm", e); await close_modal(pg)

        print("[X4] HTSP nhận hàng đã sửa: Tồn hàng lỗi đã xử lý, không vào Chưa làm")
        try:
            rv = post("/harness/g15-recv")
            d4 = get("/harness/g15")
            ok(rv["ok"] and d4["htsp"]["xlGiu"] == 2 and d4["htsp"]["chuaLam"] == d0["htsp"]["chuaLam"] and d4["htsp"]["conGiao"] == d0["htsp"]["conGiao"],
               "X41 HTSP: Tồn hàng lỗi đã xử lý 2, Chưa làm / Đã làm chưa giao không đổi", {"rv": rv, "truoc": d0["htsp"], "sau": d4["htsp"]})
            sane(d4, "X4")
        except Exception as e:
            ok(False, "X4! lỗi", e)

        print("[X5] [XL-TRA-LAI] HTSP làm lại hỏng 1 → trả lại Gia công ô Hàng lỗi cùng vụ")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "HTSP")
            await click(m, pg, "Hàng lỗi")
            await click(m, pg, "Hàng đã sửa bị hỏng lại")
            cbs = m.locator("button.gn-cellbtn", has_text="KCS")
            if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
            bt = await btn_texts(m)
            ok(any(x.startswith("Trả lại vào ô Hàng lỗi") for x in bt), "X51 có việc 'Trả lại vào ô Hàng lỗi (cùng vụ, để sửa lại)'", bt)
            await click(m, pg, "cùng vụ, để sửa lại")   # chữ riêng của nút việc (nút vị trí cũng có "trả lại vào ô Hàng lỗi")
            sel, opts = await dest_opts(m)
            cur = await sel.evaluate("s => s.options[s.selectedIndex].text") if await sel.count() else ""
            ok(cur == "Gia công" and [o for o in opts if "Chọn nhóm nhận" not in o] == ["Gia công"], "X52 [XL-TRA-NHOMSUA] nhóm nhận chỉ Gia công (nhóm đã sửa), mặc định Gia công", {"cur": cur, "opts": opts})
            await qty(m, pg, 1)
            done = await confirm(m, pg)
            d5 = get("/harness/g15"); nt = d5["txs"][-1]
            ok(done and nt["fromGroupCode"] == "HTSP" and nt["toGroupCode"] == "GC" and nt["fromCell"] == "XL" and nt["toCell"] == "LOI" and nt["qtyIssued"] == 1 and nt["ngCaseId"] == vu,
               "X53 DB: phiếu HTSP → GC 1, ô Hàng lỗi đã xử lý → ô Hàng lỗi, cùng vụ", nt)
            sane(d5, "X5")
            rv = post("/harness/g15-recv")   # GC nhận lại (ô Hàng lỗi)
            d5b = get("/harness/g15")
            ok(rv["ok"] and d5b["gc"]["loiGiu"] == 1 and d5b["htsp"]["xlGiu"] == 1, "X54 GC nhận lại: Tồn hàng lỗi 1; HTSP còn 1 hàng đã sửa", {"gc": d5b["gc"], "htsp": d5b["htsp"]})
            sane(d5b, "X5b")
            await close_modal(pg)
        except Exception as e:
            ok(False, "X5! lỗi bấm", e); await close_modal(pg)

        print("[X6] HTSP giao hàng đã sửa về KCS (nơi phát hiện) → KCS tính đã kiểm")
        try:
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "HTSP")
            await click(m, pg, "Hàng tốt")
            await click(m, pg, "Hàng đã sửa — chờ trả nơi phát hiện lỗi")
            cbs = m.locator("button.gn-cellbtn", has_text="KCS")
            if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
            sel, opts = await dest_opts(m)
            real = [o for o in opts if "Chọn nhóm nhận" not in o]
            ok(real == ["KCS"], "X61 nhóm nhận chỉ KCS (không vượt sang Đóng gói)", opts)
            if "KCS" in real:
                await sel.select_option(label="KCS"); await pg.wait_for_timeout(700)
            await qty(m, pg, 1)
            t = await m.inner_text()
            ok("KCS là nơi phát hiện" in t and "đã kiểm xong" in t, "X62 chữ: KCS là nơi phát hiện, nhận vào tính là đã kiểm xong", t[-600:])
            done = await confirm(m, pg)
            rv = post("/harness/g15-recv")
            d6 = get("/harness/g15")
            ok(done and rv["ok"] and d6["kcs"]["conGiao"] == d0["kcs"]["conGiao"] + 1 and d6["kcs"]["chuaLam"] == d0["kcs"]["chuaLam"] and d6["htsp"]["xlGiu"] == 0,
               "X63 KCS nhận: Đã làm chưa giao +1, Chưa làm không đổi; HTSP hết hàng đã sửa", {"truoc": d0["kcs"], "sau": d6["kcs"], "htsp": d6["htsp"]})
            sane(d6, "X6")
            await close_modal(pg)
        except Exception as e:
            ok(False, "X6! lỗi bấm", e); await close_modal(pg)
        print("[X7] [SUA-OFF-LUI] Workflow OFF: Gia công sửa vụ KCS phát hiện — vẫn chọn được làm lại NC (mặc định không làm lại)")
        try:
            post("/harness/g15-off")
            await pg.reload(); await pg.wait_for_timeout(4000)
            m = await open_form(pg, "Gia công")
            await click(m, pg, "Hàng lỗi"); await click(m, pg, "Tồn hàng lỗi")
            cbs = m.locator("button.gn-cellbtn", has_text="KCS")
            if await cbs.count(): await cbs.first.click(); await pg.wait_for_timeout(700)
            await click(m, pg, "Sửa tại nhóm")
            await qty(m, pg, 1)
            sel = m.locator("select", has=pg.locator("option", has_text="Không làm lại NC")).first
            opts = await sel.evaluate("s => Array.from(s.options).map(o => o.text)") if await sel.count() else []
            ok(len(opts) >= 2 and opts[0].startswith("Không làm lại NC") and any(o.startswith("Làm lại NC02") for o in opts),
               "X71 ô chọn: 'Không làm lại NC (mặc định)' + 'Làm lại NC02 … (như trước, Workflow OFF)'", opts)
            lam = [i for i, o in enumerate(opts) if o.startswith("Làm lại NC02")]
            if lam:
                await sel.select_option(index=lam[0]); await pg.wait_for_timeout(600)
            done = await confirm(m, pg)
            d7 = get("/harness/g15")
            ok(done and d7["reps"][-1]["toNC"] == "NC02", "X72 DB: Kết quả sửa ToNC NC02 (chứng từ kiểu cũ, Workflow OFF)", d7["reps"][-1:])
            sane(d7, "X7")
            await close_modal(pg)
        except Exception as e:
            ok(False, "X7! lỗi bấm", e); await close_modal(pg)
        await b.close()
    print(f"\nKẾT QUẢ drive_g15 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
