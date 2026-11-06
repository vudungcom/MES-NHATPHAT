// [TARO-NC-MA][TARO-3T-GOP 2026-11-06] Kịch bản `taro`: Taro dùng CHUNG mã CV ở nhiều NC (khách chốt B 02/11 09:14) + mã gộp 3T thay bộ 3T1–3T3
//   (khai trong WTS tiêu chuẩn, chụp vào PO — HÙNG 02/11 10:34, 05/11 15:37). Gọi HÀM THẬT: Part Master (PartTaroService), tạo KH + snapshot
//   (KhPlanService), Kho → Gia công → HTSP (HandoverService), WTS (WtsSaveService), WTS tiêu chuẩn (StandardWtsTaskService).
//   Đáp án số tính tay theo luật (không đọc lại công thức sổ). Nguồn: KHÁCH 02/11 09:14; HÙNG 06/11 21:45 (SQL 07 đã chạy, câu B2 / B3 làm theo đề xuất).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using QuetSo;

public static class TaroScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    const int U = ChuoiHelp.Admin;

    /// <summary>Khai báo gộp 3T ← 3T1 / 3T2 / 3T3 (như đã khai ở trang WTS tiêu chuẩn).</summary>
    static void SeedGop(AppDbContext db)
    {
        int id = db.StandardWtsTaskGops.List.Count;
        foreach (var p in new[] { "3T1", "3T2", "3T3" })
            db.StandardWtsTaskGops.List.Add(new StandardWtsTaskGop { Id = ++id, GopTaskCode = "3T", PartTaskCode = p, IsActive = true, CreatedAt = DateTime.Now });
        db.Fixup();
    }

    /// <summary>Part 1 NC Gia công + Taro 2 NC (3 và 5) dùng chung mã 2T / 3T1 / 3T2 / 3T3 / 3T (+ 1T chuẩn bị ở NC 3), KCS K1. T.Chuẩn NC 3 ≠ NC 5.</summary>
    static async Task<int> BuildPart(AppDbContext db, string partNo, bool nc5 = true)
    {
        var pid = await ChuoiHelp.BuildPartAsync(db, partNo, new ChuoiHelp.Shape { Gc = 1, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
        var t = new PartTaroService(db);
        await t.AddAsync(pid, new PartTaroStep { NC = "3", WtsTaskCode = "1T", StepName = "Chuẩn bị dao + máy", StandardTime = 2 }, U, "taro");
        foreach (var (c, s3, s5) in new[] { ("2T", 1m, 0.5m), ("3T1", 1m, 0.5m), ("3T2", 0.5m, 0.4m), ("3T3", 1m, 0.6m), ("3T", 2.5m, 1.5m) })
        {
            await t.AddAsync(pid, new PartTaroStep { NC = "3", WtsTaskCode = c, StepName = "Taro " + c, StandardTime = s3 }, U, "taro");
            if (nc5) await t.AddAsync(pid, new PartTaroStep { NC = "5", WtsTaskCode = c, StepName = "Taro " + c, StandardTime = s5 }, U, "taro");
        }
        return pid;
    }

    /// <summary>Kho nhận q phôi → Gia công NC01 → WTS NC01 OK q → giao HTSP q, HTSP nhận q.</summary>
    static async Task Feed(AppDbContext db, int det, decimal q)
    {
        var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db);
        var r0 = await kho.SaveKhoInputAsync(det, DateTime.Now, (int)q, "OVL", "A1", null, null, null, "nhan phoi", U);
        var i1 = await svc.IssueByCellAsync(det, "KHO", "GC", q, U, "MOI", false, "MOI", null, "NC01");
        var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, q, 0, null, U);
        var w = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, q, 0, U);
        var i2 = await svc.IssueByCellAsync(det, "GC", "HTSP", q, U, "MOI", false, "MOI");
        var r2 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, q, 0, null, U);
        if (!(r0.Success && i1.Ok && r1.Ok && w.Ok && i2.Ok && r2.Ok)) throw new Exception($"Feed hỏng: {r0.Error} {i1.Error} {r1.Error} {w.Err} {i2.Error} {r2.Error}");
    }

    static Task<(bool Ok, string? Err, long? LogId)> Taro(AppDbContext db, int det, string code, string? nc, decimal ok, decimal ng = 0, long? edit = null)
        => WtsSvcHelp.SaveAsync(db, det, "TARO", null, code, ok, ng, U, ng > 0 ? 1 : null, null, edit, stepNc: nc);

    static LedgerStepLine? L(LedgerRow r, string key) => r.G("HTSP").Steps.FirstOrDefault(s => s.Pg == "TARO" && s.Code == key);

    public static async Task<int> Run()
    {
        Console.WriteLine("[T1] Part Master: Taro cùng mã CV ở NC khác được; cùng mã CÙNG NC bị chặn (máy chủ, dưới khoá)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA1");
            var rows = db.PartTaroSteps.List.Where(s => s.PartId == pid && s.IsActive).ToList();
            Ok(rows.Count == 11 && rows.Count(s => s.WtsTaskCode == "3T1") == 2, "Thêm 3T1 ở NC 3 và NC 5 (+ 2T, 3T2, 3T3, 3T) — được (trước: trang chặn trùng mã CV Taro)", string.Join(",", rows.Select(s => s.NC + ":" + s.WtsTaskCode)));
            string? err = null; try { await new PartTaroService(db).AddAsync(pid, new PartTaroStep { NC = " 5", WtsTaskCode = "3t1 ", StepName = "x" }, U, "taro"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("3t1") && err.Contains("NC 5") && db.PartTaroSteps.List.Count(s => s.PartId == pid && s.IsActive) == 11, "Thêm 3T1 lần 2 ở NC 5 (khác hoa thường / khoảng trắng) → chặn", err);
            var r2t5 = rows.Single(s => s.NC == "5" && s.WtsTaskCode == "2T");
            err = null; try { await new PartTaroService(db).UpdateAsync(r2t5.StepId, new PartTaroStep { NC = "5", WtsTaskCode = "3T", StepName = "x", StandardTime = 1 }, U, "taro"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("3T") && db.PartTaroSteps.List.Single(s => s.StepId == r2t5.StepId).WtsTaskCode == "2T", "Sửa 2T ở NC 5 thành 3T (đã có 3T ở NC 5) → chặn, dòng giữ nguyên", err);
            err = null; try { await new PartTaroService(db).UpdateAsync(r2t5.StepId, new PartTaroStep { NC = "5", WtsTaskCode = "2T", StepName = "Taro 2T mới", StandardTime = 0.7m }, U, "taro"); } catch (Exception ex) { err = ex.Message; }
            Ok(err == null && db.PartTaroSteps.List.Single(s => s.StepId == r2t5.StepId).StandardTime == 0.7m, "Sửa ô khác (tên, T.Chuẩn) của dòng mã dùng chung → được", err);
            Ok(Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains("MES_PM_STEPS_" + pid), "Thêm / sửa Taro chạy dưới khoá MES_PM_STEPS_<Part> (như các nhóm khác)");
        }

        Console.WriteLine("[T2] Tạo KH: snapshot chụp khai báo gộp (3T ← 3T1, 3T2, 3T3); sổ đếm Taro theo cặp (mã, NC)");
        int det; AppDbContext dbm;
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false); SeedGop(db);
            var pid = await BuildPart(db, "P-TA2");
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-TA2", new decimal[] { 10 }, "PO-TA");
            det = ids[0]; dbm = db;
            var gops = db.KhPlanRouteSnapshotWtsGops.List.Where(g => g.KhPlanDetailId == det).Select(g => g.GopCode + ">" + g.PartCode).OrderBy(x => x).ToList();
            Ok(gops.SequenceEqual(new[] { "3T>3T1", "3T>3T2", "3T>3T3" }), "Snapshot PO có 3 dòng khai báo gộp", string.Join(",", gops));
            await Feed(db, det, 10);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(det))!;
            var keys = row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => s.Code).ToList();
            Ok(keys.Contains("2T (NC 3)") && keys.Contains("2T (NC 5)") && keys.Contains("3T1 (NC 5)") && keys.Contains("3T (NC 3)") && !keys.Contains("2T"),
               "Sổ HTSP: mỗi mã dùng chung thành 2 dòng (mã, NC 3) / (mã, NC 5)", string.Join(", ", keys));
            Ok(row.G("HTSP").Steps.Any(s => s.IsPrep && s.Code == "1T") && !row.TaroNcs.ContainsKey("1T"), "Mã chuẩn bị 1T giữ nguyên (không theo NC, không tính số)");
            var g3 = L(row, "3T (NC 3)"); var p31 = L(row, "3T1 (NC 3)");
            Ok(g3 != null && g3.IsGop && g3.GopUnit == "3T (NC 3)" && p31 != null && !p31.IsGop && p31.GopUnit == "3T (NC 3)" && L(row, "3T (NC 5)")!.GopUnit == "3T (NC 5)",
               "Bộ gộp theo từng NC: 3T (NC 3) là mã gộp của 3T1/3T2/3T3 (NC 3); NC 5 bộ riêng");
            Ok(row.G("HTSP").ChuaLam == 10 && L(row, "2T (NC 3)")!.Waiting == 10 && L(row, "3T (NC 5)")!.Waiting == 10 && L(row, "3T2 (NC 5)")!.Waiting == 10,
               "HTSP nhận 10: Chưa làm 10, mỗi dòng (mã, NC) chờ 10");
        }

        Console.WriteLine("[T3] WTS mã dùng chung: bắt buộc chọn đúng NC (máy chủ đọc sổ dưới khoá PO), lưu StepNC");
        {
            var db = dbm;
            int n0 = db.WtsProductionLogs.List.Count;
            var a = await Taro(db, det, "2T", null, 1);
            Ok(!a.Ok && a.Err!.Contains("NC 3") && a.Err.Contains("NC 5") && a.Err.Contains("chọn NC") && db.WtsProductionLogs.List.Count == n0, "Không chọn NC → chặn, báo mã dùng ở NC 3, NC 5", a.Err);
            a = await Taro(db, det, "2T", "7", 1);
            Ok(!a.Ok && a.Err!.Contains("NC 7") && db.WtsProductionLogs.List.Count == n0, "NC 7 (không có mã 2T) → chặn", a.Err);
            a = await Taro(db, det, "2T", " 3 ", 10);
            var w = db.WtsProductionLogs.List.LastOrDefault();
            Ok(a.Ok && w != null && w.WtsCode == "2T" && w.StepNC == "3" && w.ProcessGroup == "TARO", "NC ' 3 ' → lưu, StepNC chuẩn hoá '3'", a.Err);
            var b = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "1T", 0, 0, U, stepNc: "5");
            var w1 = db.WtsProductionLogs.List.Last();
            Ok(b.Ok && w1.WtsCode == "1T" && w1.StepNC == null, "Mã 1 NC (1T chuẩn bị) gửi NC lạ → máy chủ bỏ NC (StepNC NULL như dòng cũ)", b.Err);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(det))!;
            Ok(L(row, "2T (NC 3)")!.Ok == 10 && L(row, "2T (NC 5)")!.Ok == 0 && L(row, "2T (NC 5)")!.Waiting == 10,
               "Sổ: 2T NC 3 OK 10, 2T NC 5 vẫn chờ 10 (trước: dòng WTS 2T cộng vào cả 2 dòng)");
        }

        Console.WriteLine("[T4] 3T gộp: mỗi chiếc làm 3T HOẶC đủ 3T1 + 3T2 + 3T3 — OK bộ = OK 3T + min(3T1..3T3), trần từng mã");
        {
            var db = dbm; var led = new HandoverLedgerService(db);
            await Taro(db, det, "2T", "5", 10);
            var s1 = await Taro(db, det, "3T", "3", 4);
            var s2 = await Taro(db, det, "3T1", "3", 6);
            var s3 = await Taro(db, det, "3T2", "3", 6);
            var s4 = await Taro(db, det, "3T3", "3", 5);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(s1.Ok && s2.Ok && s3.Ok && s4.Ok, "Ghi 3T NC3 = 4, 3T1 = 6, 3T2 = 6, 3T3 = 5", $"{s1.Err} {s2.Err} {s3.Err} {s4.Err}");
            // tay: vào 10 · 3T còn = 10 − 4 − max(6,6,5) = 0 · 3T1 = 10 − 4 − 6 = 0 · 3T3 = 10 − 4 − 5 = 1 · bộ NC3 OK = 4 + 5 = 9 · bộ NC5 OK 0 → HTSP OK 0
            Ok(L(row, "3T (NC 3)")!.Waiting == 0 && L(row, "3T1 (NC 3)")!.Waiting == 0 && L(row, "3T2 (NC 3)")!.Waiting == 0 && L(row, "3T3 (NC 3)")!.Waiting == 1,
               "Chờ: 3T 0 (10 − 4 − max 6) · 3T1 0 · 3T2 0 · 3T3 1", string.Join(" · ", row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => $"{s.Code}={s.Waiting}")));
            Ok(row.G("HTSP").ChuaLam == 10, "HTSP Đã làm = OK nhỏ nhất = bộ NC 5 (0) → Chưa làm 10", $"ChuaLam {row.G("HTSP").ChuaLam}");
            var x = await Taro(db, det, "3T", "3", 1);
            Ok(!x.Ok && x.Err!.Contains("3T (NC 3)") && x.Err.Contains("vượt"), "Ghi thêm 3T NC 3 (chờ 0) → chặn: chiếc còn lại đã bắt đầu 3T1/3T2", x.Err);
            x = await Taro(db, det, "3T1", "3", 1);
            Ok(!x.Ok, "Ghi thêm 3T1 NC 3 (chờ 0) → chặn", x.Err);
            x = await Taro(db, det, "3T", "5", 10);
            row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(x.Ok && row.G("HTSP").ChuaLam == 1 && row.G("HTSP").ConGiao == 9, "3T NC 5 = 10 → bộ NC 5 OK 10; HTSP OK = min(2T 10, 2T 10, bộ NC3 9, bộ NC5 10) = 9 → Chưa làm 1, Đã làm chưa giao 9",
               $"{x.Err} · ChuaLam {row.G("HTSP").ChuaLam} · ConGiao {row.G("HTSP").ConGiao}");
            Ok(L(row, "3T1 (NC 5)")!.Waiting == 0 && L(row, "3T (NC 5)")!.Waiting == 0, "NC 5 làm hết bằng 3T → 3T1 / 3T2 / 3T3 NC 5 chờ 0 (không làm thêm được)");
            x = await Taro(db, det, "3T3", "3", 1);
            row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(x.Ok && row.G("HTSP").ChuaLam == 0 && row.G("HTSP").ConGiao == 10 && row.Balanced && !HandoverLedgerService.NegativeCells(row).Any(),
               "3T3 NC 3 thêm 1 → bộ NC3 = 4 + 6 = 10 → HTSP làm xong 10, cân, không ô âm", x.Err);
            var okTerm = row.G("HTSP").Steps.Count(s => s.IsLast);
            Ok(okTerm >= 1, "Đánh dấu mã đang quyết định Đã làm", okTerm.ToString());
        }

        Console.WriteLine("[T5] K5: giao 10 sang KCS rồi xoá dòng 3T NC 5 → chặn (bộ NC 5 về 0 → HTSP Đã làm chưa giao âm)");
        {
            var db = dbm; var svc = new HandoverService(db);
            var i = await svc.IssueByCellAsync(det, "HTSP", "KCS", 10, U, "MOI", false, "MOI");
            var w5 = db.WtsProductionLogs.List.Last(w => w.WtsCode == "3T" && w.StepNC == "5" && !w.IsVoided);
            var v = await WtsSvcHelp.VoidAsync(db, w5.WtsLogId, det, U);
            Ok(i.Ok && !v.Ok && !db.WtsProductionLogs.List.Single(w => w.WtsLogId == w5.WtsLogId).IsVoided, "Xoá dòng 3T NC 5 → chặn K5, dòng còn nguyên", $"{i.Error} · {v.Err}");
            var e = await Taro(db, det, "3T", "3", 10, edit: w5.WtsLogId);
            Ok(!e.Ok && !db.WtsProductionLogs.List.Single(w => w.WtsLogId == w5.WtsLogId).IsVoided, "Sửa dòng 3T NC 5 sang NC 3 → chặn (NC 3 hết chờ + K5), dòng cũ còn nguyên", e.Err);
        }

        Console.WriteLine("[T6] NG ở mã dùng chung chỉ tính 1 lần (trước: NG trừ ở cả 2 dòng cùng mã)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false); SeedGop(db);
            var pid = await BuildPart(db, "P-TA6");
            var d6 = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA6", new decimal[] { 10 }, "PO-T6"))[0];
            await Feed(db, d6, 10);
            var s = await Taro(db, d6, "2T", "3", 3, ng: 1);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(d6))!;
            // tay: vào 10 · 2T NC3: OK 3 NG 1 → chờ 6 · 2T NC5: chiếc NG đã rời (1) mà 2T NC5 chưa làm → chờ 10 − 1 = 9 · NG cuối HTSP 1
            Ok(s.Ok && row.G("HTSP").NgCuoi == 1 && L(row, "2T (NC 3)")!.Waiting == 6 && L(row, "2T (NC 5)")!.Waiting == 9 && L(row, "2T (NC 5)")!.Ng == 0,
               "NG 1 ở 2T NC 3: NG cuối HTSP 1; 2T NC 3 chờ 6; 2T NC 5 chờ 9 (NG 0)", $"{s.Err} · NgCuoi {row.G("HTSP").NgCuoi} · {L(row, "2T (NC 3)")!.Waiting} / {L(row, "2T (NC 5)")!.Waiting} NG5 {L(row, "2T (NC 5)")!.Ng}");
            var s2 = await Taro(db, d6, "3T1", "5", 2, ng: 1);
            row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(d6))!;
            // bộ NC5: 3T1 OK 2 NG 1 · NG khác bộ = 1 (2T NC3) → 3T NC5 chờ = 10 − 0 − max(3) − max(0, 1 − (0 + 2)) = 7 · 3T1 NC5 = 10 − 0 − 3 − 0 = 7
            //   3T2 NC5 = min(10 − 0 − 0 − max(0, 1 − 0) = 9, Chưa làm HTSP = 10 − OK nhỏ nhất 0 − NG 2 = 8) = 8 (chỉ còn 8 chiếc tốt) · 2T NC5 = 10 − 0 − 2 = 8
            Ok(s2.Ok && L(row, "3T (NC 5)")!.Waiting == 7 && L(row, "3T2 (NC 5)")!.Waiting == 8 && L(row, "3T1 (NC 5)")!.Waiting == 7 && L(row, "2T (NC 5)")!.Waiting == 8
               && row.G("HTSP").NgCuoi == 2 && row.G("HTSP").ChuaLam == 8 && row.Balanced,
               "NG 1 ở 3T1 NC 5 (thuộc bộ gộp): 3T NC5 chờ 7, 3T1 NC5 chờ 7, 3T2 NC5 chờ 8 (≤ Chưa làm 8), 2T NC5 chờ 8, NG cuối 2, cân",
               $"{s2.Err} · " + string.Join(" · ", row.G("HTSP").Steps.Where(x => !x.IsPrep).Select(x => $"{x.Code}={x.Waiting}")));
        }

        Console.WriteLine("[T7] T.Chuẩn: máy chủ lấy đúng dòng (mã, NC) — trang gửi dòng NC 3 khi làm NC 5 vẫn ghi chuẩn NC 5");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA7");
            var d7 = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA7", new decimal[] { 10 }, "PO-T7"))[0];
            await Feed(db, d7, 10);
            var nc3 = db.KhPlanRouteSnapshotTaro.List.Single(s => s.KhPlanDetailId == d7 && s.NC == "3" && s.WtsTaskCode == "3T1");
            var nc5 = db.KhPlanRouteSnapshotTaro.List.Single(s => s.KhPlanDetailId == d7 && s.NC == "5" && s.WtsTaskCode == "3T1");
            var grp = WtsSvcHelp.Build(d7, "TARO", null, "3T1", 2, 0, U, null, null, null, stepNc: "5");
            grp.Rows[0].StdTimePerPc = nc3.StandardTime; grp.Rows[0].StdSrcTable = StdTrack.Table.Taro; grp.Rows[0].StdSrcRowId = nc3.SnapshotId;
            var res = await new WtsSaveService(db, new HandoverLedgerService(db)).SaveGroupAsync(grp); db.Fixup();
            var std = db.WtsProductionLogStds.List.LastOrDefault(x => x.WtsLogId == grp.Rows[0].WtsLogId);
            Ok(res.Ok && std != null && std.StdValue == nc5.StandardTime && std.SnapshotRowId == nc5.SnapshotId,
               $"Chuẩn ghi = 3T1 NC 5 ({nc5.StandardTime}), dòng snapshot NC 5 (trước: luôn dòng đầu NC 3 = {nc3.StandardTime})", $"{res.ErrorMsg} · {std?.StdValue} #{std?.SnapshotRowId}");
        }

        Console.WriteLine("[T8] PO tạo TRƯỚC khi khai gộp: 3T là mã thường (giữ như cũ — câu B2 Hùng 06/11 21:45); PO chưa chạy làm mới quy trình thì lấy khai báo mới");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA8");
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-TA8", new decimal[] { 10, 10 }, "PO-T8");
            SeedGop(db);   // khai gộp SAU khi tạo PO
            Ok(!db.KhPlanRouteSnapshotWtsGops.List.Any(), "PO tạo trước: không có khai báo gộp trong snapshot");
            await Feed(db, ids[0], 10);
            await Taro(db, ids[0], "3T", "3", 10);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(ids[0]))!;
            Ok(L(row, "3T (NC 3)")!.GopUnit == null && L(row, "3T1 (NC 3)")!.Waiting == 10, "PO đã chạy: 3T tính như mã thường (3T1 NC 3 vẫn chờ 10)");
            var kh = new KhPlanService(db);
            await kh.RefreshRouteSnapshotAsync(ids[1], pid, U, "lay khai bao gop");
            var g2 = db.KhPlanRouteSnapshotWtsGops.List.Count(g => g.KhPlanDetailId == ids[1]);
            Ok(g2 == 3 && !db.KhPlanRouteSnapshotWtsGops.List.Any(g => g.KhPlanDetailId == ids[0]), "PO chưa chạy: làm mới quy trình → có 3 dòng gộp; PO đã chạy không đổi", g2.ToString());
            var rDel = await kh.DeleteDetailAsync(ids[1], U, "xoa thu PO trang");
            Ok(rDel.Success && !db.KhPlanRouteSnapshotWtsGops.List.Any(g => g.KhPlanDetailId == ids[1]), "Xoá PO trắng: xoá luôn khai báo gộp của PO (khoá ngoại NO ACTION)", rDel.Error);
        }

        Console.WriteLine("[T9] Dòng WTS cũ mã dùng chung KHÔNG có NC (dữ liệu trước gói): tính vào NC đầu + cảnh báo (câu B3 Hùng 06/11 21:45)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA9");
            var d9 = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA9", new decimal[] { 10 }, "PO-T9"))[0];
            await Feed(db, d9, 10);
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 900, KhPlanDetailId = d9, ProcessGroup = "TARO", WtsCode = "2T", HtspSubGroup = "Taro", QtyDone = 4, QtyOk = 4, QtyNg = 0, WorkerId = U, StartTime = DateTime.Now.AddMinutes(-2), EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            db.Fixup();
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(d9))!;
            Ok(L(row, "2T (NC 3)")!.Ok == 4 && L(row, "2T (NC 5)")!.Ok == 0 && row.Warnings.Any(w => w.Contains("#900") && w.Contains("TARO-NC-MA")),
               "Dòng #900 2T không NC → tính vào NC 3 (NC đầu), NC 5 không cộng; sổ có cảnh báo đỏ", string.Join(" | ", row.Warnings));
            var q = await QuetCore.ScanAsync(db, new HandoverLedgerService(db));
            Ok(q.Findings.Any(f => f.Kind == QuetCore.CanhBao && f.Detail.Contains("#900")), "QuetSo báo dòng này (CANH_BAO_SO)");
            Ok(!q.Findings.Any(f => f.Kind == QuetCore.MaTrung && f.Po == "PO-T9"), "QuetSo: mã dùng chung ở NC khác không còn báo MA_TRUNG");
        }

        Console.WriteLine("[T10] WTS tiêu chuẩn: khai mã gộp (máy chủ kiểm luật, PIN, lịch sử, bỏ khai = IsActive 0)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            db.Users.List.Single(u => u.UserId == U).PIN = "1111";
            var wts = new StandardWtsTaskService(db, new AuthService(db));
            foreach (var c in new[] { "3T", "3T1", "3T2", "3T3", "1T", "4T" })
                await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = c, TaskName = "Taro " + c }, U, "1111");
            await wts.CreateAsync(new StandardWtsTask { CategoryCode = "BAVIA", CategoryName = "Bavia", TaskCode = "2V", TaskName = "Bavia 2V" }, U, "1111");
            StandardWtsTask T(string c) => db.StandardWtsTasks.List.Single(x => x.TaskCode == c);
            StandardWtsTask Cp(StandardWtsTask t) => new() { TaskId = t.TaskId, CategoryCode = t.CategoryCode, CategoryName = t.CategoryName, TaskCode = t.TaskCode, TaskName = t.TaskName, DefaultUnit = t.DefaultUnit, StandardTimeSec = t.StandardTimeSec, DisplayOrder = t.DisplayOrder, GhiChu = t.GhiChu, IsProductiveTask = t.IsProductiveTask };
            var r = await wts.UpdateAsync(Cp(T("3T")), "khai gop", U, "1111", new List<string> { "3T1", "3T2", "3t3 " });
            var act = db.StandardWtsTaskGops.List.Where(g => g.IsActive).Select(g => g.GopTaskCode + ">" + g.PartTaskCode).OrderBy(x => x).ToList();
            Ok(r.Success && act.SequenceEqual(new[] { "3T>3T1", "3T>3T2", "3T>3T3" }) && db.StandardWtsTaskChangeLogs.List.Any(l => l.FieldName == "Mã gộp (làm thay)" && l.NewValue == "3T1 + 3T2 + 3T3"),
               "Khai 3T ← 3T1, 3T2, 3t3 → 3 dòng (mã chuẩn hoá theo danh mục) + lịch sử", $"{r.Error} · {string.Join(",", act)}");
            r = await wts.UpdateAsync(Cp(T("3T")), "x", U, "0000", new List<string> { "3T1" });
            Ok(!r.Success && db.StandardWtsTaskGops.List.Count(g => g.IsActive) == 3, "Sai PIN → không đổi", r.Error);
            r = await wts.UpdateAsync(Cp(T("4T")), "x", U, "1111", new List<string> { "3T" });
            Ok(!r.Success && r.Error!.Contains("lồng"), "4T gộp mã 3T (đang là mã gộp) → chặn lồng", r.Error);
            r = await wts.UpdateAsync(Cp(T("3T1")), "x", U, "1111", new List<string> { "4T" });
            Ok(!r.Success && r.Error!.Contains("lồng"), "3T1 (đang là thành phần) khai làm mã gộp → chặn lồng", r.Error);
            r = await wts.UpdateAsync(Cp(T("4T")), "x", U, "1111", new List<string> { "3T2" });
            Ok(!r.Success && r.Error!.Contains("3T"), "4T gộp 3T2 (đã thuộc 3T) → chặn (1 thành phần 1 mã gộp)", r.Error);
            r = await wts.UpdateAsync(Cp(T("4T")), "x", U, "1111", new List<string> { "2V" });
            Ok(!r.Success && r.Error!.Contains("khác nhóm"), "4T gộp 2V (nhóm Bavia) → chặn", r.Error);
            r = await wts.UpdateAsync(Cp(T("4T")), "x", U, "1111", new List<string> { "1T" });
            Ok(!r.Success && r.Error!.Contains("chuẩn bị"), "4T gộp 1T (mã chuẩn bị) → chặn", r.Error);
            var t3 = Cp(T("3T")); t3.TaskCode = "3TX";
            r = await wts.UpdateAsync(t3, "x", U, "1111", null);
            Ok(!r.Success && r.Error!.Contains("khai báo mã gộp") && T("3T") != null, "Đổi mã 3T (đang khai gộp) → chặn", r.Error);
            r = await wts.UpdateAsync(Cp(T("3T")), "bo 3T3", U, "1111", new List<string> { "3T1", "3T2" });
            Ok(r.Success && db.StandardWtsTaskGops.List.Count(g => g.IsActive) == 2 && db.StandardWtsTaskGops.List.Any(g => !g.IsActive && g.PartTaskCode == "3T3" && g.RemovedBy == U && g.RemovedAt != null),
               "Bỏ 3T3 khỏi bộ gộp → dòng 3T3 IsActive 0 (giữ ai / lúc nào), không xoá", r.Error);
            r = await wts.SoftDeleteAsync(T("3T1").TaskId, U, "1111");
            Ok(r.Success && !db.StandardWtsTaskGops.List.Any(g => g.IsActive && g.PartTaskCode == "3T1"), "Xoá mã 3T1 khỏi danh mục → bỏ luôn khai báo gộp có 3T1", r.Error);
            var n = db.StandardWtsTaskGops.List.Count;
            r = await wts.UpdateAsync(Cp(T("3T")), "doi ten", U, "1111", null);
            Ok(db.StandardWtsTaskGops.List.Count == n, "Sửa ô khác (không gửi danh sách gộp) → khai báo gộp không đổi");
        }

        // ── Rà độc lập gói 4 (06/11) mục 1–4: ca chứng minh lỗi + chặn tái phát ──
        Console.WriteLine("[T11] Gợi ý gỡ K5 (HandoverWhy) tìm ra dòng WTS của mã Taro dùng chung (ô âm mang khoá \"2T (NC 3)\")");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA11");
            var d = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA11", new decimal[] { 10 }, "PO-T11"))[0];
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db);
            await kho.SaveKhoInputAsync(d, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
            await svc.IssueByCellAsync(d, "KHO", "GC", 10, U, "MOI", false, "MOI", null, "NC01");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, d, "GC", "NC01", null, 10, 0, U);
            long rLast = 0;
            for (int k = 0; k < 2; k++)
            {
                await svc.IssueByCellAsync(d, "GC", "HTSP", 5, U, "MOI", false, "MOI");
                await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
                rLast = db.HandoverTransactions.List.Last().Receives.Last().ReceiveId;
            }
            var w = await Taro(db, d, "2T", "3", 10);
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(d))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpVoidReceive(rLast), new HashSet<string> { "R:" + rLast });
            Ok(w.Ok && plan.Complete && plan.Steps.Any(st => st.Kind == "WTS" && st.Id == w.LogId),
               "Huỷ lần nhận thứ 2 (ô 2T (NC 3) sẽ âm 5) → gợi ý ra đúng dòng WTS 2T NC 3 (trước: \"chưa lần ra\", 0 bước)",
               $"{w.Err} · complete={plan.Complete} · {string.Join(" | ", plan.Steps.Select(st => st.Text))}");
        }

        Console.WriteLine("[T12] Gợi ý gỡ K5 tính trên sổ CÓ bộ gộp (Clone chép khai báo gộp)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false); SeedGop(db);
            var pid = await BuildPart(db, "P-TA12");
            var d = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA12", new decimal[] { 10 }, "PO-T12"))[0];
            await Feed(db, d, 10);
            var g3 = await Taro(db, d, "3T", "3", 10);
            await Taro(db, d, "3T", "5", 10); await Taro(db, d, "2T", "3", 10); await Taro(db, d, "2T", "5", 10);
            var svc = new HandoverService(db);
            var iss = await svc.IssueByCellAsync(d, "HTSP", "KCS", 10, U, "MOI", false, "MOI");
            var txId = db.HandoverTransactions.List.Last().HandoverTxId;
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(d))!;
            var plan = HandoverWhy.Plan(inp, HandoverWhy.OpWts(new[] { g3.LogId!.Value }), new HashSet<string> { "W:" + g3.LogId });
            Ok(iss.Ok && plan.Steps.Any(st => st.Id == txId), "Xoá dòng 3T NC 3 (bộ gộp NC 3 về 0, đã giao KCS 10) → gợi ý ra phiếu giao KCS (trước: sổ không gộp → gợi ý rỗng)",
               $"{iss.Error} · complete={plan.Complete} · {string.Join(" | ", plan.Steps.Select(st => st.Kind + "#" + st.Id))}");
        }

        Console.WriteLine("[T13] WTS tiêu chuẩn: đổi mã + khai gộp cùng 1 lần Lưu → khai báo theo MÃ MỚI; khai gộp chạy dưới khoá MES_WTS_GOP");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            db.Users.List.Single(u => u.UserId == U).PIN = "1111";
            var wts = new StandardWtsTaskService(db, new AuthService(db));
            foreach (var c in new[] { "5TX", "5T1", "5T2" })
                await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = c, TaskName = "Taro " + c }, U, "1111");
            var t = db.StandardWtsTasks.List.Single(x => x.TaskCode == "5TX");
            var m = new StandardWtsTask { TaskId = t.TaskId, CategoryCode = t.CategoryCode, CategoryName = t.CategoryName, TaskCode = "5T", TaskName = t.TaskName, DefaultUnit = t.DefaultUnit, DisplayOrder = t.DisplayOrder, IsProductiveTask = t.IsProductiveTask };
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var r = await wts.UpdateAsync(m, "doi ma + khai gop", U, "1111", new List<string> { "5T1", "5T2" });
            var act = db.StandardWtsTaskGops.List.Where(g => g.IsActive).Select(g => g.GopTaskCode + ">" + g.PartTaskCode).OrderBy(x => x).ToList();
            Ok(r.Success && t.TaskCode == "5T" && act.SequenceEqual(new[] { "5T>5T1", "5T>5T2" }), "5TX → 5T + tích 5T1, 5T2 → khai báo 5T ← 5T1 + 5T2 (trước: ghi 5TX — mồ côi)", $"{r.Error} · {string.Join(",", act)}");
            Ok(Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains(StandardWtsTaskService.GopLock), "Khai gộp lấy khoá MES_WTS_GOP (2 người cùng khai 1 mã thành phần: người sau đọc lại trong khoá)");
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var rd = await wts.SoftDeleteAsync(db.StandardWtsTasks.List.Single(x => x.TaskCode == "5T2").TaskId, U, "1111");
            Ok(rd.Success && Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains(StandardWtsTaskService.GopLock) && !db.StandardWtsTaskGops.List.Any(g => g.IsActive && g.PartTaskCode == "5T2"),
               "Xoá mã 5T2 cũng lấy khoá MES_WTS_GOP rồi mới tắt khai báo gộp có 5T2 (rà độc lập vòng 2 mục 2)", rd.Error);
        }

        Console.WriteLine("[T14] Mã CHUẨN BỊ dùng ở nhiều NC (1T ở NC 3 + NC 5): WTS bắt chọn NC, T.Chuẩn đúng dòng; sổ không đổi (không tính số)");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA14");
            await new PartTaroService(db).AddAsync(pid, new PartTaroStep { NC = "5", WtsTaskCode = "1T", StepName = "Chuẩn bị NC 5", StandardTime = 3 }, U, "taro");
            var d = (await ChuoiHelp.CreateKhAsync(db, pid, "P-TA14", new decimal[] { 10 }, "PO-T14"))[0];
            await Feed(db, d, 10);
            var before = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(d))!;
            int n0 = db.WtsProductionLogs.List.Count;
            var a = await WtsSvcHelp.SaveAsync(db, d, "TARO", null, "1T", 0, 0, U);
            Ok(!a.Ok && a.Err!.Contains("NC 3") && a.Err.Contains("NC 5") && db.WtsProductionLogs.List.Count == n0, "1T không chọn NC → chặn (trước: lưu, chuẩn lấy dòng NC 3)", a.Err);
            var b = await WtsSvcHelp.SaveAsync(db, d, "TARO", null, "1T", 0, 0, U, stepNc: "5");
            var w = db.WtsProductionLogs.List.Last();
            var nc5 = db.KhPlanRouteSnapshotTaro.List.Single(s => s.KhPlanDetailId == d && s.NC == "5" && s.WtsTaskCode == "1T");
            var std = db.WtsProductionLogStds.List.LastOrDefault(x => x.WtsLogId == w.WtsLogId);
            Ok(b.Ok && w.WtsCode == "1T" && w.StepNC == "5" && std != null && std.StdValue == 3 && std.SnapshotRowId == nc5.SnapshotId,
               "1T NC 5 → lưu StepNC 5, T.Chuẩn = dòng 1T NC 5 (3)", $"{b.Err} · {w.StepNC} · {std?.StdValue} #{std?.SnapshotRowId}");
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(d))!;
            Ok(row.TaroPrepNcs.ContainsKey("1T") && !row.TaroNcs.ContainsKey("1T") && row.G("HTSP").ChuaLam == before.G("HTSP").ChuaLam && row.Balanced
               && row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => s.Code + s.Waiting).SequenceEqual(before.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => s.Code + s.Waiting)),
               "Sổ: 1T chỉ ghi danh sách NC để chọn (TaroPrepNcs), không đổi khoá / số mã khác, Chưa làm giữ nguyên, cân");
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
