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
        for (int u = 1; u <= 5; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : null, GroupId = u == 1 ? 1 : null });
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
            var r = await kh.DeleteDetailAsync(1, 1);
            Ok(!r.Success && r.Error!.Contains("phôi Kho đã nhận 40"), "PO có phôi nhận: không xoá", r.Error);
            Ok(db.KhPlanDetails.List.Count == 1 && EfRaw().Count == 0, "Không chạy lệnh xoá nào");
        }
        {
            var db = Seed(phoi: null); Wts(db, "NC1", 5, voided: true); var kh = new KhPlanService(db);
            var r = await kh.DeleteDetailAsync(1, 1);
            Ok(!r.Success && r.Error!.Contains("1 dòng WTS (1 dòng đã huỷ)"), "PO chỉ có WTS đã huỷ: vẫn không xoá (WTS là lịch sử)", r.Error);
        }
        {
            var db = Seed(phoi: null); var kh = new KhPlanService(db); EfRaw().Clear();
            var r = await kh.DeleteDetailAsync(1, 1);
            Ok(r.Success && db.KhPlanDetails.List.Count == 0, "PO trắng: xoá được", r.Error);
            Ok(EfRaw().Any(x => x.Contains("UPDATE KhoVatLieus SET IsActive = 0")) && !EfRaw().Any(x => x.Contains("DELETE FROM WtsProductionLogs")) && !EfRaw().Any(x => x.Contains("DELETE FROM KhoVatLieus")),
               "Dòng Kho tắt IsActive, không xoá cứng WTS / Kho");
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

        // ── [GN-NG-AFTER] Báo hàng lỗi phát hiện sau khi nhận ─────────────
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        Console.WriteLine("[B1] Kho nhận 40 phôi, sau phát hiện 3 phôi lỗi → báo lỗi → trả khách");
        {
            var db = Seed(); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var r0 = await led.GetLedgerForDetailAsync(1);
            Ok(r0!.G("KHO").LoiGiu == 0 && HandoverLedgerService.IssueCap(r0, "KHO", HandoverPurpose.CustReturn, null, HandoverCell.Loi).Max == 0, "Trước khi báo: Kho tồn hàng lỗi 0 (ô Hàng lỗi xám như ảnh)");
            var deny = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 2, new List<string> { "GC" });
            Ok(!deny.Ok, "Người nhóm GC không báo lỗi thay Kho được", deny.Error);
            var over = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 41, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, new List<string> { "KHO" });
            Ok(!over.Ok && over.Error.Contains("tối đa 40"), "Báo 41 > phôi còn 40: chặn", over.Error);
            var r = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, new List<string> { "KHO" });
            var r1 = await led.GetLedgerForDetailAsync(1);
            Ok(r.Ok && r1!.G("KHO").ConGiao == 37 && r1.G("KHO").LoiGiu == 3 && r1.G("KHO").Ton == 40, "Báo 3: Phôi còn 37, Tồn hàng lỗi 3, Tồn Kho vẫn 40", r.Error);
            var cr = await svc.IssueSpecialAsync(1, "KHO", "KHACH", 3, 1, HandoverPurpose.CustReturn, reason: "phoi ro khi");
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
            var r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, null, 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" });
            Ok(!r.Ok && r.Error.Contains("NC"), "GC không chọn NC: bắt chọn NC", r.Error);
            r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 31, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" });
            Ok(!r.Ok && r.Error.Contains("tối đa 30"), "NC1 còn chờ 30: báo 31 bị chặn", r.Error);
            r = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, new List<string> { "GC" });
            var row = await led.GetLedgerForDetailAsync(1);
            var nc1 = row!.G("GC").Steps.First(x => x.Code == "NC1");
            Ok(r.Ok && nc1.Waiting == 28 && row.G("GC").LoiGiu == 2 && row.G("GC").ChuaLam == 28 + row.G("GC").Steps.First(x => x.Code == "NC2").Waiting,
               $"NC1 chờ 30→28, Tồn hàng lỗi GC 2, Chưa GC = Σ chờ các NC", r.Error);
            var cap = HandoverLedgerService.WtsCap(row, "GC", "NC1");
            Ok(cap.Max == 28, $"Trần WTS NC1 giảm theo: {cap.Max}");
            var mv = await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", false, "LOI", reason: "phoi nut");
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
            var r = await svc.ReportNgAfterReceiveAsync(1, "HTSP", HandoverNgSource.Da, null, 1, HandoverNgSource.DauRa, null, "xuoc", null, 3, new List<string> { "HTSP" });
            Ok(!r.Ok, "HTSP chưa có hàng: không báo được", r.Error);
            var r2 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauRa, null, "kho lam mop", null, 1, new List<string> { "KHO" });
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
            await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 2, HandoverNgSource.DauVao, "KHACH", "phoi ro", null, 1, new List<string> { "KHO" });
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
            Ok((await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 41, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, K)).Ok == false, "Bài 1: báo 41 bị chặn");
            Ok((await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 3, HandoverNgSource.DauVao, "KHACH", "phoi ro khi", null, 1, K)).Ok, "Bài 1: báo 3");
            Ok((await svc.IssueSpecialAsync(1, "KHO", "KHACH", 3, 1, HandoverPurpose.CustReturn, reason: "phoi ro khi")).Ok, "Bài 1: trả khách 3");
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 30, 1, "MOI", false, "MOI")).Ok, "Bài 2: Kho giao 30");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 30, 0, null, 2);
            Wts(db, "NC1", 10);
            Ok(!(await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, null, 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, Gc)).Ok, "Bài 2: bỏ trống NC bị chặn");
            Ok((await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauVao, "KHO", "phoi nut", null, 2, Gc)).Ok, "Bài 2: GC báo 2 ở NC1");
            var rw = (await led.GetLedgerForDetailAsync(1))!;
            Ok(HandoverLedgerService.WtsCap(rw, "GC", "NC1").Max == 18, "Bài 2: WTS NC1 chưa làm 18");
            Ok((await svc.IssueByCellAsync(1, "GC", "KHO", 2, 2, "LOI", false, "LOI", reason: "phoi nut")).Ok, "Bài 2: GC giao Hàng lỗi → Kho ô Hàng lỗi");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 0, 2, "phoi nut", 1);
            var rep3 = await svc.ReportNgAfterReceiveAsync(1, "KHO", HandoverNgSource.Phoi, null, 1, HandoverNgSource.DauRa, null, "thu bao nham", null, 1, K);
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

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
    static List<string> EfRaw() => Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog;
}
