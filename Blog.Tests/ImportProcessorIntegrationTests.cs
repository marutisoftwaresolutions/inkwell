using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services.Import;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Exercises <see cref="ImportProcessor"/> end-to-end against the app's real SQL Server: seeds an
/// import job with a category + a post (images off), runs a batch, and asserts the post was created
/// and linked to the category — then cleans everything up. Self-skips when no DB is reachable.
/// </summary>
public class ImportProcessorIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public ImportProcessorIntegrationTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
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
        catch { return false; }
    }

    [Fact]
    public async Task ProcessBatch_creates_post_and_links_category()
    {
        if (!_dbAvailable) { _out.WriteLine("[skip] no DB"); return; }

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository>();
        var jobs = sp.GetRequiredService<IImportJobRepository>();
        var processor = sp.GetRequiredService<ImportProcessor>();
        var posts = sp.GetRequiredService<IPostRepository>();
        var categories = sp.GetRequiredService<ICategoryRepository>();

        var admin = await users.GetFirstAdminAsync();
        if (admin is null) { _out.WriteLine("[skip] no admin user"); return; }
        var owner = admin.Id;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var catSlug = $"itest-cat-{tag}";
        var postSlug = $"itest-post-{tag}";

        var options = new ImportOptions
        {
            ImportImages = false, ImportComments = false, ImportAuthors = false,
            ImportPosts = true, ImportPages = false, ImportCategories = true, ImportTags = false,
            SanitizeHtml = true, CreateRedirects = false, SlugConflict = "Suffix", DefaultAuthorId = owner
        };

        var job = new ImportJob
        {
            OwnerId = owner, Source = ImportSource.WordPress, FileName = "itest.xml",
            Status = ImportJobStatus.Analyzed, OptionsJson = JsonSerializer.Serialize(options)
        };
        job.Id = await jobs.CreateJobAsync(job);

        await jobs.AddItemsAsync(new[]
        {
            new ImportItem
            {
                JobId = job.Id, ItemType = ImportItemType.Category, Ordinal = 1, SourceId = catSlug,
                Title = "ITest Cat", Status = ImportItemStatus.Pending,
                DataJson = JsonSerializer.Serialize(new ParsedTerm { Name = "ITest Cat", Slug = catSlug, IsTag = false })
            },
            new ImportItem
            {
                JobId = job.Id, ItemType = ImportItemType.Post, Ordinal = 4, SourceId = "itest-1",
                Title = "ITest Hello", Status = ImportItemStatus.Pending,
                DataJson = JsonSerializer.Serialize(new ParsedPost
                {
                    Title = "ITest Hello", Slug = postSlug, Html = "<p>Body <script>alert(1)</script></p>",
                    Status = "publish", PublishedAt = DateTime.UtcNow, CategorySlugs = new() { catSlug }
                })
            }
        });

        Guid? postId = null, catId = null;
        try
        {
            var result = await processor.ProcessBatchAsync(job, 10);
            Assert.False(result.HasMore);
            Assert.Equal(2, result.Imported);
            Assert.Equal(0, result.Failed);

            var post = await posts.GetBySlugAsync(postSlug);
            Assert.NotNull(post);
            postId = post!.Id;
            Assert.Equal(PostStatus.Published, post.Status);
            // Sanitizer stripped the <script> tag from the imported body.
            Assert.DoesNotContain("<script", post.Html ?? "");

            var linked = await categories.GetForPostAsync(post.Id);
            Assert.Contains(linked, c => c.Slug == catSlug);
            catId = linked.First(c => c.Slug == catSlug).Id;
        }
        finally
        {
            if (postId.HasValue) await posts.DeleteAsync(postId.Value);
            if (catId.HasValue) await categories.DeleteAsync(catId.Value, owner);
            await jobs.DeleteJobAsync(job.Id, owner);
        }
    }
}
