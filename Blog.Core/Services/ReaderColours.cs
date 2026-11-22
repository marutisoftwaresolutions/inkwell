using System.Text;
using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// Maps the colours saved under Theme → Colours onto the tokens the reader surface actually reads.
/// The colour pickers store the Desk-style names (background, foreground, primary, card, border,
/// muted-foreground); every public layout paints from the Inkwell names (--bg, --fg, --link …), so
/// without this mapping a saved colour changed nothing a reader could see. It is opt-in per site
/// (<see cref="SettingKey"/>), because existing sites carry saved colours that never showed and must
/// not be recoloured by an upgrade. Pure; values that are not plain hex colours are dropped, so a
/// stored value can never inject CSS.
/// </summary>
public static partial class ReaderColours
{
    /// <summary>Theme setting that switches the mapping on: "true" or "false" (default).</summary>
    public const string SettingKey = "colors-on-site";

    private static readonly (string Source, string[] Tokens)[] Mapping =
    [
        ("background",       ["--bg"]),
        ("card",             ["--surface"]),
        ("muted",            ["--surface-2"]),
        ("border",           ["--border"]),
        ("foreground",       ["--fg", "--fg-body", "--rule"]),
        ("muted-foreground", ["--fg-muted", "--fg-meta"]),
        ("primary",          ["--link"]),
        ("accent-foreground", ["--link-hover"]),
    ];

    public static bool Enabled(IReadOnlyDictionary<string, string> theme) =>
        theme.TryGetValue(SettingKey, out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Token/value pairs for every saved colour that is a valid hex colour.</summary>
    public static IReadOnlyList<(string Token, string Value)> Map(IReadOnlyDictionary<string, string> theme)
    {
        var result = new List<(string, string)>();
        foreach (var (source, tokens) in Mapping)
        {
            if (!theme.TryGetValue(source, out var value) || !IsHex(value)) continue;
            foreach (var token in tokens) result.Add((token, value.Trim()));
        }
        return result;
    }

    /// <summary>
    /// The style rules for the public layout, or an empty string when the site has not opted in or no
    /// colour is usable. Light scheme only: a forced or preferred dark scheme keeps the dark palette,
    /// since one saved colour set cannot be right for both.
    /// </summary>
    public static string Css(IReadOnlyDictionary<string, string> theme)
    {
        if (!Enabled(theme)) return "";
        var pairs = Map(theme);
        if (pairs.Count == 0) return "";

        var declarations = new StringBuilder();
        foreach (var (token, value) in pairs) declarations.Append(token).Append(':').Append(value).Append(';');

        return "html[data-theme=\"light\"] body.inkwell-root{" + declarations + "}" +
               "@media (prefers-color-scheme: light){html:not([data-theme=\"dark\"]) body.inkwell-root{" + declarations + "}}" +
               "@media (prefers-color-scheme: no-preference){html:not([data-theme=\"dark\"]) body.inkwell-root{" + declarations + "}}";
    }

    public static bool IsHex(string? value) => value is not null && Hex().IsMatch(value.Trim());

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex Hex();
}
