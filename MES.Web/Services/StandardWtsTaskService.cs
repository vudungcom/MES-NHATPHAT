using ClosedXML.Excel;
using MES.Web.Data;
using MES.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MES.Web.Services;

public class StandardWtsTaskService
{
    private readonly AppDbContext _db;
    private readonly AuthService _authSvc;

    public StandardWtsTaskService(AppDbContext db, AuthService authSvc)
    {
        _db = db;
        _authSvc = authSvc;
    }

    public async Task<List<StandardWtsTask>> GetAllActiveAsync()
    {
        return await _db.StandardWtsTasks
            .Where(t => t.IsActive)
            .OrderBy(t => t.CategoryCode)
            .ThenBy(t => t.DisplayOrder)
            .ThenBy(t => t.TaskId)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy danh sách các nhóm công đoạn động đang có trong CSDL
    /// </summary>
    public async Task<List<(string Code, string Name)>> GetDistinctCategoriesAsync()
    {
        var list = await _db.StandardWtsTasks
            .Where(t => t.IsActive)
            .Select(t => new { t.CategoryCode, t.CategoryName })
            .Distinct()
            .ToListAsync();

        return list.Select(x => (x.CategoryCode, x.CategoryName)).ToList();
    }

    public async Task<List<StandardWtsTask>> GetByCategoryAsync(string categoryCode)
    {
        return await _db.StandardWtsTasks
            .Where(t => t.IsActive && t.CategoryCode == categoryCode)
            .OrderBy(t => t.DisplayOrder)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> CreateAsync(StandardWtsTask model, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        if (string.IsNullOrWhiteSpace(model.CategoryCode)) return (false, "Mã nhóm công đoạn không được để trống.");
        if (string.IsNullOrWhiteSpace(model.CategoryName)) return (false, "Tên nhóm công đoạn không được để trống.");
        if (string.IsNullOrWhiteSpace(model.TaskCode)) return (false, "Mã công việc không được để trống.");
        if (string.IsNullOrWhiteSpace(model.TaskName)) return (false, "Tên công việc không được để trống.");

        model.CategoryCode = model.CategoryCode.Trim().ToUpper();
        model.CategoryName = model.CategoryName.Trim();
        model.TaskCode = model.TaskCode.Trim().ToUpper();
        model.TaskName = model.TaskName.Trim();
        model.DefaultUnit = string.IsNullOrWhiteSpace(model.DefaultUnit) ? "Chi tiết" : model.DefaultUnit.Trim();
        // IsProductiveTask giữ nguyên giá trị từ form (default true)

        bool exists = await _db.StandardWtsTasks.AnyAsync(x => x.TaskCode == model.TaskCode && x.IsActive);
        if (exists) return (false, $"Mã công việc '{model.TaskCode}' đã tồn tại trong danh mục.");

        var now = DateTime.Now;
        model.IsActive = true;
        model.CreatedAt = now;
        model.CreatedBy = currentUserId;

        _db.StandardWtsTasks.Add(model);

        _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
        {
            TaskId = model.TaskId,
            FieldName = "Tạo mới công việc",
            OldValue = null,
            NewValue = $"[{model.TaskCode}] {model.TaskName} ({model.CategoryName}) | Có ích: {(model.IsProductiveTask ? "Có" : "Không")}",
            Reason = "Khởi tạo công việc tiêu chuẩn",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(StandardWtsTask updatedModel, string? reason, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var task = await _db.StandardWtsTasks.FirstOrDefaultAsync(x => x.TaskId == updatedModel.TaskId && x.IsActive);
        if (task == null) return (false, "Không tìm thấy công việc.");

        var newCode = updatedModel.TaskCode.Trim().ToUpper();
        bool duplicate = await _db.StandardWtsTasks.AnyAsync(x => x.TaskCode == newCode && x.TaskId != task.TaskId && x.IsActive);
        if (duplicate) return (false, $"Mã công việc '{newCode}' đã được sử dụng bởi một mục khác.");

        var now = DateTime.Now;
        var logs = new List<StandardWtsTaskChangeLog>();

        void CheckAndLog(string fieldName, string? oldVal, string? newVal)
        {
            oldVal = oldVal?.Trim() ?? "";
            newVal = newVal?.Trim() ?? "";
            if (oldVal != newVal)
            {
                logs.Add(new StandardWtsTaskChangeLog
                {
                    TaskId = task.TaskId,
                    FieldName = fieldName,
                    OldValue = oldVal,
                    NewValue = newVal,
                    Reason = reason,
                    ChangedAt = now,
                    ChangedBy = currentUserId
                });
            }
        }

        CheckAndLog("Mã công việc", task.TaskCode, newCode);
        CheckAndLog("Tên công việc", task.TaskName, updatedModel.TaskName);
        CheckAndLog("Mã nhóm", task.CategoryCode, updatedModel.CategoryCode);
        CheckAndLog("Tên nhóm", task.CategoryName, updatedModel.CategoryName);
        CheckAndLog("Đơn vị tính", task.DefaultUnit, updatedModel.DefaultUnit);
        CheckAndLog("Thời gian chuẩn (s)", task.StandardTimeSec?.ToString(), updatedModel.StandardTimeSec?.ToString());
        CheckAndLog("Ghi chú", task.GhiChu, updatedModel.GhiChu);
        // Log thay đổi IsProductiveTask
        CheckAndLog("Phân loại có ích",
            task.IsProductiveTask ? "Có ích" : "Vô ích",
            updatedModel.IsProductiveTask ? "Có ích" : "Vô ích");

        if (logs.Any())
        {
            task.TaskCode = newCode;
            task.TaskName = updatedModel.TaskName.Trim();
            task.CategoryCode = updatedModel.CategoryCode.Trim().ToUpper();
            task.CategoryName = updatedModel.CategoryName.Trim();
            task.DefaultUnit = string.IsNullOrWhiteSpace(updatedModel.DefaultUnit) ? "Chi tiết" : updatedModel.DefaultUnit.Trim();
            task.StandardTimeSec = updatedModel.StandardTimeSec;
            task.DisplayOrder = updatedModel.DisplayOrder;
            task.GhiChu = updatedModel.GhiChu?.Trim();
            task.IsProductiveTask = updatedModel.IsProductiveTask;
            task.UpdatedAt = now;
            task.UpdatedBy = currentUserId;

            _db.StandardWtsTaskChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    /// <summary>
    /// Lưu tổng nhiều thay đổi IsProductiveTask cùng 1 lần (bulk save từ UI).
    /// Xác thực PIN 1 lần duy nhất, ghi ChangeLog cho từng item thực sự thay đổi.
    /// </summary>
    public async Task<(bool Success, string? Error)> BulkUpdateProductiveAsync(
        List<(int TaskId, bool NewValue)> changes, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");
        if (!changes.Any()) return (true, null);

        var taskIds = changes.Select(c => c.TaskId).ToList();
        var tasks = await _db.StandardWtsTasks
            .Where(x => taskIds.Contains(x.TaskId) && x.IsActive)
            .ToListAsync();

        var now = DateTime.Now;
        var logs = new List<StandardWtsTaskChangeLog>();

        foreach (var (taskId, newValue) in changes)
        {
            var task = tasks.FirstOrDefault(x => x.TaskId == taskId);
            if (task == null) continue;
            if (task.IsProductiveTask == newValue) continue; // không thay đổi thực sự

            var oldLabel = task.IsProductiveTask ? "Có ích" : "Vô ích";
            var newLabel = newValue ? "Có ích" : "Vô ích";

            task.IsProductiveTask = newValue;
            task.UpdatedAt = now;
            task.UpdatedBy = currentUserId;

            logs.Add(new StandardWtsTaskChangeLog
            {
                TaskId    = task.TaskId,
                FieldName = "Phân loại có ích",
                OldValue  = oldLabel,
                NewValue  = newLabel,
                Reason    = "Cập nhật hàng loạt từ bảng danh mục",
                ChangedAt = now,
                ChangedBy = currentUserId
            });
        }

        if (logs.Any())
        {
            _db.StandardWtsTaskChangeLogs.AddRange(logs);
            await _db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SoftDeleteAsync(int taskId, int currentUserId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return (false, "Vui lòng nhập mã PIN xác nhận.");
        bool isPinValid = await _authSvc.VerifyPinAsync(currentUserId, pin);
        if (!isPinValid) return (false, "Mã PIN xác nhận không chính xác.");

        var task = await _db.StandardWtsTasks.FirstOrDefaultAsync(x => x.TaskId == taskId && x.IsActive);
        if (task == null) return (false, "Không tìm thấy công việc cần xóa.");

        var now = DateTime.Now;
        task.IsActive = false;
        task.UpdatedAt = now;
        task.UpdatedBy = currentUserId;

        _db.StandardWtsTaskChangeLogs.Add(new StandardWtsTaskChangeLog
        {
            TaskId = task.TaskId,
            FieldName = "Trạng thái",
            OldValue = "Đang áp dụng",
            NewValue = "Đã xóa khỏi danh mục tiêu chuẩn",
            Reason = "Xóa công việc tiêu chuẩn",
            ChangedAt = now,
            ChangedBy = currentUserId
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<StandardWtsTaskChangeLog>> GetChangeLogsAsync(int taskId)
    {
        return await _db.StandardWtsTaskChangeLogs
            .Include(x => x.ChangedByUser)
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.ChangedAt)
            .ToListAsync();
    }

    public async Task<byte[]> ExportExcelAsync(List<StandardWtsTask> data)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("DS WTS tieu chuan");

        ws.Cell("A1").Value = "DANH MỤC CÔNG VIỆC TIÊU CHUẨN (WORK TIME SHEET)";
        ws.Range("A1:G1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(XLColor.FromHtml("#1e3a8a"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell("A2").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Tổng số: {data.Count} công việc tiêu chuẩn";
        ws.Range("A2:G2").Merge().Style
            .Font.SetItalic(true)
            .Font.SetFontSize(10)
            .Font.SetFontColor(XLColor.FromHtml("#64748b"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "STT", "Nhóm công đoạn", "Mã công việc", "Tên công việc tiêu chuẩn", "Đơn vị tính", "Phân loại", "Ghi chú" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#2b7bc4"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        ws.Row(4).Height = 25;

        int r = 5;
        int stt = 1;
        foreach (var t in data)
        {
            ws.Cell(r, 1).SetValue(stt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 2).SetValue(t.CategoryName).Style.Font.SetBold(true);
            ws.Cell(r, 3).SetValue(t.TaskCode).Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#dc2626")).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 4).SetValue(t.TaskName);
            ws.Cell(r, 5).SetValue(t.DefaultUnit).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 6).SetValue(t.IsProductiveTask ? "Có ích" : "Vô ích").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 7).SetValue(t.GhiChu ?? "");

            ws.Row(r).Height = 20;
            r++;
            stt++;
        }

        var dataRange = ws.Range(4, 1, r - 1, 7);
        dataRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
