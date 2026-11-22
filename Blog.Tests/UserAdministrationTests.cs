using System.Security.Claims;
using Blog.Web.Models;
using Blog.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Faults found by the end-to-end pass of 2026-11-15. The Users screen hid "invite", "change role" and "disable" from
/// everyone but an Admin, while the server accepted all three from any Author who posted the form by hand: an Author
/// could create an Admin, promote themselves or disable the owner. The sidebar also offered Settings, Theme and Menus
/// to accounts those screens turn away, and a text file sent as a PNG was stored as a picture.
/// </summary>
public class UserAdministrationTests
{
    [Theory]
    [InlineData("[HttpPost(\"invite\")]")]
    [InlineData("[HttpPost(\"update-role\")]")]
    [InlineData("[HttpPost(\"toggle-active\")]")]
    public void Creating_promoting_and_disabling_accounts_is_admin_only_on_the_server(string route)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "UsersController.cs"));
        var at = source.IndexOf(route, StringComparison.Ordinal);
        Assert.True(at > 0, route + " not found");
        var signature = source.IndexOf("public async Task<IActionResult>", at, StringComparison.Ordinal);
        var attributes = source[at..signature];
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", attributes);
        Assert.Contains("[ValidateAntiForgeryToken]", attributes);
    }

    [Fact]
    public void Every_write_action_on_users_names_who_may_call_it()
    {
        // Delete keeps its own rule (an Admin, or the account that invited the user); everything else is Admin only.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "UsersController.cs"));
        var posts = System.Text.RegularExpressions.Regex.Matches(source, "\\[HttpPost\\(\"([a-z-]+)\"\\)\\]");
        Assert.True(posts.Count >= 4);
        foreach (System.Text.RegularExpressions.Match m in posts)
        {
            var end = source.IndexOf("public async Task<IActionResult>", m.Index, StringComparison.Ordinal);
            var guarded = source[m.Index..end].Contains("AdminOnly");
            Assert.True(guarded || m.Groups[1].Value == "delete", m.Groups[1].Value + " has no AdminOnly policy");
        }
    }

    [Fact]
    public void The_sidebar_offers_settings_theme_and_menus_only_to_accounts_those_screens_accept()
    {
        var author = Principal("Author", "posts.edit", "media.manage");
        var hrefs = DeskNavigation.Visible(author, cloudMode: false).Select(i => i.Href).ToList();
        Assert.DoesNotContain("/admin/settings", hrefs);
        Assert.DoesNotContain("/admin/theme", hrefs);
        Assert.DoesNotContain("/admin/menus", hrefs);
        Assert.DoesNotContain("/admin/users", hrefs);
        Assert.Contains("/admin/posts", hrefs);
        Assert.Contains("/account/profile", hrefs);

        var admin = Principal("Admin", "settings.manage", "themes.manage");
        var all = DeskNavigation.Visible(admin, cloudMode: false).Select(i => i.Href).ToList();
        Assert.Contains("/admin/settings", all);
        Assert.Contains("/admin/theme", all);
        Assert.Contains("/admin/menus", all);
        Assert.Contains("/admin/users", all);

        // A tenant in cloud mode who holds the permission keeps the link.
        var tenant = Principal("Author", "settings.manage");
        Assert.Contains("/admin/settings", DeskNavigation.Visible(tenant, cloudMode: true).Select(i => i.Href));
    }

    [Fact]
    public void Bytes_that_are_not_a_picture_are_recognised_as_such()
    {
        Assert.False(ImageGuard.IsPicture("<html><script>alert(1)</script></html>"u8.ToArray()));
        Assert.False(ImageGuard.IsPicture(Array.Empty<byte>()));

        using var image = new Image<Rgba32>(4, 3);
        using var png = new MemoryStream();
        image.SaveAsPng(png);
        Assert.True(ImageGuard.IsPicture(png.ToArray()));

        // A picture the decoder cannot read is still a picture: the check is the file's signature.
        Assert.True(ImageGuard.IsPicture(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC")));
        Assert.True(ImageGuard.IsPicture(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1 }));
        Assert.True(ImageGuard.IsPicture(System.Text.Encoding.ASCII.GetBytes("GIF89a-placeholder-bytes")));
        Assert.True(ImageGuard.IsPicture(System.Text.Encoding.ASCII.GetBytes("RIFF0000WEBPVP8 ")));
        Assert.False(ImageGuard.IsPicture(System.Text.Encoding.ASCII.GetBytes("RIFF0000WAVEfmt ")));
    }

    [Fact]
    public void An_upload_that_claims_to_be_a_picture_is_checked_before_it_is_stored()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Services", "ImageProcessingService.cs"));
        var check = service.IndexOf("!ImageGuard.IsPicture(bytes)", StringComparison.Ordinal);
        var write = service.IndexOf("File.WriteAllBytesAsync(destPath, bytes)", StringComparison.Ordinal);
        Assert.True(check > 0 && write > check, "the picture check must come before the file is written");
        Assert.Contains("throw new NotAPictureException();", service);

        var controller = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Controllers", "MediaController.cs"));
        Assert.Contains("catch (NotAPictureException ex)", controller);
    }

    [Fact]
    public void A_form_whose_computed_confirmation_title_is_empty_submits_without_a_dialog()
    {
        // Backups → "Make backup" and Webhooks → "Send again" compute the title and pass null when no confirmation is
        // wanted. Razor writes data-confirm-title="" for that, which used to raise a dialog reading "Confirm this action?".
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "wwwroot", "js", "inkwell-dialog.js"));
        Assert.Contains("(el.getAttribute('data-confirm-title') || '').trim() !== ''", js);
        Assert.DoesNotContain("form.hasAttribute('data-confirm-title')", js);
    }

    [Fact]
    public void The_address_a_new_install_is_sent_to_reaches_the_setup_wizard()
    {
        // FirstRunRedirectMiddleware sends the first visitor to /setup. Without a route of its own that address fell to
        // the post route and answered 404, so a new install opened on "That page isn't here".
        var root = RepoRoot();
        var redirect = File.ReadAllText(Path.Combine(root, "Blog.Web", "Middleware", "FirstRunRedirectMiddleware.cs"));
        Assert.Contains("ctx.Response.Redirect(\"/setup\")", redirect);
        var controller = File.ReadAllText(Path.Combine(root, "Blog.Web", "Controllers", "SetupController.cs"));
        var index = controller.IndexOf("public async Task<IActionResult> Index()", StringComparison.Ordinal);
        var attributes = controller[(index - 120)..index];
        Assert.Contains("[HttpGet(\"/setup\")]", attributes);
        Assert.Contains("[HttpGet(\"/setup/index\")]", attributes);
    }

    private static ClaimsPrincipal Principal(string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role), new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Blog.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
