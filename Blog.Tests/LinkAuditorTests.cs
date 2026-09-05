using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Classification rules for the internal-link audit. False positives here would send an author
/// chasing links that are fine, so the "leave it alone" cases are pinned as carefully as the faults.
/// </summary>
public class LinkAuditorTests
{
    private static ISet<string> Published(params string[] slugs) =>
        new HashSet<string>(slugs, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, RedirectRule> Redirects(params RedirectRule[] rules) =>
        rules.ToDictionary(r => r.From, StringComparer.OrdinalIgnoreCase);

    private static RedirectRule Moved(string from, string to) =>
        new() { From = from, To = to, StatusCode = RedirectStatus.MovedPermanently };

    private static RedirectRule Gone(string from) =>
        new() { From = from, To = "", StatusCode = RedirectStatus.Gone };

    private static LinkAuditDocument Doc(string html, string slug = "source-post") =>
        new(slug, "A source post", html);

    private static IReadOnlyList<LinkIssue> Run(string html, ISet<string>? published = null,
        Dictionary<string, RedirectRule>? redirects = null) =>
        LinkAuditor.Analyze(new[] { Doc(html) }, published ?? Published(), redirects ?? new());

    // ── Links that are fine ───────────────────────────────────────────────────

    [Fact]
    public void A_link_to_a_published_post_is_not_reported() =>
        Assert.Empty(Run("<a href=\"/good-post\">Good</a>", Published("good-post")));

    [Theory]
    [InlineData("/tag/cloud-based-ehr")]
    [InlineData("/category/practice-management")]
    [InlineData("/author/hitarth")]
    [InlineData("/series/getting-started")]
    [InlineData("/uploads/features/a.jpg")]
    [InlineData("/feed")]
    [InlineData("/search")]
    [InlineData("/robots.txt")]
    [InlineData("/")]
    public void Generated_and_reserved_routes_are_left_alone(string href) =>
        Assert.Empty(Run($"<a href=\"{href}\">x</a>"));

    [Fact]
    public void External_links_are_out_of_scope() =>
        Assert.Empty(Run("<a href=\"https://example.com/page\" rel=\"nofollow\">Vendor</a>"));

    [Fact]
    public void A_pure_anchor_link_is_ignored() =>
        Assert.Empty(Run("<a href=\"#section\">Jump</a>"));

    [Fact]
    public void A_self_link_is_ignored() =>
        Assert.Empty(Run("<a href=\"/source-post\">This page</a>"));

    [Fact]
    public void Query_strings_trailing_slashes_and_fragments_still_resolve() =>
        Assert.Empty(Run("<a href=\"/good-post/?utm=x#top\">Good</a>", Published("good-post")));

    // ── Broken ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_link_to_nothing_is_broken()
    {
        var issues = Run("<a href=\"/does-not-exist\">Missing</a>", Published("good-post"));

        var issue = Assert.Single(issues);
        Assert.Equal(LinkIssueKind.Broken, issue.Kind);
        Assert.True(issue.IsSevere);
        Assert.Equal("Missing", issue.Anchor);
        Assert.Equal("/does-not-exist", issue.Href);
    }

    // ── Retired ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_link_to_a_retired_url_is_reported_as_gone()
    {
        var issues = Run("<a href=\"/features\">Features</a>",
            Published("good-post"), Redirects(Gone("/features")));

        var issue = Assert.Single(issues);
        Assert.Equal(LinkIssueKind.Gone, issue.Kind);
        Assert.True(issue.IsSevere);
    }

    // ── Redirect hop and chains ───────────────────────────────────────────────

    [Fact]
    public void A_link_through_a_redirect_suggests_linking_the_destination()
    {
        var issues = Run("<a href=\"/old-slug\">Old</a>",
            Published("new-slug"), Redirects(Moved("/old-slug", "/new-slug")));

        var issue = Assert.Single(issues);
        Assert.Equal(LinkIssueKind.Redirect, issue.Kind);
        Assert.False(issue.IsSevere);          // costs equity, not the reader
        Assert.Contains("/new-slug", issue.Detail);
    }

    [Fact]
    public void A_multi_hop_redirect_is_reported_as_a_chain()
    {
        var issues = Run("<a href=\"/first\">First</a>",
            Published("third"),
            Redirects(Moved("/first", "/second"), Moved("/second", "/third")));

        var issue = Assert.Single(issues);
        Assert.Equal(LinkIssueKind.Chain, issue.Kind);
        Assert.Contains("redirects again", issue.Detail);
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    [Fact]
    public void Severe_issues_are_listed_first()
    {
        var html = "<a href=\"/old-slug\">Hop</a> and <a href=\"/nowhere\">Dead</a>";
        var issues = Run(html, Published("new-slug"), Redirects(Moved("/old-slug", "/new-slug")));

        Assert.Equal(2, issues.Count);
        Assert.Equal(LinkIssueKind.Broken, issues[0].Kind);   // severe first
    }

    [Fact]
    public void Every_issue_names_its_source_so_it_can_be_fixed()
    {
        var issues = LinkAuditor.Analyze(
            new[] { Doc("<a href=\"/nowhere\">x</a>", "post-a"), Doc("<a href=\"/nowhere\">y</a>", "post-b") },
            Published(), new Dictionary<string, RedirectRule>());

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.SourceSlug == "post-a");
        Assert.Contains(issues, i => i.SourceSlug == "post-b");
    }

    [Fact]
    public void Anchor_markup_is_stripped_to_readable_text()
    {
        var issues = Run("<a href=\"/nowhere\"><strong>Bold</strong> anchor</a>");

        Assert.Equal("Bold anchor", Assert.Single(issues).Anchor);
    }

    [Fact]
    public void An_empty_document_yields_nothing() =>
        Assert.Empty(LinkAuditor.Analyze(new[] { Doc("") }, Published(), new Dictionary<string, RedirectRule>()));
}
