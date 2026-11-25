// [N21-SCEN 2026-11-25] Đọc trạng thái cho drive_n21.py (luật N21: WTS có NG không tự sinh mã vụ — "NG ở WTS chưa báo lỗi", báo lỗi riêng ở Giao nhận).
//   Dùng dữ liệu seed khởi động (PO1: GC NC1 → NC2). Thuộc tính sổ đọc bằng tên (bản cũ không có NgChuaBao vẫn dựng được, trả null) → bản cũ SAI đúng chỗ.
using MES.Web.Data;
using MES.Web.Services;

public static class HarnessN21
{
    public static async Task<object> StateAsync(AppDbContext db, HandoverLedgerService led, string po)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == po);
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        object? P(object o, string n) => o.GetType().GetProperty(n)?.GetValue(o);
        return new
        {
            wts = db.WtsProductionLogs.List.Where(w => w.KhPlanDetailId == id && !w.IsVoided)
                .Select(w => new { w.WtsLogId, w.ProcessGroup, w.NC, w.QtyOk, w.QtyNg, w.NgReason, w.NgCaseId }).ToList(),
            reports = db.HandoverNgReports.List.Where(b => b.KhPlanDetailId == id && !b.IsVoided)
                .Select(b => new { b.HandoverNgReportId, b.GroupCode, b.SourceCell, b.NC, b.Qty, b.Origin, b.CauseGroupCode, b.NgCaseId }).ToList(),
            cases = db.NgCases.List.Where(c => c.KhPlanDetailId == id)
                .Select(c => new { c.NgCaseId, c.OriginKind, c.DetectGroupCode, c.DetectNC, c.CauseGroupCode, c.NgCategoryId }).ToList(),
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id && !t.IsVoided)
                .Select(t => new { t.HandoverTxId, t.FromGroupCode, t.ToGroupCode, t.FromCell, t.ToCell, t.QtyIssued, t.NgCaseId }).ToList(),
            groups = HandoverLedgerService.GroupCodes.ToDictionary(g => g, g => { var G = row.G(g);
                return new { ngcb = P(G, "NgChuaBao"), loi = G.LoiGiu, ngc = P(G, "NgCuoi"), G.ChuaLam, G.ConGiao, G.Ton }; }),
            nc = row.G("GC").Steps.ToDictionary(s => s.Code, s => s.Waiting ?? 0),
            row.Balanced,
            neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
