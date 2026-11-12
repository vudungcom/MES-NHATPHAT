// [H10 2026-10-30] Bản sao số liệu 1 PO cho bộ kiểm (y như HandoverWhy.Clone — hàm đó private).
using MES.Web.Services;

public static class HandoverWhyTest
{
    public static LedgerInput CloneInput(LedgerInput i) => new()
    {
        KhPlanDetailId = i.KhPlanDetailId, PlanNo = i.PlanNo, PurchaseOrder = i.PurchaseOrder, PartNo = i.PartNo,
        CustomerName = i.CustomerName, PlanQty = i.PlanQty, ApplyWorkflow = i.ApplyWorkflow,
        Phoi = i.Phoi, GcSteps = i.GcSteps, CodeSteps = i.CodeSteps,
        Txs = i.Txs.Select(t => t.CloneTx()).ToList(),
        Wts = new List<InWts>(i.Wts),
        NgReps = new List<InNgRep>(i.NgReps),
        Repairs = i.Repairs.Select(r => r.CloneRep()).ToList(),   // [K5-VONG-SUA 05/11] trước thiếu (sót từ gói GC-SUA-TAI-CHO) → phép kiểm WHYALL / làm theo gợi ý tính như PO không có Kết quả sửa
        CaseInfo = i.CaseInfo,
        Bu = i.Bu,   // [KHACH-GUI-LAI 2026-11-11] như HandoverWhy.Clone
        TaroGops = i.TaroGops,   // [TARO-3T-GOP 06/11] như HandoverWhy.Clone (rà độc lập gói 4 mục 2)
    };
}
