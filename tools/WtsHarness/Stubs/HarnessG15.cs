// [GÓI 15 2026-11-10][SUA-XL-GIU][SUA-KHONG-LUI][XL-VUOT-CHAN][XL-TRA-LAI] Dữ liệu drive_g15.py (POST /harness/g15-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: KHÁCH 10/11 (vòng 1 câu 2.1 A + 2.2 A — ví dụ trong doc); HÙNG 10/11 19:22.
//   KH-G15 (Workflow ON) / PO-G15 10 chiếc: GC NC01 (M1) → NC02 (M2), HTSP Taro T15 ở NC 2, KCS K1.
//   Kho → GC 10 (đã nhận), WTS NC01 10 + NC02 10, GC → HTSP 10 (đã nhận), WTS Taro T15 10, HTSP → KCS 10 (đã nhận);
//   KCS phát hiện 2 chiếc lỗi do Gia công → phiếu KCS → GC ô Hàng lỗi 2 (vụ mới, GC đã nhận). Không có Kết quả sửa.
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG15
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G15"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-G15", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G15", PurchaseOrder = "PO-G15", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "2", WtsTaskCode = "T15", StepName = "Taro T15", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "T15"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "T15", TaskName = "Taro T15", DisplayOrder = 61, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-G15", ViTriDePhoi = "G15", SoPO = "PO-G15", PartNo = "PART-G15", CreatedAt = t0, CreatedBy = admin.UserId });
            var cat = db.NgCategories.List.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
                await db.SaveChangesAsync();
                cat = db.NgCategories.List.First(c => c.IsActive);
            }
            await db.SaveChangesAsync(); db.Fixup();
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g15-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "GC nhận 10");
            foreach (var (nc, k) in new[] { ("NC01", 10), ("NC02", 20) })
                db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = nc, QtyDone = 10, QtyOk = 10, QtyNg = 0,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "HTSP", 10, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "HTSP nhận 10");
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "TARO", WtsCode = "T15", NC = "T15", QtyDone = 10, QtyOk = 10, QtyNg = 0,
                StartTime = t0.AddMinutes(40), EndTime = t0.AddMinutes(45), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(45) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "HTSP", "KCS", 10, admin.UserId, "MOI", false, "MOI"), "HTSP giao KCS 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "KCS nhận 10");
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KCS", "GC", 2, admin.UserId, "LOI", true, "LOI", ngCategoryId: cat.NgCategoryId), "KCS trả GC 2 lỗi");
            await Must(svc.ReceiveAsync(Last(), 0, 2, null, admin.UserId), "GC nhận 2 ô Hàng lỗi");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G15");
        return new { det.KhPlanDetailId };
    }

    /// <summary>KCS xác nhận Kết quả sửa mới nhất (đạt hết) — bước giữa của drive_g15 (trang KCS xác nhận đã có drive_sua K4x).</summary>
    public static async Task<object> ConfirmLastAsync(AppDbContext db)
    {
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G15");
        var rp = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == det.KhPlanDetailId && !r.IsVoided).OrderBy(r => r.HandoverRepairResultId).Last();
        var kcs = db.Users.List.FirstOrDefault(u => u.Username == "kcsg15");   // người KCS giao nhận (quyền Sửa Giao nhận KCS), khác người ghi Kết quả sửa (admin)
        if (kcs == null)
        {
            var g = new UserGroup { GroupCode = "GNKCS15", GroupName = "Giao nhận KCS (G15)", IsActive = true,
                Permissions = System.Text.Json.JsonSerializer.Serialize(new[] { new GroupPermissionSetting { SectionCode = SystemPermissions.HandoverKcs, CanView = true, CanEdit = true } }.ToList()) };
            db.UserGroups.Add(g); await db.SaveChangesAsync();
            db.Users.Add(new User { Username = "kcsg15", FullName = "KCS giao nhận G15", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true });
            await db.SaveChangesAsync();
            kcs = db.Users.List.First(u => u.Username == "kcsg15");
        }
        var r = await new HandoverService(db).ConfirmRepairAsync(rp.HandoverRepairResultId, rp.QtyPass, 0, null, kcs.UserId);
        db.Fixup();
        return new { r.Ok, r.Error, rp.HandoverRepairResultId };
    }

    /// <summary>Nhận đủ phiếu đang chờ mới nhất của PO-G15 (hàng tốt; phiếu vào ô Hàng lỗi thì nhận ở ô Hàng lỗi).</summary>
    public static async Task<object> RecvLastAsync(AppDbContext db)
    {
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G15");
        var tx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId && !t.IsVoided).OrderBy(t => t.HandoverTxId).Last();
        var admin = db.Users.List.First(u => u.Username == "admin");
        bool loi = tx.ToCell == HandoverCell.Loi;   // phiếu vào ô Hàng lỗi: nhận ở ô Hàng lỗi (như seed)
        var r = await new HandoverService(db).ReceiveAsync(tx.HandoverTxId, loi ? 0 : tx.QtyIssued, loi ? tx.QtyIssued : 0, null, admin.UserId);
        db.Fixup();
        return new { r.Ok, r.Error, tx.HandoverTxId };
    }

    /// <summary>[SUA-OFF-LUI] Tắt Workflow của PO-G15 (bước X7 drive_g15).</summary>
    public static async Task<object> OffAsync(AppDbContext db)
    {
        var plan = db.KhPlans.List.First(p => p.PlanNo == "KH-G15");
        plan.ApplyWorkflow = false; await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true };
    }

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G15");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        object G(string g) { var x = row.G(g); return new { x.ChuaLam, x.ConGiao, x.LoiGiu, x.XlGiu, x.ChoKcs, x.DaLam }; }
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued, t.NgCaseId }).ToList(),
            reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided)
                .Select(r => new { r.HandoverRepairResultId, r.GroupCode, r.SourceCell, r.NgCaseId, r.QtyPass, r.QtyFail, r.ToNC }).ToList(),
            gc = G("GC"), htsp = G("HTSP"), kcs = G("KCS"), pkg = G("PKG"),
            nc = row.G("GC").Steps.ToDictionary(s => s.Code, s => s.Waiting),
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
