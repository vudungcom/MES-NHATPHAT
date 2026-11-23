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
        var d = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1 };
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
            p.Details.Add(new KhPlanDetail { STD = DateTime.Today, PartNo = "P", PurchaseOrder = "PO-NEW", Quantity = 5 });
            int id = 0; string? err = null; try { id = await kh.CreateAsync(p, Admin); } catch (Exception ex) { err = ex.Message; }
            Ok(err == null && p.PlanNo == "KH-20261029-0001", "Tạo KH không có giao dịch ngoài: tự mở giao dịch, PlanNo KH-20261029-0001", err ?? p.PlanNo);
            Ok(raw.Any(s => s.Contains("sp_getapplock") && s.Contains("'Transaction'")), "Có khoá sp_getapplock (LockOwner = Transaction) quanh cấp số + kiểm trùng PO");
            Ok(db.Database.Current == null, "Giao dịch tự mở đã đóng");
            // trong giao dịch của trang (như Create_Khplan)
            raw.Clear();
            await using (var trx = await db.Database.BeginTransactionAsync())
            {
                var p2 = new KhPlan { PlanDate = new DateTime(2026, 10, 29), CustomerId = 1 };
                p2.Details.Add(new KhPlanDetail { STD = DateTime.Today, PartNo = "P", PurchaseOrder = "PO-NEW2", Quantity = 5 });
                err = null; try { await kh.CreateAsync(p2, Admin); } catch (Exception ex) { err = ex.Message; }
                Ok(err == null && p2.PlanNo == "KH-20261029-0002" && db.Database.Current == trx, "Tạo KH trong giao dịch của trang: dùng giao dịch đó, PlanNo 0002", err ?? p2.PlanNo);
                await trx.CommitAsync();
            }
            var p3 = new KhPlan { PlanDate = new DateTime(2026, 10, 29), CustomerId = 1 };
            p3.Details.Add(new KhPlanDetail { STD = DateTime.Today, PartNo = "P", PurchaseOrder = "PO-NEW", Quantity = 5 });
            err = null; try { await kh.CreateAsync(p3, Admin); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("PO-NEW") && db.KhPlans.List.Count == 3, "Số PO đã có: chặn (kiểm dưới khoá), không thêm phiếu", err);
            Ok(db.Database.Current == null, "Bị chặn: giao dịch tự mở đã rollback + đóng");
            // Tách PO: 2 số PO mới chỉ khác hoa / thường hoặc có khoảng trắng → trùng
            var kh2 = new KhPlanService(db);
            var blank = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = 50, KhPlanId = 1, KhPlan = db.KhPlans.List.First(x => x.KhPlanId == 1), PartNo = "P", PurchaseOrder = "PO-TRANG", Quantity = 40, LineNo = 9 };
            db.KhPlanDetails.List.Add(blank); db.Fixup();
            err = null; try { await kh2.SplitPOAsync(50, new() { new() { PurchaseOrder = "po-7", Quantity = 20 }, new() { PurchaseOrder = " PO-7", Quantity = 20 } }, Admin, "tach"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("trùng"), "Tách PO thành 'po-7' và ' PO-7': chặn trùng (trước: lọt cả 2)", err);
        }

        Console.WriteLine("[Q6] RS-E10 — đồ gá không mượn chồng / không trả khi không ai mượn; dao SL tồn không âm (dựng lại ca Q1c FIX-NGAM 31/10)");
        {
            var db = Seed(); var auth = new AuthService(db);
            db.DoGas.List.Add(new DoGa { DoGaId = 1, TenDoGa = "DG1", TrangThai = "Sẵn sàng", IsActive = true }); db.Fixup();
            var dg = new DoGaService(db, auth);
            var tr0 = await dg.TraDoGaAsync(1, null, CatView, "1111");
            Ok(!tr0.Success && tr0.Error == "Đồ gá đang không có ai mượn — không cần trả." && db.DoGaMuonTraLogs.List.Count == 0, "Trả đồ gá không ai mượn: chặn, không ghi lịch sử rỗng", tr0.Error);
            var m1 = await dg.MuonDoGaAsync(1, "Anh A", DateTime.Now, null, "ca 1", CatView, "1111"); db.Fixup();
            Ok(m1.Success && db.DoGas.List[0].TrangThai == "Đang mượn" && db.DoGas.List[0].NguoiMuonHienTai == "Anh A", "Mượn lần 1 (quyền Xem DO_GA + PIN): được", m1.Error);
            var m2 = await dg.MuonDoGaAsync(1, "Anh B", DateTime.Now, null, "ca 2", CatView, "1111"); db.Fixup();
            Ok(!m2.Success && m2.Error!.StartsWith("Đồ gá đang được Anh A mượn") && db.DoGas.List[0].NguoiMuonHienTai == "Anh A"
               && db.DoGaMuonTraLogs.List.Count(x => x.TrangThai == "Đang mượn") == 1, "Mượn chồng: chặn, vẫn đúng 1 phiếu 'Đang mượn' của Anh A", m2.Error);
            var m3 = await dg.MuonDoGaAsync(1, "Anh B", DateTime.Now, null, null, NoGroup, "1111");
            Ok(!m3.Success && m3.Error!.Contains("Đồ gá"), "Không có quyền Xem DO_GA: chặn", m3.Error);
            var tr1 = await dg.TraDoGaAsync(1, "ok", CatView, "1111"); db.Fixup();
            Ok(tr1.Success && db.DoGas.List[0].TrangThai != "Đang mượn" && db.DoGaMuonTraLogs.List.Count(x => x.TrangThai == "Đã trả") == 1, "Trả: được, phiếu mượn chốt 'Đã trả'", tr1.Error);
            var dao = new DaoService(db);
            var c = await dao.CreateAsync(new Dao { TenDao = "D6", SoLuongTon = -1 }, CatEdit);
            Ok(!c.Success && c.Error == "SL tồn không được âm." && !db.Daos.List.Any(x => x.TenDao == "D6"), "Dao: tạo SL tồn âm: chặn", c.Error);
            c = await dao.CreateAsync(new Dao { TenDao = "D6", SoLuongTon = 3 }, CatEdit); db.Fixup();
            var d6 = db.Daos.List.First(x => x.TenDao == "D6");
            var u = await dao.UpdateAsync(new Dao { DaoId = d6.DaoId, TenDao = "D6", SoLuongTon = -2 }, "x", CatEdit);
            Ok(c.Success && !u.Success && u.Error == "SL tồn không được âm." && d6.SoLuongTon == 3, "Dao: sửa SL tồn về âm: chặn, giữ 3", u.Error);
        }

        Console.WriteLine("[Q7] RS-D9 — số máy đã dùng không đổi số; máy trong Part Master đang dùng / máy loại trừ không xoá (dựng lại ca Q1c FIX-NGAM 31/10)");
        {
            var db = Seed(); var auth = new AuthService(db);
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", TenMay = "May 1", IsActive = true });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M7", TenMay = "May 7", IsActive = true });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 3, SoMay = "M8", TenMay = "May 8", IsActive = true });
            db.MachineExcludeSettings.List.Add(new MachineExcludeSetting { Id = 1, SoMay = "M8" });
            db.Fixup();
            var tb = new ThietBiService(db, auth);
            var r = await tb.UpdateAsync(new ThietBi { ThietBiId = 1, SoMay = "M1X", TenMay = "May 1" }, "doi so", null, null, CatEdit, "1111");
            Ok(!r.Success && r.Error!.Contains("Số máy 'M1' đã dùng ở") && r.Error.Contains("quy trình PO (snapshot)") && db.ThietBis.List[0].SoMay == "M1",
               "M1 có trong snapshot PO: đổi số máy bị chặn, nêu chỗ dùng", r.Error);
            r = await tb.UpdateAsync(new ThietBi { ThietBiId = 2, SoMay = "M7X", TenMay = "May 7" }, "doi so", null, null, CatEdit, "1111");
            Ok(r.Success && db.ThietBis.List[1].SoMay == "M7X", "M7 chưa dùng ở đâu: đổi số được", r.Error);
            r = await tb.SoftDeleteAsync(3, CatEdit, "1111");
            Ok(!r.Success && r.Error!.Contains("danh sách máy loại trừ") && db.ThietBis.List[2].IsActive, "M8 trong danh sách máy loại trừ: không xoá", r.Error);
        }
        Console.WriteLine("[Q7b] [B7-TIMING-MAY 06/11] Máy chỉ ghi ở cột Máy bấm giờ (TimingMachine) của Part Master: XOÁ được; ĐỔI số vẫn chặn; máy đăng ký trong quy trình vẫn không xoá");
        {
            var db = Seed(); var auth = new AuthService(db);
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 11, SoMay = "M5", TenMay = "May 5", IsActive = true });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 12, SoMay = "M6", TenMay = "May 6", IsActive = true });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 13, SoMay = "M4", TenMay = "May 4", IsActive = true });
            db.PartMasters.List.Add(new PartMaster { PartId = 71, PartNo = "P-TIM", IsActive = true });
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 711, PartId = 71, StepOrder = 1, NC = "NC1", MachineRegistered = "M6", TimingMachine = "M5", IsActive = true });
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 712, PartId = 71, StepOrder = 2, NC = "NC2", MachineRegistered = "M9", TimingMachine = "M2, m4", IsActive = true });
            db.Fixup();
            var tb = new ThietBiService(db, auth);
            var r = await tb.UpdateAsync(new ThietBi { ThietBiId = 11, SoMay = "M5X", TenMay = "May 5" }, "doi so", null, null, CatEdit, "1111");
            Ok(!r.Success && r.Error!.Contains("Part Master P-TIM") && db.ThietBis.List.Single(x => x.ThietBiId == 11).SoMay == "M5",
               "M5 là máy bấm giờ ở P-TIM: ĐỔI số bị chặn (giữ truy vết chữ máy bấm giờ)", r.Error);
            r = await tb.SoftDeleteAsync(11, CatEdit, "1111");
            Ok(r.Success && !db.ThietBis.List.Single(x => x.ThietBiId == 11).IsActive, "M5 chỉ là máy bấm giờ: XOÁ được (trước bị chặn)", r.Error);
            r = await tb.SoftDeleteAsync(13, CatEdit, "1111");
            Ok(r.Success, "M4 nằm trong CSV máy bấm giờ \"M2, m4\": XOÁ được", r.Error);
            r = await tb.SoftDeleteAsync(12, CatEdit, "1111");
            Ok(!r.Success && r.Error!.Contains("Part Master P-TIM") && db.ThietBis.List.Single(x => x.ThietBiId == 12).IsActive, "M6 là máy đăng ký NC1 của P-TIM: vẫn KHÔNG xoá", r.Error);
        }

        // [WTS-ORDER-SAVE 2026-11-02] WTS tiêu chuẩn mục 14: chỉ đổi Thứ tự hiển thị phải LƯU + có dòng lịch sử
        //   (trước: không có dòng log nào → bỏ qua cả lượt lưu mà vẫn báo thành công).
        // [RS-D9-PO-MO 2026-11-16] Gói 24 câu 10 (Hùng 15/11 15:10 OK): máy còn trong snapshot / WTS của PO CHƯA Đủ thành phẩm → không xoá; PO Đủ → xoá được.
        //   Đi đủ chuỗi bằng hàm thật (Kho → GC → KCS → PKG → Kho thành phẩm) để PO ra "DU". Bản cdbd991: xoá được khi Part đã ngưng → SAI.
        Console.WriteLine("[Q13] RS-D9-PO-MO — xoá máy đang trong quy trình / WTS của PO chưa Đủ thành phẩm: chặn (gợi ý Tạm dừng); PO Đủ rồi: xoá được");
        {
            PinThrottle.ResetAll();
            var db = new AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false);
            var pid = await ChuoiHelp.BuildPartAsync(db, "P-Q13", new ChuoiHelp.Shape { Gc = 1, ExclAt = -1, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 1, Prep = false, TaroTaskCode = false });
            var ids = await ChuoiHelp.CreateKhAsync(db, pid, "P-Q13", new decimal[] { 10 }, "PO-Q13");
            int det = ids[0];
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", TenMay = "May 1", IsActive = true, TrangThai = "Hoạt động" });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 2, SoMay = "M9", TenMay = "May 9", IsActive = true, TrangThai = "Hoạt động" });
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 3, SoMay = "M8", TenMay = "May 8", IsActive = true, TrangThai = "Hoạt động" });
            db.Users.List.Single(u => u.UserId == 1).PIN = "1111";
            db.Fixup();
            var auth = new AuthService(db); var tb = new ThietBiService(db, auth);
            Ok(db.KhPlanRouteSnapshotMachining.List.Any(x => x.KhPlanDetailId == det && x.MachineRegistered == "M1"), "Dựng: PO-Q13 (10 chiếc) snapshot NC01 máy M1");
            // Part đang dùng M1 → chặn như cũ (RS-D9); ngưng Part để kiểm riêng luật PO mở
            var r0 = await tb.SoftDeleteAsync(1, 1, "1111");
            Ok(!r0.Success && r0.Error!.Contains("Part Master P-Q13"), "Part P-Q13 đang dùng M1: chặn như cũ (RS-D9)", r0.Error);
            db.PartMasters.List.Single(x => x.PartId == pid).IsObsolete = true;
            var r1 = await tb.SoftDeleteAsync(1, 1, "1111");
            Ok(!r1.Success && r1.Error!.Contains("PO chưa xong") && r1.Error.Contains("PO-Q13") && r1.Error.Contains("Tạm dừng") && db.ThietBis.List[0].IsActive,
               "Part đã ngưng nhưng PO-Q13 chưa có phôi (chưa xong) còn trỏ M1 trong snapshot: KHÔNG xoá, nêu PO + gợi ý Tạm dừng", r1.Error);
            // máy chỉ có trong WTS của PO mở (không có trong snapshot)
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 901, KhPlanDetailId = det, ProcessGroup = "GC", NC = "NC01", MachineUsed = "M9", WorkerId = 2, QtyDone = 1, QtyOk = 1, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            db.Fixup();
            var r2 = await tb.SoftDeleteAsync(2, 1, "1111");
            Ok(!r2.Success && r2.Error!.Contains("PO-Q13") && db.ThietBis.List[1].IsActive, "M9 chỉ có ở dòng WTS của PO-Q13 (chưa xong): KHÔNG xoá", r2.Error);
            db.WtsProductionLogs.List.Single(w => w.WtsLogId == 901).IsVoided = true; db.Fixup();
            var r2b = await tb.SoftDeleteAsync(2, 1, "1111");
            Ok(r2b.Success, "Dòng WTS M9 đã huỷ: xoá M9 được", r2b.Error);
            var r3 = await tb.SoftDeleteAsync(3, 1, "1111");
            Ok(r3.Success, "M8 không ở PO nào: xoá được như cũ", r3.Error);
            // đi hết chuỗi cho PO Đủ thành phẩm
            var kho = new KhoVatLieuService(db, new UserService(null)); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var k = await kho.SaveKhoInputAsync(det, DateTime.Now, 10, "OVL", "A1", null, null, null, "nhan phoi", 1);
            var i1 = await svc.IssueByCellAsync(det, "KHO", "GC", 10, 2, "MOI", false, "MOI", null, "NC01");
            var rc1 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
            var (ncs, codeOf) = ChuoiHelp.CodesFromSnapshot(db, det);
            await VuHelp.Wts(db, "GC", "NC01", null, 10, 0, 2, 1);
            var i2 = await svc.IssueByCellAsync(det, "GC", "KCS", 10, 2, "MOI", false, "MOI");
            var rc2 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 3);
            await VuHelp.Wts(db, "KCS", null, codeOf["KCS"].codes[0], 10, 0, 3, 1);
            var i3 = await svc.IssueByCellAsync(det, "KCS", "PKG", 10, 3, "MOI", false, "MOI");
            var rc3 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 4);
            await VuHelp.Wts(db, "PKG", null, codeOf["PKG"].codes[0], 10, 0, 4, 1);
            var i4 = await svc.IssueByCellAsync(det, "PKG", "KHO", 10, 4, "MOI", false, "MOI");
            var rc4 = await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 5);
            var row = (await led.GetLedgerForDetailAsync(det))!;
            var st = HandoverLedgerService.WhereIs(row).Status;
            Ok(k.Success && i1.Ok && rc1.Ok && i2.Ok && rc2.Ok && i3.Ok && rc3.Ok && i4.Ok && rc4.Ok && st == "DU",
               "Đi hết chuỗi: PO-Q13 Đủ thành phẩm (sổ Status = DU)", $"{k.Error}|{i1.Error}|{rc1.Error}|{i2.Error}|{rc2.Error}|{i3.Error}|{rc3.Error}|{i4.Error}|{rc4.Error}|st={st}");
            var r4 = await tb.SoftDeleteAsync(1, 1, "1111");
            Ok(r4.Success && !db.ThietBis.List[0].IsActive, "PO đã Đủ thành phẩm + Part đã ngưng: xoá M1 được", r4.Error);
            PinThrottle.ResetAll();
        }

        // [BM1-01 2026-11-16] Gói 24: sổ sự cố máy — đang dừng mà lưu lại không mở dòng sự cố thứ 2; về Hoạt động chốt MỌI dòng mở. Bản cdbd991: dòng mở trùng, dòng cũ mở mãi → SAI.
        Console.WriteLine("[Q14] BM1-01 — Thiết bị: đang dừng mà sửa tên / lý do không mở dòng sự cố mới; đổi loại dừng → chốt dòng cũ, mở dòng loại mới; về Hoạt động chốt mọi dòng mở (cả dữ liệu cũ)");
        {
            PinThrottle.ResetAll();
            var db = Seed(); var auth = new AuthService(db); var tb = new ThietBiService(db, auth);
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 21, SoMay = "C01", TenMay = "May C01", BoPhan = "GC", TrangThai = "Hoạt động", IsActive = true });
            db.Fixup();
            var t0 = DateTime.Now.AddHours(-3);
            ThietBi M(string tt, string ten = "May C01") => new() { ThietBiId = 21, SoMay = "C01", TenMay = ten, BoPhan = "GC", TrangThai = tt };
            List<ThietBiChangeLog> Open() => db.ThietBiChangeLogs.List.Where(l => l.ThietBiId == 21 && l.ThoiGianBatDau != null && l.ThoiGianKetThuc == null).ToList();
            var r1 = await tb.UpdateAsync(M("Báo hỏng"), "gay dao", t0, null, CatEdit, "1111");
            Ok(r1.Success && Open().Count == 1 && Open()[0].NewValue == "Báo hỏng" && Open()[0].ThoiGianBatDau == t0, "Hoạt động → Báo hỏng (giờ t0): 1 dòng sự cố mở", r1.Error + $" open={Open().Count}");
            var tDoi = DateTime.Now;
            var r2 = await tb.UpdateAsync(M("Bảo trì"), "chuyen bao tri", null, null, CatEdit, "1111");
            var hong = db.ThietBiChangeLogs.List.Single(l => l.ThietBiId == 21 && l.NewValue == "Báo hỏng" && l.ThoiGianBatDau == t0);
            Ok(r2.Success && Open().Count == 1 && Open()[0].NewValue == "Bảo trì" && Open()[0].OldValue == "Báo hỏng" && Open()[0].ThoiGianBatDau >= tDoi.AddSeconds(-2) && Open()[0].Reason == "chuyen bao tri",
               "Báo hỏng → Bảo trì: vẫn chỉ 1 dòng mở = dòng 'Trạng thái' Bảo trì mở từ lúc đổi (trước: 2 dòng mở)", r2.Error + $" open={Open().Count}");
            Ok(hong.ThoiGianKetThuc != null && hong.ThoiGianKetThuc >= tDoi.AddSeconds(-2) && hong.NewValue == "Báo hỏng" && hong.SoPhutDung >= 170,
               "Dòng Báo hỏng cũ được CHỐT lúc đổi loại, giữ nguyên loại 'Báo hỏng' + phút dừng (rà độc lập 16/11: không ghi đè loại hồi tố → Hiệu suất máy ngày cũ không đổi)", $"ket={hong.ThoiGianKetThuc} phut={hong.SoPhutDung}");
            var r3 = await tb.UpdateAsync(M("Bảo trì", ten: "May C01 moi"), "doi ten", null, null, CatEdit, "1111");
            Ok(r3.Success && Open().Count == 1 && db.ThietBis.List.Single(x => x.ThietBiId == 21).TenMay == "May C01 moi", "Đang Bảo trì, chỉ đổi tên: lưu, vẫn 1 dòng mở (trước: thêm 'Nhật ký dừng máy' mở)", r3.Error + $" open={Open().Count}");
            var r3b = await tb.UpdateAsync(M("Bảo trì", ten: "May C01 moi"), "khong doi gi", null, null, CatEdit, "1111");
            Ok(r3b.Success && Open().Count == 1, "Đang Bảo trì, không đổi gì (chỉ nhập PIN): vẫn 1 dòng mở", r3b.Error + $" open={Open().Count}");
            // dữ liệu cũ: thêm 1 dòng mở nữa (như bản trước để lại) → về Hoạt động phải chốt cả 2
            db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = 9001, ThietBiId = 21, FieldName = "Nhật ký dừng máy", OldValue = "Báo hỏng", NewValue = "Báo hỏng", Reason = "cu", ThoiGianBatDau = t0.AddMinutes(10), ChangedAt = t0.AddMinutes(10), ChangedBy = CatEdit });
            db.Fixup();
            Ok(Open().Count == 2, "Dựng dữ liệu cũ: 2 dòng mở");
            var tEnd = DateTime.Now;   // dòng Bảo trì mở từ lúc đổi loại (vừa rồi) → giờ xong phải ≥ lúc đó
            var r4 = await tb.UpdateAsync(M("Hoạt động", ten: "May C01 moi"), "xong", null, tEnd, CatEdit, "1111");
            var closed = db.ThietBiChangeLogs.List.Where(l => l.ThietBiId == 21 && l.ThoiGianBatDau != null && l.ThoiGianKetThuc != null).ToList();
            Ok(r4.Success && Open().Count == 0 && closed.Count(l => l.ThoiGianKetThuc == tEnd) >= 2 && closed.All(l => l.SoPhutDung >= 0),
               "Về Hoạt động: chốt MỌI dòng mở (2 dòng) lúc tEnd, số phút ≥ 0 (trước: chỉ dòng mới nhất, dòng cũ mở mãi)", r4.Error + $" open={Open().Count} closed={closed.Count}");
            Ok(db.ThietBis.List.Single(x => x.ThietBiId == 21).TrangThai == "Hoạt động", "Máy về Hoạt động");
            PinThrottle.ResetAll();
        }
        // [DOGA-LOG-ID][WTS-LOG-ID 2026-11-16] Gói 24 (BM1-04): log "Tạo mới" phải mang Id thật (trước: Id 0, mồ côi). Bản cdbd991: DoGaId = 0 → SAI.
        Console.WriteLine("[Q15] DOGA-LOG-ID / WTS-LOG-ID — log 'Tạo mới' đồ gá / công việc WTS mang đúng Id (trước 0, lịch sử không có dòng Tạo)");
        {
            PinThrottle.ResetAll();
            var db = Seed(); var auth = new AuthService(db); var dg = new DoGaService(db, auth);
            var c = await dg.CreateAsync(new DoGa { TenDoGa = "VJ-LOG" }, CatEdit, "1111"); db.Fixup();
            var g = db.DoGas.List.FirstOrDefault(x => x.TenDoGa == "VJ-LOG");
            var lg = db.DoGaChangeLogs.List.FirstOrDefault(l => l.FieldName == "Tạo mới đồ gá" && l.NewValue == "VJ-LOG");
            Ok(c.Success && g != null && g.DoGaId > 0 && lg != null && lg.DoGaId == g.DoGaId, "Đồ gá: log Tạo mới DoGaId = Id đồ gá (≠ 0)", $"{c.Error} id={g?.DoGaId} log={lg?.DoGaId}");
            Ok(!db.DoGaChangeLogs.List.Any(l => l.DoGaId == 0), "Không còn dòng log DoGaId = 0");
            var wts = new StandardWtsTaskService(db, auth);
            var cw = await wts.CreateAsync(new StandardWtsTask { TaskCode = "9L", TaskName = "Log 9", CategoryCode = "KCS", CategoryName = "KCS", DefaultUnit = "Chi tiết", IsProductiveTask = true }, Admin, "1111"); db.Fixup();
            var t9 = db.StandardWtsTasks.List.FirstOrDefault(x => x.TaskCode == "9L");
            var lw = db.StandardWtsTaskChangeLogs.List.FirstOrDefault(l => l.FieldName == "Tạo mới công việc" && (l.NewValue ?? "").Contains("[9L]"));
            Ok(cw.Success && t9 != null && lw != null && lw.TaskId == t9.TaskId && t9.TaskId > 0, "WTS tiêu chuẩn: log Tạo mới TaskId = Id công việc (≠ 0)", $"{cw.Error} id={t9?.TaskId} log={lw?.TaskId}");
            PinThrottle.ResetAll();
        }
        // [WTS-NHOM-TEN 2026-11-16] Gói 24 (BP2-02): 1 mã nhóm = 1 tên. Bản cdbd991: tạo CV cùng mã nhóm tên khác → 2 dòng nhóm → SAI.
        Console.WriteLine("[Q16] WTS-NHOM-TEN — thêm công việc với mã nhóm đã có mà tên khác: chặn; sửa Tên nhóm → đổi cho cả nhóm (+ log); danh sách nhóm 1 mã 1 dòng");
        {
            PinThrottle.ResetAll();
            var db = Seed(); var auth = new AuthService(db); var wts = new StandardWtsTaskService(db, auth);
            StandardWtsTask M(string code, string cat, string catName) => new() { TaskCode = code, TaskName = "CV " + code, CategoryCode = cat, CategoryName = catName, DefaultUnit = "Chi tiết", IsProductiveTask = true };
            var c1 = await wts.CreateAsync(M("T1", "TARO", "Taro"), Admin, "1111");
            var c2 = await wts.CreateAsync(M("T2", "TARO", "Taro 2"), Admin, "1111");
            Ok(c1.Success && !c2.Success && (c2.Error ?? "").Contains("đã có tên 'Taro'") && !db.StandardWtsTasks.List.Any(x => x.TaskCode == "T2"),
               "Thêm T2 mã nhóm TARO tên 'Taro 2' (khác tên đang có 'Taro'): chặn, nêu tên đang có", c2.Error);
            var c3 = await wts.CreateAsync(M("T2", "TARO", " Taro "), Admin, "1111");
            Ok(c3.Success, "Thêm T2 với đúng tên 'Taro' (có dấu cách): được", c3.Error);
            var cats = await wts.GetDistinctCategoriesAsync();
            Ok(cats.Count(c => c.Code == "TARO") == 1, "Danh sách nhóm: TARO 1 dòng", string.Join(" | ", cats.Select(c => c.Code + "=" + c.Name)));
            // đổi tên nhóm qua 1 công việc → cả nhóm đổi
            var t1 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "T1"); var t2 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "T2");
            var u = await wts.UpdateAsync(new StandardWtsTask { TaskId = t1.TaskId, TaskCode = "T1", TaskName = "CV T1", CategoryCode = "TARO", CategoryName = "Ta rô", DefaultUnit = "Chi tiết", IsProductiveTask = true }, "doi ten nhom", CatEdit, "1111");
            Ok(u.Success && t1.CategoryName == "Ta rô" && t2.CategoryName == "Ta rô", "Sửa T1 đổi Tên nhóm 'Taro' → 'Ta rô': T2 cùng nhóm đổi theo", u.Error + $" t2={t2.CategoryName}");
            Ok(db.StandardWtsTaskChangeLogs.List.Any(l => l.TaskId == t2.TaskId && l.FieldName == "Tên nhóm" && l.NewValue == "Ta rô" && (l.Reason ?? "").Contains("cả nhóm")),
               "T2 có dòng lịch sử 'Tên nhóm' do đổi cho cả nhóm");
            // dữ liệu cũ lệch tên: distinct vẫn 1 dòng
            db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 777, TaskCode = "T7", TaskName = "cu", CategoryCode = "TARO", CategoryName = "Taro cu", IsActive = true }); db.Fixup();
            cats = await wts.GetDistinctCategoriesAsync();
            Ok(cats.Count(c => c.Code == "TARO") == 1 && cats.Single(c => c.Code == "TARO").Name == "Ta rô", "Dữ liệu cũ 1 mã 2 tên: danh sách nhóm vẫn 1 dòng, lấy tên nhiều nhất", string.Join(" | ", cats.Select(c => c.Code + "=" + c.Name)));
            // chuyển T2 sang nhóm KCS (đã có tên 'KCS' ở Q8 seed? không — nhóm mới) tên bất kỳ: được
            var u2 = await wts.UpdateAsync(new StandardWtsTask { TaskId = t2.TaskId, TaskCode = "T2", TaskName = "CV T2", CategoryCode = "BAVIA", CategoryName = "Bavia", DefaultUnit = "Chi tiết", IsProductiveTask = true }, "doi nhom", CatEdit, "1111");
            var u3 = await wts.UpdateAsync(new StandardWtsTask { TaskId = t2.TaskId, TaskCode = "T2", TaskName = "CV T2", CategoryCode = "TARO", CategoryName = "Taro X", DefaultUnit = "Chi tiết", IsProductiveTask = true }, "ve taro", CatEdit, "1111");
            Ok(u2.Success && t2.CategoryCode == "BAVIA" && !u3.Success && (u3.Error ?? "").Contains("đã có tên"), "Chuyển sang nhóm mới BAVIA: được; chuyển về TARO với tên khác tên đang có: chặn", u2.Error + " | " + u3.Error);
            PinThrottle.ResetAll();
        }

        Console.WriteLine("[Q17] QF-P-03 — 12 chỗ ghi DB của trang Part Master dời vào service: máy chủ kiểm quyền từ DB (người bị rút quyền giữa phiên bị chặn), ghi y như trang cũ");
        {
            // [QF-P-03 2026-11-23] Nguồn: CLAUDE quét Fable 14/11 (P-03); HÙNG 23/11 13:20 OK. Đáp án viết tay theo code trang cũ (Detail_Partmaster bản 040bc56).
            var db = Seed();
            var gB = new UserGroup { GroupId = 21, GroupCode = "PMB", IsActive = true, Permissions = Perm((SystemPermissions.PartGc, true, true)) };
            var gC = new UserGroup { GroupId = 22, GroupCode = "PMC", IsActive = true, Permissions = Perm((SystemPermissions.PartHtsp, true, true)) };
            var gAv = new UserGroup { GroupId = 23, GroupCode = "PMAV", IsActive = true, Permissions = Perm((SystemPermissions.PartTaoMoi, true, false), (SystemPermissions.PartGc, true, false)) };
            var gA = new UserGroup { GroupId = 24, GroupCode = "PMA", IsActive = true, Permissions = Perm((SystemPermissions.PartTaoMoi, true, true)) };
            db.UserGroups.List.AddRange(new[] { gB, gC, gAv, gA });
            const int UB = 21, UC = 22, UAv = 23, UA = 24;
            foreach (var (uid, g) in new[] { (UB, gB), (UC, gC), (UAv, gAv), (UA, gA) })
                db.Users.List.Add(new User { UserId = uid, Username = "u" + uid, FullName = "U" + uid, IsActive = true, Group = g, GroupId = g.GroupId, PIN = "1111" });
            db.PartMasters.List.Add(new PartMaster { PartId = 1, PartNo = "P" });
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 1, PartId = 1, NC = "NC1", MachineRegistered = "M1", IsActive = true });
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 2, PartId = 1, NC = "NC2", MachineRegistered = "M2", IsActive = true });
            db.PartMachiningTimings.List.Add(new PartMachiningTiming { TimingId = 7, StepId = 1, SoMay = "M5", SetupTime = 1, MachiningTime = 2, IsActive = true });
            db.Fixup();
            var ms = new PartMachiningService(db); var pms = new PartMasterService(db); var cs = new PartStepStdTimeConfirmService(db);
            var at = new DateTime(2026, 11, 23, 9, 30, 15);
            int Logs() => db.PartProcessStepChangeLogs.List.Count;
            async Task<string?> Err(Func<Task> f) { try { await f(); return null; } catch (Exception ex) { db.ChangeTracker.Clear(); return ex.GetType().Name + ": " + ex.Message; } }
            var t = new PartMachiningService.TimingGhi(" M6 ", 2, "Ga1", "Dao1", 1.5m, 3m, null, 0.5m, null);
            // (a) thêm máy đồng dạng
            int l0 = Logs(), n0 = db.PartMachiningTimings.List.Count;
            var e1 = await Err(() => ms.AddTimingAsync(1, 1, t, GcOnly, "ly do", at));
            var e2 = await Err(() => ms.AddTimingAsync(1, 1, t, UAv, "ly do", at));
            Ok(e1 != null && e1.StartsWith("UnauthorizedAccess") && e2 != null && e2.StartsWith("UnauthorizedAccess") && Logs() == l0 && db.PartMachiningTimings.List.Count == n0,
               "Q17a thêm máy đồng dạng: không quyền / chỉ XEM vùng B → chặn ở máy chủ, không ghi gì", $"{e1} | {e2}");
            long id = 0; var e3 = await Err(async () => id = await ms.AddTimingAsync(1, 1, t, UB, "ly do", at));
            var ent = db.PartMachiningTimings.List.FirstOrDefault(x => x.TimingId == id);
            var lg = db.PartProcessStepChangeLogs.List.LastOrDefault();
            Ok(e3 == null && ent != null && ent.SoMay == "M6" && ent.DisplayOrder == 2 && ent.FixtureType == "Ga1" && ent.ToolType == "Dao1" && ent.SetupTime == 1.5m && ent.MachiningTime == 3m
               && ent.PreparationTime == 0.5m && ent.IsActive && ent.CreatedBy == UB && ent.CreatedAt == at && Logs() == l0 + 1
               && lg!.StepTable == "Machining" && lg.StepId == 1 && lg.TimingId == id && lg.PartId == 1 && lg.FieldName == "Created" && lg.OldValue == null
               && lg.NewValue == "Thêm máy M6 | GL=1.5 GC=3 KT= CB=0.5 CT=" && lg.ChangedBy == UB && lg.ChangedAt == at && lg.Reason == "ly do",
               "Q17b có quyền sửa vùng B → dòng máy + 1 dòng lịch sử 'Created' đúng y chữ trang cũ (Trim máy, giờ trang truyền)", $"{e3} · {lg?.NewValue}");
            // (b) thêm: lỗi lần lưu lịch sử → không để lại dòng máy (1 giao dịch)
            int nS = 0; Microsoft.EntityFrameworkCore.DbContext.OnSave = () => ++nS == 2 ? new InvalidOperationException("gia loi luu lich su") : null;
            int n1 = db.PartMachiningTimings.List.Count, l1 = Logs();
            var e4 = await Err(() => ms.AddTimingAsync(1, 1, t with { SoMay = "M9" }, UB, "ly do", at));
            Microsoft.EntityFrameworkCore.DbContext.OnSave = null;
            Ok(e4 != null && db.PartMachiningTimings.List.Count == n1 && !db.PartMachiningTimings.List.Any(x => x.SoMay == "M9") && Logs() == l1,
               "Q17c thêm máy: lưu lịch sử lỗi → quay lại cả dòng máy (trước: 2 lần lưu rời, còn dòng máy không lịch sử)", e4);
            // (c) sửa
            var m5 = db.PartMachiningTimings.List.Single(x => x.TimingId == 7);
            gB.Permissions = Perm((SystemPermissions.PartGc, true, false));   // Admin rút quyền Sửa giữa phiên (trang vẫn mở với cờ cũ)
            var e5 = await Err(() => ms.UpdateTimingAsync(7, 1, 1, new("M5", 1, null, null, 9m, 2m, null, null, null), UB, "r", at));
            Ok(e5 != null && e5.Contains("không có quyền sửa vùng") && m5.SetupTime == 1, "Q17d bị rút quyền Sửa vùng B giữa phiên → sửa máy đồng dạng bị chặn ở máy chủ (P-03)", e5);
            gB.Permissions = Perm((SystemPermissions.PartGc, true, true));
            int l2 = Logs();
            bool okU = await ms.UpdateTimingAsync(7, 1, 1, new("M5 ", 3, "G", null, 9m, 2m, 4m, null, null), UB, "r", at);
            var newLogs = db.PartProcessStepChangeLogs.List.Skip(l2).ToList();
            Ok(okU && m5.SetupTime == 9m && m5.InspectionTime == 4m && m5.FixtureType == "G" && m5.DisplayOrder == 3 && m5.UpdatedBy == UB && m5.UpdatedAt == at
               && newLogs.Select(x => x.FieldName).SequenceEqual(new[] { "SetupTime", "InspectionTime", "FixtureType" }) && newLogs.All(x => x.TimingId == 7 && x.StepId == 1 && x.Reason == "r")
               && newLogs[0].OldValue == "1" && newLogs[0].NewValue == "9" && newLogs[1].OldValue == null && newLogs[2].NewValue == "G",
               "Q17e sửa: lịch sử đúng từng ô đổi (thứ tự như trang cũ, 'M5 ' Trim = không đổi máy), giữ giờ trang truyền", string.Join(", ", newLogs.Select(x => $"{x.FieldName}:{x.OldValue}→{x.NewValue}")));
            var e6 = await Err(() => ms.UpdateTimingAsync(7, 2, 1, new("M5", 1, null, null, 1m, null, null, null, null), UB, "r", at));
            var e7 = await Err(() => ms.UpdateTimingAsync(7, 1, 1, new("M5", 1, null, null, -1m, null, null, null, null), UB, "r", at));
            bool khong = await ms.UpdateTimingAsync(999, 1, 1, t, UB, "r", at);
            Ok(e6 != null && e6.Contains("không thuộc NC") && e7 != null && e7.Contains("Gia công:") && !khong && m5.SetupTime == 9m,
               "Q17f sửa: dòng không thuộc NC / thời gian âm → chặn; không thấy dòng → false (trang bỏ qua như cũ)", $"{e6} | {e7}");
            // (d) xoá mềm
            int l3 = Logs();
            var e8 = await Err(() => ms.SoftDeleteTimingAsync(7, 1, 1, HtspView, "r", at));
            bool okD = await ms.SoftDeleteTimingAsync(7, 1, 1, UB, "xoa", at);
            var dl = db.PartProcessStepChangeLogs.List.Last();
            Ok(e8 != null && okD && !m5.IsActive && m5.UpdatedBy == UB && Logs() == l3 + 1 && dl.FieldName == "Deleted" && dl.OldValue == "Xóa máy M5 | GL=9 GC=2 KT=4 CB= CT=" && dl.NewValue == null && dl.Reason == "xoa",
               "Q17g xoá mềm máy đồng dạng: không quyền → chặn; có quyền → IsActive = 0 + lịch sử 'Deleted' y chữ cũ", $"{e8} · {dl.OldValue}");
            // (e) lịch sử xác nhận TG chuẩn (máy) + T.Chuẩn Taro
            int l4 = Logs();
            var e9 = await Err(() => ms.LogTimingConfirmAsync(1, null, 1, "SetupTime", false, true, UAv, "r", at));
            await ms.LogTimingConfirmAsync(1, id, 1, "SetupTime", false, true, UB, "r", at);
            var cl = db.PartProcessStepChangeLogs.List.Last();
            var e10 = await Err(() => cs.LogConfirmAsync("Taro", 5, 1, true, false, UB, "r", at));
            await cs.LogConfirmAsync("Taro", 5, 1, false, true, UC, "Lưu quy trình", at);
            var tl = db.PartProcessStepChangeLogs.List.Last();
            Ok(e9 != null && e10 != null && e10.Contains("Hoàn thiện SP") && Logs() == l4 + 2
               && cl.FieldName == "TimingConfirmed" && cl.TimingId == id && cl.OldValue == "Unconfirmed [SetupTime]" && cl.NewValue == $"Confirmed [SetupTime] by userId={UB} at 2026-11-23 09:30:15"
               && tl.StepTable == "Taro" && tl.StepId == 5 && tl.OldValue == "Unconfirmed [StandardTime]" && tl.NewValue == $"Confirmed [StandardTime] by userId={UC} at 2026-11-23 09:30:15" && tl.Reason == "Lưu quy trình",
               "Q17h lịch sử xác nhận: ô máy (vùng B) / T.Chuẩn Taro (vùng C) kiểm đúng vùng ở máy chủ, chữ y trang cũ", $"{e9} | {e10} · {cl.NewValue} · {tl.NewValue}");
            // (f) máy loại trừ toàn hệ thống
            db.MachineExcludeSettings.List.Add(new MachineExcludeSetting { Id = 1, SoMay = "H04" }); db.MachineExcludeSettings.List.Add(new MachineExcludeSetting { Id = 2, SoMay = "H05" }); db.Fixup();
            var e11 = await Err(() => ms.SaveExcludeMachinesAsync(new HashSet<string> { "H04" }, UC));
            int nx = db.MachineExcludeSettings.List.Count;
            await ms.SaveExcludeMachinesAsync(new HashSet<string> { "H04", "h05x", "H09" }, UB);
            var ex = db.MachineExcludeSettings.List.Select(x => x.SoMay).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Ok(e11 != null && nx == 2 && ex.SequenceEqual(new[] { "H04", "H09", "h05x" }) && db.MachineExcludeSettings.List.Where(x => x.SoMay != "H04").All(x => x.AddedByUserId == UB || x.SoMay == "H05"),
               "Q17i máy loại trừ: không quyền vùng B → chặn, giữ nguyên; có quyền → bỏ máy không tích, thêm máy mới (người thêm ghi lại)", $"{e11} · {string.Join(",", ex)}");
            // (g) ghi chú mạ (vùng A)
            var pm = db.PartMasters.List.Single();
            var e12 = await Err(() => pms.UpdateGhiChuMaAsync(1, "Ma kem", UB));
            var g1 = await pms.UpdateGhiChuMaAsync(1, "  Ma kem  ", UA);
            var g2 = await pms.UpdateGhiChuMaAsync(1, "   ", UA);
            var g3 = await pms.UpdateGhiChuMaAsync(99, "x", UA);
            Ok(e12 != null && g1 == (true, "Ma kem") && g2 == (true, (string?)null) && pm.GhiChuMa == null && g3.Found == false,
               "Q17j ghi chú mạ: vùng B không sửa được; vùng A → Trim, trống = null; không thấy Part → Found = false", e12);
            // (h) cờ Xác nhận OK
            pm.IsPlanConfirmed = false; pm.IsMachiningConfirmed = true;
            // rà độc lập 23/11 (Thấp 12): vùng không đủ quyền → BỎ QUA vùng đó (BiChan), vùng còn lại vẫn ghi — trước ném lỗi cả lệnh
            PartMaster? f1 = null; string? ef1 = null; List<string> c1 = new();   // gói try: đột biến chặn nhầm A = OK phải ra SAI, không làm vỡ cả kịch bản
            try { (f1, c1) = await pms.UpdateConfirmFlagsAsync(1, UAv, true, null, null, null, null); } catch (Exception x) { ef1 = x.Message; }
            bool okA1 = f1 != null && c1.Count == 0 && pm.IsPlanConfirmed;
            var (_, c13) = await pms.UpdateConfirmFlagsAsync(1, UAv, false, null, null, null, null);
            bool okA2 = pm.IsPlanConfirmed && c13.SequenceEqual(new[] { "Kế hoạch nhập (Thông tin chung)" });
            pm.IsPlanConfirmed = false;
            var (f14, c14) = await pms.UpdateConfirmFlagsAsync(1, UAv, true, false, null, null, null);
            Ok(okA1 && okA2 && f14 != null && pm.IsPlanConfirmed && pm.IsMachiningConfirmed && c14.SequenceEqual(new[] { "Quy trình gia công CNC" }),
               "Q17k cờ OK: chỉ XEM mục A → đặt A = OK được (luật trang cũ, câu B5 giữ), A = NG bị bỏ qua (báo vùng); vùng B chỉ xem → bỏ qua B, A vẫn ghi", $"{ef1} | {string.Join(",", c13)} | {string.Join(",", c14)}");
            var (f2, c2) = await pms.UpdateConfirmFlagsAsync(1, UB, null, false, null, null, null);
            var e15 = await Err(() => pms.UpdateConfirmFlagsAsync(1, Locked, true, true, true, true, true));
            var (f3, c3) = await pms.UpdateConfirmFlagsAsync(1, Admin, false, true, true, true, true);
            var (f4, c4) = await pms.UpdateConfirmFlagsAsync(99, Admin, false, null, null, null, null);
            Ok(f2 != null && c2.Count == 0 && e15 != null && f3 != null && c3.Count == 0 && !pm.IsPlanConfirmed && pm.IsMachiningConfirmed && pm.IsHtspConfirmed && pm.IsKcsConfirmed && pm.IsPkgConfirmed && f4 == null,
               "Q17l cờ OK: sửa vùng B đặt NG vùng B được; tài khoản khoá → chặn; Admin đặt cả 5 vùng; không thấy Part → null", e15);
            // (h2) nhánh lỗi [RS-E2]: người sửa B + C bị rút quyền C giữa phiên → B vẫn về NG, C bỏ qua (trước: ném cả lệnh, B giữ OK)
            pm.IsMachiningConfirmed = true; pm.IsHtspConfirmed = true;
            var (f5, c5) = await pms.UpdateConfirmFlagsAsync(1, UB, null, false, false, null, null);
            Ok(f5 != null && !pm.IsMachiningConfirmed && pm.IsHtspConfirmed && c5.SequenceEqual(new[] { "Hoàn thiện SP (Taro/Bavia/Rửa)" }),
               "Q17n về NG khi lưu lỗi: vùng còn quyền (B) về NG, vùng mất quyền (C) bỏ qua + báo tên vùng", string.Join(",", c5));
            var e16 = await Err(() => ms.SaveExcludeMachinesAsync(new HashSet<string>(), NoGroup));
            var e17 = await Err(() => pms.UpdateGhiChuMaAsync(1, "x", Locked));
            Ok(e16 != null && e17 != null && db.MachineExcludeSettings.List.Count == 3, "Q17m không nhóm / tài khoản khoá → chặn mọi ghi", $"{e16} | {e17}");
        }

        Console.WriteLine("[Q8] WTS-ORDER-SAVE — WTS tiêu chuẩn: chỉ đổi Thứ tự hiển thị → lưu + lịch sử");
        {
            var db = Seed(); var auth = new AuthService(db); var wts = new StandardWtsTaskService(db, auth);
            StandardWtsTask M(int id, int order) => new() { TaskId = id, TaskCode = "9K", TaskName = "KCS 9", CategoryCode = "KCS", CategoryName = "KCS", DefaultUnit = "Chi tiết", DisplayOrder = order, IsProductiveTask = true };
            var c = await wts.CreateAsync(M(0, 3), Admin, "1111");
            var t9 = db.StandardWtsTasks.List.Single(x => x.TaskCode == "9K");
            int logs0 = db.StandardWtsTaskChangeLogs.List.Count(l => l.TaskId == t9.TaskId);
            var u = await wts.UpdateAsync(M(t9.TaskId, 3), "khong doi", CatEdit, "1111");
            Ok(c.Success && u.Success && t9.DisplayOrder == 3 && db.StandardWtsTaskChangeLogs.List.Count(l => l.TaskId == t9.TaskId) == logs0,
               "Lưu mà không đổi gì: thành công, không thêm lịch sử", c.Error + u.Error);
            u = await wts.UpdateAsync(M(t9.TaskId, 7), "xep lai", CatEdit, "1111");
            var lg = db.StandardWtsTaskChangeLogs.List.LastOrDefault(l => l.TaskId == t9.TaskId);
            Ok(u.Success && t9.DisplayOrder == 7, "Chỉ đổi Thứ tự hiển thị 3 → 7: lưu (DisplayOrder = 7)", $"{u.Error} · DisplayOrder={t9.DisplayOrder}");
            Ok(lg != null && lg.FieldName == "Thứ tự hiển thị" && lg.OldValue == "3" && lg.NewValue == "7" && lg.Reason == "xep lai" && lg.ChangedBy == CatEdit,
               "Có dòng lịch sử 'Thứ tự hiển thị' 3 → 7, lý do + người sửa", lg == null ? "không có" : $"{lg.FieldName} {lg.OldValue}→{lg.NewValue}");
            Ok(t9.TaskName == "KCS 9" && t9.TaskCode == "9K" && t9.IsActive, "Các ô khác giữ nguyên");
            u = await wts.UpdateAsync(M(t9.TaskId, 1), "xep", CatView, "1111");
            Ok(!u.Success && t9.DisplayOrder == 7, "Người chỉ có quyền Xem: chặn, thứ tự giữ 7", u.Error);
        }


        // ═══ Gói 23 (16/11/2026) — quyền máy chủ P-01/P-02/P-06/P-07 + mã quyền Hiệu suất máy. Nguồn: CLAUDE quét Fable 15/11 (A3C); Hùng 16/11 05:15 làm theo đề xuất ═══
        Console.WriteLine("[Q9] KH-PERM-SVC — (P-01) KhPlanService.CreateAsync: máy chủ kiểm Sửa KE_HOACH (trước: hàm public không kiểm, chỉ trang kiểm lúc mở)");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            KhPlan P(string po) { var p = new KhPlan { PlanDate = new DateTime(2026, 11, 16), CustomerId = 1 }; p.Details.Add(new KhPlanDetail { STD = DateTime.Today, PartNo = "P", PurchaseOrder = po, Quantity = 5 }); return p; }
            string? e1 = null; try { await kh.CreateAsync(P("PO-Q9A"), HtspView); } catch (Exception ex) { e1 = ex.Message; }
            Ok(e1 != null && e1.Contains("không có quyền Sửa mục Kế hoạch"), "u3 chỉ Xem Giao nhận: tạo KH bị từ chối (NoEdit Kế hoạch)", e1);
            Ok(!db.KhPlanDetails.List.Any(d => d.PurchaseOrder == "PO-Q9A"), "  không ghi dòng PO nào");
            string? e2 = null; try { await kh.CreateAsync(P("PO-Q9B"), KhEdit); } catch (Exception ex) { e2 = ex.Message; }
            Ok(e2 == null && db.KhPlanDetails.List.Any(d => d.PurchaseOrder == "PO-Q9B"), "u6 Sửa Kế hoạch: tạo được", e2);
            string? e3 = null; try { await kh.CreateAsync(P("PO-Q9C"), Admin); } catch (Exception ex) { e3 = ex.Message; }
            Ok(e3 == null, "Admin: tạo được", e3);
            string? e4 = null; try { await kh.CreateAsync(P("PO-Q9D"), Locked); } catch (Exception ex) { e4 = ex.Message; }
            Ok(e4 != null, "u5 Admin nhưng tài khoản khoá: từ chối", e4);
        }

        Console.WriteLine("[Q10] KH-PERM-SVC — (P-02) RefreshRouteSnapshot / AutoRefreshRoutesForPart / CopyRouteSnapshot: kiểm Sửa KE_HOACH hoặc vùng Part Master B–E");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            db.KhoVatLieus.List.Clear();
            db.PartMasters.List.Add(new PartMaster { PartId = 60, PartNo = "P", IsActive = true });
            var pgG = new UserGroup { GroupId = 9, GroupCode = "PMGC", IsActive = true, Permissions = Perm((SystemPermissions.PartGc, true, true)) };
            db.UserGroups.List.Add(pgG);
            db.Users.List.Add(new User { UserId = 9, Username = "u9", FullName = "U9", IsActive = true, Group = pgG, GroupId = 9 });
            db.Fixup();
            int snap0 = db.KhPlanRouteSnapshotMachining.List.Count;
            string? e1 = null; try { await kh.RefreshRouteSnapshotAsync(1, 60, CatEdit, "cap nhat"); } catch (Exception ex) { e1 = ex.Message; }
            Ok(e1 != null && e1.Contains("không có quyền Sửa mục Kế hoạch") && db.KhPlanRouteSnapshotMachining.List.Count == snap0,
               "u8 Sửa danh mục (không KE_HOACH): \"Cập nhật từ Part Master\" bị từ chối, snapshot giữ", e1);
            string? e2 = null; try { await kh.CopyRouteSnapshotAsync(1, 60, HtspView); } catch (Exception ex) { e2 = ex.Message; }
            Ok(e2 != null && e2.Contains("không có quyền Sửa"), "u3 chỉ Xem: CopyRouteSnapshotAsync bị từ chối", e2);
            string? e3 = null; try { await kh.AutoRefreshRoutesForPartAsync(60, CatEdit, "luu part master"); } catch (Exception ex) { e3 = ex.Message; }
            Ok(e3 != null && e3.Contains("không có quyền Sửa"), "u8: tự đẩy quy trình sang PO bị từ chối", e3);
            string? e4 = null; List<KhPlanService.AutoRouteResult>? r4 = null; try { r4 = await kh.AutoRefreshRoutesForPartAsync(60, 9, "luu part master"); } catch (Exception ex) { e4 = ex.Message; }
            Ok(e4 == null && r4 != null, "u9 chỉ Sửa PART_GC (không KE_HOACH): tự đẩy quy trình ĐƯỢC chạy (đúng ý K6-NOTRUN)", e4 ?? $"{r4!.Count} PO");
            string? e5 = null; try { await kh.AutoRefreshRoutesForPartAsync(60, KhEdit, "luu part master"); } catch (Exception ex) { e5 = ex.Message; }
            Ok(e5 == null, "u6 Sửa Kế hoạch: chạy được", e5);
            string? e6 = null; try { await kh.RefreshRouteSnapshotAsync(1, 60, KhEdit, "cap nhat"); } catch (Exception ex) { e6 = ex.Message; }
            Ok(e6 == null || e6.Contains("snapshot") || e6.Contains("Part"), "u6 Sửa Kế hoạch: qua cửa quyền (chỉ chấp nhận lỗi nghiệp vụ về snapshot / Part, không nhận ngoại lệ khác — rà độc lập 16/11 L12)", e6);
        }

        Console.WriteLine("[Q11] PERM-ACTIVE — (P-06 / P-07) nhóm ngưng hoạt động = không quyền ở MỌI bộ kiểm (PartMasterPermissionHelper, GetCanDeleteAsync, SetObsoleteAsync)");
        {
            var db = Seed();
            var deadAdmin = new UserGroup { GroupId = 11, GroupCode = "ADMIN", IsActive = false };
            var deadKh = new UserGroup { GroupId = 12, GroupCode = "KHX", IsActive = false,
                Permissions = JsonSerializer.Serialize(new List<GroupPermissionSetting> { new() { SectionCode = SystemPermissions.PartTaoMoi, CanView = true, CanEdit = true, CanDelete = true } }) };
            db.UserGroups.List.AddRange(new[] { deadAdmin, deadKh });
            db.Users.List.Add(new User { UserId = 11, Username = "u11", FullName = "U11", IsActive = true, Group = deadAdmin, GroupId = 11 });
            db.Users.List.Add(new User { UserId = 12, Username = "u12", FullName = "U12", IsActive = true, Group = deadKh, GroupId = 12 });
            db.PartMasters.List.Add(new PartMaster { PartId = 70, PartNo = "P-OBS", IsActive = true });
            db.Fixup();
            Ok(!PartMasterPermissionHelper.CheckPermission(deadAdmin, SystemPermissions.PartGc, true), "PMH: nhóm ADMIN bị ngưng (sửa DB tay) → KHÔNG quyền (trước: ADMIN qua trước khi xét IsActive)");
            Ok(!await PermCheck.CanEditAsync(db, 11, SystemPermissions.PartGc), "  PermCheck cùng người: không quyền (2 bộ kiểm nay cùng luật)");
            Ok(PartMasterPermissionHelper.CheckPermission(db.UserGroups.List.First(g => g.GroupId == 1), SystemPermissions.PartGc, true), "  nhóm ADMIN đang hoạt động: vẫn toàn quyền");
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            Ok(!await us.GetCanDeleteAsync(12, SystemPermissions.PartTaoMoi), "GetCanDeleteAsync: nhóm có CanDelete nhưng ngưng → false");
            var pm = new PartMasterService(db);
            var r = await pm.SetObsoleteAsync(70, 12, obsolete: true);
            Ok(!r.Success && !db.PartMasters.List.First(x => x.PartId == 70).IsObsolete, "SetObsoleteAsync: nhóm ngưng → từ chối, Part không bị Ngưng", r.Error);
            deadKh.IsActive = true; db.Fixup();
            var r2 = await pm.SetObsoleteAsync(70, 12, obsolete: true);
            Ok(r2.Success && db.PartMasters.List.First(x => x.PartId == 70).IsObsolete, "  bật lại nhóm: Ngưng Part được (CanDelete)", r2.Error);
        }

        Console.WriteLine("[Q12] HSM-QUYEN — Hiệu suất máy có mục quyền HIEU_SUAT_MAY: service trả rỗng khi người xem không có ô Xem; ADMIN / có Xem thì có dữ liệu");
        {
            var db = Seed();
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 1, SoMay = "M1", TenMay = "May 1", IsActive = true, TrangThai = "Hoạt động" });
            var hsmG = new UserGroup { GroupId = 13, GroupCode = "HSM", IsActive = true, Permissions = Perm((SystemPermissions.HieuSuatMay, true, false)) };
            db.UserGroups.List.Add(hsmG);
            db.Users.List.Add(new User { UserId = 13, Username = "u13", FullName = "U13", IsActive = true, Group = hsmG, GroupId = 13 });
            db.Fixup();
            var eff = new MachineEfficiencyService(db);
            // [G31-DON-BO-KIEM 2026-11-23] đổi sang hàm 24h (GetAsync theo ca đã bỏ) — cùng luật quyền
            var d = new DateOnly(2026, 11, 16);
            var rNo = await eff.GetDay24Async(d, GcOnly);
            Ok(rNo.Rows.Count == 0, "u2 (không có HIEU_SUAT_MAY): kết quả rỗng (không lộ WTS máy)", $"{rNo.Rows.Count} máy");
            var r0 = await eff.GetDay24Async(d, 0);
            Ok(r0.Rows.Count == 0, "không truyền người xem (viewerUserId = 0): rỗng");
            var rAd = await eff.GetDay24Async(d, Admin);
            Ok(rAd.Rows.Count == 1, "Admin: thấy 1 máy", $"{rAd.Rows.Count}");
            var rV = await eff.GetDay24Async(d, 13);
            var pNo = await eff.GetPeriod24Async(d, d.AddDays(6), GcOnly); var pV = await eff.GetPeriod24Async(d, d.AddDays(6), 13);
            Ok(rV.Rows.Count == 1 && pNo.Rows.Count == 0 && pV.Rows.Count == 1, "u13 nhóm có ô Xem HIEU_SUAT_MAY: thấy 1 máy (ngày + kỳ); u2 kỳ: rỗng", $"{rV.Rows.Count} {pNo.Rows.Count} {pV.Rows.Count}");
            Ok(SystemPermissions.Sections.Any(s => s.Code == SystemPermissions.HieuSuatMay), "Mục HIEU_SUAT_MAY có trong danh sách ma trận (Cài đặt tự hiện ô)");
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
