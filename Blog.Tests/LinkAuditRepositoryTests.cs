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
