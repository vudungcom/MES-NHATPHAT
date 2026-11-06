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
    public DbSet<KhPlanDeletedDetail> KhPlanDeletedDetails => Set<KhPlanDeletedDetail>();   // [KHE-5 2026-11-06] ảnh chụp PO trước khi xoá (SQL 11)
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
	public DbSet<PartMachiningTimingConfirm> PartMachiningTimingConfirms { get; set; }
	// Xác nhận T.Chuẩn per-dòng cho Taro/Bavia/Washing/Inspection/Packaging (Vùng C/D/E)
	public DbSet<PartStepStdTimeConfirm> PartStepStdTimeConfirms { get; set; }
public DbSet<KhPlanRouteSnapshotMachiningTiming> KhPlanRouteSnapshotMachiningTimings { get; set; }
    // [STD-UPDATE] Theo dõi thời gian tiêu chuẩn WTS (snapshot + đóng băng theo dòng WTS + lịch sử Update)
    public DbSet<KhPlanRouteSnapshotStdConfirm> KhPlanRouteSnapshotStdConfirms => Set<KhPlanRouteSnapshotStdConfirm>();
    public DbSet<WtsProductionLogStd>           WtsProductionLogStds           => Set<WtsProductionLogStd>();
    public DbSet<StdUpdateBatch>                StdUpdateBatches               => Set<StdUpdateBatch>();
    public DbSet<StdUpdateBatchDetail>          StdUpdateBatchDetails          => Set<StdUpdateBatchDetail>();

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

    // ==== WTS Category → ProcessGroup Mapping ====
    public DbSet<WtsCategoryGroupMapping> WtsCategoryGroupMappings => Set<WtsCategoryGroupMapping>();

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
    public DbSet<WtsExtraItemLog> WtsExtraItems => Set<WtsExtraItemLog>(); // Chi tiết phát sinh
    public DbSet<WtsBreakWorkLog> WtsBreakWorkLogs => Set<WtsBreakWorkLog>(); // [ML17-XN][CA-NGHI-HIST] giờ nghỉ chép lúc lưu + phút làm thông nghỉ

    // ==== Handover — Giao nhận hàng giữa các nhóm công đoạn ====
    public DbSet<HandoverTransaction> HandoverTransactions => Set<HandoverTransaction>();
    public DbSet<HandoverReceive> HandoverReceives => Set<HandoverReceive>();
    public DbSet<HandoverNgReport> HandoverNgReports => Set<HandoverNgReport>();   // [GN-NG-AFTER] báo hàng lỗi phát hiện sau khi nhận
    public DbSet<HandoverRepairResult> HandoverRepairResults => Set<HandoverRepairResult>();   // [GC-SUA-TAI-CHO] Kết quả sửa (chứng từ nội bộ nhóm)
    public DbSet<HandoverRepairCheck> HandoverRepairChecks => Set<HandoverRepairCheck>();      // [GC-SUA-TAI-CHO] KCS xác nhận hàng sửa
    public DbSet<StandardWtsTaskGop> StandardWtsTaskGops => Set<StandardWtsTaskGop>();                       // [TARO-3T-GOP 2026-11-06] mã gộp WTS tiêu chuẩn (SQL 07)
    public DbSet<KhPlanRouteSnapshotWtsGop> KhPlanRouteSnapshotWtsGops => Set<KhPlanRouteSnapshotWtsGop>();   // [TARO-3T-GOP 2026-11-06] mã gộp chụp vào PO (SQL 07)
    public DbSet<HandoverTxClose> HandoverTxCloses => Set<HandoverTxClose>();                 // [K5-CLOSE-NHIEU 2026-11-06] mỗi lần huỷ phần chưa nhận 1 dòng (SQL 10)
    // [DS-LOI][VULOI 2026-10-28] Danh mục lỗi + vụ lỗi (nhãn, không lưu số)
    public DbSet<NgCategory> NgCategories => Set<NgCategory>();
    public DbSet<NgCategoryChangeLog> NgCategoryChangeLogs => Set<NgCategoryChangeLog>();
    public DbSet<NgCase> NgCases => Set<NgCase>();

    // ==== Cài đặt ca làm việc ====
    public DbSet<CaLamViec> CaLamViecs => Set<CaLamViec>();
    public DbSet<CaLamViec_NghiGiaiLao> CaLamViec_NghiGiaiLaos => Set<CaLamViec_NghiGiaiLao>();
    public DbSet<CaLamViecChangeLog> CaLamViecChangeLogs => Set<CaLamViecChangeLog>();   // [CA-LOG 2026-11-06] lịch sử sửa ca (SQL 13)

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

        // ==== WtsCategoryGroupMapping ====
        mb.Entity<WtsCategoryGroupMapping>().HasKey(x => x.MappingId);
        mb.Entity<WtsCategoryGroupMapping>().HasIndex(x => x.ProcessGroup);
        mb.Entity<WtsCategoryGroupMapping>().HasIndex(x => x.CategoryCode);
        mb.Entity<WtsCategoryGroupMapping>()
            .HasIndex(x => new { x.CategoryCode, x.ProcessGroup }).IsUnique();
        mb.Entity<WtsCategoryGroupMapping>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
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

        // ==== [KHE-5 2026-11-06] Ảnh chụp PO trước khi xoá (SQL 11_KHE5_KhPlanDeletedDetails.sql) — không khoá ngoại ====
        mb.Entity<KhPlanDeletedDetail>().ToTable("KhPlanDeletedDetails");
        mb.Entity<KhPlanDeletedDetail>().HasKey(x => x.DeletedId);
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.PlanNo).HasMaxLength(50);
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.PurchaseOrder).HasMaxLength(100);
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.PartNo).HasMaxLength(100);
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.DetailJson).IsRequired();
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.Reason).HasMaxLength(500).IsRequired();
        mb.Entity<KhPlanDeletedDetail>().Property(x => x.DeletedAt).HasDefaultValueSql("SYSDATETIME()");
        mb.Entity<KhPlanDeletedDetail>().HasIndex(x => x.KhPlanDetailId).HasDatabaseName("IX_KhPlanDeletedDetails_Detail");
        mb.Entity<KhPlanDeletedDetail>().HasIndex(x => x.PurchaseOrder).HasDatabaseName("IX_KhPlanDeletedDetails_PO");

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
            .HasIndex(x => new { x.WorkDate });
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
        mb.Entity<HandoverTransaction>().Property(x => x.QtyClosed).HasPrecision(10, 2);    // [GN-VOID-RULE]
        mb.Entity<HandoverTransaction>().Property(x => x.ClosedReason).HasMaxLength(200);   // [GN-VOID-RULE]
        mb.Entity<HandoverTransaction>().Property(x => x.FromNC).HasMaxLength(20);
        mb.Entity<HandoverTransaction>().Property(x => x.FromGroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverTransaction>().Property(x => x.ToGroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverTransaction>().Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");
        mb.Entity<HandoverTransaction>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverTransaction>().Property(x => x.VoidReason).HasMaxLength(200);
        // v0.9 — NgQty / NgReason (bên giao khai báo NG ngay khi tạo phiếu)
        mb.Entity<HandoverTransaction>().Property(x => x.NgQty).HasPrecision(10, 2).HasDefaultValue(0m);
        mb.Entity<HandoverTransaction>().Property(x => x.NgReason).HasMaxLength(500);
        // v1.0 — IsReturnNg: phiếu trả hàng NG ngược chiều (GC→KHO, HTSP→GC...)
        mb.Entity<HandoverTransaction>().Property(x => x.IsReturnNg).HasDefaultValue(false);
        // [GN-LEDGER 2026-10-22] Loại phiếu (Hàng mới / Trả lỗi / Gửi sửa / Trả sửa / Làm hộ)
        mb.Entity<HandoverTransaction>().Property(x => x.Purpose).HasMaxLength(20);
        // [GN-CELL 2026-10-23] Ô bên giao chọn (MOI / LOI / SUA) — đúng cột Excel xưởng
        mb.Entity<HandoverTransaction>().Property(x => x.FromCell).HasMaxLength(10);
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

        // ==== [GN-NG-AFTER 2026-10-27] HandoverNgReport — báo hàng lỗi phát hiện sau khi nhận ====
        mb.Entity<HandoverNgReport>().ToTable("HandoverNgReports");
        mb.Entity<HandoverNgReport>().HasKey(x => x.HandoverNgReportId);
        mb.Entity<HandoverNgReport>().Property(x => x.Qty).HasPrecision(10, 2);
        mb.Entity<HandoverNgReport>().Property(x => x.GroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverNgReport>().Property(x => x.SourceCell).HasMaxLength(10).IsRequired();
        mb.Entity<HandoverNgReport>().Property(x => x.NC).HasMaxLength(20);
        mb.Entity<HandoverNgReport>().Property(x => x.Origin).HasMaxLength(10).IsRequired();
        mb.Entity<HandoverNgReport>().Property(x => x.CauseGroupCode).HasMaxLength(20);
        mb.Entity<HandoverNgReport>().Property(x => x.NgReason).HasMaxLength(500).IsRequired();
        mb.Entity<HandoverNgReport>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverNgReport>().Property(x => x.VoidReason).HasMaxLength(200);
        mb.Entity<HandoverNgReport>().HasIndex(x => new { x.KhPlanDetailId, x.IsVoided });
        mb.Entity<HandoverNgReport>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverNgReport>()
            .HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverNgReport>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== [GC-SUA-TAI-CHO 2026-11-04] Kết quả sửa + KCS xác nhận (SQL 08_KQSUA_HandoverRepair.sql) ====
        mb.Entity<HandoverRepairResult>().ToTable("HandoverRepairResults");
        mb.Entity<HandoverRepairResult>().HasKey(x => x.HandoverRepairResultId);
        mb.Entity<HandoverRepairResult>().Property(x => x.GroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<HandoverRepairResult>().Property(x => x.SourceCell).HasMaxLength(10).IsRequired();
        mb.Entity<HandoverRepairResult>().Property(x => x.QtyPass).HasPrecision(10, 2);
        mb.Entity<HandoverRepairResult>().Property(x => x.QtyFail).HasPrecision(10, 2);
        mb.Entity<HandoverRepairResult>().Property(x => x.ToNC).HasMaxLength(20);
        mb.Entity<HandoverRepairResult>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverRepairResult>().Property(x => x.VoidReason).HasMaxLength(200);
        mb.Entity<HandoverRepairResult>().HasIndex(x => new { x.KhPlanDetailId, x.IsVoided });
        mb.Entity<HandoverRepairResult>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverRepairResult>()
            .HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverRepairResult>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverRepairCheck>().ToTable("HandoverRepairChecks");
        mb.Entity<HandoverRepairCheck>().HasKey(x => x.HandoverRepairCheckId);
        mb.Entity<HandoverRepairCheck>().Property(x => x.QtyOk).HasPrecision(10, 2);
        mb.Entity<HandoverRepairCheck>().Property(x => x.QtyNg).HasPrecision(10, 2);
        mb.Entity<HandoverRepairCheck>().Property(x => x.Notes).HasMaxLength(500);
        mb.Entity<HandoverRepairCheck>().Property(x => x.VoidReason).HasMaxLength(200);
        mb.Entity<HandoverRepairCheck>().HasIndex(x => new { x.HandoverRepairResultId, x.IsVoided });
        mb.Entity<HandoverRepairCheck>()
            .HasOne(x => x.RepairResult).WithMany(r => r.Checks)
            .HasForeignKey(x => x.HandoverRepairResultId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverRepairCheck>()
            .HasOne(x => x.CheckedByUser).WithMany()
            .HasForeignKey(x => x.CheckedBy)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<HandoverRepairCheck>()
            .HasOne(x => x.VoidedByUser).WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .OnDelete(DeleteBehavior.NoAction);

        mb.Entity<HandoverReceive>().Property(x => x.ToNC).HasMaxLength(20);   // [NHAN-DOI-O-NC 2026-11-06] SQL 14

        // ==== [K5-CLOSE-NHIEU 2026-11-06] Huỷ phần chưa nhận — mỗi lần 1 dòng (SQL 10_K5CLOSE_HandoverTxCloses.sql) ====
        mb.Entity<HandoverTxClose>().ToTable("HandoverTxCloses");
        mb.Entity<HandoverTxClose>().HasKey(x => x.CloseId);
        mb.Entity<HandoverTxClose>().Property(x => x.Qty).HasPrecision(10, 2);
        mb.Entity<HandoverTxClose>().Property(x => x.Reason).HasMaxLength(200);
        mb.Entity<HandoverTxClose>().Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIME()");
        mb.Entity<HandoverTxClose>().HasIndex(x => new { x.HandoverTxId, x.ClosedAt }).HasDatabaseName("IX_HandoverTxCloses_Tx");
        mb.Entity<HandoverTxClose>()
            .HasOne<HandoverTransaction>().WithMany()
            .HasForeignKey(x => x.HandoverTxId)
            .HasConstraintName("FK_HandoverTxCloses_HandoverTransactions")
            .OnDelete(DeleteBehavior.NoAction);

        // ==== [TARO-NC-MA][TARO-3T-GOP 2026-11-06] SQL 07_TARO_NC_3TGOP.sql (Hùng chạy 06/11 21:45) ====
        mb.Entity<WtsProductionLog>().Property(x => x.StepNC).HasMaxLength(20);
        mb.Entity<StandardWtsTaskGop>().ToTable("StandardWtsTaskGops");
        mb.Entity<StandardWtsTaskGop>().HasKey(x => x.Id);
        mb.Entity<StandardWtsTaskGop>().Property(x => x.GopTaskCode).HasMaxLength(50).IsRequired();
        mb.Entity<StandardWtsTaskGop>().Property(x => x.PartTaskCode).HasMaxLength(50).IsRequired();
        mb.Entity<StandardWtsTaskGop>().Property(x => x.CreatedAt).HasDefaultValueSql("SYSDATETIME()");
        mb.Entity<StandardWtsTaskGop>().HasIndex(x => new { x.GopTaskCode, x.PartTaskCode }).IsUnique()
            .HasFilter("[IsActive] = 1").HasDatabaseName("UX_StandardWtsTaskGops_Active");
        mb.Entity<KhPlanRouteSnapshotWtsGop>().ToTable("KhPlanRouteSnapshotWtsGops");
        mb.Entity<KhPlanRouteSnapshotWtsGop>().HasKey(x => x.SnapshotId);
        mb.Entity<KhPlanRouteSnapshotWtsGop>().Property(x => x.GopCode).HasMaxLength(50).IsRequired();
        mb.Entity<KhPlanRouteSnapshotWtsGop>().Property(x => x.PartCode).HasMaxLength(50).IsRequired();
        mb.Entity<KhPlanRouteSnapshotWtsGop>().Property(x => x.SnapshotAt).HasDefaultValueSql("SYSDATETIME()");
        mb.Entity<KhPlanRouteSnapshotWtsGop>().HasIndex(x => x.KhPlanDetailId).HasDatabaseName("IX_KhPlanRouteSnapshotWtsGops_Detail");
        mb.Entity<KhPlanRouteSnapshotWtsGop>()
            .HasOne<KhPlanDetail>().WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .HasConstraintName("FK_KhPlanRouteSnapshotWtsGops_KhPlanDetails")
            .OnDelete(DeleteBehavior.NoAction);

        // ==== [DS-LOI][VULOI 2026-10-28] Danh mục lỗi + vụ lỗi ====
        mb.Entity<NgCategory>().ToTable("NgCategories");
        mb.Entity<NgCategory>().HasKey(x => x.NgCategoryId);
        mb.Entity<NgCategory>().Property(x => x.Code).HasMaxLength(20).IsRequired();
        mb.Entity<NgCategory>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        mb.Entity<NgCategory>().Property(x => x.Description).HasMaxLength(500);
        mb.Entity<NgCategory>().Ignore(x => x.Label);
        mb.Entity<NgCategory>().HasIndex(x => x.Code).IsUnique();
        mb.Entity<NgCategoryChangeLog>().ToTable("NgCategoryChangeLogs");
        mb.Entity<NgCategoryChangeLog>().HasKey(x => x.ChangeId);
        mb.Entity<NgCategoryChangeLog>().Property(x => x.Action).HasMaxLength(10).IsRequired();
        mb.Entity<NgCategoryChangeLog>().Property(x => x.OldValue).HasMaxLength(1000);
        mb.Entity<NgCategoryChangeLog>().Property(x => x.NewValue).HasMaxLength(1000);
        mb.Entity<NgCase>().ToTable("NgCases");
        mb.Entity<NgCase>().HasKey(x => x.NgCaseId);
        // [VL9 2026-11-06] Nguồn: CLAUDE 28/10 (VL9 mã vụ trùng từ vụ 1.000.001), HÙNG 05/11 15:37 chốt (B4).
        //   Công thức mới = SQL 12_VL9_NgCaseNo.sql (Hùng chạy MES 06/11 17:47): < 1 triệu 6 số, ≥ 1 triệu đủ số — khớp NgCaseNo.Format.
        //   Trước: RIGHT(…, 6) → vụ 1.000.001 cắt thành VL000001 (trùng vụ 1). Chỉ dùng khi app tự tạo DB trống (EnsureCreated); DB có sẵn không đổi gì.
        mb.Entity<NgCase>().Property(x => x.CaseNo).HasMaxLength(20)
            .HasComputedColumnSql("('VL' + CASE WHEN [NgCaseId] < 1000000 THEN RIGHT('000000' + CAST([NgCaseId] AS varchar(20)), 6) ELSE CAST([NgCaseId] AS varchar(20)) END)", stored: true);
        // [VL9 2026-11-06] Khai chỉ mục duy nhất đang có trên DB MES (tạo bằng SQL lúc làm VULOI, trước đây không khai ở đây → DB dựng mới thiếu chỉ mục).
        //   Hùng gửi sys.indexes 06/11 17:43: UX_NgCases_CaseNo NONCLUSTERED, unique, không lọc, 1 cột CaseNo.
        //   HasFilter(null): cột cho phép null → EF tự thêm lọc "[CaseNo] IS NOT NULL", mà SQL Server không cho lọc theo cột tính (rà độc lập T7) — DB thật không lọc.
        mb.Entity<NgCase>().HasIndex(x => x.CaseNo).IsUnique().HasFilter(null).HasDatabaseName("UX_NgCases_CaseNo");
        mb.Entity<NgCase>().Property(x => x.OriginKind).HasMaxLength(10).IsRequired();
        mb.Entity<NgCase>().Property(x => x.DetectGroupCode).HasMaxLength(20).IsRequired();
        mb.Entity<NgCase>().Property(x => x.DetectNC).HasMaxLength(20);
        mb.Entity<NgCase>().Property(x => x.CauseGroupCode).HasMaxLength(20);
        mb.Entity<NgCase>().HasIndex(x => x.KhPlanDetailId);
        mb.Entity<NgCase>()
            .HasOne(x => x.KhPlanDetail).WithMany()
            .HasForeignKey(x => x.KhPlanDetailId)
            .OnDelete(DeleteBehavior.NoAction);
        mb.Entity<NgCase>()
            .HasOne(x => x.NgCategory).WithMany()
            .HasForeignKey(x => x.NgCategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        // ==== [CA-LOG 2026-11-06] Lịch sử sửa ca (SQL 13_CA_CaLamViecChangeLogs.sql) — không khoá ngoại ====
        mb.Entity<CaLamViecChangeLog>().ToTable("CaLamViecChangeLogs");
        mb.Entity<CaLamViecChangeLog>().HasKey(x => x.LogId);
        mb.Entity<CaLamViecChangeLog>().Property(x => x.Action).HasMaxLength(20).IsRequired();
        mb.Entity<CaLamViecChangeLog>().Property(x => x.ChangedAt).HasDefaultValueSql("SYSDATETIME()");
        mb.Entity<CaLamViecChangeLog>().HasIndex(x => new { x.CaId, x.ChangedAt }).HasDatabaseName("IX_CaLamViecChangeLogs_Ca");

        // ==== CaLamViec ====
        mb.Entity<CaLamViec>().HasKey(x => x.CaId);
        mb.Entity<CaLamViec_NghiGiaiLao>().HasKey(x => x.NghiId);
        mb.Entity<CaLamViec_NghiGiaiLao>()
            .HasOne(x => x.Ca).WithMany(x => x.NghiGiaiLaos)
            .HasForeignKey(x => x.CaId).OnDelete(DeleteBehavior.Cascade);

        // ==== [ML17-XN][CA-NGHI-HIST 2026-11-01] WtsBreakWorkLogs — khớp tools/sql/06_ML17XN_WtsBreakWorkLogs.sql ====
        mb.Entity<WtsBreakWorkLog>().HasKey(x => x.Id);
        mb.Entity<WtsBreakWorkLog>().Property(x => x.BreakStart).HasColumnType("datetime2(0)");
        mb.Entity<WtsBreakWorkLog>().Property(x => x.BreakEnd).HasColumnType("datetime2(0)");
        mb.Entity<WtsBreakWorkLog>().HasIndex(x => new { x.WtsLogId, x.BreakStart }).IsUnique().HasFilter("[WtsLogId] IS NOT NULL");
        mb.Entity<WtsBreakWorkLog>().HasIndex(x => new { x.ActivityLogId, x.BreakStart }).IsUnique().HasFilter("[ActivityLogId] IS NOT NULL");
        mb.Entity<WtsBreakWorkLog>().HasOne<WtsProductionLog>().WithMany().HasForeignKey(x => x.WtsLogId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<WtsBreakWorkLog>().HasOne<WorkerActivityLog>().WithMany().HasForeignKey(x => x.ActivityLogId).OnDelete(DeleteBehavior.NoAction);

        // ==== WtsExtraItems ====
        mb.Entity<WtsExtraItemLog>().HasKey(x => x.Id);
        mb.Entity<WtsExtraItemLog>()
            .HasOne(x => x.WtsProductionLog)
            .WithMany()
            .HasForeignKey(x => x.WtsLogId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<WtsExtraItemLog>()
            .HasOne(x => x.WorkerActivityLog)
            .WithMany()
            .HasForeignKey(x => x.ActivityLogId)
            .OnDelete(DeleteBehavior.Cascade);

        // ==== [STD-UPDATE] Thời gian tiêu chuẩn WTS ====
        // Khớp với SQL_StdUpdate_01_Schema.sql (unique index + FK cascade)
        mb.Entity<KhPlanRouteSnapshotStdConfirm>()
            .HasIndex(x => new { x.SnapshotTable, x.SnapshotRowId, x.FieldName }).IsUnique();
        mb.Entity<KhPlanRouteSnapshotStdConfirm>()
            .HasIndex(x => x.KhPlanDetailId);

        mb.Entity<WtsProductionLogStd>()
            .HasIndex(x => new { x.WtsLogId, x.FieldName }).IsUnique();
        mb.Entity<WtsProductionLogStd>()
            .HasIndex(x => x.KhPlanDetailId);
        mb.Entity<WtsProductionLogStd>()
            .HasOne(x => x.WtsProductionLog).WithMany()
            .HasForeignKey(x => x.WtsLogId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<StdUpdateBatch>()
            .HasIndex(x => new { x.PartId, x.RunAt });
        mb.Entity<StdUpdateBatchDetail>()
            .HasOne(x => x.Batch).WithMany(x => x.Details)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<StdUpdateBatchDetail>().HasIndex(x => x.KhPlanDetailId);
        mb.Entity<StdUpdateBatchDetail>().HasIndex(x => x.WtsLogId);

        base.OnModelCreating(mb);
    }
}
