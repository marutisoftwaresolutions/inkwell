using System.Net;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Custom fields (ADR-0025): definitions in Settings, validated values per post and page, the reader's
/// "Details" list and additionalProperty, revisions. The end-to-end test runs against the dev database
/// and restores everything it changes; it skips when the database is unreachable.
/// </summary>
public class CustomFieldsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public CustomFieldsTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static CustomFieldDefinition Def(string key, string label, string type, string applies = CustomFieldTargets.Posts, bool show = true) =>
        new() { Key = key, Label = label, Type = type, AppliesTo = applies, ShowOnPage = show };

    [Theory]
    [InlineData("Price from", "price-from")]
    [InlineData("  Ölçü / Size (cm) ", "olcu-size-cm")]
    [InlineData("!!!", "field")]
    public void Keys_come_from_labels(string label, string key) =>
        Assert.Equal(key, CustomFields.KeyOf(label));

    [Fact]
    public void Existing_fields_keep_their_key_when_renamed_and_new_ones_never_take_an_old_key()
    {
        var existing = new[] { Def("price", "Price", CustomFieldTypes.Number) };
        var (fields, errors) = CustomFields.NormalizeDefinitions(new[]
        {
            Def("price", "Price from (USD)", CustomFieldTypes.Number),       // renamed: same key
            Def("", "Price", "bogus", "nowhere"),                            // new, same label: must not reuse "price"
            Def("", "   ", CustomFieldTypes.Text),                           // no label
        }, existing);

        Assert.Equal("price", fields[0].Key);
        Assert.Equal("Price from (USD)", fields[0].Label);
        Assert.Equal("price-2", fields[1].Key);
        Assert.Equal(CustomFieldTypes.Text, fields[1].Type);                  // unknown type falls back
        Assert.Equal(CustomFieldTargets.Posts, fields[1].AppliesTo);
        Assert.Equal(2, fields.Count);
        Assert.Contains("label", errors[2]);

        // A client cannot claim a key that does not exist yet.
        var (forged, _) = CustomFields.NormalizeDefinitions(new[] { Def("anything-i-like", "Version", CustomFieldTypes.Text) }, existing);
        Assert.Equal("version", forged[0].Key);

        var many = Enumerable.Range(0, CustomFields.MaxFields + 1).Select(i => Def("", "Field " + i, CustomFieldTypes.Text));
        var (capped, capErrors) = CustomFields.NormalizeDefinitions(many, null);
        Assert.Equal(CustomFields.MaxFields, capped.Count);
        Assert.Single(capErrors);
    }

    private static readonly CustomFieldDefinition[] PostFields =
    {
        Def("price", "Price from", CustomFieldTypes.Number),
        Def("released", "Released", CustomFieldTypes.Date),
        Def("site", "Website", CustomFieldTypes.Url),
        Def("trial", "Free trial", CustomFieldTypes.YesNo),
        Def("note", "Internal note", CustomFieldTypes.Text, show: false),
        Def("hours", "Opening hours", CustomFieldTypes.Text, CustomFieldTargets.Pages),
    };

    [Fact]
    public void Values_are_validated_per_type_and_values_of_other_fields_are_kept()
    {
        var defs = CustomFields.For(PostFields, pages: false);
        Assert.DoesNotContain(defs, d => d.Key == "hours");

        var ok = CustomFields.Clean(defs, new Dictionary<string, string?>
        {
            ["price"] = " 299.50 ", ["released"] = "2026-09-01", ["site"] = "https://example.com/pricing",
            ["trial"] = "true", ["note"] = "Checked by phone", ["not-a-field"] = "ignored",
        }, "{\"hours\":\"9-5\",\"price\":\"1\"}");
        Assert.Empty(ok.Errors);
        var stored = CustomFields.Parse(ok.Json);
        Assert.Equal("299.50", stored["price"]);
        Assert.Equal("9-5", stored["hours"]);                       // a page field's value is not the post editor's to clear
        Assert.False(stored.ContainsKey("not-a-field"));

        var bad = CustomFields.Clean(defs, new Dictionary<string, string?>
        {
            ["price"] = "about 300", ["released"] = "01/09/2026", ["site"] = "javascript:alert(1)", ["note"] = new string('x', 501),
        }, null);
        Assert.Equal(4, bad.Errors.Count);
        Assert.Equal("about 300", bad.Values["price"]);              // shown back as typed
        Assert.Contains("number", bad.Errors["price"]);

        var cleared = CustomFields.Clean(defs, new Dictionary<string, string?>(), ok.Json);
        Assert.Equal("{\"hours\":\"9-5\"}", cleared.Json);         // empty and unticked mean unset
        Assert.Null(CustomFields.Clean(defs, null, null).Json);
        Assert.Empty(CustomFields.Parse("not json"));

        var yesNo = CustomFields.For(PostFields, false).Where(d => d.Key == "trial").ToList();
        Assert.Equal("{\"trial\":\"false\"}", CustomFields.Clean(yesNo, new Dictionary<string, string?> { ["trial"] = "false" }, null).Json);
        Assert.Null(CustomFields.Clean(yesNo, new Dictionary<string, string?> { ["trial"] = "" }, "{\"trial\":\"true\"}").Json);

        Assert.True(CustomFields.Fits(CustomFieldTypes.Number, "4.5"));
        Assert.False(CustomFields.Fits(CustomFieldTypes.Number, "from 40"));   // saved while the field was text
        Assert.False(CustomFields.Fits(CustomFieldTypes.Date, "next spring"));
        Assert.False(CustomFields.Fits(CustomFieldTypes.YesNo, "maybe"));
        Assert.True(CustomFields.Fits(CustomFieldTypes.Url, null));
    }

    [Fact]
    public void Readers_see_only_visible_fields_with_a_value_formatted_for_reading()
    {
        var json = "{\"price\":\"299.5\",\"released\":\"2026-09-01\",\"site\":\"https://example.com/pricing/\",\"trial\":\"true\",\"note\":\"secret\",\"hours\":\"9-5\"}";
        var shown = CustomFields.Visible(PostFields, pages: false, json);
        Assert.Equal(new[] { "Price from", "Released", "Website", "Free trial" }, shown.Select(s => s.Label));
        Assert.Equal("September 1, 2026", shown[1].Text);                 // a site that has not chosen: en-US
        var gb = CustomFields.Visible(PostFields, false, json, new DateDisplayOptions("Europe/London", "en-GB", DateDisplay.FormatLocale, DateDisplay.ClockLocale));
        Assert.Equal("1 September 2026", gb[1].Text);                      // the site's date format (ADR-0027)
        Assert.Equal("2026-09-01", CustomFields.Visible(PostFields, false, json, DateDisplayOptions.Default with { Format = DateDisplay.FormatIso })[1].Text);
        Assert.Equal("example.com/pricing", shown[2].Text);
        Assert.Equal("Yes", shown[3].Text);
        Assert.Equal(299.5m, CustomFields.SchemaValue(shown[0]));
        Assert.Equal(true, CustomFields.SchemaValue(shown[3]));
        var no = CustomFields.Visible(PostFields, false, "{\"trial\":\"false\"}");
        Assert.Equal("No", Assert.Single(no).Text);                 // yes/no has three states: not set, yes, no
        Assert.Equal(false, CustomFields.SchemaValue(no[0]));
        Assert.Equal("Opening hours", Assert.Single(CustomFields.Visible(PostFields, pages: true, json)).Label);
        Assert.Empty(CustomFields.Visible(PostFields, false, null));
        Assert.Empty(CustomFields.Visible(null, false, json));
    }

    [Fact]
    public void Revisions_carry_custom_fields_and_older_ones_leave_them_alone()
    {
        var post = new Post { Id = Guid.NewGuid(), Title = "T", Html = "<p>x</p>", CustomFieldsJson = "{\"price\":\"1\"}" };
        var rev = RevisionSnapshot.FromPost(post, "Saved", Guid.NewGuid(), "me");
        var target = new Post { Id = post.Id, CustomFieldsJson = "{\"price\":\"2\"}" };
        RevisionSnapshot.ApplyToPost(rev, target);
        Assert.Equal("{\"price\":\"1\"}", target.CustomFieldsJson);

        var old = RevisionSnapshot.FromPost(new Post { Id = post.Id, Title = "T", Html = "<p>x</p>" }, "Saved", Guid.NewGuid(), "me");
        Assert.DoesNotContain("CustomFieldsJson", old.StructuredJson);   // compares equal to revisions written before this release
        RevisionSnapshot.ApplyToPost(old, target);
        Assert.Equal("{\"price\":\"1\"}", target.CustomFieldsJson);

        var page = new Page { Id = Guid.NewGuid(), Title = "P", Content = "<p>y</p>", CustomFieldsJson = "{\"hours\":\"9-5\"}" };
        var pageRev = RevisionSnapshot.FromPage(page, "Saved", Guid.NewGuid(), "me");
        var restored = new Page { Id = page.Id };
        RevisionSnapshot.ApplyToPage(pageRev, restored);
        Assert.Equal("{\"hours\":\"9-5\"}", restored.CustomFieldsJson);
        Assert.Null(RevisionSnapshot.FromPage(new Page { Title = "P" }, "Saved", Guid.NewGuid(), "me").StructuredJson);
    }

    [Fact]
    public void Every_layout_shows_the_details_and_the_editors_carry_the_panel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray()));

        foreach (var layout in new[] { "_Post_Neutral", "_Post_Magazine", "_Post_Verdict", "_Page_Neutral", "_Page_Magazine" })
            Assert.Contains("<partial name=\"_CustomFieldDetails\" />", Read("Blog.Web", "Views", "Blog", layout + ".cshtml"));
        foreach (var editor in new[] { new[] { "Posts", "Edit" }, new[] { "Posts", "Create" }, new[] { "Pages", "Edit" }, new[] { "Pages", "Create" } })
            Assert.Contains("<partial name=\"_CustomFieldsPanel\" />", Read("Blog.Web", "Views", editor[0], editor[1] + ".cshtml"));
        Assert.Contains("additionalProperty", Read("Blog.Web", "Views", "Shared", "_SchemaJsonLd.cshtml"));
        Assert.Contains("JsonSerializer.Serialize(userSettings.CustomFields)", Read("Blog.Web", "Controllers", "BlogController.cs"));   // in the ETag
        var css = Read("Blog.Web", "wwwroot", "css", "inkwell.css");
        Assert.Contains(".ink-details {", css);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\.ink-details[^}]*#[0-9a-fA-F]{3,6}"), css);   // tokens only
    }

    [Fact]
    public void Editing_a_page_saves_every_field_its_editor_shows()
    {
        // The page editor showed the search and social fields, but Edit copied only title, slug, content,
        // image, parent and order onto the stored page, so those edits were silently dropped.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        var view = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Views", "Pages", "Edit.cshtml"));
        var ctrl = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Controllers", "PagesController.cs"));
        var posted = System.Text.RegularExpressions.Regex.Matches(view, "name=\"([A-Z][A-Za-z]+)\"").Select(m => m.Groups[1].Value).Distinct()
            .Except(new[] { "FeaturedImageUrl" });   // display only; the id is what is stored
        foreach (var field in posted)
            Assert.True(ctrl.Contains($"existing.{field} = page.{field};"), $"Pages/Edit posts {field} but Edit never saves it.");
    }

    [Fact]
    public async Task A_post_shows_its_fields_to_readers_and_in_structured_data()
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
        var owner = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!.Id;
        var settings = await settingsRepo.GetSettingsAsync(owner);
        var originalFields = settings.CustomFields.ToList();

        await using var conn = new SqlConnection(cs);
        var post = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Slug, string? Json)>(
            "SELECT TOP 1 Id, Slug, CustomFieldsJson FROM Posts WHERE Status = 'Published' ORDER BY NEWID()");
        if (post.Id == Guid.Empty) { _out.WriteLine("No published post — skipping."); return; }

        try
        {
            settings.CustomFields = originalFields.Concat(new[]
            {
                Def("zz-test-price", "Test price", CustomFieldTypes.Number),
                Def("zz-test-hidden", "Test hidden", CustomFieldTypes.Text, show: false),
            }).ToList();
            await settingsRepo.SaveSettingsAsync(owner, settings);
            await conn.ExecuteAsync("UPDATE Posts SET CustomFieldsJson = @j WHERE Id = @id",
                new { j = "{\"zz-test-price\":\"42.5\",\"zz-test-hidden\":\"do not show\"}", id = post.Id });

            var res = await _factory.CreateClient().GetAsync("/" + post.Slug);
            var html = await res.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Contains("class=\"ink-details\"", html);
            Assert.Contains("<dt>Test price</dt>", html);
            Assert.DoesNotContain("do not show", html);
            Assert.Contains("\"additionalProperty\"", html);
            Assert.Contains("{\"@type\":\"PropertyValue\",\"name\":\"Test price\",\"value\":42.5}", html);
        }
        finally
        {
            settings.CustomFields = originalFields;
            await settingsRepo.SaveSettingsAsync(owner, settings);
            await conn.ExecuteAsync("UPDATE Posts SET CustomFieldsJson = @j WHERE Id = @id", new { j = post.Json, id = post.Id });
        }
    }
}
