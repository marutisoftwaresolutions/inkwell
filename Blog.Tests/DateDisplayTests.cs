using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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
/// Site-wide dates by time zone and country format (ADR-0027): the formatter, the reference catalogs, the rule
/// that no view formats a date by hand, and a rendered post in a chosen locale. Database tests skip when the
/// dev database is unreachable and restore what they change.
/// </summary>
public class DateDisplayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _out;

    public DateDisplayTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    private static readonly DateTime Instant = new(2026, 10, 5, 14, 30, 15, DateTimeKind.Utc);
    private static DateDisplayOptions O(string locale, string zone = "UTC", string format = "locale", string clock = "locale") => new(zone, locale, format, clock);

    [Theory]
    [InlineData("en-US", DateStyle.Long, "October 5, 2026")]
    [InlineData("en-US", DateStyle.Medium, "Oct 5, 2026")]
    [InlineData("en-US", DateStyle.Short, "10/5/2026")]
    [InlineData("en-US", DateStyle.LongWithWeekday, "Monday, October 5, 2026")]
    [InlineData("en-GB", DateStyle.Long, "5 October 2026")]
    [InlineData("en-GB", DateStyle.Medium, "5 Oct 2026")]
    [InlineData("en-GB", DateStyle.Short, "05/10/2026")]
    [InlineData("de-DE", DateStyle.Long, "5. Oktober 2026")]
    [InlineData("de-DE", DateStyle.Short, "05.10.2026")]
    [InlineData("fr-FR", DateStyle.Long, "5 octobre 2026")]
    [InlineData("ja-JP", DateStyle.Long, "2026年10月5日")]
    [InlineData("en-US", DateStyle.MonthYear, "October 2026")]
    [InlineData("en-US", DateStyle.Year, "2026")]
    public void Each_locale_writes_dates_its_own_way(string locale, DateStyle style, string expected) =>
        Assert.Equal(expected, DateDisplay.Format(Instant, O(locale), style));

    [Theory]
    [InlineData("ar-SA")]   // Umm al-Qura by default
    [InlineData("th-TH")]   // Buddhist era by default
    [InlineData("fa-IR")]   // Persian calendar by default
    public void Every_site_shows_the_gregorian_year(string locale)
    {
        var text = DateDisplay.Format(Instant, O(locale), DateStyle.Long);
        Assert.Contains("2026", text);
        Assert.IsType<GregorianCalendar>(DateDisplay.CultureFor(locale).DateTimeFormat.Calendar);
    }

    [Fact]
    public void The_weekday_comes_out_of_long_dates_in_every_culture()
    {
        var failures = new List<string>();
        foreach (var c in CultureInfo.GetCultures(CultureTypes.SpecificCultures).Where(c => c.Name.Contains('-')))
        {
            foreach (var style in new[] { DateStyle.Long, DateStyle.Medium })
            {
                var p = DateDisplay.Pattern(style, O(c.Name)).Replace("''", "");
                var unquoted = Regex.Replace(p, "'[^']*'", "");
                var text = DateDisplay.Format(Instant, O(c.Name), style);
                if (unquoted.Contains("ddd") || string.IsNullOrWhiteSpace(text) || !text.Contains("2026"))
                    failures.Add($"{c.Name} {style}: '{p}' → '{text}'");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        Assert.Equal("5 ตุลาคม ค.ศ. 2026", DateDisplay.Format(Instant, O("th-TH"), DateStyle.Long));   // "ที่" went with the weekday
    }

    [Fact]
    public void Iso_8601_mode_is_the_same_everywhere()
    {
        foreach (var locale in new[] { "en-US", "de-DE", "ar-SA", "ja-JP" })
        {
            Assert.Equal("2026-10-05", DateDisplay.Format(Instant, O(locale, format: "iso8601"), DateStyle.Long));
            Assert.Equal("2026-10-05 14:30", DateDisplay.Format(Instant, O(locale, format: "iso8601", clock: "12h"), DateStyle.DateTime));
            Assert.Equal("2026-10", DateDisplay.Format(Instant, O(locale, format: "iso8601"), DateStyle.MonthYear));
        }
    }

    [Fact]
    public void The_clock_can_be_forced_either_way_without_breaking_literals()
    {
        Assert.Equal("14:30", DateDisplay.Format(Instant, O("en-US", clock: "24h"), DateStyle.Time));
        Assert.Equal("2:30 pm", DateDisplay.Format(Instant, O("en-GB", clock: "12h"), DateStyle.Time).Replace(' ', ' '));   // CLDR en-GB writes am/pm in lower case
        Assert.Equal("h 'h' mm tt", DateDisplay.ApplyClock("HH 'h' mm", "12h"));    // fr-CA style: the quoted h survives
        Assert.Equal("HH:mm", DateDisplay.ApplyClock("h:mm tt", "24h"));
        Assert.Equal("h:mm tt", DateDisplay.ApplyClock("h:mm tt", "locale"));
    }

    [Fact]
    public void Instants_follow_the_zone_and_its_daylight_saving()
    {
        Assert.Equal("Oct 4, 2026", DateDisplay.Format(new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc), O("en-US", "America/New_York"), DateStyle.Medium));
        Assert.Equal("1:59 AM", DateDisplay.Format(new DateTime(2026, 3, 8, 6, 59, 0, DateTimeKind.Utc), O("en-US", "America/New_York"), DateStyle.Time).Replace(' ', ' '));
        Assert.Equal("3:00 AM", DateDisplay.Format(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), O("en-US", "America/New_York"), DateStyle.Time).Replace(' ', ' '));
        Assert.Equal("20:00", DateDisplay.Format(Instant, O("en-GB", "Asia/Kolkata", clock: "24h"), DateStyle.Time));
        Assert.Equal("2026-10-04", DateDisplay.IsoDate(new DateTime(2026, 10, 5, 2, 0, 0), O("en-US", "America/New_York")));   // unspecified kind = UTC
    }

    [Fact]
    public void Calendar_dates_never_move_between_zones_and_machine_dates_carry_their_zone()
    {
        Assert.Equal("October 5, 2026", DateDisplay.Format(new DateOnly(2026, 10, 5), O("en-US", "Pacific/Honolulu")));
        Assert.EndsWith("Z", DateDisplay.IsoUtc(new DateTime(2026, 10, 5, 14, 30, 0)));          // stored values come back unspecified
        Assert.Equal("2026-10-05T14:30:00.0000000Z", DateDisplay.IsoUtc(new DateTime(2026, 10, 5, 14, 30, 0)));
    }

    [Fact]
    public void Zone_ids_resolve_across_renames_and_windows_ids_move_to_iana()
    {
        Assert.True(TimeZoneHelper.IsKnown("Europe/Kyiv"));                  // renamed from Europe/Kiev (tzdata 2022b)
        Assert.True(TimeZoneHelper.IsKnown("Asia/Kolkata"));
        Assert.Equal("Asia/Kolkata", TimeZoneHelper.ToIana("India Standard Time"));
        Assert.Equal("Asia/Kolkata", TimeZoneHelper.ToIana("Asia/Kolkata"));
        Assert.Equal("UTC", TimeZoneHelper.ToIana(""));
        Assert.Equal("Not/AZone", TimeZoneHelper.ToIana("Not/AZone"));
        Assert.False(TimeZoneHelper.IsKnown("Not/AZone"));
        Assert.False(DateDisplay.IsKnownLocale("xx-YY"));
        Assert.False(DateDisplay.IsKnownLocale("en"));                       // a language alone is not a country format
        Assert.True(DateDisplay.IsKnownLocale("en-IN"));
        Assert.Equal("en-US", DateDisplay.CultureFor("xx-YY").Name);
    }

    // ── Source rules ────────────────────────────────────────────────────────────────

    private static string Repo(params string[] p)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBScripts", "init.sql"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray());
    }

    [Fact]
    public void No_view_formats_a_date_by_hand()
    {
        // Human-readable dates go through @Dates (SiteDates); these lines are machine formats or explicitly UTC.
        var allowed = new[]
        {
            "Analytics/Index.cshtml",          // query-string dates
            "Posts/Create.cshtml", "Posts/Edit.cshtml",   // <input type=date / datetime-local> values
            "Setup/Restore.cshtml", "Setup/Restored.cshtml",   // before any site settings exist; labelled UTC
        };
        var pattern = new Regex(@"ToString\(""[^""]*(MMM|MM|dd|yyyy|HH|hh|tt)[^""]*""|ToString\(""[dDfFgGRrOou]""\)|\bDateTime\.Now\b|ToLocalTime\(\)|ToShortDateString|ToLongDateString");
        var views = Repo("Blog.Web", "Views");
        var offenders = Directory.GetFiles(views, "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(views, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => !allowed.Contains(f.Rel))
            .SelectMany(f => f.Text.Split('\n').Select((l, i) => (f.Rel, Line: i + 1, Text: l)))
            .Where(l => pattern.IsMatch(l.Text))
            .Select(l => $"{l.Rel}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Format these through @Dates:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_seed_script_is_in_init_sql_word_for_word_and_embedded_for_migrations()
    {
        var seed = File.ReadAllText(Repo("DBScripts", "2026-10-05_create-and-seed-countries-timezones-date-locales.sql")).Replace("\r\n", "\n");
        var body = seed[seed.IndexOf("SET NOCOUNT ON;", StringComparison.Ordinal)..].TrimEnd();
        Assert.Contains(body, File.ReadAllText(Repo("DBScripts", "init.sql")).Replace("\r\n", "\n"));
        Assert.Contains("2026-10-05_create-and-seed-countries-timezones-date-locales.sql", File.ReadAllText(Repo("Blog.Infrastructure", "Blog.Infrastructure.csproj")));
        Assert.NotNull(typeof(Blog.Infrastructure.Data.MigrationService).Assembly.GetManifestResourceStream("Blog.Infrastructure.ReferenceData.sql"));
        Assert.Matches(@"IANA Time Zone Database \d{4}[a-z]", seed);
        Assert.Matches(@"Unicode CLDR \d+", seed);
    }

    // ── Dev database ────────────────────────────────────────────────────────────────

    private bool TryConnection(out string cs)
    {
        cs = "";
        try
        {
            cs = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection") ?? "";
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(cs) { ConnectTimeout = 3 }.ConnectionString);
            probe.Open();
            return true;
        }
        catch (Exception ex) { _out.WriteLine("Dev database unavailable — skipping. " + ex.Message); return false; }
    }

    [Fact]
    public async Task The_catalogs_hold_every_country_and_point_only_at_rows_that_exist()
    {
        if (!TryConnection(out var cs)) return;
        await using var conn = new SqlConnection(cs);
        var counts = await conn.QuerySingleAsync<(int C, int Z, int L)>("SELECT (SELECT COUNT(*) FROM Countries), (SELECT COUNT(*) FROM TimeZones), (SELECT COUNT(*) FROM DateLocales)");
        Assert.True(counts.C >= 249 && counts.Z >= 419 && counts.L >= 500, counts.ToString());
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Countries c WHERE c.DefaultTimeZone IS NOT NULL AND NOT EXISTS (SELECT 1 FROM TimeZones z WHERE z.Id = c.DefaultTimeZone)"));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Countries c WHERE c.LikelyLocale IS NOT NULL AND NOT EXISTS (SELECT 1 FROM DateLocales l WHERE l.Code = c.LikelyLocale)"));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TimeZones z WHERE z.CountryCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Countries c WHERE c.Code = z.CountryCode)"));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DateLocales l WHERE NOT EXISTS (SELECT 1 FROM Countries c WHERE c.Code = l.CountryCode)"));
        var india = await conn.QuerySingleAsync<(string Zone, string Locale)>("SELECT DefaultTimeZone, LikelyLocale FROM Countries WHERE Code = 'IN'");
        Assert.Equal("Asia/Kolkata", india.Zone);
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM DateLocales WHERE (NativeName + SampleLong) COLLATE Latin1_General_BIN LIKE N'%' + NCHAR(226) + NCHAR(8364) + N'%'"));   // no mojibake
    }

    [Fact]
    public async Task A_post_shows_its_date_in_the_sites_format_and_zone()
    {
        if (!TryConnection(out var cs)) return;
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        var owner = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetFirstAdminAsync())!.Id;
        var settings = await repo.GetSettingsAsync(owner);
        var before = (settings.DateLocale, settings.DisplayTimeZoneId, settings.DateFormat, settings.ClockFormat, settings.Country);

        await using var conn = new SqlConnection(cs);
        var post = await conn.QuerySingleOrDefaultAsync<(string Slug, DateTime PublishedAt)>(
            "SELECT TOP 1 Slug, PublishedAt FROM Posts WHERE Status = 'Published' AND PublishedAt IS NOT NULL ORDER BY PublishedAt DESC");
        if (post.Slug is null) { _out.WriteLine("No published post — skipping."); return; }
        try
        {
            settings.DateLocale = "de-DE";
            settings.DisplayTimeZoneId = "Europe/Berlin";
            settings.DateFormat = "locale";
            await repo.SaveSettingsAsync(owner, settings);
            var html = await (await _factory.CreateClient().GetAsync("/" + post.Slug)).Content.ReadAsStringAsync();
            var german = DateDisplay.Format(post.PublishedAt, new DateDisplayOptions("Europe/Berlin", "de-DE", "locale", "locale"), DateStyle.Long);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(german), html);
            Assert.Contains($"datetime=\"{DateDisplay.IsoDate(post.PublishedAt, new DateDisplayOptions("Europe/Berlin", "de-DE", "locale", "locale"))}\"", html);

            settings.DateFormat = "iso8601";
            await repo.SaveSettingsAsync(owner, settings);
            var iso = await (await _factory.CreateClient().GetAsync("/" + post.Slug)).Content.ReadAsStringAsync();
            Assert.Contains(DateDisplay.IsoDate(post.PublishedAt, new DateDisplayOptions("Europe/Berlin", "de-DE", "iso8601", "locale")), iso);
            Assert.Contains("\"datePublished\":\"" + DateDisplay.IsoUtc(post.PublishedAt), iso.Replace("\\u002B", "+"));   // structured data stays UTC
        }
        finally
        {
            (settings.DateLocale, settings.DisplayTimeZoneId, settings.DateFormat, settings.ClockFormat, settings.Country) = before;
            await repo.SaveSettingsAsync(owner, settings);
        }
    }
}
