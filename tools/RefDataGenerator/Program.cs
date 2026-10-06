// Generates the Countries / TimeZones / DateLocales seed script (ADR-0027) from international standards:
//   IANA Time Zone Database      zone.tab, iso3166.tab, backward, version       (https://www.iana.org/time-zones)
//   Unicode CLDR (JSON)          territoryInfo, likelySubtags, primaryZones, metaZones   (https://cldr.unicode.org)
//   the runtime's CLDR/ICU data  date and time patterns per locale, on the Gregorian calendar
//
// Usage (from the repo root):
//   dotnet run --project tools/RefDataGenerator -p:UseAppHost=false -- <sourceDir> <output.sql>
// Sources missing from <sourceDir> are downloaded (latest tzdata, CLDR main branch). The output is an idempotent
// CREATE-IF-MISSING + MERGE script. For an update, write a new dated DBScripts file, then point the init.sql
// section 39, the EmbeddedResource in Blog.Infrastructure.csproj and DateDisplayTests at it. Run on a Linux host
// (or one with current tzdata): a host that lacks a zone leaves its offset empty and says so.
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Blog.Core.Services;

var dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "inkwell-refdata");
var output = args.Length > 1 ? args[1] : Path.Combine(dir, "refdata.sql");
await EnsureSourcesAsync(dir);
var tzVersion = File.ReadAllText(Path.Combine(dir, "version")).Trim();
using var territoryDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "territoryInfo.json")));
var territories = territoryDoc.RootElement.GetProperty("supplemental").GetProperty("territoryInfo");
using var likelyDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "likelySubtags.json")));
var cldrVersion = likelyDoc.RootElement.GetProperty("supplemental").GetProperty("version").GetProperty("_cldrVersion").GetString();
var likely = likelyDoc.RootElement.GetProperty("supplemental").GetProperty("likelySubtags");

IEnumerable<string[]> Tab(string file) => File.ReadAllLines(Path.Combine(dir, file))
    .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'));

var countries = Tab("iso3166.tab").Select(p => (Code: p[0], Name: p[1])).OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
var countryName = countries.ToDictionary(c => c.Code, c => c.Name);
var zones = Tab("zone.tab").Select(p => (Country: p[0], Id: p[2], Comment: p.Length > 3 ? p[3] : null)).ToList();

static string Sql(string? s) => s is null ? "NULL" : "N'" + s.Replace("'", "''") + "'";
static string Offset(int minutes) => $"UTC{(minutes < 0 ? "−" : "+")}{Math.Abs(minutes) / 60:00}:{Math.Abs(minutes) % 60:00}";

// ── Time zones ───────────────────────────────────────────────────────────────────
var warnings = new List<string>();
var tzRows = new List<(string Id, string? Country, string? Windows, int? Offset, string City, string? Comment, string Display)>();
foreach (var z in zones.Append((Country: (string)null!, Id: "Etc/UTC", Comment: (string?)"Coordinated Universal Time")))
{
    int? offset = null;
    if (TimeZoneHelper.IsKnown(z.Id)) offset = (int)TimeZoneHelper.Resolve(z.Id).BaseUtcOffset.TotalMinutes;   // follows tzdata renames
    else warnings.Add("this host does not know zone " + z.Id + " (offset left empty; Linux hosts with current tzdata do)");
    TimeZoneInfo.TryConvertIanaIdToWindowsId(z.Id, out var windows);
    var city = z.Id == "Etc/UTC" ? "UTC" : z.Id[(z.Id.LastIndexOf('/') + 1)..].Replace('_', ' ');
    var where = z.Country is null ? "Coordinated Universal Time" : countryName.GetValueOrDefault(z.Country, z.Country) + " — " + city;
    var display = (offset is int o ? $"({Offset(o)}) " : "") + where + (z.Country is not null && !string.IsNullOrEmpty(z.Comment) ? $" ({z.Comment})" : "");
    tzRows.Add((z.Id, z.Country, windows, offset, city, z.Comment, display));
}

// ── Date locales: every specific culture the runtime has, on its Gregorian calendar ──
var sample = new DateTime(2026, 10, 5, 14, 30, 15, DateTimeKind.Utc);
var countryCodes = countries.Select(c => c.Code).ToHashSet();
var localeRows = new List<(string Code, string Country, string English, string Native, string Short, string Long, string Time, int Fdow, string SampleLong, string SampleShort)>();
foreach (var c in CultureInfo.GetCultures(CultureTypes.SpecificCultures).OrderBy(c => c.Name, StringComparer.Ordinal))
{
    if (!c.Name.Contains('-')) continue;
    string region;
    try { region = new RegionInfo(c.Name).TwoLetterISORegionName; } catch { continue; }
    if (!countryCodes.Contains(region)) continue;
    var g = DateDisplay.CultureFor(c.Name).DateTimeFormat;
    var o = new DateDisplayOptions("UTC", c.Name, DateDisplay.FormatLocale, DateDisplay.ClockLocale);
    foreach (DateStyle s in Enum.GetValues<DateStyle>())
    {
        var p = DateDisplay.Pattern(s, o);
        var text = DateDisplay.Format(sample, o, s);
        if (string.IsNullOrWhiteSpace(text)) warnings.Add($"{c.Name} {s}: empty");
        if (s is DateStyle.Long or DateStyle.Medium && p.Replace("'", "").Contains("dddd")) warnings.Add($"{c.Name} {s}: weekday left in '{p}'");
    }
    foreach (var clock in new[] { DateDisplay.Clock24, DateDisplay.Clock12 })
    {
        var t = DateDisplay.Format(sample, o with { Clock = clock }, DateStyle.Time);
        if (clock == DateDisplay.Clock24 && !t.Contains("14")) warnings.Add($"{c.Name} 24h: '{t}'");
        if (clock == DateDisplay.Clock12 && !t.Contains('2')) warnings.Add($"{c.Name} 12h: '{t}'");
    }
    localeRows.Add((c.Name, region, c.EnglishName, c.NativeName, g.ShortDatePattern, DateDisplay.Pattern(DateStyle.Long, o),
        g.ShortTimePattern, (int)g.FirstDayOfWeek, DateDisplay.Format(sample, o, DateStyle.Long), DateDisplay.Format(sample, o, DateStyle.Short)));
}
var localeCodes = localeRows.Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

// ── Countries: CLDR's most likely locale for "und-XX", narrowed to one the runtime has ──
string? LikelyLocale(string cc)
{
    // CLDR territoryInfo: the country's languages by share of population; the first with a locale for this country wins.
    if (territories.TryGetProperty(cc, out var t) && t.TryGetProperty("languagePopulation", out var langs))
    {
        foreach (var (lang, _) in langs.EnumerateObject()
                     .Select(p => (p.Name, Pct: p.Value.TryGetProperty("_populationPercent", out var pp) ? double.Parse(pp.GetString()!, CultureInfo.InvariantCulture) : 0))
                     .OrderByDescending(x => x.Pct))
        {
            // "zh" in China is "zh-Hans-CN" to the runtime: add the script CLDR says the language is written in.
            var bare = lang.Replace('_', '-');
            var script = bare.Contains('-') ? null : likely.TryGetProperty(bare, out var ls) ? ls.GetString()!.Split('-')[1] : null;
            foreach (var code in new[] { bare + "-" + cc, script is null ? null : bare + "-" + script + "-" + cc })
            {
                if (code is null) continue;
                var hit = localeRows.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Code;
                if (hit is not null) return hit;
            }
        }
    }
    if (!likely.TryGetProperty("und-" + cc, out var v)) return localeRows.FirstOrDefault(l => l.Country == cc).Code;
    var full = v.GetString()!;                               // e.g. "hi-Deva-IN", "sr-Cyrl-RS"
    var parts = full.Split('-');
    var noScript = parts[0] + "-" + parts[^1];
    if (localeCodes.Contains(full)) return localeRows.First(l => string.Equals(l.Code, full, StringComparison.OrdinalIgnoreCase)).Code;
    if (localeCodes.Contains(noScript)) return localeRows.First(l => string.Equals(l.Code, noScript, StringComparison.OrdinalIgnoreCase)).Code;
    return localeRows.FirstOrDefault(l => l.Country == cc).Code;
}
// ── Default zone per country: CLDR primaryZones where given; otherwise the zone of the country's dominant
//    metazone (the time region most of its zones share), preferring CLDR's zone for that metazone in this
//    territory, then the metazone's golden zone, then zone.tab order. CLDR uses legacy names
//    (Asia/Calcutta), so ids are canonicalised through tzdata's backward links.
var backward = File.ReadAllLines(Path.Combine(dir, "backward")).Where(l => l.StartsWith("Link\t"))
    .Select(l => l.Split('\t', StringSplitOptions.RemoveEmptyEntries)).ToDictionary(p => p[2], p => p[1]);
string Canon(string id) { for (var n = 0; n < 5 && backward.TryGetValue(id, out var t); n++) id = t; return id; }
using var primaryDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "primaryZones.json")));
var primary = primaryDoc.RootElement.GetProperty("supplemental").GetProperty("primaryZones");
using var metaDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "metaZones.json")));
var meta = metaDoc.RootElement.GetProperty("supplemental").GetProperty("metaZones");
var zoneMeta = new Dictionary<string, string>();       // zone → current metazone
void Walk(JsonElement e, string path)
{
    foreach (var p in e.EnumerateObject())
    {
        var id = path.Length == 0 ? p.Name : path + "/" + p.Name;
        if (p.Value.ValueKind == JsonValueKind.Array)
        {
            var current = p.Value.EnumerateArray().Select(x => x.GetProperty("usesMetazone"))
                .FirstOrDefault(u => !u.TryGetProperty("_to", out _));
            if (current.ValueKind == JsonValueKind.Object) zoneMeta[Canon(id)] = current.GetProperty("_mzone").GetString()!;
        }
        else Walk(p.Value, id);
    }
}
Walk(meta.GetProperty("metazoneInfo").GetProperty("timezone"), "");
var mapZones = meta.GetProperty("metazones").EnumerateArray().Select(x => x.GetProperty("mapZone"))
    .Select(m => (Meta: m.GetProperty("_other").GetString()!, Territory: m.GetProperty("_territory").GetString()!, Zone: Canon(m.GetProperty("_type").GetString()!))).ToList();
// Where counting a country's zones picks a smaller region than the capital's (no standard source gives
// population by zone), the capital's zone is named. Reviewed against every country with three or more zones.
var capitalZone = new Dictionary<string, string> { ["CA"] = "America/Toronto", ["RU"] = "Europe/Moscow", ["FM"] = "Pacific/Pohnpei" };
string? DefaultZone(string cc)
{
    var own = zones.Where(z => z.Country == cc).Select(z => z.Id).ToList();
    if (own.Count == 0) return null;
    if (capitalZone.TryGetValue(cc, out var cap) && own.Contains(cap)) return cap;
    if (primary.TryGetProperty(cc, out var p) && own.Contains(Canon(p.GetString()!))) return Canon(p.GetString()!);
    if (own.Count == 1) return own[0];
    var dominant = own.Where(zoneMeta.ContainsKey).GroupBy(z => zoneMeta[z]).OrderByDescending(g => g.Count())
        .ThenBy(g => own.IndexOf(g.First())).FirstOrDefault();
    if (dominant is null) return own[0];
    var local = mapZones.FirstOrDefault(m => m.Meta == dominant.Key && m.Territory == cc).Zone;
    if (local is not null && own.Contains(local)) return local;
    var golden = mapZones.FirstOrDefault(m => m.Meta == dominant.Key && m.Territory == "001").Zone;
    return golden is not null && own.Contains(golden) ? golden : dominant.First();
}
var countryRows = countries.Select(c => (c.Code, c.Name, Likely: LikelyLocale(c.Code), Zone: DefaultZone(c.Code))).ToList();

// ── SQL ──────────────────────────────────────────────────────────────────────────
var sb = new StringBuilder();
var file = Path.GetFileName(output);
sb.AppendLine("-- =============================================================================");
sb.AppendLine($"-- {file}");
sb.AppendLine("-- Change: Countries, time zones and date formats for the site's date settings (v1.0.8, ADR-0027).");
sb.AppendLine("--   Countries    ISO 3166-1 alpha-2 codes and names, each with its most likely locale and main zone");
sb.AppendLine("--   TimeZones    IANA time zone ids, with country, Windows id and standard UTC offset");
sb.AppendLine("--   DateLocales  every country locale with its CLDR date and time patterns and samples");
sb.AppendLine("-- Sources (international standards):");
sb.AppendLine($"--   IANA Time Zone Database {tzVersion}: zone.tab, iso3166.tab (https://www.iana.org/time-zones)");
sb.AppendLine($"--   Unicode CLDR {cldrVersion}: territoryInfo, likelySubtags (country → most used locale), primaryZones and metaZones (country → main zone) (https://cldr.unicode.org)");
sb.AppendLine($"--   CLDR date patterns as shipped in ICU with .NET {Environment.Version}, on the Gregorian calendar");
sb.AppendLine("-- These tables are the catalog the Settings screen offers. Formatting itself uses the runtime's CLDR data");
sb.AppendLine("-- for the chosen locale, and time-zone rules (including daylight saving) come from the runtime's tz database;");
sb.AppendLine("-- StandardOffsetMinutes is informative, for sorting and labels.");
sb.AppendLine("-- Default zone per country: CLDR primaryZones, else the dominant CLDR metazone's zone; the capital's zone for CA, RU, FM.");
sb.AppendLine("-- Generated; re-run the generator when tzdata or CLDR changes. Idempotent: CREATE IF NOT EXISTS, then MERGE");
sb.AppendLine("-- (insert new rows, update changed ones; rows are never deleted, so a saved setting never points at nothing).");
sb.AppendLine($"-- Rows: {countryRows.Count} countries, {tzRows.Count} time zones, {localeRows.Count} date locales.");
sb.AppendLine("-- Run with: sqlcmd -S <server> -d <db> -E -f 65001 -i <this file>   (UTF-8: native locale names)");
sb.AppendLine("-- =============================================================================");
sb.AppendLine("SET NOCOUNT ON;");
sb.AppendLine();
sb.AppendLine("""
IF OBJECT_ID('Countries', 'U') IS NULL
BEGIN
    CREATE TABLE Countries (
        Code            CHAR(2)        NOT NULL CONSTRAINT PK_Countries PRIMARY KEY,
        Name            NVARCHAR(100)  NOT NULL,
        LikelyLocale    NVARCHAR(20)   NULL,
        DefaultTimeZone NVARCHAR(64)   NULL
    );
    PRINT '  [+] Countries created.';
END

IF OBJECT_ID('TimeZones', 'U') IS NULL
BEGIN
    CREATE TABLE TimeZones (
        Id                    NVARCHAR(64)   NOT NULL CONSTRAINT PK_TimeZones PRIMARY KEY,
        CountryCode           CHAR(2)        NULL,
        WindowsId             NVARCHAR(64)   NULL,
        StandardOffsetMinutes INT            NULL,
        City                  NVARCHAR(100)  NOT NULL,
        Comment               NVARCHAR(200)  NULL,
        DisplayName           NVARCHAR(200)  NOT NULL
    );
    CREATE INDEX IX_TimeZones_CountryCode ON TimeZones (CountryCode);
    PRINT '  [+] TimeZones created.';
END

IF OBJECT_ID('DateLocales', 'U') IS NULL
BEGIN
    CREATE TABLE DateLocales (
        Code             NVARCHAR(20)   NOT NULL CONSTRAINT PK_DateLocales PRIMARY KEY,
        CountryCode      CHAR(2)        NOT NULL,
        EnglishName      NVARCHAR(150)  NOT NULL,
        NativeName       NVARCHAR(150)  NOT NULL,
        ShortDatePattern NVARCHAR(60)   NOT NULL,
        LongDatePattern  NVARCHAR(100)  NOT NULL,
        ShortTimePattern NVARCHAR(40)   NOT NULL,
        FirstDayOfWeek   TINYINT        NOT NULL,
        SampleLong       NVARCHAR(100)  NOT NULL,
        SampleShort      NVARCHAR(60)   NOT NULL
    );
    CREATE INDEX IX_DateLocales_CountryCode ON DateLocales (CountryCode);
    PRINT '  [+] DateLocales created.';
END
""");

void Merge<T>(string table, string columns, string key, IReadOnlyList<T> rows, Func<T, string> values)
{
    var cols = columns.Split(',').Select(c => c.Trim()).ToArray();
    for (var i = 0; i < rows.Count; i += 200)
    {
        var batch = rows.Skip(i).Take(200).ToList();
        sb.AppendLine($"MERGE {table} AS t");
        sb.AppendLine("USING (VALUES");
        sb.AppendLine(string.Join(",\n", batch.Select(r => "    (" + values(r) + ")")));
        sb.AppendLine($") AS s ({columns})");
        sb.AppendLine($"ON t.{key} = s.{key}");
        sb.AppendLine("WHEN MATCHED AND (" + string.Join(" OR ", cols.Where(c => c != key)
            .Select(c => $"ISNULL(CAST(t.{c} AS NVARCHAR(200)), N'\u0001') <> ISNULL(CAST(s.{c} AS NVARCHAR(200)), N'\u0001')")) + ")");
        sb.AppendLine("    THEN UPDATE SET " + string.Join(", ", cols.Where(c => c != key).Select(c => $"{c} = s.{c}")));
        sb.AppendLine($"WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({string.Join(", ", cols.Select(c => "s." + c))});");
        sb.AppendLine($"PRINT '  [=] {table}: rows {i + 1}–{i + batch.Count} merged.';");
        sb.AppendLine();
    }
}

Merge("Countries", "Code, Name, LikelyLocale, DefaultTimeZone", "Code", countryRows,
    r => $"{Sql(r.Code)}, {Sql(r.Name)}, {Sql(r.Likely)}, {Sql(r.Zone)}");
Merge("TimeZones", "Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName", "Id", tzRows,
    r => $"{Sql(r.Id)}, {Sql(r.Country)}, {Sql(r.Windows)}, {(r.Offset?.ToString(CultureInfo.InvariantCulture) ?? "NULL")}, {Sql(r.City)}, {Sql(r.Comment)}, {Sql(r.Display)}");
Merge("DateLocales", "Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort", "Code", localeRows,
    r => $"{Sql(r.Code)}, {Sql(r.Country)}, {Sql(r.English)}, {Sql(r.Native)}, {Sql(r.Short)}, {Sql(r.Long)}, {Sql(r.Time)}, {r.Fdow}, {Sql(r.SampleLong)}, {Sql(r.SampleShort)}");

sb.AppendLine("PRINT 'Countries, time zones and date locales are up to date.';");
File.WriteAllText(output, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));

Console.WriteLine($"tzdata {tzVersion}, CLDR {cldrVersion}, .NET {Environment.Version}");
Console.WriteLine($"{countryRows.Count} countries ({countryRows.Count(c => c.Likely is null)} without a locale, {countryRows.Count(c => c.Zone is null)} without a zone), {tzRows.Count} zones, {localeRows.Count} locales");
Console.WriteLine($"{warnings.Count} warnings");
foreach (var c in countryRows.Where(c => zones.Count(z => z.Country == c.Code) >= 3)) Console.WriteLine($"  multi {c.Code} {c.Name}: {c.Zone} of [{string.Join(" ", zones.Where(z => z.Country == c.Code).Select(z => z.Id[(z.Id.LastIndexOf('/') + 1)..]))}]");
foreach (var w in warnings.Take(40)) Console.WriteLine("  " + w);
foreach (var code in new[] { "en-US", "en-GB", "en-IN", "de-DE", "fr-FR", "ja-JP", "ar-SA", "hi-IN", "zh-CN", "pt-BR", "th-TH", "fa-IR" })
{
    var r = localeRows.FirstOrDefault(l => l.Code == code);
    var o = new DateDisplayOptions("UTC", code, "locale", "locale");
    Console.WriteLine($"  {code,-6} long='{r.SampleLong}' medium='{DateDisplay.Format(sample, o, DateStyle.Medium)}' short='{r.SampleShort}' dt='{DateDisplay.Format(sample, o, DateStyle.DateTime)}' wk='{DateDisplay.Format(sample, o, DateStyle.LongWithWeekday)}'");
}
foreach (var cc in new[] { "IN", "US", "GB", "AE", "SA", "DE", "JP", "BR", "CA", "AU", "RU", "MX", "ID", "CN", "KZ", "AR" })
{
    var c = countryRows.First(x => x.Code == cc);
    Console.WriteLine($"  {cc}: {c.Name} likely={c.Likely} zone={c.Zone}");
}

static async Task EnsureSourcesAsync(string dir)
{
    Directory.CreateDirectory(dir);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Inkwell-RefDataGenerator");
    string[] tz = { "zone.tab", "iso3166.tab", "backward", "version" };
    if (tz.Any(f => !File.Exists(Path.Combine(dir, f))))
    {
        Console.WriteLine("Downloading the latest IANA tzdata…");
        await using var gz = await http.GetStreamAsync("https://data.iana.org/time-zones/tzdata-latest.tar.gz");
        await using var tar = new GZipStream(gz, CompressionMode.Decompress);
        using var reader = new TarReader(tar);
        while (reader.GetNextEntry() is { } entry)
            if (tz.Contains(entry.Name) && entry.DataStream is not null)
            {
                await using var file = File.Create(Path.Combine(dir, entry.Name));
                await entry.DataStream.CopyToAsync(file);
            }
    }
    foreach (var name in new[] { "territoryInfo", "likelySubtags", "primaryZones", "metaZones" })
    {
        var path = Path.Combine(dir, name + ".json");
        if (File.Exists(path)) continue;
        Console.WriteLine($"Downloading CLDR {name}…");
        var json = await http.GetStringAsync($"https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-core/supplemental/{name}.json");
        await File.WriteAllTextAsync(path, json);
    }
}
