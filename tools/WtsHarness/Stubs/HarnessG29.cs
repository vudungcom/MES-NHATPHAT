// [GÓI 29 21/11/2026] HarnessG29 — dữ liệu cho drive_g29.py ([G29-RPT] WTS Report theo kỳ, [G29-HSM-KY] Hiệu suất máy theo kỳ,
//   [G29-HSM-MAUSO] % trên phút kế hoạch, [G29-TIMNHANH] ô Tìm nhanh). Ghi THẲNG bảng (như dữ liệu đã lưu), không qua WtsSaveService —
//   có cả dòng việc chồng giờ dòng sản xuất (dữ liệu cũ / Workflow OFF) để kiểm luật "phút có cả có ích lẫn vô ích → có ích".
//   POST /harness/g29-seed → ca "Ca G29" 08:00–17:00 nghỉ 12:00–13:00; người g29a "Công nhân A29", g29b "Công nhân B29"; máy M29A, M29B;
//   mã việc HOP29 / VS29 (vô ích), SUA29 (có ích); PO-G29; 1 lần dừng M29B ngày 2 10:00–11:00 "Kẹt dao". Ngày 1 = thứ Hai tuần này, ngày 2 = thứ Ba.
//   Đáp án tính tay (drive_g29.py đầu file).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG29
{
    public static DateTime Monday => DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));

    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G29"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var gGc = db.UserGroups.List.First(g => g.GroupCode == "GC");
            var cust = db.Customers.List.First();
            var t0 = Monday.AddDays(-3);
            var ca = new CaLamViec { TenCa = "Ca G29", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(17, 0), IsActive = true, SortOrder = 9 };
            db.CaLamViecs.Add(ca); await db.SaveChangesAsync();
            db.CaLamViec_NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { CaId = ca.CaId, NghiBatDau = new TimeOnly(12, 0), NghiKetThuc = new TimeOnly(13, 0), GhiChu = "Nghỉ trưa" });
            var hash = PasswordHasher.Hash("123456");
            var ua = new User { Username = "g29a", FullName = "Công nhân A29", PasswordHash = hash, GroupId = gGc.GroupId, IsActive = true };
            var ub = new User { Username = "g29b", FullName = "Công nhân B29", PasswordHash = hash, GroupId = gGc.GroupId, IsActive = true };
            db.Users.AddRange(ua, ub);
            db.ThietBis.Add(new ThietBi { SoMay = "M29A", BoPhan = "GC", MaMay = "M29A", TenMay = "Phay G29 A", LoaiMay = "CNC", IsActive = true, CreatedBy = admin.UserId });
            var mb = new ThietBi { SoMay = "M29B", BoPhan = "GC", MaMay = "M29B", TenMay = "Phay G29 B", LoaiMay = "CNC", IsActive = true, CreatedBy = admin.UserId };
            db.ThietBis.Add(mb);
            db.ThietBis.Add(new ThietBi { SoMay = "M29T", BoPhan = "TARO", MaMay = "M29T", TenMay = "Taro G29", LoaiMay = "TARO", IsActive = true, CreatedBy = admin.UserId });   // [G31-SCEN] máy bộ phận khác — ô Bộ phận (drive_g29 M5)
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "CHUNG", CategoryName = "Chung", TaskCode = "HOP29", TaskName = "Họp đầu ca", IsActive = true, IsProductiveTask = false, CreatedBy = admin.UserId });
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "CHUNG", CategoryName = "Chung", TaskCode = "VS29", TaskName = "Vệ sinh máy", IsActive = true, IsProductiveTask = false, CreatedBy = admin.UserId });
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "CHUNG", CategoryName = "Chung", TaskCode = "SUA29", TaskName = "Sửa hàng lỗi", IsActive = true, IsProductiveTask = true, CreatedBy = admin.UserId });
            // [WTS-1NGUON 26/11] mã dùng cho Gia công (có ích) — giữ đáp án cũ "Gia công = có ích"; bản trước gói không có cột IsGcTask → mk.sh bỏ dòng này
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "GIA_CONG", CategoryName = "Gia công", TaskCode = "GC29", TaskName = "Gia công (mã chung)", IsActive = true, IsProductiveTask = true, IsGcTask = true, CreatedBy = admin.UserId });   // [W1N-GC29]
            var plan = new KhPlan { PlanNo = "KH-G29", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = false, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(plan); await db.SaveChangesAsync();
            var det = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-G29", PurchaseOrder = "PO-G29", Quantity = 500, Unit = "EA", STD = t0.AddDays(20), CreatedAt = t0 };
            db.KhPlanDetails.Add(det); await db.SaveChangesAsync();
            var d1 = Monday; var d2 = Monday.AddDays(1);
            long id = 990100;
            void P(User u, string mach, string mode, DateTime s, DateTime e, decimal ok, decimal ng, string nc)
                => db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = ++id, KhPlanDetailId = det.KhPlanDetailId, ProcessGroup = "GC", NC = nc, MachineUsed = mach,
                    ProductionMode = mode, WorkerId = u.UserId, QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, NgReason = ng > 0 ? "Sai kích thước" : null,
                    StartTime = s, EndTime = e, CaId = ca.CaId, CreatedAt = s });
            long aid = 990200;
            void A(User u, string code, string name, DateTime s, DateTime e)
                => db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = ++aid, WorkerId = u.UserId, WorkDate = DateOnly.FromDateTime(s), StartTime = s, EndTime = e,
                    WtsCode = code, WtsName = name, CreatedAt = s });
            // Ngày 1 — A: 2 máy chồng 09–10, họp 11:00–11:30, chạy thử 13–14, sửa hàng 14–15
            P(ua, "M29A", "serial", d1.AddHours(8), d1.AddHours(10), 40, 1, "NC01");
            P(ua, "M29B", "serial", d1.AddHours(9), d1.AddHours(11), 30, 0, "NC02");
            A(ua, "HOP29", "Họp đầu ca", d1.AddHours(11), d1.AddHours(11.5));
            P(ua, "M29A", "trial", d1.AddHours(13), d1.AddHours(14), 1, 0, "NC03");
            A(ua, "SUA29", "Sửa hàng lỗi", d1.AddHours(14), d1.AddHours(15));
            // Ngày 1 — B: vệ sinh 08:00–08:30, M29B 08:30–12:00 (cùng máy với A 09–11), họp 11:30–12:00 CHỒNG dòng sản xuất (có ích thắng)
            A(ub, "VS29", "Vệ sinh máy", d1.AddHours(8), d1.AddHours(8.5));
            P(ub, "M29B", "serial", d1.AddHours(8.5), d1.AddHours(12), 50, 0, "NC02");
            A(ub, "HOP29", "Họp đầu ca", d1.AddHours(11.5), d1.AddHours(12));
            // Ngày 2 — A: M29A 08–12, M29B 13–17; M29B dừng 10:00–11:00 "Kẹt dao" (không có WTS lúc đó)
            P(ua, "M29A", "serial", d2.AddHours(8), d2.AddHours(12), 60, 0, "NC01");
            P(ua, "M29B", "serial", d2.AddHours(13), d2.AddHours(17), 70, 0, "NC02");
            await db.SaveChangesAsync();
            db.ThietBiChangeLogs.Add(new ThietBiChangeLog { ThietBiId = mb.ThietBiId, FieldName = "TrangThai", OldValue = "Hoạt động", NewValue = "Hỏng", Reason = "Kẹt dao",
                ThoiGianBatDau = d2.AddHours(10), ThoiGianKetThuc = d2.AddHours(11), SoPhutDung = 60, ChangedAt = d2.AddHours(10), ChangedBy = ub.UserId, ChangedByUser = ub });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { ok = true, monday = Monday.ToString("yyyy-MM-dd"), ca = db.CaLamViecs.List.First(c => c.TenCa == "Ca G29").CaId };
    }

    /// <summary>[G31-SCEN 22/11] POST /harness/g31-seed — thêm người g31k (nhóm KCS) có 1 việc HOP29 thứ Hai 11:00–13:00 (nghỉ trưa đã lưu, ca G29), để drive_g29 R7 kiểm ô Nhóm
    ///   ([G31-LOC-NHOM]) loại người nhóm khác ra mà vẫn giữ giờ chuẩn. Gọi SAU các bước R / M (không đổi đáp án của chúng).</summary>
    public static async Task<object> SeedG31Async(AppDbContext db)
    {
        if (!db.Users.List.Any(u => u.Username == "g31k"))
        {
            var gK = db.UserGroups.List.First(g => g.GroupCode == "KCS");
            var u = new User { Username = "g31k", FullName = "Công nhân K31", PasswordHash = PasswordHasher.Hash("123456"), GroupId = gK.GroupId, IsActive = true };
            db.Users.AddRange(u); await db.SaveChangesAsync();
            // việc 11:00–13:00 chạm nghỉ trưa ca G29 (khoảng nghỉ ĐÃ LƯU mang CaId) → ngày có ca: làm 60, kế hoạch 480, chưa khai báo 420
            db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 990301, WorkerId = u.UserId, WorkDate = DateOnly.FromDateTime(Monday), StartTime = Monday.AddHours(11),
                EndTime = Monday.AddHours(13), WtsCode = "HOP29", WtsName = "Họp đầu ca", CreatedAt = Monday.AddHours(11) });
            var ca = db.CaLamViecs.List.First(c => c.TenCa == "Ca G29");
            var ng = db.CaLamViec_NghiGiaiLaos.List.First(n => n.CaId == ca.CaId);
            db.WtsBreakWorkLogs.List.Add(new WtsBreakWorkLog { Id = 990401, ActivityLogId = 990301, CaId = ca.CaId, NghiId = ng.NghiId, BreakName = "Nghỉ trưa",
                BreakStart = Monday.AddHours(12), BreakEnd = Monday.AddHours(13), OverlapMin = 60, WorkedMin = 0 });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { ok = true };
    }
}
