using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Quản lý mapping CategoryCode (nhóm WTS tiêu chuẩn) → ProcessGroup (GC/HTSP/KCS/PKG).
/// Thay thế hardcode trong HieuSuat_Index.razor.
/// </summary>
public class WtsCategoryGroupMappingService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    // Danh sách ProcessGroup cố định — chỉ 4 giá trị hợp lệ trong hệ thống
    public static readonly List<(string Code, string Name)> ProcessGroups = new()
    {
        ("GC",   "Gia công (GC)"),
        ("HTSP", "Hoàn thiện sản phẩm (HTSP)"),
        ("KCS",  "Kiểm tra chất lượng (KCS)"),
        ("PKG",  "Đóng gói (PKG)"),
    };

    public WtsCategoryGroupMappingService(AppDbContext db, AuthService authSvc)
    {
        _db      = db;
        _authSvc = authSvc;
    }

    // ─────────────────────────────────────────────
    // READ
    // ─────────────────────────────────────────────

    /// <summary>Lấy toàn bộ mapping hiện tại.</summary>
    public async Task<List<WtsCategoryGroupMapping>> GetAllAsync()
    {
        return await _db.WtsCategoryGroupMappings
            .OrderBy(m => m.ProcessGroup)
            .ThenBy(m => m.CategoryCode)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy danh sách CategoryCode thuộc 1 ProcessGroup.
    /// Dùng trong HieuSuat để lọc WTS tasks theo nhóm công nhân — thay hardcode.
    /// </summary>
    public async Task<List<string>> GetCategoryCodesForProcessGroupAsync(string processGroup)
    {
        return await _db.WtsCategoryGroupMappings
            .Where(m => m.ProcessGroup == processGroup)
            .Select(m => m.CategoryCode)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Lấy dict ProcessGroup → List&lt;CategoryCode&gt;.
    /// Dùng để load 1 lần rồi tra cứu nhiều lần trong cùng 1 page lifecycle.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetAllGroupedAsync()
    {
        var all = await GetAllAsync();
        return all
            .GroupBy(m => m.ProcessGroup)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CategoryCode).ToList());
    }

    // ─────────────────────────────────────────────
    // WRITE — Lưu toàn bộ mapping của 1 ProcessGroup (replace strategy)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Thay thế toàn bộ mapping của 1 ProcessGroup bằng danh sách mới.
    /// PIN xác nhận bắt buộc.
    /// </summary>
    public async Task<(bool Success, string? Error)> SaveGroupMappingAsync(
        string processGroup,
        List<(string Code, string Name)> selectedCategories,
        int currentUserId,
        string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return (false, "Vui lòng nhập mã PIN xác nhận.");

        if (!ProcessGroups.Any(g => g.Code == processGroup))
            return (false, $"ProcessGroup '{processGroup}' không hợp lệ.");

        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid)
            return (false, "Mã PIN xác nhận không chính xác.");

        // Xoá toàn bộ mapping cũ của ProcessGroup này
        var existing = await _db.WtsCategoryGroupMappings
            .Where(m => m.ProcessGroup == processGroup)
            .ToListAsync();
        _db.WtsCategoryGroupMappings.RemoveRange(existing);

        // Thêm mapping mới
        var now = DateTime.Now;
        foreach (var cat in selectedCategories)
        {
            _db.WtsCategoryGroupMappings.Add(new WtsCategoryGroupMapping
            {
                CategoryCode  = cat.Code.Trim().ToUpper(),
                CategoryName  = cat.Name.Trim(),
                ProcessGroup  = processGroup,
                CreatedAt     = now,
                CreatedBy     = currentUserId,
            });
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }
}
