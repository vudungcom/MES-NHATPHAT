// [H10-KET-WHY 2026-11-07] Kịch bản `why`: gợi ý gỡ K5 ([GN-WHY] HandoverWhy.Plan) trên SỔ CHỨNG TỪ THẬT ghi lại từ các ca "[H10 kẹt]"
//   của fuzz WHYALL=1 (tools/GiaoNhanFuzz/why_ca/*.json — fuzz không tất định theo giờ chạy nên giữ đúng sổ lúc kẹt để kiểm lại lần nào cũng ra y).
//   Mỗi ca: huỷ lần nhận <rid> bị K5 chặn; gợi ý phải tìm ra đường (Complete), làm theo từng bước trên bản sao đều làm được (không bước nào tự bị
//   K5 chặn / theo thời gian, huỷ phiếu chỉ khi hết lần nhận), cuối cùng huỷ lần nhận gốc lưu được; không có bước thừa (bỏ bất kỳ 1 bước nào thì
//   không còn gỡ được). Bản HEAD 0594d93: Y1 / Y2 / Y3 SAI (gợi ý dừng sau 1 / 3 / 3 bước — không thử lùi). Nguồn: CLAUDE (sổ dòng H10-KET-WHY,
//   fuzz WHYALL seed 44 07/11); Hùng 07/11 07:55 giao Gói 5.
using MES.Web.Services;

public static class WhyScen
{
    static int fails = 0;
    static void Ok(bool cond, string name, string? info = null)
    {
        Console.WriteLine((cond ? "  ĐẠT  " : "  SAI  ") + name + (info != null ? "\n         → " + info.Replace("\n", "\n           ") : ""));
        if (!cond) fails++;
    }

    static Action<LedgerInput> Op(HandoverWhy.WhyStep st) => st.Kind switch
    {
        "WTS" => HandoverWhy.OpWts(new[] { st.Id }),
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
                "NHAN" => sim.Txs.Any(x => x.Receives.Any(y => y.Id == st.Id)),
                "BAOLOI" => sim.NgReps.Any(b => b.Id == st.Id),
                "KQSUA" => sim.Repairs.Any(x => x.Id == st.Id && !x.Checks.Any()),
                "XNSUA" => sim.Repairs.Any(x => x.Checks.Any(c => c.Id == st.Id)),
                _ => sim.Txs.Any(x => x.Id == st.Id && !x.Receives.Any()),   // huỷ phiếu: phiếu còn lần nhận thì trang không cho
            };
            if (!exists) return $"bước {st.Kind}#{st.Id} không làm được (chứng từ đã mất / phiếu còn lần nhận / Kết quả sửa còn lần KCS xác nhận)";
            var bad = Bad(sim, Op(st), st.Kind is "NHAN" or "PHIEU" or "XNSUA" or "WTS");
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
        // các ca (mã in ở đầu ca — DanhMucKiem trỏ tới): [Y1] [Y2] [Y3] [Y5] (+ [Y4] cuối file). y2 / y3 cùng 1 sổ (PO 25), khác lần nhận đem huỷ
        var cases = new (string Code, string File, long Rid, string What)[]
        {
            ("Y1", "y1_s44_po54_r3.json", 3, "huỷ lần Gia công nhận hàng Kho sửa xong trả về (đã ghi WTS NC 1–3) — đường đúng là xoá WTS NC 3 → 2 → 1, không huỷ lần Kho nhận phiếu trả NG"),
            ("Y2", "y2_s44_po25_r5.json", 5, "huỷ lần KCS nhận (ô Hàng lỗi vụ 5) — chuỗi phiếu Gia công ↔ Kho ↔ KCS"),
            ("Y3", "y3_s44_po25_r8.json", 8, "huỷ lần Kho nhận phiếu KCS trả (Kho phôi còn giao âm)"),
            // [H10-KET-WHY-2 2026-11-07] ca fuzz WHYALL chuoi_lamho seed 741 PO 34 (trước ở why_mo/h10_s741_po34_r5.json): huỷ lần Gia công nhận phôi #5
            //   sau "sửa WTS NC 1 NG 4 → 2 giữ vụ" — đường đúng phải huỷ lần GC nhận #10 / phiếu #9 / lần HTSP nhận #8 / phiếu #8 / WTS #13 TRƯỚC khi xoá
            //   WTS #11 NC 2 (NC 3 còn chỗ). Bản becb81e: gợi ý dừng sau 7 bước. Nguồn: CLAUDE (sổ dòng H10-KET-WHY-2); Hùng 07/11 22:38 giao Gói 8.
            ("Y5", "y5_s741_po34_r5.json", 5, "huỷ lần Gia công nhận phôi (đã ghi WTS NC 1–3, hàng sửa đi Kho ↔ GC ↔ HTSP) — phải gỡ nhánh NC 3 trước khi xoá WTS NC 2"),
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
        Console.WriteLine(fails == 0 ? "KẾT QUẢ: ĐẠT hết" : $"KẾT QUẢ: {fails} SAI");
        return fails == 0 ? 0 : 1;
    }
}
