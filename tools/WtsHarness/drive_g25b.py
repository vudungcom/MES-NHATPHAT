# [GÓI 25b 20/11/2026] drive_g25b.py — bấm trang WTS thật cho [WTS-SUA-NHANH]: Admin sửa nhanh dòng WTS nhóm KHÁC form (KCS của kcs01, ngày cũ)
#   + R-10 giữ ca + khoảng nghỉ dòng gốc. Nguồn: CLAUDE rà độc lập Gói 25 (R-01, R-10); HÙNG 20/11 18:12 + 18:32 → Gói 25b.
#   Dữ liệu: Stubs/HarnessG25.cs Seed25bAsync (POST /harness/g25b-seed): PO-G25B (Workflow OFF), dòng K1 của kcs01 HÔM QUA 11:30–13:30 OK 5, ca "Ca G25B"
#   (nghỉ trưa 12:00–13:00, làm thông 20 phút).
#   STEP Q1 [WTS-SUA-NHANH]  admin (form Gia công, đang chọn Ca 1) xem kcs01 ngày hôm qua → bấm "Sửa lại" dòng K1 → mở modal "Sửa nhanh WTS — KCS"
#                            (không phải lời "Dòng này của nhóm KCS…"). Bản cũ a57f112: không modal → SAI là đúng.
#   STEP Q2 [WTS-SUA-NHANH]  Lưu sửa KHÔNG lý do → lời "nhập lý do" trong modal, DB không đổi.
#   STEP Q3 [WTS-SUA-NHANH]  OK 5 → 4, kết thúc 13:30 → 14:00, lý do → lưu: dòng cũ huỷ "Sửa lại (quyền cao hơn): …" VoidedBy admin; dòng mới kcs01, KCS, K1,
#                            ngày hôm qua, OK 4, CaId = Ca G25B (R-10, không phải Ca 1 admin đang chọn), khoảng nghỉ trưa làm thông 20 giữ.
#   STEP Q4 [WTS-SUA-NHANH]  form thường (cùng nhóm Gia công) sửa dòng GC ngày cũ ca G25B của gc01: nhóm hiện 'Giữ ca của dòng: Ca G25B', lưu giữ ca + nghỉ (R25b-01).
# Chạy trên harness VỪA khởi động: python3 drive_g25b.py <port> <nhãn>
import sys, asyncio, json, urllib.request, datetime
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}
def state(): return json.loads(urllib.request.urlopen(BASE + "/harness/g25b").read().decode())
def row(st, id_): return next((w for w in st["wts"] if w["id"] == id_), None)

async def login(ctx, user, pw, wait=2500):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(wait)
    if await pg.locator("button:has-text('Ca 1')").count(): await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg
async def text(pg): return await pg.locator("body").inner_text()
async def set_val(pg, loc, val):
    await loc.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }", val)
    await pg.wait_for_timeout(1800)
def saved_rows(pg): return pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']"))

async def main():
    st = post("/harness/g25b-seed")
    ok(st.get("ok") and st.get("k1") and st.get("caB"), "Seed G25B (PO-G25B OFF, kcs01: K1 hôm qua OK 5, Ca G25B)", st)
    k1, kcs, adm, caB = st["k1"], st["kcs01"], st["admin"], st["caB"]
    yday = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
    async with async_playwright() as p:
        br = await p.chromium.launch()
        ctx = await br.new_context(); pg = await login(ctx, "admin", "admin123")

        print("STEP Q1 WTS-SUA-NHANH — admin bấm Sửa dòng KCS của kcs01 (ngày cũ) → modal Sửa nhanh")
        await pg.locator("input[type=date]").first.fill(yday); await pg.locator("input[type=date]").first.dispatch_event("change"); await pg.wait_for_timeout(1800)
        await set_val(pg, pg.locator("input[list='cuViewUsers']").first, "Công nhân KCS (kcs01)")
        rk = saved_rows(pg).filter(has_text="PO-G25B")
        ok(await rk.count() == 1 and "11:30" in await rk.first.inner_text(), "Q1 bảng dòng đã lưu có dòng K1 (11:30) của kcs01", await rk.count())
        await rk.first.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        body = await text(pg)
        ok("Sửa nhanh WTS — KCS" in body, "Q1 mở modal 'Sửa nhanh WTS — KCS'", body[:400])
        ok("Dòng này của nhóm KCS" not in body, "Q1 không còn lời 'Dòng này của nhóm KCS… xoá rồi nhập lại'")
        modal = pg.locator(".mes-modal", has_text="Sửa nhanh WTS")
        ok(await modal.locator("input[type=time]").count() == 2 and await modal.locator("input[type=number]").count() >= 3, "Q1 modal có ô Bắt đầu / Kết thúc / OK / NG / T.Thực tế")
        await pg.screenshot(path=f"g25b_{OUT}_q1.png", full_page=False)

        print("STEP Q2 WTS-SUA-NHANH — Lưu không lý do → chặn")
        nums = modal.locator("input[type=number]")
        await nums.nth(0).fill("4"); await nums.nth(0).dispatch_event("change")
        await modal.locator("input[type=time]").nth(1).fill("14:00"); await modal.locator("input[type=time]").nth(1).dispatch_event("change"); await pg.wait_for_timeout(400)
        await modal.locator("button:has-text('Lưu sửa')").click(); await pg.wait_for_timeout(2500)
        s1 = state()
        ok(not row(s1, k1)["isVoided"] and "nhập lý do" in await text(pg), "Q2 Lưu KHÔNG lý do: máy chủ chặn 'nhập lý do', dòng K1 còn", row(s1, k1))

        print("STEP Q3 WTS-SUA-NHANH — có lý do → lưu, giữ người làm / nhóm / mã / ngày / ca dòng gốc (R-10)")
        await modal.locator("input[placeholder^='Vd: tổ trưởng']").fill("kcs01 đếm thừa 1"); await modal.locator("input[placeholder^='Vd: tổ trưởng']").dispatch_event("change"); await pg.wait_for_timeout(300)
        await modal.locator("button:has-text('Lưu sửa')").click(); await pg.wait_for_timeout(3000)
        s2 = state(); old = row(s2, k1)
        news = [w for w in s2["wts"] if not w["isVoided"] and w["processGroup"] == "KCS"]
        ok(old["isVoided"] and old["voidReason"] == "Sửa lại (quyền cao hơn): kcs01 đếm thừa 1" and old["voidedBy"] == adm, "Q3 dòng cũ huỷ 'Sửa lại (quyền cao hơn): …', VoidedBy admin", old)
        nw = news[0] if len(news) == 1 else None
        ok(nw and nw["workerId"] == kcs and nw["processGroup"] == "KCS" and nw["wtsCode"] == "K1" and nw["qtyOk"] == 4 and nw["start"] == yday + " 11:30" and nw["end"] == yday + " 14:00",
           "Q3 dòng mới: người làm kcs01, KCS, K1, OK 4, 11:30–14:00 ngày hôm qua", news)
        brk = [b for b in s2["brk"] if nw and b["wtsLogId"] == nw["id"]]
        ok(nw and nw["caId"] == caB and len(brk) == 1 and brk[0]["workedMin"] == 20 and brk[0]["overlapMin"] == 60,
           "Q3 R-10: dòng mới giữ Ca G25B (admin đang chọn Ca 1) + nghỉ trưa chồng 60, làm thông 20 như dòng gốc", (nw, brk))
        ok(await pg.locator(".mes-modal", has_text="Sửa nhanh WTS").count() == 0 and "Đã sửa dòng WTS" in await text(pg), "Q3 modal đóng, khung báo 'Đã sửa dòng WTS'")
        await pg.screenshot(path=f"g25b_{OUT}_q3.png", full_page=False)

        print("STEP Q4 WTS-SUA-NHANH — form thường (cùng nhóm Gia công) sửa dòng GC ngày cũ của gc01 ca G25B: trang giữ ca dòng gốc (R25b-01)")
        g1, gcu = st["g1"], st["gc01"]
        await set_val(pg, pg.locator("input[list='cuViewUsers']").first, "Công nhân GC (gc01)")
        rg = saved_rows(pg).filter(has_text="PO-G25B")
        ok(await rg.count() == 1 and "11:00" in await rg.first.inner_text(), "Q4 bảng có dòng GC 11:00 của gc01", await rg.count())
        await rg.first.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1800)
        ok(await pg.locator(".mes-modal", has_text="Sửa nhanh WTS").count() == 0, "Q4 dòng cùng nhóm form → mở form nhóm (không modal)")
        ok("Giữ ca của dòng: Ca G25B" in await text(pg), "Q4 nhóm sửa hiện 'Giữ ca của dòng: Ca G25B' (admin đang chọn Ca 1)")
        await pg.locator("input[placeholder='≥ 3 ký tự']").fill("sửa ca G25B"); await pg.locator("input[placeholder='≥ 3 ký tự']").dispatch_event("change"); await pg.wait_for_timeout(300)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(3000)
        s4 = state(); og = row(s4, g1)
        ng4 = [w for w in s4["wts"] if not w["isVoided"] and w["processGroup"] == "GC"]
        brk4 = [b for b in s4["brk"] if ng4 and b["wtsLogId"] == ng4[0]["id"]]
        ok(og["isVoided"] and len(ng4) == 1 and ng4[0]["caId"] == caB and ng4[0]["workerId"] == gcu and len(brk4) == 1 and brk4[0]["overlapMin"] == 60,
           "Q4 lưu: dòng GC mới giữ Ca G25B + nghỉ trưa (trang + máy chủ cùng ca dòng gốc), người làm gc01", (og, ng4, brk4, (await text(pg))[-600:]))
        await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    with open(f"g25b_{OUT}.txt", "w") as f: f.write(f"{fails}\n")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
