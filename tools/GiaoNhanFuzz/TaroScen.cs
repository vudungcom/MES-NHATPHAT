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

        // [TARO-DUPHONG 2026-11-07] Taro không dùng dòng dự phòng (HÙNG 07/11 09:11): máy chủ chặn thêm / đổi thành dự phòng / đổi cha của dự phòng cũ;
        //   dự phòng CŨ (có sẵn trong DB) vẫn sửa tên / giờ chuẩn được, đổi thành dòng chính được, xoá kèm dòng cha như cũ. Bản trước gói 5 phải SAI T15a.
        Console.WriteLine("[T15] [TARO-DUPHONG] Taro: chặn tạo dòng dự phòng (máy chủ); dự phòng cũ vẫn sửa ô khác / thành dòng chính");
        {
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await BuildPart(db, "P-TA15");
            var t = new PartTaroService(db);
            int n0 = db.PartTaroSteps.List.Count(x => x.PartId == pid);
            string? e1 = null; try { await t.AddAsync(pid, new PartTaroStep { NC = "3-DP", WtsTaskCode = "2T-DP", IsBackup = true, ParentNC = "3", StepName = "dp", StandardTime = 1 }, U, "taro dp"); } catch (Exception ex) { e1 = ex.Message; }
            Ok(e1 == PartStepGuard.TaroNoBackupMsg && db.PartTaroSteps.List.Count(x => x.PartId == pid) == n0, "T15a thêm dòng dự phòng Taro → chặn, không thêm dòng", e1);
            var m2 = db.PartTaroSteps.List.Single(x => x.PartId == pid && x.NC == "5" && x.WtsTaskCode == "2T");
            string? e2 = null; try { await t.UpdateAsync(m2.StepId, new PartTaroStep { NC = "5", WtsTaskCode = "2T", IsBackup = true, ParentNC = "3", StepName = m2.StepName, StandardTime = m2.StandardTime }, U, "thanh dp"); } catch (Exception ex) { e2 = ex.Message; }
            Ok(e2 == PartStepGuard.TaroNoBackupMsg && !db.PartTaroSteps.List.Single(x => x.StepId == m2.StepId).IsBackup, "T15b đổi dòng chính 2T NC 5 thành dự phòng → chặn, dòng giữ nguyên", e2);
            // dữ liệu cũ: dòng dự phòng có sẵn (như Part "sssssssss" trên DB MES 07/11)
            var old = new PartTaroStep { StepId = 9915, PartId = pid, StepOrder = 99, NC = "3-DP-01", IsBackup = true, ParentNC = "3", StepName = "cu", StandardTime = 1, IsActive = true, CreatedBy = U, CreatedAt = DateTime.Now };
            db.PartTaroSteps.List.Add(old); db.Fixup();
            string? e3 = null; try { await t.UpdateAsync(9915, new PartTaroStep { NC = "3-DP-01", IsBackup = true, ParentNC = "3", StepName = "cu sua", StandardTime = 2 }, U, "sua gio"); } catch (Exception ex) { e3 = ex.Message; }
            var o3 = db.PartTaroSteps.List.Single(x => x.StepId == 9915);
            Ok(e3 == null && o3.StepName == "cu sua" && o3.StandardTime == 2 && o3.IsBackup, "T15c dự phòng CŨ: sửa tên / giờ chuẩn → được", e3);
            string? e4 = null; try { await t.UpdateAsync(9915, new PartTaroStep { NC = "3-DP-01", IsBackup = true, ParentNC = "5", StepName = "cu sua 2", StandardTime = 2 }, U, "doi cha"); } catch (Exception ex) { e4 = ex.Message; }
            var o4 = db.PartTaroSteps.List.Single(x => x.StepId == 9915);
            Ok(e4 == null && o4.ParentNC == "3" && o4.StepName == "cu sua 2" && !db.PartProcessStepChangeLogs.List.Any(l => l.StepId == 9915 && l.FieldName == "ParentNC"),
               "T15d dự phòng cũ: trang gửi dòng cha khác (NC 5) → bỏ qua phần đổi cha (giữ NC 3, không lịch sử đổi cha), phần sửa tên vẫn lưu, không báo lỗi", e4);
            string? e5 = null; try { await t.UpdateAsync(9915, new PartTaroStep { NC = "7", WtsTaskCode = "2T", IsBackup = false, ParentNC = null, StepName = "chinh", StandardTime = 2 }, U, "thanh chinh"); } catch (Exception ex) { e5 = ex.Message; }
            var o5 = db.PartTaroSteps.List.Single(x => x.StepId == 9915);
            Ok(e5 == null && !o5.IsBackup && o5.NC == "7" && o5.WtsTaskCode == "2T", "T15e dự phòng cũ đổi thành dòng chính (2T NC 7) → được (gỡ dữ liệu cũ)", e5);
            // [TARO-DOI-CHEO-NC] lời máy chủ khi 2 dòng cùng mã đổi chéo NC lọt qua trang: nói rõ cách làm
            var a3 = db.PartTaroSteps.List.Single(x => x.PartId == pid && x.NC == "3" && x.WtsTaskCode == "2T");
            string? e6 = null; try { await t.UpdateAsync(a3.StepId, new PartTaroStep { NC = "5", WtsTaskCode = "2T", StepName = a3.StepName, StandardTime = a3.StandardTime }, U, "doi cheo"); } catch (Exception ex) { e6 = ex.Message; }
            Ok(e6 != null && e6.Contains("đổi chéo NC") && e6.Contains("lưu, rồi đổi tiếp") && db.PartTaroSteps.List.Single(x => x.StepId == a3.StepId).NC == "3",
               "T15f [TARO-DOI-CHEO-NC] 2T NC 3 → NC 5 khi 2T NC 5 còn đó → máy chủ chặn, lời báo nói cách đổi chéo (1 dòng sang NC khác, lưu, đổi tiếp)", e6);
        }

        await LamHo();
        await LamHoRv();
        await LamHoSuaMaVu();   // [T19] [LAMHO-SUA-MA-VU]
        await LamHoKcsCause();  // [T20] [LAMHO-L2-KCS]
        await LamHoTrang();     // [T21] [LAMHO-WTS-DSPO] [LAMHO-B-CHU]

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }

    // =====================================================================
    // [TARO-LAMHO-NC] Taro làm hộ ở NC máy loại trừ GIỮA chuỗi Gia công (như Part 3B-PT1268610 của khách):
    //   Gia công NC01 → NC02 → NC03 (H04, loại trừ) → NC04 → NC05 (H04, cuối chuỗi). Taro ở NC "3" (2T, bộ gộp 3T ← 3T1 + 3T2 + 3T3, 1T chuẩn bị)
    //   và NC "5" (2T). Khách (a): làm xong NC 2 → gửi Taro → về NC 4 → lại gửi Taro (NC 5). Đáp án tính tay theo luật Hùng 07/11 10:18.
    //   Mã ca: [T16] [T17] (+ [T18] ca rà độc lập). Đột biến phần máy chủ: 9 / 9 bị bắt.
    // =====================================================================
    static async Task<int> BuildLamHoPart(AppDbContext db, string partNo)
    {
        var pid = await ChuoiHelp.BuildPartAsync(db, partNo, new ChuoiHelp.Shape { Gc = 0, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
        var gc = new PartMachiningService(db);
        foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4"), ("NC05", "H04") })
            await gc.AddAsync(pid, new PartMachiningStep { NC = nc, MachineRegistered = m, TimingMachine = m, SetupTime = 10, MachiningTime = 5, InspectionTime = 1, PreparationTime = 2, TrialRunTime = 3 }, U, "lamho");
        var t = new PartTaroService(db);
        await t.AddAsync(pid, new PartTaroStep { NC = "3", WtsTaskCode = "1T", StepName = "Chuẩn bị", StandardTime = 2 }, U, "taro");
        foreach (var c in new[] { "2T", "3T", "3T1", "3T2", "3T3" })
            await t.AddAsync(pid, new PartTaroStep { NC = "3", WtsTaskCode = c, StepName = "Taro " + c, StandardTime = 1 }, U, "taro");
        await t.AddAsync(pid, new PartTaroStep { NC = "5", WtsTaskCode = "2T", StepName = "Taro 2T NC5", StandardTime = 0.5m }, U, "taro");
        return pid;
    }

    static async Task LamHo()
    {
        Console.WriteLine("[T16] [TARO-LAMHO-NC] Taro làm hộ NC 3 (máy loại trừ giữa chuỗi): vào = hàng giao theo NC, không vào 4 số HTSP; câu 1 trả về ≤ OK bộ; câu 2 NG trả Gia công ô Hàng lỗi");
        var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true); SeedGop(db);
        var pid = await BuildLamHoPart(db, "P-LH1");
        var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-LH1", new decimal[] { 10 }, "PO-LH"))[0];
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        var kho = new KhoVatLieuService(db, new UserService(null));
        var k0 = await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", U);
        var i1 = await svc.IssueByCellAsync(det, "KHO", "GC", 10, U, "MOI", false, "MOI");
        var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
        var w1 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 10, 0, U);
        var w2 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 10, 0, U);
        var hs = await svc.IssueSpecialAsync(det, "GC", "HTSP", 10, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho taro NC3");
        var hsId = db.HandoverTransactions.List.Last().HandoverTxId;
        var hr = await svc.ReceiveAsync(hsId, 9, 1, null, U, ngCategoryId: 1);   // HTSP đếm 1 chiếc lỗi lúc nhận → trả ngay Gia công
        Ok(k0.Success && i1.Ok && r1.Ok && w1.Ok && w2.Ok && hs.Ok && hr.Ok, "Chuẩn bị: phôi 10 → GC NC01, NC02 OK 10 → giao theo NC từ NC02 cho HTSP 10, HTSP nhận 9 + NG 1",
           $"{k0.Error} {i1.Error} {r1.Error} {w1.Err} {w2.Err} {hs.Error} {hr.Error}");
        var row = (await led.GetLedgerForDetailAsync(det))!;
        var run = row.LamHoRuns.SingleOrDefault(r => r.HasTaro);
        Ok(row.LamHoRuns.Count == 1 && run != null && run.Key == "NC03" && run.FromNc == "NC02" && run.NextNc == "NC04" && run.In == 9,
           "T16 đoạn làm hộ: NC03 (H04) giữa NC02 → NC04 (NC05 H04 cuối chuỗi không phải đoạn làm hộ); HTSP nhận vào 9 (phần OK lúc nhận)",
           string.Join(" | ", row.LamHoRuns.Select(r => $"{r.Key} {r.FromNc}->{r.NextNc} taro={r.HasTaro} in={r.In}")));
        Ok(L(row, "2T (NC 3)")?.LamHoNc == "NC03" && L(row, "2T (NC 3)")!.Waiting == 9 && L(row, "3T1")!.Waiting == 9
           && L(row, "2T (NC 5)")!.LamHoNc == null && L(row, "2T (NC 5)")!.Waiting == 0 && row.G("HTSP").ChuaLam == 0 && row.G("HTSP").LamHoGiu == 9,
           "T16a Taro NC 3 chờ 9 (Taro làm hộ, NC 3 khớp NC03); Taro NC 5 chờ 0; HTSP Chưa làm 0, Tồn hàng giao theo NC 9 (trước: Taro NC 3 vào 0 — chặn WTS)",
           string.Join(" · ", row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => $"{s.Code}={s.Waiting}{(s.LamHoNc != null ? "*" : "")}")));

        var a1 = await Taro(db, det, "2T", "3", 9);
        var a2 = await Taro(db, det, "3T", "3", 4, 1);
        var a2id = db.WtsProductionLogs.List.Last().WtsLogId;
        var ngCase = db.WtsProductionLogs.List.Last().NgCaseId;
        var a3 = await Taro(db, det, "3T1", "3", 3); var a4 = await Taro(db, det, "3T2", "3", 3); var a5 = await Taro(db, det, "3T3", "3", 3);
        row = (await led.GetLedgerForDetailAsync(det))!;
        run = row.LamHoRuns.Single(r => r.HasTaro);
        // tay: bộ 3T NC3 = OK 3T 4 + min(3T1..3T3) 3 = 7; 2T 9 → OK bộ Taro NC 3 = 7. NG 1 (3T) → vụ HTSP, chiếc nằm trong Tồn hàng giao theo NC.
        //   3T1 chờ = 9 − (4 + 1) − 3 = 1 · 3T chờ = 9 − 5 − max(3) = 1 · "chưa làm" NC 3 = 9 − 7 − 1 = 1
        Ok(a1.Ok && a2.Ok && a3.Ok && a4.Ok && a5.Ok && run.Done == 7 && L(row, "3T1")!.Waiting == 1 && L(row, "3T")!.Waiting == 1 && L(row, "2T (NC 3)")!.Waiting == 0,
           "T16b WTS Taro NC 3: 2T 9, 3T OK 4 NG 1, 3T1/3T2/3T3 3 → lưu được; OK bộ = min(9, 4 + 3) = 7; chờ 3T1 = 1, 3T = 1, 2T = 0",
           $"{a1.Err} {a2.Err} {a3.Err} done={run.Done} " + string.Join(" · ", row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => $"{s.Code}={s.Waiting}")));
        Ok(row.G("HTSP").ChuaLam == 0 && row.G("HTSP").DaLam == 0 && row.G("HTSP").NgCuoi == 0 && row.G("HTSP").LoiGiu == 0 && row.G("HTSP").LamHoGiu == 9
           && row.LamHoNgHeld("HTSP") == 1 && ngCase != null && row.CaseHeld(ngCase.Value, "HTSP", HandoverCell.LamHo) == 1,
           "T16c 4 số HTSP không đổi (Chưa làm 0, Đã làm 0, NG 0, Tồn hàng lỗi 0); chiếc NG nằm trong Tồn hàng giao theo NC 9, vụ giữ ô làm hộ 1 (câu 2)",
           $"CL {row.G("HTSP").ChuaLam} DL {row.G("HTSP").DaLam} NG {row.G("HTSP").NgCuoi} LG {row.G("HTSP").LoiGiu} LH {row.G("HTSP").LamHoGiu} lhNg {row.LamHoNgHeld("HTSP")}");
        var x1 = await Taro(db, det, "2T", "3", 1);
        Ok(!x1.Ok, "T16d ghi thêm 2T NC 3 (chờ 0) → chặn", x1.Err);

        // câu 1: trả về NC04 tối đa OK bộ 7
        var b1 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 8, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
        var b2 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 7, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
        var b2id = db.HandoverTransactions.List.Last().HandoverTxId;
        var b3 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 1, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
        var b4 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 1, U, HandoverPurpose.HelpReturn, null, "NC02", "tra NC2");
        Ok(!b4.Ok && b4.Error.Contains("NC04"), "T16e2 trả hàng làm hộ vào NC02 (không phải NC sau đoạn) → chặn, báo trả về NC04 (chiếc chưa xong Taro không lọt câu 1)", b4.Error);
        Ok(!b1.Ok && b1.Error.Contains("7") && b2.Ok && !b3.Ok, "T16e câu 1: trả về NC04 8 → chặn (OK bộ 7); 7 → được; thêm 1 → chặn (OK bộ − đã trả = 0)", $"{b1.Error} | {b3.Error}");
        var g1 = await svc.ReceiveAsync(b2id, 7, 0, null, U);
        row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(g1.Ok && row.G("GC").Steps.Single(s => s.Code == "NC04").Waiting == 7 && row.G("HTSP").LamHoGiu == 2, "T16f Gia công nhận 7 vào NC04: NC04 chờ 7; HTSP Tồn hàng giao theo NC 2 (1 chưa xong + 1 NG)", g1.Error);
        var v1 = await WtsSvcHelp.VoidAsync(db, a2id, det, U);
        Ok(!v1.Ok && db.WtsProductionLogs.List.Single(w => w.WtsLogId == a2id).IsVoided == false, "T16g K5: xoá dòng WTS 3T NC 3 (OK 4 NG 1) sau khi đã trả 7 → chặn (OK bộ còn 3 < đã trả 7)", v1.Err);

        // câu 2: Form → Hàng lỗi → "Hàng làm hộ bị lỗi" → trả Gia công ô Hàng lỗi, cùng vụ
        var pos = HandoverForm.FormPositions(row, "HTSP", false).FirstOrDefault(p => p.Id == "LAMHOLOI");
        Ok(pos != null && pos.Have == 1 && pos.HeldCell == HandoverCell.LamHo && HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO").Have == 0,
           "T16h form: dòng Hàng làm hộ bị lỗi có 1 (vụ); dòng hàng tốt LAMHO trả được 0 (OK bộ 7 − đã trả 7)", $"{pos?.Have} / {HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO").Have}");
        var bad = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = ngCase, Action = HandoverForm.AGiao, ToGroup = "KCS", Qty = 1 });
        var f1 = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = ngCase, Action = HandoverForm.AGiao, Qty = 1 });
        var lt = db.HandoverTransactions.List.Last();
        Ok(!bad.Ok && f1.Ok && lt.Purpose == HandoverPurpose.HelpReturn && lt.FromCell == HandoverCell.Loi && lt.ToCell == HandoverCell.Loi && lt.NgCaseId == ngCase && lt.ToNC == null && lt.QtyIssued == 1,
           "T16i trả lỗi: giao KCS → chặn; trả Gia công 1 → phiếu Trả về Gia công theo NC ô Hàng lỗi → ô Hàng lỗi, mang vụ, không NC", $"{bad.Error} | {f1.Error}");
        var gx = await svc.ReceiveAsync(lt.HandoverTxId, 0, 1, null, U, toCell: HandoverCell.Xl, toCellReason: "doi o");
        var gl = await svc.ReceiveAsync(lt.HandoverTxId, 0, 1, null, U);
        row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(!gx.Ok && gl.Ok && row.G("HTSP").LamHoGiu == 1 && row.LamHoNgHeld("HTSP") == 0 && row.CaseHeld(ngCase!.Value, "GC", HandoverCell.Loi) == 1,
           "T16j Gia công nhận: đổi sang ô Hàng lỗi đã xử lý → chặn; nhận ô Hàng lỗi → vụ về Tồn hàng lỗi Gia công; HTSP còn 1 chiếc làm hộ chưa xong", $"{gx.Error} | {gl.Error}");
        var v2 = await WtsSvcHelp.VoidAsync(db, a2id, det, U);
        Ok(!v2.Ok, "T16k K5: xoá dòng WTS 3T NG sau khi chiếc lỗi đã trả Gia công → chặn (vụ không còn nguồn)", v2.Err);

        // sau NC04: Gia công giao Hàng mới cho HTSP làm Taro NC 5 (cuối chuỗi) — chỉ bắt nhập Taro NC 5, không bắt nhập lại Taro NC 3
        var w4 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC04", null, 7, 0, U);
        var i5 = await svc.IssueByCellAsync(det, "GC", "HTSP", 7, U, "MOI", false, "MOI");
        var r5 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 7, 0, null, U);
        row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(w4.Ok && i5.Ok && r5.Ok && row.G("HTSP").ChuaLam == 7 && L(row, "2T (NC 5)")!.Waiting == 7 && L(row, "2T (NC 3)")!.Waiting == 0,
           "T16l GC NC04 7 → giao Hàng mới HTSP 7: HTSP Chưa làm 7, Taro NC 5 chờ 7, Taro NC 3 không đòi thêm", $"{w4.Err} {i5.Error} {r5.Error}");
        var t5 = await Taro(db, det, "2T", "5", 7);
        row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(t5.Ok && row.G("HTSP").DaLam == 7 && row.G("HTSP").ConGiao == 7 && row.G("HTSP").ChuaLam == 0,
           "T16m WTS 2T NC 5 = 7 → HTSP Đã làm 7 (OK nhỏ nhất chỉ xét mã của HTSP — trước: kẹt ở Taro NC 3 phải nhập lại)", t5.Err);
        var warn = row.Groups.Values.SelectMany(g => g.Warnings).ToList();
        var bal = row.TonTong + row.DangGiao + row.TpNhap + row.DaHuy + row.TraKhach;
        Ok(warn.Count == 0 && bal == row.Phoi && HandoverLedgerService.NegativeCells(row).Count == 0,
           "T16n cân PO (L6) = phôi 10, không cảnh báo, không ô âm", $"bal {bal} / phôi {row.Phoi} · {string.Join(" | ", warn)} · {string.Join(",", HandoverLedgerService.NegativeCells(row).Keys)}");
        var qs = await QuetCore.ScanAsync(db, led);
        var qd = qs.Findings.Where(f => f.DetailId == det).ToList();
        Ok(qd.Count == 1 && qd[0].Kind == QuetCore.LamHoTaro && qd[0].Detail.Contains("OK bộ Taro 7") && qd[0].Detail.Contains("đã trả về 7"),
           "T16o trạm quet: PO chỉ có 1 dòng LAMHO_TARO (chỉ để biết: nhận 9 · OK bộ 7 · đã trả 7), không phát hiện lỗi số", string.Join(" | ", qd.Select(f => f.Kind + ": " + f.Detail)));
        var ng0 = HandoverNgTrace.Build((await led.GetInputForDetailAsync(det))!);
        Ok(ng0.Problems.Count == 0, "T16p khung đường đi hàng lỗi (NgTrace) khớp sổ: WTS Taro làm hộ NG + phiếu trả lỗi + Gia công nhận", string.Join(" | ", ng0.Problems));

        Console.WriteLine("[T17] [TARO-LAMHO-NC] Workflow OFF: câu 1 không chặn (chỉ Tồn hàng giao theo NC còn tốt); PO không Taro ở NC loại trừ giữa chuỗi: như cũ");
        {
            var db2 = new AppDbContext(); ChuoiHelp.SeedBase(db2, exclH04: true); SeedGop(db2);
            var pid2 = await BuildLamHoPart(db2, "P-LH2");
            var d2 = (await ChuoiHelp.CreateKhAsync(db2, pid2, "P-LH2", new decimal[] { 5 }, "PO-LH2", workflow: false))[0];
            var s2 = new HandoverService(db2);
            await new KhoVatLieuService(db2, new UserService(null)).SaveKhoInputAsync(d2, DateTime.Now, 5, "OVL", "A1", null, null, null, "phoi", U);
            await s2.IssueByCellAsync(d2, "KHO", "GC", 5, U, "MOI", false, "MOI"); await s2.ReceiveAsync(db2.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await WtsSvcHelp.SaveAsync(db2, d2, "GC", "NC01", null, 5, 0, U); await WtsSvcHelp.SaveAsync(db2, d2, "GC", "NC02", null, 5, 0, U);
            await s2.IssueSpecialAsync(d2, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho"); await s2.ReceiveAsync(db2.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db2, d2, "2T", "3", 2);
            await Taro(db2, d2, "3T", "3", 0, 1);   // 1 chiếc làm hộ bị lỗi (vụ) — vẫn nằm trong Tồn hàng giao theo NC
            var o0 = await s2.IssueSpecialAsync(d2, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra");
            var o1 = await s2.IssueSpecialAsync(d2, "HTSP", "GC", 4, U, HandoverPurpose.HelpReturn, null, "NC04", "tra");
            Ok(!o0.Ok && o1.Ok, "T17a Workflow OFF: trả hàng tốt về NC04 5 → chặn (1 chiếc làm hộ bị lỗi không trả như hàng tốt); 4 → được dù OK bộ Taro mới 0 (câu 1 chỉ Workflow ON)", $"{o0.Error} | {o1.Error}");

            // hàng HTSP nhận thẳng (không qua đoạn làm hộ): Đã làm HTSP chỉ xét mã của HTSP (Taro NC 5), không bị mã Taro làm hộ NC 3 (OK 0) kéo xuống
            var db4 = new AppDbContext(); ChuoiHelp.SeedBase(db4, exclH04: true); SeedGop(db4);
            var pid4 = await BuildLamHoPart(db4, "P-LH4");
            var d4 = (await ChuoiHelp.CreateKhAsync(db4, pid4, "P-LH4", new decimal[] { 5 }, "PO-LH4"))[0];
            var s4 = new HandoverService(db4);
            await new KhoVatLieuService(db4, new UserService(null)).SaveKhoInputAsync(d4, DateTime.Now, 5, "OVL", "A1", null, null, null, "phoi", U);
            var k4 = await s4.IssueByCellAsync(d4, "KHO", "HTSP", 5, U, "MOI", false, "MOI");
            var n4 = await s4.ReceiveAsync(db4.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            var t4 = await Taro(db4, d4, "2T", "5", 5);
            var row4 = (await new HandoverLedgerService(db4).GetLedgerForDetailAsync(d4))!;
            Ok(k4.Ok && n4.Ok && t4.Ok && row4.G("HTSP").DaLam == 5 && row4.G("HTSP").ChuaLam == 0 && L(row4, "2T (NC 3)")!.Waiting == 0,
               "T17c Kho giao thẳng HTSP 5, WTS 2T NC 5 = 5 → HTSP Đã làm 5 (mã Taro làm hộ NC 3 OK 0 không tính vào OK nhỏ nhất), Taro NC 3 chờ 0", $"{k4.Error} {n4.Error} {t4.Err} DL {row4.G("HTSP").DaLam}");

            var db3 = new AppDbContext(); ChuoiHelp.SeedBase(db3, exclH04: true);
            var pid3 = await ChuoiHelp.BuildPartAsync(db3, "P-LH3", new ChuoiHelp.Shape { Gc = 3, ExclAt = 1, DongDang = false, DuPhong = false, Taro = 1, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
            var d3 = (await ChuoiHelp.CreateKhAsync(db3, pid3, "P-LH3", new decimal[] { 4 }, "PO-LH3"))[0];
            var row3 = (await new HandoverLedgerService(db3).GetLedgerForDetailAsync(d3))!;
            Ok(row3.LamHoRuns.Count == 1 && !row3.LamHoRuns[0].HasTaro && row3.G("HTSP").Steps.All(s => s.LamHoNc == null),
               "T17b PO có NC loại trừ giữa chuỗi nhưng Taro ở NC riêng (T1): đoạn không có Taro → mã Taro tính như cũ");
        }
    }

    // [T18] [TARO-LAMHO-NC] Ca từ rà độc lập Gói 6 (vòng 1): 1 Cao + các Trung — bản trước vòng sửa: T18a / T18b / T18c / T18d / T18e / T18f SAI
    static async Task<(AppDbContext db, int det)> LhStart(string partNo, (string nc, string m)[] gcs, (string nc, string code)[] taros, decimal q, string[] gcDone, int bavia = 0, bool wfOff = false)
    {
        var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: true);
        var pid = await ChuoiHelp.BuildPartAsync(db, partNo, new ChuoiHelp.Shape { Gc = 0, DongDang = false, DuPhong = false, Taro = 0, Bavia = bavia, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
        var gc = new PartMachiningService(db);
        foreach (var (nc, m) in gcs)
            await gc.AddAsync(pid, new PartMachiningStep { NC = nc, MachineRegistered = m, TimingMachine = m, SetupTime = 10, MachiningTime = 5, InspectionTime = 1, PreparationTime = 2, TrialRunTime = 3 }, U, "lamho");
        var t = new PartTaroService(db);
        foreach (var (nc, c) in taros) await t.AddAsync(pid, new PartTaroStep { NC = nc, WtsTaskCode = c, StepName = "Taro " + c, StandardTime = 1 }, U, "taro");
        var det = (await ChuoiHelp.CreateKhAsync(db, pid, partNo, new decimal[] { q }, "PO-" + partNo))[0];
        if (wfOff) { db.KhPlans.List.First(p => db.KhPlanDetails.List.Any(d => d.KhPlanDetailId == det && d.KhPlanId == p.KhPlanId)).ApplyWorkflow = false; db.Fixup(); }   // [T19d]
        var svc = new HandoverService(db);
        await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(det, DateTime.Now, (int)q, "OVL", "A1", null, null, null, "phoi", U);
        await svc.IssueByCellAsync(det, "KHO", "GC", q, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, q, 0, null, U);
        foreach (var nc in gcDone) await WtsSvcHelp.SaveAsync(db, det, "GC", nc, null, q, 0, U);
        return (db, det);
    }

    static async Task LamHoRv()
    {
        Console.WriteLine("[T18] [TARO-LAMHO-NC] rà độc lập gói 6: số dư từng đoạn, giao theo NC đúng NC / đúng nhóm, mã chuẩn bị, HTSP chỉ làm hộ, NC làm tiếp của vụ làm hộ");
        var G6 = new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4"), ("NC05", "H04"), ("NC06", "M6") };
        var G4 = new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4") };
        {   // S1 (Cao): 2 đoạn — NC03 có Taro, NC05 không — hàng đoạn NC03 trả qua NC06 (NC sau đoạn kia) lách câu 1
            var (db, det) = await LhStart("P-LH5", G6, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            var b6 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC06", "tra NC6");
            Ok(!b6.Ok, "T18a hàng đoạn NC03 (Taro chưa làm) trả vào NC06 (NC sau đoạn NC05 không Taro) → chặn (số dư đoạn NC05 = 0)", b6.Error);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(det))!;
            Ok(row.LamHoRuns.Single(r => r.Key == "NC03").Held == 5 && row.LamHoRuns.Single(r => r.Key == "NC05").Held == 0, "T18a2 số dư: đoạn NC03 còn 5, đoạn NC05 0");
        }
        {   // S9: hàng đoạn có Taro giao theo NC cho KCS thay vì HTSP
            var (db, det) = await LhStart("P-LH6", G4, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var hs = await new HandoverService(db).IssueSpecialAsync(det, "GC", "KCS", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            Ok(!hs.Ok && hs.Error.Contains("HTSP"), "T18b giao theo NC từ NC02 (đoạn sau có Taro) cho KCS → chặn, báo giao cho HTSP", hs.Error);
        }
        {   // S7: giao theo NC từ NC01 (không phải NC ngay trước đoạn)
            var (db, det) = await LhStart("P-LH7", G4, new[] { ("3", "2T") }, 5, new[] { "NC01" });
            var hs = await new HandoverService(db).IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC01", null, "lam ho NC02");
            Ok(!hs.Ok && hs.Error.Contains("chỉ từ NC02"), "T18c PO có Taro làm hộ: giao theo NC từ NC01 (NC sau là NC thường) → chặn, báo chỉ từ NC02", hs.Error);
        }
        {   // S2: NC làm hộ chỉ có mã chuẩn bị 1T → không phải đoạn Taro (không kẹt câu 1)
            var (db, det) = await LhStart("P-LH8", G4, new[] { ("3", "1T"), ("T9", "2T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            var row = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(det))!;
            var b = await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            Ok(!row.LamHoRuns[0].HasTaro && b.Ok, "T18d NC 3 chỉ có mã chuẩn bị 1T: đoạn không tính Taro làm hộ, trả về NC04 5 được (trước: tối đa 0, kẹt)", b.Error);
        }
        {   // S3: HTSP chỉ có mã Taro làm hộ → không giao Hàng mới cho HTSP
            var (db, det) = await LhStart("P-LH9", G4, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db, det, "2T", "3", 5);
            await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC04", null, 5, 0, U);
            var i = await svc.IssueByCellAsync(det, "GC", "HTSP", 5, U, "MOI", false, "MOI");
            var k = await svc.IssueByCellAsync(det, "GC", "KCS", 5, U, "MOI", false, "MOI");
            Ok(!i.Ok && i.Error.Contains("Taro làm hộ") && k.Ok, "T18e HTSP chỉ làm Taro làm hộ: giao Hàng mới cho HTSP → chặn (trước: hàng vào HTSP kẹt, Đã làm luôn 0); giao KCS → được", $"{i.Error} | {k.Error}");
        }
        {   // S4: vụ Taro làm hộ NG về Gia công → NC làm tiếp mặc định như NG lúc nhận hàng giao theo NC (NC trước đoạn), không nhảy NC cuối
            var (db, det) = await LhStart("P-LHA", G6, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db, det, "2T", "3", 4, 1);
            var cs = db.WtsProductionLogs.List.Last().NgCaseId;
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var def = HandoverForm.CaseNextNc(row, cs!.Value, "GC");
            Ok(def == "NC02", "T18f vụ Taro làm hộ NG: NC làm tiếp mặc định ở Gia công = NC02 (làm lại NC trước rồi giao Taro lại), không phải NC06 (cuối)", def);
        }
        {   // vòng 2 T1: đoạn 2 NC loại trừ liền nhau, Taro ở cả 2 NC — NG ở NC 3 cũng là chiếc đã rời với mã NC 4
            var (db, det) = await LhStart("P-LHB", new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "H04"), ("NC05", "M5") }, new[] { ("3", "2T"), ("4", "4T") }, 10, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 10, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, U);
            var a1 = await Taro(db, det, "2T", "3", 7, 3);
            var a2 = await Taro(db, det, "4T", "4", 10);
            var a3 = await Taro(db, det, "4T", "4", 7);
            Ok(a1.Ok && !a2.Ok && a3.Ok, "T18g đoạn NC03 + NC04: 2T NC 3 OK 7 NG 3 → 4T NC 4 ghi 10 bị chặn (chỉ còn 7 chiếc tốt), ghi 7 được (vòng 2 T1)", $"{a1.Err} | {a2.Err} | {a3.Err}");
        }
        {   // vòng 2 T2-R2: hàng giao theo NC lúc Workflow OFF từ NC01 (không thuộc đoạn) → bật ON vẫn trả về được (vào NC khác NC sau đoạn), không kẹt
            var (db, det) = await LhStart("P-LHC", G4, new[] { ("3", "2T") }, 5, new[] { "NC01" });
            var plan = db.KhPlans.List.First(p => db.KhPlanDetails.List.Any(d => d.KhPlanDetailId == det && d.KhPlanId == p.KhPlanId));
            plan.ApplyWorkflow = false; db.Fixup();
            var svc = new HandoverService(db);
            var hs = await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC01", null, "lam ho NC02");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            plan.ApplyWorkflow = true; db.Fixup();
            var b4 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            var b2 = await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC02", "tra NC2");
            Ok(hs.Ok && !b4.Ok && b2.Ok, "T18h hàng giao theo NC không thuộc đoạn (Workflow OFF từ NC01): bật ON → trả NC04 bị chặn (đoạn NC03 không có hàng), trả NC02 được (trước: kẹt)", $"{hs.Error} | {b4.Error} | {b2.Error}");
        }
        {   // vòng 2 T2-R3: KCS giữ hàng đoạn có Taro (giao lúc Workflow OFF) → bật ON không trả thẳng vào NC sau đoạn
            var (db, det) = await LhStart("P-LHD", G4, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var plan = db.KhPlans.List.First(p => db.KhPlanDetails.List.Any(d => d.KhPlanDetailId == det && d.KhPlanId == p.KhPlanId));
            plan.ApplyWorkflow = false; db.Fixup();
            var svc = new HandoverService(db);
            var hs = await svc.IssueSpecialAsync(det, "GC", "KCS", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            plan.ApplyWorkflow = true; db.Fixup();
            var b4 = await svc.IssueSpecialAsync(det, "KCS", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            var b2 = await svc.IssueSpecialAsync(det, "KCS", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC02", "tra NC2");
            Ok(hs.Ok && !b4.Ok && b2.Ok, "T18i KCS giữ hàng đoạn có Taro: trả vào NC04 (sau đoạn) → chặn (Taro chưa làm); trả NC02 được", $"{hs.Error} | {b4.Error} | {b2.Error}");
        }
        {   // vòng 2 L1: HTSP chỉ làm hộ — nhận phiếu ô Hàng lỗi rồi đổi sang Hàng mới cũng bị chặn
            var (db, det) = await LhStart("P-LHE", G4, new[] { ("3", "2T") }, 5, new string[0]);
            var svc = new HandoverService(db);
            var i = await svc.IssueByCellAsync(det, "GC", "HTSP", 1, U, "LOI", true, "LOI", "NC01", null, null, null, 1);
            var tid = db.HandoverTransactions.List.Last().HandoverTxId;
            var r = await svc.ReceiveAsync(tid, 0, 1, null, U, toCell: HandoverCell.Moi, toCellReason: "hang tot");
            Ok(i.Ok && !r.Ok && r.Error.Contains("Taro làm hộ"), "T18j HTSP chỉ làm Taro làm hộ: nhận phiếu Hàng lỗi rồi đổi ô sang Hàng mới → chặn (vòng 2 L1)", $"{i.Error} | {r.Error}");
        }
        {   // vòng 3 T2: vụ Taro làm hộ NG trả Gia công → Gia công trả nơi gây lỗi (HTSP ô Hàng lỗi) → HTSP không sửa tại chỗ / không giao vào ô hàng đạt nhóm khác
            var (db, det) = await LhStart("P-LHF", G6, new[] { ("3", "2T"), ("5", "5T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db, det, "2T", "3", 4, 1);
            var cs = db.WtsProductionLogs.List.Last().NgCaseId!.Value;
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = cs, Action = HandoverForm.AGiao, Qty = 1 });
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 0, 1, null, U);
            var tra = await svc.IssueByCellAsync(det, "GC", "HTSP", 1, U, "LOI", false, "LOI", null, null, null, null, null, cs);   // Gia công chuyển vụ về HTSP ô Hàng lỗi (nơi gây lỗi)
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 0, 1, null, U);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var kq = await svc.CreateRepairResultAsync(det, "HTSP", HandoverCell.Loi, cs, 1, 0, null, "sua", U);
            var rk = await svc.IssueByCellAsync(det, "HTSP", "KCS", 1, U, "LOI", false, "XL", null, null, null, null, null, cs);
            var rg = await svc.IssueByCellAsync(det, "HTSP", "GC", 1, U, "LOI", false, "LOI", null, null, null, null, null, cs);
            Ok(f.Ok && tra.Ok && row.CaseHeld(cs, "HTSP", HandoverCell.Loi) == 1 && !kq.Ok && kq.Error.Contains("Taro làm hộ") && !rk.Ok && rk.Error.Contains("Taro làm hộ") && rg.Ok,
               "T18k vụ Taro làm hộ ở HTSP ô Hàng lỗi: sửa tại HTSP → chặn; giao KCS ô Hàng lỗi đã xử lý → chặn; giao trả Gia công ô Hàng lỗi → được (vòng 3 T2)", $"{f.Error} | {tra.Error} | {kq.Error} | {rk.Error} | {rg.Error}");
        }
        {   // vòng 3 T1: hàng đoạn không Taro (NC05) đang ở HTSP — dòng form LAMHO có số (trả vào NC06), không bị khoá 0
            var (db, det) = await LhStart("P-LHG", G6, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db, det, "2T", "3", 5);
            await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC04", null, 5, 0, U);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC04", null, "lam ho NC05");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var pos = HandoverForm.FormPositions(row, "HTSP", true).Single(p => p.Id == "LAMHO");
            var sv = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Tot, Pos = "LAMHO", Action = HandoverForm.AGiao, ToGroup = "GC", ToNC = "NC06", Qty = 5 });
            Ok(pos.Have == 5 && sv.Ok, "T18l hàng đoạn NC05 (không Taro) ở HTSP: dòng LAMHO có 5, trả NC06 qua form được (vòng 3 T1 — trước: dòng có 0, khoá)", $"{pos.Have} | {sv.Error}");
        }
    }

    // [T19] [LAMHO-SUA-MA-VU 2026-11-07] Sửa dòng WTS Taro NG làm hộ đổi mã / NC sang ĐOẠN máy loại trừ khác: NC Gia công làm tiếp của vụ (K2, sổ tính
    //   HelpFromNc theo đoạn của mã) đổi theo → vụ đã đi phiếu thì chặn; chưa đi thì cho (sổ tự theo). PO NC01 → NC02 → NC03 (H04) → NC04 → NC05 (H04) → NC06:
    //   Taro 2T ở NC 3 (đoạn NC03, làm tiếp NC02), 5T ở NC 5 (đoạn NC05, làm tiếp NC04). Bản becb81e: T19a SAI (lưu được, NC làm tiếp nhảy NC02 → NC04).
    //   Nguồn: CLAUDE (rà Gói 7 vòng 2, sổ dòng LAMHO-SUA-MA-VU); Hùng 07/11 22:38 giao Gói 8.
    static async Task LamHoSuaMaVu()
    {
        Console.WriteLine("[T19] [LAMHO-SUA-MA-VU] sửa dòng WTS Taro NG làm hộ sang đoạn khác: vụ đã đi → chặn; chưa đi → được, NC làm tiếp theo đoạn mới");
        var G6 = new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4"), ("NC05", "H04"), ("NC06", "M6") };
        async Task<(AppDbContext db, int det, long ngLog, long cs)> Setup(string partNo)
        {
            var (db, det) = await LhStart(partNo, G6, new[] { ("3", "2T"), ("5", "5T") }, 5, new[] { "NC01", "NC02" });
            var svc = new HandoverService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho NC03");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            await Taro(db, det, "2T", "3", 4);
            var ng = await Taro(db, det, "2T", "3", 0, 1);                     // dòng riêng: NG 1 → vụ Taro làm hộ đoạn NC03
            await svc.IssueSpecialAsync(det, "HTSP", "GC", 4, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 4, 0, null, U);
            await WtsSvcHelp.SaveAsync(db, det, "GC", "NC04", null, 4, 0, U);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 4, U, HandoverPurpose.HelpSend, "NC04", null, "lam ho NC05");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 4, 0, null, U);
            return (db, det, ng.LogId!.Value, db.WtsProductionLogs.List.Single(w => w.WtsLogId == ng.LogId).NgCaseId!.Value);
        }
        {   // vụ đã trả về Gia công (đi phiếu) → đổi dòng NG sang 5T NC 5 bị chặn, sổ giữ NC làm tiếp NC02
            var (db, det, lid, cs) = await Setup("P-LHS1");
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = cs, Action = HandoverForm.AGiao, Qty = 1 });
            var e = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "5T", 0, 1, U, 1, keepCase: cs, editLogId: lid, stepNc: "5");
            var row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(f.Ok && !e.Ok && (e.Err ?? "").Contains("NC Gia công làm tiếp") && HandoverForm.CaseNextNc(row, cs, "GC") == "NC02"
               && !db.WtsProductionLogs.List.Single(w => w.WtsLogId == lid).IsVoided,
               "T19a vụ Taro làm hộ đã trả về Gia công: sửa dòng NG 2T NC 3 → 5T NC 5 bị chặn, dòng cũ giữ, NC làm tiếp vẫn NC02", $"{f.Error} | {e.Err}");
            var e2 = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "2T", 0, 1, U, 1, keepCase: cs, editLogId: lid, stepNc: "3");
            Ok(e2.Ok, "T19b cùng vụ, sửa dòng giữ mã / NC (cùng đoạn) → được", e2.Err);
        }
        {   // vụ chưa đi phiếu → đổi được, sổ tính NC làm tiếp theo đoạn mới (NC04)
            var (db, det, lid, cs) = await Setup("P-LHS2");
            var led = new HandoverLedgerService(db);
            var e = await WtsSvcHelp.SaveAsync(db, det, "TARO", null, "5T", 0, 1, U, 1, keepCase: cs, editLogId: lid, stepNc: "5");
            var row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(e.Ok && HandoverForm.CaseNextNc(row, cs, "GC") == "NC04", "T19c vụ chưa đi phiếu: sửa dòng NG 2T NC 3 → 5T NC 5 được, NC làm tiếp theo đoạn mới = NC04", $"{e.Err} | {HandoverForm.CaseNextNc(row, cs, "GC")}");
        }
        {   // rà độc lập Gói 8 (Trung-1): Workflow OFF, vụ Taro làm hộ đã trả về Gia công → sửa dòng NG sang Bavia (mất đoạn làm hộ) → chặn (K5 không chặn khi OFF)
            var (db, det) = await LhStart("P-LHS3", G6, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" }, bavia: 1, wfOff: true);
            var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho NC03");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
            var ng = await Taro(db, det, "2T", "3", 0, 1);
            var lid = ng.LogId!.Value; var cs = db.WtsProductionLogs.List.Single(w => w.WtsLogId == lid).NgCaseId!.Value;
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = cs, Action = HandoverForm.AGiao, Qty = 1 });
            var e = await WtsSvcHelp.SaveAsync(db, det, "BAVIA", null, "B1", 0, 1, U, 1, keepCase: cs, editLogId: lid);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            Ok(f.Ok && !e.Ok && (e.Err ?? "").Contains("NC Gia công làm tiếp") && HandoverForm.CaseNextNc(row, cs, "GC") == "NC02",
               "T19d Workflow OFF: sửa dòng NG Taro làm hộ (vụ đã trả về Gia công) sang Bavia B1 → chặn, NC làm tiếp vẫn NC02 (rà độc lập Gói 8 Trung-1)", $"{f.Error} | {e.Err}");
        }
    }

    // [T20] [LAMHO-L2-KCS 2026-11-07] PO mà HTSP chỉ có Taro làm hộ: Gia công giao thẳng KCS; KCS tích "Lỗi công đoạn trước" bỏ trống nơi gây lỗi → nơi gây lỗi
    //   = Gia công (máy chủ HandoverService.GiaoXuLy + trang GiaoXuLyModal dùng HandoverForm.PrevGroup(nhóm, sổ)). PO thường: vẫn HTSP.
    //   Bản becb81e: T20a / T20b SAI (vụ ghi HTSP gây, phiếu không phải Hàng lỗi công đoạn trước về Gia công). Nguồn: CLAUDE (rà độc lập Gói 6 L2).
    static async Task LamHoKcsCause()
    {
        Console.WriteLine("[T20] [LAMHO-L2-KCS] HTSP chỉ Taro làm hộ: KCS báo lỗi công đoạn trước, bỏ trống nơi gây lỗi → Gia công");
        var G4 = new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4") };
        var (db, det) = await LhStart("P-LHK1", G4, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
        await Taro(db, det, "2T", "3", 5);
        await svc.IssueSpecialAsync(det, "HTSP", "GC", 5, U, HandoverPurpose.HelpReturn, null, "NC04", "tra NC4");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
        await WtsSvcHelp.SaveAsync(db, det, "GC", "NC04", null, 5, 0, U);
        await svc.IssueByCellAsync(det, "GC", "KCS", 5, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
        var row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(HandoverForm.PrevGroup("KCS", row) == "GC" && HandoverForm.PrevGroup("HTSP", row) == "GC" && HandoverForm.PrevGroup("PKG", row) == "KCS",
           "T20a nhóm trước theo PO: KCS ← Gia công (HTSP chỉ làm hộ); HTSP ← Gia công, Đóng gói ← KCS giữ nguyên", HandoverForm.PrevGroup("KCS", row));
        var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "KCS", UserId = U, Kind = HandoverForm.Loi, Pos = "CHUA", Prev = true, Cause = null,
                                                               CatId = 1, Action = HandoverForm.AGiao, Purpose = HandoverForm.PTra, ToGroup = "GC", Qty = 1 });
        var tx = db.HandoverTransactions.List.Last();
        var cs = tx.NgCaseId == null ? null : db.NgCases.List.FirstOrDefault(c => c.NgCaseId == tx.NgCaseId);
        Ok(f.Ok && tx.Purpose == HandoverPurpose.NgReturn && tx.ToGroupCode == "GC" && cs?.CauseGroupCode == "GC",
           "T20b KCS lỗi công đoạn trước, bỏ trống nơi gây lỗi, trả Gia công → phiếu Hàng lỗi công đoạn trước về Gia công, vụ ghi Gia công gây (trước: HTSP)",
           $"{f.Error} | {tx.Purpose} → {tx.ToGroupCode} · do {cs?.CauseGroupCode}");
        {   // PO thường (HTSP có mã chính không làm hộ): KCS ← HTSP như cũ
            var (db2, det2) = await LhStart("P-LHK2", G4, new[] { ("3", "2T"), ("T9", "9T") }, 5, new[] { "NC01" });
            var row2 = (await new HandoverLedgerService(db2).GetLedgerForDetailAsync(det2))!;
            Ok(HandoverForm.PrevGroup("KCS", row2) == "HTSP", "T20c PO HTSP có mã thường (không chỉ làm hộ): nhóm trước của KCS vẫn HTSP", HandoverForm.PrevGroup("KCS", row2));
        }
    }

    // [T21] [LAMHO-WTS-DSPO][LAMHO-B-CHU 2026-11-07] Thử live PO TEST-0711-TARO: (a) HTSP chỉ nhận hàng giao theo NC cho Taro làm hộ → trang WTS phải hiện PO
    //   (HandoverLedgerService.HasWtsInput — trước lọc theo Nhận sản xuất = 0 nên ẩn); (b) dòng B lần trả / nhận hàng làm hộ bị lỗi không ghi "NG … trả ngay".
    //   Bản becb81e: T21b SAI (chữ cũ); T21a không có hàm (phần trang kiểm ở harness drive_g8.py). Nguồn: CLAUDE (thử live 07/11 22:4x).
    static async Task LamHoTrang()
    {
        Console.WriteLine("[T21] [LAMHO-WTS-DSPO][LAMHO-B-CHU] HTSP chỉ có hàng làm hộ: PO có hàng vào để ghi WTS; dòng B trả / nhận hàng làm hộ bị lỗi đúng chữ");
        var G4 = new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H04"), ("NC04", "M4") };
        var (db, det) = await LhStart("P-LHT1", G4, new[] { ("3", "2T") }, 5, new[] { "NC01", "NC02" });
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        var row0 = (await led.GetLedgerForDetailAsync(det))!;
        await svc.IssueSpecialAsync(det, "GC", "HTSP", 5, U, HandoverPurpose.HelpSend, "NC02", null, "lam ho");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 5, 0, null, U);
        var row = (await led.GetLedgerForDetailAsync(det))!;
        Ok(!HandoverLedgerService.HasWtsInput(row0, "HTSP") && row.G("HTSP").NhanSanXuat == 0 && HandoverLedgerService.HasWtsInput(row, "HTSP") && HandoverLedgerService.HasWtsInput(row, "GC"),
           "T21a HTSP nhận 5 giao theo NC (Taro làm hộ), Nhận sản xuất 0 → vẫn có hàng vào ghi WTS (trước khi nhận: chưa)", $"{row.G("HTSP").NhanSanXuat}");
        await Taro(db, det, "2T", "3", 3, 2);
        var cs = db.WtsProductionLogs.List.Last().NgCaseId!.Value;
        var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = det, Group = "HTSP", UserId = U, Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = cs, Action = HandoverForm.AGiao, Qty = 2 });
        var tid = db.HandoverTransactions.List.Last().HandoverTxId;
        await svc.ReceiveAsync(tid, 0, 2, null, U);
        var steps = HandoverLedgerService.ComputeSteps((await led.GetInputForDetailAsync(det))!);
        var nhan = steps.LastOrDefault(x => x.Kind == "NHAN" && x.Text.Contains("phiếu #" + tid))?.Text ?? "";
        Ok(f.Ok && nhan.Contains("hàng làm hộ bị lỗi") && nhan.Contains("ô Hàng lỗi") && !nhan.Contains("trả ngay"),
           "T21b dòng B Gia công nhận hàng làm hộ bị lỗi: \"nhận 2 hàng làm hộ bị lỗi · ô Hàng lỗi (cùng vụ)\", không ghi \"NG 2 trả ngay\"", nhan);
        Ok(steps.Any(x => x.Text.Contains("trả hàng làm hộ bị lỗi 2")), "T21c dòng B HTSP lập phiếu: \"trả hàng làm hộ bị lỗi 2 → Gia công ô Hàng lỗi (cùng vụ)\"",
           string.Join(" | ", steps.Where(x => x.Text.Contains("HTSP tr")).Select(x => x.Text)));
    }
}
