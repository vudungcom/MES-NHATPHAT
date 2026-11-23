# [GÓI 32a 22/11/2026] drive_g32.py — bấm trang thật: tab Sao lưu (Cài đặt & Phân quyền) + băng cảnh báo Admin [G32-SAOLUU].
#   Harness: SaoLuuSql giả (Stubs/HarnessSaoLuu.cs) ghi file .bak giả; DB = MES_HARNESS; thư mục sao lưu = /tmp/g32_<port> (driver tạo mới).
#   STEP B1 Admin (stdadm) trang chủ: băng "Chưa cài thư mục sao lưu"
#   STEP B2 người thường (xem01): KHÔNG có băng; /settings?tab=saoluu → "Truy cập bị từ chối", không có tab Sao lưu
#   STEP T1 stdadm /settings?tab=saoluu → mở thẳng tab, trạng thái "Chưa cài thư mục"
#   STEP T2 nhập thư mục → Lưu → "Đã lưu cài đặt", "Tự động: BẬT", nhật ký có stdadm
#   STEP T3 Lưu nhanh ngay → ĐẠT, folder nhanh/<tên>/manifest.json KetQua DAT + .bak.zip, bảng bản nhanh 1 dòng ĐẠT
#   STEP T4 Tạo trọn bộ ngay → ĐẠT, folder bo/<tên>/ có phan-mem/MES.Web.zip + cau-hinh/appsettings.json, bảng bộ ngày "Mới nhất"
#   STEP T5 giả lỗi BACKUP → báo "Sao lưu lỗi", bảng có dòng LỖI, băng đỏ (trang khác) có "bị LỖI"
#   STEP T6 thêm 10 bộ cũ trên đĩa → giảm Giữ bộ ngày 7 → 2, tuần 4 → 0 → hiện PIN "sẽ XOÁ"; Huỷ → không đổi; PIN 1111 → lưu;
#           lưu nhanh lần sau → dọn đúng luật (còn bộ hôm nay + hôm qua + mới nhất mỗi tháng)
#   STEP T7 nhập sai (5 phút) → báo lỗi, không lưu
#   STEP T8 link trên băng → mở thẳng tab Sao lưu
#   STEP T9 [G32-RA2] thêm 12 bộ cũ ngoài luật → lượt lưu TẠM DỪNG dọn bộ ngày; nút 'Cho dọn lại' + PIN → lượt sau dọn
#   STEP T10 [G32B-XOA-BAN] nút Xoá: bộ ĐẠT mới nhất không có nút; Huỷ PIN → không xoá; PIN → folder mất + nhật ký "Xoá tay"
#   STEP T11 [G32B-TRUOC-NANG-CAP] bộ trước nâng cấp (harness chạy service thật) + bộ cũ giả: bảng riêng; giảm "Giữ bộ trước nâng cấp" → PIN → dọn còn 1
#   STEP T12 [G32B-O-DAY] ô "Ổ còn trống" có số; giả ổ còn ít hơn cỡ bộ ngày → băng đỏ trang chủ "còn trống … nút Xoá"
#   STEP T13 ô Giữ bộ trước nâng cấp = 0 → báo lỗi, không lưu
#   Bản cũ (a4c1a75): không có tab / băng → SAI là đúng. Bản 040bc56 (Gói 32a): T10–T13 SAI là đúng. Nguồn: HÙNG 22/11 14:34–14:52; Gói 32b HÙNG 23/11 13:20.
# Chạy trên harness VỪA khởi động: python3 drive_g32.py <port> <nhãn>
import sys, asyncio, json, os, shutil, glob, datetime, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2] if len(sys.argv) > 2 else "x"
BASE = f"http://127.0.0.1:{PORT}"
BK = f"/tmp/g32_{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except Exception as e: return {"ok": False, "error": str(e)}

def sets(kind):
    d = os.path.join(BK, "MES_HARNESS", kind)
    if not os.path.isdir(d): return []
    return sorted(x for x in os.listdir(d) if os.path.isfile(os.path.join(d, x, "manifest.json")))

def manifest(kind, name):
    with open(os.path.join(BK, "MES_HARNESS", kind, name, "manifest.json"), encoding="utf-8") as f: return json.load(f)

async def login(b, user, pw="123456"):
    ctx = await b.new_context(); pg = await ctx.new_page()
    await pg.set_viewport_size({"width": 1700, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return ctx, pg

async def banner(pg, want=None, timeout_ms=8000):
    # băng là thành phần tương tác (prerender: false) → chờ mạch Blazor nối; want: chờ tới khi có chữ này
    loc = pg.locator("#saoLuuBanner"); t = ""
    for _ in range(timeout_ms // 250):
        t = (await loc.inner_text()) if await loc.count() else ""
        if (want is None and t) or (want is not None and want in t): return t
        await pg.wait_for_timeout(250)
    return t

async def open_tab(pg):
    await pg.goto(BASE + "/settings?tab=saoluu"); await pg.wait_for_timeout(2500)

async def wait_text(pg, sel, timeout=20000):
    try:
        await pg.wait_for_selector(sel, timeout=timeout)
        return await pg.locator(sel).inner_text()
    except Exception:
        return ""

async def click_wait(pg, btn, kw, timeout=60000):
    # bấm rồi chờ #slMsg / #slErr có chữ kw (ô báo cũ còn từ bước trước → không đọc nhầm)
    await pg.click(btn)
    js = "kw => { const a = document.querySelector('#slMsg'), b = document.querySelector('#slErr'); return (a && a.innerText.includes(kw)) || (b && b.innerText.includes(kw)); }"
    try: await pg.wait_for_function(js, arg=kw, timeout=timeout)
    except Exception: pass
    m = await pg.locator("#slMsg").inner_text() if await pg.locator("#slMsg").count() else ""
    e = await pg.locator("#slErr").inner_text() if await pg.locator("#slErr").count() else ""
    return m, e

async def set_val(pg, sel, v):
    await pg.fill(sel, str(v)); await pg.locator(sel).dispatch_event("change")

async def main():
    shutil.rmtree(BK, ignore_errors=True); os.makedirs(BK)
    r = post("/harness/saoluu?fail=0&reset=1")   # cài đặt sao lưu về mặc định (harness dùng lại được)
    ok("cfg" in r, "Reset cài đặt sao lưu harness", r)
    async with async_playwright() as p:
        br = await p.chromium.launch()
        ctx, pg = await login(br, "stdadm")
        try:
            print("STEP B1 băng Admin chưa cài")
            await pg.goto(BASE + "/"); await pg.wait_for_timeout(2500)
            t = await banner(pg, "Chưa cài thư mục sao lưu")
            ok("Chưa cài thư mục sao lưu" in t, "B1 Admin: băng đỏ 'Chưa cài thư mục sao lưu'", t)

            print("STEP B2 người thường")
            c2, p2 = await login(br, "xem01")
            await p2.goto(BASE + "/"); await p2.wait_for_timeout(2500)
            ok(await banner(p2, None, 4000) == "", "B2a người thường: không có băng sao lưu")
            await p2.goto(BASE + "/settings?tab=saoluu"); await p2.wait_for_timeout(2500)
            body2 = await p2.locator("body").inner_text()
            ok("Truy cập bị từ chối" in body2 and await p2.locator("#tabSaoLuu").count() == 0 and await p2.locator("#slBtnNhanh").count() == 0,
               "B2b người thường vào /settings?tab=saoluu → từ chối, không có tab / nút", body2[:300])
            await c2.close()

            print("STEP T1 mở thẳng tab")
            await open_tab(pg)
            st1 = (await pg.locator("#slTuDong").inner_text()).strip() if await pg.locator("#slTuDong").count() else ""
            ok(st1 == "Chưa cài thư mục", "T1 ?tab=saoluu mở tab Sao lưu, trạng thái 'Chưa cài thư mục'", st1 or "(tab Sao lưu không mở)")
            if not st1: await pg.click("#tabSaoLuu"); await pg.wait_for_timeout(1500)

            print("STEP T2 lưu thư mục")
            await set_val(pg, "#slThuMucSql", BK)
            msg, _ = await click_wait(pg, "#slBtnLuu", "Đã lưu cài đặt", 10000)
            ok("Đã lưu cài đặt" in msg and (await pg.locator("#slTuDong").inner_text()).strip() == "Tự động: BẬT", "T2 lưu thư mục → 'Đã lưu cài đặt', Tự động BẬT", msg)
            body = await pg.locator("body").inner_text()
            ok("Nhật ký đổi cài đặt" in body, "T2b có mục nhật ký đổi cài đặt")
            await pg.click("summary:has-text('Nhật ký đổi cài đặt')")
            nk = await pg.locator("details pre").inner_text()
            ok("stdadm" in nk and "Thư mục (máy SQL)" in nk, "T2c nhật ký: stdadm đổi thư mục", nk[:300])

            print("STEP T3 lưu nhanh ngay")
            msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")
            nh = sets("nhanh")
            m = manifest("nhanh", nh[0]) if nh else {}
            zips = glob.glob(os.path.join(BK, "MES_HARNESS", "nhanh", "*", "du-lieu", "*.bak.zip"))
            ok("Đã lưu nhanh" in msg and len(nh) == 1 and m.get("KetQua") == "DAT" and len(zips) == 1, "T3a Lưu nhanh ngay → ĐẠT, 1 folder + manifest DAT + .bak.zip", (msg, nh, m.get("KetQua")))
            await pg.click("#slBtnXemNhanh"); await pg.wait_for_timeout(500)
            rows = await pg.locator("#slBangNhanh tbody tr").all_inner_texts()
            ok(len(rows) == 1 and "ĐẠT" in rows[0] and "Mới nhất" in rows[0], "T3b bảng bản nhanh: 1 dòng ĐẠT, Giữ vì 'Mới nhất'", rows)

            print("STEP T4 trọn bộ")
            msg, _ = await click_wait(pg, "#slBtnBo", "trọn bộ")
            bo = sets("bo")
            ok("Đã tạo bộ trọn bộ" in msg and len(bo) == 1, "T4a Tạo trọn bộ ngay → ĐẠT", (msg, bo))
            if bo:
                d = os.path.join(BK, "MES_HARNESS", "bo", bo[0])
                ok(all(os.path.isfile(os.path.join(d, x)) for x in ["phan-mem/MES.Web.zip", "cau-hinh/appsettings.json", "uploads/uploads.zip", "DOC-TRUOC.txt", "cau-hinh/sao-luu/cai-dat-MES_HARNESS.json"]),
                   "T4b bộ có phần mềm, appsettings, uploads, DOC-TRUOC, cài đặt sao lưu", os.listdir(d))
            rows = await pg.locator("#slBangBo tbody tr").all_inner_texts()
            ok(len(rows) == 1 and "ĐẠT" in rows[0] and "Mới nhất" in rows[0], "T4c bảng bộ ngày: 1 dòng ĐẠT 'Mới nhất'", rows)
            st = (await pg.locator("#slBoGanNhat").inner_text())
            ok(datetime.date.today().strftime("%d/%m/%Y") in st, "T4d 'Bộ ngày gần nhất' = hôm nay", st)

            print("STEP T5 giả lỗi")
            post("/harness/saoluu?fail=1")
            _, err = await click_wait(pg, "#slBtnNhanh", "Sao lưu lỗi")
            rows = await pg.locator("#slBangNhanh tbody tr").all_inner_texts()
            ok("Sao lưu lỗi" in err and "BACKUP giả lỗi" in err and any("LỖI" in r for r in rows), "T5a lỗi BACKUP → báo lỗi + dòng LỖI trong bảng", (err, rows))
            await pg.goto(BASE + "/"); await pg.wait_for_timeout(2500)
            t = await banner(pg, "bị LỖI")
            ok("bị LỖI" in t and "BACKUP giả lỗi" in t, "T5b băng đỏ trang chủ: lần sao lưu gần nhất bị LỖI", (t or "(không có băng)", sets("nhanh"), sets("bo")))

            print("STEP T8 link băng")
            await pg.click("#saoLuuBanner a"); await pg.wait_for_timeout(2500)
            ok("tab=saoluu" in pg.url and await pg.locator("#slBtnNhanh").count() == 1, "T8 link 'Mở Cài đặt › Sao lưu' mở thẳng tab", pg.url)

            print("STEP T6 giảm số giữ (PIN)")
            today = datetime.date.today()
            fake = []
            for i in range(1, 11):
                dd = today - datetime.timedelta(days=i)
                name = dd.strftime("%Y-%m-%d") + "_235500"
                d = os.path.join(BK, "MES_HARNESS", "bo", name); os.makedirs(d)
                with open(os.path.join(d, "manifest.json"), "w", encoding="utf-8") as f: json.dump({"KetQua": "DAT", "TongBytes": 10}, f)
                fake.append((dd, name))
            await open_tab(pg)
            await set_val(pg, "#slGiuNgay", 2); await set_val(pg, "#slGiuTuan", 0)
            await pg.click("#slBtnLuu"); await pg.wait_for_timeout(1500)
            modal = await pg.locator(".mes-modal").inner_text() if await pg.locator(".mes-modal").count() else ""
            ok("sẽ XOÁ" in modal and "Xác nhận đổi cài đặt sao lưu" in modal, "T6a giảm số giữ → hiện PIN kèm danh sách sẽ xoá", modal[:400])
            await pg.click(".mes-modal button:has-text('Hủy')"); await pg.wait_for_timeout(800)
            msg = await wait_text(pg, "#slMsg", 5000)
            await open_tab(pg)
            v = await pg.locator("#slGiuNgay").input_value()
            ok("Đã huỷ" in msg and v == "7", "T6b Huỷ PIN → cài đặt giữ nguyên 7 ngày", (msg, v))
            await set_val(pg, "#slGiuNgay", 2); await set_val(pg, "#slGiuTuan", 0)
            await pg.click("#slBtnLuu"); await pg.wait_for_timeout(1500)
            await pg.fill(".mes-modal input[type=password]", "1111"); await pg.click(".mes-modal button:has-text('Xác nhận')")
            msg = await wait_text(pg, "#slMsg", 8000)
            await open_tab(pg)
            v = await pg.locator("#slGiuNgay").input_value()
            ok("Đã lưu cài đặt" in msg and v == "2", "T6c PIN đúng → lưu 2 ngày", (msg, v))
            n_before = len(sets("bo"))
            msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")
            # đáp án: bộ hôm nay (thật) + hôm qua (giả) + mới nhất mỗi tháng (giả, tháng khác tháng này)
            exp = {sets_today for sets_today in [x for x in sets("bo") if x.startswith(today.strftime("%Y-%m-%d"))]}
            exp.add((today - datetime.timedelta(days=1)).strftime("%Y-%m-%d") + "_235500")
            by_month = {}
            for dd, name in fake:
                k = (dd.year, dd.month)
                if k != (today.year, today.month) and (k not in by_month or name > by_month[k]): by_month[k] = name
            exp |= set(by_month.values())
            got = set(sets("bo"))
            ok(n_before == 11 and got == exp and "Dọn" in msg, "T6d lần lưu sau dọn đúng luật mới (hôm nay + hôm qua + mới nhất mỗi tháng cũ)", (n_before, sorted(got), sorted(exp), msg))

            print("STEP T9 tạm dừng dọn + Cho dọn lại (PIN)")
            for i in range(20, 32):
                dd = today - datetime.timedelta(days=i)
                d = os.path.join(BK, "MES_HARNESS", "bo", dd.strftime("%Y-%m-%d") + "_235400"); os.makedirs(d, exist_ok=True)
                with open(os.path.join(d, "manifest.json"), "w", encoding="utf-8") as f: json.dump({"KetQua": "DAT", "TongBytes": 10}, f)
            n9 = len(sets("bo"))
            await open_tab(pg)
            msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")
            ok("TẠM DỪNG" in msg and len(sets("bo")) == n9, "T9a thêm 12 bộ cũ ngoài luật → lượt lưu báo TẠM DỪNG dọn, không xoá bộ nào", (msg, n9, len(sets("bo"))))
            await open_tab(pg)
            has = await pg.locator("#slBtnChoDon").count()
            btxt = await pg.locator("#slBtnChoDon").inner_text() if has else ""
            ok(has == 1 and "nhập PIN" in btxt, "T9b hiện nút 'Cho dọn lại — xác nhận xoá N bộ ngày (nhập PIN)'", btxt)
            if has:
                await pg.click("#slBtnChoDon"); await pg.wait_for_timeout(1000)
                mt = await pg.locator(".mes-modal").inner_text() if await pg.locator(".mes-modal").count() else ""
                if mt:
                    await pg.fill(".mes-modal input[type=password]", "1111"); await pg.click(".mes-modal button:has-text('Xác nhận')")
                msg = await wait_text(pg, "#slMsg", 8000)
                ok("Cho phép lần dọn tới XOÁ" in mt and "Đã cho dọn lại" in msg, "T9c PIN → 'Đã cho dọn lại'", (mt[:200], msg))
                msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")
                ok("Dọn" in msg and "TẠM DỪNG" not in msg and len(sets("bo")) < n9, "T9d lượt sau dọn đúng luật", (msg, len(sets("bo"))))

            print("STEP T10 xoá tay 1 bản (PIN)")
            await open_tab(pg)
            rows = pg.locator("#slBangBo tbody tr")
            nrow = await rows.count()
            first_has = await rows.nth(0).locator(".sl-xoa").count() if nrow else -1
            last_has = await rows.nth(nrow - 1).locator(".sl-xoa").count() if nrow else 0
            ok(nrow >= 2 and first_has == 0 and last_has == 1, "T10a bộ ĐẠT mới nhất (dòng đầu) không có nút Xoá, bộ cũ có", (nrow, first_has, last_has))
            if nrow >= 2 and last_has:
                cu_ten = (await rows.nth(nrow - 1).locator("td").first.inner_text()).strip()
                truoc = set(sets("bo"))
                await rows.nth(nrow - 1).locator(".sl-xoa").click(); await pg.wait_for_timeout(800)
                mt = await pg.locator(".mes-modal").inner_text() if await pg.locator(".mes-modal").count() else ""
                await pg.click(".mes-modal button:has-text('Hủy')"); await pg.wait_for_timeout(800)
                msg = await wait_text(pg, "#slMsg", 5000)
                ok("Xác nhận xoá bản sao lưu" in mt and "XOÁ hẳn" in mt and "không xoá bản nào" in msg and set(sets("bo")) == truoc, "T10b Huỷ PIN → không xoá", (mt[:200], msg))
                rows = pg.locator("#slBangBo tbody tr"); nrow = await rows.count()
                await rows.nth(nrow - 1).locator(".sl-xoa").click(); await pg.wait_for_timeout(800)
                await pg.fill(".mes-modal input[type=password]", "1111"); await pg.click(".mes-modal button:has-text('Xác nhận')")
                msg = await wait_text(pg, "#slMsg", 8000)
                sau = set(sets("bo"))
                await open_tab(pg); await pg.click("summary:has-text('Nhật ký đổi cài đặt')")
                nk = await pg.locator("details pre").inner_text()
                ok("Đã xoá" in msg and len(truoc - sau) == 1 and len(sau - truoc) == 0 and "Xoá tay bộ ngày" in nk and "stdadm" in nk,
                   "T10c PIN đúng → xoá đúng 1 bộ (folder mất), nhật ký 'Xoá tay bộ ngày' + người bấm", (msg, cu_ten, sorted(truoc - sau), nk[:300]))

            print("STEP T11 bộ trước nâng cấp")
            r = post("/harness/saoluu-tnc")
            for i in (40, 41):
                dd = today - datetime.timedelta(days=i)
                d = os.path.join(BK, "MES_HARNESS", "truoc-nang-cap", dd.strftime("%Y-%m-%d") + "_090000"); os.makedirs(d, exist_ok=True)
                with open(os.path.join(d, "manifest.json"), "w", encoding="utf-8") as f: json.dump({"KetQua": "DAT", "TongBytes": 10}, f)
            tn = sets("truoc-nang-cap")
            m = manifest("truoc-nang-cap", r.get("ten", "")) if r.get("ten") in tn else {}
            ok(r.get("dat") and len(tn) == 3 and m.get("Loai") == "truoc-nang-cap"
               and os.path.isfile(os.path.join(BK, "MES_HARNESS", "truoc-nang-cap", r.get("ten", "?"), "phan-mem", "MES.Web.zip")),
               "T11a service tạo bộ trước nâng cấp TRỌN BỘ vào truoc-nang-cap\\ (manifest Loai truoc-nang-cap)", (r, tn))
            await open_tab(pg)
            rows = await pg.locator("#slBangTnc tbody tr").all_inner_texts()
            ok(len(rows) == 3 and "Trong 10 bộ đạt mới nhất" in rows[0], "T11b bảng 'Bộ trước nâng cấp' 3 dòng, Giữ vì 'Trong 10 bộ đạt mới nhất'", rows)
            await set_val(pg, "#slGiuTnc", 1)
            await pg.click("#slBtnLuu"); await pg.wait_for_timeout(1500)
            modal = await pg.locator(".mes-modal").inner_text() if await pg.locator(".mes-modal").count() else ""
            ok(modal.count("Bộ trước nâng cấp") == 2 and "sẽ XOÁ 2" in modal, "T11c giảm Giữ bộ trước nâng cấp 10 → 1 → PIN báo xoá 2 bộ", modal[:400])
            if modal:
                await pg.fill(".mes-modal input[type=password]", "1111"); await pg.click(".mes-modal button:has-text('Xác nhận')")
                await wait_text(pg, "#slMsg", 8000)
            msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")
            ok(sets("truoc-nang-cap") == [r.get("ten")], "T11d lượt lưu sau dọn còn đúng bộ trước nâng cấp mới nhất", (sets("truoc-nang-cap"), msg))

            print("STEP T12 ổ sắp đầy")
            await open_tab(pg)
            ot = (await pg.locator("#slOTrong").inner_text()).strip() if await pg.locator("#slOTrong").count() else ""
            ok("/" in ot and ("GB" in ot or "MB" in ot), "T12a ô 'Ổ còn trống' có số (đo ở máy chủ)", ot)
            post("/harness/saoluu-o?trong=100")
            msg, _ = await click_wait(pg, "#slBtnNhanh", "Đã lưu nhanh")   # lượt lưu xoá bộ nhớ đệm băng
            await pg.goto(BASE + "/"); await pg.wait_for_timeout(2500)
            t = await banner(pg, "còn trống")
            ok("còn trống 100 B" in t and "nút Xoá" in t, "T12b giả ổ còn 100 B < cỡ bộ ngày → băng đỏ trang chủ 'còn trống … nút Xoá'", t)
            post("/harness/saoluu-o?trong=-1")

            print("STEP T13 ô giữ bộ trước nâng cấp sai")
            await open_tab(pg)
            await set_val(pg, "#slGiuTnc", 0)
            _, err = await click_wait(pg, "#slBtnLuu", "trước nâng cấp", 10000)
            await open_tab(pg)
            v = await pg.locator("#slGiuTnc").input_value()
            ok("từ 1 đến 100" in err and v == "1", "T13 Giữ bộ trước nâng cấp = 0 → báo 'từ 1 đến 100', không lưu", (err, v))

            print("STEP T7 nhập sai")
            await open_tab(pg)
            await set_val(pg, "#slPhut", 5)
            _, err = await click_wait(pg, "#slBtnLuu", "từ 15", 10000)
            await open_tab(pg)
            v = await pg.locator("#slPhut").input_value()
            ok("từ 15" in err and v == "60", "T7 5 phút → báo 'từ 15 đến 1440', không lưu", (err, v))
        finally:
            await br.close()
    print(f"\n[drive_g32 {OUT}] {'ĐẠT HẾT' if fails == 0 else str(fails) + ' SAI'}")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
