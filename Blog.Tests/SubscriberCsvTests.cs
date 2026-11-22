using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Subscriber import and export: what a spreadsheet produces is read forgivingly, only valid addresses
/// are accepted and each once, an export cannot be turned into a formula, and an import needs the
/// admin's statement that these people asked for email and never revives someone who left.
/// </summary>
public class SubscriberCsvTests
{
    [Fact]
    public void A_header_row_names_the_columns_wherever_they_are()
    {
        var r = SubscriberCsv.Parse("Name,Plan,Email\r\nAda Lovelace,pro,ADA@Example.org\r\n\"Hopper, Grace\",free,grace@example.org\r\n");
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(new SubscriberCsv.Row("ada@example.org", "Ada Lovelace"), r.Rows[0]);
        Assert.Equal("Hopper, Grace", r.Rows[1].Name);
        Assert.Equal(0, r.Invalid);
    }

    [Fact]
    public void Without_a_header_the_first_address_column_is_used_and_semicolons_work()
    {
        var r = SubscriberCsv.Parse("\uFEFFa@example.org;Ann\nb@example.org;Bob\n");
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal("Ann", r.Rows[0].Name);
        Assert.Single(SubscriberCsv.Parse("only@example.org").Rows);
    }

    [Fact]
    public void Invalid_and_repeated_addresses_are_counted_not_imported()
    {
        var r = SubscriberCsv.Parse("email\na@example.org\nA@EXAMPLE.ORG\nnot-an-address\nb@example\n\n c@example.org \n");
        Assert.Equal(new[] { "a@example.org", "c@example.org" }, r.Rows.Select(x => x.Email).ToArray());
        Assert.Equal(1, r.Duplicates);
        Assert.Equal(2, r.Invalid);
    }

    [Fact]
    public void A_file_is_read_up_to_the_cap_and_says_when_it_stopped()
    {
        var text = "email\n" + string.Join("\n", Enumerable.Range(0, SubscriberCsv.MaxRows + 10).Select(i => $"u{i}@example.org"));
        var r = SubscriberCsv.Parse(text);
        Assert.Equal(SubscriberCsv.MaxRows, r.Rows.Count);
        Assert.True(r.Truncated);
    }

    [Theory]
    [InlineData("a@example.org", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@example.org", false)]
    [InlineData("a@example.org,b@example.org", false)]
    [InlineData("<a@example.org>", false)]
    [InlineData("a@@example.org", false)]
    [InlineData("a@.example.org", false)]
    [InlineData("", false)]
    public void Only_a_single_plain_address_counts(string value, bool valid) => Assert.Equal(valid, SubscriberCsv.IsEmail(value));

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("Lovelace, Ada", "\"Lovelace, Ada\"")]
    [InlineData("plain", "plain")]
    public void An_exported_cell_cannot_run_as_a_formula_in_a_spreadsheet(string value, string expected) =>
        Assert.Equal(expected, SubscriberCsv.Cell(value));

    [Fact]
    public void Export_writes_a_header_and_one_line_per_subscriber()
    {
        var csv = SubscriberCsv.Write([("a@example.org", "=Ann", "confirmed", new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc)), ("b@example.org", null, "pending", null)]);
        Assert.Equal("email,name,status,confirmed_at_utc\r\na@example.org,'=Ann,confirmed,2026-11-12T09:00:00Z\r\nb@example.org,,pending,\r\n", csv);
    }

    [Fact]
    public void Import_needs_the_consent_statement_skips_existing_people_and_logs_counts_not_addresses()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        var controller = File.ReadAllText(Path.Combine(dir!.FullName, "Blog.Web", "Controllers", "SubscribersController.cs"));
        var import = controller[controller.IndexOf("[HttpPost(\"import\")]", StringComparison.Ordinal)..controller.IndexOf("[HttpPost(\"delete/{id:guid}\")]", StringComparison.Ordinal)];
        Assert.Contains("[ValidateAntiForgeryToken]", import);
        Assert.True(import.IndexOf("if (!consent)", StringComparison.Ordinal) < import.IndexOf("SubscriberCsv.Parse(", StringComparison.Ordinal));
        Assert.Contains("GetByEmailAsync(row.Email) is not null) { existing++; continue; }", import);   // never revives someone who left
        Assert.Contains("AuditActions.SubscriberImported", import);
        Assert.DoesNotContain("row.Email}", import);   // no address in a message or the audit trail
        Assert.Contains("AuditActions.SubscriberExported", controller);
        var view = File.ReadAllText(Path.Combine(dir.FullName, "Blog.Web", "Views", "Subscribers", "Index.cshtml"));
        Assert.Contains("name=\"consent\" value=\"true\" required", view);
        Assert.Contains("data-confirm-action=\"Import subscribers\"", view);
    }
}
