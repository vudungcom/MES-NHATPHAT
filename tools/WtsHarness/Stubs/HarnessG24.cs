// [GÓI 24 16/11/2026] HarnessG24 — dữ liệu + điểm gọi cho drive_g24.py (PIN hash / throttle, đăng xuất POST, /uploads đăng nhập, Admin đặt lại mật khẩu,
//   Tạo KH Part chưa OK / ngày trống, Kho ngày nhận, sổ sự cố máy).
//   POST /harness/g24-seed              → user g24pin (nhóm ADMIN, mật khẩu 123456, PIN CHỮ RÕ "1234" như dữ liệu cũ) + user g24kho (cũng ADMIN, PIN "2222" — đích đặt lại mật khẩu / khoá PIN);
//                                          Part P-RONG24 (không công đoạn), Part P-NHAP24 (1 NC, mục B chưa Xác nhận OK); máy C24 Báo hỏng có 2 dòng sự cố mở (dữ liệu cũ).
//   POST /harness/g24-pin?user=&pin=    → AuthService.VerifyPinAsync (DI thật) → { ok, error, pinHashed } — [PIN-HASH][PIN-THROTTLE]; bản cũ: không khoá, PIN không hash → SAI là đúng.
//   POST /harness/g24-pin-reset         → PinThrottle.ResetAll() (bản cũ không có lớp này → trả { ok = false }).
//   POST /harness/g24-checkpw?user=&pw= → PasswordHasher.Verify mật khẩu đăng nhập — [ADMIN-RESET-MK].
//   POST /harness/g24-tb?status=&name=  → ThietBiService.UpdateAsync máy C24 bằng g24pin (PIN 1234) → { ok, error, open } (số dòng sự cố mở) — [BM1-01].
//   GET  /harness/g24                   → trạng thái user g24pin / g24kho (pinHashed), máy C24 (trạng thái, số dòng mở), Part.
//   Nguồn: CLAUDE quét Fable 15/11 (A3C P-05 / P-09 / C-05 / C-11, B-K BK-03 / BK-06, B-M1 BM1-01, B-M2 BM2-04, câu hỏi 10 / 11); HÙNG 16/11 11:34 OK Gói 24.
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class HarnessG24
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var hash = PasswordHasher.Hash("123456");
        var adminG = db.UserGroups.List.First(g => g.GroupCode == "ADMIN");
                if (!db.Users.List.Any(x => x.Username == "g24pin"))
        { db.Users.Add(new User { Username = "g24pin", FullName = "G24 PIN", PasswordHash = hash, GroupId = adminG.GroupId, IsActive = true, PIN = "1234" }); await db.SaveChangesAsync(); db.Fixup(); }
        if (!db.Users.List.Any(x => x.Username == "g24kho"))
        { db.Users.Add(new User { Username = "g24kho", FullName = "G24 Kho", PasswordHash = hash, GroupId = adminG.GroupId, IsActive = true, PIN = "2222" }); await db.SaveChangesAsync(); db.Fixup(); }
        var cust = db.Customers.List.First();
        if (!db.PartMasters.List.Any(p => p.PartNo == "P-RONG24"))
        { db.PartMasters.Add(new PartMaster { PartNo = "P-RONG24", PartName = "Part rỗng G24", IsActive = true, CustomerId = cust.CustomerId }); await db.SaveChangesAsync(); db.Fixup(); }
        if (!db.PartMasters.List.Any(p => p.PartNo == "P-NHAP24"))
        {
            var p = new PartMaster { PartNo = "P-NHAP24", PartName = "Part nháp G24", IsActive = true, CustomerId = cust.CustomerId, IsMachiningConfirmed = false };
            db.PartMasters.Add(p); await db.SaveChangesAsync(); db.Fixup();
            db.PartMachiningSteps.Add(new PartMachiningStep { PartId = p.PartId, StepOrder = 1, NC = "NC1", MachineRegistered = "M1", SetupTime = 1, MachiningTime = 1, IsActive = true, CreatedBy = 1, CreatedAt = DateTime.Now });
            await db.SaveChangesAsync(); db.Fixup();
        }
        if (!db.ThietBis.List.Any(t => t.SoMay == "C24"))
        {
            var t0 = DateTime.Now.AddHours(-4);
            var m = new ThietBi { SoMay = "C24", TenMay = "May C24", BoPhan = "GC", LoaiMay = "CNC", MaMay = "C24", TrangThai = "Báo hỏng", IsActive = true, CreatedAt = t0, CreatedBy = 1 };
            db.ThietBis.Add(m); await db.SaveChangesAsync(); db.Fixup();
            db.ThietBiChangeLogs.Add(new ThietBiChangeLog { ThietBiId = m.ThietBiId, FieldName = "Trạng thái", OldValue = "Hoạt động", NewValue = "Báo hỏng", Reason = "gay dao", ThoiGianBatDau = t0, ChangedAt = t0, ChangedBy = 1 });
            db.ThietBiChangeLogs.Add(new ThietBiChangeLog { ThietBiId = m.ThietBiId, FieldName = "Nhật ký dừng máy", OldValue = "Báo hỏng", NewValue = "Báo hỏng", Reason = "sua ten (ban cu)", ThoiGianBatDau = t0.AddMinutes(30), ChangedAt = t0.AddMinutes(30), ChangedBy = 1 });
            await db.SaveChangesAsync(); db.Fixup();
        }
        return new { ok = true, state = State(db) };
    }

    public static object State(AppDbContext db)
    {
        var u = db.Users.List.FirstOrDefault(x => x.Username == "g24pin"); var k = db.Users.List.FirstOrDefault(x => x.Username == "g24kho");
        var m = db.ThietBis.List.FirstOrDefault(t => t.SoMay == "C24");
        int open = m == null ? -1 : db.ThietBiChangeLogs.List.Count(l => l.ThietBiId == m.ThietBiId && l.ThoiGianBatDau != null && l.ThoiGianKetThuc == null);
        return new
        {
            pinUser = u == null ? null : new { u.UserId, u.Username, pinHashed = (u.PIN ?? "").StartsWith("PBKDF2."), pinLen = (u.PIN ?? "").Length },
            khoUser = k == null ? null : new { k.UserId, k.Username, pinHashed = (k.PIN ?? "").StartsWith("PBKDF2.") },
            may = m == null ? null : new { m.ThietBiId, m.SoMay, m.TrangThai, m.TenMay, open },
            pos = db.KhPlanDetails.List.Where(d => (d.PurchaseOrder ?? "").StartsWith("PO-G24")).Select(d => d.PurchaseOrder).ToList(),
            parts = db.PartMasters.List.Where(p => p.PartNo.EndsWith("24")).Select(p => new { p.PartId, p.PartNo, p.IsMachiningConfirmed, steps = db.PartMachiningSteps.List.Count(s => s.PartId == p.PartId && s.IsActive) }).ToList(),
        };
    }

    public static async Task<object> PinAsync(AppDbContext db, AuthService auth, string user, string pin)
    {
        var u = db.Users.List.First(x => x.Username == user);
        bool ok;
        try { ok = await auth.VerifyPinAsync(u.UserId, pin); }
        catch (Exception ex) { return new { ok = false, error = "EXC " + ex.GetType().Name + ": " + ex.Message, pinHashed = false }; }
        var err = (string?)auth.GetType().GetProperty("LastPinError")?.GetValue(auth);   // bản cũ không có → null
        return new { ok, error = err ?? "", pinHashed = (u.PIN ?? "").StartsWith("PBKDF2.") };
    }

    public static object PinReset()
    {
        var t = Type.GetType("MES.Web.Services.PinThrottle, MES.Web");
        if (t == null) return new { ok = false };
        t.GetMethod("ResetAll")!.Invoke(null, null);
        return new { ok = true };
    }

    public static object CheckPw(AppDbContext db, string user, string pw)
    {
        var u = db.Users.List.FirstOrDefault(x => x.Username == user);
        return new { ok = u != null && PasswordHasher.Verify(pw, u.PasswordHash) };
    }

    public static async Task<object> UpdateTbAsync(AppDbContext db, ThietBiService tb, string status, string? name)
    {
        var u = db.Users.List.First(x => x.Username == "g24pin");
        var m = db.ThietBis.List.First(t => t.SoMay == "C24");
        var model = new ThietBi { ThietBiId = m.ThietBiId, SoMay = m.SoMay, BoPhan = m.BoPhan, MaMay = m.MaMay, LoaiMay = m.LoaiMay, TenMay = name ?? m.TenMay, TrangThai = status };
        var r = await tb.UpdateAsync(model, "drive_g24", null, status == "Hoạt động" ? DateTime.Now : (DateTime?)null, u.UserId, "1234");
        db.Fixup();
        int open = db.ThietBiChangeLogs.List.Count(l => l.ThietBiId == m.ThietBiId && l.ThoiGianBatDau != null && l.ThoiGianKetThuc == null);
        return new { ok = r.Success, error = r.Error ?? "", open, trangThai = db.ThietBis.List.First(t => t.SoMay == "C24").TrangThai };
    }
}
