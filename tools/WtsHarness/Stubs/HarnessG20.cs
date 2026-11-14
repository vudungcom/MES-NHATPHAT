// [GÓI 20 2026-11-12][G19-HUYHIEU-NHAN][G19-VITRI-OFF][G19-KIEM-THEM] Dữ liệu drive_g20.py (POST /harness/g20-seed) — không trong seed khởi động (drive.py không đổi).
//   Nguồn: CLAUDE rà độc lập Gói 19 12/11 (3 điểm Thấp ghi nhớ + ca kiểm còn thiếu: phiếu bù / nhãn ô Hàng lỗi đã xử lý); HÙNG 12/11 20:02.
//   KH-G20 / PO-G20 10 chiếc (Workflow ON): GC NC01 → NC02, HTSP Taro T20, KCS K1.
//     Kho giao GC 10 → GC nhận 8 + NG 2 lúc nhận (vụ V1, tự lập phiếu trả Kho) → Kho nhận lại 2 → Kho trả khách 2 (V1)
//     → khách gửi bù 1 (phiếu bù #a) + 1 (phiếu bù #b, rồi HUỶ "nhập nhầm")
//     GC báo lỗi sau nhận 5 (V2, chưa làm) → GC gửi HTSP nhờ sửa 1 + 1 + 2 + 1 (ô Hàng sửa) → HTSP nhận ĐỔI Ô sang Hàng lỗi (lý do) — huy hiệu phải ghi "nhận thật: ô Hàng lỗi"
//     GC WTS NC01 / NC02 3 → GC giao HTSP 3 → HTSP WTS 3 → HTSP giao KCS 3 → KCS nhận 3 → KCS trả GC 2 lỗi công đoạn trước (V3)
//     → GC Kết quả sửa 2 (@XONG) → KCS xác nhận 2 → GC giữ ô Hàng lỗi đã xử lý 2 → GC giao HTSP (nhóm giữa) 2 ô Hàng lỗi đã xử lý ("đã sửa đạt")
//     → HTSP nhận 2 → HTSP trả lại GC 1 ô Hàng lỗi ("Hàng lỗi đã xử lý (trả lại — chưa đạt)", XL-TRA-LAI) + giao KCS 1 ô Hàng lỗi đã xử lý
//   KH-G20F / PO-G20F 5 chiếc (Workflow OFF): Kho giao GC 5 → GC nhận 5 → WTS NC01 OK 7 (OFF lưu được) → ô âm — khung Đang ở đâu phải ghi "PO tắt Workflow".
//   KH-G20G / PO-G20G 5 chiếc (Workflow OFF, cân, không ô âm) → khung không có chữ đó.
//   KH-G20H / PO-G20H 5 chiếc: ô âm nhập lúc OFF, nay Workflow ON → khung có "có ô âm" nhưng không có chữ "PO tắt Workflow".
//   KH-G20L / PO-G20L 10 chiếc (Workflow OFF): dòng ghi thẳng HTSP trả hàng theo NC về Kho → khung lệch, không ô âm → đỏ "lệch", không vàng.
//   V2: GC báo lỗi 4 → phiếu 1 HTSP nhận đổi ô Hàng lỗi (huy hiệu "nhận thật"); phiếu 2 lần nhận đổi ô bị huỷ rồi nhận lại ô Hàng sửa (không "nhận thật"); phiếu 3 nhận lẫn 2 ô ("có lần nhận vào"); phiếu 4 huỷ lần nhận đúng ô, còn lần đổi ô ("nhận thật").
//   Chỉ dùng hàm có từ trước (bản Gói 19 dựng được). seq = dấu từng chứng từ theo giờ lập (như HarnessG19), có cả phiếu bù.
using MES.Web.Constants;
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG20
{
    public static async Task<object> SeedAsync(AppDbContext db)
    {
        var admin = db.Users.List.First(u => u.Username == "admin");
        var kcsU  = db.Users.List.FirstOrDefault(u => u.Username == "kcsg20");
        if (kcsU == null)
        {
            // người KCS có quyền sửa Giao nhận KCS (xác nhận hàng sửa — khác người ghi Kết quả sửa), như HarnessG15
            var g = new UserGroup { GroupCode = "GNKCS20", GroupName = "Giao nhận KCS (G20)", IsActive = true,
                Permissions = System.Text.Json.JsonSerializer.Serialize(new[] { new GroupPermissionSetting { SectionCode = SystemPermissions.HandoverKcs, CanView = true, CanEdit = true } }.ToList()) };
            db.UserGroups.Add(g); await db.SaveChangesAsync();
            db.Users.Add(new User { Username = "kcsg20", FullName = "KCS giao nhận G20", PasswordHash = PasswordHasher.Hash("123456"), GroupId = g.GroupId, IsActive = true });
            await db.SaveChangesAsync();
            kcsU = db.Users.List.First(u => u.Username == "kcsg20");
        }
        if (!db.KhPlans.List.Any(p => p.PlanNo == "KH-G20"))
        {
            var cust = db.Customers.List.First();
            var t0 = DateTime.Now.AddHours(-3);
            KhPlanDetail NewPo(string planNo, string po, decimal qty, bool wf)
            {
                var plan = new KhPlan { PlanNo = planNo, PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = wf, CreatedBy = admin.UserId, CreatedAt = t0 };
                db.KhPlans.Add(plan); db.SaveChanges();
                var d = new KhPlanDetail { KhPlanId = plan.KhPlanId, LineNo = 1, PartNo = "PART-" + po, PurchaseOrder = po, Quantity = qty, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
                db.KhPlanDetails.Add(d); db.SaveChanges();
                int so = 0;
                foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "M2") })
                    db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = d.KhPlanDetailId, StepOrder = ++so, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
                db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "2", WtsTaskCode = "T20", StepName = "Taro T20", StandardTime = 1, SnapshotAt = t0, SnapshotBy = admin.UserId });
                db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = d.KhPlanDetailId, StepOrder = 1, NC = "K1" });
                db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = d.KhPlanDetailId, IsActive = true, SoLuongThucNhan = (int)qty, SoLuongKeHoach = (int)qty, NgayNhanPhoi = t0, OrderVatLieu = "OVL-" + po, ViTriDePhoi = po, SoPO = po, PartNo = "PART-" + po, CreatedAt = t0, CreatedBy = admin.UserId });
                db.SaveChanges();
                return d;
            }
            if (!db.StandardWtsTasks.List.Any(t => t.TaskCode == "T20"))
                db.StandardWtsTasks.Add(new StandardWtsTask { CategoryCode = "TARO", CategoryName = "Công việc nhóm Taro", TaskCode = "T20", TaskName = "Taro T20", DisplayOrder = 65, IsActive = true, CreatedAt = t0, CreatedBy = admin.UserId });
            if (!db.NgCategories.List.Any(c => c.IsActive))
                db.NgCategories.Add(new NgCategory { Code = "KT-SAI", Name = "Sai kích thước", SortOrder = 1, IsActive = true, CreatedBy = admin.UserId, CreatedAt = t0 });
            var d = NewPo("KH-G20", "PO-G20", 10, true);
            var f = NewPo("KH-G20F", "PO-G20F", 5, false);
            await db.SaveChangesAsync(); db.Fixup();
            var cat = db.NgCategories.List.First(c => c.IsActive);
            var svc = new HandoverService(db); int id = d.KhPlanDetailId;
            async Task Must(Task<(bool Ok, string Error)> t, string what) { var r = await t; if (!r.Ok) throw new Exception("g20-seed " + what + ": " + r.Error); db.Fixup(); }
            long Last() => db.HandoverTransactions.List.Max(x => x.HandoverTxId);
            long LastCase(int det) => db.HandoverTransactions.List.Where(x => x.KhPlanDetailId == det && x.NgCaseId != null).OrderBy(x => x.HandoverTxId).Last().NgCaseId!.Value;
            void Wts(int det, string pg, string? nc, string? code, int k, decimal ok)
                => db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = det, ProcessGroup = pg, NC = nc ?? code, WtsCode = code, QtyDone = ok, QtyOk = ok, QtyNg = 0,
                    StartTime = t0.AddMinutes(k), EndTime = t0.AddMinutes(k + 5), WorkerId = admin.UserId, CreatedAt = t0.AddMinutes(k + 5) });

            // V1 — NG lúc nhận → Kho trả khách → khách gửi bù 1 + 1 (huỷ)
            await Must(svc.IssueByCellAsync(id, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 8, 2, cat.Name, admin.UserId, ngCategoryId: cat.NgCategoryId), "GC nhận 8 + NG 2");
            var v1 = LastCase(id);
            await Must(svc.ReceiveAsync(Last(), 0, 2, null, admin.UserId), "Kho nhận lại 2 ô Hàng lỗi");
            await Must(svc.IssueSpecialAsync(id, "KHO", "KHACH", 2, admin.UserId, HandoverPurpose.CustReturn, reason: "phôi lỗi", ngCaseId: v1), "Kho trả khách 2");
            await Must(svc.ReceiveReplacementAsync(id, v1, 1, "phiếu khách 1", admin.UserId), "khách gửi bù 1");
            await Must(svc.ReceiveReplacementAsync(id, v1, 1, null, admin.UserId), "khách gửi bù 1 (sẽ huỷ)");
            var buLast = db.HandoverReplacements.List.Max(x => x.ReplacementId);
            await Must(svc.VoidReplacementAsync(buLast, "nhập nhầm", admin.UserId), "huỷ phiếu bù");

            // V2 — bên nhận đổi ô (Hàng sửa → Hàng lỗi)
            await Must(svc.ReportNgAfterReceiveAsync(id, "GC", HandoverNgSource.Chua, "NC01", 5, HandoverNgSource.DauRa, "GC", "", null, admin.UserId, new List<string> { "GC" }, cat.NgCategoryId), "GC báo lỗi 5 (V2)");
            var v2 = db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == id).OrderBy(r => r.HandoverNgReportId).Last().NgCaseId!.Value;
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 1, admin.UserId, "LOI", false, "SUA", ngCaseId: v2), "GC gửi HTSP nhờ sửa 1 (V2, phiếu đổi ô)");
            await Must(svc.ReceiveAsync(Last(), 0, 1, null, admin.UserId, toCell: HandoverCell.Loi, toCellReason: "giữ ô hàng lỗi chờ xem"), "HTSP nhận đổi ô sang Hàng lỗi");
            // phiếu thứ 2: lần nhận đổi ô bị HUỶ, nhận lại đúng ô Hàng sửa → huy hiệu KHÔNG ghi "nhận thật" (lần nhận đã huỷ không tính)
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 1, admin.UserId, "LOI", false, "SUA", ngCaseId: v2, notes: "phieu-huy-nhan"), "GC gửi HTSP nhờ sửa 1 (V2, lần nhận huỷ)");
            long tx2 = Last();
            await Must(svc.ReceiveAsync(tx2, 0, 1, null, admin.UserId, toCell: HandoverCell.Loi, toCellReason: "nham o"), "HTSP nhận đổi ô (sẽ huỷ)");
            var rc2 = db.HandoverReceives.List.Where(r => r.HandoverTxId == tx2).OrderBy(r => r.ReceiveId).Last().ReceiveId;
            await Must(svc.VoidReceiveAsync(rc2, "nhận nhầm ô", admin.UserId, new List<string> { "HTSP", "GC", "KHO", "KCS", "PKG" }), "huỷ lần nhận đổi ô");
            await Must(svc.ReceiveAsync(tx2, 1, 0, null, admin.UserId), "HTSP nhận lại đúng ô Hàng sửa");
            // phiếu thứ 3 (2 chiếc): nhận 1 đúng ô Hàng sửa + 1 đổi ô Hàng lỗi → "(có lần nhận vào ô Hàng lỗi)" (rà độc lập Gói 20 Thấp 1)
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 2, admin.UserId, "LOI", false, "SUA", ngCaseId: v2, notes: "phieu-nhan-lan"), "GC gửi HTSP nhờ sửa 2 (V2, nhận lẫn ô)");
            long tx3 = Last();
            await Must(svc.ReceiveAsync(tx3, 1, 0, null, admin.UserId), "HTSP nhận 1 ô Hàng sửa");
            await Must(svc.ReceiveAsync(tx3, 0, 1, null, admin.UserId, toCell: HandoverCell.Loi, toCellReason: "chiec nay de o loi"), "HTSP nhận 1 đổi ô Hàng lỗi");
            // phiếu thứ 4 (ngược L11b): lần nhận đúng ô Hàng sửa bị HUỶ, lần còn hiệu lực đổi ô Hàng lỗi → "(nhận thật: ô Hàng lỗi)" (rà độc lập Gói 20 vòng 2 Thấp 4)
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 1, admin.UserId, "LOI", false, "SUA", ngCaseId: v2, notes: "phieu-huy-dung-o"), "GC gửi HTSP nhờ sửa 1 (V2, huỷ lần nhận đúng ô)");
            long tx4 = Last();
            await Must(svc.ReceiveAsync(tx4, 1, 0, null, admin.UserId), "HTSP nhận 1 ô Hàng sửa (sẽ huỷ)");
            var rc4 = db.HandoverReceives.List.Where(r => r.HandoverTxId == tx4).OrderBy(r => r.ReceiveId).Last().ReceiveId;
            await Must(svc.VoidReceiveAsync(rc4, "nhan lai o loi", admin.UserId, new List<string> { "HTSP", "GC", "KHO", "KCS", "PKG" }), "huỷ lần nhận đúng ô");
            await Must(svc.ReceiveAsync(tx4, 0, 1, null, admin.UserId, toCell: HandoverCell.Loi, toCellReason: "o loi"), "HTSP nhận lại đổi ô Hàng lỗi");

            // V3 — KCS trả GC → GC sửa đạt, giữ ô Hàng lỗi đã xử lý → giao KCS (đã sửa đạt) → KCS trả lại 1 (chưa đạt)
            Wts(id, "GC", "NC01", null, 10, 3); Wts(id, "GC", "NC02", null, 20, 3); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 3, admin.UserId, "MOI", false, "MOI"), "GC giao HTSP 3");
            await Must(svc.ReceiveAsync(Last(), 3, 0, null, admin.UserId), "HTSP nhận 3");
            Wts(id, "TARO", null, "T20", 40, 3); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(id, "HTSP", "KCS", 3, admin.UserId, "MOI", false, "MOI"), "HTSP giao KCS 3");
            await Must(svc.ReceiveAsync(Last(), 3, 0, null, admin.UserId), "KCS nhận 3");
            await Must(svc.IssueByCellAsync(id, "KCS", "GC", 2, admin.UserId, "LOI", true, "LOI", ngCategoryId: cat.NgCategoryId), "KCS trả GC 2 lỗi (V3)");
            var v3 = LastCase(id);
            await Must(svc.ReceiveAsync(Last(), 0, 2, null, admin.UserId), "GC nhận 2 ô Hàng lỗi");
            await Must(svc.CreateRepairResultAsync(id, "GC", HandoverCell.Loi, v3, 2, 0, HandoverLedgerService.RepXong, null, admin.UserId), "GC Kết quả sửa 2 (V3)");
            var rep = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id).OrderBy(r => r.HandoverRepairResultId).Last();
            await Must(svc.ConfirmRepairAsync(rep.HandoverRepairResultId, 2, 0, null, kcsU.UserId), "KCS xác nhận 2");
            await Must(svc.IssueByCellAsync(id, "GC", "HTSP", 2, admin.UserId, "XL", false, "XL", ngCaseId: v3), "GC giao HTSP 2 ô Hàng lỗi đã xử lý (nhóm giữa giữ ô)");
            await Must(svc.ReceiveAsync(Last(), 2, 0, null, admin.UserId), "HTSP nhận 2 ô Hàng lỗi đã xử lý");
            await Must(svc.IssueByCellAsync(id, "HTSP", "GC", 1, admin.UserId, "XL", false, "LOI", ngCaseId: v3), "HTSP trả lại GC 1 ô Hàng lỗi (chưa đạt)");
            await Must(svc.IssueByCellAsync(id, "HTSP", "KCS", 1, admin.UserId, "XL", false, "XL", ngCaseId: v3), "HTSP giao KCS 1 ô Hàng lỗi đã xử lý (đã sửa đạt)");

            // PO-G20F — Workflow OFF, ô âm
            int fid = f.KhPlanDetailId;
            await Must(svc.IssueByCellAsync(fid, "KHO", "GC", 5, admin.UserId, "MOI", false, "MOI"), "F: Kho giao GC 5");
            await Must(svc.ReceiveAsync(Last(), 5, 0, null, admin.UserId), "F: GC nhận 5");
            Wts(fid, "GC", "NC01", null, 30, 7); await db.SaveChangesAsync(); db.Fixup();
            // PO-G20G — Workflow OFF nhưng sổ cân, không ô âm → khung Đang ở đâu không có huy hiệu "PO tắt Workflow"
            var gg = NewPo("KH-G20G", "PO-G20G", 5, false); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(gg.KhPlanDetailId, "KHO", "GC", 5, admin.UserId, "MOI", false, "MOI"), "G: Kho giao GC 5");
            await Must(svc.ReceiveAsync(Last(), 5, 0, null, admin.UserId), "G: GC nhận 5");
            // PO-G20H — ô âm có từ lúc OFF, nay PO BẬT Workflow (đặt cờ thẳng như dữ liệu cũ) → không có huy hiệu "PO tắt Workflow" (vẫn "có ô âm")
            var hh = NewPo("KH-G20H", "PO-G20H", 5, false); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueByCellAsync(hh.KhPlanDetailId, "KHO", "GC", 5, admin.UserId, "MOI", false, "MOI"), "H: Kho giao GC 5");
            await Must(svc.ReceiveAsync(Last(), 5, 0, null, admin.UserId), "H: GC nhận 5");
            Wts(hh.KhPlanDetailId, "GC", "NC01", null, 30, 7); await db.SaveChangesAsync();
            db.KhPlans.List.First(x => x.PlanNo == "KH-G20H").ApplyWorkflow = true; await db.SaveChangesAsync(); db.Fixup();
            // PO-G20L — Workflow OFF, khung "Đang ở đâu" LỆCH mà không ô âm: dòng ghi thẳng HTSP trả hàng giao theo NC về KHO (máy chủ chặn đường này —
            //   chỉ dựng được bằng ghi thẳng DB). Khung phải có đỏ "lệch", KHÔNG có vàng "PO tắt Workflow" (lệch là lỗi sổ cần báo, không che).
            //   Cách dựng: rà độc lập Gói 20 vòng 3 (Trung 1).
            var lp = new KhPlan { PlanNo = "KH-G20L", PlanDate = t0, CustomerId = cust.CustomerId, ApplyWorkflow = false, CreatedBy = admin.UserId, CreatedAt = t0 };
            db.KhPlans.Add(lp); db.SaveChanges();
            var ll = new KhPlanDetail { KhPlanId = lp.KhPlanId, LineNo = 1, PartNo = "PART-PO-G20L", PurchaseOrder = "PO-G20L", Quantity = 10, Unit = "EA", STD = t0.AddDays(10), CreatedAt = t0 };
            db.KhPlanDetails.Add(ll); db.SaveChanges();
            int so2 = 0;
            foreach (var (nc, m) in new[] { ("NC01", "M1"), ("NC02", "H04"), ("NC03", "M2") })
                db.KhPlanRouteSnapshotMachining.Add(new KhPlanRouteSnapshotMachining { KhPlanDetailId = ll.KhPlanDetailId, StepOrder = ++so2, NC = nc, MachineRegistered = m, SetupTime = 5, MachiningTime = 2, InspectionTime = 1, PreparationTime = 10, TrialRunTime = 3 });
            db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude { KhPlanDetailId = ll.KhPlanDetailId, SoMay = "H04", SnapshotAt = t0, SnapshotBy = admin.UserId });
            db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection { KhPlanDetailId = ll.KhPlanDetailId, StepOrder = 1, NC = "K1" });
            db.KhoVatLieus.Add(new KhoVatLieu { PlanDetailId = ll.KhPlanDetailId, IsActive = true, SoLuongThucNhan = 10, SoLuongKeHoach = 10, NgayNhanPhoi = t0, OrderVatLieu = "OVL-PO-G20L", ViTriDePhoi = "PO-G20L", SoPO = "PO-G20L", PartNo = "PART-PO-G20L", CreatedAt = t0, CreatedBy = admin.UserId });
            await db.SaveChangesAsync(); db.Fixup();
            int lid = ll.KhPlanDetailId;
            await Must(svc.IssueByCellAsync(lid, "KHO", "GC", 10, admin.UserId, "MOI", false, "MOI"), "L: Kho giao GC 10");
            await Must(svc.ReceiveAsync(Last(), 10, 0, null, admin.UserId), "L: GC nhận 10");
            Wts(lid, "GC", "NC01", null, 60, 2); await db.SaveChangesAsync(); db.Fixup();
            await Must(svc.IssueSpecialAsync(lid, "GC", "HTSP", 2, admin.UserId, HandoverPurpose.HelpSend, fromNC: "NC01", toNC: "NC02", notes: "giao theo NC"), "L: GC giao HTSP theo NC 2");
            await Must(svc.ReceiveAsync(Last(), 2, 0, null, admin.UserId), "L: HTSP nhận 2");
            db.HandoverTransactions.Add(new HandoverTransaction { KhPlanDetailId = lid, FromGroupCode = "HTSP", ToGroupCode = "KHO", QtyIssued = 2, Purpose = HandoverPurpose.HelpReturn,
                FromNC = "NC02", Status = "COMPLETED", IssuedBy = admin.UserId, IssuedAt = DateTime.Now.AddSeconds(1), Notes = "harness: ghi thẳng (máy chủ chặn)" });   // [G20-CA-SHOWNCELL 2026-11-14] giờ sau phiếu thật (trước t0+80 phút — sớm hơn phiếu lập bằng hàm thật, ghi nhớ G20-GHI-NHO 4)
            await db.SaveChangesAsync(); db.Fixup();
            db.HandoverReceives.Add(new HandoverReceive { HandoverTxId = Last(), QtyOk = 2, QtyNg = 0, ReceivedBy = admin.UserId, ReceivedAt = DateTime.Now.AddSeconds(2), CreatedAt = DateTime.Now.AddSeconds(2) });
            await db.SaveChangesAsync(); db.Fixup();
            // PO-G21C — [G20-CA-SHOWNCELL 2026-11-14] phiếu CŨ không ghi ô (trước [GN-CELL] 24/10: FromCell / ToCell null, lần nhận không ô) — ghi thẳng DB như dữ liệu cũ.
            //   Huy hiệu Lịch sử phiếu (RealRecvCellsText nhánh "?? shownCell"): lần nhận không ô = ô đang hiện (Hàng mới).
            //   Phiếu A: 1 lần không ô + 1 lần ô Hàng lỗi → "(có lần nhận vào ô Hàng lỗi)"; phiếu B: chỉ lần không ô → không chữ.
            //   Nguồn: CLAUDE (sổ G20-GHI-NHO mục 2 — nhánh chưa có ca kiểm); HÙNG 14/11 08:28 OK Gói 21.
            var cc = NewPo("KH-G21C", "PO-G21C", 6, false); await db.SaveChangesAsync(); db.Fixup();
            int cid = cc.KhPlanDetailId;
            await Must(svc.IssueByCellAsync(cid, "KHO", "GC", 6, admin.UserId, "MOI", false, "MOI"), "C: Kho giao GC 6");
            await Must(svc.ReceiveAsync(Last(), 6, 0, null, admin.UserId), "C: GC nhận 6");
            Wts(cid, "GC", "NC01", null, 90, 6); Wts(cid, "GC", "NC02", null, 100, 6); await db.SaveChangesAsync(); db.Fixup();
            foreach (var (note, cells) in new[] { ("phieu-cu-lan", new string?[] { null, HandoverCell.Loi }), ("phieu-cu-khong-o", new string?[] { null }) })
            {
                db.HandoverTransactions.Add(new HandoverTransaction { KhPlanDetailId = cid, FromGroupCode = "GC", ToGroupCode = "HTSP", QtyIssued = cells.Length, Purpose = HandoverPurpose.New,
                    Status = "COMPLETED", IssuedBy = admin.UserId, IssuedAt = DateTime.Now, Notes = note });
                await db.SaveChangesAsync(); db.Fixup();
                long tc = Last();
                foreach (var c in cells)
                    db.HandoverReceives.Add(new HandoverReceive { HandoverTxId = tc, QtyOk = 1, QtyNg = 0, ToCell = c, ToCellReason = c == null ? null : "o loi (du lieu cu)",
                        ReceivedBy = admin.UserId, ReceivedAt = DateTime.Now, CreatedAt = DateTime.Now });
                await db.SaveChangesAsync(); db.Fixup();
            }
        }
        var det = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G20");
        int did = det.KhPlanDetailId;
        var txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == did && !t.IsVoided).OrderBy(t => t.HandoverTxId).ToList();
        var bus = db.HandoverReplacements.List.Where(r => r.KhPlanDetailId == did).OrderBy(r => r.ReplacementId).ToList();
        long v1c = bus.First().NgCaseId;
        long Cnt(long c) => txs.Count(t => t.NgCaseId == c || db.HandoverReceives.List.Any(r => r.HandoverTxId == t.HandoverTxId && r.NgCaseId == c))
            + db.HandoverNgReports.List.Count(r => r.KhPlanDetailId == did && r.NgCaseId == c) + db.HandoverRepairResults.List.Count(r => r.KhPlanDetailId == did && r.NgCaseId == c)
            + bus.Count(b => b.NgCaseId == c);
        var cases = txs.Select(t => t.NgCaseId).Concat(db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == did).Select(r => r.NgCaseId)).Where(x => x is > 0).Select(x => x!.Value).Distinct().OrderBy(x => x).ToList();
        var led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(did);
        return new
        {
            balanced = led?.Balanced, negs = led == null ? -1 : HandoverLedgerService.NegativeCells(led).Count,
            id = did, fid = db.KhPlanDetails.List.First(x => x.PurchaseOrder == "PO-G20F").KhPlanDetailId,
            v1 = NgCaseNo.Format(v1c), cases = cases.Select(NgCaseNo.Format).ToList(),
            nV1 = Cnt(v1c), nV2 = cases.Count > 1 ? Cnt(cases[1]) : 0,
            buIds = bus.Select(b => b.ReplacementId).ToList(), buVoid = bus.Where(b => b.IsVoided).Select(b => b.ReplacementId).ToList(),
            seq = txs.Select(t => (At: t.IssuedAt, P: "T" + t.HandoverTxId, K: "#" + t.HandoverTxId + " "))
                .Concat(db.HandoverNgReports.List.Where(r => r.KhPlanDetailId == did).Select(r => (At: r.CreatedAt, P: "B" + r.HandoverNgReportId, K: "Báo lỗi #" + r.HandoverNgReportId + " ")))
                .Concat(db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == did).Select(r => (At: r.CreatedAt, P: "R" + r.HandoverRepairResultId, K: "Kết quả sửa #" + r.HandoverRepairResultId + " ")))
                .Concat(bus.Select(b => (At: b.ReceivedAt, P: "U" + b.ReplacementId, K: "Phiếu bù #" + b.ReplacementId + " ")))
                .OrderBy(x => x.At).ThenBy(x => x.P, StringComparer.Ordinal).Select(x => x.K).ToList(),
        };
    }
}
