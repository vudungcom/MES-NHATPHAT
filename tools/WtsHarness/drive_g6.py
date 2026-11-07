# [TARO-LAMHO-NC 07/11/2026] Gói 6 — Taro làm hộ ở NC máy loại trừ GIỮA chuỗi Gia công (như Part 3B-PT1268610 của khách), bấm trang thật (harness EF giả).
#   Dữ liệu: POST /harness/g6-seed (HarnessSua.SeedG6Async — PO-LAMHO: GC NC01 → NC02 → NC03 H01 → NC04 → NC05 H01; HTSP nhận 10 hàng giao theo NC từ NC02;
#     WTS Taro NC 3: 2T 8, 3T OK 5 NG 1, 3T1 2 → OK bộ 7, 1 chiếc làm hộ bị lỗi). Hùng 07/11 10:18 OK kế hoạch.
#   L1 sổ: đoạn làm hộ NC03 (NC02 → NC04), HTSP nhận 10, OK bộ 7; 4 số HTSP không đổi (Chưa làm 0, Đã làm 0, Tồn hàng lỗi 0); Tồn hàng giao theo NC 10, 1 chiếc lỗi.
#   L2 form Giao, HTSP, Hàng tốt: dòng "Đang giữ hàng Gia công giao theo NC (làm hộ)" có 7 (câu 1: OK bộ − đã trả), NC nhận mặc định NC04, số mặc định 7;
#      gõ 8 → báo vượt tối đa 7; ô NC nhận chỉ có NC04 (NC sau đoạn). Xác nhận 7 vào NC04 → phiếu Trả về Gia công ToNC NC04 7.
#   L3 form Giao, HTSP, Hàng lỗi: dòng "Hàng làm hộ bị lỗi (Taro làm hộ NG, đã có vụ)" có 1 → việc "Trả về Gia công (ô Hàng lỗi, cùng vụ)" (chỉ việc này),
#      nhóm nhận cố định Gia công → Xác nhận → phiếu Trả về Gia công FromCell LOI → ToCell LOI, mang vụ, không NC.
#   L4 Gia công bấm Nhận phiếu trả lỗi: không có ô 'Nhận vào NC', chỉ nhập ô Hàng lỗi → lần nhận ô Hàng lỗi, Tồn hàng lỗi Gia công + 1 (cùng vụ).
#   Mọi bước: sổ cân, 0 ô âm. Bản Gói 5: L1 / L2 / L3 SAI (không có đoạn làm hộ, dòng LAMHO có 10, không có dòng hàng làm hộ bị lỗi).
# Chạy trên harness VỪA khởi động: python3 drive_g6.py <port> <nhãn>
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
def row(pg): return pg.locator("tr", has_text="PO-LAMHO").first
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
def sane(d, name): ok(d.get("balanced") and d.get("neg") == 0, name + ": sổ cân, 0 ô âm", d)

async def main():
    seed = post("/harness/g6-seed"); print("seed", seed)
    d0 = get("/harness/g6")
    run = next((r for r in d0.get("runs", []) if r["hasTaro"]), None)
    print("[L1] sổ Taro làm hộ")
    ok(run is not None and run["key"] == "NC03" and run["fromNc"] == "NC02" and run["nextNc"] == "NC04" and run["in"] == 10 and run["done"] == 7,
       "L1a đoạn làm hộ NC03 (NC02 → NC04), HTSP nhận vào 10, OK bộ Taro 7", d0.get("runs"))
    ok(d0.get("htsp", {}).get("chuaLam") == 0 and d0.get("htsp", {}).get("daLam") == 0 and d0.get("htsp", {}).get("loiGiu") == 0 and d0.get("lamHoGiu") == 10 and d0.get("lamHoNg") == 1,
       "L1b 4 số HTSP không đổi; Tồn hàng giao theo NC 10, trong đó 1 chiếc làm hộ bị lỗi", d0)
    sane(d0, "L1c")
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[L2] hàng tốt: trả hàng làm hộ về Gia công ≤ OK bộ Taro (câu 1)")
        m = await open_form(pg, "HTSP")
        await click(m, pg, "Hàng tốt")
        bt = await btn_texts(m)
        lh = [x for x in bt if x.startswith("Đang giữ hàng Gia công giao theo NC")]
        ok(len(lh) == 1 and lh[0].endswith("có 7") and "NC04" in lh[0], "L2a dòng hàng làm hộ có 7 (OK bộ Taro − đã trả), ghi NC nhận NC04", bt)
        await click(m, pg, "Đang giữ hàng Gia công giao theo NC")
        sel = m.locator("select:has(option:has-text('-- Chọn NC --'))").first
        cur = await sel.input_value() if await sel.count() else None
        qty = m.locator("input[type=number]").first
        ok(cur == "NC04" and await qty.input_value() == "7", "L2b NC nhận mặc định NC04, số mặc định 7", f"nc={cur} qty={await qty.input_value()}")
        await qty.fill("8"); await qty.press("Tab"); await pg.wait_for_timeout(500)
        t = await m.inner_text()
        ok("Vượt tối đa 7" in t and await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").is_disabled(), "L2c gõ 8 → báo vượt tối đa 7, nút Xác nhận khoá", t[-400:])
        opts = [await o.get_attribute("value") for o in await sel.locator("option").all()]
        ok([x for x in opts if x] == ["NC04"], "L2d ô NC nhận chỉ có NC04 (NC sau đoạn máy loại trừ) — PO có Taro làm hộ", opts)
        await qty.fill("7"); await qty.press("Tab"); await pg.wait_for_timeout(400)
        okc = await confirm(m, pg)
        d1 = get("/harness/g6")
        ret = [x for x in d1["txs"] if x["purpose"] == "HELP_RETURN" and x["fromCell"] is None]
        ok(okc and len(ret) == 1 and ret[0]["toNC"] == "NC04" and ret[0]["qtyIssued"] == 7, "L2e Xác nhận 7 → phiếu Trả về Gia công theo NC vào NC04, 7", d1["txs"])
        sane(d1, "L2f")
        await close_modal(pg)

        print("[L3] hàng lỗi: hàng làm hộ bị lỗi → trả Gia công ô Hàng lỗi, cùng vụ (câu 2)")
        m = await open_form(pg, "HTSP")
        await click(m, pg, "Hàng lỗi")
        bt = await btn_texts(m)
        ll = [x for x in bt if x.startswith("Hàng làm hộ bị lỗi")]
        ok(len(ll) == 1 and ll[0].endswith("có 1"), "L3a dòng 'Hàng làm hộ bị lỗi (Taro làm hộ NG, đã có vụ)' có 1", bt)
        await click(m, pg, "Hàng làm hộ bị lỗi")
        acts = [x for x in await btn_texts(m) if x.startswith("Trả về Gia công") or x in ("Giao cho nhóm khác", "Huỷ", "Trả khách", "Sửa tại nhóm (không giao đi)")]
        ok(acts == ["Trả về Gia công (ô Hàng lỗi, cùng vụ)"], "L3b chỉ có việc 'Trả về Gia công (ô Hàng lỗi, cùng vụ)'", acts)
        await click(m, pg, "Trả về Gia công (ô Hàng lỗi, cùng vụ)")
        t3 = await m.inner_text()
        ok("trả hàng làm hộ bị lỗi về Gia công" in t3 and "Sẽ ghi: phiếu trả hàng làm hộ bị lỗi" in t3, "L3c nhóm nhận cố định Gia công, dòng Sẽ ghi đúng", t3[-500:])
        okc = await confirm(m, pg)
        d2 = get("/harness/g6")
        lo = [x for x in d2["txs"] if x["purpose"] == "HELP_RETURN" and x["fromCell"] == "LOI"]
        ok(okc and len(lo) == 1 and lo[0]["toCell"] == "LOI" and lo[0]["ngCaseId"] is not None and lo[0]["toNC"] is None and lo[0]["qtyIssued"] == 1,
           "L3d Xác nhận → phiếu Trả về Gia công ô Hàng lỗi → ô Hàng lỗi, mang vụ, không NC", d2["txs"])
        ok(d2["lamHoNg"] == 0 and d2["lamHoGiu"] == 2, "L3e HTSP: 0 chiếc lỗi, Tồn hàng giao theo NC 2 (2 chiếc chưa xong bộ Taro)", d2)
        sane(d2, "L3f")
        await close_modal(pg)

        print("[L4] Gia công nhận hàng làm hộ bị lỗi: chỉ ô Hàng lỗi, không chọn NC")
        lid = lo[0]["handoverTxId"] if lo else None
        await row(pg).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
        m = modal(pg)
        if await m.locator("select").count():
            await m.locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1500)
        hdr = m.locator("div[style*='cursor:pointer']", has_text="Lỗi:").first
        opened = lid is not None and await hdr.count() > 0
        if opened: await hdr.click(); await pg.wait_for_timeout(900)
        t4 = await m.inner_text()
        nums = [x for x in await m.locator("input[type=number]").all() if await x.is_enabled() and await x.is_visible()]
        ok(opened and "Nhận vào NC" not in t4 and "Hàng lỗi đã xử lý" not in t4 and "Nhận vào ô Hàng lỗi" in t4 and len(nums) == 1, "L4a mở phiếu trả lỗi hàng làm hộ: không có ô 'Nhận vào NC', không có nút đổi ô, chỉ 1 ô số được nhập (ô Hàng lỗi)", t4[:900] + f" nums={len(nums)}")
        if nums:
            await nums[0].fill("1"); await nums[0].dispatch_event("change"); await pg.wait_for_timeout(300)
            await m.locator("button:has-text('Xác nhận nhận')").first.click(); await pg.wait_for_timeout(2500)
        d3 = get("/harness/g6")
        rl = [r for r in d3["recs"] if r["handoverTxId"] == lid]
        ok(len(rl) == 1 and rl[0]["toCell"] == "LOI" and rl[0]["qtyOk"] + rl[0]["qtyNg"] == 1 and d3["gcLoi"] == 1 and d3["caseGcLoi"] == 1,
           "L4b DB: lần nhận vào ô Hàng lỗi 1; Gia công Tồn hàng lỗi 1 (vụ về Gia công)", d3)
        sane(d3, "L4c")
        await close_modal(pg)
        await b.close()
    print(f"KẾT QUẢ ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
