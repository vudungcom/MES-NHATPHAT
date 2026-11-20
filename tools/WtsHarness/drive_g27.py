# [GÓI 27 20/11/2026] drive_g27.py — bấm trang WTS thật cho [PO-DL-NHE]: datalist Số PO ô trống 50 PO ưu tiên đầu, gõ chữ tìm toàn bộ tối đa 100.
#   Nguồn: CLAUDE quét Fable 15/11 (BW1-09); HÙNG 20/11 18:32 OK (B7). Dữ liệu: Stubs/HarnessG27.cs (130 PO G27-000…129, Workflow OFF, có snapshot NC01; G27-129 Gấp).
#   STEP L1 [PO-DL-NHE]  admin mở /hieu-suat-cong-nhan, dòng con đầu: datalist có ĐÚNG 50 option; G27-129 (Gấp) đứng trước G27-000.
#                        Bản cũ a57f112: > 130 option → SAI là đúng.
#   STEP L2 [PO-DL-NHE]  gõ "G27-1" → 30 option, đều chứa "G27-1"; gõ "G27-" → 100 option (130 khớp, cắt 100).
# Chạy trên harness VỪA khởi động: python3 drive_g27.py <port> <nhãn>
import sys, asyncio, json, urllib.request
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:600]) if (not cond and info not in (None, "")) else ""))
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}

async def opts(pg, inp):
    dl = await inp.get_attribute("list")
    return await pg.eval_on_selector_all(f"datalist[id='{dl}'] option", "els => els.map(e => e.value)")

async def main():
    st = post("/harness/g27-seed")
    ok(st.get("ok") and st.get("n") == 130, "Seed G27 (130 PO)", st)
    async with async_playwright() as p:
        br = await p.chromium.launch(); ctx = await br.new_context(); pg = await ctx.new_page()
        await pg.set_viewport_size({"width": 1900, "height": 1200})
        await pg.goto(BASE + "/login"); await pg.fill("#username", "admin"); await pg.fill("#password", "admin123")
        await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
        await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(3000)
        if await pg.locator("button:has-text('Ca 1')").count(): await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)

        print("STEP L1 PO-DL-NHE — ô trống: 50 PO ưu tiên đầu")
        inp = pg.locator("input[placeholder='Gõ PO...']").first
        ok(await inp.count() == 1, "L1 có ô Số PO ở dòng con đầu")
        o = await opts(pg, inp)
        ok(len(o) == 50, "L1 datalist ĐÚNG 50 option khi ô trống", len(o))
        ok("G27-129" in o and ("G27-000" not in o or o.index("G27-129") < o.index("G27-000")), "L1 G27-129 (Gấp) đứng trước G27-000", o[:8])

        print("STEP L2 PO-DL-NHE — gõ chữ: tìm toàn bộ, tối đa 100")
        await inp.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('input', {bubbles: true})); }", "G27-1"); await pg.wait_for_timeout(1500)
        o = await opts(pg, inp)
        ok(len(o) == 30 and all("G27-1" in x for x in o), "L2 gõ 'G27-1' → 30 option đều khớp", (len(o), o[:5]))
        await inp.evaluate("(e, v) => { e.value = v; e.dispatchEvent(new Event('input', {bubbles: true})); }", "G27-"); await pg.wait_for_timeout(1500)
        o = await opts(pg, inp)
        ok(len(o) == 100 and all(x.startswith("G27-") for x in o), "L2 gõ 'G27-' → 100 option (130 khớp, cắt 100)", len(o))
        await br.close()
    print("KẾT QUẢ: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
    sys.exit(0 if fails == 0 else 1)

asyncio.run(main())
