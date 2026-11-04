# [KIEM-CHUA 03/11/2026] Trang WTS thật (harness EF giả) — cột "Dự kiến", T.Thực tế mặc định, ô T.Chuẩn xác nhận, bỏ PO:
#   [SERIAL-EST-ACTUAL]  Hàng loạt: đã nhập ≥ 1 ô (T.Gá / T.GC / T.Ktra) → chỉ tính bằng ô thực tế (ô trống = 0); cả 3 trống → theo chuẩn
#   [TRIAL-EST-ACTUAL]   Chạy thử: đã nhập T.CB hoặc T.Chạy → ô còn lại = 0; cả 2 trống → theo chuẩn; 1 lần, không nhân SL
#   [FIX-ACTUAL-EST]     không có chuẩn (chưa chọn máy) mà đã nhập thực tế → vẫn tính Dự kiến; không chuẩn + không thực tế → không hiện
#   [FIX-EST-CONV]       Dự kiến nhảy qua giờ nghỉ ca (Ca 1 nghỉ 12:00–13:00): đúng mốc 12:00, qua nghỉ, bắt đầu trong giờ nghỉ
#   [STD-ACTUAL-DEFAULT] Non-GC bật "Tham khảo chuẩn": T.Thực tế/pc = T.Chuẩn/pc của mã vừa chọn (Taro / Bavia / Rửa / KCS / Đóng gói)
#   [STD-CONFIRM-ROW]    ô T.Chuẩn non-GC chỉ xanh khi ĐÚNG dòng chuẩn đó đã tích xác nhận trong snapshot (T1 có, 1T / B1 không)
#   [FIX-PO-CLEAR]       xoá PO của dòng đang có KH → xoá NC / máy / SL / giờ thực tế / mã; giữ giờ bắt đầu / kết thúc; nhóm hết KH → PO lọc lại đủ
# Đáp án Dự kiến tính độc lập trong file này (đi từng phút, bỏ 12:00–13:00), không đọc code trang.
# Chạy trên harness VỪA khởi động: python3 drive_est.py <port> <old|new>
import sys, asyncio, json, urllib.request, datetime as dt
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info not in (None, "") else ""))

BREAKS = [((12, 0), (13, 0))]
def expect(start, minutes):
    """Đáp án độc lập: đi từng phút từ giờ bắt đầu, phút nằm trong giờ nghỉ không tính."""
    t = dt.datetime(2026, 1, 1, *map(int, start.split(":"))); left = minutes
    def in_break(x): return any(dt.time(*a) <= x.time() < dt.time(*b) for a, b in BREAKS)
    while left > 0:
        if not in_break(t): left -= 1
        t += dt.timedelta(minutes=1)
    return t.strftime("%H:%M")

async def login(b, user, pw):
    pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2500)
    await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg
def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
def subrow(pg, i=0): return grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(i)
async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)
async def new_group(pg):
    await remove_groups(pg); await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(600)
async def set_use_std(pg, on):
    cb = pg.locator("input[id^='grpStd_']").first
    if await cb.is_checked() != on: await cb.click(); await pg.wait_for_timeout(500)
async def set_po(row, po):
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await row.page.wait_for_timeout(900)
async def set_num(row, i, v):
    n = row.locator("input[type=number]").nth(i); await n.fill("" if v is None else str(v)); await n.dispatch_event("change"); await row.page.wait_for_timeout(300)
async def get_num(row, i): return (await row.locator("input[type=number]").nth(i).input_value()).strip()
async def set_times(row, s, e):
    t = row.locator("input[placeholder='HH:mm']")
    await t.nth(0).fill(s); await t.nth(0).dispatch_event("change"); await row.page.wait_for_timeout(300)
    await t.nth(1).fill(e); await t.nth(1).dispatch_event("change"); await row.page.wait_for_timeout(300)
async def get_times(row):
    t = row.locator("input[placeholder='HH:mm']"); return [(await t.nth(i).input_value()).strip() for i in range(2)]
async def est(row):
    # ô Dự kiến = <span style="…font-weight:600…">HH:mm</span> (không có → "")
    return await row.evaluate("""r => { for (const s of r.querySelectorAll('span')) { const st = s.getAttribute('style') || '';
        if (st.includes('font-weight:600') && /^\\d\\d:\\d\\d$/.test(s.textContent.trim())) return s.textContent.trim(); } return ''; }""")

async def gc_row(pg, po="PO1", nc="NC1", m="M1", okq=10, start="08:00", end="16:00"):
    row = subrow(pg); await set_po(row, po)
    if nc: await row.locator("select").nth(0).select_option(nc); await pg.wait_for_timeout(400)
    if m: await row.locator("select").nth(1).select_option(m); await pg.wait_for_timeout(400)
    await set_num(row, 0, okq); await set_times(row, start, end)
    return row

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch(); pg = await login(b, "admin", "admin123")
        # GC: số thực tế ô 2/3/4 = T.Gá / T.GC / T.Ktra, ô 5/6 = T.CB / T.Chạy; chuẩn PO1 NC1 M1 = 5 / 2 / 1, CB 10, Chạy 3
        print("[S] Hàng loạt [SERIAL-EST-ACTUAL]")
        await new_group(pg); await set_use_std(pg, False); row = await gc_row(pg)
        e = await est(row); ok(e == expect("08:00", (5 + 2) * 10), "S1 cả 3 ô trống → Dự kiến theo chuẩn (5 + max(2,1)) × 10 = 70 ph", e)
        await set_num(row, 2, 3)
        e = await est(row); ok(e == expect("08:00", 3 * 10), "S2 [SERIAL-EST-ACTUAL] chỉ nhập T.Gá 3 → T.GC / T.Ktra trống = 0: 3 × 10 = 30 ph (không trộn chuẩn)", e)
        await set_num(row, 4, 4)
        e = await est(row); ok(e == expect("08:00", (3 + 4) * 10), "S3 T.Gá 3 + T.Ktra 4 (T.GC trống = 0): (3 + max(0,4)) × 10 = 70 ph", e)
        await set_num(row, 2, None); await set_num(row, 4, None)
        e = await est(row); ok(e == expect("08:00", 70), "S4 xoá hết số thực tế → về theo chuẩn 70 ph", e)
        await set_num(row, 1, 2)
        e = await est(row); ok(e == expect("08:00", 7 * 12), "S5 SL tính = OK 10 + NG 2 = 12 → 84 ph", e)

        print("[T] Chạy thử [TRIAL-EST-ACTUAL]")
        await new_group(pg); await set_use_std(pg, False)
        await pg.click("button:has-text('Chạy thử')"); await pg.wait_for_timeout(600)
        row = await gc_row(pg)
        e = await est(row); ok(e == expect("08:00", 10 + 3), "T1 cả 2 ô trống → theo chuẩn CB 10 + Chạy 3 = 13 ph (không nhân SL)", e)
        await set_num(row, 5, 10)
        e = await est(row); ok(e == expect("08:00", 10), "T2 [TRIAL-EST-ACTUAL] chỉ nhập T.CB 10 → T.Chạy trống = 0: 08:10 (không cộng T.Chạy chuẩn)", e)
        await set_num(row, 5, None); await set_num(row, 6, 7)
        e = await est(row); ok(e == expect("08:00", 7), "T3 chỉ nhập T.Chạy 7 → T.CB = 0: 08:07", e)

        print("[A] Không có chuẩn (chưa chọn máy) [FIX-ACTUAL-EST]")
        await new_group(pg); await set_use_std(pg, False); row = await gc_row(pg, m=None)
        e = await est(row); ok(e == "", "A1 chưa chọn máy (không chuẩn), chưa nhập thực tế → không hiện Dự kiến", e)
        await set_num(row, 2, 2); await set_num(row, 3, 1)
        e = await est(row); ok(e == expect("08:00", (2 + 1) * 10), "A2 [FIX-ACTUAL-EST] không chuẩn, đã nhập T.Gá 2 + T.GC 1 → vẫn tính (2 + 1) × 10 = 30 ph", e)

        print("[C] Qua giờ nghỉ 12:00–13:00 [FIX-EST-CONV]")
        await new_group(pg); await set_use_std(pg, False); row = await gc_row(pg, start="11:30")
        await set_num(row, 2, 1); await set_num(row, 3, 2)   # chu kỳ 3 × 10 = 30 ph
        e = await est(row); ok(e == "12:00" == expect("11:30", 30), "C1 11:30 + 30 ph làm = đúng 12:00 (chưa vào nghỉ)", e)
        await set_times(row, "11:45", "16:00")
        e = await est(row); ok(e == "13:15" == expect("11:45", 30), "C2 [FIX-EST-CONV] 11:45 + 30 ph = 13:15 (15 ph trước nghỉ + 15 ph sau nghỉ)", e)
        await set_times(row, "12:20", "16:00")
        e = await est(row); ok(e == "13:30" == expect("12:20", 30), "C3 [FIX-EST-CONV] bắt đầu trong giờ nghỉ 12:20 → làm từ 13:00: 13:30", e)
        await set_num(row, 0, 40); await set_times(row, "10:00", "16:00")
        e = await est(row); ok(e == expect("10:00", 120), "C4 10:00 + 3 × 40 = 120 ph → đúng 12:00 (không nhảy sang 13:00)", e)
        await set_num(row, 0, 50)
        e = await est(row); ok(e == expect("10:00", 150), "C5 10:00 + 150 ph → 13:30", e)

        print("[P] Xoá PO [FIX-PO-CLEAR]")
        await new_group(pg); await set_use_std(pg, False); row = await gc_row(pg, start="08:00", end="09:00")
        await set_num(row, 1, 2); await set_num(row, 2, 5); await set_num(row, 3, 2)
        await set_po(row, "")
        nc = await row.locator("select").nth(0).input_value() if await row.locator("select").count() else ""
        nums = [await get_num(row, i) for i in (0, 1, 2, 3, 4)]
        tms = await get_times(row)
        ok(nc in ("", None), "X1 [FIX-PO-CLEAR] xoá PO → ô NC về trống", nc)
        ok(all(x in ("", "0") for x in nums), "X2 [FIX-PO-CLEAR] xoá PO → OK / NG / T.Gá / T.GC / T.Ktra về trống", nums)
        ok(tms == ["08:00", "09:00"], "X3 [FIX-PO-CLEAR] giữ giờ bắt đầu / kết thúc", tms)
        opts = await pg.evaluate("""() => { const i = document.querySelector("input[placeholder='Gõ PO...']"); const l = i && i.list; return l ? [...l.options].map(o => o.value) : []; }""")
        ok(any("PO2" in o for o in opts) and any("PO1" in o for o in opts), "X4 [FIX-PO-CLEAR] nhóm hết KH → danh sách PO lọc lại đủ (không kẹt theo Part cũ)", opts[:8])
        await set_po(row, "PO1"); await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(400)
        ok(await row.locator("select").nth(1).input_value() == "", "X5 chọn lại PO1 + NC1: ô Máy chưa chọn (không mang máy cũ)", await row.locator("select").nth(1).input_value())
        await remove_groups(pg); await b.close()

        # Non-GC: T.Thực tế/pc = ô number cuối cùng trước T.PS; đọc mọi ô number của dòng
        async def nongc(user, po, sub, code, std, confirmed, flat=False):
            b = await p.chromium.launch(); pg = await login(b, user, "123456")
            await new_group(pg); await set_use_std(pg, True); row = subrow(pg); await set_po(row, po)
            if sub:
                sel = pg.locator("select").filter(has=pg.locator(f"option[value='{sub}']")).first
                await sel.select_option(sub); await pg.wait_for_timeout(500)
            inputs = row.locator("input[list]"); cin = None
            for i in range(await inputs.count()):
                ph = await inputs.nth(i).get_attribute("placeholder") or ""
                if "PO" not in ph: cin = inputs.nth(i); break
            await cin.fill(code); await cin.dispatch_event("change"); await pg.wait_for_timeout(700)
            vals = [(await row.locator("input[type=number]").nth(i).input_value()).strip() for i in range(await row.locator("input[type=number]").count())]
            act_i = 0 if flat else 2   # ô T.Thực tế/pc: mã chuẩn bị = ô số đầu; mã thường = sau OK / NG
            ok(len(vals) > act_i and vals[act_i] == str(std), f"N1 [STD-ACTUAL-DEFAULT] {user} {sub or ''} {code}: bật Tham khảo chuẩn → T.Thực tế/pc = T.Chuẩn/pc {std}", vals)
            stdbox = row.locator("input[readonly][title*='T.Chuẩn/pc']").first
            cls = await stdbox.get_attribute("class") or ""; title = await stdbox.get_attribute("title") or ""; v = await stdbox.input_value()
            ok(v == str(std), f"N2 ô T.Chuẩn hiện {std}", v)
            style = (await stdbox.get_attribute("style") or "").replace(" ", "")
            green = "bg-light" not in cls and "border:2pxsolid#198754" in style   # xanh = viền xanh đậm (NonGcStdInputStyle), không chỉ "thiếu bg-light"
            ok(green == confirmed, f"N3 [STD-CONFIRM-ROW] {code}: ô T.Chuẩn {'XANH (dòng đã xác nhận)' if confirmed else 'XÁM (dòng chưa xác nhận)'}", (cls, title[:120]))
            if not flat: await set_num(row, 0, 10)   # mã chuẩn bị (1T…) không có ô OK / NG: ô số đầu là T.Thực tế
            await set_times(row, "08:00", "16:00")
            e = await est(row); mins = round(float(std) if flat else float(std) * 10, 6)
            ok(mins == int(mins) and e == expect("08:00", int(mins)), f"N4 Dự kiến non-GC = {'T.Thực tế (mã chuẩn bị, không nhân SL)' if flat else '10 × T.Thực tế'} = {mins:g} ph", e)
            await set_use_std(pg, False); await pg.wait_for_timeout(300)
            await b.close()
        print("[N] Non-GC [STD-ACTUAL-DEFAULT][STD-CONFIRM-ROW]")
        await nongc("htsp01", "PO3", "Taro", "T1", 1.5, True)
        await nongc("htsp01", "PO3", "Taro", "1T", 15, False, flat=True)
        await nongc("htsp01", "PO3", "Bavia", "B1", 0.5, False)
        await nongc("htsp01", "PO3", "Rua", "R1", 0.2, False)
        await nongc("kcs01", "PO2", None, "K1", 1, False)
        await nongc("pkg01", "PO2", None, "P1", 0.3, False)
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
