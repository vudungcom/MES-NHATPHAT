// [N19-SCEN 2026-11-26] Dữ liệu + đọc trạng thái cho drive_n19.py (luật N19 Cách A — mockup TH1 / TH1b: phôi khách gửi bù; nút "Nhận phôi bù" ở cột Nhận của Kho).
//   POST /harness/n19-seed (dựng khi drive gọi, không nằm trong seed khởi động): KH-N19, 2 PO 10 chiếc, Gia công NC01 → NC02, bằng HandoverService thật:
//     PO-N19A (TH1): Kho → Gia công 9 (nhận đủ); Kho báo 1 phôi lỗi (phôi khách) + Trả khách 1 → Kho phôi còn 0
//     PO-N19B (TH1b): Kho → Gia công 10 (nhận đủ); Gia công nhập WTS NC01 OK 10 (dòng chép thẳng) — bước 1 (báo lỗi + Trả khách) drive bấm trên form
//   GET /harness/n19?po=… : phiếu (ô / NC / vụ) · lần nhận · phiếu bù · vụ · ô sổ + từng NC Gia công + vụ (Bù vào / chờ bù) · cân.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessN19
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-N19"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-N19", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var cat = db.NgCategories.List.First(c => c.IsActive);
            var svc = new HandoverService(db);
            var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("n19-seed " + what + ": " + r.Error); db.Fixup(); }
            int line = 0;
            foreach (var po in new[] { "PO-N19A", "PO-N19B" })
            {
                var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = ++line, PartNo = "4S-PN1114510", PurchaseOrder = po, Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
                db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC01", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 2, NC = "NC02", MachineRegistered = "M2", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = "N19", SoPO = po, PartNo = "4S-PN1114510", CreatedAt = t0, CreatedBy = admin.UserId });
                await db.SaveChangesAsync(); db.Fixup();
                var id = d.KhPlanDetailId;
                if (po == "PO-N19A")
                {
                    await Must(svc.IssueByCellAsync(id, "KHO", "GC", 9, admin.UserId, "MOI", false, "MOI"), po + " giao 9");
                    await Must(svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 9, 0, null, admin.UserId), po + " nhận 9");
                    await Must(svc.ReportNgAfterReceiveAsync(id, "KHO", HandoverNgSource.Phoi, null, 1, HandoverNgSource.DauVao, "KHACH", "", null, admin.UserId, all, cat.NgCategoryId), po + " báo phôi lỗi");
                    var vu = db.NgCases.List.Where(c => c.KhPlanDetailId == id).Max(c => c.NgCaseId);
                    await Must(svc.IssueSpecialAsync(id, "KHO", "KHACH", 1, admin.UserId, HandoverPurpose.CustReturn, reason: "phoi loi", ngCaseId: vu), po + " trả khách 1");
                }
                else
                {
                    await Must(svc.IssueByCellAsync(id, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), po + " giao 10");
                    await Must(svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, admin.UserId), po + " nhận 10");
                    var at = db.HandoverReceives.List.Last().ReceivedAt.AddMinutes(5);
                    db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = id, ProcessGroup = "GC", NC = "NC01", WorkerId = admin.UserId, MachineUsed = "M1",
                        QtyDone = 10, QtyOk = 10, QtyNg = 0, StartTime = at, EndTime = at.AddMinutes(40), CreatedAt = at });
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
        var K = row.G("KHO"); var G = row.G("GC");
        return new
        {
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id)
                .Select(t => new { t.HandoverTxId, t.FromGroupCode, t.ToGroupCode, t.Purpose, t.FromCell, t.ToCell, t.ToNC, t.QtyIssued, t.NgCaseId, t.Status, t.IsVoided }).ToList(),
            recvs = db.HandoverReceives.List.Where(r => db.HandoverTransactions.List.Any(t => t.HandoverTxId == r.HandoverTxId && t.KhPlanDetailId == id))
                .Select(r => new { r.ReceiveId, r.HandoverTxId, r.QtyOk, r.QtyNg, r.ToCell, r.ToNC, r.NgCaseId, r.IsVoided }).ToList(),
            bu = db.HandoverReplacements.List.Where(b => b.KhPlanDetailId == id).Select(b => new { b.ReplacementId, b.NgCaseId, b.Qty, b.Notes, b.IsVoided }).ToList(),
            cases = row.Cases.Values.Where(c => c.Id > 0).Select(c => new { c.Id, c.DetectGroup, c.DetectNc, c.Cause, c.Origin, c.SinhRa, c.TraKhach, c.DaBu, c.BuVao, cho = HandoverLedgerService.BuVuCho(row, c), qua = HandoverLedgerService.BuQuaGc(row, c), lech = c.Lech }).ToList(),
            kho = new { nm = K.NhanMoi, nx = K.NhanLoiXl, gm = K.GiaoMoi, ton = K.Ton, con = K.ConGiao },
            gc = new { nm = G.NhanMoi, nx = G.NhanLoiXl, ton = G.Ton, cho = G.ChoNhan },
            nc = G.Steps.Where(s => s.Waiting != null).ToDictionary(s => s.Code, s => s.Waiting!.Value),
            traKhach = row.TraKhach,
            row.Balanced,
            neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
