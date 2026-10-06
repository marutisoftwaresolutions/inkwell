using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The home page can be cached at the edge: an anonymous GET sets no cookie and is not marked
/// no-store. The newsletter form gets its anti-forgery token on demand, and a form posted without one
/// writes nothing and offers a page to finish on. Render tests skip when the dev database is unreachable.
/// </summary>
public class HomePageCacheTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public HomePageCacheTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Home_page_sets_no_cookie_and_is_not_no_store()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var res = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Set-Cookie"), "the home page set a cookie: " + string.Join(" | ", res.Headers.TryGetValues("Set-Cookie", out var c) ? c : []));
        Assert.DoesNotContain("no-store", res.Headers.CacheControl?.ToString() ?? "");
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"newsletter-form\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\" value=\"\"", html);   // the slot is empty; the script fills it
    }

    [Fact]
    public async Task Token_endpoint_returns_a_token_that_is_never_cached()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/newsletter/token");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal("noindex", string.Join("", res.Headers.GetValues("X-Robots-Tag")));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task A_form_posted_without_a_token_writes_nothing()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"no-token-{Guid.NewGuid():N}@example.test";
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = email, ["name"] = "Reader" });

        // Without JavaScript: a page to finish on, carrying its own token and the address typed.
        var page = await client.PostAsync("/newsletter/subscribe", form);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Press Subscribe to finish signing up.", html);
        Assert.Matches("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\"", html);
        Assert.Contains($"value=\"{email}\"", html);
        Assert.Contains("noindex", html);

        // From the script: a clear message, not a page.
        using var ajax = new HttpRequestMessage(HttpMethod.Post, "/newsletter/subscribe") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = email }) };
        ajax.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var json = await client.SendAsync(ajax);
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Contains("expired", await json.Content.ReadAsStringAsync());

        await using var conn = new SqlConnection(ConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM Members WHERE Email = @e", conn);
        cmd.Parameters.AddWithValue("@e", email);
        Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public void Newsletter_form_never_renders_a_token_or_reads_temp_data()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        var partial = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Shared", "_NewsletterSignup.cshtml"));
        Assert.DoesNotContain("AntiForgeryToken()", partial);
        Assert.DoesNotContain("TempData[", partial);
        Assert.Contains("/newsletter/token", partial);
        var ctrl = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "NewsletterController.cs"));
        Assert.Contains("_antiforgery.IsRequestValidAsync(HttpContext)", ctrl);   // the check moved, it did not go away
    }

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
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
