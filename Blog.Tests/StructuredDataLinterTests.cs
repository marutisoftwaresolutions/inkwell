using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The linter decides what may be published, so a wrong rule either blocks good content or lets
/// invalid schema through. Both directions are pinned here.
/// </summary>
public class StructuredDataLinterTests
{
    private static Post PostWith(string? faq = null, string? keyFacts = null,
                                 string? howTo = null, string? roundup = null) => new()
    {
        Title = "A post",
        Slug = "a-post",
        FaqJson = faq,
        KeyFactsJson = keyFacts,
        HowToJson = howTo,
        RoundupJson = roundup
    };

    private static bool HasError(IReadOnlyList<LintFinding> f) => f.Any(x => x.Severity == LintSeverity.Error);

    // ── Nothing to lint ───────────────────────────────────────────────────────

    [Fact]
    public void A_post_with_no_structured_data_is_clean()
    {
        var findings = StructuredDataLinter.Lint(PostWith());

        Assert.Empty(findings);
        Assert.True(StructuredDataLinter.CanPublish(findings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_absent_block_is_not_a_problem(string? json) =>
        Assert.Empty(StructuredDataLinter.Lint(PostWith(faq: json)));

    // ── Answer capsule ────────────────────────────────────────────────────────

    [Fact]
    public void A_capsule_of_the_right_length_passes()
    {
        var post = PostWith();
        post.AnswerCapsule = string.Join(" ", Enumerable.Repeat("word", 45));

        Assert.Empty(StructuredDataLinter.Lint(post));
    }

    [Fact]
    public void A_capsule_too_short_to_be_an_answer_warns()
    {
        var post = PostWith();
        post.AnswerCapsule = "Optical software is broad.";

        var findings = StructuredDataLinter.Lint(post);

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Block == "Answer capsule" && f.Message.Contains("Too short"));
    }

    [Fact]
    public void A_capsule_long_enough_to_stop_being_quotable_warns()
    {
        var post = PostWith();
        post.AnswerCapsule = string.Join(" ", Enumerable.Repeat("word", 120));

        var findings = StructuredDataLinter.Lint(post);

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Block == "Answer capsule" && f.Message.Contains("aim for 40-60"));
    }

    [Fact]
    public void No_capsule_is_not_a_finding() =>
        Assert.Empty(StructuredDataLinter.Lint(PostWith()));

    // ── Malformed JSON — the silent-failure case ──────────────────────────────

    [Fact]
    public void Malformed_json_is_an_error_not_a_silent_empty_block()
    {
        // This is the failure this linter exists for: the renderer swallows the parse error and
        // emits nothing, so a broken block is indistinguishable from a missing one.
        var findings = StructuredDataLinter.Lint(PostWith(faq: "[{\"Question\":\"Broken\","));

        Assert.True(HasError(findings));
        Assert.False(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Block == "FAQ" && f.Message.Contains("not valid JSON"));
    }

    [Fact]
    public void Each_block_is_parsed_independently()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"Is this valid?\",\"Answer\":\"Yes it certainly is valid.\"}]",
            keyFacts: "{ this is not json"));

        Assert.Contains(findings, f => f.Block == "Key Facts");
        Assert.DoesNotContain(findings, f => f.Block == "FAQ");
    }

    // ── FAQ ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_valid_faq_passes()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"What is optical software?\",\"Answer\":\"It is an umbrella term for five categories of product.\"}]"));

        Assert.Empty(findings);
    }

    [Fact]
    public void An_empty_question_or_answer_is_an_error()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"\",\"Answer\":\"\"}]"));

        Assert.Equal(2, findings.Count(f => f.Severity == LintSeverity.Error));
        Assert.All(findings, f => Assert.Equal(1, f.Item));
    }

    [Fact]
    public void A_thin_answer_warns_but_does_not_block()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"Is it good?\",\"Answer\":\"Yes.\"}]"));

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Severity == LintSeverity.Warning && f.Message.Contains("too thin"));
    }

    [Fact]
    public void A_question_without_a_question_mark_warns()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"Pricing explained\",\"Answer\":\"It costs a certain amount per month.\"}]"));

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Message.Contains("question mark"));
    }

    [Fact]
    public void A_repeated_question_warns()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"What is it?\",\"Answer\":\"A long enough answer here.\"}," +
                 "{\"Question\":\"what is it?\",\"Answer\":\"Another long enough answer.\"}]"));

        Assert.Contains(findings, f => f.Message.Contains("more than once"));
    }

    // ── Key Facts ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_empty_key_fact_value_is_an_error()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            keyFacts: "[{\"Label\":\"Best for\",\"Value\":\"\"}]"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Block == "Key Facts" && f.Message.Contains("blank row"));
    }

    [Fact]
    public void Valid_key_facts_pass() =>
        Assert.Empty(StructuredDataLinter.Lint(PostWith(
            keyFacts: "[{\"Label\":\"Best for\",\"Value\":\"Multi-location groups\"}]")));

    // ── How-To ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_step_without_text_is_an_error()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            howTo: "[{\"Name\":\"Export your data\",\"Text\":\"\"},{\"Name\":\"Import it\",\"Text\":\"Upload the file.\"}]"));

        Assert.True(HasError(findings));
    }

    [Fact]
    public void A_single_step_howto_warns()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            howTo: "[{\"Name\":\"Do the thing\",\"Text\":\"Just do it.\"}]"));

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Block == "How-To" && f.Message.Contains("single step"));
    }

    // ── Roundup — the policy rules ────────────────────────────────────────────

    private const string ValidEntry =
        "{\"Rank\":1,\"Name\":\"RevolutionEHR\",\"Score\":8.5,\"Website\":\"https://www.revolutionehr.com\",\"CtaUrl\":\"/revolution-ehr-review\"}";

    [Fact]
    public void A_valid_roundup_entry_passes() =>
        Assert.Empty(StructuredDataLinter.Lint(PostWith(roundup: "{\"Entries\":[" + ValidEntry + "]}")));

    [Fact]
    public void An_entry_without_a_website_is_an_error()
    {
        // Content External-Link Rule: every entry links the vendor's real, verified site.
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"SomeProduct\",\"Score\":7}]}"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Message.Contains("no website"));
    }

    [Fact]
    public void A_relative_website_is_an_error()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"SomeProduct\",\"Score\":7,\"Website\":\"/somewhere\"}]}"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Message.Contains("absolute http"));
    }

    [Fact]
    public void An_external_cta_url_is_an_error()
    {
        // ctaUrl is the internal review path; putting the vendor site there breaks the topic cluster.
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"P\",\"Score\":7,\"Website\":\"https://example.com\",\"CtaUrl\":\"https://example.com\"}]}"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Message.Contains("external ctaUrl"));
    }

    [Theory]
    [InlineData(10.5)]
    [InlineData(-1)]
    public void A_score_outside_the_scale_is_an_error(double score)
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"P\",\"Score\":" + score.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                     ",\"Website\":\"https://example.com\"}]}"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Message.Contains("0-10 scale"));
    }

    [Fact]
    public void Scoring_our_own_product_is_an_error()
    {
        // The self-serving rating the SEO/AEO Rule bans. This is the rule most likely to be broken
        // by accident, and the one with the worst consequences, so it blocks publish.
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"OptoSoft\",\"Score\":9.4,\"Website\":\"https://www.opto-soft.com\"}]}"));

        Assert.True(HasError(findings));
        Assert.Contains(findings, f => f.Message.Contains("our own product"));
    }

    [Fact]
    public void Our_own_product_may_appear_unscored()
    {
        // Featuring it is fine — the ban is on us rating ourselves.
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[{\"Name\":\"OptoSoft\",\"Score\":0,\"Website\":\"https://www.opto-soft.com\"}," + ValidEntry + "]}"));

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.DoesNotContain(findings, f => f.Message.Contains("our own product"));
    }

    [Fact]
    public void Duplicate_ranks_warn()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            roundup: "{\"Entries\":[" + ValidEntry + ",{\"Rank\":1,\"Name\":\"Other\",\"Score\":7,\"Website\":\"https://example.com\"}]}"));

        Assert.True(StructuredDataLinter.CanPublish(findings));
        Assert.Contains(findings, f => f.Message.Contains("Rank 1"));
    }

    // ── Ordering and gating ───────────────────────────────────────────────────

    [Fact]
    public void Errors_are_listed_before_warnings()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"Short?\",\"Answer\":\"Yes.\"}]",
            keyFacts: "[{\"Label\":\"X\",\"Value\":\"\"}]"));

        Assert.Equal(LintSeverity.Error, findings[0].Severity);
    }

    [Fact]
    public void Warnings_alone_never_block_publishing()
    {
        var findings = StructuredDataLinter.Lint(PostWith(
            faq: "[{\"Question\":\"Short\",\"Answer\":\"Yes.\"}]"));

        Assert.NotEmpty(findings);
        Assert.True(StructuredDataLinter.CanPublish(findings));
    }

    [Fact]
    public void Where_names_the_block_and_the_item()
    {
        var findings = StructuredDataLinter.Lint(PostWith(keyFacts: "[{\"Label\":\"\",\"Value\":\"\"}]"));

        Assert.Contains(findings, f => f.Where == "Key Facts #1");
    }
}
