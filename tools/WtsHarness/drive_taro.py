# [TARO-NC-MA][TARO-3T-GOP 06/11/2026] Bấm trang WTS thật (harness EF giả) — Taro dùng chung mã CV ở NC 3 / NC 5 (khách chốt B 02/11):
#   A (htsp01): chọn mã 2T → hiện ô "chọn NC Taro" (NC 3, NC 5), danh sách mã CV không lặp 2T; lưu khi chưa chọn NC → máy chủ chặn;
#     chọn NC 5 → lưu StepNC = 5, T.Chuẩn ghi dòng NC 5 (0.5, trước: luôn dòng đầu NC 3 = 1); sổ đếm theo cặp (mã, NC);
#     bảng dòng đã lưu hiện "NC 5"; Sửa lại giữ NC 5; 3T gộp 3T1: 3T NC 3 = 4 → 3T1 NC 3 chỉ còn 6.
#   P (htsp01): mã chuẩn bị 1T ở NC 3 + NC 5 cũng chọn NC → T.Chuẩn đúng dòng (rà độc lập 06/11 mục 4).
#   G (admin): trang WTS tiêu chuẩn — sửa mã 3T, tích thêm 3T2 vào "Mã gộp — làm thay các mã" (PIN 1111) → khai báo 3T ← 3T1 + 3T2.
# Dữ liệu: POST /harness/taro-seed (HarnessSua.SeedTaroAsync) — không trong seed khởi động (drive.py không đổi).
# Chạy: python3 drive_taro.py <port> <nhãn>   (harness dựng bằng mk.sh, DB giả mới tinh — 1 lần / 1 lần khởi động)
import sys, asyncio, json, re, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0

def dump(): return json.loads(urllib.request.urlopen(BASE + "/harness/taro").read().decode())
def post(path): return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))
def live(d): return [l for l in d["logs"] if not l["isVoided"]]

async def login(b, user, pw, url="/hieu-suat-cong-nhan", ca=True):
    ctx = await b.new_context(); ctx.set_default_timeout(15000); pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + url); await pg.wait_for_timeout(1800)
    if ca: await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
    return pg

def grp(pg): return pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first
async def ensure_group(pg):
    if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
        await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
async def remove_groups(pg):
    while await pg.locator("button.btn-outline-danger.ms-auto").count():
        await pg.locator("button.btn-outline-danger.ms-auto").first.click(); await pg.wait_for_timeout(300)
async def taro_row(pg, po, code, okq, start, end, actual="1"):
    await ensure_group(pg); g = grp(pg)
    row = g.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
    inp = row.locator("input[placeholder='Gõ PO...']"); await inp.fill(po); await inp.press("Tab"); await pg.wait_for_timeout(800)
    # PO trước rồi mới chọn nhóm nhỏ: danh sách mã CV nạp lúc chọn nhóm nhỏ (OnGroupHtspSubChanged — có từ trước, không đổi)
    await g.locator("select").nth(0).select_option("Taro"); await pg.wait_for_timeout(500)
    cv = row.locator("input[placeholder='Mã CV...']"); await cv.fill(code); await cv.dispatch_event("change"); await pg.wait_for_timeout(600)
    n = row.locator("input[type=number]").nth(0); await n.fill(str(okq)); await pg.wait_for_timeout(250)
    t = row.locator("input[placeholder='HH:mm']")
    await t.nth(0).fill(start); await t.nth(0).dispatch_event("change"); await pg.wait_for_timeout(250)
    await t.nth(1).fill(end); await t.nth(1).dispatch_event("change"); await pg.wait_for_timeout(250)
    a = row.locator("input[type=number][step='0.1']")
    if await a.count(): await a.first.fill(actual); await pg.wait_for_timeout(250)
    return row
def ncsel(row): return row.locator("select[title^='Mã này dùng ở nhiều NC Taro']")
async def save(pg):
    await pg.click("button:has-text('Lưu')"); await pg.wait_for_timeout(2500)
    errs = [t.strip() for t in await pg.locator(".alert-danger").all_inner_texts() if t.strip()]
    body = await pg.inner_text("body")
    return " | ".join(errs) + " || " + " | ".join(l for l in body.split("\n") if "NC Taro" in l or "vượt" in l)

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        seed = post("/harness/taro-seed")
        print(f"seed {seed}")
        pg = await login(b, "htsp01", "123456")

        print("[A] [TARO-NC-MA] form WTS Taro: mã dùng ở NC 3 + NC 5 bắt chọn NC; số + T.Chuẩn theo cặp (mã, NC)")
        try:
            row = await taro_row(pg, "PO-TARO", "2T", 3, "08:00", "08:20")
            sel = ncsel(row)
            opts = [o.strip() for o in await sel.locator("option").all_inner_texts()] if await sel.count() else []
            dl = await row.locator("input[placeholder='Mã CV...']").evaluate("e => [...(e.list ? e.list.options : [])].map(o => o.value)")
            ok(opts == ["— chọn NC Taro —", "NC 3", "NC 5"] and dl.count("2T") >= 1 and len(dl) == len(set(dl)),
               "A1 chọn mã 2T → hiện ô chọn NC (NC 3, NC 5); danh sách mã CV không lặp mã dùng chung", f"{opts} · {dl}")
            t = await save(pg); d = dump()
            ok("chọn NC" in t and not live(d), "A2 Lưu khi chưa chọn NC → chặn (máy chủ), chưa ghi dòng nào", f"{t} · {live(d)}")
            await sel.select_option("5"); await pg.wait_for_timeout(600)
            t = await save(pg); d = dump()
            lv = live(d); st = [s for s in d["stds"] if s["fieldName"] == "StandardTime"]
            ok(len(lv) == 1 and lv[0]["wtsCode"] == "2T" and lv[0]["stepNC"] == "5" and lv[0]["qtyOk"] == 3,
               "A3 chọn NC 5 → lưu 1 dòng 2T StepNC = 5, OK 3", f"{t} · {lv}")
            ok(len(st) == 1 and st[0]["stdValue"] == 0.5 and st[0]["row"] == "5|2T",
               "A4 T.Chuẩn ghi = dòng 2T NC 5 (0.5) — trước: luôn dòng đầu NC 3 (1)", st)
            ok(d["lines"]["2T (NC 5)"]["ok"] == 3 and d["lines"]["2T (NC 3)"]["ok"] == 0 and d["lines"]["2T (NC 3)"]["waiting"] == 10,
               "A5 sổ: 2T NC 5 OK 3, 2T NC 3 OK 0 chờ 10 (trước: dòng 2T cộng vào cả 2 NC)", d["lines"])
            await remove_groups(pg); await pg.wait_for_timeout(300)
            body = await pg.inner_text("body")
            ok(any("2T" in l and "NC 5" in l for l in body.split("\n")), "A6 bảng dòng đã lưu hiện \"2T · NC 5\"",
               [l for l in body.split("\n") if "2T" in l][:5])
            rs = pg.locator("tr", has=pg.locator("button[title='Xóa WTS này']")).filter(has_text="2T").first
            await rs.locator("button[title='Sửa lại']").click(); await pg.wait_for_timeout(1500)
            row = grp(pg).locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).nth(0)
            sv = await ncsel(row).input_value() if await ncsel(row).count() else None
            n = row.locator("input[type=number]").nth(0); await n.fill("4"); await pg.wait_for_timeout(300)
            t = await save(pg); d = dump(); lv = live(d)
            ok(sv == "5" and len(lv) == 1 and lv[0]["stepNC"] == "5" and lv[0]["qtyOk"] == 4 and any(l["isVoided"] and l["voidReason"] == "Sửa lại" for l in d["logs"]),
               "A7 Sửa lại: form giữ NC 5; lưu → dòng cũ huỷ \"Sửa lại\", dòng mới NC 5 OK 4", f"chọn={sv} · {t} · {d['logs']}")
        except Exception as e:
            ok(False, "A! pha [A] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        print("[B] [TARO-3T-GOP] 3T gộp 3T1 (NC 3): 3T = 4 → 3T1 NC 3 chỉ còn 6")
        try:
            await remove_groups(pg)
            row = await taro_row(pg, "PO-TARO", "3T", 4, "09:00", "09:20")
            await ncsel(row).select_option("3"); await pg.wait_for_timeout(500)
            t = await save(pg); d = dump()
            ok(d["lines"]["3T (NC 3)"]["ok"] == 4 and d["lines"]["3T (NC 3)"]["isGop"] and d["lines"]["3T1 (NC 3)"]["waiting"] == 6,
               "B1 lưu 3T NC 3 = 4: dòng 3T (NC 3) là mã gộp, 3T1 (NC 3) còn 6", f"{t} · {d['lines']}")
            await remove_groups(pg)
            row = await taro_row(pg, "PO-TARO", "3T1", 7, "09:30", "09:50")
            await ncsel(row).select_option("3"); await pg.wait_for_timeout(500)
            n0 = len(live(dump()))
            t = await save(pg); d = dump()
            ok("vượt" in t and len(live(d)) == n0, "B2 3T1 NC 3 = 7 (còn 6) → chặn vượt Chưa làm", t)
            await remove_groups(pg)
        except Exception as e:
            ok(False, "B! pha [B] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])
        print("[P] [TARO-NC-MA] mã CHUẨN BỊ 1T ở NC 3 + NC 5 (rà độc lập 06/11 mục 4): chọn NC → T.Chuẩn đúng dòng; sổ không đếm")
        try:
            await remove_groups(pg)
            n0 = len(dump()["logs"])
            row = await taro_row(pg, "PO-TARO", "1T", 0, "10:00", "10:20", actual="20")
            sel = ncsel(row)
            opts = [o.strip() for o in await sel.locator("option").all_inner_texts()] if await sel.count() else []
            t = await save(pg); d = dump()
            ok(opts == ["— chọn NC Taro —", "NC 3", "NC 5"] and len(d["logs"]) == n0, "P1 mã 1T hiện ô chọn NC (NC 3, NC 5); chưa chọn → chặn, không ghi", f"{opts} · {t}")
            await sel.select_option("5"); await pg.wait_for_timeout(600)
            t = await save(pg); d = dump()
            new = [l for l in d["logs"] if l["wtsCode"] == "1T" and not l["isVoided"]]
            st = [x for x in d["stds"] if new and x["wtsLogId"] == new[-1]["wtsLogId"] and x["fieldName"] == "StandardTime"]
            ok(len(new) == 1 and new[0]["stepNC"] == "5" and len(st) == 1 and st[0]["stdValue"] == 3 and st[0]["row"] == "5|1T" and d["balanced"],
               "P2 chọn NC 5 → lưu StepNC 5, T.Chuẩn = dòng 1T NC 5 (3) — trước: dòng NC 3 (2); sổ vẫn cân", f"{t} · {new} · {st}")
            await remove_groups(pg)
        except Exception as e:
            ok(False, "P! pha [P] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])
        await pg.screenshot(path=f"taro_{OUT}_wts.png", full_page=True)

        print("[G] [TARO-3T-GOP] trang WTS tiêu chuẩn: khai mã gộp 3T ← 3T1 + 3T2 (PIN)")
        try:
            pa = await login(b, "admin", "admin123", "/wts-tieu-chuan", ca=False)
            r3 = pa.locator("tr").filter(has=pa.locator("td.font-monospace", has_text=re.compile(r"^\s*3T\s*$"))).first
            body0 = await pa.inner_text("body")
            ok("Gộp: 3T1" in body0 and "Thuộc gộp 3T" in body0, "G1 bảng danh mục hiện nhãn \"Gộp: 3T1\" (3T) và \"Thuộc gộp 3T\" (3T1)")
            await r3.locator("button[title='Sửa']").click(); await pa.wait_for_timeout(800)
            boxes = pa.locator(".modal-body label:has(input[type=checkbox])")
            labels = [x.strip() for x in await boxes.all_inner_texts()]
            await pa.locator(".modal-body label:has(input[type=checkbox])", has_text="3T2").locator("input").check(); await pa.wait_for_timeout(300)
            await pa.locator(".modal-body input[type=password]").fill("1111")
            await pa.locator(".modal-footer button:has-text('Lưu')").click(); await pa.wait_for_timeout(1800)
            g = sorted((x["gopTaskCode"], x["partTaskCode"]) for x in dump()["gops"] if x["isActive"])
            ok("3T1" in labels and "3T2" in labels and ("3T", "3T1") in g and ("3T", "3T2") in g,
               "G2 popup Sửa 3T có ô tích mã cùng nhóm; tích 3T2 + PIN → khai báo 3T ← 3T1 + 3T2", f"{labels} · {g}")
            await pa.screenshot(path=f"taro_{OUT}_std.png", full_page=True)
        except Exception as e:
            ok(False, "G! pha [G] dừng giữa chừng — lỗi bấm trang", repr(e)[:400])

        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))

asyncio.run(main())
