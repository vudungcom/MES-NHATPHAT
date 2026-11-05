# [N3-FIRST 04/11/2026] Trang Giao nhận thật (harness EF giả) — NC ĐẦU chuỗi Gia công là máy loại trừ (H04):
#   hàng vào Gia công nằm ở dòng "Nhận vào (chưa qua NC)" (mã @VAO), giao theo NC từ đây cho nhóm làm H04, NC02 chỉ nhận hàng trả về.
#   N1 bảng "Gia công — từng NC" có dòng Nhận vào (chưa qua NC): vào 8, Đã GC "—", Chưa GC 8; NC02 vào 0
#   N2 form Giao / xử lý hàng [GN-FORM1] · Gia công · Hàng tốt: dòng "Nhận vào (chưa qua NC) · chờ giao làm NC01 (máy loại trừ)" có 8; giao 5 cho HTSP → DB phiếu FromNC = @VAO, 5 chiếc
#   N3 sau khi giao: dòng Nhận vào còn chờ 3, Giao đi 5 (số cân / không âm kiểm ở GiaoNhanFuzz chuoi C14 + nang)
#   N4 cửa sổ Giao Kho → Gia công Hàng mới: ô "Nhận vào NC" KHÔNG có @VAO, dòng mặc định ghi "Nhận vào (chưa qua NC) (mặc định)"
# Dữ liệu: POST /harness/n3f-seed (Stubs/HarnessN3f.cs). Chạy trên harness VỪA khởi động: python3 drive_n3f.py <port> <old|new>
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if info not in (None, "") else ""))
DID = 0
def txs():
    return [t for t in get("/harness/n3f") if not t.get("isVoided")]
def modal(pg): return pg.locator(".mes-modal").first
async def close_modal(pg):
    if await pg.locator(".mes-modal").count():
        await modal(pg).locator(".mes-modal-footer button.btn-secondary").first.click(); await pg.wait_for_timeout(700)
async def gc_steps(pg):
    # bảng "Gia công — từng NC" trong phần mở rộng của dòng PO-N3F → list các dòng (ô chữ)
    tb = pg.locator("div.fw-semibold", has_text="Gia công — từng NC").first.locator("xpath=..").locator("table tbody tr")
    out = []
    for i in range(await tb.count()):
        out.append([ (await c.inner_text()).strip() for c in await tb.nth(i).locator("td").all() ])
    return out
async def expand(pg):
    if await pg.locator("button:has-text('Hiện công thức')").count():   # bảng từng NC chỉ hiện khi bật Hiện công thức
        await pg.locator("button:has-text('Hiện công thức')").first.click(); await pg.wait_for_timeout(1200)
    row = pg.locator("tr", has_text="PO-N3F").first
    await row.locator("td").first.locator("button").click(); await pg.wait_for_timeout(2500)
async def open_issue_from(pg, grp):
    await pg.locator("tr", has_text="PO-N3F").first.locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
    await modal(pg).locator("select").first.select_option(label=grp); await pg.wait_for_timeout(1000)

async def main():
    global DID
    DID = post("/harness/n3f-seed")["khPlanDetailId"]
    async with async_playwright() as p:
        b = await p.chromium.launch(); pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1100})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)

        print("[N1] bảng từng NC có dòng Nhận vào (chưa qua NC)")
        try:
            await expand(pg); st = await gc_steps(pg)
        except Exception as e:
            st = []; print("   lỗi mở dòng:", e)
        v = [r for r in st if r and r[0].startswith("Nhận vào (chưa qua NC)")]
        n2 = [r for r in st if r and r[0].startswith("NC02")]
        ok(len(v) == 1 and v[0][2] == "8" and v[0][3] == "—" and v[0][5] == "8", "N11 dòng 'Nhận vào (chưa qua NC)': Nhận vào 8 · Đã GC — · Chưa GC 8", st)
        ok(len(n2) == 1 and n2[0][2] == "0", "N12 NC02 (sau máy loại trừ) nhận vào 0 — chỉ nhận hàng trả về", n2)
        ok(len(st) >= 2 and st[0][0].startswith("Nhận vào") and "loại trừ" in st[1][0], "N13 thứ tự: Nhận vào → NC01 loại trừ → NC02", [r[0] if r else "" for r in st])

        print("[N2] Giao theo NC từ 'Nhận vào (chưa qua NC)' (form Giao / xử lý hàng [GN-FORM1]: Hàng tốt → dòng Nhận vào)")
        opts = []; saved = False
        try:
            await open_issue_from(pg, "Gia công")
            m = modal(pg)
            await m.locator("button.gn-cellbtn", has_text="Hàng tốt").first.click(); await pg.wait_for_timeout(800)
            opts = [ (await x.inner_text()).replace("\n", " ").strip() for x in await m.locator("button.gn-cellbtn").all() ]
            vb = m.locator("button.gn-cellbtn", has_text="Nhận vào (chưa qua NC)").first
            if await vb.count():
                await vb.click(); await pg.wait_for_timeout(900)
                to = m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first
                await to.select_option(label="HTSP"); await pg.wait_for_timeout(900)
                await m.locator("input[type=number]").first.fill("5"); await m.locator("input[type=number]").first.dispatch_event("change"); await pg.wait_for_timeout(500)
                await m.locator("input[maxlength='500']").first.fill("lam H04"); await m.locator("input[maxlength='500']").first.dispatch_event("change"); await pg.wait_for_timeout(400)
                await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").click(); await pg.wait_for_timeout(3000)
                saved = True
        except Exception as e:
            print("   lỗi thao tác giao:", e)
        ok(any(o.startswith("Nhận vào (chưa qua NC)") and "có 8" in o for o in opts), "N21 hàng tốt Gia công có dòng 'Nhận vào (chưa qua NC) · chờ giao làm NC01 (máy loại trừ …)' có 8", opts)
        t = [x for x in txs() if x.get("purpose") == "HELP_SEND" or x.get("fromNC") == "@VAO"]
        ok(saved and len(t) == 1 and t[0].get("fromNC") == "@VAO" and float(t[0]["qtyIssued"]) == 5 and t[0]["toGroupCode"] == "HTSP",
           "N22 bấm Xác nhận → DB 1 phiếu Gia công → HTSP 5 chiếc, FromNC = @VAO", t or txs())
        await close_modal(pg)

        print("[N3] sau khi giao")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
        try:
            await expand(pg); st = await gc_steps(pg)
        except Exception as e:
            st = []
        v = [r for r in st if r and r[0].startswith("Nhận vào (chưa qua NC)")]
        ok(len(v) == 1 and v[0][2] == "8" and v[0][5] == "3" and v[0][6] == "5", "N31 dòng Nhận vào: vào 8 · Chưa GC 3 · Giao đi 5", st)

        print("[N4] Kho → Gia công: ô 'Nhận vào NC' không có @VAO")
        o4 = []; v4 = []
        try:
            await open_issue_from(pg, "Kho")
            m = modal(pg)
            await m.locator("button.gn-cellbtn", has_text="Hàng tốt").first.click(); await pg.wait_for_timeout(700)
            await m.locator("button.gn-cellbtn", has_text="Phôi còn (chưa giao)").first.click(); await pg.wait_for_timeout(700)
            await m.locator("select", has=pg.locator("option", has_text="Chọn nhóm nhận")).first.select_option(label="Gia công"); await pg.wait_for_timeout(900)
            sel = m.locator("select", has=pg.locator("option", has_text="(mặc định)")).first
            o4 = [ (await o.inner_text()).strip() for o in await sel.locator("option").all() ]
            v4 = [ await o.get_attribute("value") for o in await sel.locator("option").all() ]
        except Exception as e:
            print("   lỗi mở Kho → Gia công:", e)
        ok(len(v4) > 0 and "@VAO" not in v4 and "NC02" in v4, "N41 ô Nhận vào NC: có NC02, không có @VAO", list(zip(v4, o4)))
        ok(len(o4) > 0 and o4[0] == "-- Nhận vào (chưa qua NC) (mặc định) --", "N42 dòng mặc định ghi 'Nhận vào (chưa qua NC) (mặc định)'", o4[:1])
        await close_modal(pg)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
