using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

/// <summary>Mints API tokens the way <c>ApiTokenService</c> does, for tests that need a caller.</summary>
internal static class TestApiTokens
{
    public static string For(IServiceProvider services, Guid userId, string? key = null, DateTime? expires = null)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            notBefore: (expires ?? DateTime.UtcNow.AddMinutes(5)).AddMinutes(-10),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>A real Desk sign-in cookie, protected with the app's own key ring.</summary>
    public static string DeskCookie(IServiceProvider services, Guid userId)
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Admin"),
            new Claim("Permission", "posts.edit"), new Claim("Permission", "posts.publish"),
        }, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new ClaimsPrincipal(identity),
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        return (options.Cookie.Name ?? ".AspNetCore.Cookies") + "=" + options.TicketDataFormat.Protect(ticket);
    }
}

/// <summary>
/// The API's authentication and authorisation (ADR-0033): bearer tokens are accepted, forged and stale ones are
/// refused, writes follow the Desk's permissions and ownership rules and are audited, and the API is switched off
/// while <c>Jwt:Key</c> is a placeholder.
/// </summary>
public class ApiSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public ApiSecurityTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("REPLACE_WITH_A_RANDOM_64_CHAR_SECRET")]
    [InlineData("short-but-random-9f3k")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("CHANGE-ME-CHANGE-ME-CHANGE-ME-CHANGE-ME-1234")]
    public void Unusable_signing_keys_switch_the_api_off(string? key)
    {
        Assert.False(ApiSigningKey.IsUsable(key));
        Assert.Contains("Jwt:Key", ApiSigningKey.Problem(key));
    }

    [Fact]
    public void A_random_key_is_usable() =>
        Assert.True(ApiSigningKey.IsUsable(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))));

    [Fact]
    public void Every_api_write_names_a_desk_policy_and_is_audited()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        foreach (var file in new[] { "PostsApiController.cs", "MiscApiController.cs" })
        {
            var src = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "Api", file));
            var writes = System.Text.RegularExpressions.Regex.Matches(src,
                @"\[Http(Post|Put|Patch|Delete)[^\]]*\]\s*(\[[^\]]+\]\s*)*public async Task<IActionResult> (\w+)\((?<body>[\s\S]*?)\n    \}");
            foreach (System.Text.RegularExpressions.Match m in writes)
            {
                var name = m.Groups[3].Value;
                if (name == "UpdateMe") { Assert.Contains("AuditActions.UserProfileUpdated", m.Value); continue; }
                Assert.Matches(@"Policy = ""Can\w+""", m.Value);
                Assert.Contains("_audit.LogAsync(AuditActions.", m.Value);
            }
            Assert.DoesNotContain("[HttpPost(\"comments\")]", src);
        }
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Program.cs"));
        Assert.Contains(".AddJwtBearer(JwtBearerDefaults.AuthenticationScheme", program);
    }

    // ── End to end (dev database) ────────────────────────────────────────────

    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private HttpClient Client(string? token)
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Tokens_are_checked_and_writes_follow_the_desk_rules()
    {
        string? cs;
        try
        {
            cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        await using var conn = new SqlConnection(cs);
        var otherPost = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Title)>(
            "SELECT TOP 1 Id, Title FROM Posts WHERE Status = 'Published' ORDER BY NEWID()");
        var contributorId = Guid.NewGuid();
        var email = $"api-contributor-{contributorId:N}@example.invalid";
        Guid ownPost = Guid.Empty;

        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Users (Id, Email, Username, DisplayName, PasswordHash, Role, IsActive, UpdatedAt)
                                      VALUES (@contributorId, @email, @email, N'API contributor check', N'x', 'Contributor', 1, SYSUTCDATETIME());
                                      INSERT INTO RolesUsers (RoleId, UserId) SELECT Id, @contributorId FROM Roles WHERE Name = 'Contributor';",
                new { contributorId, email });
            var contributor = TestApiTokens.For(_factory.Services, contributorId);

            // Before ADR-0033 these were 500s: the scheme was never registered.
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PutAsync($"/api/v1/posts/{Guid.NewGuid()}", Body(new { title = "x" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(TestApiTokens.For(_factory.Services, contributorId, "REPLACE_WITH_A_RANDOM_64_CHAR_SECRET"))
                .GetAsync("/api/v1/users/me")).StatusCode);                                                      // forged with the example key
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(TestApiTokens.For(_factory.Services, Guid.NewGuid())).GetAsync("/api/v1/users/me")).StatusCode);   // no such user
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(TestApiTokens.For(_factory.Services, contributorId, expires: DateTime.UtcNow.AddMinutes(-5)))
                .GetAsync("/api/v1/users/me")).StatusCode);                                                      // expired
            Assert.Equal(HttpStatusCode.OK, (await Client(contributor).GetAsync("/api/v1/users/me")).StatusCode);

            // A contributor can draft and delete their own post, audited as themselves.
            var slug = "zz-api-contributor-" + contributorId.ToString("N")[..8];
            Assert.Equal(HttpStatusCode.Created, (await Client(contributor).PostAsync("/api/v1/posts", Body(new { title = "API contributor check", slug, content = "<p>Draft</p>" }))).StatusCode);
            ownPost = await conn.ExecuteScalarAsync<Guid>("SELECT Id FROM Posts WHERE Slug = @slug", new { slug });
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @ownPost", new { ownPost }));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Post.Created' AND EntityId = @id AND UserId = @contributorId",
                new { id = ownPost.ToString(), contributorId }));

            // ...but cannot publish it, touch someone else's post, or moderate comments.
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(contributor).PatchAsync($"/api/v1/posts/{ownPost}/publish", null)).StatusCode);
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @ownPost", new { ownPost }));
            if (otherPost.Id != Guid.Empty)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await Client(contributor).PutAsync($"/api/v1/posts/{otherPost.Id}", Body(new { title = "Hijacked" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await Client(contributor).DeleteAsync($"/api/v1/posts/{otherPost.Id}")).StatusCode);
                Assert.Equal(otherPost.Title, await conn.ExecuteScalarAsync<string>("SELECT Title FROM Posts WHERE Id = @id", new { id = otherPost.Id }));
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(contributor).PatchAsync($"/api/v1/comments/{Guid.NewGuid()}/approve", null)).StatusCode);

            // Once their own post is scheduled or live (an editor's decision), changing it needs the publish permission.
            await conn.ExecuteAsync("UPDATE Posts SET Status = 'Scheduled', ScheduledAt = DATEADD(DAY, 1, SYSUTCDATETIME()) WHERE Id = @ownPost", new { ownPost });
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(contributor).PutAsync($"/api/v1/posts/{ownPost}", Body(new { title = "Changed live" }))).StatusCode);
            await conn.ExecuteAsync("UPDATE Posts SET Status = 'Draft' WHERE Id = @ownPost", new { ownPost });

            Assert.Equal(HttpStatusCode.OK, (await Client(contributor).DeleteAsync($"/api/v1/posts/{ownPost}")).StatusCode);
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Posts WHERE Id = @ownPost", new { ownPost }));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Post.Deleted' AND EntityId = @id", new { id = ownPost.ToString() }));
            ownPost = Guid.Empty;

            // A disabled account's token stops working at once, not when it expires.
            await conn.ExecuteAsync("UPDATE Users SET IsActive = 0 WHERE Id = @contributorId", new { contributorId });
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(contributor).GetAsync("/api/v1/users/me")).StatusCode);

            // The anonymous comment endpoint that skipped the spam gate is gone.
            var comment = await Client(null).PostAsync("/api/v1/comments", Body(new { postId = Guid.NewGuid(), authorName = "x", authorEmail = "x@example.invalid", content = "x" }));
            Assert.True(comment.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, comment.StatusCode.ToString());
        }
        finally
        {
            if (ownPost != Guid.Empty)
                await conn.ExecuteAsync("DELETE FROM PostCategories WHERE PostId = @ownPost; DELETE FROM PostTags WHERE PostId = @ownPost; DELETE FROM Revisions WHERE EntityId = @ownPost; DELETE FROM Posts WHERE Id = @ownPost", new { ownPost });
            await conn.ExecuteAsync("DELETE FROM RolesUsers WHERE UserId = @contributorId; DELETE FROM Users WHERE Id = @contributorId", new { contributorId });
        }
    }

    [Fact]
    public async Task Reads_keep_private_data_private_and_writes_cannot_ride_a_desk_cookie()
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
        var admin = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!;
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var settings = await settingsRepo.GetSettingsAsync(admin.Id);
        var originalFields = settings.CustomFields.ToList();
        await using var conn = new SqlConnection(cs);
        var contributorId = Guid.NewGuid();
        var email = $"api-reader-{contributorId:N}@example.invalid";
        var postId = Guid.NewGuid();
        var slug = "zz-api-private-" + postId.ToString("N")[..8];

        try
        {
            settings.CustomFields = originalFields.Append(new Blog.Core.Domain.CustomFieldDefinition
                { Key = "zz-api-secret", Label = "Secret", Type = Blog.Core.Domain.CustomFieldTypes.Text, AppliesTo = "posts", ShowOnPage = false }).ToList();
            await settingsRepo.SaveSettingsAsync(admin.Id, settings);
            await conn.ExecuteAsync(@"INSERT INTO Users (Id, Email, Username, DisplayName, PasswordHash, Role, IsActive, UpdatedAt)
                                      VALUES (@contributorId, @email, @email, N'API reader check', N'x', 'Contributor', 1, SYSUTCDATETIME());
                                      INSERT INTO RolesUsers (RoleId, UserId) SELECT Id, @contributorId FROM Roles WHERE Name = 'Contributor';",
                new { contributorId, email });
            // An admin's live post (scheduled and due, so other tests picking a random published post never see it),
            // with an internal field and a FAQ whose answer is empty: structured data that must block publishing.
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, AuthorId, Status, ScheduledAt, PublishedAt, CustomFieldsJson, ReviewNote, CreatedAt, UpdatedAt)
                                      VALUES (@postId, N'API privacy check', @slug, N'<p>x</p>', N'x', @adminId, 'Scheduled', DATEADD(MINUTE, -1, SYSUTCDATETIME()), SYSUTCDATETIME(),
                                              N'{""zz-api-secret"":""internal only""}', N'editor-only note', SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { postId, slug, adminId = admin.Id });

            // A reader-level view: no internal field, no editorial fields, for anonymous and for a contributor alike.
            foreach (var token in new[] { null, TestApiTokens.For(_factory.Services, contributorId) })
            {
                var body = await (await Client(token).GetAsync($"/api/v1/posts/{slug}")).Content.ReadAsStringAsync();
                var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
                Assert.False(data.GetProperty("customFields").TryGetProperty("zz-api-secret", out _));
                foreach (var hidden in new[] { "customFieldsJson", "reviewNote", "markdown", "status", "uuid", "nextReviewAt" })
                    Assert.False(data.TryGetProperty(hidden, out _), hidden);
            }
            // The post's editor sees the internal field.
            var adminView = JsonDocument.Parse(await (await Client(TestApiTokens.For(_factory.Services, admin.Id)).GetAsync($"/api/v1/posts/{slug}")).Content.ReadAsStringAsync());
            Assert.Equal("internal only", adminView.RootElement.GetProperty("data").GetProperty("customFields").GetProperty("zz-api-secret").GetString());

            // Comments come back without the commenter's email or address.
            var comments = await (await Client(TestApiTokens.For(_factory.Services, contributorId)).GetAsync("/api/v1/comments")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("authorEmail", comments);
            Assert.DoesNotContain("authorIp", comments);

            // A Desk cookie alone cannot write through the API (no CSRF path), and the refusal is JSON, not the HTML error page.
            var withCookie = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using (var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/posts/{postId}") { Content = Body(new { title = "Via cookie" }) })
            {
                put.Headers.Add("Cookie", TestApiTokens.DeskCookie(_factory.Services, admin.Id));
                var res = await withCookie.SendAsync(put);
                Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
                Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
            }
            var forbidden = await Client(TestApiTokens.For(_factory.Services, contributorId)).PatchAsync($"/api/v1/comments/{Guid.NewGuid()}/approve", null);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Contains("permission", await forbidden.Content.ReadAsStringAsync());
            Assert.Equal("API privacy check", await conn.ExecuteScalarAsync<string>("SELECT Title FROM Posts WHERE Id = @postId", new { postId }));

            // Publishing through the API runs the Desk's structured-data gate.
            await conn.ExecuteAsync("UPDATE Posts SET Status = 'Draft', FaqJson = N'[{\"Question\":\"Why?\",\"Answer\":\"\"}]' WHERE Id = @postId", new { postId });
            var publish = await Client(TestApiTokens.For(_factory.Services, admin.Id)).PatchAsync($"/api/v1/posts/{postId}/publish", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, publish.StatusCode);
            Assert.Contains("FAQ", await publish.Content.ReadAsStringAsync());
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @postId", new { postId }));
        }
        finally
        {
            settings.CustomFields = originalFields;
            await settingsRepo.SaveSettingsAsync(admin.Id, settings);
            await conn.ExecuteAsync("DELETE FROM Revisions WHERE EntityId = @postId; DELETE FROM Posts WHERE Id = @postId; DELETE FROM RolesUsers WHERE UserId = @contributorId; DELETE FROM Users WHERE Id = @contributorId",
                new { postId, contributorId });
        }
    }

    [Fact]
    public async Task Registration_is_closed_once_the_site_has_an_account()
    {
        if (!await _factory.Services.CreateScope().ServiceProvider.GetRequiredService<IUserRepository>().AnyUsersExistAsync()) return;
        var client = Client(null);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/register")).StatusCode);

        // A valid anti-forgery token (from the sign-in form) so the POST reaches the action itself.
        var login = await client.GetStringAsync("/account/login");
        Assert.DoesNotContain("/account/register", login);
        var token = System.Text.RegularExpressions.Regex.Match(login, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        if (token.Length == 0) token = System.Text.RegularExpressions.Regex.Match(login, "__RequestVerificationToken[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var email = $"intruder-{Guid.NewGuid():N}@example.invalid";
        var post = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["email"] = email, ["password"] = "Aa123456x", ["confirmPassword"] = "Aa123456x", ["name"] = "Intruder" }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Null(await _factory.Services.CreateScope().ServiceProvider.GetRequiredService<IUserRepository>().GetByEmailAsync(email));
    }

    [Fact]
    public void Api_reads_return_explicit_shapes_never_a_domain_object()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        foreach (var file in Directory.GetFiles(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "Api"), "*.cs"))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotMatch(@"data = (post|page|comment|user|result\.Items)\b\s*[,}]", src);
            Assert.DoesNotContain("SerializeToNode(post", src);
        }
    }

    [Fact]
    public async Task With_the_example_key_the_api_refuses_to_sign_anyone_in()
    {
        using var placeholder = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "REPLACE_WITH_A_RANDOM_64_CHAR_SECRET" })));
        var res = await placeholder.CreateClient().PostAsync("/api/v1/auth/login", Body(new { email = "nobody@example.invalid", password = "x" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("Jwt:Key", await res.Content.ReadAsStringAsync());

        // A token signed with that well-known key is refused too.
        var forged = TestApiTokens.For(placeholder.Services, Guid.NewGuid(), "REPLACE_WITH_A_RANDOM_64_CHAR_SECRET");
        var c = placeholder.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/users/me")).StatusCode);
    }
}
