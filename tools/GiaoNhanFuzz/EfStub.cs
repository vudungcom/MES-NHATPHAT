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
        };

        public int SaveChanges() => SaveChangesAsync().GetAwaiter().GetResult();
        public virtual Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            int n = 0;
            foreach (var s in _sets.Values)
            {
                if (s.Pending.Count == 0) continue;
                var keyName = KeyName.TryGetValue(s.ElementType.Name, out var k) ? k : null;
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
        public static int RawCalls;
        public Task<int> ExecuteSqlRawAsync(string sql, params object[] args)
        {
            RawCalls++;
            if (Current == null && sql.Contains("sp_getapplock") && sql.Contains("'Transaction'"))
                throw new InvalidOperationException("sp_getapplock LockOwner=Transaction gọi ngoài giao dịch");
            return Task.FromResult(-1);
        }
    }

    public class StubTransaction : IDisposable, IAsyncDisposable
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
