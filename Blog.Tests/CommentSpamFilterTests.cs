using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The spam filter is pure, so every rule is exercised here with no database. The positive cases are
/// modelled on what a live deployment actually received in September 2026: raw HTML anchors posted
/// for the backlink, and a ~1,500-character lead-generation pitch posted under three different names
/// from three different addresses. The negative cases prove ordinary readers still get through.
/// </summary>
public class CommentSpamFilterTests
{
    private static CommentSubmission Human(
        string content = "Really useful comparison, thank you. We moved to a cloud EHR last year and the billing point matches our experience.",
        string name = "Priya Shah",
        string? honeypot = null,
        CommentFormTokenState token = CommentFormTokenState.Valid,
        TimeSpan? age = null,
        bool duplicate = false,
        int recentFromIp = 0) =>
        new(name, "priya@example.com", content, honeypot, token, age ?? TimeSpan.FromSeconds(90), duplicate, recentFromIp);

    // ── A real reader passes ────────────────────────────────────────────────────────────────

    [Fact]
    public void Ordinary_comment_is_accepted()
    {
        var v = CommentSpamFilter.Evaluate(Human());
        Assert.Equal(CommentSpamAction.Accept, v.Action);
        Assert.False(v.IsSpam);
    }

    [Theory]
    [InlineData("a < b but b > c, so the ordering matters")]
    [InlineData("Loved this <3 thanks")]
    [InlineData("I compared x<y in the sheet and it held")]
    public void Angle_brackets_in_prose_are_not_markup(string content)
    {
        Assert.False(CommentSpamFilter.ContainsMarkup(content));
        Assert.Equal(CommentSpamAction.Accept, CommentSpamFilter.Evaluate(Human(content)).Action);
    }

    [Fact]
    public void One_or_two_links_with_real_text_are_allowed()
    {
        var content = "We use https://www.revolutionehr.com and considered www.eyefinity.com — the migration notes here matched what we saw.";
        Assert.Equal(2, CommentSpamFilter.CountLinks(content));
        Assert.Equal(CommentSpamAction.Accept, CommentSpamFilter.Evaluate(Human(content)).Action);
    }

    [Fact]
    public void Slow_thoughtful_comment_is_accepted()
    {
        var v = CommentSpamFilter.Evaluate(Human(age: TimeSpan.FromHours(3)));
        Assert.Equal(CommentSpamAction.Accept, v.Action);
    }

    // ── Form-filler signatures ──────────────────────────────────────────────────────────────

    [Fact]
    public void Honeypot_filled_is_rejected_silently()
    {
        var v = CommentSpamFilter.Evaluate(Human(honeypot: "http://example.test"));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("honeypot", v.Reason);
    }

    [Fact]
    public void Missing_token_means_the_form_was_never_fetched()
    {
        var v = CommentSpamFilter.Evaluate(Human(token: CommentFormTokenState.Missing, age: null));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
    }

    [Fact]
    public void Invalid_token_asks_the_reader_to_reload_rather_than_calling_them_spam()
    {
        var v = CommentSpamFilter.Evaluate(Human(token: CommentFormTokenState.Invalid, age: null));
        Assert.Equal(CommentSpamAction.AskToRetry, v.Action);
        Assert.False(v.IsSpam);
    }

    [Fact]
    public void Submitted_within_seconds_of_render_is_rejected()
    {
        var v = CommentSpamFilter.Evaluate(Human(age: TimeSpan.FromSeconds(1.2)));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("after the form was rendered", v.Reason);
    }

    [Fact]
    public void Form_older_than_a_day_asks_for_a_reload()
    {
        var v = CommentSpamFilter.Evaluate(Human(age: TimeSpan.FromHours(25)));
        Assert.Equal(CommentSpamAction.AskToRetry, v.Action);
    }

    // ── Backlink spam ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<a href=\"https://drivelity-bulgaria.com/car-rental-veliko-tarnovo\">car hire veliko tarnovo</a>")]
    [InlineData("<a href=\"https://drivelity-balkans.com/car-rental-belgrade-serbia\">cheap car hire belgrade</a>")]
    [InlineData("[url=https://example.test]great post[/url]")]
    [InlineData("Nice article. [Read more](https://example.test/page)")]
    [InlineData("<script>alert(1)</script>")]
    public void Html_bbcode_and_markdown_links_are_rejected(string content)
    {
        var v = CommentSpamFilter.Evaluate(Human(content));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("markup", v.Reason);
    }

    [Fact]
    public void Bare_link_with_no_real_text_is_rejected()
    {
        var v = CommentSpamFilter.Evaluate(Human("check https://example.test/deal"));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("bare link", v.Reason);
    }

    [Fact]
    public void Link_in_the_author_name_is_rejected()
    {
        var v = CommentSpamFilter.Evaluate(Human(name: "cheap car hire www.example.test"));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("author name", v.Reason);
    }

    // ── Lead-generation mailers ─────────────────────────────────────────────────────────────

    private const string LeadGenPitch =
        "Hello Site Owner, My name is Kristofer and I'm betting you'd like your website to generate more leads. " +
        "Here's how: Web Visitors Into Leads is a software widget that works on your site, ready to capture any visitor's Name, " +
        "Email address, and Phone Number. Visit https://blastleadgeneration.com to try out a Live Demo. " +
        "Visit https://blastleadgeneration.com to discover what Web Visitors Into Leads can do for your business. " +
        "Visit https://blastleadgeneration.com to try Web Visitors Into Leads now. " +
        "Update your email preferences by visiting https://blastleadgeneration.com/unsubscribe.aspx?d=example.org";

    [Fact]
    public void Pitch_that_repeats_its_landing_page_is_rejected_for_link_count()
    {
        var v = CommentSpamFilter.Evaluate(Human(LeadGenPitch));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("links", v.Reason);
        Assert.True(CommentSpamFilter.CountLinks(LeadGenPitch) > CommentSpamFilter.MaxLinks);
    }

    [Fact]
    public void Same_body_under_a_different_name_is_rejected_as_a_repeat()
    {
        // The caller found the body in the recent comments; the name and address differ.
        var v = CommentSpamFilter.Evaluate(Human(name: "Sabina Barrow", duplicate: true));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
        Assert.Contains("identical", v.Reason);
    }

    [Fact]
    public void Duplicate_key_ignores_case_and_whitespace()
    {
        var a = CommentSpamFilter.NormalizeForDuplicate("  Hello   World \n");
        var b = CommentSpamFilter.NormalizeForDuplicate("hello world");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Burst_from_one_address_is_rejected()
    {
        var ok = CommentSpamFilter.Evaluate(Human(recentFromIp: CommentSpamFilter.MaxCommentsPerAddressInWindow - 1));
        var no = CommentSpamFilter.Evaluate(Human(recentFromIp: CommentSpamFilter.MaxCommentsPerAddressInWindow));
        Assert.Equal(CommentSpamAction.Accept, ok.Action);
        Assert.Equal(CommentSpamAction.RejectSilently, no.Action);
    }

    [Fact]
    public void Oversized_body_is_rejected()
    {
        var v = CommentSpamFilter.Evaluate(Human(new string('x', CommentSpamFilter.MaxContentLength + 1)));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
    }

    // ── Order of evidence ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Honeypot_wins_over_every_other_signal()
    {
        // Even a submission that would otherwise be asked to retry is discarded when the trap is sprung.
        var v = CommentSpamFilter.Evaluate(Human(honeypot: "x", token: CommentFormTokenState.Invalid, age: null));
        Assert.Equal(CommentSpamAction.RejectSilently, v.Action);
    }
}
