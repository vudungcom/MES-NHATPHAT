using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PartMaster> PartMasters => Set<PartMaster>();
    public DbSet<PartMasterAttribute> PartMasterAttributes => Set<PartMasterAttribute>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<KhPlan> KhPlans => Set<KhPlan>();
    public DbSet<KhPlanDetail> KhPlanDetails => Set<KhPlanDetail>();
    public DbSet<KhPlanDetailChangeLog> KhPlanDetailChangeLogs => Set<KhPlanDetailChangeLog>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<FileStorage> FileStorages => Set<FileStorage>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    // ==== Thiet Bi module ====
    public DbSet<ThietBi> ThietBis => Set<ThietBi>();
    public DbSet<ThietBiChangeLog> ThietBiChangeLogs => Set<ThietBiChangeLog>();

    // ==== So Do To Chuc module ====
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentChangeLog> DepartmentChangeLogs => Set<DepartmentChangeLog>();
    public DbSet<CustomerChangeLog> CustomerChangeLogs { get; set; }
	public DbSet<PartMachiningTiming> PartMachiningTimings { get; set; }
public DbSet<KhPlanRouteSnapshotMachiningTiming> KhPlanRouteSnapshotMachiningTimings { get; set; }

    // ==== Do Ga & Phu Kien module ====
    public DbSet<DoGa> DoGas => Set<DoGa>();
    public DbSet<DoGaMuonTraLog> DoGaMuonTraLogs => Set<DoGaMuonTraLog>();
    public DbSet<DoGaChangeLog> DoGaChangeLogs => Set<DoGaChangeLog>();
    public DbSet<Dao> Daos { get; set; }
    public DbSet<DaoChangeLog> DaoChangeLogs { get; set; }
    public DbSet<KhoVatLieu> KhoVatLieus => Set<KhoVatLieu>();
    public DbSet<KhoVatLieuChangeLog> KhoVatLieuChangeLogs => Set<KhoVatLieuChangeLog>();

    // ==== Standard WTS Tasks module ====
    public DbSet<StandardWtsTask> StandardWtsTasks => Set<StandardWtsTask>();
    public DbSet<StandardWtsTaskChangeLog> StandardWtsTaskChangeLogs => Set<StandardWtsTaskChangeLog>();

    // ==== Luot 6A - Part Master process step tables (5 bang + 1 log) ====
    public DbSet<PartMachiningStep> PartMachiningSteps => Set<PartMachiningStep>();
    public DbSet<PartTaroStep> PartTaroSteps => Set<PartTaroStep>();
    public DbSet<PartBaviaStep> PartBaviaSteps => Set<PartBaviaStep>();
    public DbSet<PartWashingStep> PartWashingSteps => Set<PartWashingStep>();
    public DbSet<PartInspectionStep> PartInspectionSteps => Set<PartInspectionStep>();

    // BỔ SUNG VÙNG E
    public DbSet<PartPackagingStep> PartPackagingSteps => Set<PartPackagingStep>();

    public DbSet<PartProcessStepChangeLog> PartProcessStepChangeLogs => Set<PartProcessStepChangeLog>();

    // ==== Machine Exclude Settings (global) ====
    public DbSet<MachineExcludeSetting> MachineExcludeSettings => Set<MachineExcludeSetting>();

    // ==== KhPlan Route Snapshot (B→E + Máy loại trừ) ====
    public DbSet<KhPlanRouteSnapshotMachining>      KhPlanRouteSnapshotMachining      => Set<KhPlanRouteSnapshotMachining>();
    public DbSet<KhPlanRouteSnapshotTaro>            KhPlanRouteSnapshotTaro            => Set<KhPlanRouteSnapshotTaro>();
    public DbSet<KhPlanRouteSnapshotBavia>           KhPlanRouteSnapshotBavia           => Set<KhPlanRouteSnapshotBavia>();
    public DbSet<KhPlanRouteSnapshotWashing>         KhPlanRouteSnapshotWashing         => Set<KhPlanRouteSnapshotWashing>();
    public DbSet<KhPlanRouteSnapshotInspection>      KhPlanRouteSnapshotInspection      => Set<KhPlanRouteSnapshotInspection>();
    public DbSet<KhPlanRouteSnapshotPackaging>       KhPlanRouteSnapshotPackaging       => Set<KhPlanRouteSnapshotPackaging>();
    public DbSet<KhPlanRouteSnapshotMachineExclude>  KhPlanRouteSnapshotMachineExclude  => Set<KhPlanRouteSnapshotMachineExclude>();

    // ==== WTS Production Log (công nhân submit WTS thực tế) ====
    public DbSet<WtsProductionLog> WtsProductionLogs => Set<WtsProductionLog>();

    // ==== Worker Activity Log (thời gian chết / hoạt động không link PO) ====
    public DbSet<WorkerActivityLog> WorkerActivityLogs => Set<WorkerActivityLog>();

    // ==== Handover — Giao nhận hàng giữa các nhóm công đoạn ====
    public DbSet<HandoverTransaction> HandoverTransactions => Set<HandoverTransaction>();
    public DbSet<HandoverReceive> HandoverReceives => Set<HandoverReceive>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ==== Explicit Primary Keys ====
        mb.Entity<PartMaster>().HasKey(x => x.PartId);
        mb.Entity<PartMasterAttribute>().HasKey(x => x.AttributeId);
        mb.Entity<UserGroup>().HasKey(x => x.GroupId);
        mb.Entity<KhPlanDetailChangeLog>().HasKey(x => x.ChangeId);
        mb.Entity<StockTransaction>().HasKey(x => x.TransactionId);
        mb.Entity<FileStorage>().HasKey(x => x.StorageId);
        mb.Entity<FileAttachment>().HasKey(x => x.FileId);
        mb.Entity<ThietBi>().HasKey(x => x.ThietBiId);
        mb.Entity<ThietBiChangeLog>().HasKey(x => x.ChangeId);
        mb.Entity<Department>().HasKey(x => x.DepartmentId);
        mb.Entity<DepartmentChangeLog>().HasKey(x => x.ChangeId);

        // ==== Do Ga Primary Keys ====
        mb.Entity<DoGa>().HasKey(x => x.DoGaId);
        mb.Entity<DoGaMuonTraLog>().HasKey(x => x.TransactionId);
        mb.Entity<DoGaChangeLog>().HasKey(x => x.ChangeId);

        // ==== Standard WTS Primary Keys ====
        mb.Entity<StandardWtsTask>().HasKey(x => x.TaskId);
        mb.Entity<StandardWtsTaskChangeLog>().HasKey(x => x.ChangeId);

        // ==== Luot 6A - Explicit Primary Keys cho 6 bang moi ====
        mb.Entity<PartMachiningStep>().HasKey(x => x.StepId);
        mb.Entity<PartTaroStep>().HasKey(x => x.StepId);
        mb.Entity<PartBaviaStep>().HasKey(x => x.StepId);
        mb.Entity<PartWashingStep>().HasKey(x => x.StepId);
        mb.Entity<PartInspectionStep>().HasKey(x => x.StepId);

        // BỔ SUNG PK VÙNG E
        mb.Entity<PartPackagingStep>().HasKey(x => x.StepId);

        mb.Entity<PartProcessStepChangeLog>().HasKey(x => x.ChangeId);

        // ==== MachineExcludeSetting ====
        mb.Entity<MachineExcludeSetting>().HasKey(x => x.Id);
        mb.Entity<MachineExcludeSetting>().HasIndex(x => x.SoMay).IsUnique();
        mb.Entity<MachineExcludeSetting>()
            .HasOne(x => x.AddedByUser)
            .WithMany()
            .HasForeignKey(x => x.AddedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== Customer ====
        mb.Entity<Customer>().HasIndex(x => x.CustomerCode).IsUnique();

        // ==== PartMaster ====
        mb.Entity<PartMaster>().HasIndex(x => x.PartNo).IsUnique();

        // ==== PartMasterAttribute ====
        mb.Entity<PartMasterAttribute>().HasIndex(x => new { x.PartId, x.AttributeType, x.IsDefault });
        mb.Entity<PartMasterAttribute>().HasIndex(x => new { x.PartId, x.AttributeType, x.Value });
        mb.Entity<PartMasterAttribute>()
            .HasOne(x => x.Part)
            .WithMany(p => p.Attributes)
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartMasterAttribute>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== User ====
        mb.Entity<User>().HasIndex(x => x.Username).IsUnique();

        // ==== UserGroup ====
        mb.Entity<UserGroup>().HasIndex(x => x.GroupCode).IsUnique();

        // ==== ThietBi ====
        mb.Entity<ThietBi>().HasIndex(x => x.SoMay).IsUnique().HasFilter("[IsActive] = 1");

        // ==== ThietBiChangeLog ====
        mb.Entity<ThietBiChangeLog>().HasIndex(x => new { x.ThietBiId, x.ChangedAt });
        // Index phục vụ query hiệu suất máy: lọc sự kiện dừng máy theo khoảng thời gian
        mb.Entity<ThietBiChangeLog>().HasIndex(x => new { x.ThietBiId, x.ThoiGianBatDau, x.ThoiGianKetThuc });
        mb.Entity<ThietBiChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== Department & DepartmentChangeLog ====
        mb.Entity<Department>().HasIndex(x => x.ParentId);
        mb.Entity<DepartmentChangeLog>().HasIndex(x => new { x.DepartmentId, x.ChangedAt });
        mb.Entity<DepartmentChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== Do Ga & DoGa Logs ====
        mb.Entity<DoGa>().HasIndex(x => x.TenDoGa);
        mb.Entity<DoGa>().HasIndex(x => x.TrangThai);
        mb.Entity<DoGa>().HasIndex(x => x.SanPhamSuDung);

        mb.Entity<DoGaMuonTraLog>().HasIndex(x => new { x.DoGaId, x.NgayMuon });
        mb.Entity<DoGaMuonTraLog>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<DoGaMuonTraLog>()
            .HasOne(x => x.ReturnedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReturnedBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<DoGaChangeLog>().HasIndex(x => new { x.DoGaId, x.ChangedAt });
        mb.Entity<DoGaChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== Standard WTS Tasks & Logs ====
        mb.Entity<StandardWtsTask>().HasIndex(x => x.TaskCode).IsUnique();
        mb.Entity<StandardWtsTask>().HasIndex(x => x.CategoryCode);
        mb.Entity<StandardWtsTaskChangeLog>().HasIndex(x => new { x.TaskId, x.ChangedAt });
        mb.Entity<StandardWtsTaskChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== KhPlan ====
        mb.Entity<KhPlan>().HasIndex(x => x.PlanNo).IsUnique();
        mb.Entity<KhPlan>().HasIndex(x => new { x.CustomerId, x.PlanDate });

        // ==== KhPlanDetail ====
        mb.Entity<KhPlanDetail>().HasIndex(x => new { x.KhPlanId, x.PurchaseOrder, x.PartNo });
        mb.Entity<KhPlanDetail>().HasIndex(x => x.PurchaseOrder);
        mb.Entity<KhPlanDetail>().HasIndex(x => x.PartNo);
        mb.Entity<KhPlanDetail>().Property(x => x.Quantity).HasPrecision(18, 4);
        mb.Entity<KhPlanDetail>()
            .HasOne(x => x.KhPlan)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.KhPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // ==== KhPlanDetailChangeLog ====
        mb.Entity<KhPlanDetailChangeLog>().HasIndex(x => new { x.KhPlanDetailId, x.FieldName, x.ChangedAt });
        mb.Entity<KhPlanDetailChangeLog>().HasIndex(x => x.ImportBatchId);
        mb.Entity<KhPlanDetailChangeLog>()
            .HasOne(x => x.KhPlanDetail)
            .WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanDetailChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== StockTransaction ====
        mb.Entity<StockTransaction>().HasIndex(x => new { x.PartId, x.PurchaseOrder });
        mb.Entity<StockTransaction>().HasIndex(x => x.TransactionTime);
        mb.Entity<StockTransaction>().Property(x => x.QuantityChange).HasPrecision(18, 4);
        mb.Entity<StockTransaction>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<StockTransaction>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== FileStorage / FileAttachment ====
        mb.Entity<FileStorage>().HasIndex(x => x.StorageCode).IsUnique();
        mb.Entity<FileAttachment>().HasIndex(x => new { x.SourceType, x.SourceId });
        mb.Entity<FileAttachment>()
            .HasOne(x => x.Storage)
            .WithMany()
            .HasForeignKey(x => x.StorageId)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartMachiningStep ====
        mb.Entity<PartMachiningStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartMachiningStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartMachiningStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartMachiningStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartMachiningStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartTaroStep ====
        mb.Entity<PartTaroStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartTaroStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartTaroStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartTaroStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartTaroStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartBaviaStep ====
        mb.Entity<PartBaviaStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartBaviaStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartBaviaStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartBaviaStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartBaviaStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartWashingStep ====
        mb.Entity<PartWashingStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartWashingStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartWashingStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartWashingStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartWashingStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartInspectionStep ====
        mb.Entity<PartInspectionStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartInspectionStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartInspectionStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartInspectionStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartInspectionStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== BỔ SUNG: PartPackagingStep (Vùng E) ====
        mb.Entity<PartPackagingStep>().HasIndex(x => new { x.PartId, x.StepOrder });
        mb.Entity<PartPackagingStep>().HasIndex(x => new { x.PartId, x.IsActive });
        mb.Entity<PartPackagingStep>()
            .HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<PartPackagingStep>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<PartPackagingStep>()
            .HasOne(x => x.UpdatedByUser)
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== PartProcessStepChangeLog ====
        mb.Entity<PartProcessStepChangeLog>().HasIndex(x => new { x.StepTable, x.StepId, x.ChangedAt });
        mb.Entity<PartProcessStepChangeLog>().HasIndex(x => new { x.PartId, x.ChangedAt });
        mb.Entity<PartProcessStepChangeLog>()
            .HasOne(x => x.ChangedByUser)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== KhPlan Route Snapshots (B→E) ====
        mb.Entity<KhPlanRouteSnapshotMachining>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotMachining>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotMachining>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotMachining>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== KhPlanRouteSnapshotMachiningTiming (sub-row máy đồng dạng) ====
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>().HasKey(x => x.TimingSnapshotId);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .HasIndex(x => x.MachiningSnapshotId);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .HasIndex(x => x.KhPlanDetailId);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .Property(x => x.SetupTime).HasPrecision(10, 2);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .Property(x => x.MachiningTime).HasPrecision(10, 2);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .Property(x => x.InspectionTime).HasPrecision(10, 2);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .Property(x => x.PreparationTime).HasPrecision(10, 2);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .Property(x => x.TrialRunTime).HasPrecision(10, 2);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .HasOne(x => x.MachiningSnapshot)
            .WithMany(m => m.TimingRows)
            .HasForeignKey(x => x.MachiningSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotMachiningTiming>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<KhPlanRouteSnapshotTaro>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotTaro>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotTaro>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotTaro>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<KhPlanRouteSnapshotBavia>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotBavia>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotBavia>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotBavia>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<KhPlanRouteSnapshotWashing>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotWashing>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotWashing>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotWashing>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<KhPlanRouteSnapshotInspection>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotInspection>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotInspection>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotInspection>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<KhPlanRouteSnapshotPackaging>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotPackaging>()
            .HasIndex(x => new { x.KhPlanDetailId, x.StepOrder });
        mb.Entity<KhPlanRouteSnapshotPackaging>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotPackaging>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== KhPlanRouteSnapshotMachineExclude ====
        mb.Entity<KhPlanRouteSnapshotMachineExclude>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotMachineExclude>()
            .HasIndex(x => new { x.KhPlanDetailId, x.SoMay });
        mb.Entity<KhPlanRouteSnapshotMachineExclude>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<KhPlanRouteSnapshotMachineExclude>()
            .HasOne(x => x.SnapshotByUser).WithMany()
            .HasForeignKey(x => x.SnapshotBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== WtsProductionLog ====
        mb.Entity<WtsProductionLog>().HasKey(x => x.WtsLogId);
        mb.Entity<WtsProductionLog>().Property(x => x.QtyDone).HasPrecision(10, 2);
        mb.Entity<WtsProductionLog>()
            .HasIndex(x => new { x.KhPlanDetailId, x.ProcessGroup, x.NC });
        mb.Entity<WtsProductionLog>()
            .HasIndex(x => new { x.WorkerId, x.CreatedAt });
        // Index phục vụ query hiệu suất máy: lọc theo máy + khoảng giờ
        mb.Entity<WtsProductionLog>()
            .HasIndex(x => new { x.MachineUsed, x.StartTime, x.EndTime })
            .HasFilter("[MachineUsed] IS NOT NULL AND [IsVoided] = 0");
        mb.Entity<WtsProductionLog>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<WtsProductionLog>()
            .HasOne(x => x.Worker).WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<WtsProductionLog>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== WorkerActivityLog ====
        mb.Entity<WorkerActivityLog>().HasKey(x => x.ActivityLogId);
        mb.Entity<WorkerActivityLog>()
            .HasIndex(x => new { x.WorkerId, x.WorkDate });
        mb.Entity<WorkerActivityLog>()
            .HasIndex(x => x.WorkDate);
        mb.Entity<WorkerActivityLog>()
            .HasOne(x => x.Worker).WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<WorkerActivityLog>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== HandoverTransaction ====
        mb.Entity<HandoverTransaction>().HasKey(x => x.HandoverTxId);
        mb.Entity<HandoverTransaction>().Property(x => x.QtyIssued).HasPrecision(10, 2);
        mb.Entity<HandoverTransaction>().Property(x => x.FromNC).HasMaxLength(20);
        mb.Entity<HandoverTransaction>().Property(x => x.FromGroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverTransaction>().Property(x => x.ToGroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverTransaction>().Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");
        mb.Entity<HandoverTransaction>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverTransaction>().Property(x => x.VoidReason).HasMaxLength(200);
        mb.Entity<HandoverTransaction>()
            .HasIndex(x => new { x.KhPlanDetailId, x.IsVoided });
        mb.Entity<HandoverTransaction>()
            .HasIndex(x => new { x.FromGroupCode, x.IssuedAt });
        mb.Entity<HandoverTransaction>()
            .HasIndex(x => new { x.ToGroupCode, x.IssuedAt });
        mb.Entity<HandoverTransaction>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverTransaction>()
            .HasOne(x => x.IssuedByUser).WithMany()
            .HasForeignKey(x => x.IssuedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverTransaction>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== HandoverReceive ====
        mb.Entity<HandoverReceive>().HasKey(x => x.ReceiveId);
        mb.Entity<HandoverReceive>().Property(x => x.QtyOk).HasPrecision(10, 2).HasDefaultValue(0m);
        mb.Entity<HandoverReceive>().Property(x => x.QtyNg).HasPrecision(10, 2).HasDefaultValue(0m);
        mb.Entity<HandoverReceive>().Property(x => x.NgReason).HasMaxLength(500);
        mb.Entity<HandoverReceive>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverReceive>().Property(x => x.VoidReason).HasMaxLength(200);
        mb.Entity<HandoverReceive>()
            .HasIndex(x => new { x.HandoverTxId, x.IsVoided });
        mb.Entity<HandoverReceive>()
            .HasOne(x => x.Transaction)
            .WithMany(t => t.Receives)
            .HasForeignKey(x => x.HandoverTxId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverReceive>()
            .HasOne(x => x.ReceivedByUser).WithMany()
            .HasForeignKey(x => x.ReceivedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverReceive>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        base.OnModelCreating(mb);
    }
}
