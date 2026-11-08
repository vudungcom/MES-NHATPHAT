// [H10-KET-WHY 2026-11-07] Kịch bản `why`: gợi ý gỡ K5 ([GN-WHY] HandoverWhy.Plan) trên SỔ CHỨNG TỪ THẬT ghi lại từ các ca "[H10 kẹt]"
//   của fuzz WHYALL=1 (tools/GiaoNhanFuzz/why_ca/*.json — fuzz không tất định theo giờ chạy nên giữ đúng sổ lúc kẹt để kiểm lại lần nào cũng ra y).
//   Mỗi ca: huỷ lần nhận <rid> bị K5 chặn; gợi ý phải tìm ra đường (Complete), làm theo từng bước trên bản sao đều làm được (không bước nào tự bị
//   K5 chặn / theo thời gian, huỷ phiếu chỉ khi hết lần nhận), cuối cùng huỷ lần nhận gốc lưu được; không có bước thừa (bỏ bất kỳ 1 bước nào thì
//   không còn gỡ được). Bản HEAD 0594d93: Y1 / Y2 / Y3 SAI (gợi ý dừng sau 1 / 3 / 3 bước — không thử lùi). Nguồn: CLAUDE (sổ dòng H10-KET-WHY,
//   fuzz WHYALL seed 44 07/11); Hùng 07/11 07:55 giao Gói 5.
using MES.Web.Services;
using MES.Web.Data.Entities;

public static class WhyScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    // [H10-KET-WHY-3 2026-11-08] Thao tác từng bước tự viết (đáp án độc lập với StepOp của Plan); WTSV = sửa dòng về số bản trước, thêm 1 bản MaxValue
    //   vào chuỗi như máy chủ kiểm lúc sửa (HandoverLedgerService.SnapshotBefore)
    static Action<LedgerInput> Op(HandoverWhy.WhyStep st) => st.Kind switch
    {
        "WTS" => HandoverWhy.OpWts(new[] { st.Id }),
        "WTSV" => s => s.Wts = s.Wts.Select(w => w.Id != st.Id || w.Vers is not { Count: >= 2 } v ? w
                         : w with { Ok = v[^2].Ok, Ng = v[^2].Ng, Vers = v.Append(new WtsVer(DateTime.MaxValue, v[^2].Ok, v[^2].Ng)).ToList() }).ToList(),
        "NHAN" => HandoverWhy.OpVoidReceive(st.Id),
        "BAOLOI" => HandoverWhy.OpVoidNgRep(st.Id),
        "KQSUA" => HandoverWhy.OpVoidRepair(st.Id),
        "XNSUA" => HandoverWhy.OpVoidRepairCheck(st.Id),
        _ => HandoverWhy.OpVoidIssue(st.Id),
    };

    /// <summary>Làm theo các bước rồi huỷ lần nhận gốc: null = được; khác = lý do không được. Đáp án độc lập (không dùng hàm bỏ bước thừa của Plan).</summary>
    static string? Follow(LedgerInput input, List<HandoverWhy.WhyStep> steps, long rid)
    {
        var sim = HandoverWhyTest.CloneInput(input);
        List<string> Bad(LedgerInput s, Action<LedgerInput> o, bool loop)
        {
            var r0 = HandoverLedgerService.Compute(s); var a = HandoverWhyTest.CloneInput(s); o(a);
            var bad = HandoverLedgerService.NewNegativeKeys(r0, HandoverLedgerService.Compute(a));
            if (loop && HandoverLedgerService.RepairedReturnTimes(a).Count > 0)
                bad.AddRange(HandoverLedgerService.NewLoopKeys(s, r0, a, HandoverLedgerService.LoopTimeline(s)).Select(x => x.Key));
            return bad;
        }
        foreach (var st in steps)
        {
            bool exists = st.Kind switch
            {
                "WTS" => sim.Wts.Any(w => w.Id == st.Id),
                "WTSV" => sim.Wts.Any(w => w.Id == st.Id && w.Vers is { Count: >= 2 } v && v[^1].At != DateTime.MaxValue),   // [H10-KET-WHY-3] chưa sửa về lần nào
                "NHAN" => sim.Txs.Any(x => x.Receives.Any(y => y.Id == st.Id)),
                "BAOLOI" => sim.NgReps.Any(b => b.Id == st.Id),
                "KQSUA" => sim.Repairs.Any(x => x.Id == st.Id && !x.Checks.Any()),
                "XNSUA" => sim.Repairs.Any(x => x.Checks.Any(c => c.Id == st.Id)),
                _ => sim.Txs.Any(x => x.Id == st.Id && !x.Receives.Any()),   // huỷ phiếu: phiếu còn lần nhận thì trang không cho
            };
            if (!exists) return $"bước {st.Kind}#{st.Id} không làm được (chứng từ đã mất / phiếu còn lần nhận / Kết quả sửa còn lần KCS xác nhận)";
            var bad = Bad(sim, Op(st), st.Kind is "NHAN" or "PHIEU" or "XNSUA" or "WTS" or "WTSV");   // [H10-KET-WHY-3] sửa dòng WTS = huỷ chứng từ nguồn (xét theo thời gian)
            if (bad.Count > 0) return $"bước {st.Kind}#{st.Id} tự nó bị K5 chặn ({string.Join(", ", bad)})";
            var s2 = HandoverWhyTest.CloneInput(sim); Op(st)(s2); sim = s2;
        }
        if (!sim.Txs.Any(x => x.Receives.Any(y => y.Id == rid))) return "lần nhận gốc đã mất sau các bước";
        var badF = Bad(sim, HandoverWhy.OpVoidReceive(rid), true);
        return badF.Count == 0 ? null : "làm theo gợi ý xong vẫn bị K5 chặn: " + string.Join(", ", badF);
    }

    public static int Run()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "why_ca");
        if (!Directory.Exists(dir)) dir = "why_ca";
        // các ca (mã in ở đầu ca — DanhMucKiem trỏ tới): [Y1] [Y2] [Y3] [Y5] [Y8] (+ [Y4] [Y9] [Y10] [Y11] cuối file). y2 / y3 cùng 1 sổ (PO 25), khác lần nhận đem huỷ
        var cases = new (string Code, string File, long Rid, string What)[]
        {
            ("Y1", "y1_s44_po54_r3.json", 3, "huỷ lần Gia công nhận hàng Kho sửa xong trả về (đã ghi WTS NC 1–3) — đường đúng là xoá WTS NC 3 → 2 → 1, không huỷ lần Kho nhận phiếu trả NG"),
            ("Y2", "y2_s44_po25_r5.json", 5, "huỷ lần KCS nhận (ô Hàng lỗi vụ 5) — chuỗi phiếu Gia công ↔ Kho ↔ KCS"),
            ("Y3", "y3_s44_po25_r8.json", 8, "huỷ lần Kho nhận phiếu KCS trả (Kho phôi còn giao âm)"),
            // [H10-KET-WHY-2 2026-11-07] ca fuzz WHYALL chuoi_lamho seed 741 PO 34 (trước ở why_mo/h10_s741_po34_r5.json): huỷ lần Gia công nhận phôi #5
            //   sau "sửa WTS NC 1 NG 4 → 2 giữ vụ" — đường đúng phải huỷ lần GC nhận #10 / phiếu #9 / lần HTSP nhận #8 / phiếu #8 / WTS #13 TRƯỚC khi xoá
            //   WTS #11 NC 2 (NC 3 còn chỗ). Bản becb81e: gợi ý dừng sau 7 bước. Nguồn: CLAUDE (sổ dòng H10-KET-WHY-2); Hùng 07/11 22:38 giao Gói 8.
            ("Y5", "y5_s741_po34_r5.json", 5, "huỷ lần Gia công nhận phôi (đã ghi WTS NC 1–3, hàng sửa đi Kho ↔ GC ↔ HTSP) — phải gỡ nhánh NC 3 trước khi xoá WTS NC 2"),
            // [H10-KET-WHY-3 2026-11-08] ca fuzz WHYALL chuoi_lamho seed 801 PO 10 (trước ở why_mo/h10_s801_po10_r4.json): huỷ lần Kho → HTSP nhận #4; dòng WTS
            //   Bavia B1 #13 đã sửa NG 1 → 2 SAU lúc KCS xác nhận hàng sửa — chỉ gỡ được khi SỬA GIẢM dòng về NG 1 trước (bước WTSV) rồi mới huỷ xác nhận.
            //   Bản a32c59a: Y8a SAI (0 bước, "chưa lần ra"). Nguồn: CLAUDE (sổ dòng H10-KET-WHY-3); Hùng 08/11 11:24 OK Gói 10.
            ("Y8", "y8_s801_po10_r4.json", 4, "huỷ lần Kho → HTSP nhận (WTS Bavia B1 đã sửa NG 1 → 2 sau lúc KCS xác nhận hàng sửa) — phải gợi ý sửa dòng về NG 1"),
        };
        foreach (var (code, file, rid, what) in cases)
        {
            Console.WriteLine($"[{code}] {what}");
            var input = System.Text.Json.JsonSerializer.Deserialize<LedgerInput>(File.ReadAllText(Path.Combine(dir, file)),
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
            var opDocs = new HashSet<string> { "R:" + rid };
            foreach (var b in input.Txs.Where(b => b.SourceReceiveId == rid)) { opDocs.Add("T:" + b.Id); foreach (var br in b.Receives) opDocs.Add("R:" + br.Id); }
            var before = HandoverLedgerService.Compute(input);
            var a = HandoverWhyTest.CloneInput(input); HandoverWhy.OpVoidReceive(rid)(a);
            Ok(HandoverLedgerService.NewNegativeKeys(before, HandoverLedgerService.Compute(a)).Any(), $"{code} tiền đề: huỷ lần nhận #{rid} ngay bị K5 chặn");
            var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid), opDocs, loopOp: true);   // trần mặc định như trang (400 lần tính / 5 s)
            var steps = string.Join(" → ", plan.Steps.Select(s => s.Kind + "#" + s.Id));
            Ok(plan.Complete, $"{code}a gợi ý tìm ra đường gỡ trong trần mặc định (trước: dừng giữa chừng)", $"{plan.Steps.Count} bước: {steps} · {plan.Computes} lần tính");
            var why = Follow(input, plan.Steps, rid);
            Ok(why == null, $"{code}b làm theo từng bước (trên bản sao) đều làm được, cuối cùng huỷ lần nhận gốc lưu được", why ?? steps);
            var extra = plan.Steps.Select((s, i) => (s, i)).Where(x => Follow(input, plan.Steps.Where((_, k) => k != x.i).ToList(), rid) == null).Select(x => x.s.Kind + "#" + x.s.Id).ToList();
            Ok(extra.Count == 0, $"{code}c không có bước thừa (bỏ bất kỳ 1 bước nào thì không gỡ được)", extra.Count == 0 ? null : "thừa: " + string.Join(", ", extra));
        }
        // [Y4] không có đường gỡ (ca Y2, cấm đụng phiếu #7 — bước cuối của đường duy nhất): gợi ý phải dừng sớm, không chạy tới trần 400 lần tính / 5 s
        //   (rà độc lập gói 5 mục 2: thử lùi không giới hạn làm người dùng chờ tới 5 s mỗi lần bị chặn).
        {
            Console.WriteLine("[Y4] không có đường gỡ → gợi ý dừng sớm (không chạy tới trần)");
            var input = System.Text.Json.JsonSerializer.Deserialize<LedgerInput>(File.ReadAllText(Path.Combine(dir, "y2_s44_po25_r5.json")),
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
            var opDocs = new HashSet<string> { "R:5", "T:7", "R:8" };   // ca Y2 cấm đụng phiếu KCS → Kho #7 (bước cuối bắt buộc của đường duy nhất)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(5), opDocs, loopOp: true);
            sw.Stop();
            Ok(!plan.Complete && plan.Computes < 200 && sw.ElapsedMilliseconds < 3000,
               "Y4 không có đường: báo \"chưa lần ra\" sau ít lần tính (< 200, trần 400)", $"Complete={plan.Complete} · {plan.Computes} lần tính · {sw.ElapsedMilliseconds} ms · {plan.Steps.Count} bước");
            Ok(!plan.Steps.Any(st => st.Id == 7 || st.Id == 8), "Y4 phần gợi ý dở không chứa bước bị cấm", string.Join(" → ", plan.Steps.Select(x => x.Kind + "#" + x.Id)));
        }
        Y9().GetAwaiter().GetResult();   // [H10-KET-WHY-3]
        Y11().GetAwaiter().GetResult();  // [H10-KET-WHY-3] đột biến M5 / M6
        // [Y10] [GOI8-THAP-D 2026-11-08] ô "HTSP|ChoKcs" + "VU|5|CHUYEN" âm: thao tác thật không làm 2 ô này mới âm (phân tích ở HandoverWhy) → dựng thử trên sổ Y8
        //   bằng thao tác giả "Kết quả sửa #2 còn đạt 0" (KCS đã xác nhận 1). Gợi ý phải ra lần KCS xác nhận #1 (trước: không ứng viên, "chưa lần ra").
        //   Bản a32c59a: Y10a SAI. Nguồn: CLAUDE (sổ GOI8-THAP (d)); Hùng 08/11 11:24 OK Gói 10.
        {
            Console.WriteLine("[Y10] ô hàng sửa chờ KCS / vụ đang chuyển âm → gợi ý huỷ lần KCS xác nhận");
            var input = System.Text.Json.JsonSerializer.Deserialize<LedgerInput>(File.ReadAllText(Path.Combine(dir, "y8_s801_po10_r4.json")),
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
            Action<LedgerInput> op = s0 => s0.Repairs = s0.Repairs.Select(r => { if (r.Id != 2) return r; var c = r.CloneRep(); c.Pass = 0; return c; }).ToList();
            var a = HandoverWhyTest.CloneInput(input); op(a);
            var bad = HandoverLedgerService.NewNegativeKeys(HandoverLedgerService.Compute(input), HandoverLedgerService.Compute(a));
            Ok(bad.Contains("HTSP|ChoKcs") && bad.Contains("VU|5|CHUYEN"), "Y10 tiền đề: thao tác giả làm ô HTSP|ChoKcs và VU|5|CHUYEN mới âm", string.Join(", ", bad));
            var plan = HandoverWhy.Plan(input, op, new HashSet<string> { "K:2" });
            // huỷ lần KCS xác nhận #1 tự nó vướng K5 theo thời gian (dòng Bavia #13 sửa NG 1 → 2 sau lúc xác nhận — chuyện ca Y8) → trước đó phải sửa #13 về NG 1
            Ok(plan.Complete && plan.Steps.Count >= 1 && plan.Steps[^1].Kind == "XNSUA" && plan.Steps[^1].Id == 1 && Follow2(input, plan.Steps, op) == null,
               "Y10a gợi ý ra đường: bước cuối huỷ lần KCS xác nhận #1, làm theo trên bản sao gỡ được (trước: không ứng viên → chưa lần ra)", string.Join(" → ", plan.Steps.Select(x => x.Kind + "#" + x.Id)) + $" · Complete={plan.Complete}");
        }
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>[Y10] Như Follow nhưng thao tác gốc tuỳ ý (không phải huỷ lần nhận).</summary>
    static string? Follow2(LedgerInput input, List<HandoverWhy.WhyStep> steps, Action<LedgerInput> op)
    {
        var sim = HandoverWhyTest.CloneInput(input);
        foreach (var st in steps)
        {
            var r0 = HandoverLedgerService.Compute(sim); var a = HandoverWhyTest.CloneInput(sim); Op(st)(a);
            var bad = HandoverLedgerService.NewNegativeKeys(r0, HandoverLedgerService.Compute(a));
            if (HandoverLedgerService.RepairedReturnTimes(a).Count > 0) bad.AddRange(HandoverLedgerService.NewLoopKeys(sim, r0, a, HandoverLedgerService.LoopTimeline(sim)).Select(x => x.Key));
            if (bad.Count > 0) return $"bước {st.Kind}#{st.Id} bị chặn: {string.Join(", ", bad)}";
            sim = a;
        }
        var f = HandoverWhyTest.CloneInput(sim); op(f);
        var badF = HandoverLedgerService.NewNegativeKeys(HandoverLedgerService.Compute(sim), HandoverLedgerService.Compute(f));
        return badF.Count == 0 ? null : "vẫn chặn: " + string.Join(", ", badF);
    }

    // [Y11] [H10-KET-WHY-3 2026-11-08] Bước WTSV phải được kiểm K5 THEO THỜI GIAN như máy chủ kiểm khi bấm Sửa (đột biến M5 / M6 Gói 10 lọt kịch bản cũ).
    //   Sổ dựng bằng HÀM THẬT: Gia công nhận 1 + 2; WTS NC01 OK 1 rồi SỬA lên OK 3; WTS NC02 OK 1 NG 1 (vụ V); Gia công sửa tại chỗ vụ V đạt 1 → KCS xác nhận
    //   (hàng sửa về NC02 lúc T). Huỷ lần nhận 2 chiếc: NC01 thiếu → "sửa NC01 về OK 1" gỡ được số cuối nhưng ảnh sổ ngay trước T thì NC02 đã làm 2 mà
    //   mới nhận vào 1 (máy chủ chặn sửa theo thứ tự thời gian). Gợi ý phải là đường làm được THẬT: làm từng bước bằng hàm thật (WtsSaveService sửa / xoá,
    //   huỷ lần KCS xác nhận / Kết quả sửa / lần nhận / phiếu) đều lưu được, rồi huỷ lần nhận gốc lưu được. Nguồn: CLAUDE (đột biến Gói 10).
    static async Task Y11()
    {
        Console.WriteLine("[Y11] bước sửa dòng WTS về số trước bị chặn theo thứ tự thời gian (hàng sửa về NC02) → gợi ý đường làm được thật");
        const int U = ChuoiHelp.Admin; const int Kcs = 4;
        var db = new MES.Web.Data.AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false); WtsSvcHelp.Reset();
        var pid = await ChuoiHelp.BuildPartAsync(db, "P-Y11", new ChuoiHelp.Shape { Gc = 2, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
        var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-Y11", new decimal[] { 3 }, "PO-Y11"))[0];
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(det, DateTime.Now, 3, "OVL", "A1", null, null, null, "phoi", U);
        await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 1, 0, null, U);
        await svc.IssueByCellAsync(det, "KHO", "GC", 2, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 2, 0, null, U);
        var rid2 = db.HandoverReceives.List.Last().ReceiveId;
        var w1 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 1, 0, U);
        await Task.Delay(20);
        var w1b = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 3, 0, U, editLogId: w1.LogId);
        {   // giờ LÀM (EndTime) của dòng NC01 ghi muộn nhất (người nhập ghi giờ kết thúc sau) — giờ LƯU vẫn trước lúc hàng sửa về → gợi ý xét dòng này đầu tiên
            var rw = db.WtsProductionLogs.List.Single(x => x.WtsLogId == w1b.LogId); rw.EndTime = DateTime.Now.AddMinutes(50); rw.StartTime = rw.EndTime.AddMinutes(-1); db.Fixup();
        }
        await Task.Delay(20);
        var w2 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC02", null, 1, 1, U, 1);
        var vu = db.WtsProductionLogs.List.Single(x => x.WtsLogId == w2.LogId).NgCaseId;
        await Task.Delay(20);
        var kq = await svc.CreateRepairResultAsync(det, "GC", HandoverCell.Loi, vu, 1, 0, "NC02", null, U);
        var repId = db.HandoverRepairResults.List.Last().HandoverRepairResultId;
        await Task.Delay(20);
        var xn = await svc.ConfirmRepairAsync(repId, 1, 0, null, Kcs);
        var v0 = await svc.VoidReceiveAsync(rid2, "huy thu y11", U, all);
        Ok(w1.Ok && w1b.Ok && w2.Ok && kq.Ok && xn.Ok && !v0.Ok, "Y11 tiền đề: dựng xong, huỷ lần nhận 2 chiếc bị K5 chặn", $"{w1b.Err} | {w2.Err} | {kq.Error} | {xn.Error} | {v0.Error}");
        var input = (await led.GetInputForDetailAsync(det))!;
        var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid2), new HashSet<string> { "R:" + rid2 }, loopOp: true);
        var map = new Dictionary<long, long>();
        long Id(long x) { while (map.TryGetValue(x, out var n2)) x = n2; return x; }
        string? why = null;
        foreach (var st in plan.Steps)
        {
            (bool Ok, string? Err) r;
            switch (st.Kind)
            {
                case "WTSV":
                {
                    var oid = Id(st.Id); var w = db.WtsProductionLogs.List.Single(x => x.WtsLogId == oid);
                    var iw = (await led.GetInputForDetailAsync(det))!.Wts.Single(x => x.Id == oid);
                    var p = iw.Vers![^2];
                    var e = await WtsSvcHelp.SaveAsync(db, det, w.ProcessGroup, w.NC, w.WtsCode, p.Ok, p.Ng, w.WorkerId, 1, p.Ng > 0 ? w.NgCaseId : null, oid);
                    if (e.Ok) map[oid] = e.LogId!.Value; r = (e.Ok, e.Err); break;
                }
                case "WTS": r = await WtsSvcHelp.VoidAsync(db, Id(st.Id), det, U); break;
                case "NHAN": { var x = await svc.VoidReceiveAsync(st.Id, "goi y y11", U, all); r = (x.Ok, x.Error); break; }
                case "XNSUA": { var x = await svc.VoidRepairCheckAsync(st.Id, "goi y y11", Kcs); r = (x.Ok, x.Error); break; }
                case "KQSUA": { var x = await svc.VoidRepairResultAsync(st.Id, "goi y y11", U); r = (x.Ok, x.Error); break; }
                default: { var x = await svc.VoidIssueAsync(st.Id, "goi y y11", U, all); r = (x.Ok, x.Error); break; }
            }
            db.Fixup();
            if (!r.Ok) { why = $"{st.Kind}#{st.Id}: {r.Err}"; break; }
        }
        (bool Ok, string Error) v1 = why == null ? await svc.VoidReceiveAsync(rid2, "huy thu y11", U, all) : (false, "");
        Ok(plan.Complete && why == null && v1.Ok, "Y11a làm theo từng bước gợi ý bằng hàm thật đều lưu được, rồi huỷ lần nhận gốc được",
           $"{string.Join(" → ", plan.Steps.Select(x => x.Kind + "#" + x.Id))} · Complete={plan.Complete} · {why} · {v1.Error}");
    }

    // [Y9] [H10-KET-WHY-3 2026-11-08] Sổ dựng bằng HÀM THẬT: Gia công nhận 2 + 1 (2 lần nhận), WTS NC01 OK 2 rồi SỬA lên OK 3 (sau lần nhận thứ 2).
    //   Huỷ lần nhận thứ 2 bị K5 chặn (NC01 chưa làm −1) → gợi ý phải là "sửa dòng WTS về OK 2" (1 bước, nhẹ hơn xoá cả dòng), lời khuyên có số đích;
    //   làm theo bằng hàm thật (WtsSaveService sửa dòng) rồi huỷ lần nhận được. Bản a32c59a: Y9a SAI (gợi ý xoá cả dòng WTS).
    //   Nguồn: CLAUDE (thiết kế bước WTSV, bản "em hiểu" Gói 10); Hùng 08/11 11:24 OK.
    static async Task Y9()
    {
        Console.WriteLine("[Y9] dòng WTS đã sửa tăng OK 2 → 3, huỷ lần nhận làm thiếu 1 chiếc → gợi ý sửa dòng về OK 2 (không xoá cả dòng)");
        const int U = ChuoiHelp.Admin;
        var db = new MES.Web.Data.AppDbContext(); ChuoiHelp.SeedBase(db, exclH04: false); WtsSvcHelp.Reset();
        var pid = await ChuoiHelp.BuildPartAsync(db, "P-Y9", new ChuoiHelp.Shape { Gc = 2, DongDang = false, DuPhong = false, Taro = 0, Bavia = 0, Rua = 0, Kcs = 1, Pkg = 0, Prep = false });
        var det = (await ChuoiHelp.CreateKhAsync(db, pid, "P-Y9", new decimal[] { 3 }, "PO-Y9"))[0];
        var svc = new HandoverService(db); var led = new HandoverLedgerService(db);
        await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(det, DateTime.Now, 3, "OVL", "A1", null, null, null, "phoi", U);
        await svc.IssueByCellAsync(det, "KHO", "GC", 2, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 2, 0, null, U);
        await svc.IssueByCellAsync(det, "KHO", "GC", 1, U, "MOI", false, "MOI");
        await svc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, 1, 0, null, U);
        var rid2 = db.HandoverReceives.List.Last().ReceiveId;
        var w1 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 2, 0, U);
        await Task.Delay(20);
        var w2 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 3, 0, U, editLogId: w1.LogId);
        var all = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };
        var v0 = await svc.VoidReceiveAsync(rid2, "huy thu y9", U, all);
        Ok(w1.Ok && w2.Ok && !v0.Ok && v0.Error.StartsWith("Không lưu"), "Y9 tiền đề: sửa dòng NC01 OK 2 → 3 được; huỷ lần nhận thứ 2 bị K5 chặn", $"{w1.Err} | {w2.Err} | {v0.Error}");
        var input = (await led.GetInputForDetailAsync(det))!;
        var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid2), new HashSet<string> { "R:" + rid2 }, loopOp: true);
        var st0 = plan.Steps.FirstOrDefault();
        Ok(plan.Complete && plan.Steps.Count == 1 && st0!.Kind == "WTSV" && st0.Id == w2.LogId && (st0.Hint ?? "").Contains("sửa dòng này về OK 2 NG 0"),
           "Y9a gợi ý 1 bước: sửa dòng WTS NC01 về OK 2 NG 0 (số trước lần sửa) — không xoá cả dòng", string.Join(" → ", plan.Steps.Select(x => $"{x.Kind}#{x.Id} {x.Hint}")));
        Ok(v0.Error.Contains("sửa dòng này về OK 2 NG 0"), "Y9b lời báo chặn có bước gợi ý sửa dòng về OK 2 NG 0", v0.Error);
        var r1 = await WtsSvcHelp.SaveAsync(db, det, "GC", "NC01", null, 2, 0, U, editLogId: w2.LogId);   // làm theo gợi ý bằng hàm thật
        // [Y9d] (rà độc lập Gói 10 L1) dòng vừa sửa về (chuỗi 2 → 3 → 2): lần gợi ý sau KHÔNG đề xuất "sửa về OK 3" (ngược lại)
        var inR = (await led.GetInputForDetailAsync(det))!;
        var wr = inR.Wts.FirstOrDefault(x => x.Id == r1.LogId);
        Ok(wr != null && wr.Vers is { Count: 3 } && !HandoverWhy.CanRevertWts(wr),
           "Y9d dòng đã sửa về theo gợi ý (chuỗi 2 → 3 → 2): không đề xuất sửa ngược về 3", $"{wr?.Vers?.Count} bản · {(wr == null ? "" : HandoverWhy.CanRevertWts(wr).ToString())}");
        var v1 = await svc.VoidReceiveAsync(rid2, "huy thu y9", U, all);
        Ok(r1.Ok && v1.Ok, "Y9c làm theo gợi ý (sửa dòng về OK 2 bằng hàm thật) rồi huỷ lần nhận → được", $"{r1.Err} | {v1.Error}");
    }
}
