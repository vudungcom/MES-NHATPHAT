// [KHACH-GUI-LAI 2026-11-11] Kịch bản cố định "bu" (Gói 14): khách gửi bù phôi cho lần lỗi đã Trả khách / Huỷ — bằng hàm thật
//   HandoverService (ReceiveReplacementAsync / VoidReplacementAsync / VoidIssueAsync) + sổ chung HandoverLedgerService.
//   Nguồn: KHÁCH 10/11 vòng 1 câu 1a A (phôi bù vào Kho ô Hàng lỗi đã xử lý = phôi còn, gắn lần lỗi cũ, giao Gia công vào NC đầu như phôi thường)
//          + 1b A (chặn bù vượt; dư thì sửa Số lượng thực nhận); HÙNG 11/11 14:39 duyệt thiết kế + SQL 15_KHBU, trần bù chặn CẢ khi Workflow OFF.
//   Sau MỖI bước ghi kiểm chung: cân PO (phôi + bù), không ô âm, vụ cân, khung lỗi + dòng B + "Đang ở đâu" khớp, đã bù mỗi vụ ≤ trả khách + huỷ.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class BuScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    const int Kho = 1, Gc = 2;
    static readonly List<string> All = new() { "KHO", "GC", "HTSP", "KCS", "PKG" };

    static AppDbContext Seed(bool workflow = true)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-BU", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 20, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 20, NgayNhanPhoi = DateTime.Now.AddDays(-1), SoPO = "PO1", PartNo = "P" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    static long LastTx(AppDbContext db) => db.HandoverTransactions.List.Max(t => t.HandoverTxId);
    static long LastBu(AppDbContext db) => db.HandoverReplacements.List.Max(t => t.ReplacementId);

    static string Sig(LedgerRow r)
    {
        var sb = new System.Text.StringBuilder($"PH{r.Phoi}/BU{r.Bu}/H{r.DaHuy}/K{r.TraKhach};");
        foreach (var g in r.Groups.Values.OrderBy(x => x.Code))
        {
            sb.Append($"{g.Code}:{g.ChuaLam}/{g.DaLam}/{g.ConGiao}/{g.LoiGiu}/{g.SuaGiu}/{g.ChoNhan}/{g.NhanLoiXl}/{g.Ton};");
            foreach (var s in g.Steps) sb.Append($"{s.Pg}{s.Code}={s.Waiting}/{s.Ok}/{s.Ng};");
        }
        foreach (var c in r.Cases.Values.OrderBy(x => x.Id))
            sb.Append($"V{c.Id}:{c.SinhRa}|{c.Huy}|{c.TraKhach}|{c.DaBu}|{string.Join(",", c.Held.Where(x => x.Value != 0).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))};");
        return sb.ToString();
    }

    /// <summary>Kiểm chung sau mỗi bước ghi (đáp án độc lập: tổng phôi bù đọc thẳng bảng, không qua sổ).</summary>
    static async Task<LedgerRow> Check(AppDbContext db, string buoc, bool allowNeg = false)
    {
        var led = new HandoverLedgerService(db);
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var bad = new List<string>();
        var buDb = db.HandoverReplacements.List.Where(b => b.KhPlanDetailId == 1 && !b.IsVoided).ToList();
        if (row.Bu != buDb.Sum(b => b.Qty)) bad.Add($"sổ bù {row.Bu} ≠ bảng {buDb.Sum(b => b.Qty)}");
        var phoi = db.KhoVatLieus.List.Where(k => k.PlanDetailId == 1 && k.IsActive).Sum(k => (decimal)(k.SoLuongThucNhan ?? 0));
        if (phoi + buDb.Sum(b => b.Qty) != row.TonTong + row.DangGiao + row.TpNhap + row.DaHuy + row.TraKhach) bad.Add("PO không cân (phôi + bù)");
        if (!row.Balanced) bad.Add("row.Balanced = false");
        if (row.BalanceExplain.Result != row.Phoi + row.Bu) bad.Add($"diễn giải cân {row.BalanceExplain.Result} ≠ phôi + bù {row.Phoi + row.Bu}");
        foreach (var g in buDb.GroupBy(b => b.NgCaseId))
        {
            var c = row.Cases.TryGetValue(g.Key, out var cc) ? cc : null;
            if (c == null || c.DaBu != g.Sum(b => b.Qty)) bad.Add($"vụ {NgCaseNo.Format(g.Key)} đã bù sổ {c?.DaBu} ≠ bảng {g.Sum(b => b.Qty)}");
            else if (c.DaBu > c.TraKhach + c.Huy) bad.Add($"vụ {c.No} đã bù {c.DaBu} > trả khách {c.TraKhach} + huỷ {c.Huy}");
        }
        if (HandoverLedgerService.BuOver(row).Count > 0) bad.Add("BuOver có vụ");
        var neg = HandoverLedgerService.NegativeCells(row);
        if (!allowNeg && neg.Count > 0) bad.Add("ô âm: " + string.Join(", ", neg.Select(x => x.Value.Text + "=" + x.Value.Value)));
        if (!allowNeg && row.Warnings.Count > 0) bad.Add("cảnh báo: " + string.Join(" | ", row.Warnings));
        foreach (var c in row.Cases.Values) if (c.Lech != 0) bad.Add($"vụ {c.No} lệch {c.Lech}");
        var inp = (await led.GetInputForDetailAsync(1))!;
        var tr = HandoverNgTrace.Build(inp);
        if (tr.Problems.Count > 0) bad.Add("khung lỗi: " + string.Join(" | ", tr.Problems));
        var steps = HandoverLedgerService.ComputeSteps(inp);
        if (steps.Count > 0 && Sig(steps.Last().Row) != Sig(row)) bad.Add("dòng B cuối ≠ sổ");
        if (steps.Count(s => s.Kind == "BU") != buDb.Count) bad.Add($"dòng B bước bù {steps.Count(s => s.Kind == "BU")} ≠ {buDb.Count} phiếu");
        var w = HandoverLedgerService.WhereIs(row);
        if (!w.Balanced || w.Total != phoi + buDb.Sum(b => b.Qty)) bad.Add($"Đang ở đâu {w.Total} ≠ phôi {w.Phoi} + bù {w.Bu}");
        Ok(bad.Count == 0, $"[chung] {buoc}: cân phôi + bù, không ô âm, vụ cân, đã bù ≤ trả khách + huỷ, dòng B + khung lỗi + Đang ở đâu khớp", string.Join("\n", bad));
        return row;
    }

    /// <summary>Ví dụ thiết kế: Kho báo 2 phôi lỗi → trả khách 2 (vụ A); Kho giao GC 18 (NC1); GC WTS NC1 OK 10 NG 1 (vụ B) → GC huỷ 1.</summary>
    static async Task<(AppDbContext db, HandoverService svc, long vA, long vB, long txKhach, long txHuy)> Base(bool workflow = true)
    {
        var db = Seed(workflow); var svc = new HandoverService(db); WtsSvcHelp.Reset();
        var r1 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "phoi nut", null, Kho, All, ngCategoryId: 1);
        var vA = VuHelp.LastCase(db);
        var r2 = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 2, Kho, HandoverPurpose.CustReturn, reason: "phoi nut", ngCaseId: vA);
        var txKhach = LastTx(db);
        var r3 = await svc.IssueByCellAsync(1, "KHO", "GC", 18, Kho, "MOI", false, "MOI", toNC: "NC1");
        var r4 = await svc.ReceiveAsync(LastTx(db), 18, 0, null, Gc);
        var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 1, Gc, catId: 2);
        var vB = db.WtsProductionLogs.List.Single(z => z.WtsLogId == w.LogId).NgCaseId ?? 0;
        var r5 = await svc.IssueSpecialAsync(1, "GC", "HUY", 1, Gc, HandoverPurpose.Scrap, reason: "xuoc", ngCaseId: vB);
        var txHuy = LastTx(db);
        Ok(r1.Ok && r2.Ok && r3.Ok && r4.Ok && w.Ok && r5.Ok && vA > 0 && vB > 0 && vA != vB, $"dựng xong (Workflow {(workflow ? "ON" : "OFF")}): vụ A trả khách 2, vụ B huỷ 1",
           $"{r1.Error}|{r2.Error}|{r3.Error}|{r4.Error}|{w.Err}|{r5.Error}");
        return (db, svc, vA, vB, txKhach, txHuy);
    }

    public static async Task<int> Run()
    {
        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U1] Ví dụ thiết kế: khách gửi bù 2 cho lần lỗi đã trả khách 2 — trần, sổ Kho, cân PO, dòng B, khung lỗi, Đang ở đâu");
        {
            var (db, svc, vA, vB, _, _) = await Base();
            var led = new HandoverLedgerService(db);
            var r0 = await Check(db, "U1 trước bù");
            var sig0 = Sig(r0);
            var cA = HandoverLedgerService.BuCap(r0, vA); var cB = HandoverLedgerService.BuCap(r0, vB);
            Ok(cA.Max == 2 && cA.Text == "trả khách 2 + huỷ 0 − đã bù 0" && cB.Max == 1 && cB.Text == "trả khách 0 + huỷ 1 − đã bù 0", $"Trần bù: A {cA.Max} ({cA.Text}), B {cB.Max} ({cB.Text})");
            Ok(HandoverLedgerService.BuCases(r0).Select(c => c.Id).SequenceEqual(new[] { vA, vB }), "Danh sách vụ được bù = A, B (theo giờ)");
            Ok(r0.G("KHO").ConGiao == 0 && r0.Bu == 0, "Trước bù: Kho phôi còn 0, bù 0");
            var over = await svc.ReceiveReplacementAsync(1, vA, 3, null, Kho);
            Ok(!over.Ok && over.Error.Contains("tối đa 2") && over.Error.Contains("trả khách 2 + huỷ 0 − đã bù 0") && over.Error.Contains("Số lượng thực nhận"),
               "Bù 3 cho A (trần 2): chặn, lời báo đủ trần + cách xử lý gửi dư", over.Error);
            Ok(db.HandoverReplacements.List.Count == 0, "Bị chặn: không ghi dòng nào");
            var ok = await svc.ReceiveReplacementAsync(1, vA, 2, "  PX-01  ", Kho);
            var r1 = await Check(db, "U1 sau bù 2");
            var bu = db.HandoverReplacements.List.Single();
            Ok(ok.Ok && bu.Qty == 2 && bu.NgCaseId == vA && bu.ReceivedBy == Kho && bu.Notes == "PX-01" && !bu.IsVoided && (DateTime.Now - bu.ReceivedAt).TotalMinutes < 1,
               "Ghi 1 dòng HandoverReplacements: vụ A, 2, người Kho, ghi chú cắt khoảng trắng, giờ máy chủ", ok.Error);
            Ok(r1.G("KHO").ConGiao == 2 && r1.G("KHO").NhanLoiXl == 2 && r1.Bu == 2 && r1.Phoi == 20 && r1.G("KHO").Ton == 2,
               $"Kho: phôi còn 2, Nhận · Hàng lỗi đã xử lý 2, Tồn 2; PO phôi 20 + bù 2 (ConGiao {r1.G("KHO").ConGiao}, NhanLoiXl {r1.G("KHO").NhanLoiXl})");
            Ok(r1.Cases[vA].DaBu == 2 && r1.Cases[vA].Lech == 0 && HandoverLedgerService.BuCap(r1, vA).Max == 0 && HandoverLedgerService.BuCap(r1, vB).Max == 1,
               "Vụ A: đã bù 2 (vụ vẫn cân), còn bù 0; vụ B còn 1");
            var ce = r1.G("KHO").Explains["ConGiao"];
            Ok(ce.Terms.Any(t => t.Label == "Khách gửi bù" && t.Value == 2 && t.Sign == 1) && ce.Formula.Contains("Khách gửi bù"), "Diễn giải Kho · Phôi còn giao có vế Khách gửi bù 2");
            var xe = r1.G("KHO").Explains["NhanLoiXl"];
            Ok(xe.Terms.Any(t => t.Value == 2 && t.Label.StartsWith("Khách gửi bù") && t.Docs.Any(d => d.Kind == "BU" && d.RefId == bu.ReplacementId)), "Diễn giải ô Nhận · Hàng lỗi đã xử lý Kho: Khách gửi bù 2 + chứng từ phiếu bù");
            Ok(r1.BalanceExplain.Formula.StartsWith("Phôi nhận + Khách gửi bù"), "Diễn giải cân PO: Phôi nhận + Khách gửi bù = …");
            var inp = (await led.GetInputForDetailAsync(1))!;
            var st = HandoverLedgerService.ComputeSteps(inp).Last(x => x.Kind == "BU");
            Ok(st.Text.StartsWith($"Khách gửi bù 2 cho {NgCaseNo.Format(vA)}") && st.Text.Contains("phiếu bù #" + bu.ReplacementId) && st.Text.Contains("PX-01") && st.Row.G("KHO").ConGiao == 2 && st.Cell == HandoverCell.Xl,
               $"Dòng B: «{st.Text}»");
            var trc = HandoverNgTrace.BuildCases(r1).Single(x => x.Id == vA);
            Ok(trc.DaBu == 2 && trc.DaBuText.Contains("#" + bu.ReplacementId) && trc.Balanced, "Khung hàng lỗi: vụ A cột Đã bù 2, vụ vẫn cân");
            var w = HandoverLedgerService.WhereIs(r1);
            Ok(w.Bu == 2 && w.Phoi == 20 && w.Balanced && w.Items.Where(i => i.Place == "KHO" && i.Kind == "PHOI").Sum(i => i.Qty) == 2, $"Đang ở đâu: phôi 20 + bù 2 = tổng {w.Total}, Kho phôi còn 2");
            var o2 = await svc.ReceiveReplacementAsync(1, vA, 1, null, Kho);
            Ok(!o2.Ok && o2.Error.Contains("tối đa 0") && db.HandoverReplacements.List.Count == 1, "Bù thêm 1 cho A (đã bù đủ): chặn", o2.Error);
            var g1 = await svc.IssueByCellAsync(1, "KHO", "GC", 2, Kho, "MOI", false, "MOI", toNC: "NC1");
            var n1 = await svc.ReceiveAsync(LastTx(db), 2, 0, null, Gc);
            var r2 = await Check(db, "U1 Kho giao phôi bù vào Gia công NC1");
            Ok(g1.Ok && n1.Ok && r2.G("KHO").ConGiao == 0 && r2.G("GC").Steps.First(s => s.Code == "NC1").Waiting == 9, "Phôi bù giao Gia công như phôi thường: NC1 chờ 18 − 11 + 2 = 9", g1.Error + n1.Error);
            var g2 = await svc.IssueByCellAsync(1, "KHO", "GC", 1, Kho, "MOI", false, "MOI", toNC: "NC1");
            Ok(!g2.Ok, "Kho hết phôi còn (đã giao cả bù): không giao thêm", g2.Error);
            Ok(sig0 != Sig(r1), "Sổ đổi sau bù (chữ ký khác)");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U2] Chặn nhập bù: quyền Kho, vụ không hợp lệ / chưa trả khách, huỷ; số lẻ / 0 / âm; vụ B huỷ 1 → bù 1");
        {
            var (db, svc, vA, vB, _, _) = await Base();
            var u9 = FuzzPerm.GcOnlyUser(db);
            var d1 = await svc.ReceiveReplacementAsync(1, vA, 1, null, u9);
            Ok(!d1.Ok && d1.Error.Contains("không có quyền sửa nhóm Kho"), "Người chỉ Sửa Gia công: không nhận bù thay Kho", d1.Error);
            var d2 = await svc.ReceiveReplacementAsync(1, 0, 1, null, Kho);
            Ok(!d2.Ok && d2.Error.Contains("Chọn"), "Không chọn vụ: chặn", d2.Error);
            var d3 = await svc.ReceiveReplacementAsync(1, 99999, 1, null, Kho);
            Ok(!d3.Ok && d3.Error.Contains("tối đa 0"), "Vụ không thuộc PO: trần 0", d3.Error);
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 0, 1, Gc, catId: 3);
            var vC = db.WtsProductionLogs.List.Single(z => z.WtsLogId == w2.LogId).NgCaseId ?? 0;
            var rC = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            var d4 = await svc.ReceiveReplacementAsync(1, vC, 1, null, Kho);
            Ok(w2.Ok && vC > 0 && rC.Cases[vC].HeldTotal == 1 && !d4.Ok && d4.Error.Contains("tối đa 0"), "Vụ đang giữ (chưa trả khách / huỷ): trần 0, chặn", d4.Error);
            foreach (var q in new[] { 1.5m, 0m, -1m })
            {
                var dq = await svc.ReceiveReplacementAsync(1, vA, q, null, Kho);   // vụ A trần 2: 1.5 chỉ bị chặn vì không nguyên (đột biến M18)
                Ok(!dq.Ok && (q != 1.5m || dq.Error.Contains("số nguyên")), $"Số {q}: chặn", dq.Error);
            }
            Ok(db.HandoverReplacements.List.Count == 0, "Mọi lần bị chặn: không ghi dòng nào");
            var okB = await svc.ReceiveReplacementAsync(1, vB, 1, null, Kho);
            var r = await Check(db, "U2 bù 1 cho vụ B (Gia công huỷ 1)");
            Ok(okB.Ok && r.Cases[vB].DaBu == 1 && r.G("KHO").ConGiao == 1, "Vụ Gia công huỷ 1 → khách bù 1 vào Kho phôi còn", okB.Error);
            var lg = await svc.GetReplacementsAsync(1);
            Ok(lg.Count == 1 && lg[0].NgCaseId == vB && lg[0].ReceivedByName == "U1" && !lg[0].IsVoided, "Lịch sử phiếu: 1 phiếu bù, tên người nhập");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U3] Workflow OFF: trần bù VẪN chặn (Hùng 11/11 14:39); huỷ phiếu trả khách làm bù vượt cũng chặn");
        {
            var (db, svc, vA, _, txKhach, _) = await Base(workflow: false);
            var over = await svc.ReceiveReplacementAsync(1, vA, 3, null, Kho);
            Ok(!over.Ok && over.Error.Contains("tối đa 2"), "OFF: bù 3 > 2 vẫn chặn", over.Error);
            var ok = await svc.ReceiveReplacementAsync(1, vA, 2, null, Kho);
            await Check(db, "U3 OFF bù 2");
            var vk = await svc.VoidIssueAsync(txKhach, "tra nham", Kho, All);
            Ok(ok.Ok && !vk.Ok && vk.Error.Contains("Huỷ phiếu khách gửi bù trước") && vk.Error.Contains("#" + LastBu(db)), "OFF: huỷ phiếu trả khách (đã bù 2) bị chặn, chỉ phiếu bù cần huỷ", vk.Error);
            Ok(!db.HandoverTransactions.List.Single(t => t.HandoverTxId == txKhach).IsVoided, "Phiếu trả khách vẫn còn");
            // OFF: phôi bù đã giao Gia công → huỷ bù vẫn lưu được (N7 — chỉ cảnh báo, sổ thấy ô âm)
            await svc.IssueByCellAsync(1, "KHO", "GC", 2, Kho, "MOI", false, "MOI", toNC: "NC1");
            var vb = await svc.VoidReplacementAsync(LastBu(db), "nhap nham", Kho);
            var rOff = await Check(db, "U3 OFF huỷ bù sau khi đã giao (N7)", allowNeg: true);
            Ok(vb.Ok && rOff.Bu == 0 && rOff.G("KHO").ConGiao == -2, "OFF: huỷ bù đã giao lưu được (N7), Kho phôi còn −2 hiện đỏ", vb.Error);
            var vk2 = await svc.VoidIssueAsync(txKhach, "tra nham", Kho, All);
            Ok(vk2.Ok, "Hết phiếu bù: huỷ phiếu trả khách được", vk2.Error);
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U4] Huỷ phiếu bù (Workflow ON): phôi bù đã giao → K5 chặn + gợi ý; quyền; lý do; huỷ xong = như chưa bù");
        {
            var (db, svc, vA, _, txKhach, _) = await Base();
            var led = new HandoverLedgerService(db);
            var sig0 = Sig((await led.GetLedgerForDetailAsync(1))!);
            await svc.ReceiveReplacementAsync(1, vA, 2, null, Kho);
            var buId = LastBu(db);
            var g = await svc.IssueByCellAsync(1, "KHO", "GC", 2, Kho, "MOI", false, "MOI", toNC: "NC1");
            var txG = LastTx(db);
            var v1 = await svc.VoidReplacementAsync(buId, "nhap nham", Kho);
            Ok(g.Ok && !v1.Ok && v1.Error.Contains("Không lưu") && v1.Error.Contains("#" + txG) && v1.Error.Contains("Huỷ phiếu khách gửi bù #" + buId),
               "Phôi bù đã giao Gia công: huỷ bù bị K5 chặn, gợi ý huỷ phiếu giao #" + txG + " trước", v1.Error);
            var u9 = FuzzPerm.GcOnlyUser(db);
            var v2 = await svc.VoidReplacementAsync(buId, "nhap nham", u9);
            Ok(!v2.Ok && v2.Error.Contains("Chỉ người đã nhập hoặc nhóm Kho"), "Người chỉ Sửa Gia công (không phải người nhập): không huỷ được", v2.Error);
            var v3 = await svc.VoidReplacementAsync(buId, "ab", Kho);
            Ok(!v3.Ok && v3.Error.Contains("ít nhất 3"), "Lý do < 3 ký tự: chặn", v3.Error);
            var vi = await svc.VoidIssueAsync(txG, "giao nham", Kho, All);
            var v4 = await svc.VoidReplacementAsync(buId, "  khach gui nham  ", 2);   // u2 nhóm XUONG có quyền Sửa Kho (không phải người nhập)
            var r = await Check(db, "U4 huỷ phiếu giao rồi huỷ bù");
            var row = db.HandoverReplacements.List.Single();
            Ok(vi.Ok && v4.Ok && row.IsVoided && row.VoidedBy == 2 && row.VoidReason == "khach gui nham" && row.VoidedAt != null, "Huỷ bù: người có quyền Sửa Kho, ghi người / giờ / lý do (cắt khoảng trắng)", vi.Error + v4.Error);
            Ok(Sig(r) == sig0 && HandoverLedgerService.BuCap(r, vA).Max == 2, "Huỷ xong: sổ như chưa bù, vụ A còn bù 2");
            var v5 = await svc.VoidReplacementAsync(buId, "huy lai", Kho);
            Ok(!v5.Ok && v5.Error.Contains("đã huỷ"), "Huỷ lại phiếu đã huỷ: chặn", v5.Error);
            var lg = await svc.GetReplacementsAsync(1);
            Ok(lg.Single().IsVoided && lg.Single().VoidedByName == "U2", "Lịch sử phiếu: phiếu bù ĐÃ HUỶ, người huỷ");
            var cnt = await PoGuard.CountDocsAsync(db, 1);
            Ok(cnt.BuAll == 1 && cnt.HasAny && cnt.Text().Contains("1 phiếu khách gửi bù"), "PoGuard đếm phiếu bù kể cả đã huỷ (không xoá / tách PO được)");
            var vk = await svc.VoidIssueAsync(txKhach, "tra nham", Kho, All);
            Ok(vk.Ok, "Không còn bù: huỷ phiếu trả khách được", vk.Error);
            await Check(db, "U4 cuối");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U5] Vụ vừa huỷ vừa trả khách: bù từng phần; huỷ phiếu huỷ / trả khách chỉ chặn khi làm bù vượt");
        {
            var db = Seed(); var svc = new HandoverService(db); WtsSvcHelp.Reset();
            await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi nut", null, Kho, All, ngCategoryId: 1);
            var v = VuHelp.LastCase(db);
            var h = await svc.IssueSpecialAsync(1, "KHO", "HUY", 1, Kho, HandoverPurpose.Scrap, reason: "nut", ngCaseId: v); var txH = LastTx(db);
            var k = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 2, Kho, HandoverPurpose.CustReturn, reason: "nut", ngCaseId: v); var txK = LastTx(db);
            var r0 = await Check(db, "U5 dựng");
            Ok(h.Ok && k.Ok && HandoverLedgerService.BuCap(r0, v).Max == 3 && HandoverLedgerService.BuCap(r0, v).Text == "trả khách 2 + huỷ 1 − đã bù 0", "Trần = trả khách 2 + huỷ 1 = 3");
            var b1 = await svc.ReceiveReplacementAsync(1, v, 1, null, Kho);
            var b2 = await svc.ReceiveReplacementAsync(1, v, 1, null, Kho);
            var r1 = await Check(db, "U5 bù 1 + 1");
            Ok(b1.Ok && b2.Ok && r1.Cases[v].DaBu == 2 && HandoverLedgerService.BuCap(r1, v).Max == 1 && r1.Cases[v].BuDocs.Count == 2, "Bù từng lần: đã bù 2, còn 1, 2 phiếu");
            var b3 = await svc.ReceiveReplacementAsync(1, v, 2, null, Kho);
            Ok(!b3.Ok && b3.Error.Contains("tối đa 1"), "Bù 2 khi còn 1: chặn", b3.Error);
            var vh = await svc.VoidIssueAsync(txH, "huy nham", Kho, All);
            var r2 = await Check(db, "U5 huỷ phiếu huỷ (còn trả khách 2 ≥ đã bù 2)");
            Ok(vh.Ok && r2.Cases[v].Huy == 0 && HandoverLedgerService.BuCap(r2, v).Max == 0, "Huỷ phiếu Huỷ 1: trả khách 2 vẫn ≥ đã bù 2 → được, còn bù 0", vh.Error);
            var vk = await svc.VoidIssueAsync(txK, "tra nham", Kho, All);
            Ok(!vk.Ok && vk.Error.Contains("khách đã gửi bù 2") && !db.HandoverTransactions.List.Single(t => t.HandoverTxId == txK).IsVoided, "Huỷ phiếu trả khách 2 (đã bù 2): chặn", vk.Error);
            await Check(db, "U5 cuối");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U6] Theo thời gian (K5-VONG): ảnh sổ trước lúc bù không có phôi bù; dữ liệu cũ không bù tính như cũ");
        {
            var (db, svc, vA, _, _, _) = await Base();
            var led = new HandoverLedgerService(db);
            var inp0 = (await led.GetInputForDetailAsync(1))!;
            var r0 = HandoverLedgerService.Compute(inp0);
            Ok(inp0.Bu.Count == 0 && r0.Bu == 0 && r0.Balanced && r0.BalanceExplain.Result == r0.Phoi, "PO chưa có bù: sổ như cũ (phôi = vế phải)");
            await Task.Delay(20);
            await svc.ReceiveReplacementAsync(1, vA, 2, null, Kho);
            var inp = (await led.GetInputForDetailAsync(1))!;
            var at = inp.Bu.Single().At;
            var before = HandoverLedgerService.SnapshotBefore(inp, at);
            var after = HandoverLedgerService.SnapshotBefore(inp, at.AddTicks(1));
            Ok(before.Bu.Count == 0 && after.Bu.Count == 1 && HandoverLedgerService.Compute(before).G("KHO").ConGiao == 0 && HandoverLedgerService.Compute(after).G("KHO").ConGiao == 2,
               "SnapshotBefore: trước giờ bù không có bù (Kho phôi còn 0), sau đó có (2)");
            var op = new LedgerInput(); op.Bu = inp.Bu.ToList(); HandoverWhy.OpVoidBu(inp.Bu.Single().Id)(op);
            Ok(op.Bu.Count == 0, "Thao tác thử HandoverWhy.OpVoidBu bỏ đúng phiếu bù");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U7] Gợi ý gỡ (GN-WHY): huỷ báo phôi lỗi đã trả khách + đã được bù → gợi ý huỷ phiếu bù TRƯỚC rồi mới huỷ phiếu trả khách");
        {
            var db = Seed(); var svc = new HandoverService(db); WtsSvcHelp.Reset();
            var b1 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "phoi nut", null, Kho, All, ngCategoryId: 1);
            var vA = VuHelp.LastCase(db); var ngId = db.HandoverNgReports.List.Max(x => x.HandoverNgReportId);
            var k1 = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 2, Kho, HandoverPurpose.CustReturn, reason: "phoi nut", ngCaseId: vA); var txK = LastTx(db);
            var u1 = await svc.ReceiveReplacementAsync(1, vA, 2, null, Kho); var buId = LastBu(db);
            await Check(db, "U7 dựng");
            var v = await svc.VoidNgReportAsync(ngId, "bao nham", Kho, All);
            int iBu = v.Error.IndexOf("Phiếu khách gửi bù #" + buId, StringComparison.Ordinal), iK = v.Error.IndexOf("Kiểm tra Phiếu #" + txK + " · Kho → Khách hàng", StringComparison.Ordinal);
            Ok(b1.Ok && k1.Ok && u1.Ok && !v.Ok && v.Error.Contains("nên kiểm tra theo các bước sau") && iBu > 0 && iK > iBu && v.Error.Contains("huỷ phiếu khách gửi bù này ở Lịch sử phiếu"),
               $"Lời gợi ý: phiếu bù #{buId} trước phiếu trả khách #{txK}", v.Error);
            if (Environment.GetEnvironmentVariable("BUDBG") == "1") Console.WriteLine(v.Error);
            var s1 = await svc.VoidReplacementAsync(buId, "goi y", Kho);
            var s2 = await svc.VoidIssueAsync(txK, "goi y", Kho, All);
            var s3 = await svc.VoidNgReportAsync(ngId, "bao nham", Kho, All);
            Ok(s1.Ok && s2.Ok && s3.Ok, "Làm đúng theo gợi ý (huỷ bù → huỷ trả khách → huỷ báo lỗi): lưu được", s1.Error + s2.Error + s3.Error);
            await Check(db, "U7 cuối");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[U8] Gợi ý gỡ theo thời gian (rà độc lập T1): phôi bù đã giao đi trước lúc hàng sửa về Kho — bước huỷ bù phải xét như máy chủ (cả theo thời gian)");
        {
            var db = Seed(); var svc = new HandoverService(db); WtsSvcHelp.Reset();
            async Task<(bool Ok, string Error)> St(Task<(bool Ok, string Error)> t) { var r = await t; await Task.Delay(15); return r; }
            var r1 = await St(svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 4, HandoverNgSource.DauVao, "KHACH", "phoi nut", null, Kho, All, ngCategoryId: 1));
            var vC = VuHelp.LastCase(db);
            var r2 = await St(svc.IssueSpecialAsync(1, "KHO", "KHACH", 2, Kho, HandoverPurpose.CustReturn, reason: "phoi nut", ngCaseId: vC)); var txK = LastTx(db);
            var r3 = await St(svc.IssueByCellAsync(1, "KHO", "GC", 2, Kho, "LOI", false, "SUA", ngCaseId: vC));
            var r4 = await St(svc.ReceiveAsync(LastTx(db), 2, 0, null, Gc, null, HandoverCell.Sua));
            var r5 = await St(svc.ReceiveReplacementAsync(1, vC, 2, null, Kho)); var buId = LastBu(db);
            var r6 = await St(svc.IssueByCellAsync(1, "KHO", "GC", 18, Kho, "MOI", false, "MOI", toNC: "NC1")); var txG = LastTx(db);
            var r7 = await St(svc.ReceiveAsync(txG, 18, 0, null, Gc));
            var r8 = await St(VuHelp.Off(db, () => svc.IssueByCellAsync(1, "GC", "KHO", 2, Gc, "SUA", false, "XL", ngCaseId: vC)));   // dựng chứng từ kiểu cũ: sửa hộ xong trả Kho ô Hàng lỗi đã xử lý
            var r9 = await St(VuHelp.Off(db, () => svc.ReceiveAsync(LastTx(db), 2, 0, null, Kho)));
            var row = await Check(db, "U8 dựng");
            Ok(r1.Ok && r2.Ok && r3.Ok && r4.Ok && r5.Ok && r6.Ok && r7.Ok && r8.Ok && r9.Ok && row.G("KHO").ConGiao == 2,
               "Dựng: phôi 20 − báo lỗi 4 + bù 2 − giao 18 + hàng sửa về 2 = Kho phôi còn 2", string.Join("|", new[] { r1, r2, r3, r4, r5, r6, r7, r8, r9 }.Select(x => x.Error)));
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(1))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidIssue(txK), new HashSet<string> { "T:" + txK }, loopOp: true);
            Ok(plan.Complete && plan.Steps.Any(x => x.Kind == "BU" && x.Id == buId), "Huỷ phiếu trả khách: gợi ý có bước huỷ phiếu bù #" + buId, string.Join(" → ", plan.Steps.Select(x => x.Kind + "#" + x.Id)));
            string? bad = null;
            foreach (var st in plan.Steps)
            {
                var rs = st.Kind switch
                {
                    "BU" => await svc.VoidReplacementAsync(st.Id, "goi y", Kho),
                    "NHAN" => await svc.VoidReceiveAsync(st.Id, "goi y", Kho, All),
                    "BAOLOI" => await svc.VoidNgReportAsync(st.Id, "goi y", Kho, All),
                    "KQSUA" => await svc.VoidRepairResultAsync(st.Id, "goi y", Kho),
                    "XNSUA" => await svc.VoidRepairCheckAsync(st.Id, "goi y", Kho),
                    _ => await svc.VoidIssueAsync(st.Id, "goi y", Kho, All),
                };
                if (!rs.Ok) { bad = $"{st.Kind}#{st.Id}: {rs.Error}"; break; }
            }
            (bool Ok, string Error) fin = bad == null ? await svc.VoidIssueAsync(txK, "tra nham", Kho, All) : (false, "");
            Ok(bad == null && fin.Ok, "Làm theo từng bước gợi ý bằng hàm thật: mọi bước lưu được, cuối cùng huỷ được phiếu trả khách", bad ?? fin.Error);
            await Check(db, "U8 cuối");
        }

        // ─────────────────────────────────────────────────────────────────
        // [G14-KIEM-THEM 2026-11-12] Xoá / tách PO THẬT (KhPlanService) khi PO có phiếu khách gửi bù: bị chặn, lời báo kể phiếu bù, không mất dòng nào.
        //   Trước chỉ kiểm PoGuard.CountDocsAsync (U4). Nguồn: CLAUDE (rà độc lập Gói 14 L5, sổ G14-KIEM-THEM); HÙNG 12/11 08:11 OK làm.
        Console.WriteLine("[U9] Xoá / tách PO thật (KhPlanService) khi PO có phiếu bù: chặn, lời báo có phiếu bù, không mất dữ liệu");
        {
            var (db, svc, vA, _, _, _) = await Base();
            var u1 = await svc.ReceiveReplacementAsync(1, vA, 2, null, Kho);
            var vb = await svc.VoidReplacementAsync(LastBu(db), "bu thu huy", Kho);   // phiếu bù đã huỷ vẫn giữ PO (lịch sử)
            var sig0 = Sig((await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!);
            int nBu = db.HandoverReplacements.List.Count, nTx = db.HandoverTransactions.List.Count, nDet = db.KhPlanDetails.List.Count;
            var kh = new KhPlanService(db);
            var del = await kh.DeleteDetailAsync(1, 1, "tao nham PO");
            string? errS = null; try { await kh.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 10 }, new() { PurchaseOrder = "PO1B", Quantity = 10 } }, 1, "tach PO"); } catch (Exception ex) { errS = ex.Message; }
            var cnt = await PoGuard.CountDocsAsync(db, 1);
            Ok(u1.Ok && vb.Ok && cnt.BuAll == 1, "Dựng: 1 phiếu bù đã huỷ (PoGuard đếm cả phiếu đã huỷ)", u1.Error + vb.Error + " BuAll=" + cnt.BuAll);
            Ok(!del.Success && del.Error != null && del.Error.Contains("phiếu khách gửi bù"), "Xoá PO: chặn, lời báo kể phiếu khách gửi bù", del.Error);
            Ok(errS != null && errS.Contains("phiếu khách gửi bù") && errS.Contains("không tách được"), "Tách PO: chặn, lời báo kể phiếu khách gửi bù", errS);
            var r = await Check(db, "U9 sau xoá / tách bị chặn");
            Ok(db.HandoverReplacements.List.Count == nBu && db.HandoverTransactions.List.Count == nTx && db.KhPlanDetails.List.Count == nDet && Sig(r) == sig0,
               "Không mất / thêm dòng nào (phiếu bù, phiếu giao nhận, PO), sổ không đổi");
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
