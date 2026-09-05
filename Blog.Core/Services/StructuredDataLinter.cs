using System.Text.Json;
using Blog.Core.Domain;

namespace Blog.Core.Services;

/// <summary>
/// Validates the structured data a post will emit, before it ships.
///
/// Two problems this exists to stop. First, a malformed block fails *silently*: the renderer
/// swallows the parse error and emits nothing, so a broken FAQ looks exactly like a missing one and
/// nobody notices until the page stops being quoted. Second, the SEO/AEO Rule bans self-serving and
/// unsourced ratings — a rule that only holds if something enforces it, so the first-party check
/// here is deliberately an error, not a warning.
///
/// Pure and dependency-free, so every rule is unit-testable and there is one place to change a
/// threshold.
/// </summary>
public static class StructuredDataLinter
{
    /// <summary>Answers shorter than this are too thin for an answer engine to quote.</summary>
    private const int MinAnswerLength = 20;

    /// <summary>Roundup scores are out of 10; anything else breaks the rendered scale and the schema.</summary>
    private const double MaxScore = 10;

    /// <summary>Below this the capsule is not a real answer; above it, it stops being quotable.</summary>
    private const int MinCapsuleWords = 20;
    private const int MaxCapsuleWords = 80;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Every problem in the post's structured data, errors first.</summary>
    public static IReadOnlyList<LintFinding> Lint(Post post)
    {
        var findings = new List<LintFinding>();
        if (post is null) return findings;

        LintAnswerCapsule(post, findings);
        LintFaq(post.FaqJson, findings);
        LintKeyFacts(post.KeyFactsJson, findings);
        LintHowTo(post.HowToJson, findings);
        LintRoundup(post.RoundupJson, findings);

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Block, StringComparer.Ordinal)
            .ThenBy(f => f.Item ?? 0)
            .ToList();
    }

    /// <summary>True when nothing blocks publishing. Warnings are allowed through.</summary>
    public static bool CanPublish(IEnumerable<LintFinding> findings) =>
        !findings.Any(f => f.Severity == LintSeverity.Error);

    // ── Answer capsule → schema.org abstract ──────────────────────────────────

    private static void LintAnswerCapsule(Post post, List<LintFinding> findings)
    {
        if (!post.HasAnswerCapsule) return;

        var words = post.AnswerCapsuleWordCount;
        if (words < MinCapsuleWords)
            findings.Add(new(LintSeverity.Warning, "Answer capsule",
                $"Only {words} words. Too short to answer the question the reader arrived with."));
        else if (words > MaxCapsuleWords)
            findings.Add(new(LintSeverity.Warning, "Answer capsule",
                $"{words} words. Long enough that an answer engine is unlikely to quote it whole - aim for 40-60."));
    }

    // ── FAQ → FAQPage / Question / acceptedAnswer ─────────────────────────────

    private static void LintFaq(string? json, List<LintFinding> findings)
    {
        if (!TryParse<List<FaqItem>>(json, "FAQ", findings, out var items) || items is null) return;

        for (var i = 0; i < items.Count; i++)
        {
            var n = i + 1;
            var q = items[i].Question;
            var a = items[i].Answer;

            if (string.IsNullOrWhiteSpace(q))
                findings.Add(new(LintSeverity.Error, "FAQ", "The question is empty. schema.org requires a name on every Question.", n));
            else if (!q.TrimEnd().EndsWith('?'))
                findings.Add(new(LintSeverity.Warning, "FAQ", "The question does not end in a question mark, so it may not be read as one.", n));

            if (string.IsNullOrWhiteSpace(a))
                findings.Add(new(LintSeverity.Error, "FAQ", "The answer is empty. A Question with no acceptedAnswer is invalid.", n));
            else if (a.Trim().Length < MinAnswerLength)
                findings.Add(new(LintSeverity.Warning, "FAQ", "The answer is very short, and too thin for a search or AI engine to quote.", n));
        }

        foreach (var d in Duplicates(items.Select(i => i.Question)))
            findings.Add(new(LintSeverity.Warning, "FAQ", $"The question \"{Shorten(d)}\" appears more than once."));
    }

    // ── Key Facts ─────────────────────────────────────────────────────────────

    private static void LintKeyFacts(string? json, List<LintFinding> findings)
    {
        if (!TryParse<List<KeyFact>>(json, "Key Facts", findings, out var items) || items is null) return;

        for (var i = 0; i < items.Count; i++)
        {
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(items[i].Label))
                findings.Add(new(LintSeverity.Error, "Key Facts", "The label is empty.", n));
            if (string.IsNullOrWhiteSpace(items[i].Value))
                findings.Add(new(LintSeverity.Error, "Key Facts", "The value is empty, and renders as a blank row.", n));
        }

        foreach (var d in Duplicates(items.Select(i => i.Label)))
            findings.Add(new(LintSeverity.Warning, "Key Facts", $"The label \"{Shorten(d)}\" appears more than once."));
    }

    // ── How-To → HowTo / HowToStep ────────────────────────────────────────────

    private static void LintHowTo(string? json, List<LintFinding> findings)
    {
        if (!TryParse<List<HowToStep>>(json, "How-To", findings, out var steps) || steps is null) return;

        for (var i = 0; i < steps.Count; i++)
        {
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(steps[i].Name))
                findings.Add(new(LintSeverity.Error, "How-To", "The step has no name.", n));
            if (string.IsNullOrWhiteSpace(steps[i].Text))
                findings.Add(new(LintSeverity.Error, "How-To", "The step has no instruction text.", n));
        }

        if (steps.Count == 1)
            findings.Add(new(LintSeverity.Warning, "How-To", "A How-To with a single step is unlikely to be treated as a procedure."));
    }

    // ── Roundup → ItemList / Review / AggregateRating ─────────────────────────

    private static void LintRoundup(string? json, List<LintFinding> findings)
    {
        if (!TryParse<RoundupData>(json, "Roundup", findings, out var data) || data is null) return;

        for (var i = 0; i < data.Entries.Count; i++)
        {
            var e = data.Entries[i];
            var n = i + 1;
            var name = Shorten(e.Name);

            if (string.IsNullOrWhiteSpace(e.Name))
                findings.Add(new(LintSeverity.Error, "Roundup", "The entry has no name.", n));

            // Content External-Link Rule: every entry links its real, verified site.
            if (!e.HasWebsite)
                findings.Add(new(LintSeverity.Error, "Roundup", $"\"{name}\" has no website. Every entry must link the vendor's real site.", n));
            else if (!IsAbsoluteHttpUrl(e.Website!))
                findings.Add(new(LintSeverity.Error, "Roundup", $"\"{name}\" has a website that is not an absolute http(s) URL.", n));

            // ctaUrl is the internal review path; the vendor's own site belongs in website.
            if (e.HasCta && IsAbsoluteHttpUrl(e.CtaUrl!))
                findings.Add(new(LintSeverity.Error, "Roundup", $"\"{name}\" has an external ctaUrl. That field is the internal review path; the vendor site goes in website.", n));

            if (e.Score < 0 || e.Score > MaxScore)
                findings.Add(new(LintSeverity.Error, "Roundup", $"\"{name}\" is scored {e.Score}, outside the 0-10 scale.", n));

            // A self-authored score on our own product is the self-serving rating the SEO/AEO Rule
            // bans and Google's policy prohibits.
            if (e.IsFirstParty && e.Score > 0)
                findings.Add(new(LintSeverity.Error, "Roundup",
                    $"\"{name}\" is our own product and carries a self-authored score. Remove the score, or cite a real third-party source.", n));
        }

        foreach (var g in data.Entries.Where(e => e.Rank > 0).GroupBy(e => e.Rank).Where(g => g.Count() > 1))
            findings.Add(new(LintSeverity.Warning, "Roundup", $"Rank {g.Key} is used by more than one entry."));

        if (data.Entries.Count > 0 && data.Entries.All(e => e.Score <= 0))
            findings.Add(new(LintSeverity.Warning, "Roundup", "No entry is scored, so no ranking or AggregateRating can be emitted."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a block, reporting malformed JSON as an error instead of letting it fail silently the
    /// way the renderer does. An absent block is not a problem — every block is optional.
    /// </summary>
    private static bool TryParse<T>(string? json, string block, List<LintFinding> findings, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            value = JsonSerializer.Deserialize<T>(json, _json);
            return value is not null;
        }
        catch (JsonException ex)
        {
            findings.Add(new(LintSeverity.Error, block,
                $"The block is not valid JSON, so it will render as empty. {ex.Message}"));
            return false;
        }
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
              .Where(g => g.Count() > 1)
              .Select(g => g.Key);

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Shorten(string? s, int max = 40) =>
        string.IsNullOrWhiteSpace(s) ? "(unnamed)" : (s.Length > max ? s[..max] + "…" : s);
}
