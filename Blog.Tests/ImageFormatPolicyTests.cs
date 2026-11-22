using System.Text.RegularExpressions;
using Blog.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The image library never reads or writes TIFF. The pinned ImageSharp has published advisories in its TIFF
/// code that only a later, licensed version fixes, so the format is switched off and the build accepts those
/// advisories by name, with a reason, and no others.
/// </summary>
public class ImageFormatPolicyTests
{
    // A little-endian TIFF header: "II", 42, first directory at offset 8, then an empty directory.
    private static readonly byte[] Tiff = { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] BigTiff = { 0x49, 0x49, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    private static byte[] Png()
    {
        using var image = new Image<Rgba32>(3, 2);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void A_tiff_is_not_a_picture_the_library_reads()
    {
        ImageFormatPolicy.Apply();
        ImageFormatPolicy.Apply();   // safe to call twice

        Assert.Throws<UnknownImageFormatException>(() => Image.Identify(Tiff));
        Assert.Throws<UnknownImageFormatException>(() => Image.Load(Tiff));
        Assert.Throws<UnknownImageFormatException>(() => Image.Identify(BigTiff));
        Assert.Throws<UnknownImageFormatException>(() => Image.DetectFormat(Tiff));
    }

    [Fact]
    public void The_formats_the_site_accepts_still_open()
    {
        ImageFormatPolicy.Apply();
        var png = Png();
        var info = Image.Identify(png);
        Assert.Equal(3, info.Width);
        Assert.Equal(2, info.Height);
        Assert.True(ImageGuard.IsPicture(png));
        ImageGuard.Check(png);
        ImageGuard.Check(Tiff);                    // not a picture the library knows: passes through, undecoded
        Assert.False(ImageGuard.IsPicture(Tiff));  // and is refused when sent as a JPG, PNG, GIF or WebP
    }

    [Fact]
    public void The_resizer_configuration_has_no_tiff_and_keeps_the_rest()
    {
        var formats = ImageFormatPolicy.WithoutTiff().ImageFormatsManager;
        Assert.False(formats.TryFindFormatByFileExtension("tif", out _));
        Assert.False(formats.TryFindFormatByFileExtension("tiff", out _));
        Assert.False(formats.TryFindFormatByMimeType("image/tiff", out _));
        foreach (var ext in new[] { "png", "jpg", "gif", "webp", "bmp" })
            Assert.True(formats.TryFindFormatByFileExtension(ext, out _), ext);
    }

    [Fact]
    public void Startup_applies_the_policy_and_nothing_in_the_app_uses_tiff_or_histogram_equalisation()
    {
        var web = Path.Combine(ListingHeadingSourceTests.RepoRoot(), "Blog.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        Assert.Contains("ImageFormatPolicy.Apply();", program);
        Assert.Contains("options.Configuration = Blog.Web.Services.ImageFormatPolicy.WithoutTiff()", program);
        Assert.DoesNotContain("AddImageSharp();", program);

        foreach (var file in Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (Path.GetFileName(file) == "ImageFormatPolicy.cs") continue;
            var source = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(source, @"Formats\.Tiff|SaveAsTiff|TiffEncoder|TiffDecoder|HistogramEqualization"), Path.GetFileName(file) + " uses TIFF or histogram equalisation");
        }
    }

    [Fact]
    public void An_upload_is_checked_by_signature_before_anything_decodes_it()
    {
        var source = File.ReadAllText(Path.Combine(ListingHeadingSourceTests.RepoRoot(), "Blog.Web", "Services", "ImageProcessingService.cs"));
        var signature = source.IndexOf("!ImageGuard.IsPicture(bytes)", StringComparison.Ordinal);
        var decode = source.IndexOf("ImageGuard.Check(bytes)", StringComparison.Ordinal);
        Assert.True(signature > 0 && decode > 0);
        Assert.True(signature < decode, "the signature check must come before the size check, which reads the file with the image library");
    }

    [Fact]
    public void The_build_fails_on_any_advisory_that_is_not_accepted_with_a_reason()
    {
        var root = ListingHeadingSourceTests.RepoRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-and-test.yml"));
        Assert.Contains("package --vulnerable --include-transitive", workflow);
        Assert.Contains(".github/accepted-advisories.txt", workflow);
        Assert.Contains("comm -23 found.txt accepted.txt > unaccepted.txt", workflow);
        Assert.Contains("if [ -s unaccepted.txt ]; then", workflow);

        var lines = File.ReadAllLines(Path.Combine(root, ".github", "accepted-advisories.txt"))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
        Assert.NotEmpty(lines);
        var ids = new HashSet<string>();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^(GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4})\s+(.+)$");
            Assert.True(m.Success, "not an advisory id followed by a reason: " + line);
            Assert.True(m.Groups[2].Value.Trim().Length >= 30, "the reason is too short to be one: " + line);
            Assert.True(ids.Add(m.Groups[1].Value), "listed twice: " + m.Groups[1].Value);
        }
        // An advisory that is accepted without being shown unreachable says so in capitals.
        Assert.Contains(lines, l => l.StartsWith("GHSA-gwg2-r3hj-4w44") && l.Contains("OPEN"));
    }
}
