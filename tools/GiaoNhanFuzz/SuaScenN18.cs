// [N18-SCEN 2026-11-25] Kịch bản cố định "n18": luật N18 Cách Y — chiếc "Sửa chưa đạt" ở ô Hàng sửa phần mềm tự trả nơi làm lỗi (N23) được ĐẾM ở cột riêng
//   "Nhận · Hàng sửa trả về chưa đạt" của nơi làm lỗi, không vào Nhận · Hàng lỗi (không tính thêm 1 lỗi); bên sửa không hiện Nhận / Giao · Hàng lỗi cho phần đó.
//   Nguồn: KHÁCH 23/11 (mockup TH6, TH7 chọn Cách Y; sheet dòng 15, 16); HÙNG 23/11 16:24 (N18) + 25/11 13:56 OK bản "em hiểu" G-E.
//   Đáp án độc lập = cột "Cách Y" của bảng giao nhận trong mockup TH6 / TH7 (số chép tay từ mockup, không lấy từ code). Bằng hàm thật HandoverService.SaveFormAsync /
//   ReceiveAsync / Void*Async + sổ chung. Sau MỖI bước ghi: Check() chung của kịch bản sua (cân PO, không ô âm, vụ cân, khung lỗi, dòng B cuối = sổ, Đang ở đâu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    const int Htsp = 4;   // người thử HTSP (FuzzPerm.Xuong: có quyền mọi nhóm sản xuất — như Gc / Kcs)

    /// <summary>TH6 dựng tới bước 1: Kho → Gia công 20 (nhận đủ) · Gia công phát hiện 1 lỗi (vừa nhận, chưa làm NC 1), tích Lỗi công đoạn trước (Kho), Trả nơi làm lỗi
    ///   → Kho nhận ô Hàng lỗi · Kho "Gửi bộ phận khác sửa" HTSP 1 → HTSP nhận ô Hàng sửa. Trả mã vụ.</summary>
    static async Task<long> PreTh6(AppDbContext db, HandoverService svc)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
        var t = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC1"; r.Prev = true; r.Cause = "KHO";
                                       r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PTra; r.ToGroup = "KHO"; r.Qty = 1; r.CatId = 1; });
        var tx = Tx(db);
        var k = await svc.ReceiveAsync(tx.HandoverTxId, 0, 1, null, 1);
        var vu = tx.NgCaseId!.Value;
        var g = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 1; });
        var h = await svc.ReceiveAsync(Tx(db).HandoverTxId, 1, 0, null, Htsp);
        Ok(t.Ok && k.Ok && g.Ok && h.Ok, "TH6 bước 1: Gia công trả Kho 1 (VL…), Kho gửi HTSP sửa 1, HTSP nhận ô Hàng sửa", $"{t.Error}|{k.Error}|{g.Error}|{h.Error}");
        return vu;
    }

    /// <summary>TH6 bước 2: HTSP "Sửa tại bộ phận mình" — Sửa đạt 0, Sửa chưa đạt 1 (phần mềm tự trả Kho). Trả phiếu tự trả.</summary>
    static async Task<HandoverTransaction> Th6Fail(AppDbContext db, HandoverService svc, long vu)
    {
        var f = await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua;
                                       r.Pass = 0; r.Fail = 1; r.SuaFailTra = true; });
        var tv = Tx(db);
        Ok(f.Ok && tv.FromGroupCode == "HTSP" && tv.ToGroupCode == "KHO" && tv.FromCell == HandoverCell.Loi && tv.ToCell == HandoverCell.LoiVe
           && tv.Purpose == HandoverPurpose.RepairSend && tv.QtyIssued == 1 && tv.NgCaseId == vu && tv.Status == "PENDING",
           "TH6 bước 2: Sửa chưa đạt 1 → phiếu HTSP → Kho 1, ô chỉ định \"Hàng sửa trả về chưa đạt\" (LOIV), cùng mã vụ",
           f.Error + $" | {tv.FromGroupCode}→{tv.ToGroupCode} {tv.FromCell}→{tv.ToCell} {tv.Purpose} {tv.QtyIssued} vụ {tv.NgCaseId} {tv.Status}");
        return tv;
    }

    public static async Task<int> RunN18()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[E1] TH6 (mockup, Cách Y): Gia công trả Kho 1 → Kho gửi HTSP sửa → HTSP sửa chưa đạt 1 → phần mềm tự trả Kho → Kho nhận");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var b = await Check(db, "E1 HTSP giữ 1 ở ô Hàng sửa");
            var tv = await Th6Fail(db, svc, vu);
            var a = await Check(db, "E1 sau Sửa chưa đạt");
            Ok(a.G("HTSP").NhanLoi == 0 && a.G("HTSP").GiaoLoi == 0 && a.G("HTSP").LoiGiu == 0 && a.G("HTSP").SuaGiu == 0 && a.G("KHO").ChoNhan == 1 && a.Cases[vu].Transit == 1,
               "HTSP không giữ gì, không hiện Nhận / Giao · Hàng lỗi (chiếc không nằm lại HTSP); Kho chờ nhận 1; vụ đang chuyển 1",
               $"HTSP nl {a.G("HTSP").NhanLoi} gl {a.G("HTSP").GiaoLoi} loi {a.G("HTSP").LoiGiu} sua {a.G("HTSP").SuaGiu} · Kho cn {a.G("KHO").ChoNhan} · vụ chuyển {a.Cases[vu].Transit}");
            var k = await svc.ReceiveAsync(tv.HandoverTxId, 0, 1, null, 1);
            var c = await Check(db, "E1 Kho nhận");
            var rc = db.HandoverReceives.List.Last();
            // đáp án = cột "Cách Y" bảng giao nhận mockup TH6 (chép tay)
            var G = c.G("GC"); var K = c.G("KHO"); var H = c.G("HTSP");
            Ok(k.Ok && rc.ToCell == HandoverCell.Loi
               && G.GiaoLoi == 1 && K.NhanLoi == 1 && K.NhanLoiVe == 1 && K.GiaoLoi == 1 && H.NhanSua == 1 && H.GiaoSua == 1 && K.LoiGiu == 1,
               "Mockup TH6 Cách Y: GC Giao·Hàng lỗi 1 · Kho Nhận·Hàng lỗi 1 (không 2) · Kho Nhận·Hàng sửa trả về chưa đạt 1 · Kho Giao·Hàng lỗi 1 · HTSP Nhận / Giao·Hàng sửa 1 · Kho giữ 1 chiếc lỗi",
               $"{k.Error} | lần nhận ô {rc.ToCell} · GC gl {G.GiaoLoi} · Kho nl {K.NhanLoi} nlv {K.NhanLoiVe} gl {K.GiaoLoi} loi {K.LoiGiu} · HTSP ns {H.NhanSua} gs {H.GiaoSua}");
            Ok(c.Cases[vu].SinhRa == 1 && c.Cases[vu].HeldAt("KHO", HandoverCell.Loi) == 1 && c.Cases.Values.Count(x => x.Id > 0) == 1,
               "Số lỗi của PO vẫn 1 (1 vụ, sinh ra 1), chiếc nằm ở ô Hàng lỗi Kho, cùng vụ", $"sinh {c.Cases[vu].SinhRa} vụ {c.Cases.Count}");
            Ok(K.Ton == 1 && b.G("KHO").Ton == 0 && H.Ton == 0 && c.Balanced, "Mockup TH6: Kho · Tồn 1 (chiếc lỗi đang ở Kho; Kho đã giao hết phôi), HTSP Tồn 0, PO cân", $"Kho Tồn {b.G("KHO").Ton}→{K.Ton} HTSP {H.Ton}");
            var tr = HandoverNgTrace.BuildCases(c).Single(x => x.Id == vu);
            Ok(tr.NhanLoi.Contains("Kho 1") && tr.NhanLoi.Contains("sửa chưa đạt trả về: Kho 1") && !tr.NhanLoi.Contains("HTSP") && !tr.NhanLoi.Contains("Kho 2"),
               "Bảng theo vụ: Nhận lỗi \"Kho 1 · sửa chưa đạt trả về: Kho 1\" (không còn \"Kho 2\", không có HTSP)", tr.NhanLoi);
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(1))!;
            var st = HandoverLedgerService.ComputeSteps(inp);
            var kq = st.Single(x => x.Kind == "KQSUA");
            Ok(!st.Any(x => x.Kind == "GIAO" && x.RefId == tv.HandoverTxId) && kq.TraVeTxIds.SequenceEqual(new[] { tv.HandoverTxId }) && kq.Text.Contains("tự trả Kho")
               && kq.Row.G("HTSP").NhanLoi == 0 && kq.Row.G("KHO").ChoNhan == 1,
               "Dòng B: phiếu tự trả gộp vào dòng Kết quả sửa (không có dòng HTSP giữ ô Hàng lỗi), sau dòng đó Kho chờ nhận 1", kq.Text);
            var nb = st.Single(x => x.Kind == "NHAN" && x.Row.G("KHO").NhanLoiVe == 1 && x.Group == "KHO");
            Ok(nb.Text.Contains("không tính lỗi mới") && !nb.Text.Contains("bên nhận chọn") && !K.Explains["NhanLoiVe"].Terms.SelectMany(x => x.Docs).Any(d => d.Text.Contains("bên nhận chọn")),
               "Dòng B Kho nhận: ghi rõ hàng sửa trả về chưa đạt, không tính lỗi mới; không báo nhầm \"bên nhận chọn ô khác\" (ô chỉ định = Hàng lỗi)", nb.Text);
            Ok(K.Explains.TryGetValue("NhanLoiVe", out var ex) && ex.Result == 1 && H.Explains["NhanLoi"].Result == 0,
               "Hiện công thức: ô mới có diễn giải = 1; HTSP Nhận·Hàng lỗi diễn giải = 0 (có dòng trừ phần tự trả)");
            // xử lý tiếp chiếc đã quay về: Kho giữ ô Hàng lỗi như mọi hàng lỗi (6 nút)
            var posK = HandoverForm.FormPositions(c, "KHO", false).Single(p => p.Id == "TONLOI");
            Ok(posK.Have == 1 && HandoverForm.FormButtons(c, "KHO", posK, c.Cases[vu], null, false).Count(x => x.On) >= 3, "Kho thấy chiếc quay về ở dòng \"Đang giữ ở ô Hàng lỗi\" (1), xử lý tiếp bằng 6 nút");
        }

        Console.WriteLine("[E2] TH7 (mockup, Cách Y): Gia công làm xong NC 1, phát hiện 5 lỗi → gửi HTSP sửa → HTSP đạt 2 (giao Gia công), chưa đạt 3 (tự trả Gia công) — 1 lần nhập");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
            await VuHelp.Wts(db, "GC", "NC1", null, 20, 0, Gc, 0);
            var b0 = await Check(db, "E2 Gia công xong NC 1");
            var posG = HandoverForm.FormPositions(b0, "GC", false).Single(p => p.Id == "NC:NC2");
            var g = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho;
                                           r.ToGroup = "HTSP"; r.Qty = 5; r.CatId = 1; });
            var gtx = Tx(db); var vu = gtx.NgCaseId!.Value;
            var h = await svc.ReceiveAsync(gtx.HandoverTxId, 5, 0, null, Htsp);
            var b = await Check(db, "E2 HTSP giữ 5");
            Ok(g.Ok && h.Ok && posG.Have == 20 && gtx.ToCell == HandoverCell.Sua && b.G("HTSP").SuaGiu == 5, "TH7 bước 1: Gia công Gửi bộ phận khác sửa (HTSP) 5 — ô Hàng sửa, mã vụ mới", g.Error + "|" + h.Error);
            var f = await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua;
                                           r.Pass = 2; r.Fail = 3; r.SuaFailTra = true; r.SuaTo = "GC"; });
            var a = await Check(db, "E2 HTSP đạt 2 chưa đạt 3");
            var two = db.HandoverTransactions.List.OrderBy(t => t.HandoverTxId).TakeLast(2).ToList();
            Ok(f.Ok && two.Count(t => t.ToGroupCode == "GC") == 2 && two.Any(t => t.ToCell == HandoverCell.Xl && t.QtyIssued == 2) && two.Any(t => t.ToCell == HandoverCell.LoiVe && t.QtyIssued == 3),
               "Sẽ ghi: 2 phiếu HTSP → Gia công (đạt 2 ô Hàng lỗi đã xử lý, chưa đạt 3 ô Hàng sửa trả về chưa đạt)", f.Error + " | " + string.Join(" ; ", two.Select(t => $"{t.ToCell} {t.QtyIssued}")));
            foreach (var t in two) await svc.ReceiveAsync(t.HandoverTxId, t.ToCell == HandoverCell.Xl ? 2 : 0, t.ToCell == HandoverCell.Xl ? 0 : 3, null, Gc);
            var c = await Check(db, "E2 Gia công nhận cả 2 phiếu");
            var G = c.G("GC"); var H = c.G("HTSP");
            // đáp án = cột "Cách Y" bảng giao nhận mockup TH7 (chép tay)
            Ok(G.GiaoLoi == 5 && H.NhanSua == 5 && H.GiaoSua == 5 && G.NhanLoiXl == 2 && G.NhanLoi == 0 && G.NhanLoiVe == 3 && c.Cases[vu].SinhRa == 5 && G.LoiGiu == 3,
               "Mockup TH7 Cách Y: GC Giao·Hàng lỗi 5 · HTSP Nhận·Hàng sửa 5 · HTSP Giao·Hàng sửa 5 · GC Nhận·Hàng lỗi đã xử lý 2 · GC Nhận·Hàng lỗi 0 · GC Nhận·Hàng sửa trả về chưa đạt 3 · số lỗi GC 5 · GC giữ hàng lỗi 3",
               $"GC gl {G.GiaoLoi} nx {G.NhanLoiXl} nl {G.NhanLoi} nlv {G.NhanLoiVe} loi {G.LoiGiu} · HTSP ns {H.NhanSua} gs {H.GiaoSua} · sinh {c.Cases[vu].SinhRa}");
            Ok(H.NhanLoi == 0 && H.GiaoLoi == 0 && H.LoiGiu == 0 && H.Ton == 0, "3 chiếc không nằm lại HTSP: HTSP Nhận / Giao·Hàng lỗi 0, Tồn 0");
            var posL = HandoverForm.FormPositions(c, "GC", false).Single(p => p.Id == "TONLOI");
            Ok(posL.Have == 3 && c.Cases[vu].HeldAt("GC", HandoverCell.Loi) == 3, "Gia công thấy 3 chiếc ở dòng \"Đang giữ ở ô Hàng lỗi\", cùng vụ — xử lý tiếp như hàng lỗi", $"have {posL.Have}");
        }

        Console.WriteLine("[E3] Nhận phiếu tự trả: Workflow ON chỉ ô Hàng lỗi (đổi ô bị chặn, không ghi gì); OFF đổi ô được (có lý do), số vào ô đã chọn");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var tv = await Th6Fail(db, svc, vu);
            await Check(db, "E3 trước nhận");
            var sig0 = await FullSig(db);
            var x = await svc.ReceiveAsync(tv.HandoverTxId, 1, 0, null, 1, null, HandoverCell.Sua, "đổi thử");
            Ok(!x.Ok && x.Error == HandoverService.TraVeOnlyLoiMsg && await FullSig(db) == sig0, "ON: đổi sang ô Hàng sửa → chặn đúng lời, không ghi gì", x.Error);
            var plan = db.KhPlans.List.Single(); plan.ApplyWorkflow = false;
            var y = await svc.ReceiveAsync(tv.HandoverTxId, 1, 0, null, 1, null, HandoverCell.Sua, "Kho tự sửa");
            var c = await Check(db, "E3 OFF nhận ô Hàng sửa");
            Ok(y.Ok && c.G("KHO").NhanSua == 1 && c.G("KHO").NhanLoiVe == 0 && c.G("KHO").SuaGiu == 1 && c.Balanced,
               "OFF: nhận vào ô Hàng sửa (có lý do) → Kho Nhận·Hàng sửa 1, cột Hàng sửa trả về chưa đạt 0, PO cân", y.Error + $" ns {c.G("KHO").NhanSua} nlv {c.G("KHO").NhanLoiVe}");
        }

        Console.WriteLine("[E4] Huỷ: phiếu tự trả chưa nhận (HTSP huỷ) → chiếc về ô Hàng lỗi HTSP; huỷ lần nhận ở Kho → về chờ nhận; huỷ Kết quả sửa khi phiếu còn → K5 chặn");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var tv = await Th6Fail(db, svc, vu);
            await Check(db, "E4 trước");
            var sig0 = await FullSig(db);
            var vk = await svc.VoidRepairResultAsync(db.HandoverRepairResults.List.Last().HandoverRepairResultId, "thử huỷ", 1);
            Ok(!vk.Ok && await FullSig(db) == sig0, "ON: huỷ Kết quả sửa khi phiếu tự trả còn → chặn (K5), không ghi gì", vk.Error);
            var vi = await svc.VoidIssueAsync(tv.HandoverTxId, "thử huỷ phiếu", 1, new[] { "HTSP" });
            var a = await Check(db, "E4 huỷ phiếu tự trả");
            Ok(vi.Ok && a.G("HTSP").NhanLoi == 1 && a.G("HTSP").LoiGiu == 1 && a.G("KHO").ChoNhan == 0 && a.G("KHO").NhanLoiVe == 0 && a.Cases[vu].HeldAt("HTSP", HandoverCell.Loi) == 1,
               "Huỷ phiếu tự trả: chiếc về ô Hàng lỗi HTSP (Nhận·Hàng lỗi HTSP 1 — đúng chiếc HTSP đang giữ), Kho không chờ nhận", vi.Error + $" HTSP nl {a.G("HTSP").NhanLoi} loi {a.G("HTSP").LoiGiu}");

            WtsSvcHelp.Reset();
            var db2 = Seed(); var svc2 = new HandoverService(db2);
            var vu2 = await PreTh6(db2, svc2);
            var tv2 = await Th6Fail(db2, svc2, vu2);
            var b2 = await Check(db2, "E4b trước nhận");
            await svc2.ReceiveAsync(tv2.HandoverTxId, 0, 1, null, 1);
            await Check(db2, "E4b Kho nhận");
            var vr = await svc2.VoidReceiveAsync(db2.HandoverReceives.List.Last().ReceiveId, "nhận nhầm", 1, new[] { "KHO" });
            var c2 = await Check(db2, "E4b huỷ lần nhận");
            Ok(vr.Ok && c2.G("KHO").NhanLoiVe == 0 && c2.G("KHO").ChoNhan == 1 && Sig(c2) == Sig(b2), "Huỷ lần nhận ở Kho: cột Hàng sửa trả về chưa đạt về 0, Kho chờ nhận 1 (= như trước khi nhận)", vr.Error);
        }

        Console.WriteLine("[E5] Kho gửi sửa lần 2 chiếc đã quay về → HTSP sửa đạt → về Gia công (nơi báo lỗi): cột trả về giữ 1, Giao·Hàng lỗi Kho 2, vụ xong");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var tv = await Th6Fail(db, svc, vu);
            await svc.ReceiveAsync(tv.HandoverTxId, 0, 1, null, 1);
            await Check(db, "E5 Kho giữ lại 1");
            var g = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 1; });
            await svc.ReceiveAsync(Tx(db).HandoverTxId, 1, 0, null, Htsp);
            await Check(db, "E5 HTSP giữ lần 2");
            var f = await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Pass = 1; r.SuaFailTra = true; r.SuaTo = "GC"; });
            var px = Tx(db);
            var a = await Check(db, "E5 HTSP sửa đạt");
            var rc = await svc.ReceiveAsync(px.HandoverTxId, 1, 0, null, Gc);
            var c = await Check(db, "E5 Gia công nhận");
            Ok(g.Ok && f.Ok && rc.Ok && c.G("KHO").GiaoLoi == 2 && c.G("KHO").NhanLoiVe == 1 && c.G("KHO").NhanLoi == 1 && c.G("HTSP").NhanSua == 2 && c.G("HTSP").GiaoSua == 2
               && c.G("HTSP").NhanLoi == 0 && c.Cases[vu].HeldTotal == 0 && c.Cases[vu].Transit == 0 && c.G("GC").NhanLoiXl == 1,
               "Kho Giao·Hàng lỗi 2 (2 lần gửi), Nhận·Hàng lỗi 1 + trả về 1; HTSP Nhận / Giao·Hàng sửa 2; vụ xong, Gia công Nhận·Hàng lỗi đã xử lý 1",
               $"{g.Error}|{f.Error}|{rc.Error} Kho gl {c.G("KHO").GiaoLoi} nl {c.G("KHO").NhanLoi} nlv {c.G("KHO").NhanLoiVe} HTSP ns {c.G("HTSP").NhanSua} gs {c.G("HTSP").GiaoSua}");
        }

        Console.WriteLine("[E6] Chứng từ cũ giữ nghĩa cũ: phiếu tự trả lập trước bản G-E (ô chỉ định LOI) vẫn đếm vào Nhận·Hàng lỗi; sổ cân cả 2 kiểu");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var tv = await Th6Fail(db, svc, vu);
            tv.ToCell = HandoverCell.Loi;   // giả phiếu ghi bằng bản G-A2 (97aa1bb)
            await svc.ReceiveAsync(tv.HandoverTxId, 0, 1, null, 1);
            var c = await Check(db, "E6 phiếu cũ");
            Ok(c.G("KHO").NhanLoi == 2 && c.G("KHO").NhanLoiVe == 0 && c.G("HTSP").NhanLoi == 1 && c.G("HTSP").GiaoLoi == 1 && c.G("KHO").LoiGiu == 1,
               "Phiếu cũ (LOI): Kho Nhận·Hàng lỗi 2, HTSP Nhận / Giao·Hàng lỗi 1 — như bản G-A2", $"Kho nl {c.G("KHO").NhanLoi} nlv {c.G("KHO").NhanLoiVe} HTSP nl {c.G("HTSP").NhanLoi} gl {c.G("HTSP").GiaoLoi}");
        }

        Console.WriteLine("[E7] Ô LOIV chỉ máy chủ tự ghi: lập phiếu / form chỉ định ô LOIV → từ chối, không ghi gì");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreTh6(db, svc);
            var b = await Check(db, "E7 trước");
            var sig0 = await FullSig(db);
            var i1 = await svc.IssueByCellAsync(1, "HTSP", "KHO", 1, Htsp, "SUA", false, HandoverCell.LoiVe, ngCaseId: vu);
            var i2 = await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho;
                                            r.ToGroup = "KHO"; r.ToCell = HandoverCell.LoiVe; r.Qty = 1; });
            var i3 = await svc.IssueByCellAsync(1, "KHO", "HTSP", 1, 1, "LOI", false, HandoverCell.LoiVe, ngCaseId: vu);
            Ok(!i1.Ok && !i2.Ok && !i3.Ok && await FullSig(db) == sig0, "3 đường gọi với ô LOIV đều bị từ chối, sổ không đổi", $"{i1.Error} | {i2.Error} | {i3.Error}");
        }

        Console.WriteLine("[E8] Nơi làm lỗi là khách: chiếc sửa chưa đạt tự trả NƠI GỬI (Kho) — cũng đếm ở cột Hàng sửa trả về chưa đạt");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            // Kho tự báo phôi lỗi (lỗi có sẵn — phôi khách) 2 → gửi HTSP sửa → HTSP chưa đạt 2
            var bl = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "PHOI"; r.Prev = true; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 2; r.CatId = 1; });
            var t1 = Tx(db); var vu = t1.NgCaseId ?? 0;
            await svc.ReceiveAsync(t1.HandoverTxId, 2, 0, null, Htsp);
            var b = await Check(db, "E8 HTSP giữ 2 phôi lỗi");
            var f = await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Fail = 2; r.SuaFailTra = true; });
            var tv = Tx(db);
            await Check(db, "E8 sửa chưa đạt");
            var k = await svc.ReceiveAsync(tv.HandoverTxId, 0, 2, null, 1);
            var c = await Check(db, "E8 Kho nhận");
            Ok(bl.Ok && f.Ok && k.Ok && tv.ToGroupCode == "KHO" && tv.ToCell == HandoverCell.LoiVe && c.G("KHO").NhanLoiVe == 2 && c.G("KHO").LoiGiu == 2 && c.G("HTSP").NhanLoi == 0,
               "Trả nơi gửi (Kho), ô LOIV; Kho cột trả về 2, giữ 2 phôi lỗi; HTSP Nhận·Hàng lỗi 0", $"{bl.Error}|{f.Error}|{k.Error} → {tv.ToGroupCode} {tv.ToCell} nlv {c.G("KHO").NhanLoiVe}");
        }

        Console.WriteLine("[E4c] Huỷ phần chưa nhận của phiếu tự trả (rà độc lập L6): Kho nhận 1 / 2 rồi HTSP huỷ phần còn lại → chiếc còn lại về ô Hàng lỗi HTSP");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var bl = await Form(svc, r => { r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "PHOI"; r.Prev = true; r.Action = HandoverForm.AGiao; r.Purpose = HandoverForm.PNho; r.ToGroup = "HTSP"; r.Qty = 2; r.CatId = 1; });
            var t1 = Tx(db); var vu = t1.NgCaseId ?? 0;
            await svc.ReceiveAsync(t1.HandoverTxId, 2, 0, null, Htsp);
            await Form(svc, r => { r.UserId = Htsp; r.Group = "HTSP"; r.Kind = HandoverForm.Loi; r.Pos = "TONSUA"; r.CaseId = vu; r.Action = HandoverForm.ASua; r.Fail = 2; r.SuaFailTra = true; });
            var tv = Tx(db);
            var k = await svc.ReceiveAsync(tv.HandoverTxId, 0, 1, null, 1);
            await Check(db, "E4c Kho nhận 1 / 2");
            var v = await svc.CloseRemainderAsync(tv.HandoverTxId, "chỉ trả 1", Htsp, new[] { "HTSP" });
            var c = await Check(db, "E4c huỷ phần chưa nhận");
            var tr = HandoverNgTrace.Build((await new HandoverLedgerService(db).GetInputForDetailAsync(1))!);
            Ok(bl.Ok && k.Ok && v.Ok && db.HandoverTransactions.List.Single(x => x.HandoverTxId == tv.HandoverTxId).QtyClosed == 1
               && c.G("HTSP").NhanLoi == 1 && c.G("HTSP").LoiGiu == 1 && c.G("HTSP").GiaoLoi == 0 && c.G("KHO").NhanLoiVe == 1 && c.G("KHO").LoiGiu == 1
               && c.Cases[vu].HeldAt("HTSP", HandoverCell.Loi) == 1 && c.Cases[vu].Transit == 0 && tr.Problems.Count == 0,
               "HTSP Nhận·Hàng lỗi 1 (chiếc còn lại về ô Hàng lỗi HTSP), Giao·Hàng lỗi 0; Kho Hàng sửa trả về chưa đạt 1; vụ không đang chuyển; khung lỗi khớp",
               $"{bl.Error}|{k.Error}|{v.Error} HTSP nl {c.G("HTSP").NhanLoi} loi {c.G("HTSP").LoiGiu} gl {c.G("HTSP").GiaoLoi} · Kho nlv {c.G("KHO").NhanLoiVe} · {string.Join(" | ", tr.Problems)}");
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
