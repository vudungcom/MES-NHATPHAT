using MES.Web.Components;
using MES.Web.Data;
using MES.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// [LOG-FILE 2026-11-20] Gói 26: log ra file logs\mes-yyyyMMdd.log (mục "Logging:File" — appsettings.Production.json), từ Warning, giữ 30 ngày; console giữ như cũ.
//   Nguồn: CLAUDE quét Fable 15/11 (QF-F); HÙNG 20/11 18:32 OK.
builder.Logging.AddProvider(FileLoggerProvider.FromConfig(builder.Configuration, builder.Environment.ContentRootPath));

// ==== Blazor Server ====
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();   // [RS-E11 2026-10-30] chỉ bật khi Development — trước: luôn bật, lỗi hiện chi tiết code cho người dùng
    // [C-01 2026-11-15] App 5000 nay chạy Production (tools/chay_app_5000.bat: ASPNETCORE_ENVIRONMENT=Production + --no-launch-profile) → dòng trên = false,
    //   UseExceptionHandler("/error") bên dưới bật; appsettings.json bỏ "DetailedErrors": true. Code không đổi — chỉ ghi để tra tag.
    //   Nguồn: CLAUDE quét Fable 14/11 (A3C C-01 Cao); Hùng 15/11 15:10 OK câu 5.
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
// [C-11 2026-11-16] service không có ILogger (CustomerService, DaoService, WtsSaveService…) ghi lỗi hệ thống vào logger chung này qua UiSafe.Text
UiSafe.Fallback = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MES.UiSafe");
// [PIN-HASH] log khi chưa nâng được PIN sang hash (cột hẹp) đi vào logger thay Console
{ var _pinLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MES.PinHash"); AuthService.PinUpgradeLog = m => _pinLog.LogWarning("{Msg}", m); }

// [SEED-DEMO 2026-11-20] cảnh báo lúc seed DB trống (mật khẩu admin mặc định) đi vào log (cả file) — Gói 26
//   Nguồn: CLAUDE quét Fable 14/11 (C-07); HÙNG 20/11 18:32 OK (B4).
{ var _seedLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MES.Seed"); SeedData.Warn = m => _seedLog.LogWarning("{Msg}", m); }

// ==== Auto-create DB + seed (prototype only) ====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
        SeedData.EnsureSeeded(db);
        Console.WriteLine("[MES] Database ready.");
        // [PIN-HASH] (P-05) PIN còn chữ rõ → nâng lên hash 1 lần lúc khởi động (cần cột Users.PIN ≥ 95 — tools/sql/16_PIN_HASH.sql;
        //   cột còn hẹp thì AuthService ghi log 1 dòng, PIN chữ rõ vẫn so được, app vẫn chạy). Lỗi gì cũng không chặn khởi động.
        try
        {
            var nPin = AuthService.UpgradeAllPlainPinsAsync(db).GetAwaiter().GetResult();
            if (nPin > 0) Console.WriteLine($"[MES] [PIN-HASH] Đã nâng {nPin} PIN chữ rõ sang hash.");
        }
        catch (Exception exPin) { Console.WriteLine($"[MES] [PIN-HASH] Bỏ qua nâng PIN: {exPin.GetType().Name}"); }
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
// [UPLOADS-AUTH 2026-11-16] (C-05) Ảnh /uploads (ảnh đại diện Sơ đồ tổ chức) chỉ phát cho người đã đăng nhập — trước: ai có URL cũng tải được
//   (URL lộ ra ngoài là xem ảnh nhân viên). Chưa đăng nhập → 401. Chỉ xác thực cookie cho đúng đường /uploads (AuthenticateAsync tại chỗ) — không
//   đưa UseAuthentication lên trước UseStaticFiles để file css / js / lib không phải kiểm cookie (OnValidatePrincipal đọc DB) mỗi lần tải.
//   Nguồn: CLAUDE quét Fable 15/11 (A3C C-05 Thấp); Hùng 16/11 11:34 OK Gói 24.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/uploads"))
    {
        var auth = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal?.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = 401; return; }
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
        new(System.Security.Claims.ClaimTypes.Role, user.Group?.GroupCode ?? "VIEWER"),
        new(AuthGuard.PermHashClaim, AuthGuard.PermHash(user.Group?.Permissions))   // [PERM-HASH 2026-11-16] đổi ma trận quyền → phiên cũ hết hiệu lực (AuthGuard.IsStillValidAsync)
    };
    var identity = new System.Security.Claims.ClaimsIdentity(
        claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect(safeReturn);   // [RS-C4] chỉ đường dẫn nội bộ — trước: returnUrl bất kỳ (chuyển sang trang lạ sau đăng nhập)
}).DisableAntiforgery();

// [LOGOUT-POST 2026-11-16] (P-09 / C-03) Đăng xuất bằng POST + antiforgery (nút Đăng xuất ở MainLayout là form POST) — trước: GET /logout không
//   kiểm gì → trang lạ nhúng <img src=".../logout"> là đăng xuất người đang nhập WTS (mất ô đang gõ). GET /logout nay chỉ về trang chủ, không đăng xuất.
//   Nguồn: CLAUDE quét Fable 15/11 (A3C P-09 / C-03 Thấp); Hùng 16/11 11:34 OK Gói 24.
app.MapPost("/logout", async (HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    try { await af.ValidateRequestAsync(ctx); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.Redirect("/login"); }   // token lệch (phiên hết 8h / bị đá) → về /login, KHÔNG đăng xuất (rà độc lập 16/11; trước: 400 trống)
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});   // không có form-binding nên UseAntiforgery không tự kiểm endpoint này → tự gọi ValidateRequestAsync ở trên, sai token trả 400
app.MapGet("/logout", () => Results.Redirect("/"));   // [LOGOUT-POST] GET không còn đăng xuất

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
