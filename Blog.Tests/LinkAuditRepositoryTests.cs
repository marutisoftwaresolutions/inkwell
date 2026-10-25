using System.Net;
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
/// Exercises the link audit against the real database — the repository SQL and the admin route.
/// The classification rules themselves are covered in <see cref="LinkAuditorTests"/>.
/// Skips cleanly when the dev database is unreachable.
/// </summary>
public class LinkAuditRepositoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public LinkAuditRepositoryTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task It_loads_documents_slugs_and_redirects_together()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILinkAuditRepository>();

        var data = await repo.LoadAsync();

        Assert.NotEmpty(data.Documents);
        Assert.NotEmpty(data.PublishedSlugs);
        _out.WriteLine($"documents={data.Documents.Count} slugs={data.PublishedSlugs.Count} redirects={data.Redirects.Count}");

        // The analysis must run over the real corpus without throwing.
        var issues = LinkAuditor.Analyze(data.Documents, data.PublishedSlugs, data.Redirects);
        _out.WriteLine($"issues found: {issues.Count}");
        foreach (var i in issues.Take(10)) _out.WriteLine($"  {i.Kind,-8} {i.Href} (in /{i.SourceSlug})");

        Assert.All(issues, i => Assert.False(string.IsNullOrWhiteSpace(i.SourceSlug)));
    }

    [Fact]
    public async Task A_scheduled_post_counts_as_live_once_its_time_has_passed_and_not_before()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var data = await scope.ServiceProvider.GetRequiredService<ILinkAuditRepository>().LoadAsync();

        // Scheduled posts never flip to Published; the post route serves them once ScheduledAt passes
        // (EditorialWorkflow.IsLive). The audit once counted Published only, so on 2026-10-24 it reported three
        // links to the live /low-dose-atropine-myopia-fda as broken.
        await using var conn = new SqlConnection(_factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();
        var scheduled = new List<(string Slug, bool Due)>();
        await using (var cmd = new SqlCommand(
            "SELECT Slug, CAST(CASE WHEN ScheduledAt <= SYSUTCDATETIME() THEN 1 ELSE 0 END AS BIT) FROM Posts WHERE Status = 'Scheduled' AND ScheduledAt IS NOT NULL", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) scheduled.Add((r.GetString(0), r.GetBoolean(1)));
        _out.WriteLine($"scheduled posts: {scheduled.Count(s => s.Due)} due, {scheduled.Count(s => !s.Due)} still in the future");

        foreach (var (slug, due) in scheduled)
        {
            if (due) Assert.Contains(slug, data.PublishedSlugs);
            else Assert.DoesNotContain(slug, data.PublishedSlugs);
        }
    }

    [Fact]
    public async Task The_page_requires_a_signed_in_editor()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/admin/link-audit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString ?? "");
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
