# [STD-UPDATE-V4][STD-UPDATE-V3][STD-5][STD-UPD 02/11/2026] Bấm trang Part Master thật — nút "Update TG chuẩn" (phần chỉ có ở trang:
#   ẩn nút Chạy / mục trống, báo lý do khi bấm Chạy, tick PO, trang gửi dấu bản Xem trước). Phần máy chủ (StdUpdateService) kiểm ở
#   GiaoNhanFuzz `std` (S1–S5). Dữ liệu: PART-STD (NC 1 M1 T.Gá 10 + đồng dạng M3) · phiếu KH-STD 2 PO PO-STD1 / PO-STD2 chưa có WTS,
#   snapshot chép bằng KhPlanService.CopyRouteSnapshotAsync thật (HarnessSeed.SeedPmAsync). Người chạy: stdadm (ADMIN, PIN 1111).
#   Sửa Part Master bằng "Lưu nháp" (Lưu & Xác nhận OK sẽ tự làm mới quy trình PO chưa chạy [K6-NOTRUN] → không còn gì để Update).
# Chạy: python3 drive_std.py <port> <nhãn>   (harness mk.sh, DB giả mới tinh)
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
PART = "/part-master/detail?partNo=PART-STD"

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump2").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if (not cond and info not in (None, "")) else ""))
def po_id(d, po): return [x["khPlanDetailId"] for x in d["po"] if x["purchaseOrder"] == po][0]
def snap_setup(d, po): return [s["setupTime"] for s in d["snapm"] if s["khPlanDetailId"] == po_id(d, po)]

async def login(b, user="stdadm", pw="123456"):
    ctx = await b.new_context(); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg): await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)

async def set_nc1_setup(pg, v):
    """Bút chì NC 1 → T.Gá lắp = v → Lưu nháp."""
    tr = pg.locator("tr", has=pg.locator("input[list^='dl-thietbi']")).first
    await tr.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
    n = pg.locator("tr", has=pg.locator("input[list^='dl-thietbi']")).first.locator("input[type=number]").nth(0)
    await n.fill(str(v)); await n.press("Tab"); await pg.wait_for_timeout(300)
    await pg.locator("textarea").last.fill("doi TG chuan"); await pg.wait_for_timeout(250)
    await pg.click("button:has-text('Lưu nháp')"); await pg.wait_for_timeout(3000)

def modal(pg): return pg.locator(".mes-modal").filter(has_text="Update thời gian chuẩn")
async def open_upd(pg):
    await pg.click("button:has-text('Update TG chuẩn')"); await pg.wait_for_timeout(800)
async def preview(pg):
    await modal(pg).locator("button:has-text('Xem trước')").click(); await pg.wait_for_timeout(2000)
async def mtext(pg): return await modal(pg).inner_text()
async def run_btn(pg): return modal(pg).locator("button:has-text('Chạy Update (PIN)')")
async def po_box(pg, po): return modal(pg).locator("tr", has=pg.locator(f"code:text-is('{po}')")).locator("input[type=checkbox]")

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await login(b)
        await open_part(pg)
        print("[V] Không có gì cần Update → 1 dòng xanh, không có nút Chạy, không có mục PO ([STD-UPDATE-V4])")
        await open_upd(pg); await preview(pg)
        t = await mtext(pg)
        ok("không cần Update" in t and "Tất cả 2 PO" in t, "V1 [STD-UPDATE-V4] Xem trước khi snapshot đã khớp: \"Tất cả 2 PO … không cần Update\"", t[:300])
        ok(await (await run_btn(pg)).count() == 0 and "PO chưa có WTS" not in t and "PO đang dở dang" not in t,
           "V2 [STD-UPDATE-V4] ẩn nút \"Chạy Update (PIN)\" và cả 2 mục PO", t[:300])
        await modal(pg).locator("button:has-text('Đóng')").click(); await pg.wait_for_timeout(300)

        print("[U] Part Master đổi T.Gá NC 1 10 → 12: mục PO chưa có WTS hiện, mục rỗng ẩn, tick sẵn PO; bấm Chạy báo lý do thay vì khoá nút")
        await set_nc1_setup(pg, 12)
        d = dump()
        ok(snap_setup(d, "PO-STD1") == [10] and snap_setup(d, "PO-STD2") == [10], "U0 Lưu nháp Part Master: PO giữ snapshot cũ 10 (chưa Update)", d["snapm"])
        await open_part(pg); await open_upd(pg); await preview(pg)
        t = await mtext(pg)
        ok("PO chưa có WTS" in t and "PO đang dở dang" not in t, "U1 [STD-UPDATE-V4] chỉ hiện mục \"PO chưa có WTS\"; mục \"PO đang dở dang\" không có PO → ẩn hẳn", t[:400])
        c1 = await (await po_box(pg, "PO-STD1")).is_checked(); c2 = await (await po_box(pg, "PO-STD2")).is_checked()
        ok(c1 and c2 and "đang chọn 2" in t.replace("\n", " "), "U2 [STD-UPDATE-V3][STD-UPD] sau Xem trước: tick sẵn cả 2 PO có thay đổi (chưa xong)", f"{c1} {c2} · {t[:200]}")
        ok(await (await run_btn(pg)).count() == 1, "U3 [STD-UPDATE-V4] có thay đổi → hiện nút \"Chạy Update (PIN)\"")
        await (await run_btn(pg)).click(); await pg.wait_for_timeout(600)
        t = await pg.inner_text("body")
        ok("Nhập Lý do trước khi chạy." in t and await pg.locator("input[type=password]").count() == 0,
           "U4 [STD-UPDATE-V4] chưa nhập lý do bấm Chạy → báo \"Nhập Lý do trước khi chạy.\", không mở PIN", [l for l in t.split("\n") if "Lý do" in l][:3])
        await modal(pg).locator("input[placeholder^='VD: Sửa T.GC']").fill("cap nhat TG chuan"); await pg.wait_for_timeout(300)
        await modal(pg).locator("button:has-text('Bỏ chọn tất cả')").first.click(); await pg.wait_for_timeout(400)
        await (await run_btn(pg)).click(); await pg.wait_for_timeout(600)
        t = await pg.inner_text("body")
        ok("Chưa chọn PO nào để Update." in t and await pg.locator("input[type=password]").count() == 0,
           "U5 [STD-UPDATE-V4] bỏ chọn hết PO bấm Chạy → báo \"Chưa chọn PO nào để Update.\"", [l for l in t.split("\n") if "chọn" in l][:3])

        print("[F] Part Master đổi sau lúc Xem trước (tab khác) → Chạy bị dừng, bắt Xem trước lại ([STD-5] — trang gửi dấu bản Xem trước)")
        await (await po_box(pg, "PO-STD1")).check(); await pg.wait_for_timeout(300)
        pb = await login(b); await open_part(pb); await set_nc1_setup(pb, 13); await pb.context.close()
        d = dump()
        ok([s["setupTime"] for s in d["steps"] if s["part"] == "PART-STD"] == [13], "F0 tab khác Lưu nháp Part Master NC 1 T.Gá = 13", d["steps"])
        await (await run_btn(pg)).click(); await pg.wait_for_timeout(600)
        await pg.locator("input[type=password]").fill("1111"); await pg.wait_for_timeout(200)
        await pg.locator(".mes-modal button.btn-primary:has-text('Xác nhận')").last.click(); await pg.wait_for_timeout(2500)
        t = await mtext(pg); d = dump()
        ok("vừa thay đổi sau lúc bấm Xem trước" in t and snap_setup(d, "PO-STD1") == [10] and not d["batches"],
           "F1 [STD-5] Chạy (PIN) với bản Xem trước cũ (12) → \"…vừa thay đổi sau lúc bấm Xem trước…\", snapshot vẫn 10, không có batch",
           f"{[l for l in t.split(chr(10)) if 'Lỗi' in l or 'Update' in l][:3]} · {d['snapm']} · {d['batches']}")

        print("[R] Xem trước lại, chỉ tick PO-STD1 → Chạy: chỉ PO-STD1 lấy số mới ([STD-UPDATE-V3])")
        await preview(pg)
        t = await mtext(pg)
        await (await po_box(pg, "PO-STD2")).uncheck(); await pg.wait_for_timeout(300)
        await modal(pg).locator("input[placeholder^='VD: Sửa T.GC']").fill("cap nhat TG chuan"); await pg.wait_for_timeout(300)
        await (await run_btn(pg)).click(); await pg.wait_for_timeout(600)
        await pg.locator("input[type=password]").fill("1111"); await pg.wait_for_timeout(200)
        await pg.locator(".mes-modal button.btn-primary:has-text('Xác nhận')").last.click(); await pg.wait_for_timeout(2500)
        t = await mtext(pg); d = dump()
        ok("Đã Update (Batch #" in t and "1 phiếu KH" in t.replace("\n", " "), "R1 Chạy xong: \"Đã Update (Batch #…)\" · 1 phiếu KH", t[:300])
        ok(snap_setup(d, "PO-STD1") == [13] and snap_setup(d, "PO-STD2") == [10] and len(d["batches"]) == 1 and d["batches"][0]["planCount"] == 1,
           "R2 [STD-UPDATE-V3] DB: PO-STD1 snapshot T.Gá 13, PO-STD2 (bỏ tick) giữ 10, batch 1 phiếu", f"{d['snapm']} · {d['batches']}")
        ok(await (await run_btn(pg)).count() == 0, "R3 [STD-UPDATE-V4] sau khi chạy: ẩn nút Chạy (không chạy lại 2 lần)")
        await pg.screenshot(path=f"std_{OUT}.png", full_page=False)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
