// [WTS-SVC harness] Nối navigation theo khoá ngoại cho AppDbContext thật chạy trên EF giả (EF thật tự làm việc này).
using MES.Web.Data;
using MES.Web.Data.Entities;

public static class HarnessFixup
{
    public static void Run(AppDbContext db)
    {
        var users  = db.Users.List.ToDictionary(u => u.UserId);
        var groups = db.UserGroups.List.ToDictionary(g => g.GroupId);
        foreach (var u in db.Users.List)
            u.Group = u.GroupId != null && groups.TryGetValue(u.GroupId.Value, out var g) ? g : u.Group;
        var custs = db.Customers.List.ToDictionary(c => c.CustomerId);
        var plans = db.KhPlans.List.ToDictionary(p => p.KhPlanId);
        foreach (var p in db.KhPlans.List) { p.Details = new List<KhPlanDetail>(); if (custs.TryGetValue(p.CustomerId, out var c)) p.Customer = c; }
        var dets = db.KhPlanDetails.List.ToDictionary(d => d.KhPlanDetailId);
        foreach (var d in db.KhPlanDetails.List)
            if (plans.TryGetValue(d.KhPlanId, out var p)) { d.KhPlan = p; p.Details.Add(d); }
        var txById = db.HandoverTransactions.List.ToDictionary(t => t.HandoverTxId);
        foreach (var t in db.HandoverTransactions.List)
        {
            t.Receives = new List<HandoverReceive>();
            if (users.TryGetValue(t.IssuedBy, out var u)) t.IssuedByUser = u;
            if (dets.TryGetValue(t.KhPlanDetailId, out var d)) t.KhPlanDetail = d;
        }
        foreach (var r in db.HandoverReceives.List)
        {
            if (txById.TryGetValue(r.HandoverTxId, out var t)) { r.Transaction = t; t.Receives.Add(r); }
            if (users.TryGetValue(r.ReceivedBy, out var u)) r.ReceivedByUser = u;
        }
        var cas = db.CaLamViecs.List.ToDictionary(c => c.CaId);
        foreach (var c in db.CaLamViecs.List) c.NghiGiaiLaos = new List<CaLamViec_NghiGiaiLao>();
        foreach (var n in db.CaLamViec_NghiGiaiLaos.List) if (cas.TryGetValue(n.CaId, out var c)) { n.Ca = c; c.NghiGiaiLaos.Add(n); }
        foreach (var w in db.WtsProductionLogs.List)
        {
            if (users.TryGetValue(w.WorkerId, out var u)) w.Worker = u;
            if (dets.TryGetValue(w.KhPlanDetailId, out var d)) w.KhPlanDetail = d;
            if (w.CaId != null && cas.TryGetValue(w.CaId.Value, out var c)) w.Ca = c;
        }
        foreach (var w in db.WorkerActivityLogs.List)
            if (users.TryGetValue(w.WorkerId, out var u)) w.Worker = u;
        var snapTim = db.KhPlanRouteSnapshotMachiningTimings.List.GroupBy(t => t.MachiningSnapshotId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var m in db.KhPlanRouteSnapshotMachining.List)
            if (snapTim.TryGetValue(m.SnapshotId, out var ts))
                foreach (var t in ts) if (!m.TimingRows.Contains(t)) m.TimingRows.Add(t);
        var timByStep = db.PartMachiningTimings.List.Where(t => t.IsActive).GroupBy(t => t.StepId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var s in db.PartMachiningSteps.List)
            s.Timings = timByStep.TryGetValue(s.StepId, out var ts) ? ts.ToList() : new List<PartMachiningTiming>();
    }
}
