# [N19-SCEN 26/11/2026] Trang Giao nhận thật (harness EF giả) — luật N19 Cách A (khách chốt mockup TH1 / TH1b 23/11): phôi khách gửi bù.
#   Dữ liệu: POST /harness/n19-seed (Stubs/HarnessN19.cs). Đọc DB: GET /harness/n19?po=…
#   STEP A (TH1, PO-N19A — phôi lỗi Kho phát hiện, đã trả khách 1): ô Kho · Nhận · Hàng lỗi đã xử lý có nút "Nhận phôi bù" → cửa sổ "Nhận phôi khách gửi bù"
#          (đã trả khách 1 (từ Kho), còn bù 1; Sẽ ghi … giao Gia công như phôi thường) → Nhận → DB 1 phiếu bù; form Kho: bước 1 KHÔNG còn nút "Khách gửi bù";
#          Hàng tốt → "Phôi còn (chưa giao)" ghi "gồm 1 phôi khách gửi bù"; giao Gia công: không có ô "Bù cho mã vụ", chữ "Không cần chọn mã vụ" → phiếu Hàng mới, không mã vụ
#   STEP B (TH1b bước 1, PO-N19B — Gia công đã làm NC01 cho 10): Hàng lỗi → "Đã làm xong NC01 · chưa làm NC02" → tích Lỗi công đoạn trước: nơi gây lỗi chỉ "Khách hàng"
#          → Trả khách 1 (loại lỗi) → DB: vụ Gia công phát hiện, nơi gây lỗi Khách hàng, trả khách 1
#   STEP C (TH1b bước 2–3): Nhận phôi bù (Sẽ ghi "gắn vụ VL…") → form Kho: "gồm 1 phôi khách gửi bù của VL…", ô "Bù cho mã vụ *" (bắt buộc), chọn VL… →
#          "Sẽ ghi: phiếu Kho → Gia công 1, mang mã vụ VL… (… làm từ NC01)" → DB phiếu ô chỉ định XLBU, NC01, vụ
#   STEP D (TH1b bước 4): cửa sổ Nhận · Gia công: nhãn "Phôi khách gửi bù (bù theo mã vụ)", "Vào ô Hàng lỗi đã xử lý — VL… (bù) · làm từ NC01", Workflow ON không có
#          ô chọn "Nhận vào ô" → nhận 1 → DB lần nhận ô XL; sổ mockup cột Cách A: Kho Giao · Hàng mới 11, Tồn 0; Gia công Nhận · Hàng lỗi đã xử lý 1, Tồn 10, NC01 chờ 1
#   Bản cũ (45a1f0b, trước G-F) phải SAI ở STEP A (không có nút "Nhận phôi bù") và STEP B (B2: chưa nới Lỗi công đoạn trước cho Khách hàng ở hàng đã gia công).
# Chạy trên harness VỪA khởi động: python3 drive_n19.py <port> <old|new>
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
CAT = "KT-SAI · Sai kích thước"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def st(po): return get("/harness/n19?po=" + po)
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
def porow(pg, po): return pg.locator("tr", has=pg.locator("td", has_text=re.compile(r"^\s*" + po + r"\s*$"))).first
def sane(d, name): ok(d["balanced"] and d["neg"] == 0, name + ": sổ cân, 0 ô âm", {"kho": d.get("kho"), "gc": d.get("gc")})
def vl(i): return "VL%06d" % i
async def click(m, pg, text, wait=800):
    await m.locator("button.gn-cellbtn", has_text=text).first.click(); await pg.wait_for_timeout(wait)
async def num(m, pg, i, v):
    x = m.locator("input[type=number]").nth(i); await x.fill(str(v)); await x.dispatch_event("change"); await pg.wait_for_timeout(500)
async def pick_cat(m, pg, label):
    inp = m.locator("input[list^='ngcat_']").first
    await inp.fill(label); await inp.dispatch_event("change"); await pg.wait_for_timeout(500)
async def confirm(m, pg):
    b = m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')")
    dis = await b.is_disabled()
    if not dis: await b.click(); await pg.wait_for_timeout(2500)
    return not dis
async def home(pg):
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4500)
async def open_form(pg, po, grp):
    await porow(pg, po).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    sel = modal(pg).locator("select").first
    if await sel.count(): await sel.select_option(label=grp); await pg.wait_for_timeout(900)
    return modal(pg)
async def nhan_bu(pg, po):
    btn = porow(pg, po).locator("button:has-text('Nhận phôi bù')")
    if await btn.count() == 0: return None
    await btn.first.click(); await pg.wait_for_timeout(1500)
    return modal(pg)

async def main():
    post("/harness/n19-seed")
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await home(pg)

        print("[STEP A] TH1 PO-N19A: nút Nhận phôi bù ở cột Nhận của Kho → nhận 1 → Kho giao Gia công như phôi thường")
        a0 = st("PO-N19A")
        m = await nhan_bu(pg, "PO-N19A")
        ok(m is not None, "A1 ô Kho · Nhận · Hàng lỗi đã xử lý có nút 'Nhận phôi bù' (mockup TH1 bước 2)")
        if m is not None:   # bản cũ không có nút: bỏ phần còn lại của A, vẫn chạy B (bản cũ phải SAI cả B2 — chưa nới Lỗi công đoạn trước cho Khách hàng)
            t = await m.inner_text()
            ok("Nhận phôi khách gửi bù" in t and "Bù cho lần trả khách nào" in t and "đã trả khách 1 (từ Kho), còn bù 1" in t and "Số thực nhận" in t,
               "A2 cửa sổ 'Nhận phôi khách gửi bù': lần trả khách (đã trả khách 1 (từ Kho), còn bù 1), Số thực nhận", t[:700])
            ok("Kho Nhận · Hàng lỗi đã xử lý +1 (phôi dùng được)" in t and "như phôi thường" in t, "A3 Sẽ ghi: Kho Nhận · Hàng lỗi đã xử lý +1 — giao Gia công như phôi thường", t[-400:])
            await m.locator(".mes-modal-footer button.btn-primary:has-text('Nhận')").click(); await pg.wait_for_timeout(2500)
            a1 = st("PO-N19A")
            ok(len(a1["bu"]) == 1 and a1["bu"][0]["qty"] == 1 and a1["kho"]["nx"] == 1 and a1["kho"]["ton"] == 1, "A4 DB: 1 phiếu bù 1; Kho Nhận · Hàng lỗi đã xử lý 1, Tồn 1 (mockup sau bước 2)", a1)
            sane(a1, "A5")
            await home(pg)
            m = await open_form(pg, "PO-N19A", "Kho")
            kinds = [(await x.inner_text()).replace("\n", " ") for x in await m.locator("button.gn-cellbtn").all()]
            ok(not any("Khách gửi bù" in x for x in kinds), "A6 form Kho bước 1 không còn nút 'Khách gửi bù' (nút đã ở cột Nhận của Kho)", kinds)
            await click(m, pg, "Hàng tốt")
            t = await m.inner_text()
            ok("Phôi còn (chưa giao)" in t and "gồm 1 phôi khách gửi bù" in t and "gồm 1 phôi khách gửi bù của" not in t, "A7 dòng 'Phôi còn (chưa giao)' ghi 'gồm 1 phôi khách gửi bù' (mockup TH1 bước 3)", t[:600])
            await click(m, pg, "Phôi còn (chưa giao)")
            t = await m.inner_text()
            ok("Bù cho mã vụ" not in t and "Không cần chọn mã vụ — phôi bù đi như phôi thường" in t, "A8 giao Gia công: không có ô 'Bù cho mã vụ', chữ 'Không cần chọn mã vụ — phôi bù đi như phôi thường'", t[-700:])
            sent = await confirm(m, pg)
            a2 = st("PO-N19A")
            nt = [x for x in a2["txs"] if x not in a1["txs"]]
            ok(sent and len(nt) == 1 and nt[0]["toCell"] == "MOI" and nt[0]["ngCaseId"] is None and nt[0]["qtyIssued"] == 1 and a2["kho"]["gm"] == 10 and a2["kho"]["ton"] == 0,
               "A9 DB: phiếu Kho → Gia công Hàng mới 1, không mã vụ; Kho Giao · Hàng mới 10, Tồn 0 (mockup sau bước 3)", nt)
            sane(a2, "A10")

        print("[STEP B] TH1b bước 1 PO-N19B: Gia công đã làm NC01 → phôi lỗi, tích Lỗi công đoạn trước (chỉ Khách hàng) → Trả khách 1")
        await home(pg)
        m = await open_form(pg, "PO-N19B", "Gia công")
        await click(m, pg, "Hàng lỗi")
        await click(m, pg, "Đã làm xong NC01 · chưa làm NC02", 900)
        cb = m.locator("#gxlPrev")
        ok(await cb.count() == 1, "B1 hàng đã gia công: vẫn có ô tích 'Lỗi công đoạn trước'", await m.inner_text())
        if await cb.count() == 0:
            await b.close(); print(f"KẾT QUẢ drive_n19 ({OUT}): {fails} SAI"); return
        await cb.check(); await pg.wait_for_timeout(700)
        sel = m.locator("select").nth(1)
        opts = [x.strip() for x in await sel.locator("option").all_inner_texts()]
        t = await m.inner_text()
        ok(opts == ["-- Chọn nơi gây lỗi --", "phôi khách"] and "chỉ chọn được Khách hàng" in t, "B2 nơi gây lỗi chỉ 'phôi khách' (Khách hàng) + dòng giải thích (mockup TH1b bước 1)", {"opts": opts})
        await sel.select_option(label="phôi khách"); await pg.wait_for_timeout(700)
        await click(m, pg, "Trả khách")
        await num(m, pg, 0, 1); await pick_cat(m, pg, CAT)
        sent = await confirm(m, pg)
        bb = st("PO-N19B")
        vu = [c for c in bb["cases"] if c["traKhach"] == 1]
        ok(sent and len(vu) == 1 and vu[0]["detectGroup"] == "GC" and vu[0]["cause"] == "KHACH" and vu[0]["qua"] and bb["traKhach"] == 1,
           "B3 DB: vụ Gia công phát hiện, nơi gây lỗi Khách hàng, đã qua Gia công, trả khách 1", bb["cases"])
        sane(bb, "B4")
        if not vu:
            await b.close(); print(f"KẾT QUẢ drive_n19 ({OUT}): {fails} SAI"); return
        V = vl(vu[0]["id"])

        print("[STEP C] TH1b bước 2–3: Nhận phôi bù (gắn vụ) → form Kho chọn 'Bù cho mã vụ'")
        await home(pg)
        m = await nhan_bu(pg, "PO-N19B")
        t = await m.inner_text() if m else ""
        ok(m is not None and f"{V}" in t and "đã trả khách 1 (từ Gia công), còn bù 1" in t and f"gắn vụ {V}" in t,
           "C1 cửa sổ nhận bù: VL… đã trả khách 1 (từ Gia công), còn bù 1; Sẽ ghi … gắn vụ VL… (mockup TH1b bước 2)", t[:800])
        if m: await m.locator(".mes-modal-footer button.btn-primary:has-text('Nhận')").click(); await pg.wait_for_timeout(2500)
        c1 = st("PO-N19B")
        ok(c1["kho"]["nx"] == 1 and c1["kho"]["ton"] == 1 and [c["cho"] for c in c1["cases"] if c["traKhach"] == 1] == [1], "C2 DB: Kho Nhận · Hàng lỗi đã xử lý 1, Tồn 1; vụ còn chờ giao theo vụ 1", c1)
        await home(pg)
        m = await open_form(pg, "PO-N19B", "Kho")
        await click(m, pg, "Hàng tốt")
        t = await m.inner_text()
        ok(f"gồm 1 phôi khách gửi bù của {V}" in t, "C3 dòng 'Phôi còn' ghi 'gồm 1 phôi khách gửi bù của VL…' (mockup TH1b bước 3)", t[:600])
        await click(m, pg, "Phôi còn (chưa giao)")
        t = await m.inner_text()
        bsel = m.locator("select", has=pg.locator("option", has_text="đã qua Gia công"))
        ok("Bù cho mã vụ" in t and await bsel.count() == 1, "C4 có ô 'Bù cho mã vụ *'", t[-800:])
        b_on = await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").is_disabled()
        ok(b_on and "Chọn ô \"Bù cho mã vụ\"" in t, "C5 chưa chọn mã vụ: nút Xác nhận khoá + lý do", t[-500:])
        if await bsel.count():
            vals = [x.strip() for x in await bsel.locator("option").all_inner_texts()]
            ok(any(V in x and "đã qua Gia công" in x for x in vals) and not any(x.startswith("Phôi thường") for x in vals),
               "C6 lựa chọn: VL… · đã qua Gia công (Workflow ON phôi thường còn 0 — không có dòng 'Phôi thường')", vals)
            await bsel.select_option(value=str(vu[0]["id"])); await pg.wait_for_timeout(800)
        t = await m.inner_text()
        ok(f"Sẽ ghi: phiếu Kho → Gia công 1, mang mã vụ {V}" in t and "làm từ NC01" in t, "C7 Sẽ ghi: phiếu Kho → Gia công 1, mang mã vụ VL… (Gia công nhận ô Hàng lỗi đã xử lý, làm từ NC01)", t[-500:])
        sent = await confirm(m, pg)
        c2 = st("PO-N19B")
        nt = [x for x in c2["txs"] if x not in c1["txs"]]
        ok(sent and len(nt) == 1 and nt[0]["fromCell"] == "MOI" and nt[0]["toCell"] == "XLBU" and nt[0]["ngCaseId"] == vu[0]["id"] and nt[0]["toNC"] == "NC01" and nt[0]["qtyIssued"] == 1,
           "C8 DB: phiếu Kho → Gia công 1, ô Hàng mới → ô chỉ định XLBU, NC01, mang mã vụ", nt)
        ok(c2["kho"]["gm"] == 11 and c2["kho"]["ton"] == 0 and c2["gc"]["cho"] == 1, "C9 sổ: Kho Giao · Hàng mới 11, Tồn 0; Gia công chờ nhận 1", c2)
        sane(c2, "C10")

        print("[STEP D] TH1b bước 4: Gia công nhận phôi bù theo vụ")
        await home(pg)
        await porow(pg, "PO-N19B").locator("button:has-text('Nhận')").first.click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1500)
        m = modal(pg); t = await m.inner_text()
        ok("Phôi khách gửi bù (bù theo mã vụ)" in t, "D1 thẻ phiếu có nhãn 'Phôi khách gửi bù (bù theo mã vụ)'", t[:500])
        await m.locator("text=Từ Kho").first.click(); await pg.wait_for_timeout(1000)
        t = await m.inner_text()
        pick = await m.locator("label", has_text="Nhận vào ô").count()
        ok(f"Hàng lỗi đã xử lý — {V} (bù)" in t and "làm từ NC01" in t and pick == 0,
           "D2 'Vào ô Hàng lỗi đã xử lý — VL… (bù) · làm từ NC01'; Workflow ON không có ô chọn 'Nhận vào ô' (mockup TH1b bước 4)", {"pick": pick, "t": t[:900]})
        okin = m.locator("input[type=number]").first
        await okin.fill("1"); await okin.dispatch_event("change"); await pg.wait_for_timeout(400)
        await m.locator("button.btn-success:has-text('Xác nhận')").first.click(); await pg.wait_for_timeout(2500)
        d = st("PO-N19B")
        nr = [x for x in d["recvs"] if x not in c2["recvs"]]
        ok(len(nr) == 1 and nr[0]["toCell"] == "XL" and nr[0]["qtyOk"] == 1, "D3 DB: lần nhận ô Hàng lỗi đã xử lý 1", nr)
        cs = [c for c in d["cases"] if c["id"] == vu[0]["id"]]
        ok(d["kho"]["nm"] == 10 and d["kho"]["nx"] == 1 and d["kho"]["gm"] == 11 and d["kho"]["ton"] == 0 and d["gc"]["nm"] == 10 and d["gc"]["nx"] == 1
           and d["traKhach"] == 1 and d["gc"]["ton"] == 10 and d["nc"].get("NC01") == 1 and d["nc"].get("NC02") == 9,
           "D4 sổ = mockup TH1b cột 'Sau bước 4 — cách A': Kho 10 / 1 / 11 / Tồn 0 · Gia công Nhận · Hàng mới 10, Nhận · Hàng lỗi đã xử lý 1, trả khách 1, Tồn 10 (NC01 chờ 1, NC02 chờ 9)", d)
        ok(len(cs) == 1 and cs[0]["buVao"] == 1 and cs[0]["cho"] == 0 and cs[0]["lech"] == 0, "D5 vụ: Bù vào 1, chờ bù 0, cân", cs)
        sane(d, "D6")
        await home(pg)
        cells = [(await x.inner_text()).strip() for x in await porow(pg, "PO-N19B").locator("td.gn-num").all()]
        ok("Nhận phôi bù" not in " ".join(cells[:12]), "D7 bảng: hết lần trả khách còn bù → không còn nút 'Nhận phôi bù'", cells[:12])
        await b.close()
    print(f"KẾT QUẢ drive_n19 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
