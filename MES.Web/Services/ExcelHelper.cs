using ClosedXML.Excel;
using MES.Web.Services;

namespace MES.Web.Services;

public static class ExcelHelper
{
    /// <summary>
    /// Tạo file Excel template với 2 cột: PO | STD.
    /// Có 3 dòng ví dụ để user hiểu format.
    /// </summary>
    public static byte[] CreateStdUpdateTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Update STD");

        // Header
        ws.Cell(1, 1).Value = "PO";
        ws.Cell(1, 2).Value = "STD (YYYY-MM-DD)";
        var header = ws.Range(1, 1, 1, 2);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Ví dụ
        ws.Cell(2, 1).Value = "PO-EXAMPLE-001";
        ws.Cell(2, 2).Value = "2026-12-31";
        ws.Cell(3, 1).Value = "PO-EXAMPLE-002";
        ws.Cell(3, 2).Value = "2026-11-15";
        ws.Cell(4, 1).Value = "PO-EXAMPLE-003";
        ws.Cell(4, 2).Value = "2027-01-10";

        // Format cột STD as date
        ws.Column(2).Style.NumberFormat.Format = "yyyy-MM-dd";

        // Width
        ws.Column(1).Width = 20;
        ws.Column(2).Width = 20;

        // Note
        ws.Cell(6, 1).Value = "Ghi chú:";
        ws.Cell(6, 1).Style.Font.Bold = true;
        ws.Cell(7, 1).Value = "- Cột PO: điền số PO chính xác như trong hệ thống";
        ws.Cell(8, 1).Value = "- Cột STD: điền ngày deadline mới (định dạng YYYY-MM-DD hoặc date)";
        ws.Cell(9, 1).Value = "- Xóa các dòng ví dụ trước khi upload";
        ws.Cell(10, 1).Value = "- 1 dòng = 1 PO cần sửa STD";

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Đọc file Excel STD update, parse ra list BulkStdRow.
    /// Bỏ qua dòng trống hoặc dòng chứa "EXAMPLE".
    /// </summary>
    public static List<BulkStdRow> ParseStdUpdate(Stream stream, out List<string> errors)
    {
        errors = new List<string>();
        var result = new List<BulkStdRow>();

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        var rowsUsed = ws.RowsUsed().Skip(1);  // skip header

        int rowNum = 1;
        foreach (var row in rowsUsed)
        {
            rowNum++;
            var po = row.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(po)) continue;
            if (po.Contains("EXAMPLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (po == "Ghi chú:" || po.StartsWith("-")) continue;

            var stdCell = row.Cell(2);
            DateTime std;
            try
            {
                if (stdCell.DataType == XLDataType.DateTime)
                {
                    std = stdCell.GetDateTime();
                }
                else
                {
                    var s = stdCell.GetString().Trim();
                    if (!DateTime.TryParse(s, out std))
                    {
                        errors.Add($"Dòng {rowNum}: STD không đọc được ('{s}')");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Dòng {rowNum}: lỗi đọc STD - {ex.Message}");
                continue;
            }

            result.Add(new BulkStdRow
            {
                PurchaseOrder = po,
                NewStd = std
            });
        }

        return result;
    }

    /// <summary>
    /// Xuất danh sách Part Master ra file Excel.
    /// Xuất đúng dữ liệu đang lọc (truyền vào filtered list).
    /// </summary>
    public static byte[] ExportPartMasterList(IEnumerable<PartMasterListRow> rows, string filterLabel)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Part Master");

        // ==== Tiêu đề file ====
        ws.Cell(1, 1).Value = "DANH SÁCH PART MASTER";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 13).Merge();

        ws.Cell(2, 1).Value = $"Bộ lọc: {filterLabel}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        ws.Range(2, 1, 2, 13).Merge();

        ws.Cell(3, 1).Value = $"Xuất ngày: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Cell(3, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
        ws.Range(3, 1, 3, 13).Merge();

        // ==== Header cột (dòng 5) ====
        var headers = new[]
        {
            "STT", "Part No", "Tên chi tiết", "Khách hàng",
            "Mã VL", "Cấu hình phôi", "Ghi chú phôi",
            "KH", "Gia công", "HTSP", "KCS", "Đóng gói", "Tổng OK"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(5, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // ==== Dữ liệu ====
        int rowIdx = 6;
        int stt = 1;
        foreach (var r in rows)
        {
            ws.Cell(rowIdx, 1).Value = stt++;
            ws.Cell(rowIdx, 2).Value = r.PartNo;
            ws.Cell(rowIdx, 3).Value = r.PartName ?? "";
            ws.Cell(rowIdx, 4).Value = r.CustomerName ?? "";
            ws.Cell(rowIdx, 5).Value = r.Material ?? "";
            ws.Cell(rowIdx, 6).Value = r.MaterialConfig ?? "";
            ws.Cell(rowIdx, 7).Value = r.MaterialNote ?? "";

            SetConfirmCell(ws.Cell(rowIdx, 8),  r.IsPlanConfirmed);
            SetConfirmCell(ws.Cell(rowIdx, 9),  r.IsMachiningConfirmed);
            SetConfirmCell(ws.Cell(rowIdx, 10), r.IsHtspConfirmed);
            SetConfirmCell(ws.Cell(rowIdx, 11), r.IsKcsConfirmed);
            SetConfirmCell(ws.Cell(rowIdx, 12), r.IsPkgConfirmed);

            int okCount = (r.IsPlanConfirmed ? 1 : 0) + (r.IsMachiningConfirmed ? 1 : 0)
                        + (r.IsHtspConfirmed ? 1 : 0) + (r.IsKcsConfirmed ? 1 : 0) + (r.IsPkgConfirmed ? 1 : 0);
            ws.Cell(rowIdx, 13).Value = $"{okCount}/5";
            ws.Cell(rowIdx, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(rowIdx, 13).Style.Font.Bold = true;
            ws.Cell(rowIdx, 13).Style.Font.FontColor = okCount == 5 ? XLColor.Green : XLColor.Red;

            ws.Range(rowIdx, 1, rowIdx, 13).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 13).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            if (stt % 2 == 0)
                ws.Range(rowIdx, 1, rowIdx, 13).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f9fa");

            rowIdx++;
        }

        // ==== Căn width ====
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 25;
        ws.Column(4).Width = 30;
        ws.Column(5).Width = 18;
        ws.Column(6).Width = 22;
        ws.Column(7).Width = 25;
        ws.Column(8).Width = 8;
        ws.Column(9).Width = 10;
        ws.Column(10).Width = 8;
        ws.Column(11).Width = 8;
        ws.Column(12).Width = 10;
        ws.Column(13).Width = 10;

        ws.SheetView.FreezeRows(5);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetConfirmCell(IXLCell cell, bool confirmed)
    {
        cell.Value = confirmed ? "OK" : "NG";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = confirmed ? XLColor.Green : XLColor.Red;
    }
}
