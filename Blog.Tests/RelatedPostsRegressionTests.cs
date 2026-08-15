using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Regression guard for the LIVE 500 "ORDER BY position number 0 is out of range" — a post with no
/// categories and no tags produced <c>ORDER BY (0) DESC</c> in <see cref="IPostRepository.GetRelatedPostsAsync"/>,
/// which SQL Server read as an (invalid) ordinal column position. Every category-less post's detail
/// page threw a 500. This asserts the query runs (recency fallback) instead of throwing.
/// </summary>
public class RelatedPostsRegressionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public RelatedPostsRegressionTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
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
    public async Task GetRelatedPosts_for_post_with_no_categories_or_tags_does_not_throw()
    {
        if (!_dbAvailable) { _out.WriteLine("[skip] no DB"); return; }

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository>();
        var posts = sp.GetRequiredService<IPostRepository>();

        var admin = await users.GetFirstAdminAsync();
        if (admin is null) { _out.WriteLine("[skip] no admin"); return; }

        // A published post with NO category/tag assignments — exactly the prod condition that 500'd.
        var slug = $"regress-orderby0-{Guid.NewGuid():N}"[..24];
        var id = await posts.CreateAsync(new Post
        {
            Title = "Regress OrderBy0", Slug = slug, Html = "<p>x</p>", Plaintext = "x",
            Status = PostStatus.Published, PublishedAt = DateTime.UtcNow, AuthorId = admin.Id, Type = "post"
        });

        try
        {
            // Before the fix this threw SqlException ("ORDER BY position number 0 is out of range").
            var related = await posts.GetRelatedPostsAsync(id, new List<Guid>(), new List<Guid>(), count: 5);
            Assert.NotNull(related); // runs cleanly via the recency fallback
        }
        finally
        {
            await posts.DeleteAsync(id);
        }
    }
}
