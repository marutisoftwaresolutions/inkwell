using Blog.Core.Interfaces;
using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// <see cref="IExportPushRepository"/> against the dev database (ADR-0029): the SQL the in-memory fakes in
/// <c>ExportPushTests</c> stand in for. Writes one throwaway push and deletes it; skips without the database.
/// </summary>
public class ExportPushRepositoryTests
{
    private readonly ITestOutputHelper _out;
    public ExportPushRepositoryTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task A_push_round_trips_through_the_database()
    {
        using var factory = new WebApplicationFactory<Program>();
        string cs;
        try
        {
            cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExportPushRepository>();
        var now = DateTime.UtcNow;
        var push = new ExportPush
        {
            Id = Guid.NewGuid(), Target = ExportTarget.Ghost, SiteUrl = "https://repo-test.example", ConflictPolicy = PushConflictPolicy.Update,
            IncludeDrafts = true, IncludePages = false, SourceBaseUrl = "https://source.example", Status = PushStatus.Running,
            StartedBy = Guid.NewGuid(), StartedByName = "repo test", StartedAt = now, UpdatedAt = now,
        };
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        try
        {
            await repo.CreateAsync(push, new[]
            {
                new NewPushItem(PushItemKind.Post, b.ToString(), "Second — “quoted”", 2),
                new NewPushItem(PushItemKind.Page, a.ToString(), "First", 1),
            });

            var loaded = await repo.GetAsync(push.Id);
            Assert.NotNull(loaded);
            Assert.Equal(ExportTarget.Ghost, loaded!.Target);
            Assert.Equal(PushConflictPolicy.Update, loaded.ConflictPolicy);
            Assert.Equal(PushStatus.Running, loaded.Status);
            Assert.Equal(2, loaded.TotalCount);
            Assert.True(loaded.IncludeDrafts);
            Assert.False(loaded.IncludePages);
            Assert.Equal(push.Id, (await repo.GetActiveAsync())?.Id);

            var due = await repo.GetDueAsync(push.Id, DateTime.UtcNow.AddSeconds(1), 10);
            Assert.Equal(new[] { a.ToString(), b.ToString() }, due.Select(d => d.SourceKey));   // send order
            Assert.Equal("Second — “quoted”", due[1].Title);                                   // UTF-8 kept

            var image = await repo.GetOrAddAsync(push.Id, PushItemKind.Media, "/uploads/2026/10/x.webp", null);
            var again = await repo.GetOrAddAsync(push.Id, PushItemKind.Media, "/uploads/2026/10/x.webp", null);
            Assert.Equal(image.Id, again.Id);
            Assert.Equal(PushItemStatus.Pending, image.Status);
            Assert.Empty((await repo.GetDueAsync(push.Id, DateTime.UtcNow.AddSeconds(1), 10)).Where(i => i.Kind == PushItemKind.Media));

            await repo.MarkItemAsync(due[0].Id, PushItemStatus.Created, 1, DateTime.UtcNow, "42", "https://repo-test.example/first/", null, DateTime.UtcNow);
            await repo.MarkItemAsync(due[1].Id, PushItemStatus.Pending, 1, DateTime.UtcNow.AddMinutes(5), null, null, new string('e', 900), DateTime.UtcNow);
            var counts = await repo.CountAsync(push.Id);
            Assert.Equal((1, 1, 2), (counts.Created, counts.Pending, counts.Total));             // the image is not counted
            Assert.Equal("42", (await repo.FindAsync(push.Id, PushItemKind.Page, a.ToString()))!.RemoteId);
            Assert.Empty(await repo.GetDueAsync(push.Id, DateTime.UtcNow, 10));                 // the retry is not due yet
            Assert.True((await repo.ListContentItemsAsync(push.Id, PushItemStatus.Pending))[0].Error!.Length <= 500);

            await repo.SetStatusAsync(push.Id, PushStatus.Paused, "paused", DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow, null);
            Assert.DoesNotContain(await repo.GetRunningAsync(), p => p.Id == push.Id);
            Assert.Equal(1, await repo.CancelPendingAsync(push.Id, DateTime.UtcNow));
            Assert.Equal(1, (await repo.CountAsync(push.Id)).Cancelled);
            Assert.Single(await repo.ListContentItemsAsync(push.Id, PushItemStatus.Created));
            Assert.Contains(await repo.ListRecentAsync(50), p => p.Id == push.Id);
        }
        finally
        {
            using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("DELETE FROM ExportPushes WHERE Id = @id", conn);   // items cascade
            cmd.Parameters.AddWithValue("@id", push.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
