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
        public DbSet<KhoVatLieuChangeLog> KhoVatLieuChangeLogs => Set<KhoVatLieuChangeLog>();
        public DbSet<KhPlanDetailChangeLog> KhPlanDetailChangeLogs => Set<KhPlanDetailChangeLog>();
        public DbSet<WtsProductionLog> WtsProductionLogs => Set<WtsProductionLog>();
        public DbSet<HandoverTransaction> HandoverTransactions => Set<HandoverTransaction>();
        public DbSet<HandoverReceive> HandoverReceives => Set<HandoverReceive>();
        public DbSet<HandoverNgReport> HandoverNgReports => Set<HandoverNgReport>();   // [GN-NG-AFTER]
        public DbSet<KhPlanRouteSnapshotMachining> KhPlanRouteSnapshotMachining => Set<KhPlanRouteSnapshotMachining>();
        public DbSet<KhPlanRouteSnapshotTaro> KhPlanRouteSnapshotTaro => Set<KhPlanRouteSnapshotTaro>();
        public DbSet<KhPlanRouteSnapshotBavia> KhPlanRouteSnapshotBavia => Set<KhPlanRouteSnapshotBavia>();
        public DbSet<KhPlanRouteSnapshotWashing> KhPlanRouteSnapshotWashing => Set<KhPlanRouteSnapshotWashing>();
        public DbSet<KhPlanRouteSnapshotInspection> KhPlanRouteSnapshotInspection => Set<KhPlanRouteSnapshotInspection>();
        public DbSet<KhPlanRouteSnapshotPackaging> KhPlanRouteSnapshotPackaging => Set<KhPlanRouteSnapshotPackaging>();
        public DbSet<KhPlanRouteSnapshotMachineExclude> KhPlanRouteSnapshotMachineExclude => Set<KhPlanRouteSnapshotMachineExclude>();

        public DbSet<PartMaster> PartMasters => Set<PartMaster>();
        public DbSet<PartMasterAttribute> PartMasterAttributes => Set<PartMasterAttribute>();
        public DbSet<UserGroup> UserGroups => Set<UserGroup>();
        public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
        public DbSet<FileStorage> FileStorages => Set<FileStorage>();
        public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
        public DbSet<ThietBi> ThietBis => Set<ThietBi>();
        public DbSet<ThietBiChangeLog> ThietBiChangeLogs => Set<ThietBiChangeLog>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<DepartmentChangeLog> DepartmentChangeLogs => Set<DepartmentChangeLog>();
        public DbSet<CustomerChangeLog> CustomerChangeLogs => Set<CustomerChangeLog>();
        public DbSet<PartMachiningTiming> PartMachiningTimings => Set<PartMachiningTiming>();
        public DbSet<PartMachiningTimingConfirm> PartMachiningTimingConfirms => Set<PartMachiningTimingConfirm>();
        public DbSet<PartStepStdTimeConfirm> PartStepStdTimeConfirms => Set<PartStepStdTimeConfirm>();
        public DbSet<KhPlanRouteSnapshotMachiningTiming> KhPlanRouteSnapshotMachiningTimings => Set<KhPlanRouteSnapshotMachiningTiming>();
        public DbSet<KhPlanRouteSnapshotStdConfirm> KhPlanRouteSnapshotStdConfirms => Set<KhPlanRouteSnapshotStdConfirm>();
        public DbSet<WtsProductionLogStd> WtsProductionLogStds => Set<WtsProductionLogStd>();
        public DbSet<StdUpdateBatch> StdUpdateBatches => Set<StdUpdateBatch>();
        public DbSet<StdUpdateBatchDetail> StdUpdateBatchDetails => Set<StdUpdateBatchDetail>();
        public DbSet<DoGa> DoGas => Set<DoGa>();
        public DbSet<DoGaMuonTraLog> DoGaMuonTraLogs => Set<DoGaMuonTraLog>();
        public DbSet<DoGaChangeLog> DoGaChangeLogs => Set<DoGaChangeLog>();
        public DbSet<Dao> Daos => Set<Dao>();
        public DbSet<DaoChangeLog> DaoChangeLogs => Set<DaoChangeLog>();
        public DbSet<StandardWtsTask> StandardWtsTasks => Set<StandardWtsTask>();
        public DbSet<StandardWtsTaskChangeLog> StandardWtsTaskChangeLogs => Set<StandardWtsTaskChangeLog>();
        public DbSet<WtsCategoryGroupMapping> WtsCategoryGroupMappings => Set<WtsCategoryGroupMapping>();
        public DbSet<PartMachiningStep> PartMachiningSteps => Set<PartMachiningStep>();
        public DbSet<PartTaroStep> PartTaroSteps => Set<PartTaroStep>();
        public DbSet<PartBaviaStep> PartBaviaSteps => Set<PartBaviaStep>();
        public DbSet<PartWashingStep> PartWashingSteps => Set<PartWashingStep>();
        public DbSet<PartInspectionStep> PartInspectionSteps => Set<PartInspectionStep>();
        public DbSet<PartPackagingStep> PartPackagingSteps => Set<PartPackagingStep>();
        public DbSet<PartProcessStepChangeLog> PartProcessStepChangeLogs => Set<PartProcessStepChangeLog>();
        public DbSet<MachineExcludeSetting> MachineExcludeSettings => Set<MachineExcludeSetting>();
        public DbSet<WorkerActivityLog> WorkerActivityLogs => Set<WorkerActivityLog>();
        public DbSet<WtsExtraItemLog> WtsExtraItems => Set<WtsExtraItemLog>();
        public DbSet<CaLamViec> CaLamViecs => Set<CaLamViec>();
        public DbSet<CaLamViec_NghiGiaiLao> CaLamViec_NghiGiaiLaos => Set<CaLamViec_NghiGiaiLao>();

        public AppDbContext()
        {
            // tạo sẵn mọi set để snapshot giao dịch luôn thấy đủ bảng
            _ = Customers; _ = Users; _ = KhPlans; _ = KhPlanDetails; _ = KhoVatLieus; _ = KhoVatLieuChangeLogs; _ = KhPlanDetailChangeLogs; _ = WtsProductionLogs;
            _ = HandoverTransactions; _ = HandoverNgReports; _ = HandoverReceives; _ = KhPlanRouteSnapshotMachining; _ = KhPlanRouteSnapshotTaro;
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
