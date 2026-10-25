using System.Net;
using Blog.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Organization contact point (v1.0.9): Settings → Identity's contact fields reach the publisher's structured data
/// and llms.txt only when set; a site that leaves them empty renders exactly as before.
/// </summary>
public class ContactPointRenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public ContactPointRenderTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Fact]
    public async Task The_home_page_and_llms_txt_carry_the_contact_only_when_it_is_set()
    {
        try
        {
            var cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            await probe.OpenAsync();
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return; }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var owner = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!.Id;
        var settings = await repo.GetSettingsAsync(owner);
        var (email, url, type) = (settings.EntityContactEmail, settings.EntityContactUrl, settings.EntityContactType);
        var client = _factory.CreateClient();

        try
        {
            settings.EntityContactEmail = settings.EntityContactUrl = settings.EntityContactType = string.Empty;
            await repo.SaveSettingsAsync(owner, settings);
            Assert.DoesNotContain("\"contactPoint\"", await client.GetStringAsync("/"));

            settings.EntityContactEmail = "contact-check@example.invalid";
            settings.EntityContactUrl = "/contact";
            await repo.SaveSettingsAsync(owner, settings);
            var home = await client.GetStringAsync("/");
            Assert.Contains("\"contactPoint\":{\"@type\":\"ContactPoint\",\"contactType\":\"customer support\",\"email\":\"contact-check@example.invalid\",\"url\":\"http://localhost/contact\"}", home);
            Assert.Contains("contact-check@example.invalid", await client.GetStringAsync("/llms.txt"));
        }
        finally
        {
            (settings.EntityContactEmail, settings.EntityContactUrl, settings.EntityContactType) = (email, url, type);
            await repo.SaveSettingsAsync(owner, settings);
        }
    }
}
