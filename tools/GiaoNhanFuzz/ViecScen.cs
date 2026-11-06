// [ML16-ACT][TOOLS-PUSH 2026-11-01] Kịch bản cố định `viec`: "Việc không ra sản phẩm" (WorkerActivityLogs) qua HÀM THẬT
//   WtsSaveService.SaveActivityAsync / VoidRowAsync. Dựng lại ca kịch bản viec của gói ML2 (30/10) — bản cũ ở zip sandbox, chưa lên master.
//   Luật (Sổ luật · ML16-ACT): chỉ hôm nay · giờ tròn phút · mã CV phải có trong WTS tiêu chuẩn + thuộc nhóm WTS của người nhập (Admin bỏ qua) ·
//   PO + vụ lỗi tuỳ chọn, vụ phải thuộc PO · chặn chồng giờ với WTS / việc khác của CHÍNH người đó (chạm mép không tính) ·
//   sửa = huỷ dòng cũ + dòng mới (append-only, mục phát sinh chép sang) · chỉ sửa / xoá dòng của mình, hôm nay · KHÔNG đổi sổ số lượng PO.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class ViecScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null && !cond ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static void Eq(string? got, string expect, string name) => Ok(got == expect, name, $"có:  {got}\n           cần: {expect}");

    static readonly DateTime D = DateTime.Today;
    const int UGc = 7;   // người nhập có quyền WTS Gia công (không Admin)

    /// <summary>Seed của kịch bản wts (PO1 Workflow ON 40 chiếc đã vào GC) + mã CV "VS" (nhóm CHUNG → GC), "XX" (nhóm KHAC, không ánh xạ),
    /// "NG0" đã ngưng; nhóm quyền WTSGC (Xem + Sửa "Nhập WTS: Gia công") cho u7.</summary>
    static async Task<AppDbContext> SeedAsync(int po1 = 40)
    {
        var db = await WtsScen.SeedAsync(po1);
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 1, CategoryCode = "CHUNG", CategoryName = "Việc chung", TaskCode = "VS", TaskName = "Vệ sinh máy", IsActive = true });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 2, CategoryCode = "KHAC", CategoryName = "Khác", TaskCode = "XX", TaskName = "Việc nhóm khác", IsActive = true });
        db.StandardWtsTasks.List.Add(new StandardWtsTask { TaskId = 3, CategoryCode = "CHUNG", CategoryName = "Việc chung", TaskCode = "NG0", TaskName = "Mã đã ngưng", IsActive = false });
        db.WtsCategoryGroupMappings.List.Add(new WtsCategoryGroupMapping { MappingId = 1, CategoryCode = "CHUNG", ProcessGroup = "GC" });
        var g = new UserGroup { GroupId = 77, GroupCode = "WTSGC", GroupName = "WTS Gia công (thử)", IsActive = true, Permissions = FuzzPerm.Json(MES.Web.Constants.SystemPermissions.WtsGc) };
        db.UserGroups.List.Add(g);
        db.Users.List.Add(new User { UserId = UGc, Username = "u7", FullName = "U7", IsActive = true, Group = g, GroupId = 77 });
        db.Users.List.Add(new User { UserId = 8, Username = "u8", FullName = "U8", IsActive = true, Group = g, GroupId = 77 });
        db.Fixup();
        return db;
    }

    static WtsSaveService.WtsActivityRequest Req(int user, string? code, DateTime s, DateTime e, int? po = null, long? vu = null, string? notes = null, long? edit = null)
        => new() { UserId = user, WtsCode = code, StartTime = s, EndTime = e, RefKhPlanDetailId = po, NgCaseId = vu, Notes = notes, ActivityLogId = edit, CaId = 1 };

    static int Acts(AppDbContext db, bool active = true) => db.WorkerActivityLogs.List.Count(a => !active || !a.IsVoided);
    static async Task<string> SoPo(AppDbContext db, int id) => System.Text.Json.JsonSerializer.Serialize(await new HandoverLedgerService(db).GetLedgerForDetailAsync(id));

    public static async Task<int> Run()
    {
        Console.WriteLine("[X1] Kiểm đầu vào — từng lời báo, không ghi dòng nào");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db); int n0 = Acts(db, false);
            Eq((await s.SaveActivityAsync(Req(UGc, " ", D.AddHours(9), D.AddHours(10)))).ErrorMsg, "Chưa chọn Mã CV.", "Mã CV trống");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddDays(-1).AddHours(9), D.AddDays(-1).AddHours(10)))).ErrorMsg, "Việc không ra sản phẩm chỉ nhập cho ngày hôm nay.", "Ngày hôm qua");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(10), D.AddHours(10)))).ErrorMsg, "T.Kết thúc phải sau T.Bắt đầu.", "Kết thúc = bắt đầu");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(23), D.AddDays(1).AddHours(1)))).ErrorMsg, "T.Kết thúc phải trong ngày hôm nay.", "Kết thúc qua ngày mai");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(9), D.AddHours(10), notes: new string('a', WtsSaveService.ActivityNotesMax + 1)))).ErrorMsg,
               $"Ghi chú dài quá {WtsSaveService.ActivityNotesMax} ký tự.", "Ghi chú quá dài");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(9), D.AddHours(10), vu: 1))).ErrorMsg, $"Chọn PO trước khi chọn {NgCaseNo.Term}.", "Chọn vụ mà chưa chọn PO");
            Eq((await s.SaveActivityAsync(Req(UGc, "ZZZ", D.AddHours(9), D.AddHours(10)))).ErrorMsg, "Mã CV ZZZ không có trong WTS tiêu chuẩn hoặc đã ngưng dùng.", "Mã không có");
            Eq((await s.SaveActivityAsync(Req(UGc, "NG0", D.AddHours(9), D.AddHours(10)))).ErrorMsg, "Mã CV NG0 không có trong WTS tiêu chuẩn hoặc đã ngưng dùng.", "Mã đã ngưng");
            Eq((await s.SaveActivityAsync(Req(UGc, "XX", D.AddHours(9), D.AddHours(10)))).ErrorMsg,
               "Mã CV XX không thuộc nhóm WTS của bạn (WTS tiêu chuẩn → Cấu hình nhóm) — nhờ Admin thêm.", "Mã thuộc nhóm WTS người nhập không có");
            Eq((await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(9), D.AddHours(10), po: 999))).ErrorMsg, "PO đã chọn không còn trong Kế hoạch — chọn lại.", "PO không còn");
            Eq((await s.SaveActivityAsync(Req(99, "VS", D.AddHours(9), D.AddHours(10)))).ErrorMsg, "Tài khoản không hoạt động — đăng nhập lại.", "Tài khoản không có");
            Ok(Acts(db, false) == n0 && db.WtsBreakWorkLogs.List.Count == 0, "Không dòng việc / dòng nghỉ nào được ghi");
        }

        Console.WriteLine("[X2] Lưu được: dòng đúng, giờ tròn phút, mã chuẩn hoá, PO + vụ chỉ là nhãn — sổ số lượng PO giống hệt trước / sau");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db);
            // vụ lỗi của PO1: 1 dòng WTS NG (hàm thật) → NgCases
            var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 4, 1, D.AddHours(7), D.AddHours(8), cat: 1)); var rg = await s.SaveGroupAsync(g); db.Fixup();
            var vu = db.NgCases.List.Single(c => c.KhPlanDetailId == 1).NgCaseId;
            var so0 = await SoPo(db, 1); var so2 = await SoPo(db, 2);
            int case0 = db.NgCases.List.Count, tx0 = db.HandoverTransactions.List.Count, wts0 = db.WtsProductionLogs.List.Count;
            var r = await s.SaveActivityAsync(Req(UGc, " vs ", D.AddHours(9).AddSeconds(40), D.AddHours(10).AddSeconds(10), po: 1, vu: vu, notes: "  sửa đồ gá  "));
            db.Fixup();
            var a = db.WorkerActivityLogs.List.LastOrDefault();
            Ok(rg.Ok && r.Ok && a != null && a.WorkerId == UGc && a.WtsCode == "VS" && a.WtsName == "Vệ sinh máy" && a.Notes == "sửa đồ gá"
               && a.StartTime == D.AddHours(9).AddMinutes(1) && a.EndTime == D.AddHours(10) && a.WorkDate == DateOnly.FromDateTime(D)
               && a.RefKhPlanDetailId == 1 && a.NgCaseId == vu && !a.IsVoided,
               "1 dòng: người, mã 'VS' (gõ ' vs '), tên, ghi chú cắt trắng, 09:00:40 → 09:01, 10:00:10 → 10:00, PO + vụ", rg.ErrorMsg + " | " + r.ErrorMsg);
            Ok(await SoPo(db, 1) == so0 && await SoPo(db, 2) == so2, "Sổ số lượng PO1 + PO2 không đổi (việc không ra SP không phải chứng từ số lượng)");
            Ok(db.NgCases.List.Count == case0 && db.HandoverTransactions.List.Count == tx0 && db.WtsProductionLogs.List.Count == wts0, "Không sinh vụ lỗi / phiếu / dòng WTS nào");
            // vụ của PO khác → chặn
            var bad = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(11), D.AddHours(12), po: 2, vu: vu));
            Eq(bad.ErrorMsg, $"{NgCaseNo.TermCap} {NgCaseNo.Format(vu)} không thuộc PO đã chọn — chọn lại.", "Vụ lỗi không thuộc PO đã chọn: chặn");
            // Admin không cần ánh xạ nhóm
            var ad = await s.SaveActivityAsync(Req(1, "XX", D.AddHours(11), D.AddHours(12)));
            Ok(ad.Ok, "Admin lưu mã nhóm KHAC (không ánh xạ) được", ad.ErrorMsg);
        }

        Console.WriteLine("[X3] Trùng giờ chỉ xét CHÍNH người nhập (WTS sản xuất + việc khác), chạm mép không tính");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db);
            // u2 có WTS sản xuất 08:00–09:00 (NC1, PO1)
            var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 2, 0, D.AddHours(8), D.AddHours(9))); var rg = await s.SaveGroupAsync(g);
            // u2 thuộc nhóm XUONG (không có quyền WTS) → cho u2 vào nhóm WTSGC để kiểm trùng giờ (không đổi luật quyền)
            db.Users.List.First(u => u.UserId == 2).GroupId = 77; db.Users.List.First(u => u.UserId == 2).Group = db.UserGroups.List.First(x => x.GroupId == 77); db.Fixup();
            var r1 = await s.SaveActivityAsync(Req(2, "VS", D.AddHours(8).AddMinutes(30), D.AddHours(9).AddMinutes(30)));
            Ok(!r1.Ok && r1.ErrorMsg == "Trùng giờ với dòng WTS của bạn 08:00–09:00 (PO PO1 · NC1) — sửa giờ cho khỏi chồng nhau.", "Chồng WTS sản xuất của chính mình: chặn, nêu PO + NC", rg.ErrorMsg + " | " + r1.ErrorMsg);
            var r2 = await s.SaveActivityAsync(Req(2, "VS", D.AddHours(9), D.AddHours(10)));
            Ok(r2.Ok, "Chạm mép 09:00 (bắt đầu = kết thúc WTS): được", r2.ErrorMsg);
            var r3 = await s.SaveActivityAsync(Req(2, "VS", D.AddHours(9).AddMinutes(45), D.AddHours(10).AddMinutes(30)));
            Eq(r3.ErrorMsg, "Trùng giờ với việc VS 09:00–10:00 đã lưu — sửa giờ cho khỏi chồng nhau.", "Chồng việc khác của chính mình: chặn");
            var r4 = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(8).AddMinutes(30), D.AddHours(9).AddMinutes(30)));
            Ok(r4.Ok, "Người khác cùng giờ: được (trùng giờ chỉ xét chính người nhập)", r4.ErrorMsg);
            var vv = db.WorkerActivityLogs.List.First(a => a.WorkerId == 2 && !a.IsVoided);
            var vd = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = vv.ActivityLogId, UserId = 2 });
            var r5 = await s.SaveActivityAsync(Req(2, "VS", D.AddHours(9).AddMinutes(45), D.AddHours(10).AddMinutes(30)));
            Ok(vd.Ok && r5.Ok, "Dòng đã xoá (IsVoided) không tính trùng giờ", vd.ErrorMsg + " | " + r5.ErrorMsg);
        }

        Console.WriteLine("[X4] Sửa = huỷ dòng cũ + dòng mới (append-only); chỉ dòng của mình, hôm nay, chưa bị huỷ; mục phát sinh chép sang");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db);
            var r0 = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(13), D.AddHours(14), notes: "a")); db.Fixup();
            var old = db.WorkerActivityLogs.List.Last();
            db.WtsExtraItems.List.Add(new WtsExtraItemLog { Id = 501, ActivityLogId = old.ActivityLogId, WtsCode = "PS", WtsName = "Phát sinh", Minutes = 5, CreatedAt = D });
            db.Fixup();
            int n0 = Acts(db, false);
            // sửa giờ đè lên chính dòng đang sửa: không tính trùng với chính nó
            var r1 = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(13).AddMinutes(30), D.AddHours(14).AddMinutes(30), notes: "b", edit: old.ActivityLogId)); db.Fixup();
            var neu = db.WorkerActivityLogs.List.Last();
            Ok(r0.Ok && r1.Ok && old.IsVoided && old.VoidReason == "Sửa lại" && old.VoidedBy == UGc && Acts(db, false) == n0 + 1 && Acts(db) == 1
               && neu.ActivityLogId != old.ActivityLogId && neu.Notes == "b" && neu.StartTime == D.AddHours(13).AddMinutes(30),
               "Dòng cũ huỷ ('Sửa lại', người sửa), dòng mới ghi thêm — không UPDATE nội dung dòng cũ", r0.ErrorMsg + " | " + r1.ErrorMsg);
            Ok(db.WtsExtraItems.List.Any(e => e.ActivityLogId == neu.ActivityLogId && e.WtsCode == "PS" && e.Minutes == 5)
               && db.WtsExtraItems.List.Any(e => e.Id == 501 && e.ActivityLogId == old.ActivityLogId), "Mục phát sinh chép sang dòng mới, dòng cũ giữ nguyên");
            var r2 = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(15), D.AddHours(16), edit: old.ActivityLogId));
            Eq(r2.ErrorMsg, "Dòng này đã bị sửa / xoá ở nơi khác (tab khác) — tải lại trang rồi sửa lại.", "Sửa lại dòng đã huỷ (tab cũ): chặn");
            var r3 = await s.SaveActivityAsync(Req(8, "VS", D.AddHours(15), D.AddHours(16), edit: neu.ActivityLogId));
            Eq(r3.ErrorMsg, "Không tìm thấy dòng cần sửa của bạn — tải lại trang.", "Sửa dòng của người khác: chặn");
            db.WorkerActivityLogs.List.Add(new WorkerActivityLog { ActivityLogId = 900, WorkerId = UGc, WorkDate = DateOnly.FromDateTime(D.AddDays(-1)), WtsCode = "VS", WtsName = "Vệ sinh máy",
                                                                   StartTime = D.AddDays(-1).AddHours(9), EndTime = D.AddDays(-1).AddHours(10), CreatedAt = D.AddDays(-1) });
            db.Fixup();
            var r4 = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(15), D.AddHours(16), edit: 900));
            Eq(r4.ErrorMsg, "Chỉ sửa được dòng của ngày hôm nay.", "Sửa dòng hôm qua: chặn");
            Ok(Acts(db, false) == n0 + 2 && db.WorkerActivityLogs.List.Count(a => a.ActivityLogId == 900 && !a.IsVoided) == 1, "3 lần bị chặn không ghi / huỷ gì");
        }

        Console.WriteLine("[X5] Ngẫu nhiên 300 lượt (lưu / sửa / xoá, 3 người): mỗi người không có 2 khoảng hiệu lực chồng nhau; sổ PO không đổi; dòng huỷ không mất");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db);
            foreach (var u in db.Users.List.Where(u => u.UserId == 2 || u.UserId == 3)) { u.GroupId = 77; u.Group = db.UserGroups.List.First(x => x.GroupId == 77); }
            db.Fixup();
            var so0 = await SoPo(db, 1);
            var rnd = new Random(1101); int okN = 0, blockN = 0; string? bad = null; int everIds = 0;
            for (int i = 0; i < 300 && bad == null; i++)
            {
                int u = new[] { 2, 3, UGc }[rnd.Next(3)];
                var st = D.AddHours(6).AddMinutes(rnd.Next(0, 14 * 60) / 5 * 5); var en = st.AddMinutes(5 + rnd.Next(0, 24) * 5);
                if (en > D.AddDays(1)) en = D.AddDays(1);
                var mine = db.WorkerActivityLogs.List.Where(a => a.WorkerId == u && !a.IsVoided).ToList();
                int op = rnd.Next(10);
                WtsSaveService.WtsSaveResult r;
                if (op < 2 && mine.Any()) r = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = mine[rnd.Next(mine.Count)].ActivityLogId, UserId = u });
                else if (op < 5 && mine.Any()) r = await s.SaveActivityAsync(Req(u, "VS", st, en, edit: mine[rnd.Next(mine.Count)].ActivityLogId));
                else r = await s.SaveActivityAsync(Req(u, "VS", st, en, po: rnd.Next(2) == 0 ? 1 : null));
                db.Fixup();
                if (r.Ok) okN++; else blockN++;
                foreach (var grp in db.WorkerActivityLogs.List.Where(a => !a.IsVoided).GroupBy(a => a.WorkerId))
                {
                    var L = grp.OrderBy(a => a.StartTime).ToList();
                    for (int k = 1; k < L.Count; k++)
                        if (L[k].StartTime < L[k - 1].EndTime) bad = $"lượt {i}: người {grp.Key} có 2 khoảng chồng {L[k - 1].StartTime:HH:mm}–{L[k - 1].EndTime:HH:mm} / {L[k].StartTime:HH:mm}–{L[k].EndTime:HH:mm}";
                }
                if (db.WorkerActivityLogs.List.Count < everIds) bad = $"lượt {i}: số dòng giảm (mất dòng đã huỷ)";
                everIds = db.WorkerActivityLogs.List.Count;
                if (!r.Ok && r.ErrorMsg != null && !(r.ErrorMsg.StartsWith("Trùng giờ") || r.ErrorMsg.StartsWith("T.Kết thúc"))) bad = $"lượt {i}: lời báo lạ: {r.ErrorMsg}";
            }
            Ok(bad == null && okN > 50 && blockN > 10, $"300 lượt: {okN} lưu được, {blockN} bị chặn đúng luật, không người nào chồng giờ", bad);
            Ok(await SoPo(db, 1) == so0, "Sổ số lượng PO1 giống hệt trước khi chạy");
        }

        // [ML16-DOCS 2026-11-02] PO chỉ có giờ sửa (dòng "Việc không ra sản phẩm" ghi PO đó, kể cả đã huỷ) KHÔNG còn là PO trắng:
        //   WorkerActivityLogs.RefKhPlanDetailId có khoá ngoại → Xoá / Tách PO phải chặn trước (không để SQL báo lỗi giữa chừng).
        Console.WriteLine("[X6] ML16-DOCS — PO chỉ có giờ sửa (việc không ra SP, kể cả đã huỷ): Xoá / Tách PO bị chặn");
        {
            var db = await SeedAsync(); var s = WtsScen.Svc(db); var kh = new KhPlanService(db);
            var plan = db.KhPlans.List.First(p => p.KhPlanId == 1);
            var d3 = new KhPlanDetail { KhPlanDetailId = 3, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO3", Quantity = 10, Unit = "EA", LineNo = 2 };
            db.KhPlanDetails.List.Add(d3); plan.Details.Add(d3); db.Fixup();
            var c0 = await PoGuard.CountDocsAsync(db, 3);
            Ok(!c0.HasAny, "PO3 mới (không phôi / WTS / phiếu / báo lỗi / giờ sửa): PO trắng", c0.Text());
            var r = await s.SaveActivityAsync(Req(UGc, "VS", D.AddHours(9), D.AddHours(10), po: 3, notes: "sua do ga PO3"));
            db.Fixup();
            var c1 = await PoGuard.CountDocsAsync(db, 3);
            Ok(r.Ok && c1.GioSuaAll == 1 && c1.HasAny && c1.Text() == "1 dòng việc không ra sản phẩm (giờ sửa) ghi PO này",
               "Lưu 1 dòng việc không ra SP ghi PO3 → PoGuard đếm 1 dòng giờ sửa, HasAny", (r.ErrorMsg ?? "") + " · " + c1.Text());
            EfRaw().Clear();
            var del = await kh.DeleteDetailAsync(3, 1, "tao nham");
            Ok(!del.Success && del.Error!.Contains("1 dòng việc không ra sản phẩm (giờ sửa) ghi PO này") && db.KhPlanDetails.List.Any(x => x.KhPlanDetailId == 3) && EfRaw().Count == 0,
               "Xoá PO3: chặn, lời báo nêu dòng giờ sửa, không chạy lệnh xoá nào", del.Error);
            var act = db.WorkerActivityLogs.List.Single(a => a.RefKhPlanDetailId == 3);
            var vd = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = act.ActivityLogId, UserId = UGc }); db.Fixup();
            var c2 = await PoGuard.CountDocsAsync(db, 3);
            del = await kh.DeleteDetailAsync(3, 1, "tao nham");
            Ok(vd.Ok && act.IsVoided && c2.GioSuaAll == 1 && !del.Success && del.Error!.Contains("giờ sửa"), "Huỷ dòng giờ sửa → vẫn đếm (lịch sử, khoá ngoại) → Xoá PO3 vẫn chặn", (vd.ErrorMsg ?? "") + " · " + del.Error);
            string? err = null; try { await kh.SplitPOAsync(3, new() { new() { PurchaseOrder = "PO3A", Quantity = 5 }, new() { PurchaseOrder = "PO3B", Quantity = 5 } }, 1, "tach"); } catch (Exception ex) { err = ex.Message; }
            Ok(err != null && err.Contains("không tách được") && err.Contains("giờ sửa") && !db.KhPlanDetails.List.Any(x => x.PurchaseOrder == "PO3A"), "Tách PO3: chặn, không sinh PO mới", err);
        }

        // [ML16-Q 2026-11-02] Hùng duyệt: chiều ngược lại — lưu WTS sản xuất chồng giờ "Việc không ra SP" đã lưu của CHÍNH người đó → chặn;
        //   khoá WTS_WORKER_ đầu tiên (thứ tự người → máy → PO); WTS chồng WTS (GC nhiều máy) vẫn được.
        Console.WriteLine("[X7] ML16-Q — lưu WTS sản xuất chồng giờ việc không ra SP của chính mình: chặn; chạm mép / người khác / việc đã huỷ: được; khoá người → máy → PO");
        {
            var db = await SeedAsync(1000); var s = WtsScen.Svc(db);   // [B3-SO-NGUYEN 06/11] PO1 1000: phần trộn ngẫu nhiên ghi WTS 1 chiếc / dòng (trước 0.01)
            db.Users.List.First(u => u.UserId == 2).GroupId = 77; db.Users.List.First(u => u.UserId == 2).Group = db.UserGroups.List.First(x => x.GroupId == 77); db.Fixup();
            var a1 = await s.SaveActivityAsync(Req(2, "VS", D.AddHours(10), D.AddHours(11)));
            int w0 = db.WtsProductionLogs.List.Count;
            var g = WtsScen.Gc(); g.Rows.Add(WtsScen.Row(1, 2, 0, D.AddHours(10).AddMinutes(30), D.AddHours(11).AddMinutes(30)));
            var r1 = await s.SaveGroupAsync(g);
            Ok(a1.Ok && !r1.Ok && r1.ErrorMsg == "Trùng giờ với việc không ra sản phẩm VS 10:00–11:00 đã lưu — sửa giờ, hoặc sửa / xoá dòng việc đó." && db.WtsProductionLogs.List.Count == w0,
               "WTS 10:30–11:30 chồng việc VS 10:00–11:00 của chính mình: chặn, không ghi dòng nào", a1.ErrorMsg + " | " + r1.ErrorMsg);
            var g2 = WtsScen.Gc(); g2.Rows.Add(WtsScen.Row(1, 2, 0, D.AddHours(11), D.AddHours(12)));
            Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.Clear();
            var r2 = await s.SaveGroupAsync(g2);
            var ln = Microsoft.EntityFrameworkCore.DatabaseFacade.LockNames.ToList();
            Ok(r2.Ok, "Chạm mép 11:00 (bắt đầu WTS = kết thúc việc): lưu được", r2.ErrorMsg);
            int iw = ln.IndexOf("WTS_WORKER_2"), im = ln.IndexOf("WTS_MACHINE_M1"), ip = ln.IndexOf("MES_PO_1");
            Ok(iw >= 0 && im > iw && ip > im, "Thứ tự khoá lúc lưu WTS: WTS_WORKER_2 → WTS_MACHINE_M1 → MES_PO_1", string.Join(",", ln));
            var g3 = WtsScen.Gc(machine: "M2"); g3.UserId = UGc; g3.Rows.Add(WtsScen.Row(1, 2, 0, D.AddHours(10).AddMinutes(30), D.AddHours(10).AddMinutes(50)));
            var r3 = await s.SaveGroupAsync(g3);
            Ok(r3.Ok, "Người khác (u7) cùng giờ việc của u2: lưu được (chỉ xét chính người nhập)", r3.ErrorMsg);
            var w11 = db.WtsProductionLogs.List.First(w => w.WorkerId == 2 && !w.IsVoided);
            var g4 = WtsScen.Gc(); var e4 = WtsScen.Row(1, 2, 0, D.AddHours(10).AddMinutes(15), D.AddHours(11)); e4.IsEditing = true; e4.WtsLogId = w11.WtsLogId; g4.Rows.Add(e4);
            var r4 = await s.SaveGroupAsync(g4); db.Fixup();
            Ok(!r4.Ok && r4.ErrorMsg!.StartsWith("Trùng giờ với việc không ra sản phẩm VS 10:00–11:00") && !w11.IsVoided,
               "Sửa dòng WTS 11:00–12:00 thành 10:15–11:00 (đè lên việc): chặn, dòng cũ KHÔNG bị huỷ", r4.ErrorMsg);
            var vv = db.WorkerActivityLogs.List.First(a => a.WorkerId == 2 && !a.IsVoided);
            var vd = await s.VoidRowAsync(new WtsSaveService.WtsVoidRequest { ActivityLogId = vv.ActivityLogId, UserId = 2 }); db.Fixup();
            var g5 = WtsScen.Gc(); g5.Rows.Add(WtsScen.Row(1, 2, 0, D.AddHours(10).AddMinutes(30), D.AddHours(10).AddMinutes(55)));
            var r5 = await s.SaveGroupAsync(g5);
            Ok(vd.Ok && r5.Ok, "Việc đã xoá (IsVoided) không tính trùng: WTS 10:30–10:55 lưu được", vd.ErrorMsg + " | " + r5.ErrorMsg);
            var g6 = WtsScen.Gc(machine: "M3"); g6.Rows.Add(WtsScen.Row(1, 1, 0, D.AddHours(10).AddMinutes(30), D.AddHours(10).AddMinutes(55)));
            var r6 = await s.SaveGroupAsync(g6);
            Ok(r6.Ok, "WTS chồng WTS của chính mình (GC đứng 2 máy M1 + M3): vẫn lưu được như cũ", r6.ErrorMsg);
            // ngẫu nhiên: u2 trộn lưu WTS (máy ngẫu nhiên) + việc không ra SP → không bao giờ có WTS hiệu lực chồng việc hiệu lực
            var rnd = new Random(1102); string? bad = null; int okW = 0, okA = 0, blk = 0;
            for (int i = 0; i < 200 && bad == null; i++)
            {
                var st = D.AddHours(6).AddMinutes(rnd.Next(0, 14 * 60) / 5 * 5); var en = st.AddMinutes(5 + rnd.Next(0, 12) * 5);
                if (en > D.AddDays(1)) en = D.AddDays(1);
                WtsSaveService.WtsSaveResult r;
                if (rnd.Next(2) == 0) { var gg = WtsScen.Gc(machine: "MR" + rnd.Next(1, 40)); gg.Rows.Add(WtsScen.Row(1, 1m, 0, st, en)); r = await s.SaveGroupAsync(gg); if (r.Ok) okW++; }
                else { r = await s.SaveActivityAsync(Req(2, "VS", st, en)); if (r.Ok) okA++; }
                if (!r.Ok) blk++;
                db.Fixup();
                var W = db.WtsProductionLogs.List.Where(w => w.WorkerId == 2 && !w.IsVoided).ToList();
                var A = db.WorkerActivityLogs.List.Where(a => a.WorkerId == 2 && !a.IsVoided).ToList();
                foreach (var a in A) foreach (var w in W)
                    if (w.StartTime < a.EndTime && w.EndTime > a.StartTime) bad = $"lượt {i}: WTS {w.StartTime:HH:mm}–{w.EndTime:HH:mm} chồng việc {a.StartTime:HH:mm}–{a.EndTime:HH:mm}";
            }
            Ok(bad == null && okW > 10 && okA >= 5 && blk > 10, $"200 lượt trộn: {okW} WTS + {okA} việc lưu được, {blk} bị chặn; không WTS nào chồng việc", bad);
        }

        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
    static List<string> EfRaw() => Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog;   // [ML16-DOCS] lệnh SQL thô đã chạy (EfStub)
}
