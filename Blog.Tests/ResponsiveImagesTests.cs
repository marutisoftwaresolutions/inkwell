using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ImageInfo = Blog.Core.Services.ImageInfo;

namespace Blog.Tests;

/// <summary>Responsive images (ADR-0032): naming, widths, the body rewriter, and the store against real files.</summary>
public class ResponsiveImagesTests : IDisposable
{
    // ── Rules ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/uploads/features/a.jpg", 480, "/uploads/features/a.jpg.w480.webp")]
    [InlineData("/uploads/images/2026-10/x.y.png", 800, "/uploads/images/2026-10/x.y.png.w800.webp")]
    [InlineData("/uploads/b.webp", 1200, "/uploads/b.webp.w1200.webp")]
    public void Copies_are_named_beside_the_original(string path, int width, string expected) =>
        Assert.Equal(expected, ResponsiveImages.VariantPath(path, width));

    [Fact]
    public void A_name_with_a_space_is_found_on_disk_and_written_safely_into_srcset()
    {
        Assert.Equal("/uploads/my photo.jpg", ResponsiveImages.UploadPath("/uploads/my%20photo.jpg?v=2"));
        Assert.Null(ResponsiveImages.UploadPath("/uploads/%2E%2E/appsettings.json"));   // no way out of uploads, encoded or not
        var set = ResponsiveImages.Srcset("/uploads/my photo, final.jpg", new ImageInfo(1000, 600, new[] { 480 }));
        Assert.Equal("/uploads/my%20photo%2C%20final.jpg.w480.webp 480w, /uploads/my%20photo%2C%20final.jpg 1000w", set);
    }

    [Theory]
    [InlineData("/uploads/a.jpg", true)]
    [InlineData("/uploads/a.JPEG", true)]
    [InlineData("/uploads/a.png", true)]
    [InlineData("/uploads/a.webp", true)]
    [InlineData("/uploads/a.jpg.w480.webp", false)] // a copy is never copied again
    [InlineData("/uploads/banner-w1200.webp", true)] // only looks like an old-style copy: an original
    [InlineData("/uploads/a.gif", false)]           // may be animated
    [InlineData("/uploads/a.svg", false)]           // already scalable
    public void Only_raster_originals_get_copies(string path, bool expected) => Assert.Equal(expected, ResponsiveImages.IsSource(path));

    [Fact]
    public void An_image_gets_only_narrower_copies()
    {
        Assert.Equal(new[] { 480, 800, 1200 }, ResponsiveImages.WidthsFor(2400));
        Assert.Equal(new[] { 480, 800 }, ResponsiveImages.WidthsFor(1200));
        Assert.Empty(ResponsiveImages.WidthsFor(480));
    }

    [Theory]
    [InlineData("/uploads/a.jpg?v=3", "/uploads/a.jpg")]
    [InlineData("/uploads/a.jpg#x", "/uploads/a.jpg")]
    [InlineData("https://cdn.example/uploads/a.jpg", null)]
    [InlineData("/uploads/../appsettings.json", null)]
    [InlineData("/images/logo.png", null)]
    [InlineData("", null)]
    public void Only_local_upload_paths_are_considered(string src, string? expected) => Assert.Equal(expected, ResponsiveImages.UploadPath(src));

    private static readonly ImageInfo Wide = new(1600, 900, new[] { 800, 480, 1200 });
    private static ImageInfo? Describe(string path) => path == "/uploads/a.jpg" ? Wide : path == "/uploads/small.jpg" ? new ImageInfo(400, 300, Array.Empty<int>()) : null;

    [Fact]
    public void The_srcset_lists_copies_narrowest_first_then_the_original()
    {
        Assert.Equal("/uploads/a.jpg.w480.webp 480w, /uploads/a.jpg.w800.webp 800w, /uploads/a.jpg.w1200.webp 1200w, /uploads/a.jpg 1600w",
            ResponsiveImages.Srcset("/uploads/a.jpg", Wide));
        Assert.Null(ResponsiveImages.Srcset("/uploads/small.jpg", new ImageInfo(400, 300, Array.Empty<int>())));
    }

    [Fact]
    public void Body_images_gain_srcset_sizes_dimensions_and_lazy_loading()
    {
        var html = ResponsiveImages.EnhanceHtml("<p>x</p><img src=\"/uploads/a.jpg\" alt=\"A\"><img src='/uploads/small.jpg' alt='' />", Describe);
        Assert.Contains("<img src=\"/uploads/a.jpg\" alt=\"A\" srcset=\"/uploads/a.jpg.w480.webp 480w, /uploads/a.jpg.w800.webp 800w, /uploads/a.jpg.w1200.webp 1200w, /uploads/a.jpg 1600w\" sizes=\"" + ResponsiveImages.BodySizes + "\" width=\"1600\" height=\"900\" loading=\"lazy\" decoding=\"async\">", html);
        Assert.Contains("<img src='/uploads/small.jpg' alt='' width=\"400\" height=\"300\" loading=\"lazy\" decoding=\"async\" />", html);   // no copies: dimensions only
        Assert.StartsWith("<p>x</p>", html);
    }

    [Fact]
    public void What_the_author_set_is_kept_and_foreign_images_are_untouched()
    {
        var own = "<img src=\"/uploads/a.jpg\" srcset=\"/x.jpg 1x\">";
        Assert.Equal(own, ResponsiveImages.EnhanceHtml(own, Describe));
        var sized = ResponsiveImages.EnhanceHtml("<img src=\"/uploads/a.jpg\" width=\"300\" sizes=\"300px\" loading=\"eager\">", Describe);
        Assert.Contains("sizes=\"300px\"", sized);
        Assert.DoesNotContain("height=\"900\"", sized);           // the author's width stands; no conflicting pair added
        Assert.DoesNotContain("loading=\"lazy\"", sized);
        Assert.Contains("srcset=", sized);
        var foreign = "<img src=\"https://other.example/a.jpg\">";
        Assert.Equal(foreign, ResponsiveImages.EnhanceHtml(foreign, Describe));
        Assert.Equal("", ResponsiveImages.EnhanceHtml(null, Describe));
        Assert.Equal("<p>no images</p>", ResponsiveImages.EnhanceHtml("<p>no images</p>", Describe));
    }

    // ── The store, against real files ───────────────────────────────────────────

    private readonly string _root = Path.Combine(Path.GetTempPath(), "inkwell-ri-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    private ResponsiveImageStore Store() => new(new Env(_root), NullLogger<ResponsiveImageStore>.Instance);

    private void Picture(string rel, int w, int h)
    {
        var file = Path.Combine(_root, rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var img = new Image<Rgba32>(w, h, new Rgba32(30, 90, 160));
        if (file.EndsWith(".png")) img.SaveAsPng(file); else img.SaveAsJpeg(file);
    }

    [Fact]
    public async Task Copies_are_made_once_at_the_right_widths_and_described()
    {
        Picture("/uploads/features/hero.jpg", 1600, 840);
        Picture("/uploads/images/small.png", 300, 200);
        Picture("/uploads/og/card.jpg", 1200, 630);
        var store = Store();

        Assert.Equal(new[] { 480, 800, 1200 }, store.MissingWidths("/uploads/features/hero.jpg"));
        Assert.Equal(3, await store.EnsureVariantsAsync("/uploads/features/hero.jpg"));
        Assert.Equal(0, await store.EnsureVariantsAsync("/uploads/features/hero.jpg"));    // idempotent
        foreach (var w in new[] { 480, 800, 1200 })
        {
            var copy = Path.Combine(_root, "uploads", "features", $"hero.jpg.w{w}.webp");
            Assert.True(File.Exists(copy));
            var info = Image.Identify(copy);
            Assert.Equal(w, info.Width);
            Assert.InRange(info.Height, (int)Math.Round(840.0 * w / 1600) - 1, (int)Math.Round(840.0 * w / 1600) + 1);   // proportions kept
        }
        var described = store.Describe("/uploads/features/hero.jpg")!;
        Assert.Equal((1600, 840), (described.Width, described.Height));
        Assert.Equal(new[] { 480, 800, 1200 }, described.Variants.OrderBy(x => x));
        Assert.Equal(0, await store.EnsureVariantsAsync("/uploads/images/small.png"));    // too small for any copy
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));

        var sources = store.SourceImages().ToList();
        Assert.Contains("/uploads/features/hero.jpg", sources);
        Assert.Contains("/uploads/images/small.png", sources);
        Assert.DoesNotContain("/uploads/og/card.jpg", sources);                          // social cards are left alone
        Assert.DoesNotContain(sources, s => s.Contains(".w480.webp"));
        Assert.Null(store.FileOf("/uploads/../../secret.txt"));
        Assert.Null(store.Describe("/uploads/missing.jpg"));
    }

    [Fact]
    public void Source_scans_views_get_the_tag_helper_and_bodies_are_enhanced_at_render()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { root!.FullName }.Concat(p).ToArray()));
        Assert.Contains("@addTagHelper *, Blog.Web", Read("Blog.Web", "Views", "_ViewImports.cshtml"));
        Assert.Contains("ResponsiveImages.EnhanceHtml(Model.Html", Read("Blog.Web", "Views", "Blog", "Post.cshtml"));
        Assert.Contains("ResponsiveImages.EnhanceHtml(Model.Content", Read("Blog.Web", "Views", "Blog", "Page.cshtml"));
        Assert.Contains("_responsive.EnsureVariantsAsync(saved.RelativeUrl", Read("Blog.Web", "Controllers", "MediaController.cs"));
    }

    private sealed class Env : IWebHostEnvironment
    {
        public Env(string root) { WebRootPath = root; ContentRootPath = root; }
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Blog.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
