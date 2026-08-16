using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

public class KhPlan
{
    public int KhPlanId { get; set; }

    [Required, MaxLength(30)]
    public string PlanNo { get; set; } = "";  // KH-YYYYMMDD-####

    /// <summary>Ngày ghi trên PO của khách</summary>
    public DateTime PlanDate { get; set; }

    /// <summary>Ngày công ty thực nhận PO (khách gửi email/fax) — có thể khác PlanDate</summary>
    public DateTime? ReceivedDate { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? AttachmentFilePath { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "WaitingSupplierOrder";

    public int CreatedBy { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<KhPlanDetail> Details { get; set; } = new();
}

public class KhPlanDetail
{
    public int KhPlanDetailId { get; set; }

    public int KhPlanId { get; set; }
    public KhPlan? KhPlan { get; set; }

    public int LineNo { get; set; }

    [Required, MaxLength(100)]
    public string PartNo { get; set; } = "";

    [Required, MaxLength(50)]
    public string PurchaseOrder { get; set; } = "";

    /// <summary>
    /// Nếu dòng này được tạo ra từ tách PO cũ → lưu số PO gốc.
    /// Ví dụ: PO cũ 0003374817 (10 EA) được tách thành 2 PO mới:
    ///   Dòng 1: PurchaseOrder='0003958168', OldPurchaseOrder='0003374817', Quantity=6
    ///   Dòng 2: PurchaseOrder='0003958169', OldPurchaseOrder='0003374817', Quantity=4
    /// Dòng gốc bị xóa. Truy vết qua ChangeLog (FieldName='SplitInto').
    /// </summary>
    [MaxLength(50)]
    public string? OldPurchaseOrder { get; set; }

    [MaxLength(50)]
    public string? ManufacturingOrder { get; set; }

    public decimal Quantity { get; set; }

    [MaxLength(10)]
    public string Unit { get; set; } = "EA";

    public DateTime STD { get; set; }

    [MaxLength(100)]
    public string? Type { get; set; }

    [MaxLength(100)]
    public string? Priority { get; set; }

    // ==== Snapshot từ PartMasterAttribute tại thời điểm tạo phiếu ====
    [MaxLength(100)]
    public string? MaterialConfig { get; set; }

    [MaxLength(100)]
    public string? Material { get; set; }

    // ==== Thông tin nội bộ ====
    [MaxLength(50)]
    public string? OrderVL { get; set; }

    /// <summary>
    /// Vị trí kho — từ v0.5 nhập TỰ DO, KHÔNG lấy từ PartMaster.
    /// Kho sẽ điền khi phôi về.
    /// </summary>
    [MaxLength(50)]
    public string? Location { get; set; }

    [MaxLength(200)]
    public string? MaterialCondition { get; set; }

    [MaxLength(500)]
    public string? PartNotes { get; set; }

    [MaxLength(1000)]
    public string? MaterialNotes { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "WaitingSupplierOrder";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
