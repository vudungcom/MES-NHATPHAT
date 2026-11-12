// [GÓI 14 2026-11-11][KHACH-GUI-LAI] Endpoint drive_g14.py: seed (HarnessG14Seed) + đọc sổ / bảng phiếu bù. Chỉ chép vào harness bản CÓ bảng HandoverReplacements
//   (mk.sh); bản cũ dùng HarnessG14Old.cs. Nguồn: KHÁCH 10/11 vòng 1 câu 1a A + 1b A; HÙNG 11/11 14:39.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class HarnessG14
{
    public static Task<object> SeedAsync(AppDbContext db) => HarnessG14Seed.SeedAsync(db);

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G14");
        if (det == null) return new { };
        var id = det.KhPlanDetailId;
        var row = (await led.GetLedgerForDetailAsync(id))!;
        var w = HandoverLedgerService.WhereIs(row);
        return new
        {
            bu = db.HandoverReplacements.List.Where(b => b.KhPlanDetailId == id)
                .Select(b => new { b.ReplacementId, b.NgCaseId, b.Qty, b.Notes, b.ReceivedBy, b.IsVoided, b.VoidReason }).ToList(),
            txs = db.HandoverTransactions.List.Where(t => t.KhPlanDetailId == id)
                .Select(t => new { t.HandoverTxId, t.Purpose, t.FromGroupCode, t.ToGroupCode, t.QtyIssued, t.NgCaseId, t.IsVoided }).ToList(),
            khoConGiao = row.G("KHO").ConGiao, khoNhanXl = row.G("KHO").NhanLoiXl, rowBu = row.Bu, phoi = row.Phoi,
            daBu = row.Cases.Values.Where(c => c.Id > 0).ToDictionary(c => c.Id.ToString(), c => c.DaBu),
            balanced = row.Balanced && w.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count,
        };
    }
}
