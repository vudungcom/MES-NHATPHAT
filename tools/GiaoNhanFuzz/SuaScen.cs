// [GC-SUA-TAI-CHO 2026-11-04] Kịch bản cố định "sua": Kết quả sửa (chứng từ nội bộ nhóm sửa) + KCS xác nhận — bằng hàm thật
//   HandoverService (CreateRepairResultAsync / ConfirmRepairAsync / VoidRepairResultAsync / VoidRepairCheckAsync) + sổ chung.
//   Khách chốt 04/11 14:53: sửa tại chỗ dùng chung mọi bộ phận; đạt có KCS xác nhận; không đạt theo quy trình lỗi bình thường.
//   Hùng chốt 04/11 16:11: 2 bảng mới; người KCS xác nhận khác người ghi.
//   Sau MỖI bước ghi kiểm chung: cân PO, không ô âm (gồm từng NC, vụ), vụ cân (Lech 0), Tồn = tổng các phần, khung lỗi (NgTrace) không lệch,
//   dòng B cuối = sổ hiện tại.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class SuaScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    const int Kcs = 3, Gc = 2, Other = 4;

    static AppDbContext Seed(bool firstExcluded = false, bool workflow = true)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-SUA", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 20, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 20, NgayNhanPhoi = DateTime.Now, SoPO = "PO1", PartNo = "P" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 3, KhPlanDetailId = 1, StepOrder = 3, NC = "NC3", MachineRegistered = "M3" });
        if (firstExcluded)
            db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 1, KhPlanDetailId = 1, SoMay = "M1" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    static long LastTx(AppDbContext db) => db.HandoverTransactions.List.Max(t => t.HandoverTxId);
    /// <summary>[NHAN-DOI-O-NC] S36: Kho→GC 2 (NC1) · WTS NC1 OK 2 · WTS NC2 NG 1 (vụ ở NC2) · GC giao ô Hàng lỗi → Kho ô Hàng lỗi · Kho nhận ·
    /// Kho giao ô Hàng lỗi → GC ô Hàng lỗi (phiếu không NC) — trả phiếu chờ GC nhận.</summary>
    static void Seed36(out AppDbContext d, out HandoverService s, out long txBack)
    {
        d = Seed(); s = new HandoverService(d); WtsSvcHelp.Reset();
        s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI", toNC: "NC1").GetAwaiter().GetResult();
        s.ReceiveAsync(LastTx(d), 2, 0, null, Gc).GetAwaiter().GetResult();
        WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc).GetAwaiter().GetResult();
        var w = WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2).GetAwaiter().GetResult();
        var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w.LogId).NgCaseId;
        var g1 = s.IssueByCellAsync(1, "GC", "KHO", 1, Gc, "LOI", false, "LOI", ngCaseId: vu).GetAwaiter().GetResult();
        var k1 = s.ReceiveAsync(LastTx(d), 0, 1, null, 1, null, HandoverCell.Loi).GetAwaiter().GetResult();
        var g2 = s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "LOI", false, "LOI", ngCaseId: vu).GetAwaiter().GetResult();
        Ok(w.Ok && g1.Ok && k1.Ok && g2.Ok && d.HandoverTransactions.List.Last().ToNC == null, "S36 dựng xong (phiếu Kho → GC ô Hàng lỗi, không NC)", $"{w.Err}|{g1.Error}|{k1.Error}|{g2.Error}");
        txBack = LastTx(d);
    }
    static long LastRep(AppDbContext db) => db.HandoverRepairResults.List.Max(t => t.HandoverRepairResultId);
    static long LastChk(AppDbContext db) => db.HandoverRepairChecks.List.Max(t => t.HandoverRepairCheckId);

    /// <summary>Chữ ký sổ: mọi ô mọi nhóm + từng NC / mã CV + từng vụ (để so "huỷ xong = như chưa ghi").</summary>
    static string Sig(LedgerRow r)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in r.Groups.Values.OrderBy(x => x.Code))
        {
            sb.Append($"{g.Code}:{g.ChuaLam}/{g.DaLam}/{g.ConGiao}/{g.LoiGiu}/{g.SuaGiu}/{g.ChoNhan}/{g.ChoKcs}/{g.Ton};");
            foreach (var s in g.Steps) sb.Append($"{s.Pg}{s.Code}={s.Waiting}/{s.Ok}/{s.Ng};");
        }
        foreach (var c in r.Cases.Values.OrderBy(x => x.Id))
            sb.Append($"V{c.Id}:{c.SinhRa}|{string.Join(",", c.Held.Where(x => x.Value != 0).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))}|{c.Transit}|{string.Join(",", c.DaXuLy.Where(x => x.Value != 0).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))};");
        return sb.ToString();
    }

    /// <summary>Kiểm chung sau mỗi bước ghi.</summary>
    static async Task<LedgerRow> Check(AppDbContext db, string buoc)
    {
        var led = new HandoverLedgerService(db);
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var bad = new List<string>();
        if (!row.Balanced) bad.Add("PO không cân");
        var neg = HandoverLedgerService.NegativeCells(row);
        if (neg.Count > 0) bad.Add("ô âm: " + string.Join(", ", neg.Select(x => x.Value.Text + "=" + x.Value.Value)));
        if (row.Warnings.Count > 0) bad.Add("cảnh báo: " + string.Join(" | ", row.Warnings));
        foreach (var c in row.Cases.Values) if (c.Lech != 0) bad.Add($"vụ {c.No} lệch {c.Lech}");
        var inp = (await led.GetInputForDetailAsync(1))!;
        var tr = HandoverNgTrace.Build(inp);
        if (tr.Problems.Count > 0) bad.Add("khung lỗi: " + string.Join(" | ", tr.Problems));
        var steps = HandoverLedgerService.ComputeSteps(inp);
        if (steps.Count > 0 && Sig(steps.Last().Row) != Sig(row)) bad.Add("dòng B cuối ≠ sổ");
        var w = HandoverLedgerService.WhereIs(row);
        if (!w.Balanced) bad.Add($"Đang ở đâu {w.Total} ≠ phôi {w.Phoi}");
        Ok(bad.Count == 0, $"[chung] {buoc}: cân, không ô âm, vụ cân, khung lỗi + dòng B + Đang ở đâu khớp", string.Join("\n", bad));
        return row;
    }

    public static async Task<int> Run()
    {
        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[S1] Kho gửi GC sửa 2 phôi lỗi (ô Hàng sửa) → GC ghi Kết quả sửa: đạt 1 (chờ KCS), không đạt 1");
        var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 18, 2, null, Gc, ngCategoryId: 1);   // 2 phôi lỗi lúc nhận → phiếu trả tự lập
        var back = db.HandoverTransactions.List.Single(x => x.SourceReceiveId != null);
        await svc.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1);               // Kho nhận lại 2 vào ô Hàng lỗi
        var vu1 = back.NgCaseId;
        var r0 = await svc.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "SUA", ngCaseId: vu1);   // gửi GC nhờ sửa
        await svc.ReceiveAsync(LastTx(db), 2, 0, null, Gc);
        var b1 = await Check(db, "S1 trước");
        Ok(r0.Ok && b1.G("GC").SuaGiu == 2 && b1.G("KHO").LoiGiu == 0, "GC đang giữ ô Hàng sửa 2 (vụ của Kho)", $"{r0.Error} sua {b1.G("GC").SuaGiu}");
        var sigBefore = Sig(b1);
        var c1 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, 1, 1, "NC1", "mài lại", Gc);
        var a1 = await Check(db, "S1 sau");
        var g = a1.G("GC");
        Ok(c1.Ok, "Ghi Kết quả sửa được", c1.Error);
        Ok(g.SuaGiu == 0 && g.LoiGiu == 1 && g.ChoKcs == 1 && g.ChuaLam == b1.G("GC").ChuaLam && g.Ton == b1.G("GC").Ton,
           "GC: ô Hàng sửa 0, ô Hàng lỗi 1 (không đạt), Chờ KCS 1, Chưa GC không đổi, Tồn không đổi",
           $"sua {g.SuaGiu} loi {g.LoiGiu} kcs {g.ChoKcs} chua {g.ChuaLam}/{b1.G("GC").ChuaLam} ton {g.Ton}/{b1.G("GC").Ton}");
        var cs1 = a1.Cases[vu1!.Value];
        Ok(cs1.HeldAt("GC", HandoverCell.Loi) == 1 && cs1.HeldAt("GC", HandoverCell.Sua) == 0 && cs1.Transit == 1 && cs1.SinhRa == 2,
           "Vụ của Kho: không đạt giữ ô Hàng lỗi GC 1, đạt đang chuyển 1 (chờ KCS), không sinh vụ mới", $"held {string.Join(",", cs1.Held)} transit {cs1.Transit}");
        Ok(a1.Cases.Count == b1.Cases.Count, "Không sinh vụ lỗi mới");
        Ok(a1.G("GC").Explains["Ton"].Formula.Contains("Chờ KCS") && a1.G("GC").Explains.ContainsKey("ChoKcs"), "Diễn giải Tồn có Chờ KCS + diễn giải ô Chờ KCS");
        var wh = HandoverLedgerService.WhereIs(a1);
        Ok(wh.Items.Any(i => i.Place == "GC" && i.Kind == "CHO_KCS" && i.Qty == 1), "Đang ở đâu: GC · hàng sửa chờ KCS xác nhận 1");
        var wts0 = HandoverLedgerService.WtsCap(a1, "GC", "NC1");
        Ok(wts0.Max == b1.G("GC").Steps.First(s => s.Code == "NC1").Waiting, "Chờ KCS chưa vào NC1 (trần WTS NC1 không đổi)", $"{wts0.Max}");

        Console.WriteLine("[S2] KCS xác nhận: người ghi tự xác nhận → chặn; người không quyền KCS → chặn; KCS đạt 1 → vào NC1");
        {
            var self = await svc.ConfirmRepairAsync(LastRep(db), 1, 0, null, Gc);
            Ok(!self.Ok && self.Error.Contains("khác người"), "Người ghi Kết quả sửa tự xác nhận bị chặn", self.Error);
            var noKcs = await svc.ConfirmRepairAsync(LastRep(db), 1, 0, null, FuzzPerm.GcOnlyUser(db));
            Ok(!noKcs.Ok && noKcs.Error.Contains("quyền"), "Người không có quyền Sửa KCS bị chặn", noKcs.Error);
            var over = await svc.ConfirmRepairAsync(LastRep(db), 2, 0, null, Kcs);
            Ok(!over.Ok && over.Error.Contains("Còn chờ"), "Xác nhận vượt số chờ bị chặn", over.Error);
            var bad0 = await svc.ConfirmRepairAsync(LastRep(db), 0, 0, null, Kcs);
            Ok(!bad0.Ok, "Xác nhận 0 bị chặn", bad0.Error);
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var okc = await svc.ConfirmRepairAsync(LastRep(db), 1, 0, "đạt", Kcs);
            Ok(okc.Ok && Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains("MES_PO_1"), "KCS xác nhận đạt 1 (dưới khoá MES_PO_1)", okc.Error + " khoá: " + string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            var a2 = await Check(db, "S2 sau");
            var nc1 = a2.G("GC").Steps.First(s => s.Code == "NC1");
            Ok(a2.G("GC").ChoKcs == 0 && a2.G("GC").ChuaLam == b1.G("GC").ChuaLam + 1 && nc1.Waiting == b1.G("GC").Steps.First(s => s.Code == "NC1").Waiting + 1,
               "Chờ KCS 0; Chưa GC +1; chờ NC1 +1 (NC đã chọn)", $"kcs {a2.G("GC").ChoKcs} chua {a2.G("GC").ChuaLam} nc1 {nc1.Waiting}");
            Ok(a2.Cases[vu1.Value].DaXuLy.TryGetValue("GC", out var dx) && dx == 1 && a2.Cases[vu1.Value].Transit == 0, "Vụ: đã xử lý ở GC 1, đang chuyển 0");
            Ok(a2.G("GC").NhanLoiXl == b1.G("GC").NhanLoiXl + 1 && a2.G("GC").GiaoSua == b1.G("GC").GiaoSua + 2,
               "Cột Excel GC: Nhận·Hàng lỗi đã xử lý +1, Giao·Hàng sửa +2");
            var tr = (await led.GetNgTraceAsync(1))!;
            Ok(tr.Events.Any(e => e.Type == "KQ_SUA" && e.Group == "GC") && tr.Events.Any(e => e.Type == "KCS_XN" && e.VeSx == 1), "Khung hàng lỗi có dòng Kết quả sửa + KCS xác nhận (đã xử lý 1)");
            var steps = await led.GetStepsForDetailAsync(1);
            Ok(steps.Any(s => s.Kind == "KQSUA") && steps.Any(s => s.Kind == "XNSUA"), "Dòng B có bước Kết quả sửa + KCS xác nhận");
        }

        Console.WriteLine("[S3] Huỷ: Kết quả sửa có xác nhận → chặn; huỷ xác nhận rồi huỷ Kết quả sửa → sổ y như trước khi ghi");
        {
            var v1 = await svc.VoidRepairResultAsync(LastRep(db), "ghi nham", Gc);
            Ok(!v1.Ok && v1.Error.Contains("huỷ các lần xác nhận"), "Huỷ Kết quả sửa khi còn lần KCS xác nhận bị chặn", v1.Error);
            var vOther = await svc.VoidRepairCheckAsync(LastChk(db), "xac nhan nham", FuzzPerm.GcOnlyUser(db));
            Ok(!vOther.Ok, "Người không phải người xác nhận / không quyền KCS không huỷ được lần xác nhận", vOther.Error);
            var vc = await svc.VoidRepairCheckAsync(LastChk(db), "xac nhan nham", Kcs);
            await Check(db, "S3 huỷ xác nhận");
            var vr = await svc.VoidRepairResultAsync(LastRep(db), "ghi nham", Gc);
            var a3 = await Check(db, "S3 huỷ Kết quả sửa");
            Ok(vc.Ok && vr.Ok && Sig(a3) == sigBefore, "Huỷ hết → mọi ô, từng NC, từng vụ y như trước khi ghi", vc.Error + vr.Error);
            Ok(db.HandoverRepairResults.List.Count == 1 && db.HandoverRepairResults.List[0].IsVoided && db.HandoverRepairChecks.List[0].IsVoided,
               "Chỉ ghi thêm: dòng đã huỷ vẫn còn (IsVoided), không xoá");
        }

        Console.WriteLine("[S4] Trần + vụ + NC làm tiếp: vượt / vụ sai / không đạt từ ô Hàng lỗi / NC lạ → chặn");
        {
            var over = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, 2, 1, "NC1", null, Gc);
            Ok(!over.Ok && over.Error.Contains("tối đa 2"), "Đạt + không đạt vượt số vụ đang giữ ô Hàng sửa bị chặn", over.Error);
            var noCase = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, null, 1, 0, "NC1", null, Gc);
            Ok(!noCase.Ok, "Không chọn vụ (không có hàng lỗi cũ chưa mã) bị chặn", noCase.Error);
            var badCase = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, 999, 1, 0, "NC1", null, Gc);
            Ok(!badCase.Ok && badCase.Error.Contains("không thuộc PO"), "Vụ không thuộc PO bị chặn", badCase.Error);
            var badNc = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, 1, 0, "NC9", null, Gc);
            Ok(!badNc.Ok && badNc.Error.Contains("NC NC9"), "NC làm tiếp không có trong quy trình bị chặn", badNc.Error);
            var vao = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, 1, 0, HandoverLedgerService.VaoCode, null, Gc);
            Ok(!vao.Ok, "NC làm tiếp = @VAO bị chặn", vao.Error);
            var cell = await svc.CreateRepairResultAsync(1, "GC", "MOI", vu1, 1, 0, null, null, Gc);
            Ok(!cell.Ok, "Ô lấy hàng khác Hàng sửa / Hàng lỗi bị chặn", cell.Error);
            var neg = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, -1, 2, null, null, Gc);
            Ok(!neg.Ok, "Số âm bị chặn", neg.Error);
            var noRight = await svc.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, null, 1, 0, null, null, FuzzPerm.GcOnlyUser(db));
            Ok(!noRight.Ok && noRight.Error.Contains("quyền"), "Người không có quyền Sửa nhóm bị chặn", noRight.Error);
            Ok(db.HandoverRepairResults.List.Count == 1, "Không có dòng nào ghi thêm khi bị chặn");
        }

        Console.WriteLine("[S5] Lấy từ ô Hàng lỗi (GC tự làm hỏng ở NC2, WTS NG 2): đạt 2 → KCS đạt 1 + loại 1 → loại về ô Hàng lỗi cùng vụ");
        {
            await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu1, 2, 0, "NC1", null, Gc);   // sửa lại cả 2 phôi của Kho, đạt hết
            await svc.ConfirmRepairAsync(LastRep(db), 2, 0, null, Kcs);
            await Check(db, "S5 phôi Kho sửa xong");
            await VuHelp.Wts(db, "GC", "NC1", null, 20, 0, Gc, 0);
            var w2 = await VuHelp.Wts(db, "GC", "NC2", null, 18, 2, Gc, 2);
            var b5 = await Check(db, "S5 trước");
            var failLoi = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w2.NgCaseId, 1, 1, "NC3", null, Gc);
            Ok(!failLoi.Ok && failLoi.Error.Contains("chỉ ghi số đạt"), "Lấy từ ô Hàng lỗi mà ghi không đạt bị chặn", failLoi.Error);
            // [GN-FORM1][K2] hàng hỏng lúc chạy NC2 → sửa đạt làm tiếp NC2 (khách 05/11 15:39); chọn NC3 (bỏ qua NC2) bị chặn. Trước: bộ kiểm dùng NC3.
            var skip5 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w2.NgCaseId, 2, 0, "NC3", null, Gc);
            Ok(!skip5.Ok && skip5.Error.Contains("NC2") && skip5.Error.Contains("không bỏ qua NC"), "[K2] NC làm tiếp NC3 (sau NC của vụ NC2) bị chặn", skip5.Error);
            var c5 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w2.NgCaseId, 2, 0, "NC2", null, Gc);
            var a5 = await Check(db, "S5 ghi");
            Ok(c5.Ok && a5.G("GC").LoiGiu == b5.G("GC").LoiGiu - 2 && a5.G("GC").ChoKcs == 2, "Ô Hàng lỗi GC −2, Chờ KCS 2", c5.Error);
            await svc.ConfirmRepairAsync(LastRep(db), 1, 0, null, Kcs);
            await Check(db, "S5 KCS lần 1");
            await svc.ConfirmRepairAsync(LastRep(db), 0, 1, "van xuoc", Kcs);
            var a5b = await Check(db, "S5 KCS lần 2");
            var cs = a5b.Cases[w2.NgCaseId!.Value];
            Ok(a5b.G("GC").ChoKcs == 0 && a5b.G("GC").LoiGiu == b5.G("GC").LoiGiu - 1 && cs.HeldAt("GC", HandoverCell.Loi) == 1 && cs.DaXuLy["GC"] == 1,
               "KCS từng phần: đạt 1 vào NC2, loại 1 về ô Hàng lỗi GC cùng vụ", $"loi {a5b.G("GC").LoiGiu} held {cs.HeldAt("GC", HandoverCell.Loi)}");
            Ok(a5b.G("GC").Steps.First(s => s.Code == "NC2").Waiting == b5.G("GC").Steps.First(s => s.Code == "NC2").Waiting + 1, "Chờ NC2 +1");
            Ok(cs.TuXuLy.Contains("GC"), "Vụ ghi GC tự xử lý");
        }

        Console.WriteLine("[S6] K5: hàng đạt đã nhập WTS → huỷ lần KCS xác nhận bị chặn + gợi ý gỡ chỉ dòng WTS");
        {
            var nc3 = (await led.GetLedgerForDetailAsync(1))!.G("GC").Steps.First(s => s.Code == "NC2");   // [GN-FORM1] hàng đạt nay vào NC2 (K2)
            var w = await VuHelp.Wts(db, "GC", "NC2", null, nc3.Waiting ?? 0, 0, Gc, 0);
            await Check(db, "S6 WTS NC2");
            var okChk = db.HandoverRepairChecks.List.Where(c => !c.IsVoided && c.QtyOk > 0).Max(c => c.HandoverRepairCheckId);
            var v = await svc.VoidRepairCheckAsync(okChk, "xac nhan nham", Kcs);
            Ok(!v.Ok && v.Error.Contains("K5") && v.Error.Contains($"WTS #{w.WtsLogId}"), "Huỷ lần xác nhận khi hàng đạt đã WTS bị chặn, gợi ý dòng WTS", v.Error);
            Ok(!db.HandoverRepairChecks.List.Single(c => c.HandoverRepairCheckId == okChk).IsVoided, "Bị chặn thì không ghi gì (lần xác nhận vẫn còn)");
            await Check(db, "S6 sau chặn");
        }

        Console.WriteLine("[S7] K5: không đạt đã đem đi (huỷ hàng lỗi) → huỷ Kết quả sửa có không đạt bị chặn, gợi ý phiếu huỷ");
        {
            var db7 = Seed(); var s7 = new HandoverService(db7);
            await s7.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await s7.ReceiveAsync(db7.HandoverTransactions.List.Max(t => t.HandoverTxId), 20, 0, null, Gc);
            var w7 = await VuHelp.Wts(db7, "GC", "NC1", null, 17, 3, Gc, 2);
            await s7.IssueByCellAsync(1, "GC", "HTSP", 3, Gc, "LOI", false, "SUA", ngCaseId: w7.NgCaseId);   // GC gửi HTSP nhờ sửa 3
            await s7.ReceiveAsync(db7.HandoverTransactions.List.Max(t => t.HandoverTxId), 3, 0, null, Other);
            var c7 = await s7.CreateRepairResultAsync(1, "HTSP", HandoverCell.Sua, w7.NgCaseId, 1, 2, null, null, Other);
            var r7 = (await new HandoverLedgerService(db7).GetLedgerForDetailAsync(1))!;
            Ok(c7.Ok && r7.G("HTSP").LoiGiu == 2 && r7.G("HTSP").ChoKcs == 1 && r7.G("HTSP").SuaGiu == 0, "HTSP sửa tại chỗ hàng GC gửi: không đạt 2 vào ô Hàng lỗi HTSP, chờ KCS 1", c7.Error);
            await Check(db7, "S7 HTSP ghi");
            var huy = await s7.IssueSpecialAsync(1, "HTSP", "HUY", 2, Other, HandoverPurpose.Scrap, ngCaseId: w7.NgCaseId);
            await Check(db7, "S7 HTSP huỷ 2");
            var rid = db7.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId);
            var v7 = await s7.VoidRepairResultAsync(rid, "ghi nham", Other);
            Ok(huy.Ok && !v7.Ok && v7.Error.Contains("K5") && v7.Error.Contains("Phiếu #"), "Huỷ Kết quả sửa khi hàng không đạt đã huỷ bị chặn (K5), gợi ý phiếu huỷ", huy.Error + " | " + v7.Error);
            var x7 = await s7.ConfirmRepairAsync(rid, 1, 0, null, Kcs);
            var a7 = await Check(db7, "S7 KCS");
            Ok(x7.Ok && a7.G("HTSP").ChuaLam == r7.G("HTSP").ChuaLam + 1, "KCS xác nhận: hàng đạt vào Chưa làm HTSP (làm tiếp ở nhóm sửa)");
        }

        Console.WriteLine("[S8] Kho sửa phôi lỗi của mình (báo lỗi phôi) → KCS đạt → về phôi còn giao");
        {
            var db8 = Seed(); var s8 = new HandoverService(db8);
            var all = new List<string> { "KHO" };
            await s8.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "", null, 1, all, ngCategoryId: 1);
            var vu8 = db8.HandoverNgReports.List.Last().NgCaseId;
            var b8 = await Check(db8, "S8 trước");
            var c8 = await s8.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu8, 2, 0, "NC1", null, 1);
            await Check(db8, "S8 ghi");
            var x8 = await s8.ConfirmRepairAsync(db8.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId), 2, 0, null, Kcs);
            var a8 = await Check(db8, "S8 KCS");
            Ok(c8.Ok && x8.Ok && a8.G("KHO").ConGiao == b8.G("KHO").ConGiao + 2 && a8.G("KHO").LoiGiu == 1 && db8.HandoverRepairResults.List[0].ToNC == null,
               "Kho: phôi còn giao +2, ô Hàng lỗi còn 1; NC chỉ lưu cho Gia công", c8.Error + x8.Error);
        }

        Console.WriteLine("[S9] NC đầu là máy loại trừ (@VAO): đạt không chọn NC → về dòng Nhận vào; chọn NC loại trừ → chặn");
        {
            var db9 = Seed(firstExcluded: true); var s9 = new HandoverService(db9);
            await s9.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var vu9 = db9.HandoverNgReports.List.Last().NgCaseId;
            await s9.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "SUA", ngCaseId: vu9);
            await s9.ReceiveAsync(db9.HandoverTransactions.List.Max(t => t.HandoverTxId), 2, 0, null, Gc);
            var ex = await s9.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu9, 2, 0, "NC1", null, Gc);
            Ok(!ex.Ok && ex.Error.Contains("máy loại trừ"), "NC làm tiếp là máy loại trừ bị chặn", ex.Error);
            var c9 = await s9.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu9, 2, 0, null, null, Gc);
            await s9.ConfirmRepairAsync(db9.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId), 2, 0, null, Kcs);
            var a9 = await Check(db9, "S9 KCS");
            var vao = a9.G("GC").Steps.FirstOrDefault(s => HandoverLedgerService.IsVao(s.Code));
            Ok(c9.Ok && vao != null && vao.Waiting == 2, "Hàng đạt (như hàng mới) nằm ở Nhận vào (chưa qua NC) 2", c9.Error + $" vao {vao?.Waiting}");
        }

        Console.WriteLine("[S10] Workflow OFF: vẫn kiểm trần + quyền + khác người");
        {
            var db10 = Seed(workflow: false); var s10 = new HandoverService(db10);
            await s10.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var vu10 = db10.HandoverNgReports.List.Last().NgCaseId;
            var o10 = await s10.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu10, 3, 0, null, null, 1);
            Ok(!o10.Ok && o10.Error.Contains("tối đa 2"), "Workflow OFF vẫn chặn vượt trần", o10.Error);
            var c10 = await s10.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu10, 2, 0, null, null, 1);
            var self10 = await s10.ConfirmRepairAsync(db10.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId), 2, 0, null, 1);
            Ok(c10.Ok && !self10.Ok, "Workflow OFF vẫn bắt KCS khác người ghi", self10.Error);
        }

        Console.WriteLine("[S11] PO có Kết quả sửa = đã có chứng từ (không xoá / tách, không làm mới quy trình)");
        {
            var docs = await PoGuard.CountDocsAsync(db, 1);
            Ok(docs.KqSuaAll >= 1 && docs.HasAny && docs.Text().Contains("Kết quả sửa"), "PoGuard đếm Kết quả sửa", docs.Text());
            var db11 = Seed(); var s11 = new HandoverService(db11);
            await s11.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 1, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var vu11 = db11.HandoverNgReports.List.Last().NgCaseId;
            await s11.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu11, 1, 0, null, null, 1);
            db11.HandoverNgReports.List.Last().IsVoided = true;   // bỏ lý do báo lỗi để chỉ còn Kết quả sửa chặn
            var blk = await new KhPlanService(db11).RouteRefreshBlockAsync(1, 1);
            Ok(blk != null && blk.Contains("Kết quả sửa"), "Làm mới quy trình bị chặn vì PO đã có Kết quả sửa", blk);
        }

        Console.WriteLine("[S12] Danh sách Lịch sử + số chờ KCS");
        {
            var list = await svc.GetRepairsAsync(1);
            Ok(list.Count == 3 && list.Count(x => x.IsVoided) == 1 && list.All(x => x.Checks.Count >= 0) && list.First().CaseLabel.StartsWith("VL"), "GetRepairsAsync: 3 dòng (1 đã huỷ), có nhãn vụ",
               string.Join(" | ", list.Select(x => $"{x.Id} void={x.IsVoided} {x.CaseLabel}")));
            await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, db.WtsProductionLogs.List.Single(w => w.NC == "NC2" && w.QtyNg > 0).NgCaseId, 1, 0, "NC2", null, Gc);   // [GN-FORM1][K2] NC của vụ
            var pend = await svc.GetRepairPendingCountPerDetailAsync(new[] { 1 });
            Ok(pend.TryGetValue(1, out var n) && n == 1, "Số Kết quả sửa chờ KCS của PO = 1", $"{n}");
            await Check(db, "S12");
        }

        Console.WriteLine("[S13] 2 vụ cùng ô Hàng sửa (VL a = 2, VL b = 3): ghi vụ a đạt 4 → TRẦN theo vụ chặn 'tối đa 2' (Workflow ON và OFF)");
        foreach (var wf in new[] { true, false })
        {
            var d13 = Seed(workflow: wf); var s13 = new HandoverService(d13);
            await s13.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var va = d13.HandoverNgReports.List.Last().NgCaseId;
            await s13.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 2);
            var vb = d13.HandoverNgReports.List.Last().NgCaseId;
            await s13.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "SUA", ngCaseId: va);
            await s13.ReceiveAsync(d13.HandoverTransactions.List.Max(t => t.HandoverTxId), 2, 0, null, Gc);
            await s13.IssueByCellAsync(1, "KHO", "GC", 3, 1, "LOI", false, "SUA", ngCaseId: vb);
            await s13.ReceiveAsync(d13.HandoverTransactions.List.Max(t => t.HandoverTxId), 3, 0, null, Gc);
            var o13 = await s13.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, va, 3, 1, null, null, Gc);
            Ok(!o13.Ok && o13.Error.Contains("tối đa 2"), $"Workflow {(wf ? "ON" : "OFF")}: vụ a đạt 3 + không đạt 1 (ô có 5) bị trần theo vụ chặn 'tối đa 2'", o13.Error);
            var c13 = await s13.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vb, 2, 1, null, null, Gc);
            var a13 = await Check(d13, $"S13 {(wf ? "ON" : "OFF")}");
            Ok(c13.Ok && a13.Cases[vb!.Value].HeldAt("GC", HandoverCell.Sua) == 0 && a13.Cases[va!.Value].HeldAt("GC", HandoverCell.Sua) == 2,
               $"Workflow {(wf ? "ON" : "OFF")}: vụ b đạt 2 + không đạt 1 lấy đúng vụ b, vụ a còn 2", c13.Error);
        }

        Console.WriteLine("[S14] Nhóm sửa = KCS: KCS sửa hàng lỗi của mình (báo lỗi đầu ra) → người KCS khác xác nhận");
        {
            var d14 = Seed(); var s14 = new HandoverService(d14);
            await s14.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await s14.ReceiveAsync(d14.HandoverTransactions.List.Max(t => t.HandoverTxId), 20, 0, null, Gc);
            await VuHelp.Wts(d14, "GC", "NC1", null, 20, 0, Gc, 0); await VuHelp.Wts(d14, "GC", "NC2", null, 20, 0, Gc, 0); await VuHelp.Wts(d14, "GC", "NC3", null, 20, 0, Gc, 0);
            await s14.IssueByCellAsync(1, "GC", "KCS", 5, Gc, "MOI", false, "MOI");
            await s14.ReceiveAsync(d14.HandoverTransactions.List.Max(t => t.HandoverTxId), 5, 0, null, Kcs);
            var rp14 = await s14.ReportNgAfterReceiveAsync(1, "KCS", HandoverNgSource.Chua, null, 2, HandoverNgSource.DauRa, null, "", null, Kcs, new List<string> { "KCS" }, ngCategoryId: 5);
            var v14 = d14.HandoverNgReports.List.Last().NgCaseId;
            var c14 = await s14.CreateRepairResultAsync(1, "KCS", HandoverCell.Loi, v14, 2, 0, "NC1", null, Kcs);
            var self14 = await s14.ConfirmRepairAsync(d14.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId), 2, 0, null, Kcs);
            var x14 = await s14.ConfirmRepairAsync(d14.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId), 2, 0, null, 5);
            var a14 = await Check(d14, "S14");
            Ok(rp14.Ok && c14.Ok && !self14.Ok && x14.Ok && a14.G("KCS").ChuaLam == 5 && a14.G("KCS").LoiGiu == 0 && d14.HandoverRepairResults.List[0].ToNC == null,
               "KCS tự sửa: chính người ghi không xác nhận được, người khác xác nhận → Chưa làm KCS về 5; NC chỉ lưu cho Gia công", rp14.Error + c14.Error + x14.Error);
        }

        Console.WriteLine("[S15] KCS loại 1 → chiếc loại bị huỷ hàng lỗi → huỷ lần xác nhận bị chặn (K5), gợi ý phiếu huỷ");
        {
            var d15 = Seed(); var s15 = new HandoverService(d15);
            await s15.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var v15 = d15.HandoverNgReports.List.Last().NgCaseId;
            await s15.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, v15, 2, 0, null, null, 1);
            var rid = d15.HandoverRepairResults.List.Max(x => x.HandoverRepairResultId);
            await s15.ConfirmRepairAsync(rid, 1, 1, null, Kcs);
            await Check(d15, "S15 KCS đạt 1 loại 1");
            var h15 = await s15.IssueSpecialAsync(1, "KHO", "HUY", 1, 1, HandoverPurpose.Scrap, ngCaseId: v15);
            await Check(d15, "S15 Kho huỷ chiếc loại");
            var cid = d15.HandoverRepairChecks.List.Max(c => c.HandoverRepairCheckId);
            var v = await s15.VoidRepairCheckAsync(cid, "xac nhan nham", Kcs);
            Ok(h15.Ok && !v.Ok && v.Error.Contains("K5") && v.Error.Contains("Phiếu #"), "Huỷ lần xác nhận khi chiếc KCS loại đã bị huỷ → chặn, gợi ý phiếu huỷ", h15.Error + " | " + v.Error);
            var inp = (await new HandoverLedgerService(d15).GetInputForDetailAsync(1))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidRepairCheck(cid), new HashSet<string> { "X:" + cid }, loopOp: true);
            Ok(plan.Complete && plan.Steps.Count == 1 && plan.Steps[0].Kind == "PHIEU", "Gợi ý gỡ đúng 1 bước: huỷ phiếu huỷ hàng lỗi", string.Join(",", plan.Steps.Select(x => x.Kind + "#" + x.Id)));
            var vt = await s15.VoidIssueAsync(plan.Steps[0].Id, "goi y", 1, new List<string> { "KHO" });
            var v2 = await s15.VoidRepairCheckAsync(cid, "xac nhan nham", Kcs);
            var a15 = await Check(d15, "S15 sau gỡ");
            Ok(vt.Ok && v2.Ok && a15.G("KHO").ChoKcs == 2, "Làm theo gợi ý → huỷ lần xác nhận được, Chờ KCS về 2", vt.Error + v2.Error);
        }

        Console.WriteLine("[S16] Vụ đã có Kết quả sửa → không đổi loại lỗi được (truy vết KCS đã ký)");
        {
            var vu16 = db.HandoverRepairResults.List.First(r => !r.IsVoided).NgCaseId!.Value;
            var cur = db.NgCases.List.First(c => c.NgCaseId == vu16).NgCategoryId;
            var blk = await NgCaseService.CategoryChangeBlockedAsync(db, vu16, cur == 1 ? 2 : 1);
            Ok(blk != null && blk.Contains("Kết quả sửa"), "Đổi loại lỗi của vụ có Kết quả sửa bị chặn", blk);
        }

        // ─────────────────────────────────────────────────────────────────
        // [K5-VONG-SUA 2026-11-05] Vòng tròn K5 xét số cuối (seed 31983 PO4): chiếc sửa đạt không được thế chỗ chính chiếc đã bị NG
        Console.WriteLine("[S17] [K5-VONG-SUA] Nhận 1 → WTS NG 1 → sửa → KCS đạt: huỷ lần nhận gốc bị chặn (trước: lọt, sau đó huỷ KCS kẹt), gợi ý gỡ đi được");
        {
            var d = Seed(); var s = new HandoverService(d); var l = new HandoverLedgerService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var rid = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var w = await VuHelp.Wts(d, "GC", "NC1", null, 0, 1, Gc, 2);
            var k = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w.NgCaseId, 1, 0, "NC1", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Check(d, "S17 sau KCS");
            var tl0 = HandoverLedgerService.LoopTimeline((await l.GetInputForDetailAsync(1))!);
            Ok(k.Ok && x.Ok && tl0.Count == 0, "Đi xuôi (nhận → NG → sửa → KCS) không có ô âm theo thời gian", k.Error + x.Error + string.Join(",", tl0.Keys));
            var v = await s.VoidReceiveAsync(rid, "huy thu vong", 1, new[] { "KHO", "GC" });
            Ok(!v.Ok && v.Error.Contains("theo thứ tự thời gian") && v.Error.Contains("Lần KCS xác nhận"),
               "Huỷ lần nhận gốc bị chặn (K5 theo thời gian: chiếc sửa không thế chỗ chiếc đã NG), gợi ý có lần KCS xác nhận", v.Error);
            Ok(!d.HandoverTransactions.List.SelectMany(t => t.Receives).Single(r => r.ReceiveId == rid).IsVoided, "Bị chặn thì không ghi gì");
            // làm theo thứ tự gỡ: huỷ KCS xác nhận → huỷ Kết quả sửa → huỷ WTS NG → huỷ lần nhận
            var v1 = await s.VoidRepairCheckAsync(LastChk(d), "go vong", Kcs);
            var v2 = await s.VoidRepairResultAsync(LastRep(d), "go vong", Gc);
            d.WtsProductionLogs.List.Single(z => z.WtsLogId == w.WtsLogId).IsVoided = true;
            await Check(d, "S17 gỡ KCS + Kết quả sửa + WTS");
            var v3 = await s.VoidReceiveAsync(rid, "go vong", 1, new[] { "KHO", "GC" });
            await Check(d, "S17 huỷ lần nhận");
            Ok(v1.Ok && v2.Ok && v3.Ok, "Gỡ theo thứ tự (KCS → Kết quả sửa → WTS → lần nhận) lưu được hết", $"{v1.Error} | {v2.Error} | {v3.Error}");
        }

        Console.WriteLine("[S18] [K5-VONG-SUA] Vòng gửi nhóm khác sửa: GC NG → gửi HTSP sửa → HTSP trả ô Hàng lỗi đã xử lý → GC nhận: huỷ lần nhận gốc bị chặn");
        {
            var d = Seed(); var s = new HandoverService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var rid = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var w = await VuHelp.Wts(d, "GC", "NC1", null, 0, 1, Gc, 2);
            var g1 = await s.IssueByCellAsync(1, "GC", "HTSP", 1, Gc, "LOI", false, "SUA", ngCaseId: w.NgCaseId);
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Other);
            var g2 = await s.IssueByCellAsync(1, "HTSP", "GC", 1, Other, "SUA", false, "XL", null, "NC1", ngCaseId: w.NgCaseId);
            var g3 = await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            await Check(d, "S18 hàng sửa về GC");
            Ok(g1.Ok && g2.Ok && g3.Ok, "Gửi sửa + trả về + nhận ô Hàng lỗi đã xử lý lưu được", $"{g1.Error} | {g2.Error} | {g3.Error}");
            var v = await s.VoidReceiveAsync(rid, "huy thu vong", 1, new[] { "KHO", "GC" });
            Ok(!v.Ok && v.Error.Contains("theo thứ tự thời gian"), "Huỷ lần nhận gốc bị chặn (chiếc sửa trả về không thế chỗ chiếc đã NG)", v.Error);
        }

        Console.WriteLine("[S19] [K5-VONG-SUA] Không chặn oan: lần nhận sau thay lần nhận đầu khi hàng sửa về TRƯỚC lần nhận sau; sửa dòng WTS sau vòng; Workflow OFF");
        {
            var d = Seed(); var s = new HandoverService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 5, 0, null, Gc);
            var w = await VuHelp.Wts(d, "GC", "NC1", null, 3, 2, Gc, 2);   // 5 chiếc lần nhận 1: 3 OK + 2 NG
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w.NgCaseId, 2, 0, "NC1", null, Gc);
            await s.ConfirmRepairAsync(LastRep(d), 2, 0, null, Kcs);
            await s.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 5, 0, null, Gc);
            var rid2 = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var w2 = await VuHelp.Wts(d, "GC", "NC1", null, 2, 0, Gc, 0);   // 2 chiếc sửa đạt làm tiếp
            await Check(d, "S19 trước huỷ lần nhận 2");
            var v = await s.VoidReceiveAsync(rid2, "nhan nham lan 2", 1, new[] { "KHO", "GC" });
            await Check(d, "S19 sau huỷ lần nhận 2");
            Ok(v.Ok, "Huỷ lần nhận 2 (chưa dùng tới) lưu được — 5 chiếc lần 1: 3 OK + 2 NG sửa đạt rồi OK", v.Error);
            // sửa dòng WTS (huỷ + dòng mới giữ vụ, ghi sau lúc hàng sửa về): không bị chặn
            d.WtsProductionLogs.List.Single(z => z.WtsLogId == w.WtsLogId).IsVoided = true;
            var wn = await VuHelp.Wts(d, "GC", "NC1", null, 3, 2, Gc, 2, keepCase: w.NgCaseId);
            await Check(d, "S19 sửa dòng WTS giữ vụ");
            // [K5-VONG-SUA] (rà độc lập C1 / C2) xét theo thời gian chỉ khi HUỶ chứng từ nguồn: lưu mới sau khi sửa dòng WTS vẫn được
            var g19 = await s.IssueByCellAsync(1, "KHO", "GC", 3, 1, "MOI", false, "MOI");
            var n19 = await s.ReceiveAsync(LastTx(d), 3, 0, null, Gc);
            await Check(d, "S19 lưu mới sau sửa WTS");
            Ok(g19.Ok && n19.Ok, "Sau sửa dòng WTS: Kho giao + GC nhận thêm lưu được (không xét thời gian khi lưu mới)", g19.Error + n19.Error);
            var rid3 = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var v19 = await s.VoidReceiveAsync(rid3, "nhan nham", 1, new[] { "KHO", "GC" });
            Ok(v19.Ok, "Huỷ lần nhận vừa thêm (chưa dùng) lưu được — mức nền không chặn oan", v19.Error);
            // Workflow OFF: không xét thêm
            var dOff = Seed(workflow: false); var sOff = new HandoverService(dOff);
            await sOff.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await sOff.ReceiveAsync(LastTx(dOff), 1, 0, null, Gc);
            var ridO = dOff.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(dOff)).Receives.Single().ReceiveId;
            var wO = await VuHelp.Wts(dOff, "GC", "NC1", null, 0, 1, Gc, 2);
            await sOff.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, wO.NgCaseId, 1, 0, "NC1", null, Gc);
            await sOff.ConfirmRepairAsync(LastRep(dOff), 1, 0, null, Kcs);
            var vO = await sOff.VoidReceiveAsync(ridO, "huy off", 1, new[] { "KHO", "GC" });
            var tlO = HandoverLedgerService.LoopTimeline((await new HandoverLedgerService(dOff).GetInputForDetailAsync(1))!);
            Ok(vO.Ok && tlO.Count == 0, "Workflow OFF: không xét theo thời gian (N7), huỷ lưu được", vO.Error);
        }

        Console.WriteLine("[S20] Hàng lỗi cũ chưa có mã vụ (vụ 0, dữ liệu trước 28/10): ghi Kết quả sửa không chọn vụ → KCS đạt 1 + loại 1 → huỷ ngược lại");
        {
            var d = Seed(); var s = new HandoverService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 5, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 5, 0, null, Gc);
            // dòng WTS NG cũ không gắn vụ (như dữ liệu trước bản VULOI) → ô Hàng lỗi GC có 2 chiếc "hàng lỗi cũ chưa có mã"
            d.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = d.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = Gc,
                QtyDone = 2, QtyOk = 0, QtyNg = 2, NgReason = "loi cu", StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            d.Fixup();
            var b = await Check(d, "S20 trước");
            Ok(b.CaseHeld(0, "GC", HandoverCell.Loi) == 2, "GC giữ 2 hàng lỗi cũ (vụ 0)", $"{b.CaseHeld(0, "GC", HandoverCell.Loi)}");
            var sig0 = Sig(b);
            var over = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, null, 3, 0, "NC1", null, Gc);   // [GN-FORM1] vòng 3 C1b: vụ 0 ở Gia công mặc định như hàng mới (NC1)
            Ok(!over.Ok && over.Error.Contains("tối đa 2"), "Ghi vượt số hàng cũ (3 > 2) bị chặn theo trần vụ 0", over.Error);
            var nr = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, null, 2, 0, "NC3", null, Gc);
            Ok(!nr.Ok && nr.Error.Contains("lý do") && Sig((await new HandoverLedgerService(d).GetLedgerForDetailAsync(1))!) == sig0, "Hàng cũ chưa mã vụ: chọn NC3 (sau mặc định) không lý do bị chặn, không ghi gì", nr.Error);
            var k = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, null, 2, 0, "NC1", "sua hang cu", Gc, "hang cu lam lai tu NC1");   // [GN-FORM1] rà V2: lùi NC1 cần lý do
            var a = await Check(d, "S20 ghi");
            Ok(k.Ok && a.CaseHeld(0, "GC", HandoverCell.Loi) == 0 && a.G("GC").ChoKcs == 2, "Không chọn vụ: lấy 2 hàng cũ, Chờ KCS 2", k.Error);
            var hist = await s.GetRepairsAsync(1);
            Ok(hist.Count == 1 && hist[0].CaseLabel == "Hàng lỗi cũ (chưa có mã vụ)", "Lịch sử ghi 'Hàng lỗi cũ (chưa có mã vụ)'", hist.FirstOrDefault()?.CaseLabel);
            var x1 = await s.ConfirmRepairAsync(LastRep(d), 1, 1, "1 dat 1 loai", Kcs);
            var a2 = await Check(d, "S20 KCS");
            Ok(x1.Ok && a2.CaseHeld(0, "GC", HandoverCell.Loi) == 1 && a2.G("GC").ChoKcs == 0, "KCS đạt 1 (vào NC1) + loại 1 → về ô Hàng lỗi GC vụ 0", x1.Error);
            var vx = await s.VoidRepairCheckAsync(LastChk(d), "huy thu", Kcs);
            var vr = await s.VoidRepairResultAsync(LastRep(d), "huy thu", Gc);
            var a3 = await Check(d, "S20 huỷ");
            Ok(vx.Ok && vr.Ok && Sig(a3) == sig0, "Huỷ lần xác nhận rồi huỷ Kết quả sửa → sổ y như trước khi ghi", $"{vx.Error} | {vr.Error}");
        }

        Console.WriteLine("[S21] [K5-VONG-SUA] (rà độc lập V1) Kho: báo 3 phôi lỗi → giao hết GC → sửa 2 → KCS đạt 2; giảm phôi 20 → 18 bị chặn (phôi sửa không thế chỗ phôi đã giao)");
        {
            var d = Seed(); var s = new HandoverService(d); var kho = new KhoVatLieuService(d, new UserService(null!));
            await s.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var vu = d.HandoverNgReports.List.Last().NgCaseId;
            var g21 = await s.IssueByCellAsync(1, "KHO", "GC", 17, 1, "MOI", false, "MOI");
            var k = await s.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 2, 0, null, null, 1);
            var x = await s.ConfirmRepairAsync(LastRep(d), 2, 0, null, Kcs);
            var b = await Check(d, "S21 trước");
            Ok(g21.Ok && k.Ok && x.Ok && b.G("KHO").ConGiao == 2, "Kho còn giao 2 (phôi sửa đạt)", $"{g21.Error}{k.Error}{x.Error} còn giao {b.G("KHO").ConGiao}");
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, 18, "OVL", "A1", null, null, null, "giam phoi thu", 1);
            var a = (await new HandoverLedgerService(d).GetLedgerForDetailAsync(1))!;
            Ok(!r.Success && (r.Error ?? "").Contains("theo thứ tự thời gian") && a.Phoi == 20, "Giảm phôi 20 → 18 bị chặn (K5 theo thời gian), phôi giữ 20", r.Error);
            var r2 = await kho.SaveKhoInputAsync(1, DateTime.Now, 20, "OVL", "A1", null, null, null, "giu phoi", 1);
            Ok(r2.Success, "Lưu lại phôi 20 (không giảm) vẫn được", r2.Error);
        }

        // ─────────────────────────────────────────────────────────────────
        // [K5-VONG-HO-WTS 2026-11-05] Hở A2 của [K5-VONG-SUA]: xoá dòng WTS ở NC trước làm NC sau mất nguồn theo thời gian
        Console.WriteLine("[S22] [K5-VONG-HO-WTS] NC1 OK 1 → NC2 NG 1 → sửa → KCS đạt về NC2: xoá WTS NC1 bị chặn (NC2 đã làm chiếc đó trước lúc hàng sửa về); gỡ theo gợi ý được; xoá phần thừa không chặn oan");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = w2.LogId == null ? null : d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            var k = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Check(d, "S22 sau KCS");
            Ok(w1.Ok && w2.Ok && k.Ok && x.Ok, "Đi xuôi: WTS NC1 OK 1, WTS NC2 NG 1, Kết quả sửa đạt 1 về NC2, KCS xác nhận lưu được", $"{w1.Err} | {w2.Err} | {k.Error} | {x.Error}");
            var v = await WtsSvcHelp.VoidAsync(d, w1.LogId!.Value, 1, Gc);
            Ok(!v.Ok && (v.Err ?? "").Contains("theo thứ tự thời gian") && (v.Err ?? "").Contains("nên kiểm tra theo các bước sau"),
               "Xoá WTS NC1 bị chặn (K5 theo thời gian: chiếc sửa về NC2 không thế chỗ chiếc NC2 đã làm), lời báo có gợi ý gỡ", v.Err);
            Ok(!d.WtsProductionLogs.List.Single(z => z.WtsLogId == w1.LogId).IsVoided, "Bị chặn thì dòng WTS NC1 vẫn còn");
            var g1 = await s.VoidRepairCheckAsync(LastChk(d), "go vong", Kcs);
            var g2 = await s.VoidRepairResultAsync(LastRep(d), "go vong", Gc);
            var g3 = await WtsSvcHelp.VoidAsync(d, w2.LogId!.Value, 1, Gc);
            var g4 = await WtsSvcHelp.VoidAsync(d, w1.LogId!.Value, 1, Gc);
            await Check(d, "S22 gỡ xong");
            Ok(g1.Ok && g2.Ok && g3.Ok && g4.Ok, "Gỡ theo thứ tự (KCS → Kết quả sửa → WTS NC2 → WTS NC1) lưu được hết", $"{g1.Error} | {g2.Error} | {g3.Err} | {g4.Err}");

            // không chặn oan: NC1 làm 2 (2 dòng), NC2 NG 1 → sửa về NC2; xoá 1 dòng NC1 (chiếc chưa ai dùng) vẫn được
            var e = Seed(); var se = new HandoverService(e); WtsSvcHelp.Reset();
            await se.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI");
            await se.ReceiveAsync(LastTx(e), 2, 0, null, Gc);
            var e1 = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC1", null, 1, 0, Gc);
            var e2 = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC1", null, 1, 0, Gc);
            var e3 = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu3 = e.WtsProductionLogs.List.Single(z => z.WtsLogId == e3.LogId).NgCaseId;
            await se.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu3, 1, 0, "NC2", null, Gc);
            await se.ConfirmRepairAsync(LastRep(e), 1, 0, null, Kcs);
            var ve = await WtsSvcHelp.VoidAsync(e, e2.LogId!.Value, 1, Gc);
            await Check(e, "S22 xoá dòng thừa");
            Ok(e1.Ok && e2.Ok && e3.Ok && ve.Ok, "Xoá 1 dòng NC1 OK 1 (NC2 chỉ dùng 1 chiếc trước lúc hàng sửa về) lưu được — không chặn oan", ve.Err);
        }

        // [WTS-SUA-GIO-GOC 2026-11-05] Hở A4: sửa dòng WTS (huỷ + dòng mới giờ mới) che phần OK có từ trước trong ảnh sổ theo thời gian
        Console.WriteLine("[S23] [WTS-SUA-GIO-GOC] Sửa dòng WTS NC1 SAU lúc hàng sửa về: ảnh sổ theo thời gian không âm giả; xoá dòng đã sửa vẫn bị chặn (trước: lọt)");
        {
            var d = Seed(); var s = new HandoverService(d); var l = new HandoverLedgerService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(20);
            var ed = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc, editLogId: w1.LogId);   // sửa dòng (giữ số, như sửa giờ / ghi chú)
            await Check(d, "S23 sau sửa dòng");
            var old = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w1.LogId);
            var neu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == ed.LogId);
            Ok(ed.Ok && old.IsVoided && old.VoidReason == "Sửa lại" && old.VoidedAt == neu.CreatedAt,
               "Sửa dòng: dòng cũ huỷ 'Sửa lại', VoidedAt dòng cũ = CreatedAt dòng mới (1 giờ máy chủ chung)", $"{ed.Err} voidedAt {old.VoidedAt:HH:mm:ss.fffffff} created {neu.CreatedAt:HH:mm:ss.fffffff}");
            var inp = (await l.GetInputForDetailAsync(1))!;
            var saved = inp.Wts.Single(z => z.Id == ed.LogId).Saved;
            Ok(saved == old.CreatedAt, "Sổ: dòng sửa mang giờ lưu GỐC của dòng cũ", $"{saved:HH:mm:ss.fffffff} / gốc {old.CreatedAt:HH:mm:ss.fffffff}");
            var tl = HandoverLedgerService.LoopTimeline(inp);
            Ok(tl.Count == 0, "Ảnh sổ theo thời gian không âm giả ở NC2 (trước: NC1 OK bị coi là ghi sau lúc hàng sửa về → NC2 −1)", string.Join(", ", tl.Select(z => z.Key + "=" + z.Value.Value)));
            var v = await WtsSvcHelp.VoidAsync(d, ed.LogId!.Value, 1, Gc);
            Ok(!v.Ok && (v.Err ?? "").Contains("theo thứ tự thời gian"), "Xoá dòng NC1 đã sửa bị chặn như dòng chưa sửa (trước: âm giả làm mức nền thấp → lọt)", v.Err);
        }

        Console.WriteLine("[S24] [WTS-SUA-GIO-GOC] EditOrigins: chỉ ghép đúng giờ, chuỗi sửa nhiều lần, trùng giờ, không ghép khi đổi NC / lệch giờ / dòng mới khác trong cùng lần lưu / dòng thay đã xoá");
        {
            var t0 = new DateTime(2026, 11, 5, 8, 0, 0);
            HandoverLedgerService.WtsEditNode N(long id, DateTime c, DateTime? v = null, string nc = "NC1", int w = 2) => new(id, 1, w, "GC", nc, null, c, v);
            // chuỗi: #1 (8:00) sửa lúc 9:00 → #2 (9:00) sửa lúc 10:00 → #3 (10:00, còn hiệu lực)
            var o1 = HandoverLedgerService.EditOrigins(new[] { N(3, t0.AddHours(2)) }, new[] { N(1, t0, t0.AddHours(1)), N(2, t0.AddHours(1), t0.AddHours(2)) });
            Ok(o1.TryGetValue(3, out var g3) && g3 == t0, "Chuỗi sửa 2 lần: dòng cuối mang giờ gốc 8:00", string.Join(",", o1));
            // [WTS-SUA-GIO-GOC-EXACT] chỉ ghép ĐÚNG giờ: lệch 0,4 giây / 5 giây đều không ghép (trước: ≤ 3 giây ghép — ghép nhầm dữ liệu mới, rà độc lập Cao)
            var o2 = HandoverLedgerService.EditOrigins(new[] { N(5, t0.AddHours(1).AddMilliseconds(400)), N(7, t0.AddHours(2).AddSeconds(5)) },
                                                       new[] { N(4, t0, t0.AddHours(1)), N(6, t0, t0.AddHours(2)) });
            Ok(o2.Count == 0, "Lệch 0,4 giây / 5 giây: không ghép (chỉ ghép đúng giờ — dữ liệu sửa trước bản này tính như master cũ)", string.Join(",", o2));
            // trùng giờ (cột datetime làm tròn): 2 dòng cũ + 2 dòng mới cùng khoá, cùng giờ → cũ Id tăng dần ↔ mới Id tăng dần
            var o5 = HandoverLedgerService.EditOrigins(new[] { N(31, t0.AddHours(3)), N(30, t0.AddHours(3)) },
                                                       new[] { N(21, t0.AddHours(2), t0.AddHours(3)), N(20, t0, t0.AddHours(3)) });
            Ok(o5.TryGetValue(30, out var g30) && g30 == t0 && o5.TryGetValue(31, out var g31) && g31 == t0.AddHours(2),
               "Trùng giờ: dòng mới Id nhỏ nhận dòng cũ Id nhỏ (không ghép chéo)", string.Join(",", o5));
            // dòng thay A' bị xoá sau đó: dòng C mới cùng khoá (lưu lệch vài ms) KHÔNG nhận giờ gốc của A
            var o6 = HandoverLedgerService.EditOrigins(new[] { N(42, t0.AddHours(1).AddMilliseconds(3)) }, new[] { N(40, t0, t0.AddHours(1)) });
            Ok(o6.Count == 0, "Dòng thay đã xoá: dòng mới khác cùng khoá không nhận giờ gốc (rà độc lập Cao, biến thể xoá A')", string.Join(",", o6));
            // [WTS-SUA-PHAN] chuỗi #50 (8:00 OK 1 NG 1) → #51 (9:00 OK 2 NG 0) → #52 (10:00 OK 3 NG 1): giờ gốc 8:00 + đủ 3 bản cũ → mới
            HandoverLedgerService.WtsEditNode Q(long id, DateTime c, DateTime? v, decimal ok, decimal ng) => new(id, 1, 2, "GC", "NC1", null, c, v, ok, ng);
            var o7 = HandoverLedgerService.EditOriginsFull(new[] { Q(52, t0.AddHours(2), null, 3, 1) }, new[] { Q(50, t0, t0.AddHours(1), 1, 1), Q(51, t0.AddHours(1), t0.AddHours(2), 2, 0) });
            Ok(o7.TryGetValue(52, out var e7) && e7.Origin == t0
               && e7.Vers.SequenceEqual(new[] { new WtsVer(t0, 1, 1), new WtsVer(t0.AddHours(1), 2, 0), new WtsVer(t0.AddHours(2), 3, 1) }),
               "EditOriginsFull: giờ gốc 8:00, đủ 3 bản (giờ, OK, NG) cũ → mới", e7 == null ? "không ghép" : string.Join(",", e7.Vers));
            // SnapshotBefore với các bản: mốc 8:30 → bản 8:00 có hiệu lực, OK = min(1,2,3) = 1, đã làm = min(2,2,4) = 2 → OK 1 NG 1;
            //   mốc 9:30 → bản 9:00, OK min(2,3) = 2, đã làm min(2,4) = 2 → OK 2 NG 0; mốc 7:00 → chưa có bản nào
            var wv = new InWts(52, t0, "GC", "NC1", null, 3, 1, null, null, t0, e7?.Vers);
            var si = new LedgerInput { Wts = new List<InWts> { wv } };
            var sv1 = HandoverLedgerService.SnapshotBefore(si, t0.AddMinutes(30)).Wts.SingleOrDefault();
            var sv2 = HandoverLedgerService.SnapshotBefore(si, t0.AddMinutes(90)).Wts.SingleOrDefault();
            var sv3 = HandoverLedgerService.SnapshotBefore(si, t0.AddHours(-1)).Wts.SingleOrDefault();
            Ok(sv1 != null && sv1.Ok == 1 && sv1.Ng == 1 && sv2 != null && sv2.Ok == 2 && sv2.Ng == 0 && sv3 == null,
               "SnapshotBefore: lấy bản đang có hiệu lực ở mốc + min từ bản đó về sau (8:30 → OK 1 NG 1; 9:30 → OK 2 NG 0; 7:00 → chưa có)",
               $"8:30 {sv1?.Ok}/{sv1?.Ng} · 9:30 {sv2?.Ok}/{sv2?.Ng} · 7:00 {(sv3 == null ? "không" : "có")}");
            // đổi NC khi sửa → không ghép; người khác → không ghép
            var o3 = HandoverLedgerService.EditOrigins(new[] { N(9, t0.AddHours(1), nc: "NC2"), N(10, t0.AddHours(1), w: 3) }, new[] { N(8, t0, t0.AddHours(1)) });
            Ok(o3.Count == 0, "Sửa đổi NC / dòng của người khác: không ghép (tính như dòng mới — phía chặt)", string.Join(",", o3));
            // cùng lần lưu: dòng sửa (đúng giờ) + dòng mới khác cùng NC 2 ms sau → chỉ dòng sửa ghép
            var o4 = HandoverLedgerService.EditOrigins(new[] { N(12, t0.AddHours(1)), N(13, t0.AddHours(1).AddMilliseconds(2)) }, new[] { N(11, t0, t0.AddHours(1)) });
            Ok(o4.ContainsKey(12) && !o4.ContainsKey(13), "Dòng mới khác trong cùng lần lưu không lấy nhầm giờ gốc (ghép đúng giờ trước)", string.Join(",", o4));
        }

        // [K5-VONG-HO-NHAN 2026-11-05] Hở A3 ghi ở [K5-VONG-SUA] ("lần nhận ghi muộn trong cùng nhóm nhận hàng sửa"): kiểm bằng hàm thật.
        //   Mọi chứng từ dùng hàng (WTS, phiếu giao) chỉ lưu được SAU lần nhận cấp hàng cho nó (trần L3 / L4 xét lúc lưu) → nhận ghi muộn
        //   thì chứng từ dùng nó cũng muộn theo, ảnh sổ theo thời gian vẫn khớp nhau: không lọt vòng tròn. Ca dưới: phiếu B giao TRƯỚC lúc
        //   hàng sửa về nhưng GC bấm Nhận SAU → huỷ lần nhận gốc A vẫn bị chặn (chiếc NG là chiếc của A), và khi gỡ đúng thứ tự thì được.
        Console.WriteLine("[S25] [K5-VONG-HO-NHAN] Lần nhận ghi muộn (phiếu giao trước, bấm Nhận sau lúc hàng sửa về): huỷ lần nhận gốc vẫn bị chặn đúng, gỡ được; huỷ lần nhận muộn chưa dùng thì được");
        {
            var d = Seed(); var s = new HandoverService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var ridA = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var txB = await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");   // phiếu B giao trước lúc hàng sửa về
            var txBId = LastTx(d);
            var w = await VuHelp.Wts(d, "GC", "NC1", null, 0, 1, Gc, 2);
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w.NgCaseId, 1, 0, "NC1", null, Gc);
            await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            var rB = await s.ReceiveAsync(txBId, 1, 0, null, Gc);   // GC bấm Nhận phiếu B muộn (sau lúc hàng sửa về)
            var ridB = d.HandoverTransactions.List.Single(t => t.HandoverTxId == txBId).Receives.Single().ReceiveId;
            await Check(d, "S25 nhận muộn");
            var vA = await s.VoidReceiveAsync(ridA, "huy thu", 1, new[] { "KHO", "GC" });
            Ok(txB.Ok && rB.Ok && !vA.Ok && vA.Error.Contains("theo thứ tự thời gian"),
               "Huỷ lần nhận gốc A bị chặn (chiếc NG là chiếc của A — lần nhận B ghi muộn không thế chỗ được)", vA.Error);
            var vB = await s.VoidReceiveAsync(ridB, "nhan nham", 1, new[] { "KHO", "GC" });
            await Check(d, "S25 huỷ lần nhận muộn");
            Ok(vB.Ok, "Huỷ lần nhận muộn B (chưa ai dùng) lưu được — không chặn oan", vB.Error);
        }

        // [WTS-SUA-GIO-GOC-EXACT 2026-11-05] ca dò P1 của rà độc lập lần 1 (Cao) — trước bản vá: C nhận giờ gốc của A, xoá D lọt
        Console.WriteLine("[S26] [WTS-SUA-GIO-GOC-EXACT] Sửa dòng A đổi PO + thêm dòng C cùng PO / NC cũ trong 1 lần lưu: C giữ giờ lưu thật, xoá D (nguồn NC1 trước lúc hàng sửa về) bị chặn");
        {
            var d = Seed(); WtsSvcHelp.Reset();
            var plan = d.KhPlans.List[0];
            var d2 = new KhPlanDetail { KhPlanDetailId = 2, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO2", Quantity = 20, LineNo = 2 };
            d.KhPlanDetails.List.Add(d2); plan.Details.Add(d2);
            d.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 2, PlanDetailId = 2, IsActive = true, SoLuongThucNhan = 20, NgayNhanPhoi = DateTime.Now, SoPO = "PO2", PartNo = "P" });
            d.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 11, KhPlanDetailId = 2, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
            d.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 12, KhPlanDetailId = 2, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
            d.Fixup();
            var s = new HandoverService(d); var l = new HandoverLedgerService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            await s.IssueByCellAsync(2, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var rr = await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var a = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            await Task.Delay(10);
            var dd = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var b = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == b.LogId).NgCaseId;
            var k = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(20);
            // 1 lần lưu: dòng A (sửa, đổi PO1 → PO2) + dòng C (mới, PO1, cùng NC1)
            var grp = WtsSvcHelp.Build(2, "GC", "NC1", null, 1, 0, Gc, null, null, a.LogId, "MX");
            var pr0 = grp.Rows[0]; pr0.StartTime = DateTime.Now.AddMinutes(-30); pr0.EndTime = DateTime.Now.AddMinutes(-20);
            var g2 = WtsSvcHelp.Build(1, "GC", "NC1", null, 1, 0, Gc, null, null, null, "MX");
            var r1 = g2.Rows[0]; r1.StartTime = DateTime.Now.AddMinutes(-10); r1.EndTime = DateTime.Now.AddMinutes(-5);
            grp.Rows.Add(r1);
            var res = await new WtsSaveService(d, new HandoverLedgerService(d)).SaveGroupAsync(grp); d.Fixup();
            Ok(a.Ok && dd.Ok && b.Ok && k.Ok && x.Ok && rr.Ok && res.Ok, "S26 dựng xong", $"{a.Err}|{dd.Err}|{b.Err}|{k.Error}|{x.Error}|{rr.Error}|{res.ErrorMsg}");
            var cId = r1.WtsLogId!.Value;
            var oldA = d.WtsProductionLogs.List.Single(z => z.WtsLogId == a.LogId);
            var cRow = d.WtsProductionLogs.List.Single(z => z.WtsLogId == cId);
            var inp = (await l.GetInputForDetailAsync(1))!;
            var cSaved = inp.Wts.Single(z => z.Id == cId).Saved;
            Console.WriteLine($"   C created {cRow.CreatedAt:HH:mm:ss.fff}, C.Saved trong sổ {cSaved:HH:mm:ss.fff}, A gốc {oldA.CreatedAt:HH:mm:ss.fff}, A voided {oldA.VoidedAt:HH:mm:ss.fff}");
            Ok(cSaved == cRow.CreatedAt, "C (dòng mới) giữ giờ lưu thật — không ghép nhầm với dòng A đổi PO", $"C.Saved={cSaved:HH:mm:ss.fff}");
            var v = await WtsSvcHelp.VoidAsync(d, dd.LogId!.Value, 1, Gc);
            Ok(!v.Ok, "Xoá D (nguồn NC1 duy nhất trước lúc hàng sửa về) phải bị chặn theo thời gian", v.Err ?? "(xoá được)");
        }

        // [K5-VONG-HO-WTS-SUA 2026-11-05] ca dò P2 của rà độc lập lần 1 (Vừa 3) — trước bản vá: xoá bị chặn nhưng sửa OK → NG lọt
        Console.WriteLine("[S27] [K5-VONG-HO-WTS-SUA] Như S22 nhưng SỬA dòng WTS NC1 (OK 1 → NG 1 / đổi NC): bị chặn như xoá; sửa giữ số / sửa phần thừa không chặn oan");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            var v = await WtsSvcHelp.VoidAsync(d, w1.LogId!.Value, 1, Gc);
            var ed = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 0, 1, Gc, catId: 2, editLogId: w1.LogId);
            Ok(!v.Ok && !ed.Ok, "Xoá bị chặn thì sửa OK→NG cũng phải bị chặn", $"xoá ok={v.Ok}; sửa ok={ed.Ok} {ed.Err}");
            Ok((ed.Err ?? "").Contains("theo thứ tự thời gian") && (ed.Err ?? "").Contains("nên kiểm tra theo các bước sau"), "Lời báo sửa bị chặn có lý do theo thời gian + gợi ý gỡ", ed.Err);
            Ok(!d.WtsProductionLogs.List.Single(z => z.WtsLogId == w1.LogId).IsVoided, "Bị chặn thì dòng WTS NC1 vẫn còn (không huỷ dở)");
            var en = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC3", null, 1, 0, Gc, editLogId: w1.LogId);   // đổi NC1 → NC3 (khác khoá = như xoá NC1)
            Ok(!en.Ok, "Sửa đổi NC1 → NC3 cũng bị chặn (khác khoá = xoá dòng NC1 + dòng mới)", en.Err);
            var same = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc, editLogId: w1.LogId);   // giữ số (sửa giờ / ghi chú)
            await Check(d, "S27 sửa giữ số");
            Ok(same.Ok, "Sửa dòng NC1 giữ nguyên số: lưu được (dòng thay mang giờ gốc — không chặn oan)", same.Err);

            // phần thừa: NC1 làm 2 trong 1 dòng, NC2 NG 1 → sửa về NC2; sửa dòng NC1 2 → 1 (chiếc chưa ai dùng) được
            var e = Seed(); var se = new HandoverService(e); WtsSvcHelp.Reset();
            await se.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI");
            await se.ReceiveAsync(LastTx(e), 2, 0, null, Gc);
            var e1 = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC1", null, 2, 0, Gc);
            var e3 = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu3 = e.WtsProductionLogs.List.Single(z => z.WtsLogId == e3.LogId).NgCaseId;
            await se.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu3, 1, 0, "NC2", null, Gc);
            await se.ConfirmRepairAsync(LastRep(e), 1, 0, null, Kcs);
            var ee = await WtsSvcHelp.SaveAsync(e, 1, "GC", "NC1", null, 1, 0, Gc, editLogId: e1.LogId);
            await Check(e, "S27 sửa phần thừa");
            Ok(e1.Ok && e3.Ok && ee.Ok, "Sửa dòng NC1 2 → 1 (NC2 chỉ dùng 1 chiếc trước lúc hàng sửa về): lưu được — không chặn oan", ee.Err);
        }

        // [K5-VONG-HO-WTS 2026-11-05] Rà độc lập lần 1 (Thấp 6): bước gỡ "xoá dòng WTS" trong lời khuyên phải xét theo thời gian như máy chủ.
        //   Huỷ lần nhận A (S22): bỏ xét thời gian ở bước gỡ thì gợi ý chỉ "xoá WTS NC1" — máy chủ lại chặn bước đó (vòng tròn NC2).
        Console.WriteLine("[S28] [K5-VONG-HO-WTS] Lời khuyên gỡ khi huỷ lần nhận gốc: bước xoá WTS NC1 đi SAU xoá WTS NC2 + KCS + Kết quả sửa (bước gỡ cũng xét theo thời gian)");
        {
            var d = Seed(); var s = new HandoverService(d); var l = new HandoverLedgerService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var ridA = d.HandoverTransactions.List.Single(t => t.HandoverTxId == LastTx(d)).Receives.Single().ReceiveId;
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            var inp = (await l.GetInputForDetailAsync(1))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidReceive(ridA), new HashSet<string> { "R:" + ridA }, loopOp: true);
            int i1 = plan.Steps.FindIndex(z => z.Kind == "WTS" && z.Id == w1.LogId), i2 = plan.Steps.FindIndex(z => z.Kind == "WTS" && z.Id == w2.LogId);
            Ok(plan.Complete && i1 >= 0 && i2 >= 0 && i2 < i1 && plan.Steps.Count >= 4,
               "Lời khuyên đủ bước, xoá WTS NC2 trước WTS NC1 (bước gỡ xét thời gian như máy chủ)", string.Join(" → ", plan.Steps.Select(z => z.Kind + "#" + z.Id)));
        }
        // [K5-VONG-HO-WTS 2026-11-05] ca dò Q1 của rà độc lập lần 2: phân biệt được cờ "WTS" ở bước gỡ Plan (bỏ cờ → Plan chỉ gợi ý xoá WTS NC2, máy chủ chặn bước đó)
        Console.WriteLine("[S29] [K5-VONG-HO-WTS] Bước gỡ xoá WTS NC2 tự nó vướng theo thời gian: lời khuyên xếp KCS xác nhận / Kết quả sửa / WTS NC3 trước, làm theo từng bước máy chủ không chặn");
        {
            var d = Seed(); var s = new HandoverService(d); var l = new HandoverLedgerService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 1, 0, Gc);
            var w3 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC3", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w3.LogId).NgCaseId;
            var k = await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC3", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(20);
            d.WtsProductionLogs.List.Single(z => z.WtsLogId == w1.LogId).CreatedAt = DateTime.Now;   // dữ liệu cũ: dòng NC1 có giờ lưu sau lúc hàng sửa về (vd sửa trước bản này)
            Ok(w1.Ok && w2.Ok && w3.Ok && k.Ok && x.Ok, "S29 dựng xong", $"{w1.Err}|{w2.Err}|{w3.Err}|{k.Error}|{x.Error}");
            var inp = (await l.GetInputForDetailAsync(1))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpWts(new[] { w1.LogId!.Value }), new HashSet<string> { "W:" + w1.LogId }, loopOp: true);
            var v0 = await WtsSvcHelp.VoidAsync(d, w1.LogId!.Value, 1, Gc);
            Ok(!v0.Ok, "Xoá W1 bị chặn (K5 số cuối NC2)", v0.Err);
            bool allOk = true; string why = "";
            foreach (var st in plan.Steps)
            {
                bool ok2; string? er;
                if (st.Kind == "WTS") { var r = await WtsSvcHelp.VoidAsync(d, st.Id, 1, Gc); ok2 = r.Ok; er = r.Err; }
                else if (st.Kind == "XNSUA") { var r = await s.VoidRepairCheckAsync(st.Id, "go vong", Kcs); ok2 = r.Ok; er = r.Error; }
                else if (st.Kind == "KQSUA") { var r = await s.VoidRepairResultAsync(st.Id, "go vong", Gc); ok2 = r.Ok; er = r.Error; }
                else { ok2 = false; er = "khac " + st.Kind; }
                if (!ok2) { allOk = false; why = $"{st.Kind}#{st.Id}: {er}"; break; }
            }
            Ok(allOk, "Làm theo từng bước gợi ý: máy chủ không chặn bước nào", why);
            if (allOk) { var v1 = await WtsSvcHelp.VoidAsync(d, w1.LogId!.Value, 1, Gc); Ok(v1.Ok, "Xoá W1 sau khi gỡ: được", v1.Err); }
        }

        // [WTS-SUA-PHAN 2026-11-05] ca dò Q2 của rà độc lập lần 2 (Cao): sửa tăng số bị chặn oan khi cả dòng mang giờ gốc
        Console.WriteLine("[S30] [WTS-SUA-PHAN] Sửa TĂNG OK dòng NC1 (1 → 2) bằng hàng nhận SAU lúc hàng sửa về: lưu được như thêm dòng mới; sổ nạp lại: phần có từ giờ gốc = 1");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var w1 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(10);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var r2 = await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var neu = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);   // đối chứng: thêm dòng mới thì được
            var v = await WtsSvcHelp.VoidAsync(d, neu.LogId!.Value, 1, Gc);
            var ed = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc, editLogId: w1.LogId);   // cùng việc đó nhưng sửa dòng cũ 1 → 2
            Ok(x.Ok && r2.Ok && neu.Ok && v.Ok, "S30 dựng xong (thêm dòng mới OK 1 được, xoá lại được)", $"{x.Error}|{r2.Error}|{neu.Err}|{v.Err}");
            Ok(ed.Ok, "Sửa dòng NC1 OK 1 → 2 (chiếc thứ 2 nhận sau lúc hàng sửa về) phải lưu được", ed.Err);
            await Check(d, "S30 sửa tăng");
            var inp2 = (await new HandoverLedgerService(d).GetInputForDetailAsync(1))!;
            var wn = inp2.Wts.Single(z => z.Id == ed.LogId);
            Ok(wn.Vers is { Count: 2 } && wn.Vers[0].Ok == 1 && wn.Vers[1].Ok == 2 && wn.Saved == wn.Vers[0].At && wn.Vers[0].At < wn.Vers[1].At,
               "Sổ nạp lại: dòng sửa mang giờ gốc + 2 bản (OK 1 lúc gốc, OK 2 lúc sửa)", wn.Vers == null ? "không có bản" : string.Join(",", wn.Vers));
            Ok(HandoverLedgerService.LoopTimeline(inp2).Count == 0, "Ảnh sổ theo thời gian không âm sau khi sửa tăng", string.Join(",", HandoverLedgerService.LoopTimeline(inp2).Select(z => z.Key + "=" + z.Value.Value)));
            var vg = await WtsSvcHelp.VoidAsync(d, ed.LogId!.Value, 1, Gc);
            Ok(!vg.Ok && (vg.Err ?? "").Contains("theo thứ tự thời gian"), "Xoá dòng đã sửa tăng vẫn bị chặn theo thời gian (phần OK 1 có từ giờ gốc NC2 đã dùng)", vg.Err);
        }
        // [K5-VONG-HO-WTS-SUA 2026-11-05] ca dò Q3 của rà độc lập lần 2
        Console.WriteLine("[S31] [K5-VONG-HO-WTS-SUA] 2 dòng cùng NC sửa giữ số trong 1 lần lưu + sửa dòng NG giữ vụ (PO có hàng sửa về): không chặn oan");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            var a = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var b = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var w2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 1, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            var grp = WtsSvcHelp.Build(1, "GC", "NC1", null, 1, 0, Gc, null, null, a.LogId, "MX");
            grp.Rows[0].StartTime = DateTime.Now.AddMinutes(-30); grp.Rows[0].EndTime = DateTime.Now.AddMinutes(-20);
            var g2 = WtsSvcHelp.Build(1, "GC", "NC1", null, 1, 0, Gc, null, null, b.LogId, "MX");
            g2.Rows[0].StartTime = DateTime.Now.AddMinutes(-10); g2.Rows[0].EndTime = DateTime.Now.AddMinutes(-5); grp.Rows.Add(g2.Rows[0]);
            var res = await new WtsSaveService(d, new HandoverLedgerService(d)).SaveGroupAsync(grp); d.Fixup();
            Ok(x.Ok && res.Ok, "2 dòng NC1 sửa giữ số cùng 1 lần lưu: được", $"{x.Error}|{res.ErrorMsg}");
            var e2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 1, 1, Gc, catId: 2, keepCase: vu, editLogId: w2.LogId);
            Ok(e2.Ok, "Sửa dòng NC2 OK 1 NG 1 giữ vụ (vụ đã sửa xong): được", e2.Err);
        }
        // [WTS-SUA-PHAN 2026-11-05] ca dò Q4 của rà độc lập lần 3 (Cao 1): min qua cả chuỗi gán giờ gốc → chặn oan
        Console.WriteLine("[S32] [WTS-SUA-PHAN] Chuỗi sửa: A OK 1 → OK 2 TRƯỚC lúc hàng sửa về → sửa giữ số SAU lúc đó: lưu được, ảnh sổ không âm");
        {
            var d = Seed(); var s = new HandoverService(d); var l = new HandoverLedgerService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            var a = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            await Task.Delay(5);
            var a2 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc, editLogId: a.LogId);   // sửa tăng trước lúc hàng sửa về
            var b = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 1, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == b.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(10);
            var inp0 = (await l.GetInputForDetailAsync(1))!;
            Ok(a.Ok && a2.Ok && b.Ok && x.Ok && HandoverLedgerService.LoopTimeline(inp0).Count == 0, "S32 dựng xong, ảnh sổ không âm", $"{a2.Err}|{b.Err}|{x.Error}|tl={HandoverLedgerService.LoopTimeline(inp0).Count}");
            var a3 = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc, editLogId: a2.LogId);   // chỉ sửa giờ / ghi chú, giữ số
            Ok(a3.Ok, "Sửa giữ số dòng A' (OK 2 có từ trước lúc hàng sửa về) phải lưu được", a3.Err);
            if (a3.Ok) { var inp = (await l.GetInputForDetailAsync(1))!; var tl = HandoverLedgerService.LoopTimeline(inp);
                Ok(tl.Count == 0, "Sổ nạp lại: ảnh sổ trước lúc hàng sửa về không âm", string.Join(",", tl.Select(z => z.Key + "=" + z.Value.Value))); }
        }
        // [WTS-SUA-PHAN 2026-11-05] ca dò Q5 + Q5b của rà độc lập lần 3 (Cao 2): min từng số khi đổi NG → OK làm tổng lúc gốc nhỏ đi → lọt
        Console.WriteLine("[S33] [WTS-SUA-PHAN] Sửa A OK 1 NG 1 → OK 2 NG 0 (sau lúc hàng sửa về) rồi huỷ lần nhận B (trước lúc đó): vẫn bị chặn như khi không sửa");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var tB = LastTx(d); await s.ReceiveAsync(tB, 1, 0, null, Gc);
            var ridB = d.HandoverTransactions.List.Single(t => t.HandoverTxId == tB).Receives.Single().ReceiveId;
            var a = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 1, Gc, catId: 2);
            var b = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var c = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == c.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(10);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var rr = await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var ed = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 2, 0, Gc, editLogId: a.LogId);   // A: 1 NG đổi thành OK (2 chiếc vẫn làm lúc gốc)
            Ok(a.Ok && b.Ok && c.Ok && x.Ok && rr.Ok && ed.Ok, "S33 dựng xong", $"{a.Err}|{b.Err}|{c.Err}|{x.Error}|{rr.Error}|{ed.Err}");
            var v = await s.VoidReceiveAsync(ridB, "huy thu", 1, new[] { "KHO", "GC" });
            Ok(!v.Ok, "Huỷ lần nhận 1 chiếc (trước lúc hàng sửa về NC1 đã làm 3 chiếc từ 3 chiếc nhận) phải bị chặn theo thời gian", v.Error ?? "(huỷ được)");
        }
        Console.WriteLine("[S33b] [WTS-SUA-PHAN] Đối chứng S33: không sửa A → huỷ lần nhận B bị chặn");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI"); await s.ReceiveAsync(LastTx(d), 2, 0, null, Gc);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var tB = LastTx(d); await s.ReceiveAsync(tB, 1, 0, null, Gc);
            var ridB = d.HandoverTransactions.List.Single(t => t.HandoverTxId == tB).Receives.Single().ReceiveId;
            var a = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 1, Gc, catId: 2);
            var b = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 1, 0, Gc);
            var c = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == c.LogId).NgCaseId;
            await s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, "NC2", null, Gc);
            var x = await s.ConfirmRepairAsync(LastRep(d), 1, 0, null, Kcs);
            await Task.Delay(10);
            await s.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI"); var rr = await s.ReceiveAsync(LastTx(d), 1, 0, null, Gc);
            var ed = (Ok: true, Err: (string?)null, LogId: (long?)null);   // KHÔNG sửa   // A: 1 NG đổi thành OK (2 chiếc vẫn làm lúc gốc)
            Ok(a.Ok && b.Ok && c.Ok && x.Ok && rr.Ok && ed.Ok, "S33b dựng xong", $"{a.Err}|{b.Err}|{c.Err}|{x.Error}|{rr.Error}|{ed.Err}");
            var v = await s.VoidReceiveAsync(ridB, "huy thu", 1, new[] { "KHO", "GC" });
            Ok(!v.Ok, "Huỷ lần nhận 1 chiếc (trước lúc hàng sửa về NC1 đã làm 3 chiếc từ 3 chiếc nhận) phải bị chặn theo thời gian", v.Error ?? "(huỷ được)");
        }
        // ─────────────────────────────────────────────────────────────────
        // [K5-CLOSE-NHIEU 2026-11-06] SQL 10 HandoverTxCloses: mỗi lần huỷ phần chưa nhận 1 dòng — ca đúng lỗi fuzz seed 602 PO5 (05/11):
        //   huỷ phần chưa nhận → (hàng sửa về Kho) → huỷ 1 lần nhận → huỷ phần chưa nhận lần 2. Bản cũ: 1 ClosedAt cho cả 8 chiếc đã huỷ →
        //   ảnh sổ lúc hàng sửa về coi 8 chiếc còn giao (Kho −6) → chặn oan. Nay chỉ 2 chiếc của lần 2 là sau mốc đó.
        Console.WriteLine("[S34] [K5-CLOSE-NHIEU] Huỷ phần chưa nhận 2 lần trên 1 phiếu, giữa 2 lần có hàng sửa về Kho: lần 2 lưu được (bản cũ chặn oan Kho −6); mỗi lần 1 dòng, dòng B 2 bước, Lịch sử 2 lần");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            var kho = new List<string> { "KHO" };
            var b0 = await s.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, 1, kho, ngCategoryId: 1);
            var vu = d.HandoverNgReports.List.Last().NgCaseId;
            var i1 = await s.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", toNC: "NC1"); var t1 = LastTx(d);
            var ra = await s.ReceiveAsync(t1, 2, 0, null, Gc);
            var rb = await s.ReceiveAsync(t1, 2, 0, null, Gc);
            var ridB = d.HandoverTransactions.List.Single(t => t.HandoverTxId == t1).Receives.Where(r => !r.IsVoided).Max(r => r.ReceiveId);
            await Task.Delay(5);
            var cl1 = await s.CloseRemainderAsync(t1, "huy lan 1", 1, kho);
            var i2 = await s.IssueByCellAsync(1, "KHO", "GC", 14, 1, "MOI", false, "MOI", toNC: "NC1");
            var r2 = await s.ReceiveAsync(LastTx(d), 14, 0, null, Gc);
            await Task.Delay(5);
            var k = await s.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 2, 0, null, null, 1);
            var x = await s.ConfirmRepairAsync(LastRep(d), 2, 0, null, Kcs);
            await Task.Delay(5);
            var vr = await s.VoidReceiveAsync(ridB, "nhan nham", 1, new[] { "KHO", "GC" });
            await Check(d, "S34 trước huỷ lần 2");
            Ok(b0.Ok && i1.Ok && ra.Ok && rb.Ok && cl1.Ok && i2.Ok && r2.Ok && k.Ok && x.Ok && vr.Ok, "S34 dựng xong",
               $"{b0.Error}|{i1.Error}|{ra.Error}|{rb.Error}|{cl1.Error}|{i2.Error}|{r2.Error}|{k.Error}|{x.Error}|{vr.Error}");
            // lời khuyên gỡ (HandoverWhy.OpCloseRemainder) phải tính như máy chủ: lần huỷ mới ở giờ hiện tại, các lần cũ giữ giờ của chúng
            var simIn = HandoverWhyTest.CloneInput((await new HandoverLedgerService(d).GetInputForDetailAsync(1))!);
            var rAt = d.HandoverRepairChecks.List.Max(c => c.CheckedAt);
            HandoverWhy.OpCloseRemainder(t1)(simIn);
            var simT = HandoverLedgerService.SnapshotBefore(simIn, rAt).Txs.Single(t => t.Id == t1);
            Ok(simT.Issued == 4 && simT.Closed == 6 && HandoverLedgerService.LoopTimeline(simIn).Count == 0,
               "Lời khuyên gỡ mô phỏng huỷ lần 2: lúc hàng sửa về phiếu còn 4 (chỉ mở lại 2 chiếc lần 2), không ô âm", $"{simT.Issued}/{simT.Closed}");
            await Task.Delay(5);
            var c2 = await s.CloseRemainderAsync(t1, "huy lan 2", Other, new List<string> { "KHO" });
            var c2b = c2.Ok ? c2 : await s.CloseRemainderAsync(t1, "huy lan 2", 1, kho);   // Other có thể không có quyền nhóm Kho → admin
            await Check(d, "S34 huỷ lần 2");
            Ok(c2b.Ok, "Huỷ phần chưa nhận lần 2 lưu được (không chặn oan theo thời gian)", c2b.Error);
            var tx = d.HandoverTransactions.List.Single(t => t.HandoverTxId == t1);
            var rows = d.HandoverTxCloses.List.Where(c => c.HandoverTxId == t1).OrderBy(c => c.ClosedAt).ToList();
            Ok(rows.Count == 2 && rows[0].Qty == 6 && rows[1].Qty == 2 && rows[0].Reason == "huy lan 1" && rows[1].Reason == "huy lan 2" && rows[0].ClosedAt < rows[1].ClosedAt
               && tx.QtyClosed == 8 && tx.QtyIssued == 2 && tx.ClosedAt == rows[1].ClosedAt && !rows.Any(r => r.IsMigrated),
               "Bảng HandoverTxCloses: 2 dòng (6 · lần 1, 2 · lần 2, đúng lý do, đúng giờ); phiếu: QtyClosed 8, còn 2, ClosedAt = lần 2",
               string.Join(" | ", rows.Select(r => $"{r.Qty} {r.Reason} {r.ClosedAt:HH:mm:ss.fff}")) + $" · phiếu {tx.QtyClosed}/{tx.QtyIssued}");
            var inp = (await new HandoverLedgerService(d).GetInputForDetailAsync(1))!;
            var steps = HandoverLedgerService.ComputeSteps(inp);
            var dong = steps.Where(st => st.Kind == "DONG" && st.RefId == t1).ToList();
            Ok(dong.Count == 2 && dong[0].Qty == -6 && dong[1].Qty == -2 && dong[0].At < dong[1].At && dong[1].Text.Contains("lần 2/2"),
               "Dòng B: 2 bước huỷ phần chưa nhận (−6 lúc lần 1, −2 lúc lần 2)", string.Join(" | ", dong.Select(st => $"{st.Qty} {st.At:HH:mm:ss.fff} {st.Text}")));
            var tl = HandoverLedgerService.LoopTimeline(inp);
            Ok(tl.Count == 0, "Ảnh sổ lúc hàng sửa về Kho: không ô âm (bản cũ: Kho −6)", string.Join(",", tl.Select(z => z.Key + "=" + z.Value.Value)));
            var logs = await s.GetLogsAsync(1);
            var lt = logs.Single(l => l.HandoverTxId == t1);
            Ok(lt.Closes.Count == 2 && lt.Closes[0].Qty == 6 && lt.Closes[1].Qty == 2 && lt.Closes.All(c => !string.IsNullOrEmpty(c.ByName)),
               "Lịch sử phiếu: 2 lần huỷ, có tên người từng lần", string.Join(" | ", lt.Closes.Select(c => $"{c.Qty} {c.ByName} {c.Reason}")));
            // đối chứng: bỏ 2 dòng (như dữ liệu trước SQL 10, chỉ bộ cột cũ) → sổ tính như bản cũ: mốc hàng sửa về có Kho âm (đúng lỗi cũ, chứng minh ca này bắt được)
            var oldInp = HandoverWhyTest.CloneInput(inp);
            foreach (var t in oldInp.Txs) t.Closes = new List<InClose>();
            var tlOld = HandoverLedgerService.LoopTimeline(oldInp);
            Ok(tlOld.Count > 0, "Đối chứng: tính theo bộ cột cũ (1 giờ cho cả 8) thì ảnh sổ lúc hàng sửa về có ô âm — ca này đúng là lỗi cũ", $"{tlOld.Count}");
            // giữa 2 lần huỷ: ảnh sổ ngay sau lần 1 → phiếu còn 4 (2 lần nhận); ngay trước lần 1 → 10
            var mid = HandoverLedgerService.SnapshotBefore(inp, rows[1].ClosedAt);
            var pre = HandoverLedgerService.SnapshotBefore(inp, rows[0].ClosedAt);
            var tm = mid.Txs.Single(t => t.Id == t1); var tp = pre.Txs.Single(t => t.Id == t1);
            Ok(tm.Issued == 2 + 2 && tm.Closed == 6 && tp.Issued == 10 && tp.Closed == 0,
               "SnapshotBefore: trước lần 2 phiếu 4 (đã huỷ 6), trước lần 1 phiếu 10", $"mid {tm.Issued}/{tm.Closed} pre {tp.Issued}/{tp.Closed}");
        }

        // khung hàng lỗi (HandoverNgTrace): phiếu HÀNG LỖI huỷ phần chưa nhận 2 lần → mỗi bước trừ đúng số lần đó (đột biến dùng tổng Closed phải SAI)
        Console.WriteLine("[S34b] [K5-CLOSE-NHIEU] Phiếu hàng lỗi Gia công → Kho huỷ phần chưa nhận 2 lần: khung hàng lỗi cân từng bước, đang chuyển = 0");
        {
            var d = Seed(); var s = new HandoverService(d); WtsSvcHelp.Reset();
            await s.IssueByCellAsync(1, "KHO", "GC", 4, 1, "MOI", false, "MOI", toNC: "NC1"); await s.ReceiveAsync(LastTx(d), 4, 0, null, Gc);
            var w = await WtsSvcHelp.SaveAsync(d, 1, "GC", "NC1", null, 0, 4, Gc, catId: 2);
            var vu = d.WtsProductionLogs.List.Single(z => z.WtsLogId == w.LogId).NgCaseId;
            var gL = await s.IssueByCellAsync(1, "GC", "KHO", 4, Gc, "LOI", false, "LOI", ngCaseId: vu); var tl = LastTx(d);
            var k1 = await s.ReceiveAsync(tl, 0, 1, null, 1, null, HandoverCell.Loi);
            var k2 = await s.ReceiveAsync(tl, 0, 1, null, 1, null, HandoverCell.Loi);
            var cA = await s.CloseRemainderAsync(tl, "tra lai 2", Gc, new List<string> { "GC" });
            var rid = d.HandoverTransactions.List.Single(t => t.HandoverTxId == tl).Receives.Where(r => !r.IsVoided).Max(r => r.ReceiveId);
            var vr = await s.VoidReceiveAsync(rid, "nhan nham", 1, new[] { "KHO", "GC" });
            var cB = await s.CloseRemainderAsync(tl, "tra lai 1", Gc, new List<string> { "GC" });
            var a = await Check(d, "S34b");
            var tr = HandoverNgTrace.Build((await new HandoverLedgerService(d).GetInputForDetailAsync(1))!);
            var dong = tr.Events.Where(e => e.Type == "DONG").ToList();
            Ok(w.Ok && gL.Ok && k1.Ok && k2.Ok && cA.Ok && vr.Ok && cB.Ok && tr.Problems.Count == 0 && dong.Count == 2 && dong[0].Qty == 2 && dong[1].Qty == 1,
               "Khung hàng lỗi: 2 bước huỷ phần chưa nhận (2, 1), không lệch", $"{w.Err}|{gL.Error}|{k1.Error}|{k2.Error}|{cA.Error}|{vr.Error}|{cB.Error} · {string.Join(" | ", tr.Problems)} · {string.Join(",", dong.Select(e => e.Qty))}");
        }

        Console.WriteLine("[S35] [K5-CLOSE-NHIEU] Dữ liệu cũ / phần chênh: CloseRows bù 1 dòng phần chênh (giờ không trễ hơn lần ghi bằng code mới); dòng nhiều hơn phiếu → tính như cũ; QuetSo báo CLOSE_LECH");
        {
            var t0 = new DateTime(2026, 11, 6, 17, 0, 0);
            var none = HandoverLedgerService.CloseRows(5, t0.AddMinutes(30), t0, Array.Empty<(long, DateTime, decimal, bool)>());
            Ok(none.Count == 1 && none[0].Qty == 5 && none[0].At == t0.AddMinutes(30) && none[0].Id == 0, "Không có dòng: 1 dòng bù = tổng, lúc ClosedAt cũ", string.Join(",", none));
            var gap = HandoverLedgerService.CloseRows(8, t0.AddMinutes(50), t0, new[] { (11L, t0.AddMinutes(10), 3m, true), (12L, t0.AddMinutes(40), 2m, false) });
            Ok(gap.Count == 3 && gap.Sum(c => c.Qty) == 8 && gap[1].Id == 0 && gap[1].Qty == 3 && gap[1].At == t0.AddMinutes(40) && gap[2].Id == 12,
               "Có dòng chuyển (3) + dòng code mới (2), phiếu 8: bù 3, giờ = lần code mới đầu (không trễ hơn), đứng trước dòng đó", string.Join(",", gap));
            var over = HandoverLedgerService.CloseRows(2, t0, t0, new[] { (1L, t0, 3m, false) });
            Ok(over.Count == 0, "Dòng nhiều hơn phiếu → rỗng (ClosesOf tính theo bộ cột cũ)");
            var tx = new InTx { Id = 1, Closed = 2, ClosedAt = t0, At = t0.AddHours(-1), Closes = over };
            var co = HandoverLedgerService.ClosesOf(tx);
            Ok(co.Count == 1 && co[0].Qty == 2 && co[0].At == t0, "ClosesOf rỗng → 1 lần (2, lúc ClosedAt) như bản cũ");
            // QuetSo: phiếu có QtyClosed mà không có dòng → CLOSE_LECH (ít hơn, sổ tự bù); dòng nhiều hơn → CLOSE_LECH (báo Claude)
            var d = Seed(); var s = new HandoverService(d);
            await s.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", toNC: "NC1"); var t1 = LastTx(d);
            await s.ReceiveAsync(t1, 4, 0, null, Gc);
            var cl1 = await s.CloseRemainderAsync(t1, "huy", 1, new List<string> { "KHO" });
            d.HandoverTxCloses.List.Clear();   // giả dữ liệu huỷ trước khi code mới lên (chỉ bộ cột cũ)
            var a = await Check(d, "S35 phiếu không có dòng");
            Ok(cl1.Ok && a.G("KHO").ConGiao == 16 && a.Balanced, "Phiếu cũ (chỉ bộ cột cũ): sổ đúng như trước (Kho còn 16)", cl1.Error + $" {a.G("KHO").ConGiao}");
            var q1 = await QuetSo.QuetCore.ScanAsync(d, new HandoverLedgerService(d));
            Ok(q1.Findings.Any(f => f.Kind == QuetSo.QuetCore.CloseLech && f.Detail.Contains("sổ tự bù")), "QuetSo: CLOSE_LECH (ít hơn — sổ tự bù)", string.Join(" | ", q1.Findings.Select(f => f.Kind)));
            d.HandoverTxCloses.List.Add(new HandoverTxClose { CloseId = 1, HandoverTxId = t1, Qty = 9, ClosedBy = 1, ClosedAt = DateTime.Now });
            var q2 = await QuetSo.QuetCore.ScanAsync(d, new HandoverLedgerService(d));
            var a2 = await Check(d, "S35 dòng nhiều hơn");
            Ok(q2.Findings.Any(f => f.Kind == QuetSo.QuetCore.CloseLech && f.Detail.Contains("KHÔNG THỂ")) && a2.G("KHO").ConGiao == 16,
               "Dòng nhiều hơn phiếu: QuetSo báo KHÔNG THỂ, sổ vẫn tính theo phiếu (Kho còn 16)", string.Join(" | ", q2.Findings.Select(f => f.Detail)));
            d.HandoverTxCloses.List.Clear();
            var q3 = await QuetSo.QuetCore.ScanAsync(d, new HandoverLedgerService(d));
            d.HandoverTxCloses.List.Add(new HandoverTxClose { CloseId = 2, HandoverTxId = t1, Qty = 6, ClosedBy = 1, ClosedAt = DateTime.Now });
            var q4 = await QuetSo.QuetCore.ScanAsync(d, new HandoverLedgerService(d));
            Ok(!q4.Findings.Any(f => f.Kind == QuetSo.QuetCore.CloseLech), "Khớp → QuetSo không báo CLOSE_LECH");
        }
        // [NHAN-DOI-O-NC 2026-11-06] SQL 14 HandoverReceives.ToNC: phiếu mang vụ vào Gia công ô Hàng lỗi (không NC), bên nhận đổi ô sang Hàng lỗi đã xử lý
        //   → trước: hàng vào NC đầu (lùi không lý do). Nay NC của vụ (vụ NG ở NC2 → NC2), chỉ lùi NC trước + lý do, NC sau chặn.
        Console.WriteLine("[S36] [NHAN-DOI-O-NC] Vụ NG ở NC2 → GC trả Kho ô Hàng lỗi → Kho chuyển lại GC ô Hàng lỗi → GC nhận đổi ô Hàng lỗi đã xử lý: vào NC2 (trước: NC1); NC3 chặn; NC1 cần lý do");
        {
            Seed36(out var d, out var s, out var txBack);
            var b = await Check(d, "S36 trước nhận");
            var r3 = await s.ReceiveAsync(txBack, 1, 0, null, Gc, null, HandoverCell.Xl, "da sua xong", null, "NC3", "thu nc sau");
            Ok(!r3.Ok && r3.Error.Contains("không bỏ qua NC") && d.HandoverReceives.List.Count(r => r.HandoverTxId == txBack) == 0, "Chọn NC3 (sau NC của vụ) bị chặn, không ghi", r3.Error);
            var r1 = await s.ReceiveAsync(txBack, 1, 0, null, Gc, null, HandoverCell.Xl, "da sua xong", null, "NC1", null);
            Ok(!r1.Ok && r1.Error.Contains("lý do"), "Chọn NC1 (làm lại) không lý do bị chặn", r1.Error);
            var x0 = await s.ReceiveAsync(txBack, 0, 1, null, Gc, null, null, null, null, "NC2", null);   // nhận vào ô Hàng lỗi: số ở ô NG
            Ok(!x0.Ok && x0.Error.Contains("không chọn NC"), "Không đổi ô (vào ô Hàng lỗi) mà gửi NC → báo không chọn NC được", x0.Error);
            var r = await s.ReceiveAsync(txBack, 1, 0, null, Gc, null, HandoverCell.Xl, "da sua xong", null);
            var a = await Check(d, "S36 nhận vào NC2");
            var rr = d.HandoverReceives.List.Last();
            var nc1b = b.G("GC").Steps.First(x => x.Code == "NC1").Waiting; var nc2b = b.G("GC").Steps.First(x => x.Code == "NC2").Waiting;
            var nc1a = a.G("GC").Steps.First(x => x.Code == "NC1").Waiting; var nc2a = a.G("GC").Steps.First(x => x.Code == "NC2").Waiting;
            Ok(r.Ok && rr.ToNC == "NC2" && nc2a == nc2b + 1 && nc1a == nc1b, "Mặc định theo vụ: lần nhận lưu NC2, chờ NC2 +1, NC1 không đổi", $"{r.Error} · ToNC {rr.ToNC} · NC1 {nc1b}→{nc1a} NC2 {nc2b}→{nc2a}");
            var inp = (await new HandoverLedgerService(d).GetInputForDetailAsync(1))!;
            Ok(inp.Txs.Single(t => t.Id == txBack).Receives.Single().ToNC == "NC2", "Sổ nạp ToNC của lần nhận");
            // làm lại NC1 có lý do
            Seed36(out var e, out var se, out var txE);
            var re = await se.ReceiveAsync(txE, 1, 0, null, Gc, null, HandoverCell.Xl, "da sua xong", null, "nc1", "kiem lai kich thuoc");   // chữ thường: lưu đúng mã NC1 (rà độc lập T3)
            var ae = await Check(e, "S36 làm lại NC1");
            var rre = e.HandoverReceives.List.Last();
            Ok(re.Ok && rre.ToNC == "NC1" && (rre.Notes ?? "").Contains("Làm lại từ NC1") && (rre.Notes ?? "").Contains("kiem lai kich thuoc"),
               "Làm lại NC1 + lý do: lưu NC1, lý do vào Ghi chú lần nhận", re.Error + " · " + rre.ToNC + " · " + rre.Notes);
            // rà độc lập 06/11 C1: phiếu CHỈ ĐỊNH SẴN ô Hàng lỗi đã xử lý không NC (bên giao chọn "như hàng mới — làm lại từ đầu") → nhận không đổi ô:
            //   giữ ý bên giao (vào NC đầu), KHÔNG tự điền NC vụ; gửi NC thì báo không chọn được
            {
                var f = Seed(); var sf = new HandoverService(f); WtsSvcHelp.Reset();
                await sf.IssueByCellAsync(1, "KHO", "GC", 2, 1, "MOI", false, "MOI", toNC: "NC1"); await sf.ReceiveAsync(LastTx(f), 2, 0, null, Gc);
                await WtsSvcHelp.SaveAsync(f, 1, "GC", "NC1", null, 2, 0, Gc);
                var wf = await WtsSvcHelp.SaveAsync(f, 1, "GC", "NC2", null, 0, 1, Gc, catId: 2);
                var vf = f.WtsProductionLogs.List.Single(z => z.WtsLogId == wf.LogId).NgCaseId;
                await sf.IssueByCellAsync(1, "GC", "KHO", 1, Gc, "LOI", false, "LOI", ngCaseId: vf); await sf.ReceiveAsync(LastTx(f), 0, 1, null, 1, null, HandoverCell.Loi);
                var gx = await sf.IssueByCellAsync(1, "KHO", "GC", 1, 1, "LOI", false, "XL", ngCaseId: vf); var tx = LastTx(f);
                var bf = await Check(f, "S36 C1 trước");
                var rz = await sf.ReceiveAsync(tx, 1, 0, null, Gc, null, null, null, null, "NC2", null);
                var rf = await sf.ReceiveAsync(tx, 1, 0, null, Gc);
                var af = await Check(f, "S36 C1 nhận");
                Ok(gx.Ok && !rz.Ok && rf.Ok && f.HandoverReceives.List.Last().ToNC == null
                   && af.G("GC").Steps.First(x => x.Code == "NC1").Waiting == bf.G("GC").Steps.First(x => x.Code == "NC1").Waiting + 1,
                   "C1: phiếu chỉ định sẵn ô Hàng lỗi đã xử lý không NC → nhận giữ ý bên giao (vào NC1, ToNC trống); gửi NC → báo không chọn được", $"{gx.Error}|{rz.Error}|{rf.Error} · {f.HandoverReceives.List.Last().ToNC}");
            }
            // đối chứng: bỏ ToNC lần nhận (như dữ liệu trước SQL 14) → sổ tính như cũ: vào NC1
            var inpOld = HandoverWhyTest.CloneInput(inp);
            foreach (var t in inpOld.Txs) t.Receives = t.Receives.Select(x => x with { ToNC = null }).ToList();
            var old = HandoverLedgerService.Compute(inpOld);
            Ok(old.G("GC").Steps.First(x => x.Code == "NC1").Waiting == nc1b + 1, "Đối chứng: lần nhận không NC (dữ liệu cũ) → vào NC đầu như trước");
        }
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
