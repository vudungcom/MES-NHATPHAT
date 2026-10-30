// [WTS-SVC harness] Dữ liệu giả cố định để bấm trang WTS thật (bản cũ / bản mới cùng số nhập → DB phải giống hệt).
using System.Text.Json;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using MES.Web.Constants;

public static class HarnessSeed
{
    static DateTime T0 = new DateTime(2026, 10, 29, 6, 0, 0);   // mốc giờ cố định cho dữ liệu seed

    static string Perms(params string[] codes) => JsonSerializer.Serialize(codes.Select(c => new GroupPermissionSetting { SectionCode = c, CanView = true, CanEdit = true }).ToList());

    public static async Task RunAsync(AppDbContext db)
    {
        // ── nhóm + người dùng ──
        var gHtsp = new UserGroup { GroupCode = "HTSP", GroupName = "Hoàn thiện", IsActive = true, Permissions = Perms(SystemPermissions.WtsHtsp) };
        var gKcs  = new UserGroup { GroupCode = "KCS",  GroupName = "KCS",        IsActive = true, Permissions = Perms(SystemPermissions.WtsKcs) };
        var gPkg  = new UserGroup { GroupCode = "PKG",  GroupName = "Đóng gói",   IsActive = true, Permissions = Perms(SystemPermissions.WtsDongGoi) };
        var gGc   = new UserGroup { GroupCode = "GC",   GroupName = "Gia công",   IsActive = true, Permissions = Perms(SystemPermissions.WtsGc) };
        db.UserGroups.AddRange(gHtsp, gKcs, gPkg, gGc); await db.SaveChangesAsync();
        var hash = PasswordHasher.Hash("123456");
        db.Users.AddRange(
            new User { Username = "gc01",   FullName = "Công nhân GC",   PasswordHash = hash, GroupId = gGc.GroupId,   IsActive = true },
            new User { Username = "htsp01", FullName = "Công nhân HTSP", PasswordHash = hash, GroupId = gHtsp.GroupId, IsActive = true },
            new User { Username = "kcs01",  FullName = "Công nhân KCS",  PasswordHash = hash, GroupId = gKcs.GroupId,  IsActive = true },
            new User { Username = "pkg01",  FullName = "Công nhân PKG",  PasswordHash = hash, GroupId = gPkg.GroupId,  IsActive = true });
        await db.SaveChangesAsync();
        var admin = db.Users.List.First(u => u.Username == "admin");

        // ── ca làm việc ──
        var ca = new CaLamViec { TenCa = "Ca 1", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(17, 0), IsActive = true, SortOrder = 1 };
        db.CaLamViecs.Add(ca); await db.SaveChangesAsync();
        db.CaLamViec_NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { CaId = ca.CaId, NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0), GhiChu = "Nghỉ trưa" });
        // ── máy ──
        foreach (var m in new[] { "M1", "M2", "M3" })
            db.ThietBis.Add(new ThietBi { SoMay = m, BoPhan = "GC", MaMay = m, TenMay = "Máy " + m, LoaiMay = "CNC", IsActive = true, CreatedBy = admin.UserId });
        // ── loại lỗi ──
        db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 });
        db.NgCategories.Add(new NgCategory { Code = "BM-XUOC", Name = "Xước", SortOrder = 2, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 });
        await db.SaveChangesAsync();

        // ── 3 PO cùng Part (snapshot giống nhau) ──
        var cust = db.Customers.List.First();
        var plan = new KhPlan { PlanNo = "KH-TEST", PlanDate = T0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = T0 };
        db.KhPlans.Add(plan); await db.SaveChangesAsync();
        int line = 0;
        foreach (var (po, qty) in new[] { ("PO1", 40m), ("PO2", 40m), ("PO3", 30m) })
        {
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = ++line, PartNo = "PART-A", PurchaseOrder = po, Quantity = qty, Unit = "EA", STD = T0.AddDays(10), CreatedAt = T0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int id = d.KhPlanDetailId;
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = id, IsActive = true, SoLuongThucNhan = (int)qty, SoLuongKeHoach = (int)qty, NgayNhanPhoi = T0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = "A1", SoPO = po, PartNo = "PART-A" });
            var m1 = new KhPlanRouteSnapshotMachining { KhPlanDetailId = id, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 };
            db.KhPlanRouteSnapshotMachining.Add(m1); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotMachiningTimings.Add(new KhPlanRouteSnapshotMachiningTiming { MachiningSnapshotId = m1.SnapshotId, KhPlanDetailId = id, SoMay = "M3", SetupTime = 6, MachiningTime = 2.5m, InspectionTime = 1, PreparationTime = 12, TrialRunTime = 4 });
            var m2 = new KhPlanRouteSnapshotMachining { KhPlanDetailId = id, StepOrder = 2, NC = "NC2", MachineRegistered = "M2", SetupTime = 4, MachiningTime = 3, InspectionTime = 1, PreparationTime = 8, TrialRunTime = 2 };
            db.KhPlanRouteSnapshotMachining.Add(m2); await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = id, StepOrder = 1, NC = "T1", WtsTaskCode = "T1", StepName = "Taro M6", StandardTime = 1.5m });
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = id, StepOrder = 2, NC = "1T", WtsTaskCode = "1T", StepName = "Chuẩn bị taro", StandardTime = 15 });
            db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia { KhPlanDetailId = id, StepOrder = 1, NC = "B1", StepName = "Bavia", StandardTime = 0.5m });
            db.KhPlanRouteSnapshotWashing.Add(new KhPlanRouteSnapshotWashing { KhPlanDetailId = id, StepOrder = 1, NC = "R1", StepName = "Rửa", StandardTime = 0.2m });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = id, StepOrder = 1, NC = "K1", StepName = "Kiểm tra", StandardTime = 1 });
            db.KhPlanRouteSnapshotPackaging.Add(new KhPlanRouteSnapshotPackaging { KhPlanDetailId = id, StepOrder = 1, NC = "P1", StepName = "Đóng gói", StandardTime = 0.3m });
            await db.SaveChangesAsync();
            db.KhPlanRouteSnapshotStdConfirms.Add(new KhPlanRouteSnapshotStdConfirm { KhPlanDetailId = id, SnapshotTable = StdTrack.Table.Machining, SnapshotRowId = m2.SnapshotId, FieldName = StdTrack.Field.Setup, IsConfirmed = true, ConfirmedBy = admin.UserId, ConfirmedByName = "Administrator", ConfirmedAt = T0, SyncSource = StdTrack.Source.Copy, SyncedAt = T0 });
            var t1 = db.KhPlanRouteSnapshotTaro.List.First(x => x.KhPlanDetailId == id && x.NC == "T1");
            db.KhPlanRouteSnapshotStdConfirms.Add(new KhPlanRouteSnapshotStdConfirm { KhPlanDetailId = id, SnapshotTable = StdTrack.Table.Taro, SnapshotRowId = t1.SnapshotId, FieldName = StdTrack.Field.Standard, IsConfirmed = true, ConfirmedBy = admin.UserId, ConfirmedByName = "Administrator", ConfirmedAt = T0, SyncSource = StdTrack.Source.Copy, SyncedAt = T0 });
            await db.SaveChangesAsync();
        }
        db.Fixup();

        var svc = new HandoverService(db);
        async Task Move(int id, string from, string to, decimal q, string? toNc = null)
        {
            var r = await svc.IssueByCellAsync(id, from, to, q, admin.UserId, "MOI", false, "MOI", null, toNc, "seed");
            if (!r.Ok) throw new Exception($"seed giao {from}->{to} PO{id}: {r.Error}");
            var tx = db.HandoverTransactions.List.Last();
            var rr = await svc.ReceiveAsync(tx.HandoverTxId, q, 0, null, admin.UserId);
            if (!rr.Ok) throw new Exception($"seed nhận {from}->{to} PO{id}: {rr.Error}");
        }
        void RawWts(int id, string pg, string? nc, string? code, decimal ok, int worker)
        {
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = id, ProcessGroup = pg, NC = nc, WtsCode = code,
                WorkerId = worker, QtyDone = ok, QtyOk = ok, QtyNg = 0, StartTime = T0, EndTime = T0.AddMinutes(30), CreatedAt = T0, CaId = ca.CaId,
                MachineUsed = pg == "GC" ? (nc == "NC1" ? "M1" : "M2") : null, ProductionMode = pg == "GC" ? "serial" : null });
            db.Fixup();
        }
        var ids = db.KhPlanDetails.List.OrderBy(d => d.LineNo).Select(d => d.KhPlanDetailId).ToList();
        // PO1: Kho → GC nhận 40 (NC1) — thử GC Hàng loạt / Chạy thử / sửa / xoá / NG
        await Move(ids[0], "KHO", "GC", 40, "NC1");
        // PO2: GC xong 40 → HTSP (T1 40, B1 40, R1 20) → KCS 20 (K1 10) → PKG 10 — thử KCS / PKG / HTSP R1 còn 20
        await Move(ids[1], "KHO", "GC", 40, "NC1");
        RawWts(ids[1], "GC", "NC1", null, 40, admin.UserId); RawWts(ids[1], "GC", "NC2", null, 40, admin.UserId);
        await Move(ids[1], "GC", "HTSP", 40);
        var htsp = db.Users.List.First(u => u.Username == "htsp01").UserId;
        RawWts(ids[1], "TARO", null, "T1", 40, htsp); RawWts(ids[1], "BAVIA", null, "B1", 40, htsp); RawWts(ids[1], "WASHING", null, "R1", 20, htsp);
        await Move(ids[1], "HTSP", "KCS", 20);
        RawWts(ids[1], "KCS", null, "K1", 10, db.Users.List.First(u => u.Username == "kcs01").UserId);
        await Move(ids[1], "KCS", "PKG", 10);
        // PO3: GC xong 30 → HTSP nhận 30, chưa WTS — thử HTSP nhiều mã CV
        await Move(ids[2], "KHO", "GC", 30, "NC1");
        RawWts(ids[2], "GC", "NC1", null, 30, admin.UserId); RawWts(ids[2], "GC", "NC2", null, 30, admin.UserId);
        await Move(ids[2], "GC", "HTSP", 30);
        // ── [RS-C1][RS-C2][RS-E2 29/10] người dùng cho drive_quyen.py (thêm SAU dữ liệu WTS → không đổi id / kết quả kịch bản WTS) ──
        static string PermsVE(params (string c, bool v, bool e)[] ps) => JsonSerializer.Serialize(ps.Select(p => new GroupPermissionSetting { SectionCode = p.c, CanView = p.v, CanEdit = p.e }).ToList());
        var dmCodes = new[] { SystemPermissions.ThietBi, SystemPermissions.KhachHang, SystemPermissions.Dao, SystemPermissions.DoGa, SystemPermissions.WtsMaster, SystemPermissions.WtsMapping };
        var gXem = new UserGroup { GroupCode = "XEMDM", GroupName = "Xem danh mục", IsActive = true, Permissions = PermsVE(dmCodes.Select(c => (c, true, false)).ToArray()) };
        var gDm  = new UserGroup { GroupCode = "SUADM", GroupName = "Sửa danh mục", IsActive = true, Permissions = PermsVE(dmCodes.Select(c => (c, true, true)).ToArray()) };
        var gGn  = new UserGroup { GroupCode = "GNGC", GroupName = "Giao nhận GC", IsActive = true, Permissions = PermsVE(
            (SystemPermissions.HandoverGc, true, true), (SystemPermissions.HandoverKho, true, false), (SystemPermissions.HandoverHtsp, true, false)) };
        db.UserGroups.AddRange(gXem, gDm, gGn); await db.SaveChangesAsync();
        db.Users.AddRange(
            new User { Username = "xem01", FullName = "Xem danh mục", PasswordHash = hash, GroupId = gXem.GroupId, IsActive = true, PIN = "1111" },
            new User { Username = "dm01",  FullName = "Sửa danh mục", PasswordHash = hash, GroupId = gDm.GroupId,  IsActive = true, PIN = "1111" },
            new User { Username = "gn01",  FullName = "Giao nhận GC", PasswordHash = hash, GroupId = gGn.GroupId,  IsActive = true, PIN = "1111" });
        await db.SaveChangesAsync();
        // Part cho [RS-E2]: 1 dòng Bavia, mục C đang Xác nhận OK
        var pe2 = new PartMaster { PartNo = "PART-E2", PartName = "Thử E2", IsActive = true, IsPlanConfirmed = true, IsHtspConfirmed = true, CustomerId = cust.CustomerId };
        db.PartMasters.Add(pe2); await db.SaveChangesAsync();
        db.PartBaviaSteps.Add(new PartBaviaStep { PartId = pe2.PartId, StepOrder = 1, NC = "B1", StepName = "Bavia", StandardTime = 0.5m, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 });
        db.PartWashingSteps.Add(new PartWashingStep { PartId = pe2.PartId, StepOrder = 1, NC = "R1", StepName = "Rửa", StandardTime = 0.2m, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 });
        await db.SaveChangesAsync();
        // [SUBROW-DEL-UNDO 30/10] Part PART-30: NC 1 máy M1 + 1 dòng máy đồng dạng M3 (thử xoá / khôi phục dòng đồng dạng)
        var p30 = new PartMaster { PartNo = "PART-30", PartName = "Thử mục 30", IsActive = true, CustomerId = cust.CustomerId };
        db.PartMasters.Add(p30); await db.SaveChangesAsync();
        var s30 = new PartMachiningStep { PartId = p30.PartId, StepOrder = 1, NC = "1", MachineRegistered = "M1", SetupTime = 1, MachiningTime = 7, InspectionTime = 6, PreparationTime = 10, TrialRunTime = 20, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 };
        db.PartMachiningSteps.Add(s30); await db.SaveChangesAsync();
        db.PartMachiningTimings.Add(new PartMachiningTiming { StepId = s30.StepId, SoMay = "M3", DisplayOrder = 1, SetupTime = 1, MachiningTime = 6, InspectionTime = 5, PreparationTime = 10, TrialRunTime = 20, IsActive = true, CreatedBy = admin.UserId, CreatedAt = T0 });
        await db.SaveChangesAsync();
        // [K6-NOTRUN 30/10] PO30 của PART-30 tạo TRƯỚC khi có quy trình (snapshot rỗng) — lưu Part Master OK thì phải tự có quy trình
        db.KhPlanDetails.Add(new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 99, PartNo = "PART-30", PurchaseOrder = "PO30", Quantity = 5, Unit = "EA", STD = T0.AddDays(10), CreatedAt = T0 });
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.Fixup();
        Console.WriteLine($"[HARNESS] seed xong: {db.KhPlanDetails.List.Count} PO, {db.WtsProductionLogs.List.Count} WTS, {db.HandoverTransactions.List.Count} phiếu");
    }

    /// <summary>Chụp các bảng WTS ghi ra JSON (bỏ cột giờ máy / id để so bản cũ – mới).</summary>
    public static object Dump(AppDbContext db)
    {
        static string D(DateTime? t) => t?.ToString("yyyy-MM-dd HH:mm") ?? "";
        return new
        {
            wts = db.WtsProductionLogs.List.OrderBy(w => w.WtsLogId).Select(w => new
            {
                w.WtsLogId, w.KhPlanDetailId, w.ProcessGroup, w.NC, w.WtsCode, w.HtspSubGroup, w.WorkerId, w.MachineUsed,
                w.QtyDone, w.QtyOk, w.QtyNg, w.NgReason, w.NgCaseId, Start = D(w.StartTime), End = D(w.EndTime), w.Notes, w.ExtraMinutes, w.CaId, w.IsOvertime,
                w.SetupTime_Actual, w.MachiningTime_Actual, w.InspectionTime_Actual, w.PreparationTime_Actual, w.TrialRunTime_Actual,
                w.ProductionMode, w.ActualTimePerPc, w.IsActualManual, w.IsVoided, w.VoidReason, w.VoidedBy,
            }),
            stds = db.WtsProductionLogStds.List.OrderBy(s => s.Id).Select(s => new
            {
                s.Id, s.WtsLogId, s.KhPlanDetailId, s.FieldName, s.StdValue, s.IsConfirmed, s.ConfirmedBy, s.ConfirmedByName, Cfa = D(s.ConfirmedAt), s.SnapshotTable, s.SnapshotRowId, s.SyncSource, s.SyncedBy, s.Note,
            }),
            extras = db.WtsExtraItems.List.OrderBy(e => e.Id).Select(e => new { e.Id, e.WtsLogId, e.ActivityLogId, e.WtsCode, e.WtsName, e.Minutes, e.Notes }),
            cases = db.NgCases.List.OrderBy(c => c.NgCaseId).Select(c => new { c.NgCaseId, c.KhPlanDetailId, c.NgCategoryId, c.OriginKind, c.DetectGroupCode, c.DetectNC, c.CauseGroupCode, c.CreatedBy }),
            acts = db.WorkerActivityLogs.List.OrderBy(a => a.ActivityLogId).Select(a => new { a.ActivityLogId, a.WorkerId, a.WtsCode, a.WtsName, Start = D(a.StartTime), End = D(a.EndTime), a.IsVoided }),
            raw = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.ToList(),
            // [RS-E2] cờ xác nhận Part (drive_quyen.py) · [RS-C1] máy / khách / mã CV (đếm)
            parts = db.PartMasters.List.OrderBy(p => p.PartId).Select(p => new { p.PartNo, p.IsPlanConfirmed, p.IsMachiningConfirmed, p.IsHtspConfirmed, p.IsKcsConfirmed, p.IsPkgConfirmed }),
            bavia = db.PartBaviaSteps.List.OrderBy(b => b.StepId).Select(b => new { b.PartId, b.NC, b.StepName, b.IsActive }),
            thietbi = db.ThietBis.List.Where(t => t.IsActive).Select(t => t.SoMay),
            timings = db.PartMachiningTimings.List.OrderBy(t => t.TimingId).Select(t => new { t.StepId, t.SoMay, t.IsActive }),
            snapm = db.KhPlanRouteSnapshotMachining.List.Select(m => new { m.KhPlanDetailId, m.NC, m.MachineRegistered }),
            podet = db.KhPlanDetails.List.Select(d => new { d.KhPlanDetailId, d.PurchaseOrder }),
        };
    }
}
