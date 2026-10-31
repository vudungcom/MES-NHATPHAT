// [ISO-FIX] 31/10/2026 — kịch bản `iso`: các hàm GHI số lượng (Kho nhận phôi, lập phiếu, nhận, WTS, báo lỗi sau nhận, huỷ báo lỗi,
//   huỷ phần chưa nhận, huỷ phiếu, xoá WTS) gọi HÀM THẬT của service và phải:
//   1) không giao dịch nào xin IsolationLevel.Serializable (SQL thật: mức Serializable rò qua kết nối pool sang câu đọc / giao dịch sau —
//      T1 MES_TEST đo ReadCommitted 902 · Serializable 396 trước khi sửa);
//   2) mọi lần ghi chứng từ số lượng nằm trong giao dịch ĐÃ khoá MES_PO_ (khoá PO thay cho Serializable);
//   3) sổ PO cân, không ô âm.
//   Kèm 2 phép "kiểm cái kiểm": cố ý xin Serializable / cố ý ghi không khoá → bộ đếm phải bắt được.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Facade = Microsoft.EntityFrameworkCore.DatabaseFacade;

public static class IsoScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static AppDbContext Seed()
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(adminG);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = adminG, GroupId = 1, PIN = "1111" });
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-ISO", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "2T", WtsTaskCode = "2T" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    static void Reset()
    {
        Facade.IsoDefault = Facade.IsoSerializable = Facade.IsoOther = 0;
        Facade.WriteNoPoLock = 0; Facade.WriteNoPoLockWhere.Clear();
    }

    public static async Task<int> Run()
    {
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        Console.WriteLine("[I1] ISO-FIX — mọi hàm ghi số lượng: không Serializable, ghi sau khoá MES_PO_, sổ cân");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            Reset(); Facade.TrackPoLock = true;
            try
            {
                var k = await kho.SaveKhoInputAsync(1, DateTime.Now, 40, "OVL", "A1", null, null, null, "nhan phoi", 1);
                Ok(k.Success, "Kho nhận 40 phôi (SaveKhoInputAsync)", k.Error);
                var i1 = await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI", null, "NC1");
                Ok(i1.Ok, "Lập phiếu Kho → GC 40 (IssueWithPurposeOnceAsync — trước Serializable)", i1.Error);
                var tx1 = db.HandoverTransactions.List.Last().HandoverTxId;
                var r1 = await svc.ReceiveAsync(tx1, 40, 0, null, 1);
                Ok(r1.Ok, "GC nhận 40", r1.Error);
                var w1 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, 1);
                Ok(w1.Ok, "WTS GC NC1 OK 10", w1.Err);
                var b1 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauRa, null, "x", null, 1, all, ngCategoryId: 1);
                Ok(b1.Ok, "Báo lỗi sau nhận GC NC1 2 (ReportNgAfterReceiveOnceAsync — trước Serializable)", b1.Error);
                var repId = db.HandoverNgReports.List.Last().HandoverNgReportId;
                var vb = await svc.VoidNgReportAsync(repId, "huy thu", 1, all);
                Ok(vb.Ok, "Huỷ phiếu báo lỗi", vb.Error);
                var i2 = await svc.IssueByCellAsync(1, "GC", "HTSP", 5, 1, "MOI", false, "MOI");
                Ok(i2.Ok, "Lập phiếu GC → HTSP 5", i2.Error);
                var tx2 = db.HandoverTransactions.List.Last().HandoverTxId;
                var r2 = await svc.ReceiveAsync(tx2, 3, 0, null, 1);
                Ok(r2.Ok, "HTSP nhận 3 / 5", r2.Error);
                var c2 = await svc.CloseRemainderAsync(tx2, "dong phan con", 1, all);
                Ok(c2.Ok, "Huỷ phần chưa nhận (2) của phiếu", c2.Error);
                var i3 = await svc.IssueByCellAsync(1, "GC", "HTSP", 2, 1, "MOI", false, "MOI");
                Ok(i3.Ok, "Lập phiếu GC → HTSP 2", i3.Error);
                var vi = await svc.VoidIssueAsync(db.HandoverTransactions.List.Last().HandoverTxId, "huy thu", 1, all);
                Ok(vi.Ok, "Huỷ phiếu chờ nhận", vi.Error);
                var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 1, 0, 1);
                Ok(w2.Ok && w2.LogId != null, "WTS GC NC1 OK 1", w2.Err);
                var vw = await WtsSvcHelp.VoidAsync(db, w2.LogId!.Value, 1, 1);
                Ok(vw.Ok, "Xoá dòng WTS vừa lưu", vw.Err);
                var vr = await svc.VoidReceiveAsync(db.HandoverReceives.List.Last(r => !r.IsVoided).ReceiveId, "huy thu", 1, all);
                Ok(vr.Ok, "Huỷ lần nhận HTSP 3", vr.Error);
            }
            finally { Facade.TrackPoLock = false; }

            Ok(Facade.IsoSerializable == 0 && Facade.IsoOther == 0,
               "Không giao dịch nào xin Serializable / mức khác (đều BeginTransactionAsync() mặc định; RCSI có tác dụng hay không phải đo bằng T1 trên SQL thật)",
               $"mặc định {Facade.IsoDefault} · Serializable {Facade.IsoSerializable} · mức khác {Facade.IsoOther}");
            var nTx = Facade.IsoDefault + Facade.IsoSerializable + Facade.IsoOther;
            Ok(nTx >= 12, "Đủ số giao dịch đã đi qua bộ đếm (≥ 12 hàm ghi ở trên)", $"{nTx} giao dịch");
            Ok(Facade.WriteNoPoLock == 0, "Mọi lần thêm chứng từ / UPDATE dòng WTS nằm trong giao dịch đã khoá ĐÚNG PO đó (MES_PO_<id>; sửa trực tiếp đối tượng EF chưa đếm được — xem rà code)",
               Facade.WriteNoPoLock == 0 ? null : string.Join("; ", Facade.WriteNoPoLockWhere.Distinct()));
            var row = await led.GetLedgerForDetailAsync(1);
            Ok(row != null && row.Balanced && HandoverLedgerService.NegativeCells(row).Count == 0, "Sổ PO cân, không ô âm",
               row == null ? "không có sổ" : $"Phôi {row.Phoi} · ô âm {HandoverLedgerService.NegativeCells(row).Count}");
        }

        Console.WriteLine("[I2] Kiểm cái kiểm — bộ đếm phải bắt được Serializable và ghi không khoá PO");
        {
            var db = Seed();
            Reset(); Facade.TrackPoLock = true;
            try
            {
                await using (var t = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)) { }
                Ok(Facade.IsoSerializable == 1, "Cố ý xin Serializable → bộ đếm = 1", $"{Facade.IsoSerializable}");
                await using (var t = await db.Database.BeginTransactionAsync())
                {
                    db.HandoverTransactions.Add(new HandoverTransaction { KhPlanDetailId = 1, FromGroupCode = "KHO", ToGroupCode = "GC", QtyIssued = 1, Status = "PENDING", IssuedBy = 1, IssuedAt = DateTime.Now, CreatedAt = DateTime.Now });
                    await db.SaveChangesAsync();
                }
                Ok(Facade.WriteNoPoLock == 1, "Cố ý ghi phiếu trong giao dịch chưa khoá MES_PO_ → bộ đếm = 1", $"{Facade.WriteNoPoLock}");
                // khoá PO 2 nhưng ghi phiếu của PO 1 → vẫn phải bị đếm (khoá nhầm PO)
                await using (var t = await db.Database.BeginTransactionAsync())
                {
                    await PoGuard.LockAsync(db, 2);
                    db.HandoverTransactions.Add(new HandoverTransaction { KhPlanDetailId = 1, FromGroupCode = "KHO", ToGroupCode = "GC", QtyIssued = 1, Status = "PENDING", IssuedBy = 1, IssuedAt = DateTime.Now, CreatedAt = DateTime.Now });
                    await db.SaveChangesAsync();
                }
                Ok(Facade.WriteNoPoLock == 2, "Cố ý khoá PO 2 rồi ghi phiếu PO 1 → bộ đếm = 2 (bắt khoá nhầm PO)", $"{Facade.WriteNoPoLock} · {string.Join("; ", Facade.WriteNoPoLockWhere)}");
            }
            finally { Facade.TrackPoLock = false; Reset(); }
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
