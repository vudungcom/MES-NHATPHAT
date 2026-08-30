using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class KhPlanService
{
    private readonly AppDbContext _db;

    // Format quantity: có thập phân thì hiện, không thì thôi (0.## = tối đa 2 chữ số thập phân)
    private const string QtyFormat = "0.####";

    public KhPlanService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> GenerateNextPlanNoAsync(DateTime date)
    {
        var prefix = $"KH-{date:yyyyMMdd}-";
        var count = await _db.KhPlans.CountAsync(x => x.PlanNo.StartsWith(prefix));
        return $"{prefix}{(count + 1):D4}";
    }

    public async Task<int> CreateAsync(KhPlan plan, int userId)
    {
        plan.PlanNo = await GenerateNextPlanNoAsync(plan.PlanDate);
        plan.CreatedBy = userId;
        plan.CreatedAt = DateTime.Now;
        plan.Status = "WaitingSupplierOrder";

        int line = 1;
        foreach (var d in plan.Details)
        {
            d.LineNo = line++;
            d.CreatedAt = DateTime.Now;
            d.Status = "WaitingSupplierOrder";
        }

        _db.KhPlans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.KhPlanId;
    }

    public async Task<KhPlan?> GetByIdAsync(int id)
    {
        return await _db.KhPlans
            .Include(x => x.Customer)
            .Include(x => x.CreatedByUser)
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.KhPlanId == id);
    }

    /// <summary>
    /// Lấy 1 KhPlanDetail cùng thông tin phiếu cha, dùng cho View/Edit chỉ hiển thị 1 dòng.
    /// Trả về (KhPlan header, KhPlanDetail cụ thể).
    /// </summary>
    public async Task<(KhPlan? Plan, KhPlanDetail? Detail)> GetDetailByIdAsync(int khPlanDetailId)
    {
        var detail = await _db.KhPlanDetails
            .FirstOrDefaultAsync(x => x.KhPlanDetailId == khPlanDetailId);
        if (detail == null) return (null, null);

        var plan = await _db.KhPlans
            .Include(x => x.Customer)
            .Include(x => x.CreatedByUser)
            .FirstOrDefaultAsync(x => x.KhPlanId == detail.KhPlanId);

        return (plan, detail);
    }

    public async Task<List<KhPlan>> GetListAsync(int? customerId = null, string? status = null)
    {
        var q = _db.KhPlans
            .Include(x => x.Customer)
            .Include(x => x.Details)
            .AsQueryable();

        if (customerId.HasValue) q = q.Where(x => x.CustomerId == customerId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);

        return await q.OrderByDescending(x => x.PlanDate)
                      .ThenByDescending(x => x.KhPlanId)
                      .Take(500)
                      .ToListAsync();
    }

    public async Task<List<KhPlanDetailRow>> GetDetailRowsAsync()
    {
        var q = from d in _db.KhPlanDetails
                join p in _db.KhPlans on d.KhPlanId equals p.KhPlanId
                join c in _db.Customers on p.CustomerId equals c.CustomerId
                join k in _db.KhoVatLieus on d.KhPlanDetailId equals k.PlanDetailId into kGroup
                from k in kGroup.DefaultIfEmpty()
                orderby p.PlanDate descending, d.LineNo
                select new KhPlanDetailRow
                {
                    KhPlanDetailId = d.KhPlanDetailId,
                    KhPlanId = p.KhPlanId,
                    PlanNo = p.PlanNo,
                    PlanDate = p.PlanDate,
                    ReceivedDate = p.ReceivedDate,
                    CustomerName = c.CustomerName,
                    PurchaseOrder = d.PurchaseOrder,
                    OldPurchaseOrder = d.OldPurchaseOrder,
                    PartNo = d.PartNo,
                    Quantity = d.Quantity,
                    Unit = d.Unit,
                    STD = d.STD,
                    Type = d.Type,
                    Priority = d.Priority,
                    Material = d.Material,
                    MaterialConfig = d.MaterialConfig,
                    OrderVL = d.OrderVL,
                    Location = d.Location,
                    PartNotes = d.PartNotes,
                    Status = d.Status,
                    // Lấy từ Kho — chỉ để hiển thị
                    KhoOrderVL = k != null ? k.OrderVatLieu : null,
                    KhoViTri   = k != null ? k.ViTriDePhoi  : null,
                };

        return await q.Take(1000).ToListAsync();
    }

    public async Task UpdateFieldAsync(
        int khPlanDetailId,
        string fieldName,
        string? newValue,
        int changedBy,
        string source = "Manual",
        long? importBatchId = null,
        string? reason = null)
    {
        var detail = await _db.KhPlanDetails.FirstOrDefaultAsync(x => x.KhPlanDetailId == khPlanDetailId);
        if (detail == null) throw new InvalidOperationException($"KhPlanDetail {khPlanDetailId} không tồn tại");

        string? oldValue = fieldName switch
        {
            "STD" => detail.STD.ToString("yyyy-MM-dd"),
            "Quantity" => detail.Quantity.ToString(QtyFormat),
            "OrderVL" => detail.OrderVL,
            "Location" => detail.Location,
            "Type" => detail.Type,
            "Priority" => detail.Priority,
            "PartNotes" => detail.PartNotes,
            "MaterialNotes" => detail.MaterialNotes,
            _ => throw new InvalidOperationException($"Field '{fieldName}' không được phép sửa qua UpdateFieldAsync")
        };

        switch (fieldName)
        {
            case "STD": detail.STD = DateTime.Parse(newValue!); break;
            case "Quantity": detail.Quantity = decimal.Parse(newValue!); break;
            case "OrderVL": detail.OrderVL = newValue; break;
            case "Location": detail.Location = newValue; break;
            case "Type": detail.Type = newValue; break;
            case "Priority": detail.Priority = newValue; break;
            case "PartNotes": detail.PartNotes = newValue; break;
            case "MaterialNotes": detail.MaterialNotes = newValue; break;
        }
        detail.UpdatedAt = DateTime.Now;

        _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
        {
            KhPlanDetailId = khPlanDetailId,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = changedBy,
            ChangedAt = DateTime.Now,
            ChangeSource = source,
            ImportBatchId = importBatchId,
            Reason = reason
        });

        await _db.SaveChangesAsync();
    }

    public async Task<int> UpdateFullAsync(
        int khPlanDetailId,
        KhPlanDetail newValues,
        int changedBy,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Bắt buộc nhập lý do sửa");

        var d = await _db.KhPlanDetails.FirstOrDefaultAsync(x => x.KhPlanDetailId == khPlanDetailId);
        if (d == null) throw new InvalidOperationException("Dòng không tồn tại");

        int changeCount = 0;

        void LogChange(string fieldName, string? oldVal, string? newVal)
        {
            if (oldVal == newVal) return;
            _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
            {
                KhPlanDetailId = khPlanDetailId,
                FieldName = fieldName,
                OldValue = oldVal,
                NewValue = newVal,
                ChangedBy = changedBy,
                ChangedAt = DateTime.Now,
                ChangeSource = "Manual",
                Reason = reason
            });
            changeCount++;
        }

        // Quantity: format decimal đẹp
        if (d.Quantity != newValues.Quantity)
        {
            LogChange("Quantity", d.Quantity.ToString(QtyFormat), newValues.Quantity.ToString(QtyFormat));
            d.Quantity = newValues.Quantity;
        }
        if (d.Unit != newValues.Unit) { LogChange("Unit", d.Unit, newValues.Unit); d.Unit = newValues.Unit; }
        if (d.STD != newValues.STD) { LogChange("STD", d.STD.ToString("yyyy-MM-dd"), newValues.STD.ToString("yyyy-MM-dd")); d.STD = newValues.STD; }
        if ((d.Type ?? "") != (newValues.Type ?? "")) { LogChange("Type", d.Type, newValues.Type); d.Type = newValues.Type; }
        if ((d.Priority ?? "") != (newValues.Priority ?? "")) { LogChange("Priority", d.Priority, newValues.Priority); d.Priority = newValues.Priority; }
        if ((d.Material ?? "") != (newValues.Material ?? "")) { LogChange("Material", d.Material, newValues.Material); d.Material = newValues.Material; }
        if ((d.MaterialConfig ?? "") != (newValues.MaterialConfig ?? "")) { LogChange("MaterialConfig", d.MaterialConfig, newValues.MaterialConfig); d.MaterialConfig = newValues.MaterialConfig; }
        if ((d.Location ?? "") != (newValues.Location ?? "")) { LogChange("Location", d.Location, newValues.Location); d.Location = newValues.Location; }
        if ((d.MaterialNotes ?? "") != (newValues.MaterialNotes ?? "")) { LogChange("MaterialNotes", d.MaterialNotes, newValues.MaterialNotes); d.MaterialNotes = newValues.MaterialNotes; }
        if ((d.OrderVL ?? "") != (newValues.OrderVL ?? "")) { LogChange("OrderVL", d.OrderVL, newValues.OrderVL); d.OrderVL = newValues.OrderVL; }
        if ((d.MaterialCondition ?? "") != (newValues.MaterialCondition ?? "")) { LogChange("MaterialCondition", d.MaterialCondition, newValues.MaterialCondition); d.MaterialCondition = newValues.MaterialCondition; }
        if ((d.PartNotes ?? "") != (newValues.PartNotes ?? "")) { LogChange("PartNotes", d.PartNotes, newValues.PartNotes); d.PartNotes = newValues.PartNotes; }

        if (changeCount > 0)
        {
            d.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }

        return changeCount;
    }

    /// <summary>
    /// Tách 1 KhPlanDetail thành N dòng mới. Xóa dòng gốc.
    /// v0.5.2:
    /// - Log SplitFrom tách rõ OldValue = "PO gốc (SL N)", NewValue = "PO mới (SL N)"
    /// - Format số dùng QtyFormat (không lẻ .0000)
    /// - Sau khi tách xong, RENUMBER LineNo toàn phiếu về 1, 2, 3... theo CreatedAt
    /// </summary>
    public async Task<List<int>> SplitPOAsync(
        int sourceDetailId,
        List<SplitPORow> newRows,
        int changedBy,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Bắt buộc nhập lý do tách");
        if (newRows == null || newRows.Count < 2)
            throw new InvalidOperationException("Cần ít nhất 2 dòng mới để tách");

        var source = await _db.KhPlanDetails
            .Include(x => x.KhPlan)
            .FirstOrDefaultAsync(x => x.KhPlanDetailId == sourceDetailId);
        if (source == null) throw new InvalidOperationException("Dòng gốc không tồn tại");

        var totalNewQty = newRows.Sum(r => r.Quantity);
        if (totalNewQty != source.Quantity)
            throw new InvalidOperationException(
                $"Tổng SL các PO mới ({totalNewQty}) không khớp SL PO gốc ({source.Quantity}). " +
                "Phải bằng nhau tuyệt đối.");

        var duplicates = newRows.GroupBy(r => r.PurchaseOrder).Where(g => g.Count() > 1).ToList();
        if (duplicates.Any())
            throw new InvalidOperationException($"PO mới bị trùng: {string.Join(", ", duplicates.Select(g => g.Key))}");

        foreach (var r in newRows)
        {
            if (string.IsNullOrWhiteSpace(r.PurchaseOrder))
                throw new InvalidOperationException("PO mới không được trống");
            if (r.Quantity <= 0)
                throw new InvalidOperationException($"SL của PO {r.PurchaseOrder} phải > 0");

            var exists = await _db.KhPlanDetails
                .AnyAsync(x => x.PurchaseOrder == r.PurchaseOrder && x.KhPlanDetailId != sourceDetailId);
            if (exists)
                throw new InvalidOperationException($"PO {r.PurchaseOrder} đã tồn tại trong hệ thống");
        }

        // Snapshot data từ source
        var srcKhPlanId = source.KhPlanId;
        var srcPartNo = source.PartNo;
        var srcPO = source.PurchaseOrder;
        var srcQty = source.Quantity;
        var srcManufacturingOrder = source.ManufacturingOrder;
        var srcUnit = source.Unit;
        var srcSTD = source.STD;
        var srcType = source.Type;
        var srcPriority = source.Priority;
        var srcMaterial = source.Material;
        var srcMaterialConfig = source.MaterialConfig;
        var srcOrderVL = source.OrderVL;
        var srcLocation = source.Location;
        var srcMaterialCondition = source.MaterialCondition;
        var srcPartNotes = source.PartNotes;
        var srcMaterialNotes = source.MaterialNotes;
        var srcStatus = source.Status;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var newDetailIds = new List<int>();
            var newIdList = new List<string>();

            var maxLineNo = await _db.KhPlanDetails
                .Where(x => x.KhPlanId == srcKhPlanId)
                .MaxAsync(x => (int?)x.LineNo) ?? 0;

            // 1. Tạo N dòng mới
            foreach (var r in newRows)
            {
                var newDetail = new KhPlanDetail
                {
                    KhPlanId = srcKhPlanId,
                    LineNo = ++maxLineNo,  // tạm, sẽ renumber ở cuối
                    PartNo = srcPartNo,
                    PurchaseOrder = r.PurchaseOrder.Trim(),
                    OldPurchaseOrder = srcPO,
                    ManufacturingOrder = srcManufacturingOrder,
                    Quantity = r.Quantity,
                    Unit = srcUnit,
                    STD = srcSTD,
                    Type = srcType,
                    Priority = srcPriority,
                    Material = srcMaterial,
                    MaterialConfig = srcMaterialConfig,
                    OrderVL = srcOrderVL,
                    Location = srcLocation,
                    MaterialCondition = srcMaterialCondition,
                    PartNotes = srcPartNotes,
                    MaterialNotes = srcMaterialNotes,
                    Status = srcStatus,
                    CreatedAt = DateTime.Now
                };
                _db.KhPlanDetails.Add(newDetail);
                await _db.SaveChangesAsync();
                newDetailIds.Add(newDetail.KhPlanDetailId);
                newIdList.Add($"{r.PurchaseOrder}={r.Quantity.ToString(QtyFormat)}");

                // Log SplitFrom cho dòng mới - tách rõ Old/New
                _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
                {
                    KhPlanDetailId = newDetail.KhPlanDetailId,
                    FieldName = "SplitFrom",
                    OldValue = $"{srcPO} (SL {srcQty.ToString(QtyFormat)})",
                    NewValue = $"{r.PurchaseOrder} (SL {r.Quantity.ToString(QtyFormat)})",
                    ChangedBy = changedBy,
                    ChangedAt = DateTime.Now,
                    ChangeSource = "Manual",
                    Reason = reason
                });
            }

            // 2. Log cho dòng gốc
            var splitInfo = string.Join(", ", newIdList);
            _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
            {
                KhPlanDetailId = sourceDetailId,
                FieldName = "SplitInto",
                OldValue = $"{srcPO} (SL {srcQty.ToString(QtyFormat)})",
                NewValue = splitInfo,
                ChangedBy = changedBy,
                ChangedAt = DateTime.Now,
                ChangeSource = "Manual",
                Reason = reason
            });
            await _db.SaveChangesAsync();

            // 3. Detach source + log liên quan khỏi tracker
            _db.Entry(source).State = EntityState.Detached;
            var localLogs = _db.ChangeTracker.Entries<KhPlanDetailChangeLog>()
                .Where(e => e.Entity.KhPlanDetailId == sourceDetailId)
                .ToList();
            foreach (var log in localLogs)
            {
                log.State = EntityState.Detached;
            }

            // 4. Xóa source bằng raw SQL
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanDetails WHERE KhPlanDetailId = {0}",
                sourceDetailId);

            // 5. RENUMBER LineNo toàn phiếu (v0.5.2)
            // Đảm bảo LineNo bắt đầu từ 1, tăng dần theo CreatedAt.
            // Dùng [LineNo] bracket + <> thay != để tránh lỗi SQL parser
            await _db.Database.ExecuteSqlRawAsync(@"
                ;WITH cte AS (
                    SELECT KhPlanDetailId,
                           ROW_NUMBER() OVER (ORDER BY CreatedAt, KhPlanDetailId) AS NewLineNo
                    FROM KhPlanDetails
                    WHERE KhPlanId = {0}
                )
                UPDATE d SET d.[LineNo] = cte.NewLineNo
                FROM KhPlanDetails d
                INNER JOIN cte ON d.KhPlanDetailId = cte.KhPlanDetailId
                WHERE d.[LineNo] <> cte.NewLineNo;", srcKhPlanId);

            await tx.CommitAsync();
            return newDetailIds;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<BulkUpdateResult> BulkUpdateStdAsync(
        List<BulkStdRow> rows,
        int changedBy,
        string batchReason)
    {
        var batchId = DateTime.Now.Ticks;
        var result = new BulkUpdateResult { BatchId = batchId };

        foreach (var row in rows)
        {
            var details = await _db.KhPlanDetails
                .Where(x => x.PurchaseOrder == row.PurchaseOrder)
                .ToListAsync();

            if (!details.Any())
            {
                result.NotFoundPOs.Add(row.PurchaseOrder);
                continue;
            }

            foreach (var d in details)
            {
                var oldStd = d.STD.ToString("yyyy-MM-dd");
                d.STD = row.NewStd;
                d.UpdatedAt = DateTime.Now;

                _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
                {
                    KhPlanDetailId = d.KhPlanDetailId,
                    FieldName = "STD",
                    OldValue = oldStd,
                    NewValue = row.NewStd.ToString("yyyy-MM-dd"),
                    ChangedBy = changedBy,
                    ChangedAt = DateTime.Now,
                    ChangeSource = "ExcelImport",
                    ImportBatchId = batchId,
                    Reason = batchReason
                });

                result.UpdatedCount++;
            }
        }

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<List<KhPlanDetailChangeLog>> GetChangeHistoryAsync(int khPlanDetailId, string? fieldName = null)
    {
        var q = _db.KhPlanDetailChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.KhPlanDetailId == khPlanDetailId);
        if (!string.IsNullOrEmpty(fieldName)) q = q.Where(x => x.FieldName == fieldName);
        return await q.OrderByDescending(x => x.ChangedAt).ToListAsync();
    }
}

// ==== DTOs ====
public class KhPlanDetailRow
{
    public int KhPlanDetailId { get; set; }
    public int KhPlanId { get; set; }
    public string PlanNo { get; set; } = "";
    public DateTime PlanDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string CustomerName { get; set; } = "";
    public string PurchaseOrder { get; set; } = "";
    public string? OldPurchaseOrder { get; set; }
    public string PartNo { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "EA";
    public DateTime STD { get; set; }
    public string? Type { get; set; }
    public string? Priority { get; set; }
    public string? Material { get; set; }
    public string? MaterialConfig { get; set; }
    public string? OrderVL { get; set; }
    public string? Location { get; set; }
    public string? PartNotes { get; set; }
    public string Status { get; set; } = "";
    // Lấy từ KhoVatLieus — chỉ để hiển thị, không cho sửa ở KhPlan
    public string? KhoOrderVL { get; set; }
    public string? KhoViTri { get; set; }
}

public class BulkStdRow
{
    public string PurchaseOrder { get; set; } = "";
    public DateTime NewStd { get; set; }
    public string? CurrentStd { get; set; }
    public bool Found { get; set; }
}

public class BulkUpdateResult
{
    public long BatchId { get; set; }
    public int UpdatedCount { get; set; }
    public List<string> NotFoundPOs { get; set; } = new();
}

public class SplitPORow
{
    public string PurchaseOrder { get; set; } = "";
    public decimal Quantity { get; set; }
}