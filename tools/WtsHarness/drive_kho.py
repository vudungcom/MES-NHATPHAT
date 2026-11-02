# [KHO-5][KHO-LEDGER][KHO-ACTIVE][KHO-VIEW][KHO-STALE][KHO-LEN][E4-KHO-NEG][D11-KHO-SL 02/11/2026] Bấm trang Kho thật (Kho_Index.razor).
# Dữ liệu riêng KH-KHO dựng bằng POST /harness/kho-seed (HarnessSeed.SeedKhoAsync — không nằm trong seed khởi động nên không đổi kết quả drive.py):
#   PO-K2 phôi 20, giao GC 12 ngày 27/10 (GC nhận đủ) · PO-K1 phôi 10, giao GC 10 ngày 25/10 (chờ GC nhận) · PO-K3 phôi 15, Kho báo lỗi 5 → gửi GC sửa 5 ngày 20/10
#   (phiếu Hàng lỗi, không phải phôi giao) · PO-K4 chưa có dòng Kho · PO-K5 dòng Kho cũ đã tắt + dòng đang dùng · PO-K6 chỉ dòng đã tắt · PO-K7 phôi 7 (thử sửa).
#   PO-K8 phôi 15, giao GC 5 ngày 28/10, GC nhận cả 5 là NG (GC chưa xác nhận OK chiếc nào — sổ vẫn là Kho đã giao 5).
# Số "đúng" của cột N / O / Còn lại / Trạng thái tính lại ở đây từ phiếu + sổ (/harness/kho), không gọi lại hàm trang dùng.
# Excel: thay window.downloadFileFromBytes để lấy file trang xuất (harness dùng Stubs/XlMini.cs ghi .xlsx thật), đọc bằng zipfile + xml.
# Nút lọc cột chỉ có icon Bootstrap (app lấy từ CDN, harness không có mạng) → trỏ CDN về thư mục Bootstrap tải sẵn KHO_BS
#   (mặc định /home/claude/bs_shared; cách tải: xem đầu drive_ncw.py).
# Chạy trên harness VỪA khởi động (DB mới): python3 drive_kho.py <port> <old|new>  → in ĐẠT / SAI từng bước, ghi kho_<old|new>.json
import sys, os, asyncio, json, io, re, base64, zipfile, urllib.request, xml.etree.ElementTree as ET
from datetime import datetime
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"; BS = os.environ.get("KHO_BS", "/home/claude/bs_shared")
res = []

def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())

def ok(cond, name, info=""):
    res.append({"buoc": name, "dat": bool(cond), "info": str(info)[:500]})
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if info != "" else ""))

def num(s):   # "0.##" của trang → số
    return float(s) if s not in ("", None) else None

# ── số đúng theo phiếu + sổ (độc lập với hàm trang) ──
def expected():
    k = get("/harness/kho"); ex = {}
    for p in k["po"]:
        row = next((r for r in k["kho"] if r["planDetailId"] == p["id"] and r["isActive"]), None)
        moi = [t for t in p["tx"] if t["purpose"] == "NEW" and t["sourceReceiveId"] is None]   # [D11-KHO-SL] chỉ phiếu Hàng mới là "phôi đã giao"
        conf = sum(t["ok"] for t in moi)
        pend = max(0, sum(t["qtyIssued"] - t["ok"] - t["ng"] for t in moi if t["status"] != "COMPLETED"))
        first = min((t["at"] for t in moi), default=None)
        sl = row["soLuongThucNhan"] if row else None
        con = p["conGiao"] if sl is not None else None
        gm = p["giaoMoi"] or 0; cg = p["conGiao"] if p["conGiao"] is not None else (sl or 0)
        nhan = bool(row and row["nhan"])
        st = ("Đã giao GC" if gm > 0 and cg <= 0 and (sl or 0) > 0 else "Giao một phần" if gm > 0 and cg > 0
              else "Sẵn sàng giao" if nhan and (sl or 0) > 0 else "Đã nhận phôi" if nhan else "Chưa có phôi")
        ex[p["purchaseOrder"]] = dict(n=datetime.strptime(first, "%Y-%m-%d").strftime("%d/%m/%Y") if first else "No",
                                     conf=conf, pend=pend, con=con, st=st, dacap=gm > 0 and cg <= 0,
                                     chuacap=nhan and (gm <= 0 or cg > 0), key=conf + pend)
    return ex, k

ROWS = """() => [...document.querySelectorAll('#kho-table tbody tr')].filter(tr => tr.querySelectorAll('td').length > 5).map(tr => {
  const t = [...tr.querySelectorAll('td')].map(td => td.innerText.trim());
  return {po: t[1], slnhan: t[10], ovl: t[11], n: t[14], o: t[15], con: t[16], st: t[18],
          sua: tr.querySelector("button[title='Cập nhật thông tin Kho']")?.disabled}; })"""

def o_split(o):   # cột O trên trang: "12" · "0 (+10⏳)" · "—"
    if o == "—": return None, None
    m = re.match(r"^([\d.]+)(?:\s*\(\+([\d.]+)⏳\))?$", o)
    return (float(m.group(1)), float(m.group(2)) if m.group(2) else 0.0) if m else ("?", o)

def con_num(c):   # Còn lại: "8" · "0⏳10" · "0lỗi 2" · "—"
    if c == "—": return None
    m = re.match(r"^-?[\d.]+", c); return float(m.group(0)) if m else "?"

async def cdn(ctx):   # như drive_ncw.py
    async def h(route):
        u = route.request.url
        f = BS + ("/bootstrap-icons-1.11.3/" + u.split("bootstrap-icons@1.11.3/")[1] if "bootstrap-icons@" in u else "/bootstrap-5.3.3/" + u.split("bootstrap@5.3.3/")[1])
        await (route.fulfill(path=f) if os.path.exists(f) else route.abort())
    await ctx.route("https://cdn.jsdelivr.net/npm/**", h)

async def login(b, user, pw="123456"):
    ctx = await b.new_context(viewport={"width": 1800, "height": 1100}); await cdn(ctx); pg = await ctx.new_page()
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg

async def open_kho(pg):
    await pg.goto(BASE + "/kho"); await pg.wait_for_timeout(2500)

async def rows(pg): return await pg.evaluate(ROWS)
def pos(rs): return [r["po"] for r in rs]
def by(rs, po): return next((r for r in rs if r["po"] == po), None)

async def status_filter(pg, v):
    await pg.select_option("select.form-select-sm", v); await pg.wait_for_timeout(600)

async def reset(pg):
    btn = pg.locator("button:has-text('Xóa filter cột')")
    if await btn.is_visible(): await btn.click(); await pg.wait_for_timeout(500)

COL = {"Ngày giao GC": "N", "SL đã giao": "O"}
def th(pg, label):   # ô tiêu đề cột theo chữ "(Cột X)"
    return pg.locator(f"#kho-table thead th:has(small:text-is('(Cột {COL[label]})'))").first

async def col_values(pg, label):
    await th(pg, label).locator("button.col-filter-btn").click(); await pg.wait_for_timeout(500)
    return await pg.evaluate("() => [...document.querySelectorAll('.col-filter-dropdown .value-list label')].slice(1).map(l => l.innerText.trim())")

async def pick_only(pg, value):   # bỏ chọn tất cả, chỉ chọn 1 giá trị, OK
    await pg.locator(".col-filter-dropdown .value-list label").first.locator("input").click(); await pg.wait_for_timeout(300)
    lab = pg.locator(".col-filter-dropdown .value-list label", has_text=value)
    for i in range(await lab.count()):
        if (await lab.nth(i).inner_text()).strip() == value:
            await lab.nth(i).locator("input").click(); break
    await pg.wait_for_timeout(300)
    await pg.click(".col-filter-dropdown button:has-text('OK')"); await pg.wait_for_timeout(500)

async def export_xlsx(pg):
    await pg.evaluate("() => { window.__xl = null; window.downloadFileFromBytes = (n, t, b) => { window.__xl = b; }; }")
    await pg.click("button:has-text('Xuất Excel')"); await pg.wait_for_timeout(1500)
    b64 = await pg.evaluate("() => window.__xl")
    if not b64: return None
    z = zipfile.ZipFile(io.BytesIO(base64.b64decode(b64)))
    ns = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
    cells = {}
    for c in ET.fromstring(z.read("xl/worksheets/sheet1.xml")).iter("{%s}c" % ns["m"]):
        if c.get("t") == "inlineStr": v = "".join(t.text or "" for t in c.iter("{%s}t" % ns["m"]))
        elif c.get("t") == "s": v = "?shared"
        else: v = float(c.find("m:v", ns).text)
        cells[c.get("r")] = v
    return cells

async def open_edit(pg, po):
    await pg.locator(f"#kho-table tr:has(td[title='{po}']) button[title='Cập nhật thông tin Kho']").click(); await pg.wait_for_timeout(600)

async def set_val(pg, sel, val):   # gán thẳng giá trị (bỏ qua maxlength của form) + change như người gõ xong
    await pg.locator(".modal-content " + sel).first.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('change', {bubbles: true})); }", val)
    await pg.wait_for_timeout(200)

async def save_modal(pg, reason=None, pin=None):
    if reason is not None: await set_val(pg, "input[placeholder^='Nhập lý do cập nhật']", reason)
    if pin is not None: await set_val(pg, "input[type=password]", pin)
    await pg.click(".modal-content button:has-text('Lưu dữ liệu kho')"); await pg.wait_for_timeout(1500)
    err = pg.locator(".modal-content .alert-danger")
    return (await err.first.inner_text()).strip() if await err.count() else None, await pg.locator(".modal-content").count() > 0

def khorow(po):
    k = get("/harness/kho"); pid = next(p["id"] for p in k["po"] if p["purchaseOrder"] == po)
    act = [r for r in k["kho"] if r["planDetailId"] == pid and r["isActive"]]
    ids = {r["khoVatLieuId"] for r in act}
    return act, [l for l in k["kholog"] if l["khoVatLieuId"] in ids]

async def close_modal(pg):
    if await pg.locator(".modal-content button:has-text('Hủy')").count():
        await pg.click(".modal-content button:has-text('Hủy')"); await pg.wait_for_timeout(300)

async def ensure_edit(pg, po):   # đột biến có thể đã lưu + đóng cửa sổ → mở lại để bước sau vẫn chạy
    if not await pg.locator(".modal-content").count():
        await open_kho(pg); await open_edit(pg, po)

async def crashed(pg):
    return await pg.evaluate("() => { const e = document.getElementById('blazor-error-ui'); return !!e && getComputedStyle(e).display !== 'none'; }")

async def main():
    if not os.path.exists(BS + "/bootstrap-icons-1.11.3/font/bootstrap-icons.min.css"):
        print("THIẾU Bootstrap ở " + BS + " — xem đầu file"); sys.exit(2)
    seed = post("/harness/kho-seed")
    ex, k0 = expected()
    npo = len(k0["po"])
    async with async_playwright() as p:
        b = await p.chromium.launch()

        print("[1] KHO-VIEW — trang Kho cần quyền Xem KHO (ma trận)")
        g = await login(b, "gc01"); await open_kho(g)
        ok(not g.url.rstrip("/").endswith("/kho") and await g.locator("#kho-table").count() == 0,
           "K1a gc01 (không có quyền Xem KHO) mở /kho → về trang chủ, không thấy bảng Kho", g.url)
        x = await login(b, "khoxem01"); await open_kho(x); rx = await rows(x)
        ok(x.url.endswith("/kho") and len(rx) == npo and "Quyền: Chỉ xem" in await x.inner_text("body") and all(r["sua"] for r in rx),
           "K1b khoxem01 (chỉ Xem KHO) xem được đủ dòng, nhãn 'Quyền: Chỉ xem', nút sửa khoá", f"url={x.url} dòng={len(rx)}")
        A = await login(b, "khoa01"); await open_kho(A); ra = await rows(A)
        ok(A.url.endswith("/kho") and "Quyền: Nhập & Cấp Kho" in await A.inner_text("body") and not any(r["sua"] for r in ra),
           "K1c khoa01 (Xem + Sửa KHO) mở được, nhãn 'Quyền: Nhập & Cấp Kho', nút sửa mở", A.url)

        print("[2] KHO-ACTIVE — mỗi PO 1 dòng, chỉ đọc dòng Kho đang dùng")
        ok(len(ra) == npo and sorted(pos(ra)) == sorted(p["purchaseOrder"] for p in k0["po"]),
           f"K2a bảng có đúng {npo} dòng, mỗi PO 1 dòng (dòng Kho đã tắt không nhân đôi PO)", pos(ra))
        r5 = by(ra, "PO-K5")
        ok(r5 and r5["ovl"] == "OVL-K5" and r5["slnhan"] == "6", "K2b PO-K5 hiện dòng đang dùng (OVL-K5, 6), không phải dòng đã tắt (OVL-CU, 99)", r5)
        r6 = by(ra, "PO-K6")
        ok(r6 and r6["ovl"] == "—" and r6["slnhan"] == "—" and r6["st"] == "Chưa có phôi", "K2c PO-K6 chỉ có dòng đã tắt → trống, 'Chưa có phôi'", r6)

        print("[3] KHO-5 / D11-KHO-SL — cột N / O / Còn lại đọc sổ Giao nhận")
        r1, r2, r3 = by(ra, "PO-K1"), by(ra, "PO-K2"), by(ra, "PO-K3")
        ok(r1 and r1["n"] == "25/10/2026" and o_split(r1["o"]) == (0, 10) and con_num(r1["con"]) == 0 and "⏳10" in r1["con"],
           "K3a PO-K1 giao 10 chờ GC nhận: N 25/10/2026 · O 0 (+10 chờ) · Còn lại 0 ⏳10", r1)
        ok(r2 and r2["n"] == "27/10/2026" and r2["o"] == "12" and r2["con"] == "8", "K3b PO-K2 GC nhận 12: N 27/10/2026 · O 12 · Còn lại 8", r2)
        ok(r3 and r3["n"] == "No" and r3["o"] == "—" and r3["con"] == "10",
           "K4 D11 PO-K3 Kho gửi GC sửa 5 phôi lỗi (phiếu Hàng lỗi): không tính là phôi đã giao — N No · O — · Còn lại 10", r3)
        bad = []
        for r in ra:
            e = ex[r["po"]]
            if r["n"] != e["n"] or o_split(r["o"]) != ((e["conf"], e["pend"]) if e["conf"] or e["pend"] else (None, None)) or con_num(r["con"]) != e["con"]:
                bad.append((r["po"], r["n"], r["o"], r["con"], e))
        ok(not bad, f"K5 cả {len(ra)} dòng: N = ngày phiếu Hàng mới đầu, O = GC đã nhận OK (+ chờ nhận), Còn lại = Phôi còn giao của sổ", bad)

        print("[4] KHO-LEDGER — trạng thái + lọc Đã giao hết / Chưa giao hết theo sổ (phiếu chờ nhận cũng là đã giao)")
        ok(r1 and r1["st"] == "Đã giao GC" and r2["st"] == "Giao một phần" and r3["st"] == "Sẵn sàng giao",
           "K6a PO-K1 (10 chờ GC nhận) 'Đã giao GC' · PO-K2 'Giao một phần' · PO-K3 'Sẵn sàng giao'", (r1 and r1["st"], r2 and r2["st"], r3 and r3["st"]))
        r8 = by(ra, "PO-K8")
        ok(r8 and r8["st"] == "Giao một phần" and r8["con"].startswith("10"),
           "K6c PO-K8 (giao 5, GC nhận cả 5 là NG): 'Giao một phần', Còn lại 10 — không coi là chưa giao (trước: chỉ đếm số GC xác nhận OK)", r8)
        bad = [(r["po"], r["st"], ex[r["po"]]["st"]) for r in ra if r["st"] != ex[r["po"]]["st"]]
        ok(not bad, f"K6b trạng thái cả {len(ra)} dòng theo sổ", bad)
        await status_filter(A, "DaCap"); rf = await rows(A)
        want = sorted(po for po, e in ex.items() if e["dacap"])
        ok(sorted(pos(rf)) == want and "PO-K1" in want, "K7 lọc 'Đã giao hết cho GC' = đúng các PO sổ báo Phôi còn giao ≤ 0 (có PO-K1 chờ nhận)", (pos(rf), want))
        await status_filter(A, "ChuaCap"); rf = await rows(A)
        want = sorted(po for po, e in ex.items() if e["chuacap"])
        ok(sorted(pos(rf)) == want and "PO-K1" not in want and {"PO-K2", "PO-K3", "PO-K8"} <= set(want),
           "K8 lọc 'Đã nhận nhưng chưa giao hết' = PO đã nhận phôi còn phôi tốt chưa giao (PO-K2, PO-K3, PO-K8; không PO-K1)", (pos(rf), want))
        await status_filter(A, "")

        print("[5] KHO-5 — lọc cột N / O theo đúng chữ trên ô, sắp xếp theo số sổ")
        vals = await col_values(A, "SL đã giao")
        norm = lambda s: re.sub(r"\s*\(\+([\d.]+)(?:⏳| chờ nhận)\)", r" (+\1)", s)
        ok(sorted(set(map(norm, vals))) == sorted(set(norm(r["o"]) for r in ra)),
           "K9a danh sách lọc cột O = các số đang hiện trên cột O (so phần số)", (vals, sorted(set(r["o"] for r in ra))))
        await pick_only(A, "12"); rf = await rows(A)
        ok(rf and all(r["o"] == "12" for r in rf) and "PO-K2" in pos(rf) and len(rf) == sum(r["o"] == "12" for r in ra),
           "K9b chọn '12' ở cột O → chỉ còn các dòng O = 12 (có PO-K2)", pos(rf))
        await reset(A)
        vals = await col_values(A, "Ngày giao GC")
        ok(sorted(set(vals)) == sorted(set(r["n"] for r in ra)), "K10a danh sách lọc cột N = các ngày đang hiện trên cột N", vals)
        await pick_only(A, "27/10/2026"); rf = await rows(A)
        ok(rf and all(r["n"] == "27/10/2026" for r in rf) and "PO-K2" in pos(rf), "K10b chọn '27/10/2026' ở cột N → chỉ PO giao ngày đó (PO-K2)", pos(rf))
        await reset(A)
        await th(A, "Ngày giao GC").locator("span[style*='cursor']").first.click(); await A.wait_for_timeout(500); rs = await rows(A)
        keys = [datetime.strptime(r["n"], "%d/%m/%Y") if r["n"] != "No" else datetime(9999, 1, 1) for r in rs]
        ok(keys == sorted(keys) and pos(rs).index("PO-K1") < pos(rs).index("PO-K2") < pos(rs).index("PO-K3"),
           "K11 sắp xếp cột N tăng dần theo ngày trên ô ('No' cuối): PO-K1 (25/10) trước PO-K2 (27/10), PO-K3 (No) sau", [r["n"] for r in rs])
        await th(A, "SL đã giao").locator("span[style*='cursor']").first.click(); await A.wait_for_timeout(500); rs = await rows(A)
        keys = [ex[r["po"]]["key"] for r in rs]
        ok(keys == sorted(keys) and pos(rs).index("PO-K3") < pos(rs).index("PO-K1") < pos(rs).index("PO-K2"),
           "K12 sắp xếp cột O tăng dần theo số đã giao + chờ nhận: PO-K3 (0) < PO-K1 (10) < PO-K2 (12)", list(zip(pos(rs), keys)))

        print("[6] KHO-5 — Excel xuất cùng chữ + cùng số với bảng (đọc file .xlsx)")
        xl = await export_xlsx(A)
        ok(xl is not None, "K13a bấm Xuất Excel ra file .xlsx", "")
        if xl:
            ok([xl.get(c + "2") for c in "NOPQ"] == ["Ngày giao GC", "SL đã giao", "Chờ GC nhận", "Còn lại"], "K13b tiêu đề N / O / P / Q = Ngày giao GC · SL đã giao · Chờ GC nhận · Còn lại",
               [xl.get(c + "2") for c in "NOPQ"])
            xpos = [xl.get(f"A{i}") for i in range(3, 3 + len(rs) + 2)]
            ok(xpos[:len(rs)] == pos(rs) and xpos[len(rs)] is None, "K13c Excel đủ dòng, đúng thứ tự đang sắp trên trang", (xpos, pos(rs)))
            bad = []
            for i, r in enumerate(rs):
                c, pd = o_split(r["o"])
                got = (xl.get(f"N{i+3}"), xl.get(f"O{i+3}"), xl.get(f"P{i+3}"), xl.get(f"Q{i+3}"))
                want = (r["n"], c, (pd or None) if c is not None else None, con_num(r["con"]))
                if got != want: bad.append((r["po"], got, want))
            ok(not bad, f"K13d cả {len(rs)} dòng: Excel N / O / P / Q = chữ / số trên trang (N, O, chờ nhận, Còn lại)", bad)
            i1 = pos(rs).index("PO-K1") + 3; i3 = pos(rs).index("PO-K3") + 3
            ok((xl.get(f"N{i1}"), xl.get(f"O{i1}"), xl.get(f"P{i1}"), xl.get(f"Q{i1}")) == ("25/10/2026", 0, 10, 0)
               and (xl.get(f"N{i3}"), xl.get(f"O{i3}"), xl.get(f"P{i3}"), xl.get(f"Q{i3}")) == ("No", None, None, 10),
               "K13e PO-K1: 25/10/2026 · 0 · 10 · 0 ; PO-K3: No · trống · trống · 10", "")
        await reset(A); await status_filter(A, "DaCap"); rf = await rows(A)
        xl = await export_xlsx(A)
        xpos = [xl.get(f"A{i}") for i in range(3, 3 + len(rf) + 1)] if xl else None
        ok(xl and xpos[:len(rf)] == pos(rf) and xpos[len(rf)] is None, "K14 lọc 'Đã giao hết' rồi xuất: Excel đúng các dòng đang lọc", (xpos, pos(rf)))
        await status_filter(A, "")

        print("[7] KHO-STALE — 2 người cùng mở PO-K4 chưa có dòng Kho (nhập lần đầu); B lưu trước, A lưu sau → A bị chặn")
        B = await login(b, "khob01"); await open_kho(B); await open_kho(A)
        await open_edit(A, "PO-K4"); await open_edit(B, "PO-K4")
        first = "Nhập lần đầu" in await A.locator(".modal-content").inner_text()
        await set_val(B, "input[type=number]", "8"); await set_val(B, "input[placeholder^='VD: 0003488452']", "OVL-B")
        eb, openb = await save_modal(B)
        await set_val(A, "input[type=number]", "5"); await set_val(A, "input[placeholder^='VD: 0003488452']", "OVL-A")
        ea, opena = await save_modal(A)
        act, logs = khorow("PO-K4")
        ok(first and eb is None and not openb, "K15a cửa sổ hiện 'Nhập lần đầu'; B lưu được, cửa sổ đóng", eb)
        ok(ea is not None and "vừa được người khác nhập" in ea and opena, "K15b A (trang mở lúc dòng còn trống) bị báo 'vừa được người khác nhập', cửa sổ còn mở", ea)
        ok(len(act) == 1 and act[0]["soLuongThucNhan"] == 8 and act[0]["orderVatLieu"] == "OVL-B" and len(logs) == 1,
           "K15c DB giữ số của B (8, OVL-B), 1 dòng Kho, lịch sử chỉ 1 lần 'Khởi tạo Kho'", (act, logs))
        await close_modal(A); await open_kho(A); await open_edit(A, "PO-K4")
        txt = await A.locator(".modal-content").inner_text()
        ok("Nhập lần đầu" not in txt and "Lý do thay đổi" in txt, "K15d A tải lại trang → sửa PO-K4 phải có lý do + xác nhận", "")
        await close_modal(A)

        print("[8] KHO-LEN / E4-KHO-NEG — sửa PO-K7: chữ dài quá cột DB, số âm → máy chủ chặn, báo trong cửa sổ, trang không sập")
        await open_edit(A, "PO-K7")
        await set_val(A, "input[placeholder^='VD: 0003488452']", "X" * 101)
        e, still = await save_modal(A, "kiem do dai", "123456")
        act, logs = khorow("PO-K7")
        ok(e is not None and "Order vật liệu dài quá 100 ký tự" in e and still and not await crashed(A) and act[0]["orderVatLieu"] == "OVL-K7" and not logs,
           "K16a Order VL 101 ký tự (vượt maxlength form) → báo 'dài quá 100 ký tự' trong cửa sổ, DB giữ OVL-K7, không ghi lịch sử", (e, act[0]["orderVatLieu"], len(logs)))
        await ensure_edit(A, "PO-K7")
        await set_val(A, "input[placeholder^='VD: 0003488452']", "OVL-K7")
        await set_val(A, "input[placeholder^='VD: C6']", "V" * 101)
        e, still = await save_modal(A, "kiem do dai", "123456")
        act, _ = khorow("PO-K7")
        ok(e is not None and "Vị trí để phôi dài quá 100 ký tự" in e and act[0]["viTriDePhoi"] == "K-7", "K16b Vị trí 101 ký tự → chặn, DB giữ K-7", e)
        await ensure_edit(A, "PO-K7")
        await set_val(A, "input[placeholder^='VD: C6']", "K-7")
        await set_val(A, "input[placeholder^='VD: OK, NG']", "T" * 51)
        e, still = await save_modal(A, "kiem do dai", "123456")
        act, _ = khorow("PO-K7")
        ok(e is not None and "Tình trạng phôi dài quá 50 ký tự" in e and act[0]["tinhTrangPhoi"] is None, "K16c Tình trạng 51 ký tự → chặn, DB không đổi", e)
        await ensure_edit(A, "PO-K7")
        await set_val(A, "input[placeholder^='VD: OK, NG']", "")
        await set_val(A, "input[type=number]", "-3")
        e, still = await save_modal(A, "kiem so am", "123456")
        act, logs = khorow("PO-K7")
        ok(e is not None and "Số lượng không được âm" in e and still and act[0]["soLuongThucNhan"] == 7 and not logs,
           "K17 E4 SL nhận −3 → máy chủ báo 'Số lượng không được âm', DB giữ 7", (e, act[0]["soLuongThucNhan"]))
        await ensure_edit(A, "PO-K7")
        await set_val(A, "input[type=number]", "7")
        await set_val(A, "input[placeholder^='VD: 0003488452']", "Y" * 100)
        e, still = await save_modal(A, "dung 100 ky tu", "123456")
        act, logs = khorow("PO-K7")
        ok(e is None and not still and act[0]["orderVatLieu"] == "Y" * 100 and len(logs) == 1,
           "K16d Order VL đúng 100 ký tự → lưu được, 1 dòng lịch sử", (e, len(act[0]["orderVatLieu"] or ""), logs))
        await b.close()
    json.dump(res, open(f"kho_{OUT}.json", "w"), ensure_ascii=False, indent=1)
    nf = sum(not r["dat"] for r in res)
    print(f"\nKẾT QUẢ drive_kho {OUT}: " + ("ĐẠT hết" if nf == 0 else f"{nf} SAI"))

try:
    asyncio.run(main())
except Exception as e:
    ok(False, "LỖI CHẠY (dừng giữa chừng)", repr(e)[:300])
    json.dump(res, open(f"kho_{OUT}.json", "w"), ensure_ascii=False, indent=1)
    print(f"\nKẾT QUẢ drive_kho {OUT}: {sum(not r['dat'] for r in res)} SAI (dừng giữa chừng)")
