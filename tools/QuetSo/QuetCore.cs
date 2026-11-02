// [RA-SOCHUNG 2026-10-31] QuetSo — quét TOÀN DB, CHỈ ĐỌC: PO nào ô âm / không cân / WTS vượt trần / dòng WTS số lệch.
//   Mọi số tính bằng ĐÚNG hàm sổ chung của phần mềm (HandoverLedgerService) — không công thức riêng.
//   Phần này dùng chung cho bản thật (RealHost.cs, SQL Server) và bản tự kiểm trong bộ kiểm GiaoNhanFuzz (kịch bản `quet`).
using MES.Web.Data;
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

    public sealed class Result
    {
        public List<Finding> Findings { get; } = new();
        public int PoCount { get; set; }
        public int WtsCount { get; set; }
        public int WtsOld { get; set; }   // dòng WTS cũ chưa có QtyOk (sổ lấy QtyDone − NG)
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
                    Add(r, WtsVuot, $"{G.Label} {(s.Pg == "GC" ? "NC" : s.SubGroup)} {s.Code}: nhận vào {(s.In ?? 0):0.##} − đã ghi WTS OK {s.Ok:0.##} − đã ghi WTS NG {s.Ng:0.##} = {s.Waiting:0.##}");
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
        if (res.Findings.Count == 0) o.WriteLine("KHÔNG PHÁT HIỆN GÌ — mọi PO cân, không ô âm, không WTS vượt trần.");
    }
}
