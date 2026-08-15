using System.Net;
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
/// The domain previously hosted the OptoSoft product site, so routes like /features are still
/// indexed and still crawled. Per the Production Deployment Rule they must be retired deliberately
/// with <b>410 Gone</b> — an indexed 404 is re-crawled indefinitely and keeps the stale product
/// identity alive in search results.
///
/// Requires the dev database; skips cleanly when it is unreachable.
/// </summary>
public class LegacyUrlGoneTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string GonePath     = "/a-retired-legacy-route-test";
    private const string MovedPath    = "/a-moved-legacy-route-test";
    private const string MovedTarget  = "/best-optometry-ehr-software-2026";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public LegacyUrlGoneTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task A_retired_route_answers_410_not_404()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        await RepoAsync().UpsertAsync(GonePath, "", RedirectStatus.Gone);

        var response = await Client().GetAsync(GonePath);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task A_moved_route_still_301s_so_existing_redirects_keep_working()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        // Backward compatibility: rules written before StatusCode existed default to 301.
        await RepoAsync().UpsertAsync(MovedPath, MovedTarget);

        var response = await Client().GetAsync(MovedPath);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(MovedTarget, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_unknown_route_still_answers_404()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var response = await Client().GetAsync("/no-rule-exists-for-this-path-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_gone_rule_never_yields_a_destination()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var repo = RepoAsync();
        await repo.UpsertAsync(GonePath, "", RedirectStatus.Gone);

        // Returning the empty [To] of a 410 row would redirect the caller to "" — guard against it.
        Assert.Null(await repo.GetDestinationAsync(GonePath));
        Assert.True((await repo.GetRuleAsync(GonePath))!.IsGone);
    }

    public void Dispose()
    {
        if (_dbAvailable) Cleanup();
        GC.SuppressFinalize(this);
    }

    private IRedirectRepository RepoAsync() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<IRedirectRepository>();

    private void Cleanup()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Redirects WHERE [From] IN (@a, @b);";
            cmd.Parameters.AddWithValue("@a", GonePath);
            cmd.Parameters.AddWithValue("@b", MovedPath);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { _out.WriteLine($"Cleanup skipped: {ex.Message}"); }
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
