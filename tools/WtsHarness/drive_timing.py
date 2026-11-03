# [TIMING-COPY-MAIN][RA-CASCADE-PM 03/11/2026] Bấm trang Part Master thật (harness EF giả, Part PART-ML9 của HarnessSeed.SeedPmAsync):
#   NC 1 (M1, giờ 5/7/1/2/3) + đồng dạng M2, M3 cùng giờ · NC 3 (M1, 3/3/1/2/3) chưa có đồng dạng.  DB đọc qua /harness/dump2.
#   (a) dòng máy đồng dạng MỚI đi theo dòng chính tới khi lưu; sửa 1 ô chép của dòng → dòng thôi theo; chọn máy không tính là sửa.
#   (b) nút "Chọn nhiều máy đồng dạng" ở dòng chính: dòng ĐÃ LƯU giữ TimingId + giờ + ✓ (chỉ rút máy bỏ tích, hết máy thì xoá),
#       không bao giờ gán sang máy khác (rà độc lập C1); chỉ gộp các dòng MỚI giống hệt / đi theo dòng chính.
# Chạy: python3 drive_timing.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — chạy 1 lần / 1 lần khởi động)
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
def steps(d, part="PART-ML9"): return [s for s in d["steps"] if s["part"] == part]
def step_id(d, nc): return [s["stepId"] for s in steps(d) if s["nc"] == nc and s["isActive"] and not s["isBackup"]][0]
def tims(d, nc): return [t for t in d["timings"] if t["stepId"] == step_id(d, nc) and t["isActive"]]
def tv(t): return (t["timingId"], t["soMay"], t["setupTime"], t["machiningTime"])
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)

JS_ROWS = """() => { const out = []; let nc = null;
  document.querySelectorAll('tr').forEach((tr, i) => {
    const main = tr.querySelector("input[list^='dl-thietbi']");
    if (main) { nc = tr.querySelector('input').value; out.push({i, kind:'main', nc, may: main.value, txt: tr.innerText, cls: tr.className}); return; }
    const sub = tr.querySelector("button[title='Xem lịch sử máy này'],button[title='Chọn máy'],button[title='Xóa máy đồng dạng này'],button[title='Khôi phục máy đồng dạng này']");
    if (sub && nc !== null) out.push({i, kind:'sub', nc, may: tr.querySelector('input').value, txt: tr.innerText, cls: tr.className,
        nums: [...tr.querySelectorAll('input[type=number]')].map(x => x.value)});
  }); return out; }"""
async def rows(pg): return await pg.evaluate(JS_ROWS)
async def main_row(pg, nc):
    r = [x for x in await rows(pg) if x["kind"] == "main" and x["nc"] == nc][0]; return pg.locator("tr").nth(r["i"])
async def sub_rows(pg, nc): return [x for x in await rows(pg) if x["kind"] == "sub" and x["nc"] == nc]
async def sub_row(pg, nc, k): return pg.locator("tr").nth((await sub_rows(pg, nc))[k]["i"])
async def modal_click(pg, may):
    await pg.locator(".modal .list-group-item", has=pg.locator(f"strong:text-is('{may}')")).first.click(); await pg.wait_for_timeout(250)
async def modal_ok(pg):
    await pg.locator(".modal button.btn-primary:has-text('Xác nhận')").click(); await pg.wait_for_timeout(400)
async def add_sub(pg, nc):
    await (await main_row(pg, nc)).locator("xpath=following-sibling::tr[contains(.,'Thêm máy đồng dạng')][1]//button[contains(.,'Thêm máy đồng dạng')]").click()
    await pg.wait_for_timeout(400)
async def pick_sub_machine(pg, nc, k, check=(), uncheck=()):
    r = await sub_row(pg, nc, k)
    if await r.locator("button[title='Sửa dòng máy đồng dạng này']").count():
        await r.locator("button[title='Sửa dòng máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
    r = await sub_row(pg, nc, k); await r.locator("button[title='Chọn máy']").click(); await pg.wait_for_timeout(400)
    for m in uncheck: await modal_click(pg, m)
    for m in check: await modal_click(pg, m)
    await modal_ok(pg)
async def main_num(pg, nc, idx, val):   # idx 0 Gá lắp, 1 Gia công, 2 K.Tra, 3 Chuẩn bị, 4 Chạy thử
    n = (await main_row(pg, nc)).locator("input[type=number]").nth(idx)
    await n.fill(str(val)); await n.press("Tab"); await pg.wait_for_timeout(350)
async def sub_num(pg, nc, k, idx, val):
    r = await sub_row(pg, nc, k)
    if await r.locator("button[title='Sửa dòng máy đồng dạng này']").count():
        await r.locator("button[title='Sửa dòng máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
    n = (await sub_row(pg, nc, k)).locator("input[type=number]").nth(idx)
    await n.fill(str(val)); await n.press("Tab"); await pg.wait_for_timeout(350)
async def main_pencil(pg, nc):
    mr = await main_row(pg, nc)
    if await mr.locator("button[title='Sửa dòng này']").count():
        await mr.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
async def main_alt(pg, nc, check=(), uncheck=()):
    await main_pencil(pg, nc)
    await (await main_row(pg, nc)).locator("button[title='Chọn nhiều máy đồng dạng']").click(); await pg.wait_for_timeout(400)
    for m in uncheck: await modal_click(pg, m)
    for m in check: await modal_click(pg, m)
    await modal_ok(pg)
async def add_gc_row(pg, nc, may):
    await pg.locator("button[title='Thêm dòng']").nth(0).click(); await pg.wait_for_timeout(400)
    r = [x for x in await rows(pg) if x["kind"] == "main"][-1]
    tr = pg.locator("tr").nth(r["i"])
    inp = tr.locator("input").first; await inp.fill(nc); await inp.press("Tab"); await pg.wait_for_timeout(300)
    m = (await main_row(pg, nc)).locator("input[list^='dl-thietbi']"); await m.fill(may); await m.press("Tab"); await pg.wait_for_timeout(400)
async def save(pg, reason="kiem timing", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    return (await al.last.inner_text()) if await al.count() else ""

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()

        print("[A] Dòng máy đồng dạng mới đi theo dòng chính tới khi lưu ([TIMING-COPY-MAIN][RA-CASCADE-PM (a)])")
        try:
            pg = await login(b); await open_part(pg)
            await add_gc_row(pg, "5", "M1")
            await add_sub(pg, "5"); await pick_sub_machine(pg, "5", 0, check=["M2"])
            s0 = (await sub_rows(pg, "5"))[0]
            ok("theo dòng chính" in s0["txt"], "A1 dòng đồng dạng mới (bấm Thêm trước khi gõ giờ dòng chính, đã chọn máy M2) hiện nhãn \"theo dòng chính\"", s0["txt"][:200])
            await main_num(pg, "5", 0, 6); await main_num(pg, "5", 1, 8)
            s0 = (await sub_rows(pg, "5"))[0]
            ok(s0["nums"][:2] == ["6", "8"], "A2 gõ giờ dòng chính SAU khi thêm dòng đồng dạng (Gá 6, Gia công 8) → dòng đồng dạng hiện 6 / 8 ngay (trước: trống)", s0["nums"])
            await add_sub(pg, "5"); await pick_sub_machine(pg, "5", 1, check=["M3"])
            await sub_num(pg, "5", 1, 0, 2)
            await main_num(pg, "5", 1, 9)
            ss = await sub_rows(pg, "5")
            ok(ss[0]["nums"][:2] == ["6", "9"] and ss[1]["nums"][:2] == ["2", "8"] and "theo dòng chính" not in ss[1]["txt"],
               "A3 dòng M3 sửa Gá = 2 → thôi theo; đổi Gia công dòng chính 8 → 9: dòng M2 (đi theo) 6 / 9, dòng M3 (đã sửa) giữ 2 / 8, mất nhãn", [x["nums"] for x in ss])
            # [rà độc lập T1] mở modal đồ gá của dòng đi theo (M2), Xác nhận không đổi gì → vẫn đi theo
            r = await sub_row(pg, "5", 0); await r.locator("button[title='Chọn nhiều đồ gá']").click(); await pg.wait_for_timeout(400)
            await pg.locator(".modal button.btn-success:has-text('Xác nhận')").click(); await pg.wait_for_timeout(400)
            s0 = (await sub_rows(pg, "5"))[0]
            ok("theo dòng chính" in s0["txt"], "A3b mở modal đồ gá dòng đi theo, Xác nhận không đổi gì → dòng vẫn đi theo (nhãn còn)", s0["txt"][:200])
            t = await save(pg)
            d = dump(); tt = sorted((x["soMay"], x["setupTime"], x["machiningTime"]) for x in tims(d, "5")) if [s for s in steps(d) if s["nc"] == "5"] else []
            ok("✗" not in t and tt == [("M2", 6, 9), ("M3", 2, 8)], "A4 Lưu: DB NC 5 có M2 6 / 9 (số dòng chính lúc lưu) và M3 2 / 8 (số riêng)", f"{lines(t, '✗', 'Timing')} · {tt}")
            ss = await sub_rows(pg, "5")
            ok(all("theo dòng chính" not in x["txt"] for x in ss), "A5 sau khi lưu không còn nhãn \"theo dòng chính\" (dòng đã lưu giữ số riêng)", [x["txt"][:80] for x in ss])
            await open_part(pg)
            await main_pencil(pg, "5"); await main_num(pg, "5", 0, 7)
            ss = await sub_rows(pg, "5")
            ok([x["nums"][0] for x in ss] == ["6", "2"], "A6 mở lại trang, đổi Gá dòng chính 6 → 7: dòng đồng dạng ĐÃ LƯU không tự đổi (6, 2)", [x["nums"] for x in ss])
        except Exception as e:
            ok(False, "A! pha [A] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])

        print("[B] Nút \"Chọn nhiều máy đồng dạng\" ở dòng chính: giữ dòng có giờ riêng, gộp dòng giống hệt, dùng lại TimingId ([RA-CASCADE-PM (b)])")
        try:
            pg = await login(b); await open_part(pg)
            d0 = dump(); t0 = {x["soMay"]: x for x in tims(d0, "1")}
            await sub_num(pg, "1", 1, 0, 9)   # M3: Gá 5 → 9 (giờ riêng)
            t = await save(pg)
            d = dump(); t1 = {x["soMay"]: x for x in tims(d, "1")}
            ok("✗" not in t and t1["M3"]["setupTime"] == 9 and t1["M3"]["timingId"] == t0["M3"]["timingId"],
               "B1 sửa dòng đồng dạng M3 NC 1: Gá 5 → 9, lưu (chuẩn bị ca)", f"{lines(t, '✗')} · {[tv(x) for x in tims(d, '1')]}")
            await open_part(pg)
            await main_alt(pg, "1")   # mở modal ở dòng chính, giữ nguyên M2 + M3, Xác nhận
            ss = await sub_rows(pg, "1")
            ok(not any("line-through" in x["cls"] for x in ss) and sorted(x["may"] for x in ss) == ["M2", "M3"],
               "B2 bấm nút dòng chính, giữ nguyên M2 + M3 → không dòng nào bị gạch xoá (trước: gạch cả 2, thêm 1 dòng gộp \"M2, M3\")", [(x["may"], x["cls"]) for x in ss])
            t = await save(pg)
            d = dump(); t2 = {x["soMay"]: x for x in tims(d, "1")}
            ok("✗" not in t and sorted(t2) == ["M2", "M3"] and t2["M3"]["setupTime"] == 9 and t2["M3"]["timingId"] == t0["M3"]["timingId"] and t2["M2"]["timingId"] == t0["M2"]["timingId"],
               "B3 Lưu: DB giữ M3 Gá 9 (giờ riêng) + M2, CÙNG TimingId cũ (dấu xác nhận theo TimingId không mất)", f"{lines(t, '✗')} · {[tv(x) for x in tims(d, '1')]}")
            await open_part(pg)
            await main_alt(pg, "1", uncheck=["M3"])
            t = await save(pg)
            d = dump(); t3 = {x["soMay"]: x for x in tims(d, "1")}
            ok("✗" not in t and list(t3) == ["M2"] and t3["M2"]["timingId"] == t0["M2"]["timingId"],
               "B4 bỏ tích M3 ở nút dòng chính → chỉ dòng M3 bị xoá, M2 giữ TimingId", f"{lines(t, '✗')} · {[tv(x) for x in tims(d, '1')]}")
            # NC 3 chưa có đồng dạng: nút dòng chính tích M2 + M3 → 1 dòng gộp, đi theo dòng chính tới khi lưu
            await open_part(pg)
            await main_alt(pg, "3", check=["M2", "M3"])
            await main_num(pg, "3", 0, 4)
            ss = await sub_rows(pg, "3")
            ok(len(ss) == 1 and ss[0]["may"] == "M2, M3" and ss[0]["nums"][0] == "4" and "theo dòng chính" in ss[0]["txt"],
               "B5 NC 3: nút dòng chính tích M2 + M3 → 1 dòng gộp \"M2, M3\", đổi Gá dòng chính 3 → 4 thì dòng gộp theo 4", [(x["may"], x["nums"]) for x in ss])
            t = await save(pg)
            d = dump(); t5 = tims(d, "3")
            ok("✗" not in t and [(x["soMay"], x["setupTime"]) for x in t5] == [("M2, M3", 4)], "B6 Lưu: DB NC 3 có 1 dòng \"M2, M3\" Gá 4", f"{lines(t, '✗')} · {[tv(x) for x in t5]}")
            # dòng gộp đã lưu giống hệt dòng chính → bỏ tích M3: dùng lại TimingId, chỉ đổi máy
            await open_part(pg)
            await main_alt(pg, "3", uncheck=["M3"])
            t = await save(pg)
            d = dump(); t6 = tims(d, "3")
            ok("✗" not in t and [(x["timingId"], x["soMay"]) for x in t6] == [(t5[0]["timingId"], "M2")] if t5 else False,
               "B7 NC 3 bỏ tích M3 → dòng gộp giữ TimingId, máy còn \"M2\" (trước: xoá dòng, thêm dòng mới)", f"{lines(t, '✗')} · {[tv(x) for x in t6]}")
            # [rà độc lập C1 + V2] NC 3: 2 dòng ĐÃ LƯU giống hệt dòng chính (M2 = x, M3 = y). Tích ✓ Gá ở dòng M2 (chờ lưu),
            #   rồi nút dòng chính bỏ tích M2 → dòng x bị xoá, dòng y vẫn là M3 (KHÔNG đổi x thành M3), ✓ chờ lưu của x không ghi DB
            await open_part(pg)
            await add_sub(pg, "3"); await pick_sub_machine(pg, "3", 1, check=["M3"])
            t = await save(pg)
            d = dump(); t8 = {x["soMay"]: x for x in tims(d, "3")}
            ok("✗" not in t and sorted(t8) == ["M2", "M3"] and t8["M2"]["setupTime"] == t8["M3"]["setupTime"],
               "B8 NC 3 thêm dòng đồng dạng M3 (đi theo) → lưu: 2 dòng đã lưu M2 / M3 cùng giờ dòng chính (chuẩn bị ca)", f"{lines(t, '✗')} · {[tv(x) for x in tims(d, '3')]}")
            x_id, y_id = t8["M2"]["timingId"], t8["M3"]["timingId"]
            await open_part(pg)
            r = await sub_row(pg, "3", 0)
            await r.locator("xpath=.//input[@type='number'][1]/following-sibling::i").first.dispatch_event("click"); await pg.wait_for_timeout(400)
            await main_alt(pg, "3", uncheck=["M2"])
            ss = await sub_rows(pg, "3")
            ok([(x["may"], "line-through" in x["cls"]) for x in ss] == [("M2", True), ("M3", False)],
               "B9 bỏ tích M2 ở nút dòng chính → dòng M2 gạch xoá, dòng M3 giữ nguyên (bản đầu: dòng M2 đổi thành M3, dòng M3 bị xoá)", [(x["may"], x["cls"]) for x in ss])
            t = await save(pg)
            d = dump(); t9 = tims(d, "3")
            conf_x = [c for c in d["tconf"] if c["timingId"] == x_id]
            ok("✗" not in t and [(x["timingId"], x["soMay"]) for x in t9] == [(y_id, "M3")] and not conf_x,
               "B10 Lưu: DB NC 3 còn đúng dòng M3 với TimingId cũ của M3; dòng M2 tắt; ✓ chờ lưu của dòng M2 đã xoá KHÔNG ghi vào DB",
               f"{lines(t, '✗', 'xác nhận')} · {[tv(x) for x in t9]} · ✓ dòng M2: {conf_x}")
            await pg.screenshot(path=f"timing_{OUT}.png", full_page=False)
        except Exception as e:
            ok(False, "B! pha [B] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
