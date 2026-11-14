// [RA-SOCHUNG] 31/10/2026 — kịch bản `quet`: tự kiểm công cụ quét DB tools/QuetSo (QuetCore.ScanAsync) trên DB giả.
//   Q1: PO đi đủ đường bằng hàm thật (Kho → GC → WTS → HTSP) → quét KHÔNG báo gì.
//   Q2: cài lỗi cố ý (giống dữ liệu cũ ngoài đời) → quét phải bắt đúng loại:
//       dòng WTS QtyDone ≠ OK + NG · WTS vượt trần (OK + NG > nhận vào) · PO không cân · ô âm (giao 16 khi NC sau chưa có WTS).
//   Q3 [N3-FIRST 03/11]: NC đầu chuỗi Gia công là máy loại trừ → LOAI_TRU_DAU (PO) + mục Part Master.
//   Q5 [QUET-VU-SOLE 06/11]: vụ lỗi đi đúng đường → không báo VU_LECH / SO_LE; số lẻ cài cố ý ở PO / lần nhận / WTS đã huỷ → SO_LE đúng chỗ.
//       VU_LECH là lưới chặn lỗi CODE (sổ vụ tính lại từ chứng từ nên cân theo cấu trúc, sửa dữ liệu không làm lệch) — đột biến đảo điều kiện phải làm Q5 SAI.
//       Nguồn: CLAUDE 06/11 (bản "em hiểu" Bước 1) + B3 RS-E9 (Hùng chốt 05/11 15:37 "kiểm DB trước").
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using QuetSo;

public static class QuetScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static AppDbContext Seed(bool workflow)
    {
        var db = new AppDbContext();
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(adminG);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = adminG, GroupId = 1, PIN = "1111" });
        var cust = new Customer { CustomerId = 1, CustomerCode = "KH1", CustomerName = "KH", IsActive = true };
        db.Customers.List.Add(cust);
        var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-QUET", Customer = cust, CustomerId = 1, ApplyWorkflow = workflow };
        db.KhPlans.List.Add(plan);
        var d = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO1", Quantity = 16, LineNo = 1 };
        db.KhPlanDetails.List.Add(d); plan.Details.Add(d);
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "NC1", MachineRegistered = "M1" });
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "NC2", MachineRegistered = "M2" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "2V" });
        db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "3V" });
        VuHelp.SeedCats(db);
        db.Fixup();
        return db;
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("[Q1] PO đi đúng đường bằng hàm thật → quét không báo gì");
        {
            var db = Seed(true); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            var k = await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            Ok(k.Success, "Kho nhận 16 phôi", k.Error);
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1")).Ok, "Kho → GC 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "GC nhận 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 16, 0, 1)).Ok, "WTS NC1 OK 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 16, 0, 1)).Ok, "WTS NC2 OK 16");
            Ok((await svc.IssueByCellAsync(1, "GC", "HTSP", 16, 1, "MOI", false, "MOI")).Ok, "GC → HTSP 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "HTSP nhận 16");
            var r = await QuetCore.ScanAsync(db, led);
            Ok(r.PoCount == 1 && r.Findings.Count == 0, "Quét: 1 PO, 0 phát hiện", string.Join(" | ", r.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
        }

        Console.WriteLine("[Q2] Cài lỗi cố ý (dữ liệu cũ) → quét phải bắt đúng loại");
        {
            var db = Seed(false); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1);
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 16, 0, 1)).Ok, "WTS NC1 OK 16");
            // NC2 có WTS lúc giao rồi bị huỷ → giống PO 0003655603 (GC "đã làm chưa giao" −16)
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 16, 0, 1);
            Ok((await svc.IssueByCellAsync(1, "GC", "HTSP", 16, 1, "MOI", false, "MOI")).Ok, "GC → HTSP 16");
            await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1);
            var log2 = db.WtsProductionLogs.List.First(x => x.WtsLogId == w2.LogId); log2.IsVoided = true;   // ghi thẳng như dữ liệu cũ (không qua K5)
            // dòng WTS Bavia 2V: QtyDone 16 nhưng OK 15 NG 0 → số lệch; 3V: OK 20 > nhận vào 16 → vượt trần
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 900, KhPlanDetailId = 1, ProcessGroup = "BAVIA", WtsCode = "2V", QtyDone = 16, QtyOk = 15, QtyNg = 0, WorkerId = 1, StartTime = DateTime.Now, EndTime = DateTime.Now });
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 901, KhPlanDetailId = 1, ProcessGroup = "BAVIA", WtsCode = "3V", QtyDone = 20, QtyOk = 20, QtyNg = 0, WorkerId = 1, StartTime = DateTime.Now, EndTime = DateTime.Now });
            db.Fixup();
            var before = (db.WtsProductionLogs.List.Count, db.HandoverTransactions.List.Count, db.HandoverReceives.List.Count, db.WtsProductionLogs.List.Count(x => x.IsVoided));
            var r = await QuetCore.ScanAsync(db, led);
            var after = (db.WtsProductionLogs.List.Count, db.HandoverTransactions.List.Count, db.HandoverReceives.List.Count, db.WtsProductionLogs.List.Count(x => x.IsVoided));
            string Has(string kind) => string.Join(" | ", r.Findings.Where(f => f.Kind == kind).Select(f => f.Detail));
            Console.WriteLine("     " + string.Join("\n     ", r.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.WtsSoLech && f.Detail.Contains("#900")), "Bắt dòng WTS QtyDone ≠ OK + NG (#900)", Has(QuetCore.WtsSoLech));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.WtsVuot && f.Detail.Contains("3V")), "Bắt WTS vượt trần Bavia 3V", Has(QuetCore.WtsVuot));
            Ok(r.Findings.Any(f => f.Kind == QuetCore.OAm), "Bắt ô âm (GC đã làm chưa giao âm)", Has(QuetCore.OAm));
            Ok(!r.Findings.Any(f => f.Kind == QuetCore.WtsSoLech && !f.Detail.Contains("#900")), "Không báo nhầm dòng WTS đúng", Has(QuetCore.WtsSoLech));
            Ok(r.Findings.All(f => f.Po == "PO1" && f.Plan == "KH-QUET"), "Mỗi phát hiện ghi đúng PO / KH");
            var sw = new StringWriter(); QuetCore.Write(r, sw, "GIA");
            Ok(sw.ToString().Contains("── PO PO1 · KH KH-QUET · ") && sw.ToString().Contains(" · Workflow OFF (KhPlanDetailId 1)"), "File kết quả liệt kê theo PO (kèm mã chi tiết · khách — [PM-NC-DUP-SVC] 02/11)", sw.ToString().Split('\n').FirstOrDefault(l => l.StartsWith("── PO")));
            Ok(before == after, "Quét không ghi gì (số dòng WTS / phiếu / lần nhận / dòng huỷ giữ nguyên)", $"{before} → {after}");
            // [PM-NC-DUP-SVC 2026-11-01] quy trình PO có 2 dòng chính cùng mã (Đóng gói P1 ×2; Taro theo mã CV) → MA_TRUNG; dòng dự phòng không tính
            Ok(!r.Findings.Any(f => f.Kind == QuetCore.MaTrung), "Quy trình chưa trùng mã: không báo MA_TRUNG", Has(QuetCore.MaTrung));
            db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 801, KhPlanDetailId = 1, StepOrder = 8, NC = "PX" });
            db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 802, KhPlanDetailId = 1, StepOrder = 9, NC = " px " });
            db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 803, KhPlanDetailId = 1, StepOrder = 10, NC = "PX", IsBackup = true, ParentNC = "PX" });
            db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 811, KhPlanDetailId = 1, StepOrder = 8, NC = "TA", WtsTaskCode = "9T" });
            db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 812, KhPlanDetailId = 1, StepOrder = 9, NC = "TB", WtsTaskCode = "9t" });
            // [TARO-NC-MA 2026-11-06] đáp án đổi theo luật mới: Taro trùng theo CẶP (mã, NC) — 9T ở NC TA + NC TB là hợp lệ (khách chốt B 02/11);
            //   thêm 9T thứ 2 ở NC TA → mới là trùng
            db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 813, KhPlanDetailId = 1, StepOrder = 10, NC = " ta", WtsTaskCode = "9T " });
            db.Fixup();
            var r2 = await QuetCore.ScanAsync(db, led);
            var mt = r2.Findings.Where(f => f.Kind == QuetCore.MaTrung).Select(f => f.Detail).ToList();
            Ok(mt.Count == 2 && mt.Any(d => d.Contains("Đóng gói có 2 dòng chính cùng mã PX")) && mt.Any(d => d.Contains("Taro có 2 dòng chính cùng mã 9T (NC TA)")),
               "Bắt MA_TRUNG: Đóng gói PX ×2 (dòng dự phòng không tính), Taro theo cặp (mã CV, NC): 9T ở NC TA ×2 (9T ở NC TB không tính — TARO-NC-MA)", string.Join(" | ", mt));
        }
        Console.WriteLine("[Q3] [N3-FIRST] NC đầu chuỗi Gia công là máy loại trừ → LOAI_TRU_DAU (PO) + danh sách Part Master; NC loại trừ giữa / cuối không báo");
        {
            var db = Seed(true); var led = new HandoverLedgerService(db);
            var r0 = await QuetCore.ScanAsync(db, led);
            Ok(!r0.Findings.Any(f => f.Kind == QuetCore.LoaiTruDau) && r0.PartFindings.Count == 0, "Chưa có máy loại trừ: không báo LOAI_TRU_DAU",
               string.Join(" | ", r0.Findings.Where(f => f.Kind == QuetCore.LoaiTruDau).Select(f => f.Detail).Concat(r0.PartFindings)));
            db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 1, KhPlanDetailId = 1, SoMay = "M2" });
            db.Fixup();
            var r1 = await QuetCore.ScanAsync(db, led);
            Ok(!r1.Findings.Any(f => f.Kind == QuetCore.LoaiTruDau), "NC loại trừ đứng CUỐI (NC2 máy M2): không báo LOAI_TRU_DAU (thuộc HL-X)",
               string.Join(" | ", r1.Findings.Where(f => f.Kind == QuetCore.LoaiTruDau).Select(f => f.Detail)));
            db.KhPlanRouteSnapshotMachineExclude.List.Clear();
            db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 2, KhPlanDetailId = 1, SoMay = " m1 " });
            db.Fixup();
            var r2 = await QuetCore.ScanAsync(db, led);
            var lt = r2.Findings.Where(f => f.Kind == QuetCore.LoaiTruDau).Select(f => f.Detail).ToList();
            Ok(lt.Count == 1 && lt[0].Contains("NC NC1 (máy M1") && lt[0].Contains("NC thường đầu tiên: NC NC2") && lt[0].Contains("Nhận vào (chưa qua NC)") && r2.Findings.First(f => f.Kind == QuetCore.LoaiTruDau).Po == "PO1",
               "NC đầu NC1 chạy máy loại trừ M1 (snapshot ghi \" m1 \" — bỏ khoảng trắng, không phân biệt hoa thường): báo 1 dòng LOAI_TRU_DAU, nêu NC thường đầu tiên NC2 + hàng ở \"Nhận vào (chưa qua NC)\" (chữ đổi 04/11 [N3-FIRST])", string.Join(" | ", lt));
            // Part Master theo danh sách máy loại trừ hiện tại: chỉ Part đang dùng có NC chính đầu tiên chạy máy loại trừ
            db.MachineExcludeSettings.List.Add(new MachineExcludeSetting { Id = 1, SoMay = "MX" });
            var cust = db.Customers.List.First();
            void Part(int id, string no, bool obsolete, params (string nc, string may, bool backup)[] st)
            {
                db.PartMasters.List.Add(new PartMaster { PartId = id, PartNo = no, IsActive = true, IsObsolete = obsolete, CustomerId = cust.CustomerId });
                int o = 1;
                foreach (var s in st)
                    db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = id * 10 + o, PartId = id, StepOrder = o++, NC = s.nc, MachineRegistered = s.may, IsBackup = s.backup, IsActive = true });
            }
            Part(51, "P-DAU", false, ("1", "MX", false), ("2", "M2", false));
            Part(52, "P-GIUA", false, ("1", "M1", false), ("2", "MX", false));
            Part(53, "P-BO", true, ("1", "MX", false), ("2", "M2", false));
            Part(54, "P-DUPHONG", false, ("1", "M1", false), ("1b", "MX", true));
            db.Fixup();
            var r3 = await QuetCore.ScanAsync(db, led);
            Ok(r3.PartFindings.Count == 1 && r3.PartFindings[0].StartsWith("Part P-DAU ") && r3.PartFindings[0].Contains("NC 1 (máy MX)") && r3.PartFindings[0].Contains("NC sau: 2"),
               "Part Master: chỉ P-DAU (NC đầu máy MX) — không báo P-GIUA (loại trừ giữa), P-BO (đã bỏ), P-DUPHONG (dòng dự phòng)", string.Join(" | ", r3.PartFindings));
            var sw = new StringWriter(); QuetCore.Write(r3, sw, "GIA");
            Ok(sw.ToString().Contains("── Part Master: 1 chi tiết có NC đầu Gia công là máy loại trừ") && sw.ToString().Contains("[LOAI_TRU_DAU]"),
               "File kết quả có mục PO [LOAI_TRU_DAU] và mục Part Master", sw.ToString().Split('\n').FirstOrDefault(l => l.Contains("Part Master")));
        }
        Console.WriteLine("[Q4] [PM-T4][PM-NC-AT 05/11] Part Master: mã chính trùng đang dùng (PM_TRUNG, Taro không xét, dự phòng / đã xoá không tính) + mã bắt đầu \"@\" (PM_MA_A)");
        {
            var db = Seed(true); var led = new HandoverLedgerService(db);
            var r0 = await QuetCore.ScanAsync(db, led);
            Ok(r0.PmFindings.Count == 0, "Part Master sạch: không báo PM_TRUNG / PM_MA_A", string.Join(" | ", r0.PmFindings));
            var cust = db.Customers.List.First();
            db.PartMasters.List.Add(new PartMaster { PartId = 61, PartNo = "P-TRUNG", IsActive = true, CustomerId = cust.CustomerId });
            db.PartMasters.List.Add(new PartMaster { PartId = 62, PartNo = "P-SACH", IsActive = true, CustomerId = cust.CustomerId });
            // P-TRUNG: Bavia 2V ×2 (chính, 1 dòng có khoảng trắng / chữ thường) → báo; Rửa 1R chính + 1R dự phòng → không; KCS K1 chính + K1 đã xoá → không; Taro 1T ×2 → không (TARO-NC-MA)
            db.PartBaviaSteps.List.Add(new PartBaviaStep { StepId = 611, PartId = 61, StepOrder = 1, NC = "2V", IsActive = true });
            db.PartBaviaSteps.List.Add(new PartBaviaStep { StepId = 612, PartId = 61, StepOrder = 2, NC = " 2v ", IsActive = true });
            db.PartWashingSteps.List.Add(new PartWashingStep { StepId = 613, PartId = 61, StepOrder = 1, NC = "1R", IsActive = true });
            db.PartWashingSteps.List.Add(new PartWashingStep { StepId = 614, PartId = 61, StepOrder = 2, NC = "1R", IsBackup = true, ParentNC = "1R", IsActive = true });
            db.PartInspectionSteps.List.Add(new PartInspectionStep { StepId = 615, PartId = 61, StepOrder = 1, NC = "K1", IsActive = true });
            db.PartInspectionSteps.List.Add(new PartInspectionStep { StepId = 616, PartId = 61, StepOrder = 2, NC = "K1", IsActive = false });
            db.PartTaroSteps.List.Add(new PartTaroStep { StepId = 617, PartId = 61, StepOrder = 1, NC = "3", WtsTaskCode = "1T", IsActive = true });
            db.PartTaroSteps.List.Add(new PartTaroStep { StepId = 618, PartId = 61, StepOrder = 2, NC = "5", WtsTaskCode = "1T", IsActive = true });
            // P-SACH: mã bắt đầu "@" ở Gia công (dự phòng) + Taro mã CV → báo PM_MA_A, không báo PM_TRUNG
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 621, PartId = 62, StepOrder = 1, NC = "NC1", IsActive = true });
            db.PartMachiningSteps.List.Add(new PartMachiningStep { StepId = 622, PartId = 62, StepOrder = 2, NC = " @VAO", IsBackup = true, ParentNC = "NC1", IsActive = true });
            db.PartTaroSteps.List.Add(new PartTaroStep { StepId = 623, PartId = 62, StepOrder = 1, NC = "1", WtsTaskCode = "@T", IsActive = true });
            db.PartPackagingSteps.List.Add(new PartPackagingStep { StepId = 624, PartId = 62, StepOrder = 1, NC = "@X", IsActive = false });   // đã xoá: không báo
            db.Fixup();
            var r = await QuetCore.ScanAsync(db, led);
            var tr = r.PmFindings.Where(f => f.StartsWith("[" + QuetCore.PmTrung)).ToList();
            var at = r.PmFindings.Where(f => f.StartsWith("[" + QuetCore.PmMaA)).ToList();
            Ok(tr.Count == 1 && tr[0].Contains("Part P-TRUNG") && tr[0].Contains("Bavia: 2 dòng chính cùng mã \"2V\"") && tr[0].Contains("611") && tr[0].Contains("612"),
               "PM_TRUNG: chỉ Bavia 2V ×2 của P-TRUNG (Rửa dự phòng, KCS đã xoá, Taro mã CV chung không báo)", string.Join(" | ", tr));
            Ok(at.Count == 2 && at.All(f => f.Contains("Part P-SACH")) && at.Any(f => f.Contains("622") && f.Contains("dự phòng")) && at.Any(f => f.Contains("623") && f.Contains("@T")),
               "PM_MA_A: dự phòng \" @VAO\" Gia công + Taro mã CV \"@T\" (dòng đã xoá không báo)", string.Join(" | ", at));
            var sw = new StringWriter(); QuetCore.Write(r, sw, "GIA");
            Ok(sw.ToString().Contains("── Part Master: 1 nhóm mã chính trùng (PM_TRUNG, Taro không xét) · 2 dòng mã bắt đầu \"@\" (PM_MA_A)"),
               "File kết quả có mục Part Master PM_TRUNG / PM_MA_A", sw.ToString().Split('\n').FirstOrDefault(l => l.Contains("PM_TRUNG")));
        }
        Console.WriteLine("[Q5] [QUET-VU-SOLE 06/11] vụ lỗi cân (VU_LECH) + số lẻ (SO_LE, B3)");
        {
            var db = Seed(true); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1")).Ok, "Kho → GC 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "GC nhận 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 14, 2, 1, 1)).Ok, "WTS NC1 OK 14 NG 2 (sinh vụ)");
            var r0 = await QuetCore.ScanAsync(db, led);
            var row0 = (await led.GetLedgerForDetailAsync(1))!;
            Ok(row0.Cases.Count == 1 && !r0.Findings.Any(f => f.Kind == QuetCore.VuLech || f.Kind == QuetCore.SoLe),
               "Vụ đi đúng đường: 1 vụ, không báo VU_LECH / SO_LE", string.Join(" | ", r0.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
            var sw0 = new StringWriter(); QuetCore.Write(r0, sw0, "GIA");
            Ok(sw0.ToString().Contains("Vụ lỗi cân (VU_LECH): mọi vụ cân") && sw0.ToString().Contains("Số lẻ (SO_LE, B3): không có dòng nào"),
               "File kết quả in rõ 2 mục mới khi không có gì", string.Join(" / ", sw0.ToString().Split('\n').Where(l => l.Contains("VU_LECH") || l.Contains("SO_LE"))));
            // Số lẻ ghi thẳng như dữ liệu cũ: SL PO 16.5, WTS NC2 OK 1.5 (dòng đã huỷ vẫn xét), lần nhận NG 0.25
            db.KhPlanDetails.List.First().Quantity = 16.5m;
            db.WtsProductionLogs.List.Add(new WtsProductionLog { WtsLogId = 950, KhPlanDetailId = 1, ProcessGroup = "GC", NC = "NC2", QtyDone = 1.5m, QtyOk = 1.5m, QtyNg = 0, WorkerId = 1, IsVoided = true, StartTime = DateTime.Now, EndTime = DateTime.Now });
            db.HandoverReceives.List.First().QtyNg = 0.25m;
            db.Fixup();
            var r1 = await QuetCore.ScanAsync(db, led);
            var le = r1.Findings.Where(f => f.Kind == QuetCore.SoLe).Select(f => f.Detail).ToList();
            Ok(le.Count == 3 && le.Any(d => d.Contains("SL PO 16.5")) && le.Any(d => d.Contains("#950") && d.Contains("(đã huỷ)")) && le.Any(d => d.StartsWith("lần nhận #") && d.Contains("NG 0.25"))
               && r1.Findings.Where(f => f.Kind == QuetCore.SoLe).All(f => f.Po == "PO1"),
               "SO_LE: đúng 3 chỗ (SL PO, dòng WTS đã huỷ, lần nhận) và ghi đúng PO", string.Join(" | ", le));
            var sw1 = new StringWriter(); QuetCore.Write(r1, sw1, "GIA");
            Ok(sw1.ToString().Contains("Số lẻ (SO_LE, B3): CÓ"), "File kết quả báo CÓ số lẻ");
        }
        // [QUET-PHIEU-CHO 2026-11-14] Q6: (a) phiếu chờ nhận chỉ định ô đạt từ ô Hàng lỗi trên PO bật Workflow → PHIEU_CHO_O_DAT (OFF không báo, nhận dở vẫn báo phần còn,
        //   nhận xong không báo, phiếu Hàng mới thường không báo); (b) "Trả về Gia công theo NC" ghi thẳng tới Kho → TRA_VE_NHOM_LA, tới Gia công không báo.
        //   Nguồn: CLAUDE (sổ G16-PHIEU-CHO + G20-GHI-NHO 1); HÙNG 14/11 08:28 OK Gói 21.
        Console.WriteLine("[Q6] [QUET-PHIEU-CHO 14/11] phiếu chờ nhận chỉ định ô đạt từ ô Hàng lỗi (PHIEU_CHO_O_DAT) + trả về theo NC tới nhóm lạ (TRA_VE_NHOM_LA)");
        {
            var db = Seed(false); var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
            var kho = new KhoVatLieuService(db, new UserService(null));
            WtsSvcHelp.Reset();
            await kho.SaveKhoInputAsync(1, DateTime.Now, 16, "OVL", "A1", null, null, null, "nhan phoi", 1);
            Ok((await svc.IssueByCellAsync(1, "KHO", "GC", 16, 1, "MOI", false, "MOI", null, "NC1")).Ok, "Kho → GC 16");
            Ok((await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 16, 0, null, 1)).Ok, "GC nhận 16");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 12, 4, 1, 1)).Ok, "WTS NC1 OK 12 NG 4 (sinh vụ)");
            Ok((await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 4, 0, 1)).Ok, "WTS NC2 OK 4");
            var vu = db.NgCases.List.Last().NgCaseId;
            var a = await svc.IssueByCellAsync(1, "GC", "HTSP", 3, 1, "LOI", false, "XL", ngCaseId: vu);
            var txA = db.HandoverTransactions.List.Last().HandoverTxId;
            var cm = await svc.IssueByCellAsync(1, "GC", "HTSP", 1, 1, "LOI", false, "MOI", ngCaseId: vu);
            var txC = db.HandoverTransactions.List.Last().HandoverTxId;
            var bm = await svc.IssueByCellAsync(1, "GC", "HTSP", 4, 1, "MOI", false, "MOI");
            Ok(a.Ok && cm.Ok && bm.Ok, "Workflow OFF: GC → HTSP 3 từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý + 1 từ ô Hàng lỗi chỉ định ô Hàng mới + phiếu Hàng mới 4 (cả 3 chờ nhận)", $"{a.Error} {cm.Error} {bm.Error}");
            var r0 = await QuetCore.ScanAsync(db, led);
            Ok(!r0.Findings.Any(f => f.Kind == QuetCore.PhieuChoODat || f.Kind == QuetCore.TraVeLa), "Q6a OFF: không báo PHIEU_CHO_O_DAT (OFF nhận vào ô đạt được)",
               string.Join(" | ", r0.Findings.Select(f => $"[{f.Kind}] {f.Detail}")));
            db.KhPlans.List.First().ApplyWorkflow = true; db.Fixup();
            var r1 = await QuetCore.ScanAsync(db, led);
            var pc = r1.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat).ToList();
            Ok(pc.Count == 2 && pc.Any(f => f.Detail.StartsWith($"phiếu #{txA} Gia công → HTSP lấy từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý, còn chờ nhận 3") && f.Po == "PO1")
               && pc.Any(f => f.Detail.StartsWith($"phiếu #{txC} Gia công → HTSP lấy từ ô Hàng lỗi chỉ định ô Hàng mới, còn chờ nhận 1")),
               "Q6b bật ON: đúng 2 phiếu (ô chỉ định Hàng lỗi đã xử lý + Hàng mới; phiếu Hàng mới thường không báo), ghi ô lấy / ô chỉ định / còn chờ nhận", string.Join(" | ", pc.Select(f => f.Detail)));
            var rv = await svc.ReceiveAsync(txA, 0, 1, null, 1, toCell: HandoverCell.Loi, toCellReason: "workflow on");
            var r2 = await QuetCore.ScanAsync(db, led);
            var pc2 = r2.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat && f.Detail.StartsWith($"phiếu #{txA} ")).ToList();
            Ok(rv.Ok && pc2.Count == 1 && pc2[0].Detail.Contains("còn chờ nhận 2"), "Q6c HTSP nhận 1 vào ô Hàng lỗi → còn báo phần chờ 2", $"{rv.Error} · " + string.Join(" | ", pc2.Select(f => f.Detail)));
            var rv2 = await svc.ReceiveAsync(txA, 0, 2, null, 1, toCell: HandoverCell.Loi, toCellReason: "workflow on");
            var r3 = await QuetCore.ScanAsync(db, led);
            Ok(rv2.Ok && r3.Findings.Count(f => f.Kind == QuetCore.PhieuChoODat) == 1 && r3.Findings.Any(f => f.Kind == QuetCore.PhieuChoODat && f.Detail.StartsWith($"phiếu #{txC} ")),
               "Q6d phiếu #A nhận hết vào ô Hàng lỗi → hết báo, còn phiếu ô Hàng mới", rv2.Error + " · " + string.Join(" | ", r3.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat).Select(f => f.Detail)));
            // Q6f–Q6h (rà độc lập Gói 21 Trung 1 + Thấp 7): phiếu cũ không ghi ô (ToCell NULL, ô chỉ định suy theo loại phiếu như máy chủ), phiếu lấy từ ô Hàng sửa,
            //   lần nhận đã huỷ làm số chờ tăng lại — ghi thẳng DB (dữ liệu cũ / hiếm), chỉ để thử bộ quét
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9701, KhPlanDetailId = 1, FromGroupCode = "GC", ToGroupCode = "HTSP", QtyIssued = 1, Purpose = HandoverPurpose.RepairReturn, Status = "PENDING", IssuedBy = 1, IssuedAt = DateTime.Now });
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9702, KhPlanDetailId = 1, FromGroupCode = "GC", ToGroupCode = "HTSP", QtyIssued = 1, Purpose = HandoverPurpose.RepairReturn, FromCell = HandoverCell.Sua, ToCell = HandoverCell.Moi, Status = "PENDING", IssuedBy = 1, IssuedAt = DateTime.Now });
            db.Fixup();
            var rf = await QuetCore.ScanAsync(db, led);
            var pf = rf.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat).ToList();
            Ok(pf.Any(f => f.Detail.StartsWith("phiếu #9701 ") && f.Detail.Contains("chỉ định ô Hàng lỗi đã xử lý (phiếu cũ không ghi ô), còn chờ nhận 1")),
               "Q6f phiếu \"Trả hàng đã sửa\" cũ không ghi ô (ToCell NULL) → ô chỉ định suy như máy chủ (Hàng lỗi đã xử lý) → báo", string.Join(" | ", pf.Select(f => f.Detail)));
            Ok(pf.Any(f => f.Detail.StartsWith("phiếu #9702 Gia công → HTSP lấy từ ô Hàng sửa chỉ định ô Hàng mới, còn chờ nhận 1")),
               "Q6g phiếu lấy từ ô Hàng sửa chỉ định ô Hàng mới → báo", string.Join(" | ", pf.Select(f => f.Detail)));
            foreach (var id in new long[] { 9701, 9702 }) db.HandoverTransactions.List.First(x => x.HandoverTxId == id).IsVoided = true;
            var rcC = db.HandoverReceives.List.Where(x => x.HandoverTxId == txC).ToList();
            if (rcC.Count == 0)
            {
                Ok((await svc.ReceiveAsync(txC, 0, 1, null, 1, toCell: HandoverCell.Loi, toCellReason: "workflow on")).Ok, "HTSP nhận hết phiếu ô Hàng mới vào ô Hàng lỗi");
                rcC = db.HandoverReceives.List.Where(x => x.HandoverTxId == txC).ToList();
            }
            var rg = await QuetCore.ScanAsync(db, led);
            Ok(!rg.Findings.Any(f => f.Kind == QuetCore.PhieuChoODat), "Q6h(1) nhận hết + 2 phiếu ghi thẳng đã huỷ → không báo", string.Join(" | ", rg.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat).Select(f => f.Detail)));
            foreach (var r in rcC) r.IsVoided = true;
            db.HandoverTransactions.List.First(x => x.HandoverTxId == txC).Status = "PENDING"; db.Fixup();
            var rh = await QuetCore.ScanAsync(db, led);
            var ph = rh.Findings.Where(f => f.Kind == QuetCore.PhieuChoODat).ToList();
            Ok(ph.Count == 1 && ph[0].Detail.StartsWith($"phiếu #{txC} ") && ph[0].Detail.Contains("còn chờ nhận 1"), "Q6h(2) lần nhận bị huỷ → phiếu chờ lại, số chờ tính lại 1 (không tính lần nhận đã huỷ)",
               string.Join(" | ", ph.Select(f => f.Detail)));
            foreach (var r in rcC) r.IsVoided = false;
            db.HandoverTransactions.List.First(x => x.HandoverTxId == txC).Status = "COMPLETED"; db.Fixup();
            // (b) ghi thẳng: HTSP → Kho "Trả về Gia công theo NC" (máy chủ chặn) + HTSP → GC (đúng đường) — chỉ dòng tới Kho bị báo
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9601, KhPlanDetailId = 1, FromGroupCode = "HTSP", ToGroupCode = "KHO", QtyIssued = 1, Purpose = HandoverPurpose.HelpReturn, FromNC = "NC2", Status = "COMPLETED", IssuedBy = 1, IssuedAt = DateTime.Now });
            db.HandoverTransactions.List.Add(new HandoverTransaction { HandoverTxId = 9602, KhPlanDetailId = 1, FromGroupCode = "HTSP", ToGroupCode = "GC", QtyIssued = 1, Purpose = HandoverPurpose.HelpReturn, FromNC = "NC2", Status = "COMPLETED", IssuedBy = 1, IssuedAt = DateTime.Now });
            db.Fixup();
            var r4 = await QuetCore.ScanAsync(db, led);
            var tv = r4.Findings.Where(f => f.Kind == QuetCore.TraVeLa).ToList();
            Ok(tv.Count == 1 && tv[0].Detail.StartsWith("phiếu #9601 HTSP → Kho \"Trả về Gia công theo NC\" 1"), "Q6e trả về theo NC tới Kho (ghi thẳng) → TRA_VE_NHOM_LA; tới Gia công không báo",
               string.Join(" | ", tv.Select(f => f.Detail)));
            var sw = new StringWriter(); QuetCore.Write(r4, sw, "GIA"); var tx = sw.ToString();
            Ok(tx.Contains("(PHIEU_CHO_O_DAT, lọc trước khi lên máy khách): 0 phiếu · 0 PO") && tx.Contains("(TRA_VE_NHOM_LA, ghi ngoài phần mềm): CÓ — báo Claude"),
               "File kết quả in 2 mục mới", string.Join(" / ", tx.Split('\n').Where(l => l.Contains("PHIEU_CHO") || l.Contains("TRA_VE"))));
        }
        Console.WriteLine(fails == 0 ? "\nKẾT QUẢ: ĐẠT hết" : $"\nKẾT QUẢ: SAI {fails}");
        return fails == 0 ? 0 : 1;
    }
}
