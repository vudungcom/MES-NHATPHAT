// [A2-SCEN 2026-11-19] Kịch bản `a2` — viết lại từ mô tả project doc claude/quet-fable/2026-11-15-agent-a2-soluat-workflow.md (bản gốc lượt 2 quét Fable
//   chỉ nằm ở /tmp sandbox, đã mất). 4 ca Sổ luật ↔ code / Workflow ON-OFF bằng hàm THẬT của service trên DB giả (EfStub):
//   A2-01 WTS-NGAYCU-GO — [L7-WTS-CU] Gói 25: chuỗi gỡ K5 đi qua dòng WTS hôm qua: người nhập bị chặn (chữ cũ), Admin xoá có lý do → gỡ được, K5 vẫn giữ
//     (bản 7439e3e Gói 24: Admin cũng bị chặn "chính mình" / "hôm nay" → kẹt vĩnh viễn, chỉ SQL gỡ — đúng là SAI trên bản cũ);
//   A2-02 OFF-TRAN-PHIEU — Workflow OFF vẫn chặn trần lập phiếu (N7 chữ mới), WTS NC2 khi NC1 = 0 lưu được + sổ cảnh báo;
//   A2-03 WF-ON-OFF-ON — ON huỷ lần nhận bị K5 → tắt WF huỷ được (NC1 âm) → bật ON bị K7 → xoá WTS → bật được;
//   A2-04 K7-PHIEU-CHO — OFF giao từ ô Hàng lỗi chỉ định ô XL → bật ON (K7 cho) → bên nhận ô đạt bị chặn (Gói 16), nhận ô Hàng lỗi + lý do được.
//   Nguồn: CLAUDE quét Fable 15/11 (GĐ3 A.3 / A.4 / A.9, bảng phát hiện A2-01…A2-04); HÙNG 15/11 15:32 chốt A (L7 WTS) → Gói 25.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class A2Scen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (!string.IsNullOrEmpty(info) ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }
    static readonly DateTime T = DateTime.Today.AddHours(8);
    static readonly string[] All = { "KHO", "GC", "HTSP", "KCS", "PKG" };

    /// <summary>Dòng WTS GC NC1 ngày hôm qua của người 2 (ghi thẳng DB — ngày cũ không lưu được qua service).</summary>
    static WtsProductionLog OldRow(AppDbContext db, long id, int detailId, int ok, string machine = "M1")
    {
        var y = T.AddDays(-1);
        var w = new WtsProductionLog { WtsLogId = id, KhPlanDetailId = detailId, ProcessGroup = "GC", NC = "NC1", MachineUsed = machine, WorkerId = 2, QtyDone = ok, QtyOk = ok,
                                       StartTime = y, EndTime = y.AddHours(1), CreatedAt = y.AddHours(1), ProductionMode = "serial" };
        db.WtsProductionLogs.List.Add(w); db.Fixup(); return w;
    }
    static long LastReceiveId(AppDbContext db, int detailId = 1)
    {
        var tx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == detailId).Select(t => t.HandoverTxId).ToHashSet();
        return db.HandoverReceives.List.Where(r => !r.IsVoided && tx.Contains(r.HandoverTxId)).OrderByDescending(r => r.ReceiveId).First().ReceiveId;
    }
    static bool Void(AppDbContext db, long id) => db.WtsProductionLogs.List.First(w => w.WtsLogId == id).IsVoided;

    public static async Task<int> Run()
    {
        Console.WriteLine("[A2-01] WTS-NGAYCU-GO — [L7-WTS-CU] chuỗi gỡ K5 đi qua dòng WTS hôm qua: người nhập bị chặn (chữ cũ); Admin / Sửa WTS_MASTER xoá có lý do → gỡ được; K5 vẫn giữ");
        {
            var db = await WtsScen.SeedAsync(); var svc = new HandoverService(db); var wsvc = WtsScen.Svc(db);
            var old = OldRow(db, 900, 1, 10);                               // hôm qua: NC1 OK 10 (u2)
            var w2 = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC2", null, 10, 0, 2);   // hôm nay: NC2 OK 10 dùng số NC1
            Ok(w2.Ok, "Seed: NC1 OK 10 hôm qua (u2) + NC2 OK 10 hôm nay (u2), PO1 ON", w2.Err);
            var v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 1, UserId = 2 });
            Ok(!v.Ok && v.ErrorMsg == "Chỉ được xoá WTS của ngày hôm nay. Ngày cũ cần quyền cao hơn." && !Void(db, 900), "1 Người nhập (u2) xoá dòng hôm qua của mình: chặn chữ cũ", v.ErrorMsg);
            v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 1, UserId = 3 });
            Ok(!v.Ok && v.ErrorMsg == "Chỉ xoá được dòng WTS của chính mình." && !Void(db, 900), "2 Người khác không quyền cao hơn (u3) xoá: chặn chữ cũ", v.ErrorMsg);
            var vr = await svc.VoidReceiveAsync(LastReceiveId(db), "thử gỡ", 1, All);
            Ok(!vr.Ok && vr.Error.Contains("ngày trước hôm nay") && vr.Error.Contains("Sửa WTS tiêu chuẩn"), "3 Admin huỷ lần nhận Kho → GC: K5 chặn + gợi ý trỏ dòng ngày cũ → nhờ Admin / Sửa WTS tiêu chuẩn (chữ mới)", vr.Error + " | ok=" + vr.Ok);
            v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 1, UserId = 1 });
            Ok(!v.Ok && v.ErrorMsg == WtsSaveService.CuReasonMsg && !Void(db, 900), "4 Admin xoá dòng hôm qua KHÔNG lý do: chặn (bắt lý do ≥ 3)", v.ErrorMsg);
            v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 1, UserId = 1, Reason = "gỡ K5 sau khi kiểm" });
            Ok(!v.Ok && (v.ErrorMsg ?? "").StartsWith("Không xoá được: xoá xong sẽ làm sai số ở Gia công NC2") && !Void(db, 900), "5 Admin xoá dòng hôm qua CÓ lý do nhưng NC2 đang dùng số: K5 vẫn chặn (quyền cao hơn không bỏ K5)", v.ErrorMsg);
            v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = w2.LogId, KhPlanDetailId = 1, UserId = 1, Reason = "gỡ từ cuối ngược lên" });
            Ok(v.Ok && Void(db, w2.LogId!.Value), "6 Admin xoá NC2 hôm nay của u2 (có lý do): được", v.ErrorMsg);
            v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = 900, KhPlanDetailId = 1, UserId = 1, Reason = "gỡ K5 sau khi kiểm" });
            Ok(v.Ok && Void(db, 900) && old.VoidedBy == 1 && old.VoidReason == "Xóa dòng (quyền cao hơn): gỡ K5 sau khi kiểm", "7 Admin xoá dòng hôm qua (có lý do): được — VoidedBy Admin, lý do ghi vào VoidReason (bản Gói 24: chặn → kẹt, chỉ SQL gỡ)", v.ErrorMsg);
            vr = await svc.VoidReceiveAsync(LastReceiveId(db), "gỡ xong", 1, All);
            var led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            Ok(vr.Ok && led != null && led.Balanced && led.G("GC").NhanTong == 0, "8 Huỷ lần nhận Kho → GC: được, sổ PO1 cân, GC Nhận = 0 (chuỗi gỡ thông suốt trên giao diện)", vr.Error + (led == null ? "" : $" | GC Nhận {led.G("GC").NhanTong}"));
        }

        Console.WriteLine("[A2-02] OFF-TRAN-PHIEU — Workflow OFF vẫn chặn trần lập phiếu (N7 chữ mới 15/11); WTS NC2 khi NC1 = 0 lưu được + sổ cảnh báo");
        {
            var db = await WtsScen.SeedAsync(); var svc = new HandoverService(db);
            var i = await svc.IssueByCellAsync(2, "GC", "HTSP", 1, 2, "MOI", false, "MOI");
            Ok(!i.Ok && i.Error.Contains("tối đa 0"), "PO2 (OFF) GC Đã làm 0 → giao Hàng mới 1 cho HTSP: chặn \"tối đa 0\" (OFF vẫn kiểm trần lập phiếu)", i.Error);
            var w = await WtsSvcHelp.SaveAsync(db, 2, "GC", "NC2", null, 1, 0, 2);
            var led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(2);
            Ok(w.Ok && led != null && led.Balanced, "PO2 (OFF) WTS NC2 OK 1 khi NC1 = 0: lưu được (N7: OFF không kiểm trần WTS), sổ vẫn cân", w.Err);
            Ok(led != null && led.G("GC").Warnings.Count > 0, "  sổ GC có cảnh báo (ô âm NC2 chưa làm −1 lúc OFF)", led == null ? "null" : string.Join(" | ", led.G("GC").Warnings));
            var i2 = await svc.IssueByCellAsync(2, "GC", "HTSP", 1, 2, "MOI", false, "MOI");
            Ok(i2.Ok, "  GC Đã làm 1 → giao Hàng mới 1 cho HTSP: được", i2.Error);
        }

        Console.WriteLine("[A2-03] WF-ON-OFF-ON — ON huỷ lần nhận bị K5 → tắt WF: huỷ được (NC1 âm) → bật ON bị K7 → xoá WTS → bật ON được");
        {
            var db = await WtsScen.SeedAsync(); var svc = new HandoverService(db); var kh = new KhPlanService(db); var wsvc = WtsScen.Svc(db);
            var w = await WtsSvcHelp.SaveAsync(db, 1, "GC", "NC1", null, 10, 0, 2);
            Ok(w.Ok, "PO1 ON: NC1 OK 10", w.Err);
            var rid = LastReceiveId(db);
            var vr = await svc.VoidReceiveAsync(rid, "thử", 1, All);
            Ok(!vr.Ok && vr.Error.Contains("K5"), "ON: huỷ lần nhận Kho → GC bị K5 chặn", vr.Error);
            var off = await kh.SetApplyWorkflowAsync(1, false, 1, "thử tắt");
            Ok(off == null, "Tắt Workflow PO1: được (ON → OFF không kiểm gì)", off);
            vr = await svc.VoidReceiveAsync(rid, "huỷ lúc OFF", 1, All);
            var led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            Ok(vr.Ok && led != null && led.Balanced && led.G("GC").Steps.First(s => s.Code == "NC1").Waiting == -10, "OFF: huỷ lần nhận được → GC NC1 chưa làm = −10 (ô âm, chỉ cảnh báo), sổ vẫn cân", vr.Error + (led == null ? "" : $" | NC1 chờ {led.G("GC").Steps.First(s => s.Code == "NC1").Waiting}"));
            var on = await kh.SetApplyWorkflowAsync(1, true, 1, "thử bật");
            Ok(on != null && on.Contains("âm"), "Bật ON khi có ô âm: K7 chặn", on);
            var v = await wsvc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = w.LogId, KhPlanDetailId = 1, UserId = 2 });
            Ok(v.Ok, "u2 xoá dòng WTS NC1 của mình (OFF, hôm nay): được", v.ErrorMsg);
            on = await kh.SetApplyWorkflowAsync(1, true, 1, "bật lại");
            led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(1);
            Ok(on == null && led != null && led.Balanced && led.G("GC").NhanTong == 0, "Bật ON sau khi xoá WTS: được, sổ cân, GC Nhận 0", on);
        }

        Console.WriteLine("[A2-04] K7-PHIEU-CHO — OFF: giao từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý → bật ON (K7 cho) → bên nhận ô đạt bị chặn (Gói 16), nhận ô Hàng lỗi + lý do được");
        {
            var db = await WtsScen.SeedAsync(); var svc = new HandoverService(db); var kh = new KhPlanService(db);
            var w = await WtsSvcHelp.SaveAsync(db, 2, "GC", "NC1", null, 0, 2, 2, catId: 1);
            var caseId = db.NgCases.List.Last().NgCaseId;
            Ok(w.Ok && caseId > 0, "PO2 (OFF) GC NC1 NG 2 → vụ lỗi sinh", w.Err);
            var i = await svc.IssueByCellAsync(2, "GC", "HTSP", 2, 2, "LOI", false, "XL", ngCaseId: caseId);
            Ok(i.Ok, "OFF: GC giao 2 từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý cho HTSP: được (OFF không chặn Gói 16)", i.Error);
            var txId = db.HandoverTransactions.List.Last().HandoverTxId;
            var on = await kh.SetApplyWorkflowAsync(2, true, 1, "bật");
            Ok(on == null, "Bật ON PO2: được (K7 chỉ xét ô âm — phiếu chờ không tính; G16-PHIEU-CHO + QuetSo PHIEU_CHO_O_DAT quét)", on);
            var r = await svc.ReceiveAsync(txId, 2, 0, null, 3);
            Ok(!r.Ok && r.Error.StartsWith("Workflow ON: phiếu này lấy từ ô Hàng lỗi"), "ON: HTSP nhận vào ô Hàng lỗi đã xử lý: chặn (Gói 16 SUA-QUA-KCS)", r.Error);
            r = await svc.ReceiveAsync(txId, 0, 2, "phiếu lập lúc OFF", 3, toCell: "LOI", toCellReason: "phiếu lập lúc OFF — nhận vào ô Hàng lỗi");   // ô Hàng lỗi: số nhận ghi ở ô NG
            var led = await new HandoverLedgerService(db).GetLedgerForDetailAsync(2);
            Ok(r.Ok && led != null && led.Balanced && led.G("HTSP").NhanLoi == 2, "ON: HTSP nhận vào ô Hàng lỗi + lý do: được, HTSP Nhận·Hàng lỗi = 2, sổ cân (đường gỡ có)", r.Error + (led == null ? "" : $" | HTSP NhanLoi {led.G("HTSP").NhanLoi}"));
        }

        // chuỗi kết quả phải đúng "KẾT QUẢ: ĐẠT hết" — Nang.cs so chuỗi này trên trạm kiem (bài học a1 15/11)
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
