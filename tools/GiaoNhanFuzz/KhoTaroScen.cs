// [KHO-TARO-THANG][KTT-PHOILOI][KTT-CHI-TARO 2026-11-12] Kịch bản `ktt`: NC đầu Gia công là máy loại trừ có Taro → Kho giao THẲNG Taro.
//   Nguồn: KHÁCH 10/11 (hỏi lại 3.3 chọn B) + 12/11 file Word (a = A: Taro phát hiện phôi lỗi khi đang làm trả thẳng Kho, nơi gây lỗi Khách hàng;
//   b = A: NC đầu Taro thì Kho chỉ giao Taro); HÙNG 12/11 14:00 OK kế hoạch + 3 câu B (OFF Kho vẫn giao Gia công; phôi lỗi chỉ trả Kho;
//   NG lúc Taro nhận phôi ghi nơi gây lỗi Kho như cũ). Gọi HÀM THẬT: Part Master → KH + snapshot → Kho → Giao nhận (form + phiếu) → WTS → quét.
//   Đáp án số tính tay theo luật (cân Kho −n / Gia công "Nhận vào" +n −n / Taro làm hộ +n / Gia công NC2 +n) — không đọc lại công thức sổ.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using QuetSo;

public static class KhoTaroScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    const int U = ChuoiHelp.Admin;   // Kho (ADMIN)
    const int Gc = 2, Ht = 3;        // nhóm Xưởng (đủ quyền 5 nhóm)
    const string VAO = HandoverLedgerService.VaoCode;

    /// <summary>Part: NC01 = máy loại trừ H04 (Taro "9X" ở NC "1" — PM-LOAITRU-TARO), NC02, NC03 thường; HTSP có Bavia B1 (việc của HTSP); KCS K1.</summary>
    static async Task<(AppDbContext db, int det)> MkPo(string name, decimal qty, bool workflow = true, bool legacy = false, int exclAt = 0)
    {
        var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
        var pid = await ChuoiHelp.BuildPartAsync(db, name, new ChuoiHelp.Shape { Gc = 3, ExclAt = exclAt, DongDang = false, DuPhong = false, Taro = 0, Bavia = 1, Rua = 0, Kcs = 1, Pkg = 0, Prep = false, TaroTaskCode = true });
        var ids = legacy ? await ChuoiHelp.CreateKhLegacyAsync(db, pid, name, new[] { qty }, "PO-" + name, workflow)
                         : await ChuoiHelp.CreateKhAsync(db, pid, name, new[] { qty }, "PO-" + name, workflow);
        var k = await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(ids[0], DateTime.Now, (int)qty, "OVL", "A1", null, null, null, "nhan phoi", U);
        if (!k.Success) throw new Exception("Kho nhận phôi lỗi: " + k.Error);
        WtsSvcHelp.Reset();
        return (db, ids[0]);
    }

    static bool Clean(LedgerRow r) => r.Balanced && HandoverLedgerService.NegativeCells(r).Count == 0 && HandoverLedgerService.WhereIs(r).Balanced
                                      && r.Groups.Values.All(g => !g.Warnings.Any(w => w.Contains("báo lại để kiểm")));
    static string Neg(LedgerRow r) => string.Join("; ", HandoverLedgerService.NegativeCells(r).Values.Select(v => $"{v.Text}={v.Value}"))
                                      + " | " + string.Join("; ", r.Groups.Values.SelectMany(g => g.Warnings));
    static LedgerStepLine? Ln(LedgerRow r, string g, string code) => r.G(g).Steps.FirstOrDefault(s => s.Code == code);
    static string Sh(LedgerRow r) => $"Kho giao {r.G("KHO").GiaoMoi} phôi còn {r.G("KHO").ConGiao} lỗi {r.G("KHO").LoiGiu} chờ nhận {r.G("KHO").ChoNhan}"
        + $" · GC nhận mới {r.G("GC").NhanMoi} giao theo NC {r.G("GC").GiaoLamHo} chưa GC {r.G("GC").ChuaLam} lỗi {r.G("GC").LoiGiu}"
        + $" · HTSP nhận theo NC {r.G("HTSP").NhanLamHo} tồn theo NC {r.G("HTSP").LamHoGiu} chờ nhận {r.G("HTSP").ChoNhan} chưa làm {r.G("HTSP").ChuaLam}"
        + (HandoverLedgerService.KhoTaroRun(r) is { } k ? $" · đoạn @VAO vào {k.In} xong {k.Done} NG {k.Ng} trả {k.Ret} trả Kho {k.TraKho} chưa làm {k.Chua}" : " · (không đoạn @VAO)");

    public static async Task<int> Run()
    {
        Console.WriteLine("[K1] Kho giao thẳng Taro 10 (phiếu giao theo NC do Kho lập, NC nguồn \"Nhận vào\", ô Hàng mới): Kho −10, HTSP Chờ nhận 10, Gia công chưa đổi");
        var (db, det) = await MkPo("P-KT1", 10);
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        async Task<LedgerRow> Row() => (await led.GetLedgerForDetailAsync(det))!;
        long LastTx() => db.HandoverTransactions.List.Last().HandoverTxId;
        var r0 = await Row();
        Ok(HandoverLedgerService.KhoTaroRun(r0) is { Ncs.Count: 1 } kr0 && kr0.Ncs[0] == "NC01" && kr0.NextNc == "NC02" && FormHelp.KhoTaroPo(db, det),
           "PO có đoạn Taro đầu chuỗi: NC01 (H04) → trả về NC02 (sổ + đáp án độc lập đọc snapshot)", Sh(r0));
        Ok(HandoverForm.NextGroup("KHO", r0) == "HTSP" && HandoverForm.KhoTaroText(r0).Contains("NC01") && HandoverForm.KhoTaroText(r0).Contains("H04"),
           "Form: nhóm nhận mặc định của Kho = HTSP, chữ \"Taro — làm NC01 (máy loại trừ H04)\"", HandoverForm.KhoTaroText(r0));
        var k1 = await svc.IssueSpecialAsync(det, "KHO", "HTSP", 10, U, HandoverPurpose.HelpSend, VAO, null, null);
        var t1 = db.HandoverTransactions.List.Last();
        var r1 = await Row();
        Ok(k1.Ok && t1.Purpose == HandoverPurpose.HelpSend && t1.FromGroupCode == "KHO" && t1.FromNC == VAO && t1.FromCell == HandoverCell.Moi && t1.ToNC == null && t1.ToCell == null,
           "Phiếu: HELP_SEND · Kho → HTSP · FromNC @VAO · FromCell MOI · không NC / ô nhận (ghi chú không bắt buộc)", k1.Error);
        Ok(r1.G("KHO").GiaoMoi == 10 && r1.G("KHO").ConGiao == 0 && r1.G("HTSP").ChoNhan == 10 && r1.G("HTSP").Ton == 10 && r1.G("GC").NhanMoi == 0 && r1.G("GC").Ton == 0 && Clean(r1),
           "Kho Giao Hàng mới 10 · phôi còn 0 · HTSP Chờ nhận 10 (Tồn 10) · Gia công 0 · cân", Sh(r1) + " " + Neg(r1));
        var k1b = await svc.IssueSpecialAsync(det, "KHO", "HTSP", 1, U, HandoverPurpose.HelpSend, VAO, null, null);
        Ok(!k1b.Ok && k1b.Error.Contains("tối đa 0"), "Giao thêm 1 (phôi còn 0) → chặn trần", k1b.Error);
        var k1c = await svc.IssueSpecialAsync(det, "KHO", "KCS", 1, U, HandoverPurpose.HelpSend, "NC01", null, null);
        var k1d = await svc.IssueSpecialAsync(det, "KHO", "HTSP", 1, U, HandoverPurpose.HelpSend, "NC01", null, null);
        Ok(!k1c.Ok && k1c.Error.Contains("nhóm nhận là HTSP") && !k1d.Ok && k1d.Error.Contains("NC nguồn phải là"),
           "Kho giao thẳng sai: nhóm nhận KCS (NC nguồn NC01) → báo \"nhóm nhận là HTSP\" trước (fuzz KTT seed 7 PO18) · HTSP NC nguồn NC01 → báo NC nguồn", k1c.Error + " | " + k1d.Error);

        Console.WriteLine("[K2] Taro nhận 9 tốt + 1 NG: Gia công Nhận Hàng mới +10 và Giao theo NC từ \"Nhận vào\" +10 (chờ 0); HTSP Tồn theo NC 9; NG tự trả THẲNG Kho");
        var rc = await svc.ReceiveAsync(t1.HandoverTxId, 9, 1, null, Ht, null, null, null, 1);
        var r2 = await Row();
        var v2 = Ln(r2, "GC", VAO);
        var back = db.HandoverTransactions.List.Last();
        var vu2 = db.NgCases.List.Last();
        Ok(rc.Ok && r2.G("GC").NhanMoi == 10 && r2.G("GC").GiaoLamHo == 10 && r2.G("GC").ChuaLam == 0 && v2 != null && v2.In == 10 && v2.HelpOut == 10 && v2.Waiting == 0,
           "Gia công: Nhận Hàng mới 10 · Giao theo NC 10 · dòng \"Nhận vào\" vào 10 giao 10 chờ 0 · Chưa GC 0", rc.Error + " · " + Sh(r2));
        Ok(r2.G("HTSP").NhanLamHo == 10 && r2.G("HTSP").LamHoGiu == 9 && r2.G("HTSP").ChoNhan == 0 && r2.G("HTSP").ChuaLam == 0 && HandoverLedgerService.KhoTaroRun(r2)!.In == 9
           && Ln(r2, "HTSP", "9X")!.Waiting == 9,
           "HTSP: Nhận theo NC 10 · trả NG 1 → Tồn theo NC 9 · Chưa làm của HTSP 0 (K4) · đoạn @VAO vào 9 · Taro 9X chờ 9", Sh(r2));
        Ok(back.FromGroupCode == "HTSP" && back.ToGroupCode == "KHO" && back.Purpose == HandoverPurpose.NgReturn && back.SourceReceiveId != null && r2.G("KHO").ChoNhan == 1,
           "NG lúc nhận: phiếu trả tự lập HTSP → Kho 1 (Kho Chờ nhận 1)", $"#{back.HandoverTxId} {back.FromGroupCode}→{back.ToGroupCode} {back.Purpose}");
        Ok(vu2.DetectGroupCode == "HTSP" && vu2.CauseGroupCode == "KHO" && !r2.Cases[vu2.NgCaseId].IsLamHo,
           "Vụ NG lúc nhận: phát hiện HTSP, nơi gây lỗi Kho (giữ như Gia công nhận phôi — câu B3) — KHÔNG phải vụ làm hộ (Kho sửa / trả khách / huỷ)", $"{vu2.DetectGroupCode}/{vu2.CauseGroupCode} lamho={r2.Cases[vu2.NgCaseId].IsLamHo}");
        Ok(HandoverForm.CaseNextNc(r2, vu2.NgCaseId, "KHO") == null && FormHelp.ExpectNextNc(db, det, vu2.NgCaseId, "KHO") == null && HandoverForm.RepairTarget(r2, vu2.NgCaseId, "GC") == null,
           "Vụ NG lúc nhận (phôi chưa qua NC nào): sửa đạt về Gia công như hàng mới (không chờ NC02 — sổ = đáp án)",
           $"sổ {HandoverForm.CaseNextNc(r2, vu2.NgCaseId, "KHO")} đáp án {FormHelp.ExpectNextNc(db, det, vu2.NgCaseId, "KHO")} đích {HandoverForm.RepairTarget(r2, vu2.NgCaseId, "GC")}");
        var ks2 = (await svc.GetKhoIssueSummaryAsync(new[] { det })).SingleOrDefault();
        Ok(ks2 != null && ks2.TotalConfirmed == 9 && ks2.TotalPending == 0 && ks2.FirstIssuedAt == t1.IssuedAt,
           "Trang Kho (GetKhoIssueSummaryAsync): phiếu Kho giao thẳng Taro tính là phôi đã giao — đã giao 9 (OK), chờ 0, ngày giao = phiếu (rà độc lập Gói 18 Trung 1)",
           ks2 == null ? "không có dòng" : $"{ks2.TotalConfirmed}/{ks2.TotalPending}/{ks2.FirstIssuedAt}");
        var kb = await svc.ReceiveAsync(back.HandoverTxId, 0, 1, null, U);
        var r2b = await Row();
        var wh = HandoverLedgerService.WhereIs(r2b);
        Ok(kb.Ok && r2b.G("KHO").LoiGiu == 1 && r2b.G("KHO").ChoNhan == 0 && Clean(r2b) && wh.Items.Any(i => i.Place == "HTSP" && i.Kind == "THEO_NC" && i.Qty == 9 && i.Hint.Contains("Kho giao thẳng Taro"))
           && wh.Items.Any(i => i.Place == "KHO" && i.Kind == "LOI" && i.Qty == 1),
           "Kho nhận phiếu trả → Kho Tồn hàng lỗi 1; Đang ở đâu: HTSP hàng giao theo NC 9 (chữ có \"Kho giao thẳng Taro\"), Kho hàng lỗi 1; tổng = phôi", kb.Error + " · " + string.Join(", ", wh.Items.Select(i => $"{i.Place}/{i.Kind}={i.Qty}")));

        Console.WriteLine("[K3] (a) Taro phát hiện phôi lỗi khi đang làm → trả THẲNG Kho ô Hàng lỗi, nơi gây lỗi Khách hàng; không đụng Chưa làm / NG đầu của HTSP");
        var fp = HandoverForm.FormPositions(r2b, "HTSP", false).FirstOrDefault(p => p.Id == "LHKHO");
        Ok(fp != null && fp.Have == 9 && fp.TaroPhoi, "Form HTSP · Hàng lỗi: dòng \"Hàng giao theo NC chưa làm xong Taro NC01 (NC đầu)\" có 9", fp == null ? "không có dòng" : fp.Label + " " + fp.Have);
        FormGiaoRequest Ra(decimal q, string? to = "KHO", int? cat = 2, bool prev = true, string act = HandoverForm.AGiao, string? cause = "KHACH")
            => new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = Ht, Kind = HandoverForm.Loi, Pos = "LHKHO", Prev = prev, Cause = cause, Action = act, ToGroup = to, Qty = q, CatId = cat };
        int n0 = db.HandoverTransactions.List.Count;
        var a1 = await svc.SaveFormAsync(Ra(10));
        var a2 = await svc.SaveFormAsync(Ra(2, to: "GC"));
        var a3 = await svc.SaveFormAsync(Ra(2, cat: null));
        var a4 = await svc.SaveFormAsync(Ra(2, prev: false));
        var a5 = await svc.SaveFormAsync(Ra(2, act: HandoverForm.AHuy));
        var a6 = await svc.SaveFormAsync(Ra(2, cause: "GC"));
        var a7 = await svc.IssueByCellAsync(det, "HTSP", "GC", 2, Ht, "LOI", true, "LOI", VAO, null, null, null, 2);
        Ok(!a1.Ok && a1.Error.Contains("tối đa 9") && !a2.Ok && a2.Error.Contains("chỉ trả về Kho") && !a3.Ok && !a4.Ok && a4.Error.Contains("tích") && !a5.Ok && a5.Error.Contains("chỉ trả về Kho")
           && !a6.Ok && a6.Error.Contains("Khách hàng") && !a7.Ok && a7.Error.Contains("chỉ trả về Kho") && db.HandoverTransactions.List.Count == n0,
           "Chặn: vượt 9 · trả Gia công (form + phiếu) · thiếu loại lỗi · không tích Lỗi công đoạn trước · Huỷ · nơi gây lỗi khác Khách hàng — không ghi gì",
           string.Join(" | ", new[] { a1, a2, a3, a4, a5, a6, a7 }.Select(x => x.Error)));
        var a8 = await svc.SaveFormAsync(Ra(2));
        var ta = db.HandoverTransactions.List.Last(); var vua = db.NgCases.List.Last();
        var r3 = await Row();
        Ok(a8.Ok && ta.Purpose == HandoverPurpose.NgReturn && ta.FromGroupCode == "HTSP" && ta.ToGroupCode == "KHO" && ta.FromNC == VAO && ta.FromCell == HandoverCell.Loi && ta.ToCell == HandoverCell.Loi
           && ta.NgCaseId == vua.NgCaseId && vua.CauseGroupCode == "KHACH" && vua.DetectGroupCode == "HTSP" && vua.OriginKind == NgOrigin.TraLoi,
           "1 phiếu Hàng lỗi công đoạn trước HTSP → Kho (FromNC @VAO, ô Hàng lỗi), vụ mới: phát hiện HTSP, nơi gây lỗi Khách hàng", a8.Error + $" · vụ {vua.DetectGroupCode}/{vua.CauseGroupCode}");
        Ok(r3.G("HTSP").LamHoGiu == 7 && r3.G("HTSP").ChuaLam == 0 && r3.G("HTSP").NgDau == 0 && HandoverLedgerService.KhoTaroRun(r3)!.TraKho == 2 && Ln(r3, "HTSP", "9X")!.Waiting == 7
           && r3.G("KHO").ChoNhan == 2 && Clean(r3),
           "HTSP Tồn theo NC 7 · Chưa làm / NG đầu HTSP giữ 0 · đoạn trả Kho 2 · Taro 9X chờ 7 · Kho Chờ nhận 2 · cân", Sh(r3) + " " + Neg(r3));
        Ok(HandoverForm.CaseNextNc(r3, vua.NgCaseId, "KHO") == null && FormHelp.ExpectNextNc(db, det, vua.NgCaseId, "KHO") == null && HandoverForm.RepairTarget(r3, vua.NgCaseId, "GC") == null,
           "Vụ phôi lỗi (a): sửa đạt về Gia công như hàng mới (không NC cuối như vụ HTSP thường — sổ = đáp án)",
           $"sổ {HandoverForm.CaseNextNc(r3, vua.NgCaseId, "KHO")} đáp án {FormHelp.ExpectNextNc(db, det, vua.NgCaseId, "KHO")} đích {HandoverForm.RepairTarget(r3, vua.NgCaseId, "GC")}");
        var kb2 = await svc.ReceiveAsync(ta.HandoverTxId, 0, 2, null, U);
        var r3b = await Row();
        Ok(kb2.Ok && r3b.G("KHO").LoiGiu == 3 && r3b.Cases[vua.NgCaseId].HeldAt("KHO", HandoverCell.Loi) == 2 && Clean(r3b), "Kho nhận → Tồn hàng lỗi 3 (vụ phôi lỗi giữ 2 ở Kho)", kb2.Error);
        var trace = HandoverNgTrace.Build((await led.GetInputForDetailAsync(det))!);
        Ok(trace.Problems.Count == 0 && trace.Events.Any(e => e.CausedBy == "KHACH" && e.DetectedAt == "HTSP" && e.Text.Contains("phôi lỗi")),
           "Đường đi hàng lỗi: sự kiện phát sinh ở HTSP, nơi gây lỗi Khách hàng (chữ \"phôi lỗi\"), không vấn đề", string.Join(" | ", trace.Problems));

        Console.WriteLine("[K4] Taro làm NC01 (WTS 9X): OK 5, Taro làm hỏng 1 → vụ làm hộ (như cũ, trả Gia công); chưa làm Taro 1; phiếu (a) trần = 1");
        var w1 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "9X", 5, 1, Ht, 1);
        var r4 = await Row();
        var kr4 = HandoverLedgerService.KhoTaroRun(r4)!;
        Ok(w1.Ok && kr4.Done == 5 && kr4.Ng == 1 && Ln(r4, "HTSP", "9X")!.Waiting == 1 && r4.LamHoNgHeld("HTSP") == 1 && kr4.Held == 6 && kr4.Chua == 1,
           "Đoạn: xong 5, NG 1 · Taro 9X chờ 1 (= 9 − 2 trả Kho − 5 − 1) · chiếc làm hộ hỏng 1 · hàng đoạn còn giữ 6 (9 − 0 − 1 − 2)", w1.Err + " · " + Sh(r4));
        Ok(HandoverForm.FormPositions(r4, "HTSP", false).First(p => p.Id == "LHKHO").Have == 1, "Form dòng phôi lỗi còn 1 (min chưa làm 1, còn giữ 6)");
        var w2 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "9X", 2, 0, Ht);
        Ok(!w2.Ok, "WTS 9X thêm 2 (chờ 1) → chặn", w2.Err);

        Console.WriteLine("[K5] HTSP trả Gia công NC02 (≤ OK bộ Taro − đã trả = 5) + trả chiếc làm hộ hỏng về Gia công ô Hàng lỗi");
        var h1 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 6, Ht, HandoverPurpose.HelpReturn, null, "NC02", "tra ve");
        var h2 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, Ht, HandoverPurpose.HelpReturn, null, "NC02", "tra ve");
        var hr = await svc.ReceiveAsync(LastTx(), 5, 0, null, Gc);
        var r5 = await Row();
        Ok(!h1.Ok && h1.Error.Contains("tối đa 5") && h2.Ok && hr.Ok && Ln(r5, "GC", "NC02")!.In == 5 && Ln(r5, "GC", "NC02")!.Waiting == 5 && r5.G("HTSP").LamHoGiu == 2 && Clean(r5),
           "Trả 6 bị chặn (tối đa 5) · trả 5 → Gia công NC02 vào 5 · HTSP còn 2 (1 chưa làm + 1 hỏng)", h1.Error + " · " + Sh(r5));
        var lv = r5.CasesHeldAt("HTSP", HandoverCell.LamHo).Single();
        var lf = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = Ht, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = lv.Id, Action = HandoverForm.AGiao, Qty = 1 });
        var lr = await svc.ReceiveAsync(LastTx(), 0, 1, null, Gc);
        var r5b = await Row();
        Ok(lf.Ok && lr.Ok && r5b.G("GC").LoiGiu == 1 && r5b.G("HTSP").LamHoGiu == 1 && Clean(r5b)
           && r5b.G("KHO").LoiGiu + Ln(r5b, "GC", "NC02")!.Waiting!.Value + r5b.G("GC").LoiGiu + r5b.G("HTSP").LamHoGiu == 10,
           "Chiếc Taro làm hỏng → Gia công ô Hàng lỗi (như cũ) · cân 10 = Kho lỗi 3 + NC02 5 + GC lỗi 1 + Taro còn 1", lf.Error + lr.Error + " · " + Sh(r5b));

        Console.WriteLine("[K6] [KTT-CHI-TARO] Workflow ON: Kho chỉ giao Taro — Kho → Gia công / HTSP Hàng mới bị chặn; hàng lỗi vẫn giao được");
        var kp2 = await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(det, DateTime.Now, 14, "OVL", "A2", null, null, null, "them phoi lo 2", U);   // sửa phôi nhận 10 → 14
        var r6 = await Row();
        Ok(kp2.Success && r6.G("KHO").ConGiao == 4, "Kho sửa phôi nhận 10 → 14 (thêm 4) → phôi còn 4", kp2.Error);
        int n6 = db.HandoverTransactions.List.Count;
        var b1 = await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "MOI", false, "MOI");
        var b2 = await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "MOI", false, "XL");
        var b3 = await svc.IssueByCellAsync(det, "KHO", "HTSP", 1, U, "MOI", false, "MOI");
        var b4 = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "KHO", UserId = U, Kind = HandoverForm.Tot, Pos = "PHOI", Action = HandoverForm.AGiao, ToGroup = "GC", Qty = 1 });
        var b5 = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "KHO", UserId = U, Kind = HandoverForm.Tot, Pos = "PHOI", Action = HandoverForm.AGiao, ToGroup = "HTSP", ToCell = HandoverCell.Moi, Qty = 1 });
        Ok(r6.G("KHO").ConGiao == 4 && new[] { b1, b2, b3, b4 }.All(x => !x.Ok && x.Error.Contains("Kho giao phôi thẳng cho Taro")) && !b5.Ok && b5.Error.Contains("không chọn ô nhận") && db.HandoverTransactions.List.Count == n6,
           "Kho → Gia công (ô Hàng mới / Hàng lỗi đã xử lý), Kho → HTSP Hàng mới, form Kho → Gia công: chặn \"Kho giao phôi thẳng cho Taro\"; form chọn ô nhận: chặn",
           string.Join(" | ", new[] { b1, b2, b3, b4, b5 }.Select(x => x.Error)));
        var vk = r6.CasesHeldAt("KHO", HandoverCell.Loi).First(c => c.Id == vu2.NgCaseId);
        var b6a = await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "LOI", false, "MOI", null, null, null, null, null, vk.Id);
        Ok(b6a.Ok || !b6a.Error.Contains("Kho giao phôi thẳng cho Taro"), "Kho giao hàng lỗi của vụ (ô Hàng lỗi) cho Gia công vào ô Hàng mới → không bị luật chỉ giao Taro chặn (chỉ chặn ô phôi — fuzz KTT seed 1)", b6a.Error);
        if (b6a.Ok) await svc.VoidIssueAsync(LastTx(), "huy thu", U, new[] { "KHO" });
        var b6 = await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "LOI", false, "SUA", null, null, null, null, null, vk.Id);
        Ok(b6.Ok, "Kho gửi hàng lỗi (vụ phôi lỗi) cho Gia công nhờ sửa (ô Hàng sửa) → vẫn được (không phải hàng như mới)", b6.Error);
        var vs = await svc.VoidIssueAsync(LastTx(), "huy thu", U, new[] { "KHO" });
        var b7 = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "KHO", UserId = U, Kind = HandoverForm.Tot, Pos = "PHOI", Action = HandoverForm.AGiao, ToGroup = "HTSP", Qty = 4 });
        var t7 = db.HandoverTransactions.List.Last();
        var r7 = await Row();
        Ok(vs.Ok && b7.Ok && t7.FromGroupCode == "KHO" && t7.Purpose == HandoverPurpose.HelpSend && t7.FromNC == VAO && r7.G("KHO").ConGiao == 0 && r7.G("HTSP").ChoNhan == 4 && Clean(r7),
           "Form Kho · phôi còn → HTSP = phiếu Kho giao thẳng Taro 4 (chờ HTSP nhận)", vs.Error + b7.Error);
        var rc7 = await svc.ReceiveAsync(t7.HandoverTxId, 4, 0, null, Ht);
        var r7b = await Row();
        Ok(rc7.Ok && HandoverLedgerService.KhoTaroRun(r7b)!.In == 13 && Ln(r7b, "HTSP", "9X")!.Waiting == 5 && r7b.G("GC").NhanMoi == 14 && Ln(r7b, "GC", VAO)!.Waiting == 0 && Clean(r7b),
           "Taro nhận 4 → đoạn vào 13 · 9X chờ 5 (13 − 2 − 5 − 1) · Gia công Nhận Hàng mới 14 · Nhận vào chờ 0", Sh(r7b));

        Console.WriteLine("[K7] Huỷ: lần nhận Taro đã dùng → K5 chặn + gợi ý gỡ; phiếu phôi lỗi chờ Kho nhận → huỷ được; phiếu Kho giao thẳng chờ nhận → huỷ được");
        var rec1 = db.HandoverReceives.List.First(x => x.HandoverTxId == t1.HandoverTxId && !x.IsVoided);
        var vr = await svc.VoidReceiveAsync(rec1.ReceiveId, "huy thu", Ht, new[] { "HTSP" });
        Ok(!vr.Ok && vr.Error.StartsWith("Không lưu") && vr.Error.Contains("WTS #") && Clean(await Row()), "Huỷ lần nhận Taro 10 (đã WTS / trả NC02) → K5 chặn, gợi ý gỡ có dòng WTS Taro", vr.Error.Split('\n')[0]);
        var a9 = await svc.SaveFormAsync(Ra(1));
        var t9 = db.HandoverTransactions.List.Last();
        var mid = await Row();
        var v9 = await svc.VoidIssueAsync(t9.HandoverTxId, "huy thu", Ht, new[] { "HTSP" });
        var r9 = await Row();
        Ok(a9.Ok && HandoverLedgerService.KhoTaroRun(mid)!.TraKho == 3 && v9.Ok && HandoverLedgerService.KhoTaroRun(r9)!.TraKho == 2 && Ln(r9, "HTSP", "9X")!.Waiting == 5 && Clean(r9),
           "Phiếu phôi lỗi 1 (trả Kho 3) rồi huỷ khi Kho chưa nhận → trả Kho về 2, 9X chờ 5", a9.Error + v9.Error);

        Console.WriteLine("[K8] Phôi khách gửi bù → Kho giao thẳng Taro như phôi");
        var cr = await svc.IssueSpecialAsync(det, "KHO", "KHACH", 2, U, HandoverPurpose.CustReturn, null, null, "tra khach", null, vua.NgCaseId);
        var bu = await svc.ReceiveReplacementAsync(det, vua.NgCaseId, 2, "bu", U);
        var r8 = await Row();
        var k8 = await svc.IssueSpecialAsync(det, "KHO", "HTSP", 2, U, HandoverPurpose.HelpSend, VAO, null, null);
        var t8 = LastTx();
        var v8 = await svc.VoidIssueAsync(t8, "huy thu", U, new[] { "KHO" });
        var r8b = await Row();
        var k8b = await svc.IssueSpecialAsync(det, "KHO", "HTSP", 2, U, HandoverPurpose.HelpSend, VAO, null, null);
        var rc8 = await svc.ReceiveAsync(LastTx(), 2, 0, null, Ht);
        var r8c = await Row();
        Ok(cr.Ok && bu.Ok && r8.G("KHO").ConGiao == 2 && k8.Ok && v8.Ok && r8b.G("KHO").ConGiao == 2 && k8b.Ok && rc8.Ok && HandoverLedgerService.KhoTaroRun(r8c)!.In == 15
           && r8c.Bu == 2 && Clean(r8c) && HandoverLedgerService.WhereIs(r8c).Balanced,
           "Trả khách 2 → khách gửi bù 2 (phôi còn 2) → giao thẳng Taro 2 · huỷ phiếu chờ nhận → phôi còn về 2 · giao lại + nhận → đoạn vào 15 · cân phôi + bù",
           cr.Error + bu.Error + k8.Error + v8.Error + k8b.Error + rc8.Error + " · " + Sh(r8c));

        Console.WriteLine("[K9] Dòng B + đường đi hàng lỗi + quét DB (chỉ đọc): đủ chữ, khớp, 0 phiếu lạ");
        var inp9 = (await led.GetInputForDetailAsync(det))!;
        var steps = HandoverLedgerService.ComputeSteps(inp9);
        Ok(steps.Any(s => s.Text.Contains("Kho giao thẳng Taro")) && steps.Any(s => s.Text.Contains("phôi Kho giao thẳng Taro")) && steps.Any(s => s.Text.Contains("Taro phát hiện phôi lỗi"))
           && ChuoiHelp.LedgerSig(steps.Last().Row) == ChuoiHelp.LedgerSig(await Row()),
           "Dòng B: \"Kho giao thẳng Taro …\", \"HTSP nhận … phôi Kho giao thẳng Taro\", \"(Taro phát hiện phôi lỗi …)\"; dòng B cuối = sổ");
        Ok(HandoverNgTrace.Build(inp9).Problems.Count == 0, "Đường đi hàng lỗi cả PO: không vấn đề", string.Join(" | ", HandoverNgTrace.Build(inp9).Problems));
        var q9 = await QuetCore.ScanAsync(db, led);
        Ok(!q9.Findings.Any(f => f.Kind == QuetCore.KhoTaroLa), "Quét: 0 phiếu Kho giao thẳng / trả phôi lỗi lạ", string.Join(" | ", q9.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));

        Console.WriteLine("[K10] PO cũ (NC01 máy loại trừ KHÔNG Taro) / máy loại trừ giữa chuỗi: không có đường Kho giao thẳng Taro; Kho → Gia công như cũ");
        {
            var (dl, dd) = await MkPo("P-KT10", 6, legacy: true);
            var sl = new HandoverService(dl); var ll = new HandoverLedgerService(dl);
            var rl = (await ll.GetLedgerForDetailAsync(dd))!;
            var x1 = await sl.IssueSpecialAsync(dd, "KHO", "HTSP", 1, U, HandoverPurpose.HelpSend, VAO, null, null);
            var x2 = await sl.IssueByCellAsync(dd, "KHO", "GC", 6, U, "MOI", false, "MOI");
            var xr = await sl.ReceiveAsync(dl.HandoverTransactions.List.Last().HandoverTxId, 6, 0, null, Gc);
            var x3 = await sl.IssueByCellAsync(dd, "HTSP", "KHO", 1, Ht, "LOI", true, "LOI", VAO, null, null, null, 2);
            var rl2 = (await ll.GetLedgerForDetailAsync(dd))!;
            Ok(HandoverLedgerService.KhoTaroRun(rl) == null && !FormHelp.KhoTaroPo(dl, dd) && HandoverForm.NextGroup("KHO", rl) == "GC" && !rl.G("HTSP").Steps.Any(s => s.Code == "9X")
               && !x1.Ok && x1.Error.Contains("chỉ dùng khi NC đầu") && x2.Ok && xr.Ok && Ln(rl2, "GC", VAO)!.Waiting == 6 && !x3.Ok && x3.Error.Contains("chỉ dùng khi NC đầu")
               && !HandoverForm.FormPositions(rl2, "HTSP", false).Any(p => p.Id == "LHKHO") && Clean(rl2),
               "PO cũ: Kho giao thẳng Taro bị chặn · Kho → Gia công vào \"Nhận vào\" 6 như cũ · Taro trả phôi lỗi bị chặn · form không có dòng phôi lỗi",
               x1.Error + " | " + x2.Error + " | " + x3.Error);
            var (dm, dmid) = await MkPo("P-KT10B", 6, exclAt: 1);
            var sm = new HandoverService(dm);
            var rm = (await new HandoverLedgerService(dm).GetLedgerForDetailAsync(dmid))!;
            var y1 = await sm.IssueSpecialAsync(dmid, "KHO", "HTSP", 1, U, HandoverPurpose.HelpSend, VAO, null, null);
            Ok(HandoverLedgerService.KhoTaroRun(rm) == null && rm.LamHoRuns.Any(x => x.HasTaro && x.FromNc == "NC01") && !y1.Ok && !FormHelp.KhoTaroPo(dm, dmid),
               "Máy loại trừ GIỮA chuỗi (Taro làm hộ NC02): Kho giao thẳng Taro bị chặn (đường làm hộ giữa chuỗi như cũ)", y1.Error);
            dl.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9001, KhPlanDetailId = dd, FromGroupCode = "KHO", ToGroupCode = "HTSP", QtyIssued = 1,
                Purpose = HandoverPurpose.HelpSend, FromNC = "NC02", Status = "PENDING", IssuedBy = U, IssuedAt = DateTime.Now, CreatedAt = DateTime.Now });
            dl.Fixup();
            var qx = await QuetCore.ScanAsync(dl, ll);
            var fx = qx.Findings.Where(f => f.Kind == QuetCore.KhoTaroLa).ToList();
            Ok(fx.Count == 1 && fx[0].Detail.Contains("không có đoạn Taro") && fx[0].Detail.Contains("NC02") && fx[0].Detail.Contains("ô giao"),
               "Quét: cài 1 phiếu Kho giao theo NC lạ (PO không có Taro đầu chuỗi, NC nguồn NC02, ô giao trống) → bắt đúng loại KHO_TARO_LA", string.Join(" | ", fx.Select(f => f.Detail)));
        }

        Console.WriteLine("[K11] Workflow OFF: Kho vẫn giao Gia công \"Nhận vào\" (câu B1) song song với giao thẳng Taro — cân, đoạn cộng cả 2 đường");
        {
            var (dw, dwd) = await MkPo("P-KT11", 8, workflow: false);
            var sw = new HandoverService(dw); var lw = new HandoverLedgerService(dw);
            var o1 = await sw.IssueByCellAsync(dwd, "KHO", "GC", 3, U, "MOI", false, "MOI");
            var o1r = await sw.ReceiveAsync(dw.HandoverTransactions.List.Last().HandoverTxId, 3, 0, null, Gc);
            var o2 = await sw.IssueSpecialAsync(dwd, "KHO", "HTSP", 5, U, HandoverPurpose.HelpSend, VAO, null, null);
            var o2r = await sw.ReceiveAsync(dw.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, Ht);
            var o3 = await sw.IssueSpecialAsync(dwd, "GC", "HTSP", 3, Gc, HandoverPurpose.HelpSend, VAO, null, "lam taro");
            var o3r = await sw.ReceiveAsync(dw.HandoverTransactions.List.Last().HandoverTxId, 3, 0, null, Ht);
            var rw = (await lw.GetLedgerForDetailAsync(dwd))!;
            Ok(o1.Ok && o1r.Ok && o2.Ok && o2r.Ok && o3.Ok && o3r.Ok && HandoverLedgerService.KhoTaroRun(rw)!.In == 8 && Ln(rw, "GC", VAO)!.In == 8 && Ln(rw, "GC", VAO)!.HelpOut == 8
               && rw.G("GC").NhanMoi == 8 && rw.G("HTSP").LamHoGiu == 8 && Clean(rw),
               "OFF: Kho → Gia công 3 + Kho giao thẳng Taro 5 + Gia công giao theo NC từ \"Nhận vào\" 3 → đoạn vào 8, Nhận vào vào 8 giao 8, HTSP giữ 8, cân",
               o1.Error + o2.Error + o3.Error + " · " + Sh(rw));
        }

        Console.WriteLine("[K12] Quyền máy chủ: người chỉ có quyền Sửa Gia công không lập được phiếu Kho giao thẳng Taro");
        {
            var (dq, dqd) = await MkPo("P-KT12", 3);
            var sq = new HandoverService(dq);
            var gcOnly = FuzzPerm.GcOnlyUser(dq);
            var z1 = await sq.IssueSpecialAsync(dqd, "KHO", "HTSP", 1, gcOnly, HandoverPurpose.HelpSend, VAO, null, null);
            Ok(!z1.Ok && dq.HandoverTransactions.List.Count == 0, "Người ngoài nhóm Kho → chặn, không ghi", z1.Error);
        }

        Console.WriteLine("[K13] [K6-NOTRUN] PO chỉ có phiếu Kho giao thẳng Taro (chưa WTS): vẫn làm mới quy trình được; quy trình mới mất Taro NC đầu → giữ quy trình cũ (rà độc lập Gói 18 Trung 2)");
        {
            var dk = new AppDbContext(); ChuoiHelp.SeedBase(dk, exclH04: true);
            var pk = await ChuoiHelp.BuildPartAsync(dk, "P-KT13", new ChuoiHelp.Shape { Gc = 3, ExclAt = 0, DongDang = false, DuPhong = false, Taro = 0, Bavia = 1, Rua = 0, Kcs = 1, Pkg = 0, Prep = false, TaroTaskCode = true });
            var ik = await ChuoiHelp.CreateKhAsync(dk, pk, "P-KT13", new[] { 10m }, "PO-KT13", true);
            await new KhoVatLieuService(dk, new UserService(null)).SaveKhoInputAsync(ik[0], DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            var sk = new HandoverService(dk); var khk = new KhPlanService(dk); var lk = new HandoverLedgerService(dk);
            var q1 = await sk.IssueSpecialAsync(ik[0], "KHO", "HTSP", 6, U, HandoverPurpose.HelpSend, VAO, null, null);
            var q2 = await sk.ReceiveAsync(dk.HandoverTransactions.List.Last().HandoverTxId, 6, 0, null, Ht);
            var bl = await khk.RouteRefreshBlockAsync(ik[0], pk);
            Ok(q1.Ok && q2.Ok && bl == null, "Kho giao thẳng Taro 6, Taro nhận đủ, chưa WTS → K6 không chặn (\"PO chưa chạy\" như Kho → GC)", q1.Error + q2.Error + bl);
            string? ek = null; try { await khk.RefreshRouteSnapshotAsync(ik[0], pk, 1, "cap nhat"); } catch (Exception ex) { ek = ex.Message; }
            var rk = (await lk.GetLedgerForDetailAsync(ik[0]))!;
            Ok(ek == null && HandoverLedgerService.KhoTaroRun(rk)?.In == 6 && rk.G("HTSP").LamHoGiu == 6 && Clean(rk), "Làm mới (quy trình y cũ): được, đoạn vào 6, HTSP giữ theo NC 6, cân", ek);
            var snap0 = ChuoiHelp.SnapPicture(dk, ik[0]).Sig;
            foreach (var st in dk.PartMachiningSteps.List.Where(x => x.PartId == pk && x.NC == "NC01")) st.MachineRegistered = "M1";   // NC01 thành máy thường → mất Taro NC đầu
            await dk.SaveChangesAsync();
            ek = null; try { await khk.RefreshRouteSnapshotAsync(ik[0], pk, 1, "cap nhat"); } catch (Exception ex) { ek = ex.Message; }
            Ok(ek != null && ek.Contains("Kho giao thẳng Taro") && ChuoiHelp.SnapPicture(dk, ik[0]).Sig == snap0, "Part Master đổi NC01 sang máy thường (mất Taro NC đầu) → giữ quy trình cũ, báo \"Kho giao thẳng Taro\"", ek);
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
