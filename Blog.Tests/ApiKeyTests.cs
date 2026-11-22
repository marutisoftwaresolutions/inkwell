using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// API keys: the shape and hashing of a key, scopes that only ever narrow the owner's permissions,
/// and the running API accepting a key in place of a sign-in token with exactly those limits.
/// </summary>
public class ApiKeyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiKeyTests(WebApplicationFactory<Program> factory) => _factory = factory;

    // ── Format ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_generated_key_is_recognisable_unique_and_hashes_to_64_hex_characters()
    {
        var (a, prefixA) = ApiKeyFormat.Generate();
        var (b, _) = ApiKeyFormat.Generate();
        Assert.NotEqual(a, b);
        Assert.StartsWith("ink_" + prefixA + "_", a);
        Assert.True(ApiKeyFormat.LooksLikeKey(a));
        Assert.Equal(64, ApiKeyFormat.Hash(a).Length);
        Assert.Equal(ApiKeyFormat.Hash(a), ApiKeyFormat.Hash(a));
        Assert.NotEqual(ApiKeyFormat.Hash(a), ApiKeyFormat.Hash(b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.abc")]                      // a sign-in token is not a key
    [InlineData("ink_short")]
    [InlineData("ink_ABCDEF12_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]   // prefix must be lower-case hex
    [InlineData("ink_ab12cd34_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa aaa")]   // no spaces
    public void Anything_else_is_not_mistaken_for_a_key(string? value) => Assert.False(ApiKeyFormat.LooksLikeKey(value));

    [Fact]
    public void Scopes_are_normalised_and_reading_is_always_included()
    {
        Assert.Equal(new[] { "read" }, ApiKeyFormat.NormalizeScopes(null));
        Assert.Equal(new[] { "read", "write", "publish" }, ApiKeyFormat.NormalizeScopes(new[] { "PUBLISH", "write", "write", "admin", "" }));
        Assert.Equal("read,comments", ApiKeyFormat.JoinScopes(new[] { "comments" }));
    }

    [Fact]
    public void A_key_holds_only_permissions_its_owner_has_and_its_scopes_name()
    {
        var admin = new[] { "posts.edit", "posts.publish", "posts.delete", "pages.manage", "comments.manage", "settings.manage", "themes.manage", "media.manage" };
        Assert.Empty(ApiKeyFormat.AllowedPermissions(new[] { "read" }, admin));
        Assert.Equal(new[] { "posts.edit", "pages.manage" }, ApiKeyFormat.AllowedPermissions(new[] { "write" }, admin));
        Assert.Equal(new[] { "posts.edit", "posts.publish", "posts.delete", "pages.manage", "comments.manage" },
            ApiKeyFormat.AllowedPermissions(new[] { "write", "publish", "comments" }, admin));

        // A contributor's key cannot publish whatever scope it was given.
        Assert.Equal(new[] { "posts.edit" }, ApiKeyFormat.AllowedPermissions(new[] { "write", "publish", "comments" }, new[] { "posts.edit" }));

        // Settings, themes and media never reach a key.
        foreach (var never in new[] { "settings.manage", "themes.manage", "media.manage" })
            Assert.DoesNotContain(never, ApiKeyFormat.AllowedPermissions(ApiKeyFormat.AllScopes.Select(s => s.Scope), admin));
    }

    [Fact]
    public void A_key_name_is_trimmed_and_cannot_carry_control_characters()
    {
        Assert.Equal("Claude Desktop", ApiKeyFormat.CleanName("  Claude\r\n Desktop\t "));
        Assert.Equal(60, ApiKeyFormat.CleanName(new string('x', 200)).Length);
    }

    [Fact]
    public void An_expired_or_revoked_key_is_not_usable()
    {
        var now = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(new ApiKey().IsUsable(now));
        Assert.True(new ApiKey { ExpiresAt = now.AddDays(1) }.IsUsable(now));
        Assert.False(new ApiKey { ExpiresAt = now.AddSeconds(-1) }.IsUsable(now));
        Assert.False(new ApiKey { RevokedAt = now.AddDays(-1), ExpiresAt = now.AddDays(1) }.IsUsable(now));
    }

    // ── The running API ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_api_accepts_a_key_within_its_scopes_and_names_it_in_the_audit_trail()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var slug = "zz-api-key-check-" + tag;
        var created = new List<Guid>();

        async Task<string> NewKey(string name, params string[] scopes)
        {
            var (key, prefix) = ApiKeyFormat.Generate();
            created.Add(await keys.CreateAsync(new ApiKey { UserId = admin.Id, Name = name, Prefix = prefix, KeyHash = ApiKeyFormat.Hash(key), Scopes = ApiKeyFormat.JoinScopes(scopes) }));
            return key;
        }
        HttpClient With(string key)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return client;
        }

        try
        {
            var readOnly = await NewKey("zz read " + tag);
            var writer = await NewKey("zz writer " + tag, "write");
            var body = new { title = "API key check", slug, content = "<p>Draft</p>" };

            // A key signs in where a token would.
            Assert.Equal(HttpStatusCode.OK, (await With(readOnly).GetAsync("/api/v1/users/me")).StatusCode);
            // Read-only: no writes.
            Assert.Equal(HttpStatusCode.Forbidden, (await With(readOnly).PostAsJsonAsync("/api/v1/posts", body)).StatusCode);
            // Write: a draft is created, as the owner, and the trail names the key.
            Assert.Equal(HttpStatusCode.Created, (await With(writer).PostAsJsonAsync("/api/v1/posts", body)).StatusCode);

            await using var conn = new SqlConnection(cs);
            var postId = await conn.ExecuteScalarAsync<Guid>("SELECT Id FROM Posts WHERE Slug = @slug", new { slug });
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @postId", new { postId }));
            var auditName = await conn.ExecuteScalarAsync<string>(
                "SELECT TOP 1 UserName FROM AuditLogs WHERE Action = 'Post.Created' AND EntityId = @id ORDER BY Id DESC", new { id = postId.ToString() });
            Assert.Contains("(API key: zz writer " + tag + ")", auditName);

            // Without the publish scope the owner's publish permission does not come along.
            Assert.Equal(HttpStatusCode.Forbidden, (await With(writer).PatchAsync($"/api/v1/posts/{postId}/publish", null)).StatusCode);
            // A key never edits the account itself.
            Assert.Equal(HttpStatusCode.Forbidden, (await With(writer).PutAsJsonAsync("/api/v1/users/me", new { displayName = "x" })).StatusCode);

            // Revoked: refused at once, with a reason.
            Assert.True(await keys.RevokeAsync(created[1], admin.Id));
            var refused = await With(writer).GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Contains("API key", await refused.Content.ReadAsStringAsync());

            // A well-formed key nobody issued is refused too, and is never tried as a sign-in token.
            Assert.Equal(HttpStatusCode.Unauthorized, (await With(ApiKeyFormat.Generate().Key).GetAsync("/api/v1/users/me")).StatusCode);

            // Only the hash is stored.
            var stored = await conn.QuerySingleAsync<string>("SELECT KeyHash FROM ApiKeys WHERE Id = @id", new { id = created[0] });
            Assert.Equal(ApiKeyFormat.Hash(readOnly), stored);
            Assert.DoesNotContain(readOnly, stored);
        }
        finally
        {
            await using var conn = new SqlConnection(cs);
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Slug = @slug", new { slug });
            if (created.Count > 0) await conn.ExecuteAsync("DELETE FROM ApiKeys WHERE Id IN @ids", new { ids = created });
        }
    }

    [Fact]
    public void The_key_is_shown_once_and_never_put_in_a_cookie_or_redirect()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "ApiKeysController.cs"));
        Assert.Contains("ViewBag.Key = key;", controller);
        Assert.Contains("no-store", controller);
        Assert.DoesNotContain("TempData[\"Key\"]", controller);
        Assert.Contains("AuditActions.ApiKeyCreated", controller);
        Assert.Contains("AuditActions.ApiKeyRevoked", controller);
    }
}
