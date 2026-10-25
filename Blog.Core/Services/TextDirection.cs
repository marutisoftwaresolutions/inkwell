namespace Blog.Core.Services;

/// <summary>
/// Reading direction for the public site, from its language (Settings → General → site language, BCP-47). A site in
/// Arabic, Hebrew, Persian, Urdu and the other right-to-left languages gets <c>dir="rtl"</c> on <c>&lt;html&gt;</c>; the
/// reader views and stylesheets use logical properties (start/end), so every layout and preset mirrors. The Desk
/// stays left-to-right. Pure (v1.0.9).
/// </summary>
public static class TextDirection
{
    public const string Ltr = "ltr";
    public const string Rtl = "rtl";

    /// <summary>Primary language subtags written right to left (CLDR character order).</summary>
    private static readonly HashSet<string> RtlLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "arc", "ckb", "dv", "fa", "he", "iw", "ks", "mzn", "nqo", "pnb", "ps", "sd", "syr", "ug", "ur", "yi",
    };

    /// <summary>Script subtags written right to left, for tags such as <c>pa-Arab</c> or <c>az-Arab</c>.</summary>
    private static readonly HashSet<string> RtlScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arab", "Hebr", "Thaa", "Syrc", "Nkoo", "Adlm", "Rohg",
    };

    /// <summary>"rtl" or "ltr". Unknown, empty or malformed tags are left to right, today's behaviour.</summary>
    public static string For(string? languageTag)
    {
        var tag = (languageTag ?? "").Trim().Replace('_', '-');
        if (tag.Length == 0) return Ltr;
        var parts = tag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Ltr;
        // An explicit script wins: "ku-Latn" is left to right, "ku-Arab" right to left.
        var script = parts.Skip(1).FirstOrDefault(p => p.Length == 4 && p.All(char.IsLetter));
        if (script is not null) return RtlScripts.Contains(script) ? Rtl : Ltr;
        return RtlLanguages.Contains(parts[0]) ? Rtl : Ltr;
    }

    public static bool IsRtl(string? languageTag) => For(languageTag) == Rtl;
}
