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
            foreach (var G in r.Groups.Values)
                foreach (var msg in G.Warnings.Where(m => !m.Contains("WTS nhập vượt")))   // "WTS nhập vượt" đã ghi ở WTS_VUOT_TRAN
                    Add(r, CanhBao, msg);
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
            .Select(x => (x.KhPlanDetailId, "Taro", K(string.IsNullOrWhiteSpace(x.WtsTaskCode) ? x.NC : x.WtsTaskCode), I(x.StepOrder, x.NC, x.WtsTaskCode, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotBavia.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Bavia", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotWashing.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Rửa", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotInspection.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "KCS", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        snaps.AddRange(db.KhPlanRouteSnapshotPackaging.Where(x => !x.IsBackup).ToList().Select(x => (x.KhPlanDetailId, "Đóng gói", K(x.NC), I(x.StepOrder, x.NC, null, x.SourceStepId, x.SnapshotAt, x.StepName, x.StandardTime))));
        foreach (var g in snaps.GroupBy(x => (x.Det, x.Grp, x.Code)).Where(g => g.Count() > 1))
            if (plans.TryGetValue(g.Key.Det, out var r))
            {
                // [PM-NC-DUP-SVC 02/11] WTS đã ghi mã này ở PO (WTS chỉ ghi mã CV / NC, không biết dòng nào trong các dòng trùng)
                var pgs = g.Key.Grp switch { "Gia công" => new[] { "GC" }, "Taro" => new[] { "TARO" }, "Bavia" => new[] { "BAVIA" }, "Rửa" => new[] { "WASHING" }, "KCS" => new[] { "KCS" }, _ => new[] { "PKG" } };
                var used = wtsAll.Where(w => w.KhPlanDetailId == g.Key.Det && pgs.Contains(w.ProcessGroup) && K(w.ProcessGroup == "GC" ? w.NC : (w.WtsCode ?? w.NC)) == g.Key.Code).ToList();
                var wtsTxt = used.Count == 0 ? "chưa có WTS mã này" : $"WTS mã này {used.Count} dòng (OK {used.Sum(w => w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0))):0.##}, NG {used.Sum(w => w.QtyNg ?? 0):0.##})";
                Add(r, MaTrung, $"quy trình PO: {g.Key.Grp} có {g.Count()} dòng chính cùng mã {g.Key.Code} · {wtsTxt} ({string.Join("; ", g.Select(x => x.Info))})");
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

        var wts = db.WtsProductionLogs.Where(x => !x.IsVoided).ToList();
        res.WtsCount = wts.Count;
        foreach (var x in wts)
        {
            if (!plans.TryGetValue(x.KhPlanDetailId, out var r)) continue;
            if (x.QtyOk == null) res.WtsOld++;
            if (x.QtyOk.HasValue && x.QtyDone != x.QtyOk.Value + (x.QtyNg ?? 0))
                Add(r, WtsSoLech, $"dòng WTS #{x.WtsLogId} {x.ProcessGroup} {x.NC ?? x.WtsCode} {x.StartTime:dd/MM HH:mm}: QtyDone {x.QtyDone:0.##} ≠ OK {x.QtyOk:0.##} + NG {(x.QtyNg ?? 0):0.##}");
            if (x.QtyDone < 0 || (x.QtyOk ?? 0) < 0 || (x.QtyNg ?? 0) < 0)
                Add(r, WtsSoAm, $"dòng WTS #{x.WtsLogId} {x.ProcessGroup} {x.NC ?? x.WtsCode}: Done {x.QtyDone:0.##} OK {x.QtyOk:0.##} NG {x.QtyNg:0.##}");
        }
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
