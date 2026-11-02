// [STD-UPDATE][STD-UPDATE-V2][STD-UPDATE-V3][STD-UPD][STD-5 2026-11-02] Kịch bản cố định `std`: nút "Update TG chuẩn" ở Part Master
//   bằng HÀM THẬT Services/StdUpdateService (PreviewAsync / RunAsync / SnapshotFingerprint). Part + 3 PO dựng qua chuỗi thật
//   (ChuoiHelp: PartMachiningService… → KhPlanService.CreateAsync + CopyRouteSnapshotAsync), WTS lưu bằng WtsSaveService thật
//   (số chuẩn đóng băng WtsProductionLogStds trỏ đúng dòng snapshot). Sửa Part Master bằng service thật (+ máy đồng dạng ghi như trang).
//   Phần chỉ có ở trang (tick sẵn PO, ẩn nút Chạy, mục trống ẩn) kiểm ở harness drive_std.py.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class StdScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static async Task<string?> Throws(Func<Task> f) { try { await f(); return null; } catch (Exception ex) { return ex.Message; } }

    const int Admin = 1, Xuong = 2, StdUser = 7;   // u7: nhóm chỉ có quyền PART_STD_UPDATE (không ADMIN)
    const string R = "kiem std";

    sealed class W
    {
        public AppDbContext Db = null!; public int PartId; public int A, B, C;   // A: có WTS trước + sau mốc · B: có WTS sau mốc · C: chưa có WTS
        public DateTime Moc; public long WBefore, WAfter, WTiming, WB;
        public long Nc01, Nc02, TimM1b, Bav, Kcs;
        public long SnapNc01(int d) => Db.KhPlanRouteSnapshotMachining.List.Single(s => s.KhPlanDetailId == d && s.SourceStepId == Nc01).SnapshotId;
        public long SnapNc02(int d) => Db.KhPlanRouteSnapshotMachining.List.Single(s => s.KhPlanDetailId == d && s.SourceStepId == Nc02).SnapshotId;
        public KhPlanRouteSnapshotMachiningTiming SnapTim(int d) => Db.KhPlanRouteSnapshotMachiningTimings.List.Single(t => t.KhPlanDetailId == d && t.SourceTimingId == TimM1b);
        public KhPlanRouteSnapshotBavia SnapBav(int d) => Db.KhPlanRouteSnapshotBavia.List.Single(s => s.KhPlanDetailId == d && s.SourceStepId == Bav);
        public KhPlanRouteSnapshotMachining SnapM(int d, long step) => Db.KhPlanRouteSnapshotMachining.List.Single(s => s.KhPlanDetailId == d && s.SourceStepId == step);
        public bool? Conf(int d, string table, long row, string field) => Db.KhPlanRouteSnapshotStdConfirms.List.FirstOrDefault(x => x.KhPlanDetailId == d && x.SnapshotTable == table && x.SnapshotRowId == row && x.FieldName == field)?.IsConfirmed;
        public WtsProductionLogStd Std(long log, string field) => Db.WtsProductionLogStds.List.Single(x => x.WtsLogId == log && x.FieldName == field);
    }

    static int _slot;
    static async Task<long> SaveGc(AppDbContext db, int detailId, string nc, string table, long snapRow)
    {
        // [RS-E6-Q 2026-11-02] máy chủ nay kiểm dòng chuẩn đúng NC + đúng máy → nhóm phải chọn đúng máy của dòng chuẩn (NC01 dòng chính M1, dòng đồng dạng M1b)
        var machine = table == StdTrack.Table.MachiningTiming ? "M1b" : "M1";
        var grp = WtsSvcHelp.Build(detailId, "GC", nc, null, 1, 0, Admin, null, null, null, machine);
        //   có máy thì máy chủ kiểm trùng giờ máy → mỗi dòng 1 khoảng 2 phút riêng (lùi dần), không chồng nhau
        var sh = -2 * (++_slot); grp.Rows[0].StartTime = grp.Rows[0].StartTime.AddMinutes(sh); grp.Rows[0].EndTime = grp.Rows[0].EndTime.AddMinutes(sh);
        grp.Rows[0].GcStd = new WtsSaveService.WtsSaveStd { SrcTable = table, SrcRowId = snapRow };   // số chuẩn service đọc lại từ đúng dòng snapshot
        var res = await new WtsSaveService(db, new HandoverLedgerService(db)).SaveGroupAsync(grp);
        db.Fixup();
        if (!res.Ok) throw new Exception("lưu WTS lỗi: " + res.ErrorMsg);
        return grp.Rows[0].WtsLogId!.Value;
    }

    /// <summary>Part P-STD (NC01 M1 + máy đồng dạng M1b, NC02 M2, Taro, Bavia B1, Rửa R1, KCS K1, ĐG P1), NC02 T.Gá đã xác nhận;
    /// 3 PO cùng phiếu KH; A: WTS trước mốc + 2 WTS sau mốc (NC01 + máy đồng dạng), B: 1 WTS sau mốc, C: chưa WTS.
    /// Sau đó Part Master đổi 5 ô: NC01 T.Gá 10→15, M1b T.GC 6→9, NC02 bỏ xác nhận T.Gá, Bavia B1 1→1.2, KCS K1 xác nhận.</summary>
    static async Task<W> BuildAsync()
    {
        var w = new W(); var db = w.Db = new AppDbContext();
        ChuoiHelp.SeedBase(db, exclH04: false);
        var gStd = new UserGroup { GroupId = 97, GroupCode = "STDUPD", GroupName = "Update TG chuẩn (thử)", IsActive = true, Permissions = FuzzPerm.Json(MES.Web.Constants.SystemPermissions.PartStdUpdate) };
        db.UserGroups.List.Add(gStd);
        db.Users.List.Add(new User { UserId = StdUser, Username = "u7", FullName = "U7", IsActive = true, Group = gStd, GroupId = 97 });
        db.Fixup();
        w.PartId = await ChuoiHelp.BuildPartAsync(db, "P-STD", new ChuoiHelp.Shape { Gc = 2, DongDang = true, DuPhong = false, Taro = 1, Bavia = 1, Rua = 1, Kcs = 1, Pkg = 1, TaroTaskCode = true, Prep = false });
        w.Nc01 = db.PartMachiningSteps.List.Single(s => s.PartId == w.PartId && s.NC == "NC01").StepId;
        w.Nc02 = db.PartMachiningSteps.List.Single(s => s.PartId == w.PartId && s.NC == "NC02").StepId;
        w.TimM1b = db.PartMachiningTimings.List.Single(t => t.StepId == w.Nc01 && t.IsActive).TimingId;
        w.Bav = db.PartBaviaSteps.List.Single(s => s.PartId == w.PartId).StepId;
        w.Kcs = db.PartInspectionSteps.List.Single(s => s.PartId == w.PartId).StepId;
        var mach = new PartMachiningService(db);
        await mach.ToggleTimingConfirmAsync(w.Nc02, null, StdTrack.Field.Setup, Admin);   // NC02 T.Gá xác nhận → snapshot chép ✓
        var ids = await ChuoiHelp.CreateKhAsync(db, w.PartId, "P-STD", new decimal[] { 20, 20, 20 }, "PO-S");
        (w.A, w.B, w.C) = (ids[0], ids[1], ids[2]);
        var kho = new KhoVatLieuService(db, new UserService(null)); var hs = new HandoverService(db);
        foreach (var d in new[] { w.A, w.B })
        {
            var kr = await kho.SaveKhoInputAsync(d, DateTime.Now, 20, "OVL", "A1", null, null, null, "nhan phoi", Admin);
            if (!kr.Success) throw new Exception("Kho: " + kr.Error);
            var gi = await hs.IssueByCellAsync(d, "KHO", "GC", 20, Admin, "MOI", false, "MOI", null, "NC01");
            if (!gi.Ok) throw new Exception("giao: " + gi.Error);
            var rr = await hs.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 20, 0, null, Admin);
            if (!rr.Ok) throw new Exception("nhận: " + rr.Error);
        }
        w.WBefore = await SaveGc(db, w.A, "NC01", StdTrack.Table.Machining, w.SnapNc01(w.A));
        await Task.Delay(30); w.Moc = DateTime.Now; await Task.Delay(30);
        w.WAfter  = await SaveGc(db, w.A, "NC01", StdTrack.Table.Machining, w.SnapNc01(w.A));
        w.WTiming = await SaveGc(db, w.A, "NC01", StdTrack.Table.MachiningTiming, w.SnapTim(w.A).TimingSnapshotId);
        w.WB      = await SaveGc(db, w.B, "NC01", StdTrack.Table.Machining, w.SnapNc01(w.B));
        // ── sửa Part Master sau khi PO chạy ──
        var s1 = db.PartMachiningSteps.List.Single(s => s.StepId == w.Nc01);
        await mach.UpdateAsync(w.Nc01, new PartMachiningStep { NC = s1.NC, Drawing = s1.Drawing, MachineRegistered = s1.MachineRegistered, FixtureType = s1.FixtureType, ToolType = s1.ToolType, TimingMachine = s1.TimingMachine, SetupTime = 15, MachiningTime = s1.MachiningTime, InspectionTime = s1.InspectionTime, PreparationTime = s1.PreparationTime, TrialRunTime = s1.TrialRunTime }, Admin, R);
        var tm = db.PartMachiningTimings.List.Single(t => t.TimingId == w.TimM1b); tm.MachiningTime = 9; await db.SaveChangesAsync();   // máy đồng dạng: trang ghi thẳng Db
        await mach.ToggleTimingConfirmAsync(w.Nc02, null, StdTrack.Field.Setup, Admin);   // bỏ xác nhận
        var bv = db.PartBaviaSteps.List.Single(s => s.StepId == w.Bav);
        await new PartBaviaService(db).UpdateAsync(w.Bav, new PartBaviaStep { NC = bv.NC, StepName = bv.StepName, StandardTime = 1.2m }, Admin, R);
        await new PartStepStdTimeConfirmService(db).SetConfirmAsync("Inspection", w.Kcs, w.PartId, true, Admin);
        db.Fixup();
        return w;
    }

    static string Sig(AppDbContext db, int detailId)
        => string.Join(";", db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.SnapshotId).Select(s => $"{s.NC}:{s.SetupTime}/{s.MachiningTime}"))
         + "|" + string.Join(";", db.KhPlanRouteSnapshotMachiningTimings.List.Where(t => t.KhPlanDetailId == detailId).OrderBy(t => t.TimingSnapshotId).Select(t => $"{t.SoMay}:{t.SetupTime}/{t.MachiningTime}"))
         + "|" + string.Join(";", db.KhPlanRouteSnapshotBavia.List.Where(s => s.KhPlanDetailId == detailId).Select(s => $"{s.NC}:{s.StandardTime}"))
         + "|" + string.Join(";", db.KhPlanRouteSnapshotStdConfirms.List.Where(x => x.KhPlanDetailId == detailId).OrderBy(x => x.SnapshotTable).ThenBy(x => x.SnapshotRowId).ThenBy(x => x.FieldName).Select(x => $"{x.SnapshotTable}.{x.SnapshotRowId}.{x.FieldName}={(x.IsConfirmed ? 1 : 0)}"));
    static string WtsSig(AppDbContext db) => string.Join(";", db.WtsProductionLogStds.List.OrderBy(x => x.WtsLogId).ThenBy(x => x.FieldName).Select(x => $"{x.WtsLogId}.{x.FieldName}={x.StdValue}/{(x.IsConfirmed ? 1 : 0)}/{x.SyncSource}"));

    public static async Task<int> Run()
    {
        Console.WriteLine("[S1] Xem trước: phân loại PO (đang dở dang / chưa có WTS), 5 ô snapshot mỗi PO, WTS từ mốc — không ghi DB");
        {
            var w = await BuildAsync(); var db = w.Db; var svc = new StdUpdateService(db, new KhPlanService(db));
            var sig0 = Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db);
            var pv = await svc.PreviewAsync(w.PartId, w.Moc);
            var pa = pv.Plans.Single(p => p.KhPlanDetailId == w.A); var pb = pv.Plans.Single(p => p.KhPlanDetailId == w.B); var pc = pv.Plans.Single(p => p.KhPlanDetailId == w.C);
            Ok(pv.Plans.Count == 3 && pa.HasWts && pa.WtsTotal == 3 && pb.HasWts && pb.WtsTotal == 1 && !pc.HasWts && pc.WtsTotal == 0,
               "[STD-UPDATE-V2] S1a PO A (3 WTS) + B (1 WTS) = đang dở dang, PO C = chưa có WTS",
               string.Join(" · ", pv.Plans.Select(p => $"{p.PurchaseOrder} HasWts={p.HasWts} tổng={p.WtsTotal}")));
            var snap = pv.Changes.Where(c => c.Target == StdTrack.Target.Snapshot).ToList();
            string Desc(StdUpdateService.CellChange c) => $"{c.PurchaseOrder}:{c.SnapshotTable}.{c.FieldName} {c.OldValue}>{c.NewValue} {c.OldIsConfirmed}>{c.NewIsConfirmed}";
            bool Has(int d, string table, string field, decimal? o, decimal? n, bool? oc, bool nc) => snap.Any(c => c.KhPlanDetailId == d && c.SnapshotTable == table && c.FieldName == field && c.OldValue == o && c.NewValue == n && c.OldIsConfirmed == oc && c.NewIsConfirmed == nc);
            bool five(int d) => snap.Count(c => c.KhPlanDetailId == d) == 5
                && Has(d, StdTrack.Table.Machining, StdTrack.Field.Setup, 10, 15, false, false)
                && Has(d, StdTrack.Table.MachiningTiming, StdTrack.Field.Machining, 6, 9, false, false)
                && Has(d, StdTrack.Table.Machining, StdTrack.Field.Setup, 11, 11, true, false)
                && Has(d, StdTrack.Table.Bavia, StdTrack.Field.Standard, 1, 1.2m, false, false)
                && Has(d, StdTrack.Table.Inspection, StdTrack.Field.Standard, 2, 2, false, true);
            Ok(five(w.A) && five(w.B) && five(w.C), "[STD-UPDATE] S1b mỗi PO đúng 5 ô snapshot đổi: NC01 T.Gá 10→15, M1b T.GC 6→9, NC02 T.Gá ✓→○, Bavia 1→1.2, KCS ○→✓",
               string.Join("\n", snap.Select(Desc)));
            var wts = pv.Changes.Where(c => c.Target == StdTrack.Target.WtsLog).ToList();
            Ok(wts.Count == 3 && wts.Any(c => c.WtsLogId == w.WAfter && c.FieldName == StdTrack.Field.Setup && c.OldValue == 10 && c.NewValue == 15)
               && wts.Any(c => c.WtsLogId == w.WTiming && c.FieldName == StdTrack.Field.Machining && c.OldValue == 6 && c.NewValue == 9)
               && wts.Any(c => c.WtsLogId == w.WB && c.FieldName == StdTrack.Field.Setup) && !wts.Any(c => c.WtsLogId == w.WBefore),
               "[STD-UPDATE-V2] S1c WTS lưu từ mốc đổi số chuẩn theo đúng dòng snapshot (NC01 + máy đồng dạng), WTS trước mốc không có trong danh sách",
               string.Join("\n", wts.Select(c => $"WTS#{c.WtsLogId} {c.FieldName} {c.OldValue}>{c.NewValue}")));
            Ok(pa.WtsLogCount == 2 && pb.WtsLogCount == 1 && pc.WtsLogCount == 0, "S1d đếm WTS từ mốc theo PO: A 2 · B 1 · C 0", $"{pa.WtsLogCount} {pb.WtsLogCount} {pc.WtsLogCount}");
            Ok(Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db) == sig0 && db.StdUpdateBatches.List.Count == 0, "S1e Xem trước không ghi gì (snapshot, số chuẩn WTS, lịch sử y nguyên)");
        }

        Console.WriteLine("[S2] Chạy Update cho PO A + C (bỏ tích B): chỉ PO chọn đổi, WTS trước mốc giữ nguyên, lịch sử + lời báo đếm theo PO chọn, khoá PO chọn");
        {
            var w = await BuildAsync(); var db = w.Db; var svc = new StdUpdateService(db, new KhPlanService(db));
            var pv = await svc.PreviewAsync(w.PartId, w.Moc);
            var sel = new List<int> { w.C, w.A };
            var fp = StdUpdateService.SnapshotFingerprint(pv, sel);
            var sigB = Sig(db, w.B); var stdB = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WB).Select(x => $"{x.FieldName}={x.StdValue}").OrderBy(x => x).ToList();
            var before0 = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WBefore).Select(x => $"{x.FieldName}={x.StdValue}/{x.IsConfirmed}/{x.SyncSource}").OrderBy(x => x).ToList();
            var locks = new List<string>();
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => locks.Add(n);
            StdUpdateService.RunResult? rr = null; string? err;
            try { err = await Throws(async () => rr = await svc.RunAsync(w.PartId, w.Moc, "sua TG chuan", Admin, sel, fp)); }
            finally { Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null; }
            db.Fixup();
            Ok(err == null && rr != null, "S2a RunAsync chạy được (ADMIN, có lý do, dấu Xem trước khớp)", err);
            if (rr == null) { Console.WriteLine("KẾT QUẢ: dừng sớm (std)"); return 1; }
            Ok(locks.Where(l => l.StartsWith("MES_PO_")).SequenceEqual(new[] { "MES_PO_" + Math.Min(w.A, w.C), "MES_PO_" + Math.Max(w.A, w.C) }),
               "[STD-UPD] S2b khoá MES_PO_ đúng các PO được chọn, id tăng dần, không khoá PO B", string.Join(",", locks));
            foreach (var d in new[] { w.A, w.C })
            {
                var t = w.SnapTim(d);
                Ok(w.SnapM(d, w.Nc01).SetupTime == 15 && t.MachiningTime == 9 && w.SnapBav(d).StandardTime == 1.2m
                   && w.Conf(d, StdTrack.Table.Machining, w.SnapNc02(d), StdTrack.Field.Setup) == false
                   && w.Conf(d, StdTrack.Table.Inspection, db.KhPlanRouteSnapshotInspection.List.Single(s => s.KhPlanDetailId == d).SnapshotId, StdTrack.Field.Standard) == true,
                   $"[STD-UPDATE] S2c PO {(d == w.A ? "A" : "C")} (được chọn): snapshot = Part Master mới (NC01 15, M1b 9, Bavia 1.2, NC02 bỏ ✓, KCS ✓)", Sig(db, d));
            }
            Ok(Sig(db, w.B) == sigB && db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WB).Select(x => $"{x.FieldName}={x.StdValue}").OrderBy(x => x).SequenceEqual(stdB),
               "[STD-UPDATE-V3] S2d PO B (bỏ tích): snapshot + số chuẩn WTS y nguyên", Sig(db, w.B));
            var sA = w.Std(w.WAfter, StdTrack.Field.Setup); var sT = w.Std(w.WTiming, StdTrack.Field.Machining);
            Ok(sA.StdValue == 15 && sA.SyncSource == StdTrack.Source.StdUpdate && sA.SyncBatchId == rr.BatchId && sT.StdValue == 9 && sT.SyncSource == StdTrack.Source.StdUpdate,
               "[STD-UPDATE] S2e WTS PO A lưu từ mốc: số chuẩn đóng băng ghi đè (NC01 T.Gá 15, máy đồng dạng T.GC 9), nguồn StdUpdate + batch", $"{sA.StdValue}/{sA.SyncSource} {sT.StdValue}/{sT.SyncSource}");
            Ok(db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WBefore).Select(x => $"{x.FieldName}={x.StdValue}/{x.IsConfirmed}/{x.SyncSource}").OrderBy(x => x).SequenceEqual(before0),
               "[STD-UPDATE-V2] S2f WTS PO A lưu TRƯỚC mốc: số chuẩn + xác nhận giữ nguyên (T.Gá vẫn 10)", string.Join(",", before0));
            var bt = db.StdUpdateBatches.List.Single();
            Ok(bt.PlanCount == 2 && bt.SnapshotCellChanged == 10 && bt.WtsCellChanged == 2 && bt.WtsLogCount == 2 && bt.Reason == "sua TG chuan" && bt.RunBy == Admin
               && rr.Applied.Plans.Count == 2 && rr.Applied.SnapshotCellChanged == 10 && rr.Applied.WtsCellChanged == 2,
               "[STD-UPD] S2g lịch sử + lời báo đếm theo PO chọn: 2 phiếu · 10 ô snapshot · 2 ô WTS trên 2 dòng WTS (không tính PO B)",
               $"batch {bt.PlanCount}/{bt.SnapshotCellChanged}/{bt.WtsCellChanged}/{bt.WtsLogCount} · applied {rr.Applied.Plans.Count}/{rr.Applied.SnapshotCellChanged}/{rr.Applied.WtsCellChanged}");
            var det = db.StdUpdateBatchDetails.List.Where(x => x.BatchId == bt.BatchId).ToList();
            Ok(det.Count == 12 && det.All(x => x.KhPlanDetailId != w.B) && det.Any(x => x.Target == StdTrack.Target.WtsLog && x.WtsLogId == w.WAfter && x.OldValue == 10 && x.NewValue == 15),
               "[STD-UPDATE] S2h lịch sử chi tiết: 12 ô (10 snapshot + 2 WTS), cũ → mới, không có PO B", $"{det.Count}");
            var pv2 = await svc.PreviewAsync(w.PartId, w.Moc);
            Ok(!pv2.Changes.Any(c => c.KhPlanDetailId != w.B) && pv2.Changes.Count(c => c.KhPlanDetailId == w.B && c.Target == StdTrack.Target.Snapshot) == 5,
               "S2i Xem trước lại: PO A, C đã khớp Part Master (không còn ô đổi), PO B vẫn còn 5 ô", string.Join(",", pv2.Changes.Select(c => c.PurchaseOrder + "." + c.FieldName)));
        }

        Console.WriteLine("[S3] Máy chủ chặn: thiếu lý do / không có quyền / chưa chọn PO — không ghi gì");
        {
            var w = await BuildAsync(); var db = w.Db; var svc = new StdUpdateService(db, new KhPlanService(db));
            var pv = await svc.PreviewAsync(w.PartId, w.Moc); var all = new List<int> { w.A, w.B, w.C };
            var fp = StdUpdateService.SnapshotFingerprint(pv, all);
            var sig0 = Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db);
            var e1 = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "  ", Admin, all, fp));
            Ok(e1 != null && e1.Contains("lý do"), "[STD-UPDATE] S3a lý do trống → chặn \"Bắt buộc nhập lý do\"", e1);
            var e2 = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu", Xuong, all, fp));
            Ok(e2 != null && e2.Contains("Update TG chuẩn") && db.StdUpdateBatches.List.Count == 0,
               "[STD-UPD] S3b người không có quyền PART_STD_UPDATE (nhóm Xưởng) gọi thẳng RunAsync → máy chủ chặn, không ghi lịch sử", e2);
            var e3 = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu", Admin, new List<int>(), StdUpdateService.SnapshotFingerprint(pv, new List<int>())));
            Ok(e3 != null && e3.Contains("Không có ô nào"), "[STD-UPDATE-V3] S3c không chọn PO nào → \"Không có ô nào để Update\"", e3);
            Ok(Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db) == sig0 && db.StdUpdateBatches.List.Count == 0 && db.StdUpdateBatchDetails.List.Count == 0,
               "S3d sau 3 lần bị chặn: snapshot, số chuẩn WTS, lịch sử y nguyên");
            string? e4 = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu u7", StdUser, new List<int> { w.C }, StdUpdateService.SnapshotFingerprint(pv, new List<int> { w.C })));
            Ok(e4 == null && db.StdUpdateBatches.List.Count == 1 && db.StdUpdateBatches.List[0].RunBy == StdUser && w.SnapM(w.C, w.Nc01).SetupTime == 15,
               "[STD-UPD] S3e người KHÔNG phải ADMIN nhưng có quyền PART_STD_UPDATE (ma trận DB) → chạy được", e4);
        }

        Console.WriteLine("[S4] [STD-5] Part Master đổi sau lúc Xem trước → Chạy bị dừng, không áp số chưa ai xem; Xem trước lại thì chạy được");
        {
            var w = await BuildAsync(); var db = w.Db; var svc = new StdUpdateService(db, new KhPlanService(db));
            var sel = new List<int> { w.A, w.B, w.C };
            var pv = await svc.PreviewAsync(w.PartId, w.Moc); var fp = StdUpdateService.SnapshotFingerprint(pv, sel);
            // người khác sửa Part Master giữa lúc Xem trước và lúc Chạy: NC01 T.Gá 15 → 16
            var s1 = db.PartMachiningSteps.List.Single(s => s.StepId == w.Nc01);
            await new PartMachiningService(db).UpdateAsync(w.Nc01, new PartMachiningStep { NC = s1.NC, Drawing = s1.Drawing, MachineRegistered = s1.MachineRegistered, FixtureType = s1.FixtureType, ToolType = s1.ToolType, TimingMachine = s1.TimingMachine, SetupTime = 16, MachiningTime = s1.MachiningTime, InspectionTime = s1.InspectionTime, PreparationTime = s1.PreparationTime, TrialRunTime = s1.TrialRunTime }, Admin, R);
            db.Fixup();
            var sig0 = Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db);
            var e = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu", Admin, sel, fp));
            db.Fixup();
            Ok(e != null && e.Contains("Xem trước lại"), "S4a Chạy với dấu bản Xem trước cũ → \"…vừa thay đổi sau lúc bấm Xem trước… Bấm Xem trước lại\"", e);
            Ok(Sig(db, w.A) + Sig(db, w.B) + Sig(db, w.C) + WtsSig(db) == sig0 && db.StdUpdateBatches.List.Count == 0, "S4b bị dừng: snapshot, WTS, lịch sử không đổi (rollback)");
            var pv2 = await svc.PreviewAsync(w.PartId, w.Moc);
            var e2 = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu", Admin, sel, StdUpdateService.SnapshotFingerprint(pv2, sel)));
            db.Fixup();
            Ok(e2 == null && w.SnapM(w.A, w.Nc01).SetupTime == 16 && w.Std(w.WAfter, StdTrack.Field.Setup).StdValue == 16, "S4c Xem trước lại rồi Chạy: áp số mới nhất 16 (snapshot + WTS từ mốc)", e2);
            var pv3 = await svc.PreviewAsync(w.PartId, w.Moc);
            Ok(StdUpdateService.SnapshotFingerprint(pv3, sel) == "" && !pv3.HasChanges, "S4d sau khi chạy: Xem trước không còn ô nào đổi (dấu rỗng)");
        }

        Console.WriteLine("[S5] [STD-UPDATE] khác cấu trúc (NC mới / máy đồng dạng mới ở Part Master) chỉ cảnh báo \"cần Làm mới quy trình\", không thêm dòng snapshot");
        {
            var w = await BuildAsync(); var db = w.Db; var svc = new StdUpdateService(db, new KhPlanService(db));
            await new PartMachiningService(db).AddAsync(w.PartId, new PartMachiningStep { NC = "NC09", MachineRegistered = "M9", SetupTime = 1, MachiningTime = 1 }, Admin, R);
            await ChuoiHelp.AddTimingLikePage(db, w.Nc02, "M2x", 1);
            db.Fixup();
            var nSnap = db.KhPlanRouteSnapshotMachining.List.Count; var nTim = db.KhPlanRouteSnapshotMachiningTimings.List.Count;
            var pv = await svc.PreviewAsync(w.PartId, w.Moc);
            var wa = pv.Plans.Single(p => p.KhPlanDetailId == w.A).Warnings;
            Ok(wa.Any(x => x.Contains("NC09") && x.Contains("Làm mới quy trình")) && wa.Any(x => x.Contains("M2x") && x.Contains("Làm mới quy trình")),
               "S5a Xem trước: cảnh báo NC09 + máy đồng dạng M2x \"mới thêm ở Part Master — cần Làm mới quy trình\"", string.Join("\n", wa));
            var sel = new List<int> { w.A };
            var e = await Throws(() => svc.RunAsync(w.PartId, w.Moc, "thu", Admin, sel, StdUpdateService.SnapshotFingerprint(pv, sel)));
            db.Fixup();
            Ok(e == null && db.KhPlanRouteSnapshotMachining.List.Count == nSnap && db.KhPlanRouteSnapshotMachiningTimings.List.Count == nTim && w.SnapM(w.A, w.Nc01).SetupTime == 15,
               "S5b Chạy: đổi số các dòng đã có, KHÔNG thêm dòng snapshot nào", e);
        }

        Console.WriteLine($"\nKẾT QUẢ: " + (fails == 0 ? "ĐẠT hết" : $"{fails} SAI"));   // chuỗi chuẩn chung — Nang.cs tìm đúng "KẾT QUẢ: ĐẠT hết"
        return fails == 0 ? 0 : 1;
    }
}
