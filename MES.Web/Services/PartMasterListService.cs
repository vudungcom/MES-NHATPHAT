using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

/// <summary>
/// Service query dữ liệu cho trang Part Master Index (list + expand).
///
/// Thiết kế:
///  - `GetAllRowsAsync` load 1 lượt tất cả Part active + Customer + attribute default + preview NC của 6 bảng step.
///    Tránh N+1: chỉ query 1 lần cho toàn bộ list, không phải query lặp lại cho mỗi row.
///  - `GetDetailForExpandAsync` load lazy khi user click ▶: chỉ query 6 bảng cho 1 Part cụ thể.
///  - Part có IsObsolete = true vẫn hiện trong list (có badge "Ngưng"), không bị filter ra.
/// </summary>
public class PartMasterListService
{
    private readonly AppDbContext _db;

    public PartMasterListService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Load toàn bộ Part active + Customer + attribute default + preview NC 6 bảng step.
    /// Bao gồm cả Part IsObsolete = true (hiện với badge "Ngưng" trong UI).
    /// Trả về list đã sort: Part Obsolete xuống cuối, trong mỗi nhóm sort theo CreatedAt DESC.
    /// </summary>
    public async Task<List<PartMasterListRow>> GetAllRowsAsync()
    {
        // 1. PartMasters + Customer (bao gồm cả Obsolete, chỉ loại IsActive = false)
        var parts = await _db.PartMasters
            .Include(p => p.Customer)
            .Where(p => p.IsActive)
            .OrderBy(p => p.IsObsolete)          // false (đang dùng) lên trước
            .ThenByDescending(p => p.CreatedAt)
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

        var attrLookup = attributes
            .GroupBy(a => a.PartId)
            .ToDictionary(g => g.Key,
                          g => g.ToDictionary(x => x.AttributeType, x => x.Value));

        // 3-8. Preview NC của 6 bảng step
        var machiningPreview = await LoadNcPreviewAsync(
            _db.PartMachiningSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var taroPreview = await LoadNcPreviewAsync(
            _db.PartTaroSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var baviaPreview = await LoadNcPreviewAsync(
            _db.PartBaviaSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var washingPreview = await LoadNcPreviewAsync(
            _db.PartWashingSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var inspectionPreview = await LoadNcPreviewAsync(
            _db.PartInspectionSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

        var pkgPreview = await LoadNcPreviewAsync(
            _db.PartPackagingSteps.Where(s => partIds.Contains(s.PartId) && s.IsActive && !s.IsBackup)
                .OrderBy(s => s.PartId).ThenBy(s => s.StepOrder)
                .Select(s => new NcPreviewRaw { PartId = s.PartId, NC = s.NC }));

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

            IsObsolete = p.IsObsolete,

            IsPlanConfirmed      = p.IsPlanConfirmed,
            IsMachiningConfirmed = p.IsMachiningConfirmed,
            IsHtspConfirmed      = p.IsHtspConfirmed,
            IsKcsConfirmed       = p.IsKcsConfirmed,
            IsPkgConfirmed       = p.IsPkgConfirmed,

            MachiningNcs  = machiningPreview.GetValueOrDefault(p.PartId) ?? new(),
            TaroNcs       = taroPreview.GetValueOrDefault(p.PartId) ?? new(),
            BaviaNcs      = baviaPreview.GetValueOrDefault(p.PartId) ?? new(),
            WashingNcs    = washingPreview.GetValueOrDefault(p.PartId) ?? new(),
            InspectionNcs = inspectionPreview.GetValueOrDefault(p.PartId) ?? new(),
            PkgNcs        = pkgPreview.GetValueOrDefault(p.PartId) ?? new(),
        }).ToList();
    }

    /// <summary>
    /// Load chi tiết đầy đủ 6 bảng step cho 1 Part (dùng khi expand).
    /// </summary>
    public async Task<PartMasterExpandDetail> GetDetailForExpandAsync(int partId)
    {
        var machining = await _db.PartMachiningSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var taro = await _db.PartTaroSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var bavia = await _db.PartBaviaSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var washing = await _db.PartWashingSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var inspection = await _db.PartInspectionSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        var pkg = await _db.PartPackagingSteps
            .Where(s => s.PartId == partId && s.IsActive && !s.IsBackup)
            .OrderBy(s => s.StepOrder)
            .AsNoTracking()
            .ToListAsync();

        return new PartMasterExpandDetail
        {
            Machining  = machining,
            Taro       = taro,
            Bavia      = bavia,
            Washing    = washing,
            Inspection = inspection,
            Packaging  = pkg
        };
    }

    // ==== PRIVATE ====

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
// DTO
// ============================================================================

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

    /// <summary>True = Part ngưng sử dụng — vẫn hiện trong list, có badge "Ngưng".</summary>
    public bool IsObsolete { get; set; }

    public bool IsPlanConfirmed { get; set; }
    public bool IsMachiningConfirmed { get; set; }
    public bool IsHtspConfirmed { get; set; }
    public bool IsKcsConfirmed { get; set; }
    public bool IsPkgConfirmed { get; set; }

    public bool IsFullyConfirmed =>
        IsPlanConfirmed && IsMachiningConfirmed && IsHtspConfirmed && IsKcsConfirmed && IsPkgConfirmed;

    public List<string> MachiningNcs { get; set; } = new();
    public List<string> TaroNcs      { get; set; } = new();
    public List<string> BaviaNcs     { get; set; } = new();
    public List<string> WashingNcs   { get; set; } = new();
    public List<string> InspectionNcs { get; set; } = new();
    public List<string> PkgNcs       { get; set; } = new();

    public int TotalSteps =>
        MachiningNcs.Count + TaroNcs.Count + BaviaNcs.Count +
        WashingNcs.Count + InspectionNcs.Count + PkgNcs.Count;
}

public class PartMasterExpandDetail
{
    public List<PartMachiningStep>  Machining  { get; set; } = new();
    public List<PartTaroStep>       Taro       { get; set; } = new();
    public List<PartBaviaStep>      Bavia      { get; set; } = new();
    public List<PartWashingStep>    Washing    { get; set; } = new();
    public List<PartInspectionStep> Inspection { get; set; } = new();
    public List<PartPackagingStep>  Packaging  { get; set; } = new();

    public int TotalSteps =>
        Machining.Count + Taro.Count + Bavia.Count +
        Washing.Count + Inspection.Count + Packaging.Count;
}
