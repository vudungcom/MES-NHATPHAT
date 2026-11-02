// [KIEM-TONG 2026-11-01] "GiaoNhanFuzz danhmuc": đối chiếu tools/DanhMucKiem.txt (sổ phép kiểm) với code MES.Web + bộ kiểm.
//   - tag [TAG] trong MES.Web chưa có dòng ở sổ                → THIẾU DANH MỤC (cảnh báo; DANHMUC_CHAN=1 → SAI)
//   - dòng sổ trỏ tới ca không có (kịch bản / ca / file / bước) → CA KHÔNG CÓ   (luôn SAI: sổ nói có kiểm mà thật không có)
//   - mã ở mục tag mà code đã bỏ                               → TAG ĐÃ BỎ     (cảnh báo)
//   - dòng CHUA                                                → đếm việc còn phải viết ca kiểm
// Chỉ đọc file, không đụng DB. Tìm gốc repo bằng cách đi ngược lên tới khi thấy thư mục MES.Web.
using System.Text.RegularExpressions;

static class DanhMuc
{
    // kịch bản → file chứa ca (mã ca in ở đầu ca dạng "[W12]")
    static readonly Dictionary<string, string> ScenFile = new()
    {
        ["kh"] = "KhScen.cs", ["loi"] = "LoiScen.cs", ["vu"] = "VuScen.cs", ["chuoi"] = "ChuoiScen.cs", ["wts"] = "WtsScen.cs",
        ["quyen"] = "QuyenScen.cs", ["iso"] = "IsoScen.cs", ["quet"] = "QuetScen.cs", ["nghi"] = "NghiScen.cs", ["viec"] = "ViecScen.cs",
        ["auth"] = "AuthScen.cs", ["scen"] = "Scen.cs",
    };
    static readonly HashSet<string> Plain = new() { "nang", "giaodien", "chung", "CHUA" };

    public static int Run()
    {
        var root = FindRoot();
        if (root == null) { Console.WriteLine("DANH MỤC: không tìm thấy thư mục MES.Web (chạy từ trong repo)"); return 1; }
        var tools = Path.Combine(root, "tools");
        var fuzz = Path.Combine(tools, "GiaoNhanFuzz");
        var harness = Path.Combine(tools, "WtsHarness");
        var file = Path.Combine(tools, "DanhMucKiem.txt");
        if (!File.Exists(file)) { Console.WriteLine("DANH MỤC: thiếu tools/DanhMucKiem.txt"); return 1; }

        // 1. tag trong code
        var tagRx = new Regex(@"\[([A-Z][A-Z0-9]*(?:-[A-Za-z0-9]+)+|[A-Z]{2,}[0-9]*)(?:\]| [0-9])");   // [TAG] hoặc [TAG dd/mm…]
        var codeTags = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "MES.Web"), "*.*", SearchOption.AllDirectories))
        {
            if (!(f.EndsWith(".cs") || f.EndsWith(".razor"))) continue;
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            foreach (Match m in tagRx.Matches(File.ReadAllText(f)))
            {
                if (!codeTags.TryGetValue(m.Groups[1].Value, out var set)) codeTags[m.Groups[1].Value] = set = new();
                set.Add(Path.GetFileNameWithoutExtension(f));
            }
        }

        // 2. đọc sổ
        var scenText = new Dictionary<string, string>();
        string ScenSrc(string sc)
        {
            if (!scenText.TryGetValue(sc, out var t))
            {
                var p = Path.Combine(fuzz, ScenFile[sc]);
                scenText[sc] = t = File.Exists(p) ? File.ReadAllText(p) : "";
            }
            return t;
        }
        var entries = new Dictionary<string, (int Line, List<string> Refs, string Section)>(StringComparer.Ordinal);
        var broken = new List<string>(); var dup = new List<string>(); var chua = new List<string>();
        string section = "";
        int ln = 0;
        foreach (var raw in File.ReadAllLines(file))
        {
            ln++;
            var line = raw.Trim();
            if (line.StartsWith("# ═══")) { section = line; continue; }
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) { broken.Add($"dòng {ln}: sai cú pháp (MÃ | ca kiểm | ghi chú): {line}"); continue; }
            var code = parts[0].Trim();
            var refs = parts[1].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (entries.ContainsKey(code)) { dup.Add($"{code} (dòng {entries[code].Line} và {ln})"); continue; }
            entries[code] = (ln, refs, section);
            if (refs.Count == 0) broken.Add($"{code} (dòng {ln}): không ghi ca kiểm nào (ghi CHUA nếu chưa có)");
            foreach (var r in refs)
            {
                var err = CheckRef(r, ScenSrc, harness);
                if (err != null) broken.Add($"{code} (dòng {ln}): {r} — {err}");
                if (r == "CHUA") chua.Add($"{code}: {(parts.Length > 2 ? parts[2].Trim() : "")}");
            }
        }

        bool isRule(string c) => Regex.IsMatch(c, @"^[LNK][0-9]+$");
        var missing = codeTags.Keys.Where(t => !entries.ContainsKey(t)).ToList();
        var removed = entries.Where(e => !isRule(e.Key) && !codeTags.ContainsKey(e.Key) && !e.Value.Section.Contains("ĐÃ BỎ")).Select(e => $"{e.Key} (dòng {e.Value.Line})").ToList();
        int covered = codeTags.Keys.Count(t => entries.TryGetValue(t, out var e) && e.Refs.Any(r => r != "CHUA" && r != "giaodien" && r != "chung"));

        Console.WriteLine($"DANH MỤC KIỂM: {codeTags.Count} tag trong code · {entries.Count} dòng sổ · {covered} tag có ca kiểm thật · {chua.Count} dòng CHUA");
        foreach (var b in broken) Console.WriteLine($"  SAI  CA KHÔNG CÓ: {b}");
        foreach (var d in dup) Console.WriteLine($"  SAI  TRÙNG MÃ: {d}");
        foreach (var m in missing) Console.WriteLine($"  CẢNH BÁO  THIẾU DANH MỤC: [{m}] ở {string.Join(", ", codeTags[m])}");
        foreach (var r in removed) Console.WriteLine($"  CẢNH BÁO  TAG ĐÃ BỎ khỏi code: {r}");
        if (chua.Any())
        {
            Console.WriteLine($"  VIỆC CÒN PHẢI VIẾT CA KIỂM ({chua.Count}):");
            foreach (var c in chua) Console.WriteLine($"    - {c}");
        }
        bool chan = Environment.GetEnvironmentVariable("DANHMUC_CHAN") == "1";
        int fail = broken.Count + dup.Count + (chan ? missing.Count : 0);
        Console.WriteLine(fail == 0 ? "KẾT QUẢ DANH MỤC: ĐẠT" + (missing.Any() ? $" (còn {missing.Count} tag chưa vào sổ — cảnh báo)" : "") : $"KẾT QUẢ DANH MỤC: {fail} SAI");
        return fail == 0 ? 0 : 1;
    }

    static string? CheckRef(string r, Func<string, string> scenSrc, string harness)
    {
        if (Plain.Contains(r)) return null;
        if (r.StartsWith("harness:"))
        {
            var rest = r.Substring(8).Split(':', 2);
            var p = Path.Combine(harness, rest[0]);
            if (!File.Exists(p)) return "không có file trong tools/WtsHarness";
            if (rest.Length == 2 && !File.ReadAllText(p).Contains(rest[1] + " ")) return $"không thấy bước \"{rest[1]}\" trong {rest[0]}";
            return null;
        }
        var sp = r.Split(':', 2);
        if (!ScenFile.ContainsKey(sp[0])) return "không phải kịch bản / loại ca đã biết";
        var src = scenSrc(sp[0]);
        if (src.Length == 0) return $"chưa có file {ScenFile[sp[0]]}";
        if (sp.Length == 2 && !src.Contains("[" + sp[1] + "]")) return $"không thấy ca [{sp[1]}] trong {ScenFile[sp[0]]}";
        return null;
    }

    static string? FindRoot()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d != null) { if (Directory.Exists(Path.Combine(d.FullName, "MES.Web")) && Directory.Exists(Path.Combine(d.FullName, "tools"))) return d.FullName; d = d.Parent; }
        return null;
    }
}
