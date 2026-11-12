// [KHO-TARO-THANG][KTT-PHOILOI][KTT-CHI-TARO 2026-11-12] Dữ liệu drive_ktt.py (POST /harness/ktt-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: KHÁCH 12/11 (câu 3.3 a = A, b = A); HÙNG 12/11 14:00 OK kế hoạch.
//   KH-KTT / PO-KTT 10 chiếc (Workflow ON): Gia công NC01 = máy H04 (loại trừ, ĐẦU chuỗi, Taro T91 làm NC01) → NC02 (M1), KCS K1; Kho nhận 10 phôi, chưa giao.
//   Chỉ dùng thành viên có ở bản cũ (TraKho đọc bằng reflection) để mk.sh dựng được cả bản master cũ (drive_ktt bản cũ phải SAI).
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessKtt
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-KTT"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            admin.PIN ??= "1111";
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-KTT", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-KTT", PurchaseOrder = "PO-KTT", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "H04"), ("NC02", "M1") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude { KhPlanDetailId = d.KhPlanDetailId, SoMay = "H04", SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "1", WtsTaskCode = "T91", StepName = "Taro T91", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "T91"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "T91", TaskName = "Taro T91", DisplayOrder = 63, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            if (!db.NgCategories.List.Any(c => c.IsActive))
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-KTT", ViTriDePhoi = "KTT", SoPO = "PO-KTT", PartNo = "PART-KTT", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { id = Id(db), cat = db.NgCategories.List.First(c => c.IsActive).Code };
    }

    static int Id(AppDbContext db) => db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-KTT").KhPlanDetailId;

    /// <summary>HTSP nhận phiếu đang chờ mới nhất của PO-KTT: ok chiếc tốt + ng chiếc NG lúc nhận (loại lỗi đầu tiên).</summary>
    public static async Task<object> RecvAsync(AppDbContext db, int ok, int ng)
    {
        var id = Id(db);
        var tx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided && t.Status != "COMPLETED" && t.ToGroupCode == "HTSP").OrderBy(t => t.HandoverTxId).LastOrDefault();
        if (tx == null) return new { Ok = false, Error = "không có phiếu chờ HTSP nhận", HandoverTxId = 0L };
        var admin = db.Users.List.First(u => u.Username == "admin");
        int? cat = ng > 0 ? db.NgCategories.List.First(c => c.IsActive).NgCategoryId : null;
        var r = await new HandoverService(db).ReceiveAsync(tx.HandoverTxId, ok, ng, ng > 0 ? "phoi loi" : null, admin.UserId, null, null, null, cat);
        db.Fixup();
        return new { r.Ok, r.Error, tx.HandoverTxId };
    }

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var id = Id(db);
        var row = (await led.GetLedgerForDetailAsync(id))!;
        static object? P(object o, string n) => o.GetType().GetProperty(n)?.GetValue(o);
        var vao = row.G("GC").Steps.FirstOrDefault(s => HandoverLedgerService.IsVao(s.Code));
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.FromNC, t.ToNC, t.FromCell, t.ToCell, t.QtyIssued, t.Status, t.NgCaseId, t.SourceReceiveId }).ToList(),
            recs = db.HandoverReceives.List.Where(r => !r.IsVoided && r.Transaction.KhPlanDetailId == id).Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.ToCell, r.NgCaseId }).ToList(),
            cases = db.NgCases.List.Where(c => c.KhPlanDetailId == id).Select(c => new { c.NgCaseId, c.CauseGroupCode, c.DetectGroupCode, c.DetectNC }).ToList(),
            runs = row.LamHoRuns.Select(r => new { r.Key, r.FromNc, r.NextNc, r.HasTaro, r.In, r.Ret, r.Ng, TraKho = P(r, "TraKho"), Held = P(r, "Held") }).ToList(),
            kho = new { row.G("KHO").ConGiao, row.G("KHO").LoiGiu, row.G("KHO").ChoNhan },
            gcVao = vao == null ? null : new { vao.Code, vao.In, vao.Ok, vao.HelpOut, vao.Waiting },
            htsp = new { row.G("HTSP").ChuaLam, row.G("HTSP").LamHoGiu, row.G("HTSP").ChoNhan, row.G("HTSP").LoiGiu },
            balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
