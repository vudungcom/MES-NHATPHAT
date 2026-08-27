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

    // ==== Do Ga & Phu Kien module ====
    public DbSet<DoGa> DoGas => Set<DoGa>();
    public DbSet<DoGaMuonTraLog> DoGaMuonTraLogs => Set<DoGaMuonTraLog>();
    public DbSet<DoGaChangeLog> DoGaChangeLogs => Set<DoGaChangeLog>();
	public DbSet<Dao> Daos { get; set; }
public DbSet<DaoChangeLog> DaoChangeLogs { get; set; }

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

        base.OnModelCreating(mb);
    }
}