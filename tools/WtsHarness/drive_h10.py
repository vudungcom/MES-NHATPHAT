# [H10 30/10/2026] Bấm trang Giao nhận thật (harness EF giả): huỷ DÂY CHUYỀN lần nhận gốc có NG + phiếu trả tự lập + lần nhận phiếu trả.
# Dữ liệu: phiếu KH-H10 / PO-H10 (HarnessSeed): Kho → Gia công 10, Gia công nhận 8 + NG 2 → phiếu trả tự lập → Kho đã nhận lại 2.
# Chạy: python3 drive_h10.py <port> <old|new>  → in từng bước ĐẠT / SAI, ghi h10_<old|new>.json
#   Bản mới: gn01 (chỉ quyền sửa Gia công) bị chặn vì thiếu quyền Kho, không đổi gì; admin huỷ 1 lần được cả 3 chứng từ.
#   Bản master: cả 2 đều bị chặn "Huỷ lần nhận đó trước".
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
res = []

def dump():
    return json.loads(urllib.request.urlopen(BASE + "/harness/dump").read().decode())

def ok(cond, name, info=""):
    res.append({"buoc": name, "dat": bool(cond), "info": str(info)[:500]})
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:500]) if info else ""))

async def login(b, user, pw="123456"):
    ctx = await b.new_context(); pg = await ctx.new_page()
    await pg.set_viewport_size({"width": 1800, "height": 1100})
    await pg.goto(BASE + "/login")
    await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    return pg

def h10(d):
    det = [x for x in d["podet"] if x["purchaseOrder"] == "PO-H10"][0]["khPlanDetailId"]
    txs = [t for t in d["gntx"] if t["khPlanDetailId"] == det]
    tids = {t["handoverTxId"] for t in txs}
    recs = [r for r in d["gnrec"] if r["handoverTxId"] in tids]
    return txs, recs

async def open_log(pg):
    await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(3500)
    row = pg.locator("tr", has_text="PO-H10").first
    await row.locator("button[title='Lịch sử phiếu']").first.click()
    await pg.wait_for_timeout(1500)

async def void_r1(pg, reason="nhan nham"):
    """Bấm Huỷ ở lần nhận gốc (dòng đạt 8) → đọc ghi chú modal → nhập lý do → xác nhận → trả (ghi chú, lỗi)."""
    btns = pg.locator("tr:has(button[title='Huỷ lần nhận này'])")
    target = None
    for i in range(await btns.count()):
        tds = [t.strip() for t in await btns.nth(i).locator("td").all_inner_texts()]
        if "8" in tds:
            target = btns.nth(i); break
    if target is None:
        return None, "không thấy nút Huỷ ở lần nhận gốc (đạt 8)"
    await target.locator("button[title='Huỷ lần nhận này']").click(); await pg.wait_for_timeout(600)
    note = await pg.locator(".mes-modal .alert-warning").first.inner_text()
    await pg.fill(".mes-modal input[placeholder^='Nhập lý do']", reason)
    await pg.locator(".mes-modal-footer button.btn-danger").click(); await pg.wait_for_timeout(1500)
    err = ""
    if await pg.locator(".mes-modal .alert-danger").count():
        err = await pg.locator(".mes-modal .alert-danger").first.inner_text()
    return note, err

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        txs0, recs0 = h10(dump())
        ok(len(txs0) == 2 and len(recs0) == 2 and not any(r["isVoided"] for r in recs0), "Dữ liệu: PO-H10 có phiếu Kho → Gia công + phiếu trả tự lập, 2 lần nhận", (txs0, recs0))

        print("[1] gn01 (chỉ quyền sửa Gia công) huỷ lần nhận gốc của Gia công")
        pg = await login(b, "gn01")
        await open_log(pg)
        note, err = await void_r1(pg)
        txs, recs = h10(dump())
        if OUT == "new":
            ok(note is not None and "đã nhận lại 2 chiếc ở phiếu trả" in note and "huỷ kèm" in note, "Ghi chú modal báo lần nhận phiếu trả sẽ huỷ kèm (cần quyền Kho)", note)
            ok("không có quyền huỷ lần nhận đó" in err, "Bị chặn: thiếu quyền Kho cho lần nhận phiếu trả", err)
        else:
            ok("Huỷ lần nhận đó trước" in err, "Bản master: bị chặn 'Huỷ lần nhận đó trước'", err)
        ok(not any(r["isVoided"] for r in recs) and not any(t["isVoided"] for t in txs), "DB không đổi (không chứng từ nào bị huỷ)", (txs, recs))
        await pg.screenshot(path=f"h10_{OUT}_1.png")
        await pg.context.close()

        print("[2] admin (đủ quyền 5 nhóm) huỷ lần nhận gốc")
        pg = await login(b, "admin", "admin123")
        await open_log(pg)
        note, err = await void_r1(pg)
        txs, recs = h10(dump())
        r1 = [r for r in recs if r["qtyOk"] == 8][0]; r2 = [r for r in recs if r["qtyOk"] == 0][0]
        back = [t for t in txs if t["sourceReceiveId"] is not None][0]; t1 = [t for t in txs if t["sourceReceiveId"] is None][0]
        if OUT == "new":
            ok(err == "" and r1["isVoided"] and r2["isVoided"] and back["isVoided"] and t1["status"] == "PENDING",
               "Huỷ được 1 lần: lần nhận gốc + phiếu trả + lần nhận phiếu trả đều huỷ, phiếu Kho → Gia công về chờ nhận", (err, txs, recs))
            ok((r2["voidReason"] or "").startswith(f"Huỷ theo lần nhận #{r1['receiveId']}") and (back["voidReason"] or "").startswith(f"Huỷ theo lần nhận #{r1['receiveId']}"),
               "Lý do huỷ kèm ghi 'Huỷ theo lần nhận #…' (truy được)", (r2["voidReason"], back["voidReason"]))
        else:
            ok("Huỷ lần nhận đó trước" in err and not r1["isVoided"], "Bản master: admin cũng bị chặn", err)
        await pg.screenshot(path=f"h10_{OUT}_2.png")
        body = await pg.inner_text("body")
        ok("unhandled error" not in body.lower(), "Trang không lỗi")
        await b.close()
    json.dump(res, open(f"h10_{OUT}.json", "w"), ensure_ascii=False, indent=1)
    print(f"KẾT QUẢ {OUT}: {sum(r['dat'] for r in res)}/{len(res)} ĐẠT")

asyncio.run(main())
