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
///
/// LƯU Ý về permission:
/// - `UpdatePartNameAsync` CÓ enforce permission vùng A (Lượt 6D thêm mới)
/// - `SetAsDefaultAsync` KHÔNG enforce permission — để module KH cũ vẫn dùng được
///   Detail.razor của Part Master phải tự chặn ở UI trước khi gọi
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
    /// KHÔNG check permission ở service — để module KH cũ vẫn dùng được.
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
    ///
    /// Lượt 6D: thêm optional param partName + check PartNo unique.
    /// </summary>
    public async Task<PartMaster> CreatePartMasterAsync(
        string partNo,
        int? customerId,
        Dictionary<string, string> initialAttributes,
        int createdBy,
        string? partName = null)
    {
        var trimmedPartNo = partNo.Trim();
        if (string.IsNullOrEmpty(trimmedPartNo))
            throw new ArgumentException("Part No không được để trống", nameof(partNo));

        // Lượt 6D: check unique để trả về error thân thiện thay vì lỗi SQL raw
        var exists = await _db.PartMasters.AnyAsync(p => p.PartNo == trimmedPartNo);
        if (exists)
            throw new InvalidOperationException($"Part No '{trimmedPartNo}' đã tồn tại trong hệ thống");

        var pm = new PartMaster
        {
            PartNo = trimmedPartNo,
            PartName = string.IsNullOrWhiteSpace(partName) ? null : partName.Trim(),
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

    /// <summary>
    /// Lượt 6D: Cập nhật Tên chi tiết (PartMaster.PartName) — thuộc vùng A của Part Master.
    /// Enforce permission: chỉ ADMIN hoặc PLANNING Leader.
    /// </summary>
    public async Task UpdatePartNameAsync(int partId, string? newPartName, int userId)
    {
        var user = await _db.Users
            .Include(u => u.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) throw new UnauthorizedAccessException("User không tồn tại");
        if (!user.IsActive) throw new UnauthorizedAccessException("User đã bị khóa");

        var groupCode = user.Group?.GroupCode;
        if (!PartMasterPermissionHelper.CanEditArea(groupCode, user.Role, PartMasterArea.A_KeHoach))
            throw new UnauthorizedAccessException(
                "Bạn không có quyền sửa vùng Kế hoạch nhập. " +
                "Chỉ ADMIN hoặc PLANNING Leader mới được sửa Tên chi tiết.");

        var part = await _db.PartMasters.FirstOrDefaultAsync(p => p.PartId == partId);
        if (part == null)
            throw new InvalidOperationException($"Không tìm thấy Part với PartId={partId}");

        var trimmed = string.IsNullOrWhiteSpace(newPartName) ? null : newPartName.Trim();
        if (part.PartName == trimmed) return; // Không có gì đổi

        part.PartName = trimmed;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Lượt 6D: Lấy Part theo PartNo (dùng cho URL /part-master/detail?partNo=xxx).
    /// </summary>
    public async Task<PartMaster?> GetByPartNoAsync(string partNo)
    {
        if (string.IsNullOrWhiteSpace(partNo)) return null;
        return await _db.PartMasters
            .Include(p => p.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartNo == partNo.Trim());
    }

    /// <summary>
    /// Lượt 6D: Lấy context của user để check permission ở UI.
    /// Fresh từ DB — không dùng claim (claim có thể cũ).
    /// </summary>
    public async Task<UserContext?> GetUserContextAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return null;
        return new UserContext
        {
            UserId = user.UserId,
            FullName = user.FullName,
            GroupCode = user.Group?.GroupCode,
            Role = user.Role,
            IsActive = user.IsActive
        };
    }
}

/// <summary>DTO chứa context user cho UI check permission.</summary>
public class UserContext
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? GroupCode { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Chuỗi hiển thị badge quyền: "TECHNICAL Leader" hoặc "ADMIN".</summary>
    public string DisplayRoleBadge
    {
        get
        {
            if (GroupCode == PartMasterPermissionHelper.GroupAdmin) return "ADMIN";
            if (string.IsNullOrEmpty(GroupCode)) return "?";
            if (string.IsNullOrEmpty(Role)) return GroupCode;
            return $"{GroupCode} {Role}";
        }
    }
}
