using System.Net;
using System.Net.Http.Headers;
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
/// The subscriber list over the API is personal data: an Admin only, a key only with the subscribers
/// scope, no tokens in the answer, bounded pages, and every read in the audit trail.
/// </summary>
public class SubscribersApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SubscribersApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void The_subscribers_scope_carries_no_desk_permission_and_is_not_implied_by_the_others()
    {
        var admin = new[] { "posts.edit", "posts.publish", "posts.delete", "pages.manage", "comments.manage", "settings.manage" };
        Assert.Empty(ApiKeyFormat.AllowedPermissions(new[] { ApiKeyFormat.Subscribers }, admin));
        Assert.DoesNotContain(ApiKeyFormat.Subscribers, ApiKeyFormat.NormalizeScopes(new[] { "write", "publish", "comments" }));
        Assert.Equal("read,subscribers", ApiKeyFormat.JoinScopes(new[] { "subscribers" }));
    }

    [Fact]
    public void The_endpoint_is_admin_only_and_the_desk_offers_the_scope_to_admins_only()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var api = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "Api", "SubscribersApiController.cs"));
        Assert.Contains("Policy = \"AdminOnly\")]", api);
        Assert.Contains("AuditActions.SubscriberExported", api);
        Assert.DoesNotContain("Token", api.Replace("confirm and\n/// unsubscribe tokens", ""));
        var keys = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "ApiKeysController.cs"));
        Assert.Contains("s.Scope == ApiKeyFormat.Subscribers ? User.IsInRole(\"Admin\")", keys);
    }

    [Fact]
    public async Task A_key_reads_subscribers_only_with_the_scope_and_never_receives_a_token()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        var tag = Guid.NewGuid().ToString("N")[..10];
        var email = $"zz-sub-{tag}@example.invalid";
        var confirmToken = "zzconfirm" + tag;
        var created = new List<Guid>();

        async Task<HttpClient> KeyClient(params string[] scopes)
        {
            var (key, prefix) = ApiKeyFormat.Generate();
            created.Add(await keys.CreateAsync(new ApiKey { UserId = admin.Id, Name = "zz subs " + tag, Prefix = prefix, KeyHash = ApiKeyFormat.Hash(key), Scopes = ApiKeyFormat.JoinScopes(scopes) }));
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return client;
        }

        await using var conn = new SqlConnection(cs);
        try
        {
            await conn.ExecuteAsync(@"INSERT INTO Members (Id, Uuid, Email, Name, Status, Subscribed, ConfirmToken, UnsubscribeToken, ConfirmedAt, CreatedAt)
                                      VALUES (NEWID(), NEWID(), @email, N'Zed', N'confirmed', 1, @confirmToken, @unsub, SYSUTCDATETIME(), DATEADD(DAY, 1, SYSUTCDATETIME()))",
                new { email, confirmToken, unsub = "zzunsub" + tag });

            var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/subscribers")).StatusCode);

            // An Admin's key with every other scope still does not reach the list.
            var others = await KeyClient("write", "publish", "comments");
            Assert.Equal(HttpStatusCode.Forbidden, (await others.GetAsync("/api/v1/subscribers")).StatusCode);

            var allowed = await KeyClient("subscribers");
            var response = await allowed.GetAsync("/api/v1/subscribers?pageSize=5000");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(email, body);                              // newest first: the row dated tomorrow is on page 1
            Assert.DoesNotContain(confirmToken, body);
            Assert.DoesNotContain("zzunsub" + tag, body);
            Assert.DoesNotContain("oken\"", body);                     // no token field of any name
            Assert.Contains("\"pageSize\":" + Blog.Web.Controllers.Api.SubscribersApiController.MaxPageSize, body);

            Assert.DoesNotContain(email, await allowed.GetStringAsync("/api/v1/subscribers?status=unsubscribed"));
            Assert.Equal(HttpStatusCode.BadRequest, (await allowed.GetAsync("/api/v1/subscribers?status=everyone")).StatusCode);

            var audited = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Subscriber.Exported' AND UserName LIKE @who AND EntityName LIKE N'%read through the API%'",
                new { who = "%zz subs " + tag + "%" });
            Assert.Equal(2, audited);                                  // the two successful reads, not the refusals
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Members WHERE Email = @email", new { email });
            if (created.Count > 0) await conn.ExecuteAsync("DELETE FROM ApiKeys WHERE Id IN @ids", new { ids = created });
        }
    }
}
