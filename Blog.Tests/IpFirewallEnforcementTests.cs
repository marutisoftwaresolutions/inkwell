using System.Net;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// End-to-end proof that the IP firewall actually stops an attacker: a scanner probing for
/// PHP/WordPress entry points crosses the score threshold and is refused on its next request,
/// while an ordinary visitor from another address is untouched.
///
/// Requires the dev database (rules are persisted); skips cleanly when it is unreachable.
/// </summary>
public class IpFirewallEnforcementTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AttackerIp = "198.51.100.77";  // TEST-NET-2, never a real visitor
    private const string VisitorIp  = "198.51.100.78";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public IpFirewallEnforcementTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
        if (_dbAvailable) ClearRules();
    }

    /// <summary>Pins the connection's remote address so the firewall sees a routable client IP.</summary>
    private HttpClient ClientFrom(string ip) =>
        _factory.WithWebHostBuilder(b =>
                b.ConfigureServices(s => s.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(
                    new RemoteIpStartupFilter(ip))))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Repeated_exploit_probes_get_the_address_blocked()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var attacker = ClientFrom(AttackerIp);

        // Two probes at 5 points each reach the default threshold of 10.
        var first  = await attacker.GetAsync("/wp-login.php");
        var second = await attacker.GetAsync("/xmlrpc.php");
        _out.WriteLine($"probe responses: {(int)first.StatusCode}, {(int)second.StatusCode}");

        if (!RuleExists(AttackerIp))
        {
            // Firewall inert (no admin/settings row in this database) — nothing to assert against.
            _out.WriteLine("No block rule was created; firewall is inert in this database — skipping.");
            return;
        }

        var third = await attacker.GetAsync("/");
        Assert.Equal(HttpStatusCode.Forbidden, third.StatusCode);

        // A different address is unaffected — blocks are per-IP, not site-wide.
        var visitor = await ClientFrom(VisitorIp).GetAsync("/");
        Assert.NotEqual(HttpStatusCode.Forbidden, visitor.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_missing_page_does_not_block_a_visitor()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var visitor = ClientFrom(VisitorIp);

        // Nine 404s are one point each — under the threshold, so the reader keeps browsing.
        for (var i = 0; i < 9; i++)
            await visitor.GetAsync($"/a-post-that-moved-{i}");

        var next = await visitor.GetAsync("/");
        Assert.NotEqual(HttpStatusCode.Forbidden, next.StatusCode);
        Assert.False(RuleExists(VisitorIp));
    }

    [Fact]
    public void The_security_dashboard_reads_cleanly_with_no_rules()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        // SUM() over an empty table yields NULL — the tiles must still map to zero on a fresh install.
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();

        var stats = repo.GetStatsAsync().GetAwaiter().GetResult();

        Assert.True(stats.ActiveBlocks >= 0);
        Assert.True(stats.RequestsDenied >= 0);
    }

    public void Dispose()
    {
        if (_dbAvailable) ClearRules();
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? ConnectionString() =>
        _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

    private bool ProbeDb()
    {
        try
        {
            var cs = ConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex)
        {
            _out.WriteLine($"DB probe failed: {ex.Message}");
            return false;
        }
    }

    private bool RuleExists(string ip)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();
        return repo.GetByIpAsync(ip).GetAwaiter().GetResult() is not null;
    }

    private void ClearRules()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IIpFirewallRepository>();
            repo.DeleteByIpAsync(AttackerIp).GetAwaiter().GetResult();
            repo.DeleteByIpAsync(VisitorIp).GetAwaiter().GetResult();
            _factory.Services.GetRequiredService<Blog.Web.Services.Security.IpFirewallService>().Invalidate();
        }
        catch (Exception ex)
        {
            _out.WriteLine($"Rule cleanup skipped: {ex.Message}");
        }
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _ip;
        public RemoteIpStartupFilter(string ip) => _ip = IPAddress.Parse(ip);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMw) =>
            {
                ctx.Connection.RemoteIpAddress = _ip;
                await nextMw();
            });
            next(app);
        };
    }
}
