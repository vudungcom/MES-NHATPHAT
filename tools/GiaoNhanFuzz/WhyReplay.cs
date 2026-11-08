// [H10-KET-WHY 2026-11-07] Tái hiện ca "[H10 kẹt]" của WHYALL=1 từ sổ chứng từ đã ghi ra JSON (Program.cs WhyAllCheck).
//   Fuzz không tất định theo giờ chạy (cùng seed ra đường đi khác) → ghi đúng LedgerInput lúc kẹt rồi chạy lại HandoverWhy.Plan trên đó,
//   in từng bước gợi ý + ô còn âm sau mỗi bước. Chỉ tính, không ghi gì. Nguồn: CLAUDE (sổ dòng H10-KET-WHY); Hùng 07/11 07:55 giao Gói 5.
//   Chạy: dotnet bin2/GiaoNhanFuzz.dll whyreplay <file.json> <id lần nhận>
using MES.Web.Services;

public static class WhyReplay
{
    public static int Run(string file, long rid)
    {
        var input = System.Text.Json.JsonSerializer.Deserialize<LedgerInput>(File.ReadAllText(file),
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
        var before = HandoverLedgerService.Compute(input);
        var a = HandoverWhyTest.CloneInput(input); HandoverWhy.OpVoidReceive(rid)(a);
        Console.WriteLine("Huỷ lần nhận #" + rid + " → ô mới âm: " + string.Join(", ", HandoverLedgerService.NewNegativeKeys(before, HandoverLedgerService.Compute(a))));
        var opDocs = new HashSet<string> { "R:" + rid };
        foreach (var b in input.Txs.Where(b => b.SourceReceiveId == rid)) { opDocs.Add("T:" + b.Id); foreach (var br in b.Receives) opDocs.Add("R:" + br.Id); }
        var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid), opDocs, 5000, 60000, loopOp: true);
        Console.WriteLine($"Plan: Complete={plan.Complete} · {plan.Steps.Count} bước · {plan.Computes} lần tính");
        var sim = HandoverWhyTest.CloneInput(input);
        foreach (var st in plan.Steps)
        {
            var bs = HandoverLedgerService.Compute(sim); var s2 = HandoverWhyTest.CloneInput(sim);
            HandoverWhy.StepOpOf(st)(s2);   // [H10-KET-WHY-3] + WTSV
            Console.WriteLine($"  {st.Kind}#{st.Id} · {st.Text}");
            sim = s2;
            var s3 = HandoverWhyTest.CloneInput(sim); HandoverWhy.OpVoidReceive(rid)(s3);
            var bad = HandoverLedgerService.NewNegativeKeys(HandoverLedgerService.Compute(sim), HandoverLedgerService.Compute(s3));
            if (HandoverLedgerService.RepairedReturnTimes(s3).Count > 0)
                bad.AddRange(HandoverLedgerService.NewLoopKeys(sim, HandoverLedgerService.Compute(sim), s3, HandoverLedgerService.LoopTimeline(sim)).Select(x => x.Key));
            Console.WriteLine("      sau bước này, huỷ lần nhận gốc còn chặn ở: " + (bad.Count == 0 ? "(hết)" : string.Join(", ", bad)));
        }
        return plan.Complete ? 0 : 1;
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
}
