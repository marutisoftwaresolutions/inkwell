using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

public class ReadingTimeTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one two three", 3)]
    [InlineData("well-known state-of-the-art it's", 3)]        // hyphens and apostrophes join, not split
    [InlineData("<p>Hello <strong>world</strong></p><br/>", 2)] // tags count as nothing
    [InlineData("&amp; &nbsp; 2026 &mdash; done", 2)]           // entities decode; punctuation-only tokens are none
    [InlineData("¿Qué tal? Très bien — 東京 2026", 6)]            // any script with letters or digits
    public void CountWords_counts_words_not_tokens(string? text, int expected)
    {
        Assert.Equal(expected, TextHelper.CountWords(text));
    }

    [Fact]
    public void ReadingMinutes_never_reports_zero()
    {
        Assert.Equal(1, TextHelper.ReadingMinutes("", ""));
        Assert.Equal(1, TextHelper.ReadingMinutes("a short note", null));
    }

    [Fact]
    public void ReadingMinutes_rounds_to_the_nearest_minute_at_the_standard_speed()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", TextHelper.WordsPerMinute * 3 + 100)); // 3.42 min
        Assert.Equal(3, TextHelper.ReadingMinutes(words));

        var longer = string.Join(' ', Enumerable.Repeat("word", TextHelper.WordsPerMinute * 3 + 130)); // 3.55 min
        Assert.Equal(4, TextHelper.ReadingMinutes(longer));
    }

    [Fact]
    public void ReadingMinutes_falls_back_to_the_html_when_plaintext_is_missing()
    {
        var html = "<p>" + string.Join(' ', Enumerable.Repeat("word", TextHelper.WordsPerMinute * 2)) + "</p>";
        Assert.Equal(2, TextHelper.ReadingMinutes(null, html));
        Assert.Equal(2, TextHelper.ReadingMinutes("", html));
    }

    [Fact]
    public void Plaintext_wins_over_html_when_both_exist()
    {
        var plain = string.Join(' ', Enumerable.Repeat("word", TextHelper.WordsPerMinute));      // 1 min
        var html  = "<p>" + string.Join(' ', Enumerable.Repeat("word", TextHelper.WordsPerMinute * 5)) + "</p>";
        Assert.Equal(1, TextHelper.ReadingMinutes(plain, html));
    }
}
