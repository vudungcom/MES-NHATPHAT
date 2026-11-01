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

        // ── [KHE-3][KHE-6 30/10] 2 người Kế hoạch có PIN cho drive_khe.py (thêm CUỐI → không đổi id / kết quả các kịch bản trước) ──
        var gKh = new UserGroup { GroupCode = "KHTEST", GroupName = "Kế hoạch", IsActive = true, Permissions = PermsVE((SystemPermissions.KeHoach, true, true)) };
        db.UserGroups.Add(gKh); await db.SaveChangesAsync();
        db.Users.AddRange(
            new User { Username = "khe01", FullName = "Kế hoạch A", PasswordHash = hash, GroupId = gKh.GroupId, IsActive = true, PIN = "1111" },
            new User { Username = "khe02", FullName = "Kế hoạch B", PasswordHash = hash, GroupId = gKh.GroupId, IsActive = true, PIN = "1111" });
        await db.SaveChangesAsync();
        // ── [H10 30/10] phiếu KH riêng KH-H10 (thêm CUỐI, không chung phiếu KH-TEST → không đổi kịch bản trước): Kho → Gia công 10,
        //    Gia công nhận 8 + NG 2 → phiếu trả tự lập → Kho đã nhận lại 2. drive_h10.py thử huỷ dây chuyền lần nhận gốc trên trang Giao nhận.
        var planH = new KhPlan { PlanNo = "KH-H10", PlanDate = T0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = T0 };
        db.KhPlans.Add(planH); await db.SaveChangesAsync();
        var dH = new KhPlanDetail { KhPlanId = planH.KhPlanId, LineNo = 1, PartNo = "PART-A", PurchaseOrder = "PO-H10", Quantity = 10, Unit = "EA", STD = T0.AddDays(10), CreatedAt = T0 };
        db.KhPlanDetails.Add(dH); await db.SaveChangesAsync();
        db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = dH.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = T0, OrderVatLieu = "OVL-H10", ViTriDePhoi = "A1", SoPO = "PO-H10", PartNo = "PART-A" });
        db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = dH.KhPlanDetailId, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
        await db.SaveChangesAsync(); db.Fixup();
        var gH = await svc.IssueByCellAsync(dH.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI", null, "NC1", "seed h10");
        if (!gH.Ok) throw new Exception("seed h10 giao: " + gH.Error);
        var rH = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 8, 2, null, admin.UserId, ngCategoryId: db.NgCategories.List.First().NgCategoryId);
        if (!rH.Ok) throw new Exception("seed h10 nhận: " + rH.Error);
        var backH = db.HandoverTransactions.List.Last();
        var rbH = await svc.ReceiveAsync(backH.HandoverTxId, 0, 2, null, admin.UserId);
        if (!rbH.Ok) throw new Exception("seed h10 nhận phiếu trả: " + rbH.Error);

        // ── [WTS-PANEL-QTY 31/10] phiếu KH riêng KH-CS / PO-CS (thêm CUỐI → không đổi id / kết quả các kịch bản trước) cho drive_cungso.py:
        //    giống PO 0003655603 khách báo — Workflow OFF, GC NC1 + NC1b dự phòng (gộp NC1), NC2 chưa WTS mà đã giao 16 sang HTSP (ô âm GC),
        //    Bavia 2V OK 15 NG 1, 3V dòng cũ không có QtyOk (sổ lấy QtyDone − NG), 1V chuẩn bị.
        var planC = new KhPlan { PlanNo = "KH-CS", PlanDate = T0, CustomerId = cust.CustomerId, ApplyWorkflow = false, CreatedBy = admin.UserId, CreatedAt = T0 };
        db.KhPlans.Add(planC); await db.SaveChangesAsync();
        var dC = new KhPlanDetail { KhPlanId = planC.KhPlanId, LineNo = 1, PartNo = "PART-A", PurchaseOrder = "PO-CS", Quantity = 16, Unit = "EA", STD = T0.AddDays(10), CreatedAt = T0 };
        db.KhPlanDetails.Add(dC); await db.SaveChangesAsync();
        int idC = dC.KhPlanDetailId;
        db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = idC, IsActive = true, SoLuongThucNhan = 16, SoLuongKeHoach = 16, NgayNhanPhoi = T0, OrderVatLieu = "OVL-CS", ViTriDePhoi = "A1", SoPO = "PO-CS", PartNo = "PART-A" });
        db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = idC, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
        db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = idC, StepOrder = 2, NC = "NC1b", IsBackup = true, ParentNC = "NC1", MachineRegistered = "M3", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
        db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = idC, StepOrder = 3, NC = "NC2", MachineRegistered = "M2", SetupTime = 4, MachiningTime = 3, InspectionTime = 1, PreparationTime = 8, TrialRunTime = 2 });
        db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia { KhPlanDetailId = idC, StepOrder = 1, NC = "1V", StepName = "Chuẩn bị", StandardTime = 3 });
        db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia { KhPlanDetailId = idC, StepOrder = 2, NC = "2V", StepName = "Kiểm tra bề mặt", StandardTime = 0.2m });
        db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia { KhPlanDetailId = idC, StepOrder = 3, NC = "3V1", StepName = "Bavia bao quanh", StandardTime = 2 });
        db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia { KhPlanDetailId = idC, StepOrder = 4, NC = "3V", StepName = "Bavia tất cả", StandardTime = 5.5m });
        await db.SaveChangesAsync(); db.Fixup();
        await Move(idC, "KHO", "GC", 16, "NC1");
        void WtsC(string pg, string? nc, string? code, decimal done, decimal? ok, decimal? ng, int worker, string? m, decimal? act)
        {
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = idC, ProcessGroup = pg, NC = nc, WtsCode = code,
                HtspSubGroup = pg == "BAVIA" ? "Bavia" : null, WorkerId = worker, QtyDone = done, QtyOk = ok, QtyNg = ng, StartTime = T0, EndTime = T0.AddMinutes(30), CreatedAt = T0, CaId = ca.CaId,
                MachineUsed = m, ProductionMode = pg == "GC" ? "serial" : null, ActualTimePerPc = act, SetupTime_Actual = pg == "GC" ? 5 : null, MachiningTime_Actual = pg == "GC" ? 2 : null, InspectionTime_Actual = pg == "GC" ? 1 : null });
            db.Fixup();
        }
        WtsC("GC", "NC1", null, 10, 10, 0, admin.UserId, "M1", null);
        WtsC("GC", "NC1b", null, 6, 6, 0, admin.UserId, "M3", null);
        WtsC("GC", "NC2", null, 16, 16, 0, admin.UserId, "M2", null);
        await Move(idC, "GC", "HTSP", 16);
        // dòng WTS NC2 bị huỷ sau khi đã giao (dữ liệu giống PO khách: giao 16 sang HTSP mà NC2 không có WTS) → GC "đã làm chưa giao" −16
        var w2 = db.WtsProductionLogs.List.Last(); w2.IsVoided = true; w2.VoidReason = "seed: giống PO khách"; db.Fixup();
        WtsC("BAVIA", null, "1V", 0, 0, 0, htsp, null, 3);
        WtsC("BAVIA", null, "2V", 16, 15, 1, htsp, null, 0.25m);
        WtsC("BAVIA", null, "3V", 4, null, null, htsp, null, 5);   // dòng cũ: QtyOk / QtyNg null
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
            // [KHE-3][KHE-6][KHE-7 30/10] phiếu KH + dòng PO + lịch sử (drive_khe.py)
            khplan = db.KhPlans.List.Select(p => new { p.KhPlanId, p.Notes, Recv = D(p.ReceivedDate), p.ApplyWorkflow }),
            khdet  = db.KhPlanDetails.List.Select(d => new { d.KhPlanDetailId, d.KhPlanId, d.Quantity, d.Location, d.PartNotes, d.Priority }),
            khlog  = db.KhPlanDetailChangeLogs.List.Select(l => new { l.KhPlanDetailId, l.FieldName, l.OldValue, l.NewValue, l.ChangedBy, l.Reason }),
            // [H10 30/10] phiếu giao nhận + lần nhận (drive_h10.py)
            gntx  = db.HandoverTransactions.List.OrderBy(t => t.HandoverTxId).Select(t => new { t.HandoverTxId, t.KhPlanDetailId, t.FromGroupCode, t.ToGroupCode, t.Purpose, t.QtyIssued, t.Status, t.IsVoided, t.VoidReason, t.SourceReceiveId }),
            gnrec = db.HandoverReceives.List.OrderBy(r => r.ReceiveId).Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.IsVoided, r.VoidReason, r.VoidedBy }),
            // [ML17-XN 01/11] dòng nghỉ (bản cũ không có bảng → rỗng; đọc bằng reflection để bản cũ vẫn biên dịch)
            brk = ((System.Collections.IEnumerable?)(db.GetType().GetProperty("WtsBreakWorkLogs")?.GetValue(db) as dynamic)?.List ?? new List<object>())
                .Cast<object>().Select(x => { dynamic d = x; return new { Wts = (long?)d.WtsLogId, Act = (long?)d.ActivityLogId, Bs = D((DateTime)d.BreakStart), Be = D((DateTime)d.BreakEnd), Ov = (int)d.OverlapMin, Wk = (int)d.WorkedMin, Ca = (int?)d.CaId, Bf = (bool)d.IsBackfill }; }),
        };
    }
}
