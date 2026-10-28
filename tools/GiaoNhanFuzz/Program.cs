// Kiểm in/out ngẫu nhiên: Giao / Nhận (đủ + từng phần, có NG) / WTS (thêm + xoá) / Huỷ lần nhận / Huỷ phiếu / Huỷ phần chưa nhận.
// Gọi đúng hàm thật của HandoverService + HandoverLedgerService; WTS mô phỏng đúng 3 bước trang HieuSuat_Index:
//   (1) trần WtsCap, (2) thử lưu → NewNegatives (K5), (3) ghi dòng. Xoá WTS: NewNegatives với dòng bị bỏ.
// Sau MỖI thao tác thành công kiểm: cân PO, không ô âm (gồm từng NC GC + từng mã CV HTSP/KCS/PKG), WTS mỗi mã ≤ hàng thật,
// trạng thái phiếu khớp số đã nhận, phiếu trả tự lập khớp lần nhận gốc.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

// [FUZZ-UX] 27/10/2026: tự hiện tiếng Việt ở mọi cửa sổ cmd / VS, không cần gõ chcp 65001
Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length > 0 && args[0] == "scen") { await Scen.Run(); return 0; }
if (args.Length > 0 && args[0] == "kh") { return await KhScen.Run(); }
if (args.Length > 0 && args[0] == "bench") { await Bench.Run(); return 0; }
if (args.Length > 0 && args[0] == "loi") { return await LoiScen.Run(); }   // [GN-NG-TRACE] kịch bản hàng lỗi qua nhiều nơi
if (args.Length > 0 && args[0] == "vu") { return await VuScen.Run(); }   // [VULOI] kịch bản vụ lỗi: A/B/C/D + K5 theo vụ
if (args.Length > 0 && args[0] == "nang") { return await Nang.Run(args); }   // [GN-NG-TRACE] chạy nặng: nhiều cấu hình song song
var poCount = args.Length > 0 ? int.Parse(args[0]) : 200;
var opsPerPo = args.Length > 1 ? int.Parse(args[1]) : 120;
var seed0 = args.Length > 2 ? int.Parse(args[2]) : 1;
var stats = new Dictionary<string, int>();
void Inc(string k, int n = 1) { stats[k] = stats.TryGetValue(k, out var v) ? v + n : n; }
var fails = new List<string>();
int checks = 0;
bool dumped = false;
var whyMs = new List<double>(); var whyComputes = new List<int>(); var whyMiss = new List<string>(); string lastErr = "";
var All = new List<string> { "KHO", "GC", "HTSP", "KCS", "PKG" };

string[] groups = { "KHO", "GC", "HTSP", "KCS", "PKG" };
var editAll = groups.ToList();

var startPo = args.Length > 3 ? int.Parse(args[3]) : 0;
// [GN-NG-TRACE 2026-10-28] LOI=1: thử nặng hàng lỗi — chọn thao tác có hàng thật để làm (ít bỏ qua), NG nhiều hơn,
// nhiều phiếu Hàng lỗi / Hàng sửa hơn, không sửa phôi (trừ khi đặt PHOI). Không đặt LOI thì giữ nguyên như cũ (cùng seed ra cùng kết quả).
bool smart = Environment.GetEnvironmentVariable("LOI") == "1";
bool VuStrict = Environment.GetEnvironmentVariable("VU") != "0";   // [VULOI] mọi chứng từ lỗi mới đều phải gắn vụ (VU=0 để tắt)
var swProg = System.Diagnostics.Stopwatch.StartNew(); double lastProg = 0; // [FUZZ-UX] đồng hồ báo tiến độ
for (int po = startPo; po < startPo + poCount; po++)
{
    var rnd = new Random(seed0 * 100000 + po);
    var db = new AppDbContext();
    // ── Dữ liệu gốc 1 PO ──
    var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
    for (int u = 1; u <= 6; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : null, GroupId = u == 1 ? 1 : null });
    var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
    db.Customers.List.Add(cust);
    var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
    db.KhPlans.List.Add(plan);
    var qty = rnd.Next(8, 40);
    var det = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO" + po, Quantity = qty };
    db.KhPlanDetails.List.Add(det);
    db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = qty, NgayNhanPhoi = DateTime.Now, SoPO = "PO", PartNo = "P" });
    bool excl = rnd.NextDouble() < (Environment.GetEnvironmentVariable("EXCL") is string exv ? double.Parse(exv) : 0.3);
    var ncs = new[] { "NC1", "NC2", "NC3" };
    for (int i = 0; i < 3; i++)
        db.KhPlanRouteSnapshotMachining.List.Add(new KhPlanRouteSnapshotMachining
        { SnapshotId = i + 1, KhPlanDetailId = 1, StepOrder = i + 1, NC = ncs[i], MachineRegistered = excl && i == 1 ? "H04" : "M" + i });
    if (excl) db.KhPlanRouteSnapshotMachineExclude.List.Add(new KhPlanRouteSnapshotMachineExclude { SnapshotId = 1, KhPlanDetailId = 1, SoMay = "H04" });
    db.KhPlanRouteSnapshotTaro.List.Add(new KhPlanRouteSnapshotTaro { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "T1", WtsTaskCode = "T1" });
    db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 2, NC = "B1" });
    db.KhPlanRouteSnapshotBavia.List.Add(new KhPlanRouteSnapshotBavia { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 3, NC = "1V" });
    db.KhPlanRouteSnapshotWashing.List.Add(new KhPlanRouteSnapshotWashing { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 4, NC = "R1" });
    db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "K1" });
    db.KhPlanRouteSnapshotInspection.List.Add(new KhPlanRouteSnapshotInspection { SnapshotId = 2, KhPlanDetailId = 1, StepOrder = 2, NC = "K2" });
    db.KhPlanRouteSnapshotPackaging.List.Add(new KhPlanRouteSnapshotPackaging { SnapshotId = 1, KhPlanDetailId = 1, StepOrder = 1, NC = "P1" });
    VuHelp.SeedCats(db);   // [VULOI] 5 loại lỗi đang dùng + 1 đã ngưng (Id 6)
    db.Fixup();

    var svc = new HandoverService(db);
    var led = new HandoverLedgerService(db);
    var kho = new KhoVatLieuService(db, new UserService(null));
    var codeOf = new Dictionary<string, (string pg, string[] codes)>
    {
        ["GC"] = ("GC", ncs), ["HTSP"] = ("", new[] { "TARO|T1", "BAVIA|B1", "WASHING|R1" }),
        ["KCS"] = ("KCS", new[] { "K1", "K2" }), ["PKG"] = ("PKG", new[] { "P1" }),
    };

    for (int op = 0; op < opsPerPo; op++)
    {
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var kind = Pick(rnd, ("issue", 30), ("recv", 30), ("wts", 30), ("wtsvoid", 4), ("vrecv", 6), ("vissue", 6), ("close", 5), ("phoi", int.TryParse(Environment.GetEnvironmentVariable("PHOI"), out var pw) ? pw : smart ? 0 : 6),
            ("ngrep", int.TryParse(Environment.GetEnvironmentVariable("NGREP"), out var nw) ? nw : 10), ("vngrep", 4), ("wtsedit", int.TryParse(Environment.GetEnvironmentVariable("WTSEDIT"), out var we) ? we : 5));   // [VULOI] + sửa dòng WTS (giữ vụ)   // [GN-NG-AFTER] [GN-NG-TRACE] LOI=1: mặc định không sửa phôi
        string desc = kind;
        bool ok = false;
        try
        {
            switch (kind)
            {
                case "issue":
                {
                    var from = groups[rnd.Next(groups.Length)];
                    var special = rnd.NextDouble() < 0.12;
                    if (special)
                    {
                        var p = Pick(rnd, (HandoverPurpose.Scrap, 3), (HandoverPurpose.CustReturn, 2), (HandoverPurpose.HelpSend, 3), (HandoverPurpose.HelpReturn, 3));
                        var to = p == HandoverPurpose.HelpReturn ? "GC" : groups[rnd.Next(groups.Length)];
                        if (p == HandoverPurpose.HelpSend) from = "GC";
                        var fnc = p == HandoverPurpose.HelpSend ? ncs[rnd.Next(3)] : null;
                        var tnc = p == HandoverPurpose.HelpReturn ? ncs[rnd.Next(3)] : null;
                        // [VULOI] Huỷ / Trả khách: chọn 1 vụ đang giữ ở ô Hàng lỗi bên giao (trần theo vụ)
                        long? vu = null;
                        if (p is HandoverPurpose.Scrap or HandoverPurpose.CustReturn)
                        {
                            if (smart) { var fr = groups.Where(x => row.CasesHeldAt(x, HandoverCell.Loi).Any()).ToList(); if (fr.Any()) from = fr[rnd.Next(fr.Count)]; }
                            var held = row.CasesHeldAt(from, HandoverCell.Loi);
                            if (!held.Any()) { Inc("skip.issue"); continue; }
                            var pk = held[rnd.Next(held.Count)]; vu = pk.Id == 0 ? null : pk.Id;
                        }
                        var (max, _) = HandoverLedgerService.IssueCapCase(row, from, p, fnc, p is HandoverPurpose.Scrap or HandoverPurpose.CustReturn ? HandoverCell.Loi : null, vu);
                        if (max <= 0) { Inc("skip.issue"); continue; }
                        var q = rnd.NextDouble() < 0.06 ? (int)max + 1 : rnd.Next(1, (int)Math.Max(1, max) + 1);
                        desc = $"special {p} {from}->{to} {q} max={max} vu={vu} fnc={fnc} tnc={tnc}";
                        var r = await svc.IssueSpecialAsync(1, from, to, q, Uid(from), p, fnc, tnc, "ghi chu", null, vu);
                        ok = r.Ok; if (!ok) Inc("rej.issue");
                        if (ok && q > max) fails.Add($"[Giao vượt trần vụ] PO{po} op{op} {desc}");
                    }
                    else
                    {
                        var cell = smart ? Pick(rnd, (HandoverCell.Moi, 4), (HandoverCell.Loi, 4), (HandoverCell.Sua, 3)) : Pick(rnd, (HandoverCell.Moi, 6), (HandoverCell.Loi, 3), (HandoverCell.Sua, 2));
                        bool prev = cell == HandoverCell.Loi && rnd.NextDouble() < 0.5;
                        var toCell = rnd.NextDouble() < 0.8 ? HandoverCell.DefaultToCell(cell, prev) : HandoverCell.ReceiveCells[rnd.Next(4)];
                        var (purpose, _) = HandoverCell.ResolveCells(cell, prev, toCell);
                        if (purpose == null) { Inc("skip.issue"); continue; }
                        string to = ""; string? fnc = null; decimal max = 0; long? vu = null;   // [VULOI]
                        bool takes = HandoverLedgerService.TakesHeldCase(purpose, cell);
                        // [GN-NG-TRACE] LOI=1: thử lần lượt các nhóm giao (thứ tự ngẫu nhiên) tới khi có hàng ở ô đã chọn
                        foreach (var f in smart ? groups.OrderBy(_ => rnd.Next()).ToArray() : new[] { from })
                        {
                            from = f;
                            to = rnd.NextDouble() < 0.7 ? groups[(Array.IndexOf(groups, from) + (prev ? 4 : 1)) % 5] : groups[rnd.Next(groups.Length)];
                            if (to == from) continue;
                            fnc = null;
                            if (prev && from == "GC")
                            {
                                var cands = smart ? row.G("GC").Steps.Where(x => !x.IsExcluded && (x.Waiting ?? 0) > 0).Select(x => x.Code).ToList() : new List<string>();
                                fnc = cands.Any() ? cands[rnd.Next(cands.Count)] : ncs[rnd.Next(3)];
                            }
                            vu = null;
                            if (takes)
                            {   // [VULOI] chọn 1 vụ đang giữ ở ô lấy hàng
                                var held = row.CasesHeldAt(from, cell);
                                if (!held.Any()) { max = 0; continue; }
                                var pk = held[rnd.Next(held.Count)]; vu = pk.Id == 0 ? null : pk.Id;
                            }
                            (max, _) = HandoverLedgerService.IssueCapCase(row, from, purpose, fnc, cell, vu);
                            if (max > 0) break;
                        }
                        if (to == from || to == "") { Inc("skip.issue"); continue; }
                        if (max <= 0) { Inc("skip.issue"); continue; }
                        var q = rnd.NextDouble() < 0.08 ? (int)max + 1 : rnd.Next(1, (int)Math.Max(1, max) + 1);
                        // [VULOI] Hàng lỗi công đoạn trước: chọn loại lỗi (4% cố tình bỏ trống / loại đã ngưng → phải bị chặn)
                        int? cat = prev ? (rnd.NextDouble() < 0.04 ? (rnd.NextDouble() < 0.5 ? null : 6) : rnd.Next(1, 6)) : null;
                        desc = $"issue {from}->{to} {cell}{(prev ? "☑" : "")}->{toCell} q={q} max={max} fnc={fnc} vu={vu} cat={cat}";
                        var r = await svc.IssueByCellAsync(1, from, to, q, Uid(from), cell, prev, toCell, fnc, null, null, null, cat, vu);
                        ok = r.Ok; if (!ok) Inc("rej.issue");
                        if (ok && q > max) fails.Add($"[Giao vượt trần vụ] PO{po} op{op} {desc}");
                        if (ok && prev && cat is null or 6) fails.Add($"[Hàng lỗi công đoạn trước không có loại lỗi] PO{po} op{op} {desc}");
                        if (ok && prev)
                        {
                            var nt = db.HandoverTransactions.List.Last();
                            var ncs0 = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == nt.NgCaseId);
                            if (ncs0 == null || ncs0.OriginKind != NgOrigin.TraLoi || ncs0.NgCategoryId != cat || ncs0.DetectGroupCode != from || ncs0.CauseGroupCode != to)
                                fails.Add($"[Vụ phiếu công đoạn trước sai nhãn] PO{po} op{op} {desc}");
                        }
                    }
                    break;
                }
                case "recv":
                {
                    var pend = db.HandoverTransactions.List.Where(t => !t.IsVoided && t.Status != "COMPLETED" && !HandoverPurpose.IsOut(t.Purpose)).ToList();
                    if (!pend.Any()) { Inc("skip.recv"); continue; }
                    var t = pend[rnd.Next(pend.Count)];
                    var rem = t.QtyIssued - t.Receives.Where(x => !x.IsVoided).Sum(x => x.QtyOk + x.QtyNg);
                    var q = rnd.NextDouble() < 0.6 || rem <= 1 ? rem : rnd.Next(1, (int)rem);
                    var cell = t.SourceReceiveId != null ? HandoverCell.Loi : (HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell : null);
                    // [VULOI] 15%: bên nhận đổi ô (thử nhận vào Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý / Hàng mới khác ô chỉ định)
                    string? pickCell = null;
                    if (cell != null && t.SourceReceiveId == null && rnd.NextDouble() < 0.15)
                    { pickCell = HandoverCell.ReceiveCells[rnd.Next(4)]; if (pickCell == cell) pickCell = null; else cell = pickCell; }
                    decimal ng = 0;
                    if (cell == HandoverCell.Loi) ng = q;
                    else if (cell != HandoverCell.Sua && rnd.NextDouble() < (smart ? 0.4 : 0.25)) ng = rnd.Next(1, (int)q + 1);
                    var okq = q - ng;
                    // [VULOI] Lần nhận sinh vụ mới (phiếu không mang vụ + vào ô Hàng lỗi / Hàng sửa, hoặc NG lúc nhận) thì phải chọn loại lỗi.
                    var tfc = row.TxFromCell.TryGetValue(t.HandoverTxId, out var tf) ? tf : null;
                    bool carryR = t.SourceReceiveId == null && tfc is HandoverCell.Loi or HandoverCell.Sua;
                    bool needCat = t.SourceReceiveId == null && !carryR && (cell is HandoverCell.Loi or HandoverCell.Sua || (cell != HandoverCell.Sua && (cell == HandoverCell.Loi ? q : ng) > 0));
                    int? rcat = rnd.NextDouble() < 0.06 ? null : rnd.Next(1, 6);
                    desc = $"recv #{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} cell={cell}{(pickCell != null ? "(đổi)" : "")} ok={okq} ng={ng} rem={rem} cat={rcat} carry={carryR}";
                    var r = await svc.ReceiveAsync(t.HandoverTxId, cell == HandoverCell.Loi ? 0 : okq, cell == HandoverCell.Sua ? 0 : ng, null, Uid(t.ToGroupCode),
                        null, pickCell, pickCell != null ? "doi o" : null, rcat);
                    ok = r.Ok; if (!ok) { Inc("rej.recv"); desc += " ERR " + r.Error; }
                    if (ok && needCat && rcat == null) fails.Add($"[Nhận sinh vụ không có loại lỗi] PO{po} op{op} {desc}");
                    if (!ok && !needCat && r.Error == NgCaseService.MsgPickCategory) fails.Add($"[Nhận phiếu mang vụ bị bắt chọn lỗi] PO{po} op{op} {desc}");
                    if (ok)
                    {
                        var rr0 = db.HandoverReceives.List.Last();
                        if (needCat && (rr0.NgCaseId == null || db.NgCases.List.First(x => x.NgCaseId == rr0.NgCaseId).NgCategoryId != rcat)) fails.Add($"[Lần nhận sinh vụ sai] PO{po} op{op} {desc}");
                        if (!needCat && rr0.NgCaseId != t.NgCaseId) fails.Add($"[Lần nhận không theo vụ phiếu] PO{po} op{op} {desc}");
                        if (ok && pickCell != null) Inc("vu.recv.doio");
                    }
                    break;
                }
                case "wts":
                {
                    var g = new[] { "GC", "HTSP", "KCS", "PKG" }[rnd.Next(4)];
                    var cs = codeOf[g].codes;
                    var c = cs[rnd.Next(cs.Length)];
                    string pg = codeOf[g].pg, code = c;
                    if (g == "HTSP") { pg = c.Split('|')[0]; code = c.Split('|')[1]; }
                    var cap = HandoverLedgerService.WtsCap(row, pg, code);
                    if (smart && (cap.Max == null || cap.Locked || cap.Max <= 0))
                    {
                        // [GN-NG-TRACE] LOI=1: tìm mã CV / NC đang có hàng chờ
                        var all = codeOf.SelectMany(kv => kv.Value.codes.Select(x => (kv.Key, x))).OrderBy(_ => rnd.Next()).ToList();
                        foreach (var (gg, cc) in all)
                        {
                            string p2 = codeOf[gg].pg, c2 = cc;
                            if (gg == "HTSP") { p2 = cc.Split('|')[0]; c2 = cc.Split('|')[1]; }
                            var cp = HandoverLedgerService.WtsCap(row, p2, c2);
                            if (cp.Max != null && !cp.Locked && cp.Max > 0) { g = gg; pg = p2; code = c2; cap = cp; break; }
                        }
                    }
                    if (cap.Max == null || cap.Locked || cap.Max <= 0) { Inc("skip.wts"); continue; }
                    var q = rnd.Next(1, (int)cap.Max.Value + 1);
                    var ng = rnd.NextDouble() < (smart ? 0.4 : 0.2) ? rnd.Next(0, q + 1) : 0;
                    var extra = new List<InWts> { new InWts(-1, DateTime.Now, pg, g == "GC" ? code : null, g == "GC" ? null : code, q - ng, ng, null, ng > 0 ? -1 : null) };   // [VULOI] dòng mới: vụ mới
                    var after = (await led.GetLedgerForDetailAsync(1, null, extra))!;
                    var bad = HandoverLedgerService.NewNegatives(row, after);
                    desc = $"wts {pg} {code} ok={q - ng} ng={ng} cap={cap.Max}";
                    if (bad.Any()) { if (Environment.GetEnvironmentVariable("DUMP") == "1") { Console.WriteLine("BEFORE " + desc); Dbg.Dump(row); foreach (var t in db.HandoverTransactions.List) Console.WriteLine($"   tx#{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} {t.Purpose} {t.FromCell}->{t.ToCell} q={t.QtyIssued} cl={t.QtyClosed} st={t.Status} void={t.IsVoided} fnc={t.FromNC} tnc={t.ToNC} src={t.SourceReceiveId} rec={string.Join(",", t.Receives.Select(r => $"#{r.ReceiveId}:{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}"); foreach (var w in db.WtsProductionLogs.List) Console.WriteLine($"   wts#{w.WtsLogId} {w.ProcessGroup} {w.NC}{w.WtsCode} {w.QtyOk}/{w.QtyNg} void={w.IsVoided}"); Console.WriteLine("AFTER"); Dbg.Dump(after); } Inc("rej.wts.k5"); fails.Add($"[WTS bị chặn oan dù trong trần] PO{po} op{op} {desc} → {string.Join("; ", bad)}"); continue; }
                    await VuHelp.Wts(db, pg, g == "GC" ? code : null, g == "GC" ? null : code, q - ng, ng, Uid(g), rnd.Next(1, 6));   // [VULOI] NG → vụ mới (như trang WTS)
                    ok = true;
                    break;
                }
                case "wtsvoid":
                {
                    var ws = db.WtsProductionLogs.List.Where(w => !w.IsVoided).ToList();
                    if (!ws.Any()) { Inc("skip.wtsvoid"); continue; }
                    var w = ws[rnd.Next(ws.Count)];
                    var after = (await led.GetLedgerForDetailAsync(1, new List<long> { w.WtsLogId }))!;
                    var bad = HandoverLedgerService.NewNegatives(row, after);
                    desc = $"wtsvoid #{w.WtsLogId} {w.ProcessGroup} {w.NC ?? w.WtsCode}";
                    if (bad.Any())
                    {
                        Inc("rej.wtsvoid");
                        var wid = w.WtsLogId;
                        await WhyCheck(db, svc, led, HandoverWhy.OpWts(new List<long> { wid }), new HashSet<string> { "W:" + wid },
                            async () => await WtsVoid(db, led, wid), null, rnd, po, op, desc, null);
                        continue;
                    }
                    w.IsVoided = true; ok = true;
                    break;
                }
                case "vrecv":
                {
                    var rs = db.HandoverReceives.List.Where(r => !r.IsVoided && !r.Transaction.IsVoided).ToList();
                    if (!rs.Any()) { Inc("skip.vrecv"); continue; }
                    var r0 = rs[rnd.Next(rs.Count)];
                    desc = $"vrecv #{r0.ReceiveId} (phiếu #{r0.HandoverTxId} {r0.Transaction.FromGroupCode}->{r0.Transaction.ToGroupCode} ok={r0.QtyOk} ng={r0.QtyNg})";
#if NEWAPI
                    // người không có quyền: phải bị chặn
                    var wrong = groups.Where(x => x != r0.Transaction.ToGroupCode).ToList();
                    var deny = await svc.VoidReceiveAsync(r0.ReceiveId, "huy thu", 99, wrong);
                    if (deny.Ok) fails.Add($"[Quyền] PO{po} op{op} người ngoài nhóm nhận huỷ được lần nhận #{r0.ReceiveId}");
                    var res = await svc.VoidReceiveAsync(r0.ReceiveId, "huy thu", Uid(r0.Transaction.ToGroupCode), new List<string> { r0.Transaction.ToGroupCode });
#else
                    var res = await svc.VoidReceiveAsync(r0.ReceiveId, "huy thu", 1);
#endif
                    ok = res.Ok; if (!ok) Inc("rej.vrecv");
#if NEWAPI
                    if (!ok && res.Error.StartsWith("Không lưu"))
                    {
                        var rid = r0.ReceiveId; var grp = r0.Transaction.ToGroupCode;
                        await WhyCheck(db, svc, led, HandoverWhy.OpVoidReceive(rid), new HashSet<string> { "R:" + rid },
                            async () => (await svc.VoidReceiveAsync(rid, "thu lai", Uid(grp), All)).Ok, null, rnd, po, op, desc, res.Error);
                    }
#endif
                    break;
                }
                case "vissue":
                {
                    var ts = db.HandoverTransactions.List.Where(t => !t.IsVoided).ToList();
                    if (!ts.Any()) { Inc("skip.vissue"); continue; }
                    var t = ts[rnd.Next(ts.Count)];
                    desc = $"vissue #{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} {t.Purpose} st={t.Status} auto={t.SourceReceiveId}";
#if NEWAPI
                    var wrong = groups.Where(x => x != t.FromGroupCode).ToList();
                    var deny = await svc.VoidIssueAsync(t.HandoverTxId, "huy thu", 99, wrong);
                    if (deny.Ok) fails.Add($"[Quyền] PO{po} op{op} người ngoài nhóm giao huỷ được phiếu #{t.HandoverTxId}");
                    // người lập phiếu (không cần quyền nhóm) huỷ được
                    var res = await svc.VoidIssueAsync(t.HandoverTxId, "huy thu", t.IssuedBy, new List<string>());
#else
                    var res = await svc.VoidIssueAsync(t.HandoverTxId, "huy thu", 1);
#endif
                    ok = res.Ok; if (!ok) Inc("rej.vissue");
                    if (ok && t.SourceReceiveId != null) Inc("ok.vissue.auto");
#if NEWAPI
                    if (!ok && (res.Error.StartsWith("Không lưu") || res.Error.StartsWith("Phiếu đã có bên nhận")))
                    {
                        var tid = t.HandoverTxId; var issuer = t.IssuedBy;
                        Action<LedgerInput> opFn; var opDocs = new HashSet<string>(); Func<Task<bool>>? pre = null;
                        if (t.SourceReceiveId != null) { opFn = HandoverWhy.OpVoidReceive(t.SourceReceiveId.Value); opDocs.Add("R:" + t.SourceReceiveId.Value); }
                        else
                        {
                            opFn = HandoverWhy.OpVoidIssue(tid); opDocs.Add("T:" + tid);
                            var own = t.Receives.Where(x => !x.IsVoided).OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.ReceiveId).Select(x => x.ReceiveId).ToList();
                            foreach (var rr in own)
                            {
                                opDocs.Add("R:" + rr);
                                foreach (var b in db.HandoverTransactions.List.Where(b => !b.IsVoided && b.SourceReceiveId == rr))
                                { opDocs.Add("T:" + b.HandoverTxId); foreach (var br in b.Receives.Where(x => !x.IsVoided)) opDocs.Add("R:" + br.ReceiveId); }
                            }
                            if (own.Any()) pre = async () =>
                            {
                                foreach (var rr in own)
                                {
                                    foreach (var b in db.HandoverTransactions.List.Where(b => !b.IsVoided && b.SourceReceiveId == rr).ToList())
                                        foreach (var br in b.Receives.Where(x => !x.IsVoided).OrderByDescending(x => x.ReceivedAt).Select(x => x.ReceiveId).ToList())
                                        { var vr = await svc.VoidReceiveAsync(br, "goi y", 1, All); if (!vr.Ok) { lastErr = "auto-recv #" + br + ": " + vr.Error; if (Environment.GetEnvironmentVariable("WHYDBG") == "1") { foreach (var tt in db.HandoverTransactions.List) Console.WriteLine($"   tx#{tt.HandoverTxId} {tt.FromGroupCode}->{tt.ToGroupCode} {tt.Purpose} {tt.FromCell}->{tt.ToCell} q={tt.QtyIssued} void={tt.IsVoided} src={tt.SourceReceiveId} rec={string.Join(",", tt.Receives.Select(r => $"#{r.ReceiveId}:{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}"); Console.WriteLine("   phoi=" + db.KhoVatLieus.List.First().SoLuongThucNhan); } return false; } }
                                    var rv = await svc.VoidReceiveAsync(rr, "goi y", 1, All);
                                    if (!rv.Ok) { lastErr = rv.Error; return false; }
                                }
                                return true;
                            };
                        }
                        await WhyCheck(db, svc, led, opFn, opDocs,
                            async () => (await svc.VoidIssueAsync(tid, "thu lai", issuer, new List<string>())).Ok, pre, rnd, po, op, desc, res.Error);
                    }
#endif
                    break;
                }
                case "wtsedit":   // [VULOI] sửa dòng WTS (void + dòng mới, tổng OK + NG giữ nguyên): dòng mới giữ vụ cũ; NG giảm mà vụ đã đi tiếp → K5 chặn
                {
                    var ws = db.WtsProductionLogs.List.Where(w => !w.IsVoided && w.QtyDone > 0).ToList();
                    if (!ws.Any()) { Inc("skip.wtsedit"); continue; }
                    var w = ws[rnd.Next(ws.Count)];
                    var tot = w.QtyDone; var ng2 = (decimal)rnd.Next(0, (int)tot + 1);
                    var keep = ng2 > 0 ? w.NgCaseId : null;
                    var extra = new List<InWts> { new InWts(-1, DateTime.Now, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, null, ng2 > 0 ? (keep ?? -1) : null) };
                    var after = (await led.GetLedgerForDetailAsync(1, new List<long> { w.WtsLogId }, extra))!;
                    var bad = HandoverLedgerService.NewNegatives(row, after);
                    desc = $"wtsedit #{w.WtsLogId} {w.ProcessGroup} {w.NC ?? w.WtsCode} NG {w.QtyNg}->{ng2} vu={w.NgCaseId}";
                    if (bad.Any()) { Inc("rej.wtsedit"); if (w.NgCaseId != null && bad.Any(b => b.Contains("VL"))) Inc("vu.k5.wtsedit"); continue; }
                    // 30%: đổi loại lỗi của vụ cũ (chỉ được khi vụ chưa đi phiếu nào)
                    if (keep != null && rnd.NextDouble() < 0.3)
                    {
                        var moved = db.HandoverTransactions.List.Any(t => !t.IsVoided && t.NgCaseId == keep);
                        var cs0 = db.NgCases.List.First(x => x.NgCaseId == keep);
                        var newCat = cs0.NgCategoryId % 5 + 1;
                        var err = await NgCaseService.ChangeCaseCategoryAsync(db, keep.Value, newCat);
                        if (moved && err == null) fails.Add($"[Đổi loại lỗi vụ đã đi phiếu] PO{po} op{op} {desc}");
                        if (!moved && (err != null || cs0.NgCategoryId != newCat)) fails.Add($"[Không đổi được loại lỗi vụ chưa đi] PO{po} op{op} {desc}: {err}");
                        if (err == null) Inc("vu.doiloai");
                    }
                    w.IsVoided = true;
                    await VuHelp.Wts(db, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, w.WorkerId, db.NgCases.List.FirstOrDefault(x => x.NgCaseId == keep)?.NgCategoryId ?? rnd.Next(1, 6), keep);
                    ok = true;
                    break;
                }
                case "ngrep":   // [GN-NG-AFTER] báo hàng lỗi phát hiện sau khi nhận (hàm thật ReportNgAfterReceiveAsync)
                {
                    var g = groups[rnd.Next(groups.Length)];
                    var G = row.G(g);
                    var srcs = g == "KHO" ? new[] { HandoverNgSource.Phoi } : new[] { HandoverNgSource.Chua, HandoverNgSource.Da };
                    var src = srcs[rnd.Next(srcs.Length)];
                    string? nc = null;
                    if (g == "GC" && src == HandoverNgSource.Chua)
                    {
                        var cand = G.Steps.Where(x => !x.IsExcluded && (x.Waiting ?? 0) > 0).ToList();
                        if (!cand.Any()) { Inc("skip.ngrep"); continue; }
                        nc = cand[rnd.Next(cand.Count)].Code;
                    }
                    var cap = HandoverLedgerService.NgReportCap(row, g, src, nc).Max;
                    if (cap <= 0) { Inc("skip.ngrep"); continue; }
                    var over = rnd.NextDouble() < 0.1;
                    var q = over ? cap + 1 : rnd.Next(1, (int)Math.Max(1, cap) + 1);
                    if (q > cap && !over) q = cap;
                    var origin = rnd.NextDouble() < 0.5 ? HandoverNgSource.DauVao : HandoverNgSource.DauRa;
                    var cause = origin == HandoverNgSource.DauVao ? (g == "KHO" ? "KHACH" : groups[Math.Max(0, Array.IndexOf(groups, g) - 1)]) : null;
                    desc = $"ngrep {g} {src} {nc} q={q} cap={cap} {origin}";
                    var deny = await svc.ReportNgAfterReceiveAsync(1, g, src, nc, Math.Min(q, cap), origin, cause, "loi test", null, 99, groups.Where(x => x != g).ToList(), 1);
                    if (deny.Ok) fails.Add($"[Quyền báo lỗi] PO{po} op{op} người ngoài nhóm báo lỗi được cho {g}");
                    // [VULOI] loại lỗi bắt buộc chọn từ danh mục đang dùng: thử bỏ trống / loại đã ngưng → phải bị chặn
                    if (rnd.NextDouble() < 0.05)
                    {
                        var noCat = await svc.ReportNgAfterReceiveAsync(1, g, src, nc, Math.Min(q, cap), origin, cause, "loi test", null, Uid(g), new List<string> { g }, rnd.NextDouble() < 0.5 ? null : 6);
                        if (noCat.Ok) fails.Add($"[Báo lỗi không có loại lỗi] PO{po} op{op} {desc}");
                    }
                    var bcat = rnd.Next(1, 6);
                    var res = await svc.ReportNgAfterReceiveAsync(1, g, src, nc, q, origin, cause, "loi test", null, Uid(g), new List<string> { g }, bcat);
                    ok = res.Ok; if (!ok) Inc("rej.ngrep");
                    if (ok)
                    {
                        var bb = db.HandoverNgReports.List.Last(); var vc = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == bb.NgCaseId);
                        if (vc == null || vc.NgCategoryId != bcat || vc.OriginKind != NgOrigin.BaoLoi || vc.DetectGroupCode != g || bb.NgReason != VuHelp.Label(db, bcat))
                            fails.Add($"[Vụ báo lỗi sai nhãn] PO{po} op{op} {desc}");
                    }
                    if (over && ok) fails.Add($"[Báo lỗi vượt trần] PO{po} op{op} {desc}");
                    if (!over && !ok && !res.Error.StartsWith("Không lưu")) fails.Add($"[Báo lỗi trong trần bị từ chối] PO{po} op{op} {desc}: {res.Error}");
                    break;
                }
                case "vngrep":   // [GN-NG-AFTER] huỷ báo lỗi
                {
                    var bs = db.HandoverNgReports.List.Where(b => !b.IsVoided).ToList();
                    if (!bs.Any()) { Inc("skip.vngrep"); continue; }
                    var b = bs[rnd.Next(bs.Count)];
                    desc = $"vngrep #{b.HandoverNgReportId} {b.GroupCode} {b.SourceCell} {b.NC} q={b.Qty}";
                    var deny = await svc.VoidNgReportAsync(b.HandoverNgReportId, "huy thu", 99, groups.Where(x => x != b.GroupCode).ToList());
                    if (deny.Ok) fails.Add($"[Quyền huỷ báo lỗi] PO{po} op{op} người ngoài nhóm huỷ được báo lỗi #{b.HandoverNgReportId}");
                    var bid = b.HandoverNgReportId; var by = b.CreatedBy;
                    var res = await svc.VoidNgReportAsync(bid, "huy thu", by, new List<string>());
                    ok = res.Ok; if (!ok) Inc("rej.vngrep");
                    if (!ok && res.Error.StartsWith("Không lưu"))
                        await WhyCheck(db, svc, led, HandoverWhy.OpVoidNgRep(bid), new HashSet<string> { "B:" + bid },
                            async () => (await svc.VoidNgReportAsync(bid, "thu lai", by, new List<string>())).Ok, null, rnd, po, op, desc, res.Error);
                    break;
                }
                case "phoi":   // [KH-GUARD] Kho sửa SL phôi nhận (hàm thật KhoVatLieuService.SaveKhoInputAsync)
                {
                    var kRow = db.KhoVatLieus.List.First(x => x.PlanDetailId == 1 && x.IsActive);
                    var cur = kRow.SoLuongThucNhan ?? 0;
                    var nq = Math.Max(0, cur + rnd.Next(-12, 7));
                    if (nq == cur) { Inc("skip.phoi"); continue; }
                    desc = $"phoi {cur}->{nq}";
                    var kid = kRow.KhoVatLieuId;
                    var res = await kho.SaveKhoInputAsync(1, DateTime.Now, nq, null, null, null, null, null, "sua phoi", 1);
                    ok = res.Success; if (!ok) Inc("rej.phoi");
                    var after = db.KhoVatLieus.List.First(x => x.PlanDetailId == 1 && x.IsActive).SoLuongThucNhan ?? 0;
                    if (!ok && after != cur) fails.Add($"[Phôi không rollback] PO{po} op{op} {desc} còn {after}");
                    if (ok && after != nq) fails.Add($"[Phôi không lưu] PO{po} op{op} {desc} còn {after}");
                    if (!ok && !res.Error!.Contains("Không lưu")) fails.Add($"[Phôi lời báo lạ] PO{po} op{op} {res.Error}");
                    if (!ok && res.Error!.Contains("Không lưu"))
                        await WhyCheck(db, svc, led, HandoverWhy.OpSetPhoi(kid, nq), new HashSet<string>(),
                            async () => (await kho.SaveKhoInputAsync(1, DateTime.Now, nq, null, null, null, null, null, "thu lai", 1)).Success, null, rnd, po, op, desc, res.Error);
                    break;
                }
                case "close":
                {
#if NEWAPI
                    var ts = db.HandoverTransactions.List.Where(t => !t.IsVoided && t.Status == "PARTIAL").ToList();
                    if (!ts.Any()) { Inc("skip.close"); continue; }
                    var t = ts[rnd.Next(ts.Count)];
                    desc = $"close #{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} issued={t.QtyIssued} auto={t.SourceReceiveId}";
                    var deny = await svc.CloseRemainderAsync(t.HandoverTxId, "huy phan con", 99, groups.Where(x => x != t.FromGroupCode).ToList());
                    if (deny.Ok) fails.Add($"[Quyền] PO{po} op{op} người ngoài nhóm giao huỷ được phần chưa nhận #{t.HandoverTxId}");
                    var res = await svc.CloseRemainderAsync(t.HandoverTxId, "huy phan con", Uid(t.FromGroupCode), new List<string> { t.FromGroupCode });
                    ok = res.Ok; if (!ok) Inc("rej.close");
                    if (!ok && res.Error.StartsWith("Không lưu"))
                    {
                        var tid = t.HandoverTxId; var fg = t.FromGroupCode;
                        await WhyCheck(db, svc, led, HandoverWhy.OpCloseRemainder(tid), new HashSet<string> { "T:" + tid },
                            async () => (await svc.CloseRemainderAsync(tid, "thu lai", Uid(fg), All)).Ok, null, rnd, po, op, desc, res.Error);
                    }
                    if (ok && t.SourceReceiveId != null) fails.Add($"[Đóng phiếu tự lập] PO{po} op{op} #{t.HandoverTxId}");
#else
                    Inc("skip.close"); continue;
#endif
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            fails.Add($"[EXCEPTION] PO{po} op{op} {desc}: {ex.GetType().Name} {ex.Message}\n{ex.StackTrace?.Split('\n').FirstOrDefault()}");
            continue;
        }
        if (db.Database.Current != null) { fails.Add($"[Giao dịch treo] PO{po} op{op} {desc}"); db.Database.Current = null; }
        if (!ok) continue;
        Inc("ok." + kind);
        await CheckAll(db, led, po, op, desc);
    }
    // [FUZZ-UX] 27/10/2026: báo tiến độ ~5 giây một lần + khi xong, để biết tool đang chạy (không đổi phép kiểm nào)
    var poDone = po - startPo + 1;
    if (poDone == poCount || swProg.Elapsed.TotalSeconds - lastProg >= 5)
    {
        lastProg = swProg.Elapsed.TotalSeconds;
        var conLai = poDone < poCount ? $" · còn khoảng {lastProg / poDone * (poCount - poDone):0} giây" : "";
        Console.WriteLine($"  đã chạy {poDone}/{poCount} PO · sai {fails.Count} · {lastProg:0} giây{conLai}");
    }
}

Console.WriteLine($"PO: {poCount} · thao tác/PO: {opsPerPo} · seed {seed0}");
if (whyMs.Any()) Console.WriteLine($"  [GN-WHY] tìm gốc rễ: {whyMs.Count} lần · trung bình {whyMs.Average():0.0} ms · lâu nhất {whyMs.Max():0.0} ms · số lần tính sổ TB {whyComputes.Average():0.0} / lớn nhất {whyComputes.Max()}");
foreach (var m in whyMiss) Console.WriteLine("  [GN-WHY chưa lần ra hết] " + m);
foreach (var kv in stats.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Key,-20} {kv.Value}");
Console.WriteLine($"Phép kiểm: {checks}");
var groupsFail = fails.GroupBy(f => f.Split(']')[0] + "]").ToList();
Console.WriteLine($"SAI: {fails.Count}");
foreach (var g in groupsFail) { Console.WriteLine($"  {g.Key} × {g.Count()}"); foreach (var f in g.Take(3)) Console.WriteLine("     " + f); }
return fails.Count == 0 ? 0 : 1;

async Task CheckAll(AppDbContext db, HandoverLedgerService led, int po, int op, string desc)
{
    var row = (await led.GetLedgerForDetailAsync(1))!;
    void F(string tag, string msg) => fails.Add($"[{tag}] PO{po} op{op} sau «{desc}»: {msg}");
    checks++;
    if (!row.Balanced) F("Lệch cân PO", $"Phôi {row.Phoi} ≠ {row.BalanceExplain.Result}");
    checks++;
    var neg = HandoverLedgerService.NegativeCells(row);
    if (neg.Any()) F("Ô âm", string.Join("; ", neg.Values.Select(v => $"{v.Text}={v.Value}")));
    // WTS từng mã CV / NC không vượt hàng thật (độc lập với NegativeCells: đọc thẳng Waiting của mọi dòng)
    foreach (var G in row.Groups.Values)
        foreach (var s in G.Steps.Where(s => !s.IsPrep && !s.IsExcluded && s.Waiting.HasValue))
        {
            checks++;
            if (s.Waiting!.Value < 0) F("WTS vượt hàng thật", $"{G.Label} {s.Pg} {s.Code}: chờ {s.Waiting} (OK {s.Ok} NG {s.Ng})");
        }
    foreach (var G in row.Groups.Values)
        foreach (var w in G.Warnings.Where(w => w.Contains("WTS nhập vượt")))
        { checks++; F("WTS vượt hàng thật", w); }
    // GC: Σ chờ các NC = Chưa GC
    var gc = row.G("GC");
    var gcLines = gc.Steps.Where(s => !s.IsExcluded && s.Waiting.HasValue).ToList();
    if (gcLines.Any() && !gc.Steps.Any(s => s.IsExcluded))   // có NC loại trừ: hàng chờ gửi làm hộ nằm trong Chưa GC, không thuộc NC nào
    {
        checks++;
        var sum = gcLines.Sum(s => s.Waiting!.Value);
        if (sum != gc.ChuaLam) { F("Σ chờ NC ≠ Chưa GC", $"{sum} ≠ {gc.ChuaLam}"); if (Environment.GetEnvironmentVariable("DUMP") == "1" && !dumped) { dumped = true; Console.WriteLine(desc); Dbg.Dump(row); foreach (var t in db.HandoverTransactions.List) Console.WriteLine($"   tx#{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} {t.Purpose} {t.FromCell}->{t.ToCell} q={t.QtyIssued} st={t.Status} void={t.IsVoided} fnc={t.FromNC} tnc={t.ToNC} rec={string.Join(",", t.Receives.Select(r => $"{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}"); foreach (var w in db.WtsProductionLogs.List) Console.WriteLine($"   wts {w.ProcessGroup} {w.NC}{w.WtsCode} {w.QtyOk}/{w.QtyNg} void={w.IsVoided}"); } }
    }
    // [GN-WHERE] "PO đang ở đâu": cộng mọi ô = phôi nhận; không ô nào âm (Workflow ON); không có NC loại trừ thì không có phần chênh NC
    {
        var wh = HandoverLedgerService.WhereIs(row);
        checks++;
        if (!wh.Balanced) F("Đang ở đâu lệch", $"tổng {wh.Total} ≠ phôi {wh.Phoi}: " + string.Join(", ", wh.Items.Select(i => $"{i.Place}/{i.Label}={i.Qty}")));
        checks++;
        if (row.ApplyWorkflow && wh.Items.Any(i => i.Qty < 0)) F("Đang ở đâu âm", string.Join(", ", wh.Items.Where(i => i.Qty < 0).Select(i => $"{i.Place}/{i.Label}={i.Qty}")));
        checks++;
        if (wh.Items.Any(i => i.Label.StartsWith("chưa GC (lệch NC"))) F("Đang ở đâu lệch NC", string.Join(", ", wh.Items.Where(i => i.Place == "GC").Select(i => $"{i.Label}={i.Qty}")));
    }
    // Dòng B cuối cùng (sổ theo từng bước) phải trùng sổ hiện tại — kể cả bước "huỷ phần chưa nhận"
    var steps = await led.GetStepsForDetailAsync(1);
    if (steps.Any())
    {
        var last = steps[^1].Row;
        foreach (var G in row.Groups.Values)
        {
            checks++;
            var L = last.G(G.Code);
            if (L.Ton != G.Ton || L.ChuaLam != G.ChuaLam || L.ConGiao != G.ConGiao || L.LoiGiu != G.LoiGiu || L.SuaGiu != G.SuaGiu)
                F("Dòng B cuối ≠ sổ", $"{G.Code}: Tồn {L.Ton}/{G.Ton} Chưa {L.ChuaLam}/{G.ChuaLam} ĐãChưaGiao {L.ConGiao}/{G.ConGiao} Lỗi {L.LoiGiu}/{G.LoiGiu} Sửa {L.SuaGiu}/{G.SuaGiu}");
        }
        checks++;
        if (!last.Balanced) F("Dòng B cuối lệch cân", "");
    }
    // [GN-NG-TRACE 2026-10-28] Đường đi hàng lỗi: cân hàng lỗi ở MỌI bước + khớp ô sổ hiện tại
    {
        var input = (await led.GetInputForDetailAsync(1))!;
        var tr = HandoverNgTrace.Build(input, null, steps);
        checks++;
        if (tr.Problems.Any()) F("Hàng lỗi lệch", string.Join(" | ", tr.Problems.Take(3)) + (tr.Problems.Count > 3 ? $" (+{tr.Problems.Count - 3})" : ""));
        checks++;
        var held = row.Groups.Values.Sum(g => g.LoiGiu + g.SuaGiu);
        if (tr.DangGiu != held) F("Hàng lỗi đang giữ ≠ sổ", $"{tr.DangGiu} ≠ {held}");
        checks++;
        foreach (var g in tr.Groups.Where(g => g.Code != "KHACH"))
            if (g.LoiGiu != row.G(g.Code).LoiGiu || g.SuaGiu != row.G(g.Code).SuaGiu) F("Hàng lỗi từng nơi ≠ sổ", $"{g.Code}: {g.LoiGiu}/{g.SuaGiu} ≠ {row.G(g.Code).LoiGiu}/{row.G(g.Code).SuaGiu}");
        checks++;
        var ever = HandoverNgTrace.EverHadNg(row); var any = tr.Events.Any(e => e.PhatSinh > 0);
        if (ever != any) F("Lọc 'có phát sinh lỗi' ≠ khung lỗi", $"sổ {ever} / khung {any}");
        checks++;
        if (tr.Groups.Where(g => g.Code != "KHACH").Sum(g => g.PhatHien) != tr.PhatSinh || tr.Groups.Sum(g => g.GayRa) != tr.PhatSinh)
            F("Hàng lỗi theo nơi ≠ tổng", $"phát hiện {tr.Groups.Sum(g => g.PhatHien)} · gây ra {tr.Groups.Sum(g => g.GayRa)} · tổng {tr.PhatSinh}");
        if (op % 15 == 0)   // hàm nạp thật (đọc thêm lý do lỗi WTS, người lập / nhận) — đôi lúc, cho nhanh
        {
            checks++;
            var tr2 = (await led.GetNgTraceAsync(1))!;
            if (tr2.Events.Count != tr.Events.Count || tr2.PhatSinh != tr.PhatSinh || !tr2.Problems.SequenceEqual(tr.Problems))
                F("GetNgTraceAsync ≠ Build", $"{tr2.Events.Count}/{tr.Events.Count}");
        }
        Inc("ng.events", tr.Events.Count > 0 ? 1 : 0);
        if (tr.VeSx > 0) Inc("ng.vesx");
        if (tr.DangChuyen > 0) Inc("ng.transit");
    }
    // [VULOI 2026-10-28] Sổ theo từng vụ lỗi: cân từng vụ, khớp ô Tồn hàng lỗi / Tồn hàng sửa, khớp đường đi hàng lỗi, dòng B cuối
    {
        foreach (var cs in row.Cases.Values)
        {
            checks++;
            if (cs.Lech != 0) F("Vụ lỗi lệch cân", $"{cs.No}: sinh {cs.SinhRa} giữ {cs.HeldTotal} chuyển {cs.Transit} xử lý {cs.DaXuLyTotal} huỷ {cs.Huy} khách {cs.TraKhach}");
        }
        foreach (var G in row.Groups.Values)
        {
            checks++;
            var vl = row.Cases.Values.Sum(x => x.HeldAt(G.Code, HandoverCell.Loi));
            var vs = row.Cases.Values.Sum(x => x.HeldAt(G.Code, HandoverCell.Sua));
            if (vl != G.LoiGiu || vs != G.SuaGiu) F("Vụ lỗi ≠ ô sổ", $"{G.Code}: vụ {vl}/{vs} ≠ sổ {G.LoiGiu}/{G.SuaGiu}");
        }
        var trv = HandoverNgTrace.Build((await led.GetInputForDetailAsync(1))!, null, steps);
        checks++;
        var sSinh = row.Cases.Values.Sum(x => x.SinhRa); var sChuyen = row.Cases.Values.Sum(x => x.Transit);
        var sXl = row.Cases.Values.Sum(x => x.DaXuLyTotal);
        if (sSinh != trv.PhatSinh || sChuyen != trv.DangChuyen || sXl != trv.VeSx
            || row.Cases.Values.Sum(x => x.Huy) != row.DaHuy || row.Cases.Values.Sum(x => x.TraKhach) != row.TraKhach)
            F("Vụ lỗi ≠ đường đi hàng lỗi", $"sinh {sSinh}/{trv.PhatSinh} chuyển {sChuyen}/{trv.DangChuyen} xử lý {sXl}/{trv.VeSx} huỷ {row.Cases.Values.Sum(x => x.Huy)}/{row.DaHuy} khách {row.Cases.Values.Sum(x => x.TraKhach)}/{row.TraKhach}");
        if (steps.Any())
        {
            checks++;
            var lastC = steps[^1].Row.Cases;
            string Sig(Dictionary<long, LedgerCase> d) => string.Join(";", d.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value.SinhRa}:{k.Value.Transit}:{string.Join(",", k.Value.Held.OrderBy(h => h.Key).Select(h => h.Key + "=" + h.Value))}"));
            if (Sig(lastC) != Sig(row.Cases)) F("Dòng B cuối ≠ sổ vụ lỗi", Sig(lastC) + " ≠ " + Sig(row.Cases));
        }
        if (VuStrict)
        {
            checks++;
            if (row.Cases.ContainsKey(0)) F("Có hàng lỗi chưa gắn vụ", $"sinh {row.Cases[0].SinhRa} giữ {row.Cases[0].HeldTotal}");
            Inc("vu.count", row.Cases.Count);
            if (row.Cases.Values.Any(x => x.DaXuLyTotal > 0 && (x.Huy > 0 || x.HeldTotal > 0))) Inc("vu.tachnhanh");
        }
    }
    // Trạng thái phiếu khớp số nhận; phiếu tự lập khớp lần nhận gốc
    foreach (var t in db.HandoverTransactions.List.Where(t => !t.IsVoided))
    {
        checks++;
        var rec = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg);
        if (rec > t.QtyIssued) F("Nhận vượt phiếu", $"#{t.HandoverTxId} nhận {rec} > {t.QtyIssued}");
        if (!HandoverPurpose.IsOut(t.Purpose))
        {
            var st = rec <= 0 ? "PENDING" : rec >= t.QtyIssued ? "COMPLETED" : "PARTIAL";
            if (st != t.Status) F("Trạng thái phiếu", $"#{t.HandoverTxId} {t.Status} nhưng đã nhận {rec}/{t.QtyIssued}");
        }
        if (t.SourceReceiveId != null)
        {
            var src = db.HandoverReceives.List.FirstOrDefault(r => r.ReceiveId == t.SourceReceiveId);
            if (src == null || src.IsVoided) F("Phiếu tự lập mồ côi", $"#{t.HandoverTxId} lần nhận gốc {t.SourceReceiveId} không còn");
            else if (src.QtyNg != t.QtyIssued) F("Phiếu tự lập lệch NG", $"#{t.HandoverTxId} {t.QtyIssued} ≠ NG {src.QtyNg}");
        }
    }
    foreach (var r in db.HandoverReceives.List.Where(r => !r.IsVoided && !r.Transaction.IsVoided && r.QtyNg > 0))
    {
        var cell = r.ToCell;
        if (cell == HandoverCell.Loi || r.Transaction.SourceReceiveId != null) continue;
        checks++;
        var n = db.HandoverTransactions.List.Count(t => !t.IsVoided && t.SourceReceiveId == r.ReceiveId);
        if (n != 1) F("Thiếu phiếu trả tự lập", $"lần nhận #{r.ReceiveId} NG {r.QtyNg} có {n} phiếu trả");
    }
}

static int Uid(string g) => g switch { "KHO" => 1, "GC" => 2, "HTSP" => 3, "KCS" => 4, "PKG" => 5, _ => 6 };
static T Pick<T>(Random r, params (T v, int w)[] xs)
{
    var tot = xs.Sum(x => x.w); var k = r.Next(tot);
    foreach (var x in xs) { if (k < x.w) return x.v; k -= x.w; }
    return xs[^1].v;
}


// [GN-WHY] Kiểm gợi ý gỡ: (1) gợi ý có trong lời báo; (2) làm đúng theo gợi ý (qua hàm thật / mô phỏng trang WTS) thì thao tác gốc lưu được
async Task WhyCheck(AppDbContext db, HandoverService svc, HandoverLedgerService led, Action<LedgerInput> opFn, HashSet<string> opDocs,
                    Func<Task<bool>> retry, Func<Task<bool>>? pre, Random rnd, int po, int opIdx, string desc, string? svcMsg)
{
    var input = (await led.GetInputForDetailAsync(1))!;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var plan = HandoverWhy.Plan(input, opFn, opDocs);
    sw.Stop(); whyMs.Add(sw.Elapsed.TotalMilliseconds); whyComputes.Add(plan.Computes);
    Inc(plan.Complete ? "why.complete" : "why.incomplete");
    if (!plan.Complete)
    {
        if (whyMiss.Count < 8) whyMiss.Add($"PO{po} op{opIdx} {desc} ({plan.Steps.Count} bước, {plan.Computes} lần tính)");
        if (Environment.GetEnvironmentVariable("WHYDBG") == "1")
        {
            var b = HandoverLedgerService.Compute(input); var a2 = (await led.GetInputForDetailAsync(1))!; opFn(a2);
            Console.WriteLine("BAD KEYS: " + string.Join(", ", HandoverLedgerService.NewNegativeKeys(b, HandoverLedgerService.Compute(a2))));
            foreach (var t in db.HandoverTransactions.List) Console.WriteLine($"   tx#{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} {t.Purpose} {t.FromCell}->{t.ToCell} q={t.QtyIssued} st={t.Status} void={t.IsVoided} fnc={t.FromNC} tnc={t.ToNC} src={t.SourceReceiveId} rec={string.Join(",", t.Receives.Select(r => $"#{r.ReceiveId}:{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}");
            foreach (var w in db.WtsProductionLogs.List) Console.WriteLine($"   wts#{w.WtsLogId} {w.ProcessGroup} {w.NC}{w.WtsCode} {w.QtyOk}/{w.QtyNg} void={w.IsVoided}");
        }
        return;
    }
    if (svcMsg != null && plan.Steps.Count > 0 && !svcMsg.Contains("nên kiểm tra theo các bước sau"))
        fails.Add($"[GN-WHY thiếu lời gợi ý] PO{po} op{opIdx} {desc}");
    // Làm theo gợi ý
    foreach (var st in plan.Steps)
    {
        bool okStep;
        if (st.Kind == "WTS") okStep = await WtsVoid(db, led, st.Id);
        else if (st.Kind == "NHAN") { var r = await svc.VoidReceiveAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }
        else if (st.Kind == "BAOLOI") { var r = await svc.VoidNgReportAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }   // [GN-NG-AFTER]
        else { var r = await svc.VoidIssueAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }
        if (!okStep) { fails.Add($"[GN-WHY bước gợi ý bị chặn] PO{po} op{opIdx} {desc} → {st.Kind} #{st.Id}: {lastErr}"); return; }
        Inc("why.step");
    }
    if (pre != null && !await pre()) { fails.Add($"[GN-WHY huỷ lần nhận của chính phiếu bị chặn] PO{po} op{opIdx} {desc}: {lastErr}"); return; }
    if (!await retry()) { fails.Add($"[GN-WHY làm theo gợi ý vẫn không lưu được] PO{po} op{opIdx} {desc}"); return; }
    Inc("why.retry.ok");
    await CheckAll(db, led, po, opIdx, desc + " (sau khi làm theo gợi ý)");
}

// Mô phỏng xoá dòng WTS đúng như trang WTS (K5)
async Task<bool> WtsVoid(AppDbContext db, HandoverLedgerService led, long wid)
{
    var w = db.WtsProductionLogs.List.FirstOrDefault(x => x.WtsLogId == wid && !x.IsVoided);
    if (w == null) { lastErr = "WTS không còn"; return false; }
    var before = (await led.GetLedgerForDetailAsync(1))!;
    var after = (await led.GetLedgerForDetailAsync(1, new List<long> { wid }))!;
    var bad = HandoverLedgerService.NewNegatives(before, after);
    if (bad.Any()) { lastErr = string.Join("; ", bad); return false; }
    w.IsVoided = true;
    return true;
}
