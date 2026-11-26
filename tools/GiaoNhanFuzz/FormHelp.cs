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

    /// <summary>[KHO-TARO-THANG 2026-11-12] Đáp án độc lập (đọc thẳng snapshot, không dùng sổ / HandoverForm): PO đi đường "Kho giao thẳng Taro" =
    ///   NC đầu Gia công (dòng chính theo thứ tự) chạy máy loại trừ, sau đoạn loại trừ đầu còn NC thường, và có dòng Taro chính (không mã chuẩn bị,
    ///   không dự phòng) ở 1 NC của đoạn đầu (so NC bỏ chữ "NC", khoảng trắng, số 0 đầu — NC Gia công khác chữ cùng khoá thì không tính).
    ///   Nguồn: KHÁCH 12/11 (3.3 a = A, b = A); HÙNG 12/11 14:00.</summary>
    public static bool KhoTaroPo(AppDbContext db, int id)
    {
        var excl = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay))
                     .Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
        var all = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup && !string.IsNullOrWhiteSpace(s.NC)).OrderBy(s => s.StepOrder).ToList();
        bool Ex(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && excl.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
        if (all.Count == 0 || !Ex(all[0])) return false;
        var lead = all.TakeWhile(Ex).ToList();
        if (lead.Count == all.Count) return false;
        static string Key(string? nc)
        {
            var s0 = (nc ?? "").Trim();
            if (s0.StartsWith("NC", StringComparison.OrdinalIgnoreCase)) s0 = s0[2..];
            s0 = s0.Replace(" ", "");
            var k = s0.TrimStart('0');
            return (k.Length == 0 && s0.Length > 0 ? "0" : k).ToUpperInvariant();
        }
        var amb = all.GroupBy(s => Key(s.NC)).Where(g => g.Select(s => s.NC.Trim().ToUpperInvariant()).Distinct().Count() > 1).Select(g => g.Key).ToHashSet();
        var leadKeys = lead.Select(s => Key(s.NC)).Where(k => !amb.Contains(k)).ToHashSet();
        return db.KhPlanRouteSnapshotTaro.List.Any(t => t.KhPlanDetailId == id && !t.IsBackup && !string.IsNullOrWhiteSpace(t.NC)
            && !HandoverLedgerService.IsPrepCode((string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode)!.Trim())
            && leadKeys.Contains(Key(t.NC)));
    }

    /// <summary>[N19-BU-VU 2026-11-26] Đáp án ĐỘC LẬP (đọc thẳng bảng NgCases / HandoverNgReports / HandoverReplacements / HandoverTransactions + snapshot, không qua sổ
    ///   hay HandoverLedgerService.BuQuaGc): phôi khách gửi bù còn chờ giao Gia công theo mã vụ, mỗi vụ (chỉ vụ ĐÃ QUA Gia công, phiếu bù nhận từ G-F).
    ///   Luật (Claude tự chốt, HÙNG 26/11 03:49 OK): PO Kho giao thẳng Taro → không; Kho phát hiện → không; bộ phận sau Gia công phát hiện → có; Gia công phát hiện:
    ///   NG lúc nhận (nơi gây lỗi Kho) → không, báo lỗi / hàng lỗi công đoạn trước ở hàng chưa làm NC đầu (dòng Nhận vào khi NC đầu máy loại trừ) → không, còn lại → có.</summary>
    /// <summary>[N19-BU-VU] đáp án độc lập "vụ đã qua Gia công" (đọc bảng NgCases / HandoverNgReports + chuỗi NC).</summary>
    public static bool QuaGcDb(AppDbContext db, NgCase c, bool ktt, List<string> ncs, bool vao)
    {
        bool qua;
        if (ktt || string.IsNullOrEmpty(c.DetectGroupCode)) qua = false;
        else if (c.OriginKind is NgOrigin.Nhan or NgOrigin.NhanLoi) qua = new[] { "GC", "HTSP", "KCS", "PKG" }.Contains(c.CauseGroupCode);   // [N19 rà T1] NG lúc nhận: hàng bộ phận sản xuất giao
        else if (c.DetectGroupCode == "KHO") qua = false;
        else if (c.DetectGroupCode != "GC") qua = true;
        else if (c.OriginKind is NgOrigin.BaoLoi or NgOrigin.TraLoi
                 && !db.HandoverNgReports.List.Any(b => !b.IsVoided && b.NgCaseId == c.NgCaseId && b.SourceCell == HandoverNgSource.Da))
        {
            var dn = c.DetectNC?.Trim();
            qua = !(string.Equals(dn, "@VAO", IC) || (!vao && ncs.Count > 0 && string.Equals(ncs[0].Trim(), dn, IC)) || (string.IsNullOrEmpty(dn) && ncs.Count == 0));
        }
        else qua = true;
        return qua;
    }
    public static bool QuaGcDb(AppDbContext db, int id, long caseId)
    {
        var c = db.NgCases.List.FirstOrDefault(v => v.NgCaseId == caseId && v.KhPlanDetailId == id);
        if (c == null) return false;
        var (ncs, vao) = Chain(db, id);
        return QuaGcDb(db, c, KhoTaroPo(db, id), ncs, vao);
    }

    public static Dictionary<long, (decimal Moi, decimal Giao)> BuVuDb(AppDbContext db, int id)
    {
        var res = new Dictionary<long, (decimal, decimal)>();
        bool ktt = KhoTaroPo(db, id);
        var (ncs, vao) = Chain(db, id);
        var cut = HandoverLedgerService.BuVuFrom;
        foreach (var c in db.NgCases.List.Where(v => v.KhPlanDetailId == id))
        {
            var moi = db.HandoverReplacements.List.Where(b => b.KhPlanDetailId == id && !b.IsVoided && b.NgCaseId == c.NgCaseId && b.ReceivedAt >= cut).Sum(b => b.Qty);
            var giao = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided && t.NgCaseId == c.NgCaseId && t.ToCell == HandoverCell.BuVu).Sum(t => t.QtyIssued);
            if (moi == 0 && giao == 0) continue;
            bool qua = QuaGcDb(db, c, ktt, ncs, vao);
            if (qua) res[c.NgCaseId] = (moi, giao);
        }
        return res;
    }

    /// <summary>[GÓI 13 2026-11-10] Đáp án độc lập (đọc snapshot, không dùng HandoverForm): NC Gia công được tích "Lỗi công đoạn trước"
    ///   = NC thường đầu (có @VAO thì chỉ "@VAO") + NC thường đứng ngay sau 1 NC máy loại trừ. Khách 10/11 câu 5a.</summary>
    public static HashSet<string> GcPrevNcs(AppDbContext db, int id)
    {
        var excl = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay))
                     .Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
        var rows = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
        bool Ex(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && excl.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rows.Count == 0 || rows.All(Ex)) return res;
        if (Ex(rows[0])) res.Add("@VAO"); else res.Add(rows[0].NC.Trim());
        for (int i = 1; i < rows.Count; i++) if (!Ex(rows[i]) && Ex(rows[i - 1])) res.Add(rows[i].NC.Trim());
        return res;
    }

    /// <summary>[GÓI 13] Đáp án độc lập: Hàng mới giao ngược (Kho 0 · GC 1 · HTSP 2 · KCS 3 · Đóng gói 4; Đóng gói → Kho không ngược). Khách 10/11 câu 5.</summary>
    public static bool Nguoc(string from, string to)
    {
        if (from == "PKG" && to == "KHO") return false;
        var o = new[] { "KHO", "GC", "HTSP", "KCS", "PKG" };
        return Array.IndexOf(o, to) < Array.IndexOf(o, from);
    }

    /// <summary>[GÓI 13] Đáp án độc lập: Kho giao Gia công (như hàng mới) chỉ được NC trống hoặc NC thường đầu (không @VAO). Khách 10/11 câu 5.</summary>
    public static bool KhoNcDung(AppDbContext db, int id, string? toNc)
    {
        if (string.IsNullOrWhiteSpace(toNc)) return true;
        var (ncs, vao) = Chain(db, id);
        return !vao && ncs.Count > 0 && ncs[0].Trim().Equals(toNc.Trim(), IC);
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
            // [LAMHO-NCKEY 2026-11-08] (rà độc lập Gói 11 L3) khoá như luật mới: "NC0" / "00" → "0"; NC Taro khớp 2 NC Gia công khác chữ → không làm hộ (xuống switch)
            static string K(string s) { var t = s.Trim(); if (t.StartsWith("NC", StringComparison.OrdinalIgnoreCase)) t = t[2..]; t = t.Replace(" ", ""); var k = t.TrimStart('0'); if (k.Length == 0 && t.Length > 0) k = "0"; return k.ToUpperInvariant(); }
            var code = wl.WtsCode.Trim();
            var tnc = !string.IsNullOrWhiteSpace(wl.StepNC) ? wl.StepNC!.Trim() : null;
            var taroRows = db.KhPlanRouteSnapshotTaro.List.Where(t => t.KhPlanDetailId == id && string.Equals((string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode).Trim(), code, IC)).ToList();
            // [GOI11-FUZZ-DP 2026-11-08] dòng Taro dự phòng CŨ (dữ liệu trước TARO-DUPHONG): sổ coi như mã ở NC dòng CHA (StepNc = ParentNC — InCodeStep) → cũng là
            //   Taro làm hộ nếu NC cha là NC máy loại trừ giữa chuỗi. Trước đáp án bỏ qua dòng dự phòng → rơi xuống "HTSP phát hiện → NC cuối" (lệch sổ, sổ đúng:
            //   vụ đang giữ ở ô LAMHO). Lộ ra khi thao tác lhsua Gói 11 đi trúng đường (seed 1102 PO1 op27 vụ 3T-DP). Nguồn: CLAUDE (kiểm Gói 11).
            if (tnc == null) { var ncsT = taroRows.Select(t => (t.IsBackup ? t.ParentNC : t.NC)?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (ncsT.Count == 1) tnc = ncsT[0]; }
            bool prep = new[] { "1V", "1T", "1R", "1K", "1D", "1G" }.Contains(code, StringComparer.OrdinalIgnoreCase);
            if (tnc != null && !prep)
            {
                var exclT = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
                var rowsT = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
                bool ExT(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && exclT.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
                int k = rowsT.FindIndex(r => K(r.NC) == K(tnc) && ExT(r));
                bool trung = rowsT.Where(r => K(r.NC) == K(tnc)).Select(r => r.NC.Trim().ToUpperInvariant()).Distinct().Count() > 1;   // [LAMHO-NCKEY]
                if (k >= 0 && !trung && rowsT.Skip(k + 1).Any(r => !ExT(r)))
                {
                    int i = k - 1; while (i >= 0 && ExT(rowsT[i])) i--;
                    return i < 0 ? null : ncs.FirstOrDefault(x => x.Equals(rowsT[i].NC, IC));
                }
            }
        }
        // [N21-NGW 2026-11-25] vụ sinh từ báo lỗi "NG ở WTS Taro làm hộ chưa báo lỗi" (phiếu báo lỗi HTSP nguồn NGW, NC = NC máy loại trừ đầu đoạn): như vụ WTS
        //   Taro làm hộ kiểu cũ — NC làm tiếp = NC ngay trước đoạn; đoạn ở đầu chuỗi → như hàng mới. Đọc thẳng phiếu báo lỗi + snapshot.
        //   Trước: rơi xuống "HTSP phát hiện → NC cuối" (fuzz seed 2102 PO15 / 2104 PO27 — đáp án thiếu, sổ đúng).
        var rpL = db.HandoverNgReports.List.FirstOrDefault(b => b.NgCaseId == caseId && !b.IsVoided && b.SourceCell == HandoverNgSource.NgW && b.GroupCode == "HTSP" && !string.IsNullOrWhiteSpace(b.NC));
        if (rpL != null)
        {
            var exclL = db.KhPlanRouteSnapshotMachineExclude.List.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay)).Select(e => e.SoMay!.Trim().ToUpperInvariant()).ToHashSet();
            var rowsL = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == id && !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            bool ExL(KhPlanRouteSnapshotMachining s) => !string.IsNullOrWhiteSpace(s.MachineRegistered) && exclL.Contains(s.MachineRegistered!.Trim().ToUpperInvariant());
            int k = rowsL.FindIndex(r => r.NC.Trim().Equals(rpL.NC!.Trim(), IC) && ExL(r));
            if (k >= 0)
            {
                int i = k - 1; while (i >= 0 && ExL(rowsL[i])) i--;
                return i < 0 ? null : ncs.FirstOrDefault(x => x.Equals(rowsL[i].NC, IC));
            }
        }
        // [KTT-PHOILOI 2026-11-12] vụ sinh từ phiếu Taro trả phôi lỗi về Kho (HTSP → Kho, NC nguồn @VAO — phôi chưa qua NC nào): sửa đạt như hàng mới.
        //   Nguồn: KHÁCH 12/11 (3.3 a = A) — lỗi do phôi (nơi gây lỗi Khách hàng), không phải do HTSP làm sau NC cuối.
        if (db.HandoverTransactions.List.Any(t => t.NgCaseId == caseId && !t.IsVoided && t.Purpose == HandoverPurpose.NgReturn && t.FromGroupCode == "HTSP"
                && string.Equals(t.FromNC?.Trim(), "@VAO", IC) && t.SourceReceiveId == null))
            return null;
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

    /// <summary>[SUA-XL-GIU][SUA-OK-NC 2026-11-10] Đáp án độc lập (đọc thẳng NgCases / báo lỗi / snapshot, không gọi HandoverForm): đích hàng sửa đạt của vụ ở nhóm sửa
    ///   <paramref name="g"/>: "@XONG" (nhóm đã làm xong chiếc trước lúc phát hiện) · "@OK" (Gia công làm hỏng ở NC k) · Gia công khác: ExpectNextNc · nhóm khác: null.
    ///   Vụ làm hộ (cờ của sổ — kiểm độc lập ở bất biến LAMHO-NHAN-NG) → như cũ. Nguồn: KHÁCH 10/11 vòng 1 (2.1 A, 2.2 A); HÙNG 10/11 19:22.</summary>
    public static string? ExpectRepairTarget(AppDbContext db, int id, long caseId, string g, bool lamHo)
    {
        var legacy = g == "GC" ? ExpectNextNc(db, id, caseId, g) : null;
        if (caseId <= 0 || lamHo) return legacy;
        var c = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId);
        if (c == null) return legacy;
        static int I(string? x) => x switch { "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1 };
        int gi = I(g), di = I(c.DetectGroupCode);
        // [N23-KHO-SUA 2026-11-25] đáp án đổi theo luật N23 (mockup TH4 / TH3): Kho sửa vụ do bộ phận sản xuất phát hiện → "@XONG" (giữ ô Tồn hàng lỗi đã xử lý),
        //   trừ vụ sinh lúc bên nhận đếm NG khi nhận (chưa từng nhận — TH3) → như cũ (không dấu, đạt thành phôi còn). Trước: Kho luôn không dấu.
        //   [N23-KHO-SUA rà độc lập C1] trừ phôi lỗi Taro (PO NC đầu Taro — đọc snapshot KhoTaroPo; HTSP phát hiện, nơi gây lỗi Khách hàng) → như cũ
        if (g == "KHO") return di > 0 && c.OriginKind != NgOrigin.Nhan && !(c.DetectGroupCode == "HTSP" && c.CauseGroupCode == "KHACH" && KhoTaroPo(db, id)) ? "@XONG" : null;
        if (gi < 0 || di < 0) return legacy;
        if (g == "GC")
        {
            var (ncs, _) = Chain(db, id);
            if (ncs.Count == 0) return legacy;
            if (di > 1 || (di == 1 && string.IsNullOrWhiteSpace(c.DetectNC) && (c.OriginKind == NgOrigin.BaoLoi || c.CauseGroupCode is "HTSP" or "KCS" or "PKG"))) return "@XONG";
            if (di == 1 && NgOrigin.IsWts(c.OriginKind) && !string.IsNullOrWhiteSpace(c.DetectNC))   // [N21-NGW] + vụ báo lỗi từ NG ở WTS (đi như vụ WTS)
            {
                var d = c.DetectNC!.Trim();
                var bk = db.KhPlanRouteSnapshotMachining.List.FirstOrDefault(s => s.KhPlanDetailId == id && s.IsBackup && s.NC.Trim().Equals(d, IC) && !string.IsNullOrWhiteSpace(s.ParentNC));
                if (bk != null) d = bk.ParentNC!.Trim();
                if (ncs.Any(x => x.Equals(d, IC))) return "@OK";
            }
            return legacy;
        }
        bool fromDa = db.HandoverNgReports.List.Any(b => b.NgCaseId == caseId && !b.IsVoided && b.SourceCell == HandoverNgSource.Da);
        // [SUA-HO-TRA-NGUOC 2026-11-11] + nhóm sửa hộ đứng SAU nơi phát hiện (gi > di) cũng @XONG (giữ ô XL rồi trả ngược nơi phát hiện) — khách 11/11
        return gi != di || fromDa ? "@XONG" : null;
    }

    /// <summary>[SUA-XL-GIU] Đích hàng sửa đạt cho vụ SẼ sinh khi báo lỗi ở dòng <paramref name="pos"/> rồi sửa tại nhóm (đáp án độc lập):
    ///   "đã làm xong" (DA) → "@XONG" (Gia công / nhóm khác: nơi phát hiện đã làm xong chiếc); Gia công chờ NC → ExpectFreshNc; nhóm khác chưa làm / Kho → null.</summary>
    public static string? ExpectFreshTarget(AppDbContext db, int id, string g, string pos)
    {
        if (g == "KHO") return null;
        if (g == "GC") { var (ncs, _) = Chain(db, id);
            // [N21-NGW] NG ở WTS của NC X (Gia công làm hỏng ở NC X): sửa đạt = xong NC X (luật SUA-OK-NC của vụ WTS)
            if (pos.StartsWith("NGW:")) return ncs.Count > 0 && ncs.Any(x => x.Equals(pos[4..], IC)) ? "@OK" : null;
            return ncs.Count > 0 && pos == "DA" ? "@XONG" : ExpectFreshNc(db, id, g, pos); }
        return pos == "DA" ? "@XONG" : null;
    }

    /// <summary>[SUA-XL-GIU 2026-11-10] Đáp án độc lập (đọc chứng từ DB): Tồn hàng lỗi đã xử lý của <paramref name="group"/> = hàng đã sửa vào giữ
    ///   (KCS xác nhận đạt Kết quả sửa "@XONG" khi nhóm sửa đứng trước nơi phát hiện + lần nhận phiếu FromCell XL vào ô XL khi nhóm nhận đứng trước nơi phát hiện)
    ///   − phiếu giao từ ô XL (số giao: đủ = đã nhận, chưa đủ = max(phiếu, đã nhận)). Và phần "đã làm do sửa" của nhóm (nơi phát hiện).</summary>
    public static (decimal XlGiu, decimal DaSua) ExpectXl(AppDbContext db, int id, string group)
    {
        static int I(string? x) => x switch { "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1 };
        string? D(long? cid) => cid == null ? null : db.NgCases.List.FirstOrDefault(x => x.NgCaseId == cid)?.DetectGroupCode;
        bool FromDa(long? cid) => cid != null && db.HandoverNgReports.List.Any(b => b.NgCaseId == cid && !b.IsVoided && b.SourceCell == HandoverNgSource.Da);
        decimal giu = 0, da = 0;
        foreach (var rp in db.HandoverRepairResults.List.Where(r => r.KhPlanDetailId == id && !r.IsVoided && r.GroupCode == group && r.ToNC == "@XONG"))
        {
            var ok = db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == rp.HandoverRepairResultId && !c.IsVoided).Sum(c => c.QtyOk);
            var d = D(rp.NgCaseId);
            if (d == group) da += ok;
            else if ((I(group) > 0 || group == "KHO") && I(d) > 0) giu += ok;   // [N23-KHO-SUA] + Kho sửa vụ bộ phận sản xuất phát hiện (dấu @XONG)   // [SUA-HO-TRA-NGUOC 2026-11-11] nhóm sửa đứng trước HOẶC sau nơi phát hiện đều giữ ô XL (trước: chỉ đứng trước)
        }
        foreach (var t in db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided && t.FromCell == HandoverCell.Xl))
        {
            var recs = t.Receives.Where(r => !r.IsVoided).ToList();
            if (t.FromGroupCode == group)
            {
                var rv = recs.Sum(r => r.QtyOk + r.QtyNg);
                giu -= t.Status == "COMPLETED" ? rv : Math.Max(t.QtyIssued, rv);
            }
            if (t.ToGroupCode == group)
            {
                var d = D(t.NgCaseId);
                foreach (var r in recs.Where(r => (r.ToCell ?? t.ToCell) == HandoverCell.Xl))
                {
                    if (I(group) > 0 && I(group) < I(d)) giu += r.QtyOk;
                    else if (I(group) > 0 && group == d && (group == "KCS" || FromDa(t.NgCaseId))) da += r.QtyOk;
                }
            }
        }
        return (giu, da);
    }

    /// <summary>[XL-VUOT-CHAN] Đáp án độc lập: hàng đã sửa giao xuôi về nơi phát hiện (nhóm nhận sau nhóm giữ, không sau nơi phát hiện).</summary>
    public static bool XlDung(AppDbContext db, long caseId, string from, string to, bool htspOnlyLamHo = false)
    {
        static int I(string? x) => x switch { "KHO" => 0 /* [N23-KHO-SUA] Kho đứng đầu chuỗi — đáp án đổi 25/11 */, "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1 };
        var d = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId)?.DetectGroupCode;
        if (I(from) > 0 && I(d) > 0 && I(from) > I(d)) return to == d;   // [SUA-HO-TRA-NGUOC 2026-11-11] nhóm sửa hộ đứng sau nơi phát hiện: chỉ trả ngược đúng nơi phát hiện
        return I(from) >= 0 && I(to) > I(from) && I(d) > 0 && I(to) <= I(d) && !(htspOnlyLamHo && to == "HTSP");   // [XL-HTSP-LAMHO] · [N23-KHO-SUA] I(from) ≥ 0
    }

    /// <summary>[SUA-HO-TRA-NGUOC 2026-11-11] Đáp án độc lập (đọc NgCases + snapshot, rà độc lập Gói 16 L3): NC phiếu hàng đã sửa trả NGƯỢC về Gia công
    ///   (Gia công là nơi phát hiện) — null khi tính là xong NC (đã làm xong NC cuối lúc phát hiện / lỗi từ nhóm sau / WTS NG ở NC thường), còn lại = NC của vụ (ExpectNextNc).</summary>
    public static string? ExpectGcBack(AppDbContext db, int id, long caseId)
    {
        var c = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId);
        var (ncs, _) = Chain(db, id);
        if (c == null || c.DetectGroupCode != "GC" || ncs.Count == 0) return ExpectNextNc(db, id, caseId, "GC");
        if (string.IsNullOrWhiteSpace(c.DetectNC) && (c.OriginKind == NgOrigin.BaoLoi || c.CauseGroupCode is "HTSP" or "KCS" or "PKG")) return null;
        if (NgOrigin.IsWts(c.OriginKind) && !string.IsNullOrWhiteSpace(c.DetectNC))   // [N21-NGW] + vụ báo lỗi từ NG ở WTS
        {
            var d = c.DetectNC!.Trim();
            var bk = db.KhPlanRouteSnapshotMachining.List.FirstOrDefault(s => s.KhPlanDetailId == id && s.IsBackup && s.NC.Trim().Equals(d, IC) && !string.IsNullOrWhiteSpace(s.ParentNC));
            if (bk != null) d = bk.ParentNC!.Trim();
            if (ncs.Any(x => x.Equals(d, IC))) return null;
        }
        return ExpectNextNc(db, id, caseId, "GC");
    }

    /// <summary>[XL-TRA-NHOMSUA 2026-11-10] Đáp án độc lập (đọc DB, theo vị trí trong chuỗi): nhóm trả lại được hàng đã sửa bị hỏng lại (Workflow ON) =
    ///   nhóm có Kết quả sửa còn hiệu lực của vụ ghi dấu "@XONG" mà đứng TRƯỚC nơi phát hiện (hàng vào giữ ô XL), có lần KCS xác nhận còn hiệu lực ĐẠT &gt; 0,
    ///   và đứng trước nhóm giữ <paramref name="from"/>; rỗng = không giới hạn.</summary>
    public static List<string> XlNhomSua(AppDbContext db, long caseId, string from)
    {
        static int I(string? x) => x switch { "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1 };
        var vu = db.NgCases.List.FirstOrDefault(x => x.NgCaseId == caseId);
        var det = vu?.DetectGroupCode;
        // [SUA-HO-TRA-NGUOC 2026-11-11] nhóm sửa đứng SAU nơi phát hiện cũng giữ ô XL (dấu @XONG) → điều kiện: khác nơi phát hiện (trước: đứng trước nơi phát hiện)
        var reps = db.HandoverRepairResults.List.Where(r => r.NgCaseId == caseId && !r.IsVoided && r.ToNC == "@XONG"
                                                           && I(r.GroupCode) > 0 && I(det) > 0 && r.GroupCode != det && I(r.GroupCode) < I(from)).ToList();
        var list = reps.Where(r => db.HandoverRepairChecks.List.Any(c => c.HandoverRepairResultId == r.HandoverRepairResultId && !c.IsVoided && c.QtyOk > 0))
                   .Select(r => r.GroupCode).Distinct().ToList();
        // [XL-TRA-NOILOI 2026-11-11] + bộ phận làm lỗi (nơi gây lỗi là nhóm, khác nhóm giữ) — khách 11/11 "NG trả bộ phận làm lỗi"
        var cause = vu?.CauseGroupCode;
        if (cause is "KHO" or "GC" or "HTSP" or "KCS" or "PKG" && cause != from && !list.Contains(cause)) list.Add(cause);
        return list;
    }

    /// <summary>Mặc định cho vụ SẼ sinh khi báo lỗi ở dòng <paramref name="pos"/> của nhóm <paramref name="g"/> (đáp án độc lập).</summary>
    public static string? ExpectFreshNc(AppDbContext db, int id, string g, string pos)
    {
        var (ncs, _) = Chain(db, id);
        if (ncs.Count == 0) return null;
        if (g == "GC" && pos.StartsWith("NGW:")) return ncs.FirstOrDefault(x => x.Equals(pos[4..], IC));   // [N21-NGW] NC của NG ở WTS
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

    /// <summary>[N17-KCS-BO 2026-11-23] Ô sổ "nhìn như chứng từ kiểu cũ": bỏ dòng tự xác nhận của các Kết quả sửa mới ghi (Id &gt; minRepId) — đạt về lại
    ///   Chờ KCS. Đáp án độc lập cũ (FormHelp.Build: Kết quả sửa → Chờ KCS + đạt) áp vào góc nhìn này; phần tự xác nhận kiểm riêng (AutoOkCheck)
    ///   + kịch bản n17 (sổ N17 = sổ kiểu cũ + KCS xác nhận đạt hết).</summary>
    public static async Task<Dictionary<string, decimal>> CellsKieuCu(AppDbContext db, int id, long minRepId)
    {
        var inp = (await new HandoverLedgerService(db).GetInputForDetailAsync(id))!;
        foreach (var rp in inp.Repairs.Where(r => r.Id > minRepId)) rp.Checks.RemoveAll(c => c.Auto);
        return Cells(HandoverLedgerService.Compute(inp));
    }
    public static long MaxRepId(AppDbContext db) => db.HandoverRepairResults.List.Select(x => x.HandoverRepairResultId).DefaultIfEmpty(0).Max();
    /// <summary>[N17-KCS-BO] Mỗi Kết quả sửa mới ghi (Id &gt; minRepId) có đạt &gt; 0 phải có đúng 1 dòng tự xác nhận (đạt = số đạt, người = người ghi, cùng giờ,
    ///   chưa huỷ); đạt 0 thì không có. Ô Chờ KCS sau = trước (chứng từ mới không vào Chờ KCS). null = đúng.</summary>
    public static string? AutoOkCheck(AppDbContext db, long minRepId, Dictionary<string, decimal> pre, Dictionary<string, decimal> post)
    {
        foreach (var rp in db.HandoverRepairResults.List.Where(r => r.HandoverRepairResultId > minRepId))
        {
            var cks = db.HandoverRepairChecks.List.Where(c => c.HandoverRepairResultId == rp.HandoverRepairResultId).ToList();
            var ok = rp.QtyPass == 0 ? cks.Count == 0
                   : cks.Count == 1 && cks[0].QtyOk == rp.QtyPass && cks[0].QtyNg == 0 && cks[0].CheckedBy == rp.CreatedBy && cks[0].CheckedAt == rp.CreatedAt
                     && cks[0].Notes == HandoverService.AutoCheckNote && !cks[0].IsVoided;
            if (!ok) return $"Kết quả sửa #{rp.HandoverRepairResultId} đạt {rp.QtyPass}: dòng xác nhận {string.Join(",", cks.Select(c => $"{c.QtyOk}/{c.QtyNg} by{c.CheckedBy} {c.Notes}"))}";
        }
        foreach (var g in HandoverLedgerService.GroupCodes)
            if (post[g + ".ChoKcs"] != pre[g + ".ChoKcs"]) return $"{g}.ChoKcs {pre[g + ".ChoKcs"]} → {post[g + ".ChoKcs"]} (chứng từ mới không vào Chờ KCS)";
        return null;
    }

    /// <summary>Ô sổ so sánh: mọi nhóm + từng NC Gia công + ra khỏi PO.</summary>
    public static Dictionary<string, decimal> Cells(LedgerRow r)
    {
        var d = new Dictionary<string, decimal>();
        foreach (var g in HandoverLedgerService.GroupCodes)
        {
            var G = r.G(g);
            d[g + ".Chua"] = G.ChuaLam; d[g + ".Da"] = G.ConGiao; d[g + ".Loi"] = G.LoiGiu; d[g + ".Sua"] = G.SuaGiu;
            d[g + ".ChoNhan"] = G.ChoNhan; d[g + ".ChoKcs"] = G.ChoKcs; d[g + ".LamHo"] = G.LamHoGiu; d[g + ".Ton"] = G.Ton;
            d[g + ".Xl"] = G.XlGiu;   // [SUA-XL-GIU]
            d[g + ".Ngw"] = G.NgChuaBao;   // [N21-NGW] NG ở WTS chưa báo lỗi
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
        /// <summary>[N21-NGW] Báo lỗi từ NG ở WTS chưa báo lỗi: phiếu báo lỗi nguồn NGW, vụ nguồn "Báo lỗi NG ở WTS".</summary>
        public bool RepNgw;
        /// <summary>[QF-KIEM-02 2026-11-16] Hàng đã sửa (ô XL) hàng tốt trả về Gia công có NC: máy chủ tự điền NC của chiếc lỗi ([SUA-HO-TRA-NGUOC] Gói 16 —
        /// chiếc đã xong NC k / NC cuối → không NC). Đáp án: ToNC trống hoặc = NC theo vụ (ExpectNextNc); đáp án cũ đòi trống → SAI giả (kiem trạm 16/11 08:09 seed 15394 PO8; 84d9b9e y hệt).</summary>
        public bool XlToGc; public long XlCase;
        public void Add(string k, decimal v) { Delta[k] = (Delta.TryGetValue(k, out var x) ? x : 0) + v; }
    }

    /// <summary>Luật ô Excel: lựa chọn → chứng từ + ô đổi. Không gọi HandoverForm (đáp án độc lập).</summary>
    /// <param name="khoTaro">[KHO-TARO-THANG] PO đi đường Kho giao thẳng Taro (đáp án độc lập KhoTaroPo — người gọi tính).</param>
    public static Expect Build(LedgerRow pre, FormGiaoRequest r, bool khoTaro = false)
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
        if (pos == "TONXL")
        {   // [SUA-XL-GIU][XL-TRA-LAI] hàng đã sửa (vụ) → phiếu Trả hàng đã sửa từ ô Hàng lỗi đã xử lý: hàng tốt → ô Hàng lỗi đã xử lý, hàng lỗi → ô Hàng lỗi
            e.Tx = 1; e.Purpose = HandoverPurpose.RepairReturn; e.FromCell = HandoverCell.Xl;
            e.ToCell = r.Kind == HandoverForm.Tot ? HandoverCell.Xl : HandoverCell.Loi;
            if (r.Kind == HandoverForm.Tot && to == "GC" && r.CaseId is long xc) { e.XlToGc = true; e.XlCase = xc; }   // [QF-KIEM-02]
            e.Add(g + ".Xl", -n); ToRecv(e.ToCell);
            return e;
        }
        if (r.Kind == HandoverForm.Tot)
        {
            e.Tx = 1;
            if (pos == "LAMHO") { to = "GC"; e.Purpose = HandoverPurpose.HelpReturn; e.TxToNc = r.ToNC; e.Add(g + ".LamHo", -n); ToRecv(""); return e; }
            if (khoTaro && g == "KHO" && pos == "PHOI" && to == "HTSP")
            {   // [KHO-TARO-THANG] Kho giao thẳng Taro: phiếu giao theo NC do Kho lập (từ ô phôi còn, NC nguồn @VAO), HTSP Chờ nhận (Tồn bên nhận)
                e.Purpose = HandoverPurpose.HelpSend; e.FromCell = HandoverCell.Moi; e.TxFromNc = HandoverLedgerService.VaoCode;
                e.Add("KHO.Da", -n); ToRecv(""); return e;
            }
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
            if (g == "KHO" && pos == "PHOI" && r.CaseId is long bvc && bvc > 0)
            {   // [N19-BU-VU 2026-11-26] Kho giao phôi bù theo mã vụ: phiếu Hàng mới ô chỉ định XLBU, NC nhận = NC đầu (không NC khi NC đầu là máy loại trừ — dòng "Nhận vào")
                e.Purpose = HandoverPurpose.New; e.FromCell = HandoverCell.Moi; e.ToCell = HandoverCell.BuVu;
                var st0 = pre.G("GC").Steps;
                e.TxToNc = st0.Any(x => HandoverLedgerService.IsVao(x.Code)) ? null : st0.FirstOrDefault(x => !x.IsExcluded && !x.IsPrep)?.Code;
                e.Add("KHO.Da", -n); ToRecv(HandoverCell.Xl); return e;
            }
            var tc = r.ToCell ?? HandoverCell.Moi;
            e.Purpose = HandoverPurpose.New; e.FromCell = HandoverCell.Moi; e.ToCell = tc;
            e.TxToNc = to == "GC" && tc is HandoverCell.Moi or HandoverCell.Xl ? r.ToNC : null;
            e.Add(g + ".Da", -n); ToRecv(tc); return e;
        }
        if (pos == "LHKHO")
        {   // [KTT-PHOILOI] Taro trả phôi lỗi (hàng đoạn đầu chuỗi) → 1 phiếu Hàng lỗi công đoạn trước HTSP → Kho ô Hàng lỗi, vụ mới; lấy từ Tồn hàng giao theo NC
            e.Tx = 1; e.Vu = 1; to = "KHO"; e.Purpose = HandoverPurpose.NgReturn; e.FromCell = HandoverCell.Loi; e.ToCell = HandoverCell.Loi; e.TxFromNc = HandoverLedgerService.VaoCode;
            e.Add(g + ".LamHo", -n); ToRecv(HandoverCell.Loi);
            return e;
        }
        if (pos.StartsWith("NGWLH:"))
        {   // [N21-NGW] NG ở WTS Taro làm hộ chưa báo lỗi: báo lỗi (vụ làm hộ giữ trong Tồn hàng giao theo NC — không ô nào đổi) [+ trả Gia công ô Hàng lỗi, cùng vụ]
            e.Vu = 1; e.Bl = 1; e.RepNc = pos[6..]; e.RepNgw = true; e.RepOrigin = r.Prev ? HandoverNgSource.DauVao : HandoverNgSource.DauRa; e.RepCause = r.Prev ? r.Cause?.Trim() : null;
            if (r.Action == HandoverForm.AGiao) { e.Tx = 1; to = "GC"; e.Purpose = HandoverPurpose.HelpReturn; e.FromCell = HandoverCell.Loi; e.ToCell = HandoverCell.Loi; e.Add(g + ".LamHo", -n); ToRecv(HandoverCell.Loi); }
            return e;
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
            else if (pos.StartsWith("NGW:"))
            {   // [N21-NGW] đáp án luật N21: lấy NG ở WTS chưa báo lỗi trước; phần dư = hàng đã làm xong — Gia công NC giữa: chiếc đã xong NC đó (chờ NC sau,
                //   ra khỏi Chưa GC); NC cuối / nhóm khác: Đã làm chưa giao. Số NG chưa báo đầu vào = ô sổ trước khi lưu (như mọi ô "pre").
                var nk = pos[4..];
                var have = pre.NgPendAt(g, nk == "" ? null : nk);
                var take = Math.Min(n, have); var exc = n - take;
                e.Add(g + ".Ngw", -take);
                if (exc > 0)
                {
                    var st = pre.G("GC").Steps;
                    var i = g == "GC" ? st.FindIndex(x => x.Code == nk && !x.IsExcluded) : -1;
                    var lastN = st.LastOrDefault(x => !x.IsExcluded && !HandoverLedgerService.IsVao(x.Code));
                    if (g == "GC" && i >= 0 && lastN != null && lastN.Code != nk)
                    {
                        e.Add("GC.Chua", -exc);
                        if (i + 1 < st.Count && !st[i + 1].IsExcluded) e.Add("GC.w." + st[i + 1].Code, -exc);
                    }
                    else e.Add(g + ".Da", -exc);
                }
            }
        }
        e.Vu = 1;
        Source();
        // [LAMHO-L2-KCS 2026-11-07] HTSP chỉ có mã Taro làm hộ (mọi mã chính của HTSP thuộc đoạn máy loại trừ giữa chuỗi): hàng Gia công giao thẳng KCS
        //   → bỏ trống nơi gây lỗi ở KCS = Gia công (trước: HTSP). Đáp án đọc dòng sổ trực tiếp (không gọi HandoverForm.PrevGroup).
        // [GOI8-THAP-E 2026-11-08] + PO không có công đoạn HTSP của mình (không mã chính nào ngoài chuẩn bị / làm hộ — gồm PO không có snapshot HTSP) → KCS ← Gia công
        //   (rà độc lập Gói 10 T1) PO không có mã HTSP nhưng HTSP đã nhận làm hàng thật (Hàng mới / Hàng lỗi xử lý / làm hộ về > 0) → vẫn HTSP.
        // [CAUSE-BAT-BUOC 2026-11-10] nơi gây lỗi bỏ trống bị máy chủ chặn (người gọi đặt expErr "nơi gây lỗi" — Build chỉ dựng đáp án ca lưu được) → không còn mặc định nhóm trước
        string? cause = !r.Prev ? null : g == "KHO" ? "KHACH" : r.Cause?.Trim();
        e.RepNc = pos.StartsWith("NC:") ? pos[3..] : pos.StartsWith("NGW:") && pos.Length > 4 ? pos[4..] : null;   // [N21-NGW] + NC của NG ở WTS
        e.RepNgw = pos.StartsWith("NGW:");
        e.RepOrigin = r.Prev ? HandoverNgSource.DauVao : HandoverNgSource.DauRa; e.RepCause = cause;
        bool unworked = pos == "CHUA" || pos.StartsWith("NC:");
        if (r.Action == HandoverForm.AGiao && r.Prev && unworked && g != "KHO" && r.Purpose == HandoverForm.PTra && to == cause && (r.ToCell == null || r.ToCell == HandoverCell.Loi))
        {
            e.Tx = 1; e.Purpose = HandoverPurpose.NgReturn; e.FromCell = HandoverCell.Loi; e.ToCell = HandoverCell.Loi;
            // [QF-KIEM-01 2026-11-15] đáp án sai: pos == "CHUA" (Gia công chỉ có dòng CHUA khi PO toàn máy loại trừ, HandoverForm.cs:193–210 → không có NC)
            //   bị cắt "CHUA"[3..] = "A" → báo SAI oan (kiem 15/11 08:06 seed 26285 PO16 op59). Chỉ lấy NC khi pos là "NC:…" (như RepNc ở trên); app đúng, không sửa app.
            e.TxFromNc = g == "GC" && pos.StartsWith("NC:") ? pos[3..] : null;
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
            if (e.XlToGc)
            {   // [QF-KIEM-02] máy chủ tự điền NC của chiếc lỗi hoặc để trống (đã xong NC) — chấp nhận trống hoặc đúng NC theo vụ, NC khác là sai
                var nxc = ExpectNextNc(db, t.KhPlanDetailId, e.XlCase, g);
                if (t.ToNC != null && !string.Equals(t.ToNC, nxc, IC)) bad.Add($"ToNC {t.ToNC} ≠ (trống hoặc {nxc})");
            }
            else if (t.ToNC != wantTo) bad.Add($"ToNC {t.ToNC} ≠ {wantTo}");
        }
        if (e.Bl == 1)
        {
            var b = db.HandoverNgReports.List.Last();
            if (b.GroupCode != g || b.NC != e.RepNc || b.Origin != e.RepOrigin || b.CauseGroupCode != (e.RepOrigin == HandoverNgSource.DauVao ? e.RepCause : null))
                bad.Add($"báo lỗi {b.GroupCode} NC={b.NC} {b.Origin} do {b.CauseGroupCode} ≠ {g} NC={e.RepNc} {e.RepOrigin} do {e.RepCause}");
            var c = db.NgCases.List.Last();
            if (c.NgCaseId != b.NgCaseId || c.DetectGroupCode != g || c.DetectNC != e.RepNc || c.CauseGroupCode != (e.RepOrigin == HandoverNgSource.DauVao ? e.RepCause : g))
                bad.Add($"vụ phát hiện {c.DetectGroupCode} {c.DetectNC} do {c.CauseGroupCode}");
            if ((b.SourceCell == HandoverNgSource.NgW) != e.RepNgw || (c.OriginKind == NgOrigin.WtsBao) != e.RepNgw)   // [N21-NGW]
                bad.Add($"báo lỗi nguồn {b.SourceCell} / vụ {c.OriginKind} ≠ {(e.RepNgw ? "NGW / WTSBL" : "khác NGW")}");
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
