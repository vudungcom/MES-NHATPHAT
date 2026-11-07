// [KH-GUARD] Kịch bản kiểm vòng A: Kho sửa phôi nhận (K5), Xoá / Tách PO, Cập nhật quy trình (K6), bật Workflow (K7)
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class KhScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static AppDbContext Seed(bool workflow = true, int? phoi = 40)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = phoi, NgayNhanPhoi = phoi != null ? DateTime.Now : null, OrderVatLieu = "OVL-1", ViTriDePhoi = "A1", SoPO = "PO1", PartNo = "P" });
        var m1 = new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" };
        m1.TimingRows.Add(new KhPlanRouteSnapshotMachiningTiming { TimingSnapshotId = 1, MachiningSnapshotId = 1, KhPlanDetailId = 1 });
        db.KhPlanRouteSnapshotMachining.List.Add(m1);
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        db.KhPlanRouteSnapshotStdConfirms.List.Add(new KhPlanRouteSnapshotStdConfirm { Id = 1, KhPlanDetailId = 1, SnapshotTable = StdTrack.Table.Machining, SnapshotRowId = 2, FieldName = "SetupTime", IsConfirmed = true });
        VuHelp.SeedCats(db);   // [VULOI]
        db.KhPlanRouteSnapshotStdConfirms.List.Add(new KhPlanRouteSnapshotStdConfirm { Id = 2, KhPlanDetailId = 1, SnapshotTable = StdTrack.Table.Taro, SnapshotRowId = 1, FieldName = "StandardTime", IsConfirmed = false });
        db.Fixup();
        return db;
    }
    static void Wts(AppDbContext db, string nc, decimal ok, bool voided = false)
    {
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = "GC",
            NC = nc, WorkerId = 2, QtyDone = ok, QtyOk = ok, QtyNg = 0, IsVoided = voided, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
        db.Fixup();
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[A1] Kho sửa phôi nhận (K5)");
        {
            var db = Seed(); var svc = new HandoverService(db); var kho = new KhoVatLieuService(db, new UserService(null));
            await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI");
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, 30, "OVL-1", "A1", null, null, null, "dem lai", 1);
            Ok(!r.Success && r.Error!.Contains("phôi còn giao") && r.Error.Contains("nên kiểm tra"), "Đã giao 40 cho GC, sửa phôi nhận 40→30: bị chặn + gợi ý gỡ", r.Error);
            Ok((db.KhoVatLieus.List.Single().SoLuongThucNhan ?? 0) == 40, "Bị chặn thì phôi nhận vẫn 40 (rollback)");
            var r2 = await kho.SaveKhoInputAsync(1, DateTime.Now, 45, "OVL-1", "A1", null, null, null, "nhan them", 1);
            Ok(r2.Success && db.KhoVatLieus.List.Single().SoLuongThucNhan == 45, "Tăng phôi nhận 40→45: lưu được");
            await svc.VoidIssueAsync(1, "huy", 1, new List<string> { "KHO" });
            var r3 = await kho.SaveKhoInputAsync(1, DateTime.Now, 30, "OVL-1", "A1", null, null, null, "dem lai", 1);
            Ok(r3.Success, "Huỷ phiếu giao rồi sửa 45→30: lưu được", r3.Error);
        }
        {
            var db = Seed(workflow: false); var svc = new HandoverService(db); var kho = new KhoVatLieuService(db, new UserService(null));
            await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI");
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, 30, "OVL-1", "A1", null, null, null, "dem lai", 1);
            var row = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            Ok(r.Success && row!.G("KHO").ConGiao == -10, "Workflow OFF: không chặn (như luật H7), ô Phôi còn giao hiện −10 để người kiểm", $"ConGiao={row!.G("KHO").ConGiao}");
        }

        Console.WriteLine("[A2] Xoá PO");
        {
            var db = Seed(); var kh = new KhPlanService(db); EfRaw().Clear();
            var r = await kh.DeleteDetailAsync(1, 1, "tao nham");
            Ok(!r.Success && r.Error!.Contains("phôi Kho đã nhận 40"), "PO có phôi nhận: không xoá", r.Error);
            Ok(db.KhPlanDetails.List.Count == 1 && EfRaw().Count == 0, "Không chạy lệnh xoá nào");
        }
        {
            var db = Seed(phoi: null); Wts(db, "NC1", 5, voided: true); var kh = new KhPlanService(db);
            var r = await kh.DeleteDetailAsync(1, 1, "tao nham");
            Ok(!r.Success && r.Error!.Contains("1 dòng WTS (1 dòng đã huỷ)"), "PO chỉ có WTS đã huỷ: vẫn không xoá (WTS là lịch sử)", r.Error);
        }
        {
            var db = Seed(phoi: null); var kh = new KhPlanService(db); EfRaw().Clear();
            var r = await kh.DeleteDetailAsync(1, 1, "tao nham");
            Ok(r.Success && db.KhPlanDetails.List.Count == 0, "PO trắng: xoá được", r.Error);
            Ok(EfRaw().Any(x => x.Contains("UPDATE KhoVatLieus SET IsActive = 0")) && !EfRaw().Any(x => x.Contains("DELETE FROM WtsProductionLogs")) && !EfRaw().Any(x => x.Contains("DELETE FROM KhoVatLieus")),
               "Dòng Kho tắt IsActive, không xoá cứng WTS / Kho");
        }
        // [KHE-5 2026-11-06] lý do bắt buộc + ảnh chụp PO (JSON dòng PO + lịch sử sửa + ai / lúc nào / lý do) trước khi xoá, cùng giao dịch
        {
            var db = Seed(phoi: null); var kh = new KhPlanService(db); EfRaw().Clear();
            db.KhPlanDetailChangeLogs.List.Add(new KhPlanDetailChangeLog { ChangeId = 7, KhPlanDetailId = 1, FieldName = "Quantity", OldValue = "30", NewValue = "40", ChangedBy = 1, Reason = "khách đổi SL" });
            var r0 = await kh.DeleteDetailAsync(1, 1, "  x ");
            Ok(!r0.Success && r0.Error!.Contains("lý do") && db.KhPlanDetails.List.Count == 1 && db.KhPlanDeletedDetails.List.Count == 0 && EfRaw().Count == 0,
               "[KHE-5] Lý do < 3 ký tự: không xoá, không chụp, không chạy lệnh nào", r0.Error);
            var r = await kh.DeleteDetailAsync(1, 1, "  tạo nhầm PO  ");
            var snap = db.KhPlanDeletedDetails.List.SingleOrDefault();
            Ok(r.Success && snap != null && snap.KhPlanDetailId == 1 && snap.Reason == "tạo nhầm PO" && snap.DeletedBy == 1 && snap.PurchaseOrder == "PO1"
               && snap.DetailJson.Contains("\"PurchaseOrder\":\"PO1\"") && snap.DetailJson.Contains("\"PlanNo\":\"KH-TEST\"") && !snap.DetailJson.Contains("\"Details\"") && !snap.DetailJson.Contains("\"Customer\"")
               && (snap.ChangeLogsJson ?? "").Contains("khách đổi SL") && (snap.ChangeLogsJson ?? "").Contains("\"ChangeId\":7"),
               "[KHE-5] Xoá có lý do: 1 dòng KhPlanDeletedDetails (PO, lý do đã cắt khoảng trắng, người xoá, JSON dòng PO + đầu phiếu KH không kèm navigation, JSON lịch sử sửa có dòng ChangeId 7)",
               r.Error + " · " + snap?.DetailJson + " · " + snap?.ChangeLogsJson);
            var iDel = EfRaw().FindIndex(x => x.Contains("DELETE FROM KhPlanDetailChangeLogs"));
            Ok(iDel >= 0 && EfRaw()[iDel].Contains("IF EXISTS (SELECT 1 FROM sys.foreign_keys") && db.Database.Current == null,
               "[KHE-5] Lịch sử sửa chỉ xoá khi DB còn khoá ngoại vào KhPlanDetails (rà độc lập V1: FK đã drop v0.5.1 → giữ log); giao dịch đã đóng", string.Join(" | ", EfRaw()));
        }
        {
            // [KHE-5] lỗi giữa chừng (sau khi chụp) → quay lại: không còn dòng chụp, PO còn nguyên
            var db = Seed(phoi: null); var kh = new KhPlanService(db); EfRaw().Clear();
            var fire = 0;
            AppDbContext.OnSave = () => ++fire == 2 ? new InvalidOperationException("giả lỗi DB lúc xoá PO") : null;   // lần lưu 1 = ảnh chụp, lần 2 = xoá PO
            var r = await kh.DeleteDetailAsync(1, 1, "tao nham");
            AppDbContext.OnSave = null;
            Ok(!r.Success && db.KhPlanDetails.List.Count == 1 && db.KhPlanDeletedDetails.List.Count == 0,
               "[KHE-5] Lỗi DB sau khi chụp: quay lại hết (không còn dòng chụp, PO còn)", r.Error + $" · chụp {db.KhPlanDeletedDetails.List.Count}");
        }

        Console.WriteLine("[A3] Tách PO");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            string? err = null; try { await kh.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 20 }, new() { PurchaseOrder = "PO1B", Quantity = 20 } }, 1, "tach"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("không tách được"), "PO có phôi nhận: không tách", err);
            Ok(db.KhPlanDetails.List.Count == 1, "Không sinh PO mới");
        }
        {
            var db = Seed(phoi: null); var kh = new KhPlanService(db); EfRaw().Clear();
            List<int>? ids = null; string? err = null;
            try { ids = await kh.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 15 }, new() { PurchaseOrder = "PO1B", Quantity = 25 } }, 1, "tach"); } catch (Exception ex) { err = ex.Message + ex.StackTrace; }
            Ok(ids != null && ids.Count == 2, "PO trắng: tách được thành 2 PO", err);
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    var mc = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id).ToList();
                    Ok(mc.Count == 2 && mc.Select(s => s.NC).SequenceEqual(new[] { "NC1", "NC2" }) && mc[0].TimingRows.Count == 1 && mc[0].TimingRows.All(t => t.KhPlanDetailId == id),
                       $"PO mới #{id}: quy trình GC chép đúng từ PO gốc (NC1, NC2, máy đồng dạng)");
                    Ok(db.KhPlanRouteSnapshotTaro.List.Count(s => s.KhPlanDetailId == id) == 1 && db.KhPlanRouteSnapshotInspection.List.Count(s => s.KhPlanDetailId == id) == 1
                       && db.KhPlanRouteSnapshotPackaging.List.Count(s => s.KhPlanDetailId == id) == 1, $"PO mới #{id}: Taro / KCS / Đóng gói chép đủ");
                    var nc2 = mc.First(s => s.NC == "NC2");
                    var conf = db.KhPlanRouteSnapshotStdConfirms.List.Where(c => c.KhPlanDetailId == id).ToList();
                    var taroNew = db.KhPlanRouteSnapshotTaro.List.First(s => s.KhPlanDetailId == id);
                    Ok(conf.Count == 2 && conf.Any(c => c.SnapshotTable == "Machining" && c.SnapshotRowId == nc2.SnapshotId && c.IsConfirmed)
                       && conf.Any(c => c.SnapshotTable == "Taro" && c.SnapshotRowId == taroNew.SnapshotId && !c.IsConfirmed),
                       $"PO mới #{id}: trạng thái xác nhận TG chuẩn trỏ đúng dòng snapshot mới");
                    var k = db.KhoVatLieus.List.Single(x => x.PlanDetailId == id);
                    Ok(k.IsActive && k.OrderVatLieu == "OVL-1" && k.ViTriDePhoi == "A1" && k.SoLuongThucNhan == null, $"PO mới #{id}: dòng Kho chép order / vị trí, chưa có phôi nhận");
                }
                Ok(!db.KhoVatLieus.List.Single(x => x.PlanDetailId == 1).IsActive, "Dòng Kho của PO gốc tắt IsActive");
                var iSnap = EfRaw().FindIndex(x => x.Contains("DELETE FROM KhPlanRouteSnapshotMachining ") || x.Contains("DELETE FROM KhPlanRouteSnapshotMachining  "));
                var iDet = EfRaw().FindIndex(x => x.Contains("DELETE FROM KhPlanDetails"));
                Ok(iSnap >= 0 && iDet > iSnap && EfRaw().Any(x => x.Contains("DELETE FROM KhPlanRouteSnapshotStdConfirms")), "Xoá snapshot PO gốc TRƯỚC khi xoá dòng gốc (khoá ngoại NO ACTION)");
            }
        }

        Console.WriteLine("[A4] Cập nhật quy trình (K6)");
        {
            var db = Seed(); Wts(db, "NC1", 5); var kh = new KhPlanService(db);
            string? err = null; try { await kh.RefreshRouteSnapshotAsync(1, 1, 1, "doi quy trinh"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("K6"), "PO đã có WTS: chặn cập nhật quy trình", err);
        }
        {
            var db = Seed(); Wts(db, "NC1", 5, voided: true); var kh = new KhPlanService(db);
            string? err = null; try { await kh.RefreshRouteSnapshotAsync(1, 1, 1, "doi quy trinh"); } catch (Exception ex) { err = ex.Message; }
            Ok(err == null || !err.Contains("K6"), "PO chỉ có WTS đã huỷ: cập nhật được", err);
        }

        Console.WriteLine("[A5] Bật Workflow (K7)");
        {
            var db = Seed(workflow: false, phoi: 10); var svc = new HandoverService(db); var kh = new KhPlanService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "NC1", 15);   // lúc OFF nhập vượt
            var e = await kh.CheckWorkflowOnAsync(1);
            Ok(e != null && e.Contains("K7") && e.Contains("PO1"), "Có ô âm: chặn bật Workflow", e);
            var e2 = await kh.SetApplyWorkflowAsync(1, true, 1, "bat lai");
            Ok(e2 != null && !db.KhPlans.List[0].ApplyWorkflow && db.KhPlanDetailChangeLogs.List.Count == 0 && db.KhPlanDetailChangeLogs.Added.Count == 0, "Bị chặn thì vẫn OFF, không ghi log");
            db.WtsProductionLogs.List.Last().IsVoided = true; db.Fixup();
            var e3 = await kh.SetApplyWorkflowAsync(1, true, 1, "bat lai");
            await db.SaveChangesAsync();
            Ok(e3 == null && db.KhPlans.List[0].ApplyWorkflow && db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "ApplyWorkflow" && l.OldValue == "OFF" && l.NewValue == "ON"),
               "Sửa WTS gây âm rồi bật: được + ghi lịch sử OFF→ON", e3);
        }

        // ── [KHE-6][KHE-3][KHE-7 2026-10-30] Sửa phiếu KH: Workflow dưới khoá PO, chỉ ghi ô người dùng đổi, lịch sử Ghi chú / Ngày nhận ──
        static KhPlanService.KhEditValues EV(AppDbContext db, int detId)
        {
            var d = db.KhPlanDetails.List.First(x => x.KhPlanDetailId == detId); var p = db.KhPlans.List.First(x => x.KhPlanId == d.KhPlanId);
            return new KhPlanService.KhEditValues { PlanNotes = p.Notes, ReceivedDate = p.ReceivedDate, ApplyWorkflow = p.ApplyWorkflow,
                Quantity = d.Quantity, Unit = d.Unit, STD = d.STD, Type = d.Type, Priority = d.Priority, Material = d.Material,
                MaterialConfig = d.MaterialConfig, Location = d.Location, MaterialNotes = d.MaterialNotes, OrderVL = d.OrderVL,
                MaterialCondition = d.MaterialCondition, PartNotes = d.PartNotes };
        }
        static void AddPo2(AppDbContext db)
        {
            var plan = db.KhPlans.List[0];
            var d2 = new KhPlanDetail { KhPlanDetailId = 2, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO2", Quantity = 20, LineNo = 2 };
            db.KhPlanDetails.List.Insert(0, d2); plan.Details.Add(d2); db.Fixup();   // chèn ĐẦU danh sách → DB trả 2,1: kiểm được khoá có sắp tăng dần
        }
        Console.WriteLine("[A6] KHE-6: bật Workflow kiểm K7 SAU khi khoá mọi PO của phiếu (tăng dần, trong giao dịch)");
        {
            var db = Seed(workflow: false, phoi: 10); AddPo2(db); var svc = new HandoverService(db); var kh = new KhPlanService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "NC1", 10);
            Ok(await kh.CheckWorkflowOnAsync(1) == null, "Lúc bấm Lưu (kiểm trước, ngoài khoá): chưa âm");
            // người khác lưu WTS lúc còn OFF, xong ngay trước khi mình lấy được khoá PO1 → vượt 5
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            bool fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = n => { if (!fired && n == "MES_PO_1") { fired = true; Wts(db, "NC1", 5); db.Fixup(); } };
            var e = await kh.SetApplyWorkflowAsync(1, true, 1, "bat");
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(e != null && e.Contains("K7") && !db.KhPlans.List[0].ApplyWorkflow, "WTS chen vào trước khoá: K7 đọc sau khoá → chặn, vẫn OFF", e);
            Ok(string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == "MES_PO_1,MES_PO_2", "Khoá đủ 2 PO của phiếu theo thứ tự tăng dần",
               string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            Ok(!db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "ApplyWorkflow") && db.Database.Current == null, "Bị chặn: không ghi log, giao dịch đã đóng");
        }
        {
            var db = Seed(workflow: false, phoi: 10); AddPo2(db); var svc = new HandoverService(db); var kh = new KhPlanService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "NC1", 10);
            var o = EV(db, 1); var n = EV(db, 1); n.ApplyWorkflow = true; n.Location = "B9";
            bool fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = nm => { if (!fired && nm == "MES_PO_1") { fired = true; Wts(db, "NC1", 5); db.Fixup(); } };
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "bat wf");
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(e != null && e.Contains("K7") && !db.KhPlans.List[0].ApplyWorkflow && db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 1).Location != "B9",
               "Trang Sửa phiếu: WTS chen trước khoá → chặn cả phiếu (Workflow + ô khác không lưu)", e);
        }
        {
            var db = Seed(workflow: false, phoi: 10); AddPo2(db); var svc = new HandoverService(db); var kh = new KhPlanService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 10, 0, null, 2);
            Wts(db, "NC1", 10);
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var o = EV(db, 1); var n = EV(db, 1); n.ApplyWorkflow = true;
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "bat wf");
            Ok(e == null && db.KhPlans.List[0].ApplyWorkflow
               && db.KhPlanDetailChangeLogs.List.Count(l => l.FieldName == "ApplyWorkflow" && l.OldValue == "OFF" && l.NewValue == "ON") == 2
               && string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == "MES_PO_1,MES_PO_2",
               "Không âm: bật được, log OFF→ON cả 2 PO, khoá 2 PO", e);
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var o2 = EV(db, 2); var n2 = EV(db, 2); n2.Quantity = 25;
            var e2 = await kh.SaveEditAsync(1, 2, o2, n2, 1, "sua sl");
            Ok(e2 == null && db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 2).Quantity == 25 && string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == "MES_PO_2",
               "Sửa SL không đổi Workflow: chỉ khoá đúng PO đang sửa", e2);
        }
        Console.WriteLine("[A7] KHE-3: chỉ ghi ô người dùng đổi; người khác vừa sửa cùng ô → không lưu");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 45;
            db.KhPlanDetails.List[0].Location = "KHAC";      // người khác lưu Vị trí sau khi mình mở trang
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "sua sl");
            Ok(e == null && db.KhPlanDetails.List[0].Quantity == 45 && db.KhPlanDetails.List[0].Location == "KHAC",
               "Mình sửa SL, người khác sửa Vị trí: giữ cả 2 (trước: Vị trí bị ghi đè về giá trị cũ)", e);
            Ok(db.KhPlanDetailChangeLogs.List.Count(l => l.FieldName == "Quantity" && l.NewValue == "45") == 1
               && !db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "Location"), "Log chỉ ô SL");
        }
        {
            var db = Seed(); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 45; n.PlanNotes = "ghi chu moi";
            db.KhPlanDetails.List[0].Quantity = 50;            // người khác đổi SL sang số khác
            var logs0 = db.KhPlanDetailChangeLogs.List.Count;
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "sua sl");
            Ok(e != null && e.Contains("SL") && e.Contains("50") && db.KhPlanDetails.List[0].Quantity == 50 && db.KhPlans.List[0].Notes == null
               && db.KhPlanDetailChangeLogs.List.Count == logs0 && db.Database.Current == null,
               "Cùng ô SL, người khác đã đổi 40→50: không lưu gì (cả Ghi chú), báo ô SL", e);
        }
        {
            var db = Seed(); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 45;
            db.KhPlanDetails.List[0].Quantity = 45;            // người khác đổi đúng số mình định đổi
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "sua sl");
            Ok(e == null && db.KhPlanDetails.List[0].Quantity == 45 && !db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "Quantity"),
               "Người khác đã đổi đúng giá trị mình định đổi: không báo xung đột, không ghi log trùng", e);
        }
        {
            var db = Seed(workflow: false); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.Location = "X1";
            db.KhPlans.List[0].ApplyWorkflow = true;             // người khác bật Workflow; mình không đụng công tắc
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "sua vi tri");
            Ok(e == null && db.KhPlans.List[0].ApplyWorkflow && db.KhPlanDetails.List[0].Location == "X1",
               "Người khác bật Workflow, mình chỉ sửa Vị trí: giữ Workflow ON (trước: trang tắt lại về OFF)", e);
        }
        Console.WriteLine("[A8] KHE-7: Ghi chú phiếu / Ngày nhận PO có lịch sử ở mọi PO của phiếu");
        {
            var db = Seed(); AddPo2(db); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.PlanNotes = "giao gap"; n.ReceivedDate = new DateTime(2026, 10, 30);
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "cap nhat");
            var L = db.KhPlanDetailChangeLogs.List;
            Ok(e == null && db.KhPlans.List[0].Notes == "giao gap" && db.KhPlans.List[0].ReceivedDate == new DateTime(2026, 10, 30)
               && L.Count(l => l.FieldName == "PlanNotes" && l.OldValue == null && l.NewValue == "giao gap" && l.Reason == "cap nhat") == 2
               && L.Count(l => l.FieldName == "ReceivedDate" && l.OldValue == null && l.NewValue == "2026-10-30") == 2,
               "Lưu Ghi chú + Ngày nhận: ghi phiếu + 2 dòng log mỗi trường (PO1, PO2)", e);
            var o2 = EV(db, 1); var n2 = EV(db, 1);
            var c0 = L.Count;
            var e2 = await kh.SaveEditAsync(1, 1, o2, n2, 1, "khong doi gi");
            Ok(e2 == null && db.KhPlanDetailChangeLogs.List.Count == c0, "Không đổi gì: không ghi log", e2);
        }
        {   // [rà độc lập] Ghi chú phiếu dùng chung → sửa từ PO2 cũng khoá mọi PO; chỉ sửa ô của PO → chỉ khoá PO đó
            var db = Seed(); AddPo2(db); var kh = new KhPlanService(db);
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var o = EV(db, 2); var n = EV(db, 2); n.PlanNotes = "tu PO2";
            var e = await kh.SaveEditAsync(1, 2, o, n, 1, "ghi chu");
            Ok(e == null && string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames) == "MES_PO_1,MES_PO_2",
               "Đổi Ghi chú phiếu: khoá cả 2 PO (2 người sửa 2 PO không ghi đè Ghi chú của nhau)", string.Join(",", Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames));
            // PO mới chen vào phiếu trong lúc chờ khoá → không lưu
            bool fired = false;
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = nm => { if (!fired) { fired = true;
                var p3 = new KhPlanDetail { KhPlanDetailId = 3, KhPlanId = 1, KhPlan = db.KhPlans.List[0], PartNo = "P", PurchaseOrder = "PO3", Quantity = 5, LineNo = 3 };
                db.KhPlanDetails.List.Add(p3); db.Fixup(); } };
            var o2 = EV(db, 1); var n2 = EV(db, 1); n2.PlanNotes = "lan 2";
            var e2 = await kh.SaveEditAsync(1, 1, o2, n2, 1, "ghi chu 2");
            Microsoft.EntityFrameworkCore.DatabaseFacade.OnAppLock = null;
            Ok(e2 != null && e2.Contains("thêm / bớt PO") && db.KhPlans.List[0].Notes == "tu PO2", "PO thêm vào phiếu lúc đang chờ khoá: không lưu, báo bấm Lưu lại", e2);
        }
        {   // null / "" và STD có giờ: không báo xung đột oan, không ghi log thừa
            var db = Seed(); var kh = new KhPlanService(db);
            db.KhPlanDetails.List[0].STD = new DateTime(2026, 11, 5, 14, 30, 0); db.KhPlanDetails.List[0].Type = null;
            var o = EV(db, 1); var n = EV(db, 1); n.Type = ""; n.STD = new DateTime(2026, 11, 5); n.PlanNotes = "";
            var c0 = db.KhPlanDetailChangeLogs.List.Count;
            var e = await kh.SaveEditAsync(1, 1, o, n, 1, "khong doi");
            Ok(e == null && db.KhPlanDetailChangeLogs.List.Count == c0, "Ô trống null ↔ \"\" và STD cùng ngày khác giờ: coi như không đổi", e);
        }
        Console.WriteLine("[A9] SaveEditAsync không có quyền Sửa Kế hoạch: chặn ở máy chủ");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 41;
            var e = await kh.SaveEditAsync(1, 1, o, n, 5, "x");
            Ok(e != null && db.KhPlanDetails.List[0].Quantity == 40, "u5 (xưởng, không có Sửa KE_HOACH) bị chặn", e);
        }

        // ── [GN-NG-AFTER] Báo hàng lỗi phát hiện sau khi nhận ─────────────
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        Console.WriteLine("[B1] Kho nhận 40 phôi, sau phát hiện 3 phôi lỗi → báo lỗi → trả khách");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var r0 = await led.GetLedgerForDetailAsync(1);
            Ok(r0!.G("KHO").LoiGiu == 0 && HandoverLedgerService.IssueCap(r0, "KHO", HandoverPurpose.CustReturn, null, HandoverCell.Loi).Max == 0, "Trước khi báo: Kho tồn hàng lỗi 0 (ô Hàng lỗi xám như ảnh)");
            var deny = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, FuzzPerm.GcOnlyUser(db), new List<string> { "GC" }, ngCategoryId: 1);   // [VOID-PERM] người chỉ Sửa GC trong DB (trước u2 nhóm XUONG — nay máy chủ đọc quyền DB)
            Ok(!deny.Ok && deny.Error.Contains("không có quyền sửa nhóm"), "Người nhóm GC không báo lỗi thay Kho được (đúng lời báo quyền)", deny.Error);   // [VOID-PERM] rà độc lập: chặn vì lý do khác không được tính là đạt
            var over = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 41, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            Ok(!over.Ok && over.Error.Contains("tối đa 40"), "Báo 41 > phôi còn 40: chặn", over.Error);
            var r = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var r1 = await led.GetLedgerForDetailAsync(1);
            Ok(r.Ok && r1!.G("KHO").ConGiao == 37 && r1.G("KHO").LoiGiu == 3 && r1.G("KHO").Ton == 40, "Báo 3: Phôi còn 37, Tồn hàng lỗi 3, Tồn Kho vẫn 40", r.Error);
            var cr = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 3, 1, HandoverPurpose.CustReturn, reason: "phoi ro khi", ngCaseId: VuHelp.LastCase(db));
            var r2 = await led.GetLedgerForDetailAsync(1);
            Ok(cr.Ok && r2!.G("KHO").LoiGiu == 0 && r2.TraKhach == 3 && r2.G("KHO").Ton == 37, "Trả khách 3 từ ô Hàng lỗi: Tồn Kho 37, Đã trả khách 3", cr.Error);
            Ok(r2.BalanceExplain.Result == r2.Phoi, $"Cân PO: {r2.BalanceExplain.Result} = phôi {r2.Phoi}");
            var vb = await svc.VoidNgReportAsync(1, "bao nham", 1, new List<string>());
            Ok(!vb.Ok && vb.Error.Contains("Không lưu") && vb.Error.Contains("nên kiểm tra"), "Hàng lỗi đã trả khách: huỷ báo lỗi bị chặn + gợi ý", vb.Error);
            var steps = HandoverLedgerService.ComputeSteps((await led.GetInputForDetailAsync(1))!);
            Ok(steps.Any(x => x.Kind == "BAOLOI" && x.Group == "KHO" && x.Qty == 3), "Dòng B có bước Báo lỗi");
        }
        Console.WriteLine("[B2] GC nhận 40, NC1 làm 10, phát hiện 2 chiếc lỗi phôi đang chờ NC1 → chuyển hàng lỗi về Kho");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 40, 0, null, 2);
            Wts(db, "NC1", 10);
            var r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, null, 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" }, ngCategoryId: 1);
            Ok(!r.Ok && r.Error.Contains("NC"), "GC không chọn NC: bắt chọn NC", r.Error);
            r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 31, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" }, ngCategoryId: 1);
            Ok(!r.Ok && r.Error.Contains("tối đa 30"), "NC1 còn chờ 30: báo 31 bị chặn", r.Error);
            r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" }, ngCategoryId: 1);
            var row = await led.GetLedgerForDetailAsync(1);
            var nc1 = row!.G("GC").Steps.First(x => x.Code == "NC1");
            Ok(r.Ok && nc1.Waiting == 28 && row.G("GC").LoiGiu == 2 && row.G("GC").ChuaLam == 28 + row.G("GC").Steps.First(x => x.Code == "NC2").Waiting,
               $"NC1 chờ 30→28, Tồn hàng lỗi GC 2, Chưa GC = Σ chờ các NC", r.Error);
            var cap = HandoverLedgerService.WtsCap(row, "GC", "NC1");
            Ok(cap.Max == 28, $"Trần WTS NC1 giảm theo: {cap.Max}");
            var mv = await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", false, "LOI", reason: "phoi nut", ngCaseId: VuHelp.LastCase(db));
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 0, 2, "phoi nut", 1);
            var r2 = await led.GetLedgerForDetailAsync(1);
            Ok(mv.Ok && r2!.G("GC").LoiGiu == 0 && r2.G("KHO").LoiGiu == 2 && r2.BalanceExplain.Result == r2.Phoi,
               "GC giao ô Hàng lỗi → Kho nhận ô Hàng lỗi: GC hết lỗi, Kho tồn lỗi 2, cân PO", mv.Error);
            var vb = await svc.VoidNgReportAsync(1, "bao nham", 2, new List<string>());
            Ok(!vb.Ok && vb.Error.Contains("nên kiểm tra"), "Hàng lỗi đã chuyển Kho: huỷ báo lỗi bị chặn + chỉ phiếu cần gỡ", vb.Error);
        }
        Console.WriteLine("[B3] HTSP: hàng đã làm chưa giao phát hiện lỗi đầu ra → huỷ báo (chưa dùng) được");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var r = await svc.ReportNgAfterReceiveAsync(1, "HTSP", HandoverNgSource.Da, null, 1, HandoverNgSource.DauRa, null, "xuoc", null, 3, new List<string> { "HTSP" }, ngCategoryId: 1);
            Ok(!r.Ok, "HTSP chưa có hàng: không báo được", r.Error);
            var r2 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauRa, null, "kho lam mop", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var v = await svc.VoidNgReportAsync(1, "bao nham", 1, new List<string>());
            var row = await led.GetLedgerForDetailAsync(1);
            Ok(r2.Ok && v.Ok && row!.G("KHO").ConGiao == 40 && row.G("KHO").LoiGiu == 0, "Báo rồi huỷ ngay: số về lại như cũ", v.Error);
        }

        Console.WriteLine("[C1] PO 40 đang ở đâu: Kho giao GC 30, NC1 làm 18 OK + 2 NG, Kho báo 2 phôi lỗi");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 30, 1, "MOI", false, "MOI");
            await svc.ReceiveAsync(1, 30, 0, null, 2);
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 1, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", WorkerId = 2, QtyDone = 20, QtyOk = 18, QtyNg = 2, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            db.Fixup();
            await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "phoi ro", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var w = HandoverLedgerService.WhereIs((await led.GetLedgerForDetailAsync(1))!);
            decimal Q(string place, string label) => w.Items.Where(i => i.Place == place && i.Label == label).Sum(i => i.Qty);
            Ok(w.Balanced && w.Total == 40, $"Tổng = phôi 40: {w.Total}");
            Ok(Q("KHO", "phôi còn") == 8 && Q("KHO", "hàng lỗi") == 2, "Kho: phôi còn 8, hàng lỗi 2");
            Ok(Q("GC", "chờ NC NC1") == 10 && Q("GC", "chờ NC NC2") == 18 && Q("GC", "hàng lỗi") == 2, "GC: chờ NC1 10, chờ NC2 18, hàng lỗi (NG WTS) 2");
            Ok(w.Short() == "Kho 8 · Gia công 28 · lỗi 4" && w.Status == "DANG_SX" && w.Loi == 4, $"Tóm tắt dòng KH: «{w.Short()}» · {HandoverLedgerService.WhereSummary.StatusLabel(w.Status)}");
        }

        Console.WriteLine("[C2] Đúng các bước trong tài liệu test vòng 2 (Bài 1–4)");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var K = new List<string> { "KHO" }; var Gc = new List<string> { "GC" };
            Ok((await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 41, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, K, ngCategoryId: 1)).Ok == false, "Bài 1: báo 41 bị chặn");
            Ok((await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, K, ngCategoryId: 1)).Ok, "Bài 1: báo 3");
            Ok((await svc.IssueSpecialAsync(1, "KHO", "KHACH", 3, 1, HandoverPurpose.CustReturn, reason: "phoi ro khi", ngCaseId: VuHelp.LastCase(db))).Ok, "Bài 1: trả khách 3");
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 30, 1, "MOI", false, "MOI")).Ok, "Bài 2: Kho giao 30");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 30, 0, null, 2);
            Wts(db, "NC1", 10);
            Ok(!(await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, null, 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, Gc, ngCategoryId: 1)).Ok, "Bài 2: bỏ trống NC bị chặn");
            Ok((await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, Gc, ngCategoryId: 1)).Ok, "Bài 2: GC báo 2 ở NC1");
            var rw = (await led.GetLedgerForDetailAsync(1))!;
            Ok(HandoverLedgerService.WtsCap(rw, "GC", "NC1").Max == 18, "Bài 2: WTS NC1 chưa làm 18");
            Ok((await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", false, "LOI", reason: "phoi nut", ngCaseId: VuHelp.LastCase(db))).Ok, "Bài 2: GC giao Hàng lỗi → Kho ô Hàng lỗi");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 0, 2, "phoi nut", 1);
            var rep3 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 1, HandoverNgSource.DauRa, null, "thu bao nham", null, 1, K, ngCategoryId: 1);
            var id3 = db.HandoverNgReports.List.Max(b => b.HandoverNgReportId);
            Ok(rep3.Ok && (await led.GetLedgerForDetailAsync(1))!.G("KHO").ConGiao == 6, "Bài 3: báo nhầm 1 → phôi còn 6");
            Ok((await svc.VoidNgReportAsync(id3, "bao nham", 1, K)).Ok && (await led.GetLedgerForDetailAsync(1))!.G("KHO").ConGiao == 7, "Bài 3: huỷ → phôi còn 7");
            var vGc = await svc.VoidNgReportAsync(2, "thu", 2, Gc);
            Ok(!vGc.Ok && vGc.Error.Contains("Lần nhận") && vGc.Error.Contains("không tự xoá"), "Bài 3: huỷ báo lỗi GC bị chặn + liệt kê lần nhận / phiếu", vGc.Error);
            var vK = await svc.VoidNgReportAsync(1, "thu", 1, K);
            Ok(!vK.Ok && vK.Error.Contains("Khách hàng"), "Bài 3: huỷ báo lỗi Kho bị chặn (đã trả khách)", vK.Error);
            Ok(!HandoverService.CanVoidNgReport(1, "KHO", 3, new List<string> { "HTSP" }), "Bài 3: người HTSP không huỷ được báo lỗi Kho");
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var w = HandoverLedgerService.WhereIs(row);
            var nc1 = row.G("GC").Steps.First(x => x.Code == "NC1"); var nc2 = row.G("GC").Steps.First(x => x.Code == "NC2");
            Ok(w.Short() == "Kho 7 · Gia công 28 · lỗi 2 · huỷ/trả 3", $"Bài 4: tóm tắt «{w.Short()}»");
            Ok(w.PlaceTotal("KHO") == 9 && w.PlaceTotal("GC") == 28 && w.PlaceTotal("RA") == 3 && w.Balanced && w.TpNhap == 0, $"Bài 4: Kho 9 · GC 28 · ra 3 · cân {w.Total}");
            Ok(nc1.Ok == 10 && nc1.In == 28 && nc2.Ok == 0 && nc2.In == 10, $"Bài 4: NC1 OK {nc1.Ok}/vào {nc1.In}, NC2 OK {nc2.Ok}/vào {nc2.In}");
            Ok(w.Status == "DANG_SX" && w.Loi == 2, $"Bài 4: trạng thái {HandoverLedgerService.WhereSummary.StatusLabel(w.Status)} · lỗi {w.Loi}");
        }

        Console.WriteLine("[A10] KHO-4 — Workflow OFF: Kho sửa phôi làm sai số → vẫn lưu nhưng CẢNH BÁO ô nào âm; không âm thì không cảnh báo (dựng lại ca A10 FIX-NGAM 31/10)");
        {
            var db = Seed(workflow: false); var svc = new HandoverService(db); var kho = new KhoVatLieuService(db, new UserService(null));
            await svc.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI");
            var up = await kho.SaveKhoInputAsync(1, DateTime.Now, 45, "OVL-1", "A1", null, null, null, "nhan them", 1);
            Ok(up.Success && up.Error == null, "Tăng phôi 40→45 (không ô nào âm): lưu, không cảnh báo", up.Error);
            var dn = await kho.SaveKhoInputAsync(1, DateTime.Now, 30, "OVL-1", "A1", null, null, null, "dem lai", 1);
            Ok(dn.Success && dn.Error != null && dn.Error.StartsWith("Đã lưu — nhưng PO đang tắt Workflow") && db.KhoVatLieus.List.Single().SoLuongThucNhan == 30,
               "Giảm 45→30 khi đã giao 40: lưu 30 + cảnh báo nêu ô âm (trước: lưu im lặng)", dn.Error);
        }

        // ── [KHO] luật máy chủ KhoVatLieuService (02/11) — trang thật: tools/WtsHarness/drive_kho.py ──
        Console.WriteLine("[A11] E4-KHO-NEG — SaveKhoInputAsync chặn số âm ở máy chủ (cả khi Workflow OFF, không còn K5 đỡ)");
        {
            var db = Seed(workflow: false); var kho = new KhoVatLieuService(db, new UserService(null));
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, -5, "OVL-1", "A1", null, null, null, "nhap am", 1);
            Ok(!r.Success && r.Error == "Số lượng không được âm." && db.KhoVatLieus.List.Single().SoLuongThucNhan == 40 && db.KhoVatLieuChangeLogs.List.Count == 0,
               "SL nhận −5: chặn, DB giữ 40, không ghi lịch sử", r.Error);
            var r2 = await kho.SaveKhoInputAsync(1, DateTime.Now, 40, "OVL-1", "A1", null, null, -1, "cap am", 1);
            Ok(!r2.Success && r2.Error == "Số lượng không được âm." && db.KhoVatLieus.List.Single().SoLuongCap == null, "SL cấp −1 (trường cũ): chặn", r2.Error);
            var r3 = await kho.SaveKhoInputAsync(1, DateTime.Now, 0, "OVL-1", "A1", null, null, null, "het phoi", 1);
            Ok(r3.Success && db.KhoVatLieus.List.Single().SoLuongThucNhan == 0, "SL nhận 0: lưu được (0 không phải số âm)", r3.Error);
        }

        Console.WriteLine("[A12] KHO-LEN — SaveKhoInputAsync kiểm độ dài theo cột DB (Order / Vị trí 100, Tình trạng 50; tính sau Trim)");
        {
            var db = Seed(); var kho = new KhoVatLieuService(db, new UserService(null));
            async Task<(bool Success, string? Error)> Sv(string? o, string? v, string? t) => await kho.SaveKhoInputAsync(1, DateTime.Now, 40, o, v, t, null, null, "do dai", 1);
            var e1 = await Sv(new string('X', 101), "A1", null);
            var e2 = await Sv("OVL-1", new string('V', 101), null);
            var e3 = await Sv("OVL-1", "A1", new string('T', 51));
            var k = db.KhoVatLieus.List.Single();
            Ok(!e1.Success && e1.Error == "Order vật liệu dài quá 100 ký tự." && !e2.Success && e2.Error == "Vị trí để phôi dài quá 100 ký tự."
               && !e3.Success && e3.Error == "Tình trạng phôi dài quá 50 ký tự." && k.OrderVatLieu == "OVL-1" && k.ViTriDePhoi == "A1" && k.TinhTrangPhoi == null
               && db.KhoVatLieuChangeLogs.List.Count == 0, "Order 101 / Vị trí 101 / Tình trạng 51 ký tự: chặn từng ô, DB không đổi", $"{e1.Error} | {e2.Error} | {e3.Error}");
            var ok100 = await Sv(" " + new string('X', 100) + " ", new string('V', 100), new string('T', 50));
            k = db.KhoVatLieus.List.Single();
            Ok(ok100.Success && k.OrderVatLieu == new string('X', 100) && k.ViTriDePhoi!.Length == 100 && k.TinhTrangPhoi!.Length == 50,
               "Đúng 100 / 100 / 50 ký tự (Order có khoảng trắng 2 đầu → Trim): lưu được", ok100.Error);
        }

        Console.WriteLine("[A13] KHO-STALE — trang mở lúc dòng Kho còn trống (nhập lần đầu) mà người khác đã nhập → không ghi đè");
        {
            var db = Seed(phoi: null); db.KhoVatLieus.List.Clear(); db.Fixup();
            var kho = new KhoVatLieuService(db, new UserService(null));
            var b = await kho.SaveKhoInputAsync(1, DateTime.Now, 30, "OVL-B", "B1", null, null, null, "Nhập lần đầu", 1, expectFirstEntry: true);
            Ok(b.Success && db.KhoVatLieus.List.Count == 1, "B nhập lần đầu (chưa có dòng Kho): lưu được", b.Error);
            var a = await kho.SaveKhoInputAsync(1, DateTime.Now, 25, "OVL-A", "A1", null, null, null, "Nhập lần đầu", 1, expectFirstEntry: true);
            var k = db.KhoVatLieus.List.Single();
            Ok(!a.Success && a.Error!.Contains("vừa được người khác nhập") && k.SoLuongThucNhan == 30 && k.OrderVatLieu == "OVL-B"
               && db.KhoVatLieuChangeLogs.List.Count == 1, "A (trang cũ, vẫn tưởng nhập lần đầu) lưu sau: bị chặn, DB giữ số của B, không thêm lịch sử", a.Error);
            var a2 = await kho.SaveKhoInputAsync(1, DateTime.Now, 25, "OVL-A", "A1", null, null, null, "A sua co ly do", 1);
            Ok(a2.Success && db.KhoVatLieus.List.Single().SoLuongThucNhan == 25, "A tải lại rồi sửa có lý do (không phải nhập lần đầu): lưu được", a2.Error);
        }
        {   // dòng Kho đã có nhưng mọi ô I–M còn trống (vd Tách PO chưa chép gì) → vẫn là nhập lần đầu, không chặn oan
            var db = Seed(phoi: null); var k0 = db.KhoVatLieus.List.Single(); k0.OrderVatLieu = null; k0.ViTriDePhoi = null;
            var kho = new KhoVatLieuService(db, new UserService(null));
            var r = await kho.SaveKhoInputAsync(1, DateTime.Now, 12, "OVL-N", "N1", null, null, null, "Nhập lần đầu", 1, expectFirstEntry: true);
            Ok(r.Success && db.KhoVatLieus.List.Single().SoLuongThucNhan == 12, "Dòng Kho có sẵn nhưng trống hết: nhập lần đầu lưu được", r.Error);
        }

        Console.WriteLine("[A14] KHO-ACTIVE — trang Kho / xem 1 PO / danh sách Kế hoạch / lưu Kho chỉ dùng dòng Kho đang dùng (IsActive)");
        {
            var db = Seed();
            // dòng Kho cũ đã tắt (vd sau Tách PO) đặt TRƯỚC dòng đang dùng: đọc nhầm thì ra 99 / OVL-CU
            db.KhoVatLieus.List.Insert(0, new KhoVatLieu { KhoVatLieuId = 2, PlanDetailId = 1, IsActive = false, SoLuongThucNhan = 99, NgayNhanPhoi = DateTime.Now, OrderVatLieu = "OVL-CU", ViTriDePhoi = "Z9", SoPO = "PO1", PartNo = "P" });
            db.Fixup();
            var kho = new KhoVatLieuService(db, new UserService(null));
            var lst = await kho.GetAllAsync();
            Ok(lst.Count == 1 && lst[0].KhoVatLieuId == 1 && lst[0].SoLuongThucNhan == 40 && lst[0].OrderVatLieu == "OVL-1",
               "GetAllAsync (trang Kho): PO1 đúng 1 dòng, số của dòng đang dùng", string.Join(" | ", lst.Select(x => $"{x.KhoVatLieuId}:{x.SoLuongThucNhan}:{x.OrderVatLieu}")));
            var one = await kho.GetByDetailIdAsync(1);
            Ok(one != null && one.KhoVatLieuId == 1 && one.OrderVatLieu == "OVL-1", "GetByDetailIdAsync (xem 1 PO): dòng đang dùng", one?.OrderVatLieu);
            var kh = await new KhPlanService(db).GetDetailRowsAsync();
            Ok(kh.Count == 1 && kh[0].KhoOrderVL == "OVL-1" && kh[0].KhoViTri == "A1", "KhPlanService.GetDetailRowsAsync (danh sách KH): 1 dòng PO, Order / Vị trí Kho của dòng đang dùng",
               string.Join(" | ", kh.Select(x => $"{x.KhoOrderVL}/{x.KhoViTri}")));
            var s = await kho.SaveKhoInputAsync(1, DateTime.Now, 45, "OVL-1", "A1", null, null, null, "nhan them", 1);
            var cu = db.KhoVatLieus.List.Single(x => x.KhoVatLieuId == 2); var moi = db.KhoVatLieus.List.Single(x => x.KhoVatLieuId == 1);
            Ok(s.Success && moi.SoLuongThucNhan == 45 && cu.SoLuongThucNhan == 99 && !cu.IsActive && db.KhoVatLieuChangeLogs.List.All(l => l.KhoVatLieuId == 1),
               "SaveKhoInputAsync sửa dòng đang dùng (40→45), dòng đã tắt giữ nguyên 99", s.Error);
        }

        Console.WriteLine("[A15] D11-KHO-SL — \"SL đã giao\" trang Kho (GetKhoIssueSummaryAsync) chỉ đếm phiếu Hàng mới");
        {
            var db = Seed(); var svc = new HandoverService(db); var K = new List<string> { "KHO" };
            var rep = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 5, HandoverNgSource.DauRa, null, "kho lam mop", null, 1, K, ngCategoryId: 1);
            var gs = await svc.IssueByCellAsync(1, "KHO", "GC", 5, 1, "LOI", false, "SUA", toNC: "NC1", reason: "gui sua", ngCaseId: VuHelp.LastCase(db));
            var tSua = db.HandoverTransactions.List.Last();
            var s0 = await svc.GetKhoIssueSummaryAsync(new[] { 1 });
            Ok(rep.Ok && gs.Ok && tSua.Purpose == HandoverPurpose.RepairSend && s0.Sum(x => x.TotalConfirmed + x.TotalPending) == 0 && s0.All(x => x.FirstIssuedAt == null),
               "Kho báo lỗi 5 phôi rồi gửi GC sửa 5 (phiếu Hàng lỗi, chờ nhận): SL đã giao 0, chờ 0, chưa có ngày giao", $"{rep.Error}{gs.Error} · {string.Join(",", s0.Select(x => $"{x.TotalConfirmed}/{x.TotalPending}/{x.FirstIssuedAt}"))}");
            var g1 = await svc.IssueByCellAsync(1, "KHO", "GC", 12, 1, "MOI", false, "MOI", toNC: "NC1");
            var t1 = db.HandoverTransactions.List.Last(); await svc.ReceiveAsync(t1.HandoverTxId, 12, 0, null, 2);
            var g2 = await svc.IssueByCellAsync(1, "KHO", "GC", 3, 1, "MOI", false, "MOI", toNC: "NC1");
            var s1 = (await svc.GetKhoIssueSummaryAsync(new[] { 1 })).SingleOrDefault();
            Ok(g1.Ok && g2.Ok && s1 != null && s1.TotalConfirmed == 12 && s1.TotalPending == 3 && s1.FirstIssuedAt == t1.IssuedAt,
               "Thêm Hàng mới 12 (GC nhận) + 3 (chờ): đã giao 12, chờ 3, ngày giao = phiếu Hàng mới đầu (không phải phiếu gửi sửa)", s1 == null ? "không có dòng" : $"{s1.TotalConfirmed}/{s1.TotalPending}");
        }

        // [E3-NGREP 2026-11-02] "PO trắng" (PoGuard.CountDocsAsync) đếm cả phiếu báo hàng lỗi sau nhận (HandoverNgReports, kể cả đã huỷ):
        //   Kho báo 2 phôi lỗi → huỷ báo → đưa phôi nhận về 0 → PO chỉ còn 1 phiếu báo lỗi đã huỷ (lịch sử, có khoá ngoại vụ lỗi) → Xoá / Tách PO phải bị chặn.
        Console.WriteLine("[A16] E3-NGREP — PO chỉ còn phiếu báo hàng lỗi (đã huỷ): không còn là PO trắng → Xoá / Tách bị chặn");
        {
            var db = Seed(); var svc = new HandoverService(db); var kho = new KhoVatLieuService(db, new UserService(null)); var kh = new KhPlanService(db);
            var rp = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauRa, null, "kho lam mop", null, 1, new List<string> { "KHO" }, ngCategoryId: 1);
            var vr = await svc.VoidNgReportAsync(db.HandoverNgReports.List.Last().HandoverNgReportId, "bao nham", 1, new List<string>());
            var k0 = await kho.SaveKhoInputAsync(1, DateTime.Now, 0, "OVL-1", "A1", null, null, null, "tao nham", 1);
            var docs = await PoGuard.CountDocsAsync(db, 1);
            Ok(rp.Ok && vr.Ok && k0.Success && docs.PhoiNhan == 0 && docs.WtsAll == 0 && docs.PhieuAll == 0 && docs.BaoLoiAll == 1 && docs.BaoLoiActive == 0,
               "Dựng: báo 2 phôi lỗi → huỷ báo → phôi nhận về 0; PoGuard đếm 1 phiếu báo lỗi (0 còn hiệu lực), không còn chứng từ nào khác",
               $"{rp.Error}{vr.Error}{k0.Error} · phôi {docs.PhoiNhan} WTS {docs.WtsAll} phiếu {docs.PhieuAll} báo lỗi {docs.BaoLoiAll}/{docs.BaoLoiActive}");
            Ok(docs.HasAny && docs.Text() == "1 phiếu báo hàng lỗi (1 phiếu đã huỷ)", "HasAny = true, Text nêu đúng '1 phiếu báo hàng lỗi (1 phiếu đã huỷ)'", docs.Text());
            EfRaw().Clear();
            var d = await kh.DeleteDetailAsync(1, 1, "tao nham");
            Ok(!d.Success && d.Error!.Contains("1 phiếu báo hàng lỗi (1 phiếu đã huỷ)") && db.KhPlanDetails.List.Count == 1 && EfRaw().Count == 0,
               "Xoá PO: chặn, lời báo nêu phiếu báo lỗi, không chạy lệnh xoá nào", d.Error);
            string? err = null; try { await kh.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 20 }, new() { PurchaseOrder = "PO1B", Quantity = 20 } }, 1, "tach"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("không tách được") && err.Contains("1 phiếu báo hàng lỗi") && db.KhPlanDetails.List.Count == 1, "Tách PO: chặn, không sinh PO mới", err);
        }

        Console.WriteLine("[A17] [B3-SO-NGUYEN 06/11] Máy chủ chỉ nhận số nguyên: tạo KH / tách PO / sửa SL PO / lập phiếu / nhận / báo lỗi / form / WTS / Kết quả sửa — số lẻ bị chặn, không ghi gì");
        {
            var db = Seed(); var kh = new KhPlanService(db); var svc = new HandoverService(db);
            WtsSvcHelp.Reset();
            int Docs() => db.HandoverTransactions.List.Count + db.HandoverReceives.List.Count + db.HandoverNgReports.List.Count + db.WtsProductionLogs.List.Count + db.KhPlanDetails.List.Count + db.HandoverRepairResults.List.Count;
            string? E(Func<Task> a) { try { a().GetAwaiter().GetResult(); return null; } catch (Exception ex) { return ex.Message; } }
            int d0 = Docs();
            var plan = new KhPlan { PlanDate = DateTime.Today, CustomerId = 1, Customer = db.Customers.List[0] };
            plan.Details.Add(new KhPlanDetail { PurchaseOrder = "PO-LE", PartNo = "P", Quantity = 10.5m });
            var eCreate = E(() => kh.CreateAsync(plan, 1));
            var dbS = Seed(phoi: null); var khS = new KhPlanService(dbS);   // tách chỉ được khi PO trắng
            var eSplit = E(() => khS.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 20.5m }, new() { PurchaseOrder = "PO1B", Quantity = 19.5m } }, 1, "tach"));
            Ok(eSplit != null && dbS.KhPlanDetails.List.Count == 1, "Tách PO trắng 20.5 + 19.5 → chặn, không sinh PO mới", eSplit);
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 40.25m;
            var eEdit = await kh.SaveEditAsync(1, 1, o, n, 1, "sua sl le");
            Ok(eCreate != null && eCreate.Contains("số nguyên") && eSplit != null && eSplit.Contains("số nguyên") && eEdit != null && eEdit.Contains("số nguyên")
               && db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 1).Quantity == 40 && Docs() == d0,
               "KH: tạo PO SL 10.5 / tách 20.5 + 19.5 / sửa SL 40.25 → chặn, SL giữ 40", $"{eCreate} | {eSplit} | {eEdit}");
            var okSplit = E(() => khS.SplitPOAsync(1, new() { new() { PurchaseOrder = "PO1A", Quantity = 21 }, new() { PurchaseOrder = "PO1B", Quantity = 19 } }, 1, "tach"));
            Ok(okSplit == null && dbS.KhPlanDetails.List.Count(x => x.PurchaseOrder.StartsWith("PO1")) >= 2, "Tách PO trắng 21 + 19 (số nguyên) → được", okSplit);
            var i1 = await svc.IssueByCellAsync(1, "KHO", "GC", 2.5m, 1, "MOI", false, "MOI", null, "NC1");
            Ok(!i1.Ok && i1.Error.Contains("số nguyên") && Docs() == d0, "Lập phiếu Kho → GC 2.5 → chặn", i1.Error);
            var i2 = await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC1");
            var tx = db.HandoverTransactions.List.Last().HandoverTxId; int d1 = Docs();
            var r1 = await svc.ReceiveAsync(tx, 9.5m, 0.5m, null, 2);
            Ok(i2.Ok && !r1.Ok && r1.Error.Contains("số nguyên") && Docs() == d1, "Nhận OK 9.5 + NG 0.5 → chặn", r1.Error);
            var r2 = await svc.ReceiveAsync(tx, 10, 0, null, 2);
            int d2 = Docs();
            var w1 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 4.5m, 0, 2);
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 4, 0.5m, 2, 1);
            Ok(r2.Ok && !w1.Ok && !w2.Ok && (w1.Err ?? "").Contains("số nguyên") && (w2.Err ?? "").Contains("số nguyên") && Docs() == d2, "WTS OK 4.5 / NG 0.5 → chặn", $"{w1.Err} | {w2.Err}");
            var b1 = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 1.5m, HandoverNgSource.DauRa, null, "", null, 1, new List<string> { "GC" }, ngCategoryId: 1);
            var f1 = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = 1, Group = "GC", UserId = 1, Kind = HandoverForm.Loi, Pos = "NC:NC1", Action = HandoverForm.AGiu, Qty = 1.5m, CatId = 1 });
            Ok(!b1.Ok && b1.Error.Contains("số nguyên") && !f1.Ok && f1.Error.Contains("số nguyên") && Docs() == d2, "Báo lỗi 1.5 / form báo lỗi 1.5 → chặn", $"{b1.Error} | {f1.Error}");
            var w3 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 8, 2, 2, 1);
            var row = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            var vu = row!.CasesHeldAt("GC", HandoverCell.Loi).First().Id;
            var k1 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1.5m, 0, null, null, 1);
            Ok(w3.Ok && !k1.Ok && k1.Error.Contains("số nguyên"), "Kết quả sửa đạt 1.5 → chặn (trước: nhận tới 2 số lẻ)", k1.Error);
            var w4 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 3, 0, 2);
            Ok(w4.Ok, "Số nguyên vẫn lưu bình thường (WTS NC2 OK 3)", w4.Err);
        }

        Console.WriteLine("[A18] [WTS-AM 06/11] WTS OK / NG âm bị chặn ở máy chủ (cả Workflow OFF — trước: OK 5 + NG -2 lưu được, ghi Done 3 / NG -2)");
        {
            var db = Seed(workflow: false); WtsSvcHelp.Reset();
            int W() => db.WtsProductionLogs.List.Count(x => !x.IsVoided);
            var a = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 5, -2, 2);
            var b = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, -1, 3, 2);
            Ok(!a.Ok && !b.Ok && (a.Err ?? "").Contains("âm") && (b.Err ?? "").Contains("âm") && W() == 0, "Thêm WTS OK 5 / NG -2 và OK -1 / NG 3 → chặn, không ghi", $"{a.Err} | {b.Err} | W={W()}");
            var c = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 5, 0, 2);
            var e = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 6, -1, 2, null, null, c.LogId);
            var old = db.WtsProductionLogs.List.FirstOrDefault(x => x.WtsLogId == c.LogId);
            Ok(c.Ok && !e.Ok && (e.Err ?? "").Contains("âm") && old != null && !old.IsVoided && old.QtyNg == 0 && W() == 1, "Sửa dòng WTS sang NG -1 → chặn, dòng cũ giữ nguyên (không bị huỷ)", $"{e.Err} | voided={old?.IsVoided} ng={old?.QtyNg} W={W()}");
        }

        // [KH-SL-MAY-CHU] [KHO-ADMIN-NHOM] rà độc lập toàn hệ thống 07/11 (Thấp)
        Console.WriteLine("[A19] KH-SL-MAY-CHU — SL PO ≤ 0 / quá lớn bị máy chủ chặn (tạo / sửa); KHO-ADMIN-NHOM — nhóm ADMIN đã ngưng không sửa được Kho");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            string? E(Func<Task> a) { try { a().GetAwaiter().GetResult(); return null; } catch (Exception ex) { return ex.Message; } }
            int n0 = db.KhPlanDetails.List.Count;
            var p0 = new KhPlan { PlanDate = DateTime.Today, CustomerId = 1, Customer = db.Customers.List[0] }; p0.Details.Add(new KhPlanDetail { PurchaseOrder = "PO-0", PartNo = "P", Quantity = 0 });
            var pBig = new KhPlan { PlanDate = DateTime.Today, CustomerId = 1, Customer = db.Customers.List[0] }; pBig.Details.Add(new KhPlanDetail { PurchaseOrder = "PO-BIG", PartNo = "P", Quantity = 5_000_000_000m });
            var e0 = E(() => kh.CreateAsync(p0, 1)); var eBig = E(() => kh.CreateAsync(pBig, 1));
            Ok(e0 != null && e0.Contains("> 0") && eBig != null && eBig.Contains("quá lớn") && db.KhPlanDetails.List.Count == n0, "Tạo PO SL 0 / SL 5 tỷ → chặn, không thêm dòng", $"{e0} | {eBig}");
            var o = EV(db, 1); var n = EV(db, 1); n.Quantity = 0;
            var eEdit = await kh.SaveEditAsync(1, 1, o, n, 1, "sua sl 0");
            n = EV(db, 1); n.Quantity = 2_000_000_000m;
            var eEdit2 = await kh.SaveEditAsync(1, 1, EV(db, 1), n, 1, "sua sl lon");
            Ok(eEdit != null && eEdit.Contains("> 0") && eEdit2 != null && eEdit2.Contains("quá lớn") && db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 1).Quantity == 40, "Sửa SL PO về 0 / 2 tỷ → chặn, SL giữ 40", $"{eEdit} | {eEdit2}");
            // (tách PO: tổng phải bằng SL gốc nên không thể vượt PoMax — máy chủ vẫn kiểm từng dòng)
            // Kho: user nhóm ADMIN nhưng nhóm đã ngưng → không sửa được (trước: ADMIN qua trước khi xét nhóm còn hoạt động)
            var dbK = Seed(); var kho = new KhoVatLieuService(dbK, new UserService(null));
            var gA = dbK.UserGroups.List.FirstOrDefault(g => g.GroupCode == "ADMIN") ?? dbK.Users.List.First(u => u.UserId == 1).Group!;
            gA.IsActive = false;
            var rk = await kho.SaveKhoInputAsync(1, DateTime.Now, 45, "OVL-1", "A1", null, null, null, "nhan them", 1);
            Ok(!rk.Success && (rk.Error ?? "").Contains("không có quyền"), "Nhóm ADMIN đã ngưng: sửa Kho bị chặn", rk.Error);
            gA.IsActive = true;
            var rk2 = await kho.SaveKhoInputAsync(1, DateTime.Now, 45, "OVL-1", "A1", null, null, null, "nhan them", 1);
            Ok(rk2.Success, "Nhóm ADMIN hoạt động: sửa Kho được như cũ", rk2.Error);
        }

        // [WTS-MA-TRIM] rà Gói 7 vòng 3 (Trung): K6-NOTRUN so NC phiếu (Gói 7 lưu đã Trim) với NC Part Master (không Trim) → phải so 2 phía đã Trim
        Console.WriteLine("[A20] WTS-MA-TRIM — làm mới quy trình: phiếu Kho → GC vào 'NC1' + Part Master NC 'NC1 ' → không chặn oan");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 701, PartId = 1, NC = "NC1 ", StepOrder = 1, IsActive = true, IsBackup = false });
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9701, KhPlanDetailId = 1, FromGroupCode = "KHO", ToGroupCode = "GC", Purpose = HandoverPurpose.New, ToCell = HandoverCell.Moi, ToNC = "NC1", QtyIssued = 1, Status = "PENDING", IssuedAt = DateTime.Now });
            db.Fixup();
            var b1 = await kh.RouteRefreshBlockAsync(1, 1);
            Ok(b1 == null, "Part Master 'NC1 ' + phiếu vào 'NC1': không chặn", b1);
            db.PartMachiningSteps.List.First(x => x.StepId == 701).NC = "NC9";
            var b2 = await kh.RouteRefreshBlockAsync(1, 1);
            Ok(b2 != null && b2.Contains("không còn NC đó"), "Đối chứng: Part Master chỉ còn NC9 → chặn", b2);
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
    static List<string> EfRaw() => Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog;
}
