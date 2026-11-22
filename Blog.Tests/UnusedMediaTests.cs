using Blog.Core.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The unused-files report lists a file only when nothing on the site points at it. Being wrong in
/// the other direction would invite an operator to delete a logo or a picture that is in use, so each
/// place an address can be stored is checked here.
/// </summary>
public class UnusedMediaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public UnusedMediaTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Only_a_file_nothing_points_at_is_reported()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var owner = Guid.Empty;       // set below to the admin: Media.UploadedBy must be a real account
        var admin = await conn.ExecuteScalarAsync<Guid?>("SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY CreatedAt");
        if (admin is null) return;
        owner = admin.Value;

        var tag = Guid.NewGuid().ToString("N")[..8];
        string Url(string name) => $"/uploads/images/2026-11/zz_{tag}_{name}.webp";   // underscores on purpose: they are LIKE wildcards
        var files = new[] { "orphan", "in-body", "feature", "page-body", "theme", "avatar", "near-miss" }.ToDictionary(n => n, _ => Guid.NewGuid());
        var postId = Guid.NewGuid(); var pageId = Guid.NewGuid(); var themeId = Guid.NewGuid();
        var slug = "zz-unused-" + tag;
        string? originalAvatar = null;
        try
        {
            foreach (var (name, id) in files)
                await conn.ExecuteAsync(@"INSERT INTO Media (Id, FileName, OriginalFileName, FilePath, Url, ContentType, FileSize, UploadedBy, CreatedAt)
                                          VALUES (@id, @file, @file, @url, @url, N'image/webp', 1000, @owner, SYSUTCDATETIME())",
                    new { id, file = name + ".webp", url = Url(name), owner });

            // "near-miss" differs from "orphan" only where an unescaped underscore would match any character.
            await conn.ExecuteAsync(@"INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, FeatureImage, CreatedAt, UpdatedAt)
                                      VALUES (@postId, N'Unused check', @slug, @html, N'x', N'Draft', N'public', N'post', @admin, @feature, SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { postId, slug, admin, feature = Url("feature"), html = $"<p><img src=\"{Url("in-body")}\" alt=\"\"></p><p>{Url("near-miss").Replace("zz_", "zzX")}</p>" });
            await conn.ExecuteAsync(@"INSERT INTO Pages (Id, Title, Slug, Content, AuthorId, IsPublished, IsInNav, SortOrder, CreatedAt, UpdatedAt)
                                      VALUES (@pageId, N'Unused check', @slug, @content, @admin, 0, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { pageId, slug = slug + "-page", admin, content = $"<img src=\"{Url("page-body")}\">" });
            await conn.ExecuteAsync(@"INSERT INTO CustomThemeSettings (Id, UserId, SettingGroup, SettingKey, SettingType, SettingValue, DefaultValue, Label, Description)
                                      VALUES (@themeId, @admin, N'site', @key, N'text', @value, N'', N'Unused check', N'')",
                new { themeId, admin, key = "zz-unused-" + tag, value = Url("theme") });
            originalAvatar = await conn.ExecuteScalarAsync<string?>("SELECT AvatarUrl FROM Users WHERE Id = @admin", new { admin });
            await conn.ExecuteAsync("UPDATE Users SET AvatarUrl = @url WHERE Id = @admin", new { admin, url = Url("avatar") });

            var unused = (await media.GetUnusedAsync(owner, 5000)).Select(m => m.Id).ToHashSet();

            Assert.Contains(files["orphan"], unused);
            Assert.Contains(files["near-miss"], unused);      // a look-alike address elsewhere does not count as use
            foreach (var used in new[] { "in-body", "feature", "page-body", "theme", "avatar" })
                Assert.False(unused.Contains(files[used]), $"\"{used}\" is in use but was reported as unused.");
        }
        finally
        {
            if (originalAvatar is not null || files.Count > 0)
                await conn.ExecuteAsync("UPDATE Users SET AvatarUrl = @a WHERE Id = @admin AND AvatarUrl LIKE @mine", new { a = originalAvatar, admin, mine = $"%zz[_]{tag}[_]%" });
            await conn.ExecuteAsync("DELETE FROM CustomThemeSettings WHERE Id = @themeId; DELETE FROM Pages WHERE Id = @pageId; DELETE FROM Posts WHERE Id = @postId; DELETE FROM Media WHERE Id IN @ids;",
                new { themeId, pageId, postId, ids = files.Values.ToArray() });
        }
    }

    [Fact]
    public void The_report_says_what_it_cannot_see_and_deletes_only_through_the_librarys_own_action()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var view = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Media", "Unused.cshtml"));
        Assert.Contains("cannot see a link from another website", view);
        Assert.Contains("action=\"/admin/media/delete/@item.Id\"", view);
        Assert.Contains("data-confirm-action=\"Delete file\"", view);
        var controller = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "MediaController.cs"));
        Assert.Contains("[HttpGet(\"unused\")]", controller);
    }
}
