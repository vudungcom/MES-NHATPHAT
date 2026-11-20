# [GÓI 25 19/11/2026] drive_g25.py — bấm trang WTS thật cho [L7-WTS-CU]: Admin / Sửa WTS_MASTER sửa-xoá WTS ngày cũ / của người khác (lý do ≥ 3, vẫn K5).
#   Nguồn: CLAUDE quét Fable 15/11 (A2-01 Trung); HÙNG 15/11 15:32 chốt A → Gói 25. Dữ liệu: Stubs/HarnessG25.cs (POST /harness/g25-seed):
#   PO-G25 ON, gc01 có dòng Y1 = NC01 OK 5 HÔM QUA, T1 = NC01 OK 3 HÔM NAY.
#   STEP V1 [L7-WTS-CU]  admin mở /hieu-suat-cong-nhan → có ô "Xem WTS của" (datalist); gc01 mở → KHÔNG có ô này, ngày hôm qua không có nút Sửa / Xoá (như cũ).
#   STEP D1 [L7-WTS-CU]  admin chọn ngày hôm qua + "Xem WTS của" gc01 → huy hiệu "Ngày cũ — chỉ Sửa / Xoá kèm lý do", bảng có dòng Y1, nút "Xóa WTS này" hiện;
#                         bấm Xóa với lý do TRỐNG → modal còn + lời "nhập lý do"; gõ lý do → xoá: DB Y1 IsVoided, VoidReason "Xóa dòng (quyền cao hơn): …", VoidedBy admin.
#                         Bản cũ: ngày cũ không có nút Xoá → SAI là đúng.
#   STEP E1 [L7-WTS-CU]  admin về hôm nay, vẫn xem gc01 → dòng T1; "Sửa lại" → nhóm sửa mở có ô "Lý do sửa"; OK 3 → 4; Lưu KHÔNG lý do → lỗi "nhập lý do", DB không đổi;
#                         gõ lý do → Lưu: T1 huỷ "Sửa lại (quyền cao hơn): …" VoidedBy admin; dòng mới WorkerId = gc01 (giữ người làm), OK 4, ngày hôm nay.
#   STEP D2 [L7-WTS-CU]  admin xoá dòng mới (hôm nay, của gc01) với lý do → xoá được, VoidedBy admin; nút "Thêm nhóm" bị khoá khi đang xem người khác.
# Chạy trên harness VỪA khởi động: python3 drive_g25.py <port> <nhãn>
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
def state(): return json.loads(urllib.request.urlopen(BASE + "/harness/g25").read().decode())
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
    st = post("/harness/g25-seed")
    ok(st.get("ok") and st.get("y1") and st.get("t1"), "Seed G25 (PO-G25 ON, gc01: Y1 hôm qua OK 5, T1 hôm nay OK 3)", st)
    y1, t1, gc, adm = st["y1"], st["t1"], st["gc01"], st["admin"]
    yday = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
    async with async_playwright() as p:
        br = await p.chromium.launch()

        print("STEP V1 L7-WTS-CU — ô 'Xem WTS của' chỉ Admin / WTS_MASTER thấy; gc01 ngày cũ vẫn chỉ xem")
        c2 = await br.new_context(); g = await login(c2, "gc01", "123456")
        ok(await g.locator("input[list='cuViewUsers']").count() == 0, "V1 gc01: KHÔNG có ô 'Xem WTS của'")
        await g.locator("input[type=date]").first.fill(yday); await g.locator("input[type=date]").first.dispatch_event("change"); await g.wait_for_timeout(1800)
        ok("Ngày cũ — chỉ xem" in await text(g), "V1 gc01 ngày hôm qua: huy hiệu 'Ngày cũ — chỉ xem' (chữ cũ)")
        ok(await g.locator("button[title='Xóa WTS này']").count() == 0 and await g.locator("button[title='Sửa lại']").count() == 0, "V1 gc01 ngày hôm qua: không có nút Sửa / Xoá (như cũ)")
        await c2.close()

        ctx = await br.new_context(); pg = await login(ctx, "admin", "admin123")
        ok(await pg.locator("input[list='cuViewUsers']").count() == 1, "V1 admin: có ô 'Xem WTS của'")

        print("STEP D1 L7-WTS-CU — admin xoá dòng HÔM QUA của gc01: bắt lý do, xoá được")
        await pg.locator("input[type=date]").first.fill(yday); await pg.locator("input[type=date]").first.dispatch_event("change"); await pg.wait_for_timeout(1800)
        ok("Ngày cũ — chỉ Sửa / Xoá kèm lý do" in await text(pg), "D1 huy hiệu 'Ngày cũ — chỉ Sửa / Xoá kèm lý do'", (await text(pg))[:300])
        await set_val(pg, pg.locator("input[list='cuViewUsers']").first, "Công nhân GC (gc01)")
        ok("WTS của Công nhân GC (gc01)" in await text(pg), "D1 huy hiệu 'WTS của Công nhân GC (gc01) — chỉ Sửa / Xoá kèm lý do'")
        ok(await pg.locator("button:has-text('Thêm nhóm')").first.is_disabled(), "D1 nút 'Thêm nhóm' bị khoá (không nhập mới cho người khác / ngày cũ)")
        r1 = saved_rows(pg).filter(has_text="PO-G25")
        ok(await r1.count() == 1 and "08:00" in await r1.first.inner_text(), "D1 bảng dòng đã lưu hiện dòng Y1 (NC01 OK 5, 08:00) của gc01 + nút Xóa", await r1.count())
        await r1.first.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(600)
        reason = pg.locator(".modal input[maxlength='150'], .mes-modal input[maxlength='150'], input[placeholder^='Vd: công nhân nhập nhầm']").first
        ok(await reason.count() == 1, "D1 modal xoá có ô 'Lý do xoá'")
        await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        s1 = state()
        ok(not row(s1, y1)["isVoided"] and "nhập lý do" in await text(pg), "D1 Xóa với lý do TRỐNG: máy chủ chặn (lời 'nhập lý do'), dòng còn", row(s1, y1))
        await reason.fill("nhập nhầm NC"); await pg.wait_for_timeout(300)
        await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        s1 = state(); ry = row(s1, y1)
        ok(ry["isVoided"] and ry["voidReason"] == "Xóa dòng (quyền cao hơn): nhập nhầm NC" and ry["voidedBy"] == adm, "D1 có lý do → Y1 huỷ, VoidReason ghi lý do, VoidedBy = admin", ry)
        ok(await saved_rows(pg).filter(has_text="PO-G25").count() == 0, "D1 bảng không còn dòng Y1")
        await pg.screenshot(path=f"g25_{OUT}_d1.png", full_page=False)

        print("STEP E1 L7-WTS-CU — admin sửa dòng HÔM NAY của gc01: ô Lý do sửa, giữ người làm")
        today = datetime.date.today().isoformat()
        await pg.locator("input[type=date]").first.fill(today); await pg.locator("input[type=date]").first.dispatch_event("change"); await pg.wait_for_timeout(1800)
        if await saved_rows(pg).filter(has_text="PO-G25").count() == 0:   # đổi ngày có thể đặt lại người xem → chọn lại
            await set_val(pg, pg.locator("input[list='cuViewUsers']").first, "Công nhân GC (gc01)")
        rt = saved_rows(pg).filter(has_text="PO-G25")
        ok(await rt.count() == 1 and "00:30" in await rt.first.inner_text(), "E1 hôm nay, xem gc01: dòng T1 (00:30) + nút Sửa / Xoá", await rt.count())
        await rt.first.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1800)
        grp = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
        ok(await grp.count() == 1, "E1 nhóm sửa mở")
        ok("Lý do sửa" in await text(pg) and await pg.locator("input[placeholder='≥ 3 ký tự']").count() == 1, "E1 nhóm sửa có ô 'Lý do sửa' (dòng của người khác)")
        trow = grp.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
        await trow.locator("input[type=number]").nth(0).fill("4"); await pg.wait_for_timeout(300)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        s2 = state()
        ok(not row(s2, t1)["isVoided"] and "nhập lý do" in await text(pg), "E1 Lưu KHÔNG lý do: máy chủ chặn 'nhập lý do', T1 còn", row(s2, t1))
        await pg.locator("input[placeholder='≥ 3 ký tự']").fill("gc01 đếm thừa 1 chiếc"); await pg.wait_for_timeout(300)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(3000)
        s2 = state(); rt1 = row(s2, t1)
        news = [w for w in s2["wts"] if not w["isVoided"] and w["id"] not in (y1, t1)]
        ok(rt1["isVoided"] and rt1["voidReason"] == "Sửa lại (quyền cao hơn): gc01 đếm thừa 1 chiếc" and rt1["voidedBy"] == adm, "E1 có lý do → T1 huỷ 'Sửa lại (quyền cao hơn): …', VoidedBy admin", rt1)
        ok(len(news) == 1 and news[0]["workerId"] == gc and news[0]["qtyOk"] == 4 and news[0]["start"].startswith(today), "E1 dòng mới GIỮ người làm gc01, OK 4, ngày hôm nay", news)
        await pg.screenshot(path=f"g25_{OUT}_e1.png", full_page=False)

        print("STEP D2 L7-WTS-CU — admin xoá dòng mới (hôm nay, của gc01) với lý do")
        await pg.wait_for_timeout(1500)
        rn = saved_rows(pg).filter(has_text="PO-G25")
        ok(await rn.count() == 1, "D2 bảng hiện dòng mới của gc01", await rn.count())
        await rn.first.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(600)
        reason = pg.locator("input[placeholder^='Vd: công nhân nhập nhầm']").first
        ok(await reason.count() == 1, "D2 modal xoá dòng hôm nay của NGƯỜI KHÁC vẫn có ô lý do")
        await reason.fill("xoá thử xong"); await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        s3 = state(); rnew = row(s3, news[0]["id"]) if news else None
        ok(rnew and rnew["isVoided"] and rnew["voidedBy"] == adm and rnew["voidReason"] == "Xóa dòng (quyền cao hơn): xoá thử xong", "D2 xoá được, VoidedBy admin, lý do ghi", rnew)
        ok(all(w["isVoided"] for w in s3["wts"]), "D2 PO-G25 không còn dòng WTS hiệu lực (gỡ sạch trên giao diện, không cần SQL)", s3["wts"])
        await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    with open(f"g25_{OUT}.txt", "w") as f: f.write(f"{fails}\n")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
