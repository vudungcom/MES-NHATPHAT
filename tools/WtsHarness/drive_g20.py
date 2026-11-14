# [GÓI 20 12/11/2026][G19-HUYHIEU-NHAN][G19-VITRI-OFF][G19-KIEM-THEM] Vá 3 điểm Thấp ghi nhớ Gói 19 + ca kiểm còn thiếu — bấm trang thật (harness EF giả).
#   Nguồn: CLAUDE rà độc lập Gói 19 12/11; HÙNG 12/11 20:02 (giao vá 3 điểm Thấp + ca kiểm phiếu bù / nhãn ô Hàng lỗi đã xử lý).
#   L (admin, trang Giao nhận, dữ liệu POST /harness/g20-seed — PO-G20, xem Stubs/HarnessG20.cs):
#     L8  Lịch sử phiếu Cũ → mới: phiếu bù xếp chung với phiếu / báo lỗi / Kết quả sửa đúng giờ lập (ca kiểm mới — Gói 19 chưa có phiếu bù)
#     L9  phiếu bù đã huỷ ghi "ĐÃ HUỶ" + lý do; phiếu bù còn hiệu lực không ghi
#     L10 lọc mã vụ V1 → đủ mục của V1 gồm 2 phiếu bù; lọc vụ khác → không phiếu bù nào
#     L11 phiếu bên nhận đổi ô (Hàng sửa → Hàng lỗi): huy hiệu ghi thêm "(nhận thật: ô Hàng lỗi)" (trước: chỉ ghi ô bên giao chỉ định); phiếu không đổi ô không có chữ đó
#     L11b lần nhận đổi ô đã huỷ, nhận lại đúng ô → không ghi "nhận thật" · L11c nhận lẫn 2 ô → "(có lần nhận vào ô Hàng lỗi)" · L11d huỷ lần đúng ô, còn lần đổi ô → "nhận thật"
#     L12 phiếu từ ô Hàng lỗi đã xử lý giao tiếp (Gia công → HTSP nhóm giữa, HTSP → KCS nơi phát hiện): nhãn "Giao · Hàng lỗi đã xử lý (đã sửa đạt)" + "nhận vào ô Hàng lỗi đã xử lý" (ca kiểm mới)
#     L13 phiếu từ ô Hàng lỗi đã xử lý trả lại ô Hàng lỗi: nhãn "Giao · Hàng lỗi đã xử lý (trả lại — chưa đạt)" + "nhận vào ô Hàng lỗi" (ca kiểm mới)
#   C [G20-CA-SHOWNCELL 2026-11-14] (PO-G21C, phiếu cũ không ghi ô — ghi thẳng DB): L11e 1 lần không ô + 1 lần ô Hàng lỗi → "(có lần nhận vào ô Hàng lỗi)";
#     L11f chỉ lần không ô → không chữ. Đột biến bỏ "?? shownCell" phải làm L11e SAI. Nguồn: CLAUDE (sổ G20-GHI-NHO mục 2); HÙNG 14/11 08:28 OK Gói 21.
#   V (gc01, trang WTS): nút "hàng đang ở đâu"
#     V1 PO-G20F (Workflow OFF, có ô âm) → khung ghi "PO tắt Workflow — ô âm có thể do nhập / giao vượt (chỉ cảnh báo, không chặn)" (trước: không nói, chỉ "có ô âm")
#     V3 PO-G20G (Workflow OFF nhưng cân, không ô âm) → không có chữ đó · V5 PO-G20L (OFF, khung lệch, không ô âm — ghi thẳng DB) → đỏ "lệch", không chữ đó · V4 PO-G20H (ô âm từ lúc OFF, nay ON) → "có ô âm", không chữ đó · V2 PO-G20 (Workflow ON) → không có chữ đó
# Chạy trên harness VỪA khởi động: python3 drive_g20.py <port> <nhãn>   (bản Gói 19 486fa57 phải SAI ở L11 / V1; L8–L10, L12, L13 = ca kiểm mới, bản cũ cũng ĐẠT)
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))

async def login(b, user, pw, url):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + url); await pg.wait_for_timeout(1800)
    return pg

LOG_JS = """m => { const c = m.querySelector('div[style="display:flex;flex-direction:column"]'); if (!c) return null;
  return Array.from(c.children).map(e => ({ y: e.getBoundingClientRect().top, t: e.innerText.replace(/\\s+/g, ' ').slice(0, 500) })).sort((a, b) => a.y - b.y).map(x => x.t); }"""

def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
async def vitri_text(pg, po):
    await ensure_group(pg); g = grp(pg)
    row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await pg.wait_for_timeout(900)
    vb = row.locator("button:has-text('hàng đang ở đâu')"); n = await vb.count()
    if not n: return None
    await vb.first.click(); await pg.wait_for_timeout(1500)
    if not await pg.locator(".mes-modal").count(): return ""
    t = await pg.locator(".mes-modal").last.inner_text()
    await pg.locator(".mes-modal").last.locator("button:has-text('Đóng')").click(); await pg.wait_for_timeout(600)
    return t

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        sd = post("/harness/g20-seed")
        print(f"seed {sd}")
        ok(sd.get("balanced") is True and sd.get("negs") == 0, "S0 dữ liệu seed PO-G20 (Workflow ON) cân, không ô âm", sd)

        print("[L] Lịch sử phiếu (admin): phiếu bù theo giờ, huy hiệu ô nhận thật, nhãn ô Hàng lỗi đã xử lý")
        try:
            pg = await login(b, "admin", "admin123", "/handover"); await pg.wait_for_timeout(3500)
            await pg.locator("tr", has_text="PO-G20").first.locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
            m = pg.locator(".mes-modal").last
            items = await m.evaluate(LOG_JS) or []
            seq = sd["seq"]
            ok(len(items) == len(seq) and all(x.startswith(k) for x, k in zip(items, seq)) and sum(k.startswith("Phiếu bù") for k in seq) == 2,
               "L8 Cũ → mới: 2 phiếu bù xếp chung với phiếu / báo lỗi / Kết quả sửa đúng giờ lập", {"seq": seq, "trang": [x[:40] for x in items]})
            bus = [x for x in items if x.startswith("Phiếu bù")]
            vb = [x for x in bus if f"Phiếu bù #{sd['buVoid'][0]} " in x] if sd["buVoid"] else []
            va = [x for x in bus if x not in vb]
            ok(len(vb) == 1 and "ĐÃ HUỶ" in vb[0] and "nhập nhầm" in vb[0] and len(va) == 1 and "ĐÃ HUỶ" not in va[0],
               "L9 phiếu bù đã huỷ ghi ĐÃ HUỶ + lý do \"nhập nhầm\"; phiếu bù còn hiệu lực không ghi", bus)
            sel = m.locator("select", has=pg.locator("option", has_text="Tất cả")).first
            opts = await sel.evaluate("s => Array.from(s.options).map(o => o.text)") if await sel.count() else []
            it1 = it2 = None
            if sd["v1"] in opts and len(sd["cases"]) > 1 and sd["cases"][1] in opts:
                await sel.select_option(label=sd["v1"]); await pg.wait_for_timeout(900); it1 = await m.evaluate(LOG_JS)
                await sel.select_option(label=sd["cases"][1]); await pg.wait_for_timeout(900); it2 = await m.evaluate(LOG_JS)
                await sel.select_option(label="Tất cả"); await pg.wait_for_timeout(900)
            ok(it1 is not None and len(it1) == sd["nV1"] and sum(x.startswith("Phiếu bù") for x in it1) == 2 and all(sd["v1"] in x for x in it1),
               f"L10 lọc {sd['v1']} → {sd['nV1']} mục gồm 2 phiếu bù, mục nào cũng có mã vụ đó", it1)
            ok(it2 is not None and len(it2) == sd["nV2"] and not any(x.startswith("Phiếu bù") for x in it2),
               f"L10b lọc vụ khác → {sd['nV2']} mục, không phiếu bù nào", it2)
            doi = [x for x in items if "gửi nhờ sửa" in x and "giữ ô hàng lỗi chờ xem" in x]
            huy = [x for x in items if "phieu-huy-nhan" in x]
            lan = [x for x in items if "phieu-nhan-lan" in x]
            hdo = [x for x in items if "phieu-huy-dung-o" in x]
            khac = [x for x in items if x.startswith("#") and x not in doi and x not in lan and x not in hdo]
            ok(len(doi) == 1 and "→ nhận vào ô Hàng sửa" in doi[0] and "(nhận thật: ô Hàng lỗi)" in doi[0] and not any("nhận thật" in x for x in khac),
               "L11 bên nhận đổi ô Hàng sửa → Hàng lỗi: huy hiệu ghi thêm \"(nhận thật: ô Hàng lỗi)\"; phiếu không đổi ô không có chữ đó",
               doi + [x for x in khac if "nhận thật" in x][:2])
            ok(len(huy) == 1 and "Đã huỷ" in huy[0] and "đổi từ Hàng sửa" in huy[0] and "nhận thật" not in huy[0] and "có lần nhận vào" not in huy[0],
               "L11b lần nhận đổi ô đã HUỶ, nhận lại đúng ô → huy hiệu không ghi \"nhận thật\" / \"có lần nhận vào\"", huy)
            ok(len(lan) == 1 and "(có lần nhận vào ô Hàng lỗi)" in lan[0] and "nhận thật" not in lan[0],
               "L11c nhận lẫn ô (1 đúng ô Hàng sửa + 1 đổi ô Hàng lỗi) → \"(có lần nhận vào ô Hàng lỗi)\", không ghi \"nhận thật\"", lan)
            ok(len(hdo) == 1 and "(nhận thật: ô Hàng lỗi)" in hdo[0] and "có lần nhận vào" not in hdo[0],
               "L11d lần nhận đúng ô đã HUỶ, lần còn hiệu lực đổi ô Hàng lỗi → \"(nhận thật: ô Hàng lỗi)\" (không \"có lần nhận vào\")", hdo)
            dat = [x for x in items if "Giao · Hàng lỗi đã xử lý (đã sửa đạt)" in x]
            ok(len(dat) == 2 and any("Gia công → HTSP" in x for x in dat) and any("HTSP → KCS" in x for x in dat) and all("nhận vào ô Hàng lỗi đã xử lý" in x for x in dat),
               "L12 Gia công → HTSP và HTSP → KCS từ ô Hàng lỗi đã xử lý: nhãn \"Hàng lỗi đã xử lý (đã sửa đạt)\", nhận vào ô Hàng lỗi đã xử lý", [x for x in items if "đã xử lý" in x][:4])
            tl = [x for x in items if "Giao · Hàng lỗi đã xử lý (trả lại — chưa đạt)" in x]
            ok(len(tl) == 1 and "HTSP → Gia công" in tl[0] and re.search(r"nhận vào ô Hàng lỗi(?! đã xử lý)", tl[0]) is not None and "nhận vào ô Hàng lỗi đã xử lý" not in tl[0],
               "L13 HTSP trả lại Gia công từ ô Hàng lỗi đã xử lý: nhãn \"Hàng lỗi đã xử lý (trả lại — chưa đạt)\", nhận vào ô Hàng lỗi", [x for x in items if "trả lại" in x][:3])
        except Exception as e:
            ok(False, "L! pha [L] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        # [G20-CA-SHOWNCELL 2026-11-14] phiếu CŨ không ghi ô (PO-G21C, ghi thẳng DB): lần nhận không ô = ô đang hiện (Hàng mới)
        print("[C] Lịch sử phiếu PO-G21C — phiếu cũ không ghi ô")
        try:
            pg = await login(b, "admin", "admin123", "/handover"); await pg.wait_for_timeout(3500)
            await pg.locator("tr", has_text="PO-G21C").first.locator("button[title='Lịch sử phiếu']").first.click(); await pg.wait_for_timeout(2500)
            items = await pg.locator(".mes-modal").last.evaluate(LOG_JS) or []
            ca = [x for x in items if "phieu-cu-lan" in x]
            cb = [x for x in items if "phieu-cu-khong-o" in x]
            ok(len(ca) == 1 and "→ nhận vào ô Hàng mới" in ca[0] and "(có lần nhận vào ô Hàng lỗi)" in ca[0] and "nhận thật" not in ca[0],
               "L11e phiếu cũ không ghi ô: 1 lần nhận không ô + 1 lần ô Hàng lỗi → \"(có lần nhận vào ô Hàng lỗi)\" (lần không ô tính là ô đang hiện, không ghi \"nhận thật\")", ca or items[:4])
            ok(len(cb) == 1 and "→ nhận vào ô Hàng mới" in cb[0] and "nhận thật" not in cb[0] and "có lần nhận vào" not in cb[0],
               "L11f phiếu cũ chỉ có lần nhận không ô → huy hiệu không có chữ thêm", cb or items[:4])
        except Exception as e:
            ok(False, "C! pha [C] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[V] trang WTS (gc01): khung Đang ở đâu với PO tắt Workflow")
        try:
            pg = await login(b, "gc01", "123456", "/hieu-suat-cong-nhan")
            await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
            tf = await vitri_text(pg, "PO-G20F")
            ok(tf is not None and "Hàng đang ở đâu — PO PO-G20F" in tf and "PO tắt Workflow — ô âm có thể do nhập / giao vượt (chỉ cảnh báo, không chặn)" in tf and "có ô âm" in tf and "cân:" in tf and "lệch:" not in tf,
               "V1 PO-G20F (Workflow OFF, có ô âm): khung ghi \"PO tắt Workflow — ô âm có thể do nhập / giao vượt (chỉ cảnh báo, không chặn)\", khung vẫn \"cân\"", (tf or "")[:500])
            tg = await vitri_text(pg, "PO-G20G")
            ok(tg is not None and "Hàng đang ở đâu — PO PO-G20G" in tg and "PO tắt Workflow" not in tg and "cân" in tg,
               "V3 PO-G20G (Workflow OFF nhưng cân, không ô âm): khung không có chữ PO tắt Workflow", (tg or "")[:300])
            tl = await vitri_text(pg, "PO-G20L")
            ok(tl is not None and "Hàng đang ở đâu — PO PO-G20L" in tl and "lệch:" in tl and "có ô âm" not in tl and "PO tắt Workflow" not in tl,
               "V5 PO-G20L (Workflow OFF, khung lệch, không ô âm): đỏ \"lệch\", KHÔNG có chữ PO tắt Workflow (không che lỗi lệch)", (tl or "")[:700].replace("\n", " | "))
            th = await vitri_text(pg, "PO-G20H")
            ok(th is not None and "Hàng đang ở đâu — PO PO-G20H" in th and "có ô âm" in th and "PO tắt Workflow" not in th,
               "V4 PO-G20H (ô âm có từ lúc OFF, nay Workflow ON): khung có \"có ô âm\", không có chữ PO tắt Workflow", (th or "")[:300])
            to = await vitri_text(pg, "PO-G20")
            ok(to is not None and "Hàng đang ở đâu — PO PO-G20\n" in to + "\n" and "SL PO 10" in to and "PO tắt Workflow" not in to,
               "V2 PO-G20 (Workflow ON): khung không có chữ PO tắt Workflow", (to or "")[:300])
        except Exception as e:
            ok(False, "V! pha [V] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])
        await b.close()
    print("KẾT QUẢ: ĐẠT hết" if fails == 0 else f"KẾT QUẢ: {fails} SAI")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
