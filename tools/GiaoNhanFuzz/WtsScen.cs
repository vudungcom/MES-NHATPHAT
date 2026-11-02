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
    internal static async Task<AppDbContext> SeedAsync()
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        db.CaLamViecs.List.Add(new CaLamViec { CaId = 1, TenCa = "HC", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0), IsActive = true });   // [ML17-XN] ca phải có thật (máy chủ đọc giờ nghỉ ca); không giờ nghỉ → kịch bản cũ y như trước
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
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

    internal static WtsSaveService Svc(AppDbContext db) => new(db, new HandoverLedgerService(db));

    internal static WtsSaveService.WtsSaveGroup Gc(string? nc = "NC1", string? machine = "M1", string mode = "serial")
        => new() { IsGc = true, IsHtsp = false, HandoverGroupCode = "GC", NC = nc, MachineUsed = machine, ProductionMode = mode, UserId = 2, CaId = 1, WorkDate = DateTime.Today };
    internal static WtsSaveService.WtsSaveGroup NonGc(string group, string sub = "")
        => new() { IsGc = false, IsHtsp = group == "HTSP", HandoverGroupCode = group, HtspSubGroup = sub, ProductionMode = "serial", UserId = 3, CaId = 1, WorkDate = DateTime.Today };

    internal static WtsSaveService.WtsSaveRow Row(int? detailId, decimal ok, decimal ng, DateTime start, DateTime end, string? code = null, int? cat = null,
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
            // [RS-E6-Q 2026-11-02] trước: nhóm NC1 / M1 mà gửi dòng chuẩn của NC2 (SrcRowId 2) — máy chủ nay KHÔNG nhận chuẩn sai NC / máy
            //   → ca dùng dòng NC1 M1 (SrcRowId 1) + dấu xác nhận Setup trên chính dòng đó (ý ca giữ nguyên: đọc trạng thái xác nhận từ DB).
            db.KhPlanRouteSnapshotStdConfirms.List.Add(new KhPlanRouteSnapshotStdConfirm { Id = 99, KhPlanDetailId = 1, SnapshotTable = StdTrack.Table.Machining, SnapshotRowId = 1, FieldName = StdTrack.Field.Setup, IsConfirmed = true, ConfirmedBy = 1, ConfirmedByName = "U1", ConfirmedAt = T });
            row.GcStd = new WtsSaveService.WtsSaveStd { SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3, SrcTable = StdTrack.Table.Machining, SrcRowId = 1 };   // trang tra sẵn (NC1 M1 PO1, đã xác nhận Setup)
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
            Ok(stds.Count == 5 && su != null && su.StdValue == 5 && su.IsConfirmed && su.ConfirmedByName == "U1" && su.SnapshotTable == StdTrack.Table.Machining && su.SnapshotRowId == 1 && su.SyncSource == StdTrack.Source.Save && su.SyncedBy == 2
               && stds.First(x => x.FieldName == StdTrack.Field.Machining).IsConfirmed == false && stds.First(x => x.FieldName == StdTrack.Field.Preparation).StdValue == 10,
               "5 dòng số chuẩn GC; ô Setup ĐÃ xác nhận (đọc KhPlanRouteSnapshotStdConfirms), các ô khác chưa", string.Join(",", stds.Select(x => $"{x.FieldName}={x.StdValue}/{x.IsConfirmed}")));
            Ok(r.DetailIds.SequenceEqual(new[] { 1 }), "DetailIds trả PO đã ghi");
            // không có số chuẩn (máy lạ) → 5 dòng null + ghi chú
            g = Gc(machine: "M9"); row = Row(1, 3, 0, T.AddHours(2), T.AddHours(3)); g.Rows.Add(row);
            r = await s.SaveGroupAsync(g);
            var st2 = db.WtsProductionLogStds.List.Where(x => x.WtsLogId == row.WtsLogId).ToList();
            Ok(r.Ok && st2.Count == 5 && st2.All(x => x.StdValue == null && x.Note == "Không tìm thấy chuẩn trong snapshot phiếu KH (NC NC1 · máy M9)"), "Không có chuẩn: 5 dòng trống + ghi chú nêu NC + máy ([RS-E6-Q] đáp án đổi 02/11)");
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
            Eq(r.ErrorMsg, "PO PO1 · mã K1: chưa nhận hàng — chưa nhập số lượng được. (Nhận vào 0 − đã ghi WTS OK 0 − đã ghi WTS NG 0 = còn 0)", "  nguyên văn");
        }

        Console.WriteLine("[W4] Trần L3 + Workflow OFF + K5 khi sửa — chữ y cũ");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); g.Rows.Add(Row(1, 30, 0, T, T.AddHours(1))); g.Rows.Add(Row(1, 15, 0, T.AddHours(1), T.AddHours(2)));
            var r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "PO PO1 · NC NC1: nhập 45 (OK + NG) vượt Chưa làm 40. (Nhận vào 40 − đã ghi WTS OK 0 − đã ghi WTS NG 0 = còn 40)", "2 dòng cùng NC cộng dồn vượt trần");
            Ok(g.Rows.All(x => x.ErrorMsg == r.ErrorMsg) && WtsCount(db) == 0, "Cả 2 dòng mang lời báo, chưa ghi gì");
            g = Gc(nc: "NC2", machine: "M2"); g.Rows.Add(Row(1, 1, 0, T, T.AddHours(1)));
            r = await s.SaveGroupAsync(g);
            Eq(r.ErrorMsg, "PO PO1 · NC NC2: chưa nhận hàng — chưa nhập số lượng được. (Nhận vào 0 − đã ghi WTS OK 0 − đã ghi WTS NG 0 = còn 0)", "NC2 chưa có hàng (NC1 chưa làm) → Locked");
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
            // [WTS-VOID-PO 2026-11-01] đổi chủ đích: xoá dòng đã huỷ → báo tải lại (trước: báo xoá xong dù 0 dòng đổi, như [ML16-ACT] đã sửa cho việc không ra SP)
            Ok(!v.Ok && v.ErrorMsg == "Dòng WTS này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang." && wb.VoidedBy == 2, "[D8] xoá lần 2 dòng đã huỷ: không ghi đè người huỷ trước, báo tải lại", v.ErrorMsg);
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

        Console.WriteLine("[W9] Khoá: người + máy + PO đều LockOwner = Transaction trong 1 giao dịch (PoGuard)");
        {
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear();
            var db = await SeedAsync(); var s = Svc(db);
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear(); Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var g = Gc(); g.Rows.Add(Row(1, 5, 0, T, T.AddHours(1))); var r = await s.SaveGroupAsync(g);
            var names = Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.ToList();
            var locks = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Where(x => x.Contains("sp_getapplock")).ToList();
            // [ML16-Q 2026-11-02] đáp án đổi theo luật mới (Hùng duyệt 02/11): thêm khoá người WTS_WORKER_ đứng ĐẦU → GC 3 lần khoá, non-GC 2 lần
            Ok(r.Ok && locks.Count == 3 && names.SequenceEqual(new[] { "WTS_WORKER_2", "WTS_MACHINE_M1", "MES_PO_1" })
               && locks.All(x => x.Contains("'Transaction'")) && !Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Any(x => x.Contains("sp_releaseapplock")),
               "Lưu GC: 3 lần khoá (người → máy → PO) kiểu Transaction, không có lệnh nhả riêng", string.Join(",", names) + " · " + locks.Count);
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Clear(); Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var h = NonGc("KCS"); h.Rows.Add(Row(1, 1, 0, T, T.AddHours(1), code: "K1", setup: null, mach: null, insp: null, actualPc: 1)); await s.SaveGroupAsync(h);
            locks = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Where(x => x.Contains("sp_getapplock")).ToList();
            Ok(locks.Count == 2 && Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.SequenceEqual(new[] { "WTS_WORKER_3", "MES_PO_1" }), "Lưu non-GC: khoá người → PO (không khoá máy)", string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
        }

        // [RS-D10b 2026-10-31] Lưu nhóm / xoá dòng bị chọn làm nạn nhân deadlock 1205 → tự lưu lại, không ghi trùng, không báo "đã lưu" sai.
        Console.WriteLine("[W10] Deadlock 1205 khi lưu nhóm / xoá dòng → tự lưu lại (RS-D10b)");
        {
            var db = await SeedAsync(); var s = Svc(db);
            int left = 0, skip = 0;
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => skip-- > 0 ? null : left-- > 0
                ? new InvalidOperationException("transient", new Exception("save", new FakeSqlException(1205))) : null;
            try
            {
                int n0 = WtsCount(db, false), st0 = db.WtsProductionLogStds.List.Count;
                left = 1; skip = 1;   // lần lưu thứ 2 (bước ghi số chuẩn) hỏng 1 lần
                var g = Gc(); var row = Row(1, 5, 0, T, T.AddHours(1)); g.Rows.Add(row);
                var r = await s.SaveGroupAsync(g);
                Ok(r.Ok && WtsCount(db, false) == n0 + 1 && db.WtsProductionLogStds.List.Count == st0 + 5 && row.WtsLogId == db.WtsProductionLogs.List.Last().WtsLogId && !StubTransactionOpen(db),
                   "Hỏng ở bước số chuẩn 1 lần: tự lưu lại — đúng 1 dòng WTS + 5 dòng số chuẩn, giao dịch đóng", $"{r.ErrorMsg} · wts {WtsCount(db, false) - n0} · std {db.WtsProductionLogStds.List.Count - st0}");
                left = 99; skip = 0;
                g = Gc(); var row2 = Row(1, 3, 0, T.AddHours(2), T.AddHours(3)); g.Rows.Add(row2);
                r = await s.SaveGroupAsync(g);
                left = 0;
                Ok(!r.Ok && r.ErrorMsg == HandoverService.DeadlockMsg && WtsCount(db, false) == n0 + 1 && row2.WtsLogId == null && !row2.Saved && !StubTransactionOpen(db),
                   "Deadlock mãi: báo 'Máy chủ đang bận … chưa lưu gì', không thêm dòng, dòng về như trước khi bấm Lưu", r.ErrorMsg);
                left = 1;
                var v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = row.WtsLogId, KhPlanDetailId = 1, UserId = 2 });
                Ok(v.Ok && db.WtsProductionLogs.List.SingleOrDefault(x => x.WtsLogId == row.WtsLogId)?.IsVoided == true && !StubTransactionOpen(db), "Xoá dòng gặp deadlock 1 lần: tự thử lại, dòng đã huỷ", v.ErrorMsg);
            }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
        }

        // [RS-D10c 2026-10-31] sp_getapplock trả −3 (khoá bị chọn làm nạn nhân deadlock) → nơi có vòng thử lại tự lưu lại;
        //   "Việc không ra sản phẩm" gặp 1205 → tự lưu lại; nơi chưa có vòng thử lại giữ lời báo cũ.
        Console.WriteLine("[W11] Khoá −3 / deadlock ở WTS + Việc không ra SP → tự thử lại (RS-D10c)");
        {
            var db = await SeedAsync(); var s = Svc(db);
            db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", CategoryName = "Việc chung", TaskCode = "VS", TaskName = "Vệ sinh máy", IsActive = true });
            db.Fixup();
            var dl = new Dictionary<string, int>();   // tên khoá → số lần còn trả −3
            Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = name => dl.TryGetValue(name, out var k) && k > 0 ? (dl[name] = k - 1) >= 0 ? -3 : 0 : 0;
            int left = 0;
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => left-- > 0 ? new InvalidOperationException("transient", new Exception("save", new FakeSqlException(1205))) : null;
            try
            {
                int n0 = WtsCount(db, false);
                dl["WTS_MACHINE_M1"] = 1;
                var g = Gc(); var row = Row(1, 5, 0, T, T.AddHours(1)); g.Rows.Add(row);
                var r = await s.SaveGroupAsync(g);
                Ok(r.Ok && WtsCount(db, false) == n0 + 1 && !StubTransactionOpen(db), "Khoá máy bị −3 1 lần: tự lưu lại, đúng 1 dòng (trước: 'Máy M1 đang có người khác lưu')", r.ErrorMsg);
                dl["MES_PO_1"] = 2;
                g = Gc(); g.Rows.Add(Row(1, 2, 0, T.AddHours(1), T.AddHours(2))); r = await s.SaveGroupAsync(g);
                Ok(r.Ok && WtsCount(db, false) == n0 + 2, "Khoá PO bị −3 2 lần: lần 3 lưu được, đúng 1 dòng", r.ErrorMsg);
                dl["MES_PO_1"] = 99;
                g = Gc(); var row3 = Row(1, 1, 0, T.AddHours(3), T.AddHours(4)); g.Rows.Add(row3); r = await s.SaveGroupAsync(g);
                dl["MES_PO_1"] = 0;
                Ok(!r.Ok && r.ErrorMsg == HandoverService.DeadlockMsg && WtsCount(db, false) == n0 + 2 && row3.WtsLogId == null, "Khoá PO −3 mãi: báo 'Máy chủ đang bận', không ghi", r.ErrorMsg);
                dl["MES_PO_1"] = 1;
                var v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = row.WtsLogId, KhPlanDetailId = 1, UserId = 2 });
                Ok(v.Ok && db.WtsProductionLogs.List.SingleOrDefault(x => x.WtsLogId == row.WtsLogId)?.IsVoided == true, "Xoá dòng: khoá PO −3 1 lần → tự thử lại, đã huỷ", v.ErrorMsg);
                // Việc không ra sản phẩm (người 1 = Admin)
                var a0 = db.WorkerActivityLogs.List.Count;
                var req = new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "VS", StartTime = DateTime.Today.AddHours(13), EndTime = DateTime.Today.AddHours(13.5) };
                left = 1;
                var ra = await s.SaveActivityAsync(req);
                Ok(ra.Ok && db.WorkerActivityLogs.List.Count == a0 + 1 && !StubTransactionOpen(db), "Việc không ra SP gặp 1205 1 lần: tự lưu lại, đúng 1 dòng (trước: 'Lỗi khi lưu: … deadlocked')", ra.ErrorMsg);
                dl["WTS_WORKER_1"] = 1;
                req = new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "VS", StartTime = DateTime.Today.AddHours(14), EndTime = DateTime.Today.AddHours(14.5) };
                ra = await s.SaveActivityAsync(req);
                Ok(ra.Ok && db.WorkerActivityLogs.List.Count == a0 + 2, "Việc không ra SP: khoá người nhập −3 1 lần → tự lưu lại", ra.ErrorMsg);
                left = 99;
                req = new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "VS", StartTime = DateTime.Today.AddHours(15), EndTime = DateTime.Today.AddHours(15.5) };
                ra = await s.SaveActivityAsync(req);
                left = 0;
                Ok(!ra.Ok && ra.ErrorMsg == HandoverService.DeadlockMsg && db.WorkerActivityLogs.List.Count == a0 + 2 && !StubTransactionOpen(db), "Việc không ra SP deadlock mãi: 'Máy chủ đang bận', không ghi", ra.ErrorMsg);
                left = 1; Exception? other = null;
                Microsoft.EntityFrameworkCore.DbContext.OnSave = () => left-- > 0 ? new Exception("save", new FakeSqlException(547)) : null;
                ra = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "VS", StartTime = DateTime.Today.AddHours(16), EndTime = DateTime.Today.AddHours(16.5) });
                Ok(!ra.Ok && ra.ErrorMsg!.StartsWith("Lỗi khi lưu: ") && db.WorkerActivityLogs.List.Count == a0 + 2, "Lỗi SQL khác: giữ y cũ 'Lỗi khi lưu: …', không thử lại", ra.ErrorMsg + other);
                // Nơi chưa có vòng thử lại (Kho / Kế hoạch qua PoGuard.LockAsync mặc định): −3 vẫn báo y cũ, không ném lỗi
                dl["MES_PO_2"] = 1;
                await using (var trx = await db.Database.BeginTransactionAsync())
                {
                    var e = await PoGuard.LockAsync(db, 2);
                    Eq(e, "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.", "PoGuard.LockAsync mặc định: −3 → lời báo cũ (Kho, Kế hoạch, Update TG chuẩn)");
                }
                dl["MES_PO_2"] = 1; Exception? thrown = null;
                await using (var trx = await db.Database.BeginTransactionAsync())
                { try { await PoGuard.LockAsync(db, 2, deadlockThrows: true); } catch (Exception ex) { thrown = ex; } }
                Ok(thrown != null && HandoverService.IsDeadlock(thrown), "deadlockThrows: −3 → lỗi deadlock (51205) cho vòng thử lại");
                dl["MES_PO_2"] = 0; dl["X"] = 0;
                Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = name => name == "MES_PO_2" ? -1 : 0;
                await using (var trx = await db.Database.BeginTransactionAsync())
                {
                    var e = await PoGuard.LockAsync(db, 2, deadlockThrows: true);
                    Eq(e, "PO đang có người khác lưu WTS / giao nhận. Thử lại sau vài giây.", "Hết giờ chờ (−1): vẫn lời báo cũ, không thử lại");
                }
            }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; Microsoft.EntityFrameworkCore.DatabaseFacade.AppLockResult = null; }
        }

        // [WTS-VOID-PO][WTS-NGCASE-PO][MACHINE-CASE][KH-UPDFIELD 2026-11-01] máy chủ không tin PO / vụ / tên máy trang gửi
        Console.WriteLine("[W12] Xoá / sửa dòng: PO đọc từ DB, chỉ dòng của mình + hôm nay; vụ lỗi đúng PO; khoá máy không phân biệt hoa thường; KH không sửa SL qua UpdateField");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var g = Gc(); var a = Row(1, 10, 0, T, T.AddHours(1)); g.Rows.Add(a); await s.SaveGroupAsync(g);
            g = Gc(nc: "NC2", machine: "M2"); var b = Row(1, 10, 0, T.AddHours(1), T.AddHours(2)); g.Rows.Add(b); await s.SaveGroupAsync(g);
            db.Fixup();
            var wa = db.WtsProductionLogs.List.First(x => x.WtsLogId == a.WtsLogId);
            // 1) trang gửi PO sai (PO2 tắt Workflow) → trước: khoá PO2, không K5 → xoá được, sổ PO1 NC2 âm −10
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = a.WtsLogId, KhPlanDetailId = 2, UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg!.StartsWith("Không xoá được: xoá xong sẽ làm sai số ở Gia công NC2: chưa làm = -10") && !wa.IsVoided
               && Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains("MES_PO_1") && !Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains("MES_PO_2"),
               "Xoá với PO trang gửi SAI: khoá + K5 theo PO thật của dòng (PO1) → chặn, dòng còn nguyên", v.ErrorMsg + " · khoá " + string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            // 2) PO trống → trước: nhánh cuối huỷ thẳng, không khoá, không K5
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = a.WtsLogId, KhPlanDetailId = null, UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg!.StartsWith("Không xoá được:") && !wa.IsVoided, "Xoá với PO trống: vẫn khoá PO1 + K5 → chặn (trước: huỷ không khoá, không kiểm)", v.ErrorMsg);
            // 3) dòng của người khác
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = b.WtsLogId, KhPlanDetailId = 1, UserId = 3 });
            var wb = db.WtsProductionLogs.List.First(x => x.WtsLogId == b.WtsLogId);
            Ok(!v.Ok && v.ErrorMsg == "Chỉ xoá được dòng WTS của chính mình." && !wb.IsVoided, "Xoá dòng của người khác: chặn, dòng còn nguyên", v.ErrorMsg);
            // 4) dòng ngày cũ
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 900, KhPlanDetailId = 2, ProcessGroup = "GC", NC = "NC1", WorkerId = 2, QtyDone = 1, QtyOk = 1, StartTime = T.AddDays(-1), EndTime = T.AddDays(-1).AddHours(1), CreatedAt = T.AddDays(-1) });
            db.Fixup();
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 2, UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg == "Chỉ được xoá WTS của ngày hôm nay. Ngày cũ cần quyền cao hơn." && !db.WtsProductionLogs.List.First(x => x.WtsLogId == 900).IsVoided, "Xoá dòng ngày cũ: chặn (như trang + lưu WTS)", v.ErrorMsg);
            // 5) id không có
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 12345, KhPlanDetailId = 1, UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg == "Dòng WTS này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang.", "Xoá id không có: báo tải lại, không ghi", v.ErrorMsg);
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg == "Không có dòng để xoá — tải lại trang.", "Xoá không id: không ghi gì", v.ErrorMsg);
            // 6) xoá đúng: dòng NC2 (cuối) của mình, PO trang gửi sai vẫn huỷ đúng + báo đúng PO
            v = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = b.WtsLogId, KhPlanDetailId = 2, UserId = 2 });
            Ok(v.Ok && wb.IsVoided && wb.VoidedBy == 2 && v.DetailIds.SequenceEqual(new[] { 1 }), "Xoá dòng của mình: huỷ, DetailIds = PO thật của dòng (PO1)", v.ErrorMsg);
            // 7) Sửa dòng (void + mới) dòng của người khác / ngày cũ → không huỷ, không ghi dòng mới
            int n0 = WtsCount(db, false);
            g = Gc(); g.UserId = 3; var e1 = Row(1, 5, 0, T.AddHours(3), T.AddHours(4)); e1.IsEditing = true; e1.WtsLogId = a.WtsLogId; g.Rows.Add(e1);
            var r = await s.SaveGroupAsync(g);
            Ok(!r.Ok && r.ErrorMsg!.Contains("Dòng WTS này đã bị sửa / xoá ở nơi khác") && !wa.IsVoided && WtsCount(db, false) == n0, "Sửa dòng của người khác: không huỷ dòng cũ, không ghi dòng mới", r.ErrorMsg);
            g = Gc(machine: "M5"); var e2 = Row(2, 1, 0, T.AddHours(4), T.AddHours(5)); e2.IsEditing = true; e2.WtsLogId = 900; g.Rows.Add(e2);
            r = await s.SaveGroupAsync(g);
            Ok(!r.Ok && r.ErrorMsg!.Contains("Dòng WTS này đã bị sửa / xoá ở nơi khác") && !db.WtsProductionLogs.List.First(x => x.WtsLogId == 900).IsVoided && WtsCount(db, false) == n0, "Sửa dòng ngày cũ: không huỷ, không ghi", r.ErrorMsg);
            // sửa dòng gửi vụ KHÁC cùng PO (không phải vụ của dòng cũ trong DB) → không nhận, vụ mới (rà độc lập 01/11)
            g = Gc(machine: "M9"); var q1 = Row(1, 1, 1, T.AddHours(9), T.AddHours(10), cat: 1); g.Rows.Add(q1); r = await s.SaveGroupAsync(g); db.Fixup();
            g = Gc(machine: "M10"); var q2 = Row(1, 1, 1, T.AddHours(10), T.AddHours(11), cat: 2); g.Rows.Add(q2); r = await s.SaveGroupAsync(g); db.Fixup();
            int ncq = db.NgCases.List.Count;
            g = Gc(machine: "M10"); var q3 = Row(1, 1, 1, T.AddHours(10), T.AddHours(11), cat: 2); q3.IsEditing = true; q3.WtsLogId = q2.WtsLogId; q3.NgCaseId = q1.NgCaseId; g.Rows.Add(q3);
            r = await s.SaveGroupAsync(g); db.Fixup();
            Ok(r.Ok && q3.NgCaseId != q1.NgCaseId && q3.NgCaseId != null && db.NgCases.List.First(c => c.NgCaseId == q1.NgCaseId).NgCategoryId == 1,
               "Sửa dòng gửi vụ của dòng khác cùng PO: không gắn / không đổi loại lỗi vụ đó", r.ErrorMsg);

            // 8) [WTS-NGCASE-PO] vụ lỗi theo PO của dòng
            g = Gc(machine: "M6"); var n1 = Row(1, 2, 1, T.AddHours(5), T.AddHours(6), cat: 1); g.Rows.Add(n1);
            r = await s.SaveGroupAsync(g); db.Fixup();
            var v1 = n1.NgCaseId;
            Ok(r.Ok && v1 != null && db.NgCases.List.First(c => c.NgCaseId == v1).KhPlanDetailId == 1, "Lưu NG PO1 → vụ thuộc PO1", r.ErrorMsg);
            // sửa dòng, đổi sang PO2 (khác loại lỗi) → vụ mới của PO2, vụ PO1 không bị đổi loại lỗi
            int ncase = db.NgCases.List.Count;
            g = Gc(machine: "M6"); var n2 = Row(2, 2, 1, T.AddHours(5), T.AddHours(6), cat: 2); n2.IsEditing = true; n2.WtsLogId = n1.WtsLogId; n2.NgCaseId = v1; g.Rows.Add(n2);
            r = await s.SaveGroupAsync(g); db.Fixup();
            var newLog = db.WtsProductionLogs.List.First(x => x.WtsLogId == n2.WtsLogId);
            Ok(r.Ok && newLog.KhPlanDetailId == 2 && newLog.NgCaseId != v1 && newLog.NgCaseId != null
               && db.NgCases.List.First(c => c.NgCaseId == newLog.NgCaseId).KhPlanDetailId == 2 && db.NgCases.List.Count == ncase + 1
               && db.NgCases.List.First(c => c.NgCaseId == v1).NgCategoryId == 1,
               "Sửa dòng đổi PO1 → PO2: vụ MỚI của PO2, vụ PO1 giữ loại lỗi (trước: WTS PO2 gắn vụ PO1, đổi loại lỗi vụ PO1)", r.ErrorMsg);
            // dòng MỚI (không sửa) mà trang còn sót NgCaseId → vụ mới (đúng như bước thử K5)
            ncase = db.NgCases.List.Count;
            g = Gc(machine: "M7"); var n3 = Row(1, 1, 1, T.AddHours(6), T.AddHours(7), cat: 1); n3.NgCaseId = v1; g.Rows.Add(n3);
            r = await s.SaveGroupAsync(g); db.Fixup();
            Ok(r.Ok && n3.NgCaseId != v1 && db.NgCases.List.Count == ncase + 1, "Dòng mới còn sót NgCaseId: tạo vụ mới, không gắn vụ cũ", r.ErrorMsg);
            // sửa dòng giữ PO → giữ vụ (như cũ)
            ncase = db.NgCases.List.Count; var v3 = n3.NgCaseId;
            g = Gc(machine: "M7"); var n4 = Row(1, 1, 1, T.AddHours(6), T.AddHours(7), cat: 1); n4.IsEditing = true; n4.WtsLogId = n3.WtsLogId; n4.NgCaseId = v3; g.Rows.Add(n4);
            r = await s.SaveGroupAsync(g); db.Fixup();
            Ok(r.Ok && n4.NgCaseId == v3 && db.NgCases.List.Count == ncase, "Sửa dòng cùng PO: giữ vụ cũ (như cũ)", r.ErrorMsg);

            // 8b) Xoá dòng "Việc không ra SP": chỉ của mình + hôm nay
            db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", CategoryName = "Việc chung", TaskCode = "VS", TaskName = "Vệ sinh máy", IsActive = true });
            db.Fixup();
            var ra = await s.SaveActivityAsync(new WtsSaveService.WtsActivityRequest { UserId = 1, WtsCode = "VS", StartTime = DateTime.Today.AddHours(16), EndTime = DateTime.Today.AddHours(16.5) });
            var act = db.WorkerActivityLogs.List.Last();
            var va = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = act.ActivityLogId, UserId = 2 });
            Ok(ra.Ok && !va.Ok && !act.IsVoided, "Xoá việc không ra SP của người khác: chặn", va.ErrorMsg);
            db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 950, WorkerId = 1, WorkDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), WtsCode = "VS", StartTime = DateTime.Today.AddDays(-1).AddHours(9), EndTime = DateTime.Today.AddDays(-1).AddHours(10), CreatedAt = DateTime.Today.AddDays(-1) });
            db.Fixup();
            va = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = 950, UserId = 1 });
            Ok(!va.Ok && !db.WorkerActivityLogs.List.First(x => x.ActivityLogId == 950).IsVoided, "Xoá việc không ra SP ngày cũ: chặn", va.ErrorMsg);
            va = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = act.ActivityLogId, UserId = 1 });
            Ok(va.Ok && act.IsVoided, "Xoá việc không ra SP của mình hôm nay: được", va.ErrorMsg);

            // 9) [MACHINE-CASE] tên khoá máy chuẩn hoá
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            g = Gc(machine: " m8 "); g.Rows.Add(Row(1, 1, 0, T.AddHours(8), T.AddHours(9))); r = await s.SaveGroupAsync(g);
            Ok(Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Contains("WTS_MACHINE_M8"),
               "Khoá máy ' m8 ' = 'WTS_MACHINE_M8' (cùng khoá với 'M8')", string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));

            // 10) [KH-UPDFIELD] UpdateFieldAsync không sửa SL PO
            var kh = new KhPlanService(db); string? err = null;
            try { await kh.UpdateFieldAsync(1, "Quantity", "999", 1); } catch (InvalidOperationException ex) { err = ex.Message; }
            Ok(err != null && err.StartsWith("SL PO chỉ sửa ở trang Sửa KH") && db.KhPlanDetails.List.First(d => d.KhPlanDetailId == 1).Quantity == 40, "UpdateFieldAsync Quantity: chặn, SL PO giữ 40", err);
            err = null;
            try { await kh.UpdateFieldAsync(1, "PartNotes", "x", 2); } catch (InvalidOperationException ex) { err = ex.Message; }
            Ok(err != null && db.KhPlanDetails.List.First(d => d.KhPlanDetailId == 1).PartNotes != "x", "UpdateFieldAsync người không có quyền Sửa KE_HOACH: chặn", err);
            err = null;
            try { await kh.UpdateFieldAsync(1, "PartNotes", "ghi chú", 1); } catch (InvalidOperationException ex) { err = ex.Message; }
            Ok(err == null && db.KhPlanDetails.List.First(d => d.KhPlanDetailId == 1).PartNotes == "ghi chú", "UpdateFieldAsync Admin sửa ghi chú: được (như cũ)", err);
        }

        // [STD-SNAP 2026-11-02] Trang tra số chuẩn lúc chọn PO (cache); "Update TG chuẩn" chạy SAU đó đổi số trong snapshot DB.
        //   Lưu phải đọc lại SỐ chuẩn từ đúng dòng snapshot (bảng + id trang đã tra) → WtsProductionLogStds + IsActualManual theo số MỚI.
        //   Không thấy dòng snapshot → giữ số trang như cũ.
        Console.WriteLine("[W13] STD-SNAP — lưu WTS đọc số chuẩn từ snapshot DB (trang để cũ không ghi số cũ)");
        {
            var db = await SeedAsync(); var s = Svc(db);
            // trang tra lúc mở: NC1 M1 = 5 / 2 / 1 / 10 / 3 (snapshot #1); NC1 M3 (máy đồng dạng) = 6 / 2.5 / 1 / 12 / 4 (timing #1); Taro 1T = 15 (#12)
            var pageM1 = new WtsSaveService.WtsSaveStd { SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3, SrcTable = StdTrack.Table.Machining, SrcRowId = 1 };
            var pageM3 = new WtsSaveService.WtsSaveStd { SetupTime = 6, MachiningTime = 2.5m, InspectionTime = 1, PreparationTime = 12, TrialRunTime = 4, SrcTable = StdTrack.Table.MachiningTiming, SrcRowId = 1 };
            // sau đó Update TG chuẩn đổi snapshot DB
            var sm = db.KhPlanRouteSnapshotMachining.List.First(x => x.SnapshotId == 1); sm.SetupTime = 7; sm.MachiningTime = 2.2m;
            var st = sm.TimingRows.First(x => x.TimingSnapshotId == 1); st.MachiningTime = 2.8m; st.TrialRunTime = 5;
            if (!db.KhPlanRouteSnapshotMachiningTimings.List.Contains(st)) db.KhPlanRouteSnapshotMachiningTimings.List.Add(st);   // DB thật: dòng timing nằm ở bảng riêng (seed chỉ gắn vào TimingRows)
            var s1t = db.KhPlanRouteSnapshotTaro.List.First(x => x.SnapshotId == 12); s1t.StandardTime = 20;
            Dictionary<string, decimal?> Stds(long? id) => db.WtsProductionLogStds.List.Where(x => x.WtsLogId == id).ToDictionary(x => x.FieldName, x => x.StdValue);
            string Show(Dictionary<string, decimal?> d) => string.Join(",", d.Select(x => $"{x.Key}={x.Value}"));

            var g = Gc(); var r1 = Row(1, 3, 0, T, T.AddHours(1)); r1.GcStd = pageM1; g.Rows.Add(r1);
            var res = await s.SaveGroupAsync(g);
            var d1 = Stds(r1.WtsLogId);
            Ok(res.Ok && d1.Count == 5 && d1[StdTrack.Field.Setup] == 7 && d1[StdTrack.Field.Machining] == 2.2m && d1[StdTrack.Field.Inspection] == 1 && d1[StdTrack.Field.Preparation] == 10 && d1[StdTrack.Field.TrialRun] == 3,
               "GC NC1 M1: trang giữ 5 / 2, snapshot DB đã đổi 7 / 2.2 → số chuẩn ghi 7 / 2.2 / 1 / 10 / 3", (res.ErrorMsg ?? "") + " · " + Show(d1));
            Ok(r1.GcStd != null && r1.GcStd.SetupTime == 7 && r1.GcStd.SrcRowId == 1, "DTO dòng nhận số chuẩn mới (trang hiện đúng sau khi lưu)", r1.GcStd?.SetupTime?.ToString());

            g = Gc(machine: "M3"); var r2 = Row(1, 3, 0, T.AddHours(1), T.AddHours(2)); r2.GcStd = pageM3; g.Rows.Add(r2);
            res = await s.SaveGroupAsync(g);
            var d2 = Stds(r2.WtsLogId);
            Ok(res.Ok && d2[StdTrack.Field.Setup] == 6 && d2[StdTrack.Field.Machining] == 2.8m && d2[StdTrack.Field.TrialRun] == 5,
               "GC NC1 M3 (máy đồng dạng, bảng timing): ghi số timing mới 6 / 2.8 / … / 5", (res.ErrorMsg ?? "") + " · " + Show(d2));

            var h = NonGc("HTSP", "Taro"); var p = Row(1, 0, 0, T.AddHours(2), T.AddHours(2).AddMinutes(20), code: "1T", setup: null, mach: null, insp: null, actualPc: 20);
            p.StdTimePerPc = 15; p.StdSrcTable = StdTrack.Table.Taro; p.StdSrcRowId = 12; h.Rows.Add(p);
            res = await s.SaveGroupAsync(h);
            var w3 = db.WtsProductionLogs.List.Last(); var d3 = Stds(w3.WtsLogId);
            Ok(res.Ok && w3.WtsCode == "1T" && w3.IsActualManual == false && d3.Count == 1 && d3[StdTrack.Field.Standard] == 20,
               "HTSP Taro 1T: trang giữ chuẩn 15, DB đã đổi 20, T.Thực tế 20 → IsActualManual = false (bằng chuẩn mới), số chuẩn ghi 20",
               $"{res.ErrorMsg} · IsActualManual={w3.IsActualManual} · {Show(d3)}");

            // [RS-E6-Q 02/11 — đáp án đổi] không tìm thấy dòng snapshot (id lạ) → GC để TRỐNG chuẩn (trước: giữ số trang)
            var pageLa = new WtsSaveService.WtsSaveStd { SetupTime = 5, MachiningTime = 2, InspectionTime = 1, SrcTable = StdTrack.Table.Machining, SrcRowId = 999 };
            g = Gc(); var r4 = Row(1, 2, 0, T.AddHours(3), T.AddHours(4)); r4.GcStd = pageLa; g.Rows.Add(r4);
            res = await s.SaveGroupAsync(g);
            var d4 = Stds(r4.WtsLogId);
            Ok(res.Ok && d4.Count == 5 && d4.Values.All(v => v == null), "Dòng snapshot trang tra không còn (id 999): để trống chuẩn ([RS-E6-Q], trước giữ số trang)", (res.ErrorMsg ?? "") + " · " + Show(d4));
            // snapshot của PO KHÁC cùng id không được dùng (kiểm KhPlanDetailId)
            var pageOther = new WtsSaveService.WtsSaveStd { SetupTime = 4, MachiningTime = 3, InspectionTime = 1, SrcTable = StdTrack.Table.Machining, SrcRowId = 3 };   // #3 là NC1 của PO2
            g = Gc(); var r5 = Row(1, 2, 0, T.AddHours(4), T.AddHours(5)); r5.GcStd = pageOther; g.Rows.Add(r5);
            res = await s.SaveGroupAsync(g);
            var d5 = Stds(r5.WtsLogId);
            Ok(res.Ok && d5.Count == 5 && d5.Values.All(v => v == null), "Id snapshot thuộc PO khác: không đọc số PO đó, để trống chuẩn ([RS-E6-Q])", (res.ErrorMsg ?? "") + " · " + Show(d5));
        }

        // [RS-E6-Q 2026-11-02] Hùng duyệt: máy không có trong quy trình (snapshot) PO → KHÔNG ghi số chuẩn của máy / NC khác (trước: trang mượn số
        //   dòng NC chính, máy chủ ghi như số thật). Máy chủ tự kiểm dòng chuẩn trang gửi đúng NC của nhóm + đúng máy đang chọn.
        Console.WriteLine("[W14] RS-E6-Q — số chuẩn trang gửi phải đúng NC + đúng máy; sai → không ghi số chuẩn (để trống), vẫn lưu dòng");
        {
            var db = await SeedAsync(); var s = Svc(db);
            var sm = db.KhPlanRouteSnapshotMachining.List.First(x => x.SnapshotId == 1);
            foreach (var t in sm.TimingRows) if (!db.KhPlanRouteSnapshotMachiningTimings.List.Contains(t)) db.KhPlanRouteSnapshotMachiningTimings.List.Add(t);   // DB thật: bảng riêng
            var tm = sm.TimingRows.First(x => x.TimingSnapshotId == 1);
            Dictionary<string, decimal?> Stds(long? id) => db.WtsProductionLogStds.List.Where(x => x.WtsLogId == id).ToDictionary(x => x.FieldName, x => x.StdValue);
            bool AllNull(Dictionary<string, decimal?> d) => d.Count == 5 && d.Values.All(v => v == null);
            WtsSaveService.WtsSaveStd Main() => new() { SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3, SrcTable = StdTrack.Table.Machining, SrcRowId = 1 };
            WtsSaveService.WtsSaveStd Sub() => new() { SetupTime = 6, MachiningTime = 2.5m, InspectionTime = 1, PreparationTime = 12, TrialRunTime = 4, SrcTable = StdTrack.Table.MachiningTiming, SrcRowId = 1 };
            async Task<(WtsSaveService.WtsSaveResult r, WtsSaveService.WtsSaveRow row)> Save(string nc, string mc, WtsSaveService.WtsSaveStd std, int h)
            { var g = Gc(nc: nc, machine: mc); var row = Row(1, 1, 0, T.AddHours(h), T.AddHours(h).AddMinutes(30)); row.GcStd = std; g.Rows.Add(row); return (await s.SaveGroupAsync(g), row); }
            var (a, ra) = await Save("NC1", "M9", Main(), 0);
            Ok(a.Ok && AllNull(Stds(ra.WtsLogId)) && ra.GcStd == null, "Máy M9 không có trong quy trình, trang (cũ) gửi số NC chính: dòng lưu, số chuẩn để trống", (a.ErrorMsg ?? "") + " · " + string.Join(",", Stds(ra.WtsLogId)));
            Ok(db.WtsProductionLogStds.List.Any(x => x.WtsLogId == ra.WtsLogId && (x.Note ?? "").Contains("máy M9")), "Ghi chú số chuẩn nêu rõ máy M9", string.Join(" | ", db.WtsProductionLogStds.List.Where(x => x.WtsLogId == ra.WtsLogId).Select(x => x.Note)));
            var (b, rb) = await Save("NC1", "M3", Main(), 1);
            Ok(b.Ok && AllNull(Stds(rb.WtsLogId)), "Máy đồng dạng M3 mà trang gửi dòng NC chính (M1): không ghi số M1 cho M3", b.ErrorMsg);
            var (c, rc) = await Save("NC1", "M1", Sub(), 2);
            Ok(c.Ok && AllNull(Stds(rc.WtsLogId)), "Máy M1 mà trang gửi dòng đồng dạng của M3: không ghi", c.ErrorMsg);
            var (d, rd) = await Save("NC1", "M3", Sub(), 3);
            var dd = Stds(rd.WtsLogId);
            Ok(d.Ok && dd[StdTrack.Field.Setup] == tm.SetupTime && dd[StdTrack.Field.Machining] == tm.MachiningTime, "Máy M3 + dòng đồng dạng M3: giữ số đúng", d.ErrorMsg + " · " + string.Join(",", dd));
            var (e, re) = await Save("NC1", " m1 ", Main(), 4);
            var de = Stds(re.WtsLogId);
            Ok(e.Ok && de[StdTrack.Field.Setup] == 5 && de[StdTrack.Field.Machining] == 2, "Máy ' m1 ' (khác hoa thường / khoảng trắng) + dòng chính M1: giữ số đúng", e.ErrorMsg + " · " + string.Join(",", de));
            var (f, rf) = await Save("NC2", "M1", Main(), 5);
            Ok(f.Ok && AllNull(Stds(rf.WtsLogId)), "Nhóm NC2 mà trang gửi dòng chuẩn của NC1: lưu được, không ghi số NC1", f.ErrorMsg);
            // rà độc lập V2: 2 nhánh mới — (1) NC đăng ký máy TRỐNG: không có dòng đồng dạng → mọi máy dùng số dòng chính; có dòng đồng dạng → máy
            //   không thuộc dòng nào thì để trống; (2) dòng đồng dạng của NC KHÁC (NC cha sai) mà đúng máy → không ghi
            var n2 = db.KhPlanRouteSnapshotMachining.List.First(x => x.SnapshotId == 2);   // NC2 PO1 (M2)
            n2.MachineRegistered = null;
            WtsSaveService.WtsSaveStd N2() => new() { SetupTime = 4, MachiningTime = 3, InspectionTime = 1, SrcTable = StdTrack.Table.Machining, SrcRowId = 2 };
            var (g1, rg1) = await Save("NC2", "MX", N2(), 6);
            var dg1 = Stds(rg1.WtsLogId);
            Ok(g1.Ok && dg1[StdTrack.Field.Setup] == 4 && dg1[StdTrack.Field.Machining] == 3, "NC2 không đăng ký máy, không dòng đồng dạng: máy MX dùng số dòng chính", g1.ErrorMsg + " · " + string.Join(",", dg1));
            var t2 = new KhPlanRouteSnapshotMachiningTiming { TimingSnapshotId = 77, MachiningSnapshotId = 2, KhPlanDetailId = 1, SoMay = "M3", SetupTime = 9, MachiningTime = 9, InspectionTime = 1, DisplayOrder = 1 };
            db.KhPlanRouteSnapshotMachiningTimings.List.Add(t2); n2.TimingRows.Add(t2);
            var (g2, rg2) = await Save("NC2", "MY", N2(), 7);
            Ok(g2.Ok && AllNull(Stds(rg2.WtsLogId)), "NC2 không đăng ký máy NHƯNG có dòng đồng dạng (M3): máy MY không thuộc dòng nào → để trống", g2.ErrorMsg);
            WtsSaveService.WtsSaveStd Sub2() => new() { SetupTime = 9, MachiningTime = 9, InspectionTime = 1, SrcTable = StdTrack.Table.MachiningTiming, SrcRowId = 1 };   // dòng đồng dạng M3 của NC1
            var (g3, rg3) = await Save("NC2", "M3", Sub2(), 8);
            Ok(g3.Ok && AllNull(Stds(rg3.WtsLogId)), "Nhóm NC2 máy M3 mà trang gửi dòng đồng dạng M3 của NC1 (NC cha sai): không ghi", g3.ErrorMsg);
            var (g4, rg4) = await Save("NC2", "m3", new() { SetupTime = 9, MachiningTime = 9, InspectionTime = 1, SrcTable = StdTrack.Table.MachiningTiming, SrcRowId = 77 }, 9);
            var dg4 = Stds(rg4.WtsLogId);
            Ok(g4.Ok && dg4[StdTrack.Field.Setup] == 9, "Nhóm NC2 máy 'm3' + dòng đồng dạng M3 của chính NC2: giữ số", g4.ErrorMsg + " · " + string.Join(",", dg4));
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }

    static bool StubTransactionOpen(AppDbContext db) => db.Database.Current != null;
}
