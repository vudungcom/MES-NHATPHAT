using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Data
{
    /// <summary>AppDbContext giả: chỉ các bảng HandoverService / HandoverLedgerService dùng.</summary>
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<User> Users => Set<User>();
        public DbSet<KhPlan> KhPlans => Set<KhPlan>();
        public DbSet<KhPlanDetail> KhPlanDetails => Set<KhPlanDetail>();
        public DbSet<KhoVatLieu> KhoVatLieus => Set<KhoVatLieu>();
        public DbSet<WtsProductionLog> WtsProductionLogs => Set<WtsProductionLog>();
        public DbSet<HandoverTransaction> HandoverTransactions => Set<HandoverTransaction>();
        public DbSet<HandoverReceive> HandoverReceives => Set<HandoverReceive>();
        public DbSet<KhPlanRouteSnapshotMachining> KhPlanRouteSnapshotMachining => Set<KhPlanRouteSnapshotMachining>();
        public DbSet<KhPlanRouteSnapshotTaro> KhPlanRouteSnapshotTaro => Set<KhPlanRouteSnapshotTaro>();
        public DbSet<KhPlanRouteSnapshotBavia> KhPlanRouteSnapshotBavia => Set<KhPlanRouteSnapshotBavia>();
        public DbSet<KhPlanRouteSnapshotWashing> KhPlanRouteSnapshotWashing => Set<KhPlanRouteSnapshotWashing>();
        public DbSet<KhPlanRouteSnapshotInspection> KhPlanRouteSnapshotInspection => Set<KhPlanRouteSnapshotInspection>();
        public DbSet<KhPlanRouteSnapshotPackaging> KhPlanRouteSnapshotPackaging => Set<KhPlanRouteSnapshotPackaging>();
        public DbSet<KhPlanRouteSnapshotMachineExclude> KhPlanRouteSnapshotMachineExclude => Set<KhPlanRouteSnapshotMachineExclude>();

        public AppDbContext()
        {
            // tạo sẵn mọi set để snapshot giao dịch luôn thấy đủ bảng
            _ = Customers; _ = Users; _ = KhPlans; _ = KhPlanDetails; _ = KhoVatLieus; _ = WtsProductionLogs;
            _ = HandoverTransactions; _ = HandoverReceives; _ = KhPlanRouteSnapshotMachining; _ = KhPlanRouteSnapshotTaro;
            _ = KhPlanRouteSnapshotBavia; _ = KhPlanRouteSnapshotWashing; _ = KhPlanRouteSnapshotInspection;
            _ = KhPlanRouteSnapshotPackaging; _ = KhPlanRouteSnapshotMachineExclude;
        }

        public override void Fixup()
        {
            var users = Users.List.ToDictionary(u => u.UserId);
            var txById = HandoverTransactions.List.ToDictionary(t => t.HandoverTxId);
            foreach (var t in HandoverTransactions.List)
            {
                t.Receives = new List<HandoverReceive>();
                if (users.TryGetValue(t.IssuedBy, out var u)) t.IssuedByUser = u;
            }
            foreach (var r in HandoverReceives.List)
            {
                if (txById.TryGetValue(r.HandoverTxId, out var t)) { r.Transaction = t; t.Receives.Add(r); }
                if (users.TryGetValue(r.ReceivedBy, out var u)) r.ReceivedByUser = u;
            }
            foreach (var w in WtsProductionLogs.List)
                if (users.TryGetValue(w.WorkerId, out var u)) w.Worker = u;
        }
    }
}
