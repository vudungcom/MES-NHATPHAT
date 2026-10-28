// [VULOI 2026-10-28] Trợ giúp chung cho kịch bản / bộ kiểm: danh mục lỗi giả + WTS có NG gắn vụ (như trang WTS lưu).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class VuHelp
{
    /// <summary>5 loại lỗi đang dùng (Id 1..5) + 1 loại đã ngưng (Id 6).</summary>
    public static void SeedCats(AppDbContext db)
    {
        var cats = new[] { ("NUT", "Phôi nứt"), ("XUOC", "Xước"), ("SKT", "Sai kích thước"), ("BAVIA", "Bavia sót"), ("MOP", "Móp") };
        for (int i = 0; i < cats.Length; i++)
            db.NgCategories.List.Add(new NgCategory { NgCategoryId = i + 1, Code = cats[i].Item1, Name = cats[i].Item2, SortOrder = i, IsActive = true, CreatedBy = 1, CreatedAt = DateTime.Now });
        db.NgCategories.List.Add(new NgCategory { NgCategoryId = 6, Code = "CU", Name = "Loại cũ đã ngưng", SortOrder = 9, IsActive = false, CreatedBy = 1, CreatedAt = DateTime.Now });
    }

    public static string Label(AppDbContext db, int catId) => db.NgCategories.List.First(x => x.NgCategoryId == catId).Label;

    /// <summary>Thêm dòng WTS (như trang WTS): NG &gt; 0 thì tạo vụ lỗi (hoặc giữ vụ cũ khi sửa dòng).</summary>
    public static async Task<WtsProductionLog> Wts(AppDbContext db, string pg, string? nc, string? code, decimal ok, decimal ng, int worker, int catId, long? keepCase = null)
    {
        long? caseId = null;
        string? reason = null;
        if (ng > 0)
        {
            var cat = db.NgCategories.List.First(x => x.NgCategoryId == catId);
            reason = cat.Label;
            if (keepCase != null) caseId = keepCase;
            else
            {
                var g = pg switch { "GC" => "GC", "TARO" or "BAVIA" or "WASHING" => "HTSP", _ => pg };
                var vu = await NgCaseService.CreateCaseAsync(db, 1, cat, NgOrigin.Wts, g, nc ?? code, g, worker);
                caseId = vu.NgCaseId;
            }
        }
        var w = new WtsProductionLog
        {
            WtsLogId = db.WtsProductionLogs.List.Count + 1, KhPlanDetailId = 1, ProcessGroup = pg, NC = nc, WtsCode = code, WorkerId = worker,
            QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, NgReason = reason, NgCaseId = caseId,
            StartTime = DateTime.Now, EndTime = DateTime.Now, CreatedAt = DateTime.Now
        };
        db.WtsProductionLogs.List.Add(w);
        db.Fixup();
        return w;
    }

    public static long LastCase(AppDbContext db) => db.NgCases.List.Max(x => x.NgCaseId);
}
