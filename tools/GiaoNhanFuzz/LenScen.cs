// [LEN-SCEN 2026-11-16] Kịch bản `len` — Gói 23 nhóm B3 quét Fable: máy chủ kiểm ĐỘ DÀI CHỮ theo cột DB ở từng service (LenCheck) + helper UiSafe.
//   Gọi HÀM THẬT của service trên DB giả (EfStub). Chữ dài quá cột phải bị từ chối bằng lời VIỆT (không phải SqlException "truncated"),
//   KHÔNG ghi gì; chữ vừa đủ (= cột) đi tiếp bình thường. Code cũ 84d9b9e (Gói 22) chạy kịch bản này phải SAI (không có LenCheck → ghi được chữ dài).
//   Nguồn: CLAUDE quét Fable 15/11 (B-W1 BW1-01, B-W2 BW2-03, B-K BK-02, B-M2 BM2-01/03/05, B-P1 BP1-02/03, B-P2 BP2-01, B-M1 BM1-05/06/15);
//   Hùng 16/11 05:15 "không ảnh hưởng khách thì làm theo ý bạn".
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class LenScen
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static string S(int n, char ch = 'x') => new string(ch, n);
    static bool Viet(string? s) => s != null && s.Contains("dài quá") && !s.Contains("truncated");

    /// <summary>u1 ADMIN (PIN 1111) + khách KH1 + PO1 (id 1, SL 40, phôi 40) quy trình NC1 → K1 → P1.</summary>
    internal static AppDbContext Seed()   // [G28-SCEN] g28 dùng lại (ThietBi / PIN 1111)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(adminG);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = adminG, GroupId = 1, PIN = "1111" });
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-LEN", Customer = cust, CustomerId = 1, ApplyWorkflow = true, PlanDate = new DateTime(2026, 11, 16) };
        db.KhPlans.List.Add(plan);
        var d1 = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 40, LineNo = 1, STD = new DateTime(2026, 12, 1) };
        db.KhPlanDetails.List.Add(d1); plan.Details.Add(d1);
        db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = 40, NgayNhanPhoi = DateTime.Now, SoPO = "PO1", PartNo = "P" });
        var m1 = new KhPlanRouteSnapshotMachining { SnapshotId = 11, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" };
        m1.TimingRows.Add(new KhPlanRouteSnapshotMachiningTiming { TimingSnapshotId = 11, MachiningSnapshotId = 11, KhPlanDetailId = 1 });
        db.KhPlanRouteSnapshotMachining.List.Add(m1);
        db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
        db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
        db.PartMasters.List.Add(new PartMaster { PartId = 1, PartNo = "P", IsActive = true });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[LN0] LenCheck / UiSafe — helper: Over/Max/FirstOver (Trim trước khi đếm); UiSafe.Text: lỗi nghiệp vụ Việt hiện nguyên, lỗi EF / SQL → câu chung");
        {
            // [LEN-THO 2026-11-20] ĐÁP ÁN ĐỔI Gói 28 (G23-GHI-NHO L8, Hùng 20/11 22:51): đếm nguyên văn — "  12345  " (9 ký tự) với trần 5 nay QUÁ (trước: hợp lệ sau Trim)
            Ok(LenCheck.Over(null, 5, "A") == null && LenCheck.Over("12345", 5, "A") == null && LenCheck.Over("  12345  ", 5, "A") is string _o9 && _o9.Contains("đang 9 ký tự"),
               "null / đúng 5 ký tự: hợp lệ; \"  12345  \" (9 ký tự nguyên văn) với trần 5: quá (đáp án đổi Gói 28 — trước đếm sau Trim)");
            var o = LenCheck.Over("123456", 5, "Ô thử");
            Ok(o != null && o.Contains("Ô thử dài quá 5 ký tự (đang 6 ký tự)"), "6 ký tự với trần 5: lời Việt nêu tên ô + trần + độ dài", o);
            Ok(LenCheck.FirstOver(("a", 1, "A"), ("bb", 1, "B"), ("ccc", 1, "C"))?.StartsWith("B dài quá") == true, "FirstOver: trả ô vi phạm ĐẦU TIÊN (B)");
            string? thrown = null; try { LenCheck.Max("12", 1, "M"); } catch (InvalidOperationException ex) { thrown = ex.Message; }
            Ok(thrown != null && thrown.StartsWith("M dài quá 1"), "Max: ném InvalidOperationException (lỗi nghiệp vụ)", thrown);
            var biz = UiSafe.Text(new InvalidOperationException("Số PO dài quá 50 ký tự"));
            Ok(biz == "Số PO dài quá 50 ký tự", "UiSafe.Text: InvalidOperationException chữ Việt (lỗi nghiệp vụ) → hiện nguyên", biz);
            var ef = UiSafe.Text(new InvalidOperationException("A second operation was started on this context instance before a previous operation completed."), null, "tải bảng");
            Ok(ef.StartsWith("Chưa tải bảng được") && !ef.Contains("second operation"), "UiSafe.Text: InvalidOperationException của EF (tiếng Anh) → câu chung, không lộ chữ Anh", ef);
            var sql = UiSafe.Text(new DbUpdateException("String or binary data would be truncated in table 'MES.dbo.WtsExtraItems'"), null, "lưu");
            Ok(sql.StartsWith("Chưa lưu được") && !sql.Contains("WtsExtraItems"), "UiSafe.Text: DbUpdateException → câu chung, không lộ tên bảng", sql);
            var ua = UiSafe.Text(new UnauthorizedAccessException("Bạn không có quyền sửa vùng Kế hoạch nhập của Part Master"), null, "tạo Part");
            Ok(ua.StartsWith("Bạn không có quyền"), "UiSafe.Text: UnauthorizedAccessException chữ Việt (PartMasterService) → hiện nguyên (rà độc lập T3)", ua);
            var ae = UiSafe.Text(new ArgumentException("Part No không được để trống"), null, "tạo Part");
            Ok(ae == "Part No không được để trống", "UiSafe.Text: ArgumentException chữ Việt → hiện nguyên", ae);
            var an = UiSafe.Text(new ArgumentNullException("x"), null, "tạo Part");
            Ok(an.StartsWith("Chưa tạo Part được"), "UiSafe.Text: ArgumentNullException (lỗi code) → câu chung", an);
            string? r = await UiSafe.RunAsync(() => Task.CompletedTask);
            Ok(r == null, "UiSafe.RunAsync: không lỗi → null");
            r = await UiSafe.RunAsync(() => throw new InvalidOperationException("Bạn không có quyền Sửa mục Kế hoạch"));
            Ok(r == "Bạn không có quyền Sửa mục Kế hoạch", "UiSafe.RunAsync: lỗi nghiệp vụ → đúng câu", r);
        }

        Console.WriteLine("[LN1] WtsSaveService — phát sinh: Mã WTS PS ≤ 50, Nội dung PS ≤ 500 (cột WtsExtraItems) — từ chối TRƯỚC khi ghi, lời Việt ở dòng + nhóm");
        {
            var db = Seed(); var s = WtsScen.Svc(db);
            var hs = new HandoverService(db);
            var i1 = await hs.IssueByCellAsync(1, "KHO", "GC", 40, 1, "MOI", false, "MOI", null, "NC1");
            var tx = db.HandoverTransactions.List.Last();
            var r1 = await hs.ReceiveAsync(tx.HandoverTxId, 40, 0, null, 1);
            Ok(i1.Ok && r1.Ok, "Dựng: Kho giao GC 40 → GC nhận 40 vào NC1", i1.Error + r1.Error);
            int gio = 8;
            WtsSaveService.WtsSaveGroup G(string code, string notes, int minutes = 5)
            {
                var g = new WtsSaveService.WtsSaveGroup { IsGc = true, HandoverGroupCode = "GC", NC = "NC1", MachineUsed = "M1", ProductionMode = "SERIAL", UserId = 1, CaId = null, WorkDate = DateTime.Today };
                var row = new WtsSaveService.WtsSaveRow { KhPlanDetailId = 1, PO = "PO1", QtyDone = 1, QtyOk = 1, QtyNg = 0, StartTime = DateTime.Today.AddHours(gio), EndTime = DateTime.Today.AddHours(gio + 1),
                    SetupTime_Actual = 1, MachiningTime_Actual = 1, InspectionTime_Actual = 1 };
                row.ExtraItems.Add(new WtsSaveService.WtsSaveExtra { WtsCode = code, WtsName = "PS", Minutes = minutes, Notes = notes });
                g.Rows.Add(row); gio++; return g;   // mỗi nhóm một giờ khác — tránh trùng giờ máy M1
            }
            int n0 = db.WtsProductionLogs.List.Count, e0 = db.WtsExtraItems.List.Count;
            var g1 = G("PS", S(501)); var res1 = await s.SaveGroupAsync(g1);
            Ok(!res1.Ok && Viet(g1.Rows[0].ErrorMsg) && g1.Rows[0].ErrorMsg!.Contains("Nội dung PS") && Viet(res1.ErrorMsg), "Nội dung PS 501 ký tự: từ chối, lời Việt ở dòng + nhóm", g1.Rows[0].ErrorMsg + " | " + res1.ErrorMsg);
            var g2 = G(S(51, 'P'), "ok"); var res2 = await s.SaveGroupAsync(g2);
            Ok(!res2.Ok && Viet(g2.Rows[0].ErrorMsg) && g2.Rows[0].ErrorMsg!.Contains("Mã WTS phát sinh"), "Mã WTS PS 51 ký tự: từ chối", g2.Rows[0].ErrorMsg);
            Ok(db.WtsProductionLogs.List.Count == n0 && db.WtsExtraItems.List.Count == e0, "  2 lần từ chối: không ghi dòng WTS / phát sinh nào");
            var g3 = G("PS", S(500)); var res3 = await s.SaveGroupAsync(g3);
            Ok(res3.Ok && db.WtsExtraItems.List.Count == e0 + 1 && db.WtsExtraItems.List.Last().Notes!.Length == 500, "Nội dung PS đúng 500 ký tự: lưu được (1 dòng phát sinh)", res3.ErrorMsg);
            var g4 = G("PS", S(501), minutes: 0); var res4 = await s.SaveGroupAsync(g4);
            Ok(res4.Ok, "Phát sinh 0 phút (không ghi) mang chữ dài: không xét (như cũ — dòng không được ghi)", res4.ErrorMsg);
        }

        Console.WriteLine("[LN2] KhPlanService — Tạo KH / Sửa PO / Tách PO: PO 50, Order VL 50, Vị trí 50, Loại 100, Tình trạng 200, Ghi chú 500 (cột KhPlanDetails)");
        {
            var db = Seed(); var kh = new KhPlanService(db);
            KhPlan P(string po, string? loc = null, string? type = null) { var p = new KhPlan { PlanDate = new DateTime(2026, 11, 16), CustomerId = 1 }; p.Details.Add(new KhPlanDetail { STD = DateTime.Today, PartNo = "P", PurchaseOrder = po, Quantity = 5, Location = loc, Type = type }); return p; }
            int n0 = db.KhPlanDetails.List.Count;
            string? e1 = null; try { await kh.CreateAsync(P(S(51, 'A')), 1); } catch (Exception ex) { e1 = ex.Message; }
            Ok(Viet(e1) && e1!.Contains("Số PO dài quá 50"), "Tạo KH: PO 51 ký tự → từ chối lời Việt", e1);
            string? e2 = null; try { await kh.CreateAsync(P("PO-L2", loc: S(51)), 1); } catch (Exception ex) { e2 = ex.Message; }
            Ok(Viet(e2) && e2!.Contains("Vị trí dài quá 50"), "Tạo KH: Vị trí 51 → từ chối", e2);
            string? e3 = null; try { await kh.CreateAsync(P("PO-L3", type: S(101)), 1); } catch (Exception ex) { e3 = ex.Message; }
            Ok(Viet(e3) && e3!.Contains("Loại dài quá 100"), "Tạo KH: Loại 101 → từ chối", e3);
            Ok(db.KhPlanDetails.List.Count == n0, "  không ghi dòng PO nào");
            string? e4 = null; try { await kh.CreateAsync(P(S(50, 'B'), loc: S(50), type: S(100)), 1); } catch (Exception ex) { e4 = ex.Message; }
            Ok(e4 == null && db.KhPlanDetails.List.Count == n0 + 1, "Tạo KH: PO 50 / Vị trí 50 / Loại 100 (đúng trần) → tạo được", e4);
            // Sửa PO: SaveEditAsync trả lời báo (không ném)
            KhPlanService.KhEditValues V(string? loc = null, string? notes = null) => new() { Quantity = 40, Unit = "EA", STD = new DateTime(2026, 12, 1), Location = loc, PlanNotes = notes };
            var r1 = await kh.SaveEditAsync(1, 1, V(), V(loc: S(51)), 1, "sua vi tri");
            Ok(Viet(r1) && r1!.Contains("Vị trí dài quá 50"), "Sửa PO: Vị trí 51 → lời Việt (trước khi mở khoá PO)", r1);
            var r2 = await kh.SaveEditAsync(1, 1, V(), V(notes: S(501)), 1, "sua ghi chu");
            Ok(Viet(r2) && r2!.Contains("Ghi chú phiếu dài quá 500"), "Sửa PO: Ghi chú phiếu 501 → lời Việt", r2);
            Ok(db.KhPlanDetails.List.First(d => d.KhPlanDetailId == 1).Location == null, "  Vị trí PO1 không đổi");
            // Tách PO (PO2 trắng)
            var plan = db.KhPlans.List.First();
            var idMoi = db.KhPlanDetails.List.Max(d => d.KhPlanDetailId) + 1;
            var d2 = new KhPlanDetail { STD = DateTime.Today, KhPlanDetailId = idMoi, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO2", Quantity = 10, LineNo = 2 };
            db.KhPlanDetails.List.Add(d2); plan.Details.Add(d2); db.Fixup();
            string? e5 = null; try { await kh.SplitPOAsync(idMoi, new List<SplitPORow> { new() { PurchaseOrder = S(51, 'C'), Quantity = 4 }, new() { PurchaseOrder = "PO2-B", Quantity = 6 } }, 1, "tach"); } catch (Exception ex) { e5 = ex.Message; }
            Ok(Viet(e5) && e5!.Contains("Số PO mới dài quá 50"), "Tách PO: PO mới 51 ký tự → từ chối", e5);
            Ok(db.KhPlanDetails.List.Any(d => d.KhPlanDetailId == idMoi) && !db.KhPlanDetails.List.Any(d => d.PurchaseOrder == "PO2-B"), "  dòng gốc còn, không có dòng mới");
        }

        Console.WriteLine("[LN3] CustomerService — Mã KH 20, Tên 200, Supplier Code 20; lý do sửa 500 (CustomerChangeLogs)");
        {
            var db = Seed(); var cs = new CustomerService(db);
            var c1 = await cs.CreateAsync(new Customer { CustomerCode = S(21, 'K'), CustomerName = "Khach" }, 1);
            Ok(!c1.Success && Viet(c1.Error) && c1.Error!.Contains("Mã khách hàng dài quá 20"), "Thêm: Mã KH 21 → từ chối lời Việt", c1.Error);
            var c2 = await cs.CreateAsync(new Customer { CustomerCode = "K2", CustomerName = S(201) }, 1);
            Ok(!c2.Success && Viet(c2.Error) && c2.Error!.Contains("Tên khách hàng dài quá 200"), "Thêm: Tên 201 → từ chối", c2.Error);
            var c3 = await cs.CreateAsync(new Customer { CustomerCode = S(20, 'K'), CustomerName = S(200), SupplierCode = S(20) }, 1);
            Ok(c3.Success, "Thêm: đúng trần 20 / 200 / 20 → được", c3.Error);
            var u1 = await cs.UpdateAsync(new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", SupplierCode = S(21) }, "sua sc", 1);
            Ok(!u1.Success && Viet(u1.Error), "Sửa: Supplier Code 21 → từ chối (trước: SqlException không bắt → trang chết)", u1.Error);
            var u2 = await cs.UpdateAsync(new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH" }, S(501), 1);
            Ok(!u2.Success && Viet(u2.Error) && u2.Error!.Contains("Lý do sửa dài quá 500"), "Sửa: lý do 501 → từ chối", u2.Error);
            Ok(db.Customers.List.First(x => x.CustomerId == 1).SupplierCode == null, "  khách KH1 không đổi");
        }

        Console.WriteLine("[LN4] UserService — Tên đăng nhập 50, Họ tên 100, Mã nhóm 50, Tên nhóm 100");
        {
            var db = Seed();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            var gc = new UserGroup { GroupId = 2, GroupCode = "GC", GroupName = "Gia công", IsActive = true, Permissions = "[]" };
            db.UserGroups.List.Add(gc); db.Fixup();
            var r1 = await us.CreateUserAsync(new User { Username = S(51, 'u'), FullName = "A", GroupId = 2 }, "123456", 1);
            Ok(!r1.Success && Viet(r1.Error) && r1.Error!.Contains("Tên đăng nhập dài quá 50"), "Thêm User: tên đăng nhập 51 → từ chối", r1.Error);
            var r2 = await us.CreateUserAsync(new User { Username = "cn01", FullName = S(101), GroupId = 2 }, "123456", 1);
            Ok(!r2.Success && Viet(r2.Error) && r2.Error!.Contains("Họ tên dài quá 100"), "Thêm User: họ tên 101 → từ chối", r2.Error);
            Ok(!db.Users.List.Any(u => u.Username == "cn01" || u.Username.StartsWith("uuu")), "  không tạo tài khoản");
            var r3 = await us.CreateUserAsync(new User { Username = S(50, 'u'), FullName = S(100), GroupId = 2 }, "123456", 1);
            Ok(r3.Success, "Thêm User: đúng trần 50 / 100 → được", r3.Error);
            var g1 = await us.CreateGroupAsync(new UserGroup { GroupCode = S(51, 'g'), GroupName = "N" }, 1);
            Ok(!g1.Success && Viet(g1.Error) && g1.Error!.Contains("Mã nhóm dài quá 50"), "Thêm Nhóm: mã 51 → từ chối", g1.Error);
            var g2 = await us.UpdateGroupAsync(new UserGroup { GroupId = 2, GroupCode = "GC", GroupName = S(101), IsActive = true }, 1);
            Ok(!g2.Success && Viet(g2.Error) && g2.Error!.Contains("Tên nhóm dài quá 100"), "Sửa Nhóm: tên 101 → từ chối", g2.Error);
            Ok(gc.GroupName == "Gia công", "  tên nhóm GC không đổi");
        }

        Console.WriteLine("[LN5] CaLamViecService — Tên ca 100, ghi chú nghỉ 100; ca 0 phút chặn; nhánh SỬA lỗi DB → quay lại + lời Việt, bản EF không còn kẹt (trước: ngoại lệ lên trang)");
        {
            var db = Seed(); var ca = new CaLamViecService(db);
            var r1 = await ca.SaveAsync(new CaLamViec { TenCa = S(101), GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) }, new(), 1);
            Ok(!r1.Ok && Viet(r1.Error) && r1.Error.Contains("Tên ca dài quá 100"), "Tạo ca: tên 101 → từ chối", r1.Error);
            var r2 = await ca.SaveAsync(new CaLamViec { TenCa = "Ca 0", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(7, 0) }, new(), 1);
            Ok(!r2.Ok && r2.Error.Contains("ca 0 phút"), "Tạo ca: bắt đầu = kết thúc (ca 0 phút) → chặn [CA-0-PHUT]", r2.Error);
            var okCa = new CaLamViec { TenCa = "Ca ngay", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) };
            var r3 = await ca.SaveAsync(okCa, new() { new CaLamViec_NghiGiaiLao { NghiBatDau = new TimeOnly(11, 30), NghiKetThuc = new TimeOnly(12, 30), GhiChu = S(100) } }, 1);
            Ok(r3.Ok && okCa.CaId > 0, "Tạo ca hợp lệ (ghi chú nghỉ đúng 100) → được", r3.Error);
            var r4 = await ca.SaveAsync(new CaLamViec { CaId = okCa.CaId, TenCa = "Ca ngay", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) },
                new() { new CaLamViec_NghiGiaiLao { NghiBatDau = new TimeOnly(11, 30), NghiKetThuc = new TimeOnly(12, 30), GhiChu = S(101) } }, 1);
            Ok(!r4.Ok && Viet(r4.Error) && r4.Error.Contains("Ghi chú giờ nghỉ dài quá 100"), "Sửa ca: ghi chú nghỉ 101 → từ chối", r4.Error);
            // nhánh Sửa lỗi DB giả lúc SaveChanges
            int fire = 0; Microsoft.EntityFrameworkCore.DbContext.OnSave = () => ++fire == 1 ? new DbUpdateException("gia loi DB luc sua ca") : null;
            (bool Ok, string Error) r5; try { r5 = await ca.SaveAsync(new CaLamViec { CaId = okCa.CaId, TenCa = "Ca ngay 2", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) }, new(), 1); }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
            Ok(!r5.Ok && r5.Error.StartsWith("Chưa lưu ca được") && !r5.Error.Contains("gia loi"), "Sửa ca gặp lỗi DB: trả (false, câu Việt chung), không ném ra trang", r5.Error);
            Ok(db.CaLamViecs.Added.Count == 0 && db.CaLamViec_NghiGiaiLaos.Added.Count == 0 && db.CaLamViecChangeLogs.Added.Count == 0 && fire == 1,
               "  bản EF sau lỗi: không còn dòng Added chờ ghi ở Ca / Nghỉ / ChangeLog (service đã Detached; stub Entries<T>() đọc Pending thật — T6)",
               $"ca={db.CaLamViecs.Added.Count} nghi={db.CaLamViec_NghiGiaiLaos.Added.Count} log={db.CaLamViecChangeLogs.Added.Count}");
            int log0 = db.CaLamViecChangeLogs.List.Count;
            var r6 = await ca.SaveAsync(new CaLamViec { CaId = okCa.CaId, TenCa = "Ca ngay 3", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0) }, new(), 1);
            Ok(r6.Ok && db.CaLamViecs.List.First(x => x.CaId == okCa.CaId).TenCa == "Ca ngay 3" && db.CaLamViecChangeLogs.List.Count == log0 + 1,
               "  lưu lại sau lỗi: được, tên = Ca ngay 3, chỉ +1 dòng ChangeLog (không ghi lại log của lần lỗi)", $"{r6.Error} log {log0}→{db.CaLamViecChangeLogs.List.Count}");
        }

        Console.WriteLine("[LN6] StandardWtsTaskService / DaoService / ThietBiService / DoGaService — cột danh mục (mã 50, tên nhóm 150, Ký hiệu dao 50, Số máy 50, Tên đồ gá 150, người mượn 150)");
        {
            var db = Seed(); var auth = new AuthService(db);
            var wts = new StandardWtsTaskService(db, auth);
            StandardWtsTask T(string code, string catName = "KCS", string? ghiChu = null) => new() { TaskCode = code, TaskName = "Viec", CategoryCode = "KCS", CategoryName = catName, DefaultUnit = "Chi tiết", GhiChu = ghiChu, IsProductiveTask = true };
            var w1 = await wts.CreateAsync(T(S(51, 'W')), 1, "1111");
            Ok(!w1.Success && Viet(w1.Error) && w1.Error!.Contains("Mã công việc dài quá 20"), "WTS chuẩn: mã 51 → từ chối (trước: DbUpdateException không bắt → trang chết) — đáp án đổi 20/11 [WTS-MA-20]: trần 20", w1.Error);
            var w2 = await wts.CreateAsync(T("9K", catName: S(151)), 1, "1111");
            Ok(!w2.Success && Viet(w2.Error) && w2.Error!.Contains("Tên nhóm công đoạn dài quá 150"), "WTS chuẩn: tên nhóm 151 → từ chối", w2.Error);
            var w3 = await wts.CreateAsync(T("9K", ghiChu: S(500)), 1, "1111");
            Ok(w3.Success && db.StandardWtsTasks.List.Any(t => t.TaskCode == "9K"), "WTS chuẩn: ghi chú đúng 500 → tạo được", w3.Error);
            var t9 = db.StandardWtsTasks.List.First(t => t.TaskCode == "9K");
            var w4 = await wts.UpdateAsync(new StandardWtsTask { TaskId = t9.TaskId, TaskCode = "9K", TaskName = S(251), CategoryCode = "KCS", CategoryName = "KCS", DefaultUnit = "Chi tiết", IsProductiveTask = true }, "sua ten", 1, "1111");
            Ok(!w4.Success && Viet(w4.Error) && w4.Error!.Contains("Tên công việc dài quá 250"), "WTS chuẩn sửa: tên 251 → từ chối", w4.Error);

            var dao = new DaoService(db);
            var d1 = await dao.CreateAsync(new Dao { TenDao = S(51, 'D'), MaDao = S(51, 'D') }, 1);
            Ok(!d1.Success && Viet(d1.Error) && d1.Error!.Contains("Ký hiệu dao dài quá 50"), "Dao: ký hiệu 51 (trang gán MaDao = TenDao, cột MaDao 50) → từ chối [BM1-05]", d1.Error);
            var d2 = await dao.CreateAsync(new Dao { TenDao = S(50, 'D'), MaDao = S(50, 'D'), LoaiDao = S(100) }, 1);
            Ok(d2.Success, "Dao: ký hiệu 50 / loại 100 → được", d2.Error);

            var tb = new ThietBiService(db, auth);
            var m1 = await tb.CreateAsync(new ThietBi { SoMay = S(51, 'M'), TenMay = "May" }, 1);
            Ok(!m1.Success && Viet(m1.Error) && m1.Error!.Contains("Số máy dài quá 50"), "Thiết bị: số máy 51 → từ chối [BM1-15]", m1.Error);
            var m2 = await tb.CreateAsync(new ThietBi { SoMay = "M9", TenMay = S(101) }, 1);
            Ok(!m2.Success && Viet(m2.Error) && m2.Error!.Contains("Tên máy dài quá 100"), "Thiết bị: tên máy 101 → từ chối", m2.Error);
            Ok(!db.ThietBis.List.Any(x => x.SoMay == "M9"), "  không ghi máy");

            var dg = new DoGaService(db, auth);
            var g1 = await dg.CreateAsync(new DoGa { TenDoGa = S(151, 'G') }, 1, "1111");
            Ok(!g1.Success && Viet(g1.Error) && g1.Error!.Contains("Tên đồ gá dài quá 150"), "Đồ gá: tên 151 → từ chối", g1.Error);
            var g2 = await dg.CreateAsync(new DoGa { TenDoGa = "VJ63", BuLong = S(250) }, 1, "1111");
            Ok(g2.Success, "Đồ gá: bu lông đúng 250 → được", g2.Error);
            var vj = db.DoGas.List.First(x => x.TenDoGa == "VJ63");
            var g3 = await dg.MuonDoGaAsync(vj.DoGaId, S(151, 'N'), DateTime.Now, DateTime.Now.AddDays(1), "ly do", 1, "1111");
            Ok(!g3.Success && Viet(g3.Error) && g3.Error!.Contains("Người mượn dài quá 150"), "Mượn đồ gá: người mượn 151 → từ chối", g3.Error);
            Ok(vj.TrangThai != "Đang mượn", "  đồ gá không đổi trạng thái");
        }

        Console.WriteLine("[LN7] Part Master — NC / mã CV ≤ 20, lý do ≤ 450 (service nối thêm chữ vào Reason 500), đồ gá / dao ghép ≤ 50 / 100 ở dòng NC chính");
        {
            var db = Seed(); var mach = new PartMachiningService(db); var taro = new PartTaroService(db);
            string? e1 = null; try { await mach.AddAsync(1, new PartMachiningStep { NC = S(21, 'N'), StepOrder = 1 }, 1, "them nc"); } catch (Exception ex) { e1 = ex.Message; }
            Ok(e1 != null && Viet(e1) && e1.Contains("NC dài quá 20"), "Gia công thêm NC 21 ký tự → PartStepCodeException lời Việt", e1);
            string? e2 = null; try { await mach.AddAsync(1, new PartMachiningStep { NC = "NC9", StepOrder = 1, FixtureType = S(51, 'F') }, 1, "them nc"); } catch (Exception ex) { e2 = ex.Message; }
            Ok(e2 != null && Viet(e2) && e2.Contains("Đồ gá (ghép) dài quá 50"), "Gia công thêm NC: đồ gá ghép 51 → từ chối [BP1-02]", e2);
            string? e3 = null; try { await mach.AddAsync(1, new PartMachiningStep { NC = "NC9", StepOrder = 1 }, 1, S(451)); } catch (Exception ex) { e3 = ex.Message; }
            Ok(e3 != null && e3.Contains("Lý do dài quá 450"), "Gia công thêm NC: lý do 451 → từ chối (ArgumentException lời Việt)", e3);
            Ok(!db.PartMachiningSteps.List.Any(s => s.PartId == 1), "  không ghi dòng NC nào");
            string? e4 = null; try { await mach.AddAsync(1, new PartMachiningStep { NC = S(20, 'N'), StepOrder = 1, FixtureType = S(50), ToolType = S(100) }, 1, S(450)); } catch (Exception ex) { e4 = ex.Message; }
            Ok(e4 == null && db.PartMachiningSteps.List.Any(s => s.PartId == 1), "Gia công thêm NC đúng trần 20 / 50 / 100 / 450 → được", e4);
            string? e5 = null; try { await taro.AddAsync(1, new PartTaroStep { NC = "NC1", WtsTaskCode = S(21, 'T'), StepOrder = 1 }, 1, "them taro"); } catch (Exception ex) { e5 = ex.Message; }
            Ok(e5 != null && Viet(e5) && e5.Contains("Mã CV dài quá 20"), "Taro thêm: mã CV 21 → từ chối", e5);
        }

        Console.WriteLine("[LN8] HandoverService.ReceiveAsync — lý do đổi phân loại ≤ 500 (NgReason) [BH-05]; DepartmentService — tên 150 + khoá MES_DEPT_NEW quanh Max+1 [BM1-06]");
        {
            var db = Seed(); var hs = new HandoverService(db);
            var i1 = await hs.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI", null, "NC1");
            var tx = db.HandoverTransactions.List.Last();
            var r1 = await hs.ReceiveAsync(tx.HandoverTxId, 0, 10, null, 1, toCell: HandoverCell.Loi, toCellReason: S(501), ngCategoryId: 1);
            Ok(i1.Ok && !r1.Ok && Viet(r1.Error) && r1.Error.Contains("Lý do đổi phân loại dài quá 500"), "Nhận đổi sang ô Hàng lỗi với lý do 501 → từ chối lời Việt", r1.Error);
            Ok(!db.HandoverReceives.List.Any(r => r.HandoverTxId == tx.HandoverTxId), "  chưa có lần nhận nào");
            var r2 = await hs.ReceiveAsync(tx.HandoverTxId, 0, 10, null, 1, toCell: HandoverCell.Loi, toCellReason: S(500), ngCategoryId: 1);
            Ok(r2.Ok, "Lý do đúng 500 → nhận được", r2.Error);

            var dep = new DepartmentService(db, new AuthService(db));
            var locks = Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames; locks.Clear();
            var p1 = await dep.CreateAsync(new Department { DepartmentCode = "P1", DepartmentName = S(151) }, 1, "1111");
            Ok(!p1.Success && Viet(p1.Error) && p1.Error!.Contains("Tên đơn vị / nhóm dài quá 150"), "Sơ đồ TC thêm: tên 151 → từ chối", p1.Error);
            var p2 = await dep.CreateAsync(new Department { DepartmentCode = "P2", DepartmentName = "Phong 2", RoleTitle = "TP", ManagerName = "A" }, 1, "1111");
            Ok(p2.Success && db.Departments.List.Any(d => d.DepartmentCode == "P2"), "Sơ đồ TC thêm hợp lệ → được", p2.Error);
            Ok(locks.Contains("MES_DEPT_NEW"), "  có khoá sp_getapplock MES_DEPT_NEW quanh Max+1 + SaveChanges (LockOwner = Transaction)", string.Join(" → ", locks));
            Ok(db.Database.Current == null, "  giao dịch tự mở đã đóng");
            var p3 = await dep.CreateAsync(new Department { DepartmentCode = "P3", DepartmentName = "Phong 3" }, 1, "1111");
            Ok(p3.Success && db.Departments.List.Where(d => d.DepartmentCode is "P2" or "P3").Select(d => d.DepartmentId).Distinct().Count() == 2, "Thêm tiếp: ID khác nhau (Max+1 dưới khoá)", p3.Error);
            // lỗi DB lúc SaveChanges trong giao dịch khoá → rollback + tách bản EF, lời Việt chung; lần sau thêm được đúng 1 dòng
            int dl0 = db.DepartmentChangeLogs.List.Count, dn0 = db.Departments.List.Count;
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => new DbUpdateException("gia loi DB luc them don vi");
            (bool Success, string? Error) p4; try { p4 = await dep.CreateAsync(new Department { DepartmentCode = "P4", DepartmentName = "Phong 4" }, 1, "1111"); }
            finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
            Ok(!p4.Success && p4.Error != null && p4.Error.StartsWith("Chưa") && !p4.Error.Contains("gia loi"), "Thêm gặp lỗi DB: (false, câu Việt chung), không ném", p4.Error);
            Ok(db.Departments.Added.Count == 0 && db.DepartmentChangeLogs.Added.Count == 0 && db.Database.Current == null, "  bản EF tách hết, giao dịch đã đóng", $"dept={db.Departments.Added.Count} log={db.DepartmentChangeLogs.Added.Count}");
            var p5 = await dep.CreateAsync(new Department { DepartmentCode = "P5", DepartmentName = "Phong 5" }, 1, "1111");
            Ok(p5.Success && db.Departments.List.Count == dn0 + 1 && db.DepartmentChangeLogs.List.Count == dl0 + 1 && !db.Departments.List.Any(d => d.DepartmentCode == "P4"),
               "  thêm lại sau lỗi: +1 đơn vị, +1 log, không có P4 ma", $"dept {dn0}→{db.Departments.List.Count} log {dl0}→{db.DepartmentChangeLogs.List.Count}");
        }

        Console.WriteLine("[LN9] PERM-HASH — UserService.SamePerms: lưu ma trận chỉ ghi lại JSON của nhóm có quyền THẬT SỰ đổi (không đá cả xưởng khi Admin chỉ bấm Lưu)");
        {
            static MES.Web.Constants.GroupPermissionSetting P(string c, bool v, bool e) => new() { SectionCode = c, CanView = v, CanEdit = e };
            var oldJson = System.Text.Json.JsonSerializer.Serialize(new List<MES.Web.Constants.GroupPermissionSetting> { P("WTS_GC", true, true), P("HANDOVER_GC", true, false) });
            Ok(UserService.SamePerms(oldJson, new() { P("HANDOVER_GC", true, false), P("WTS_GC", true, true), P("HIEU_SUAT_MAY", false, false), P("KE_HOACH", false, false) }),
               "cùng ô bật, khác thứ tự + thêm mục mới toàn ô trống (HIEU_SUAT_MAY) → GIỐNG → không ghi lại JSON, hash giữ nguyên, không ai bị đăng xuất");
            Ok(!UserService.SamePerms(oldJson, new() { P("WTS_GC", true, false), P("HANDOVER_GC", true, false) }), "rút ô Sửa WTS_GC → KHÁC → ghi lại");
            Ok(!UserService.SamePerms(oldJson, new() { P("WTS_GC", true, true), P("HANDOVER_GC", true, false), P("HIEU_SUAT_MAY", true, false) }), "thêm ô Xem HIEU_SUAT_MAY → KHÁC → ghi lại");
            Ok(UserService.SamePerms(null, new() { P("WTS_GC", false, false) }) && UserService.SamePerms("", new()), "JSON cũ null / rỗng so với ma trận toàn ô trống → GIỐNG");
            Ok(!UserService.SamePerms("{hỏng", new()), "JSON cũ hỏng → KHÁC (ghi lại cho sạch)");
            // qua hàm thật: UpdateAllGroupPermissionsAsync với ma trận y nguyên → Permissions không đổi (cùng chuỗi)
            var db = Seed();
            var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var us = new UserService(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp));
            var gc = new UserGroup { GroupId = 2, GroupCode = "GC", GroupName = "Gia công", IsActive = true, Permissions = oldJson };
            db.UserGroups.List.Add(gc); db.Fixup();
            var r1 = await us.UpdateAllGroupPermissionsAsync(new() { [2] = new() { P("HIEU_SUAT_MAY", false, false), P("HANDOVER_GC", true, false), P("WTS_GC", true, true) } }, 1);
            Ok(r1.Success && ReferenceEquals(gc.Permissions, oldJson), "UpdateAllGroupPermissionsAsync ma trận y nguyên (thứ tự khác) → chuỗi Permissions giữ NGUYÊN (PermHash không đổi)", r1.Error);
            var r2 = await us.UpdateAllGroupPermissionsAsync(new() { [2] = new() { P("HANDOVER_GC", true, false), P("WTS_GC", true, true), P("HIEU_SUAT_MAY", true, false) } }, 1);
            Ok(r2.Success && gc.Permissions != oldJson && gc.Permissions!.Contains("HIEU_SUAT_MAY"), "tick thêm Xem HIEU_SUAT_MAY → JSON đổi (nhóm này đăng nhập lại)", r2.Error);
        }

        Console.WriteLine($"Tổng phép kiểm: {checks}");

        // [WTS-MA-20][WTS-TAO-1TX][TB-TAO-1TX][CA-LOG-ILOGGER 2026-11-20] gói nhỏ G23 / G24-GHI-NHO. Nguồn: CLAUDE rà độc lập Gói 23 (L7, L10, L15) + Gói 24; Hùng 20/11 18:12.
        Console.WriteLine("[LN10] mã công việc ≤ 20 (cột WtsCode / WtsTaskCode khâu sau); Tạo công việc / thiết bị 1 giao dịch — lỗi DB lần ghi lịch sử → không còn công việc / máy, bản EF sạch, lưu lại được");
        {
            var db = Seed(); var auth = new AuthService(db);
            var wts = new StandardWtsTaskService(db, auth);
            StandardWtsTask T(string code) => new() { TaskCode = code, TaskName = "Viec", CategoryCode = "KCS", CategoryName = "KCS", DefaultUnit = "Chi tiết", IsProductiveTask = true };
            var a1 = await wts.CreateAsync(T(S(21, 'W')), 1, "1111");
            Ok(!a1.Success && a1.Error!.Contains("Mã công việc dài quá 20"), "[LN10a] mã 21 → từ chối (WorkerActivityLogs.WtsCode 20)", a1.Error);
            var a2 = await wts.CreateAsync(T(S(20, 'W')), 1, "1111");
            Ok(a2.Success, "[LN10a] mã đúng 20 → được", a2.Error);
            int n0 = db.StandardWtsTasks.List.Count, l0 = db.StandardWtsTaskChangeLogs.List.Count;
            int fire = 0; Microsoft.EntityFrameworkCore.DbContext.OnSave = () => ++fire == 2 ? new DbUpdateException("gia loi DB luc ghi lich su") : null;
            (bool Success, string? Error) a3; var m3 = T("8Q");
            try { a3 = await wts.CreateAsync(m3, 1, "1111"); } finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
            Ok(!a3.Success && a3.Error!.StartsWith("Chưa thêm công việc tiêu chuẩn được") && !a3.Error!.Contains("gia loi") && fire == 2, "[LN10b] Tạo công việc: lỗi DB ở lần ghi lịch sử → (false, câu Việt), không ném", a3.Error);
            Ok(db.StandardWtsTasks.List.Count == n0 && db.StandardWtsTaskChangeLogs.List.Count == l0 && db.StandardWtsTasks.Added.Count == 0 && db.StandardWtsTaskChangeLogs.Added.Count == 0 && m3.TaskId == 0,
               "[LN10b]   giao dịch quay lại: không còn công việc 8Q (trước: có công việc mà không có dòng Tạo mới), bản EF sạch", $"task {n0}→{db.StandardWtsTasks.List.Count} log {l0}→{db.StandardWtsTaskChangeLogs.List.Count}");
            var a4 = await wts.CreateAsync(T("8Q"), 1, "1111");
            Ok(a4.Success && db.StandardWtsTasks.List.Count(t => t.TaskCode == "8Q") == 1 && db.StandardWtsTaskChangeLogs.List.Count == l0 + 1, "[LN10b]   lưu lại sau lỗi: 1 công việc + 1 dòng lịch sử", a4.Error);

            var tb = new ThietBiService(db, auth);
            int t0 = db.ThietBis.List.Count, tl0 = db.ThietBiChangeLogs.List.Count; fire = 0;
            Microsoft.EntityFrameworkCore.DbContext.OnSave = () => ++fire == 2 ? new DbUpdateException("gia loi DB luc ghi lich su may") : null;
            (bool Success, string? Error) b1; var mm = new ThietBi { SoMay = "MX1", TenMay = "May X" };
            try { b1 = await tb.CreateAsync(mm, 1); } finally { Microsoft.EntityFrameworkCore.DbContext.OnSave = null; }
            Ok(!b1.Success && b1.Error!.StartsWith("Chưa thêm thiết bị được") && !b1.Error!.Contains("gia loi") && db.ThietBis.List.Count == t0 && db.ThietBiChangeLogs.List.Count == tl0 && db.ThietBis.Added.Count == 0 && mm.ThietBiId == 0,
               "[LN10c] Thêm thiết bị: lỗi DB lần ghi lịch sử → quay lại, không còn máy, bản EF sạch (trước: máy kẹt Added trong DbContext phiên trang)", $"{b1.Error} may {t0}→{db.ThietBis.List.Count}");
            var b2 = await tb.CreateAsync(mm, 1);
            Ok(b2.Success && db.ThietBis.List.Count(x => x.SoMay == "MX1") == 1 && db.ThietBiChangeLogs.List.Count == tl0 + 1, "[LN10c]   bấm Lưu lại (cùng đối tượng form): 1 máy + 1 dòng lịch sử", b2.Error);
            // [EF-GHOST 2026-11-20] stub nay có bản Unchanged ma sau rollback: lần lỗi trên (máy đã ghi rồi giao dịch quay lại) KHÔNG để lại bản ma ThietBi — service gỡ đúng
            //   (đột biến bỏ dòng gỡ Entries<ThietBi>… Detached ở ThietBiService.CreateAsync phải SAI ở đây — trước Gói 28 lọt)
            Ok(!db.Ghosts.OfType<ThietBi>().Any() && !db.Ghosts.OfType<StandardWtsTask>().Any(), "[LN10d] sau lỗi Tạo máy / công việc: không còn bản EF Unchanged ma (service gỡ đúng — stub EF-GHOST Gói 28)",
               string.Join(",", db.Ghosts.Select(g => g.GetType().Name)));
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }
}
