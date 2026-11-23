# [QF-P-03 23/11/2026] drive_p03.py — bấm trang Part Master thật: ghi DB qua service, MÁY CHỦ kiểm quyền (người bị rút quyền giữa phiên bị chặn).
#   Harness: Part PART-ML9 (HarnessSeed: NC 1 máy M1 + đồng dạng M2, M3), nhóm P03GC (Xem + Sửa PART_GC) + user p03gc (Stubs/HarnessP03.cs).
#   STEP P1 p03gc mở Part, sửa Gá dòng đồng dạng M2 NC 1 → 11; Admin rút quyền Sửa PART_GC (ngoài trang) → bấm Lưu: báo "không có quyền sửa vùng",
#           DB M2 giữ nguyên (bản cũ 040bc56: trang ghi thẳng Db → M2 = 11 → SAI là đúng)
#   STEP P2 trả quyền → bấm Lưu lại (trang vẫn mở): lưu được, DB M2 = 11, người sửa = p03gc, có lịch sử
#   STEP P3 modal "Cài đặt máy loại trừ": tích H04, rút quyền → Lưu: báo lỗi trong modal, DB không đổi; trả quyền → Lưu: DB có H04
#   STEP P4 cờ Xác nhận OK: sửa máy + bị rút quyền rồi bấm "Lưu & Xác nhận OK" → máy chủ chặn, cờ vùng B giữ OK (bản cũ: trang ghi cờ NG thẳng)
# Chạy trên harness VỪA khởi động: python3 drive_p03.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2] if len(sys.argv) > 2 else "x"
BASE = f"http://127.0.0.1:{PORT}"
PART = "/part-master/detail?partNo=PART-ML9"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except Exception as e: return {"ok": False, "error": str(e)}
def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/p03").read().decode())
def m2(d): return [t for t in d["timings"] if t["soMay"] == "M2"]

JS_ROWS = """() => { const out = []; let nc = null;
  document.querySelectorAll('tr').forEach((tr, i) => {
    const main = tr.querySelector("input[list^='dl-thietbi']");
    if (main) { nc = tr.querySelector('input').value; out.push({i, kind:'main', nc}); return; }
    const sub = tr.querySelector("button[title='Xem lịch sử máy này'],button[title='Chọn máy'],button[title='Xóa máy đồng dạng này'],button[title='Khôi phục máy đồng dạng này']");
    if (sub && nc !== null) out.push({i, kind:'sub', nc, may: tr.querySelector('input').value});
  }); return out; }"""

async def login(b, user, pw="123456"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def sub_row_may(pg, nc, may):
    r = [x for x in await pg.evaluate(JS_ROWS) if x["kind"] == "sub" and x["nc"] == nc and x["may"] == may][0]
    return pg.locator("tr").nth(r["i"])
async def sub_num(pg, nc, may, idx, val):
    r = await sub_row_may(pg, nc, may)
    if await r.locator("button[title='Sửa dòng máy đồng dạng này']").count():
        await r.locator("button[title='Sửa dòng máy đồng dạng này']").click(); await pg.wait_for_timeout(300)
    n = (await sub_row_may(pg, nc, may)).locator("input[type=number]").nth(idx)
    await n.fill(str(val)); await n.press("Tab"); await pg.wait_for_timeout(350)
async def save(pg, reason="kiem p03", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    return (await al.last.inner_text()) if await al.count() else ""

async def main():
    r = post("/harness/p03-seed"); ok(r.get("ok"), "Tạo nhóm P03GC + user p03gc", r)
    async with async_playwright() as p:
        b = await p.chromium.launch()
        try:
            print("STEP P1 bị rút quyền giữa phiên → lưu máy đồng dạng bị máy chủ chặn")
            pg = await login(b, "p03gc"); await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
            d0 = dump(); s0 = m2(d0)[0]["setupTime"]; l0 = d0["logs"]
            await sub_num(pg, "1", "M2", 0, 11)
            post("/harness/p03-perm?edit=0")
            t = await save(pg)
            d1 = dump()
            ok("không có quyền sửa vùng" in t and m2(d1)[0]["setupTime"] == s0 and d1["logs"] == l0,
               "P1 Admin rút quyền Sửa PART_GC khi trang đang mở → bấm Lưu: báo 'không có quyền sửa vùng', DB máy M2 + lịch sử không đổi", (t[:400], m2(d1), d1["logs"], l0))

            print("STEP P2 trả quyền → lưu lại được")
            post("/harness/p03-perm?edit=1")
            t = await save(pg)
            d2 = dump()
            ok("sửa M2" in t and m2(d2)[0]["setupTime"] == 11 and m2(d2)[0]["updatedBy"] is not None and d2["logs"] > l0,
               "P2 trả quyền, bấm Lưu lại trên trang đang mở → lưu M2 Gá = 11, có người sửa + lịch sử", (t[:400], m2(d2), d2["logs"]))

            print("STEP P3 máy loại trừ toàn hệ thống")
            await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
            await pg.locator("button[title='Cài đặt danh sách máy loại trừ (toàn hệ thống)']").click(); await pg.wait_for_timeout(600)
            ex0 = dump()["exclude"]
            items = pg.locator(".modal .list-group-item")
            may = None
            for i in range(await items.count()):
                it = items.nth(i)
                if not await it.locator("input[type=checkbox]").is_checked():
                    may = (await it.locator("strong").first.inner_text()).strip(); await it.locator("input[type=checkbox]").check(); break
            await pg.wait_for_timeout(300)
            ok(may is not None, "P3 tiền đề: modal có máy chưa tích", ex0)
            post("/harness/p03-perm?edit=0")
            await pg.locator(".modal button.btn-danger").last.click(); await pg.wait_for_timeout(1500)
            al = await pg.locator(".modal .alert-danger").inner_text() if await pg.locator(".modal .alert-danger").count() else ""
            ok("không có quyền sửa vùng" in al and dump()["exclude"] == ex0, "P3a bị rút quyền → Lưu máy loại trừ: báo lỗi trong modal, DB không đổi", (al, dump()["exclude"]))
            post("/harness/p03-perm?edit=1")
            await pg.locator(".modal button.btn-danger").last.click(); await pg.wait_for_timeout(1500)
            ex1 = dump()["exclude"]
            ok(may in ex1 and await pg.locator(".modal .alert-danger").count() == 0, "P3b trả quyền → Lưu: DB có máy vừa tích, modal đóng", (may, ex1))

            print("STEP P4 cờ Xác nhận OK")
            await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
            okv = dump()["machiningOk"]
            await sub_num(pg, "1", "M2", 0, 12)
            post("/harness/p03-perm?edit=0")
            t = await save(pg, wait=2500)
            d4 = dump()
            ok(okv is True and d4["machiningOk"] is True and m2(d4)[0]["setupTime"] == 11 and "quyền" in t,
               "P4 bị rút quyền → 'Lưu & Xác nhận OK': máy chủ chặn cả máy đồng dạng lẫn cờ Xác nhận vùng B (cờ giữ OK, bản cũ: trang tự ghi cờ NG)", (t[:300], okv, d4["machiningOk"], m2(d4)))
            post("/harness/p03-perm?edit=1")
        except Exception as e:
            ok(False, "P! dừng giữa chừng — lỗi bấm trang", repr(e)[:400])
        finally:
            await b.close()
    print(f"\n[drive_p03 {OUT}] {'ĐẠT HẾT' if fails == 0 else str(fails) + ' SAI'}")
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
