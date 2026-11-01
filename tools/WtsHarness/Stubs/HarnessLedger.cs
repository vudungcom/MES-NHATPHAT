// [WTS-PANEL-QTY 31/10] /harness/ledger — sổ chung của MỌI PO, nạp theo 3 đường các trang đang dùng:
//   A = GetLedgerForDetailAsync (tab KH xem chi tiết, bảng quy trình trang WTS, trang Giao nhận khi nạp lại 1 PO)
//   B = GetLedgerAsync()        (danh sách trang Giao nhận)
//   C = GetLedgerForDetailsAsync (danh sách Kế hoạch — khung "Đang ở đâu")
// drive_cungso.py so A = B = C và so số hiện trên trang (WTS / tab KH / Đang ở đâu) với A. Chỉ đọc.
using MES.Web.Data;
using MES.Web.Services;

public static class HarnessLedger
{
    static object Dto(LedgerRow r)
    {
        var w = HandoverLedgerService.WhereIs(r);
        return new
        {
            r.Phoi, r.TpNhap, r.Balanced, r.PlanQty,
            where = new { w.Phoi, w.Total, w.Balanced, w.HasNegative, w.TpNhap, items = w.Items.Select(i => new { i.Place, i.Kind, i.Nc, i.Qty }) },
            groups = r.Groups.ToDictionary(g => g.Key, g => (object)new
            {
                g.Value.NhanTong, g.Value.GiaoTong, g.Value.ChuaLam, g.Value.DaLam, g.Value.NgCuoi, g.Value.ConGiao,
                g.Value.LoiGiu, g.Value.SuaGiu, g.Value.ChoNhan, g.Value.Ton, g.Value.NgDau, neg = g.Value.HasNegative,
                steps = g.Value.Steps.Select(s => new { s.Pg, s.Code, s.Ok, s.Ng, s.In, s.Waiting, s.IsPrep, s.IsExcluded })
            })
        };
    }

    public static async Task<object> SnapshotAsync(HandoverLedgerService L, AppDbContext db)
    {
        var ids = db.KhPlanDetails.List.Select(d => d.KhPlanDetailId).ToList();
        var listB = await L.GetLedgerAsync();
        var mapC = await L.GetLedgerForDetailsAsync(ids);
        var res = new List<object>();
        foreach (var d in db.KhPlanDetails.List)
        {
            var a = await L.GetLedgerForDetailAsync(d.KhPlanDetailId);
            var b = listB.FirstOrDefault(x => x.KhPlanDetailId == d.KhPlanDetailId);
            mapC.TryGetValue(d.KhPlanDetailId, out var c);
            res.Add(new
            {
                id = d.KhPlanDetailId, po = d.PurchaseOrder, planId = d.KhPlanId,
                A = a == null ? null : Dto(a), B = b == null ? null : Dto(b), C = c == null ? null : Dto(c)
            });
        }
        return res;
    }
}
