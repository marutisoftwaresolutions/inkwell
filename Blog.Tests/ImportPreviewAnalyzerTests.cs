using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The preview is what an operator decides on before overwriting their own content, so a wrong
/// prediction is worse than no preview. The slug-conflict arithmetic in particular has to match what
/// the importer will actually do.
/// </summary>
public class ImportPreviewAnalyzerTests
{
    private static StagedItem Post(string title, string slug, string? html = null, bool published = true) =>
        new(ImportItemType.Post, title, slug, html, published);

    private static ISet<string> Existing(params string[] slugs) =>
        new HashSet<string>(slugs, StringComparer.OrdinalIgnoreCase);

    private static ImportOptions Options(string conflict = "Suffix") =>
        new() { SlugConflict = conflict };

    // ── Creating ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_slug_is_a_plain_create()
    {
        var preview = ImportPreviewAnalyzer.Analyze([Post("A", "a-post")], Options(), Existing());

        var p = Assert.Single(preview.Predictions);
        Assert.Equal(ImportOutcome.Create, p.Outcome);
        Assert.Equal(1, preview.Creates);
        Assert.False(preview.HasWarnings);
    }

    // ── Slug conflicts, per policy ────────────────────────────────────────────

    [Fact]
    public void Suffix_policy_predicts_the_exact_new_slug()
    {
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "a-post")], Options("Suffix"), Existing("a-post"));

        var p = Assert.Single(preview.Predictions);
        Assert.Equal(ImportOutcome.CreateWithNewSlug, p.Outcome);
        Assert.Equal("a-post-2", p.Slug);
        Assert.Contains("a-post-2", p.Reason);
    }

    [Fact]
    public void Suffixing_skips_numbers_already_taken()
    {
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "a-post")], Options("Suffix"), Existing("a-post", "a-post-2", "a-post-3"));

        Assert.Equal("a-post-4", Assert.Single(preview.Predictions).Slug);
    }

    [Fact]
    public void Two_imported_posts_with_the_same_slug_do_not_collide_with_each_other()
    {
        // The second must account for the first, which does not exist yet at analysis time.
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "dup"), Post("B", "dup")], Options("Suffix"), Existing());

        Assert.Equal("dup", preview.Predictions[0].Slug);
        Assert.Equal("dup-2", preview.Predictions[1].Slug);
    }

    [Fact]
    public void Skip_policy_excludes_the_clashing_item()
    {
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "a-post")], Options("Skip"), Existing("a-post"));

        var p = Assert.Single(preview.Predictions);
        Assert.Equal(ImportOutcome.Skip, p.Outcome);
        Assert.Contains("conflict policy is Skip", p.Reason);
    }

    [Fact]
    public void Overwrite_policy_is_flagged_as_a_warning()
    {
        // Replacing existing content is the one outcome that destroys work, so it must be visible.
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "a-post")], Options("Overwrite"), Existing("a-post"));

        Assert.Equal(ImportOutcome.Overwrite, Assert.Single(preview.Predictions).Outcome);
        Assert.Equal(1, preview.Overwrites);
        Assert.True(preview.HasWarnings);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    [Fact]
    public void Unselected_types_are_skipped_with_a_reason()
    {
        var options = new ImportOptions { ImportPages = false };
        var items = new[] { Post("A", "a"), new StagedItem(ImportItemType.Page, "P", "p", null, true) };

        var preview = ImportPreviewAnalyzer.Analyze(items, options, Existing());

        var page = preview.Predictions.Single(p => p.Type == ImportItemType.Page);
        Assert.Equal(ImportOutcome.Skip, page.Outcome);
        Assert.Contains("not selected", page.Reason);
    }

    [Fact]
    public void Drafts_are_skipped_when_only_published_content_is_selected()
    {
        var options = new ImportOptions { PublishedOnly = true };

        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("Draft", "d", published: false)], options, Existing());

        Assert.Equal(ImportOutcome.Skip, Assert.Single(preview.Predictions).Outcome);
    }

    [Fact]
    public void A_skipped_item_never_reserves_its_slug()
    {
        var options = new ImportOptions { PublishedOnly = true };
        var items = new[] { Post("Draft", "shared", published: false), Post("Live", "shared") };

        var preview = ImportPreviewAnalyzer.Analyze(items, options, Existing());

        // The live post gets the clean slug because the skipped draft never takes it.
        Assert.Equal("shared", preview.Predictions[1].Slug);
        Assert.Equal(ImportOutcome.Create, preview.Predictions[1].Outcome);
    }

    // ── Broken links ──────────────────────────────────────────────────────────

    [Fact]
    public void A_link_to_content_the_import_does_not_bring_is_reported()
    {
        var preview = ImportPreviewAnalyzer.Analyze(
            [Post("A", "a", "<p>See <a href=\"/missing-post\">this</a>.</p>")], Options(), Existing());

        var link = Assert.Single(preview.BrokenLinks);
        Assert.Equal("/missing-post", link.Href);
        Assert.True(preview.HasWarnings);
    }

    [Fact]
    public void A_link_to_a_post_arriving_later_in_the_same_import_is_fine()
    {
        // Judging links in one pass would wrongly flag forward references.
        var items = new[]
        {
            Post("A", "a", "<p><a href=\"/b\">B</a></p>"),
            Post("B", "b")
        };

        Assert.Empty(ImportPreviewAnalyzer.Analyze(items, Options(), Existing()).BrokenLinks);
    }

    [Fact]
    public void A_link_to_existing_content_is_fine() =>
        Assert.Empty(ImportPreviewAnalyzer.Analyze(
            [Post("A", "a", "<p><a href=\"/already-here\">x</a></p>")],
            Options(), Existing("already-here")).BrokenLinks);

    [Theory]
    [InlineData("<a href=\"https://example.com\">x</a>")]
    [InlineData("<a href=\"/tag/something\">x</a>")]
    [InlineData("<a href=\"/files/doc.pdf\">x</a>")]
    [InlineData("<a href=\"#anchor\">x</a>")]
    public void Links_that_are_not_content_slugs_are_ignored(string html) =>
        Assert.Empty(ImportPreviewAnalyzer.Analyze([Post("A", "a", html)], Options(), Existing()).BrokenLinks);

    [Fact]
    public void The_same_broken_link_is_reported_once_per_post()
    {
        var html = "<a href=\"/missing\">one</a> and <a href=\"/missing\">two</a>";

        Assert.Single(ImportPreviewAnalyzer.Analyze([Post("A", "a", html)], Options(), Existing()).BrokenLinks);
    }

    // ── Images ────────────────────────────────────────────────────────────────

    [Fact]
    public void Distinct_images_are_counted_not_fetched()
    {
        var html = "<img src=\"https://old.example/a.jpg\"><img src=\"https://old.example/b.jpg\">" +
                   "<img src=\"https://old.example/a.jpg\">";

        var preview = ImportPreviewAnalyzer.Analyze([Post("A", "a", html)], Options(), Existing());

        Assert.Equal(2, preview.ImagesToFetch);
    }

    [Fact]
    public void Images_are_not_counted_when_they_are_not_being_imported()
    {
        var options = new ImportOptions { ImportImages = false };
        var html = "<img src=\"https://old.example/a.jpg\">";

        Assert.Equal(0, ImportPreviewAnalyzer.Analyze([Post("A", "a", html)], options, Existing()).ImagesToFetch);
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_summary_adds_up()
    {
        var items = new[]
        {
            Post("New", "new-one"),
            Post("Clash", "taken"),
            Post("Draft", "draft-one", published: false)
        };
        var options = new ImportOptions { PublishedOnly = true, SlugConflict = "Suffix" };

        var preview = ImportPreviewAnalyzer.Analyze(items, options, Existing("taken"));

        Assert.Equal(2, preview.Creates);     // new + suffixed clash
        Assert.Equal(1, preview.Renames);
        Assert.Equal(1, preview.Skips);       // the draft
        Assert.Equal(0, preview.Overwrites);
    }
}
