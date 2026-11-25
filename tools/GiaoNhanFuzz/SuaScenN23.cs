// [N23-SCEN 2026-11-25] Kịch bản cố định "n23": luật N23 — form "Giao / xử lý hàng" 6 nút (khách chốt mockup giao nhận 23/11: Form chung, TH3, TH4, TH10;
//   Hùng 23/11 16:24 OK ghi sổ). Bằng hàm thật HandoverService.SaveFormAsync + sổ chung. Đáp án độc lập = số trên bảng giao nhận của mockup
//   (cột "Mockup" / "Số") + luật N23: nút xám đúng lý do, ô bộ phận nhận mặc định, "Sửa đạt → giao đến" (1 lần lưu), "Sửa chưa đạt" ở ô Hàng sửa tự trả nơi làm lỗi.
//   Sau MỖI bước ghi: Check() chung của kịch bản sua (cân PO, không ô âm, vụ cân, khung lỗi, dòng B cuối = sổ, Đang ở đâu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    static Task<(bool Ok, string Error)> Form(HandoverService svc, Action<FormGiaoRequest> set)
    {
        var r = new FormGiaoRequest { KhPlanDetailId = 1, UserId = 1 };
        set(r);
        return svc.SaveFormAsync(r);
    }

    static HandoverTransaction Tx(AppDbContext db) => db.HandoverTransactions.List.OrderBy(t => t.HandoverTxId).Last();
    static string Btn(List<HandoverForm.FormBtn> bs) => string.Join(" · ", bs.Select(b => (b.On ? "✓" : "✗") + b.Code + (b.On ? "" : "(" + b.Why + ")")));
    static bool On(List<HandoverForm.FormBtn> bs, params string[] on) => bs.Count == 6 && bs.All(b => b.On == on.Contains(b.Code)) && bs.Where(b => !b.On).All(b => b.Why.Length > 3);

    /// <summary>TH4 dựng: Kho → GC 10 (nhận đủ, NC1) → GC phát hiện 2 lỗi "Vừa nhận, chưa làm NC 1", tích Lỗi công đoạn trước (Kho), Trả nơi làm lỗi → Kho nhận ô Hàng lỗi.</summary>
    static async Task<long> PreTh4(AppDbContext db, HandoverService svc)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
        var t = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC1"; r.Prev = true; r.Cause = "KHO";
                                       r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PTra; r.ToGroup = "KHO"; r.Qty = 2; r.CatId = 1; });
        Ok(t.Ok, "TH4 bước 1: Gia công Trả nơi làm lỗi (Kho) 2 chiếc — 1 phiếu Hàng lỗi công đoạn trước", t.Error);
        var tx = Tx(db);
        Ok(tx.Purpose == HandoverPurpose.NgReturn && tx.ToCell == HandoverCell.Loi && tx.QtyIssued == 2 && tx.NgCaseId != null, "phiếu Gia công → Kho, ô Hàng lỗi 2, mã vụ mới",
           $"{tx.Purpose} {tx.ToCell} {tx.QtyIssued} vụ {tx.NgCaseId}");
        var k = await svc.ReceiveAsync(tx.HandoverTxId, 0, 2, null, 1);
        Ok(k.Ok, "TH4 bước 2: Kho nhận 2 vào ô Hàng lỗi", k.Error);
        return tx.NgCaseId!.Value;
    }

    /// <summary>TH10 dựng: Kho → GC 10 · GC NC1–NC3 OK 10 · GC → KCS 10 · KCS phát hiện 5 lỗi (chưa kiểm xong, lỗi công đoạn trước — Gia công) → Trả nơi làm lỗi
    ///   → Gia công nhận ô Hàng lỗi.</summary>
    static async Task<long> PreTh10(AppDbContext db, HandoverService svc)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
        foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(db, "GC", nc, null, 10, 0, Gc, 0);
        await svc.IssueByCellAsync(1, "GC", "KCS", 10, Gc, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 10, 0, null, Kcs);
        var t = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "CHUA"; r.Prev = true; r.Cause = "GC";
                                       r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PTra; r.ToGroup = "GC"; r.Qty = 5; r.CatId = 1; });
        Ok(t.Ok, "TH10 bước 2: KCS Trả nơi làm lỗi (Gia công) 5 — ô Hàng lỗi Gia công", t.Error);
        var tx = Tx(db);
        var k = await svc.ReceiveAsync(tx.HandoverTxId, 0, 5, null, Gc);
        Ok(k.Ok && tx.NgCaseId != null, "Gia công nhận 5 vào ô Hàng lỗi", k.Error);
        return tx.NgCaseId!.Value;
    }

    public static async Task<int> RunN23()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[X1] TH4: Gia công trả Kho 2 lỗi → Kho \"Sửa tại bộ phận mình / XXN KH\" đạt 2, giao đến Gia công (1 lần lưu) → Gia công nhận ô Hàng lỗi đã xử lý, làm tiếp NC 1");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh4(db, svc);
            var b = await Check(db, "X1 Kho đang giữ 2 lỗi");
            var cs = b.Cases[vu];
            var posK = HandoverForm.FormPositions(b, "KHO", false).Single(p => p.Id == "TONLOI");
            var bsK = HandoverForm.FormButtons(b, "KHO", posK, cs, null, false);
            Ok(On(bsK, HandoverForm.ASua, HandoverForm.BNho, HandoverForm.AGiu, HandoverForm.AHuy, HandoverForm.AKhach) && bsK.Single(x => x.Code == HandoverForm.BTra).Why.Contains("chính Kho")
               && HandoverForm.GiuNoop(posK),
               "6 nút Kho (ô Hàng lỗi) như mockup TH4 bước 3: chỉ Trả nơi làm lỗi xám \"nơi làm lỗi là chính Kho\" (Giữ lại bấm được — không cần ghi gì)", Btn(bsK));
            Ok(HandoverForm.SuaGive(b, "KHO", cs) == ("GC", HandoverCell.Xl), "Sửa đạt → giao đến: mặc định Gia công (nơi báo lỗi), vào ô Hàng lỗi đã xử lý",
               HandoverForm.SuaGive(b, "KHO", cs)?.ToString());
            var f = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; r.SuaTo = "GC"; });
            var a = await Check(db, "X1 Kho sửa đạt + giao");
            var px = Tx(db);
            Ok(f.Ok && px.FromGroupCode == "KHO" && px.ToGroupCode == "GC" && px.FromCell == HandoverCell.Xl && px.ToCell == HandoverCell.Xl && px.QtyIssued == 2 && px.NgCaseId == vu && px.Status == "PENDING",
               "Sẽ ghi: Kết quả sửa (tự xác nhận) + phiếu Kho → Gia công, ô Hàng lỗi đã xử lý 2, cùng vụ — 1 lần lưu", f.Error + $" | {px.FromCell}→{px.ToCell} {px.QtyIssued} vụ {px.NgCaseId} {px.Status}");
            var kb = b.G("KHO"); var ka = a.G("KHO");
            Ok(ka.ConGiao == kb.ConGiao && ka.LoiGiu == kb.LoiGiu - 2 && ka.XlGiu == 0 && ka.GiaoLoi == kb.GiaoLoi + 2 && ka.GiaoMoi == kb.GiaoMoi && ka.ChoKcs == 0,
               "Kho: Giao · Hàng lỗi +2 (mockup 2), Giao · Hàng mới không đổi (trước: +2 — sai), phôi còn không đổi, Tồn hàng lỗi −2",
               $"còn {kb.ConGiao}→{ka.ConGiao} lỗi {kb.LoiGiu}→{ka.LoiGiu} xl {ka.XlGiu} giaoLoi {kb.GiaoLoi}→{ka.GiaoLoi} giaoMoi {kb.GiaoMoi}→{ka.GiaoMoi}");
            var r1 = await svc.ReceiveAsync(px.HandoverTxId, 2, 0, null, Gc);
            var c = await Check(db, "X1 Gia công nhận");
            var gb = b.G("GC"); var gc = c.G("GC");
            var nc1b = gb.Steps.First(s => s.Code == "NC1").Waiting ?? 0; var nc1c = gc.Steps.First(s => s.Code == "NC1").Waiting ?? 0;
            Ok(r1.Ok && gc.NhanLoiXl == gb.NhanLoiXl + 2 && gc.NhanMoi == gb.NhanMoi && nc1c == nc1b + 2 && db.HandoverTransactions.List.Single(t => t.HandoverTxId == px.HandoverTxId).ToNC == "NC1",
               "Gia công: Nhận · Hàng lỗi đã xử lý 2 (mockup 2), Nhận · Hàng mới không đổi, 2 chiếc về chờ NC 1 (NC của vụ, máy chủ tự điền)",
               $"{r1.Error} nx {gb.NhanLoiXl}→{gc.NhanLoiXl} nm {gb.NhanMoi}→{gc.NhanMoi} NC1 {nc1b}→{nc1c}");
        }

        Console.WriteLine("[X1b] TH4 chọn \"Chưa giao\": hàng đạt giữ ở Tồn hàng lỗi đã xử lý của Kho, giao sau ở dòng \"Đang giữ ở ô Hàng lỗi đã xử lý\" (chỉ xuôi về nơi báo lỗi)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh4(db, svc);
            var b = await Check(db, "X1b trước");
            var f = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; });
            var a = await Check(db, "X1b Kho sửa đạt, chưa giao");
            Ok(f.Ok && a.G("KHO").XlGiu == 2 && a.G("KHO").ConGiao == b.G("KHO").ConGiao && HandoverLedgerService.WhereIs(a).Items.Any(i => i.Place == "KHO" && i.Kind == "XL" && i.Qty == 2),
               "Kho: Tồn hàng lỗi đã xử lý 2 (không thành phôi còn), Đang ở đâu có dòng Kho · hàng đã sửa chờ trả 2", f.Error + $" xl {a.G("KHO").XlGiu} còn {a.G("KHO").ConGiao}");
            Ok(HandoverForm.FormPositions(a, "KHO", true).Any(p => p.Id == "TONXL" && p.Have == 2), "Dòng hàng đã sửa chờ trả của Kho có 2");
            var sig0 = await FullSig(db);
            var x = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Tot; r.Pos = "TONXL"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.ToGroup = "HTSP"; r.Qty = 2; });
            Ok(!x.Ok && await FullSig(db) == sig0, "Workflow ON: Kho giao hàng đã sửa vượt nơi báo lỗi (HTSP) bị chặn, không ghi gì", x.Error);
            var y = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Tot; r.Pos = "TONXL"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.ToGroup = "GC"; r.Qty = 2; });
            await Check(db, "X1b giao sau");
            var r1 = await svc.ReceiveAsync(Tx(db).HandoverTxId, 2, 0, null, Gc);
            var c = await Check(db, "X1b Gia công nhận");
            Ok(y.Ok && r1.Ok && c.G("KHO").XlGiu == 0 && c.G("GC").NhanLoiXl == 2, "Giao sau về Gia công (ô Hàng lỗi đã xử lý) được", y.Error + " | " + r1.Error);
        }

        Console.WriteLine("[X1c] TH3 bước 3: chiếc Gia công KHÔNG nhận lúc nhận (NG lúc nhận) → Kho sửa đạt 2, giao đến Gia công vào ô Hàng mới (như cũ: phôi còn → Hàng mới)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var b = await Check(db, "X1c trước");
            Ok(HandoverForm.SuaGive(b, "KHO", b.Cases[vu]) == ("GC", HandoverCell.Moi), "Sửa đạt → giao đến Gia công, vào ô Hàng mới (Gia công chưa từng nhận)", HandoverForm.SuaGive(b, "KHO", b.Cases[vu])?.ToString());
            var f = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; r.SuaTo = "GC"; });
            var a = await Check(db, "X1c Kho sửa đạt + giao Hàng mới");
            var px = Tx(db);
            Ok(f.Ok && px.FromCell == HandoverCell.Moi && px.ToCell == HandoverCell.Moi && px.QtyIssued == 2 && a.G("KHO").ConGiao == b.G("KHO").ConGiao && a.G("KHO").XlGiu == 0
               && a.G("KHO").GiaoMoi == b.G("KHO").GiaoMoi + 2, "Phiếu Kho → Gia công Hàng mới 2 (mockup TH3 \"Sẽ ghi: phiếu Kho → Gia công Hàng mới 2\")",
               f.Error + $" {px.FromCell}→{px.ToCell} {px.QtyIssued} còn {b.G("KHO").ConGiao}→{a.G("KHO").ConGiao}");
            await svc.ReceiveAsync(px.HandoverTxId, 2, 0, null, Gc);
            await Check(db, "X1c Gia công nhận");
        }

        Console.WriteLine("[X2] TH10: KCS trả Gia công 5 → Gia công tự sửa đạt 5, giao HTSP làm lại Bavia → HTSP giao KCS ô Hàng lỗi đã xử lý → KCS kiểm lại");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh10(db, svc);
            var b = await Check(db, "X2 Gia công đang giữ 5 lỗi");
            var cs = b.Cases[vu];
            var pos = HandoverForm.FormPositions(b, "GC", false).Single(p => p.Id == "TONLOI");
            var bs = HandoverForm.FormButtons(b, "GC", pos, cs, null, false);
            Ok(!bs.Single(x => x.Code == HandoverForm.BTra).On && bs.Single(x => x.Code == HandoverForm.BTra).Why.Contains("chính Gia công") && bs.Single(x => x.Code == HandoverForm.ASua).On,
               "Trả nơi làm lỗi xám: nơi làm lỗi là chính Gia công (mockup TH10 bước 3)", Btn(bs));
            Ok(HandoverForm.SuaGive(b, "GC", cs) == ("KCS", HandoverCell.Xl), "Sửa đạt → giao đến: mặc định KCS (nơi báo lỗi)");
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 5; r.SuaTo = "HTSP"; });
            var a = await Check(db, "X2 Gia công sửa đạt + giao HTSP");
            var px = Tx(db);
            Ok(f.Ok && px.FromGroupCode == "GC" && px.ToGroupCode == "HTSP" && px.FromCell == HandoverCell.Xl && px.ToCell == HandoverCell.Xl && px.QtyIssued == 5,
               "Sẽ ghi: phiếu Gia công → HTSP ô Hàng lỗi đã xử lý 5", f.Error);
            Ok(a.G("GC").GiaoLoi == b.G("GC").GiaoLoi + 5 && a.G("GC").XlGiu == 0 && a.G("GC").LoiGiu == 0, "Gia công · Giao · Hàng lỗi 5 (mockup)",
               $"{b.G("GC").GiaoLoi}→{a.G("GC").GiaoLoi}");
            var r1 = await svc.ReceiveAsync(px.HandoverTxId, 5, 0, null, 1);
            var c = await Check(db, "X2 HTSP nhận");
            var posH = HandoverForm.FormPositions(c, "HTSP", true).SingleOrDefault(p => p.Id == "TONXL");
            Ok(r1.Ok && c.G("HTSP").NhanLoiXl == 5 && c.G("HTSP").XlGiu == 5 && posH?.Have == 5, "HTSP · Nhận · Hàng lỗi đã xử lý 5, giữ ô (đang giữ ở ô Hàng lỗi đã xử lý — về KCS)", r1.Error);
            var g2 = await Form(svc, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Tot; r.Pos = "TONXL"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.ToGroup = "KCS"; r.Qty = 5; });
            await Check(db, "X2 HTSP giao KCS");
            var r2 = await svc.ReceiveAsync(Tx(db).HandoverTxId, 5, 0, null, Kcs);
            var d = await Check(db, "X2 KCS nhận");
            Ok(g2.Ok && r2.Ok && d.G("HTSP").GiaoLoiXl == 5 && d.G("KCS").NhanLoiXl == 5 && d.G("KCS").GiaoLoi == 5 && d.G("KCS").ConGiao == 5 && d.G("KCS").ChuaLam == 5,
               "HTSP · Giao · Hàng lỗi đã xử lý 5 · KCS · Nhận · Hàng lỗi đã xử lý 5 · KCS · Giao · Hàng lỗi 5 (mockup) · 5 chiếc sửa về tính là đã kiểm xong (kiểm lại ghi Việc không làm ra SP), 5 chiếc còn lại vẫn chưa kiểm",
               $"{g2.Error} {r2.Error} htspGx {d.G("HTSP").GiaoLoiXl} kcsNx {d.G("KCS").NhanLoiXl} kcsGl {d.G("KCS").GiaoLoi} kcsDa {d.G("KCS").ConGiao} kcsChua {d.G("KCS").ChuaLam}");
        }

        Console.WriteLine("[X3] Ô Hàng sửa: Kho gửi HTSP sửa 2 → HTSP sửa đạt 1, chưa đạt 1 → chiếc chưa đạt tự trả Kho (nơi làm lỗi) ô Hàng lỗi, chiếc đạt giao về Gia công (nơi báo lỗi)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var b0 = await Check(db, "X3 trước");
            var posK = HandoverForm.FormPositions(b0, "KHO", false).Single(p => p.Id == "TONLOI");
            Ok(HandoverForm.NhoDefault("KHO", b0) == "GC" && HandoverForm.NhoDefault("PKG", b0) == "KCS" && HandoverForm.NhoDefault("GC", b0) == "HTSP", "Gửi đến mặc định: bộ phận kế tiếp (Đóng gói → KCS)");
            var g = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 2; });
            var rr = await svc.ReceiveAsync(Tx(db).HandoverTxId, 2, 0, null, 1);
            var b = await Check(db, "X3 HTSP giữ 2 ở ô Hàng sửa");
            Ok(g.Ok && rr.Ok && b.G("HTSP").SuaGiu == 2, "Gửi bộ phận khác sửa: phiếu Kho → HTSP ô Hàng sửa 2", g.Error + " | " + rr.Error);
            var cs = b.Cases[vu];
            var posS = HandoverForm.FormPositions(b, "HTSP", false).Single(p => p.Id == "TONSUA");
            var bs = HandoverForm.FormButtons(b, "HTSP", posS, cs, null, false);
            Ok(On(bs, HandoverForm.ASua, HandoverForm.BNho, HandoverForm.AGiu) && bs.Single(x => x.Code == HandoverForm.BTra).Why.Contains("Hàng sửa"),
               "6 nút ô Hàng sửa như mockup TH3 bước 5: Trả nơi làm lỗi xám (nhập Sửa chưa đạt), Huỷ / Trả khách xám", Btn(bs));
            Ok(HandoverService.SuaFailReturnGroup(b, vu, "HTSP") == "KHO", "Chiếc sửa chưa đạt tự trả Kho (nơi làm lỗi)");
            var f = await Form(svc, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 1; r.Fail = 1; r.SuaFailTra = true; r.SuaTo = "GC"; });
            var a = await Check(db, "X3 HTSP sửa 1 đạt 1 chưa đạt");
            var tx2 = db.HandoverTransactions.List.OrderBy(t => t.HandoverTxId).TakeLast(2).ToList();
            Ok(f.Ok && a.G("HTSP").SuaGiu == 0 && a.G("HTSP").LoiGiu == 0 && a.G("HTSP").XlGiu == 0
               && tx2.Any(t => t.ToGroupCode == "GC" && t.FromCell == HandoverCell.Xl && t.ToCell == HandoverCell.Xl && t.QtyIssued == 1)
               && tx2.Any(t => t.ToGroupCode == "KHO" && t.FromCell == HandoverCell.Loi && t.ToCell == HandoverCell.Loi && t.QtyIssued == 1 && t.NgCaseId == vu)
               && db.HandoverRepairResults.List.Last() is { QtyPass: 1, QtyFail: 1 },
               "1 lần lưu: Kết quả sửa đạt 1 / chưa đạt 1; đạt → phiếu HTSP → Gia công ô Hàng lỗi đã xử lý; chưa đạt → phiếu HTSP → Kho ô Hàng lỗi, cùng vụ; HTSP không còn giữ gì",
               f.Error + " | " + string.Join(" ; ", tx2.Select(t => $"{t.ToGroupCode} {t.FromCell}→{t.ToCell} {t.QtyIssued}")));
            foreach (var t in tx2) await svc.ReceiveAsync(t.HandoverTxId, t.ToCell == HandoverCell.Loi ? 0 : 1, t.ToCell == HandoverCell.Loi ? 1 : 0, null, t.ToGroupCode == "KHO" ? 1 : Gc);
            var c = await Check(db, "X3 nhận cả 2 phiếu");
            Ok(c.G("KHO").LoiGiu == 1 && c.Cases[vu].HeldAt("KHO", HandoverCell.Loi) == 1, "Kho giữ lại 1 chiếc chưa đạt ở ô Hàng lỗi, cùng vụ");

            // như cũ khi không bật SuaFailTra (bộ kiểm / trang cũ): chưa đạt ở lại ô Hàng lỗi của nhóm sửa
            WtsSvcHelp.Reset();
            var db2 = Seed(); var svc2 = new HandoverService(db2);
            var vu2 = await PreKhoLoi(db2, svc2);
            await Form(svc2, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu2; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 2; });
            await svc2.ReceiveAsync(Tx(db2).HandoverTxId, 2, 0, null, 1);
            var f2 = await Form(svc2, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu2; r.Action = HandoverForm.ASua; r.Pass = 1; r.Fail = 1; });
            var a2 = await Check(db2, "X3 kiểu cũ");
            Ok(f2.Ok && a2.G("HTSP").LoiGiu == 1 && a2.G("HTSP").XlGiu == 1, "Không bật tự trả (kiểu cũ): chưa đạt ở lại ô Hàng lỗi HTSP, đạt giữ Tồn hàng lỗi đã xử lý", f2.Error);
        }

        Console.WriteLine("[X4] Bảng 6 nút: lỗi mới (không tích / tích Kho / tích khách / chưa chọn), Kho phôi, hàng làm hộ bị lỗi, phôi lỗi Taro, hàng đã sửa bị hỏng lại");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
            var b = await Check(db, "X4");
            var nc1 = HandoverForm.FormPositions(b, "GC", false).Single(p => p.Id == "NC:NC1");
            var all = new[] { HandoverForm.ASua, HandoverForm.BNho, HandoverForm.BTra, HandoverForm.AGiu, HandoverForm.AHuy, HandoverForm.AKhach };
            var noTra = all.Where(x => x != HandoverForm.BTra).ToArray();
            var b1 = HandoverForm.FormButtons(b, "GC", nc1, null, null, false);
            Ok(On(b1, noTra) && b1.Single(x => x.Code == HandoverForm.BTra).Why.Contains("chính Gia công"), "Không tích: Trả nơi làm lỗi xám (nơi làm lỗi là chính Gia công)", Btn(b1));
            var b2 = HandoverForm.FormButtons(b, "GC", nc1, null, "KHO", true);
            Ok(On(b2, all) && HandoverForm.TraTarget("GC", nc1, null, "KHO") == "KHO", "Tích + nơi gây lỗi Kho: đủ 6 nút, Trả đến Kho", Btn(b2));
            var b3 = HandoverForm.FormButtons(b, "GC", nc1, null, "KHACH", true);
            Ok(On(b3, noTra) && b3.Single(x => x.Code == HandoverForm.BTra).Why.Contains("khách"), "Tích + nơi gây lỗi khách: Trả xám (nơi làm lỗi là khách)", Btn(b3));
            var b4 = HandoverForm.FormButtons(b, "GC", nc1, null, "", true);
            Ok(On(b4, noTra) && b4.Single(x => x.Code == HandoverForm.BTra).Why.Contains("Chọn nơi làm lỗi"), "Tích chưa chọn nơi gây lỗi: Trả xám (chọn nơi làm lỗi trước)", Btn(b4));
            var phoi = HandoverForm.FormPositions(b, "KHO", false).Single(p => p.Id == "PHOI");
            var b5 = HandoverForm.FormButtons(b, "KHO", phoi, null, "KHACH", true);
            Ok(On(b5, noTra) && b5.Single(x => x.Code == HandoverForm.BTra).Why.Contains("khách"), "Kho phôi lỗi: Trả xám (nơi làm lỗi là khách — phôi khách)", Btn(b5));
            var lh = new FormPos { Id = "LAMHOLOI", HeldCell = HandoverCell.LamHo };
            Ok(On(HandoverForm.FormButtons(b, "HTSP", lh, null, null, false), HandoverForm.BNho), "Hàng làm hộ bị lỗi: chỉ Gửi bộ phận khác sửa (trả Gia công)");
            var tp = new FormPos { Id = "LHKHO", TaroPhoi = true, Unworked = true };
            Ok(On(HandoverForm.FormButtons(b, "HTSP", tp, null, null, false), HandoverForm.BTra) && HandoverForm.TraTarget("HTSP", tp, null, null) == "KHO", "Phôi lỗi Taro: chỉ Trả nơi làm lỗi → Kho");
            var xl = new FormPos { Id = "TONXL", HeldCell = HandoverCell.Xl };
            Ok(On(HandoverForm.FormButtons(b, "HTSP", xl, null, null, false)), "Hàng đã sửa bị hỏng lại không có vụ: cả 6 nút xám (không có nơi trả)");
        }

        Console.WriteLine("[X5] Máy chủ chặn sai, không ghi gì: giao kèm khi vụ do mình phát hiện / Gia công làm hỏng ở NC (sửa đạt = xong NC) / giao cho chính mình");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
            var sig0 = await FullSig(db);
            var f1 = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC1"; r.Action = HandoverForm.ASua; r.Pass = 1; r.CatId = 1; r.SuaTo = "HTSP"; });
            Ok(!f1.Ok && f1.Error == HandoverService.SuaToFreshMsg && await FullSig(db) == sig0, "Lỗi mới do chính mình phát hiện + giao kèm → chặn, không ghi gì (kể cả báo lỗi)", f1.Error);
            WtsSvcHelp.Reset();
            var db2 = Seed(); var svc2 = new HandoverService(db2);
            var vu = await PreGcNc2(db2, svc2);
            var sig2 = await FullSig(db2);
            var f2 = await Form(svc2, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 1; r.SuaTo = "KCS"; });
            Ok(!f2.Ok && f2.Error == HandoverService.SuaToNoGiuMsg && await FullSig(db2) == sig2, "Gia công làm hỏng ở NC2 (sửa đạt = xong NC2) + giao kèm → chặn, không ghi gì", f2.Error);
            WtsSvcHelp.Reset();
            var db3 = Seed(); var svc3 = new HandoverService(db3);
            var vu3 = await PreTh4(db3, svc3);
            var sig3 = await FullSig(db3);
            var f3 = await Form(svc3, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu3; r.Action = HandoverForm.ASua; r.Pass = 2; r.SuaTo = "KHO"; });
            var f4 = await Form(svc3, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu3; r.Action = HandoverForm.ASua; r.Pass = 3; r.SuaTo = "GC"; });
            Ok(!f3.Ok && !f4.Ok && f4.Error.Contains("tối đa") && await FullSig(db3) == sig3, "Giao cho chính mình / sửa đạt vượt số đang giữ → chặn, không ghi gì", f3.Error + " | " + f4.Error);
            var vk = HandoverForm.SuaGive(await Check(db3, "X5"), "KHO", (await new HandoverLedgerService(db3).GetLedgerForDetailAsync(1))!.Cases[vu3]);
            Ok(vk?.Cell == HandoverCell.Xl, "Tiền đề: vụ TH4 có ô giao đến (giữ ô)");
        }

        Console.WriteLine("[X6] Huỷ: phiếu Kho → Gia công (hàng đã sửa) chưa nhận → huỷ Kết quả sửa bị chặn (K5), huỷ phiếu trước rồi huỷ Kết quả sửa được, sổ về như trước");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh4(db, svc);
            var sig0 = await FullSig(db);
            await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; r.SuaTo = "GC"; });
            var rep = db.HandoverRepairResults.List.Last();
            var px = Tx(db);
            var sig1 = await FullSig(db);
            var v1 = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", 1);
            Ok(!v1.Ok && v1.Error.Contains("K5") && await FullSig(db) == sig1, "Huỷ Kết quả sửa khi hàng đã sửa đang giao đi → K5 chặn", v1.Error);
            var v2 = await svc.VoidIssueAsync(px.HandoverTxId, "thu", 1, new[] { "KHO" });
            var v3 = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", 1);
            await Check(db, "X6 huỷ xong");
            Ok(v2.Ok && v3.Ok && await FullSig(db) == sig0, "Huỷ phiếu rồi huỷ Kết quả sửa: sổ y như trước khi sửa", v2.Error + " | " + v3.Error);
        }

        Console.WriteLine("[X7] Workflow OFF: TH4 + TH10 vẫn chạy (cảnh báo như cũ), số như ON");
        {
            WtsSvcHelp.Reset();
            var db = Seed(workflow: false); var svc = new HandoverService(db);
            var vu = await PreTh4(db, svc);
            var f = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; r.SuaTo = "GC"; });
            await svc.ReceiveAsync(Tx(db).HandoverTxId, 2, 0, null, Gc);
            var a = await Check(db, "X7 TH4 OFF");
            Ok(f.Ok && a.G("GC").NhanLoiXl == 2 && a.G("KHO").GiaoLoi == 2 && a.G("KHO").XlGiu == 0, "OFF: TH4 như ON", f.Error);
            WtsSvcHelp.Reset();
            var db2 = Seed(workflow: false); var svc2 = new HandoverService(db2);
            var vu2 = await PreTh10(db2, svc2);
            var f2 = await Form(svc2, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu2; r.Action = HandoverForm.ASua; r.Pass = 5; r.SuaTo = "HTSP"; });
            await svc2.ReceiveAsync(Tx(db2).HandoverTxId, 5, 0, null, 1);
            var a2 = await Check(db2, "X7 TH10 OFF");
            Ok(f2.Ok && a2.G("HTSP").XlGiu == 5, "OFF: TH10 như ON", f2.Error);
        }

        Console.WriteLine("[X8] Ô Hàng sửa: Gửi bộ phận khác sửa (không xám theo mockup TH3 bước 5) — HTSP chuyển KCS sửa tiếp, ô Hàng sửa → ô Hàng sửa, cùng vụ; Giữ lại không ghi gì");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 2; });
            await svc.ReceiveAsync(Tx(db).HandoverTxId, 2, 0, null, 1);
            var b = await Check(db, "X8 HTSP giữ ô Hàng sửa 2");
            var posS = HandoverForm.FormPositions(b, "HTSP", false).Single(p => p.Id == "TONSUA");
            var bs = HandoverForm.FormButtons(b, "HTSP", posS, b.Cases[vu], null, false);
            Ok(On(bs, HandoverForm.ASua, HandoverForm.BNho, HandoverForm.AGiu) && HandoverForm.GiuNoop(posS), "Ô Hàng sửa: Sửa · Gửi bộ phận khác sửa · Giữ lại bấm được (Giữ lại không cần ghi), Trả / Huỷ / Trả khách xám", Btn(bs));
            var f = await Form(svc, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "KCS"; r.Qty = 2; });
            var tx = Tx(db);
            var a = await Check(db, "X8 HTSP chuyển KCS");
            Ok(f.Ok && tx.FromCell == HandoverCell.Sua && tx.ToCell == HandoverCell.Sua && tx.ToGroupCode == "KCS" && tx.NgCaseId == vu && a.G("HTSP").SuaGiu == 0, "Phiếu HTSP → KCS ô Hàng sửa 2, cùng vụ", f.Error);
            await svc.ReceiveAsync(tx.HandoverTxId, 2, 0, null, Kcs);
            var c = await Check(db, "X8 KCS nhận");
            Ok(HandoverService.SuaFailReturnGroup(c, vu, "KCS") == "KHO" && c.G("KCS").SuaGiu == 2, "KCS giữ ô Hàng sửa 2; chưa đạt sẽ tự trả Kho (nơi làm lỗi)");
            var sig = await FullSig(db);
            var x = await Form(svc, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "PKG"; r.Qty = 1; });
            Ok(!x.Ok && await FullSig(db) == sig, "HTSP không còn hàng sửa → chặn, không ghi gì", x.Error);
        }

        Console.WriteLine("[X9] Rà độc lập T1: Workflow OFF, Gia công giữ sẵn hàng đã sửa 2 (chưa giao); lần sửa sau chọn làm lại NC (không vào giữ ô) + giao kèm → chặn, không lấy 2 chiếc cũ");
        {
            WtsSvcHelp.Reset();
            var db = Seed(workflow: false); var svc = new HandoverService(db);
            var vu = await PreTh10(db, svc);
            var f1 = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; });
            var b = await Check(db, "X9 GC giữ hàng đã sửa 2");
            var sig = await FullSig(db);
            var f2 = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 2; r.NcNext = "NC2"; r.NcReason = "lam lai NC2"; r.SuaTo = "HTSP"; });
            Ok(f1.Ok && b.G("GC").XlGiu == 2 && !f2.Ok && f2.Error == HandoverService.SuaToNoGiuMsg && await FullSig(db) == sig, "Chặn, không ghi gì (trước bản rà: lấy 2 chiếc cũ giao đi)", f1.Error + " | " + f2.Error);
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
