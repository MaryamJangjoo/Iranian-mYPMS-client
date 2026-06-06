// Program.cs — mYPMS ASP.NET Core entry point

using Microsoft.EntityFrameworkCore;
using mYPMS.Data;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using mYPMS.Models;

var waOptions = new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    Args            = args,
    ApplicationName = System.Diagnostics.Process.GetCurrentProcess().ProcessName,
};

WebApplicationBuilder builder = WebApplication.CreateBuilder(waOptions);

builder.Host.UseWindowsService();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrgins", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<mYPMSContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CNS")!));

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount   = false;
    options.Password.RequiredLength         = 4;
    options.Password.RequireDigit           = false;
    options.Password.RequireLowercase       = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase       = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<mYPMSContext>();

// ── MVC + JSON ────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddControllers()
    .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// ── Session ───────────────────────────────────────────────────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(option =>
{
    option.IdleTimeout        = TimeSpan.FromHours(2);
    option.Cookie.HttpOnly    = true;
    option.Cookie.IsEssential = true;
    option.Cookie.Name        = ".mYPMS.Session";
});

// ── ALPR options (reads BaseUrl + MinConfidence from appsettings.json) ────────
builder.Services.Configure<AlprOptions>(
    builder.Configuration.GetSection("Alpr"));

// ── SatpaClient — typed HttpClient for ALPR FastAPI server ────────────────────
// Injected into HomeController. BaseAddress + Timeout set inside SatpaClient ctor.
builder.Services.AddHttpClient<SatpaClient>();

// ── Camera HttpClient — for IP camera snapshot downloads ──────────────────────
// Short-lived clients are built per-gate in CaptureImageAsync because credentials
// differ per gate. This named client is available if gates share credentials.
builder.Services.AddHttpClient("camera")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        UseCookies = false,
    });

// ── Static files / optimiser ──────────────────────────────────────────────────
builder.Services.AddWebOptimizer(pipeline =>
{
    pipeline.MinifyJsFiles("js/**/*.js");
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();
// ─────────────────────────────────────────────────────────────────────────────

// ── Exception handling ────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// ── Database ──────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<mYPMSContext>();
    context.Database.EnsureCreated();
}

// ── Culture ───────────────────────────────────────────────────────────────────
CultureInfo.DefaultThreadCurrentCulture   = PersianDateExtensionMethods.GetPersianCulture();
CultureInfo.DefaultThreadCurrentUICulture = PersianDateExtensionMethods.GetPersianCulture();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseWebOptimizer();
app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType    = "text/html",
});
app.UseRouting();
app.UseCors("AllowAllOrgins");
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// ── Startup log ───────────────────────────────────────────────────────────────
// SatpaClient is lazy — no Init() call needed at startup.
// First plate read triggers the connection automatically.
// Health status visible via GET /Home/Licence → ApiStatus: "online"/"offline"
var startupLogger = app.Services
                       .GetRequiredService<ILoggerFactory>()
                       .CreateLogger("Startup");

startupLogger.LogInformation("mYPMS started — ALPR lazy mode (SatpaClient)");
startupLogger.LogInformation("ALPR endpoint: {Url}",
    builder.Configuration["Alpr:BaseUrl"] ?? "not configured");

// ── Run ───────────────────────────────────────────────────────────────────────
app.Run();