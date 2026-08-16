using ClosedXML.Excel;

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
}
