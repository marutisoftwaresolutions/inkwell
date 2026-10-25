using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Forms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// A contact form end to end on the dev database (ADR-0030): a published page with the Form block renders the real
/// form; a reader's submission is checked, stored and answered; a bad one comes back with errors at its fields; a bot
/// filling the honeypot gets the same "sent" reply and nothing is stored. Creates its own form and page and deletes
/// both. Skips without the database.
/// </summary>
public class ContactFormFlowTests
{
    private readonly ITestOutputHelper _out;
    public ContactFormFlowTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task A_page_form_renders_accepts_rejects_and_discards()
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
        var forms = scope.ServiceProvider.GetRequiredService<IFormRepository>();
        var pages = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var admin = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync();
        var key = "flow-test-" + Guid.NewGuid().ToString("N")[..8];
        var form = new ContactForm
        {
            OwnerId = Guid.Empty, FormKey = key, Name = "Flow test — “form”", IsActive = true, RetentionDays = 30,
            SubmitLabel = "Send it", SuccessMessage = "Got it, thanks.",
            FieldsJson = FormRules.SerializeFields(new[]
            {
                new FormField { Key = "name", Label = "Your name", Type = FormFieldTypes.Name, Required = true },
                new FormField { Key = "email", Label = "Email <address>", Type = FormFieldTypes.Email, Required = true },
                new FormField { Key = "message", Label = "Message", Type = FormFieldTypes.LongText, Required = true },
            }),
        };
        var page = new Page
        {
            Title = "Form flow test", Slug = key, AuthorId = admin!.Id, IsPublished = true, IsInNav = false, PublishedAt = DateTime.UtcNow,
            Content = $"<p>Before.</p>\n<div class=\"comp-form\" data-form=\"{key}\" data-form-name=\"Flow test\"></div>\n<p>After.</p>",
        };
        await forms.CreateAsync(form);
        page.Id = await pages.CreateAsync(page);
        try
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            var html = await client.GetStringAsync("/" + key);
            Assert.Contains($"id=\"form-{key}\"", html);
            Assert.Contains("Flow test — “form”", html);
            Assert.Contains("Email &lt;address&gt;", html);                         // labels are encoded
            Assert.Contains("inkwell-forms.js", html);
            Assert.DoesNotContain("data-form-name", html);                          // the placeholder is gone
            Assert.Contains("<p>After.</p>", html);

            string Hidden(string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(name)}\" value=\"([^\"]*)\"").Groups[1].Value);
            var fields = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Hidden("__RequestVerificationToken"),
                ["formToken"] = Hidden("formToken"),
                ["returnTo"] = "/" + key,
                ["contact_ref"] = "",
            };
            Assert.False(string.IsNullOrEmpty(fields["formToken"]));

            async Task<(HttpStatusCode Status, JsonElement Body)> Send(Dictionary<string, string> extra)
            {
                var all = new Dictionary<string, string>(fields);
                foreach (var kv in extra) all[kv.Key] = kv.Value;
                var req = new HttpRequestMessage(HttpMethod.Post, "/forms/" + key) { Content = new FormUrlEncodedContent(all) };
                req.Headers.Add("X-Requested-With", "fetch");
                var res = await client.SendAsync(req);
                return (res.StatusCode, JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone());
            }

            // A real reader takes more than a few seconds to fill a form in.
            await Task.Delay(TimeSpan.FromSeconds(4.5));

            var bad = await Send(new() { ["f_name"] = "", ["f_email"] = "nope", ["f_message"] = "Hi" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
            Assert.Equal("Enter your name.", bad.Body.GetProperty("errors").GetProperty("name").GetString());
            Assert.True(bad.Body.GetProperty("errors").TryGetProperty("email", out _));

            var good = await Send(new() { ["f_name"] = "Ana", ["f_email"] = "ana@example.com", ["f_message"] = "Hello — “there”" });
            Assert.Equal(HttpStatusCode.OK, good.Status);
            Assert.True(good.Body.GetProperty("ok").GetBoolean());
            Assert.Equal("Got it, thanks.", good.Body.GetProperty("message").GetString());

            var bot = await Send(new() { ["f_name"] = "Bot", ["f_email"] = "bot@example.com", ["f_message"] = "buy now", ["contact_ref"] = "http://spam" });
            Assert.Equal(HttpStatusCode.OK, bot.Status);                           // same reply as a real submission
            Assert.True(bot.Body.GetProperty("ok").GetBoolean());

            var again = await Send(new() { ["f_name"] = "Ana", ["f_email"] = "ana@example.com", ["f_message"] = "Hello — “there”" });
            Assert.True(again.Body.GetProperty("ok").GetBoolean());                // a repeat is answered but not stored

            var stored = await forms.AllSubmissionsAsync(form.Id, Guid.Empty);
            Assert.Single(stored);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stored[0].ValuesJson)!;
            Assert.Equal("Hello — “there”", values["message"]);                    // UTF-8 kept
            // The test host has no client address; a real request is stored as a keyed hash, never the address itself.
            Assert.True(stored[0].AddressHash is null || !stored[0].AddressHash!.Contains('.'));

            // Without JavaScript: the reader is sent back to the form, which shows the outcome.
            var plain = await client.PostAsync("/forms/" + key, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["f_name"] = "", ["f_email"] = "", ["f_message"] = "",
            }));
            Assert.Equal(HttpStatusCode.Redirect, plain.StatusCode);
            Assert.Equal($"/{key}#form-{key}", plain.Headers.Location!.OriginalString);
            var back = await client.GetStringAsync("/" + key);
            Assert.Contains("Check the 3 fields marked below.", back);
            Assert.Contains("aria-invalid=\"true\"", back);

            // A form that is switched off disappears from the page and refuses posts.
            form.IsActive = false;
            await forms.UpdateAsync(form);
            Assert.DoesNotContain($"id=\"form-{key}\"", await client.GetStringAsync("/" + key));
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/forms/" + key, new FormUrlEncodedContent(fields))).StatusCode);
        }
        finally
        {
            await pages.DeleteAsync(page.Id);
            await forms.DeleteAsync(form.Id, Guid.Empty);   // submissions cascade
        }
    }
}
