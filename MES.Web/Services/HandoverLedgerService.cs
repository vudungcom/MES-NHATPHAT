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

        public static readonly string[] All = { New, NgReturn, RepairSend, RepairReturn, HelpSend, HelpReturn };

        public static string Label(string? p) => p switch
        {
            New          => "Hàng mới",
            NgReturn     => "Trả hàng lỗi",
            RepairSend   => "Gửi sửa",
            RepairReturn => "Trả hàng đã sửa",
            HelpSend     => "Gửi làm hộ",
            HelpReturn   => "Trả làm hộ",
            _            => p ?? ""
        };
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
        public decimal GiaoTong => GiaoMoi + GiaoLoi + GiaoSua + GiaoLoiXl + GiaoLamHo;

        /// <summary>Hàng chưa làm (trần nhập WTS — L3). Kho: không dùng.</summary>
        public decimal ChuaLam  { get; set; }
        /// <summary>Đã làm OK (WTS) — Kho: không dùng.</summary>
        public decimal DaLam    { get; set; }
        /// <summary>NG do chính nhóm làm hỏng (WTS QtyNg) = NG cuối.</summary>
        public decimal NgCuoi   { get; set; }
        /// <summary>Còn giao = Đã làm − Giao mới (trần phiếu Hàng mới — L4). Kho: phôi tốt còn giao.</summary>
        public decimal ConGiao  { get; set; }
        /// <summary>Hàng lỗi / hàng sửa đang giữ (gồm NG cuối chưa xử lý).</summary>
        public decimal LoiGiu   { get; set; }
        /// <summary>Hàng nhóm khác gửi nhờ làm hộ, đang giữ.</summary>
        public decimal LamHoGiu { get; set; }
        /// <summary>Tồn = ΣNhận − ΣGiao.</summary>
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
        /// <summary>Hàng trên phiếu đã giao nhưng chưa ai nhận.</summary>
        public decimal DangGiao { get; set; }

        public decimal TonTong => Groups.Values.Sum(g => g.Ton);
        /// <summary>L6: Phôi = Σ Tồn các nhóm + đang giao + thành phẩm đã nhập kho.</summary>
        public bool    Balanced => Phoi == TonTong + DangGiao + TpNhap;
        public LedgerExplain BalanceExplain { get; set; } = new();

        public List<string> Warnings => Groups.Values.SelectMany(g => g.Warnings).ToList();
        public bool HasWarning => Warnings.Count > 0 || !Balanced;
        public bool HasLoiGiu  => Groups.Values.Any(g => g.LoiGiu > 0);
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
    public record InRecv(long Id, DateTime At, decimal Ok, decimal Ng, string? NgReason);
    public class InTx
    {
        public long    Id        { get; set; }
        public string  From      { get; set; } = "";
        public string  To        { get; set; } = "";
        public string? Purpose   { get; set; }
        public bool    IsReturnNg { get; set; }
        public decimal Issued    { get; set; }
        public string  Status    { get; set; } = "PENDING";
        public string? FromNC    { get; set; }
        public string? ToNC      { get; set; }
        public DateTime At       { get; set; }
        public string? Notes     { get; set; }
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
            "KHO" => "Kho", "GC" => "Gia công", "HTSP" => "HTSP", "KCS" => "KCS", "PKG" => "Đóng gói", _ => code
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

        public async Task<LedgerRow?> GetLedgerForDetailAsync(int khPlanDetailId)
        {
            var details = await _db.KhPlanDetails.Include(d => d.KhPlan).ThenInclude(p => p!.Customer)
                .Where(d => d.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync();
            if (!details.Any()) return null;
            var inputs = await LoadInputsAsync(details);
            return Compute(inputs[0]);
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
                        IsReturnNg = t.IsReturnNg, Issued = t.QtyIssued, Status = t.Status,
                        FromNC = t.FromNC, ToNC = t.ToNC, At = t.IssuedAt, Notes = t.Notes,
                        Receives = t.Receives.Where(r => !r.IsVoided)
                                    .Select(r => new InRecv(r.ReceiveId, r.ReceivedAt, r.QtyOk, r.QtyNg, r.NgReason)).ToList()
                    }).ToList(),
                    Wts = wts.Where(w => w.KhPlanDetailId == id).Select(w => new InWts(
                        w.WtsLogId, w.EndTime, w.ProcessGroup, w.NC, w.WtsCode,
                        w.QtyOk ?? (w.QtyDone - (w.QtyNg ?? 0)), w.QtyNg ?? 0, w.Worker)).ToList(),
                    GcSteps = mach.Where(m => m.KhPlanDetailId == id).Select(m => new InGcStep(
                        m.StepOrder, m.NC ?? "", m.IsBackup, m.ParentNC, m.MachineRegistered,
                        !string.IsNullOrWhiteSpace(m.MachineRegistered) && exclSet.Contains(m.MachineRegistered!.Trim()))).ToList(),
                };
                // Mã CV trên WTS: Taro = WtsTaskCode ?? NC (khớp HieuSuat_Index); Bavia / Rửa / KCS / Đóng gói = NC
                inp.CodeSteps.AddRange(taro.Where(s => s.KhPlanDetailId == id).Select(s => new InCodeStep("TARO", s.StepOrder, s.WtsTaskCode ?? s.NC, s.IsBackup, s.ParentNC, s.StepName)));
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

        /// <summary>Số tính là "đã giao" của 1 phiếu: đã nhận đủ → số nhận; chưa đủ → số trên phiếu (L4: tính cả phiếu chưa nhận).</summary>
        static decimal GiaoQty(InTx t)
        {
            var rec = Received(t);
            return t.Status == "COMPLETED" ? rec : Math.Max(t.Issued, rec);
        }

        static string TxText(InTx t, string purpose)
            => $"Phiếu #{t.Id} · {GroupLabel(t.From)} → {GroupLabel(t.To)} · {HandoverPurpose.Label(purpose)}"
               + (string.IsNullOrEmpty(t.FromNC) ? "" : $" · từ NC {t.FromNC}")
               + (string.IsNullOrEmpty(t.ToNC) ? "" : $" · vào NC {t.ToNC}")
               + (t.Status == "COMPLETED" ? "" : $" · {(Received(t) > 0 ? "nhận dở" : "chờ nhận")} (phiếu {t.Issued:0.##})");

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

            // Bộ gom số hạng có kèm chứng từ
            var acc = new Dictionary<(string g, string key), LedgerTerm>();
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
            foreach (var t in inp.Txs.OrderBy(x => x.At))
            {
                if (!row.Groups.ContainsKey(t.From) || !row.Groups.ContainsKey(t.To)) continue;
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

                if (t.Status != "COMPLETED")
                {
                    row.DangGiao += Math.Max(0, giao - rec);
                    row.G(t.From).PendingToIssue++;
                    row.G(t.To).PendingToReceive++;
                }

                // Thành phẩm: Đóng gói → Kho (hàng mới) — không vào phôi Kho
                bool isTp = t.From == "PKG" && t.To == "KHO" && purpose == HandoverPurpose.New;

                // ---- Bên giao ----
                switch (purpose)
                {
                    case HandoverPurpose.New:
                        Add(t.From, "gm", "Giao Hàng mới", giao, txDoc);
                        break;
                    case HandoverPurpose.NgReturn:
                        Add(t.From, "glTra", "Giao Hàng lỗi (trả nơi gây lỗi)", giao, txDoc);
                        break;
                    case HandoverPurpose.RepairSend:
                        Add(t.From, "glGui", "Giao Hàng lỗi (gửi sửa)", giao, txDoc);
                        break;
                    case HandoverPurpose.RepairReturn:
                        Add(t.From, "gs", "Giao Hàng Sửa (trả hàng đã sửa)", giao, txDoc);
                        break;
                    case HandoverPurpose.HelpSend:
                        Add(t.From, "ghSend", "Giao Làm hộ (gửi đi)", giao, txDoc);
                        if (t.From == "GC" && !string.IsNullOrEmpty(t.FromNC))
                            Add("GC", "helpOut:" + t.FromNC, "Gửi làm hộ từ NC " + t.FromNC, giao, txDoc);
                        break;
                    case HandoverPurpose.HelpReturn:
                        Add(t.From, "ghRet", "Giao Làm hộ (trả về)", giao, txDoc);
                        break;
                }

                // ---- Bên nhận (số bên nhận đếm) ----
                if (rec > 0)
                {
                    if (isTp)
                    {
                        row.TpNhap += recOk;
                    }
                    else switch (purpose)
                    {
                        case HandoverPurpose.New:
                            Add(t.To, "nm", "Nhận Hàng mới", rec, RecDoc(""));
                            if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC)) Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, rec, RecDoc(""));
                            break;
                        case HandoverPurpose.NgReturn:
                            Add(t.To, "nl", "Nhận Hàng lỗi", rec, RecDoc(""));
                            break;
                        case HandoverPurpose.RepairSend:
                            Add(t.To, "ns", "Nhận Hàng Sửa", rec, RecDoc(""));
                            break;
                        case HandoverPurpose.RepairReturn:
                            Add(t.To, "nx", "Nhận Hàng lỗi đã xử lý", rec, RecDoc(""));
                            if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC)) Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, rec, RecDoc(""));
                            break;
                        case HandoverPurpose.HelpSend:
                            Add(t.To, "nhSend", "Nhận Làm hộ (nhóm khác gửi)", rec, RecDoc(""));
                            break;
                        case HandoverPurpose.HelpReturn:
                            Add(t.To, "nhRet", "Nhận Làm hộ (trả về)", rec, RecDoc(""));
                            if (t.To == "GC" && !string.IsNullOrEmpty(t.ToNC)) Add("GC", "in:" + t.ToNC, "Nhận vào NC " + t.ToNC, rec, RecDoc(""));
                            break;
                    }
                }

                // ---- NG đếm thấy lúc nhận: trả ngay bên giao (L2, giữ cách làm cũ) ----
                // Bên nhận: Giao Hàng lỗi; bên giao: Nhận Hàng lỗi (về ô lỗi đang giữ).
                // Phiếu Trả hàng lỗi thì toàn bộ đã là hàng lỗi, không trả lại lần nữa.
                if (recNg > 0 && purpose != HandoverPurpose.NgReturn)
                {
                    var ngDoc = RecDoc($" · bên nhận đếm NG {recNg:0.##}, trả ngay bên giao");
                    var bucket = purpose switch
                    {
                        HandoverPurpose.RepairSend => "glGui",
                        HandoverPurpose.HelpSend   => "glLamHo",
                        _                          => "glTra"
                    };
                    var lbl = bucket switch
                    {
                        "glGui"   => "Giao Hàng lỗi (gửi sửa)",
                        "glLamHo" => "Giao Hàng lỗi (trả từ hàng làm hộ)",
                        _         => "Giao Hàng lỗi (trả nơi gây lỗi)"
                    };
                    if (isTp)
                    {
                        // Thành phẩm Kho đếm NG: trả lại Đóng gói
                        Add("PKG", "nl", "Nhận Hàng lỗi", recNg, ngDoc);
                    }
                    else
                    {
                        Add(t.To, bucket, lbl, recNg, ngDoc);
                        Add(t.From, "nl", "Nhận Hàng lỗi", recNg, ngDoc);
                    }
                }
            }

            // ── WTS ─────────────────────────────────────────────────────
            foreach (var w in inp.Wts)
            {
                var g = Pg2Group(w.ProcessGroup);
                if (g == "") continue;
                if (w.Ng != 0)
                    Add(g, "ngc", "NG làm hỏng (WTS)", w.Ng, new LedgerDoc { Kind = "WTS", RefId = w.Id, At = w.At, Text = WtsText(w) });
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
                G.GiaoMoi   = V("gm");
                G.GiaoLoi   = V("glTra") + V("glGui") + V("glLamHo");
                G.GiaoSua   = V("gs");
                G.GiaoLoiXl = 0; // [GN-LEDGER] Excel có cột này nhưng chưa tình huống nào dùng — chờ xưởng xác nhận
                G.GiaoLamHo = V("ghSend") + V("ghRet");
                G.Ton       = G.NhanTong - G.GiaoTong;
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
                ListExplain("NhanSua",   "Nhận · Hàng Sửa",          ("ns", "Hàng nhóm khác gửi nhờ sửa"));
                ListExplain("NhanLoiXl", "Nhận · Hàng lỗi đã xử lý", ("nx", "Hàng đã sửa trả về"));
                ListExplain("NhanLamHo", "Nhận · Làm hộ",            ("nhSend", "Nhóm khác gửi nhờ làm hộ"), ("nhRet", "Hàng làm hộ trả về"));
                ListExplain("GiaoMoi",   "Giao · Hàng mới",          ("gm", "Phiếu Hàng mới (gồm phiếu chờ nhận)"));
                ListExplain("GiaoLoi",   "Giao · Hàng lỗi",          ("glTra", "Trả hàng lỗi về nơi gây lỗi"), ("glGui", "Gửi hàng lỗi đi sửa"), ("glLamHo", "Trả lỗi từ hàng làm hộ"));
                ListExplain("GiaoSua",   "Giao · Hàng Sửa",          ("gs", "Trả hàng đã sửa"));
                ListExplain("GiaoLamHo", "Giao · Làm hộ",            ("ghSend", "Gửi đi làm hộ"), ("ghRet", "Trả hàng làm hộ"));
                G.Explains["GiaoLoiXl"] = new LedgerExplain
                {
                    Title = $"{G.Label} · Giao · Hàng lỗi đã xử lý", Formula = "—",
                    Note = "Cột có trong Excel của xưởng nhưng chưa tình huống nào dùng. Hàng đã sửa trả đi được ghi ở cột Giao · Hàng Sửa (giống TH1, TH3). Chờ xưởng xác nhận có cần cột này không."
                };

                // Tổng Nhận / Tổng Giao (cột gọn trên bảng chính)
                {
                    var en = new LedgerExplain { Title = $"{G.Label} · Tổng Nhận", Formula = "Hàng mới + Hàng lỗi + Hàng Sửa + Hàng lỗi đã xử lý + Làm hộ",
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
                    var eg = new LedgerExplain { Title = $"{G.Label} · Tổng Giao", Formula = "Hàng mới + Hàng lỗi + Hàng Sửa + Hàng lỗi đã xử lý + Làm hộ",
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
                        Title = $"{G.Label} · Tồn", Formula = "Tổng Nhận − Tổng Giao", Rule = "L6",
                        Note = "Tồn là số hàng đang thật sự nằm ở nhóm. Phiếu đã giao nhưng chưa ai nhận thì không còn ở bên giao, cũng chưa tới bên nhận (xem dòng Đang giao).",
                        Result = G.Ton
                    };
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Nhận", Value = G.NhanTong, Sign = 1, Docs = AllDocs(acc, code, "nm", "nl", "ns", "nx", "nhSend", "nhRet") });
                    e.Terms.Add(new LedgerTerm { Label = "Tổng Giao", Value = G.GiaoTong, Sign = -1, Docs = AllDocs(acc, code, "gm", "glTra", "glGui", "glLamHo", "gs", "ghSend", "ghRet") });
                    G.Explains["Ton"] = e;
                }

                if (code == "KHO")
                {
                    // Kho: phôi tốt còn giao + hàng lỗi đang giữ
                    G.ConGiao = G.NhanMoi + G.NhanLoiXl - G.GiaoMoi - V("glTra");
                    G.LoiGiu  = G.NhanLoi + G.NhanSua - V("glGui") - G.GiaoSua;
                    G.LamHoGiu = 0;
                    var e = new LedgerExplain { Title = "Kho · Phôi còn giao", Formula = "Phôi nhận + Phôi đã sửa trả về − Giao Hàng mới − Trả lỗi", Rule = "L4", Result = G.ConGiao };
                    e.Terms.Add(Tm("nm", "Phôi Kho nhận", 1));
                    e.Terms.Add(Tm("nx", "Phôi đã sửa trả về", 1));
                    e.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    e.Terms.Add(Tm("glTra", "Trả lỗi", -1));
                    e.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Phôi Kho nhận");
                    G.Explains["ConGiao"] = e;
                    var l = new LedgerExplain { Title = "Kho · Hàng lỗi đang giữ", Formula = "Nhận Hàng lỗi + Nhận Hàng Sửa − Gửi sửa − Trả hàng đã sửa", Result = G.LoiGiu,
                        Note = "Phôi lỗi bị trả về Kho, chờ Kho gửi sửa hoặc trả khách." };
                    l.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1)); l.Terms.Add(Tm("ns", "Nhận Hàng Sửa", 1));
                    l.Terms.Add(Tm("glGui", "Gửi sửa", -1)); l.Terms.Add(Tm("gs", "Trả hàng đã sửa", -1));
                    l.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng lỗi");
                    G.Explains["LoiGiu"] = l;
                    if (G.ConGiao < 0) G.Warnings.Add($"Kho: giao nhiều hơn phôi có ({G.ConGiao:0.##})");
                    if (G.LoiGiu < 0)  G.Warnings.Add($"Kho: chuyển hàng lỗi nhiều hơn số đang giữ ({G.LoiGiu:0.##}) — K1");
                }
                else
                {
                    var daLamTerm = acc.TryGetValue((code, "ok"), out var okT) ? okT : new LedgerTerm { Label = "Đã làm (WTS OK)" };
                    G.DaLam = daLamTerm.Value;

                    // Chưa làm = Nhận mới + Nhận lỗi đã XL + Làm hộ trả về − Đã làm − NG làm hỏng − Trả lỗi − Gửi làm hộ
                    G.ChuaLam = G.NhanMoi + G.NhanLoiXl + V("nhRet") - G.DaLam - G.NgCuoi - V("glTra") - V("ghSend");
                    G.ConGiao = G.DaLam - G.GiaoMoi;
                    G.LoiGiu  = G.NhanLoi + G.NhanSua + G.NgCuoi - V("glGui") - G.GiaoSua;
                    G.LamHoGiu = V("nhSend") - V("ghRet") - V("glLamHo");

                    var e = new LedgerExplain
                    {
                        Title = $"{G.Label} · Chưa làm", Rule = "L3",
                        Formula = "Nhận Hàng mới + Nhận Hàng lỗi đã xử lý + Làm hộ trả về − Đã làm − NG làm hỏng − Trả hàng lỗi − Gửi làm hộ",
                        Note = "Đây là trần khi nhập WTS: không ghi làm xong nhiều hơn số này. Khác Excel: trừ cả NG làm hỏng, và chỉ trừ phần Giao hàng lỗi lấy từ hàng chưa làm (không trừ hàng lỗi đang giữ chỉ chuyển tiếp đi sửa).",
                        Result = G.ChuaLam
                    };
                    e.Terms.Add(Tm("nm", "Nhận Hàng mới", 1));
                    e.Terms.Add(Tm("nx", "Nhận Hàng lỗi đã xử lý", 1));
                    e.Terms.Add(Tm("nhRet", "Làm hộ trả về", 1));
                    e.Terms.Add(new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = -1, Docs = daLamTerm.Docs });
                    e.Terms.Add(Tm("ngc", "NG làm hỏng (WTS)", -1));
                    e.Terms.Add(Tm("glTra", "Trả hàng lỗi", -1));
                    e.Terms.Add(Tm("ghSend", "Gửi làm hộ", -1));
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
                    var ng = new LedgerExplain { Title = $"{G.Label} · NG làm hỏng", Formula = "Tổng NG trên WTS của nhóm", Rule = "L1",
                        Note = "NG cuối: lỗi do chính nhóm làm hỏng. Hàng này nằm trong ô Hàng lỗi đang giữ cho tới khi được gửi sửa / huỷ.", Result = G.NgCuoi };
                    ng.Terms.Add(Tm("ngc", "NG trên WTS", 1));
                    G.Explains["NgCuoi"] = ng;

                    var cg = new LedgerExplain { Title = $"{G.Label} · Còn giao", Formula = "Đã làm − Giao Hàng mới", Rule = "L4",
                        Note = "Trần khi lập phiếu Hàng mới. Tính cả phiếu đã giao nhưng bên nhận chưa nhận.", Result = G.ConGiao };
                    cg.Terms.Add(new LedgerTerm { Label = daLamTerm.Label, Value = G.DaLam, Sign = 1, Docs = daLamTerm.Docs });
                    cg.Terms.Add(Tm("gm", "Giao Hàng mới", -1));
                    G.Explains["ConGiao"] = cg;

                    var lg = new LedgerExplain { Title = $"{G.Label} · Hàng lỗi / sửa đang giữ",
                        Formula = "Nhận Hàng lỗi + Nhận Hàng Sửa + NG làm hỏng − Gửi sửa − Trả hàng đã sửa", Rule = "L5, K1",
                        Note = "Trần khi lập phiếu Gửi sửa hoặc Trả hàng đã sửa.", Result = G.LoiGiu };
                    lg.Terms.Add(Tm("nl", "Nhận Hàng lỗi", 1)); lg.Terms.Add(Tm("ns", "Nhận Hàng Sửa", 1)); lg.Terms.Add(Tm("ngc", "NG làm hỏng", 1));
                    lg.Terms.Add(Tm("glGui", "Gửi sửa", -1)); lg.Terms.Add(Tm("gs", "Trả hàng đã sửa", -1));
                    lg.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận Hàng lỗi");
                    G.Explains["LoiGiu"] = lg;

                    var lh = new LedgerExplain { Title = $"{G.Label} · Làm hộ đang giữ", Formula = "Nhóm khác gửi nhờ làm hộ − Đã trả làm hộ − Lỗi trả lại", Rule = "N3, K4",
                        Note = "Hàng của nhóm khác, không tính vào số sản xuất của nhóm này.", Result = G.LamHoGiu };
                    lh.Terms.Add(Tm("nhSend", "Nhận làm hộ", 1)); lh.Terms.Add(Tm("ghRet", "Trả làm hộ", -1)); lh.Terms.Add(Tm("glLamHo", "Lỗi trả lại", -1));
                    lh.Terms.RemoveAll(x => x.Value == 0 && x.Label != "Nhận làm hộ");
                    G.Explains["LamHoGiu"] = lh;

                    if (G.ChuaLam < 0)  G.Warnings.Add($"{G.Label}: Chưa làm âm ({G.ChuaLam:0.##}) — WTS / trả lỗi nhiều hơn hàng nhận vào (L3)");
                    if (G.ConGiao < 0)  G.Warnings.Add($"{G.Label}: giao Hàng mới nhiều hơn số đã làm ({G.ConGiao:0.##}) — L4");
                    if (G.LoiGiu < 0)   G.Warnings.Add($"{G.Label}: chuyển hàng lỗi / sửa nhiều hơn số đang giữ ({G.LoiGiu:0.##}) — K1");
                    if (G.LamHoGiu < 0) G.Warnings.Add($"{G.Label}: trả làm hộ nhiều hơn số đang giữ ({G.LamHoGiu:0.##}) — K4");
                }

                // Kiểm tách: Tồn = các phần đang giữ (phát hiện lỗi chương trình nếu lệch)
                var parts = code == "KHO" ? G.ConGiao + G.LoiGiu : G.ChuaLam + G.ConGiao + G.LoiGiu + G.LamHoGiu;
                if (parts != G.Ton) G.Warnings.Add($"{G.Label}: Tồn {G.Ton:0.##} khác tổng các phần {parts:0.##} — báo lại để kiểm");
            }

            // ── Chi tiết NC / mã CV ──────────────────────────────────────
            BuildGcSteps(inp, gcChain, row, acc);

            // ── Cân bằng PO (L6) ─────────────────────────────────────────
            var b = new LedgerExplain
            {
                Title = "Cân bằng PO", Rule = "L6",
                Formula = "Phôi nhận = Tồn Kho + Tồn GC + Tồn HTSP + Tồn KCS + Tồn Đóng gói + Đang giao + Thành phẩm nhập kho",
                Note = "Mọi số đều tính từ chứng từ nên dòng này luôn phải cân. Chỗ nhập sai sẽ lộ ra ở ô âm (tô đỏ) của từng nhóm.",
                Result = row.TonTong + row.DangGiao + row.TpNhap
            };
            foreach (var c in GroupCodes) b.Terms.Add(new LedgerTerm { Label = "Tồn " + GroupLabel(c), Value = row.G(c).Ton, Sign = 1 });
            b.Terms.Add(new LedgerTerm { Label = "Đang giao (phiếu chưa nhận)", Value = row.DangGiao, Sign = 1 });
            b.Terms.Add(new LedgerTerm { Label = "Thành phẩm nhập kho", Value = row.TpNhap, Sign = 1 });
            row.BalanceExplain = b;

            return row;
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
                                 Dictionary<(string g, string key), LedgerTerm> acc)
        {
            var G = row.G("GC");
            decimal V(string k) => acc.TryGetValue(("GC", k), out var t) ? t.Value : 0m;
            var logs = inp.Wts.Where(w => w.ProcessGroup == "GC").ToList();
            var all  = inp.GcSteps.Where(s => !s.IsBackup).OrderBy(s => s.StepOrder).ToList();
            if (!all.Any()) return;

            // Hàng vào NC đầu = mọi hàng vào sản xuất GC không chỉ định NC − trả hàng lỗi (phát hiện trước khi làm)
            var targeted = chain.Sum(s => V("in:" + s.NC));
            decimal prevOk = 0, prevHelpOut = 0;
            bool first = true;
            foreach (var s in all)
            {
                var codes = NcCodes(inp, s.NC);
                var ok = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ok);
                var ng = logs.Where(w => codes.Contains(w.NC ?? "")).Sum(w => w.Ng);
                var line = new LedgerStepLine
                {
                    SubGroup = "GC", Code = s.NC, StepOrder = s.StepOrder, Machine = s.Machine,
                    IsExcluded = s.IsExcluded, Ok = ok, Ng = ng, IsLast = chain.Count > 0 && s.NC == chain.Last().NC,
                    HelpOut = V("helpOut:" + s.NC)
                };
                if (s.IsExcluded)
                {
                    line.Note = "máy loại trừ — nhóm khác làm hộ (N3)";
                    G.Steps.Add(line);
                    continue;
                }
                decimal input;
                if (first)
                {
                    input = G.NhanMoi + G.NhanLoiXl + V("nhRet") - targeted - V("glTra");
                    line.Note = "vào = Nhận mới + Nhận lỗi đã XL + Làm hộ trả về (không chỉ định NC) − Trả hàng lỗi";
                    first = false;
                }
                else
                {
                    input = prevOk - prevHelpOut;
                    line.Note = prevHelpOut > 0 ? "vào = OK NC trước − gửi làm hộ" : "vào = OK NC trước";
                }
                var tin = V("in:" + s.NC);
                if (tin != 0) { input += tin; line.Note += " + hàng chỉ định vào NC này"; }
                line.In = input;
                line.Waiting = input - ok - ng;
                if (line.Waiting < 0)
                    G.Warnings.Add($"Gia công NC {s.NC}: OK + NG ({ok + ng:0.##}) nhiều hơn hàng vào NC ({input:0.##}) — WTS nhập vượt (L3, N1)");
                G.Steps.Add(line);
                prevOk = ok;
                prevHelpOut = V("helpOut:" + s.NC);
            }
        }

        // ================================================================
        // TRẦN LẬP PHIẾU — dùng chung cho modal Giao và IssueAsync
        // ================================================================

        /// <summary>[GN-LEDGER] Trần số lượng được lập cho 1 loại phiếu + diễn giải.</summary>
        public static (decimal Max, LedgerExplain Explain) IssueCap(LedgerRow row, string from, string purpose, string? fromNC)
        {
            var G = row.G(from);
            LedgerExplain Pick(string key) => G.Explains.TryGetValue(key, out var e) ? e : new LedgerExplain();
            switch (purpose)
            {
                case HandoverPurpose.New:
                    return (Math.Max(0, G.ConGiao), Pick("ConGiao"));
                case HandoverPurpose.NgReturn:
                    if (from == "KHO") return (0, new LedgerExplain { Title = "Kho không trả hàng lỗi theo loại này", Note = "Phôi lỗi đang giữ ở Kho: dùng Gửi sửa." });
                    return (Math.Max(0, G.ChuaLam), Pick("ChuaLam"));
                case HandoverPurpose.RepairSend:
                case HandoverPurpose.RepairReturn:
                    return (Math.Max(0, G.LoiGiu), Pick("LoiGiu"));
                case HandoverPurpose.HelpReturn:
                    return (Math.Max(0, G.LamHoGiu), Pick("LamHoGiu"));
                case HandoverPurpose.HelpSend:
                {
                    if (from != "GC" || string.IsNullOrEmpty(fromNC))
                        return (0, new LedgerExplain { Title = "Gửi làm hộ", Note = "Chỉ Gia công gửi làm hộ, và phải chọn NC nguồn." });
                    var line = G.Steps.FirstOrDefault(s => s.Code == fromNC && !s.IsExcluded);
                    if (line == null)
                        return (0, new LedgerExplain { Title = "Gửi làm hộ", Note = $"NC {fromNC} không có trong snapshot (hoặc là NC máy loại trừ)." });
                    var next = G.Steps.SkipWhile(s => s.Code != fromNC).Skip(1).FirstOrDefault();
                    // Hàng OK ở NC nguồn đi tiếp theo 2 đường: NC sau (nếu NC sau không phải máy loại trừ) hoặc gửi làm hộ.
                    var usedNext = next != null && !next.IsExcluded ? next.Ok + next.Ng : 0;
                    var e = new LedgerExplain
                    {
                        Title = $"Gia công · NC {fromNC} còn gửi làm hộ được", Rule = "N3, N4",
                        Formula = $"OK NC {fromNC} − đã gửi làm hộ từ NC {fromNC}" + (usedNext > 0 ? $" − NC sau ({next!.Code}) đã làm" : ""),
                    };
                    e.Terms.Add(new LedgerTerm { Label = $"OK NC {fromNC}", Value = line.Ok, Sign = 1 });
                    e.Terms.Add(new LedgerTerm { Label = "Đã gửi làm hộ", Value = line.HelpOut, Sign = -1 });
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
                    if (to == "KHO" && from != "PKG") return "Hàng mới chỉ về Kho khi Đóng gói nhập thành phẩm.";
                    if (GroupRank(to) < GroupRank(from) && to != "KHO") return "Hàng mới chỉ giao xuôi. Trả về nhóm trước: dùng Trả hàng lỗi / Trả hàng đã sửa / Trả làm hộ.";
                    return null;
                case HandoverPurpose.HelpSend:
                    if (from != "GC") return "Chỉ Gia công gửi làm hộ.";
                    if (to == "KHO") return "Không gửi làm hộ cho Kho.";
                    return null;
                case HandoverPurpose.HelpReturn:
                    if (to != "GC") return "Hàng làm hộ chỉ trả về Gia công.";
                    return null;
                default:
                    return null;
            }
        }
    }
}
