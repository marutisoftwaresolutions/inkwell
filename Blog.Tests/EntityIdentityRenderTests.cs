using System.Text.Json;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Proves the entity settings actually reach the two surfaces that matter — the Organization JSON-LD
/// on every page, and the Identity section of llms.txt — and that an unconfigured blog emits neither
/// a blank property nor an invented one.
///
/// Restores the tenant's settings afterwards. Requires the dev database; skips when unreachable.
/// </summary>
public class EntityIdentityRenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;
    private readonly bool _dbAvailable;

    public EntityIdentityRenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
        _dbAvailable = ProbeDb();
    }

    [Fact]
    public async Task Configured_identity_reaches_the_organization_schema_and_llms_txt()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var ownerId = (await users.GetFirstAdminAsync())?.Id ?? Guid.Empty;

        var original = await repo.GetSettingsAsync(ownerId);
        var restore = JsonSerializer.Deserialize<Blog.Core.Domain.UserSettings>(JsonSerializer.Serialize(original))!;

        try
        {
            original.EntityLegalName         = "Example Publishing Ltd";
            original.EntityFounder           = "Ada Lovelace";
            original.EntityFoundingDate      = "2019";
            original.EntitySameAs            = "https://en.wikipedia.org/wiki/Example\nnot-a-url";
            original.EntityIdentityStatement = "Example Review is a reviews blog and is not the vendor of the same name.";
            await repo.SaveSettingsAsync(ownerId, original);

            var client = _factory.CreateClient();

            var home = await client.GetStringAsync("/");
            Assert.Contains("\"legalName\":\"Example Publishing Ltd\"", home);
            Assert.Contains("\"foundingDate\":\"2019\"", home);
            Assert.Contains("Ada Lovelace", home);
            Assert.Contains("https://en.wikipedia.org/wiki/Example", home);
            Assert.DoesNotContain("not-a-url", home);   // invalid sameAs entries never ship

            var llms = await client.GetStringAsync("/llms.txt");
            Assert.Contains("Example Review is a reviews blog", llms);
            Assert.Contains("- Founder: Ada Lovelace", llms);
            Assert.Contains("- Founded: 2019", llms);
            Assert.Contains("- Profile: https://en.wikipedia.org/wiki/Example", llms);
        }
        finally
        {
            await repo.SaveSettingsAsync(ownerId, restore);
        }
    }

    [Fact]
    public async Task An_unconfigured_blog_emits_no_blank_or_invented_entity_properties()
    {
        if (!_dbAvailable) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var ownerId = (await users.GetFirstAdminAsync())?.Id ?? Guid.Empty;

        var original = await repo.GetSettingsAsync(ownerId);
        var restore = JsonSerializer.Deserialize<Blog.Core.Domain.UserSettings>(JsonSerializer.Serialize(original))!;

        try
        {
            original.EntityLegalName = original.EntityFounder = original.EntityFoundingDate =
                original.EntitySameAs = original.EntityIdentityStatement = "";
            await repo.SaveSettingsAsync(ownerId, original);

            var home = await _factory.CreateClient().GetStringAsync("/");

            // An absent property is correct; an empty one is a claim that the value is blank.
            Assert.DoesNotContain("\"legalName\":\"\"", home);
            Assert.DoesNotContain("\"foundingDate\":\"\"", home);
            Assert.DoesNotContain("\"founder\"", home);

            // The publisher node itself must still be well-formed and typed.
            Assert.Contains("\"@type\":\"Organization\"", home);

            var llms = await _factory.CreateClient().GetStringAsync("/llms.txt");
            Assert.Contains("## Identity", llms);
            Assert.DoesNotContain("- Founder:", llms);
        }
        finally
        {
            await repo.SaveSettingsAsync(ownerId, restore);
        }
    }

    private bool ProbeDb()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }
}
