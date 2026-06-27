using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Blog.Web.Controllers;

[Route("og-image")]
public class OgImageController : Controller
{
    private const int Width = 1200;
    private const int Height = 630;

    private readonly IPostRepository _posts;
    private readonly ISettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly IUserRepository _users;
    private readonly IWebHostEnvironment _env;

    public OgImageController(IPostRepository posts, ISettingRepository settings,
        ITenantContext tenantContext, IUserRepository users, IWebHostEnvironment env)
    {
        _posts = posts;
        _settings = settings;
        _tenantContext = tenantContext;
        _users = users;
        _env = env;
    }

    [HttpGet("{slug}.png")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Generate(string slug)
    {
        // Serve cached file if it already exists
        var cachePath = Path.Combine(_env.WebRootPath, "og-cache", $"{slug}.png");
        if (System.IO.File.Exists(cachePath))
            return PhysicalFile(cachePath, "image/png");

        var post = await _posts.GetBySlugAsync(slug);
        if (post == null) return NotFound();

        var ownerId = await GetOwnerIdAsync();
        var siteSettings = await _settings.GetSettingsAsync(ownerId);
        var siteName = siteSettings.SiteName ?? "Blog";
        var category = post.Categories.FirstOrDefault()?.Name ?? string.Empty;
        var author = string.IsNullOrEmpty(post.AuthorName) ? string.Empty : $"by {post.AuthorName}";

        var png = GenerateImage(post.Title, siteName, category, author);

        // Cache to disk for subsequent requests
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await System.IO.File.WriteAllBytesAsync(cachePath, png);

        return File(png, "image/png");
    }

    private static byte[] GenerateImage(string title, string siteName, string category, string author)
    {
        // Resolve fonts — prefer Segoe UI or Arial, fall back to first available
        var preferredNames = new[] { "Segoe UI", "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" };
        FontFamily fontFamily = default;
        foreach (var name in preferredNames)
        {
            if (SystemFonts.TryGet(name, out fontFamily)) break;
        }
        if (fontFamily.Name == null)
            fontFamily = SystemFonts.Families.First();

        var titleFont = fontFamily.CreateFont(58, FontStyle.Bold);
        var labelFont = fontFamily.CreateFont(26, FontStyle.Regular);
        var siteFont = fontFamily.CreateFont(28, FontStyle.Bold);

        // Colors
        var bgTop = Color.ParseHex("0f172a");
        var bgBottom = Color.ParseHex("1e293b");
        var accentColor = Color.ParseHex("6366f1"); // indigo accent
        var white = Color.White;
        var muted = Color.ParseHex("94a3b8");

        using var image = new Image<Rgba32>(Width, Height);

        image.Mutate(ctx =>
        {
            // Background gradient (top → bottom via two fills)
            ctx.Fill(bgTop);
            ctx.Fill(new LinearGradientBrush(
                new PointF(0, 0), new PointF(0, Height),
                GradientRepetitionMode.None,
                new ColorStop(0f, bgTop),
                new ColorStop(1f, bgBottom)));

            // Left accent bar
            ctx.Fill(accentColor, new RectangleF(0, 0, 8, Height));

            // Site name — top-left
            var siteOpts = new RichTextOptions(siteFont)
            {
                Origin = new PointF(56, 52),
                WrappingLength = Width - 120,
            };
            ctx.DrawText(siteOpts, siteName, white);

            // Category label — below site name (if set)
            if (!string.IsNullOrEmpty(category))
            {
                var catOpts = new RichTextOptions(labelFont)
                {
                    Origin = new PointF(56, 106),
                };
                ctx.DrawText(catOpts, category.ToUpperInvariant(), accentColor);
            }

            // Post title — center of image, word-wrapped
            var titleY = string.IsNullOrEmpty(category) ? 160f : 190f;
            var titleOpts = new RichTextOptions(titleFont)
            {
                Origin = new PointF(56, titleY),
                WrappingLength = Width - 112,
                LineSpacing = 1.2f,
            };
            // Trim title to ~160 chars to avoid overflow
            var displayTitle = title.Length > 120 ? title[..117] + "…" : title;
            ctx.DrawText(titleOpts, displayTitle, white);

            // Bottom divider line
            ctx.Fill(accentColor, new RectangleF(56, Height - 90, 60, 3));

            // Author — bottom-left
            if (!string.IsNullOrEmpty(author))
            {
                var authorOpts = new RichTextOptions(labelFont)
                {
                    Origin = new PointF(56, Height - 72),
                };
                ctx.DrawText(authorOpts, author, muted);
            }
        });

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private async Task<Guid> GetOwnerIdAsync()
    {
        if (_tenantContext.IsCloudMode && _tenantContext.IsResolved)
            return _tenantContext.UserId;
        var admin = await _users.GetFirstAdminAsync();
        return admin?.Id ?? Guid.Empty;
    }
}
