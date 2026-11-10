#!/bin/bash
# mk.sh <src MES.Web> <dest dir> <port>  — dựng harness chạy cả MES.Web trên EF giả
set -e
SRC="$1"; DST="$2"; PORT="$3"
H="$(cd "$(dirname "$0")" && pwd)"
TOOLS="$H/../GiaoNhanFuzz"
rm -rf "$DST"; mkdir -p "$DST"
cp -r "$SRC/." "$DST/"; rm -rf "$DST/bin" "$DST/obj" "$DST/Data/AppDbContext - Copy.bak"
rm -f "$DST/Services/PmSaveLockSql.cs"   # [S8D-KHOA 07/11] khoá lượt lưu PM trên kết nối SQL riêng (Microsoft.Data.SqlClient) — harness EF giả dùng cách cũ
mkdir -p "$DST/Stubs"
cp "$TOOLS/EfStub.cs" "$TOOLS/EfCompile2.cs" "$DST/Stubs/"
# [KHO-5 02/11] ClosedXML tối thiểu ghi .xlsx thật (thay XlStub chỉ-để-biên-dịch) — drive_kho.py đọc file Excel trang Kho xuất ra
cp "$H/Stubs/XlMini.cs" "$DST/Stubs/"
cp "$H/Stubs/WebStub.cs" "$H/Stubs/HarnessFixup.cs" "$H/Stubs/HarnessSeed.cs" "$H/Stubs/HarnessLedger.cs" "$H/Stubs/HarnessFail.cs" "$H/Stubs/HarnessN3f.cs" "$H/Stubs/HarnessSua.cs" "$H/Stubs/HarnessG13.cs" "$DST/Stubs/"   # [N3-FIRST 04/11] HarnessN3f = dữ liệu KH-N3F (drive_n3f.py) · [KIEM-CHUA 03/11] HarnessFail = giả lỗi máy chủ + dữ liệu KH-PE (drive_pageerr.py)
# DbContext giả: thêm ctor nhận options
python3 - "$DST" <<'PY'
import sys,re
d=sys.argv[1]
p=d+'/Stubs/EfStub.cs'; s=open(p).read()
s=s.replace("public DbContext() { Database = new DatabaseFacade(this); ChangeTracker = new ChangeTracker(this); }",
            "public DbContext() { Database = new DatabaseFacade(this); ChangeTracker = new ChangeTracker(this); }\n        public DbContext(DbContextOptions o) : this() { }\n        protected virtual void OnModelCreating(ModelBuilder mb) { }")
s=s.replace("public void AddRange(IEnumerable<T> es) => Added.AddRange(es);", "public void AddRange(IEnumerable<T> es) => Added.AddRange(es);\n        public void AddRange(params T[] es) => Added.AddRange(es);\n        public List<T> Local => List;")
assert "params T[] es" in s
# ket noi gia cho code cu (GetDbConnection)
s=s.replace("public static System.Data.Common.DbConnection GetDbConnection(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => null!;",
            "public static System.Data.Common.DbConnection GetDbConnection(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => HarnessFake.FakeConn.Instance;")
s=s.replace("public static Task OpenConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, CancellationToken ct = default) => Task.CompletedTask;",
            "public static Task OpenConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, CancellationToken ct = default) { HarnessFake.FakeConn.Instance.Open(); return Task.CompletedTask; }")
s=s.replace("public static Task CloseConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => Task.CompletedTask;",
            "public static Task CloseConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d) { HarnessFake.FakeConn.Instance.Close(); return Task.CompletedTask; }")
open(p,'w').write(s)
p=d+'/Stubs/EfCompile2.cs'; s=open(p).read()
s=s.replace("public static System.Data.Common.DbConnection GetDbConnection(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => null!;",
            "public static System.Data.Common.DbConnection GetDbConnection(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => HarnessFake.FakeConn.Instance;")
s=s.replace("public static Task OpenConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, CancellationToken ct = default) => Task.CompletedTask;",
            "public static Task OpenConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, CancellationToken ct = default) { HarnessFake.FakeConn.Instance.Open(); return Task.CompletedTask; }")
s=s.replace("public static Task CloseConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => Task.CompletedTask;",
            "public static Task CloseConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d) { HarnessFake.FakeConn.Instance.Close(); return Task.CompletedTask; }")
assert "FakeConn" in s
open(p,'w').write(s)
p=d+'/Data/AppDbContext.cs'; s=open(p).read()
s=s.replace("    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }",
            "    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }\n    public override void Fixup() => HarnessFixup.Run(this);   // [harness]")
s=re.sub(r'public DbSet<(\w+)>\s+(\w+)\s*\{\s*get;\s*set;\s*\}(\s*=\s*null!;)?', r'public DbSet<\1> \2 => Set<\1>();', s)
assert "HarnessFixup" in s
open(p,'w').write(s)
p=d+'/MES.Web.csproj'; s=open(p).read()
s=re.sub(r'<PackageReference[^>]*/>','',s); s=re.sub(r'<PackageReference[^>]*>.*?</PackageReference>','',s,flags=re.S)
s=s.replace("<RootNamespace>MES.Web</RootNamespace>","<RootNamespace>MES.Web</RootNamespace>\n    <NoWarn>CS8618;CS8602;CS8600;CS8604;CS8601;CS8603;CS8625;CS0168;CS0219;CS1998;CS8619;CS8620;CS0108;CS0114;CS1591;CS8632;CS8629;CS8634</NoWarn>")
open(p,'w').write(s)
p=d+'/Program.cs'; s=open(p).read()
s=s.replace("        SeedData.EnsureSeeded(db);","        SeedData.EnsureSeeded(db);\n        await HarnessSeed.RunAsync(db);   // [harness]")
s=s.replace("PartStepGuard.SessionLockOpener = PmSaveLockSql.OpenAsync;","// [harness][S8D-KHOA] EF giả: khoá lượt lưu theo cách cũ (kết nối DbContext)")   # [S8D-KHOA 07/11]
s=s.replace("app.UseStaticFiles();","app.UseStaticFiles();\napp.MapGet(\"/harness/dump\", (AppDbContext d) => Results.Json(HarnessSeed.Dump(d)));   // [harness]\napp.MapGet(\"/harness/dump2\", (AppDbContext d) => Results.Json(HarnessSeed.Dump2(d)));   // [harness][ML9-DUP] Part Master drive_ml9 / drive_std\napp.MapGet(\"/harness/ledger\", async (HandoverLedgerService l, AppDbContext d) => Results.Json(await HarnessLedger.SnapshotAsync(l, d)));   // [harness][WTS-PANEL-QTY]\napp.MapPost(\"/harness/kho-seed\", async (AppDbContext d) => Results.Json(await HarnessSeed.SeedKhoAsync(d)));   // [harness][KHO-5] dữ liệu trang Kho dựng khi drive_kho.py gọi\napp.MapGet(\"/harness/kho\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSeed.DumpKhoAsync(d, l)));   // [harness][KHO-5]\napp.MapPost(\"/harness/fail\", (string? kind, int? n) => Results.Json(HarnessFail.Set(kind ?? \"off\", n ?? 1)));   // [harness][KIEM-CHUA] giả lỗi máy chủ\napp.MapGet(\"/harness/fail\", () => Results.Json(HarnessFail.Stat()));   // [harness][KIEM-CHUA]\napp.MapPost(\"/harness/pe-seed\", async (AppDbContext d) => Results.Json(await HarnessFail.SeedPeAsync(d)));   // [harness][KIEM-CHUA] KH-PE\napp.MapPost(\"/harness/n3f-seed\", async (AppDbContext d) => Results.Json(await HarnessN3f.SeedAsync(d)));   // [harness][N3-FIRST] KH-N3F drive_n3f.py\napp.MapGet(\"/harness/n3f\", (AppDbContext d) => Results.Json(HarnessN3f.Dump(d)));   // [harness][N3-FIRST]\napp.MapPost(\"/harness/p0311-seed\", async (AppDbContext d) => Results.Json(await HarnessSeed.SeedP0311Async(d)));   // [harness][PHAT-HIEN-0311] drive_p0311.py\napp.MapGet(\"/harness/p0311\", (AppDbContext d) => Results.Json(HarnessSeed.DumpP0311(d)));   // [harness][PHAT-HIEN-0311]\napp.MapPost(\"/harness/sua-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedAsync(d)));   // [harness][GC-SUA-TAI-CHO] drive_sua.py\napp.MapGet(\"/harness/sua\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSua.DumpAsync(d, l)));   // [harness][GC-SUA-TAI-CHO]\napp.MapGet(\"/harness/form\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSua.DumpFormAsync(d, l)));   // [harness][GN-FORM1] drive_form.py\napp.MapPost(\"/harness/form-recv\", async (AppDbContext d) => Results.Json(await HarnessSua.RecvKhoAsync(d)));   // [harness][GN-FORM1] drive_form.py D6\napp.MapPost(\"/harness/nhannc-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedNhanNcAsync(d)));   // [harness][NHAN-DOI-O-NC] drive_nhannc.py\napp.MapGet(\"/harness/nhannc\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSua.DumpNhanNcAsync(d, l)));   // [harness][NHAN-DOI-O-NC]\napp.MapPost(\"/harness/taro-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedTaroAsync(d)));   // [harness][TARO-NC-MA] drive_taro.py\napp.MapGet(\"/harness/taro\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSua.DumpTaroAsync(d, l)));   // [harness][TARO-NC-MA]\napp.MapPost(\"/harness/g5-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedG5Async(d)));   // [harness][TARO-DUPHONG][TARO-DOI-CHEO-NC] drive_g5.py\napp.MapPost(\"/harness/g6-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedG6Async(d)));   // [harness][TARO-LAMHO-NC] drive_g6.py\napp.MapPost(\"/harness/g9-seed\", async (AppDbContext d) => Results.Json(await HarnessSua.SeedG9Async(d)));   // [harness][WTS-SUA-NHOM][GOI9-OFF-CANHBAO] drive_g9.py\napp.MapGet(\"/harness/g6\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessSua.DumpG6Async(d, l)));   // [harness][TARO-LAMHO-NC]")
s=s.replace("app.MapGet(\"/harness/dump\",","app.MapGet(\"/harness/applocks\", () => Results.Json(new { raw = Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Where(x => x.Contains(\"applock\")).ToList(), session = Microsoft.EntityFrameworkCore.DatabaseFacade.SessionLocks.ToList(), connOpen = Microsoft.EntityFrameworkCore.DatabaseFacade.ConnOpen }));   // [harness][PM-SNAP-MID] drive_dpma.py\napp.MapPost(\"/harness/b6-seed\", async (AppDbContext d) => Results.Json(await HarnessSeed.SeedB6Async(d)));   // [harness][B6-DP-THAP] drive_dpma.py\napp.MapGet(\"/harness/b6\", (AppDbContext d) => Results.Json(HarnessSeed.DumpB6(d)));   // [harness][B6-DP-THAP]\napp.MapPost(\"/harness/b7-seed\", async (AppDbContext d) => Results.Json(await HarnessSeed.SeedB7Async(d)));   // [harness][B7-TIMING-MAY] drive_dpma.py\napp.MapGet(\"/harness/dump\",",1)
s=s.replace("app.MapGet(\"/harness/dump\",","app.MapPost(\"/harness/g13-seed\", async (AppDbContext d) => Results.Json(await HarnessG13.SeedAsync(d)));   // [harness][GÓI 13] drive_g13.py\napp.MapGet(\"/harness/g13\", async (AppDbContext d, HandoverLedgerService l) => Results.Json(await HarnessG13.DumpAsync(d, l)));   // [harness][GÓI 13]\napp.MapGet(\"/harness/dump\",",1)
assert "HarnessSeed.RunAsync" in s and "/harness/dump" in s
open(p,'w').write(s)
p=d+'/appsettings.json'; s=open(p).read()
s=s.replace('"Url": "http://0.0.0.0:5000"', '"Url": "http://127.0.0.1:'+sys.argv[2]+'"') if len(sys.argv)>2 else s
open(p,'w').write(s)
PY
python3 - "$DST" "$PORT" <<'PY'
import sys; d=sys.argv[1]; p=d+'/appsettings.json'; s=open(p).read()
s=s.replace('"Url": "http://0.0.0.0:5000"', '"Url": "http://127.0.0.1:'+sys.argv[2]+'"'); open(p,'w').write(s)
PY
cp "$TOOLS/nuget.config" "$DST/"
echo "harness tai $DST (port $PORT)"
