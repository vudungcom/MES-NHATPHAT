// [N22-SCEN 2026-11-25] Kịch bản cố định "n22": luật N22 — Sửa phiếu (bên giao, phiếu chưa nhận), Sửa lần nhận (bên nhận, chưa dùng hàng sau lần nhận),
//   Đổi loại lỗi (vụ chưa giao / giao chưa nhận). Nguồn: KHÁCH 23/11 (mockup D7 bước 1 / 2a / 2b / 3, ví dụ chat 16:19 "25 OK + 5 NG → 24 + 6");
//   HÙNG 23/11 16:24 (N22) + 25/11 21:39 OK bản "em hiểu" G-B (3 chi tiết tự chốt: NG thêm = phiếu trả mới cùng vụ; đã giao tiếp cũng chặn; chiếc trả sau WTS = vụ mới).
//   Đáp án độc lập: (1) "như làm đúng ngay từ đầu" — sổ sau khi sửa phải GIỐNG HỆT sổ của 1 DB khác chỉ ghi chứng từ với số cuối (FullSig: mọi ô + NC + vụ +
//   Đang ở đâu + khung lỗi); (2) chứng từ cũ còn đủ (huỷ, không xoá), lý do + người sửa; (3) chặn thì không ghi gì (FullSig không đổi).
//   Bằng hàm thật HandoverService (EditIssueAsync / EditReceiveAsync / ChangeCaseCategoryAsync / VoidReceiveAsync / IssueByCellAsync / ReceiveAsync) + sổ chung.
//   Sau MỖI bước ghi: Check() chung của kịch bản sua (cân PO, không ô âm, vụ cân, khung lỗi, dòng B cuối = sổ, Đang ở đâu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    const int NoRight = 8;   // người có tài khoản nhưng không có quyền Sửa nhóm nào (giao nhận)

    static AppDbContext SeedN22(bool workflow = true)
    {
        var db = Seed(workflow: workflow);
        var g = new UserGroup { GroupId = 92, GroupCode = "NONE", GroupName = "Không quyền (thử)", IsActive = true, Permissions = FuzzPerm.Json() };
        db.UserGroups.List.Add(g);
        db.Users.List.Add(new User { UserId = NoRight, Username = "u8", FullName = "U8", IsActive = true, Group = g, GroupId = 92 });
        db.Fixup();
        return db;
    }

    /// <summary>Kho → Gia công 20 (ô Hàng mới), Gia công nhận ok + ng (ng &gt; 0: loại lỗi 1 → vụ mới, phiếu trả tự lập). Trả lần nhận.</summary>
    static async Task<HandoverReceive> PreRecv(AppDbContext db, HandoverService svc, decimal ok, decimal ng, decimal issue = 20)
    {
        var i = await svc.IssueByCellAsync(1, "KHO", "GC", issue, 1, "MOI", false, "MOI");
        var r = await svc.ReceiveAsync(LastTx(db), ok, ng, null, Gc, ngCategoryId: ng > 0 ? 1 : null);
        Ok(i.Ok && r.Ok, $"dựng: Kho → Gia công {issue}, Gia công nhận {ok} OK + {ng} NG", i.Error + "|" + r.Error);
        return db.HandoverReceives.List.Where(x => !x.IsVoided).OrderBy(x => x.ReceiveId).Last();
    }
    static HandoverTransaction? Back(AppDbContext db, long receiveId) => db.HandoverTransactions.List.FirstOrDefault(t => !t.IsVoided && t.SourceReceiveId == receiveId);
    static List<HandoverReceive> ActiveRecv(AppDbContext db, long txId) => db.HandoverReceives.List.Where(r => r.HandoverTxId == txId && !r.IsVoided).OrderBy(r => r.ReceiveId).ToList();

    public static async Task<int> RunN22()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[F1] D7 bước 2a: Kho giao Gia công 20, nhận 15 OK + 5 NG → Sửa lần nhận 14 OK + 6 NG (Gia công chưa WTS) — phiếu trả cũ 5 giữ, thêm phiếu trả 1 cùng vụ");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            var b0 = Back(db, r0.ReceiveId)!;
            var vu = r0.NgCaseId!.Value;
            await Check(db, "F1 trước sửa");
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "Phát hiện thêm 1 chiếc lỗi", Gc);
            var c = await Check(db, "F1 sau sửa");
            var act = ActiveRecv(db, r0.HandoverTxId);
            var backs = db.HandoverTransactions.List.Where(t => !t.IsVoided && t.SourceReceiveId != null).OrderBy(t => t.HandoverTxId).ToList();
            Ok(e.Ok && e.Error == "" && r0.IsVoided && r0.VoidedBy == Gc && r0.VoidReason!.Contains("Phát hiện thêm")
               && act.Count == 2 && act.Sum(x => x.QtyOk) == 14 && act.Sum(x => x.QtyNg) == 6
               && act.All(x => HandoverService.N22Root(x.Notes) == r0.ReceiveId && x.ReceivedBy == r0.ReceivedBy && x.ReceivedAt == r0.ReceivedAt && x.NgCaseId == vu),
               "Lần nhận cũ huỷ (người sửa + lý do), 2 dòng mới cùng dấu nhóm [N22#gốc]: tổng 14 OK + 6 NG, giữ người nhận + giờ nhận gốc, cùng vụ",
               e.Error + " | " + string.Join(" ; ", act.Select(x => $"#{x.ReceiveId} {x.QtyOk}+{x.QtyNg} {x.Notes} vụ {x.NgCaseId}")));
            Ok(backs.Count == 2 && backs[0].HandoverTxId == b0.HandoverTxId && backs[0].QtyIssued == 5 && backs[0].SourceReceiveId == act[0].ReceiveId
               && backs[1].QtyIssued == 1 && backs[1].SourceReceiveId == act[1].ReceiveId && backs[1].NgCaseId == vu && backs[1].ToGroupCode == "KHO" && backs[1].Status == "PENDING",
               "Phiếu trả cũ 5 giữ nguyên (nối sang dòng 1), phiếu trả MỚI 1 chiếc cùng vụ Gia công → Kho chờ Kho nhận (chi tiết (a))",
               string.Join(" ; ", backs.Select(t => $"#{t.HandoverTxId} {t.QtyIssued} src {t.SourceReceiveId} vụ {t.NgCaseId} {t.Status}")));
            // đáp án độc lập: như nhận 14 + 6 ngay từ đầu
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await PreRecv(dbO, svO, 14, 6);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ sau sửa = sổ của PO nhận đúng 14 OK + 6 NG ngay từ đầu (mọi ô, NC, vụ, Đang ở đâu, khung lỗi)",
               await FullSig(db) + "\n≠\n" + await FullSig(dbO));
            // Kho nhận cả 2 phiếu trả → như nhận 1 phiếu 6
            foreach (var bk in backs) await svc.ReceiveAsync(bk.HandoverTxId, 0, bk.QtyIssued, null, 1);
            await Check(db, "F1 Kho nhận 2 phiếu trả");
            await svO.ReceiveAsync(dbO.HandoverTransactions.List.Single(t => t.SourceReceiveId != null).HandoverTxId, 0, 6, null, 1);
            Ok(await FullSig(db) == await FullSig(dbO), "Kho nhận 2 phiếu trả (5 + 1) = nhận 1 phiếu 6 ở đáp án");
        }

        Console.WriteLine("[F2] NG tăng khi Kho ĐÃ nhận phiếu trả cũ: vẫn sửa được (phiếu trả cũ giữ), phiếu trả mới 1");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1 lỗi", Gc);
            await Check(db, "F2 sau sửa");
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await PreRecv(dbO, svO, 14, 6);
            await svO.ReceiveAsync(dbO.HandoverTransactions.List.Single(t => t.SourceReceiveId != null).HandoverTxId, 0, 5, null, 1);
            Ok(e.Ok && await FullSig(db) == await FullSig(dbO), "Sổ = PO nhận 14 + 6, Kho đã nhận 5 / chờ 1", e.Error);
        }

        Console.WriteLine("[F3] NG giảm (5 → 3) khi phiếu trả chưa ai nhận: phiếu trả cũ huỷ, lập lại 3 cùng vụ");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            var b0 = Back(db, r0.ReceiveId)!; var vu = r0.NgCaseId;
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 17, 3, null, "đếm nhầm", Gc);
            await Check(db, "F3 sau sửa");
            var act = ActiveRecv(db, r0.HandoverTxId);
            var nb = Back(db, act.Single().ReceiveId);
            Ok(e.Ok && b0.IsVoided && act.Count == 1 && act[0].QtyOk == 17 && act[0].QtyNg == 3 && nb != null && nb.QtyIssued == 3 && nb.NgCaseId == vu,
               "Phiếu trả 5 huỷ, 1 dòng mới 17 + 3, phiếu trả mới 3 cùng vụ", e.Error);
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await PreRecv(dbO, svO, 17, 3);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ = PO nhận đúng 17 + 3 ngay từ đầu");
        }

        Console.WriteLine("[F4] NG giảm khi Kho ĐÃ nhận phiếu trả → chặn, không ghi gì (giao ngược báo lỗi như thường)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            var sig = await FullSig(db); var nRec = db.HandoverReceives.List.Count; var nTx = db.HandoverTransactions.List.Count;
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 16, 4, null, "đếm nhầm", Gc);
            Ok(!e.Ok && e.Error.Contains("đã nhận phiếu trả") && await FullSig(db) == sig && !r0.IsVoided
               && db.HandoverReceives.List.Count == nRec && db.HandoverTransactions.List.Count == nTx,
               "Chặn đúng lời, không ghi gì", e.Error);
            var e2 = await svc.EditReceiveAsync(r0.ReceiveId, 13, 5, null, "đếm nhầm OK", Gc);   // NG giữ 5, OK giảm → được
            await Check(db, "F4 chỉ giảm OK");
            Ok(e2.Ok && ActiveRecv(db, r0.HandoverTxId).Sum(x => x.QtyOk) == 13, "NG giữ nguyên, chỉ đổi OK (15 → 13): được, phiếu trả đã nhận nối sang dòng mới", e2.Error);
        }

        Console.WriteLine("[F5] D7 bước 2b: Gia công đã nhập WTS sau lần nhận → Workflow ON chặn (lời nêu WTS); OFF sửa được + cảnh báo");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            var w = await VuHelp.Wts(db, "GC", "NC1", null, 3, 0, Gc, 0);
            w.CreatedAt = r0.ReceivedAt.AddSeconds(5);
            var sig = await FullSig(db);
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1 lỗi", Gc);
            Ok(!e.Ok && e.Error.Contains("nhập WTS") && e.Error.Contains("Trả nơi làm lỗi") && await FullSig(db) == sig && !r0.IsVoided,
               "ON: chặn, lời ghi đã nhập WTS sau lần nhận + hướng Trả nơi làm lỗi, không ghi gì", e.Error);
            var info = await svc.GetEditInfoAsync(1);
            Ok(info.WorkflowOn && info.RecvUsed.TryGetValue(r0.ReceiveId, out var u) && u.Contains("WTS"), "Lịch sử: nút Sửa lần nhận xám + dòng lý do (bước 2b)");
            db.KhPlans.List.Single().ApplyWorkflow = false;
            var e2 = await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1 lỗi", Gc);
            await Check(db, "F5 OFF sau sửa");
            Ok(e2.Ok && e2.Error.Contains("Workflow OFF") && e2.Error.Contains("WTS") && ActiveRecv(db, r0.HandoverTxId).Sum(x => x.QtyNg) == 6,
               "OFF: sửa được, trả cảnh báo (N7)", e2.Error);
        }

        Console.WriteLine("[F6] Chi tiết (b): Gia công đã lập phiếu sau lần nhận → chặn như đã WTS; phiếu trước lần nhận / WTS trước lần nhận không tính");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 18, 0);
            var w = await VuHelp.Wts(db, "GC", "NC1", null, 0, 0, Gc, 0);
            w.CreatedAt = r0.ReceivedAt.AddSeconds(-5);   // WTS lưu TRƯỚC lần nhận: không tính
            var ok1 = (await svc.GetEditInfoAsync(1)).RecvUsed.ContainsKey(r0.ReceiveId);
            var t = await svc.IssueByCellAsync(1, "GC", "KHO", 1, Gc, "LOI", true, "LOI", fromNC: "NC1", ngCategoryId: 2);
            var tx = db.HandoverTransactions.List.Single(x => x.HandoverTxId == LastTx(db));
            tx.IssuedAt = r0.ReceivedAt.AddSeconds(3);
            var sig = await FullSig(db);
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 17, 0, null, "đếm nhầm", Gc);
            Ok(!ok1 && t.Ok && !e.Ok && e.Error.Contains($"lập phiếu #{tx.HandoverTxId}") && await FullSig(db) == sig,
               "WTS lưu trước lần nhận: chưa xám; Gia công lập phiếu sau lần nhận → chặn, lời nêu số phiếu", t.Error + " | " + e.Error);
        }

        Console.WriteLine("[F7] NG từ 0 lên 2: lần nhận chưa có vụ → bắt chọn loại lỗi; có loại lỗi → vụ mới + phiếu trả 2");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 20, 0);
            var sig = await FullSig(db);
            var e = await svc.EditReceiveAsync(r0.ReceiveId, 18, 2, null, "phát hiện 2 lỗi", Gc);
            Ok(!e.Ok && e.Error == NgCaseService.MsgPickCategory && await FullSig(db) == sig, "Thiếu loại lỗi → chặn, không ghi gì", e.Error);
            var e2 = await svc.EditReceiveAsync(r0.ReceiveId, 18, 2, 3, "phát hiện 2 lỗi", Gc);
            await Check(db, "F7 sau sửa");
            var act = ActiveRecv(db, r0.HandoverTxId).Single();
            var cs = db.NgCases.List.Single();
            Ok(e2.Ok && act.NgCaseId == cs.NgCaseId && cs.NgCategoryId == 3 && cs.OriginKind == NgOrigin.Nhan && cs.DetectGroupCode == "GC" && cs.CauseGroupCode == "KHO"
               && Back(db, act.ReceiveId)?.QtyIssued == 2, "Vụ mới (NG lúc nhận, phát hiện Gia công, nơi gây lỗi Kho), phiếu trả 2", e2.Error);
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await svO.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            await svO.ReceiveAsync(LastTx(dbO), 18, 2, null, Gc, ngCategoryId: 3);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ = PO nhận 18 + 2 ngay từ đầu");
        }

        Console.WriteLine("[F8] Số nhận ≤ số giao còn lại (phiếu nhận 2 lần), số âm / số lẻ / lý do ngắn / quyền / không đổi → chặn");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 10, 0, null, Gc);
            var r1 = db.HandoverReceives.List.Last();
            await svc.ReceiveAsync(tid, 5, 0, null, Gc);
            var sig = await FullSig(db);
            var a = await svc.EditReceiveAsync(r1.ReceiveId, 16, 0, null, "sửa số", Gc);
            var b = await svc.EditReceiveAsync(r1.ReceiveId, -1, 0, null, "sửa số", Gc);
            var cx = await svc.EditReceiveAsync(r1.ReceiveId, 9.5m, 0, null, "sửa số", Gc);
            var d = await svc.EditReceiveAsync(r1.ReceiveId, 9, 0, null, "ab", Gc);
            var f = await svc.EditReceiveAsync(r1.ReceiveId, 9, 0, null, "sửa số", NoRight);
            var g = await svc.EditReceiveAsync(r1.ReceiveId, 10, 0, null, "sửa số", Gc);
            var h = await svc.EditReceiveAsync(r1.ReceiveId, 0, 0, null, "sửa số", Gc);
            Ok(!a.Ok && a.Error.Contains("vượt số giao") && !b.Ok && !cx.Ok && !d.Ok && d.Error == HandoverService.N22ReasonMsg && !f.Ok && f.Error.Contains("Chỉ người đã bấm Nhận")
               && !g.Ok && g.Error == HandoverService.N22NoChangeMsg && !h.Ok && await FullSig(db) == sig,
               "16 > 20 − 5 chặn · số âm · số lẻ · lý do < 3 · người không quyền · không đổi · về 0 → đều chặn, không ghi gì",
               string.Join(" | ", new[] { a, b, cx, d, f, g, h }.Select(x => x.Error)));
            var k = await svc.EditReceiveAsync(r1.ReceiveId, 15, 0, null, "sửa số", Gc);
            await Check(db, "F8 15 + 5 = 20");
            Ok(k.Ok && db.HandoverTransactions.List.Single(x => x.HandoverTxId == tid).Status == "COMPLETED", "Sửa lên đúng 15 (+ 5 = 20): được, phiếu thành đã nhận đủ", k.Error);
            var m = await svc.EditReceiveAsync(ActiveRecv(db, tid).First(x => x.QtyOk == 15).ReceiveId, 12, 0, null, "sửa số", Gc);
            await Check(db, "F8 12 + 5");
            Ok(m.Ok && db.HandoverTransactions.List.Single(x => x.HandoverTxId == tid).Status == "PARTIAL", "Giảm xuống 12: phiếu về nhận một phần (còn 3 chờ nhận)", m.Error);
        }

        Console.WriteLine("[F9] Lịch sử 1 dòng: huỷ lần nhận đã sửa (nhóm 2 dòng) = huỷ cả nhóm + 2 phiếu trả; sửa lần 2 trên nhóm");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var sig0 = await FullSig(db);
            var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 15, 5, null, Gc, ngCategoryId: 1);
            var r0 = db.HandoverReceives.List.Last();
            await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1", Gc);
            var two = ActiveRecv(db, tid);
            var e = await svc.EditReceiveAsync(two[1].ReceiveId, 13, 7, null, "thêm 1 nữa", Gc);   // sửa lần 2 từ dòng thứ 2 của nhóm
            await Check(db, "F9 sửa lần 2");
            var three = ActiveRecv(db, tid);
            Ok(e.Ok && three.Count == 3 && three.Sum(x => x.QtyOk) == 13 && three.Sum(x => x.QtyNg) == 7 && three.All(x => HandoverService.N22Root(x.Notes) == r0.ReceiveId)
               && db.HandoverTransactions.List.Count(t => !t.IsVoided && t.SourceReceiveId != null) == 3,
               "Sửa lần 2 lấy cả nhóm: 13 + 7, 3 dòng cùng dấu gốc, 3 phiếu trả (5 + 1 + 1)", e.Error);
            var v = await svc.VoidReceiveAsync(three[2].ReceiveId, "nhận nhầm phiếu", Gc, new[] { "GC" });
            await Check(db, "F9 huỷ nhóm");
            Ok(v.Ok && ActiveRecv(db, tid).Count == 0 && db.HandoverTransactions.List.Count(t => !t.IsVoided && t.SourceReceiveId != null) == 0 && await FullSig(db) == sig0
               && db.HandoverTransactions.List.Single(t => t.HandoverTxId == tid).Status == "PENDING",
               "Huỷ 1 dòng của nhóm = huỷ cả 3 dòng + 3 phiếu trả, sổ về như chưa nhận", v.Error);
        }

        Console.WriteLine("[F10] Ghi chú người gõ không giả được dấu nhóm [N22#…]");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 5, 0, null, Gc);
            var r1 = db.HandoverReceives.List.Last();
            await svc.ReceiveAsync(tid, 5, 0, null, Gc, $"[N22#{r1.ReceiveId}] ghi đùa");
            var r2 = db.HandoverReceives.List.Last();
            Ok(HandoverService.N22Root(r2.Notes) == null && r2.Notes == $"[N22 #{r1.ReceiveId}] ghi đùa", "Dấu nhóm trong ghi chú người gõ bị vô hiệu (\"[N22#\" → \"[N22 #\")", r2.Notes);   // đáp án đổi sau rà độc lập T1
            var v = await svc.VoidReceiveAsync(r1.ReceiveId, "huỷ thử", Gc, new[] { "GC" });
            Ok(v.Ok && !r2.IsVoided, "Huỷ lần nhận 1 không kéo lần nhận 2", v.Error);
        }

        Console.WriteLine("[F11] D7 bước 1: Sửa phiếu Hàng lỗi công đoạn trước Gia công → Kho 5 (chờ nhận) thành → HTSP? (luật lập phiếu quyết) / số 4 / loại lỗi mới — cùng mã vụ");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await PreRecv(db, svc, 20, 0);
            var t = await svc.IssueByCellAsync(1, "GC", "KHO", 5, Gc, "LOI", true, "LOI", fromNC: "NC1", ngCategoryId: 2);
            var tx = db.HandoverTransactions.List.Single(x => x.HandoverTxId == LastTx(db));
            var vu = tx.NgCaseId!.Value;
            await Check(db, "F11 trước sửa");
            var e = await svc.EditIssueAsync(tx.HandoverTxId, "KHO", 4, 3, null, null, "Đếm nhầm số + loại lỗi", Gc);
            await Check(db, "F11 sau sửa số + loại lỗi");
            var nt = db.HandoverTransactions.List.Single(x => !x.IsVoided && x.HandoverTxId == LastTx(db));
            var cs = db.NgCases.List.Single(x => x.NgCaseId == vu);
            Ok(t.Ok && e.Ok && tx.IsVoided && tx.VoidReason!.Contains($"#{nt.HandoverTxId}") && nt.QtyIssued == 4 && nt.NgCaseId == vu && nt.Purpose == HandoverPurpose.NgReturn
               && nt.FromNC == "NC1" && nt.Notes!.Contains($"sửa từ phiếu #{tx.HandoverTxId}") && cs.NgCategoryId == 3 && db.NgCases.List.Count == 1,
               "Phiếu cũ huỷ (ghi \"Sửa phiếu → #mới\"), phiếu mới 4 cùng mã vụ (không sinh vụ mới), NC phát hiện giữ, loại lỗi vụ đổi 2 → 3",
               t.Error + "|" + e.Error + $" | mới {nt.QtyIssued} vụ {nt.NgCaseId} cat {cs.NgCategoryId} vụ {db.NgCases.List.Count}");
            var lg = db.KhPlanDetailChangeLogs.List.SingleOrDefault(l => l.FieldName == HandoverService.CatLogField(vu));
            Ok(lg != null && lg.OldValue == VuHelp.Label(db, 2) && lg.NewValue == VuHelp.Label(db, 3) && lg.ChangedBy == Gc && lg.Reason!.Contains("Sửa phiếu"), "Lịch sử PO ghi lần đổi loại lỗi (cũ → mới, người, lý do)");
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await PreRecv(dbO, svO, 20, 0);
            await svO.IssueByCellAsync(1, "GC", "KHO", 4, Gc, "LOI", true, "LOI", fromNC: "NC1", ngCategoryId: 3);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ = PO lập đúng phiếu 4 (loại lỗi 3) ngay từ đầu");
            // đổi bộ phận nhận: nơi gây lỗi của vụ đổi theo
            var e2 = await svc.EditIssueAsync(nt.HandoverTxId, "HTSP", 4, null, null, null, "Chọn nhầm bộ phận nhận", Gc);
            await Check(db, "F11 sau đổi bộ phận nhận");
            var nt2 = db.HandoverTransactions.List.Single(x => x.HandoverTxId == LastTx(db));
            Ok(e2.Ok == !nt2.IsVoided && (!e2.Ok || (nt2.ToGroupCode == "HTSP" && nt2.NgCaseId == vu && cs.CauseGroupCode == "HTSP")),
               "Đổi bộ phận nhận Kho → HTSP: lưu theo đúng luật lập phiếu (được thì nơi gây lỗi của vụ = HTSP, cùng vụ)", e2.Error + $" · cause {cs.CauseGroupCode}");
            if (!e2.Ok)
            {
                var dbX = SeedN22(); var svX = new HandoverService(dbX);
                await PreRecv(dbX, svX, 20, 0);
                var dx = await svX.IssueByCellAsync(1, "GC", "HTSP", 4, Gc, "LOI", true, "LOI", fromNC: "NC1", ngCategoryId: 3);
                Ok(!dx.Ok && dx.Error == e2.Error, "Bị chặn đúng như lập mới phiếu đó (cùng lời)", dx.Error + " | " + e2.Error);
            }
        }

        Console.WriteLine("[F12] Sửa phiếu: chỉ khi bên nhận chưa nhận; không áp phiếu tự lập / Huỷ / Trả khách; vượt trần / quyền / không đổi → không ghi gì");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 15, 1, "MOI", false, "MOI");
            var tid = LastTx(db);
            var sig = await FullSig(db);
            var a = await svc.EditIssueAsync(tid, "GC", 25, null, null, null, "sửa số", 1);
            var b = await svc.EditIssueAsync(tid, "GC", 15, null, null, null, "sửa số", 1);
            var c = await svc.EditIssueAsync(tid, "GC", 12, null, null, null, "sửa số", NoRight);
            var d = await svc.EditIssueAsync(tid, "GC", 12, null, null, null, "x", 1);
            Ok(!a.Ok && a.Error.Contains("tối đa") && !b.Ok && b.Error == HandoverService.N22NoChangeMsg && !c.Ok && c.Error.Contains("Chỉ người lập phiếu") && !d.Ok
               && await FullSig(db) == sig && !db.HandoverTransactions.List.Single(t => t.HandoverTxId == tid).IsVoided,
               "Vượt trần (25 > 20 phôi) · không đổi · người không quyền · lý do ngắn → chặn, phiếu cũ còn nguyên", string.Join(" | ", a.Error, b.Error, c.Error, d.Error));
            var e = await svc.EditIssueAsync(tid, "GC", 20, null, null, "ghi chú mới", "Giao thiếu", 1);
            await Check(db, "F12 sửa 15 → 20");
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            await svO.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            Ok(e.Ok && await FullSig(db) == await FullSig(dbO), "Sửa 15 → 20 (Hàng mới): sổ = lập đúng 20 ngay từ đầu", e.Error);
            var nt = LastTx(db);
            await svc.ReceiveAsync(nt, 10, 0, null, Gc);
            var f = await svc.EditIssueAsync(nt, "GC", 18, null, null, null, "sửa sau nhận", 1);
            Ok(!f.Ok && f.Error.Contains("Bên nhận đã nhận"), "Bên nhận đã nhận một phần → không sửa phiếu được", f.Error);
            var r = await svc.EditReceiveAsync(db.HandoverReceives.List.Last().ReceiveId, 8, 2, 1, "2 lỗi", Gc);
            var back = db.HandoverTransactions.List.Single(t => !t.IsVoided && t.SourceReceiveId != null);
            var g = await svc.EditIssueAsync(back.HandoverTxId, "KHO", 1, null, null, null, "sửa phiếu trả", Gc);
            Ok(r.Ok && !g.Ok && g.Error.Contains("tự lập"), "Phiếu trả tự lập: không Sửa phiếu (sửa ở lần nhận gốc)", r.Error + " | " + g.Error);
            Ok(HandoverService.EditIssueBlock(HandoverPurpose.Scrap, null, null, "COMPLETED", false, null) != null
               && HandoverService.EditIssueBlock(HandoverPurpose.RepairSend, null, HandoverCell.LoiVe, "PENDING", false, null) != null
               && HandoverService.EditIssueBlock(HandoverPurpose.New, null, "MOI", "PENDING", false, null) == null,
               "Phiếu Huỷ / Trả khách, phiếu tự trả \"Sửa chưa đạt\": không có Sửa phiếu; phiếu Hàng mới chờ nhận: có");
        }

        Console.WriteLine("[F13] Sửa phiếu mang vụ (Kho giao ô Hàng lỗi → Gia công) giữ vụ; giảm số còn lại ở Tồn hàng lỗi Kho");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            var vu = r0.NgCaseId!.Value;
            var t = await svc.IssueByCellAsync(1, "KHO", "GC", 5, 1, "LOI", false, "LOI", ngCaseId: vu);
            var e = await svc.EditIssueAsync(LastTx(db), "GC", 3, null, null, null, "giao bớt", 1);
            await Check(db, "F13 sau sửa");
            var nt = db.HandoverTransactions.List.Single(x => x.HandoverTxId == LastTx(db));
            Ok(t.Ok && e.Ok && nt.NgCaseId == vu && nt.FromCell == HandoverCell.Loi && nt.QtyIssued == 3, "Phiếu mới 3 cùng vụ, từ ô Hàng lỗi", t.Error + "|" + e.Error);
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            var rO = await PreRecv(dbO, svO, 15, 5);
            await svO.ReceiveAsync(Back(dbO, rO.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            await svO.IssueByCellAsync(1, "KHO", "GC", 3, 1, "LOI", false, "LOI", ngCaseId: rO.NgCaseId);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ = lập đúng 3 ngay từ đầu (2 chiếc vẫn ở Tồn hàng lỗi Kho)");
        }

        Console.WriteLine("[F14] D7 bước 3: Đổi loại lỗi — được khi vụ chưa giao / giao chưa nhận; bên nhận đã nhận → ON chặn, OFF cảnh báo; quyền; lịch sử");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);   // vụ sinh lúc Gia công nhận (phiếu trả tự lập chờ Kho nhận)
            var vu = r0.NgCaseId!.Value;
            var sig = await FullSig(db);
            var a = await svc.ChangeCaseCategoryAsync(vu, 2, "Ghi nhầm loại lỗi", Gc);
            Ok(a.Ok && db.NgCases.List.Single().NgCategoryId == 2 && await FullSig(db) == sig
               && db.KhPlanDetailChangeLogs.List.Count(l => l.FieldName == HandoverService.CatLogField(vu)) == 1,
               "Vụ đã giao (phiếu trả) nhưng Kho chưa nhận: đổi được, số không đổi, lịch sử PO 1 dòng", a.Error);
            var info = await svc.GetEditInfoAsync(1);
            Ok(!info.CaseCatBlock.ContainsKey(vu) && info.CatLogs.Count == 1 && info.CatLogs[0].Old == VuHelp.Label(db, 1) && info.CatLogs[0].New == VuHelp.Label(db, 2),
               "Lịch sử phiếu: nút Đổi loại lỗi bật, dòng lịch sử đổi cũ → mới");
            var x = await svc.ChangeCaseCategoryAsync(vu, 3, "đổi thử", NoRight);
            var y = await svc.ChangeCaseCategoryAsync(vu, 2, "đổi thử", Gc);
            var z = await svc.ChangeCaseCategoryAsync(vu, 6, "đổi thử", Gc);
            Ok(!x.Ok && x.Error.Contains("nơi phát hiện") && !y.Ok && y.Error == HandoverService.N22NoChangeMsg && !z.Ok && z.Error == NgCaseService.MsgPickCategory,
               "Người không quyền · cùng loại · loại đã ngưng → chặn", string.Join(" | ", x.Error, y.Error, z.Error));
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);   // Kho đã nhận
            var b = await svc.ChangeCaseCategoryAsync(vu, 3, "đổi sau nhận", Gc);
            Ok(!b.Ok && b.Error.Contains("bên nhận nhận hàng") && db.NgCases.List.Single().NgCategoryId == 2, "Kho đã nhận phiếu mang vụ: ON chặn", b.Error);
            Ok((await svc.GetEditInfoAsync(1)).CaseCatBlock.ContainsKey(vu), "Lịch sử: nút Đổi loại lỗi xám + lý do");
            db.KhPlans.List.Single().ApplyWorkflow = false;
            var c = await svc.ChangeCaseCategoryAsync(vu, 3, "đổi sau nhận", Gc);
            Ok(c.Ok && c.Error.Contains("áp cho CẢ vụ") && db.NgCases.List.Single().NgCategoryId == 3, "OFF: đổi được + cảnh báo", c.Error);
            Ok(await NgCaseService.CategoryChangeBlockedAsync(db, vu, 4) != null, "Luật dùng chung (đường sửa WTS): vụ đã có bên nhận nhận → báo chặn");
        }

        Console.WriteLine("[F15] Luật đổi loại lỗi: phiếu mang vụ chờ nhận → được; vụ Huỷ / Trả khách → chặn; vụ có Kết quả sửa → chặn");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            var vu = r0.NgCaseId!.Value;
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            // vụ chỉ còn ở Kho; Kho giao 2 sang Gia công ô Hàng lỗi (chờ nhận) — bên nhận chưa nhận nhưng phiếu trả cũ đã được nhận → vẫn chặn
            Ok(await NgCaseService.CategoryChangeBlockedAsync(db, vu, 4) != null, "Phiếu trả mang vụ đã được nhận → chặn");
            var db2 = SeedN22(); var sv2 = new HandoverService(db2);
            await PreRecv(db2, sv2, 20, 0);
            await sv2.IssueByCellAsync(1, "GC", "KHO", 3, Gc, "LOI", true, "LOI", fromNC: "NC1", ngCategoryId: 2);
            var v2 = db2.NgCases.List.Single().NgCaseId;
            Ok(await NgCaseService.CategoryChangeBlockedAsync(db2, v2, 4) == null, "Phiếu Hàng lỗi công đoạn trước chờ nhận → đổi được (trước N22: chặn)");
            await sv2.ReceiveAsync(LastTx(db2), 0, 3, null, 1);
            var h = await sv2.IssueSpecialAsync(1, "KHO", "KHACH", 1, 1, HandoverPurpose.CustReturn, ngCaseId: v2);
            Ok(h.Ok && await NgCaseService.CategoryChangeBlockedAsync(db2, v2, 4) != null, "Vụ đã nhận + trả khách → chặn", h.Error);
        }


        Console.WriteLine("[F16] Rà độc lập T1: ghi chú gõ 2 dấu liền \"[N22#a][N22#a] x\" / dấu giữa chuỗi — không giả được nhóm");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 5, 0, null, Gc);
            var r1 = db.HandoverReceives.List.Last();
            await svc.ReceiveAsync(tid, 5, 0, null, Gc, $"[N22#{r1.ReceiveId}][N22#{r1.ReceiveId}] x");
            var r2 = db.HandoverReceives.List.Last();
            await svc.ReceiveAsync(tid, 5, 0, null, Gc, $"ghi [N22#{r1.ReceiveId}] giữa");
            var r3 = db.HandoverReceives.List.Last();
            Ok(HandoverService.N22Root(r2.Notes) == null && HandoverService.N22Root(r3.Notes) == null && !r2.Notes!.Contains("[N22#") && !r3.Notes!.Contains("[N22#"),
               "Mọi chữ \"[N22#\" người gõ bị vô hiệu (không nhận ra nhóm)", r2.Notes + " | " + r3.Notes);
            var e = await svc.EditReceiveAsync(r1.ReceiveId, 4, 0, null, "sửa số", Gc);
            Ok(e.Ok && ActiveRecv(db, tid).Count == 3 && !r2.IsVoided && !r3.IsVoided, "Sửa lần nhận 1 không kéo lần nhận 2, 3", e.Error);
            var v = await svc.VoidReceiveAsync(ActiveRecv(db, tid).First(x => HandoverService.N22Root(x.Notes) != null).ReceiveId, "huỷ", Gc, new[] { "GC" });
            Ok(v.Ok && ActiveRecv(db, tid).Count == 2, "Huỷ lần nhận đã sửa không kéo lần nhận khác", v.Error);
        }

        Console.WriteLine("[F17] Rà độc lập T2: Sửa phiếu đổi loại lỗi cần quyền người tạo vụ / nhóm phát hiện (Workflow OFF, người chỉ có quyền Kho)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(workflow: false); var svc = new HandoverService(db);
            var gk = new UserGroup { GroupId = 93, GroupCode = "KHOONLY", GroupName = "Chỉ Kho (thử)", IsActive = true, Permissions = FuzzPerm.Json(MES.Web.Constants.SystemPermissions.HandoverKho) };
            db.UserGroups.List.Add(gk);
            db.Users.List.Add(new User { UserId = 10, Username = "u10", FullName = "U10", IsActive = true, Group = gk, GroupId = 93 });
            db.Fixup();
            var r0 = await PreRecv(db, svc, 15, 5);   // vụ phát hiện ở Gia công (người tạo u2)
            var vu = r0.NgCaseId!.Value;
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 10);
            var t = await svc.IssueByCellAsync(1, "KHO", "GC", 5, 10, "LOI", false, "LOI", ngCaseId: vu);
            var tid = LastTx(db);
            var sig = await FullSig(db);
            var a = await svc.EditIssueAsync(tid, "GC", 5, 3, null, null, "đổi loại lỗi", 10);
            Ok(t.Ok && !a.Ok && a.Error.Contains("nơi phát hiện") && db.NgCases.List.Single().NgCategoryId == 1 && await FullSig(db) == sig && !db.HandoverTransactions.List.Single(x => x.HandoverTxId == tid).IsVoided,
               "Người Kho (không quyền Gia công, không tạo vụ) Sửa phiếu đổi loại lỗi → chặn, không ghi gì", t.Error + " | " + a.Error);
            var b2 = await svc.EditIssueAsync(tid, "GC", 4, null, null, null, "bớt số", 10);
            Ok(b2.Ok && db.NgCases.List.Single().NgCategoryId == 1, "Cùng người, chỉ sửa số → được (loại lỗi giữ)", b2.Error);
        }

        Console.WriteLine("[F18] Sửa lần nhận ô Hàng lỗi (Kho nhận phiếu trả 5 → 4) và ô Hàng sửa (Gia công nhận hàng gửi sửa 2 → 1)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            var bk = Back(db, r0.ReceiveId)!;
            await svc.ReceiveAsync(bk.HandoverTxId, 0, 5, null, 1);
            var rk = db.HandoverReceives.List.Last();
            var a = await svc.EditReceiveAsync(rk.ReceiveId, 1, 4, null, "sửa", 1);
            var b = await svc.EditReceiveAsync(rk.ReceiveId, 0, 4, null, "đếm nhầm", 1);
            await Check(db, "F18 Kho nhận 4");
            Ok(!a.Ok && b.Ok && bk.Status == "PARTIAL" && ActiveRecv(db, bk.HandoverTxId).Single().QtyNg == 4, "Ô Hàng lỗi: không có OK (chặn); NG 5 → 4: phiếu trả còn 1 chờ nhận", a.Error + " | " + b.Error);
            var dbO = SeedN22(); var svO = new HandoverService(dbO);
            var rO = await PreRecv(dbO, svO, 15, 5);
            await svO.ReceiveAsync(Back(dbO, rO.ReceiveId)!.HandoverTxId, 0, 4, null, 1);
            Ok(await FullSig(db) == await FullSig(dbO), "Sổ = Kho nhận đúng 4 ngay từ đầu");
            // ô Hàng sửa
            var vu = r0.NgCaseId!.Value;
            await svc.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "SUA", ngCaseId: vu);
            var ts = LastTx(db);
            await svc.ReceiveAsync(ts, 2, 0, null, Gc);
            var rs = db.HandoverReceives.List.Last();
            var c = await svc.EditReceiveAsync(rs.ReceiveId, 1, 1, null, "sửa", Gc);
            var d = await svc.EditReceiveAsync(rs.ReceiveId, 1, 0, null, "đếm nhầm", Gc);
            await Check(db, "F18 Gia công nhận hàng sửa 1");
            Ok(!c.Ok && d.Ok && ActiveRecv(db, ts).Single().QtyOk == 1 && ActiveRecv(db, ts).Single().NgCaseId == vu, "Ô Hàng sửa: không có NG (chặn); 2 → 1 giữ vụ", c.Error + " | " + d.Error);
        }

        Console.WriteLine("[F19] Kho huỷ phiếu trả MỚI (B2) của lần nhận đã sửa = huỷ cả nhóm lần nhận + phiếu trả cũ");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var sig0 = await FullSig(db); var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 15, 5, null, Gc, ngCategoryId: 1);
            var r0 = db.HandoverReceives.List.Last();
            await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1", Gc);
            var b2 = db.HandoverTransactions.List.Where(t => !t.IsVoided && t.SourceReceiveId != null).OrderBy(t => t.HandoverTxId).Last();
            var v = await svc.VoidIssueAsync(b2.HandoverTxId, "huỷ phiếu trả", Gc, new[] { "GC" });
            await Check(db, "F19 sau huỷ");
            Ok(v.Ok && ActiveRecv(db, tid).Count == 0 && !db.HandoverTransactions.List.Any(t => !t.IsVoided && t.SourceReceiveId != null) && await FullSig(db) == sig0,
               "Cả 2 dòng + 2 phiếu trả huỷ, sổ về như chưa nhận", v.Error);
        }

        Console.WriteLine("[F20] Rà độc lập L2: phiếu đã Huỷ phần chưa nhận → không giảm số nhận; đổi OK ↔ NG cùng tổng vẫn được");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
            var tid = LastTx(db);
            await svc.ReceiveAsync(tid, 10, 0, null, Gc);
            var r1 = db.HandoverReceives.List.Last();
            var cl = await svc.CloseRemainderAsync(tid, "giao thừa", 1, new[] { "KHO" });
            var sig = await FullSig(db);
            var a = await svc.EditReceiveAsync(r1.ReceiveId, 8, 0, null, "giảm", Gc);
            var b = await svc.EditReceiveAsync(r1.ReceiveId, 11, 0, null, "tăng", Gc);
            Ok(cl.Ok && !a.Ok && a.Error.Contains("huỷ phần chưa nhận") && !b.Ok && b.Error.Contains("vượt số giao") && await FullSig(db) == sig, "Giảm 10 → 8 / tăng 10 → 11 chặn, không ghi gì", cl.Error + " | " + a.Error + " | " + b.Error);
            var c = await svc.EditReceiveAsync(r1.ReceiveId, 9, 1, 2, "1 lỗi", Gc);
            await Check(db, "F20 9 + 1");
            Ok(c.Ok && db.HandoverTransactions.List.Single(x => x.HandoverTxId == tid).Status == "COMPLETED", "9 OK + 1 NG (cùng tổng 10): được, phiếu vẫn đủ", c.Error);
        }

        Console.WriteLine("[F21] Luật đổi loại lỗi: vụ báo lỗi (chưa giao) đổi được; ghi Kết quả sửa → chặn");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            await PreRecv(db, svc, 20, 0);
            var nr = await svc.ReportNgAfterReceiveAsync(1, "GC", HandoverNgSource.Chua, "NC1", 2, HandoverNgSource.DauVao, "KHO", "", null, Gc, new List<string> { "GC" }, ngCategoryId: 1);
            var vu = db.NgCases.List.Single().NgCaseId;
            var a = await NgCaseService.CategoryChangeBlockedAsync(db, vu, 3);
            var k = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 2, 0, null, null, Gc);
            var b = await NgCaseService.CategoryChangeBlockedAsync(db, vu, 3);
            var c = await svc.ChangeCaseCategoryAsync(vu, 3, "đổi sau sửa", Gc);
            Ok(nr.Ok && a == null && k.Ok && b != null && !c.Ok, "Vụ báo lỗi chưa giao: đổi được; sau Kết quả sửa: chặn (cả nút Đổi loại lỗi)", nr.Error + " | " + k.Error + " | " + b + " | " + c.Error);
        }


        Console.WriteLine("[F22] Rà độc lập L1: phiếu lập lúc OFF từ ô Hàng lỗi chỉ định ô Hàng lỗi đã xử lý, nhận 0 OK + 5 NG; bật ON → Sửa lần nhận thành có OK bị chặn ([SUA-QUA-KCS])");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(workflow: false); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            await svc.ReceiveAsync(Back(db, r0.ReceiveId)!.HandoverTxId, 0, 5, null, 1);
            var t = await svc.IssueByCellAsync(1, "KHO", "GC", 5, 1, "LOI", false, "XL", ngCaseId: r0.NgCaseId);
            var tid = LastTx(db);
            var r = await svc.ReceiveAsync(tid, 0, 5, null, Gc);
            var rr = db.HandoverReceives.List.Last();
            db.KhPlans.List.Single().ApplyWorkflow = true;
            var sig = await FullSig(db);
            var e = await svc.EditReceiveAsync(rr.ReceiveId, 3, 2, null, "thử", Gc);
            Ok(t.Ok && r.Ok && !e.Ok && e.Error.Contains("chưa ghi Kết quả sửa") && await FullSig(db) == sig, "ON: không nhận OK vào ô hàng đạt khi phiếu lấy từ ô Hàng lỗi (như lúc nhận), không ghi gì", t.Error + " | " + r.Error + " | " + e.Error);
        }


        Console.WriteLine("[F23] GN-WHY tính thử huỷ 1 dòng của lần nhận đã sửa = bỏ cả nhóm + phiếu trả của từng dòng (như máy chủ)");
        {
            WtsSvcHelp.Reset();
            var db = SeedN22(); var svc = new HandoverService(db);
            var r0 = await PreRecv(db, svc, 15, 5);
            await svc.EditReceiveAsync(r0.ReceiveId, 14, 6, null, "thêm 1", Gc);
            var two = ActiveRecv(db, r0.HandoverTxId);
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(1))!;
            HandoverWhy.OpVoidReceive(two[1].ReceiveId)(inp);
            var t = inp.Txs.Single(x => x.Id == r0.HandoverTxId);
            Ok(t.Receives.Count == 0 && !inp.Txs.Any(x => x.SourceReceiveId != null) && two.All(x => HandoverService.N22Root(x.Notes) != null),
               "Tính thử huỷ dòng 2: cả 2 dòng + 2 phiếu trả bị bỏ (InRecv.N22Grp)", $"còn {t.Receives.Count} lần nhận, {inp.Txs.Count(x => x.SourceReceiveId != null)} phiếu trả");
            // máy chủ: GC dùng hàng (WTS) rồi huỷ dòng 2 → K5 chặn, có gợi ý gỡ, không ghi gì
            var w = await VuHelp.Wts(db, "GC", "NC1", null, 14, 0, Gc, 0);
            var sig = await FullSig(db);
            var v = await svc.VoidReceiveAsync(two[1].ReceiveId, "huỷ", Gc, new[] { "GC" });
            Ok(!v.Ok && v.Error.StartsWith("Không lưu") && await FullSig(db) == sig && two.All(x => !x.IsVoided), "Gia công đã WTS 14: huỷ nhóm bị K5 chặn, không ghi gì", v.Error);
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
