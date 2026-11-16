# [GÓI 23 16/11/2026] drive_g23.py — bấm trang thật cho các sửa Gói 23 (quét Fable 15/11 nhóm A1–A8 / B1–B4). Nguồn: CLAUDE quét Fable; HÙNG 16/11 05:15 OK Gói 23.
#   P1 [KH-PERM-SVC] POST /harness/g23-create-kh?user=g23xem (KhPlanService.CreateAsync qua DI thật, user chỉ Xem Giao nhận) → ok=false, lời NoEdit "Kế hoạch";
#      user g23hsm (Xem Kế hoạch, không Sửa) cũng bị từ chối; admin → ok=true. Bản cũ: g23xem tạo được → SAI là đúng.
#   H1 [PERM-HASH] g23gc đăng nhập, mở /handover (Sửa Giao nhận GC) → ok; POST /harness/g23-perm?group=G23GC&edit=0 (giả Admin rút ô Sửa) →
#      tải lại /handover → bị đưa về /login (phiên cũ hết hiệu lực); đăng nhập lại → vào được, không còn nút ghi. Bản cũ: vẫn ở /handover → SAI là đúng.
#   H2 [PERM-HASH] admin đăng nhập; đổi json ma trận nhóm ADMIN (g23-perm?group=ADMIN) → tải lại /kh-plan vẫn ở trang (nhóm ADMIN không so hash).
#   V1 [VIEW-PERM] g23xem gõ URL /kh-plan/{id}/detail/{d} (không Xem Kế hoạch) → "Truy cập bị từ chối", KHÔNG có bảng số liệu / nút Sửa phiếu. Bản cũ: hiện số liệu → SAI là đúng.
#   V2 [VIEW-PERM] g23xem gõ /part-master/history → "Truy cập bị từ chối"; admin → có bảng.
#   V3 [VIEW-PERM][HSM-QUYEN] g23xem gõ /hieu-suat-may → "Truy cập bị từ chối" + menu không có "Hiệu suất máy"; bản cũ: trang mở, menu có → SAI là đúng.
#   M1 [HSM-QUYEN] g23hsm (có ô Xem HIEU_SUAT_MAY) → /hieu-suat-may mở được (có bộ lọc ngày / nút Xem), menu có "Hiệu suất máy"; admin cũng vậy.
#   L1 [LEN-CHECK] maxlength ở ô nhập: /kh-plan/create PO (50), /customers ô Mã KH (20) sau bấm Thêm, /thiet-bi Số máy (50) sau bấm Thêm. Bản cũ: không có → SAI là đúng.
#   E1 [UI-SAFE][BLZ-ERROR-UI] HTML /login có id="blazor-error-ui" (nút Tải lại) — bản cũ không có → SAI là đúng.
#   E2 [BLZ-ERROR-UI][UI-SAFE] giả lỗi đọc DB (POST /harness/g23-fail-read?n=1: câu đọc đầu trong KhPlanService ném) khi admin mở /kh-plan → trang hiện khung lỗi Việt (_pageError), không chết, #blazor-error-ui vẫn ẩn.
# Chạy trên harness VỪA khởi động: python3 drive_g23.py <port> <nhãn>
import sys, os, asyncio, json, urllib.request, urllib.error
from playwright.async_api import async_playwright
PORT = sys.argv[1]; OUT = sys.argv[2]
BASE = f"http://127.0.0.1:{PORT}"
fails = 0
def ok(cond, name, info=""):
    global fails
    if not cond: fails += 1
    print(("  ĐẠT  " if cond else "  SAI  ") + name + (("\n         → " + str(info)[:900]) if (not cond and info not in (None, "")) else ""))
def get(path): return json.loads(urllib.request.urlopen(BASE + path).read().decode())
def post(path):
    try: return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + path, data=b"", method="POST")).read().decode())
    except urllib.error.HTTPError as e: return {"ok": False, "error": f"HTTP {e.code}"}
def status(path):
    try:
        r = urllib.request.urlopen(BASE + path); return r.status, r.read()
    except urllib.error.HTTPError as e: return e.code, b""

BS = os.environ.get("KHO_BS", "/home/claude/bs_shared")
async def cdn(ctx):
    async def h(route):
        u = route.request.url
        f = BS + ("/bootstrap-icons-1.11.3/" + u.split("bootstrap-icons@1.11.3/")[1] if "bootstrap-icons@" in u else "/bootstrap-5.3.3/" + u.split("bootstrap@5.3.3/")[1])
        await (route.fulfill(path=f) if os.path.exists(f) else route.abort())
    await ctx.route("https://cdn.jsdelivr.net/npm/**", h)

async def login(ctx, user, pw, goto, wait=3500):
    pg = await ctx.new_page(); await pg.set_viewport_size({"width": 1900, "height": 1200})
    await pg.goto(BASE + "/login"); await pg.fill("#username", user); await pg.fill("#password", pw)
    await pg.click("button[type=submit]"); await pg.wait_for_load_state("networkidle")
    await pg.goto(BASE + goto); await pg.wait_for_timeout(wait)
    return pg

async def text(pg): return await pg.locator("body").inner_text()

async def main():
    seed = post("/harness/g23-seed")
    ok(seed.get("ok"), "Seed G23 (3 nhóm / 3 user)", seed)
    dump = get("/harness/dump")
    det = sorted(dump["khdet"], key=lambda d: d["khPlanDetailId"])[0] if dump.get("khdet") else None
    ok(det is not None, "Dump có dòng PO (khdet)", list(dump.keys())[:12])
    kid, did = (det["khPlanId"], det["khPlanDetailId"]) if det else (1, 1)

    print("[P1] KH-PERM-SVC — KhPlanService.CreateAsync qua DI thật")
    r1 = post("/harness/g23-create-kh?user=g23xem")
    ok(not r1["ok"] and "Kế hoạch" in r1["error"] and "quyền" in r1["error"], "P1 g23xem (chỉ Xem Giao nhận) tạo KH: từ chối, lời NoEdit Kế hoạch", r1)
    r2 = post("/harness/g23-create-kh?user=g23hsm")
    ok(not r2["ok"] and "Kế hoạch" in r2["error"], "P1 g23hsm (Xem Kế hoạch, không Sửa) tạo KH: từ chối", r2)
    r3 = post("/harness/g23-create-kh?user=admin")
    ok(r3["ok"] and r3["id"] > 0, "P1 admin tạo KH: được (hồi quy)", r3)

    st, html = status("/login"); h = html.decode("utf-8", "ignore")
    print("[E1] BLZ-ERROR-UI")
    ok('id="blazor-error-ui"' in h and "Tải lại" in h, "E1 HTML có khối #blazor-error-ui + nút Tải lại", h[h.find("blazor-error-ui") - 100: h.find("blazor-error-ui") + 300] if "blazor-error-ui" in h else "không có")

    async with async_playwright() as p:
        br = await p.chromium.launch()

        print("[H1] PERM-HASH — Admin rút ô Sửa của nhóm → phiên cũ hết hiệu lực")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "g23gc", "123456", "/handover")
        ok("/login" not in pg.url, "H1 g23gc đăng nhập, mở /handover được", pg.url)
        pr = post("/harness/g23-perm?group=G23GC&edit=0")
        ok(pr.get("changed"), "H1 đổi ma trận nhóm G23GC (rút Sửa) ngoài trang", pr)
        await pg.goto(BASE + "/handover"); await pg.wait_for_timeout(3000)
        ok("/login" in pg.url, "H1 tải lại /handover → về /login (cookie cũ hết hiệu lực vì PermHash khác)", pg.url)
        pg2 = await login(ctx, "g23gc", "123456", "/handover")
        ok("/login" not in pg2.url, "H1 đăng nhập lại → vào /handover (quyền mới: chỉ Xem)", pg2.url)
        post("/harness/g23-perm?group=G23GC&edit=1")
        await ctx.close()

        print("[H2] PERM-HASH — nhóm ADMIN không so hash")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kh-plan")
        ok("/login" not in pg.url, "H2 admin mở /kh-plan", pg.url)
        pr = post("/harness/g23-perm?group=ADMIN&edit=1")
        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(2500)
        ok("/login" not in pg.url, "H2 đổi json ma trận ADMIN → admin vẫn ở trang (không bị đá)", f"{pg.url} · {pr}")
        await ctx.close()

        print("[V] VIEW-PERM — g23xem gõ URL các trang chỉ xem")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "g23xem", "123456", f"/kh-plan/{kid}/detail/{did}")
        t = await text(pg)
        ok("Truy cập bị từ chối" in t and "Xem Kế hoạch" in t and "Sửa phiếu" not in t and "Vật liệu" not in t,
           "V1 /kh-plan/{id}/detail/{d}: từ chối, không bảng số liệu, không nút Sửa phiếu", t[:500])
        await pg.goto(BASE + "/part-master/history"); await pg.wait_for_timeout(3000); t = await text(pg)
        ok("Truy cập bị từ chối" in t and "Part Master" in t and await pg.locator("table tbody tr").count() == 0, "V2 /part-master/history: từ chối, không bảng", t[:400])
        await pg.goto(BASE + "/hieu-suat-may"); await pg.wait_for_timeout(3000); t = await text(pg)
        ok("Truy cập bị từ chối" in t and "Hiệu suất máy" in t, "V3 /hieu-suat-may: từ chối (không có ô Xem HIEU_SUAT_MAY)", t[:400])
        menu = await pg.locator("a.nav-link[href='/hieu-suat-may']").count()
        ok(menu == 0, "V3 menu trái không có 'Hiệu suất máy'", menu)
        await ctx.close()

        print("[M1] HSM-QUYEN — có ô Xem → trang + menu")
        for user, pw in (("g23hsm", "123456"), ("admin", "admin123")):
            ctx = await br.new_context(); await cdn(ctx)
            pg = await login(ctx, user, pw, "/hieu-suat-may", 4500)
            t = await text(pg)
            ok("Truy cập bị từ chối" not in t and "Hiệu suất máy" in t and await pg.locator("input[type=date]").count() >= 1,
               f"M1 {user}: /hieu-suat-may mở được (có ô ngày), không bị từ chối", t[:400])
            ok(await pg.locator("a.nav-link[href='/hieu-suat-may']").count() == 1, f"M1 {user}: menu có 'Hiệu suất máy'")
            if user == "admin":
                # V2 hồi quy: admin xem lịch sử Part Master có bảng (hoặc chữ "Chưa có")
                await pg.goto(BASE + "/part-master/history?partNo=PART-E2"); await pg.wait_for_timeout(3000); t = await text(pg)
                ok("Truy cập bị từ chối" not in t and "Lịch sử thay đổi" in t, "V2 admin /part-master/history?partNo=PART-E2: mở được", t[:300])
            await ctx.close()

        print("[L1] LEN-CHECK — maxlength ở ô nhập")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kh-plan/create")
        n50 = await pg.locator("input[maxlength='50']").count()
        ok(n50 >= 1, "L1 /kh-plan/create: có ô maxlength=50 (PO / Order VL / Vị trí)", n50)
        await pg.goto(BASE + "/customers"); await pg.wait_for_timeout(2500)
        await pg.click("button:has-text('Thêm khách hàng mới')"); await pg.wait_for_timeout(1000)
        n20 = await pg.locator("input[maxlength='20']").count(); n200 = await pg.locator("input[maxlength='200']").count()
        ok(n20 >= 1 and n200 >= 1, "L1 /customers hộp Thêm: Mã KH maxlength=20, Tên maxlength=200", f"20:{n20} 200:{n200}")
        await pg.goto(BASE + "/thiet-bi"); await pg.wait_for_timeout(2500)
        await pg.click("button:has-text('Thêm thiết bị mới')"); await pg.wait_for_timeout(1000)
        ok(await pg.locator("input[maxlength='50']").count() >= 1, "L1 /thiet-bi hộp Thêm: Số máy maxlength=50", await pg.locator("input[maxlength='50']").count())
        await ctx.close()

        print("[E2] UI-SAFE — lỗi đọc DB khi mở trang KH → khung lỗi Việt, trang không chết")
        ctx = await br.new_context(); await cdn(ctx)
        pg = await login(ctx, "admin", "admin123", "/kh-plan", 2500)
        # rà độc lập 16/11 T7: trang prerender + tương tác = OnInitialized chạy 2 lần → n=2 để cả lượt tương tác cũng gặp lỗi; so đúng câu UiSafe
        st = post("/harness/g23-fail-read?n=2")   # câu đọc đầu tiên trong KhPlanService ném lỗi (không đụng đọc quyền / AuthGuard)
        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(3500)
        t = await text(pg); st2 = post("/harness/g23-fail-read?n=0")
        thrown = st2["readThrown"] - st["readThrown"]
        ok(thrown == 2, "E2 giả lỗi đọc đã ném ở CẢ 2 lượt (prerender + tương tác) khi tải /kh-plan", f"{st} → {st2}")
        ok(thrown == 2 and "máy chủ đang bận hoặc lỗi kết nối dữ liệu" in t and "tải danh sách Kế hoạch" in t and "GIA-LOI" not in t,
           "E2 trang KH hiện đúng câu UiSafe (không lộ chữ lỗi gốc)", t[:500])
        ok(await pg.locator("#blazor-error-ui").count() == 1 and await pg.locator("#blazor-error-ui").is_hidden(),
           "E2 mạch còn sống: #blazor-error-ui có trong DOM và đang ẩn")
        await pg.goto(BASE + "/kh-plan"); await pg.wait_for_timeout(3000); t = await text(pg)
        ok("/login" not in pg.url and "GIA-LOI" not in t and await pg.locator("table").count() >= 1, "E2 tải lại sau khi tắt lỗi → bảng KH hiện bình thường", t[:300])
        await ctx.close()
        await br.close()

    print(f"\nKẾT QUẢ {OUT}: " + ("ĐẠT hết" if fails == 0 else f"SAI {fails}"))

asyncio.run(main())
