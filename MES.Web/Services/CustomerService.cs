using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class CustomerService
{
    private readonly AppDbContext _db;

    public CustomerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Customer>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Customers.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }
        return await query.OrderBy(c => c.CustomerName).ToListAsync();
    }

    public async Task<Customer?> GetByIdAsync(int id)
    {
        return await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
    }

    public async Task<(bool Success, string? Error, Customer? Data)> CreateAsync(Customer model, int userId)
    {
        if (string.IsNullOrWhiteSpace(model.CustomerName))
            return (false, "Tên khách hàng không được để trống.", null);

        var code = model.CustomerCode?.Trim();
        if (!string.IsNullOrWhiteSpace(code))
        {
            // Kiểm tra trùng mã khách hàng
            var duplicateCode = await _db.Customers
                .AnyAsync(c => c.CustomerCode.ToLower() == code.ToLower() && c.IsActive);
            if (duplicateCode)
                return (false, $"Mã khách hàng '{code}' đã tồn tại trên hệ thống.", null);
        }

        var name = model.CustomerName.Trim();
        var duplicateName = await _db.Customers
            .AnyAsync(c => c.CustomerName.ToLower() == name.ToLower() && c.IsActive);
        if (duplicateName)
            return (false, $"Tên khách hàng '{name}' đã tồn tại.", null);

        var now = DateTime.Now;
        model.CustomerCode = code ?? "";
        model.CustomerName = name;
        model.SupplierCode = model.SupplierCode?.Trim();
        model.IsActive = true;

        try
        {
            _db.Customers.Add(model);
            await _db.SaveChangesAsync();

            // Ghi nhận Audit Trail
            _db.CustomerChangeLogs.Add(new CustomerChangeLog
            {
                CustomerId = model.CustomerId,
                FieldName = "Tạo mới",
                OldValue = null,
                NewValue = model.CustomerName,
                ChangedBy = userId,
                ChangedAt = now,
                Reason = "Khởi tạo khách hàng"
            });
            await _db.SaveChangesAsync();

            return (true, null, model);
        }
        catch (Exception ex)
        {
            return (false, ex.InnerException?.Message ?? ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Customer updated, string? reason, int userId)
    {
        if (string.IsNullOrWhiteSpace(updated.CustomerName))
            return (false, "Tên khách hàng không được để trống.");

        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == updated.CustomerId);
        if (existing == null) return (false, "Không tìm thấy khách hàng.");

        var code = updated.CustomerCode?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(code))
        {
            var duplicateCode = await _db.Customers
                .AnyAsync(c => c.CustomerId != updated.CustomerId && c.CustomerCode.ToLower() == code.ToLower() && c.IsActive);
            if (duplicateCode)
                return (false, $"Mã khách hàng '{code}' đã được sử dụng bởi khách hàng khác.");
        }

        var now = DateTime.Now;
        var r = string.IsNullOrWhiteSpace(reason) ? "Cập nhật thông tin" : reason.Trim();

        void LogDiff(string field, string? oldVal, string? newVal)
        {
            oldVal = oldVal?.Trim() ?? "";
            newVal = newVal?.Trim() ?? "";
            if (oldVal != newVal)
            {
                _db.CustomerChangeLogs.Add(new CustomerChangeLog
                {
                    CustomerId = existing.CustomerId,
                    FieldName = field,
                    OldValue = oldVal,
                    NewValue = newVal,
                    ChangedBy = userId,
                    ChangedAt = now,
                    Reason = r
                });
            }
        }

        LogDiff("Mã KH", existing.CustomerCode, updated.CustomerCode);
        LogDiff("Tên KH", existing.CustomerName, updated.CustomerName);
        LogDiff("Supplier Code", existing.SupplierCode, updated.SupplierCode);

        existing.CustomerCode = code;
        existing.CustomerName = updated.CustomerName.Trim();
        existing.SupplierCode = updated.SupplierCode?.Trim();

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleActiveAsync(int id, bool isActive, string? reason, int userId)
    {
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
        if (existing == null) return (false, "Không tìm thấy khách hàng.");

        var now = DateTime.Now;
        var oldStatus = existing.IsActive ? "Hoạt động" : "Ngừng";
        var newStatus = isActive ? "Hoạt động" : "Đã xóa/Ngừng";

        existing.IsActive = isActive;

        _db.CustomerChangeLogs.Add(new CustomerChangeLog
        {
            CustomerId = existing.CustomerId,
            FieldName = "Trạng thái",
            OldValue = oldStatus,
            NewValue = newStatus,
            ChangedBy = userId,
            ChangedAt = now,
            Reason = reason ?? (isActive ? "Khôi phục hoạt động" : "Ngừng hoạt động")
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<CustomerChangeLogItem>> GetChangeLogsAsync(int customerId)
    {
        return await _db.CustomerChangeLogs
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.ChangedAt)
            .Include(l => l.ChangedByUser)
            .Select(l => new CustomerChangeLogItem
            {
                LogId = l.LogId,
                CustomerId = l.CustomerId,
                FieldName = l.FieldName,
                OldValue = l.OldValue,
                NewValue = l.NewValue,
                Reason = l.Reason,
                ChangedAt = l.ChangedAt,
                ChangedByName = l.ChangedByUser != null ? l.ChangedByUser.FullName : l.ChangedBy.ToString()
            })
            .AsNoTracking()
            .ToListAsync();
    }
}

public class CustomerChangeLogItem
{
    public long LogId { get; set; }
    public int CustomerId { get; set; }
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
}