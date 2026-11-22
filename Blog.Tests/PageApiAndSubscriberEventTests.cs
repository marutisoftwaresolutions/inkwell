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
/// The extension surface that stands in for plugins: pages can be created and changed over the API
/// (unpublished by default, and a drafts-only key cannot alter what readers see), and a webhook can
/// follow the subscriber list.
/// </summary>
public class PageApiAndSubscriberEventTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PageApiAndSubscriberEventTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public void Subscriber_events_can_be_subscribed_to_and_say_they_carry_an_email_address()
    {
        Assert.True(WebhookEvents.IsSubscribable(WebhookEvents.SubscriberConfirmed));
        Assert.True(WebhookEvents.IsSubscribable(WebhookEvents.SubscriberUnsubscribed));
        Assert.False(WebhookEvents.IsSubscribable(WebhookEvents.Test));
        foreach (var e in WebhookEvents.All.Where(e => e.Name.StartsWith("subscriber.")))
            Assert.Contains("email address", e.Description);
        Assert.Equal(WebhookEvents.All.Count, WebhookEvents.All.Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public void Confirming_and_leaving_each_queue_their_event_and_a_repeat_unsubscribe_does_not()
    {
        var src = Source("Blog.Web", "Controllers", "NewsletterController.cs");
        Assert.Contains("SubscriberChangedAsync(HttpContext, member, confirmed: true)", src);
        Assert.Contains("SubscriberChangedAsync(HttpContext, member, confirmed: false)", src);
        var leave = src[src.IndexOf("if (member.Status != \"unsubscribed\")", StringComparison.Ordinal)..];
        Assert.True(leave.IndexOf("UnsubscribeAsync(member.Id)", StringComparison.Ordinal) < leave.IndexOf("confirmed: false", StringComparison.Ordinal));
        // Fail-open: a webhook problem never stops a reader confirming or leaving.
        var publisher = Source("Blog.Web", "Services", "Webhooks", "WebhookPublisher.cs");
        var method = publisher[publisher.IndexOf("SubscriberChangedAsync(", StringComparison.Ordinal)..];
        Assert.Contains("catch (Exception ex)", method[..1200]);
    }

    [Fact]
    public async Task Pages_are_written_over_the_api_unpublished_first_and_a_drafts_only_key_cannot_change_a_live_page()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var slug = "zz-page-api-" + tag;
        var created = new List<Guid>();

        async Task<HttpClient> KeyClient(params string[] scopes)
        {
            var (key, prefix) = ApiKeyFormat.Generate();
            created.Add(await keys.CreateAsync(new ApiKey { UserId = admin.Id, Name = "zz page " + string.Join("+", scopes) + " " + tag, Prefix = prefix, KeyHash = ApiKeyFormat.Hash(key), Scopes = ApiKeyFormat.JoinScopes(scopes) }));
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return client;
        }

        await using var conn = new SqlConnection(cs);
        try
        {
            var reader = await KeyClient();
            var writer = await KeyClient("write");
            var publisher = await KeyClient("write", "publish");
            var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // No key, and a read-only key: no writes.
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/pages", new { title = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/pages", new { title = "x" })).StatusCode);

            // A drafts-only key cannot create a published page.
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/api/v1/pages", new { title = "Page API check", slug, content = "<p>Body</p>", publish = true })).StatusCode);

            // It can create one unpublished; readers do not see it.
            Assert.Equal(HttpStatusCode.Created, (await writer.PostAsJsonAsync("/api/v1/pages", new { title = "Page API check", slug, content = "<p>Body</p>" })).StatusCode);
            var pageId = await conn.ExecuteScalarAsync<Guid>("SELECT Id FROM Pages WHERE Slug = @slug", new { slug });
            Assert.False(await conn.ExecuteScalarAsync<bool>("SELECT IsPublished FROM Pages WHERE Id = @pageId", new { pageId }));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/pages/" + slug)).StatusCode);
            Assert.Contains(slug, await writer.GetStringAsync("/api/v1/pages"));

            // The same slug twice is refused.
            Assert.Equal(HttpStatusCode.Conflict, (await writer.PostAsJsonAsync("/api/v1/pages", new { title = "Again", slug })).StatusCode);

            // Editing the unpublished page is allowed; publishing it is not.
            Assert.Equal(HttpStatusCode.OK, (await writer.PutAsJsonAsync($"/api/v1/pages/{pageId}", new { title = "Page API check, edited" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.PutAsJsonAsync($"/api/v1/pages/{pageId}", new { publish = true })).StatusCode);

            // A key that may publish does; now readers see it and the drafts-only key cannot touch it.
            Assert.Equal(HttpStatusCode.OK, (await publisher.PutAsJsonAsync($"/api/v1/pages/{pageId}", new { publish = true })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/pages/" + slug)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.PutAsJsonAsync($"/api/v1/pages/{pageId}", new { content = "<p>Defaced</p>" })).StatusCode);
            Assert.Equal("<p>Body</p>", await conn.ExecuteScalarAsync<string>("SELECT Content FROM Pages WHERE Id = @pageId", new { pageId }));

            // Audited under the key's name, with a revision for each save.
            Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityId = @id AND UserName LIKE '%(API key: zz page%'", new { id = pageId.ToString() }) >= 3);
            Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Revisions WHERE EntityId = @pageId", new { pageId }) >= 1);
        }
        finally
        {
            var pageId = await conn.ExecuteScalarAsync<Guid?>("SELECT Id FROM Pages WHERE Slug = @slug", new { slug });
            if (pageId is not null) await conn.ExecuteAsync("DELETE FROM Revisions WHERE EntityId = @pageId; DELETE FROM Pages WHERE Id = @pageId;", new { pageId });
            if (created.Count > 0) await conn.ExecuteAsync("DELETE FROM ApiKeys WHERE Id IN @ids", new { ids = created });
        }
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }
}
