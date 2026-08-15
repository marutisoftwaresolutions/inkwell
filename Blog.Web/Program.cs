using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Infrastructure;
using Blog.Infrastructure.Data;
using Blog.Web.Middleware;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using SixLabors.ImageSharp.Web.DependencyInjection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel limits globally to prevent server crashes on large uploads
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 104 * 1024 * 1024; // 104 MB limit
});

// ── Infrastructure (DB repositories for posts, media, etc.) ──────────────────
// The data layer is SQL Server only (DapperContext → SqlConnection). If no connection string is
// configured, fall back to SQL Server LocalDB so `dotnet run` works out of the box on a dev machine.
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=inkwell;Trusted_Connection=True;TrustServerCertificate=True;";
builder.Services.AddInfrastructure(connStr);

// ── Core Services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Blog.Core.Interfaces.IEmailService, SmtpEmailService>();
builder.Services.AddHttpClient<ReCaptchaService>();
builder.Services.AddHttpClient<IndexNowService>(c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddScoped<ErrorLogService>();

// ── Security: automatic IP blocking ───────────────────────────────────────────
// Singleton: keeps the rule snapshot and the sliding-window threat scores in memory so the
// enforcement path costs no database work per request.
builder.Services.AddSingleton<Blog.Web.Services.Security.IpFirewallService>();

// ── Content importer (WordPress / Ghost) ──────────────────────────────────────
builder.Services.AddScoped<IImageProcessingService, ImageProcessingService>();
builder.Services.AddScoped<Blog.Web.Services.Import.HtmlRewriter>();
builder.Services.AddScoped<Blog.Web.Services.Import.IContentImporter, Blog.Web.Services.Import.WordPressImporter>();
builder.Services.AddScoped<Blog.Web.Services.Import.IContentImporter, Blog.Web.Services.Import.GhostImporter>();
builder.Services.AddHttpClient<Blog.Web.Services.Import.ImageImportService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.MaxResponseContentBufferSize = 20 * 1024 * 1024; // 20 MB cap per image
    c.DefaultRequestHeaders.UserAgent.ParseAdd("InkwellImporter/1.0");
});
builder.Services.AddScoped<Blog.Web.Services.Import.ImportProcessor>();

// ── Multi-Tenancy ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// ── Authentication (cookie only — no DB, config-based) ────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/accessdenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(options =>
{
    // Role-based policies
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("EditorOrAbove", policy => policy.RequireRole("Admin", "Editor"));

    // Permission-based policies (claims loaded from RBAC tables at sign-in)
    options.AddPolicy("CanEditPosts", policy => policy.RequireClaim("Permission", "posts.edit"));
    options.AddPolicy("CanPublishPosts", policy => policy.RequireClaim("Permission", "posts.publish"));
    options.AddPolicy("CanDeletePosts", policy => policy.RequireClaim("Permission", "posts.delete"));
    options.AddPolicy("CanManagePages", policy => policy.RequireClaim("Permission", "pages.manage"));
    options.AddPolicy("CanManageComments", policy => policy.RequireClaim("Permission", "comments.manage"));
    options.AddPolicy("CanManageCategories", policy => policy.RequireClaim("Permission", "categories.manage"));
    options.AddPolicy("CanManageTags", policy => policy.RequireClaim("Permission", "tags.manage"));
    options.AddPolicy("CanManageMedia", policy => policy.RequireClaim("Permission", "media.manage"));
    options.AddPolicy("CanManageSettings", policy => policy.RequireClaim("Permission", "settings.manage"));
    options.AddPolicy("CanManageThemes", policy => policy.RequireClaim("Permission", "themes.manage"));
});
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddImageSharp();

var oldUploads = Path.Combine(builder.Environment.ContentRootPath, "Uploads");
var newUploads = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
if (Directory.Exists(oldUploads))
{
    try 
    {
        if (!Directory.Exists(newUploads)) Directory.CreateDirectory(newUploads);
        foreach (var dirPath in Directory.GetDirectories(oldUploads, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dirPath.Replace(oldUploads, newUploads));
        foreach (var filePath in Directory.GetFiles(oldUploads, "*.*", SearchOption.AllDirectories))
        {
            var target = filePath.Replace(oldUploads, newUploads);
            if (!File.Exists(target)) File.Move(filePath, target);
        }
        
        // Safety cleanup explicitly requested by user
        Directory.Delete(oldUploads, true);
    } 
    catch { }
}

var app = builder.Build();

// Self-heal stale static AI files: older deployments shipped a static wwwroot/llms.txt (and
// wwwroot/llms-full.txt) that static-file middleware serves BEFORE the dynamic per-tenant
// /llms.txt route — pinning every tenant to one hardcoded, often-wrong summary. Delete them on
// startup so the dynamic, correct route always wins (they are regenerated per request). Non-fatal.
try
{
    var webRoot = app.Environment.WebRootPath
        ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
    foreach (var stale in new[] { "llms.txt", "llms-full.txt", "sitemap.xml", "robots.txt" })
    {
        var path = Path.Combine(webRoot, stale);
        if (File.Exists(path))
        {
            File.Delete(path);
            app.Logger.LogInformation("Removed stale static {File} so the dynamic route serves it.", stale);
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Stale llms.txt cleanup skipped (non-fatal).");
}

// Run migrations and taxonomy seeding on startup
using (var scope = app.Services.CreateScope())
{
    try {
        var migrator = scope.ServiceProvider.GetRequiredService<MigrationService>();
        migrator.RunAsync().Wait();
    } catch (Exception ex) {
        app.Logger.LogError(ex, "Migration Error");
    }

    try {
        var taxonomySeeder = scope.ServiceProvider.GetRequiredService<Blog.Infrastructure.Data.OptometryTaxonomySeeder>();
        // Self-hosted: owner is Guid.Empty (site-wide). Cloud mode resolved per-tenant at runtime.
        taxonomySeeder.SeedAsync(Guid.Empty).Wait();
    } catch (Exception ex) {
        app.Logger.LogError(ex, "Taxonomy Seeder Error");
    }

    try {
        // Backfill author-page slugs for any pre-existing users that have none (idempotent).
        var userRepo = scope.ServiceProvider.GetRequiredService<Blog.Core.Interfaces.IUserRepository>();
        var filled = userRepo.BackfillMissingSlugsAsync().GetAwaiter().GetResult();
        if (filled > 0) app.Logger.LogInformation("User slug backfill: assigned {Count} author slug(s).", filled);
    } catch (Exception ex) {
        app.Logger.LogError(ex, "User Slug Backfill Error");
    }
}

// ── Pipeline ──────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Render the friendly 500 page for unhandled exceptions in production.
    app.UseExceptionHandler("/error/500");
}

// IP firewall — refuse blocked addresses before any other work happens, and score failing
// requests on the way out so scanners block themselves. Fail-open: if it cannot reach the
// database it lets traffic through rather than taking the site down.
app.UseIpFirewall();

// Canonical host redirect — enforce https://www.opticalsoftware.org in production
var canonicalHost = app.Configuration["CanonicalHost"];
if (!string.IsNullOrEmpty(canonicalHost) && !app.Environment.IsDevelopment())
{
    app.Use(async (ctx, next) =>
    {
        var req = ctx.Request;
        if (req.Scheme != "https" || !string.Equals(req.Host.Value, canonicalHost, StringComparison.OrdinalIgnoreCase))
        {
            var url = $"https://{canonicalHost}{req.PathBase}{req.Path}{req.QueryString}";
            ctx.Response.StatusCode = 301;
            ctx.Response.Headers.Location = url;
            return;
        }
        await next();
    });
}

// Lowercase-path canonicalization — 301 inbound mixed-case page URLs to their lowercase form.
// (RouteOptions.LowercaseUrls above only affects GENERATED links, not inbound requests, so an
// inbound /Best-Optometry-EHR-Software-2026 would otherwise serve a 200 duplicate.) Skips static
// assets (paths with a file extension) and non-GET/HEAD so filenames and form posts are untouched.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value;
    if (!string.IsNullOrEmpty(path) && path.Length > 1
        && (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
        && !System.IO.Path.HasExtension(path)
        && path.Any(char.IsUpper))
    {
        var lower = path.ToLowerInvariant();
        ctx.Response.StatusCode = 301;
        ctx.Response.Headers.Location = $"{ctx.Request.PathBase}{lower}{ctx.Request.QueryString}";
        return;
    }
    await next();
});

// Error logging — record unhandled exceptions (500) grouped, then rethrow so the
// dev page / UseExceptionHandler above renders the response. Non-fatal (swallows its own errors).
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var svc = ctx.RequestServices.GetService<Blog.Web.Services.ErrorLogService>();
        if (svc != null) await svc.RecordAsync(ctx, StatusCodes.Status500InternalServerError, ex);
        throw;
    }
});

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseImageSharp(); // Intercepts image requests from wwwroot automatically!
app.UseStaticFiles(); // Core wwwroot file serving

// HEAD support for MVC routes. Runs after static files (which already handle HEAD natively) and
// before routing, which matches methods exactly and would otherwise answer every HEAD with a 405.
app.UseHeadRequests();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantResolution();
app.UsePageViewTracking();

// ── Routes ────────────────────────────────────────────────────────────────────
// Attribute-routed controllers (admin, API)
app.MapControllers();

// Conventional routes
app.MapControllerRoute(
    name: "setup",
    pattern: "setup/{action=Index}/{id?}",
    defaults: new { controller = "Setup" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

public partial class Program { }
