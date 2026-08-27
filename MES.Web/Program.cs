using MES.Web.Components;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ==== Blazor Server ====
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ==== EF Core / SQL Server ====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ==== Cookie Auth ====
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "MES.Auth";
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// ==== Services ====
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<KhPlanService>();
builder.Services.AddScoped<PartMasterService>();
builder.Services.AddScoped<ThietBiService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<DoGaService>();
builder.Services.AddScoped<StandardWtsTaskService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PartPackagingService>();
builder.Services.AddScoped<CustomerService>();
// Luot 6B - Part Master process step services
builder.Services.AddScoped<PartMachiningService>();
builder.Services.AddScoped<PartTaroService>();
builder.Services.AddScoped<PartBaviaService>();
builder.Services.AddScoped<PartWashingService>();
builder.Services.AddScoped<PartInspectionService>();
// Luot 6C - Part Master listing
builder.Services.AddScoped<PartMasterListService>();
builder.Services.AddScoped<DaoService>();

var app = builder.Build();

// ==== Auto-create DB + seed (prototype only) ====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
        SeedData.EnsureSeeded(db);
        Console.WriteLine("[MES] Database ready.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MES] LỖI kết nối DB: {ex.Message}");
        Console.WriteLine("[MES] Kiểm tra ConnectionString trong appsettings.json");
        throw;
    }
}

// ==== Middleware pipeline ====
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ==== Login/Logout endpoints ====
app.MapPost("/login-submit", async (HttpContext ctx, AuthService auth) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var user = await auth.VerifyCredentialsAsync(username, password);
    if (user == null) return Results.Redirect("/login?error=1");

    var claims = new List<System.Security.Claims.Claim>
    {
        new(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(System.Security.Claims.ClaimTypes.Name, user.Username),
        new("FullName", user.FullName),
        new(System.Security.Claims.ClaimTypes.Role, user.Group?.GroupCode ?? "VIEWER")
    };
    var identity = new System.Security.Claims.ClaimsIdentity(
        claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
}).DisableAntiforgery();

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// ==== Download Excel template ====
app.MapGet("/download/std-update-template.xlsx", () =>
{
    var bytes = ExcelHelper.CreateStdUpdateTemplate();
    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "std-update-template.xlsx");
}).RequireAuthorization();

Console.WriteLine("");
Console.WriteLine("========================================");
Console.WriteLine(" MES đang chạy tại http://localhost:5000");
Console.WriteLine(" Login lần đầu: admin / admin123");
Console.WriteLine(" (Đổi password ngay sau khi login)");
Console.WriteLine("========================================");
Console.WriteLine("");

app.Run();