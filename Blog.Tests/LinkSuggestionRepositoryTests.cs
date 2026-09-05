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
/// Exercises the suggester against the real corpus — the taxonomy joins in particular, which unit
/// tests cannot cover. Skips cleanly when the dev database is unreachable.
/// </summary>
public class LinkSuggestionRepositoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public LinkSuggestionRepositoryTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Candidates_carry_their_taxonomy()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILinkSuggestionRepository>();

        var candidates = await repo.GetCandidatesAsync();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.Slug)));

        // If the joins were wrong every post would come back with no topics at all.
        var withTopics = candidates.Count(c => c.TagSlugs.Count > 0 || c.CategorySlugs.Count > 0);
        _out.WriteLine($"candidates={candidates.Count} with taxonomy={withTopics}");
        Assert.True(withTopics > 0, "No post carried any tag or category — the taxonomy joins are wrong.");
    }

    [Fact]
    public async Task It_produces_usable_suggestions_for_a_real_post()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILinkSuggestionRepository>();
        var all = await repo.GetCandidatesAsync();

        // A review post is the strongest case: other posts name the product without linking it.
        var current = all.FirstOrDefault(c => c.Slug.Contains("review", StringComparison.OrdinalIgnoreCase))
                      ?? all.First();
        var others = all.Where(c => c.Id != current.Id).ToList();

        var outbound = LinkSuggester.SuggestOutbound(current, others);
        var inbound = LinkSuggester.SuggestInbound(current, others);
        var status = LinkSuggester.Status(current, others);

        _out.WriteLine($"/{current.Slug}: out={status.Outbound} in={status.Inbound} " +
                       $"suggestions out={outbound.Count} in={inbound.Count}");
        foreach (var s in outbound.Take(3)) _out.WriteLine($"  → {s.Kind,-16} {s.Slug}");
        foreach (var s in inbound.Take(3)) _out.WriteLine($"  ← {s.Kind,-16} {s.Slug}");

        // Nothing may suggest linking a post to itself, in either direction.
        Assert.DoesNotContain(outbound, s => s.Slug == current.Slug);
        Assert.DoesNotContain(inbound, s => s.Slug == current.Slug);
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
