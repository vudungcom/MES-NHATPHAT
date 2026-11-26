// [N19-SCEN 2026-11-26] Kịch bản cố định "n19": luật N19 Cách A — phôi khách gửi bù cho chiếc lỗi ĐÃ QUA Gia công: Kho giao Gia công bắt buộc chọn mã vụ,
//   Gia công nhận ô Hàng lỗi đã xử lý theo vụ, làm từ NC đầu; chiếc lỗi chưa qua Gia công (lỗi từ Kho): như N15 — phôi bù đi như phôi thường, không chọn mã vụ.
//   Nút "Nhận phôi bù" ở cột Nhận của Kho (máy chủ ReceiveReplacementAsync không đổi).
//   Nguồn: KHÁCH 23/11 (mockup TH1 + TH1b "Đúng + Cách A"; sheet dòng 8); HÙNG 23/11 16:24 (N19) + 26/11 03:49 OK bản "em hiểu" G-F.
//   Đáp án độc lập = bảng giao nhận của mockup TH1 / TH1b (số chép tay từ mockup, không lấy từ code) + luật Claude tự chốt (BuQuaGc, giữ chỗ phôi bù, phiếu bù cũ).
//   Bằng hàm thật HandoverService.SaveFormAsync / ReceiveReplacementAsync / ReceiveAsync / Void*Async / ReportNgAfterReceiveAsync + sổ chung.
//   Sau MỖI bước ghi: Check() chung của kịch bản sua (cân PO, không ô âm, vụ cân, khung lỗi, dòng B cuối = sổ, Đang ở đâu) + CheckBu() (đáp án đọc thẳng bảng).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    /// <summary>PO mockup TH1 / TH1b: phôi 10, Gia công NC1 → NC2, KCS K1, Đóng gói P1.</summary>
    static AppDbContext SeedN19(bool workflow = true, bool firstExcluded = false)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-20261106-0005", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "4S-PN1114510", PurchaseOrder = "PO1", Quantity = 10, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 10, NgayNhanPhoi = DateTime.Now.AddHours(-1), SoPO = "PO1", PartNo = "4S-PN1114510" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 3, KhPlanDetailId = 1, StepOrder = 3, NC = "NC3", MachineRegistered = "M3" });
        if (firstExcluded)
            db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 1, KhPlanDetailId = 1, SoMay = "M1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    /// <summary>Đáp án độc lập phần bù (đọc thẳng bảng, không qua sổ): đã bù / đã giao theo vụ mỗi vụ, cân vụ có "Bù vào", đã giao theo vụ ≤ đã bù.</summary>
    static async Task<LedgerRow> CheckBu(AppDbContext db, string buoc)
    {
        var row = await Check(db, buoc);
        var bad = new List<string>();
        var buDb = db.HandoverReplacements.List.Where(b => b.KhPlanDetailId == 1 && !b.IsVoided).ToList();
        if (row.Bu != buDb.Sum(b => b.Qty)) bad.Add($"sổ bù {row.Bu} ≠ bảng {buDb.Sum(b => b.Qty)}");
        var bvTx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.ToCell == HandoverCell.BuVu).ToList();
        foreach (var c in row.Cases.Values.Where(c => c.Id > 0))
        {
            var giao = bvTx.Where(t => t.NgCaseId == c.Id).Sum(t => t.QtyIssued);   // QtyIssued đã trừ phần huỷ chưa nhận
            var moi = buDb.Where(b => b.NgCaseId == c.Id && b.ReceivedAt >= HandoverLedgerService.BuVuFrom).Sum(b => b.Qty);
            if (c.BuVao != giao) bad.Add($"{c.No} đã giao theo vụ sổ {c.BuVao} ≠ bảng {giao}");
            if (c.DaBuMoi != moi) bad.Add($"{c.No} phôi bù (từ G-F) sổ {c.DaBuMoi} ≠ bảng {moi}");
            if (c.BuVao > c.DaBuMoi) bad.Add($"{c.No} đã giao theo vụ {c.BuVao} > phôi bù {c.DaBuMoi}");
            var nhan = bvTx.Where(t => t.NgCaseId == c.Id).SelectMany(t => db.HandoverReceives.List.Where(r => r.HandoverTxId == t.HandoverTxId && !r.IsVoided)).Sum(r => r.QtyOk + r.QtyNg);
            if (c.BuVao - nhan < 0) bad.Add($"{c.No} nhận phôi bù {nhan} > đã giao {c.BuVao}");
        }
        foreach (var t in bvTx)
            if (t.FromGroupCode != "KHO" || t.ToGroupCode != "GC" || t.FromCell != HandoverCell.Moi || t.Purpose != HandoverPurpose.New || t.NgCaseId == null)
                bad.Add($"phiếu bù theo vụ #{t.HandoverTxId} sai dạng");
        var phoi = db.KhoVatLieus.List.Where(k => k.PlanDetailId == 1 && k.IsActive).Sum(k => (decimal)(k.SoLuongThucNhan ?? 0));
        if (phoi + buDb.Sum(b => b.Qty) != row.TonTong + row.DangGiao + row.TpNhap + row.DaHuy + row.TraKhach) bad.Add("PO không cân (phôi + bù, đáp án độc lập)");
        Ok(bad.Count == 0, $"[bù] {buoc}: đã bù / đã giao theo vụ = bảng, cân vụ có Bù vào", string.Join("\n", bad));
        return row;
    }

    /// <summary>TH1b dựng tới trước bù: Kho → Gia công 10 (nhận đủ) · WTS NC1 OK 10 · Gia công báo 1 chiếc lỗi ở "Đã làm xong NC 1 · chưa làm NC 2" → Trả khách 1. Trả mã vụ.</summary>
    static async Task<long> PreTh1b(AppDbContext db, HandoverService svc)
    {
        var g = await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", toNC: "NC1");
        var n = await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
        var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, Gc);
        var k = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Action = HandoverForm.AKhach; r.Qty = 1; r.CatId = 1; });
        var tx = Tx(db);
        Ok(g.Ok && n.Ok && w.Ok && k.Ok && tx.Purpose == HandoverPurpose.CustReturn && tx.FromGroupCode == "GC" && tx.NgCaseId > 0,
           "TH1b bước 1: Gia công đã làm NC 1 cho 10 chiếc, phát hiện 1 chiếc lỗi → Trả khách 1 (mã vụ VL…)", $"{g.Error}|{n.Error}|{w.Err}|{k.Error}");
        return tx.NgCaseId!.Value;
    }

    /// <summary>Kho giao phôi (form Hàng tốt → Phôi còn → Gia công), bvu = "Bù cho mã vụ" (null = phôi thường).</summary>
    static Task<(bool Ok, string Error)> KhoGiao(HandoverService svc, decimal qty, long? bvu, string? toCell = null)
        => Form(svc, r => { r.UserId = 1; r.Group = "KHO"; r.Kind = HandoverForm.Tot; r.Pos = "PHOI"; r.Action = HandoverForm.AGiao; r.ToGroup = "GC"; r.Qty = qty; r.CaseId = bvu; r.ToCell = toCell; });

    public static async Task<int> RunN19()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G1] TH1 (mockup): Kho nhận 10, giao Gia công 9 → 1 phôi lỗi → trả khách → khách gửi bù → Kho giao Gia công như phôi thường (không chọn mã vụ)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var g9 = await svc.IssueByCellAsync(1, "KHO", "GC", 9, 1, "MOI", false, "MOI", toNC: "NC1");
            var n9 = await svc.ReceiveAsync(LastTx(db), 9, 0, null, Gc);
            var tk = await Form(svc, r => { r.UserId = 1; r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "PHOI"; r.Prev = true; r.Action = HandoverForm.AKhach; r.Qty = 1; r.CatId = 1; });
            var vu = Tx(db).NgCaseId!.Value;
            var a = await CheckBu(db, "G1 trước bù");
            var K = a.G("KHO");
            // đáp án = cột "Trước bù" bảng TH1 (chép tay)
            Ok(g9.Ok && n9.Ok && tk.Ok && K.NhanMoi == 10 && K.NhanLoiXl == 0 && K.GiaoMoi == 9 && a.TraKhach == 1 && K.Ton == 0 && a.G("GC").NhanMoi == 9,
               "Mockup TH1 trước bù: Kho Nhận·Hàng mới 10 · Nhận·Hàng lỗi đã xử lý 0 · Giao·Hàng mới 9 · trả khách 1 · Tồn 0 · Gia công Nhận·Hàng mới 9",
               $"{g9.Error}|{n9.Error}|{tk.Error} | Kho nm {K.NhanMoi} nx {K.NhanLoiXl} gm {K.GiaoMoi} tk {a.TraKhach} ton {K.Ton} · GC nm {a.G("GC").NhanMoi}");
            Ok(!HandoverLedgerService.BuQuaGc(a, a.Cases[vu]) && HandoverLedgerService.BuCases(a).Select(c => c.Id).SequenceEqual(new[] { vu }),
               "Vụ do Kho phát hiện (phôi khách, chưa qua Gia công): nhận bù được, KHÔNG thuộc bù theo mã vụ");
            var b = await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            var c = await CheckBu(db, "G1 sau bước 2 (Kho nhận phôi bù)");
            K = c.G("KHO");
            Ok(b.Ok && K.NhanMoi == 10 && K.NhanLoiXl == 1 && K.GiaoMoi == 9 && K.Ton == 1 && K.ConGiao == 1,
               "Mockup TH1 sau bước 2: Kho Nhận·Hàng lỗi đã xử lý 1 · Tồn 1 (phôi dùng được)", $"{b.Error} | nx {K.NhanLoiXl} ton {K.Ton} con {K.ConGiao}");
            Ok(HandoverLedgerService.BuVuCases(c).Count == 0 && HandoverLedgerService.BuVuHold(c) == 0 && HandoverLedgerService.KhoBuTrongPhoiCon(c) == 1,
               "Bước 3 form: không có ô \"Bù cho mã vụ\" (không vụ nào chờ giao theo vụ); dòng Phôi còn \"gồm 1 phôi khách gửi bù\"");
            var bad = await KhoGiao(svc, 1, vu);
            Ok(!bad.Ok && bad.Error.Contains("không có phôi khách gửi bù chờ giao Gia công theo vụ") && db.HandoverTransactions.List.Count(t => t.ToCell == HandoverCell.BuVu) == 0,
               "Cố gửi bù theo mã vụ cho vụ chưa qua Gia công: máy chủ chặn, không ghi", bad.Error);
            var ok = await KhoGiao(svc, 1, null);
            var t3 = Tx(db);
            var d = await CheckBu(db, "G1 sau bước 3 (Kho giao phôi bù như phôi thường)");
            K = d.G("KHO");
            Ok(ok.Ok && t3.ToCell == HandoverCell.Moi && t3.NgCaseId == null && t3.ToNC == null,
               "Bước 3: phiếu Kho → Gia công Hàng mới 1, không mã vụ (không chọn NC = vào NC đầu như phôi)", ok.Error + $" | {t3.ToCell} vụ {t3.NgCaseId} NC {t3.ToNC}");
            var n1 = await svc.ReceiveAsync(t3.HandoverTxId, 1, 0, null, Gc);
            var e = await CheckBu(db, "G1 Gia công nhận");
            K = e.G("KHO");
            Ok(n1.Ok && K.NhanMoi == 10 && K.NhanLoiXl == 1 && K.GiaoMoi == 10 && e.TraKhach == 1 && K.Ton == 0 && e.G("GC").NhanMoi == 10 && e.G("GC").NhanLoiXl == 0,
               "Mockup TH1 sau bước 3: Kho Giao·Hàng mới 10 · Tồn 0 · Gia công Nhận·Hàng mới 10", $"{n1.Error} | Kho gm {K.GiaoMoi} ton {K.Ton} · GC nm {e.G("GC").NhanMoi} nx {e.G("GC").NhanLoiXl}");
            Ok(e.Cases[vu].BuVao == 0 && e.Cases[vu].DaBu == 1 && e.Cases[vu].Lech == 0, "Vụ TH1: đã bù 1 (chỉ tra), không có Bù vào, vẫn cân");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G2] TH1b (mockup, Cách A): Gia công đã làm NC 1 → 1 chiếc lỗi → trả khách → bù → Kho giao Gia công theo mã vụ → Gia công nhận ô Hàng lỗi đã xử lý, chờ NC 1");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            var a = await CheckBu(db, "G2 trước bù");
            var K = a.G("KHO"); var G = a.G("GC");
            // đáp án = cột "Trước bù" bảng TH1b (chép tay)
            Ok(K.NhanMoi == 10 && K.NhanLoiXl == 0 && K.GiaoMoi == 10 && K.Ton == 0 && G.NhanMoi == 10 && G.NhanLoiXl == 0 && a.TraKhach == 1 && G.Ton == 9,
               "Mockup TH1b trước bù: Kho Nhận·Hàng mới 10 · Giao·Hàng mới 10 · Tồn 0 · Gia công Nhận·Hàng mới 10 · trả khách 1 · Tồn 9",
               $"Kho nm {K.NhanMoi} nx {K.NhanLoiXl} gm {K.GiaoMoi} ton {K.Ton} · GC nm {G.NhanMoi} nx {G.NhanLoiXl} ton {G.Ton} · tk {a.TraKhach}");
            Ok(HandoverLedgerService.BuQuaGc(a, a.Cases[vu]) && a.Cases[vu].DetectGroup == "GC", "Vụ Gia công phát hiện ở hàng đã làm NC 1: ĐÃ QUA Gia công");
            var b = await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            var c = await CheckBu(db, "G2 sau bước 2");
            K = c.G("KHO");
            Ok(b.Ok && K.NhanLoiXl == 1 && K.Ton == 1 && K.ConGiao == 1, "Mockup TH1b sau bước 2: Kho Nhận·Hàng lỗi đã xử lý (phôi bù) 1 · Tồn 1", b.Error);
            Ok(HandoverLedgerService.BuVuCases(c).Select(x => x.Id).SequenceEqual(new[] { vu }) && HandoverLedgerService.BuVuCap(c, vu).Max == 1
               && HandoverLedgerService.BuVuHold(c) == 1 && HandoverLedgerService.KhoMoiThuongCap(c) == 0 && HandoverLedgerService.KhoBuTrongPhoiCon(c) == 1,
               "Bước 3 form: ô \"Bù cho mã vụ\" có đúng VL… (chờ 1), trần 1; phôi thường còn giao 0");
            var thuong = await KhoGiao(svc, 1, null);
            Ok(!thuong.Ok && thuong.Error.Contains("phải giao Gia công theo mã vụ") && thuong.Error.Contains(NgCaseNo.Format(vu)),
               "Workflow ON: giao phôi bù như phôi thường (không chọn mã vụ) bị chặn, lời báo chỉ ô \"Bù cho mã vụ\"", thuong.Error);
            var vuot = await KhoGiao(svc, 2, vu);
            Ok(!vuot.Ok && vuot.Error.Contains("tối đa 1"), "Bù cho mã vụ 2 > 1: chặn", vuot.Error);
            var oSai = await KhoGiao(svc, 1, vu, HandoverCell.Moi);
            Ok(!oSai.Ok && oSai.Error.Contains("Hàng lỗi đã xử lý"), "Bù cho mã vụ mà chọn ô nhận Hàng mới: chặn", oSai.Error);
            var ok = await KhoGiao(svc, 1, vu);
            var t3 = Tx(db);
            var d = await CheckBu(db, "G2 sau bước 3 (Kho giao theo mã vụ)");
            Ok(ok.Ok && t3.FromGroupCode == "KHO" && t3.ToGroupCode == "GC" && t3.FromCell == HandoverCell.Moi && t3.ToCell == HandoverCell.BuVu && t3.NgCaseId == vu
               && t3.ToNC == "NC1" && t3.Purpose == HandoverPurpose.New && t3.QtyIssued == 1 && t3.NgQty == 0 && t3.Status == "PENDING",
               "Mockup TH1b bước 3: phiếu Kho → Gia công 1, mang mã vụ VL…, ô chỉ định XLBU, NC 1",
               ok.Error + $" | {t3.FromCell}→{t3.ToCell} vụ {t3.NgCaseId} NC {t3.ToNC} {t3.Purpose}");
            Ok(d.Cases[vu].BuVao == 1 && d.Cases[vu].Transit == 1 && d.G("KHO").GiaoMoi == 11 && d.G("KHO").Ton == 0 && d.G("GC").ChoNhan == 1,
               "Sau bước 3: vụ Bù vào 1 đang chuyển 1 · Kho Giao·Hàng mới 11, Tồn 0 · Gia công chờ nhận 1");
            var dup = await KhoGiao(svc, 1, vu);
            Ok(!dup.Ok && dup.Error.Contains("tối đa 0"), "Đã giao hết phôi bù theo vụ: giao thêm bị chặn", dup.Error);
            var pend = (await svc.GetPendingForGroupAsync(1, "GC")).SingleOrDefault(p => p.HandoverTxId == t3.HandoverTxId);
            Ok(pend != null && pend.BuVu && pend.ToCell == HandoverCell.Xl && pend.NgCaseId == vu && pend.ToNC == "NC1",
               "Thẻ chờ nhận của Gia công: phiếu bù theo mã vụ, ô chỉ định Hàng lỗi đã xử lý, VL…, NC 1", $"{pend?.BuVu} {pend?.ToCell} {pend?.NgCaseId} {pend?.ToNC}");
            var oN = await svc.ReceiveAsync(t3.HandoverTxId, 1, 0, null, Gc, toCell: HandoverCell.Moi, toCellReason: "thu doi o");
            Ok(!oN.Ok && oN.Error == HandoverService.BuVuOnlyXlMsg, "Workflow ON: Gia công đổi ô nhận phiếu bù sang Hàng mới — chặn", oN.Error);
            var n = await svc.ReceiveAsync(t3.HandoverTxId, 1, 0, null, Gc);
            var e = await CheckBu(db, "G2 sau bước 4 (Gia công nhận)");
            var rc = db.HandoverReceives.List.Last();
            K = e.G("KHO"); G = e.G("GC");
            // đáp án = cột "Sau bước 4 — cách A" bảng TH1b (chép tay)
            Ok(n.Ok && rc.ToCell == HandoverCell.Xl && K.NhanMoi == 10 && K.NhanLoiXl == 1 && K.GiaoMoi == 11 && K.Ton == 0
               && G.NhanMoi == 10 && G.NhanLoiXl == 1 && e.TraKhach == 1 && G.Ton == 10,
               "Mockup TH1b sau bước 4 (Cách A): Kho Nhận·Hàng mới 10 · Nhận·Hàng lỗi đã xử lý 1 · Giao·Hàng mới 11 · Tồn 0 · Gia công Nhận·Hàng mới 10 · Nhận·Hàng lỗi đã xử lý (bù theo vụ) 1 · trả khách 1 · Tồn 10",
               $"{n.Error} | ô {rc.ToCell} · Kho nm {K.NhanMoi} nx {K.NhanLoiXl} gm {K.GiaoMoi} ton {K.Ton} · GC nm {G.NhanMoi} nx {G.NhanLoiXl} ton {G.Ton} tk {e.TraKhach}");
            var s1 = G.Steps.Single(s => s.Code == "NC1"); var s2 = G.Steps.Single(s => s.Code == "NC2");
            Ok(s1.Waiting == 1 && s2.Waiting == 9, "Mockup: Gia công Tồn 10 = 9 đã làm NC 1 (chờ NC 2) + 1 phôi bù chờ NC 1", $"NC1 chờ {s1.Waiting} NC2 chờ {s2.Waiting}");
            var cs = e.Cases[vu];
            Ok(cs.SinhRa == 1 && cs.BuVao == 1 && cs.TraKhach == 1 && cs.DaXuLyTotal == 1 && cs.DaXuLy["GC"] == 1 && cs.Transit == 0 && cs.Lech == 0 && cs.BuCho == 0,
               "Vụ: Sinh ra 1 + Bù vào 1 = Trả khách 1 + Đã xử lý ở Gia công 1 (cân); còn chờ bù 0");
            var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 1, 0, Gc);
            var f = await CheckBu(db, "G2 Gia công làm NC 1 cho phôi bù");
            Ok(w.Ok && f.G("GC").Steps.Single(s => s.Code == "NC2").Waiting == 10, "Phôi bù làm NC 1 như phôi thường → NC 2 chờ 10", w.Err);
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(1))!;
            var st = HandoverLedgerService.ComputeSteps(inp);
            var sg = st.Single(x => x.Kind == "GIAO" && x.RefId == t3.HandoverTxId);
            Ok(sg.Text.Contains($"phôi khách gửi bù theo mã vụ {NgCaseNo.Format(vu)}") && sg.Text.Contains("Gia công ô Hàng lỗi đã xử lý (làm từ NC1)"), $"Dòng B bước giao: «{sg.Text}»");
            var tr = HandoverNgTrace.BuildCases(f).Single(x => x.Id == vu);
            Ok(tr.Balanced && tr.BuVao == 1 && tr.DaBuText.Contains("giao Gia công theo mã vụ 1") && tr.DaXuLy.Contains("Gia công 1")
               && tr.Hops.Any(h => h.Kind == "BUGIAO" && HandoverNgTrace.HopLabel(h).Contains("phôi khách gửi bù")) && tr.Hops.Any(h => h.Kind == "BUNHAN"),
               "Bảng theo vụ: Đã bù … · giao Gia công theo mã vụ 1, Đã xử lý Gia công 1, có chặng Kho giao / Gia công nhận phôi bù", $"{tr.DaBuText} | {tr.DaXuLy}");
            var log = (await svc.GetLogsAsync(1)).Single(x => x.HandoverTxId == t3.HandoverTxId);
            Ok(log.BuVu && log.NgCaseId == vu, "Lịch sử phiếu: cờ phiếu bù theo mã vụ + mã vụ");
            Ok(HandoverService.EditIssueBlock(t3.Purpose, null, t3.ToCell, "PENDING", false, null)?.Contains("huỷ phiếu rồi lập lại") == true,
               "Phiếu bù theo mã vụ: không có Sửa phiếu (huỷ rồi lập lại)");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G3] Huỷ: phiếu bù đã giao Gia công theo mã vụ → huỷ phiếu bù bị chặn (ON: K5 + gợi ý gỡ; OFF: chặn riêng) — gỡ phiếu theo vụ trước thì được");
        foreach (var on in new[] { true, false })
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(workflow: on); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            await svc.ReceiveReplacementAsync(1, vu, 1, "PX-9", 1);
            var buId = db.HandoverReplacements.List.Single().ReplacementId;
            var g = await KhoGiao(svc, 1, vu);
            var txb = Tx(db);
            var n = await svc.ReceiveAsync(txb.HandoverTxId, 1, 0, null, Gc);
            var sig0 = Sig(await CheckBu(db, $"G3 {(on ? "ON" : "OFF")} dựng"));
            var v = await svc.VoidReplacementAsync(buId, "nhap nham", 1);
            Ok(g.Ok && n.Ok && !v.Ok && (on ? v.Error.Contains("đã giao Gia công theo mã vụ") && v.Error.Contains("#" + txb.HandoverTxId) : v.Error.Contains("Huỷ phiếu Kho → Gia công bù theo mã vụ trước"))
               && !db.HandoverReplacements.List.Single().IsVoided,
               $"{(on ? "ON" : "OFF")}: huỷ phiếu bù khi phôi bù đã giao theo vụ — chặn" + (on ? ", gợi ý gỡ chỉ phiếu #" + txb.HandoverTxId : ""), v.Error);
            Ok(Sig(await CheckBu(db, $"G3 {(on ? "ON" : "OFF")} sau lần chặn")) == sig0, "Bị chặn: sổ y như trước");
            var rId = db.HandoverReceives.List.Single(r => r.HandoverTxId == txb.HandoverTxId).ReceiveId;
            var vr = await svc.VoidReceiveAsync(rId, "nhan nham", 1, All5);
            var vi = await svc.VoidIssueAsync(txb.HandoverTxId, "giao nham", 1, All5);
            var a = await CheckBu(db, $"G3 {(on ? "ON" : "OFF")} huỷ phiếu theo vụ");
            Ok(vr.Ok && vi.Ok && a.Cases[vu].BuVao == 0 && a.Cases[vu].BuCho == 1 && HandoverLedgerService.BuVuHold(a) == 1, "Huỷ lần nhận + phiếu bù theo vụ: vụ chờ giao lại 1", vr.Error + vi.Error);
            var v2 = await svc.VoidReplacementAsync(buId, "nhap nham", 1);
            var b = await CheckBu(db, $"G3 {(on ? "ON" : "OFF")} huỷ phiếu bù");
            Ok(v2.Ok && b.Bu == 0 && HandoverLedgerService.BuVuHold(b) == 0 && b.Cases[vu].DaBuMoi == 0, "Rồi huỷ phiếu bù: được, hết phôi bù chờ", v2.Error);
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G4] Gia công đếm NG lúc nhận phôi bù → vụ MỚI (phôi lỗi, chưa qua Gia công) trả Kho; vụ cũ đã bù xong");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            await KhoGiao(svc, 1, vu);
            var txb = Tx(db);
            var n = await svc.ReceiveAsync(txb.HandoverTxId, 0, 1, null, Gc, ngCategoryId: 2);
            var a = await CheckBu(db, "G4 NG lúc nhận phôi bù");
            var back = db.HandoverTransactions.List.Single(t => t.SourceReceiveId != null);
            var nv = back.NgCaseId!.Value;
            Ok(n.Ok && nv != vu && a.Cases[vu].BuVao == 1 && a.Cases[vu].DaXuLy["GC"] == 1 && a.Cases[vu].Lech == 0
               && a.Cases[nv].SinhRa == 1 && a.Cases[nv].DetectGroup == "GC" && a.Cases[nv].Cause == "KHO" && !HandoverLedgerService.BuQuaGc(a, a.Cases[nv])
               && back.ToGroupCode == "KHO" && a.G("GC").Steps.Single(s => s.Code == "NC1").Waiting == 0,
               "Vụ cũ: Bù vào 1 → Đã xử lý Gia công 1 (cân); NG = vụ mới Gia công phát hiện, nơi gây lỗi Kho, CHƯA qua Gia công (bù lại sẽ như TH1); phiếu trả tự lập về Kho; NC 1 không có phôi bù chờ",
               n.Error + $" | vụ mới {nv} cause {a.Cases[nv].Cause}");
            var k = await svc.ReceiveAsync(back.HandoverTxId, 0, 1, null, 1);
            var tk = await Form(svc, r => { r.UserId = 1; r.Group = "KHO"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = nv; r.Action = HandoverForm.AKhach; r.Qty = 1; });
            var bu2 = await svc.ReceiveReplacementAsync(1, nv, 1, null, 1);
            var c = await CheckBu(db, "G4 vụ mới trả khách + bù");
            var ok = await KhoGiao(svc, 1, null);
            Ok(k.Ok && tk.Ok && bu2.Ok && HandoverLedgerService.BuVuHold(c) == 0 && ok.Ok, "Vụ mới (chưa qua Gia công): bù đi như phôi thường", $"{k.Error}|{tk.Error}|{bu2.Error}|{ok.Error}");
            await CheckBu(db, "G4 Kho giao phôi thường");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G5] \"Đã qua Gia công\" theo nơi phát hiện (BuQuaGc) — đáp án luật Claude tự chốt");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 6, 1, "MOI", false, "MOI", toNC: "NC1");
            await svc.ReceiveAsync(LastTx(db), 5, 1, null, Gc, ngCategoryId: 1);   // NG lúc nhận phôi Kho giao → chưa qua
            var vNhan = db.HandoverTransactions.List.Single(t => t.SourceReceiveId != null).NgCaseId!.Value;
            var b1 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 1, HandoverNgSource.DauVao, "KHO", "", null, Gc, All5, ngCategoryId: 1);   // chưa làm NC 1 → chưa qua
            var vNc1 = db.NgCases.List.Max(c => c.NgCaseId);
            await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 3, 0, Gc);
            var b2 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC2", 1, HandoverNgSource.DauRa, null, "", null, Gc, All5, ngCategoryId: 1);   // đã làm NC 1 → đã qua
            var vNc2 = db.NgCases.List.Max(c => c.NgCaseId);
            var b3 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 1, HandoverNgSource.DauVao, "KHACH", "", null, 1, All5, ngCategoryId: 1);   // Kho → chưa qua
            var vKho = db.NgCases.List.Max(c => c.NgCaseId);
            var r = await CheckBu(db, "G5 dựng");
            Ok(b1.Ok && b2.Ok && b3.Ok, "Dựng 4 vụ", $"{b1.Error}|{b2.Error}|{b3.Error}");
            Ok(!HandoverLedgerService.BuQuaGc(r, r.Cases[vNhan]) && !HandoverLedgerService.BuQuaGc(r, r.Cases[vNc1])
               && HandoverLedgerService.BuQuaGc(r, r.Cases[vNc2]) && !HandoverLedgerService.BuQuaGc(r, r.Cases[vKho]),
               "NG lúc nhận phôi (nơi gây lỗi Kho): chưa qua · báo lỗi hàng chưa làm NC 1: chưa qua · báo lỗi hàng đã làm NC 1: ĐÃ QUA · Kho phát hiện: chưa qua",
               $"{HandoverLedgerService.BuQuaGc(r, r.Cases[vNhan])} {HandoverLedgerService.BuQuaGc(r, r.Cases[vNc1])} {HandoverLedgerService.BuQuaGc(r, r.Cases[vNc2])} {HandoverLedgerService.BuQuaGc(r, r.Cases[vKho])}");
            // WTS NG ở Gia công (vụ WTS) → đã qua
            var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 0, 1, Gc, catId: 1);
            var vW = db.WtsProductionLogs.List.Single(z => z.WtsLogId == w.LogId).NgCaseId ?? 0;
            var r2 = await CheckBu(db, "G5 WTS NG");
            Ok(w.Ok && vW > 0 && r2.Cases[vW].DetectGroup == "GC" && HandoverLedgerService.BuQuaGc(r2, r2.Cases[vW]), "Vụ WTS NG Gia công: ĐÃ QUA", w.Err);
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G6] Phiếu bù nhận TRƯỚC bản G-F (giờ < BuVuFrom): giữ nghĩa cũ — không giữ chỗ, không chọn mã vụ");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            db.HandoverReplacements.List.Single().ReceivedAt = HandoverLedgerService.BuVuFrom.AddMinutes(-5);
            var a = await CheckBu(db, "G6 phiếu bù cũ");
            Ok(a.Cases[vu].DaBu == 1 && a.Cases[vu].DaBuMoi == 0 && HandoverLedgerService.BuVuHold(a) == 0 && HandoverLedgerService.BuVuCases(a).Count == 0,
               "Phiếu bù cũ: đã bù 1 (tra), không chờ giao theo vụ");
            var ok = await KhoGiao(svc, 1, null);
            var bad = await KhoGiao(svc, 1, vu);
            Ok(ok.Ok && !bad.Ok, "Giao phôi thường được; chọn mã vụ thì chặn (không có phôi bù chờ theo vụ)", ok.Error + " | " + bad.Error);
            await CheckBu(db, "G6 giao");
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G7] Workflow OFF: giao phôi thường lấy cả phôi bù chờ theo vụ — lưu được; Gia công nhận đổi ô (có lý do) — lưu được");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(workflow: false); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            var ok = await KhoGiao(svc, 1, null);
            var a = await CheckBu(db, "G7 OFF giao thường");
            Ok(ok.Ok && HandoverLedgerService.BuVuHold(a) == 1 && HandoverLedgerService.BuVuCap(a, vu).Max == 0, "OFF: lưu được; vụ vẫn còn chờ 1 nhưng Kho hết phôi (trần theo vụ 0)", ok.Error);
            WtsSvcHelp.Reset();
            var db2 = SeedN19(workflow: false); var svc2 = new HandoverService(db2);
            var vu2 = await PreTh1b(db2, svc2);
            await svc2.ReceiveReplacementAsync(1, vu2, 1, null, 1);
            var g = await KhoGiao(svc2, 1, vu2);
            var n = await svc2.ReceiveAsync(Tx(db2).HandoverTxId, 1, 0, null, Gc, toCell: HandoverCell.Moi, toCellReason: "nhan nhu phoi");
            var b = await CheckBu(db2, "G7 OFF nhận đổi ô Hàng mới");
            Ok(g.Ok && n.Ok && b.Cases[vu2].DaXuLy["GC"] == 1 && b.Cases[vu2].Lech == 0 && b.G("GC").NhanMoi == 11, "OFF: phiếu bù nhận vào ô Hàng mới (lý do) — vụ vẫn bù xong, cân", g.Error + n.Error);
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G8] Huỷ phần chưa nhận của phiếu bù theo vụ (giao 2, nhận 1) → vụ chờ giao lại 1; NC đầu là máy loại trừ → phiếu không NC (dòng Nhận vào)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var vu = await PreTh1b(db, svc);
            var x2 = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Action = HandoverForm.AKhach; r.Qty = 2; r.CatId = 1; });
            var vb = Tx(db).NgCaseId!.Value;
            await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
            await svc.ReceiveReplacementAsync(1, vb, 2, null, 1);
            var a0 = await CheckBu(db, "G8 2 vụ chờ bù");
            var g1 = await KhoGiao(svc, 2, vu);
            Ok(x2.Ok && HandoverLedgerService.BuVuHold(a0) == 3 && !g1.Ok && g1.Error.Contains("tối đa 1"), "2 vụ chờ bù 1 + 2: mỗi phiếu chỉ 1 vụ, trần theo vụ A 1", x2.Error + g1.Error);
            var ga = await KhoGiao(svc, 1, vu); var ta = Tx(db);
            var gb = await KhoGiao(svc, 2, vb); var tb = Tx(db);
            await svc.ReceiveAsync(ta.HandoverTxId, 1, 0, null, Gc);
            await svc.ReceiveAsync(tb.HandoverTxId, 1, 0, null, Gc);
            var cl = await svc.CloseRemainderAsync(tb.HandoverTxId, "khach chua gui", 1, All5);
            var a = await CheckBu(db, "G8 huỷ phần chưa nhận");
            Ok(ga.Ok && gb.Ok && cl.Ok && a.Cases[vb].BuVao == 1 && a.Cases[vb].BuCho == 1 && a.Cases[vu].BuCho == 0 && HandoverLedgerService.BuVuHold(a) == 1,
               "Phiếu vụ B giao 2 nhận 1, huỷ phần chưa nhận 1: vụ B đã giao theo vụ 1, chờ giao lại 1; vụ A xong", ga.Error + gb.Error + cl.Error);
            WtsSvcHelp.Reset();
            var dbx = SeedN19(firstExcluded: true); var svx = new HandoverService(dbx);
            var gx = await svx.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            var nx = await svx.ReceiveAsync(LastTx(dbx), 10, 0, null, Gc);
            var hs = await svx.IssueSpecialAsync(1, "GC", "HTSP", 10, Gc, HandoverPurpose.HelpSend, fromNC: HandoverLedgerService.VaoCode);
            var hr = await svx.ReceiveAsync(LastTx(dbx), 10, 0, null, Other);
            var ht = await svx.IssueSpecialAsync(1, "HTSP", "GC", 10, Other, HandoverPurpose.HelpReturn, toNC: "NC2");
            var hn = await svx.ReceiveAsync(LastTx(dbx), 10, 0, null, Gc);
            var kx = await Form(svx, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Action = HandoverForm.AKhach; r.Qty = 1; r.CatId = 1; });
            var vx = Tx(dbx).NgCaseId!.Value;
            await svx.ReceiveReplacementAsync(1, vx, 1, null, 1);
            var bx = await KhoGiao(svx, 1, vx);
            var tx = Tx(dbx);
            var cx = await CheckBu(dbx, "G8 NC đầu máy loại trừ");
            Ok(gx.Ok && nx.Ok && hs.Ok && hr.Ok && ht.Ok && hn.Ok && kx.Ok && HandoverLedgerService.BuQuaGc(cx, cx.Cases[vx]) && bx.Ok && tx.ToNC == null && tx.ToCell == HandoverCell.BuVu,
               "NC đầu máy loại trừ: chiếc đã làm hộ NC 1 → đã qua; phiếu bù theo vụ không NC (vào dòng Nhận vào)",
               $"{gx.Error}|{nx.Error}|{hs.Error}|{hr.Error}|{ht.Error}|{hn.Error}|{kx.Error}|{bx.Error} NC {tx.ToNC}");
            var nn = await svx.ReceiveAsync(tx.HandoverTxId, 1, 0, null, Gc);
            var dx = await CheckBu(dbx, "G8 nhận vào dòng Nhận vào");
            Ok(nn.Ok && dx.G("GC").Steps.Single(s => HandoverLedgerService.IsVao(s.Code)).Waiting == 1, "Phôi bù chờ ở dòng \"Nhận vào\" (chờ giao theo NC làm NC 1)", nn.Error);
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G9] [N19-KHACH-SAUGC] TH1b bước 1 tích \"Lỗi công đoạn trước\" ở hàng đã gia công: chỉ nơi gây lỗi Khách hàng (Workflow ON)");
        foreach (var on in new[] { true, false })
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(workflow: on); var svc = new HandoverService(db);
            var g = await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", toNC: "NC1");
            var n = await svc.ReceiveAsync(LastTx(db), 10, 0, null, Gc);
            var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, Gc);
            var row0 = await CheckBu(db, $"G9 {(on ? "ON" : "OFF")} dựng");
            var p2 = HandoverForm.FormPositions(row0, "GC", false).Single(p => p.Id == "NC:NC2");
            Ok(p2.PrevOk && p2.PrevKhach == on, $"{(on ? "ON" : "OFF")}: dòng \"Đã làm xong NC 1\" tích được Lỗi công đoạn trước{(on ? ", chỉ Khách hàng" : " (không giới hạn)")}", $"PrevOk {p2.PrevOk} PrevKhach {p2.PrevKhach}");
            var c0 = db.HandoverTransactions.List.Count;
            var kho = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Prev = true; r.Cause = "KHO"; r.Action = HandoverForm.AKhach; r.Qty = 1; r.CatId = 1; });
            if (on) Ok(!kho.Ok && kho.Error == HandoverForm.GcPrevKhachMsg && db.HandoverTransactions.List.Count == c0, "ON: hàng đã gia công + nơi gây lỗi Kho → chặn (lời: chưa gia công)", kho.Error);
            else Ok(kho.Ok, "OFF: hàng đã gia công + nơi gây lỗi Kho → lưu được (Workflow OFF không giới hạn)", kho.Error);
            var kh = await Form(svc, r => { r.UserId = Gc; r.Group = "GC"; r.Kind = HandoverForm.Loi; r.Pos = "NC:NC2"; r.Prev = true; r.Cause = "KHACH"; r.Action = HandoverForm.AKhach; r.Qty = 1; r.CatId = 1; });
            var t = Tx(db); var vu = t.NgCaseId!.Value;
            var a = await CheckBu(db, $"G9 {(on ? "ON" : "OFF")} trả khách");
            Ok(kh.Ok && t.Purpose == HandoverPurpose.CustReturn && a.Cases[vu].Cause == "KHACH" && a.Cases[vu].DetectGroup == "GC" && HandoverLedgerService.BuQuaGc(a, a.Cases[vu]),
               "Hàng đã gia công + nơi gây lỗi Khách hàng → Trả khách được; vụ đã qua Gia công (bù theo mã vụ)", kh.Error);
            if (on)
            {
                // báo lỗi đầu vào (lần nhận) trên hàng đã gia công: Kho chặn, Khách hàng được
                var gcAll = new List<string> { "GC" };
                var e1 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC2", 1, HandoverNgSource.DauVao, "KHO", "loi", null, Gc, gcAll, ngCategoryId: 1);
                Ok(!e1.Ok && e1.Error == HandoverForm.GcPrevKhachMsg, "Báo lỗi đầu vào ở NC 2 (đã gia công), nơi gây lỗi Kho → chặn", e1.Error);
                var e2 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC2", 1, HandoverNgSource.DauVao, "KHACH", "loi", null, Gc, gcAll, ngCategoryId: 1);
                Ok(e2.Ok, "Báo lỗi đầu vào ở NC 2 (đã gia công), nơi gây lỗi Khách hàng → được", e2.Error);
                await CheckBu(db, "G9 sau báo lỗi đầu vào");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[G10] [rà độc lập T1 + L3] NG đếm lúc nhận: Kho nhận thành phẩm Đóng gói giao về → ĐÃ QUA; KCS nhận phôi Kho giao thẳng → CHƯA QUA");
        {
            WtsSvcHelp.Reset();
            var db = SeedN19(); var svc = new HandoverService(db);
            var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
            var g = await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", toNC: "NC1");
            var n = await svc.ReceiveAsync(LastTx(db), 10, 0, null, 1);
            var w1 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, 1);
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 10, 0, 1);
            var w3 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC3", null, 10, 0, 1);
            var gp = await svc.IssueByCellAsync(1, "GC", "PKG", 10, 1, "MOI", false, "MOI");
            var np = await svc.ReceiveAsync(LastTx(db), 10, 0, null, 1);
            var wp = await WtsSvcHelp.SaveAsync(db, 1, "PKG", null, "P1", 10, 0, 1);
            var gk = await svc.IssueByCellAsync(1, "PKG", "KHO", 10, 1, "MOI", false, "MOI");
            var nk = await svc.ReceiveAsync(LastTx(db), 9, 1, null, 1, ngCategoryId: 1);
            var vu = db.HandoverReceives.List.Last().NgCaseId ?? 0;
            var rt = await svc.ReceiveAsync(LastTx(db), 0, 1, null, 1);   // Đóng gói nhận lại chiếc NG (phiếu trả tự lập)
            var tk = await Form(svc, r => { r.UserId = 1; r.Group = "PKG"; r.Kind = HandoverForm.Loi; r.Pos = "TONLOI"; r.CaseId = vu; r.Action = HandoverForm.AKhach; r.Qty = 1; });
            var a = await CheckBu(db, "G10 Kho NG lúc nhận thành phẩm");
            Ok(g.Ok && n.Ok && w1.Ok && w2.Ok && w3.Ok && gp.Ok && np.Ok && wp.Ok && gk.Ok && nk.Ok && rt.Ok && tk.Ok && vu > 0
               && a.Cases[vu].DetectGroup == "KHO" && a.Cases[vu].Cause == "PKG" && a.Cases[vu].TraKhach == 1 && HandoverLedgerService.BuQuaGc(a, a.Cases[vu]),
               "Kho đếm NG lúc nhận thành phẩm Đóng gói giao → trả khách: vụ ĐÃ QUA Gia công",
               $"{g.Error}|{n.Error}|{w1.Err}|{w2.Err}|{w3.Err}|{gp.Error}|{np.Error}|{wp.Err}|{gk.Error}|{nk.Error}|{rt.Error}|{tk.Error} vụ {vu}");
            if (vu > 0 && a.Cases.ContainsKey(vu))
            {
                var bu = await svc.ReceiveReplacementAsync(1, vu, 1, null, 1);
                var b = await CheckBu(db, "G10 sau bù");
                Ok(bu.Ok && HandoverLedgerService.BuVuCases(b).Select(c => c.Id).SequenceEqual(new[] { vu }) && HandoverLedgerService.BuVuHold(b) == 1,
                   "Khách gửi bù 1: phải giao Gia công theo mã vụ (giữ chỗ 1)", bu.Error);
                var ok = await KhoGiao(svc, 1, vu);
                var t = Tx(db);
                Ok(ok.Ok && t.ToCell == HandoverCell.BuVu && t.NgCaseId == vu && t.ToNC == "NC1", "Kho giao Gia công 1 bù cho mã vụ → ô Hàng lỗi đã xử lý, NC1", ok.Error);
                await CheckBu(db, "G10 giao bù");
            }
            // KCS nhận phôi Kho giao thẳng (Workflow ON vẫn lưu được — KTT-KCS-PKG), đếm NG lúc nhận → nơi gây lỗi Kho → chưa qua
            WtsSvcHelp.Reset();
            var db2 = SeedN19(); var sv2 = new HandoverService(db2);
            var g2 = await sv2.IssueByCellAsync(1, "KHO", "KCS", 5, 1, "MOI", false, "MOI");
            var n2 = await sv2.ReceiveAsync(LastTx(db2), 4, 1, null, 1, ngCategoryId: 1);
            var v2 = db2.HandoverReceives.List.Last().NgCaseId ?? 0;
            var c2 = await CheckBu(db2, "G10 KCS NG lúc nhận phôi");
            Ok(g2.Ok && n2.Ok && v2 > 0 && c2.Cases[v2].DetectGroup == "KCS" && c2.Cases[v2].Cause == "KHO" && !HandoverLedgerService.BuQuaGc(c2, c2.Cases[v2]),
               "KCS đếm NG lúc nhận phôi Kho giao thẳng: vụ CHƯA QUA Gia công (phôi bù đi như phôi thường)", $"{g2.Error}|{n2.Error} vụ {v2}");
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }

    static readonly List<string> All5 = new() { "KHO", "GC", "HTSP", "KCS", "PKG" };
}
