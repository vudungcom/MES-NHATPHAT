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
}
