# [GC-SUA-TAI-CHO 04/11/2026] Trang Giao nhận thật (harness EF giả) — Kết quả sửa + KCS xác nhận.
#   Dữ liệu: POST /harness/sua-seed (Stubs/HarnessSua.cs): PO-SUA, GC đang giữ ô Hàng sửa 2 (Kho gửi nhờ sửa), Chưa GC 8.
#   K1 admin: Giao → form "Giao / xử lý hàng" [GN-FORM1] → Gia công · Hàng lỗi · Tồn hàng sửa có 2, vụ tự chọn → Sửa tại nhóm: đạt 1 + không đạt 1,
#       NC làm tiếp chỉ "như hàng mới" (phôi Kho gửi sửa — K2) → DB 1 dòng (SUA, đạt 1, không đạt 1, ToNC null); sổ GC: ô Hàng sửa 0, ô Hàng lỗi 1, Chờ KCS 1, Chưa GC 8
#   K2 ô Tồn GC hiện "chờ KCS 1"
#   K3 admin (người ghi) mở Nhận · KCS → mục "Hàng sửa chờ KCS xác nhận" có 1 → bấm KCS xác nhận → báo "khác người", DB không thêm dòng
#   K4 kcsgn (chỉ Sửa Giao nhận KCS) → Nhận → mục chờ KCS → xác nhận đạt 1 → DB 1 lần xác nhận; Chờ KCS 0, chờ NC01 +1, Chưa GC 9
#   K5 kcsgn mở Lịch sử → khối Kết quả sửa có bảng KCS xác nhận + nút huỷ → huỷ (lý do) → DB lần xác nhận đã huỷ; Chờ KCS về 1
#   K6 bản cũ (trước [GN-FORM1]) chưa có form mới (chạy với old → SAI ở K1 là đúng)
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

        print("[K1] admin ghi Kết quả sửa từ Tồn hàng sửa — form Giao / xử lý hàng [GN-FORM1]")
        await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1000)
        m = modal(pg)
        t0 = await m.inner_text()
        ok("Giao / xử lý hàng" in t0 and "Sửa xong tại nhóm" not in t0 and "Báo hàng lỗi phát hiện sau khi nhận" not in t0,
           "K11 form 'Giao / xử lý hàng' (không còn link 'Sửa xong tại nhóm' / 'Báo hàng lỗi phát hiện sau khi nhận')", t0[:300])
        await m.locator("button.gn-cellbtn", has_text="Hàng lỗi").first.click(); await pg.wait_for_timeout(800)
        sua_btn = m.locator("button.gn-cellbtn", has_text="Tồn hàng sửa").first
        bt = await sua_btn.inner_text() if await sua_btn.count() else ""
        ok("có 2" in bt, "K12 dòng 'Đang ở Tồn hàng sửa (nhóm khác gửi nhờ sửa)' có 2", bt)
        if await sua_btn.count():
            await sua_btn.click(); await pg.wait_for_timeout(900)
            vu_btn = m.locator("button.gn-cellbtn", has_text="VL").first
            vt = await vu_btn.inner_text() if await vu_btn.count() else ""
            ok("VL" in vt and "giữ 2" in vt and "on" in (await vu_btn.get_attribute("class") or ""), "K13 vụ của Kho tự chọn (1 vụ), giữ 2", vt)
            await m.locator("button.gn-cellbtn", has_text="Sửa tại nhóm").first.click(); await pg.wait_for_timeout(800)
            nums = m.locator("input[type=number]")
            await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change")
            await nums.nth(1).fill("1"); await nums.nth(1).dispatch_event("change"); await pg.wait_for_timeout(600)
            sel_nc = m.locator("select", has=pg.locator("option", has_text="mặc định")).first
            nco = [ (await o.inner_text()).strip() for o in await sel_nc.locator("option").all() ] if await sel_nc.count() else []
            ok(len(nco) == 1 and nco[0].startswith("Như hàng mới") and "mặc định" in nco[0], "K14 [K2] phôi Kho gửi sửa: NC làm tiếp chỉ 'Như hàng mới (NC01) — mặc định' (không cho NC02)", nco)
            await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").click(); await pg.wait_for_timeout(2500)
            s1 = get("/harness/sua")
            rp = s1["reps"]
            ok(len(rp) == 1 and rp[0]["sourceCell"] == "SUA" and rp[0]["qtyPass"] == 1 and rp[0]["qtyFail"] == 1 and rp[0]["toNC"] is None and rp[0]["ngCaseId"],
               "K15 DB: 1 Kết quả sửa (Hàng sửa, đạt 1, không đạt 1, như hàng mới, có vụ)", rp)
            g = s1["gc"]
            ok(g["suaGiu"] == 0 and g["loiGiu"] == 1 and g["choKcs"] == 1 and g["chuaLam"] == 8 and g["ton"] == s0["gc"]["ton"] and s1["balanced"] and s1["neg"] == 0,
               "K16 sổ GC: Hàng sửa 0, Hàng lỗi 1, Chờ KCS 1, Chưa GC 8, Tồn không đổi, cân, 0 ô âm", g)
        else:
            s1 = get("/harness/sua")
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
            ok(g["choKcs"] == 0 and g["chuaLam"] == 9 and g["nc"]["NC01"] == s1["gc"]["nc"]["NC01"] + 1 and s4["balanced"] and s4["neg"] == 0,
               "K43 sổ GC: Chờ KCS 0, Chưa GC 9, chờ NC01 +1 (như hàng mới), cân, 0 ô âm", g)
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
