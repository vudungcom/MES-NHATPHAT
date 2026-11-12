// [GÓI 19 2026-11-12][LS-THOIGIAN] Dữ liệu drive_g19.py phần Lịch sử phiếu (POST /harness/g19-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: KHÁCH 12/11 (sheet test giao nhận TH4, TH6 ý 2); HÙNG 12/11 17:50.
//   KH-G19 / PO-G19 10 chiếc (Workflow ON): GC NC01 → NC02, HTSP Taro T16, KCS K1; đi hết tới KCS; KCS trả GC 2 lỗi (vụ A) rồi 1 lỗi (vụ B);
//   KCS báo lỗi 1 (vụ C); GC gửi HTSP nhờ sửa 2 (vụ A) → HTSP nhận ô Hàng sửa → HTSP trả lại GC ô Hàng lỗi (chưa sửa được, chưa nhận); GC sửa tại nhóm 1 (vụ B, Kết quả sửa).
//   seq = dấu từng chứng từ theo thứ tự lập (phiếu "#id", báo lỗi "Báo lỗi #id", Kết quả sửa "Kết quả sửa #id") — drive_g19 L1 so thứ tự trên trang. Chỉ dùng hàm có từ trước (bản cũ dựng được).
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG19
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G19"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            var plan = new KhPlan { PlanNo = "KH-G19", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G19", PurchaseOrder = "PO-G19", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
            int so = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "2", WtsTaskCode = "T19", StepName = "Taro T19", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "T19"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "T19", TaskName = "Taro T19", DisplayOrder = 64, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            if (!db.NgCategories.List.Any(c => c.IsActive))
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-G19", ViTriDePhoi = "G19", SoPO = "PO-G19", PartNo = "PART-G19", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            var cat = db.NgCategories.List.First(c => c.IsActive);
            var svc = new HandoverService(db); int id = d.KhPlanDetailId;
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g19-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            void Wts(string pg, string? nc, string? code, int k)
                => db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = id, ProcessGroup = pg, NC = nc ?? code, WtsCode = code, QtyDone = 10, QtyOk = 10, QtyNg = 0,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });
            await Must(svc.IssueByCellAsync(id, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "GC nhận 10");
            Wts("GC", "NC01", null, 10); Wts("GC", "NC02", null, 20); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 10, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "HTSP nhận 10");
            Wts("TARO", null, "T19", 40); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(id, "HTSP", "KCS", 10, admin.UserId, "MOI", false, "MOI"), "HTSP giao KCS 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "KCS nhận 10");
            await Must(svc.IssueByCellAsync(id, "KCS", "GC", 2, admin.UserId, "LOI", true, "LOI", ngCategoryId: cat.NgCategoryId), "KCS trả GC 2 lỗi (vụ A)");
            await Must(svc.ReceiveAsync(Last(), 0, 2, null, admin.UserId), "GC nhận 2 ô Hàng lỗi");
            await Must(svc.IssueByCellAsync(id, "KCS", "GC", 1, admin.UserId, "LOI", true, "LOI", ngCategoryId: cat.NgCategoryId), "KCS trả GC 1 lỗi (vụ B)");
            await Must(svc.ReceiveAsync(Last(), 0, 1, null, admin.UserId), "GC nhận 1 ô Hàng lỗi");
            await Must(svc.ReportNgAfterReceiveAsync(id, "KCS", HandoverNgSource.Chua, null, 1, HandoverNgSource.DauVao, "HTSP", "", null, admin.UserId, new List<string> { "KCS" }, cat.NgCategoryId), "KCS báo lỗi 1 (vụ C)");
            var caseA = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && t.FromGroupCode == "KCS").OrderBy(t => t.HandoverTxId).First().NgCaseId;
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 2, admin.UserId, "LOI", false, "SUA", ngCaseId: caseA), "GC gửi HTSP nhờ sửa 2 (vụ A)");
            await Must(svc.ReceiveAsync(Last(), 2, 0, null, admin.UserId), "HTSP nhận ô Hàng sửa 2");
            await Must(svc.IssueByCellAsync(id, "HTSP", "GC", 2, admin.UserId, "SUA", false, "LOI", ngCaseId: caseA), "HTSP trả lại GC 2 (chưa sửa được)");
            var caseB = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && t.FromGroupCode == "KCS").OrderBy(t => t.HandoverTxId).Last().NgCaseId;
            await Must(svc.CreateRepairResultAsync(id, "GC", HandoverCell.Loi, caseB, 1, 0, HandoverLedgerService.RepXong, null, admin.UserId), "GC sửa tại nhóm 1 (vụ B) — Kết quả sửa");
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G19");
        var kcs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId && t.FromGroupCode == "KCS" && t.ToGroupCode == "GC").OrderBy(t => t.HandoverTxId).ToList();
        long a = kcs[0].NgCaseId ?? 0, b = kcs[1].NgCaseId ?? 0;
        var txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == det.KhPlanDetailId && !t.IsVoided).OrderBy(t => t.HandoverTxId).ToList();
        return new
        {
            id = det.KhPlanDetailId, caseA = NgCaseNo.Format(a), caseB = NgCaseNo.Format(b), caseC = NgCaseNo.Format(db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == det.KhPlanDetailId).Select(r => r.NgCaseId ?? 0).FirstOrDefault()),
            nTx = txs.Count,
            nTxA = txs.Count(t => t.NgCaseId == a || db.HandoverReceives.List.Any(r => r.HandoverTxId == t.HandoverTxId && r.NgCaseId == a))   // như trang (TxCaseOk đếm cả lần nhận đã huỷ)
                 + db.HandoverNgReports.List.Count(r => r.KhPlanDetailId == det.KhPlanDetailId && r.NgCaseId == a) + db.HandoverRepairResults.List.Count(r => r.KhPlanDetailId == det.KhPlanDetailId && r.NgCaseId == a),
            nTxB = txs.Count(t => t.NgCaseId == b || db.HandoverReceives.List.Any(r => r.HandoverTxId == t.HandoverTxId && r.NgCaseId == b))   // như trang (TxCaseOk đếm cả lần nhận đã huỷ)
                 + db.HandoverNgReports.List.Count(r => r.KhPlanDetailId == det.KhPlanDetailId && r.NgCaseId == b) + db.HandoverRepairResults.List.Count(r => r.KhPlanDetailId == det.KhPlanDetailId && r.NgCaseId == b),
            last = txs.Last().HandoverTxId, first = txs.First().HandoverTxId,
            seq = txs.Select(t => (At: t.IssuedAt, P: "T" + t.HandoverTxId, K: "#" + t.HandoverTxId + " "))
                .Concat(db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == det.KhPlanDetailId).Select(r => (At: r.CreatedAt, P: "B" + r.HandoverNgReportId, K: "Báo lỗi #" + r.HandoverNgReportId + " ")))
                .Concat(db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == det.KhPlanDetailId).Select(r => (At: r.CreatedAt, P: "R" + r.HandoverRepairResultId, K: "Kết quả sửa #" + r.HandoverRepairResultId + " ")))
                .OrderBy(x => x.At).ThenBy(x => x.P, StringComparer.Ordinal).Select(x => x.K).ToList(),   // trùng giờ: như trang (khoá B / R / T)
        };
    }
}
