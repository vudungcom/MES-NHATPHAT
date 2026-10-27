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
            t.Closed += t.Issued - rec; t.Issued = rec; t.Status = "COMPLETED";
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
        public static WhyPlan Plan(LedgerInput current, Action<LedgerInput> op, ISet<string> opDocs, int maxComputes = 80, int maxMs = 1500)
        {
            var plan = new WhyPlan();
            if (!current.ApplyWorkflow) { plan.Complete = true; return plan; }   // N7: Workflow OFF không chặn
            var sw = Stopwatch.StartNew();
            var S = Clone(current);
            LedgerRow? sRow = null;   // sổ của S (tính lại khi S đổi)
            bool Over() => plan.Computes >= maxComputes || sw.ElapsedMilliseconds > maxMs;
            LedgerRow Calc(LedgerInput x) { plan.Computes++; return HandoverLedgerService.Compute(x); }
            LedgerRow SRow() => sRow ??= Calc(S);
            List<string> Bad(Action<LedgerInput> o)
            {
                var a = Clone(S); o(a);
                return HandoverLedgerService.NewNegativeKeys(SRow(), Calc(a));
            }

            bool Clear(Action<LedgerInput> o, HashSet<string> skip, int depth)
            {
                for (int guard = 0; guard < 40; guard++)
                {
                    if (Over()) return false;
                    var bad = Bad(o);
                    if (bad.Count == 0) return true;
                    var d = NextCandidate(S, SRow(), bad, skip);
                    if (d == null) return false;
                    var removeD = d.Kind switch
                    {
                        "WTS"    => OpWts(new[] { d.Id }),
                        "NHAN"   => OpVoidReceive(d.Id),
                        "BAOLOI" => OpVoidNgRep(d.Id),   // [GN-NG-AFTER]
                        _        => OpVoidIssue(d.Id),
                    };
                    var sub = new HashSet<string>(skip) { Key(d) };
                    if (depth >= 8 || !Clear(removeD, sub, depth + 1)) return false;
                    removeD(S); sRow = null;
                    plan.Steps.Add(d);
                }
                return false;
            }

            plan.Complete = Clear(op, new HashSet<string>(opDocs), 0);
            return plan;
        }

        static string WtsWhere(InWts w) => w.ProcessGroup switch
        {
            "GC"      => "Gia công " + HandoverLedgerService.NcName(w.NC),
            "TARO"    => "HTSP Taro " + w.WtsCode,
            "BAVIA"   => "HTSP Bavia " + w.WtsCode,
            "WASHING" => "HTSP Rửa " + w.WtsCode,
            "KCS"     => "KCS " + w.WtsCode,
            "PKG"     => "Đóng gói " + w.WtsCode,
            _         => w.ProcessGroup + " " + (w.WtsCode ?? w.NC)
        };

        static string Key(WhyStep d) => (d.Kind == "WTS" ? "W:" : d.Kind == "NHAN" ? "R:" : d.Kind == "BAOLOI" ? "B:" : "T:") + d.Id;   // [GN-NG-AFTER] + B:

        /// <summary>Chứng từ mới nhất đang trừ vào một trong các ô sắp âm (đã quy về việc làm được ngay: phiếu có lần nhận → lần nhận mới nhất trước).</summary>
        static WhyStep? NextCandidate(LedgerInput s, LedgerRow row, List<string> badKeys, HashSet<string> skip)
        {
            var c = new List<WhyStep>();
            string Main(string pg, string? code)
                => code == null ? "" : row.CodeMain.TryGetValue(pg + "|" + code.Trim(), out var m) ? m : code.Trim();
            void AddWts(Func<InWts, bool> f)
            {
                foreach (var w in s.Wts.Where(w => w.Id > 0 && f(w)))
                    c.Add(new WhyStep { Kind = "WTS", Id = w.Id, At = w.At,
                        Text = $"WTS #{w.Id} · {WtsWhere(w)} · OK {w.Ok:0.##}"
                             + (w.Ng > 0 ? $" NG {w.Ng:0.##}" : "") + (string.IsNullOrEmpty(w.Worker) ? "" : $" · {w.Worker}") + $" · {w.At:dd/MM HH:mm}" });
            }
            void AddRep(Func<InNgRep, bool> f)   // [GN-NG-AFTER]
            {
                foreach (var b in s.NgReps.Where(b => b.Id > 0 && f(b)))
                    c.Add(new WhyStep { Kind = "BAOLOI", Id = b.Id, At = b.At,
                        Text = $"Báo lỗi #{b.Id} · {HandoverLedgerService.GroupLabel(b.Group)} · {b.Qty:0.##} chiếc · lấy từ {HandoverNgSource.SourceLabel(b.Source, b.Group)}"
                             + (string.IsNullOrEmpty(b.NC) ? "" : $" NC {b.NC}") + (string.IsNullOrEmpty(b.Who) ? "" : $" · {b.Who}") + $" · {b.At:dd/MM HH:mm}" });
            }
            void AddTx(Func<InTx, bool> f)
            {
                foreach (var t in s.Txs.Where(f))
                {
                    var a = Actionable(s, t);
                    if (a != null) c.Add(a);
                }
            }
            foreach (var k in badKeys)
            {
                var p = k.Split('|');
                var g = p[0];
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
                            break;
                        case "SuaGiu":
                            AddTx(t => t.From == g && t.FromCell == HandoverCell.Sua);
                            break;
                        case "LamHoGiu":
                            AddTx(t => t.From == g && t.Purpose == HandoverPurpose.HelpReturn);
                            break;
                    }
                }
                else if (p.Length == 3)
                {
                    var pg = p[1]; var code = p[2];
                    if (pg == "helpOut")
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
                        AddWts(w => w.ProcessGroup == pg && string.Equals(Main(pg, w.WtsCode), code, StringComparison.OrdinalIgnoreCase));
                        AddTx(t => t.From == g && t.Purpose == HandoverPurpose.NgReturn);
                        AddRep(b => b.Group == g && b.Source == HandoverNgSource.Chua);   // [GN-NG-AFTER]
                    }
                }
            }
            return c.Where(x => !skip.Contains(Key(x)))
                    .GroupBy(Key).Select(x => x.First())
                    .OrderByDescending(x => x.At ?? DateTime.MinValue).ThenByDescending(x => x.Id)
                    .FirstOrDefault();
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
                                                      ISet<string> opDocs, IEnumerable<string>? tail = null)
        {
            try
            {
                var input = await new HandoverLedgerService(db).GetInputForDetailAsync(khPlanDetailId);
                if (input == null || !input.ApplyWorkflow) return "";
                var plan = Plan(input, op, opDocs);
                var steps = plan.Steps;
                var tailList = tail?.ToList() ?? new List<string>();
                if (!steps.Any() && !tailList.Any())
                    return plan.Complete ? "" : "\nPhần mềm chưa lần ra chứng từ gây vướng — nên mở Lịch sử phiếu và WTS của PO này để kiểm.";

                // Thêm tên người nhận / người lập (InTx / InRecv không mang tên)
                var rIds = steps.Where(x => x.Kind == "NHAN").Select(x => x.Id).ToList();
                var tIds = steps.Where(x => x.Kind == "PHIEU").Select(x => x.Id).ToList();
                var rNames = rIds.Any()
                    ? (await db.HandoverReceives.AsNoTracking().Where(r => rIds.Contains(r.ReceiveId))
                        .Select(r => new { r.ReceiveId, N = r.ReceivedByUser != null ? (r.ReceivedByUser.FullName ?? r.ReceivedByUser.Username) : "" }).ToListAsync())
                        .ToDictionary(x => x.ReceiveId, x => x.N)
                    : new Dictionary<long, string>();
                var tNames = tIds.Any()
                    ? (await db.HandoverTransactions.AsNoTracking().Where(t => tIds.Contains(t.HandoverTxId))
                        .Select(t => new { t.HandoverTxId, N = t.IssuedByUser != null ? (t.IssuedByUser.FullName ?? t.IssuedByUser.Username) : "" }).ToListAsync())
                        .ToDictionary(x => x.HandoverTxId, x => x.N)
                    : new Dictionary<long, string>();

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
