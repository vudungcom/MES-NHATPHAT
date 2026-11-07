// [GC-SUA-TAI-CHO 2026-11-04] Dữ liệu riêng cho drive_sua.py (dựng khi drive gọi POST /harness/sua-seed — không nằm trong seed khởi động,
//   để /harness/dump của drive.py không đổi): KH-SUA / PO-SUA 10 chiếc, GC NC01 (M1) → NC02 (M2); Kho nhận 10 phôi, Kho → GC 8 Hàng mới (đã nhận),
//   Kho báo 2 phôi lỗi → gửi GC nhờ sửa (ô Hàng sửa, đã nhận) — bằng HandoverService thật. Người KCS giao nhận "kcsgn" (Sửa HANDOVER_KCS, mật khẩu 123456).
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using System.Text.Json;

public static class HarnessSua
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-SUA"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-2);
            var g = new UserGroup { GroupCode = "GNKCS", GroupName = "Giao nhận KCS", IsActive = true,
                Permissions = JsonSerializer.Serialize(new[] { new GroupPermissionSetting { SectionCode = SystemPermissions.HandoverKcs, CanView = true, CanEdit = true } }.ToList()) };
            db.UserGroups.Add(g); await db.SaveChangesAsync();
            db.Users.Add(new User { Username = "kcsgn", FullName = "KCS giao nhận", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true });
            await db.SaveChangesAsync();
            var plan = new KhPlan { PlanNo = "KH-SUA", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-SUA", PurchaseOrder = "PO-SUA", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M2", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-SUA", ViTriDePhoi = "SUA", SoPO = "PO-SUA", PartNo = "PART-SUA", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync();
            db.Fixup();
            var svc = new HandoverService(db);
            var all = new List<string> { "KHO" };
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("sua-seed " + what + ": " + r.Error); db.Fixup(); }
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 8, admin.UserId, "MOI", false, "MOI"), "giao 8");
            await Must(svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 8, 0, null, admin.UserId), "nhận 8");
            var cat = db.NgCategories.List.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                db.NgCategories.Add(new NgCategory { Code = "NUT", Name = "Phôi nứt", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
                await db.SaveChangesAsync();
                cat = db.NgCategories.List.First(c => c.IsActive);
            }
            await Must(svc.ReportNgAfterReceiveAsync(d.KhPlanDetailId, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "", null, admin.UserId, all, cat.NgCategoryId), "báo phôi lỗi");
            var vu = db.HandoverNgReports.List.Last().NgCaseId;
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 2, admin.UserId, "LOI", false, "SUA", ngCaseId: vu), "gửi GC sửa");
            await Must(svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 2, 0, null, admin.UserId), "GC nhận hàng sửa");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-SUA");
        return new { det.KhPlanDetailId };
    }

    /// <summary>
    /// [NHAN-DOI-O-NC][K5-CLOSE-NHIEU 2026-11-06] drive_nhannc.py: PO-NNC 10, GC NC01 → NC02. Kho → GC 2 (NC01) đã nhận + WTS NC01 OK 2 (dòng chép thẳng);
    /// GC báo lỗi 1 chiếc "chưa làm NC02" (vụ ở NC02) → giao Kho ô Hàng lỗi → Kho nhận → Kho giao lại GC ô Hàng lỗi (phiếu KHÔNG NC, chờ GC nhận).
    /// Phiếu Kho → GC 6 (NC01): nhận 1 + 1 → huỷ phần chưa nhận (4) → huỷ lần nhận thứ 2 → huỷ phần chưa nhận lần 2 (1) → 2 dòng HandoverTxCloses.
    /// </summary>
    public static async Task<object> SeedNhanNcAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-NNC"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-2);
            var plan = new KhPlan { PlanNo = "KH-NNC", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-NNC", PurchaseOrder = "PO-NNC", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M2", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-NNC", ViTriDePhoi = "NNC", SoPO = "PO-NNC", PartNo = "PART-NNC", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync();
            db.Fixup();
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("nhannc-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 2, admin.UserId, "MOI", false, "MOI", toNC: "NC01"), "giao 2");
            await Must(svc.ReceiveAsync(Last(), 2, 0, null, admin.UserId), "nhận 2");
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", QtyDone = 2, QtyOk = 2, QtyNg = 0,
                StartTime = t0.AddMinutes(10), EndTime = t0.AddMinutes(20), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(20) });
            await db.SaveChangesAsync(); db.Fixup();
            var cat = db.NgCategories.List.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                db.NgCategories.Add(new NgCategory { Code = "NUT", Name = "Phôi nứt", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
                await db.SaveChangesAsync();
                cat = db.NgCategories.List.First(c => c.IsActive);
            }
            await Must(svc.ReportNgAfterReceiveAsync(d.KhPlanDetailId, "GC", HandoverNgSource.Chua, "NC02", 1, HandoverNgSource.DauRa, null, "", null, admin.UserId, new List<string> { "GC" }, cat.NgCategoryId), "báo lỗi NC02");
            var vu = db.HandoverNgReports.List.Last().NgCaseId;
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "KHO", 1, admin.UserId, "LOI", false, "LOI", ngCaseId: vu), "GC trả Kho");
            await Must(svc.ReceiveAsync(Last(), 0, 1, null, admin.UserId, null, HandoverCell.Loi), "Kho nhận");
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 1, admin.UserId, "LOI", false, "LOI", ngCaseId: vu), "Kho giao lại GC ô Hàng lỗi");
            // huỷ phần chưa nhận 2 lần
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 6, admin.UserId, "MOI", false, "MOI", toNC: "NC01"), "giao 6");
            var t6 = Last();
            await Must(svc.ReceiveAsync(t6, 1, 0, null, admin.UserId), "nhận 1");
            await Must(svc.ReceiveAsync(t6, 1, 0, null, admin.UserId), "nhận 1 nữa");
            await Must(svc.CloseRemainderAsync(t6, "huy lan dau", admin.UserId, new List<string> { "KHO" }), "huỷ phần còn 1");
            var rid = db.HandoverTransactions.List.Single(x => x.HandoverTxId == t6).Receives.Where(r => !r.IsVoided).Max(r => r.ReceiveId);
            await Must(svc.VoidReceiveAsync(rid, "nhan nham", admin.UserId, new List<string> { "KHO", "GC" }), "huỷ lần nhận 2");
            await Must(svc.CloseRemainderAsync(t6, "huy lan hai", admin.UserId, new List<string> { "KHO" }), "huỷ phần còn 2");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-NNC");
        return new { det.KhPlanDetailId };
    }

    public static async Task<object> DumpNhanNcAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-NNC");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        var txIds = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id).Select(t => t.HandoverTxId).ToHashSet();
        var gc = row.G("GC");
        return new
        {
            recs = db.HandoverReceives.List.Where(r => txIds.Contains(r.HandoverTxId)).Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.ToCell, r.ToNC, r.Notes, r.IsVoided }).ToList(),
            closes = db.HandoverTxCloses.List.Where(c => txIds.Contains(c.HandoverTxId)).Select(c => new { c.HandoverTxId, c.Qty, c.Reason }).ToList(),
            nc = gc.Steps.Where(s => s.Pg == "GC").ToDictionary(s => s.Code, s => s.Waiting),
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }

    /// <summary>[GN-FORM1 2026-11-06] drive_form.py D6: Kho nhận hết phiếu đang chờ của PO-SUA (ô Hàng lỗi nhận 0 đạt + n lỗi) — bằng ReceiveAsync thật, người admin.</summary>
    public static async Task<object> RecvKhoAsync(AppDbContext db)
    {
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-SUA");
        var admin = db.Users.List.First(u => u.Username == "admin");
        var svc = new HandoverService(db);
        var done = new List<string>();
        foreach (var t in db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId && !t.IsVoided && t.ToGroupCode == "KHO" && t.Status == "PENDING").ToList())
        {
            var loi = t.ToCell == HandoverCell.Loi;
            var r = await svc.ReceiveAsync(t.HandoverTxId, loi ? 0 : t.QtyIssued, loi ? t.QtyIssued : 0, null, admin.UserId);
            db.Fixup();
            done.Add($"#{t.HandoverTxId} {(r.Ok ? "ok" : r.Error)}");
        }
        return new { done };
    }

    /// <summary>[GN-FORM1 2026-11-05] drive_form.py: phiếu / báo lỗi / Kết quả sửa / vụ của PO-SUA + sổ 5 nhóm + loại lỗi đang dùng.</summary>
    public static async Task<object> DumpFormAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-SUA");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued, t.NgCaseId, t.Notes }).ToList(),
            reports = db.HandoverNgReports.List.Where(b => b.KhPlanDetailId == id && !b.IsVoided)
                .Select(b => new { b.HandoverNgReportId, b.GroupCode, b.SourceCell, b.NC, b.Qty, b.Origin, b.CauseGroupCode, b.NgCaseId }).ToList(),
            reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided)
                .Select(r => new { r.HandoverRepairResultId, r.GroupCode, r.SourceCell, r.NgCaseId, r.QtyPass, r.QtyFail, r.ToNC, r.Notes }).ToList(),
            cases = db.NgCases.List.Where(c => c.KhPlanDetailId == id).Select(c => new { c.NgCaseId, c.OriginKind, c.DetectGroupCode, c.DetectNC, c.CauseGroupCode }).ToList(),
            groups = HandoverLedgerService.GroupCodes.ToDictionary(g => g, g => { var G = row.G(g); return new { G.ChuaLam, G.ConGiao, G.LoiGiu, G.SuaGiu, G.ChoNhan, G.ChoKcs, G.Ton }; }),
            nc = row.G("GC").Steps.ToDictionary(s => s.Code, s => s.Waiting ?? 0),
            row.Balanced,
            neg = HandoverLedgerService.NegativeCells(row).Count,
            cats = db.NgCategories.List.Where(c => c.IsActive).Select(c => c.Code).ToList()
        };
    }

    /// <summary>Kết quả sửa + KCS xác nhận + sổ GC của PO-SUA.</summary>
    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-SUA");
        if (det == null) return new { };
        var row = (await led.GetLedgerForDetailAsync(det.KhPlanDetailId))!;
        var gc = row.G("GC");
        return new
        {
            reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == det.KhPlanDetailId)
                .Select(r => new { r.HandoverRepairResultId, r.GroupCode, r.SourceCell, r.NgCaseId, r.QtyPass, r.QtyFail, r.ToNC, r.CreatedBy, r.IsVoided }).ToList(),
            checks = db.HandoverRepairChecks.List
                .Select(c => new { c.HandoverRepairCheckId, c.HandoverRepairResultId, c.QtyOk, c.QtyNg, c.CheckedBy, c.IsVoided }).ToList(),
            gc = new { gc.SuaGiu, gc.LoiGiu, gc.ChoKcs, gc.ChuaLam, gc.Ton, nc = gc.Steps.ToDictionary(s => s.Code, s => s.Waiting ?? 0) },
            row.Balanced,
            neg = HandoverLedgerService.NegativeCells(row).Count,
            users = db.Users.List.Select(u => new { u.UserId, u.Username }).ToList()
        };
    }

    // ── [TARO-NC-MA][TARO-3T-GOP 2026-11-06] dữ liệu drive_taro.py (POST /harness/taro-seed — không trong seed khởi động, drive.py không đổi):
    //    KH-TARO / PO-TARO 10 chiếc: GC NC01 → HTSP đã nhận 10; Taro 2 NC dùng chung mã: NC 3 (1T chuẩn bị 2 · 2T 1 · 3T1 1 · 3T 2.5) / NC 5 (1T 3 · 2T 0.5 · 3T1 0.5 · 3T 1.5);
    //    khai gộp 3T ← 3T1 (WTS tiêu chuẩn + chụp vào PO); danh mục WTS tiêu chuẩn Taro 3T / 3T1 / 3T2; PIN admin 1111 (trang WTS tiêu chuẩn).
    public static async Task<object> SeedTaroAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-TARO"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-2);
            var plan = new KhPlan { PlanNo = "KH-TARO", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-TARO", PurchaseOrder = "PO-TARO", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            int o = 0;
            foreach (var (nc, code, std) in new[] { ("3", "1T", 2m), ("3", "2T", 1m), ("3", "3T1", 1m), ("3", "3T", 2.5m), ("5", "1T", 3m), ("5", "2T", 0.5m), ("5", "3T1", 0.5m), ("5", "3T", 1.5m) })
                db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++o, NC = nc, WtsTaskCode = code, StepName = "Taro " + code, StandardTime = std, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotWtsGops.Add(new KhPlanRouteSnapshotWtsGop { KhPlanDetailId = d.KhPlanDetailId, GopCode = "3T", PartCode = "3T1", SnapshotAt = t0, SnapshotBy = admin.UserId });
            foreach (var c in new[] { "3T", "3T1", "3T2" })
                if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == c))
                    db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = c, TaskName = "Taro " + c, DisplayOrder = 50, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            db.StandardWtsTaskGops.Add(new StandardWtsTaskGop { GopTaskCode = "3T", PartTaskCode = "3T1", IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-TARO", ViTriDePhoi = "TARO", SoPO = "PO-TARO", PartNo = "PART-TARO", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("taro-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI", toNC: "NC01"), "giao 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "nhận 10");
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", QtyDone = 10, QtyOk = 10, QtyNg = 0,
                StartTime = t0.AddMinutes(10), EndTime = t0.AddMinutes(20), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(20) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "HTSP", 10, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "HTSP nhận");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-TARO");
        return new { det.KhPlanDetailId };
    }

    // [TARO-DUPHONG][TARO-DOI-CHEO-NC 2026-11-07] drive_g5.py: Part Master PART-ML9 thêm Taro 2T ở NC 3 + NC 5, 4T ở NC 3 (+ mã 2T / 4T / 5T trong WTS tiêu chuẩn) (dữ liệu có sẵn, ghi thẳng bảng)
    //   + 1 dòng dự phòng Taro CŨ (như Part "sssssssss" trên DB MES 07/11). Chỉ drive_g5 gọi — seed khởi động không đổi.
    public static async Task<object> SeedG5Async(AppDbContext db)
    {
        var pm = db.PartMasters.List.First(p => p.PartNo == "PART-ML9");
        if (!db.PartTaroSteps.List.Any(t => t.PartId == pm.PartId && t.WtsTaskCode == "2T"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            int o = db.PartTaroSteps.List.Where(t => t.PartId == pm.PartId).Select(t => t.StepOrder).DefaultIfEmpty(0).Max();
            foreach (var c in new[] { "2T", "4T", "5T" })
                if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == c))
                    db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = c, TaskName = "Taro " + c, DisplayOrder = 60, IsActive = true, CreatedAt = DateTime.Now, CreatedBy = admin.UserId });
            foreach (var (nc, code, bk, parent) in new (string, string?, bool, string?)[] { ("3", "2T", false, null), ("3", "4T", false, null), ("5", "2T", false, null), ("3-DP-01", null, true, "3") })
                db.PartTaroSteps.Add(new PartTaroStep { PartId = pm.PartId, StepOrder = ++o, NC = nc, WtsTaskCode = code, IsBackup = bk, ParentNC = parent, StepName = "Taro " + (code ?? nc),
                    StandardTime = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = DateTime.Now.AddDays(-1) });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { pm.PartId };
    }

    public static async Task<object> DumpTaroAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-TARO");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        var logs = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == id && w.ProcessGroup == "TARO").ToList();
        var ids = logs.Select(l => l.WtsLogId).ToHashSet();
        var snap = db.KhPlanRouteSnapshotTaro.List.Where(s => s.KhPlanDetailId == id).ToDictionary(s => s.SnapshotId, s => s.NC + "|" + s.WtsTaskCode);
        return new
        {
            logs = logs.Select(l => new { l.WtsLogId, l.WtsCode, l.StepNC, l.QtyOk, l.QtyNg, l.IsVoided, l.VoidReason }).ToList(),
            stds = db.WtsProductionLogStds.List.Where(x => ids.Contains(x.WtsLogId)).Select(x => new { x.WtsLogId, x.FieldName, x.StdValue, row = x.SnapshotRowId.HasValue && snap.TryGetValue(x.SnapshotRowId.Value, out var k) ? k : null }).ToList(),
            lines = row.G("HTSP").Steps.GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.Select(s => new { s.Ok, s.Ng, s.Waiting, s.GopUnit, s.IsGop }).First()),   // mã chuẩn bị 1T ở 2 NC = 2 dòng cùng mã
            chuaLam = row.G("HTSP").ChuaLam,
            gops = db.StandardWtsTaskGops.List.Select(g => new { g.GopTaskCode, g.PartTaskCode, g.IsActive }).ToList(),
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }

    // [TARO-LAMHO-NC] drive_g6.py: KH-LAMHO / PO-LAMHO 10 chiếc — Gia công NC01 → NC02 → NC03 (H01, loại trừ) → NC04 → NC05 (H01, cuối chuỗi)
    //   như Part 3B-PT1268610 của khách. Taro NC 3: 2T, 3T (gộp ← 3T1), 3T1; NC 5: 2T. Kho → GC 10, WTS NC01 / NC02 10, giao theo NC từ NC02 cho HTSP 10
    //   (HTSP nhận 10), WTS Taro NC 3 (hàm thật WtsSaveService): 2T OK 8, 3T OK 5 NG 1, 3T1 OK 2 → OK bộ = min(8, 5 + 2) = 7, 1 chiếc làm hộ bị lỗi.
    public static async Task<object> SeedG6Async(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-LAMHO"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-LAMHO", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-LAMHO", PurchaseOrder = "PO-LAMHO", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2"), ("NC03", "H01"), ("NC04", "M4"), ("NC05", "H01") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude { KhPlanDetailId = d.KhPlanDetailId, SoMay = "H01", SnapshotAt = t0, SnapshotBy = admin.UserId });
            int o = 0;
            foreach (var (nc, code) in new[] { ("3", "2T"), ("3", "3T"), ("3", "3T1"), ("5", "2T") })
                db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++o, NC = nc, WtsTaskCode = code, StepName = "Taro " + code, StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            db.KhPlanRouteSnapshotWtsGops.Add(new KhPlanRouteSnapshotWtsGop { KhPlanDetailId = d.KhPlanDetailId, GopCode = "3T", PartCode = "3T1", SnapshotAt = t0, SnapshotBy = admin.UserId });
            foreach (var c in new[] { "2T", "3T", "3T1" })
                if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == c))
                    db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = c, TaskName = "Taro " + c, DisplayOrder = 50, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-LH", ViTriDePhoi = "LH", SoPO = "PO-LAMHO", PartNo = "PART-LAMHO", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g6-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI", toNC: "NC01"), "giao 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "nhận 10");
            foreach (var (nc, k) in new[] { ("NC01", 10), ("NC02", 20) })
                db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = nc, QtyDone = 10, QtyOk = 10, QtyNg = 0,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueSpecialAsync(d.KhPlanDetailId, "GC", "HTSP", 10, admin.UserId, HandoverPurpose.HelpSend, "NC02", null, "lam ho Taro NC3"), "giao theo NC");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "HTSP nhận theo NC");
            var wsvc = new WtsSaveService(db, new HandoverLedgerService(db));
            var cat = db.NgCategories.List.First().NgCategoryId;
            int slot = 0;
            foreach (var (code, ok, ng) in new[] { ("2T", 8m, 0m), ("3T", 5m, 1m), ("3T1", 2m, 0m) })
            {
                var end = DateTime.Now.AddMinutes(-30 + (++slot) * 5);
                var grp = new WtsSaveService.WtsSaveGroup { IsGc = false, IsHtsp = true, HandoverGroupCode = "HTSP", ProductionMode = "serial", HtspSubGroup = "Taro", UserId = admin.UserId, WorkDate = DateTime.Today };
                grp.Rows.Add(new WtsSaveService.WtsSaveRow { KhPlanDetailId = d.KhPlanDetailId, PO = "PO-LAMHO", StartTime = end.AddMinutes(-4), EndTime = end, NC = code, WtsCode = code,
                    QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, NgCategoryId = ng > 0 ? cat : null, ActualTimePerPc = 1, ProductionMode = "serial", StepNC = "3" });
                var r = await wsvc.SaveGroupAsync(grp);
                if (!r.Ok) throw new Exception("g6-seed WTS Taro " + code + ": " + r.ErrorMsg);
                db.Fixup();
            }
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-LAMHO");
        return new { det.KhPlanDetailId };
    }

    public static async Task<object> DumpG6Async(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-LAMHO");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued, t.NgCaseId }).ToList(),
            runs = row.LamHoRuns.Select(r => new { r.Key, r.FromNc, r.NextNc, r.HasTaro, r.In, r.Done, r.Ret }).ToList(),
            recs = db.HandoverReceives.List.Where(r => !r.IsVoided && r.Transaction.KhPlanDetailId == id).Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.ToCell, r.NgCaseId }).ToList(),
            gcLoi = row.G("GC").LoiGiu, caseGcLoi = row.Cases.Values.Sum(c => c.HeldAt("GC", HandoverCell.Loi)),
            lamHoGiu = row.G("HTSP").LamHoGiu, lamHoNg = row.LamHoNgHeld("HTSP"),
            htsp = new { row.G("HTSP").ChuaLam, row.G("HTSP").DaLam, row.G("HTSP").LoiGiu },
            lines = row.G("HTSP").Steps.Where(s => !s.IsPrep).Select(s => new { s.Code, s.Waiting, s.LamHoNc }).ToList(),
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
