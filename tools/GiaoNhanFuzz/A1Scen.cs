// [A1-SCEN 2026-11-15] Kịch bản `a1` — viết lại từ mô tả project doc claude/quet-fable/2026-11-15-agent-a1-inout-khoa.md
//   (bản gốc lượt 2 quét Fable chỉ nằm ở /tmp sandbox, đã mất). 4 ca đo điểm ghi số lượng / giao dịch / khoá / đồng thời bằng hàm THẬT
//   của service trên DB giả (EfStub): A1-1 khuôn ghi (1 giao dịch · khoá MES_PO_ trước đọc sổ · Notify sau Commit · không ghi ngoài khoá);
//   A1-2 WTS lưu lại cùng nhóm vừa lưu → [A1-01-ROWSAVED] từ chối (code cũ: 2 dòng KCS); A1-3 Tách PO khi SL gốc vừa đổi dưới khoá → [A1-02]
//   chặn (code cũ: tách qua, Σ mới ≠ SL gốc); A1-4 Kho nhận phôi gọi 2 lần → idempotent (1 dòng Kho, không thêm log).
//   Nguồn: CLAUDE quét Fable 14/11 (GĐ3 A.1 / A.2); Hùng 15/11 15:10 OK Gói 22.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Facade = Microsoft.EntityFrameworkCore.DatabaseFacade;

public static class A1Scen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    /// <summary>PO1 (id 1, SL 40, phôi 40 đã nhận) + PO2 (id 2, SL 10, trắng — để tách); người 1 ADMIN.</summary>
    static AppDbContext Seed(bool workflow = true, bool phoi = true)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(adminG);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = adminG, GroupId = 1, PIN = "1111" });
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-A1", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d1 = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
        var d2 = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = 2, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO2", Quantity = 10, LineNo = 2 };
        db.KhPlanDetails.List.Add(d1); plan.Details.Add(d1);
        db.KhPlanDetails.List.Add(d2); plan.Details.Add(d2);
        if (phoi)
            db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 40, NgayNhanPhoi = DateTime.Now, OrderVatLieu = "OVL-1", ViTriDePhoi = "A1", SoPO = "PO1", PartNo = "P" });
        foreach (var id in new[] { 1, 2 })
        {
            var m1 = new KhPlanRouteSnapshotMachining { SnapshotId = id * 10 + 1, KhPlanDetailId = id, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" };
            m1.TimingRows.Add(new KhPlanRouteSnapshotMachiningTiming { TimingSnapshotId = id * 10 + 1, MachiningSnapshotId = m1.SnapshotId, KhPlanDetailId = id });
            db.KhPlanRouteSnapshotMachining.List.Add(m1);
            db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "K1" });
            db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = id, KhPlanDetailId = id, StepOrder = 1, NC = "P1" });
        }
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    static int noLockAll = 0, serAll = 0; static readonly List<string> noLockWhereAll = new();   // cộng dồn qua các bước (Reset xoá bộ đếm từng bước)
    static void Reset()
    {
        noLockAll += Facade.WriteNoPoLock; noLockWhereAll.AddRange(Facade.WriteNoPoLockWhere); serAll += Facade.IsoSerializable + Facade.IsoOther;
        Facade.IsoDefault = Facade.IsoSerializable = Facade.IsoOther = 0;
        Facade.WriteNoPoLock = 0; Facade.WriteNoPoLockWhere.Clear();
        Facade.LockNames.Clear(); Facade.OnAppLock = null;
    }

    static string Locks() => string.Join(" → ", Facade.LockNames);

    public static async Task<int> Run()
    {
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };

        Console.WriteLine("[A1-1] Khuôn ghi số lượng: 1 giao dịch · khoá MES_PO_ trước đọc sổ · Notify sau Commit · không ghi ngoài khoá PO");
        {
            var db = Seed(phoi: false); var svc = new HandoverService(db);
            var kho = new KhoVatLieuService(db, new UserService(null)); var kh = new KhPlanService(db);
            WtsSvcHelp.Reset(); Reset(); Facade.TrackPoLock = true;
            int notified = 0; int? lastNotify = null; bool notifyBeforeCommit = false; bool inTx = false;
            Action<int?> onChanged = id => { notified++; lastNotify = id; if (inTx) notifyBeforeCommit = true; };
            HandoverChangeNotifier.Changed += onChanged;
            Microsoft.EntityFrameworkCore.StubTransaction.OnCommit = () => inTx = false;
            try
            {
                // Kho nhận phôi
                Reset(); notified = 0; inTx = true;
                var k = await kho.SaveKhoInputAsync(1, DateTime.Now, 40, "OVL-1", "A1", null, null, null, "nhan phoi", 1);
                Ok(k.Success, "Kho nhận 40 phôi (SaveKhoInputAsync)", k.Error);
                Ok(Facade.IsoDefault == 1 && Facade.IsoSerializable == 0, "  Kho: đúng 1 giao dịch, không Serializable", $"GD {Facade.IsoDefault} · Ser {Facade.IsoSerializable}");
                Ok(Facade.LockNames.Count > 0 && Facade.LockNames[0] == "MES_PO_1", "  Kho: khoá MES_PO_1 trước khi đọc sổ", Locks());
                Ok(notified == 1 && lastNotify == 1 && !notifyBeforeCommit, "  Kho: Notify PO 1 đúng 1 lần, sau Commit", $"N={notified}");
                // Lập phiếu Kho → GC
                Reset(); notified = 0; inTx = true;
                var i1 = await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI", null, "NC1");
                Ok(i1.Ok, "Lập phiếu Kho → GC 40 (IssueByCellAsync)", i1.Error);
                Ok(Facade.IsoDefault == 1 && Facade.LockNames.FirstOrDefault() == "MES_PO_1", "  Lập phiếu: 1 giao dịch · khoá MES_PO_1 đầu tiên", $"GD {Facade.IsoDefault} · {Locks()}");
                Ok(notified == 1 && !notifyBeforeCommit, "  Lập phiếu: Notify 1 lần sau Commit", $"N={notified}");
                var tx1 = db.HandoverTransactions.List.Last().HandoverTxId;
                // Nhận
                Reset(); notified = 0; inTx = true;
                var r1 = await svc.ReceiveAsync(tx1, 40, 0, null, 1);
                Ok(r1.Ok, "GC nhận 40 (ReceiveAsync)", r1.Error);
                Ok(Facade.IsoDefault == 1 && Facade.LockNames.FirstOrDefault() == "MES_PO_1", "  Nhận: 1 giao dịch · khoá MES_PO_1 đầu tiên", $"GD {Facade.IsoDefault} · {Locks()}");
                Ok(notified == 1 && !notifyBeforeCommit, "  Nhận: Notify 1 lần sau Commit", $"N={notified}");
                // WTS lưu nhóm
                Reset(); notified = 0; inTx = true;
                var w1 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, 1);
                Ok(w1.Ok, "WTS GC NC1 OK 10 (SaveGroupAsync)", w1.Err);
                var ln = Facade.LockNames;
                int iW = ln.FindIndex(x => x.StartsWith("WTS_WORKER_")), iM = ln.FindIndex(x => x.StartsWith("WTS_MACHINE_")), iP = ln.FindIndex(x => x.StartsWith("MES_PO_"));
                Ok(Facade.IsoDefault == 1 && iW >= 0 && iM > iW && iP > iM, "  WTS: 1 giao dịch · thứ tự khoá WTS_WORKER_ → WTS_MACHINE_ → MES_PO_", $"GD {Facade.IsoDefault} · {Locks()}");
                Ok(notified == 1 && !notifyBeforeCommit, "  WTS: Notify 1 lần sau Commit", $"N={notified}");
                // Tách PO trắng (PO2)
                Reset(); notified = 0; inTx = true;
                List<int>? ids = null; string? errS = null;
                try { ids = await kh.SplitPOAsync(2, new() { new() { PurchaseOrder = "PO2A", Quantity = 4 }, new() { PurchaseOrder = "PO2B", Quantity = 6 } }, 1, "tach"); } catch (Exception ex) { errS = ex.Message; }
                Ok(ids != null && ids.Count == 2, "Tách PO2 trắng 10 → 4 + 6 (SplitPOAsync)", errS);
                int iP2 = Facade.LockNames.IndexOf("MES_PO_2"), iN = Facade.LockNames.IndexOf("MES_KH_NUMBERS");
                Ok(Facade.IsoDefault == 1 && iP2 >= 0 && iN > iP2, "  Tách: 1 giao dịch · MES_PO_2 → MES_KH_NUMBERS", $"GD {Facade.IsoDefault} · {Locks()}");
                Reset();   // gom bước cuối vào tổng
                Ok(noLockAll == 0, "Toàn bộ 5 bước (cộng dồn): không lần ghi chứng từ số lượng nào ngoài khoá MES_PO_", string.Join("; ", noLockWhereAll));
                Ok(serAll == 0, "Toàn bộ 5 bước (cộng dồn): không giao dịch xin IsolationLevel riêng");
            }
            finally
            {
                HandoverChangeNotifier.Changed -= onChanged;
                Microsoft.EntityFrameworkCore.StubTransaction.OnCommit = null;
                Facade.TrackPoLock = false; Reset();
            }
        }

        Console.WriteLine("[A1-2] [A1-01-ROWSAVED] WTS: gửi lại cùng nhóm vừa lưu (bấm Lưu 2 lần) → lần 2 bị từ chối, không ra dòng thứ 2");
        {
            var db = Seed(); var svc = new HandoverService(db); WtsSvcHelp.Reset(); Reset();
            var i1 = await svc.IssueByCellAsync(1, "KHO", "KCS", 10, 1, "MOI", false, "MOI");
            Ok(i1.Ok, "Kho giao thẳng KCS 10 (Hàng mới, Workflow ON — KTT-KCS-PKG giữ A)", i1.Error);
            var r1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 1);
            Ok(r1.Ok, "KCS nhận 10", r1.Error);
            var wsvc = new WtsSaveService(db, new HandoverLedgerService(db));
            var grp = WtsSvcHelp.Build(1, "KCS", null, "K1", 3, 0, 1, null, null, null);
            var s1 = await wsvc.SaveGroupAsync(grp); db.Fixup();
            int RowsKcs() => db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == 1 && w.ProcessGroup == "KCS" && !w.IsVoided);
            Ok(s1.Ok && RowsKcs() == 1 && grp.Rows[0].WtsLogId != null && !grp.Rows[0].IsEditing, "Lưu K1 OK 3 lần 1: 1 dòng KCS, dòng có WtsLogId + không ở chế độ Sửa", s1.ErrorMsg);
            var s2 = await wsvc.SaveGroupAsync(grp); db.Fixup();
            Ok(!s2.Ok && (s2.ErrorMsg ?? "").Contains("đã lưu rồi"), "Gửi lại CÙNG nhóm (WtsLogId có, IsEditing false): máy chủ từ chối \"đã lưu rồi\"", s2.ErrorMsg);
            Ok(RowsKcs() == 1, "Vẫn 1 dòng KCS (trước Gói 22: 2 dòng y nhau cùng giờ — Đã làm nhân đôi, sổ vẫn cân nên không bắt được)", $"dòng KCS = {RowsKcs()}");
            // Nhóm mới thật (người dùng cố ý lưu thêm 3 nữa) vẫn lưu được — máy chủ chỉ chặn dòng GỬI LẠI, không chặn lưu thêm
            var s3 = await WtsSvcHelp.SaveAsync(db, 1, "KCS", null, "K1", 3, 0, 1);
            Ok(s3.Ok && RowsKcs() == 2, "Nhóm MỚI (dòng chưa có WtsLogId) K1 OK 3 → lưu được, 2 dòng (hành vi cố ý giữ)", s3.Err);
            // Sửa dòng (IsEditing + WtsLogId) vẫn đi đường cũ: huỷ dòng cũ + dòng mới, tổng dòng còn hiệu lực không đổi
            var s4 = await WtsSvcHelp.SaveAsync(db, 1, "KCS", null, "K1", 2, 0, 1, editLogId: s3.LogId);
            Ok(s4.Ok && RowsKcs() == 2 && db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == 1 && w.ProcessGroup == "KCS" && w.IsVoided) == 1,
               "Sửa dòng (IsEditing + WtsLogId): vẫn huỷ dòng cũ + ghi dòng mới (không bị chặn nhầm)", s4.Err);
        }

        Console.WriteLine("[A1-3] [A1-02] Tách PO: SL PO gốc vừa bị đổi ngay lúc Tách đang chờ khoá → từ chối, không tách");
        {
            var db = Seed(); var kh = new KhPlanService(db); Reset();
            bool fired = false;
            Facade.OnAppLock = n =>
            {
                if (fired || n != "MES_PO_2") return;
                fired = true;
                db.KhPlanDetails.List.Single(x => x.KhPlanDetailId == 2).Quantity = 12;   // "người khác" SaveEditAsync đổi SL 10 → 12 trước khi Tách lấy được khoá
                db.Fixup();
            };
            List<int>? ids = null; string? err = null;
            try { ids = await kh.SplitPOAsync(2, new() { new() { PurchaseOrder = "PO2A", Quantity = 4 }, new() { PurchaseOrder = "PO2B", Quantity = 6 } }, 1, "tach"); } catch (Exception ex) { err = ex.Message; }
            Facade.OnAppLock = null;
            Ok(fired, "Móc OnAppLock(MES_PO_2) đã chạy (SL gốc 10 → 12 trong lúc chờ khoá)");
            Ok(ids == null && err != null && err.Contains("vừa được người khác đổi"), "Tách bị từ chối: \"SL PO vừa được người khác đổi … tải lại rồi tách lại\"", err);
            Ok(db.KhPlanDetails.List.Count == 2, "Không sinh PO mới (trước Gói 22: tách qua, Σ 10 ≠ 12, log SplitInto ghi SL 10)", $"dòng KH = {db.KhPlanDetails.List.Count}");
            Ok(!db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "SplitInto"), "Không có log SplitInto");
            // DB giả: rollback của lượt Tách khôi phục cả ảnh trước giao dịch (xoá luôn sửa SL 12 của "người khác" — SQL thật thì sửa đó đã Commit riêng,
            //   không bị ảnh hưởng) → đặt lại SL 12 như DB thật rồi tách lại cho khớp → được
            db.KhPlanDetails.List.Single(x => x.KhPlanDetailId == 2).Quantity = 12; db.Fixup();
            Reset(); ids = null; err = null;
            try { ids = await kh.SplitPOAsync(2, new() { new() { PurchaseOrder = "PO2A", Quantity = 5 }, new() { PurchaseOrder = "PO2B", Quantity = 7 } }, 1, "tach lai"); } catch (Exception ex) { err = ex.Message; }
            Ok(ids != null && ids.Count == 2 && db.KhPlanDetails.List.Where(x => ids.Contains(x.KhPlanDetailId)).Sum(x => x.Quantity) == 12, "Tách lại 5 + 7 = 12: được, Σ PO mới = SL gốc mới", err);
            var logSplit = db.KhPlanDetailChangeLogs.List.FirstOrDefault(l => l.FieldName == "SplitInto");
            Ok(logSplit != null && (logSplit.OldValue ?? "").Contains("SL 12"), "Log SplitInto ghi SL gốc THẬT (12)", logSplit?.OldValue);
        }

        Console.WriteLine("[A1-4] Kho nhận phôi gọi 2 lần cùng nội dung (bấm Lưu 2 lần) → idempotent: 1 dòng Kho, không thêm log");
        {
            var db = Seed(phoi: false); var kho = new KhoVatLieuService(db, new UserService(null)); Reset();
            var k1 = await kho.SaveKhoInputAsync(1, DateTime.Today, 40, "OVL-1", "A1", null, null, null, "nhan phoi", 1);
            int logs1 = db.KhoVatLieuChangeLogs.List.Count;
            Ok(k1.Success && db.KhoVatLieus.List.Count(x => x.PlanDetailId == 1 && x.IsActive) == 1, "Lần 1: nhận 40 phôi → 1 dòng Kho", k1.Error);
            var k2 = await kho.SaveKhoInputAsync(1, DateTime.Today, 40, "OVL-1", "A1", null, null, null, "nhan phoi", 1);
            int logs2 = db.KhoVatLieuChangeLogs.List.Count;
            Ok(db.KhoVatLieus.List.Count(x => x.PlanDetailId == 1 && x.IsActive) == 1 && db.KhoVatLieus.List.Single(x => x.PlanDetailId == 1 && x.IsActive).SoLuongThucNhan == 40,
               "Lần 2 cùng nội dung: vẫn 1 dòng Kho, phôi 40 (không nhân đôi)", k2.Error);
            Ok(logs2 == logs1, "Lần 2 không thêm log Kho (không đổi gì → không ghi)", $"log {logs1} → {logs2} · lần 2: {(k2.Success ? "OK" : k2.Error)}");
        }

        // A1 kết quả (2026-11-15): chuỗi kết quả phải đúng "KẾT QUẢ: ĐẠT hết" — Nang.cs:88 (kiem trạm) so chuỗi này; bản đầu in "KẾT QUẢ a1: ĐẠT HẾT" → trạm 15/11 21:14 báo SAI giả dù 0 phép sai.
        // Nguồn: trạm kiem 15/11 21:14 (ketqua_kiemtra_261115_2114.txt).
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
