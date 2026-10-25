using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>What is known about one uploaded image: its size and the narrower copies that exist on disk.</summary>
public sealed record ImageInfo(int Width, int Height, IReadOnlyList<int> Variants);

/// <summary>
/// Responsive images (ADR-0032), the pure half: how narrower copies of an upload are named, which widths an image
/// gets, and how an <c>&lt;img&gt;</c> gains <c>srcset</c>, <c>sizes</c>, <c>width</c> and <c>height</c>. The stored HTML is
/// never changed; pages are enhanced as they are rendered, so an image without copies simply renders as before.
/// </summary>
public static class ResponsiveImages
{
    /// <summary>The widths copies are made at; an image gets only those narrower than itself.</summary>
    public static readonly IReadOnlyList<int> VariantWidths = new[] { 480, 800, 1200 };

    /// <summary>The prose column is at most about 800 px wide on the reader layouts.</summary>
    public const string BodySizes = "(max-width: 800px) 100vw, 800px";
    /// <summary>For images whose layout does not say better: full width up to the largest copy.</summary>
    public const string DefaultSizes = "(max-width: 1200px) 100vw, 1200px";

    // A copy keeps its source's extension (a.jpg → a.jpg.w480.webp), so a.jpg and a.png never share a copy, and an
    // upload that merely ends in "-w1200.webp" is not mistaken for one (or deleted along with another image).
    private static readonly Regex VariantName = new(@"\.(jpe?g|png|webp)\.w\d{2,4}\.webp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] RasterExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>True for a file this feature makes copies of: a JPEG, PNG or WebP upload that is not itself a copy.
    /// GIFs (often animated) and SVGs (already scalable) are left alone.</summary>
    public static bool IsSource(string path) =>
        RasterExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && !VariantName.IsMatch(path);

    public static bool IsVariant(string path) => VariantName.IsMatch(path);

    /// <summary><c>/uploads/a/b.jpg</c> at 480 → <c>/uploads/a/b.jpg.w480.webp</c>.</summary>
    public static string VariantPath(string path, int width) =>
        path + ".w" + width.ToString(CultureInfo.InvariantCulture) + ".webp";

    /// <summary>The copy widths an image of this width should have.</summary>
    public static IReadOnlyList<int> WidthsFor(int originalWidth) => VariantWidths.Where(w => w < originalWidth).ToList();

    /// <summary>The site-relative upload path of an <c>src</c>, without query or fragment; null when it is not an upload.</summary>
    public static string? UploadPath(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        var s = src.Trim();
        if (!s.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || s.Contains("..")) return null;
        var cut = s.IndexOfAny(new[] { '?', '#' });
        var path = cut >= 0 ? s[..cut] : s;
        // "/uploads/my%20photo.jpg" names the file "my photo.jpg" on disk.
        try { path = Uri.UnescapeDataString(path); } catch (UriFormatException) { return null; }
        return path.Contains("..") ? null : path;
    }

    /// <summary>A path as it can appear in <c>srcset</c>, where a space or a comma would split the list.</summary>
    // Each segment escaped in full, so a space, comma, #, ? or quote in a file name can neither split the list nor
    // end the attribute.
    private static string ForSrcset(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>The <c>srcset</c> value: every copy, then the original at its own width. Null when there are no copies.</summary>
    public static string? Srcset(string path, ImageInfo info)
    {
        if (info.Variants.Count == 0) return null;
        var parts = info.Variants.OrderBy(w => w).Select(w => $"{ForSrcset(VariantPath(path, w))} {w}w").ToList();
        parts.Add($"{ForSrcset(path)} {info.Width}w");
        return string.Join(", ", parts);
    }

    private static readonly Regex ImgTag = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Src = new(@"\bsrc\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool HasAttribute(string tag, string name) =>
        Regex.IsMatch(tag, @"\s" + Regex.Escape(name) + @"(\s*=|\s|/?>)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Adds <c>srcset</c> and <c>sizes</c> (when copies exist), <c>width</c> and <c>height</c> (when known and missing, so the
    /// page does not shift as images load), and <c>loading="lazy"</c> and <c>decoding="async"</c> (when missing) to every
    /// upload <c>&lt;img&gt;</c> in a body of HTML. Tags that already carry a <c>srcset</c>, and images that are not uploads,
    /// are left exactly as they are.
    /// </summary>
    public static string EnhanceHtml(string? html, Func<string, ImageInfo?> describe, string sizes = BodySizes)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0) return html ?? "";
        return ImgTag.Replace(html, m =>
        {
            var tag = m.Value;
            if (HasAttribute(tag, "srcset")) return tag;
            var src = Src.Match(tag);
            var path = src.Success ? UploadPath(System.Net.WebUtility.HtmlDecode(src.Groups["v"].Value)) : null;
            if (path is null) return tag;
            var info = describe(path);
            var add = new StringBuilder();
            if (info is not null)
            {
                var set = Srcset(path, info);
                if (set is not null)
                {
                    add.Append(" srcset=\"").Append(set).Append('"');
                    if (!HasAttribute(tag, "sizes")) add.Append(" sizes=\"").Append(sizes).Append('"');
                }
                if (!HasAttribute(tag, "width") && !HasAttribute(tag, "height"))
                    add.Append(" width=\"").Append(info.Width).Append("\" height=\"").Append(info.Height).Append('"');
            }
            if (!HasAttribute(tag, "loading")) add.Append(" loading=\"lazy\"");
            if (!HasAttribute(tag, "decoding")) add.Append(" decoding=\"async\"");
            if (add.Length == 0) return tag;
            var end = tag.EndsWith("/>") ? tag.Length - 2 : tag.Length - 1;
            return tag[..end].TrimEnd() + add + (tag.EndsWith("/>") ? " />" : ">");
        });
    }
}
