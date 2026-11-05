# [GC-SUA-TAI-CHO 04/11/2026] Trang Giao nhận thật (harness EF giả) — Kết quả sửa + KCS xác nhận.
#   Dữ liệu: POST /harness/sua-seed (Stubs/HarnessSua.cs): PO-SUA, GC đang giữ ô Hàng sửa 2 (Kho gửi nhờ sửa), Chưa GC 8.
#   K1 admin: Giao → link "Sửa xong tại nhóm" → modal Kết quả sửa: ô Hàng sửa có 2, vụ tự chọn, đạt 1 + không đạt 1, NC làm tiếp NC02 → lưu
#       → DB 1 dòng (SUA, đạt 1, không đạt 1, NC02); sổ GC: ô Hàng sửa 0, ô Hàng lỗi 1, Chờ KCS 1, Chưa GC 8 (không đổi); cân, 0 ô âm
#   K2 ô Tồn GC hiện "chờ KCS 1"
#   K3 admin (người ghi) mở Nhận · KCS → mục "Hàng sửa chờ KCS xác nhận" có 1 → bấm KCS xác nhận → báo "khác người", DB không thêm dòng
#   K4 kcsgn (chỉ Sửa Giao nhận KCS) → Nhận → mục chờ KCS → xác nhận đạt 1 → DB 1 lần xác nhận; Chờ KCS 0, chờ NC02 +1, Chưa GC 9
#   K5 kcsgn mở Lịch sử → khối Kết quả sửa có bảng KCS xác nhận + nút huỷ → huỷ (lý do) → DB lần xác nhận đã huỷ; Chờ KCS về 1
#   K6 bản cũ (HEAD) không có link "Sửa xong tại nhóm" (chạy với old → SAI ở K1 là đúng)
# Chạy trên harness VỪA khởi động: python3 drive_sua.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if info not in (None, "") else ""))
def modal(pg): return pg.locator(".mes-modal").last
async def login(pg, user, pw):
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(5000)
def row(pg): return pg.locator("tr", has_text="PO-SUA").first
async def close_modal(pg):
    while await pg.locator(".mes-modal").count():
        await modal(pg).locator(".btn-close").first.click(); await pg.wait_for_timeout(600)

async def main():
    post("/harness/sua-seed")
    s0 = get("/harness/sua")
    ok(s0["gc"]["suaGiu"] == 2 and s0["gc"]["chuaLam"] == 8 and s0["balanced"], "K0 dữ liệu: GC giữ ô Hàng sửa 2, Chưa GC 8, cân", s0["gc"])
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1100})
        await login(pg, "admin", "admin123")

        print("[K1] admin ghi Kết quả sửa từ ô Hàng sửa")
        await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1000)
        link = modal(pg).locator("button:has-text('Sửa xong tại nhóm')")
        has = await link.count() == 1
        ok(has, "K11 form Giao có link 'Sửa xong tại nhóm (không giao đi)? Ghi Kết quả sửa'")
        if has:
            await link.click(); await pg.wait_for_timeout(1200)
            m = modal(pg)
            t = await m.inner_text()
            ok("Kết quả sửa" in t and "chờ KCS" in t.replace("Chờ", "chờ"), "K12 modal Kết quả sửa mở", t[:300])
            sua_btn = m.locator("button.gn-cellbtn", has_text="Hàng sửa").first
            ok("có 2" in (await sua_btn.inner_text()), "K13 ô Hàng sửa có 2 (tự chọn)", await sua_btn.inner_text())
            sel_vu = m.locator("select").nth(0 if await m.locator("select").count() == 2 else 1)
            ok("VL" in (await sel_vu.inner_text()), "K14 ô vụ lỗi có mã vụ của Kho", await sel_vu.inner_text())
            nums = m.locator("input[type=number]")
            await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change")
            await nums.nth(1).fill("1"); await nums.nth(1).dispatch_event("change"); await pg.wait_for_timeout(500)
            sel_nc = m.locator("select").last
            await sel_nc.select_option("NC02"); await pg.wait_for_timeout(500)
            await m.locator("button:has-text('Lưu Kết quả sửa')").click(); await pg.wait_for_timeout(2500)
            s1 = get("/harness/sua")
            rp = s1["reps"]
            ok(len(rp) == 1 and rp[0]["sourceCell"] == "SUA" and rp[0]["qtyPass"] == 1 and rp[0]["qtyFail"] == 1 and rp[0]["toNC"] == "NC02" and rp[0]["ngCaseId"],
               "K15 DB: 1 Kết quả sửa (Hàng sửa, đạt 1, không đạt 1, NC02, có vụ)", rp)
            g = s1["gc"]
            ok(g["suaGiu"] == 0 and g["loiGiu"] == 1 and g["choKcs"] == 1 and g["chuaLam"] == 8 and g["ton"] == s0["gc"]["ton"] and s1["balanced"] and s1["neg"] == 0,
               "K16 sổ GC: Hàng sửa 0, Hàng lỗi 1, Chờ KCS 1, Chưa GC 8, Tồn không đổi, cân, 0 ô âm", g)
        print("[K2] ô Tồn hiện chờ KCS")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        rt = await row(pg).inner_text()
        ok("chờ KCS 1" in rt, "K21 dòng PO-SUA hiện 'chờ KCS 1' ở ô Tồn GC", rt[:400])

        print("[K3] người ghi tự xác nhận → chặn")
        await row(pg).locator("button:has-text('Nhận')").click(); await pg.wait_for_timeout(1500)
        m = modal(pg)
        if await m.locator("select").count():
            await m.locator("select").first.select_option(label="KCS"); await pg.wait_for_timeout(1500)
        t = await m.inner_text()
        ok("Hàng sửa chờ KCS xác nhận: 1" in t, "K31 modal Nhận · KCS có mục 'Hàng sửa chờ KCS xác nhận: 1'", t[:400])
        if "Kết quả sửa #" in t:
            await m.locator("div", has_text="Kết quả sửa #").last.click(); await pg.wait_for_timeout(800)
            btn = m.locator("button:has-text('KCS xác nhận')").last
            t2 = await modal(pg).inner_text()
            # rà độc lập T3: người ghi thấy lời nhắc + nút khoá (máy chủ vẫn chặn — kiểm ở GiaoNhanFuzz sua:S2)
            ok(await btn.is_disabled() and "người KCS xác nhận phải là người khác" in t2 and len(get("/harness/sua")["checks"]) == 0,
               "K32 admin (người ghi): nút KCS xác nhận khoá + lời nhắc khác người, DB không thêm", t2[-300:])
        await close_modal(pg)

        print("[K4] kcsgn xác nhận đạt 1")
        pk = await (await b.new_context()).new_page(); await pk.set_viewport_size({"width": 1800, "height": 1100})
        await login(pk, "kcsgn", "123456")
        await row(pk).locator("button:has-text('Nhận')").click(); await pk.wait_for_timeout(2000)
        mk_ = modal(pk)
        tk = await mk_.inner_text()
        ok("Hàng sửa chờ KCS xác nhận: 1" in tk, "K41 kcsgn mở Nhận: mục chờ KCS có 1", tk[:300])
        if "Kết quả sửa #" in tk:
            await mk_.locator("div", has_text="Kết quả sửa #").last.click(); await pk.wait_for_timeout(800)
            await mk_.locator("button:has-text('KCS xác nhận')").last.click(); await pk.wait_for_timeout(2500)
            s4 = get("/harness/sua")
            ck = [c for c in s4["checks"] if not c["isVoided"]]
            g = s4["gc"]
            ok(len(ck) == 1 and ck[0]["qtyOk"] == 1 and ck[0]["qtyNg"] == 0, "K42 DB: 1 lần KCS xác nhận đạt 1", s4["checks"])
            ok(g["choKcs"] == 0 and g["chuaLam"] == 9 and g["nc"]["NC02"] == s1["gc"]["nc"]["NC02"] + 1 and s4["balanced"] and s4["neg"] == 0,
               "K43 sổ GC: Chờ KCS 0, Chưa GC 9, chờ NC02 +1, cân, 0 ô âm", g)
            tk2 = await modal(pk).inner_text()
            ok("Hàng sửa chờ KCS xác nhận: 0" in tk2, "K44 mục chờ KCS về 0 sau khi xác nhận", tk2[:300])
        await close_modal(pk)

        print("[K5] kcsgn huỷ lần xác nhận trong Lịch sử")
        await row(pk).locator("button[title='Lịch sử phiếu']").click(); await pk.wait_for_timeout(2000)
        ml = modal(pk); tl = await ml.inner_text()
        ok("Kết quả sửa #" in tl and "sửa tại nhóm" in tl, "K51 Lịch sử có khối Kết quả sửa", tl[:300])
        vb = ml.locator("button[title='Huỷ lần KCS xác nhận này']")
        ok(await vb.count() == 1, "K52 có nút huỷ lần KCS xác nhận (người xác nhận)")
        if await vb.count():
            await vb.first.click(); await pk.wait_for_timeout(800)
            mv = modal(pk)
            await mv.locator("input[type=text]").first.fill("xac nhan nham"); await pk.wait_for_timeout(200)
            await mv.locator("button:has-text('Xác nhận')").click(); await pk.wait_for_timeout(2500)
            s5 = get("/harness/sua")
            ok(all(c["isVoided"] for c in s5["checks"]) and s5["gc"]["choKcs"] == 1 and s5["gc"]["chuaLam"] == 8 and s5["balanced"],
               "K53 DB: lần xác nhận đã huỷ (không xoá); Chờ KCS 1, Chưa GC 8 như trước", s5)
        await b.close()
    print(f"KẾT QUẢ drive_sua ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
