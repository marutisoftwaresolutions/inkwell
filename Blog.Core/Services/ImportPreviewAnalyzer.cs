using System.Text.RegularExpressions;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>What would happen to one staged item if the import ran.</summary>
public enum ImportOutcome
{
    /// <summary>Would be created as new content.</summary>
    Create,
    /// <summary>A slug clash would produce a suffixed slug (-2, -3, …).</summary>
    CreateWithNewSlug,
    /// <summary>A slug clash would replace the existing post or page.</summary>
    Overwrite,
    /// <summary>Excluded — by a slug clash under the Skip policy, or by the selected options.</summary>
    Skip
}

/// <summary>One predicted outcome, with the reason shown to the operator.</summary>
public record ImportPrediction(
    ImportItemType Type,
    string Title,
    string? Slug,
    ImportOutcome Outcome,
    string Reason);

/// <summary>An internal link in imported content that would not resolve after the import.</summary>
public record ImportBrokenLink(string InPostTitle, string Href, string Reason);

/// <summary>The full preview shown before anything is written.</summary>
public record ImportPreview(
    IReadOnlyList<ImportPrediction> Predictions,
    IReadOnlyList<ImportBrokenLink> BrokenLinks,
    int ImagesToFetch)
{
    public int Creates    => Predictions.Count(p => p.Outcome is ImportOutcome.Create or ImportOutcome.CreateWithNewSlug);
    public int Overwrites => Predictions.Count(p => p.Outcome == ImportOutcome.Overwrite);
    public int Skips      => Predictions.Count(p => p.Outcome == ImportOutcome.Skip);
    public int Renames    => Predictions.Count(p => p.Outcome == ImportOutcome.CreateWithNewSlug);

    /// <summary>Anything the operator should look at before committing.</summary>
    public bool HasWarnings => Overwrites > 0 || BrokenLinks.Count > 0;

    public IEnumerable<IGrouping<ImportItemType, ImportPrediction>> ByType =>
        Predictions.GroupBy(p => p.Type).OrderBy(g => g.Key);
}

/// <summary>
/// Predicts the result of an import without performing it.
///
/// Deliberately contains no write path at all. A dry run implemented as a flag threaded through the
/// real importer is one missed branch away from writing during a "preview", so the safe shape is a
/// separate read-only pass: it cannot mutate anything because it has nothing to mutate with.
///
/// It predicts rather than simulates, and says so — image downloads are counted, not attempted, and
/// slug arithmetic follows the same conflict policy the importer applies.
/// </summary>
public static class ImportPreviewAnalyzer
{
    private static readonly Regex _internalLink = new(
        @"href=""(?<href>/[^""#][^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _imageTag = new(
        @"<img\s[^>]*src=""(?<src>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// </summary>
    /// <param name="items">Staged content: type, title, slug, body, and published state.</param>
    /// <param name="options">The options the operator has chosen.</param>
    /// <param name="existingSlugs">Slugs already in this blog, so clashes can be predicted.</param>
    public static ImportPreview Analyze(
        IReadOnlyList<StagedItem> items,
        ImportOptions options,
        ISet<string> existingSlugs)
    {
        var predictions = new List<ImportPrediction>();
        var brokenLinks = new List<ImportBrokenLink>();
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Slugs that will exist afterwards: what is already here, plus what this import adds.
        var taken = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);
        var willExist = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var (outcome, reason, finalSlug) = Predict(item, options, taken);
            predictions.Add(new(item.Type, item.Title, finalSlug, outcome, reason));

            if (outcome != ImportOutcome.Skip && !string.IsNullOrWhiteSpace(finalSlug))
            {
                taken.Add(finalSlug);
                willExist.Add(finalSlug);
            }

            if (outcome == ImportOutcome.Skip || string.IsNullOrEmpty(item.Html)) continue;

            if (options.ImportImages)
                foreach (Match m in _imageTag.Matches(item.Html))
                    images.Add(m.Groups["src"].Value);
        }

        // Second pass: a link can point at something imported later in the file, so links are only
        // judged once every slug that will exist is known.
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Html)) continue;

            foreach (Match m in _internalLink.Matches(item.Html))
            {
                var href = m.Groups["href"].Value;
                var slug = NormalizeSlug(href);
                if (slug is null) continue;
                if (willExist.Contains(slug)) continue;

                brokenLinks.Add(new(item.Title, href,
                    "Nothing with this slug exists here, and the import does not create it."));
            }
        }

        return new ImportPreview(predictions, Dedupe(brokenLinks), images.Count);
    }

    private static (ImportOutcome, string, string?) Predict(
        StagedItem item, ImportOptions options, ISet<string> taken)
    {
        if (!IsSelected(item, options, out var why))
            return (ImportOutcome.Skip, why, item.Slug);

        var slug = item.Slug;
        if (string.IsNullOrWhiteSpace(slug))
            return (ImportOutcome.Create, "Will be created.", slug);

        if (!taken.Contains(slug))
            return (ImportOutcome.Create, "Will be created.", slug);

        return options.SlugConflict switch
        {
            "Skip" => (ImportOutcome.Skip, $"A post already uses /{slug}, and the conflict policy is Skip.", slug),
            "Overwrite" => (ImportOutcome.Overwrite, $"Will replace the existing /{slug}.", slug),
            _ => (ImportOutcome.CreateWithNewSlug,
                  $"/{slug} is taken, so it will be created as /{NextFreeSlug(slug, taken)}.",
                  NextFreeSlug(slug, taken))
        };
    }

    private static bool IsSelected(StagedItem item, ImportOptions o, out string why)
    {
        why = "";
        var included = item.Type switch
        {
            ImportItemType.Post => o.ImportPosts,
            ImportItemType.Page => o.ImportPages,
            ImportItemType.Category => o.ImportCategories,
            ImportItemType.Tag => o.ImportTags,
            ImportItemType.Author => o.ImportAuthors,
            ImportItemType.Comment => o.ImportComments,
            ImportItemType.Image => o.ImportImages,
            _ => true
        };

        if (!included)
        {
            why = $"{item.Type} is not selected for import.";
            return false;
        }

        if (o.PublishedOnly && !item.IsPublished)
        {
            why = "Not published at the source, and only published content is selected.";
            return false;
        }

        return true;
    }

    /// <summary>Mirrors the importer's suffixing: slug-2, slug-3, … until one is free.</summary>
    private static string NextFreeSlug(string slug, ISet<string> taken)
    {
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{slug}-{n}";
            if (!taken.Contains(candidate)) return candidate;
        }
        return $"{slug}-{Guid.NewGuid():N}";
    }

    /// <summary>A single-segment internal path, or null when it is not a content link.</summary>
    private static string? NormalizeSlug(string href)
    {
        var path = href.Split('?')[0].Split('#')[0].Trim('/');
        if (path.Length == 0) return null;
        if (path.Contains('/')) return null;                 // generated route, not a post slug
        if (Path.HasExtension(path)) return null;            // a file
        return path;
    }

    private static IReadOnlyList<ImportBrokenLink> Dedupe(IEnumerable<ImportBrokenLink> links) =>
        links.GroupBy(l => (l.InPostTitle, l.Href))
             .Select(g => g.First())
             .OrderBy(l => l.InPostTitle, StringComparer.OrdinalIgnoreCase)
             .ToList();
}

/// <summary>Content staged by the parser, reduced to what the preview needs.</summary>
public record StagedItem(
    ImportItemType Type,
    string Title,
    string? Slug,
    string? Html,
    bool IsPublished);
