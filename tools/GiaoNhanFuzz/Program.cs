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
// [KIEM-NHAN 2026-11-01] cuối mỗi tiến trình in giây CPU đã dùng — lệnh nang so CPU / thời gian thực để biết máy có bận việc khác không
AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Console.WriteLine($"CPU tiến trình: {System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"); } catch { } };

if (args.Length > 0 && args[0] == "scen") { await Scen.Run(); return 0; }
if (args.Length > 0 && args[0] == "kh") { return await KhScen.Run(); }
if (args.Length > 0 && args[0] == "bench") { await Bench.Run(); return 0; }
if (args.Length > 0 && args[0] == "sua") { return await SuaScen.Run(); }   // [GC-SUA-TAI-CHO] Kết quả sửa + KCS xác nhận
if (args.Length > 0 && args[0] == "form") { return await FormScen.Run(); }
if (args.Length > 0 && args[0] == "why") { return WhyScen.Run(); }   // [H10-KET-WHY] gợi ý gỡ K5 trên sổ các ca kẹt WHYALL
if (args.Length > 0 && args[0] == "taro") { return await TaroScen.Run(); }   // [TARO-NC-MA][TARO-3T-GOP] Taro mã dùng chung nhiều NC + mã gộp 3T   // [GN-FORM1] form Giao / xử lý hàng (1 form 4 bước)
if (args.Length > 0 && args[0] == "loi") { return await LoiScen.Run(); }   // [GN-NG-TRACE] kịch bản hàng lỗi qua nhiều nơi
if (args.Length > 0 && args[0] == "vu") { return await VuScen.Run(); }   // [VULOI] kịch bản vụ lỗi: A/B/C/D + K5 theo vụ
if (args.Length > 0 && args[0] == "chuoi") { return await ChuoiScen.Run(); }   // [CHUOI] kịch bản đi đủ chuỗi Part Master → KH → snapshot → Kho → giao nhận + WTS bằng hàm thật
if (args.Length > 0 && args[0] == "wts") { return await WtsScen.Run(); }   // [WTS-SVC] kịch bản lưu / sửa / xoá WTS bằng service thật, chữ báo y cũ
if (args.Length > 0 && args[0] == "quyen") { return await QuyenScen.Run(); }   // [RS-C1][RS-C2][RS-E8][RS-E15][RS-E16] quyền máy chủ + danh mục + quy trình trống + khoá cấp số KH
if (args.Length > 0 && args[0] == "iso") { return await IsoScen.Run(); }
if (args.Length > 0 && args[0] == "nghi") { return await NghiScen.Run(); }   // [ML17-XN][CA-NGHI-HIST] làm thông nghỉ + giờ nghỉ chép lúc lưu + ca qua đêm
if (args.Length > 0 && args[0] == "quet") { return await QuetScen.Run(); }   // [RA-SOCHUNG] tự kiểm công cụ quét DB tools/QuetSo (chỉ đọc)   // [ISO-FIX] không giao dịch Serializable + mọi lần ghi số sau khoá MES_PO_
if (args.Length > 0 && args[0] == "auth") { return await AuthScen.Run(); }   // [RS-C3][RS-C4][TOOLS-PUSH] đăng nhập / phiên
if (args.Length > 0 && args[0] == "viec") { return await ViecScen.Run(); }   // [ML16-ACT][TOOLS-PUSH] Việc không ra sản phẩm
if (args.Length > 0 && args[0] == "std") { return await StdScen.Run(); }   // [STD-UPDATE][STD-UPD][STD-5] Update TG chuẩn Part Master bằng StdUpdateService thật
if (args.Length > 0 && args[0] == "danhmuc") { return DanhMuc.Run(); }   // [KIEM-TONG] đối chiếu tools/DanhMucKiem.txt với tag trong code + ca kiểm
if (args.Length > 2 && args[0] == "whyreplay") { return WhyReplay.Run(args[1], long.Parse(args[2])); }   // [H10-KET-WHY 07/11] tái hiện ca [H10 kẹt] từ JSON
if (args.Length > 0 && args[0] == "nang") { return await Nang.Run(args); }   // [GN-NG-TRACE] chạy nặng: nhiều cấu hình song song
var poCount = args.Length > 0 ? int.Parse(args[0]) : 200;
var opsPerPo = args.Length > 1 ? int.Parse(args[1]) : 120;
var seed0 = args.Length > 2 ? int.Parse(args[2]) : 1;
var stats = new Dictionary<string, int>();
void Inc(string k, int n = 1) { stats[k] = stats.TryGetValue(k, out var v) ? v + n : n; }
var fails = new List<string>();
int checks = 0;
// [TARO-LAMHO-NC 2026-11-07] Đáp án trần trả hàng làm hộ về Gia công (từ ô sổ, theo luật Hùng 10:18 + rà độc lập gói 6 vòng 1–2):
//   tốt = Tồn hàng giao theo NC − chiếc làm hộ bị lỗi (vụ ô LAMHO); Workflow ON + PO có đoạn Taro: nhóm khác HTSP → 0 nếu NC sau đoạn Taro;
//   HTSP → NC sau đoạn R: ≤ (nhận − trả − NG) của R, R có Taro: ≤ OK bộ − đã trả; NC khác: ≤ hàng không thuộc đoạn nào
static decimal ExpLamHoRet(LedgerRow row, string from, string? tnc)
{
    var good = row.G(from).LamHoGiu - row.Cases.Values.Sum(x => x.HeldAt(from, HandoverCell.LamHo));
    if (row.ApplyWorkflow && row.LamHoRuns.Any(x => x.HasTaro))
    {
        var lr = row.LamHoRuns.FirstOrDefault(x => tnc != null && string.Equals(x.NextNc, tnc, StringComparison.OrdinalIgnoreCase));
        if (from != "HTSP") return lr is { HasTaro: true } ? 0 : Math.Max(0, good);
        var held = row.LamHoRuns.Sum(x => Math.Max(0, x.In - x.Ret - x.Ng));
        if (lr == null) return Math.Max(0, Math.Min(good, good - held));
        var cap = Math.Min(good, lr.In - lr.Ret - lr.Ng);
        if (lr.HasTaro) cap = Math.Min(cap, lr.Done - lr.Ret);
        return Math.Max(0, cap);
    }
    return Math.Max(0, good);
}
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
// [CHUOI 2026-10-29] CHUOI=1: PO dựng bằng HÀM THẬT đi đủ chuỗi Part Master (ngẫu nhiên) → KhPlanService.CreateAsync + CopyRouteSnapshotAsync
//   → KhoVatLieuService.SaveKhoInputAsync, mã WTS đọc từ snapshot; thêm thao tác `pmedit` (sửa Part Master giữa chừng → PO cũ phải y nguyên)
//   và 1 PO "hàng xóm" cùng phiếu KH có sẵn chứng từ → sổ PO đó không được đổi. Không đặt CHUOI thì y như cũ (cùng seed ra cùng kết quả).
bool chuoi = Environment.GetEnvironmentVariable("CHUOI") == "1";
// [H10 2026-10-30] WHYALL=1: sau mỗi thao tác, thử tính đường gỡ [GN-WHY] cho MỌI lần nhận còn hiệu lực (như người dùng bấm Huỷ lần nhận) —
//   chỉ tính trên bản sao số liệu, không ghi gì, không rút số ngẫu nhiên (cùng seed vẫn ra cùng chuỗi thao tác). Gặp "chưa lần ra" (bị K5 chặn mà
//   không có đường gỡ) thì ghi lỗi [H10 kẹt] kèm sổ chứng từ để tái hiện vòng khoá.
bool whyAll = Environment.GetEnvironmentVariable("WHYALL") == "1";
var h10Seen = new HashSet<string>();
Dictionary<long, long>? editPrevCur = null; long lastRevertNew = 0;   // [H10-KET-WHY-3] số dòng mới của lần sửa về gần nhất   // [H10-KET-WHY-3] editPrev của PO đang chạy (bước gợi ý WTSV sửa dòng → ghi chuỗi sửa như thao tác wtsedit)
int h10MaxSteps = 0;
var swProg = System.Diagnostics.Stopwatch.StartNew(); double lastProg = 0; // [FUZZ-UX] đồng hồ báo tiến độ
for (int po = startPo; po < startPo + poCount; po++)
{
    var rnd = new Random(seed0 * 100000 + po);
    var db = new AppDbContext();
    var editPrev = new Dictionary<long, long>(); editPrevCur = editPrev;   // [H10-KET-WHY-3] WtsRevertStep ghi tiếp chuỗi sửa · [WTS-SUA-PHAN] bộ kiểm tự ghi dòng sửa → dòng nó thay (KHÔNG qua EditOrigins — đáp án độc lập, rà độc lập lần 3 Vừa)
    WtsSvcHelp.Reset();   // [WTS-SVC] khung giờ WTS bắt đầu lại từng PO
    double exclP = Environment.GetEnvironmentVariable("EXCL") is string exv ? double.Parse(exv) : 0.3;
    bool excl;
    string[] ncs; Dictionary<string, (string pg, string[] codes)> codeOf;
    int pmId = 0; string snapSig0 = ""; string sigNb = ""; int nbId = 0; int nbDocs = 0;   // [CHUOI] Part Master, chữ ký snapshot PO 1, PO hàng xóm
    if (chuoi)
    {
        excl = rnd.NextDouble() < exclP;
        ChuoiHelp.SeedBase(db, exclH04: true);
        var shape = ChuoiHelp.Shape.Random(rnd, excl);
        pmId = await ChuoiHelp.BuildPartAsync(db, "P" + po, shape);
        var pic = ChuoiHelp.Picture(db, pmId);
        var twoPo = rnd.NextDouble() < 0.3;
        var q1 = rnd.Next(8, 40);
        var ids = await ChuoiHelp.CreateKhAsync(db, pmId, "P" + po, twoPo ? new decimal[] { q1, rnd.Next(5, 30) } : new decimal[] { q1 }, "PO" + po);
        if (ids[0] != 1) { fails.Add($"[CHUOI] PO{po} dòng KH đầu có Id {ids[0]} ≠ 1"); continue; }
        var dchk = ChuoiHelp.Diff(db, 1, pic, pmId);
        if (dchk != null) fails.Add($"[Snapshot ≠ Part Master] PO{po}: {dchk}");
        var kr = await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(1, DateTime.Now, q1, "OVL", "A1", null, null, null, "nhan phoi", 1);
        if (!kr.Success) { fails.Add($"[CHUOI] PO{po} Kho nhận phôi lỗi: {kr.Error}"); continue; }
        if (twoPo)
        {   // PO hàng xóm: nhận phôi + giao Kho→GC + nhận có NG + 1 dòng WTS, rồi chụp chữ ký sổ — suốt lượt không được đổi
            nbId = ids[1]; var q2 = (int)db.KhPlanDetails.List.First(d => d.KhPlanDetailId == nbId).Quantity;
            await new KhoVatLieuService(db, new UserService(null)).SaveKhoInputAsync(nbId, DateTime.Now, q2, "OVL", "A2", null, null, null, "nhan phoi", 1);
            var nsvc = new HandoverService(db);
            var nbRow0 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(nbId))!;   // [N3-TONC] NC thường đầu tiên (NC đầu có thể là máy loại trừ)
            var nc0 = nbRow0.G("GC").Steps.First(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)).Code;   // [N3-FIRST] @VAO không phải NC nhận
            var gi = await nsvc.IssueByCellAsync(nbId, "KHO", "GC", q2, 1, "MOI", false, "MOI", null, nc0);
            if (!gi.Ok) fails.Add($"[CHUOI] PO{po} PO hàng xóm giao Kho → GC lỗi: {gi.Error}");
            if (gi.Ok) await nsvc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, q2 - 1, 1, "loi", 2, null, null, null, 2);
            db.WtsProductionLogs.Add(new WtsProductionLog { KhPlanDetailId = nbId, ProcessGroup = "GC", NC = nc0, WorkerId = 2, QtyDone = 2, QtyOk = 2, StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now });
            await db.SaveChangesAsync();
            sigNb = ChuoiHelp.LedgerSig((await new HandoverLedgerService(db).GetLedgerForDetailAsync(nbId))!);
            nbDocs = db.HandoverTransactions.List.Count(t => t.KhPlanDetailId == nbId) + db.HandoverReceives.List.Count(r => r.Transaction.KhPlanDetailId == nbId) + db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == nbId) + db.NgCases.List.Count(x => x.KhPlanDetailId == nbId) + db.HandoverNgReports.List.Count(b => b.KhPlanDetailId == nbId);
        }
        if (Environment.GetEnvironmentVariable("TARO") == "1" && shape.ExclAt < 0 && shape.Taro > 0)
        {   // [TARO-NC-MA][TARO-3T-GOP 2026-11-06] lượt chuoi_taro: đưa sẵn 1 phần hàng qua Gia công vào HTSP (hàm thật) để thao tác WTS Taro có hàng
            var tsvc = new HandoverService(db);
            var r0 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            var gcN = r0.G("GC").Steps.Where(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)).Select(s => s.Code).ToList();
            var feed = rnd.Next(Math.Max(1, q1 / 2), q1 + 1);
            bool fok = gcN.Count > 0 && (await tsvc.IssueByCellAsync(1, "KHO", "GC", feed, 1, "MOI", false, "MOI", null, gcN[0])).Ok
                       && (await tsvc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, feed, 0, null, 1)).Ok;
            foreach (var n in gcN) if (fok) fok = (await WtsSvcHelp.SaveAsync(db, 1, "GC", n, null, feed, 0, 1)).Ok;
            fok = fok && (await tsvc.IssueByCellAsync(1, "GC", "HTSP", feed, 1, "MOI", false, "MOI")).Ok
                      && (await tsvc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, feed, 0, null, 1)).Ok;
            if (!fok) fails.Add($"[CHUOI TARO] PO{po} đưa hàng vào HTSP lỗi");
            else Inc("taro.feed");
        }
        if (Environment.GetEnvironmentVariable("LAMHO") == "1" && shape.TaroLamHo)
        {   // [TARO-LAMHO-NC 2026-11-07] lượt chuoi_lamho: đưa sẵn 1 phần hàng qua các NC trước đoạn máy loại trừ rồi giao theo NC cho HTSP (hàm thật) để có Taro làm hộ
            var lsvc = new HandoverService(db);
            var r0 = (await new HandoverLedgerService(db).GetLedgerForDetailAsync(1))!;
            var lr = r0.LamHoRuns.FirstOrDefault(x => x.HasTaro);
            var gcN = r0.G("GC").Steps.Where(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)).Select(s => s.Code).ToList();
            var feed = rnd.Next(Math.Max(1, q1 / 2), q1 + 1);
            bool fok = lr != null && gcN.Count > 0 && (await lsvc.IssueByCellAsync(1, "KHO", "GC", feed, 1, "MOI", false, "MOI", null, gcN[0])).Ok
                       && (await lsvc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, feed, 0, null, 1)).Ok;
            foreach (var n in gcN.TakeWhile(n => n != lr?.NextNc)) if (fok) fok = (await WtsSvcHelp.SaveAsync(db, 1, "GC", n, null, feed, 0, 1)).Ok;
            fok = fok && (await lsvc.IssueSpecialAsync(1, "GC", "HTSP", feed, 1, HandoverPurpose.HelpSend, lr!.FromNc, null, "lam ho taro")).Ok
                      && (await lsvc.ReceiveAsync(db.HandoverTransactions.List.Last().HandoverTxId, feed, 0, null, 1)).Ok;
            if (!fok) fails.Add($"[CHUOI LAMHO] PO{po} đưa hàng làm hộ vào HTSP lỗi");
            else Inc("lamho.feed");
        }
        snapSig0 = ChuoiHelp.SnapPicture(db, 1).Sig;
        (ncs, codeOf) = ChuoiHelp.CodesFromSnapshot(db, 1);
        if (Environment.GetEnvironmentVariable("TRACEPO") == po.ToString()) Console.WriteLine($"   [TRACE] PO{po} SL={q1} Part Master:\n   " + snapSig0.Replace("\n", "\n   "));
        Inc("chuoi.po"); if (twoPo) Inc("chuoi.2po"); if (shape.ExclAt >= 0) Inc("chuoi.excl"); if (shape.ExclAt == 0 && shape.Gc >= 2) Inc("chuoi.exclfirst"); if (shape.DuPhong) Inc("chuoi.duphong"); if (shape.DongDang) Inc("chuoi.dongdang");
    }
    else
    {
    // ── Dữ liệu gốc 1 PO ──
    var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN" };
    for (int u = 1; u <= 6; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
    var cust = new Customer { CustomerId = 1, CustomerName = "KH" };
    db.Customers.List.Add(cust);
    var plan = new KhPlan { KhPlanId = 1, PlanNo = "KH-TEST", Customer = cust, CustomerId = 1, ApplyWorkflow = true };
    db.KhPlans.List.Add(plan);
    var qty = rnd.Next(8, 40);
    var det = new KhPlanDetail { KhPlanDetailId = 1, KhPlanId = 1, KhPlan = plan, PartNo = "P", PurchaseOrder = "PO" + po, Quantity = qty };
    db.KhPlanDetails.List.Add(det);
    db.KhoVatLieus.List.Add(new KhoVatLieu { KhoVatLieuId = 1, PlanDetailId = 1, IsActive = true, SoLuongThucNhan = qty, NgayNhanPhoi = DateTime.Now, SoPO = "PO", PartNo = "P" });
    excl = rnd.NextDouble() < exclP;   // (giữ đúng thứ tự rút số ngẫu nhiên như bản cũ)
    ncs = new[] { "NC1", "NC2", "NC3" };
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
    codeOf = new Dictionary<string, (string pg, string[] codes)>
    {
        ["GC"] = ("GC", ncs), ["HTSP"] = ("", new[] { "TARO|T1", "BAVIA|B1", "WASHING|R1" }),
        ["KCS"] = ("KCS", new[] { "K1", "K2" }), ["PKG"] = ("PKG", new[] { "P1" }),
    };
    }

    var svc = new HandoverService(db);
    var led = new HandoverLedgerService(db);
    var kho = new KhoVatLieuService(db, new UserService(null));

    var kq = int.TryParse(Environment.GetEnvironmentVariable("KQSUA"), out var kqw) ? kqw : 1;   // [GC-SUA-TAI-CHO] hệ số trọng số 4 thao tác Kết quả sửa
    for (int op = 0; op < opsPerPo; op++)
    {
        var row = (await led.GetLedgerForDetailAsync(1))!;
        var kind = Pick(rnd, ("issue", 30), ("recv", 30), ("wts", 30), ("wtsvoid", 4), ("vrecv", 6), ("vissue", 6), ("close", 5), ("phoi", int.TryParse(Environment.GetEnvironmentVariable("PHOI"), out var pw) ? pw : smart ? 0 : 6),
            ("ngrep", int.TryParse(Environment.GetEnvironmentVariable("NGREP"), out var nw) ? nw : 10), ("vngrep", 4), ("wtsedit", int.TryParse(Environment.GetEnvironmentVariable("WTSEDIT"), out var we) ? we : 5),
            ("pmedit", chuoi ? 5 : 0),
            // [GC-SUA-TAI-CHO] Kết quả sửa + KCS xác nhận + huỷ. KQSUA=0 tắt (chạy lại seed cũ trước gói này ra đúng dãy thao tác cũ).
            ("kqsua", kq * 8), ("xnsua", kq * 7), ("vkqsua", kq * 3), ("vxnsua", kq * 3),
            ("form", int.TryParse(Environment.GetEnvironmentVariable("FORM"), out var fw) ? fw : 12),
            // [LAMHO-NHO-SUA][LAMHO-NHAN-NG 2026-11-08] Gói 11: thử gửi / nhận đổi ô vụ làm hộ vào ô Hàng sửa / ô hàng đạt nhóm khác Gia công (phải chặn).
            //   Chỉ lượt LAMHO=1 (trọng số 0 ở lượt khác → dãy thao tác các seed cũ không đổi). Nguồn: CLAUDE (Gói 10: fuzz chưa đi trúng đường này — chỉ T22 phủ).
            ("lhsua", Environment.GetEnvironmentVariable("LAMHO") == "1" ? 20 : 0));   // [GN-FORM1] form Giao / xử lý hàng (FORM=0 tắt)   // [CHUOI] sửa Part Master giữa chừng (chỉ khi CHUOI=1; trọng số 0 thì không bao giờ rút trúng)   // [VULOI] + sửa dòng WTS (giữ vụ)   // [GN-NG-AFTER] [GN-NG-TRACE] LOI=1: mặc định không sửa phôi
        string desc = kind;
        bool ok = false;
        try
        {
            switch (kind)
            {
                case "issue":
                {
                    var from = groups[rnd.Next(groups.Length)];
                    // [N3-FIRST 2026-11-04] PO có dòng "Nhận vào (chưa qua NC)": đường duy nhất qua máy loại trừ đầu chuỗi là giao theo NC → trả về,
                    //   nên tăng tỉ lệ phiếu đặc biệt / giao theo NC / trả về cho các PO này (PO khác giữ y tỉ lệ cũ)
                    bool vaoPo = row.G("GC").Steps.Any(x => HandoverLedgerService.IsVao(x.Code));
                    var lhRun = row.LamHoRuns.FirstOrDefault(x => x.HasTaro);   // [TARO-LAMHO-NC] PO có Taro làm hộ (chỉ lượt LAMHO=1) → nhiều phiếu giao theo NC / trả về hơn
                    var special = rnd.NextDouble() < (vaoPo || lhRun != null ? 0.35 : 0.12);
                    if (special)
                    {
                        var p = vaoPo || lhRun != null ? Pick(rnd, (HandoverPurpose.Scrap, 2), (HandoverPurpose.CustReturn, 1), (HandoverPurpose.HelpSend, 6), (HandoverPurpose.HelpReturn, 5))
                                      : Pick(rnd, (HandoverPurpose.Scrap, 3), (HandoverPurpose.CustReturn, 2), (HandoverPurpose.HelpSend, 3), (HandoverPurpose.HelpReturn, 3));
                        var to = p == HandoverPurpose.HelpReturn ? "GC" : groups[rnd.Next(groups.Length)];
                        if (p == HandoverPurpose.HelpSend) from = "GC";
                        var fnc = p == HandoverPurpose.HelpSend ? ncs[rnd.Next(ncs.Length)] : null;
                        // [N3-FIRST 2026-11-04] NC đầu là máy loại trừ: giao theo NC từ dòng "Nhận vào (chưa qua NC)" (như trang: ô NC nguồn có dòng này)
                        bool hasVao = row.G("GC").Steps.Any(x => HandoverLedgerService.IsVao(x.Code));
                        if (p == HandoverPurpose.HelpSend && hasVao && rnd.NextDouble() < 0.8) fnc = HandoverLedgerService.VaoCode;
                        var ncsIn = row.G("GC").Steps.Where(x => !x.IsExcluded && !HandoverLedgerService.IsVao(x.Code)).Select(x => x.Code).ToArray();   // [N3-TONC] NC nhận vào hợp lệ (như trang) [N3-FIRST] trừ @VAO
                        var tnc = p == HandoverPurpose.HelpReturn ? (ncsIn.Length > 0 ? ncsIn[rnd.Next(ncsIn.Length)] : ncs[rnd.Next(ncs.Length)]) : null;
                        if (lhRun != null && rnd.NextDouble() < 0.8)
                        {   // [TARO-LAMHO-NC] giao theo NC từ NC trước đoạn / trả về NC sau đoạn (đường của khách)
                            if (p == HandoverPurpose.HelpSend) fnc = lhRun.FromNc;
                            if (p == HandoverPurpose.HelpReturn) { tnc = lhRun.NextNc; if (rnd.NextDouble() < 0.7) from = "HTSP"; }
                        }
                        if (p == HandoverPurpose.HelpReturn && hasVao && rnd.NextDouble() < 0.08)
                        {   // [N3-FIRST] trả về vào "@VAO" phải bị chặn (trang không cho chọn; máy chủ kiểm lại)
                            var nb = db.HandoverTransactions.List.Count;
                            var bad = await svc.IssueSpecialAsync(1, from, "GC", 1, Uid(from), p, null, HandoverLedgerService.VaoCode, "ghi chu");
                            Inc("vao.tonc");
                            if (bad.Ok || db.HandoverTransactions.List.Count != nb) fails.Add($"[N3-FIRST trả về vào @VAO không bị chặn] PO{po} op{op} {from}");
                        }
                        // [VULOI] Huỷ / Trả khách: chọn 1 vụ đang giữ ở ô Hàng lỗi bên giao (trần theo vụ)
                        long? vu = null;
                        if (p is HandoverPurpose.Scrap or HandoverPurpose.CustReturn)
                        {
                            if (smart) { var fr = groups.Where(x => row.CasesHeldAt(x, HandoverCell.Loi).Any()).ToList(); if (fr.Any()) from = fr[rnd.Next(fr.Count)]; }
                            var held = row.CasesHeldAt(from, HandoverCell.Loi);
                            if (!held.Any()) { Inc("skip.issue"); continue; }
                            var pk = held[rnd.Next(held.Count)]; vu = pk.Id == 0 ? null : pk.Id;
                        }
                        var (max, _) = HandoverLedgerService.IssueCapCase(row, from, p, fnc, p is HandoverPurpose.Scrap or HandoverPurpose.CustReturn ? HandoverCell.Loi : null, vu, tnc);   // [TARO-LAMHO-NC] + NC nhận (câu 1)
                        // [TARO-LAMHO-NC] đáp án độc lập trần trả hàng làm hộ: Tồn hàng giao theo NC − chiếc làm hộ bị lỗi (vụ ô LAMHO), Workflow ON + NC sau đoạn Taro: ≤ OK bộ − đã trả
                        if (p == HandoverPurpose.HelpReturn)
                        {
                            var expM = ExpLamHoRet(row, from, tnc);
                            checks++;
                            if (Math.Max(0, expM) != max) fails.Add($"[TARO-LAMHO-NC trần trả về ≠ đáp án] PO{po} op{op} {from}→GC {tnc}: trần {max} · tính tay {Math.Max(0, expM)}");
                        }
                        if (max <= 0) { Inc("skip.issue"); continue; }
                        var q = rnd.NextDouble() < 0.06 ? (int)max + 1 : rnd.Next(1, (int)Math.Max(1, max) + 1);
                        desc = $"special {p} {from}->{to} {q} max={max} vu={vu} fnc={fnc} tnc={tnc}";
                        var r = await svc.IssueSpecialAsync(1, from, to, q, Uid(from), p, fnc, tnc, "ghi chu", null, vu);
                        ok = r.Ok; if (!ok) Inc("rej.issue");
                        if (ok && HandoverLedgerService.IsVao(fnc)) Inc("vao.helpsend.ok");   // [N3-FIRST]
                        if (ok && vaoPo && p == HandoverPurpose.HelpReturn) Inc("vao.helpreturn.ok");
                        if (lhRun != null) { Inc("lamho.issue"); if (ok && p == HandoverPurpose.HelpReturn && from == "HTSP") Inc("lamho.ret.ok"); if (!ok && p == HandoverPurpose.HelpReturn && from == "HTSP") Inc("lamho.ret.rej"); if (ok && p == HandoverPurpose.HelpSend) Inc("lamho.send.ok"); }   // [TARO-LAMHO-NC]
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
                                fnc = cands.Any() ? cands[rnd.Next(cands.Count)] : ncs[rnd.Next(ncs.Length)];
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
                        if (row.ApplyWorkflow && takes && to != "GC" && toCell == HandoverCell.Sua && vu is long lhv && row.Cases.TryGetValue(lhv, out var lhvC) && lhvC.IsLamHo)
                        {   // [LAMHO-NHO-SUA 2026-11-08] đáp án: vụ làm hộ vào ô Hàng sửa nhóm khác Gia công → phải chặn
                            Inc(ok ? "lamho.nhosua.LOT" : "lamho.nhosua.chan");
                            if (ok) fails.Add($"[LAMHO-NHO-SUA gửi vụ làm hộ vào ô Hàng sửa nhóm khác không bị chặn] PO{po} op{op} {desc}");
                        }
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
                    var pend = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Status != "COMPLETED" && !HandoverPurpose.IsOut(t.Purpose)).ToList();   // [CHUOI] chỉ chứng từ của PO 1 (PO hàng xóm không đụng)
                    if (!pend.Any()) { Inc("skip.recv"); continue; }
                    var t = pend[rnd.Next(pend.Count)];
                    var rem = t.QtyIssued - t.Receives.Where(x => !x.IsVoided).Sum(x => x.QtyOk + x.QtyNg);
                    var q = rnd.NextDouble() < 0.6 || rem <= 1 ? rem : rnd.Next(1, (int)rem);
                    var cell = t.SourceReceiveId != null ? HandoverCell.Loi : (HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell : null);
                    // [VULOI] 15%: bên nhận đổi ô (thử nhận vào Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý / Hàng mới khác ô chỉ định)
                    string? pickCell = null;
                    var desig0 = cell;   // [NHAN-DOI-O-NC] ô bên giao chỉ định
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
                    // [NHAN-DOI-O-NC 2026-11-06] đổi ô vào Gia công (Hàng lỗi đã xử lý / Hàng mới) với phiếu không có NC nhận: NC của lần nhận theo vụ (K2).
                    //   Đáp án độc lập FormHelp.ExpectNextNc; 20% chọn NC trước + lý do; 10% thử NC sau (vụ có mã) → phải bị chặn, không ghi.
                    var (ncsR, vaoR) = FormHelp.Chain(db, 1);
                    //   Chỉ khi bên nhận ĐỔI từ ô chỉ định Hàng lỗi / Hàng sửa sang ô đạt (rà độc lập 06/11 C1: phiếu chỉ định sẵn ô đạt không NC = bên giao
                    //   đã chọn "như hàng mới" — không điền đè). Đáp án cũ (seed 26) xét cả phiếu chỉ định ô đạt — đổi theo luật đã sửa.
                    bool ncApp = pickCell != null && desig0 is HandoverCell.Loi or HandoverCell.Sua
                                 && t.SourceReceiveId == null && t.ToGroupCode == "GC" && cell is HandoverCell.Xl or HandoverCell.Moi && string.IsNullOrEmpty(t.ToNC)
                                 && t.Purpose is not (HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn) && ncsR.Count > 0;
                    long caseK = carryR ? (t.NgCaseId ?? 0) : 0;
                    string? defR = ncApp ? FormHelp.ExpectNextNc(db, 1, caseK, "GC") : null, pickNc = null, ncWhyR = null;
                    if (ncApp)
                    {
                        int diR = defR == null ? (vaoR ? -1 : 0) : ncsR.FindIndex(x => x.Equals(defR, StringComparison.OrdinalIgnoreCase));
                        var earR = ncsR.Where((c, i) => i < diR).ToList();
                        var latR = ncsR.Where((c, i) => i > diR).ToList();
                        if (caseK != 0 && latR.Any() && rnd.NextDouble() < 0.1)
                        {
                            var nRc = db.HandoverReceives.List.Count;
                            var bl = await svc.ReceiveAsync(t.HandoverTxId, cell == HandoverCell.Loi ? 0 : okq, cell == HandoverCell.Sua ? 0 : ng, null, Uid(t.ToGroupCode),
                                null, pickCell, pickCell != null ? "doi o" : null, rcat, latR[rnd.Next(latR.Count)], "thu nc sau");
                            Inc("recv.nc.sau");
                            if (bl.Ok || db.HandoverReceives.List.Count != nRc) fails.Add($"[Nhận đổi ô vào NC sau lọt (K2)] PO{po} op{op} #{t.HandoverTxId} vụ {caseK}");
                        }
                        if (earR.Any() && rnd.NextDouble() < 0.2) { pickNc = earR[rnd.Next(earR.Count)]; ncWhyR = "lam lai nc truoc"; Inc("recv.nc.truoc"); }
                    }
                    desc = $"recv #{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} cell={cell}{(pickCell != null ? "(đổi)" : "")} ok={okq} ng={ng} rem={rem} cat={rcat} carry={carryR}" + (ncApp ? $" nc={pickNc ?? defR ?? "mới"}" : "");
                    var r = await svc.ReceiveAsync(t.HandoverTxId, cell == HandoverCell.Loi ? 0 : okq, cell == HandoverCell.Sua ? 0 : ng, null, Uid(t.ToGroupCode),
                        null, pickCell, pickCell != null ? "doi o" : null, rcat, pickNc, ncWhyR);
                    ok = r.Ok; if (!ok) { Inc("rej.recv"); desc += " ERR " + r.Error; }
                    if (row.ApplyWorkflow && carryR && t.ToGroupCode != "GC" && cell == HandoverCell.Sua && t.NgCaseId is long rlv && row.Cases.TryGetValue(rlv, out var rlvC) && rlvC.IsLamHo)
                    {   // [LAMHO-NHO-SUA 2026-11-08] đáp án: nhận vụ làm hộ vào ô Hàng sửa nhóm khác Gia công → phải chặn
                        Inc(ok ? "lamho.nhosua.recv.LOT" : "lamho.nhosua.recv.chan");
                        if (ok) fails.Add($"[LAMHO-NHO-SUA nhận vụ làm hộ vào ô Hàng sửa nhóm khác không bị chặn] PO{po} op{op} {desc}");
                    }
                    if (ok && needCat && rcat == null) fails.Add($"[Nhận sinh vụ không có loại lỗi] PO{po} op{op} {desc}");
                    if (!ok && !needCat && r.Error == NgCaseService.MsgPickCategory) fails.Add($"[Nhận phiếu mang vụ bị bắt chọn lỗi] PO{po} op{op} {desc}");
                    if (ok)
                    {
                        var rr0 = db.HandoverReceives.List.Last();
                        if (needCat && (rr0.NgCaseId == null || db.NgCases.List.First(x => x.NgCaseId == rr0.NgCaseId).NgCategoryId != rcat)) fails.Add($"[Lần nhận sinh vụ sai] PO{po} op{op} {desc}");
                        if (!needCat && rr0.NgCaseId != t.NgCaseId) fails.Add($"[Lần nhận không theo vụ phiếu] PO{po} op{op} {desc}");
                        if (ok && pickCell != null) Inc("vu.recv.doio");
                        // [NHAN-DOI-O-NC] NC lưu trên lần nhận = NC chọn / mặc định theo vụ; không bao giờ sau công đoạn vụ đã tới; lý do lùi vào Ghi chú
                        var wantNc = ncApp ? (pickNc ?? defR) : null;
                        if (!string.Equals(rr0.ToNC, wantNc, StringComparison.OrdinalIgnoreCase) || (ncWhyR != null && !(rr0.Notes ?? "").Contains(ncWhyR)))
                            fails.Add($"[Nhận đổi ô lưu sai NC (K2)] PO{po} op{op} {desc}: {rr0.ToNC ?? "null"} ≠ {wantNc ?? "null"} · {rr0.Notes}");
                        if (ncApp && rr0.ToNC != null)
                        {
                            Inc("recv.nc.luu");
                            var mxR = FormHelp.MaxReachIdx(db, 1, caseK, "GC");
                            var tiR = ncsR.FindIndex(x => x.Equals(rr0.ToNC, StringComparison.OrdinalIgnoreCase));
                            if (mxR != null && tiR > mxR) fails.Add($"[Nhận đổi ô vào NC sau công đoạn đã tới] PO{po} op{op} {desc}: {rr0.ToNC} (#{tiR}) > #{mxR}");
                        }
                    }
                    break;
                }
                case "wts":
                {
                    var g = new[] { "GC", "HTSP", "KCS", "PKG" }[rnd.Next(4)];
                    var cs = codeOf[g].codes;
                    if (cs.Length == 0) { Inc("skip.wts"); continue; }   // [CHUOI] Part Master không có mã cho nhóm này
                    var c = cs[rnd.Next(cs.Length)];
                    string pg = codeOf[g].pg, code = c; string? tnc = null;   // [TARO-NC-MA] mã Taro dùng ở nhiều NC: "TARO|mã|NC"
                    if (g == "HTSP") { var cp0 = c.Split('|'); pg = cp0[0]; code = cp0[1]; tnc = cp0.Length > 2 ? cp0[2] : null; }
                    string CapKey(string cd, string? n) => n == null ? cd : HandoverLedgerService.TaroKey(cd, n);
                    var cap = HandoverLedgerService.WtsCap(row, pg, CapKey(code, tnc));
                    if (smart && (cap.Max == null || cap.Locked || cap.Max <= 0))
                    {
                        // [GN-NG-TRACE] LOI=1: tìm mã CV / NC đang có hàng chờ
                        var all = codeOf.SelectMany(kv => kv.Value.codes.Select(x => (kv.Key, x))).OrderBy(_ => rnd.Next()).ToList();
                        foreach (var (gg, cc) in all)
                        {
                            string p2 = codeOf[gg].pg, c2 = cc; string? n2 = null;
                            if (gg == "HTSP") { var cp2 = cc.Split('|'); p2 = cp2[0]; c2 = cp2[1]; n2 = cp2.Length > 2 ? cp2[2] : null; }
                            var cp = HandoverLedgerService.WtsCap(row, p2, CapKey(c2, n2));
                            if (cp.Max != null && !cp.Locked && cp.Max > 0) { g = gg; pg = p2; code = c2; tnc = n2; cap = cp; break; }
                        }
                    }
                    if (Environment.GetEnvironmentVariable("LAMHO") == "1" && row.LamHoRuns.Any(x => x.HasTaro) && rnd.NextDouble() < 0.6)
                    {   // [TARO-LAMHO-NC] lượt chuoi_lamho: ưu tiên mã Taro làm hộ đang có hàng chờ (để có OK bộ → trả về NC sau đoạn / NG → trả lỗi)
                        foreach (var cc in codeOf["HTSP"].codes.OrderBy(_ => rnd.Next()))
                        {
                            var cp2 = cc.Split('|'); if (cp2[0] != "TARO") continue;
                            var n2 = cp2.Length > 2 ? cp2[2] : null; var k2 = CapKey(cp2[1], n2);
                            if (!row.G("HTSP").Steps.Any(x => x.LamHoNc != null && x.Code == k2)) continue;
                            var cpx = HandoverLedgerService.WtsCap(row, "TARO", k2);
                            if (cpx.Max != null && !cpx.Locked && cpx.Max > 0) { g = "HTSP"; pg = "TARO"; code = cp2[1]; tnc = n2; cap = cpx; break; }
                        }
                    }
                    if (row.G("GC").Steps.Any(x => HandoverLedgerService.IsVao(x.Code)) && rnd.NextDouble() < 0.05)
                    {   // [N3-FIRST] WTS ghi vào "@VAO" phải bị chặn (không phải NC thật)
                        var nWtsVao = db.WtsProductionLogs.List.Count;
                        var bv = await WtsSvcHelp.SaveAsync(db, 1, "GC", HandoverLedgerService.VaoCode, null, 1, 0, Uid("GC"));
                        Inc("vao.wts");
                        if (bv.Ok || db.WtsProductionLogs.List.Count != nWtsVao) fails.Add($"[N3-FIRST WTS ghi vào @VAO không bị chặn] PO{po} op{op}");
                    }
                    if (cap.Max == null || cap.Locked || cap.Max <= 0) { Inc("skip.wts"); continue; }
                    var q = rnd.Next(1, (int)cap.Max.Value + 1);
                    var ng = rnd.NextDouble() < (smart ? 0.4 : 0.2) ? rnd.Next(0, q + 1) : 0;
                    var extra = new List<InWts> { new InWts(-1, DateTime.Now, pg, g == "GC" ? code : null, g == "GC" ? null : code, q - ng, ng, null, ng > 0 ? -1 : null, StepNC: tnc) };   // [VULOI] dòng mới: vụ mới · [TARO-NC-MA] + NC Taro
                    var after = (await led.GetLedgerForDetailAsync(1, null, extra))!;
                    var bad = HandoverLedgerService.NewNegatives(row, after);
                    desc = $"wts {pg} {code}{(tnc == null ? "" : " NC " + tnc)} ok={q - ng} ng={ng} cap={cap.Max}";
                    if (bad.Any()) { if (Environment.GetEnvironmentVariable("DUMP") == "1") { Console.WriteLine("BEFORE " + desc); Dbg.Dump(row); foreach (var t in db.HandoverTransactions.List) Console.WriteLine($"   tx#{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} {t.Purpose} {t.FromCell}->{t.ToCell} q={t.QtyIssued} cl={t.QtyClosed} st={t.Status} void={t.IsVoided} fnc={t.FromNC} tnc={t.ToNC} src={t.SourceReceiveId} rec={string.Join(",", t.Receives.Select(r => $"#{r.ReceiveId}:{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}"); foreach (var w in db.WtsProductionLogs.List) Console.WriteLine($"   wts#{w.WtsLogId} {w.ProcessGroup} {w.NC}{w.WtsCode} {w.QtyOk}/{w.QtyNg} void={w.IsVoided}"); Console.WriteLine("AFTER"); Dbg.Dump(after); } Inc("rej.wts.k5"); fails.Add($"[WTS bị chặn oan dù trong trần] PO{po} op{op} {desc} → {string.Join("; ", bad)}"); continue; }
                    // [WTS-SVC] lưu bằng HÀM THẬT WtsSaveService.SaveGroupAsync (trần + K5 + ghi dòng + vụ lỗi + số chuẩn, 1 giao dịch);
                    //   bộ kiểm đã tự tính trần + K5 ở trên → service phải cho lưu, không thì "chặn oan"
                    var catW = rnd.Next(1, 6);
                    if (tnc != null && rnd.NextDouble() < 0.1)
                    {   // [TARO-NC-MA] mã dùng ở nhiều NC mà không chọn NC / chọn NC lạ → máy chủ PHẢI chặn, không ghi gì
                        var nW0 = db.WtsProductionLogs.List.Count;
                        var bx = await WtsSvcHelp.SaveAsync(db, 1, pg, null, code, q - ng, ng, Uid(g), catW, stepNc: rnd.NextDouble() < 0.5 ? null : "ZZ");
                        Inc("taro.nc.thieu");
                        if (bx.Ok || db.WtsProductionLogs.List.Count != nW0) fails.Add($"[TARO-NC-MA thiếu / sai NC không bị chặn] PO{po} op{op} {desc}");
                    }
                    var sv = await WtsSvcHelp.SaveAsync(db, 1, pg, g == "GC" ? code : null, g == "GC" ? null : code, q - ng, ng, Uid(g), catW, stepNc: tnc);
                    if (!sv.Ok) { Inc("rej.wts.svc"); fails.Add($"[WtsSaveService chặn oan dù trong trần + qua K5] PO{po} op{op} {desc} → {sv.Err}"); continue; }
                    if (tnc != null) Inc("taro.nc.wts");
                    if (row.G("HTSP").Steps.Any(x => x.LamHoNc != null && x.Pg == pg && x.Code == CapKey(code, tnc))) Inc(ng > 0 ? "lamho.wts.ng" : "lamho.wts");   // [TARO-LAMHO-NC]   // [TARO-NC-MA] dòng WTS mã Taro dùng chung nhiều NC
                    if (tnc != null && db.WtsProductionLogs.List.First(w => w.WtsLogId == sv.LogId).StepNC != tnc) fails.Add($"[TARO-NC-MA StepNC ghi sai] PO{po} op{op} {desc}");
                    {
                        var wNew = db.WtsProductionLogs.List.First(w => w.WtsLogId == sv.LogId);
                        if (wNew.QtyOk != q - ng || wNew.QtyNg != ng || wNew.IsVoided) fails.Add($"[WtsSaveService ghi sai số] PO{po} op{op} {desc} → ok={wNew.QtyOk} ng={wNew.QtyNg}");
                        if (ng > 0 && (wNew.NgCaseId == null || db.NgCases.List.First(x => x.NgCaseId == wNew.NgCaseId).NgCategoryId != catW)) fails.Add($"[WtsSaveService không tạo vụ / sai loại lỗi] PO{po} op{op} {desc}");
                        if (ng == 0 && wNew.NgCaseId != null) fails.Add($"[WtsSaveService gắn vụ cho dòng không NG] PO{po} op{op} {desc}");
                        var nStd = db.WtsProductionLogStds.List.Count(x => x.WtsLogId == wNew.WtsLogId);
                        if (nStd != (g == "GC" ? 5 : 1)) fails.Add($"[WtsSaveService số dòng chuẩn sai] PO{po} op{op} {desc} → {nStd}");
                    }
                    ok = true;
                    break;
                }
                case "lhsua":
                {   // [LAMHO-NHO-SUA][LAMHO-NHAN-NG 2026-11-08] vụ làm hộ (Taro làm hộ NG / NG lúc nhận phiếu theo NC) đang giữ ở ô Hàng lỗi một nhóm:
                    //   (a) giao sang nhóm khác Gia công ô Hàng sửa / Hàng lỗi đã xử lý / Hàng mới → phải chặn, không phiếu nào;
                    //   (b) form "Giao / xử lý hàng" nhờ nhóm khác sửa → phải chặn; (c) nhóm khác Gia công giữ vụ: Kết quả sửa → phải chặn;
                    //   (d) giao ô Hàng lỗi sang nhóm khác (được) rồi bên nhận đổi ô sang Hàng sửa → phải chặn; nhận ô Hàng lỗi → được.
                    //   Đáp án độc lập: vụ làm hộ = vụ có chặng phát sinh ở ô LAMHO (WTS Taro làm hộ NG) HOẶC vụ của lần nhận có NG trên phiếu theo NC (đọc DB).
                    bool LamHoVu(LedgerCase x) => LamHoVuDoc(db, x);
                    if (!row.ApplyWorkflow) { Inc("skip.lhsua.off"); continue; }   // [LAMHO-OFF 2026-11-09] OFF không chặn — đáp án lhsua chỉ cho Workflow ON (OFF: kịch bản taro T27)
                    var cands = groups.SelectMany(g => row.CasesHeldAt(g, HandoverCell.Loi).Where(x => x.Id > 0 && LamHoVu(x)).Select(x => (g, x))).ToList();
                    if (!cands.Any())
                    {   // chưa có vụ làm hộ ở ô Hàng lỗi nào: dựng bằng hàm thật — (1) nhận phiếu theo NC đang chờ có đếm NG 1 (sinh vụ NG lúc nhận),
                        //   (2) HTSP trả chiếc Taro làm hộ bị lỗi về Gia công ô Hàng lỗi (form, như T21) rồi Gia công nhận
                        // (0) phiếu trả chiếc làm hộ bị lỗi (ô Hàng lỗi, mang vụ) đang chờ Gia công nhận → nhận vào ô Hàng lỗi
                        var pLoi = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Status != "COMPLETED"
                                        && t.Purpose == HandoverPurpose.HelpReturn && t.FromCell == HandoverCell.Loi).ToList();
                        if (pLoi.Any())
                        {
                            var t = pLoi[rnd.Next(pLoi.Count)];
                            var rem = t.QtyIssued - db.HandoverReceives.List.Where(x => x.HandoverTxId == t.HandoverTxId && !x.IsVoided).Sum(x => x.QtyOk + x.QtyNg) - (t.QtyClosed ?? 0);
                            if (rem >= 1)
                            {
                                desc = $"lhsua.dung Gia công nhận #{t.HandoverTxId} chiếc làm hộ bị lỗi {rem}";
                                var r = await svc.ReceiveAsync(t.HandoverTxId, 0, rem, null, Uid("GC"));
                                ok = r.Ok; Inc(ok ? "lhsua.dung.nhanloi" : "lhsua.dung.nhanloi.rej"); if (!ok) desc += " ERR " + r.Error;
                                break;
                            }
                        }
                        var pend = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Status != "COMPLETED"
                                        && t.Purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn && t.FromCell != HandoverCell.Loi).ToList();
                        if (!pend.Any() && row.LamHoRuns.FirstOrDefault(x => x.HasTaro) is { } lr0)
                        {   // (3) chưa có phiếu theo NC chờ nhận: HTSP trả 1 chiếc làm hộ (hàng tốt) về NC sau đoạn → bước sau Gia công nhận có NG
                            var (mx, _) = HandoverLedgerService.IssueCapCase(row, "HTSP", HandoverPurpose.HelpReturn, null, null, null, lr0.NextNc);
                            if (mx >= 1)
                            {
                                var r = await svc.IssueSpecialAsync(1, "HTSP", "GC", 1, Uid("HTSP"), HandoverPurpose.HelpReturn, null, lr0.NextNc, "lhsua tra");
                                desc = $"lhsua.dung HTSP trả 1 về {lr0.NextNc}"; ok = r.Ok; Inc(ok ? "lhsua.dung.trave" : "lhsua.dung.trave.rej"); if (!ok) desc += " ERR " + r.Error;
                                break;
                            }
                        }
                        var lhNgH = row.LamHoNgHeld("HTSP");
                        if (pend.Any())
                        {
                            var t = pend[rnd.Next(pend.Count)];
                            var rem = t.QtyIssued - db.HandoverReceives.List.Where(x => x.HandoverTxId == t.HandoverTxId && !x.IsVoided).Sum(x => x.QtyOk + x.QtyNg) - (t.QtyClosed ?? 0);
                            if (rem < 1) { Inc("skip.lhsua"); continue; }
                            desc = $"lhsua.dung nhận #{t.HandoverTxId} {t.Purpose} NG 1";
                            var r = await svc.ReceiveAsync(t.HandoverTxId, rem - 1, 1, null, Uid(t.ToGroupCode), null, null, null, rnd.Next(1, 6));
                            ok = r.Ok; Inc(ok ? "lhsua.dung.nhan" : "lhsua.dung.nhan.rej"); if (!ok) desc += " ERR " + r.Error;
                            break;
                        }
                        if (lhNgH > 0)
                        {
                            var vl = row.Cases.Values.FirstOrDefault(x => x.Id > 0 && x.HeldAt("HTSP", HandoverCell.LamHo) > 0);
                            if (vl == null) { Inc("skip.lhsua"); continue; }
                            desc = $"lhsua.dung trả {vl.No} về Gia công ô Hàng lỗi";
                            var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = 1, Group = "HTSP", UserId = Uid("HTSP"), Kind = HandoverForm.Loi, Pos = "LAMHOLOI", CaseId = vl.Id, Action = HandoverForm.AGiao, Qty = 1 });
                            ok = f.Ok; Inc(ok ? "lhsua.dung.tra" : "lhsua.dung.tra.rej"); if (!ok) desc += " ERR " + f.Error;
                            break;
                        }
                        Inc("skip.lhsua"); continue;
                    }
                    var (hg, cs) = cands[rnd.Next(cands.Count)];
                    var others = groups.Where(g => g != "GC" && g != hg).ToArray();
                    var to = others[rnd.Next(others.Length)];
                    var n0 = db.HandoverTransactions.List.Count; var k0 = db.HandoverRepairResults.List.Count;
                    var how = Pick(rnd, ("giao", 4), ("form", 2), ("kq", hg == "GC" ? 0 : 2), ("nhan", 3));
                    desc = $"lhsua {how} {cs.No} giữ {hg} → {to}";
                    Inc("lhsua." + how);
                    if (how == "giao")
                    {
                        var tc = Pick(rnd, (HandoverCell.Sua, 3), (HandoverCell.Xl, 2), (HandoverCell.Moi, 1));
                        var r = await svc.IssueByCellAsync(1, hg, to, 1, Uid(hg), HandoverCell.Loi, false, tc, null, null, null, null, null, cs.Id);
                        desc += $" ô {tc}";
                        if (r.Ok || db.HandoverTransactions.List.Count != n0) fails.Add($"[LAMHO gửi vụ làm hộ vào ô {tc} nhóm khác Gia công không bị chặn] PO{po} op{op} {desc}");
                        else if (!(r.Error ?? "").Contains("làm hộ bị lỗi")) fails.Add($"[LAMHO chặn sai lời] PO{po} op{op} {desc}: {r.Error}");
                        ok = false; Inc("lhsua.chan");
                    }
                    else if (how == "form")
                    {
                        var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = 1, Group = hg, UserId = Uid(hg), Kind = HandoverForm.Loi, Pos = "TONLOI", CaseId = cs.Id,
                                                                               Action = HandoverForm.AGiao, Purpose = HandoverForm.PNho, ToGroup = to, Qty = 1 });
                        if (f.Ok || db.HandoverTransactions.List.Count != n0) fails.Add($"[LAMHO form nhờ nhóm khác sửa vụ làm hộ không bị chặn] PO{po} op{op} {desc}");
                        ok = false; Inc("lhsua.chan");
                    }
                    else if (how == "kq")
                    {
                        var f = await svc.SaveFormAsync(new FormGiaoRequest { KhPlanDetailId = 1, Group = hg, UserId = Uid(hg), Kind = HandoverForm.Loi, Pos = "TONLOI", CaseId = cs.Id,
                                                                               Action = HandoverForm.ASua, Pass = 1, Notes = "sua" });
                        if (f.Ok || db.HandoverRepairResults.List.Count != k0) fails.Add($"[LAMHO {hg} ghi Kết quả sửa vụ làm hộ không bị chặn] PO{po} op{op} {desc}");
                        ok = false; Inc("lhsua.chan");
                    }
                    else
                    {   // giao ô Hàng lỗi (được) → bên nhận đổi ô sang Hàng sửa (chặn) → nhận ô Hàng lỗi (được)
                        var r = await svc.IssueByCellAsync(1, hg, to, 1, Uid(hg), HandoverCell.Loi, false, HandoverCell.Loi, null, null, null, null, null, cs.Id);
                        if (!r.Ok) { Inc("lhsua.nhan.skip"); desc += " giao ô Hàng lỗi bị từ chối: " + r.Error; ok = false; break; }
                        var tid = db.HandoverTransactions.List.Last().HandoverTxId;
                        var r1 = await svc.ReceiveAsync(tid, 1, 0, null, Uid(to), null, HandoverCell.Sua, "doi o lhsua");
                        if (r1.Ok) fails.Add($"[LAMHO nhận đổi ô vụ làm hộ sang Hàng sửa {to} không bị chặn] PO{po} op{op} {desc}");
                        else Inc("lhsua.nhan.chan");
                        if (rnd.NextDouble() < 0.6)
                        {
                            var r2 = await svc.ReceiveAsync(tid, 0, 1, null, Uid(to));
                            if (!r2.Ok) fails.Add($"[LAMHO nhận vụ làm hộ vào ô Hàng lỗi {to} bị chặn] PO{po} op{op} {desc}: {r2.Error}");
                        }
                        ok = true;
                    }
                    break;
                }
                case "wtsvoid":
                {
                    var ws = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == 1 && !w.IsVoided).ToList();   // [CHUOI] chỉ PO 1
                    if (!ws.Any()) { Inc("skip.wtsvoid"); continue; }
                    var w = ws[rnd.Next(ws.Count)];
                    var bad = await WtsVoidBad(led, w.WtsLogId);   // [K5-VONG-HO-WTS] + K5 theo thời gian hàng sửa trả về (như máy chủ)
                    desc = $"wtsvoid #{w.WtsLogId} {w.ProcessGroup} {w.NC ?? w.WtsCode}";
                    if (bad.Any())
                    {
                        Inc("rej.wtsvoid");
                        var wid = w.WtsLogId;
                        // [WTS-SVC] service cũng phải chặn (K5) — chặn + lời báo có gợi ý gỡ [GN-WHY]
                        var vb = await WtsSvcHelp.VoidAsync(db, wid, 1, w.WorkerId);
                        if (vb.Ok) fails.Add($"[WtsSaveService cho xoá dù K5 âm] PO{po} op{op} {desc} → {string.Join("; ", bad)}");
                        else if (!vb.Err!.StartsWith("Không xoá được")) fails.Add($"[WtsSaveService chặn xoá sai lời] PO{po} op{op} {desc} → {vb.Err}");
                        await WhyCheck(db, svc, led, HandoverWhy.OpWts(new List<long> { wid }), new HashSet<string> { "W:" + wid },
                            async () => await WtsVoid(db, led, wid), null, rnd, po, op, desc, null, loop: true);   // [K5-VONG-HO-WTS]
                        continue;
                    }
                    {   // [WTS-SVC] xoá bằng hàm thật
                        var vd = await WtsSvcHelp.VoidAsync(db, w.WtsLogId, 1, w.WorkerId);
                        if (!vd.Ok) { Inc("rej.wtsvoid.svc"); fails.Add($"[WtsSaveService chặn xoá oan] PO{po} op{op} {desc} → {vd.Err}"); continue; }
                        if (!w.IsVoided || w.VoidReason != "Xóa dòng") fails.Add($"[WtsSaveService xoá không ghi cờ] PO{po} op{op} {desc}");
                    }
                    ok = true;
                    break;
                }
                case "vrecv":
                {
                    var rs = db.HandoverReceives.List.Where(r => r.Transaction.KhPlanDetailId == 1 && !r.IsVoided && !r.Transaction.IsVoided).ToList();   // [CHUOI] chỉ PO 1
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
                        await WhyCheck(db, svc, led, HandoverWhy.OpVoidReceive(rid), RecvOpDocs((await led.GetInputForDetailAsync(1))!, rid),   // [H10] tập bỏ qua như máy chủ
                            async () => (await svc.VoidReceiveAsync(rid, "thu lai", Uid(grp), All)).Ok, null, rnd, po, op, desc, res.Error, loop: true);
                    }
#endif
                    break;
                }
                case "vissue":
                {
                    var ts = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided).ToList();   // [CHUOI] chỉ PO 1
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
                                        { var vr = await VoidRecvDeep(db, svc, led, br, 0); if (!vr.Ok) { lastErr = "auto-recv #" + br + ": " + vr.Error; if (Environment.GetEnvironmentVariable("WHYDBG") == "1") { foreach (var tt in db.HandoverTransactions.List) Console.WriteLine($"   tx#{tt.HandoverTxId} {tt.FromGroupCode}->{tt.ToGroupCode} {tt.Purpose} {tt.FromCell}->{tt.ToCell} q={tt.QtyIssued} void={tt.IsVoided} src={tt.SourceReceiveId} rec={string.Join(",", tt.Receives.Select(r => $"#{r.ReceiveId}:{r.QtyOk}/{r.QtyNg}{(r.IsVoided?"x":"")}@{r.ToCell}"))}"); Console.WriteLine("   phoi=" + db.KhoVatLieus.List.First().SoLuongThucNhan); } return false; } }
                                    var rv = await VoidRecvDeep(db, svc, led, rr, 0);   // [FUZZ-PRE-DEEP] bị chặn thì làm theo gợi ý gỡ của phần mềm (đệ quy) rồi thử lại
                                    if (!rv.Ok) { lastErr = rv.Error; return false; }
                                }
                                return true;
                            };
                        }
                        await WhyCheck(db, svc, led, opFn, opDocs,
                            async () => (await svc.VoidIssueAsync(tid, "thu lai", issuer, new List<string>())).Ok, pre, rnd, po, op, desc, res.Error, loop: true);
                    }
#endif
                    break;
                }
                case "wtsedit":   // [VULOI] sửa dòng WTS (void + dòng mới; 70% tổng OK + NG giữ nguyên, 30% đổi tổng ±1 [WTS-SUA-PHAN]): dòng mới giữ vụ cũ; NG giảm mà vụ đã đi tiếp → K5 chặn
                {
                    var ws = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == 1 && !w.IsVoided && w.QtyDone > 0).ToList();   // [CHUOI] chỉ PO 1
                    if (!ws.Any()) { Inc("skip.wtsedit"); continue; }
                    var w = ws[rnd.Next(ws.Count)];
                    var tot = w.QtyDone;
                    if (rnd.NextDouble() < 0.3) tot = Math.Max(1, tot + rnd.Next(-1, 2));   // [WTS-SUA-PHAN 2026-11-05] 30%: sửa đổi cả tổng (tăng / giảm 1) — rà độc lập lần 2
                    var ng2 = (decimal)rnd.Next(0, (int)tot + 1);
                    var keep = ng2 > 0 ? w.NgCaseId : null;
                    var extra = new List<InWts> { new InWts(-1, DateTime.Now, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, null, ng2 > 0 ? (keep ?? -1) : null, StepNC: w.StepNC) };   // [TARO-NC-MA] giữ NC Taro
                    var after = (await led.GetLedgerForDetailAsync(1, new List<long> { w.WtsLogId }, extra))!;
                    var bad = HandoverLedgerService.NewNegatives(row, after);
                    desc = $"wtsedit #{w.WtsLogId} {w.ProcessGroup} {w.NC ?? w.WtsCode} NG {w.QtyNg}->{ng2} vu={w.NgCaseId}";
                    if (bad.Any()) { Inc("rej.wtsedit"); if (w.NgCaseId != null && bad.Any(b => b.Contains("VL"))) Inc("vu.k5.wtsedit"); continue; }
                    {   // [WTS-SVC] trần L3 khi sửa như trang: sổ bỏ dòng đang sửa, OK + NG mới ≤ Chưa làm (dòng mồ côi do lần nhận đã huỷ → "chưa nhận hàng")
                        var basis = (await led.GetLedgerForDetailAsync(1, new List<long> { w.WtsLogId }))!;
                        var capE = HandoverLedgerService.WtsCap(basis, w.ProcessGroup, HandoverLedgerService.WtsKey(basis, w.ProcessGroup, w.NC ?? w.WtsCode, w.StepNC) ?? (w.NC ?? w.WtsCode));   // [TARO-NC-MA] khoá (mã, NC)
                        if (capE.Max != null && (capE.Locked || tot > capE.Max.Value))
                        {
                            var seB = await WtsSvcHelp.SaveAsync(db, 1, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, w.WorkerId, w.NgCaseId != null ? db.NgCases.List.First(x => x.NgCaseId == w.NgCaseId).NgCategoryId : 1, keep, w.WtsLogId, stepNc: w.StepNC);
                            if (seB.Ok) fails.Add($"[WtsSaveService cho sửa vượt trần L3] PO{po} op{op} {desc} cap={capE.Max} locked={capE.Locked}");
                            else if (!seB.Err!.StartsWith("PO ")) fails.Add($"[WtsSaveService chặn sửa sai lời] PO{po} op{op} {desc} → {seB.Err}");
                            Inc("rej.wtsedit.cap"); continue;
                        }
                    }
                    {   // [K5-VONG-HO-WTS-SUA 2026-11-05] (sau trần L3 — cùng thứ tự máy chủ, rà độc lập lần 2 Thấp 3) sửa dòng = huỷ chứng từ nguồn: máy chủ xét thêm K5 theo thời gian hàng sửa trả về
                        //   (dòng thay cùng NC / mã / người → mang giờ lưu gốc của dòng cũ). Có vòng tròn mới → máy chủ PHẢI chặn, dòng cũ giữ nguyên.
                        // [WTS-SUA-PHAN] đáp án độc lập: giờ + các bản của mọi dòng sửa dựng lại từ editPrev (bộ kiểm tự ghi), không dùng EditOrigins của sổ
                        var bi = Retime((await led.GetInputForDetailAsync(1))!, db, editPrev);
                        var ai = Retime((await led.GetInputForDetailAsync(1))!, db, editPrev);
                        var oldW = bi.Wts.First(x => x.Id == w.WtsLogId);
                        ai.Wts = ai.Wts.Where(x => x.Id != w.WtsLogId).ToList();
                        var oldVers = TrackedVers(db, editPrev, w.WtsLogId);
                        ai.Wts.AddRange(extra.Select(x => x with { Saved = oldW.Saved,
                            Vers = oldVers.Append(new WtsVer(DateTime.MaxValue, x.Ok, x.Ng)).ToList() }));   // bản mới = mới nhất
                        var loopBad = bi.ApplyWorkflow ? HandoverLedgerService.NewLoopKeys(bi, HandoverLedgerService.Compute(bi), ai) : new();
                        if (loopBad.Any())
                        {
                            var catL = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == keep)?.NgCategoryId ?? 1;
                            var seL = await WtsSvcHelp.SaveAsync(db, 1, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, w.WorkerId, catL, keep, w.WtsLogId, stepNc: w.StepNC);
                            if (seL.Ok) fails.Add($"[WtsSaveService cho sửa lọt vòng tròn theo thời gian] PO{po} op{op} {desc}");
                            else if (!(seL.Err ?? "").Contains("theo thứ tự thời gian")) fails.Add($"[WtsSaveService chặn sửa (vòng) sai lời] PO{po} op{op} {desc} → {seL.Err}");
                            if (db.WtsProductionLogs.List.First(x => x.WtsLogId == w.WtsLogId).IsVoided) fails.Add($"[Sửa bị chặn mà dòng cũ đã huỷ] PO{po} op{op} {desc}");
                            Inc("rej.wtsedit.loop"); continue;
                        }
                    }
                    // 30%: đổi loại lỗi của vụ cũ (chỉ được khi vụ chưa đi phiếu nào / chưa có Kết quả sửa)
                    if (keep != null && rnd.NextDouble() < 0.3)
                    {
                        var moved = db.HandoverTransactions.List.Any(t => !t.IsVoided && t.NgCaseId == keep);
                        moved |= db.HandoverRepairResults.List.Any(r => !r.IsVoided && r.NgCaseId == keep);   // [GC-SUA-TAI-CHO] vụ có Kết quả sửa cũng không đổi loại lỗi
                        var cs0 = db.NgCases.List.First(x => x.NgCaseId == keep);
                        var newCat = cs0.NgCategoryId % 5 + 1;
                        var err = await NgCaseService.ChangeCaseCategoryAsync(db, keep.Value, newCat);
                        if (moved && err == null) fails.Add($"[Đổi loại lỗi vụ đã đi phiếu] PO{po} op{op} {desc}");
                        if (!moved && (err != null || cs0.NgCategoryId != newCat)) fails.Add($"[Không đổi được loại lỗi vụ chưa đi] PO{po} op{op} {desc}: {err}");
                        if (err == null) Inc("vu.doiloai");
                    }
                    {   // [WTS-SVC] sửa dòng bằng hàm thật: huỷ dòng cũ (Sửa lại) + ghi dòng mới giữ vụ, cùng giao dịch
                        var catE = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == keep)?.NgCategoryId ?? rnd.Next(1, 6);
                        var oldId = w.WtsLogId;
                        var se = await WtsSvcHelp.SaveAsync(db, 1, w.ProcessGroup, w.NC, w.WtsCode, tot - ng2, ng2, w.WorkerId, catE, keep, oldId, stepNc: w.StepNC);
                        if (!se.Ok) { Inc("rej.wtsedit.svc"); fails.Add($"[WtsSaveService chặn sửa oan dù qua K5] PO{po} op{op} {desc} → {se.Err}"); continue; }
                        var wOld = db.WtsProductionLogs.List.First(x => x.WtsLogId == oldId);
                        var wNew = db.WtsProductionLogs.List.First(x => x.WtsLogId == se.LogId);
                        if (!wOld.IsVoided || wOld.VoidReason != "Sửa lại") fails.Add($"[WtsSaveService sửa không huỷ dòng cũ] PO{po} op{op} {desc}");
                        if (wNew.QtyOk != tot - ng2 || wNew.QtyNg != ng2) fails.Add($"[WtsSaveService sửa ghi sai số] PO{po} op{op} {desc}");
                        if (ng2 > 0 && keep != null && wNew.NgCaseId != keep) fails.Add($"[WtsSaveService sửa không giữ vụ] PO{po} op{op} {desc} → {wNew.NgCaseId}");
                        if (ng2 > 0 && keep == null && wNew.NgCaseId == null) fails.Add($"[WtsSaveService sửa thêm NG không tạo vụ] PO{po} op{op} {desc}");
                        if (ng2 == 0 && wNew.NgCaseId != null) fails.Add($"[WtsSaveService sửa hết NG vẫn gắn vụ] PO{po} op{op} {desc}");
                        // [WTS-SUA-PHAN] sổ ghép dòng sửa (EditOrigins) phải khớp đúng chuỗi bộ kiểm tự ghi: giờ gốc + từng bản (giờ, OK, NG)
                        editPrev[se.LogId!.Value] = oldId;
                        var chk = (await led.GetInputForDetailAsync(1))!.Wts.First(x => x.Id == se.LogId);
                        var tv = TrackedVers(db, editPrev, se.LogId.Value);
                        if (chk.Vers == null || chk.Saved != tv[0].At || chk.Vers.Count != tv.Count || !chk.Vers.SequenceEqual(tv))
                            fails.Add($"[Sổ ghép dòng sửa sai chuỗi] PO{po} op{op} {desc} sổ={string.Join(",", chk.Vers ?? new List<WtsVer>())} kiểm={string.Join(",", tv)}");
                    }
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
                case "kqsua":   // [GC-SUA-TAI-CHO] ghi Kết quả sửa (hàm thật CreateRepairResultAsync)
                {
                    var cands = new List<(string g, string cell, long id)>();
                    foreach (var g0 in groups) foreach (var c0 in new[] { HandoverCell.Sua, HandoverCell.Loi })
                        foreach (var cs0 in row.CasesHeldAt(g0, c0)) cands.Add((g0, c0, cs0.Id));
                    if (!cands.Any()) { Inc("skip.kqsua"); continue; }
                    var (g, cell, cid) = cands[rnd.Next(cands.Count)];
                    long? caseId = cid == 0 ? null : cid;
                    var cap = HandoverLedgerService.RepairCap(row, g, cell, caseId).Max;
                    if (cap <= 0) { Inc("skip.kqsua"); continue; }
                    var over = rnd.NextDouble() < 0.1;
                    var tot = over ? cap + 1 : rnd.Next(1, (int)Math.Max(1, cap) + 1);
                    if (!over && tot > cap) tot = cap;
                    var fail = cell == HandoverCell.Sua ? rnd.Next(0, (int)tot + 1) : 0;
                    var pass = tot - fail;
                    string? toNc = null; string? ncWhy = null; string? badLater = null;
                    if (g == "GC" && pass > 0)
                    {
                        // [GN-FORM1][K2] NC làm tiếp: mặc định = NC của vụ (đáp án độc lập FormHelp), 25% làm lại NC trước + lý do, 10% thử NC sau (phải bị chặn).
                        //   Trước: chọn NC bất kỳ 70% (luật cũ cho mọi NC).
                        var (ncs0, vao0) = FormHelp.Chain(db, 1);
                        var def0 = FormHelp.ExpectNextNc(db, 1, cid, g);
                        if (HandoverForm.CaseNextNc(row, cid, g) != def0) fails.Add($"[NC làm tiếp mặc định ≠ đáp án] PO{po} op{op} vụ {cid}: {HandoverForm.CaseNextNc(row, cid, g)} ≠ {def0}");
                        var di0 = FormHelp.Idx(ncs0, vao0, def0);
                        var earlier = ncs0.Where((c, i) => i < di0).Select(c => (string?)c).ToList();
                        if (vao0 && di0 >= 0 && def0 != null) earlier.Add(null);
                        var later = ncs0.Where((c, i) => i > di0).ToList();
                        toNc = def0;
                        if (earlier.Any() && rnd.NextDouble() < 0.25) { toNc = earlier[rnd.Next(earlier.Count)]; ncWhy = "lam lai NC truoc"; }
                        if (later.Any() && rnd.NextDouble() < 0.1) badLater = later[rnd.Next(later.Count)];
                    }
                    desc = $"kqsua {g} {cell} vu={cid} dat={pass} khong={fail} cap={cap} nc={toNc}";
                    // [TARO-LAMHO-NC] đáp án: vụ hàng Taro làm hộ bị lỗi chỉ sửa ở Gia công (HandoverService.KetQuaSua) — nhóm khác đang giữ vụ đó ở ô Hàng sửa / Hàng lỗi
                    //   (Gia công gửi nhờ sửa) thì ghi Kết quả sửa phải bị chặn, chỉ trả lại nơi gửi. Kiem trạm 08/11 seed 23980 PO19: đáp án cũ coi là "trong trần bị từ chối"
                    //   (có từ Gói 6, 2cdcd14 y hệt). Đường gửi nhờ sửa vụ làm hộ sang nhóm khác: sổ dòng LAMHO-NHO-SUA (Thấp).
                    if (row.ApplyWorkflow && g != "GC" && caseId is long lhq && row.Cases.TryGetValue(lhq, out var lhqC) && lhqC.IsLamHo)   // [LAMHO-OFF] chỉ ON
                    {
                        var lhr = await svc.CreateRepairResultAsync(1, g, cell, caseId, pass, fail, null, null, Uid(g));
                        checks++;
                        if (lhr.Ok || !(lhr.Error ?? "").Contains("làm hộ bị lỗi")) fails.Add($"[Kết quả sửa vụ làm hộ ngoài Gia công không bị chặn đúng] PO{po} op{op} {desc}: {lhr.Error}");
                        Inc("lamho.kqsua.chan"); continue;
                    }
                    if (badLater != null)
                    {
                        var bl = await svc.CreateRepairResultAsync(1, g, cell, caseId, Math.Max(1, Math.Min(pass, cap)), 0, badLater, null, Uid(g), "thu NC sau");
                        if (bl.Ok) fails.Add($"[K2 NC sau lọt] PO{po} op{op} {desc} → {badLater}");
                    }
                    var deny = await svc.CreateRepairResultAsync(1, g, cell, caseId, Math.Min(pass, cap), 0, toNc, null, 99);
                    if (deny.Ok) fails.Add($"[Quyền Kết quả sửa] PO{po} op{op} người ngoài nhóm ghi được cho {g}");
                    // Chỉ Gia công lưu NC làm tiếp (nhóm khác máy chủ bỏ ToNC → lệnh thử sẽ GHI THẬT, ăn mất trần của lệnh chính — kiem máy Hùng 04/11 22:08 seed 31975 / 31976)
                    if (g == "GC" && rnd.NextDouble() < 0.05)
                    {
                        var badNc = await svc.CreateRepairResultAsync(1, g, cell, caseId, Math.Max(1, Math.Min(pass, cap)), 0, rnd.NextDouble() < 0.5 ? HandoverLedgerService.VaoCode : "NCX", null, Uid(g));
                        if (badNc.Ok) fails.Add($"[Kết quả sửa NC lạ] PO{po} op{op} {desc}");
                    }
                    if (cell == HandoverCell.Loi && rnd.NextDouble() < 0.05)
                    {
                        var fl = await svc.CreateRepairResultAsync(1, g, cell, caseId, 0, 1, null, null, Uid(g));
                        if (fl.Ok) fails.Add($"[Kết quả sửa không đạt từ ô Hàng lỗi] PO{po} op{op} {desc}");
                    }
                    var res = await svc.CreateRepairResultAsync(1, g, cell, caseId, pass, fail, toNc, null, Uid(g), ncWhy);
                    ok = res.Ok; if (!ok) Inc("rej.kqsua");
                    if (ok && g == "GC" && pass > 0)
                    {   // [GN-FORM1][K2] lưu đúng NC đã chọn; làm lại NC trước thì lý do nằm trong Ghi chú
                        var kr = db.HandoverRepairResults.List.Last();
                        if (kr.ToNC != toNc || (ncWhy != null && !(kr.Notes ?? "").Contains(ncWhy))) fails.Add($"[K2 lưu sai NC / thiếu lý do] PO{po} op{op} {desc}: {kr.ToNC} · {kr.Notes}");
                        var (ncsB, _) = FormHelp.Chain(db, 1);
                        var mxB = FormHelp.MaxReachIdx(db, 1, kr.NgCaseId ?? 0, "GC");
                        var tiB = kr.ToNC == null ? -1 : ncsB.FindIndex(x => x.Equals(kr.ToNC, StringComparison.OrdinalIgnoreCase));
                        if (mxB != null && tiB > mxB) fails.Add($"[Sửa đạt vào NC sau công đoạn đã tới] PO{po} op{op} {desc}: {kr.ToNC} (#{tiB}) > xa nhất #{mxB}");
                    }
                    if (over && ok) fails.Add($"[Kết quả sửa vượt trần] PO{po} op{op} {desc}");
                    // [GC-SUA-TAI-CHO] rà độc lập V3: vượt trần (theo vụ) phải bị TRẦN chặn ("tối đa"), không được để K5 chặn thay (Workflow OFF thì K5 không chặn)
                    if (over && !ok && !res.Error.Contains("tối đa")) fails.Add($"[Kết quả sửa vượt trần không bị trần chặn] PO{po} op{op} {desc}: {res.Error}");
                    if (!over && !ok && !res.Error.StartsWith("Không lưu")) fails.Add($"[Kết quả sửa trong trần bị từ chối] PO{po} op{op} {desc}: {res.Error}");
                    break;
                }
                case "form":   // [GN-FORM1] form "Giao / xử lý hàng" (hàm thật SaveFormAsync) — đáp án độc lập FormHelp: chứng từ + ô sổ; bị chặn thì y nguyên
                {
                    var g = groups[rnd.Next(groups.Length)];
                    bool tot = rnd.NextDouble() < 0.4;
                    var poss = HandoverForm.FormPositions(row, g, tot).Where(p => p.Have > 0).ToList();
                    if (!poss.Any()) { Inc("skip.form"); continue; }
                    var pos = poss[rnd.Next(poss.Count)];
                    var G = row.G(g);
                    // [TARO-LAMHO-NC] LAMHO = Tồn hàng giao theo NC − chiếc làm hộ bị lỗi (Workflow ON, HTSP 1 đoạn Taro: ≤ OK bộ − đã trả); LAMHOLOI = chiếc làm hộ bị lỗi
                    var lhNgH = row.Cases.Values.Sum(x => x.HeldAt(g, HandoverCell.LamHo));
                    var lhRuns = row.LamHoRuns.Where(x => x.HasTaro).ToList();
                    decimal lhGood = row.ApplyWorkflow && lhRuns.Count > 0 ? row.G("GC").Steps.Where(x => !x.IsExcluded && !HandoverLedgerService.IsVao(x.Code)).Select(x => ExpLamHoRet(row, g, x.Code)).DefaultIfEmpty(0).Max()
                                   : G.LamHoGiu - lhNgH;
                    decimal? want = pos.Id switch { "PHOI" or "DA" => G.ConGiao, "CHUA" => G.ChuaLam, "TONLOI" => G.LoiGiu, "TONSUA" => G.SuaGiu, "LAMHO" => lhGood, "LAMHOLOI" => lhNgH, _ => null };
                    if (pos.Id.StartsWith("NC:")) { var st0 = G.Steps.First(x => x.Code == pos.Id[3..]); want = Math.Min(st0.Waiting ?? 0, G.ChuaLam); }
                    if (want != null && Math.Max(0, want.Value) != pos.Have) fails.Add($"[Form số dòng ≠ ô sổ] PO{po} op{op} {g} {pos.Id}: {pos.Have} ≠ {want}");
                    var req = new FormGiaoRequest { KhPlanDetailId = 1, Group = g, UserId = Uid(g), Kind = tot ? HandoverForm.Tot : HandoverForm.Loi, Pos = pos.Id };
                    var others = groups.Where(x => x != g).ToArray();
                    var (fncs, fvao) = FormHelp.Chain(db, 1);
                    string? RndNc() => fncs.Count == 0 ? null : fncs[rnd.Next(fncs.Count)];
                    string? expErr = null;   // cố ý sai: "" = bị chặn bất kỳ lời nào, khác "" = lời báo phải chứa
                    bool over = rnd.NextDouble() < 0.08;
                    decimal have = pos.Have;
                    int Q(decimal max) => over ? (int)Math.Floor(max) + 1 : rnd.Next(1, (int)Math.Max(1, Math.Floor(max)) + 1);
                    string? expNc = null; bool chkNc = false; string? ncWhy = null;
                    void PickNc(long? caseKey, string? defFresh)
                    {   // [K2] NC làm tiếp (GC, sửa đạt)
                        if (g != "GC" || req.Pass <= 0 || fncs.Count == 0) return;
                        var def0 = caseKey != null ? FormHelp.ExpectNextNc(db, 1, caseKey.Value, g) : defFresh;
                        var di0 = FormHelp.Idx(fncs, fvao, def0);
                        var earlier = fncs.Where((c, i) => i < di0).Select(c => (string?)c).ToList();
                        if (fvao && di0 >= 0 && def0 != null) earlier.Add(null);
                        var later = fncs.Where((c, i) => i > di0).ToList();
                        chkNc = true; expNc = def0;
                        var x = rnd.NextDouble();
                        if (x < 0.15 && earlier.Any()) { expNc = earlier[rnd.Next(earlier.Count)]; req.NcNext = expNc ?? HandoverService.FormNewNc; req.NcReason = ncWhy = "lam lai NC truoc"; }
                        else if (x < 0.22 && earlier.Any()) { req.NcNext = earlier[rnd.Next(earlier.Count)] ?? HandoverService.FormNewNc; expErr ??= "lý do"; }
                        else if (x < 0.3 && later.Any() && caseKey == 0)
                        {   // vòng 3 C1b: hàng cũ chưa mã vụ — NC sau được, kèm lý do; thiếu lý do thì chặn
                            if (rnd.NextDouble() < 0.7) { expNc = later[rnd.Next(later.Count)]; req.NcNext = expNc; req.NcReason = ncWhy = "da qua NC do"; }
                            else { req.NcNext = later[rnd.Next(later.Count)]; expErr ??= "lý do"; }
                        }
                        else if (x < 0.3 && later.Any()) { req.NcNext = later[rnd.Next(later.Count)]; req.NcReason = "abc"; expErr ??= "không bỏ qua NC"; }
                    }
                    if (tot)
                    {
                        req.Action = HandoverForm.AGiao;
                        if (pos.LamHo)
                        {
                            req.ToGroup = "GC"; if (rnd.NextDouble() < 0.9) req.ToNC = RndNc();
                            // [TARO-LAMHO-NC] trả về NC sau đoạn Taro làm hộ (đường của khách); trần theo NC nhận đã chọn (câu 1)
                            if (g == "HTSP" && lhRuns.Count > 0 && rnd.NextDouble() < 0.7) req.ToNC = lhRuns[0].NextNc;
                            if (req.ToNC == null) expErr ??= "K3";
                            if (req.ToNC != null) have = ExpLamHoRet(row, g, req.ToNC);
                        }
                        else
                        {
                            var nx = HandoverForm.NextGroup(g);
                            req.ToGroup = rnd.NextDouble() < 0.7 && nx != "" ? nx : others[rnd.Next(others.Length)];
                            if (pos.HelpFrom != null) { if (rnd.NextDouble() < 0.9) req.Notes = "lam ho"; else expErr ??= "ghi chú"; }
                            if (pos.HelpFrom != null && row.ApplyWorkflow && req.ToGroup != "HTSP" && row.LamHoRuns.Any(x => x.HasTaro && x.FromNc == pos.HelpFrom)) expErr ??= "HTSP";   // [TARO-LAMHO-NC] đoạn sau có Taro: chỉ HTSP
                            else
                            {
                                if (rnd.NextDouble() < 0.15) req.ToCell = HandoverCell.ReceiveCells[rnd.Next(HandoverCell.ReceiveCells.Length)];
                                if (req.ToGroup == "GC" && (req.ToCell ?? HandoverCell.Moi) is HandoverCell.Moi or HandoverCell.Xl && rnd.NextDouble() < 0.5) req.ToNC = RndNc();
                                // [GOI12-FUZZ-HTSPLH 2026-11-09] đáp án còn thiếu từ Gói 6 (TARO-LAMHO-NC, HandoverService ~dòng 671): Workflow ON, HTSP chỉ có mã Taro làm hộ
                                //   → giao Hàng mới cho HTSP bị chặn ("chỉ làm Taro làm hộ"). Lộ ở kiem trạm 09/11 10:31 seed 15504 PO5 op115 (Kho → HTSP);
                                //   bản 719c14c y hệt (không phải lỗi Gói 12). Đáp án độc lập: mọi mã HTSP chính (không chuẩn bị) đều là mã Taro ở NC của một đoạn làm hộ có Taro.
                                //   Chỉ phiếu thường (giao theo NC làm hộ không phải Hàng mới — kiem trạm 09/11 11:21 seed 28904 PO9).
                                if (pos.HelpFrom == null && row.ApplyWorkflow && req.ToGroup == "HTSP" && HandoverCell.ResolveCells(HandoverCell.Moi, false, req.ToCell ?? HandoverCell.Moi).Item1 == HandoverPurpose.New
                                    && row.G("HTSP").Steps.Where(s => !s.IsPrep).ToList() is { Count: > 0 } hm && hm.All(s => s.Pg == "TARO" && s.LamHoNc != null
                                        && row.LamHoRuns.Any(r => r.HasTaro && r.Ncs.Contains(s.LamHoNc, StringComparer.OrdinalIgnoreCase))))
                                    expErr ??= "chỉ làm Taro làm hộ";
                            }
                        }
                        req.Qty = Q(have);
                        if (req.Qty > have) over = true;   // trần đổi theo NC nhận (trả hàng làm hộ) — rà vòng 2 L6
                    }
                    else if (pos.HeldCell != null)
                    {
                        var cases = row.CasesHeldAt(g, pos.HeldCell);
                        if (!cases.Any()) { Inc("skip.form"); continue; }
                        var cs = cases[rnd.Next(cases.Count)]; req.CaseId = cs.Id;
                        var ch = cs.HeldAt(g, pos.HeldCell);
                        var acts = pos.HeldCell == HandoverCell.Loi ? new[] { HandoverForm.AGiao, HandoverForm.ASua, HandoverForm.AHuy, HandoverForm.AKhach }
                                 : pos.HeldCell == HandoverCell.LamHo ? new[] { HandoverForm.AGiao, HandoverForm.AGiao, HandoverForm.AGiao, HandoverForm.AHuy }   // [TARO-LAMHO-NC] chỉ trả Gia công (Huỷ phải bị chặn)
                                                                    : new[] { HandoverForm.ASua, HandoverForm.ATraLai, HandoverForm.ASuaXong };
                        req.Action = acts[rnd.Next(acts.Length)];
                        if (req.Action == HandoverForm.ASua)
                        {
                            var n = Q(ch);
                            req.Fail = pos.HeldCell == HandoverCell.Sua ? rnd.Next(0, n + 1) : 0; req.Pass = n - req.Fail;
                            PickNc(cs.Id, null);
                            // [LAMHO-NHAN-NG] (rà độc lập Gói 11 vòng 2 Tr1) vụ làm hộ ở nhóm khác Gia công: Sửa tại nhóm phải bị chặn (KetQuaSua) — đáp án độc lập
                            if (row.ApplyWorkflow && g != "GC" && LamHoVuDoc(db, cs)) { expErr ??= "làm hộ bị lỗi"; Inc("lamho.form.asua.chan"); }
                        }
                        else
                        {
                            req.Qty = Q(ch);
                            if (pos.HeldCell == HandoverCell.LamHo)
                            {   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ: chỉ về Gia công (nhóm khác → chặn); Huỷ → chặn
                                if (req.Action == HandoverForm.AHuy) expErr ??= "";
                                else if (rnd.NextDouble() < 0.1) { req.ToGroup = others.Where(x => x != "GC").DefaultIfEmpty("KCS").First(); expErr ??= "Gia công"; }
                                else req.ToGroup = rnd.NextDouble() < 0.5 ? "GC" : null;
                            }
                            else if (req.Action == HandoverForm.AGiao)
                            {
                                var cg = HandoverForm.CaseCauseGroup(cs, g);
                                req.Purpose = cg != null && rnd.NextDouble() < 0.5 ? HandoverForm.PTra : HandoverForm.PNho;
                                req.ToGroup = req.Purpose == HandoverForm.PTra && rnd.NextDouble() < 0.8 ? cg : others[rnd.Next(others.Length)];
                                if (rnd.NextDouble() < 0.1) req.ToCell = HandoverCell.ReceiveCells[rnd.Next(HandoverCell.ReceiveCells.Length)];
                                // [LAMHO-NHO-SUA 2026-11-08] đáp án: vụ Taro làm hộ bị lỗi sang nhóm khác Gia công — ô Hàng sửa chặn (chỉ Gia công sửa),
                                //   ô hàng đạt chặn (Gói 6 vòng 3 T2 — trước gói này bộ kiểm chưa đặt đáp án cho nhánh ô hàng đạt), ô Hàng lỗi được
                                var tcLh = req.ToCell ?? (req.Purpose == HandoverForm.PTra ? HandoverCell.Loi : HandoverCell.Sua);
                                if (row.ApplyWorkflow && LamHoVuDoc(db, cs) && req.ToGroup != null && req.ToGroup != "GC")   // [LAMHO-OFF] chỉ ON   // [LAMHO-NHAN-NG] đáp án độc lập (rà độc lập Gói 11 T1 phụ)
                                {
                                    if (tcLh == HandoverCell.Sua) { expErr ??= "chỉ Gia công sửa"; Inc("lamho.nhosua.form"); }
                                    else if (tcLh is HandoverCell.Moi or HandoverCell.Xl) expErr ??= "làm hộ bị lỗi";   // [LAMHO-NHAN-NG] chữ theo loại vụ (vụ NG lúc nhận: "hàng làm hộ bị lỗi — NG lúc nhận …")
                                }
                            }
                            else if (req.Action is HandoverForm.ATraLai or HandoverForm.ASuaXong)
                                req.ToGroup = HandoverForm.CaseSender(cs, g) ?? others[rnd.Next(others.Length)];
                        }
                    }
                    else
                    {
                        req.Prev = rnd.NextDouble() < 0.5;
                        if (req.Prev && g != "KHO")
                        {
                            var pg0 = HandoverForm.PrevGroup(g);
                            var causes = new[] { "KHACH" }.Concat(others).ToArray();
                            req.Cause = rnd.NextDouble() < 0.7 && pg0 != "" ? pg0 : causes[rnd.Next(causes.Length)];
                            if (rnd.NextDouble() < 0.1) req.Cause = null;   // [T4] bỏ trống → máy chủ lấy nhóm trước
                        }
                        var cx = rnd.NextDouble();
                        req.CatId = cx < 0.02 ? null : cx < 0.04 ? 6 : rnd.Next(1, 6);
                        if (req.CatId == null || req.CatId == 6) expErr ??= "";
                        req.Action = Pick(rnd, (HandoverForm.AGiao, 4), (HandoverForm.ASua, 3), (HandoverForm.AGiu, 2), (HandoverForm.AHuy, 1), (HandoverForm.AKhach, 1));
                        if (req.Action == HandoverForm.ASua)
                        {
                            var n = Q(have);
                            req.Fail = rnd.Next(0, n + 1); req.Pass = n - req.Fail;
                            if (req.Pass <= 0 && req.CatId is null or 6) expErr = "";
                            var defF = FormHelp.ExpectFreshNc(db, 1, g, pos.Id);
                            if (g == "GC" && HandoverForm.CaseNextNc(row, "GC", pos.NgNc, NgOrigin.BaoLoi) != defF)
                                fails.Add($"[NC làm tiếp mặc định (lỗi mới) ≠ đáp án] PO{po} op{op} {pos.Id}: {HandoverForm.CaseNextNc(row, "GC", pos.NgNc, NgOrigin.BaoLoi)} ≠ {defF}");
                            PickNc(null, defF);
                        }
                        else
                        {
                            req.Qty = Q(have);
                            if (req.Action == HandoverForm.AGiao)
                            {
                                var cg = req.Prev && g != "KHO" && req.Cause != "KHACH" && req.Cause != g ? req.Cause : null;
                                req.Purpose = g == "KHO" ? HandoverForm.PNho : cg != null && rnd.NextDouble() < 0.6 ? HandoverForm.PTra : HandoverForm.PNho;
                                req.ToGroup = req.Purpose == HandoverForm.PTra && rnd.NextDouble() < 0.8 ? cg : others[rnd.Next(others.Length)];
                                if (rnd.NextDouble() < 0.1) req.ToCell = HandoverCell.ReceiveCells[rnd.Next(HandoverCell.ReceiveCells.Length)];
                            }
                        }
                    }
                    var exp = FormHelp.Build(row, req);
                    if (exp.ToNcByCase && fncs.Count > 0 && expErr == null)
                    {   // [GN-FORM1] rà độc lập V3: hàng của vụ vào Gia công → NC theo vụ; 15% làm lại NC trước + lý do, 5% thiếu lý do, 5% NC sau (phải bị chặn)
                        var defC = FormHelp.CaseNcFor(db, req);
                        var diC = FormHelp.Idx(fncs, fvao, defC);
                        var earC = fncs.Where((c, i) => i < diC).Select(c => (string?)c).ToList();
                        if (fvao && diC >= 0 && defC != null) earC.Add(null);
                        var latC = fncs.Where((c, i) => i > diC).ToList();
                        var y = rnd.NextDouble();
                        if (y < 0.15 && earC.Any()) { req.ToNC = earC[rnd.Next(earC.Count)] ?? HandoverService.FormNewNc; req.NcReason = "lam lai NC truoc"; }
                        else if (y < 0.2 && earC.Any()) { req.ToNC = earC[rnd.Next(earC.Count)] ?? HandoverService.FormNewNc; expErr = "lý do"; }
                        else if (y < 0.25 && latC.Any() && req.CaseId == 0)
                        {   // vòng 3 C1b: hàng cũ chưa mã vụ
                            req.ToNC = latC[rnd.Next(latC.Count)];
                            if (rnd.NextDouble() < 0.7) req.NcReason = "da qua NC do"; else expErr = "lý do";
                        }
                        else if (y < 0.25 && latC.Any()) { req.ToNC = latC[rnd.Next(latC.Count)]; req.NcReason = "abc"; expErr = "không bỏ qua NC"; }
                    }
                    var caseNcExp = exp.ToNcByCase ? FormHelp.CaseNcFor(db, req) : null;
                    desc = $"form {g} {req.Kind} {pos.Id} {req.Action} q={req.Qty} p={req.Pass}/{req.Fail} to={req.ToGroup} {req.Purpose} cell={req.ToCell} nc={req.NcNext} tonc={req.ToNC} vu={req.CaseId} prev={req.Prev}:{req.Cause} cat={req.CatId} have={have} over={over} exp={expErr}";
                    var preCells = FormHelp.Cells(row); var preDocs = FormHelp.Docs(db, 1); var preSig = ChuoiHelp.LedgerSig(row);
                    int tx0 = db.HandoverTransactions.List.Count, bl0 = db.HandoverNgReports.List.Count, kq0 = db.HandoverRepairResults.List.Count, vu0 = db.NgCases.List.Count;
                    if (rnd.NextDouble() < 0.03)
                    {   // người không có quyền nhóm → chặn, không ghi
                        var r99 = new FormGiaoRequest { KhPlanDetailId = 1, Group = req.Group, UserId = 99, Kind = req.Kind, Pos = req.Pos, Action = req.Action, Qty = req.Qty, Pass = req.Pass, Fail = req.Fail,
                                                        ToGroup = req.ToGroup, Purpose = req.Purpose, CaseId = req.CaseId, Prev = req.Prev, Cause = req.Cause, CatId = req.CatId, ToCell = req.ToCell, ToNC = req.ToNC, Notes = req.Notes };
                        var d99 = await svc.SaveFormAsync(r99);
                        if (d99.Ok || FormHelp.Docs(db, 1) != preDocs) fails.Add($"[Form quyền] PO{po} op{op} người ngoài nhóm lưu được / ghi dở: {desc}");
                    }
                    var res = await svc.SaveFormAsync(req);
                    ok = res.Ok;
                    if (pos.Id is "LAMHO" or "LAMHOLOI") Inc($"lamho.form.{pos.Id}.{(ok ? "ok" : "rej")}");   // [TARO-LAMHO-NC]
                    var after = (await led.GetLedgerForDetailAsync(1))!;
                    if (!ok)
                    {
                        Inc("rej.form");
                        if (FormHelp.Docs(db, 1) != preDocs || ChuoiHelp.LedgerSig(after) != preSig) fails.Add($"[Form bị chặn mà ghi dở] PO{po} op{op} {desc}: {res.Error} · {preDocs} → {FormHelp.Docs(db, 1)}");
                        if (over && !res.Error.Contains("tối đa") && expErr == null) fails.Add($"[Form vượt trần không bị trần chặn] PO{po} op{op} {desc}: {res.Error}");
                        if (!over && expErr == null && !res.Error.StartsWith("Không lưu")) fails.Add($"[Form trong trần bị từ chối] PO{po} op{op} {desc}: {res.Error}");
                        if (expErr is string ee && ee != "" && !over && !res.Error.Contains(ee)) fails.Add($"[Form chặn sai lời] PO{po} op{op} {desc}: cần \"{ee}\" · {res.Error}");
                        break;
                    }
                    if (over || expErr != null) { fails.Add($"[Form lọt] PO{po} op{op} {desc}"); break; }
                    int dTx = db.HandoverTransactions.List.Count - tx0, dBl = db.HandoverNgReports.List.Count - bl0, dKq = db.HandoverRepairResults.List.Count - kq0, dVu = db.NgCases.List.Count - vu0;
                    if (dTx != exp.Tx || dBl != exp.Bl || dKq != exp.Kq || dVu != exp.Vu)
                        fails.Add($"[Form sai số chứng từ] PO{po} op{op} {desc}: phiếu {dTx}/{exp.Tx} báo lỗi {dBl}/{exp.Bl} KQ {dKq}/{exp.Kq} vụ {dVu}/{exp.Vu}");
                    if (exp.Tx == 1 && dTx == 1)
                    {
                        var t = db.HandoverTransactions.List.Last();
                        if ((exp.Purpose != null && t.Purpose != exp.Purpose) || (exp.FromCell != null && t.FromCell != exp.FromCell) || (exp.ToCell != null && t.ToCell != exp.ToCell))
                            fails.Add($"[Form sai loại phiếu / ô] PO{po} op{op} {desc}: {t.Purpose} {t.FromCell}→{t.ToCell} cần {exp.Purpose} {exp.FromCell}→{exp.ToCell}");
                        if (exp.Bl == 1 && t.NgCaseId != db.HandoverNgReports.List.Last().NgCaseId) fails.Add($"[Form phiếu kèm sai vụ] PO{po} op{op} {desc}");
                    }
                    if (chkNc && dKq == 1)
                    {
                        var kr = db.HandoverRepairResults.List.Last();
                        if (kr.ToNC != expNc || (ncWhy != null && !(kr.Notes ?? "").Contains(ncWhy))) fails.Add($"[Form K2 lưu sai NC] PO{po} op{op} {desc}: {kr.ToNC} cần {expNc} · {kr.Notes}");
                        var mxF = FormHelp.MaxReachIdx(db, 1, kr.NgCaseId ?? 0, "GC");
                        var tiF = kr.ToNC == null ? -1 : fncs.FindIndex(x => x.Equals(kr.ToNC, StringComparison.OrdinalIgnoreCase));
                        if (mxF != null && tiF > mxF) fails.Add($"[Form sửa đạt vào NC sau công đoạn đã tới] PO{po} op{op} {desc}: {kr.ToNC} (#{tiF}) > xa nhất #{mxF}");
                    }
                    var badCells = FormHelp.CheckCells(preCells, FormHelp.Cells(after), exp);
                    if (badCells != null) fails.Add($"[Form ô sổ sai đáp án] PO{po} op{op} {desc}: {badCells}");
                    if (dTx == exp.Tx && dBl == exp.Bl)
                    {   // [GN-FORM1] rà độc lập T4: truy vết (NC phiếu theo vụ, nguồn / nơi gây lỗi / NC của báo lỗi, vụ phát hiện)
                        var tr = FormHelp.CheckTrace(db, exp, g, caseNcExp);
                        if (tr != null) fails.Add($"[Form truy vết sai] PO{po} op{op} {desc}: {tr}");
                        // [GN-FORM1] vòng 2 T3: bất biến không theo luật mặc định — chiếc của vụ không chờ ở NC sau công đoạn xa nhất đã tới
                        if (exp.ToNcByCase && fncs.Count > 0 && dTx == 1)
                        {
                            var tN = db.HandoverTransactions.List.Last();
                            var mx = FormHelp.MaxReachIdx(db, 1, tN.NgCaseId ?? 0, g);
                            var ti = tN.ToNC == null ? -1 : fncs.FindIndex(x => x.Equals(tN.ToNC, StringComparison.OrdinalIgnoreCase));
                            if (mx != null && ti > mx) fails.Add($"[Vụ vào Gia công ở NC sau công đoạn đã tới] PO{po} op{op} {desc}: ToNC {tN.ToNC} (#{ti}) > xa nhất #{mx}");
                        }
                        if (exp.ToNcByCase && fncs.Count > 0 && req.NcReason is "lam lai NC truoc" or "da qua NC do" && !(db.HandoverTransactions.List.Last().Notes ?? "").Contains(req.NcReason))
                            fails.Add($"[Form V3 thiếu lý do lùi NC trong Ghi chú phiếu] PO{po} op{op} {desc}");
                    }
                    break;
                }
                case "xnsua":   // [GC-SUA-TAI-CHO] KCS xác nhận (hàm thật ConfirmRepairAsync)
                {
                    var reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == 1 && !r.IsVoided).ToList();
                    var wait = reps.Select(r => (r, w: r.QtyPass - db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == r.HandoverRepairResultId && !c.IsVoided).Sum(c => c.QtyOk + c.QtyNg)))
                                   .Where(x => x.w > 0).ToList();
                    if (!wait.Any()) { Inc("skip.xnsua"); continue; }
                    var (rp, w) = wait[rnd.Next(wait.Count)];
                    var over = rnd.NextDouble() < 0.1;
                    var tot = over ? w + 1 : rnd.Next(1, (int)w + 1);
                    var ngq = rnd.NextDouble() < 0.35 ? rnd.Next(0, (int)tot + 1) : 0;
                    var okq = tot - ngq;
                    var checker = Uid("KCS") == rp.CreatedBy ? 6 : Uid("KCS");
                    desc = $"xnsua #{rp.HandoverRepairResultId} {rp.GroupCode} dat={okq} loai={ngq} cho={w}";
                    var self = await svc.ConfirmRepairAsync(rp.HandoverRepairResultId, Math.Min(okq, w), 0, null, rp.CreatedBy);
                    if (self.Ok) fails.Add($"[KCS tự xác nhận hàng mình ghi] PO{po} op{op} {desc}");
                    var res = await svc.ConfirmRepairAsync(rp.HandoverRepairResultId, okq, ngq, null, checker);
                    ok = res.Ok; if (!ok) Inc("rej.xnsua");
                    if (over && ok) fails.Add($"[KCS xác nhận vượt số chờ] PO{po} op{op} {desc}");
                    if (over && !ok && !res.Error.Contains("Còn chờ")) fails.Add($"[KCS xác nhận vượt số chờ không bị trần chặn] PO{po} op{op} {desc}: {res.Error}");
                    if (!over && !ok && !res.Error.StartsWith("Không lưu")) fails.Add($"[KCS xác nhận trong số chờ bị từ chối] PO{po} op{op} {desc}: {res.Error}");
                    break;
                }
                case "vkqsua":   // [GC-SUA-TAI-CHO] huỷ Kết quả sửa
                {
                    var reps = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == 1 && !r.IsVoided).ToList();
                    if (!reps.Any()) { Inc("skip.vkqsua"); continue; }
                    var rp = reps[rnd.Next(reps.Count)];
                    var rid = rp.HandoverRepairResultId; var by = rp.CreatedBy;
                    var hasChk = db.HandoverRepairChecks.List.Any(c => c.HandoverRepairResultId == rid && !c.IsVoided);
                    desc = $"vkqsua #{rid} {rp.GroupCode} {rp.SourceCell} dat={rp.QtyPass} khong={rp.QtyFail} coXN={hasChk}";
                    var deny = await svc.VoidRepairResultAsync(rid, "huy thu", 99);
                    if (deny.Ok) fails.Add($"[Quyền huỷ Kết quả sửa] PO{po} op{op} {desc}");
                    var res = await svc.VoidRepairResultAsync(rid, "huy thu", by);
                    ok = res.Ok; if (!ok) Inc("rej.vkqsua");
                    if (hasChk && ok) fails.Add($"[Huỷ Kết quả sửa còn lần KCS xác nhận] PO{po} op{op} {desc}");
                    if (!ok && res.Error.StartsWith("Không lưu"))
                        await WhyCheck(db, svc, led, HandoverWhy.OpVoidRepair(rid), new HashSet<string> { "K:" + rid },
                            async () => (await svc.VoidRepairResultAsync(rid, "thu lai", by)).Ok, null, rnd, po, op, desc, res.Error);
                    break;
                }
                case "vxnsua":   // [GC-SUA-TAI-CHO] huỷ lần KCS xác nhận
                {
                    var chks = db.HandoverRepairChecks.List.Where(c => !c.IsVoided && c.RepairResult != null && c.RepairResult.KhPlanDetailId == 1).ToList();
                    if (!chks.Any()) { Inc("skip.vxnsua"); continue; }
                    var ck = chks[rnd.Next(chks.Count)];
                    var cid = ck.HandoverRepairCheckId; var by = ck.CheckedBy;
                    desc = $"vxnsua #{cid} (KQ #{ck.HandoverRepairResultId} {ck.RepairResult.GroupCode}) dat={ck.QtyOk} loai={ck.QtyNg}";
                    var deny = await svc.VoidRepairCheckAsync(cid, "huy thu", 99);
                    if (deny.Ok) fails.Add($"[Quyền huỷ KCS xác nhận] PO{po} op{op} {desc}");
                    var res = await svc.VoidRepairCheckAsync(cid, "huy thu", by);
                    ok = res.Ok; if (!ok) Inc("rej.vxnsua");
                    if (!ok && res.Error.StartsWith("Không lưu"))
                        await WhyCheck(db, svc, led, HandoverWhy.OpVoidRepairCheck(cid), new HashSet<string> { "X:" + cid },
                            async () => (await svc.VoidRepairCheckAsync(cid, "thu lai", by)).Ok, null, rnd, po, op, desc, res.Error, loop: true);
                    break;
                }
                case "vngrep":   // [GN-NG-AFTER] huỷ báo lỗi
                {
                    var bs = db.HandoverNgReports.List.Where(b => b.KhPlanDetailId == 1 && !b.IsVoided).ToList();   // [CHUOI] chỉ PO 1
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
                            async () => (await kho.SaveKhoInputAsync(1, DateTime.Now, nq, null, null, null, null, null, "thu lai", 1)).Success, null, rnd, po, op, desc, res.Error, loop: true);
                    break;
                }
                case "pmedit":   // [CHUOI] sửa Part Master giữa chừng bằng hàm thật: snapshot + sổ PO đang chạy KHÔNG được đổi
                {
                    var before = ChuoiHelp.LedgerSig(row);
                    var pmBefore = ChuoiHelp.Picture(db, pmId).Sig;
                    var what = await ChuoiHelp.MutateAsync(db, rnd, pmId);
                    desc = "pmedit " + what;
                    if (what == "bỏ qua") { Inc("skip.pmedit"); continue; }
                    if (what.StartsWith("bị chặn"))   // [PM-NC-DUP-SVC] mã chính trùng bị service chặn: Part Master + snapshot + sổ không đổi
                    {
                        Inc("pmedit.chan.matrung");
                        if (ChuoiHelp.Picture(db, pmId).Sig != pmBefore) fails.Add($"[Mã trùng bị chặn mà Part Master vẫn đổi] PO{po} op{op} {desc}");
                        if (ChuoiHelp.LedgerSig((await led.GetLedgerForDetailAsync(1))!) != before) fails.Add($"[Mã trùng bị chặn mà sổ PO đổi] PO{po} op{op} {desc}");
                        if (ChuoiHelp.SnapPicture(db, 1).Sig != snapSig0) fails.Add($"[Mã trùng bị chặn mà snapshot PO đổi] PO{po} op{op} {desc}");
                        if (db.Database.Current != null) { fails.Add($"[Giao dịch treo] PO{po} op{op} {desc}"); db.Database.Current = null; }   // (rà độc lập K1: nhánh này continue trước chỗ kiểm chung)
                        continue;
                    }
                    if (ChuoiHelp.Picture(db, pmId).Sig == pmBefore) fails.Add($"[Sửa Part Master không có tác dụng] PO{po} op{op} {desc}");
                    if (ChuoiHelp.SnapPicture(db, 1).Sig != snapSig0) fails.Add($"[Sửa Part Master làm đổi snapshot PO] PO{po} op{op} {desc}");
                    var after = ChuoiHelp.LedgerSig((await led.GetLedgerForDetailAsync(1))!);
                    if (after != before) fails.Add($"[Sửa Part Master làm đổi sổ PO] PO{po} op{op} {desc}\n   {before}\n   {after}");
                    // [K6-NOTRUN 30/10] như bấm "Lưu & Xác nhận OK": PO chưa chạy tự lấy quy trình mới (Master list mục 13)
                    if (Environment.GetEnvironmentVariable("AUTORT") != "0")
                    {
                        var rowB = (await led.GetLedgerForDetailAsync(1))!;
                        var pmF = db.PartMasters.List.First(x => x.PartId == pmId);   // như bấm "Lưu & Xác nhận OK"
                        pmF.IsPlanConfirmed = pmF.IsMachiningConfirmed = pmF.IsHtspConfirmed = pmF.IsKcsConfirmed = pmF.IsPkgConfirmed = true;
                        var ar = await new KhPlanService(db).AutoRefreshRoutesForPartAsync(pmId, 1, "fuzz luu part master");
                        if (ar.Any(r => r.Status == KhPlanService.AutoRouteResult.Error)) fails.Add($"[Tự cập nhật lỗi hệ thống] PO{po} op{op}: {string.Join("; ", ar.Where(r => r.Status == KhPlanService.AutoRouteResult.Error).Select(r => r.Note))}");
                        db.Fixup();
                        var a1 = ar.FirstOrDefault(r => r.KhPlanDetailId == 1);
                        var hasWts = db.WtsProductionLogs.List.Any(w => w.KhPlanDetailId == 1 && !w.IsVoided);
                        if (a1 != null && a1.Status == KhPlanService.AutoRouteResult.Updated)
                        {
                            Inc("pmedit.auto.upd");
                            if (hasWts) fails.Add($"[Tự cập nhật quy trình PO đã có WTS] PO{po} op{op} {desc}");
                            var dd = ChuoiHelp.Diff(db, 1, ChuoiHelp.Picture(db, pmId), pmId);
                            if (dd != null) fails.Add($"[Tự cập nhật: snapshot ≠ Part Master] PO{po} op{op} {desc}: {dd}");
                            var rowA = (await led.GetLedgerForDetailAsync(1))!;
                            var neg = HandoverLedgerService.NewNegatives(rowB, rowA);
                            if (!rowA.Balanced || neg.Any() || rowA.G("GC").ChuaLam != rowB.G("GC").ChuaLam || rowA.G("KHO").ConGiao != rowB.G("KHO").ConGiao || rowA.TonTong != rowB.TonTong)
                                fails.Add($"[Tự cập nhật làm đổi sổ PO] PO{po} op{op} {desc}: GC chưa {rowB.G("GC").ChuaLam}→{rowA.G("GC").ChuaLam} · Kho còn {rowB.G("KHO").ConGiao}→{rowA.G("KHO").ConGiao} · âm {string.Join(";", neg)}");
                            // [K6-NOTRUN] hàng đã cấp vào NC chỉ định không được trôi sang NC khác
                            foreach (var pin in db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.ToGroupCode == "GC" && !string.IsNullOrEmpty(t.ToNC)).Select(t => t.ToNC!).Distinct())
                            {
                                var sb = rowB.G("GC").Steps.FirstOrDefault(x => x.Pg == "GC" && x.Code == pin); var sa = rowA.G("GC").Steps.FirstOrDefault(x => x.Pg == "GC" && x.Code == pin);
                                if (sa == null || sa.IsExcluded || (sa.Waiting ?? 0) != (sb?.Waiting ?? 0))
                                    fails.Add($"[Tự cập nhật dời hàng NC {pin}] PO{po} op{op} {desc}: chờ {sb?.Waiting} → {sa?.Waiting}{(sa?.IsExcluded == true ? " (loại trừ)" : "")}");
                            }
                            snapSig0 = ChuoiHelp.SnapPicture(db, 1).Sig;
                            (ncs, codeOf) = ChuoiHelp.CodesFromSnapshot(db, 1);
                        }
                        else
                        {
                            Inc(a1?.Status == KhPlanService.AutoRouteResult.Same ? "pmedit.auto.same" : "pmedit.auto.kept");
                            if (ChuoiHelp.SnapPicture(db, 1).Sig != snapSig0) fails.Add($"[Tự cập nhật bị chặn mà snapshot vẫn đổi] PO{po} op{op} {desc}");
                        }
                        if (nbId > 0 && ar.Any(r => r.KhPlanDetailId == nbId && r.Status == KhPlanService.AutoRouteResult.Updated))
                            fails.Add($"[Tự cập nhật PO hàng xóm đã chạy] PO{po} op{op}");
                    }
                    ok = true;
                    break;
                }
                case "close":
                {
#if NEWAPI
                    var ts = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Status == "PARTIAL").ToList();   // [CHUOI] chỉ PO 1
                    if (!ts.Any()) { Inc("skip.close"); continue; }
                    var t = ts[rnd.Next(ts.Count)];
                    desc = $"close #{t.HandoverTxId} {t.FromGroupCode}->{t.ToGroupCode} issued={t.QtyIssued} auto={t.SourceReceiveId}";
                    var closedBefore = t.QtyClosed ?? 0;   // rà độc lập T1: đọc TRƯỚC khi gọi (đối tượng EF giả bị sửa tại chỗ)
                    var deny = await svc.CloseRemainderAsync(t.HandoverTxId, "huy phan con", 99, groups.Where(x => x != t.FromGroupCode).ToList());
                    if (deny.Ok) fails.Add($"[Quyền] PO{po} op{op} người ngoài nhóm giao huỷ được phần chưa nhận #{t.HandoverTxId}");
                    var res = await svc.CloseRemainderAsync(t.HandoverTxId, "huy phan con", Uid(t.FromGroupCode), new List<string> { t.FromGroupCode });
                    ok = res.Ok; if (!ok) Inc("rej.close");
                    // [K5-CLOSE-NHIEU 2026-11-06] ĐÃ SỬA (bảng HandoverTxCloses, mỗi lần huỷ 1 dòng): bỏ đếm riêng ghinhan.K5-CLOSE-NHIEU → bị chặn thì
                    //   kiểm như mọi thao tác (lời khuyên phải gỡ được). Huỷ lần ≥ 2 đếm riêng để biết fuzz có đi qua đường này.
                    if (closedBefore > 0) Inc(ok ? "close.lan2.ok" : "close.lan2.rej");
                    // rà độc lập T1: fuzz gần như không tự đi qua "huỷ nhiều lần" → 35%: huỷ 1 lần nhận (không NG) của phiếu vừa đóng rồi huỷ phần chưa nhận lần nữa
                    if (ok && t.SourceReceiveId == null && rnd.NextDouble() < 0.35)
                    {
                        db.Fixup();
                        var rv = t.Receives.Where(x => !x.IsVoided && x.QtyNg == 0).OrderBy(_ => rnd.Next()).FirstOrDefault();
                        if (rv != null && t.Receives.Count(x => !x.IsVoided) >= 2)
                        {
                            var vr2 = await svc.VoidReceiveAsync(rv.ReceiveId, "nhan nham", Uid(t.ToGroupCode), All);
                            db.Fixup();
                            if (vr2.Ok)
                            {
                                var tid2 = t.HandoverTxId; var fg2 = t.FromGroupCode;
                                var c2 = await svc.CloseRemainderAsync(tid2, "huy lan nua", Uid(fg2), new List<string> { fg2 });
                                Inc(c2.Ok ? "close.chuoi.ok" : "close.chuoi.rej");
                                if (!c2.Ok && c2.Error.StartsWith("Không lưu"))
                                    await WhyCheck(db, svc, led, HandoverWhy.OpCloseRemainder(tid2), new HashSet<string> { "T:" + tid2 },
                                        async () => (await svc.CloseRemainderAsync(tid2, "thu lai", Uid(fg2), All)).Ok, null, rnd, po, op, desc + " (huỷ lần 2)", c2.Error, loop: true);
                                desc += " +vrecv +close2";
                            }
                        }
                    }
                    if (!ok && res.Error.StartsWith("Không lưu"))
                    {
                        var tid = t.HandoverTxId; var fg = t.FromGroupCode;
                        await WhyCheck(db, svc, led, HandoverWhy.OpCloseRemainder(tid), new HashSet<string> { "T:" + tid },
                            async () => (await svc.CloseRemainderAsync(tid, "thu lai", Uid(fg), All)).Ok, null, rnd, po, op, desc, res.Error, loop: true);
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
    if (Environment.GetEnvironmentVariable("TRACEPO") == po.ToString()) Console.WriteLine($"   [TRACE] op{op} {desc}");   // [FIX-1028] in từng thao tác thành công của 1 PO để tái hiện lỗi
        await CheckAll(db, led, po, op, desc);
        if (whyAll) await WhyAllCheck(db, led, po, op, desc);   // [H10] WHYALL=1: thử tìm đường gỡ cho MỌI lần nhận / phiếu (chỉ tính trên bản sao, không ghi)
        if (chuoi)
        {   // [CHUOI] snapshot PO đang chạy không đổi; PO hàng xóm cùng phiếu KH: sổ + số chứng từ y nguyên; chứng từ mới chỉ thuộc PO 1
            checks++;
            if (ChuoiHelp.SnapPicture(db, 1).Sig != snapSig0) fails.Add($"[Snapshot PO đổi giữa chừng] PO{po} op{op} sau «{desc}»");
            if (nbId > 0)
            {
                checks++;
                var nbRow = (await led.GetLedgerForDetailAsync(nbId))!;
                if (ChuoiHelp.LedgerSig(nbRow) != sigNb) fails.Add($"[Sổ PO hàng xóm đổi] PO{po} op{op} sau «{desc}»: {ChuoiHelp.LedgerSig(nbRow)} ≠ {sigNb}");
                checks++;
                var nbNow = db.HandoverTransactions.List.Count(t => t.KhPlanDetailId == nbId) + db.HandoverReceives.List.Count(r => r.Transaction.KhPlanDetailId == nbId) + db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == nbId) + db.NgCases.List.Count(x => x.KhPlanDetailId == nbId) + db.HandoverNgReports.List.Count(b => b.KhPlanDetailId == nbId);
                if (nbNow != nbDocs) fails.Add($"[Chứng từ lẫn sang PO hàng xóm] PO{po} op{op} sau «{desc}»: {nbNow} ≠ {nbDocs}");
                checks++;
                if (db.HandoverNgReports.List.Any(b => b.KhPlanDetailId != 1) || db.NgCases.List.Any(c => c.KhPlanDetailId != 1 && c.KhPlanDetailId != nbId))
                    fails.Add($"[Báo lỗi / vụ lỗi sai PO] PO{po} op{op} sau «{desc}»");
            }
        }
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
if (whyAll) Console.WriteLine($"  [H10] thử huỷ mọi lần nhận: gỡ được {(stats.TryGetValue("h10.ok", out var h1) ? h1 : 0)} · hết trần tìm nhưng có đường {(stats.TryGetValue("h10.hettran", out var h2) ? h2 : 0)} (dài nhất {h10MaxSteps} bước) · kẹt {(stats.TryGetValue("h10.ket", out var h3) ? h3 : 0)}");
foreach (var kv in stats.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Key,-20} {kv.Value}");
Console.WriteLine($"Phép kiểm: {checks}");
// [ISO-FIX 2026-10-31] Chạy ngẫu nhiên cũng kiểm: không hàm nào của phần mềm xin giao dịch Serializable (rò qua kết nối pool trên SQL thật)
Console.WriteLine($"Giao dịch: mặc định {Microsoft.EntityFrameworkCore.DatabaseFacade.IsoDefault} · Serializable {Microsoft.EntityFrameworkCore.DatabaseFacade.IsoSerializable} · mức khác {Microsoft.EntityFrameworkCore.DatabaseFacade.IsoOther}");
if (Microsoft.EntityFrameworkCore.DatabaseFacade.IsoSerializable + Microsoft.EntityFrameworkCore.DatabaseFacade.IsoOther > 0)
    fails.Add($"[ISO-FIX giao dịch xin mức cô lập riêng] Serializable {Microsoft.EntityFrameworkCore.DatabaseFacade.IsoSerializable} · khác {Microsoft.EntityFrameworkCore.DatabaseFacade.IsoOther}");
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
    // [K5-CLOSE-NHIEU 2026-11-06] mỗi phiếu: Σ dòng HandoverTxCloses = QtyClosed (code mới luôn ghi cả 2 cùng giao dịch); mỗi dòng > 0;
    //   số dòng = số lần huỷ thành công (đếm ở thao tác close) — đọc thẳng bảng, độc lập với sổ
    foreach (var t in db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1))
    {
        checks++;
        var rows = db.HandoverTxCloses.List.Where(c => c.HandoverTxId == t.HandoverTxId).ToList();
        if (rows.Sum(c => c.Qty) != (t.QtyClosed ?? 0) || rows.Any(c => c.Qty <= 0))
            F("Huỷ phần chưa nhận ≠ bảng từng lần", $"#{t.HandoverTxId}: phiếu {t.QtyClosed} · bảng {string.Join("+", rows.Select(c => c.Qty))}");
        if (rows.Count > 0 && t.ClosedAt != rows.Max(c => c.ClosedAt))
            F("Giờ huỷ phần chưa nhận lệch", $"#{t.HandoverTxId}: phiếu {t.ClosedAt:HH:mm:ss.fff} · dòng cuối {rows.Max(c => c.ClosedAt):HH:mm:ss.fff}");
    }
    // [GC-SUA-TAI-CHO] Cảnh báo tự kiểm của sổ (Tồn ≠ tổng các phần — gồm Chờ KCS; vụ ≠ ô) không bao giờ được hiện
    foreach (var G in row.Groups.Values)
        foreach (var w in G.Warnings.Where(w => w.Contains("báo lại để kiểm")))
        { checks++; F("Sổ tự kiểm lệch", w); }
    // [GC-SUA-TAI-CHO] Chờ KCS của mỗi nhóm = Σ (đạt − đã xác nhận) các Kết quả sửa còn hiệu lực (đếm thẳng từ bảng, độc lập với sổ)
    foreach (var G in row.Groups.Values)
    {
        checks++;
        var ck = db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == 1 && !r.IsVoided && r.GroupCode == G.Code)
            .Sum(r => r.QtyPass - db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == r.HandoverRepairResultId && !c.IsVoided).Sum(c => c.QtyOk + c.QtyNg));
        if (ck != G.ChoKcs) F("Chờ KCS ≠ bảng", $"{G.Code}: sổ {G.ChoKcs} · bảng {ck}");
        if (G.ChoKcs < 0) F("Chờ KCS âm", $"{G.Code}: {G.ChoKcs}");
    }
    // [WTS-PANEL-QTY 2026-10-31] Số hiện trên bảng quy trình trang WTS + tab KH (LedgerQtyText) = cộng OK / NG đúng từng dòng WTS
    //   (gồm dòng NC / mã dự phòng gộp vào mã chính), cùng công thức OK dòng cũ (QtyDone − NG) mà 2 trang dùng cho cột OK / NG.
    {
        var wl = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == 1 && !w.IsVoided).ToList();
        // [TARO-NC-MA 2026-11-06] đáp án độc lập (đọc thẳng bảng snapshot Taro, không dùng row.TaroNcs): mã Taro dòng chính dùng ở nhiều NC
        //   → dòng WTS thuộc cặp (mã, StepNC); thiếu / sai NC tính vào NC đầu (thứ tự quy trình) — luật câu B3 Hùng 06/11
        var tSnap = db.KhPlanRouteSnapshotTaro.List.Where(t => t.KhPlanDetailId == 1 && !t.IsBackup).OrderBy(t => t.StepOrder).ToList();
        string TCode(KhPlanRouteSnapshotTaro t) => (string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode)!.Trim();
        var tShared = tSnap.Where(t => !HandoverLedgerService.IsPrepCode(TCode(t))).GroupBy(TCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Ncs: g.Select(t => (t.NC ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(g => g.Ncs.Count > 1).ToDictionary(g => g.Key, g => g.Ncs, StringComparer.OrdinalIgnoreCase);
        string? WKey(WtsProductionLog w)
        {
            var c = (w.ProcessGroup == "GC" ? w.NC : (w.WtsCode ?? w.NC))?.Trim();
            if (w.ProcessGroup != "TARO" || c == null || !tShared.TryGetValue(c, out var ncs)) return c;
            var n = ncs.FirstOrDefault(x => string.Equals(x, (w.StepNC ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? ncs[0];
            return $"{c} (NC {n})";
        }
        foreach (var G in row.Groups.Values)
            foreach (var s in G.Steps.Where(s => !s.IsPrep && !s.IsExcluded))
            {
                checks++;
                var mine = wl.Where(w => w.ProcessGroup == s.Pg && string.Equals(
                    row.CodeMain.TryGetValue(s.Pg + "|" + WKey(w), out var mm) ? mm : WKey(w),
                    s.Code, StringComparison.OrdinalIgnoreCase)).ToList();
                var o = mine.Sum(LedgerQtyText.LogOk); var g = mine.Sum(LedgerQtyText.LogNg);
                if (o != s.Ok || g != s.Ng) F("Bảng quy trình ≠ dòng WTS", $"{s.Pg} {s.Code}: sổ OK {s.Ok} NG {s.Ng} · cộng dòng WTS OK {o} NG {g}");
                checks++;
                var t = LedgerQtyText.Text(LedgerQtyText.Step(row, s.Pg, s.Code));
                if (t != $"OK {s.Ok:0.##} / vào {(s.In ?? 0):0.##}" + (s.Ng > 0 ? $" · NG {s.Ng:0.##}" : "")) F("Chữ badge ≠ sổ", $"{s.Pg} {s.Code}: {t}");
            }
    }
    // [TARO-NC-MA][TARO-3T-GOP 2026-11-06] HTSP Đã làm tính ĐỘC LẬP từ bảng (snapshot Taro / Bavia / Rửa + khai báo gộp chụp vào PO + dòng WTS):
    //   đơn vị = mã thường (dòng chính, không mã chuẩn bị) hoặc bộ gộp (mã gộp + mã thành phần cùng NC) → OK bộ = OK mã gộp + min OK thành phần;
    //   Đã làm = min các đơn vị. Bộ gộp: chiếc đã vào bộ (OK+NG mã gộp + max OK+NG thành phần) ≤ hàng thật đã nhận vào HTSP (Σ lần nhận).
    {
        var wlH = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == 1 && !w.IsVoided).ToList();
        var tS = db.KhPlanRouteSnapshotTaro.List.Where(t => t.KhPlanDetailId == 1).OrderBy(t => t.StepOrder).ToList();
        var bS = db.KhPlanRouteSnapshotBavia.List.Where(t => t.KhPlanDetailId == 1).ToList();
        var rS = db.KhPlanRouteSnapshotWashing.List.Where(t => t.KhPlanDetailId == 1).ToList();
        if (tS.Any(t => !t.IsBackup) || bS.Any(t => !t.IsBackup) || rS.Any(t => !t.IsBackup))
        {
            string Tc(KhPlanRouteSnapshotTaro t) => (string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode)!.Trim();
            static bool Eq(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            // [TARO-LAMHO-NC] NC máy loại trừ GIỮA chuỗi Gia công (sau nó còn NC thường) — đọc chuỗi NC Gia công (thứ tự + cờ máy loại trừ),
            //   so NC Taro bỏ chữ "NC" / số 0 đầu: mã Taro ở đó là Taro làm hộ → không vào Đã làm của HTSP, OK bộ của nó so riêng với sổ
            static string Nk(string? x) { var y = (x ?? "").Trim(); if (y.StartsWith("NC", StringComparison.OrdinalIgnoreCase)) y = y[2..]; return y.Replace(" ", "").TrimStart('0').ToUpperInvariant(); }
            var gcL = row.G("GC").Steps.Where(x => !HandoverLedgerService.IsVao(x.Code)).ToList();
            var lhNcs = gcL.Select((x, i) => (x, i)).Where(z => z.x.IsExcluded && gcL.Skip(z.i + 1).Any(y => !y.IsExcluded)).Select(z => Nk(z.x.Code)).ToHashSet();
            var mainsAll = tS.Where(t => !t.IsBackup && !HandoverLedgerService.IsPrepCode(Tc(t))).ToList();
            var mainsT = mainsAll.Where(t => !lhNcs.Contains(Nk(t.NC))).ToList();
            var shared = mainsAll.GroupBy(Tc, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(t => (t.NC ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                               .ToDictionary(g => g.Key, g => g.OrderBy(t => t.StepOrder).Select(t => (t.NC ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
            // dòng WTS Taro thuộc dòng quy trình nào (mã + NC; dự phòng gộp về dòng cha cùng NC)
            (decimal ok, decimal ng) Done(KhPlanRouteSnapshotTaro m)
            {
                var code = Tc(m); var nc = (m.NC ?? "").Trim();
                var codes = new List<string> { code };
                codes.AddRange(tS.Where(b => b.IsBackup && Eq(b.ParentNC, m.NC) && tS.FirstOrDefault(x => !x.IsBackup && Eq(x.NC, b.ParentNC)) == m).Select(Tc));
                var rows = wlH.Where(w => w.ProcessGroup == "TARO" && codes.Contains((w.WtsCode ?? w.NC ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    && (!shared.TryGetValue((w.WtsCode ?? w.NC ?? "").Trim(), out var ncs) || Eq(ncs.FirstOrDefault(x => Eq(x, w.StepNC)) ?? ncs[0], nc))).ToList();
                return (rows.Sum(LedgerQtyText.LogOk), rows.Sum(LedgerQtyText.LogNg));
            }
            var gopsS = db.KhPlanRouteSnapshotWtsGops.List.Where(g => g.KhPlanDetailId == 1).ToList();
            var used = new HashSet<KhPlanRouteSnapshotTaro>(ReferenceEqualityComparer.Instance);
            var units = new List<decimal>();
            decimal recvH = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && t.ToGroupCode == "HTSP" && !t.IsVoided)
                              .SelectMany(t => t.Receives.Where(r => !r.IsVoided)).Sum(r => r.QtyOk + r.QtyNg)
                            // + chiếc HTSP sửa tại chỗ ĐẠT đã được KCS xác nhận: quay lại HTSP ô Hàng lỗi đã xử lý, làm lại các mã như hàng vào mới ([GC-SUA-TAI-CHO] — không phải lần nhận phiếu)
                            //   (fuzz seed 77 PO16 op112: 18 nhận + 1 chiếc sửa đạt → 8G1 NC T9 làm 19 là đúng; bản đầu bộ kiểm chỉ cộng lần nhận → báo oan)
                            + db.HandoverRepairChecks.List.Where(c => !c.IsVoided && db.HandoverRepairResults.List.Any(r => r.HandoverRepairResultId == c.HandoverRepairResultId
                                  && !r.IsVoided && r.KhPlanDetailId == 1 && r.GroupCode == "HTSP")).Sum(c => c.QtyOk);
            foreach (var g in mainsT)
            {
                var pc = gopsS.Where(x => Eq(x.GopCode, Tc(g))).Select(x => x.PartCode.Trim()).ToList();
                var parts = pc.Count == 0 ? new() : mainsT.Where(p => p != g && Eq(p.NC, g.NC) && pc.Contains(Tc(p), StringComparer.OrdinalIgnoreCase)).ToList();
                if (parts.Count == 0 || used.Contains(g) || parts.Any(used.Contains)) continue;
                used.Add(g); foreach (var p in parts) used.Add(p);
                var dg = Done(g); var dps = parts.Select(Done).ToList();
                units.Add(dg.ok + dps.Min(x => x.ok));
                checks++;
                if (dg.ok + dg.ng + dps.Max(x => x.ok + x.ng) > recvH)
                    F("TARO-3T-GOP bộ gộp vượt hàng thật", $"{Tc(g)} NC {g.NC}: gộp {dg.ok + dg.ng} + thành phần {dps.Max(x => x.ok + x.ng)} > đã nhận vào HTSP {recvH}");
            }
            foreach (var m in mainsT.Where(m => !used.Contains(m))) units.Add(Done(m).ok);
            foreach (var (pgB, list) in new[] { ("BAVIA", bS.Where(x => !x.IsBackup).Select(x => (x.NC, x.IsBackup)).ToList()), ("WASHING", rS.Where(x => !x.IsBackup).Select(x => (x.NC, x.IsBackup)).ToList()) })
                foreach (var m in list.Where(m => !HandoverLedgerService.IsPrepCode(m.NC)))
                {
                    var bk = (pgB == "BAVIA" ? bS.Where(b => b.IsBackup && Eq(b.ParentNC, m.NC)).Select(b => b.NC) : rS.Where(b => b.IsBackup && Eq(b.ParentNC, m.NC)).Select(b => b.NC)).Append(m.NC).ToList();
                    units.Add(wlH.Where(w => w.ProcessGroup == pgB && bk.Contains((w.WtsCode ?? w.NC ?? "").Trim(), StringComparer.OrdinalIgnoreCase)).Sum(LedgerQtyText.LogOk));
                }
            // [TARO-LAMHO-NC] OK bộ Taro làm hộ từng NC loại trừ giữa chuỗi (cùng luật: mã thường / bộ gộp cùng NC) = sổ (LamHoRuns.Done = nhỏ nhất các NC của đoạn)
            foreach (var nk in lhNcs)
            {
                var ms = mainsAll.Where(t => Nk(t.NC) == nk).ToList();
                if (ms.Count == 0) continue;
                var u2 = new List<decimal>(); var used2 = new HashSet<KhPlanRouteSnapshotTaro>(ReferenceEqualityComparer.Instance);
                foreach (var g in ms)
                {
                    var pc = gopsS.Where(x => Eq(x.GopCode, Tc(g))).Select(x => x.PartCode.Trim()).ToList();
                    var parts = pc.Count == 0 ? new() : ms.Where(q => q != g && pc.Contains(Tc(q), StringComparer.OrdinalIgnoreCase)).ToList();
                    if (parts.Count == 0 || used2.Contains(g) || parts.Any(used2.Contains)) continue;
                    used2.Add(g); foreach (var q in parts) used2.Add(q);
                    u2.Add(Done(g).ok + parts.Select(Done).Min(x => x.ok));
                }
                foreach (var m in ms.Where(m => !used2.Contains(m))) u2.Add(Done(m).ok);
                var run = row.LamHoRuns.FirstOrDefault(r => r.Ncs.Any(n => Nk(n) == nk));
                checks++;
                if (run == null || !run.HasTaro) { F("TARO-LAMHO-NC thiếu đoạn làm hộ", $"NC Taro {nk} ở máy loại trừ giữa chuỗi mà sổ không có đoạn"); continue; }
                if (run.Ncs.Count == 1 && run.Done != u2.Min()) F("TARO-LAMHO-NC OK bộ ≠ đáp án độc lập", $"NC {nk}: sổ {run.Done} · tính tay {u2.Min()}");
                // đã trả hàng tốt về NC sau đoạn (đọc thẳng phiếu) = sổ
                var retT = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.FromGroupCode == "HTSP" && t.Purpose == HandoverPurpose.HelpReturn
                                                                && t.FromCell != HandoverCell.Loi && string.Equals(t.ToNC, run.NextNc, StringComparison.OrdinalIgnoreCase)).Sum(t => { var rv = t.Receives.Where(r => !r.IsVoided).Sum(r => r.QtyOk + r.QtyNg); return t.Status == "COMPLETED" ? rv : Math.Max(t.QtyIssued, rv); });   // số giao của phiếu (phiếu đã đủ: số đã nhận)
                checks++;
                if (retT != run.Ret) F("TARO-LAMHO-NC đã trả ≠ phiếu", $"đoạn {run.Key}: sổ {run.Ret} · phiếu {retT}");
                checks++;
                if (row.ApplyWorkflow && run.Ret > run.Done) F("TARO-LAMHO-NC trả về vượt OK bộ (câu 1)", $"đoạn {run.Key}: đã trả {run.Ret} > OK bộ {run.Done}");
            }
            checks++;
            var expDaLam = units.Count == 0 ? row.G("HTSP").DaLam : units.Min();   // không có mã thường nào (chỉ mã chuẩn bị) → sổ cộng mọi mã, không xét ở đây
            if (row.G("HTSP").DaLam != expDaLam) F("HTSP Đã làm ≠ đáp án độc lập (TARO-NC-MA / 3T gộp)", $"sổ {row.G("HTSP").DaLam} · tính tay {expDaLam}");
        }
    }
    // GC: Σ chờ các NC = Chưa GC
    var gc = row.G("GC");
    var gcLines = gc.Steps.Where(s => !s.IsExcluded && s.Waiting.HasValue).ToList();
    // [N3-FIRST 2026-11-04] siết: chỉ bỏ qua khi có máy loại trừ GIỮA 2 NC thường (hàng OK NC trước chờ gửi làm hộ không thuộc dòng nào).
    //   Máy loại trừ đầu chuỗi (dòng @VAO giữ hàng chờ gửi) / cuối chuỗi: Σ chờ các dòng phải = Chưa GC. Trước: bỏ qua mọi PO có máy loại trừ.
    bool exclMid = false;
    { bool seenNormal = false, seenExclAfterNormal = false;
      foreach (var s in gc.Steps) { if (s.IsExcluded) { if (seenNormal) seenExclAfterNormal = true; } else if (!HandoverLedgerService.IsVao(s.Code)) { if (seenExclAfterNormal) exclMid = true; seenNormal = true; } } }
    if (gcLines.Any() && !exclMid)
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
            if (L.Ton != G.Ton || L.ChuaLam != G.ChuaLam || L.ConGiao != G.ConGiao || L.LoiGiu != G.LoiGiu || L.SuaGiu != G.SuaGiu
                || L.ChoKcs != G.ChoKcs   // [GC-SUA-TAI-CHO] rà độc lập T7: + Chờ KCS + chờ từng NC / mã CV
                || string.Join(",", L.Steps.Select(x => x.Pg + x.Code + "=" + x.Waiting)) != string.Join(",", G.Steps.Select(x => x.Pg + x.Code + "=" + x.Waiting)))
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
        var held = row.Groups.Values.Sum(g => g.LoiGiu + g.SuaGiu + row.LamHoNgHeld(g.Code));   // [TARO-LAMHO-NC] + chiếc làm hộ bị lỗi (nằm trong Tồn hàng giao theo NC)
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
            // [LAMHO-NHO-SUA 2026-11-08] vụ Taro làm hộ bị lỗi không bao giờ nằm ở ô Hàng sửa nhóm khác Gia công (chỉ Gia công sửa) — đáp án độc lập: đọc vụ + nhóm
            if (G.Code != "GC" && row.ApplyWorkflow)   // Workflow OFF: sửa dòng WTS đổi vụ thường đã đi thành vụ làm hộ được (GOI9-OFF-CANHBAO, chỉ cảnh báo)
            {
                checks++;
                var lhSua = row.Cases.Values.Where(x => x.IsLamHo && x.HeldAt(G.Code, HandoverCell.Sua) > 0).Select(x => x.No).ToList();
                if (lhSua.Any()) F("[LAMHO-NHO-SUA] vụ làm hộ ở ô Hàng sửa nhóm khác Gia công", $"{G.Code}: {string.Join(", ", lhSua)}");
                var lhTx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.ToGroupCode == G.Code && t.ToCell == HandoverCell.Sua
                                                                  && t.NgCaseId is long lc && row.Cases.TryGetValue(lc, out var lcC) && lcC.IsLamHo).Select(t => "#" + t.HandoverTxId).ToList();
                if (lhTx.Any()) F("[LAMHO-NHO-SUA] phiếu vụ làm hộ chỉ định ô Hàng sửa nhóm khác Gia công", $"{G.Code}: {string.Join(", ", lhTx)}");
            }
        }
        {   // [LAMHO-NHAN-NG 2026-11-08] đáp án độc lập: vụ "NG lúc nhận phiếu theo NC" = vụ của lần nhận còn hiệu lực có NG trên phiếu giao làm hộ / trả hàng làm hộ (đọc DB)
            checks++;
            var helpTx2 = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn && t.FromCell != HandoverCell.Loi).Select(t => t.HandoverTxId).ToHashSet();   // trừ phiếu trả chiếc làm hộ bị lỗi (mang vụ)
            var expNhan = db.HandoverReceives.List.Where(r => !r.IsVoided && r.QtyNg > 0 && r.NgCaseId != null && helpTx2.Contains(r.HandoverTxId)).Select(r => r.NgCaseId!.Value).ToHashSet();
            var lech = row.Cases.Values.Where(x => x.Id > 0 && x.LamHoNhan != expNhan.Contains(x.Id)).Select(x => $"{x.No} sổ {x.LamHoNhan}").ToList();
            if (lech.Any()) F("[LAMHO-NHAN-NG] cờ vụ NG lúc nhận phiếu theo NC ≠ đáp án", string.Join(", ", lech));
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
    foreach (var t in db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided))   // [CHUOI] chỉ PO 1
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
    foreach (var r in db.HandoverReceives.List.Where(r => r.Transaction.KhPlanDetailId == 1 && !r.IsVoided && !r.Transaction.IsVoided && r.QtyNg > 0))   // [CHUOI] chỉ PO 1
    {
        var cell = r.ToCell;
        if (cell == HandoverCell.Loi || r.Transaction.SourceReceiveId != null) continue;
        checks++;
        var n = db.HandoverTransactions.List.Count(t => !t.IsVoided && t.SourceReceiveId == r.ReceiveId);
        if (n != 1) F("Thiếu phiếu trả tự lập", $"lần nhận #{r.ReceiveId} NG {r.QtyNg} có {n} phiếu trả");
    }
}

// [LAMHO-NHAN-NG 2026-11-08] Đáp án độc lập "vụ làm hộ" (đọc DB, không đọc cờ sổ): vụ có chặng phát sinh ở ô LAMHO (WTS Taro làm hộ NG — chặng của sổ)
//   HOẶC vụ của lần nhận còn hiệu lực có NG trên phiếu theo NC (giao làm hộ / trả hàng làm hộ), trừ phiếu trả chiếc làm hộ bị lỗi (ô Hàng lỗi, mang vụ).
static bool LamHoVuDoc(MES.Web.Data.AppDbContext db, LedgerCase x)
{
    if (x.Id <= 0) return false;
    if (x.Hops.Any(h => h.Kind == "PS" && h.Cell == HandoverCell.LamHo)) return true;
    var helpTx = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == 1 && !t.IsVoided && t.Purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn && t.FromCell != HandoverCell.Loi).Select(t => t.HandoverTxId).ToHashSet();
    return db.HandoverReceives.List.Any(r => !r.IsVoided && r.QtyNg > 0 && r.NgCaseId == x.Id && helpTx.Contains(r.HandoverTxId));
}
static int Uid(string g) => g switch { "KHO" => 1, "GC" => 2, "HTSP" => 3, "KCS" => 4, "PKG" => 5, _ => 6 };
static T Pick<T>(Random r, params (T v, int w)[] xs)
{
    var tot = xs.Sum(x => x.w); var k = r.Next(tot);
    foreach (var x in xs) { if (k < x.w) return x.v; k -= x.w; }
    return xs[^1].v;
}


// [H10 2026-10-30] Chứng từ máy chủ bỏ qua khi tìm gợi ý cho "huỷ lần nhận rid" (giống HandoverService.VoidReceiveAsync):
//   lần nhận + phiếu trả tự lập của nó + các lần nhận phiếu trả (huỷ dây chuyền).
HashSet<string> RecvOpDocs(LedgerInput inp, long rid)
{
    var d = new HashSet<string> { "R:" + rid };
    foreach (var b in inp.Txs.Where(b => b.SourceReceiveId == rid)) { d.Add("T:" + b.Id); foreach (var br in b.Receives) d.Add("R:" + br.Id); }
    return d;
}

// [H10 2026-10-30] Mọi lần nhận còn hiệu lực (kể cả lần nhận gốc có phiếu trả đã được nhận lại — huỷ dây chuyền): nếu huỷ bị K5 chặn thì
//   [GN-WHY] phải lần ra đường gỡ, và làm theo từng bước gợi ý trên bản sao phải gỡ được (bước không trỏ chứng từ đã mất). Chỉ tính, không ghi.
async Task WhyAllCheck(AppDbContext db, HandoverLedgerService led, int po, int opIdx, string desc)
{
    var input = (await led.GetInputForDetailAsync(1))!;
    if (!input.ApplyWorkflow) return;
    var before = HandoverLedgerService.Compute(input);
    foreach (var t in input.Txs)
        foreach (var r in t.Receives)
        {
            if (input.Txs.Any(b => b.SourceReceiveId == r.Id && b.Receives.Any())) Inc("h10.daychuyen");
            var opDocs = RecvOpDocs(input, r.Id);
            var a = HandoverWhyTest.CloneInput(input); HandoverWhy.OpVoidReceive(r.Id)(a);
            if (!HandoverLedgerService.NewNegativeKeys(before, HandoverLedgerService.Compute(a)).Any()) continue;
            checks++;
            var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(r.Id), opDocs, loopOp: true);
            if (!plan.Complete)
            {   // hết trần tìm (mặc định Plan: 400 lần tính / 5 s — [WHY-TRAN]; trước 80 / 1,5 s) chưa phải vòng khoá: tìm lại không giới hạn để phân biệt
                var big = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(r.Id), opDocs, 5000, 60000, loopOp: true);
                if (big.Complete) { Inc("h10.hettran"); h10MaxSteps = Math.Max(h10MaxSteps, big.Steps.Count); continue; }
                plan = big;
            }
            Inc(plan.Complete ? "h10.ok" : "h10.ket");
            if (plan.Complete)
            {   // làm theo gợi ý trên bản sao: mỗi bước phải còn chứng từ, cuối cùng thao tác gốc không còn ô mới âm
                var sim = HandoverWhyTest.CloneInput(input); string? bad = null;
                foreach (var st in plan.Steps)
                {
                    bool exists = st.Kind switch
                    {
                        "WTS" => sim.Wts.Any(w => w.Id == st.Id),
                        "WTSV" => sim.Wts.Any(w => w.Id == st.Id && HandoverWhy.CanRevertWts(w)),   // [H10-KET-WHY-3]
                        "NHAN" => sim.Txs.Any(x => x.Receives.Any(y => y.Id == st.Id)),
                        "BAOLOI" => sim.NgReps.Any(b2 => b2.Id == st.Id),
                        "KQSUA" => sim.Repairs.Any(x => x.Id == st.Id),   // [K5-VONG-SUA 05/11] trước rơi vào nhánh phiếu
                        "XNSUA" => sim.Repairs.Any(x => x.Checks.Any(c => c.Id == st.Id)),
                        _ => sim.Txs.Any(x => x.Id == st.Id),
                    };
                    if (!exists) { bad = $"bước {st.Kind}#{st.Id} trỏ chứng từ đã mất"; break; }
                    var bs = HandoverLedgerService.Compute(sim); var s2 = HandoverWhyTest.CloneInput(sim);
                    HandoverWhy.StepOpOf(st)(s2);   // [H10-KET-WHY-3] dùng chung thao tác của Plan (+ WTSV sửa dòng về số trước)
                    if (HandoverLedgerService.NewNegativeKeys(bs, HandoverLedgerService.Compute(s2)).Any()) { bad = $"bước {st.Kind}#{st.Id} tự nó bị K5 chặn"; break; }
                    sim = s2;
                }
                if (bad == null && !sim.Txs.Any(x => x.Receives.Any(y => y.Id == r.Id))) bad = "làm theo gợi ý xong thì lần nhận gốc đã mất (thao tác gốc không còn để làm)";
                if (bad == null)
                {
                    var bs = HandoverLedgerService.Compute(sim); var s3 = HandoverWhyTest.CloneInput(sim); HandoverWhy.OpVoidReceive(r.Id)(s3);
                    if (HandoverLedgerService.NewNegativeKeys(bs, HandoverLedgerService.Compute(s3)).Any()) bad = "làm theo gợi ý xong vẫn bị K5 chặn";
                }
                if (bad != null && h10Seen.Add($"{po}:{r.Id}:sim")) fails.Add($"[H10 gợi ý sai] PO{po} op{opIdx} sau «{desc}»: huỷ lần nhận #{r.Id}: {bad}");
                continue;
            }
            var key = $"{po}:{r.Id}";
            if (!h10Seen.Add(key)) continue;
            var dump = new System.Text.StringBuilder();
            var a2 = HandoverWhyTest.CloneInput(input); HandoverWhy.OpVoidReceive(r.Id)(a2);
            dump.Append("ô âm: " + string.Join(", ", HandoverLedgerService.NewNegativeKeys(before, HandoverLedgerService.Compute(a2))));
            foreach (var x in input.Txs) dump.Append($"\n      tx#{x.Id} {x.From}->{x.To} {x.Purpose} {x.FromCell}->{x.ToCell} q={x.Issued} fnc={x.FromNC} tnc={x.ToNC} vu={x.NgCaseId} src={x.SourceReceiveId} @{x.At:HH:mm:ss.fff} rec={string.Join(",", x.Receives.Select(y => $"#{y.Id}:{y.Ok}/{y.Ng}@{y.Cell}/vu{y.NgCaseId}"))}");
            foreach (var w in input.Wts) dump.Append($"\n      wts#{w.Id} {w.ProcessGroup} {w.NC}{w.WtsCode} {w.Ok}/{w.Ng} vu={w.NgCaseId}");
            foreach (var b in input.NgReps) dump.Append($"\n      baoloi#{b.Id} {b.Group} {b.Source} {b.NC} {b.Qty} vu={b.NgCaseId}");
            foreach (var rp in input.Repairs) dump.Append($"\n      kqsua#{rp.Id} {rp.Group} {rp.Cell} vu={rp.NgCaseId} dat={rp.Pass} khong={rp.Fail} nc={rp.ToNC} @{rp.At:HH:mm:ss.fff} xn={string.Join(",", rp.Checks.Select(c => $"#{c.Id}:{c.Ok}/{c.Ng}@{c.At:HH:mm:ss.fff}"))}");   // [H10-KET-WHY 07/11]
            // [H10-KET-WHY 2026-11-07] ghi sổ chứng từ ra JSON để tái hiện đúng ca (fuzz không tất định theo giờ chạy): dotnet GiaoNhanFuzz.dll whyreplay <file> <lần nhận>
            try
            {
                Directory.CreateDirectory("nang_ketqua");
                var jf = Path.Combine("nang_ketqua", $"h10_s{seed0}_po{po}_r{r.Id}_{DateTime.Now:HHmmssfff}.json");
                File.WriteAllText(jf, System.Text.Json.JsonSerializer.Serialize(input, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
                dump.Append("\n      JSON: " + jf);
            }
            catch (Exception ex) { dump.Append("\n      (không ghi được JSON: " + ex.Message + ")"); }
            fails.Add($"[H10 kẹt] PO{po} op{opIdx} sau «{desc}»: huỷ lần nhận #{r.Id} (phiếu #{t.Id} {t.From}->{t.To}{(t.SourceReceiveId != null ? " tự lập" : "")}) bị chặn, gợi ý dừng sau {plan.Steps.Count} bước ({string.Join(" → ", plan.Steps.Select(st => st.Kind + "#" + st.Id))})\n      " + dump);
        }
}

// [GN-WHY] Kiểm gợi ý gỡ: (1) gợi ý có trong lời báo; (2) làm đúng theo gợi ý (qua hàm thật / mô phỏng trang WTS) thì thao tác gốc lưu được
async Task WhyCheck(AppDbContext db, HandoverService svc, HandoverLedgerService led, Action<LedgerInput> opFn, HashSet<string> opDocs,
                    Func<Task<bool>> retry, Func<Task<bool>>? pre, Random rnd, int po, int opIdx, string desc, string? svcMsg, bool loop = false)   // [K5-VONG-SUA] loop: thao tác huỷ chứng từ nguồn (như máy chủ)
{
    var input = (await led.GetInputForDetailAsync(1))!;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var plan = HandoverWhy.Plan(input, opFn, opDocs, loopOp: loop);
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
            foreach (var w in db.WtsProductionLogs.List) Console.WriteLine($"   wts#{w.WtsLogId} {w.ProcessGroup} {w.NC}{w.WtsCode} {w.QtyOk}/{w.QtyNg} void={w.IsVoided} vu={w.NgCaseId}");
            foreach (var r in db.HandoverRepairResults.List) Console.WriteLine($"   kqsua#{r.HandoverRepairResultId} {r.GroupCode} {r.SourceCell} vu={r.NgCaseId} dat={r.QtyPass} khong={r.QtyFail} nc={r.ToNC} void={r.IsVoided} xn={string.Join(",", db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == r.HandoverRepairResultId).Select(c => $"#{c.HandoverRepairCheckId}:{c.QtyOk}/{c.QtyNg}{(c.IsVoided ? "x" : "")}"))}");   // [GC-SUA-TAI-CHO]
        }
        return;
    }
    if (svcMsg != null && plan.Steps.Count > 0 && !svcMsg.Contains("nên kiểm tra theo các bước sau"))
        fails.Add($"[GN-WHY thiếu lời gợi ý] PO{po} op{opIdx} {desc}");
    // Làm theo gợi ý
    var wMap = new Dictionary<long, long>();   // [H10-KET-WHY-3] sửa dòng WTS (bước WTSV) → dòng mới mang số mới; bước sau đụng cùng dòng dùng số mới (lời gợi ý nói rõ "chính dòng đã sửa ở bước trên")
    long WId(long id) { while (wMap.TryGetValue(id, out var n2)) id = n2; return id; }
    foreach (var st in plan.Steps)
    {
        bool okStep;
        if (st.Kind == "WTS") okStep = await WtsVoid(db, led, WId(st.Id));
        else if (st.Kind == "WTSV") { var o2 = WId(st.Id); okStep = await WtsRevertStep(db, led, o2); if (okStep) wMap[o2] = lastRevertNew; }   // [H10-KET-WHY-3] sửa dòng về số trước bằng hàm thật
        else if (st.Kind == "NHAN") { var r = await svc.VoidReceiveAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }
        else if (st.Kind == "BAOLOI") { var r = await svc.VoidNgReportAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }   // [GN-NG-AFTER]
        else if (st.Kind == "KQSUA") { var r = await svc.VoidRepairResultAsync(st.Id, "goi y", 1); okStep = r.Ok; lastErr = r.Error; }   // [GC-SUA-TAI-CHO]
        else if (st.Kind == "XNSUA") { var r = await svc.VoidRepairCheckAsync(st.Id, "goi y", 1); okStep = r.Ok; lastErr = r.Error; }   // [GC-SUA-TAI-CHO]
        else { var r = await svc.VoidIssueAsync(st.Id, "goi y", 1, All); okStep = r.Ok; lastErr = r.Error; }
        if (!okStep) { fails.Add($"[GN-WHY bước gợi ý bị chặn] PO{po} op{opIdx} {desc} → {st.Kind} #{st.Id}: {lastErr}"); return; }
        Inc("why.step");
    }
    if (pre != null && !await pre()) { fails.Add($"[GN-WHY huỷ lần nhận của chính phiếu bị chặn] PO{po} op{opIdx} {desc}: {lastErr}"); return; }
    if (!await retry()) { fails.Add($"[GN-WHY làm theo gợi ý vẫn không lưu được] PO{po} op{opIdx} {desc}"); return; }
    Inc("why.retry.ok");
    await CheckAll(db, led, po, opIdx, desc + " (sau khi làm theo gợi ý)");
}

// [FUZZ-PRE-DEEP 30/10] Huỷ 1 lần nhận; bị K5 chặn thì làm theo gợi ý gỡ của [GN-WHY] (lần nhận / phiếu / WTS / báo lỗi mới hơn đang dùng số),
//   mỗi bước NHAN cũng đệ quy như vậy (tối đa 4 tầng), rồi thử lại. Trước: bước pre không đệ quy → báo "huỷ lần nhận của chính phiếu bị chặn"
//   dù phần mềm đã chỉ đúng đường gỡ (loi_loaitru seed 52, chuoi_loaitru seed 129) — giới hạn của bộ kiểm, không phải lỗi phần mềm.
async Task<(bool Ok, string Error)> VoidRecvDeep(AppDbContext db, HandoverService svc, HandoverLedgerService led, long rid, int depth)
{
    var r0 = await svc.VoidReceiveAsync(rid, "goi y", 1, All);
    if (r0.Ok || depth >= 4 || !r0.Error.StartsWith("Không lưu")) return r0;
    var input = (await led.GetInputForDetailAsync(1))!;
    var plan = HandoverWhy.Plan(input, HandoverWhy.OpVoidReceive(rid), RecvOpDocs(input, rid), loopOp: true);   // [H10] tập bỏ qua như máy chủ
    if (!plan.Complete || plan.Steps.Count == 0) return r0;
    if (Environment.GetEnvironmentVariable("DEEPDBG") == "1") Console.WriteLine($"   [DEEP] huỷ lần nhận #{rid} (tầng {depth}) bị chặn: {r0.Error}\n   [DEEP] gợi ý: {string.Join(" → ", plan.Steps.Select(st => st.Kind + "#" + st.Id))}");
    var wMap = new Dictionary<long, long>();   // [H10-KET-WHY-3] như WhyCheck: số dòng mới sau bước WTSV
    long WId(long id) { while (wMap.TryGetValue(id, out var n2)) id = n2; return id; }
    foreach (var st in plan.Steps)
    {
        (bool Ok, string Error) rs;
        if (st.Kind == "WTS") { var okW = await WtsVoid(db, led, WId(st.Id)); rs = (okW, lastErr ?? ""); }
        else if (st.Kind == "WTSV") { var o2 = WId(st.Id); var okV = await WtsRevertStep(db, led, o2); if (okV) wMap[o2] = lastRevertNew; rs = (okV, lastErr ?? ""); }   // [H10-KET-WHY-3]
        else if (st.Kind == "NHAN") rs = await VoidRecvDeep(db, svc, led, st.Id, depth + 1);
        else if (st.Kind == "BAOLOI") rs = await svc.VoidNgReportAsync(st.Id, "goi y", 1, All);
        // [GN-FORM1 2026-11-05] lỗi BỘ KIỂM (có từ [GC-SUA-TAI-CHO]): gợi ý KQSUA / XNSUA bị coi là phiếu → huỷ nhầm phiếu cùng số Id
        //   ("Phiếu giao không tồn tại"). Fuzz CHUOI seed 12 PO17 có thao tác form bắt được. Phần mềm không sai.
        else if (st.Kind == "KQSUA") rs = await svc.VoidRepairResultAsync(st.Id, "goi y", 1);
        else if (st.Kind == "XNSUA") rs = await svc.VoidRepairCheckAsync(st.Id, "goi y", 1);
        else rs = await svc.VoidIssueAsync(st.Id, "goi y", 1, All);
        if (!rs.Ok) return rs;
        Inc("why.pre.deep");
    }
    return await svc.VoidReceiveAsync(rid, "goi y", 1, All);
}

// [K5-VONG-HO-WTS 2026-11-05] Ô mới âm khi xoá dòng WTS: K5 số cuối + K5 theo thời gian hàng sửa trả về (xoá dòng = huỷ chứng từ nguồn)
// [WTS-SUA-PHAN 2026-11-05] chuỗi bản của 1 dòng WTS theo editPrev (bộ kiểm tự ghi lúc sửa): cũ → mới, mỗi bản (CreatedAt, OK, NG)
List<WtsVer> TrackedVers(AppDbContext db, Dictionary<long, long> prev, long id)
{
    var chain = new List<WtsProductionLog>();
    long cur = id;
    for (int g = 0; g < 60; g++)
    {
        chain.Add(db.WtsProductionLogs.List.First(x => x.WtsLogId == cur));
        if (!prev.TryGetValue(cur, out var pId)) break;
        cur = pId;
    }
    chain.Reverse();
    return chain.Select(r => new WtsVer(r.CreatedAt, r.QtyOk ?? (r.QtyDone - (r.QtyNg ?? 0)), r.QtyNg ?? 0)).ToList();
}

// [WTS-SUA-PHAN] đầu vào sổ với giờ / các bản của dòng sửa dựng lại từ editPrev (thay phần EditOrigins của sổ)
LedgerInput Retime(LedgerInput i, AppDbContext db, Dictionary<long, long> prev)
{
    i.Wts = i.Wts.Select(w =>
    {
        if (!prev.ContainsKey(w.Id)) return w with { Saved = db.WtsProductionLogs.List.First(x => x.WtsLogId == w.Id).CreatedAt, Vers = null };
        var v = TrackedVers(db, prev, w.Id);
        return w with { Saved = v[0].At, Vers = v };
    }).ToList();
    return i;
}

async Task<List<string>> WtsVoidBad(HandoverLedgerService led, long wid)
{
    var bi = (await led.GetInputForDetailAsync(1))!;
    var b = HandoverLedgerService.Compute(bi);
    if (!b.ApplyWorkflow) return new List<string>();
    var ai = (await led.GetInputForDetailAsync(1))!;
    ai.Wts = ai.Wts.Where(x => x.Id != wid).ToList();
    var bad = HandoverLedgerService.NewNegatives(b, HandoverLedgerService.Compute(ai));
    bad.AddRange(HandoverLedgerService.NewLoopKeys(bi, b, ai).Select(x => $"{x.Text} = {x.Value:0.##}"));
    return bad;
}

// [H10-KET-WHY-3 2026-11-08] Bước gợi ý "sửa dòng WTS về số trước lần sửa" làm bằng HÀM THẬT (WtsSaveService sửa dòng: huỷ dòng + dòng mới giữ vụ),
//   như người dùng bấm Sửa ở trang WTS. Số đích lấy từ chuỗi bản của sổ (bản ngay trước bản cuối). Ghi chuỗi sửa vào editPrev như thao tác wtsedit.
async Task<bool> WtsRevertStep(AppDbContext db, HandoverLedgerService led, long wid)
{
    var w = db.WtsProductionLogs.List.FirstOrDefault(x => x.WtsLogId == wid && !x.IsVoided);
    if (w == null) { lastErr = "WTS không còn"; return false; }
    var inW = (await led.GetInputForDetailAsync(w.KhPlanDetailId))!.Wts.FirstOrDefault(x => x.Id == wid);
    if (inW == null || !HandoverWhy.CanRevertWts(inW)) { lastErr = "dòng WTS không sửa về được (không còn bản trước)"; return false; }
    var p = inW.Vers![^2];
    var cat = w.NgCaseId != null ? db.NgCases.List.First(x => x.NgCaseId == w.NgCaseId).NgCategoryId : 1;
    var keep = p.Ng > 0 ? w.NgCaseId : null;
    var se = await WtsSvcHelp.SaveAsync(db, w.KhPlanDetailId, w.ProcessGroup, w.NC, w.WtsCode, p.Ok, p.Ng, w.WorkerId, cat, keep, wid, stepNc: w.StepNC);
    if (!se.Ok) { lastErr = se.Err ?? ""; return false; }
    if (editPrevCur != null) editPrevCur[se.LogId!.Value] = wid;
    lastRevertNew = se.LogId!.Value;
    Inc("why.wtsv");
    return true;
}

// Mô phỏng xoá dòng WTS đúng như trang WTS (K5)
async Task<bool> WtsVoid(AppDbContext db, HandoverLedgerService led, long wid)
{
    var w = db.WtsProductionLogs.List.FirstOrDefault(x => x.WtsLogId == wid && !x.IsVoided);
    if (w == null) { lastErr = "WTS không còn"; return false; }
    var bad = await WtsVoidBad(led, wid);   // [K5-VONG-HO-WTS]
    if (bad.Any()) { lastErr = string.Join("; ", bad); return false; }
    var vr = await WtsSvcHelp.VoidAsync(db, wid, 1, w.WorkerId);   // [WTS-SVC] xoá bằng hàm thật
    if (!vr.Ok) { lastErr = vr.Err ?? "?"; return false; }
    return true;
}
