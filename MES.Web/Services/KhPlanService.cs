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
                    // [WORKFLOW] Cờ kiểm soát thứ tự NC từ phiếu KH
                    ApplyWorkflow = p.ApplyWorkflow,
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

        // [KH-GUARD 2026-10-27] Chỉ tách PO "trắng". PO đã có phôi nhận / WTS / phiếu giao nhận (kể cả đã huỷ) thì chứng từ
        //   đang gắn vào PO gốc — tách sẽ làm mất phôi (dòng Kho không có khoá ngoại) hoặc vướng khoá ngoại WTS / phiếu.
        var srcDocs = await PoGuard.CountDocsAsync(_db, sourceDetailId);
        if (srcDocs.HasAny)
            throw new InvalidOperationException(
                $"PO {source.PurchaseOrder} đã có chứng từ số lượng ({srcDocs.Text()}) — không tách được. "
                + "Hàng đã vào sản xuất đi theo PO gốc; muốn chia giao khách thì xử lý ở bước giao thành phẩm.");

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

            // [KH-GUARD 2026-10-27] PO mới mang ĐÚNG quy trình (snapshot) của PO gốc — không chép lại từ Part Master
            //   (snapshot chụp lúc tạo KH, không đọc live). Trước đây PO tách ra không có quy trình.
            foreach (var newId in newDetailIds)
                await CloneRouteSnapshotAsync(sourceDetailId, newId);

            // [KH-GUARD] Dòng Kho của PO gốc (chỉ có thông tin order / vị trí — đã chặn phôi nhận ở trên):
            //   chép thông tin sang từng PO mới, dòng gốc tắt IsActive (không xoá cứng).
            var srcKho = await _db.KhoVatLieus
                .Where(k => k.PlanDetailId == (long?)sourceDetailId && k.IsActive)
                .ToListAsync();
            foreach (var k in srcKho)
            {
                for (int i = 0; i < newDetailIds.Count; i++)
                {
                    _db.KhoVatLieus.Add(new KhoVatLieu
                    {
                        PlanDetailId    = newDetailIds[i],
                        SoPO            = newRows[i].PurchaseOrder.Trim(),
                        PartNo          = k.PartNo,
                        DonViTinh       = k.DonViTinh,
                        SoLuongKeHoach  = (int)newRows[i].Quantity,
                        ThoiHan         = k.ThoiHan,
                        MaVatLieu       = k.MaVatLieu,
                        CauHinhPhoi     = k.CauHinhPhoi,
                        GhiChuPhoi      = k.GhiChuPhoi,
                        NgayNhanPhoi    = k.NgayNhanPhoi,
                        SoLuongThucNhan = null,
                        OrderVatLieu    = k.OrderVatLieu,
                        ViTriDePhoi     = k.ViTriDePhoi,
                        TinhTrangPhoi   = k.TinhTrangPhoi,
                        NgayCapPhoi     = null,
                        SoLuongCap      = null,
                        IsActive        = true,
                        CreatedAt       = DateTime.Now,
                        CreatedBy       = changedBy
                    });
                }
                k.IsActive  = false;
                k.UpdatedAt = DateTime.Now;
                k.UpdatedBy = changedBy;
            }
            await _db.SaveChangesAsync();

            // [KH-GUARD] Xoá snapshot của PO gốc trước khi xoá dòng gốc (khoá ngoại NO ACTION — trước đây làm Tách PO luôn lỗi)
            await DeleteRouteSnapshotRowsAsync(sourceDetailId);

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

        // [KH-GUARD 2026-10-27] PO đã có chứng từ số lượng (phôi nhận / WTS / phiếu giao nhận — kể cả đã huỷ) thì KHÔNG xoá:
        //   trước đây xoá cứng WTS + dòng Kho rồi mới vướng khoá ngoại phiếu giao nhận → PO còn phiếu mà mất phôi + WTS (lệch in/out).
        //   WTS là log chỉ ghi thêm (không xoá). Chỉ PO "trắng" mới xoá được.
        var docs = await PoGuard.CountDocsAsync(_db, khPlanDetailId);
        if (docs.HasAny)
            return (false, $"PO {detail.PurchaseOrder} đã có chứng từ số lượng ({docs.Text()}) — không xoá được để giữ cân số lượng và lịch sử. "
                         + "Nếu PO tạo nhầm: huỷ hết phiếu giao nhận / WTS và đưa phôi nhận về 0 trước (WTS đã huỷ vẫn là lịch sử nên PO vẫn không xoá được — báo quản trị).");

        // [KH-GUARD] Mọi lệnh xoá trong 1 giao dịch: lỗi giữa chừng thì không mất gì
        using var trx = await _db.Database.BeginTransactionAsync();
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
            // [STD-UPDATE] Xóa trạng thái xác nhận snapshot (không có FK — phải xóa tay)
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotStdConfirms WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // [KH-GUARD] Bỏ lệnh xoá WtsProductionLogs: đã chặn ở trên (PO có WTS thì không xoá) — WTS chỉ ghi thêm, không xoá

            // Xóa change logs
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanDetailChangeLogs WHERE KhPlanDetailId = {0}", khPlanDetailId);

            // [KH-GUARD] Dòng Kho (chỉ còn thông tin order / vị trí, chưa có phôi nhận — đã chặn ở trên): tắt IsActive thay vì xoá cứng
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE KhoVatLieus SET IsActive = 0 WHERE PlanDetailId = {0}", khPlanDetailId);

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

            await trx.CommitAsync();   // [KH-GUARD]
            return (true, null);
        }
        catch (Exception ex)
        {
            await trx.RollbackAsync();   // [KH-GUARD] lỗi giữa chừng → trả lại như cũ
            _db.ChangeTracker.Clear();
            return (false, ex.Message);
        }
    }

    // ================================================================
    // [KH-GUARD 2026-10-27] Chép / xoá snapshot quy trình của 1 PO (dùng cho Tách PO)
    // ================================================================

    /// <summary>Chép toàn bộ snapshot B→E + máy loại trừ + trạng thái xác nhận TG chuẩn từ PO nguồn sang PO đích (đã lưu, chưa có snapshot).</summary>
    private async Task CloneRouteSnapshotAsync(int fromId, int toId)
    {
        var mach = await _db.KhPlanRouteSnapshotMachining.Include(s => s.TimingRows)
            .Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var taro = await _db.KhPlanRouteSnapshotTaro.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var bav  = await _db.KhPlanRouteSnapshotBavia.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var wash = await _db.KhPlanRouteSnapshotWashing.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var insp = await _db.KhPlanRouteSnapshotInspection.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var pack = await _db.KhPlanRouteSnapshotPackaging.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var excl = await _db.KhPlanRouteSnapshotMachineExclude.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();
        var conf = await _db.KhPlanRouteSnapshotStdConfirms.Where(s => s.KhPlanDetailId == fromId).AsNoTracking().ToListAsync();

        // Ghép dòng cũ → dòng mới để đổi SnapshotRowId của trạng thái xác nhận
        var pairs = new List<(string Table, long OldId, Func<long> NewId)>();

        foreach (var o in mach)
        {
            var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId;
            n.TimingRows = new List<KhPlanRouteSnapshotMachiningTiming>();
            foreach (var ot in o.TimingRows)
            {
                var nt = CopyScalars(ot); nt.TimingSnapshotId = 0; nt.MachiningSnapshotId = 0; nt.KhPlanDetailId = toId;
                n.TimingRows.Add(nt);
                pairs.Add((StdTrack.Table.MachiningTiming, ot.TimingSnapshotId, () => nt.TimingSnapshotId));
            }
            _db.KhPlanRouteSnapshotMachining.Add(n);
            pairs.Add((StdTrack.Table.Machining, o.SnapshotId, () => n.SnapshotId));
        }
        foreach (var o in taro) { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotTaro.Add(n);       pairs.Add((StdTrack.Table.Taro, o.SnapshotId, () => n.SnapshotId)); }
        foreach (var o in bav)  { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotBavia.Add(n);      pairs.Add((StdTrack.Table.Bavia, o.SnapshotId, () => n.SnapshotId)); }
        foreach (var o in wash) { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotWashing.Add(n);    pairs.Add((StdTrack.Table.Washing, o.SnapshotId, () => n.SnapshotId)); }
        foreach (var o in insp) { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotInspection.Add(n); pairs.Add((StdTrack.Table.Inspection, o.SnapshotId, () => n.SnapshotId)); }
        foreach (var o in pack) { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotPackaging.Add(n);  pairs.Add((StdTrack.Table.Packaging, o.SnapshotId, () => n.SnapshotId)); }
        foreach (var o in excl) { var n = CopyScalars(o); n.SnapshotId = 0; n.KhPlanDetailId = toId; _db.KhPlanRouteSnapshotMachineExclude.Add(n); }
        await _db.SaveChangesAsync();   // có Id mới

        var map = pairs.ToDictionary(x => (x.Table, x.OldId), x => x.NewId());
        foreach (var c in conf)
        {
            if (!map.TryGetValue((c.SnapshotTable, c.SnapshotRowId), out var newRowId)) continue;   // dòng xác nhận không còn dòng snapshot → bỏ
            var n = CopyScalars(c); n.Id = 0; n.KhPlanDetailId = toId; n.SnapshotRowId = newRowId;
            _db.KhPlanRouteSnapshotStdConfirms.Add(n);
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Xoá snapshot B→E + máy loại trừ + trạng thái xác nhận của 1 PO (cùng các lệnh như DeleteDetailAsync / RefreshRouteSnapshotAsync).</summary>
    private async Task DeleteRouteSnapshotRowsAsync(int khPlanDetailId)
    {
        await _db.Database.ExecuteSqlRawAsync(
            @"DELETE t FROM KhPlanRouteSnapshotMachiningTimings t
              INNER JOIN KhPlanRouteSnapshotMachining m ON t.MachiningSnapshotId = m.SnapshotId
              WHERE m.KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotMachining      WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotTaro           WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotBavia          WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotWashing        WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotInspection     WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotPackaging      WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotMachineExclude WHERE KhPlanDetailId = {0}", khPlanDetailId);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM KhPlanRouteSnapshotStdConfirms    WHERE KhPlanDetailId = {0}", khPlanDetailId);
    }

    /// <summary>Bản sao nông: chỉ chép thuộc tính kiểu giá trị / string (bỏ navigation).</summary>
    private static T CopyScalars<T>(T src) where T : class, new()
    {
        var dst = new T();
        foreach (var pi in typeof(T).GetProperties())
        {
            if (!pi.CanRead || !pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
            var t = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (t.IsValueType || t == typeof(string))
                pi.SetValue(dst, pi.GetValue(src));
        }
        return dst;
    }

    // ================================================================
    // [KH-GUARD 2026-10-27] K7 — bật / tắt "Áp dụng Workflow" của phiếu KH
    // ================================================================

    /// <summary>
    /// Bật Workflow (OFF → ON) mà PO nào đang có ô âm (số nhập lúc OFF vượt hàng thật) thì chặn:
    /// bật lên sẽ khoá cứng mọi thao tác của PO đó. Trả null = bật được.
    /// </summary>
    public async Task<string?> CheckWorkflowOnAsync(int khPlanId)
    {
        var ids = await _db.KhPlanDetails.Where(d => d.KhPlanId == khPlanId).Select(d => d.KhPlanDetailId).ToListAsync();
        if (ids.Count == 0) return null;
        var rows = await new HandoverLedgerService(_db).GetLedgerForDetailsAsync(ids);
        var bad = new List<string>();
        foreach (var r in rows.Values)
        {
            var neg = HandoverLedgerService.NegativeCells(r);
            if (neg.Count > 0)
                bad.Add($"PO {r.PurchaseOrder}: " + string.Join("; ", neg.Values.Select(v => $"{v.Text} {v.Value:0.##}")));
        }
        return bad.Count == 0 ? null
            : "Không bật Workflow được (K7) — đang có số âm do nhập lúc tắt Workflow:\n" + string.Join("\n", bad)
              + "\nNên sửa / huỷ các WTS hoặc phiếu gây âm trước rồi mới bật.";
    }

    /// <summary>Đổi ApplyWorkflow của phiếu KH (kiểm K7 khi bật) + ghi lịch sử vào từng dòng PO. Trả null = đã đổi (chưa SaveChanges của trang).</summary>
    public async Task<string?> SetApplyWorkflowAsync(int khPlanId, bool on, int changedBy, string reason)
    {
        var plan = await _db.KhPlans.FirstOrDefaultAsync(p => p.KhPlanId == khPlanId);
        if (plan == null) return "Không tìm thấy phiếu KH.";
        if (plan.ApplyWorkflow == on) return null;
        if (on)
        {
            var err = await CheckWorkflowOnAsync(khPlanId);
            if (err != null) return err;
        }
        var ids = await _db.KhPlanDetails.Where(d => d.KhPlanId == khPlanId).Select(d => d.KhPlanDetailId).ToListAsync();
        foreach (var id in ids)
            _db.KhPlanDetailChangeLogs.Add(new KhPlanDetailChangeLog
            {
                KhPlanDetailId = id,
                FieldName      = "ApplyWorkflow",
                OldValue       = plan.ApplyWorkflow ? "ON" : "OFF",
                NewValue       = on ? "ON" : "OFF",
                ChangedBy      = changedBy,
                ChangedAt      = DateTime.Now,
                ChangeSource   = "Manual",
                Reason         = reason
            });
        plan.ApplyWorkflow = on;
        return null;
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

        // [KH-GUARD 2026-10-27] K6: PO đã chạy (WTS / phiếu giao nhận chưa huỷ) thì không làm mới quy trình —
        //   đổi NC / mã CV giữa chừng làm WTS cũ treo ở NC không còn, Chưa làm theo NC tính sai.
        var docs = await PoGuard.CountDocsAsync(_db, khPlanDetailId);
        if (docs.HasActiveFlow)
            throw new InvalidOperationException(
                $"PO đã chạy ({docs.Text()}) — không cập nhật quy trình được (K6): số đã ghi theo NC / mã CV hiện tại sẽ lệch. "
                + "Quy trình mới chỉ áp cho PO tạo sau. Nếu bắt buộc phải đổi: huỷ hết WTS + phiếu giao nhận của PO trước.");

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
            // [STD-UPDATE] Xóa trạng thái xác nhận snapshot cũ — CopyRouteSnapshotAsync ghi lại theo Part Master
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM KhPlanRouteSnapshotStdConfirms WHERE KhPlanDetailId = {0}", khPlanDetailId);

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
                SourceStepId       = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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
                    SourceStepId        = s.StepId,     // [STD-UPDATE]
                    SourceTimingId      = t.TimingId,   // [STD-UPDATE] nối chắc chắn về dòng máy đồng dạng
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
                SourceStepId   = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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
                SourceStepId   = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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
                SourceStepId   = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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
                SourceStepId   = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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
                SourceStepId   = s.StepId,   // [STD-UPDATE] nối chắc chắn về Part Master
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

        // [STD-UPDATE] Chép trạng thái xác nhận từng ô của Part Master sang snapshot (cùng lúc với số)
        await WriteSnapshotStdConfirmsAsync(khPlanDetailId, partId, StdTrack.Source.Copy, null, snapshotBy);
    }

    // ════════════════════════════════════════════════════════════════════════
    // [STD-UPDATE] Trạng thái xác nhận thời gian chuẩn của snapshot
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Trạng thái xác nhận 1 ô ở Part Master.</summary>
    public sealed record PmStdConfirm(bool IsConfirmed, int? ConfirmedBy, string? ConfirmedByName, DateTime? ConfirmedAt);

    /// <summary>
    /// Đọc trạng thái xác nhận hiện tại của toàn bộ ô thời gian chuẩn của 1 Part.
    /// Gc  : key (StepId, TimingId, FieldName) — TimingId = null cho NC chính.
    ///       Cùng quy tắc với PartMachiningService.GetTimingConfirmsAsync (bản ghi mới nhất).
    /// Cde : key (StepTable, StepId) — cùng quy tắc với PartStepStdTimeConfirmService.GetConfirmsAsync.
    /// Chỉ chứa ô đang xác nhận (bản ghi mới nhất IsConfirmed = true).
    /// </summary>
    public async Task<(Dictionary<(long StepId, long? TimingId, string Field), PmStdConfirm> Gc,
                       Dictionary<(string Table, long StepId), PmStdConfirm> Cde)>
        LoadPmStdConfirmsAsync(int partId)
    {
        var stepIds = await _db.PartMachiningSteps
            .Where(s => s.PartId == partId)
            .Select(s => s.StepId)
            .ToListAsync();

        var gcRows = stepIds.Count == 0
            ? new List<PartMachiningTimingConfirm>()
            : await _db.PartMachiningTimingConfirms
                .Where(x => stepIds.Contains(x.StepId))
                .Include(x => x.ConfirmedByUser)
                .AsNoTracking()
                .ToListAsync();

        var gc = new Dictionary<(long, long?, string), PmStdConfirm>();
        foreach (var g in gcRows.GroupBy(x => (x.StepId, x.TimingId, x.FieldName)))
        {
            var latest = g.OrderByDescending(x => x.ConfirmedAt).ThenByDescending(x => x.Id).First();
            if (latest.IsConfirmed)
                gc[g.Key] = new PmStdConfirm(true, latest.ConfirmedBy, latest.ConfirmedByUser?.FullName, latest.ConfirmedAt);
        }

        var cdeRows = await _db.PartStepStdTimeConfirms
            .Where(x => x.PartId == partId)
            .Include(x => x.ConfirmedByUser)
            .AsNoTracking()
            .ToListAsync();

        var cde = new Dictionary<(string, long), PmStdConfirm>();
        foreach (var g in cdeRows.GroupBy(x => (x.StepTable, x.StepId)))
        {
            var latest = g.OrderByDescending(x => x.ConfirmedAt).ThenByDescending(x => x.Id).First();
            if (latest.IsConfirmed)
                cde[g.Key] = new PmStdConfirm(true, latest.ConfirmedBy, latest.ConfirmedByUser?.FullName, latest.ConfirmedAt);
        }

        return (gc, cde);
    }

    /// <summary>
    /// Ghi lại (xóa + ghi mới) bảng KhPlanRouteSnapshotStdConfirms cho 1 phiếu KH,
    /// theo trạng thái xác nhận HIỆN TẠI của Part Master. Mỗi ô 1 dòng, kể cả ô chưa xác nhận.
    /// Gọi ngay sau khi snapshot được chép từ Part Master (số và xác nhận cùng thời điểm).
    /// Dòng snapshot không có SourceStepId (snapshot cũ chưa nối) → ghi IsConfirmed = false.
    /// </summary>
    public async Task WriteSnapshotStdConfirmsAsync(int khPlanDetailId, int partId, string syncSource, long? syncBatchId, int? userId)
    {
        var now = DateTime.Now;

        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM KhPlanRouteSnapshotStdConfirms WHERE KhPlanDetailId = {0}", khPlanDetailId);

        var (pmGc, pmCde) = await LoadPmStdConfirmsAsync(partId);
        var rows = new List<KhPlanRouteSnapshotStdConfirm>();

        KhPlanRouteSnapshotStdConfirm Make(string table, long rowId, string field, PmStdConfirm? pm, bool hasSource) => new()
        {
            KhPlanDetailId  = khPlanDetailId,
            SnapshotTable   = table,
            SnapshotRowId   = rowId,
            FieldName       = field,
            IsConfirmed     = pm?.IsConfirmed ?? false,
            ConfirmedBy     = pm?.ConfirmedBy,
            ConfirmedByName = pm?.ConfirmedByName,
            ConfirmedAt     = pm?.ConfirmedAt,
            SyncSource      = syncSource,
            SyncBatchId     = syncBatchId,
            SyncedAt        = now,
            SyncedBy        = userId,
            Note            = hasSource ? null : "Snapshot chưa nối được dòng Part Master nguồn",
        };

        // ── B. NC chính + máy đồng dạng ──
        var mach = await _db.KhPlanRouteSnapshotMachining
            .Include(s => s.TimingRows)
            .Where(s => s.KhPlanDetailId == khPlanDetailId)
            .AsNoTracking()
            .ToListAsync();

        foreach (var s in mach)
        {
            foreach (var f in StdTrack.Field.Gc)
            {
                PmStdConfirm? pm = null;
                if (s.SourceStepId.HasValue) pmGc.TryGetValue((s.SourceStepId.Value, null, f), out pm);
                rows.Add(Make(StdTrack.Table.Machining, s.SnapshotId, f, pm, s.SourceStepId.HasValue));
            }
            foreach (var t in s.TimingRows)
            {
                var ok = t.SourceStepId.HasValue && t.SourceTimingId.HasValue;
                foreach (var f in StdTrack.Field.Gc)
                {
                    PmStdConfirm? pm = null;
                    if (ok) pmGc.TryGetValue((t.SourceStepId!.Value, t.SourceTimingId, f), out pm);
                    rows.Add(Make(StdTrack.Table.MachiningTiming, t.TimingSnapshotId, f, pm, ok));
                }
            }
        }

        // ── C/D/E (StandardTime) ──
        void AddCde(string table, long snapshotId, long? sourceStepId)
        {
            PmStdConfirm? pm = null;
            if (sourceStepId.HasValue) pmCde.TryGetValue((table, sourceStepId.Value), out pm);
            rows.Add(Make(table, snapshotId, StdTrack.Field.Standard, pm, sourceStepId.HasValue));
        }

        foreach (var s in await _db.KhPlanRouteSnapshotTaro.Where(x => x.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync())
            AddCde(StdTrack.Table.Taro, s.SnapshotId, s.SourceStepId);
        foreach (var s in await _db.KhPlanRouteSnapshotBavia.Where(x => x.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync())
            AddCde(StdTrack.Table.Bavia, s.SnapshotId, s.SourceStepId);
        foreach (var s in await _db.KhPlanRouteSnapshotWashing.Where(x => x.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync())
            AddCde(StdTrack.Table.Washing, s.SnapshotId, s.SourceStepId);
        foreach (var s in await _db.KhPlanRouteSnapshotInspection.Where(x => x.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync())
            AddCde(StdTrack.Table.Inspection, s.SnapshotId, s.SourceStepId);
        foreach (var s in await _db.KhPlanRouteSnapshotPackaging.Where(x => x.KhPlanDetailId == khPlanDetailId).AsNoTracking().ToListAsync())
            AddCde(StdTrack.Table.Packaging, s.SnapshotId, s.SourceStepId);

        if (rows.Count > 0)
        {
            _db.KhPlanRouteSnapshotStdConfirms.AddRange(rows);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Đọc trạng thái xác nhận snapshot của 1 phiếu KH (dùng cho trang WTS).
    /// Key = (SnapshotTable, SnapshotRowId, FieldName).
    /// </summary>
    public async Task<Dictionary<(string Table, long RowId, string Field), KhPlanRouteSnapshotStdConfirm>>
        GetSnapshotStdConfirmsAsync(int khPlanDetailId)
    {
        var list = await _db.KhPlanRouteSnapshotStdConfirms
            .Where(x => x.KhPlanDetailId == khPlanDetailId)
            .AsNoTracking()
            .ToListAsync();
        return list.ToDictionary(x => (x.SnapshotTable, x.SnapshotRowId, x.FieldName));
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
    // [WORKFLOW] true = kiểm soát thứ tự NC; false = thả tự do
    public bool ApplyWorkflow { get; set; } = true;
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