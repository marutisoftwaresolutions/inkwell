using System.Net;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// What a bookmark card shows for a link: the linked page's title, description and site name, read
/// from that page's own HTML. Everything here treats the fetched page as untrusted text: values are
/// decoded once, stripped of markup and control characters, cut to length, and encoded again when the
/// card is built, so nothing a remote page says can become markup or script on this site. No image
/// is taken from the remote page: the site serves only its own files. Pure.
/// </summary>
public static partial class LinkPreview
{
    public const int MaxTitleLength = 150;
    public const int MaxDescriptionLength = 300;
    public const int MaxSiteLength = 80;

    /// <summary>How much of a page is searched: the details live in the head, and a hostile page must not be able to buy unbounded work.</summary>
    public const int MaxSearchedChars = 64_000;
    /// <summary>Each pattern gives up after this long on text built to make it slow; the page then simply has no details.</summary>
    public const int MatchTimeoutMs = 250;

    public sealed record Details(string Url, string Title, string Description, string Site);

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"\b([a-zA-Z:_-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Whitespace();

    /// <summary>The link a person typed, as an absolute http(s) address without credentials; null when it is not one.</summary>
    public static Uri? CleanUrl(string? url)
    {
        var text = (url ?? "").Trim();
        if (text.Length == 0 || text.Length > 2000) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        return string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    }

    /// <summary>
    /// Reads the details from a page's HTML. Open Graph first, then the Twitter card, then the plain
    /// title and description. A page that says nothing still gives a card: the title falls back to
    /// the address's host.
    /// </summary>
    public static Details Parse(string? html, Uri pageUrl)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var head = html ?? "";
        var endOfHead = head.IndexOf("</head", StringComparison.OrdinalIgnoreCase);
        if (endOfHead >= 0) head = head[..endOfHead];
        if (head.Length > MaxSearchedChars) head = head[..MaxSearchedChars];
        var host = pageUrl.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? pageUrl.Host[4..] : pageUrl.Host;

        try
        {
        foreach (Match tag in MetaTag().Matches(head))
        {
            string? key = null, content = null;
            foreach (Match a in Attribute().Matches(tag.Value))
            {
                var name = a.Groups[1].Value;
                var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
                if (name.Equals("property", StringComparison.OrdinalIgnoreCase) || name.Equals("name", StringComparison.OrdinalIgnoreCase)) key ??= value;
                else if (name.Equals("content", StringComparison.OrdinalIgnoreCase)) content ??= value;
            }
            if (!string.IsNullOrWhiteSpace(key) && content is not null) meta.TryAdd(key.Trim(), content);
        }

        string? First(params string[] keys) => keys.Select(k => meta.TryGetValue(k, out var v) ? Clean(v, int.MaxValue) : "").FirstOrDefault(v => v.Length > 0);

        var titleTag = TitleTag().Match(head);
        var title = First("og:title", "twitter:title") ?? (titleTag.Success ? Clean(titleTag.Groups[1].Value, int.MaxValue) : "");
        if (title.Length == 0) title = host;

        return new Details(
            pageUrl.AbsoluteUri,
            Cut(title, MaxTitleLength),
            Cut(First("og:description", "twitter:description", "description") ?? "", MaxDescriptionLength),
            Cut(First("og:site_name") ?? host, MaxSiteLength));
        }
        catch (RegexMatchTimeoutException)
        {
            // Text built to stall the patterns: the link still gets a card, named by its host.
            return new Details(pageUrl.AbsoluteUri, host, "", host);
        }
    }

    /// <summary>Text from a remote page made plain: entities decoded once, tags and control characters removed, one line.</summary>
    public static string Clean(string? value, int maxLength)
    {
        var text = WebUtility.HtmlDecode(value ?? "");
        text = AnyTag().Replace(text, " ");
        text = new string(text.Where(c => !char.IsControl(c) || c is ' ' or '\n' or '\t').ToArray());
        text = Whitespace().Replace(text, " ").Trim();
        return Cut(text, maxLength);
    }

    private static string Cut(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>
    /// The card as stored in a post: one link holding the title, the description and the site. Every
    /// value is encoded here, whatever it was before. An outside link, so it carries
    /// <c>rel="nofollow noopener"</c> like every other editorial reference.
    /// </summary>
    public static string CardHtml(Details d)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var url = CleanUrl(d.Url)?.AbsoluteUri ?? "#";
        var description = d.Description.Length > 0 ? $"<span class=\"comp-bookmark-text\">{E(d.Description)}</span>" : "";
        return $"<a class=\"comp-bookmark\" href=\"{E(url)}\" rel=\"nofollow noopener\">" +
               $"<span class=\"comp-bookmark-title\">{E(d.Title)}</span>{description}" +
               $"<span class=\"comp-bookmark-site\">{E(d.Site)}</span></a>";
    }
}
