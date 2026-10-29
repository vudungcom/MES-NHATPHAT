// Stub tối thiểu của EF Core để biên dịch + chạy HandoverService / HandoverLedgerService trong sandbox (không có NuGet).
// - DbSet<T> = danh sách trong bộ nhớ, truy vấn bằng LINQ-to-Objects.
// - Add chỉ thấy được sau SaveChangesAsync (giống EF): gán Id tự tăng + nối navigation.
// - Giao dịch: chụp toàn bộ dữ liệu lúc Begin; Dispose mà chưa Commit → trả lại y như cũ (giống rollback).
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class IndexAttribute : Attribute { public IndexAttribute(params string[] p) { } }

    public interface IStubSet
    {
        Type ElementType { get; }
        IList Items { get; }
        IList Pending { get; }
    }

    public class DbSet<T> : IQueryable<T>, IStubSet where T : class
    {
        public List<T> List = new();
        public List<T> Added = new();
        public IList Items => List;
        public IList Pending => Added;
        IQueryable<T> Q => List.AsQueryable();
        public Type ElementType => typeof(T);
        public Expression Expression => Q.Expression;
        public IQueryProvider Provider => Q.Provider;
        public IEnumerator<T> GetEnumerator() => List.ToList().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Add(T e) => Added.Add(e);
        public void AddRange(IEnumerable<T> es) => Added.AddRange(es);
        public void Remove(T e) { List.Remove(e); }
        public void RemoveRange(IEnumerable<T> es) { foreach (var e in es.ToList()) List.Remove(e); }   // [CHUOI] Part Master xoá NC: Confirms → Timings → Step
        public ValueTask<T?> FindAsync(params object?[] k) { var pk = typeof(T).GetProperties().First(p => p.Name.EndsWith("Id")); return ValueTask.FromResult(List.FirstOrDefault(x => Equals(pk.GetValue(x), k[0]))); }
    }

    public interface IIncludableQueryable<out TEntity, out TProperty> : IQueryable<TEntity> { }

    class Inc<T, P> : IIncludableQueryable<T, P>
    {
        readonly IQueryable<T> _q;
        public Inc(IQueryable<T> q) { _q = q; }
        public Type ElementType => _q.ElementType;
        public Expression Expression => _q.Expression;
        public IQueryProvider Provider => _q.Provider;
        public IEnumerator<T> GetEnumerator() => _q.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IIncludableQueryable<T, P> Include<T, P>(this IQueryable<T> q, Expression<Func<T, P>> nav) where T : class => new Inc<T, P>(q);
        public static IIncludableQueryable<T, P2> ThenInclude<T, P1, P2>(this IIncludableQueryable<T, IEnumerable<P1>> q, Expression<Func<P1, P2>> nav) where T : class => new Inc<T, P2>(q);
        public static IIncludableQueryable<T, P2> ThenInclude<T, P1, P2>(this IIncludableQueryable<T, P1> q, Expression<Func<P1, P2>> nav) where T : class => new Inc<T, P2>(q);
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> q) where T : class => q;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.ToList());
        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.FirstOrDefault());
        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.FirstOrDefault(p));
        public static Task<bool> AnyAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.Any());
        public static Task<bool> AnyAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.Any(p));
        public static Task<int> CountAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.Count());
        public static Task<int> CountAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.Count(p));
        public static Task<decimal> SumAsync(this IQueryable<decimal> q, CancellationToken ct = default) => Task.FromResult(q.Sum());
        public static Task<decimal?> SumAsync(this IQueryable<decimal?> q, CancellationToken ct = default) => Task.FromResult(q.Sum());
        public static Task<decimal> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, decimal>> s, CancellationToken ct = default) => Task.FromResult(q.Sum(s));
        public static Task<decimal?> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, decimal?>> s, CancellationToken ct = default) => Task.FromResult(q.Sum(s));
        public static Task<int> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, int>> s, CancellationToken ct = default) => Task.FromResult(q.Sum(s));
        public static Task<Dictionary<K, T>> ToDictionaryAsync<T, K>(this IQueryable<T> q, Func<T, K> k, CancellationToken ct = default) where K : notnull => Task.FromResult(q.ToDictionary(k));
        public static Task<Dictionary<K, V>> ToDictionaryAsync<T, K, V>(this IQueryable<T> q, Func<T, K> k, Func<T, V> v, CancellationToken ct = default) where K : notnull => Task.FromResult(q.ToDictionary(k, v));
    }

    public class ChangeTracker
    {
        readonly DbContext _c;
        public ChangeTracker(DbContext c) { _c = c; }
        public void Clear() { foreach (var s in _c.AllSets()) s.Pending.Clear(); }
    }

    public class DbContext
    {
        readonly Dictionary<Type, IStubSet> _sets = new();
        public DatabaseFacade Database { get; }
        public ChangeTracker ChangeTracker { get; }
        public DbContext() { Database = new DatabaseFacade(this); ChangeTracker = new ChangeTracker(this); }
        public DbSet<T> Set<T>() where T : class
        {
            if (!_sets.TryGetValue(typeof(T), out var s)) { s = new DbSet<T>(); _sets[typeof(T)] = s; }
            return (DbSet<T>)s;
        }
        public IEnumerable<IStubSet> AllSets() => _sets.Values;

        /// <summary>Tên cột khoá chính tự tăng của từng bảng (chỉ các bảng harness thêm dòng lúc chạy).</summary>
        public static readonly Dictionary<string, string> KeyName = new()
        {
            ["HandoverTransaction"] = "HandoverTxId",
            ["HandoverReceive"]     = "ReceiveId",
            ["WtsProductionLog"]    = "WtsLogId",
            ["KhoVatLieu"]          = "KhoVatLieuId",
            ["HandoverNgReport"]    = "HandoverNgReportId",   // [GN-NG-AFTER]
            ["NgCategory"]          = "NgCategoryId",         // [VULOI]
            ["NgCase"]              = "NgCaseId",
            ["NgCategoryChangeLog"] = "ChangeId",
            // [CHUOI] Part Master → KH → snapshot bằng hàm thật
            ["PartMaster"]          = "PartId",
            ["PartMachiningStep"]   = "StepId",
            ["PartTaroStep"]        = "StepId",
            ["PartBaviaStep"]       = "StepId",
            ["PartWashingStep"]     = "StepId",
            ["PartInspectionStep"]  = "StepId",
            ["PartPackagingStep"]   = "StepId",
            ["PartMachiningTiming"] = "TimingId",
            ["KhoVatLieuChangeLog"] = "LogId",   // (rà độc lập 29/10: trước đó LogId luôn 0, không bảng nào đọc ngược nên vô hại)
        };

        public int SaveChanges() => SaveChangesAsync().GetAwaiter().GetResult();
        public virtual Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            int n = 0;
            foreach (var s in _sets.Values.ToList())
            {
                if (s.Pending.Count == 0) continue;
                var keyName = KeyName.TryGetValue(s.ElementType.Name, out var k) ? k
                    : new[] { "SnapshotId", "TimingSnapshotId", "ChangeId", s.ElementType.Name + "Id", "Id" }.FirstOrDefault(n => s.ElementType.GetProperty(n) != null && s.ElementType.GetProperty(n)!.PropertyType != typeof(string))
                      // [WTS-SVC] bảng chưa khai KeyName: lấy thuộc tính [Key] (hoặc *Id số đầu tiên) — ActivityLogId, GroupId, CaId… khi chạy cả web trên EF giả
                      ?? s.ElementType.GetProperties().FirstOrDefault(p => p.Name.EndsWith("Id") && (p.PropertyType == typeof(int) || p.PropertyType == typeof(long)) && p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), true).Length > 0)?.Name
                      ?? s.ElementType.GetProperties().FirstOrDefault(p => p.Name.EndsWith("Id") && (p.PropertyType == typeof(int) || p.PropertyType == typeof(long)))?.Name;
                var keyProp = keyName == null ? null : s.ElementType.GetProperty(keyName);
                foreach (var e in s.Pending)
                {
                    if (keyProp != null)
                    {
                        long max = 0;
                        foreach (var x in s.Items) max = Math.Max(max, Convert.ToInt64(keyProp.GetValue(x)));
                        var cur = Convert.ToInt64(keyProp.GetValue(e));
                        if (cur == 0) keyProp.SetValue(e, Convert.ChangeType(max + 1, keyProp.PropertyType));
                    }
                    s.Items.Add(e);
                    n++;
                    // [CHUOI] giống EF: KhPlans.Add(plan) chèn luôn plan.Details (KhPlanService.CreateAsync)
                    if (e is MES.Web.Data.Entities.KhPlan kp)
                    {
                        var ds = Set<MES.Web.Data.Entities.KhPlanDetail>();
                        foreach (var d in kp.Details)
                        {
                            if (ds.List.Contains(d)) continue;
                            if (d.KhPlanDetailId == 0) d.KhPlanDetailId = ds.List.Count == 0 ? 1 : ds.List.Max(x => x.KhPlanDetailId) + 1;
                            d.KhPlanId = kp.KhPlanId; d.KhPlan = kp; ds.List.Add(d);
                        }
                    }
                    // giống EF: chèn cả con trong navigation (máy đồng dạng của snapshot GC)
                    if (e is MES.Web.Data.Entities.KhPlanRouteSnapshotMachining pm)
                    {
                        var ts = Set<MES.Web.Data.Entities.KhPlanRouteSnapshotMachiningTiming>();
                        foreach (var t in pm.TimingRows)
                        {
                            if (ts.List.Contains(t)) continue;
                            if (t.TimingSnapshotId == 0) t.TimingSnapshotId = ts.List.Count == 0 ? 1 : ts.List.Max(x => x.TimingSnapshotId) + 1;
                            t.MachiningSnapshotId = pm.SnapshotId; ts.List.Add(t);
                        }
                    }
                }
                s.Pending.Clear();
            }
            Fixup();
            return Task.FromResult(n);
        }

        /// <summary>Nối navigation theo khoá ngoại (EF tự làm việc này).</summary>
        public virtual void Fixup() { }

        // ── Giao dịch: chụp / trả lại ──────────────────────────────────
        internal Dictionary<IStubSet, (List<object> items, List<Dictionary<PropertyInfo, object?>> vals)> Snapshot()
        {
            var d = new Dictionary<IStubSet, (List<object>, List<Dictionary<PropertyInfo, object?>>)>();
            foreach (var s in _sets.Values)
            {
                var props = Scalars(s.ElementType);
                var items = s.Items.Cast<object>().ToList();
                var vals = items.Select(o => props.ToDictionary(p => p, p => p.GetValue(o))).ToList();
                d[s] = (items, vals);
            }
            return d;
        }
        internal void Restore(Dictionary<IStubSet, (List<object> items, List<Dictionary<PropertyInfo, object?>> vals)> snap)
        {
            foreach (var s in _sets.Values)
            {
                s.Pending.Clear();
                if (!snap.TryGetValue(s, out var v)) { s.Items.Clear(); continue; }
                s.Items.Clear();
                for (int i = 0; i < v.items.Count; i++)
                {
                    foreach (var kv in v.vals[i]) kv.Key.SetValue(v.items[i], kv.Value);
                    s.Items.Add(v.items[i]);
                }
            }
            Fixup();
        }
        static readonly Dictionary<Type, PropertyInfo[]> _scalars = new();
        static PropertyInfo[] Scalars(Type t)
        {
            if (_scalars.TryGetValue(t, out var p)) return p;
            p = t.GetProperties().Where(x => x.CanRead && x.CanWrite && x.GetIndexParameters().Length == 0
                    && (x.PropertyType.IsValueType || x.PropertyType == typeof(string))).ToArray();
            _scalars[t] = p;
            return p;
        }
    }

    public class DatabaseFacade
    {
        readonly DbContext _c;
        public DatabaseFacade(DbContext c) { _c = c; }
        public StubTransaction? Current;
        public Task<StubTransaction> BeginTransactionAsync(CancellationToken ct = default) => Begin();
        public Task<StubTransaction> BeginTransactionAsync(System.Data.IsolationLevel lvl, CancellationToken ct = default) => Begin();
        Task<StubTransaction> Begin()
        {
            if (Current != null) throw new InvalidOperationException("Giao dịch lồng nhau (EF thật cũng báo lỗi)");
            Current = new StubTransaction(_c, this, _c.Snapshot());
            return Task.FromResult(Current);
        }
        public static int RawCalls; public static List<string> RawLog = new();
        public Task<int> ExecuteSqlRawAsync(string sql, params object[] args)
        {
            RawCalls++; RawLog.Add(sql);
            if (Current == null && sql.Contains("sp_getapplock") && sql.Contains("'Transaction'"))
                throw new InvalidOperationException("sp_getapplock LockOwner=Transaction gọi ngoài giao dịch");
            // [CHUOI] Làm thật câu "DELETE FROM KhPlanRouteSnapshot… WHERE KhPlanDetailId = {0}" (RefreshRouteSnapshotAsync / WriteSnapshotStdConfirmsAsync)
            //   để kiểm được "làm mới quy trình = Part Master hiện tại". Các câu raw khác vẫn chỉ ghi log như cũ.
            var m = System.Text.RegularExpressions.Regex.Match(sql, @"DELETE\s+(?:\w+\s+)?FROM\s+(KhPlanRouteSnapshot\w+)");
            if (m.Success && args.Length > 0 && sql.Contains("KhPlanDetailId"))
            {
                var table = m.Groups[1].Value;
                var set = _c.AllSets().FirstOrDefault(s => s.ElementType.Name == table || s.ElementType.Name + "s" == table);
                var pk = set?.ElementType.GetProperty("KhPlanDetailId");
                if (set != null && pk != null)
                {
                    var id = Convert.ToInt32(args[0]); int n = 0;
                    foreach (var it in set.Items.Cast<object>().ToList())
                        if (Convert.ToInt32(pk.GetValue(it)) == id) { set.Items.Remove(it); n++; }
                    _c.Fixup();
                    return Task.FromResult(n);
                }
            }
            // [WTS-SVC] Làm thật câu huỷ dòng WTS / hoạt động của WtsSaveService (trước ở trang HieuSuat_Index):
            //   "UPDATE WtsProductionLogs SET IsVoided=1, VoidReason=N'…', VoidedBy={0}, VoidedAt={1} WHERE WtsLogId={2} AND IsVoided=0"
            //   → trả số dòng đổi (1 / 0) như SQL Server, để kiểm được [D8-WTS-VOID] (dòng đã huỷ ở nơi khác thì không ghi dòng mới).
            var mv = System.Text.RegularExpressions.Regex.Match(sql, @"^UPDATE\s+(WtsProductionLogs|WorkerActivityLogs)\s+SET\s+IsVoided=1,\s*VoidReason=N'([^']*)',\s*VoidedBy=\{0\},\s*VoidedAt=\{1\}\s+WHERE\s+(WtsLogId|ActivityLogId)=\{2\}\s+AND\s+IsVoided=0");
            if (mv.Success && args.Length == 3)
            {
                var set = _c.AllSets().FirstOrDefault(s => s.ElementType.Name + "s" == mv.Groups[1].Value);
                var keyP = set?.ElementType.GetProperty(mv.Groups[3].Value);
                if (set != null && keyP != null)
                {
                    var id = Convert.ToInt64(args[2]); int n = 0;
                    foreach (var it in set.Items)
                    {
                        if (Convert.ToInt64(keyP.GetValue(it)) != id) continue;
                        var t = it.GetType();
                        if ((bool)t.GetProperty("IsVoided")!.GetValue(it)!) continue;
                        t.GetProperty("IsVoided")!.SetValue(it, true);
                        t.GetProperty("VoidReason")?.SetValue(it, mv.Groups[2].Value);
                        t.GetProperty("VoidedBy")?.SetValue(it, Convert.ToInt32(args[0]));
                        t.GetProperty("VoidedAt")?.SetValue(it, (DateTime)args[1]);
                        n++;
                    }
                    return Task.FromResult(n);
                }
            }
            return Task.FromResult(-1);
        }
    }

    public class StubTransaction : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
    {
        readonly DbContext _c; readonly DatabaseFacade _f;
        readonly Dictionary<IStubSet, (List<object>, List<Dictionary<PropertyInfo, object?>>)> _snap;
        bool _done;
        public static int Rollbacks;
        internal StubTransaction(DbContext c, DatabaseFacade f, Dictionary<IStubSet, (List<object>, List<Dictionary<PropertyInfo, object?>>)> snap) { _c = c; _f = f; _snap = snap; }
        public Task CommitAsync(CancellationToken ct = default) { _done = true; _f.Current = null; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct = default) { if (!_done) { _c.Restore(_snap); Rollbacks++; } _done = true; _f.Current = null; return Task.CompletedTask; }
        public void Dispose()
        {
            if (!_done) { _c.Restore(_snap); Rollbacks++; }
            _done = true; _f.Current = null;
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

namespace Microsoft.EntityFrameworkCore.Storage
{
    public interface IDbContextTransaction : IDisposable, IAsyncDisposable { Task CommitAsync(CancellationToken ct = default); Task RollbackAsync(CancellationToken ct = default); }
}
