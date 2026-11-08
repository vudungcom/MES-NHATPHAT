using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MES.Web.Data;

namespace MES.Web.Services
{
    // =====================================================================
    // [GN-WHY 2026-10-26] Báo gốc rễ khi một thao tác bị chặn (K5): đang vướng ở chứng từ nào, gỡ theo thứ tự nào.
    //
    // Cách tìm (chỉ ĐỌC và tính thử trên bản sao số liệu trong bộ nhớ — không ghi gì vào DB, không đổi cách tính in/out):
    //   1. Thử thao tác trên bản sao → ô nào mới âm (cùng hàm NegativeCells / K5 như lúc lưu).
    //   2. Ô âm đó đang bị chứng từ nào trừ vào (WTS của NC / mã CV đó, phiếu giao đi từ ô đó…) → lấy chứng từ MỚI NHẤT.
    //   3. Muốn bỏ chứng từ đó thì chính nó có bị vướng không → lần tiếp (đệ quy), để mỗi bước gợi ý đều tự lưu được.
    //   4. Lặp tới khi thao tác gốc hợp lệ, hoặc hết trần (số lần tính / thời gian) → báo "chưa lần ra hết".
    // Chỉ chạy khi đã bị chặn, sau khi nhả khoá PO. Thao tác bình thường không chạy phần này.
    // =====================================================================
    public static class HandoverWhy
    {
        /// <summary>Một bước gợi ý kiểm tra. Kind: WTS | NHAN | PHIEU | BAOLOI ([GN-NG-AFTER]).</summary>
        public class WhyStep
        {
            public string    Kind  { get; set; } = "";
            public long      Id    { get; set; }
            public long      TxId  { get; set; }
            public DateTime? At    { get; set; }
            public string    Text  { get; set; } = "";
        }

        public class WhyPlan
        {
            public List<WhyStep> Steps { get; } = new();
            public bool Complete { get; set; }
            public int  Computes { get; set; }
        }

        // ── Thao tác (áp lên bản sao LedgerInput) ──────────────────────────
        static string StatusOf(InTx t)
        {
            if (HandoverPurpose.IsOut(t.Purpose)) return t.Status;
            var rec = t.Receives.Sum(r => r.Ok + r.Ng);
            return rec <= 0 ? "PENDING" : rec >= t.Issued ? "COMPLETED" : "PARTIAL";
        }

        static void RemoveReceive(LedgerInput s, long rid)
        {
            var t = s.Txs.FirstOrDefault(x => x.Receives.Any(r => r.Id == rid));
            if (t != null) { t.Receives.RemoveAll(r => r.Id == rid); t.Status = StatusOf(t); }
            s.Txs.RemoveAll(x => x.SourceReceiveId == rid);   // phiếu trả tự lập huỷ theo (như VoidReceiveAsync)
        }

        /// <summary>Huỷ 1 lần nhận (+ phiếu trả tự lập của nó).</summary>
        public static Action<LedgerInput> OpVoidReceive(long receiveId) => s => RemoveReceive(s, receiveId);

        /// <summary>Huỷ phiếu: bỏ mọi lần nhận (+ phiếu trả tự lập) rồi bỏ phiếu.</summary>
        public static Action<LedgerInput> OpVoidIssue(long txId) => s =>
        {
            var t = s.Txs.FirstOrDefault(x => x.Id == txId);
            if (t == null) return;
            foreach (var r in t.Receives.Select(r => r.Id).ToList()) RemoveReceive(s, r);
            s.Txs.RemoveAll(x => x.Id == txId);
        };

        /// <summary>Huỷ phần chưa nhận: phiếu còn đúng số đã nhận.</summary>
        public static Action<LedgerInput> OpCloseRemainder(long txId) => s =>
        {
            var t = s.Txs.FirstOrDefault(x => x.Id == txId);
            if (t == null) return;
            var rec = t.Receives.Sum(r => r.Ok + r.Ng);
            var q = t.Issued - rec;
            t.Closes = HandoverLedgerService.ClosesOf(t);   // [K5-CLOSE-NHIEU 2026-11-06] giữ các lần huỷ cũ đúng giờ của chúng, thêm 1 lần mới lúc này (như máy chủ ghi 1 dòng HandoverTxCloses)
            if (q > 0) t.Closes.Add(new InClose(0, DateTime.Now, q));
            t.Closed += q; t.Issued = rec; t.Status = "COMPLETED";
            // [K5-CLOSE-NHIEU 2026-11-05] như máy chủ (HandoverService.CloseRemainderOnceAsync ghi ClosedAt = giờ lần huỷ mới nhất cho CẢ số đã huỷ):
            //   trước giữ ClosedAt cũ → huỷ phần chưa nhận lần 2 trên phiếu đã huỷ 1 lần, PO có hàng sửa trả về: máy chủ chặn theo thời gian
            //   mà lời khuyên rỗng (fuzz seed 602 PO5, 05/11). Nguồn: CLAUDE (lỗi có từ [K5-VONG-SUA], không thuộc gói A2–A4).
            t.ClosedAt = DateTime.Now;
            // [K5-CLOSE-NHIEU 2026-11-06] nay máy chủ ghi từng lần (HandoverTxCloses) — sổ chỉ mở lại lần huỷ mới (giờ ClosedAt cột cũ chỉ còn để hiện / dữ liệu cũ)
        };

        /// <summary>Xoá / sửa dòng WTS: bỏ các dòng removeIds, thêm các dòng mới (Id âm).</summary>
        public static Action<LedgerInput> OpWts(IReadOnlyCollection<long> removeIds, IEnumerable<InWts>? add = null) => s =>
        {
            s.Wts.RemoveAll(w => removeIds.Contains(w.Id));
            if (add != null) s.Wts.AddRange(add);
        };

        /// <summary>[KH-GUARD] Kho sửa SL phôi thực nhận của 1 dòng Kho (khoVatLieuId = 0: dòng Kho mới chưa có).
        /// Gán danh sách mới (không sửa list cũ — bản sao Clone dùng chung list Phoi).</summary>
        public static Action<LedgerInput> OpSetPhoi(long khoVatLieuId, decimal qty) => s =>
        {
            var list = s.Phoi.Where(p => p.Id != khoVatLieuId || khoVatLieuId == 0).ToList();
            list.Add(new InPhoi(khoVatLieuId == 0 ? -1 : khoVatLieuId, DateTime.Now, qty, null));
            s.Phoi = list;
        };

        /// <summary>[GN-NG-AFTER] Huỷ 1 phiếu báo hàng lỗi phát hiện sau khi nhận.</summary>
        public static Action<LedgerInput> OpVoidNgRep(long id) => s =>
        {
            s.NgReps = s.NgReps.Where(b => b.Id != id).ToList();
        };
        /// <summary>[GC-SUA-TAI-CHO] Huỷ 1 Kết quả sửa (cùng mọi lần KCS xác nhận của nó — service chỉ cho huỷ khi đã huỷ hết xác nhận).</summary>
        public static Action<LedgerInput> OpVoidRepair(long id) => s =>
        {
            s.Repairs = s.Repairs.Where(r => r.Id != id).ToList();
        };
        /// <summary>[GC-SUA-TAI-CHO] Huỷ 1 lần KCS xác nhận hàng sửa.</summary>
        public static Action<LedgerInput> OpVoidRepairCheck(long checkId) => s =>
        {
            s.Repairs = s.Repairs.Select(r =>
            {
                if (!r.Checks.Any(c => c.Id == checkId)) return r;
                var c2 = r.CloneRep(); c2.Checks.RemoveAll(c => c.Id == checkId); return c2;
            }).ToList();
        };
        /// <summary>[GN-NG-AFTER] Thêm 1 phiếu báo lỗi (Id âm) — dùng khi lưu báo lỗi bị chặn.</summary>
        public static Action<LedgerInput> OpAddNgRep(InNgRep b) => s =>
        {
            s.NgReps = s.NgReps.Concat(new[] { b }).ToList();
        };

        // ── Bản sao ────────────────────────────────────────────────────────
        static LedgerInput Clone(LedgerInput i) => new()
        {
            KhPlanDetailId = i.KhPlanDetailId, PlanNo = i.PlanNo, PurchaseOrder = i.PurchaseOrder, PartNo = i.PartNo,
            CustomerName = i.CustomerName, PlanQty = i.PlanQty, ApplyWorkflow = i.ApplyWorkflow,
            Phoi = i.Phoi, GcSteps = i.GcSteps, CodeSteps = i.CodeSteps,
            Txs = i.Txs.Select(t => t.CloneTx()).ToList(),
            Wts = new List<InWts>(i.Wts),
            NgReps = new List<InNgRep>(i.NgReps),   // [GN-NG-AFTER]
            Repairs = i.Repairs.Select(r => r.CloneRep()).ToList(),   // [GC-SUA-TAI-CHO]
            CaseInfo = i.CaseInfo,                  // [VULOI] nhãn vụ lỗi (không góp số)
            TaroGops = i.TaroGops,                  // [TARO-3T-GOP 2026-11-06] rà độc lập 06/11 mục 2: thiếu → tìm đường gỡ tính trên sổ KHÔNG có bộ gộp (ô âm sẵn, gợi ý rỗng)
        };

        static string GroupOfPg(string pg) => pg switch
        {
            "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", "PKG" => "PKG", _ => ""
        };

        // ── Tìm đường gỡ ───────────────────────────────────────────────────
        /// <summary>
        /// Tìm các chứng từ cần kiểm / gỡ trước để thao tác <paramref name="op"/> lưu được (K5).
        /// opDocs: khoá chứng từ chính thao tác này đụng tới ("W:id", "R:id", "T:id") — không gợi ý chính nó.
        /// </summary>
        // [WHY-TRAN 2026-10-31] Trần tìm 80 lần tính / 1,5 s → 400 lần / 5 s, mỗi tầng tối đa 150 bước (trước 40).
        //   Trước: huỷ lần nhận rất sớm của PO nhiều chứng từ cần 27–36 bước gỡ (~3 lần tính sổ / bước, PO lớn ~17 ms / lần tính)
        //   → hết trần giữa chừng, lời báo chỉ có phần đầu + "Phần mềm dừng tìm ở đây". Ca thường vẫn vài chục ms (chỉ tính khi bị
        //   chặn, sau khi đã nhả khoá PO, chỉ đọc). Lời báo vẫn hiện tối đa 8 bước + "… và N chứng từ nữa".
        // [K5-VONG-SUA 2026-11-05] loopOp = thao tác gốc là huỷ chứng từ nguồn (huỷ lần nhận / phiếu / phần chưa nhận / KCS xác nhận / sửa phôi Kho)
        //   → xét thêm theo thời gian như máy chủ; bước gỡ cũng chỉ xét khi là huỷ lần nhận / phiếu / KCS xác nhận (rà độc lập V3: khớp máy chủ).
        //   [K5-VONG-HO-WTS 2026-11-05] + xoá dòng WTS (thao tác gốc và bước gỡ).
        public static WhyPlan Plan(LedgerInput current, Action<LedgerInput> op, ISet<string> opDocs, int maxComputes = 400, int maxMs = 5000, bool loopOp = false)
        {
            var plan = new WhyPlan();
            if (!current.ApplyWorkflow) { plan.Complete = true; return plan; }   // N7: Workflow OFF không chặn
            var sw = Stopwatch.StartNew();
            var S = Clone(current);
            LedgerRow? sRow = null;   // sổ của S (tính lại khi S đổi)
            bool Over() => plan.Computes >= maxComputes || sw.ElapsedMilliseconds > maxMs;
            LedgerRow Calc(LedgerInput x) { plan.Computes++; return HandoverLedgerService.Compute(x); }
            LedgerRow SRow() => sRow ??= Calc(S);
            Dictionary<string, (decimal Value, string Text)>? sTl = null;   // [K5-VONG-SUA] LoopTimeline(S) — tính lại khi S đổi (rà độc lập V4)
            List<string> Bad(Action<LedgerInput> o, bool loop)
            {
                var a = Clone(S); o(a);
                var bad = HandoverLedgerService.NewNegativeKeys(SRow(), Calc(a));
                if (loop && HandoverLedgerService.RepairedReturnTimes(a).Count > 0)
                    bad.AddRange(HandoverLedgerService.NewLoopKeys(S, SRow(), a, sTl ??= HandoverLedgerService.LoopTimeline(S)).Select(x => x.Key));
                return bad;
            }

            // [H10-KET-WHY 2026-11-07] Thử lùi: chứng từ ứng viên mới nhất mà gỡ KHÔNG được (bước con của nó thất bại) thì trả sổ tạm về như trước
            //   bước đó rồi thử ứng viên kế (trước: bỏ cuộc cả lượt → "chưa lần ra đường gỡ" dù có đường). Ca WHYALL seed 44 PO 54 (HEAD 1ee303c / 0594d93
            //   cũng gặp): huỷ lần nhận hàng sửa Kho trả về Gia công — ứng viên mới nhất là lần Kho nhận phiếu trả NG (gỡ không được vì cần huỷ chính
            //   lần nhận gốc), đường đúng là xoá WTS NC 3 → NC 2 → NC 1 không được thử. Nguồn: CLAUDE (sổ dòng H10-KET-WHY); Hùng 07/11 07:55 giao Gói 5.
            int luiTong = 0;   // [H10-KET-WHY] tổng số lần lùi cả lượt tìm
            bool Clear(Action<LedgerInput> o, HashSet<string> skip, int depth, bool loop)
            {
                var tried = new HashSet<string>();   // [H10-KET-WHY] ứng viên đã thử ở lượt này mà gỡ không được (không thử lại trong lượt này)
                int lui = 0;   // [H10-KET-WHY] số lần lùi ở lượt này — tối đa LuiMax (rà độc lập gói 5 mục 2: không có đường thì trước bỏ cuộc ngay, nay không được chạy tới trần 5 s)
                for (int guard = 0; guard < 150; guard++)   // [WHY-TRAN] trước 40
                {
                    if (Over()) return false;
                    var bad = Bad(o, loop);
                    if (bad.Count == 0) return true;
                    var d = NextCandidate(S, SRow(), bad, tried.Count == 0 ? skip : new HashSet<string>(skip.Concat(tried)));   // [H10-KET-WHY]
                    if (d == null) return false;
                    var removeD = d.Kind switch
                    {
                        "WTS"    => OpWts(new[] { d.Id }),
                        "NHAN"   => OpVoidReceive(d.Id),
                        "BAOLOI" => OpVoidNgRep(d.Id),   // [GN-NG-AFTER]
                        "KQSUA"  => OpVoidRepair(d.Id),        // [GC-SUA-TAI-CHO]
                        "XNSUA"  => OpVoidRepairCheck(d.Id),   // [GC-SUA-TAI-CHO]
                        _        => OpVoidIssue(d.Id),
                    };
                    var sub = new HashSet<string>(skip) { Key(d) };
                    // [K5-VONG-HO-WTS 2026-11-05] bước gỡ "xoá dòng WTS" cũng xét theo thời gian như máy chủ (WtsSaveService.VoidRowOnceAsync)
                    if (depth >= 8) return false;
                    var keepS = S; var keepRow = sRow; var keepTl = sTl; var keepN = plan.Steps.Count;   // [H10-KET-WHY] chụp để lùi
                    S = Clone(S);   // bước con sửa trên bản sao — thất bại thì lấy lại keepS nguyên vẹn
                    if (!Clear(removeD, sub, depth + 1, d.Kind is "NHAN" or "PHIEU" or "XNSUA" or "WTS"))
                    {
                        // lùi về trước bước này (cả khi hết trần: phần gợi ý dở chỉ giữ các bước đã chắc — rà độc lập gói 5 mục 4)
                        S = keepS; sRow = keepRow; sTl = keepTl;
                        plan.Steps.RemoveRange(keepN, plan.Steps.Count - keepN);
                        if (Over() || ++lui > LuiMax || ++luiTong > LuiTongMax) return false;   // + trần lùi cả lượt tìm (rà độc lập gói 5 vòng 2 mục 3)
                        tried.Add(Key(d));
                        continue;
                    }
                    removeD(S); sRow = null; sTl = null;
                    plan.Steps.Add(d);
                }
                return false;
            }

            plan.Complete = Clear(op, new HashSet<string>(opDocs), 0, loopOp);

            // [H10-KET-WHY-2 2026-11-07] Lượt tìm trên không ra đường → BÓC DẦN.
            //   Nguồn: CLAUDE (sổ dòng H10-KET-WHY-2, whyreplay); Hùng 07/11 22:38 giao Gói 8.
            //   Gom tập chứng từ liên quan (mọi chứng từ đang trừ vào ô sắp âm
            //   của thao tác, và đệ quy của từng chứng từ đó khi gỡ trọn), rồi mỗi lượt gỡ chứng từ MỚI NHẤT trong tập mà gỡ ngay được
            //   (không tạo ô âm mới, kể cả theo thời gian hàng sửa), tới khi thao tác gốc lưu được. Lượt trên đi sâu từng nhánh rồi chốt nên có thể
            //   gỡ mất chỗ trống mà nhánh sau cần (ca fuzz WHYALL chuoi_lamho seed 741 PO 34, why_ca/y5_s741_po34_r5.json: nhánh WTS #14 xoá WTS #11
            //   NC 2 trước → NC 3 hết chỗ để huỷ lần GC nhận #10 → vòng WTS #13 ↔ lần nhận #8 ↔ lần nhận #10 → "chưa lần ra" dù có đường).
            //   Lượt trên ra đường thì kết quả y như trước; bóc cũng không ra thì giữ gợi ý dở của lượt trên. Bước thừa bỏ ở đoạn "bỏ bước thừa" dưới.
            if (!plan.Complete && !Over())
            {
                var firstTry = plan.Steps.ToList();
                plan.Steps.Clear(); S = Clone(current); sRow = null; sTl = null;
                if (Peel()) plan.Complete = true;
                else { plan.Steps.Clear(); plan.Steps.AddRange(firstTry); }
            }
            bool Peel()
            {
                bool Busy(string u) => u[0] switch   // đơn vị trùng chứng từ của chính thao tác → không gỡ
                {
                    // phiếu có lần nhận của chính thao tác vẫn vào tập (rà độc lập Gói 8 Trung-2): bước gỡ của nó chỉ là lần nhận KHÁC (lọc khoá bước bên dưới)
                    'T' => opDocs.Contains(u),
                    'A' => opDocs.Contains("T:" + u[2..]),
                    'W' => opDocs.Contains(u), 'B' => opDocs.Contains(u), _ => opDocs.Contains(u),
                };
                int peelStart = plan.Computes;   // [H10-KET-WHY-2] trần riêng của bóc (rà độc lập Gói 8 Trung-3: ca không có đường không chạy tới trần chung)
                bool POver() => Over() || plan.Computes - peelStart >= PeelMaxComputes;
                var row0 = SRow();
                var tl0 = HandoverLedgerService.LoopTimeline(S);
                List<string> UnitBad(string u)
                {
                    var a = Clone(S); UnitOp(u)(a);
                    var bad = HandoverLedgerService.NewNegativeKeys(row0, Calc(a));
                    if (u[0] != 'B' && HandoverLedgerService.RepairedReturnTimes(a).Count > 0)
                        bad.AddRange(HandoverLedgerService.NewLoopKeys(S, row0, a, tl0).Select(x => x.Key));
                    return bad;
                }
                var units = new List<string>();
                var seen = new HashSet<string>();
                var queue = new Queue<List<string>>();
                queue.Enqueue(Bad(op, loopOp));
                while (queue.Count > 0 && units.Count < PeelMaxUnits)
                {
                    if (POver()) return false;
                    var found = new HashSet<string>();
                    Candidates(S, row0, queue.Dequeue(), found);
                    foreach (var u in found)
                    {
                        if (!seen.Add(u) || Busy(u)) continue;
                        units.Add(u);
                        if (units.Count >= PeelMaxUnits || POver()) break;
                        var ub = UnitBad(u);
                        if (ub.Count > 0) queue.Enqueue(ub);
                    }
                }
                for (int round = 0; round < 4 * PeelMaxUnits; round++)
                {
                    if (POver()) return false;
                    if (Bad(op, loopOp).Count == 0) return true;
                    WhyStep? pick = null;
                    foreach (var a in units.Select(u => AtomOf(S, u)).Where(a => a != null && !opDocs.Contains(Key(a!)))
                                           .GroupBy(a => Key(a!)).Select(g => g.First()!)
                                           .OrderByDescending(a => a.At ?? DateTime.MinValue).ThenByDescending(a => a.Id))
                    {
                        if (POver()) return false;
                        if (Bad(StepOp(a), a.Kind is "NHAN" or "PHIEU" or "XNSUA" or "WTS").Count == 0) { pick = a; break; }
                    }
                    if (pick == null) return false;
                    StepOp(pick)(S); sRow = null; sTl = null;
                    plan.Steps.Add(pick);
                }
                return false;
            }
            // [H10-KET-WHY 2026-11-07] Bỏ bước thừa: thử lùi có thể để lại bước đã làm mà không giúp gỡ (vd huỷ lần Kho nhận phiếu trả NG trước khi
            //   tìm ra đường đúng) → người dùng huỷ chứng từ đúng một cách vô ích. Sau khi có đường đủ, thử bỏ từng bước (từ đầu): làm theo các bước
            //   còn lại vẫn không bị K5 chặn và thao tác gốc lưu được thì bỏ hẳn. Hết trần tính thì giữ đường hiện có (vẫn đúng, chỉ chưa gọn).
            //   Nguồn: CLAUDE (whyreplay seed 44 PO 54 — đường 4 bước có 1 bước thừa); Hùng 07/11 07:55 giao Gói 5.
            //   Giới hạn (rà độc lập gói 5 mục 1): chỉ khi đường ≤ GonMaxSteps bước, tối đa GonMaxComputes lần tính riêng cho phần này; trạng thái sau
            //   từng bước đầu được nhớ (states[i]) nên thử bỏ bước i chỉ tính lại từ bước i.
            if (plan.Complete && plan.Steps.Count > 1 && plan.Steps.Count <= GonMaxSteps)
            {
                int gonStart = plan.Computes;
                bool GonOver() => Over() || plan.Computes - gonStart >= GonMaxComputes;
                bool Works(LedgerInput start, IEnumerable<WhyStep> steps)
                {
                    var sim = start;
                    foreach (var st in steps)
                    {
                        if (GonOver()) return false;
                        // làm được thật như trên trang: huỷ phiếu chỉ khi phiếu không còn lần nhận; huỷ Kết quả sửa chỉ khi không còn lần KCS xác nhận
                        if (st.Kind is not ("WTS" or "NHAN" or "BAOLOI" or "KQSUA" or "XNSUA") && sim.Txs.Any(t => t.Id == st.Id && t.Receives.Any())) return false;
                        if (st.Kind == "KQSUA" && sim.Repairs.Any(r => r.Id == st.Id && r.Checks.Any(c => c.Id > 0))) return false;
                        // huỷ lần nhận có phiếu trả tự lập đã được nhận lại: tìm đường luôn gợi ý huỷ lần nhận phiếu trả trước (Actionable) → không bỏ bước đó
                        //   (bỏ thì máy chủ chỉ cho làm theo dây chuyền H10, cần quyền cả 2 nhóm, lời gợi ý không nói) — rà độc lập gói 5 vòng 2 mục 1
                        if (st.Kind == "NHAN" && sim.Txs.Any(b => b.SourceReceiveId == st.Id && b.Receives.Any())) return false;
                        var o = StepOp(st);
                        var a = Clone(sim); o(a);
                        var r0 = Calc(sim);
                        var bad = HandoverLedgerService.NewNegativeKeys(r0, Calc(a));
                        if (bad.Count == 0 && st.Kind is "NHAN" or "PHIEU" or "XNSUA" or "WTS" && HandoverLedgerService.RepairedReturnTimes(a).Count > 0)
                            bad.AddRange(HandoverLedgerService.NewLoopKeys(sim, r0, a, HandoverLedgerService.LoopTimeline(sim)).Select(x => x.Key));
                        if (bad.Count > 0) return false;
                        sim = a;
                    }
                    if (GonOver()) return false;
                    var f = Clone(sim); op(f);
                    var rf = Calc(sim);
                    var badF = HandoverLedgerService.NewNegativeKeys(rf, Calc(f));
                    if (badF.Count == 0 && loopOp && HandoverLedgerService.RepairedReturnTimes(f).Count > 0)
                        badF.AddRange(HandoverLedgerService.NewLoopKeys(sim, rf, f, HandoverLedgerService.LoopTimeline(sim)).Select(x => x.Key));
                    return badF.Count == 0;
                }
                for (bool changed = true; changed && !GonOver(); )   // lặp tới khi không bỏ được nữa (bỏ 1 bước có thể làm bước trước nó thành thừa)
                {
                    changed = false;
                    // states[i] = sổ tạm sau các bước 0..i-1 (đường hiện tại làm được trọn nên các trạng thái này hợp lệ)
                    var states = new List<LedgerInput> { Clone(current) };
                    foreach (var st in plan.Steps) { var nx = Clone(states[^1]); StepOp(st)(nx); states.Add(nx); }
                    for (int i = 0; i < plan.Steps.Count && plan.Steps.Count > 1 && !GonOver(); i++)
                    {
                        if (Works(Clone(states[i]), plan.Steps.Skip(i + 1)))
                        {
                            plan.Steps.RemoveAt(i); changed = true;
                            break;   // đường đổi → tính lại states rồi thử tiếp
                        }
                    }
                }
            }
            return plan;
        }

        // [H10-KET-WHY 2026-11-07] Trần riêng của thử lùi / bỏ bước thừa (rà độc lập gói 5 mục 1–2). Nguồn: CLAUDE.
        const int LuiMax = 4;            // mỗi tầng lùi tối đa 4 lần rồi thôi
        const int LuiTongMax = 12;       // cả lượt tìm lùi tối đa 12 lần
        const int GonMaxSteps = 16;      // đường dài hơn thì giữ nguyên (vẫn đúng, chỉ chưa gọn)
        const int GonMaxComputes = 150;  // lần tính dành riêng cho phần bỏ bước thừa
        const int PeelMaxUnits = 40;     // [H10-KET-WHY-2] tập chứng từ liên quan tối đa 40 đơn vị
        const int PeelMaxComputes = 200; // [H10-KET-WHY-2] lần tính dành riêng cho bóc (để lại phần cho bỏ bước thừa)

        /// <summary>[H10-KET-WHY] Thao tác gỡ của 1 bước gợi ý (dùng chung lúc tìm đường + bỏ bước thừa).</summary>
        static Action<LedgerInput> StepOp(WhyStep d) => d.Kind switch
        {
            "WTS"    => OpWts(new[] { d.Id }),
            "NHAN"   => OpVoidReceive(d.Id),
            "BAOLOI" => OpVoidNgRep(d.Id),
            "KQSUA"  => OpVoidRepair(d.Id),
            "XNSUA"  => OpVoidRepairCheck(d.Id),
            _        => OpVoidIssue(d.Id),
        };

        static string WtsWhere(InWts w) => w.ProcessGroup switch
        {
            "GC"      => "Gia công " + HandoverLedgerService.NcName(w.NC),
            "TARO"    => "HTSP Taro " + w.WtsCode + (string.IsNullOrWhiteSpace(w.StepNC) ? "" : " · NC " + w.StepNC.Trim()),   // [TARO-NC-MA] NC Taro của dòng
            "BAVIA"   => "HTSP Bavia " + w.WtsCode,
            "WASHING" => "HTSP Rửa " + w.WtsCode,
            "KCS"     => "KCS " + w.WtsCode,
            "PKG"     => "Đóng gói " + w.WtsCode,
            _         => w.ProcessGroup + " " + (w.WtsCode ?? w.NC)
        };

        static string Key(WhyStep d) => (d.Kind == "WTS" ? "W:" : d.Kind == "NHAN" ? "R:" : d.Kind == "BAOLOI" ? "B:"
                                         : d.Kind == "KQSUA" ? "K:" : d.Kind == "XNSUA" ? "X:" : "T:") + d.Id;   // [GN-NG-AFTER] + B:; [GC-SUA-TAI-CHO] + K: / X:

        /// <summary>Chứng từ mới nhất đang trừ vào một trong các ô sắp âm (đã quy về việc làm được ngay: phiếu có lần nhận → lần nhận mới nhất trước).</summary>
        static WhyStep? NextCandidate(LedgerInput s, LedgerRow row, List<string> badKeys, HashSet<string> skip)
            => Candidates(s, row, badKeys, null).Where(x => !skip.Contains(Key(x)))
                    .GroupBy(Key).Select(x => x.First())
                    .OrderByDescending(x => x.At ?? DateTime.MinValue).ThenByDescending(x => x.Id)
                    .FirstOrDefault();

        /// <summary>[H10-KET-WHY-2] Mọi chứng từ đang trừ vào các ô sắp âm (chưa lọc / chưa xếp). units != null: ghi thêm "đơn vị chứng từ" của từng
        ///   ứng viên — T:&lt;phiếu&gt; (huỷ cả phiếu), A:&lt;phiếu trả tự lập&gt; (huỷ các lần nhận của nó), W / B / K (dòng WTS / báo lỗi / Kết quả sửa).</summary>
        static List<WhyStep> Candidates(LedgerInput s, LedgerRow row, List<string> badKeys, HashSet<string>? units)
        {
            var c = new List<WhyStep>();
            string Main(string pg, string? code)
                => code == null ? "" : row.CodeMain.TryGetValue(pg + "|" + code.Trim(), out var m) ? m : code.Trim();
            string? WtsKeyOf(InWts w)   // [TARO-NC-MA] như TaroKeyed: mã = WtsCode ?? NC (dòng rất cũ ghi mã ở cột NC — rà độc lập vòng 2 mục 1); NC sai / thiếu → NC đầu
            {
                var wc = w.WtsCode ?? w.NC;
                return w.ProcessGroup == "TARO" && HandoverLedgerService.TaroNcsOf(row, "TARO", wc) is { } ncs
                    ? HandoverLedgerService.WtsKey(row, "TARO", wc, w.StepNC) ?? HandoverLedgerService.TaroKey(wc!, ncs[0])
                    : wc;
            }
            void AddWts(Func<InWts, bool> f)
            {
                foreach (var w in s.Wts.Where(w => w.Id > 0 && f(w)))
                { c.Add(StepWts(w)); units?.Add("W:" + w.Id); }   // [H10-KET-WHY-2] chữ bước tách ra StepWts (dùng chung lúc bóc)
            }
            void AddRep(Func<InNgRep, bool> f)   // [GN-NG-AFTER]
            {
                foreach (var b in s.NgReps.Where(b => b.Id > 0 && f(b)))
                { c.Add(StepRep(b)); units?.Add("B:" + b.Id); }
            }
            // [GC-SUA-TAI-CHO] Kết quả sửa đang lấy hàng từ ô Hàng sửa / Hàng lỗi: có lần KCS xác nhận → huỷ lần mới nhất trước
            void AddRepair(Func<InRepair, bool> f)
            {
                foreach (var r in s.Repairs.Where(r => r.Id > 0 && f(r)))
                { c.Add(StepRepair(r)); units?.Add("K:" + r.Id); }
            }
            void AddTx(Func<InTx, bool> f)
            {
                foreach (var t in s.Txs.Where(f))
                {
                    var a = Actionable(s, t);
                    if (a != null) { c.Add(a); units?.Add((t.SourceReceiveId == null ? "T:" : "A:") + t.Id); }
                }
            }
            foreach (var k0 in badKeys)
            {
                // [K5-VONG-SUA 2026-11-05] Ô "TL|<ô>" = ô chưa làm âm trong ảnh sổ ngay trước lúc hàng sửa trả về: chứng từ trừ vào ô đó (như ô thường)
                //   + chính lần hàng sửa trả về của nhóm đó (lần nhận hàng sửa / lần KCS xác nhận đạt) — gỡ cái mới nhất trước.
                var k = k0.StartsWith("TL|", StringComparison.Ordinal) ? k0[3..] : k0;
                var p = k.Split('|');
                var g = p[0];
                if (!ReferenceEquals(k, k0))
                {
                    AddTx(t => t.To == g && t.Receives.Any(r => HandoverLedgerService.IsRepairedReturn(t, r)));
                    foreach (var r in s.Repairs.Where(r => r.Id > 0 && r.Group == g))   // (rà độc lập T2) chỉ lần KCS xác nhận có đạt — lần chỉ loại không phải hàng sửa về
                    {
                        var c0 = r.Checks.Where(x => x.Id > 0 && x.Ok > 0).OrderByDescending(x => x.At).ThenByDescending(x => x.Id).FirstOrDefault();
                        if (c0 != null)
                        {
                            c.Add(new WhyStep { Kind = "XNSUA", Id = c0.Id, TxId = r.Id, At = c0.At,
                                Text = $"Lần KCS xác nhận #{c0.Id} của Kết quả sửa #{r.Id} · {HandoverLedgerService.GroupLabel(r.Group)} · đạt {c0.Ok:0.##}"
                                     + (c0.Ng > 0 ? $" loại {c0.Ng:0.##}" : "") + (string.IsNullOrEmpty(c0.Who) ? "" : $" · {c0.Who}") + $" · {c0.At:dd/MM HH:mm}" });
                            units?.Add("K:" + r.Id);   // [H10-KET-WHY-2]
                        }
                    }
                    if (g == "KHO" && p.Length == 2 && p[1] == "ConGiao")   // Kho: phiếu giao từ ô Hàng mới / báo lỗi lấy phôi trừ vào phôi còn giao
                    {
                        AddTx(t => t.From == "KHO" && !HandoverPurpose.IsOut(t.Purpose) && HandoverCell.IssueCellOf(HandoverLedgerService.Classify(t, null), t.FromCell) == HandoverCell.Moi);
                        AddRep(b => b.Group == "KHO");
                        continue;
                    }
                }
                // [VULOI 2026-10-28] Ô vụ lỗi âm ("VU|<id>|<nhóm>|<ô>"): phiếu đang lấy hàng của vụ đó từ nhóm đó (mới nhất gỡ trước)
                if (g == "VU")
                {
                    if (p.Length == 4 && long.TryParse(p[1], out var vuId))
                    {
                        long? want = vuId == 0 ? null : vuId;
                        var vg = p[2]; var vcell = p[3];
                        AddTx(t => t.From == vg && t.NgCaseId == want && t.SourceReceiveId == null
                                   && (HandoverPurpose.IsOut(t.Purpose)
                                       || (vcell == HandoverCell.LamHo && HandoverCell.IsLamHoLoi(t.Purpose, t.FromCell))   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ lấy từ ô vụ LAMHO
                                       || (t.Purpose != HandoverPurpose.NgReturn && !HandoverCell.IsLamHoLoi(t.Purpose, t.FromCell) && HandoverCell.IssueCellOf(HandoverLedgerService.Classify(t, null), t.FromCell) == vcell)));   // rà gói 6 mục 13
                        AddRepair(r => r.Group == vg && r.NgCaseId == want && r.Cell == vcell);   // [GC-SUA-TAI-CHO]
                    }
                    continue;
                }
                if (p.Length == 2)
                {
                    switch (p[1])
                    {
                        case "ChuaLam":
                            AddWts(w => GroupOfPg(w.ProcessGroup) == g);
                            AddTx(t => t.From == g && (t.Purpose == HandoverPurpose.NgReturn || t.Purpose == HandoverPurpose.HelpSend));
                            AddRep(b => b.Group == g && b.Source == HandoverNgSource.Chua);   // [GN-NG-AFTER]
                            break;
                        case "ConGiao":
                            AddTx(t => t.From == g && !HandoverPurpose.IsOut(t.Purpose) && HandoverCell.IssueCellOf(HandoverLedgerService.Classify(t, null), t.FromCell) == HandoverCell.Moi);
                            AddRep(b => b.Group == g && b.Source != HandoverNgSource.Chua);   // [GN-NG-AFTER] Kho phôi / đã làm chưa giao
                            break;
                        case "LoiGiu":
                            AddTx(t => t.From == g && t.Purpose != HandoverPurpose.NgReturn && t.SourceReceiveId == null
                                       && (HandoverPurpose.IsOut(t.Purpose) || HandoverCell.IssueCellOf(HandoverLedgerService.Classify(t, null), t.FromCell) == HandoverCell.Loi));
                            AddRepair(r => r.Group == g && r.Cell == HandoverCell.Loi);   // [GC-SUA-TAI-CHO]
                            break;
                        case "SuaGiu":
                            AddTx(t => t.From == g && t.FromCell == HandoverCell.Sua);
                            AddRepair(r => r.Group == g && r.Cell == HandoverCell.Sua);   // [GC-SUA-TAI-CHO]
                            break;
                        case "LamHoGiu":
                            AddTx(t => t.From == g && t.Purpose == HandoverPurpose.HelpReturn);
                            break;
                        case "LamHoTot":   // [TARO-LAMHO-NC] hàng làm hộ còn tốt: phiếu trả về Gia công + WTS Taro làm hộ có NG (chiếc bị lỗi)
                        {
                            AddTx(t => t.From == g && t.Purpose == HandoverPurpose.HelpReturn);
                            var lh = row.G(g).Steps.Where(x => x.LamHoNc != null).Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            AddWts(w => w.Ng > 0 && w.ProcessGroup == "TARO" && lh.Contains(Main("TARO", WtsKeyOf(w))));
                            break;
                        }
                    }
                }
                else if (p.Length == 3)
                {
                    var pg = p[1]; var code = p[2];
                    if (pg == "LamHoRun")   // [TARO-LAMHO-NC] số dư đoạn âm: phiếu trả về NC sau đoạn + WTS Taro làm hộ có NG ở đoạn
                    {
                        var run = row.LamHoRuns.FirstOrDefault(x => x.Key == code);
                        if (run != null)
                        {
                            AddTx(t => t.From == "HTSP" && t.Purpose == HandoverPurpose.HelpReturn && !HandoverCell.IsLamHoLoi(t.Purpose, t.FromCell) && string.Equals(t.ToNC, run.NextNc, StringComparison.OrdinalIgnoreCase));
                            var lh = row.G("HTSP").Steps.Where(x => x.LamHoNc != null && run.Ncs.Contains(x.LamHoNc, StringComparer.OrdinalIgnoreCase)).Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            AddWts(w => w.Ng > 0 && w.ProcessGroup == "TARO" && lh.Contains(Main("TARO", WtsKeyOf(w))));
                        }
                    }
                    else                     if (pg == "LamHoTaro")   // [TARO-LAMHO-NC] câu 1: đã trả về NC sau đoạn > OK bộ Taro làm hộ → gỡ phiếu trả về (hàng tốt) vào NC đó trước
                    {
                        var run = row.LamHoRuns.FirstOrDefault(x => x.Key == code);
                        if (run != null)
                            AddTx(t => t.From == "HTSP" && t.Purpose == HandoverPurpose.HelpReturn && !HandoverCell.IsLamHoLoi(t.Purpose, t.FromCell)
                                       && string.Equals(t.ToNC, run.NextNc, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (pg == "helpOut")
                        AddTx(t => t.From == "GC" && t.Purpose == HandoverPurpose.HelpSend && t.FromNC == code);
                    else if (pg == "GC")
                    {
                        AddWts(w => w.ProcessGroup == "GC" && string.Equals(Main("GC", w.NC), code, StringComparison.OrdinalIgnoreCase));
                        AddTx(t => t.From == "GC" && t.FromNC == code && t.Purpose == HandoverPurpose.NgReturn);
                        AddRep(b => b.Group == "GC" && b.Source == HandoverNgSource.Chua && string.Equals(b.NC, code, StringComparison.OrdinalIgnoreCase));   // [GN-NG-AFTER]
                        // Giao theo NC từ NC trước cũng trừ vào "nhận vào" của NC này (N1: nhận vào = OK NC trước − đã giao đi)
                        AddTx(t => t.From == "GC" && t.Purpose == HandoverPurpose.HelpSend);
                    }
                    else
                    {
                        // [TARO-NC-MA 2026-11-06] mã Taro dùng ở nhiều NC: ô âm mang khoá "mã (NC x)" → so theo khoá của dòng WTS (mã + StepNC; dòng cũ không NC = NC đầu như sổ).
                        //   Trước (rà độc lập 06/11 mục 1): so mã thô "2T" với "2T (NC 3)" → không tìm ra dòng WTS nào, gợi ý "chưa lần ra". Nguồn: CLAUDE (rà độc lập gói 4 06/11).
                        AddWts(w => w.ProcessGroup == pg && string.Equals(Main(pg, WtsKeyOf(w)), code, StringComparison.OrdinalIgnoreCase));
                        AddTx(t => t.From == g && t.Purpose == HandoverPurpose.NgReturn);
                        AddRep(b => b.Group == g && b.Source == HandoverNgSource.Chua);   // [GN-NG-AFTER]
                    }
                }
            }
            return c;
        }

        // [H10-KET-WHY-2] Chữ từng loại bước (trước viết thẳng trong NextCandidate — giữ nguyên chữ).
        static WhyStep StepWts(InWts w) => new() { Kind = "WTS", Id = w.Id, At = w.At,
            Text = $"WTS #{w.Id} · {WtsWhere(w)} · OK {w.Ok:0.##}"
                 + (w.Ng > 0 ? $" NG {w.Ng:0.##}" : "") + (string.IsNullOrEmpty(w.Worker) ? "" : $" · {w.Worker}") + $" · {w.At:dd/MM HH:mm}" };
        static WhyStep StepRep(InNgRep b) => new() { Kind = "BAOLOI", Id = b.Id, At = b.At,
            Text = $"Báo lỗi #{b.Id} · {HandoverLedgerService.GroupLabel(b.Group)} · {b.Qty:0.##} chiếc · lấy từ {HandoverNgSource.SourceLabel(b.Source, b.Group)}"
                 + (string.IsNullOrEmpty(b.NC) ? "" : $" {HandoverLedgerService.NcText(b.NC)}") + (string.IsNullOrEmpty(b.Who) ? "" : $" · {b.Who}") + $" · {b.At:dd/MM HH:mm}" };
        static WhyStep StepRepair(InRepair r)
        {
            var c0 = r.Checks.Where(x => x.Id > 0).OrderByDescending(x => x.At).ThenByDescending(x => x.Id).FirstOrDefault();
            if (c0 != null)
                return new WhyStep { Kind = "XNSUA", Id = c0.Id, TxId = r.Id, At = c0.At,
                    Text = $"Lần KCS xác nhận #{c0.Id} của Kết quả sửa #{r.Id} · {HandoverLedgerService.GroupLabel(r.Group)} · đạt {c0.Ok:0.##}"
                         + (c0.Ng > 0 ? $" loại {c0.Ng:0.##}" : "") + (string.IsNullOrEmpty(c0.Who) ? "" : $" · {c0.Who}") + $" · {c0.At:dd/MM HH:mm}" };
            return new WhyStep { Kind = "KQSUA", Id = r.Id, TxId = r.Id, At = r.At,
                Text = $"Kết quả sửa #{r.Id} · {HandoverLedgerService.GroupLabel(r.Group)} · ô {HandoverCell.Label(r.Cell)} · đạt {r.Pass:0.##}"
                     + (r.Fail > 0 ? $" không đạt {r.Fail:0.##}" : "") + (string.IsNullOrEmpty(r.Who) ? "" : $" · {r.Who}") + $" · {r.At:dd/MM HH:mm}" };
        }

        /// <summary>[H10-KET-WHY-2] Bước làm được ngay của 1 đơn vị chứng từ (null = đơn vị đã gỡ hết).</summary>
        static WhyStep? AtomOf(LedgerInput s, string unit)
        {
            var id = long.Parse(unit[2..]);
            switch (unit[0])
            {
                case 'T': { var t = s.Txs.FirstOrDefault(x => x.Id == id); return t == null ? null : Actionable(s, t); }
                case 'A': { var t = s.Txs.FirstOrDefault(x => x.Id == id); return t == null || !t.Receives.Any() ? null : Actionable(s, t); }
                case 'W': { var w = s.Wts.FirstOrDefault(x => x.Id == id); return w == null ? null : StepWts(w); }
                case 'B': { var b = s.NgReps.FirstOrDefault(x => x.Id == id); return b == null ? null : StepRep(b); }
                case 'K': { var r = s.Repairs.FirstOrDefault(x => x.Id == id); return r == null ? null : StepRepair(r); }
            }
            return null;
        }

        /// <summary>[H10-KET-WHY-2] Gỡ trọn 1 đơn vị chứng từ (dùng để gom tập chứng từ liên quan).</summary>
        static Action<LedgerInput> UnitOp(string unit)
        {
            var id = long.Parse(unit[2..]);
            return unit[0] switch
            {
                'T' => OpVoidIssue(id),
                'A' => s => { var t = s.Txs.FirstOrDefault(x => x.Id == id); if (t != null) foreach (var r in t.Receives.Select(r => r.Id).ToList()) RemoveReceive(s, r); },
                'W' => OpWts(new[] { id }),
                'B' => OpVoidNgRep(id),
                _   => OpVoidRepair(id),
            };
        }

        /// <summary>Phiếu đã có lần nhận → việc làm được trước là huỷ lần nhận mới nhất (lần nhận có phiếu trả tự lập đã được nhận lại → huỷ lần nhận phiếu trả trước).</summary>
        static WhyStep? Actionable(LedgerInput s, InTx t)
        {
            if (t.SourceReceiveId != null && !t.Receives.Any()) return null;   // phiếu trả tự lập chưa nhận: huỷ theo lần nhận gốc
            var r = t.Receives.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).FirstOrDefault();
            if (r == null)
                return new WhyStep { Kind = "PHIEU", Id = t.Id, TxId = t.Id, At = t.At,
                    Text = $"Phiếu #{t.Id} · {HandoverLedgerService.GroupLabel(t.From)} → {HandoverLedgerService.GroupLabel(t.To)} · {t.Issued:0.##} chiếc · {t.At:dd/MM HH:mm}" };
            var back = s.Txs.FirstOrDefault(x => x.SourceReceiveId == r.Id && x.Receives.Any());
            if (back != null) return Actionable(s, back);
            return new WhyStep { Kind = "NHAN", Id = r.Id, TxId = t.Id, At = r.At,
                Text = $"Lần nhận #{r.Id} của phiếu #{t.Id} · {HandoverLedgerService.GroupLabel(t.From)} → {HandoverLedgerService.GroupLabel(t.To)} · nhận {r.Ok + r.Ng:0.##}"
                     + (r.Ng > 0 ? $" (NG {r.Ng:0.##})" : "") + $" · {r.At:dd/MM HH:mm}" };
        }

        // ── Lời báo cho người dùng ─────────────────────────────────────────
        /// <summary>
        /// Lời khuyên "nên kiểm tra theo các bước sau" (chuỗi rỗng nếu Workflow OFF / không tìm được gì).
        /// Gọi SAU khi đã rollback / nhả khoá PO. Chỉ đọc DB.
        /// tail: các bước cuối cố định của chính thao tác (vd "Huỷ lần nhận #5 của phiếu này").
        /// </summary>
        public static async Task<string> ExplainAsync(AppDbContext db, int khPlanDetailId, Action<LedgerInput> op,
                                                      ISet<string> opDocs, IEnumerable<string>? tail = null, bool loopOp = false)   // [K5-VONG-SUA] loopOp: xem Plan
        {
            try
            {
                var input = await new HandoverLedgerService(db).GetInputForDetailAsync(khPlanDetailId);
                if (input == null || !input.ApplyWorkflow) return "";

                // [WHY-TRAN-DB 2026-11-01] Đọc tên người nhận / người lập của MỌI phiếu + lần nhận của PO TRƯỚC Task.Run.
                //   Trước: đọc tên SAU khi Plan chạy xong (tới 5 s trên luồng nền) — trong lúc chờ, trang mở cho thao tác khác
                //   dùng chung DbContext của phiên; câu đọc tên lúc đó có thể đụng thao tác kia ("A second operation was started
                //   on this context") → gợi ý gỡ rỗng hoặc thao tác kia lỗi. Nay sau Task.Run KHÔNG còn câu DB nào, chỉ ghép chữ.
                //   Lọc theo PO (không gửi danh sách id). Đọc tên lỗi thì bỏ phần tên — lời báo vẫn đủ các bước.
                var rNames = new Dictionary<long, string>();
                var tNames = new Dictionary<long, string>();
                try
                {
                    rNames = (await db.HandoverReceives.AsNoTracking().Where(r => r.Transaction.KhPlanDetailId == khPlanDetailId)
                        .Select(r => new { r.ReceiveId, N = r.ReceivedByUser != null ? (r.ReceivedByUser.FullName ?? r.ReceivedByUser.Username) : "" }).ToListAsync())
                        .ToDictionary(x => x.ReceiveId, x => x.N);
                    tNames = (await db.HandoverTransactions.AsNoTracking().Where(t => t.KhPlanDetailId == khPlanDetailId)
                        .Select(t => new { t.HandoverTxId, N = t.IssuedByUser != null ? (t.IssuedByUser.FullName ?? t.IssuedByUser.Username) : "" }).ToListAsync())
                        .ToDictionary(x => x.HandoverTxId, x => x.N);
                }
                catch (Exception) { rNames.Clear(); tNames.Clear(); }

                var plan = await Task.Run(() => Plan(input, op, opDocs, loopOp: loopOp));   // [WHY-TRAN] tính trên bản sao trong bộ nhớ (không dùng DbContext) → chạy luồng nền, không giữ luồng trang
                var steps = plan.Steps;
                var tailList = tail?.ToList() ?? new List<string>();
                if (!steps.Any() && !tailList.Any())
                    return plan.Complete ? "" : "\nPhần mềm chưa lần ra chứng từ gây vướng — nên mở Lịch sử phiếu và WTS của PO này để kiểm.";

                // Tên người nhận / người lập (InTx / InRecv không mang tên): đã đọc sẵn trước Task.Run [WHY-TRAN-DB]

                var lines = new List<string>();
                // Gỡ từ chứng từ mới nhất ngược lên: thứ tự plan.Steps đã là thứ tự làm được
                foreach (var st in steps)
                {
                    var who = st.Kind == "NHAN" && rNames.TryGetValue(st.Id, out var rn) && !string.IsNullOrEmpty(rn) ? $" · {rn} nhận"
                            : st.Kind == "PHIEU" && tNames.TryGetValue(st.Id, out var tn) && !string.IsNullOrEmpty(tn) ? $" · {tn} lập" : "";
                    var hint = st.Kind switch
                    {
                        "WTS"  => "nếu nhập sai thì xoá / sửa dòng này ở trang WTS",
                        "NHAN" => "nếu nhận sai thì huỷ lần nhận này ở Lịch sử phiếu",
                        "BAOLOI" => "nếu báo sai thì huỷ phiếu báo lỗi này ở Lịch sử phiếu",   // [GN-NG-AFTER]
                        "KQSUA"  => "nếu ghi sai thì huỷ Kết quả sửa này ở Lịch sử phiếu",              // [GC-SUA-TAI-CHO]
                        "XNSUA"  => "nếu xác nhận sai thì huỷ lần KCS xác nhận này ở Lịch sử phiếu",     // [GC-SUA-TAI-CHO]
                        _      => "nếu giao sai thì huỷ phiếu này ở Lịch sử phiếu",
                    };
                    lines.Add($"Kiểm tra {st.Text}{who} — {hint}");
                }
                const int MaxShow = 8;
                var shown = lines.Take(MaxShow).ToList();
                if (lines.Count > MaxShow) shown.Add($"… và {lines.Count - MaxShow} chứng từ nữa (kiểm tiếp theo cùng thứ tự)");
                shown.AddRange(tailList);
                shown.Add("Sau đó thử lại thao tác này.");

                var sb = new System.Text.StringBuilder();
                sb.Append("\n\nSố này đang được dùng ở bước sau. Nếu chắc chắn thao tác này cần làm, nên kiểm tra theo các bước sau (từ chứng từ mới nhất ngược lên):");
                for (int i = 0; i < shown.Count; i++) sb.Append($"\n{i + 1}. {shown[i]}");
                if (!plan.Complete) sb.Append("\n(Phần mềm dừng tìm ở đây — có thể còn chứng từ khác, nên xem thêm Lịch sử phiếu / WTS của PO.)");
                sb.Append("\nĐây chỉ là gợi ý để kiểm tra, phần mềm không tự xoá gì; mỗi bước vẫn được kiểm khi lưu.");
                return sb.ToString();
            }
            catch (Exception)
            {
                // [GN-WHY] Phần gợi ý không bao giờ được làm hỏng lời báo chặn gốc
                return "";
            }
        }
    }
}
