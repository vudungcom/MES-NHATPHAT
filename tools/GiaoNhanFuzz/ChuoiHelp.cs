// [CHUOI 2026-10-29] Đi đủ chuỗi bằng HÀM THẬT: Part Master → tạo KH (CreateAsync + CopyRouteSnapshotAsync như trang Create_Khplan)
//   → Kho nhận phôi (SaveKhoInputAsync) → giao nhận + WTS. Trước đây bộ kiểm đặt sẵn snapshot vào PO, không đi qua Part Master.
// Phần ghi máy đồng dạng (PartMachiningTimings) và bảng máy loại trừ (MachineExcludeSettings) không có service — trang Detail_Partmaster
//   ghi thẳng Db → ở đây chép đúng cách trang ghi (Add + SaveChanges, IsActive=true; xoá = IsActive=false).
using MES.Web.Data;
using MES.Web.Data.Entities;
using MES.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class ChuoiHelp
{
    public const int Admin = 1;   // user 1 nhóm ADMIN: sửa được mọi vùng Part Master (PartMasterPermissionHelper)
    public const string Reason = "kiem chuoi";

    /// <summary>Dữ liệu nền: 6 user (u1 ADMIN), 1 khách, danh mục lỗi, máy loại trừ H04 (tuỳ chọn).</summary>
    public static void SeedBase(AppDbContext db, bool exclH04)
    {
        var adminG = new UserGroup { GroupId = 1, GroupCode = "ADMIN", GroupName = "Admin", IsActive = true };
        db.UserGroups.List.Add(adminG);
        for (int u = 1; u <= 6; u++) db.Users.List.Add(new User { UserId = u, Username = "u" + u, FullName = "U" + u, IsActive = true, Group = u == 1 ? adminG : FuzzPerm.Xuong(db), GroupId = u == 1 ? 1 : FuzzPerm.XuongId });
        db.Customers.List.Add(new Customer { CustomerId = 1, CustomerName = "KH" });
        if (exclH04) db.MachineExcludeSettings.List.Add(new MachineExcludeSetting { Id = 1, SoMay = "H04", AddedAt = DateTime.Now, AddedByUserId = 1 });
        VuHelp.SeedCats(db);
        db.Fixup();
    }

    /// <summary>Hình dáng Part Master muốn dựng (null = ngẫu nhiên theo rnd).</summary>
    public class Shape
    {
        public int Gc = 3;                 // số NC chính
        public int ExclAt = -1;            // NC thứ mấy (0-based) dùng máy loại trừ H04; -1 = không
        public bool DongDang = true;       // có máy đồng dạng
        public bool DuPhong = true;        // có dòng dự phòng (IsBackup)
        public int Taro = 1, Bavia = 1, Rua = 1, Kcs = 1, Pkg = 1;
        public bool TaroTaskCode = true;   // Taro dùng WtsTaskCode (3T) thay vì NC
        public bool Prep = true;           // có mã chuẩn bị 1T/1V/1R
        public bool HtspDuPhong = false;   // dòng dự phòng ở Taro

        public static Shape Random(Random rnd, bool allowExcl)
        {
            var s = new Shape();
            s.Gc = Pick(rnd, (1, 15), (2, 30), (3, 30), (4, 20), (5, 5));
            s.ExclAt = allowExcl && s.Gc >= 2 ? rnd.Next(s.Gc) : -1;
            s.DongDang = rnd.NextDouble() < 0.5; s.DuPhong = rnd.NextDouble() < 0.35;
            s.Taro = Pick(rnd, (0, 15), (1, 55), (2, 30)); s.Bavia = Pick(rnd, (0, 15), (1, 60), (2, 25)); s.Rua = Pick(rnd, (0, 30), (1, 70));
            s.Kcs = Pick(rnd, (0, 10), (1, 60), (2, 30)); s.Pkg = Pick(rnd, (0, 15), (1, 85));
            s.TaroTaskCode = rnd.NextDouble() < 0.6; s.Prep = rnd.NextDouble() < 0.4; s.HtspDuPhong = rnd.NextDouble() < 0.2;
            return s;
        }
        static T Pick<T>(Random r, params (T v, int w)[] xs)
        {
            var tot = xs.Sum(x => x.w); var k = r.Next(tot);
            foreach (var x in xs) { if (k < x.w) return x.v; k -= x.w; }
            return xs[^1].v;
        }
    }

    /// <summary>Tạo Part Master bằng hàm thật theo hình dáng; trả về PartId.</summary>
    public static async Task<int> BuildPartAsync(AppDbContext db, string partNo, Shape s)
    {
        var pm = await new PartMasterService(db).CreatePartMasterAsync(partNo, 1, new Dictionary<string, string>(), Admin, "Part " + partNo);
        var gc = new PartMachiningService(db);
        for (int i = 0; i < s.Gc; i++)
        {
            var nc = $"NC{i + 1:00}";
            var mach = i == s.ExclAt ? "H04" : $"M{i + 1}";
            var stepId = await gc.AddAsync(pm.PartId, new PartMachiningStep { NC = nc, MachineRegistered = mach, Drawing = "DWG-" + nc, FixtureType = "VJ" + (i + 1), ToolType = "D10", TimingMachine = mach, SetupTime = 10 + i, MachiningTime = 5 + i, InspectionTime = 1, PreparationTime = 2, TrialRunTime = 3 }, Admin, Reason);
            if (s.DongDang && i != s.ExclAt && (i % 2 == 0))
                for (int k = 1; k <= 1 + i % 2; k++) await AddTimingLikePage(db, stepId, $"M{i + 1}{(char)('a' + k)}", k);
            if (s.DuPhong && i == 0)
                await gc.AddAsync(pm.PartId, new PartMachiningStep { NC = nc + "-DP-01", MachineRegistered = "D1", IsBackup = true, ParentNC = nc, SetupTime = 12, MachiningTime = 6 }, Admin, Reason);
        }
        var taro = new PartTaroService(db);
        for (int i = 0; i < s.Taro; i++)
            await taro.AddAsync(pm.PartId, new PartTaroStep { NC = $"T{i + 1}", WtsTaskCode = s.TaroTaskCode ? $"{3 + i}T" : null, StepName = "Taro " + (i + 1), StandardTime = 1.5m }, Admin, Reason);
        if (s.HtspDuPhong && s.Taro > 0)
            await taro.AddAsync(pm.PartId, new PartTaroStep { NC = "T1-DP", WtsTaskCode = s.TaroTaskCode ? "3T-DP" : null, IsBackup = true, ParentNC = "T1", StepName = "Taro DP", StandardTime = 2 }, Admin, Reason);
        if (s.Prep && s.Taro > 0) await taro.AddAsync(pm.PartId, new PartTaroStep { NC = "1T", WtsTaskCode = "1T", StepName = "Chuẩn bị taro", StandardTime = 5 }, Admin, Reason);
        var bavia = new PartBaviaService(db);
        for (int i = 0; i < s.Bavia; i++) await bavia.AddAsync(pm.PartId, new PartBaviaStep { NC = $"B{i + 1}", StepName = "Bavia " + (i + 1), StandardTime = 1 }, Admin, Reason);
        if (s.Prep && s.Bavia > 0) await bavia.AddAsync(pm.PartId, new PartBaviaStep { NC = "1V", StepName = "Chuẩn bị bavia", StandardTime = 4 }, Admin, Reason);
        var rua = new PartWashingService(db);
        for (int i = 0; i < s.Rua; i++) await rua.AddAsync(pm.PartId, new PartWashingStep { NC = $"R{i + 1}", StepName = "Rửa " + (i + 1), StandardTime = 0.5m }, Admin, Reason);
        if (s.Prep && s.Rua > 0) await rua.AddAsync(pm.PartId, new PartWashingStep { NC = "1R", StepName = "Chuẩn bị rửa", StandardTime = 3 }, Admin, Reason);
        var kcs = new PartInspectionService(db);
        for (int i = 0; i < s.Kcs; i++) await kcs.AddAsync(pm.PartId, new PartInspectionStep { NC = $"K{i + 1}", StepName = "KCS " + (i + 1), StandardTime = 2 }, Admin, Reason);
        var pkg = new PartPackagingService(db);
        for (int i = 0; i < s.Pkg; i++) await pkg.AddAsync(pm.PartId, new PartPackagingStep { NC = $"P{i + 1}", StepName = "Đóng gói " + (i + 1), StandardTime = 1 }, Admin, Reason);
        return pm.PartId;
    }

    /// <summary>Máy đồng dạng: ghi y như trang Detail_Partmaster.SaveMachiningTimingsAsync (không có service).</summary>
    public static async Task<long> AddTimingLikePage(AppDbContext db, long stepId, string soMay, int order)
    {
        var ent = new PartMachiningTiming { StepId = stepId, SoMay = soMay, DisplayOrder = order, FixtureType = "VJ-" + soMay, ToolType = "D8", SetupTime = 11, MachiningTime = 6, InspectionTime = 1, PreparationTime = 2, TrialRunTime = 3, IsActive = true, CreatedBy = Admin, CreatedAt = DateTime.Now };
        db.PartMachiningTimings.Add(ent);
        await db.SaveChangesAsync();
        return ent.TimingId;
    }
    public static async Task RemoveTimingLikePage(AppDbContext db, long timingId)
    {
        var ent = await db.PartMachiningTimings.FindAsync(timingId);
        if (ent != null) { ent.IsActive = false; ent.UpdatedBy = Admin; ent.UpdatedAt = DateTime.Now; await db.SaveChangesAsync(); }
    }

    /// <summary>Tạo phiếu KH y như trang Create_Khplan.DoSaveAsync: 1 giao dịch, CreateAsync rồi CopyRouteSnapshotAsync từng dòng. Trả về KhPlanDetailId các dòng.</summary>
    public static async Task<List<int>> CreateKhAsync(AppDbContext db, int partId, string partNo, decimal[] qtys, string poPrefix, bool workflow = true)
    {
        var kh = new KhPlanService(db);
        var plan = new KhPlan { CustomerId = 1, PlanDate = DateTime.Today, ApplyWorkflow = workflow };
        for (int i = 0; i < qtys.Length; i++)
            plan.Details.Add(new KhPlanDetail { PartNo = partNo, PurchaseOrder = qtys.Length == 1 ? poPrefix : poPrefix + (char)('A' + i), Quantity = qtys[i], Unit = "EA", STD = DateTime.Today.AddDays(7) });
        await using var trx = await db.Database.BeginTransactionAsync();
        try
        {
            await kh.CreateAsync(plan, Admin);
            foreach (var d in plan.Details) await kh.CopyRouteSnapshotAsync(d.KhPlanDetailId, partId, Admin);
            await trx.CommitAsync();
        }
        catch { await trx.RollbackAsync(); db.ChangeTracker.Clear(); throw; }
        return plan.Details.Select(d => d.KhPlanDetailId).ToList();
    }

    // ── Ảnh chụp Part Master (đúng các cột CopyRouteSnapshotAsync chép) để so với snapshot ──
    public record Pic(List<string> Gc, List<string> Taro, List<string> Bavia, List<string> Rua, List<string> Kcs, List<string> Pkg, List<string> Excl)
    {
        public string Sig => string.Join("\n", new[] { "GC" }.Concat(Gc).Concat(new[] { "TARO" }).Concat(Taro).Concat(new[] { "BAVIA" }).Concat(Bavia)
            .Concat(new[] { "RUA" }).Concat(Rua).Concat(new[] { "KCS" }).Concat(Kcs).Concat(new[] { "PKG" }).Concat(Pkg).Concat(new[] { "EXCL" }).Concat(Excl));
    }
    static string D(decimal? v) => v?.ToString("0.##") ?? "";
    static string Cde(int order, string nc, string? name, decimal? std, bool bk, string? parent) => $"{order}|{nc}|{name}|{D(std)}|{(bk ? 1 : 0)}|{parent}";

    public static Pic Picture(AppDbContext db, int partId)
    {
        var gc = db.PartMachiningSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s =>
            $"{s.StepOrder}|{s.NC}|{s.MachineRegistered}|{(s.IsBackup ? 1 : 0)}|{s.ParentNC}|{D(s.SetupTime)}|{D(s.MachiningTime)}|{D(s.InspectionTime)}|{D(s.PreparationTime)}|{D(s.TrialRunTime)}|{s.Drawing}|{s.FixtureType}|{s.ToolType}|{s.TimingMachine}|" +
            string.Join(",", db.PartMachiningTimings.List.Where(t => t.StepId == s.StepId && t.IsActive).OrderBy(t => t.DisplayOrder).Select(t => $"{t.DisplayOrder}:{t.SoMay}:{D(t.SetupTime)}:{D(t.MachiningTime)}:{D(t.InspectionTime)}:{D(t.PreparationTime)}:{D(t.TrialRunTime)}:{t.FixtureType}:{t.ToolType}"))).ToList();
        var taro = db.PartTaroSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC) + "|" + s.WtsTaskCode).ToList();
        var bavia = db.PartBaviaSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var rua = db.PartWashingSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var kcs = db.PartInspectionSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var pkg = db.PartPackagingSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var excl = db.MachineExcludeSettings.List.Select(m => m.SoMay).OrderBy(x => x).ToList();
        return new Pic(gc, taro, bavia, rua, kcs, pkg, excl);
    }

    /// <summary>Snapshot của 1 PO đọc theo cùng khuôn ảnh chụp.</summary>
    public static Pic SnapPicture(AppDbContext db, int detailId)
    {
        var gc = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s =>
            $"{s.StepOrder}|{s.NC}|{s.MachineRegistered}|{(s.IsBackup ? 1 : 0)}|{s.ParentNC}|{D(s.SetupTime)}|{D(s.MachiningTime)}|{D(s.InspectionTime)}|{D(s.PreparationTime)}|{D(s.TrialRunTime)}|{s.Drawing}|{s.FixtureType}|{s.ToolType}|{s.TimingMachine}|" +
            string.Join(",", db.KhPlanRouteSnapshotMachiningTimings.List.Where(t => t.MachiningSnapshotId == s.SnapshotId).OrderBy(t => t.DisplayOrder).Select(t => $"{t.DisplayOrder}:{t.SoMay}:{D(t.SetupTime)}:{D(t.MachiningTime)}:{D(t.InspectionTime)}:{D(t.PreparationTime)}:{D(t.TrialRunTime)}:{t.FixtureType}:{t.ToolType}"))).ToList();
        var taro = db.KhPlanRouteSnapshotTaro.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC) + "|" + s.WtsTaskCode).ToList();
        var bavia = db.KhPlanRouteSnapshotBavia.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var rua = db.KhPlanRouteSnapshotWashing.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var kcs = db.KhPlanRouteSnapshotInspection.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var pkg = db.KhPlanRouteSnapshotPackaging.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).Select(s => Cde(s.StepOrder, s.NC, s.StepName, s.StandardTime, s.IsBackup, s.ParentNC)).ToList();
        var excl = db.KhPlanRouteSnapshotMachineExclude.List.Where(m => m.KhPlanDetailId == detailId).Select(m => m.SoMay).OrderBy(x => x).ToList();
        return new Pic(gc, taro, bavia, rua, kcs, pkg, excl);
    }

    /// <summary>So snapshot PO với ảnh chụp Part Master; trả về mô tả chỗ lệch (rỗng = khớp). Kiểm cả cột nối nguồn (SourcePartId / SourceStepId / SourceTimingId).</summary>
    public static string? Diff(AppDbContext db, int detailId, Pic pm, int partId)
    {
        var sn = SnapPicture(db, detailId);
        if (sn.Sig != pm.Sig)
        {
            var a = pm.Sig.Split('\n'); var b = sn.Sig.Split('\n');
            var first = Enumerable.Range(0, Math.Max(a.Length, b.Length)).FirstOrDefault(i => i >= a.Length || i >= b.Length || a[i] != b[i]);
            return $"dòng {first}: Part Master «{(first < a.Length ? a[first] : "(hết)")}» ≠ snapshot «{(first < b.Length ? b[first] : "(hết)")}»";
        }
        var bad = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || s.SourceStepId == null || !db.PartMachiningSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "GC " + s.NC).ToList();
        bad.AddRange(db.KhPlanRouteSnapshotMachiningTimings.List.Where(t => t.KhPlanDetailId == detailId && (t.SourceStepId == null || t.SourceTimingId == null || !db.PartMachiningTimings.List.Any(p => p.TimingId == t.SourceTimingId && p.StepId == t.SourceStepId && p.SoMay == t.SoMay))).Select(t => "máy " + t.SoMay));
        bad.AddRange(db.KhPlanRouteSnapshotTaro.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || !db.PartTaroSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "Taro " + s.NC));
        bad.AddRange(db.KhPlanRouteSnapshotBavia.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || !db.PartBaviaSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "Bavia " + s.NC));
        bad.AddRange(db.KhPlanRouteSnapshotWashing.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || !db.PartWashingSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "Rửa " + s.NC));
        bad.AddRange(db.KhPlanRouteSnapshotInspection.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || !db.PartInspectionSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "KCS " + s.NC));
        bad.AddRange(db.KhPlanRouteSnapshotPackaging.List.Where(s => s.KhPlanDetailId == detailId && (s.SourcePartId != partId || !db.PartPackagingSteps.List.Any(p => p.StepId == s.SourceStepId && p.NC == s.NC))).Select(s => "ĐG " + s.NC));
        // Trạng thái xác nhận TG chuẩn: mỗi dòng snapshot phải có đủ dòng StdConfirm trỏ đúng
        var conf = db.KhPlanRouteSnapshotStdConfirms.List.Where(c => c.KhPlanDetailId == detailId).ToList();
        int gcRows = db.KhPlanRouteSnapshotMachining.List.Count(s => s.KhPlanDetailId == detailId), tmRows = db.KhPlanRouteSnapshotMachiningTimings.List.Count(t => t.KhPlanDetailId == detailId);
        int cde = sn.Taro.Count + sn.Bavia.Count + sn.Rua.Count + sn.Kcs.Count + sn.Pkg.Count;
        var expect = (gcRows + tmRows) * StdTrack.Field.Gc.Length + cde;
        if (conf.Count != expect) bad.Add($"StdConfirm {conf.Count} dòng ≠ {expect} mong đợi");
        if (conf.Any(c => c.Note != null)) bad.Add("StdConfirm có dòng không nối được nguồn");
        bool RowExists(KhPlanRouteSnapshotStdConfirm c) => c.SnapshotTable switch
        {
            StdTrack.Table.Machining => db.KhPlanRouteSnapshotMachining.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && StdTrack.Field.Gc.Contains(c.FieldName),
            StdTrack.Table.MachiningTiming => db.KhPlanRouteSnapshotMachiningTimings.List.Any(s => s.KhPlanDetailId == detailId && s.TimingSnapshotId == c.SnapshotRowId) && StdTrack.Field.Gc.Contains(c.FieldName),
            StdTrack.Table.Taro => db.KhPlanRouteSnapshotTaro.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && c.FieldName == StdTrack.Field.Standard,
            StdTrack.Table.Bavia => db.KhPlanRouteSnapshotBavia.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && c.FieldName == StdTrack.Field.Standard,
            StdTrack.Table.Washing => db.KhPlanRouteSnapshotWashing.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && c.FieldName == StdTrack.Field.Standard,
            StdTrack.Table.Inspection => db.KhPlanRouteSnapshotInspection.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && c.FieldName == StdTrack.Field.Standard,
            StdTrack.Table.Packaging => db.KhPlanRouteSnapshotPackaging.List.Any(s => s.KhPlanDetailId == detailId && s.SnapshotId == c.SnapshotRowId) && c.FieldName == StdTrack.Field.Standard,
            _ => false,
        };
        var orphan = conf.Count(c => !RowExists(c));
        if (orphan > 0) bad.Add($"StdConfirm {orphan} dòng trỏ sai dòng snapshot / sai FieldName");
        if (conf.GroupBy(c => (c.SnapshotTable, c.SnapshotRowId, c.FieldName)).Any(g => g.Count() > 1)) bad.Add("StdConfirm có dòng trùng (bảng, dòng, trường)");
        return bad.Any() ? "cột nối nguồn / xác nhận sai: " + string.Join(", ", bad) : null;
    }

    /// <summary>Sửa Part Master ngẫu nhiên bằng hàm thật (mọi kiểu sửa trang cho phép). Trả về mô tả.</summary>
    public static async Task<string> MutateAsync(AppDbContext db, Random rnd, int partId, int kind = -1)
    {
        var gc = new PartMachiningService(db);
        var steps = db.PartMachiningSteps.List.Where(s => s.PartId == partId && s.IsActive).OrderBy(s => s.StepOrder).ToList();
        var mains = steps.Where(s => !s.IsBackup).ToList();
        if (kind < 0) kind = rnd.Next(9);
        switch (kind)
        {
            case 0:   // đổi tên NC + thời gian
            {
                if (!mains.Any()) return "bỏ qua";
                var s = mains[rnd.Next(mains.Count)]; var oldNc = s.NC;
                var up = Clone(s); up.NC = s.NC + "X"; up.SetupTime = (s.SetupTime ?? 0) + 1;
                await gc.UpdateAsync(s.StepId, up, Admin, Reason);
                return $"PM đổi tên NC {oldNc}→{up.NC}";
            }
            case 1:   // thêm NC mới
            {
                var n = steps.Count + 1;
                await gc.AddAsync(partId, new PartMachiningStep { NC = $"NC{n:00}N", MachineRegistered = "MN" + n, SetupTime = 9, MachiningTime = 4 }, Admin, Reason);
                return $"PM thêm NC NC{n:00}N";
            }
            case 2:   // xoá NC (hard delete + máy đồng dạng)
            {
                if (mains.Count < 2) return "bỏ qua";
                var s = mains[rnd.Next(mains.Count)];
                foreach (var b in steps.Where(x => x.IsBackup && x.ParentNC == s.NC).ToList()) await gc.SoftDeleteAsync(b.StepId, Admin, Reason);
                await gc.SoftDeleteAsync(s.StepId, Admin, Reason);
                return $"PM xoá NC {s.NC}";
            }
            case 3:   // thêm máy đồng dạng
            {
                if (!mains.Any()) return "bỏ qua";
                var s = mains[rnd.Next(mains.Count)];
                var k = db.PartMachiningTimings.List.Count(t => t.StepId == s.StepId) + 1;
                await AddTimingLikePage(db, s.StepId, $"{s.MachineRegistered}z{k}", k);
                return $"PM thêm máy đồng dạng cho {s.NC}";
            }
            case 4:   // bỏ máy đồng dạng
            {
                var ts = db.PartMachiningTimings.List.Where(t => t.IsActive && steps.Any(s => s.StepId == t.StepId)).ToList();
                if (!ts.Any()) return "bỏ qua";
                var t = ts[rnd.Next(ts.Count)];
                await RemoveTimingLikePage(db, t.TimingId);
                return $"PM bỏ máy đồng dạng {t.SoMay}";
            }
            case 5:   // thêm / bỏ máy loại trừ (bảng chung)
            {
                var h = db.MachineExcludeSettings.List.FirstOrDefault(m => m.SoMay == "H04");
                if (h != null) { db.MachineExcludeSettings.Remove(h); await db.SaveChangesAsync(); return "bỏ máy loại trừ H04"; }
                db.MachineExcludeSettings.Add(new MachineExcludeSetting { SoMay = "H04", AddedAt = DateTime.Now, AddedByUserId = Admin }); await db.SaveChangesAsync();
                return "thêm máy loại trừ H04";
            }
            case 6:   // Taro: đổi mã CV
            {
                var ts = db.PartTaroSteps.List.Where(s => s.PartId == partId && s.IsActive).ToList();
                if (!ts.Any()) return "bỏ qua";
                var s = ts[rnd.Next(ts.Count)];
                await new PartTaroService(db).UpdateAsync(s.StepId, new PartTaroStep { NC = s.NC, WtsTaskCode = (s.WtsTaskCode ?? s.NC) + "X", StepName = s.StepName, StandardTime = (s.StandardTime ?? 0) + 1, IsBackup = s.IsBackup, ParentNC = s.ParentNC }, Admin, Reason);
                return $"PM Taro đổi mã {s.NC}";
            }
            case 7:   // thêm dòng dự phòng
            {
                if (!mains.Any()) return "bỏ qua";
                var s = mains[rnd.Next(mains.Count)];
                await gc.AddAsync(partId, new PartMachiningStep { NC = s.NC + "-DP-09", MachineRegistered = "D9", IsBackup = true, ParentNC = s.NC, SetupTime = 1 }, Admin, Reason);
                return $"PM thêm dự phòng cho {s.NC}";
            }
            default:  // KCS / Đóng gói thêm dòng
            {
                if (rnd.NextDouble() < 0.5) { await new PartInspectionService(db).AddAsync(partId, new PartInspectionStep { NC = "K9", StepName = "KCS thêm", StandardTime = 1 }, Admin, Reason); return "PM thêm KCS K9"; }
                await new PartPackagingService(db).AddAsync(partId, new PartPackagingStep { NC = "P9", StepName = "ĐG thêm", StandardTime = 1 }, Admin, Reason); return "PM thêm ĐG P9";
            }
        }
    }
    static PartMachiningStep Clone(PartMachiningStep s) => new()
    {
        NC = s.NC, Drawing = s.Drawing, MachineRegistered = s.MachineRegistered, FixtureType = s.FixtureType, ToolType = s.ToolType, TimingMachine = s.TimingMachine,
        IsBackup = s.IsBackup, ParentNC = s.ParentNC, SetupTime = s.SetupTime, MachiningTime = s.MachiningTime, InspectionTime = s.InspectionTime, PreparationTime = s.PreparationTime, TrialRunTime = s.TrialRunTime
    };

    /// <summary>Mã dùng cho thao tác ngẫu nhiên, đọc từ SNAPSHOT của PO (không đọc Part Master): NC chính GC (kể cả loại trừ) + mã WTS từng nhóm (gồm dự phòng, bỏ mã chuẩn bị).</summary>
    public static (string[] ncs, Dictionary<string, (string pg, string[] codes)> codeOf) CodesFromSnapshot(AppDbContext db, int detailId)
    {
        var gc = db.KhPlanRouteSnapshotMachining.List.Where(s => s.KhPlanDetailId == detailId).OrderBy(s => s.StepOrder).ToList();
        var ncs = gc.Where(s => !s.IsBackup).Select(s => s.NC).ToArray();
        string TaroCode(KhPlanRouteSnapshotTaro t) => string.IsNullOrWhiteSpace(t.WtsTaskCode) ? t.NC : t.WtsTaskCode!.Trim();
        var htsp = db.KhPlanRouteSnapshotTaro.List.Where(s => s.KhPlanDetailId == detailId).Select(s => "TARO|" + TaroCode(s))
            .Concat(db.KhPlanRouteSnapshotBavia.List.Where(s => s.KhPlanDetailId == detailId).Select(s => "BAVIA|" + s.NC))
            .Concat(db.KhPlanRouteSnapshotWashing.List.Where(s => s.KhPlanDetailId == detailId).Select(s => "WASHING|" + s.NC))
            .Where(c => !HandoverLedgerService.IsPrepCode(c.Split('|')[1])).ToArray();
        var kcs = db.KhPlanRouteSnapshotInspection.List.Where(s => s.KhPlanDetailId == detailId).Select(s => s.NC).ToArray();
        var pkg = db.KhPlanRouteSnapshotPackaging.List.Where(s => s.KhPlanDetailId == detailId).Select(s => s.NC).ToArray();
        var codeOf = new Dictionary<string, (string pg, string[] codes)>
        {
            ["GC"] = ("GC", gc.Select(s => s.NC).ToArray()), ["HTSP"] = ("", htsp), ["KCS"] = ("KCS", kcs), ["PKG"] = ("PKG", pkg),
        };
        return (ncs, codeOf);
    }

    /// <summary>Chữ ký sổ 1 PO (để chứng minh thao tác trên PO khác / sửa Part Master không làm sổ đổi).</summary>
    public static string LedgerSig(LedgerRow row)
        => string.Join(";", row.Groups.Values.Select(g => $"{g.Code}:{g.Ton}/{g.ChuaLam}/{g.ConGiao}/{g.LoiGiu}/{g.SuaGiu}/" + string.Join(",", g.Steps.Select(s => $"{s.Pg}.{s.Code}={s.Ok}/{s.Ng}/{s.Waiting}/{(s.IsExcluded ? "X" : "")}"))))
           + $"|phoi={row.Phoi}|bal={row.Balanced}|vu=" + string.Join(",", row.Cases.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value.SinhRa}:{k.Value.Transit}:{k.Value.HeldTotal}:{k.Value.DaXuLyTotal}:{k.Value.Huy}:{k.Value.TraKhach}"));
}
