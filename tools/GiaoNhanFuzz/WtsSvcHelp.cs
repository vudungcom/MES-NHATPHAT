// [WTS-SVC 2026-10-29] Gọi HÀM THẬT Services/WtsSaveService (phần lưu / sửa / xoá WTS tách từ trang HieuSuat_Index)
//   thay cho mô phỏng cũ (VuHelp.Wts + w.IsVoided = true). Bộ kiểm vẫn tự tính trần + K5 riêng để đối chiếu với service.
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;

public static class WtsSvcHelp
{
    static int _slot;   // mỗi lần lưu 1 máy riêng "M<n>" (GC) → không bao giờ trùng giờ máy; giờ kết thúc = Now như mô phỏng cũ (sổ xét thứ tự theo EndTime)
    public static void Reset() => _slot = 0;

    public static WtsSaveService.WtsSaveGroup Build(int detailId, string pg, string? nc, string? code, decimal ok, decimal ng, int worker,
                                                   int? catId, long? keepCase, long? editLogId, string? machine = null)
    {
        bool gc = pg == "GC";
        bool htsp = pg is "TARO" or "BAVIA" or "WASHING";
        var end = DateTime.Now; var start = end.AddMinutes(-1); _slot++;
        var grp = new WtsSaveService.WtsSaveGroup
        {
            IsGc = gc, IsHtsp = htsp,
            HandoverGroupCode = gc ? "GC" : htsp ? "HTSP" : pg,
            NC = gc ? nc : null, MachineUsed = gc ? (machine ?? "M" + _slot) : null, ProductionMode = "serial",
            HtspSubGroup = pg switch { "TARO" => "Taro", "BAVIA" => "Bavia", "WASHING" => "Rua", _ => "" },
            UserId = worker, CaId = null, WorkDate = DateTime.Today,
        };
        grp.Rows.Add(new WtsSaveService.WtsSaveRow
        {
            KhPlanDetailId = detailId, PO = "PO" + detailId, StartTime = start, EndTime = end,
            NC = gc ? nc : code, WtsCode = gc ? null : code,
            QtyDone = ok + ng, QtyOk = ok, QtyNg = ng, NgCategoryId = ng > 0 ? catId : null, NgCaseId = keepCase,
            SetupTime_Actual = gc ? 1 : null, MachiningTime_Actual = gc ? 1 : null, InspectionTime_Actual = gc ? 1 : null,
            ActualTimePerPc = gc ? null : 1, ProductionMode = "serial",
            IsEditing = editLogId != null, WtsLogId = editLogId,
        });
        return grp;
    }

    /// <summary>Lưu 1 dòng WTS bằng service thật. Trả (Ok, lời báo, WtsLogId dòng mới).</summary>
    public static async Task<(bool Ok, string? Err, long? LogId)> SaveAsync(AppDbContext db, int detailId, string pg, string? nc, string? code,
        decimal ok, decimal ng, int worker, int? catId = null, long? keepCase = null, long? editLogId = null)
    {
        var grp = Build(detailId, pg, nc, code, ok, ng, worker, catId, keepCase, editLogId);
        var svc = new WtsSaveService(db, new HandoverLedgerService(db));
        var res = await svc.SaveGroupAsync(grp);
        db.Fixup();
        return (res.Ok, res.Ok ? null : res.ErrorMsg, grp.Rows[0].WtsLogId);
    }

    /// <summary>Xoá dòng WTS bằng service thật (K5 + khoá PO trong giao dịch).</summary>
    public static async Task<(bool Ok, string? Err)> VoidAsync(AppDbContext db, long wtsLogId, int detailId, int worker)
    {
        var svc = new WtsSaveService(db, new HandoverLedgerService(db));
        var res = await svc.VoidRowAsync(new WtsSaveService.WtsVoidRequest { WtsLogId = wtsLogId, KhPlanDetailId = detailId, UserId = worker });
        db.Fixup();
        return (res.Ok, res.Ok ? null : res.ErrorMsg);
    }
}
