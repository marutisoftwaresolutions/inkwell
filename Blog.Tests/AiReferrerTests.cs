using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Visits that arrive from an AI assistant's answer are recognised by where they came from, counted
/// per assistant, and include visits recorded before the list existed. A search engine that shares a
/// domain with an assistant is not mistaken for it, and the other way round.
/// </summary>
public class AiReferrerTests
{
    private static QueryCollection Query(params (string Key, string Value)[] pairs) =>
        new(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value)));

    [Theory]
    [InlineData("chatgpt.com", "ChatGPT")]
    [InlineData("www.perplexity.ai", "Perplexity")]
    [InlineData("https://claude.ai/chat/abc", "Claude")]
    [InlineData("gemini.google.com", "Gemini")]
    [InlineData("copilot.microsoft.com", "Copilot")]
    [InlineData("labs.perplexity.ai", "Perplexity")]
    [InlineData("CHATGPT.COM", "ChatGPT")]
    public void A_known_assistant_is_named(string host, string name) => Assert.Equal(name, AiReferrers.NameFor(host));

    [Theory]
    [InlineData("google.com")]
    [InlineData("www.bing.com")]
    [InlineData("notchatgpt.com")]              // a look-alike domain is not the assistant
    [InlineData("chatgpt.com.example.org")]
    [InlineData("example.org")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_an_assistant(string? host) => Assert.Null(AiReferrers.NameFor(host));

    [Fact]
    public void A_visit_is_stored_as_an_ai_visit_and_searches_stay_searches()
    {
        Assert.Equal(("chatgpt.com", "ai"), VisitSourceClassifier.Classify("https://chatgpt.com/", Query()));
        Assert.Equal(("gemini.google.com", "ai"), VisitSourceClassifier.Classify("https://gemini.google.com/app", Query()));   // not "google, organic"
        Assert.Equal(("copilot.microsoft.com", "ai"), VisitSourceClassifier.Classify("https://copilot.microsoft.com/", Query()));
        Assert.Equal(("google", "organic"), VisitSourceClassifier.Classify("https://www.google.com/search?q=x", Query()));
        Assert.Equal(("bing", "organic"), VisitSourceClassifier.Classify("https://www.bing.com/search?q=x", Query()));
        Assert.Equal(("direct", "none"), VisitSourceClassifier.Classify(null, Query()));
    }

    [Fact]
    public void A_link_marked_by_the_assistant_counts_and_a_campaign_keeps_its_own_medium()
    {
        Assert.Equal(("chatgpt.com", "ai"), VisitSourceClassifier.Classify(null, Query(("utm_source", "chatgpt.com"))));
        Assert.Equal(("chatgpt.com", "email"), VisitSourceClassifier.Classify(null, Query(("utm_source", "chatgpt.com"), ("utm_medium", "email"))));
        Assert.Equal(("newsletter", "unknown"), VisitSourceClassifier.Classify(null, Query(("utm_source", "newsletter"))));
    }

    [Fact]
    public void Only_a_leading_www_is_dropped_from_a_referring_site()
    {
        // The old code trimmed every leading "w" and ".": wired.com was stored as "ired.com".
        Assert.Equal(("wired.com", "referral"), VisitSourceClassifier.Classify("https://www.wired.com/story/x", Query()));
        Assert.Equal(("wikipedia.org", "referral"), VisitSourceClassifier.Classify("https://wikipedia.org/", Query()));
        Assert.Equal(("w3.org", "referral"), VisitSourceClassifier.Classify("https://w3.org/", Query()));
    }

    [Fact]
    public void The_report_counts_each_assistant_once_and_includes_visits_stored_before_the_list_existed()
    {
        var rows = new[]
        {
            ("chatgpt.com", "ai", 7),
            ("chatgpt.com", "referral", 5),          // recorded before assistants were recognised
            ("chat.openai.com", "referral", 2),      // the assistant's older address
            ("perplexity.ai", "ai", 4),
            ("google", "organic", 900),
            ("wired.com", "referral", 3),
            ("claude.ai", "ai", 0),
        };
        var summary = AiReferrers.Summarise(rows);
        Assert.Equal(new[] { ("ChatGPT", 14), ("Perplexity", 4) }, summary.ToArray());
        Assert.Empty(AiReferrers.Summarise([("google", "organic", 10)]));
    }

    [Fact]
    public void The_desk_names_the_assistant_whichever_way_the_visit_was_stored()
    {
        Assert.Equal("AI assistant (ChatGPT)", VisitSourceClassifier.GetDisplayLabel("chatgpt.com", "ai"));
        Assert.Equal("AI assistant (Perplexity)", VisitSourceClassifier.GetDisplayLabel("perplexity.ai", "referral"));
        Assert.Equal("Referral (wired.com)", VisitSourceClassifier.GetDisplayLabel("wired.com", "referral"));
        Assert.Equal("Organic Search", VisitSourceClassifier.GetDisplayLabel("google", "organic"));
    }

    [Fact]
    public void The_card_says_what_it_cannot_see_and_the_download_names_only_known_sheets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var view = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Analytics", "Index.cshtml"));
        Assert.Contains("No visits from an AI assistant in this period.", view);
        Assert.Contains("An assistant that passes no referrer is counted as Direct.", view);
        var controller = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "AnalyticsController.cs"));
        Assert.Contains("if (sheet is not (\"sources\" or \"ai\" or \"daily\" or \"referrers\" or \"countries\")) sheet = \"pages\";", controller);
    }
}
