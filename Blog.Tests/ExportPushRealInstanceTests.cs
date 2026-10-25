using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;
using Blog.Web.Services.Export.Push;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The real-instance proof the Data Portability rule requires for push (ADR-0029; EXPORTER-PLAN §11): the dev
/// site pushed into a running WordPress and a running Ghost through the real clients, repository and job logic,
/// then pushed again with "Skip" to prove nothing is created twice. Opt-in, because it needs those sites:
/// <list type="bullet">
/// <item><c>INKWELL_PUSH_WP</c> = <c>http://127.0.0.1:9400|admin|application password</c></item>
/// <item><c>INKWELL_PUSH_GHOST</c> = <c>http://localhost:2368|id:secret</c></item>
/// <item><c>INKWELL_PUSH_SOURCE</c> = this site's origin for image links (default <c>http://localhost:5099</c>)</item>
/// <item><c>INKWELL_PUSH_ALSO_UPDATE</c> = 1 adds a third push with "Update", which replaces every item</item>
/// </list>
/// Without them, or without the dev database, the test returns at once.
/// </summary>
public class ExportPushRealInstanceTests
{
    private readonly ITestOutputHelper _out;
    public ExportPushRealInstanceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task The_dev_site_pushes_to_a_real_WordPress() =>
        await PushTwiceAsync(ExportTarget.WordPress, Environment.GetEnvironmentVariable("INKWELL_PUSH_WP"));

    [Fact]
    public async Task The_dev_site_pushes_to_a_real_Ghost() =>
        await PushTwiceAsync(ExportTarget.Ghost, Environment.GetEnvironmentVariable("INKWELL_PUSH_GHOST"));

    private async Task PushTwiceAsync(ExportTarget target, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) { _out.WriteLine($"No {target} target configured — skipping."); return; }
        var parts = spec.Split('|');
        var (site, user, secret) = target == ExportTarget.WordPress ? (parts[0], parts[1], parts[2]) : (parts[0], (string?)null, parts[1]);
        var source = Environment.GetEnvironmentVariable("INKWELL_PUSH_SOURCE") ?? "http://localhost:5099";

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exports:PushAllowPrivateTargets"] = "true",   // the test sites are local, over http
                ["Exports:PushBatchSize"] = "50",
            }));
            // The test drives the job itself; the background scheduler must not work the same push alongside it.
            b.ConfigureTestServices(s => s.RemoveAll<IHostedService>());
        });
        try
        {
            var cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        var rounds = new List<(string Round, PushConflictPolicy Policy)> { ("first", PushConflictPolicy.Skip), ("again, with Skip", PushConflictPolicy.Skip) };
        if (Environment.GetEnvironmentVariable("INKWELL_PUSH_ALSO_UPDATE") == "1") rounds.Add(("again, with Update", PushConflictPolicy.Update));
        foreach (var (round, policy) in rounds)
        {
            using var scope = factory.Services.CreateScope();
            var push = scope.ServiceProvider.GetRequiredService<ExportPushService>();
            var active = (await push.ListRecentAsync(20)).Where(p => p.IsActive).ToList();
            foreach (var stale in active) await push.CancelAsync(stale.Id);   // a run left over from an aborted proof

            var started = await push.StartAsync(target, site, user, secret, policy,
                new ExportOptions { IncludePages = true }, source, Guid.Empty, "real-instance test", CancellationToken.None);
            Assert.True(started.Push is not null, started.Error);
            var id = started.Push!.Id;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            for (var run = 0; run < 120; run++)
            {
                using var runScope = factory.Services.CreateScope();
                var r = await runScope.ServiceProvider.GetRequiredService<ExportPushService>().RunAsync(CancellationToken.None);
                var state = await push.GetAsync(id);
                if (state!.Status != PushStatus.Running) break;
                if (state.ResumeAt is DateTime resume && resume > DateTime.UtcNow) await Task.Delay(resume - DateTime.UtcNow);
                foreach (var waiting in await push.ItemsAsync(id, PushItemStatus.Pending))
                    if (waiting.Attempts > 0) _out.WriteLine($"  retrying {waiting.Title}: {waiting.Error}");
                if (r.Sent + r.Skipped + r.Retrying + r.Failed == 0) await Task.Delay(TimeSpan.FromSeconds(30));   // only retries are left; they are due later
            }

            var done = await push.GetAsync(id);
            var counts = await push.CountAsync(id);
            _out.WriteLine($"{target} push {round}: {done!.Status} in {clock.Elapsed:mm\\:ss}; {counts.Created} created, {counts.Updated} updated, " +
                           $"{counts.Skipped} skipped, {counts.Failed} failed, of {done.TotalCount}. {done.Message}");
            foreach (var f in await push.ItemsAsync(id, PushItemStatus.Failed)) _out.WriteLine($"  FAILED {f.Title}: {f.Error}");

            Assert.Equal(PushStatus.Completed, done.Status);
            Assert.Equal(0, counts.Failed);
            if (round == "first") Assert.Equal(done.TotalCount, counts.Created + counts.Skipped);
            else if (policy == PushConflictPolicy.Skip)
            {
                Assert.Equal(0, counts.Created);
                Assert.Equal(done.TotalCount, counts.Skipped);
            }
            else
            {
                Assert.Equal(0, counts.Created);
                Assert.Equal(done.TotalCount, counts.Updated);   // every item already exists, so every one is replaced
            }
        }
    }
}
