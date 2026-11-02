# [WTS-NC-WIDTH 02/11/2026] Trang WTS thật có Bootstrap 5.3.3: mọi ô nhập dòng đầu hiện ĐỦ chữ — NC (cả nhãn "<NC> — chưa làm: 0"), Máy,
#   Số PO 10 số, Part No, OK / NG 4 số, Bắt đầu / Kết thúc "08:00" — nhóm GC + HTSP + KCS. Harness không có menu trái như app thật:
#   khung 1087 ≈ app thật màn 1366 (đo 02/11: bảng 1071px). Màn hẹp được phép CUỘN NGANG (không cắt chữ); từ 1600 trở lên không được tràn.
# Bootstrap lấy từ CDN trong app → harness không có mạng: tải 1 lần bằng npm rồi trỏ NCW_BS (mặc định /tmp/bs):
#   mkdir -p /tmp/bs && cd /tmp/bs && npm pack bootstrap@5.3.3 bootstrap-icons@1.11.3 && for f in *.tgz; do tar xzf $f; mv package ${f%.tgz}; done
#   + ô Số PO vẫn đủ PO 10 số, ô OK / NG đủ 3 số (nới ô NC không được làm ô PO co quá).
# Chạy: python3 drive_ncw.py <port> <old|new>
import sys, os, asyncio
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]; BASE = f"http://127.0.0.1:{PORT}"; BS = os.environ.get("NCW_BS", "/tmp/bs")
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:300]) if info != "" else ""))
async def cdn(ctx):
    async def h(route):
        u = route.request.url
        f = BS + ("/bootstrap-icons-1.11.3/" + u.split("bootstrap-icons@1.11.3/")[1] if "bootstrap-icons@" in u else "/bootstrap-5.3.3/" + u.split("bootstrap@5.3.3/")[1])
        await (route.fulfill(path=f) if os.path.exists(f) else route.abort())
    await ctx.route("https://cdn.jsdelivr.net/npm/**", h)
# đo: chữ cần = bề rộng chữ (canvas, đúng font ô) + padding trái phải + 2px viền
JS = """(gc) => {
 const t=[...document.querySelectorAll('table')].find(x=>x.querySelector("input[placeholder='Gõ PO...']"));
 const r=[...t.querySelectorAll('tr')].find(x=>x.querySelector("input[placeholder='Gõ PO...']"));
 const c=document.createElement('canvas').getContext('2d');
 const need=(el,txt)=>{const cs=getComputedStyle(el); c.font=cs.fontSize+' '+cs.fontFamily; return Math.ceil(c.measureText(txt).width+parseFloat(cs.paddingLeft)+parseFloat(cs.paddingRight)+2);};
 const o={tran:t.parentElement.scrollWidth-t.parentElement.clientWidth};
 const po=r.querySelector("input[placeholder='Gõ PO...']"); o.po=[po.offsetWidth,need(po,'0003628899')];
 const nb=[...r.querySelectorAll("input[type=number]")]; o.ok=[nb[0].offsetWidth,need(nb[0],'1500')]; o.ng=[nb[1].offsetWidth,need(nb[1],'1500')];
 const pt=[...r.querySelectorAll('input[readonly]')][0]; o.part=pt?[pt.offsetWidth,need(pt,'3B-PT1268610')]:null;
 const tm=[...r.querySelectorAll("input[placeholder='HH:mm']")]; o.gio=tm.map(e=>[e.offsetWidth,need(e,'08:00')]);
 if(gc){ const s=r.querySelectorAll('select'); const nc=s[0].value;
   o.nc=[s[0].offsetWidth,need(s[0],nc+' — chưa làm: 0'),nc]; o.may=[s[1].offsetWidth,need(s[1],s[1].value),s[1].value]; }
 return o; }"""
async def main():
    if not os.path.exists(BS + "/bootstrap-5.3.3/dist/css/bootstrap.min.css"):
        print("THIẾU Bootstrap ở " + BS + " — xem đầu file"); sys.exit(2)
    async with async_playwright() as p:
        b = await p.chromium.launch()
        for user, pw, gc in (("admin", "admin123", True), ("htsp01", "123456", False), ("kcs01", "123456", False)):
            for W in (1087, 1280, 1366, 1600, 1920):
                ctx = await b.new_context(viewport={"width": W, "height": 900}); await cdn(ctx); pg = await ctx.new_page()
                await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
                await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
                await pg.goto(BASE + "/hieu-suat-cong-nhan"); await pg.wait_for_timeout(2500)
                await pg.click("button:has-text('Ca 1')"); await pg.wait_for_timeout(800)
                if await pg.locator("input[placeholder='Gõ PO...']").count() == 0:
                    await pg.click("button:has-text('Thêm nhóm')"); await pg.wait_for_timeout(500)
                inp = pg.locator("input[placeholder='Gõ PO...']").first; await inp.fill("PO1"); await inp.press("Tab"); await pg.wait_for_timeout(900)
                if gc:
                    row = pg.locator("tr", has=pg.locator("input[placeholder='Gõ PO...']")).first
                    await row.locator("select").nth(0).select_option("NC1"); await pg.wait_for_timeout(500)
                    await row.locator("select").nth(1).select_option("M1"); await pg.wait_for_timeout(500)
                o = await pg.evaluate(JS, gc)
                if W >= 1600: ok(o["tran"] <= 0, f"N1 {user} {W}px: bảng nhập không tràn ngang", o["tran"])
                else: print(f"  (đo)  {user} {W}px: bảng cuộn ngang {max(o['tran'],0)}px")
                ok(all(w >= n for w, n in o["gio"]), f"N2 {user} {W}px: ô Bắt đầu / Kết thúc hiện đủ '08:00'", o["gio"])
                ok(o["po"][0] >= o["po"][1], f"N5 {user} {W}px: ô Số PO hiện đủ PO 10 số '0003628899'", o["po"])
                ok(o["ok"][0] >= o["ok"][1] and o["ng"][0] >= o["ng"][1], f"N6 {user} {W}px: ô OK / NG hiện đủ 4 số '1500'", [o["ok"], o["ng"]])
                if o["part"]: ok(o["part"][0] >= o["part"][1], f"N7 {user} {W}px: ô Part No hiện đủ '3B-PT1268610'", o["part"])
                if gc:
                    ok(o["nc"][0] >= o["nc"][1], f"N3 {W}px: ô NC đủ chỗ cho '{o['nc'][2]} — chưa làm: 0'", o["nc"])
                    ok(o["may"][0] >= o["may"][1], f"N4 {W}px: ô Máy hiện đủ '{o['may'][2]}'", o["may"])
                    if W == 1366: await pg.locator("table", has=pg.locator("input[placeholder='Gõ PO...']")).first.screenshot(path=f"ncw_{OUT}_{W}.png")
                await ctx.close()
        await b.close()
    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"{fails} SAI"))
asyncio.run(main())
