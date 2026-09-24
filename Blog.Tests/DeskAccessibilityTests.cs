using System.Text.RegularExpressions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Source-level guards for the 2026-09-19 Desk accessibility pass: icon-only controls carry a name,
/// error toasts are announced and never auto-dismiss, the active sidebar item is marked with
/// aria-current, hard-coded palette classes are replaced by the tone/pill tokens, and no view
/// carries a double-encoded em dash. Each assertion pins a defect that was live on that date.
/// </summary>
public class DeskAccessibilityTests
{
    /// <summary>The views this pass owns. Other screens belong to other workstreams and are scanned by UxTier2Tests.</summary>
    private static readonly string[] OwnedViews =
    {
        "Views/Shared/_AdminLayout.cshtml",
        "Views/Dashboard/Index.cshtml",
        "Views/Audit/Index.cshtml",
        "Views/Errors/Index.cshtml",
        "Views/Analytics/Index.cshtml",
        "Views/Security/Index.cshtml",
        "Views/AiCrawlers/Index.cshtml",
        "Views/Categories/Index.cshtml",
        "Views/Tags/Index.cshtml",
        "Views/Series/Index.cshtml",
        "Views/Series/Edit.cshtml",
        "Views/Pages/Index.cshtml",
        "Views/Redirects/Index.cshtml",
        "Views/Shared/_MediaSelectorModal.cshtml",
    };

    /// <summary>
    /// Views owned by another workstream that still carry a double-encoded em dash. Listed so the
    /// sweep stays green while naming the debt; remove the entry when the owner fixes the line.
    /// </summary>
    private static readonly string[] KnownMojibake =
    {
        "Views/Posts/Edit.cshtml", // line ~611: sample quote cite "â€” Abraham Lincoln" inside the component palette JS
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// A button or link whose only content is an svg or a Bootstrap-icon &lt;i&gt; has no accessible name unless
    /// it carries aria-label or visually-hidden text. title alone is not enough (screen readers do not
    /// reliably announce it), so unlike UxTier2Tests this check does not accept it.
    /// </summary>
    [Fact]
    public void Every_icon_only_button_or_link_in_the_owned_views_has_an_accessible_name()
    {
        var open = new Regex(@"<(button|a)\b[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline);
        var offenders = new List<string>();
        foreach (var rel in OwnedViews)
        {
            // Markup built inside <script> strings gets its names at runtime (Series/Edit sets them with
            // setAttribute below); only server-rendered markup is scanned here.
            var text = Regex.Replace(Web(rel), "<script.*?</script>", "", RegexOptions.Singleline);
            foreach (Match m in open.Matches(text))
            {
                var tag = m.Value;
                var after = text[(m.Index + m.Length)..];
                var close = after.IndexOf(m.Groups[1].Value == "a" ? "</a>" : "</button>", StringComparison.Ordinal);
                if (close < 0) continue;
                var inner = after[..close];
                // Icon-only: the content is an svg or an <i class="bi …"> and nothing else that reads as text.
                var trimmed = inner.Trim();
                var iconOnly = trimmed.StartsWith("<svg", StringComparison.Ordinal) || trimmed.StartsWith("<i class=\"bi", StringComparison.Ordinal);
                if (!iconOnly) continue;
                var visible = Regex.Replace(inner, "<svg.*?</svg>", "", RegexOptions.Singleline);
                visible = Regex.Replace(visible, "<i class=\"bi[^>]*>.*?</i>", "", RegexOptions.Singleline);
                visible = Regex.Replace(visible, "<[^>]+>", "").Trim();
                if (visible.Length > 0) continue;
                var named = tag.Contains("aria-label=") || tag.Contains("aria-labelledby=") || inner.Contains("sr-only");
                if (!named) offenders.Add(rel + ": " + Regex.Replace(tag, @"\s+", " ")[..Math.Min(100, tag.Length)]);
            }
        }
        Assert.True(offenders.Count == 0, "Icon-only control without aria-label / sr-only text:\n" + string.Join('\n', offenders));

        // The Series editor builds its move/remove buttons in JavaScript; each gets a name carrying the post title.
        var seriesEdit = Web("Views/Series/Edit.cshtml");
        foreach (var cls in new[] { ".move-up", ".move-down", ".remove-post" })
            Assert.Contains($"li.querySelector('{cls}').setAttribute('aria-label',", seriesEdit);
    }

    [Fact]
    public void Error_and_warning_toasts_are_alerts_that_never_auto_dismiss()
    {
        var layout = Web("Views/Shared/_AdminLayout.cshtml");

        Assert.Matches(new Regex("<div id=\"toast-error\"[^>]*role=\"alert\""), layout);
        Assert.Matches(new Regex("<div id=\"toast-warning\"[^>]*role=\"alert\""), layout);
        Assert.Matches(new Regex("<div[^>]*role=\"alert\"[^>]*class=\"toast-model-error"), layout);
        Assert.Matches(new Regex("<div id=\"toast-success\"[^>]*role=\"status\""), layout);

        // Every toast keeps a named close button.
        foreach (var id in new[] { "toast-success", "toast-error", "toast-warning" })
        {
            var start = layout.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, id);
            var block = layout[start..layout.IndexOf("</div>", start, StringComparison.Ordinal)];
            Assert.Contains("aria-label=\"Dismiss notification\"", block);
        }

        // The only timer in the layout removes the success toast and nothing else.
        var timers = Regex.Matches(layout, @"setTimeout\s*\(", RegexOptions.Singleline);
        Assert.True(timers.Count == 1, "expected exactly one auto-dismiss timer in the layout, found " + timers.Count);
        var timerStart = timers[0].Index;
        var timerBlock = layout[timerStart..layout.IndexOf("</script>", timerStart, StringComparison.Ordinal)];
        Assert.Contains("toast-success", timerBlock);
        Assert.DoesNotContain("toast-error", timerBlock);
        Assert.DoesNotContain("toast-warning", timerBlock);
        Assert.DoesNotContain("toast-model-error", timerBlock);
    }

    [Fact]
    public void Layout_scrolls_to_and_focuses_the_first_placed_validation_error()
    {
        var layout = Web("Views/Shared/_AdminLayout.cshtml");
        Assert.Contains(".field-validation-error, [data-valmsg-for]", layout);
        Assert.Contains("scrollIntoView({ block: 'center' })", layout);
        Assert.Contains("field.focus(", layout);
    }

    [Fact]
    public void Sidebar_marks_the_active_item_with_aria_current()
    {
        var layout = Web("Views/Shared/_AdminLayout.cshtml");
        Assert.Contains("aria-current=\"page\"", layout); // the helper's literal value
        Assert.Contains("string? NavCurrent(", layout);
        // Every NavClass link also carries NavCurrent, so no item is colour-only.
        var navClass = Regex.Matches(layout, "class=\"@NavClass\\(").Count;
        var navCurrent = Regex.Matches(layout, "aria-current=\"@NavCurrent\\(").Count;
        Assert.True(navClass > 0 && navClass == navCurrent, $"NavClass links: {navClass}, with aria-current: {navCurrent}");

        // In-page selectors that behave like tabs do the same.
        Assert.Contains("aria-current=\"@(active ? \"page\" : null)\"", Web("Views/Analytics/Index.cshtml"));
        Assert.Contains("aria-current=\"@(days == d ? \"page\" : null)\"", Web("Views/AiCrawlers/Index.cshtml"));
        Assert.Contains("aria-current=\"@(tab == \"rules\" ? \"page\" : null)\"", Web("Views/Redirects/Index.cshtml"));
    }

    [Fact]
    public void Audit_detail_rows_open_from_a_keyboard_reachable_button_and_filters_are_labelled()
    {
        var audit = Web("Views/Audit/Index.cshtml");
        Assert.Matches(new Regex("<button type=\"button\" aria-expanded=\"false\" aria-controls=\"@rowId\""), audit);
        Assert.Contains("<tr id=\"@rowId\" hidden", audit);
        Assert.DoesNotContain("@@click=\"$refs", audit); // the row-click toggle is gone
        foreach (var name in new[] { "entityType", "action", "userId", "from", "to" })
            Assert.Matches(new Regex($"<(select|input)[^>]*name=\"{name}\"[^>]*aria-label=\""), audit);

        foreach (var (view, names) in new (string, string[])[]
        {
            ("Views/Analytics/Index.cshtml", new[] { "from", "to" }),
            ("Views/Errors/Index.cshtml", new[] { "status", "q" }),
            ("Views/Security/Index.cshtml", new[] { "kind", "q", "hours" }),
        })
        {
            var html = Web(view);
            foreach (var name in names)
                Assert.True(Regex.IsMatch(html, $"<(select|input)[^>]*name=\"{name}\"[^>]*aria-label=\""), $"{view}: {name} has no aria-label");
        }
    }

    [Fact]
    public void Tone_and_pill_utilities_exist_in_admin_css_with_dark_scheme_values()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "Blog.Web", "wwwroot", "css", "admin.css"));
        foreach (var cls in new[] { ".pill-ok", ".pill-warn", ".pill-bad", ".pill-info", ".tone-ok", ".tone-warn", ".tone-bad", ".toast-ok", ".toast-warn", ".toast-bad" })
            Assert.Contains(cls + " ", css);
        Assert.Contains("--success:", css);
        Assert.Contains("--warning:", css);
        // Both dark entry points redefine the new tokens.
        Assert.Matches(new Regex("@media \\(prefers-color-scheme: dark\\)\\s*\\{\\s*html:not\\(\\[data-theme=\"light\"\\]\\)\\s*\\{\\s*--success:"), css);
        Assert.Matches(new Regex("html\\[data-theme=\"dark\"\\]\\s*\\{\\s*--success:"), css);
        // Pills use tokens, never a literal colour.
        var pillBlock = css[css.IndexOf(".pill-ok", StringComparison.Ordinal)..];
        pillBlock = pillBlock[..pillBlock.IndexOf(".desk-toast", StringComparison.Ordinal)];
        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b"), pillBlock);
    }

    /// <summary>
    /// Tailwind palette classes (bg-green-100, text-amber-600, bg-white …) ignore the dark scheme and the
    /// brand palette. The owned views use the tone/pill utilities or the shadcn tokens instead. Chart
    /// palettes in Analytics JS arrays, the print stylesheet and the translucent black scrims behind
    /// overlays (bg-black/50 is a shade, not a colour, and reads the same in both schemes) are excluded.
    /// </summary>
    [Fact]
    public void Owned_views_use_tokens_instead_of_palette_classes()
    {
        var palette = new Regex(@"(?<![\w-])(?:bg|text|border|hover:bg|hover:text)-(?:white|black|red|green|yellow|amber|orange|emerald|blue|indigo|slate|gray|purple|pink|teal)(?:-\d{2,3})?(?![\w/-])", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var rel in OwnedViews)
        {
            var text = Web(rel);
            // Strip <script> and <style> blocks: chart colours and the print sheet are not UI classes.
            text = Regex.Replace(text, "<script.*?</script>", "", RegexOptions.Singleline);
            text = Regex.Replace(text, "<style.*?</style>", "", RegexOptions.Singleline);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in palette.Matches(lines[i]))
                    offenders.Add($"{rel}:{i + 1}: {m.Value}");
        }
        Assert.True(offenders.Count == 0, "Palette class where a token is required:\n" + string.Join('\n', offenders));
        Assert.DoesNotContain("bg-white", Web("Views/Dashboard/Index.cshtml"));
    }

    [Fact]
    public void Dashboard_stat_numbers_are_not_headings_and_the_view_link_names_the_post()
    {
        var dash = Web("Views/Dashboard/Index.cshtml");
        Assert.DoesNotMatch(new Regex("<h3 class=\"text-2xl font-bold\">@ViewBag"), dash);
        Assert.Contains("<p class=\"text-2xl font-bold\">@ViewBag.TotalPosts</p>", dash);
        Assert.Contains("aria-label=\"View @post.Title\"", dash);
    }

    [Fact]
    public void AiCrawlers_chart_is_described_and_the_search_crawler_table_has_a_header()
    {
        var view = Web("Views/AiCrawlers/Index.cshtml");
        Assert.Matches(new Regex("<div class=\"flex items-end gap-1 h-24\" role=\"img\" aria-label=\"AI crawler visits per day:"), view);
        Assert.Equal(3, Regex.Matches(view, "<thead>").Count);
        Assert.DoesNotContain("🎉", Web("Views/Errors/Index.cshtml"));
    }

    /// <summary>
    /// "â€”" (or the fully double-encoded "â") is an em dash that was saved through the wrong
    /// code page. The sqlcmd rule guards the database; this guards the views.
    /// </summary>
    [Fact]
    public void No_view_carries_a_double_encoded_em_dash()
    {
        var root = Path.Combine(Root(), "Blog.Web", "Views");
        var mojibake = new Regex("â[€]", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Path.Combine(Root(), "Blog.Web"), path).Replace('\\', '/');
            if (KnownMojibake.Contains(rel)) continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                if (mojibake.IsMatch(lines[i])) offenders.Add($"{rel}:{i + 1}");
        }
        Assert.True(offenders.Count == 0, "Double-encoded characters in views:\n" + string.Join('\n', offenders));
        foreach (var rel in OwnedViews)
            Assert.DoesNotMatch(mojibake, Web(rel));
    }
}
