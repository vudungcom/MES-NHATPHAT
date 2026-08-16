using System.ComponentModel.DataAnnotations;

namespace MES.Web.Data.Entities;

/// <summary>
/// Event log tồn kho. Append-only, KHÔNG UPDATE/DELETE.
/// Query tồn hiện tại: SUM(QuantityChange) GROUP BY PartId, PurchaseOrder.
/// </summary>
public class StockTransaction
{
    public long TransactionId { get; set; }

    public DateTime TransactionTime { get; set; } = DateTime.Now;

    [Required, MaxLength(30)]
    public string TransactionType { get; set; } = "";
    // RECEIVE / ISSUE / RETURN / NG_REJECT / ADJUST

    public int PartId { get; set; }
    public PartMaster? Part { get; set; }

    [Required, MaxLength(50)]
    public string PurchaseOrder { get; set; } = "";

    [MaxLength(50)]
    public string? ManufacturingOrder { get; set; }

    /// <summary>Số dương = nhập, số âm = xuất</summary>
    public decimal QuantityChange { get; set; }

    [MaxLength(30)]
    public string? SourceSlipType { get; set; }  // ReceivingSlip / IssueSlip / KhPlan / ...

    public long? SourceSlipId { get; set; }
    public long? SourceDetailId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
