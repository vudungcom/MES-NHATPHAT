// [GN-FORM1 2026-11-05] Kịch bản cố định "form": form "Giao / xử lý hàng" 1 form 4 bước — gọi hàm thật HandoverService.SaveFormAsync
//   + sổ chung. Nguồn: KHÁCH 05/11 15:43 (K1 gộp 1 form), 15:39 (K2 Gia công chọn theo NC, sửa xong làm tiếp đúng NC kế),
//   HÙNG 16:43 (K3 chữ bước 2), KHÁCH 04/11 16:09 + HÙNG 17:35 (K4 gộp link Báo lỗi / Kết quả sửa / mục Khác; (a)–(d)).
//   Mỗi lần lưu: đáp án độc lập FormHelp (số chứng từ mới từng bảng, loại phiếu / ô, ô sổ đổi bao nhiêu); lưu không được → DB + sổ y nguyên.
//   Sau mỗi lần lưu: kiểm chung (cân PO, không ô âm, vụ cân, khung lỗi + dòng B + Đang ở đâu khớp).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class FormScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    const int Kho = 1, Gc = 2, Htsp = 3, Kcs = 4, Pkg = 5;

    /// <summary>PO 1: GC NC1 · NC2 · NC3 (mid: NC2 máy loại trừ H04; first: NC1 máy loại trừ), HTSP Taro T1 + Bavia B1 + Rửa R1 + 1V, KCS K1, Đóng gói P1.</summary>
    static AppDbContext Seed(string excl = "", bool workflow = true)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 6; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-FORM", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 30, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 30, NgayNhanPhoi = DateTime.Now, SoPO = "PO1", PartNo = "P" });
        for (int i = 1; i <= 3; i++)
            db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = i, KhPlanDetailId = 1, StepOrder = i, NC = "NC" + i,
                MachineRegistered = (excl == "mid" && i == 2) || (excl == "first" && i == 1) || (excl == "last" && i == 3) ? "H04" : "M" + i });   // [GC-HTSP-NC-HIEN] + "last"
        if (excl != "") db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 1, KhPlanDetailId = 1, SoMay = "H04" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 2, NC = "B1" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 3, NC = "1V" });
        db.KhPlanRouteSnapshotWashing.List.Add(new KhPlanRouteSnapshotWashing { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 4, NC = "R1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    static async Task<LedgerRow> Row(AppDbContext db) => (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
    static long LastTx(AppDbContext db) => db.HandoverTransactions.List.Max(t => t.HandoverTxId);

    static async Task<LedgerRow> Check(AppDbContext db, string buoc)
    {
        var led = new HandoverLedgerService(db);
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var bad = new List<string>();
        if (!row.Balanced) bad.Add("PO không cân");
        if (row.ApplyWorkflow)
        {
            var neg = HandoverLedgerService.NegativeCells(row);
            if (neg.Count > 0) bad.Add("ô âm: " + string.Join(", ", neg.Select(x => x.Value.Text + "=" + x.Value.Value)));
        }
        if (row.Warnings.Count > 0) bad.Add("cảnh báo: " + string.Join(" | ", row.Warnings));
        foreach (var c in row.Cases.Values) if (c.Lech != 0) bad.Add($"vụ {c.No} lệch {c.Lech}");
        var inp = (await led.GetInputForDetailAsync(1))!;
        var tr = HandoverNgTrace.Build(inp);
        if (tr.Problems.Count > 0) bad.Add("khung lỗi: " + string.Join(" | ", tr.Problems));
        var steps = HandoverLedgerService.ComputeSteps(inp);
        if (steps.Count > 0 && ChuoiHelp.LedgerSig(steps.Last().Row) != ChuoiHelp.LedgerSig(row)) bad.Add("dòng B cuối ≠ sổ");
        var w = HandoverLedgerService.WhereIs(row);
        if (!w.Balanced) bad.Add($"Đang ở đâu {w.Total} ≠ phôi {w.Phoi}");
        Ok(bad.Count == 0, $"[chung] {buoc}: cân, không ô âm, vụ cân, khung lỗi + dòng B + Đang ở đâu khớp", string.Join("\n", bad));
        return row;
    }

    static FormGiaoRequest Req(string g, int uid, string kind, string pos, string act, decimal qty = 0, string? to = null, string? purpose = null,
                               bool prev = false, string? cause = null, long? caseId = null, decimal pass = 0, decimal fail = 0, string? ncNext = null,
                               string? ncReason = null, int? cat = 1, string? toCell = null, string? toNc = null, string? notes = null)
        => new FormGiaoRequest { KhPlanDetailId = 1, Group = g, UserId = uid, Kind = kind, Pos = pos, Action = act, Qty = qty, ToGroup = to, Purpose = purpose,
                                 Prev = prev, Cause = cause, CaseId = caseId, Pass = pass, Fail = fail, NcNext = ncNext, NcReason = ncReason,
                                 CatId = kind == HandoverForm.Loi && pos is not ("TONLOI" or "TONSUA") ? cat : null, ToCell = toCell, ToNC = toNc, Notes = notes };

    /// <summary>Lưu form + kiểm đáp án độc lập. expectOk=false: phải bị chặn, DB + sổ y nguyên; errHas: lời báo phải chứa.</summary>
    static async Task<(bool Ok, string Error)> Save(AppDbContext db, FormGiaoRequest r, string name, bool expectOk = true, string? errHas = null)
    {
        var svc = new HandoverService(db);
        var pre = await Row(db);
        var preCells = FormHelp.Cells(pre); var preDocs = FormHelp.Docs(db, 1); var preSig = ChuoiHelp.LedgerSig(pre);
        int tx0 = db.HandoverTransactions.List.Count, bl0 = db.HandoverNgReports.List.Count, kq0 = db.HandoverRepairResults.List.Count, vu0 = db.NgCases.List.Count;
        var exp = FormHelp.Build(pre, r);
        var caseNc = FormHelp.CaseNcFor(db, r);
        var res = await svc.SaveFormAsync(r);
        var post = await Row(db);
        if (!expectOk)
        {
            Ok(!res.Ok && (errHas == null || res.Error.Contains(errHas)), name + " → bị chặn" + (errHas == null ? "" : $" (\"{errHas}\")"), res.Error);
            Ok(FormHelp.Docs(db, 1) == preDocs && ChuoiHelp.LedgerSig(post) == preSig, name + " → không ghi gì (1 giao dịch)", $"{preDocs} → {FormHelp.Docs(db, 1)}");
            return res;
        }
        Ok(res.Ok, name, res.Error);
        if (!res.Ok) return res;
        var dTx = db.HandoverTransactions.List.Count - tx0; var dBl = db.HandoverNgReports.List.Count - bl0;
        var dKq = db.HandoverRepairResults.List.Count - kq0; var dVu = db.NgCases.List.Count - vu0;
        Ok(dTx == exp.Tx && dBl == exp.Bl && dKq == exp.Kq && dVu == exp.Vu,
           name + $" → đúng chứng từ (phiếu {exp.Tx}, báo lỗi {exp.Bl}, Kết quả sửa {exp.Kq}, vụ mới {exp.Vu})", $"phiếu {dTx} báo lỗi {dBl} KQ {dKq} vụ {dVu}");
        if (exp.Tx == 1 && dTx == 1)
        {
            var t = db.HandoverTransactions.List.Last();
            Ok((exp.Purpose == null || t.Purpose == exp.Purpose) && (exp.FromCell == null || t.FromCell == exp.FromCell) && (exp.ToCell == null || t.ToCell == exp.ToCell),
               name + $" → phiếu {exp.Purpose} ô {exp.FromCell}→{exp.ToCell}", $"{t.Purpose} {t.FromCell}→{t.ToCell}");
            if (exp.Bl == 1 && dBl == 1)
                Ok(t.NgCaseId == db.HandoverNgReports.List.Last().NgCaseId && t.NgCaseId != null, name + " → chứng từ kèm mang đúng vụ vừa sinh");
        }
        if (exp.Kq == 1 && dKq == 1 && exp.Bl == 1)
            Ok(db.HandoverRepairResults.List.Last().NgCaseId == db.HandoverNgReports.List.Last().NgCaseId, name + " → Kết quả sửa mang đúng vụ vừa sinh");
        var bad = FormHelp.CheckCells(preCells, FormHelp.Cells(post), exp);
        Ok(bad == null, name + " → ô sổ đổi đúng đáp án", bad);
        var tr = FormHelp.CheckTrace(db, exp, r.Group, caseNc);
        Ok(tr == null, name + " → truy vết đúng (NC phiếu, nguồn / nơi gây lỗi, vụ)", tr);
        await Check(db, name);
        return res;
    }

    /// <summary>[GC-LOI-TRUOC-NCDAU 2026-11-10] Bật / tắt Workflow của PO 1 (dựng dữ liệu cũ mà nay Workflow ON chặn).</summary>
    static void WorkflowSet(AppDbContext db, bool on) => db.KhPlans.List.First(p => p.KhPlanId == 1).ApplyWorkflow = on;

    static List<FormPos> P(LedgerRow r, string g, bool good) => HandoverForm.FormPositions(r, g, good);
    static FormPos? Pos(LedgerRow r, string g, bool good, string id) => P(r, g, good).FirstOrDefault(p => p.Id == id);

    public static async Task<int> Run()
    {
        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[F1] K3 chữ bước 2 + số từng dòng = ô sổ (Gia công theo \"đã làm xong NC nào\")");
        var db = Seed(); var svc = new HandoverService(db);
        {
            var r0 = await Row(db);
            Ok(Pos(r0, "KHO", true, "PHOI")?.Label == "Phôi còn (chưa giao)" && Pos(r0, "KHO", true, "PHOI")!.Have == 30, "Kho hàng tốt: \"Phôi còn (chưa giao)\" có 30");
            await Save(db, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 25, "GC"), "Kho giao 25 phôi cho Gia công (Hàng mới), giữ 5");
            await svc.ReceiveAsync(LastTx(db), 25, 0, null, Gc);
            await VuHelp.Wts(db, "GC", "NC1", null, 20, 0, Gc, 0);
            await VuHelp.Wts(db, "GC", "NC2", null, 12, 0, Gc, 0);
            await VuHelp.Wts(db, "GC", "NC3", null, 6, 0, Gc, 0);
            var r = await Check(db, "F1 WTS");
            var lo = P(r, "GC", false);
            string L(string id) => lo.FirstOrDefault(p => p.Id == id)?.Label ?? "?";
            decimal H(string id) => lo.FirstOrDefault(p => p.Id == id)?.Have ?? -1;
            Ok(L("NC:NC1") == "Vừa nhận, chưa làm NC1" && H("NC:NC1") == 5, "GC lỗi: \"Vừa nhận, chưa làm NC1\" có 5", $"{L("NC:NC1")} {H("NC:NC1")}");
            Ok(L("NC:NC2") == "Đã làm xong NC1 · chưa làm NC2" && H("NC:NC2") == 8, "GC lỗi: \"Đã làm xong NC1 · chưa làm NC2\" có 8", $"{L("NC:NC2")} {H("NC:NC2")}");
            Ok(L("NC:NC3") == "Đã làm xong NC2 · chưa làm NC3" && H("NC:NC3") == 6, "GC lỗi: \"Đã làm xong NC2 · chưa làm NC3\" có 6", $"{L("NC:NC3")} {H("NC:NC3")}");
            Ok(L("DA") == "Đã làm xong NC3 (NC cuối) · chưa giao" && H("DA") == 6, "GC lỗi: \"Đã làm xong NC3 (NC cuối) · chưa giao\" có 6", $"{L("DA")} {H("DA")}");
            Ok(L("TONLOI") == HandoverForm.TonLoiText && L("TONSUA") == HandoverForm.TonSuaText && H("TONLOI") == 0, "GC lỗi: Tồn hàng lỗi / Tồn hàng sửa (0)");
            foreach (var st in r.G("GC").Steps.Where(s => !s.IsExcluded))
                Ok(H("NC:" + st.Code) == Math.Min(st.Waiting ?? 0, r.G("GC").ChuaLam), $"Số dòng NC {st.Code} = chờ NC trong sổ ({st.Waiting})");
            var go = P(r, "GC", true);
            Ok(go.Select(p => p.Id).SequenceEqual(new[] { "HELP:NC1", "HELP:NC2", "DA" }) && go[0].Label == "Đã làm xong NC1 · chưa làm NC2" && go[2].Have == 6,
               "GC hàng tốt: giao theo NC từ NC1 / NC2 (làm hộ) + \"Đã làm xong NC3 (NC cuối) · chưa giao\"", string.Join(" | ", go.Select(p => p.Id + "=" + p.Label + ":" + p.Have)));
            await Save(db, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 4, "HTSP"), "GC giao 4 cho HTSP (Hàng mới), giữ 2");
            await svc.ReceiveAsync(LastTx(db), 4, 0, null, Htsp);
            await VuHelp.Wts(db, "TARO", null, "T1", 2, 0, Htsp, 0);
            await VuHelp.Wts(db, "BAVIA", null, "B1", 3, 0, Htsp, 0);
            var r2 = await Check(db, "F1 HTSP");
            var hl = P(r2, "HTSP", false);
            Ok(hl[0].Label == "Chưa làm xong (gồm cả đang làm dở)" && hl[1].Label == "Đã làm xong · chưa giao", "HTSP: \"Chưa làm xong (gồm cả đang làm dở)\" / \"Đã làm xong · chưa giao\"");
            Ok(hl[0].Sub == "còn Taro T1: 2 · Bavia B1: 1 · Rửa R1: 4" && hl[0].Have == 4, "HTSP dòng nhỏ \"còn Taro T1: 2 · Bavia B1: 1 · Rửa R1: 4\" (chỉ xem, N5), có 4", $"{hl[0].Sub} / {hl[0].Have}");
            var kl = P(r2, "KCS", false); var pl = P(r2, "PKG", false);
            Ok(kl[0].Label == "Chưa kiểm xong" && kl[1].Label == "Đã kiểm xong · chưa giao" && pl[0].Label == "Chưa đóng gói xong" && pl[1].Label == "Đã đóng gói · chưa giao",
               "KCS \"Chưa kiểm xong / Đã kiểm xong · chưa giao\", Đóng gói \"Chưa đóng gói xong / Đã đóng gói · chưa giao\"");
            Ok(P(r2, "KHO", false)[0].Label == "Phôi còn (chưa giao)", "Kho lỗi: \"Phôi còn (chưa giao)\"");
        }

        Console.WriteLine("[F2] K1/K4 lỗi công đoạn trước, chiếc chưa làm, trả đúng nơi gây lỗi → 1 phiếu Hàng lỗi công đoạn trước (như cũ)");
        {
            // [GC-LOI-TRUOC-NCDAU 2026-11-10] đổi theo khách (vòng 1 hỏi tiếp câu 5a): Workflow ON chỉ hàng chưa gia công (chờ NC đầu) được tích
            //   "Lỗi công đoạn trước" — chờ NC2 / NC3 / đã xong NC cuối bị chặn, không ghi gì. Bản 9bd3973 cho lưu → SAI đúng ở đây.
            var r2a = await Row(db);
            Ok(Pos(r2a, "GC", false, "NC:NC1")!.PrevOk && !Pos(r2a, "GC", false, "NC:NC2")!.PrevOk && !Pos(r2a, "GC", false, "NC:NC3")!.PrevOk && !Pos(r2a, "GC", false, "DA")!.PrevOk,
               "[GC-LOI-TRUOC-NCDAU] form: chỉ dòng \"Vừa nhận, chưa làm NC1\" cho tích Lỗi công đoạn trước (NC2 / NC3 / đã xong NC cuối không)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.AGiao, 2, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2),
                       "[GC-LOI-TRUOC-NCDAU] GC: chờ NC2 (đã gia công NC1) tích lỗi do Kho → trả Kho", false, "chưa gia công");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "DA", HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2),
                       "[GC-LOI-TRUOC-NCDAU] GC: đã xong NC cuối tích lỗi do Kho → trả Kho", false, "chưa gia công");
            var bD = FormHelp.Docs(db, 1);
            var dNg = await svc.IssueByCellAsync(1, "GC", "KHO", 1, Gc, HandoverCell.Loi, true, HandoverCell.Loi, "NC2", null, null, null, 2);
            var dBl = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC3", 1, HandoverNgSource.DauVao, "KHO", "", null, Gc, Array.Empty<string>(), 2);
            var dBl2 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Da, null, 1, HandoverNgSource.DauVao, "KHO", "", null, Gc, Array.Empty<string>(), 2);
            Ok(!dNg.Ok && dNg.Error == HandoverForm.GcPrevMsg && !dBl.Ok && dBl.Error == HandoverForm.GcPrevMsg && !dBl2.Ok && dBl2.Error == HandoverForm.GcPrevMsg && FormHelp.Docs(db, 1) == bD,
               "[GC-LOI-TRUOC-NCDAU] máy chủ (ngoài form): phiếu Hàng lỗi công đoạn trước từ NC2 / báo lỗi đầu vào ở NC3 / đã xong NC cuối → chặn, không ghi", $"{dNg.Error} | {dBl.Error} | {dBl2.Error}");
            var dOk = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC3", 1, HandoverNgSource.DauRa, null, "", null, Gc, Array.Empty<string>(), 2);
            Ok(dOk.Ok, "[GC-LOI-TRUOC-NCDAU] bỏ tích (lỗi của Gia công) ở NC3: báo lỗi được", dOk.Error);
            await Check(db, "F2 lỗi đầu ra NC3");
            // Các bước cũ của F2 / F3 giữ nguyên (đường đi hàng phía sau dùng tiếp): dựng lúc Workflow OFF = dữ liệu nhập trước luật / OFF tự kiểm soát
            WorkflowSet(db, false);
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.AGiao, 2, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2),
                       "GC (Workflow OFF): 2 chiếc \"Đã làm xong NC1 · chưa làm NC2\" lỗi do Kho → trả Kho");
            var t = db.HandoverTransactions.List.Last(); var vu = db.NgCases.List.Last();
            Ok(t.Purpose == HandoverPurpose.NgReturn && t.FromNC == "NC2" && vu.OriginKind == NgOrigin.TraLoi && vu.DetectNC == "NC2" && vu.CauseGroupCode == "KHO" && vu.NgCategoryId == 2,
               "Phiếu Hàng lỗi công đoạn trước chốt NC2, vụ TRALOI · phát hiện NC2 · do Kho · đúng loại lỗi", $"{t.Purpose} {t.FromNC} {vu.OriginKind} {vu.DetectNC} {vu.CauseGroupCode}");
            var r = await Row(db);
            Ok(HandoverForm.CaseOriginText(r, r.Cases[vu.NgCaseId]).Contains("đã làm xong NC1 · chưa làm NC2"), "Chữ vụ: \"phát hiện ở Gia công · đã làm xong NC1 · chưa làm NC2 · do Kho\"",
               HandoverForm.CaseOriginText(r, r.Cases[vu.NgCaseId]));
            await svc.ReceiveAsync(LastTx(db), 0, 2, null, Kho);
            await Check(db, "F2 Kho nhận ô Hàng lỗi");
        }

        Console.WriteLine("[F3] Lỗi công đoạn trước nhưng giao nhóm khác nơi gây lỗi / chiếc đã làm xong → báo lỗi + phiếu kèm (2 chứng từ 1 lần lưu)");
        {
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC3", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PNho, prev: true, cause: "KHO", cat: 3),
                       "GC: 1 chiếc chưa làm NC3 lỗi do Kho → gửi HTSP nhờ sửa");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "DA", HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 3),
                       "GC: 1 chiếc đã làm xong NC cuối lỗi do Kho → trả Kho (báo lỗi + phiếu ô Hàng lỗi)");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Where(t => t.ToGroupCode == "HTSP" && t.Status == "PENDING").Max(t => t.HandoverTxId), 1, 0, null, Htsp);
            await svc.ReceiveAsync(LastTx(db), 0, 1, null, Kho);
            WorkflowSet(db, true);   // [GC-LOI-TRUOC-NCDAU] hết phần dựng lúc OFF
            await Check(db, "F3 nhận");
        }

        Console.WriteLine("[F4] K2 sửa tại nhóm: hàng sửa đạt làm tiếp đúng NC của vụ; chỉ lùi NC trước + lý do; NC sau bị chặn (máy chủ kiểm)");
        {
            var b = FormHelp.Docs(db, 1);
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.ASua, pass: 1, fail: 0, ncNext: "NC3", cat: 4), "GC lỗi chưa làm NC2, sửa đạt làm tiếp NC3 (bỏ qua NC2)", false, "không bỏ qua NC");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.ASua, pass: 1, fail: 0, ncNext: "NC1", cat: 4), "Làm lại từ NC1 không lý do", false, "lý do");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.ASua, pass: 1, fail: 0, ncNext: "NC1", ncReason: "ab", cat: 4), "Làm lại từ NC1 lý do 2 ký tự", false, "lý do");
            Ok(FormHelp.Docs(db, 1) == b, "3 lần bị chặn: báo lỗi của lần đó cũng không ghi (không ghi dở)");
            var ex = HandoverForm.NcOptions(await Row(db), "NC2");
            Ok(ex.Count == 2 && ex[0].Code == "NC2" && ex[0].Default && ex[1].Code == "NC1" && ex[1].Earlier, "Lựa chọn NC làm tiếp (vụ NC2): NC2 mặc định, NC1 làm lại — không có NC3",
               string.Join(" | ", ex.Select(o => o.Code + ":" + o.Label)));
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.ASua, pass: 2, fail: 1, cat: 4), "GC lỗi đầu ra 3 chiếc chưa làm NC2 → sửa đạt 2, không đạt 1 (NC mặc định)");
            var k = db.HandoverRepairResults.List.Last();
            Ok(k.ToNC == "NC2" && k.QtyPass == 2 && k.QtyFail == 0 && k.SourceCell == HandoverCell.Loi, "Kết quả sửa: đạt 2 làm tiếp NC2 (mặc định = NC của vụ), không đạt ở lại Tồn hàng lỗi", $"{k.ToNC} {k.QtyPass}/{k.QtyFail}");
            var w0 = (await Row(db)).G("GC").Steps.First(s => s.Code == "NC2").Waiting;
            await svc.ConfirmRepairAsync(k.HandoverRepairResultId, 2, 0, null, Kcs);
            var r = await Check(db, "F4 KCS xác nhận");
            Ok(r.G("GC").Steps.First(s => s.Code == "NC2").Waiting == w0 + 2, "KCS đạt → chờ NC2 +2 (làm tiếp đúng NC kế)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC2", HandoverForm.ASua, pass: 1, ncNext: "NC1", ncReason: "lam lai vat", cat: 4), "Làm lại từ NC1 có lý do");
            var k2 = db.HandoverRepairResults.List.Last();
            Ok(k2.ToNC == "NC1" && (k2.Notes ?? "").Contains("Làm lại từ NC1") && (k2.Notes ?? "").Contains("lam lai vat"), "Lưu NC1 + lý do vào Ghi chú (truy vết)", k2.Notes);
            var oldApi = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, db.HandoverNgReports.List.Last(b0 => b0.NC == "NC2").NgCaseId, 1, 0, "NC3", null, Gc);
            Ok(!oldApi.Ok && oldApi.Error.Contains("không bỏ qua NC"), "Hàm cũ CreateRepairResultAsync cũng kiểm luật NC (máy chủ, không tin trang)", oldApi.Error);
        }

        Console.WriteLine("[F5] (d) vụ phát hiện sau Gia công → làm lại NC cuối; vụ phôi Kho → như hàng mới; Tồn hàng lỗi / sửa theo vụ");
        {
            // HTSP phát hiện lỗi do GC trên 1 chiếc chưa làm → trả GC (1 phiếu); GC nhận ô Hàng lỗi
            await Save(db, Req("HTSP", Htsp, HandoverForm.Loi, "CHUA", HandoverForm.AGiao, 1, "GC", HandoverForm.PTra, prev: true, cause: "GC", cat: 5), "HTSP: 1 chiếc chưa làm lỗi do GC → trả GC");
            await svc.ReceiveAsync(LastTx(db), 0, 1, null, Gc);
            var vuH = db.NgCases.List.Last().NgCaseId;
            Ok(FormHelp.ExpectNextNc(db, 1, vuH) == "NC3" && HandoverForm.CaseNextNc(await Row(db), vuH) == "NC3", "Vụ phát hiện ở HTSP: NC làm tiếp mặc định = NC cuối NC3 (d)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, caseId: vuH, pass: 1, ncNext: HandoverService.FormNewNc), "Như hàng mới (làm lại từ đầu) không lý do", false, "lý do");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, caseId: vuH, pass: 1, fail: 1), "Tồn hàng lỗi: ghi không đạt", false, "chỉ ghi số đạt");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, caseId: vuH, pass: 1), "GC sửa tại nhóm 1 chiếc của vụ HTSP (mặc định NC3)");
            Ok(db.HandoverRepairResults.List.Last().ToNC == "NC3", "Kết quả sửa làm tiếp NC3");
            // Kho: phôi khách lỗi sẵn → gửi GC nhờ sửa (Kho chỉ có Gửi nhờ sửa); GC nhận ô Hàng sửa → sửa: như hàng mới
            await Save(db, Req("KHO", Kho, HandoverForm.Loi, "PHOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, prev: true, cat: 1), "Kho: 1 phôi lỗi có sẵn (phôi khách) → gửi GC nhờ sửa");
            var vk = db.NgCases.List.Last();
            Ok(vk.CauseGroupCode == "KHACH" && db.HandoverNgReports.List.Last().Origin == HandoverNgSource.DauVao, "(c) Kho tích \"Lỗi có sẵn (phôi khách)\": lỗi đầu vào do phôi khách");
            await svc.ReceiveAsync(LastTx(db), 1, 0, null, Gc);
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASua, caseId: vk.NgCaseId, pass: 1, ncNext: "NC2", ncReason: "xx x"), "Phôi Kho gửi sửa: làm tiếp NC2 (sau \"như hàng mới\")", false, "không bỏ qua NC");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASua, caseId: vk.NgCaseId, pass: 1), "GC sửa phôi Kho gửi: như hàng mới (mặc định)");
            Ok(db.HandoverRepairResults.List.Last().ToNC == null && db.HandoverRepairResults.List.Last().SourceCell == HandoverCell.Sua, "Kết quả sửa ô Hàng sửa, NC làm tiếp = như hàng mới");
        }

        Console.WriteLine("[F6] Chỉ báo lỗi giữ lại · Huỷ · Trả khách (lỗi mới và Tồn hàng lỗi)");
        {
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiu, 1, cat: 2), "GC: chỉ báo lỗi 1 chiếc, giữ lại");
            var vG = db.NgCases.List.Last().NgCaseId;
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AHuy, 1, cat: 2), "GC: lỗi mới → huỷ (báo lỗi + phiếu huỷ)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AKhach, 1, cat: 2), "GC: lỗi mới → trả khách");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.AHuy, 1, caseId: vG), "GC: Tồn hàng lỗi (vụ giữ lại) → huỷ");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.AHuy, 1, caseId: vG), "Huỷ thêm khi vụ đã hết", false, "tối đa");
        }

        Console.WriteLine("[F7] Tồn hàng sửa: trả lại nơi gửi (chưa sửa được → ô Hàng lỗi) / sửa xong trả nơi gửi (→ Hàng lỗi đã xử lý)");
        {
            await Save(db, Req("KHO", Kho, HandoverForm.Loi, "PHOI", HandoverForm.AGiao, 2, "GC", HandoverForm.PNho, cat: 1), "Kho (tự làm hỏng) gửi GC nhờ sửa 2");
            var vk = db.NgCases.List.Last();
            await svc.ReceiveAsync(LastTx(db), 2, 0, null, Gc);
            var rr = await Row(db);
            Ok(HandoverForm.CaseSender(rr.Cases[vk.NgCaseId], "GC") == "KHO", "Nơi gửi nhờ sửa = Kho (mặc định nhóm nhận khi trả lại)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ATraLai, 1, "KHO", caseId: vk.NgCaseId), "GC trả lại Kho 1 (chưa sửa được)");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASuaXong, 1, "KHO", caseId: vk.NgCaseId), "GC sửa xong trả Kho 1");
            await svc.ReceiveAsync(LastTx(db) - 1, 0, 1, null, Kho);
            await svc.ReceiveAsync(LastTx(db), 1, 0, null, Kho);
            await Check(db, "F7 Kho nhận");
        }

        Console.WriteLine("[F8] Lưu không được → không ghi gì: nhóm nhận trùng nhóm giao (sau bước báo lỗi), vượt trần, thiếu loại lỗi, quyền, dòng lạ");
        {
            var r = await Row(db);
            var have = Pos(r, "GC", false, "NC:NC1")!.Have;
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, cat: 2), "Lỗi mới giao cho chính Gia công", false, "không được trùng");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiu, have + 1, cat: 2), "Báo lỗi vượt số chờ NC1", false, "tối đa");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiu, 1, cat: null), "Thiếu loại lỗi", false);
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiu, 1, cat: 6), "Loại lỗi đã ngưng", false);
            await Save(db, Req("KHO", FuzzPerm.GcOnlyUser(db), HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 1, "GC"), "Người chỉ có quyền Gia công lập thay Kho", false, "quyền");
            await Save(db, Req("GC", Gc, HandoverForm.Tot, "NC:NC1", HandoverForm.AGiao, 1, "HTSP"), "Dòng không có trong bước 2 hàng tốt", false, "bước 2");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ATraLai, 1, "KHO", caseId: 0), "Việc không hợp lệ với dòng (Tồn hàng lỗi · Trả lại nơi gửi)", false, "bước 3");
            await Save(db, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 999, "HTSP"), "Hàng tốt vượt Đã làm chưa giao", false, "tối đa");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1.234m, "HTSP", HandoverForm.PNho, cat: 2), "Số lẻ 1.234 (đổi đáp án 06/11 [B3-SO-NGUYEN]: chỉ số nguyên)", false, "số nguyên");
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1.5m, "HTSP", HandoverForm.PNho, cat: 2), "Số lẻ 1.5 (trước 06/11 lưu được)", false, "số nguyên");
        }

        Console.WriteLine("[F9] Khoá PO + mọi lần ghi dưới khoá + tự thử lại khi deadlock (không ghi 2 lần)");
        {
            Microsoft.EntityFrameworkCore.DatabaseFacade.TrackPoLock = true;
            Microsoft.EntityFrameworkCore.DatabaseFacade.WriteNoPoLock = 0; Microsoft.EntityFrameworkCore.DatabaseFacade.WriteNoPoLockWhere.Clear();
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            await Save(db, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PNho, cat: 2), "Lỗi mới gửi HTSP sửa (2 chứng từ)");
            Ok(Microsoft.EntityFrameworkCore.DatabaseFacade.WriteNoPoLock == 0 && Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Count(x => x == "MES_PO_1") == 1,
               "Báo lỗi + phiếu ghi dưới 1 khoá MES_PO_1 (1 lần lấy khoá)",
               string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.WriteNoPoLockWhere) + " | " + string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            int n = 0;
            AppDbContext.OnSave = () => ++n == 2 ? new FakeSqlException(1205) : null;   // bước 2 (phiếu) bị chọn làm nạn nhân deadlock lần đầu
            var tx0 = db.HandoverTransactions.List.Count; var bl0 = db.HandoverNgReports.List.Count;
            var dl = await new HandoverService(db).SaveFormAsync(Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PNho, cat: 2));
            AppDbContext.OnSave = null;
            Ok(dl.Ok && db.HandoverTransactions.List.Count == tx0 + 1 && db.HandoverNgReports.List.Count == bl0 + 1, "Deadlock giữa 2 chứng từ: tự thử lại, ghi đúng 1 báo lỗi + 1 phiếu", dl.Error);
            Microsoft.EntityFrameworkCore.DatabaseFacade.TrackPoLock = false;
            await Check(db, "F9");
        }

        Console.WriteLine("[F10] (a) Giao theo NC làm hộ + trả hàng làm hộ về Gia công (NC nhận bắt buộc), PO có máy loại trừ giữa chuỗi");
        {
            var d2 = Seed("mid"); var s2 = new HandoverService(d2);
            await Save(d2, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s2.ReceiveAsync(d2.HandoverTransactions.List.Max(t => t.HandoverTxId), 10, 0, null, Gc);
            await VuHelp.Wts(d2, "GC", "NC1", null, 6, 0, Gc, 0);
            var r = await Row(d2);
            var go = P(r, "GC", true);
            Ok(go.Any(p => p.Id == "HELP:NC1" && p.Have == 6 && p.Label.Contains("chờ giao làm NC2 (máy loại trừ H04)")), "GC hàng tốt: \"Đã làm xong NC1 · chờ giao làm NC2 (máy loại trừ H04)\" có 6",
               string.Join(" | ", go.Select(p => p.Id + "=" + p.Label + ":" + p.Have)));
            await Save(d2, Req("GC", Gc, HandoverForm.Tot, "HELP:NC1", HandoverForm.AGiao, 4, "HTSP"), "Giao theo NC thiếu ghi chú", false, "ghi chú");
            await Save(d2, Req("GC", Gc, HandoverForm.Tot, "HELP:NC1", HandoverForm.AGiao, 4, "HTSP", notes: "lam ho H04"), "GC giao theo NC từ NC1 cho HTSP làm hộ 4");
            await s2.ReceiveAsync(d2.HandoverTransactions.List.Max(t => t.HandoverTxId), 4, 0, null, Htsp);
            var r2 = await Row(d2);
            Ok(Pos(r2, "HTSP", true, "LAMHO")?.Have == 4, "HTSP hàng tốt: \"Đang giữ hàng Gia công giao theo NC (làm hộ)\" có 4");
            await Save(d2, Req("HTSP", Htsp, HandoverForm.Tot, "LAMHO", HandoverForm.AGiao, 4, "GC"), "Trả về Gia công thiếu NC nhận", false, "K3");
            await Save(d2, Req("HTSP", Htsp, HandoverForm.Tot, "LAMHO", HandoverForm.AGiao, 4, "GC", toNc: "NC3"), "HTSP trả 4 về Gia công vào NC3");
            await s2.ReceiveAsync(d2.HandoverTransactions.List.Max(t => t.HandoverTxId), 4, 0, null, Gc);
            var r3 = await Check(d2, "F10 nhận về NC3");
            Ok(r3.G("GC").Steps.First(s => s.Code == "NC3").Waiting == 4, "Chờ NC3 = 4 (hàng làm hộ trả về)");
            Ok(P(r3, "GC", false).First(p => p.Id == "NC:NC3").Label == "Đã làm xong NC2 (máy loại trừ H04, làm hộ) · chưa làm NC3", "Chữ: \"Đã làm xong NC2 (máy loại trừ H04, làm hộ) · chưa làm NC3\"");
        }

        Console.WriteLine("[F11] N3-FIRST: NC đầu là máy loại trừ → dòng \"Nhận vào (chưa qua NC)\": lỗi ở đây sửa đạt = như hàng mới; hàng tốt giao theo NC từ đây");
        {
            var d3 = Seed("first"); var s3 = new HandoverService(d3);
            await Save(d3, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s3.ReceiveAsync(d3.HandoverTransactions.List.Max(t => t.HandoverTxId), 10, 0, null, Gc);
            var r = await Row(d3);
            var lo = P(r, "GC", false);
            Ok(lo[0].Id == "NC:@VAO" && lo[0].Label == HandoverLedgerService.VaoLabel && lo[0].Have == 10, "GC lỗi: \"Nhận vào (chưa qua NC)\" có 10", lo[0].Id + "=" + lo[0].Label + ":" + lo[0].Have);
            Ok(P(r, "GC", true).Any(p => p.Id == "HELP:@VAO" && p.Have == 10), "GC hàng tốt: giao theo NC từ \"Nhận vào (chưa qua NC)\" có 10");
            await Save(d3, Req("GC", Gc, HandoverForm.Loi, "NC:@VAO", HandoverForm.ASua, pass: 1, cat: 2), "Lỗi ở dòng Nhận vào → sửa đạt (mặc định như hàng mới)");
            Ok(d3.HandoverRepairResults.List.Last().ToNC == null && d3.NgCases.List.Last().DetectNC == HandoverLedgerService.VaoCode, "NC làm tiếp = như hàng mới, vụ chốt @VAO");
            await Save(d3, Req("GC", Gc, HandoverForm.Loi, "NC:@VAO", HandoverForm.ASua, pass: 1, ncNext: "NC2", ncReason: "abc", cat: 2), "Làm tiếp NC2 khi vụ ở Nhận vào", false, "không bỏ qua NC");
            await Save(d3, Req("GC", Gc, HandoverForm.Loi, "NC:@VAO", HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2), "Lỗi do Kho ở Nhận vào → trả Kho (1 phiếu, chốt @VAO)");
            Ok(d3.HandoverTransactions.List.Last().FromNC == HandoverLedgerService.VaoCode, "Phiếu Hàng lỗi công đoạn trước chốt dòng @VAO");
        }

        Console.WriteLine("[F12] Workflow OFF: vẫn kiểm trần + quyền; Đóng gói → Kho hàng tốt = thành phẩm chờ Kho nhận");
        {
            var d4 = Seed(workflow: false); var s4 = new HandoverService(d4);
            await Save(d4, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 31, "GC"), "Workflow OFF: giao vượt phôi", false, "tối đa");
            await Save(d4, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 5, "PKG"), "Kho giao thẳng Đóng gói 5 (nhắc, không chặn)");
            await s4.ReceiveAsync(d4.HandoverTransactions.List.Max(t => t.HandoverTxId), 5, 0, null, Pkg);
            await VuHelp.Wts(d4, "PKG", null, "P1", 5, 0, Pkg, 0);
            await Save(d4, Req("PKG", Pkg, HandoverForm.Tot, "DA", HandoverForm.AGiao, 5, "KHO"), "Đóng gói giao Kho 5 thành phẩm");
            Ok((await Row(d4)).DangGiao == 5, "Thành phẩm chờ Kho nhận 5 (không vào phôi Kho)");
        }

        Console.WriteLine("[F13] Rà độc lập V1/V2/V3: NC làm tiếp theo vụ khi vụ sinh lúc nhận hàng làm hộ / nhận từ nhóm sau Gia công / hàng của vụ giao vào Gia công");
        {
            // V3 + V2 — PO không máy loại trừ
            var d5 = Seed(); var s5 = new HandoverService(d5);
            await Save(d5, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 20, "GC"), "Kho giao 20");
            await s5.ReceiveAsync(LastTx(d5), 20, 0, null, Gc);
            foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(d5, "GC", nc, null, 8, 0, Gc, 0);
            await Save(d5, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 6, "HTSP"), "GC giao 6 HTSP");
            await s5.ReceiveAsync(LastTx(d5), 6, 0, null, Htsp);
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Loi, "CHUA", HandoverForm.AGiu, 2, cat: 2), "HTSP báo lỗi 2 chiếc chưa làm xong, giữ lại");
            var vuA = d5.NgCases.List.Last();
            var r = await Row(d5);
            Ok(vuA.DetectGroupCode == "HTSP" && HandoverForm.CaseNextNc(r, vuA.NgCaseId) == "NC3" && FormHelp.ExpectNextNc(d5, 1, vuA.NgCaseId) == "NC3", "Vụ HTSP nhận lỗi: NC làm tiếp mặc định NC3 (NC cuối)");
            Ok(new[] { "KHO", "GC", "HTSP", "KCS", "PKG" }.All(h => HandoverForm.CaseNextNc(r, 0, h) == null && FormHelp.ExpectNextNc(d5, 1, 0, h) == null),
               "V2 + vòng 3 C1b + vòng 4 V1: hàng lỗi cũ chưa có mã vụ, nhóm nào giữ cũng → như hàng mới (chọn NC khác + lý do)");
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: vuA.NgCaseId, toCell: HandoverCell.Xl),
                       "V3: HTSP giao 1 chiếc của vụ vào Gia công ô Hàng lỗi đã xử lý (mặc định)");
            Ok(d5.HandoverTransactions.List.Last().ToNC == "NC3", "V3: phiếu mang NC3 theo vụ (trước: không NC → nhận vào như hàng mới)", d5.HandoverTransactions.List.Last().ToNC);
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: vuA.NgCaseId, toCell: HandoverCell.Xl, toNc: "NC1"),
                       "V3: lùi NC1 không lý do", false, "lý do");
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: vuA.NgCaseId, toCell: HandoverCell.Xl, toNc: "NC1", ncReason: "ren hong lam lai"),
                       "V3: lùi NC1 có lý do");
            var t5 = d5.HandoverTransactions.List.Last();
            Ok(t5.ToNC == "NC1" && (t5.Notes ?? "").Contains("ren hong lam lai"), "V3: phiếu mang NC1, lý do vào Ghi chú", t5.ToNC + " · " + t5.Notes);
            await s5.ReceiveAsync(LastTx(d5) - 1, 1, 0, null, Gc);
            await s5.ReceiveAsync(LastTx(d5), 1, 0, null, Gc);
            await Check(d5, "F13 V3 Gia công nhận");
            // V2: HTSP làm xong giao lại Gia công, Gia công đếm NG lúc nhận → vụ phát hiện ở Gia công không NC, do HTSP → NC cuối
            foreach (var (pg, c) in new[] { ("TARO", "T1"), ("BAVIA", "B1"), ("BAVIA", "1V"), ("WASHING", "R1") }) await VuHelp.Wts(d5, pg, null, c, 2, 0, Htsp, 0);
            // [GN-NGUOC-CHAN 2026-11-10] Workflow ON chặn Hàng mới giao ngược (đổi theo khách câu 5) → phiếu ngược dựng lúc OFF (dữ liệu cũ), nhận lúc ON
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Tot, "DA", HandoverForm.AGiao, 2, "GC"), "[GN-NGUOC-CHAN] HTSP giao Hàng tốt ngược lại Gia công (Workflow ON)", false, "giao ngược");
            WorkflowSet(d5, false);
            await Save(d5, Req("HTSP", Htsp, HandoverForm.Tot, "DA", HandoverForm.AGiao, 2, "GC"), "HTSP giao 2 lại Gia công (Workflow OFF — dữ liệu cũ)");
            WorkflowSet(d5, true);
            { var rr = await s5.ReceiveAsync(LastTx(d5), 0, 2, null, Gc, toCell: HandoverCell.Loi, toCellReason: "ba via sot", ngCategoryId: 2); Ok(rr.Ok, "Gia công nhận 2 vào ô Hàng lỗi", rr.Error); }
            var vuB = d5.NgCases.List.Last();
            r = await Row(d5);
            Ok(vuB.DetectGroupCode == "GC" && string.IsNullOrEmpty(vuB.DetectNC) && HandoverForm.CaseNextNc(r, vuB.NgCaseId) == "NC3" && FormHelp.ExpectNextNc(d5, 1, vuB.NgCaseId) == "NC3",
               "V2: vụ Gia công nhận lỗi từ HTSP (không NC) → mặc định NC3 (trước: như hàng mới)", $"{vuB.DetectGroupCode} {vuB.DetectNC} do {vuB.CauseGroupCode} → {HandoverForm.CaseNextNc(r, vuB.NgCaseId)}");
            await Save(d5, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, pass: 1, caseId: vuB.NgCaseId), "V2: Gia công sửa đạt 1 (mặc định)");
            Ok(d5.HandoverRepairResults.List.Last().ToNC == "NC3", "V2: Kết quả sửa làm tiếp NC3");

            // V1 — PO máy loại trừ giữa chuỗi: vụ sinh lúc HTSP nhận hàng Gia công giao theo NC từ NC1
            var d6 = Seed("mid"); var s6 = new HandoverService(d6);
            await Save(d6, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s6.ReceiveAsync(LastTx(d6), 10, 0, null, Gc);
            await VuHelp.Wts(d6, "GC", "NC1", null, 6, 0, Gc, 0);
            await Save(d6, Req("GC", Gc, HandoverForm.Tot, "HELP:NC1", HandoverForm.AGiao, 4, "HTSP", notes: "lam ho H04"), "GC giao theo NC từ NC1 cho HTSP làm hộ 4");
            { var rr = await s6.ReceiveAsync(LastTx(d6), 3, 1, "loi", Htsp, ngCategoryId: 2); Ok(rr.Ok, "nhận có NG", rr.Error); }
            var vuC = d6.NgCases.List.Last();
            var r6 = await Row(d6);
            Ok(r6.Cases[vuC.NgCaseId].HelpFromNc == "NC1", "V1: vụ ghi lại NC giao theo NC (NC1)");
            Ok(HandoverForm.CaseNextNc(r6, vuC.NgCaseId) == "NC1" && FormHelp.ExpectNextNc(d6, 1, vuC.NgCaseId) == "NC1",
               "V1: NC kế (NC2) là máy loại trừ → mặc định làm lại từ NC1, không nhảy NC3 (trước: NC3 bỏ qua NC2)", HandoverForm.CaseNextNc(r6, vuC.NgCaseId));
            await s6.ReceiveAsync(LastTx(d6), 0, 1, null, Gc);   // phiếu tự lập trả NG về Gia công
            await Check(d6, "F13 V1 Gia công nhận NG trả về");
            await Save(d6, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, pass: 1, caseId: vuC.NgCaseId, ncNext: "NC3", ncReason: "abc"),
                       "V1: sửa đạt làm tiếp NC3 (bỏ qua NC2 làm hộ)", false, "không bỏ qua NC");
            await Save(d6, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.ASua, pass: 1, caseId: vuC.NgCaseId), "V1: Gia công sửa đạt 1 (mặc định)");
            Ok(d6.HandoverRepairResults.List.Last().ToNC == "NC1", "V1: Kết quả sửa làm tiếp NC1", d6.HandoverRepairResults.List.Last().ToNC);
        }

        Console.WriteLine("[F14] K5 cuối form (lưới an toàn): đúng lúc lưu, 1 dòng WTS lọt ngoài khoá ăn mất hàng → K5 chặn, form không ghi gì");
        {
            var d7 = Seed(); var s7 = new HandoverService(d7);
            await Save(d7, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s7.ReceiveAsync(LastTx(d7), 10, 0, null, Gc);
            int tx0 = d7.HandoverTransactions.List.Count, bl0 = d7.HandoverNgReports.List.Count, vu0 = d7.NgCases.List.Count;
            int k = 0;
            AppDbContext.OnSave = () =>
            {
                if (++k == 1)
                {
                    d7.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = d7.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = Gc,
                        QtyDone = 1, QtyOk = 1, QtyNg = 0, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
                    d7.Fixup();
                }
                return null;
            };
            var k5 = await s7.SaveFormAsync(Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiu, 10, cat: 2));
            AppDbContext.OnSave = null;
            Ok(!k5.Ok && k5.Error.Contains("K5"), "Báo lỗi 10 chiếc chờ NC1 khi WTS NC1 lọt vào giữa: K5 chặn", k5.Error);
            Ok(d7.HandoverTransactions.List.Count == tx0 && d7.HandoverNgReports.List.Count == bl0 && d7.NgCases.List.Count == vu0, "K5 chặn → không ghi báo lỗi / vụ nào");
        }

        Console.WriteLine("[F15] Rà độc lập vòng 2: C1 phôi lỗi cũ (vụ 0) ở Kho giao Gia công → như hàng mới; V-a vụ Kho phát hiện ở hàng Gia công giao về → NC cuối");
        {
            var d8 = Seed(); var s8 = new HandoverService(d8);
            await Save(d8, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s8.ReceiveAsync(LastTx(d8), 10, 0, null, Gc);
            // báo phôi lỗi cũ không gắn vụ (dữ liệu trước bản VULOI) → Kho Tồn hàng lỗi có 2 chiếc vụ 0
            d8.HandoverNgReports.List.Add(new HandoverNgReport { HandoverNgReportId = d8.HandoverNgReports.List.Count + 1, KhPlanDetailId = 1, GroupCode = "KHO", SourceCell = HandoverNgSource.Phoi,
                Qty = 2, Origin = HandoverNgSource.DauVao, CauseGroupCode = "KHACH", NgReason = "phoi nut cu", CreatedBy = Kho, CreatedAt = DateTime.Now });
            d8.Fixup();
            var r8 = await Check(d8, "F15 phôi lỗi cũ");
            Ok(r8.CaseHeld(0, "KHO", HandoverCell.Loi) == 2, "Kho giữ 2 phôi lỗi cũ (vụ 0)", r8.CaseHeld(0, "KHO", HandoverCell.Loi).ToString());
            Ok(HandoverForm.CaseNextNc(r8, 0, "KHO") == null && HandoverForm.CaseNextNc(r8, 0, "GC") == null && HandoverForm.CaseNextNc(r8, 0, "KCS") == null,
               "C1/C1b/vòng 4 V1: vụ 0 ở Kho / Gia công / KCS → như hàng mới");
            await Save(d8, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: 0, toCell: HandoverCell.Moi),
                       "C1: Kho giao 1 phôi lỗi cũ vào Gia công ô Hàng mới (mặc định)");
            Ok(d8.HandoverTransactions.List.Last().ToNC == null, "C1: phiếu không NC (như hàng mới, chờ NC1) — trước: NC3 bỏ qua NC1/NC2", d8.HandoverTransactions.List.Last().ToNC);
            await Save(d8, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: 0, toCell: HandoverCell.Moi, toNc: "NC3"),
                       "C1: phôi lỗi cũ vào NC3 không lý do", false, "lý do");
            // C1b: phôi lỗi cũ Kho gửi Gia công nhờ sửa → Gia công sửa đạt: mặc định như hàng mới; chọn NC3 phải ghi lý do (không có mốc nên không chặn hẳn)
            await Save(d8, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: 0), "C1b: Kho gửi 1 phôi lỗi cũ cho Gia công sửa");
            await s8.ReceiveAsync(LastTx(d8), 1, 0, null, Gc);
            r8 = await Check(d8, "F15 C1b Gia công nhận hàng sửa vụ 0");
            Ok(r8.CaseHeld(0, "GC", HandoverCell.Sua) == 1, "C1b: Gia công giữ ô Hàng sửa 1 (vụ 0)", r8.CaseHeld(0, "GC", HandoverCell.Sua).ToString());
            await Save(d8, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASua, pass: 1, caseId: 0, ncNext: "NC3"), "C1b: sửa đạt vào NC3 không lý do", false, "lý do");
            Ok(HandoverForm.NcOptions(r8, null, free: true).Any(o => o.Code == "NC3" && o.Earlier) && !HandoverForm.NcOptions(r8, null).Any(o => o.Code == "NC3"),
               "C1b: trang cho chọn NC sau (ghi lý do) chỉ với hàng cũ chưa mã vụ");
            await Save(d8, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASua, pass: 1, caseId: 0), "C1b: Gia công sửa đạt 1 phôi lỗi cũ (mặc định)");
            Ok(d8.HandoverRepairResults.List.Last().ToNC == null, "C1b: làm tiếp như hàng mới (trước vòng 3: NC3 bỏ qua NC1/NC2)", d8.HandoverRepairResults.List.Last().ToNC);
            // V-a: Gia công làm xong NC1..NC3, giao Kho; Kho nhận vào ô Hàng lỗi → vụ phát hiện ở Kho do Gia công
            foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(d8, "GC", nc, null, 4, 0, Gc, 0);
            // [GN-NGUOC-CHAN 2026-11-10] Hàng mới Gia công → Kho = giao ngược: Workflow ON chặn; dựng lúc OFF (dữ liệu cũ), nhận lúc ON
            await Save(d8, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 3, "KHO"), "[GN-NGUOC-CHAN] GC giao Hàng tốt ngược về Kho (Workflow ON)", false, "giao ngược");
            WorkflowSet(d8, false);
            await Save(d8, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 3, "KHO"), "GC giao 3 về Kho (Workflow OFF — dữ liệu cũ)");
            WorkflowSet(d8, true);
            { var rr = await s8.ReceiveAsync(LastTx(d8), 0, 3, null, Kho, toCell: HandoverCell.Loi, toCellReason: "ren hong", ngCategoryId: 2); Ok(rr.Ok, "Kho nhận 3 vào ô Hàng lỗi", rr.Error); }
            var vuK = d8.NgCases.List.Last();
            r8 = await Row(d8);
            Ok(vuK.DetectGroupCode == "KHO" && vuK.CauseGroupCode == "GC" && HandoverForm.CaseNextNc(r8, vuK.NgCaseId) == "NC3" && FormHelp.ExpectNextNc(d8, 1, vuK.NgCaseId, "KHO") == "NC3",
               "V-a: vụ Kho phát hiện do Gia công → mặc định NC3 (trước: như hàng mới)", $"{vuK.DetectGroupCode} do {vuK.CauseGroupCode} → {HandoverForm.CaseNextNc(r8, vuK.NgCaseId)}");
            await Save(d8, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 2, "GC", HandoverForm.PNho, caseId: vuK.NgCaseId), "V-a: Kho gửi 2 Gia công nhờ sửa");
            await s8.ReceiveAsync(LastTx(d8), 2, 0, null, Gc);
            await Save(d8, Req("GC", Gc, HandoverForm.Loi, "TONSUA", HandoverForm.ASua, pass: 1, caseId: vuK.NgCaseId), "V-a: Gia công sửa đạt 1 (mặc định)");
            Ok(d8.HandoverRepairResults.List.Last().ToNC == "NC3", "V-a: Kết quả sửa làm tiếp NC3 (không phải làm lại từ NC1)", d8.HandoverRepairResults.List.Last().ToNC);
        }

        Console.WriteLine("[F16] [A7-VU-AM 06/11] Tồn hàng lỗi chọn mã vụ âm / không có trong PO → báo thẳng \"không thuộc PO này\", không lời K2, không ghi gì");
        {
            var d9 = Seed(); var s9 = new HandoverService(d9);
            await Save(d9, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s9.ReceiveAsync(LastTx(d9), 10, 0, null, Gc);
            var w9 = await VuHelp.Wts(d9, "GC", "NC1", null, 8, 2, Gc, 2);
            var vu9 = w9.NgCaseId ?? 0;
            Ok(vu9 > 0, "WTS NC1 NG 2 sinh vụ", vu9.ToString());
            foreach (var bad in new long[] { -1, -vu9, vu9 + 9999 })
                foreach (var act in new[] { HandoverForm.AGiao, HandoverForm.ASua, HandoverForm.AHuy })
                {
                    var rq = act == HandoverForm.ASua ? Req("GC", Gc, HandoverForm.Loi, "TONLOI", act, pass: 1, caseId: bad)
                           : Req("GC", Gc, HandoverForm.Loi, "TONLOI", act, 1, act == HandoverForm.AGiao ? "HTSP" : null, act == HandoverForm.AGiao ? HandoverForm.PNho : null, caseId: bad);
                    var res = await Save(d9, rq, $"vụ {bad} · {act}", false, "không thuộc PO này");
                    Ok(!res.Error.Contains("NC "), $"vụ {bad} · {act}: lời báo không nhắc NC làm tiếp (K2)", res.Error);
                }
            await Save(d9, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PNho, caseId: vu9), "Đúng vụ: GC gửi HTSP nhờ sửa 1");
            // Đường K2 (phiếu MANG VỤ vào Gia công ô Hàng lỗi đã xử lý): Kho giữ vụ ở Tồn hàng lỗi rồi giao Gia công — mã vụ sai trước báo lời NC làm tiếp
            await Save(d9, Req("GC", Gc, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, caseId: vu9), "GC trả Kho 1 chiếc của vụ (ô Hàng lỗi)");
            { var rk = await s9.ReceiveAsync(LastTx(d9), 0, 1, null, Kho); Ok(rk.Ok && (await Row(d9)).CaseHeld(vu9, "KHO", HandoverCell.Loi) == 1, "Kho nhận 1 vào ô Hàng lỗi (giữ vụ)", rk.Error); }
            foreach (var bad in new long[] { -1, vu9 + 9999 })
                foreach (var nc in new string?[] { null, "NC3" })
                {
                    var res = await Save(d9, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: bad, toCell: HandoverCell.Xl, toNc: nc),
                                         $"Kho → Gia công ô Hàng lỗi đã xử lý, vụ {bad}, NC {nc ?? "mặc định"}", false, "không thuộc PO này");
                    Ok(!res.Error.Contains("lý do") && !res.Error.Contains("NC làm tiếp"), $"vụ {bad} NC {nc ?? "mặc định"}: không báo lời K2", res.Error);
                }
            await Save(d9, Req("KHO", Kho, HandoverForm.Loi, "TONLOI", HandoverForm.AGiao, 1, "GC", HandoverForm.PNho, caseId: vu9, toCell: HandoverCell.Xl), "Đúng vụ: Kho giao Gia công ô Hàng lỗi đã xử lý (NC theo vụ)");
        }

        // [KHACH-GUI-LAI 06/11] Câu khách 06/11 (qua Hùng): Kho nhận phôi → phát hiện 1 chiếc lỗi bề mặt (phôi khách) → Trả khách; khách gửi lại
        //   1 chiếc thì làm thế nào. Cách dùng hiện có (không đổi code): trang Kho sửa "Số lượng thực nhận" + 1 (kèm lý do) → Phôi còn + 1 → giao tiếp
        //   như hàng tốt. Kiểm đường đó bằng hàm thật: sổ cân (Phôi = tồn + đang giao + … + trả khách), vụ lỗi cũ vẫn ghi "Trả khách 1".
        Console.WriteLine("[F17] [KHACH-GUI-LAI] Kho: phôi lỗi có sẵn → Trả khách 1; khách gửi lại 1 → Kho sửa SL thực nhận 30 → 31 → Phôi còn + 1, giao tiếp được, sổ cân");
        {
            var dk = Seed();
            await Save(dk, Req("KHO", Kho, HandoverForm.Loi, "PHOI", HandoverForm.AKhach, 1, prev: true, cat: 1), "Kho: 1 phôi lỗi có sẵn (phôi khách) → Trả khách");
            var r1 = await Check(dk, "sau trả khách");
            var vu = r1.Cases.Values.Single();
            Ok(r1.G("KHO").ConGiao == 29 && r1.TraKhach == 1 && vu.TraKhach == 1, "Sau trả khách: Kho Phôi còn 29, PO Trả khách 1, vụ lỗi ghi Trả khách 1", $"còn {r1.G("KHO").ConGiao} · trả khách {r1.TraKhach} · vụ {vu.No} trả khách {vu.TraKhach}");
            var kho = new KhoVatLieuService(dk, new UserService(null));
            var k = await kho.SaveKhoInputAsync(1, DateTime.Now, 31, null, null, null, null, null, "Khach gui lai 1 chiec thay chiec loi BM da tra", 1);
            dk.Fixup();
            var r2 = await Check(dk, "sau khi Kho sửa SL thực nhận 31");
            var vu2 = r2.Cases.Values.Single();
            Ok(k.Success && r2.G("KHO").ConGiao == 30 && r2.TraKhach == 1 && vu2.TraKhach == 1 && r2.Balanced,
               "Kho sửa SL thực nhận 30 → 31 (lý do khách gửi lại): Phôi còn 30, trả khách vẫn 1 (truy vết giữ), sổ cân", $"{k.Error} · còn {r2.G("KHO").ConGiao} · trả khách {r2.TraKhach} · cân={r2.Balanced}");
            await Save(dk, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 30, "GC"), "Kho giao đủ 30 phôi (gồm chiếc khách gửi lại) cho Gia công");
        }

        // ─────────────────────────────────────────────────────────────────
        // [GÓI 13 2026-11-10] Khách 10/11 (vòng 1 + hỏi lại + hỏi tiếp): KHO-NC-DAU · GN-NGUOC-CHAN · GC-LOI-TRUOC-NCDAU · GC-HTSP-NC-HIEN.
        //   Bản 9bd3973 phải SAI ở F18 / F19 / F20 / F21 (lưu được hoặc thiếu chữ); Workflow OFF lưu được như cũ (N7).
        Console.WriteLine("[F18] [KHO-NC-DAU] Kho giao phôi cho Gia công luôn vào NC đầu (form + máy chủ + nhận đổi ô); Workflow OFF như cũ");
        {
            var dk = Seed(); var sk = new HandoverService(dk);
            await Save(dk, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 3, "GC", toNc: "NC2"), "[KHO-NC-DAU] form: Kho giao Gia công chọn NC2", false, "NC đầu");
            var b0 = FormHelp.Docs(dk, 1);
            var i3 = await sk.IssueByCellAsync(1, "KHO", "GC", 3, Kho, "MOI", false, "MOI", null, "NC3");
            var iX = await sk.IssueByCellAsync(1, "KHO", "GC", 3, Kho, "MOI", false, HandoverCell.Xl, null, "NC2");
            Ok(!i3.Ok && i3.Error.Contains("NC đầu") && !iX.Ok && iX.Error.Contains("NC đầu") && FormHelp.Docs(dk, 1) == b0,
               "[KHO-NC-DAU] máy chủ: phiếu Kho → Gia công vào NC3 / ô Hàng lỗi đã xử lý vào NC2 bị chặn, không ghi", i3.Error + " | " + iX.Error);
            await Save(dk, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 3, "GC", toNc: "NC1"), "Kho giao Gia công chọn đúng NC1 (NC đầu)");
            await sk.ReceiveAsync(LastTx(dk), 3, 0, null, Gc);
            await Save(dk, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 2, "GC"), "Kho giao Gia công không chọn NC (mặc định NC đầu)");
            await sk.ReceiveAsync(LastTx(dk), 2, 0, null, Gc);
            var rk = await Check(dk, "F18 Kho → GC NC đầu");
            Ok(rk.G("GC").Steps.First(x => x.Code == "NC1").Waiting == 5 && rk.G("GC").Steps.Where(x => x.Code != "NC1").All(x => (x.In ?? 0) == 0), "Chờ NC1 = 5, NC2 / NC3 không nhận gì");
            // nhận đổi ô: Kho gửi nhờ sửa phôi lỗi (ô Hàng sửa) → Gia công nhận đổi sang Hàng mới, chọn NC2 → chặn; mặc định / NC1 được
            await Save(dk, Req("KHO", Kho, HandoverForm.Loi, "PHOI", HandoverForm.AGiao, 2, "GC", HandoverForm.PNho, prev: true, cat: 1), "Kho: 2 phôi lỗi có sẵn → gửi Gia công nhờ sửa");
            var tS = LastTx(dk);
            var rv = await sk.ReceiveAsync(tS, 1, 0, null, Gc, null, HandoverCell.Moi, "phoi dung duoc", null, "NC2", "da lam ngoai");
            Ok(!rv.Ok && rv.Error.Contains("NC đầu"), "[KHO-NC-DAU] Gia công nhận đổi ô phiếu Kho sang Hàng mới vào NC2 → chặn", rv.Error);
            var rv1 = await sk.ReceiveAsync(tS, 1, 0, null, Gc, null, HandoverCell.Moi, "phoi dung duoc");
            Ok(rv1.Ok, "Gia công nhận đổi ô phiếu Kho sang Hàng mới (mặc định NC đầu) → được", rv1.Error);
            await Check(dk, "F18 nhận đổi ô");
            WorkflowSet(dk, false);
            var off = await sk.IssueByCellAsync(1, "KHO", "GC", 1, Kho, "MOI", false, "MOI", null, "NC3");
            WorkflowSet(dk, true);
            Ok(off.Ok, "Workflow OFF: Kho → Gia công vào NC3 vẫn lưu (N7 — họ tự kiểm soát)", off.Error);
            // NC đầu là máy loại trừ: hàng vào "Nhận vào", chọn NC thường đầu (NC2 sau máy loại trừ) cũng chặn
            var df = Seed("first"); var sf = new HandoverService(df);
            var fx = await sf.IssueByCellAsync(1, "KHO", "GC", 2, Kho, "MOI", false, "MOI", null, "NC2");
            Ok(!fx.Ok && fx.Error.Contains("NC đầu") && fx.Error.Contains(HandoverLedgerService.VaoLabel), "[KHO-NC-DAU] NC1 máy loại trừ: Kho → Gia công chọn NC2 bị chặn (hàng vào \"Nhận vào\")", fx.Error);
            await Save(df, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 2, "GC"), "NC1 máy loại trừ: Kho giao không chọn NC → \"Nhận vào\"");
            // rà độc lập Gói 13 T1: Kho làm hộ NC máy loại trừ (đoạn không Taro — dữ liệu / quy trình cũ) trả về Gia công NC sau đoạn → KHÔNG bị luật NC đầu chặn (K3 / N3-TONC)
            var dm = Seed("mid"); var sm = new HandoverService(dm);
            await Save(dm, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 6, "GC"), "PO NC2 máy loại trừ: Kho giao 6");
            await sm.ReceiveAsync(LastTx(dm), 6, 0, null, Gc);
            await VuHelp.Wts(dm, "GC", "NC1", null, 6, 0, Gc, 0);
            await Save(dm, Req("GC", Gc, HandoverForm.Tot, "HELP:NC1", HandoverForm.AGiao, 3, "KHO", notes: "kho lam ho H04"), "Gia công giao theo NC từ NC1 cho Kho làm hộ 3");
            await sm.ReceiveAsync(LastTx(dm), 3, 0, null, Kho);
            await Save(dm, Req("KHO", Kho, HandoverForm.Tot, "LAMHO", HandoverForm.AGiao, 3, "GC", toNc: "NC3"), "[KHO-NC-DAU] (T1) Kho trả hàng làm hộ về Gia công vào NC3 → được (không phải phôi)");
        }

        Console.WriteLine("[F19] [GN-NGUOC-CHAN] Hàng mới không giao ngược về bộ phận trước (Workflow ON); Đóng gói → Kho thành phẩm vẫn được; OFF như cũ");
        {
            Ok(HandoverForm.IsBackward("GC", "KHO") && HandoverForm.IsBackward("KCS", "HTSP") && HandoverForm.IsBackward("PKG", "GC") && !HandoverForm.IsBackward("PKG", "KHO")
               && !HandoverForm.IsBackward("KHO", "PKG") && !HandoverForm.IsBackward("GC", "KCS"), "Thứ tự Kho → GC → HTSP → KCS → Đóng gói; Đóng gói → Kho không phải ngược");
            var dn = Seed(); var sn = new HandoverService(dn);
            await Save(dn, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await sn.ReceiveAsync(LastTx(dn), 10, 0, null, Gc);
            foreach (var nc in new[] { "NC1", "NC2", "NC3" }) await VuHelp.Wts(dn, "GC", nc, null, 10, 0, Gc, 0);
            await Save(dn, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 8, "KCS"), "GC giao 8 thẳng KCS (xuôi, bỏ qua HTSP — được)");
            await sn.ReceiveAsync(LastTx(dn), 8, 0, null, Kcs);
            await VuHelp.Wts(dn, "KCS", null, "K1", 8, 0, Kcs, 0);
            var b1 = FormHelp.Docs(dn, 1);
            foreach (var to in new[] { "HTSP", "GC", "KHO" })
            {
                var x = await sn.IssueByCellAsync(1, "KCS", to, 1, Kcs, "MOI", false, "MOI");
                Ok(!x.Ok && x.Error.Contains("giao ngược"), $"[GN-NGUOC-CHAN] máy chủ: KCS → {to} Hàng mới bị chặn", x.Error);
            }
            await Save(dn, Req("GC", Gc, HandoverForm.Tot, "DA", HandoverForm.AGiao, 1, "KHO"), "[GN-NGUOC-CHAN] form: Gia công giao Hàng tốt về Kho", false, "giao ngược");
            Ok(FormHelp.Docs(dn, 1) == b1, "Các lần bị chặn không ghi gì");
            await Save(dn, Req("KCS", Kcs, HandoverForm.Tot, "DA", HandoverForm.AGiao, 8, "PKG"), "KCS giao 8 cho Đóng gói (xuôi)");
            await sn.ReceiveAsync(LastTx(dn), 8, 0, null, Pkg);
            await VuHelp.Wts(dn, "PKG", null, "P1", 8, 0, Pkg, 0);
            await Save(dn, Req("PKG", Pkg, HandoverForm.Tot, "DA", HandoverForm.AGiao, 8, "KHO"), "Đóng gói → Kho thành phẩm (không phải giao ngược)");
            await sn.ReceiveAsync(LastTx(dn), 8, 0, null, Kho);
            // hàng lỗi giao ngược vẫn được: KCS phát hiện 1 chiếc chưa kiểm lỗi do Gia công → trả Gia công (Lỗi công đoạn trước)
            await Save(dn, Req("KCS", Kcs, HandoverForm.Tot, "DA", HandoverForm.AGiao, 0, "PKG"), "(KCS hết hàng đã kiểm — 0 chiếc)", false);
            WorkflowSet(dn, false);
            var offN = await sn.IssueByCellAsync(1, "GC", "KHO", 1, Gc, "MOI", false, "MOI");
            WorkflowSet(dn, true);
            Ok(offN.Ok, "Workflow OFF: Gia công → Kho Hàng mới vẫn lưu (N7)", offN.Error);
            await sn.ReceiveAsync(LastTx(dn), 1, 0, null, Kho);
            await Check(dn, "F19");
        }

        Console.WriteLine("[F20] [GC-LOI-TRUOC-NCDAU] máy loại trừ: hàng làm hộ trả về (chờ NC sau máy loại trừ, chưa làm) được tích Lỗi công đoạn trước; \"Nhận vào\" (NC1 loại trừ) được");
        {
            var d2 = Seed("mid"); var s2 = new HandoverService(d2);
            await Save(d2, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 10, "GC"), "Kho giao 10");
            await s2.ReceiveAsync(LastTx(d2), 10, 0, null, Gc);
            await VuHelp.Wts(d2, "GC", "NC1", null, 6, 0, Gc, 0);
            await Save(d2, Req("GC", Gc, HandoverForm.Tot, "HELP:NC1", HandoverForm.AGiao, 4, "HTSP", notes: "lam ho H04"), "GC giao theo NC từ NC1 cho HTSP làm hộ 4");
            await s2.ReceiveAsync(LastTx(d2), 4, 0, null, Htsp);
            await Save(d2, Req("HTSP", Htsp, HandoverForm.Tot, "LAMHO", HandoverForm.AGiao, 4, "GC", toNc: "NC3"), "HTSP trả 4 về Gia công vào NC3");
            await s2.ReceiveAsync(LastTx(d2), 4, 0, null, Gc);
            var r = await Check(d2, "F20 nhận về NC3");
            Ok(Pos(r, "GC", false, "NC:NC1")!.PrevOk && Pos(r, "GC", false, "NC:NC3")!.PrevOk && !Pos(r, "GC", false, "DA")!.PrevOk,
               "Dòng chờ NC1 (đầu) và chờ NC3 (ngay sau máy loại trừ NC2) cho tích; đã xong NC cuối không");
            // [CAUSE-BAT-BUOC 2026-11-10] Hùng 10/11 16:52: tích "Lỗi công đoạn trước" bắt buộc chọn nơi gây lỗi, không điền sẵn → bỏ trống bị chặn, không ghi
            await Save(d2, Req("GC", Gc, HandoverForm.Loi, "NC:NC3", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PTra, prev: true, cat: 2),
                       "[CAUSE-BAT-BUOC] GC tích Lỗi công đoạn trước, bỏ trống nơi gây lỗi", false, "nơi gây lỗi");
            await Save(d2, Req("GC", Gc, HandoverForm.Loi, "NC:NC3", HandoverForm.AGiao, 1, "HTSP", HandoverForm.PTra, prev: true, cause: "HTSP", cat: 2),
                       "GC: 1 chiếc làm hộ trả về (chờ NC3, chưa làm) lỗi do HTSP → trả HTSP (1 phiếu Hàng lỗi công đoạn trước)");
            Ok(d2.NgCases.List.Last().CauseGroupCode == "HTSP" && d2.HandoverTransactions.List.Last().ToGroupCode == "HTSP", "vụ ghi HTSP gây lỗi, phiếu trả HTSP");
            await Save(d2, Req("GC", Gc, HandoverForm.Loi, "NC:NC1", HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2),
                       "GC: 1 chiếc chờ NC1 (chưa làm) lỗi do Kho → trả Kho");
            var df = Seed("first"); var sf = new HandoverService(df);
            await Save(df, Req("KHO", Kho, HandoverForm.Tot, "PHOI", HandoverForm.AGiao, 5, "GC"), "NC1 máy loại trừ: Kho giao 5 → \"Nhận vào\"");
            await sf.ReceiveAsync(LastTx(df), 5, 0, null, Gc);
            var rf = await Row(df);
            Ok(Pos(rf, "GC", false, "NC:" + HandoverLedgerService.VaoCode)!.PrevOk, "Dòng \"Nhận vào (chưa qua NC)\" cho tích Lỗi công đoạn trước");
            await Save(df, Req("GC", Gc, HandoverForm.Loi, "NC:" + HandoverLedgerService.VaoCode, HandoverForm.AGiao, 1, "KHO", HandoverForm.PTra, prev: true, cause: "KHO", cat: 2),
                       "GC: 1 chiếc ở \"Nhận vào\" lỗi do Kho → trả Kho");
        }

        Console.WriteLine("[F21] [GC-HTSP-NC-HIEN] NC cuối là máy loại trừ (Taro ở HTSP): chữ \"để làm NC3 (Taro)\"; không có máy loại trừ cuối thì không có chữ");
        {
            var dl = Seed("last");
            Ok(HandoverForm.TailExclText(await Row(dl)) == "để làm NC3 (Taro)", "NC3 máy loại trừ cuối: \"để làm NC3 (Taro)\"", HandoverForm.TailExclText(await Row(dl)));
            Ok(HandoverForm.TailExclText(await Row(Seed("mid"))) == null && HandoverForm.TailExclText(await Row(Seed())) == null, "Máy loại trừ giữa chuỗi / không có: không có chữ");
        }

        Console.WriteLine($"\nKẾT QUẢ: {(fails == 0 ? "ĐẠT hết" : fails + " SAI")}");
        return fails == 0 ? 0 : 1;
    }
}
