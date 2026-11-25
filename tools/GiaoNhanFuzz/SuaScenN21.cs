// [N21-SCEN 2026-11-25] Kịch bản cố định "n21": luật N21 — WTS có NG KHÔNG tự sinh mã vụ (mọi bộ phận, cả Taro làm hộ): NG nằm ở "NG ở WTS chưa báo lỗi"
//   (tính vào Tồn, chưa giao đi được), báo lỗi riêng ở form Giao / xử lý hàng (dòng "NG ở WTS chưa báo lỗi" → nơi làm lỗi + loại lỗi → mã vụ) rồi 6 nút;
//   báo lỗi được nhiều hơn NG ở WTS (phần dư trừ hàng đã làm / kiểm xong chưa giao; Gia công: chiếc đã làm xong NC đó); sửa WTS giảm NG được; chỉ chặn
//   tổng giao ≤ số nhận (K5). Dòng WTS cũ có vụ (trước N21) giữ nghĩa cũ.
//   Nguồn: KHÁCH 23/11 (mockup TH10 Cách 2 + Sơ đồ chung; sheet dòng 19; chat 16:09–16:19 câu 2); HÙNG 23/11 16:24 (N21) + 25/11 16:27 "ok tự làm đi".
//   Đáp án độc lập = bảng giao nhận mockup TH10 (chép tay: KCS Nhận 30 · WTS 25 / 5 · NG chưa báo lỗi 5 → 0 · KCS Giao·Hàng lỗi 5 · Gia công Nhận·Hàng lỗi 5 ·
//   Gia công Giao·Hàng lỗi 5 · HTSP Nhận / Giao·Hàng lỗi đã xử lý 5 · KCS Nhận·Hàng lỗi đã xử lý 5) + số tính tay từ luật N21. Bằng hàm thật
//   WtsSaveService (WtsSvcHelp) / HandoverService.SaveFormAsync / ReceiveAsync / Void*Async + sổ chung. Sau MỖI bước ghi: Check() chung của kịch bản sua.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    /// <summary>Seed như kịch bản sua nhưng PO + phôi = <paramref name="qty"/> (mockup TH10: 30).</summary>
    static AppDbContext SeedQ(decimal qty, bool workflow = true)
    {
        var db = Seed(workflow: workflow);
        db.KhPlanDetails.List.Single().Quantity = qty;
        db.KhoVatLieus.List.Single().SoLuongThucNhan = (int)qty;
        return db;
    }

    static int NgCaseCount(AppDbContext db) => db.NgCases.List.Count;

    /// <summary>TH10 dựng tới trước bước 1: Kho → GC 30 · GC NC1–NC3 OK 30 · GC → KCS 30 · KCS nhận 30.</summary>
    static async Task PreTh10N21(AppDbContext db, HandoverService svc)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 30, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 30, 0, null, Gc);
        foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(db, "GC", nc, null, 30, 0, Gc, 0);
        await svc.IssueByCellAsync(1, "GC", "KCS", 30, Gc, "MOI", false, "MOI");
        var r = await svc.ReceiveAsync(LastTx(db), 30, 0, null, Kcs);
        Ok(r.Ok, "TH10 dựng: KCS nhận 30 (từ Gia công)", r.Error);
    }

    public static async Task<int> RunN21()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;
        WtsSaveService.SinhVuWts = false;   // luật mới

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[D1] TH10 (mockup Cách 2): KCS WTS OK 25 / NG 5 không sinh vụ → KCS báo lỗi (nơi làm lỗi Gia công) + Trả nơi làm lỗi → Gia công sửa đạt giao HTSP → HTSP trả KCS");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            var nv0 = NgCaseCount(db);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs);   // không chọn loại lỗi
            var log = db.WtsProductionLogs.List.Single(x => x.WtsLogId == w.LogId);
            var a = await Check(db, "D1 KCS WTS 25 / 5");
            var K = a.G("KCS");
            Ok(w.Ok && log.NgCaseId == null && log.NgReason == null && NgCaseCount(db) == nv0,
               "Bước 1: WTS KCS OK 25 NG 5 lưu được KHÔNG chọn loại lỗi; dòng không vụ, không chữ lý do; không sinh mã vụ nào", $"{w.Err} vụ {log.NgCaseId} lý do {log.NgReason} số vụ {nv0}→{NgCaseCount(db)}");
            Ok((w.Warn ?? "").Contains("NG 5 chưa báo lỗi") && (w.Warn ?? "").Contains("Giao nhận"), "Lưu xong nhắc: NG 5 chưa báo lỗi — báo ở trang Giao nhận", w.Warn);
            // mockup: KCS · Nhận 30 · WTS 25 / 5 · NG chưa báo lỗi 5
            Ok(K.NhanTong == 30 && K.DaLam == 25 && K.NgCuoi == 5 && K.NgChuaBao == 5 && K.LoiGiu == 0 && K.ConGiao == 25 && K.ChuaLam == 0 && K.Ton == 30,
               "Mockup TH10: KCS Nhận 30 · WTS OK 25 / NG 5 · NG chưa báo lỗi 5 (không vào Tồn hàng lỗi) · Đã kiểm chưa giao 25 · Tồn 30",
               $"nhận {K.NhanTong} OK {K.DaLam} NG {K.NgCuoi} chưa báo {K.NgChuaBao} loi {K.LoiGiu} da {K.ConGiao} chua {K.ChuaLam} ton {K.Ton}");
            Ok(K.Explains.TryGetValue("NgChuaBao", out var ex) && ex.Result == 5 && K.Explains["LoiGiu"].Result == 0, "Hiện công thức: ô NG chưa báo lỗi = 5, Tồn hàng lỗi = 0");
            var posL = HandoverForm.FormPositions(a, "KCS", false);
            var pw = posL.Single(p => p.Id == "NGW:");
            Ok(pw.Show == 5 && pw.Have == 30 && pw.Fresh && pw.NgSource == HandoverNgSource.NgW && pw.Label == "NG ở WTS chưa báo lỗi" && posL.Single(p => p.Id == "DA").Have == 25,
               "Form bước 2 (Hàng lỗi): dòng \"NG ở WTS chưa báo lỗi\" có 5 (báo được tới 30 = 5 + 25 đã kiểm xong chưa giao); dòng Đã kiểm xong · chưa giao có 25",
               $"show {pw.Show} have {pw.Have} label {pw.Label}");
            Ok(HandoverForm.FormPositions(a, "KCS", true).Single(p => p.Id == "DA").Have == 25, "Hàng tốt chỉ giao được 25 (NG chưa báo lỗi chưa giao đi được)");
            var over = await svc.IssueByCellAsync(1, "KCS", "PKG", 26, Kcs, "MOI", false, "MOI");
            Ok(!over.Ok, "Giao Hàng mới KCS → Đóng gói 26 bị chặn (chỉ 25 đã kiểm xong)", over.Error);
            var wh = HandoverLedgerService.WhereIs(a);
            Ok(wh.Items.Any(i => i.Place == "KCS" && i.Kind == "NGW" && i.Qty == 5) && wh.Balanced && wh.Loi == 5,
               "Đang ở đâu: KCS · NG ở WTS chưa báo lỗi 5; tổng = phôi; tóm tắt \"lỗi 5\"", wh.Short());

            // bước 2: KCS báo lỗi + Trả nơi làm lỗi (Gia công)
            var f = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Prev = true; r.Cause = "GC";
                                           r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PTra; r.ToGroup = "GC"; r.Qty = 5; r.CatId = 1; });
            var rep = db.HandoverNgReports.List.Last(); var tx = Tx(db);
            var vu = rep.NgCaseId!.Value; var cs = db.NgCases.List.Single(x => x.NgCaseId == vu);
            Ok(f.Ok && rep.SourceCell == HandoverNgSource.NgW && rep.GroupCode == "KCS" && rep.Qty == 5 && rep.NC == null && rep.Origin == HandoverNgSource.DauVao && rep.CauseGroupCode == "GC",
               "Bước 2: 1 phiếu báo lỗi KCS 5, nguồn NG ở WTS (NGW), lỗi công đoạn trước — Gia công", f.Error + $" | {rep.SourceCell} {rep.Qty} {rep.Origin} {rep.CauseGroupCode}");
            Ok(cs.OriginKind == NgOrigin.WtsBao && cs.DetectGroupCode == "KCS" && cs.CauseGroupCode == "GC" && NgCaseCount(db) == nv0 + 1,
               "Mã vụ mới (1 vụ): nguồn \"Báo lỗi NG ở WTS\", nơi phát hiện KCS, nơi làm lỗi Gia công", $"{cs.OriginKind} {cs.DetectGroupCode} {cs.CauseGroupCode}");
            Ok(tx.FromGroupCode == "KCS" && tx.ToGroupCode == "GC" && tx.FromCell == HandoverCell.Loi && tx.ToCell == HandoverCell.Loi && tx.QtyIssued == 5 && tx.NgCaseId == vu,
               "Kèm phiếu KCS → Gia công ô Hàng lỗi 5, cùng mã vụ (1 lần lưu)", $"{tx.FromGroupCode}→{tx.ToGroupCode} {tx.FromCell}→{tx.ToCell} {tx.QtyIssued} vụ {tx.NgCaseId}");
            var b = await Check(db, "D1 KCS báo lỗi + trả Gia công");
            Ok(b.G("KCS").NgChuaBao == 0 && b.G("KCS").GiaoLoi == 5 && b.G("KCS").LoiGiu == 0 && b.G("KCS").ConGiao == 25,
               "Mockup TH10: KCS · NG chưa báo lỗi 5 → 0 · KCS · Giao · Hàng lỗi 5 · đã kiểm chưa giao vẫn 25", $"chưa báo {b.G("KCS").NgChuaBao} gl {b.G("KCS").GiaoLoi} da {b.G("KCS").ConGiao}");
            Ok(!HandoverForm.FormPositions(b, "KCS", false).Any(p => p.Id.StartsWith("NGW")), "Hết NG chưa báo: dòng \"NG ở WTS chưa báo lỗi\" ẩn");
            var k = await svc.ReceiveAsync(tx.HandoverTxId, 0, 5, null, Gc);
            var c = await Check(db, "D1 Gia công nhận ô Hàng lỗi");
            Ok(k.Ok && c.G("GC").NhanLoi == 5 && c.G("GC").LoiGiu == 5 && c.Cases[vu].HeldAt("GC", HandoverCell.Loi) == 5, "Mockup TH10: Gia công · Nhận · Hàng lỗi 5 (giữ ô Hàng lỗi, cùng vụ)", k.Error);

            // bước 3–5 (như G-A2): Gia công sửa đạt 5 → HTSP → KCS ô Hàng lỗi đã xử lý
            var s3 = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 5; r.SuaTo = "HTSP"; });
            var px = Tx(db);
            var d3 = await Check(db, "D1 Gia công sửa đạt giao HTSP");
            var r4 = await svc.ReceiveAsync(px.HandoverTxId, 5, 0, null, 1);
            await Check(db, "D1 HTSP nhận");
            var g4 = await Form(svc, r => { r.Group = "HTSP"; r.Kind = HandoverForm.Tot; r.Pos = "TONXL"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.ToGroup = "KCS"; r.Qty = 5; });
            var r5 = await svc.ReceiveAsync(Tx(db).HandoverTxId, 5, 0, null, Kcs);
            var e = await Check(db, "D1 KCS nhận lại");
            Ok(s3.Ok && r4.Ok && g4.Ok && r5.Ok && d3.G("GC").GiaoLoi == 5 && e.G("HTSP").NhanLoiXl == 5 && e.G("HTSP").GiaoLoiXl == 5 && e.G("KCS").NhanLoiXl == 5,
               "Mockup TH10: Gia công · Giao · Hàng lỗi 5 · HTSP · Nhận / Giao · Hàng lỗi đã xử lý 5 · KCS · Nhận · Hàng lỗi đã xử lý 5", $"{s3.Error}|{r4.Error}|{g4.Error}|{r5.Error}");
            Ok(e.Cases.Values.Count(x => x.Id > 0) == 1 && e.G("KCS").NgChuaBao == 0, "Cả TH10 chỉ 1 mã vụ (báo lỗi), không vụ nào từ WTS");
        }

        Console.WriteLine("[D2] Báo lỗi NHIỀU hơn NG ở WTS: KCS NG 5 → báo 7 (dư 2 trừ đã kiểm xong chưa giao); báo 31 bị chặn");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs);
            await Check(db, "D2 trước");
            var x = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Action = HandoverForm.AGiu; r.Qty = 31; r.CatId = 1; });
            Ok(!x.Ok && x.Error.Contains("tối đa 30"), "Báo 31 > 5 + 25 → chặn, báo trần 30", x.Error);
            var f = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Action = HandoverForm.AGiu; r.Qty = 7; r.CatId = 1; });
            var a = await Check(db, "D2 báo 7");
            var K = a.G("KCS");
            // tay: NG chưa báo = max(0, 5 − 7) = 0 · dư 2 → đã kiểm chưa giao 25 − 2 = 23 · Tồn hàng lỗi 7 · Tồn 30
            Ok(f.Ok && K.NgChuaBao == 0 && K.ConGiao == 23 && K.LoiGiu == 7 && K.Ton == 30 && K.Explains["NgChuaBao"].Terms.Any(t => t.Label.StartsWith("Phần báo dư") && t.Value == 2),
               "Báo 7: NG chưa báo 0 · đã kiểm chưa giao 25 → 23 (dư 2) · Tồn hàng lỗi 7 · Tồn 30 · diễn giải có dòng phần báo dư 2",
               $"{f.Error} chưa báo {K.NgChuaBao} da {K.ConGiao} loi {K.LoiGiu} ton {K.Ton}");
            var gx = await svc.IssueByCellAsync(1, "KCS", "PKG", 24, Kcs, "MOI", false, "MOI");
            Ok(!gx.Ok, "Giao Hàng mới 24 > 23 → chặn (tổng giao ≤ số nhận)", gx.Error);
        }

        Console.WriteLine("[D3] Sửa WTS giảm NG sau khi đã báo lỗi: được (phần dư trừ đã kiểm chưa giao); hết hàng để trừ → Workflow ON chặn, OFF lưu");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs);
            var f = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Action = HandoverForm.AGiu; r.Qty = 5; r.CatId = 1; });
            var g = await svc.IssueByCellAsync(1, "KCS", "PKG", 25, Kcs, "MOI", false, "MOI");
            await Check(db, "D3 đã báo 5, giao Đóng gói 25");
            var e1 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 27, 3, Kcs, editLogId: w.LogId);
            var a = await Check(db, "D3 sửa WTS 27 / 3");
            // tay: NG ở WTS 3, đã báo 5 → dư 2; đã kiểm chưa giao = 27 − 25 − 2 = 0; Tồn hàng lỗi vẫn 5; Chưa kiểm 0
            Ok(f.Ok && g.Ok && e1.Ok && a.G("KCS").NgChuaBao == 0 && a.G("KCS").ConGiao == 0 && a.G("KCS").LoiGiu == 5 && a.G("KCS").ChuaLam == 0,
               "Sửa WTS NG 5 → 3 (OK 25 → 27) sau khi đã báo 5: lưu được; dư 2 trừ vào đã kiểm chưa giao (27 − 25 − 2 = 0); Tồn hàng lỗi vẫn 5",
               $"{f.Error}|{g.Error}|{e1.Err} chưa báo {a.G("KCS").NgChuaBao} da {a.G("KCS").ConGiao} loi {a.G("KCS").LoiGiu} chua {a.G("KCS").ChuaLam}");
            var sig0 = await FullSig(db);
            var e2 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 26, 3, Kcs, editLogId: db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.ProcessGroup == "KCS").Max(x => x.WtsLogId));
            Ok(!e2.Ok && await FullSig(db) == sig0, "Sửa tiếp OK 27 → 26 (NG 3): đã kiểm chưa giao sẽ âm (26 − 25 − 2) → Workflow ON chặn, không ghi gì", e2.Err);
            db.KhPlans.List.Single().ApplyWorkflow = false;
            var e3 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 26, 3, Kcs, editLogId: db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.ProcessGroup == "KCS").Max(x => x.WtsLogId));
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            Ok(e3.Ok && row.Balanced && row.G("KCS").ConGiao == -1, "Workflow OFF: lưu được (chỉ chặn mỗi dòng ≤ PO — N7), PO vẫn cân, ô đã kiểm chưa giao lộ âm −1", e3.Err);
        }

        Console.WriteLine("[D4] Gia công NC giữa: WTS NC1 OK 18 NG 2 → báo 3 (dư 1 lấy chiếc đã làm xong NC1, chưa làm NC2) → sửa đạt = xong NC1, về chờ NC2");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 18, 2, Gc);
            var a = await Check(db, "D4 WTS NC1 18 / 2");
            var nc2 = a.G("GC").Steps.Single(s => s.Code == "NC2");
            var pw = HandoverForm.FormPositions(a, "GC", false).Single(p => p.Id == "NGW:NC1");
            // tay: NC2 chờ 18 · Chưa GC 18 · NG chưa báo 2 · trần báo = 2 + 18 (chiếc đã xong NC1 đang chờ NC2)
            Ok(w.Ok && a.G("GC").NgChuaBao == 2 && a.G("GC").LoiGiu == 0 && nc2.Waiting == 18 && a.G("GC").ChuaLam == 18 && pw.Show == 2 && pw.Have == 20 && pw.NgNc == "NC1" && !pw.PrevOk,
               "NG 2 ở NC1 chưa báo lỗi; NC2 chờ 18; dòng form \"NG ở WTS chưa báo lỗi · NC 1\" có 2, báo được tới 20; Workflow ON không tích Lỗi công đoạn trước (NC đã gia công)",
               $"{w.Err} chưa báo {a.G("GC").NgChuaBao} NC2 {nc2.Waiting} chưa GC {a.G("GC").ChuaLam} show {pw.Show} have {pw.Have} prev {pw.PrevOk}");
            var xp = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC1"; r.Prev = true; r.Cause = "KHO"; r.Action = HandoverForm.AGiu; r.Qty = 2; r.CatId = 1; });
            Ok(!xp.Ok, "Workflow ON: tích Lỗi công đoạn trước ở NG ở WTS của Gia công → chặn (như hàng đã gia công)", xp.Error);
            var sig0 = await FullSig(db);
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC1"; r.Action = HandoverForm.AGiu; r.Qty = 3; r.CatId = 1; });
            var b = await Check(db, "D4 báo 3 ở NC1");
            var vu = db.HandoverNgReports.List.Last().NgCaseId!.Value; var cs = db.NgCases.List.Single(x => x.NgCaseId == vu);
            var l1 = b.G("GC").Steps.Single(s => s.Code == "NC1"); var l2 = b.G("GC").Steps.Single(s => s.Code == "NC2");
            // tay: dư 1 = chiếc đã xong NC1 → NC2 chờ 17 · Chưa GC 20 − 2 − 1 = 17 · Tồn hàng lỗi 3 · NC1 OkBao 1
            Ok(f.Ok && b.G("GC").NgChuaBao == 0 && b.G("GC").LoiGiu == 3 && l2.Waiting == 17 && b.G("GC").ChuaLam == 17 && l1.OkBao == 1 && cs.DetectNC == "NC1" && cs.OriginKind == NgOrigin.WtsBao,
               "Báo 3: NG chưa báo 0 · Tồn hàng lỗi 3 · NC2 chờ 18 → 17 (dư 1 lấy chiếc đã xong NC1) · vụ NC phát hiện NC1",
               $"{f.Error} chưa báo {b.G("GC").NgChuaBao} loi {b.G("GC").LoiGiu} NC2 {l2.Waiting} chưa {b.G("GC").ChuaLam} okBao {l1.OkBao} vụ {cs.DetectNC} {cs.OriginKind}");
            Ok(HandoverForm.RepairTarget(b, vu, "GC") == HandoverLedgerService.RepOk, "Gia công tự sửa vụ này: sửa đạt = xong NC1 (như vụ WTS cũ — SUA-OK-NC)");
            var s = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 3; });
            var c = await Check(db, "D4 sửa đạt 3");
            Ok(s.Ok && c.G("GC").LoiGiu == 0 && c.G("GC").Steps.Single(x => x.Code == "NC2").Waiting == 20 && c.G("GC").ChuaLam == 20,
               "Sửa đạt 3 → xong NC1, về chờ NC2: NC2 chờ 20, Chưa GC 20, Tồn hàng lỗi 0", s.Error);
            // huỷ Kết quả sửa rồi huỷ báo lỗi → sổ như trước khi báo
            var vr = await svc.VoidRepairResultAsync(LastRep(db), "thu huy", 1);
            var vb = await svc.VoidNgReportAsync(db.HandoverNgReports.List.Last().HandoverNgReportId, "bao nham", 1, new List<string>());
            await Check(db, "D4 huỷ sửa + huỷ báo");
            Ok(vr.Ok && vb.Ok && await FullSig(db) == sig0, "Huỷ Kết quả sửa rồi huỷ phiếu báo lỗi → sổ đúng như trước khi báo (NG chưa báo 2, NC2 chờ 18)", $"{vr.Error}|{vb.Error}");
        }

        Console.WriteLine("[D5] Gia công NC cuối: NG 1 ở NC3, báo 2 → dư 1 trừ Đã làm chưa giao");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 20, 0, Gc);
            await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC2", null, 20, 0, Gc);
            await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC3", null, 19, 1, Gc);
            var a = await Check(db, "D5 WTS xong");
            var pw = HandoverForm.FormPositions(a, "GC", false).Single(p => p.Id == "NGW:NC3");
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC3"; r.Action = HandoverForm.AGiu; r.Qty = 2; r.CatId = 1; });
            var b = await Check(db, "D5 báo 2");
            Ok(pw.Have == 20 && pw.IsLast && f.Ok && b.G("GC").ConGiao == 18 && b.G("GC").LoiGiu == 2 && b.G("GC").NgChuaBao == 0,
               "NC3 (NC cuối): trần 1 + 19; báo 2 → Đã làm chưa giao 19 → 18, Tồn hàng lỗi 2", $"{pw.Have} {f.Error} da {b.G("GC").ConGiao} loi {b.G("GC").LoiGiu}");
        }

        Console.WriteLine("[D6] Xoá dòng WTS có NG đã báo lỗi: hàng để trừ không đủ → Workflow ON chặn + gợi ý gỡ; còn đủ → xoá được");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 18, 2, Gc);
            await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC2", null, 18, 0, Gc);
            await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC1"; r.Action = HandoverForm.AGiu; r.Qty = 2; r.CatId = 1; });
            await Check(db, "D6 trước");
            var sig0 = await FullSig(db);
            var v = await WtsSvcHelp.VoidAsync(db, w.LogId!.Value, 1, Gc);
            Ok(!v.Ok && await FullSig(db) == sig0 && (v.Err ?? "").Contains("nên kiểm tra"), "Xoá dòng NC1 (OK 18 NG 2) khi NC2 đã làm 18 và 2 chiếc đã báo lỗi → chặn (NC2 âm), có gợi ý gỡ, không ghi gì", v.Err);
        }

        Console.WriteLine("[D6b] Xoá dòng WTS NC giữa có NG đã báo dư (NC sau chưa làm): chặn, gợi ý gỡ chỉ đúng phiếu Báo lỗi (NG ở WTS)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 18, 2, Gc);
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC1"; r.Action = HandoverForm.AGiu; r.Qty = 3; r.CatId = 1; });
            var sig0 = await FullSig(db);
            var v = await WtsSvcHelp.VoidAsync(db, w.LogId!.Value, 1, Gc);
            var bl = db.HandoverNgReports.List.Last().HandoverNgReportId;
            Ok(f.Ok && !v.Ok && await FullSig(db) == sig0 && (v.Err ?? "").Contains($"Báo lỗi #{bl}"), "Xoá NC1 (OK 18 NG 2, đã báo 3): NC2 âm → chặn; gợi ý gỡ nêu phiếu Báo lỗi (NG ở WTS)", v.Err);
        }

        Console.WriteLine("[D7] Dòng WTS cũ (trước N21) giữ nghĩa cũ: kiểu cũ sinh vụ → Tồn hàng lỗi; sửa dòng giữ vụ + bắt loại lỗi; dòng rất cũ có chữ lý do → Tồn hàng lỗi (vụ khoá 0)");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            WtsSaveService.SinhVuWts = true;
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs, catId: 1);
            WtsSaveService.SinhVuWts = false;
            var log = db.WtsProductionLogs.List.Single(x => x.WtsLogId == w.LogId);
            var a = await Check(db, "D7 dòng kiểu cũ");
            Ok(w.Ok && log.NgCaseId != null && a.G("KCS").LoiGiu == 5 && a.G("KCS").NgChuaBao == 0 && !HandoverForm.FormPositions(a, "KCS", false).Any(p => p.Id.StartsWith("NGW")),
               "Dòng có vụ (kiểu cũ): NG 5 ở Tồn hàng lỗi (vụ WTS), không có NG chưa báo lỗi", w.Err);
            var e0 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 26, 4, Kcs, catId: null, keepCase: log.NgCaseId, editLogId: log.WtsLogId);
            Ok(!e0.Ok && (e0.Err ?? "").Contains("loại lỗi"), "Sửa dòng có vụ (luật mới) mà bỏ trống loại lỗi → chặn (dòng cũ giữ vụ + loại lỗi)", e0.Err);
            var e1 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 26, 4, Kcs, catId: 1, keepCase: log.NgCaseId, editLogId: log.WtsLogId);
            var b = await Check(db, "D7 sửa dòng cũ");
            var nl = db.WtsProductionLogs.List.Single(x => x.WtsLogId == e1.LogId);
            Ok(e1.Ok && nl.NgCaseId == log.NgCaseId && b.G("KCS").LoiGiu == 4 && b.G("KCS").NgChuaBao == 0, "Sửa dòng có vụ: dòng mới giữ đúng vụ cũ, NG 4 ở Tồn hàng lỗi", e1.Err);
            // dòng rất cũ: không vụ, có chữ lý do (trước vụ lỗi 28/10)
            var db2 = SeedQ(30); var svc2 = new HandoverService(db2);
            await PreTh10N21(db2, svc2);
            db2.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 99901, KhPlanDetailId = 1, ProcessGroup = "KCS", WtsCode = "K1", WorkerId = Kcs, QtyDone = 30, QtyOk = 27, QtyNg = 3,
                NgReason = "xuoc (go tay cu)", StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            db2.Fixup();
            var c = await Check(db2, "D7 dòng rất cũ");
            Ok(c.G("KCS").LoiGiu == 3 && c.G("KCS").NgChuaBao == 0 && c.CaseHeld(0, "KCS", HandoverCell.Loi) == 3, "Dòng rất cũ có chữ lý do: NG 3 vẫn ở Tồn hàng lỗi (hàng lỗi cũ chưa có mã vụ)");
        }

        Console.WriteLine("[D8] Taro làm hộ: NG ở WTS Taro làm hộ không sinh vụ → nằm trong hàng giao theo NC (không trả về được) → báo lỗi + trả Gia công ô Hàng lỗi");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true); TaroScen.SeedGopN21(db);
            var pid = await TaroScen.BuildLamHoPartN21(db, "P-N21LH");
            var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-N21LH", new decimal[] { 10 }, "PO-N21LH"))[0];
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db); const int U = ChuoiHelp.Admin;
            var kho = new KhoVatLieuService(db, new UserService(null));
            await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            await svc.IssueByCellAsync(det, "KHO", "GC", 10, U, "MOI", false, "MOI");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 10, 0, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 10, 0, U);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 10, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            var nv0 = db.NgCases.List.Count;
            var t1 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "2T", 9, 1, U, null, null, null, stepNc: "3");
            foreach (var cd in new[] { "3T" }) await WtsSvcHelp.SaveAsync(db, det, "TARO", null, cd, 9, 0, U, null, null, null, stepNc: "3");
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var run = row.LamHoRuns.Single(r => r.HasTaro);
            var pos = HandoverForm.FormPositions(row, "HTSP", false).SingleOrDefault(p => p.Id == "NGWLH:" + run.Key);
            var good = HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO");
            Ok(t1.Ok && db.NgCases.List.Count == nv0 && row.LamHoNgPendOf("HTSP") == 1 && row.LamHoNgHeld("HTSP") == 0 && row.G("HTSP").LamHoGiu == 10 && row.Balanced
               && pos != null && pos.Show == 1 && pos.LamHoNgW && good.Have == 9 && !HandoverLedgerService.NegativeCells(row).Any(),
               "WTS Taro làm hộ NG 1: không sinh vụ; NG chưa báo (làm hộ) 1 trong Tồn hàng giao theo NC 10; trả về được tối đa 9; form có dòng NG ở WTS Taro làm hộ chưa báo lỗi 1",
               $"{t1.Err} vụ {nv0}→{db.NgCases.List.Count} pend {row.LamHoNgPendOf("HTSP")} lh {row.G("HTSP").LamHoGiu} pos {pos?.Show} good {good.Have}");
            var bad = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = pos!.Id, Action = HandoverForm.AGiao, ToGroup = "KCS", Qty = 1, CatId = 1 });
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = pos.Id, Action = HandoverForm.AGiao, ToGroup = "GC", Qty = 1, CatId = 1 });
            var lt = db.HandoverTransactions.List.Last(); var rep = db.HandoverNgReports.List.Last();
            row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(!bad.Ok && f.Ok && rep.SourceCell == HandoverNgSource.NgW && rep.NC == run.Key && lt.Purpose == HandoverPurpose.HelpReturn && lt.FromCell == HandoverCell.Loi
               && lt.ToCell == HandoverCell.Loi && lt.NgCaseId == rep.NgCaseId && row.LamHoNgPendOf("HTSP") == 0 && row.Cases[rep.NgCaseId!.Value].IsLamHo && row.Balanced
               && !HandoverLedgerService.NegativeCells(row).Any(),
               "Báo lỗi + trả KCS → chặn (chỉ Gia công); trả Gia công 1 → phiếu báo lỗi (nguồn NGW, đoạn làm hộ) + phiếu trả về Gia công ô Hàng lỗi cùng vụ (vụ làm hộ)", $"{bad.Error} | {f.Error}");
            var g1 = await svc.ReceiveAsync(lt.HandoverTxId, 0, 1, null, U);
            row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(g1.Ok && row.CaseHeld(rep.NgCaseId!.Value, "GC", HandoverCell.Loi) == 1 && row.G("HTSP").LamHoGiu == 9 && row.Balanced, "Gia công nhận ô Hàng lỗi 1 (cùng vụ); HTSP còn 9 hàng giao theo NC", g1.Error);
        }

        Console.WriteLine("[D8b] Taro làm hộ báo dư: phần dư lấy từ chiếc đã xong bộ Taro chưa trả về — hàng đang chờ Taro làm không đổi (trần WTS Taro giữ)");
        {   // [N21-NGW 2026-11-25] fuzz seed 2104 PO27: bản nháp cộng phần dư vào NG đoạn → hàng còn tốt về 0 mà trần WTS Taro vẫn cho nhập (lưu bị K5 chặn)
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true); TaroScen.SeedGopN21(db);
            var pid = await TaroScen.BuildLamHoPartN21(db, "P-N21LB");
            var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-N21LB", new decimal[] { 10 }, "PO-N21LB"))[0];
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db); const int U = ChuoiHelp.Admin;
            var kho = new KhoVatLieuService(db, new UserService(null));
            await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            await svc.IssueByCellAsync(det, "KHO", "GC", 10, U, "MOI", false, "MOI");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 10, 0, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 10, 0, U);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 10, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "2T", 4, 1, U, null, null, null, stepNc: "3");
            await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "3T", 4, 0, U, null, null, null, stepNc: "3");
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var run = row.LamHoRuns.Single(r => r.HasTaro);
            var pos = HandoverForm.FormPositions(row, "HTSP", false).Single(p => p.Id == "NGWLH:" + run.Key);
            Ok(pos.Show == 1 && pos.Have == 1 + 4 && run.Done == 4, "NG chưa báo 1; báo được tối đa 1 + 4 chiếc đã xong bộ Taro (không tính 5 chiếc đang chờ Taro làm)", $"show {pos.Show} have {pos.Have} done {run.Done}");
            var over = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = pos.Id, Action = HandoverForm.AGiu, Qty = 6, CatId = 1 });
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = pos.Id, Action = HandoverForm.AGiu, Qty = 3, CatId = 1 });
            row = (await led.GetLedgerForDetailAsync(det))!; run = row.LamHoRuns.Single(r => r.HasTaro);
            var good = HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO");
            Ok(!over.Ok && f.Ok && run.Bao == 2 && run.Held == 10 - 1 - 2 && good.Have == 4 - 2 && row.LamHoNgPendOf("HTSP") == 0 && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "Báo 6 (> 5) → chặn; báo 3 (dư 2) → phần dư 2 trừ chiếc đã xong bộ: trả về Gia công hàng tốt tối đa 2, hàng còn tốt của đoạn 7", $"{over.Error} | {f.Error} bao {run.Bao} held {run.Held} good {good.Have}");
            var w2 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "2T", 5, 0, U, null, null, null, stepNc: "3");
            var w3 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "3T", 5, 0, U, null, null, null, stepNc: "3");
            row = (await led.GetLedgerForDetailAsync(det))!; run = row.LamHoRuns.Single(r => r.HasTaro);
            good = HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO");
            Ok(w2.Ok && w3.Ok && run.Done == 9 && good.Have == 9 - 2 && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "5 chiếc chờ Taro vẫn nhập WTS được (2T, 3T OK 5); xong bộ 9 − báo dư 2 → trả về hàng tốt tối đa 7", $"{w2.Err} | {w3.Err} done {run.Done} good {good.Have}");
        }

        Console.WriteLine("[D9] Workflow OFF: báo lỗi NG ở WTS như ON (số giống), dư vẫn trừ đã kiểm chưa giao");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30, workflow: false); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs);
            var f = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Prev = true; r.Cause = "GC";
                                           r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PTra; r.ToGroup = "GC"; r.Qty = 6; r.CatId = 1; });
            var a = await Check(db, "D9 OFF");
            Ok(f.Ok && a.G("KCS").NgChuaBao == 0 && a.G("KCS").ConGiao == 24 && a.G("KCS").GiaoLoi == 6, "OFF: báo 6 (dư 1) + trả Gia công 6 → đã kiểm chưa giao 24", f.Error);
        }

        // ── [N21-NGW rà độc lập G-D 25/11] các ca bổ sung ──
        Console.WriteLine("[D10] Theo thời gian (rà độc lập C1): báo dư rồi mới có NG ở WTS mới → NG mới là NG chưa báo (không nuốt phần dư); sửa dòng cũ tăng NG = sửa lại số đếm (tính ở giờ gốc)");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(40); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            var w1 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 25, 5, Kcs);
            var f = await Form(svc, r => { r.UserId = Kcs; r.Group = "KCS"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:"; r.Action = HandoverForm.AGiu; r.Qty = 7; r.CatId = 1; });
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
            foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(db, "GC", nc, null, 10, 0, Gc, 0);
            await svc.IssueByCellAsync(1, "GC", "KCS", 10, Gc, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 10, 0, null, Kcs);
            var w2 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 8, 2, Kcs);
            var a = await Check(db, "D10 đợt 2");
            // tay: đợt 1 báo 7 (NG 5 + dư 2) → đã kiểm chưa giao 23; đợt 2 OK 8 NG 2 (ghi SAU phiếu) → chưa báo 2, đã kiểm chưa giao 23 + 8 = 31; Tồn hàng lỗi 7
            Ok(f.Ok && w2.Ok && a.G("KCS").NgChuaBao == 2 && a.G("KCS").ConGiao == 31 && a.G("KCS").LoiGiu == 7 && a.G("KCS").Ton == 40,
               "Đợt 2 (NG 2 ghi sau lần báo dư): NG chưa báo 2, đã kiểm chưa giao 31 (không đếm thừa), Tồn hàng lỗi 7",
               $"{f.Error}|{w2.Err} chưa báo {a.G("KCS").NgChuaBao} da {a.G("KCS").ConGiao} loi {a.G("KCS").LoiGiu}");
            var e1 = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 23, 7, Kcs, editLogId: w1.LogId);
            var b = await Check(db, "D10 sửa dòng đợt 1 → 23 / 7");
            // tay: dòng đợt 1 sửa thành NG 7 (giờ gốc trước phiếu) → phiếu 7 lấy đủ từ NG, dư 0 → đã kiểm chưa giao 23 + 8 = 31; chưa báo vẫn 2 (đợt 2)
            Ok(e1.Ok && b.G("KCS").NgChuaBao == 2 && b.G("KCS").ConGiao == 31 && b.G("KCS").Explains["NgChuaBao"].Terms.All(t => !t.Label.StartsWith("Phần báo dư")),
               "Sửa dòng đợt 1 NG 5 → 7 (đếm lại): hết phần dư, NG chưa báo vẫn 2 của đợt 2, đã kiểm chưa giao 31",
               $"{e1.Err} chưa báo {b.G("KCS").NgChuaBao} da {b.G("KCS").ConGiao}");
        }

        Console.WriteLine("[D11] PO chưa có NC Gia công (rà độc lập T1): NG ở WTS Gia công 1 dòng chung, báo lỗi khớp, không trừ oan");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            db.KhPlanRouteSnapshotMachining.List.Clear(); db.Fixup();
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 99911, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = Gc, QtyDone = 20, QtyOk = 15, QtyNg = 5,
                StartTime = DateTime.Now.AddMinutes(-30), EndTime = DateTime.Now.AddMinutes(-10), CreatedAt = DateTime.Now.AddMinutes(-10) });
            db.Fixup();
            var a = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;   // (Check chung báo cảnh báo "PO chưa có snapshot NC" — đúng ý ca này)
            var pos = HandoverForm.FormPositions(a, "GC", false).SingleOrDefault(p => p.Id.StartsWith("NGW"));
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = pos?.Id ?? "NGW:"; r.Action = HandoverForm.AGiu; r.Qty = 5; r.CatId = 1; });
            var b = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            Ok(pos != null && pos.Id == "NGW:" && pos.Show == 5 && a.G("GC").NgChuaBao == 5 && a.Balanced && b.Balanced && f.Ok && b.G("GC").NgChuaBao == 0 && b.G("GC").ConGiao == 15 && b.G("GC").LoiGiu == 5,
               "PO không NC: dòng 'NG ở WTS chưa báo lỗi' chung có 5 (sổ cân); báo 5 → chưa báo 0, đã làm chưa giao vẫn 15 (không trừ oan), Tồn hàng lỗi 5",
               $"pos {pos?.Id} {pos?.Show} {f.Error} chưa báo {b.G("GC").NgChuaBao} da {b.G("GC").ConGiao} loi {b.G("GC").LoiGiu}");
        }

        Console.WriteLine("[D12] Sửa WTS Taro làm hộ giảm NG sau khi đã báo (rà độc lập T3): phần báo thành dư, trừ chiếc đã xong bộ Taro");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true); TaroScen.SeedGopN21(db);
            var pid = await TaroScen.BuildLamHoPartN21(db, "P-N21LE");
            var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-N21LE", new decimal[] { 10 }, "PO-N21LE"))[0];
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db); const int U = ChuoiHelp.Admin;
            var kho = new KhoVatLieuService(db, new UserService(null));
            await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            await svc.IssueByCellAsync(det, "KHO", "GC", 10, U, "MOI", false, "MOI");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 10, 0, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 10, 0, U);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 10, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            var t2 = await WtsSvcHelp.SaveWAsync(db, det, "TARO", null, "2T", 4, 1, U, stepNc: "3");
            await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "3T", 5, 0, U, null, null, null, stepNc: "3");
            var row = (await led.GetLedgerForDetailAsync(det))!; var run = row.LamHoRuns.Single(r => r.HasTaro);
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "NGWLH:" + run.Key, Action = HandoverForm.AGiu, Qty = 1, CatId = 1 });
            var e = await WtsSvcHelp.SaveWAsync(db, det, "TARO", null, "2T", 5, 0, U, editLogId: t2.LogId, stepNc: "3");
            row = (await led.GetLedgerForDetailAsync(det))!; run = row.LamHoRuns.Single(r => r.HasTaro);
            var good = HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO");
            // tay: báo 1 = NG 1; sửa 2T 4/1 → 5/0 → dư 1; xong bộ = min(5, 5) = 5 → trả về hàng tốt tối đa 5 − 1 = 4
            Ok(f.Ok && e.Ok && run.Bao == 1 && run.Done == 5 && good.Have == 4 && row.LamHoNgPendOf("HTSP") == 0 && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "Sửa 2T NG 1 → 0 sau khi đã báo 1: dư 1 trừ chiếc đã xong bộ (5 − 1 = 4 trả về được)", $"{f.Error}|{e.Err} bao {run.Bao} done {run.Done} good {good.Have}");
            // trả về Gia công 4 (hết hàng tốt đã xong bộ) rồi sửa 3T OK 5 → 4: xong bộ 4 − trả 4 − dư 1 = −1 → Workflow ON chặn (ô K5 "Taro làm hộ … trả về nhiều hơn OK bộ")
            var rt = await svc.IssueSpecialAsync(det, "HTSP", "GC", 4, U, HandoverPurpose.HelpReturn, null, run.NextNc, "tra ve");
            var t3 = db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.WtsCode == "3T").OrderBy(x => x.WtsLogId).Last();
            var sig0 = await FullSig(db);
            var e3 = await WtsSvcHelp.SaveWAsync(db, det, "TARO", null, "3T", 4, 0, U, editLogId: t3.WtsLogId, stepNc: "3");
            Ok(rt.Ok && !e3.Ok && await FullSig(db) == sig0, "Trả về 4 rồi sửa 3T OK 5 → 4: đã trả + báo dư vượt OK bộ → chặn (K5), không ghi gì", $"{rt.Error} | {e3.Err}");
        }

        Console.WriteLine("[D13] Sửa dòng WTS RẤT CŨ (NG không vụ, có chữ lý do) giữ nghĩa cũ (rà độc lập T4)");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 99921, KhPlanDetailId = 1, ProcessGroup = "KCS", WtsCode = "K1", WorkerId = Kcs, QtyDone = 30, QtyOk = 27, QtyNg = 3,
                NgReason = "xuoc (go tay cu)", StartTime = DateTime.Now.AddMinutes(-20), EndTime = DateTime.Now.AddMinutes(-5), CreatedAt = DateTime.Now.AddMinutes(-5) });
            db.Fixup();
            var gl = await svc.IssueByCellAsync(1, "KCS", "GC", 3, Kcs, "LOI", false, "LOI", reason: "tra GC");   // 3 chiếc lỗi cũ (vụ khoá 0) đã đi khỏi Tồn hàng lỗi
            var e = await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 27, 3, Kcs, editLogId: 99921);
            var a = await Check(db, "D13 sửa dòng rất cũ");
            var nl = db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.ProcessGroup == "KCS").OrderBy(x => x.WtsLogId).Last();
            Ok(gl.Ok && e.Ok && nl.WtsLogId != 99921 && nl.NgCaseId == null && nl.NgReason == "xuoc (go tay cu)" && a.G("KCS").LoiGiu == 0 && a.G("KCS").GiaoLoi == 3 && a.G("KCS").NgChuaBao == 0,
               "3 chiếc lỗi cũ đã trả Gia công; sửa lại dòng (cùng số): Workflow ON không chặn oan, dòng mới giữ chữ lý do → NG 3 vẫn tính là hàng lỗi đã đi (không thành NG chưa báo)", $"{e.Err} lý do '{nl.NgReason}' loi {a.G("KCS").LoiGiu} chưa báo {a.G("KCS").NgChuaBao}");
        }

        Console.WriteLine("[D14] Báo lỗi nguồn 'NG ở WTS' khi không còn NG chưa báo → chặn (rà độc lập L10)");
        {
            WtsSvcHelp.Reset();
            var db = SeedQ(30); var svc = new HandoverService(db);
            await PreTh10N21(db, svc);
            await WtsSvcHelp.SaveWAsync(db, 1, "KCS", null, "K1", 30, 0, Kcs);
            var sig0 = await FullSig(db);
            var r = await svc.ReportNgAfterReceiveAsync(1, "KCS", HandoverNgSource.NgW, null, 2, HandoverNgSource.DauRa, null, "", null, Kcs, new List<string> { "KCS" }, 1);
            Ok(!r.Ok && await FullSig(db) == sig0, "KCS không có NG ở WTS: báo nguồn NGW 2 → chặn, không ghi gì", r.Error);
        }

        Console.WriteLine("[D15] Gia công NC ngay trước máy loại trừ: báo dư trừ OK NC chưa giao theo NC");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true); TaroScen.SeedGopN21(db);
            var pid = await TaroScen.BuildLamHoPartN21(db, "P-N21LX");
            var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-N21LX", new decimal[] { 10 }, "PO-N21LX"))[0];
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db); const int U = ChuoiHelp.Admin;
            var kho = new KhoVatLieuService(db, new UserService(null));
            await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            await svc.IssueByCellAsync(det, "KHO", "GC", 10, U, "MOI", false, "MOI");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 10, 0, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 8, 2, U);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var pos = HandoverForm.FormPositions(row, "GC", false).Single(p => p.Id == "NGW:NC02");
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "GC", UserId = U, Kind = HandoverForm.Loi, Pos = pos.Id, Action = HandoverForm.AGiu, Qty = 3, CatId = 1 });
            var h8 = await svc.IssueSpecialAsync(det, "GC", "HTSP", 8, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
            var h7 = await svc.IssueSpecialAsync(det, "GC", "HTSP", 7, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
            row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(pos.Show == 2 && pos.Have == 2 + 8 && f.Ok && !h8.Ok && h7.Ok && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "NC02 OK 8 NG 2 (NC sau máy loại trừ): báo được tối đa 2 + 8; báo 3 (dư 1) → giao theo NC 8 chặn, 7 được", $"have {pos.Have} {f.Error} | h8 {h8.Error} | h7 {h7.Error}");
        }

        Console.WriteLine("[D16] Sửa dòng WTS N21 (NG không vụ) giảm OK khi NC sau đã dùng → Workflow ON chặn (K5) như dòng thường (harness drive.py S10)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 10, 2, Gc);
            await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC2", null, 5, 0, Gc);
            var e1 = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 12, 2, Gc, editLogId: w.LogId);
            var cur = db.WtsProductionLogs.List.Where(x => !x.IsVoided && x.ProcessGroup == "GC" && x.NC == "NC1").Max(x => x.WtsLogId);
            var sig0 = await FullSig(db);
            var e2 = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 1, 2, Gc, editLogId: cur);
            Ok(w.Ok && e1.Ok && !e2.Ok && await FullSig(db) == sig0 && (e2.Err ?? "").Contains("NC2"), "Sửa NC1 OK 12 → 1 (NG 2 không vụ) khi NC2 đã làm 5 → chặn (NC2 âm)", $"{e1.Err} | {e2.Err}");
        }

        Console.WriteLine("[D17] Sửa dòng WTS đổi khoá (NC → máy đồng dạng) sau khi đã báo dư (rà lượt 2 M1): thử lưu xếp như dòng mới → Workflow ON chặn đúng như lưu thật");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 9, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1b", MachineRegistered = "M9", IsBackup = true, ParentNC = "NC1" });
            db.Fixup();
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            var w = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1", null, 15, 5, Gc);
            var f = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NGW:NC1"; r.Action = HandoverForm.AGiu; r.Qty = 7; r.CatId = 1; });
            var n2 = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC2", null, 13, 0, Gc);
            var sig0 = await FullSig(db);
            var e = await WtsSvcHelp.SaveWAsync(db, 1, "GC", "NC1b", null, 15, 5, Gc, editLogId: w.LogId);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            // tay: dòng sửa sang NC1b (máy đồng dạng của NC1 — cùng NC chính nhưng KHÁC khoá dòng) = dòng mới ghi SAU phiếu 7 → phiếu lấy từ 0 NG ghi trước → dư 7 → NC2 chờ 15 − 7 − 13 = −5 → chặn
            Ok(w.Ok && f.Ok && n2.Ok && !e.Ok && await FullSig(db) == sig0 && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "Sửa NC1 → NC1b (cùng số) sau khi đã báo 7 (dư 2) và NC2 đã làm 13: ON chặn (lưu thật sẽ thành dư 7, NC2 âm), không ghi gì", $"{f.Error} | {e.Err}");
        }

        Console.WriteLine($"KẾT QUẢ: {(fails == 0 ? "ĐẠT hết" : fails + " SAI")}");
        WtsSaveService.SinhVuWts = true;
        return fails == 0 ? 0 : 1;
    }
}
