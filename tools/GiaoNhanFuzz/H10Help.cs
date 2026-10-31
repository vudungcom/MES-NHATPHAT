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
        CaseInfo = i.CaseInfo,
    };
}
