using System.Net;
using System.Security.Claims;
using Blog.Core.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The two Desk 500s from the error log (v1.0.9 hardening). <c>/admin/theme</c> failed with a foreign-key error for a
/// session whose user no longer existed; a sign-in cookie now stops working once its account is missing or disabled.
/// <c>/admin/redirects</c> failed converting 'Published' to int in the published-slug query; it compares the stored
/// name now, and this keeps it that way.
/// </summary>
public class DeskSessionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public DeskSessionTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private bool ProbeDb(out string cs)
    {
        cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection") ?? "";
        try
        {
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            probe.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return false; }
    }

    /// <summary>A real Desk sign-in cookie for <paramref name="userId"/>, protected with the app's own key ring.</summary>
    private string Cookie(Guid userId, string role = "Admin")
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Email, "session-check@example.invalid"),
            new Claim(ClaimTypes.Name, "Session check"), new Claim(ClaimTypes.Role, role), new Claim("Permission", "themes.manage"),
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) }, CookieAuthenticationDefaults.AuthenticationScheme);
        return (options.Cookie.Name ?? ".AspNetCore.Cookies") + "=" + options.TicketDataFormat.Protect(ticket);
    }

    private async Task<HttpResponseMessage> GetAs(string cookie, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Cookie", cookie);
        return await client.SendAsync(req);
    }

    private static bool ToLogin(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location?.ToString().Contains("/account/login", StringComparison.OrdinalIgnoreCase) == true;

    [Fact]
    public async Task A_session_for_a_missing_or_disabled_account_is_signed_out_not_a_500()
    {
        if (!ProbeDb(out var cs)) return;

        // The account is not in the database at all (the 2026-09-16 error-log case).
        var ghost = await GetAs(Cookie(Guid.NewGuid()), "/admin/theme");
        Assert.True(ToLogin(ghost), $"Expected a redirect to sign-in, got {(int)ghost.StatusCode}.");
        Assert.Contains(ghost.Headers.TryGetValues("Set-Cookie", out var cleared) ? cleared : [], c => c.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal));

        await using var conn = new SqlConnection(cs);
        var userId = Guid.NewGuid();
        var email = $"session-check-{userId:N}@example.invalid";
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Users (Id, Email, Username, DisplayName, PasswordHash, Role, IsActive, UpdatedAt)
                                      VALUES (@userId, @email, @email, N'Session check', N'x', 'Admin', 1, SYSUTCDATETIME())", new { userId, email });
            var cookie = Cookie(userId);
            Assert.False(ToLogin(await GetAs(cookie, "/admin/theme")), "An active account keeps its session.");

            // Disabled through the Desk's own path: the cached answer is cleared, so the next request is refused.
            await conn.ExecuteAsync("UPDATE Users SET IsActive = 0 WHERE Id = @userId", new { userId });
            Blog.Web.Services.DeskSessionValidator.Forget(_factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), userId);
            Assert.True(ToLogin(await GetAs(cookie, "/admin/theme")));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM CustomThemeSettings WHERE UserId = @userId; DELETE FROM Users WHERE Id = @userId", new { userId });
        }
    }

    [Fact]
    public void Disabling_or_deleting_a_user_ends_their_session_at_once()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        var users = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "UsersController.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(users, @"DeskSessionValidator\.Forget\(").Count);
        Assert.Contains("options.Events.OnValidatePrincipal = DeskSessionValidator.ValidateAsync;",
            File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Program.cs")));
    }

    [Fact]
    public async Task The_redirects_screen_reads_published_slugs_by_status_name()
    {
        if (!ProbeDb(out var cs)) return;
        await using var conn = new SqlConnection(cs);
        var expected = (await conn.QueryAsync<string>(
            "SELECT Slug FROM Posts WHERE Slug <> '' AND ((Status = 'Published' AND PublishedAt <= SYSUTCDATETIME()) OR (Status = 'Scheduled' AND ScheduledAt <= SYSUTCDATETIME()))")).ToHashSet();

        using var scope = _factory.Services.CreateScope();
        var slugs = await scope.ServiceProvider.GetRequiredService<IRedirectRepository>().GetPublishedSlugsAsync();   // threw 'Published' → int before
        Assert.True(expected.SetEquals(slugs), "The suggestions are exactly the live posts.");
        var draft = await conn.ExecuteScalarAsync<string?>("SELECT TOP 1 Slug FROM Posts WHERE Status = 'Draft' AND Slug <> ''");
        if (draft is not null && !expected.Contains(draft)) Assert.DoesNotContain(draft, slugs);
    }
}
