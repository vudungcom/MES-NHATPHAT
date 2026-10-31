// Tự kiểm Core.cs trên DB giả (1 DbContext dùng chung, chạy tuần tự) — chỉ để biên dịch + kiểm logic kịch bản, không đo đồng thời.
using MES.Web.Data;
using MES.Web.Data.Entities;
namespace SqlDongThoi;
public static class Host
{
    static AppDbContext? _db;
    public static AppDbContext NewDb()
    {
        if (_db != null) return _db;
        var db = new AppDbContext();
        var g = new UserGroup { GroupId = 1, GroupCode = "ADMIN", IsActive = true };
        db.UserGroups.List.Add(g);
        db.Users.List.Add(new User { UserId = 1, Username = "admin", FullName = "Admin", IsActive = true, Group = g, GroupId = 1 });
        db.Customers.List.Add(new Customer { CustomerId = 1, CustomerName = "KH", IsActive = true });
        db.PartMasters.List.Add(new PartMaster { PartId = 1, PartNo = "P" });
        db.Fixup();
        return _db = db;
    }
    public static ValueTask ReleaseAsync(AppDbContext db) => ValueTask.CompletedTask;
    public static Task<string> DbNameAsync(AppDbContext db) => Task.FromResult("MES_TEST");
    public static async Task<int> Main(string[] args)
    {
        Core.Sequential = true; Core.Workers = 1; Core.Seconds = args.Length > 0 ? int.Parse(args[0]) : 5; Core.PoCount = 2; Core.Phoi = 50;
        return await Core.MainAsync();
    }
}
