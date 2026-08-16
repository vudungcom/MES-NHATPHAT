using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service query dữ liệu cho trang Part Master Index (list + expand).
///
/// Thiết kế:
///  - `GetAllRowsAsync` load 1 lượt tất cả Part + Customer + attribute default + preview NC của 5 bảng step.
///    Tránh N+1: chỉ 7 query cho toàn bộ list, không phải 5*N cho mỗi row.
///  - `GetDetailForExpandAsync` load lazy khi user click ▶: chỉ query 5 bảng cho 1 Part cụ thể.
/// </summary>
public class PartMasterListService
{
    private readonly AppDbContext _db;

    public PartMasterListService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Load toàn bộ Part active + Customer + attribute default + preview NC 5 bảng step.
    /// Trả về list đã sort theo PartNo.
    /// </summary>
    public async Task<List<PartMasterListRow>> GetAllRowsAsync()
    {
        // 1. PartMasters + Customer
        var parts = await _db.PartMasters
            .Include(p => p.Customer)
            .Where(p => p.IsActive)
            .OrderBy(p => p.PartNo)
            .AsNoTracking()
            .ToListAsync();

        if (parts.Count == 0)
            return new List<PartMasterListRow>();

        var partIds = parts.Select(p => p.PartId).ToHashSet();

        // 2. Attribute default (Material / MaterialConfig / MaterialNote)
        var attributes = await _db.PartMasterAttributes
            .Where(a => partIds.Contains(a.PartId) && a.IsDefault && a.IsActive)
            .Select(a => new { a.PartId, a.AttributeType, a.Value })
            .AsNoTracking()
            .ToListAsync();

        // Map: (partId, attributeType) -> value
        var attrLookup = attributes
            .GroupBy(a => a.PartId)
            .ToDictionary(g => g.Key,
                          g => g.ToDictionary(x => x.AttributeType, x => x.Value));

        // 3-7. Preview NC của 5 bảng step (chỉ NC + StepOrder)
        var machiningPreview = await LoadNcPreviewAsync(
            _db.PartMachiningSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var taroPreview = await LoadNcPreviewAsync(
            _db.PartTaroSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var baviaPreview = await LoadNcPreviewAsync(
            _db.PartBaviaSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var washingPreview = await LoadNcPreviewAsync(
            _db.PartWashingSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var inspectionPreview = await LoadNcPreviewAsync(
            _db.PartInspectionSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        // Compose result
        return parts.Select(p => new PartMasterListRow
        {
            PartId = p.PartId,
            PartNo = p.PartNo,
            PartName = p.PartName,
            CustomerId = p.CustomerId,
            CustomerName = p.Customer?.CustomerName,
            Material       = GetAttr(attrLookup, p.PartId, "Material"),
            MaterialConfig = GetAttr(attrLookup, p.PartId, "MaterialConfig"),
            MaterialNote   = GetAttr(attrLookup, p.PartId, "MaterialNote"),
            MachiningNcs   = machiningPreview.GetValueOrDefault(p.PartId) ?? new(),
            TaroNcs        = taroPreview.GetValueOrDefault(p.PartId) ?? new(),
            BaviaNcs       = baviaPreview.GetValueOrDefault(p.PartId) ?? new(),
            WashingNcs     = washingPreview.GetValueOrDefault(p.PartId) ?? new(),
            InspectionNcs  = inspectionPreview.GetValueOrDefault(p.PartId) ?? new(),
        }).ToList();
    }

    /// <summary>
    /// Load chi tiết đầy đủ 5 bảng step cho 1 Part (dùng khi expand).
    /// Chỉ IsActive=true. Sort theo StepOrder.
    /// </summary>
    public async Task<PartMasterExpandDetail> GetDetailForExpandAsync(int partId)
    {
        var machining = await _db.PartMachiningSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var taro = await _db.PartTaroSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var bavia = await _db.PartBaviaSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var washing = await _db.PartWashingSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var inspection = await _db.PartInspectionSteps
            .Where(s => s.PartId == partId && s.IsActive)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        return new PartMasterExpandDetail
        {
            Machining = machining,
            Taro = taro,
            Bavia = bavia,
            Washing = washing,
            Inspection = inspection
        };
    }

    // ==== PRIVATE ====

    /// <summary>Load NC preview thành dict PartId -> List of NC (đã sort theo StepOrder).</summary>
    private static async Task<Dictionary<int, List<string>>> LoadNcPreviewAsync(IQueryable<NcPreviewRaw> query)
    {
        var raw = await query.AsNoTracking().ToListAsync();
        return raw
            .GroupBy(x => x.PartId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.NC).ToList());
    }

    private static string? GetAttr(Dictionary<int, Dictionary<string, string>> lookup, int partId, string type)
    {
        if (!lookup.TryGetValue(partId, out var dict)) return null;
        return dict.TryGetValue(type, out var val) ? val : null;
    }

    private class NcPreviewRaw
    {
        public int PartId { get; set; }
        public string NC { get; set; } = string.Empty;
    }
}

// ============================================================================
// DTO — dùng cho UI, không map trực tiếp DB
// ============================================================================

/// <summary>1 dòng trong bảng list Part Master.</summary>
public class PartMasterListRow
{
    public int PartId { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string? PartName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? Material { get; set; }
    public string? MaterialConfig { get; set; }
    public string? MaterialNote { get; set; }
    public List<string> MachiningNcs { get; set; } = new();
    public List<string> TaroNcs { get; set; } = new();
    public List<string> BaviaNcs { get; set; } = new();
    public List<string> WashingNcs { get; set; } = new();
    public List<string> InspectionNcs { get; set; } = new();

    /// <summary>Tổng số công đoạn (tất cả 5 loại). Dùng để hiển thị "Chưa có" nếu = 0.</summary>
    public int TotalSteps =>
        MachiningNcs.Count + TaroNcs.Count + BaviaNcs.Count + WashingNcs.Count + InspectionNcs.Count;
}

/// <summary>Data chi tiết cho phần expand — chứa 5 list step entity đầy đủ.</summary>
public class PartMasterExpandDetail
{
    public List<PartMachiningStep> Machining { get; set; } = new();
    public List<PartTaroStep> Taro { get; set; } = new();
    public List<PartBaviaStep> Bavia { get; set; } = new();
    public List<PartWashingStep> Washing { get; set; } = new();
    public List<PartInspectionStep> Inspection { get; set; } = new();

    public int TotalSteps =>
        Machining.Count + Taro.Count + Bavia.Count + Washing.Count + Inspection.Count;
}
