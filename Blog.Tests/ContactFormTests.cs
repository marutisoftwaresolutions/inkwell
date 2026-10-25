using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Core.Services.Forms;
using Xunit;

namespace Blog.Tests;

/// <summary>Contact forms (ADR-0030): definition rules, submission checks, the spam gate, CSV export and source scans.</summary>
public class ContactFormTests
{
    private static readonly IReadOnlyList<FormField> Contact = new[]
    {
        new FormField { Key = "name", Label = "Your name", Type = FormFieldTypes.Name, Required = true },
        new FormField { Key = "email", Label = "Email address", Type = FormFieldTypes.Email, Required = true },
        new FormField { Key = "topic", Label = "Topic", Type = FormFieldTypes.Choice, Options = new[] { "Sales", "Support" } },
        new FormField { Key = "message", Label = "Message", Type = FormFieldTypes.LongText, Required = true },
        new FormField { Key = "news", Label = "Send me news", Type = FormFieldTypes.Checkbox },
        new FormField { Key = "consent", Label = "I agree", Type = FormFieldTypes.Consent, Required = true },
    };

    // ── Definition ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Contact us", "contact-us")]
    [InlineData("  Café — Réservations!  ", "cafe-reservations")]
    [InlineData("A/B   test__form", "a-b-test-form")]
    [InlineData("!!!", "")]
    public void Keys_are_made_from_names(string name, string expected) => Assert.Equal(expected, FormRules.MakeKey(name));

    [Fact]
    public void A_definition_is_cleaned_keys_filled_and_made_unique()
    {
        var (fields, problems) = FormRules.CleanDefinition("Contact", "contact", new[]
        {
            new FormField { Label = " Name ", Type = FormFieldTypes.Name },
            new FormField { Label = "Name", Type = FormFieldTypes.Text, Options = new[] { "dropped" } },
            new FormField { Label = "Agree", Type = FormFieldTypes.Consent, Required = false },
            new FormField { Label = "Size", Type = "no-such-type" },
        }, null, 365);
        Assert.Empty(problems);
        Assert.Equal(new[] { "name", "name-2", "agree", "size" }, fields.Select(f => f.Key));
        Assert.Equal("Name", fields[0].Label);
        Assert.Empty(fields[1].Options);                                  // options only on choice lists
        Assert.True(fields[2].Required);                                  // consent is always required
        Assert.Equal(FormFieldTypes.Text, fields[3].Type);                // unknown types fall back to text
    }

    [Fact]
    public void A_bad_definition_reports_each_problem_at_its_field()
    {
        var (_, problems) = FormRules.CleanDefinition("", "Not A Key", new[]
        {
            new FormField { Label = "", Type = FormFieldTypes.Text },
            new FormField { Label = "Pick", Type = FormFieldTypes.Choice, Options = new[] { "only one" } },
        }, "not-an-email", -1);
        Assert.Contains(problems, p => p.Field == "Name");
        Assert.Contains(problems, p => p.Field == "FormKey");
        Assert.Contains(problems, p => p.Field == "NotifyEmail");
        Assert.Contains(problems, p => p.Field == "RetentionDays");
        Assert.Contains(problems, p => p.Field!.StartsWith("Fields[") && p.Message.Contains("needs a label"));
        Assert.Contains(problems, p => p.Field!.StartsWith("Fields[") && p.Message.Contains("at least two choices"));
        Assert.Contains(FormRules.CleanDefinition("x", "x", Array.Empty<FormField>(), null, 0).Problems, p => p.Message.Contains("at least one field"));
        Assert.Contains(FormRules.CleanDefinition("x", "x", Enumerable.Range(0, 21).Select(i => new FormField { Label = "F" + i }), null, 0).Problems,
            p => p.Message.Contains("at most 20"));
    }

    [Fact]
    public void Fields_round_trip_through_json()
    {
        var json = FormRules.SerializeFields(Contact);
        Assert.Equal(Contact, FormRules.ParseFields(json), new FieldComparer());
        Assert.Empty(FormRules.ParseFields("not json"));
        Assert.Empty(FormRules.ParseFields(null));
    }

    private sealed class FieldComparer : IEqualityComparer<FormField>
    {
        public bool Equals(FormField? a, FormField? b) => a!.Key == b!.Key && a.Label == b.Label && a.Type == b.Type && a.Required == b.Required && a.Options.SequenceEqual(b.Options);
        public int GetHashCode(FormField f) => f.Key.GetHashCode();
    }

    // ── Submission ──────────────────────────────────────────────────────────────

    private static Func<string, string?> Form(params (string Key, string Value)[] values) =>
        k => values.FirstOrDefault(v => v.Key == k).Value;

    [Fact]
    public void A_complete_submission_is_kept_cleaned_and_limited_to_the_forms_fields()
    {
        var (values, problems) = FormRules.CheckSubmission(Contact, Form(("name", "  Ana  "), ("email", "ana@example.com"),
            ("topic", "Support"), ("message", "Hello — “there”"), ("consent", "yes"), ("extra", "smuggled")));
        Assert.Empty(problems);
        Assert.Equal("Ana", values["name"]);
        Assert.Equal("no", values["news"]);
        Assert.Equal("yes", values["consent"]);
        Assert.Equal("Hello — “there”", values["message"]);
        Assert.False(values.ContainsKey("extra"));
        Assert.Equal("ana@example.com", FormRules.ReplyAddress(Contact, values));
    }

    [Fact]
    public void Missing_and_malformed_values_are_placed_at_their_fields()
    {
        var (_, problems) = FormRules.CheckSubmission(Contact, Form(("email", "not-an-address"), ("topic", "Billing"), ("message", new string('x', 5001))));
        Assert.Contains(problems, p => p.Field == "name" && p.Message == "Enter your name.");
        Assert.Contains(problems, p => p.Field == "email" && p.Message.Contains("email address"));
        Assert.Contains(problems, p => p.Field == "topic" && p.Message == "Choose one of the options.");
        Assert.Contains(problems, p => p.Field == "message" && p.Message.Contains("5,000"));
        Assert.Contains(problems, p => p.Field == "consent" && p.Message.Contains("Tick this box"));
        Assert.DoesNotContain(problems, p => p.Field == "news");      // an unticked optional checkbox is fine
    }

    [Fact]
    public void Repeat_detection_ignores_case_and_spacing_but_not_content()
    {
        var a = FormRules.ContentHash(new Dictionary<string, string> { ["m"] = "Hello  World", ["n"] = "Ana" });
        var b = FormRules.ContentHash(new Dictionary<string, string> { ["n"] = "ana", ["m"] = "hello world" });
        var c = FormRules.ContentHash(new Dictionary<string, string> { ["m"] = "Hello there", ["n"] = "Ana" });
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    // ── Spam gate ───────────────────────────────────────────────────────────────

    private static FormSpamCheck Check(Dictionary<string, string>? values = null, string? honeypot = null,
        CommentFormTokenState state = CommentFormTokenState.Valid, double ageSeconds = 30, bool repeat = false, int recent = 0) =>
        new(values ?? new() { ["name"] = "Ana", ["email"] = "ana@example.com", ["message"] = "See https://vendor.example for the quote." },
            Contact, honeypot, state, state == CommentFormTokenState.Valid ? TimeSpan.FromSeconds(ageSeconds) : null, repeat, recent);

    [Fact]
    public void A_normal_submission_with_one_link_passes() => Assert.Equal(CommentSpamAction.Accept, FormSpamFilter.Evaluate(Check()).Action);

    [Theory]
    [InlineData("honeypot")]
    [InlineData("missing token")]
    [InlineData("too fast")]
    [InlineData("markup")]
    [InlineData("links")]
    [InlineData("name link")]
    [InlineData("repeat")]
    [InlineData("burst")]
    public void Automated_submissions_are_discarded_silently(string kind)
    {
        var check = kind switch
        {
            "honeypot" => Check(honeypot: "x"),
            "missing token" => Check(state: CommentFormTokenState.Missing),
            "too fast" => Check(ageSeconds: 1),
            "markup" => Check(new() { ["name"] = "Ana", ["message"] = "<a href=\"https://spam.example\">cheap</a>" }),
            "links" => Check(new() { ["name"] = "Ana", ["message"] = "a.com b.com c.com" }),
            "name link" => Check(new() { ["name"] = "www.spam.example", ["message"] = "hi" }),
            "repeat" => Check(repeat: true),
            _ => Check(recent: FormSpamFilter.MaxPerAddressInWindow),
        };
        Assert.Equal(CommentSpamAction.RejectSilently, FormSpamFilter.Evaluate(check).Action);
    }

    [Fact]
    public void An_expired_or_unverifiable_form_asks_the_reader_to_reload()
    {
        Assert.Equal(CommentSpamAction.AskToRetry, FormSpamFilter.Evaluate(Check(state: CommentFormTokenState.Invalid)).Action);
        Assert.Equal(CommentSpamAction.AskToRetry, FormSpamFilter.Evaluate(Check(ageSeconds: 25 * 3600)).Action);
    }

    [Fact]
    public void An_email_address_does_not_count_as_a_link()
    {
        var values = new Dictionary<string, string> { ["name"] = "Ana", ["email"] = "ana@company.com", ["message"] = "x.com and y.com" };
        Assert.Equal(CommentSpamAction.Accept, FormSpamFilter.Evaluate(Check(values)).Action);
    }

    // ── CSV ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_has_a_column_per_field_and_neutralises_formulas()
    {
        var rows = new[]
        {
            new FormSubmission { ValuesJson = "{\"name\":\"=HYPERLINK(\\\"http://x\\\")\",\"email\":\"a@b.co\",\"message\":\"line1\\nline2, with comma\"}",
                                 CreatedAt = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc) },
            new FormSubmission { ValuesJson = "not json", CreatedAt = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc), ReadAt = DateTime.UtcNow },
        };
        var csv = FormCsv.Write(Contact, rows);
        var lines = csv.Split("\r\n");
        Assert.Equal("Your name,Email address,Topic,Message,Send me news,I agree,Received (UTC),Read", lines[0]);
        Assert.StartsWith("\"'=HYPERLINK(\"\"http://x\"\")\",a@b.co,,\"line1\nline2, with comma\"", lines[1]);
        Assert.EndsWith("2026-10-14T09:30:00Z,no", lines[1]);
        Assert.EndsWith(",,,,,,2026-10-14T10:00:00Z,yes", lines[2]);
        Assert.Equal("'+1", FormCsv.Cell("+1"));
        Assert.Equal("'-2", FormCsv.Cell("-2"));
        Assert.Equal("'@x", FormCsv.Cell("@x"));
    }

    // ── Source scans ────────────────────────────────────────────────────────────

    private static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_desk_is_admin_only_every_write_is_protected_and_audited()
    {
        var src = Repo("Blog.Web", "Controllers", "FormsController.cs");
        Assert.Contains("[Authorize(Policy = \"AdminOnly\")]", src);
        var posts = Regex.Matches(src, @"\[HttpPost\(""[^""]+""\)\]\s*\[ValidateAntiForgeryToken\]").Count;
        Assert.Equal(Regex.Matches(src, @"\[HttpPost\(").Count, posts);
        foreach (var a in new[] { "FormCreated", "FormUpdated", "FormDeleted", "FormSubmissionDeleted", "FormSubmissionExported" })
            Assert.Contains("AuditActions." + a, src);
        // The builder's list of forms is the only thing a page editor may read.
        // It lives in its own controller: an action inside the Admin-only one would need Admin as well, so an Editor who
        // builds pages saw "There are no forms yet" (v1.0.9 review).
        Assert.DoesNotContain("[HttpGet(\"options\")]", src);
        var options = Repo("Blog.Web", "Controllers", "FormOptionsController.cs");
        Assert.Contains("[Authorize(Policy = \"CanManagePages\")]", options);
        Assert.Contains("[HttpGet(\"options\")]", options);
        Assert.DoesNotContain("AdminOnly", options);
    }

    [Fact]
    public void The_public_endpoint_is_protected_and_fails_soft()
    {
        var src = Repo("Blog.Web", "Controllers", "PublicFormsController.cs");
        Assert.Matches(new Regex(@"\[HttpPost\(""forms/\{key\}""\)\]\s*\[ValidateAntiForgeryToken\]"), src);
        Assert.Contains("User.Identity?.IsAuthenticated != true", src);            // staff skip the spam gate
        Assert.Contains("RegisterCommentSpamAsync", src);                          // discards feed the firewall
        Assert.Contains("_ipHasher.Hash(ip)", src);                                // never the raw address
        Assert.DoesNotContain("AddressHash = ip", src);
        var renderer = Repo("Blog.Web", "Services", "Forms", "FormEmbedRenderer.cs");
        Assert.Contains("HtmlEncode(f.Label)", renderer);
        Assert.Contains("HoneypotFieldName", renderer);
        Assert.Contains("_tokens.Issue(form.Id)", renderer);                       // the timing token is bound to this form
        var page = Repo("Blog.Web", "Views", "Blog", "Page.cshtml");
        Assert.Contains("FormEmbeds.ExpandAsync", page);
        Assert.Contains("ViewData[\"HasForm\"]", Repo("Blog.Web", "Views", "Shared", "_PublicLayout.cshtml"));
    }

    [Fact]
    public void The_form_block_is_offered_on_pages_only()
    {
        Assert.Contains("register('form'", Repo("Blog.Web", "wwwroot", "js", "inkwell-builder.js"));
        Assert.Contains("ViewData[\"BuilderForms\"] = true", Repo("Blog.Web", "Views", "Pages", "Edit.cshtml"));
        Assert.Contains("ViewData[\"BuilderForms\"] = true", Repo("Blog.Web", "Views", "Pages", "Create.cshtml"));
        Assert.DoesNotContain("BuilderForms", Repo("Blog.Web", "Views", "Posts", "Edit.cshtml"));
    }
}
