// [RS-C1][RS-C2][RS-E8][RS-E15][RS-E16] 29/10/2026 — kịch bản `quyen`: kiểm quyền ma trận ở MÁY CHỦ + danh mục mã CV / khách hàng
//   + làm mới quy trình với Part trống + khoá cấp số Kế hoạch. Gọi HÀM THẬT của service (không qua trang).
using System.Text.Json;
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class QuyenScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static string Perm(params (string Key, bool View, bool Edit)[] ps)
        => JsonSerializer.Serialize(ps.Select(p => new GroupPermissionSetting { SectionCode = p.Key, CanView = p.View, CanEdit = p.Edit }).ToList());

    // u1 Admin · u2 chỉ Sửa Giao nhận GC · u3 chỉ XEM Giao nhận HTSP · u4 không nhóm · u5 Admin nhưng tài khoản khoá
    // u6 Sửa Kế hoạch (không có Khách hàng) · u7 Xem danh mục (Thiết bị, Dao, Đồ gá, WTS tiêu chuẩn, Cấu hình nhóm WTS, Khách hàng)
    // u8 Sửa danh mục + cấu hình nhóm WTS
    const int Admin = 1, GcOnly = 2, HtspView = 3, NoGroup = 4, Locked = 5, KhEdit = 6, CatView = 7, CatEdit = 8;
    static AppDbContext Seed()
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        var gcG = new UserGroup { GroupId = 2, GroupCode = "GC", IsActive = true, Permissions = Perm((SystemPermissions.HandoverGc, true, true), (SystemPermissions.HandoverKho, true, false)) };
        var htspG = new UserGroup { GroupId = 3, GroupCode = "HTSP", IsActive = true, Permissions = Perm((SystemPermissions.HandoverHtsp, true, false)) };
        var khG = new UserGroup { GroupId = 6, GroupCode = "KH", IsActive = true, Permissions = Perm((SystemPermissions.KeHoach, true, true)) };
        var catV = new UserGroup { GroupId = 7, GroupCode = "CATV", IsActive = true, Permissions = Perm(
            (SystemPermissions.ThietBi, true, false), (SystemPermissions.Dao, true, false), (SystemPermissions.DoGa, true, false),
            (SystemPermissions.WtsMaster, true, false), (SystemPermissions.WtsMapping, true, false), (SystemPermissions.KhachHang, true, false)) };
        var catE = new UserGroup { GroupId = 8, GroupCode = "CATE", IsActive = true, Permissions = Perm(
            (SystemPermissions.ThietBi, true, true), (SystemPermissions.Dao, true, true), (SystemPermissions.DoGa, true, true),
            (SystemPermissions.WtsMaster, true, true), (SystemPermissions.WtsMapping, true, true), (SystemPermissions.KhachHang, true, true)) };
        db.UserGroups.List.AddRange(new[] { adminG, gcG, htspG, khG, catV, catE });
        void U(int id, UserGroup? g, bool active = true)
            => db.Users.List.Add(new User { UserId = id, Username = "u" + id, FullName = "U" + id, IsActive = active, Group = g, GroupId = g?.GroupId, PIN = "1111" });
        U(Admin, adminG); U(GcOnly, gcG); U(HtspView, htspG); U(NoGroup, null); U(Locked, adminG, active: false);
        U(KhEdit, khG); U(CatView, catV); U(CatEdit, catE);
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 40, NgayNhanPhoi = DateTime.Now, SoPO = "PO1", PartNo = "P" });
        var m1 = new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" };
        db.KhPlanRouteSnapshotMachining.List.Add(m1);
        db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "2T", WtsTaskCode = "2T" });
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }
    static void WtsGc(AppDbContext db, decimal ok)
    {
        db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = "GC",
            NC = "NC1", WorkerId = 2, QtyDone = ok, QtyOk = ok, QtyNg = 0, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
        db.Fixup();
    }
    static string Snap(AppDbContext db) => $"tx={db.HandoverTransactions.List.Count} recv={db.HandoverReceives.List.Count}";

    public static async Task<int> Run()
    {
        Console.WriteLine("[Q1] RS-C2 — Giao / Nhận: máy chủ kiểm người lập / người nhận có quyền SỬA nhóm (đọc ma trận từ DB)");
        {
            var db = Seed(); var svc = new HandoverService(db);
            var s0 = Snap(db);
            var r = await svc.IssueByCellAsync(1, "KHO", "GC", 40, GcOnly, "MOI", false, "MOI");
            Ok(!r.Ok && r.Error.Contains("không có quyền sửa nhóm") && r.Error.Contains("lập phiếu giao"), "u2 chỉ có quyền Sửa GC (Kho chỉ Xem) lập phiếu Kho → GC: bị chặn", r.Error);
            Ok(Snap(db) == s0, "Bị chặn: không ghi phiếu nào", Snap(db));
            r = await svc.IssueByCellAsync(1, "KHO", "GC", 40, NoGroup, "MOI", false, "MOI");
            Ok(!r.Ok && r.Error.Contains("không có quyền sửa nhóm"), "u4 không có nhóm: bị chặn", r.Error);
            r = await svc.IssueByCellAsync(1, "KHO", "GC", 40, Locked, "MOI", false, "MOI");
            Ok(!r.Ok && r.Error.Contains("không có quyền sửa nhóm"), "u5 nhóm Admin nhưng tài khoản đã khoá: bị chặn", r.Error);
            r = await svc.IssueByCellAsync(1, "KHO", "GC", 40, Admin, "MOI", false, "MOI");
            Ok(r.Ok, "Admin lập phiếu Kho → GC 40: được", r.Error);
            var tx1 = db.HandoverTransactions.List.Last().HandoverTxId;
            var rr = await svc.ReceiveAsync(tx1, 40, 0, null, HtspView);
            Ok(!rr.Ok && rr.Error.Contains("không có quyền sửa nhóm") && rr.Error.Contains("nhận hàng"), "u3 (HTSP chỉ Xem) bấm Nhận phiếu vào GC: bị chặn", rr.Error);
            Ok(db.HandoverReceives.List.Count == 0, "Bị chặn: không ghi lần nhận");
            rr = await svc.ReceiveAsync(tx1, 40, 0, null, GcOnly);
            Ok(rr.Ok, "u2 (Sửa GC) nhận phiếu vào GC: được", rr.Error);
            WtsGc(db, 10);
            r = await svc.IssueByCellAsync(1, "GC", "HTSP", 10, GcOnly, "MOI", false, "MOI");
            Ok(r.Ok, "u2 lập phiếu GC → HTSP 10: được (nhóm của mình)", r.Error);
            var tx2 = db.HandoverTransactions.List.Last().HandoverTxId;
            rr = await svc.ReceiveAsync(tx2, 10, 0, null, GcOnly);
            Ok(!rr.Ok && rr.Error.Contains("không có quyền sửa nhóm"), "u2 (chỉ Sửa GC) tự bấm Nhận vào HTSP: bị chặn (nhận thay nhóm khác)", rr.Error);
            rr = await svc.ReceiveAsync(tx2, 10, 0, null, HtspView);
            Ok(!rr.Ok, "u3 chỉ Xem HTSP: không nhận được", rr.Error);
            var sx = await svc.IssueSpecialAsync(1, "KHO", "HUY", 1, GcOnly, HandoverPurpose.Scrap, reason: "x");
            Ok(!sx.Ok && sx.Error.Contains("không có quyền sửa nhóm"), "u2 lập phiếu Huỷ hàng lỗi của Kho: bị chặn", sx.Error);
            var row = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            Ok(row!.G("GC").ConGiao == 0 && row.G("HTSP").ChoNhan == 10, "Sổ sau chuỗi trên: GC đã giao hết, HTSP chờ nhận 10 (các lần bị chặn không đổi số)",
                $"GC ConGiao={row.G("GC").ConGiao} HTSP ChoNhan={row.G("HTSP").ChoNhan}");
            rr = await svc.ReceiveAsync(tx2, 10, 0, null, Admin);
            Ok(rr.Ok, "Admin nhận vào HTSP: được (Admin = mọi nhóm)", rr.Error);
            // phiếu Huỷ / Trả khách không có bên nhận: lời báo cũ giữ nguyên (không bị lời "không có quyền nhóm Huỷ" che)
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 900, KhPlanDetailId = 1, FromGroupCode = "KHO", ToGroupCode = "HUY", Purpose = HandoverPurpose.Scrap, QtyIssued = 1, Status = "COMPLETED", IssuedBy = Admin, IssuedAt = DateTime.Now });
            db.Fixup();
            rr = await svc.ReceiveAsync(900, 1, 0, null, Admin);
            Ok(!rr.Ok && !rr.Error.Contains("không có quyền sửa nhóm"), "Bấm Nhận phiếu Huỷ (không có bên nhận): không báo nhầm 'không có quyền nhóm Huỷ'", rr.Error);
        }

        Console.WriteLine("[Q2] RS-C1 — danh mục: service chặn khi không có quyền Sửa (trước: chỉ cần đăng nhập)");
        {
            var db = Seed(); var auth = new AuthService(db);
            var tb = new ThietBiService(db, auth);
            var t = await tb.CreateAsync(new ThietBi { SoMay = "M9", TenMay = "May 9" }, CatView);
            Ok(!t.Success && t.Error!.Contains("không có quyền Sửa mục Thiết bị"), "Thiết bị: chỉ Xem → thêm máy bị chặn", t.Error);
            Ok(!db.ThietBis.List.Any(), "Bị chặn: không có dòng máy mới");
            t = await tb.CreateAsync(new ThietBi { SoMay = "M9", TenMay = "May 9" }, CatEdit);
            Ok(t.Success && db.ThietBis.List.Count == 1, "Thiết bị: có quyền Sửa → thêm được", t.Error);
            var m9 = db.ThietBis.List.Single();
            t = await tb.SoftDeleteAsync(m9.ThietBiId, GcOnly, "1111");
            Ok(!t.Success && t.Error!.Contains("quyền Sửa") && m9.IsActive, "Thiết bị: u2 (không quyền) xoá máy: bị chặn trước cả PIN", t.Error);
            t = await tb.UpdateAsync(new ThietBi { ThietBiId = m9.ThietBiId, SoMay = "M9X", TenMay = "x" }, "doi", null, null, NoGroup, "1111");
            Ok(!t.Success && m9.SoMay == "M9", "Thiết bị: u4 sửa số máy: bị chặn", t.Error);

            var cs = new CustomerService(db);
            var c = await cs.CreateAsync(new Customer { CustomerCode = "QM", CustomerName = "Quang Minh" }, CatView);
            Ok(!c.Success && c.Error!.Contains("Khách hàng"), "Khách hàng: chỉ Xem → thêm bị chặn", c.Error);
            c = await cs.CreateAsync(new Customer { CustomerCode = "QM", CustomerName = "Quang Minh" }, KhEdit);
            Ok(c.Success, "Khách hàng: người có quyền Sửa Kế hoạch tạo nhanh khách mới (trang Tạo KH): được", c.Error);
            var qm = db.Customers.List.Single(x => x.CustomerCode == "QM");
            var cu = await cs.UpdateAsync(new Customer { CustomerId = qm.CustomerId, CustomerCode = "QM", CustomerName = "Doi ten" }, "x", KhEdit);
            Ok(!cu.Success && qm.CustomerName == "Quang Minh", "Khách hàng: quyền Kế hoạch KHÔNG sửa được khách (chỉ tạo nhanh)", cu.Error);
            cu = await cs.ToggleActiveAsync(qm.CustomerId, false, null, GcOnly);
            Ok(!cu.Success && qm.IsActive, "Khách hàng: u2 ngừng hoạt động khách: bị chặn", cu.Error);

            var dao = new DaoService(db);
            var dd = await dao.CreateAsync(new Dao { TenDao = "D10" }, CatView);
            Ok(!dd.Success && !db.Daos.List.Any(), "Dao: chỉ Xem → thêm bị chặn", dd.Error);
            dd = await dao.CreateAsync(new Dao { TenDao = "D10" }, CatEdit);
            Ok(dd.Success, "Dao: có quyền Sửa → thêm được", dd.Error);

            var dg = new DoGaService(db, auth);
            var g = await dg.CreateAsync(new DoGa { TenDoGa = "G1" }, CatView, "1111");
            Ok(!g.Success && !db.DoGas.List.Any(), "Đồ gá: chỉ Xem → thêm bị chặn", g.Error);
            g = await dg.CreateAsync(new DoGa { TenDoGa = "G1" }, CatEdit, "1111");
            Ok(g.Success && db.DoGas.List.Count == 1, "Đồ gá: có quyền Sửa → thêm được", g.Error);
            var g1 = db.DoGas.List.Single();
            g = await dg.MuonDoGaAsync(g1.DoGaId, "Tuan", DateTime.Now, null, null, NoGroup, "1111");
            Ok(!g.Success && g.Error!.Contains("quyền Xem"), "Đồ gá: u4 (không quyền xem) mượn: bị chặn", g.Error);
            g = await dg.MuonDoGaAsync(g1.DoGaId, "Tuan", DateTime.Now, null, null, CatView, "1111");
            Ok(g.Success, "Đồ gá: người có quyền Xem mượn: được (mượn / trả là việc hằng ngày)", g.Error);

            var wts = new StandardWtsTaskService(db, auth);
            var w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = "9T", TaskName = "Taro 9" }, CatView, "1111");
            Ok(!w.Success && w.Error!.Contains("WTS tiêu chuẩn") && !db.StandardWtsTasks.List.Any(), "WTS tiêu chuẩn: chỉ Xem → thêm mã CV bị chặn", w.Error);
            w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = "9T", TaskName = "Taro 9" }, CatEdit, "1111");
            Ok(w.Success, "WTS tiêu chuẩn: có quyền Sửa → thêm được", w.Error);
            var t9 = db.StandardWtsTasks.List.Single();
            w = await wts.BulkUpdateProductiveAsync(new List<(int, bool)> { (t9.TaskId, false) }, CatView, "1111");
            Ok(!w.Success && t9.IsProductiveTask, "WTS tiêu chuẩn: chỉ Xem → đổi Có ích / Vô ích bị chặn", w.Error);

            var map = new WtsCategoryGroupMappingService(db);
            var mp = await map.SaveGroupMappingAsync("HTSP", new List<(string, string)> { ("TARO", "Taro") }, CatView);
            Ok(!mp.Success && !db.WtsCategoryGroupMappings.List.Any(), "Cấu hình nhóm WTS: chỉ Xem → lưu bị chặn (trước: trang xét quyền Xem là cho lưu)", mp.Error);
            mp = await map.SaveGroupMappingAsync("HTSP", new List<(string, string)> { ("TARO", "Taro") }, CatEdit);
            Ok(mp.Success && db.WtsCategoryGroupMappings.List.Count == 1, "Cấu hình nhóm WTS: có quyền Sửa → lưu được", mp.Error);
        }

        Console.WriteLine("[Q3] RS-E8 — mã trùng sau xoá mềm + đổi / xoá mã đang dùng");
        {
            var db = Seed(); var auth = new AuthService(db);
            var cs = new CustomerService(db);
            var c = await cs.CreateAsync(new Customer { CustomerCode = "QM", CustomerName = "Quang Minh" }, Admin);
            var qm = db.Customers.List.Single(x => x.CustomerCode == "QM");
            await cs.ToggleActiveAsync(qm.CustomerId, false, null, Admin);
            c = await cs.CreateAsync(new Customer { CustomerCode = "qm", CustomerName = "Quang Minh 2" }, Admin);
            Ok(!c.Success && c.Error!.Contains("Ngừng hoạt động") && c.Error.Contains("Khôi phục"), "Khách 'QM' đã ngừng → tạo lại mã 'qm': báo khôi phục (trước: lỗi SQL trùng khoá)", c.Error);
            Ok(db.Customers.List.Count(x => x.CustomerCode.ToLower() == "qm") == 1, "Không thêm dòng trùng mã");
            c = await cs.CreateAsync(new Customer { CustomerCode = "  ", CustomerName = "Khong ma" }, Admin);
            Ok(!c.Success && c.Error!.Contains("không được để trống"), "Tạo khách không có mã: chặn (chỉ mục mã là unique)", c.Error);
            var cu = await cs.UpdateAsync(new Customer { CustomerId = 1, CustomerCode = "QM", CustomerName = "KH" }, "x", Admin);
            Ok(!cu.Success && cu.Error!.Contains("Ngừng hoạt động"), "Đổi mã khách KH1 thành 'QM' của khách đã ngừng: chặn", cu.Error);
            var tg = await cs.ToggleActiveAsync(qm.CustomerId, true, null, Admin);
            Ok(tg.Success && qm.IsActive, "Khôi phục khách 'QM': được, giữ đúng 1 dòng");

            var wts = new StandardWtsTaskService(db, auth);
            var w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = "3T", TaskName = "Taro 3" }, Admin, "1111");
            var t3 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "3T");
            w = await wts.SoftDeleteAsync(t3.TaskId, Admin, "1111");
            Ok(w.Success && !t3.IsActive, "Mã CV 3T chưa dùng: xoá được", w.Error);
            w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = "3t", TaskName = "Taro 3 moi", DefaultUnit = "Chi tiết" }, Admin, "1111");
            Ok(w.Success && db.StandardWtsTasks.List.Count(x => x.TaskCode == "3T") == 1 && t3.IsActive && t3.TaskName == "Taro 3 moi",
               "Thêm lại mã 3T đã xoá: khôi phục ĐÚNG dòng cũ (1 mã = 1 dòng; trước: lỗi SQL trùng khoá)", w.Error);
            Ok(db.StandardWtsTaskChangeLogs.List.Any(l => l.TaskId == t3.TaskId && l.FieldName == "Khôi phục mã đã xóa"), "Có dòng lịch sử 'Khôi phục mã đã xóa'");

            // mã đang dùng trong quy trình Part Master
            db.PartMasters.List.Add(new PartMaster { PartId = 50, PartNo = "PX-50", IsActive = true });
            db.PartBaviaSteps.List.Add(new PartBaviaStep { StepId = 1, PartId = 50, StepOrder = 1, NC = "1B", IsActive = true });
            db.Fixup();
            w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "BAVIA", CategoryName = "Bavia", TaskCode = "1B", TaskName = "Bavia 1" }, Admin, "1111");
            var b1 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "1B");
            w = await wts.SoftDeleteAsync(b1.TaskId, Admin, "1111");
            Ok(!w.Success && w.Error!.Contains("PX-50") && b1.IsActive, "Mã 1B đang dùng ở Part PX-50: không xoá được, báo tên Part", w.Error);
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = b1.TaskId, TaskCode = "1BX", TaskName = "Bavia 1", CategoryCode = "BAVIA", CategoryName = "Bavia" }, "doi", Admin, "1111");
            Ok(!w.Success && w.Error!.Contains("Part Master") && b1.TaskCode == "1B", "Đổi mã 1B → 1BX khi đã dùng: chặn", w.Error);
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = b1.TaskId, TaskCode = "1B", TaskName = "Bavia mot", CategoryCode = "BAVIA", CategoryName = "Bavia" }, "doi ten", Admin, "1111");
            Ok(w.Success && b1.TaskName == "Bavia mot", "Đổi TÊN mã đang dùng: vẫn được", w.Error);
            db.PartMasters.List.Single(x => x.PartId == 50).IsObsolete = true;
            w = await wts.SoftDeleteAsync(b1.TaskId, Admin, "1111");
            Ok(w.Success && !b1.IsActive, "Part PX-50 đã ngưng (Obsolete) → xoá mã 1B được", w.Error);

            // mã đã có trong snapshot PO (2T) → không đổi mã
            w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Taro", TaskCode = "2T", TaskName = "Taro 2" }, Admin, "1111");
            var t2 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "2T");
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = t2.TaskId, TaskCode = "2X", TaskName = "Taro 2", CategoryCode = "TARO", CategoryName = "Taro" }, "doi", Admin, "1111");
            Ok(!w.Success && w.Error!.Contains("snapshot") && t2.TaskCode == "2T", "Mã 2T có trong quy trình (snapshot) PO1: không đổi mã được", w.Error);
            // mã chưa dùng đổi sang mã của dòng đã xoá → chặn rõ (không để SQL báo trùng khoá)
            w = await wts.CreateAsync(new StandardWtsTask { CategoryCode = "KCS", CategoryName = "KCS", TaskCode = "8K", TaskName = "KCS 8" }, Admin, "1111");
            var k8 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "8K");
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = k8.TaskId, TaskCode = "1B", TaskName = "KCS 8", CategoryCode = "KCS", CategoryName = "KCS" }, "doi", Admin, "1111");
            Ok(!w.Success && w.Error!.Contains("đã xóa") && k8.TaskCode == "8K", "Đổi mã 8K thành 1B (thuộc dòng đã xoá): chặn, chỉ cách khôi phục", w.Error);
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = k8.TaskId, TaskCode = "8KA", TaskName = "KCS 8", CategoryCode = "KCS", CategoryName = "KCS" }, "doi", Admin, "1111");
            Ok(w.Success && k8.TaskCode == "8KA", "Mã 8K chưa dùng ở đâu: đổi mã được", w.Error);
            // mã cũ lưu chữ thường đã dùng: chỉ đổi tên → không bị coi là đổi mã, giữ nguyên chữ đã lưu
            db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 500, TaskCode = "2t", TaskName = "cu", CategoryCode = "TARO", CategoryName = "Taro", IsActive = true });
            db.Fixup();
            var t2low = db.StandardWtsTasks.List.Single(x => x.TaskId == 500);
            t2.IsActive = false;   // tránh trùng mã đang dùng với dòng 2T ở trên (chỉ để thử)
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = 500, TaskCode = "2T", TaskName = "Taro hai", CategoryCode = "TARO", CategoryName = "Taro" }, "ten", Admin, "1111");
            Ok(w.Success && t2low.TaskCode == "2t" && t2low.TaskName == "Taro hai", "Mã cũ '2t' (đã dùng ở snapshot) chỉ đổi tên: được, mã giữ '2t'", w.Error);
            // WTS đã ghi mã → không đổi
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 99, KhPlanDetailId = 1, ProcessGroup = "KCS", NC = "8KA", WtsCode = "8KA", WorkerId = 2, QtyDone = 1, QtyOk = 1, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now, IsVoided = true });
            db.Fixup();
            w = await wts.UpdateAsync(new StandardWtsTask { TaskId = k8.TaskId, TaskCode = "8KB", TaskName = "KCS 8", CategoryCode = "KCS", CategoryName = "KCS" }, "doi", Admin, "1111");
            Ok(!w.Success && w.Error!.Contains("WTS"), "Mã 8KA đã có trong WTS (kể cả dòng đã huỷ — vẫn là lịch sử): không đổi mã", w.Error);
        }

        Console.WriteLine("[Q4] RS-E15 — Cập nhật quy trình với Part không còn công đoạn");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            db.KhoVatLieus.List.Clear();   // PO trắng (không phôi / phiếu / WTS) mới cập nhật được (K6)
            db.PartMasters.List.Add(new PartMaster { PartId = 60, PartNo = "P", IsActive = true });
            db.Fixup();
            int snap0 = db.KhPlanRouteSnapshotMachining.List.Count + db.KhPlanRouteSnapshotTaro.List.Count + db.KhPlanRouteSnapshotInspection.List.Count;
            string? err = null; try { await kh.RefreshRouteSnapshotAsync(1, 60, Admin, "cap nhat"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("chưa có công đoạn"), "Part P không có công đoạn: không cập nhật (trước: xoá hết quy trình PO rồi commit rỗng)", err);
            int snap1 = db.KhPlanRouteSnapshotMachining.List.Count + db.KhPlanRouteSnapshotTaro.List.Count + db.KhPlanRouteSnapshotInspection.List.Count;
            Ok(snap1 == snap0 && snap0 == 3, "Quy trình PO giữ nguyên 3 dòng", $"trước {snap0} · sau {snap1}");
            Ok(!db.KhPlanDetailChangeLogs.List.Any(l => l.FieldName == "RouteSnapshot"), "Không ghi log 'RouteSnapshot' GC:0 …");
        }

        Console.WriteLine("[Q5] RS-E16 — cấp số Kế hoạch + kiểm trùng PO dưới khoá chung");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            var raw = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog; raw.Clear();
            var p = new KhPlan { PlanDate = new DateTime(2026, 10, 29), CustomerId = 1 };
            p.Details.Add(new KhPlanDetail { PartNo = "P", PurchaseOrder = "PO-NEW", Quantity = 5 });
            int id = 0; string? err = null; try { id = await kh.CreateAsync(p, Admin); } catch (Exception ex) { err = ex.Message; }
            Ok(err == null && p.PlanNo == "KH-20261029-0001", "Tạo KH không có giao dịch ngoài: tự mở giao dịch, PlanNo KH-20261029-0001", err ?? p.PlanNo);
            Ok(raw.Any(s => s.Contains("sp_getapplock") && s.Contains("'Transaction'")), "Có khoá sp_getapplock (LockOwner = Transaction) quanh cấp số + kiểm trùng PO");
            Ok(db.Database.Current == null, "Giao dịch tự mở đã đóng");
            // trong giao dịch của trang (như Create_Khplan)
            raw.Clear();
            await using (var trx = await db.Database.BeginTransactionAsync())
            {
                var p2 = new KhPlan { PlanDate = new DateTime(2026, 10, 29), CustomerId = 1 };
                p2.Details.Add(new KhPlanDetail { PartNo = "P", PurchaseOrder = "PO-NEW2", Quantity = 5 });
                err = null; try { await kh.CreateAsync(p2, Admin); } catch (Exception ex) { err = ex.Message; }
                Ok(err == null && p2.PlanNo == "KH-20261029-0002" && db.Database.Current == trx, "Tạo KH trong giao dịch của trang: dùng giao dịch đó, PlanNo 0002", err ?? p2.PlanNo);
                await trx.CommitAsync();
            }
            var p3 = new KhPlan { PlanDate = new DateTime(2026, 10, 29), CustomerId = 1 };
            p3.Details.Add(new KhPlanDetail { PartNo = "P", PurchaseOrder = "PO-NEW", Quantity = 5 });
            err = null; try { await kh.CreateAsync(p3, Admin); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("PO-NEW") && db.KhPlans.List.Count == 3, "Số PO đã có: chặn (kiểm dưới khoá), không thêm phiếu", err);
            Ok(db.Database.Current == null, "Bị chặn: giao dịch tự mở đã rollback + đóng");
            // Tách PO: 2 số PO mới chỉ khác hoa / thường hoặc có khoảng trắng → trùng
            var kh2 = new KhPlanService(db);
            var blank = new KhPlanDetail { KhPlanDetailId = 50, KhPlanId = 1, KhPlan = db.KhPlans.List.First(x => x.KhPlanId == 1), PartNo = "P", PurchaseOrder = "PO-TRANG", Quantity = 40, LineNo = 9 };
            db.KhPlanDetails.List.Add(blank); db.Fixup();
            err = null; try { await kh2.SplitPOAsync(50, new() { new() { PurchaseOrder = "po-7", Quantity = 20 }, new() { PurchaseOrder = " PO-7", Quantity = 20 } }, Admin, "tach"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("trùng"), "Tách PO thành 'po-7' và ' PO-7': chặn trùng (trước: lọt cả 2)", err);
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
