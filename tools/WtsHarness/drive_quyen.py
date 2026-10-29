# [RS-C1][RS-E2 29/10/2026] Bấm trang thật (harness EF giả): quyền ma trận ở 5 trang danh mục + Part Master không "Xác nhận OK"
# khi lưu lỗi giữa chừng. Chạy: python3 drive_quyen.py <port> <old|new>  → in từng bước ĐẠT / SAI, ghi quyen_<old|new>.json
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
res = []

def dump():
    return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())

def ok(cond, name, info=""):
    res.append({"buoc": name, "dat": bool(cond), "info": str(info)[:400]})
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if info else ""))

async def login(b, user, pw="123456"):
    ctx = await b.new_context(); pg = await ctx.new_page()
    await pg.set_viewport_size({"width": 1800, "height": 1100})
    await pg.goto(BASE + "/login")
    await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg

async def goto(pg, path, wait=2000):
    await pg.goto(BASE + path); await pg.wait_for_timeout(wait)

async def row_with(pg, val):
    rows = pg.locator("tbody tr")
    for i in range(await rows.count()):
        r = rows.nth(i); inp = r.locator("input")
        if await inp.count() and (await inp.first.input_value()) == val and await r.locator("button[title='Xóa']").count():
            return r
    raise Exception("không thấy dòng " + val)

async def count(pg, sel):
    return await pg.locator(sel).count()

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        print("[C1] Người chỉ có quyền XEM danh mục (xem01)")
        pg = await login(b, "xem01")
        await goto(pg, "/thiet-bi")
        ok(await count(pg, "button:has-text('Thêm thiết bị mới')") == 0, "Thiết bị: không có nút Thêm", await count(pg, "button:has-text('Thêm thiết bị mới')"))
        ns, nx = await count(pg, "button[title='Sửa']"), await count(pg, "button[title='Xóa']")
        ok(ns == 0 and nx == 0, "Thiết bị: không có nút Sửa / Xóa từng dòng", f"sửa={ns} xoá={nx}")
        ok(await count(pg, "button[title='Lịch sử trạng thái']") > 0, "Thiết bị: vẫn xem được danh sách + lịch sử")
        await goto(pg, "/customers")
        ok(await count(pg, "button:has-text('Thêm khách hàng mới')") == 0 and await count(pg, "button[title='Chỉnh sửa']") == 0
           and await count(pg, "button[title='Ngừng hoạt động']") == 0, "Khách hàng: không có Thêm / Sửa / Ngừng")
        await goto(pg, "/dao")
        ok(await count(pg, "button:has-text('Thêm dao mới')") == 0, "Dao: không có nút Thêm")
        await goto(pg, "/do-ga")
        ok(await count(pg, "button:has-text('Thêm đồ gá mới')") == 0, "Đồ gá: không có nút Thêm")
        await goto(pg, "/wts-tieu-chuan")
        ok(await count(pg, "button:has-text('Thêm công việc mới')") == 0 and await count(pg, "button[title='Sửa']") == 0, "WTS tiêu chuẩn: không có Thêm / Sửa")
        tab = pg.locator("button:has-text('Cấu hình nhóm WTS')")
        ok(await tab.count() == 1, "WTS tiêu chuẩn: quyền Xem cấu hình nhóm → thấy tab")
        if await tab.count():
            await tab.click(); await pg.wait_for_timeout(1500)
            save = pg.locator("button:has-text('Lưu cấu hình')")
            cbs = pg.locator("table input[type=checkbox]")
            n = await cbs.count(); dis = 0
            for i in range(n):
                if await cbs.nth(i).is_disabled(): dis += 1
            ok(await save.count() == 1 and await save.is_disabled() and n > 0 and dis == n,
               "Cấu hình nhóm WTS: chỉ Xem → ô tích khoá, nút Lưu khoá (trước: quyền Xem là lưu được)", f"ô={n} khoá={dis}")
        await pg.context.close()

        print("[C1] Người không có quyền xem danh mục (gc01) gõ thẳng URL")
        pg = await login(b, "gc01")
        for path in ["/thiet-bi", "/customers", "/dao", "/do-ga", "/wts-tieu-chuan"]:
            await goto(pg, path, 2500)
            ok(not pg.url.rstrip("/").endswith(path), f"{path}: bị đưa về trang chủ (trước: vào được, thêm / sửa / xoá được)", pg.url)
        await pg.context.close()

        print("[C1] Người có quyền SỬA danh mục (dm01) — vẫn làm được như cũ")
        pg = await login(b, "dm01")
        await goto(pg, "/thiet-bi")
        ok(await count(pg, "button:has-text('Thêm thiết bị mới')") == 1 and await count(pg, "button[title='Sửa']") > 0, "Thiết bị: có nút Thêm + Sửa")
        await pg.click("button:has-text('Thêm thiết bị mới')"); await pg.wait_for_timeout(800)
        modal = pg.locator(".modal-content", has=pg.locator("text=Thêm thiết bị mới"))
        so = modal.locator("input[placeholder^='VD: C01']")
        await so.fill("M99"); await so.press("Tab"); await pg.wait_for_timeout(300)
        await modal.locator("button.btn-primary").click(); await pg.wait_for_timeout(1500)
        ok("M99" in dump()["thietbi"], "Thiết bị: dm01 thêm máy M99 qua trang → có trong DB", dump()["thietbi"])
        await goto(pg, "/wts-tieu-chuan")
        await pg.click("button:has-text('Cấu hình nhóm WTS')"); await pg.wait_for_timeout(1500)
        cbs = pg.locator("table input[type=checkbox]")
        ok(await cbs.count() > 0 and not await cbs.first.is_disabled(), "Cấu hình nhóm WTS: quyền Sửa → tích được")
        await pg.context.close()

        print("[C2] Trang Giao nhận: quyền nhóm đọc theo DB (không tin claim)")
        for u, pw in [("gn01", "123456"), ("admin", "admin123")]:
            pg = await login(b, u, pw)
            await goto(pg, "/handover", 3500)
            body = await pg.inner_text("body")
            ok("unhandled error" not in body.lower() and "Giao nhận" in body, f"{u}: trang Giao nhận mở bình thường")
            await pg.context.close()

        print("[E2] Part Master: lưu lỗi giữa chừng không được 'Xác nhận OK' (2 người mở cùng Part)")
        pa = await login(b, "admin", "admin123"); pb = await login(b, "admin", "admin123")
        await goto(pa, "/part-master/detail?partNo=PART-E2", 3000); await goto(pb, "/part-master/detail?partNo=PART-E2", 3000)
        before = [x for x in dump()["parts"] if x["partNo"] == "PART-E2"][0]
        ok(before["isHtspConfirmed"], "Trước: mục C HTSP đang Xác nhận OK")
        # A xoá dòng Bavia B1 rồi lưu
        rowA = await row_with(pa, "B1")
        await rowA.locator("button[title='Xóa']").click(); await pa.wait_for_timeout(500)
        await pa.locator("textarea").last.fill("xoa bavia thu E2"); await pa.wait_for_timeout(300)
        await pa.click("button:has-text('Lưu & Xác nhận OK')"); await pa.wait_for_timeout(3000)
        ta = await pa.inner_text("body"); print("   A:", [l for l in ta.split("\n") if "✓" in l or "✗" in l or "⚠" in l][:8])
        bav = [x for x in dump()["bavia"] if x["nc"] == "B1"]
        ok((not bav or not bav[0]["isActive"]) and "✓ Đã lưu & Xác nhận OK." in ta, "Người A xoá dòng Bavia B1 (xoá hẳn + log): đã lưu & Xác nhận OK", bav)
        # B (trang cũ, chưa tải lại) sửa dòng B1 + Rửa R1 rồi lưu → dòng B1 lỗi "đã xoá"
        rowB = await row_with(pb, "B1")
        await rowB.locator("button[title='Sửa dòng này']").click(); await pb.wait_for_timeout(500)
        await pb.locator("textarea").last.fill("sua bavia thu E2"); await pb.wait_for_timeout(300)
        await pb.click("button:has-text('Lưu & Xác nhận OK')"); await pb.wait_for_timeout(3500)
        txt = await pb.inner_text("body")
        after = [x for x in dump()["parts"] if x["partNo"] == "PART-E2"][0]
        print("   B:", [l for l in txt.split("\n") if "✓" in l or "✗" in l or "⚠" in l][:8])
        has_err = "✗ [Bavia] NC=B1" in txt
        ok(has_err, "Người B (trang cũ) sửa dòng B1 đã bị xoá rồi lưu: dòng B1 báo lỗi (lỗi giữa chừng)")
        ok(not after["isHtspConfirmed"], "Sau khi lưu lỗi: mục C HTSP KHÔNG còn Xác nhận OK (về NG)", after)
        ok("Đã lưu & Xác nhận OK." not in txt and "CHƯA Xác nhận OK" in txt, "Lời báo không nói 'Đã lưu & Xác nhận OK', nói rõ mục C chưa OK",
           [l for l in txt.split("\n") if "Xác nhận OK" in l][:4])
        ok(after["isPlanConfirmed"], "Mục A (không lỗi) giữ nguyên Xác nhận OK", after)
        await b.close()
    json.dump(res, open(f"quyen_{OUT}.json", "w"), ensure_ascii=False, indent=1)
    print(f"\nKẾT QUẢ {OUT}: {sum(1 for r in res if r['dat'])} ĐẠT / {sum(1 for r in res if not r['dat'])} SAI")

asyncio.run(main())
