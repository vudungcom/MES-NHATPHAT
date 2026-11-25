# [N17-KCS-BO 23/11/2026] Trang Giao nhận thật (harness EF giả) — luật N17: sửa đạt KHÔNG cần KCS xác nhận (khách 23/11 mockup TH3 + sheet dòng 14).
#   Dữ liệu: POST /harness/sua-seed (Stubs/HarnessSua.cs): PO-SUA, GC đang giữ ô Hàng sửa 2 (Kho gửi nhờ sửa), Chưa GC 8.
#   STEP N1 admin: form "Giao / xử lý hàng" → Gia công · Hàng lỗi · Tồn hàng sửa → Sửa tại nhóm: chữ "Đạt → vào thẳng ô đạt … (không cần KCS xác nhận)",
#       "Sẽ ghi: Kết quả sửa — đạt vào thẳng ô đạt"; đạt 1 + không đạt 1 → DB 1 Kết quả sửa + 1 dòng tự xác nhận (đạt 1, người ghi, ghi chú [N17]);
#       sổ GC: Hàng sửa 0, Hàng lỗi 1, Chờ KCS 0, Chưa GC 9, chờ NC01 +1, cân, 0 ô âm
#   STEP N2 ô Tồn GC không hiện "chờ KCS"; bảng "Tồn theo loại hàng" ẩn cột Chờ KCS
#   STEP N3 kcsgn mở Nhận: không có mục "Hàng sửa chờ KCS xác nhận"
#   STEP N4 Lịch sử: khối Kết quả sửa có nhãn "đạt không cần KCS", không có bảng / nút huỷ "lần KCS xác nhận"; admin bấm "Huỷ Kết quả sửa" (lý do)
#       → DB Kết quả sửa + dòng tự xác nhận cùng huỷ (không xoá); sổ GC về như trước (Hàng sửa 2, Chưa GC 8)
#   Bản cũ (trước N17) phải SAI từ N1 (đạt vào Chờ KCS, không có dòng tự xác nhận).
# Chạy trên harness VỪA khởi động: python3 drive_n17.py <port> <old|new>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
NOTE = "[N17] Sửa đạt — không cần KCS xác nhận"
fails = 0
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if info not in (None, "") else ""))
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
    ok(s0["gc"]["suaGiu"] == 2 and s0["gc"]["chuaLam"] == 8 and s0["balanced"], "N0 dữ liệu: GC giữ ô Hàng sửa 2, Chưa GC 8, cân", s0["gc"])
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await (await b.new_context()).new_page(); await pg.set_viewport_size({"width": 1800, "height": 1100})
        await login(pg, "admin", "admin123")

        print("[STEP N1] admin ghi Kết quả sửa — đạt vào thẳng ô đạt, không chờ KCS")
        await row(pg).locator("button:has-text('Giao')").click(); await pg.wait_for_timeout(1500)
        await modal(pg).locator("select").first.select_option(label="Gia công"); await pg.wait_for_timeout(1000)
        m = modal(pg)
        await m.locator("button.gn-cellbtn", has_text="Hàng lỗi").first.click(); await pg.wait_for_timeout(800)
        sua_btn = m.locator("button.gn-cellbtn", has_text="Đang giữ ở ô Hàng sửa").first   # [N23-FORM6 25/11] chữ dòng mới
        s1 = s0
        if await sua_btn.count():
            await sua_btn.click(); await pg.wait_for_timeout(900)
            await m.locator("button.gn-cellbtn", has_text="Sửa tại bộ phận mình").first.click(); await pg.wait_for_timeout(800)
            nums = m.locator("input[type=number]")
            # [N23-SUA-CHUADAT 25/11] đáp án đổi: chưa đạt ở ô Hàng sửa nay tự trả nơi gửi (kiểm ở drive_n23) — driver N17 chỉ ghi đạt 1 (chưa đạt 0)
            await nums.nth(0).fill("1"); await nums.nth(0).dispatch_event("change")
            await nums.nth(1).fill("0"); await nums.nth(1).dispatch_event("change"); await pg.wait_for_timeout(700)
            t1 = await m.inner_text()
            ok("vào thẳng ô đạt" in t1 and "không cần KCS xác nhận" in t1 and "chờ KCS" not in t1,
               "N11 form: 'Đạt → vào thẳng ô đạt … (không cần KCS xác nhận)', không còn chữ 'chờ KCS'", t1[-700:])
            ok("Sẽ ghi: sửa đạt 1 — đạt vào thẳng ô đạt" in t1, "N12 câu 'Sẽ ghi: sửa đạt 1 — đạt vào thẳng ô đạt (không cần KCS xác nhận)' [N23-FORM6 chữ mới]", t1[-400:])
            await m.locator(".mes-modal-footer button.btn-primary:has-text('Xác nhận')").click(); await pg.wait_for_timeout(2500)
            s1 = get("/harness/sua")
            rp = s1["reps"]; ck = s1["checks"]
            ok(len(rp) == 1 and rp[0]["qtyPass"] == 1 and rp[0]["qtyFail"] == 0 and not rp[0]["isVoided"], "N13 DB: 1 Kết quả sửa (đạt 1)", rp)
            ok(len(rp) == 1 and len(ck) == 1 and ck[0]["handoverRepairResultId"] == rp[0]["handoverRepairResultId"] and ck[0]["qtyOk"] == 1 and ck[0]["qtyNg"] == 0
               and ck[0]["checkedBy"] == rp[0]["createdBy"] and ck[0]["notes"] == NOTE and not ck[0]["isVoided"],
               "N14 DB: 1 dòng tự xác nhận (đạt 1, người = người ghi, ghi chú [N17])", ck)
            g = s1["gc"]
            ok(g["suaGiu"] == 1 and g["loiGiu"] == 0 and g["choKcs"] == 0 and g["chuaLam"] == 9 and g["nc"]["NC01"] == s0["gc"]["nc"]["NC01"] + 1
               and g["ton"] == s0["gc"]["ton"] and s1["balanced"] and s1["neg"] == 0,
               "N15 sổ GC: Hàng sửa 1, Hàng lỗi 0, Chờ KCS 0, Chưa GC 9, chờ NC01 +1 (làm tiếp ngay), Tồn không đổi, cân, 0 ô âm", g)
        else:
            ok(False, "N11 không thấy dòng 'Tồn hàng sửa' trên form", await m.inner_text())

        print("[STEP N2] trang không còn 'chờ KCS'")
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(4000)
        rt = await row(pg).inner_text()
        ok("chờ KCS" not in rt, "N21 dòng PO-SUA không hiện 'chờ KCS' ở ô Tồn", rt[:400])
        if await pg.locator("button:has-text('Hiện công thức')").count():   # bảng Tồn theo loại hàng chỉ hiện khi bật Hiện công thức (như drive_g15 X22)
            await pg.locator("button:has-text('Hiện công thức')").first.click(); await pg.wait_for_timeout(1200)
        await row(pg).locator("button[title^='Xem từng bước']").click(); await pg.wait_for_timeout(2500)
        info = await pg.evaluate("""() => { const t = [...document.querySelectorAll('th')].filter(x => x.textContent.trim() === 'Chờ KCS');
            return { n: t.length, hidden: t.every(x => getComputedStyle(x).display === 'none'), page: document.body.innerText.includes('Tồn theo loại hàng') }; }""")
        ok(info["page"] and info["n"] >= 1 and info["hidden"], "N22 bảng 'Tồn theo loại hàng' ẩn cột Chờ KCS (PO không còn chứng từ cũ chờ)", info)
        await close_modal(pg)

        print("[STEP N3] kcsgn mở Nhận: không còn mục chờ KCS")
        pk = await (await b.new_context()).new_page(); await pk.set_viewport_size({"width": 1800, "height": 1100})
        await login(pk, "kcsgn", "123456")
        nb = row(pk).locator("button:has-text('Nhận')")
        if await nb.count():
            await nb.click(); await pk.wait_for_timeout(2000)
            tk = await modal(pk).inner_text()
            ok("Hàng sửa chờ KCS xác nhận" not in tk, "N31 modal Nhận · KCS không có mục 'Hàng sửa chờ KCS xác nhận'", tk[:400])
            await close_modal(pk)
        else:
            ok(False, "N31 kcsgn không thấy nút Nhận — không kiểm được mục chờ KCS (dữ liệu sua-seed phải có việc chờ KCS nhận)")   # rà độc lập G-C Thấp 7: không tự ĐẠT

        print("[STEP N4] Lịch sử + huỷ Kết quả sửa (huỷ luôn dòng tự xác nhận)")
        await row(pg).locator("button[title='Lịch sử phiếu']").click(); await pg.wait_for_timeout(2000)
        ml = modal(pg); tl = await ml.inner_text()
        ok("Kết quả sửa #" in tl and "đạt không cần KCS" in tl, "N41 Lịch sử: khối Kết quả sửa có nhãn 'đạt không cần KCS'", tl[:500])
        ok(await ml.locator("button[title='Huỷ lần KCS xác nhận này']").count() == 0 and "KCS xác nhận\tNgười" not in tl,
           "N42 không có bảng / nút huỷ 'lần KCS xác nhận' cho chứng từ mới")
        vb = ml.locator("button:has-text('Huỷ Kết quả sửa')")
        ok(await vb.count() == 1, "N43 có nút 'Huỷ Kết quả sửa' (người ghi)")
        if await vb.count():
            await vb.first.click(); await pg.wait_for_timeout(800)
            mv = modal(pg)
            await mv.locator("input[type=text]").first.fill("ghi nham"); await pg.wait_for_timeout(200)
            await mv.locator("button:has-text('Xác nhận')").click(); await pg.wait_for_timeout(2500)
            s4 = get("/harness/sua")
            ok(all(r["isVoided"] for r in s4["reps"]) and len(s4["checks"]) == 1 and all(c["isVoided"] and c["voidReason"] == "ghi nham" for c in s4["checks"]),
               "N44 DB: Kết quả sửa + dòng tự xác nhận cùng huỷ (không xoá, cùng lý do)", s4)
            g = s4["gc"]
            ok(g["suaGiu"] == 2 and g["loiGiu"] == 0 and g["chuaLam"] == 8 and g["choKcs"] == 0 and g["nc"]["NC01"] == s0["gc"]["nc"]["NC01"] and s4["balanced"] and s4["neg"] == 0,
               "N45 sổ GC về như trước khi ghi (Hàng sửa 2, Chưa GC 8, chờ NC01 như cũ), cân", g)
        await b.close()
    print(f"KẾT QUẢ drive_n17 ({OUT}): " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
