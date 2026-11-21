// [G28-SCEN 2026-11-20] Kịch bản `g28` — Gói 28 sửa lỗi ngầm (không đổi số / luật xưởng, không SQL):
//   G28-1 [G21-WF-NOTIFY] đổi Workflow báo trang Giao nhận ngay (SetApplyWorkflowAsync + Sửa phiếu); không đổi / bị chặn K7 thì không báo
//   G28-2 [G21-NOTE-TARO] chữ giải thích đoạn Taro làm hộ đầu chuỗi (Kho giao thẳng) / giữa chuỗi
//   G28-3 [LOGIN-TRIM] đăng nhập thử nguyên văn rồi bản bỏ dấu cách đầu / cuối
//   G28-4 [LEN-L16] Máy bấm giờ ≤ 50; Ghi chú vật liệu ≤ 500; LogClip cắt giá trị cũ dài hơn cột lịch sử
//   G28-5 [TB-DOI-LOAI-GIO] đổi loại dừng máy dùng giờ bắt đầu người nhập
//   G28-6 [LOG-TRAN] trần dung lượng file nhật ký / ngày
//   G28-7 [G27-L11-CHU] băng bản quyền "còn chưa tới 1 ngày" thay "còn khoảng 0 ngày"
//   G28-8 [CU-NGHI-LUU] WorkMinutes.SavedSegs / VerifySegs (trang + máy chủ dùng chung)
//   Bản 572cd3b (Gói 26 + 27): G28-1 / 2 / 3 / 4 / 5 / 6 / 7 SAI hoặc không biên dịch (hàm / tham số mới).
//   Nguồn: CLAUDE rà độc lập Gói 21–27 (G21 / G22 / G23 / G24 / G26-27-GHI-NHO); HÙNG 20/11 22:51 OK Gói 28.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public static class G28Scen
{
    static int fails = 0, checks = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        checks++;
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!cond && !string.IsNullOrEmpty(info) ? "\n         → " + info : ""));
        if (!cond) fails++;
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[G28-1] G21-WF-NOTIFY — Kế hoạch đổi Workflow → báo trang Giao nhận tính lại NGAY mọi PO của phiếu; không đổi / bị chặn K7 → không báo");
        {
            var got = new List<int?>();
            void On(int? id) { lock (got) got.Add(id); }
            HandoverChangeNotifier.Changed += On;
            try
            {
                var db = KhScen.Seed(workflow: true); var kh = new KhPlanService(db);
                var ids = db.KhPlanDetails.List.Where(d => d.KhPlanId == 1).Select(d => d.KhPlanDetailId).OrderBy(x => x).ToList();
                var e1 = await kh.SetApplyWorkflowAsync(1, false, 1, "tắt thử");
                Ok(e1 == null && !db.KhPlans.List[0].ApplyWorkflow && ids.All(i => got.Contains(i)), "G28-1a ON → OFF: báo đủ PO của phiếu", $"{e1} | báo {string.Join(",", got)} | PO {string.Join(",", ids)}");
                got.Clear();
                var e2 = await kh.SetApplyWorkflowAsync(1, false, 1, "tắt lại");
                Ok(e2 == null && got.Count == 0, "G28-1b đặt lại đúng trạng thái đang có (OFF → OFF): không báo", string.Join(",", got));
                // bị chặn K7: OFF nhập vượt → bật ON bị chặn → không báo
                var svc = new HandoverService(db);
                await svc.IssueByCellAsync(1, "KHO", "GC", 10, 1, "MOI", false, "MOI");
                await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 10, 0, null, 2);
                db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 999801, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC1", MachineUsed = "M1", WorkerId = 2, QtyDone = 15, QtyOk = 15, StartTime = DateTime.Today.AddHours(8), EndTime = DateTime.Today.AddHours(9), CreatedAt = DateTime.Now });
                db.Fixup(); got.Clear();
                var e3 = await kh.SetApplyWorkflowAsync(1, true, 1, "bật");
                Ok(e3 != null && e3.Contains("K7") && got.Count == 0, "G28-1c bật ON bị chặn K7: không báo (không đổi gì)", $"{e3} | {string.Join(",", got)}");
                db.WtsProductionLogs.List.Last().IsVoided = true; db.Fixup(); got.Clear();
                var e4 = await kh.SetApplyWorkflowAsync(1, true, 1, "bật lại");
                Ok(e4 == null && db.KhPlans.List[0].ApplyWorkflow && ids.All(i => got.Contains(i)), "G28-1d bật ON được: báo đủ PO", $"{e4} | {string.Join(",", got)}");
                // Sửa phiếu (SaveEditAsync) đổi Workflow → báo; chỉ đổi ghi chú dòng → không báo
                var d = db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 1); var p = db.KhPlans.List[0];
                KhPlanService.KhEditValues EV() => new()
                {
                    PlanNotes = p.Notes, ReceivedDate = p.ReceivedDate, ApplyWorkflow = p.ApplyWorkflow, Quantity = d.Quantity, Unit = d.Unit, STD = d.STD, Type = d.Type,
                    Priority = d.Priority, Material = d.Material, MaterialConfig = d.MaterialConfig, Location = d.Location, MaterialNotes = d.MaterialNotes, OrderVL = d.OrderVL,
                    MaterialCondition = d.MaterialCondition, PartNotes = d.PartNotes
                };
                got.Clear();
                var o1 = EV(); var n1 = EV(); n1.PartNotes = "ghi chú mới";
                var s1 = await kh.SaveEditAsync(1, 1, o1, n1, 1, "sửa ghi chú");
                Ok(s1 == null && got.Count == 0, "G28-1e Sửa phiếu chỉ đổi ghi chú dòng: không báo (như cũ)", $"{s1} | {string.Join(",", got)}");
                var o2 = EV(); var n2 = EV(); n2.ApplyWorkflow = false;
                var s2 = await kh.SaveEditAsync(1, 1, o2, n2, 1, "tắt workflow ở Sửa phiếu");
                Ok(s2 == null && !p.ApplyWorkflow && ids.All(i => got.Contains(i)), "G28-1f Sửa phiếu đổi Workflow ON → OFF: báo đủ PO của phiếu", $"{s2} | {string.Join(",", got)}");
            }
            finally { HandoverChangeNotifier.Changed -= On; }
        }

        Console.WriteLine("[G28-2] G21-NOTE-TARO — chữ đoạn Taro làm hộ: đầu chuỗi (NC nguồn @VAO) = Kho giao thẳng; giữa chuỗi = Gia công giao theo NC");
        {
            var row = new LedgerRow();
            row.LamHoRuns.Add(new LedgerLamHoRun { Key = "NC01", Ncs = new() { "NC01" }, FromNc = HandoverLedgerService.VaoCode, NextNc = "NC02", HasTaro = true });
            row.LamHoRuns.Add(new LedgerLamHoRun { Key = "NC04", Ncs = new() { "NC04" }, FromNc = "NC03", NextNc = "NC05", HasTaro = true });
            var a = HandoverLedgerService.LamHoViTri(row, "NC01"); var b = HandoverLedgerService.LamHoViTri(row, "nc04");
            Ok(a.Contains("đầu chuỗi") && a.Contains("Kho giao thẳng") && a.Contains("trả thẳng Kho") && HandoverLedgerService.LamHoNguon(row, "NC01") == "Kho giao thẳng",
               "G28-2a NC01 (đoạn đầu chuỗi, nguồn @VAO): 'máy loại trừ đầu chuỗi … Kho giao thẳng … phôi lỗi trả thẳng Kho'", a);
            Ok(b == "máy loại trừ giữa chuỗi Gia công" && HandoverLedgerService.LamHoNguon(row, "NC04") == "Gia công giao theo NC", "G28-2b NC04 (giữa chuỗi): giữ chữ cũ 'máy loại trừ giữa chuỗi Gia công'", b);
            Ok(HandoverLedgerService.LamHoViTri(row, null) == "máy loại trừ giữa chuỗi Gia công", "G28-2c NC trống: chữ cũ (không lỗi)");
        }

        Console.WriteLine("[G28-3] LOGIN-TRIM — mật khẩu khởi tạo đã Trim: gõ có dấu cách đầu / cuối vẫn vào; mật khẩu cũ có dấu cách vẫn vào bằng nguyên văn; sai vẫn sai");
        {
            var db = new AppDbContext();
            var g = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true }; db.UserGroups.List.Add(g);
            db.Users.List.Add(new User { UserId = 1, Username = "a", FullName = "A", IsActive = true, Group = g, GroupId = 1, PasswordHash = PasswordHasher.Hash("abc123") });
            db.Users.List.Add(new User { UserId = 2, Username = "b", FullName = "B", IsActive = true, Group = g, GroupId = 1, PasswordHash = PasswordHasher.Hash(" pass b ") });
            db.Fixup();
            var auth = new AuthService(db);
            Ok(await auth.VerifyCredentialsAsync("a", "abc123") != null, "G28-3a đúng nguyên văn: vào");
            Ok(await auth.VerifyCredentialsAsync("a", "  abc123 ") != null, "G28-3b gõ '  abc123 ' (mật khẩu lưu 'abc123'): vào (trước: báo sai)");
            Ok(await auth.VerifyCredentialsAsync("b", " pass b ") != null, "G28-3c mật khẩu cũ có dấu cách ' pass b ': nguyên văn vẫn vào");
            Ok(await auth.VerifyCredentialsAsync("a", "abc12") == null && await auth.VerifyCredentialsAsync("a", "   ") == null && await auth.VerifyCredentialsAsync("a", " abc1234 ") == null,
               "G28-3d sai / chỉ dấu cách / Trim vẫn sai: không vào");
        }

        Console.WriteLine("[G28-4] LEN-L16 — Máy bấm giờ ≤ 50 (PartMachiningStep.TimingMachine); Ghi chú vật liệu ≤ 500 (ô nhập, cột lịch sử, Kho GhiChuPhoi); LogClip");
        {
            string? e51 = null, e50 = null;
            try { PartStepGuard.CheckLen("GC", (new string('M', 51), 50, "Máy bấm giờ")); } catch (PartStepCodeException ex) { e51 = ex.Message; }
            try { PartStepGuard.CheckLen("GC", (new string('M', 50), 50, "Máy bấm giờ")); } catch (PartStepCodeException ex) { e50 = ex.Message; }
            Ok(e51 != null && e51.Contains("Máy bấm giờ dài quá 50") && e50 == null, "G28-4a CheckLen Máy bấm giờ 51 → chặn, 50 → được", e51);
            var src = File.ReadAllText(FindUp("MES.Web/Services/PartMachiningService.cs"));
            Ok(src.Split("(newStep.TimingMachine, 50, \"Máy bấm giờ\")").Length == 2 && src.Split("(updated.TimingMachine, 50, \"Máy bấm giờ\")").Length == 2,
               "G28-4b PartMachiningService Add + Update đều kiểm Máy bấm giờ 50 (trước: thiếu → SqlException truncated)");
            var m501 = KhPlanService.DetailLenError(null, null, null, null, null, null, null, null, null, null, new string('v', 501));
            var m500 = KhPlanService.DetailLenError(null, null, null, null, null, null, null, null, null, null, new string('v', 500));
            Ok(m501 != null && m501.Contains("Ghi chú vật liệu dài quá 500") && m500 == null, "G28-4c Ghi chú vật liệu 501 → chặn (trước: trần 1000 → nổ ở lịch sử / Kho), 500 → được", m501);
            var c = KhPlanService.LogClip(new string('x', 700));
            Ok(c != null && c.Length == 500 && c.EndsWith("…") && KhPlanService.LogClip("ngắn") == "ngắn" && KhPlanService.LogClip(null) == null && KhPlanService.LogClip(new string('y', 500))!.Length == 500 && !KhPlanService.LogClip(new string('y', 500))!.EndsWith("…"),
               "G28-4d LogClip: > 500 cắt còn 500 kèm '…'; ≤ 500 / null giữ nguyên");
            // Sửa phiếu: Ghi chú vật liệu cũ 700 ký tự (dữ liệu trước Gói 28) đổi sang 'mới' → lưu được, lịch sử OldValue cắt 500
            var db = KhScen.Seed(); var kh = new KhPlanService(db);
            var d = db.KhPlanDetails.List.First(x => x.KhPlanDetailId == 1); var p = db.KhPlans.List[0];
            d.MaterialNotes = new string('c', 700); db.Fixup();
            KhPlanService.KhEditValues EV() => new()
            {
                PlanNotes = p.Notes, ReceivedDate = p.ReceivedDate, ApplyWorkflow = p.ApplyWorkflow, Quantity = d.Quantity, Unit = d.Unit, STD = d.STD, Type = d.Type,
                Priority = d.Priority, Material = d.Material, MaterialConfig = d.MaterialConfig, Location = d.Location, MaterialNotes = d.MaterialNotes, OrderVL = d.OrderVL,
                MaterialCondition = d.MaterialCondition, PartNotes = d.PartNotes
            };
            var o1 = EV(); var n1 = EV(); n1.MaterialNotes = "mới";
            var r = await kh.SaveEditAsync(1, 1, o1, n1, 1, "rút ngắn ghi chú VL");
            var lg = db.KhPlanDetailChangeLogs.List.LastOrDefault(x => x.FieldName == "MaterialNotes");
            Ok(r == null && d.MaterialNotes == "mới" && lg != null && lg.OldValue!.Length == 500 && lg.OldValue.EndsWith("…") && lg.NewValue == "mới",
               "G28-4e ghi chú VL cũ 700 ký tự đổi sang 'mới': lưu được, lịch sử OldValue cắt 500 kèm '…' (trước: nổ truncated ở cột lịch sử)", $"{r} | {lg?.OldValue?.Length}");
            // rà độc lập Gói 28 T1: PO có ghi chú VL cũ 700 ký tự (cột 1000) — sửa ô KHÁC (ghi chú hàng) không bị trần 500 của ô không đổi chặn
            d.MaterialNotes = new string('c', 700); db.Fixup();
            var o2 = EV(); var n2 = EV(); n2.PartNotes = "ghi chú hàng mới";
            var r2 = await kh.SaveEditAsync(1, 1, o2, n2, 1, "sửa ghi chú hàng");
            Ok(r2 == null && d.PartNotes == "ghi chú hàng mới" && d.MaterialNotes!.Length == 700, "G28-4f ghi chú VL cũ 700 (không đổi) + sửa ghi chú hàng: lưu được (trần 500 chỉ khi ĐỔI ô ghi chú VL — rà độc lập T1)", r2);
        }

        Console.WriteLine("[G28-5] TB-DOI-LOAI-GIO — đổi loại dừng máy (Báo hỏng → Bảo trì): chốt dòng cũ + mở dòng mới tại GIỜ NHẬP (sau giờ dừng cũ, không tương lai); không nhập → giờ hiện tại");
        {
            var db = LenScen.Seed(); var auth = new AuthService(db); var tb = new ThietBiService(db, auth);
            var now = DateTime.Now;
            var t0 = now.AddHours(-3); var t1 = now.AddHours(-1);
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 50, SoMay = "MB1", TenMay = "May B", TrangThai = "Báo hỏng", IsActive = true });
            db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = 5001, ThietBiId = 50, FieldName = "Trạng thái", OldValue = "Hoạt động", NewValue = "Báo hỏng", ThoiGianBatDau = t0, ChangedAt = t0, ChangedBy = 1 });
            db.Fixup();
            var m = new ThietBi { ThietBiId = 50, SoMay = "MB1", TenMay = "May B", TrangThai = "Bảo trì", IsActive = true };
            var r = await tb.UpdateAsync(m, "chuyển bảo trì từ 1 giờ trước", t1, null, 1, "1111"); db.Fixup();
            var old = db.ThietBiChangeLogs.List.First(x => x.ChangeId == 5001);
            var neu = db.ThietBiChangeLogs.List.Where(x => x.ThietBiId == 50 && x.ChangeId != 5001 && x.FieldName == "Trạng thái").OrderByDescending(x => x.ChangedAt).FirstOrDefault();
            Ok(r.Success && old.ThoiGianKetThuc == t1 && old.SoPhutDung == 120 && neu != null && neu.NewValue == "Bảo trì" && neu.ThoiGianBatDau == t1 && neu.ThoiGianKetThuc == null,
               "G28-5a nhập giờ bắt đầu Bảo trì 1 giờ trước: dòng Báo hỏng chốt tại giờ đó (120 phút), dòng Bảo trì mở từ giờ đó (trước: luôn giờ hiện tại)",
               $"{r.Error} | cũ kết thúc {old.ThoiGianKetThuc:HH:mm} {old.SoPhutDung} ph · mới {neu?.NewValue} từ {neu?.ThoiGianBatDau:HH:mm}");
            // không nhập (trang để giờ bắt đầu sự cố cũ) → giờ hiện tại
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 51, SoMay = "MB2", TenMay = "May B2", TrangThai = "Báo hỏng", IsActive = true });
            db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = 5101, ThietBiId = 51, FieldName = "Trạng thái", OldValue = "Hoạt động", NewValue = "Báo hỏng", ThoiGianBatDau = t0, ChangedAt = t0, ChangedBy = 1 });
            db.Fixup();
            var before = DateTime.Now;
            var r2 = await tb.UpdateAsync(new ThietBi { ThietBiId = 51, SoMay = "MB2", TenMay = "May B2", TrangThai = "Bảo trì", IsActive = true }, "chuyển bảo trì", t0, null, 1, "1111"); db.Fixup();
            var old2 = db.ThietBiChangeLogs.List.First(x => x.ChangeId == 5101);
            Ok(r2.Success && old2.ThoiGianKetThuc >= before && old2.ThoiGianKetThuc <= DateTime.Now, "G28-5b giờ trong ô = giờ dừng cũ (không nhập mới): chốt tại giờ hiện tại như trước", $"{r2.Error} | {old2.ThoiGianKetThuc:HH:mm:ss}");
            // rà độc lập Gói 28 L3: giờ nhập TRƯỚC giờ dừng đang mở → báo, không đổi gì
            db.ThietBis.List.Add(new ThietBi { ThietBiId = 52, SoMay = "MB3", TenMay = "May B3", TrangThai = "Báo hỏng", IsActive = true });
            db.ThietBiChangeLogs.List.Add(new ThietBiChangeLog { ChangeId = 5201, ThietBiId = 52, FieldName = "Trạng thái", OldValue = "Hoạt động", NewValue = "Báo hỏng", ThoiGianBatDau = t1, ChangedAt = t1, ChangedBy = 1 });
            db.Fixup();
            var r3 = await tb.UpdateAsync(new ThietBi { ThietBiId = 52, SoMay = "MB3", TenMay = "May B3", TrangThai = "Bảo trì", IsActive = true }, "chuyển", t0, null, 1, "1111"); db.Fixup();
            var old3 = db.ThietBiChangeLogs.List.First(x => x.ChangeId == 5201);
            Ok(!r3.Success && (r3.Error ?? "").Contains("phải sau giờ bắt đầu lần dừng đang mở") && old3.ThoiGianKetThuc == null, "G28-5c giờ nhập trước giờ dừng đang mở → báo, dòng cũ không chốt (trước: lặng lẽ lấy giờ hiện tại)", r3.Error);
        }

        Console.WriteLine("[G28-6] LOG-TRAN — file nhật ký / ngày chạm trần (Logging:File:MaxMBPerDay) → ghi 1 dòng báo rồi ngừng tới ngày sau");
        {
            var dir = Path.Combine(Path.GetTempPath(), "mes_g28_log_" + Guid.NewGuid().ToString("N"));
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:File:Path"] = dir, ["Logging:File:MaxMBPerDay"] = "1" }).Build();
            using (var prov = FileLoggerProvider.FromConfig(cfg, "/khong-dung"))
            {
                var lg = prov.CreateLogger("MES.Thu");
                var line = new string('x', 2000);
                for (int i = 0; i < 1200; i++) lg.LogWarning("dòng {I} {L}", i, line);
                lg.LogError("LOI-SAU-TRAN {X}", 1);   // rà độc lập Gói 28 L4: lỗi vẫn ghi (tới 2× trần)
            }
            var f = Path.Combine(dir, $"mes-{DateTime.Now:yyyyMMdd}.log");
            var len = new FileInfo(f).Length; var txt = File.ReadAllText(f);
            int capLines = txt.Split("chạm trần 1 MB").Length - 1;
            Ok(len <= 1024 * 1024 + 8192 && capLines == 1 && txt.Contains("dòng 0 ") && txt.Contains("LOI-SAU-TRAN 1"), "G28-6 trần 1 MB: file ≤ ~1 MB, đúng 1 dòng báo chạm trần, các dòng đầu vẫn có; dòng Lỗi sau trần vẫn ghi (trước: không trần — ~2,4 MB)", $"len {len} cap {capLines}");
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine("[G28-7] G27-L11-CHU — băng vàng bản quyền: số ngày còn lại tính từ giờ thật (không còn 'còn khoảng 0 ngày' khi chưa khoá)");
        {
            var now = new DateTime(2026, 11, 20, 9, 0, 0);
            ServerLicenseHelper.LicenseInfo L(double days)
            {
                var i = new ServerLicenseHelper.LicenseInfo { Status = ServerLicenseHelper.LicenseStatus.Active, LastOnlineVerify = now.AddDays(-days), Message = "cũ" };
                ServerLicenseHelper.ApplyOfflineGrace(i, now); return i;
            }
            var a = L(29.5); var b = L(10); var c = L(29);
            Ok(a.Status == ServerLicenseHelper.LicenseStatus.Active && (a.OfflineWarning ?? "").Contains("còn chưa tới 1 ngày") && !(a.OfflineWarning ?? "").Contains("0 ngày trước"),
               "G28-7a 29,5 ngày: 'còn chưa tới 1 ngày' (trước: 'còn khoảng 0 ngày')", a.OfflineWarning);
            Ok((b.OfflineWarning ?? "").Contains("còn khoảng 20 ngày") && (c.OfflineWarning ?? "").Contains("còn khoảng 1 ngày"), "G28-7b 10 ngày → 'còn khoảng 20 ngày'; 29 ngày → 'còn khoảng 1 ngày'", b.OfflineWarning + " | " + c.OfflineWarning);
        }

        Console.WriteLine("[G28-8] CU-NGHI-LUU — WorkMinutes.SavedSegs (trang + máy chủ dùng chung): giờ nghỉ đã lưu giữ, ca hiện tại chỉ lấy khoảng không chồng; lọc theo khung");
        {
            var D = DateTime.Today;
            var ca = new CaLamViec { CaId = 9, TenCa = "C9", GioBatDau = new TimeOnly(7, 0), GioKetThuc = new TimeOnly(16, 0), IsActive = true };
            ca.NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { NghiId = 91, CaId = 9, NghiBatDau = new TimeOnly(12, 30), NghiKetThuc = new TimeOnly(13, 30) });
            ca.NghiGiaiLaos.Add(new CaLamViec_NghiGiaiLao { NghiId = 92, CaId = 9, NghiBatDau = new TimeOnly(15, 0), NghiKetThuc = new TimeOnly(15, 15) });
            var saved = new[] { new BreakSeg(D.AddHours(12), D.AddHours(13), 90, "Trưa cũ") };
            var a = WorkMinutes.SavedSegs(ca, D.AddHours(11), D.AddHours(16), saved);
            Ok(a.Count == 2 && a[0].Start == D.AddHours(12) && a[0].End == D.AddHours(13) && a[1].Start == D.AddHours(15),
               "G28-8a nghỉ đã lưu 12:00–13:00 giữ; ca nay 12:30–13:30 (chồng 1 phần) bỏ; 15:00–15:15 (không chồng) lấy theo ca", string.Join(",", a.Select(x => $"{x.Start:HH:mm}-{x.End:HH:mm}")));
            var b = WorkMinutes.SavedSegs(ca, D.AddHours(14), D.AddHours(16), saved);
            Ok(b.Count == 1 && b[0].Start == D.AddHours(15), "G28-8b khung 14:00–16:00: khoảng đã lưu ngoài khung không lấy (như BreakSegments)", string.Join(",", b.Select(x => $"{x.Start:HH:mm}")));
            var c = WorkMinutes.SavedSegs(ca, D.AddHours(11), D.AddHours(16), Array.Empty<BreakSeg>());
            var c0 = WorkMinutes.BreakSegments(ca, D.AddHours(11), D.AddHours(16));
            Ok(c.Count == c0.Count && c.Zip(c0).All(z => z.First.Start == z.Second.Start && z.First.End == z.Second.End), "G28-8c dòng gốc không có nghỉ đã lưu: = theo ca hiện tại (y cũ)");
            var e = WorkMinutes.SavedSegs(null, D.AddHours(11), D.AddHours(16), saved);
            Ok(e.Count == 1 && e[0].Start == D.AddHours(12), "G28-8d ca null: chỉ khoảng đã lưu (không lỗi)");
            var v = WorkMinutes.VerifySegs(a, 9, D.AddHours(11), D.AddHours(16), new List<WorkMinutes.SentBreak> { new() { BreakStart = D.AddHours(12), BreakEnd = D.AddHours(13), WorkedMin = 20 }, new() { BreakStart = D.AddHours(15), BreakEnd = D.AddHours(15.25), WorkedMin = 0 } });
            var v2 = WorkMinutes.VerifySegs(a, 9, D.AddHours(11), D.AddHours(16), new List<WorkMinutes.SentBreak> { new() { BreakStart = D.AddHours(12.5), BreakEnd = D.AddHours(13.5), WorkedMin = 0 } });
            Ok(v.Error == null && v.Rows.Count == 2 && v.Rows[0].WorkedMin == 20 && v2.Error == WorkMinutes.MsgCaChanged, "G28-8e VerifySegs: trang gửi đúng bộ đã lưu → nhận (làm thông 20); gửi theo ca hiện tại → báo lệch", v.Error + " | " + v2.Error);
        }

        Console.WriteLine($"Tổng phép kiểm: {checks}");
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }

    static string FindUp(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (File.Exists(p)) return p;
        }
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 10 && cwd != null; i++, cwd = cwd.Parent)
        {
            var p = Path.Combine(cwd.FullName, rel);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(rel);
    }
}
