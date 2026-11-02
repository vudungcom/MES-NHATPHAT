// [KHO-5 harness 02/11/2026] ClosedXML tối thiểu cho harness (thay tools/GiaoNhanFuzz/XlStub.cs chỉ-để-biên-dịch): ghi được file .xlsx THẬT
//   (chỉ ô + chữ / số, bỏ định dạng) để drive_kho.py đọc file Excel trang Kho xuất ra bằng zipfile / xml và so với số trên trang.
//   Cùng khai báo công khai như XlStub.cs (code MES.Web biên dịch y như cũ). Định dạng (Style / Merge / AdjustToContents) nhận rồi bỏ qua.
//   Chỉ dựng phần ExportToExcel trang Kho dùng; đọc file (new XLWorkbook(stream).Worksheet(1)) vẫn trả null như stub cũ.
using System.Dynamic;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace ClosedXML.Excel
{
    public enum XLAlignmentHorizontalValues { Center, Left, Right, General, Justify }
    public enum XLAlignmentVerticalValues { Center, Top, Bottom }
    public enum XLBorderStyleValues { Thin, Medium, Hair, None, Thick, Dashed, Dotted }
    public enum XLDataType { DateTime, Text, Number, Boolean, Blank }
    public class XLColor { public static dynamic FromHtml(string s) => new XLColor(); public static XLColor White => new(); public static XLColor Red => new(); public static XLColor Green => new(); public static XLColor Gray => new(); public static XLColor LightGray => new(); public static XLColor Black => new(); public static XLColor Yellow => new(); public static XLColor NoColor => new(); }
    public interface IXLCell { dynamic Value { get; set; } dynamic Style { get; } dynamic SetValue(object? v); T GetValue<T>(); string GetString(); dynamic DataType { get; } dynamic FormulaA1 { get; set; } dynamic Worksheet { get; } }
    public interface IXLWorksheet { dynamic Cell(int r, int c); dynamic Cell(string a); dynamic Range(int r1, int c1, int r2, int c2); dynamic Range(string a); dynamic Column(int c); dynamic Column(string c); dynamic Columns(); dynamic Columns(int a, int b); dynamic Row(int r); dynamic Rows(); dynamic RowsUsed(); dynamic SheetView { get; } dynamic Style { get; } dynamic LastRowUsed(); dynamic LastColumnUsed(); dynamic Name { get; set; } }

    /// <summary>Nhận mọi thuộc tính / lệnh định dạng rồi bỏ qua.</summary>
    public class HxSink : DynamicObject
    {
        public override bool TryGetMember(GetMemberBinder b, out object? r) { r = this; return true; }
        public override bool TrySetMember(SetMemberBinder b, object? v) => true;
        public override bool TryInvokeMember(InvokeMemberBinder b, object?[]? a, out object? r) { r = this; return true; }
    }

    public class HxCell : IXLCell
    {
        readonly HxSheet _ws; public HxCell(HxSheet ws) { _ws = ws; }
        public dynamic Value { get; set; } = null!;
        public dynamic Style => new HxSink();
        public dynamic SetValue(object? v) { Value = v!; return this; }
        public T GetValue<T>() => (T)Convert.ChangeType(Value, typeof(T), CultureInfo.InvariantCulture);
        public string GetString() => Convert.ToString((object?)Value, CultureInfo.InvariantCulture) ?? "";
        public dynamic DataType => XLDataType.Text;
        public dynamic FormulaA1 { get; set; } = null!;
        public dynamic Worksheet => _ws;
    }

    public class HxRange
    {
        readonly HxSheet _ws; readonly int _r1, _c1;
        public HxRange(HxSheet ws, int r1, int c1) { _ws = ws; _r1 = r1; _c1 = c1; }
        public HxRange Merge() => this;
        public HxRange Merge(bool b) => this;
        public dynamic Value { get => _ws.Cell(_r1, _c1).Value; set => _ws.Cell(_r1, _c1).Value = value; }   // ô gộp: giá trị ở ô trái trên
        public dynamic Style => new HxSink();
    }

    public class HxSheet : IXLWorksheet
    {
        internal readonly SortedDictionary<(int r, int c), HxCell> Cells = new();
        public HxSheet(string name) { Name = name; }
        internal static (int r, int c) Addr(string a)
        {
            int c = 0, i = 0;
            while (i < a.Length && char.IsLetter(a[i])) { c = c * 26 + (char.ToUpperInvariant(a[i]) - 'A' + 1); i++; }
            return (int.Parse(a[i..], CultureInfo.InvariantCulture), c);
        }
        internal static string Col(int c) { var s = ""; while (c > 0) { c--; s = (char)('A' + c % 26) + s; c /= 26; } return s; }
        public dynamic Cell(int r, int c) { if (!Cells.TryGetValue((r, c), out var x)) Cells[(r, c)] = x = new HxCell(this); return x; }
        public dynamic Cell(string a) { var (r, c) = Addr(a); return Cell(r, c); }
        public dynamic Range(int r1, int c1, int r2, int c2) => new HxRange(this, r1, c1);
        public dynamic Range(string a) { var (r, c) = Addr(a.Split(':')[0]); return new HxRange(this, r, c); }
        public dynamic Column(int c) => new HxSink();
        public dynamic Column(string c) => new HxSink();
        public dynamic Columns() => new HxSink();
        public dynamic Columns(int a, int b) => new HxSink();
        public dynamic Row(int r) => new HxSink();
        public dynamic Rows() => new HxSink();
        public dynamic RowsUsed() => new HxSink();
        public dynamic SheetView => new HxSink();
        public dynamic Style => new HxSink();
        public dynamic LastRowUsed() => new HxSink();
        public dynamic LastColumnUsed() => new HxSink();
        public dynamic Name { get; set; }

        internal string Xml()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            foreach (var row in Cells.Where(kv => (object?)kv.Value.Value != null).GroupBy(kv => kv.Key.r))
            {
                sb.Append($"<row r=\"{row.Key}\">");
                foreach (var kv in row.OrderBy(k => k.Key.c))
                {
                    var a = Col(kv.Key.c) + kv.Key.r; object v = kv.Value.Value;
                    if (v is int or long or decimal or double or float or short or byte)
                        sb.Append($"<c r=\"{a}\"><v>{Convert.ToString(v, CultureInfo.InvariantCulture)}</v></c>");
                    else if (v is bool bv)
                        sb.Append($"<c r=\"{a}\" t=\"b\"><v>{(bv ? 1 : 0)}</v></c>");
                    else
                        sb.Append($"<c r=\"{a}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(Convert.ToString(v, CultureInfo.InvariantCulture))}</t></is></c>");
                }
                sb.Append("</row>");
            }
            return sb.Append("</sheetData></worksheet>").ToString();
        }
    }

    public class HxSheets
    {
        readonly XLWorkbook _wb; public HxSheets(XLWorkbook wb) { _wb = wb; }
        public HxSheet Add(string name) => (HxSheet)_wb.AddWorksheet(name);
    }

    public class XLWorkbook : IDisposable
    {
        readonly List<HxSheet> _sheets = new();
        public XLWorkbook() { }
        public XLWorkbook(System.IO.Stream s) { }
        public XLWorkbook(string p) { }
        public dynamic Worksheets => new HxSheets(this);
        public IXLWorksheet AddWorksheet(string n) { var s = new HxSheet(n); _sheets.Add(s); return s; }
        public IXLWorksheet Worksheet(int i) => i >= 1 && i <= _sheets.Count ? _sheets[i - 1] : null!;
        public IXLWorksheet Worksheet(string n) => _sheets.FirstOrDefault(s => (string)s.Name == n)!;
        public void SaveAs(string p) { using var f = System.IO.File.Create(p); SaveAs(f); }
        public void SaveAs(System.IO.Stream s)
        {
            const string M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            using var z = new ZipArchive(s, ZipArchiveMode.Create, leaveOpen: true);
            void Put(string path, string text) { using var w = new System.IO.StreamWriter(z.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(text); }
            var ov = string.Concat(_sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
            Put("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" + ov + "</Types>");
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + R + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put("xl/workbook.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"{M}\" xmlns:r=\"{R}\"><sheets>"
                + string.Concat(_sheets.Select((sh, i) => $"<sheet name=\"{SecurityElement.Escape((string)sh.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(_sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{R}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) + "</Relationships>");
            for (int i = 0; i < _sheets.Count; i++) Put($"xl/worksheets/sheet{i + 1}.xml", _sheets[i].Xml());
        }
        public void Dispose() { }
    }
}
