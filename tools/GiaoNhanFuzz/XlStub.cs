// [KH-GUARD] Stub thêm (chỉ để biên dịch) cho KhPlanService / KhoVatLieuService.
// Stub CHỈ để biên dịch (ClosedXML)
namespace ClosedXML.Excel
{
    public enum XLAlignmentHorizontalValues { Center, Left, Right, General, Justify }
    public enum XLAlignmentVerticalValues { Center, Top, Bottom }
    public enum XLBorderStyleValues { Thin, Medium, Hair, None, Thick, Dashed, Dotted }
    public enum XLDataType { DateTime, Text, Number, Boolean, Blank }
    public class XLColor { public static dynamic FromHtml(string s) => new XLColor(); public static XLColor White => new(); public static XLColor Red => new(); public static XLColor Green => new(); public static XLColor Gray => new(); public static XLColor LightGray => new(); public static XLColor Black => new(); public static XLColor Yellow => new(); public static XLColor NoColor => new(); }
    public interface IXLCell { dynamic Value { get; set; } dynamic Style { get; } dynamic SetValue(object? v); T GetValue<T>(); string GetString(); dynamic DataType { get; } dynamic FormulaA1 { get; set; } dynamic Worksheet { get; } }
    public interface IXLWorksheet { dynamic Cell(int r, int c); dynamic Cell(string a); dynamic Range(int r1, int c1, int r2, int c2); dynamic Range(string a); dynamic Column(int c); dynamic Column(string c); dynamic Columns(); dynamic Columns(int a, int b); dynamic Row(int r); dynamic Rows(); dynamic RowsUsed(); dynamic SheetView { get; } dynamic Style { get; } dynamic LastRowUsed(); dynamic LastColumnUsed(); dynamic Name { get; set; } }
    public class XLWorkbook : IDisposable { public XLWorkbook() { } public XLWorkbook(System.IO.Stream s) { } public XLWorkbook(string p) { } public dynamic Worksheets => null!; public IXLWorksheet AddWorksheet(string n) => null!; public IXLWorksheet Worksheet(int i) => null!; public IXLWorksheet Worksheet(string n) => null!; public void SaveAs(System.IO.Stream s) { } public void SaveAs(string p) { } public void Dispose() { } }
}
