# [KHE-6][KHE-3][KHE-7 30/10/2026] Bấm trang thật "Sửa phiếu KH" (Edit_Khplan) bằng 2 người Kế hoạch (khe01 = A, khe02 = B) cùng mở 1 PO.
# Chạy: python3 drive_khe.py <port> <old|new>  → in từng bước ĐẠT / SAI, ghi khe_<old|new>.json
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
    await pg.set_viewport_size({"width": 1600, "height": 1100})
    await pg.goto(BASE + "/login")
    await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg

def field(pg, label):
    return pg.locator(f"div:has(> label:has-text('{label}')) > input").first

async def open_edit(pg, plan, det):
    await pg.goto(BASE + f"/kh-plan/{plan}/detail/{det}/edit"); await pg.wait_for_timeout(2000)

async def save(pg, reason):
    await pg.fill("textarea", reason)
    await pg.click("button:has-text('Nhập PIN & Lưu')"); await pg.wait_for_timeout(800)
    await pg.fill("input[type=password]", "1111")
    await pg.click(".mes-modal-footer button:has-text('Xác nhận')"); await pg.wait_for_timeout(1200)
    err = pg.locator(".alert-danger")
    return (await err.first.inner_text()) if await err.count() else ""

async def main():
    d0 = dump(); plan = d0["khplan"][0]["khPlanId"]
    det = [x for x in d0["khdet"] if x["khPlanId"] == plan][0]["khPlanDetailId"]
    npo = len([x for x in d0["khdet"] if x["khPlanId"] == plan])
    async with async_playwright() as p:
        b = await p.chromium.launch()
        A = await login(b, "khe01"); B = await login(b, "khe02")

        print("[1] A, B cùng mở PO; B sửa Vị trí kho rồi lưu; A sửa Ghi chú chung rồi lưu")
        await open_edit(A, plan, det); await open_edit(B, plan, det)
        await field(B, "Vị trí kho").fill("B-VITRI"); await field(B, "Vị trí kho").blur()
        eb = await save(B, "B doi vi tri")
        await field(A, "Ghi chú chung").fill("ghi chu A"); await field(A, "Ghi chú chung").blur()
        ea = await save(A, "A doi ghi chu")
        d = dump(); dd = [x for x in d["khdet"] if x["khPlanDetailId"] == det][0]; pl = d["khplan"][0]
        ok(eb == "" and ea == "", "Cả 2 lưu được (khác ô)", f"B='{eb}' A='{ea}'")
        ok(dd["location"] == "B-VITRI", "Vị trí kho của B còn nguyên (KHE-3: A không ghi đè ô mình không sửa)", dd)
        ok(pl["notes"] == "ghi chu A", "Ghi chú chung của A đã lưu", pl)
        nlog = len([l for l in d["khlog"] if l["fieldName"] == "PlanNotes" and l["newValue"] == "ghi chu A"])
        ok(nlog == npo, f"KHE-7: Ghi chú chung có lịch sử ở cả {npo} PO của phiếu", nlog)

        print("[2] A, B cùng mở; B đổi Vị trí → B2; A đổi Vị trí → A2: A bị chặn, giữ B2")
        await open_edit(A, plan, det); await open_edit(B, plan, det)
        await field(B, "Vị trí kho").fill("B2"); await field(B, "Vị trí kho").blur()
        eb = await save(B, "B doi lan 2")
        await field(A, "Vị trí kho").fill("A2"); await field(A, "Vị trí kho").blur()
        ea = await save(A, "A doi lan 2")
        dd = [x for x in dump()["khdet"] if x["khPlanDetailId"] == det][0]
        ok(eb == "" and "người khác vừa lưu" in ea and "B2" in ea, "A bị báo: ô Vị trí kho người khác vừa lưu B2", ea)
        ok(dd["location"] == "B2", "DB giữ B2", dd)

        print("[3] Workflow: B tắt (ON → OFF) rồi A (mở trước khi B tắt, không đụng công tắc) sửa Ưu tiên")
        raw0 = len(dump()["raw"])
        await open_edit(A, plan, det); await open_edit(B, plan, det)
        await B.click("#applyWorkflowEdit"); await B.wait_for_timeout(300)
        eb = await save(B, "B tat workflow")
        await field(A, "Ưu tiên").fill("GAP"); await field(A, "Ưu tiên").blur()
        ea = await save(A, "A doi uu tien")
        d = dump(); pl = d["khplan"][0]; dd = [x for x in d["khdet"] if x["khPlanDetailId"] == det][0]
        ok(eb == "" and ea == "" and pl["applyWorkflow"] is False and dd["priority"] == "GAP",
           "Workflow giữ OFF như B vừa lưu (A không ghi đè), Ưu tiên của A lưu", f"B='{eb}' A='{ea}' {pl} {dd}")
        nwf = len([l for l in d["khlog"] if l["fieldName"] == "ApplyWorkflow" and l["newValue"] == "OFF"])
        ok(nwf == npo, f"Log ON→OFF ở {npo} PO", nwf)
        locks = [r for r in d["raw"][raw0:] if "sp_getapplock" in r]   # chỉ lệnh của bước này
        ok(len(locks) >= npo + 1, f"Bước này có khoá PO: B đổi Workflow khoá {npo} PO + A sửa dòng khoá 1 PO", len(locks))

        print("[4] B bật lại Workflow (OFF → ON, không âm) → được")
        await open_edit(B, plan, det)
        await B.click("#applyWorkflowEdit"); await B.wait_for_timeout(300)
        eb = await save(B, "B bat lai")
        d = dump(); pl = d["khplan"][0]
        ok(eb == "" and pl["applyWorkflow"] is True, "Bật lại ON được", eb)
        await b.close()
    json.dump(res, open(f"khe_{OUT}.json", "w"), ensure_ascii=False, indent=1)
    print("KẾT QUẢ:", "ĐẠT hết" if all(r["dat"] for r in res) else f"{sum(not r['dat'] for r in res)} SAI")

asyncio.run(main())
