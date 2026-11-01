# [WTS-PANEL-QTY 31/10] "Cùng số mọi màn hình": mọi PO seed — số trên bảng quy trình trang WTS = tab KH (xem chi tiết)
# = khung "Đang ở đâu" = sổ chung (HandoverLedgerService), và sổ nạp theo 3 đường (1 PO / danh sách Giao nhận / danh sách KH) giống nhau.
# Thêm: sau khi LƯU / XOÁ dòng WTS, bảng quy trình phải tự cập nhật (không cần chọn lại PO).
# Chạy SAU drive.py trên cùng harness (DB đã có dòng WTS của 23 bước) hoặc trên harness mới tinh:
#   python3 drive_cungso.py 5101
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; BASE = f"http://127.0.0.1:{PORT}"
fails = []; checks = 0

def _P(o):   # JSON máy chủ trả camelCase → đổi chữ đầu khoá thành hoa cho khớp tên C#
    if isinstance(o, dict): return {k[:1].upper() + k[1:]: _P(v) for k, v in o.items()}
    if isinstance(o, list): return [_P(v) for v in o]
    return o
def get(path): return _P(json.loads(urllib.request.urlopen(BASE + path).read().decode()))
def fmt(x):
    if x is None: x = 0
    s = ('%.2f' % float(x)).rstrip('0').rstrip('.')
    return '0' if s in ('-0', '') else s
def ok(cond, what):
    global checks; checks += 1
    if not cond: fails.append(what); print("  SAI:", what)

def exp_text(st):
    if st['IsPrep']: return 'chuẩn bị'
    t = f"OK {fmt(st['Ok'])} / vào {fmt(st['In'])}"
    if st['Ng'] > 0: t += f" · NG {fmt(st['Ng'])}"
    return t
def exp_color(st):
    if st is None or st['IsPrep'] or (st['In'] or 0) <= 0: return 'secondary'
    if (st['Waiting'] or 0) < 0: return 'danger'
    if st['Ok'] <= 0: return 'danger'
    return 'success' if (st['Waiting'] or 0) <= 0 else 'warning'

def steps_of(A):
    m = {}
    for g in A['Groups'].values():
        for s in g['Steps']: m[s['Code'].upper()] = s
    return m

# Đọc mọi badge số lượng + dòng WTS dưới nó trên trang hiện tại
JS = r"""
() => Array.from(document.querySelectorAll('span[data-q=badge]')).map(b => {
  const td = b.closest('td'), tr = b.closest('tr'); const R = td.rowSpan || 1;
  const rows = [tr]; let n = tr; for (let i = 1; i < R; i++) { n = n.nextElementSibling; if (!n) break; rows.push(n); }
  const codes = Array.from(tr.querySelectorAll('code')).map(c => ({t: c.innerText.trim(), g: c.classList.contains('text-success')}));
  let code = (codes.find(c => c.g && c.t !== '—') || codes[0] || {t: ''}).t;
  const logs = [];
  rows.forEach(r => { const o = r.querySelector('[data-q=ok]'), g = r.querySelector('[data-q=ng]'); if (o) logs.push([o.innerText.trim(), g ? g.innerText.trim() : '—']); });
  return { code, text: b.innerText.trim(), cls: b.className, title: b.title, logs };
})
"""

def num(t): return 0.0 if t in ('—', '', None) else float(t)

def check_page(where, po, badges, A):
    st = steps_of(A)
    seen = set(); sums = {}
    for b in badges:
        code = b['code'].upper(); seen.add(code)
        s = st.get(code)
        if s is None:
            # dự phòng: không có dòng riêng trong sổ → "gộp <mã chính>" (mã chính phải có trong sổ)
            m = re.fullmatch(r'gộp (\S+)', b['text'])
            ok(m is not None and m.group(1).upper() in st, f"{where} {po} {code}: dòng không có trong sổ phải hiện 'gộp <mã chính>', đang '{b['text']}'")
            if m:
                k = m.group(1).upper(); o, g = sums.get(k, (0, 0))
                sums[k] = (o + sum(num(x[0]) for x in b['logs']), g + sum(num(x[1]) for x in b['logs']))
            continue
        ok(b['text'] == exp_text(s), f"{where} {po} {code}: badge '{b['text']}' ≠ sổ '{exp_text(s)}'")
        ok(('bg-' + exp_color(s)) in b['cls'], f"{where} {po} {code}: màu {b['cls']} ≠ sổ bg-{exp_color(s)}")
        if not s['IsPrep']:
            o, g = sums.get(code, (0, 0))
            sums[code] = (o + sum(num(x[0]) for x in b['logs']), g + sum(num(x[1]) for x in b['logs']))
        else:
            ok(all(x == ['—', '—'] for x in b['logs']), f"{where} {po} {code}: mã chuẩn bị phải hiện OK/NG '—', đang {b['logs']}")
    for code, (o, g) in sums.items():
        s = st[code]
        ok(abs(o - s['Ok']) < 1e-9 and abs(g - s['Ng']) < 1e-9,
           f"{where} {po} {code}: cộng OK/NG các dòng WTS hiện trên trang = {fmt(o)}/{fmt(g)} ≠ sổ {fmt(s['Ok'])}/{fmt(s['Ng'])}")
    for code, s in st.items():
        if s['Pg'] and not s['IsExcluded']:
            ok(code in seen, f"{where} {po} {code}: có trong sổ nhưng trang không hiện badge")

async def login(ctx, user, pw, url):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + url); await pg.wait_for_timeout(1500)
    return pg

async def wts_panel(pg, po):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
    inp = pg.locator("input[placeholder='Gõ PO...']").first
    await inp.fill(po); await inp.press("Tab"); await pg.wait_for_timeout(1500)
    return await pg.evaluate(JS)

async def main():
    led = get("/harness/ledger")
    print(f"sổ: {len(led)} PO")
    for d in led:
        ok(json.dumps(d['A'], sort_keys=True) == json.dumps(d['B'], sort_keys=True), f"{d['Po']}: sổ 1 PO (A) ≠ danh sách Giao nhận (B)")
        ok(json.dumps(d['A'], sort_keys=True) == json.dumps(d['C'], sort_keys=True), f"{d['Po']}: sổ 1 PO (A) ≠ danh sách Kế hoạch (C)")
    pos = [d for d in led if d['A'] and any(g['Steps'] for g in d['A']['Groups'].values())]
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context()
        # ── tab KH (xem chi tiết) + Đang ở đâu ──
        pg = await login(ctx, "admin", "admin123", "/")
        for d in pos:
            await pg.goto(BASE + f"/kh-plan/{d['PlanId']}/detail/{d['Id']}"); await pg.wait_for_timeout(1500)
            bs = await pg.evaluate(JS)
            check_page("tab KH", d['Po'], bs, d['A'])
            pv = await pg.locator(".poviti").first.inner_text()
            w = d['A']['Where']
            ok(f"Phôi nhận {fmt(w['Phoi'])}" in pv.replace('\n', ' '), f"Đang ở đâu {d['Po']}: thiếu 'Phôi nhận {fmt(w['Phoi'])}' — {pv[:120]!r}")
            ok(("cân:" in pv) == bool(w['Balanced']), f"Đang ở đâu {d['Po']}: cân/lệch khác sổ")
            ok(("có ô âm" in pv) == bool(w['HasNegative']), f"Đang ở đâu {d['Po']}: báo ô âm khác sổ (sổ {w['HasNegative']})")
            print(f"tab KH {d['Po']}: {len(bs)} badge")
        await pg.close()
        # ── trang WTS: bảng quy trình bên phải ──
        pg = await login(ctx, "admin", "admin123", "/hieu-suat-cong-nhan")
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        for d in pos:
            bs = await wts_panel(pg, d['Po'])
            check_page("trang WTS", d['Po'], bs, d['A'])
            print(f"trang WTS {d['Po']}: {len(bs)} badge")
        await pg.close()
        # ── lưu / xoá dòng → bảng quy trình tự cập nhật (PO-CS Bavia 3V1) ──
        pg = await login(ctx, "htsp01", "123456", "/hieu-suat-cong-nhan")
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
        tb = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
        await tb.locator("select").nth(0).select_option("Bavia"); await pg.wait_for_timeout(500)
        row = tb.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
        i = row.locator("input[placeholder='Gõ PO...']"); await i.fill("PO-CS"); await i.press("Tab"); await pg.wait_for_timeout(1200)
        cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill("3V1"); await cv.dispatch_event("change"); await pg.wait_for_timeout(500)
        await row.locator("input[type=number]").nth(0).fill("2"); await pg.wait_for_timeout(200)
        t = row.locator("input[placeholder='HH:mm']")
        await t.nth(0).fill("14:00"); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
        await t.nth(1).fill("14:10"); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        errs = [e.strip() for e in await pg.locator(".alert-danger").all_inner_texts() if e.strip()]
        ok(not errs, f"lưu PO-CS 3V1 OK 2: lỗi {errs}")
        # bảng quy trình vẫn hiện PO-CS (PO dòng vừa lưu) — badge 3V1 phải là số mới, không chọn lại PO
        A = [d for d in get("/harness/ledger") if d['Po'] == 'PO-CS'][0]['A']
        bs = await pg.evaluate(JS)
        b31 = [x for x in bs if x['code'].upper() == '3V1']
        ok(len(b31) == 1 and b31[0]['text'] == exp_text(steps_of(A)['3V1']), f"sau khi lưu: badge 3V1 = {b31[0]['text'] if b31 else None} ≠ sổ {exp_text(steps_of(A)['3V1'])}")
        ok(len(b31) == 1 and ['2', '—'] in b31[0]['logs'], f"sau khi lưu: dòng WTS mới (OK 2) chưa hiện ở bảng quy trình: {b31[0]['logs'] if b31 else None}")
        check_page("trang WTS sau lưu", "PO-CS", bs, A)
        # dòng đã lưu: cột OK / NG / SL PO hiện số đã lưu
        sr = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']")).filter(has_text="PO-CS").filter(has_text="3V1").first
        txt = await sr.inner_text()
        ok("2" in txt and "16" in txt, f"dòng đã lưu PO-CS 3V1 thiếu OK 2 / SL PO 16: {txt!r}")
        # xoá dòng vừa lưu → bảng quy trình về số cũ
        await sr.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(500)
        await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        A2 = [d for d in get("/harness/ledger") if d['Po'] == 'PO-CS'][0]['A']
        ok(steps_of(A2)['3V1']['Ok'] == steps_of(A)['3V1']['Ok'] - 2, "sau khi xoá: sổ 3V1 không giảm 2")
        bs = await pg.evaluate(JS)
        if bs:
            b31 = [x for x in bs if x['code'].upper() == '3V1']
            ok(len(b31) == 1 and b31[0]['text'] == exp_text(steps_of(A2)['3V1']), f"sau khi xoá: badge 3V1 = {b31[0]['text'] if b31 else None} ≠ sổ {exp_text(steps_of(A2)['3V1'])}")
            check_page("trang WTS sau xoá", "PO-CS", bs, A2)
        else:
            print("  (sau khi xoá không còn PO trên form → bảng quy trình ẩn; mở lại để kiểm)")
            bs = await wts_panel(pg, "PO-CS"); check_page("trang WTS sau xoá (mở lại)", "PO-CS", bs, A2)
        await pg.screenshot(path="cungso_htsp.png", full_page=True)
        await b.close()
    print(f"\n{checks} phép kiểm · SAI {len(fails)}")
    sys.exit(1 if fails else 0)

asyncio.run(main())
