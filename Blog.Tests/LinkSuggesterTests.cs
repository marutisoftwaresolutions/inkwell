using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Suggestion quality decides whether an author trusts the panel or ignores it. A false suggestion
/// is worse than none, so the "do not suggest this" cases carry as much weight as the positives.
/// </summary>
public class LinkSuggesterTests
{
    private static LinkCandidate Post(string slug, string title, string html = "",
        string[]? tags = null, string[]? categories = null) =>
        new(Guid.NewGuid(), slug, title, html, categories ?? [], tags ?? []);

    // ── Unlinked mentions — the strongest signal ──────────────────────────────

    [Fact]
    public void A_named_but_unlinked_post_is_the_top_suggestion()
    {
        var current = Post("switching-guide", "How to switch EHR",
            "<p>Many practices move from RevolutionEHR to something else.</p>");
        var target = Post("revolution-ehr-review", "RevolutionEHR Review 2026");

        var suggestions = LinkSuggester.SuggestOutbound(current, [target]);

        var s = Assert.Single(suggestions);
        Assert.Equal("revolution-ehr-review", s.Slug);
        Assert.Equal(SuggestionKind.UnlinkedMention, s.Kind);
        Assert.Contains("already names it", s.Reason);
    }

    [Fact]
    public void A_mention_that_is_already_linked_is_not_suggested()
    {
        var current = Post("switching-guide", "How to switch EHR",
            "<p>Move from <a href=\"/revolution-ehr-review\">RevolutionEHR</a> if needed.</p>");
        var target = Post("revolution-ehr-review", "RevolutionEHR Review 2026");

        Assert.Empty(LinkSuggester.SuggestOutbound(current, [target]));
    }

    [Fact]
    public void A_name_inside_another_links_text_is_not_treated_as_unlinked()
    {
        // The word sits inside an anchor pointing elsewhere — linking it again would nest anchors.
        var current = Post("guide", "A guide",
            "<p>See <a href=\"/other-post\">the RevolutionEHR comparison</a> for detail.</p>");
        var target = Post("revolution-ehr-review", "RevolutionEHR Review 2026");

        Assert.Empty(LinkSuggester.SuggestOutbound(current, [target]));
    }

    [Fact]
    public void A_name_inside_an_attribute_is_not_a_mention()
    {
        var current = Post("guide", "A guide",
            "<p><img src=\"/uploads/revolutionehr-logo.jpg\" alt=\"RevolutionEHR logo\"></p>");
        var target = Post("revolution-ehr-review", "RevolutionEHR Review 2026");

        // The only occurrences are inside the tag, so there is nothing in the prose to link.
        Assert.DoesNotContain(LinkSuggester.SuggestOutbound(current, [target]),
            s => s.Kind == SuggestionKind.UnlinkedMention);
    }

    [Fact]
    public void Generic_words_never_read_as_a_mention()
    {
        // "Software" and "Review" are stop-listed; without a distinctive token there is no mention.
        var current = Post("guide", "A guide", "<p>Any review of software is useful.</p>");
        var target = Post("software-review", "Software Review 2026");

        Assert.Empty(LinkSuggester.SuggestOutbound(current, [target]));
    }

    // ── Topic overlap ─────────────────────────────────────────────────────────

    [Fact]
    public void Shared_tags_produce_a_suggestion_with_a_readable_reason()
    {
        var current = Post("a", "Post A", "", tags: ["cloud-based-ehr", "billing"]);
        var other = Post("b", "Post B", "", tags: ["cloud-based-ehr"]);

        var s = Assert.Single(LinkSuggester.SuggestOutbound(current, [other]));

        Assert.Equal(SuggestionKind.SharedTopic, s.Kind);
        Assert.Contains("cloud-based-ehr", s.Reason);
    }

    [Fact]
    public void Posts_with_nothing_in_common_are_not_suggested()
    {
        var current = Post("a", "Post A", "", tags: ["billing"]);
        var other = Post("b", "Post B", "", tags: ["frames"]);

        Assert.Empty(LinkSuggester.SuggestOutbound(current, [other]));
    }

    [Fact]
    public void A_mention_outranks_topic_overlap()
    {
        var current = Post("a", "Post A", "<p>We rate MaximEyes highly.</p>", tags: ["ehr"]);
        var topical = Post("b", "Another EHR piece", "", tags: ["ehr"]);
        var mentioned = Post("maximeyes-ehr-review", "MaximEyes EHR Review", "", tags: ["ehr"]);

        var suggestions = LinkSuggester.SuggestOutbound(current, [topical, mentioned]);

        Assert.Equal("maximeyes-ehr-review", suggestions[0].Slug);
        Assert.Equal(SuggestionKind.UnlinkedMention, suggestions[0].Kind);
    }

    [Fact]
    public void More_shared_topics_rank_higher()
    {
        var current = Post("a", "A", "", tags: ["x", "y", "z"]);
        var weak = Post("weak", "Weak", "", tags: ["x"]);
        var strong = Post("strong", "Strong", "", tags: ["x", "y", "z"]);

        var suggestions = LinkSuggester.SuggestOutbound(current, [weak, strong]);

        Assert.Equal("strong", suggestions[0].Slug);
    }

    [Fact]
    public void A_post_never_suggests_itself()
    {
        var current = Post("a", "Post A", "", tags: ["x"]);

        Assert.Empty(LinkSuggester.SuggestOutbound(current, [current]));
    }

    [Fact]
    public void The_result_count_is_capped()
    {
        var current = Post("a", "A", "", tags: ["x"]);
        var others = Enumerable.Range(1, 20).Select(i => Post($"p{i}", $"Post {i}", "", tags: ["x"]));

        Assert.Equal(3, LinkSuggester.SuggestOutbound(current, others, max: 3).Count);
    }

    // ── Inbound direction ─────────────────────────────────────────────────────

    [Fact]
    public void Inbound_finds_posts_that_name_this_one_without_linking()
    {
        var current = Post("maximeyes-ehr-review", "MaximEyes EHR Review");
        var other = Post("cloud-vs-server", "Cloud vs server",
            "<p>For multi-location independents: MaximEyes.</p>");

        var s = Assert.Single(LinkSuggester.SuggestInbound(current, [other]));

        Assert.Equal("cloud-vs-server", s.Slug);
        Assert.Equal(SuggestionKind.UnlinkedMention, s.Kind);
        Assert.Contains("from there", s.Reason);
    }

    [Fact]
    public void Inbound_skips_posts_that_already_link_here()
    {
        var current = Post("target-post", "Target Post");
        var other = Post("source", "Source", "<p>See <a href=\"/target-post\">Target Post</a>.</p>");

        Assert.Empty(LinkSuggester.SuggestInbound(current, [other]));
    }

    [Fact]
    public void A_brand_without_an_internal_capital_falls_back_to_topic_suggestions()
    {
        // Documented limitation: "Compulink" and "Eyefinity" are ordinary Title Case, indistinguishable
        // from common nouns without a dictionary. Treating them as mentions would also fire on words
        // like "Appointment" and "Scheduling", which appear capitalised in headings throughout the
        // corpus - so the rule errs toward silence rather than noise.
        var current = Post("a", "A guide", "<p>We rate Compulink highly.</p>", tags: ["ehr"]);
        var target = Post("compulink-review", "Compulink Review", "", tags: ["ehr"]);

        var s = Assert.Single(LinkSuggester.SuggestOutbound(current, [target]));

        Assert.Equal(SuggestionKind.SharedTopic, s.Kind);   // still suggested, just not as a mention
    }

    // ── Rule compliance ───────────────────────────────────────────────────────

    [Fact]
    public void Status_counts_links_in_both_directions()
    {
        var current = Post("current", "Current",
            "<p><a href=\"/a\">A</a> and <a href=\"/b\">B</a></p>");
        var a = Post("a", "A");
        var b = Post("b", "B", "<p>See <a href=\"/current\">Current</a>.</p>");

        var status = LinkSuggester.Status(current, [a, b]);

        Assert.Equal(2, status.Outbound);
        Assert.Equal(1, status.Inbound);
        Assert.False(status.NeedsOutbound);
        Assert.True(status.NeedsInbound);      // the rule asks for two
        Assert.False(status.IsOrphan);
    }

    [Fact]
    public void A_post_nothing_links_to_is_an_orphan()
    {
        var current = Post("lonely", "Lonely");

        var status = LinkSuggester.Status(current, [Post("a", "A"), Post("b", "B")]);

        Assert.True(status.IsOrphan);
        Assert.True(status.NeedsInbound);
        Assert.True(status.NeedsOutbound);
    }

    [Fact]
    public void Links_to_unknown_slugs_do_not_count_as_outbound()
    {
        // A link to something that is not a published post is not internal-link coverage.
        var current = Post("current", "Current", "<p><a href=\"/deleted-post\">Gone</a></p>");

        Assert.Equal(0, LinkSuggester.Status(current, [Post("a", "A")]).Outbound);
    }
}
