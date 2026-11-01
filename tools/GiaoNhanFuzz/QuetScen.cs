// [RA-SOCHUNG] 31/10/2026 — kịch bản `quet`: tự kiểm công cụ quét DB tools/QuetSo (QuetCore.ScanAsync) trên DB giả.
//   Q1: PO đi đủ đường bằng hàm thật (Kho → GC → WTS → HTSP) → quét KHÔNG báo gì.
//   Q2: cài lỗi cố ý (giống dữ liệu cũ ngoài đời) → quét phải bắt đúng loại:
//       dòng WTS QtyDone ≠ OK + NG · WTS vượt trần (OK + NG > nhận vào) · PO không cân · ô âm (giao 16 khi NC sau chưa có WTS).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using QuetSo;

public static class QuetScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static AppDbContext Seed(bool workflow)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(adminG);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = adminG, GroupId = 1, PIN = "1111" });
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-QUET", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 16, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "2V" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "3V" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[Q1] PO đi đúng đường bằng hàm thật → quét không báo gì");
        {
            var db = Seed(true); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            var k = await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            Ok(k.Success, "Kho nhận 16 phôi", k.Error);
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1")).Ok, "Kho → GC 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "GC nhận 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 16, 0, 1)).Ok, "WTS NC1 OK 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 16, 0, 1)).Ok, "WTS NC2 OK 16");
            Ok((await svc.IssueByCellAsync(1, "GC", "HTSP", 16, 1, "MOI", false, "MOI")).Ok, "GC → HTSP 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "HTSP nhận 16");
            var r = await QuetCore.ScanAsync(db, led);
            Ok(r.PoCount == 1 && r.Findings.Count == 0, "Quét: 1 PO, 0 phát hiện", string.Join(" | ", r.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
        }

        Console.WriteLine("[Q2] Cài lỗi cố ý (dữ liệu cũ) → quét phải bắt đúng loại");
        {
            var db = Seed(false); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1);
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 16, 0, 1)).Ok, "WTS NC1 OK 16");
            // NC2 có WTS lúc giao rồi bị huỷ → giống PO 0003655603 (GC "đã làm chưa giao" −16)
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 16, 0, 1);
            Ok((await svc.IssueByCellAsync(1, "GC", "HTSP", 16, 1, "MOI", false, "MOI")).Ok, "GC → HTSP 16");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1);
            var log2 = db.WtsProductionLogs.List.First(x => x.WtsLogId == w2.LogId); log2.IsVoided = true;   // ghi thẳng như dữ liệu cũ (không qua K5)
            // dòng WTS Bavia 2V: QtyDone 16 nhưng OK 15 NG 0 → số lệch; 3V: OK 20 > nhận vào 16 → vượt trần
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 900, KhPlanDetailId = 1, ProcessGroup = "BAVIA", WtsCode = "2V", QtyDone = 16, QtyOk = 15, QtyNg = 0, WorkerId = 1, StartTime = DateTime.Now, EndTime = DateTime.Now });
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 901, KhPlanDetailId = 1, ProcessGroup = "BAVIA", WtsCode = "3V", QtyDone = 20, QtyOk = 20, QtyNg = 0, WorkerId = 1, StartTime = DateTime.Now, EndTime = DateTime.Now });
            db.Fixup();
            var before = (db.WtsProductionLogs.List.Count, db.HandoverTransactions.List.Count, db.HandoverReceives.List.Count, db.WtsProductionLogs.List.Count(x => x.IsVoided));
            var r = await QuetCore.ScanAsync(db, led);
            var after = (db.WtsProductionLogs.List.Count, db.HandoverTransactions.List.Count, db.HandoverReceives.List.Count, db.WtsProductionLogs.List.Count(x => x.IsVoided));
            string Has(string kind) => string.Join(" | ", r.Findings.Where(f => f.Kind == kind).Select(f => f.Detail));
            Console.WriteLine("     " + string.Join("\n     ", r.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.WtsSoLech && f.Detail.Contains("#900")), "Bắt dòng WTS QtyDone ≠ OK + NG (#900)", Has(QuetCore.WtsSoLech));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.WtsVuot && f.Detail.Contains("3V")), "Bắt WTS vượt trần Bavia 3V", Has(QuetCore.WtsVuot));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.OAm), "Bắt ô âm (GC đã làm chưa giao âm)", Has(QuetCore.OAm));
            Ok(!r.Findings.Any(f => f.Kind == QuetCore.WtsSoLech && !f.Detail.Contains("#900")), "Không báo nhầm dòng WTS đúng", Has(QuetCore.WtsSoLech));
            Ok(r.Findings.All(f => f.Po == "PO1" && f.Plan == "KH-QUET"), "Mỗi phát hiện ghi đúng PO / KH");
            var sw = new StringWriter(); QuetCore.Write(r, sw, "GIA");
            Ok(sw.ToString().Contains("── PO PO1 · KH KH-QUET · Workflow OFF"), "File kết quả liệt kê theo PO");
            Ok(before == after, "Quét không ghi gì (số dòng WTS / phiếu / lần nhận / dòng huỷ giữ nguyên)", $"{before} → {after}");
        }
        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }
}
