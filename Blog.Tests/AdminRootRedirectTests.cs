using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// "/admin" on its own is what people type and what the docs say; every Desk screen lives under
/// /admin/&lt;area&gt;, so the bare path used to be a 404 (which also fed the firewall's 404 score).
/// It must redirect to the dashboard, which then challenges for sign-in.
/// </summary>
public class AdminRootRedirectTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AdminRootRedirectTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Admin_root_redirects_to_dashboard()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/admin/dashboard", response.Headers.Location?.ToString());
    }
}
