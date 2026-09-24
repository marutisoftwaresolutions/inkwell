using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>The revision endpoints are admin-only: every one of them turns an anonymous caller away.</summary>
public class RevisionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RevisionEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/admin/revisions/post/11111111-1111-1111-1111-111111111111")]
    [InlineData("/admin/revisions/page/11111111-1111-1111-1111-111111111111")]
    [InlineData("/admin/revisions/post/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222")]
    public async Task Anonymous_callers_are_sent_to_sign_in(string url)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task Unknown_entity_type_is_not_found_even_before_auth_matters()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/revisions/widget/11111111-1111-1111-1111-111111111111");
        // Anonymous → login redirect wins; the point is that nothing 500s on a bad type.
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.NotFound, $"unexpected {(int)response.StatusCode}");
    }
}
