// [KIEM-CHUA 2026-11-03] Giả lỗi máy chủ cho drive_pageerr.py (trang Giao nhận [PAGE-ERR][PAGE-ERR2][PAGE-ERR2-PO]).
//   POST /harness/fail?kind=save&n=1    → n lần SaveChanges tới ném lỗi (giao dịch chưa ghi, phải rollback, DB không đổi)
//   POST /harness/fail?kind=reload&n=1  → sau n lần giao dịch GHI XONG (Commit), câu đọc async ĐẦU TIÊN ném lỗi (= bước tải lại số trên trang)
//   POST /harness/fail?kind=off         → tắt hết
//   POST /harness/pe-seed               → phiếu KH-PE: PO-PE1 / PO-PE2 (Kho còn phôi 20, chưa giao) — dựng khi drive gọi, không nằm trong seed khởi động
//   GET  /harness/fail                  → đếm: saveThrown, reloadThrown, commits
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

public static class HarnessFail
{
    static int _saveN, _reloadN, _saveThrown, _reloadThrown, _commits; static bool _armed;
    static readonly object _l = new();
    public static object Set(string kind, int n)
    {
        lock (_l)
        {
            if (kind == "save") _saveN = n;
            else if (kind == "reload") _reloadN = n;
            else
            {   // tắt hẳn: gỡ cả 3 móc tĩnh (không để trơ) — rà độc lập 03/11
                _saveN = 0; _reloadN = 0; _armed = false;
                DbContext.OnSave = null; StubTransaction.OnCommit = null; EntityFrameworkQueryableExtensions.OnQuery = null;
                return Stat();
            }
            DbContext.OnSave = () => { lock (_l) { if (_saveN > 0) { _saveN--; _saveThrown++; return new InvalidOperationException("GIA-LOI-SQL-LUU"); } return null; } };
            StubTransaction.OnCommit = () => { lock (_l) { _commits++; if (_reloadN > 0) { _reloadN--; _armed = true; } } };
            EntityFrameworkQueryableExtensions.OnQuery = () =>
            {
                bool t; lock (_l) { t = _armed; if (t) { _armed = false; _reloadThrown++; } }
                if (t) throw new InvalidOperationException("GIA-LOI-SQL-TAI-LAI");
            };
        }
        return Stat();
    }
    public static object Stat() { lock (_l) return new { saveThrown = _saveThrown, reloadThrown = _reloadThrown, commits = _commits, saveLeft = _saveN, reloadLeft = _reloadN, armed = _armed }; }

    public static async Task<object> SeedPeAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-PE"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var cust = db.Customers.List.First();
            var t0 = new DateTime(2026, 10, 29, 6, 0, 0);
            var plan = new KhPlan { PlanNo = "KH-PE", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = true, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            int line = 0;
            foreach (var po in new[] { "PO-PE1", "PO-PE2" })
            {
                var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = ++line, PartNo = "PART-A", PurchaseOrder = po, Quantity = 20, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
                db.KhPlanDetails.Add(d); await db.SaveChangesAsync();
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 20, SoLuongKeHoach = 20, NgayNhanPhoi = t0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = "PE", SoPO = po, PartNo = "PART-A", CreatedAt = t0, CreatedBy = admin.UserId });
                await db.SaveChangesAsync();
            }
            db.Fixup();
        }
        return db.KhPlanDetails.List.Where(d => d.PurchaseOrder.StartsWith("PO-PE")).Select(d => new { d.PurchaseOrder, d.KhPlanDetailId }).ToList();
    }
}
