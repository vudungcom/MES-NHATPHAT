# [PM-NC-DUP-SVC 02/11/2026] Bấm trang Part Master thật (harness EF giả, Part PART-ML9 của HarnessSeed.SeedPmAsync):
#   - 2 người cùng mở 1 Part, mỗi người thêm cùng mã chính (Đóng gói P9 / KCS K9 / Gia công 9) → người lưu sau bị máy chủ chặn,
#     DB chỉ 1 dòng chính mã đó (trước: trang chỉ xét danh sách của mình → 2 dòng → sổ PO mới trừ NG 2 lần, lỗi seed 27253 PO16).
#   - 1 người: xoá P9 + thêm P9 mới / đổi tên P9 → P10 + thêm P9 trong CÙNG 1 lần lưu → không báo trùng oan (trang ghi Xoá → Sửa → Thêm).
#   KHÔNG kiểm Taro (khách chốt 02/11 Taro dùng chung mã CV giữa nhiều NC — gói TARO-NC-MA).
# Chạy: python3 drive_pmdup.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — chạy 1 lần / 1 lần khởi động)
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
PART = "/part-master/detail?partNo=PART-ML9"

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump2").read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if (not cond and info not in (None, "")) else ""))
def cde(d, key, part="PART-ML9"): return [x for x in d[key] if x["part"] == part and x["isActive"]]
def gc_main(d, part="PART-ML9"): return [s for s in d["steps"] if s["part"] == part and s["isActive"] and not s["isBackup"]]
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
async def save(pg, reason="kiem pmdup", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    return (await al.last.inner_text()) if await al.count() else ""
# nút "Thêm dòng" thứ add_idx: 0 GC, 1 Taro, 2 Bavia, 3 Rửa, 4 KCS, 5 Đóng gói → bảng ngay sau nút đó
async def add_cde_row(pg, add_idx, nc):
    await pg.locator("button[title='Thêm dòng']").nth(add_idx).click(); await pg.wait_for_timeout(400)
    inp = pg.locator(f"xpath=(//button[@title='Thêm dòng'])[{add_idx + 1}]/following::table[1]//tbody/tr[last()]//input").first
    await inp.fill(nc); await inp.press("Tab"); await pg.wait_for_timeout(250)
async def cde_row(pg, add_idx, nc):
    trs = pg.locator(f"xpath=(//button[@title='Thêm dòng'])[{add_idx + 1}]/following::table[1]//tbody/tr")
    for i in range(await trs.count()):
        inp = trs.nth(i).locator("input").first
        if await inp.count() and (await inp.input_value()) == nc: return trs.nth(i)
    raise Exception(f"không thấy dòng {nc}")
JS_GC_MAIN = """() => { const out = []; document.querySelectorAll('tr').forEach((tr, i) => {
    if (tr.querySelector("input[list^='dl-thietbi']")) out.push({i, nc: tr.querySelector('input').value}); }); return out; }"""
async def add_gc_row(pg, nc):
    await pg.locator("button[title='Thêm dòng']").nth(0).click(); await pg.wait_for_timeout(400)
    r = (await pg.evaluate(JS_GC_MAIN))[-1]
    inp = pg.locator("tr").nth(r["i"]).locator("input").first
    await inp.fill(nc); await inp.press("Tab"); await pg.wait_for_timeout(300)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()

        print("[N] 2 người cùng mở Part, cùng thêm 1 mã chính → người lưu sau bị máy chủ chặn ([PM-NC-DUP-SVC])")
        try:
            pa = await login(b); pb = await login(b)
            await open_part(pa); await open_part(pb)
            await add_cde_row(pa, 5, "P9"); await add_cde_row(pa, 4, "K9"); await add_gc_row(pa, "9")
            await add_cde_row(pb, 5, "P9"); await add_cde_row(pb, 4, "K9"); await add_gc_row(pb, "9")
            ta = await save(pa)
            d = dump()
            ok("✗" not in ta and [x["nc"] for x in cde(d, "pkg")].count("P9") == 1 and [x["nc"] for x in cde(d, "kcs")].count("K9") == 1 and [s["nc"] for s in gc_main(d)].count("9") == 1,
               "N1 người A lưu Đóng gói P9 + KCS K9 + Gia công NC 9: lưu được, DB mỗi mã 1 dòng", lines(ta, "✗", "⚠"))
            tb = await save(pb, wait=4000)
            d = dump(); body = await pb.inner_text("body")
            ok([x["nc"] for x in cde(d, "pkg")].count("P9") == 1 and [x["nc"] for x in cde(d, "kcs")].count("K9") == 1 and [s["nc"] for s in gc_main(d)].count("9") == 1,
               "N2 người B (trang mở trước khi A lưu) lưu cùng mã: DB VẪN mỗi mã 1 dòng chính (trước: thành 2 dòng P9 / K9 / NC 9)",
               f"P9×{[x['nc'] for x in cde(d, 'pkg')].count('P9')} K9×{[x['nc'] for x in cde(d, 'kcs')].count('K9')} NC9×{[s['nc'] for s in gc_main(d)].count('9')}")
            ok("✗ [Đóng gói] NC=P9" in tb and "P9 đã có ở dòng chính khác" in tb and "✗ [Kiểm tra] NC=K9" in tb and "✗ [Machining] NC=9" in tb,
               "N3 người B thấy lời báo từng dòng \"✗ [Đóng gói] NC=P9: … P9 đã có ở dòng chính khác … Tải lại trang\" (KCS, Gia công tương tự)", lines(tb, "✗", "⚠"))
            ok("Đang xử lý" not in body and "Lỗi hệ thống" not in tb, "N4 trang B không treo, không báo lỗi hệ thống", lines(body, "Đang xử lý", "Lỗi hệ thống"))
        except Exception as e:
            ok(False, "N! pha [N] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])

        print("[O] 1 lần lưu: xoá P9 + thêm P9 mới; đổi tên P9 → P10 + thêm P9 → không báo trùng oan (trang ghi Xoá → Sửa → Thêm)")
        try:
            pg = await login(b); await open_part(pg)
            d0 = dump(); old_id = [x["stepId"] for x in cde(d0, "pkg") if x["nc"] == "P9"][0]
            r = await cde_row(pg, 5, "P9")
            await r.locator("button[title='Xóa']").click(); await pg.wait_for_timeout(400)
            await add_cde_row(pg, 5, "P9")
            # dòng mới mang T.Chuẩn 2.5 (dòng cũ trống) → nhận ra dòng mới (DB giả có thể cấp lại đúng số StepId vừa xoá)
            n = pg.locator("xpath=(//button[@title='Thêm dòng'])[6]/following::table[1]//tbody/tr[last()]//input[@type='number']").first
            await n.fill("2.5"); await n.press("Tab"); await pg.wait_for_timeout(300)
            t = await save(pg)
            d = dump(); p9 = [x for x in cde(d, "pkg") if x["nc"] == "P9"]
            ok("✗" not in t and len(p9) == 1 and p9[0]["standardTime"] == 2.5,
               "O1 xoá dòng P9 + thêm dòng P9 mới cùng 1 lần lưu: lưu được, DB 1 dòng P9 (dòng mới, T.Chuẩn 2.5)", f"{lines(t, '✗', '⚠')} · {p9} · cũ {old_id}")
            await open_part(pg)
            r = await cde_row(pg, 5, "P9")
            await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            r = await cde_row(pg, 5, "P9"); inp = r.locator("input").first
            await inp.fill("P10"); await inp.press("Tab"); await pg.wait_for_timeout(300)
            await add_cde_row(pg, 5, "P9")
            t = await save(pg)
            d = dump(); ncs = sorted(x["nc"] for x in cde(d, "pkg"))
            ok("✗" not in t and ncs.count("P9") == 1 and ncs.count("P10") == 1,
               "O2 đổi tên P9 → P10 + thêm P9 mới cùng 1 lần lưu: lưu được, DB có P10 và 1 dòng P9", f"{lines(t, '✗', '⚠')} · {ncs}")
            # O3 [rà độc lập V1] đổi tên NỐI CHUỖI trong 1 lần lưu: dòng P1 → P10 (đứng TRƯỚC trên form) và dòng P10 → P12 → trang ghi P10 → P12 trước
            await open_part(pg)
            for frm, to in (("P1", "P10"), ("P10", "P12")):
                r = await cde_row(pg, 5, frm)
                await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
                r = await cde_row(pg, 5, frm); inp = r.locator("input").first
                await inp.fill(to + "~"); await inp.press("Tab"); await pg.wait_for_timeout(200)
            for to in ("P10", "P12"):   # gõ tạm "~" để cde_row không nhầm dòng vừa đổi với dòng còn tên cũ, rồi gõ lại đúng mã
                r = await cde_row(pg, 5, to + "~"); inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(200)
            t = await save(pg)
            d = dump(); ncs = sorted(x["nc"] for x in cde(d, "pkg"))
            ok("✗" not in t and ncs == ["P10", "P12", "P9"],
               "O3 đổi tên nối chuỗi P1 → P10 + P10 → P12 cùng 1 lần lưu (dòng P1 đứng trước): lưu được cả 2, DB P10 / P12 / P9", f"{lines(t, '✗', '⚠')} · {ncs}")
            # O4 đổi chéo P10 ↔ P9 → trang chặn trước khi lưu, báo đổi qua mã tạm, DB không đổi
            await open_part(pg)
            for frm, to in (("P10", "P9~"), ("P9", "P10~")):
                r = await cde_row(pg, 5, frm)
                await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
                r = await cde_row(pg, 5, frm); inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(200)
            for to in ("P9", "P10"):
                r = await cde_row(pg, 5, to + "~"); inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(200)
            t = await save(pg)
            d = dump(); ncs2 = sorted(x["nc"] for x in cde(d, "pkg"))
            ok("đổi chéo mã" in t and ncs2 == ncs, "O4 đổi chéo P10 ↔ P9 trong 1 lần lưu → trang chặn \"đổi chéo mã … đổi 1 dòng sang mã tạm\", DB không đổi", f"{lines(t, 'chéo', '✗', '⚠')} · {ncs2}")
            await pg.screenshot(path=f"pmdup_{OUT}.png", full_page=False)
        except Exception as e:
            ok(False, "O! pha [O] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
