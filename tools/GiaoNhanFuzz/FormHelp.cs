// [GN-FORM1 2026-11-05] Đáp án ĐỘC LẬP cho form "Giao / xử lý hàng" (không gọi HandoverForm — viết lại từ luật):
//   - ExpectNextNc: NC làm tiếp mặc định của vụ đọc thẳng bảng NgCases + snapshot (khách 05/11 15:39, Hùng 17:35 (d)).
//   - Expect: mỗi lựa chọn trên form → số chứng từ mới từng bảng + ô sổ nào đổi bao nhiêu (từ luật ô Excel / L1–L6),
//     so với sổ trước / sau khi lưu. Lưu không được → DB + sổ phải y nguyên (1 giao dịch).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class FormHelp
{
    static readonly StringComparison IC = StringComparison.OrdinalIgnoreCase;

    /// <summary>Chuỗi NC thường (không máy loại trừ, không dự phòng) theo StepOrder + có dòng @VAO (NC đầu là máy loại trừ) không.</summary>
    public static (List<string> Ncs, bool Vao) Chain(AppDbContext db, int id)
    {
        var excl = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay))
                     .Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
        var rows = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
        bool Ex(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && excl.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
        var ncs = rows.Where(s => !Ex(s)).Select(s => s.NC).ToList();
        return (ncs, rows.Count > 0 && Ex(rows[0]) && ncs.Count > 0);
    }

    /// <summary>NC làm tiếp mặc định (null = như hàng mới). [GN-FORM1] rà độc lập V1/V2: hàng lỗi cũ (vụ 0) → NC cuối; vụ sinh lúc nhóm làm hộ
    ///   đếm NG khi nhận hàng giao theo NC từ NC k → NC ngay sau k (máy loại trừ thì NC k; @VAO thì như hàng mới); vụ GC không NC từ nhóm sau GC → NC cuối.</summary>
    public static string? ExpectNextNc(AppDbContext db, int id, long caseId, string? holder = null)
    {
        var (ncs, _) = Chain(db, id);
        if (ncs.Count == 0) return null;
        if (caseId == 0) return null;   // vòng 3 C1b + vòng 4 V1: hàng lỗi cũ chưa mã vụ — không chứng từ cho biết đã qua NC nào → như hàng mới (chọn NC khác + lý do)
        if (caseId < 0) return null;
        var c = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId);
        if (c == null) return null;
        var hr = db.HandoverReceives.List.FirstOrDefault(r => r.NgCaseId == caseId && !r.IsVoided && r.QtyNg > 0 && r.Transaction != null
                     && !r.Transaction.IsVoided && r.Transaction.Purpose == HandoverPurpose.HelpSend && !string.IsNullOrEmpty(r.Transaction.FromNC));
        if (hr != null)
        {
            var from = hr.Transaction!.FromNC!;
            var excl = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
            var rows = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            bool Ex(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && excl.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
            int i = from.Equals("@VAO", IC) ? -1 : rows.FindIndex(r => r.NC.Equals(from, IC));
            var nx = i + 1 < rows.Count ? rows[i + 1] : null;
            if (nx != null && !Ex(nx)) return nx.NC;
            return from.Equals("@VAO", IC) ? null : ncs.FirstOrDefault(x => x.Equals(from, IC));
        }
        // [TARO-LAMHO-NC] vụ sinh từ WTS Taro LÀM HỘ bị NG (HTSP làm hỏng chiếc Gia công giao theo NC): như NG lúc nhận hàng giao theo NC —
        //   NC làm tiếp = NC ngay trước đoạn máy loại trừ (làm lại NC đó rồi giao Taro lại), đoạn ở đầu chuỗi (@VAO) → như hàng mới.
        //   Đáp án đọc thẳng snapshot (Taro: NC + mã; Gia công: máy loại trừ), không gọi sổ. (Kiểm trạm 07/11 seed 26172 PO38: đáp án cũ = NC cuối.)
        var wl = db.WtsProductionLogs.List.FirstOrDefault(w => w.NgCaseId == caseId && !w.IsVoided && (w.QtyNg ?? 0) > 0
                     && string.Equals(w.ProcessGroup, "TARO", IC) && c.DetectGroupCode == "HTSP");
        if (wl != null && !string.IsNullOrWhiteSpace(wl.WtsCode))
        {
            static string K(string s) { var t = s.Trim(); if (t.StartsWith("NC", StringComparison.OrdinalIgnoreCase)) t = t[2..]; t = t.TrimStart('0'); return t.ToUpperInvariant(); }
            var code = wl.WtsCode.Trim();
            var tnc = !string.IsNullOrWhiteSpace(wl.StepNC) ? wl.StepNC!.Trim() : null;
            var taroRows = db.KhPlanRouteSnapshotTaro.List.Where(t => t.KhPlanDetailId == id && string.Equals((string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode).Trim(), code, IC)).ToList();
            bool backup = taroRows.Any() && taroRows.All(t => t.IsBackup);
            if (tnc == null) { var ncsT = taroRows.Where(t => !t.IsBackup).Select(t => t.NC.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (ncsT.Count == 1) tnc = ncsT[0]; }
            bool prep = new[] { "1V", "1T", "1R", "1K", "1D", "1G" }.Contains(code, StringComparer.OrdinalIgnoreCase);
            if (tnc != null && !backup && !prep)
            {
                var exclT = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
                var rowsT = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
                bool ExT(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && exclT.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
                int k = rowsT.FindIndex(r => K(r.NC) == K(tnc) && ExT(r));
                if (k >= 0 && rowsT.Skip(k + 1).Any(r => !ExT(r)))
                {
                    int i = k - 1; while (i >= 0 && ExT(rowsT[i])) i--;
                    return i < 0 ? null : ncs.FirstOrDefault(x => x.Equals(rowsT[i].NC, IC));
                }
            }
        }
        switch (c.DetectGroupCode)
        {
            case "GC":
                if (string.IsNullOrWhiteSpace(c.DetectNC))
                    return c.OriginKind == NgOrigin.BaoLoi || c.CauseGroupCode is "HTSP" or "KCS" or "PKG" ? ncs.Last() : null;
                var d = c.DetectNC!.Trim();
                if (d.Equals("@VAO", IC)) return null;
                var bk = db.KhPlanRouteSnapshotMachining.List.FirstOrDefault(s => s.KhPlanDetailId == id && s.IsBackup && s.NC.Trim().Equals(d, IC) && !string.IsNullOrWhiteSpace(s.ParentNC));
                if (bk != null) d = bk.ParentNC!.Trim();
                return ncs.FirstOrDefault(x => x.Equals(d, IC));
            case "HTSP": case "KCS": case "PKG":
                return ncs.Last();
            case "KHO":   // vòng 2 V-a: Kho phát hiện ở hàng Gia công / nhóm sau giao về
                return c.CauseGroupCode is "GC" or "HTSP" or "KCS" or "PKG" ? ncs.Last() : null;
            default:
                return null;
        }
    }

    /// <summary>Mặc định cho vụ SẼ sinh khi báo lỗi ở dòng <paramref name="pos"/> của nhóm <paramref name="g"/> (đáp án độc lập).</summary>
    public static string? ExpectFreshNc(AppDbContext db, int id, string g, string pos)
    {
        var (ncs, _) = Chain(db, id);
        if (ncs.Count == 0) return null;
        if (g == "GC") return pos.StartsWith("NC:") ? (pos[3..].Equals("@VAO", IC) ? null : ncs.FirstOrDefault(x => x.Equals(pos[3..], IC))) : ncs.Last();
        return g == "KHO" ? null : ncs.Last();
    }

    /// <summary>Vị trí trên chuỗi (null = như hàng mới).</summary>
    public static int Idx(List<string> ncs, bool vao, string? c) => c == null ? (vao ? -1 : 0) : ncs.FindIndex(x => x.Equals(c, IC));

    /// <summary>Số dòng các bảng chứng từ của PO 1 (đếm cả dòng đã huỷ).</summary>
    public static string Docs(AppDbContext db, int id)
        => $"tx{db.HandoverTransactions.List.Count(t => t.KhPlanDetailId == id)} rec{db.HandoverReceives.List.Count(r => r.Transaction != null && r.Transaction.KhPlanDetailId == id)}"
         + $" bl{db.HandoverNgReports.List.Count(b => b.KhPlanDetailId == id)} kq{db.HandoverRepairResults.List.Count(k => k.KhPlanDetailId == id)}"
         + $" xn{db.HandoverRepairChecks.List.Count} vu{db.NgCases.List.Count(c => c.KhPlanDetailId == id)} wts{db.WtsProductionLogs.List.Count(w => w.KhPlanDetailId == id)}";

    /// <summary>Ô sổ so sánh: mọi nhóm + từng NC Gia công + ra khỏi PO.</summary>
    public static Dictionary<string, decimal> Cells(LedgerRow r)
    {
        var d = new Dictionary<string, decimal>();
        foreach (var g in HandoverLedgerService.GroupCodes)
        {
            var G = r.G(g);
            d[g + ".Chua"] = G.ChuaLam; d[g + ".Da"] = G.ConGiao; d[g + ".Loi"] = G.LoiGiu; d[g + ".Sua"] = G.SuaGiu;
            d[g + ".ChoNhan"] = G.ChoNhan; d[g + ".ChoKcs"] = G.ChoKcs; d[g + ".LamHo"] = G.LamHoGiu; d[g + ".Ton"] = G.Ton;
        }
        foreach (var s in r.G("GC").Steps.Where(s => !s.IsExcluded && s.Waiting != null)) d["GC.w." + s.Code] = s.Waiting!.Value;
        d["DaHuy"] = r.DaHuy; d["TraKhach"] = r.TraKhach; d["DangGiao"] = r.DangGiao; d["TpNhap"] = r.TpNhap;
        return d;
    }

    public class Expect
    {
        public Dictionary<string, decimal> Delta = new();
        public int Tx, Bl, Kq, Vu;
        /// <summary>Phiếu mới: loại phiếu + ô lấy + ô nhận (null = không kiểm).</summary>
        public string? Purpose, FromCell, ToCell;
        /// <summary>[T4] Truy vết: FromNC / ToNC phiếu (ToNcByCase = NC theo vụ, bộ kiểm tự đặt); báo lỗi: NC, nguồn, nơi gây lỗi.</summary>
        public string? TxFromNc, TxToNc, RepNc, RepOrigin, RepCause;
        public bool ToNcByCase;
        public void Add(string k, decimal v) { Delta[k] = (Delta.TryGetValue(k, out var x) ? x : 0) + v; }
    }

    /// <summary>Luật ô Excel: lựa chọn → chứng từ + ô đổi. Không gọi HandoverForm (đáp án độc lập).</summary>
    public static Expect Build(LedgerRow pre, FormGiaoRequest r)
    {
        var e = new Expect();
        var g = r.Group; var to = r.ToGroup ?? "";
        string pos = r.Pos;
        decimal n = r.Action == HandoverForm.ASua ? r.Pass + r.Fail : r.Qty;
        void ToRecv(string cellTo)
        {
            if (g == "PKG" && to == "KHO" && cellTo == HandoverCell.Moi && r.Kind == HandoverForm.Tot) e.Add("DangGiao", n);   // thành phẩm chờ Kho nhận
            else { e.Add(to + ".ChoNhan", n); e.Add(to + ".Ton", n); }
            e.Add(g + ".Ton", -n);
        }
        if (r.Kind == HandoverForm.Tot)
        {
            e.Tx = 1;
            if (pos == "LAMHO") { to = "GC"; e.Purpose = HandoverPurpose.HelpReturn; e.TxToNc = r.ToNC; e.Add(g + ".LamHo", -n); ToRecv(""); return e; }
            if (pos.StartsWith("HELP:"))
            {
                var nc = pos[5..];
                e.Purpose = HandoverPurpose.HelpSend; e.TxFromNc = nc; e.Add("GC.Chua", -n);
                var steps = pre.G("GC").Steps;
                var i = steps.FindIndex(s => s.Code == nc);
                if (HandoverLedgerService.IsVao(nc)) e.Add("GC.w." + nc, -n);
                else if (i >= 0 && i + 1 < steps.Count && !steps[i + 1].IsExcluded) e.Add("GC.w." + steps[i + 1].Code, -n);
                ToRecv(""); return e;
            }
            var tc = r.ToCell ?? HandoverCell.Moi;
            e.Purpose = HandoverPurpose.New; e.FromCell = HandoverCell.Moi; e.ToCell = tc;
            e.TxToNc = to == "GC" && tc is HandoverCell.Moi or HandoverCell.Xl ? r.ToNC : null;
            e.Add(g + ".Da", -n); ToRecv(tc); return e;
        }
        if (pos == "LAMHOLOI")
        {   // [TARO-LAMHO-NC] hàng làm hộ bị lỗi (vụ ô LAMHO, nằm trong Tồn hàng giao theo NC) → 1 phiếu Trả về Gia công theo NC từ ô Hàng lỗi → ô Hàng lỗi, cùng vụ
            e.Tx = 1; to = "GC"; e.Purpose = HandoverPurpose.HelpReturn; e.FromCell = HandoverCell.Loi; e.ToCell = HandoverCell.Loi;
            e.Add(g + ".LamHo", -n); ToRecv(HandoverCell.Loi);
            return e;
        }
        if (pos is "TONLOI" or "TONSUA")
        {
            var held = pos == "TONLOI" ? ".Loi" : ".Sua";
            switch (r.Action)
            {
                case HandoverForm.ASua:
                    e.Kq = 1;
                    e.Add(g + held, -(r.Pass + r.Fail)); e.Add(g + ".Loi", r.Fail); e.Add(g + ".ChoKcs", r.Pass);
                    return e;
                case HandoverForm.AHuy: case HandoverForm.AKhach:
                    e.Tx = 1; e.Purpose = r.Action == HandoverForm.AHuy ? HandoverPurpose.Scrap : HandoverPurpose.CustReturn; e.FromCell = HandoverCell.Loi;
                    e.Add(g + ".Loi", -n); e.Add(g + ".Ton", -n); e.Add(r.Action == HandoverForm.AHuy ? "DaHuy" : "TraKhach", n);
                    return e;
                default:
                {
                    e.Tx = 1;
                    string tc = r.Action == HandoverForm.ATraLai ? HandoverCell.Loi : r.Action == HandoverForm.ASuaXong ? HandoverCell.Xl
                              : r.ToCell ?? (r.Purpose == HandoverForm.PTra ? HandoverCell.Loi : HandoverCell.Sua);
                    e.FromCell = pos == "TONLOI" ? HandoverCell.Loi : HandoverCell.Sua; e.ToCell = tc;
                    e.Purpose = pos == "TONSUA" ? HandoverPurpose.RepairReturn : tc is HandoverCell.Xl or HandoverCell.Moi ? HandoverPurpose.RepairReturn : HandoverPurpose.RepairSend;
                    e.ToNcByCase = to == "GC" && tc is HandoverCell.Xl or HandoverCell.Moi;
                    e.Add(g + held, -n); ToRecv(tc);
                    return e;
                }
            }
        }
        // lỗi mới: ô lấy
        void Source()
        {
            if (pos == "PHOI" || pos == "DA") e.Add(g + ".Da", -n);
            else if (pos == "CHUA") e.Add(g + ".Chua", -n);
            else if (pos.StartsWith("NC:")) { e.Add("GC.Chua", -n); e.Add("GC.w." + pos[3..], -n); }
        }
        e.Vu = 1;
        Source();
        string? cause = !r.Prev ? null : g == "KHO" ? "KHACH" : r.Cause ?? (g switch { "GC" => "KHO", "HTSP" => "GC", "KCS" => "HTSP", "PKG" => "KCS", _ => null });
        e.RepNc = pos.StartsWith("NC:") ? pos[3..] : null;
        e.RepOrigin = r.Prev ? HandoverNgSource.DauVao : HandoverNgSource.DauRa; e.RepCause = cause;
        bool unworked = pos == "CHUA" || pos.StartsWith("NC:");
        if (r.Action == HandoverForm.AGiao && r.Prev && unworked && g != "KHO" && r.Purpose == HandoverForm.PTra && to == cause && (r.ToCell == null || r.ToCell == HandoverCell.Loi))
        {
            e.Tx = 1; e.Purpose = HandoverPurpose.NgReturn; e.FromCell = HandoverCell.Loi; e.ToCell = HandoverCell.Loi;
            e.TxFromNc = g == "GC" ? pos[3..] : null;
            ToRecv(HandoverCell.Loi);
            return e;
        }
        e.Bl = 1; e.Add(g + ".Loi", n);
        switch (r.Action)
        {
            case HandoverForm.AGiu: break;
            case HandoverForm.ASua:
                if (r.Pass > 0) { e.Kq = 1; e.Add(g + ".Loi", -r.Pass); e.Add(g + ".ChoKcs", r.Pass); }
                break;
            case HandoverForm.AHuy: case HandoverForm.AKhach:
                e.Tx = 1; e.Purpose = r.Action == HandoverForm.AHuy ? HandoverPurpose.Scrap : HandoverPurpose.CustReturn; e.FromCell = HandoverCell.Loi;
                e.Add(g + ".Loi", -n); e.Add(g + ".Ton", -n); e.Add(r.Action == HandoverForm.AHuy ? "DaHuy" : "TraKhach", n);
                break;
            default:
            {
                e.Tx = 1;
                var tc = r.ToCell ?? (r.Purpose == HandoverForm.PTra && g != "KHO" ? HandoverCell.Loi : HandoverCell.Sua);
                e.FromCell = HandoverCell.Loi; e.ToCell = tc;
                e.ToNcByCase = to == "GC" && tc is HandoverCell.Xl or HandoverCell.Moi;
                e.Purpose = tc is HandoverCell.Xl or HandoverCell.Moi ? HandoverPurpose.RepairReturn : HandoverPurpose.RepairSend;
                e.Add(g + ".Loi", -n); ToRecv(tc);
                break;
            }
        }
        return e;
    }

    /// <summary>[K2] NC phiếu phải mang khi hàng của vụ vào Gia công (Hàng mới / Hàng đã xử lý): NC chọn, hoặc mặc định theo vụ (đáp án độc lập). Gọi TRƯỚC khi lưu.</summary>
    public static string? CaseNcFor(AppDbContext db, FormGiaoRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.ToNC)) return r.ToNC == HandoverService.FormNewNc ? null : r.ToNC.Trim();   // giao vào Gia công: NC chọn nằm ở ToNC (như trang gửi)
        return r.CaseId is long c ? ExpectNextNc(db, r.KhPlanDetailId, c, r.Group) : ExpectFreshNc(db, r.KhPlanDetailId, r.Group, r.Pos);
    }

    /// <summary>
    /// [GN-FORM1] rà độc lập vòng 2 T3 — bất biến KHÔNG theo luật mặc định: chiếc của vụ vào Gia công không được chờ ở NC đứng SAU công đoạn xa nhất
    /// mà nó đã tới (tính thẳng từ chứng từ): phôi / Kho / khách → NC đầu; vụ Gia công có NC → NC đó (NC chính của máy dự phòng); vụ sinh khi nhận
    /// hàng giao theo NC từ NC k → NC ngay sau k; hàng đã qua nhóm sau Gia công (phát hiện / gây lỗi ở HTSP · KCS · Đóng gói, hoặc Gia công gây lỗi
    /// mà phát hiện ở nhóm khác) → NC cuối. Trả chỉ số NC xa nhất được phép (−1 = chỉ "như hàng mới"), null = không đủ dữ kiện (không kiểm).
    /// </summary>
    public static int? MaxReachIdx(AppDbContext db, int id, long caseId, string holder)
    {
        var (ncs, vao) = Chain(db, id);
        if (ncs.Count == 0) return null;
        int first = vao ? -1 : 0;   // vòng 3 L1: PO có dòng Nhận vào (NC đầu máy loại trừ) → chưa qua gì = chỉ "như hàng mới"
        if (caseId == 0) return null;   // vòng 3 C1b: hàng cũ chưa mã vụ — không có chứng từ cho mốc, không kiểm (cho chọn NC sau + lý do)
        var c = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId);
        if (c == null) return null;
        var hs = db.HandoverReceives.List.FirstOrDefault(r => r.NgCaseId == caseId && !r.IsVoided && r.Transaction != null && r.Transaction.Purpose == HandoverPurpose.HelpSend);
        if (hs != null)
        {
            var k = hs.Transaction!.FromNC ?? "";
            if (k.Equals("@VAO", IC)) return first;
            var all = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).Select(s => s.NC).ToList();
            var ki = all.FindIndex(x => x.Equals(k, IC));
            // NC ngay sau k trong chuỗi thật (có thể là máy loại trừ) → chỉ số xa nhất trong ncs = số NC thường đứng trước hoặc bằng NC đó
            var nextAll = ki + 1 < all.Count ? all[ki + 1] : all.Last();
            var reach = all.Take(all.IndexOf(nextAll) + 1).Count(x => ncs.Contains(x, StringComparer.OrdinalIgnoreCase)) - 1;
            return Math.Max(0, reach);
        }
        bool later = c.DetectGroupCode is "HTSP" or "KCS" or "PKG" || c.CauseGroupCode is "HTSP" or "KCS" or "PKG"
                     || (c.CauseGroupCode == "GC" && c.DetectGroupCode != "GC");
        if (later) return ncs.Count - 1;
        if (c.DetectGroupCode == "GC")
        {
            if (string.IsNullOrWhiteSpace(c.DetectNC)) return c.OriginKind == NgOrigin.BaoLoi ? ncs.Count - 1 : first;
            var d = c.DetectNC!.Trim();
            if (d.Equals("@VAO", IC)) return first;
            var bk = db.KhPlanRouteSnapshotMachining.List.FirstOrDefault(s => s.KhPlanDetailId == id && s.IsBackup && s.NC.Trim().Equals(d, IC) && !string.IsNullOrWhiteSpace(s.ParentNC));
            if (bk != null) d = bk.ParentNC!.Trim();
            var i = ncs.FindIndex(x => x.Equals(d, IC));
            if (i >= 0) return i;
            // NC máy loại trừ: chỉ số = số NC thường đứng trước nó
            var all = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).Select(s => s.NC).ToList();
            var ai = all.FindIndex(x => x.Equals(d, IC));
            // vòng 3 L2: NC máy loại trừ → NC thường đứng ngay trước nó (chưa được bỏ qua chính máy loại trừ)
            return ai < 0 ? null : all.Take(ai).Count(x => ncs.Contains(x, StringComparer.OrdinalIgnoreCase)) - 1;
        }
        return first;   // Kho / phôi / khách
    }

    /// <summary>[T4] Kiểm trường truy vết của chứng từ mới (phiếu cuối, báo lỗi cuối, vụ cuối). caseNc = NC theo vụ bộ kiểm tự đặt. null = khớp.</summary>
    public static string? CheckTrace(AppDbContext db, Expect e, string g, string? caseNc)
    {
        var bad = new List<string>();
        if (e.Tx == 1)
        {
            var t = db.HandoverTransactions.List.Last();
            if (e.TxFromNc != null && t.FromNC != e.TxFromNc) bad.Add($"FromNC {t.FromNC} ≠ {e.TxFromNc}");
            var wantTo = e.ToNcByCase ? caseNc : e.TxToNc;
            if (t.ToNC != wantTo) bad.Add($"ToNC {t.ToNC} ≠ {wantTo}");
        }
        if (e.Bl == 1)
        {
            var b = db.HandoverNgReports.List.Last();
            if (b.GroupCode != g || b.NC != e.RepNc || b.Origin != e.RepOrigin || b.CauseGroupCode != (e.RepOrigin == HandoverNgSource.DauVao ? e.RepCause : null))
                bad.Add($"báo lỗi {b.GroupCode} NC={b.NC} {b.Origin} do {b.CauseGroupCode} ≠ {g} NC={e.RepNc} {e.RepOrigin} do {e.RepCause}");
            var c = db.NgCases.List.Last();
            if (c.NgCaseId != b.NgCaseId || c.DetectGroupCode != g || c.DetectNC != e.RepNc || c.CauseGroupCode != (e.RepOrigin == HandoverNgSource.DauVao ? e.RepCause : g))
                bad.Add($"vụ phát hiện {c.DetectGroupCode} {c.DetectNC} do {c.CauseGroupCode}");
        }
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }

    /// <summary>So ô sổ trước / sau với đáp án: null = khớp.</summary>
    public static string? CheckCells(Dictionary<string, decimal> pre, Dictionary<string, decimal> post, Expect e)
    {
        var bad = new List<string>();
        foreach (var k in pre.Keys.Union(post.Keys))
        {
            var a = pre.TryGetValue(k, out var x) ? x : 0; var b = post.TryGetValue(k, out var y) ? y : 0;
            var want = a + (e.Delta.TryGetValue(k, out var dv) ? dv : 0);
            if (b != want) bad.Add($"{k}: {a}→{b} (đáp án {want})");
        }
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }
}
