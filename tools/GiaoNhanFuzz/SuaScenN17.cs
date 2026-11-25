// [N17-KCS-BO 2026-11-23] Kịch bản cố định "n17": luật N17 — sửa đạt KHÔNG cần KCS xác nhận (chứng từ ghi từ 23/11), bằng hàm thật HandoverService
//   + sổ chung. Khách chốt mockup giao nhận 23/11 (TH3, sheet dòng 14 "bỏ"); Hùng 23/11 16:24 OK ghi sổ, 19:05 cho code.
//   Đáp án độc lập:
//     (a) TƯƠNG ĐƯƠNG: cùng 1 thao tác, chứng từ mới (tự xác nhận) phải ra sổ y hệt chứng từ kiểu cũ + người KCS xác nhận đạt hết ngay sau đó
//         (mọi ô mọi nhóm, từng NC / mã CV, từng vụ, Đang ở đâu, khung hàng lỗi) — luật N17 = "như KCS đã xác nhận đạt", không đường tính mới.
//     (b) số cụ thể theo mockup TH3 / TH5 (Kho sửa đạt → phôi còn giao luôn; Gia công sửa hộ → làm tiếp luôn; @OK → chờ NC kế luôn).
//     (c) chứng từ cũ (ghi trước bản này) giữ nghĩa cũ: KCS vẫn xác nhận được, huỷ như cũ (Hùng 23/11 câu B).
//   Sau MỖI bước ghi: Check() chung của kịch bản sua (cân PO, không ô âm, vụ cân, khung lỗi, dòng B cuối = sổ, Đang ở đâu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static partial class SuaScen
{
    sealed class CoScope : IDisposable
    {
        readonly bool _old;
        public CoScope(bool v) { _old = HandoverService.TuXacNhanSuaDat; HandoverService.TuXacNhanSuaDat = v; }
        public void Dispose() => HandoverService.TuXacNhanSuaDat = _old;
    }
    /// <summary>[N17-KCS-BO] Dựng Kết quả sửa KIỂU CŨ (trước 23/11: đạt chờ KCS) bằng hàm thật.</summary>
    static IDisposable KieuCu() => new CoScope(false);

    static List<HandoverRepairCheck> AutoChecks(AppDbContext db, long repId)
        => db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == repId && c.Notes == HandoverService.AutoCheckNote).ToList();

    static decimal Xl(LedgerCase c, string g) => c.DaXuLy.TryGetValue(g, out var v) ? v : 0m;

    /// <summary>Chữ ký đầy đủ để so tương đương: Sig (ô + NC + vụ) + Đang ở đâu + tổng khung hàng lỗi.</summary>
    static async Task<string> FullSig(AppDbContext db)
    {
        var led = new HandoverLedgerService(db);
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var w = HandoverLedgerService.WhereIs(row);
        var inp = (await led.GetInputForDetailAsync(1))!;
        var tr = HandoverNgTrace.Build(inp);
        return Sig(row) + " | WHERE " + string.Join(",", w.Items.OrderBy(i => i.Place).ThenBy(i => i.Kind).ThenBy(i => i.Nc).Select(i => $"{i.Place}:{i.Kind}:{i.Nc}={i.Qty}"))
             + $" | TRACE ps {tr.PhatSinh} ve {tr.VeSx} huy {tr.DaHuy} khach {tr.TraKhach} giu {tr.DangGiu} chuyen {tr.DangChuyen} van {tr.Problems.Count}"
             + $" | XL {string.Join(",", row.Groups.Values.Select(g => g.Code + "=" + g.XlGiu + "/" + g.GiaoLoiXl + "/" + g.NhanLoiXl))}";
    }

    // ── Dựng sẵn (dùng chung cho đường mới + đường cũ) ───────────────────
    /// <summary>Kho → GC 20, GC nhận 18 + NG 2 (phiếu trả tự lập) → Kho nhận lại 2 ô Hàng lỗi. Trả mã vụ.</summary>
    static async Task<long> PreKhoLoi(AppDbContext db, HandoverService svc)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 18, 2, null, Gc, ngCategoryId: 1);
        var back = db.HandoverTransactions.List.Single(x => x.SourceReceiveId != null);
        await svc.ReceiveAsync(back.HandoverTxId, 0, 2, null, 1);
        return back.NgCaseId!.Value;
    }
    /// <summary>PreKhoLoi + Kho gửi GC nhờ sửa 2 (ô Hàng sửa), GC nhận.</summary>
    static async Task<long> PreGcSuaHo(AppDbContext db, HandoverService svc)
    {
        var vu = await PreKhoLoi(db, svc);
        await svc.IssueByCellAsync(1, "KHO", "GC", 2, 1, "LOI", false, "SUA", ngCaseId: vu);
        await svc.ReceiveAsync(LastTx(db), 2, 0, null, Gc);
        return vu;
    }
    /// <summary>Kho → GC 20 nhận đủ; WTS NC1 20; WTS NC2 OK 18 NG 2 (vụ Gia công làm hỏng ở NC2). Trả mã vụ.</summary>
    static async Task<long> PreGcNc2(AppDbContext db, HandoverService svc, decimal nc3Ok = 0)
    {
        await svc.IssueByCellAsync(1, "KHO", "GC", 20, 1, "MOI", false, "MOI");
        await svc.ReceiveAsync(LastTx(db), 20, 0, null, Gc);
        await VuHelp.Wts(db, "GC", "NC1", null, 20, 0, Gc, 0);
        var w2 = await VuHelp.Wts(db, "GC", "NC2", null, 18, 2, Gc, 2);
        if (nc3Ok > 0) await VuHelp.Wts(db, "GC", "NC3", null, nc3Ok, 0, Gc, 0);
        return w2.NgCaseId!.Value;
    }

    /// <summary>(a) Tương đương: chạy pre + Kết quả sửa ở đường mới, rồi ở đường cũ + KCS xác nhận đạt hết; sổ phải y hệt.</summary>
    static Task Eq(string name, Func<AppDbContext, HandoverService, Task<long>> pre,
                   Func<AppDbContext, HandoverService, long, Task<(bool Ok, string Error)>> kq, bool workflow = true)
        => EqB(name, async () => { var db = Seed(workflow: workflow); var svc = new HandoverService(db); return (db, svc, await pre(db, svc)); }, kq);

    static async Task EqB(string name, Func<Task<(AppDbContext Db, HandoverService Svc, long Vu)>> build,
                          Func<AppDbContext, HandoverService, long, Task<(bool Ok, string Error)>> kq)
    {
        async Task<(bool Ok, string Err, string Sig, AppDbContext Db)> One(bool cu)
        {
            WtsSvcHelp.Reset();
            var (db, svc, vu) = await build();
            (bool Ok, string Error) r;
            using (cu ? KieuCu() : null) r = await kq(db, svc, vu);
            if (cu && r.Ok)
            {
                var rp = db.HandoverRepairResults.List.Last();
                var wait = rp.QtyPass - db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == rp.HandoverRepairResultId && !c.IsVoided).Sum(c => c.QtyOk + c.QtyNg);
                if (wait > 0)
                {
                    var x = await svc.ConfirmRepairAsync(rp.HandoverRepairResultId, wait, 0, null, Kcs);
                    if (!x.Ok) return (false, "KCS xác nhận (đường cũ): " + x.Error, "", db);
                }
            }
            await Check(db, name + (cu ? " (đường cũ + KCS)" : " (N17)"));
            return (r.Ok, r.Error, await FullSig(db), db);
        }
        var moi = await One(false);
        var cu = await One(true);
        Ok(moi.Ok && cu.Ok, name + ": lưu được cả 2 đường", moi.Err + " | " + cu.Err);
        Ok(moi.Sig == cu.Sig, name + ": sổ N17 = sổ kiểu cũ + KCS xác nhận đạt hết (mọi ô, NC, vụ, Đang ở đâu, khung lỗi)", "N17:  " + moi.Sig + "\nCŨ:   " + cu.Sig);
        var rpM = moi.Db.HandoverRepairResults.List.LastOrDefault();
        var ac = rpM == null ? new List<HandoverRepairCheck>() : AutoChecks(moi.Db, rpM.HandoverRepairResultId);
        Ok(rpM != null && (rpM.QtyPass == 0 ? ac.Count == 0 : ac.Count == 1 && ac[0].QtyOk == rpM.QtyPass && ac[0].QtyNg == 0 && ac[0].CheckedBy == rpM.CreatedBy
                                                                && ac[0].CheckedAt == rpM.CreatedAt && !ac[0].IsVoided)
           && moi.Db.HandoverRepairChecks.List.Count(c => c.HandoverRepairResultId == rpM.HandoverRepairResultId) == ac.Count,
           name + ": đường mới có đúng 1 dòng tự xác nhận (đạt = số đạt, người = người ghi, cùng giờ), không dòng nào khác",
           rpM == null ? "không có Kết quả sửa" : string.Join(",", moi.Db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == rpM.HandoverRepairResultId).Select(c => $"#{c.HandoverRepairCheckId} {c.QtyOk}/{c.QtyNg} by{c.CheckedBy} {c.Notes}")));
        var rowM = (await new HandoverLedgerService(moi.Db).GetLedgerForDetailAsync(1))!;
        Ok(rowM.Groups.Values.All(g => g.ChoKcs == 0), name + ": không còn ô Chờ KCS ở đường mới", string.Join(",", rowM.Groups.Values.Select(g => g.Code + "=" + g.ChoKcs)));
    }

    public static async Task<int> RunN17()
    {
        fails = 0;
        HandoverService.TuXacNhanSuaDat = true;

        // ─────────────────────────────────────────────────────────────────
        Console.WriteLine("[N1] TH3 bước 3: Kho tự sửa phôi lỗi đạt 1 → không qua KCS, vào phôi còn giao ngay; giao Gia công luôn");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var b = await Check(db, "N1 trước");
            var k = await svc.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 1, 0, null, "sua phoi", 1);
            var a = await Check(db, "N1 Kho sửa đạt 1");
            var rep = db.HandoverRepairResults.List.Last();
            var ac = AutoChecks(db, rep.HandoverRepairResultId);
            Ok(k.Ok && ac.Count == 1 && ac[0].QtyOk == 1 && ac[0].CheckedBy == 1 && ac[0].CheckedAt == rep.CreatedAt, "Ghi Kết quả sửa: tự có dòng xác nhận đạt 1 (người ghi, cùng giờ)", k.Error);
            var kb = b.G("KHO"); var ka = a.G("KHO");
            Ok(ka.ChoKcs == 0 && ka.ConGiao == kb.ConGiao + 1 && ka.LoiGiu == kb.LoiGiu - 1 && ka.Ton == kb.Ton,
               "Kho: phôi còn giao +1 ngay, Tồn hàng lỗi −1, Chờ KCS 0, Tồn không đổi", $"còn {kb.ConGiao}→{ka.ConGiao} lỗi {kb.LoiGiu}→{ka.LoiGiu} kcs {ka.ChoKcs} tồn {kb.Ton}→{ka.Ton}");
            Ok(a.Cases[vu].Transit == 0 && Xl(a.Cases[vu], "KHO") == 1 && a.Cases[vu].HeldAt("KHO", HandoverCell.Loi) == 1,
               "Vụ: đã xử lý ở Kho 1, không 'đang chuyển', còn giữ 1 ở ô Hàng lỗi Kho");
            Ok(!HandoverLedgerService.WhereIs(a).Items.Any(i => i.Kind == "CHO_KCS"), "Đang ở đâu: không có dòng 'hàng sửa chờ KCS xác nhận'");
            var g1 = await svc.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            var r1 = await svc.ReceiveAsync(LastTx(db), 1, 0, null, Gc);
            var a2 = await Check(db, "N1 giao Gia công");
            Ok(g1.Ok && r1.Ok && a2.G("KHO").ConGiao == kb.ConGiao, "Giao luôn phôi đã sửa sang Gia công (không chờ ai xác nhận)", g1.Error + " | " + r1.Error);
            var x = await svc.ConfirmRepairAsync(rep.HandoverRepairResultId, 1, 0, null, Kcs);
            Ok(!x.Ok && x.Error.Contains("không cần KCS xác nhận"), "[N3] KCS xác nhận Kết quả sửa mới bị từ chối (luật N17), lời báo rõ", x.Error);
            Ok(db.HandoverRepairChecks.List.Count(c => c.HandoverRepairResultId == rep.HandoverRepairResultId) == 1, "Bị từ chối thì không ghi thêm dòng nào");
        }

        Console.WriteLine("[N2] Tương đương (a): chứng từ mới = chứng từ cũ + KCS xác nhận đạt hết, ở mọi đích (Kho / GC sửa hộ / @OK / @XONG giữ ô / nhóm khác sửa hộ / OFF)");
        {
            await Eq("N2a Kho sửa phôi đạt 1", PreKhoLoi, (d, s, v) => s.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, v, 1, 0, null, null, 1));
            await Eq("N2b GC sửa hộ Kho (ô Hàng sửa): đạt 1, chưa đạt 1, làm tiếp NC1", PreGcSuaHo, (d, s, v) => s.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, v, 1, 1, "NC1", "mai", Gc));
            await Eq("N2c GC tự làm hỏng NC2, sửa đạt 2 (@OK = xong NC2)", (d, s) => PreGcNc2(d, s),
                     (d, s, v) => s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, v, 2, 0, HandoverLedgerService.RepOk, null, Gc));
            await EqB("N2d KCS phát hiện, GC sửa @XONG (giữ Tồn hàng lỗi đã xử lý)", async () => await SeedKcsLoi(),
                     (d, s, v) => s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, v, 1, 0, HandoverLedgerService.RepXong, null, Gc));
            await Eq("N2e GC gửi HTSP sửa hộ 2 (vụ NC2) → HTSP sửa đạt 2", async (d, s) =>
                     {
                         var v = await PreGcNc2(d, s);
                         await s.IssueByCellAsync(1, "GC", "HTSP", 2, Gc, "LOI", false, "SUA", ngCaseId: v);
                         await s.ReceiveAsync(LastTx(d), 2, 0, null, Other);
                         return v;
                     },
                     (d, s, v) => s.CreateRepairResultAsync(1, "HTSP", HandoverCell.Sua, v, 2, 0, null, null, Other));
            await Eq("N2f Workflow OFF: GC sửa đạt 1 làm lại NC1 (lý do)", (d, s) => PreGcNc2(d, s),
                     (d, s, v) => s.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, v, 1, 0, "NC1", null, Gc, "lam lai"), workflow: false);
            await Eq("N2g chỉ không đạt (đạt 0): không có dòng tự xác nhận", PreGcSuaHo, (d, s, v) => s.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, v, 0, 1, null, null, Gc));
            // [rà độc lập G-C Thấp 7] @XONG mà nhóm sửa chính là nơi phát hiện (HTSP báo lỗi hàng ĐÃ LÀM → sửa tại HTSP = Đã làm, không WTS lại)
            await EqB("N2h HTSP báo lỗi hàng đã làm → tự sửa đạt 1 (@XONG = Đã làm)", async () =>
                      {
                          var (d, s, _) = await SeedKcsLoi(0, htspOnly: true);
                          await s.ReportNgAfterReceiveAsync(1, "HTSP", HandoverNgSource.Da, null, 1, HandoverNgSource.DauRa, null, "", null, Other, new List<string> { "HTSP" }, ngCategoryId: 2);
                          return (d, s, d.HandoverNgReports.List.Last().NgCaseId!.Value);
                      },
                      (d, s, v) => s.CreateRepairResultAsync(1, "HTSP", HandoverCell.Loi, v, 1, 0, null, null, Other));
        }

        Console.WriteLine("[N2x] Nhận diện dòng tự xác nhận: phải đủ CẢ 2 điều kiện (người = người ghi + ghi chú tiền tố [N17]) — dữ liệu cũ không bị nhận nhầm");
        {
            Ok(HandoverService.AutoCheckTag == "[N17]" && HandoverService.AutoCheckNote == "[N17] Sửa đạt — không cần KCS xác nhận",
               "Hằng số tiền tố / ghi chú đúng nguyên văn (đổi chữ sẽ làm dòng đã lưu thành 'lần KCS xác nhận')", HandoverService.AutoCheckNote);
            // (a) chứng từ cũ, KCS (khác người ghi) xác nhận với ghi chú y nguyên câu tự xác nhận → vẫn là lần KCS xác nhận thật
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcNc2(db, svc);
            using (KieuCu()) await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 2, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep = db.HandoverRepairResults.List.Last();
            var xa = await svc.ConfirmRepairAsync(rep.HandoverRepairResultId, 1, 0, HandoverService.AutoCheckNote, Kcs);
            var va = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", Gc);
            var it = (await svc.GetRepairsAsync(1)).Single(r => r.Id == rep.HandoverRepairResultId);
            Ok(xa.Ok && !va.Ok && va.Error.Contains("lần KCS xác nhận") && !it.AutoOk && it.Waiting == 1,
               "(a) KCS khác người ghi, ghi chú trùng câu [N17] → vẫn là lần KCS xác nhận (huỷ Kết quả sửa bị chặn, còn chờ 1)", xa.Error + " | " + va.Error);
            await Check(db, "N2x a");
            // (b) dòng xác nhận của chính người ghi nhưng ghi chú khác (dữ liệu dựng thẳng) → không phải dòng tự xác nhận
            WtsSvcHelp.Reset();
            var db2 = Seed(); var svc2 = new HandoverService(db2);
            var vu2 = await PreGcNc2(db2, svc2);
            using (KieuCu()) await svc2.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu2, 2, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep2 = db2.HandoverRepairResults.List.Last();
            db2.HandoverRepairChecks.Add(new HandoverRepairCheck { HandoverRepairResultId = rep2.HandoverRepairResultId, QtyOk = 1, QtyNg = 0, Notes = "kcs tay", CheckedBy = Gc, CheckedAt = DateTime.Now });
            await db2.SaveChangesAsync();
            var vb = await svc2.VoidRepairResultAsync(rep2.HandoverRepairResultId, "thu", Gc);
            var it2 = (await svc2.GetRepairsAsync(1)).Single(r => r.Id == rep2.HandoverRepairResultId);
            Ok(!vb.Ok && vb.Error.Contains("lần KCS xác nhận") && !it2.AutoOk, "(b) người ghi + ghi chú khác → không phải dòng tự xác nhận (huỷ Kết quả sửa bị chặn như cũ)", vb.Error);
        }

        Console.WriteLine("[N4] Dòng tự xác nhận không huỷ riêng (cả Admin / người có quyền KCS)");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcNc2(db, svc);
            var k = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 2, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep = db.HandoverRepairResults.List.Last(); var ac = AutoChecks(db, rep.HandoverRepairResultId).Single();
            var sig0 = await FullSig(db);
            var v1 = await svc.VoidRepairCheckAsync(ac.HandoverRepairCheckId, "huy thu", Gc);
            var v2 = await svc.VoidRepairCheckAsync(ac.HandoverRepairCheckId, "huy thu", 1);
            var v3 = await svc.VoidRepairCheckAsync(ac.HandoverRepairCheckId, "huy thu", Kcs);
            Ok(k.Ok && !v1.Ok && !v2.Ok && !v3.Ok && v1.Error.Contains("không huỷ riêng") && v1.Error.Contains($"Kết quả sửa #{rep.HandoverRepairResultId}"),
               "Huỷ riêng dòng tự xác nhận bị chặn (người ghi / Admin / KCS), lời báo chỉ đường huỷ Kết quả sửa", v1.Error + " | " + v2.Error + " | " + v3.Error);
            Ok(!ac.IsVoided && await FullSig(db) == sig0, "Bị chặn thì không đổi gì");
            var reps = await svc.GetRepairsAsync(1);
            var it = reps.Single(r => r.Id == rep.HandoverRepairResultId);
            Ok(it.AutoOk && it.Checks.Count == 1 && it.Checks[0].Auto && it.Waiting == 0, "Lịch sử (GetRepairsAsync): Kết quả sửa có cờ 'đạt không cần KCS', dòng xác nhận có cờ Auto, chờ 0");
            var pend = await svc.GetRepairPendingCountPerDetailAsync(new[] { 1 });
            Ok(pend.Count == 0, "Số trên nút Nhận của KCS không cộng chứng từ mới", string.Join(",", pend));
        }

        Console.WriteLine("[N5] Huỷ Kết quả sửa mới (hàng đạt chưa dùng) → huỷ luôn dòng tự xác nhận, sổ về như chưa ghi");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcNc2(db, svc);
            var sig0 = await FullSig(db);
            await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 2, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep = db.HandoverRepairResults.List.Last(); var ac = AutoChecks(db, rep.HandoverRepairResultId).Single();
            var deny = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "huy thu", 99);
            Ok(!deny.Ok && !rep.IsVoided && !ac.IsVoided, "Người không có quyền huỷ bị chặn, không đổi gì", deny.Error);
            var v = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "ghi nham", Gc);
            await Check(db, "N5 huỷ");
            Ok(v.Ok && rep.IsVoided && ac.IsVoided && ac.VoidReason == "ghi nham" && ac.VoidedBy == Gc && ac.VoidedAt == rep.VoidedAt,
               "Huỷ được: Kết quả sửa + dòng tự xác nhận cùng huỷ (cùng lý do, người, giờ — chỉ ghi cờ, không xoá dòng)", v.Error);
            Ok(await FullSig(db) == sig0, "Sổ về đúng như trước khi ghi Kết quả sửa");
            var again = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "lan 2", Gc);
            Ok(!again.Ok, "Huỷ lần 2 bị chặn (đã huỷ)", again.Error);
        }

        Console.WriteLine("[N6] K5: hàng đạt đã dùng → huỷ Kết quả sửa mới bị chặn, gợi ý đúng chứng từ (không gợi ý 'lần KCS xác nhận')");
        {
            // (a) Kho sửa phôi đạt 1 rồi giao đi → huỷ bị chặn ô âm thường
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            await svc.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 1, 0, null, null, 1);
            var rep = db.HandoverRepairResults.List.Last();
            await svc.IssueByCellAsync(1, "KHO", "GC", 1, 1, "MOI", false, "MOI");
            var tx = LastTx(db);
            var sig0 = await FullSig(db);
            var v = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", 1);
            Ok(!v.Ok && v.Error.Contains("K5") && v.Error.Contains($"Phiếu #{tx}") && !v.Error.Contains("lần KCS xác nhận") && await FullSig(db) == sig0 && !rep.IsVoided && AutoChecks(db, rep.HandoverRepairResultId).All(c => !c.IsVoided),
               "N6a phôi đã sửa đã giao đi → huỷ bị chặn, gợi ý phiếu giao, không ghi gì", v.Error);
            var gy = await svc.VoidIssueAsync(tx, "go theo goi y", 1, new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" });
            var v2 = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", 1);
            await Check(db, "N6a gỡ theo gợi ý");
            Ok(gy.Ok && v2.Ok, "Gỡ theo gợi ý (huỷ phiếu giao) rồi huỷ Kết quả sửa được", gy.Error + " | " + v2.Error);
        }
        {
            // (b) K5 THEO THỜI GIAN: NC3 hết chờ → KQ1 @OK (+1 chờ NC3) → WTS NC3 1 → KQ2 @OK (+1) → huỷ KQ1: số cuối không âm nhưng lúc trước KQ2 âm → phải chặn
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcNc2(db, svc, nc3Ok: 18);
            await Task.Delay(20);
            var k1 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep1 = db.HandoverRepairResults.List.Last();
            await Task.Delay(20);
            var w = await VuHelp.Wts(db, "GC", "NC3", null, 1, 0, Gc, 0);
            await Task.Delay(20);
            var k2 = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 1, 0, HandoverLedgerService.RepOk, null, Gc);
            var b = await Check(db, "N6b dựng");
            var nc3 = b.G("GC").Steps.First(s => s.Code == "NC3");
            Ok(k1.Ok && k2.Ok && nc3.Waiting == 1, "N6b dựng xong: chờ NC3 = 1 (chiếc của KQ2)", $"{k1.Error} | {k2.Error} | chờ NC3 {nc3.Waiting}");
            // tiền đề: bỏ KQ1 thì số cuối KHÔNG âm (chỉ luật theo thời gian bắt được)
            var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(1))!;
            var sim = inp.ShallowCopy(); HandoverWhy.OpVoidRepair(rep1.HandoverRepairResultId)(sim);
            var after = HandoverLedgerService.Compute(sim);
            Ok(HandoverLedgerService.NewNegatives(b, after).Count == 0 && HandoverLedgerService.NewLoopKeys(inp, b, sim).Count > 0,
               "N6b tiền đề: bỏ KQ1 không làm ô âm ở số cuối, chỉ âm theo thời gian (trước lúc KQ2 về)");
            var sig0 = await FullSig(db);
            var v = await svc.VoidRepairResultAsync(rep1.HandoverRepairResultId, "thu", Gc);
            Ok(!v.Ok && v.Error.Contains("K5") && v.Error.Contains($"WTS #{w.WtsLogId}") && !v.Error.Contains("lần KCS xác nhận") && await FullSig(db) == sig0,
               "N6b huỷ KQ1 bị chặn theo thời gian (chiếc đạt của KQ1 đã được WTS NC3 dùng trước lúc KQ2 về), gợi ý dòng WTS", v.Error);
            // đường cũ cũng chặn y vậy (huỷ lần KCS xác nhận — luật [K5-VONG-SUA])
            WtsSvcHelp.Reset();
            var dbc = Seed(); var svcc = new HandoverService(dbc);
            var vuc = await PreGcNc2(dbc, svcc, nc3Ok: 18);
            long c1;
            using (KieuCu()) await svcc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vuc, 1, 0, HandoverLedgerService.RepOk, null, Gc);
            await svcc.ConfirmRepairAsync(LastRep(dbc), 1, 0, null, Kcs); c1 = LastChk(dbc);
            await Task.Delay(20);
            await VuHelp.Wts(dbc, "GC", "NC3", null, 1, 0, Gc, 0);
            await Task.Delay(20);
            using (KieuCu()) await svcc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vuc, 1, 0, HandoverLedgerService.RepOk, null, Gc);
            await svcc.ConfirmRepairAsync(LastRep(dbc), 1, 0, null, Kcs);
            var vc = await svcc.VoidRepairCheckAsync(c1, "thu", Kcs);
            Ok(!vc.Ok && vc.Error.Contains("K5"), "N6b đường cũ (huỷ lần KCS xác nhận tương ứng) cũng bị chặn — cùng luật", vc.Error);
            // gỡ theo gợi ý: xoá dòng WTS NC3 rồi huỷ KQ1 được
            var xw = await WtsSvcHelp.VoidAsync(db, w.WtsLogId, 1, Gc);
            var v3 = await svc.VoidRepairResultAsync(rep1.HandoverRepairResultId, "thu lai", Gc);
            await Check(db, "N6b gỡ theo gợi ý");
            Ok(xw.Ok && v3.Ok, "Gỡ theo gợi ý (xoá dòng WTS NC3) rồi huỷ KQ1 được", xw.Err + " | " + v3.Error);
        }

        Console.WriteLine("[N6c] Gợi ý gỡ có bước 'Huỷ Kết quả sửa' (không phải 'lần KCS xác nhận'): Kho nhận lại 2 phôi lỗi, sửa đạt 2 → huỷ lần nhận lại bị chặn");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var rcvBack = db.HandoverReceives.List.Last(r => !r.IsVoided);
            var k = await svc.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 2, 0, null, null, 1);
            var rep = db.HandoverRepairResults.List.Last();
            var sig0 = await FullSig(db);
            var v = await svc.VoidReceiveAsync(rcvBack.ReceiveId, "thu", 1, new[] { "KHO", "GC" });
            Ok(k.Ok && !v.Ok && v.Error.Contains($"Kết quả sửa #{rep.HandoverRepairResultId}") && !v.Error.Contains("KCS xác nhận #") && await FullSig(db) == sig0,
               "Huỷ lần Kho nhận lại phôi lỗi bị chặn; gợi ý gỡ = huỷ Kết quả sửa (không gợi ý huỷ dòng tự xác nhận)", v.Error);
            var vk = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "go theo goi y", 1);
            var v2 = await svc.VoidReceiveAsync(rcvBack.ReceiveId, "thu lai", 1, new[] { "KHO", "GC" });
            await Check(db, "N6c gỡ theo gợi ý");
            Ok(vk.Ok && v2.Ok, "Làm theo gợi ý (huỷ Kết quả sửa) rồi huỷ lần nhận lại được", vk.Error + " | " + v2.Error);
        }

        Console.WriteLine("[N7] Chứng từ cũ (ghi trước 23/11, đạt chờ KCS) giữ nghĩa cũ: KCS xác nhận được, Chờ KCS còn hiện, huỷ như cũ");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcNc2(db, svc);
            (bool Ok, string Error) k;
            using (KieuCu()) k = await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Loi, vu, 2, 0, HandoverLedgerService.RepOk, null, Gc);
            var rep = db.HandoverRepairResults.List.Last();
            var a = await Check(db, "N7 chứng từ cũ");
            Ok(k.Ok && AutoChecks(db, rep.HandoverRepairResultId).Count == 0 && a.G("GC").ChoKcs == 2, "Chứng từ cũ: không có dòng tự xác nhận, Chờ KCS 2", k.Error);
            var pend = await svc.GetRepairPendingCountPerDetailAsync(new[] { 1 });
            Ok(pend.TryGetValue(1, out var pc) && pc == 1, "Số trên nút Nhận của KCS vẫn cộng chứng từ cũ đang chờ", string.Join(",", pend));
            var it = (await svc.GetRepairsAsync(1)).Single(r => r.Id == rep.HandoverRepairResultId);
            Ok(!it.AutoOk && it.Waiting == 2, "Lịch sử: chứng từ cũ không có cờ 'đạt không cần KCS', chờ 2");
            var self = await svc.ConfirmRepairAsync(rep.HandoverRepairResultId, 1, 0, null, Gc);
            Ok(!self.Ok && self.Error.Contains("khác người"), "Người ghi tự xác nhận chứng từ cũ vẫn bị chặn như cũ", self.Error);
            var x = await svc.ConfirmRepairAsync(rep.HandoverRepairResultId, 1, 0, null, Kcs);
            var a2 = await Check(db, "N7 KCS xác nhận 1");
            Ok(x.Ok && a2.G("GC").ChoKcs == 1, "KCS xác nhận đạt 1 chứng từ cũ được, Chờ KCS 1", x.Error);
            var vk = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", Gc);
            Ok(!vk.Ok && vk.Error.Contains("lần KCS xác nhận"), "Huỷ Kết quả sửa cũ còn lần KCS xác nhận bị chặn như cũ", vk.Error);
            var vc = await svc.VoidRepairCheckAsync(LastChk(db), "xac nhan nham", Kcs);
            var vk2 = await svc.VoidRepairResultAsync(rep.HandoverRepairResultId, "thu", Gc);
            await Check(db, "N7 huỷ");
            Ok(vc.Ok && vk2.Ok, "Huỷ lần KCS xác nhận rồi huỷ Kết quả sửa cũ được (như cũ)", vc.Error + " | " + vk2.Error);
        }

        Console.WriteLine("[N8] Dòng B + khung hàng lỗi: chứng từ mới là 1 dòng B 'Kết quả sửa … không cần KCS xác nhận', không có bước KCS xác nhận");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreGcSuaHo(db, svc);
            await svc.CreateRepairResultAsync(1, "GC", HandoverCell.Sua, vu, 2, 0, "NC1", null, Gc);
            var rep = db.HandoverRepairResults.List.Last(); var ac = AutoChecks(db, rep.HandoverRepairResultId).Single();
            var led = new HandoverLedgerService(db);
            var inp = (await led.GetInputForDetailAsync(1))!;
            var steps = HandoverLedgerService.ComputeSteps(inp);
            var kq = steps.Where(s => s.Kind == "KQSUA" && s.RefId == rep.HandoverRepairResultId).ToList();
            Ok(kq.Count == 1 && kq[0].Text.Contains("không cần KCS xác nhận") && !kq[0].Text.Contains("chờ KCS")
               && !steps.Any(s => s.Kind == "XNSUA" && s.RefId == ac.HandoverRepairCheckId),
               "1 dòng B Kết quả sửa (chữ 'không cần KCS xác nhận'), không có dòng 'KCS xác nhận'", string.Join(" || ", steps.Where(s => s.Kind is "KQSUA" or "XNSUA").Select(s => s.Kind + ": " + s.Text)));
            var tr = HandoverNgTrace.Build(inp);
            var ev = tr.Events.Where(e => e.RefId == rep.HandoverRepairResultId && e.Type == "KQ_SUA").ToList();
            Ok(tr.Problems.Count == 0 && ev.Count == 1 && ev[0].VeSx == 2 && !tr.Events.Any(e => e.Type == "KCS_XN") && tr.DangChuyen == 0,
               "Khung hàng lỗi: Kết quả sửa đạt 2 = xử lý xong 2 ngay (không 'đang chuyển', không chặng KCS)", string.Join(" | ", tr.Problems));
            var row = (await led.GetLedgerForDetailAsync(1))!;
            var hops = row.Cases[vu].Hops.Where(h => h.Kind == "XN").ToList();
            Ok(hops.Count == 1 && hops[0].Other == null && HandoverNgTrace.HopLabel(hops[0]) == "Sửa đạt (không cần KCS)",
               "Chặng vụ: 'Sửa đạt (không cần KCS)' (không có nơi KCS)", string.Join(",", hops.Select(h => HandoverNgTrace.HopLabel(h) + "/" + h.Other)));
            Ok(!row.G("GC").Explains["Ton"].Terms.Any(t => t.Label.Contains("Chờ KCS")), "Diễn giải Tồn Gia công không còn số hạng Chờ KCS");
        }

        Console.WriteLine("[N9] Form 'Giao / xử lý hàng' (SaveFormAsync) nút Sửa tại nhóm: cũng tự xác nhận trong cùng giao dịch");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var b = await Check(db, "N9 trước");
            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = 1, Group = "KHO", UserId = 1, Kind = HandoverForm.Loi, Pos = "TONLOI", CaseId = vu, Action = HandoverForm.ASua, Pass = 2, Notes = "sua" });
            var a = await Check(db, "N9 form");
            var rep = db.HandoverRepairResults.List.Last();
            Ok(f.Ok && AutoChecks(db, rep.HandoverRepairResultId).Count == 1 && a.G("KHO").ConGiao == b.G("KHO").ConGiao + 2 && a.G("KHO").ChoKcs == 0,
               "Form: Kết quả sửa đạt 2 → phôi còn +2 ngay, có dòng tự xác nhận", f.Error);
        }

        Console.WriteLine("[N10] Người nhóm KCS tự sửa hàng của KCS (vd KCS sửa tại chỗ) — không còn luật 'người KCS khác người ghi' cho chứng từ mới");
        {
            WtsSvcHelp.Reset();
            var db = Seed(); var svc = new HandoverService(db);
            var vu = await PreKhoLoi(db, svc);
            var k = await svc.CreateRepairResultAsync(1, "KHO", HandoverCell.Loi, vu, 1, 0, null, null, Kcs);
            var rep = db.HandoverRepairResults.List.Last(); var ac = AutoChecks(db, rep.HandoverRepairResultId);
            await Check(db, "N10");
            Ok(k.Ok && ac.Count == 1 && ac[0].CheckedBy == Kcs, "Kết quả sửa do người KCS ghi: tự xác nhận (người = người ghi)", k.Error);
        }

        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
