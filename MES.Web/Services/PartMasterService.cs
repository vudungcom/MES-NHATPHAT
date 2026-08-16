using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Quản lý PartMaster + PartMasterAttribute versioning.
///
/// Nguyên tắc:
/// - Mỗi (PartId, AttributeType) có tối đa 1 giá trị IsDefault=true
/// - Khi tạo phiếu KH mới và chọn Part cũ → auto-fill giá trị IsDefault
/// - User có thể chọn giá trị khác từ list, hoặc nhập mới
/// - Thay đổi default cần PIN xác nhận (rule ở phía UI, service chỉ apply)
/// - Snapshot copy vào KhPlanDetail → phiếu đã tạo giữ nguyên giá trị cũ
/// </summary>
public class PartMasterService
{
    private readonly AppDbContext _db;

    public PartMasterService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lấy tất cả attribute của 1 Part, group theo AttributeType.
    /// Dùng để hiển thị dropdown trong form KH.
    /// </summary>
    public async Task<Dictionary<string, List<PartMasterAttribute>>> GetAttributesGroupedAsync(int partId)
    {
        var attrs = await _db.PartMasterAttributes
            .Where(a => a.PartId == partId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync();

        return attrs.GroupBy(a => a.AttributeType)
                    .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Lấy giá trị default của 1 attribute cho 1 Part.
    /// Trả về null nếu chưa có default.
    /// </summary>
    public async Task<string?> GetDefaultValueAsync(int partId, string attributeType)
    {
        var attr = await _db.PartMasterAttributes
            .FirstOrDefaultAsync(a => a.PartId == partId
                                   && a.AttributeType == attributeType
                                   && a.IsDefault
                                   && a.IsActive);
        return attr?.Value;
    }

    /// <summary>
    /// Lấy default cho nhiều Part cùng lúc (query 1 lần thay vì n lần).
    /// Trả về dictionary: (PartId, AttributeType) → value.
    /// </summary>
    public async Task<Dictionary<(int PartId, string AttributeType), string>> GetDefaultsAsync(List<int> partIds)
    {
        var attrs = await _db.PartMasterAttributes
            .Where(a => partIds.Contains(a.PartId) && a.IsDefault && a.IsActive)
            .ToListAsync();

        return attrs.ToDictionary(a => (a.PartId, a.AttributeType), a => a.Value);
    }

    /// <summary>
    /// Thêm giá trị mới cho 1 attribute (không đè default).
    /// Nếu value đã tồn tại → không tạo trùng, trả về attribute cũ.
    /// </summary>
    public async Task<PartMasterAttribute> AddValueAsync(
        int partId,
        string attributeType,
        string value,
        int createdBy)
    {
        var existing = await _db.PartMasterAttributes
            .FirstOrDefaultAsync(a => a.PartId == partId
                                   && a.AttributeType == attributeType
                                   && a.Value == value);
        if (existing != null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _db.SaveChangesAsync();
            }
            return existing;
        }

        var attr = new PartMasterAttribute
        {
            PartId = partId,
            AttributeType = attributeType,
            Value = value,
            IsDefault = false,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now
        };
        _db.PartMasterAttributes.Add(attr);
        await _db.SaveChangesAsync();
        return attr;
    }

    /// <summary>
    /// Đặt 1 giá trị làm default (bỏ default của giá trị cũ).
    /// Nếu value chưa tồn tại → tạo mới rồi set default.
    /// Đây là thao tác NHẠY CẢM - UI phải yêu cầu PIN trước khi gọi.
    /// </summary>
    public async Task SetAsDefaultAsync(
        int partId,
        string attributeType,
        string value,
        int changedBy)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Bỏ default hiện tại của (partId, attributeType)
            var currentDefaults = await _db.PartMasterAttributes
                .Where(a => a.PartId == partId
                         && a.AttributeType == attributeType
                         && a.IsDefault)
                .ToListAsync();
            foreach (var d in currentDefaults)
            {
                d.IsDefault = false;
            }

            // Tìm value hoặc tạo mới
            var target = await _db.PartMasterAttributes
                .FirstOrDefaultAsync(a => a.PartId == partId
                                       && a.AttributeType == attributeType
                                       && a.Value == value);
            if (target == null)
            {
                target = new PartMasterAttribute
                {
                    PartId = partId,
                    AttributeType = attributeType,
                    Value = value,
                    IsActive = true,
                    CreatedBy = changedBy,
                    CreatedAt = DateTime.Now
                };
                _db.PartMasterAttributes.Add(target);
            }
            else if (!target.IsActive)
            {
                target.IsActive = true;
            }

            target.IsDefault = true;
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
    /// Tạo PartMaster mới + seed các attribute default ban đầu.
    /// Dùng khi user nhập Part No mới chưa có trong PartMaster.
    /// </summary>
    public async Task<PartMaster> CreatePartMasterAsync(
        string partNo,
        int? customerId,
        Dictionary<string, string> initialAttributes,
        int createdBy)
    {
        var pm = new PartMaster
        {
            PartNo = partNo.Trim(),
            CustomerId = customerId,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _db.PartMasters.Add(pm);
        await _db.SaveChangesAsync();

        foreach (var kv in initialAttributes)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            _db.PartMasterAttributes.Add(new PartMasterAttribute
            {
                PartId = pm.PartId,
                AttributeType = kv.Key,
                Value = kv.Value.Trim(),
                IsDefault = true,
                IsActive = true,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now
            });
        }
        if (initialAttributes.Any(kv => !string.IsNullOrWhiteSpace(kv.Value)))
        {
            await _db.SaveChangesAsync();
        }

        return pm;
    }
}
