using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [GN-FORM1 2026-11-05] FORM "GIAO / XỬ LÝ HÀNG" — 1 FORM 4 BƯỚC (khách anh Ngọc duyệt mockup 05/11, Hùng OK kế hoạch 17:35)
    //   Nguồn: KHÁCH 05/11 15:43 (K1 gộp 1 form) · 15:39 (K2 Gia công chọn theo "đã làm xong NC nào", sửa xong làm tiếp đúng NC kế)
    //          · HÙNG 16:43 (K3 chữ bước 2 theo nhóm, N5 giữ) · HÙNG 17:35 (a)–(d).
    //   File này CHỈ ĐỌC các ô sổ đã tính trong HandoverLedgerService.Compute (không công thức số riêng — L6):
    //     - FormPositions: các dòng bước 2 "hàng / chiếc lỗi đang ở đâu", mỗi dòng = 1 ô sổ, số = trần của chứng từ sẽ lập từ ô đó.
    //     - GcStageText: chữ bước Gia công "Đã làm xong NC k · chưa làm NC k+1".
    //     - CaseNextNc: NC làm tiếp mặc định của hàng sửa đạt ở Gia công + các NC được chọn (chỉ lùi, không tiến).
    //   Máy chủ (HandoverService.SaveFormAsync) dùng đúng các hàm này để ánh xạ lựa chọn → chứng từ, trang dùng để hiện.
    // =====================================================================

    /// <summary>[GN-FORM1] 1 dòng bước 2 của form.</summary>
    public class FormPos
    {
        /// <summary>Mã dòng: PHOI | CHUA | DA | NC:&lt;mã&gt; (GC: chiếc đang chờ NC này) | HELP:&lt;mã&gt; (GC: giao theo NC từ NC này) | LAMHO | TONLOI | TONSUA.</summary>
        public string  Id     { get; set; } = "";
        public string  Label  { get; set; } = "";
        /// <summary>Dòng nhỏ chỉ để xem (HTSP: còn mã nào).</summary>
        public string  Sub    { get; set; } = "";
        /// <summary>Số đang có ở ô = trần của chứng từ lập từ dòng này.</summary>
        public decimal Have   { get; set; }
        /// <summary>Hàng lỗi mới phát hiện (chưa có vụ): báo lỗi lấy từ ô này.</summary>
        public bool    Fresh  { get; set; }
        /// <summary>Báo lỗi: nguồn (PHOI / CHUA / DA) + NC (GC).</summary>
        public string? NgSource { get; set; }
        public string? NgNc   { get; set; }
        /// <summary>Chiếc chưa làm (CHUA / GC chờ NC): tích "Lỗi công đoạn trước" + trả đúng nơi gây lỗi = 1 phiếu Hàng lỗi công đoạn trước.</summary>
        public bool    Unworked { get; set; }
        /// <summary>Đã làm xong NC cuối của Gia công (sửa đạt: làm lại NC cuối — câu chờ khách).</summary>
        public bool    IsLast { get; set; }
        /// <summary>Ô đang giữ theo vụ: LOI | SUA.</summary>
        public string? HeldCell { get; set; }
        /// <summary>Hàng tốt: giao theo NC (làm hộ) từ NC này.</summary>
        public string? HelpFrom { get; set; }
        /// <summary>Hàng tốt: hàng Gia công giao theo NC đang giữ → trả về Gia công.</summary>
        public bool    LamHo  { get; set; }
    }

    public static class HandoverForm
    {
        public const string Tot = "TOT";
        public const string Loi = "LOI";

        // Bước 3 — làm gì
        public const string AGiao   = "GIAO";     // giao nhóm khác
        public const string ASua    = "SUA";      // sửa tại nhóm (Kết quả sửa)
        public const string AGiu    = "GIU";      // chỉ báo lỗi, giữ lại
        public const string AHuy    = "HUY";      // huỷ
        public const string AKhach  = "KHACH";    // trả khách
        public const string ATraLai = "TRALAI";   // Tồn hàng sửa: trả lại nơi gửi (chưa sửa được) → ô Hàng lỗi bên kia
        public const string ASuaXong = "SUAXONG"; // Tồn hàng sửa: sửa xong, trả nơi gửi → ô Hàng lỗi đã xử lý bên kia ((a) Hùng 17:35)

        // Bước 4 — giao hàng lỗi: trả nơi gây lỗi (ô Hàng lỗi) / gửi nhờ sửa (ô Hàng sửa)
        public const string PTra = "TRA";
        public const string PNho = "NHO";

        public const int NcReasonMin = 3;

        // ================================================================
        // Chuỗi NC Gia công (đúng thứ tự sổ: dòng ảo @VAO + NC chính, NC máy loại trừ giữ để ghi chữ)
        // ================================================================
        public record GcNode(string Code, bool Excluded, string? Machine);

        /// <summary>Chuỗi Gia công từ sổ đã tính (row.G("GC").Steps — đã có @VAO khi NC đầu là máy loại trừ).</summary>
        public static List<GcNode> GcChain(LedgerRow row)
            => row.G("GC").Steps.Select(s => new GcNode(s.Code, s.IsExcluded, s.Machine)).ToList();

        /// <summary>Chuỗi Gia công từ đầu vào sổ (cùng luật BuildGcSteps: NC chính theo StepOrder, @VAO khi NC đầu là máy loại trừ).</summary>
        public static List<GcNode> GcChain(LedgerInput inp)
        {
            var all = inp.GcSteps.Where(s => !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            var list = new List<GcNode>();
            if (all.Count > 0 && all[0].IsExcluded && all.Any(s => !s.IsExcluded))
                list.Add(new GcNode(HandoverLedgerService.VaoCode, false, null));
            list.AddRange(all.Select(s => new GcNode(s.NC, s.IsExcluded, s.Machine)));
            return list;
        }

        static string Nc(string code) => HandoverLedgerService.NcName(code);

        static string ExclText(GcNode n) => Nc(n.Code) + " (máy loại trừ" + (string.IsNullOrEmpty(n.Machine) ? "" : " " + n.Machine) + ", làm hộ)";

        /// <summary>
        /// [K3] Chữ bước của chiếc đang CHỜ NC <paramref name="code"/> ở Gia công:
        /// "Vừa nhận, chưa làm NC1" · "Đã làm xong NC1 · chưa làm NC2" · "Nhận vào (chưa qua NC)".
        /// </summary>
        public static string GcStageText(List<GcNode> chain, string? code)
        {
            if (string.IsNullOrEmpty(code)) return "Gia công";
            if (HandoverLedgerService.IsVao(code)) return HandoverLedgerService.VaoLabel;
            var i = chain.FindIndex(n => string.Equals(n.Code, code, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return "chưa làm " + Nc(code);
            var p = i > 0 ? chain[i - 1] : null;
            if (p == null || HandoverLedgerService.IsVao(p.Code)) return "Vừa nhận, chưa làm " + Nc(chain[i].Code);
            return "Đã làm xong " + (p.Excluded ? ExclText(p) : Nc(p.Code)) + " · chưa làm " + Nc(chain[i].Code);
        }

        /// <summary>[K3] "Đã làm xong NC3 (NC cuối) · chưa giao".</summary>
        public static string GcDoneText(List<GcNode> chain)
        {
            var last = chain.LastOrDefault(n => !n.Excluded && !HandoverLedgerService.IsVao(n.Code));
            return last == null ? "Đã làm xong · chưa giao" : "Đã làm xong " + Nc(last.Code) + " (NC cuối) · chưa giao";
        }

        /// <summary>Dòng hàng tốt giao theo NC từ NC <paramref name="code"/>: chữ theo chiếc đang nằm sau NC đó.</summary>
        static string HelpText(List<GcNode> chain, string code)
        {
            var i = chain.FindIndex(n => n.Code == code);
            var next = i >= 0 && i + 1 < chain.Count ? chain[i + 1] : null;
            var head = HandoverLedgerService.IsVao(code) ? HandoverLedgerService.VaoLabel : "Đã làm xong " + Nc(code);
            if (next == null) return head;
            return next.Excluded ? head + " · chờ giao làm " + ExclText(next).Replace(", làm hộ)", ")")
                                 : head + " · chưa làm " + Nc(next.Code);
        }

        /// <summary>[K3] Chữ 2 dòng chính của nhóm không phải Gia công: (chưa xong, đã xong).</summary>
        public static (string Chua, string Da) GroupStageText(string group) => group switch
        {
            "HTSP" => ("Chưa làm xong (gồm cả đang làm dở)", "Đã làm xong · chưa giao"),
            "KCS"  => ("Chưa kiểm xong", "Đã kiểm xong · chưa giao"),
            "PKG"  => ("Chưa đóng gói xong", "Đã đóng gói · chưa giao"),
            "GC"   => ("Chưa gia công xong", "Đã làm xong · chưa giao"),   // Gia công chưa có snapshot NC
            _      => ("Chưa làm xong", "Đã làm xong · chưa giao")
        };

        public const string TonLoiText = "Đang ở Tồn hàng lỗi (đã có vụ)";
        public const string TonSuaText = "Đang ở Tồn hàng sửa (nhóm khác gửi nhờ sửa)";
        public const string LamHoText  = "Đang giữ hàng Gia công giao theo NC (làm hộ)";
        public const string LamHoLoiText = "Hàng làm hộ bị lỗi (Taro làm hộ NG, đã có vụ)";   // [TARO-LAMHO-NC]

        static decimal Pos0(decimal v) => Math.Max(0, v);

        /// <summary>
        /// [GN-FORM1] Các dòng bước 2. <paramref name="good"/> = Hàng tốt (bước 1). Số = trần chứng từ lập từ dòng đó (đúng hàm trần của sổ).
        /// Không đổi: dòng nào có số 0 vẫn hiện (khoá) cho người dùng thấy hàng đang không ở đó.
        /// </summary>
        public static List<FormPos> FormPositions(LedgerRow row, string group, bool good)
        {
            var G = row.G(group);
            var list = new List<FormPos>();
            if (good)
            {
                if (group == "KHO")
                    list.Add(new FormPos { Id = "PHOI", Label = "Phôi còn (chưa giao)", Have = Pos0(G.ConGiao) });
                else if (group == "GC" && G.Steps.Any(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)))
                {
                    var chain = GcChain(row);
                    var normals = G.Steps.Where(s => !s.IsExcluded).ToList();
                    var lastNormal = normals.Last(s => !HandoverLedgerService.IsVao(s.Code));
                    // (a) Hùng 17:35: giữ giao theo NC (làm hộ) — chiếc đã làm xong NC k, giao nhóm khác làm hộ NC sau (trần = IssueCap HelpSend, N3/N4)
                    foreach (var s in normals.Where(s => s.Code != lastNormal.Code))
                    {
                        var cap = HandoverLedgerService.IssueCap(row, "GC", HandoverPurpose.HelpSend, s.Code).Max;
                        list.Add(new FormPos { Id = "HELP:" + s.Code, Label = HelpText(chain, s.Code), Sub = "giao theo NC cho nhóm khác làm hộ (ghi chú bắt buộc)",
                                               Have = Pos0(cap), HelpFrom = s.Code });
                    }
                    list.Add(new FormPos { Id = "DA", Label = GcDoneText(chain), Have = Pos0(G.ConGiao), IsLast = true });
                }
                else
                    list.Add(new FormPos { Id = "DA", Label = GroupStageText(group).Da, Have = Pos0(G.ConGiao) });
                if (group != "GC" && G.LamHoGiu != 0)
                {
                    // [TARO-LAMHO-NC] số = trần trả hàng tốt (trừ chiếc làm hộ bị lỗi; câu 1: ≤ OK bộ Taro đoạn, NC nhận mặc định = NC sau đoạn)
                    var taroRuns = row.LamHoRuns.Where(r => r.HasTaro).ToList();
                    var defNc = group == "HTSP" && taroRuns.Count == 1 ? taroRuns[0].NextNc : null;
                    // nhiều đoạn Taro: số lớn nhất trả được vào 1 NC thường (mỗi NC có trần riêng — rà vòng 2 L2)
                    // PO có Taro làm hộ: số lớn nhất trả được vào 1 NC thường (mỗi NC có trần riêng; hàng đoạn khác / không thuộc đoạn — rà vòng 2 L2, vòng 3 T1)
                    var cap = taroRuns.Count == 0 ? HandoverLedgerService.IssueCap(row, group, HandoverPurpose.HelpReturn, null, null, defNc).Max
                            : row.G("GC").Steps.Where(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code))
                                 .Select(s => HandoverLedgerService.IssueCap(row, group, HandoverPurpose.HelpReturn, null, null, s.Code).Max).DefaultIfEmpty(0).Max();
                    list.Add(new FormPos { Id = "LAMHO", Label = LamHoText, Sub = "trả về Gia công, chọn NC nhận vào" + (defNc != null ? $" (Taro làm hộ xong bộ → {HandoverLedgerService.NcName(defNc)})" : ""),
                                           Have = Pos0(cap), LamHo = true });
                }
                return list;
            }

            // Hàng lỗi
            if (group == "KHO")
                list.Add(new FormPos { Id = "PHOI", Label = "Phôi còn (chưa giao)", Have = Pos0(G.ConGiao), Fresh = true, NgSource = HandoverNgSource.Phoi });
            else if (group == "GC" && G.Steps.Any(s => !s.IsExcluded))
            {
                var chain = GcChain(row);
                foreach (var s in G.Steps.Where(s => !s.IsExcluded && s.Waiting != null))
                {
                    var cap = HandoverLedgerService.NgReportCap(row, "GC", HandoverNgSource.Chua, s.Code).Max;
                    list.Add(new FormPos { Id = "NC:" + s.Code, Label = GcStageText(chain, s.Code), Have = Pos0(cap),
                                           Fresh = true, NgSource = HandoverNgSource.Chua, NgNc = s.Code, Unworked = true });
                }
                list.Add(new FormPos { Id = "DA", Label = GcDoneText(chain), Have = Pos0(G.ConGiao), Fresh = true, NgSource = HandoverNgSource.Da, IsLast = true });
            }
            else
            {
                var (chua, da) = GroupStageText(group);
                list.Add(new FormPos { Id = "CHUA", Label = chua, Sub = group == "HTSP" ? HtspSub(G) : "", Have = Pos0(G.ChuaLam),
                                       Fresh = true, NgSource = HandoverNgSource.Chua, Unworked = true });
                list.Add(new FormPos { Id = "DA", Label = da, Have = Pos0(G.ConGiao), Fresh = true, NgSource = HandoverNgSource.Da, IsLast = group == "GC" });
            }
            list.Add(new FormPos { Id = "TONLOI", Label = TonLoiText, Have = Pos0(G.LoiGiu), HeldCell = HandoverCell.Loi });
            list.Add(new FormPos { Id = "TONSUA", Label = TonSuaText, Have = Pos0(G.SuaGiu), HeldCell = HandoverCell.Sua });
            // [TARO-LAMHO-NC] chiếc làm hộ bị lỗi (vụ do nhóm làm hộ gây) nằm trong Tồn hàng giao theo NC → trả Gia công vào ô Hàng lỗi
            var lhNg = row.LamHoNgHeld(group);
            if (group != "GC" && lhNg != 0)
                list.Add(new FormPos { Id = "LAMHOLOI", Label = LamHoLoiText, Sub = "nằm trong Tồn hàng giao theo NC — trả về Gia công vào ô Hàng lỗi", Have = Pos0(lhNg), HeldCell = HandoverCell.LamHo });
            return list;
        }

        /// <summary>[K3] HTSP: "còn Taro 1T: 2 · Bavia 2V: 3 · Rửa 1R: 5" — chỉ xem (N5: thứ tự tự do, không đếm theo bước).</summary>
        static string HtspSub(LedgerGroup G)
        {
            var parts = G.Steps.Where(s => !s.IsPrep && !s.IsExcluded && s.Waiting != null && s.LamHoNc == null)   // [TARO-LAMHO-NC] mã Taro làm hộ không thuộc hàng HTSP
                               .Select(s => $"{s.SubGroup} {s.Code}: {s.Waiting!.Value:0.##}").ToList();
            return parts.Count == 0 ? "" : "còn " + string.Join(" · ", parts);
        }

        // ================================================================
        // [K2] NC làm tiếp của hàng sửa đạt ở Gia công (khách 15:39 "NC2 chứ không phải NC1 hay NC3")
        //   Mặc định = NC của vụ: chiếc lỗi đang chờ NC nào (vụ phát hiện ở Gia công: DetectNC = NC đang chờ / NC chạy hỏng)
        //   thì sửa xong làm tiếp đúng NC đó. Vụ phát hiện ở Gia công lúc đã làm xong NC cuối, hoặc phát hiện SAU Gia công
        //   (HTSP / KCS / Đóng gói) → làm lại NC cuối ((d), chờ khách chốt). Vụ phát hiện ở Kho / phôi / không rõ → như hàng mới (null).
        //   Chỉ cho đổi sang NC ĐỨNG TRƯỚC (làm lại) + bắt lý do; NC sau bị chặn (máy chủ kiểm).
        // ================================================================
        public record NcOpt(string? Code, string Label, bool Default, bool Earlier);

        /// <summary>Các NC thường (không máy loại trừ, không @VAO) của Gia công theo thứ tự.</summary>
        static List<string> NormalNcs(LedgerRow row)
            => row.G("GC").Steps.Where(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code)).Select(s => s.Code).ToList();

        static bool HasVao(LedgerRow row) => row.G("GC").Steps.Any(s => HandoverLedgerService.IsVao(s.Code));

        /// <summary>Vị trí trên chuỗi: null (như hàng mới) = trước NC đầu (có @VAO) hoặc = NC đầu (không có @VAO).</summary>
        static int NcIdx(LedgerRow row, List<string> ncs, string? code)
        {
            if (string.IsNullOrEmpty(code)) return HasVao(row) ? -1 : 0;
            var i = ncs.FindIndex(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MinValue : i;
        }

        /// <summary>NC chính của mã WTS (máy đồng dạng gộp về NC chính — N2).</summary>
        static string MainNc(LedgerRow row, string code)
            => row.CodeMain.TryGetValue("GC|" + code, out var m) ? m : code;

        /// <summary>[K2] NC làm tiếp mặc định cho hàng sửa đạt của vụ (Gia công). null = như hàng mới.</summary>
        /// <param name="holder">Nhóm đang giữ hàng (chỉ dùng cho hàng lỗi cũ vụ 0).</param>
        public static string? CaseNextNc(LedgerRow row, long caseId, string? holder = null)
        {
            var ncs = NormalNcs(row);
            if (ncs.Count == 0) return null;
            // [GN-FORM1] rà độc lập V2 + vòng 2 C1 + vòng 3 C1b: hàng lỗi cũ chưa có mã vụ — không có chứng từ cho biết đã qua NC nào:
            //   nhóm nào giữ cũng có thể là phôi Kho gửi sang (rà vòng 4 V1: phôi cũ Kho → HTSP → Gia công vào NC cuối) → luôn như hàng mới,
            //   và vì không có mốc nên cho chọn NC khác (kể cả NC sau) kèm lý do — xem CheckNextNc(free). Không bao giờ tự bỏ qua NC. (chờ Hùng chốt)
            //   Trước vòng 3: Gia công giữ → NC cuối → phôi lỗi cũ Kho gửi Gia công sửa, sửa đạt vào chờ NC cuối, bỏ qua mọi NC.
            //   holder giữ lại tham số (chỗ gọi đã truyền) để Hùng đổi luật theo nhóm giữ nếu muốn.
            if (caseId == 0) return null;
            if (!row.Cases.TryGetValue(caseId, out var cs) || caseId < 0) return null;
            return CaseNextNc(ncs, cs.DetectGroup, cs.DetectNc, cs.Origin, cs.Cause, cs.HelpFromNc, row);
        }

        /// <summary>[K2] Như trên, cho vụ vừa sinh trong cùng lần lưu (chưa có trong sổ đã đọc).</summary>
        public static string? CaseNextNc(LedgerRow row, string? detectGroup, string? detectNc, string? origin)
            => NormalNcs(row) is var ncs && ncs.Count > 0 ? CaseNextNc(ncs, detectGroup, detectNc, origin, null, null, row) : null;

        static string? CaseNextNc(List<string> ncs, string? detectGroup, string? detectNc, string? origin, string? cause, string? helpFrom, LedgerRow row)
        {
            // [GN-FORM1] rà độc lập V1: vụ sinh lúc nhóm làm hộ đếm NG khi nhận hàng Gia công giao theo NC từ NC k → làm tiếp NC ngay sau k
            //   (NC đó là máy loại trừ thì làm lại từ NC k — chưa có chỗ đặt hàng "chờ giao làm hộ", không bỏ qua công đoạn)
            if (!string.IsNullOrEmpty(helpFrom))
            {
                var chain = row.G("GC").Steps;
                var i = chain.FindIndex(x => string.Equals(x.Code, helpFrom, StringComparison.OrdinalIgnoreCase));
                var next = i >= 0 && i + 1 < chain.Count ? chain[i + 1] : null;
                if (next != null && !next.IsExcluded && !HandoverLedgerService.IsVao(next.Code)) return next.Code;
                if (HandoverLedgerService.IsVao(helpFrom)) return null;
                return ncs.FirstOrDefault(c => string.Equals(c, helpFrom, StringComparison.OrdinalIgnoreCase));
            }
            switch (detectGroup)
            {
                case "GC":
                    if (string.IsNullOrWhiteSpace(detectNc))
                    {
                        if (origin == NgOrigin.BaoLoi) return ncs.Last();   // báo lỗi từ "đã làm xong NC cuối" → làm lại NC cuối (d)
                        // [GN-FORM1] rà độc lập V2: NG lúc nhận / nhận vào ô Hàng lỗi hàng từ nhóm SAU Gia công (đã xong NC cuối) → NC cuối;
                        //   từ Kho / phôi / không rõ → như hàng mới
                        return cause is "HTSP" or "KCS" or "PKG" ? ncs.Last() : null;
                    }
                    if (HandoverLedgerService.IsVao(detectNc)) return null;
                    var m = MainNc(row, detectNc.Trim());
                    var hit = ncs.FirstOrDefault(c => string.Equals(c, m, StringComparison.OrdinalIgnoreCase));
                    return hit;   // NC không còn trong quy trình / máy loại trừ → như hàng mới
                case "HTSP":
                case "KCS":
                case "PKG":
                    return ncs.Last();   // (d) vụ phát hiện sau Gia công, gửi về Gia công sửa → làm lại NC cuối (chờ khách chốt)
                case "KHO":
                    // [GN-FORM1] rà độc lập vòng 2 V-a: Kho phát hiện lỗi ở hàng do Gia công / nhóm sau giao về (đã xong NC cuối) → NC cuối;
                    //   phôi / khách / không rõ → như hàng mới. Trước: luôn như hàng mới → sửa đạt chọn NC cuối bị chặn oan.
                    return cause is "GC" or "HTSP" or "KCS" or "PKG" ? ncs.Last() : null;
                default:
                    return null;         // Kho / phôi / không rõ → như hàng mới
            }
        }

        /// <summary>[K2] Các lựa chọn NC làm tiếp: mặc định + các NC đứng trước (làm lại, bắt lý do). Không có NC sau.</summary>
        /// <param name="free">[GN-FORM1] vòng 3 C1b: hàng lỗi cũ chưa có mã vụ (không có mốc) — thêm các NC sau, cũng bắt lý do.</param>
        public static List<NcOpt> NcOptions(LedgerRow row, string? def, bool free = false)
        {
            var ncs = NormalNcs(row);
            var list = new List<NcOpt>();
            if (ncs.Count == 0) return list;
            var di = NcIdx(row, ncs, def);
            var vao = HasVao(row);
            string NewLabel() => "Như hàng mới (" + (vao ? HandoverLedgerService.VaoLabel : Nc(ncs[0])) + ")";
            // mặc định trước
            list.Add(new NcOpt(def, (def == null ? NewLabel() : Nc(def) + (def == ncs.Last() ? " (NC cuối)" : "")) + " — mặc định", true, false));
            for (int i = Math.Min(di - 1, ncs.Count - 1); i >= 0; i--)
                list.Add(new NcOpt(ncs[i], "Làm lại từ " + Nc(ncs[i]) + " (ghi lý do)", false, true));
            if (def != null && vao && di >= 0)   // không có @VAO: "như hàng mới" = NC đầu (đã có trong danh sách)
                list.Add(new NcOpt(null, NewLabel() + " — làm lại từ đầu (ghi lý do)", false, true));
            if (free)
                for (int i = Math.Max(di + 1, 0); i < ncs.Count; i++)
                    if (ncs[i] != def)
                        list.Add(new NcOpt(ncs[i], "Làm tiếp ở " + Nc(ncs[i]) + " (hàng lỗi cũ chưa có mã vụ — ghi lý do)", false, true));
            return list;
        }

        /// <summary>
        /// [K2] Máy chủ kiểm NC làm tiếp: chỉ NC mặc định của vụ hoặc NC đứng trước; đứng trước thì lý do ≥ 3 ký tự.
        /// Trả (lỗi, chữ ghi kèm vào Ghi chú của Kết quả sửa).
        /// </summary>
        public static (string? Error, string? NoteAdd) CheckNextNc(LedgerRow row, string? def, string? chosen, string? reason, bool free = false)
        {
            var ncs = NormalNcs(row);
            if (ncs.Count == 0) return (null, null);
            var di = NcIdx(row, ncs, def);
            var ci = NcIdx(row, ncs, chosen);
            if (ci == int.MinValue) return ($"NC {chosen} không phải NC làm tiếp của PO này. Chọn lại NC làm tiếp.", null);
            string Name(string? c) => c == null ? "như hàng mới" : Nc(c);
            if (ci > di && free)
            {   // [GN-FORM1] vòng 3 C1b: hàng lỗi cũ chưa có mã vụ — không có mốc để chặn, cho chọn NC sau kèm lý do (ghi vào Ghi chú)
                var rf = (reason ?? "").Trim();
                if (rf.Length < NcReasonMin)
                    return ($"Hàng lỗi cũ chưa có mã vụ làm tiếp ở {Name(chosen)} (mặc định {Name(def)}): ghi lý do ít nhất {NcReasonMin} ký tự (đã qua NC nào).", null);
                return (null, $"Hàng lỗi cũ chưa có mã vụ: làm tiếp ở {Name(chosen)} thay vì {Name(def)}: {rf}");
            }
            if (ci > di)
                return ($"Không cho làm tiếp ở {Name(chosen)}: hàng sửa đạt của {NgCaseNo.Term} này làm tiếp ở {Name(def)} — chỉ được chọn NC đó hoặc NC đứng trước (làm lại), không bỏ qua NC.", null);
            if (ci == di) return (null, null);
            var r = (reason ?? "").Trim();
            if (r.Length < NcReasonMin)
                return ($"Làm lại từ {Name(chosen)} (NC đứng trước {Name(def)}): ghi lý do ít nhất {NcReasonMin} ký tự.", null);
            return (null, $"Làm lại từ {Name(chosen)} thay vì {Name(def)}: {r}");
        }

        // ================================================================
        // Vụ lỗi trên form: chữ + nơi gửi nhờ sửa
        // ================================================================

        /// <summary>"phát hiện ở Gia công · đã làm xong NC1 · chưa làm NC2 · do Kho".</summary>
        public static string CaseOriginText(LedgerRow row, LedgerCase cs)
        {
            if (cs.Id == 0) return "dữ liệu trước khi có mã vụ";
            var s = "phát hiện ở " + (cs.DetectGroup == null ? "?" : HandoverLedgerService.GroupLabel(cs.DetectGroup));
            if (cs.DetectGroup == "GC")
            {
                if (!string.IsNullOrEmpty(cs.DetectNc)) s += " · " + LowerFirst(GcStageText(GcChain(row), MainNc(row, cs.DetectNc)));
                else if (cs.Origin == NgOrigin.BaoLoi) s += " · " + LowerFirst(GcDoneText(GcChain(row)).Replace(" · chưa giao", ""));
            }
            else if (!string.IsNullOrEmpty(cs.DetectNc)) s += " " + cs.DetectNc;
            if (!string.IsNullOrEmpty(cs.Cause) && cs.Cause != cs.DetectGroup) s += " · do " + HandoverNgSource.CauseLabel(cs.Cause);
            return s;
        }

        static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

        /// <summary>Nhóm đã gửi hàng của vụ vào ô Hàng sửa của <paramref name="group"/> (lần nhận gần nhất). null = không rõ.</summary>
        public static string? CaseSender(LedgerCase cs, string group)
        {
            var hop = cs.Hops.LastOrDefault(h => h.Kind == "NHAN" && h.Group == group && h.Cell == HandoverCell.Sua && !string.IsNullOrEmpty(h.Other));
            if (hop != null && HandoverLedgerService.GroupCodes.Contains(hop.Other!)) return hop.Other;
            return cs.DetectGroup != null && cs.DetectGroup != group && HandoverLedgerService.GroupCodes.Contains(cs.DetectGroup) ? cs.DetectGroup : null;
        }

        /// <summary>Nơi gây lỗi của vụ là 1 nhóm khác nhóm đang giữ ((b): "Trả nơi gây lỗi" chỉ khi khác nhóm mình).</summary>
        public static string? CaseCauseGroup(LedgerCase cs, string group)
            => !string.IsNullOrEmpty(cs.Cause) && cs.Cause != group && HandoverLedgerService.GroupCodes.Contains(cs.Cause) ? cs.Cause : null;

        public static string NextGroup(string from) => from switch { "KHO" => "GC", "GC" => "HTSP", "HTSP" => "KCS", "KCS" => "PKG", "PKG" => "KHO", _ => "" };
        public static string PrevGroup(string from) => from switch { "GC" => "KHO", "HTSP" => "GC", "KCS" => "HTSP", "PKG" => "KCS", _ => "" };
        /// <summary>[LAMHO-L2-KCS 2026-11-07] Nhóm trước theo PO: HTSP chỉ có mã Taro làm hộ (hàng Gia công giao thẳng KCS, HTSP chỉ nhận giao theo NC)
        ///   thì nhóm trước của KCS là Gia công — nơi gây lỗi mặc định khi KCS tích "Lỗi công đoạn trước" (trước: HTSP → vụ ghi HTSP gây, hàng lỗi trả về
        ///   HTSP ô Hàng lỗi nếu người dùng không đổi). Nguồn: CLAUDE (rà độc lập Gói 6 L2, sổ dòng LAMHO-L2-KCS); Hùng 07/11 22:38 giao Gói 8.</summary>
        public static string PrevGroup(string from, LedgerRow? row)
            => from == "KCS" && row != null && HtspNoOwnWork(row) ? "GC" : PrevGroup(from);   // [GOI8-THAP-E] HtspOnlyLamHo → HtspNoOwnWork (+ PO không có công đoạn HTSP)
        /// <summary>[GOI8-THAP-E 2026-11-08] HTSP không có công đoạn của mình trong PO: không có mã chính nào ngoài mã chuẩn bị / mã Taro làm hộ — gồm PO không có
        ///   snapshot HTSP (Part không khai Taro / Bavia / Rửa) và PO HTSP chỉ làm hộ (LAMHO-L2-KCS). Trước: PO không có HTSP thì nhóm trước của KCS vẫn là HTSP
        ///   (vụ "Lỗi công đoạn trước" ở KCS mặc định ghi HTSP gây, hàng lỗi mặc định trả HTSP) và form giao Hàng mới từ Gia công mặc định HTSP.
        ///   Chỉ đổi giá trị MẶC ĐỊNH trên form (người dùng vẫn đổi được); chặn ở máy chủ giữ nguyên. Nguồn: CLAUDE (rà độc lập Gói 8, sổ GOI8-THAP (e));
        ///   Hùng 08/11 11:24 OK Gói 10.</summary>
        ///   [GOI8-THAP-E rà độc lập Gói 10 T1] PO cũ chưa có snapshot HTSP mà HTSP VẪN nhận làm hàng thật (Nhận sản xuất > 0, giao Hàng mới tự do GN-ROUTE-FREE)
        ///   thì KHÔNG coi là "không có công đoạn" (trước bản sửa: KCS lỗi công đoạn trước bỏ trống nơi gây → vụ ghi Gia công dù hàng đi HTSP → KCS).
        ///   Nguồn: CLAUDE (rà độc lập Gói 10, ca R1 → TaroScen T23c).
        public static bool HtspNoOwnWork(LedgerRow row)
            => HandoverLedgerService.HtspOnlyLamHo(row)
            || (!row.G("HTSP").Steps.Any(s => !s.IsPrep) && row.G("HTSP").NhanSanXuat <= 0);
        /// <summary>[GOI8-THAP-E 2026-11-08] Nhóm kế tiếp theo PO: Gia công → KCS khi HTSP không có công đoạn của mình (mặc định nhóm nhận Hàng mới trên form).</summary>
        public static string NextGroup(string from, LedgerRow? row)
            => from == "GC" && row != null && HtspNoOwnWork(row) && row.G("HTSP").ChoNhan <= 0 ? "KCS" : NextGroup(from);
        //   [GOI8-THAP-E rà độc lập Gói 10 vòng 2] HTSP đang có phiếu chờ nhận (PO chưa snapshot HTSP, Gia công vừa giao HTSP) → phiếu sau vẫn mặc định HTSP.
        //   Nguồn: CLAUDE (rà độc lập Gói 10 vòng 2, ca RV2-B → TaroScen T23d).
    }
}
