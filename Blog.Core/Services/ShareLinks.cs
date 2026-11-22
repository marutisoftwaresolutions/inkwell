namespace Blog.Core.Services;

/// <summary>
/// The links under a post that pass it on: by email and to a few networks. Each is an ordinary link
/// to that service's own "share" page with the post's address in it. Nothing is loaded from any of
/// them, no script of theirs runs on the site, and nothing is known about the reader until the
/// reader chooses to follow one. Pure.
/// </summary>
public static class ShareLinks
{
    public sealed record Link(string Key, string Label, string Href);

    /// <summary>The links for a post, in the order they are shown. Empty when the address is not an absolute web address.</summary>
    public static IReadOnlyList<Link> For(string? url, string? title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return [];
        var address = Uri.EscapeDataString(uri.AbsoluteUri);
        var name = string.Join(' ', (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var text = Uri.EscapeDataString(name);
        var both = Uri.EscapeDataString(name.Length > 0 ? name + " " + uri.AbsoluteUri : uri.AbsoluteUri);

        return
        [
            new("email", "Email", $"mailto:?subject={text}&body={both}"),
            new("linkedin", "LinkedIn", $"https://www.linkedin.com/sharing/share-offsite/?url={address}"),
            new("bluesky", "Bluesky", $"https://bsky.app/intent/compose?text={both}"),
            new("x", "X", $"https://x.com/intent/post?url={address}&text={text}"),
            new("facebook", "Facebook", $"https://www.facebook.com/sharer/sharer.php?u={address}"),
            new("whatsapp", "WhatsApp", $"https://wa.me/?text={both}"),
        ];
    }
}
