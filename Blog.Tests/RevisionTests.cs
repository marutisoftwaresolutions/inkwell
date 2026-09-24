using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

public class RevisionSnapshotTests
{
    private static Post SamplePost() => new()
    {
        Id = Guid.NewGuid(), Title = "Best EHR 2026", Slug = "best-ehr-2026", Html = "<p>Body</p>",
        MetaTitle = "mt", MetaDescription = "md", FaqJson = "[{\"q\":\"a\"}]", KeyFactsJson = "{\"k\":1}",
        HowToJson = null, RoundupJson = null, AnswerCapsule = "Short answer.", Status = PostStatus.Published,
    };

    [Fact]
    public void A_post_snapshot_captures_content_meta_blocks_slug_and_status()
    {
        var post = SamplePost();
        var r = RevisionSnapshot.FromPost(post, "Saved", Guid.NewGuid(), "Ada");

        Assert.Equal("Post", r.EntityType);
        Assert.Equal(post.Id, r.EntityId);
        Assert.Equal(post.Title, r.Title);
        Assert.Equal(post.Slug, r.Slug);
        Assert.Equal(post.Html, r.Html);
        Assert.Equal("Published", r.Status);
        Assert.Contains("\"FaqJson\":\"[{\\u0022q\\u0022:\\u0022a\\u0022}]\"", r.StructuredJson!.Replace("\\\"", "\\u0022"));
        Assert.Contains("Short answer.", r.StructuredJson);
    }

    [Fact]
    public void Restore_puts_content_back_but_leaves_slug_and_status_alone()
    {
        var original = SamplePost();
        var r = RevisionSnapshot.FromPost(original, "Saved", Guid.NewGuid(), null);

        var later = SamplePost();
        later.Id = original.Id;
        later.Title = "Renamed"; later.Html = "<p>Rewritten</p>"; later.Slug = "renamed"; later.Status = PostStatus.Draft;
        later.FaqJson = null; later.AnswerCapsule = null; later.MetaDescription = "changed";

        RevisionSnapshot.ApplyToPost(r, later);

        Assert.Equal("Best EHR 2026", later.Title);
        Assert.Equal("<p>Body</p>", later.Html);
        Assert.Equal("Body", later.Plaintext);
        Assert.Equal("md", later.MetaDescription);
        Assert.Equal("[{\"q\":\"a\"}]", later.FaqJson);
        Assert.Equal("Short answer.", later.AnswerCapsule);
        Assert.Equal("renamed", later.Slug);            // untouched
        Assert.Equal(PostStatus.Draft, later.Status);   // untouched
    }

    [Fact]
    public void Same_content_ignores_status_and_slug_but_not_body_or_blocks()
    {
        var a = RevisionSnapshot.FromPost(SamplePost(), "Saved", Guid.Empty, null);

        var statusOnly = SamplePost(); statusOnly.Status = PostStatus.Draft; statusOnly.Slug = "other";
        Assert.True(RevisionSnapshot.SameContent(a, RevisionSnapshot.FromPost(statusOnly, "Saved", Guid.Empty, null)));

        var bodyChanged = SamplePost(); bodyChanged.Html = "<p>Body!</p>";
        Assert.False(RevisionSnapshot.SameContent(a, RevisionSnapshot.FromPost(bodyChanged, "Saved", Guid.Empty, null)));

        var blockChanged = SamplePost(); blockChanged.KeyFactsJson = "{\"k\":2}";
        Assert.False(RevisionSnapshot.SameContent(a, RevisionSnapshot.FromPost(blockChanged, "Saved", Guid.Empty, null)));
    }

    [Fact]
    public void Page_snapshot_and_restore_round_trip()
    {
        var page = new Page { Id = Guid.NewGuid(), Title = "About", Slug = "about", Content = "<p>Us</p>", MetaDescription = "d", IsPublished = true };
        var r = RevisionSnapshot.FromPage(page, "Published", Guid.Empty, null);
        Assert.Equal("Page", r.EntityType);
        Assert.Equal("Published", r.Status);
        Assert.Null(r.StructuredJson);

        page.Title = "About us"; page.Content = "<p>Them</p>"; page.IsPublished = false;
        RevisionSnapshot.ApplyToPage(r, page);
        Assert.Equal("About", page.Title);
        Assert.Equal("<p>Us</p>", page.Content);
        Assert.False(page.IsPublished); // untouched
    }
}

public class TextDiffTests
{
    [Fact]
    public void Inserted_paragraph_shows_as_one_added_block()
    {
        var old = "<p>one</p><p>three</p>";
        var now = "<p>one</p><p>two</p><p>three</p>";
        var d = TextDiff.Lines(old, now);
        Assert.Equal(new[] { DiffKind.Same, DiffKind.Added, DiffKind.Same }, d.Select(l => l.Kind));
        Assert.Equal((1, 0), TextDiff.Count(d));
    }

    [Fact]
    public void Removed_and_changed_lines_are_marked()
    {
        var d = TextDiff.Lines("a\nb\nc", "a\nB\nc\nd");
        Assert.Equal(new[] { DiffKind.Same, DiffKind.Removed, DiffKind.Added, DiffKind.Same, DiffKind.Added }, d.Select(l => l.Kind));
    }

    [Fact]
    public void Identical_and_empty_inputs_produce_no_changes()
    {
        Assert.Equal((0, 0), TextDiff.Count(TextDiff.Lines("<p>x</p>", "<p>x</p>")));
        Assert.Empty(TextDiff.Lines(null, ""));
    }

    [Fact]
    public void Oversized_inputs_fall_back_rather_than_building_a_huge_table()
    {
        var big = string.Join('\n', Enumerable.Range(0, TextDiff.MaxLines + 1).Select(i => "line " + i));
        var d = TextDiff.Lines(big, "line 0");
        Assert.Equal(TextDiff.MaxLines + 1, d.Count(l => l.Kind == DiffKind.Removed));
        Assert.Equal(1, d.Count(l => l.Kind == DiffKind.Added));
    }
}

public class TimeZoneHelperTests
{
    [Fact]
    public void Unknown_or_empty_zone_falls_back_to_utc()
    {
        var utc = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, TimeZoneHelper.ToDisplay(utc, null));
        Assert.Equal(utc, TimeZoneHelper.ToDisplay(utc, "Not/AZone"));
        Assert.True(TimeZoneHelper.IsKnown(""));
        Assert.False(TimeZoneHelper.IsKnown("Not/AZone"));
    }

    [Fact]
    public void Converts_both_ways_for_a_known_zone()
    {
        // India has no DST, so the offset is fixed at +5:30 whichever id family the host uses.
        var id = TimeZoneHelper.IsKnown("India Standard Time") ? "India Standard Time" : "Asia/Kolkata";
        var utc = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc);
        var local = TimeZoneHelper.ToDisplay(utc, id);
        Assert.Equal(new DateTime(2026, 9, 16, 1, 30, 0), local);
        Assert.Equal(utc, TimeZoneHelper.FromDisplay(local, id));
    }

    [Fact]
    public void Unspecified_kind_is_treated_as_utc_because_that_is_what_storage_holds()
    {
        var stored = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(stored, TimeZoneHelper.ToDisplay(stored, "UTC"));
    }
}
