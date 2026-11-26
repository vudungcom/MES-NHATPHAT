// [W1N-SCEN 26/11/2026] HarnessW1N — dữ liệu cho drive_w1n.py (Gói WTS-1NGUON). Ghi THẲNG bảng (như dữ liệu đã lưu).
//   POST /harness/w1n-seed → người w1na "Công nhân W1N" (nhóm GC, mật khẩu 123456); mã GC1N (có ích, CHƯA chọn làm mã Gia công), CHO1N (vô ích);
//   thứ Hai tuần này: dòng Gia công 08:00–10:00 (không ca) có phát sinh CHO1N 20 phút + phát sinh KHÔNG mã 10 phút; việc không ra SP mã lạ LA1N 10:00–11:00.
//   Đáp án tính tay ở đầu drive_w1n.py.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessW1N
{
    public static DateTime Monday => DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        if (!db.Users.List.Any(u => u.Username == "w1na"))
        {
            var admin = db.Users.List.First(u => u.Username == "admin");
            var gGc = db.UserGroups.List.First(g => g.GroupCode == "GC");
            var u = new User { Username = "w1na", FullName = "Công nhân W1N", PasswordHash = PasswordHasher.Hash("123456"), GroupId = gGc.GroupId, IsActive = true };
            db.Users.AddRange(u);
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "GIA_CONG", CategoryName = "Gia công", TaskCode = "GC1N", TaskName = "Gia công chung W1N", IsActive = true, IsProductiveTask = true, CreatedBy = admin.UserId });
            db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "CHUNG", CategoryName = "Chung", TaskCode = "CHO1N", TaskName = "Chờ vật tư W1N", IsActive = true, IsProductiveTask = false, CreatedBy = admin.UserId });
            await db.SaveChangesAsync();
            var det = db.KhPlanDetails.List.First();
            var d1 = Monday;
            var w = new WtsProductionLog { WtsLogId = 991100, KhPlanDetailId = det.KhPlanDetailId, ProcessGroup = "GC", NC = "NC1", MachineUsed = "M1", ProductionMode = "serial",
                WorkerId = u.UserId, QtyDone = 1, QtyOk = 1, QtyNg = 0, StartTime = d1.AddHours(8), EndTime = d1.AddHours(10), ExtraMinutes = 30, CreatedAt = d1.AddHours(8) };
            db.WtsProductionLogs.List.Add(w);
            db.WtsExtraItems.List.Add(new WtsExtraItemLog { Id = 991101, WtsLogId = w.WtsLogId, WtsCode = "CHO1N", WtsName = "Chờ vật tư W1N", Minutes = 20, CreatedAt = d1 });
            db.WtsExtraItems.List.Add(new WtsExtraItemLog { Id = 991102, WtsLogId = w.WtsLogId, WtsCode = null, WtsName = null, Minutes = 10, CreatedAt = d1 });
            db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 991200, WorkerId = u.UserId, WorkDate = DateOnly.FromDateTime(d1), StartTime = d1.AddHours(10), EndTime = d1.AddHours(11),
                WtsCode = "LA1N", WtsName = "Mã lạ", CreatedAt = d1.AddHours(10) });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { ok = true, monday = Monday.ToString("yyyy-MM-dd") };
    }

    /// <summary>POST /harness/w1n-codes?c=K2,B9 — khai mã CV vào WTS tiêu chuẩn (Part Master nay chỉ nhận mã có trong danh mục khi lưu mới / đổi mã).</summary>
    public static async Task<object> CodesAsync(AppDbContext db, string? c)
    {
        var admin = db.Users.List.First(u => u.Username == "admin");
        foreach (var code in (c ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == code))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "KCS", CategoryName = "KCS", TaskCode = code, TaskName = "Việc " + code, IsActive = true, CreatedBy = admin.UserId });
        await db.SaveChangesAsync(); db.Fixup();
        return new { ok = true };
    }
}
