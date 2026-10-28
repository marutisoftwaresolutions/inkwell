using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// First-Party Products rule (.claude/rules/first-party-products.md, owner decision 2026-10-25). OptoSoft and RetaPOS
/// are made by the site's publisher; articles must read as independent guides: no first-person ownership wording, no
/// company named as the site's owner, and the publisher's products never scored, ranked first or badged as the pick.
/// Content reaches the site through dated SQL scripts that bypass the Desk's linter, so this guards the scripts and
/// the published rows as well. A violation here once read "OptoSoft is our own product" on 15 live posts.
/// </summary>
public class FirstPartyContentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Regex Ownership = new(@"\bour own (?:product|platform)\b|\bour product\b|\bour platform\b|runs this site|maruti",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly DateOnly RuleDate = new(2026, 10, 25);
    private static readonly string[] FirstPartyNames = { "OptoSoft", "RetaPOS" };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public FirstPartyContentTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    [Fact]
    public void Content_scripts_since_the_rule_carry_no_ownership_wording_and_never_crown_a_first_party_product()
    {
        var problems = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoRoot(), "DBScripts"), "20??-??-??_*.sql"))
        {
            var name = Path.GetFileName(path);
            if (!DateOnly.TryParse(name[..10], out var date) || date < RuleDate) continue;
            var sql = File.ReadAllText(path);

            foreach (Match m in Ownership.Matches(sql))
                problems.Add($"{name}: \"{m.Value}\"");

            // A first-party roundup entry object: from its "name" to the closing brace of that entry.
            foreach (var product in FirstPartyNames)
                foreach (Match m in Regex.Matches(sql, $@"""name""\s*:\s*""{product}""(?:[^{{}}]|\{{[^{{}}]*\}})*\}}", RegexOptions.IgnoreCase))
                {
                    if (Regex.IsMatch(m.Value, @"""score""\s*:\s*[1-9]", RegexOptions.IgnoreCase))
                        problems.Add($"{name}: {product} entry carries a score");
                    if (Regex.IsMatch(m.Value, @"""badge""\s*:\s*""[^""]*(?:pick|best overall|winner)", RegexOptions.IgnoreCase))
                        problems.Add($"{name}: {product} entry carries a pick badge");
                    if (Regex.IsMatch(m.Value, @"""rank""\s*:\s*1\b", RegexOptions.IgnoreCase))
                        problems.Add($"{name}: {product} entry is ranked first");
                }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Published_posts_read_as_independent_guides()
    {
        if (!DbAvailable()) { _out.WriteLine("Dev database unavailable — skipping."); return; }

        await using var conn = new SqlConnection(ConnectionString());
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT Slug, CONCAT(Title, N' ', MetaTitle, N' ', MetaDescription, N' ', AnswerCapsule, N' ', Html, N' ',
                                RoundupJson, N' ', FaqJson, N' ', KeyFactsJson), RoundupJson
              FROM Posts WHERE Status IN ('Published', 'Scheduled')", conn);
        var problems = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var slug = r.GetString(0);
            if (Ownership.Match(r.GetString(1)) is { Success: true } m) problems.Add($"/{slug}: \"{m.Value}\"");

            if (r.IsDBNull(2) || string.IsNullOrWhiteSpace(r.GetString(2))) continue;
            var findings = StructuredDataLinter.Lint(new Post { Title = slug, RoundupJson = r.GetString(2) });
            foreach (var f in findings.Where(f => f.Severity == LintSeverity.Error && f.Message.Contains("publisher's own product")))
                problems.Add($"/{slug}: {f.Message}");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private string? ConnectionString() =>
        _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

    private bool DbAvailable()
    {
        try
        {
            var cs = ConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return false;
            using var conn = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            conn.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine($"DB probe failed: {ex.Message}"); return false; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
