// [GÓI 16 2026-11-11][SUA-QUA-KCS][SUA-HO-TRA-NGUOC][XL-TRA-NOILOI][XL-CHU-KCS] Dữ liệu drive_g16.py (POST /harness/g16-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: KHÁCH 11/11 ("OK trả bộ phận báo lỗi, NG trả bộ phận làm lỗi" · "KCS chỉ là trung gian đánh giá tạm hàng sửa"); HÙNG 11/11 15:21 + 15:34.
//   KH-G16 / PO-G16 10 chiếc (Workflow ON): GC NC01 → NC02, HTSP Taro T16, KCS K1; đi hết tới KCS; KCS phát hiện 2 lỗi do Gia công → GC nhận ô Hàng lỗi 2.
//   KH-G16B / PO-G16B 10 chiếc (Workflow ON): Kho → GC 10, WTS NC01 OK 8 NG 2 (vụ Gia công làm hỏng ở NC01); chưa giao đi.
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG16
{
    static async Task<KhPlanDetail> NewPo(AppDbContext db, string plan, string po, User admin, DateTime t0)
    {
        var cust = db.Customers.List.First();
        var p = new KhPlan { PlanNo = plan, PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
        db.KhPlans.Add(p); await db.SaveChangesAsync();
        var d = new KhPlanDetail { KhPlanId = p.KhPlanId, LineNo = 1, PartNo = "PART-" + po, PurchaseOrder = po, Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
        db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
        int so = 0;
        foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
            db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
        db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "2", WtsTaskCode = "T16", StepName = "Taro T16", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
        db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
        db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = po, SoPO = po, PartNo = "PART-" + po, CreatedAt = t0, CreatedBy = admin.UserId });
        await db.SaveChangesAsync(); db.Fixup();
        return d;
    }

    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G16"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var t0 = DateTime.Now.AddHours(-3);
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "T16"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "T16", TaskName = "Taro T16", DisplayOrder = 62, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            var cat = db.NgCategories.List.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
                await db.SaveChangesAsync();
                cat = db.NgCategories.List.First(c => c.IsActive);
            }
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g16-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            void Wts(int id, string pg, string? nc, string? code, int okq, int ng, int k, long? cs = null)
                => db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = id, ProcessGroup = pg, NC = nc ?? code, WtsCode = code, QtyDone = okq + ng, QtyOk = okq, QtyNg = ng, NgCaseId = cs,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });
            // PO-G16
            var d = await NewPo(db, "KH-G16", "PO-G16", admin, t0);
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "GC nhận 10");
            Wts(d.KhPlanDetailId, "GC", "NC01", null, 10, 0, 10); Wts(d.KhPlanDetailId, "GC", "NC02", null, 10, 0, 20);
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "HTSP", 10, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "HTSP nhận 10");
            Wts(d.KhPlanDetailId, "TARO", null, "T16", 10, 0, 40);
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "HTSP", "KCS", 10, admin.UserId, "MOI", false, "MOI"), "HTSP giao KCS 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "KCS nhận 10");
            await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KCS", "GC", 2, admin.UserId, "LOI", true, "LOI", ngCategoryId: cat.NgCategoryId), "KCS trả GC 2 lỗi");
            await Must(svc.ReceiveAsync(Last(), 0, 2, null, admin.UserId), "GC nhận 2 ô Hàng lỗi");
            // PO-G16B — vụ WTS NG ở NC01 (Gia công làm hỏng, Gia công là nơi phát hiện)
            var e = await NewPo(db, "KH-G16B", "PO-G16B", admin, t0);
            await Must(svc.IssueByCellAsync(e.KhPlanDetailId, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "B: Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "B: GC nhận 10");
            var vu = await NgCaseService.CreateCaseAsync(db, e.KhPlanDetailId, cat, NgOrigin.Wts, "GC", "NC01", "GC", admin.UserId);
            Wts(e.KhPlanDetailId, "GC", "NC01", null, 8, 2, 10, vu.NgCaseId);
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { g16 = Id(db, "PO-G16"), g16b = Id(db, "PO-G16B") };
    }

    static int Id(AppDbContext db, string po) => db.KhPlanDetails.List.First(x => x.PurchaseOrder == po).KhPlanDetailId;

    /// <summary>KCS xác nhận Kết quả sửa mới nhất của PO (đạt hết) — người KCS khác người ghi (admin).</summary>
    public static async Task<object> ConfirmLastAsync(AppDbContext db, string po)
    {
        var id = Id(db, po);
        var rp = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided).OrderBy(r => r.HandoverRepairResultId).Last();
        var kcs = db.Users.List.FirstOrDefault(u => u.Username == "kcsg16");
        if (kcs == null)
        {
            var g = new UserGroup { GroupCode = "GNKCS16", GroupName = "Giao nhận KCS (G16)", IsActive = true,
                Permissions = System.Text.Json.JsonSerializer.Serialize(new[] { new GroupPermissionSetting { SectionCode = SystemPermissions.HandoverKcs, CanView = true, CanEdit = true } }.ToList()) };
            db.UserGroups.Add(g); await db.SaveChangesAsync();
            db.Users.Add(new User { Username = "kcsg16", FullName = "KCS giao nhận G16", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true });
            await db.SaveChangesAsync();
            kcs = db.Users.List.First(u => u.Username == "kcsg16");
        }
        var r = await new HandoverService(db).ConfirmRepairAsync(rp.HandoverRepairResultId, rp.QtyPass, 0, null, kcs.UserId);
        db.Fixup();
        return new { r.Ok, r.Error, rp.HandoverRepairResultId };
    }

    /// <summary>Nhận đủ phiếu đang chờ mới nhất của PO (ô bên giao chỉ định; ô Hàng lỗi / Hàng sửa thì nhận ở ô đó).</summary>
    public static async Task<object> RecvLastAsync(AppDbContext db, string po)
    {
        var id = Id(db, po);
        var tx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided && t.Status != "COMPLETED").OrderBy(t => t.HandoverTxId).Last();
        var admin = db.Users.List.First(u => u.Username == "admin");
        bool loi = tx.ToCell == HandoverCell.Loi;
        var r = await new HandoverService(db).ReceiveAsync(tx.HandoverTxId, loi ? 0 : tx.QtyIssued, loi ? tx.QtyIssued : 0, null, admin.UserId);
        db.Fixup();
        return new { r.Ok, r.Error, tx.HandoverTxId };
    }

    /// <summary>Bật / tắt Workflow của phiếu KH chứa PO (dùng cho cả drive_nhannc — PO-NNC).</summary>
    public static async Task<object> WorkflowAsync(AppDbContext db, string po, bool on)
    {
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == po);
        var plan = db.KhPlans.List.First(p => p.KhPlanId == det.KhPlanId);
        plan.ApplyWorkflow = on; await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true, on };
    }

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led, string po)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == po);
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        object G(string g) { var x = row.G(g); return new { x.ChuaLam, x.ConGiao, x.LoiGiu, x.SuaGiu, x.XlGiu, x.ChoKcs, x.DaLam }; }
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued, t.NgCaseId, t.Status }).ToList(),
            reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided)
                .Select(r => new { r.HandoverRepairResultId, r.GroupCode, r.SourceCell, r.NgCaseId, r.QtyPass, r.QtyFail, r.ToNC }).ToList(),
            wts = db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == id && !w.IsVoided),
            gc = G("GC"), htsp = G("HTSP"), kcs = G("KCS"), pkg = G("PKG"),
            nc = row.G("GC").Steps.ToDictionary(s => s.Code, s => s.Waiting),
            ncok = row.G("GC").Steps.ToDictionary(s => s.Code, s => s.Ok + s.OkSua),
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
