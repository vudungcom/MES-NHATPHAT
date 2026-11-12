using Microsoft.EntityFrameworkCore;
using MES.Web.Data;
using MES.Web.Data.Entities;

namespace MES.Web.Services
{
    // =====================================================================
    // [GN-LEDGER 2026-10-22] SỔ GIAO NHẬN — HÀM TÍNH CHUNG DUY NHẤT
    // ---------------------------------------------------------------------
    // Mọi số trên trang Giao nhận (và trần khi lập phiếu giao) đều tính ở đây,
    // từ chứng từ gốc: phôi Kho (KhoVatLieus), phiếu giao (HandoverTransactions),
    // lần nhận (HandoverReceives), WTS (WtsProductionLogs), snapshot NC.
    // Không màn hình nào tự cộng trừ riêng. Mỗi số đi kèm diễn giải công thức
    // + danh sách chứng từ tạo ra nó (nút "Hiện công thức" trên trang).
    //
    // Bố cục theo Excel xưởng gửi (TH1–TH3):
    //   Nhận: Hàng Mới · Hàng lỗi · Hàng Sửa · Hàng lỗi đã xử lý · Làm hộ
    //   Giao: Hàng Mới · Hàng lỗi · Hàng Sửa · Hàng lỗi đã xử lý · Làm hộ
    //   Chưa làm · Đã làm · NG làm hỏng · Tồn
    // Luật: Sổ luật Giao nhận MES — L1..L6, N1..N10, K1..K8.
    // =====================================================================

    /// <summary>[GN-LEDGER] Loại phiếu giao (cột HandoverTransactions.Purpose).</summary>
    public static class HandoverPurpose
    {
        /// <summary>Hàng mới — giao xuôi hàng OK / phôi (Giao Hàng Mới → Nhận Hàng Mới).</summary>
        public const string New          = "NEW";
        /// <summary>Hàng lỗi — nơi phát hiện trả về nơi gây lỗi, lấy từ hàng chưa làm (Giao Hàng lỗi → Nhận Hàng lỗi).</summary>
        public const string NgReturn     = "NG_RETURN";
        /// <summary>Gửi sửa — nơi gây lỗi chuyển hàng lỗi đang giữ cho nhóm sửa (Giao Hàng lỗi → Nhận Hàng Sửa).</summary>
        public const string RepairSend   = "REPAIR_SEND";
        /// <summary>Trả hàng đã sửa — về nơi phát hiện để làm tiếp (Giao Hàng Sửa → Nhận Hàng lỗi đã xử lý).</summary>
        public const string RepairReturn = "REPAIR_RETURN";
        /// <summary>Làm hộ: gửi đi — GC gửi NC máy loại trừ cho nhóm khác làm hộ (N3).</summary>
        public const string HelpSend     = "HELP_SEND";
        /// <summary>Làm hộ: trả về — nhóm làm hộ trả lại GC, vào NC sau (N3).</summary>
        public const string HelpReturn   = "HELP_RETURN";

        /// <summary>[GN-NG-OUT] Huỷ hàng lỗi đang giữ — ra khỏi PO (L5). Phiếu không có bên nhận, ToGroupCode = "HUY".</summary>
        public const string Scrap        = "SCRAP";
        /// <summary>[GN-NG-OUT] Trả khách hàng lỗi đang giữ (phôi lỗi / hàng lỗi khách nhận lại) — ra khỏi PO (L5, N9). ToGroupCode = "KHACH".</summary>
        public const string CustReturn   = "CUST_RETURN";

        public static readonly string[] All = { New, NgReturn, RepairSend, RepairReturn, HelpSend, HelpReturn, Scrap, CustReturn };

        /// <summary>[GN-NG-OUT] Phiếu đưa hàng RA KHỎI PO (không có nhóm nhận, không cần xác nhận nhận).</summary>
        public static bool IsOut(string? p) => p is Scrap or CustReturn;

        /// <summary>[GN-NG-OUT] Mã "nơi nhận" ghi vào ToGroupCode cho phiếu ra khỏi PO.</summary>
        public static string? OutTarget(string? p) => p switch { Scrap => "HUY", CustReturn => "KHACH", _ => null };

        public static string Label(string? p) => p switch
        {
            New          => "Hàng mới",
            NgReturn     => "Trả hàng lỗi",
            RepairSend   => "Gửi sửa",
            RepairReturn => "Trả hàng đã sửa",
            HelpSend     => "Giao theo NC",
            HelpReturn   => "Trả về Gia công theo NC",
            Scrap        => "Huỷ hàng lỗi",
            CustReturn   => "Trả khách hàng lỗi",
            _            => p ?? ""
        };
    }

    /// <summary>
    /// [GN-NG-AFTER 2026-10-27] Báo hàng lỗi phát hiện SAU khi nhận (chứng từ nội bộ của 1 nhóm, bảng HandoverNgReports).
    /// Lấy từ ô hàng tốt → Tồn hàng lỗi của chính nhóm. Tồn nhóm, Cân PO không đổi.
    /// </summary>
    public static class HandoverNgSource
    {
        public const string Phoi = "PHOI";   // Kho: phôi còn giao
        public const string Chua = "CHUA";   // nhóm SX: Chưa làm (GC: theo NC)
        public const string Da   = "DA";     // nhóm SX: Đã làm chưa giao
        public const string DauVao = "DAU_VAO";   // lỗi có sẵn (nhóm trước / phôi khách)
        public const string DauRa  = "DAU_RA";    // nhóm mình làm hỏng
        public static readonly string[] Causes = { "KHACH", "KHO", "GC", "HTSP", "KCS", "PKG" };

        public static string SourceLabel(string? s, string group) => s switch
        {
            Phoi => "phôi còn", Chua => group == "GC" ? "chưa gia công" : "chưa làm", Da => "đã làm chưa giao", _ => s ?? ""
        };
        public static string OriginLabel(string? o, string? cause) => o switch
        {
            DauVao => "lỗi đầu vào" + (string.IsNullOrEmpty(cause) ? "" : " (do " + CauseLabel(cause) + ")"),
            DauRa  => "lỗi đầu ra (nhóm mình làm hỏng)",
            _ => o ?? ""
        };
        public static string CauseLabel(string? c) => c == "KHACH" ? "phôi khách" : HandoverLedgerService.GroupLabel(c ?? "");
    }

    /// <summary>
    /// [GN-CELL 2026-10-23] Ô trên Excel xưởng (mỗi nhóm: Nhận / Giao × Hàng mới · Hàng lỗi · Hàng sửa · Hàng lỗi đã xử lý).
    /// Form Giao: người giao chọn Ô GIAO (+ tích "Hàng lỗi công đoạn trước"), phần mềm suy loại phiếu và Ô NHẬN của bên nhận.
    /// </summary>
    public static class HandoverCell
    {
        public const string Moi = "MOI";   // Hàng mới
        public const string Loi = "LOI";   // Hàng lỗi
        public const string Sua = "SUA";   // Hàng sửa
        public const string Xl  = "XL";    // Hàng lỗi đã xử lý. [SUA-XL-GIU] từ 10/11 cũng là ô GIAO: hàng đã sửa (KCS xác nhận), giữ vụ, chờ trả nơi phát hiện
        /// <summary>[TARO-LAMHO-NC] Ô giữ của VỤ LỖI (chỉ trong sổ, không ghi DB): hàng làm hộ bị lỗi (WTS Taro làm hộ NG) vẫn nằm trong
        ///   Tồn hàng giao theo NC của nhóm làm hộ tới khi trả Gia công vào ô Hàng lỗi (phiếu Trả về Gia công theo NC, FromCell = LOI, mang vụ).</summary>
        public const string LamHo = "LAMHO";

        public static readonly string[] IssueCells = { Moi, Loi, Sua };
        /// <summary>[SUA-XL-GIU 2026-11-10] Ô giao được của phiếu (IssueCells + ô Hàng lỗi đã xử lý — chỉ phiếu Trả hàng đã sửa, mang vụ).
        ///   Nguồn: KHÁCH 10/11 vòng 1 (hỏi lại 2.2 A, hỏi tiếp 2.1 A); HÙNG 10/11 19:22 OK thiết kế Gói 15.</summary>
        public static readonly string[] IssueCellsXl = { Moi, Loi, Sua, Xl };

        public static string Label(string? c) => c switch
        {
            Moi => "Hàng mới", Loi => "Hàng lỗi", Sua => "Hàng sửa", Xl => "Hàng lỗi đã xử lý", LamHo => "Tồn hàng giao theo NC (làm hộ bị lỗi)", _ => c ?? ""   // [TARO-LAMHO-NC]
        };

        /// <summary>Ô GIAO của bên giao theo loại phiếu (phiếu cũ không có FromCell). Làm hộ / Huỷ / Trả khách: không nằm trong 4 ô Excel → null.</summary>
        public static string? IssueCellOf(string purpose, string? fromCell) => purpose switch
        {
            HandoverPurpose.New          => Moi,
            HandoverPurpose.NgReturn     => Loi,
            HandoverPurpose.RepairSend   => Loi,
            HandoverPurpose.RepairReturn => fromCell == Loi ? Loi : fromCell == Xl ? Xl : Sua,   // TH2: nơi gây lỗi tự sửa xong → giao từ ô Hàng lỗi · [SUA-XL-GIU] hàng đã sửa giữ ô Hàng lỗi đã xử lý
            HandoverPurpose.Scrap        => Loi,
            HandoverPurpose.CustReturn   => Loi,
            HandoverPurpose.HelpReturn   => fromCell == Loi ? Loi : null,   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ (mang vụ) về Gia công ô Hàng lỗi
            // [KHO-TARO-THANG 2026-11-12] Kho giao thẳng Taro (phiếu giao theo NC do Kho lập) lấy từ ô phôi còn = Giao · Hàng mới của Kho.
            //   Chỉ phiếu Kho giao thẳng Taro có FromCell MOI (máy chủ tự ghi); Gia công giao theo NC FromCell NULL → null như cũ. Nguồn: KHÁCH 12/11 (3.3 b = A); HÙNG 12/11 14:00.
            HandoverPurpose.HelpSend     => fromCell == Moi ? Moi : null,
            _ => null
        };

        /// <summary>[TARO-LAMHO-NC] Phiếu Trả về Gia công theo NC lấy từ hàng làm hộ bị lỗi (FromCell = LOI): mang vụ, Gia công nhận vào ô Hàng lỗi.</summary>
        public static bool IsLamHoLoi(string? purpose, string? fromCell) => purpose == HandoverPurpose.HelpReturn && fromCell == Loi;

        /// <summary>Ô NHẬN của bên nhận theo loại phiếu (Excel: B1 Hàng lỗi, B2 Hàng sửa, B3 Hàng lỗi đã xử lý).</summary>
        public static string? ReceiveCellOf(string purpose) => purpose switch
        {
            HandoverPurpose.New          => Moi,
            HandoverPurpose.NgReturn     => Loi,
            HandoverPurpose.RepairSend   => Sua,
            HandoverPurpose.RepairReturn => Xl,
            _ => null
        };

        /// <summary>
        /// Form Giao → loại phiếu. prevStageNg = tích "Hàng lỗi công đoạn trước"; repaired = "đã sửa xong, trả về nơi phát hiện".
        /// Trả null nếu tổ hợp không hợp lệ (kèm lý do).
        /// </summary>
        public static (string? Purpose, string? Error) Resolve(string cell, bool prevStageNg, bool repaired)
        {
            switch (cell)
            {
                case Moi: return (HandoverPurpose.New, null);
                case Loi:
                    if (prevStageNg) return (HandoverPurpose.NgReturn, null);
                    return (repaired ? HandoverPurpose.RepairReturn : HandoverPurpose.RepairSend, null);
                case Sua: return (HandoverPurpose.RepairReturn, null);
                default:  return (null, "Phân loại hàng không hợp lệ (chỉ Hàng mới / Hàng lỗi / Hàng sửa)."); // [GN-PLH] mục 36: trước "Ô giao không hợp lệ"
            }
        }

        // ================================================================
        // [GN-CELL2 2026-10-24] Phân loại hàng 2 đầu (khách chốt): bên giao chọn ô LẤY (FromCell) + ô bên nhận (ToCell);
        // bên nhận được chọn lại khi nhận. Số lượng tính thẳng theo cặp ô, loại phiếu (Purpose) chỉ còn để ghi / hiển thị.
        // ================================================================

        /// <summary>4 ô bên nhận có thể ghi vào.</summary>
        public static readonly string[] ReceiveCells = { Moi, Loi, Sua, Xl };

        public static bool IsReceiveCell(string? c) => c is Moi or Loi or Sua or Xl;

        /// <summary>Ô nhận mặc định theo ô lấy (đúng Excel): Hàng mới → Hàng mới; Hàng lỗi ☑ công đoạn trước → Hàng lỗi;
        /// Hàng lỗi (của mình) → Hàng sửa (gửi nhờ sửa); Hàng sửa (sửa hộ xong) → Hàng lỗi đã xử lý.</summary>
        public static string DefaultToCell(string fromCell, bool prevStageNg) => fromCell switch
        {
            Moi => Moi,
            Loi => prevStageNg ? Loi : Sua,
            Sua => Xl,
            _   => Moi
        };

        /// <summary>Cặp ô quen thuộc (Excel TH1–TH3). Cặp khác vẫn cho, form hiện dòng nhắc.</summary>
        public static bool IsUsualPair(string fromCell, bool prevStageNg, string toCell)
            => toCell == DefaultToCell(fromCell, prevStageNg)
               || (fromCell == Loi && !prevStageNg && toCell == Xl);   // TH2: nơi gây lỗi tự sửa xong → trả nơi phát hiện

        /// <summary>Cặp ô → loại phiếu (để ghi cột Purpose, chọn trần, luật nơi nhận).</summary>
        public static (string? Purpose, string? Error) ResolveCells(string fromCell, bool prevStageNg, string toCell)
        {
            if (!IsReceiveCell(toCell)) return (null, "Ô nhận không hợp lệ (Hàng mới / Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý).");
            switch (fromCell)
            {
                case Moi: return (HandoverPurpose.New, null);
                case Loi:
                    if (prevStageNg) return (HandoverPurpose.NgReturn, null);
                    return (toCell is Xl or Moi ? HandoverPurpose.RepairReturn : HandoverPurpose.RepairSend, null);
                case Sua: return (HandoverPurpose.RepairReturn, null);
                case Xl:  return toCell is Xl or Loi ? (HandoverPurpose.RepairReturn, null)   // [SUA-XL-GIU] hàng đã sửa: trả tiếp nơi phát hiện (ô Hàng lỗi đã xử lý) / trả lại ô Hàng lỗi [XL-TRA-LAI]
                                                     : (null, "Hàng đã sửa (ô Hàng lỗi đã xử lý) chỉ giao vào ô Hàng lỗi đã xử lý (trả tiếp nơi phát hiện) hoặc trả lại vào ô Hàng lỗi.");
                default:  return (null, "Phân loại hàng không hợp lệ (chỉ Hàng mới / Hàng lỗi / Hàng sửa).");
            }
        }
    }

    // ── DTO ───────────────────────────────────────────────────────────

    /// <summary>[GN-LEDGER] Một chứng từ góp vào một số.</summary>
    public class LedgerDoc
    {
        /// <summary>PHOI | PHIEU | NHAN | WTS</summary>
        public string   Kind  { get; set; } = "";
        public long     RefId { get; set; }
        public DateTime? At   { get; set; }
        public string   Text  { get; set; } = "";
        public decimal  Qty   { get; set; }
    }

    /// <summary>[GN-LEDGER] Một số hạng trong công thức (dấu + / −).</summary>
    public class LedgerTerm
    {
        public string  Label { get; set; } = "";
        public int     Sign  { get; set; } = 1;
        public decimal Value { get; set; }
        public List<LedgerDoc> Docs { get; set; } = new();
    }

    /// <summary>[GN-LEDGER] Diễn giải 1 ô: công thức + số thật + chứng từ.</summary>
    public class LedgerExplain
    {
        public string  Title   { get; set; } = "";
        public string  Formula { get; set; } = "";
        public string? Rule    { get; set; }
        public string? Note    { get; set; }
        public decimal Result  { get; set; }
        public List<LedgerTerm> Terms { get; set; } = new();
        /// <summary>Dòng "100 + 5 − 50 − 5 = 50" để user đối chiếu nhanh.</summary>
        public string Arithmetic
        {
            get
            {
                if (Terms.Count == 0) return Result.ToString("0.##");
                var s = "";
                for (int i = 0; i < Terms.Count; i++)
                {
                    var t = Terms[i];
                    var v = t.Value.ToString("0.##");
                    s += i == 0 ? (t.Sign < 0 ? "−" + v : v) : (t.Sign < 0 ? " − " : " + ") + v;
                }
                return s + " = " + Result.ToString("0.##");
            }
        }
    }

    /// <summary>[GN-LEDGER] Một dòng NC (GC) hoặc mã CV (HTSP/KCS/PKG) trong chi tiết.</summary>
    public class LedgerStepLine
    {
        /// <summary>[GN-WTS-CAP] ProcessGroup trên WTS: GC | TARO | BAVIA | WASHING | KCS | PKG.</summary>
        public string  Pg        { get; set; } = "";
        public string  SubGroup  { get; set; } = "";
        public string  Code      { get; set; } = "";
        public int     StepOrder { get; set; }
        public string? Machine   { get; set; }
        public bool    IsExcluded { get; set; }
        public bool    IsPrep    { get; set; }
        public bool    IsLast    { get; set; }
        public decimal? In       { get; set; }
        public decimal Ok        { get; set; }
        public decimal Ng        { get; set; }
        /// <summary>[SUA-OK-NC 2026-11-10] Gia công: hàng sửa đạt (KCS xác nhận) tính là xong NC này — không có dòng WTS (Ok chỉ là WTS).
        ///   Đi tiếp NC sau / giao theo NC như hàng OK. Nguồn: KHÁCH 10/11 hỏi tiếp ("mọi trường hợp sửa không quay lại NC"); HÙNG 10/11 19:22.</summary>
        public decimal OkSua     { get; set; }
        public decimal? Waiting  { get; set; }
        /// <summary>GC: đã gửi làm hộ từ NC này.</summary>
        public decimal HelpOut   { get; set; }
        public string? Note      { get; set; }
        /// <summary>[TARO-3T-GOP 2026-11-06] Dòng thuộc 1 bộ gộp Taro (mã gộp + các mã thành phần cùng NC): khoá bộ = mã gộp (khoá sổ). null = mã thường.</summary>
        public string? GopUnit   { get; set; }
        /// <summary>[TARO-3T-GOP] true = dòng là mã gộp (vd 3T); false = mã thành phần (vd 3T1). Chỉ có nghĩa khi GopUnit khác null.</summary>
        public bool    IsGop     { get; set; }
        /// <summary>[WTS-3T-BAO] Chỉ dòng mã gộp: số chiếc đã vào bộ theo mã lẻ (max OK + NG các mã thành phần) — chỉ để hiện câu báo, không dùng tính số.</summary>
        public decimal GopDone   { get; set; }
        /// <summary>[TARO-LAMHO-NC] Mã Taro ở NC máy loại trừ GIỮA chuỗi Gia công (HTSP làm hộ): NC đó. null = mã thường.
        ///   Vào = hàng Gia công giao theo NC cho HTSP làm NC này; không tính vào OK nhỏ nhất / Chưa làm / NG của HTSP.</summary>
        public string? LamHoNc   { get; set; }
    }

    /// <summary>[TARO-LAMHO-NC] 1 đoạn NC máy loại trừ liền nhau GIỮA chuỗi Gia công (sau nó còn NC thường) — hàng giao theo NC
    ///   từ NC thường ngay trước (FromNc, hoặc @VAO khi đoạn ở đầu chuỗi), làm xong trả về NC thường ngay sau (NextNc).
    ///   Chỉ đoạn có mã Taro (HasTaro) mới tính "Taro làm hộ". Nguồn: KHÁCH 07/11 (qua Hùng 10:03) trả lời (a): Part 3B-PT1268610
    ///   làm xong NC 2 → gửi Taro (NC 3 = H01) → về làm NC 4 → lại gửi Taro (NC 5 = H01, cuối chuỗi); HÙNG 07/11 10:18 OK kế hoạch.</summary>
    public class LedgerLamHoRun
    {
        public string       Key    { get; set; } = "";   // NC loại trừ đầu của đoạn
        public List<string> Ncs    { get; set; } = new();
        public string?      FromNc { get; set; }
        public string       NextNc { get; set; } = "";
        public bool         HasTaro { get; set; }
        /// <summary>HTSP đã nhận (phần OK lúc nhận) hàng Gia công giao theo NC cho đoạn này.</summary>
        public decimal      In     { get; set; }
        /// <summary>OK bộ Taro nhỏ nhất giữa các NC của đoạn (OK nhỏ nhất các mã Taro chính cùng NC, bộ gộp = OK gộp + min thành phần).</summary>
        public decimal      Done   { get; set; }
        /// <summary>HTSP đã trả về Gia công (hàng tốt) vào NextNc.</summary>
        public decimal      Ret    { get; set; }
        /// <summary>NG trên WTS Taro làm hộ ở các NC của đoạn (chiếc hỏng — đang giữ hoặc đã trả lỗi về Gia công).</summary>
        public decimal      Ng     { get; set; }
        /// <summary>[KHO-TARO-THANG][KTT-PHOILOI 2026-11-12] Taro phát hiện phôi lỗi khi đang làm (hàng đoạn đầu chuỗi, chưa xong bộ Taro) → trả thẳng Kho
        ///   ô Hàng lỗi (phiếu Hàng lỗi công đoạn trước HTSP → Kho, FromNC @VAO). Nguồn: KHÁCH 12/11 (3.3 a = A); HÙNG 12/11 14:00.</summary>
        public decimal      TraKho { get; set; }
        /// <summary>[KTT-PHOILOI] Chưa làm xong bộ Taro của đoạn = nhận vào − trả phôi lỗi Kho − OK bộ − NG (null = đoạn không có mã Taro).</summary>
        public decimal?     Chua   { get; set; }
        /// <summary>Hàng làm hộ của đoạn HTSP còn giữ, còn tốt = nhận vào − đã trả về (hàng tốt) − chiếc hỏng (rà độc lập gói 6 mục 1).
        ///   [KTT-PHOILOI 2026-11-12] − phôi lỗi đã trả thẳng Kho.</summary>
        public decimal      Held   => In - Ret - Ng - TraKho;
        public List<LedgerDoc> DoneDocs { get; set; } = new();
        public List<LedgerDoc> RetDocs  { get; set; } = new();
    }

    /// <summary>[GN-LEDGER] Số của 1 nhóm trên 1 PO — đúng cột Excel xưởng.</summary>
    public class LedgerGroup
    {
        public string Code  { get; set; } = "";
        public string Label { get; set; } = "";
        public bool   IsKho => Code == "KHO";

        // Nhận (theo loại phiếu, số bên nhận đã đếm)
        public decimal NhanMoi    { get; set; }
        public decimal NhanLoi    { get; set; }
        public decimal NhanSua    { get; set; }
        public decimal NhanLoiXl  { get; set; }
        public decimal NhanLamHo  { get; set; }
        // Giao (theo loại phiếu, tính cả phiếu đang chờ nhận)
        public decimal GiaoMoi    { get; set; }
        public decimal GiaoLoi    { get; set; }
        public decimal GiaoSua    { get; set; }
        public decimal GiaoLoiXl  { get; set; }
        public decimal GiaoLamHo  { get; set; }

        public decimal NhanTong => NhanMoi + NhanLoi + NhanSua + NhanLoiXl + NhanLamHo;
        /// <summary>[GN-WTS-CAP] Phần Làm hộ trả về (hàng của chính nhóm quay lại để làm tiếp).</summary>
        public decimal NhanLamHoVe { get; set; }
        /// <summary>[GN-WTS-CAP] Hàng vào SẢN XUẤT của nhóm = Nhận Hàng mới + Hàng lỗi đã xử lý + Làm hộ trả về
        /// (không tính hàng nhóm khác gửi sửa / gửi làm hộ, không tính hàng lỗi bị trả về).</summary>
        public decimal NhanSanXuat => NhanMoi + NhanLoiXl + NhanLamHoVe;
        public decimal GiaoTong => GiaoMoi + GiaoLoi + GiaoSua + GiaoLoiXl + GiaoLamHo;

        /// <summary>Hàng chưa làm (trần nhập WTS — L3). Kho: không dùng.</summary>
        public decimal ChuaLam  { get; set; }
        /// <summary>Đã làm OK (WTS) — Kho: không dùng.</summary>
        public decimal DaLam    { get; set; }
        /// <summary>NG do chính nhóm làm hỏng (WTS QtyNg) = NG cuối.</summary>
        public decimal NgCuoi   { get; set; }
        /// <summary>Còn giao = Đã làm − Giao mới (trần phiếu Hàng mới — L4). Kho: phôi tốt còn giao.</summary>
        public decimal ConGiao  { get; set; }
        /// <summary>
        /// Ô Hàng lỗi đang giữ = hàng lỗi CỦA MÌNH (bị trả về + NG làm hỏng) chưa gửi sửa / tự sửa trả / huỷ / trả khách.
        /// [GN-CELL] Trước gộp cả hàng nhóm khác gửi nhờ sửa — nay tách ra SuaGiu (Excel: 2 ô khác nhau).
        /// </summary>
        public decimal LoiGiu   { get; set; }
        /// <summary>[GN-CELL] Ô Hàng sửa đang giữ = hàng nhóm khác gửi nhờ sửa, chưa trả lại (Nhận Hàng sửa − Giao Hàng sửa).</summary>
        public decimal SuaGiu   { get; set; }
        /// <summary>Hàng nhóm khác gửi nhờ làm hộ, đang giữ.</summary>
        public decimal LamHoGiu { get; set; }
        /// <summary>[GN-RECV-NG] Master list mục 37: hàng trên phiếu đã giao TỚI nhóm này nhưng nhóm chưa bấm Nhận.
        /// Tính vào Tồn của bên nhận (khách yêu cầu), KHÔNG tính vào Chưa làm (chưa đếm thì chưa được nhập WTS — L2, L3).</summary>
        public decimal ChoNhan  { get; set; }
        /// <summary>[GC-SUA-TAI-CHO 2026-11-04] Hàng nhóm đã sửa xong (Kết quả sửa — đạt), chờ KCS xác nhận. Tính vào Tồn, KHÔNG vào Chưa làm
        /// (chưa xác nhận đạt thì chưa nhập WTS được). KCS xác nhận đạt → ô Hàng lỗi đã xử lý; KCS loại → ô Hàng lỗi của nhóm.</summary>
        public decimal ChoKcs   { get; set; }
        /// <summary>[SUA-XL-GIU 2026-11-10] Tồn hàng lỗi đã xử lý = hàng ĐÃ SỬA (KCS xác nhận đạt), nhóm đã làm xong chiếc đó trước lúc phát hiện lỗi,
        ///   giữ vụ, chờ trả tiếp về nơi phát hiện. Không vào Chưa làm (không ghi WTS số lượng), không vào Đã làm chưa giao. Tính vào Tồn.
        ///   Nguồn: KHÁCH 10/11 vòng 1 (hỏi lại 2.2 A "mọi trường hợp sửa không quay lại NC", hỏi tiếp 2.1 A "nhận ô Hàng lỗi đã xử lý = đã làm");
        ///   HÙNG 10/11 19:22 OK thiết kế Gói 15.</summary>
        public decimal XlGiu    { get; set; }
        /// <summary>Tồn = ΣNhận − ΣGiao + Chờ nhận + Chờ KCS. [GN-RECV-NG] trước: ΣNhận − ΣGiao (hàng chờ nhận nằm ngoài, ở dòng Đang giao). [GC-SUA-TAI-CHO] + Chờ KCS.</summary>
        public decimal Ton      { get; set; }

        /// <summary>Phần trong Chưa làm do trả lỗi (NG đầu) — dùng cho diễn giải.</summary>
        public decimal NgDau    { get; set; }

        public int PendingToIssue   { get; set; }
        public int PendingToReceive { get; set; }

        public Dictionary<string, LedgerExplain> Explains { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<LedgerStepLine> Steps { get; set; } = new();

        public bool HasNegative => Warnings.Count > 0;
    }

    /// <summary>[GN-LEDGER] Một PO (KhPlanDetail) — 5 nhóm + cân bằng.</summary>
    public class LedgerRow
    {
        public int     KhPlanDetailId { get; set; }
        public string  PlanNo         { get; set; } = "";
        public string  PurchaseOrder  { get; set; } = "";
        public string  PartNo         { get; set; } = "";
        public string  CustomerName   { get; set; } = "";
        public decimal PlanQty        { get; set; }
        public bool    ApplyWorkflow  { get; set; }

        public Dictionary<string, LedgerGroup> Groups { get; set; } = new();
        public LedgerGroup G(string code) => Groups.TryGetValue(code, out var g) ? g : new LedgerGroup { Code = code };

        /// <summary>Phôi Kho đã nhận thực tế.</summary>
        public decimal Phoi     { get; set; }
        /// <summary>Thành phẩm Đóng gói đã nhập Kho (bên Kho đếm OK).</summary>
        public decimal TpNhap   { get; set; }
        /// <summary>Hàng trên phiếu đã giao nhưng chưa ai nhận.
        /// [GN-RECV-NG] Nay CHỈ còn thành phẩm Đóng gói → Kho chưa nhận (phiếu khác: tính vào Tồn bên nhận qua ChoNhan).</summary>
        public decimal DangGiao { get; set; }
        /// <summary>[GN-NG-OUT] Hàng lỗi đã huỷ (ra khỏi PO).</summary>
        public decimal DaHuy    { get; set; }
        /// <summary>[GN-NG-OUT] Hàng lỗi đã trả khách (ra khỏi PO).</summary>
        public decimal TraKhach { get; set; }

        /// <summary>[KHACH-GUI-LAI 2026-11-11] Phôi khách gửi bù (bảng HandoverReplacements, chưa huỷ) — Kho nhận vào ô Hàng lỗi đã xử lý (phôi còn).
        ///   Nguồn: KHÁCH 10/11 vòng 1 câu 1a A + 1b A; HÙNG 11/11 14:39 duyệt.</summary>
        public decimal Bu       { get; set; }

        public decimal TonTong => Groups.Values.Sum(g => g.Ton);
        /// <summary>L6: Phôi = Σ Tồn các nhóm (gồm chờ nhận) + thành phẩm chờ Kho nhận + thành phẩm đã nhập kho + đã huỷ + đã trả khách [GN-NG-OUT][GN-RECV-NG].
        /// [KHACH-GUI-LAI 2026-11-11] vế trái + phôi khách gửi bù (trước: chỉ phôi nhận — bù không có chứng từ).</summary>
        public bool    Balanced => Phoi + Bu == TonTong + DangGiao + TpNhap + DaHuy + TraKhach;
        public LedgerExplain BalanceExplain { get; set; } = new();

        /// <summary>[TARO-NC-MA 2026-11-06] Mã CV Taro dùng ở NHIỀU NC của PO → danh sách NC (thứ tự quy trình). Sổ đếm theo cặp (mã, NC):
        /// khoá sổ = TaroKey(mã, NC). WTS mã này phải ghi kèm NC (WtsProductionLogs.StepNC). Mã chỉ ở 1 NC: không có ở đây, khoá = mã.</summary>
        public Dictionary<string, List<string>> TaroNcs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>[TARO-NC-MA] Mã CHUẨN BỊ Taro (1T…) dùng ở nhiều NC → danh sách NC: WTS cũng phải chọn NC, CHỈ để lấy đúng T.Chuẩn dòng (mã, NC)
        /// (mã chuẩn bị không tính số nên sổ không đổi khoá). Rà độc lập 06/11 mục 4.</summary>
        public Dictionary<string, List<string>> TaroPrepNcs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>[GN-WTS-CAP] Mã trên WTS → mã chính (N2: máy đồng dạng / mã dự phòng gộp vào mã chính). Key = "PG|mã".</summary>
        public Dictionary<string, string> CodeMain { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings => Groups.Values.SelectMany(g => g.Warnings).ToList();
        public bool HasWarning => Warnings.Count > 0 || !Balanced;
        public bool HasLoiGiu  => Groups.Values.Any(g => g.LoiGiu > 0 || g.SuaGiu > 0 || g.XlGiu > 0);   // [GN-CELL] cả 2 ô đang giữ · [XL-HIEN-LOI] + hàng đã sửa chờ trả
        /// <summary>[GN-NG-TRACE 2026-10-28] Ô bên giao lấy hàng của từng phiếu (MOI / LOI / SUA; null = phiếu theo NC) — đúng ô Compute() đã dùng để trừ.
        /// Chỉ ghi lại để khung "Hàng lỗi" đọc, không dùng để tính số nào.</summary>
        public Dictionary<long, string?> TxFromCell { get; set; } = new();

        /// <summary>
        /// [VULOI 2026-10-28] Sổ theo TỪNG VỤ LỖI — tính song song trong Compute từ đúng các chứng từ của ô Tồn hàng lỗi / Tồn hàng sửa.
        /// Khoá 0 = hàng lỗi cũ chưa có mã vụ. Luôn khớp: mỗi nhóm Σ vụ đang giữ ô Hàng lỗi = LoiGiu, ô Hàng sửa = SuaGiu.
        /// </summary>
        public Dictionary<long, LedgerCase> Cases { get; set; } = new();
        /// <summary>[VULOI] Số đang giữ của 1 vụ ở 1 nhóm · 1 ô (LOI / SUA). Vụ không có → 0.</summary>
        public decimal CaseHeld(long caseId, string group, string cell)
            => Cases.TryGetValue(caseId, out var cs) ? cs.HeldAt(group, cell) : 0m;
        /// <summary>[VULOI] Các vụ đang giữ ở 1 nhóm · 1 ô (số &gt; 0), vụ có mã trước, hàng cũ chưa có mã sau.</summary>
        public List<LedgerCase> CasesHeldAt(string group, string cell)
            => Cases.Values.Where(x => x.HeldAt(group, cell) > 0).OrderBy(x => x.Id == 0 ? 1 : 0).ThenBy(x => x.Id).ToList();

        /// <summary>[TARO-LAMHO-NC] Các đoạn NC máy loại trừ giữa chuỗi Gia công (thứ tự chuỗi) + số Taro làm hộ của từng đoạn.</summary>
        public List<LedgerLamHoRun> LamHoRuns { get; set; } = new();
        /// <summary>[TARO-LAMHO-NC] Đoạn nhận hàng trả về vào NC <paramref name="toNc"/> (NC thường ngay sau đoạn). null = không có.</summary>
        public LedgerLamHoRun? LamHoRunTo(string? toNc)
            => string.IsNullOrWhiteSpace(toNc) ? null : LamHoRuns.FirstOrDefault(r => string.Equals(r.NextNc, toNc.Trim(), StringComparison.OrdinalIgnoreCase));
        /// <summary>[TARO-LAMHO-NC] Hàng làm hộ bị lỗi (WTS Taro làm hộ NG, đã có vụ) <paramref name="group"/> đang giữ — nằm trong Tồn hàng giao theo NC.</summary>
        public decimal LamHoNgHeld(string group) => Cases.Values.Sum(x => x.HeldAt(group, HandoverCell.LamHo));
    }

    /// <summary>[GN-CELL] Một dòng "Bước" của PO — đúng kiểu Excel xưởng (B1, B2, B3…): trạng thái sổ SAU một chứng từ.</summary>
    /// <summary>[VULOI] Một chặng của vụ lỗi. Kind: PS (phát sinh) | GIAO | NHAN | TRA (NG lúc nhận trả bên giao) | HUY | KHACH.</summary>
    public class LedgerCaseHop
    {
        public DateTime? At    { get; set; }
        public string    Kind  { get; set; } = "";
        public string    Group { get; set; } = "";     // nơi làm việc này
        public string?   Other { get; set; }           // nơi kia
        public string?   Cell  { get; set; }           // ô (Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý / Hàng mới)
        public decimal   Qty   { get; set; }
        public string    Doc   { get; set; } = "";     // "Phiếu #12", "WTS #5", "Báo lỗi #3"
        public long      RefId { get; set; }
    }

    /// <summary>
    /// [VULOI 2026-10-28] Sổ của 1 vụ lỗi (1 lần phát sinh lỗi). Cân: Sinh ra = Đang giữ + Đang chuyển + Đã xử lý + Huỷ + Trả khách.
    /// Không lưu DB — Compute tính lại từ chứng từ mỗi lần.
    /// </summary>
    public class LedgerCase
    {
        public long    Id        { get; set; }
        public string  No        => NgCaseNo.Format(Id);
        public string? CatCode   { get; set; }
        public string? CatName   { get; set; }
        public string  CatLabel  => Id == 0 ? "Hàng lỗi cũ (chưa có mã vụ)" : string.IsNullOrEmpty(CatCode) ? "(chưa rõ loại lỗi)" : $"{CatCode} · {CatName}";
        public string? Origin    { get; set; }
        public string? DetectGroup { get; set; }
        public string? DetectNc  { get; set; }
        public string? Cause     { get; set; }
        /// <summary>[GN-FORM1 2026-11-05] Vụ sinh lúc nhận hàng Gia công giao theo NC (làm hộ): NC nguồn của phiếu đó — chỉ để tính NC làm tiếp
        ///   khi hàng sửa đạt về Gia công (HandoverForm.CaseNextNc). Không dùng tính số nào.</summary>
        public string? HelpFromNc { get; set; }
        /// <summary>[TARO-LAMHO-NC] Vụ sinh từ WTS Taro làm hộ NG (chặng phát sinh ở ô LAMHO): chiếc thuộc chuỗi Gia công — chỉ sửa / làm tiếp ở Gia công
        ///   (rà độc lập gói 6 vòng 3 T2: vụ đi vòng về HTSP rồi sửa tại chỗ → vào hàng sản xuất HTSP, bỏ qua NC sau).</summary>
        public bool IsLamHo => LamHoNhan || Hops.Any(h => h.Kind == "PS" && h.Cell == HandoverCell.LamHo);   // [LAMHO-NHAN-NG] + vụ NG lúc nhận phiếu theo NC
        /// <summary>[LAMHO-NHAN-NG 2026-11-08] Vụ sinh lúc ĐẾM NG KHI NHẬN phiếu theo NC (Gia công giao làm hộ / nhóm làm hộ trả về Gia công): chiếc cũng thuộc
        ///   chuỗi Gia công (chưa làm xong / chưa làm tiếp các NC sau) → luật như vụ Taro làm hộ NG: chỉ Gia công sửa, không vào ô hàng đạt / ô Hàng sửa nhóm khác.
        ///   Trước: vụ thường → nhóm làm hộ sửa tại chỗ (KCS xác nhận) hoặc giao KCS ô Hàng lỗi đã xử lý → chiếc vào hàng sản xuất nhóm đó, bỏ qua các NC sau
        ///   (số PO vẫn cân, sai đường đi). Nguồn: CLAUDE (sổ LAMHO-NHAN-NG, đo bằng kịch bản 08/11 trên 5d42247); Hùng 08/11 16:37 OK (cả làm hộ Taro lẫn N3 nhóm khác).</summary>
        public bool LamHoNhan { get; set; }
        /// <summary>[SUA-XL-GIU 2026-11-10] Vụ sinh từ báo lỗi lấy ở "đã làm xong chưa giao" (nơi phát hiện đã làm xong chiếc đó): hàng sửa đạt trả về
        ///   nơi phát hiện tính là đã làm (không làm lại). Chỉ để chọn ô, không tính số. Nguồn: KHÁCH 10/11 hỏi tiếp 2.1 A; HÙNG 10/11 19:22.</summary>
        public bool FromDa { get; set; }
        /// <summary>[LAMHO-NHAN-NG] Tên loại vụ làm hộ trong lời báo (vụ Taro làm hộ NG giữ chữ cũ).</summary>
        public string LamHoTen => Hops.Any(h => h.Kind == "PS" && h.Cell == HandoverCell.LamHo) ? "hàng Taro làm hộ bị lỗi" : "hàng làm hộ bị lỗi — NG lúc nhận hàng giao / trả theo NC";
        public DateTime? At      { get; set; }
        public string? Who       { get; set; }

        public decimal SinhRa    { get; set; }
        /// <summary>Đang giữ: khoá "NHÓM|Ô" (Ô = LOI | SUA).</summary>
        public Dictionary<string, decimal> Held { get; set; } = new();
        /// <summary>Đang chuyển: phiếu hàng lỗi đã lập, bên nhận chưa nhận (kể cả phiếu trả tự lập).</summary>
        public decimal Transit   { get; set; }
        /// <summary>Đã xử lý (nhận vào ô Hàng lỗi đã xử lý / Hàng mới) theo nhóm nhận.</summary>
        public Dictionary<string, decimal> DaXuLy { get; set; } = new();
        public decimal Huy       { get; set; }
        public decimal TraKhach  { get; set; }
        /// <summary>[KHACH-GUI-LAI 2026-11-11] Phôi khách đã gửi bù cho vụ này (chưa huỷ). Phôi bù là phôi mới vào Kho, KHÔNG thuộc cân vụ (Lech).
        ///   Trần (cả Workflow OFF): đã bù ≤ trả khách + huỷ của vụ — xem HandoverLedgerService.BuCap. Nguồn: KHÁCH 10/11 1b A; HÙNG 11/11 14:39.</summary>
        public decimal DaBu      { get; set; }
        /// <summary>[KHACH-GUI-LAI] Các phiếu bù của vụ (Id, số) — chỉ để hiện / lời báo.</summary>
        public List<(long Id, decimal Qty)> BuDocs { get; set; } = new();
        public Dictionary<string, decimal> HuyBy   { get; set; } = new();
        public Dictionary<string, decimal> KhachBy { get; set; } = new();
        /// <summary>Chặng "Nhận lỗi": số vào ô Hàng lỗi theo nhóm (gồm cả nơi tự giữ hàng mình làm hỏng).</summary>
        public Dictionary<string, decimal> NhanLoi { get; set; } = new();
        /// <summary>Chặng "Sửa": số vào ô Hàng sửa theo nhóm (nhóm khác gửi nhờ sửa).</summary>
        public Dictionary<string, decimal> NhanSua { get; set; } = new();
        /// <summary>Nhóm tự xử lý (giao từ ô Hàng lỗi thẳng về ô Hàng lỗi đã xử lý / Hàng mới).</summary>
        public HashSet<string> TuXuLy { get; set; } = new();
        /// <summary>[XL-TRA-NHOMSUA] Nhóm có Kết quả sửa của vụ được KCS xác nhận ĐẠT (≥ 1 chiếc) vào giữ ô Tồn hàng lỗi đã xử lý (đích "GIU") — nơi trả lại được
        ///   hàng đã sửa bị hỏng lại. Chỉ ghi lại, không tính số.</summary>
        public HashSet<string> SuaDatBy { get; set; } = new();
        public List<LedgerCaseHop> Hops { get; set; } = new();

        public decimal HeldAt(string g, string cell) => Held.TryGetValue(g + "|" + cell, out var v) ? v : 0m;
        /// <summary>[VL8-TIMELINE 2026-10-28] Mỗi lần ô đang giữ của vụ đổi: (lúc, "NHÓM|Ô", số). Compute ghi, chỉ dùng để tính MinHeld.</summary>
        internal List<(DateTime At, string Key, decimal Q)> Moves { get; } = new();
        /// <summary>[VL8-TIMELINE] Số thấp nhất của từng ô đang giữ theo THỨ TỰ THỜI GIAN chứng từ (chỉ ghi ô từng xuống dưới 0).
        /// Vd Kho nhận vụ 1 (+1) → giao đi (−1) → nhận lại (+1): huỷ lần nhận đầu thì cuối cùng vẫn 0 nhưng lúc giao đi là −1
        /// = hàng của vụ đã đi tiếp mà không có nguồn → K5 phải chặn (trước: chỉ so số cuối nên lọt, vụ kẹt không gỡ được).</summary>
        public Dictionary<string, decimal> MinHeld { get; set; } = new();
        public decimal HeldTotal => Held.Values.Sum();
        public decimal DaXuLyTotal => DaXuLy.Values.Sum();
        /// <summary>Phải luôn = 0 (cân vụ).</summary>
        public decimal Lech => SinhRa - HeldTotal - Transit - DaXuLyTotal - Huy - TraKhach;
        public bool IsEmpty => SinhRa == 0 && Hops.Count == 0 && Held.Values.All(v => v == 0) && Transit == 0 && DaBu == 0;   // [KHACH-GUI-LAI] vụ có phiếu bù giữ lại
    }

    public class LedgerStepRow
    {
        public int      Index  { get; set; }
        public DateTime? At    { get; set; }
        /// <summary>PHOI | GIAO | NHAN | WTS</summary>
        public string   Kind   { get; set; } = "";
        public string   Group  { get; set; } = "";
        /// <summary>Nhóm đối tác (GIAO: nhóm nhận; NHAN: nhóm giao).</summary>
        public string?  Other  { get; set; }
        /// <summary>Ô Excel của chứng từ này ở nhóm Group (MOI/LOI/SUA/XL). WTS / phôi: null.</summary>
        public string?  Cell   { get; set; }
        public bool     PrevStageNg { get; set; }
        public decimal  Qty    { get; set; }
        public decimal  Ng     { get; set; }
        public long     RefId  { get; set; }
        public string   Text   { get; set; } = "";
        /// <summary>Sổ sau bước này (mọi ô, mọi nhóm).</summary>
        public LedgerRow Row   { get; set; } = new();
    }

    // ── Dữ liệu đầu vào thuần (để tính và để test không cần DB) ─────────

    public class LedgerInput
    {
        public int     KhPlanDetailId { get; set; }
        public string  PlanNo         { get; set; } = "";
        public string  PurchaseOrder  { get; set; } = "";
        public string  PartNo         { get; set; } = "";
        public string  CustomerName   { get; set; } = "";
        public decimal PlanQty        { get; set; }
        public bool    ApplyWorkflow  { get; set; } = true;

        public List<InPhoi> Phoi { get; set; } = new();
        /// <summary>[KHACH-GUI-LAI 2026-11-11] Phôi khách gửi bù (chưa huỷ), tăng dần theo giờ máy chủ.</summary>
        public List<InBu>   Bu   { get; set; } = new();
        public List<InTx>   Txs  { get; set; } = new();
        public List<InWts>  Wts  { get; set; } = new();
        /// <summary>Snapshot NC gia công (gồm cả máy đồng dạng IsBackup).</summary>
        public List<InGcStep>  GcSteps  { get; set; } = new();
        /// <summary>Snapshot mã CV HTSP (TARO/BAVIA/WASHING), KCS, PKG.</summary>
        public List<InCodeStep> CodeSteps { get; set; } = new();
        /// <summary>[GN-NG-AFTER] Báo hàng lỗi phát hiện sau khi nhận (chưa huỷ).</summary>
        public List<InNgRep> NgReps { get; set; } = new();
        /// <summary>[GC-SUA-TAI-CHO] Kết quả sửa (chưa huỷ) + các lần KCS xác nhận (chưa huỷ).</summary>
        public List<InRepair> Repairs { get; set; } = new();
        /// <summary>[VULOI] Nhãn vụ lỗi của PO (loại lỗi, nơi phát hiện…) — chỉ để hiện, không góp vào số.</summary>
        public Dictionary<long, InCase> CaseInfo { get; set; } = new();
        /// <summary>[TARO-3T-GOP 2026-11-06] Khai báo mã gộp Taro chụp vào PO (KhPlanRouteSnapshotWtsGops): mã gộp → mã thành phần.</summary>
        public List<InGop> TaroGops { get; set; } = new();
        /// <summary>[TARO-NC-MA] Bản đã đổi mã Taro dùng ở nhiều NC sang khoá (mã, NC) — Compute không đổi lần 2. Lời cảnh báo dòng WTS thiếu / sai NC.</summary>
        public bool TaroKeyedDone { get; set; }
        public List<string> TaroWarnings { get; set; } = new();
        /// <summary>[TARO-NC-MA] Bản sao nông để đổi danh sách mã CV / WTS mà không đụng bản gốc.</summary>
        public LedgerInput ShallowCopy() => (LedgerInput)MemberwiseClone();
    }

    /// <summary>[TARO-3T-GOP 2026-11-06] 1 cặp khai báo gộp: mã gộp (vd 3T) làm thay mã thành phần (vd 3T1).</summary>
    public record InGop(string Gop, string Part);

    /// <summary>[VULOI] Nhãn 1 vụ lỗi (bảng NgCases + NgCategories).</summary>
    public record InCase(long Id, string? CatCode, string? CatName, string Origin, string DetectGroup, string? DetectNc,
                         string? Cause, DateTime At, string? Who);

    /// <summary>[GN-NG-AFTER] Group = nhóm phát hiện; Source = PHOI | CHUA | DA; NC = NC GC (CHUA); Origin = DAU_VAO | DAU_RA.</summary>
    public record InNgRep(long Id, DateTime At, string Group, string Source, string? NC, decimal Qty,
                          string Origin, string? Cause, string? Reason, string? Who,
                          long? NgCaseId = null);   // [VULOI] vụ lỗi sinh ra từ phiếu báo lỗi

    public record InPhoi(long Id, DateTime? At, decimal Qty, string? Note);

    /// <summary>[KHACH-GUI-LAI 2026-11-11] 1 lần khách gửi bù (HandoverReplacements): giờ máy chủ, lần lỗi được bù, số, người Kho nhận.</summary>
    public record InBu(long Id, DateTime At, long NgCaseId, decimal Qty, string? Who, string? Notes);

    /// <summary>[GC-SUA-TAI-CHO 2026-11-04] 1 lần KCS xác nhận hàng sửa: Ok = đạt → ô Hàng lỗi đã xử lý nhóm sửa; Ng = KCS loại → ô Hàng lỗi nhóm sửa (cùng vụ).</summary>
    public record InRepairCheck(long Id, DateTime At, decimal Ok, decimal Ng, string? Who);

    /// <summary>
    /// [GC-SUA-TAI-CHO 2026-11-04] "Kết quả sửa" — chứng từ nội bộ của nhóm sửa (Group = nhóm đang giữ hàng).
    /// Cell = SUA | LOI (ô lấy hàng); Pass = đạt (chờ KCS); Fail = không đạt (chỉ SUA → ô Hàng lỗi cùng vụ); ToNC = Gia công: NC làm tiếp hàng đạt.
    /// </summary>
    public class InRepair
    {
        public long    Id       { get; set; }
        public DateTime At      { get; set; }
        public string  Group    { get; set; } = "";
        public string  Cell     { get; set; } = "";
        public long?   NgCaseId { get; set; }
        public decimal Pass     { get; set; }
        public decimal Fail     { get; set; }
        public string? ToNC     { get; set; }
        public string? Who      { get; set; }
        public string? Notes    { get; set; }
        public List<InRepairCheck> Checks { get; set; } = new();
        public decimal Checked  => Checks.Sum(c => c.Ok + c.Ng);
        /// <summary>Số đạt còn chờ KCS xác nhận.</summary>
        public decimal Waiting  => Pass - Checked;
        /// <summary>[GN-WHY] Bản sao để tính thử (danh sách lần xác nhận riêng).</summary>
        public InRepair CloneRep() { var c = (InRepair)MemberwiseClone(); c.Checks = new List<InRepairCheck>(Checks); return c; }
    }
    public record InRecv(long Id, DateTime At, decimal Ok, decimal Ng, string? NgReason,
                         string? Cell = null, string? CellReason = null,   // [GN-CELL2] ô bên nhận thực nhận + lý do đổi
                         long? NgCaseId = null,    // [VULOI] vụ lỗi sinh ra ở lần nhận (phiếu chưa mang vụ)
                         string? ToNC = null);     // [NHAN-DOI-O-NC 2026-11-06] NC nhận vào Gia công của lần nhận (đổi ô, phiếu không có NC). null = theo phiếu
    public class InTx
    {
        public long    Id        { get; set; }
        public string  From      { get; set; } = "";
        public string  To        { get; set; } = "";
        public string? Purpose   { get; set; }
        /// <summary>[GN-CELL] Ô bên giao chọn (MOI / LOI / SUA). Null = phiếu cũ.</summary>
        public string? FromCell  { get; set; }
        public bool    IsReturnNg { get; set; }
        public decimal Issued    { get; set; }
        public string  Status    { get; set; } = "PENDING";
        public string? FromNC    { get; set; }
        public string? ToNC      { get; set; }
        public DateTime At       { get; set; }
        public string? Notes     { get; set; }
        /// <summary>[GN-NG-OUT] Lỗi cụ thể ghi trên phiếu hàng lỗi / huỷ / trả khách.</summary>
        public string? NgReason  { get; set; }
        /// <summary>[GN-RECV-NG] Phiếu trả hàng lỗi tự lập khi nhận: Id lần nhận gốc. Null = phiếu người dùng lập.</summary>
        public long?   SourceReceiveId { get; set; }
        /// <summary>[GN-CELL2] Ô bên giao chỉ định cho bên nhận. Null = phiếu cũ.</summary>
        public string? ToCell    { get; set; }
        /// <summary>[GN-VOID-RULE] Phần chưa nhận đã huỷ (Issued đã trừ phần này). Chỉ dùng hiển thị dòng B.</summary>
        public decimal Closed    { get; set; }
        public DateTime? ClosedAt { get; set; }
        /// <summary>[K5-CLOSE-NHIEU 2026-11-06] Từng lần huỷ phần chưa nhận (bảng HandoverTxCloses), tăng dần theo giờ; Σ Qty = Closed.
        /// Rỗng / lệch Closed (input dựng tay, dữ liệu trước SQL 10) → ClosesOf coi như 1 lần lúc ClosedAt như cũ.</summary>
        public List<InClose> Closes { get; set; } = new();
        /// <summary>[VULOI] Vụ lỗi của phiếu (phiếu hàng lỗi). Null = không mang vụ / hàng lỗi cũ chưa có mã.</summary>
        public long?   NgCaseId  { get; set; }
        public List<InRecv> Receives { get; set; } = new();
        /// <summary>[GN-WHY] Bản sao để tính thử (danh sách lần nhận riêng).</summary>
        public InTx CloneTx() { var c = (InTx)MemberwiseClone(); c.Receives = new List<InRecv>(Receives); c.Closes = new List<InClose>(Closes); return c; }   // [K5-CLOSE-NHIEU] + danh sách lần huỷ riêng
    }
    /// <summary>[K5-CLOSE-NHIEU 2026-11-06] 1 lần huỷ phần chưa nhận: Id dòng HandoverTxCloses (0 = dòng bù / dựng từ bộ cột cũ), giờ máy chủ, số.</summary>
    public record InClose(long Id, DateTime At, decimal Qty);
    public record InWts(long Id, DateTime At, string ProcessGroup, string? NC, string? WtsCode,
                        decimal Ok, decimal Ng, string? Worker,
                        long? NgCaseId = null,   // [VULOI] vụ lỗi của phần NG
                        DateTime? Saved = null,  // [K5-VONG-SUA 2026-11-05] giờ MÁY CHỦ lưu dòng (CreatedAt; At = giờ làm người nhập). null = dòng đang thử lưu (tính là mới nhất)
                        // [WTS-SUA-PHAN 2026-11-05] dòng là bản SỬA của dòng khác: Saved = giờ lưu gốc; Vers = các bản của cả chuỗi sửa (cũ → mới,
                        //   mỗi bản: giờ lưu, OK, NG; bản đang thử lưu có At = DateTime.MaxValue). null = dòng thường. Xem SnapshotBefore.
                        IReadOnlyList<WtsVer>? Vers = null,
                        string? StepNC = null,   // [TARO-NC-MA 2026-11-06] NC Taro của dòng (WtsProductionLogs.StepNC) — chỉ khi mã CV dùng ở nhiều NC
                        // [H10-WHY-NGAYCU 2026-11-08] giờ BẮT ĐẦU dòng (WtsProductionLogs.StartTime) — chỉ cho lời khuyên gợi ý gỡ "dòng ngày trước hôm nay"
                        //   (máy chủ chỉ cho sửa / xoá dòng BẮT ĐẦU hôm nay — WtsSaveService; At = EndTime lệch với ca qua đêm). Không dùng tính số nào.
                        //   null = dòng đang thử lưu / dựng tay → lời khuyên xét theo At như cũ. Nguồn: CLAUDE (rà độc lập Gói 10 vòng 2, sổ H10-WHY-NGAYCU).
                        DateTime? Start = null);
    /// <summary>[WTS-SUA-PHAN] 1 bản của dòng WTS trong chuỗi sửa: có hiệu lực từ At (giờ máy chủ lưu bản đó) tới bản sau.</summary>
    public record WtsVer(DateTime At, decimal Ok, decimal Ng);
    public record InGcStep(int StepOrder, string NC, bool IsBackup, string? ParentNC, string? Machine, bool IsExcluded);
    /// <summary>Pg = TARO | BAVIA | WASHING | KCS | PKG; Code = mã CV khớp với WtsCode ?? NC của WTS.
    /// [TARO-NC-MA 2026-11-06] StepNc = NC Taro của dòng (dòng dự phòng: NC dòng cha = ParentNC); RawCode = mã CV gốc khi Code đã đổi sang
    /// khoá (mã, NC) vì mã dùng ở nhiều NC (TaroKeyed). Nhóm khác: null.</summary>
    public record InCodeStep(string Pg, int StepOrder, string Code, bool IsBackup, string? ParentCode, string? StepName,
                             string? StepNc = null, string? RawCode = null);

    // ── Service ───────────────────────────────────────────────────────

    public class HandoverLedgerService
    {
        private readonly AppDbContext _db;
        public HandoverLedgerService(AppDbContext db) => _db = db;

        public static readonly string[] GroupCodes = { "KHO", "GC", "HTSP", "KCS", "PKG" };

        public static string GroupLabel(string code) => code switch
        {
            "KHO" => "Kho", "GC" => "Gia công", "HTSP" => "HTSP", "KCS" => "KCS", "PKG" => "Đóng gói",
            "HUY" => "Huỷ", "KHACH" => "Khách hàng", // [GN-NG-OUT] nơi nhận của phiếu ra khỏi PO
            _ => code
        };

        /// <summary>[GN-LEDGER][N5] Mã chuẩn bị: tính giờ, không tính số lượng.</summary>
        public static readonly string[] PrepCodes = { "1V", "1T", "1R", "1K", "1D", "1G" };
        public static bool IsPrepCode(string? code)
            => !string.IsNullOrWhiteSpace(code) && PrepCodes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);

        static string Pg2Group(string pg) => pg switch
        {
            "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", "KCS" => "KCS", "PKG" => "PKG", _ => ""
        };

        static string SubLabel(string pg) => pg switch
        {
            "TARO" => "Taro", "BAVIA" => "Bavia", "WASHING" => "Rửa", "KCS" => "KCS", "PKG" => "Đóng gói", _ => pg
        };

        static int GroupRank(string g) => g switch
        {
            "KHO" => 0, "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1
        };

        // ================================================================
        // LOAD từ DB
        // ================================================================

        public async Task<List<LedgerRow>> GetLedgerAsync(int? customerId = null, int? month = null, int? year = null)
        {
            var q = _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer).AsQueryable();
            if (customerId.HasValue) q = q.Where(d => d.KhPlan!.CustomerId == customerId.Value);
            if (month.HasValue && year.HasValue)
                q = q.Where(d => d.KhPlan!.PlanDate.Month == month.Value && d.KhPlan.PlanDate.Year == year.Value);
            else if (year.HasValue)
                q = q.Where(d => d.KhPlan!.PlanDate.Year == year.Value);
            var details = await q.OrderBy(d => d.KhPlan!.PlanNo).ThenBy(d => d.PartNo).AsNoTracking().ToListAsync();
            if (!details.Any()) return new List<LedgerRow>();
            var inputs = await LoadInputsAsync(details);
            return inputs.Select(Compute).ToList();
        }

        /// <summary>
        /// [GN-LEDGER] Sổ của 1 PO.
        /// [GN-WTS-CAP] excludeWtsIds: bỏ các dòng WTS đang sửa (sẽ void) khỏi phép tính;
        /// extraWts: thêm dòng WTS giả định (chưa lưu) để thử trước "lưu xong có ô nào âm không" (K5, K8).
        /// </summary>
        public async Task<LedgerRow?> GetLedgerForDetailAsync(int khPlanDetailId,
            IReadOnlyCollection<long>? excludeWtsIds = null, IEnumerable<InWts>? extraWts = null)
        {
            var details = await _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer)
                .Where(d => d.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync();
            if (!details.Any()) return null;
            var inputs = await LoadInputsAsync(details);
            var inp = inputs[0];
            if (excludeWtsIds != null && excludeWtsIds.Count > 0)
                inp.Wts = inp.Wts.Where(w => !excludeWtsIds.Contains(w.Id)).ToList();
            if (extraWts != null)
                inp.Wts.AddRange(extraWts);
            return Compute(inp);
        }

        /// <summary>[GN-WHY] Dữ liệu đầu vào thô của 1 PO (để tính thử "nếu bỏ chứng từ X thì sao" — chỉ đọc).</summary>
        public async Task<LedgerInput?> GetInputForDetailAsync(int khPlanDetailId)
        {
            var details = await _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer)
                .Where(d => d.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync();
            if (!details.Any()) return null;
            return (await LoadInputsAsync(details))[0];
        }

        /// <summary>[GN-WTS-CAP] Sổ của nhiều PO (dùng cho lọc danh sách PO trên trang WTS).</summary>
        public async Task<Dictionary<int, LedgerRow>> GetLedgerForDetailsAsync(IEnumerable<int> khPlanDetailIds)
        {
            var ids = khPlanDetailIds.Distinct().ToList();
            if (!ids.Any()) return new Dictionary<int, LedgerRow>();
            var details = await _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer)
                .Where(d => ids.Contains(d.KhPlanDetailId)).AsNoTracking().ToListAsync();
            if (!details.Any()) return new Dictionary<int, LedgerRow>();
            var inputs = await LoadInputsAsync(details);
            return inputs.Select(Compute).ToDictionary(r => r.KhPlanDetailId);
        }

        private async Task<List<LedgerInput>> LoadInputsAsync(List<KhPlanDetail> details)
        {
            var ids     = details.Select(d => d.KhPlanDetailId).ToList();
            var idsLong = ids.Select(i => (long?)i).ToList();

            var phoi = await _db.KhoVatLieus
                .Where(k => k.PlanDetailId != null && idsLong.Contains(k.PlanDetailId) && k.IsActive)
                .Select(k => new { k.KhoVatLieuId, k.PlanDetailId, k.NgayNhanPhoi, k.SoLuongThucNhan, k.GhiChuPhoi })
                .AsNoTracking().ToListAsync();

            var txs = await _db.HandoverTransactions
                .Where(t => ids.Contains(t.KhPlanDetailId) && !t.IsVoided)
                .Include(t => t.Receives)
                .AsNoTracking().ToListAsync();
            // [K5-CLOSE-NHIEU 2026-11-06] từng lần huỷ phần chưa nhận — chỉ đọc khi có phiếu đã huỷ phần còn (đa số PO không có)
            var closedTxIds = txs.Where(t => (t.QtyClosed ?? 0) > 0).Select(t => t.HandoverTxId).ToList();
            var closeRows = closedTxIds.Any()
                ? await _db.HandoverTxCloses.Where(c => closedTxIds.Contains(c.HandoverTxId))
                    .Select(c => new { c.CloseId, c.HandoverTxId, c.ClosedAt, c.Qty, c.IsMigrated })
                    .AsNoTracking().ToListAsync()
                : new();

            // [KHACH-GUI-LAI 2026-11-11] phôi khách gửi bù (chưa huỷ) — bảng SQL 15_KHBU
            var bus = await _db.HandoverReplacements
                .Where(b => ids.Contains(b.KhPlanDetailId) && !b.IsVoided)
                .Select(b => new { b.ReplacementId, b.KhPlanDetailId, b.NgCaseId, b.Qty, b.ReceivedAt, b.ReceivedBy, b.Notes })
                .AsNoTracking().ToListAsync();
            var buUserIds = bus.Select(b => b.ReceivedBy).Distinct().ToList();
            var buUsers = buUserIds.Any()
                ? await _db.Users.Where(u => buUserIds.Contains(u.UserId)).Select(u => new { u.UserId, Name = u.FullName ?? u.Username }).AsNoTracking().ToListAsync()
                : new();

            var wts = await _db.WtsProductionLogs
                .Where(w => ids.Contains(w.KhPlanDetailId) && !w.IsVoided)
                .Select(w => new
                {
                    w.WtsLogId, w.KhPlanDetailId, w.EndTime, w.ProcessGroup, w.NC, w.WtsCode,
                    w.StartTime,  // [H10-WHY-NGAYCU] chỉ cho lời khuyên gợi ý gỡ
                    w.StepNC,     // [TARO-NC-MA 2026-11-06]
                    w.QtyDone, w.QtyOk, w.QtyNg,
                    w.NgCaseId,   // [VULOI]
                    w.CreatedAt,  // [K5-VONG-SUA]
                    w.WorkerId,   // [WTS-SUA-GIO-GOC]
                    Worker = w.Worker != null ? (w.Worker.FullName ?? w.Worker.Username) : null
                })
                .AsNoTracking().ToListAsync();
            // [WTS-SUA-GIO-GOC 2026-11-05] dòng WTS đã huỷ vì "Sửa lại" → dòng mới thay nó giữ giờ máy chủ lưu GỐC (xem EditOrigins)
            var wtsEdited = await _db.WtsProductionLogs
                .Where(w => ids.Contains(w.KhPlanDetailId) && w.IsVoided && w.VoidReason == WtsEditVoidReason && w.VoidedAt != null)
                .Select(w => new { w.WtsLogId, w.KhPlanDetailId, w.WorkerId, w.ProcessGroup, w.NC, w.WtsCode, w.StepNC, w.CreatedAt, w.VoidedAt,
                                   w.QtyDone, w.QtyOk, w.QtyNg })   // [WTS-SUA-PHAN] + số của dòng cũ · [TARO-NC-MA] + NC Taro
                .AsNoTracking().ToListAsync();
            var editDets = wtsEdited.Select(w => w.KhPlanDetailId).ToHashSet();   // chỉ PO có dòng sửa mới cần ghép (rà độc lập lần 2: hiệu năng)
            var wtsOrigin = EditOriginsFull(
                wts.Where(w => editDets.Contains(w.KhPlanDetailId)).Select(w => new WtsEditNode(w.WtsLogId, w.KhPlanDetailId, w.WorkerId, w.ProcessGroup, w.NC, WtsEditCode(w.WtsCode, w.StepNC), w.CreatedAt, null,
                    w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0)), w.QtyNg ?? 0)),
                wtsEdited.Select(w => new WtsEditNode(w.WtsLogId, w.KhPlanDetailId, w.WorkerId, w.ProcessGroup, w.NC, WtsEditCode(w.WtsCode, w.StepNC), w.CreatedAt, w.VoidedAt,
                    w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0)), w.QtyNg ?? 0)));   // [TARO-NC-MA] sửa đổi NC Taro = khác khoá (không ghép, như đổi mã)

            // [GN-NG-AFTER] báo hàng lỗi phát hiện sau khi nhận (chưa huỷ)
            var ngReps = await _db.HandoverNgReports
                .Where(b => ids.Contains(b.KhPlanDetailId) && !b.IsVoided)
                .Select(b => new
                {
                    b.HandoverNgReportId, b.KhPlanDetailId, b.CreatedAt, b.GroupCode, b.SourceCell, b.NC, b.Qty, b.Origin, b.CauseGroupCode, b.NgReason,
                    b.NgCaseId,   // [VULOI]
                    Who = b.CreatedByUser != null ? (b.CreatedByUser.FullName ?? b.CreatedByUser.Username) : null
                })
                .AsNoTracking().ToListAsync();

            // [GC-SUA-TAI-CHO 2026-11-04] Kết quả sửa (chưa huỷ) + lần KCS xác nhận (chưa huỷ)
            var reps = await _db.HandoverRepairResults
                .Where(x => ids.Contains(x.KhPlanDetailId) && !x.IsVoided)
                .Select(x => new
                {
                    x.HandoverRepairResultId, x.KhPlanDetailId, x.CreatedAt, x.GroupCode, x.SourceCell, x.NgCaseId, x.QtyPass, x.QtyFail, x.ToNC, x.Notes,
                    Who = x.CreatedByUser != null ? (x.CreatedByUser.FullName ?? x.CreatedByUser.Username) : null
                })
                .AsNoTracking().ToListAsync();
            var repChecks = reps.Any()
                ? await _db.HandoverRepairChecks
                    .Where(c => ids.Contains(c.RepairResult.KhPlanDetailId) && !c.RepairResult.IsVoided && !c.IsVoided)   // lọc theo PO (rà độc lập T6: danh sách id dài khi tải cả năm)
                    .Select(c => new
                    {
                        c.HandoverRepairCheckId, c.HandoverRepairResultId, c.CheckedAt, c.QtyOk, c.QtyNg,
                        Who = c.CheckedByUser != null ? (c.CheckedByUser.FullName ?? c.CheckedByUser.Username) : null
                    })
                    .AsNoTracking().ToListAsync()
                : new();

            // [VULOI 2026-10-28] Nhãn vụ lỗi (loại lỗi, nơi phát hiện) — chỉ để hiện, số của vụ tính từ chứng từ ở Compute
            var ngCases = await _db.NgCases.Where(x => ids.Contains(x.KhPlanDetailId))
                .Select(x => new { x.NgCaseId, x.KhPlanDetailId, x.NgCategoryId, x.OriginKind, x.DetectGroupCode, x.DetectNC, x.CauseGroupCode, x.CreatedAt, x.CreatedBy })
                .AsNoTracking().ToListAsync();
            var ngCatIds = ngCases.Select(x => x.NgCategoryId).Distinct().ToList();
            var ngCats = ngCatIds.Any()
                ? await _db.NgCategories.Where(x => ngCatIds.Contains(x.NgCategoryId)).Select(x => new { x.NgCategoryId, x.Code, x.Name }).AsNoTracking().ToListAsync()
                : new();
            var ngCaseUserIds = ngCases.Select(x => x.CreatedBy).Distinct().ToList();
            var ngCaseUsers = ngCaseUserIds.Any()
                ? await _db.Users.Where(u => ngCaseUserIds.Contains(u.UserId)).Select(u => new { u.UserId, Name = u.FullName ?? u.Username }).AsNoTracking().ToListAsync()
                : new();
            // [DS-LOI-LIVE 2026-10-28] chữ lỗi trên phiếu / lần nhận / báo lỗi có mã vụ → tên loại lỗi HIỆN TẠI (chỉ hiển thị, không đụng số)
            var caseLbl = new Dictionary<long, string>();
            foreach (var x in ngCases)
            {
                var k = ngCats.FirstOrDefault(c => c.NgCategoryId == x.NgCategoryId);
                if (k != null) caseLbl[x.NgCaseId] = NgCaseService.LabelOf(k.Code, k.Name);
            }

            var mach = await _db.KhPlanRouteSnapshotMachining.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.MachineRegistered })
                .AsNoTracking().ToListAsync();
            var excl = await _db.KhPlanRouteSnapshotMachineExclude.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.SoMay })
                .AsNoTracking().ToListAsync();
            var taro = await _db.KhPlanRouteSnapshotTaro.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.WtsTaskCode, s.IsBackup, s.ParentNC, s.StepName })
                .AsNoTracking().ToListAsync();
            var gops = await _db.KhPlanRouteSnapshotWtsGops.Where(s => ids.Contains(s.KhPlanDetailId))   // [TARO-3T-GOP 2026-11-06] khai báo gộp chụp vào PO
                .Select(s => new { s.KhPlanDetailId, s.GopCode, s.PartCode })
                .AsNoTracking().ToListAsync();
            var bavia = await _db.KhPlanRouteSnapshotBavia.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName })
                .AsNoTracking().ToListAsync();
            var washing = await _db.KhPlanRouteSnapshotWashing.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName })
                .AsNoTracking().ToListAsync();
            var kcs = await _db.KhPlanRouteSnapshotInspection.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName })
                .AsNoTracking().ToListAsync();
            var pkg = await _db.KhPlanRouteSnapshotPackaging.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName })
                .AsNoTracking().ToListAsync();

            var result = new List<LedgerInput>();
            foreach (var d in details)
            {
                var id = d.KhPlanDetailId;
                var exclSet = excl.Where(e => e.KhPlanDetailId == id && !string.IsNullOrWhiteSpace(e.SoMay))
                                  .Select(e => e.SoMay!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var inp = new LedgerInput
                {
                    KhPlanDetailId = id,
                    PlanNo         = d.KhPlan?.PlanNo ?? "",
                    PurchaseOrder  = d.PurchaseOrder,
                    PartNo         = d.PartNo,
                    CustomerName   = d.KhPlan?.Customer?.CustomerName ?? "",
                    PlanQty        = d.Quantity,
                    ApplyWorkflow  = d.KhPlan?.ApplyWorkflow ?? true,
                    Phoi = phoi.Where(p => p.PlanDetailId == id)
                               .Select(p => new InPhoi(p.KhoVatLieuId, p.NgayNhanPhoi, p.SoLuongThucNhan ?? 0, p.GhiChuPhoi)).ToList(),
                    Bu = bus.Where(b => b.KhPlanDetailId == id).OrderBy(b => b.ReceivedAt).ThenBy(b => b.ReplacementId)   // [KHACH-GUI-LAI]
                            .Select(b => new InBu(b.ReplacementId, b.ReceivedAt, b.NgCaseId, b.Qty, buUsers.FirstOrDefault(u => u.UserId == b.ReceivedBy)?.Name, b.Notes)).ToList(),
                    Txs = txs.Where(t => t.KhPlanDetailId == id).Select(t => new InTx
                    {
                        Id = t.HandoverTxId, From = t.FromGroupCode, To = t.ToGroupCode, Purpose = t.Purpose,
                        FromCell = t.FromCell, // [GN-CELL]
                        IsReturnNg = t.IsReturnNg, Issued = t.QtyIssued, Status = t.Status,
                        FromNC = t.FromNC, ToNC = t.ToNC, At = t.IssuedAt, Notes = t.Notes,
                        NgReason = NgCaseService.LiveReason(t.NgCaseId, t.NgReason, caseLbl), // [GN-NG-OUT]; [DS-LOI-LIVE] tên lỗi hiện tại
                        SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                        ToCell = t.ToCell, // [GN-CELL2]
                        Closed = t.QtyClosed ?? 0, ClosedAt = t.ClosedAt, // [GN-VOID-RULE]
                        Closes = CloseRows(t.QtyClosed ?? 0, t.ClosedAt, t.IssuedAt,   // [K5-CLOSE-NHIEU] từng lần huỷ (+ dòng bù phần chênh)
                                    closeRows.Where(c => c.HandoverTxId == t.HandoverTxId).Select(c => (c.CloseId, c.ClosedAt, c.Qty, c.IsMigrated))),
                        NgCaseId = t.NgCaseId, // [VULOI]
                        Receives = t.Receives.Where(r => !r.IsVoided)
                                    .Select(r => new InRecv(r.ReceiveId, r.ReceivedAt, r.QtyOk, r.QtyNg, NgCaseService.LiveReason(r.NgCaseId, r.NgReason, caseLbl), r.ToCell, r.ToCellReason, r.NgCaseId, r.ToNC)).ToList()   // [DS-LOI-LIVE] · [NHAN-DOI-O-NC] + NC lần nhận
                    }).ToList(),
                    Wts = wts.Where(w => w.KhPlanDetailId == id).Select(w => new InWts(
                        w.WtsLogId, w.EndTime, w.ProcessGroup, w.NC, w.WtsCode,
                        w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0)), w.QtyNg ?? 0, w.Worker, w.NgCaseId,
                        wtsOrigin.TryGetValue(w.WtsLogId, out var _goc) ? _goc.Origin : w.CreatedAt,   // [VULOI] + vụ lỗi · [K5-VONG-SUA] + giờ máy chủ lưu · [WTS-SUA-GIO-GOC] dòng sửa = giờ lưu gốc
                        _goc?.Vers, w.StepNC, w.StartTime)).ToList(),   // [WTS-SUA-PHAN] các bản của chuỗi sửa · [TARO-NC-MA] + NC Taro · [H10-WHY-NGAYCU] + giờ bắt đầu
                    GcSteps = mach.Where(m => m.KhPlanDetailId == id).Select(m => new InGcStep(
                        m.StepOrder, m.NC ?? "", m.IsBackup, m.ParentNC, m.MachineRegistered,
                        !string.IsNullOrWhiteSpace(m.MachineRegistered) && exclSet.Contains(m.MachineRegistered!.Trim()))).ToList(),
                    NgReps = ngReps.Where(b => b.KhPlanDetailId == id).Select(b => new InNgRep(   // [GN-NG-AFTER]
                        b.HandoverNgReportId, b.CreatedAt, b.GroupCode, b.SourceCell, b.NC, b.Qty, b.Origin, b.CauseGroupCode,
                        NgCaseService.LiveReason(b.NgCaseId, b.NgReason, caseLbl), b.Who, b.NgCaseId)).ToList(),   // [VULOI] + vụ lỗi; [DS-LOI-LIVE] tên lỗi hiện tại
                    Repairs = reps.Where(x => x.KhPlanDetailId == id).Select(x => new InRepair   // [GC-SUA-TAI-CHO]
                    {
                        Id = x.HandoverRepairResultId, At = x.CreatedAt, Group = x.GroupCode, Cell = x.SourceCell, NgCaseId = x.NgCaseId,
                        Pass = x.QtyPass, Fail = x.QtyFail, ToNC = x.ToNC, Who = x.Who, Notes = x.Notes,
                        Checks = repChecks.Where(c => c.HandoverRepairResultId == x.HandoverRepairResultId)
                                          .Select(c => new InRepairCheck(c.HandoverRepairCheckId, c.CheckedAt, c.QtyOk, c.QtyNg, c.Who)).ToList()
                    }).ToList(),
                    CaseInfo = ngCases.Where(x => x.KhPlanDetailId == id).ToDictionary(x => x.NgCaseId, x =>   // [VULOI]
                    {
                        var cat = ngCats.FirstOrDefault(k => k.NgCategoryId == x.NgCategoryId);
                        return new InCase(x.NgCaseId, cat?.Code, cat?.Name, x.OriginKind, x.DetectGroupCode, x.DetectNC, x.CauseGroupCode, x.CreatedAt,
                                          ngCaseUsers.FirstOrDefault(u => u.UserId == x.CreatedBy)?.Name);
                    }),
                };
                // Mã CV trên WTS: Taro = WtsTaskCode (trống thì NC) — khớp HieuSuat_Index; Bavia / Rửa / KCS / Đóng gói = NC
                // [GN-WTS-CAP] trước: WtsTaskCode ?? NC → WtsTaskCode = "" thì mã rỗng, không khớp WTS
                // [GN-WTS-CAP] Taro dự phòng: ParentNC là NC của dòng cha → đổi sang mã CV của dòng cha để gộp đúng (N2)
                var taroOf = taro.Where(s => s.KhPlanDetailId == id).ToList();
                string TaroCode(string? wtsTaskCode, string? nc) => string.IsNullOrWhiteSpace(wtsTaskCode) ? (nc ?? "") : wtsTaskCode!;
                string? TaroParentCode(string? parentNc)
                {
                    if (string.IsNullOrWhiteSpace(parentNc)) return parentNc;
                    var p = taroOf.FirstOrDefault(x => !x.IsBackup && string.Equals((x.NC ?? "").Trim(), parentNc.Trim(), StringComparison.OrdinalIgnoreCase));   // [WTS-MA-TRIM] rà Gói 7 vòng 2: so đã bỏ khoảng trắng (Part Master SameCode cũng Trim)
                    return p == null ? parentNc : TaroCode(p.WtsTaskCode, p.NC);
                }
                // [TARO-NC-MA 2026-11-06] + NC của dòng (dự phòng: NC dòng cha) — Compute đổi mã dùng ở nhiều NC sang khoá (mã, NC) (TaroKeyed)
                inp.CodeSteps.AddRange(taroOf.Select(s => new InCodeStep("TARO", s.StepOrder, TaroCode(s.WtsTaskCode, s.NC), s.IsBackup, TaroParentCode(s.ParentNC), s.StepName,
                    (s.IsBackup ? s.ParentNC : s.NC)?.Trim())));
                inp.TaroGops = gops.Where(g => g.KhPlanDetailId == id).Select(g => new InGop(g.GopCode.Trim(), g.PartCode.Trim())).ToList();   // [TARO-3T-GOP]
                inp.CodeSteps.AddRange(bavia.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("BAVIA", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(washing.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("WASHING", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(kcs.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("KCS", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(pkg.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("PKG", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                TrimInputCodes(inp);   // [WTS-MA-TRIM]
                result.Add(inp);
            }
            return result;
        }

        // ================================================================
        // PHÂN LOẠI PHIẾU (phiếu cũ chưa có Purpose → suy ra, không sửa DB)
        // ================================================================

        /// <summary>[GN-LEDGER] Loại phiếu. Phiếu mới: đọc cột Purpose. Phiếu cũ (Purpose NULL): suy ra từ cờ cũ.</summary>
        public static string Classify(InTx t, string? gcLastNc)
        {
            if (!string.IsNullOrWhiteSpace(t.Purpose)) return t.Purpose!;
            if (t.IsReturnNg) return HandoverPurpose.NgReturn;
            // Phiếu cũ: GC giao giữa chừng (NC chưa phải NC cuối) sang nhóm khác = gửi làm hộ (N3/N4)
            if (t.From == "GC" && t.To != "GC" && t.To != "KHO" && !string.IsNullOrEmpty(t.FromNC)
                && !string.Equals(t.FromNC, gcLastNc, StringComparison.OrdinalIgnoreCase))
                return HandoverPurpose.HelpSend;
            // Phiếu cũ: nhóm sau giao ngược về GC (không phải trả NG) = làm hộ xong trả về
            if (t.To == "GC" && t.From is "HTSP" or "KCS" or "PKG") return HandoverPurpose.HelpReturn;
            return HandoverPurpose.New;
        }

        static decimal Received(InTx t) => t.Receives.Sum(r => r.Ok + r.Ng);

        /// <summary>[GN-CELL2] Ô bên giao chỉ định cho bên nhận. Phiếu cũ (ToCell NULL): suy từ loại phiếu (Excel B1/B2/B3). Phiếu theo NC / Huỷ / Trả khách: null.</summary>
        public static string? DesigCellOf(InTx t, string purpose)
            => HandoverCell.IsReceiveCell(t.ToCell) ? t.ToCell : HandoverCell.ReceiveCellOf(purpose);

        /// <summary>[GN-CELL2] Ghi chú khi bên nhận chọn ô khác ô bên giao chỉ định (hiện ở diễn giải + dòng B).</summary>
        static string CellChangeNote(string? desig, InRecv r)
            => HandoverCell.IsReceiveCell(r.Cell) && desig != null && r.Cell != desig
               ? $" · bên giao chỉ định {HandoverCell.Label(desig)}, bên nhận chọn {HandoverCell.Label(r.Cell)}" + (string.IsNullOrWhiteSpace(r.CellReason) ? "" : $" ({r.CellReason})")
               : "";

        /// <summary>Số tính là "đã giao" của 1 phiếu: đã nhận đủ → số nhận; chưa đủ → số trên phiếu (L4: tính cả phiếu chưa nhận).</summary>
        static decimal GiaoQty(InTx t)
        {
            var rec = Received(t);
            return t.Status == "COMPLETED" ? rec : Math.Max(t.Issued, rec);
        }

        static string TxText(InTx t, string purpose)
            => $"Phiếu #{t.Id} · {GroupLabel(t.From)} → {GroupLabel(t.To)} · {PairLabel(t, purpose)}"
               + (string.IsNullOrEmpty(t.FromNC) || IsKhoTaro(t, purpose) ? ""   // [KHO-TARO-THANG] chữ ở PairLabel
                  : IsTaroPhoiLoi(t, purpose) ? " · " + TaroPhoiLoiFrom   // [KTT-PHOILOI]
                  : $" · từ {NcText(t.FromNC)}")   // [N3-FIRST] chữ @VAO
               + (string.IsNullOrEmpty(t.ToNC) ? "" : $" · vào NC {t.ToNC}")
               + (t.Status == "COMPLETED" ? "" : $" · {(Received(t) > 0 ? "nhận dở" : "chờ nhận")} (phiếu {t.Issued:0.##})");

        /// <summary>[GN-CELL2] Nhãn phiếu theo cặp ô: "Hàng lỗi ☑ → ô Hàng lỗi". Phiếu theo NC / Huỷ / Trả khách: tên loại phiếu.</summary>
        public static string PairLabel(InTx t, string purpose)
        {
            if (IsKhoTaro(t, purpose)) return KhoTaroLabel;   // [KHO-TARO-THANG]
            if (purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn || HandoverPurpose.IsOut(purpose)) return HandoverPurpose.Label(purpose);
            var from = HandoverCell.IssueCellOf(purpose, t.FromCell);
            return $"{HandoverCell.Label(from)}{(purpose == HandoverPurpose.NgReturn ? " ☑ công đoạn trước" : "")} → ô {HandoverCell.Label(DesigCellOf(t, purpose))}";
        }

        static string WtsText(InWts w)
            => $"WTS #{w.Id} · {SubLabel(w.ProcessGroup)} {w.WtsCode ?? w.NC} · OK {w.Ok:0.##} · NG {w.Ng:0.##}"
               + (string.IsNullOrEmpty(w.Worker) ? "" : $" · {w.Worker}");

        // ================================================================
        // TÍNH — hàm thuần, không đụng DB
        // ================================================================


        // =====================================================================
        // [K5-VONG-SUA 2026-11-05] K5 xét THỨ TỰ THỜI GIAN với hàng sửa trả về (vòng tròn K5 xét số cuối — sổ "Lỗi cần sửa", seed 31983 PO4).
        //   Lỗi: K5 chỉ xét số cuối → nhận 1 → WTS NG 1 → sửa → KCS đạt (hàng về ô Hàng lỗi đã xử lý) rồi HUỶ LẦN NHẬN GỐC vẫn lưu được:
        //   chiếc sửa đạt "thế chỗ" chính chiếc đã bị NG (thực tế không thể — chiếc NG chỉ có sau khi sửa xong chính nó). Số cuối vẫn cân
        //   nhưng sau đó huỷ KCS xác nhận bị chặn mà không gỡ được (WTS NG giữ vụ bị Kết quả sửa dùng, Kết quả sửa có KCS xác nhận).
        //   Cùng loại: gửi nhóm khác sửa rồi trả về (phiếu Hàng lỗi / Hàng sửa → nhận ô Hàng lỗi đã xử lý / Hàng mới).
        //   Cách tính: PO có hàng sửa trả về thì dựng thêm ẢNH SỔ NGAY TRƯỚC mỗi lúc hàng sửa về (bỏ mọi chứng từ ghi từ lúc đó trở đi,
        //   theo giờ MÁY CHỦ lưu; phôi Kho giữ nguyên) rồi xét các ô chưa làm (Kho: phôi còn giao). Ô nào thấp hơn số cuối và âm → khoá "TL|<ô>".
        //   CHỈ XÉT KHI HUỶ chứng từ nguồn (huỷ lần nhận / huỷ phiếu / huỷ phần chưa nhận / huỷ KCS xác nhận — HandoverService) và trong [GN-WHY]:
        //   so với mức nền = min(TL trước, số ô trước) → chỉ chặn khi lần huỷ làm ô âm thêm theo thời gian (NewLoopKeys).
        //   KHÔNG đưa vào NegativeCells / Compute (rà độc lập 05/11 C1–C2, V3): sửa dòng WTS (huỷ + dòng mới, giờ lưu mới) đẩy phần OK ra sau
        //   → ảnh sổ âm giả; lưu bù dữ liệu cũ đang âm bị chặn oan; Compute chậm ở mọi trang. Lưu mới / sửa WTS không xét phần này.
        //   WTS: phần OK tính từ giờ lưu dòng; phần NG có vụ tính từ lúc vụ sinh. PO không có hàng sửa trả về: không tính thêm.
        //   Hở đã vá 05/11: xoá dòng WTS NC trước làm NC sau mất nguồn theo vòng → [K5-VONG-HO-WTS] (WtsSaveService xét như huỷ chứng từ nguồn);
        //   sửa dòng WTS che phần OK có từ trước → [WTS-SUA-GIO-GOC] (dòng sửa mang giờ lưu gốc). Lần nhận ghi muộn: không phải hở —
        //   chứng từ dùng hàng chỉ lưu được sau lần nhận cấp hàng (trần L3 / L4 lúc lưu) nên ảnh sổ vẫn khớp ([K5-VONG-HO-NHAN], sua S25).
        //   Nguồn: CLAUDE (lỗi ngầm kiem 04/11 21:59 seed 31983); Hùng 05/11 10:28 OK coi là sửa ruột, không cần hỏi khách.
        // =====================================================================
        // [K5-VONG-XL 2026-11-10] Rà độc lập Gói 15 (Trung T1): hàng sửa đạt kiểu mới (@XONG / @OK ở NC cuối / phiếu từ ô Hàng lỗi đã xử lý về nơi
        //   phát hiện) vào thẳng "Đã làm chưa giao" — ô này trước chỉ xét cho Kho, lần nhận phiếu từ ô XL không thành mốc → xoá dòng WTS / huỷ chứng từ
        //   nguồn để chiếc sửa "thế chỗ" chính nó vẫn lưu được (cùng loại K5-VONG-SUA). Nay: phiếu từ ô XL là mốc + xét ô Đã làm chưa giao mọi nhóm.
        //   Nguồn: CLAUDE (rà độc lập 10/11, ca RV1 / RV2 / RV9); Hùng 05/11 10:28 OK K5 theo thời gian là sửa ruột.
        /// <summary>[K5-VONG-SUA] Lần nhận này là hàng sửa trả về (phiếu lấy từ ô Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý, nhận vào ô Hàng lỗi đã xử lý / Hàng mới)?</summary>
        public static bool IsRepairedReturn(InTx t, InRecv r)
        {
            var pu = Classify(t, null);
            if (pu is HandoverPurpose.NgReturn or HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn || HandoverPurpose.IsOut(pu) || t.SourceReceiveId != null) return false;
            var from = HandoverCell.IssueCellOf(pu, t.FromCell);
            if (from is not (HandoverCell.Loi or HandoverCell.Sua or HandoverCell.Xl)) return false;   // [K5-VONG-XL 2026-11-10] + hàng đã sửa (phiếu từ ô Hàng lỗi đã xử lý)
            var cell = HandoverCell.IsReceiveCell(r.Cell) ? r.Cell : DesigCellOf(t, pu);
            return cell is (HandoverCell.Xl or HandoverCell.Moi) && r.Ok > 0;
        }

        /// <summary>[K5-VONG-SUA] Các ô "chưa làm" xét theo thời gian (khoá như NegativeCells), kể cả ô không âm.</summary>
        static Dictionary<string, (decimal Value, string Text)> WaitCells(LedgerRow r)
        {
            var d = new Dictionary<string, (decimal Value, string Text)>();
            foreach (var G in r.Groups.Values)
            {
                if (G.IsKho) { d["KHO|ConGiao"] = (G.ConGiao, "Kho: phôi còn giao"); continue; }
                d[G.Code + "|ChuaLam"] = (G.ChuaLam, $"{G.Label}: Chưa làm");
                d[G.Code + "|ConGiao"] = (G.ConGiao, $"{G.Label}: Đã làm chưa giao");   // [K5-VONG-XL] hàng sửa đạt từ 10/11 về thẳng Đã làm (@XONG / @OK NC cuối)
                foreach (var st in G.Steps.Where(st => st.Waiting.HasValue && !st.IsExcluded && !st.IsPrep))
                    d[$"{G.Code}|{st.Pg}|{st.Code}"] = (st.Waiting!.Value, st.Pg == "GC"
                        ? (IsVao(st.Code) ? $"{G.Label} {VaoLabel}" : $"{G.Label} {NcName(st.Code)}: chưa làm")
                        : $"{G.Label}{(st.SubGroup == G.Label || string.IsNullOrEmpty(st.SubGroup) ? "" : " " + st.SubGroup)} mã {st.Code}: chưa làm");
            }
            return d;
        }

        // =====================================================================
        // [WTS-SUA-GIO-GOC 2026-11-05] Sửa dòng WTS = huỷ dòng cũ ("Sửa lại") + dòng mới CreatedAt mới. Ảnh sổ theo thời gian
        //   (SnapshotBefore) đọc giờ lưu → phần OK của dòng đã sửa bị coi như ghi SAU lúc hàng sửa trả về (che OK có từ trước):
        //   NC đó thấy chưa làm cao hơn thật, NC sau thấy đầu vào thấp hơn thật → ảnh sổ sai cả 2 chiều.
        //   Nay: dòng mới ghép với dòng cũ nó thay (cùng PO, người, nhóm, NC / mã CV; dòng cũ huỷ "Sửa lại" lúc VoidedAt, dòng mới
        //   lưu CreatedAt = ĐÚNG giờ đó — WtsSaveService [WTS-SUA-GIO-GOC]) và nhận giờ lưu GỐC (đi ngược cả chuỗi sửa nhiều lần).
        //   Sửa đổi NC / mã / PO: không ghép (tính như dòng mới — phía chặt). Không đổi Compute / số cuối: chỉ đổi giờ cắt ảnh sổ.
        //   [WTS-SUA-GIO-GOC-EXACT 2026-11-05] Rà độc lập lần 1 (Cao): ghép "lệch ≤ 3 giây" cho dữ liệu cũ ghép nhầm cả dữ liệu mới
        //   (sửa dòng A đổi PO + thêm dòng C cùng NC trong 1 lần lưu → C nhận giờ gốc của A → xoá dòng nguồn khác lọt K5 theo thời gian).
        //   Nay CHỈ ghép đúng giờ; dòng sửa từ trước bản này (lệch vài ms) không ghép = như master trước gói (không lùi), dọn lúc bản trắng.
        //   Nhóm theo khoá (Dictionary) thay vì so mọi cặp (rà độc lập Vừa 2: 50k dòng ~9 s); trùng giờ (cột datetime làm tròn) → ghép
        //   dòng cũ Id tăng dần với dòng mới Id tăng dần (rà độc lập Thấp 5).
        //   Còn mở (Thấp, ghi sổ): sửa TĂNG số OK thì phần tăng cũng mang giờ gốc (dễ dãi phần tăng; WTS chỉ sửa trong ngày).
        //   Nguồn: CLAUDE (hở A4 ghi ở [K5-VONG-SUA], rà độc lập 05/11); Hùng 05/11 10:28 OK K5 theo thời gian là sửa ruột.
        // =====================================================================
        public const string WtsEditVoidReason = "Sửa lại";   // chữ VoidReason WtsSaveService ghi khi sửa dòng
        /// <summary>[TARO-NC-MA 2026-11-06] Khoá mã của dòng WTS khi ghép dòng sửa: mã CV + NC Taro (nếu có) — đổi NC Taro lúc sửa = đổi khoá.</summary>
        public static string? WtsEditCode(string? wtsCode, string? stepNc)
            => string.IsNullOrWhiteSpace(stepNc) ? wtsCode : (wtsCode ?? "").Trim() + "@" + stepNc.Trim();   // [WTS-MA-TRIM] mã dòng cũ "3T " + NC03 ghép được với dòng sửa "3T"
        public record WtsEditNode(long Id, int Det, int Worker, string Pg, string? Nc, string? Code, DateTime Created, DateTime? VoidedAt,
                                  decimal Ok = 0, decimal Ng = 0);   // [WTS-SUA-PHAN] số OK / NG của dòng (để tính phần có từ giờ gốc)
        /// <summary>[WTS-SUA-PHAN] Dòng sửa: giờ lưu gốc + các bản của cả chuỗi sửa (cũ → mới, bản cuối = chính dòng này).</summary>
        public record WtsEditOrigin(DateTime Origin, IReadOnlyList<WtsVer> Vers);

        /// <summary>[WTS-SUA-GIO-GOC] Id dòng WTS → giờ máy chủ lưu GỐC (chỉ dòng là bản sửa của dòng khác; dòng gốc không có trong kết quả).</summary>
        public static Dictionary<long, DateTime> EditOrigins(IEnumerable<WtsEditNode> active, IEnumerable<WtsEditNode> editedVoided)
            => EditOriginsFull(active, editedVoided).ToDictionary(kv => kv.Key, kv => kv.Value.Origin);

        /// <summary>[WTS-SUA-PHAN] Như EditOrigins, kèm các bản của cả chuỗi sửa (giờ lưu, OK, NG).
        /// Rà độc lập lần 2 (Cao): sửa TĂNG số (vd OK 1 → 2, chiếc thứ 2 nhận sau lúc hàng sửa về) mà cả dòng mang giờ gốc → chặn oan.
        /// Rà độc lập lần 3 (2 Cao): gán "min qua cả chuỗi" vào 1 mốc gốc → chặn oan (bản tăng lưu TRƯỚC lúc hàng sửa về bị bỏ) và lọt (đổi NG → OK
        /// làm tổng lúc gốc nhỏ đi). Nay SnapshotBefore lấy bản đang có hiệu lực ở từng mốc (xem đó).</summary>
        public static Dictionary<long, WtsEditOrigin> EditOriginsFull(IEnumerable<WtsEditNode> active, IEnumerable<WtsEditNode> editedVoided)
        {
            static string K(string? s) => (s ?? "").Trim().ToUpperInvariant();
            static string Key(WtsEditNode n, DateTime at) => $"{n.Det}|{n.Worker}|{K(n.Pg)}|{K(n.Nc)}|{K(n.Code)}|{at.Ticks}";
            var olds = editedVoided.Where(v => v.VoidedAt != null).ToList();
            var origin = new Dictionary<long, WtsEditOrigin>();
            if (olds.Count == 0) return origin;
            // [WTS-SUA-GIO-GOC-EXACT] dòng cũ theo (khoá, giờ huỷ) — mỗi dòng cũ chỉ được 1 dòng mới nhận
            var bucket = olds.GroupBy(o => Key(o, o.VoidedAt!.Value))
                             .ToDictionary(g => g.Key, g => new Queue<WtsEditNode>(g.OrderBy(o => o.Id)));
            var pred = new Dictionary<long, WtsEditNode>();
            var activeList = active.ToList();
            foreach (var n in activeList.Concat(olds).OrderBy(n => n.Created).ThenBy(n => n.Id))
            {
                if (!bucket.TryGetValue(Key(n, n.Created), out var q)) continue;
                while (q.Count > 0 && (q.Peek().Id == n.Id || q.Peek().Created >= n.Created)) q.Dequeue();   // không tự ghép / không ghép ngược giờ
                if (q.Count == 0) continue;
                pred[n.Id] = q.Dequeue();
            }
            foreach (var n in activeList)
            {
                if (!pred.TryGetValue(n.Id, out var p)) continue;
                var chain = new List<WtsEditNode> { n, p };
                var cur = p;
                for (int guard = 0; guard < 50 && pred.TryGetValue(cur.Id, out var pp); guard++) { cur = pp; chain.Add(cur); }
                chain.Reverse();   // cũ → mới
                origin[n.Id] = new WtsEditOrigin(cur.Created, chain.Select(c => new WtsVer(c.Created, c.Ok, c.Ng)).ToList());
            }
            return origin;
        }

        /// <summary>
        /// [K5-CLOSE-NHIEU 2026-11-06] Danh sách lần huỷ phần chưa nhận của 1 phiếu từ bảng HandoverTxCloses + bộ cột cũ (QtyClosed / ClosedAt).
        /// Σ dòng = QtyClosed → đúng các dòng. Σ dòng &lt; QtyClosed (huỷ làm giữa lúc chạy SQL 10 và lúc code mới lên — chỉ bộ cột cũ có) →
        /// thêm 1 dòng bù (Id 0) phần chênh, giờ = ClosedAt cũ (không trễ hơn lần huỷ đầu ghi bằng code mới — phần chênh làm trước code mới).
        /// Σ dòng &gt; QtyClosed (không thể khi code ghi đúng — QuetSo báo CLOSE_LECH) → trả rỗng: ClosesOf tính như cũ (1 lần lúc ClosedAt).
        /// </summary>
        public static List<InClose> CloseRows(decimal closedCol, DateTime? closedAtCol, DateTime issuedAt,
                                              IEnumerable<(long Id, DateTime At, decimal Qty, bool Migrated)> rows)
        {
            if (closedCol <= 0) return new List<InClose>();
            var list = rows.OrderBy(r => r.At).ThenBy(r => r.Id).ToList();
            var sum = list.Sum(r => r.Qty);
            if (sum > closedCol) return new List<InClose>();
            var res = list.Select(r => new InClose(r.Id, r.At, r.Qty)).ToList();
            if (sum < closedCol)
            {
                var at = closedAtCol ?? issuedAt;
                var firstNew = list.Where(r => !r.Migrated).Select(r => (DateTime?)r.At).FirstOrDefault();
                if (firstNew is DateTime fn && fn < at) at = fn;
                var lastMig = list.Where(r => r.Migrated).Select(r => (DateTime?)r.At).LastOrDefault();
                if (lastMig is DateTime lm && at < lm) at = lm;
                res.Add(new InClose(0, at, closedCol - sum));
                res = res.OrderBy(r => r.At).ThenBy(r => r.Id == 0 ? 0 : 1).ToList();   // trùng giờ: dòng bù (làm trước code mới) đứng trước
            }
            return res;
        }

        /// <summary>[K5-CLOSE-NHIEU] Các lần huỷ phần chưa nhận của phiếu (tăng dần). Closes rỗng / lệch Closed → 1 lần (Closed, lúc ClosedAt) như trước SQL 10.</summary>
        public static List<InClose> ClosesOf(InTx t)
        {
            if (t.Closed <= 0) return new List<InClose>();
            if (t.Closes.Count > 0 && t.Closes.Sum(c => c.Qty) == t.Closed) return t.Closes.OrderBy(c => c.At).ToList();
            return new List<InClose> { new InClose(0, t.ClosedAt ?? t.At, t.Closed) };
        }

        /// <summary>[K5-VONG-SUA] Ảnh sổ ngay trước lúc <paramref name="cut"/>: chỉ giữ chứng từ ghi TRƯỚC lúc đó (giờ máy chủ).</summary>
        public static LedgerInput SnapshotBefore(LedgerInput i, DateTime cut)
        {
            DateTime CaseAt(long? id, DateTime fallback) => id is long v && v > 0 && i.CaseInfo.TryGetValue(v, out var ci) ? ci.At : fallback;
            var txs = new List<InTx>();
            var dropRecv = new HashSet<long>();
            foreach (var t in i.Txs.Where(t => t.At < cut))
            {
                var c = t.CloneTx();
                var nDrop = c.Receives.Count(r => r.At >= cut);
                foreach (var r in c.Receives.Where(r => r.At >= cut)) dropRecv.Add(r.Id);
                c.Receives.RemoveAll(r => r.At >= cut);
                // [K5-CLOSE-NHIEU 2026-11-06] mở lại ĐÚNG các lần huỷ phần chưa nhận làm từ lúc cut (mỗi lần 1 dòng HandoverTxCloses).
                //   Trước: 1 ClosedAt cho cả số đã huỷ → huỷ lần 2 kéo luôn số huỷ lần 1 về "còn giao" ở các mốc cũ → chặn oan (fuzz seed 602).
                var cls = ClosesOf(c);
                var reo = cls.Where(x => x.At >= cut).Sum(x => x.Qty);
                bool reopened = reo > 0;
                if (reopened)   // huỷ phần chưa nhận sau lúc đó: lúc đó phiếu còn mở phần này
                {
                    c.Issued += reo; c.Closed -= reo;
                    c.Closes = cls.Where(x => x.At < cut).ToList();
                    c.ClosedAt = c.Closes.Count > 0 ? c.Closes[^1].At : null;
                }
                if ((nDrop > 0 || reopened) && !HandoverPurpose.IsOut(c.Purpose))   // (rà độc lập T3) phiếu không đổi gì thì giữ trạng thái gốc (dữ liệu cũ)
                {
                    var rec = c.Receives.Sum(r => r.Ok + r.Ng);
                    c.Status = rec <= 0 ? "PENDING" : rec >= c.Issued ? "COMPLETED" : "PARTIAL";
                }
                txs.Add(c);
            }
            txs.RemoveAll(t => t.SourceReceiveId is long src && dropRecv.Contains(src));   // phiếu trả tự lập của lần nhận bị bỏ
            var wts = new List<InWts>();
            foreach (var w in i.Wts)
            {
                var saved = w.Saved ?? DateTime.MaxValue;
                // [WTS-SUA-PHAN] dòng sửa: bản đang có hiệu lực lúc cut (bản cuối lưu trước cut); số tính = phần còn đúng qua mọi bản sửa SAU đó
                //   (OK = min OK, đã làm = min (OK + NG) từ bản đó tới bản mới nhất) — bản tăng lưu trước mốc được tính (rà độc lập lần 3 Cao 1),
                //   đổi NG ↔ OK không làm tổng lúc đó nhỏ đi (lần 3 Cao 2), phần tăng sau mốc không tính (lần 2 Cao). Chưa có bản nào → như dòng thường.
                if (saved < cut && w.Vers is { Count: > 0 } vers)
                {
                    int vi = -1;
                    for (int k = 0; k < vers.Count; k++) if (vers[k].At < cut) vi = k;
                    if (vi >= 0)
                    {
                        decimal okm = decimal.MaxValue, totm = decimal.MaxValue;
                        for (int k = vi; k < vers.Count; k++) { okm = Math.Min(okm, vers[k].Ok); totm = Math.Min(totm, vers[k].Ok + vers[k].Ng); }
                        wts.Add(w with { Ok = okm, Ng = Math.Max(0, totm - okm) });
                        continue;
                    }
                }
                if (saved < cut) { wts.Add(w); continue; }
                if (w.Ng > 0 && w.NgCaseId is long vid && vid > 0 && CaseAt(vid, saved) < cut) wts.Add(w with { Ok = 0 });   // phần NG có từ lúc vụ sinh
            }
            var reps = new List<InRepair>();
            foreach (var rp in i.Repairs.Where(rp => rp.At < cut))
            {
                var c = rp.CloneRep(); c.Checks.RemoveAll(x => x.At >= cut); reps.Add(c);
            }
            return new LedgerInput
            {
                KhPlanDetailId = i.KhPlanDetailId, PlanNo = i.PlanNo, PurchaseOrder = i.PurchaseOrder, PartNo = i.PartNo,
                CustomerName = i.CustomerName, PlanQty = i.PlanQty, ApplyWorkflow = i.ApplyWorkflow,
                Phoi = i.Phoi, GcSteps = i.GcSteps, CodeSteps = i.CodeSteps, CaseInfo = i.CaseInfo, TaroGops = i.TaroGops,   // [TARO-3T-GOP]
                Bu = i.Bu.Where(b => b.At < cut).ToList(),   // [KHACH-GUI-LAI 2026-11-11] phôi bù có giờ máy chủ: chỉ tính phiếu bù lưu trước mốc
                Txs = txs, Wts = wts, NgReps = i.NgReps.Where(b => b.At < cut).ToList(), Repairs = reps,
            };
        }

        /// <summary>[K5-VONG-SUA] Các lúc hàng sửa trả về + nhóm nhận hàng sửa (lần nhận hàng sửa trả về: nhóm nhận; lần KCS xác nhận đạt: nhóm sửa), tăng dần.</summary>
        public static List<(DateTime At, string Group)> RepairedReturnTimes(LedgerInput i)
            => i.Txs.SelectMany(t => t.Receives.Where(r => IsRepairedReturn(t, r)).Select(r => (r.At, t.To)))
                .Concat(i.Repairs.SelectMany(rp => rp.Checks.Where(c => c.Ok > 0).Select(c => (c.At, rp.Group))))
                .Distinct().OrderBy(x => x.Item1).ToList();

        /// <summary>[K5-VONG-SUA] Ô chưa làm (khoá như NegativeCells) thấp nhất trong các ảnh sổ ngay trước lúc hàng sửa trả về — chỉ ô âm.</summary>
        public static Dictionary<string, (decimal Value, string Text)> LoopTimeline(LedgerInput inp)
        {
            var res = new Dictionary<string, (decimal Value, string Text)>();
            if (!inp.ApplyWorkflow) return res;   // N7: Workflow OFF không chặn
            // (rà độc lập 05/11 V2) mỗi mốc chỉ xét ô của NHÓM NHẬN hàng sửa về lúc đó — nhóm khác không bị xét thứ tự thời gian
            foreach (var g in RepairedReturnTimes(inp).GroupBy(x => x.At))
            {
                var groups = g.Select(x => x.Group).ToHashSet();
                foreach (var kv in WaitCells(Compute(SnapshotBefore(inp, g.Key))))
                    if (kv.Value.Value < 0 && groups.Contains(kv.Key.Split('|')[0]) && (!res.TryGetValue(kv.Key, out var cur) || kv.Value.Value < cur.Value))
                        res[kv.Key] = kv.Value;
            }
            return res;
        }

        /// <summary>[K5-VONG-SUA] Khoá "TL|&lt;ô&gt;" mới âm / âm thêm theo thời gian do thao tác (trước → sau). Mức nền = min(TL trước, số ô trước):
        /// dữ liệu cũ đã âm không chặn oan (rà độc lập 05/11 C2), vòng tròn (số trước 0, TL sau −1) vẫn chặn.</summary>
        public static List<(string Key, decimal Value, string Text)> NewLoopKeys(LedgerInput beforeIn, LedgerRow beforeRow, LedgerInput afterIn,
            Dictionary<string, (decimal Value, string Text)>? beforeTl = null)   // beforeTl: LoopTimeline(beforeIn) đã tính sẵn ([GN-WHY] dùng lại — rà độc lập V4)
        {
            var res = new List<(string, decimal, string)>();
            if (!afterIn.ApplyWorkflow) return res;
            var a = LoopTimeline(afterIn);
            if (a.Count == 0) return res;
            var b = beforeTl ?? LoopTimeline(beforeIn);
            var bw = WaitCells(beforeRow);
            foreach (var kv in a)
            {
                var basev = bw.TryGetValue(kv.Key, out var bv) ? bv.Value : 0m;
                if (b.TryGetValue(kv.Key, out var bt) && bt.Value < basev) basev = bt.Value;
                if (kv.Value.Value < Math.Min(basev, 0m))
                    res.Add(("TL|" + kv.Key, kv.Value.Value, kv.Value.Text + " (theo thứ tự thời gian: hàng đã được làm / giao trước lúc hàng sửa trả về — chiếc sửa không thế chỗ được chiếc đã dùng trước đó)"));
            }
            return res;
        }

        // =====================================================================
        // [TARO-NC-MA 2026-11-06] Taro: 1 mã CV dùng ở NHIỀU NC của PO (vd 1T / 2T / 3T1 / 3T2 / 3T3 / 3T ở cả NC 3 lẫn NC 5).
        //   Trước: sổ đếm theo mã → 2 dòng quy trình cùng mã đều nhận MỌI dòng WTS mã đó (NG trừ 2 lần, không biết còn bao nhiêu chiếc chưa
        //   làm NC 5), T.Chuẩn luôn lấy dòng đầu. Nay: mã dùng ở nhiều NC đếm theo cặp (mã, NC) — khoá sổ TaroKey(mã, NC) = "3T1 (NC 3)";
        //   WTS mã đó ghi kèm NC (WtsProductionLogs.StepNC, form WTS bắt chọn). Mã chỉ ở 1 NC, mã chuẩn bị (1T…): giữ khoá = mã như cũ.
        //   Dòng WTS mã dùng ở nhiều NC mà thiếu / sai NC (dữ liệu cũ — QuetSo 06/11: 0 dòng): tính vào NC đầu tiên chứa mã + cảnh báo đỏ.
        //   Nguồn: KHÁCH 02/11 09:14 (anh Ngọc chọn B: giữ mã chung, form WTS Taro thêm ô NC); HÙNG 05/11 15:37 OK; HÙNG 06/11 21:45 OK
        //   (dòng cũ không NC: không chép bù, lọt thì tính NC đầu + cảnh báo).
        // =====================================================================
        public static string TaroKey(string code, string nc) => $"{code.Trim()} (NC {nc.Trim()})";

        static bool SameNc(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        // =====================================================================
        // [TARO-LAMHO-NC 2026-11-07] TARO LÀM HỘ Ở NC MÁY LOẠI TRỪ GIỮA CHUỖI GIA CÔNG
        //   Nguồn: CLAUDE (thử live 07/11, PO TEST-0711-TARO) + KHÁCH (qua Hùng 07/11 10:03, trả lời (a)) + HÙNG 07/11 10:18 "ok, theo đề xuất đi".
        //   Lỗi (thử live 07/11, PO TEST-0711-TARO, Part 3B-PT1268610): Taro NC 3 làm trên máy H01 (loại trừ) giữa NC 2 và NC 4 → Gia công giao
        //   theo NC cho HTSP, HTSP làm Taro rồi trả về NC 4. Hàng làm hộ không vào 4 số HTSP → mã Taro NC 3 "vào 0", WTS Taro NC 3 bị chặn;
        //   sau NC 4 Gia công giao Hàng mới cho HTSP (Taro NC 5 = H01 cuối chuỗi) thì sổ bắt nhập lại Taro NC 3 (đếm 2 lần).
        //   Luật mới (Hùng 07/11 10:18 "ok, theo đề xuất đi"; khách xác nhận (a)):
        //     - Mã Taro ở NC loại trừ mà phía sau còn NC Gia công thường = "Taro làm hộ": vào = HTSP nhận hàng Gia công giao theo NC từ NC
        //       thường ngay trước (phần OK lúc nhận); WTS Taro (mã, NC đó) trừ vào đó (có bộ gộp 3T); KHÔNG tính vào OK nhỏ nhất / Chưa làm / NG
        //       của HTSP (4 số HTSP chỉ là hàng HTSP nhận để làm phần của mình).
        //     - Câu 1: trả hàng làm hộ (hàng tốt) về NC sau đoạn không vượt OK bộ Taro đoạn đó (Workflow ON — ô K5 "HTSP|LamHoTaro|<NC>").
        //     - Câu 2: WTS Taro làm hộ NG → vụ lỗi do HTSP gây; chiếc hỏng vẫn nằm trong Tồn hàng giao theo NC (vụ giữ ô LAMHO) tới khi trả
        //       Gia công vào ô Hàng lỗi (phiếu Trả về Gia công theo NC, FromCell = LOI, mang vụ — sổ ghi "Lỗi trả lại" glLamHo).
        //     - NC loại trừ CUỐI chuỗi (sau nó không còn NC thường): như cũ — Gia công giao Hàng mới, HTSP làm như mã thường.
        //   Không SQL (dùng cột có sẵn). Nguồn: CLAUDE (thử live 07/11) + KHÁCH (qua Hùng 07/11 10:03) + HÙNG 07/11 10:18.
        // =====================================================================
        /// <summary>[LAMHO-WTS-DSPO 2026-11-07] Nhóm đã có hàng vào để ghi WTS sản xuất: Nhận sản xuất (Hàng mới + Hàng lỗi đã xử lý + Làm hộ trả về)
        ///   hoặc — HTSP — hàng Gia công giao theo NC cho đoạn Taro làm hộ (không vào 4 số HTSP). Trang WTS dùng để lọc danh sách PO
        ///   (trước: PO chỉ có Taro làm hộ không hiện cho HTSP, phải bật "Hiện phiếu chưa nhận hàng" — thử live 07/11 PO TEST-0711-TARO).
        ///   Nguồn: CLAUDE (thử live 07/11 22:4x); Hùng 07/11 22:38 giao Gói 8.</summary>
        public static bool HasWtsInput(LedgerRow row, string group)
            => row.G(group).NhanSanXuat > 0 || (group == "HTSP" && row.LamHoRuns.Any(r => r.HasTaro && r.In > 0));

        /// <summary>[TARO-LAMHO-NC] HTSP chỉ có mã Taro làm hộ (không mã chính nào của HTSP) — rà độc lập gói 6 mục 4.</summary>
        public static bool HtspOnlyLamHo(LedgerRow row)
        {
            var m = row.G("HTSP").Steps.Where(s => !s.IsPrep).ToList();
            return m.Count > 0 && m.All(s => s.LamHoNc != null);
        }

        /// <summary>[TARO-LAMHO-NC] So NC giữa bảng Gia công và bảng Taro (nhập tay ở Part Master): bỏ chữ "NC", khoảng trắng, số 0 đầu.</summary>
        public static string NcKey(string? nc)
        {
            var s = (nc ?? "").Trim();
            if (s.StartsWith("NC", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            s = s.Replace(" ", "");
            var k = s.TrimStart('0');
            if (k.Length == 0 && s.Length > 0) k = "0";   // [LAMHO-NCKEY 2026-11-08] "NC0" / "00" giữ "0" (trước thành chuỗi rỗng). Nguồn: CLAUDE (sổ LAMHO-NCKEY).
            return k.ToUpperInvariant();
        }

        /// <summary>[LAMHO-NCKEY 2026-11-08] Khoá NC (NcKey) mà 2 NC Gia công KHÁC CHỮ cùng mang (vd "3" và "NC003"): NC Taro mang khoá đó không biết khớp NC nào
        ///   → không coi là Taro làm hộ (rơi về cách cũ: tính như mã HTSP thường) + cảnh báo trên sổ. Trước: lấy NC gặp đầu tiên (GroupBy.First) — có thể gán nhầm.
        ///   Nguồn: CLAUDE (sổ LAMHO-NCKEY); Hùng 08/11 16:37 OK (không cảnh báo ở trang Part Master — chỉ sổ PO + quét).</summary>
        public static HashSet<string> AmbiguousNcKeys(LedgerInput inp)
            => inp.GcSteps.Where(s => !s.IsBackup && !string.IsNullOrWhiteSpace(s.NC))
                  .GroupBy(s => NcKey(s.NC))
                  .Where(g => g.Select(s => s.NC.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                  .Select(g => g.Key).ToHashSet();
        /// <summary>[LAMHO-NCKEY] Đầu câu cảnh báo NC Taro khớp nhiều NC Gia công (QuetSo tách riêng loại TARO_NC_LECH theo đầu câu này).</summary>
        public const string TaroNcLechHead = "Taro ở NC khớp nhiều NC Gia công";

        /// <summary>[TARO-LAMHO-NC] Các đoạn NC máy loại trừ liền nhau mà sau đó còn NC thường (đoạn đầu chuỗi: NC nguồn = @VAO).</summary>
        public static List<LedgerLamHoRun> LamHoRunsOf(LedgerInput inp)
        {
            var all = inp.GcSteps.Where(s => !s.IsBackup && !string.IsNullOrWhiteSpace(s.NC)).OrderBy(s => s.StepOrder).ToList();
            var res = new List<LedgerLamHoRun>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].IsExcluded) continue;
                int j = i;
                while (j + 1 < all.Count && all[j + 1].IsExcluded) j++;
                if (j + 1 < all.Count)
                {
                    var ncs = all.Skip(i).Take(j - i + 1).Select(s => s.NC.Trim()).ToList();
                    res.Add(new LedgerLamHoRun { Key = ncs[0], Ncs = ncs, FromNc = i == 0 ? VaoCode : all[i - 1].NC.Trim(), NextNc = all[j + 1].NC.Trim() });
                }
                i = j;
            }
            // chỉ mã Taro CHÍNH (không mã chuẩn bị, không dự phòng) — đoạn chỉ có 1T không có OK bộ, câu 1 sẽ kẹt (rà độc lập gói 6 mục 3)
            var amb = AmbiguousNcKeys(inp);   // [LAMHO-NCKEY] khoá NC trùng giữa 2 NC Gia công khác chữ → không ghép
            var taroNcs = inp.CodeSteps.Where(s => s.Pg == "TARO" && !s.IsBackup && !IsPrepCode(s.RawCode ?? s.Code) && !string.IsNullOrWhiteSpace(s.StepNc)).Select(s => NcKey(s.StepNc)).Where(k => !amb.Contains(k)).ToHashSet();
            foreach (var r in res) r.HasTaro = r.Ncs.Any(n => taroNcs.Contains(NcKey(n)));
            return res;
        }

        /// <summary>[TARO-LAMHO-NC] Mã Taro (khoá sổ, gồm dòng dự phòng cũ) ở NC của 1 đoạn làm hộ → NC đó (đúng chữ NC Gia công).</summary>
        static Dictionary<string, string> LamHoCodes(LedgerInput inp, List<LedgerLamHoRun> runs)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (runs.Count == 0) return d;
            var amb = AmbiguousNcKeys(inp);   // [LAMHO-NCKEY] khoá trùng → mã Taro tính như HTSP thường (cách cũ), không lấy NC gặp đầu
            var ncOf = runs.SelectMany(r => r.Ncs).GroupBy(NcKey).Where(g => !amb.Contains(g.Key)).ToDictionary(g => g.Key, g => g.First());
            foreach (var s in inp.CodeSteps.Where(s => s.Pg == "TARO" && !string.IsNullOrWhiteSpace(s.Code) && !string.IsNullOrWhiteSpace(s.StepNc)))
                if (ncOf.TryGetValue(NcKey(s.StepNc), out var nc)) d[s.Code.Trim()] = nc;
            return d;
        }

        /// <summary>[TARO-LAMHO-NC] Đoạn làm hộ của phiếu Gia công giao theo NC từ <paramref name="fromNc"/> (phiếu cũ không ghi NC nguồn: đoạn duy nhất nếu có).</summary>
        static LedgerLamHoRun? LamHoRunFrom(List<LedgerLamHoRun> runs, string? fromNc)
            => string.IsNullOrWhiteSpace(fromNc) ? (runs.Count == 1 ? runs[0] : null)   // phiếu cũ không NC nguồn: chỉ gán khi PO có đúng 1 đoạn (rà vòng 2 L4)
               : runs.FirstOrDefault(r => string.Equals(r.FromNc, fromNc.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>[TARO-NC-MA] Mã CV Taro dùng ở nhiều NC của PO → danh sách NC (thứ tự quy trình). Không gồm mã chuẩn bị.</summary>
        public static Dictionary<string, List<string>> TaroSharedNcs(IEnumerable<InCodeStep> codeSteps, bool prep = false)   // [TARO-NC-MA] prep = true: chỉ mã chuẩn bị
            => codeSteps.Where(x => x.Pg == "TARO" && !x.IsBackup && IsPrepCode(x.RawCode ?? x.Code) == prep && !string.IsNullOrWhiteSpace(x.RawCode ?? x.Code))
                .GroupBy(x => (x.RawCode ?? x.Code).Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, Ncs: g.OrderBy(x => x.StepOrder).Select(x => (x.StepNc ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
                .Where(g => g.Ncs.Count > 1)
                .ToDictionary(g => g.Key, g => g.Ncs, StringComparer.OrdinalIgnoreCase);

        /// <summary>[TARO-NC-MA] Bản đầu vào đã đổi mã Taro dùng ở nhiều NC sang khoá (mã, NC). Ghi danh sách mã → NC vào row.TaroNcs.</summary>
        public static LedgerInput TaroKeyed(LedgerInput inp, LedgerRow? row = null)
        {
            var shared = TaroSharedNcs(inp.CodeSteps);
            if (row != null) { row.TaroNcs = shared; row.TaroPrepNcs = TaroSharedNcs(inp.CodeSteps, prep: true); }   // [TARO-NC-MA] mã chuẩn bị: chỉ để chọn NC lấy T.Chuẩn
            if (inp.TaroKeyedDone || shared.Count == 0) return inp;
            var o = inp.ShallowCopy();
            o.TaroKeyedDone = true;
            o.TaroWarnings = new List<string>();
            o.CodeSteps = inp.CodeSteps.Select(x =>
            {
                if (x.Pg != "TARO") return x;
                var raw = (x.RawCode ?? x.Code).Trim();
                var nc  = (x.StepNc ?? "").Trim();
                var y = x;
                if (shared.ContainsKey(raw)) y = y with { Code = TaroKey(raw, nc), RawCode = raw };
                if (x.IsBackup && !string.IsNullOrWhiteSpace(x.ParentCode) && shared.ContainsKey(x.ParentCode!.Trim()))
                    y = y with { ParentCode = TaroKey(x.ParentCode!.Trim(), nc) };   // dự phòng gộp về đúng dòng cha cùng NC (N2)
                return y;
            }).ToList();
            o.Wts = inp.Wts.Select(w =>
            {
                if (w.ProcessGroup != "TARO") return w;
                var raw = (w.WtsCode ?? w.NC ?? "").Trim();
                if (!shared.TryGetValue(raw, out var ncs)) return w;
                var nc = ncs.FirstOrDefault(n => SameNc(n, w.StepNC));
                if (nc == null)
                {
                    nc = ncs[0];
                    o.TaroWarnings.Add($"HTSP: dòng WTS #{w.Id} Taro mã {raw} {(string.IsNullOrWhiteSpace(w.StepNC) ? "không ghi NC" : $"ghi NC {w.StepNC} không có mã này")} "
                                     + $"(mã dùng ở NC {string.Join(", ", ncs)}) — đang tính vào NC {nc}; sửa dòng WTS chọn đúng NC (TARO-NC-MA)");
                }
                return w with { WtsCode = TaroKey(raw, nc) };
            }).ToList();
            return o;
        }

        /// <summary>[TARO-NC-MA] Khoá sổ của 1 dòng WTS (nhóm, mã, NC Taro) — dùng gọi WtsCap / LedgerQtyText.Step. Mã Taro dùng ở nhiều NC:
        /// NC phải là 1 NC có mã đó (sai / thiếu → null). Nhóm khác / mã 1 NC: trả mã như cũ.</summary>
        public static string? WtsKey(LedgerRow row, string pg, string? code, string? stepNc)
        {
            if (pg != "TARO" || string.IsNullOrWhiteSpace(code)) return code;
            var raw = code.Trim();
            if (!row.TaroNcs.TryGetValue(raw, out var ncs)) return code;
            var nc = ncs.FirstOrDefault(n => SameNc(n, stepNc));
            return nc == null ? null : TaroKey(raw, nc);
        }

        /// <summary>[TARO-NC-MA] NC của mã Taro dùng ở nhiều NC (null = mã chỉ ở 1 NC / không phải Taro).</summary>
        public static List<string>? TaroNcsOf(LedgerRow? row, string pg, string? code)
            => row != null && pg == "TARO" && !string.IsNullOrWhiteSpace(code) && row.TaroNcs.TryGetValue(code.Trim(), out var ncs) ? ncs : null;

        /// <summary>[TARO-NC-MA] NC phải chọn trên WTS: mã đếm số dùng ở nhiều NC (TaroNcsOf) hoặc mã chuẩn bị dùng ở nhiều NC (chỉ để lấy T.Chuẩn).
        /// Rà độc lập 06/11 mục 4: trước, 1T ở NC 3 + NC 5 làm ở NC 5 vẫn ghi T.Chuẩn dòng NC 3.</summary>
        public static List<string>? TaroNcChoices(LedgerRow? row, string pg, string? code)
            => TaroNcsOf(row, pg, code)
               ?? (row != null && pg == "TARO" && !string.IsNullOrWhiteSpace(code) && row.TaroPrepNcs.TryGetValue(code.Trim(), out var pn) ? pn : null);

        static bool NeedTrim(string? s) => s != null && (s.Length == 0 || char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]));
        static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        /// <summary>[WTS-MA-TRIM] Mã / NC từ trang: bỏ khoảng trắng đầu cuối (trống → null) — như sổ khi đọc DB.</summary>
        public static string? TrimCode(string? s) => TrimOrNull(s);

        /// <summary>[WTS-MA-TRIM] Mọi mã / NC đọc từ DB vào sổ bỏ khoảng trắng đầu cuối — CẢ HAI PHÍA của phép ghép: quy trình (snapshot NC, mã CV,
        /// dòng cha, NC Taro), phiếu (NC giao / nhận, NC lần nhận), báo lỗi, Kết quả sửa, NC phát hiện của vụ. Rà độc lập Gói 7 (Cao): chỉ Trim dòng WTS
        /// thì snapshot "NC2 " (Part Master không Trim NC) + dòng WTS cũ "NC2 " (trước khớp thô) nay lệch → OK rơi khỏi sổ. Mã chỉ còn chữ thật nên
        /// mọi so sánh (đã không phân biệt hoa thường) khớp như WtsCap / kiểm mã của máy chủ (vốn so bản đã Trim).</summary>
        public static void TrimInputCodes(LedgerInput inp)
        {
            if (inp.GcSteps.Any(s => NeedTrim(s.NC) || NeedTrim(s.ParentNC)))
                inp.GcSteps = inp.GcSteps.Select(s => s with { NC = s.NC.Trim(), ParentNC = TrimOrNull(s.ParentNC) }).ToList();
            if (inp.CodeSteps.Any(s => NeedTrim(s.Code) || NeedTrim(s.ParentCode) || NeedTrim(s.StepNc) || NeedTrim(s.RawCode)))
                inp.CodeSteps = inp.CodeSteps.Select(s => s with { Code = s.Code.Trim(), ParentCode = TrimOrNull(s.ParentCode), StepNc = TrimOrNull(s.StepNc), RawCode = TrimOrNull(s.RawCode) }).ToList();
            foreach (var t in inp.Txs)
            {
                if (NeedTrim(t.FromNC)) t.FromNC = TrimOrNull(t.FromNC);
                if (NeedTrim(t.ToNC)) t.ToNC = TrimOrNull(t.ToNC);
                if (t.Receives.Any(r => NeedTrim(r.ToNC))) t.Receives = t.Receives.Select(r => r with { ToNC = TrimOrNull(r.ToNC) }).ToList();
            }
            if (inp.NgReps.Any(b => NeedTrim(b.NC))) inp.NgReps = inp.NgReps.Select(b => b with { NC = TrimOrNull(b.NC) }).ToList();
            foreach (var x in inp.Repairs) if (NeedTrim(x.ToNC)) x.ToNC = TrimOrNull(x.ToNC);
            foreach (var k in inp.CaseInfo.Where(kv => NeedTrim(kv.Value.DetectNc)).Select(kv => kv.Key).ToList())
                inp.CaseInfo[k] = inp.CaseInfo[k] with { DetectNc = TrimOrNull(inp.CaseInfo[k].DetectNc) };
        }

        public static LedgerRow Compute(LedgerInput inp)
        {
            // [WTS-MA-TRIM] mã CV / NC / NC Taro của dòng WTS bỏ khoảng trắng đầu cuối trước khi ghép với quy trình
            //   (dòng cũ lưu "K1 " trước khi WtsSaveService chuẩn hoá) — WtsCap / kiểm mã cũng so bản đã Trim.
            if (inp.Wts.Any(w => NeedTrim(w.NC) || NeedTrim(w.WtsCode) || NeedTrim(w.StepNC)))
                inp.Wts = inp.Wts.Select(w => w with { NC = TrimOrNull(w.NC), WtsCode = TrimOrNull(w.WtsCode), StepNC = TrimOrNull(w.StepNC) }).ToList();
            var row = new LedgerRow
            {
                KhPlanDetailId = inp.KhPlanDetailId, PlanNo = inp.PlanNo, PurchaseOrder = inp.PurchaseOrder,
                PartNo = inp.PartNo, CustomerName = inp.CustomerName, PlanQty = inp.PlanQty,
                ApplyWorkflow = inp.ApplyWorkflow,
            };
            foreach (var c in GroupCodes)
                row.Groups[c] = new LedgerGroup { Code = c, Label = GroupLabel(c) };

            // [TARO-NC-MA 2026-11-06] Mã CV Taro dùng ở nhiều NC → đếm theo cặp (mã, NC): đổi mã dòng quy trình + dòng WTS sang khoá TaroKey
            //   (bản sao, không đụng đầu vào gốc). Mã chỉ ở 1 NC: y như cũ.
            inp = TaroKeyed(inp, row);
            foreach (var w in inp.TaroWarnings) row.G("HTSP").Warnings.Add(w);

            // ── Chuỗi NC gia công (N1, N2, N3) ─────────────────────────
            var gcChain = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).ToList();
            var gcLastNc = gcChain.LastOrDefault()?.NC;

            // [TARO-LAMHO-NC] Đoạn NC máy loại trừ giữa chuỗi + mã Taro làm hộ ở đó (khoá sổ mã → NC đoạn)
            row.LamHoRuns = LamHoRunsOf(inp);
            var lamHoCode = LamHoCodes(inp, row.LamHoRuns);

            // [GN-WTS-CAP] Bảng mã WTS → mã chính (N2). Mã chính trỏ về chính nó.
            foreach (var s in inp.GcSteps.Where(s => !string.IsNullOrWhiteSpace(s.NC)))
                row.CodeMain["GC|" + s.NC.Trim()] = s.IsBackup && !string.IsNullOrWhiteSpace(s.ParentNC) ? s.ParentNC!.Trim() : s.NC.Trim();
            foreach (var s in inp.CodeSteps.Where(s => !string.IsNullOrWhiteSpace(s.Code)))
                row.CodeMain[s.Pg + "|" + s.Code.Trim()] = s.IsBackup && !string.IsNullOrWhiteSpace(s.ParentCode) ? s.ParentCode!.Trim() : s.Code.Trim();

            // Bộ gom số hạng có kèm chứng từ
            var acc = new Dictionary<(string g, string key), LedgerTerm>();
            // [GN-WTS-CAP] NG bên nhận đếm lúc nhận (nằm trong ô "Trả hàng lỗi" của bên nhận) — chưa từng vào sản xuất,
            // tách riêng khỏi phiếu Trả hàng lỗi (N10: phát hiện sau khi nhận, chiếc đó có thể đã qua vài NC / mã CV).
            var recvNgSx = GroupCodes.ToDictionary(g => g, g => 0m);
            LedgerTerm T(string g, string key, string label)
            {
                if (!acc.TryGetValue((g, key), out var t)) acc[(g, key)] = t = new LedgerTerm { Label = label };
                return t;
            }
            void Add(string g, string key, string label, decimal qty, LedgerDoc doc)
            {
                if (qty == 0) return;
                var t = T(g, key, label);
                t.Value += qty;
                doc.Qty = qty;
                t.Docs.Add(doc);
            }

            // ── Phôi Kho ────────────────────────────────────────────────
            foreach (var p in inp.Phoi)
            {
                row.Phoi += p.Qty;
                Add("KHO", "nm", "Phôi Kho nhận", p.Qty, new LedgerDoc
                {
                    Kind = "PHOI", RefId = p.Id, At = p.At,
                    Text = $"Phiếu Kho vật liệu #{p.Id} · phôi nhận thực tế" + (string.IsNullOrEmpty(p.Note) ? "" : $" · {p.Note}")
                });
            }
            // ── [KHACH-GUI-LAI 2026-11-11] Phôi khách gửi bù → Kho Nhận · Hàng lỗi đã xử lý (phôi còn), cân PO: phôi + bù ──
            //   Nguồn: KHÁCH 10/11 vòng 1 câu 1a A; HÙNG 11/11 14:39 duyệt. Khoá riêng "nbu" (cộng vào NhanLoiXl của Kho), không lẫn hàng đã sửa trả về ("nx").
            foreach (var bu0 in inp.Bu)
            {
                row.Bu += bu0.Qty;
                Add("KHO", "nbu", "Khách gửi bù", bu0.Qty, new LedgerDoc
                {
                    Kind = "BU", RefId = bu0.Id, At = bu0.At,
                    Text = BuText(bu0)
                });
            }

            // ── Phiếu giao / nhận ──────────────────────────────────────
            // [GN-CELL] Phiếu Trả hàng đã sửa cũ (FromCell NULL): giao từ ô Hàng sửa nếu lúc đó nhóm còn hàng nhờ sửa, không thì từ ô Hàng lỗi (tự sửa).
            var suaRunning = GroupCodes.ToDictionary(g => g, g => 0m);
            // [GN-RECV-NG] Master list mục 38: phiếu trả hàng lỗi phần mềm tự lập khi bên nhận đếm NG lúc nhận.
            // Không đi như phiếu thường — tính ngay tại lần nhận gốc (bên dưới), để số NG lúc nhận vẫn ghi đúng ô như cũ.
            var autoBySrc = new Dictionary<long, InTx>();
            foreach (var a in inp.Txs.Where(x => x.SourceReceiveId != null && !HandoverPurpose.IsOut(x.Purpose)))
                autoBySrc.TryAdd(a.SourceReceiveId!.Value, a);
            var recvIds = inp.Txs.SelectMany(x => x.Receives).Select(r => r.Id).ToHashSet();
            var orphanAuto = new List<InTx>();
            var fromDaCs = FromDaCases(inp);   // [SUA-XL-GIU] vụ báo lỗi lấy từ "đã làm xong chưa giao"

            // ── [VULOI 2026-10-28] Sổ theo từng vụ lỗi — chạy song song, đúng các chứng từ góp vào ô Tồn hàng lỗi / Tồn hàng sửa ──
            //   Vụ = 1 lần phát sinh lỗi; khoá 0 = hàng lỗi cũ chưa có mã. Không đổi bất kỳ ô nào của sổ hiện có.
            LedgerCase Cs(long? id)
            {
                var k = id ?? 0;
                if (!row.Cases.TryGetValue(k, out var cs)) row.Cases[k] = cs = new LedgerCase { Id = k };
                return cs;
            }
            void CAdd(Dictionary<string, decimal> d, string g, decimal q) { if (q != 0) d[g] = (d.TryGetValue(g, out var v) ? v : 0m) + q; }
            void CHold(LedgerCase cs, string g, string cell, decimal q, DateTime? at)   // [VL8-TIMELINE] + lúc chứng từ
            {
                CAdd(cs.Held, g + "|" + cell, q);
                // [VL8-TIMELINE 29/10] Hàng lỗi cũ chưa có mã vụ (Id 0) KHÔNG xét thứ tự thời gian: NG của nó có thể đến từ WTS cũ
                //   mà giờ là giờ kết thúc công nhân tự nhập (có thể sau giờ lưu thật) → xét thời gian sẽ chặn oan dữ liệu cũ (rà độc lập 29/10).
                if (q != 0 && cs.Id != 0) cs.Moves.Add((at ?? DateTime.MinValue, g + "|" + cell, q));
            }
            // [VL8-TIMELINE] WTS có vụ: lúc vụ sinh ra (NgCases.CreatedAt, giờ máy chủ) — sửa dòng WTS (huỷ + dòng mới giữ vụ) không đẩy vụ ra sau phiếu đã lấy nó.
            //   [29/10] Không có lúc sinh vụ (dòng đang nhập trên form, vụ chưa lưu) → coi là sớm nhất: không bao giờ dùng giờ kết thúc WTS người nhập.
            DateTime WtsCaseAt(InWts w) => w.NgCaseId is long wid && inp.CaseInfo.TryGetValue(wid, out var wci) ? wci.At : DateTime.MinValue;
            void CHop(LedgerCase cs, string kind, string g, string? other, string? cell, decimal q, string doc, long refId, DateTime? at)
            {
                if (q != 0) cs.Hops.Add(new LedgerCaseHop { Kind = kind, Group = g, Other = other, Cell = cell, Qty = q, Doc = doc, RefId = refId, At = at });
            }

            // [GN-CELL2] Bên nhận ghi vào 1 trong 4 ô (Excel): Hàng mới / Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý
            void AddRecvCell(string g, string cell, decimal qty, LedgerDoc doc)
            {
                switch (cell)
                {
                    case HandoverCell.Loi: Add(g, "nl", "Nhận Hàng lỗi", qty, doc); break;
                    case HandoverCell.Sua: Add(g, "ns", "Nhận Hàng sửa", qty, doc); break;
                    case HandoverCell.Xl:  Add(g, "nx", "Nhận Hàng lỗi đã xử lý", qty, doc); break;
                    default:               Add(g, "nm", "Nhận Hàng mới", qty, doc); break;
                }
            }

            // ---- NG đếm thấy lúc nhận: trả bên giao (L2) ----
            // Bên nhận: Giao Hàng lỗi ngay lúc lưu lần nhận; bên giao: Nhận Hàng lỗi (về ô lỗi đang giữ).
            // [GN-RECV-NG] Master list mục 38: lần nhận có phiếu trả tự lập → bên giao chỉ có Nhận · Hàng lỗi khi đã bấm Nhận phiếu đó,
            // phần chưa nhận nằm ở ô Chờ nhận của bên giao. Lần nhận cũ (trước bản này, không có phiếu trả) giữ cách cũ: vào thẳng Nhận · Hàng lỗi.
            // [GN-CELL2] tách thành hàm dùng chung (phiếu theo ô và phiếu theo NC); bucket = ô bên nhận trả lại từ đó.
            void NgBack(InTx t, string purpose, InRecv r, string bucket, bool skipReceiver, LedgerCase ngCs)   // [VULOI] ngCs = vụ của phần NG
            {
                autoBySrc.TryGetValue(r.Id, out var back);
                var lbl = bucket switch
                {
                    "glSuaNg" => "Giao Hàng lỗi (trả lại hàng nhờ sửa bị NG lúc nhận)",
                    "glLamHo" => "Giao Hàng lỗi (trả lại hàng giao theo NC)",
                    _         => "Giao Hàng lỗi (trả nơi gây lỗi)"
                };
                var ngText = TxText(t, purpose) + $" · bên nhận đếm NG {r.Ng:0.##}" + (back == null ? ", trả ngay bên giao" : $" → phiếu trả #{back.Id}");
                // Bên nhận (thành phẩm Kho đếm NG: Kho không giữ thành phẩm nên không ghi ô của Kho)
                if (!skipReceiver)
                {
                    Add(t.To, bucket, lbl, r.Ng, new LedgerDoc { Kind = "NHAN", RefId = t.Id, At = r.At, Text = ngText });
                    if (bucket == "glTra") recvNgSx[t.To] += r.Ng; // [GN-WTS-CAP]
                }
                // Bên giao (thành phẩm: t.From = Đóng gói)
                if (back == null)
                {
                    Add(t.From, "nl", "Nhận Hàng lỗi", r.Ng, new LedgerDoc { Kind = "NHAN", RefId = t.Id, At = r.At, Text = ngText });
                    CHold(ngCs, t.From, HandoverCell.Loi, r.Ng, r.At); CAdd(ngCs.NhanLoi, t.From, r.Ng);   // [VULOI]
                    CHop(ngCs, "TRA", t.From, t.To, HandoverCell.Loi, r.Ng, $"Phiếu #{t.Id}", t.Id, r.At);
                }
                else
                {
                    var backRec = back.Receives.Sum(x => x.Ok + x.Ng);
                    var backText = $"Phiếu #{back.Id} · {GroupLabel(back.From)} → {GroupLabel(back.To)} · trả NG lúc nhận phiếu #{t.Id}"
                                   + (string.IsNullOrEmpty(back.NgReason) ? "" : $" · lỗi: {back.NgReason}");
                    Add(t.From, "nl", "Nhận Hàng lỗi", backRec, new LedgerDoc
                    {
                        Kind = "NHAN", RefId = back.Id, At = back.Receives.Select(x => (DateTime?)x.At).Max(), Text = backText
                    });
                    if (back.Status != "COMPLETED")
                        Add(t.From, "cn", "Chờ nhận", Math.Max(0, back.Issued - backRec), new LedgerDoc
                        {
                            Kind = "PHIEU", RefId = back.Id, At = back.At, Text = backText + $" · chờ nhận (phiếu {back.Issued:0.##})"
                        });
                    // [VULOI] vụ của phần NG: đi theo phiếu trả tự lập — đã nhận vào ô Hàng lỗi bên giao, phần chưa nhận đang chuyển
                    CAdd(ngCs.Held, t.From + "|" + HandoverCell.Loi, backRec); CAdd(ngCs.NhanLoi, t.From, backRec);
                    foreach (var bx in back.Receives) if (bx.Ok + bx.Ng != 0) ngCs.Moves.Add((bx.At, t.From + "|" + HandoverCell.Loi, bx.Ok + bx.Ng));   // [VL8-TIMELINE] từng lần nhận phiếu trả
                    if (back.Status != "COMPLETED") ngCs.Transit += Math.Max(0, back.Issued - backRec);
                    CHop(ngCs, "TRA", t.From, t.To, HandoverCell.Loi, r.Ng, $"Phiếu trả #{back.Id}", back.Id, back.At);
                }
            }

            foreach (var t in inp.Txs.OrderBy(x => x.At))
            {
                // [GN-NG-OUT] Phiếu Huỷ / Trả khách: chỉ có bên giao, hàng lỗi đang giữ ra khỏi PO ngay khi lập phiếu.
                if (HandoverPurpose.IsOut(t.Purpose))
                {
                    if (!row.Groups.ContainsKey(t.From)) continue;
                    var isScrap = t.Purpose == HandoverPurpose.Scrap;
                    Add(t.From, isScrap ? "glHuy" : "glKhach", isScrap ? "Huỷ hàng lỗi" : "Trả khách hàng lỗi", t.Issued,
                        new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At,
                            Text = $"Phiếu #{t.Id} · {GroupLabel(t.From)} → {GroupLabel(t.To)} · {HandoverPurpose.Label(t.Purpose)}"
                                   + (string.IsNullOrEmpty(t.NgReason) ? "" : $" · lỗi: {t.NgReason}")
                                   + (string.IsNullOrEmpty(t.Notes) ? "" : $" · {t.Notes}") });
                    if (isScrap) row.DaHuy += t.Issued; else row.TraKhach += t.Issued;
                    row.TxFromCell[t.Id] = HandoverCell.Loi;   // [GN-NG-TRACE] chỉ ghi lại, không tính
                    {   // [VULOI] vụ ra khỏi PO từ ô Hàng lỗi bên giao
                        var csO = Cs(t.NgCaseId);
                        CHold(csO, t.From, HandoverCell.Loi, -t.Issued, t.At);
                        if (isScrap) { csO.Huy += t.Issued; CAdd(csO.HuyBy, t.From, t.Issued); }
                        else { csO.TraKhach += t.Issued; CAdd(csO.KhachBy, t.From, t.Issued); }
                        CHop(csO, isScrap ? "HUY" : "KHACH", t.From, null, HandoverCell.Loi, t.Issued, $"Phiếu #{t.Id}", t.Id, t.At);
                    }
                    continue;
                }
                if (!row.Groups.ContainsKey(t.From) || !row.Groups.ContainsKey(t.To)) continue;
                // [GN-RECV-NG] Phiếu trả tự lập: số tính ở lần nhận gốc; ở đây chỉ đếm badge chờ nhận
                if (t.SourceReceiveId != null)
                {
                    row.TxFromCell[t.Id] = HandoverCell.Loi;   // [GN-NG-TRACE] phiếu trả tự lập = hàng lỗi (chỉ ghi lại)
                    if (!recvIds.Contains(t.SourceReceiveId.Value)) orphanAuto.Add(t);
                    else if (t.Status != "COMPLETED") { row.G(t.From).PendingToIssue++; row.G(t.To).PendingToReceive++; }
                    continue;
                }
                var purpose = Classify(t, gcLastNc);
                var giao    = GiaoQty(t);
                var recOk   = t.Receives.Sum(r => r.Ok);
                var recNg   = t.Receives.Sum(r => r.Ng);
                var rec     = recOk + recNg;
                var txDoc   = new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At, Text = TxText(t, purpose) };
                LedgerDoc RecDoc(string extra) => new LedgerDoc
                {
                    Kind = "NHAN", RefId = t.Id, At = t.Receives.Select(r => (DateTime?)r.At).Max(),
                    Text = TxText(t, purpose) + extra
                };

                // ── [GN-CELL2] Ô hai đầu của phiếu: bên giao lấy từ ô nào (FromCell) và chỉ định bên nhận vào ô nào (ToCell).
                //    Phiếu cũ (cột NULL): suy từ loại phiếu như trước. Phiếu theo NC (làm hộ) nằm ngoài 8 ô Excel → không có ô.
                bool isHelp = purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn;
                bool lhLoi  = HandoverCell.IsLamHoLoi(purpose, t.FromCell);   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ (mang vụ) về Gia công ô Hàng lỗi
                LedgerCase? csL = lhLoi ? Cs(t.NgCaseId) : null;
                string? fromCell = isHelp ? null
                    : t.FromCell ?? (purpose == HandoverPurpose.RepairReturn
                        ? (suaRunning[t.From] >= giao && giao > 0 ? HandoverCell.Sua : HandoverCell.Loi)   // [GN-CELL] phiếu Trả hàng đã sửa cũ
                        : HandoverCell.IssueCellOf(purpose, null));
                bool prevNg = purpose == HandoverPurpose.NgReturn;
                row.TxFromCell[t.Id] = lhLoi ? HandoverCell.Loi : fromCell;   // [GN-NG-TRACE] chỉ ghi lại ô đã dùng, không đổi phép tính · [TARO-LAMHO-NC] trả lỗi hàng làm hộ = hàng lỗi
                string? desig = isHelp ? null : DesigCellOf(t, purpose);
                // Thành phẩm: Đóng gói → Kho hàng mới — không vào phôi Kho. [GN-CELL2] chỉ phần Kho nhận vào ô Hàng mới mới là thành phẩm.
                bool isTpTx = t.From == "PKG" && t.To == "KHO" && fromCell == HandoverCell.Moi;
                // [VULOI] Phiếu lấy từ ô Hàng lỗi / Hàng sửa = phiếu MANG VỤ (1 phiếu = 1 vụ). Phiếu Hàng mới / theo NC: không mang vụ.
                bool carry = !isHelp && fromCell is HandoverCell.Loi or HandoverCell.Sua or HandoverCell.Xl;   // [SUA-XL-GIU] + hàng đã sửa (ô Hàng lỗi đã xử lý) mang vụ
                LedgerCase? csT = carry ? Cs(t.NgCaseId) : null;
                string txDocName = $"Phiếu #{t.Id}";

                // ── [KHO-TARO-THANG 2026-11-12] KHO GIAO THẲNG TARO (NC đầu Gia công là máy loại trừ có Taro — đoạn làm hộ đầu chuỗi, NC nguồn @VAO) ──
                //   Nguồn: KHÁCH 10/11 (hỏi lại 3.3 chọn B) + 12/11 file Word (b = A: NC đầu Taro thì Kho chỉ giao Taro); HÙNG 12/11 14:00 OK kế hoạch.
                //   Phiếu = Giao theo NC do KHO lập (Purpose HELP_SEND, From KHO, FromNC @VAO, FromCell MOI). Cân (L6), mỗi chứng từ cộng trừ = 0:
                //     lập phiếu      : Kho Giao · Hàng mới + số giao (phôi còn giảm); phần chưa nhận ở Chờ nhận của HTSP (Tồn bên nhận — mục 37)
                //     mỗi lần nhận   : Gia công Nhận · Hàng mới + số nhận VÀ Giao theo NC từ "Nhận vào (chưa qua NC)" + số nhận (dòng @VAO vào = giao, chờ 0
                //                      — bảng Giao nhận vẫn tính Gia công đã nhận, khách 12/11 không phản đối); HTSP Nhận theo NC + số nhận; đoạn @VAO vào += phần tốt
                //     NG lúc nhận    : phôi lỗi — trả THẲNG Kho (phiếu trả tự lập HTSP → Kho, như mọi NG lúc nhận); vụ KHÔNG là vụ làm hộ (Kho sửa / trả khách / huỷ)
                //   Trước: không có đường này (Kho chỉ giao Gia công "Nhận vào", Gia công giao theo NC cho HTSP — N3-FIRST).
                if (IsKhoTaro(t, purpose))
                {
                    row.TxFromCell[t.Id] = HandoverCell.Moi;   // [GN-NG-TRACE] chỉ ghi lại: Kho lấy từ ô phôi còn
                    if (t.Status != "COMPLETED")
                    {
                        Add(t.To, "cn", "Chờ nhận", Math.Max(0, giao - rec), new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At, Text = TxText(t, purpose) });
                        row.G(t.From).PendingToIssue++;
                        row.G(t.To).PendingToReceive++;
                    }
                    Add(t.From, "gm", "Giao Hàng mới", giao, txDoc);
                    if (rec > 0)
                    {
                        var vNote = " · Gia công tính đã nhận vào và giao theo NC ngay (Kho giao thẳng Taro)";
                        Add("GC", "nm", "Nhận Hàng mới", rec, RecDoc(vNote));
                        Add("GC", "ghSend", "Giao theo NC (máy loại trừ)", rec, RecDoc(vNote));
                        Add("GC", "helpOut:" + VaoCode, "Giao theo NC từ " + VaoLabel, rec, RecDoc(vNote));
                        Add(t.To, "nhSend", "Nhận hàng Gia công giao theo NC", rec, RecDoc(""));
                        if (LamHoRunFrom(row.LamHoRuns, VaoCode) is { } rk) rk.In += recOk;   // [TARO-LAMHO-NC] vào đoạn = phần tốt lúc nhận
                    }
                    foreach (var r in t.Receives.Where(x => x.Ng > 0).OrderBy(x => x.At))
                    {
                        var csK = Cs(r.NgCaseId);   // phôi lỗi đếm lúc Taro nhận: vụ mới ở HTSP, trả Kho (không gắn LamHoNhan — không phải chiếc của chuỗi Gia công)
                        csK.SinhRa += r.Ng; csK.HelpFromNc ??= VaoCode;   // sửa đạt về Gia công: như hàng mới (chưa qua NC nào)
                        CHop(csK, "PS", t.To, t.From, null, r.Ng, txDocName, t.Id, r.At);
                        NgBack(t, purpose, r, "glLamHo", false, csK);
                    }
                    continue;
                }

                if (t.Status != "COMPLETED")
                {
                    // [GN-RECV-NG] Master list mục 37: phần chưa nhận tính vào Tồn BÊN NHẬN (ô Chờ nhận), không còn nằm ngoài ở dòng Đang giao.
                    // Riêng thành phẩm Đóng gói → Kho: thành phẩm không vào Tồn Kho → vẫn để dòng "Thành phẩm chờ Kho nhận".
                    var cho = Math.Max(0, giao - rec);
                    if (isTpTx && desig == HandoverCell.Moi) row.DangGiao += cho;
                    else Add(t.To, "cn", "Chờ nhận", cho, new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At, Text = TxText(t, purpose) });
                    row.G(t.From).PendingToIssue++;
                    row.G(t.To).PendingToReceive++;
                }

                // ---- Bên giao ---- [GN-CELL2] theo Ô LẤY HÀNG (mỗi ô có trần riêng), không phụ thuộc ô bên nhận chọn
                if (isHelp)
                {
                    if (purpose == HandoverPurpose.HelpSend)
                    {
                        Add(t.From, "ghSend", "Giao theo NC (máy loại trừ)", giao, txDoc);
                        if (t.From == "GC" && !string.IsNullOrEmpty(t.FromNC))
                            Add("GC", "helpOut:" + t.FromNC, IsVao(t.FromNC) ? "Giao theo NC từ " + VaoLabel : "Giao theo NC " + t.FromNC, giao, txDoc);   // [N3-FIRST] chữ @VAO (NC thường giữ chữ cũ)
                    }
                    else if (lhLoi)
                    {   // [TARO-LAMHO-NC] chiếc làm hộ bị lỗi (vụ đang giữ ô LAMHO) rời Tồn hàng giao theo NC → đang chuyển về Gia công
                        Add(t.From, "glLamHo", "Giao Hàng lỗi (trả lỗi hàng làm hộ về Gia công)", giao, txDoc);
                        CHold(csL!, t.From, HandoverCell.LamHo, -giao, t.At); csL!.Transit += giao;
                        CHop(csL, "GIAO", t.From, t.To, HandoverCell.LamHo, giao, txDocName, t.Id, t.At);
                    }
                    else
                    {
                        Add(t.From, "ghRet", "Trả về Gia công theo NC", giao, txDoc);
                        // [TARO-LAMHO-NC] HTSP trả hàng làm hộ (hàng tốt) vào NC thường ngay sau đoạn máy loại trừ → câu 1 (đã trả ≤ OK bộ Taro đoạn)
                        if (t.From == "HTSP" && row.LamHoRunTo(t.ToNC) is { } rr) { rr.Ret += giao; rr.RetDocs.Add(txDoc); }   // mọi đoạn (kể cả không Taro) — số dư từng đoạn
                    }
                }
                else switch (fromCell)
                {
                    case HandoverCell.Moi:
                        Add(t.From, "gm", "Giao Hàng mới", giao, txDoc);
                        break;
                    case HandoverCell.Loi:
                        if (prevNg)
                        {
                            // [KTT-PHOILOI 2026-11-12] Taro phát hiện phôi lỗi khi đang làm (hàng đoạn đầu chuỗi chưa xong bộ Taro) → trả thẳng Kho ô Hàng lỗi.
                            //   Lấy từ Tồn hàng giao theo NC (không phải hàng của HTSP — K4): không trừ Chưa làm / NG đầu HTSP; đoạn @VAO TraKho += (chưa làm Taro giảm).
                            //   Nguồn: KHÁCH 12/11 (3.3 a = A); HÙNG 12/11 14:00 (chỉ trả Kho).
                            if (IsTaroPhoiLoi(t, purpose))
                            {
                                Add(t.From, "glLhKho", "Giao Hàng lỗi (Taro trả phôi lỗi về Kho — hàng giao theo NC)", giao, txDoc);
                                if (LamHoRunFrom(row.LamHoRuns, VaoCode) is { } rpk) rpk.TraKho += giao;
                                csT!.HelpFromNc ??= VaoCode;   // sửa đạt về Gia công: như hàng mới (chưa qua NC nào — NC đầu là Taro)
                            }
                            else
                            Add(t.From, "glTra", "Giao Hàng lỗi (trả nơi gây lỗi)", giao, txDoc);
                            // [VULOI] Hàng lỗi công đoạn trước: vụ SINH RA cùng phiếu (lấy từ hàng chưa làm), đang chuyển tới nơi gây lỗi
                            csT!.SinhRa += giao; csT.Transit += giao;
                            CHop(csT, "PS", t.From, t.To, HandoverCell.Loi, giao, txDocName, t.Id, t.At);
                            // [GN-NC-PIN] Gia công: phiếu mới chốt NC phát hiện lỗi (FromNC) → trừ đúng NC đó, không trôi theo dữ liệu sau (để K5 bắt được)
                            if (t.From == "GC" && !string.IsNullOrEmpty(t.FromNC))
                                Add("GC", "rrNc:" + t.FromNC, "Trả hàng lỗi phát hiện ở " + NcText(t.FromNC), giao,   // [N3-FIRST] chữ @VAO
                                    new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At, Text = TxText(t, purpose) });
                        }
                        else if (desig == HandoverCell.Sua) Add(t.From, "glGui", "Giao Hàng lỗi (gửi sửa)", giao, txDoc);
                        else if (desig == HandoverCell.Loi) Add(t.From, "glChuyen", "Giao Hàng lỗi (chuyển sang ô Hàng lỗi nhóm khác)", giao, txDoc);   // [GN-CELL2]
                        else Add(t.From, "glTu", "Giao Hàng lỗi (tự sửa xong, trả nơi phát hiện)", giao, txDoc);
                        if (!prevNg)
                        {   // [VULOI] vụ rời ô Hàng lỗi bên giao, đang chuyển
                            CHold(csT!, t.From, HandoverCell.Loi, -giao, t.At); csT!.Transit += giao;
                            if (desig is HandoverCell.Xl or HandoverCell.Moi) csT.TuXuLy.Add(t.From);
                            CHop(csT, "GIAO", t.From, t.To, HandoverCell.Loi, giao, txDocName, t.Id, t.At);
                        }
                        break;
                    case HandoverCell.Xl:   // [SUA-XL-GIU] hàng đã sửa (KCS xác nhận) rời ô Tồn hàng lỗi đã xử lý, đang chuyển (cùng vụ)
                        Add(t.From, "gx", "Giao Hàng lỗi đã xử lý (hàng đã sửa, trả tiếp nơi phát hiện)", giao, txDoc);
                        CHold(csT!, t.From, HandoverCell.Xl, -giao, t.At); csT!.Transit += giao;
                        CHop(csT, "GIAO", t.From, t.To, HandoverCell.Xl, giao, txDocName, t.Id, t.At);
                        break;
                    case HandoverCell.Sua:
                        Add(t.From, "gs", "Giao Hàng sửa (sửa hộ xong, trả nơi phát hiện)", giao, txDoc);
                        suaRunning[t.From] -= giao;
                        // [VULOI] vụ rời ô Hàng sửa bên giao, đang chuyển
                        CHold(csT!, t.From, HandoverCell.Sua, -giao, t.At); csT!.Transit += giao;
                        CHop(csT, "GIAO", t.From, t.To, HandoverCell.Sua, giao, txDocName, t.Id, t.At);
                        break;
                }

                // ---- Bên nhận ---- [GN-CELL2] từng lần nhận, vào ô bên nhận THỰC CHỌN (mặc định ô bên giao chỉ định)
                if (lhLoi)
                {   // [TARO-LAMHO-NC] Gia công nhận chiếc làm hộ bị lỗi vào ô Hàng lỗi (cả số nhận — hàng đã là hàng lỗi, không đếm NG lúc nhận)
                    if (rec > 0) Add(t.To, "nl", "Nhận Hàng lỗi (hàng làm hộ bị lỗi trả về)", rec, RecDoc(""));
                    foreach (var r in t.Receives.OrderBy(x => x.At))
                    {
                        var q = r.Ok + r.Ng;
                        if (q == 0) continue;
                        csL!.Transit -= q; CHold(csL, t.To, HandoverCell.Loi, q, r.At); CAdd(csL.NhanLoi, t.To, q);
                        CHop(csL, "NHAN", t.To, t.From, HandoverCell.Loi, q, txDocName, t.Id, r.At);
                    }
                }
                else if (isHelp)
                {
                    if (rec > 0)
                    {
                        if (purpose == HandoverPurpose.HelpSend)
                        {
                            Add(t.To, "nhSend", "Nhận hàng Gia công giao theo NC", rec, RecDoc(""));
                            // [TARO-LAMHO-NC] vào của mã Taro làm hộ = phần OK lúc nhận (NG đếm lúc nhận trả ngay Gia công)
                            if (t.To == "HTSP" && LamHoRunFrom(row.LamHoRuns, t.FromNC) is { } rin) rin.In += recOk;
                        }
                        else
                        {
                            Add(t.To, "nhRet", "Nhận vào NC (hàng trả về)", rec, RecDoc(""));
                            if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC)) Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, recOk, RecDoc("")); // [GN-WTS-CAP] chỉ phần OK — NG lúc nhận trả ngay, không vào NC đích
                        }
                    }
                    foreach (var r in t.Receives.Where(x => x.Ng > 0).OrderBy(x => x.At))
                    {
                        var csH = Cs(r.NgCaseId);   // [VULOI] NG đếm lúc nhận hàng theo NC: vụ mới sinh ở bên nhận
                        if (purpose == HandoverPurpose.HelpSend && !string.IsNullOrEmpty(t.FromNC)) csH.HelpFromNc ??= t.FromNC;   // [GN-FORM1] chỉ ghi lại, không tính
                        if (r.NgCaseId is long nid && nid > 0) csH.LamHoNhan = true;   // [LAMHO-NHAN-NG 2026-11-08] chiếc của chuỗi Gia công — chỉ Gia công sửa (không đổi phép tính số nào);
                                                                                        //   vụ 0 (hàng lỗi cũ chưa mã, gom chung) không gắn cờ — rà độc lập Gói 11 L1
                        csH.SinhRa += r.Ng;
                        CHop(csH, "PS", t.To, t.From, null, r.Ng, txDocName, t.Id, r.At);
                        NgBack(t, purpose, r, purpose == HandoverPurpose.HelpSend ? "glLamHo" : "glTra", false, csH);
                    }
                }
                else foreach (var r in t.Receives.OrderBy(x => x.At))
                {
                    var cell = HandoverCell.IsReceiveCell(r.Cell) ? r.Cell! : (desig ?? HandoverCell.Moi);
                    var rDoc = new LedgerDoc
                    {
                        Kind = "NHAN", RefId = t.Id, At = r.At,
                        Text = TxText(t, purpose) + $" · nhận {r.Ok + r.Ng:0.##} vào ô {HandoverCell.Label(cell)}" + CellChangeNote(desig, r)
                    };
                    bool tpHere = isTpTx && cell == HandoverCell.Moi;
                    // [SUA-XL-GIU] hàng đã sửa (phiếu từ ô Hàng lỗi đã xử lý) nhận vào ô Hàng lỗi đã xử lý: giữ ô / đã làm / như cũ (XlDest)
                    var xd = carry && fromCell == HandoverCell.Xl && cell == HandoverCell.Xl && t.NgCaseId is long xid && inp.CaseInfo.TryGetValue(xid, out var xci)
                        ? XlDest(t.To, xci.DetectGroup, fromDaCs.Contains(xid)) : "";
                    bool gcBackDone = false;   // [SUA-HO-TRA-NGUOC] lần nhận đã vào NC + OK NC (không vào "in:" NC phiếu nữa)
                    if (tpHere) row.TpNhap += r.Ok;
                    else
                    {
                        AddRecvCell(t.To, cell, r.Ok + r.Ng, rDoc);
                        if (xd == "GIU") Add(t.To, "nxGiu", "Nhận hàng đã sửa vào Tồn hàng lỗi đã xử lý (chờ trả nơi phát hiện)", r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text });
                        else if (t.To == "GC" && xd != "GIU" && carry && fromCell == HandoverCell.Xl && cell == HandoverCell.Xl
                                 && t.NgCaseId is long gid && inp.CaseInfo.TryGetValue(gid, out var gci) && GcXlBack(gci, gcChain, row.CodeMain) is { Kind: not "" } gb)
                        {   // [SUA-HO-TRA-NGUOC 2026-11-11] nơi phát hiện là Gia công, hàng nhóm khác sửa hộ trả NGƯỢC về: cùng đích như Gia công tự sửa (HandoverForm.RepairTarget):
                            //   đã làm xong NC cuối lúc phát hiện → xong NC cuối; Gia công làm hỏng ở NC k (WTS NG — [SUA-OK-NC]) → xong NC k; còn lại (chờ NC k) → về chờ NC k
                            //   theo NC phiếu (máy chủ tự điền — HandoverService [SUA-HO-TRA-NGUOC]). Vào NC + OK NC, không WTS. Trước Gói 16 không có đường này.
                            //   Nguồn: KHÁCH 11/11 ("bộ phận khác sửa hộ xong thì chuyển trả thẳng bộ phận báo lỗi") + 10/11 ("mọi trường hợp sửa không quay lại NC"); HÙNG 11/11 15:34.
                            Add("GC", "in:" + gb.Nc, "Nhận vào NC " + gb.Nc + " (hàng đã sửa về nơi phát hiện)", r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text });
                            Add("GC", "okSua:" + gb.Nc, "Hàng đã sửa về nơi phát hiện — tính là xong " + NcName(gb.Nc) + " (không WTS)", r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text });
                            gcBackDone = true;
                        }
                        else if (xd == "XONG") Add(t.To, "nxDa", "Hàng đã sửa về nơi phát hiện — tính là đã làm (không WTS)", r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text });
                        var rNc = !string.IsNullOrEmpty(r.ToNC) ? r.ToNC : t.ToNC;   // [NHAN-DOI-O-NC 2026-11-06] NC của lần nhận (đổi ô, phiếu không có NC) trước NC phiếu
                        if (t.To == "GC" && xd == "" && !gcBackDone && !string.IsNullOrEmpty(rNc) && cell is HandoverCell.Moi or HandoverCell.Xl)
                            Add("GC", "in:" + rNc, "Nhận vào NC " + rNc, r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text }); // chỉ phần OK
                        if (cell == HandoverCell.Sua) suaRunning[t.To] += r.Ok; // [GN-CELL] phần NG đếm lúc nhận trả ngay, không vào ô Hàng sửa
                    }
                    // ── [VULOI] vụ lỗi của lần nhận ──
                    //   Phiếu mang vụ: hàng của vụ tới ô bên nhận chọn (Hàng lỗi / Hàng sửa = đang giữ; Hàng lỗi đã xử lý / Hàng mới = đã xử lý);
                    //     NG đếm lúc nhận = vẫn vụ đó, quay về bên giao.
                    //   Phiếu không mang vụ: nhận vào ô Hàng lỗi / Hàng sửa, hoặc NG đếm lúc nhận = vụ MỚI sinh ở bên nhận (r.NgCaseId).
                    var q = r.Ok + r.Ng;
                    LedgerCase? ngCs = null;
                    if (carry)
                    {
                        csT!.Transit -= q;
                        if (cell == HandoverCell.Loi) { CHold(csT, t.To, HandoverCell.Loi, q, r.At); CAdd(csT.NhanLoi, t.To, q); }
                        else if (cell == HandoverCell.Sua) { CHold(csT, t.To, HandoverCell.Sua, q - r.Ng, r.At); CAdd(csT.NhanSua, t.To, q - r.Ng); }
                        else if (xd == "GIU") CHold(csT, t.To, HandoverCell.Xl, q - r.Ng, r.At);   // [SUA-XL-GIU] chưa về nơi phát hiện: vụ vẫn giữ
                        else CAdd(csT.DaXuLy, t.To, q - r.Ng);
                        CHop(csT, "NHAN", t.To, t.From, cell, q, txDocName, t.Id, r.At);
                        ngCs = csT;
                    }
                    else if (!tpHere && cell is HandoverCell.Loi or HandoverCell.Sua)
                    {
                        ngCs = Cs(r.NgCaseId);
                        ngCs.SinhRa += q;
                        CHold(ngCs, t.To, cell, q - (cell == HandoverCell.Sua ? r.Ng : 0), r.At);
                        CAdd(cell == HandoverCell.Loi ? ngCs.NhanLoi : ngCs.NhanSua, t.To, q - (cell == HandoverCell.Sua ? r.Ng : 0));
                        CHop(ngCs, "PS", t.To, t.From, cell, q, txDocName, t.Id, r.At);
                    }
                    else if (r.Ng > 0)
                    {
                        ngCs = Cs(r.NgCaseId);
                        ngCs.SinhRa += r.Ng;
                        CHop(ngCs, "PS", t.To, t.From, null, r.Ng, txDocName, t.Id, r.At);
                    }
                    // NG đếm lúc nhận: nhận vào ô Hàng lỗi thì toàn bộ đã là hàng lỗi, không trả lại lần nữa
                    if (r.Ng > 0 && cell != HandoverCell.Loi)
                        NgBack(t, purpose, r, cell == HandoverCell.Sua ? "glSuaNg" : "glTra", tpHere, ngCs!);
                }
            }

            // ── WTS ─────────────────────────────────────────────────────
            foreach (var w in inp.Wts)
            {
                var g = Pg2Group(w.ProcessGroup);
                if (g == "") continue;
                // [TARO-LAMHO-NC] WTS Taro làm hộ NG: vụ do HTSP gây, chiếc hỏng vẫn nằm trong Tồn hàng giao theo NC (ô vụ LAMHO) — không vào NG / Tồn hàng lỗi HTSP
                if (w.Ng != 0 && w.ProcessGroup == "TARO" && lamHoCode.ContainsKey((w.WtsCode ?? w.NC ?? "").Trim()))
                {
                    Add(g, "lhNg", "NG (WTS Taro làm hộ)", w.Ng, new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w) });
                    var csWl = Cs(w.NgCaseId);
                    var runW = row.LamHoRuns.FirstOrDefault(r => r.Ncs.Contains(lamHoCode[(w.WtsCode ?? w.NC ?? "").Trim()], StringComparer.OrdinalIgnoreCase));
                    if (runW != null) { runW.Ng += w.Ng; csWl.HelpFromNc ??= runW.FromNc; }   // NC làm tiếp khi sửa đạt về Gia công = như NG lúc nhận hàng giao theo NC (rà độc lập gói 6 mục 6)
                    csWl.SinhRa += w.Ng; CHold(csWl, g, HandoverCell.LamHo, w.Ng, WtsCaseAt(w));
                    CHop(csWl, "PS", g, null, HandoverCell.LamHo, w.Ng, $"WTS #{w.Id}", w.Id, w.At);
                    continue;
                }
                if (w.Ng != 0)
                {
                    Add(g, "ngc", "NG (WTS)", w.Ng, new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w) });
                    var csW = Cs(w.NgCaseId);   // [VULOI] NG làm hỏng: vụ sinh ra, nhóm tự giữ ở ô Hàng lỗi
                    csW.SinhRa += w.Ng; CHold(csW, g, HandoverCell.Loi, w.Ng, WtsCaseAt(w)); CAdd(csW.NhanLoi, g, w.Ng);
                    CHop(csW, "PS", g, null, HandoverCell.Loi, w.Ng, $"WTS #{w.Id}", w.Id, w.At);
                }
            }

            // ── [GN-NG-AFTER] Báo hàng lỗi phát hiện sau khi nhận: ô hàng tốt → Tồn hàng lỗi (cùng nhóm) ──
            foreach (var nr in inp.NgReps)
            {
                if (!row.Groups.ContainsKey(nr.Group) || nr.Qty == 0) continue;
                LedgerDoc BDoc() => new LedgerDoc { Kind = "BAOLOI", RefId = nr.Id, At = nr.At, Text = NgRepText(nr) };
                Add(nr.Group, "bl", "Báo hàng lỗi phát hiện sau khi nhận", nr.Qty, BDoc());
                {   // [VULOI] vụ sinh ra, nhóm báo giữ ở ô Hàng lỗi
                    var csB = Cs(nr.NgCaseId);
                    if (nr.Source == HandoverNgSource.Da && nr.NgCaseId != null) csB.FromDa = true;   // [SUA-XL-GIU] chỉ ghi lại, không tính
                    csB.SinhRa += nr.Qty; CHold(csB, nr.Group, HandoverCell.Loi, nr.Qty, nr.At); CAdd(csB.NhanLoi, nr.Group, nr.Qty);
                    CHop(csB, "PS", nr.Group, nr.Cause, HandoverCell.Loi, nr.Qty, $"Báo lỗi #{nr.Id}", nr.Id, nr.At);
                }
                if (nr.Source == HandoverNgSource.Chua)
                {
                    Add(nr.Group, "blChua", "Báo hàng lỗi (lấy từ hàng chưa làm)", nr.Qty, BDoc());
                    if (nr.Group == "GC" && !string.IsNullOrEmpty(nr.NC))
                        Add("GC", "blNc:" + nr.NC, "Báo hàng lỗi ở " + NcText(nr.NC), nr.Qty, BDoc());   // [N3-FIRST] chữ @VAO
                }
                else Add(nr.Group, "blDa", nr.Group == "KHO" ? "Báo phôi lỗi (lấy từ phôi còn)" : "Báo hàng lỗi (lấy từ hàng đã làm chưa giao)", nr.Qty, BDoc());
                if (nr.Origin == HandoverNgSource.DauVao) Add(nr.Group, "blDau", "Báo lỗi đầu vào", nr.Qty, BDoc());
            }

            // ── [GC-SUA-TAI-CHO 2026-11-04] Kết quả sửa (chứng từ nội bộ nhóm sửa) + KCS xác nhận ──
            //   Dùng đúng cột Excel đang có của nhóm sửa X (không ô nào tự cộng riêng):
            //   Lập, lấy từ ô Hàng sửa  : Giao·Hàng sửa + (đạt + không đạt); Nhận·Hàng lỗi + không đạt; Chờ KCS + đạt
            //   Lập, lấy từ ô Hàng lỗi  : Giao·Hàng lỗi (tự sửa) + đạt; Chờ KCS + đạt (không đạt = 0: hàng vẫn ở ô Hàng lỗi)
            //   KCS xác nhận            : Nhận·Hàng lỗi đã xử lý + đạt (GC: vào NC đã chọn); Nhận·Hàng lỗi + KCS loại; Chờ KCS − (đạt + loại)
            //   Tồn X = Tổng Nhận − Tổng Giao + Chờ nhận + Chờ KCS → mỗi chứng từ cộng trừ = 0, Cân PO không đổi (L6).
            //   Vụ lỗi: không sinh vụ mới; đạt chờ KCS = đang chuyển; KCS đạt = đã xử lý ở X; không đạt / KCS loại = giữ ô Hàng lỗi X.
            foreach (var rp in inp.Repairs.OrderBy(x => x.At).ThenBy(x => x.Id))
            {
                if (!row.Groups.ContainsKey(rp.Group)) continue;
                var g = rp.Group;
                var csR = Cs(rp.NgCaseId);
                var rDoc = new LedgerDoc { Kind = "KQSUA", RefId = rp.Id, At = rp.At, Text = RepairText(rp) };
                LedgerDoc RD() => new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text };
                if (rp.Cell == HandoverCell.Sua)
                {
                    var take = rp.Pass + rp.Fail;
                    Add(g, "gs", "Giao Hàng sửa (sửa hộ xong, trả nơi phát hiện)", take, RD());
                    suaRunning[g] -= take;
                    CHold(csR, g, HandoverCell.Sua, -take, rp.At);
                    if (rp.Fail != 0)
                    {
                        Add(g, "nl", "Nhận Hàng lỗi", rp.Fail, RD());
                        CHold(csR, g, HandoverCell.Loi, rp.Fail, rp.At); CAdd(csR.NhanLoi, g, rp.Fail);
                    }
                }
                else
                {
                    Add(g, "glTu", "Giao Hàng lỗi (tự sửa xong, trả nơi phát hiện)", rp.Pass, RD());
                    CHold(csR, g, HandoverCell.Loi, -rp.Pass, rp.At);
                }
                csR.Transit += rp.Pass;
                CHop(csR, "SUA", g, null, rp.Cell, rp.Pass + rp.Fail, $"Kết quả sửa #{rp.Id}", rp.Id, rp.At);
                // [SUA-XL-GIU][SUA-OK-NC] Kết quả sửa có dấu (chứng từ từ 10/11): đích của hàng đạt khi KCS xác nhận
                var rdest = RepairDest(inp, rp);
                string? okNc = null;   // Gia công: NC tính là đã xong (vào NC + OK NC, không WTS)
                if (g == "GC" && (rdest == "OK" || rdest == "XONG"))
                {
                    var raw = rdest == "XONG" ? gcLastNc
                            : rp.NgCaseId is long okId && inp.CaseInfo.TryGetValue(okId, out var okCi) && !string.IsNullOrWhiteSpace(okCi.DetectNc)
                              ? (row.CodeMain.TryGetValue("GC|" + okCi.DetectNc!.Trim(), out var mn) ? mn : okCi.DetectNc!.Trim()) : null;
                    okNc = raw != null && gcChain.Any(x => string.Equals(x.NC, raw, StringComparison.OrdinalIgnoreCase))
                         ? gcChain.First(x => string.Equals(x.NC, raw, StringComparison.OrdinalIgnoreCase)).NC : null;   // không còn trong quy trình → như cũ (dữ liệu lạ)
                    if (okNc == null) rdest = "";
                }
                foreach (var c in rp.Checks.OrderBy(x => x.At).ThenBy(x => x.Id))
                {
                    var cText = RepairCheckText(rp, c);
                    if (c.Ok != 0)
                    {
                        if (rdest == "GIU") csR.SuaDatBy.Add(g);   // [XL-TRA-NHOMSUA] nhóm đã sửa đạt chiếc của vụ mà hàng vào giữ ô Hàng lỗi đã xử lý (rà vòng 3: chỉ nhóm này mới là người sửa chiếc đang ở ô XL)
                        LedgerDoc XD() => new LedgerDoc { Kind = "XNSUA", RefId = c.Id, At = c.At, Text = cText };
                        Add(g, "nx", "Nhận Hàng lỗi đã xử lý", c.Ok, XD());
                        if (rdest == "GIU")
                        {   // [SUA-XL-GIU] nhóm sửa đứng trước nơi phát hiện: giữ ô Tồn hàng lỗi đã xử lý (vụ chưa xong), chờ trả tiếp
                            Add(g, "nxGiu", "Hàng đã sửa (KCS xác nhận) vào Tồn hàng lỗi đã xử lý (chờ trả nơi phát hiện)", c.Ok, XD());
                            CHold(csR, g, HandoverCell.Xl, c.Ok, c.At);
                        }
                        else
                        {
                            if (okNc != null)
                            {   // [SUA-OK-NC] / [SUA-XL-GIU] Gia công: hàng đạt tính là xong NC (vào NC + OK NC) — không ghi WTS lại
                                Add("GC", "in:" + okNc, "Nhận vào NC " + okNc + " (hàng sửa đạt)", c.Ok, XD());
                                Add("GC", "okSua:" + okNc, "Sửa đạt tính là xong " + NcName(okNc) + " (không WTS)", c.Ok, XD());
                            }
                            else if (rdest == "XONG")   // [SUA-XL-GIU] nhóm sửa là nơi phát hiện, đã làm xong chiếc trước lúc phát hiện → Đã làm
                                Add(g, "nxDa", "Hàng đã sửa (KCS xác nhận) — tính là đã làm (không WTS)", c.Ok, XD());
                            else if (g == "GC" && !string.IsNullOrEmpty(rp.ToNC) && !IsRepMark(rp.ToNC))
                                Add("GC", "in:" + rp.ToNC, "Nhận vào NC " + rp.ToNC, c.Ok, XD());
                            CAdd(csR.DaXuLy, g, c.Ok);
                            if (rp.Cell == HandoverCell.Loi) csR.TuXuLy.Add(g);   // nhóm tự xử lý hàng lỗi của mình — chỉ khi KCS đã xác nhận đạt (rà độc lập T2)
                        }
                    }
                    if (c.Ng != 0)
                    {
                        Add(g, "nl", "Nhận Hàng lỗi", c.Ng, new LedgerDoc { Kind = "XNSUA", RefId = c.Id, At = c.At, Text = cText });
                        CHold(csR, g, HandoverCell.Loi, c.Ng, c.At); CAdd(csR.NhanLoi, g, c.Ng);
                    }
                    csR.Transit -= c.Ok + c.Ng;
                    CHop(csR, "XN", g, "KCS", HandoverCell.Xl, c.Ok + c.Ng, $"KCS xác nhận #{c.Id}", c.Id, c.At);
                }
                Add(g, "ckcs", "Chờ KCS xác nhận hàng sửa", rp.Waiting, RD());
            }

            // Đã làm (OK) theo luật từng nhóm
            ComputeGcOk(inp, gcChain, row, T);
            foreach (var g in new[] { "HTSP", "KCS", "PKG" })
                ComputeCodeOk(inp, g, row, T);
            // [SUA-XL-GIU][SUA-OK-NC] Đã làm + hàng sửa đạt tính là đã làm (không WTS): Gia công = sửa đạt xong NC cuối; nhóm khác = hàng đã sửa về nơi phát hiện
            foreach (var g in new[] { "GC", "HTSP", "KCS", "PKG" })
            {
                var k = g == "GC" ? (gcLastNc == null ? null : "okSua:" + gcLastNc) : "nxDa";
                if (k == null || !acc.TryGetValue((g, k), out var extra) || extra.Value == 0) continue;
                var okT = T(g, "ok", "Đã làm");
                okT.Value += extra.Value;
                okT.Docs.AddRange(extra.Docs);
                okT.Label += " + hàng sửa đạt tính là đã làm";
            }

            // ── Ráp số từng nhóm ─────────────────────────────────────────
            foreach (var code in GroupCodes)
            {
                var G = row.Groups[code];
                decimal V(string k) => acc.TryGetValue((code, k), out var t) ? t.Value : 0m;
                LedgerTerm Tm(string k, string label, int sign)
                {
                    var src = acc.TryGetValue((code, k), out var t) ? t : new LedgerTerm { Label = label };
                    return new LedgerTerm { Label = src.Label == "" ? label : src.Label, Value = src.Value, Sign = sign, Docs = src.Docs };
                }

                G.NhanMoi   = V("nm");
                G.NhanLoi   = V("nl");
                G.NhanSua   = V("ns");
                G.NhanLoiXl = V("nx") + V("nbu");   // [KHACH-GUI-LAI] Kho: + phôi khách gửi bù (nhóm khác không có "nbu")
                G.NhanLamHo = V("nhSend") + V("nhRet");
                G.NhanLamHoVe = V("nhRet"); // [GN-WTS-CAP]
                G.GiaoMoi   = V("gm");
                G.GiaoLoi   = V("glTra") + V("glGui") + V("glTu") + V("glChuyen") /* [GN-CELL2] */ + V("glSuaNg") + V("glLamHo") + V("glHuy") + V("glKhach")
                            + V("glLhKho");   // [KTT-PHOILOI] Taro trả phôi lỗi về Kho (từ hàng giao theo NC) // [GN-NG-OUT] + huỷ / trả khách; [GN-CELL] + tự sửa trả, + trả lại hàng nhờ sửa NG
                G.GiaoSua   = V("gs");
                G.GiaoLoiXl = V("gx"); // [GN-LEDGER] trước 10/11 luôn 0 · [SUA-XL-GIU] hàng đã sửa giao tiếp từ ô Tồn hàng lỗi đã xử lý (chờ trả nơi phát hiện)
                G.XlGiu     = V("nxGiu") - V("gx");   // [SUA-XL-GIU] Tồn hàng lỗi đã xử lý (hàng đã sửa, giữ vụ)
                G.GiaoLamHo = V("ghSend") + V("ghRet");
                G.ChoNhan   = V("cn");                              // [GN-RECV-NG] mục 37
                G.ChoKcs    = V("ckcs");                            // [GC-SUA-TAI-CHO] hàng sửa đạt chờ KCS xác nhận
                G.Ton       = G.NhanTong - G.GiaoTong + G.ChoNhan + G.ChoKcs;  // [GN-RECV-NG] trước: NhanTong − GiaoTong; [GC-SUA-TAI-CHO] + Chờ KCS
                G.NgCuoi    = V("ngc");
                G.NgDau     = V("glTra") + V("blDau");   // [GN-NG-AFTER] + báo lỗi đầu vào phát hiện sau khi nhận

                // Ô Nhận / Giao: diễn giải = danh sách phiếu
                void ListExplain(string key, string title, params (string k, string label)[] parts)
                {
                    var e = new LedgerExplain { Title = $"{G.Label} · {title}", Formula = string.Join(" + ", parts.Select(p => p.label)) };
                    foreach (var p in parts) e.Terms.Add(Tm(p.k, p.label, 1));
                    e.Terms.RemoveAll(x => x.Value == 0 && parts.Length > 1);
                    e.Result = e.Terms.Sum(x => x.Sign * x.Value);
                    G.Explains[key] = e;
                }
                ListExplain("NhanMoi",   "Nhận · Hàng mới",          ("nm", code == "KHO" ? "Phôi Kho nhận" : "Phiếu Hàng mới đã nhận"));
                ListExplain("NhanLoi",   "Nhận · Hàng lỗi",          ("nl", "Hàng lỗi bị trả về (lỗi do mình / phôi lỗi)"));
                ListExplain("NhanSua",   "Nhận · Hàng sửa",          ("ns", "Hàng nhóm khác gửi nhờ sửa"));
                if (code == "KHO") ListExplain("NhanLoiXl", "Nhận · Hàng lỗi đã xử lý", ("nx", "Phôi đã sửa trả về"), ("nbu", "Khách gửi bù (phôi bù cho hàng lỗi đã trả khách / huỷ)"));   // [KHACH-GUI-LAI]
                else
                ListExplain("NhanLoiXl", "Nhận · Hàng lỗi đã xử lý", ("nx", "Hàng đã sửa trả về (đã OK, nhập WTS tiếp được)"));
                ListExplain("NhanLamHo", "Nhận · Giao theo NC",     ("nhSend", "Gia công giao theo NC (máy loại trừ)"), ("nhRet", "Hàng trả về vào NC"));
                ListExplain("GiaoMoi",   "Giao · Hàng mới",          ("gm", "Phiếu Hàng mới (gồm phiếu chờ nhận)"));
                ListExplain("GiaoLoi",   "Giao · Hàng lỗi",          ("glTra", "Hàng lỗi công đoạn trước — trả nơi gây lỗi"), ("glGui", "Gửi hàng lỗi đi nhờ sửa"), ("glTu", "Tự sửa xong, trả nơi phát hiện"), ("glChuyen", "Chuyển sang ô Hàng lỗi nhóm khác"), ("glSuaNg", "Trả lại hàng nhờ sửa bị NG lúc nhận"), ("glLamHo", "Trả lỗi từ hàng giao theo NC"), ("glHuy", "Huỷ hàng lỗi"), ("glKhach", "Trả khách hàng lỗi"),
                    ("glLhKho", "Taro trả phôi lỗi về Kho (hàng giao theo NC)")); // [GN-NG-OUT][GN-CELL] · [KTT-PHOILOI]
                ListExplain("GiaoSua",   "Giao · Hàng sửa",          ("gs", "Sửa hộ xong, trả nơi phát hiện"));
                ListExplain("GiaoLamHo", "Giao · Giao theo NC",     ("ghSend", "Giao theo NC (máy loại trừ)"), ("ghRet", "Trả về Gia công theo NC"));
                // [SUA-XL-GIU 2026-11-10] trước: cột luôn trống (hàng đã xử lý chỉ NHẬN). Nay = hàng đã sửa giao tiếp về nơi phát hiện (khách 10/11 2.1 A / 2.2 A).
                ListExplain("GiaoLoiXl", "Giao · Hàng lỗi đã xử lý", ("gx", "Hàng đã sửa (KCS xác nhận) giao tiếp về nơi phát hiện"));
                {
                    var xe = new LedgerExplain { Title = $"{G.Label} · Tồn hàng lỗi đã xử lý", Formula = "Hàng đã sửa nhận vào giữ − Giao Hàng lỗi đã xử lý", Rule = "SUA-XL-GIU", Result = G.XlGiu,
                        Note = "Hàng đã sửa xong, KCS đã xác nhận đạt, nhóm này đã làm xong chiếc đó trước lúc phát hiện lỗi: không ghi WTS số lượng, chờ giao tiếp về nơi phát hiện lỗi (giữ vụ). Giờ làm lại ghi \"Việc không làm ra SP\"." };
                    xe.Terms.Add(Tm("nxGiu", "Hàng đã sửa nhận vào giữ", 1)); xe.Terms.Add(Tm("gx", "Giao Hàng lỗi đã xử lý", -1));
                    xe.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Hàng đã sửa nhận vào giữ");
                    G.Explains["XlGiu"] = xe;
                    if (G.XlGiu < 0) G.Warnings.Add($"{G.Label}: giao hàng đã sửa nhiều hơn Tồn hàng lỗi đã xử lý ({G.XlGiu:0.##}) — K1");
                }

                // Tổng Nhận / Tổng Giao (cột gọn trên bảng chính)
                {
                    var en = new LedgerExplain { Title = $"{G.Label} · Tổng Nhận", Formula = "Hàng mới + Hàng lỗi + Hàng Sửa + Hàng lỗi đã xử lý + Giao theo NC",
                        Note = "Số bên nhận đã đếm và xác nhận. Hàng quay vòng (lỗi trả về, sửa xong trả về) cũng tính, nên có thể lớn hơn số PO." };
                    foreach (var k in new[] { "NhanMoi", "NhanLoi", "NhanSua", "NhanLoiXl", "NhanLamHo" })
                    {
                        var x = G.Explains[k];
                        var v = x.Terms.Sum(t => t.Sign * t.Value);
                        if (v != 0 || k == "NhanMoi")
                            en.Terms.Add(new LedgerTerm { Label = x.Title.Replace(G.Label + " · ", ""), Value = v, Sign = 1, Docs = x.Terms.SelectMany(t => t.Docs).ToList() });
                    }
                    en.Result = G.NhanTong;
                    G.Explains["NhanTong"] = en;
                    var eg = new LedgerExplain { Title = $"{G.Label} · Tổng Giao", Formula = "Hàng mới + Hàng lỗi + Hàng Sửa + Hàng lỗi đã xử lý + Giao theo NC",
                        Note = "Tính cả phiếu đã lập mà bên nhận chưa nhận (L4)." };
                    foreach (var k in new[] { "GiaoMoi", "GiaoLoi", "GiaoSua", "GiaoLamHo" })
                    {
                        var x = G.Explains[k];
                        var v = x.Terms.Sum(t => t.Sign * t.Value);
                        if (v != 0 || k == "GiaoMoi")
                            eg.Terms.Add(new LedgerTerm { Label = x.Title.Replace(G.Label + " · ", ""), Value = v, Sign = 1, Docs = x.Terms.SelectMany(t => t.Docs).ToList() });
                    }
                    eg.Result = G.GiaoTong;
                    G.Explains["GiaoTong"] = eg;
                }

                // Tồn
                {
                    var e = new LedgerExplain
                    {
                        // [GN-RECV-NG] mục 37: + Chờ nhận
                        Title = $"{G.Label} · Tồn", Formula = "Tổng Nhận − Tổng Giao + Chờ nhận + Chờ KCS", Rule = "L6",   // [GC-SUA-TAI-CHO] + Chờ KCS
                        Note = "Tồn là số hàng thuộc nhóm. Bên giao đã bấm Giao thì hàng tính sang Tồn bên nhận (phần Chờ nhận), dù bên nhận chưa bấm Nhận. "
                             + "Phần Chờ nhận chưa được nhập WTS: phải đếm và bấm Nhận trước (không nằm trong Chưa làm).",
                        Result = G.Ton
                    };
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Nhận", Value = G.NhanTong, Sign = 1, Docs = AllDocs(acc, code, "nm", "nl", "ns", "nx", "nbu", "nhSend", "nhRet") });   // [KHACH-GUI-LAI] + phiếu bù (rà độc lập Gói 14 L2)
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Giao", Value = G.GiaoTong, Sign = -1, Docs = AllDocs(acc, code, "gm", "glTra", "glGui", "glTu", "glChuyen", "glSuaNg", "glLamHo", "glHuy", "glKhach", "gs", "ghSend", "ghRet", "glLhKho") });   // [KTT-PHOILOI] + trả phôi lỗi Kho
                    if (G.ChoNhan != 0)
                        e.Terms.Add(new LedgerTerm { Label = "Chờ nhận (đã giao tới, chưa bấm Nhận)", Value = G.ChoNhan, Sign = 1, Docs = AllDocs(acc, code, "cn") });
                    if (G.ChoKcs != 0)   // [GC-SUA-TAI-CHO]
                        e.Terms.Add(new LedgerTerm { Label = "Chờ KCS xác nhận hàng sửa", Value = G.ChoKcs, Sign = 1, Docs = AllDocs(acc, code, "ckcs") });
                    G.Explains["Ton"] = e;

                    // [GN-RECV-NG] Diễn giải riêng ô Chờ nhận
                    var ce = new LedgerExplain
                    {
                        Title = $"{G.Label} · Chờ nhận", Formula = "Σ (số trên phiếu − số đã nhận) các phiếu giao tới nhóm chưa nhận đủ", Rule = "L2",
                        Note = "Đã tính vào Tồn của nhóm. Chưa tính vào Chưa làm: phải bấm Nhận (đếm OK / NG) thì mới nhập WTS được.",
                        Result = G.ChoNhan
                    };
                    ce.Terms.Add(Tm("cn", "Phiếu chờ nhận", 1));
                    G.Explains["ChoNhan"] = ce;

                    // [GC-SUA-TAI-CHO] Diễn giải ô Chờ KCS
                    var ke = new LedgerExplain
                    {
                        Title = $"{G.Label} · Chờ KCS xác nhận hàng sửa", Formula = "Σ (số sửa đạt − số KCS đã xác nhận) các Kết quả sửa của nhóm", Rule = "L6",
                        Note = "Hàng nhóm đã sửa xong, ghi đạt. Đã tính vào Tồn của nhóm. Chưa vào Chưa làm: KCS xác nhận đạt mới nhập WTS được; KCS loại thì về ô Hàng lỗi của nhóm.",
                        Result = G.ChoKcs
                    };
                    ke.Terms.Add(Tm("ckcs", "Kết quả sửa chờ KCS", 1));
                    G.Explains["ChoKcs"] = ke;
                }

                if (code == "KHO")
                {
                    // Kho: phôi tốt còn giao + hàng lỗi đang giữ
                    G.ConGiao = G.NhanMoi + G.NhanLoiXl - G.GiaoMoi - V("glTra") - V("blDa");   // [GN-NG-AFTER] − phôi báo lỗi
                    // [GN-CELL] Tách 2 ô đang giữ theo Excel: Hàng lỗi (của mình) / Hàng sửa (nhóm khác gửi nhờ)
                    G.LoiGiu  = G.NhanLoi + V("bl") - V("glGui") - V("glTu") - V("glChuyen") - V("glHuy") - V("glKhach"); // [GN-NG-OUT] [GN-CELL2] + chuyển hàng lỗi; [GN-NG-AFTER] + báo lỗi sau khi nhận
                    G.SuaGiu  = G.NhanSua - G.GiaoSua - V("glSuaNg");
                    G.LamHoGiu = V("nhSend") - V("ghRet") - V("glLamHo");   // [GN-ROUTE-FREE] trước: 0 (Gia công không giao theo NC cho Kho được)
                    var e = new LedgerExplain { Title = "Kho · Phôi còn giao", Formula = "Phôi nhận + Phôi đã sửa trả về + Khách gửi bù − Giao Hàng mới − Trả lỗi − Báo phôi lỗi", Rule = "L4", Result = G.ConGiao };   // [GN-NG-AFTER] · [KHACH-GUI-LAI] + bù
                    e.Terms.Add(Tm("nm", "Phôi Kho nhận", 1));
                    e.Terms.Add(Tm("nx", "Phôi đã sửa trả về", 1));
                    e.Terms.Add(Tm("nbu", "Khách gửi bù", 1));   // [KHACH-GUI-LAI]
                    e.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    e.Terms.Add(Tm("glTra", "Trả lỗi", -1));
                    e.Terms.Add(Tm("blDa", "Báo phôi lỗi (phát hiện sau khi nhận)", -1));   // [GN-NG-AFTER]
                    e.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Phôi Kho nhận");
                    G.Explains["ConGiao"] = e;
                    var l = new LedgerExplain { Title = "Kho · Tồn hàng lỗi", Formula = "Nhận Hàng lỗi + Báo phôi lỗi − Gửi đi nhờ sửa − Tự sửa trả − Chuyển hàng lỗi − Huỷ − Trả khách", Rule = "L5, N9", Result = G.LoiGiu,
                        Note = "Phôi lỗi bị trả về Kho (Excel TH1 B1) hoặc Kho tự báo phôi lỗi phát hiện sau khi nhận, chờ Kho gửi nhờ sửa (TH1 B2), chuyển, huỷ hoặc trả khách." };
                    l.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1));
                    l.Terms.Add(Tm("bl", "Báo phôi lỗi (phát hiện sau khi nhận)", 1));   // [GN-NG-AFTER]
                    l.Terms.Add(Tm("glGui", "Gửi đi nhờ sửa", -1)); l.Terms.Add(Tm("glTu", "Tự sửa trả", -1)); l.Terms.Add(Tm("glChuyen", "Chuyển hàng lỗi", -1)); // [GN-CELL2]
                    l.Terms.Add(Tm("glHuy", "Huỷ hàng lỗi", -1)); l.Terms.Add(Tm("glKhach", "Trả khách hàng lỗi", -1)); // [GN-NG-OUT]
                    l.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng lỗi");
                    G.Explains["LoiGiu"] = l;
                    var sg = new LedgerExplain { Title = "Kho · Tồn hàng sửa", Formula = "Nhận Hàng sửa − Giao Hàng sửa − Trả lại hàng NG lúc nhận", Rule = "L5", Result = G.SuaGiu,
                        Note = "Hàng nhóm khác gửi Kho nhờ sửa (Excel không có tình huống này, để sẵn cho đủ ô)." };
                    sg.Terms.Add(Tm("ns", "Nhận Hàng sửa", 1)); sg.Terms.Add(Tm("gs", "Giao Hàng sửa", -1)); sg.Terms.Add(Tm("glSuaNg", "Trả lại hàng NG lúc nhận", -1));
                    sg.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng sửa");
                    G.Explains["SuaGiu"] = sg;
                    if (G.ConGiao < 0) G.Warnings.Add($"Kho: giao nhiều hơn phôi có ({G.ConGiao:0.##})");
                    if (G.LoiGiu < 0)  G.Warnings.Add($"Kho: giao hàng lỗi nhiều hơn tồn hàng lỗi ({G.LoiGiu:0.##}) — K1");
                    if (G.SuaGiu < 0)  G.Warnings.Add($"Kho: giao hàng sửa nhiều hơn tồn hàng sửa ({G.SuaGiu:0.##}) — K1");
                    if (G.LamHoGiu < 0) G.Warnings.Add($"Kho: trả về Gia công nhiều hơn tồn theo NC ({G.LamHoGiu:0.##}) — K4");   // [GN-ROUTE-FREE]
                }
                else
                {
                    var daLamTerm = acc.TryGetValue((code, "ok"), out var okT) ? okT : new LedgerTerm { Label = "Đã làm (WTS OK)" };
                    G.DaLam = daLamTerm.Value;

                    // Chưa làm = Nhận mới + Nhận lỗi đã XL + Làm hộ trả về − Đã làm − NG làm hỏng − Trả lỗi − Gửi làm hộ
                    G.ChuaLam = G.NhanMoi + G.NhanLoiXl + V("nhRet") - G.DaLam - G.NgCuoi - V("glTra") - V("ghSend") - V("blChua")   // [GN-NG-AFTER] − báo lỗi lấy từ hàng chưa làm
                              - V("nxGiu");   // [SUA-XL-GIU] hàng đã sửa giữ ở Tồn hàng lỗi đã xử lý: không vào Chưa làm
                    G.ConGiao = G.DaLam - G.GiaoMoi - V("blDa");   // [GN-NG-AFTER] − báo lỗi lấy từ hàng đã làm chưa giao
                    // [GN-CELL] Tách 2 ô đang giữ theo Excel: Hàng lỗi (của mình: bị trả về + NG làm hỏng) / Hàng sửa (nhóm khác gửi nhờ)
                    G.LoiGiu  = G.NhanLoi + G.NgCuoi + V("bl") - V("glGui") - V("glTu") - V("glChuyen") - V("glHuy") - V("glKhach"); // [GN-NG-OUT] [GN-CELL2] + chuyển hàng lỗi; [GN-NG-AFTER] + báo lỗi sau khi nhận
                    G.SuaGiu  = G.NhanSua - G.GiaoSua - V("glSuaNg");
                    G.LamHoGiu = V("nhSend") - V("ghRet") - V("glLamHo") - V("glLhKho");   // [KTT-PHOILOI] − phôi lỗi Taro trả thẳng Kho

                    var e = new LedgerExplain
                    {
                        Title = $"{G.Label} · Chưa làm", Rule = "L3",
                        Formula = "Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + Hàng trả về vào NC − Đã làm − NG (WTS) − Trả hàng lỗi − Giao theo NC − Báo hàng lỗi",   // [GN-NG-AFTER]
                        Note = "Đây là trần khi nhập WTS: không ghi làm xong nhiều hơn số này. Khác Excel: trừ cả NG (WTS), và chỉ trừ phần Giao hàng lỗi lấy từ hàng chưa làm (Giao hàng lỗi có tích “công đoạn trước”; tồn hàng lỗi chuyển đi sửa / tự sửa trả không trừ).",
                        Result = G.ChuaLam
                    };
                    e.Terms.Add(Tm("nm", "Nhận Hàng mới", 1));
                    e.Terms.Add(Tm("nx", "Nhận Hàng lỗi đã xử lý", 1));
                    e.Terms.Add(Tm("nhRet", "Hàng trả về vào NC", 1));
                    e.Terms.Add(new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = -1, Docs = daLamTerm.Docs });
                    e.Terms.Add(Tm("ngc", "NG (WTS)", -1));
                    e.Terms.Add(Tm("glTra", "Trả hàng lỗi", -1));
                    e.Terms.Add(Tm("ghSend", "Giao theo NC", -1));
                    e.Terms.Add(Tm("blChua", "Báo hàng lỗi (phát hiện sau khi nhận)", -1));   // [GN-NG-AFTER]
                    e.Terms.Add(Tm("nxGiu", "Hàng đã sửa giữ ở Tồn hàng lỗi đã xử lý", -1));   // [SUA-XL-GIU]
                    e.Terms.RemoveAll(x => x.Value == 0 && !(x.Label == "Nhận Hàng mới" || x.Label == daLamTerm.Label));
                    G.Explains["ChuaLam"] = e;

                    G.Explains["DaLam"] = new LedgerExplain
                    {
                        Title = $"{G.Label} · Đã làm", Rule = code == "GC" ? "N1, N2" : "N5",
                        Formula = daLamTerm.Label, Result = G.DaLam,
                        Note = code switch
                        {
                            "GC" => "Gia công: Đã làm = OK của NC cuối (máy đồng dạng gộp vào NC chính, NC máy loại trừ không tính). Hàng chỉ xong khi qua NC cuối.",
                            "HTSP" => "HTSP: Đã làm = số OK nhỏ nhất giữa các mã CV chính (Taro / Bavia / Rửa). Mã chuẩn bị 1V/1T/1R/1K/1D/1G không tính số lượng.",
                            _ => "Đã làm = số OK nhỏ nhất giữa các mã CV chính. Mã chuẩn bị không tính số lượng."
                        },
                        Terms = daLamTerm.Docs.Count > 0 || daLamTerm.Value != 0
                            ? new List<LedgerTerm> { new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = 1, Docs = daLamTerm.Docs } }
                            : new List<LedgerTerm>()
                    };
                    var ng = new LedgerExplain { Title = $"{G.Label} · NG (WTS)", Formula = "Tổng NG trên WTS của nhóm", Rule = "L1",
                        Note = "NG cuối: lỗi do chính nhóm làm hỏng. Hàng này nằm trong tồn hàng lỗi cho tới khi được gửi sửa, tự sửa trả lại, huỷ hoặc trả khách.", Result = G.NgCuoi };
                    ng.Terms.Add(Tm("ngc", "NG trên WTS", 1));
                    G.Explains["NgCuoi"] = ng;

                    var cg = new LedgerExplain { Title = $"{G.Label} · Đã làm chưa giao", Formula = "Đã làm − Giao Hàng mới − Báo hàng lỗi", Rule = "L4",   // [GN-NG-AFTER]
                        Note = "Trần khi lập phiếu Hàng mới. Tính cả phiếu đã giao nhưng bên nhận chưa nhận.", Result = G.ConGiao };
                    cg.Terms.Add(new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = 1, Docs = daLamTerm.Docs });
                    cg.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    cg.Terms.Add(Tm("blDa", "Báo hàng lỗi (phát hiện sau khi nhận)", -1));   // [GN-NG-AFTER]
                    cg.Terms.RemoveAll(x => x.Value == 0 && x.Label == "Báo hàng lỗi (phát hiện sau khi nhận)");
                    G.Explains["ConGiao"] = cg;

                    var lg = new LedgerExplain { Title = $"{G.Label} · Tồn hàng lỗi",
                        Formula = "Nhận Hàng lỗi + NG (WTS) + Báo hàng lỗi − Gửi đi nhờ sửa − Tự sửa trả − Chuyển hàng lỗi − Huỷ − Trả khách", Rule = "L5, K1",   // [GN-NG-AFTER]
                        Note = "Hàng lỗi CỦA NHÓM (bị trả về vì lỗi công đoạn trước, hoặc tự làm hỏng). Trần khi giao phân loại Hàng lỗi không tích: gửi nhờ sửa / tự sửa xong trả về; và khi Huỷ / Trả khách.", Result = G.LoiGiu }; // [GN-PLH] mục 36: trước "giao từ ô"
                    lg.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1)); lg.Terms.Add(Tm("ngc", "NG (WTS)", 1));
                    lg.Terms.Add(Tm("bl", "Báo hàng lỗi (phát hiện sau khi nhận)", 1));   // [GN-NG-AFTER]
                    lg.Terms.Add(Tm("glGui", "Gửi đi nhờ sửa", -1)); lg.Terms.Add(Tm("glTu", "Tự sửa trả", -1)); lg.Terms.Add(Tm("glChuyen", "Chuyển hàng lỗi", -1)); // [GN-CELL2]
                    lg.Terms.Add(Tm("glHuy", "Huỷ hàng lỗi", -1)); lg.Terms.Add(Tm("glKhach", "Trả khách hàng lỗi", -1)); // [GN-NG-OUT]
                    lg.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng lỗi");
                    G.Explains["LoiGiu"] = lg;

                    // [GN-CELL] Ô Hàng sửa đang giữ
                    var sg = new LedgerExplain { Title = $"{G.Label} · Tồn hàng sửa",
                        Formula = "Nhận Hàng sửa − Giao Hàng sửa − Trả lại hàng NG lúc nhận", Rule = "L5, K1",
                        Note = "Hàng NHÓM KHÁC gửi nhờ sửa (Excel TH1 B2, TH3 B2). Sửa xong giao phân loại Hàng sửa về nơi phát hiện (TH1 B3, TH3 B3). Không tính vào số sản xuất của nhóm.", Result = G.SuaGiu }; // [GN-PLH] mục 36: trước "giao từ ô"
                    sg.Terms.Add(Tm("ns", "Nhận Hàng sửa", 1)); sg.Terms.Add(Tm("gs", "Giao Hàng sửa", -1)); sg.Terms.Add(Tm("glSuaNg", "Trả lại hàng NG lúc nhận", -1));
                    sg.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng sửa");
                    G.Explains["SuaGiu"] = sg;

                    var lh = new LedgerExplain { Title = $"{G.Label} · Tồn hàng giao theo NC", Formula = "Nhận hàng giao theo NC − Đã trả về Gia công − Lỗi trả lại − Phôi lỗi trả Kho", Rule = "N3, K4",   // [KTT-PHOILOI]
                        Note = "Hàng của nhóm khác, không tính vào số sản xuất của nhóm này.", Result = G.LamHoGiu };
                    lh.Terms.Add(Tm("nhSend", "Nhận theo NC", 1)); lh.Terms.Add(Tm("ghRet", "Trả về Gia công theo NC", -1)); lh.Terms.Add(Tm("glLamHo", "Lỗi trả lại", -1));
                    lh.Terms.Add(Tm("glLhKho", "Taro trả phôi lỗi về Kho", -1));   // [KTT-PHOILOI]
                    lh.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận theo NC");
                    G.Explains["LamHoGiu"] = lh;
                    // [TARO-LAMHO-NC] Hàng làm hộ bị lỗi (WTS Taro làm hộ NG, đã có vụ) nằm trong Tồn hàng giao theo NC, chỉ trả Gia công ô Hàng lỗi
                    var lhNgHeld = row.LamHoNgHeld(code);
                    if (lhNgHeld != 0 || V("lhNg") != 0)
                    {
                        lh.Note += $" Trong đó {lhNgHeld:0.##} chiếc làm hộ bị lỗi (WTS Taro làm hộ NG, đã có vụ) — chỉ trả Gia công vào ô Hàng lỗi; trả hàng tốt tối đa {G.LamHoGiu - lhNgHeld:0.##}.";
                        if (lhNgHeld > G.LamHoGiu) G.Warnings.Add($"{G.Label}: hàng làm hộ bị lỗi đang giữ ({lhNgHeld:0.##}) nhiều hơn Tồn hàng giao theo NC ({G.LamHoGiu:0.##}) — K4");
                    }
                    if (code == "HTSP")
                        foreach (var r in row.LamHoRuns.Where(r => r.HasTaro))
                        {
                            var ex = new LedgerExplain { Title = $"HTSP · Taro làm hộ {string.Join(", ", r.Ncs.Select(NcName))} · trả về {NcName(r.NextNc)}", Rule = "N3, K4",
                                Formula = "OK bộ Taro (nhỏ nhất các mã Taro chính của NC, bộ gộp = OK mã gộp + min mã thành phần) − đã trả về Gia công (hàng tốt)",
                                Note = $"Hàng Gia công giao theo NC từ {NcName(r.FromNc)}" + (IsVao(r.FromNc) ? " / Kho giao thẳng Taro" : "") + $" cho HTSP làm Taro; chỉ trả về {NcName(r.NextNc)} số đã xong bộ Taro (Workflow ON). Nhận vào {r.In:0.##}."
                                     + (r.TraKho != 0 ? $" Phôi lỗi đã trả thẳng Kho {r.TraKho:0.##}." : ""),   // [KHO-TARO-THANG][KTT-PHOILOI] chỉ chữ
                                Result = r.Done - r.Ret };
                            ex.Terms.Add(new LedgerTerm { Label = "OK bộ Taro làm hộ", Value = r.Done, Sign = 1, Docs = r.DoneDocs });
                            ex.Terms.Add(new LedgerTerm { Label = $"Đã trả về {NcName(r.NextNc)} (hàng tốt)", Value = r.Ret, Sign = -1, Docs = r.RetDocs });
                            G.Explains["LamHoTaro:" + r.Key] = ex;
                            if (r.Done - r.Ret < 0) G.Warnings.Add($"HTSP: trả hàng làm hộ Taro {string.Join(", ", r.Ncs.Select(NcName))} về {NcName(r.NextNc)} nhiều hơn số đã xong bộ Taro ({r.Done - r.Ret:0.##}) — K4");
                        }

                    if (G.ChuaLam < 0)  G.Warnings.Add($"{G.Label}: Chưa làm âm ({G.ChuaLam:0.##}) — WTS / trả lỗi nhiều hơn hàng nhận vào (L3)");
                    if (G.ConGiao < 0)  G.Warnings.Add($"{G.Label}: giao Hàng mới nhiều hơn số đã làm ({G.ConGiao:0.##}) — L4");
                    if (G.LoiGiu < 0)   G.Warnings.Add($"{G.Label}: giao hàng lỗi nhiều hơn tồn hàng lỗi ({G.LoiGiu:0.##}) — K1");
                    if (G.SuaGiu < 0)   G.Warnings.Add($"{G.Label}: giao hàng sửa nhiều hơn tồn hàng sửa ({G.SuaGiu:0.##}) — K1");
                    if (G.LamHoGiu < 0) G.Warnings.Add($"{G.Label}: trả về Gia công nhiều hơn tồn theo NC ({G.LamHoGiu:0.##}) — K4");
                }

                // Kiểm tách: Tồn = các phần đang giữ (phát hiện lỗi chương trình nếu lệch) — [GN-CELL] + ô Hàng sửa
                var parts = code == "KHO" ? G.ConGiao + G.LoiGiu + G.SuaGiu + G.LamHoGiu /* [GN-ROUTE-FREE] */ : G.ChuaLam + G.ConGiao + G.LoiGiu + G.SuaGiu + G.LamHoGiu;
                parts += G.ChoNhan;   // [GN-RECV-NG] mục 37: Tồn gồm cả phần chờ nhận
                parts += G.ChoKcs;    // [GC-SUA-TAI-CHO] + hàng sửa chờ KCS xác nhận
                parts += G.XlGiu;     // [SUA-XL-GIU] + hàng đã sửa chờ trả nơi phát hiện
                if (parts != G.Ton) G.Warnings.Add($"{G.Label}: Tồn {G.Ton:0.##} khác tổng các phần {parts:0.##} — báo lại để kiểm");
            }

            // [GN-RECV-NG] Phiếu trả tự lập mà lần nhận gốc đã huỷ (không được xảy ra — huỷ lần nhận thì phiếu trả huỷ theo)
            foreach (var o in orphanAuto)
                row.G(o.From).Warnings.Add($"Phiếu trả #{o.Id} ({GroupLabel(o.From)} → {GroupLabel(o.To)}) mất lần nhận gốc — báo lại để kiểm");

            // ── [KHACH-GUI-LAI 2026-11-11] Đã bù theo vụ (không vào cân vụ — phôi bù là phôi mới của Kho) ──
            foreach (var bu1 in inp.Bu)
            {
                var csB = Cs(bu1.NgCaseId);
                csB.DaBu += bu1.Qty;
                csB.BuDocs.Add((bu1.Id, bu1.Qty));
            }

            // ── [VULOI] Chốt sổ vụ lỗi: gắn nhãn, bỏ vụ rỗng, tự kiểm (cân từng vụ + khớp ô Tồn hàng lỗi / Tồn hàng sửa) ──
            foreach (var k in row.Cases.Keys.ToList())
            {
                var cs = row.Cases[k];
                // [VL8-TIMELINE] Chạy lại các lần đổi ô theo thời gian (cùng lúc: số vào trước số ra) → số thấp nhất từng ô
                {
                    var run = new Dictionary<string, decimal>();
                    foreach (var mv in cs.Moves.OrderBy(m => m.At).ThenByDescending(m => m.Q))
                    {
                        var v = (run.TryGetValue(mv.Key, out var rv) ? rv : 0m) + mv.Q;
                        run[mv.Key] = v;
                        if (v < 0 && (!cs.MinHeld.TryGetValue(mv.Key, out var mn) || v < mn)) cs.MinHeld[mv.Key] = v;
                    }
                }
                foreach (var hk in cs.Held.Where(x => x.Value == 0).Select(x => x.Key).ToList()) cs.Held.Remove(hk);
                foreach (var hk in cs.DaXuLy.Where(x => x.Value == 0).Select(x => x.Key).ToList()) cs.DaXuLy.Remove(hk);
                if (cs.IsEmpty && cs.MinHeld.Count == 0) { row.Cases.Remove(k); continue; }   // [VL8-TIMELINE] giữ vụ từng âm theo thời gian để K5 thấy
                cs.Hops = cs.Hops.OrderBy(h => h.At ?? DateTime.MinValue).ToList();
                var ps = cs.Hops.FirstOrDefault(h => h.Kind == "PS");
                if (inp.CaseInfo.TryGetValue(k, out var ci))
                {
                    cs.CatCode = ci.CatCode; cs.CatName = ci.CatName; cs.Origin = ci.Origin;
                    cs.DetectGroup = ci.DetectGroup; cs.DetectNc = ci.DetectNc; cs.Cause = ci.Cause; cs.At = ci.At; cs.Who = ci.Who;
                }
                else
                {
                    cs.DetectGroup = ps?.Group; cs.At = ps?.At;
                }
                if (cs.Lech != 0)
                    row.G(cs.DetectGroup ?? "KHO").Warnings.Add($"{cs.No}: sinh ra {cs.SinhRa:0.##} khác giữ + chuyển + đã xử lý + huỷ + trả khách (lệch {cs.Lech:0.##}) — báo lại để kiểm");
                // [LAMHO-OFF 2026-11-09] Vụ làm hộ (chiếc của chuỗi Gia công) mà đã vào ô hàng đạt / ô Hàng sửa / Kết quả sửa của nhóm khác Gia công: Workflow ON máy chủ chặn,
                //   OFF lưu được (họ tự kiểm soát) → cảnh báo trên sổ nhóm đó (dữ liệu cũ trước khi có chặn cũng hiện). Không đổi phép tính số.
                //   Nguồn: HÙNG 09/11 08:21 + 09:18 OK (Gói 12).
                if (cs.Id > 0 && cs.IsLamHo)
                {
                    var lot = cs.DaXuLy.Where(x => x.Key != "GC" && x.Value > 0).Select(x => (x.Key, "vào hàng đạt (Hàng lỗi đã xử lý / Hàng mới)"))
                        .Concat(cs.Held.Where(x => x.Value > 0 && x.Key.EndsWith("|" + HandoverCell.Sua) && !x.Key.StartsWith("GC|")).Select(x => (x.Key.Split('|')[0], "ở ô Hàng sửa")))
                        .Concat(cs.Hops.Where(h => h.Kind == "SUA" && h.Group != "GC").Select(h => (h.Group, "có Kết quả sửa")))
                        .GroupBy(x => x.Item1).ToList();
                    foreach (var gl in lot)
                        row.G(gl.Key).Warnings.Add($"{cs.No} là {cs.LamHoTen}: {string.Join(", ", gl.Select(x => x.Item2).Distinct())} ở {GroupLabel(gl.Key)}"
                            + " — chiếc của chuỗi Gia công chưa làm các NC sau, chỉ Gia công sửa"
                            + (row.ApplyWorkflow ? " (nhập lúc Workflow OFF / dữ liệu cũ — Gia công kiểm lại)" : " (Workflow OFF không chặn — nhóm tự kiểm soát)"));   // rà độc lập Gói 12 mục 1
                }
            }
            foreach (var code in GroupCodes)
            {
                var G = row.Groups[code];
                var vl = row.Cases.Values.Sum(x => x.HeldAt(code, HandoverCell.Loi));
                var vs = row.Cases.Values.Sum(x => x.HeldAt(code, HandoverCell.Sua));
                if (vl != G.LoiGiu) G.Warnings.Add($"{G.Label}: tổng vụ lỗi đang giữ ô Hàng lỗi {vl:0.##} khác Tồn hàng lỗi {G.LoiGiu:0.##} — báo lại để kiểm");
                if (vs != G.SuaGiu) G.Warnings.Add($"{G.Label}: tổng vụ lỗi đang giữ ô Hàng sửa {vs:0.##} khác Tồn hàng sửa {G.SuaGiu:0.##} — báo lại để kiểm");
            }

            // ── Chi tiết NC / mã CV ──────────────────────────────────────
            BuildGcSteps(inp, gcChain, row, acc, recvNgSx["GC"]);
            foreach (var g in new[] { "HTSP", "KCS", "PKG" })
                FillCodeWaiting(row, g, acc, recvNgSx[g]);

            // ── Cân bằng PO (L6) ─────────────────────────────────────────
            var b = new LedgerExplain
            {
                Title = "Cân bằng PO", Rule = "L6",
                // [GN-RECV-NG] mục 37: hàng chờ nhận đã nằm trong Tồn bên nhận; chỉ còn thành phẩm chờ Kho nhận đứng riêng
                Formula = "Phôi nhận + Khách gửi bù = Tồn Kho + Tồn GC + Tồn HTSP + Tồn KCS + Tồn Đóng gói + Thành phẩm chờ Kho nhận + Thành phẩm nhập kho + Đã huỷ + Đã trả khách",   // [KHACH-GUI-LAI] + bù
                Note = "Mọi số đều tính từ chứng từ nên dòng này luôn phải cân. Chỗ nhập sai sẽ lộ ra ở ô âm (tô đỏ) của từng nhóm. Tồn mỗi nhóm đã gồm hàng chờ nhận.",
                Result = row.TonTong + row.DangGiao + row.TpNhap + row.DaHuy + row.TraKhach
            };
            foreach (var c in GroupCodes) b.Terms.Add(new LedgerTerm { Label = "Tồn " + GroupLabel(c), Value = row.G(c).Ton, Sign = 1 });
            b.Terms.Add(new LedgerTerm { Label = "Thành phẩm chờ Kho nhận", Value = row.DangGiao, Sign = 1 });   // [GN-RECV-NG] trước: "Đang giao (phiếu chưa nhận)"
            b.Terms.Add(new LedgerTerm { Label = "Thành phẩm nhập kho", Value = row.TpNhap, Sign = 1 });
            // [GN-NG-OUT] Hàng lỗi đã ra khỏi PO
            b.Terms.Add(new LedgerTerm { Label = "Đã huỷ (hàng lỗi)", Value = row.DaHuy, Sign = 1, Docs = row.Groups.Keys.SelectMany(g => acc.TryGetValue((g, "glHuy"), out var x) ? x.Docs : new List<LedgerDoc>()).ToList() });
            b.Terms.Add(new LedgerTerm { Label = "Đã trả khách (hàng lỗi)", Value = row.TraKhach, Sign = 1, Docs = row.Groups.Keys.SelectMany(g => acc.TryGetValue((g, "glKhach"), out var x) ? x.Docs : new List<LedgerDoc>()).ToList() });
            row.BalanceExplain = b;

            return row;
        }

        // ================================================================
        // [GN-CELL] SỔ THEO TỪNG BƯỚC — đúng kiểu Excel xưởng (mỗi dòng B1, B2, B3… = trạng thái SAU một chứng từ)
        //   Không có công thức riêng: mỗi dòng gọi lại Compute() với các chứng từ tính đến bước đó.
        // ================================================================

        public async Task<List<LedgerStepRow>> GetStepsForDetailAsync(int khPlanDetailId)
        {
            var details = await _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer)
                .Where(d => d.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync();
            if (!details.Any()) return new List<LedgerStepRow>();
            var inputs = await LoadInputsAsync(details);
            return ComputeSteps(inputs[0]);
        }

        /// <summary>
        /// [GN-NG-TRACE 2026-10-28] Đường đi hàng lỗi của 1 PO (chỉ đọc): chứng từ của sổ + lý do lỗi trên WTS + người lập / nhận phiếu.
        /// Tính bằng HandoverNgTrace.Build — không công thức số riêng; số đang giữ đọc từ ô Tồn hàng lỗi / Tồn hàng sửa của sổ.
        /// </summary>
        public async Task<NgTrace?> GetNgTraceAsync(int khPlanDetailId)
        {
            var inp = await GetInputForDetailAsync(khPlanDetailId);
            if (inp == null) return null;
            var extra = new NgTraceExtra();
            var wr = await _db.WtsProductionLogs
                .Where(w => w.KhPlanDetailId == khPlanDetailId && !w.IsVoided && w.QtyNg != null && w.QtyNg != 0)
                .Select(w => new { w.WtsLogId, w.NgReason, w.NgCaseId }).AsNoTracking().ToListAsync();   // [DS-LOI-LIVE] + vụ
            // [DS-LOI-LIVE 2026-10-28] dòng WTS có mã vụ → tên loại lỗi hiện tại (CaseInfo đã nạp tên mới ở LoadInputsAsync)
            var wLbl = inp.CaseInfo.Where(kv => kv.Value.CatCode != null).ToDictionary(kv => kv.Key, kv => NgCaseService.LabelOf(kv.Value.CatCode, kv.Value.CatName));
            foreach (var w in wr) extra.WtsReason[w.WtsLogId] = NgCaseService.LiveReason(w.NgCaseId, w.NgReason, wLbl);
            var tw = await _db.HandoverTransactions
                .Where(t => t.KhPlanDetailId == khPlanDetailId && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, Who = t.IssuedByUser != null ? (t.IssuedByUser.FullName ?? t.IssuedByUser.Username) : null })
                .AsNoTracking().ToListAsync();
            foreach (var t in tw) extra.TxWho[t.HandoverTxId] = t.Who;
            var rw = await _db.HandoverReceives
                .Where(r => r.Transaction.KhPlanDetailId == khPlanDetailId && !r.IsVoided)
                .Select(r => new { r.ReceiveId, Who = r.ReceivedByUser != null ? (r.ReceivedByUser.FullName ?? r.ReceivedByUser.Username) : null })
                .AsNoTracking().ToListAsync();
            foreach (var r in rw) extra.RecvWho[r.ReceiveId] = r.Who;
            var trace = HandoverNgTrace.Build(inp, extra);
            // [ML16-ACT 2026-10-30] Master list 11: giờ sửa hàng ("Việc không ra sản phẩm" ghi PO / vụ) — chỉ đọc thêm để hiển thị,
            //   không đổi phép tính hàng lỗi (L6). Dòng đã huỷ không tính.
            var gs = await _db.WorkerActivityLogs
                .Where(a => a.RefKhPlanDetailId == khPlanDetailId && !a.IsVoided)
                .Select(a => new { a.NgCaseId, a.StartTime, a.EndTime, a.WtsCode, Who = a.Worker != null ? (a.Worker.FullName ?? a.Worker.Username) : null })
                .AsNoTracking().ToListAsync();
            if (gs.Count > 0)
            {
                int Min(DateTime s0, DateTime e0) => e0 > s0 ? (int)Math.Round((e0 - s0).TotalMinutes) : 0;
                trace.GioSuaPo      = gs.Sum(a => Min(a.StartTime, a.EndTime));
                trace.GioSuaKhongVu = gs.Where(a => a.NgCaseId == null).Sum(a => Min(a.StartTime, a.EndTime));
                foreach (var c in trace.Cases)
                {
                    var mine = gs.Where(a => a.NgCaseId == c.Id).OrderBy(a => a.StartTime).ToList();
                    if (mine.Count == 0) continue;
                    c.GioSua     = mine.Sum(a => Min(a.StartTime, a.EndTime));
                    c.GioSuaText = string.Join(" | ", mine.Select(a => $"{a.StartTime:dd/MM HH:mm}–{a.EndTime:HH:mm} {a.WtsCode} {a.Who}"));
                }
            }
            return trace;
        }

        public static List<LedgerStepRow> ComputeSteps(LedgerInput inp)
        {
            var gcLastNc = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).LastOrDefault()?.NC;
            var partial = new LedgerInput
            {
                KhPlanDetailId = inp.KhPlanDetailId, PlanNo = inp.PlanNo, PurchaseOrder = inp.PurchaseOrder, PartNo = inp.PartNo,
                CustomerName = inp.CustomerName, PlanQty = inp.PlanQty, ApplyWorkflow = inp.ApplyWorkflow,
                GcSteps = inp.GcSteps, CodeSteps = inp.CodeSteps,
                CaseInfo = inp.CaseInfo,   // [VULOI] nhãn vụ lỗi
                TaroGops = inp.TaroGops,   // [TARO-3T-GOP 2026-11-06]
            };
            // [GC-SUA-TAI-CHO] Kết quả sửa: bước "lập" thêm chứng từ (chưa có lần xác nhận), mỗi lần KCS xác nhận là 1 bước
            var repCopies = new Dictionary<long, InRepair>();
            InRepair RCopy(InRepair r)
            {
                if (!repCopies.TryGetValue(r.Id, out var c))
                {
                    c = r.CloneRep(); c.Checks = new List<InRepairCheck>();
                    repCopies[r.Id] = c;
                    partial.Repairs.Add(c);
                }
                return c;
            }
            var copies = new Dictionary<long, InTx>();
            InTx Copy(InTx t)
            {
                if (!copies.TryGetValue(t.Id, out var c))
                {
                    c = new InTx
                    {
                        Id = t.Id, From = t.From, To = t.To, Purpose = t.Purpose, FromCell = t.FromCell, IsReturnNg = t.IsReturnNg,
                        Issued = t.Issued + t.Closed,   // [GN-VOID-RULE] lúc lập phiếu: số gốc; bước "huỷ phần chưa nhận" mới trừ
                        Status = HandoverPurpose.IsOut(t.Purpose) ? t.Status : "PENDING",
                        FromNC = t.FromNC, ToNC = t.ToNC, At = t.At, Notes = t.Notes, NgReason = t.NgReason,
                        SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                        ToCell = t.ToCell, // [GN-CELL2]
                        NgCaseId = t.NgCaseId, // [VULOI]
                    };
                    copies[t.Id] = c;
                    partial.Txs.Add(c);
                }
                return c;
            }

            var events = new List<(DateTime at, int order, long id, Action apply, LedgerStepRow meta)>();
            foreach (var p in inp.Phoi)
                events.Add((p.At ?? DateTime.MinValue, 0, p.Id, () => partial.Phoi.Add(p), new LedgerStepRow
                {
                    Kind = "PHOI", Group = "KHO", Cell = HandoverCell.Moi, Qty = p.Qty, RefId = p.Id,
                    Text = $"Kho nhận phôi {p.Qty:0.##}" + (string.IsNullOrEmpty(p.Note) ? "" : $" · {p.Note}")
                }));
            // [KHACH-GUI-LAI 2026-11-11] Khách gửi bù: 1 bước "Khách gửi bù n cho VL…" — Kho Nhận · Hàng lỗi đã xử lý (phôi còn)
            foreach (var b in inp.Bu)
            {
                var bb = b;
                events.Add((b.At, 0, b.Id, () => partial.Bu.Add(bb), new LedgerStepRow
                {
                    Kind = "BU", Group = "KHO", Cell = HandoverCell.Xl, Qty = b.Qty, RefId = b.Id,
                    Text = BuText(b)
                }));
            }
            // [GN-RECV-NG] Phiếu trả tự lập (mục 38) không có bước Giao riêng: nó sinh ra cùng lúc với lần nhận gốc
            var autoBySrc = new Dictionary<long, InTx>();
            foreach (var a in inp.Txs.Where(x => x.SourceReceiveId != null && !HandoverPurpose.IsOut(x.Purpose)))
                autoBySrc.TryAdd(a.SourceReceiveId!.Value, a);
            var recvIds = inp.Txs.SelectMany(x => x.Receives).Select(r => r.Id).ToHashSet();
            foreach (var t in inp.Txs)
            {
                var purpose = Classify(t, gcLastNc);
                var cell = HandoverCell.IssueCellOf(purpose, t.FromCell);
                var tx = t;
                bool isAuto = t.SourceReceiveId != null && recvIds.Contains(t.SourceReceiveId.Value);   // [GN-RECV-NG]
                string giaoText = purpose switch
                {
                    HandoverPurpose.Scrap      => $"{GroupLabel(t.From)} huỷ {t.Issued:0.##} · ô Hàng lỗi",
                    HandoverPurpose.CustReturn => $"{GroupLabel(t.From)} trả khách {t.Issued:0.##} · ô Hàng lỗi",
                    HandoverPurpose.HelpSend when IsKhoTaro(t, purpose)   // [KHO-TARO-THANG]
                                               => $"Kho giao thẳng Taro {t.Issued:0.##} → {GroupLabel(t.To)} (Gia công tính nhận vào + giao theo NC cùng số khi Taro nhận)",
                    HandoverPurpose.HelpSend   => $"{GroupLabel(t.From)} giao theo NC {t.Issued:0.##}" + (string.IsNullOrEmpty(t.FromNC) ? "" : $" ({NcText(t.FromNC)})") + $" → {GroupLabel(t.To)}",   // [N3-FIRST] chữ @VAO
                    HandoverPurpose.HelpReturn when HandoverCell.IsLamHoLoi(purpose, t.FromCell)   // [LAMHO-B-CHU 2026-11-07] trả hàng làm hộ bị lỗi (mang vụ) — trước chữ như trả hàng tốt
                                               => $"{GroupLabel(t.From)} trả hàng làm hộ bị lỗi {t.Issued:0.##} → {GroupLabel(t.To)} ô Hàng lỗi (cùng vụ)",
                    HandoverPurpose.HelpReturn => $"{GroupLabel(t.From)} trả về Gia công {t.Issued:0.##} → {GroupLabel(t.To)}" + (string.IsNullOrEmpty(t.ToNC) ? "" : $" (vào NC {t.ToNC})"),
                    _ => $"{GroupLabel(t.From)} giao {t.Issued:0.##} · ô {HandoverCell.Label(cell)}"
                         + (purpose == HandoverPurpose.NgReturn ? " ☑ lỗi công đoạn trước" : "")
                         + (purpose == HandoverPurpose.NgReturn && !string.IsNullOrEmpty(t.FromNC) ? (IsTaroPhoiLoi(t, purpose) ? $" ({TaroPhoiLoiFrom})" : $" (phát hiện ở {NcText(t.FromNC)})") : "")   // [GN-NC-PIN] [N3-FIRST] chữ @VAO · [KTT-PHOILOI]
                         + $" → {GroupLabel(t.To)} ô {HandoverCell.Label(DesigCellOf(t, purpose))}"   // [GN-CELL2] ô bên giao chỉ định
                         + (string.IsNullOrEmpty(t.ToNC) ? "" : $" (vào NC {t.ToNC})")
                };
                if (!isAuto)   // [GN-RECV-NG]
                    events.Add((t.At, 1, t.Id, () => Copy(tx), new LedgerStepRow
                    {
                        Kind = "GIAO", Group = t.From, Other = t.To, Cell = cell, PrevStageNg = purpose == HandoverPurpose.NgReturn,
                        Qty = t.Issued, RefId = t.Id, Text = giaoText + $" · phiếu #{t.Id}"
                    }));
                var desigCell = DesigCellOf(t, purpose);   // [GN-CELL2]
                var recs = t.Receives.OrderBy(r => r.At).ToList();
                for (int i = 0; i < recs.Count; i++)
                {
                    var r = recs[i];
                    bool last = i == recs.Count - 1;
                    var rr = r; var ttx = t;
                    autoBySrc.TryGetValue(r.Id, out var back);   // [GN-RECV-NG] phiếu trả tự lập từ lần nhận này
                    var recCell = HandoverCell.IsReceiveCell(r.Cell) ? r.Cell : desigCell;   // [GN-CELL2] ô bên nhận thực chọn
                    events.Add((r.At, 2, r.Id, () =>
                    {
                        var c = Copy(ttx);
                        c.Receives.Add(rr);
                        if (ttx.Closed > 0) c.Status = StepStatus(c);   // [GN-VOID-RULE] phiếu đã huỷ phần chưa nhận: trạng thái theo số gốc tới bước đóng
                        else if (last) c.Status = ttx.Status;
                        else if (c.Status == "PENDING") c.Status = "PARTIAL";
                        if (back != null) Copy(back);   // [GN-RECV-NG] phiếu trả sinh cùng lúc, chờ bên giao nhận
                    }, new LedgerStepRow
                    {
                        Kind = "NHAN", Group = t.To, Other = t.From, Cell = recCell, Qty = r.Ok + r.Ng, Ng = r.Ng, RefId = r.Id,
                        // [LAMHO-B-CHU 2026-11-07] nhận phiếu trả hàng làm hộ bị lỗi: cả phiếu là hàng lỗi vào ô Hàng lỗi cùng vụ — trước ghi "(NG 2 trả ngay)"
                        //   (thử live 07/11 PO TEST-0711-TARO dòng B13) dù không có phiếu trả nào. Nguồn: CLAUDE (thử live 07/11); Hùng 07/11 22:38 giao Gói 8.
                        Text = HandoverCell.IsLamHoLoi(purpose, t.FromCell)
                            ? $"{GroupLabel(t.To)} nhận {r.Ok + r.Ng:0.##} hàng làm hộ bị lỗi · ô Hàng lỗi (cùng vụ) · từ {GroupLabel(t.From)} · phiếu #{t.Id}"
                            : purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn
                            ? $"{GroupLabel(t.To)} nhận {r.Ok + r.Ng:0.##} " + (IsKhoTaro(t, purpose) ? "phôi Kho giao thẳng Taro" : "giao theo NC") + (r.Ng > 0 ? (back != null ? $" (NG {r.Ng:0.##} → phiếu trả #{back.Id})" : $" (NG {r.Ng:0.##} trả ngay)") : "") + $" · từ {GroupLabel(t.From)} · phiếu #{t.Id}"   // [KHO-TARO-THANG] chữ dòng B nhận phiếu Kho giao thẳng
                            : $"{GroupLabel(t.To)} nhận {r.Ok + r.Ng:0.##} · ô {HandoverCell.Label(recCell)}"
                              + (recCell is HandoverCell.Xl ? XlRecvNote(inp, t, recCell) : "")   // [XL-CHU-B] hàng đã sửa: giữ / đã làm — không WTS · [SUA-HO-TRA-NGUOC] Gia công: xong NC
                              + CellChangeNote(desigCell, r)   // [GN-CELL2]
                              + (!string.IsNullOrEmpty(r.ToNC) ? $" · vào {NcText(r.ToNC)}" : "")   // [NHAN-DOI-O-NC] NC của lần nhận
                              + (r.Ng > 0 && recCell != HandoverCell.Loi ? (back != null   // [GN-RECV-NG2][GN-CELL2] nhận vào ô Hàng lỗi: số ở ô NG là hàng lỗi nhận, không phải "NG lúc nhận"
                                    ? $" · NG lúc nhận {r.Ng:0.##} → phiếu trả #{back.Id} chờ {GroupLabel(t.From)} nhận"   // [GN-RECV-NG]
                                    : $" · NG lúc nhận {r.Ng:0.##} trả ngay {GroupLabel(t.From)}") : "")
                              + (isAuto ? " (hàng lỗi trả lại lúc nhận)" : "")   // [GN-RECV-NG]
                              + $" · từ {GroupLabel(t.From)} · phiếu #{t.Id}"
                    }));
                }
            }
            // [GN-VOID-RULE] Bước "huỷ phần chưa nhận": số phiếu về bằng số đã nhận, phần còn lại trả về ô bên giao
            //   [K5-CLOSE-NHIEU 2026-11-06] mỗi lần huỷ (dòng HandoverTxCloses) = 1 bước, đúng giờ lần đó (trước: 1 bước cho tổng, giờ lần cuối)
            foreach (var t in inp.Txs.Where(x => x.Closed > 0))
            {
                var ttx = t;
                var cls = ClosesOf(t);
                for (int k = 0; k < cls.Count; k++)
                {
                    var cq = cls[k].Qty;
                    var left = t.Issued + cls.Skip(k + 1).Sum(x => x.Qty);   // phiếu còn sau lần huỷ này
                    events.Add((cls[k].At, 2, t.Id, () =>
                    {
                        var c = Copy(ttx);
                        c.Issued -= cq;
                        c.Status = StepStatus(c);
                    }, new LedgerStepRow
                    {
                        Kind = "DONG", Group = t.From, Other = t.To, Cell = HandoverCell.IssueCellOf(Classify(t, gcLastNc), t.FromCell), Qty = -cq, RefId = t.Id,
                        Text = $"{GroupLabel(t.From)} huỷ phần chưa nhận {cq:0.##} của phiếu #{t.Id} (phiếu còn {left:0.##})"
                             + (cls.Count > 1 ? $" · lần {k + 1}/{cls.Count}" : "") + $" · hàng về lại {GroupLabel(t.From)}"
                    }));
                }
            }
            // [GN-NG-AFTER] Báo hàng lỗi phát hiện sau khi nhận
            foreach (var b in inp.NgReps)
            {
                var bb = b;
                events.Add((b.At, 4, b.Id, () => partial.NgReps.Add(bb), new LedgerStepRow
                {
                    Kind = "BAOLOI", Group = b.Group, Cell = HandoverCell.Loi, Qty = b.Qty, RefId = b.Id,
                    Text = NgRepText(b)
                }));
            }
            // [GC-SUA-TAI-CHO] Kết quả sửa + KCS xác nhận
            foreach (var r in inp.Repairs)
            {
                var rr0 = r;
                events.Add((r.At, 5, r.Id, () => RCopy(rr0), new LedgerStepRow
                {
                    Kind = "KQSUA", Group = r.Group, Cell = r.Cell, Qty = r.Pass + r.Fail, Ng = r.Fail, RefId = r.Id,
                    Text = $"{GroupLabel(r.Group)} ghi Kết quả sửa {r.Pass + r.Fail:0.##} · ô {HandoverCell.Label(r.Cell)}"
                           + (r.NgCaseId is long v0 && v0 > 0 ? $" · {NgCaseNo.Format(v0)}" : "")
                           + $" · đạt {r.Pass:0.##} chờ KCS" + (r.Fail != 0 ? $" · không đạt {r.Fail:0.##} → ô Hàng lỗi" : "")
                           + (string.IsNullOrEmpty(r.Who) ? "" : $" · {r.Who}") + $" · Kết quả sửa #{r.Id}"
                }));
                foreach (var c in r.Checks)
                {
                    var cc = c;
                    events.Add((c.At, 6, c.Id, () => RCopy(rr0).Checks.Add(cc), new LedgerStepRow
                    {
                        Kind = "XNSUA", Group = r.Group, Other = "KCS", Cell = HandoverCell.Xl, Qty = c.Ok + c.Ng, Ng = c.Ng, RefId = c.Id,
                        Text = $"KCS xác nhận hàng {GroupLabel(r.Group)} sửa: đạt {c.Ok:0.##} → ô Hàng lỗi đã xử lý" + XlStepNote(RepairDest(inp, r), r.Group)   // [XL-CHU-B]
                               + (r.Group == "GC" && !string.IsNullOrEmpty(r.ToNC) && !IsRepMark(r.ToNC) && c.Ok != 0 ? $" vào {NcText(r.ToNC)}" : "")   // [XL-CHU-B] dấu không in "vào NC @XONG"
                               + (c.Ng != 0 ? $" · loại {c.Ng:0.##} → ô Hàng lỗi" : "")
                               + (string.IsNullOrEmpty(c.Who) ? "" : $" · {c.Who}") + $" · Kết quả sửa #{r.Id}"
                    }));
                }
            }
            foreach (var w in inp.Wts)
            {
                var g = Pg2Group(w.ProcessGroup);
                if (g == "") continue;
                var ww = w;
                events.Add((w.At, 3, w.Id, () => partial.Wts.Add(ww), new LedgerStepRow
                {
                    Kind = "WTS", Group = g, Qty = w.Ok, Ng = w.Ng, RefId = w.Id,
                    Text = $"{GroupLabel(g)} WTS {(w.ProcessGroup == "GC" ? "NC " : SubLabel(w.ProcessGroup) + " ")}{w.WtsCode ?? w.NC} · OK {w.Ok:0.##}" + (w.Ng > 0 ? $" · NG {w.Ng:0.##}" : "")
                           + (string.IsNullOrEmpty(w.Worker) ? "" : $" · {w.Worker}")
                }));
            }

            var rows = new List<LedgerStepRow>();
            int idx = 0;
            foreach (var ev in events.OrderBy(e => e.at).ThenBy(e => e.order).ThenBy(e => e.id))
            {
                ev.apply();
                ev.meta.Index = ++idx;
                ev.meta.At = ev.at == DateTime.MinValue ? null : ev.at;
                ev.meta.Row = Compute(partial);
                rows.Add(ev.meta);
            }
            return rows;
        }

        /// <summary>[GN-VOID-RULE] Trạng thái phiếu theo số đã nhận tới bước đang dựng (dòng B).</summary>
        static string StepStatus(InTx c)
        {
            var rec = c.Receives.Sum(x => x.Ok + x.Ng);
            return rec <= 0 ? "PENDING" : rec >= c.Issued ? "COMPLETED" : "PARTIAL";
        }

        static List<LedgerDoc> AllDocs(Dictionary<(string g, string key), LedgerTerm> acc, string g, params string[] keys)
            => keys.SelectMany(k => acc.TryGetValue((g, k), out var t) ? t.Docs : new List<LedgerDoc>()).OrderBy(d => d.At).ToList();

        // ── GC: Đã làm = OK NC cuối (N1, N2) ───────────────────────────
        static void ComputeGcOk(LedgerInput inp, List<InGcStep> chain, LedgerRow row,
                                Func<string, string, string, LedgerTerm> T)
        {
            var logs = inp.Wts.Where(w => w.ProcessGroup == "GC").ToList();
            if (!chain.Any())
            {
                var t0 = T("GC", "ok", "Đã làm (WTS OK, chưa có snapshot NC)");
                foreach (var w in logs.Where(w => w.Ok != 0))
                {
                    t0.Value += w.Ok;
                    t0.Docs.Add(new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok });
                }
                if (logs.Any()) row.G("GC").Warnings.Add("Gia công: PO chưa có snapshot NC — Đã làm đang cộng mọi NC, cần tạo snapshot");
                return;
            }
            var last = chain.Last();
            var codes = NcCodes(inp, last.NC);
            var t = T("GC", "ok", $"OK NC cuối ({last.NC})");
            foreach (var w in logs.Where(w => codes.Contains(w.NC ?? "") && w.Ok != 0))
            {
                t.Value += w.Ok;
                t.Docs.Add(new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok });
            }
        }

        /// <summary>NC chính + các máy đồng dạng (IsBackup, ParentNC = NC chính) — N2.</summary>
        static HashSet<string> NcCodes(LedgerInput inp, string nc)
        {
            var set = inp.GcSteps.Where(s => s.IsBackup && string.Equals(s.ParentNC, nc, StringComparison.OrdinalIgnoreCase))
                                 .Select(s => s.NC).ToHashSet(StringComparer.OrdinalIgnoreCase);
            set.Add(nc);
            return set;
        }

        // ── HTSP / KCS / PKG: Đã làm = min OK các mã CV chính (N5) ──────
        static void ComputeCodeOk(LedgerInput inp, string group, LedgerRow row,
                                  Func<string, string, string, LedgerTerm> T)
        {
            var pgs  = group == "HTSP" ? new[] { "TARO", "BAVIA", "WASHING" } : new[] { group };
            var logs = inp.Wts.Where(w => pgs.Contains(w.ProcessGroup)).ToList();
            var steps = inp.CodeSteps.Where(s => pgs.Contains(s.Pg)).ToList();
            var mains = steps.Where(s => !s.IsBackup && !IsPrepCode(s.Code)).ToList();
            var G = row.G(group);

            if (!mains.Any())
            {
                var t0 = T(group, "ok", "Đã làm (WTS OK, chưa có snapshot mã CV)");
                foreach (var w in logs.Where(w => w.Ok != 0 && !IsPrepCode(w.WtsCode ?? w.NC)))
                {
                    t0.Value += w.Ok;
                    t0.Docs.Add(new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok });
                }
                if (logs.Any()) G.Warnings.Add($"{G.Label}: PO chưa có snapshot mã CV — Đã làm đang cộng mọi mã, cần tạo snapshot");
                return;
            }

            // [TARO-3T-GOP 2026-11-06] Bộ gộp Taro: mã gộp (vd 3T) + các mã thành phần (3T1, 3T2, 3T3) cùng NC, theo khai báo chụp vào PO.
            //   Mỗi chiếc đi 1 trong 2 cách → OK của bộ = OK mã gộp + min OK các mã thành phần (thay cho OK từng mã khi lấy OK nhỏ nhất).
            //   Nguồn: KHÁCH 02/11 09:14 (3T thay bộ 3T1–3T3); HÙNG 05/11 15:37 OK "3T = min(3T1..3T3)" (cách tính gửi 03/11).
            var units = group == "HTSP" ? TaroGopUnits(inp, mains, G) : new List<(InCodeStep Gop, List<InCodeStep> Parts)>();
            var lamHo = group == "HTSP" ? LamHoCodes(inp, row.LamHoRuns) : new Dictionary<string, string>();   // [TARO-LAMHO-NC]
            if (group == "HTSP")
            {   // [LAMHO-NCKEY 2026-11-08] NC Taro khớp 2 NC Gia công khác chữ (vd "3" / "NC003") → không ghép làm hộ; báo để sửa Part Master. Chỉ cảnh báo, không đổi số.
                //   Chỉ khi khoá trùng có NC thuộc một ĐOẠN làm hộ (NC máy loại trừ còn NC thường phía sau — row.LamHoRuns) — 2 NC máy thường cùng khoá,
                //   hay NC loại trừ cuối chuỗi, vốn không có làm hộ → không cảnh báo (rà độc lập Gói 11 L2 + vòng 2 Th1: tránh badge đỏ "lỗi" giả).
                var amb = AmbiguousNcKeys(inp);
                var runKeys = row.LamHoRuns.SelectMany(r => r.Ncs).Select(NcKey).ToHashSet();
                amb.RemoveWhere(k => !runKeys.Contains(k));
                foreach (var g in steps.Where(s => s.Pg == "TARO" && !s.IsBackup && !string.IsNullOrWhiteSpace(s.StepNc) && amb.Contains(NcKey(s.StepNc)))
                                       .GroupBy(s => s.StepNc!.Trim(), StringComparer.OrdinalIgnoreCase))
                    G.Warnings.Add($"{TaroNcLechHead}: NC Taro \"{g.Key}\" ({string.Join(", ", g.Select(s => s.Code).Distinct())}) khớp "
                        + string.Join(" / ", inp.GcSteps.Where(s => !s.IsBackup && NcKey(s.NC) == NcKey(g.Key)).Select(s => NcName(s.NC.Trim())).Distinct())
                        + " — không coi là Taro làm hộ, tính như mã HTSP thường; sửa tên NC ở Part Master cho khớp đúng 1 NC");
            }
            var info = new Dictionary<InCodeStep, (decimal ok, List<InWts> rows, LedgerStepLine line)>(ReferenceEqualityComparer.Instance);
            foreach (var m in steps.OrderBy(s => Array.IndexOf(pgs, s.Pg)).ThenBy(s => s.StepOrder))
            {
                if (m.IsBackup) continue;
                var codes = steps.Where(b => b.IsBackup && b.Pg == m.Pg && string.Equals(b.ParentCode, m.Code, StringComparison.OrdinalIgnoreCase))
                                 .Select(b => b.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                codes.Add(m.Code);
                var rows = logs.Where(w => string.Equals(w.ProcessGroup, m.Pg, StringComparison.OrdinalIgnoreCase)
                                        && codes.Contains(w.WtsCode ?? w.NC ?? "")).ToList();
                var ok = rows.Sum(w => w.Ok);
                var ng = rows.Sum(w => w.Ng);
                var prep = IsPrepCode(m.Code);
                var unit = units.FirstOrDefault(u => ReferenceEquals(u.Gop, m) || u.Parts.Any(p => ReferenceEquals(p, m)));
                var line = new LedgerStepLine
                {
                    Pg = m.Pg, // [GN-WTS-CAP]
                    SubGroup = SubLabel(m.Pg), Code = m.Code, StepOrder = m.StepOrder, Ok = ok, Ng = ng, IsPrep = prep,
                    Note = prep ? "mã chuẩn bị — không tính số lượng" : m.StepName,
                    GopUnit = unit.Gop?.Code, IsGop = unit.Gop != null && ReferenceEquals(unit.Gop, m),   // [TARO-3T-GOP]
                    LamHoNc = m.Pg == "TARO" && lamHo.TryGetValue(m.Code.Trim(), out var lhNc) ? lhNc : null,   // [TARO-LAMHO-NC]
                };
                if (line.LamHoNc != null)
                    line.Note = (line.Note == null ? "" : line.Note + " · ") + $"Taro làm hộ {NcName(line.LamHoNc)} (máy loại trừ giữa chuỗi Gia công) — không tính vào Đã làm của HTSP";
                if (unit.Gop != null)
                    line.Note = (line.Note == null ? "" : line.Note + " · ") + (line.IsGop
                        ? $"mã gộp — làm thay {string.Join(" + ", unit.Parts.Select(p => p.Code))}"
                        : $"thuộc bộ gộp {unit.Gop.Code} (làm {unit.Gop.Code} thì không làm mã này)");
                G.Steps.Add(line);
                info[m] = (ok, rows, line);
            }
            // OK nhỏ nhất: mã thường = OK mã; bộ gộp = OK mã gộp + min OK mã thành phần (xét ở vị trí mã đầu tiên của bộ, cùng thứ tự như cũ)
            (decimal ok, List<InWts> rows, List<LedgerStepLine> marks, string label)? min = null;
            var unitDone = new HashSet<InCodeStep>(ReferenceEqualityComparer.Instance);
            // [TARO-LAMHO-NC] OK nhỏ nhất riêng từng NC Taro làm hộ (cùng cách tính, không vào Đã làm của HTSP)
            var lhMin = new Dictionary<string, (decimal ok, List<InWts> rows)>(StringComparer.OrdinalIgnoreCase);
            void LhTake(string nc, decimal ok, List<InWts> rows) { if (!lhMin.TryGetValue(nc, out var cur) || ok < cur.ok) lhMin[nc] = (ok, rows); }
            foreach (var m in steps.OrderBy(s => Array.IndexOf(pgs, s.Pg)).ThenBy(s => s.StepOrder))
            {
                if (m.IsBackup || IsPrepCode(m.Code)) continue;
                var unit = units.FirstOrDefault(u => ReferenceEquals(u.Gop, m) || u.Parts.Any(p => ReferenceEquals(p, m)));
                if (unit.Gop == null)
                {
                    var x = info[m];
                    if (x.line.LamHoNc != null) { LhTake(x.line.LamHoNc, x.ok, x.rows); continue; }   // [TARO-LAMHO-NC]
                    if (min == null || x.ok < min.Value.ok) min = (x.ok, x.rows, new List<LedgerStepLine> { x.line }, $"{SubLabel(m.Pg)} {m.Code}");
                    continue;
                }
                if (!unitDone.Add(unit.Gop)) continue;
                var g = info[unit.Gop];
                var pMin = unit.Parts.Select(p => info[p]).OrderBy(p => p.ok).ThenBy(p => p.line.StepOrder).First();
                var uOk = g.ok + pMin.ok;
                if (g.line.LamHoNc != null) { LhTake(g.line.LamHoNc, uOk, g.rows.Concat(pMin.rows).ToList()); continue; }   // [TARO-LAMHO-NC] bộ gộp cùng NC
                if (min == null || uOk < min.Value.ok)
                    min = (uOk, g.rows.Concat(pMin.rows).ToList(), new List<LedgerStepLine> { g.line, pMin.line },
                           $"{SubLabel(m.Pg)} {unit.Gop.Code} + {pMin.line.Code} (gộp)");
            }
            // [TARO-LAMHO-NC] OK bộ Taro của đoạn làm hộ = nhỏ nhất giữa các NC của đoạn có mã Taro
            foreach (var r in row.LamHoRuns)
            {
                var have = r.Ncs.Where(n => lhMin.ContainsKey(n)).Select(n => lhMin[n]).OrderBy(v => v.ok).ToList();
                if (have.Count == 0) continue;
                r.Done = have[0].ok;
                r.DoneDocs = have[0].rows.Where(w => w.Ok != 0).Select(w => new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok }).ToList();
            }
            if (min == null)
            {   // [TARO-LAMHO-NC] nhóm chỉ có mã Taro làm hộ: HTSP không có phần việc của mình trên PO này → Đã làm = 0
                T(group, "ok", "OK nhỏ nhất: (không có mã CV chính ngoài Taro làm hộ)");
                return;
            }
            var t = T(group, "ok", $"OK nhỏ nhất: {min.Value.label}");
            t.Value = min.Value.ok;
            foreach (var w in min.Value.rows.Where(w => w.Ok != 0))
                t.Docs.Add(new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok });
            foreach (var mk in min.Value.marks)
                foreach (var s in G.Steps.Where(s => !s.IsPrep && s.Code == mk.Code && s.SubGroup == mk.SubGroup))
                    s.IsLast = true; // đánh dấu mã đang quyết định Đã làm
        }

        /// <summary>[TARO-3T-GOP 2026-11-06] Các bộ gộp Taro của PO: mã gộp + mã thành phần CÙNG NC (đều là dòng chính, không mã chuẩn bị) theo
        /// khai báo chụp vào PO. 1 mã chỉ thuộc 1 bộ (khai báo lồng / chồng → bộ đầu, cảnh báo). Bộ không có mã thành phần nào trên PO = mã thường.</summary>
        static List<(InCodeStep Gop, List<InCodeStep> Parts)> TaroGopUnits(LedgerInput inp, List<InCodeStep> mains, LedgerGroup G)
        {
            var res = new List<(InCodeStep Gop, List<InCodeStep> Parts)>();
            if (inp.TaroGops.Count == 0) return res;
            var taro = mains.Where(x => x.Pg == "TARO").OrderBy(x => x.StepOrder).ToList();
            var used = new HashSet<InCodeStep>(ReferenceEqualityComparer.Instance);
            foreach (var g in taro)
            {
                var raw = (g.RawCode ?? g.Code).Trim();
                var partCodes = inp.TaroGops.Where(x => string.Equals(x.Gop, raw, StringComparison.OrdinalIgnoreCase)).Select(x => x.Part).ToList();
                if (partCodes.Count == 0) continue;
                var parts = taro.Where(p => !ReferenceEquals(p, g) && NcKey(p.StepNc) == NcKey(g.StepNc)   // [TARO-LAMHO-NC] so NC như đoạn làm hộ ("3" = "NC03") — rà độc lập gói 6 mục 10
                                            && partCodes.Contains((p.RawCode ?? p.Code).Trim(), StringComparer.OrdinalIgnoreCase)).ToList();
                if (parts.Count == 0) continue;
                if (used.Contains(g) || parts.Any(used.Contains))
                {
                    G.Warnings.Add($"HTSP: khai báo gộp Taro mã {g.Code} chồng với bộ gộp khác — bỏ qua, tính như mã thường (sửa WTS tiêu chuẩn rồi làm mới quy trình)");
                    continue;
                }
                used.Add(g); foreach (var p in parts) used.Add(p);
                res.Add((g, parts));
            }
            return res;
        }

        // ── GC: dòng chảy theo NC (N1, N3) — chỉ để xem ───────────────
        static void BuildGcSteps(LedgerInput inp, List<InGcStep> chain, LedgerRow row,
                                 Dictionary<(string g, string key), LedgerTerm> acc, decimal recvNg)
        {
            var G = row.G("GC");
            decimal V(string k) => acc.TryGetValue(("GC", k), out var t) ? t.Value : 0m;
            // [GN-WTS-CAP][N10] Phiếu Trả hàng lỗi của GC (phát hiện lỗi khâu trước SAU khi nhận): chiếc lỗi được lấy từ
            // NC đang có hàng chờ, NC đầu trước rồi tới NC sau. Trước: luôn trừ ở NC đầu → NC đầu đã làm xong thì âm giả.
            // [GN-NC-PIN] Phiếu mới chốt NC phát hiện (FromNC) → trừ cố định ở NC đó; chỉ phiếu cũ (không có FromNC) còn trôi theo NC đầu còn hàng chờ.
            var pinnedTotal = inp.GcSteps.Where(s => !s.IsBackup).Select(s => s.NC).Distinct().Sum(nc => V("rrNc:" + nc))
                            + V("rrNc:" + VaoCode);   // [N3-FIRST] trả hàng lỗi chốt ở dòng "Nhận vào (chưa qua NC)"
            var rrLeft = V("glTra") - recvNg - pinnedTotal;
            LedgerStepLine? lastLine = null;
            var logs = inp.Wts.Where(w => w.ProcessGroup == "GC").ToList();
            var all  = inp.GcSteps.Where(s => !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            if (!all.Any()) return;

            // Hàng vào NC đầu = mọi hàng vào sản xuất GC không chỉ định NC − trả hàng lỗi (phát hiện trước khi làm)
            var targeted = chain.Sum(s => V("in:" + s.NC))
                         + V("nxGiu");   // [SUA-XL-GIU] hàng đã sửa giữ ở Tồn hàng lỗi đã xử lý (không vào NC nào)
            decimal prevOk = 0, prevHelpOut = 0;
            bool first = true;
            bool afterExcluded = false; // [GN-WTS-CAP][N3] vừa đi qua 1 NC máy loại trừ

            // [N3-FIRST 2026-11-04] NC ĐẦU chuỗi là máy loại trừ (và sau đó còn NC thường): hàng nhận vào Gia công nằm ở dòng ảo
            //   "@VAO" = Nhận vào (chưa qua NC). Từ đây Gia công giao theo NC cho nhóm làm máy loại trừ; NC thường đầu tiên chỉ nhận
            //   hàng trả về (như NC sau máy loại trừ giữa chuỗi — nhánh afterExcluded bên dưới).
            //   Trước: NC thường đầu tiên nhận thẳng mọi hàng vào Gia công, công đoạn máy loại trừ không có chứng từ, không có NC nguồn để giao theo NC.
            //   Dòng @VAO không có WTS (OK = NG = 0): Nhận vào = như NC đầu cũ; còn chờ = Nhận vào − đã giao theo NC từ đây (K5 canh ô GC|GC|@VAO).
            //   Σ chờ các dòng (gồm @VAO) = Chưa GC khi không có máy loại trừ GIỮA chuỗi (bộ kiểm canh).
            if (all[0].IsExcluded && all.Any(s => !s.IsExcluded))
            {
                var x0 = all[0];
                var vline = new LedgerStepLine
                {
                    Pg = "GC", SubGroup = "GC", Code = VaoCode, StepOrder = x0.StepOrder, Machine = null,
                    HelpOut = V("helpOut:" + VaoCode)
                };
                var vin = G.NhanMoi + G.NhanLoiXl + V("nhRet") - targeted - recvNg;
                vline.Note = $"nhận vào = Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + hàng trả về (không chọn NC) − NG đếm lúc nhận; chưa qua NC — NC đầu {x0.NC} là máy loại trừ ({x0.Machine}): giao theo NC từ đây";
                var vpin = V("rrNc:" + VaoCode);
                if (vpin != 0) { vin -= vpin; vline.Note += $" − trả hàng lỗi phát hiện ở đây {vpin:0.##}"; }
                var vbl = V("blNc:" + VaoCode);
                if (vbl != 0) { vin -= vbl; vline.Note += $" − báo hàng lỗi ở đây {vbl:0.##}"; }
                if (rrLeft > 0)
                {
                    var take = Math.Min(Math.Max(0, vin - vline.HelpOut), rrLeft);   // như NC đầu cũ: phiếu trả lỗi cũ (không chốt NC) lấy hàng đang chờ ở đây trước
                    if (take > 0) { vin -= take; rrLeft -= take; vline.Note += $" − trả hàng lỗi {take:0.##}"; }
                }
                if (vline.HelpOut != 0) vline.Note += $"; đã giao theo NC {vline.HelpOut:0.##}";
                vline.In = vin;
                vline.Waiting = vin - vline.HelpOut;
                if (vline.Waiting < 0)
                    G.Warnings.Add($"Gia công {VaoLabel}: đã giao theo NC ({vline.HelpOut:0.##}) nhiều hơn số nhận vào ({vin:0.##}) — N3");
                lastLine = vline;
                G.Steps.Add(vline);
                first = false;   // NC máy loại trừ ngay sau → afterExcluded → NC thường đầu tiên chỉ nhận hàng trả về
            }
            foreach (var s in all)
            {
                var codes = NcCodes(inp, s.NC);
                var ok = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ok);
                var ng = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ng);
                var okSua = s.IsExcluded ? 0 : V("okSua:" + s.NC);   // [SUA-OK-NC] hàng sửa đạt tính là xong NC này (đã vào NC qua "in:") — không WTS; Ok của dòng giữ = WTS
                var line = new LedgerStepLine
                {
                    Pg = "GC", // [GN-WTS-CAP]
                    SubGroup = "GC", Code = s.NC, StepOrder = s.StepOrder, Machine = s.Machine,
                    IsExcluded = s.IsExcluded, Ok = ok, Ng = ng, IsLast = chain.Count > 0 && s.NC == chain.Last().NC, OkSua = okSua,   // [SUA-OK-NC]
                    HelpOut = V("helpOut:" + s.NC)
                };
                if (s.IsExcluded)
                {
                    line.Note = "máy loại trừ — giao nhóm khác làm, nhận về NC sau (N3)";
                    G.Steps.Add(line);
                    if (!first) afterExcluded = true; // [GN-WTS-CAP][N3]
                    continue;
                }
                decimal input;
                if (first)
                {
                    input = G.NhanMoi + G.NhanLoiXl + V("nhRet") - targeted - recvNg;
                    line.Note = "nhận vào = Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + hàng trả về (không chọn NC) − NG đếm lúc nhận";
                    first = false;
                }
                else if (afterExcluded)
                {
                    // [GN-WTS-CAP][N3] NC ngay sau NC máy loại trừ: chỉ nhận hàng làm hộ trả về (phiếu chọn NC này).
                    // Trước: lấy luôn OK của NC trước NC loại trừ → hàng chưa gửi làm hộ cũng được làm tiếp, sai N3
                    // (code WTS cũ cũng chặn kiểu này: NC sau NC loại trừ chỉ mở khi có hàng nhận về).
                    input = 0;
                    line.Note = "nhận vào = 0 (NC trước là máy loại trừ — chỉ nhận hàng trả về vào NC này)";
                    afterExcluded = false;
                }
                else
                {
                    input = prevOk - prevHelpOut;
                    line.Note = prevHelpOut > 0 ? "nhận vào = OK NC trước − đã giao đi" : "nhận vào = OK NC trước";
                }
                var tin = V("in:" + s.NC);
                if (tin != 0) { input += tin; line.Note += " + hàng chỉ định vào NC này"; }
                if (okSua != 0) line.Note += $"; OK gồm {okSua:0.##} chiếc sửa đạt tính là xong NC này (không WTS)";   // [SUA-OK-NC]
                var pin = V("rrNc:" + s.NC);   // [GN-NC-PIN]
                if (pin != 0) { input -= pin; line.Note += $" − trả hàng lỗi phát hiện ở NC này {pin:0.##}"; }
                var bpin = V("blNc:" + s.NC);   // [GN-NG-AFTER] báo hàng lỗi phát hiện sau khi nhận ở NC này
                if (bpin != 0) { input -= bpin; line.Note += $" − báo hàng lỗi ở NC này {bpin:0.##}"; }
                if (rrLeft > 0)
                {
                    var take = Math.Min(Math.Max(0, input - ok - ng), rrLeft);   // [GN-WTS-CAP][N10]
                    if (take > 0) { input -= take; rrLeft -= take; line.Note += $" − trả hàng lỗi {take:0.##}"; }
                }
                line.In = input;
                lastLine = line;
                line.Waiting = input - ok - okSua - ng;   // [SUA-OK-NC] − hàng sửa đạt tính là xong NC này
                if (line.Waiting < 0)
                    G.Warnings.Add($"Gia công NC {s.NC}: OK + NG ({ok + ng:0.##}) nhiều hơn số nhận vào NC ({input:0.##}) — WTS nhập vượt (L3, N1)");
                G.Steps.Add(line);
                prevOk = ok + okSua;   // [SUA-OK-NC] hàng sửa đạt xong NC này đi tiếp NC sau như hàng OK
                prevHelpOut = V("helpOut:" + s.NC);
            }
            // Trả hàng lỗi nhiều hơn mọi hàng đang chờ (dữ liệu cũ) → ghi vào NC cuối để lộ ra ô âm
            if (rrLeft > 0 && lastLine != null)
            {
                lastLine.In -= rrLeft; lastLine.Waiting -= rrLeft;
                lastLine.Note += $" − trả hàng lỗi {rrLeft:0.##} (vượt hàng đang chờ)";
                G.Warnings.Add($"Gia công: trả hàng lỗi nhiều hơn hàng đang chờ ở các NC ({rrLeft:0.##}) — N10");
            }
        }

        // ── HTSP / KCS / PKG: hàng vào + còn chờ làm theo từng mã CV (N5) ──
        // [GN-WTS-CAP] Các mã CV chính dùng chung hàng vào nhóm, làm thứ tự nào cũng được (N5).
        //   Vào SX     = Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + Làm hộ trả về − NG đếm lúc nhận − Gửi làm hộ
        //   Đã rời     = NG ở các mã khác + phiếu Trả hàng lỗi (chiếc phát hiện lỗi sau khi nhận)
        //   Chiếc đã rời có thể là chiếc mã này đã làm OK trước đó → chỉ trừ phần vượt số OK của mã này.
        //   Còn chờ làm = min( Vào SX − OK − NG − max(0, Đã rời − OK) ,  Chưa làm của cả nhóm )
        // Chưa làm của cả nhóm (= Vào − OK nhỏ nhất − mọi NG − Trả hàng lỗi) là ô bảo toàn thật, K8 canh ô này.
        static void FillCodeWaiting(LedgerRow row, string group, Dictionary<(string g, string key), LedgerTerm> acc, decimal recvNg)
        {
            var G = row.G(group);
            decimal V(string k) => acc.TryGetValue((group, k), out var t) ? t.Value : 0m;
            // [TARO-LAMHO-NC] mã Taro làm hộ: vào = hàng giao theo NC của đoạn (không dùng hàng nhận của nhóm) — tính riêng, cùng công thức
            FillLamHoWaiting(row, G);
            var mains = G.Steps.Where(s => !s.IsPrep && s.LamHoNc == null).ToList();
            if (!mains.Any()) return;
            var gnet  = G.NhanMoi + G.NhanLoiXl + V("nhRet") - recvNg - V("ghSend")
                      - V("nxGiu") - V("nxDa");   // [SUA-XL-GIU] hàng đã sửa giữ ô / tính đã làm: không vào hàng của từng mã CV
            var rr    = V("glTra") - recvNg + V("blChua");   // [GN-NG-AFTER] báo lỗi lấy từ hàng chưa làm = chiếc đã rời Chưa làm (như trả hàng lỗi)
            FillWaitLines(G, mains, gnet, rr, G.ChuaLam, "");
        }

        /// <summary>Còn chờ làm từng mã (N5) — dùng chung cho mã thường của nhóm và mã Taro làm hộ của 1 NC ([TARO-LAMHO-NC]).</summary>
        static void FillWaitLines(LedgerGroup G, List<LedgerStepLine> mains, decimal gnet, decimal rr, decimal chuaLam, string where)
        {
            var ngAll = mains.Sum(s => s.Ng);
            foreach (var s in mains)
            {
                var gone = (ngAll - s.Ng) + rr;
                var perm = gnet - s.Ok - s.Ng - Math.Max(0, gone - s.Ok);
                // [TARO-3T-GOP 2026-11-06] Bộ gộp (mã gộp + mã thành phần cùng NC): mỗi chiếc đi 1 trong 2 cách.
                //   Chiếc đã vào bộ: theo mã gộp = OK+NG mã gộp; theo bộ thành phần ≥ max(OK+NG mã thành phần).
                //   Mã gộp còn làm được = vào − (OK+NG mã gộp) − max(OK+NG mã thành phần) − phần đã rời vượt (OK mã gộp + max OK mã thành phần);
                //   mã thành phần còn làm được = vào − (OK+NG mã gộp) − (OK+NG mã đó) − phần đã rời vượt (OK mã gộp + OK mã đó).
                //   NG ở mọi mã của bộ là NG của chính bộ (không tính "đã rời" cho bộ). Bộ 1 mã = công thức cũ.
                if (s.GopUnit != null)
                {
                    var uLines = mains.Where(x => x.Pg == s.Pg && x.GopUnit == s.GopUnit).ToList();
                    var gL = uLines.FirstOrDefault(x => x.IsGop);
                    var pLs = uLines.Where(x => !x.IsGop).ToList();
                    if (gL != null && pLs.Count > 0)
                    {
                        if (s.IsGop) s.GopDone = pLs.Max(p => p.Ok + p.Ng);   // [WTS-3T-BAO] chỉ hiện chữ
                        var goneU = (ngAll - uLines.Sum(x => x.Ng)) + rr;
                        var doneG = gL.Ok + gL.Ng;
                        perm = s.IsGop
                            ? gnet - doneG - pLs.Max(p => p.Ok + p.Ng) - Math.Max(0, goneU - (gL.Ok + pLs.Max(p => p.Ok)))
                            : gnet - doneG - (s.Ok + s.Ng) - Math.Max(0, goneU - (gL.Ok + s.Ok));
                    }
                }
                var w    = Math.Min(perm, chuaLam);
                s.Waiting = w;
                s.In      = w + s.Ok + s.Ng;
                if (perm < 0)
                    G.Warnings.Add($"{G.Label} {s.SubGroup} {s.Code}: OK + NG ({s.Ok + s.Ng:0.##}) nhiều hơn số nhận vào mã{where} — WTS nhập vượt (L3, N5)");
            }
        }

        /// <summary>[TARO-LAMHO-NC] OK bộ của các dòng mã (cùng 1 NC): mã thường = OK, bộ gộp = OK mã gộp + min OK mã thành phần; lấy nhỏ nhất.</summary>
        static decimal LinesOkMin(List<LedgerStepLine> lines)
        {
            decimal? min = null;
            foreach (var s in lines.Where(x => x.GopUnit == null)) min = min == null ? s.Ok : Math.Min(min.Value, s.Ok);
            foreach (var u in lines.Where(x => x.GopUnit != null).GroupBy(x => x.GopUnit))
            {
                var gL = u.FirstOrDefault(x => x.IsGop);
                var pL = u.Where(x => !x.IsGop).ToList();
                var ok = (gL?.Ok ?? 0) + (pL.Count > 0 ? pL.Min(p => p.Ok) : 0);
                min = min == null ? ok : Math.Min(min.Value, ok);
            }
            return min ?? 0;
        }

        /// <summary>[TARO-LAMHO-NC] Còn chờ làm của mã Taro làm hộ từng NC: vào = HTSP nhận hàng giao theo NC của đoạn (phần OK lúc nhận);
        ///   "chưa làm" của NC = vào − OK bộ Taro NC đó − NG các mã NC đó (chiếc đã trả về Gia công là chiếc đã xong bộ — câu 1 chặn trả quá OK bộ).</summary>
        static void FillLamHoWaiting(LedgerRow row, LedgerGroup G)
        {
            // cả ĐOẠN (1 hoặc nhiều NC máy loại trừ liền nhau) là 1 nhóm: NG ở NC này cũng là chiếc đã rời với mã NC kia (rà vòng 2 T1)
            foreach (var run in row.LamHoRuns)
            {
                var lines = G.Steps.Where(s => !s.IsPrep && s.LamHoNc != null && run.Ncs.Contains(s.LamHoNc, StringComparer.OrdinalIgnoreCase)).ToList();
                if (lines.Count == 0) continue;
                var okMin = lines.GroupBy(x => x.LamHoNc!, StringComparer.OrdinalIgnoreCase).Min(g => LinesOkMin(g.ToList()));
                // [KTT-PHOILOI 2026-11-12] phôi lỗi Taro trả thẳng Kho = chiếc đã rời chưa làm của đoạn (như trả hàng lỗi ở mã thường — có thể đã làm vài mã)
                var chua = run.In - run.TraKho - okMin - lines.Sum(x => x.Ng);
                run.Chua = chua;
                FillWaitLines(G, lines, run.In, run.TraKho, chua, $" (Taro làm hộ {string.Join(", ", run.Ncs.Select(NcName))})");
            }
        }

        // ================================================================
        // [GN-WTS-CAP] TRẦN NHẬP WTS — dùng chung cho trang WTS (realtime, nhãn, lúc Lưu)
        // ================================================================

        /// <summary>[GN-WTS-CAP] Trần nhập WTS của 1 NC (GC) / 1 mã CV (HTSP/KCS/PKG) trên 1 PO.</summary>
        public class WtsCapInfo
        {
            /// <summary>Số OK + NG còn được ghi. null = không chặn (mã chuẩn bị).</summary>
            public decimal? Max     { get; set; }
            /// <summary>Chưa từng có hàng vào NC / mã này.</summary>
            public bool     Locked  { get; set; }
            /// <summary>Mã chính (máy đồng dạng / mã dự phòng đã gộp về mã chính — N2).</summary>
            public string   Code    { get; set; } = "";
            public LedgerExplain Explain { get; set; } = new();
            /// <summary>[WTS-3T-BAO] Câu giải thích thêm (mã gộp: chiếc đã làm mã lẻ thì ghi tiếp mã lẻ). "" = không có.</summary>
            public string Hint { get; set; } = "";
            /// <summary>Dòng ngắn để hiện trên form: "còn chờ làm 45 = 100 − 50 − 5".</summary>
            public string Text => Max == null ? "" : $"chưa làm {Max.Value:0.##} ({Explain.Arithmetic})";
        }

        /// <summary>
        /// [GN-WTS-CAP] Trần nhập WTS (L3): OK + NG lần này ≤ Còn chờ làm của NC / mã CV đó.
        /// pg = ProcessGroup ghi trên WTS (GC | TARO | BAVIA | WASHING | KCS | PKG); code = NC (GC) hoặc mã CV.
        /// </summary>
        public static WtsCapInfo WtsCap(LedgerRow row, string pg, string? code)
        {
            var grp = Pg2Group(pg);
            var c = (code ?? "").Trim();
            if (grp == "") return new WtsCapInfo { Max = 0, Locked = true, Code = c, Explain = new LedgerExplain { Title = "Nhóm không hợp lệ", Note = $"ProcessGroup '{pg}' không thuộc GC / HTSP / KCS / Đóng gói." } };
            var G = row.G(grp);
            if (grp != "GC" && IsPrepCode(c))
                return new WtsCapInfo { Max = null, Code = c, Explain = new LedgerExplain { Title = "Mã chuẩn bị", Rule = "N5", Note = "Mã chuẩn bị (1V/1T/1R/1K/1D/1G) chỉ tính giờ, không tính số lượng." } };

            var main = row.CodeMain.TryGetValue(pg + "|" + c, out var m) ? m : c;
            var line = G.Steps.FirstOrDefault(s => s.Pg == pg && string.Equals(s.Code, main, StringComparison.OrdinalIgnoreCase));

            if (line == null)
            {
                // Chưa có snapshot (hoặc mã không có trong snapshot): dùng Chưa làm của cả nhóm
                var src = G.Explains.TryGetValue("ChuaLam", out var ce) ? ce : new LedgerExplain();
                var e = new LedgerExplain
                {
                    Title = $"{G.Label} · Chưa làm (cả nhóm)", Rule = src.Rule, Formula = src.Formula, Result = G.ChuaLam, Terms = src.Terms,
                    Note = G.Steps.Any(s => s.Pg == pg)
                        ? $"Mã {main} không có trong snapshot quy trình của PO — tạm chặn theo Chưa làm của cả nhóm."
                        : "PO chưa có snapshot quy trình cho nhóm này — tạm chặn theo Chưa làm của cả nhóm."
                };
                return new WtsCapInfo { Max = G.ChuaLam, Locked = G.NhanSanXuat <= 0, Code = main, Explain = e };
            }

            // [N3-FIRST] dòng "Nhận vào (chưa qua NC)" không phải NC thật — không ghi WTS ở đây
            if (IsVao(line.Code))
                return new WtsCapInfo
                {
                    Max = 0, Locked = true, Code = main,
                    Explain = new LedgerExplain { Title = VaoLabel, Rule = "N3",
                        Note = "Hàng nhận vào Gia công chưa qua NC nào (NC đầu là máy loại trừ): không ghi WTS ở đây — giao theo NC cho nhóm làm máy loại trừ, hàng trả về NC sau thì ghi WTS ở NC đó." }
                };
            if (line.IsExcluded)
                return new WtsCapInfo
                {
                    Max = 0, Locked = true, Code = main,
                    Explain = new LedgerExplain { Title = $"NC {main} · máy loại trừ", Rule = "N3, K4",
                        Note = $"NC {main} dùng máy loại trừ ({line.Machine}): hàng giao nhóm khác làm (giao theo NC), Gia công không ghi số lượng ở NC này." }
                };

            var label = pg == "GC" ? $"NC {line.Code}" : $"{line.SubGroup} {line.Code}";
            var ex = new LedgerExplain
            {
                Title = $"{G.Label} · {label} · Chưa làm", Rule = pg == "GC" ? "L3, N1, N2" : "L3, N5",
                Formula = "Nhận vào − đã ghi WTS OK − đã ghi WTS NG",   // [WTS-CAP-TEXT]
                Note = pg == "GC"
                    ? $"Nhận vào NC: {line.Note}. Máy đồng dạng gộp chung số với NC chính."
                    : line.LamHoNc != null   // [TARO-LAMHO-NC]
                    ? $"Taro làm hộ {NcName(line.LamHoNc)} (máy loại trừ giữa chuỗi Gia công): nhận vào mã = hàng Gia công giao theo NC cho HTSP làm NC này (phần OK lúc nhận) − chiếc đã NG ở mã khác cùng NC mà mã này chưa làm. Không tính vào Chưa làm / Đã làm của HTSP."
                    : "Nhận vào mã = hàng nhận vào sản xuất của nhóm − chiếc đã rời nhóm (NG ở mã khác, trả hàng lỗi) mà mã này chưa làm; không vượt Chưa làm của cả nhóm.",
                Result = line.Waiting ?? 0
            };
            // [WTS-CAP-TEXT 2026-10-31] chỉ đổi chữ (số giữ nguyên): "OK" / "NG" dễ hiểu nhầm là số đang nhập → "đã ghi WTS OK / NG"
            // [WTS-3T-BAO 2026-11-12] mã gộp (vd 3T thay cả bộ 3T1 + 3T2 + 3T3): chiếc đã làm mã lẻ trong bộ không ghi mã gộp được — trước câu báo
            //   "chưa nhận hàng (Nhận vào 0 …)" dù nhóm đã nhận (khách test 12/11 TH9, PO 0002416654). Chỉ đổi chữ: số trần (Waiting) giữ nguyên.
            //   Nguồn: KHÁCH 12/11 (sheet test giao nhận TH9); HÙNG 12/11 17:50.
            bool gop = line.IsGop && line.GopUnit != null && line.GopDone > 0;
            ex.Terms.Add(new LedgerTerm { Label = gop ? "Nhận vào bộ" : "Nhận vào", Value = (line.In ?? 0) + (gop ? line.GopDone : 0), Sign = 1 });
            ex.Terms.Add(new LedgerTerm { Label = "đã ghi WTS OK", Value = line.Ok, Sign = -1 });
            ex.Terms.Add(new LedgerTerm { Label = "đã ghi WTS NG", Value = line.Ng, Sign = -1 });
            if (gop) ex.Terms.Add(new LedgerTerm { Label = "chiếc đã làm mã lẻ trong bộ", Value = line.GopDone, Sign = -1 });
            if (gop) ex.Formula = "Nhận vào bộ − đã ghi WTS OK − đã ghi WTS NG − chiếc đã làm mã lẻ trong bộ";   // rà độc lập Gói 19 (Thấp 5): công thức khớp số hạng
            if (line.OkSua != 0) ex.Terms.Add(new LedgerTerm { Label = "hàng sửa đạt tính là xong NC này (không WTS)", Value = line.OkSua, Sign = -1 });   // [SUA-OK-NC]
            var hint = "";
            if (gop)
            {
                var parts = G.Steps.Where(x => x.Pg == pg && x.GopUnit == line.GopUnit && !x.IsGop && x.LamHoNc == line.LamHoNc).ToList();
                static string Bare(string k) { var i = k.IndexOf(" (NC ", StringComparison.Ordinal); return i > 0 ? k[..i] : k; }   // cùng NC với mã gộp — bỏ "(NC x)" cho gọn
                // rà độc lập Gói 19 (Trung 2): "thiếu" = chiếc đã vào bộ (GopDone) − chiếc mã lẻ đó đã ghi (OK + NG); chỉ nêu mã lẻ còn thiếu (> 0)
                var miss = parts.Select(x => (Code: Bare(x.Code), Thieu: line.GopDone - (x.Ok + x.Ng))).Where(x => x.Thieu > 0).ToList();
                hint = $"Mã {line.Code} làm cả bộ {string.Join(" + ", parts.Select(x => Bare(x.Code)))} một lần — chỉ ghi cho chiếc chưa làm mã lẻ nào trong bộ; "
                     + (miss.Count > 0
                        ? $"{line.GopDone:0.##} chiếc đã làm mã lẻ thì ghi tiếp mã lẻ còn thiếu ({string.Join(", ", miss.Select(x => $"{x.Code} thiếu {x.Thieu:0.##}"))})."
                        : $"{line.GopDone:0.##} chiếc đã làm đủ các mã lẻ trong bộ.");
            }
            return new WtsCapInfo { Max = line.Waiting ?? 0, Locked = (line.In ?? 0) + (gop ? line.GopDone : 0) <= 0, Code = main, Explain = ex, Hint = hint };
        }

        /// <summary>[GN-WTS-CAP][K5][K8] Các ô đang âm của 1 PO (key → giá trị, mô tả).</summary>
        // ================================================================
        // [GN-NG-AFTER 2026-10-27] Báo hàng lỗi phát hiện sau khi nhận
        // ================================================================
        public static string NgRepText(InNgRep b)
            => $"Báo lỗi #{b.Id} · {GroupLabel(b.Group)} phát hiện {b.Qty:0.##} chiếc lỗi sau khi nhận · lấy từ {HandoverNgSource.SourceLabel(b.Source, b.Group)}"
               + (string.IsNullOrEmpty(b.NC) ? "" : $" {NcText(b.NC)}")   // [N3-FIRST] chữ @VAO
               + $" · {HandoverNgSource.OriginLabel(b.Origin, b.Cause)}"
               + (string.IsNullOrEmpty(b.Reason) ? "" : $" · lỗi: {b.Reason}")
               + (string.IsNullOrEmpty(b.Who) ? "" : $" · {b.Who}");

        /// <summary>[GC-SUA-TAI-CHO] Chữ 1 Kết quả sửa (diễn giải, dòng B, khung lỗi).</summary>
        public static string RepairText(InRepair r)
            => $"Kết quả sửa #{r.Id} · {GroupLabel(r.Group)} sửa {r.Pass + r.Fail:0.##} lấy từ ô {HandoverCell.Label(r.Cell)}"
               + (r.NgCaseId is long v && v > 0 ? $" · {NgCaseNo.Format(v)}" : "")
               + $" · đạt {r.Pass:0.##} (chờ KCS)" + (r.Fail != 0 ? $" · không đạt {r.Fail:0.##} → ô Hàng lỗi" : "")
               + (string.IsNullOrEmpty(r.ToNC) ? "" : " · " + RepMarkText(r.ToNC))   // [SUA-XL-GIU][SUA-OK-NC] chữ dấu
               + (string.IsNullOrEmpty(r.Who) ? "" : $" · {r.Who}");

        /// <summary>[KHACH-GUI-LAI 2026-11-11] Chữ 1 phiếu khách gửi bù (dòng B + diễn giải ô).</summary>
        public static string BuText(InBu b)
            => $"Khách gửi bù {b.Qty:0.##} cho {NgCaseNo.Format(b.NgCaseId)} · Kho nhận vào ô Hàng lỗi đã xử lý (phôi còn)"
               + (string.IsNullOrEmpty(b.Notes) ? "" : $" · {b.Notes}") + (string.IsNullOrEmpty(b.Who) ? "" : $" · {b.Who}") + $" · phiếu bù #{b.Id}";

        /// <summary>
        /// [KHACH-GUI-LAI 2026-11-11] Trần nhận phôi bù cho 1 vụ = trả khách + huỷ của vụ − đã bù. Chặn cả khi PO tắt Workflow.
        /// Vụ 0 (hàng lỗi cũ chưa có mã) / không thuộc PO / không có trả khách, huỷ: 0.
        /// Nguồn: KHÁCH 10/11 vòng 1 câu 1b A (chặn bù vượt; gửi dư thì sửa Số lượng thực nhận); HÙNG 11/11 14:39 (chặn cả Workflow OFF).
        /// </summary>
        public static (decimal Max, string Text) BuCap(LedgerRow row, long ngCaseId)
        {
            if (ngCaseId <= 0 || !row.Cases.TryGetValue(ngCaseId, out var c))
                return (0, "lần lỗi này không có hàng trả khách / huỷ ở PO này");   // (rà độc lập Gói 14 L4: lời báo đã có mã vụ ở đầu)
            var max = Math.Max(0, c.TraKhach + c.Huy - c.DaBu);
            return (max, $"trả khách {c.TraKhach:0.##} + huỷ {c.Huy:0.##} − đã bù {c.DaBu:0.##}");
        }

        /// <summary>[KHACH-GUI-LAI] Vụ được nhận bù (còn trần &gt; 0), theo giờ vụ.</summary>
        public static List<LedgerCase> BuCases(LedgerRow row)
            => row.Cases.Values.Where(c => c.Id > 0 && BuCap(row, c.Id).Max > 0).OrderBy(c => c.At ?? DateTime.MinValue).ThenBy(c => c.Id).ToList();

        /// <summary>[KHACH-GUI-LAI] Vụ có đã bù vượt trả khách + huỷ (không được xảy ra — máy chủ chặn nhập bù vượt và huỷ phiếu trả khách / huỷ làm bù vượt).</summary>
        public static List<LedgerCase> BuOver(LedgerRow row)
            => row.Cases.Values.Where(c => c.DaBu > c.TraKhach + c.Huy).ToList();

        /// <summary>[SUA-XL-GIU][SUA-OK-NC] Chữ cột ToNC của Kết quả sửa: dấu → chữ cho người đọc; NC thường → "làm tiếp NC x".</summary>
        public static string RepMarkText(string? toNc) => toNc switch
        {
            RepXong => "không làm lại NC (đã làm xong trước lúc phát hiện)",
            RepOk   => "tính là xong NC của vụ (không làm lại NC)",
            null or "" => "",
            _ => $"làm tiếp {NcText(toNc)}"
        };

        /// <summary>[GC-SUA-TAI-CHO] Chữ 1 lần KCS xác nhận.</summary>
        public static string RepairCheckText(InRepair r, InRepairCheck c)
            => $"KCS xác nhận #{c.Id} (Kết quả sửa #{r.Id} · {GroupLabel(r.Group)}) · đạt {c.Ok:0.##}"
               + (c.Ng != 0 ? $" · loại {c.Ng:0.##} → ô Hàng lỗi {GroupLabel(r.Group)}" : "")
               + (r.Group == "GC" && !string.IsNullOrEmpty(r.ToNC) && !IsRepMark(r.ToNC) && c.Ok != 0 ? $" · vào {NcText(r.ToNC)}" : "")
               + (IsRepMark(r.ToNC) && c.Ok != 0 ? " · " + RepMarkText(r.ToNC) : "")   // [SUA-XL-GIU]
               + (string.IsNullOrEmpty(c.Who) ? "" : $" · {c.Who}");

        /// <summary>
        /// [GC-SUA-TAI-CHO] Trần ghi Kết quả sửa: đạt + không đạt ≤ số vụ đó đang giữ ở ô lấy của nhóm (và ≤ ô đó của nhóm).
        /// Dùng chung trần của phiếu "Hàng sửa / Hàng lỗi → trả nơi phát hiện" (IssueCapCase · RepairReturn) — cùng ô, cùng vụ.
        /// </summary>
        public static (decimal Max, LedgerExplain Explain) RepairCap(LedgerRow row, string group, string cell, long? ngCaseId)
            => cell is HandoverCell.Sua or HandoverCell.Loi
                ? IssueCapCase(row, group, HandoverPurpose.RepairReturn, null, cell, ngCaseId)
                : (0, new LedgerExplain { Title = "Ô lấy hàng không hợp lệ (chỉ Hàng sửa / Hàng lỗi)" });

        /// <summary>
        /// [GN-NG-AFTER] Trần báo lỗi: số đang có ở ô lấy hàng. Kho: Phôi còn giao. Nhóm SX: Chưa làm (GC: hàng chờ của NC đã chọn,
        /// không vượt Chưa GC) hoặc Đã làm chưa giao.
        /// </summary>
        public static (decimal Max, LedgerExplain Explain) NgReportCap(LedgerRow row, string group, string source, string? nc)
        {
            var G = row.G(group);
            LedgerExplain Pick(string key) => G.Explains.TryGetValue(key, out var e) ? e : new LedgerExplain { Title = key };
            if (group == "KHO")
                return source == HandoverNgSource.Phoi ? (Math.Max(0, G.ConGiao), Pick("ConGiao")) : (0, new LedgerExplain { Title = "Kho chỉ báo lỗi từ phôi còn" });
            if (source == HandoverNgSource.Da) return (Math.Max(0, G.ConGiao), Pick("ConGiao"));
            if (source != HandoverNgSource.Chua) return (0, new LedgerExplain { Title = "Ô lấy hàng không hợp lệ" });
            if (group == "GC" && G.Steps.Any(s => !s.IsExcluded))
            {
                var st = G.Steps.FirstOrDefault(s => s.Code == nc && !s.IsExcluded);
                if (st == null) return (0, new LedgerExplain { Title = "Chọn NC đang chứa hàng lỗi" });
                var w = st.Waiting ?? 0;
                var ex = new LedgerExplain { Title = $"Gia công · {NcText(nc)} · chưa gia công", Rule = "L3, N1",   // [N3-FIRST] chữ @VAO
                    Formula = "Hàng chờ của NC (không vượt Chưa GC của cả nhóm)", Result = Math.Min(w, G.ChuaLam),
                    Note = "Chiếc lỗi đang nằm ở NC này, chưa gia công qua NC này." };
                return (Math.Max(0, Math.Min(w, G.ChuaLam)), ex);
            }
            return (Math.Max(0, G.ChuaLam), Pick("ChuaLam"));
        }

        // ================================================================
        // [GN-WHERE 2026-10-27] PO ĐANG Ở ĐÂU — phân bổ số lượng hiện tại của 1 PO theo nơi / trạng thái.
        //   Chỉ ĐỌC các ô đã tính trong Compute() (không công thức riêng). Tổng mọi dòng = Phôi nhận (L6) — bộ kiểm canh.
        // ================================================================
        public class WhereItem
        {
            public string  Place { get; set; } = "";   // KHO | GC | HTSP | KCS | PKG | RA (đã ra khỏi PO / thành phẩm)
            public string  Kind  { get; set; } = "";   // CHO_NHAN | PHOI | CHUA | DA | LOI | SUA | THEO_NC | TP_CHO | TP | HUY | KHACH
            public string  Label { get; set; } = "";
            public string? Nc    { get; set; }
            public decimal Qty   { get; set; }
            public string  Hint  { get; set; } = "";
        }

        public class WhereSummary
        {
            public List<WhereItem> Items { get; set; } = new();
            public decimal Phoi       { get; set; }
            /// <summary>[KHACH-GUI-LAI 2026-11-11] Phôi khách gửi bù (nằm trong "phôi còn" của Kho rồi đi như phôi thường).</summary>
            public decimal Bu         { get; set; }
            public decimal PlanQty    { get; set; }
            public decimal Total      => Items.Sum(i => i.Qty);
            public bool    Balanced   => Total == Phoi + Bu;   // [KHACH-GUI-LAI] + bù
            /// <summary>SL PO chưa có phôi (phôi nhận ít hơn SL PO). Không nằm trong tổng.</summary>
            public decimal ChuaCoPhoi => Math.Max(0, PlanQty - Phoi);
            public decimal TpNhap     { get; set; }
            public decimal Loi        { get; set; }   // tồn hàng lỗi + hàng sửa đang giữ ở các nhóm
            public bool    HasNegative { get; set; }
            public decimal PlaceTotal(string place) => Items.Where(i => i.Place == place).Sum(i => i.Qty);
            /// <summary>Trạng thái PO tính từ sổ (thay cột Trạng thái nhập tay không bao giờ đổi).</summary>
            public string Status => HasNegative ? "AM" : Phoi <= 0 ? "CHUA_PHOI" : (PlanQty > 0 && TpNhap >= PlanQty) ? "DU" : "DANG_SX";
            public static string StatusLabel(string s) => s switch
            {
                "AM" => "Số âm — cần kiểm", "CHUA_PHOI" => "Chưa có phôi", "DU" => "Đủ thành phẩm", "DANG_SX" => "Đang SX", _ => s
            };
            /// <summary>Tóm tắt 1 dòng: "Kho 5 · GC 25 · HTSP 10 · TP 0 · lỗi 3".</summary>
            public string Short()
            {
                var parts = new List<string>();
                foreach (var g in GroupCodes)
                {
                    var q = Items.Where(i => i.Place == g && i.Kind != "LOI" && i.Kind != "SUA").Sum(i => i.Qty);
                    if (q != 0) parts.Add($"{GroupLabel(g)} {q:0.##}");
                }
                var tp = Items.Where(i => i.Kind is "TP" or "TP_CHO").Sum(i => i.Qty);
                if (tp != 0) parts.Add($"TP {tp:0.##}");
                if (Loi != 0) parts.Add($"lỗi {Loi:0.##}");
                var ra = Items.Where(i => i.Kind is "HUY" or "KHACH").Sum(i => i.Qty);
                if (ra != 0) parts.Add($"huỷ/trả {ra:0.##}");
                return parts.Count == 0 ? "—" : string.Join(" · ", parts);
            }
        }

        public static WhereSummary WhereIs(LedgerRow r)
        {
            var w = new WhereSummary { Phoi = r.Phoi, Bu = r.Bu /* [KHACH-GUI-LAI] + bù */, PlanQty = r.PlanQty, TpNhap = r.TpNhap, HasNegative = NegativeCells(r).Count > 0 };
            void Put(string place, string kind, string label, decimal qty, string hint = "", string? nc = null)
            {
                if (qty != 0) w.Items.Add(new WhereItem { Place = place, Kind = kind, Label = label, Qty = qty, Hint = hint, Nc = nc });
            }
            foreach (var code in GroupCodes)
            {
                var G = r.G(code);
                Put(code, "CHO_NHAN", "chờ nhận", G.ChoNhan, "đã giao tới, nhóm chưa bấm Nhận");
                Put(code, "CHO_KCS", "hàng sửa chờ KCS xác nhận", G.ChoKcs, "nhóm đã sửa xong (đạt), chờ KCS xác nhận rồi mới làm tiếp");   // [GC-SUA-TAI-CHO]
                if (code == "KHO")
                {
                    Put(code, "PHOI", "phôi còn", G.ConGiao, "phôi tốt chưa giao");
                }
                else
                {
                    if (code == "GC" && G.Steps.Any(s => !s.IsExcluded && s.Waiting != null))
                    {
                        decimal sumNc = 0;
                        foreach (var st in G.Steps.Where(s => !s.IsExcluded && s.Waiting != null))
                        {
                            if (IsVao(st.Code))   // [N3-FIRST] hàng nhận vào chưa qua NC nào, chờ giao theo NC cho nhóm làm máy loại trừ đầu chuỗi
                            {
                                var xn = G.Steps.SkipWhile(s => !IsVao(s.Code)).Skip(1).FirstOrDefault();
                                Put(code, "CHUA", "nhận vào, chưa giao làm " + (xn != null ? $"NC {xn.Code} (máy loại trừ{(string.IsNullOrEmpty(xn.Machine) ? "" : " " + xn.Machine)})" : "NC máy loại trừ"),
                                    st.Waiting!.Value, "đã nhận vào Gia công, chưa giao theo NC cho nhóm làm máy loại trừ đầu chuỗi", st.Code);
                            }
                            else
                            Put(code, "CHUA", "chờ NC " + st.Code, st.Waiting!.Value, "đã nhận, chưa gia công qua NC này", st.Code);
                            sumNc += st.Waiting!.Value;
                        }
                        // Không có NC loại trừ: Chưa GC luôn = Σ chờ các NC (bộ kiểm canh). Có NC loại trừ: phần còn lại là hàng đã qua
                        // NC trước máy loại trừ, chưa giao nhóm khác làm (N3) — không thuộc NC nào. Ghi đủ để tổng luôn đúng sổ.
                        bool hasExcl = G.Steps.Any(s => s.IsExcluded);
                        Put(code, "CHUA", hasExcl ? "chờ giao theo NC (máy loại trừ)" : "chưa GC (lệch NC — báo lại)", G.ChuaLam - sumNc,
                            hasExcl ? "đã qua NC trước máy loại trừ, chưa giao nhóm khác làm" : "phần chênh giữa Chưa GC và tổng chờ các NC — báo lại để kiểm");
                    }
                    else Put(code, "CHUA", code == "GC" ? "chưa GC" : "chưa làm", G.ChuaLam, "đã nhận, chưa làm xong");
                    Put(code, "DA", "đã làm chưa giao", G.ConGiao, "làm xong, chưa lập phiếu giao");
                }
                Put(code, "LOI", "hàng lỗi", G.LoiGiu, "tồn hàng lỗi của nhóm (chờ chuyển / sửa / huỷ / trả khách)");
                Put(code, "SUA", "hàng nhờ sửa", G.SuaGiu, "nhóm khác gửi nhờ sửa, chưa trả");
                Put(code, "THEO_NC", "hàng giao theo NC", G.LamHoGiu, code == "HTSP" && KhoTaroRun(r) != null
                    ? "hàng Gia công giao theo NC / Kho giao thẳng Taro (NC máy loại trừ), chưa trả về Gia công"   // [KHO-TARO-THANG] chỉ chữ
                    : "hàng Gia công giao theo NC (máy loại trừ), chưa trả về");
                Put(code, "XL", "hàng đã sửa chờ trả nơi phát hiện", G.XlGiu, "đã sửa xong, KCS xác nhận đạt — chờ giao về nơi phát hiện lỗi");   // [SUA-XL-GIU]
                w.Loi += G.LoiGiu + G.SuaGiu;
            }
            Put("RA", "TP_CHO", "thành phẩm chờ Kho nhận", r.DangGiao);
            Put("RA", "TP", "thành phẩm nhập kho", r.TpNhap);
            Put("RA", "HUY", "đã huỷ (hàng lỗi)", r.DaHuy);
            Put("RA", "KHACH", "đã trả khách (hàng lỗi)", r.TraKhach);
            return w;
        }

        public static Dictionary<string, (decimal Value, string Text)> NegativeCells(LedgerRow r)
        {
            var d = new Dictionary<string, (decimal Value, string Text)>();
            void Put(string k, decimal v, string t) { if (v < 0) d[k] = (v, t); }
            foreach (var G in r.Groups.Values)
            {
                if (G.IsKho)
                {
                    Put("KHO|ConGiao", G.ConGiao, "Kho: phôi còn giao");
                    Put("KHO|LoiGiu",  G.LoiGiu,  "Kho: tồn hàng lỗi");
                    Put("KHO|SuaGiu",  G.SuaGiu,  "Kho: tồn hàng sửa");   // [GN-CELL]
                    Put("KHO|LamHoGiu", G.LamHoGiu, "Kho: Tồn hàng giao theo NC");   // [GN-ROUTE-FREE]
                    Put("KHO|ChoKcs", G.ChoKcs, "Kho: hàng sửa chờ KCS");   // [GC-SUA-TAI-CHO]
                    Put("KHO|XlGiu", G.XlGiu, "Kho: tồn hàng lỗi đã xử lý");   // [SUA-XL-GIU]
                    continue;
                }
                Put(G.Code + "|ChuaLam",  G.ChuaLam,  $"{G.Label}: Chưa làm");
                Put(G.Code + "|ConGiao",  G.ConGiao,  $"{G.Label}: Đã làm chưa giao");
                Put(G.Code + "|LoiGiu",   G.LoiGiu,   $"{G.Label}: tồn hàng lỗi");
                Put(G.Code + "|SuaGiu",   G.SuaGiu,   $"{G.Label}: tồn hàng sửa");   // [GN-CELL]
                Put(G.Code + "|LamHoGiu", G.LamHoGiu, $"{G.Label}: Tồn hàng giao theo NC");
                Put(G.Code + "|ChoKcs", G.ChoKcs, $"{G.Label}: hàng sửa chờ KCS");   // [GC-SUA-TAI-CHO]
                Put(G.Code + "|XlGiu", G.XlGiu, $"{G.Label}: tồn hàng lỗi đã xử lý (hàng đã sửa chờ trả)");   // [SUA-XL-GIU]
                // [TARO-LAMHO-NC] hàng làm hộ còn tốt (Tồn hàng giao theo NC − chiếc làm hộ bị lỗi đang giữ) + câu 1: trả về ≤ OK bộ Taro đoạn
                Put(G.Code + "|LamHoTot", G.LamHoGiu - r.LamHoNgHeld(G.Code), $"{G.Label}: hàng giao theo NC còn tốt (trừ chiếc làm hộ bị lỗi)");
                if (G.Code == "HTSP" && r.LamHoRuns.Any(x => x.HasTaro))
                    foreach (var lr in r.LamHoRuns)
                        Put("HTSP|LamHoRun|" + lr.Key, lr.Held, $"HTSP: hàng giao theo NC của đoạn {string.Join(", ", lr.Ncs.Select(NcName))} còn giữ (nhận − trả về − chiếc hỏng)");   // rà độc lập gói 6 mục 1
                if (G.Code == "HTSP")
                    foreach (var lr in r.LamHoRuns.Where(x => x.HasTaro))
                        Put("HTSP|LamHoTaro|" + lr.Key, lr.Done - lr.Ret, $"HTSP: Taro làm hộ {string.Join(", ", lr.Ncs.Select(NcName))} — đã trả về {NcName(lr.NextNc)} nhiều hơn OK bộ Taro");
                // Ô theo NC (GC) là ô bảo toàn thật (N1).
                foreach (var s in G.Steps.Where(s => s.Pg == "GC" && s.Waiting.HasValue && !s.IsExcluded && !s.IsPrep))
                    Put($"{G.Code}|{s.Pg}|{s.Code}", s.Waiting!.Value, IsVao(s.Code)
                        ? $"{G.Label} {VaoLabel}: hàng nhận vào chưa giao theo NC"   // [N3-FIRST]
                        : $"{G.Label} {NcName(s.Code)}: chưa làm");   // [GN-WHY] chữ: bỏ "NC NC1"
                // [GN-VOID-RULE] Gia công giao theo NC mà NC sau là máy loại trừ (hoặc không có NC sau): số đã giao từ NC không vượt OK NC đó.
                //   NC sau thường đã tự canh (nhận vào = OK NC trước − đã giao đi), riêng NC sau là máy loại trừ thì không có ô nào canh.
                //   Trước: xoá WTS NC nguồn sau khi đã giao theo NC vẫn cho (fuzz 26/10: OK NC1 còn 1, đã giao theo NC 4).
                if (G.Code == "GC")
                    for (int i = 0; i < G.Steps.Count; i++)
                    {
                        var s = G.Steps[i];
                        if (s.IsExcluded || s.IsPrep || s.HelpOut <= 0) continue;
                        if (IsVao(s.Code)) continue;   // [N3-FIRST] dòng @VAO không có OK (WTS): đã canh bằng ô GC|GC|@VAO (còn chờ = Nhận vào − đã giao theo NC)
                        var next = i + 1 < G.Steps.Count ? G.Steps[i + 1] : null;
                        if (next != null && !next.IsExcluded) continue;
                        Put($"GC|helpOut|{s.Code}", s.Ok + s.OkSua - s.HelpOut, $"{G.Label} {NcName(s.Code)}: đã giao theo NC nhiều hơn OK của NC (WTS + sửa đạt)");   // [SUA-OK-NC] + sửa đạt
                    }
                // [GN-VOID-RULE] Ô theo mã CV (HTSP/KCS/PKG) cũng canh (K5): huỷ lần nhận / huỷ phiếu mà làm WTS một mã vượt số hàng thật nhận
                //   → chặn, phải xoá / sửa WTS mã đó trước. Trước: không canh, nhóm vẫn cân (OK nhóm = min các mã) nhưng WTS mã lệch hàng thật.
                foreach (var s in G.Steps.Where(s => s.Pg != "GC" && s.Waiting.HasValue && !s.IsExcluded && !s.IsPrep))
                    Put($"{G.Code}|{s.Pg}|{s.Code}", s.Waiting!.Value, $"{G.Label}{(s.SubGroup == G.Label || string.IsNullOrEmpty(s.SubGroup) ? "" : " " + s.SubGroup)} mã {s.Code}: WTS đã làm vượt hàng nhận (chưa làm)");   // [GN-WHY] chữ: bỏ "KCS KCS"
            }
            // [VULOI 2026-10-28] Kiểu K5 theo từng vụ: mỗi vụ ở mỗi nơi (ô Hàng lỗi / Hàng sửa) và phần đang chuyển không âm.
            //   Khoá "VU|<id>|<nhóm>|<ô>" — HandoverWhy lần ra phiếu đang lấy từ vụ đó.
            foreach (var cs in r.Cases.Values)
            {
                // [VL8-TIMELINE] Lấy số thấp hơn giữa số cuối và số thấp nhất theo thời gian (hàng của vụ đã giao đi trước khi có nguồn)
                foreach (var hkey in cs.Held.Keys.Concat(cs.MinHeld.Keys).Distinct())
                {
                    var hp = hkey.Split('|');
                    var fin = cs.Held.TryGetValue(hkey, out var hv) ? hv : 0m;
                    var low = cs.MinHeld.TryGetValue(hkey, out var mv) ? Math.Min(mv, fin) : fin;
                    Put($"VU|{cs.Id}|{hkey}", low, $"{(cs.Id == 0 ? "Hàng lỗi cũ chưa có mã" : cs.No)} ở {GroupLabel(hp[0])} · {(hp[1] == HandoverCell.Sua ? "tồn hàng sửa" : hp[1] == HandoverCell.LamHo ? "hàng làm hộ bị lỗi (Tồn hàng giao theo NC)" : hp[1] == HandoverCell.Xl ? "tồn hàng lỗi đã xử lý (hàng đã sửa chờ trả)" : "tồn hàng lỗi")}"
                        + (low < fin ? " (theo thứ tự thời gian: hàng của vụ đã giao đi trước khi có chứng từ nguồn)" : ""));
                }
                Put($"VU|{cs.Id}|CHUYEN", cs.Transit, $"{(cs.Id == 0 ? "Hàng lỗi cũ chưa có mã" : cs.No)}: đang chuyển");
                // [KHACH-GUI-LAI 2026-11-11] Đã bù vượt trả khách + huỷ của vụ (khoá "BU|<vụ>") — không được xảy ra; HandoverWhy lần ra phiếu bù cần huỷ trước.
                //   Nguồn: KHÁCH 10/11 vòng 1 câu 1b A; HÙNG 11/11 14:39 (trần bù chặn cả Workflow OFF — máy chủ chặn riêng ở nhập bù + huỷ phiếu Trả khách / Huỷ).
                if (cs.DaBu > 0) Put($"BU|{cs.Id}", cs.TraKhach + cs.Huy - cs.DaBu, $"{cs.No}: khách đã gửi bù {cs.DaBu:0.##} nhiều hơn trả khách + huỷ");
            }
            return d;
        }

        /// <summary>[GN-WHY] Tên NC để hiện: "NC1" giữ nguyên, mã không bắt đầu bằng NC thì thêm chữ "NC ".</summary>
        public static string NcName(string? code)
            => IsVao(code) ? VaoLabel   // [N3-FIRST]
             : string.IsNullOrEmpty(code) ? "NC" : code.StartsWith("NC", StringComparison.OrdinalIgnoreCase) ? code : "NC " + code;

        // [N3-FIRST 2026-11-04] Dòng ảo "Nhận vào, chưa qua NC" của Gia công khi NC ĐẦU chuỗi là máy loại trừ (Hùng duyệt phương án A 02/11,
        //   thiết kế @VAO duyệt 04/11). Mã "@VAO" không bao giờ là NC thật (Part Master không cho ký tự @ đầu mã NC? — không dựa vào đó:
        //   máy chủ chặn WTS / phiếu nhận vào NC "@VAO", chỉ cho làm NC nguồn của Giao theo NC / Trả hàng lỗi / Báo lỗi).
        public const string VaoCode  = "@VAO";

        // [KHO-TARO-THANG 2026-11-12] KHO GIAO THẲNG TARO khi NC đầu Gia công là máy loại trừ có Taro (đoạn làm hộ đầu chuỗi, NC nguồn @VAO).
        //   Nguồn: KHÁCH 10/11 (hỏi lại 3.3 chọn B) + 12/11 file Word (a = A: Taro phát hiện phôi lỗi khi đang làm trả thẳng Kho, nơi gây lỗi Khách hàng;
        //   b = A: NC đầu Taro thì Kho chỉ giao Taro); HÙNG 12/11 14:00 OK kế hoạch + 3 câu B. Không SQL: dùng cột sẵn có.
        //   Phiếu Kho → Taro = HELP_SEND From KHO (FromNC @VAO, FromCell MOI); Taro trả phôi lỗi = NG_RETURN From HTSP → Kho, FromNC @VAO.
        public const string KhoTaroLabel = "Kho giao thẳng Taro (NC đầu là máy loại trừ)";
        /// <summary>[KTT-PHOILOI] Chữ nguồn của phiếu Taro trả phôi lỗi về Kho.</summary>
        public const string TaroPhoiLoiFrom = "Taro phát hiện phôi lỗi khi đang làm (hàng giao theo NC, chưa làm xong Taro NC đầu)";
        /// <summary>[KHO-TARO-THANG] Phiếu Kho giao thẳng Taro (Giao theo NC do Kho lập).</summary>
        public static bool IsKhoTaro(InTx t, string purpose) => purpose == HandoverPurpose.HelpSend && t.From == "KHO";
        /// <summary>[KTT-PHOILOI] Phiếu Taro (HTSP) trả phôi lỗi về Kho từ hàng đoạn đầu chuỗi (Hàng lỗi công đoạn trước, FromNC @VAO).</summary>
        public static bool IsTaroPhoiLoi(InTx t, string purpose) => purpose == HandoverPurpose.NgReturn && t.From == "HTSP" && IsVao(t.FromNC);
        /// <summary>[KHO-TARO-THANG] Đoạn làm hộ đầu chuỗi (NC nguồn @VAO) có Taro — PO đi đường Kho giao thẳng Taro. null = không có (đường cũ).</summary>
        public static LedgerLamHoRun? KhoTaroRun(LedgerRow row) => row.LamHoRuns.FirstOrDefault(r => r.HasTaro && IsVao(r.FromNc));

        // ================================================================
        // [SUA-XL-GIU 2026-11-10] HÀNG SỬA ĐẠT KHÔNG QUAY LẠI NC / NHÓM ĐÃ LÀM XONG NHẬN LÀ ĐÃ LÀM
        //   Nguồn: KHÁCH 10/11 vòng 1 (docx câu 2 "sửa xong trả bộ phận báo lỗi, giờ sửa ghi tab việc không làm ra SP"; hỏi lại 2.2 A;
        //   hỏi tiếp 2.1 A "chiếc sửa vào HTSP ô Hàng lỗi đã xử lý, tính là đã làm xong, không ghi WTS số lượng"); HÙNG 10/11 19:22 OK thiết kế Gói 15.
        //   Đánh dấu ở cột ToNC của Kết quả sửa (chứng từ mới; chứng từ cũ giữ nghĩa cũ — sổ PO cũ không đổi):
        //     "@XONG" = nhóm sửa đã làm xong chiếc trước lúc phát hiện → KCS xác nhận đạt: nhóm sửa là nơi phát hiện → Đã làm (Gia công: OK NC cuối);
        //               nhóm sửa đứng trước nơi phát hiện → ô Tồn hàng lỗi đã xử lý (giữ vụ) rồi giao tiếp phiếu "Hàng lỗi đã xử lý" (FromCell XL).
        //     "@OK"   = [SUA-OK-NC] Gia công làm hỏng ở NC k (WTS NG) → sửa đạt tính là xong NC k (vào NC k + OK NC k, không ghi WTS lại).
        //   Bên nhận phiếu FromCell XL vào ô Hàng lỗi đã xử lý (XlDest): đứng trước nơi phát hiện → giữ ô; là nơi phát hiện và (là KCS hoặc đã làm
        //   chiếc lúc phát hiện) → Đã làm; còn lại → như cũ (vào Chưa làm, đã xử lý).
        // ================================================================
        public const string RepXong = "@XONG";
        /// <summary>[SUA-OK-NC 2026-11-10] Kết quả sửa Gia công: sửa đạt tính là xong NC của vụ (vụ WTS NG ở NC k).
        ///   Nguồn: KHÁCH 10/11 hỏi tiếp ("mọi trường hợp sửa đều không quay lại NC"); HÙNG 10/11 19:22.</summary>
        public const string RepOk   = "@OK";
        public static bool IsRepMark(string? toNc) => toNc == RepXong || toNc == RepOk;

        /// <summary>[SUA-XL-GIU] Thứ tự nhóm sản xuất (Kho không tính): Gia công 1 · HTSP 2 · KCS 3 · Đóng gói 4; khác −1.</summary>
        public static int ProdIdx(string? g) => g switch { "GC" => 1, "HTSP" => 2, "KCS" => 3, "PKG" => 4, _ => -1 };

        /// <summary>[SUA-XL-GIU] Hàng đã sửa (phiếu FromCell XL) nhận vào ô Hàng lỗi đã xử lý của <paramref name="to"/>:
        ///   "GIU" = giữ ô Tồn hàng lỗi đã xử lý (nhóm đứng trước nơi phát hiện) · "XONG" = Đã làm (nơi phát hiện là KCS — KCS đã xác nhận,
        ///   hoặc nơi phát hiện đã làm xong chiếc lúc phát hiện) · "" = như cũ (vào Chưa làm, đã xử lý).</summary>
        public static string XlDest(string to, string? detectGroup, bool fromDa)
        {
            int ti = ProdIdx(to), di = ProdIdx(detectGroup);
            if (ti < 0 || di < 0) return "";
            if (ti < di) return "GIU";
            if (ti == di && (to == "KCS" || fromDa)) return "XONG";
            return "";
        }

        /// <summary>[SUA-XL-GIU] Lần KCS xác nhận đạt của Kết quả sửa có dấu: "GIU" (ô Tồn hàng lỗi đã xử lý) · "XONG" (Đã làm) · "OK" (Gia công: xong NC của vụ)
        ///   · "" (như cũ). Dùng chung Compute + đường đi hàng lỗi.</summary>
        public static string RepairDest(LedgerInput inp, InRepair rp)
        {
            if (rp.ToNC == RepOk) return rp.Group == "GC" ? "OK" : "";
            if (rp.ToNC != RepXong || rp.NgCaseId is not long id || !inp.CaseInfo.TryGetValue(id, out var ci)) return "";
            // [SUA-HO-TRA-NGUOC 2026-11-11] + nhóm sửa hộ đứng SAU nơi phát hiện: cũng giữ ô Tồn hàng lỗi đã xử lý (rồi trả ngược đúng nơi phát hiện).
            //   Dấu @XONG cho nhóm sau nơi phát hiện chỉ có từ Gói 16 (HandoverForm.RepairTarget) — chứng từ cũ không đổi nghĩa.
            return ci.DetectGroup == rp.Group ? "XONG" : ProdIdx(rp.Group) > 0 && ProdIdx(ci.DetectGroup) > 0 ? "GIU" : "";
        }

        /// <summary>[XL-CHU-B 2026-11-10] Chữ dòng B khi hàng vào ô Hàng lỗi đã xử lý theo đích (XlDest / RepairDest). Rà độc lập Gói 15 (Thấp 5):
        ///   trước luôn ghi "(SP đã OK, nhập WTS được)" kể cả hàng giữ ô / tính đã làm (không WTS). Nguồn: CLAUDE (rà độc lập 10/11).</summary>
        ///   [XL-CHU-KCS 2026-11-11] KCS là nơi phát hiện nhận hàng đã sửa: "KCS kiểm lại, giờ ghi Việc không làm ra SP" (khách 11/11). Gia công: xem XlRecvNote (GcXlBack).
        public static string XlStepNote(string dest, string? group = null) => dest switch
        {
            "XONG" when group == "KCS" => " (hàng đã sửa — KCS kiểm lại, giờ ghi \"Việc không làm ra SP\"; không ghi WTS số lượng)",
            "GIU"  => " (hàng đã sửa — giữ chờ trả nơi phát hiện, không ghi WTS số lượng)",
            "XONG" => " (hàng đã sửa — tính là đã làm, không ghi WTS số lượng)",
            "OK"   => " (hàng đã sửa — tính là xong NC của chiếc lỗi, không ghi WTS lại)",
            _      => " (SP đã OK, nhập WTS được)",
        };

        /// <summary>[SUA-XL-GIU] Vụ báo lỗi lấy từ "đã làm xong chưa giao" (dùng cho XlDest).</summary>
        public static HashSet<long> FromDaCases(LedgerInput inp)
            => inp.NgReps.Where(b => b.Source == HandoverNgSource.Da && b.NgCaseId is long).Select(b => b.NgCaseId!.Value).ToHashSet();

        /// <summary>[SUA-XL-GIU] Phiếu hàng đã sửa (FromCell XL) nhận vào ô Hàng lỗi đã xử lý: đích theo XlDest.</summary>
        /// <summary>[SUA-HO-TRA-NGUOC 2026-11-11] Hàng đã sửa (nhóm khác sửa hộ) trả ngược về Gia công khi Gia công là nơi phát hiện: đích giống Gia công tự sửa
        ///   (HandoverForm.RepairTarget nhánh Gia công): "XONG" = xong NC cuối (phát hiện ở "đã làm xong NC cuối" / lỗi từ nhóm sau) · "OK" = xong NC k (WTS NG ở NC k —
        ///   [SUA-OK-NC]) · "" = theo NC phiếu (chờ NC k như K2). Nc = NC tính là đã xong (đúng chữ mã trong chuỗi). Nguồn: KHÁCH 11/11; HÙNG 11/11 15:34.</summary>
        public static (string Kind, string Nc) GcXlBack(InCase ci, List<InGcStep> gcChain, Dictionary<string, string> codeMain)
        {
            if (ci.DetectGroup != "GC" || gcChain.Count == 0) return ("", "");
            string? Hit(string? raw) => raw == null ? null : gcChain.FirstOrDefault(x => string.Equals(x.NC, raw, StringComparison.OrdinalIgnoreCase))?.NC;
            if (string.IsNullOrWhiteSpace(ci.DetectNc))
                return ci.Origin == NgOrigin.BaoLoi || ci.Cause is "HTSP" or "KCS" or "PKG" ? ("XONG", gcChain[^1].NC) : ("", "");
            if (ci.Origin != NgOrigin.Wts) return ("", "");
            var dn = ci.DetectNc.Trim();
            var nc = Hit(codeMain.TryGetValue("GC|" + dn, out var mn) ? mn : dn);
            return nc == null ? ("", "") : ("OK", nc);
        }

        /// <summary>[SUA-HO-TRA-NGUOC][XL-CHU-KCS 2026-11-11] Chữ dòng B lần nhận phiếu hàng đã sửa: như XlStepNote, riêng Gia công là nơi phát hiện (GcXlBack) ghi xong NC nào.</summary>
        public static string XlRecvNote(LedgerInput inp, InTx t, string cell)
        {
            if (t.To == "GC" && t.FromCell == HandoverCell.Xl && cell == HandoverCell.Xl && t.NgCaseId is long id && inp.CaseInfo.TryGetValue(id, out var ci))
            {
                var chain = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).ToList();
                var cm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in inp.GcSteps.Where(s => !string.IsNullOrWhiteSpace(s.NC))) cm["GC|" + s.NC.Trim()] = s.IsBackup && !string.IsNullOrWhiteSpace(s.ParentNC) ? s.ParentNC!.Trim() : s.NC.Trim();
                var gb = GcXlBack(ci, chain, cm);
                if (gb.Kind != "") return $" (hàng đã sửa — tính là xong {NcName(gb.Nc)}, không ghi WTS số lượng)";   // rà vòng 2 L1: còn lại theo XlDest như sổ
            }
            return XlStepNote(XlRecvDest(inp, t, cell), t.To);
        }

        public static string XlRecvDest(LedgerInput inp, InTx t, string cell, HashSet<long>? fromDa = null)
        {
            if (t.FromCell != HandoverCell.Xl || cell != HandoverCell.Xl || t.NgCaseId is not long id || !inp.CaseInfo.TryGetValue(id, out var ci)) return "";
            fromDa ??= FromDaCases(inp);
            return XlDest(t.To, ci.DetectGroup, fromDa.Contains(id));
        }
        public const string VaoLabel = "Nhận vào (chưa qua NC)";
        /// <summary>[N3-FIRST] Mã là dòng ảo @VAO? Bỏ khoảng trắng, không phân biệt hoa thường (rà độc lập 04/11 M1: "@vao" lọt WTS khi Workflow OFF).</summary>
        public static bool IsVao(string? code) => code != null && string.Equals(code.Trim(), VaoCode, StringComparison.OrdinalIgnoreCase);
        /// <summary>[N3-FIRST] Chữ "NC x" để hiện; riêng @VAO hiện "Nhận vào (chưa qua NC)". Mã khác giữ đúng chữ cũ "NC " + mã.</summary>
        public static string NcText(string? code) => IsVao(code) ? VaoLabel : "NC " + code;

        /// <summary>[GN-WHY] Như NewNegatives nhưng trả khoá ô (để lần ra chứng từ đang trừ vào ô đó).</summary>
        public static List<string> NewNegativeKeys(LedgerRow before, LedgerRow after)
        {
            var b = NegativeCells(before);
            return NegativeCells(after)
                .Where(kv => !b.TryGetValue(kv.Key, out var bv) || kv.Value.Value < bv.Value)
                .Select(kv => kv.Key).ToList();
        }

        /// <summary>
        /// [GN-WTS-CAP][K5][K8] So 2 trạng thái của cùng 1 PO: liệt kê ô mới bị âm hoặc âm thêm.
        /// Ô đã âm từ trước (dữ liệu cũ) mà không âm thêm thì không tính — không chặn oan dữ liệu cũ.
        /// </summary>
        public static List<string> NewNegatives(LedgerRow before, LedgerRow after)
        {
            var b = NegativeCells(before);
            return NegativeCells(after)
                .Where(kv => !b.TryGetValue(kv.Key, out var bv) || kv.Value.Value < bv.Value)
                .Select(kv => $"{kv.Value.Text} = {kv.Value.Value:0.##}")
                .ToList();
        }

        // ================================================================
        // TRẦN LẬP PHIẾU — dùng chung cho modal Giao và IssueAsync
        // ================================================================

        /// <summary>
        /// [GN-LEDGER] Trần số lượng được lập cho 1 loại phiếu + diễn giải.
        /// [GN-CELL] fromCell = ô bên giao chọn (MOI/LOI/SUA): Trả hàng đã sửa từ ô Hàng lỗi (tự sửa) lấy trần ô Hàng lỗi, từ ô Hàng sửa lấy trần ô Hàng sửa.
        /// </summary>
        public static (decimal Max, LedgerExplain Explain) IssueCap(LedgerRow row, string from, string purpose, string? fromNC, string? fromCell = null, string? toNC = null)   // [TARO-LAMHO-NC] + toNC (trả hàng làm hộ)
        {
            var G = row.G(from);
            LedgerExplain Pick(string key) => G.Explains.TryGetValue(key, out var e) ? e : new LedgerExplain();
            switch (purpose)
            {
                case HandoverPurpose.New:
                    return (Math.Max(0, G.ConGiao), Pick("ConGiao"));
                case HandoverPurpose.NgReturn:
                    // [KTT-PHOILOI 2026-11-12] Taro phát hiện phôi lỗi khi đang làm (hàng đoạn đầu chuỗi chưa xong bộ Taro) → trả thẳng Kho.
                    //   Trần = min(chưa làm xong bộ Taro của đoạn, hàng của đoạn còn giữ). Nguồn: KHÁCH 12/11 (3.3 a = A); HÙNG 12/11 14:00.
                    if (from == "HTSP" && IsVao(fromNC))
                    {
                        var kr = KhoTaroRun(row);
                        if (kr == null)
                            return (0, new LedgerExplain { Title = "Taro trả phôi lỗi về Kho", Note = "Chỉ khi NC đầu Gia công là máy loại trừ có Taro (Taro làm NC đầu)." });
                        var ek = new LedgerExplain { Title = $"HTSP · Taro {string.Join(", ", kr.Ncs.Select(NcName))} · chưa làm xong (hàng giao theo NC đầu chuỗi)", Rule = "N3, K4, N16",
                            Formula = "min(chưa làm xong bộ Taro của đoạn, hàng của đoạn còn giữ)",
                            Note = "Phôi lỗi Taro phát hiện khi đang làm (chưa xong bộ Taro): trả thẳng Kho ô Hàng lỗi, nơi gây lỗi Khách hàng. Chiếc Taro làm hỏng: ghi NG ở WTS Taro (trả Gia công như cũ)." };
                        ek.Terms.Add(new LedgerTerm { Label = "Chưa làm xong bộ Taro", Value = kr.Chua ?? 0, Sign = 1 });
                        ek.Terms.Add(new LedgerTerm { Label = "Hàng của đoạn còn giữ", Value = kr.Held, Sign = 1 });
                        ek.Result = Math.Min(kr.Chua ?? 0, kr.Held);
                        return (Math.Max(0, ek.Result), ek);
                    }
                    if (from == "KHO") return (0, new LedgerExplain { Title = "Kho không có công đoạn trước", Note = "Phôi lỗi tồn ở Kho: giao phân loại Hàng lỗi KHÔNG tích (gửi nhờ sửa), hoặc Huỷ / Trả khách." }); // [GN-PLH] mục 36: trước "giao từ ô"
                    // [GN-NC-PIN] Gia công: trần theo NC phát hiện lỗi = hàng đang chờ ở NC đó (không vượt Chưa làm của nhóm)
                    if (from == "GC" && !string.IsNullOrEmpty(fromNC))
                    {
                        var ln = G.Steps.FirstOrDefault(s => s.Code == fromNC && !s.IsExcluded);
                        if (ln == null)
                            return (0, new LedgerExplain { Title = "Trả hàng lỗi theo NC", Note = $"NC {fromNC} không có trong snapshot (hoặc là NC máy loại trừ)." });
                        var w = ln.Waiting ?? 0;
                        var ex = new LedgerExplain { Title = $"Gia công · {NcText(fromNC)} chưa làm", Rule = "N10", Formula = $"Nhận vào {NcText(fromNC)} − OK − NG",   // [N3-FIRST] chữ @VAO
                            Note = "Chiếc lỗi phát hiện ở NC này, lấy từ hàng đang chờ của NC (chưa làm).", Result = Math.Min(w, G.ChuaLam) };
                        ex.Terms.Add(new LedgerTerm { Label = $"Nhận vào {NcText(fromNC)}", Value = ln.In ?? 0, Sign = 1 });
                        ex.Terms.Add(new LedgerTerm { Label = "OK + NG đã làm", Value = ln.Ok + ln.OkSua + ln.Ng, Sign = -1 });   // [SUA-OK-NC] + sửa đạt xong NC
                        return (Math.Max(0, Math.Min(w, G.ChuaLam)), ex);
                    }
                    return (Math.Max(0, G.ChuaLam), Pick("ChuaLam"));
                case HandoverPurpose.RepairSend:
                case HandoverPurpose.Scrap:        // [GN-NG-OUT] chỉ huỷ / trả khách được hàng lỗi đang giữ (L5)
                case HandoverPurpose.CustReturn:
                    return (Math.Max(0, G.LoiGiu), Pick("LoiGiu"));
                case HandoverPurpose.RepairReturn:
                    // [GN-CELL] ô Hàng sửa (sửa hộ xong) hay ô Hàng lỗi (tự sửa xong)
                    return fromCell == HandoverCell.Loi
                        ? (Math.Max(0, G.LoiGiu), Pick("LoiGiu"))
                        : fromCell == HandoverCell.Xl ? (Math.Max(0, G.XlGiu), Pick("XlGiu"))   // [SUA-XL-GIU] hàng đã sửa chờ trả
                        : (Math.Max(0, G.SuaGiu), Pick("SuaGiu"));
                case HandoverPurpose.HelpReturn:
                {
                    // [TARO-LAMHO-NC] trả lỗi hàng làm hộ (mang vụ) = chiếc làm hộ bị lỗi đang giữ; hàng tốt = Tồn hàng giao theo NC − chiếc bị lỗi,
                    //   và (Workflow ON, HTSP, NC nhận là NC sau đoạn có Taro làm hộ) không vượt OK bộ Taro đoạn − đã trả (câu 1)
                    var ngHeld = row.LamHoNgHeld(from);
                    if (fromCell == HandoverCell.Loi)
                        return (Math.Max(0, ngHeld), new LedgerExplain { Title = $"{G.Label} · hàng làm hộ bị lỗi đang giữ", Rule = "N3, K4", Result = ngHeld,
                            Note = "Chiếc làm hộ bị lỗi (WTS Taro làm hộ NG, đã có vụ) nằm trong Tồn hàng giao theo NC — trả Gia công vào ô Hàng lỗi, cùng vụ." });
                    var good = G.LamHoGiu - ngHeld;
                    LedgerExplain GoodEx()
                    {
                        if (ngHeld == 0) return Pick("LamHoGiu");
                        var e0 = new LedgerExplain { Title = $"{G.Label} · hàng giao theo NC còn tốt", Rule = "N3, K4", Result = good,
                            Formula = "Tồn hàng giao theo NC − chiếc làm hộ bị lỗi (trả lỗi riêng)" };   // rà độc lập gói 6 vòng 2 L3
                        e0.Terms.Add(new LedgerTerm { Label = "Tồn hàng giao theo NC", Value = G.LamHoGiu, Sign = 1 });
                        e0.Terms.Add(new LedgerTerm { Label = "chiếc làm hộ bị lỗi", Value = ngHeld, Sign = -1 });
                        return e0;
                    }
                    if (row.ApplyWorkflow && row.LamHoRuns.Any(x => x.HasTaro))
                    {
                        var lr = row.LamHoRunTo(toNC);
                        // nhóm khác HTSP (vd hàng giao lúc Workflow OFF) không trả hàng đoạn có Taro vào NC sau đoạn: Taro chưa làm (rà vòng 2 T2-R3)
                        if (from != "HTSP")
                            return lr is { HasTaro: true }
                                ? (0, new LedgerExplain { Title = $"{G.Label} · không trả vào {NcName(toNC)}: đoạn {string.Join(", ", lr.Ncs.Select(NcName))} có Taro làm hộ (HTSP làm)", Rule = "N3, K4", Result = 0 })
                                : (Math.Max(0, good), GoodEx());
                        // HTSP: hàng của đoạn chỉ trả về NC sau chính đoạn đó (số dư đoạn = nhận − đã trả − chiếc hỏng; đoạn Taro: ≤ OK bộ − đã trả — câu 1).
                        //   Hàng giao theo NC không thuộc đoạn nào (giao lúc Workflow OFF / dữ liệu cũ) = phần còn lại → trả vào NC khác NC sau đoạn (rà vòng 2 T2-R2, L4)
                        var orphan = good - row.LamHoRuns.Sum(x => Math.Max(0, x.Held));
                        if (lr == null)
                            return (Math.Max(0, Math.Min(good, orphan)), new LedgerExplain { Title = $"HTSP · hàng làm hộ Taro chỉ trả về {string.Join(" / ", row.LamHoRuns.Select(x => NcName(x.NextNc)).Distinct())}; vào {NcName(toNC)} chỉ hàng giao theo NC không thuộc đoạn nào", Rule = "N3, K4",
                                Result = Math.Max(0, Math.Min(good, orphan)),
                                Note = "PO có Taro làm hộ ở NC máy loại trừ giữa chuỗi: hàng của đoạn trả về NC thường ngay sau đoạn đó." });
                        var cap = Math.Min(good, lr.Held);
                        if (lr.HasTaro && lr.Done - lr.Ret < cap)
                            return (Math.Max(0, lr.Done - lr.Ret), Pick("LamHoTaro:" + lr.Key));
                        if (lr.Held < good)
                            return (Math.Max(0, cap), new LedgerExplain { Title = $"HTSP · hàng giao theo NC của đoạn {string.Join(", ", lr.Ncs.Select(NcName))} còn giữ", Rule = "N3, K4", Result = cap,
                                Formula = "nhận theo NC của đoạn − đã trả về − chiếc hỏng (Taro làm hộ NG)" });
                    }
                    return (Math.Max(0, good), GoodEx());
                }
                case HandoverPurpose.HelpSend:
                {
                    // [KHO-TARO-THANG 2026-11-12] Kho giao thẳng Taro: trần = phôi còn (chỉ khi PO có đoạn Taro đầu chuỗi, NC nguồn @VAO).
                    //   Nguồn: KHÁCH 12/11 (3.3 b = A); HÙNG 12/11 14:00.
                    if (from == "KHO")
                        return KhoTaroRun(row) == null || !IsVao(fromNC)
                            ? (0, new LedgerExplain { Title = "Kho giao thẳng Taro", Note = "Chỉ khi NC đầu Gia công là máy loại trừ có Taro (Taro làm NC đầu) — PO này không có." })
                            : (Math.Max(0, G.ConGiao), Pick("ConGiao"));
                    if (from != "GC" || string.IsNullOrEmpty(fromNC))
                        return (0, new LedgerExplain { Title = "Giao theo NC", Note = "Chỉ Gia công giao theo NC, và phải chọn NC nguồn." });
                    var line = G.Steps.FirstOrDefault(s => s.Code == fromNC && !s.IsExcluded);
                    if (line == null)
                        return (0, new LedgerExplain { Title = "Giao theo NC", Note = $"NC {fromNC} không có trong snapshot (hoặc là NC máy loại trừ)." });
                    // [N3-FIRST 2026-11-04] Giao theo NC từ dòng "Nhận vào (chưa qua NC)": trần = hàng nhận vào chưa giao (dòng này không có OK WTS)
                    if (IsVao(line.Code))
                    {
                        var ev = new LedgerExplain
                        {
                            Title = $"Gia công · {VaoLabel} · còn giao được", Rule = "N3, N4",
                            Formula = $"{VaoLabel} − đã giao theo NC từ đây",
                            Note = "NC đầu của Gia công là máy loại trừ: hàng nhận vào giao theo NC cho nhóm làm máy đó, nhóm đó trả về NC sau."
                        };
                        ev.Terms.Add(new LedgerTerm { Label = VaoLabel, Value = line.In ?? 0, Sign = 1 });
                        ev.Terms.Add(new LedgerTerm { Label = "Đã giao theo NC", Value = line.HelpOut, Sign = -1 });
                        ev.Result = line.Waiting ?? 0;
                        return (Math.Max(0, Math.Min(ev.Result, G.ChuaLam)), ev);
                    }
                    var next = G.Steps.SkipWhile(s => s.Code != fromNC).Skip(1).FirstOrDefault();
                    // [TARO-LAMHO-NC] PO có Taro làm hộ: giao theo NC chỉ từ NC ngay trước đoạn máy loại trừ (hàng giao từ NC khác không thuộc đoạn nào → kẹt ở nhóm làm hộ)
                    //   (rà độc lập gói 6 mục 5)
                    if (row.ApplyWorkflow && row.LamHoRuns.Any(x => x.HasTaro) && next != null && !next.IsExcluded)
                        return (0, new LedgerExplain { Title = $"Giao theo NC chỉ từ {string.Join(" / ", row.LamHoRuns.Select(x => NcName(x.FromNc)).Distinct())}", Rule = "N3, N4",
                            Note = $"PO có Taro làm hộ ở NC máy loại trừ giữa chuỗi: giao theo NC từ NC ngay trước máy loại trừ; {NcName(fromNC)} có NC sau là NC thường ({NcName(next.Code)})." });
                    // [GN-HELP-LAST] NC cuối không giao theo NC: OK của NC cuối đã là "Đã làm chưa giao" của Gia công → giao bằng Hàng mới.
                    //   Trước: giao theo NC từ NC cuối trừ Chưa GC nhưng không trừ NC nào → Σ chờ các NC > Chưa GC → WTS nhập vượt, Chưa GC âm.
                    //   (Bản sửa 24/10 commit cục bộ 3823d97 chưa lên master 11e8f78 — đưa lại.)
                    if (next == null)
                        return (0, new LedgerExplain { Title = "Giao theo NC", Rule = "N1, N4",
                            Note = $"NC {fromNC} là NC cuối của Gia công: hàng OK ở đây là Đã làm chưa giao — giao bằng phân loại Hàng mới." });
                    // [HELP-LAST-EXCL 2026-10-29] Sau NC nguồn chỉ còn NC máy loại trừ (vd NC01, NC02 = H04): chuỗi NC thường kết thúc ở NC nguồn
                    //   → OK của nó đã là "Đã làm chưa giao" (như NC cuối). Trước: next = NC loại trừ ≠ null nên vẫn cho giao theo NC
                    //   → 1 chiếc vừa nằm ở Đã làm chưa giao vừa bị trừ khỏi Chưa GC → ô "chờ giao theo NC (máy loại trừ)" âm, WTS dự phòng bị chặn oan
                    //   (bộ kiểm chuỗi C10; fuzz CHUOI=1 LOI=1 EXCL=0.7 seed 49 PO11). Luật: giao bằng Hàng mới cho nhóm làm NC loại trừ đó.
                    if (!G.Steps.SkipWhile(s => s.Code != fromNC).Skip(1).Any(s => !s.IsExcluded))
                        return (0, new LedgerExplain { Title = "Giao theo NC", Rule = "N1, N3, N4",
                            Note = $"NC {fromNC} là NC cuối của Gia công (sau nó chỉ còn NC máy loại trừ {next.Code}): hàng OK ở đây là Đã làm chưa giao — giao bằng phân loại Hàng mới." });
                    // Hàng OK ở NC nguồn đi tiếp theo 2 đường: NC sau (nếu NC sau không phải máy loại trừ) hoặc gửi làm hộ.
                    var usedNext = next != null && !next.IsExcluded ? next.Ok + next.Ng : 0;
                    var e = new LedgerExplain
                    {
                        Title = $"Gia công · NC {fromNC} còn giao được", Rule = "N3, N4",
                        Formula = $"OK NC {fromNC} − đã giao từ NC {fromNC}" + (usedNext > 0 ? $" − NC sau ({next!.Code}) đã làm" : ""),
                    };
                    e.Terms.Add(new LedgerTerm { Label = $"OK NC {fromNC}", Value = line.Ok, Sign = 1 });
                    if (line.OkSua != 0) e.Terms.Add(new LedgerTerm { Label = $"Sửa đạt tính là xong NC {fromNC}", Value = line.OkSua, Sign = 1 });   // [SUA-OK-NC]
                    e.Terms.Add(new LedgerTerm { Label = "Đã giao theo NC", Value = line.HelpOut, Sign = -1 });
                    if (usedNext > 0) e.Terms.Add(new LedgerTerm { Label = $"NC {next!.Code} đã làm (OK + NG)", Value = usedNext, Sign = -1 });
                    e.Result = line.Ok + line.OkSua - line.HelpOut - usedNext;
                    return (Math.Max(0, e.Result), e);
                }
            }
            return (0, new LedgerExplain());
        }

        /// <summary>[VULOI] Phiếu này lấy hàng từ ô Hàng lỗi / Hàng sửa đang giữ (phải chọn vụ)? Hàng lỗi công đoạn trước: không (vụ sinh mới).</summary>
        public static bool TakesHeldCase(string purpose, string? fromCell)
            => (purpose is HandoverPurpose.RepairSend or HandoverPurpose.RepairReturn or HandoverPurpose.Scrap or HandoverPurpose.CustReturn
                && (fromCell ?? HandoverCell.IssueCellOf(purpose, null)) is HandoverCell.Loi or HandoverCell.Sua or HandoverCell.Xl)   // [SUA-XL-GIU] + hàng đã sửa (mang vụ)
               || HandoverCell.IsLamHoLoi(purpose, fromCell);   // [TARO-LAMHO-NC] trả lỗi hàng làm hộ: vụ đang giữ ô LAMHO

        /// <summary>[VULOI] Ô giữ của vụ mà phiếu lấy hàng ([TARO-LAMHO-NC] trả lỗi hàng làm hộ: ô LAMHO — chỉ trong sổ).</summary>
        public static string HeldCellOf(string purpose, string? fromCell)
            => HandoverCell.IsLamHoLoi(purpose, fromCell) ? HandoverCell.LamHo : (fromCell ?? HandoverCell.IssueCellOf(purpose, null)!);

        /// <summary>
        /// [VULOI 2026-10-28] Trần lập phiếu có tính vụ lỗi: phiếu lấy từ ô Hàng lỗi / Hàng sửa thì không vượt số vụ đó đang giữ ở nhóm giao
        /// (ngCaseId null = hàng lỗi cũ chưa có mã). Phiếu khác: như IssueCap.
        /// </summary>
        public static (decimal Max, LedgerExplain Explain) IssueCapCase(LedgerRow row, string from, string purpose, string? fromNC, string? fromCell, long? ngCaseId, string? toNC = null)
        {
            var (max, ex) = IssueCap(row, from, purpose, fromNC, fromCell, toNC);
            if (!TakesHeldCase(purpose, fromCell)) return (max, ex);
            var cell = HeldCellOf(purpose, fromCell);   // [TARO-LAMHO-NC]
            var held = row.CaseHeld(ngCaseId ?? 0, from, cell);
            if (held >= max) return (max, ex);
            var cs = row.Cases.TryGetValue(ngCaseId ?? 0, out var x) ? x : null;
            var e = new LedgerExplain
            {
                Title = $"{GroupLabel(from)} · {(ngCaseId == null ? "hàng lỗi cũ chưa có mã" : NgCaseNo.Format(ngCaseId.Value))} đang giữ ở ô {HandoverCell.Label(cell)}",
                Rule = "VULOI", Formula = "",
                Note = cs == null ? "Vụ này không có hàng ở nhóm giao." : cs.CatLabel + " — số tối đa = số của vụ này đang nằm ở ô bên giao",
                Result = Math.Max(0, held)
            };
            return (Math.Max(0, held), e);
        }

        /// <summary>[GN-LEDGER] Nhóm nhận hợp lệ theo loại phiếu (để modal gợi ý, service kiểm).</summary>
        public static string? CheckRoute(string from, string to, string purpose)
        {
            if (from == to) return "Nhóm giao và nhóm nhận không được trùng nhau.";
            switch (purpose)
            {
                case HandoverPurpose.New:
                    // [GN-ROUTE-FREE] Khách yêu cầu (24/10): giao Hàng mới được chọn nhóm nhận bất kỳ (trước: chỉ giao xuôi, Kho chỉ nhận thành phẩm từ Đóng gói).
                    // In/out không đổi: phiếu chuyển Giao·Hàng mới bên giao (trần Đã làm chưa giao) sang Nhận·Hàng mới bên nhận (vào Chưa làm, bên nhận vẫn phải bấm Nhận).
                    // Riêng Đóng gói → Kho vẫn là nhập thành phẩm (không vào phôi Kho).
                    return null;
                case HandoverPurpose.NgReturn:   // [GN-CELL] Excel: "Hàng lỗi công đoạn trước"
                    if (from == "KHO") return "Kho không có công đoạn trước để trả hàng lỗi.";
                    // [GN-NGRET-TO] Khách yêu cầu (24/10): được chọn nhóm nhận bất kỳ (trước: chỉ nhóm đứng trước).
                    // Không ảnh hưởng in/out: phiếu chỉ chuyển số từ Giao·Hàng lỗi bên giao sang Nhận·Hàng lỗi bên nhận, mọi nhóm cùng 1 công thức.
                    return null;
                case HandoverPurpose.RepairSend:
                    return null;   // [GN-ROUTE-FREE] trước: chặn gửi sửa cho Kho
                case HandoverPurpose.HelpSend:
                    if (from == "KHO") return to == "HTSP" ? null : "Kho giao thẳng Taro: nhóm nhận là HTSP (Taro).";   // [KHO-TARO-THANG 2026-11-12] (khách 12/11 3.3 b = A)
                    if (from != "GC") return "Chỉ Gia công giao theo NC.";   // giữ: "theo NC" là NC của Gia công (FromNC)
                    return null;   // [GN-ROUTE-FREE] trước: chặn giao theo NC cho Kho
                case HandoverPurpose.HelpReturn:
                    if (to != "GC") return "Hàng giao theo NC chỉ trả về Gia công.";   // giữ: hàng trả vào lại NC của Gia công
                    return null;
                case HandoverPurpose.Scrap:        // [GN-NG-OUT]
                case HandoverPurpose.CustReturn:
                    if (to != HandoverPurpose.OutTarget(purpose)) return "Phiếu Huỷ / Trả khách không có nhóm nhận.";
                    if (!GroupCodes.Contains(from)) return "Nhóm giao không hợp lệ.";
                    return null;
                default:
                    return null;
            }
        }
    }
}
