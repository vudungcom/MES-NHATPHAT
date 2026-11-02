# [ML9-DUP][PM-DUP][B6-NC-DUP][SUBROW-DEL][SUBROW-DEL-UNDO][CONFIRM-LOG-FIELD][STD-CONFIRM][SAVE-HANG-FIX 02/11/2026]
#   Bấm trang Part Master thật (harness EF giả) trên Part PART-ML9 (HarnessSeed.SeedPmAsync, thêm cuối seed):
#   NC 1 (M1) + đồng dạng M2, M3 · NC 2 (M2) DỮ LIỆU CŨ trùng: đồng dạng M2 (= máy đăng ký), M3, M3 · NC 3 (M1) chưa có đồng dạng
#   Taro T1 (3T) · Bavia B1 · Rửa R1 + R1 (dữ liệu cũ trùng) · KCS K1 · Đóng gói P1.  DB đọc qua /harness/dump2.
#   KHÔNG kiểm Taro trùng mã CV (khách đã chốt Taro dùng chung mã CV giữa 2 NC — chờ sửa) và KHÔNG kiểm [TIMING-COPY-MAIN] (chờ chốt RA-CASCADE-PM).
# Chạy: python3 drive_ml9.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — chạy 1 lần / 1 lần khởi động)
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
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:400]) if (not cond and info not in (None, "")) else ""))

def steps(d, part="PART-ML9"): return [s for s in d["steps"] if s["part"] == part]
def step_id(d, nc): return [s["stepId"] for s in steps(d) if s["nc"] == nc and s["isActive"]][0]
def tims(d, nc, active=True):
    sid = step_id(d, nc)
    return [t for t in d["timings"] if t["stepId"] == sid and (t["isActive"] or not active)]
def act_may(d, nc): return sorted(t["soMay"] for t in tims(d, nc))
def cde(d, key, part="PART-ML9"): return [x for x in d[key] if x["part"] == part and x["isActive"]]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)

# Bảng gia công: danh sách dòng (chỉ số tr toàn trang) — dòng chính / dòng máy đồng dạng (gắn NC của dòng chính phía trên)
JS_ROWS = """() => { const out = []; let nc = null;
  document.querySelectorAll('tr').forEach((tr, i) => {
    const main = tr.querySelector("input[list^='dl-thietbi']");
    if (main) { nc = tr.querySelector('input').value; out.push({i, kind:'main', nc, may: main.value, txt: tr.innerText, cls: tr.className}); return; }
    const sub = tr.querySelector("button[title='Xem lịch sử máy này'],button[title='Chọn máy'],button[title='Xóa máy đồng dạng này'],button[title='Khôi phục máy đồng dạng này']");
    if (sub && nc !== null) out.push({i, kind:'sub', nc, may: tr.querySelector('input').value, txt: tr.innerText, cls: tr.className});
  }); return out; }"""
async def rows(pg): return await pg.evaluate(JS_ROWS)
async def tr_at(pg, i): return pg.locator("tr").nth(i)
async def main_row(pg, nc):
    r = [x for x in await rows(pg) if x["kind"] == "main" and x["nc"] == nc][0]; return pg.locator("tr").nth(r["i"])
async def sub_rows(pg, nc): return [x for x in await rows(pg) if x["kind"] == "sub" and x["nc"] == nc]
async def sub_row(pg, nc, k):
    return pg.locator("tr").nth((await sub_rows(pg, nc))[k]["i"])

async def modal_items(pg):
    return await pg.evaluate("""() => [...document.querySelectorAll('.modal .list-group-item')].map(li => ({
        may: (li.querySelector('strong')||{}).innerText, checked: li.querySelector('input').checked,
        disabled: li.querySelector('input').disabled, badge: (li.querySelector('.badge.bg-warning')||{}).innerText || ''}))""")
async def modal_click(pg, may):
    await pg.locator(".modal .list-group-item", has=pg.locator(f"strong:text-is('{may}')")).first.click(); await pg.wait_for_timeout(250)
async def modal_ok(pg):
    await pg.locator(".modal button.btn-primary:has-text('Xác nhận')").click(); await pg.wait_for_timeout(400)
async def modal_cancel(pg):
    await pg.locator(".modal button:has-text('Hủy')").first.click(); await pg.wait_for_timeout(300)
async def pick_sub_machine(pg, nc, k, uncheck=(), check=(), pencil=True):
    r = await sub_row(pg, nc, k)
    if pencil and await r.locator("button[title='Sửa dòng máy đồng dạng này']").count():
        await r.locator("button[title='Sửa dòng máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
    r = await sub_row(pg, nc, k)
    await r.locator("button[title='Chọn máy']").click(); await pg.wait_for_timeout(400)
    for m in uncheck: await modal_click(pg, m)
    for m in check: await modal_click(pg, m)
    await modal_ok(pg)

async def save(pg, reason="kiem ml9", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    return (await al.last.inner_text()) if await al.count() else ""
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

# nút "Thêm dòng" thứ add_idx: 0 GC, 1 Taro, 2 Bavia, 3 Rửa, 4 KCS, 5 Đóng gói → bảng ngay sau nút đó
async def add_cde_row(pg, add_idx, nc):
    await pg.locator("button[title='Thêm dòng']").nth(add_idx).click(); await pg.wait_for_timeout(400)
    if nc is not None:
        # dòng mới = ô NC đang mở (không disabled) có giá trị "..." / trống, lấy ô cuối cùng như vậy trong bảng của mục
        inp = pg.locator(f"xpath=(//button[@title='Thêm dòng'])[{add_idx + 1}]/following::table[1]//tbody/tr[last()]//input").first
        await inp.fill(nc); await pg.wait_for_timeout(250)
async def cde_row(pg, add_idx, nc, nth=0):
    trs = pg.locator(f"xpath=(//button[@title='Thêm dòng'])[{add_idx + 1}]/following::table[1]//tbody/tr")
    found = []
    for i in range(await trs.count()):
        inp = trs.nth(i).locator("input").first
        if await inp.count() and (await inp.input_value()) == nc: found.append(trs.nth(i))
    return found[nth]

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()

        print("[D] Dữ liệu cũ trùng máy hiện đỏ ngay khi mở trang ([ML9-DUP] / [PM-DUP])")
        try:
            pg = await login(b); await open_part(pg)
            R = await rows(pg)
            nc2 = [x for x in R if x["kind"] == "main" and x["nc"] == "2"][0]
            ok("Trùng máy đồng dạng: M2" in nc2["txt"], "D1 [PM-DUP] NC 2: ô máy đăng ký M2 báo đỏ \"Trùng máy đồng dạng: M2\"", nc2["txt"][:200])
            reds = {m["nc"]: [(x["may"], "Trùng máy trong NC" in x["txt"]) for x in R if x["kind"] == "sub" and x["nc"] == m["nc"]] for m in R if m["kind"] == "main"}
            ok(reds.get("2") == [("M2", True), ("M3", True), ("M3", True)], "D2 [ML9-DUP] NC 2 (dữ liệu cũ, chưa bấm sửa): cả 3 dòng đồng dạng M2 / M3 / M3 hiện \"Trùng máy trong NC\"", reds.get("2"))
            ok(reds.get("1") == [("M2", False), ("M3", False)] and not any("Trùng" in x["txt"] for x in R if x["nc"] in ("1", "3")),
               "D3 NC 1 / NC 3 không trùng: không dòng nào báo đỏ (không báo oan)", reds)
            # modal chọn máy của dòng đồng dạng: khoá máy đăng ký + máy dòng khác, ghi lý do
            r = await sub_row(pg, "1", 0)
            await r.locator("button[title='Sửa dòng máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
            r = await sub_row(pg, "1", 0); await r.locator("button[title='Chọn máy']").click(); await pg.wait_for_timeout(400)
            it = {x["may"]: x for x in await modal_items(pg)}
            ok(it["M1"]["disabled"] and "Máy đăng ký" in it["M1"]["badge"] and it["M3"]["disabled"] and "dòng khác" in it["M3"]["badge"] and not it["M2"]["disabled"] and it["M2"]["checked"],
               "D4 [PM-DUP] modal máy của dòng đồng dạng M2 (NC 1): M1 khoá \"Máy đăng ký của NC này\", M3 khoá \"Đã chọn ở dòng khác\", M2 chọn được", it)
            await modal_cancel(pg)
            await open_part(pg)
            mr = await main_row(pg, "1")
            await mr.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            mr = await main_row(pg, "1"); await mr.locator("button[title='Chọn nhiều máy đồng dạng']").click(); await pg.wait_for_timeout(400)
            it = {x["may"]: x for x in await modal_items(pg)}
            ok(it["M1"]["disabled"] and "Máy đăng ký" in it["M1"]["badge"] and not it["M2"]["disabled"] and not it["M3"]["disabled"],
               "D5 [PM-DUP] modal máy đồng dạng mở từ dòng chính NC 1: khoá máy đăng ký M1 (trước: không khoá gì)", it)
            await modal_cancel(pg)

        except Exception as e:
            ok(False, "D! pha [D] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[P] Lưu: chặn trùng máy ở NC đang sửa, NC cũ không đụng tới thì không chặn oan ([PM-DUP])")
        try:
            pg = await login(b); await open_part(pg)
            await open_part(pg)
            mr = await main_row(pg, "1")
            await mr.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            mr = await main_row(pg, "1"); await mr.locator("input[list^='dl-thietbi']").fill("M2"); await pg.wait_for_timeout(400)
            mr = await main_row(pg, "1")
            ok("Trùng máy đồng dạng: M2" in await mr.inner_text(), "P1 [PM-DUP] đổi máy đăng ký NC 1 thành M2 (= máy đồng dạng): báo đỏ ngay", (await mr.inner_text())[:200])
            t = await save(pg)
            d = dump()
            ok("NC 1: máy M2 bị trùng ở 2 dòng" in t and [s["machineRegistered"] for s in steps(d) if s["nc"] == "1"] == ["M1"],
               "P2 [PM-DUP] Lưu bị chặn \"NC 1: máy M2 bị trùng ở 2 dòng\", DB máy đăng ký NC 1 vẫn M1", lines(t, "NC 1", "Lỗi", "✓", "⚠"))
            await open_part(pg)
            mr = await main_row(pg, "3")
            await mr.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            mr = await main_row(pg, "3"); n = mr.locator("input[type=number]").nth(0)
            await n.fill("3.5"); await n.press("Tab"); await pg.wait_for_timeout(400)
            t = await save(pg)
            d = dump()
            ok("bị trùng" not in t and [s["setupTime"] for s in steps(d) if s["nc"] == "3"] == [3.5] and act_may(d, "2") == ["M2", "M3", "M3"],
               "P3 [PM-DUP] sửa NC 3 rồi Lưu: lưu được (NC 2 dữ liệu cũ trùng không đụng tới → không chặn oan), NC 2 giữ nguyên", lines(t, "trùng", "✗", "✓"))

        except Exception as e:
            ok(False, "P! pha [P] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[M] Chốt ở chỗ GHI DB: đọc lại DB (2 tab), đổi chéo không báo oan, xoá ghi trước ([ML9-DUP])")
        try:
            pg = await login(b); await open_part(pg)
            pa = await login(b); pb = await login(b)
            await open_part(pa); await open_part(pb)
            for x in (pa, pb):
                await (await main_row(x, "3")).locator("xpath=following-sibling::tr[1]//button[contains(.,'Thêm máy đồng dạng')]").click(); await x.wait_for_timeout(400)
                await pick_sub_machine(x, "3", 0, check=["M3"], pencil=False)
            ta = await save(pa)
            d = dump()
            ok("✗" not in ta and act_may(d, "3") == ["M3"], "M1 tab A thêm máy đồng dạng M3 cho NC 3 → lưu được", lines(ta, "✗", "Timing"))
            tb = await save(pb)
            d = dump()
            ok("NC=3 M3: máy M3 đã có trong NC này" in tb and act_may(d, "3") == ["M3"],
               "M2 [ML9-DUP] tab B (trang cũ, form chưa có M3) thêm M3 cho NC 3 → máy chủ đọc lại DB, chặn; DB vẫn đúng 1 dòng M3", lines(tb, "✗", "Timing", "M3"))
            await pa.context.close(); await pb.context.close()
            # đổi chéo M2 ↔ M3 ở NC 1 (3 bước qua modal), Lưu 1 lần
            await open_part(pg)
            d = dump(); ids0 = {t["timingId"]: t["soMay"] for t in tims(d, "1")}
            await pick_sub_machine(pg, "1", 1, uncheck=["M3"])                     # dòng 2: M3 → trống
            await pick_sub_machine(pg, "1", 0, uncheck=["M2"], check=["M3"])        # dòng 1: M2 → M3
            await pick_sub_machine(pg, "1", 1, check=["M2"], pencil=False)          # dòng 2: trống → M2
            sr = [x["may"] for x in await sub_rows(pg, "1")]
            t = await save(pg)
            d = dump(); ids1 = {t2["timingId"]: t2["soMay"] for t2 in tims(d, "1")}
            ok(sr == ["M3", "M2"] and "✗" not in t and sorted(ids1) == sorted(ids0) and all(ids1[k] != ids0[k] for k in ids0) and sorted(ids1.values()) == ["M2", "M3"],
               "M3 [ML9-DUP] đổi chéo 2 dòng đồng dạng NC 1 (M2 ↔ M3) cùng 1 lượt Lưu: không báo trùng oan, DB đổi đúng 2 dòng", f"form {sr} · trước {ids0} · sau {ids1} · {lines(t, '✗', 'Timing')}")
            # dòng TRÊN lấy máy của dòng DƯỚI đang bị xoá, cùng lượt (form: [M3, M2] → xoá dòng M2, dòng M3 đổi thành M2)
            await open_part(pg)
            d = dump(); idm = {t2["soMay"]: t2["timingId"] for t2 in tims(d, "1")}
            sm = [x["may"] for x in await sub_rows(pg, "1")]
            await (await sub_row(pg, "1", sm.index("M2"))).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
            await pick_sub_machine(pg, "1", sm.index("M3"), uncheck=["M3"], check=["M2"])
            t = await save(pg)
            d = dump(); byid = {t2["timingId"]: t2 for t2 in tims(d, "1", active=False)}
            ok(sm == ["M3", "M2"] and "✗" not in t and byid[idm["M3"]]["soMay"] == "M2" and byid[idm["M3"]]["isActive"] and not byid[idm["M2"]]["isActive"] and act_may(d, "1") == ["M2"],
               "M4 [ML9-DUP] dòng trên đổi sang máy M2 của dòng dưới đang xoá, cùng lượt Lưu: không báo trùng (dòng xoá ghi trước / tính theo form), DB còn 1 dòng M2",
               f"form {sm} · {lines(t, '✗', 'Timing')} · {list(byid.values())}")
            # xoá dòng M2 rồi thêm dòng mới M2 cùng lượt
            await open_part(pg)
            d = dump(); keep = tims(d, "1")
            await (await sub_row(pg, "1", 0)).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
            await (await main_row(pg, "1")).locator("xpath=following-sibling::tr[contains(.,'Thêm máy đồng dạng')][1]//button[contains(.,'Thêm máy đồng dạng')]").click(); await pg.wait_for_timeout(400)
            nk = len(await sub_rows(pg, "1")) - 1
            await pick_sub_machine(pg, "1", nk, check=["M2"], pencil=False)
            t = await save(pg)
            d = dump(); now = tims(d, "1")
            ok("✗" not in t and len(keep) == 1 and act_may(d, "1") == ["M2"] and now and now[0]["timingId"] != keep[0]["timingId"],
               "M5 [ML9-DUP] xoá dòng M2 + thêm dòng mới M2 cùng lượt Lưu: không báo trùng, dòng cũ tắt, dòng mới M2 hiệu lực", f"{lines(t, '✗', 'Timing')} · trước {keep} · sau {now}")

        except Exception as e:
            ok(False, "M! pha [M] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[X] Xoá dòng máy đồng dạng: nút luôn hiện, gạch ngang + khôi phục, Lưu mới xoá ([SUBROW-DEL] / [SUBROW-DEL-UNDO])")
        try:
            pg = await login(b); await open_part(pg)
            await open_part(pg)
            subs2 = await sub_rows(pg, "2")
            ndel = 0
            for x in subs2: ndel += await pg.locator("tr").nth(x["i"]).locator("button[title='Xóa máy đồng dạng này']").count()
            ok(len(subs2) == 3 and ndel == 3, "X1 [SUBROW-DEL] NC 2: 3 dòng đồng dạng đều có nút Xoá ngay (chưa bấm bút chì)", f"{len(subs2)} dòng · {ndel} nút")
            await (await sub_row(pg, "2", 0)).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(400)
            s = await sub_rows(pg, "2")
            ok(len(s) == 3 and "line-through" in s[0]["cls"] and await pg.locator("tr").nth(s[0]["i"]).locator("button[title='Khôi phục máy đồng dạng này']").count() == 1
               and "Trùng máy trong NC" not in s[0]["txt"] and "Trùng máy đồng dạng" not in (await (await main_row(pg, "2")).inner_text()),
               "X2 [SUBROW-DEL-UNDO] bấm Xoá dòng M2: dòng vẫn hiện, gạch ngang, có nút khôi phục; dòng chờ xoá không còn tính trùng", s)
            await (await sub_row(pg, "2", 0)).locator("button[title='Khôi phục máy đồng dạng này']").click(); await pg.wait_for_timeout(400)
            s = await sub_rows(pg, "2")
            ok("line-through" not in s[0]["cls"] and await pg.locator("tr").nth(s[0]["i"]).locator("button[title='Xóa máy đồng dạng này']").count() == 1 and "Trùng máy trong NC" in s[0]["txt"],
               "X3 [SUBROW-DEL-UNDO] bấm khôi phục: dòng M2 trở lại bình thường (lại báo trùng như cũ)", s[0])
            # dòng mới chưa lưu bấm Xoá → bỏ luôn
            await (await main_row(pg, "2")).locator("xpath=following-sibling::tr[contains(.,'Thêm máy đồng dạng')][1]//button[contains(.,'Thêm máy đồng dạng')]").click(); await pg.wait_for_timeout(400)
            n4 = len(await sub_rows(pg, "2"))
            await (await sub_row(pg, "2", 3)).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(400)
            ok(n4 == 4 and len(await sub_rows(pg, "2")) == 3, "X4 [SUBROW-DEL] dòng mới chưa lưu bấm Xoá: bỏ luôn (không gạch ngang)", f"{n4} → {len(await sub_rows(pg, '2'))}")
            # sửa dữ liệu cũ: xoá dòng M2 + 1 dòng M3, Lưu → DB tắt 2 dòng, hết báo đỏ
            await (await sub_row(pg, "2", 0)).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
            await (await sub_row(pg, "2", 2)).locator("button[title='Xóa máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
            d = dump(); before = tims(d, "2")
            t = await save(pg)
            d = dump()
            lg = [l for l in d["logs"] if l["fieldName"] == "Deleted" and l["timingId"] in [x["timingId"] for x in before]]
            R = await rows(pg)
            ok("✗" not in t and act_may(d, "2") == ["M3"] and len(lg) == 2 and all("Xóa máy" in (l["oldValue"] or "") for l in lg),
               "X5 [SUBROW-DEL] Lưu: 2 dòng đánh dấu xoá tắt trong DB (còn M3), lịch sử ghi \"Xóa máy …\"", f"{act_may(d, '2')} · {lg} · {lines(t, '✗')}")
            ok(not any("Trùng" in x["txt"] for x in R if x["nc"] == "2") and [x["may"] for x in R if x["kind"] == "sub" and x["nc"] == "2"] == ["M3"],
               "X6 [ML9-DUP] sau khi sửa dữ liệu cũ: NC 2 hết báo đỏ, trang còn 1 dòng đồng dạng M3", [x["txt"][:80] for x in R if x["nc"] == "2"])

        except Exception as e:
            ok(False, "X! pha [X] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[B] Lưu: NC / mã CV trống hoặc trùng trong cùng mục bị chặn — GC, Bavia, Rửa, KCS, Đóng gói ([B6-NC-DUP]; Taro không kiểm)")
        try:
            pg = await login(b); await open_part(pg)
            await open_part(pg)
            d = dump(); n_before = (len(steps(d)), len(cde(d, "bavia")), len(cde(d, "washing")), len(cde(d, "kcs")), len(cde(d, "pkg")))
            await pg.locator("button[title='Thêm dòng']").nth(0).click(); await pg.wait_for_timeout(400)
            R = await rows(pg); newm = [x for x in R if x["kind"] == "main"][-1]
            await pg.locator("tr").nth(newm["i"]).locator("input").first.fill("1"); await pg.locator("tr").nth(newm["i"]).locator("input").first.press("Tab"); await pg.wait_for_timeout(300)
            await add_cde_row(pg, 2, "B1"); await add_cde_row(pg, 3, "R1"); await add_cde_row(pg, 4, "K1"); await add_cde_row(pg, 5, "P1")
            t = await save(pg)
            d = dump(); n_after = (len(steps(d)), len(cde(d, "bavia")), len(cde(d, "washing")), len(cde(d, "kcs")), len(cde(d, "pkg")))
            ok("Gia công: NC / mã CV 1 bị trùng ở 2 dòng" in t, "B1 [B6-NC-DUP] GC thêm dòng NC 1 (đã có) → \"Gia công: NC / mã CV 1 bị trùng ở 2 dòng\"", lines(t, "Gia công", "⚠"))
            ok("Bavia: NC / mã CV B1 bị trùng ở 2 dòng" in t, "B2 [B6-NC-DUP] Bavia thêm B1 (đã có) → bị chặn", lines(t, "Bavia"))
            ok("Rửa: NC / mã CV R1 bị trùng ở 3 dòng" in t, "B3 [B6-NC-DUP] Rửa thêm R1 (đã có 2 dòng cũ) → bị chặn \"ở 3 dòng\"", lines(t, "Rửa"))
            ok("KCS: NC / mã CV K1 bị trùng ở 2 dòng" in t, "B4 [B6-NC-DUP] KCS thêm K1 (đã có) → bị chặn", lines(t, "KCS"))
            ok("Đóng gói: NC / mã CV P1 bị trùng ở 2 dòng" in t, "B5 [B6-NC-DUP] Đóng gói thêm P1 (đã có) → bị chặn", lines(t, "Đóng gói"))
            ok(n_after == n_before, "B6 [B6-NC-DUP] bị chặn: DB không thêm dòng nào (GC / Bavia / Rửa / KCS / ĐG)", f"{n_before} → {n_after}")
            await open_part(pg)
            await pg.locator("button[title='Thêm dòng']").nth(0).click(); await pg.wait_for_timeout(400)
            R = await rows(pg); newm = [x for x in R if x["kind"] == "main"][-1]   # dòng GC mới tự đánh số NC → xoá trắng
            await pg.locator("tr").nth(newm["i"]).locator("input").first.fill(""); await pg.locator("tr").nth(newm["i"]).locator("input").first.press("Tab"); await pg.wait_for_timeout(300)
            await add_cde_row(pg, 2, None); await add_cde_row(pg, 4, None)
            t = await save(pg)
            d = dump(); n_after = (len(steps(d)), len(cde(d, "bavia")), len(cde(d, "washing")), len(cde(d, "kcs")), len(cde(d, "pkg")))
            ok("Gia công: có dòng chưa nhập NC / mã CV" in t and "Bavia: có dòng chưa nhập NC / mã CV" in t and "KCS: có dòng chưa nhập NC / mã CV" in t and n_after == n_before
               and not any(s["nc"] in ("-", "") for s in steps(d)),
               "B7 [B6-NC-DUP] dòng mới để trống NC (GC, Bavia, KCS) → \"có dòng chưa nhập NC / mã CV\", không lưu thành \"-\"", lines(t, "chưa nhập", "⚠"))
            await open_part(pg)
            await add_cde_row(pg, 4, "K2")
            t = await save(pg)
            d = dump()
            ok("✗" not in t and "trùng" not in t and sorted(x["nc"] for x in cde(d, "kcs")) == ["K1", "K2"] and len(cde(d, "washing")) == 2,
               "B8 [B6-NC-DUP] thêm KCS K2 (mới, không trùng) → lưu được; Rửa R1 + R1 dữ liệu cũ không đụng tới → không chặn oan", lines(t, "✗", "trùng", "✓"))

        except Exception as e:
            ok(False, "B! pha [B] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[C] Xác nhận T.Chuẩn: lưu khi bấm Lưu, lịch sử ghi rõ ô ([CONFIRM-LOG-FIELD] / [STD-CONFIRM])")
        try:
            pg = await login(b); await open_part(pg)
            await open_part(pg)
            mr = await main_row(pg, "1")
            await mr.locator("i[title='Chưa xác nhận — click để xác nhận chuẩn']").nth(0).dispatch_event("click"); await pg.wait_for_timeout(400)
            t = await save(pg)
            d = dump(); s1 = step_id(d, "1")
            tc = [c for c in d["tconf"] if c["stepId"] == s1 and c["timingId"] is None and c["fieldName"] == "SetupTime"]
            lg = [l for l in d["logs"] if l["stepId"] == s1 and l["timingId"] is None and l["fieldName"] == "TimingConfirmed"]
            ok(tc and tc[-1]["isConfirmed"] and lg and (lg[-1]["newValue"] or "").startswith("Confirmed [SetupTime]") and lg[-1]["oldValue"] == "Unconfirmed [SetupTime]",
               "C1 [CONFIRM-LOG-FIELD] tích ✓ T.Gá lắp NC 1 rồi Lưu: DB xác nhận, lịch sử ghi \"Confirmed [SetupTime] …\" (rõ ô)", f"{tc} · {lg}")
            mr = await main_row(pg, "1")
            await mr.locator("button[title='Xem lịch sử thay đổi']").click(); await pg.wait_for_timeout(1200)
            ht = await pg.locator(".modal").last.inner_text()
            ok("✓ Đã xác nhận · T.Gá lắp" in ht, "C2 [CONFIRM-LOG-FIELD] nút đồng hồ NC 1: hiện \"✓ Đã xác nhận · T.Gá lắp\"", lines(ht, "xác nhận"))
            await pg.locator(".modal .btn-close").last.click(); await pg.wait_for_timeout(300)
            # C/D/E: Bavia B1
            await open_part(pg)
            save_btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
            dis0 = await save_btn.is_disabled()
            br = await cde_row(pg, 2, "B1")
            await br.locator("i[title='Chưa xác nhận — click để xác nhận T.Chuẩn']").dispatch_event("click"); await pg.wait_for_timeout(400)
            br = await cde_row(pg, 2, "B1")
            cls = await br.locator("td").nth(2).get_attribute("class") or ""
            dis1 = await save_btn.is_disabled()
            d = dump()
            ok(dis0 and not dis1 and "table-warning" in cls and not d["sconf"], "C3 [STD-CONFIRM] tích ✓ T.Chuẩn Bavia B1: ô vàng (chờ lưu), nút Lưu mở (có thay đổi chưa lưu), DB chưa ghi",
               f"nút trước disabled={dis0} sau disabled={dis1} · class={cls} · {d['sconf']}")
            t = await save(pg)
            d = dump(); bid = [x["stepId"] for x in cde(d, "bavia") if x["nc"] == "B1"][0]
            sc = [c for c in d["sconf"] if c["stepTable"] == "Bavia" and c["stepId"] == bid]
            lg = [l for l in d["logs"] if l["stepTable"] == "Bavia" and l["stepId"] == bid and l["fieldName"] == "TimingConfirmed"]
            ok(sc and sc[-1]["isConfirmed"] and lg and (lg[-1]["newValue"] or "").startswith("Confirmed [StandardTime]"),
               "C4 [STD-CONFIRM][CONFIRM-LOG-FIELD] Lưu: DB xác nhận T.Chuẩn Bavia B1 + lịch sử \"Confirmed [StandardTime] …\"", f"{sc} · {lg} · {lines(t, 'T.Chuẩn', '✗')}")
            await open_part(pg)
            br = await cde_row(pg, 2, "B1")
            ttl = await br.locator("td").nth(2).locator("i").get_attribute("title") or ""
            ok(ttl.startswith("Đã xác nhận bởi"), "C5 [STD-CONFIRM] tải lại trang: B1 hiện đã xác nhận (xanh, tooltip người + giờ)", ttl)
            await br.locator("button[title='Xem lịch sử thay đổi']").click(); await pg.wait_for_timeout(1200)
            ht = await pg.locator(".modal").last.inner_text()
            ok("✓ Đã xác nhận · T.Chuẩn" in ht, "C6 [CONFIRM-LOG-FIELD] đồng hồ Bavia B1: \"✓ Đã xác nhận · T.Chuẩn\"", lines(ht, "xác nhận"))
            await pg.locator(".modal .btn-close").last.click(); await pg.wait_for_timeout(300)
            br = await cde_row(pg, 2, "B1")
            await br.locator("td").nth(2).locator("i").dispatch_event("click"); await pg.wait_for_timeout(300)
            t = await save(pg)
            d = dump(); sc = [c for c in d["sconf"] if c["stepTable"] == "Bavia" and c["stepId"] == bid]
            lg = [l for l in d["logs"] if l["stepTable"] == "Bavia" and l["stepId"] == bid and l["fieldName"] == "TimingConfirmed"]
            ok(sc and not sc[-1]["isConfirmed"] and lg[-1]["newValue"] == "Unconfirmed [StandardTime]" and lg[-1]["oldValue"] == "Confirmed [StandardTime]",
               "C7 [STD-CONFIRM][CONFIRM-LOG-FIELD] bấm lại + Lưu: bỏ xác nhận, lịch sử \"Unconfirmed [StandardTime]\"", f"{sc} · {lg[-1:]}")

        except Exception as e:
            ok(False, "C! pha [C] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        print("[H] Lưu gặp lỗi giữa chừng không treo trang, dòng sau vẫn lưu, lưu lại được ([SAVE-HANG-FIX])")
        try:
            pg = await login(b); await open_part(pg)
            pa = await login(b); pb = await login(b)
            await open_part(pa); await open_part(pb)
            ra = await cde_row(pa, 2, "B1")
            await ra.locator("button[title='Xóa']").click(); await pa.wait_for_timeout(400)
            ta = await save(pa)
            d = dump()
            ok(not [x for x in cde(d, "bavia") if x["nc"] == "B1"], "H1 tab A xoá Bavia B1 → đã xoá trong DB", lines(ta, "✓", "✗"))
            rb = await cde_row(pb, 2, "B1")
            await rb.locator("button[title='Sửa dòng này']").click(); await pb.wait_for_timeout(300)
            rb = await cde_row(pb, 2, "B1"); n = rb.locator("input[type=number]").first
            await n.fill("1.7"); await n.press("Tab"); await pb.wait_for_timeout(300)
            rk = await cde_row(pb, 4, "K1"); n = rk.locator("input[type=number]").first
            await n.fill("2.5"); await n.press("Tab"); await pb.wait_for_timeout(300)
            tb = await save(pb, wait=4000)
            d = dump(); body = await pb.inner_text("body")
            ok("✗ [Bavia] NC=B1" in tb, "H2 tab B (trang cũ) sửa B1 đã bị xoá → báo lỗi dòng \"✗ [Bavia] NC=B1\" ngay trên trang", lines(tb, "✗", "Lỗi"))
            ok([x["standardTime"] for x in cde(d, "kcs") if x["nc"] == "K1"] == [2.5] and "Lỗi hệ thống khi lưu" not in tb,
               "H3 [SAVE-HANG-FIX] dòng KCS K1 (lưu SAU dòng lỗi) vẫn được lưu 2.5 — lỗi 1 dòng không làm hỏng cả lượt", f"{[x for x in cde(d, 'kcs')]} · {lines(tb, '✗', 'Lỗi')}")
            ok("Đang xử lý" not in body and await pb.locator("button:has-text('Lưu & Xác nhận OK')").count() == 1, "H4 [SAVE-HANG-FIX] nút Lưu không đứng \"Đang xử lý...\"", lines(body, "Đang xử lý"))
            rp = await cde_row(pb, 5, "P1"); n = rp.locator("input[type=number]").first
            await n.fill("1.5"); await n.press("Tab"); await pb.wait_for_timeout(300)
            tb2 = await save(pb)
            d = dump()
            ok([x["standardTime"] for x in cde(d, "pkg") if x["nc"] == "P1"] == [1.5] and "✗" not in tb2,
               "H5 [SAVE-HANG-FIX] cùng trang đó sửa Đóng gói P1 rồi Lưu lại: lưu được (trang còn sống)", lines(tb2, "✗", "✓"))
            await pb.screenshot(path=f"ml9_{OUT}_h.png", full_page=False)
        except Exception as e:
            ok(False, "H! pha [H] dừng giữa chừng — lỗi bấm trang (nút / dòng cần bấm không có)", repr(e)[:300])
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
