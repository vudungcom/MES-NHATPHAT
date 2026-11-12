# [KHO-TARO-THANG][KTT-PHOILOI][KTT-CHI-TARO 12/11/2026] Trang Giao nhận thật (harness EF giả) — NC ĐẦU Gia công là máy loại trừ có Taro (Taro làm NC01):
#   Kho giao THẲNG Taro (khách 12/11 câu 3.3 b = A; Hùng 12/11 14:00 OK kế hoạch, B1 / B2 / B3).
#   T1 form Giao / xử lý hàng · Kho · Hàng tốt · Phôi còn: nhóm nhận KHÔNG có Gia công (Workflow ON), mặc định "HTSP — Taro — làm NC01", có chữ "Kho giao thẳng Taro",
#      không hỏi ghi chú / NC → bấm Xác nhận → DB 1 phiếu HELP_SEND Kho → HTSP 6, FromNC @VAO, FromCell MOI, không NC / ô nhận
#   T2 HTSP nhận 5 tốt + 1 NG lúc nhận (endpoint) → NG tự trả về Kho (1 phiếu trả tự lập HTSP → Kho, vụ thường, không phải vụ làm hộ);
#      sổ: Kho còn giao 4 · đoạn Taro vào 5 · HTSP giữ hàng giao theo NC 5 · Gia công "Nhận vào" vào = giao theo NC · cân, 0 ô âm
#   T3 bảng "Gia công — từng NC": dòng "Nhận vào (chưa qua NC)" Chưa GC 0 (Kho giao thẳng — Gia công không giữ chiếc nào)
#   T4 Workflow OFF: Kho chọn được Gia công, có cảnh báo "nên giao thẳng Taro" (B1) — không lưu
#   T6 Kho chọn KCS + đổi ô bên nhận rồi chọn lại HTSP (Taro) → lưu được, không gửi ô cũ (rà độc lập Gói 18 Thấp 1)
#   T5 HTSP · Hàng lỗi · "Hàng giao theo NC chưa làm xong Taro NC01 (NC đầu)" có 5 → nhóm nhận cố định Kho, nơi gây lỗi Khách hàng →
#      DB 1 phiếu NG_RETURN HTSP → Kho 1, FromNC @VAO, ô Hàng lỗi, vụ mới nơi gây lỗi KHACH (B2: chỉ trả Kho); sổ: đoạn trả Kho 1, HTSP giữ 4, cân
# Dữ liệu: POST /harness/ktt-seed (Stubs/HarnessKtt.cs). Chạy trên harness VỪA khởi động: python3 drive_ktt.py <port> <old|new>
#   (bản cũ — master trước gói — phải SAI ở T1 / T5: chưa có giao thẳng Taro / dòng phôi lỗi Taro.)
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if info not in (None, "") and not cond else ""))
def modal(pg): return pg.locator(".mes-modal").last
def row(pg): return pg.locator("tr", has_text="PO-KTT").first
async def btn_texts(m):
    return [(await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all()]
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
def dest_sel(m, pg): return m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first
async def dest_opts(m, pg):
    s = dest_sel(m, pg)
    if not await s.count(): return [], ""
    return (await s.evaluate("s => Array.from(s.options).map(o => o.text)"), await s.evaluate("s => s.selectedIndex >= 0 ? s.options[s.selectedIndex].text : ''"))
async def qty(m, pg, n):
    q = m.locator("input[type=number]").first; await q.fill(str(n)); await q.dispatch_event("change"); await pg.wait_for_timeout(500)
async def pick_cat(m, pg, code):
    inp = m.locator("input[list^='ngcat_']").first
    if await inp.count(): await inp.fill(code); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
async def gc_steps(pg):
    tb = pg.locator("div.fw-semibold", has_text="Gia công — từng NC").first.locator("xpath=..").locator("table tbody tr")
    out = []
    for i in range(await tb.count()):
        out.append([(await c.inner_text()).strip() for c in await tb.nth(i).locator("td").all()])
    return out
async def expand(pg):
    if await pg.locator("button:has-text('Hiện công thức')").count():
        await pg.locator("button:has-text('Hiện công thức')").first.click(); await pg.wait_for_timeout(1200)
    await row(pg).locator("td").first.locator("button").click(); await pg.wait_for_timeout(2500)
def sane(d, name): ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", {k: d[k] for k in ("kho", "htsp", "gcVao", "runs")})

async def main():
    sd = post("/harness/ktt-seed"); cat = sd["cat"]
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[T1] Kho · Hàng tốt · Phôi còn → nhóm nhận chỉ Taro (Workflow ON)")
        opts, cur, t1, sent = [], "", "", False
        try:
            m = await open_form(pg, "Kho")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Phôi còn (chưa giao)")
            opts, cur = await dest_opts(m, pg)
            t1 = await m.inner_text()
            await qty(m, pg, 6)
            sent = await confirm(m, pg)
        except Exception as e:
            print("   lỗi thao tác T1:", e)
        ok(opts and not any(o.startswith("Gia công") for o in opts), "T11 [KTT-CHI-TARO] nhóm nhận không có Gia công", opts)
        ok(cur.startswith("HTSP") and "Taro" in cur and "NC01" in cur, "T12 nhóm nhận mặc định 'HTSP — Taro — làm NC01'", cur)
        ok("Kho giao thẳng Taro" in t1, "T13 form có chữ 'Kho giao thẳng Taro'", t1[-500:])
        d1 = get("/harness/ktt")
        k = [x for x in d1["txs"] if x["fromGroupCode"] == "KHO"]
        ok(sent and len(k) == 1 and k[0]["purpose"] == "HELP_SEND" and k[0]["toGroupCode"] == "HTSP" and k[0]["fromNC"] == "@VAO" and k[0]["fromCell"] == "MOI"
           and k[0]["toNC"] is None and k[0]["toCell"] is None and float(k[0]["qtyIssued"]) == 6,
           "T14 Xác nhận → DB 1 phiếu HELP_SEND Kho → HTSP 6, FromNC @VAO, FromCell MOI, không NC / ô nhận (ghi chú không bắt buộc)", d1["txs"])
        await close_modal(pg)

        print("[T2] HTSP nhận 5 + NG 1 lúc nhận → NG tự trả Kho")
        rv = post("/harness/ktt-recv?ok=5&ng=1")
        d2 = get("/harness/ktt")
        back = [x for x in d2["txs"] if x["fromGroupCode"] == "HTSP" and x["toGroupCode"] == "KHO" and x["sourceReceiveId"]]
        ok(rv.get("ok") and len(back) == 1 and float(back[0]["qtyIssued"]) == 1, "T21 nhận được; 1 phiếu trả tự lập HTSP → Kho 1 (NG lúc nhận)", [rv, d2["txs"]])
        run = d2["runs"][0] if d2["runs"] else {}
        ok(float(d2["kho"]["conGiao"]) == 4, "T22 Kho còn giao 4 (10 − 6)", d2["kho"])
        ok(run and float(run["in"]) == 5 and float(run["held"]) == 5 and float(d2["htsp"]["lamHoGiu"]) == 5,
           "T23 đoạn Taro NC01 vào 5 · HTSP giữ hàng giao theo NC 5 (chiếc NG không vào đoạn)", [run, d2["htsp"]])
        v = d2["gcVao"] or {}
        ok(v and float(v.get("helpOut") or 0) == float(v.get("in") or 0) and float(v.get("waiting") or 0) == 0,
           "T24 Gia công 'Nhận vào': vào = giao theo NC, chờ 0 (Gia công không giữ)", v)
        sane(d2, "T25")

        print("[T3] bảng Gia công — từng NC")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
        st = []
        try:
            await expand(pg); st = await gc_steps(pg)
        except Exception as e:
            print("   lỗi mở dòng:", e)
        vr = [r for r in st if r and r[0].startswith("Nhận vào (chưa qua NC)")]
        ok(len(vr) == 1 and vr[0][5] in ("0", "—"), "T31 dòng 'Nhận vào (chưa qua NC)': Chưa GC 0", st)

        print("[T4] Workflow OFF: Kho chọn được Gia công, có cảnh báo (B1)")
        post("/harness/g16-wf?po=PO-KTT&on=0")
        await pg.reload(); await pg.wait_for_timeout(4500)
        o4, t4 = [], ""
        try:
            m = await open_form(pg, "Kho")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Phôi còn (chưa giao)")
            o4, _ = await dest_opts(m, pg)
            await dest_sel(m, pg).select_option(label="Gia công"); await pg.wait_for_timeout(900)
            t4 = await m.inner_text()
        except Exception as e:
            print("   lỗi T4:", e)
        ok(any(o.startswith("Gia công") for o in o4) and "nên giao thẳng Taro" in t4, "T41 OFF: có Gia công + cảnh báo 'nên giao thẳng Taro'", [o4, t4[-400:]])
        await close_modal(pg)
        ok(len(get("/harness/ktt")["txs"]) == len(d2["txs"]), "T42 đóng form không ghi gì")
        post("/harness/g16-wf?po=PO-KTT&on=1")
        await pg.reload(); await pg.wait_for_timeout(4500)

        print("[T5] HTSP · Hàng lỗi · phôi lỗi Taro phát hiện khi đang làm → trả thẳng Kho (B2)")
        bt, bt3, opts5, t5, sent5 = [], [], [], "", False
        try:
            m = await open_form(pg, "HTSP")
            await click(m, pg, "Hàng lỗi")
            bt = await btn_texts(m)
            await click(m, pg, "Hàng giao theo NC chưa làm xong Taro")
            bt3 = await btn_texts(m)
            await click(m, pg, "Trả về Kho")
            opts5, _ = await dest_opts(m, pg)
            t5 = await m.inner_text()
            await qty(m, pg, 1); await pick_cat(m, pg, cat)
            sent5 = await confirm(m, pg)
        except Exception as e:
            print("   lỗi T5:", e)
        ok(any(x.startswith("Hàng giao theo NC chưa làm xong Taro NC01 (NC đầu)") and "5" in x for x in bt),
           "T51 bước 2 HTSP có dòng 'Hàng giao theo NC chưa làm xong Taro NC01 (NC đầu)' có 5", bt)
        ok("Trả về Kho (ô Hàng lỗi — phôi lỗi)" in bt3 and not any(x.startswith(("Giao cho nhóm khác", "Sửa tại nhóm", "Huỷ", "Trả khách")) for x in bt3),
           "T52 bước 3 chỉ có 'Trả về Kho (ô Hàng lỗi — phôi lỗi)' (B2: chỉ trả Kho)", bt3)
        ok(not opts5 and "Kho — vào ô Hàng lỗi (phôi lỗi, nơi gây lỗi Khách hàng)" in t5.replace("\n", " "),
           "T53 nhóm nhận cố định Kho (không có ô chọn), nơi gây lỗi Khách hàng tự điền", [opts5, t5[-500:]])
        d5 = get("/harness/ktt")
        a = [x for x in d5["txs"] if x["purpose"] == "NG_RETURN" and x["fromGroupCode"] == "HTSP" and not x["sourceReceiveId"]]
        cs = [c for c in d5["cases"] if a and c["ngCaseId"] == a[0]["ngCaseId"]]
        ok(sent5 and len(a) == 1 and a[0]["fromNC"] == "@VAO" and a[0]["toGroupCode"] == "KHO" and a[0]["toCell"] == "LOI" and float(a[0]["qtyIssued"]) == 1,
           "T54 DB: 1 phiếu NG_RETURN HTSP → Kho 1, FromNC @VAO, ô Hàng lỗi", d5["txs"])
        ok(len(cs) == 1 and cs[0]["causeGroupCode"] == "KHACH" and cs[0]["detectGroupCode"] == "HTSP", "T55 vụ mới: nơi gây lỗi KHACH, phát hiện HTSP", cs)
        run5 = d5["runs"][0] if d5["runs"] else {}
        ok(run5 and float(run5.get("traKho") or 0) == 1 and float(run5["held"]) == 4 and float(d5["htsp"]["lamHoGiu"]) == 4,
           "T56 đoạn Taro: trả Kho 1, HTSP giữ hàng giao theo NC 4", [run5, d5["htsp"]])
        sane(d5, "T57")
        await close_modal(pg)

        print("[T6] Kho chọn KCS + đổi ô bên nhận rồi quay về HTSP (Taro) → vẫn lưu được (ô chọn cũ không gửi lên — rà độc lập Thấp 1)")
        await pg.reload(); await pg.wait_for_timeout(4500)
        sent6, cells6 = False, []
        try:
            m = await open_form(pg, "Kho")
            await click(m, pg, "Hàng tốt"); await click(m, pg, "Phôi còn (chưa giao)")
            await dest_sel(m, pg).select_option(label="KCS"); await pg.wait_for_timeout(900)
            tb = m.locator("button:has-text('Tuỳ chọn: đổi ô bên nhận')")
            if await tb.count():
                await tb.first.click(); await pg.wait_for_timeout(600)
                cells6 = await btn_texts(m)
                xl = m.locator("button.gn-cellbtn", has_text="Hàng lỗi đã xử lý")
                if await xl.count(): await xl.first.click(); await pg.wait_for_timeout(600)
            opts6, _ = await dest_opts(m, pg)
            await dest_sel(m, pg).select_option(label=[o for o in opts6 if o.startswith("HTSP")][0]); await pg.wait_for_timeout(900)
            await qty(m, pg, 2)
            sent6 = await confirm(m, pg)
        except Exception as e:
            print("   lỗi T6:", e)
        d6 = get("/harness/ktt")
        k6 = [x for x in d6["txs"] if x["fromGroupCode"] == "KHO" and x["purpose"] == "HELP_SEND"]
        ok(sent6 and len(k6) == 2 and float(k6[-1]["qtyIssued"]) == 2 and k6[-1]["toCell"] is None and await pg.locator(".mes-modal").count() == 0,
           "T61 lưu được phiếu Kho giao thẳng Taro 2 (không ô nhận), form đóng", [cells6, d6["txs"][-2:]])
        await close_modal(pg)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
