# [D6-LS-VIEC 26/11/2026] Trang Giao nhận thật (harness EF giả) — mockup dòng 6 (khách tích Đúng 23/11): Lịch sử phiếu ghi việc theo tên nút mới.
#   Dữ liệu: POST /harness/d6-seed (Stubs/HarnessD6.cs) — 8 phiếu ghi thẳng bảng, trả { ids } theo thứ tự lập.
#   STEP A: mở Lịch sử phiếu PO-D6 → mỗi thẻ phiếu ghi đúng chữ việc:
#     A1 Gia công → Kho Hàng lỗi công đoạn trước: "Trả nơi làm lỗi" + "nhận vào ô Hàng lỗi — lỗi công đoạn trước ở NC NC01"
#     A2 Kho → HTSP gửi nhờ sửa: "Gửi bộ phận khác sửa" + "nhận vào ô Hàng sửa"
#     A3 HTSP → Gia công sửa đạt: "Sửa đạt — trả nơi báo lỗi" + "nhận vào ô Hàng lỗi đã xử lý"
#     A4 HTSP → Kho phần mềm tự trả chiếc sửa chưa đạt: "Sửa chưa đạt — trả nơi làm lỗi" + "nhận vào ô Hàng lỗi"
#     A5 HTSP → Gia công chuyển tiếp nhờ sửa (Hàng sửa → Hàng sửa): "Gửi bộ phận khác sửa"
#     A6 Kho huỷ: "Huỷ" · A7 Kho trả khách: "Trả khách"
#     A8 Kho → Gia công Hàng mới: giữ chữ "Giao · Hàng Mới"
#     A9 chữ ô cũ nằm ở chú thích (title) của nhãn việc; không thẻ nào còn chữ ô cũ trong nhãn
#   STEP B: lọc theo mã vụ / xếp theo giờ giữ như cũ (nút "Cũ → mới" có, thứ tự thẻ = thứ tự lập).
#   Bản cũ (6062682, trước G-G) phải SAI từ A1 (chữ cũ "Giao · Hàng lỗi ☑ công đoạn trước").
# Chạy trên harness VỪA khởi động: python3 drive_d6.py <port> <old|new>
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:700]) if info not in (None, "") else ""))
def porow(pg, po): return pg.locator("tr", has=pg.locator("td", has_text=re.compile(r"^\s*" + po + r"\s*$"))).first
LOG_JS = """m => { const c = m.querySelector('div[style="display:flex;flex-direction:column"]'); if (!c) return null;
  return Array.from(c.children).map(e => ({ y: e.getBoundingClientRect().top, t: e.innerText.replace(/\\s+/g, ' ').slice(0, 500),
    ti: Array.from(e.querySelectorAll('span.badge[title]')).map(b => b.getAttribute('title')).join(' | ') })).sort((a, b) => a.y - b.y); }"""

async def main():
    sd = post("/harness/d6-seed"); ids = sd["ids"]
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1300})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4500)

        print("[STEP A] Lịch sử phiếu PO-D6: chữ việc theo tên nút mới (mockup dòng 6)")
        try:
            await porow(pg, "PO-D6").locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
            m = pg.locator(".mes-modal").last
            items = await m.evaluate(LOG_JS) or []
            def card(i):
                k = f"#{ids[i]} "
                return next((x for x in items if x["t"].startswith(k)), None)
            exp = [
                (1, "A1 Gia công → Kho Hàng lỗi công đoạn trước", "Trả nơi làm lỗi", "nhận vào ô Hàng lỗi — lỗi công đoạn trước ở NC NC01"),
                (2, "A2 Kho → HTSP gửi nhờ sửa", "Gửi bộ phận khác sửa", "nhận vào ô Hàng sửa"),
                (3, "A3 HTSP → Gia công sửa đạt", "Sửa đạt — trả nơi báo lỗi", "nhận vào ô Hàng lỗi đã xử lý"),
                (4, "A4 HTSP → Kho phần mềm tự trả chiếc sửa chưa đạt", "Sửa chưa đạt — trả nơi làm lỗi", "nhận vào ô Hàng lỗi"),
                (5, "A5 HTSP → Gia công chuyển tiếp nhờ sửa (Hàng sửa → Hàng sửa)", "Gửi bộ phận khác sửa", "nhận vào ô Hàng sửa"),
                (6, "A6 Kho huỷ", "Huỷ", None),
                (7, "A7 Kho trả khách", "Trả khách", None),
                (0, "A8 Kho → Gia công Hàng mới (giữ chữ cũ)", "Giao · Hàng Mới", "nhận vào ô Hàng mới"),
            ]
            old = ["Giao · Hàng lỗi ☑ công đoạn trước", "Giao · Hàng lỗi (gửi nhờ sửa)", 
                   "Giao · Hàng Sửa (sửa chưa đạt — phần mềm tự trả nơi làm lỗi)", "Huỷ hàng lỗi", "Trả khách hàng lỗi"]
            for i, name, lab, rcv in exp:
                c = card(i); t = c["t"] if c else ""
                good = c is not None and lab in t and (rcv is None or rcv in t) and not any(o in t for o in old)
                if lab == "Huỷ": good = good and "Huỷ hàng lỗi" not in t
                ok(good, f"{name}: nhãn \"{lab}\"" + (f", \"{rcv}\"" if rcv else ""), t[:300])
            c4 = card(4); c1 = card(1)
            ok(c4 is not None and "sửa chưa đạt — phần mềm tự trả nơi làm lỗi" in c4["ti"] and c1 is not None and "Hàng lỗi ☑ công đoạn trước" in c1["ti"],
               "A9 chữ ô cũ ở chú thích (rê chuột) của nhãn việc", {"c1": c1 and c1["ti"], "c4": c4 and c4["ti"]})
        except Exception as e:
            ok(False, "A! lỗi bấm", e)

        print("[STEP B] Xếp theo giờ giữ như cũ")
        try:
            m = pg.locator(".mes-modal").last
            items = await m.evaluate(LOG_JS) or []
            seen = [int(re.match(r"#(\d+)", x["t"]).group(1)) for x in items if re.match(r"#\d+ ", x["t"])]
            tg = m.locator("button:has-text('Cũ → mới'), button:has-text('Mới → cũ')")
            ok(await tg.count() >= 1 and sorted(seen) == sorted(ids) and (seen == ids or seen == list(reversed(ids))),
               "B1 nút xếp theo giờ có; 8 thẻ phiếu theo đúng thứ tự lập", {"seen": seen, "ids": ids})
        except Exception as e:
            ok(False, "B! lỗi bấm", e)
        await b.close()
    print(f"KẾT QUẢ drive_d6 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
