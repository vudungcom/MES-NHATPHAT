// [RA-SOCHUNG 2026-10-31] QuetSo — quét TOÀN DB, CHỈ ĐỌC: PO nào ô âm / không cân / WTS vượt trần / dòng WTS số lệch.
//   Mọi số tính bằng ĐÚNG hàm sổ chung của phần mềm (HandoverLedgerService) — không công thức riêng.
//   Phần này dùng chung cho bản thật (RealHost.cs, SQL Server) và bản tự kiểm trong bộ kiểm GiaoNhanFuzz (kịch bản `quet`).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

namespace QuetSo;

public static class QuetCore
{
    public sealed class Finding
    {
        public int    DetailId { get; set; }
        public string Po       { get; set; } = "";
        public string Plan     { get; set; } = "";
        public string Part     { get; set; } = "";   // [PM-NC-DUP-SVC 2026-11-02] mã chi tiết + khách (để báo khách ví dụ thật)
        public bool   Workflow { get; set; }
        public string Kind     { get; set; } = "";
        public string Detail   { get; set; } = "";
    }

    // Loại lỗi (mã ngắn để lọc trong file kết quả)
    public const string KhongCan   = "KHONG_CAN";        // Phôi ≠ tồn các nơi + đang giao + thành phẩm + huỷ + trả khách (L6)
    public const string OAm        = "O_AM";             // ô sổ âm (K5)
    public const string WtsVuot    = "WTS_VUOT_TRAN";    // NC / mã: OK + NG > hàng đã nhận vào (L3)
    public const string DangODau   = "DANG_O_DAU_LECH";  // khung "Đang ở đâu" cộng ≠ phôi
    public const string WtsSoLech  = "WTS_SO_LECH";      // dòng WTS: QtyDone ≠ QtyOk + QtyNg (màn hình cũ đọc QtyDone, sổ đọc OK / NG)
    public const string WtsSoAm    = "WTS_SO_AM";        // dòng WTS có số âm
    public const string CanhBao    = "CANH_BAO_SO";      // cảnh báo khác của sổ (vd PO chưa có snapshot mã CV)
    public const string MaTrung    = "MA_TRUNG";         // [PM-NC-DUP-SVC 2026-11-01] quy trình (snapshot) PO có 2 dòng CHÍNH cùng mã trong 1 nhóm → sổ tính lệch
    public const string LoaiTruDau = "LOAI_TRU_DAU";     // [N3-FIRST 2026-11-03] quy trình GC (snapshot PO) BẮT ĐẦU bằng NC máy loại trừ (N3 đầu chuỗi)
    public const string PmTrung    = "PM_TRUNG";         // [PM-T4 2026-11-05] Part Master có 2 dòng CHÍNH đang dùng cùng mã trong 1 bảng → chặn tạo ràng buộc DB (T4)
    public const string PmMaA      = "PM_MA_A";          // [PM-NC-AT 2026-11-05] Part Master có mã NC / mã CV bắt đầu "@" (dành cho mã hệ thống như @VAO)
    // [QUET-VU-SOLE 2026-11-06] Nguồn: CLAUDE 06/11 (bản "em hiểu" Bước 1 — QuetSo chưa kiểm vụ cân; B3 RS-E9 Hùng chốt 05/11 15:37 "kiểm DB trước")
    public const string VuLech     = "VU_LECH";          // vụ lỗi không cân (Sinh ra ≠ giữ + chuyển + xử lý + huỷ + trả khách) / Σ vụ ≠ ô Hàng lỗi · Hàng sửa / khung lỗi báo lệch
    public const string SoLe       = "SO_LE";            // số lẻ (không nguyên) ở SL PO / phiếu / lần nhận / WTS / báo lỗi / Kết quả sửa (B3 RS-E9)
    // [K5-CLOSE-NHIEU 2026-11-06] Σ dòng HandoverTxCloses ≠ QtyClosed của phiếu. Ít hơn = huỷ làm trước khi code mới lên (sổ tự bù 1 dòng, chỉ để biết);
    //   nhiều hơn / có dòng mà phiếu không có QtyClosed = không thể khi code ghi đúng → báo Claude (sổ tính như cũ theo bộ cột cũ).
    public const string CloseLech  = "CLOSE_LECH";
    // [TARO-LAMHO-NC 2026-11-07] PO có Taro ở NC máy loại trừ GIỮA chuỗi Gia công (HTSP làm hộ): chỉ để biết — số các mã Taro đó nay tính theo hàng
    //   giao theo NC (không theo hàng HTSP nhận), PO cũ đã ghi WTS Taro theo cách cũ có thể hiện "WTS vượt" ở mã đó. Nguồn: HÙNG 07/11 10:18 OK Gói 6.
    public const string LamHoTaro  = "LAMHO_TARO";
    // [WTS-MA-TRIM] dòng WTS còn hiệu lực có mã CV / NC / NC Taro dư khoảng trắng đầu cuối: trước Gói 7 sổ không ghép được với quy trình
    //   (OK mất khỏi sổ, NG vẫn trừ nhóm). Nay sổ Trim khi đọc → số mã đó đổi (đúng hơn); chỉ để biết. Nguồn: rà độc lập toàn hệ thống 07/11 (Cao).
    public const string WtsKhoangTrang = "WTS_MA_KHOANGTRANG";
    //   + quy trình (snapshot) PO có NC / mã CV / dòng cha dư khoảng trắng: sổ nay bỏ khi đọc (cả 2 phía) — chỉ để biết, nguồn ở Part Master.
    public const string SnapKhoangTrang = "SNAP_KHOANGTRANG";
    // [LAMHO-NCKEY 2026-11-08] NC Taro khớp 2 NC Gia công khác chữ (vd "3" / "NC003" — so bỏ chữ NC + số 0 đầu): sổ không ghép làm hộ (tính như HTSP thường),
    //   cảnh báo trên sổ. Chỉ để biết — sửa tên NC ở Part Master. Nguồn: CLAUDE (sổ LAMHO-NCKEY); Hùng 08/11 16:37 OK.
    public const string TaroNcLech = "TARO_NC_LECH";

    public sealed class Result
    {
        public List<Finding> Findings { get; } = new();
        public int PoCount { get; set; }
        public int WtsCount { get; set; }
        public int WtsOld { get; set; }   // dòng WTS cũ chưa có QtyOk (sổ lấy QtyDone − NG)
        public List<string> PartFindings { get; } = new();   // [N3-FIRST] Part Master (chưa thành PO) có NC đầu GC là máy loại trừ
        public List<string> PmFindings { get; } = new();     // [PM-T4][PM-NC-AT] Part Master: mã chính trùng / mã bắt đầu "@" (kèm PO đã dùng Part)
    }

    public static async Task<Result> ScanAsync(AppDbContext db, HandoverLedgerService led)
    {
        var res = new Result();
        var rows = await led.GetLedgerAsync();
        res.PoCount = rows.Count;
        var plans = rows.ToDictionary(r => r.KhPlanDetailId);
        void Add(LedgerRow r, string kind, string detail)
            => res.Findings.Add(new Finding { DetailId = r.KhPlanDetailId, Po = r.PurchaseOrder, Plan = r.PlanNo, Part = $"{r.PartNo} · {r.CustomerName}", Workflow = r.ApplyWorkflow, Kind = kind, Detail = detail });

        foreach (var r in rows)
        {
            if (!r.Balanced)
                Add(r, KhongCan, $"Phôi {r.Phoi:0.##} ≠ tồn {r.TonTong:0.##} + đang giao {r.DangGiao:0.##} + thành phẩm {r.TpNhap:0.##} + huỷ {r.DaHuy:0.##} + trả khách {r.TraKhach:0.##}");
            foreach (var kv in HandoverLedgerService.NegativeCells(r))
                Add(r, OAm, $"{kv.Value.Text} = {kv.Value.Value:0.##}");
            foreach (var G in r.Groups.Values)
                foreach (var s in G.Steps.Where(s => !s.IsPrep && !s.IsExcluded && s.Waiting.HasValue && s.Waiting.Value < 0))
                    Add(r, WtsVuot, $"{G.Label} {(HandoverLedgerService.IsVao(s.Code) ? HandoverLedgerService.VaoLabel : (s.Pg == "GC" ? "NC " : s.SubGroup + " ") + s.Code)}: nhận vào {(s.In ?? 0):0.##} − đã ghi WTS OK {s.Ok:0.##} − đã ghi WTS NG {s.Ng:0.##} = {s.Waiting:0.##}");
            var w = HandoverLedgerService.WhereIs(r);
            if (!w.Balanced)
                Add(r, DangODau, $"cộng các ô {w.Total:0.##} ≠ phôi {w.Phoi:0.##}");
            foreach (var lr in r.LamHoRuns.Where(x => x.HasTaro))   // [TARO-LAMHO-NC] chỉ để biết
                Add(r, LamHoTaro, $"Taro làm hộ {string.Join(", ", lr.Ncs.Select(HandoverLedgerService.NcName))} ({HandoverLedgerService.NcName(lr.FromNc)} → {HandoverLedgerService.NcName(lr.NextNc)}): HTSP nhận theo NC {lr.In:0.##} · OK bộ Taro {lr.Done:0.##} · đã trả về {lr.Ret:0.##} · chiếc làm hộ bị lỗi đang giữ {r.LamHoNgHeld("HTSP"):0.##}"
                    + $" · mã: {string.Join(", ", r.G("HTSP").Steps.Where(s => s.LamHoNc != null && !s.IsPrep && lr.Ncs.Contains(s.LamHoNc)).Select(s => $"{s.Code} OK {s.Ok:0.##} NG {s.Ng:0.##} chờ {s.Waiting:0.##}"))}");
            foreach (var G in r.Groups.Values)
                foreach (var msg in G.Warnings.Where(m => !m.Contains("WTS nhập vượt") && !m.Contains("khác giữ + chuyển") && !m.Contains("tổng vụ lỗi đang giữ ô")))   // "WTS nhập vượt" đã ghi ở WTS_VUOT_TRAN; 2 loại vụ lệch ghi ở VU_LECH (không đếm 2 lần — rà độc lập 06/11 mục 8)
                    Add(r, msg.StartsWith(HandoverLedgerService.TaroNcLechHead) ? TaroNcLech : CanhBao, msg);   // [LAMHO-NCKEY] tách loại riêng
        }

        // [PM-NC-DUP-SVC 2026-11-01] Mã chính trùng trong snapshot PO (dòng dự phòng không xét — như trang Part Master [B6-NC-DUP]).
        //   Mã so như sổ: Gia công / Bavia / Rửa / KCS / Đóng gói theo NC; Taro theo mã CV (trống thì NC); bỏ khoảng trắng, không phân biệt hoa thường.
        static string K(string? c) => (c ?? "").Trim().ToUpperInvariant();
        var snaps = new List<(int Det, string Grp, string Code, string Info)>();
        var wtsAll = db.WtsProductionLogs.Where(x => !x.IsVoided).ToList();
        static string I(int ord, string? nc, string? code, long? src, DateTime at, string? name = null, decimal? std = null)   // [PM-NC-DUP-SVC 02/11] + tên công đoạn + T.Chuẩn
            => $"#{ord} NC {nc}{(code != null ? " mã " + code : "")}{(string.IsNullOrWhiteSpace(name) ? "" : " \"" + name!.Trim() + "\"")}{(std.HasValue ? $" chuẩn {std:0.##}" : "")} nguồn {src?.ToString() ?? "—"} chụp {at:dd/MM HH:mm}";
        snaps.AddRange(db.KhPlanRouteSnapshotMachining.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Gia công", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.MachineRegistered))));
        snaps.AddRange(db.KhPlanRouteSnapshotTaro.Where(x => !x.IsBackup).ToList()
            // [TARO-NC-MA 2026-11-06] Taro trùng theo CẶP (mã CV, NC): khách chốt 02/11 giữ mã CV chung nhiều NC (sổ đếm theo cặp) → chỉ cùng mã CÙNG NC mới là trùng
            .Select(x => (x.KhPlanDetailId, "Taro", K(string.IsNullOrWhiteSpace(x.WtsTaskCode) ? x.NC : x.WtsTaskCode) + "|NC " + K(x.NC), I(x.StepOrder, x.NC, x.WtsTaskCode, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotBavia.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Bavia", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotWashing.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Rửa", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotInspection.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "KCS", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotPackaging.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Đóng gói", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        // [WTS-MA-TRIM] snapshot dư khoảng trắng (mọi dòng, cả dự phòng)
        static bool WsS(string? v) => v != null && v.Length > 0 && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1]));
        var wsSnap = new List<(int Det, string What)>();
        wsSnap.AddRange(db.KhPlanRouteSnapshotMachining.ToList().Where(x => WsS(x.NC) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình Gia công #{x.StepOrder} NC \"{x.NC}\"{(x.ParentNC != null ? $" cha \"{x.ParentNC}\"" : "")}")));
        wsSnap.AddRange(db.KhPlanRouteSnapshotTaro.ToList().Where(x => WsS(x.NC) || WsS(x.WtsTaskCode) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình Taro #{x.StepOrder} NC \"{x.NC}\" mã \"{x.WtsTaskCode}\"{(x.ParentNC != null ? $" cha \"{x.ParentNC}\"" : "")}")));
        wsSnap.AddRange(db.KhPlanRouteSnapshotBavia.ToList().Where(x => WsS(x.NC) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình Bavia #{x.StepOrder} \"{x.NC}\"")));
        wsSnap.AddRange(db.KhPlanRouteSnapshotWashing.ToList().Where(x => WsS(x.NC) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình Rửa #{x.StepOrder} \"{x.NC}\"")));
        wsSnap.AddRange(db.KhPlanRouteSnapshotInspection.ToList().Where(x => WsS(x.NC) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình KCS #{x.StepOrder} \"{x.NC}\"")));
        wsSnap.AddRange(db.KhPlanRouteSnapshotPackaging.ToList().Where(x => WsS(x.NC) || WsS(x.ParentNC)).Select(x => (x.KhPlanDetailId, $"quy trình Đóng gói #{x.StepOrder} \"{x.NC}\"")));
        // + NC chứng từ (phiếu, lần nhận, báo lỗi, Kết quả sửa, vụ) — sổ cũng bỏ khoảng trắng khi đọc (rà Gói 7 vòng 2 mục Thấp 3)
        wsSnap.AddRange(db.HandoverTransactions.Where(x => !x.IsVoided).ToList().Where(x => WsS(x.FromNC) || WsS(x.ToNC)).Select(x => (x.KhPlanDetailId, $"phiếu #{x.HandoverTxId} NC giao \"{x.FromNC}\" / nhận \"{x.ToNC}\"")));
        var txDetW = db.HandoverTransactions.Select(x => new { x.HandoverTxId, x.KhPlanDetailId }).ToList().ToDictionary(x => x.HandoverTxId, x => x.KhPlanDetailId);
        wsSnap.AddRange(db.HandoverReceives.Where(x => !x.IsVoided).ToList().Where(x => WsS(x.ToNC) && txDetW.ContainsKey(x.HandoverTxId)).Select(x => (txDetW[x.HandoverTxId], $"lần nhận #{x.ReceiveId} NC \"{x.ToNC}\"")));
        wsSnap.AddRange(db.HandoverNgReports.ToList().Where(x => WsS(x.NC)).Select(x => (x.KhPlanDetailId, $"báo lỗi #{x.HandoverNgReportId} NC \"{x.NC}\"")));
        wsSnap.AddRange(db.HandoverRepairResults.ToList().Where(x => WsS(x.ToNC)).Select(x => (x.KhPlanDetailId, $"Kết quả sửa #{x.HandoverRepairResultId} NC \"{x.ToNC}\"")));
        wsSnap.AddRange(db.NgCases.ToList().Where(x => WsS(x.DetectNC)).Select(x => (x.KhPlanDetailId, $"vụ #{x.NgCaseId} NC phát hiện \"{x.DetectNC}\"")));
        foreach (var w in wsSnap)
            if (plans.TryGetValue(w.Det, out var rw)) Add(rw, SnapKhoangTrang, $"{w.What} — dư khoảng trắng (sổ nay bỏ khi đọc)");
        foreach (var g in snaps.GroupBy(x => (x.Det, x.Grp, x.Code)).Where(g => g.Count() > 1))
            if (plans.TryGetValue(g.Key.Det, out var r))
            {
                // [PM-NC-DUP-SVC 02/11] WTS đã ghi mã này ở PO (WTS chỉ ghi mã CV / NC, không biết dòng nào trong các dòng trùng)
                var pgs = g.Key.Grp switch { "Gia công" => new[] { "GC" }, "Taro" => new[] { "TARO" }, "Bavia" => new[] { "BAVIA" }, "Rửa" => new[] { "WASHING" }, "KCS" => new[] { "KCS" }, _ => new[] { "PKG" } };
                var codeK = g.Key.Grp == "Taro" ? g.Key.Code.Split('|')[0] : g.Key.Code;   // [TARO-NC-MA] khoá Taro = "mã|NC x"
                var used = wtsAll.Where(w => w.KhPlanDetailId == g.Key.Det && pgs.Contains(w.ProcessGroup) && K(w.ProcessGroup == "GC" ? w.NC : (w.WtsCode ?? w.NC)) == codeK).ToList();
                var wtsTxt = used.Count == 0 ? "chưa có WTS mã này" : $"WTS mã này {used.Count} dòng (OK {used.Sum(w => w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0))):0.##}, NG {used.Sum(w => w.QtyNg ?? 0):0.##})";
                var codeDisp = g.Key.Grp == "Taro" ? $"{codeK} ({g.Key.Code.Split('|')[1]})" : g.Key.Code;   // [TARO-NC-MA] "9T (NC TA)"
                Add(r, MaTrung, $"quy trình PO: {g.Key.Grp} có {g.Count()} dòng chính cùng mã {codeDisp} · {wtsTxt} ({string.Join("; ", g.Select(x => x.Info))})");
            }

        // [N3-FIRST 2026-11-03] NC đầu chuỗi Gia công là máy loại trừ (sổ luật N3; Hùng duyệt phương án A 02/11 13:16 — quét TRƯỚC khi sửa sổ).
        //   Sổ hiện tại: NC loại trừ không có ô số, NC kế tiếp thành "NC đầu" nhận thẳng hàng vào Gia công → công đoạn làm hộ không có chứng từ.
        //   In: các NC loại trừ đứng đầu (máy), NC nhận hàng đầu thực tế + số sổ đang tính, Gia công đã nhận / giao làm hộ, WTS đã ghi các NC đó —
        //   để biết dữ liệu nào sẽ đổi cách tính khi sổ có ô "Nhận vào, chưa qua NC". Máy loại trừ của PO = snapshot (KhPlanRouteSnapshotMachineExclude).
        var exclAll = db.KhPlanRouteSnapshotMachineExclude.ToList();
        var gcSnap = db.KhPlanRouteSnapshotMachining.Where(x => !x.IsBackup).ToList();
        foreach (var grp in gcSnap.GroupBy(x => x.KhPlanDetailId))
        {
            if (!plans.TryGetValue(grp.Key, out var r)) continue;
            var ex = exclAll.Where(e => e.KhPlanDetailId == grp.Key && !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ex.Count == 0) continue;
            var chain = grp.OrderBy(x => x.StepOrder).ToList();
            bool IsEx(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && ex.Contains(s.MachineRegistered!.Trim());
            var lead = chain.TakeWhile(IsEx).ToList();
            if (lead.Count == 0) continue;
            var firstReal = chain.Skip(lead.Count).FirstOrDefault();
            var G = r.G("GC");
            string WtsOf(string? nc)
            {
                var ls = wtsAll.Where(w => w.KhPlanDetailId == grp.Key && w.ProcessGroup == "GC" && K(w.NC) == K(nc)).ToList();
                return ls.Count == 0 ? "chưa có WTS" : $"WTS {ls.Count} dòng (OK {ls.Sum(w => w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0))):0.##}, NG {ls.Sum(w => w.QtyNg ?? 0):0.##})";
            }
            var line = firstReal == null ? null : G.Steps.FirstOrDefault(s => s.Pg == "GC" && !s.IsExcluded && K(s.Code) == K(firstReal.NC));
            Add(r, LoaiTruDau,
                $"Gia công bắt đầu bằng NC máy loại trừ: {string.Join(", ", lead.Select(s => $"NC {s.NC} (máy {s.MachineRegistered?.Trim()}, {WtsOf(s.NC)})"))}"
                + (firstReal == null ? " · KHÔNG còn NC nào sau (cả chuỗi loại trừ)"
                   : $" · NC thường đầu tiên: NC {firstReal.NC} (nhận vào {(line?.In ?? 0):0.##} — từ 04/11 [N3-FIRST] chỉ nhận hàng trả về; hàng chưa giao theo NC nằm ở \"{HandoverLedgerService.VaoLabel}\" {(G.Steps.FirstOrDefault(s => HandoverLedgerService.IsVao(s.Code))?.Waiting ?? 0):0.##}, {WtsOf(firstReal.NC)})")
                + $" · Gia công đã nhận Hàng mới {G.NhanMoi:0.##}, Hàng làm hộ về {G.NhanLamHoVe:0.##}, đã giao làm hộ {G.GiaoLamHo:0.##}");
        }
        // Part Master (PO tạo sau sẽ chụp quy trình này): NC chính đầu tiên (đang dùng) chạy máy loại trừ theo danh sách hiện tại
        var exNow = db.MachineExcludeSettings.ToList().Where(e => !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (exNow.Count > 0)
        {
            var partsAct = db.PartMasters.Where(p => p.IsActive && !p.IsObsolete).ToList().ToDictionary(p => p.PartId);
            var custs = db.Customers.ToList().ToDictionary(x => x.CustomerId, x => x.CustomerName);
            foreach (var grp in db.PartMachiningSteps.Where(s => s.IsActive && !s.IsBackup).ToList().GroupBy(s => s.PartId))
            {
                if (!partsAct.TryGetValue(grp.Key, out var p)) continue;
                var chain = grp.OrderBy(s => s.StepOrder).ToList();
                var lead = chain.TakeWhile(s => !string.IsNullOrWhiteSpace(s.MachineRegistered) && exNow.Contains(s.MachineRegistered!.Trim())).ToList();
                if (lead.Count == 0) continue;
                var cn = p.CustomerId.HasValue && custs.TryGetValue(p.CustomerId.Value, out var n) ? n : "—";
                res.PartFindings.Add($"Part {p.PartNo} · {cn}: NC đầu là máy loại trừ {string.Join(", ", lead.Select(s => $"NC {s.NC} (máy {s.MachineRegistered?.Trim()})"))}"
                    + $"{(chain.Count > lead.Count ? $" · NC sau: {chain[lead.Count].NC}" : " · không còn NC nào sau")}");
            }
        }

        // [PM-T4 2026-11-05] Part Master: dòng CHÍNH đang dùng (IsActive, không dự phòng) cùng mã trong 1 bảng (Gia công / Bavia / Rửa / KCS / Đóng gói).
        //   Hùng 05/11 10:28 duyệt làm T4 (ràng buộc DB duy nhất PartId + NC lọc IsActive = 1, IsBackup = 0) vì dữ liệu đang là test — trước khi dọn
        //   + chạy SQL phải báo Hùng Part / PO nào đang trùng để khách xác nhận. Taro KHÔNG xét (khách chốt 02/11 giữ mã CV chung nhiều NC — gói TARO-NC-MA).
        //   So mã như SQL Server so (bỏ khoảng trắng cuối, không phân biệt hoa thường) + bỏ khoảng trắng đầu như service ([PM-NC-DUP-SVC]).
        // [PM-NC-AT 2026-11-05] Mã bắt đầu "@" (mọi dòng kể cả dự phòng, cả Taro mã CV / NC) — "@VAO" là mã hệ thống của dòng ảo [N3-FIRST].
        {
            var pms = db.PartMasters.ToList().ToDictionary(p => p.PartId);
            var custs2 = db.Customers.ToList().ToDictionary(x => x.CustomerId, x => x.CustomerName);
            var dets = db.KhPlanDetails.ToList();
            string PartTxt(int pid)
            {
                if (!pms.TryGetValue(pid, out var p)) return $"PartId {pid} (không còn Part)";
                var cn = p.CustomerId.HasValue && custs2.TryGetValue(p.CustomerId.Value, out var n) ? n : "—";
                var pos = dets.Where(d => string.Equals((d.PartNo ?? "").Trim(), (p.PartNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                var poTxt = pos.Count == 0 ? "chưa có PO nào"
                    : $"{pos.Count} PO: {string.Join(", ", pos.OrderByDescending(d => d.CreatedAt).Take(8).Select(d => $"{d.PurchaseOrder} (tạo {d.CreatedAt:dd/MM})"))}{(pos.Count > 8 ? " …" : "")}";
                return $"Part {p.PartNo} · {cn} · PartId {pid}{(p.IsActive ? "" : " · Part đã TẮT")}{(p.IsObsolete ? " · Part NGƯNG SX" : "")} · {poTxt}";
            }
            var pmRows = new List<(int Pid, string Tbl, long Id, string? Nc, string? Code, bool Backup, int Ord)>();
            pmRows.AddRange(db.PartMachiningSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "Gia công", (long)s.StepId, (string?)s.NC, (string?)null, s.IsBackup, s.StepOrder)));
            pmRows.AddRange(db.PartTaroSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "Taro", (long)s.StepId, (string?)s.NC, s.WtsTaskCode, s.IsBackup, s.StepOrder)));
            pmRows.AddRange(db.PartBaviaSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "Bavia", (long)s.StepId, (string?)s.NC, (string?)null, s.IsBackup, s.StepOrder)));
            pmRows.AddRange(db.PartWashingSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "Rửa", (long)s.StepId, (string?)s.NC, (string?)null, s.IsBackup, s.StepOrder)));
            pmRows.AddRange(db.PartInspectionSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "KCS", (long)s.StepId, (string?)s.NC, (string?)null, s.IsBackup, s.StepOrder)));
            pmRows.AddRange(db.PartPackagingSteps.Where(s => s.IsActive).ToList().Select(s => (s.PartId, "Đóng gói", (long)s.StepId, (string?)s.NC, (string?)null, s.IsBackup, s.StepOrder)));
            foreach (var g in pmRows.Where(x => x.Tbl != "Taro" && !x.Backup).GroupBy(x => (x.Pid, x.Tbl, Code: K(x.Nc))).Where(g => g.Count() > 1).OrderBy(g => g.Key.Pid))
                res.PmFindings.Add($"[{PmTrung}] {PartTxt(g.Key.Pid)} — {g.Key.Tbl}: {g.Count()} dòng chính cùng mã \"{g.Key.Code}\" (StepId {string.Join(", ", g.Select(x => $"{x.Id} thứ tự {x.Ord}"))})");
            foreach (var x in pmRows.Where(x => (x.Nc ?? "").Trim().StartsWith("@") || (x.Code ?? "").Trim().StartsWith("@")).OrderBy(x => x.Pid))
                res.PmFindings.Add($"[{PmMaA}] {PartTxt(x.Pid)} — {x.Tbl} StepId {x.Id}{(x.Backup ? " (dự phòng)" : "")}: NC \"{x.Nc}\"{(x.Code != null ? $" mã CV \"{x.Code}\"" : "")}");
        }

        // [QUET-VU-SOLE 2026-11-06] Mỗi vụ lỗi cân + khớp ô sổ + khung lỗi (HandoverNgTrace) — như kiểm chung của bộ kiểm GiaoNhanFuzz.
        //   Chỉ PO có vụ / hàng lỗi mới tính khung lỗi (đọc lại chứng từ từng PO).
        foreach (var r in rows)
        {
            foreach (var cs in r.Cases.Values.Where(x => x.Lech != 0))
                Add(r, VuLech, $"vụ {cs.No}: sinh {cs.SinhRa:0.##} ≠ giữ {cs.HeldTotal:0.##} + chuyển {cs.Transit:0.##} + xử lý {cs.DaXuLyTotal:0.##} + huỷ {cs.Huy:0.##} + trả khách {cs.TraKhach:0.##} (lệch {cs.Lech:0.##})");
            foreach (var G in r.Groups.Values)
            {
                var vl = r.Cases.Values.Sum(x => x.HeldAt(G.Code, HandoverCell.Loi));
                var vs = r.Cases.Values.Sum(x => x.HeldAt(G.Code, HandoverCell.Sua));
                if (vl != G.LoiGiu || vs != G.SuaGiu)
                    Add(r, VuLech, $"{G.Label}: Σ vụ đang giữ Hàng lỗi {vl:0.##} / Hàng sửa {vs:0.##} ≠ ô sổ {G.LoiGiu:0.##} / {G.SuaGiu:0.##}");
            }
            if (r.Cases.Count == 0 && !r.HasLoiGiu) continue;
            var inp = await led.GetInputForDetailAsync(r.KhPlanDetailId);
            if (inp == null) continue;
            foreach (var pb in HandoverNgTrace.Build(inp).Problems)
                Add(r, VuLech, "khung lỗi: " + pb);
        }

        var snapRaw = new HashSet<string>(StringComparer.Ordinal);   // [WTS-MA-TRIM] "PO|chữ snapshot" (Taro: mã CV, trống thì NC)
        foreach (var x in db.KhPlanRouteSnapshotMachining.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{x.NC}");
        foreach (var x in db.KhPlanRouteSnapshotTaro.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{(string.IsNullOrWhiteSpace(x.WtsTaskCode) ? x.NC : x.WtsTaskCode)}");
        foreach (var x in db.KhPlanRouteSnapshotBavia.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{x.NC}");
        foreach (var x in db.KhPlanRouteSnapshotWashing.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{x.NC}");
        foreach (var x in db.KhPlanRouteSnapshotInspection.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{x.NC}");
        foreach (var x in db.KhPlanRouteSnapshotPackaging.ToList()) snapRaw.Add($"{x.KhPlanDetailId}|{x.NC}");
        static bool Ws(string? v) => v != null && (v.Length == 0 || char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1]));   // chuỗi rỗng: sổ coi là trống (null)
        var wts = db.WtsProductionLogs.Where(x => !x.IsVoided).ToList();
        res.WtsCount = wts.Count;
        foreach (var x in wts)
        {
            if (!plans.TryGetValue(x.KhPlanDetailId, out var r)) continue;
            if (x.QtyOk == null) res.WtsOld++;
            if (x.QtyOk.HasValue && x.QtyDone != x.QtyOk.Value + (x.QtyNg ?? 0))
                Add(r, WtsSoLech, $"dòng WTS #{x.WtsLogId} {x.ProcessGroup} {x.NC ?? x.WtsCode} {x.StartTime:dd/MM HH:mm}: QtyDone {x.QtyDone:0.##} ≠ OK {x.QtyOk:0.##} + NG {(x.QtyNg ?? 0):0.##}");
            if ((Ws(x.NC) || Ws(x.WtsCode) || Ws(x.StepNC)) && !snapRaw.Contains($"{x.KhPlanDetailId}|{x.NC ?? x.WtsCode}"))   // [WTS-MA-TRIM] chữ trùng y snapshot (máy chủ cố ý ghi) → không báo
                Add(r, WtsKhoangTrang, $"dòng WTS #{x.WtsLogId} {x.ProcessGroup} mã \"{x.NC ?? x.WtsCode}\"{(x.StepNC != null ? $" NC \"{x.StepNC}\"" : "")} OK {x.QtyOk:0.##} NG {(x.QtyNg ?? 0):0.##} — dư khoảng trắng, khác chữ snapshot (sổ nay bỏ khi đọc)");
            if (x.QtyDone < 0 || (x.QtyOk ?? 0) < 0 || (x.QtyNg ?? 0) < 0)
                Add(r, WtsSoAm, $"dòng WTS #{x.WtsLogId} {x.ProcessGroup} {x.NC ?? x.WtsCode}: Done {x.QtyDone:0.##} OK {x.QtyOk:0.##} NG {x.QtyNg:0.##}");
        }

        // [QUET-VU-SOLE 2026-11-06] B3 RS-E9: số lẻ ở mọi chỗ ghi số lượng (kể cả dòng đã huỷ — vẫn là lịch sử). Kho (KhoVatLieus) là cột int nên không xét.
        //   Không có dòng nào → máy chủ chỉ nhận số nguyên (Hùng chốt 05/11 15:37); có → báo Hùng trước khi chặn.
        static bool Le(decimal v) => v != decimal.Truncate(v);
        static bool LeN(decimal? v) => v.HasValue && Le(v.Value);
        void AddLe(int det, string what)
        {
            if (plans.TryGetValue(det, out var r)) Add(r, SoLe, what);
            else res.Findings.Add(new Finding { DetailId = det, Po = $"(KhPlanDetailId {det})", Kind = SoLe, Detail = what });
        }
        foreach (var d in db.KhPlanDetails.ToList().Where(d => Le(d.Quantity)))
            AddLe(d.KhPlanDetailId, $"SL PO {d.Quantity:0.####}");
        var txAll = db.HandoverTransactions.ToList();
        foreach (var t in txAll.Where(t => Le(t.QtyIssued) || Le(t.NgQty) || LeN(t.QtyClosed)))
            AddLe(t.KhPlanDetailId, $"phiếu #{t.HandoverTxId}{(t.IsVoided ? " (đã huỷ)" : "")}: giao {t.QtyIssued:0.####} · NG {t.NgQty:0.####} · huỷ phần chưa nhận {t.QtyClosed:0.####}");
        var txDet = txAll.ToDictionary(t => t.HandoverTxId, t => t.KhPlanDetailId);
        // [K5-CLOSE-NHIEU 2026-11-06] dòng từng lần huỷ phần chưa nhận khớp bộ cột cũ
        var closeSum = db.HandoverTxCloses.ToList().GroupBy(c => c.HandoverTxId).ToDictionary(g => g.Key, g => (Sum: g.Sum(c => c.Qty), N: g.Count()));
        foreach (var t in txAll)
        {
            var col = t.QtyClosed ?? 0;
            var (sum, n) = closeSum.TryGetValue(t.HandoverTxId, out var cs) ? cs : (0m, 0);
            if (sum == col) continue;
            var what = sum < col
                ? $"phiếu #{t.HandoverTxId}{(t.IsVoided ? " (đã huỷ)" : "")}: huỷ phần chưa nhận {col:0.##} mà bảng từng lần chỉ có {sum:0.##} ({n} dòng) — phần chênh {col - sum:0.##} làm trước khi code mới lên, sổ tự bù 1 dòng (không lệch số)"
                : $"phiếu #{t.HandoverTxId}{(t.IsVoided ? " (đã huỷ)" : "")}: bảng từng lần {sum:0.##} ({n} dòng) > huỷ phần chưa nhận {col:0.##} trên phiếu — KHÔNG THỂ khi code ghi đúng, báo Claude (sổ đang tính theo phiếu)";
            if (plans.TryGetValue(t.KhPlanDetailId, out var rc)) Add(rc, CloseLech, what);
            else res.Findings.Add(new Finding { DetailId = t.KhPlanDetailId, Po = $"(KhPlanDetailId {t.KhPlanDetailId})", Kind = CloseLech, Detail = what });
        }
        foreach (var orphan in closeSum.Keys.Where(k => !txDet.ContainsKey(k)))
            res.Findings.Add(new Finding { DetailId = 0, Po = "(không có phiếu)", Kind = CloseLech, Detail = $"dòng huỷ phần chưa nhận của phiếu #{orphan} không có trong HandoverTransactions" });
        foreach (var x in db.HandoverReceives.ToList().Where(x => Le(x.QtyOk) || Le(x.QtyNg)))
            AddLe(txDet.TryGetValue(x.HandoverTxId, out var dd) ? dd : 0, $"lần nhận #{x.ReceiveId} (phiếu #{x.HandoverTxId}): OK {x.QtyOk:0.####} · NG {x.QtyNg:0.####}");
        foreach (var x in db.WtsProductionLogs.ToList().Where(x => Le(x.QtyDone) || LeN(x.QtyOk) || LeN(x.QtyNg)))
            AddLe(x.KhPlanDetailId, $"dòng WTS #{x.WtsLogId}{(x.IsVoided ? " (đã huỷ)" : "")} {x.ProcessGroup} {x.NC ?? x.WtsCode}: Done {x.QtyDone:0.####} OK {x.QtyOk:0.####} NG {x.QtyNg:0.####}");
        foreach (var x in db.HandoverNgReports.ToList().Where(x => Le(x.Qty)))
            AddLe(x.KhPlanDetailId, $"báo lỗi #{x.HandoverNgReportId}: {x.Qty:0.####}");
        var kqAll = db.HandoverRepairResults.ToList();
        foreach (var x in kqAll.Where(x => Le(x.QtyPass) || Le(x.QtyFail)))
            AddLe(x.KhPlanDetailId, $"Kết quả sửa #{x.HandoverRepairResultId}: đạt {x.QtyPass:0.####} · không đạt {x.QtyFail:0.####}");
        var kqDet = kqAll.ToDictionary(x => x.HandoverRepairResultId, x => x.KhPlanDetailId);
        foreach (var x in db.HandoverRepairChecks.ToList().Where(x => Le(x.QtyOk) || Le(x.QtyNg)))
            AddLe(kqDet.TryGetValue(x.HandoverRepairResultId, out var kd) ? kd : 0, $"KCS xác nhận #{x.HandoverRepairCheckId} (Kết quả sửa #{x.HandoverRepairResultId}): đạt {x.QtyOk:0.####} · loại {x.QtyNg:0.####}");
        return res;
    }

    public static void Write(Result res, TextWriter o, string dbName)
    {
        o.WriteLine($"QUÉT SỔ (chỉ đọc) · DB {dbName} · {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        o.WriteLine($"PO: {res.PoCount} · dòng WTS còn hiệu lực: {res.WtsCount} (dòng cũ chưa có QtyOk: {res.WtsOld})");
        var byPo = res.Findings.GroupBy(f => f.DetailId).ToList();
        o.WriteLine($"PO có vấn đề: {byPo.Count} · tổng phát hiện: {res.Findings.Count}");
        foreach (var k in res.Findings.GroupBy(f => f.Kind).OrderBy(g => g.Key))
            o.WriteLine($"  {k.Key,-16} {k.Count(),5} dòng · {k.Select(f => f.DetailId).Distinct().Count()} PO");
        // [QUET-VU-SOLE 2026-11-06] 2 mục mới in rõ cả khi không có gì (để biết đã kiểm)
        o.WriteLine($"  Vụ lỗi cân ({VuLech}): {(res.Findings.Any(f => f.Kind == VuLech) ? "CÓ LỆCH — xem từng PO" : "mọi vụ cân, khớp ô sổ + khung lỗi")}");
        o.WriteLine($"  Huỷ phần chưa nhận từng lần ({CloseLech}): {(res.Findings.Any(f => f.Kind == CloseLech) ? "CÓ LỆCH — xem từng PO" : "bảng HandoverTxCloses khớp mọi phiếu")}");   // [K5-CLOSE-NHIEU]
        o.WriteLine($"  NC Taro khớp nhiều NC Gia công ({TaroNcLech}, chỉ để biết — sửa Part Master): {res.Findings.Count(f => f.Kind == TaroNcLech)} · {res.Findings.Where(f => f.Kind == TaroNcLech).Select(f => f.DetailId).Distinct().Count()} PO");   // [LAMHO-NCKEY]
        o.WriteLine($"  Taro làm hộ ({LamHoTaro}, chỉ để biết): {res.Findings.Count(f => f.Kind == LamHoTaro)} đoạn · {res.Findings.Where(f => f.Kind == LamHoTaro).Select(f => f.DetailId).Distinct().Count()} PO");   // [TARO-LAMHO-NC]
        o.WriteLine($"  Số lẻ ({SoLe}, B3): {(res.Findings.Any(f => f.Kind == SoLe) ? "CÓ — báo Hùng trước khi chặn số lẻ" : "không có dòng nào (PO / phiếu / lần nhận / WTS / báo lỗi / Kết quả sửa / KCS xác nhận)")}");
        o.WriteLine();
        foreach (var g in byPo.OrderBy(g => g.First().Plan).ThenBy(g => g.First().Po))
        {
            var f0 = g.First();
            o.WriteLine($"── PO {f0.Po} · KH {f0.Plan} · {f0.Part} · Workflow {(f0.Workflow ? "ON" : "OFF")} (KhPlanDetailId {f0.DetailId})");
            foreach (var f in g) o.WriteLine($"   [{f.Kind}] {f.Detail}");
        }
        if (res.PartFindings.Count > 0)   // [N3-FIRST] Part Master — chưa phải lỗi số, chỉ để biết PO sau sẽ gặp
        {
            o.WriteLine();
            o.WriteLine($"── Part Master: {res.PartFindings.Count} chi tiết có NC đầu Gia công là máy loại trừ ({LoaiTruDau}, PO tạo sau sẽ chụp quy trình này)");
            foreach (var f in res.PartFindings) o.WriteLine("   " + f);
        }
        // [PM-T4][PM-NC-AT 2026-11-05] Part Master (không phải lỗi số PO): mã chính trùng (chặn ràng buộc T4) / mã bắt đầu "@"
        o.WriteLine();
        o.WriteLine($"── Part Master: {res.PmFindings.Count(f => f.StartsWith("[" + PmTrung))} nhóm mã chính trùng ({PmTrung}, Taro không xét) · {res.PmFindings.Count(f => f.StartsWith("[" + PmMaA))} dòng mã bắt đầu \"@\" ({PmMaA})");
        foreach (var f in res.PmFindings) o.WriteLine("   " + f);
        if (res.Findings.Count == 0) o.WriteLine("KHÔNG PHÁT HIỆN GÌ — mọi PO cân, không ô âm, không WTS vượt trần.");
    }
}
