using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Every MVC route used to answer HEAD with <b>405 Method Not Allowed</b> — routing matches methods
/// exactly and every action is declared <c>[HttpGet]</c>. Confirmed on production 2026-08-15 for
/// <c>HEAD /</c>, <c>HEAD /robots.txt</c> and even for URLs that do not exist (405 instead of 404).
/// Uptime monitors and link checkers use HEAD because it is cheap, so this locks the fix in place.
/// </summary>
public class HeadRequestTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public HeadRequestTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Head(string path) => new(HttpMethod.Head, path);

    [Theory]
    [InlineData("/")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    public async Task Head_matches_get_on_public_routes(string path)
    {
        var client = Client();

        var get  = await client.GetAsync(path);
        var head = await client.SendAsync(Head(path));

        _out.WriteLine($"{path}: GET={(int)get.StatusCode} HEAD={(int)head.StatusCode}");
        Assert.Equal(get.StatusCode, head.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, head.StatusCode);
    }

    [Fact]
    public async Task Head_returns_headers_without_a_body()
    {
        var response = await Client().SendAsync(Head("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Head_on_a_missing_page_is_404_not_405()
    {
        // The old behaviour returned 405 here, which tells a crawler nothing about the URL.
        var response = await Client().SendAsync(Head("/definitely-not-a-real-page-xyz"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_still_returns_a_body()
    {
        // Guard against the body-discarding middleware leaking into normal requests.
        var response = await Client().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_second_get_after_a_head_is_unaffected()
    {
        var client = Client();

        await client.SendAsync(Head("/"));
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }
}
