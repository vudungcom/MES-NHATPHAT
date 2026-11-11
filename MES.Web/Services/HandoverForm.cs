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
        /// <summary>[GC-LOI-TRUOC-NCDAU 2026-11-10] Dòng này được tích "Lỗi công đoạn trước" (Gia công, Workflow ON: chỉ hàng chưa gia công — xem GcPrevStageOk).</summary>
        public bool    PrevOk { get; set; } = true;
        /// <summary>Đã làm xong NC cuối của Gia công (sửa đạt: làm lại NC cuối — câu chờ khách).</summary>
        public bool    IsLast { get; set; }
        /// <summary>Ô đang giữ theo vụ: LOI | SUA | XL ([SUA-XL-GIU] hàng đã sửa chờ trả nơi phát hiện).</summary>
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
        public const string TonXlText   = "Hàng đã sửa — chờ trả nơi phát hiện lỗi";          // [SUA-XL-GIU]
        public const string TonXlLoiText = "Hàng đã sửa bị hỏng lại (đang giữ chờ trả)";       // [XL-TRA-LAI]

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
                if (G.XlGiu != 0)   // [SUA-XL-GIU] hàng đã sửa (KCS xác nhận) giữ vụ → giao tiếp về nơi phát hiện, vào ô Hàng lỗi đã xử lý
                    list.Add(new FormPos { Id = "TONXL", Label = TonXlText, Sub = "không ghi WTS số lượng — giao về nơi phát hiện (hoặc nhóm cần làm lại công đoạn trước đó)",
                                           Have = Pos0(G.XlGiu), HeldCell = HandoverCell.Xl });
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
                    var unw = !row.ApplyWorkflow || GcPrevStageOk(row, s.Code);   // [GC-LOI-TRUOC-NCDAU] Workflow ON: chỉ hàng chưa gia công
                    list.Add(new FormPos { Id = "NC:" + s.Code, Label = GcStageText(chain, s.Code), Have = Pos0(cap),
                                           Fresh = true, NgSource = HandoverNgSource.Chua, NgNc = s.Code, Unworked = unw, PrevOk = unw });
                }
                list.Add(new FormPos { Id = "DA", Label = GcDoneText(chain), Have = Pos0(G.ConGiao), Fresh = true, NgSource = HandoverNgSource.Da, IsLast = true,
                                       PrevOk = !row.ApplyWorkflow });   // [GC-LOI-TRUOC-NCDAU] đã làm xong NC cuối: lỗi là của Gia công
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
            if (G.XlGiu != 0)   // [XL-TRA-LAI] hàng đã sửa bị hỏng lại khi làm lại công đoạn: trả lại vào ô Hàng lỗi (cùng vụ) để sửa lại
                list.Add(new FormPos { Id = "TONXL", Label = TonXlLoiText, Sub = "trả lại vào ô Hàng lỗi của nhóm sửa (cùng vụ)", Have = Pos0(G.XlGiu), HeldCell = HandoverCell.Xl });
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

        // ================================================================
        // [GC-LOI-TRUOC-NCDAU 2026-11-10] "Lỗi công đoạn trước" ở Gia công chỉ cho hàng CHƯA gia công.
        //   Nguồn: KHÁCH 10/11 (vòng 1 hỏi tiếp, câu 5a: "hàng lỗi trả về bộ phận trước chỉ phát sinh ở NC đầu / chưa thực hiện cv … hàng đã GC
        //   mà phát sinh lỗi thì là lỗi do BP tự làm ra → cần chọn NC phát sinh lỗi"); HÙNG 10/11 14:02 "khách chốt rồi … làm đi".
        //   Dòng được tích = chờ NC thường đầu tiên của chuỗi (hoặc dòng "Nhận vào" @VAO khi NC đầu là máy loại trừ) và chờ NC ngay SAU máy loại trừ
        //   (hàng làm hộ vừa trả về, Gia công chưa động vào — cùng nguyên tắc "chưa thực hiện cv"). Các NC khác / "đã làm xong NC cuối":
        //   lỗi là của Gia công (đầu ra, ghi NG ở NC phát sinh). Chỉ Workflow ON (OFF họ tự kiểm soát — N7). Không đổi phép tính số.
        // ================================================================
        public const string GcPrevMsg = "Gia công: \"Lỗi công đoạn trước\" chỉ dùng cho hàng chưa gia công (chờ NC đầu, hoặc hàng làm hộ trả về chưa làm NC đó). "
                                      + "Hàng đã qua NC mà lỗi là lỗi của Gia công — bỏ tích, báo lỗi ở NC phát sinh.";

        /// <summary>[GC-LOI-TRUOC-NCDAU] Chờ NC <paramref name="nc"/> của Gia công là hàng chưa gia công: NC (không máy loại trừ) đầu tiên của chuỗi
        ///   (gồm @VAO) hoặc NC đứng ngay sau 1 NC máy loại trừ. nc trống / không có trong chuỗi → false.</summary>
        public static bool GcPrevStageOk(LedgerRow row, string? nc)
        {
            if (string.IsNullOrWhiteSpace(nc)) return false;
            var steps = row.G("GC").Steps;
            var i = steps.FindIndex(s => string.Equals(s.Code, nc.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0 || steps[i].IsExcluded) return false;
            var firstNormal = steps.FindIndex(s => !s.IsExcluded);
            return i == firstNormal || (i > 0 && steps[i - 1].IsExcluded);
        }

        // ================================================================
        // [GN-NGUOC-CHAN 2026-11-10] Không giao ngược Hàng mới về bộ phận trước — giao ngược chỉ cho hàng lỗi.
        //   Nguồn: KHÁCH 10/11 (vòng 1 hỏi lại, câu 5: "bộ phận sau giao ngược bộ phận trước thì chỉ có hàng lỗi mới trả ngược lại, chứ hàng mới
        //   ko trả lại"); HÙNG 10/11 14:02. Thứ tự Kho → Gia công → HTSP → KCS → Đóng gói; Đóng gói → Kho (thành phẩm) không phải ngược.
        //   Chỉ Workflow ON (N7). Trước: [GN-ROUTE-FREE] cho giao Hàng mới tới nhóm bất kỳ. Không đổi phép tính số.
        // ================================================================
        public static bool IsBackward(string from, string to)
        {
            if (from == "PKG" && to == "KHO") return false;
            var o = HandoverLedgerService.GroupCodes;
            int a = Array.IndexOf(o, from), b = Array.IndexOf(o, to);
            return a >= 0 && b >= 0 && b < a;
        }

        public static string BackwardMsg(string from, string to)
            => $"Không giao ngược Hàng mới: {HandoverLedgerService.GroupLabel(to)} đứng trước {HandoverLedgerService.GroupLabel(from)} — giao ngược chỉ dùng cho hàng lỗi "
             + "(Lỗi công đoạn trước / gửi nhờ sửa / trả nơi gây lỗi).";

        // ================================================================
        // [KHO-NC-DAU 2026-11-10] Phôi Kho giao Gia công luôn vào NC đầu theo Part Master (không chọn NC khác).
        //   Nguồn: KHÁCH 10/11 (vòng 1 câu 5 chọn A + hỏi lại câu 5 "Kho giao luôn là NC đầu"); HÙNG 10/11 14:02.
        //   NC đầu = NC thường đầu tiên; NC đầu là máy loại trừ (có dòng @VAO) → không chọn NC (hàng tự nằm ở "Nhận vào").
        //   Áp cho hàng "như hàng mới" (phôi, vụ của phôi / Kho / không rõ — CaseNextNc = null); hàng của vụ đã qua Gia công (CaseNextNc có NC)
        //   giữ luật NC làm tiếp K2. Chỉ Workflow ON (N7). Không đổi phép tính số.
        // ================================================================
        public static bool KhoNcOk(LedgerRow row, string? toNc)
        {
            if (string.IsNullOrWhiteSpace(toNc)) return true;
            if (HasVao(row)) return false;
            var ncs = NormalNcs(row);
            return ncs.Count > 0 && string.Equals(ncs[0], toNc.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string KhoNcMsg(LedgerRow row)
            => "Kho giao phôi cho Gia công luôn vào NC đầu theo Part Master"
             + (HasVao(row) ? $" (NC đầu là máy loại trừ: hàng vào \"{HandoverLedgerService.VaoLabel}\")" : NormalNcs(row) is { Count: > 0 } n ? $" ({Nc(n[0])})" : "")
             + " — không chọn NC khác.";

        // ================================================================
        // [GC-HTSP-NC-HIEN 2026-11-10] Gia công xong (NC thường cuối) mà sau đó còn NC máy loại trừ (Taro làm ở HTSP như việc thường — câu 4):
        //   phiếu Gia công → HTSP hiện "để làm NC5 (Taro)". Chỉ hiển thị, không lưu, không đổi phép tính số.
        //   Nguồn: KHÁCH 10/11 (vòng 1 hỏi tiếp, câu 5b chọn B); HÙNG 10/11 14:02.
        // ================================================================
        public static string? TailExclText(LedgerRow row)
        {
            var steps = row.G("GC").Steps;
            var lastNormal = steps.FindLastIndex(s => !s.IsExcluded && !HandoverLedgerService.IsVao(s.Code));
            if (lastNormal < 0) return null;
            var tail = steps.Skip(lastNormal + 1).Where(s => s.IsExcluded).Select(s => Nc(s.Code)).ToList();
            if (tail.Count == 0) return null;
            var taro = row.G("HTSP").Steps.Any(s => s.Pg == "TARO" && !s.IsPrep);   // rà độc lập Gói 13 Thấp 4: PO cũ không có dòng Taro → không ghi "(Taro)"
            return $"để làm {string.Join(", ", tail)}" + (taro ? " (Taro)" : "");
        }

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

        // ================================================================
        // [SUA-XL-GIU 2026-11-10] ĐÍCH HÀNG SỬA ĐẠT (Kết quả sửa) — khách: sửa xong không quay lại NC, nhóm đã làm xong chiếc thì nhận là đã làm.
        //   Nguồn: KHÁCH 10/11 vòng 1 (docx câu 2, hỏi lại 2.2 A, hỏi tiếp 2.1 A "mọi trường hợp sửa đều không quay lại NC"); HÙNG 10/11 19:22.
        //   Trả: RepXong (nhóm sửa đã làm xong chiếc trước lúc phát hiện) · RepOk (Gia công làm hỏng ở NC k — [SUA-OK-NC]) · NC (Gia công: chờ NC đó, như K2)
        //   · null (như hàng mới / nhóm khác: như cũ). Vụ làm hộ / hàng cũ chưa mã vụ / phát hiện ở Kho (N9 chưa chốt): như cũ.
        // ================================================================
        public static string? RepairTarget(LedgerRow row, long caseId, string group)
        {
            if (caseId <= 0 || !row.Cases.TryGetValue(caseId, out var cs) || cs.IsLamHo || !string.IsNullOrEmpty(cs.HelpFromNc))
                return group == "GC" ? CaseNextNc(row, caseId, group) : null;
            return RepairTarget(row, group, cs.DetectGroup, cs.DetectNc, cs.Origin, cs.Cause, cs.FromDa)
                   ?? (group == "GC" ? CaseNextNc(row, caseId, group) : null);
        }

        /// <summary>[SUA-XL-GIU] Như trên cho vụ vừa sinh trong cùng lần lưu (báo lỗi trên form): null = theo luật NC cũ (CaseNextNc).</summary>
        public static string? RepairTarget(LedgerRow row, string group, string? detectGroup, string? detectNc, string? origin, string? cause, bool fromDa)
        {
            int x = HandoverLedgerService.ProdIdx(group), d = HandoverLedgerService.ProdIdx(detectGroup);
            if (x < 0 || d < 0) return null;   // Kho sửa / phát hiện ở Kho: như cũ
            if (group == "GC")
            {
                if (NormalNcs(row).Count == 0) return null;
                if (d > 1 || (d == 1 && string.IsNullOrWhiteSpace(detectNc) && (origin == NgOrigin.BaoLoi || cause is "HTSP" or "KCS" or "PKG")))
                    return HandoverLedgerService.RepXong;   // chiếc đã xong Gia công (phát hiện sau Gia công / ở "đã làm xong NC cuối")
                if (d == 1 && origin == NgOrigin.Wts && !string.IsNullOrWhiteSpace(detectNc)
                    && NormalNcs(row).Any(c => string.Equals(c, MainNc(row, detectNc.Trim()), StringComparison.OrdinalIgnoreCase)))
                    return HandoverLedgerService.RepOk;     // [SUA-OK-NC] làm hỏng ở NC k: sửa đạt = xong NC k
                return null;
            }
            return x < d || (x == d && fromDa) ? HandoverLedgerService.RepXong : null;
        }

        /// <summary>[SUA-XL-GIU] Chữ cho người dùng: hàng sửa đạt (KCS xác nhận) đi đâu.</summary>
        public static string RepairTargetText(LedgerRow row, string group, string? target, string? detectGroup)
        {
            if (target == HandoverLedgerService.RepOk) return "Sửa đạt (KCS xác nhận) tính là xong NC của chiếc lỗi — không ghi WTS lại, giờ sửa ghi \"Việc không làm ra SP\".";
            if (target != HandoverLedgerService.RepXong) return "";
            if (detectGroup == group)
                return $"Sửa đạt (KCS xác nhận) tính là {HandoverLedgerService.GroupLabel(group)} đã làm xong — không làm lại công đoạn, không ghi WTS số lượng.";
            return $"Sửa đạt (KCS xác nhận) vào Tồn hàng lỗi đã xử lý của {HandoverLedgerService.GroupLabel(group)} — không làm lại công đoạn; sau đó giao về nơi phát hiện ({HandoverLedgerService.GroupLabel(detectGroup)}).";
        }

        // ================================================================
        // [XL-VUOT-CHAN 2026-11-10] Hàng đã sửa (ô Hàng lỗi đã xử lý) chỉ giao xuôi về nơi phát hiện: nhóm nhận đứng sau nhóm giữ và không sau nơi phát hiện.
        //   Nguồn: KHÁCH 10/11 (docx câu 2 "sửa xong trả bộ phận báo lỗi"; hỏi tiếp 2.1 A "Gia công chọn giao sang HTSP hoặc giao thẳng KCS"); HÙNG 10/11 19:22.
        //   Chỉ Workflow ON (OFF: sổ tính như hàng đã xử lý thường — vào Chưa làm bên nhận). Không đổi phép tính số.
        // ================================================================
        public static bool XlToOk(string from, string to, string? detectGroup, LedgerRow? row = null)
        {
            int f = HandoverLedgerService.ProdIdx(from), t = HandoverLedgerService.ProdIdx(to), d = HandoverLedgerService.ProdIdx(detectGroup);
            return f > 0 && t > f && d > 0 && t <= d && !XlHtspLamHo(to, row);
        }

        /// <summary>[XL-TRA-LAI] Nhóm đã sửa chiếc của vụ (chặng Kết quả sửa gần nhất; không có thì nơi gây lỗi) — mặc định nơi trả lại hàng đã sửa bị hỏng lại.</summary>
        public static string? XlRepairGroup(LedgerCase c)
            => c.Hops.LastOrDefault(h => h.Kind == "SUA")?.Group is string g && g != "" ? g : (c.Cause is string cg && HandoverLedgerService.GroupCodes.Contains(cg) ? cg : null);

        /// <summary>[XL-TRA-NHOMSUA 2026-11-10] Workflow ON: nhóm nhận được khi <paramref name="from"/> trả lại hàng đã sửa bị hỏng lại = các nhóm đã sửa ĐẠT
        ///   chiếc của vụ (KCS xác nhận), trừ chính nhóm giữ. Rỗng (vd nhóm giữ là nhóm duy nhất đã sửa) = không giới hạn (như trước — sổ không theo từng chiếc).
        ///   Rà độc lập Gói 15 vòng 1 Thấp 1 (trả sang nhóm không sửa) + vòng 2 Trung 1 (lấy 1 nhóm sửa cuối của vụ → kẹt / chặn oan khi vụ có 2 nhóm sửa
        ///   hoặc nhóm giữ tự sửa). Nguồn: KHÁCH 10/11 2.1 ("hỏng thì trả lại Gia công"); CLAUDE (rà độc lập 10/11).</summary>
        ///   Rà vòng 3 (Trung 1): chỉ nhóm sửa có hàng vào giữ ô XL (đích GIU — SuaDatBy) và đứng TRƯỚC nhóm giữ trong chuỗi (hàng đã sửa chỉ đi xuôi),
        ///   nên Kho / nơi phát hiện tự sửa / nhóm sau nhóm giữ không lọt vào; nhóm giữ tự sửa → rỗng → không giới hạn.
        public static List<string> XlTraLaiGroups(LedgerCase c, string from)
            => HandoverLedgerService.GroupCodes.Where(g => g != from && c.SuaDatBy.Contains(g)
                   && HandoverLedgerService.ProdIdx(g) > 0 && HandoverLedgerService.ProdIdx(g) < HandoverLedgerService.ProdIdx(from)).ToList();

        /// <summary>[XL-HTSP-LAMHO 2026-11-10] Rà độc lập Gói 15 (Thấp 3): PO mà HTSP chỉ làm Taro làm hộ — HTSP không nhận được ô Hàng lỗi đã xử lý
        ///   (HandoverService.ReceiveAsync chặn) nên không cho chọn HTSP làm nhóm giữa (trước: lập phiếu được rồi kẹt chờ nhận). Nguồn: CLAUDE (rà độc lập 10/11).</summary>
        public static bool XlHtspLamHo(string to, LedgerRow? row) => row != null && to == "HTSP" && HandoverLedgerService.HtspOnlyLamHo(row);

        public static string XlToMsg(string? detectGroup, string? to = null, LedgerRow? row = null)
            => to != null && XlHtspLamHo(to, row) ? "PO này HTSP chỉ làm Taro làm hộ (không có công đoạn HTSP của mình) — hàng đã sửa giao thẳng nơi phát hiện, không qua HTSP."
             : $"Hàng đã sửa chỉ giao xuôi về nơi phát hiện lỗi ({HandoverLedgerService.GroupLabel(detectGroup)}) hoặc nhóm đứng giữa cần làm lại công đoạn — không giao ngược, không vượt qua nơi phát hiện. "
             + "Hàng bị hỏng lại thì trả lại vào ô Hàng lỗi (Hàng lỗi → hàng đã sửa bị hỏng lại).";

        /// <summary>[K2] Các lựa chọn NC làm tiếp: mặc định + các NC đứng trước (làm lại, bắt lý do). Không có NC sau.</summary>
        /// <param name="free">[GN-FORM1] vòng 3 C1b: hàng lỗi cũ chưa có mã vụ (không có mốc) — thêm các NC sau, cũng bắt lý do.</param>
        /// <param name="allowEarlier">[SUA-KHONG-LUI 2026-11-10] false = không cho "Làm lại từ NC trước" (Workflow ON — khách 2.2 A).</param>
        public static List<NcOpt> NcOptions(LedgerRow row, string? def, bool free = false, bool allowEarlier = true)
        {
            var ncs = NormalNcs(row);
            var list = new List<NcOpt>();
            if (ncs.Count == 0) return list;
            var di = NcIdx(row, ncs, def);
            var vao = HasVao(row);
            string NewLabel() => "Như hàng mới (" + (vao ? HandoverLedgerService.VaoLabel : Nc(ncs[0])) + ")";
            // mặc định trước
            list.Add(new NcOpt(def, (def == null ? NewLabel() : Nc(def) + (def == ncs.Last() ? " (NC cuối)" : "")) + " — mặc định", true, false));
            if (allowEarlier || free)   // [SUA-KHONG-LUI] Workflow ON: không làm lại NC trước (khách 2.2 A); hàng cũ chưa mã vụ (không mốc) giữ như cũ
            {
                for (int i = Math.Min(di - 1, ncs.Count - 1); i >= 0; i--)
                    list.Add(new NcOpt(ncs[i], "Làm lại từ " + Nc(ncs[i]) + " (ghi lý do)", false, true));
                if (def != null && vao && di >= 0)   // không có @VAO: "như hàng mới" = NC đầu (đã có trong danh sách)
                    list.Add(new NcOpt(null, NewLabel() + " — làm lại từ đầu (ghi lý do)", false, true));
            }
            if (free)
                for (int i = Math.Max(di + 1, 0); i < ncs.Count; i++)
                    if (ncs[i] != def)
                        list.Add(new NcOpt(ncs[i], "Làm tiếp ở " + Nc(ncs[i]) + " (hàng lỗi cũ chưa có mã vụ — ghi lý do)", false, true));
            return list;
        }

        // [SUA-OFF-LUI 2026-11-10] Rà độc lập Gói 15 (Trung T2): Workflow OFF mà đích hàng sửa đạt là dấu (@XONG / @OK) thì form chỉ hiện chữ,
        //   không còn chọn "làm lại NC" — trái luật OFF "như cũ" (máy chủ OFF vẫn nhận NC + lý do). Nay OFF: mặc định = dấu (không làm lại NC,
        //   khách chốt), kèm các lựa chọn cũ: NC mặc định cũ của vụ (CaseNextNc) + làm lại NC trước (ghi lý do). ON: không đổi (không có ô chọn).
        //   Nguồn: KHÁCH 10/11 2.1 A / 2.2 A; HÙNG 09/11 08:21 ("off họ phải tự kiểm soát"); CLAUDE (rà độc lập 10/11).
        /// <summary>[SUA-OFF-LUI] Lựa chọn NC khi đích là dấu và Workflow OFF: đầu = dấu (khoá ""), sau = NC cũ của vụ + NC trước (ghi lý do).</summary>
        public static List<NcOpt> OffMarkOptions(LedgerRow row, string? oldDef)
        {
            var list = new List<NcOpt> { new NcOpt(null, "Không làm lại NC (mặc định — khách chốt)", true, false) };
            foreach (var o in NcOptions(row, oldDef, free: false, allowEarlier: true))
                list.Add(o.Default ? new NcOpt(o.Code, "Làm lại " + o.Label.Replace(" — mặc định", "") + " (như trước, Workflow OFF)", false, false) : o);
            return list;
        }

        /// <summary>
        /// [K2] Máy chủ kiểm NC làm tiếp: chỉ NC mặc định của vụ hoặc NC đứng trước; đứng trước thì lý do ≥ 3 ký tự.
        /// Trả (lỗi, chữ ghi kèm vào Ghi chú của Kết quả sửa).
        /// </summary>
        public static (string? Error, string? NoteAdd) CheckNextNc(LedgerRow row, string? def, string? chosen, string? reason, bool free = false, bool allowEarlier = true)
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
            if (!allowEarlier && !free)   // [SUA-KHONG-LUI 2026-11-10] Nguồn: KHÁCH 10/11 hỏi lại 2.2 A ("mọi trường hợp sửa đều không quay lại NC"); HÙNG 10/11 19:22. Chỉ Workflow ON.
                return (SuaKhongLuiMsg(Name(def)), null);
            var r = (reason ?? "").Trim();
            if (r.Length < NcReasonMin)
                return ($"Làm lại từ {Name(chosen)} (NC đứng trước {Name(def)}): ghi lý do ít nhất {NcReasonMin} ký tự.", null);
            return (null, $"Làm lại từ {Name(chosen)} thay vì {Name(def)}: {r}");
        }

        /// <summary>[SUA-KHONG-LUI] Lời chặn chọn "Làm lại từ NC trước" (Workflow ON).</summary>
        public static string SuaKhongLuiMsg(string def)
            => $"Không làm lại NC trước: hàng sửa đạt đi tiếp ở {def} (khách chốt: sửa xong không quay lại NC — giờ chạy lại ghi \"Việc không làm ra SP\").";

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
