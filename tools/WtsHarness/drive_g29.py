# [GÓI 29 21/11/2026] drive_g29.py — bấm trang thật: WTS Report theo kỳ [G29-RPT] + Hiệu suất máy theo kỳ [G29-HSM-KY] + % trên phút kế hoạch
#   [G29-HSM-MAUSO] + ô Tìm nhanh [G29-TIMNHANH]. Dữ liệu Stubs/HarnessG29.cs (ca G29 08–17 nghỉ 12–13; ngày 1 = thứ Hai tuần này, ngày 2 = thứ Ba).
#   ĐÁP ÁN TÍNH TAY:
#     A ngày 1: M29A 08–10 + M29B 09–11 (chồng 09–10 → mỗi dòng 90) + HOP29 11:00–11:30 (vô ích 30) + chạy thử 13–14 (60) + SUA29 14–15 (có ích 60)
#               → làm 330, có ích 300, vô ích 30; kế hoạch 480 → chưa khai báo 150
#     B ngày 1: VS29 08:00–08:30 (vô ích 30) + M29B 08:30–12:00 (210) + HOP29 11:30–12:00 CHỒNG dòng sản xuất → HOP29 tính 0 (có ích thắng)
#               → làm 240, có ích 210, vô ích 30; chưa khai báo 240
#     A ngày 2: M29A 08–12 (240) + M29B 13–17 (240) → làm 480, có ích 480, chưa khai báo 0
#     Tuần: làm 1.050, có ích 990, vô ích 60, chưa khai báo 390, OK 251 / NG 1. B 87,5 % đứng trước A 96,3 %.
#     A theo việc: Gia công · Hàng loạt 660 (4 dòng, OK 200, NG 1), Gia công · Chạy thử 60, SUA29 60, HOP29 30.
#     Máy (ca G29, 2 ngày có WTS, kế hoạch 480/ngày): M29A chạy 180 + 240 = 420 / 960 = 43,8 %; M29B ngày 1 08:30–12:00 = 210 (A + B cùng máy
#     09–11 tính 1 lần), ngày 2 13–17 = 240 + dừng 60 → chạy 450 = 46,9 %, dừng 6,3 %; M29B người đứng máy: A 360, B 210.
#   STEP R1 [G29-RPT]       admin → WTS Report (mặc định tuần này, tất cả): ô số 1.050 / 990 / 60 / 390 / 251 / 1; bảng người B trước A, 87,5 % / 96,3 %
#   STEP R2 [G29-RPT]       chọn "Công nhân A29": ô số 810 / 780 / 30 / 150; bảng theo việc Hàng loạt 660, Chạy thử 60, SUA29 60, HOP29 30
#   STEP R3 [G29-TIMNHANH]  A + tìm "M29B": ô làm 330 (90 + 240), chưa khai báo "—"
#   STEP R4 [G29-RPT]       bấm ngày 1 → xem Ngày: dòng thời gian có khối, ô làm 330, có ích 300
#   STEP R5 [G29-TIMNHANH]  tất cả + tìm "HOP29": vô ích 30 (HOP29 của B chồng dòng sản xuất = 0), bảng "Dòng khớp tìm nhanh" 2 dòng
#   STEP M1 [G29-HSM-KY]    /hieu-suat-may → Tuần, ca G29: M29A 43,8 %, M29B 46,9 %; M29B xếp trước? (43,8 < 46,9 → M29A trước M29B)
#   STEP M2 [G29-TIMNHANH]  tìm "Kẹt dao" → chỉ M29B
#   STEP M3 [G29-HSM-KY]    bấm M29B: Các lần dừng "Kẹt dao" 60; Ai đứng máy A 360, B 210
#   STEP M4 [G29-HSM-MAUSO] về tất cả, bấm ô M29A ngày 1 → trang Ngày đúng ngày, ca G29; M29A 37.5% (180/480 — trước: 180/540 = 33.3%)
#   Bản cũ (HEAD bdc3fd9): không có tab mới / nút Tuần → SAI là đúng.
#   [G30-RPT-MAUSO 21/11] % trên GIỜ CHUẨN theo ca (= làm + chưa khai báo): tuần 1.050 + 390 = 1.440; A 780 / 960 = 81,3 %, B 210 / 480 = 43,8 %
#     (trước 96,3 % / 87,5 % trên phút khai báo); A riêng 960; ngày 1 A 480 → 300 / 480 = 62,5 %. Ô số đầu = "Giờ chuẩn (theo ca)".
#     STEP R6 [G30-RPT-MAUSO] nút Năm → "Năm yyyy", A / B giữ 81,3 % / 43,8 %. Bản Gói 29 (e92bf01): R1 / R2 / R4 / R6 SAI đúng chỗ đổi.
#     Nguồn: HÙNG 21/11 16:03 + 16:10 + 16:19.
#   [G31-HSM-24H 22/11] Máy 24h / ngày, KHÔNG ca, mọi ngày lịch. Tuần 7 ngày × 1440 = 10.080 ph / máy. Xưởng có người (mọi dòng WTS mọi người):
#     ngày 1 08:00–12:00 + 13:00–15:00 = 360, ngày 2 08–12 + 13–17 = 480 → 840. M29A chạy 420 → 4,2 % (24h), 50,0 % lúc có người;
#     M29B chạy 450 + dừng 60 → 4,5 %, 53,6 %, rảnh lúc có người 840 − 450 − 60 = 330. Trang ngày M29A ngày 1: 180 / 1440 = 12.5 %.
#     Rà độc lập 22/11: ngày / giờ chưa tới KHÔNG tính → mẫu % 24h tuần này = phút từ thứ Hai 00:00 tới bây giờ (pct_set). Chạy thứ Hai trước 17:00
#     thì dữ liệu ngày 1 sau giờ hiện tại chưa tính → M1 / M4 có thể lệch (dữ liệu seed theo thứ Hai tuần này) — chạy lại ngày khác.
#     M5: thêm máy M29T bộ phận TARO (HarnessG29) để ô Bộ phận GC phải loại nó.
#     STEP M5 [G31-LOC-NHOM] ô Bộ phận GC · M6 nút Năm (bảng Máy × tháng) · R7 [G31-LOC-NHOM] ô Nhóm WTS Report (thêm người KCS qua
#     /harness/g31-seed, K31 việc 11–13 qua nghỉ trưa đã lưu: Gia công giữ 1.440 / 990 / 60 / 390, KCS = 480 / 0 / 60 / 420). Bản Gói 30 (6dc3153): M1 / M3 / M4 / M5 / M6 / R7 SAI. Nguồn: HÙNG 22/11 08:58 + 09:03 + 11:58.
# Chạy trên harness VỪA khởi động: python3 drive_g29.py <port> <nhãn>
import sys, asyncio, json, re, urllib.request, datetime
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}

def pct_set(num, mon):
    # [G31-HSM-24H] % chạy 24h tuần này: hôm nay chỉ tính tới giờ hiện tại (ngày chưa tới không tính) → mẫu = phút từ thứ Hai 00:00 tới bây giờ;
    #   chấp nhận lệch 1–2 phút (trang tải trong lúc đồng hồ chạy) + 2 cách làm tròn
    from decimal import Decimal, ROUND_HALF_UP, ROUND_HALF_EVEN
    m0 = datetime.datetime.combine(mon, datetime.time()); now = datetime.datetime.now()
    end = min(now, m0 + datetime.timedelta(days=7)); base = int((end - m0).total_seconds() // 60)
    out = set()
    for den in range(base - 2, base + 3):
        for rd in (ROUND_HALF_UP, ROUND_HALF_EVEN):
            out.add(str(Decimal(num * 100 / den).quantize(Decimal("0.1"), rounding=rd)).replace(".", ",") + "%")
    return out

async def kpis(pg):
    return [x.strip() for x in await pg.locator("div.row-cols-md-5 .fs-4").all_inner_texts()]

async def main():
    st = post("/harness/g29-seed")
    ok(st.get("ok"), "Seed G29", st)
    mon = datetime.date.fromisoformat(st.get("monday", "2000-01-01"))
    async with async_playwright() as p:
        br = await p.chromium.launch(); ctx = await br.new_context(); pg = await ctx.new_page()
        await pg.set_viewport_size({"width": 1900, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        try:
            await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(3000)
            print("STEP R1 G29-RPT — tất cả công nhân, tuần này")
            await pg.click("button:has-text('WTS Report')"); await pg.wait_for_timeout(2500)
            k = await kpis(pg)
            ok(k == ["1.440", "990", "60", "390", "251 / 1"], "R1 ô số tuần: giờ chuẩn 1.440 (1.050 + 390) / 990 / 60 / 390 / 251 / 1", k)
            body = await pg.locator("body").inner_text()
            ia = body.find("Công nhân A29"); ib = body.find("Công nhân B29")
            ok(0 <= ib < ia, "R1 bảng người: B29 (thấp hơn) đứng trước A29", (ia, ib))
            ok("43,8%" in body and "81,3%" in body and "87,5%" not in body and "96,3%" not in body, "R1 [G30] % có ích trên giờ chuẩn: B 43,8% · A 81,3% (không còn 87,5 / 96,3)", body[:500])
            ok("Giờ chuẩn (theo ca)" in body and "Vô ích đã khai báo" in body and "Không rõ ca" not in body, "R1 [G30] ô Giờ chuẩn + chú thích 3 phần, không người nào 'Không rõ ca'", body[:600])
            await pg.screenshot(path=f"{OUT}_g29_r1.png", full_page=True)

            print("STEP R2 G29-RPT — chọn Công nhân A29")
            await pg.fill("#rptWorker", "Công nhân A29"); await pg.locator("#rptWorker").dispatch_event("change"); await pg.wait_for_timeout(1500)
            k = await kpis(pg)
            ok(k[:4] == ["960", "780", "30", "150"], "R2 ô số A29: giờ chuẩn 960 (810 + 150) / 780 / 30 / 150", k)
            b2 = await pg.locator("body").inner_text()
            ok("81,3%" in b2 and "Tổng = giờ chuẩn" in b2 and "Giờ ca không có dòng WTS" in b2, "R2 [G30] A 81,3%; bảng theo việc có dòng Chưa KB + Tổng = giờ chuẩn", b2[b2.find("Theo việc"):b2.find("Theo việc") + 700])
            bars = await pg.locator("button:has-text('/ 480')").all_inner_texts()
            b1 = [x for x in bars if "300 · 30 · 150 / 480" in x]
            ok(len(b1) == 1 and "62,5%" in b1[0], "R2 [G30] thanh từng ngày: ngày 1 300 · 30 · 150 / 480 → 62,5% (trước 90,9% trên khai báo)", bars)
            hd = [x for x in await pg.locator("div.rounded.border.small").all_inner_texts() if "khai báo 330" in x]
            ok(len(hd) == 1 and "chuẩn 480" in hd[0] and "chưa KB 150" in hd[0] and "62,5%" in hd[0], "R2 [G30] đầu nhóm ngày 1 chi tiết: chuẩn 480, chưa KB 150, 62,5%", hd)
            body = await pg.locator("body").inner_text()
            def has_row(lbl, mins):
                return re.search(re.escape(lbl) + r"[^\n]*\n?[^\n]*?\b" + mins + r"\b", body) is not None
            ok("Gia công · Hàng loạt" in body and "660" in body and "Gia công · Chạy thử" in body and "SUA29" in body and "HOP29" in body,
               "R2 bảng theo việc: Hàng loạt 660, Chạy thử, SUA29, HOP29", body[body.find("Theo việc"):body.find("Theo việc") + 600])
            rows = await pg.locator("table:has(th:has-text('Việc / mã')) tbody tr").all_inner_texts()
            hl = [r for r in rows if "Hàng loạt" in r]
            ok(len(hl) == 1 and re.search(r"\b4\b", hl[0]) and "660" in hl[0] and "200" in hl[0], "R2 Hàng loạt: 4 dòng, 660 phút, OK 200", hl)
            await pg.screenshot(path=f"{OUT}_g29_r2.png", full_page=True)

            print("STEP R3 G29-TIMNHANH — A + tìm M29B")
            await pg.fill("#rptSearch", "M29B"); await pg.wait_for_timeout(1200)
            k = await kpis(pg)
            ok(k[0] == "330" and k[3] == "—", "R3 tìm M29B: làm 330 (90 + 240), chưa khai báo —", k)
            await pg.fill("#rptSearch", ""); await pg.wait_for_timeout(1000)

            print("STEP R4 G29-RPT — bấm ngày 1 → xem Ngày")
            lbl = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"][mon.weekday()] + " " + mon.strftime("%d/%m")
            await pg.locator("button", has_text=lbl).first.click(); await pg.wait_for_timeout(2500)
            k = await kpis(pg)
            body = await pg.locator("body").inner_text()
            ok(k[:4] == ["480", "300", "30", "150"] and "Dòng thời gian trong ngày" in body, "R4 ngày 1: giờ chuẩn 480 / 300 / 30 / 150 + dòng thời gian", (k, body[:300]))
            ok("62,5%" in body, "R4 [G30] ngày 1 có ích 300 / 480 = 62,5%", body[:800])
            ok(await pg.locator("div.position-absolute[title*='Hàng loạt']").count() >= 2, "R4 dòng thời gian có khối Hàng loạt")
            await pg.screenshot(path=f"{OUT}_g29_r4.png", full_page=True)

            print("STEP R5 G29-TIMNHANH — tất cả + tìm HOP29 (tuần)")
            await pg.click("button:has-text('Tuần')"); await pg.wait_for_timeout(2000)
            await pg.fill("#rptWorker", ""); await pg.locator("#rptWorker").dispatch_event("change"); await pg.wait_for_timeout(800)
            await pg.fill("#rptSearch", "HOP29"); await pg.wait_for_timeout(1200)
            k = await kpis(pg)
            ok(k[2] == "30" and k[0] == "30", "R5 tìm HOP29: vô ích 30 (HOP29 của B chồng sản xuất = 0)", k)
            n = await pg.locator("div.border.rounded", has=pg.locator("div.fw-semibold", has_text="Dòng khớp tìm nhanh")).locator("tbody tr").count()
            ok(n == 2, "R5 bảng Dòng khớp tìm nhanh: 2 dòng", n)
            await pg.fill("#rptSearch", ""); await pg.wait_for_timeout(1000)

            print("STEP R6 G30-RPT-MAUSO — nút Năm")
            await pg.click("button:has-text('Năm')"); await pg.wait_for_timeout(2500)
            body = await pg.locator("body").inner_text()
            ok(f"Năm {mon.year}" in body and "43,8%" in body and "81,3%" in body, "R6 Năm: nhãn Năm + A 81,3% / B 43,8%", body[:600])
            await pg.screenshot(path=f"{OUT}_g30_r6.png", full_page=True)
        except Exception as e:
            ok(False, "R! lỗi khi bấm", repr(e)); await pg.screenshot(path=f"{OUT}_g29_rerr.png", full_page=True)

        try:
            print("STEP M1 G31-HSM-24H — Hiệu suất máy tuần, 24h không ca")
            await pg.goto(BASE + "/hieu-suat-may"); await pg.wait_for_timeout(3000)
            await pg.click("button:has-text('Tuần')"); await pg.wait_for_timeout(3000)
            body = await pg.locator("body").inner_text()
            ok(await pg.locator("#kyCa").count() == 0 and "máy không chia ca" in body and "Xưởng không ai khai WTS" in body,
               "M1 [G31] không còn ô Ca; có 'máy không chia ca' + phần 'Xưởng không ai khai WTS'", body[:600])
            rows = await pg.locator("table:has(th:has-text('Chạy / dừng / rảnh')) tbody tr").all_inner_texts()
            ra = [r for r in rows if "M29A" in r]; rb = [r for r in rows if "M29B" in r]
            pa = pct_set(420, mon); pb = pct_set(450, mon)
            ok(len(ra) == 1 and any(x in ra[0] for x in pa) and "50,0%" in ra[0], "M1 [G31] M29A chạy 420 ÷ (phút từ thứ Hai tới bây giờ; cả tuần đã qua = 10.080 → 4,2%); 420 / 840 = 50,0% lúc xưởng có người", (ra, sorted(pa)))
            ok(len(rb) == 1 and any(x in rb[0] for x in pb) and "53,6%" in rb[0] and "Kẹt dao" in rb[0] and "60" in rb[0], "M1 [G31] M29B 450 ÷ phút tới bây giờ, 53,6% lúc có người, dừng 60 Kẹt dao", (rb, sorted(pb)))
            ia = next((i for i, r in enumerate(rows) if "M29A" in r), -1); ib = next((i for i, r in enumerate(rows) if "M29B" in r), -1)
            ok(0 <= ia < ib, "M1 xếp % chạy thấp → cao: M29A trước M29B", (ia, ib))
            await pg.screenshot(path=f"{OUT}_g31_m1.png", full_page=True)

            print("STEP M2 G29-TIMNHANH — tìm 'Kẹt dao'")
            await pg.fill("#kySearch", "Kẹt dao"); await pg.wait_for_timeout(1200)
            rows = await pg.locator("table:has(th:has-text('Chạy / dừng / rảnh')) tbody tr").all_inner_texts()
            ok(len(rows) == 1 and "M29B" in rows[0], "M2 chỉ còn M29B", rows)
            await pg.fill("#kySearch", ""); await pg.wait_for_timeout(800)

            print("STEP M5 G31-LOC-NHOM — ô Bộ phận = GC")
            n_all = await pg.locator("table:has(th:has-text('Chạy / dừng / rảnh')) tbody tr").count()
            await pg.select_option("#kyBoPhan", value="GC"); await pg.wait_for_timeout(1200)
            rows = await pg.locator("table:has(th:has-text('Chạy / dừng / rảnh')) tbody tr").all_inner_texts()
            body = await pg.locator("body").inner_text()
            ok(len(rows) >= 2 and len(rows) < n_all and all("\tGC\t" in r or "\tGC\n" in r for r in rows) and any("M29A" in r for r in rows) and not any("M29T" in r for r in rows) and "Bộ phận: GC" in body,
               "M5 [G31] chọn Bộ phận GC: bỏ M29T (TARO), còn máy GC (có M29A / M29B) + nhãn 'Bộ phận: GC'", (n_all, rows[:6]))
            await pg.select_option("#kyBoPhan", value=""); await pg.wait_for_timeout(800)

            print("STEP M3 G29-HSM-KY — bấm M29B")
            await pg.locator("button.btn-link", has_text="M29B").first.click(); await pg.wait_for_timeout(1200)
            body = await pg.locator("body").inner_text()
            dr = await pg.locator("table:has(th:has-text('Người báo')) tbody tr").all_inner_texts()
            ok(any("Kẹt dao" in r and "60" in r for r in dr), "M3 Các lần dừng: Kẹt dao 60", dr)
            wr = await pg.locator("table:has(th:has-text('Phút đứng máy')) tbody tr").all_inner_texts()
            ok(any("A29" in r and "360" in r for r in wr) and any("B29" in r and "210" in r for r in wr), "M3 Ai đứng máy: A 360, B 210", wr)
            ok("Rảnh lúc xưởng có người" in body and "330 ph" in body, "M3 [G31] M29B rảnh lúc xưởng có người 330 ph (840 − 450 − 60)", body[:800])
            await pg.screenshot(path=f"{OUT}_g31_m3.png", full_page=True)

            print("STEP M4 G31-HSM-24H — bấm ô M29A ngày 1 → trang Ngày 00:00–24:00")
            await pg.click("button:has-text('Tất cả máy')"); await pg.wait_for_timeout(800)
            row = pg.locator("table:has(th:has-text('Cả kỳ')) tbody tr", has_text="M29A").first
            await row.locator("button").first.click(); await pg.wait_for_timeout(3000)
            dv = await pg.locator("input[type=date]").first.input_value()
            body = await pg.locator("body").inner_text()
            ok(dv == mon.isoformat(), "M4 trang Ngày mở đúng ngày 1", dv)
            ok("12.5%" in body and "00:00–24:00" in body and "Ca làm việc" not in body and "Toàn ngày" not in body,
               "M4 [G31] M29A 180 / 1440 = 12.5% (trước 37.5% trên ca); khung 00:00–24:00, không còn nút ca", body[:700])
            await pg.screenshot(path=f"{OUT}_g31_m4.png", full_page=True)

            print("STEP M6 G31-HSM-24H — nút Năm")
            await pg.click("button:has-text('Năm')"); await pg.wait_for_timeout(4000)
            body = await pg.locator("body").inner_text()
            ok(f"Năm {mon.year}" in body and "Máy × tháng" in body and "M29A" in body, "M6 [G31] Năm: nhãn 'Năm yyyy', bảng Máy × tháng", body[:600])
            await pg.screenshot(path=f"{OUT}_g31_m6.png", full_page=True)

            print("STEP R7 G31-LOC-NHOM — WTS Report ô Nhóm (thêm người nhóm KCS)")
            st2 = post("/harness/g31-seed"); ok(st2.get("ok"), "Seed G31", st2)
            await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(3000)
            await pg.click("button:has-text('WTS Report')"); await pg.wait_for_timeout(2500)
            body = await pg.locator("body").inner_text()
            ok("Công nhân K31" in body, "R7 tất cả nhóm: có Công nhân K31 (KCS)", body[:400])
            await pg.select_option("#rptGroup", label="Gia công"); await pg.wait_for_timeout(1500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            ok(k[:4] == ["1.440", "990", "60", "390"] and "Công nhân K31" not in body and "Nhóm: Gia công" in body,
               "R7 [G31] Nhóm Gia công: chỉ A29 + B29, giờ chuẩn giữ 1.440 / 990 / 60 / 390 (lọc theo người, không mất chưa khai báo)", (k, body[:300]))
            await pg.select_option("#rptGroup", label="KCS"); await pg.wait_for_timeout(1500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            ok(k[:4] == ["480", "0", "60", "420"] and "Công nhân K31" in body and "Công nhân A29" not in body,
               "R7 [G31] Nhóm KCS: chỉ K31 — giờ chuẩn 480 (làm 60 + chưa khai báo 420, ca lấy từ nghỉ đã lưu), vô ích 60", (k, body[:300]))
        except Exception as e:
            ok(False, "M! lỗi khi bấm", repr(e)); await pg.screenshot(path=f"{OUT}_g29_merr.png", full_page=True)
        await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
