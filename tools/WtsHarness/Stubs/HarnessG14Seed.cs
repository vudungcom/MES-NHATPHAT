// [GÓI 14 2026-11-11][KHACH-GUI-LAI] Dữ liệu drive_g14.py (phần dựng — chỉ dùng hàm có từ trước Gói 14, bản cũ dựng được) (POST /harness/g14-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: KHÁCH 10/11 vòng 1 câu 1a A + 1b A; HÙNG 11/11 14:39 duyệt thiết kế + SQL 15_KHBU (trần chặn cả Workflow OFF).
//   KH-G14 / PO-G14 10 phôi (Workflow ON): Kho báo 2 phôi lỗi (phôi khách) → trả khách 2 (vụ A); Kho giao GC 8 (NC01), GC nhận,
//   WTS NC01 OK 7 NG 1 (vụ B Gia công làm hỏng) → GC huỷ 1. Kho phôi còn 0.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG14Seed
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G14"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var t0 = DateTime.Now.AddHours(-3);
            var cust = db.Customers.List.First();
            var p = new KhPlan { PlanNo = "KH-G14", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(p); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = p.KhPlanId, LineNo = 1, PartNo = "PART-G14", PurchaseOrder = "PO-G14", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-G14", ViTriDePhoi = "G14", SoPO = "PO-G14", PartNo = "PART-G14", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var cat = db.NgCategories.List.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
                await db.SaveChangesAsync();
                cat = db.NgCategories.List.First(c => c.IsActive);
            }
            var svc = new HandoverService(db);
            var all = HandoverLedgerService.GroupCodes.ToList();
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g14-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            var id = d.KhPlanDetailId;
            await Must(svc.ReportNgAfterReceiveAsync(id, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", cat.Label, null, admin.UserId, all, ngCategoryId: cat.NgCategoryId), "Kho báo 2 phôi lỗi");
            var vA = db.NgCases.List.Max(x => x.NgCaseId);
            await Must(svc.IssueSpecialAsync(id, "KHO", "KHACH", 2, admin.UserId, HandoverPurpose.CustReturn, reason: cat.Label, ngCaseId: vA), "Kho trả khách 2");
            await Must(svc.IssueByCellAsync(id, "KHO", "GC", 8, admin.UserId, "MOI", false, "MOI", toNC: "NC01"), "Kho giao GC 8");
            await Must(svc.ReceiveAsync(Last(), 8, 0, null, admin.UserId), "GC nhận 8");
            var vB = await NgCaseService.CreateCaseAsync(db, id, cat, NgOrigin.Wts, "GC", "NC01", "GC", admin.UserId);
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = id, ProcessGroup = "GC", NC = "NC01", QtyDone = 8, QtyOk = 7, QtyNg = 1, NgCaseId = vB.NgCaseId,
                StartTime = t0.AddMinutes(10), EndTime = t0.AddMinutes(15), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(15) });
            await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueSpecialAsync(id, "GC", "HUY", 1, admin.UserId, HandoverPurpose.Scrap, reason: cat.Label, ngCaseId: vB.NgCaseId), "GC huỷ 1");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G14");
        var cases = db.NgCases.List.Where(c => c.KhPlanDetailId == det.KhPlanDetailId).OrderBy(c => c.NgCaseId).Select(c => c.NgCaseId).ToList();
        return new { id = det.KhPlanDetailId, vA = cases[0], vB = cases[1] };
    }

}
