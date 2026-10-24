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
    /// [GN-CELL 2026-10-23] Ô trên Excel xưởng (mỗi nhóm: Nhận / Giao × Hàng mới · Hàng lỗi · Hàng sửa · Hàng lỗi đã xử lý).
    /// Form Giao: người giao chọn Ô GIAO (+ tích "Hàng lỗi công đoạn trước"), phần mềm suy loại phiếu và Ô NHẬN của bên nhận.
    /// </summary>
    public static class HandoverCell
    {
        public const string Moi = "MOI";   // Hàng mới
        public const string Loi = "LOI";   // Hàng lỗi
        public const string Sua = "SUA";   // Hàng sửa
        public const string Xl  = "XL";    // Hàng lỗi đã xử lý (chỉ là ô NHẬN — không giao từ ô này)

        public static readonly string[] IssueCells = { Moi, Loi, Sua };

        public static string Label(string? c) => c switch
        {
            Moi => "Hàng mới", Loi => "Hàng lỗi", Sua => "Hàng sửa", Xl => "Hàng lỗi đã xử lý", _ => c ?? ""
        };

        /// <summary>Ô GIAO của bên giao theo loại phiếu (phiếu cũ không có FromCell). Làm hộ / Huỷ / Trả khách: không nằm trong 4 ô Excel → null.</summary>
        public static string? IssueCellOf(string purpose, string? fromCell) => purpose switch
        {
            HandoverPurpose.New          => Moi,
            HandoverPurpose.NgReturn     => Loi,
            HandoverPurpose.RepairSend   => Loi,
            HandoverPurpose.RepairReturn => fromCell == Loi ? Loi : Sua,   // TH2: nơi gây lỗi tự sửa xong → giao từ ô Hàng lỗi
            HandoverPurpose.Scrap        => Loi,
            HandoverPurpose.CustReturn   => Loi,
            _ => null
        };

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
        public decimal? Waiting  { get; set; }
        /// <summary>GC: đã gửi làm hộ từ NC này.</summary>
        public decimal HelpOut   { get; set; }
        public string? Note      { get; set; }
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
        /// <summary>Tồn = ΣNhận − ΣGiao + Chờ nhận. [GN-RECV-NG] trước: ΣNhận − ΣGiao (hàng chờ nhận nằm ngoài, ở dòng Đang giao).</summary>
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

        public decimal TonTong => Groups.Values.Sum(g => g.Ton);
        /// <summary>L6: Phôi = Σ Tồn các nhóm (gồm chờ nhận) + thành phẩm chờ Kho nhận + thành phẩm đã nhập kho + đã huỷ + đã trả khách [GN-NG-OUT][GN-RECV-NG].</summary>
        public bool    Balanced => Phoi == TonTong + DangGiao + TpNhap + DaHuy + TraKhach;
        public LedgerExplain BalanceExplain { get; set; } = new();

        /// <summary>[GN-WTS-CAP] Mã trên WTS → mã chính (N2: máy đồng dạng / mã dự phòng gộp vào mã chính). Key = "PG|mã".</summary>
        public Dictionary<string, string> CodeMain { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings => Groups.Values.SelectMany(g => g.Warnings).ToList();
        public bool HasWarning => Warnings.Count > 0 || !Balanced;
        public bool HasLoiGiu  => Groups.Values.Any(g => g.LoiGiu > 0 || g.SuaGiu > 0);   // [GN-CELL] cả 2 ô đang giữ
    }

    /// <summary>[GN-CELL] Một dòng "Bước" của PO — đúng kiểu Excel xưởng (B1, B2, B3…): trạng thái sổ SAU một chứng từ.</summary>
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
        public List<InTx>   Txs  { get; set; } = new();
        public List<InWts>  Wts  { get; set; } = new();
        /// <summary>Snapshot NC gia công (gồm cả máy đồng dạng IsBackup).</summary>
        public List<InGcStep>  GcSteps  { get; set; } = new();
        /// <summary>Snapshot mã CV HTSP (TARO/BAVIA/WASHING), KCS, PKG.</summary>
        public List<InCodeStep> CodeSteps { get; set; } = new();
    }

    public record InPhoi(long Id, DateTime? At, decimal Qty, string? Note);
    public record InRecv(long Id, DateTime At, decimal Ok, decimal Ng, string? NgReason,
                         string? Cell = null, string? CellReason = null);   // [GN-CELL2] ô bên nhận thực nhận + lý do đổi
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
        public List<InRecv> Receives { get; set; } = new();
    }
    public record InWts(long Id, DateTime At, string ProcessGroup, string? NC, string? WtsCode,
                        decimal Ok, decimal Ng, string? Worker);
    public record InGcStep(int StepOrder, string NC, bool IsBackup, string? ParentNC, string? Machine, bool IsExcluded);
    /// <summary>Pg = TARO | BAVIA | WASHING | KCS | PKG; Code = mã CV khớp với WtsCode ?? NC của WTS.</summary>
    public record InCodeStep(string Pg, int StepOrder, string Code, bool IsBackup, string? ParentCode, string? StepName);

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

            var wts = await _db.WtsProductionLogs
                .Where(w => ids.Contains(w.KhPlanDetailId) && !w.IsVoided)
                .Select(w => new
                {
                    w.WtsLogId, w.KhPlanDetailId, w.EndTime, w.ProcessGroup, w.NC, w.WtsCode,
                    w.QtyDone, w.QtyOk, w.QtyNg,
                    Worker = w.Worker != null ? (w.Worker.FullName ?? w.Worker.Username) : null
                })
                .AsNoTracking().ToListAsync();

            var mach = await _db.KhPlanRouteSnapshotMachining.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.MachineRegistered })
                .AsNoTracking().ToListAsync();
            var excl = await _db.KhPlanRouteSnapshotMachineExclude.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.SoMay })
                .AsNoTracking().ToListAsync();
            var taro = await _db.KhPlanRouteSnapshotTaro.Where(s => ids.Contains(s.KhPlanDetailId))
                .Select(s => new { s.KhPlanDetailId, s.StepOrder, s.NC, s.WtsTaskCode, s.IsBackup, s.ParentNC, s.StepName })
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
                    Txs = txs.Where(t => t.KhPlanDetailId == id).Select(t => new InTx
                    {
                        Id = t.HandoverTxId, From = t.FromGroupCode, To = t.ToGroupCode, Purpose = t.Purpose,
                        FromCell = t.FromCell, // [GN-CELL]
                        IsReturnNg = t.IsReturnNg, Issued = t.QtyIssued, Status = t.Status,
                        FromNC = t.FromNC, ToNC = t.ToNC, At = t.IssuedAt, Notes = t.Notes, NgReason = t.NgReason, // [GN-NG-OUT]
                        SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                        ToCell = t.ToCell, // [GN-CELL2]
                        Receives = t.Receives.Where(r => !r.IsVoided)
                                    .Select(r => new InRecv(r.ReceiveId, r.ReceivedAt, r.QtyOk, r.QtyNg, r.NgReason, r.ToCell, r.ToCellReason)).ToList()
                    }).ToList(),
                    Wts = wts.Where(w => w.KhPlanDetailId == id).Select(w => new InWts(
                        w.WtsLogId, w.EndTime, w.ProcessGroup, w.NC, w.WtsCode,
                        w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0)), w.QtyNg ?? 0, w.Worker)).ToList(),
                    GcSteps = mach.Where(m => m.KhPlanDetailId == id).Select(m => new InGcStep(
                        m.StepOrder, m.NC ?? "", m.IsBackup, m.ParentNC, m.MachineRegistered,
                        !string.IsNullOrWhiteSpace(m.MachineRegistered) && exclSet.Contains(m.MachineRegistered!.Trim()))).ToList(),
                };
                // Mã CV trên WTS: Taro = WtsTaskCode (trống thì NC) — khớp HieuSuat_Index; Bavia / Rửa / KCS / Đóng gói = NC
                // [GN-WTS-CAP] trước: WtsTaskCode ?? NC → WtsTaskCode = "" thì mã rỗng, không khớp WTS
                // [GN-WTS-CAP] Taro dự phòng: ParentNC là NC của dòng cha → đổi sang mã CV của dòng cha để gộp đúng (N2)
                var taroOf = taro.Where(s => s.KhPlanDetailId == id).ToList();
                string TaroCode(string? wtsTaskCode, string? nc) => string.IsNullOrWhiteSpace(wtsTaskCode) ? (nc ?? "") : wtsTaskCode!;
                string? TaroParentCode(string? parentNc)
                {
                    if (string.IsNullOrWhiteSpace(parentNc)) return parentNc;
                    var p = taroOf.FirstOrDefault(x => !x.IsBackup && string.Equals(x.NC, parentNc, StringComparison.OrdinalIgnoreCase));
                    return p == null ? parentNc : TaroCode(p.WtsTaskCode, p.NC);
                }
                inp.CodeSteps.AddRange(taroOf.Select(s => new InCodeStep("TARO", s.StepOrder, TaroCode(s.WtsTaskCode, s.NC), s.IsBackup, TaroParentCode(s.ParentNC), s.StepName)));
                inp.CodeSteps.AddRange(bavia.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("BAVIA", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(washing.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("WASHING", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(kcs.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("KCS", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
                inp.CodeSteps.AddRange(pkg.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("PKG", s.StepOrder, s.NC, s.IsBackup, s.ParentNC, s.StepName)));
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
               + (string.IsNullOrEmpty(t.FromNC) ? "" : $" · từ NC {t.FromNC}")
               + (string.IsNullOrEmpty(t.ToNC) ? "" : $" · vào NC {t.ToNC}")
               + (t.Status == "COMPLETED" ? "" : $" · {(Received(t) > 0 ? "nhận dở" : "chờ nhận")} (phiếu {t.Issued:0.##})");

        /// <summary>[GN-CELL2] Nhãn phiếu theo cặp ô: "Hàng lỗi ☑ → ô Hàng lỗi". Phiếu theo NC / Huỷ / Trả khách: tên loại phiếu.</summary>
        public static string PairLabel(InTx t, string purpose)
        {
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

        public static LedgerRow Compute(LedgerInput inp)
        {
            var row = new LedgerRow
            {
                KhPlanDetailId = inp.KhPlanDetailId, PlanNo = inp.PlanNo, PurchaseOrder = inp.PurchaseOrder,
                PartNo = inp.PartNo, CustomerName = inp.CustomerName, PlanQty = inp.PlanQty,
                ApplyWorkflow = inp.ApplyWorkflow,
            };
            foreach (var c in GroupCodes)
                row.Groups[c] = new LedgerGroup { Code = c, Label = GroupLabel(c) };

            // ── Chuỗi NC gia công (N1, N2, N3) ─────────────────────────
            var gcChain = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).ToList();
            var gcLastNc = gcChain.LastOrDefault()?.NC;

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
            void NgBack(InTx t, string purpose, InRecv r, string bucket, bool skipReceiver)
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
                    continue;
                }
                if (!row.Groups.ContainsKey(t.From) || !row.Groups.ContainsKey(t.To)) continue;
                // [GN-RECV-NG] Phiếu trả tự lập: số tính ở lần nhận gốc; ở đây chỉ đếm badge chờ nhận
                if (t.SourceReceiveId != null)
                {
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
                string? fromCell = isHelp ? null
                    : t.FromCell ?? (purpose == HandoverPurpose.RepairReturn
                        ? (suaRunning[t.From] >= giao && giao > 0 ? HandoverCell.Sua : HandoverCell.Loi)   // [GN-CELL] phiếu Trả hàng đã sửa cũ
                        : HandoverCell.IssueCellOf(purpose, null));
                bool prevNg = purpose == HandoverPurpose.NgReturn;
                string? desig = isHelp ? null : DesigCellOf(t, purpose);
                // Thành phẩm: Đóng gói → Kho hàng mới — không vào phôi Kho. [GN-CELL2] chỉ phần Kho nhận vào ô Hàng mới mới là thành phẩm.
                bool isTpTx = t.From == "PKG" && t.To == "KHO" && fromCell == HandoverCell.Moi;

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
                            Add("GC", "helpOut:" + t.FromNC, "Giao theo NC " + t.FromNC, giao, txDoc);
                    }
                    else Add(t.From, "ghRet", "Trả về Gia công theo NC", giao, txDoc);
                }
                else switch (fromCell)
                {
                    case HandoverCell.Moi:
                        Add(t.From, "gm", "Giao Hàng mới", giao, txDoc);
                        break;
                    case HandoverCell.Loi:
                        if (prevNg)
                        {
                            Add(t.From, "glTra", "Giao Hàng lỗi (trả nơi gây lỗi)", giao, txDoc);
                            // [GN-NC-PIN] Gia công: phiếu mới chốt NC phát hiện lỗi (FromNC) → trừ đúng NC đó, không trôi theo dữ liệu sau (để K5 bắt được)
                            if (t.From == "GC" && !string.IsNullOrEmpty(t.FromNC))
                                Add("GC", "rrNc:" + t.FromNC, "Trả hàng lỗi phát hiện ở NC " + t.FromNC, giao,
                                    new LedgerDoc { Kind = "PHIEU", RefId = t.Id, At = t.At, Text = TxText(t, purpose) });
                        }
                        else if (desig == HandoverCell.Sua) Add(t.From, "glGui", "Giao Hàng lỗi (gửi sửa)", giao, txDoc);
                        else if (desig == HandoverCell.Loi) Add(t.From, "glChuyen", "Giao Hàng lỗi (chuyển sang ô Hàng lỗi nhóm khác)", giao, txDoc);   // [GN-CELL2]
                        else Add(t.From, "glTu", "Giao Hàng lỗi (tự sửa xong, trả nơi phát hiện)", giao, txDoc);
                        break;
                    case HandoverCell.Sua:
                        Add(t.From, "gs", "Giao Hàng sửa (sửa hộ xong, trả nơi phát hiện)", giao, txDoc);
                        suaRunning[t.From] -= giao;
                        break;
                }

                // ---- Bên nhận ---- [GN-CELL2] từng lần nhận, vào ô bên nhận THỰC CHỌN (mặc định ô bên giao chỉ định)
                if (isHelp)
                {
                    if (rec > 0)
                    {
                        if (purpose == HandoverPurpose.HelpSend) Add(t.To, "nhSend", "Nhận hàng Gia công giao theo NC", rec, RecDoc(""));
                        else
                        {
                            Add(t.To, "nhRet", "Nhận vào NC (hàng trả về)", rec, RecDoc(""));
                            if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC)) Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, recOk, RecDoc("")); // [GN-WTS-CAP] chỉ phần OK — NG lúc nhận trả ngay, không vào NC đích
                        }
                    }
                    foreach (var r in t.Receives.Where(x => x.Ng > 0).OrderBy(x => x.At))
                        NgBack(t, purpose, r, purpose == HandoverPurpose.HelpSend ? "glLamHo" : "glTra", false);
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
                    if (tpHere) row.TpNhap += r.Ok;
                    else
                    {
                        AddRecvCell(t.To, cell, r.Ok + r.Ng, rDoc);
                        if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC) && cell is HandoverCell.Moi or HandoverCell.Xl)
                            Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, r.Ok, new LedgerDoc { Kind = rDoc.Kind, RefId = rDoc.RefId, At = rDoc.At, Text = rDoc.Text }); // chỉ phần OK
                        if (cell == HandoverCell.Sua) suaRunning[t.To] += r.Ok; // [GN-CELL] phần NG đếm lúc nhận trả ngay, không vào ô Hàng sửa
                    }
                    // NG đếm lúc nhận: nhận vào ô Hàng lỗi thì toàn bộ đã là hàng lỗi, không trả lại lần nữa
                    if (r.Ng > 0 && cell != HandoverCell.Loi)
                        NgBack(t, purpose, r, cell == HandoverCell.Sua ? "glSuaNg" : "glTra", tpHere);
                }
            }

            // ── WTS ─────────────────────────────────────────────────────
            foreach (var w in inp.Wts)
            {
                var g = Pg2Group(w.ProcessGroup);
                if (g == "") continue;
                if (w.Ng != 0)
                    Add(g, "ngc", "NG (WTS)", w.Ng, new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w) });
            }

            // Đã làm (OK) theo luật từng nhóm
            ComputeGcOk(inp, gcChain, row, T);
            foreach (var g in new[] { "HTSP", "KCS", "PKG" })
                ComputeCodeOk(inp, g, row, T);

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
                G.NhanLoiXl = V("nx");
                G.NhanLamHo = V("nhSend") + V("nhRet");
                G.NhanLamHoVe = V("nhRet"); // [GN-WTS-CAP]
                G.GiaoMoi   = V("gm");
                G.GiaoLoi   = V("glTra") + V("glGui") + V("glTu") + V("glChuyen") /* [GN-CELL2] */ + V("glSuaNg") + V("glLamHo") + V("glHuy") + V("glKhach"); // [GN-NG-OUT] + huỷ / trả khách; [GN-CELL] + tự sửa trả, + trả lại hàng nhờ sửa NG
                G.GiaoSua   = V("gs");
                G.GiaoLoiXl = 0; // [GN-LEDGER] Excel có cột này nhưng cả 3 tình huống không dùng (hàng đã xử lý chỉ NHẬN, không GIAO từ ô này)
                G.GiaoLamHo = V("ghSend") + V("ghRet");
                G.ChoNhan   = V("cn");                              // [GN-RECV-NG] mục 37
                G.Ton       = G.NhanTong - G.GiaoTong + G.ChoNhan;  // [GN-RECV-NG] trước: NhanTong − GiaoTong
                G.NgCuoi    = V("ngc");
                G.NgDau     = V("glTra");

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
                ListExplain("NhanLoiXl", "Nhận · Hàng lỗi đã xử lý", ("nx", "Hàng đã sửa trả về (đã OK, nhập WTS tiếp được)"));
                ListExplain("NhanLamHo", "Nhận · Giao theo NC",     ("nhSend", "Gia công giao theo NC (máy loại trừ)"), ("nhRet", "Hàng trả về vào NC"));
                ListExplain("GiaoMoi",   "Giao · Hàng mới",          ("gm", "Phiếu Hàng mới (gồm phiếu chờ nhận)"));
                ListExplain("GiaoLoi",   "Giao · Hàng lỗi",          ("glTra", "Hàng lỗi công đoạn trước — trả nơi gây lỗi"), ("glGui", "Gửi hàng lỗi đi nhờ sửa"), ("glTu", "Tự sửa xong, trả nơi phát hiện"), ("glChuyen", "Chuyển sang ô Hàng lỗi nhóm khác"), ("glSuaNg", "Trả lại hàng nhờ sửa bị NG lúc nhận"), ("glLamHo", "Trả lỗi từ hàng giao theo NC"), ("glHuy", "Huỷ hàng lỗi"), ("glKhach", "Trả khách hàng lỗi")); // [GN-NG-OUT][GN-CELL]
                ListExplain("GiaoSua",   "Giao · Hàng sửa",          ("gs", "Sửa hộ xong, trả nơi phát hiện"));
                ListExplain("GiaoLamHo", "Giao · Giao theo NC",     ("ghSend", "Giao theo NC (máy loại trừ)"), ("ghRet", "Trả về Gia công theo NC"));
                G.Explains["GiaoLoiXl"] = new LedgerExplain
                {
                    Title = $"{G.Label} · Giao · Hàng lỗi đã xử lý", Formula = "—",
                    Note = "Cột có trong Excel của xưởng nhưng cả 3 tình huống đều không ghi vào đây: hàng đã xử lý chỉ NHẬN về nơi phát hiện rồi làm tiếp (WTS), không giao đi từ ô này. Cột để trống cho đúng Excel."
                };

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
                        Title = $"{G.Label} · Tồn", Formula = "Tổng Nhận − Tổng Giao + Chờ nhận", Rule = "L6",
                        Note = "Tồn là số hàng thuộc nhóm. Bên giao đã bấm Giao thì hàng tính sang Tồn bên nhận (phần Chờ nhận), dù bên nhận chưa bấm Nhận. "
                             + "Phần Chờ nhận chưa được nhập WTS: phải đếm và bấm Nhận trước (không nằm trong Chưa làm).",
                        Result = G.Ton
                    };
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Nhận", Value = G.NhanTong, Sign = 1, Docs = AllDocs(acc, code, "nm", "nl", "ns", "nx", "nhSend", "nhRet") });
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Giao", Value = G.GiaoTong, Sign = -1, Docs = AllDocs(acc, code, "gm", "glTra", "glGui", "glTu", "glChuyen", "glSuaNg", "glLamHo", "glHuy", "glKhach", "gs", "ghSend", "ghRet") });
                    if (G.ChoNhan != 0)
                        e.Terms.Add(new LedgerTerm { Label = "Chờ nhận (đã giao tới, chưa bấm Nhận)", Value = G.ChoNhan, Sign = 1, Docs = AllDocs(acc, code, "cn") });
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
                }

                if (code == "KHO")
                {
                    // Kho: phôi tốt còn giao + hàng lỗi đang giữ
                    G.ConGiao = G.NhanMoi + G.NhanLoiXl - G.GiaoMoi - V("glTra");
                    // [GN-CELL] Tách 2 ô đang giữ theo Excel: Hàng lỗi (của mình) / Hàng sửa (nhóm khác gửi nhờ)
                    G.LoiGiu  = G.NhanLoi - V("glGui") - V("glTu") - V("glChuyen") - V("glHuy") - V("glKhach"); // [GN-NG-OUT] [GN-CELL2] + chuyển hàng lỗi
                    G.SuaGiu  = G.NhanSua - G.GiaoSua - V("glSuaNg");
                    G.LamHoGiu = V("nhSend") - V("ghRet") - V("glLamHo");   // [GN-ROUTE-FREE] trước: 0 (Gia công không giao theo NC cho Kho được)
                    var e = new LedgerExplain { Title = "Kho · Phôi còn giao", Formula = "Phôi nhận + Phôi đã sửa trả về − Giao Hàng mới − Trả lỗi", Rule = "L4", Result = G.ConGiao };
                    e.Terms.Add(Tm("nm", "Phôi Kho nhận", 1));
                    e.Terms.Add(Tm("nx", "Phôi đã sửa trả về", 1));
                    e.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    e.Terms.Add(Tm("glTra", "Trả lỗi", -1));
                    e.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Phôi Kho nhận");
                    G.Explains["ConGiao"] = e;
                    var l = new LedgerExplain { Title = "Kho · Tồn hàng lỗi", Formula = "Nhận Hàng lỗi − Gửi đi nhờ sửa − Tự sửa trả − Chuyển hàng lỗi − Huỷ − Trả khách", Rule = "L5, N9", Result = G.LoiGiu,
                        Note = "Phôi lỗi bị trả về Kho (Excel TH1 B1), chờ Kho gửi nhờ sửa (TH1 B2), huỷ hoặc trả khách." };
                    l.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1));
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
                    G.ChuaLam = G.NhanMoi + G.NhanLoiXl + V("nhRet") - G.DaLam - G.NgCuoi - V("glTra") - V("ghSend");
                    G.ConGiao = G.DaLam - G.GiaoMoi;
                    // [GN-CELL] Tách 2 ô đang giữ theo Excel: Hàng lỗi (của mình: bị trả về + NG làm hỏng) / Hàng sửa (nhóm khác gửi nhờ)
                    G.LoiGiu  = G.NhanLoi + G.NgCuoi - V("glGui") - V("glTu") - V("glChuyen") - V("glHuy") - V("glKhach"); // [GN-NG-OUT] [GN-CELL2] + chuyển hàng lỗi
                    G.SuaGiu  = G.NhanSua - G.GiaoSua - V("glSuaNg");
                    G.LamHoGiu = V("nhSend") - V("ghRet") - V("glLamHo");

                    var e = new LedgerExplain
                    {
                        Title = $"{G.Label} · Chưa làm", Rule = "L3",
                        Formula = "Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + Hàng trả về vào NC − Đã làm − NG (WTS) − Trả hàng lỗi − Giao theo NC",
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

                    var cg = new LedgerExplain { Title = $"{G.Label} · Đã làm chưa giao", Formula = "Đã làm − Giao Hàng mới", Rule = "L4",
                        Note = "Trần khi lập phiếu Hàng mới. Tính cả phiếu đã giao nhưng bên nhận chưa nhận.", Result = G.ConGiao };
                    cg.Terms.Add(new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = 1, Docs = daLamTerm.Docs });
                    cg.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    G.Explains["ConGiao"] = cg;

                    var lg = new LedgerExplain { Title = $"{G.Label} · Tồn hàng lỗi",
                        Formula = "Nhận Hàng lỗi + NG (WTS) − Gửi đi nhờ sửa − Tự sửa trả − Chuyển hàng lỗi − Huỷ − Trả khách", Rule = "L5, K1",
                        Note = "Hàng lỗi CỦA NHÓM (bị trả về vì lỗi công đoạn trước, hoặc tự làm hỏng). Trần khi giao phân loại Hàng lỗi không tích: gửi nhờ sửa / tự sửa xong trả về; và khi Huỷ / Trả khách.", Result = G.LoiGiu }; // [GN-PLH] mục 36: trước "giao từ ô"
                    lg.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1)); lg.Terms.Add(Tm("ngc", "NG (WTS)", 1));
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

                    var lh = new LedgerExplain { Title = $"{G.Label} · Tồn hàng giao theo NC", Formula = "Nhận hàng giao theo NC − Đã trả về Gia công − Lỗi trả lại", Rule = "N3, K4",
                        Note = "Hàng của nhóm khác, không tính vào số sản xuất của nhóm này.", Result = G.LamHoGiu };
                    lh.Terms.Add(Tm("nhSend", "Nhận theo NC", 1)); lh.Terms.Add(Tm("ghRet", "Trả về Gia công theo NC", -1)); lh.Terms.Add(Tm("glLamHo", "Lỗi trả lại", -1));
                    lh.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận theo NC");
                    G.Explains["LamHoGiu"] = lh;

                    if (G.ChuaLam < 0)  G.Warnings.Add($"{G.Label}: Chưa làm âm ({G.ChuaLam:0.##}) — WTS / trả lỗi nhiều hơn hàng nhận vào (L3)");
                    if (G.ConGiao < 0)  G.Warnings.Add($"{G.Label}: giao Hàng mới nhiều hơn số đã làm ({G.ConGiao:0.##}) — L4");
                    if (G.LoiGiu < 0)   G.Warnings.Add($"{G.Label}: giao hàng lỗi nhiều hơn tồn hàng lỗi ({G.LoiGiu:0.##}) — K1");
                    if (G.SuaGiu < 0)   G.Warnings.Add($"{G.Label}: giao hàng sửa nhiều hơn tồn hàng sửa ({G.SuaGiu:0.##}) — K1");
                    if (G.LamHoGiu < 0) G.Warnings.Add($"{G.Label}: trả về Gia công nhiều hơn tồn theo NC ({G.LamHoGiu:0.##}) — K4");
                }

                // Kiểm tách: Tồn = các phần đang giữ (phát hiện lỗi chương trình nếu lệch) — [GN-CELL] + ô Hàng sửa
                var parts = code == "KHO" ? G.ConGiao + G.LoiGiu + G.SuaGiu + G.LamHoGiu /* [GN-ROUTE-FREE] */ : G.ChuaLam + G.ConGiao + G.LoiGiu + G.SuaGiu + G.LamHoGiu;
                parts += G.ChoNhan;   // [GN-RECV-NG] mục 37: Tồn gồm cả phần chờ nhận
                if (parts != G.Ton) G.Warnings.Add($"{G.Label}: Tồn {G.Ton:0.##} khác tổng các phần {parts:0.##} — báo lại để kiểm");
            }

            // [GN-RECV-NG] Phiếu trả tự lập mà lần nhận gốc đã huỷ (không được xảy ra — huỷ lần nhận thì phiếu trả huỷ theo)
            foreach (var o in orphanAuto)
                row.G(o.From).Warnings.Add($"Phiếu trả #{o.Id} ({GroupLabel(o.From)} → {GroupLabel(o.To)}) mất lần nhận gốc — báo lại để kiểm");

            // ── Chi tiết NC / mã CV ──────────────────────────────────────
            BuildGcSteps(inp, gcChain, row, acc, recvNgSx["GC"]);
            foreach (var g in new[] { "HTSP", "KCS", "PKG" })
                FillCodeWaiting(row, g, acc, recvNgSx[g]);

            // ── Cân bằng PO (L6) ─────────────────────────────────────────
            var b = new LedgerExplain
            {
                Title = "Cân bằng PO", Rule = "L6",
                // [GN-RECV-NG] mục 37: hàng chờ nhận đã nằm trong Tồn bên nhận; chỉ còn thành phẩm chờ Kho nhận đứng riêng
                Formula = "Phôi nhận = Tồn Kho + Tồn GC + Tồn HTSP + Tồn KCS + Tồn Đóng gói + Thành phẩm chờ Kho nhận + Thành phẩm nhập kho + Đã huỷ + Đã trả khách",
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

        public static List<LedgerStepRow> ComputeSteps(LedgerInput inp)
        {
            var gcLastNc = inp.GcSteps.Where(s => !s.IsBackup && !s.IsExcluded).OrderBy(s => s.StepOrder).LastOrDefault()?.NC;
            var partial = new LedgerInput
            {
                KhPlanDetailId = inp.KhPlanDetailId, PlanNo = inp.PlanNo, PurchaseOrder = inp.PurchaseOrder, PartNo = inp.PartNo,
                CustomerName = inp.CustomerName, PlanQty = inp.PlanQty, ApplyWorkflow = inp.ApplyWorkflow,
                GcSteps = inp.GcSteps, CodeSteps = inp.CodeSteps,
            };
            var copies = new Dictionary<long, InTx>();
            InTx Copy(InTx t)
            {
                if (!copies.TryGetValue(t.Id, out var c))
                {
                    c = new InTx
                    {
                        Id = t.Id, From = t.From, To = t.To, Purpose = t.Purpose, FromCell = t.FromCell, IsReturnNg = t.IsReturnNg,
                        Issued = t.Issued, Status = HandoverPurpose.IsOut(t.Purpose) ? t.Status : "PENDING",
                        FromNC = t.FromNC, ToNC = t.ToNC, At = t.At, Notes = t.Notes, NgReason = t.NgReason,
                        SourceReceiveId = t.SourceReceiveId, // [GN-RECV-NG]
                        ToCell = t.ToCell, // [GN-CELL2]
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
                    HandoverPurpose.HelpSend   => $"{GroupLabel(t.From)} giao theo NC {t.Issued:0.##}" + (string.IsNullOrEmpty(t.FromNC) ? "" : $" (NC {t.FromNC})") + $" → {GroupLabel(t.To)}",
                    HandoverPurpose.HelpReturn => $"{GroupLabel(t.From)} trả về Gia công {t.Issued:0.##} → {GroupLabel(t.To)}" + (string.IsNullOrEmpty(t.ToNC) ? "" : $" (vào NC {t.ToNC})"),
                    _ => $"{GroupLabel(t.From)} giao {t.Issued:0.##} · ô {HandoverCell.Label(cell)}"
                         + (purpose == HandoverPurpose.NgReturn ? " ☑ lỗi công đoạn trước" : "")
                         + (purpose == HandoverPurpose.NgReturn && !string.IsNullOrEmpty(t.FromNC) ? $" (phát hiện ở NC {t.FromNC})" : "")   // [GN-NC-PIN]
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
                        if (last) c.Status = ttx.Status;
                        else if (c.Status == "PENDING") c.Status = "PARTIAL";
                        if (back != null) Copy(back);   // [GN-RECV-NG] phiếu trả sinh cùng lúc, chờ bên giao nhận
                    }, new LedgerStepRow
                    {
                        Kind = "NHAN", Group = t.To, Other = t.From, Cell = recCell, Qty = r.Ok + r.Ng, Ng = r.Ng, RefId = r.Id,
                        Text = purpose is HandoverPurpose.HelpSend or HandoverPurpose.HelpReturn
                            ? $"{GroupLabel(t.To)} nhận {r.Ok + r.Ng:0.##} giao theo NC" + (r.Ng > 0 ? (back != null ? $" (NG {r.Ng:0.##} → phiếu trả #{back.Id})" : $" (NG {r.Ng:0.##} trả ngay)") : "") + $" · từ {GroupLabel(t.From)} · phiếu #{t.Id}"
                            : $"{GroupLabel(t.To)} nhận {r.Ok + r.Ng:0.##} · ô {HandoverCell.Label(recCell)}"
                              + (recCell is HandoverCell.Xl ? " (SP đã OK, nhập WTS được)" : "")
                              + CellChangeNote(desigCell, r)   // [GN-CELL2]
                              + (r.Ng > 0 && recCell != HandoverCell.Loi ? (back != null   // [GN-RECV-NG2][GN-CELL2] nhận vào ô Hàng lỗi: số ở ô NG là hàng lỗi nhận, không phải "NG lúc nhận"
                                    ? $" · NG lúc nhận {r.Ng:0.##} → phiếu trả #{back.Id} chờ {GroupLabel(t.From)} nhận"   // [GN-RECV-NG]
                                    : $" · NG lúc nhận {r.Ng:0.##} trả ngay {GroupLabel(t.From)}") : "")
                              + (isAuto ? " (hàng lỗi trả lại lúc nhận)" : "")   // [GN-RECV-NG]
                              + $" · từ {GroupLabel(t.From)} · phiếu #{t.Id}"
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

            (InCodeStep step, decimal ok, decimal ng, List<InWts> rows)? min = null;
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
                G.Steps.Add(new LedgerStepLine
                {
                    Pg = m.Pg, // [GN-WTS-CAP]
                    SubGroup = SubLabel(m.Pg), Code = m.Code, StepOrder = m.StepOrder, Ok = ok, Ng = ng, IsPrep = prep,
                    Note = prep ? "mã chuẩn bị — không tính số lượng" : m.StepName
                });
                if (!prep && (min == null || ok < min.Value.ok)) min = (m, ok, ng, rows);
            }
            var t = T(group, "ok", $"OK nhỏ nhất: {SubLabel(min!.Value.step.Pg)} {min.Value.step.Code}");
            t.Value = min.Value.ok;
            foreach (var w in min.Value.rows.Where(w => w.Ok != 0))
                t.Docs.Add(new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w), Qty = w.Ok });
            foreach (var s in G.Steps.Where(s => !s.IsPrep && s.Code == min.Value.step.Code && s.SubGroup == SubLabel(min.Value.step.Pg)))
                s.IsLast = true; // đánh dấu mã đang quyết định Đã làm
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
            var pinnedTotal = inp.GcSteps.Where(s => !s.IsBackup).Select(s => s.NC).Distinct().Sum(nc => V("rrNc:" + nc));
            var rrLeft = V("glTra") - recvNg - pinnedTotal;
            LedgerStepLine? lastLine = null;
            var logs = inp.Wts.Where(w => w.ProcessGroup == "GC").ToList();
            var all  = inp.GcSteps.Where(s => !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            if (!all.Any()) return;

            // Hàng vào NC đầu = mọi hàng vào sản xuất GC không chỉ định NC − trả hàng lỗi (phát hiện trước khi làm)
            var targeted = chain.Sum(s => V("in:" + s.NC));
            decimal prevOk = 0, prevHelpOut = 0;
            bool first = true;
            bool afterExcluded = false; // [GN-WTS-CAP][N3] vừa đi qua 1 NC máy loại trừ
            foreach (var s in all)
            {
                var codes = NcCodes(inp, s.NC);
                var ok = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ok);
                var ng = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ng);
                var line = new LedgerStepLine
                {
                    Pg = "GC", // [GN-WTS-CAP]
                    SubGroup = "GC", Code = s.NC, StepOrder = s.StepOrder, Machine = s.Machine,
                    IsExcluded = s.IsExcluded, Ok = ok, Ng = ng, IsLast = chain.Count > 0 && s.NC == chain.Last().NC,
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
                var pin = V("rrNc:" + s.NC);   // [GN-NC-PIN]
                if (pin != 0) { input -= pin; line.Note += $" − trả hàng lỗi phát hiện ở NC này {pin:0.##}"; }
                if (rrLeft > 0)
                {
                    var take = Math.Min(Math.Max(0, input - ok - ng), rrLeft);   // [GN-WTS-CAP][N10]
                    if (take > 0) { input -= take; rrLeft -= take; line.Note += $" − trả hàng lỗi {take:0.##}"; }
                }
                line.In = input;
                lastLine = line;
                line.Waiting = input - ok - ng;
                if (line.Waiting < 0)
                    G.Warnings.Add($"Gia công NC {s.NC}: OK + NG ({ok + ng:0.##}) nhiều hơn số nhận vào NC ({input:0.##}) — WTS nhập vượt (L3, N1)");
                G.Steps.Add(line);
                prevOk = ok;
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
            var mains = G.Steps.Where(s => !s.IsPrep).ToList();
            if (!mains.Any()) return;
            var gnet  = G.NhanMoi + G.NhanLoiXl + V("nhRet") - recvNg - V("ghSend");
            var rr    = V("glTra") - recvNg;
            var ngAll = mains.Sum(s => s.Ng);
            foreach (var s in mains)
            {
                var gone = (ngAll - s.Ng) + rr;
                var perm = gnet - s.Ok - s.Ng - Math.Max(0, gone - s.Ok);
                var w    = Math.Min(perm, G.ChuaLam);
                s.Waiting = w;
                s.In      = w + s.Ok + s.Ng;
                if (perm < 0)
                    G.Warnings.Add($"{G.Label} {s.SubGroup} {s.Code}: OK + NG ({s.Ok + s.Ng:0.##}) nhiều hơn số nhận vào mã — WTS nhập vượt (L3, N5)");
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
                Formula = "Nhận vào − OK − NG",
                Note = pg == "GC"
                    ? $"Nhận vào NC: {line.Note}. Máy đồng dạng gộp chung số với NC chính."
                    : "Nhận vào mã = hàng nhận vào sản xuất của nhóm − chiếc đã rời nhóm (NG ở mã khác, trả hàng lỗi) mà mã này chưa làm; không vượt Chưa làm của cả nhóm.",
                Result = line.Waiting ?? 0
            };
            ex.Terms.Add(new LedgerTerm { Label = "Nhận vào", Value = line.In ?? 0, Sign = 1 });
            ex.Terms.Add(new LedgerTerm { Label = "OK", Value = line.Ok, Sign = -1 });
            ex.Terms.Add(new LedgerTerm { Label = "NG", Value = line.Ng, Sign = -1 });
            return new WtsCapInfo { Max = line.Waiting ?? 0, Locked = (line.In ?? 0) <= 0, Code = main, Explain = ex };
        }

        /// <summary>[GN-WTS-CAP][K5][K8] Các ô đang âm của 1 PO (key → giá trị, mô tả).</summary>
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
                    continue;
                }
                Put(G.Code + "|ChuaLam",  G.ChuaLam,  $"{G.Label}: Chưa làm");
                Put(G.Code + "|ConGiao",  G.ConGiao,  $"{G.Label}: Đã làm chưa giao");
                Put(G.Code + "|LoiGiu",   G.LoiGiu,   $"{G.Label}: tồn hàng lỗi");
                Put(G.Code + "|SuaGiu",   G.SuaGiu,   $"{G.Label}: tồn hàng sửa");   // [GN-CELL]
                Put(G.Code + "|LamHoGiu", G.LamHoGiu, $"{G.Label}: Tồn hàng giao theo NC");
                // Ô theo NC (GC) là ô bảo toàn thật (N1). Ô theo mã CV (HTSP/KCS/PKG) chỉ là trần nhập — ô Chưa làm của nhóm canh bảo toàn.
                foreach (var s in G.Steps.Where(s => s.Pg == "GC" && s.Waiting.HasValue && !s.IsExcluded && !s.IsPrep))
                    Put($"{G.Code}|{s.Pg}|{s.Code}", s.Waiting!.Value, $"{G.Label} {(s.Pg == "GC" ? "NC" : s.SubGroup)} {s.Code}: chưa làm");
            }
            return d;
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
        public static (decimal Max, LedgerExplain Explain) IssueCap(LedgerRow row, string from, string purpose, string? fromNC, string? fromCell = null)
        {
            var G = row.G(from);
            LedgerExplain Pick(string key) => G.Explains.TryGetValue(key, out var e) ? e : new LedgerExplain();
            switch (purpose)
            {
                case HandoverPurpose.New:
                    return (Math.Max(0, G.ConGiao), Pick("ConGiao"));
                case HandoverPurpose.NgReturn:
                    if (from == "KHO") return (0, new LedgerExplain { Title = "Kho không có công đoạn trước", Note = "Phôi lỗi tồn ở Kho: giao phân loại Hàng lỗi KHÔNG tích (gửi nhờ sửa), hoặc Huỷ / Trả khách." }); // [GN-PLH] mục 36: trước "giao từ ô"
                    // [GN-NC-PIN] Gia công: trần theo NC phát hiện lỗi = hàng đang chờ ở NC đó (không vượt Chưa làm của nhóm)
                    if (from == "GC" && !string.IsNullOrEmpty(fromNC))
                    {
                        var ln = G.Steps.FirstOrDefault(s => s.Code == fromNC && !s.IsExcluded);
                        if (ln == null)
                            return (0, new LedgerExplain { Title = "Trả hàng lỗi theo NC", Note = $"NC {fromNC} không có trong snapshot (hoặc là NC máy loại trừ)." });
                        var w = ln.Waiting ?? 0;
                        var ex = new LedgerExplain { Title = $"Gia công · NC {fromNC} chưa làm", Rule = "N10", Formula = $"Nhận vào NC {fromNC} − OK − NG",
                            Note = "Chiếc lỗi phát hiện ở NC này, lấy từ hàng đang chờ của NC (chưa làm).", Result = Math.Min(w, G.ChuaLam) };
                        ex.Terms.Add(new LedgerTerm { Label = $"Nhận vào NC {fromNC}", Value = ln.In ?? 0, Sign = 1 });
                        ex.Terms.Add(new LedgerTerm { Label = "OK + NG đã làm", Value = ln.Ok + ln.Ng, Sign = -1 });
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
                        : (Math.Max(0, G.SuaGiu), Pick("SuaGiu"));
                case HandoverPurpose.HelpReturn:
                    return (Math.Max(0, G.LamHoGiu), Pick("LamHoGiu"));
                case HandoverPurpose.HelpSend:
                {
                    if (from != "GC" || string.IsNullOrEmpty(fromNC))
                        return (0, new LedgerExplain { Title = "Giao theo NC", Note = "Chỉ Gia công giao theo NC, và phải chọn NC nguồn." });
                    var line = G.Steps.FirstOrDefault(s => s.Code == fromNC && !s.IsExcluded);
                    if (line == null)
                        return (0, new LedgerExplain { Title = "Giao theo NC", Note = $"NC {fromNC} không có trong snapshot (hoặc là NC máy loại trừ)." });
                    var next = G.Steps.SkipWhile(s => s.Code != fromNC).Skip(1).FirstOrDefault();
                    // Hàng OK ở NC nguồn đi tiếp theo 2 đường: NC sau (nếu NC sau không phải máy loại trừ) hoặc gửi làm hộ.
                    var usedNext = next != null && !next.IsExcluded ? next.Ok + next.Ng : 0;
                    var e = new LedgerExplain
                    {
                        Title = $"Gia công · NC {fromNC} còn giao được", Rule = "N3, N4",
                        Formula = $"OK NC {fromNC} − đã giao từ NC {fromNC}" + (usedNext > 0 ? $" − NC sau ({next!.Code}) đã làm" : ""),
                    };
                    e.Terms.Add(new LedgerTerm { Label = $"OK NC {fromNC}", Value = line.Ok, Sign = 1 });
                    e.Terms.Add(new LedgerTerm { Label = "Đã giao theo NC", Value = line.HelpOut, Sign = -1 });
                    if (usedNext > 0) e.Terms.Add(new LedgerTerm { Label = $"NC {next!.Code} đã làm (OK + NG)", Value = usedNext, Sign = -1 });
                    e.Result = line.Ok - line.HelpOut - usedNext;
                    return (Math.Max(0, e.Result), e);
                }
            }
            return (0, new LedgerExplain());
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
