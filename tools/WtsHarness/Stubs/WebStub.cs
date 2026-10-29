// [WTS-SVC harness] Stub thêm để biên dịch + chạy CẢ MES.Web trên EF giả (không NuGet): ModelBuilder fluent, DbContextOptions,
// UseSqlServer, AddDbContext (singleton — dữ liệu trong bộ nhớ dùng chung cả app).
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore
{
    public enum DeleteBehavior { Cascade, Restrict, SetNull, NoAction, ClientSetNull, ClientCascade, ClientNoAction }

    public class DbContextOptions { }
    public class DbContextOptions<T> : DbContextOptions { }
    public class DbContextOptionsBuilder
    {
        public DbContextOptionsBuilder UseSqlServer(string? cs, Action<object>? o = null) => this;
        public DbContextOptionsBuilder EnableSensitiveDataLogging(bool b = true) => this;
        public DbContextOptionsBuilder EnableDetailedErrors(bool b = true) => this;
        public DbContextOptionsBuilder LogTo(Action<string> a, object? b = null) => this;
    }

    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new();
        public ModelBuilder Entity<T>(Action<EntityTypeBuilder<T>> a) where T : class { a(new EntityTypeBuilder<T>()); return this; }
        public ModelBuilder HasDefaultSchema(string s) => this;
        public ModelBuilder Ignore<T>() => this;
    }
    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> HasKey(Expression<Func<T, object?>> k) => this;
        public EntityTypeBuilder<T> HasKey(params string[] k) => this;
        public EntityTypeBuilder<T> HasNoKey() => this;
        public EntityTypeBuilder<T> ToTable(string n, string? schema = null) => this;
        public EntityTypeBuilder<T> ToView(string n) => this;
        public EntityTypeBuilder<T> Ignore(Expression<Func<T, object?>> p) => this;
        public EntityTypeBuilder<T> Ignore(string p) => this;
        public EntityTypeBuilder<T> HasQueryFilter(Expression<Func<T, bool>> f) => this;
        public IndexBuilder<T> HasIndex(Expression<Func<T, object?>> p) => new();
        public IndexBuilder<T> HasIndex(params string[] p) => new();
        public PropertyBuilder<P> Property<P>(Expression<Func<T, P>> p) => new();
        public PropertyBuilder<object> Property(string p) => new();
        public ReferenceNavigationBuilder<T, P> HasOne<P>(Expression<Func<T, P?>> nav) where P : class => new();
        public ReferenceNavigationBuilder<T, P> HasOne<P>(string? nav = null) where P : class => new();
        public CollectionNavigationBuilder<T, P> HasMany<P>(Expression<Func<T, IEnumerable<P>?>> nav) where P : class => new();
        public CollectionNavigationBuilder<T, P> HasMany<P>(string? nav = null) where P : class => new();
        public EntityTypeBuilder<T> HasData(params T[] d) => this;
        public EntityTypeBuilder<T> HasCheckConstraint(string n, string sql) => this;
    }
    public class IndexBuilder<T>
    {
        public IndexBuilder<T> IsUnique(bool b = true) => this;
        public IndexBuilder<T> HasFilter(string? f) => this;
        public IndexBuilder<T> HasDatabaseName(string n) => this;
        public IndexBuilder<T> HasName(string n) => this;
        public IndexBuilder<T> IsClustered(bool b = true) => this;
    }
    public class PropertyBuilder<P>
    {
        public PropertyBuilder<P> HasMaxLength(int n) => this;
        public PropertyBuilder<P> HasPrecision(int a, int b) => this;
        public PropertyBuilder<P> HasPrecision(int a) => this;
        public PropertyBuilder<P> IsRequired(bool b = true) => this;
        public PropertyBuilder<P> HasDefaultValue(object? v) => this;
        public PropertyBuilder<P> HasDefaultValueSql(string s) => this;
        public PropertyBuilder<P> HasComputedColumnSql(string s, bool stored = false) => this;
        public PropertyBuilder<P> HasColumnType(string s) => this;
        public PropertyBuilder<P> HasColumnName(string s) => this;
        public PropertyBuilder<P> ValueGeneratedOnAdd() => this;
        public PropertyBuilder<P> ValueGeneratedNever() => this;
        public PropertyBuilder<P> ValueGeneratedOnAddOrUpdate() => this;
        public PropertyBuilder<P> IsConcurrencyToken(bool b = true) => this;
        public PropertyBuilder<P> HasConversion<C>() => this;
        public PropertyBuilder<P> IsUnicode(bool b = true) => this;
    }
    public class ReferenceNavigationBuilder<T, P> where T : class where P : class
    {
        public ReferenceCollectionBuilder<T, P> WithMany(Expression<Func<P, IEnumerable<T>?>>? nav = null) => new();
        public ReferenceCollectionBuilder<T, P> WithMany(string? nav) => new();
        public ReferenceReferenceBuilder<T, P> WithOne(Expression<Func<P, T?>>? nav = null) => new();
        public ReferenceReferenceBuilder<T, P> WithOne(string? nav) => new();
    }
    public class CollectionNavigationBuilder<T, P> where T : class where P : class
    {
        public ReferenceCollectionBuilder<P, T> WithOne(Expression<Func<P, T?>>? nav = null) => new();
        public ReferenceCollectionBuilder<P, T> WithOne(string? nav) => new();
        public object WithMany(Expression<Func<P, IEnumerable<T>?>>? nav = null) => new();
    }
    public class ReferenceCollectionBuilder<T, P> where T : class where P : class
    {
        public ReferenceCollectionBuilder<T, P> HasForeignKey(Expression<Func<T, object?>> k) => this;
        public ReferenceCollectionBuilder<T, P> HasForeignKey(params string[] k) => this;
        public ReferenceCollectionBuilder<T, P> HasPrincipalKey(Expression<Func<P, object?>> k) => this;
        public ReferenceCollectionBuilder<T, P> OnDelete(DeleteBehavior d) => this;
        public ReferenceCollectionBuilder<T, P> IsRequired(bool b = true) => this;
        public ReferenceCollectionBuilder<T, P> HasConstraintName(string n) => this;
    }
    public class ReferenceReferenceBuilder<T, P> where T : class where P : class
    {
        public ReferenceReferenceBuilder<T, P> HasForeignKey<D>(Expression<Func<D, object?>> k) => this;
        public ReferenceReferenceBuilder<T, P> HasForeignKey<D>(params string[] k) => this;
        public ReferenceReferenceBuilder<T, P> HasPrincipalKey<D>(Expression<Func<D, object?>> k) => this;
        public ReferenceReferenceBuilder<T, P> OnDelete(DeleteBehavior d) => this;
        public ReferenceReferenceBuilder<T, P> IsRequired(bool b = true) => this;
    }

    public static class WebStubDiExt
    {
        /// <summary>Harness: 1 DbContext dùng chung (dữ liệu trong bộ nhớ) cho cả app — thay AddDbContext thật.</summary>
        public static IServiceCollection AddDbContext<T>(this IServiceCollection s, Action<DbContextOptionsBuilder>? o = null) where T : DbContext
        {
            var ctx = (T)Activator.CreateInstance(typeof(T), new DbContextOptions<T>())!;
            s.AddSingleton(ctx);
            return s;
        }
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public static class WebStubDbSetExt
    {
        public static ValueTask<EntityEntry<T>> AddAsync<T>(this DbSet<T> s, T e, CancellationToken ct = default) where T : class { s.Add(e); return ValueTask.FromResult(new EntityEntry<T> { Entity = e }); }
        public static void Update<T>(this DbSet<T> s, T e) where T : class { }
    }
}

// Kết nối giả cho code cũ (khoá Session qua GetDbConnection / sp_getapplock): mọi lệnh trả 0 = "khoá được"
namespace HarnessFake
{
    using System.Collections;
    using System.Data;
    using System.Data.Common;
    public class FakeConn : DbConnection
    {
        public static readonly FakeConn Instance = new();
        ConnectionState _st = ConnectionState.Closed;
        public override string ConnectionString { get; set; } = "";
        public override string Database => "MES";
        public override string DataSource => "fake";
        public override string ServerVersion => "0";
        public override ConnectionState State => _st;
        protected override DbTransaction BeginDbTransaction(IsolationLevel il) => throw new NotSupportedException();
        public override void ChangeDatabase(string db) { }
        public override void Close() { _st = ConnectionState.Closed; }
        public override void Open() { _st = ConnectionState.Open; }
        protected override DbCommand CreateDbCommand() => new FakeCmd();
    }
    public class FakeCmd : DbCommand
    {
        readonly FakeParams _p = new();
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _p;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            Microsoft.EntityFrameworkCore.DatabaseFacade.RawLog.Add(CommandText + " " + string.Join(",", _p.Items.Select(x => x.ParameterName + "=" + x.Value)));
            foreach (var x in _p.Items) if (x.Direction == ParameterDirection.ReturnValue) x.Value = 0;
            return 0;
        }
        public override object? ExecuteScalar() => 0;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeParam();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior b) => throw new NotSupportedException();
    }
    public class FakeParam : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }
    public class FakeParams : DbParameterCollection
    {
        public readonly List<DbParameter> Items = new();
        public override int Count => Items.Count;
        public override object SyncRoot => Items;
        public override int Add(object v) { Items.Add((DbParameter)v); return Items.Count - 1; }
        public override void AddRange(Array vs) { foreach (var v in vs) Add(v); }
        public override void Clear() => Items.Clear();
        public override bool Contains(object v) => Items.Contains((DbParameter)v);
        public override bool Contains(string n) => Items.Any(x => x.ParameterName == n);
        public override void CopyTo(Array a, int i) { }
        public override IEnumerator GetEnumerator() => Items.GetEnumerator();
        protected override DbParameter GetParameter(int i) => Items[i];
        protected override DbParameter GetParameter(string n) => Items.First(x => x.ParameterName == n);
        public override int IndexOf(object v) => Items.IndexOf((DbParameter)v);
        public override int IndexOf(string n) => Items.FindIndex(x => x.ParameterName == n);
        public override void Insert(int i, object v) => Items.Insert(i, (DbParameter)v);
        public override void Remove(object v) => Items.Remove((DbParameter)v);
        public override void RemoveAt(int i) => Items.RemoveAt(i);
        public override void RemoveAt(string n) => Items.RemoveAll(x => x.ParameterName == n);
        protected override void SetParameter(int i, DbParameter v) => Items[i] = v;
        protected override void SetParameter(string n, DbParameter v) { var i = IndexOf(n); if (i >= 0) Items[i] = v; else Items.Add(v); }
    }
}
