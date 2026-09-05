using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The triage decides what an operator sees. Get it wrong in one direction and the worklist fills
/// with scanner noise nobody sorts through; wrong in the other and a real broken link is buried.
/// </summary>
public class NotFoundTriageTests
{
    private static readonly string[] Slugs =
    [
        "best-optical-software-2026",
        "revolution-ehr-review",
        "compulink-optometry-review",
        "optical-inventory-management-guide"
    ];

    private static NotFoundGroup Group(string path, int hits = 1, string? referer = null) =>
        new() { Path = path, Hits = hits, LastSeenAt = DateTime.UtcNow, Referer = referer };

    // ── Near-miss detection ───────────────────────────────────────────────────

    [Theory]
    [InlineData("/best-optical-software-2025", "best-optical-software-2026")]  // last year's slug
    [InlineData("/revolution-ehr-reveiw", "revolution-ehr-review")]            // typo
    [InlineData("/revolution-ehr", "revolution-ehr-review")]                   // truncated
    public void A_slug_close_to_a_real_one_is_flagged_as_a_near_miss(string requested, string expected)
    {
        var result = NotFoundTriage.Triage([Group(requested)], Slugs);

        var candidate = Assert.Single(result);
        Assert.Equal(NotFoundKind.NearMiss, candidate.Kind);
        Assert.Equal(expected, candidate.SuggestedSlug);
        Assert.True(candidate.Similarity >= NotFoundTriage.MinSimilarity);
    }

    [Fact]
    public void An_unrelated_slug_is_not_matched_to_the_nearest_post()
    {
        // Suggesting a redirect to an unrelated article is worse than suggesting nothing: it sends
        // the reader somewhere they did not ask for and tells Google the two pages are the same.
        var result = NotFoundTriage.Triage([Group("/how-to-choose-a-frame-supplier")], Slugs);

        Assert.Equal(NotFoundKind.Unmatched, Assert.Single(result).Kind);
        Assert.Null(result[0].SuggestedSlug);
    }

    // ── Scanner noise ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/wp-login.php")]
    [InlineData("/wp-content/plugins/revslider/temp/update_extract/revslider.php")]
    [InlineData("/.env")]
    [InlineData("/vendor/phpunit/phpunit/src/Util/PHP/eval-stdin.php")]
    [InlineData("/admin/config.php")]
    public void Scanner_probes_never_reach_the_worklist(string path)
    {
        // No attack-signature list is consulted — a probe simply never resembles a post URL. That
        // keeps the one copy of those patterns in the firewall, where blocking is decided.
        var result = NotFoundTriage.Triage([Group(path, hits: 500)], Slugs);

        Assert.Equal(NotFoundKind.Unmatched, Assert.Single(result).Kind);
    }

    [Fact]
    public void Volume_alone_does_not_promote_a_probe()
    {
        // Scanners generate far more hits than readers; ranking by hits would bury every real fault.
        var result = NotFoundTriage.Triage(
            [Group("/wp-login.php", hits: 9999), Group("/revolution-ehr-reveiw", hits: 2)],
            Slugs);

        Assert.Equal("/revolution-ehr-reveiw", result[0].Path);
    }

    // ── Referrers ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_404_with_a_referrer_is_actionable_even_with_no_matching_slug()
    {
        var result = NotFoundTriage.Triage(
            [Group("/some-old-campaign-page", referer: "https://example.com/blog")], Slugs);

        Assert.Equal(NotFoundKind.Referred, Assert.Single(result).Kind);
    }

    [Fact]
    public void A_near_miss_outranks_a_referred_404()
    {
        var result = NotFoundTriage.Triage(
            [Group("/x-page", referer: "https://example.com"), Group("/revolution-ehr-reveiw")],
            Slugs);

        Assert.Equal(NotFoundKind.NearMiss, result[0].Kind);
        Assert.Equal(NotFoundKind.Referred, result[1].Kind);
    }

    [Fact]
    public void An_empty_referrer_is_treated_as_no_referrer() =>
        Assert.Equal(NotFoundKind.Unmatched,
            Assert.Single(NotFoundTriage.Triage([Group("/nothing-like-a-post", referer: "  ")], Slugs)).Kind);

    [Fact]
    public void No_published_posts_does_not_throw() =>
        Assert.Equal(NotFoundKind.Unmatched,
            Assert.Single(NotFoundTriage.Triage([Group("/anything")], [])).Kind);

    // ── Rule validation ───────────────────────────────────────────────────────

    private static readonly RedirectRule[] Existing =
    [
        new() { From = "/old-a", To = "/middle",  StatusCode = RedirectStatus.MovedPermanently },
        new() { From = "/middle", To = "/final",  StatusCode = RedirectStatus.MovedPermanently },
        new() { From = "/retired", To = "",       StatusCode = RedirectStatus.Gone }
    ];

    [Fact]
    public void A_valid_rule_passes() =>
        Assert.Null(NotFoundTriage.Validate("/old-slug", "/new-slug", 301, Existing));

    [Fact]
    public void A_410_needs_no_destination() =>
        Assert.Null(NotFoundTriage.Validate("/gone-for-good", null, 410, Existing));

    [Fact]
    public void A_url_cannot_redirect_to_itself() =>
        Assert.Contains("itself", NotFoundTriage.Validate("/same", "/same", 301, Existing));

    [Fact]
    public void A_loop_is_refused()
    {
        // /final → /old-a would close the ring /old-a → /middle → /final → /old-a.
        var error = NotFoundTriage.Validate("/final", "/old-a", 301, Existing);

        Assert.NotNull(error);
    }

    [Fact]
    public void Pointing_at_a_url_that_itself_redirects_is_refused_with_the_real_destination()
    {
        // Allowing this would manufacture the chains Link Audit exists to report.
        var error = NotFoundTriage.Validate("/new-one", "/old-a", 301, Existing);

        Assert.Contains("/final", error);
    }

    [Fact]
    public void Pointing_at_a_retired_url_is_refused() =>
        Assert.Contains("410", NotFoundTriage.Validate("/new-one", "/retired", 301, Existing));

    [Theory]
    [InlineData("", "/to", 301)]
    [InlineData("old-slug", "/to", 301)]                      // no leading slash
    [InlineData("/", "/to", 301)]                             // the home page
    [InlineData("https://evil.test/x", "/to", 301)]           // not a path on this site
    [InlineData("/from", "", 301)]                            // 301 with no destination
    [InlineData("/from", "not-a-path", 301)]                  // destination is neither path nor URL
    [InlineData("/from", "/to", 307)]                         // unsupported status
    public void Malformed_rules_are_refused(string from, string to, int status) =>
        Assert.NotNull(NotFoundTriage.Validate(from, to, status, Existing));

    [Fact]
    public void An_external_destination_is_allowed() =>
        Assert.Null(NotFoundTriage.Validate("/moved-away", "https://opto-soft.com/features", 301, Existing));

    [Fact]
    public void Editing_an_existing_rule_does_not_conflict_with_itself() =>
        Assert.Null(NotFoundTriage.Validate("/old-a", "/final", 301, Existing));
}
