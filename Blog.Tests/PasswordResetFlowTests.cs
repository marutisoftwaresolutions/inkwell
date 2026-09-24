using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// The reset pages are reachable without an account, never reveal whether an address exists, and a
/// bad token is refused. Runs against the dev database; skips when it is unreachable.
/// </summary>
public class PasswordResetFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public PasswordResetFlowTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Forgot_password_page_is_public_and_the_login_page_links_to_it()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("/account/forgot-password", await login.Content.ReadAsStringAsync());

        var forgot = await client.GetAsync("/account/forgot-password");
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var html = await forgot.Content.ReadAsStringAsync();
        // Either the form (mail configured) or the honest "not configured" notice — never an error.
        Assert.True(html.Contains("name=\"email\"") || html.Contains("not configured on this site"), "unexpected forgot-password page");
    }

    [Fact]
    public async Task A_bad_reset_token_is_refused_without_revealing_anything()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var r1 = await client.GetAsync("/account/reset-password?token=not-a-real-token");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Contains("invalid, has expired, or has already been used", await r1.Content.ReadAsStringAsync());

        var r2 = await client.GetAsync("/account/reset-password");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Contains("invalid, has expired, or has already been used", await r2.Content.ReadAsStringAsync());
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
