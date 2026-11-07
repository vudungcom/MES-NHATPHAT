using MES.Web.Components;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ==== Blazor Server ====
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();   // [RS-E11 2026-10-30] chỉ bật khi Development — trước: luôn bật, lỗi hiện chi tiết code cho người dùng
    // [BLZ-RECONNECT 2026-11-06] Tab để lâu / mạng chập chờn: máy chủ giữ phiên đã mất kết nối 30 phút (mặc định 3 phút) để trình duyệt
    //   nối lại đúng phiên, không mất ô đang gõ. Máy chủ khởi động lại thì phiên mất hẳn → App.razor tự tải lại trang.
    //   Chỉ cấu hình, không đổi luật. Nguồn: CLAUDE 31/10 (BLZ-RECONNECT, Hùng "để sau"), Hùng 05/11 15:37 cho làm nhóm A.
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
});

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
        options.Cookie.HttpOnly = true;                                   // [RS-E11] JS không đọc được cookie đăng nhập
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // [RS-E11] chạy sau Caddy (HTTPS) → cookie Secure (nhờ UseForwardedHeaders)
        // [RS-C3 2026-10-30] Mỗi request kiểm lại DB: tài khoản / nhóm bị khoá, đổi nhóm → đăng xuất ngay (trước: giữ quyền tới hết 8 giờ)
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            bool ok;
            try { ok = await AuthGuard.IsStillValidAsync(db, ctx.Principal); } catch { ok = true; }   // DB lỗi tạm thời: không đá ra
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// [RS-E11 2026-10-30] Chạy sau Caddy / proxy cùng máy: nhận X-Forwarded-Proto (https) + IP thật → cookie Secure đúng, giới hạn đăng nhập theo IP thật.
//   Chỉ tin proxy chạy trên chính máy này (mặc định KnownProxies = 127.0.0.1 / ::1). HTTPS + chuyển http→https do Caddy làm.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
// [RS-C3 2026-10-30] Mạch Blazor đang mở kiểm lại tài khoản mỗi 1 phút (khoá / đổi nhóm → về đăng nhập)
builder.Services.AddScoped<AuthenticationStateProvider, MesRevalidatingAuthStateProvider>();

// ==== Services ====
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<KhPlanService>();
builder.Services.AddScoped<PartMasterService>();
builder.Services.AddScoped<ThietBiService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<DoGaService>();
builder.Services.AddScoped<StandardWtsTaskService>();
builder.Services.AddScoped<WtsCategoryGroupMappingService>(); // ==== WTS Category Mapping ====
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PartPackagingService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<WtsReportService>();
builder.Services.AddScoped<HandoverService>();
builder.Services.AddScoped<HandoverLedgerService>(); // [GN-LEDGER 2026-10-22] hàm tính chung Giao nhận
builder.Services.AddScoped<NgCaseService>();   // [DS-LOI][VULOI 2026-10-28] danh mục lỗi + vụ lỗi
builder.Services.AddScoped<WtsSaveService>();   // [WTS-SVC 2026-10-29] phần lưu / xoá WTS (tách từ HieuSuat_Index.razor)
// Đăng ký tiến trình chạy ngầm duy trì bản quyền
builder.Services.AddHostedService<MES.Web.Services.LicenseBackgroundWorker>();
// Luot 6B - Part Master process step services
builder.Services.AddScoped<PartMachiningService>();
builder.Services.AddScoped<PartStepStdTimeConfirmService>();
builder.Services.AddScoped<StdUpdateService>();   // [STD-UPDATE] Nút "Update TG chuẩn" ở Part Master
builder.Services.AddScoped<PartTaroService>();
builder.Services.AddScoped<PartBaviaService>();
builder.Services.AddScoped<PartWashingService>();
builder.Services.AddScoped<PartInspectionService>();
builder.Services.AddScoped<KhoVatLieuService>();
// Luot 6C - Part Master listing
builder.Services.AddScoped<PartMasterListService>();
builder.Services.AddScoped<DaoService>();
builder.Services.AddScoped<MachineEfficiencyService>();
builder.Services.AddScoped<CaLamViecService>();

var app = builder.Build();

// [S8D-KHOA 2026-11-07] Khoá lượt lưu Part Master giữ trên kết nối riêng không pool (xem PmSaveLockSql) — trang chết giữa lượt lưu không để khoá treo.
PartStepGuard.SessionLockOpener = PmSaveLockSql.OpenAsync;

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

app.UseForwardedHeaders();   // [RS-E11] trước mọi middleware khác
// [UPLOAD-STATIC 2026-11-04] Thư mục /uploads (ảnh đại diện Sơ đồ tổ chức…) chỉ phát file ảnh .jpg / .jpeg / .png / .webp,
//   file khác (vd .html / .js / .svg tải lên trước khi có [PHAT-HIEN-0311]) trả 404; thêm nosniff để trình duyệt không đoán kiểu.
//   Nguồn: CLAUDE (rà độc lập PHAT-HIEN-0311, 04/11).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/uploads"))
    {
        var ext = Path.GetExtension(ctx.Request.Path.Value ?? "").ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    await next();
});
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

    // [RS-C4 2026-10-30] Giới hạn đăng nhập sai (5 lần / 15 phút / tên + máy; 20 lần / IP) — trước: thử không giới hạn
    var ip  = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
    var now = DateTime.Now;
    var safeReturn = AuthGuard.SafeReturnUrl(returnUrl);
    string Back(string q) => "/login?" + q + (safeReturn != "/" ? "&returnUrl=" + Uri.EscapeDataString(safeReturn) : "");
    if (LoginThrottle.LockedMinutes(username, ip, now) is int waitMin)
        return Results.Redirect(Back("error=2&wait=" + waitMin));
    var user = await auth.VerifyCredentialsAsync(username, password);
    if (user == null)
    {
        LoginThrottle.Fail(username, ip, now);
        return LoginThrottle.LockedMinutes(username, ip, now) is int w2 ? Results.Redirect(Back("error=2&wait=" + w2)) : Results.Redirect(Back("error=1"));
    }
    LoginThrottle.Success(username, ip);
    // [RS-C3] Nhóm của tài khoản đang khoá / chưa có nhóm → báo rõ (trước bản này vào được; nay phiên bị kiểm nhóm nên không cho vào rồi đá ra im lặng)
    if (!AuthGuard.AccountUsable(user)) return Results.Redirect(Back("error=3"));

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
    return Results.Redirect(safeReturn);   // [RS-C4] chỉ đường dẫn nội bộ — trước: returnUrl bất kỳ (chuyển sang trang lạ sau đăng nhập)
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
Console.WriteLine(" (Tài khoản mặc định: đổi mật khẩu ngay sau lần đăng nhập đầu)");   // [RS-C4] không in mật khẩu mặc định
Console.WriteLine("========================================");
Console.WriteLine("");

app.Run();
