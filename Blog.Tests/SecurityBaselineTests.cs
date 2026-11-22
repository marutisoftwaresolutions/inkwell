using Blog.Core.Services;
using Blog.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The security baseline: defensive response headers on everything the site serves, uploads stored
/// under an extension that belongs to their checked type, SVG sanitised, and the profile photo upload
/// (open to every signed-in account) accepting pictures only.
/// </summary>
public class SecurityBaselineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SecurityBaselineTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // ── Stored extension ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("photo.JPG", "image/jpeg", ".jpg")]
    [InlineData("photo.jpeg", "image/jpeg", ".jpeg")]
    [InlineData("report.pdf", "application/pdf", ".pdf")]
    [InlineData("page.html", "application/pdf", ".pdf")]          // the name lies: stored as what was checked
    [InlineData("shell.aspx", "image/png", ".png")]
    [InlineData("script.svg", "image/png", ".png")]
    [InlineData("noextension", "image/webp", ".webp")]
    [InlineData("page.html", "text/html", ".bin")]                // a type we do not serve
    [InlineData("x.php", "application/octet-stream", ".bin")]
    [InlineData("logo.svg", "image/svg+xml; charset=utf-8", ".svg")]
    public void An_upload_is_stored_under_an_extension_of_its_declared_type(string name, string mime, string expected) =>
        Assert.Equal(expected, UploadFileTypes.SafeExtension(name, mime));

    [Fact]
    public void Nothing_in_the_table_is_a_type_a_browser_runs()
    {
        foreach (var risky in new[] { ".html", ".htm", ".js", ".mjs", ".xml", ".xhtml", ".aspx", ".cshtml", ".php", ".config", ".exe" })
            foreach (var mime in new[] { "image/jpeg", "image/png", "image/svg+xml", "application/pdf", "video/mp4", "text/html", "application/javascript" })
                Assert.NotEqual(risky, UploadFileTypes.SafeExtension("file" + risky, mime));
    }

    // ── Headers ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/css/inkwell.css")]     // a static file
    [InlineData("/no-such-page-for-headers")]   // an error page
    [InlineData("/account/login")]
    public async Task Every_response_carries_the_defensive_headers(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("SAMEORIGIN", Header(response, "X-Frame-Options"));
        Assert.Contains("frame-ancestors 'self'", Header(response, "Content-Security-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
        // Report-only by default: it describes what a stock page loads and blocks nothing.
        Assert.Contains("object-src 'none'", Header(response, "Content-Security-Policy-Report-Only"));
        // Remembered by browsers for a year, so never sent unless the operator asks for it.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_headers_can_be_switched_off_in_configuration()
    {
        using var off = _factory.WithWebHostBuilder(b => b.UseSetting("Security:Headers:Enabled", "false"));
        var response = await off.CreateClient().GetAsync("/css/inkwell.css");
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
    }

    [Fact]
    public async Task An_operator_policy_is_enforced_and_keeps_the_framing_rule()
    {
        using var custom = _factory.WithWebHostBuilder(b => b.UseSetting("Security:Headers:ContentSecurityPolicy", "default-src 'self'"));
        var response = await custom.CreateClient().GetAsync("/css/inkwell.css");
        Assert.Equal("default-src 'self'; frame-ancestors 'self'", Header(response, "Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Theory]
    [InlineData("/uploads/images/2026-11/logo.svg", true)]
    [InlineData("/uploads/branding/LOGO.SVG", true)]
    [InlineData("/uploads/images/2026-11/photo.webp", false)]
    [InlineData("/images/site-logo.svg", false)]     // shipped with the app, not uploaded
    public void Only_uploaded_svg_files_get_the_script_free_policy(string path, bool expected) =>
        Assert.Equal(expected, SecurityHeadersMiddleware.IsUploadedSvg(new PathString(path)));

    [Fact]
    public void The_svg_policy_allows_no_script_and_sandboxes_the_document()
    {
        Assert.Contains("default-src 'none'", SecurityHeadersMiddleware.SvgPolicy);
        Assert.Contains("sandbox", SecurityHeadersMiddleware.SvgPolicy);
        Assert.DoesNotContain("script-src", SecurityHeadersMiddleware.SvgPolicy);
    }

    // ── Wiring (source checks) ───────────────────────────────────────────────

    [Fact]
    public void The_headers_middleware_sits_after_the_firewall_and_before_static_files()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Program.cs"));
        var firewall = program.IndexOf("app.UseIpFirewall()", StringComparison.Ordinal);
        var headers = program.IndexOf("SecurityHeadersMiddleware", StringComparison.Ordinal);
        var statics = program.IndexOf("app.UseStaticFiles()", StringComparison.Ordinal);
        Assert.True(firewall > 0 && headers > firewall && headers < statics);
    }

    [Fact]
    public void Uploads_are_sanitised_and_renamed_in_the_one_shared_save_path()
    {
        var save = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Services", "ImageProcessingService.cs"));
        Assert.Contains("UploadFileTypes.SafeExtension(", save);
        Assert.Contains("SvgSanitizer.Sanitize(", save);
        Assert.DoesNotContain("Path.GetExtension(originalFileName)", save);
    }

    [Fact]
    public void The_profile_photo_is_decoded_and_re_encoded_never_stored_as_sent()
    {
        var account = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "AccountController.cs"));
        Assert.Contains("Image.LoadAsync(", account);
        Assert.Contains("SaveAsWebpAsync(", account);
        Assert.DoesNotContain("avatarFile.CopyToAsync(", account);
        Assert.DoesNotContain("Path.GetFileName(avatarFile.FileName)", account);
    }

    [Theory]
    [InlineData("https://example.com/me.jpg", true)]
    [InlineData("/uploads/avatars/abc.webp", true)]
    [InlineData("/avatar/Demo%20Admin", true)]
    [InlineData("/uploads/avatars/../../appsettings.json", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("\\\\server\\share\\x.png", false)]
    public void A_typed_photo_address_is_a_web_address_or_one_of_our_uploads(string url, bool expected) =>
        Assert.Equal(expected, Blog.Web.Controllers.AccountController.IsSafeAvatarUrl(url));

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
        : response.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : null;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
