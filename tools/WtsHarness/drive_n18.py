# [N18-SCEN 25/11/2026] Trang Giao nhận thật (harness EF giả) — luật N18 Cách Y (khách chốt mockup TH6 / TH7 23/11): chiếc "Sửa chưa đạt" ở ô Hàng sửa
#   phần mềm tự trả nơi làm lỗi / nơi gửi → bên nhận đếm ở cột riêng "Hàng sửa trả về chưa đạt", không vào Nhận · Hàng lỗi; bên sửa không hiện Nhận / Giao · Hàng lỗi.
#   Dữ liệu: POST /harness/sua-seed (PO-SUA: Kho báo 2 phôi lỗi → gửi Gia công sửa, Gia công giữ ô Hàng sửa 2).
#   STEP A bảng Giao nhận: mỗi nhóm có cột Nhận "Hàng sửa trả về chưa đạt" (5 nhóm), dải "Nhận" đủ 5 ô
#   STEP B admin · Gia công · Hàng lỗi · ô Hàng sửa · Sửa tại bộ phận mình: Sửa đạt 0 / Sửa chưa đạt 2 → "Sẽ ghi … đếm ở cột Hàng sửa trả về chưa đạt"
#       → DB: phiếu Gia công → Kho từ ô Hàng lỗi, ô chỉ định LOIV 2, cùng vụ; sổ: Gia công Nhận / Giao · Hàng lỗi 0
#   STEP C cửa sổ Nhận · Kho: thẻ phiếu có nhãn "Sửa chưa đạt trả về", dòng "không tính là lỗi mới", Workflow ON không có ô chọn "Nhận vào ô";
#       nhập 2 ở ô hàng lỗi → DB lần nhận ô LOI 2; sổ Kho: Nhận · Hàng lỗi không đổi, Hàng sửa trả về chưa đạt 2, Tồn hàng lỗi 2; sổ cân
#   STEP D Lịch sử phiếu: phiếu tự trả ghi "Giao · Hàng Sửa (sửa chưa đạt — phần mềm tự trả nơi làm lỗi)", nhận vào ô Hàng lỗi, không báo đổi ô (rà độc lập T1)
#   Bản cũ (97aa1bb, trước G-E) phải SAI từ STEP A (không có cột).
# Chạy trên harness VỪA khởi động: python3 drive_n18.py <port> <old|new>
import sys, asyncio, json, re, urllib.request
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
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[STEP A] bảng Giao nhận: cột Nhận Hàng sửa trả về chưa đạt")
        hdr = [(await x.inner_text()).strip() for x in await pg.locator("table.gn-table thead tr").nth(2).locator("th").all()]
        band = [((await x.inner_text()).strip(), await x.get_attribute("colspan")) for x in await pg.locator("table.gn-table thead tr").nth(1).locator("th").all()]
        ok(hdr.count("Hàng sửa trả về chưa đạt") == 5 and sum(1 for x in band if x[0] == "Nhận" and x[1] == "5") == 5,
           "A1 5 nhóm đều có cột Nhận 'Hàng sửa trả về chưa đạt', dải Nhận 5 ô", {"hdr": hdr[:12], "band": band[:6]})
        if fails:
            await b.close(); print(f"KẾT QUẢ drive_n18 ({OUT}): {fails} SAI"); return

        print("[STEP B] Gia công · ô Hàng sửa · Sửa chưa đạt 2 → phiếu tự trả ô LOIV")
        m = await open_form(pg, "Gia công")
        await click(m, pg, "Hàng lỗi")
        await click(m, pg, "Đang giữ ở ô Hàng sửa", 900)
        await click(m, pg, SIX[0])
        await num(m, pg, 0, 0); await num(m, pg, 1, 2)
        t = await m.inner_text()
        ok("chưa đạt 2: phiếu Gia công → Kho ô Hàng lỗi" in t and "đếm ở cột Hàng sửa trả về chưa đạt" in t, "B1 Sẽ ghi: phiếu Gia công → Kho ô Hàng lỗi, Kho bấm Nhận, đếm ở cột Hàng sửa trả về chưa đạt", t[-600:])
        sent = await confirm(m, pg)
        d1 = get("/harness/form")
        nt = [x for x in d1["txs"] if x not in d0["txs"]]
        ok(sent and len(nt) == 1 and nt[0]["fromGroupCode"] == "GC" and nt[0]["toGroupCode"] == "KHO" and nt[0]["fromCell"] == "LOI" and nt[0]["toCell"] == "LOIV" and nt[0]["qtyIssued"] == 2,
           "B2 DB: phiếu Gia công → Kho, từ ô Hàng lỗi, ô chỉ định LOIV 2, cùng vụ", nt)
        ok(d1["cells"]["GC"]["nl"] == 0 and d1["cells"]["GC"]["gl"] == 0 and d1["cells"]["GC"]["loi"] == 0 and d1["groups"]["KHO"]["choNhan"] == 2,
           "B3 Gia công Nhận / Giao · Hàng lỗi 0 (chiếc không nằm lại), Kho chờ nhận 2", {"GC": d1["cells"]["GC"], "KHO": d1["groups"]["KHO"]})
        sane(d1, "B4")

        print("[STEP C] cửa sổ Nhận · Kho: phiếu 'Sửa chưa đạt trả về', không tính lỗi mới, ON không đổi ô")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        await row(pg).locator("button:has-text('Nhận')").first.click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Kho"); await pg.wait_for_timeout(1500)
        m = modal(pg); t = await m.inner_text()
        ok("Sửa chưa đạt trả về" in t, "C1 thẻ phiếu có nhãn 'Sửa chưa đạt trả về'", t[:500])
        await m.locator("text=Từ Gia công").first.click(); await pg.wait_for_timeout(1000)
        t = await m.inner_text()
        pick = await m.locator("label", has_text="Nhận vào ô").count()
        ok("không tính là lỗi mới" in t and "Hàng sửa trả về chưa đạt" in t and pick == 0 and "Giao · Hàng Sửa (sửa chưa đạt — phần mềm tự trả nơi làm lỗi)" in t,
           "C2 dòng 'chiếc lỗi cũ quay về — không tính là lỗi mới'; nhãn phiếu 'Giao · Hàng Sửa (sửa chưa đạt …)'; Workflow ON không có ô chọn 'Nhận vào ô'", {"pick": pick, "t": t[:900]})
        ng = m.locator("input.border-danger").first
        await ng.fill("2"); await ng.dispatch_event("change"); await pg.wait_for_timeout(400)
        await m.locator("button.btn-success:has-text('Xác nhận')").first.click(); await pg.wait_for_timeout(2500)
        d2 = get("/harness/form")
        nr = [x for x in d2["recvs"] if x not in d1["recvs"]]
        ok(len(nr) == 1 and nr[0]["toCell"] == "LOI" and nr[0]["qtyNg"] == 2, "C3 DB: lần nhận ô Hàng lỗi 2", nr)
        k1, k2 = d1["cells"]["KHO"], d2["cells"]["KHO"]
        ok(k2["nl"] == k1["nl"] and k2["nlv"] == 2 and k2["loi"] == k1["loi"] + 2, "C4 sổ Kho: Nhận · Hàng lỗi không đổi, Hàng sửa trả về chưa đạt 2, Tồn hàng lỗi +2", {"truoc": k1, "sau": k2})
        sane(d2, "C5")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        cells = [(await x.inner_text()).strip() for x in await row(pg).locator("td.gn-num").all()]
        ok(cells[4] == "2", "C6 bảng: ô Kho · Nhận · Hàng sửa trả về chưa đạt hiện 2 (cột thứ 5 của Kho)", cells[:12])

        print("[STEP D] Lịch sử phiếu: phiếu tự trả ghi 'Giao · Hàng Sửa (sửa chưa đạt …)' → nhận vào ô Hàng lỗi, không báo đổi ô (rà độc lập T1)")
        await row(pg).locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
        t = await modal(pg).inner_text()
        # chỉ xét khối của phiếu tự trả (từ dòng '#NN' trước nhãn tới dòng '#NN' kế) — phiếu Kho gửi sửa cũng nằm trong modal, nhận vào ô Hàng sửa là đúng
        lines = t.split("\n"); lab = "Giao · Hàng Sửa (sửa chưa đạt — phần mềm tự trả nơi làm lỗi)"
        idx = next((i for i, x in enumerate(lines) if lab in x), -1)
        st = max((i for i in range(0, idx + 1) if re.match(r"^#\d+$", lines[i].strip())), default=idx) if idx >= 0 else 0
        en = next((i for i in range(idx + 1, len(lines)) if re.match(r"^#\d+$", lines[i].strip())), len(lines)) if idx >= 0 else 0
        blk = "\n".join(lines[st:en])
        ok(idx >= 0 and "nhận vào ô Hàng lỗi" in blk and "nhận thật" not in blk and "nhận vào ô Hàng sửa" not in blk,
           "D1 Lịch sử phiếu: nhãn 'Giao · Hàng Sửa (sửa chưa đạt — phần mềm tự trả nơi làm lỗi)', ô nhận Hàng lỗi, không có '(nhận thật: …)' / 'nhận vào ô Hàng sửa' (khối phiếu tự trả)", blk[:800] or t[:800])
        ok("nhận thật" not in t, "D2 cả modal không có '(nhận thật: …)' (không báo nhầm đổi ô)", t[:800])
        await b.close()
    print(f"KẾT QUẢ drive_n18 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
