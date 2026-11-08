# [WTS-SUA-NHOM][GOI9-OFF-CANHBAO 08/11/2026] Gói 9 — trang WTS thật (gc01, nhóm Gia công), PO-G9 Workflow OFF (POST /harness/g9-seed, HarnessSua.SeedG9Async):
#   G1 dòng đã lưu của gc01 thuộc nhóm KCS (K1): bấm "Sửa lại" → KHÔNG mở nhóm sửa, khung báo trên bảng dòng đã lưu:
#      "Dòng này của nhóm KCS — form nhập của bạn là nhóm Gia công nên không sửa ở đây được: xoá dòng rồi nhập lại …"; DB không đổi; bấm × → khung ẩn.
#   G2 dòng GC NC01 OK 3 NG 2 (vụ đã giao Kho): Sửa lại → NG 0, OK 5 → Lưu được (OFF) + khung "Đã lưu. PO PO-G9 · VL… đã đi phiếu — sau khi sửa: Gia công · tồn hàng lỗi còn -2 …".
#   G3 xoá dòng GC còn lại (vụ đã giao Kho) → xoá được + khung "Đã xoá. PO PO-G9 · VL… đã đi phiếu — sau khi xoá: … còn -2 …".
#   G4 đổi ngày làm việc → khung báo cũ ẩn (rà độc lập Gói 9 Thấp-2).
#   Bản 2cdcd14: G1 SAI (mở nhóm sửa, không báo), G2 / G3 SAI (lưu / xoá lặng lẽ, không cảnh báo).
#   Nguồn: HÙNG 08/11 07:54 + 08:01 (GOI8-THAP (a) (b) → Gói 9).
# Chạy trên harness VỪA khởi động: python3 drive_g9.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))

def wts_of(det):
    return [w for w in dump()["wts"] if w["khPlanDetailId"] == det]

async def notice(pg):
    n = pg.locator("#wts-notice")
    if await n.count() == 0 or not await n.is_visible(): return None
    return (await n.inner_text()).strip()

def saved(pg, *texts):
    loc = pg.locator("tr", has=pg.locator("button[title='Sửa lại']"))
    for t in texts: loc = loc.filter(has_text=t)
    return loc

async def main():
    det = post("/harness/g9-seed")["khPlanDetailId"]
    w0 = wts_of(det)
    ok(len([w for w in w0 if not w["isVoided"]]) == 3, "G0 seed: 3 dòng WTS PO-G9 của gc01 (1 KCS K1 + 2 GC NC01 NG 2 có vụ đã giao Kho)", w0)
    async with async_playwright() as p:
        b = await p.chromium.launch(); ctx = await b.new_context(); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "gc01"); await pg.fill("#password", "123456")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2000)
        await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(1000)
        while await pg.locator("button.btn-outline-danger.ms-auto").count():
            await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)

        print("[G1] Sửa dòng nhóm KCS ở form Gia công")
        ok(await notice(pg) is None, "G1a khung báo ẩn khi mở trang")
        rk = saved(pg, "PO-G9", "K1").first
        ok(await rk.count() == 1, "G1b bảng dòng đã lưu có dòng K1 (KCS) của gc01")
        n_groups = await pg.locator("input[placeholder='Gõ PO...']").count()
        await rk.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1200)
        txt = await notice(pg)
        ok(txt == "Dòng này của nhóm KCS — form nhập của bạn là nhóm Gia công nên không sửa ở đây được: xoá dòng rồi nhập lại ở nhóm đúng (không còn quyền nhóm KCS thì nhờ Admin).",
           "G1c bấm Sửa lại → khung báo khác nhóm (xoá dòng rồi nhập lại ở nhóm đúng)", txt)
        ok(await pg.locator("input[placeholder='Gõ PO...']").count() == n_groups, "G1d không mở nhóm sửa (form không thêm dòng)")
        ok(wts_of(det) == w0, "G1e DB không đổi")
        if txt:
            await pg.locator("#wts-notice button.btn-close").click(); await pg.wait_for_timeout(500)
        ok(await notice(pg) is None, "G1f bấm × → khung báo ẩn")
        await pg.screenshot(path=f"g9_{OUT}_g1.png", full_page=False)

        print("[G2] Workflow OFF: sửa dòng NG của vụ đã đi phiếu thành NG 0 → lưu + cảnh báo")
        gcs = sorted([w for w in w0 if not w["isVoided"] and w["nc"] == "NC01"], key=lambda w: w["start"])
        rb = saved(pg, "PO-G9", "00:30").first
        await rb.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
        row = pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
        await row.locator("input[type=number]").nth(0).fill("5"); await pg.wait_for_timeout(200)
        await row.locator("input[type=number]").nth(1).fill("0"); await pg.wait_for_timeout(300)
        await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
        txt = await notice(pg) or ""
        errs = [t.strip() for t in await pg.locator(".alert-danger").all_inner_texts() if t.strip()]
        w1 = wts_of(det)
        old_b = next((w for w in w1 if w["wtsLogId"] == gcs[0]["wtsLogId"]), {})
        new_b = [w for w in w1 if not w["isVoided"] and w["nc"] == "NC01" and w["wtsLogId"] not in (g["wtsLogId"] for g in gcs)]
        ok(old_b.get("isVoided") and len(new_b) == 1 and new_b[0]["qtyNg"] == 0 and new_b[0]["qtyOk"] == 5,
           "G2a lưu được: dòng cũ huỷ, dòng mới OK 5 NG 0", {"errs": errs, "new": new_b})
        ok(txt.startswith("Đã lưu. PO PO-G9 · VL") and "đã đi phiếu — sau khi sửa: Gia công · tồn hàng lỗi còn -2" in txt and txt.endswith("Workflow OFF nên vẫn lưu — kiểm lại số liệu."),
           "G2b khung cảnh báo: 'Đã lưu. PO PO-G9 · VL… đã đi phiếu — sau khi sửa: Gia công · tồn hàng lỗi còn -2 … Workflow OFF nên vẫn lưu — kiểm lại số liệu.'", txt)
        await pg.screenshot(path=f"g9_{OUT}_g2.png", full_page=False)

        print("[G3] Workflow OFF: xoá dòng NG của vụ đã đi phiếu → xoá + cảnh báo")
        rc = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']")).filter(has_text="PO-G9").filter(has_text="01:00").first
        await rc.locator("button[title='Xóa WTS này']").click(); await pg.wait_for_timeout(500)
        await pg.click("button.btn-danger:has-text('Xóa WTS này')"); await pg.wait_for_timeout(2500)
        txt = await notice(pg) or ""
        c_old = next((w for w in wts_of(det) if w["wtsLogId"] == gcs[1]["wtsLogId"]), {})
        ok(c_old.get("isVoided"), "G3a xoá được (OFF không chặn)", c_old)
        ok(txt.startswith("Đã xoá. PO PO-G9 · VL") and "đã đi phiếu — sau khi xoá: Gia công · tồn hàng lỗi còn -2" in txt and txt.endswith("Workflow OFF nên vẫn xoá — kiểm lại số liệu."),
           "G3b khung cảnh báo: 'Đã xoá. PO PO-G9 · VL… đã đi phiếu — sau khi xoá: … còn -2 …'", txt)
        await pg.screenshot(path=f"g9_{OUT}_g3.png", full_page=False)

        print("[G4] đổi ngày làm việc → khung báo cũ ẩn (rà độc lập Gói 9 Thấp-2)")
        import datetime
        y = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
        await pg.locator("input[type=date]").first.fill(y); await pg.locator("input[type=date]").first.dispatch_event("change"); await pg.wait_for_timeout(1500)
        ok(await notice(pg) is None, "G4 chọn ngày hôm qua → khung báo 'Đã xoá …' ẩn (không còn lời báo của ngày khác)", await notice(pg))
        await b.close()

asyncio.run(main())
print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
sys.exit(0 if fails == 0 else 1)
