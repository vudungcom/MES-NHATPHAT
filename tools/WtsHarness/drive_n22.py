# [N22-SCEN 25/11/2026] Trang Giao nhận thật (harness EF giả) — luật N22 (mockup D7, khách chốt 23/11): Sửa lần nhận / Sửa phiếu / Đổi loại lỗi ở Lịch sử phiếu.
#   Dữ liệu: POST /harness/n22-seed (Stubs/HarnessN22.cs). Đọc DB: GET /harness/n22?po=…
#   STEP A PO-N22A (nhận 15 OK + 5 NG): Lịch sử → nút Sửa lần nhận → 14 OK + 6 NG, "Sẽ ghi … lập thêm phiếu trả Kho 1 chiếc cùng vụ" → DB: lần nhận cũ huỷ,
#          2 dòng cùng dấu [N22#gốc] tổng 14 + 6, phiếu trả cũ 5 giữ + phiếu trả mới 1 cùng vụ; Lịch sử hiện 1 dòng 14 / 6 "Đã sửa lần nhận · 2 phiếu trả"
#   STEP B PO-N22B (phiếu Hàng lỗi công đoạn trước Gia công → Kho 5 chờ nhận): nút Sửa phiếu → số 4, lý do → DB: phiếu cũ huỷ "Sửa phiếu → #…", phiếu mới 4 cùng vụ
#   STEP C PO-N22B lọc mã vụ → dải "Loại lỗi đang ghi" + nút Đổi loại lỗi → loại khác + lý do → DB: vụ đổi loại lỗi, lịch sử PO 1 dòng; dải hiện dòng "Đổi loại lỗi … → …"
#   STEP D PO-N22C (Gia công đã nhập WTS sau lần nhận): nút Sửa lần nhận XÁM + dòng "Sửa lần nhận xám: … nhập WTS …" (D7 bước 2b)
#   Bản cũ (a29ba8b, trước G-B) phải SAI từ STEP A (không có nút Sửa lần nhận).
# Chạy trên harness VỪA khởi động: python3 drive_n22.py <port> <old|new>
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def st(po): return get("/harness/n22?po=" + po)
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
def porow(pg, po): return pg.locator("tr", has=pg.locator("td", has_text=re.compile(r"^\s*" + po + r"\s*$"))).first
def sane(d, name): ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", d)
async def open_log(pg, po):
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4500)
    await porow(pg, po).locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
    return modal(pg)
async def fill(loc, pg, v):
    await loc.fill(str(v)); await loc.dispatch_event("change"); await pg.wait_for_timeout(400)

async def main():
    post("/harness/n22-seed")
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")

        print("[STEP A] PO-N22A Sửa lần nhận 15 + 5 → 14 + 6 (D7 bước 2a)")
        a0 = st("PO-N22A")
        m = await open_log(pg, "PO-N22A")
        btn = m.locator("button[title='Sửa lần nhận']")
        ok(await btn.count() == 1, "A1 dòng lần nhận có nút Sửa lần nhận (bút chì)", await m.inner_text())
        if fails:
            await b.close(); print(f"KẾT QUẢ drive_n22 ({OUT}): {fails} SAI"); return
        await btn.first.click(); await pg.wait_for_timeout(1500)
        e = modal(pg); t = await e.inner_text()
        ok("Sửa lần nhận — phiếu #" in t and "Nhận OK" in t and "NG lúc nhận" in t and "Lý do sửa" in t, "A2 modal Sửa lần nhận: Nhận OK, NG lúc nhận, Lý do sửa *", t[:600])
        nums = e.locator("input[type=number]")
        await fill(nums.nth(0), pg, 14); await fill(nums.nth(1), pg, 6)
        await fill(e.locator("input[placeholder^='Nhập lý do']"), pg, "Phát hiện thêm 1 chiếc lỗi")
        t = await e.inner_text()
        ok("lần nhận 14 OK + 6 NG" in t and "lập thêm phiếu trả Kho 1 chiếc cùng vụ" in t and "không sửa phiếu trả cũ" in t,
           "A3 Sẽ ghi: lần nhận 14 OK + 6 NG; lập thêm phiếu trả Kho 1 chiếc cùng vụ (không sửa phiếu trả cũ)", t[-500:])
        await e.locator(".mes-modal-footer button.btn-primary:has-text('Lưu sửa')").click(); await pg.wait_for_timeout(2500)
        a1 = st("PO-N22A")
        act = [r for r in a1["recvs"] if not r["isVoided"]]
        old = [r for r in a1["recvs"] if r["isVoided"]]
        backs = [x for x in a1["txs"] if x["sourceReceiveId"] and not x["isVoided"]]
        ok(len(old) == 1 and len(act) == 2 and sum(r["qtyOk"] for r in act) == 14 and sum(r["qtyNg"] for r in act) == 6
           and all(r["notes"].startswith("[N22#%d]" % old[0]["receiveId"]) for r in act),
           "A4 DB: lần nhận cũ huỷ, 2 dòng cùng dấu [N22#gốc] tổng 14 OK + 6 NG", a1["recvs"])
        ok(sorted(x["qtyIssued"] for x in backs) == [1, 5] and len({x["ngCaseId"] for x in backs}) == 1, "A5 DB: phiếu trả cũ 5 giữ + phiếu trả mới 1, cùng vụ", backs)
        sane(a1, "A6")
        m = modal(pg); t = await m.inner_text()
        rows = []
        for x in await m.locator("table tbody tr").all():
            rows.append([(await c.inner_text()).strip() for c in await x.locator("td").all()])
        live = [x for x in rows if len(x) >= 7 and "Đã huỷ" not in x[6]]
        # cột: Thời gian · Người nhận · Vào ô · Đạt · NG · Lỗi · TT (rà độc lập T4: so đúng ô, không khớp nhầm giờ)
        ok(sum(1 for x in live if x[3] == "14" and x[4] == "6" and "Đã sửa lần nhận" in x[5] and "2 phiếu trả" in x[5]) == 1 and not any(x[3] == "0" and x[4] == "1" for x in live),
           "A7 Lịch sử: 1 dòng lần nhận còn hiệu lực 14 / 6 'Đã sửa lần nhận · 2 phiếu trả' (như mockup 24 OK + 6 NG)", rows)

        print("[STEP B] PO-N22B Sửa phiếu Hàng lỗi công đoạn trước 5 → 4 (D7 bước 1)")
        b0 = st("PO-N22B")
        m = await open_log(pg, "PO-N22B")
        sb = m.locator("button:has-text('Sửa phiếu')")
        ok(await sb.count() == 1, "B1 chỉ phiếu chờ nhận (Gia công → Kho) có nút Sửa phiếu (phiếu Kho → Gia công đã nhận: không có)", await m.inner_text())
        await sb.first.click(); await pg.wait_for_timeout(1500)
        e = modal(pg); t = await e.inner_text()
        ok("Sửa phiếu #" in t and "Giao đến" in t and "Số lượng" in t and "Loại lỗi" in t and "cùng mã vụ" in t,
           "B2 modal Sửa phiếu: Giao đến, Số lượng, Loại lỗi, Lý do; 'Sẽ ghi … cùng mã vụ'", t[:700])
        await fill(e.locator("input[type=number]").first, pg, 4)
        await fill(e.locator("input[placeholder^='Nhập lý do']"), pg, "Đếm nhầm số")
        await e.locator(".mes-modal-footer button.btn-primary:has-text('Lưu sửa')").click(); await pg.wait_for_timeout(2500)
        b1 = st("PO-N22B")
        oldT = [x for x in b1["txs"] if x["isVoided"]]
        newT = [x for x in b1["txs"] if not x["isVoided"] and x["fromGroupCode"] == "GC"]
        ok(len(oldT) == 1 and len(newT) == 1 and newT[0]["qtyIssued"] == 4 and newT[0]["ngCaseId"] == oldT[0]["ngCaseId"]
           and oldT[0]["voidReason"].startswith("Sửa phiếu → #%d" % newT[0]["handoverTxId"]) and "sửa từ phiếu #%d" % oldT[0]["handoverTxId"] in (newT[0]["notes"] or "")
           and len(b1["cases"]) == len(b0["cases"]),
           "B3 DB: phiếu cũ huỷ 'Sửa phiếu → #mới', phiếu mới 4 'sửa từ phiếu #cũ', cùng mã vụ, không sinh vụ mới", b1["txs"])
        sane(b1, "B4")

        print("[STEP C] PO-N22B Đổi loại lỗi (D7 bước 3)")
        vu = newT[0]["ngCaseId"] if newT else 0
        m = modal(pg)
        await m.locator("select").first.select_option(value=str(vu)); await pg.wait_for_timeout(1200)
        t = await m.inner_text()
        cb = m.locator("button:has-text('Đổi loại lỗi')")
        ok("Loại lỗi đang ghi" in t and await cb.count() == 1 and not await cb.first.is_disabled(), "C1 lọc mã vụ: dải 'Loại lỗi đang ghi' + nút Đổi loại lỗi bật (phiếu mang vụ chưa nhận)", t[:600])
        await cb.first.click(); await pg.wait_for_timeout(1200)
        e = modal(pg)
        cur = [c for c in b1["cats"] if c["ngCategoryId"] == b1["cases"][0]["ngCategoryId"]][0]
        other = [c for c in b1["cats"] if c["ngCategoryId"] != cur["ngCategoryId"]][0]
        inp = e.locator("input[list^='ngcat_']").first
        await inp.fill(other["label"]); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
        await fill(e.locator("input[placeholder^='Nhập lý do']"), pg, "Ghi nhầm loại lỗi")
        await e.locator(".mes-modal-footer button.btn-primary:has-text('Lưu sửa')").click(); await pg.wait_for_timeout(2500)
        c1 = st("PO-N22B")
        cs = [c for c in c1["cases"] if c["ngCaseId"] == vu]
        ok(cs and cs[0]["ngCategoryId"] == other["ngCategoryId"] and any(l["oldValue"] == cur["label"] and l["newValue"] == other["label"] and l["reason"] == "Ghi nhầm loại lỗi" for l in c1["logs"]),
           "C2 DB: vụ đổi loại lỗi, lịch sử PO 1 dòng (cũ → mới, lý do)", {"cases": c1["cases"], "logs": c1["logs"]})
        t = await modal(pg).inner_text()
        ok("Đổi loại lỗi " + cur["label"] + " → " + other["label"] in t, "C3 dải mã vụ hiện dòng 'Đổi loại lỗi cũ → mới · người · giờ · lý do'", t[:800])
        sane(c1, "C4")

        print("[STEP D] PO-N22C Gia công đã nhập WTS sau lần nhận → Sửa lần nhận xám (D7 bước 2b)")
        m = await open_log(pg, "PO-N22C")
        bx = m.locator("button[title^='Sửa lần nhận xám']")
        t = await m.inner_text()
        ok(await bx.count() == 1 and await bx.first.is_disabled() and "Sửa lần nhận xám" in t and "nhập WTS" in t and "Trả nơi làm lỗi" in t,
           "D1 nút Sửa lần nhận xám + dòng lý do 'đã nhập WTS … sau lần nhận', hướng báo lỗi Trả nơi làm lỗi", t[:900])
        await b.close()
    print(f"KẾT QUẢ drive_n22 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
