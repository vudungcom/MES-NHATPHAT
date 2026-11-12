// [GÓI 14 2026-11-11][KHACH-GUI-LAI] Bản cũ (chưa có bảng HandoverReplacements): drive_g14.py chạy để thấy SAI đúng chỗ (không có nút "Khách gửi bù").
//   Nguồn: CLAUDE (kiểm Gói 14 — so bản cũ / mới).
using MES.Web.Data;
using MES.Web.Services;

public static class HarnessG14
{
    public static Task<object> SeedAsync(AppDbContext db) => HarnessG14Seed.SeedAsync(db);

    public static async Task<object> DumpAsync(AppDbContext db, HandoverLedgerService led)
    {
        var det = db.KhPlanDetails.List.FirstOrDefault(x => x.PurchaseOrder == "PO-G14");
        if (det == null) return new { };
        var row = (await led.GetLedgerForDetailAsync(det.KhPlanDetailId))!;
        return new { bu = new List<object>(), txs = new List<object>(), khoConGiao = row.G("KHO").ConGiao, khoNhanXl = row.G("KHO").NhanLoiXl, rowBu = 0m, phoi = row.Phoi,
                     daBu = new Dictionary<string, decimal>(), balanced = row.Balanced, neg = HandoverLedgerService.NegativeCells(row).Count };
    }
}
