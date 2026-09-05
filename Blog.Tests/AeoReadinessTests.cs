using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The score is advisory, which makes it easy to get quietly wrong — nothing fails when it drifts.
/// These pin the weighting, the honesty rules, and the promise that the editor and the Content
/// Health dashboard can never disagree about the same post.
/// </summary>
public class AeoReadinessTests
{
    private static AeoReadiness.Inputs Perfect() => new()
    {
        AnswerCapsuleWords = 50,
        KeyFactCount       = 4,
        FaqCount           = 4,
        MetaLength         = 140,
        H2Count            = 5,
        InternalLinks      = 3,
        HtmlLength         = 9000,
        HasFeatureImage    = true,
        LastVerifiedAt     = DateTime.UtcNow.AddDays(-10),
        NextReviewAt       = DateTime.UtcNow.AddMonths(6)
    };

    [Fact]
    public void A_complete_post_scores_one_hundred()
    {
        var result = AeoReadiness.Score(Perfect());

        Assert.Equal(100, result.Score);
        Assert.Equal("Excellent", result.Grade);
        Assert.Null(result.NextBestAction);
        Assert.All(result.Signals, s => Assert.True(s.IsComplete));
    }

    [Fact]
    public void An_empty_post_scores_zero_without_throwing()
    {
        var result = AeoReadiness.Score(new AeoReadiness.Inputs());

        Assert.Equal(0, result.Score);
        Assert.Equal("Needs work", result.Grade);
        Assert.All(result.Signals, s => Assert.True(s.IsMissing));
    }

    [Fact]
    public void The_signals_add_up_to_exactly_one_hundred()
    {
        // A weighting change that broke this would silently rescale every post on the dashboard.
        Assert.Equal(100, AeoReadiness.Score(new AeoReadiness.Inputs()).Signals.Sum(s => s.Possible));
    }

    [Fact]
    public void The_answer_capsule_carries_the_most_weight()
    {
        // It is the passage an engine actually lifts, so it must outrank everything else.
        var signals = AeoReadiness.Score(Perfect()).Signals;
        var capsule = signals.Single(s => s.Name == "Answer capsule");

        Assert.All(signals.Where(s => s.Name != "Answer capsule"),
            s => Assert.True(s.Possible <= capsule.Possible));
    }

    // ── Partial credit ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]     // absent
    [InlineData(12, 10)]   // too short to stand alone
    [InlineData(50, 20)]   // in range
    [InlineData(200, 10)]  // too long to quote whole
    public void Capsule_length_earns_partial_credit(int words, int expected)
    {
        var inputs = Perfect();
        inputs.AnswerCapsuleWords = words;

        Assert.Equal(expected, AeoReadiness.Score(inputs).Signals.Single(s => s.Name == "Answer capsule").Earned);
    }

    [Fact]
    public void A_meta_description_over_the_limit_loses_half_not_all()
    {
        var inputs = Perfect();
        inputs.MetaLength = AeoReadiness.MetaDescriptionLimit + 40;

        var meta = AeoReadiness.Score(inputs).Signals.Single(s => s.Name == "Meta description");

        Assert.Equal(5, meta.Earned);
        Assert.Contains("truncated", meta.Advice);
    }

    [Fact]
    public void Verification_older_than_a_year_does_not_count_as_fresh()
    {
        var inputs = Perfect();
        inputs.LastVerifiedAt = DateTime.UtcNow.AddMonths(-18);

        Assert.Equal(5, AeoReadiness.Score(inputs).Signals.Single(s => s.Name == "Freshness").Earned);
    }

    [Fact]
    public void The_next_best_action_is_the_largest_gap()
    {
        var inputs = Perfect();
        inputs.AnswerCapsuleWords = 0;   // -20
        inputs.HasFeatureImage = false;  // -5

        Assert.Equal("Answer capsule", AeoReadiness.Score(inputs).NextBestAction!.Name);
    }

    [Fact]
    public void Every_incomplete_signal_says_what_to_do_about_it()
    {
        // A score with no advice is a number that shames the author without helping them.
        var result = AeoReadiness.Score(new AeoReadiness.Inputs());

        Assert.All(result.Signals, s => Assert.False(string.IsNullOrWhiteSpace(s.Advice)));
    }

    [Fact]
    public void A_complete_signal_offers_no_advice() =>
        Assert.All(AeoReadiness.Score(Perfect()).Signals, s => Assert.Equal("", s.Advice));

    // ── Reading a Post ────────────────────────────────────────────────────────

    [Fact]
    public void Counts_are_read_from_the_post_body()
    {
        var post = new Post
        {
            Html = "<p>Intro</p><h2>One</h2><p>See <a href=\"/other-post\">this</a> and " +
                   "<a href=\"/third-post\">that</a>.</p><h2>Two</h2><h2>Three</h2>",
            AnswerCapsule = string.Join(' ', Enumerable.Repeat("word", 45)),
            MetaDescription = new string('x', 140),
            FeatureImage = "/uploads/hero.jpg",
            KeyFactsJson = JsonSerializer.Serialize(new[]
            {
                new { label = "Price", value = "$99" },
                new { label = "Free trial", value = "30 days" },
                new { label = "Cloud", value = "Yes" }
            }),
            FaqJson = JsonSerializer.Serialize(new[]
            {
                new { question = "Q1?", answer = "A1" },
                new { question = "Q2?", answer = "A2" },
                new { question = "Q3?", answer = "A3" }
            }),
            LastVerifiedAt = DateTime.UtcNow.AddDays(-1),
            NextReviewAt = DateTime.UtcNow.AddMonths(6)
        };

        var inputs = AeoReadiness.From(post);

        Assert.Equal(3, inputs.H2Count);
        Assert.Equal(2, inputs.InternalLinks);
        Assert.Equal(45, inputs.AnswerCapsuleWords);
        Assert.Equal(3, inputs.KeyFactCount);
        Assert.Equal(3, inputs.FaqCount);
        Assert.True(inputs.HasFeatureImage);
    }

    [Fact]
    public void External_links_are_not_counted_as_internal()
    {
        var post = new Post { Html = "<a href=\"https://example.com\">out</a><a href=\"/in\">in</a>" };

        Assert.Equal(1, AeoReadiness.From(post).InternalLinks);
    }

    [Fact]
    public void A_post_with_malformed_faq_json_still_scores()
    {
        // Post.Faqs throws where KeyFacts degrades, and a broken block is exactly when the author
        // opens the editor. Reporting the breakage is the linter's job, not this one's.
        var post = new Post { Html = "<p>x</p>", FaqJson = "{not json", KeyFactsJson = "{also not json" };

        var result = AeoReadiness.Score(post);

        Assert.Equal(0, result.Signals.Single(s => s.Name == "FAQ").Earned);
        Assert.Equal(0, result.Signals.Single(s => s.Name == "Key Facts").Earned);
    }

    [Fact]
    public void A_post_with_no_body_scores_without_throwing() =>
        Assert.InRange(AeoReadiness.Score(new Post()).Score, 0, 100);

    // ── The two screens must agree ────────────────────────────────────────────

    [Fact]
    public void The_editor_and_the_dashboard_score_the_same_post_identically()
    {
        // The dashboard counts headings and links in SQL to avoid transferring article bodies. If
        // the two counting methods drifted, one screen would quietly contradict the other.
        var post = new Post
        {
            Html = "<h2>A</h2><p><a href=\"/one\">1</a></p><h2>B</h2><p><a href=\"/two\">2</a></p>",
            AnswerCapsule = string.Join(' ', Enumerable.Repeat("w", 50)),
            MetaDescription = new string('x', 120),
            KeyFactsJson = JsonSerializer.Serialize(new[] { new { label = "a", value = "b" } }),
            FaqJson = JsonSerializer.Serialize(new[] { new { question = "q?", answer = "a" } }),
            FeatureImage = "/uploads/x.jpg",
            LastVerifiedAt = DateTime.UtcNow.AddDays(-5),
            NextReviewAt = DateTime.UtcNow.AddMonths(3)
        };

        // Exactly what the SQL in ContentHealthRepository computes for the same row.
        var row = new ContentHealthItem
        {
            AnswerCapsule   = post.AnswerCapsule,
            KeyFactsJson    = post.KeyFactsJson,
            FaqJson         = post.FaqJson,
            MetaLength      = post.MetaDescription!.Length,
            HasFeatureImage = true,
            H2Count         = AeoReadiness.CountOccurrences(post.Html!, "</h2>"),
            InternalLinks   = AeoReadiness.CountOccurrences(post.Html!, "href=\"/"),
            HtmlLength      = post.Html!.Length,
            LastVerifiedAt  = post.LastVerifiedAt,
            NextReviewAt    = post.NextReviewAt
        };

        Assert.Equal(AeoReadiness.Score(post).Score, AeoReadiness.Score(row).Score);
    }

    [Fact]
    public void Occurrence_counting_matches_what_the_sql_length_trick_computes()
    {
        const string html = "<h2>a</h2><H2>b</H2><h2>c</h2>";

        // SQL: (LEN(REPLACE(LOWER(html), '</h2>', '123456')) - LEN(html)) with a 6-char replacement
        // for a 5-char token, so the difference is the count.
        var sqlEquivalent = html.ToLowerInvariant().Replace("</h2>", "123456").Length - html.Length;

        Assert.Equal(3, sqlEquivalent);
        Assert.Equal(sqlEquivalent, AeoReadiness.CountOccurrences(html, "</h2>"));
    }

    [Fact]
    public void The_dashboard_weak_threshold_matches_the_grade_boundary() =>
        Assert.Equal("Fair", new AeoReadiness.Result { Score = ContentHealthItem.WeakAeoScore }.Grade);
}
