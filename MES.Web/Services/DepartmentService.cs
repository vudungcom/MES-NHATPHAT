using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class DepartmentService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public DepartmentService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<Department>> GetAllActiveAsync()
    {
        return await _db.Set<Department>()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Level)
            .ThenBy(d => d.DisplayOrder)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> UpdateManagerAsync(
        int departmentId, 
        string? newManagerName, 
        string? newRoleTitle, 
        string? reason, 
        int currentUserId, 
        string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var dept = await _db.Set<Department>().FirstOrDefaultAsync(d => d.DepartmentId == departmentId);
        if (dept == null) return (false, "Không tìm thấy bộ phận.");

        var now = DateTime.Now;
        var logs = new List<DepartmentChangeLog>();

        newManagerName = newManagerName?.Trim() ?? "";
        var oldManagerName = dept.ManagerName?.Trim() ?? "";
        if (oldManagerName != newManagerName)
        {
            logs.Add(new DepartmentChangeLog
            {
                DepartmentId = dept.DepartmentId,
                FieldName = "Người phụ trách",
                OldValue = oldManagerName,
                NewValue = newManagerName,
                Reason = reason,
                ChangedAt = now,
                ChangedBy = currentUserId
            });
            dept.ManagerName = newManagerName;
        }

        newRoleTitle = newRoleTitle?.Trim() ?? "";
        var oldRoleTitle = dept.RoleTitle?.Trim() ?? "";
        if (oldRoleTitle != newRoleTitle)
        {
            logs.Add(new DepartmentChangeLog
            {
                DepartmentId = dept.DepartmentId,
                FieldName = "Chức danh",
                OldValue = oldRoleTitle,
                NewValue = newRoleTitle,
                Reason = reason,
                ChangedAt = now,
                ChangedBy = currentUserId
            });
            dept.RoleTitle = newRoleTitle;
        }

        if (logs.Any())
        {
            dept.UpdatedAt = now;
            dept.UpdatedBy = currentUserId;
            _db.Set<DepartmentChangeLog>().AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<List<DepartmentChangeLog>> GetChangeLogsAsync(int departmentId)
    {
        return await _db.Set<DepartmentChangeLog>()
            .Include(x => x.ChangedByUser)
            .Where(x => x.DepartmentId == departmentId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }
}