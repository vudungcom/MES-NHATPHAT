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
            // EnsurePermissionAAsync đã xác nhận createdBy có quyền PART_TAO_MOI.
            // Người tạo = người đã xác nhận Section A → confirmed ngay khi tạo.
            IsPlanConfirmed = true,
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
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Đánh dấu Part "Ngưng sử dụng" (IsObsolete = true).
    /// Có quyền nếu: ADMIN, hoặc nhóm có CanDelete = true trong section PART_TAO_MOI.
    /// Part vẫn còn trong DB và hiện trong danh sách với badge "Ngưng".
    /// Không ảnh hưởng phiếu KhPlan/Kho đã tạo trước đó.
    /// </summary>
    public async Task<(bool Success, string? Error)> SetObsoleteAsync(int partId, int requestedByUserId, bool obsolete)
    {
        // Kiểm tra quyền
        var requester = await _db.Users.Include(u => u.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == requestedByUserId && u.IsActive);
        if (requester == null) return (false, "Không xác định được người thực hiện.");
        if (requester.Group == null || !requester.Group.IsActive) return (false, "Nhóm của tài khoản không hoạt động — không có quyền.");   // [PERM-ACTIVE 2026-11-16] (P-07)

        bool isAdmin = requester.Group?.GroupCode == "ADMIN" || requester.Username.ToLower() == "admin";
        if (!isAdmin)
        {
            // Kiểm tra CanDelete trong PART_TAO_MOI qua JSON permissions của group
            var canDelete = false;
            if (!string.IsNullOrEmpty(requester.Group?.Permissions))
            {
                try
                {
                    var perms = System.Text.Json.JsonSerializer
                        .Deserialize<List<MES.Web.Constants.GroupPermissionSetting>>(requester.Group.Permissions);
                    var p = perms?.FirstOrDefault(x => x.SectionCode == MES.Web.Constants.SystemPermissions.PartTaoMoi);
                    canDelete = p?.CanDelete ?? false;
                }
                catch { /* JSON lỗi → không có quyền */ }
            }
            if (!canDelete)
                return (false, "Bạn không có quyền thực hiện thao tác này.");
        }

        var part = await _db.PartMasters.FirstOrDefaultAsync(p => p.PartId == partId);
        if (part == null) return (false, "Không tìm thấy Part.");
        if (part.IsObsolete == obsolete)
            return (false, obsolete ? "Part này đã được đánh dấu Ngưng rồi." : "Part này đang hoạt động bình thường.");

        part.IsObsolete = obsolete;
        await _db.SaveChangesAsync();
        return (true, null);
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

    // [QF-P-03 2026-11-23] Ghi DB của trang Part Master (Detail_Partmaster) dời vào service — máy chủ kiểm quyền đọc từ DB mỗi lần ghi.
    //   Nguồn: CLAUDE quét Fable 14/11 (P-03); HÙNG 23/11 13:20 OK. Ghi y như trang cũ.

    /// <summary>[QF-P-03] Ghi chú mạ (vùng A, quyền sửa PART_TAO_MOI). Trả (thấy Part, giá trị đã lưu).</summary>
    public async Task<(bool Found, string? GhiChuMa)> UpdateGhiChuMaAsync(int partId, string? ghiChuMa, int userId)
    {
        await EnsurePermissionAAsync(userId);
        var pm = await _db.PartMasters.FirstOrDefaultAsync(p => p.PartId == partId);
        if (pm == null) return (false, null);
        pm.GhiChuMa = string.IsNullOrWhiteSpace(ghiChuMa) ? null : ghiChuMa.Trim();
        await _db.SaveChangesAsync();
        return (true, pm.GhiChuMa);
    }

    /// <summary>[QF-P-03] Cờ "Xác nhận OK" từng vùng sau lượt lưu / chuyển về NG khi lưu lỗi. null = không đụng vùng đó.
    /// Quyền (giữ đúng luật trang cũ): vùng B–E và đặt vùng A = false cần quyền SỬA vùng; đặt vùng A = true cần quyền XEM (hoặc sửa) PART_TAO_MOI
    /// (trang cũ: người xem được mục A mà bấm "Lưu &amp; Xác nhận OK" thì mục A thành OK — HÙNG 23/11 13:20 câu B5 giữ nguyên luật).
    /// Vùng KHÔNG đủ quyền (bị rút quyền giữa phiên) → BỎ QUA vùng đó, ghi tên vào BiChan, các vùng còn lại vẫn ghi — rà độc lập 23/11 (Thấp 12):
    /// trước ném lỗi cả lệnh → vùng hợp lệ cũng không về NG khi lưu lỗi ([RS-E2] mất tác dụng). Không thấy Part → Part = null.
    /// Trả PartMaster (đang theo dõi) sau khi lưu.</summary>
    public async Task<(PartMaster? Part, List<string> BiChan)> UpdateConfirmFlagsAsync(int partId, int userId, bool? plan, bool? machining, bool? htsp, bool? kcs, bool? pkg)
    {
        var user = await _db.Users.Include(u => u.Group).AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null || !user.IsActive) throw new UnauthorizedAccessException("User không tồn tại hoặc đã bị khóa");
        var biChan = new List<string>();
        bool? Can(bool? v, PartMasterArea a, bool xemDuoc)
        {
            if (v == null) return null;
            bool ok = xemDuoc ? PartMasterPermissionHelper.CanViewArea(user.Group, a) || PartMasterPermissionHelper.CanEditArea(user.Group, a)
                              : PartMasterPermissionHelper.CanEditArea(user.Group, a);
            if (!ok) { biChan.Add(PartMasterPermissionHelper.GetAreaName(a)); return null; }
            return v;
        }
        plan = Can(plan, PartMasterArea.A_KeHoach, plan == true);
        machining = Can(machining, PartMasterArea.B_Machining, false);
        htsp = Can(htsp, PartMasterArea.C_HoanThienSP, false);
        kcs = Can(kcs, PartMasterArea.D_KiemTra, false);
        pkg = Can(pkg, PartMasterArea.E_DongGoi, false);
        var pm = await _db.PartMasters.FirstOrDefaultAsync(p => p.PartId == partId);
        if (pm == null) return (null, biChan);
        if (plan is bool a1) pm.IsPlanConfirmed = a1;
        if (machining is bool b1) pm.IsMachiningConfirmed = b1;
        if (htsp is bool c1) pm.IsHtspConfirmed = c1;
        if (kcs is bool d1) pm.IsKcsConfirmed = d1;
        if (pkg is bool e1) pm.IsPkgConfirmed = e1;
        await _db.SaveChangesAsync();
        return (pm, biChan);
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
