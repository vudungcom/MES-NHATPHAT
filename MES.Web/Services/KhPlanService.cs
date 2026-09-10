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

            foreach (var r in newRows)
            {
                var newDetail = new KhPlanDetail
                {
                    KhPlanId = srcKhPlanId,
                    LineNo = ++maxLineNo,
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

            _db.Entry(source).State = EntityState.Detached;
            var localLogs = _db.ChangeTracker.Entries<KhPlanDetailChangeLog>()
                .Where(e => e.Entity.KhPlanDetailId == sourceDetailId)
                .ToList();
            foreach (var log in localLogs)
                log.State = EntityState.Detached;

            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanDetails WHERE KhPlanDetailId = {0}",
                sourceDetailId);

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

    /// <summary>
    /// Xóa 1 KhPlanDetail và toàn bộ dữ liệu liên quan:
    /// snapshot quy trình B→E, WTS logs, change logs, KhoVatLieu.
    /// Nếu KhPlan không còn detail nào → xóa luôn KhPlan.
    /// Bắt buộc xác thực PIN trước khi gọi method này.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteDetailAsync(int khPlanDetailId, int deletedBy)
    {
        var detail = await _db.KhPlanDetails
            .Include(d => d.KhPlan)
            .FirstOrDefaultAsync(d => d.KhPlanDetailId == khPlanDetailId);

        if (detail == null) return (false, "Không tìm thấy phiếu KH.");

        try
        {
            // Xóa snapshot quy trình B→E + Máy loại trừ
            // Xóa timing sub-rows TRƯỚC Machining (FK constraint)
            await _db.Database.ExecuteSqlRawAsync(
                @"DELETE t FROM KhPlanRouteSnapshotMachiningTimings t
                  INNER JOIN KhPlanRouteSnapshotMachining m ON t.MachiningSnapshotId = m.SnapshotId
                  WHERE m.KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotMachining WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotTaro WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotBavia WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotWashing WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotInspection WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotPackaging WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotMachineExclude WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // Xóa WTS production logs
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM WtsProductionLogs WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // Xóa change logs
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanDetailChangeLogs WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // Xóa KhoVatLieu nếu có
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhoVatLieus WHERE PlanDetailId = {0}", khPlanDetailId);

            // Xóa detail
            _db.KhPlanDetails.Remove(detail);
            await _db.SaveChangesAsync();

            // Nếu KhPlan không còn detail nào → xóa luôn KhPlan
            var remaining = await _db.KhPlanDetails
                .CountAsync(d => d.KhPlanId == detail.KhPlanId);
            if (remaining == 0)
            {
                var plan = await _db.KhPlans.FindAsync(detail.KhPlanId);
                if (plan != null)
                {
                    _db.KhPlans.Remove(plan);
                    await _db.SaveChangesAsync();
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<KhPlanDetailChangeLog>> GetChangeHistoryAsync(int khPlanDetailId, string? fieldName = null)
    {
        var q = _db.KhPlanDetailChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.KhPlanDetailId == khPlanDetailId);
        if (!string.IsNullOrEmpty(fieldName)) q = q.Where(x => x.FieldName == fieldName);
        return await q.OrderByDescending(x => x.ChangedAt).ToListAsync();
    }



    /// <summary>
    /// Xóa toàn bộ snapshot quy trình B→E cũ của 1 KhPlanDetail,
    /// sau đó copy lại từ Part Master hiện tại.
    /// Ghi log vào KhPlanDetailChangeLog để truy vết.
    /// </summary>
    public async Task RefreshRouteSnapshotAsync(int khPlanDetailId, int partId, int userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Bắt buộc nhập lý do cập nhật quy trình");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. Đếm snapshot cũ để ghi log
            var oldCountMachining  = await _db.KhPlanRouteSnapshotMachining .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var oldCountTaro       = await _db.KhPlanRouteSnapshotTaro       .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var oldCountBavia      = await _db.KhPlanRouteSnapshotBavia      .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var oldCountWashing    = await _db.KhPlanRouteSnapshotWashing    .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var oldCountInspection = await _db.KhPlanRouteSnapshotInspection .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var oldCountPackaging  = await _db.KhPlanRouteSnapshotPackaging  .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);

            // 2. Xóa snapshot cũ bằng raw SQL (nhanh, không cần load vào RAM)
            // Xóa timing sub-rows TRƯỚC khi xóa machining rows (FK constraint)
            await _db.Database.ExecuteSqlRawAsync(
                @"DELETE t FROM KhPlanRouteSnapshotMachiningTimings t
                  INNER JOIN KhPlanRouteSnapshotMachining m ON t.MachiningSnapshotId = m.SnapshotId
                  WHERE m.KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotMachining  WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotTaro       WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotBavia      WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotWashing    WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotInspection WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotPackaging  WHERE KhPlanDetailId = {0}", khPlanDetailId);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotMachineExclude WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // 3. Copy lại từ Part Master hiện tại
            await CopyRouteSnapshotAsync(khPlanDetailId, partId, userId);

            // 4. Đếm snapshot mới
            var newCountMachining  = await _db.KhPlanRouteSnapshotMachining .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var newCountTaro       = await _db.KhPlanRouteSnapshotTaro      .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var newCountBavia      = await _db.KhPlanRouteSnapshotBavia     .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var newCountWashing    = await _db.KhPlanRouteSnapshotWashing   .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var newCountInspection = await _db.KhPlanRouteSnapshotInspection.CountAsync(s => s.KhPlanDetailId == khPlanDetailId);
            var newCountPackaging  = await _db.KhPlanRouteSnapshotPackaging .CountAsync(s => s.KhPlanDetailId == khPlanDetailId);

            // 5. Ghi log
            _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
            {
                KhPlanDetailId = khPlanDetailId,
                FieldName      = "RouteSnapshot",
                OldValue       = $"GC:{oldCountMachining} Taro:{oldCountTaro} Bavia:{oldCountBavia} Rửa:{oldCountWashing} KCS:{oldCountInspection} ĐG:{oldCountPackaging}",
                NewValue       = $"GC:{newCountMachining} Taro:{newCountTaro} Bavia:{newCountBavia} Rửa:{newCountWashing} KCS:{newCountInspection} ĐG:{newCountPackaging}",
                ChangedBy      = userId,
                ChangedAt      = DateTime.Now,
                ChangeSource   = "Manual",
                Reason         = reason
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    /// <summary>
    /// Lấy tóm tắt số bước snapshot cho nhiều KhPlanDetail cùng lúc (batch).
    /// Trả về dict: KhPlanDetailId → SnapshotSummary
    /// Dùng cho trang Index để hiển thị badge GC/HTSP/KCS/ĐG mà không N+1 query.
    /// </summary>
    public async Task<Dictionary<int, SnapshotSummary>> GetSnapshotSummaryAsync(List<int> detailIds)
    {
        if (!detailIds.Any()) return new();

        var machining  = await _db.KhPlanRouteSnapshotMachining
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var taro       = await _db.KhPlanRouteSnapshotTaro
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var bavia      = await _db.KhPlanRouteSnapshotBavia
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var washing    = await _db.KhPlanRouteSnapshotWashing
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var inspection = await _db.KhPlanRouteSnapshotInspection
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var packaging  = await _db.KhPlanRouteSnapshotPackaging
            .Where(s => detailIds.Contains(s.KhPlanDetailId))
            .GroupBy(s => s.KhPlanDetailId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        // Gộp về dict theo detailId
        var result = new Dictionary<int, SnapshotSummary>();
        foreach (var id in detailIds)
        {
            result[id] = new SnapshotSummary
            {
                GC   = machining .FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                Taro = taro      .FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                Bavia= bavia     .FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                Rua  = washing   .FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                KCS  = inspection.FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                DG   = packaging .FirstOrDefault(x => x.Id == id)?.Count ?? 0,
            };
        }
        return result;
    }

    /// <summary>
    /// Lấy toàn bộ snapshot quy trình B→E của 1 KhPlanDetail.
    /// Trả về null nếu chưa có snapshot (phiếu cũ tạo trước khi có tính năng này).
    /// </summary>
    public async Task<RouteSnapshotData> GetRouteSnapshotAsync(int khPlanDetailId)
    {
        var result = new RouteSnapshotData
        {
            Machining      = await _db.KhPlanRouteSnapshotMachining
                .Include(s => s.TimingRows.OrderBy(t => t.DisplayOrder))
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            Taro           = await _db.KhPlanRouteSnapshotTaro
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            Bavia          = await _db.KhPlanRouteSnapshotBavia
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            Washing        = await _db.KhPlanRouteSnapshotWashing
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            Inspection     = await _db.KhPlanRouteSnapshotInspection
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            Packaging      = await _db.KhPlanRouteSnapshotPackaging
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.StepOrder).AsNoTracking().ToListAsync(),
            MachineExclude = await _db.KhPlanRouteSnapshotMachineExclude
                .Where(s => s.KhPlanDetailId == khPlanDetailId)
                .OrderBy(s => s.SoMay).AsNoTracking().ToListAsync(),
        };
        return result;
    }

    /// <summary>
    /// Lấy toàn bộ WTS production logs của 1 KhPlanDetail.
    /// Group theo ProcessGroup + NC để View hiển thị inline.
    /// </summary>
    public async Task<List<WtsProductionLog>> GetWtsLogsAsync(int khPlanDetailId)
    {
        return await _db.WtsProductionLogs
            .Include(x => x.Worker)
            .Where(x => x.KhPlanDetailId == khPlanDetailId)
            .OrderBy(x => x.ProcessGroup)
            .ThenBy(x => x.NC)
            .ThenBy(x => x.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>
    /// Tính tổng SL hoàn thành cho từng NC/WtsCode của 1 KhPlanDetail.
    /// Key: "ProcessGroup|NC" hoặc "ProcessGroup|WtsCode"
    /// Value: tổng QtyDone (chỉ tính IsVoided=false)
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetWtsQtySummaryAsync(int khPlanDetailId)
    {
        var logs = await _db.WtsProductionLogs
            .Where(x => x.KhPlanDetailId == khPlanDetailId && !x.IsVoided)
            .ToListAsync();

        return logs
            .GroupBy(x => x.NC != null
                ? $"{x.ProcessGroup}|{x.NC}"
                : $"{x.ProcessGroup}|{x.WtsCode}")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyDone));
    }
    /// <summary>
    /// Copy toàn bộ quy trình B→E từ PartMaster vào snapshot của KhPlanDetail.
    /// Chỉ copy các step IsActive = true.
    /// Gọi SAU KHI KhPlanDetail đã được lưu vào DB (có KhPlanDetailId).
    /// Nếu Part mới (chưa có steps) → không có gì để copy, bỏ qua.
    /// </summary>
    public async Task CopyRouteSnapshotAsync(int khPlanDetailId, int partId, int snapshotBy)
    {
        var now = DateTime.Now;

        // ── B. Machining ────────────────────────────────────────
        // Load cả Timings (máy đồng dạng sub-rows) cùng lúc
        var machining = await _db.PartMachiningSteps
            .Include(s => s.Timings.Where(t => t.IsActive))
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in machining)
        {
            var snapRow = new KhPlanRouteSnapshotMachining
            {
                KhPlanDetailId     = khPlanDetailId,
                SourcePartId       = partId,
                SnapshotAt         = now,
                SnapshotBy         = snapshotBy,
                StepOrder          = s.StepOrder,
                NC                 = s.NC,
                Drawing            = s.Drawing,
                MachineRegistered  = s.MachineRegistered,
                FixtureType        = s.FixtureType,
                ToolType           = s.ToolType,
                TimingMachine      = s.TimingMachine,
                IsBackup           = s.IsBackup,
                ParentNC           = s.ParentNC,
                SetupTime          = s.SetupTime,
                MachiningTime      = s.MachiningTime,
                InspectionTime     = s.InspectionTime,
                PreparationTime    = s.PreparationTime,
                TrialRunTime       = s.TrialRunTime,
            };
            _db.KhPlanRouteSnapshotMachining.Add(snapRow);

            // Flush để snapRow có SnapshotId trước khi tạo timing children
            await _db.SaveChangesAsync();

            // Copy timing sub-rows (máy đồng dạng)
            foreach (var t in s.Timings.OrderBy(t => t.DisplayOrder))
            {
                _db.KhPlanRouteSnapshotMachiningTimings.Add(new KhPlanRouteSnapshotMachiningTiming
                {
                    MachiningSnapshotId = snapRow.SnapshotId,
                    KhPlanDetailId      = khPlanDetailId,
                    DisplayOrder        = t.DisplayOrder,
                    SoMay               = t.SoMay,
                    FixtureType         = t.FixtureType,
                    ToolType            = t.ToolType,
                    SetupTime           = t.SetupTime,
                    MachiningTime       = t.MachiningTime,
                    InspectionTime      = t.InspectionTime,
                    PreparationTime     = t.PreparationTime,
                    TrialRunTime        = t.TrialRunTime,
                });
            }
        }

        // ── C.1 Taro ────────────────────────────────────────────
        var taro = await _db.PartTaroSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in taro)
        {
            _db.KhPlanRouteSnapshotTaro.Add(new KhPlanRouteSnapshotTaro
            {
                KhPlanDetailId = khPlanDetailId,
                SourcePartId   = partId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                StepOrder      = s.StepOrder,
                NC             = s.NC,
                WtsTaskCode    = s.WtsTaskCode,   // ← thêm dòng này
                StepName       = s.StepName,
                StandardTime   = s.StandardTime,
                IsBackup       = s.IsBackup,
                ParentNC       = s.ParentNC,
            });
        }

        // ── C.2 Bavia ───────────────────────────────────────────
        var bavia = await _db.PartBaviaSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in bavia)
        {
            _db.KhPlanRouteSnapshotBavia.Add(new KhPlanRouteSnapshotBavia
            {
                KhPlanDetailId = khPlanDetailId,
                SourcePartId   = partId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                StepOrder      = s.StepOrder,
                NC             = s.NC,
                StepName       = s.StepName,
                StandardTime   = s.StandardTime,
                IsBackup       = s.IsBackup,
                ParentNC       = s.ParentNC,
            });
        }

        // ── C.3 Rửa ─────────────────────────────────────────────
        var washing = await _db.PartWashingSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in washing)
        {
            _db.KhPlanRouteSnapshotWashing.Add(new KhPlanRouteSnapshotWashing
            {
                KhPlanDetailId = khPlanDetailId,
                SourcePartId   = partId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                StepOrder      = s.StepOrder,
                NC             = s.NC,
                StepName       = s.StepName,
                StandardTime   = s.StandardTime,
                IsBackup       = s.IsBackup,
                ParentNC       = s.ParentNC,
            });
        }

        // ── D. Inspection ────────────────────────────────────────
        var inspection = await _db.PartInspectionSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in inspection)
        {
            _db.KhPlanRouteSnapshotInspection.Add(new KhPlanRouteSnapshotInspection
            {
                KhPlanDetailId = khPlanDetailId,
                SourcePartId   = partId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                StepOrder      = s.StepOrder,
                NC             = s.NC,
                StepName       = s.StepName,
                StandardTime   = s.StandardTime,
                IsBackup       = s.IsBackup,
                ParentNC       = s.ParentNC,
            });
        }

        // ── E. Packaging ─────────────────────────────────────────
        var packaging = await _db.PartPackagingSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in packaging)
        {
            _db.KhPlanRouteSnapshotPackaging.Add(new KhPlanRouteSnapshotPackaging
            {
                KhPlanDetailId = khPlanDetailId,
                SourcePartId   = partId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                StepOrder      = s.StepOrder,
                NC             = s.NC,
                StepName       = s.StepName,
                StandardTime   = s.StandardTime,
                IsBackup       = s.IsBackup,
                ParentNC       = s.ParentNC,
            });
        }

        // ── Global: Máy loại trừ ────────────────────────────────
        // Snapshot global setting tại thời điểm tạo phiếu
        // Không per-part nên không lọc theo partId
        var machineExcludes = await _db.MachineExcludeSettings
            .AsNoTracking()
            .ToListAsync();

        foreach (var m in machineExcludes)
        {
            _db.KhPlanRouteSnapshotMachineExclude.Add(new KhPlanRouteSnapshotMachineExclude
            {
                KhPlanDetailId = khPlanDetailId,
                SnapshotAt     = now,
                SnapshotBy     = snapshotBy,
                SoMay          = m.SoMay,
            });
        }

        // Một lần SaveChanges duy nhất cho toàn bộ 7 bảng
        await _db.SaveChangesAsync();
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

// ==== Route Snapshot DTOs ====
public class RouteSnapshotData
{
    public List<KhPlanRouteSnapshotMachining>      Machining      { get; set; } = new();
    public List<KhPlanRouteSnapshotTaro>            Taro           { get; set; } = new();
    public List<KhPlanRouteSnapshotBavia>           Bavia          { get; set; } = new();
    public List<KhPlanRouteSnapshotWashing>         Washing        { get; set; } = new();
    public List<KhPlanRouteSnapshotInspection>      Inspection     { get; set; } = new();
    public List<KhPlanRouteSnapshotPackaging>       Packaging      { get; set; } = new();
    /// <summary>Danh sách máy loại trừ tại thời điểm tạo phiếu (global setting)</summary>
    public List<KhPlanRouteSnapshotMachineExclude>  MachineExclude { get; set; } = new();

    public bool HasAny => Machining.Count > 0 || Taro.Count > 0 || Bavia.Count > 0
                       || Washing.Count > 0 || Inspection.Count > 0 || Packaging.Count > 0;
}

public class SnapshotSummary
{
    public int GC   { get; set; } // Machining
    public int Taro { get; set; }
    public int Bavia{ get; set; }
    public int Rua  { get; set; } // Washing
    public int KCS  { get; set; } // Inspection
    public int DG   { get; set; } // Packaging

    // HTSP = Taro + Bavia + Rửa
    public int HTSP => Taro + Bavia + Rua;
    public bool HasAny => GC + HTSP + KCS + DG > 0;
}