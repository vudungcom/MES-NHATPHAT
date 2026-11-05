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
            var c5 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, w2.NgCaseId, 2, 0, "NC3", null, Gc);
            var a5 = await Check(db, "S5 ghi");
            Ok(c5.Ok && a5.G("GC").LoiGiu == b5.G("GC").LoiGiu - 2 && a5.G("GC").ChoKcs == 2, "Ô Hàng lỗi GC −2, Chờ KCS 2", c5.Error);
            await svc.ConfirmRepairAsync(LastRep(db), 1, 0, null, Kcs);
            await Check(db, "S5 KCS lần 1");
            await svc.ConfirmRepairAsync(LastRep(db), 0, 1, "van xuoc", Kcs);
            var a5b = await Check(db, "S5 KCS lần 2");
            var cs = a5b.Cases[w2.NgCaseId!.Value];
            Ok(a5b.G("GC").ChoKcs == 0 && a5b.G("GC").LoiGiu == b5.G("GC").LoiGiu - 1 && cs.HeldAt("GC", HandoverCell.Loi) == 1 && cs.DaXuLy["GC"] == 1,
               "KCS từng phần: đạt 1 vào NC3, loại 1 về ô Hàng lỗi GC cùng vụ", $"loi {a5b.G("GC").LoiGiu} held {cs.HeldAt("GC", HandoverCell.Loi)}");
            Ok(a5b.G("GC").Steps.First(s => s.Code == "NC3").Waiting == b5.G("GC").Steps.First(s => s.Code == "NC3").Waiting + 1, "Chờ NC3 +1");
            Ok(cs.TuXuLy.Contains("GC"), "Vụ ghi GC tự xử lý");
        }

        Console.WriteLine("[S6] K5: hàng đạt đã nhập WTS → huỷ lần KCS xác nhận bị chặn + gợi ý gỡ chỉ dòng WTS");
        {
            var nc3 = (await led.GetLedgerForDetailAsync(1))!.G("GC").Steps.First(s => s.Code == "NC3");
            var w = await VuHelp.Wts(db, "GC", "NC3", null, nc3.Waiting ?? 0, 0, Gc, 0);
            await Check(db, "S6 WTS NC3");
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
            await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, db.WtsProductionLogs.List.Single(w => w.NC == "NC2").NgCaseId, 1, 0, "NC3", null, Gc);
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
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidRepairCheck(cid), new HashSet<string> { "X:" + cid });
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

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
