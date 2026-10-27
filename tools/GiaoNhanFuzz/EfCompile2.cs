// [KH-GUARD] Stub thêm (chỉ để biên dịch) cho KhPlanService / KhoVatLieuService.
using System.Linq.Expressions;
namespace Microsoft.EntityFrameworkCore
{
    public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }
    public class EntityEntry<T> where T : class { public T Entity = null!; public EntityState State { get; set; } public dynamic Property(Expression<Func<T, object?>> e) => null!; public dynamic Collection(Expression<Func<T, IEnumerable<object>>> e) => null!; public dynamic Reference(Expression<Func<T, object?>> e) => null!; public Task ReloadAsync() => Task.CompletedTask; public dynamic OriginalValues => null!; public dynamic CurrentValues => null!; }
    public static class EF { public static class Functions { public static bool Like(string? a, string b) => true; } public static FuncsStub FunctionsObj => new(); }
    public class FuncsStub { }
    public static class MoreAsyncExt
    {
        public static Task<R> MaxAsync<T, R>(this IQueryable<T> q, Expression<Func<T, R>> s, CancellationToken ct = default) => Task.FromResult(q.Max(s))!;
        public static Task<R> MinAsync<T, R>(this IQueryable<T> q, Expression<Func<T, R>> s, CancellationToken ct = default) => Task.FromResult(q.Min(s))!;
        public static Task<T> MaxAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.Max())!;
        public static Task<T> MinAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.Min())!;
        public static Task<T> FirstAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.First());
        public static Task<T> FirstAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.First(p));
        public static Task<T> SingleAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.Single(p));
        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.SingleOrDefault());
        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.SingleOrDefault(p));
        public static Task<T?> LastOrDefaultAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.LastOrDefault());
        public static Task<bool> AllAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p, CancellationToken ct = default) => Task.FromResult(q.All(p));
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.ToArray());
        public static Task<HashSet<T>> ToHashSetAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.ToHashSet());
        public static Task<long> LongCountAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(q.LongCount());
        public static Task<double> AverageAsync<T>(this IQueryable<T> q, Expression<Func<T, decimal>> s, CancellationToken ct = default) => Task.FromResult(0d);
        public static Task<double?> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, double?>> s, CancellationToken ct = default) => Task.FromResult<double?>(0);
        public static Task<double> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, double>> s, CancellationToken ct = default) => Task.FromResult(0d);
        public static Task<int?> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, int?>> s, CancellationToken ct = default) => Task.FromResult<int?>(0);
        public static Task<long> SumAsync<T>(this IQueryable<T> q, Expression<Func<T, long>> s, CancellationToken ct = default) => Task.FromResult(0L);
        public static Task<int> ExecuteDeleteAsync<T>(this IQueryable<T> q, CancellationToken ct = default) => Task.FromResult(0);
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> q) => q;
        public static IQueryable<T> AsSplitQuery<T>(this IQueryable<T> q) => q;
        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> q) => q;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> q) => q;
        public static System.Data.Common.DbConnection GetDbConnection(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => null!;
        public static Task OpenConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, CancellationToken ct = default) => Task.CompletedTask;
        public static Task CloseConnectionAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => Task.CompletedTask;
        public static bool EnsureCreated(this Microsoft.EntityFrameworkCore.DatabaseFacade d) => true;
        public static void Migrate(this Microsoft.EntityFrameworkCore.DatabaseFacade d) { }
        public static Task<int> ExecuteSqlInterpolatedAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, FormattableString s, CancellationToken ct = default) => Task.FromResult(0);
        public static Task<int> ExecuteSqlAsync(this Microsoft.EntityFrameworkCore.DatabaseFacade d, FormattableString s, CancellationToken ct = default) => Task.FromResult(0);
        public static int ExecuteSqlRaw(this Microsoft.EntityFrameworkCore.DatabaseFacade d, string s, params object[] a) => 0;
        public static IQueryable<T> SqlQueryRaw<T>(this Microsoft.EntityFrameworkCore.DatabaseFacade d, string s, params object[] a) => Enumerable.Empty<T>().AsQueryable();
        public static IQueryable<T> SqlQuery<T>(this Microsoft.EntityFrameworkCore.DatabaseFacade d, FormattableString s) => Enumerable.Empty<T>().AsQueryable();
        public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> q, string s, params object[] a) where T : class => q;
        public static IQueryable<T> FromSqlInterpolated<T>(this DbSet<T> q, FormattableString s) where T : class => q;
        public static EntityEntry<T> Entry<T>(this DbContext c, T e) where T : class => new();
        public static IEnumerable<EntityEntry<T>> Entries<T>(this ChangeTracker c) where T : class => Array.Empty<EntityEntry<T>>();
        public static void Add<T>(this DbContext c, T e) where T : class { }
        public static void Remove<T>(this DbContext c, T e) where T : class { }
        public static void Update<T>(this DbContext c, T e) where T : class { }
    }
}
