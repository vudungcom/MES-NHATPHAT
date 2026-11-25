# [N23-SCEN 25/11/2026] Trang Giao nhận thật (harness EF giả) — luật N23: form "Giao / xử lý hàng" 6 nút (khách chốt mockup 23/11: Form chung, TH4, TH10).
#   Dữ liệu: POST /harness/sua-seed (PO-SUA: Gia công NC01 → NC02, đã nhận 8; Kho báo 2 phôi lỗi → gửi Gia công sửa, Gia công giữ ô Hàng sửa 2).
#   STEP A admin · Gia công · Hàng lỗi: bước 2 "Hàng đang ở đâu" — dòng ô Hàng sửa theo mã vụ ("— VL… · Kho gửi sửa"); bước 3 đủ 6 nút đúng chữ, Trả / Huỷ / Trả khách xám
#       + dòng lý do; Sửa: "Sửa đạt" 1 + "Sửa chưa đạt" 1 → chữ "Chưa đạt → phần mềm tự trả Kho (nơi gửi)", "Sẽ ghi … chưa đạt 1: phiếu Gia công → Kho ô Hàng lỗi"
#       → DB: Kết quả sửa đạt 1 / chưa đạt 1 + phiếu Gia công → Kho từ ô Hàng lỗi vào ô Hàng lỗi 1, cùng vụ; Gia công không còn giữ gì
#   STEP B lỗi mới "Vừa nhận, chưa làm NC01": không tích → "Trả nơi làm lỗi" xám (nơi làm lỗi là chính Gia công); tích + Kho → bấm được, "Trả đến" tự điền Kho,
#       "Sẽ ghi: phiếu Gia công → Kho, ô Hàng lỗi 2, mã vụ mới" → DB 1 phiếu Hàng lỗi công đoạn trước (NC01)
#   STEP C (Kho nhận 2 phiếu) admin · Kho · Hàng lỗi: vụ TH4 "— VL… · Gia công trả" → "Trả nơi làm lỗi" xám (nơi làm lỗi là chính Kho); Sửa / xác nhận đạt 2
#       → ô "Đạt → giao đến" mặc định Gia công (nơi báo lỗi) + "Chưa giao", vào ô Hàng lỗi đã xử lý → DB: Kết quả sửa Kho dấu @XONG + phiếu Kho → Gia công
#       ô Hàng lỗi đã xử lý → Hàng lỗi đã xử lý 2, cùng vụ, NC01 (máy chủ tự điền); phôi còn Kho không đổi
#   Bản cũ (trước G-A2) phải SAI từ STEP A (không có 6 nút / dòng theo vụ).
# Chạy trên harness VỪA khởi động: python3 drive_n23.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
SIX = ["Sửa tại bộ phận mình / XXN KH", "Gửi bộ phận khác sửa", "Trả nơi làm lỗi", "Giữ lại, xử lý sau", "Huỷ", "Trả khách"]
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg): return pg.locator("tr", has_text="PO-SUA").first
async def open_form(pg, grp):
    await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    await modal(pg).locator("select").first.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def six(m):
    out = []
    for x in await m.locator("div.row.g-1 button.gn-cellbtn").all():
        out.append(((await x.inner_text()).strip(), await x.is_disabled()))
    return out
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(600)
async def pick_cat(m, pg, code):
    inp = m.locator("input[list^='ngcat_']").first
    await inp.fill(code); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
async def num(m, pg, i, v):
    x = m.locator("input[type=number]").nth(i); await x.fill(str(v)); await x.dispatch_event("change"); await pg.wait_for_timeout(500)
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
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[STEP A] Gia công · ô Hàng sửa: 6 nút + Sửa chưa đạt tự trả nơi làm lỗi")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi")
        t = await m.inner_text()
        rows = [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
        ok("Hàng đang ở đâu" in t and any(x.startswith("Đang giữ ở ô Hàng sửa (bộ phận khác gửi sửa) — VL") and "Kho gửi sửa" in x and x.endswith("có 2") for x in rows)
           and any(x.startswith("Đang giữ ở ô Hàng lỗi") and x.endswith("có 0") for x in rows),
           "A1 bước 2 'Hàng đang ở đâu': dòng ô Hàng sửa theo mã vụ '— VL… · Kho gửi sửa' có 2; dòng ô Hàng lỗi có 0", rows)
        sua_row = m.locator("button.gn-cellbtn", has_text="Đang giữ ở ô Hàng sửa").first
        if await sua_row.count() and not await sua_row.is_disabled():
            await sua_row.click(); await pg.wait_for_timeout(900)
            s6 = await six(m); t = await m.inner_text()
            ok([x[0] for x in s6] == SIX and [x[1] for x in s6] == [False, False, True, False, True, True],
               "A2 bước 3 đủ 6 nút đúng chữ mockup; ô Hàng sửa (mockup TH3 bước 5): Trả nơi làm lỗi / Huỷ / Trả khách xám", s6)
            ok("Trả nơi làm lỗi xám:" in t and "Sửa chưa đạt" in t and "Huỷ xám:" in t, "A3 nút xám ghi lý do ngay dưới (Trả nơi làm lỗi, Huỷ …)", t[-900:])
            await click(m, pg, SIX[0])
            await num(m, pg, 0, 1); await num(m, pg, 1, 1)
            t = await m.inner_text()
            ok("Sửa đạt" in t and "Sửa chưa đạt" in t and "Chưa đạt → phần mềm tự trả Kho (nơi gửi)" in t and "chưa đạt 1: phiếu Gia công → Kho ô Hàng lỗi (nơi gửi)" in t
               and "giao đến" not in t, "A4 Chi tiết: Sửa đạt / Sửa chưa đạt, chữ tự trả Kho (nơi gửi — vụ phôi khách), Sẽ ghi có phiếu trả; không có ô giao đến (vụ do Kho phát hiện)", t[-700:])
            sent = await confirm(m, pg)
            d1 = get("/harness/form")
            nk = [x for x in d1["reps"] if x not in d0["reps"]]; nt = [x for x in d1["txs"] if x not in d0["txs"]]
            ok(sent and len(nk) == 1 and nk[0]["qtyPass"] == 1 and nk[0]["qtyFail"] == 1 and len(nt) == 1 and nt[0]["fromGroupCode"] == "GC" and nt[0]["toGroupCode"] == "KHO"
               and nt[0]["fromCell"] == "LOI" and nt[0]["toCell"] == "LOIV" and nt[0]["qtyIssued"] == 1 and nt[0]["ngCaseId"] == nk[0]["ngCaseId"],
               "A5 DB: Kết quả sửa đạt 1 / chưa đạt 1 + phiếu Gia công → Kho ô Hàng lỗi 1 (từ G-E [N18-TRAVE] ô chỉ định LOIV — đáp án đổi), cùng vụ — 1 lần lưu", {"kq": nk, "tx": nt})
            ok(d1["groups"]["GC"]["suaGiu"] == 0 and d1["groups"]["GC"]["loiGiu"] == 0 and d1["groups"]["KHO"]["choNhan"] == 1, "A6 Gia công không còn giữ gì; Kho chờ nhận 1", d1["groups"])
            sane(d1, "A7")
        else:
            ok(False, "A2 không có dòng 'Đang giữ ở ô Hàng sửa' bấm được", rows)
            await close_modal(pg); d1 = d0

        print("[STEP B] lỗi mới NC01: Trả nơi làm lỗi xám khi không tích; tích Kho → Trả đến tự điền Kho")
        await close_modal(pg)
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi"); await click(m, pg, "Vừa nhận, chưa làm NC01")
        s6 = await six(m); t = await m.inner_text()
        ok([x[0] for x in s6] == SIX and [x[1] for x in s6] == [False, False, True, False, False, False] and "Nơi làm lỗi là chính Gia công" in t,
           "B1 không tích: Trả nơi làm lỗi xám — nơi làm lỗi là chính Gia công", s6)
        await m.locator("input#gxlPrev").check(); await pg.wait_for_timeout(600)
        await m.locator("select:has(option:text-is('-- Chọn nơi gây lỗi --'))").select_option("KHO"); await pg.wait_for_timeout(700)
        s6 = await six(m)
        ok([x[1] for x in s6] == [False] * 6, "B2 tích + nơi gây lỗi Kho: đủ 6 nút bấm được", s6)
        await click(m, pg, SIX[2])
        await num(m, pg, 0, 2); await pick_cat(m, pg, cat)
        t = await m.inner_text()
        ok("Trả đến" in t and "Kho (nơi làm lỗi) — ô Hàng lỗi" in t and "Sẽ ghi: phiếu Gia công → Kho, ô Hàng lỗi 2, mã vụ mới" in t, "B3 'Trả đến: Kho (nơi làm lỗi) — ô Hàng lỗi', Sẽ ghi theo mockup TH4", t[-600:])
        sent = await confirm(m, pg)
        d2 = get("/harness/form")
        nt = [x for x in d2["txs"] if x not in d1["txs"]]
        ok(sent and len(nt) == 1 and nt[0]["purpose"] == "NG_RETURN" and nt[0]["fromNC"] == "NC01" and nt[0]["qtyIssued"] == 2, "B4 DB: 1 phiếu Hàng lỗi công đoạn trước Gia công → Kho 2 (NC01)", nt)
        sane(d2, "B5")

        print("[STEP C] Kho nhận → Kho Sửa / xác nhận đạt 2 → giao đến Gia công ô Hàng lỗi đã xử lý (TH4)")
        post("/harness/form-recv")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        d3 = get("/harness/form")
        vu = nt[0]["ngCaseId"] if nt else None
        m = await open_form(pg, "Kho")
        await click(m, pg, "Hàng lỗi")
        rows = [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
        ok(sum(1 for x in rows if x.startswith("Đang giữ ở ô Hàng lỗi — VL") and "Gia công trả" in x) == 1 and sum(1 for x in rows if x.startswith("Đang giữ ở ô Hàng lỗi — VL") and "Gia công sửa chưa đạt trả về" in x) == 1,
           "C1 Kho: 2 dòng ô Hàng lỗi theo mã vụ — '· Gia công trả' (TH4) + '· Gia công sửa chưa đạt trả về' (STEP A — [N18-TRAVE] đáp án đổi G-E)", rows)
        no = next((c["ngCaseId"] for c in d3["cases"] if c["ngCaseId"] == vu), None)
        r4 = m.locator("button.gn-cellbtn", has_text="Đang giữ ở ô Hàng lỗi — VL").filter(has_text="có 2").first
        if await r4.count():
            await r4.click(); await pg.wait_for_timeout(900)
            s6 = await six(m); t = await m.inner_text()
            ok([x[1] for x in s6] == [False, False, True, False, False, False] and "Nơi làm lỗi là chính Kho" in t,
               "C2 như mockup TH4 bước 3: chỉ Trả nơi làm lỗi xám (nơi làm lỗi là chính Kho)", s6)
            await click(m, pg, SIX[0]); await num(m, pg, 0, 2)
            t = await m.inner_text()
            sel = m.locator("select:has(option:text-is('Chưa giao — giữ ở Tồn hàng lỗi đã xử lý'))").first
            opts = [(await o.inner_text()).strip() for o in await sel.locator("option").all()] if await sel.count() else []
            ok("Sửa / xác nhận đạt" in t and "Đạt → giao đến" in t and "Gia công (nơi báo lỗi)" in opts and "Hàng lỗi đã xử lý" in t
               and "Sẽ ghi: sửa đạt 2 + phiếu Kho → Gia công ô Hàng lỗi đã xử lý 2" in t, "C3 ô 'Đạt → giao đến' mặc định Gia công (nơi báo lỗi), có 'Chưa giao', vào ô Hàng lỗi đã xử lý", {"opts": opts, "t": t[-600:]})
            sent = await confirm(m, pg)
            d4 = get("/harness/form")
            nk = [x for x in d4["reps"] if x not in d3["reps"]]; ntx = [x for x in d4["txs"] if x not in d3["txs"]]
            ok(sent and len(nk) == 1 and nk[0]["groupCode"] == "KHO" and nk[0]["toNC"] == "@XONG" and len(ntx) == 1 and ntx[0]["fromCell"] == "XL" and ntx[0]["toCell"] == "XL"
               and ntx[0]["toGroupCode"] == "GC" and ntx[0]["qtyIssued"] == 2 and ntx[0]["ngCaseId"] == vu and ntx[0]["toNC"] == "NC01",
               "C4 DB: Kết quả sửa Kho (@XONG) + phiếu Kho → Gia công ô Hàng lỗi đã xử lý 2, cùng vụ, NC01", {"kq": nk, "tx": ntx})
            ok(d4["groups"]["KHO"]["conGiao"] == d3["groups"]["KHO"]["conGiao"] and d4["groups"]["KHO"]["loiGiu"] == d3["groups"]["KHO"]["loiGiu"] - 2,
               "C5 Kho: phôi còn không đổi (không đi Hàng mới), Tồn hàng lỗi −2", {"truoc": d3["groups"]["KHO"], "sau": d4["groups"]["KHO"]})
            sane(d4, "C6")
        else:
            ok(False, "C2 không thấy dòng vụ TH4 (có 2) ở Kho", rows)
        await b.close()
    print(f"KẾT QUẢ drive_n23 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
