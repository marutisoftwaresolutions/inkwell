using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// /.well-known/security.txt (RFC 9116) is generated from the operator's configured contact, always
/// carries a future Expires, and does not exist until a contact is set.
/// </summary>
public class SecurityTxtTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _factory;
    public SecurityTxtTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void An_email_becomes_a_mailto_contact_with_a_generated_expiry()
    {
        var text = SecurityTxt.Build("security@example.org", "https://example.org/security", "https://example.org/.well-known/security.txt", "en", Now);
        Assert.NotNull(text);
        Assert.Contains("Contact: mailto:security@example.org\n", text);
        Assert.Contains("Expires: 2027-05-11T10:00:00Z\n", text);   // 180 days ahead
        Assert.Contains("Preferred-Languages: en\n", text);
        Assert.Contains("Canonical: https://example.org/.well-known/security.txt\n", text);
        Assert.Contains("Policy: https://example.org/security\n", text);
    }

    [Fact]
    public void Several_contacts_are_listed_once_each()
    {
        var text = SecurityTxt.Build("a@example.org, https://example.org/report , a@example.org", null, null, null, Now)!;
        Assert.Equal(2, text.Split('\n').Count(l => l.StartsWith("Contact: ")));
        Assert.Contains("Contact: https://example.org/report\n", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("http://example.org/report")]            // must be https
    [InlineData("a@example.org\r\nPolicy: https://evil.example")]   // a line break never becomes a second field
    public void Without_a_usable_contact_there_is_no_file(string? contacts) =>
        Assert.Null(SecurityTxt.Build(contacts, null, null, null, Now));

    [Fact]
    public void A_policy_or_canonical_that_is_not_https_is_left_out()
    {
        var text = SecurityTxt.Build("a@example.org", "javascript:alert(1)", "http://example.org/x", "en-GB", Now)!;
        Assert.DoesNotContain("Policy:", text);
        Assert.DoesNotContain("Canonical:", text);
        Assert.Contains("Preferred-Languages: en-gb\n", text);
    }

    [Fact]
    public async Task The_route_is_404_until_a_contact_is_configured()
    {
        if (!TestDatabase.Available(_factory)) return;   // tenant resolution needs the database: skip without one
        using var unset = _factory.WithWebHostBuilder(b => b.UseSetting("Security:Contact", ""));
        var response = await unset.CreateClient().GetAsync("/.well-known/security.txt");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_route_serves_plain_text_once_a_contact_is_set()
    {
        if (!TestDatabase.Available(_factory)) return;   // tenant resolution needs the database: skip without one
        using var set = _factory.WithWebHostBuilder(b => b.UseSetting("Security:Contact", "security@example.org"));
        var response = await set.CreateClient().GetAsync("/.well-known/security.txt");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Contact: mailto:security@example.org", body);
        Assert.Contains("Expires: ", body);
    }
}
