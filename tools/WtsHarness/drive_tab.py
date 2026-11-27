# [RPT-TAB-MOI 27/11/2026] drive_tab.py — WTS Report: bấm tên người / "Xem ngày" / thanh ngày mở TAB MỚI đúng màn; mỗi màn có địa chỉ riêng.
#   Nguồn: HÙNG 27/11 06:30 + 06:33 "click vào thì nó mở ra trang mới" + 06:36 "bạn sửa đi"; rà độc lập 27/11 (T1 bc=abc, T2 đọc lại địa chỉ cũ).
#   (/hieu-suat-cong-nhan?bc=1&ky=&ngay= | tu= & den=&cn=&nhom=&tim=), F5 giữ màn, tab cũ giữ nguyên. Dữ liệu Stubs/HarnessG29.cs (như drive_g29:
#   ca G29 08–17 nghỉ 12–13; ngày 1 = thứ Hai tuần này, ngày 2 = thứ Ba; A29 tuần 960 / 780 / 30 / 0 / 150; A29 ngày 1 480 / 300 / 30 / 0 / 150).
#   STEP T1 mở thẳng địa chỉ ?bc=1&ky=day&ngay=<thứ Hai> → tab WTS Report, "Thứ 2 · dd/MM/yyyy", tất cả công nhân ngày 1 960 / 510 / 60 / 0 / 390
#           (A 480 / 300 / 30 / 150 + B 480 / 210 / 30 / 240); rồi ?bc=1&ky=week&ngay=<thứ Hai> → tuần 1.440 / 990 / 60 / 0 / 390
#   STEP T10 ?bc=abc / ?bc=1) → trang không lỗi (tab Nhập WTS); ?bc=1&cn=999999 → tất cả + "Không có công nhân này"; tim=<b>x hiện như chữ
#   STEP T11 tuần, bấm "Tự chọn" (chưa Xem báo cáo) → link tên A29 vẫn mang ky=week (kỳ đang hiện)
#   STEP T12 tab A29 bấm "Tất cả công nhân" → địa chỉ bỏ cn
#   STEP T2 bấm "Công nhân A29" ở bảng người → TAB MỚI: địa chỉ cn=<id> ky=week, ô số A29 960 / 780 / 30 / 0 / 150, "Tất cả công nhân ›";
#           tab cũ vẫn tất cả công nhân, địa chỉ không đổi
#   STEP T3 tab A29 bấm "Xem ngày" ngày 1 → TAB MỚI ky=day ngay=<thứ Hai> cn=<id>: 480 / 300 / 30 / 0 / 150 + dòng thời gian; tab A29 vẫn Tuần
#   STEP T4 tab A29 bấm thanh ngày 1 (khối thanh từng ngày) → TAB MỚI cùng ngày (480 …)
#   STEP T5 F5 tab ngày → vẫn ngày 1 của A29 (480 …)
#   STEP T6 tab tất cả: chọn Tháng → địa chỉ ky=month ngay=<ngày 1 tháng>; gõ tìm "HOP29" → địa chỉ có tim=HOP29; F5 → ô tìm vẫn "HOP29", vô ích 30
#   STEP T7 bấm tab "Nhập WTS" → địa chỉ bỏ bc; bấm lại "WTS Report" → tuần này mặc định, ô tìm TRỐNG (không đọc lại tim=HOP29 của lần mở đầu);
#           F5 sau khi về Nhập WTS → tab Nhập WTS (không mở báo cáo)
#   STEP T9 địa chỉ Tự chọn ?bc=1&ky=custom&tu=<thứ Hai>&den=<thứ Ba> → ô Từ / Đến đúng ngày, 1.440 …; link tên A29 mang ky=custom + tu + den
#   STEP T8 địa chỉ rác (?bc=1&ky=xyz&ngay=abc&cn=999999&nhom=<b>&tu=2026-99-01) → báo cáo mặc định tuần này, tất cả công nhân, không lỗi
#   Bản trước gói (5e9bf2b): tên người là nút (không mở tab mới), không đọc địa chỉ → T1 / T2 / T3 … SAI là đúng.
#   Nguồn: HÙNG 27/11 06:30 + 06:33 "click vào thì nó mở ra trang mới" + 06:36 "bạn sửa đi".
# Chạy trên harness VỪA khởi động: python3 drive_tab.py <port> <nhãn>
import sys, asyncio, json, re, urllib.request, urllib.parse, datetime
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
async def kpis(pg):
    return [x.strip() for x in await pg.locator("div.row-cols-md-6 .fs-4").all_inner_texts()]
def qs(url): return {k: v[0] for k, v in urllib.parse.parse_qs(urllib.parse.urlparse(url).query).items()}
async def settle(pg, ms=2500):
    await pg.wait_for_load_state("networkidle"); await pg.wait_for_timeout(ms)

async def main():
    st = post("/harness/g29-seed")
    ok(st.get("ok"), "Seed G29", st)
    mon = datetime.date.fromisoformat(st.get("monday", "2000-01-01"))
    lbl = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"][mon.weekday()] + " " + mon.strftime("%d/%m")
    async with async_playwright() as p:
        br = await p.chromium.launch(); ctx = await br.new_context(viewport={"width": 1900, "height": 1300}); pg = await ctx.new_page()
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        try:
            print("STEP T1 mở thẳng địa chỉ ngày rồi tuần")
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=day&ngay={mon.isoformat()}"); await settle(pg, 3500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            ok(k[:5] == ["960", "510", "60", "0", "390"] and f"Thứ 2 · {mon:%d/%m/%Y}" in body, "T1 ?bc=1&ky=day&ngay=thứ Hai → ngày 1 tất cả 960 / 510 / 60 / 0 / 390", (k, body[:300]))
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=week&ngay={mon.isoformat()}"); await settle(pg, 3500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            ok(k == ["1.440", "990", "60", "0", "390", "251 / 1"] and "Tuần " in body and "Công nhân A29" in body and "Công nhân B29" in body,
               "T1 địa chỉ ?bc=1&ky=week → tab WTS Report tuần, tất cả công nhân 1.440 / 990 / 60 / 0 / 390", (k, body[:300]))
            url1 = pg.url

            print("STEP T2 bấm tên A29 → tab mới")
            link = pg.locator("table a", has_text="Công nhân A29").first
            ok(await link.count() == 1 and (await link.get_attribute("target")) == "_blank", "T2 tên người là link mở tab mới (target=_blank)")
            async with ctx.expect_page(timeout=8000) as pi: await link.click()
            pa = await pi.value; await settle(pa, 3500)
            q = qs(pa.url); k = await kpis(pa); ba = await pa.locator("body").inner_text()
            aid = q.get("cn", "")
            ok(q.get("bc") == "1" and q.get("ky") == "week" and q.get("ngay") == mon.isoformat() and aid.isdigit(),
               "T2 địa chỉ tab mới: bc=1, ky=week, ngay=thứ Hai, cn=<id A29>", pa.url)
            ok(k[:5] == ["960", "780", "30", "0", "150"] and re.search(r"Tất cả công nhân\s*›\s*Công nhân A29", ba) is not None,
               "T2 tab mới: A29 tuần 960 / 780 / 30 / 0 / 150, đường dẫn Tất cả công nhân › A29", (k, ba[:300]))
            k0 = await kpis(pg); b0 = await pg.locator("body").inner_text()
            ok(pg.url == url1 and k0[0] == "1.440" and "Công nhân B29" in b0, "T2 tab cũ giữ nguyên: tất cả công nhân, địa chỉ không đổi", (pg.url, k0))

            print("STEP T3 tab A29 bấm Xem ngày ngày 1 → tab mới")
            head = pa.locator("div.rounded.border.small", has_text=lbl).first
            xem = head.locator("a", has_text="Xem ngày")
            async with ctx.expect_page(timeout=8000) as pi: await xem.click()
            pd = await pi.value; await settle(pd, 3500)
            q = qs(pd.url); k = await kpis(pd); bd = await pd.locator("body").inner_text()
            ok(q.get("ky") == "day" and q.get("ngay") == mon.isoformat() and q.get("cn") == aid, "T3 địa chỉ: ky=day, ngay=thứ Hai, cn=A29", pd.url)
            ok(k[:5] == ["480", "300", "30", "0", "150"] and "Dòng thời gian trong ngày" in bd, "T3 tab ngày: 480 / 300 / 30 / 0 / 150 + dòng thời gian", (k, bd[:300]))
            ka = await kpis(pa)
            ok(ka[0] == "960" and qs(pa.url).get("ky") == "week", "T3 tab A29 vẫn Tuần (960)", (pa.url, ka))

            print("STEP T4 tab A29 bấm thanh ngày 1 → tab mới")
            bar = pa.locator("a:has-text('/ 480')", has_text=lbl).first
            async with ctx.expect_page(timeout=8000) as pi: await bar.click()
            pb = await pi.value; await settle(pb, 3500)
            k = await kpis(pb)
            ok(qs(pb.url).get("ky") == "day" and k[:5] == ["480", "300", "30", "0", "150"], "T4 thanh ngày mở tab mới đúng ngày 1 (480 …)", (pb.url, k))
            await pb.close()

            print("STEP T5 F5 tab ngày")
            await pd.reload(); await settle(pd, 3500)
            k = await kpis(pd); bd = await pd.locator("body").inner_text()
            ok(k[:5] == ["480", "300", "30", "0", "150"] and "Công nhân A29" in bd, "T5 F5 vẫn ngày 1 của A29", (k, bd[:200]))
            await pd.close()

            print("STEP T12 tab A29 bấm Tất cả công nhân")
            await pa.locator("button", has_text="Tất cả công nhân").first.click(); await settle(pa, 1500)
            q = qs(pa.url); k = await kpis(pa)
            ok("cn" not in q and q.get("ky") == "week" and k[0] == "1.440", "T12 về tất cả công nhân → địa chỉ bỏ cn, 1.440", (pa.url, k))
            await pa.close()

            print("STEP T6 tab tất cả: Tháng + tìm HOP29 → địa chỉ, F5")
            await pg.click("button:has-text('Tháng')"); await settle(pg, 2500)
            q = qs(pg.url)
            ok(q.get("ky") == "month" and q.get("ngay") == mon.replace(day=1).isoformat(), "T6 bấm Tháng → địa chỉ ky=month, ngay=ngày 1 tháng của thứ Hai", pg.url)
            km = await kpis(pg); await pg.reload(); await settle(pg, 3500)
            ok(f"Tháng {mon:%m/%Y}" in await pg.locator("body").inner_text() and await kpis(pg) == km, "T6 F5 kỳ Tháng: vẫn Tháng MM/yyyy, ô số như trước F5", km)
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=week&ngay={mon.isoformat()}"); await settle(pg, 3500)
            await pg.fill("#rptSearch", "HOP29"); await pg.locator("#rptSearch").dispatch_event("change"); await settle(pg, 1500)
            ok(qs(pg.url).get("tim") == "HOP29", "T6 gõ tìm HOP29 → địa chỉ có tim=HOP29", pg.url)
            await pg.reload(); await settle(pg, 3500)
            k = await kpis(pg); v = await pg.locator("#rptSearch").input_value()
            ok(v == "HOP29" and len(k) > 2 and k[2] == "30", "T6 F5: ô tìm vẫn HOP29, vô ích 30", (v, k))
            await pg.fill("#rptSearch", ""); await pg.locator("#rptSearch").dispatch_event("change"); await settle(pg, 1200)
            ok("tim" not in qs(pg.url), "T6 xoá tìm → địa chỉ bỏ tim", pg.url)

            print("STEP T7 tab Nhập WTS")
            await pg.click("button:has-text('Nhập WTS')"); await settle(pg, 1500)
            ok("bc=" not in pg.url, "T7 về Nhập WTS → địa chỉ bỏ bc", pg.url)
            await pg.click("button:has-text('WTS Report')"); await settle(pg, 3000)
            v = await pg.locator("#rptSearch").input_value(); k = await kpis(pg)
            ok(v == "" and k[:5] == ["1.440", "990", "60", "0", "390"] and "tim" not in qs(pg.url), "T7 bấm lại WTS Report → tuần này, ô tìm trống (không đọc lại tim=HOP29 lúc mở trang)", (v, k, pg.url))
            await pg.click("button:has-text('Nhập WTS')"); await settle(pg, 1500)
            await pg.reload(); await settle(pg, 3500)
            act = await pg.locator("button.nav-link.active").all_inner_texts()
            ok(any("Nhập WTS" in a for a in act) and await pg.locator("div.row-cols-md-6").count() == 0, "T7 F5 → tab Nhập WTS, không mở báo cáo", act)

            print("STEP T9 địa chỉ Tự chọn")
            tue = mon + datetime.timedelta(days=1)
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=custom&tu={mon.isoformat()}&den={tue.isoformat()}"); await settle(pg, 3500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            fv = await pg.locator("#rptFrom").input_value(); tv = await pg.locator("#rptTo").input_value()
            ok(k[:5] == ["1.440", "990", "60", "0", "390"] and fv == mon.isoformat() and tv == tue.isoformat(), "T9 Tự chọn thứ Hai – thứ Ba: 1.440 / 990 / 60 / 0 / 390, ô Từ / Đến đúng ngày", (k, fv, tv))
            href = await pg.locator("table a", has_text="Công nhân A29").first.get_attribute("href") or ""
            q = qs(BASE + href)
            ok(q.get("ky") == "custom" and q.get("tu") == mon.isoformat() and q.get("den") == tue.isoformat() and q.get("cn") == aid, "T9 link A29 mang ky=custom, tu, den, cn", href)

            print("STEP T8 địa chỉ rác")
            await pg.goto(BASE + "/hieu-suat-cong-nhan?bc=1&ky=xyz&ngay=abc&cn=999999&nhom=%3Cb%3E&tu=2026-99-01&tim=" + "x" * 300); await settle(pg, 3500)
            k = await kpis(pg); body = await pg.locator("body").inner_text()
            names = " ".join(await pg.locator("table a").all_inner_texts())
            ok(k[:5] == ["1.440", "990", "60", "0", "390"] and "Công nhân A29" in names and "Công nhân B29" in names and not [a for a in await pg.locator("div.alert-warning").all_inner_texts() if a.strip()],
               "T8 địa chỉ rác → tuần này, tất cả công nhân, không lỗi (tìm > 200 ký tự bỏ qua)", (k, body[:300]))
            await pg.screenshot(path=f"{OUT}_tab_t8.png", full_page=True)

            print("STEP T10 bc lạ, cn lạ, tim có thẻ HTML")
            for bad in ("abc", "1)"):
                await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc={bad}"); await settle(pg, 3000)
                body = await pg.locator("body").inner_text()
                act = await pg.locator("button.nav-link.active").all_inner_texts()
                ok(any("Nhập WTS" in a for a in act) and "lỗi không mong muốn" not in body.lower() and await pg.locator("#blazor-error-ui:visible").count() == 0,
                   f"T10 ?bc={bad} → trang chạy bình thường, tab Nhập WTS", (act, body[:200]))
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=week&ngay={mon.isoformat()}&cn=999999"); await settle(pg, 3500)
            body = await pg.locator("body").inner_text(); k = await kpis(pg)
            ok(k[0] == "1.440" and "Không có công nhân này" in body, "T10 cn=999999 → tất cả công nhân + báo Không có công nhân này", (k, body[:300]))
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=week&ngay={mon.isoformat()}&tim=" + urllib.parse.quote("<b>x")); await settle(pg, 3500)
            ok(await pg.locator("#rptSearch").input_value() == "<b>x" and await pg.locator("div.card b:text-is('x')").count() == 0, "T10 tim=<b>x hiện như chữ (không thành thẻ)")

            print("STEP T11 bấm Tự chọn chưa Xem báo cáo → link mang kỳ đang hiện")
            await pg.goto(BASE + f"/hieu-suat-cong-nhan?bc=1&ky=week&ngay={mon.isoformat()}"); await settle(pg, 3500)
            await pg.click("button:has-text('Tự chọn')"); await settle(pg, 1000)
            href = await pg.locator("table a", has_text="Công nhân A29").first.get_attribute("href") or ""
            q = qs(BASE + href)
            ok(q.get("ky") == "week" and q.get("ngay") == mon.isoformat(), "T11 link A29 mang ky=week (kỳ đang hiện, chưa bấm Xem báo cáo)", href)
        except Exception as e:
            ok(False, "T! lỗi khi bấm", repr(e)); await pg.screenshot(path=f"{OUT}_tab_err.png", full_page=True)
        await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
