// [WTS-SVC 2026-10-29] Kịch bản cố định `wts`: gọi HÀM THẬT Services/WtsSaveService (phần lưu / sửa / xoá WTS tách từ trang
//   HieuSuat_Index) — từng lời báo phải Y NGUYÊN chữ bản cũ (chép từ SaveGroupAsync / CheckGroupQtyAsync / SaveRowAsync /
//   CheckMachineOverlapAsync / VoidSavedRow của trang trước khi tách), số ghi DB đúng, 1 giao dịch (lỗi 1 dòng → không dòng nào ghi).
//   [RS-E14] thêm: mã WTS ngoài snapshot quy trình → chặn.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class WtsScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static void Eq(string? got, string expect, string name) => Ok(got == expect, name, $"có:  {got}\n           cần: {expect}");
    static void Starts(string? got, string expect, string name) => Ok(got != null && got.StartsWith(expect), name, $"có:  {got}\n           cần: {expect}…");

    static readonly DateTime T = DateTime.Today.AddHours(8);

    /// <summary>PO1 40 chiếc Workflow ON: NC1 (M1, số chuẩn + máy đồng dạng M3) → NC2 (M2); Taro T1 / 1T, Bavia B1, Rửa R1, KCS K1, PKG P1.
    /// Kho đã giao GC 40 (nhận vào NC1). PO2 Workflow OFF 20 chiếc, cùng quy trình, GC đã nhận 20.</summary>
    static async Task<AppDbContext> SeedAsync()
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : null, GroupId = u == 1 ? 1 : null });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-WTS", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        var plan2 = new KhPlan { KhPlanId = 2, PlanNo = "KH-OFF", Customer = cust, CustomerId = 1, ApplyWorkflow = false };
        db.KhPlans.List.Add(plan); db.KhPlans.List.Add(plan2);
        var d1 = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, Unit = "EA", LineNo = 1 };
        var d2 = new KhPlanDetail { KhPlanDetailId = 2, KhPlanId = 2, KhPlan = plan2, PartNo = "P", PurchaseOrder = "PO2", Quantity = 20, Unit = "EA", LineNo = 1 };
        db.KhPlanDetails.List.Add(d1); plan.Details.Add(d1); db.KhPlanDetails.List.Add(d2); plan2.Details.Add(d2);
        int sid = 0, tid = 0;
        foreach (var (id, q) in new[] { (1, 40), (2, 20) })
        {
            db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = id, PlanDetailId = id, IsActive = true, SoLuongThucNhan = q, NgayNhanPhoi = T, OrderVatLieu = "OVL", ViTriDePhoi = "A1", SoPO = "PO" + id, PartNo = "P" });
            var m1 = new KhPlanRouteSnapshotMachining { SnapshotId = ++sid, KhPlanDetailId = id, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 };
            m1.TimingRows.Add(new KhPlanRouteSnapshotMachiningTiming { TimingSnapshotId = ++tid, MachiningSnapshotId = m1.SnapshotId, KhPlanDetailId = id, SoMay = "M3", SetupTime = 6, MachiningTime = 2.5m, InspectionTime = 1, PreparationTime = 12, TrialRunTime = 4 });
            db.KhPlanRouteSnapshotMachining.List.Add(m1);
            db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = ++sid, KhPlanDetailId = id, StepOrder = 2, NC = "NC2", MachineRegistered = "M2", SetupTime = 4, MachiningTime = 3, InspectionTime = 1 });
            db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = id * 10 + 1, KhPlanDetailId = id, StepOrder = 1, NC = "T1", WtsTaskCode = "T1", StandardTime = 1.5m });
            db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = id * 10 + 2, KhPlanDetailId = id, StepOrder = 2, NC = "1T", WtsTaskCode = "1T", StandardTime = 15 });
            db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "B1", StandardTime = 0.5m });
            db.KhPlanRouteSnapshotWashing.List.Add(new KhPlanRouteSnapshotWashing { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "R1", StandardTime = 0.2m });
            db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "K1", StandardTime = 1 });
            db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "P1", StandardTime = 0.3m });
        }
        // xác nhận: NC2 PO1 SetupTime đã xác nhận; Taro T1 PO1 đã xác nhận
        db.KhPlanRouteSnapshotStdConfirms.List.Add(new KhPlanRouteSnapshotStdConfirm { Id = 1, KhPlanDetailId = 1, SnapshotTable = StdTrack.Table.Machining, SnapshotRowId = 2, FieldName = StdTrack.Field.Setup, IsConfirmed = true, ConfirmedBy = 1, ConfirmedByName = "U1", ConfirmedAt = T });
        db.KhPlanRouteSnapshotStdConfirms.List.Add(new KhPlanRouteSnapshotStdConfirm { Id = 2, KhPlanDetailId = 1, SnapshotTable = StdTrack.Table.Taro, SnapshotRowId = 11, FieldName = StdTrack.Field.Standard, IsConfirmed = true, ConfirmedBy = 1, ConfirmedByName = "U1", ConfirmedAt = T });
        VuHelp.SeedCats(db);
        db.Fixup();
        var svc = new HandoverService(db);
        foreach (var (id, q) in new[] { (1, 40m), (2, 20m) })
        {
            var r = await svc.IssueByCellAsync(id, "KHO", "GC", q, 1, "MOI", false, "MOI", null, "NC1");
            if (!r.Ok) throw new Exception("seed giao: " + r.Error);
            var rr = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, q, 0, null, 2);
            if (!rr.Ok) throw new Exception("seed nhận: " + rr.Error);
        }
        db.ChangeTracker.Clear();
        return db;
    }

    static WtsSaveService Svc(AppDbContext db) => new(db, new HandoverLedgerService(db));

    static WtsSaveService.WtsSaveGroup Gc(string? nc = "NC1", string? machine = "M1", string mode = "serial")
        => new() { IsGc = true, IsHtsp = false, HandoverGroupCode = "GC", NC = nc, MachineUsed = machine, ProductionMode = mode, UserId = 2, CaId = 1, WorkDate = DateTime.Today };
    static WtsSaveService.WtsSaveGroup NonGc(string group, string sub = "")
        => new() { IsGc = false, IsHtsp = group == "HTSP", HandoverGroupCode = group, HtspSubGroup = sub, ProductionMode = "serial", UserId = 3, CaId = 1, WorkDate = DateTime.Today };

    static WtsSaveService.WtsSaveRow Row(int? detailId, decimal ok, decimal ng, DateTime start, DateTime end, string? code = null, int? cat = null,
                                         decimal? setup = 5, decimal? mach = 2, decimal? insp = 1, decimal? prep = null, decimal? trial = null, decimal? actualPc = null, string mode = "serial")
        => new()
        {
            KhPlanDetailId = detailId, PO = detailId == null ? null : "PO" + detailId, StartTime = start, EndTime = end, WtsCode = code,
            QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, NgCategoryId = cat, ProductionMode = mode,
            SetupTime_Actual = setup, MachiningTime_Actual = mach, InspectionTime_Actual = insp, PreparationTime_Actual = prep, TrialRunTime_Actual = trial,
            ActualTimePerPc = actualPc,
        };

    static int WtsCount(AppDbContext db, bool active = true) => db.WtsProductionLogs.List.Count(w => !active || !w.IsVoided);

    public static async Task<int> Run()
    {
        Console.WriteLine("[W1] Thiếu thông tin nhóm / dòng — chữ y cũ, chưa ghi gì");
        {
            var db = await SeedAsync(); var s = Svc(db); int n0 = WtsCount(db, false);
            var r = await s.SaveGroupAsync(Gc(nc: null)); Eq(r.ErrorMsg, "Chưa chọn NC cho nhóm.", "GC chưa chọn NC");
            r = await s.SaveGroupAsync(Gc(machine: null)); Eq(r.ErrorMsg, "Chưa chọn máy sử dụng.", "GC chưa chọn máy");
            r = await s.SaveGroupAsync(NonGc("HTSP", "")); Eq(r.ErrorMsg, "Chưa chọn nhóm nhỏ (Taro / Bavia / Rửa).", "HTSP chưa chọn nhóm nhỏ");
            r = await s.SaveGroupAsync(Gc()); Eq(r.ErrorMsg, "Nhóm trống.", "Nhóm trống");
            var g = Gc(); g.Rows.Add(Row(null, 5, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng chưa chọn KH.", "Chưa chọn số kế hoạch"); Eq(g.Rows[0].ErrorMsg, "Chưa chọn số kế hoạch.", "  lời báo dòng");
            g = Gc(); g.Rows.Add(Row(1, 0, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng SL = 0.", "SL = 0 (Hàng loạt)"); Eq(g.Rows[0].ErrorMsg, "SL (OK+NG) phải > 0.", "  lời báo dòng");
            g = Gc(); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1), setup: 0, insp: null));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng thiếu thời gian — Hàng loạt: chưa nhập T.Gá, T.Ktra.", "Hàng loạt thiếu T.Gá / T.Ktra");
            g = Gc(mode: "trial"); g.Rows.Add(Row(1, 0, 0, T, T.AddHours(1), mode: "trial", setup: null, mach: null, insp: null, prep: 0));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng thiếu thời gian — Chạy thử: chưa nhập T.CB (thời gian chuẩn bị).", "Chạy thử thiếu T.CB");
            g = NonGc("KCS"); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1), code: "K1", setup: null, mach: null, insp: null, actualPc: null));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng chưa nhập T.Thực tế.", "Non-GC thiếu T.Thực tế"); Eq(g.Rows[0].ErrorMsg, "Chưa nhập T.Thực tế.", "  lời báo dòng");
            g = NonGc("HTSP", "Taro"); g.Rows.Add(Row(1, 0, 0, T, T.AddHours(1), code: "1T", setup: null, mach: null, insp: null, actualPc: null));
            r = await s.SaveGroupAsync(g); Eq(g.Rows[0].ErrorMsg, "Chưa nhập T.Thực tế (tổng phút chuẩn bị).", "Mã chuẩn bị thiếu T.Thực tế");
            g = Gc(); g.Rows.Add(Row(1, 4, 1, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Có hàng NG chưa chọn loại lỗi. " + NgCaseService.MsgPickCategory, "NG chưa chọn loại lỗi"); Eq(g.Rows[0].ErrorMsg, "Chọn loại lỗi (DS Lỗi).", "  lời báo dòng");
            g = Gc(); g.Rows.Add(Row(1, 4, 1, T, T.AddHours(1), cat: 6));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Loại lỗi đã ngưng dùng — chọn loại khác.", "Loại lỗi đã ngưng");
            g = Gc(); g.Rows.Add(Row(1, 5, 0, T.AddHours(1), T));
            r = await s.SaveGroupAsync(g); Eq(r.ErrorMsg, "Lỗi thời gian.", "Kết thúc trước bắt đầu"); Eq(g.Rows[0].ErrorMsg, "T.Kết thúc phải sau T.Bắt đầu.", "  lời báo dòng");
            Ok(WtsCount(db, false) == n0 && db.NgCases.List.Count == 0 && db.WtsProductionLogStds.List.Count == 0, "Không dòng nào được ghi, không vụ lỗi nào sinh ra");
        }

        Console.WriteLine("[W2] Lưu GC Hàng loạt + NG → dòng, vụ lỗi, 5 dòng số chuẩn, ô xác nhận");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); var row = Row(1, 8, 2, T, T.AddHours(1), cat: 2);
            row.GcStd = new WtsSaveService.WtsSaveStd { SetupTime = 4, MachiningTime = 3, InspectionTime = 1, SrcTable = StdTrack.Table.Machining, SrcRowId = 2 };   // trang tra sẵn (NC2 PO1 đã xác nhận Setup)
            row.ExtraItems.Add(new WtsSaveService.WtsSaveExtra { WtsCode = "PS1", WtsName = "Phát sinh", Minutes = 5 });
            row.ExtraItems.Add(new WtsSaveService.WtsSaveExtra { WtsCode = "PS2", Minutes = 0 });
            g.Rows.Add(row); row.Notes = "ghi chú"; row.IsOvertime = true;
            var r = await s.SaveGroupAsync(g);
            Ok(r.Ok && r.ErrorMsg == null, "Lưu được", r.ErrorMsg);
            var w = db.WtsProductionLogs.List.Last();
            Ok(row.WtsLogId == w.WtsLogId && row.Saved && !row.IsEditing, "Dòng DTO nhận WtsLogId + Saved");
            Ok(w.ProcessGroup == "GC" && w.NC == "NC1" && w.WtsCode == null && w.MachineUsed == "M1" && w.QtyDone == 10 && w.QtyOk == 8 && w.QtyNg == 2 && w.WorkerId == 2 && w.CaId == 1
               && w.IsOvertime && w.Notes == "ghi chú" && w.ExtraMinutes == 5 && w.ProductionMode == "serial" && w.ActualTimePerPc == null && w.IsActualManual == null
               && w.SetupTime_Actual == 5 && w.MachiningTime_Actual == 2 && w.InspectionTime_Actual == 1 && w.StartTime == T && w.EndTime == T.AddHours(1) && !w.IsVoided,
               "Cột ghi đúng như SaveRowAsync cũ", $"{w.ProcessGroup} {w.NC} {w.MachineUsed} {w.QtyOk}/{w.QtyNg} ot={w.IsOvertime} ps={w.ExtraMinutes} mode={w.ProductionMode}");
            Ok(w.NgCaseId != null && db.NgCases.List.Count == 1 && db.NgCases.List[0].NgCategoryId == 2 && w.NgReason == VuHelp.Label(db, 2) && row.NgCaseId == w.NgCaseId,
               "NG 2 → 1 vụ lỗi mới, NgReason = tên loại lỗi", $"case={w.NgCaseId} reason={w.NgReason}");
            var ex = db.WtsExtraItems.List.Where(e => e.WtsLogId == w.WtsLogId).ToList();
            Ok(ex.Count == 1 && ex[0].WtsCode == "PS1" && ex[0].Minutes == 5, "Phát sinh: chỉ dòng có phút > 0 được ghi");
            var stds = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WtsLogId).ToList();
            var su = stds.FirstOrDefault(x => x.FieldName == StdTrack.Field.Setup);
            Ok(stds.Count == 5 && su != null && su.StdValue == 4 && su.IsConfirmed && su.ConfirmedByName == "U1" && su.SnapshotTable == StdTrack.Table.Machining && su.SnapshotRowId == 2 && su.SyncSource == StdTrack.Source.Save && su.SyncedBy == 2
               && stds.First(x => x.FieldName == StdTrack.Field.Machining).IsConfirmed == false && stds.First(x => x.FieldName == StdTrack.Field.Preparation).StdValue == null,
               "5 dòng số chuẩn GC; ô Setup ĐÃ xác nhận (đọc KhPlanRouteSnapshotStdConfirms), các ô khác chưa", string.Join(",", stds.Select(x => $"{x.FieldName}={x.StdValue}/{x.IsConfirmed}")));
            Ok(r.DetailIds.SequenceEqual(new[] { 1 }), "DetailIds trả PO đã ghi");
            // không có số chuẩn (máy lạ) → 5 dòng null + ghi chú
            g = Gc(machine: "M9"); row = Row(1, 3, 0, T.AddHours(2), T.AddHours(3)); g.Rows.Add(row);
            r = await s.SaveGroupAsync(g);
            var st2 = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == row.WtsLogId).ToList();
            Ok(r.Ok && st2.Count == 5 && st2.All(x => x.StdValue == null && x.Note == "Không tìm thấy chuẩn trong snapshot phiếu KH"), "Không có chuẩn: 5 dòng trống + ghi chú y cũ");
        }

        Console.WriteLine("[W3] GC Chạy thử không SL; mã chuẩn bị non-GC không SL; T.Thực tế khác chuẩn");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(mode: "trial"); var row = Row(1, 0, 0, T, T.AddMinutes(15), mode: "trial", setup: null, mach: null, insp: null, prep: 10, trial: 3); g.Rows.Add(row);
            var r = await s.SaveGroupAsync(g);
            var w = db.WtsProductionLogs.List.Last();
            Ok(r.Ok && w.QtyDone == 0 && w.ProductionMode == "trial" && w.PreparationTime_Actual == 10 && w.TrialRunTime_Actual == 3, "Chạy thử lưu không SL, ghi T.CB / T.Chạy", r.ErrorMsg);
            var h = NonGc("HTSP", "Taro"); var p = Row(1, 0, 0, T.AddHours(1), T.AddHours(1).AddMinutes(15), code: "1T", setup: null, mach: null, insp: null, actualPc: 15); p.StdTimePerPc = 15; h.Rows.Add(p);
            r = await s.SaveGroupAsync(h); w = db.WtsProductionLogs.List.Last();
            Ok(r.Ok && w.ProcessGroup == "TARO" && w.WtsCode == "1T" && w.NC == null && w.HtspSubGroup == "Taro" && w.QtyDone == 0 && w.ActualTimePerPc == 15 && w.IsActualManual == false && w.MachineUsed == null,
               "Mã chuẩn bị 1T: lưu không SL, ProcessGroup TARO, WtsCode = mã, máy NULL, IsActualManual false (bằng chuẩn)", r.ErrorMsg);
            var k = NonGc("KCS"); var q = Row(1, 5, 0, T.AddHours(2), T.AddHours(3), code: "K1", setup: null, mach: null, insp: null, actualPc: 1.2m); q.StdTimePerPc = 1; q.StdSrcTable = StdTrack.Table.Inspection; q.StdSrcRowId = 1; k.Rows.Add(q);
            r = await s.SaveGroupAsync(k); w = db.WtsProductionLogs.List.Last();
            var st = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == w.WtsLogId).ToList();
            Ok(!r.Ok && r.ErrorMsg!.StartsWith("PO PO1 · mã K1: chưa nhận hàng"), "KCS chưa nhận hàng → chặn (trần Locked, chữ y cũ)", r.ErrorMsg);
            Eq(r.ErrorMsg, "PO PO1 · mã K1: chưa nhận hàng — chưa nhập số lượng được. (Nhận vào 0 − OK 0 − NG 0 = 0)", "  nguyên văn");
        }

        Console.WriteLine("[W4] Trần L3 + Workflow OFF + K5 khi sửa — chữ y cũ");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); g.Rows.Add(Row(1, 30, 0, T, T.AddHours(1))); g.Rows.Add(Row(1, 15, 0, T.AddHours(1), T.AddHours(2)));
            var r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "PO PO1 · NC NC1: nhập 45 (OK + NG) vượt Chưa làm 40. (Nhận vào 40 − OK 0 − NG 0 = 40)", "2 dòng cùng NC cộng dồn vượt trần");
            Ok(g.Rows.All(x => x.ErrorMsg == r.ErrorMsg) && WtsCount(db) == 0, "Cả 2 dòng mang lời báo, chưa ghi gì");
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(1, 1, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "PO PO1 · NC NC2: chưa nhận hàng — chưa nhập số lượng được. (Nhận vào 0 − OK 0 − NG 0 = 0)", "NC2 chưa có hàng (NC1 chưa làm) → Locked");
            // Workflow OFF: chỉ chặn dòng ≤ PO
            g = Gc(); g.Rows.Add(Row(2, 25, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "PO PO2: dòng 25 vượt SL PO 20 (Workflow OFF chỉ chặn mỗi dòng ≤ PO).", "Workflow OFF dòng > PO");
            Eq(g.Rows[0].ErrorMsg, "Tổng OK+NG (25) vượt KH (20 EA).", "  lời báo dòng");
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(2, 20, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g); Ok(r.Ok, "Workflow OFF: NC2 chưa có hàng vẫn lưu được (N7)", r.ErrorMsg);
            // K5 khi sửa: NC1 10 → NC2 10 → sửa NC1 còn 4
            g = Gc(); var a = Row(1, 10, 0, T, T.AddHours(1)); g.Rows.Add(a); r = await s.SaveGroupAsync(g); Ok(r.Ok, "NC1 OK 10", r.ErrorMsg);
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(1, 10, 0, T.AddHours(1), T.AddHours(2))); r = await s.SaveGroupAsync(g); Ok(r.Ok, "NC2 OK 10", r.ErrorMsg);
            g = Gc(); var e = Row(1, 4, 0, T, T.AddHours(1)); e.IsEditing = true; e.WtsLogId = a.WtsLogId; g.Rows.Add(e);
            r = await s.SaveGroupAsync(g);
            Starts(r.ErrorMsg, "PO PO1: lưu xong sẽ làm sai số ở Gia công NC2: chưa làm = -6. Kiểm lại số lượng (nhóm sau / NC sau có thể đã dùng số này — K5).", "Sửa NC1 10→4 khi NC2 đã làm 10 → K5 chặn, chữ y cũ");
            Ok(r.ErrorMsg!.Contains("nên kiểm tra theo các bước sau") && r.ErrorMsg.Contains("Kiểm tra WTS #"), "  + gợi ý gỡ [GN-WHY] chạy sau khi nhả khoá");
            var old = db.WtsProductionLogs.List.First(x => x.WtsLogId == a.WtsLogId);
            Ok(!old.IsVoided && e.IsEditing && e.WtsLogId == a.WtsLogId, "Bị chặn → dòng cũ còn nguyên, DTO trả về trạng thái đang sửa");
            // sửa tăng → được, dòng cũ huỷ "Sửa lại"
            g = Gc(); e = Row(1, 12, 0, T, T.AddHours(1)); e.IsEditing = true; e.WtsLogId = a.WtsLogId; g.Rows.Add(e);
            r = await s.SaveGroupAsync(g);
            Ok(r.Ok && old.IsVoided && old.VoidReason == "Sửa lại" && old.VoidedBy == 2 && e.WtsLogId != a.WtsLogId && db.WtsProductionLogs.List.Last().QtyOk == 12, "Sửa 10→12: dòng cũ huỷ 'Sửa lại', dòng mới 12", r.ErrorMsg);
            // [D8] sửa dòng đã bị huỷ ở nơi khác
            g = Gc(); e = Row(1, 12, 0, T.AddHours(5), T.AddHours(6)); e.IsEditing = true; e.WtsLogId = a.WtsLogId; g.Rows.Add(e);
            int n = WtsCount(db, false);
            r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "Lỗi hàng KH PO1: Dòng WTS này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại. — chưa dòng nào trong nhóm được lưu, sửa xong bấm Lưu lại.", "[D8] sửa dòng đã huỷ ở tab khác → chặn, chữ y cũ");
            Ok(WtsCount(db, false) == n, "  không ghi dòng mới");
        }

        Console.WriteLine("[W5] Máy trùng giờ (trong form + với DB) — chữ y cũ");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1))); g.Rows.Add(Row(1, 5, 0, T.AddMinutes(30), T.AddMinutes(90)));
            var r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "Máy M1 trùng giờ giữa các dòng đang nhập (08:00–09:00 và 08:30–09:30).", "Trùng trong form");
            Eq(g.Rows[1].ErrorMsg, "Máy M1: trùng giờ với dòng 08:00–09:00 trong form.", "  lời báo dòng");
            g = Gc(); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1))); r = await s.SaveGroupAsync(g); Ok(r.Ok, "Lưu 08:00–09:00", r.ErrorMsg);
            g = Gc(); g.Rows.Add(Row(1, 5, 0, T.AddMinutes(45), T.AddHours(2))); r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "Máy M1 đã có log 08:00–09:00 (PO PO1, NC NC1, U2) — trùng 08:45–09:00. Không cho lưu.", "Trùng với log đã lưu (số PO đọc từ DB)");
            g = Gc(); g.Rows.Add(Row(1, 5, 0, T.AddHours(1), T.AddHours(2))); r = await s.SaveGroupAsync(g); Ok(r.Ok, "Chạm mép 09:00→09:00 không tính trùng", r.ErrorMsg);
            g = Gc(machine: "M3"); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1))); r = await s.SaveGroupAsync(g); Ok(r.Ok, "Máy khác cùng giờ: được", r.ErrorMsg);
            // sửa dòng: bỏ qua log cũ của chính nó
            var first = db.WtsProductionLogs.List.First(x => x.MachineUsed == "M1");
            g = Gc(); var e = Row(1, 6, 0, T, T.AddHours(1)); e.IsEditing = true; e.WtsLogId = first.WtsLogId; g.Rows.Add(e);
            r = await s.SaveGroupAsync(g); Ok(r.Ok, "Sửa dòng cùng giờ: không tự trùng với chính nó", r.ErrorMsg);
        }

        Console.WriteLine("[W6] 1 giao dịch: dòng 2 lỗi → dòng 1 không ghi, vụ lỗi không sinh, DTO về như trước");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); var a = Row(1, 4, 1, T, T.AddHours(1), cat: 1); var b = Row(1, 5, 0, T.AddHours(1), T.AddHours(2)); b.NC = null; b.WtsCode = null;
            g.Rows.Add(a); g.Rows.Add(b);
            // dòng b: service gán NC = grp.NC nên hợp lệ → làm lỗi bằng cách ngày không phải hôm nay? không — dùng giờ: Minutes ≤ 0 không qua validate nhóm. Dùng loại lỗi ngưng ở dòng 2 → chặn ở validate (trước giao dịch).
            // Lỗi TRONG giao dịch: dòng 2 sửa dòng đã huỷ ([D8])
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 999, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = 2, QtyDone = 1, QtyOk = 1, IsVoided = true, StartTime = T.AddHours(5), EndTime = T.AddHours(6), CreatedAt = T });
            db.Fixup();
            b = Row(1, 5, 0, T.AddHours(1), T.AddHours(2)); b.IsEditing = true; b.WtsLogId = 999; g.Rows[1] = b;
            int n = WtsCount(db, false), nc = db.NgCases.List.Count, ns = db.WtsProductionLogStds.List.Count;
            var r = await s.SaveGroupAsync(g);
            Starts(r.ErrorMsg, "Lỗi hàng KH PO1: Dòng WTS này đã bị sửa / xoá ở nơi khác (tab khác)", "Dòng 2 lỗi trong giao dịch → lời báo nhóm y cũ");
            Ok(WtsCount(db, false) == n && db.NgCases.List.Count == nc && db.WtsProductionLogStds.List.Count == ns, "Rollback: dòng 1 không ghi, vụ lỗi dòng 1 không sinh, số chuẩn không ghi");
            Ok(a.WtsLogId == null && a.NgCaseId == null && b.IsEditing && b.WtsLogId == 999, "DTO trả về trạng thái trước khi bấm Lưu (WtsLogId / NgCaseId / IsEditing như cũ)");
            Ok(r.DetailIds.SequenceEqual(new[] { 1 }), "DetailIds vẫn trả PO đã ghi dở để trang nạp lại trần");
            Ok(db.Database.Current == null && StubTransactionOpen(db) == false, "Giao dịch đã đóng (khoá PO / máy nhả theo giao dịch)");
        }

        Console.WriteLine("[W7] Xoá dòng: K5 chặn + gợi ý; xoá được; ngày không phải hôm nay");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); var a = Row(1, 10, 0, T, T.AddHours(1)); g.Rows.Add(a); await s.SaveGroupAsync(g);
            g = Gc(nc: "NC2", machine: "M2"); var b = Row(1, 10, 0, T.AddHours(1), T.AddHours(2)); g.Rows.Add(b); await s.SaveGroupAsync(g);
            var v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = a.WtsLogId, KhPlanDetailId = 1, UserId = 2 });
            Starts(v.ErrorMsg, "Không xoá được: xoá xong sẽ làm sai số ở Gia công NC2: chưa làm = -10. Số của dòng này đã được dùng ở NC sau / nhóm sau hoặc đã giao đi — phải xoá / sửa từ cuối dòng ngược lên (K5).", "Xoá NC1 khi NC2 đã dùng → chặn, chữ y cũ");
            Ok(!v.Ok && v.ErrorMsg!.Contains("nên kiểm tra theo các bước sau") && !db.WtsProductionLogs.List.First(x => x.WtsLogId == a.WtsLogId).IsVoided, "  + gợi ý [GN-WHY], dòng còn nguyên");
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = b.WtsLogId, KhPlanDetailId = 1, UserId = 2 });
            var wb = db.WtsProductionLogs.List.First(x => x.WtsLogId == b.WtsLogId);
            Ok(v.Ok && wb.IsVoided && wb.VoidReason == "Xóa dòng" && wb.VoidedBy == 2 && v.DetailIds.SequenceEqual(new[] { 1 }), "Xoá NC2: được, cờ huỷ 'Xóa dòng' + người huỷ");
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = b.WtsLogId, KhPlanDetailId = 1, UserId = 3 });
            Ok(v.Ok && wb.VoidedBy == 2, "[D8] xoá lần 2 dòng đã huỷ: không ghi đè người huỷ trước");
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = a.WtsLogId, KhPlanDetailId = 1, UserId = 2 });
            Ok(v.Ok, "Xoá NC1 sau khi NC2 đã xoá: được", v.ErrorMsg);
            // Workflow OFF: xoá không kiểm K5 (N7)
            g = Gc(); var c = Row(2, 10, 0, T.AddHours(3), T.AddHours(4)); g.Rows.Add(c); await s.SaveGroupAsync(g);
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(2, 10, 0, T.AddHours(4), T.AddHours(5))); await s.SaveGroupAsync(g);
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = c.WtsLogId, KhPlanDetailId = 2, UserId = 2 });
            Ok(v.Ok, "Workflow OFF: xoá NC1 dù NC2 đã dùng — không chặn (N7, giữ như cũ)", v.ErrorMsg);
            // ngày cũ
            g = Gc(); g.WorkDate = DateTime.Today.AddDays(-1); g.Rows.Add(Row(1, 1, 0, T.AddHours(6), T.AddHours(7)));
            var r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "Lỗi hàng KH PO1: Chỉ được nhập WTS cho ngày hôm nay. Ngày cũ cần quyền cao hơn. — chưa dòng nào trong nhóm được lưu, sửa xong bấm Lưu lại.", "Ngày không phải hôm nay → chặn, chữ y cũ");
        }

        Console.WriteLine("[W8][RS-E14] Mã WTS ngoài snapshot quy trình → chặn (trước: lưu được, sổ bỏ qua)");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); g.Rows.Add(Row(1, 10, 0, T, T.AddHours(1))); await s.SaveGroupAsync(g);
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(1, 10, 0, T.AddHours(1), T.AddHours(2))); await s.SaveGroupAsync(g);
            var svc = new HandoverService(db);
            var i1 = await svc.IssueByCellAsync(1, "GC", "HTSP", 10, 2, "MOI", false, "MOI"); Ok(i1.Ok, "GC giao HTSP 10", i1.Error);
            var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 3); Ok(r1.Ok, "HTSP nhận 10", r1.Error);
            int n = WtsCount(db, false);
            var h = NonGc("HTSP", "Bavia"); var p = Row(1, 3, 0, T.AddHours(2), T.AddHours(3), code: "XYZ", setup: null, mach: null, insp: null, actualPc: 1); h.Rows.Add(p);
            var r = await s.SaveGroupAsync(h);
            Eq(r.ErrorMsg, "PO PO1 · mã XYZ: không có trong quy trình (snapshot) của PO — không lưu được. Chọn lại mã CV hoặc nhờ Kế hoạch cập nhật quy trình.", "Bavia mã XYZ → chặn [RS-E14]");
            Ok(WtsCount(db, false) == n && p.ErrorMsg == r.ErrorMsg, "  không ghi, dòng mang lời báo");
            var g2 = Gc(nc: "NC9", machine: "M1"); g2.Rows.Add(Row(1, 1, 0, T.AddHours(3), T.AddHours(4)));
            r = await s.SaveGroupAsync(g2);
            Eq(r.ErrorMsg, "PO PO1 · NC NC9: không có trong quy trình (snapshot) của PO — không lưu được. Chọn lại NC hoặc nhờ Kế hoạch cập nhật quy trình.", "GC NC9 → chặn [RS-E14]");
            g2 = Gc(nc: "NC9", machine: "M1"); g2.Rows.Add(Row(2, 1, 0, T.AddHours(3), T.AddHours(4)));
            r = await s.SaveGroupAsync(g2);
            Starts(r.ErrorMsg, "PO PO2 · NC NC9: không có trong quy trình", "Workflow OFF cũng chặn (không phải luật số lượng)");
            h = NonGc("HTSP", "Bavia"); p = Row(1, 3, 0, T.AddHours(2), T.AddHours(3), code: "B1", setup: null, mach: null, insp: null, actualPc: 1); h.Rows.Add(p);
            r = await s.SaveGroupAsync(h); Ok(r.Ok, "Bavia B1 (có trong snapshot) lưu được", r.ErrorMsg);
            h = NonGc("HTSP", "Taro"); p = Row(1, 0, 0, T.AddHours(3), T.AddHours(4), code: "1V", setup: null, mach: null, insp: null, actualPc: 5); h.Rows.Add(p);
            r = await s.SaveGroupAsync(h); Ok(r.Ok, "Mã chuẩn bị 1V không có trong snapshot Taro: vẫn lưu (không có số lượng, như cũ)", r.ErrorMsg);
            // HTSP: PO có Taro/Bavia nhưng không có Rửa → chặn với lời "chưa có quy trình Rửa" (sổ bỏ qua dòng này → vênh)
            db.KhPlanRouteSnapshotWashing.List.RemoveAll(x => x.KhPlanDetailId == 1); db.Fixup();
            h = NonGc("HTSP", "Rua"); p = Row(1, 2, 0, T.AddHours(6), T.AddHours(7), code: "R1", setup: null, mach: null, insp: null, actualPc: 1); h.Rows.Add(p);
            r = await s.SaveGroupAsync(h);
            Eq(r.ErrorMsg, "PO PO1 · mã R1: PO chưa có quy trình Rửa trong snapshot — không lưu được. Nhờ Kế hoạch cập nhật quy trình.", "HTSP nhóm nhỏ chưa có snapshot → chặn, lời rõ nghĩa");
            db.KhPlanRouteSnapshotWashing.List.Add(new KhPlanRouteSnapshotWashing { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "R1", StandardTime = 0.2m }); db.Fixup();
            // GC Chạy thử (không SL) NC lạ: vẫn chặn — mã không có trong quy trình thì không có chỗ ghi
            g2 = Gc(nc: "NC9", machine: "M1", mode: "trial"); g2.Rows.Add(Row(1, 0, 0, T.AddHours(8), T.AddHours(9), mode: "trial", setup: null, mach: null, insp: null, prep: 5));
            r = await s.SaveGroupAsync(g2); Starts(r.ErrorMsg, "PO PO1 · NC NC9: không có trong quy trình", "GC Chạy thử NC lạ (SL 0) cũng chặn");
            // PO chưa có snapshot cho nhóm: giữ như cũ (cho lưu theo Chưa làm cả nhóm)
            db.KhPlanRouteSnapshotInspection.List.RemoveAll(x => x.KhPlanDetailId == 1); db.Fixup();
            foreach (var (sub, code) in new[] { ("Taro", "T1"), ("Rua", "R1") })
            { h = NonGc("HTSP", sub); h.Rows.Add(Row(1, 3, 0, T.AddHours(4), T.AddHours(5), code: code, setup: null, mach: null, insp: null, actualPc: 1)); r = await s.SaveGroupAsync(h); Ok(r.Ok, $"HTSP {code} làm 3", r.ErrorMsg); }
            var i2 = await svc.IssueByCellAsync(1, "HTSP", "KCS", 3, 3, "MOI", false, "MOI"); var r2 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 3, 0, null, 4);
            Ok(i2.Ok && r2.Ok, "HTSP giao KCS 3 (sau khi B1 làm 3)", i2.Error + r2.Error);
            var k = NonGc("KCS"); k.Rows.Add(Row(1, 2, 0, T.AddHours(5), T.AddHours(6), code: "KX", setup: null, mach: null, insp: null, actualPc: 1));
            r = await s.SaveGroupAsync(k); Ok(r.Ok, "PO chưa có snapshot KCS: mã lạ vẫn lưu theo Chưa làm cả nhóm (giữ như cũ)", r.ErrorMsg);
        }

        Console.WriteLine("[W9] Khoá: máy + PO đều LockOwner = Transaction trong 1 giao dịch (PoGuard)");
        {
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear();
            var db = await SeedAsync(); var s = Svc(db);
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear();
            var g = Gc(); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1))); var r = await s.SaveGroupAsync(g);
            var locks = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Where(x => x.Contains("sp_getapplock")).ToList();
            Ok(r.Ok && locks.Count == 2 && locks.All(x => x.Contains("'Transaction'")) && !Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Any(x => x.Contains("sp_releaseapplock")),
               "Lưu GC: 2 lần khoá (máy + PO) kiểu Transaction, không có lệnh nhả riêng", string.Join(" | ", locks));
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear();
            var h = NonGc("KCS"); h.Rows.Add(Row(1, 1, 0, T, T.AddHours(1), code: "K1", setup: null, mach: null, insp: null, actualPc: 1)); await s.SaveGroupAsync(h);
            locks = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Where(x => x.Contains("sp_getapplock")).ToList();
            Ok(locks.Count == 1, "Lưu non-GC: chỉ khoá PO (không khoá máy)", string.Join(" | ", locks));
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }

    static bool StubTransactionOpen(AppDbContext db) => db.Database.Current != null;
}
