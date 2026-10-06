using System.Net;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Editorial workflow: what a save produces for whom, who may approve or return a post, and the
/// promise the whole feature rests on — a post in review or needing changes is invisible to readers.
/// </summary>
public class EditorialWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public EditorialWorkflowTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    // ── What a save produces ──────────────────────────────────────────────────

    [Theory]
    // publisher
    [InlineData(null, "publish", true, false, PostStatus.Published, false)]
    [InlineData(null, "publish", true, true, PostStatus.Scheduled, false)]
    [InlineData(PostStatus.InReview, "publish", true, false, PostStatus.Published, false)]   // an editor publishing a post in review
    // author without the publish permission
    [InlineData(null, "publish", false, false, PostStatus.InReview, true)]                   // was: silently saved as a draft
    [InlineData(PostStatus.Published, "publish", false, false, PostStatus.InReview, true)]   // edits to a live post go through review
    [InlineData(PostStatus.NeedsChanges, "review", false, false, PostStatus.InReview, true)]
    [InlineData(PostStatus.NeedsChanges, "draft", false, false, PostStatus.NeedsChanges, false)]   // keeps working with the note visible
    [InlineData(PostStatus.InReview, "draft", false, false, PostStatus.Draft, false)]         // the author withdraws from review
    [InlineData(PostStatus.InReview, "draft", true, false, PostStatus.InReview, false)]       // an editor's save is not a decision
    // anything else keeps the status — never a posted Status field
    [InlineData(PostStatus.Draft, null, false, false, PostStatus.Draft, false)]
    [InlineData(PostStatus.Draft, "anything", false, false, PostStatus.Draft, false)]
    [InlineData(PostStatus.InReview, "", false, false, PostStatus.InReview, false)]
    [InlineData(null, null, false, false, PostStatus.Draft, false)]
    public void A_save_produces_the_status_the_rules_allow(PostStatus? current, string? action, bool canPublish, bool future,
        PostStatus expected, bool submitted)
    {
        var decision = EditorialWorkflow.DecideOnSave(current, action, canPublish, future ? Now.AddDays(1) : null, Now);

        Assert.Equal(expected, decision.Status);
        Assert.Equal(submitted, decision.SubmittedForReview);
        if (submitted) Assert.NotNull(decision.Notice);
    }

    [Fact]
    public void Taking_a_live_post_off_the_site_is_always_said()
    {
        var review = EditorialWorkflow.DecideOnSave(PostStatus.Published, "publish", canPublish: false, null, Now);
        Assert.Contains("off the site", review.Notice);

        var draft = EditorialWorkflow.DecideOnSave(PostStatus.Published, "draft", canPublish: true, null, Now);
        Assert.Equal(PostStatus.Draft, draft.Status);
        Assert.Contains("no longer on the site", draft.Notice);

        var quiet = EditorialWorkflow.DecideOnSave(PostStatus.Draft, "draft", canPublish: false, null, Now);
        Assert.Null(quiet.Notice);   // a draft saved as a draft needs no message beyond "Saved"
    }

    [Fact]
    public void Only_a_publisher_can_review_and_only_a_post_in_review()
    {
        Assert.True(EditorialWorkflow.CanReview(PostStatus.InReview, canPublish: true));
        Assert.False(EditorialWorkflow.CanReview(PostStatus.InReview, canPublish: false));
        Assert.False(EditorialWorkflow.CanReview(PostStatus.Draft, canPublish: true));
        Assert.False(EditorialWorkflow.CanReview(PostStatus.NeedsChanges, canPublish: true));
        Assert.False(EditorialWorkflow.CanReview(PostStatus.Published, canPublish: true));
    }

    [Fact]
    public void Sending_a_post_back_needs_a_note()
    {
        Assert.NotNull(EditorialWorkflow.ValidateNote(null));
        Assert.NotNull(EditorialWorkflow.ValidateNote("   "));
        Assert.NotNull(EditorialWorkflow.ValidateNote(new string('x', EditorialWorkflow.MaxNoteLength + 1)));
        Assert.Null(EditorialWorkflow.ValidateNote("Add the pricing source."));
    }

    [Theory]
    [InlineData(PostStatus.Published, false, true)]
    [InlineData(PostStatus.Scheduled, true, false)]
    [InlineData(PostStatus.Draft, false, false)]
    [InlineData(PostStatus.InReview, false, false)]
    [InlineData(PostStatus.NeedsChanges, false, false)]
    public void Only_published_or_due_posts_are_live(PostStatus status, bool future, bool live) =>
        Assert.Equal(live, EditorialWorkflow.IsLive(status, future ? Now.AddDays(1) : Now.AddDays(-1), Now));

    // ── Readers never see a post in the workflow ──────────────────────────────

    /// <summary>
    /// Every public read lists the public statuses explicitly. A query that instead excluded the
    /// unpublished ones ("Status &lt;&gt; 'Draft'") would silently publish every new workflow state.
    /// </summary>
    [Fact]
    public void No_repository_shows_posts_by_excluding_drafts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        var repos = Path.Combine(root!.FullName, "Blog.Infrastructure", "Data", "Repositories");
        var pattern = new Regex(@"Status\s*(<>|!=)\s*'(Draft|Scheduled)'|Status\s+NOT\s+IN\s*\(\s*'Draft'", RegexOptions.IgnoreCase);

        var offenders = Directory.EnumerateFiles(repos, "*.cs")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => pattern.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Public visibility must list Published/Scheduled explicitly:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData(PostStatus.InReview)]
    [InlineData(PostStatus.NeedsChanges)]
    public async Task A_post_in_the_workflow_is_invisible_on_every_public_route(PostStatus status)
    {
        if (!ProbeDb()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var author = await users.GetFirstAdminAsync();
        if (author is null) { _out.WriteLine("No admin on the dev database — skipping."); return; }

        var slug = "workflow-test-" + Guid.NewGuid().ToString("N")[..10];
        var id = await posts.CreateAsync(new Post
        {
            Title = "Workflow test " + slug, Slug = slug, Html = "<p>secret draft text " + slug + "</p>", Plaintext = "secret draft text " + slug,
            AuthorId = author.Id, Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await posts.SetWorkflowAsync(id, status, status == PostStatus.NeedsChanges ? "Fix the intro." : null, DateTime.UtcNow, null);

        try
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/" + slug)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/og-image/{slug}.png")).StatusCode);

            foreach (var path in new[] { "/", "/feed", "/sitemap.xml", "/sitemap-posts-1.xml", "/llms.txt", "/llms-full.txt", "/search?q=" + slug })
            {
                var response = await client.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();
                // No link to it and no title from it. (The search page echoes the query itself, so the bare
                // slug is not the test: its path and its title are.)
                Assert.DoesNotContain("/" + slug, body);
                Assert.DoesNotContain("Workflow test " + slug, body);
                Assert.DoesNotContain("secret draft text", body);
            }

            var queue = await posts.GetReviewQueueAsync(500);
            Assert.Equal(status == PostStatus.InReview, queue.Any(p => p.Id == id));   // only InReview waits for a reviewer
        }
        finally
        {
            await posts.DeleteAsync(id);
        }
    }

    private bool ProbeDb()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
