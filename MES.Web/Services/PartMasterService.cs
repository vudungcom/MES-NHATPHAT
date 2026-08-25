using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Quản lý PartMaster + PartMasterAttribute versioning.
/// Phân quyền vùng A (PART_TAO_MOI) qua ma trận RBAC.
/// </summary>
public class PartMasterService
{
    private readonly AppDbContext _db;

    public PartMasterService(AppDbContext db)
    {
        _db = db;
    }

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

    public async Task<string?> GetDefaultValueAsync(int partId, string attributeType)
    {
        var attr = await _db.PartMasterAttributes
            .FirstOrDefaultAsync(a => a.PartId == partId
                                   && a.AttributeType == attributeType
                                   && a.IsDefault
                                   && a.IsActive);
        return attr?.Value;
    }

    public async Task<Dictionary<(int PartId, string AttributeType), string>> GetDefaultsAsync(List<int> partIds)
    {
        var attrs = await _db.PartMasterAttributes
            .Where(a => partIds.Contains(a.PartId) && a.IsDefault && a.IsActive)
            .ToListAsync();

        return attrs.ToDictionary(a => (a.PartId, a.AttributeType), a => a.Value);
    }

    public async Task<PartMasterAttribute> AddValueAsync(
        int partId,
        string attributeType,
        string value,
        int createdBy)
    {
        await EnsurePermissionAAsync(createdBy);

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
            Value = value.Trim(),
            IsDefault = false,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.Now
        };
        _db.PartMasterAttributes.Add(attr);
        await _db.SaveChangesAsync();
        return attr;
    }

    public async Task SetAsDefaultAsync(
        int partId,
        string attributeType,
        string value,
        int changedBy)
    {
        await EnsurePermissionAAsync(changedBy);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var currentDefaults = await _db.PartMasterAttributes
                .Where(a => a.PartId == partId
                         && a.AttributeType == attributeType
                         && a.IsDefault)
                .ToListAsync();
            foreach (var d in currentDefaults)
            {
                d.IsDefault = false;
            }

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
                    Value = value.Trim(),
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

    public async Task<PartMaster> CreatePartMasterAsync(
        string partNo,
        int? customerId,
        Dictionary<string, string> initialAttributes,
        int createdBy,
        string? partName = null)
    {
        await EnsurePermissionAAsync(createdBy);

        var trimmedPartNo = partNo.Trim();
        if (string.IsNullOrEmpty(trimmedPartNo))
            throw new ArgumentException("Part No không được để trống", nameof(partNo));

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

    public async Task UpdatePartNameAsync(int partId, string? newPartName, int userId)
    {
        await EnsurePermissionAAsync(userId);

        var part = await _db.PartMasters.FirstOrDefaultAsync(p => p.PartId == partId);
        if (part == null)
            throw new InvalidOperationException($"Không tìm thấy Part với PartId={partId}");

        var trimmed = string.IsNullOrWhiteSpace(newPartName) ? null : newPartName.Trim();
        if (part.PartName == trimmed) return;

        part.PartName = trimmed;
        // Bỏ part.UpdatedAt và part.UpdatedBy vì class PartMaster không có
        await _db.SaveChangesAsync();
    }

    public async Task<PartMaster?> GetByPartNoAsync(string partNo)
    {
        if (string.IsNullOrWhiteSpace(partNo)) return null;
        return await _db.PartMasters
            .Include(p => p.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartNo == partNo.Trim());
    }

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
            Role = user.Group?.GroupName,
            IsActive = user.IsActive
        };
    }

    private async Task EnsurePermissionAAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) throw new UnauthorizedAccessException("User không tồn tại");
        if (!user.IsActive) throw new UnauthorizedAccessException("User đã bị khóa");

        if (!PartMasterPermissionHelper.CanEditArea(user.Group, PartMasterArea.A_KeHoach))
            throw new UnauthorizedAccessException(
                "Bạn không có quyền sửa vùng Kế hoạch nhập (PART_TAO_MOI). Vui lòng liên hệ Admin để được cấp quyền.");
    }
}

public class UserContext
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? GroupCode { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }

    public string DisplayRoleBadge
    {
        get
        {
            if (GroupCode == PartMasterPermissionHelper.GroupAdmin) return "ADMIN";
            if (string.IsNullOrEmpty(GroupCode)) return "?";
            return string.IsNullOrEmpty(Role) ? GroupCode : $"{FullName} ({Role})";
        }
    }
}