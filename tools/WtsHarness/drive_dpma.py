# [B6-DP-MA][B7-TIMING-MAY 06/11/2026] Bấm trang Part Master thật (harness EF giả, Part PART-ML9 của HarnessSeed.SeedPmAsync) — sửa sau rà độc lập gói 1:
#   D (B6): nút "Tạo dự phòng" đặt mã KHÔNG trùng dòng đang có: NC 1 có 1-DP-01 + 1-DP-02, xoá 1-DP-01, tạo dự phòng mới → 1-DP-03
#       (trước: lấy số đếm + 1 → ra lại 1-DP-02 đang có → máy chủ B6 chặn "đã có ở dòng dự phòng khác", người dùng không gỡ được).
#   T (B7): NC 1 ghi Máy bấm giờ "MTIM" mà máy MTIM đã xoá (B7 cho xoá) → sửa NC 1 / tạo dự phòng NC 1 vẫn LƯU được
#       (trước: trang chặn cả lượt Lưu "Máy bấm giờ 'MTIM' không có trong danh sách thiết bị", ô này không sửa được trên trang).
#   R (B6-DP-THAP mục 6): đổi tên nối chuỗi Q1 → Q2, Q2 → Q3 + sửa giờ dự phòng mã Q1 trong 1 lần Lưu → lưu đủ (trước: dự phòng ghi trước → bị chặn, mất giờ).
#   Nguồn: rà độc lập gói 1 06/11 mục 1 (CAO) + mục 2 (VỪA) + mục 6.
#   L (PM-SNAP-MID / A1): 1 lượt Lưu giữ khoá phiên MES_PM_SAVE_ trước mọi khoá dòng, nhả sau cùng + đóng kết nối, trước bước tự cập nhật PO.
# Chạy: python3 drive_dpma.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — chạy 1 lần / 1 lần khởi động)
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
PART = "/part-master/detail?partNo=PART-ML9"

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/dump2").read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if (not cond and info not in (None, "")) else ""))
def bk(d): return sorted(s["nc"] for s in d["steps"] if s["part"] == "PART-ML9" and s["isActive"] and s["isBackup"] and s["nc"].startswith("1-DP-"))
def main1(d): return [s for s in d["steps"] if s["part"] == "PART-ML9" and s["isActive"] and not s["isBackup"] and s["nc"] == "1"][0]
def lines(t, *keys): return [l for l in t.split("\n") if any(k in l for k in keys)][:6]

async def login(b, user="admin", pw="admin123"):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg
async def open_part(pg):
    await pg.goto(BASE + PART); await pg.wait_for_timeout(2500)
async def save(pg, reason="kiem dpma", wait=3000):
    await pg.locator("textarea").last.fill(reason); await pg.wait_for_timeout(250)
    btn = pg.locator("button:has-text('Lưu & Xác nhận OK')")
    if await btn.is_disabled(): return "(nút Lưu & Xác nhận OK đang khoá — không lưu được)"
    await btn.click(); await pg.wait_for_timeout(wait)
    al = pg.locator(".alert pre")
    t = (await al.last.inner_text()) if await al.count() else ""
    body = await pg.inner_text("body")
    return t + "\n" + "\n".join(l for l in body.split("\n") if "Không thể lưu" in l or "Máy bấm giờ" in l or "✗" in l)
# dòng bảng Gia công có ô mã NC (ô input đầu) = nc
JS_ROWS = """() => { const out = []; document.querySelectorAll('tr').forEach((tr, i) => {
    if (tr.querySelector("input[list^='dl-thietbi']")) { const inp = tr.querySelector('input'); out.push({i, nc: inp ? inp.value : ''}); } }); return out; }"""
async def row(pg, nc):
    for r in await pg.evaluate(JS_ROWS):
        if r["nc"] == nc: return pg.locator("tr").nth(r["i"])
    raise Exception(f"không thấy dòng Gia công {nc}")
async def gc_codes(pg): return [r["nc"] for r in await pg.evaluate(JS_ROWS)]
async def add_backup(pg, nc="1"):
    await (await row(pg, nc)).locator("button[title='Tạo dự phòng']").click(); await pg.wait_for_timeout(500)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await login(b)

        print("[L] [PM-SNAP-MID] lượt Lưu Part Master giữ khoá phiên MES_PM_SAVE_<Part> suốt A–E, nhả trước bước tự cập nhật PO chưa chạy")
        try:
            await open_part(pg)
            rw = await row(pg, "1")
            await rw.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(400)
            nums = rw.locator("input[type=number]"); v0 = await nums.first.input_value()
            await nums.first.fill(str(int(float(v0 or 0)) + 1)); await nums.first.press("Tab"); await pg.wait_for_timeout(400)
            n0 = len(json.loads(urllib.request.urlopen(BASE + "/harness/applocks").read().decode())["raw"])
            t = await save(pg)
            al = json.loads(urllib.request.urlopen(BASE + "/harness/applocks").read().decode())
            raw = al["raw"][n0:]
            iS = next((i for i, x in enumerate(raw) if "'Session'" in x and "sp_getapplock" in x and "'Exclusive'" in x), -1)
            iR = next((i for i, x in enumerate(raw) if "sp_releaseapplock" in x), -1)
            iStep = next((i for i, x in enumerate(raw) if i > iS and "'Transaction'" in x and "'Exclusive'" in x), -1)
            iShared = [i for i, x in enumerate(raw) if "'Shared'" in x]
            ok("✗" not in t and iS >= 0 and iS < iStep < iR and not al["session"] and not al["connOpen"] and all(i > iR for i in iShared),
               "L1 lưu: lấy khoá phiên Exclusive trước khoá dòng (MES_PM_STEPS_), nhả (sp_releaseapplock) sau cùng, kết nối đóng; khoá đọc của bước tự cập nhật PO (nếu có) chỉ sau khi nhả",
               f"{lines(t, '✗', 'Không thể')} · S={iS} step={iStep} R={iR} shared={iShared} session={al['session']} conn={al['connOpen']}")
        except Exception as e:
            ok(False, "L! pha [L] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[D] [B6-DP-MA] nút \"Tạo dự phòng\" không đặt lại mã dự phòng đang có")
        try:
            await open_part(pg)
            await add_backup(pg); await add_backup(pg)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-01", "1-DP-02"], "D1 NC 1 tạo 2 dự phòng → lưu được 1-DP-01 + 1-DP-02", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
            await open_part(pg)
            await (await row(pg, "1-DP-01")).locator("button[title='Xóa']").click(); await pg.wait_for_timeout(400)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-02"], "D2 xoá 1-DP-01 → còn 1-DP-02", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
            await open_part(pg)
            before = await gc_codes(pg); await add_backup(pg)
            new = [c for c in await gc_codes(pg) if c not in before]
            ok(new == ["1-DP-03"], "D3 tạo dự phòng mới → mã 1-DP-03 (trước: 1-DP-02 trùng dòng đang có)", new)
            t = await save(pg); d = dump()
            ok(bk(d) == ["1-DP-02", "1-DP-03"] and "✗" not in t, "D4 lưu được, DB còn 1-DP-02 + 1-DP-03, không lời báo trùng", f"{bk(d)} · {lines(t, '✗', '⚠', 'Không thể')}")
        except Exception as e:
            ok(False, "D! pha [D] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[T] [B7-TIMING-MAY] Máy bấm giờ đã xoá không chặn lưu Part Master")
        try:
            r = post("/harness/b7-seed")
            await open_part(pg)
            s0 = main1(dump())
            rw = await row(pg, "1")
            await rw.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(400)
            nums = rw.locator("input[type=number]")
            newv = str(int(s0["setupTime"] or 0) + 1)
            await nums.first.fill(newv); await nums.first.press("Tab"); await pg.wait_for_timeout(400)
            t = await save(pg); s1 = main1(dump())
            ok("Máy bấm giờ" not in t and "Không thể lưu" not in t and s1["setupTime"] != s0["setupTime"],
               "T1 sửa giờ NC 1 (Máy bấm giờ MTIM đã xoá) → LƯU được (trước: \"Không thể lưu — Máy bấm giờ 'MTIM' không có trong danh sách thiết bị\")",
               f"seed={r} T.Gá {s0['setupTime']}→{s1['setupTime']} · {lines(t, 'Máy bấm giờ', 'Không thể', '✗')}")
            await open_part(pg)
            before = bk(dump()); await add_backup(pg)
            t = await save(pg); after = bk(dump())
            ok("Máy bấm giờ" not in t and "Không thể lưu" not in t and len(after) == len(before) + 1,
               "T2 tạo dự phòng NC 1 (chép Máy bấm giờ MTIM sang dòng mới) → LƯU được", f"{before} → {after} · {lines(t, 'Máy bấm giờ', 'Không thể', '✗')}")
        except Exception as e:
            ok(False, "T! pha [T] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[R] [B6-DP-THAP] (6) đổi tên nối chuỗi Q1 → Q2, Q2 → Q3 + sửa giờ dòng dự phòng mã Q1 (= mã cha) trong 1 lần Lưu")
        try:
            post("/harness/b6-seed")
            await open_part(pg)
            async def cde_rows(nc):   # các dòng bảng Đóng gói (sau nút "Thêm dòng" thứ 6) có ô mã = nc
                trs = pg.locator("xpath=(//button[@title='Thêm dòng'])[6]/following::table[1]//tbody/tr"); out = []
                for i in range(await trs.count()):
                    inp = trs.nth(i).locator("input").first
                    if await inp.count() and (await inp.input_value()) == nc: out.append(trs.nth(i))
                return out
            for frm, to in (("Q1", "Q2~"), ("Q2", "Q3~")):
                r = (await cde_rows(frm))[0]
                await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
                r = (await cde_rows(frm))[0]; inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(200)
            r = (await cde_rows("Q1"))[0]   # còn lại đúng dòng dự phòng mã Q1
            await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            r = (await cde_rows("Q1"))[0]; num = r.locator("input[type=number]").first
            await num.fill("7"); await num.press("Tab"); await pg.wait_for_timeout(200)
            for to in ("Q2", "Q3"):
                r = (await cde_rows(to + "~"))[0]; inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(200)
            t = await save(pg)
            rows = json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode())
            q = [(x["nc"], x["isBackup"], x["parentNC"], x["standardTime"]) for x in rows if x["nc"] in ("Q1", "Q2", "Q3")]
            mains = sorted(x[0] for x in q if not x[1]); bkr = [x for x in q if x[1]]
            ok("✗" not in t and mains == ["Q2", "Q3"] and len(bkr) == 1 and bkr[0][2] == "Q2" and bkr[0][3] == 7,
               "R1 lưu được: dòng chính Q2 (cũ Q1) + Q3 (cũ Q2), dự phòng trỏ cha Q2 và GIỜ MỚI 7 được lưu (trước: dự phòng bị chặn \"trùng mã dòng chính khác\", mất giờ sửa)",
               f"{lines(t, '✗', '⚠')} · {q}")
        except Exception as e:
            ok(False, "R! pha [R] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[R2] [B6-DP-THAP] T7 1 lần Lưu: dự phòng SQ (của S1) đổi mã → S1-DP-01 và dòng chính S2 đổi mã → SQ")
        try:
            await open_part(pg)
            for frm, to in (("SQ", "S1-DP-01"), ("S2", "SQ")):
                r = (await cde_rows(frm))[0]
                await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
                r = (await cde_rows(frm))[0]; inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(250)
            t = await save(pg)
            rows = json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode())
            q = [(x["nc"], x["isBackup"], x["parentNC"]) for x in rows if x["nc"] in ("S1", "S2", "SQ", "S1-DP-01")]
            ok("✗" not in t and sorted(q) == sorted([("S1", False, None), ("SQ", False, None), ("S1-DP-01", True, "S1")]),
               "R2 lưu được cả 2: dự phòng ghi trước (giải phóng mã SQ) rồi S2 → SQ (trước: S2 → SQ ghi trước, bị báo \"đang là mã dòng dự phòng của dòng khác\")",
               f"{lines(t, '✗', '⚠')} · {q}")
        except Exception as e:
            ok(False, "R2! pha [R2] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[R3] [B6-DP-THAP] đổi chéo mã dòng chính S1 ↔ dòng dự phòng S1-DP-01 trong 1 lần Lưu → lưu đủ cả 2 (ghi đúng thứ tự, không ghi dở)")
        try:
            await open_part(pg)
            for frm, to in (("S1", "S1~"), ("S1-DP-01", "S1"), ("S1~", "S1-DP-01")):
                r = (await cde_rows(frm))[0]
                if frm != "S1~":
                    await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
                    r = (await cde_rows(frm))[0]
                inp = r.locator("input").first
                await inp.fill(to); await inp.press("Tab"); await pg.wait_for_timeout(250)
            t = await save(pg)
            rows1 = json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode())
            q1 = sorted((x["nc"], x["isBackup"], x["parentNC"]) for x in rows1 if x["nc"] in ("S1", "S1-DP-01"))
            ok("✗" not in t and q1 == [("S1", True, "S1-DP-01"), ("S1-DP-01", False, None)],
               "R3 lưu được cả 2 đúng ý: dòng chính mang mã S1-DP-01, dự phòng mang mã S1 trỏ cha S1-DP-01 (thứ tự ghi T7 / N1 gói 2 — rà độc lập gói 2 ghi \"hỏng cả 2\" không tái hiện)",
               f"{lines(t, '✗', 'Không thể')} · {q1}")
        except Exception as e:
            ok(False, "R3! pha [R3] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[R4] [B6-DP-THAP] dòng chính Q2 (có dự phòng mã Q1) đổi tên Q2 → Q2X rồi bấm Xoá trong cùng lượt → xoá cả 2, lời báo đúng mã cũ")
        try:
            await open_part(pg)
            r = (await cde_rows("Q2"))[0]
            await r.locator("button[title='Sửa dòng này']").click(); await pg.wait_for_timeout(300)
            r = (await cde_rows("Q2"))[0]; inp = r.locator("input").first
            await inp.fill("Q2X"); await inp.press("Tab"); await pg.wait_for_timeout(250)
            r = (await cde_rows("Q2X"))[0]
            await r.locator("button[title='Xóa']").click(); await pg.wait_for_timeout(400)
            t = await save(pg)
            rows = json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode())
            left = [(x["nc"], x["isBackup"], x["parentNC"]) for x in rows if x["nc"] in ("Q1", "Q2", "Q2X") or x["parentNC"] in ("Q2", "Q2X")]
            ok("✗" not in t and left == [] and "Xóa NC=Q2" in t and "Q2X" not in t and "theo NC chính Q2" in t,
               "R4 lưu được: xoá dòng chính Q2 + dự phòng Q1 của nó; lời báo \"Xóa NC=Q2\" (mã trong DB) và dự phòng \"theo NC chính Q2\", không nhắc mã tạm Q2X",
               f"{lines(t, 'Xóa', '✗', 'Không thể')} · còn {left}")
        except Exception as e:
            ok(False, "R4! pha [R4] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        print("[P] [B6-DP-THAP] T5c thêm dòng Đóng gói P77 + Tạo dự phòng (P77-DP-01) rồi sửa NC dòng mới thành P78 → trang báo trước, không lưu dở")
        try:
            await open_part(pg)
            n0 = len(json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode()))
            await pg.locator("button[title='Thêm dòng']").nth(5).click(); await pg.wait_for_timeout(400)
            inp = pg.locator("xpath=(//button[@title='Thêm dòng'])[6]/following::table[1]//tbody/tr[last()]//input").first
            await inp.fill("P77"); await inp.press("Tab"); await pg.wait_for_timeout(300)
            r = (await cde_rows("P77"))[0]
            await r.locator("button[title='Tạo dự phòng']").click(); await pg.wait_for_timeout(400)
            r = (await cde_rows("P77"))[0]; inp = r.locator("input").first
            await inp.fill("P78"); await inp.press("Tab"); await pg.wait_for_timeout(300)
            t = await save(pg)
            body = await pg.inner_text("body")
            rows = json.loads(urllib.request.urlopen(BASE + "/harness/b6").read().decode())
            ok("trên trang không có dòng chính" in (t + body) and len(rows) == n0 and not any(x["nc"] in ("P78", "P77-DP-01") for x in rows),
               "P1 trang chặn trước khi lưu: \"dòng dự phòng P77-DP-01 trỏ về dòng cha P77 nhưng trên trang không có dòng chính mã đó\", không ghi dòng nào (trước: P78 lưu, dự phòng bị máy chủ báo \"người khác vừa đổi\")",
               f"{lines(t + chr(10) + body, 'dòng cha', '✗', 'Không thể')} · {[x['nc'] for x in rows][-5:]}")
        except Exception as e:
            ok(False, "P! pha [P] dừng giữa chừng — lỗi bấm trang", repr(e)[:300])

        await pg.screenshot(path=f"dpma_{OUT}.png", full_page=True)
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
