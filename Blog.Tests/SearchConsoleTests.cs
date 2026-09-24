using System.Security.Cryptography;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Web.Services.SearchConsole;
using Xunit;

namespace Blog.Tests;

public class ServiceAccountCredentialTests
{
    private const string Pem = "-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----\n";

    [Fact]
    public void Parses_a_service_account_key_file()
    {
        var json = JsonSerializer.Serialize(new { type = "service_account", project_id = "p", client_email = "svc@p.iam.gserviceaccount.com", private_key = Pem });
        var c = ServiceAccountCredential.TryParse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(c);
        Assert.Equal("svc@p.iam.gserviceaccount.com", c!.ClientEmail);
        Assert.Equal("p", c.ProjectId);
        Assert.Contains("PRIVATE KEY", c.PrivateKeyPem);
    }

    [Theory]
    [InlineData("", "No credential")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "Not a JSON object")]
    [InlineData("{\"client_id\":\"x\"}", "not a service-account key")]
    [InlineData("{\"type\":\"authorized_user\"}", "\"authorized_user\" credential")]
    [InlineData("{\"type\":\"service_account\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\"}", "client_email is missing")]
    [InlineData("{\"type\":\"service_account\",\"client_email\":\"a@b\",\"private_key\":\"nope\"}", "private_key is missing")]
    public void Rejects_anything_that_is_not_a_service_account_key(string json, string expectedFragment)
    {
        var c = ServiceAccountCredential.TryParse(json, out var error);
        Assert.Null(c);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }
}

public class SearchConsoleJwtTests
{
    [Fact]
    public void Assertion_is_a_valid_rs256_jws_with_the_right_claims()
    {
        using var rsa = RSA.Create(2048);
        var credential = new ServiceAccountCredential("svc@example.iam.gserviceaccount.com", rsa.ExportPkcs8PrivateKeyPem(), null);
        var issued = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        var jwt = SearchConsoleJwt.Create(credential, issued);

        Assert.True(SearchConsoleJwt.Verify(jwt, rsa), "signature did not verify with the public key");
        using var claims = SearchConsoleJwt.Claims(jwt);
        var root = claims.RootElement;
        Assert.Equal(credential.ClientEmail, root.GetProperty("iss").GetString());
        Assert.Equal(SearchConsoleJwt.ReadOnlyScope, root.GetProperty("scope").GetString());
        Assert.Equal(SearchConsoleJwt.TokenEndpoint, root.GetProperty("aud").GetString());
        var iat = root.GetProperty("iat").GetInt64();
        Assert.Equal(new DateTimeOffset(issued).ToUnixTimeSeconds(), iat);
        Assert.Equal(iat + 55 * 60, root.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void A_tampered_assertion_does_not_verify()
    {
        using var rsa = RSA.Create(2048);
        var credential = new ServiceAccountCredential("svc@example.iam.gserviceaccount.com", rsa.ExportPkcs8PrivateKeyPem(), null);
        var jwt = SearchConsoleJwt.Create(credential, DateTime.UtcNow);
        var parts = jwt.Split('.');
        var forged = parts[0] + "." + SearchConsoleJwt.Base64Url("{\"iss\":\"attacker\"}"u8.ToArray()) + "." + parts[2];
        Assert.False(SearchConsoleJwt.Verify(forged, rsa));
    }

    [Theory]
    [InlineData("example.com", "sc-domain:example.com")]
    [InlineData("  Example.COM ", "sc-domain:example.com")]
    [InlineData("sc-domain:Example.com/", "sc-domain:example.com")]
    [InlineData("https://www.example.com", "https://www.example.com/")]
    [InlineData("https://www.example.com/blog", "https://www.example.com/blog/")]
    [InlineData("HTTP://Example.com/", "http://example.com/")]
    [InlineData("", null)]
    [InlineData("not a property", null)]
    [InlineData("ftp://example.com/", null)]
    public void Property_input_is_normalised_to_what_the_api_expects(string input, string? expected)
    {
        Assert.Equal(expected, SearchConsoleJwt.NormaliseProperty(input));
    }

    [Fact]
    public void Url_prefix_properties_are_percent_encoded_in_paths_and_domain_properties_are_not()
    {
        Assert.Equal("https%3A%2F%2Fwww.example.com%2F", SearchConsoleJwt.EncodeProperty("https://www.example.com/"));
        Assert.Equal("sc-domain:example.com", SearchConsoleJwt.EncodeProperty("sc-domain:example.com"));
    }
}

public class SearchConsoleClientParseTests
{
    [Fact]
    public void Parses_rows_and_ignores_malformed_ones()
    {
        const string body = """
        {"rows":[
          {"keys":["2026-09-10","https://www.example.com/compulink-review","compulink review"],"clicks":3,"impressions":120,"ctr":0.025,"position":11.4},
          {"keys":["2026-09-10","https://www.example.com/"],"clicks":1,"impressions":5,"ctr":0.2,"position":3},
          {"keys":["not-a-date","https://www.example.com/x","q"],"clicks":1,"impressions":1,"ctr":1,"position":1}
        ],"responseAggregationType":"byPage"}
        """;
        var rows = SearchConsoleClient.Parse(body);
        var r = Assert.Single(rows);
        Assert.Equal(new DateTime(2026, 9, 10), r.Date);
        Assert.Equal("https://www.example.com/compulink-review", r.Page);
        Assert.Equal("compulink review", r.Query);
        Assert.Equal(3, r.Clicks);
        Assert.Equal(120, r.Impressions);
        Assert.Equal(11.4, r.Position);
    }

    [Fact]
    public void An_empty_response_is_zero_rows_not_an_error()
    {
        Assert.Empty(SearchConsoleClient.Parse("{}"));
        Assert.Empty(SearchConsoleClient.Parse("{\"rows\":[]}"));
    }
}

public class SearchPerformanceAnalyzerTests
{
    private static readonly DateTime AsOf = new(2026, 9, 13);

    private static IEnumerable<PageSearchDay> Days(string page, int currentImpressions, double position, int prevImpressions, int currentClicks = 0)
    {
        // Spread evenly across each window so weighted position equals the given position.
        var curStart = AsOf.AddDays(-27);
        for (var i = 0; i < 28; i++)
            yield return new PageSearchDay(page, curStart.AddDays(i), i == 0 ? currentClicks : 0, currentImpressions / 28 + (i < currentImpressions % 28 ? 1 : 0), position);
        var prevStart = curStart.AddDays(-28);
        for (var i = 0; i < 28; i++)
            yield return new PageSearchDay(page, prevStart.AddDays(i), 0, prevImpressions / 28 + (i < prevImpressions % 28 ? 1 : 0), position + 2);
    }

    [Fact]
    public void Striking_distance_needs_position_8_to_20_and_real_impressions()
    {
        var s = SearchPerformanceAnalyzer.Summarise(Days("https://x.test/compulink-review", 412, 12.67, 400), AsOf);
        var page = Assert.Single(s);
        Assert.True(page.StrikingDistance);
        Assert.Equal("compulink-review", page.Path);
        Assert.Equal(412, page.Impressions);
        Assert.Equal(12.67, page.Position, 2);

        Assert.False(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 412, 7.9, 400), AsOf)[0].StrikingDistance);   // already page one
        Assert.False(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 412, 20.1, 400), AsOf)[0].StrikingDistance);  // too far
        Assert.False(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 10, 12, 400), AsOf)[0].StrikingDistance);     // no demand
    }

    [Fact]
    public void Losing_impressions_needs_a_meaningful_base_and_a_hard_fall()
    {
        Assert.True(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 60, 15, 200), AsOf)[0].LosingImpressions);
        Assert.False(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 180, 15, 200), AsOf)[0].LosingImpressions); // only -10%
        Assert.False(SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 5, 15, 20), AsOf)[0].LosingImpressions);    // base too small
    }

    [Fact]
    public void Low_ctr_flags_page_one_pages_nobody_clicks()
    {
        var low = SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 300, 6.0, 300, currentClicks: 2), AsOf)[0];
        Assert.True(low.LowCtrForPosition);
        Assert.Equal(2.0 / 300, low.Ctr, 4);

        var fine = SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 300, 6.0, 300, currentClicks: 20), AsOf)[0];
        Assert.False(fine.LowCtrForPosition);

        var deep = SearchPerformanceAnalyzer.Summarise(Days("https://x.test/a", 300, 15.0, 300, currentClicks: 0), AsOf)[0];
        Assert.False(deep.LowCtrForPosition); // not on page one — that is a ranking problem, flagged elsewhere
    }

    [Fact]
    public void Position_is_impression_weighted_not_a_plain_mean()
    {
        var rows = new[]
        {
            new PageSearchDay("https://x.test/a", AsOf, 0, 90, 10.0),
            new PageSearchDay("https://x.test/a", AsOf.AddDays(-1), 0, 10, 50.0),
        };
        var (_, imps, pos) = SearchPerformanceAnalyzer.Aggregate(rows);
        Assert.Equal(100, imps);
        Assert.Equal(14.0, pos);
    }

    [Theory]
    [InlineData("https://www.example.com/foo-bar/", "foo-bar")]
    [InlineData("https://www.example.com/", "")]
    [InlineData("https://www.example.com/?category=x", "")]
    [InlineData("https://www.example.com/a%20b", "a b")]
    public void Path_extraction_matches_post_slugs(string page, string expected)
    {
        Assert.Equal(expected, SearchPerformanceAnalyzer.PathOf(page));
    }

    [Fact]
    public void ForSlug_is_case_insensitive_and_null_when_the_page_never_appeared()
    {
        var s = SearchPerformanceAnalyzer.Summarise(Days("https://x.test/Compulink-Review", 50, 12, 50), AsOf);
        Assert.NotNull(SearchPerformanceAnalyzer.ForSlug(s, "compulink-review"));
        Assert.Null(SearchPerformanceAnalyzer.ForSlug(s, "other-post"));
    }
}
