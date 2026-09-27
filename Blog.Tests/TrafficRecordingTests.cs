using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Web.Middleware;
using Blog.Web.Services;
using Dapper;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Page-view and crawler tracking after the 2026-09 homepage flood on the live site: the request
/// path only buffers (no insert, no geo-IP call per request), error-page re-executions are not
/// recorded a second time, and Google fetchers without a "bot" token are not counted as readers.
/// </summary>
public class TrafficRecordingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public TrafficRecordingTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    // ── BoundedBatchBuffer ────────────────────────────────────────────────────

    [Fact]
    public void A_full_buffer_drops_and_counts_instead_of_growing()
    {
        var buffer = new BoundedBatchBuffer<int>(3);

        Assert.True(buffer.TryAdd(1));
        Assert.True(buffer.TryAdd(2));
        Assert.True(buffer.TryAdd(3));
        Assert.False(buffer.TryAdd(4));
        Assert.False(buffer.TryAdd(5));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(2, buffer.TakeDropped());
        Assert.Equal(0, buffer.TakeDropped());   // reset after reading
    }

    [Fact]
    public void Drain_returns_items_in_order_up_to_the_limit()
    {
        var buffer = new BoundedBatchBuffer<int>(10);
        for (var i = 1; i <= 5; i++) buffer.TryAdd(i);

        Assert.Equal(new[] { 1, 2, 3 }, buffer.Drain(3));
        Assert.Equal(2, buffer.Count);
        Assert.Equal(new[] { 4, 5 }, buffer.Drain(100));
        Assert.Empty(buffer.Drain(100));
        Assert.True(buffer.TryAdd(6));           // space is reclaimed after draining
    }

    [Fact]
    public void Flush_is_due_on_size_or_age_and_never_when_empty()
    {
        var last = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var interval = TimeSpan.FromSeconds(30);

        Assert.False(BoundedBatchBuffer<int>.IsFlushDue(0, 500, last, last.AddHours(1), interval));
        Assert.False(BoundedBatchBuffer<int>.IsFlushDue(10, 500, last, last.AddSeconds(5), interval));
        Assert.True(BoundedBatchBuffer<int>.IsFlushDue(500, 500, last, last.AddSeconds(5), interval));
        Assert.True(BoundedBatchBuffer<int>.IsFlushDue(1, 500, last, last.AddSeconds(30), interval));
    }

    // ── Bot filter ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.6613.137 Mobile Safari/537.36 (compatible; GoogleOther)")]
    [InlineData("Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.6613.137 Mobile Safari/537.36 (compatible; Google-InspectionTool/1.0;)")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko; compatible; Google-Read-Aloud; +https://support.google.com/webmasters/answer/1061943) Chrome/128.0 Safari/537.36")]
    [InlineData("Mediapartners-Google")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("")]
    public void Non_human_agents_are_not_counted(string ua) => Assert.True(BotUserAgent.IsBot(ua));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36")]
    public void Ordinary_browsers_are_counted(string ua) => Assert.False(BotUserAgent.IsBot(ua));

    // ── Error re-execution ────────────────────────────────────────────────────

    [Fact]
    public void Only_the_original_pass_of_an_error_response_is_recorded()
    {
        var original = new DefaultHttpContext();
        Assert.False(CrawlerVisitMiddleware.IsErrorReExecution(original));

        var statusReExecute = new DefaultHttpContext();
        statusReExecute.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPath = "/missing", OriginalPathBase = ""
        });
        Assert.True(CrawlerVisitMiddleware.IsErrorReExecution(statusReExecute));

        var exceptionReExecute = new DefaultHttpContext();
        exceptionReExecute.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException(), Path = "/broken"
        });
        Assert.True(CrawlerVisitMiddleware.IsErrorReExecution(exceptionReExecute));
    }

    // ── The hot path does no I/O (source scan) ────────────────────────────────

    [Fact]
    public void Tracking_middleware_never_touches_the_database_or_network_itself()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Blog.Web", "Middleware", "CrawlerVisitMiddleware.cs"));

        Assert.DoesNotContain("Repository", src);
        Assert.DoesNotContain("HttpClient", src);
        Assert.DoesNotContain("CreateScope", src);
        Assert.Contains("_recorder.Record", src);
    }

    // ── End to end against the dev database ───────────────────────────────────

    [Fact]
    public async Task A_crawler_404_is_recorded_once_under_the_requested_path()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var marker = "inkwell-test-" + Guid.NewGuid().ToString("N")[..10];
        var path = "/no-such-page-" + marker;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("User-Agent", $"Mozilla/5.0 (compatible; GPTBot/1.1; +https://openai.com/gptbot; {marker})");
        var response = await client.SendAsync(request);
        Assert.Equal(404, (int)response.StatusCode);

        await _factory.Services.GetRequiredService<TrafficRecorder>().FlushAsync();

        await using var conn = new SqlConnection(cs);
        try
        {
            var rows = (await conn.QueryAsync<(string Path, int StatusCode)>(
                "SELECT Path, StatusCode FROM CrawlerVisits WHERE UserAgent LIKE @m",
                new { m = "%" + marker + "%" })).ToList();

            var row = Assert.Single(rows);                 // not a second /error/404 row
            Assert.Equal(path, row.Path);
            Assert.Equal(404, row.StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM CrawlerVisits WHERE UserAgent LIKE @m", new { m = "%" + marker + "%" });
        }
    }

    [Fact]
    public async Task A_page_view_batch_round_trips_through_the_real_table()
    {
        if (!ProbeDb(out var cs)) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        var path = "/batch-test-" + Guid.NewGuid().ToString("N")[..10];
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPageViewRepository>();

        var batch = Enumerable.Range(0, 3).Select(i => new PageView
        {
            OwnerId = Guid.Empty, Path = path, IpHash = "test", UserAgent = "test",
            Country = i == 0 ? "GB" : null, CreatedAt = DateTime.UtcNow
        }).ToList();

        await using var conn = new SqlConnection(cs);
        try
        {
            Assert.Equal(3, await repo.RecordManyAsync(batch));
            Assert.Equal(0, await repo.RecordManyAsync(Array.Empty<PageView>()));
            Assert.Equal(3, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PageViews WHERE Path = @path", new { path }));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM PageViews WHERE Path = @path", new { path });
        }
    }

    private bool ProbeDb(out string connectionString)
    {
        connectionString = "";
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            connectionString = cs;
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
