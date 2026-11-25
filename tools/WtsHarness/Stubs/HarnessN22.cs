// [N22-SCEN 2026-11-25] Dữ liệu + đọc trạng thái cho drive_n22.py (luật N22: Sửa phiếu / Sửa lần nhận / Đổi loại lỗi — mockup D7).
//   POST /harness/n22-seed (dựng khi drive gọi, không nằm trong seed khởi động): KH-N22, 3 PO 20 chiếc, Gia công NC01 → NC02, bằng HandoverService thật:
//     PO-N22A: Kho → Gia công 20, Gia công nhận 15 OK + 5 NG (vụ mới, phiếu trả tự lập 5 chờ Kho nhận) — Sửa lần nhận (D7 bước 2a)
//     PO-N22B: Kho → Gia công 20 nhận đủ; Gia công lập phiếu Hàng lỗi công đoạn trước 5 → Kho (chờ nhận) — Sửa phiếu + Đổi loại lỗi (D7 bước 1, 3)
//     PO-N22C: Kho → Gia công 20 nhận đủ; Gia công nhập WTS NC01 sau lần nhận (dòng chép thẳng) — Sửa lần nhận xám (D7 bước 2b)
//   GET /harness/n22?po=… : phiếu / lần nhận (cả đã huỷ) / vụ + sổ cân.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessN22
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-N22"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-N22", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var cat = db.NgCategories.List.First(c => c.IsActive);
            var svc = new HandoverService(db);
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("n22-seed " + what + ": " + r.Error); db.Fixup(); }
            int line = 0;
            foreach (var po in new[] { "PO-N22A", "PO-N22B", "PO-N22C" })
            {
                var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = ++line, PartNo = "PART-N22", PurchaseOrder = po, Quantity = 20, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
                db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M2", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 20, SoLuongKeHoach = 20, NgayNhanPhoi = t0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = "N22", SoPO = po, PartNo = "PART-N22", CreatedAt = t0, CreatedBy = admin.UserId });
                await db.SaveChangesAsync(); db.Fixup();
                await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "KHO", "GC", 20, admin.UserId, "MOI", false, "MOI"), po + " giao 20");
                var tx = db.HandoverTransactions.List.Last().HandoverTxId;
                if (po == "PO-N22A") { await Must(svc.ReceiveAsync(tx, 15, 5, null, admin.UserId, ngCategoryId: cat.NgCategoryId), po + " nhận 15 + 5"); continue; }
                await Must(svc.ReceiveAsync(tx, 20, 0, null, admin.UserId), po + " nhận 20");
                var rcv = db.HandoverReceives.List.Last();
                if (po == "PO-N22B")
                    await Must(svc.IssueByCellAsync(d.KhPlanDetailId, "GC", "KHO", 5, admin.UserId, "LOI", true, "LOI", fromNC: "NC01", ngCategoryId: cat.NgCategoryId), po + " Hàng lỗi công đoạn trước 5");
                else
                {
                    var at = rcv.ReceivedAt.AddMinutes(5);
                    db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = d.KhPlanDetailId, ProcessGroup = "GC", NC = "NC01", WorkerId = admin.UserId, MachineUsed = "M1",
                        QtyDone = 3, QtyOk = 3, QtyNg = 0, StartTime = at, EndTime = at.AddMinutes(20), CreatedAt = at });
                    await db.SaveChangesAsync(); db.Fixup();
                }
            }
        }
        return new { ok = true };
    }

    public static async Task<object> StateAsync(AppDbContext db, HandoverLedgerService led, string po)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == po);
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id)
                .Select(t => new { t.HandoverTxId, t.FromGroupCode, t.ToGroupCode, t.Purpose, t.QtyIssued, t.NgCaseId, t.SourceReceiveId, t.Status, t.IsVoided, t.VoidReason, t.Notes }).ToList(),
            recvs = db.HandoverReceives.List.Where(r => db.HandoverTransactions.List.Any(t => t.HandoverTxId == r.HandoverTxId && t.KhPlanDetailId == id))
                .Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.NgCaseId, r.IsVoided, r.Notes, r.ReceivedAt }).ToList(),
            cases = db.NgCases.List.Where(c => c.KhPlanDetailId == id).Select(c => new { c.NgCaseId, c.NgCategoryId, c.CauseGroupCode }).ToList(),
            logs = db.KhPlanDetailChangeLogs.List.Where(l => l.KhPlanDetailId == id).Select(l => new { l.FieldName, l.OldValue, l.NewValue, l.Reason }).ToList(),
            cats = db.NgCategories.List.Where(c => c.IsActive).Select(c => new { c.NgCategoryId, c.Label }).ToList(),
            row.Balanced,
            neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
