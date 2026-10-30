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
        // [D1-PLANNO 2026-10-28] Lấy số lớn nhất + 1 (trước: đếm + 1 → xoá 1 phiếu giữa ngày thì sinh lại số đã có, PlanNo unique → không tạo được phiếu nào nữa trong ngày)
        var nos = await _db.KhPlans.Where(x => x.PlanNo.StartsWith(prefix)).Select(x => x.PlanNo).ToListAsync();
        var max = nos.Select(n => int.TryParse(n.Substring(prefix.Length), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{(max + 1):D4}";
    }

    /// <summary>[RS-E16 2026-10-29] Tên khoá chung cho mọi chỗ cấp số Kế hoạch / thêm số PO mới (tạo KH, tách PO).</summary>
    public const string KhNumberLock = "MES_KH_NUMBERS";

    public async Task<int> CreateAsync(KhPlan plan, int userId)
    {
        // [RS-E16 2026-10-29] Cấp PlanNo (max + 1) và kiểm trùng số PO phải nằm DƯỚI KHOÁ: trước đây đọc ngoài khoá →
        //   2 người tạo KH cùng lúc trong ngày ra cùng PlanNo (unique chặn, 1 người lỗi phải làm lại), 2 người gõ cùng số PO
        //   cùng lúc thì lọt cả 2 ([B5-PODUP]). Khoá sp_getapplock LockOwner = Transaction, nhả khi giao dịch của trang
        //   (Create_Khplan: tạo phiếu + chép quy trình) Commit / Rollback. Không có giao dịch ngoài thì tự mở.
        var ownTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            if (!await PoGuard.TryLockInTransactionAsync(_db, KhNumberLock))
                throw new InvalidOperationException("Đang có người khác tạo Kế hoạch / tách PO — chờ vài giây rồi bấm lưu lại.");
            var id = await CreateCoreAsync(plan, userId);
            if (ownTx != null) await ownTx.CommitAsync();
            return id;
        }
        catch
        {
            if (ownTx != null) await ownTx.RollbackAsync();
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    private async Task<int> CreateCoreAsync(KhPlan plan, int userId)
    {
        // [B5-PODUP 2026-10-28] Số PO là khoá tra cứu của WTS / Kho / Tách PO → không cho trùng (trong phiếu và với PO đã có).
        //   Trước: tạo được 2 dòng cùng số PO, WTS gõ PO vào dòng tìm thấy đầu → sổ dòng kia thiếu, dòng này dư. (Tách PO đã chặn trùng từ trước.)
        foreach (var d in plan.Details) d.PurchaseOrder = (d.PurchaseOrder ?? "").Trim();
        var dupIn = plan.Details.GroupBy(d => d.PurchaseOrder, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupIn.Any())
            throw new InvalidOperationException($"Số PO bị trùng trong phiếu: {string.Join(", ", dupIn)}");
        var poList = plan.Details.Select(d => d.PurchaseOrder).ToList();
        var dupDb = await _db.KhPlanDetails.AsNoTracking().Where(x => poList.Contains(x.PurchaseOrder)).Select(x => x.PurchaseOrder).Distinct().ToListAsync();
        if (dupDb.Any())
            throw new InvalidOperationException($"Số PO đã có trong hệ thống: {string.Join(", ", dupDb)} — mỗi số PO chỉ 1 dòng.");
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
                join k in _db.KhoVatLieus.Where(x => x.IsActive) on d.KhPlanDetailId equals k.PlanDetailId into kGroup   // [KHO-ACTIVE 2026-10-28] dòng Kho đã tắt không nhân đôi dòng PO
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

        // [B4-TAKE 2026-10-28] trước Take(1000): quá 1000 dòng thì PO cũ biến khỏi Kế hoạch + WTS (sửa / xoá WTS PO đó bỏ qua kiểm).
        //   Nâng trần lên 20.000 dòng (mọi kiểm số lúc lưu WTS nay đọc thẳng DB, không dựa vào danh sách này).
        return await q.Take(20000).ToListAsync();
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
        if (!await PermCheck.CanEditAsync(_db, changedBy, MES.Web.Constants.SystemPermissions.KeHoach))   // [KHE-PERM 2026-10-30] máy chủ kiểm quyền Sửa KE_HOACH (trước: chỉ trang — danh sách KH còn tin claim ADMIN)
            throw new InvalidOperationException(PermCheck.NoEdit("Kế hoạch"));

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
        if (!await PermCheck.CanEditAsync(_db, changedBy, MES.Web.Constants.SystemPermissions.KeHoach))   // [KHE-PERM 2026-10-30] máy chủ kiểm quyền Sửa KE_HOACH (trước: chỉ trang — danh sách KH còn tin claim ADMIN)
            throw new InvalidOperationException(PermCheck.NoEdit("Kế hoạch"));
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

        // [RS-E16 2026-10-29] Chuẩn hoá số PO trước khi kiểm (trước: kiểm chữ gốc, lưu chữ đã Trim; trùng hoa / thường lọt — SQL coi là 1 số)
        foreach (var r in newRows) r.PurchaseOrder = (r.PurchaseOrder ?? "").Trim();
        var duplicates = newRows.GroupBy(r => r.PurchaseOrder, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
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
            // [D3-LOCK 2026-10-28] Khoá PO (chung WTS + Giao nhận + Kho) rồi kiểm lại "PO trắng" — trước: kiểm ngoài khoá,
            //   đúng lúc Kho nhập phôi / ai đó lập phiếu thì vẫn tách được → phôi / phiếu mất chỗ bám.
            var lockSplit = await PoGuard.LockAsync(_db, sourceDetailId);
            if (lockSplit != null) throw new InvalidOperationException(lockSplit);
            // [RS-E16 2026-10-29] Số PO mới: kiểm trùng lại dưới khoá chung với Tạo KH (trước: chỉ kiểm ngoài khoá)
            if (!await PoGuard.TryLockInTransactionAsync(_db, KhNumberLock))
                throw new InvalidOperationException("Đang có người khác tạo Kế hoạch / tách PO — chờ vài giây rồi bấm lại.");
            var newPoList = newRows.Select(r => r.PurchaseOrder).ToList();
            var dupUnder = await _db.KhPlanDetails.AsNoTracking()
                .Where(x => newPoList.Contains(x.PurchaseOrder) && x.KhPlanDetailId != sourceDetailId)
                .Select(x => x.PurchaseOrder).Distinct().ToListAsync();
            if (dupUnder.Any())
                throw new InvalidOperationException($"PO {string.Join(", ", dupUnder)} đã tồn tại trong hệ thống");
            var srcDocs2 = await PoGuard.CountDocsAsync(_db, sourceDetailId);
            if (srcDocs2.HasAny)
                throw new InvalidOperationException($"PO {srcPO} vừa có chứng từ số lượng ({srcDocs2.Text()}) — không tách được.");
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
        if (!await PermCheck.CanEditAsync(_db, changedBy, MES.Web.Constants.SystemPermissions.KeHoach))   // [KHE-PERM 2026-10-30] máy chủ kiểm quyền Sửa KE_HOACH (trước: chỉ trang — danh sách KH còn tin claim ADMIN)
            throw new InvalidOperationException(PermCheck.NoEdit("Kế hoạch"));
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
        if (!await PermCheck.CanEditAsync(_db, deletedBy, MES.Web.Constants.SystemPermissions.KeHoach))   // [KHE-PERM 2026-10-30] máy chủ kiểm quyền Sửa KE_HOACH (trước: chỉ trang — danh sách KH còn tin claim ADMIN)
            return (false, PermCheck.NoEdit("Kế hoạch"));
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
            // [D3-LOCK 2026-10-28] Khoá PO rồi kiểm lại "PO trắng" trong cùng giao dịch (trước: kiểm ngoài khoá)
            var lockDel = await PoGuard.LockAsync(_db, khPlanDetailId);
            if (lockDel != null) { await trx.RollbackAsync(); return (false, lockDel); }
            var docs2 = await PoGuard.CountDocsAsync(_db, khPlanDetailId);
            if (docs2.HasAny) { await trx.RollbackAsync(); return (false, $"PO {detail.PurchaseOrder} vừa có chứng từ số lượng ({docs2.Text()}) — không xoá được."); }
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
        if (!await PermCheck.CanEditAsync(_db, changedBy, MES.Web.Constants.SystemPermissions.KeHoach))   // [KHE-PERM 2026-10-30] máy chủ kiểm quyền Sửa KE_HOACH (trước: chỉ trang — danh sách KH còn tin claim ADMIN)
            return PermCheck.NoEdit("Kế hoạch");
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
        => await RefreshRouteCoreAsync(khPlanDetailId, partId, userId, reason, auto: false);

    // =====================================================================
    // [K6-NOTRUN 2026-10-30] Master list mục 13 — "PO chưa chạy" theo khách: chưa có WTS, kể cả GC đã nhận phôi.
    //   Luật mới của K6 (thay "có bất kỳ WTS / phiếu chưa huỷ là chặn"): làm mới quy trình được khi
    //     (1) không có dòng WTS chưa huỷ (mọi nhóm),
    //     (2) phiếu giao nhận chưa huỷ CHỈ là phiếu Kho → GC Hàng mới, lần nhận chưa huỷ chỉ nhận OK vào ô Hàng mới (không NG, không ô lỗi / sửa),
    //     (3) không có phiếu báo hàng lỗi chưa huỷ,
    //     (4) mọi phiếu Kho → GC đó nhận vào NC vẫn còn là NC chính (không dự phòng) trong quy trình Part Master mới,
    //   rồi sau khi chép lại, sổ không có ô âm mới (K5). Vì phiếu Kho → GC chỉ tham chiếu NC nhận, giữ đủ 4 điều trên thì số không lệch.
    //   null = làm mới được; còn lại = lý do (chữ hiện cho người dùng).
    // =====================================================================
    public async Task<string?> RouteRefreshBlockAsync(int khPlanDetailId, int partId)
    {
        if (await _db.WtsProductionLogs.AnyAsync(w => w.KhPlanDetailId == khPlanDetailId && !w.IsVoided))
            return "PO đã có WTS (đã bắt đầu làm)";
        if (await _db.HandoverNgReports.AnyAsync(b => b.KhPlanDetailId == khPlanDetailId && !b.IsVoided))
            return "PO đã có phiếu báo hàng lỗi";
        var txs = await _db.HandoverTransactions.AsNoTracking()
            .Where(t => t.KhPlanDetailId == khPlanDetailId && !t.IsVoided)
            .Select(t => new { t.HandoverTxId, t.FromGroupCode, t.ToGroupCode, t.Purpose, t.FromCell, t.ToCell, t.ToNC, t.FromNC })
            .ToListAsync();
        var other = txs.FirstOrDefault(t => !(t.FromGroupCode == "KHO" && t.ToGroupCode == "GC" && t.Purpose == HandoverPurpose.New
                                              && string.IsNullOrEmpty(t.FromNC)
                                              && (t.ToCell == null || t.ToCell == HandoverCell.Moi)));
        if (other != null)
            return $"PO đã có phiếu giao nhận #{other.HandoverTxId} ({HandoverLedgerService.GroupLabel(other.FromGroupCode)} → {HandoverLedgerService.GroupLabel(other.ToGroupCode)}) ngoài phiếu Kho → Gia công cấp phôi";
        if (txs.Count == 0) return null;
        var txIds = txs.Select(t => t.HandoverTxId).ToList();
        var badRecv = await _db.HandoverReceives.AsNoTracking()
            .Where(r => txIds.Contains(r.HandoverTxId) && !r.IsVoided && (r.QtyNg > 0 || (r.ToCell != null && r.ToCell != HandoverCell.Moi)))
            .Select(r => r.HandoverTxId).FirstOrDefaultAsync();
        if (badRecv != 0)
            return $"Gia công nhận phiếu #{badRecv} có hàng lỗi / nhận vào ô Hàng lỗi — đã phát sinh chứng từ lỗi";
        // [K6-NOTRUN] so CHÍNH XÁC như sổ (khoá "in:"+ToNC); phiếu không chỉ định NC (mặc định "NC đầu") không phụ thuộc tên NC → bỏ qua ở đây,
        //   phần còn lại (máy loại trừ, NC đầu mới, số chờ từng NC) kiểm bằng sổ sau khi chép lại (RefreshRouteCoreAsync).
        var mainNcs = (await _db.PartMachiningSteps.AsNoTracking()
                .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup).Select(s => s.NC).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        if (mainNcs.Count == 0)
            return "PO đã cấp phôi cho Gia công mà quy trình Part Master mới không còn NC Gia công nào";
        var lost = txs.Where(t => !string.IsNullOrEmpty(t.ToNC) && !mainNcs.Contains(t.ToNC!)).ToList();
        if (lost.Any())
            return $"phiếu Kho → Gia công #{string.Join(", #", lost.Select(t => t.HandoverTxId))} đã cấp phôi vào NC {string.Join(", ", lost.Select(t => t.ToNC ?? "(trống)").Distinct())} — quy trình Part Master mới không còn NC đó";
        return null;
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    /// <summary>
    /// [K6-NOTRUN] Sau khi chép quy trình mới (trong giao dịch): hàng Gia công đã nhận / chờ nhận không được "trôi" sang NC khác.
    ///   - phiếu chỉ định NC (ToNC): NC đó phải còn trong chuỗi mới (đúng tên, không thành máy loại trừ) với số chờ y như trước;
    ///   - tổng Chưa làm của Gia công + hàng chờ nhận giữ nguyên; chuỗi mới phải còn NC đầu không loại trừ để nhận hàng mặc định.
    /// null = giữ nguyên; còn lại = lời báo.
    /// </summary>
    private async Task<string?> GcWaitingMovedAsync(int khPlanDetailId, LedgerRow before)
    {
        var after = await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId);
        if (after == null) return null;
        var gb = before.G("GC"); var ga = after.G("GC");
        if (ga.ChuaLam != gb.ChuaLam || ga.ChoNhan != gb.ChoNhan)
            return $"số Gia công đổi (Chưa làm {gb.ChuaLam:0.##} → {ga.ChuaLam:0.##}, chờ nhận {gb.ChoNhan:0.##} → {ga.ChoNhan:0.##}) — giữ quy trình cũ";
        var pinned = await _db.HandoverTransactions.AsNoTracking()
            .Where(t => t.KhPlanDetailId == khPlanDetailId && !t.IsVoided && t.ToGroupCode == "GC" && t.ToNC != null && t.ToNC != "")
            .Select(t => t.ToNC!).Distinct().ToListAsync();
        foreach (var nc in pinned)
        {
            var sa = ga.Steps.FirstOrDefault(s => s.Pg == "GC" && s.Code == nc);
            var sb = gb.Steps.FirstOrDefault(s => s.Pg == "GC" && s.Code == nc);
            if (sa == null || sa.IsExcluded)
                return $"hàng đã cấp vào NC {nc}; quy trình mới {(sa == null ? "không còn NC đó" : "đổi NC đó sang máy loại trừ")} — hàng sẽ bị dời, giữ quy trình cũ";
            if ((sa.Waiting ?? 0) != (sb?.Waiting ?? 0) || (sa.In ?? 0) != (sb?.In ?? 0))
                return $"số chờ ở NC {nc} đổi ({sb?.Waiting ?? 0:0.##} → {sa.Waiting ?? 0:0.##}) — giữ quy trình cũ";
        }
        if ((gb.ChuaLam > 0 || gb.ChoNhan > 0) && !ga.Steps.Any(s => s.Pg == "GC" && !s.IsExcluded))
            return "quy trình mới không còn NC Gia công thường nào để nhận hàng — giữ quy trình cũ";
        return null;
    }

    /// <summary>[K6-NOTRUN] Kết quả tự cập nhật quy trình 1 PO sau khi lưu Part Master.</summary>
    public sealed record AutoRouteResult(int KhPlanDetailId, string PurchaseOrder, string Status, string? Note)
    {
        public const string Updated = "UPDATED", Same = "SAME", Kept = "KEPT", Error = "ERR";
    }

    /// <summary>
    /// [K6-NOTRUN 2026-10-30] Master list mục 13: lưu Part Master (Lưu &amp; Xác nhận OK, không lỗi) → mọi PO của Part này còn "chưa chạy"
    /// tự lấy quy trình mới nhất. Mỗi PO một giao dịch + khoá PO + K5 như nút "Cập nhật từ Part Master"; quy trình không đổi gì thì bỏ qua
    /// (không ghi log). PO đã chạy giữ quy trình cũ, trả lý do để trang báo (số chuẩn thì dùng "Update TG chuẩn").
    /// </summary>
    public async Task<List<AutoRouteResult>> AutoRefreshRoutesForPartAsync(int partId, int userId, string reason)
    {
        var res = new List<AutoRouteResult>();
        var part = await _db.PartMasters.AsNoTracking().FirstOrDefaultAsync(p => p.PartId == partId);
        if (part == null || !part.IsActive || part.IsObsolete) return res;
        var dets = await _db.KhPlanDetails.AsNoTracking().Where(d => d.PartNo == part.PartNo)
            .Select(d => new { d.KhPlanDetailId, d.PurchaseOrder }).ToListAsync();
        // [K6-NOTRUN] chỉ đẩy quy trình đã CHỐT: mục nào có công đoạn mà chưa Xác nhận OK → không đẩy (vd người sửa mục E bấm OK khi mục B còn nháp)
        var ngSec = new List<string>();
        if (!part.IsMachiningConfirmed && await _db.PartMachiningSteps.AnyAsync(s => s.PartId == partId && s.IsActive)) ngSec.Add("B Gia công");
        if (!part.IsHtspConfirmed && (await _db.PartTaroSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
                                      || await _db.PartBaviaSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
                                      || await _db.PartWashingSteps.AnyAsync(s => s.PartId == partId && s.IsActive))) ngSec.Add("C HTSP");
        if (!part.IsKcsConfirmed && await _db.PartInspectionSteps.AnyAsync(s => s.PartId == partId && s.IsActive)) ngSec.Add("D KCS");
        if (!part.IsPkgConfirmed && await _db.PartPackagingSteps.AnyAsync(s => s.PartId == partId && s.IsActive)) ngSec.Add("E Đóng gói");
        if (ngSec.Any())
        {
            var note = $"Part Master còn mục {string.Join(", ", ngSec)} chưa Xác nhận OK — chưa đẩy quy trình sang PO";
            return dets.Select(d => new AutoRouteResult(d.KhPlanDetailId, d.PurchaseOrder, AutoRouteResult.Kept, note)).ToList();
        }
        foreach (var d in dets.OrderBy(x => x.KhPlanDetailId))
        {
            var why = await RouteRefreshBlockAsync(d.KhPlanDetailId, partId);
            if (why != null) { res.Add(new AutoRouteResult(d.KhPlanDetailId, d.PurchaseOrder, AutoRouteResult.Kept, why)); continue; }
            try
            {
                var changed = await RefreshRouteCoreAsync(d.KhPlanDetailId, partId, userId, reason, auto: true);
                res.Add(new AutoRouteResult(d.KhPlanDetailId, d.PurchaseOrder, changed ? AutoRouteResult.Updated : AutoRouteResult.Same, null));
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("PO ") || ex.Message.StartsWith("Không lưu") || ex.Message.StartsWith("Part "))
            {
                res.Add(new AutoRouteResult(d.KhPlanDetailId, d.PurchaseOrder, AutoRouteResult.Kept, ex.Message));   // luật chặn (K6 / K5 / RS-E15 / khoá PO)
            }
            catch (Exception ex)
            {
                // [K6-NOTRUN] lỗi khác (SQL / deadlock …): PO này giữ nguyên (giao dịch đã rollback), chạy tiếp PO sau
                res.Add(new AutoRouteResult(d.KhPlanDetailId, d.PurchaseOrder, AutoRouteResult.Error, ex.GetBaseException().Message));
            }
            finally { _db.ChangeTracker.Clear(); }
        }
        return res;
    }

    /// <summary>[K6-NOTRUN] Chữ ký quy trình (snapshot) của 1 PO để biết làm mới có đổi gì không: mọi cột trừ khoá / giờ chụp / người chụp.</summary>
    private async Task<string> RouteSignatureAsync(int khPlanDetailId)
    {
        static string Row(object o)
        {
            var ps = o.GetType().GetProperties()
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0
                         && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))
                         && p.Name is not ("SnapshotId" or "TimingSnapshotId" or "MachiningSnapshotId" or "KhPlanDetailId" or "Id"
                                           or "SnapshotAt" or "SnapshotBy" or "SnapshotRowId" or "SyncedAt" or "SyncedBy" or "SyncBatchId" or "SyncSource"
                                           or "ConfirmedAt" or "CreatedAt" or "UpdatedAt"))
                .OrderBy(p => p.Name);
            return string.Join("|", ps.Select(p => p.Name + "=" + Convert.ToString(p.GetValue(o), System.Globalization.CultureInfo.InvariantCulture)));
        }
        var sb = new System.Text.StringBuilder();
        var m  = await _db.KhPlanRouteSnapshotMachining.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var mt = await _db.KhPlanRouteSnapshotMachiningTimings.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var ta = await _db.KhPlanRouteSnapshotTaro.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var bv = await _db.KhPlanRouteSnapshotBavia.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var wa = await _db.KhPlanRouteSnapshotWashing.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var ins = await _db.KhPlanRouteSnapshotInspection.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var pk = await _db.KhPlanRouteSnapshotPackaging.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var ex = await _db.KhPlanRouteSnapshotMachineExclude.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        var cf = await _db.KhPlanRouteSnapshotStdConfirms.AsNoTracking().Where(s => s.KhPlanDetailId == khPlanDetailId).ToListAsync();
        // dòng snapshot → chữ ký của dòng (thay khoá SnapshotRowId của xác nhận bằng nội dung dòng nó trỏ tới)
        var rowSig = new Dictionary<(string, long), string>();
        void Tab(string name, IEnumerable<object> rows, Func<object, long> key)
        {
            var list = rows.Select(r => (Sig: Row(r), Key: key(r))).OrderBy(x => x.Sig, StringComparer.Ordinal).ToList();
            foreach (var x in list) { rowSig[(name, x.Key)] = x.Sig; sb.Append(name).Append('#').Append(x.Sig).Append('\n'); }
        }
        Tab(StdTrack.Table.Machining, m, o => ((KhPlanRouteSnapshotMachining)o).SnapshotId);
        var mSig = m.ToDictionary(x => x.SnapshotId, x => Row(x));
        Tab(StdTrack.Table.MachiningTiming, mt.Select(t => (object)new { Parent = mSig.GetValueOrDefault(t.MachiningSnapshotId), T = Row(t), t.TimingSnapshotId }),
            o => (long)o.GetType().GetProperty("TimingSnapshotId")!.GetValue(o)!);
        Tab(StdTrack.Table.Taro, ta, o => ((KhPlanRouteSnapshotTaro)o).SnapshotId);
        Tab(StdTrack.Table.Bavia, bv, o => ((KhPlanRouteSnapshotBavia)o).SnapshotId);
        Tab(StdTrack.Table.Washing, wa, o => ((KhPlanRouteSnapshotWashing)o).SnapshotId);
        Tab(StdTrack.Table.Inspection, ins, o => ((KhPlanRouteSnapshotInspection)o).SnapshotId);
        Tab(StdTrack.Table.Packaging, pk, o => ((KhPlanRouteSnapshotPackaging)o).SnapshotId);
        foreach (var x in ex.Select(Row).OrderBy(s => s, StringComparer.Ordinal)) sb.Append("EX#").Append(x).Append('\n');
        foreach (var x in cf.Select(c => $"{c.SnapshotTable}|{rowSig.GetValueOrDefault((c.SnapshotTable, c.SnapshotRowId))}|{c.FieldName}|{c.IsConfirmed}|{c.ConfirmedBy}")
                            .OrderBy(s => s, StringComparer.Ordinal))
            sb.Append("CF#").Append(x).Append('\n');
        return sb.ToString();
    }

    /// <summary>[K6-NOTRUN] Làm mới quy trình 1 PO. auto = gọi từ lưu Part Master: quy trình không đổi gì thì rollback, trả false (không ghi log).</summary>
    private async Task<bool> RefreshRouteCoreAsync(int khPlanDetailId, int partId, int userId, string reason, bool auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Bắt buộc nhập lý do cập nhật quy trình");

        // [K6-NOTRUN 2026-10-30] K6 theo định nghĩa "PO chưa chạy" của khách (xem RouteRefreshBlockAsync).
        //   Trước [KH-GUARD]: có bất kỳ WTS / phiếu chưa huỷ là chặn — kể cả phiếu Kho → GC cấp phôi (khách: vẫn là chưa chạy).
        var why0 = await RouteRefreshBlockAsync(khPlanDetailId, partId);
        if (why0 != null)
            throw new InvalidOperationException(
                $"PO đã chạy ({why0}) — không cập nhật quy trình được (K6): số đã ghi theo NC / mã CV hiện tại sẽ lệch. "
                + "Quy trình mới chỉ áp cho PO chưa chạy / PO tạo sau; số chuẩn thì dùng \"Update TG chuẩn\" ở Part Master.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // [D3-LOCK 2026-10-28] K6 kiểm lại dưới khoá PO (trước: kiểm ngoài khoá, WTS / phiếu chen vào giữa vẫn làm mới quy trình được)
            var lockRf = await PoGuard.LockAsync(_db, khPlanDetailId);
            if (lockRf != null) throw new InvalidOperationException(lockRf);
            var whyRf = await RouteRefreshBlockAsync(khPlanDetailId, partId);
            if (whyRf != null)
                throw new InvalidOperationException($"PO vừa có số chạy ({whyRf}) — không cập nhật quy trình được (K6).");
            // [K6-NOTRUN] sổ trước khi làm mới — chỉ cần khi PO đã có phiếu Kho → GC (PO trắng không có số nào để lệch)
            var hasTx = await _db.HandoverTransactions.AnyAsync(t => t.KhPlanDetailId == khPlanDetailId && !t.IsVoided);
            var ledgerBefore = hasTx ? await new HandoverLedgerService(_db).GetLedgerForDetailAsync(khPlanDetailId) : null;
            var sigBefore = auto ? await RouteSignatureAsync(khPlanDetailId) : null;
            // [RS-E15 2026-10-29] Part không còn công đoạn nào (B→E đều trống / đã xoá) → không làm mới:
            //   trước đây vẫn xoá hết snapshot cũ rồi chép "rỗng" và commit → PO mất hẳn quy trình (log GC:0 …), WTS không có NC / mã CV để ghi.
            //   Kiểm TRƯỚC khi xoá, cùng điều kiện CopyRouteSnapshotAsync chép (bước đang dùng của Part).
            if (!await PartHasRouteAsync(partId))
                throw new InvalidOperationException("Part trong Part Master chưa có công đoạn nào (Gia công / HTSP / KCS / Đóng gói đều trống) — không cập nhật quy trình được: làm mới lúc này PO sẽ mất hẳn quy trình. Nhập quy trình ở Part Master trước rồi cập nhật lại.");
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

            // [K6-NOTRUN] tự cập nhật: quy trình không đổi gì → bỏ (không ghi log, không đổi id snapshot)
            if (auto && sigBefore == await RouteSignatureAsync(khPlanDetailId))
            {
                await tx.RollbackAsync();
                return false;
            }
            // [K6-NOTRUN] K5: phiếu Kho → GC đã cấp phôi vào NC — sau khi chép lại sổ không được có ô âm mới
            if (ledgerBefore != null)
            {
                var k5 = await PoGuard.CheckNoNewNegativeAsync(_db, khPlanDetailId, ledgerBefore);
                if (k5 != null) throw new InvalidOperationException(k5);
                var moved = await GcWaitingMovedAsync(khPlanDetailId, ledgerBefore);
                if (moved != null) throw new InvalidOperationException("PO đã cấp phôi cho Gia công — " + moved);
            }

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
                ChangeSource   = "Manual",   // [K6-NOTRUN] tự cập nhật ghi rõ ở lý do (giữ giá trị cột như cũ)
                Reason         = Cut(auto ? "Tự cập nhật theo Part Master (PO chưa chạy) — " + reason : reason, 500)
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    /// <summary>[RS-E15 2026-10-29] Part có ít nhất 1 công đoạn đang dùng (cùng điều kiện CopyRouteSnapshotAsync: PartId + IsActive).</summary>
    private async Task<bool> PartHasRouteAsync(int partId)
        => await _db.PartMachiningSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
        || await _db.PartTaroSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
        || await _db.PartBaviaSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
        || await _db.PartWashingSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
        || await _db.PartInspectionSteps.AnyAsync(s => s.PartId == partId && s.IsActive)
        || await _db.PartPackagingSteps.AnyAsync(s => s.PartId == partId && s.IsActive);

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
