using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Custom fields over the API (v1.0.9, ADR-0025 follow-up): typed values on post and page responses, accepted on
/// create and update with the Desk's validation, never dropped silently, and internal fields kept from anonymous callers.
/// </summary>
public class CustomFieldsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public CustomFieldsApiTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static CustomFieldDefinition Def(string key, string type, string applies = CustomFieldTargets.Posts, bool show = true) =>
        new() { Key = key, Label = key, Type = type, AppliesTo = applies, ShowOnPage = show };

    private static Dictionary<string, JsonElement> Sent(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static readonly IReadOnlyList<CustomFieldDefinition> Fields =
    [
        Def("price", CustomFieldTypes.Number), Def("launched", CustomFieldTypes.Date), Def("site", CustomFieldTypes.Url),
        Def("cloud", CustomFieldTypes.YesNo), Def("note", CustomFieldTypes.Text, show: false),
    ];

    [Fact]
    public void Responses_carry_typed_values_and_anonymous_callers_see_only_reader_fields()
    {
        var json = "{\"cloud\":\"true\",\"note\":\"internal\",\"price\":\"42.5\",\"site\":\"https://example.com/\"}";
        var all = CustomFields.ToApi(Fields, json, includeHidden: true);
        Assert.Equal(new[] { "price", "launched", "site", "cloud", "note" }, all.Keys);   // Settings order
        Assert.Equal(42.5m, all["price"]);
        Assert.Null(all["launched"]);                                                    // unset is null, not missing
        Assert.Equal(true, all["cloud"]);
        Assert.Equal("internal", all["note"]);

        var reader = CustomFields.ToApi(Fields, json, includeHidden: false);
        Assert.False(reader.ContainsKey("note"));
        // A value that no longer fits its type (the type changed in Settings) comes back as text, not lost.
        Assert.Equal("about 40", CustomFields.ToApi(Fields, "{\"price\":\"about 40\"}", true)["price"]);
    }

    [Fact]
    public void Only_the_keys_sent_change_and_null_clears_one()
    {
        var existing = "{\"note\":\"keep me\",\"orphan\":\"from a deleted field\",\"price\":\"10\"}";
        var r = CustomFields.FromApi(Fields, Sent("{\"price\": 12.50, \"cloud\": false, \"launched\": \"2026-10-14\"}"), existing);
        Assert.Empty(r.Errors);
        var v = CustomFields.Parse(r.Json);
        Assert.Equal("12.50", v["price"]);
        Assert.Equal("false", v["cloud"]);
        Assert.Equal("2026-10-14", v["launched"]);
        Assert.Equal("keep me", v["note"]);
        Assert.Equal("from a deleted field", v["orphan"]);

        var cleared = CustomFields.Parse(CustomFields.FromApi(Fields, Sent("{\"price\": null, \"note\": \"\"}"), r.Json).Json);
        Assert.False(cleared.ContainsKey("price"));
        Assert.False(cleared.ContainsKey("note"));
        Assert.Equal("false", cleared["cloud"]);

        // Nothing sent: the stored values come back exactly.
        Assert.Equal(existing, CustomFields.FromApi(Fields, null, existing).Json);
    }

    [Fact]
    public void Wrong_types_unknown_keys_and_objects_are_refused_by_name()
    {
        var r = CustomFields.FromApi(Fields, Sent(
            "{\"price\":\"cheap\",\"launched\":\"14/10/2026\",\"site\":\"javascript:alert(1)\",\"cloud\":\"yes\",\"nope\":1,\"note\":{\"a\":1}}"), null);
        Assert.Equal(new[] { "cloud", "launched", "nope", "note", "price", "site" }, r.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Contains("number", r.Errors["price"]);
        Assert.Contains("Settings", r.Errors["nope"]);

        // A field for pages only is unknown on a post.
        var pageOnly = new[] { Def("room", CustomFieldTypes.Text, CustomFieldTargets.Pages) };
        Assert.True(CustomFields.FromApi(CustomFields.For(pageOnly, pages: false), Sent("{\"room\":\"A\"}"), null).Errors.ContainsKey("room"));
    }

    // ── End to end (dev database) ────────────────────────────────────────────

    private string Token(User user)
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email),
             new Claim(ClaimTypes.Name, user.DisplayName), new Claim(ClaimTypes.Role, user.Role)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_post_and_a_page_round_trip_their_fields_through_the_api()
    {
        string? cs;
        try
        {
            cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = _factory.Services.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var admin = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!;
        var settings = await settingsRepo.GetSettingsAsync(admin.Id);
        var originalFields = settings.CustomFields.ToList();
        await using var conn = new SqlConnection(cs);
        var page = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Slug, string? Json)>(
            "SELECT TOP 1 Id, Slug, CustomFieldsJson FROM Pages WHERE IsPublished = 1 ORDER BY NEWID()");
        Guid postId = Guid.Empty;
        var slug = "zz-api-fields-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            settings.CustomFields = originalFields.Concat(new[]
            {
                Def("zz-api-price", CustomFieldTypes.Number), Def("zz-api-note", CustomFieldTypes.Text, show: false),
                Def("zz-api-room", CustomFieldTypes.Text, CustomFieldTargets.Pages),
            }).ToList();
            await settingsRepo.SaveSettingsAsync(admin.Id, settings);

            var api = _factory.CreateClient();
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(admin));

            // Create with fields.
            var created = await api.PostAsync("/api/v1/posts", Body(new
            {
                title = "API fields check", slug, content = "<p>Body</p>",
                customFields = new Dictionary<string, object?> { ["zz-api-price"] = 12.5, ["zz-api-note"] = "internal" },
            }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            postId = await conn.ExecuteScalarAsync<Guid>("SELECT Id FROM Posts WHERE Slug = @slug", new { slug });
            Assert.Equal("{\"zz-api-note\":\"internal\",\"zz-api-price\":\"12.5\"}",
                await conn.ExecuteScalarAsync<string>("SELECT CustomFieldsJson FROM Posts WHERE Id = @postId", new { postId }));

            // Refused values save nothing and name the field.
            var bad = await api.PutAsync($"/api/v1/posts/{postId}", Body(new { title = "Changed", customFields = new Dictionary<string, object?> { ["zz-unknown"] = 1 } }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var badType = await api.PutAsync($"/api/v1/posts/{postId}", Body(new { title = "Changed", customFields = new Dictionary<string, object?> { ["zz-api-price"] = "cheap" } }));
            Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
            Assert.Contains("zz-api-price", await badType.Content.ReadAsStringAsync());
            Assert.Equal("API fields check", await conn.ExecuteScalarAsync<string>("SELECT Title FROM Posts WHERE Id = @postId", new { postId }));

            // An update without customFields keeps them; null clears only that one.
            Assert.Equal(HttpStatusCode.OK, (await api.PutAsync($"/api/v1/posts/{postId}", Body(new { title = "API fields check" }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.PutAsync($"/api/v1/posts/{postId}", Body(new { customFields = new Dictionary<string, object?> { ["zz-api-price"] = null } }))).StatusCode);
            Assert.Equal("{\"zz-api-note\":\"internal\"}",
                await conn.ExecuteScalarAsync<string>("SELECT CustomFieldsJson FROM Posts WHERE Id = @postId", new { postId }));

            // Reading: made live directly in SQL (scheduled, due) so no webhook or IndexNow ping leaves the machine, and
            // other tests that pick a random published post never pick this one.
            await conn.ExecuteAsync("UPDATE Posts SET Status = 'Scheduled', ScheduledAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()), PublishedAt = SYSUTCDATETIME(), CustomFieldsJson = @j WHERE Id = @postId",
                new { postId, j = "{\"zz-api-note\":\"internal\",\"zz-api-price\":\"9\"}" });
            var anon = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync($"/api/v1/posts/{slug}")).RootElement.GetProperty("data");
            Assert.Equal(9m, anon.GetProperty("customFields").GetProperty("zz-api-price").GetDecimal());
            Assert.False(anon.GetProperty("customFields").TryGetProperty("zz-api-note", out _));
            Assert.False(anon.TryGetProperty("customFieldsJson", out _));
            var signedIn = JsonDocument.Parse(await api.GetStringAsync($"/api/v1/posts/{slug}")).RootElement.GetProperty("data");
            Assert.Equal("internal", signedIn.GetProperty("customFields").GetProperty("zz-api-note").GetString());

            // A page.
            if (page.Id != Guid.Empty)
            {
                await conn.ExecuteAsync("UPDATE Pages SET CustomFieldsJson = @j WHERE Id = @id", new { id = page.Id, j = "{\"zz-api-room\":\"Hall B\"}" });
                var p = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync($"/api/v1/pages/{page.Slug}")).RootElement.GetProperty("data");
                Assert.Equal(page.Slug, p.GetProperty("slug").GetString());
                Assert.Equal("Hall B", p.GetProperty("customFields").GetProperty("zz-api-room").GetString());
            }
            Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync("/api/v1/pages/zz-no-such-page")).StatusCode);
        }
        finally
        {
            settings.CustomFields = originalFields;
            await settingsRepo.SaveSettingsAsync(admin.Id, settings);
            if (page.Id != Guid.Empty) await conn.ExecuteAsync("UPDATE Pages SET CustomFieldsJson = @j WHERE Id = @id", new { id = page.Id, j = page.Json });
            if (postId != Guid.Empty)
            {
                await conn.ExecuteAsync("DELETE FROM PostCategories WHERE PostId = @postId; DELETE FROM PostTags WHERE PostId = @postId; DELETE FROM Revisions WHERE EntityId = @postId; DELETE FROM Posts WHERE Id = @postId", new { postId });
            }
        }
    }
}
