using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Blog.Infrastructure.Data;
using Blog.Web.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// What the 2026-11-13 architecture and security reviews found, each pinned by the test that would
/// have caught it: oversized pictures, hostile pages and drawings that buy unbounded work, a role
/// seeding that could not finish itself, a write-only API key that could delete, addresses that hide
/// a private one, and several places that said one thing and did another.
/// </summary>
public class ReviewHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ReviewHardeningTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    // ── Pictures ─────────────────────────────────────────────────────────

    /// <summary>A PNG that is a few dozen bytes long and declares any size: signature and an IHDR chunk with a valid checksum.</summary>
    private static byte[] PngHeader(int width, int height)
    {
        static uint Crc(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
        var ihdr = new byte[17];
        Encoding.ASCII.GetBytes("IHDR").CopyTo(ihdr, 0);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(8), height);
        ihdr[12] = 8; ihdr[13] = 2;                                   // 8 bits, RGB
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, 13); ms.Write(four);
        ms.Write(ihdr);
        BinaryPrimitives.WriteUInt32BigEndian(four, Crc(ihdr)); ms.Write(four);
        return ms.ToArray();
    }

    [Fact]
    public void A_picture_is_judged_by_its_pixels_not_by_its_file_size()
    {
        Assert.True(ImageGuard.Allows(6000, 4000, 1));
        Assert.True(ImageGuard.Allows(800, 600, 60));
        Assert.False(ImageGuard.Allows(30000, 30000, 1));
        Assert.False(ImageGuard.Allows(800, 600, ImageGuard.MaxFrames + 1));
        Assert.False(ImageGuard.Allows(0, 600, 1));
        Assert.False(ImageGuard.Allows(int.MaxValue, int.MaxValue, 1));          // no overflow into "small"

        var bomb = PngHeader(30000, 30000);
        Assert.True(bomb.Length < 100);
        Assert.Throws<ImageTooLargeException>(() => ImageGuard.Check(bomb));
        ImageGuard.Check(PngHeader(800, 600));                                   // an ordinary size passes
        ImageGuard.Check("not a picture at all"u8.ToArray());                    // not a picture: the decoder's business, not this guard's
    }

    [Fact]
    public void Every_place_that_decodes_an_uploaded_picture_asks_the_guard_first()
    {
        var processing = Read("Blog.Web", "Services", "ImageProcessingService.cs");
        Assert.True(processing.IndexOf("ImageGuard.Check(bytes)", StringComparison.Ordinal) < processing.IndexOf("Image.Load(bytes)", StringComparison.Ordinal));
        var variants = Read("Blog.Web", "Services", "ResponsiveImageStore.cs");
        Assert.True(variants.IndexOf("ImageGuard.Check(file)", StringComparison.Ordinal) < variants.IndexOf("Image.LoadAsync(file", StringComparison.Ordinal));
        var media = Read("Blog.Web", "Controllers", "MediaController.cs");
        Assert.Equal(2, media.Split("ImageGuard.Check(").Length - 1);            // crop and replace
        Assert.DoesNotContain("Image.LoadAsync(file.OpenReadStream())", media);  // never straight from the request
    }

    // ── Replace: says what happened ──────────────────────────────────────────

    [Fact]
    public void A_replace_is_audited_once_the_file_has_changed_and_never_shares_a_temp_name()
    {
        var media = Read("Blog.Web", "Controllers", "MediaController.cs");
        var replace = media[media.IndexOf("public async Task<IActionResult> Replace(", StringComparison.Ordinal)..];
        replace = replace[..replace.IndexOf("[HttpGet(\"api\")]", StringComparison.Ordinal)];
        Assert.Contains("Guid.NewGuid().ToString(\"N\")[..8] + \".replacing\"", replace);
        var moved = replace.IndexOf("moved = true;", StringComparison.Ordinal);
        var audit = replace.IndexOf("AuditActions.MediaUpdated", StringComparison.Ordinal);
        var variants = replace.IndexOf("EnsureVariantsAsync", StringComparison.Ordinal);
        Assert.True(moved > 0 && moved < audit && audit < variants);              // audited before the tidying that may fail
        Assert.Contains("if (moved) TempData[\"Warning\"]", replace);             // and never "the old one is still in place" after the move
    }

    // ── Hostile pages and drawings ───────────────────────────────────────────

    [Theory]
    [InlineData("meta-dashes")]
    [InlineData("unclosed-meta")]
    [InlineData("unclosed-title")]
    [InlineData("angle-brackets")]
    public void A_page_built_to_stall_the_parser_still_answers_quickly(string kind)
    {
        var html = kind switch
        {
            "meta-dashes" => "<meta " + string.Concat(Enumerable.Repeat("a-", 200_000)) + ">",
            "unclosed-meta" => string.Concat(Enumerable.Repeat("<meta ", 70_000)),
            "unclosed-title" => string.Concat(Enumerable.Repeat("<title>", 60_000)),
            _ => "<meta property=\"og:title\" content=\"" + new string('<', 300_000) + "\">",
        };
        var watch = Stopwatch.StartNew();
        var details = LinkPreview.Parse(html, new Uri("https://example.org/x"));
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{kind} took {watch.Elapsed.TotalSeconds:0.0}s");
        Assert.False(string.IsNullOrEmpty(details.Title));                        // the link still gets a card
        Assert.DoesNotContain("<", details.Title);
    }

    [Fact]
    public void Only_the_head_of_a_page_is_searched()
    {
        var page = "<html><head><title>Real title</title></head><body>" + new string('x', 100_000) + "<meta property=\"og:title\" content=\"From the body\"></body></html>";
        Assert.Equal("Real title", LinkPreview.Parse(page, new Uri("https://example.org/")).Title);
    }

    [Fact]
    public void A_drawing_nested_thousands_deep_or_megabytes_long_is_refused_quickly()
    {
        var deep = "<svg xmlns=\"http://www.w3.org/2000/svg\">" + string.Concat(Enumerable.Repeat("<g>", 50_000)) + string.Concat(Enumerable.Repeat("</g>", 50_000)) + "</svg>";
        var watch = Stopwatch.StartNew();
        Assert.Null(SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(deep)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));

        var huge = "<svg xmlns=\"http://www.w3.org/2000/svg\"><desc>" + new string('a', SvgSanitizer.MaxBytes) + "</desc></svg>";
        Assert.Null(SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(huge)));

        var ordinary = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><g><g><circle cx=\"5\" cy=\"5\" r=\"4\"/></g></g></svg>";
        Assert.NotNull(SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(ordinary)));
    }

    [Fact]
    public void The_whole_fetch_has_one_deadline_including_the_body()
    {
        var fetcher = Read("Blog.Web", "Services", "LinkPreviewFetcher.cs");
        Assert.Contains("deadline.CancelAfter(Timeout);", fetcher);
        Assert.Contains("ct = deadline.Token;", fetcher);
        Assert.True(fetcher.IndexOf("deadline.CancelAfter", StringComparison.Ordinal) < fetcher.IndexOf("for (var hop", StringComparison.Ordinal));
    }

    // ── Addresses ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("64:ff9b::a00:1", true)]          // NAT64 wrapping 10.0.0.1
    [InlineData("2002:c0a8:101::1", true)]        // 6to4 wrapping 192.168.1.1
    [InlineData("2001:0:4136:e378::1", true)]     // Teredo
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("2606:4700:4700::1111", false)]   // an ordinary public address
    [InlineData("2001:4860:4860::8888", false)]   // 2001:4860 is not Teredo's 2001:0
    [InlineData("93.184.216.34", false)]
    public void An_address_that_carries_a_private_one_inside_is_refused(string address, bool forbidden) =>
        Assert.Equal(forbidden, WebhookRules.IsForbiddenAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task The_api_description_never_names_a_host_the_caller_made_up()
    {
        if (!TestDatabase.Available(_factory)) return;   // tenant resolution needs the database: skip without one
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/openapi.json");
        request.Headers.Host = "evil.example";
        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("evil.example", body);
    }

    // ── Roles ──────────────────────────────────────────────────────────�

    private sealed class FakeRoles : IRoleRepository
    {
        public readonly List<Role> Roles = [];
        public readonly List<Permission> Permissions = [];
        public readonly HashSet<(Guid Permission, Guid Role)> Grants = [];
        public Task<List<Role>> GetAllRolesAsync() => Task.FromResult(Roles.ToList());
        public Task<List<Permission>> GetAllPermissionsAsync() => Task.FromResult(Permissions.ToList());
        public Task<List<Permission>> GetPermissionsForRoleAsync(Guid roleId) =>
            Task.FromResult(Permissions.Where(p => Grants.Contains((p.Id, roleId))).ToList());
        public Task<Guid> CreateRoleAsync(Role role) { role.Id = Guid.NewGuid(); Roles.Add(role); return Task.FromResult(role.Id); }
        public Task<Guid> CreatePermissionAsync(Permission permission) { permission.Id = Guid.NewGuid(); Permissions.Add(permission); return Task.FromResult(permission.Id); }
        public Task AssignPermissionToRoleAsync(Guid permissionId, Guid roleId) { Grants.Add((permissionId, roleId)); return Task.CompletedTask; }
        public Task<Role?> GetByIdAsync(Guid id) => Task.FromResult(Roles.FirstOrDefault(r => r.Id == id));
        public Task<Role?> GetByNameAsync(string name) => Task.FromResult(Roles.FirstOrDefault(r => r.Name == name));
        public Task<Role?> GetRoleForUserAsync(Guid userId) => Task.FromResult<Role?>(null);
        public Task<List<string>> GetPermissionNamesForUserAsync(Guid userId) => Task.FromResult(new List<string>());
        public Task AssignRoleToUserAsync(Guid userId, Guid roleId) => Task.CompletedTask;
        public Task<int> AssignMissingAdminRolesAsync() => Task.FromResult(0);
        public Task RemoveRoleFromUserAsync(Guid userId) => Task.CompletedTask;
        public bool Holds(string role, string permission) =>
            Grants.Contains((Permissions.Single(p => p.Name == permission).Id, Roles.Single(r => r.Name == role).Id));
    }

    [Fact]
    public async Task Seeding_fills_an_empty_site_and_a_second_run_adds_nothing()
    {
        var roles = new FakeRoles();
        var seeder = new ApplicationDbSeeder(roles);
        Assert.Equal(4 + 10 + 10 + 8 + 7 + 1, await seeder.SeedRolesAndPermissionsAsync());
        Assert.True(roles.Holds("Admin", "settings.manage"));
        Assert.False(roles.Holds("Editor", "settings.manage"));
        Assert.True(roles.Holds("Contributor", "posts.edit"));
        Assert.False(roles.Holds("Contributor", "posts.publish"));
        Assert.Equal(0, await seeder.SeedRolesAndPermissionsAsync());
    }

    [Fact]
    public async Task A_first_run_that_stopped_halfway_is_finished_by_the_next_one()
    {
        // Roles were created; the run died before any permission existed. The old seeder saw "roles exist" and never came back.
        var roles = new FakeRoles();
        foreach (var (name, description) in ApplicationDbSeeder.DefaultRoles) await roles.CreateRoleAsync(new Role { Name = name, Description = description });
        Assert.Equal(10 + 10 + 8 + 7 + 1, await new ApplicationDbSeeder(roles).SeedRolesAndPermissionsAsync());
        Assert.True(roles.Holds("Admin", "posts.publish"));
        Assert.True(roles.Holds("Author", "media.manage"));
    }

    [Fact]
    public async Task A_permission_a_site_took_away_from_a_role_is_not_given_back()
    {
        var roles = new FakeRoles();
        var seeder = new ApplicationDbSeeder(roles);
        await seeder.SeedRolesAndPermissionsAsync();
        var editor = roles.Roles.Single(r => r.Name == "Editor").Id;
        var delete = roles.Permissions.Single(p => p.Name == "posts.delete").Id;
        roles.Grants.Remove((delete, editor));

        Assert.Equal(0, await seeder.SeedRolesAndPermissionsAsync());
        Assert.False(roles.Holds("Editor", "posts.delete"));
    }

    [Fact]
    public void Restoring_roles_is_recorded_only_when_something_was_added_and_a_role_change_ends_the_users_sessions()
    {
        var dashboard = Read("Blog.Web", "Controllers", "DashboardController.cs");
        Assert.Contains("if (added > 0)", dashboard);
        Assert.True(dashboard.IndexOf("if (added > 0)", StringComparison.Ordinal) < dashboard.IndexOf("AuditActions.RoleDefaultsRestored", StringComparison.Ordinal));

        var users = Read("Blog.Web", "Controllers", "UsersController.cs");
        var update = users[users.IndexOf("public async Task<IActionResult> UpdateRole(", StringComparison.Ordinal)..];
        update = update[..update.IndexOf("[HttpPost(\"toggle-active\")]", StringComparison.Ordinal)];
        Assert.Contains("SetSessionsValidFromAsync(userId, Blog.Web.Services.DeskSessionValidator.CutOffNow())", update);
        Assert.Contains("DeskSessionValidator.Forget(", update);
        Assert.DoesNotContain("must log out and back in", update);
    }

    // ── Keys ───────────────────────────────────────────────────────────�

    [Fact]
    public async Task A_write_only_key_cannot_delete_and_a_password_reset_ends_every_key()
    {
        if (!TestDatabase.Available(_factory)) return;   // no database on this machine: skip
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        if (admin is null) return;
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var slug = "zz-key-delete-" + tag;
        var created = new List<Guid>();

        async Task<HttpClient> KeyClient(params string[] scopes)
        {
            var (key, prefix) = ApiKeyFormat.Generate();
            created.Add(await keys.CreateAsync(new ApiKey { UserId = admin.Id, Name = "zz del " + tag, Prefix = prefix, KeyHash = ApiKeyFormat.Hash(key), Scopes = ApiKeyFormat.JoinScopes(scopes) }));
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return client;
        }

        try
        {
            var writer = await KeyClient("write");
            Assert.Equal(HttpStatusCode.Created, (await writer.PostAsJsonAsync("/api/v1/posts", new { title = "Key delete check", slug, content = "<p>Draft</p>" })).StatusCode);
            var postId = await conn.ExecuteScalarAsync<Guid>("SELECT Id FROM Posts WHERE Slug = @slug", new { slug });

            // Its own draft, and still no: removing a post is the publish scope's.
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.DeleteAsync("/api/v1/posts/" + postId)).StatusCode);
            Assert.Equal("Draft", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @postId", new { postId }));

            var publisher = await KeyClient("write", "publish");
            Assert.Equal(HttpStatusCode.OK, (await publisher.DeleteAsync("/api/v1/posts/" + postId)).StatusCode);
            Assert.Equal("Trashed", await conn.ExecuteScalarAsync<string>("SELECT Status FROM Posts WHERE Id = @postId", new { postId }));

            // What a password reset does to an account's keys. Only these two test keys are touched: other keys of the account are parked first.
            var others = (await conn.QueryAsync<Guid>("SELECT Id FROM ApiKeys WHERE UserId = @u AND RevokedAt IS NULL AND Id NOT IN @mine", new { u = admin.Id, mine = created })).ToList();
            if (others.Count == 0)
            {
                Assert.Equal(2, await keys.RevokeAllForUserAsync(admin.Id, DateTime.UtcNow));
                Assert.Equal(HttpStatusCode.Unauthorized, (await publisher.GetAsync("/api/v1/users/me")).StatusCode);
                Assert.Equal(0, await keys.RevokeAllForUserAsync(admin.Id, DateTime.UtcNow));
            }
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM Posts WHERE Slug = @slug", new { slug });
            if (created.Count > 0) await conn.ExecuteAsync("DELETE FROM ApiKeys WHERE Id IN @ids", new { ids = created });
        }
    }

    [Fact]
    public void A_password_reset_revokes_keys_and_cloud_mode_closes_what_is_site_wide()
    {
        var account = Read("Blog.Web", "Controllers", "AccountController.cs");
        Assert.Contains("RevokeAllForUserAsync(user.Id, DateTime.UtcNow)", account);

        var subscribers = Read("Blog.Web", "Controllers", "Api", "SubscribersApiController.cs");
        Assert.Contains("IsCloudMode) return NotFound(", subscribers);
        Assert.Contains("Math.Clamp(page, 1, MaxPage)", subscribers);

        var mcp = Read("Blog.Web", "Controllers", "Api", "McpController.cs");
        Assert.Contains("if (_tenant.IsCloudMode) return false;", mcp);
    }

    // ── Goals ──────────────────────────────────────────────────────────�

    [Fact]
    public void Removing_a_goal_takes_what_is_still_in_memory_with_it()
    {
        var goals = Read("Blog.Web", "Controllers", "GoalsController.cs");
        var delete = goals[goals.IndexOf("public async Task<IActionResult> Delete(", StringComparison.Ordinal)..];
        Assert.True(delete.IndexOf("_recorder.FlushAsync()", StringComparison.Ordinal) < delete.IndexOf("DeleteGoalAsync", StringComparison.Ordinal));
    }
}
