using System.Globalization;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Services;

/// <summary>
/// The country, time-zone and date-format choices Settings offers (ADR-0027), read from the Countries,
/// TimeZones and DateLocales tables and cached (they change only when the seed script is re-run). Fails open:
/// if the tables are missing or empty, the runtime's own zones and cultures are offered instead.
/// </summary>
public sealed class DateSettingsCatalog
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private readonly IReferenceDataRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DateSettingsCatalog> _log;

    public DateSettingsCatalog(IReferenceDataRepository repo, IMemoryCache cache, ILogger<DateSettingsCatalog> log)
    {
        _repo = repo;
        _cache = cache;
        _log = log;
    }

    public sealed record Option(string Value, string Label);

    public Task<IReadOnlyList<Country>> CountriesAsync() =>
        Cached("countries", async () => await _repo.GetCountriesAsync(), () => Array.Empty<Country>());

    public Task<IReadOnlyList<TimeZoneEntry>> ZonesAsync() =>
        Cached("zones", async () => await _repo.GetTimeZonesAsync(), RuntimeZones);

    public Task<IReadOnlyList<DateLocaleEntry>> LocalesAsync() =>
        Cached("locales", async () => await _repo.GetDateLocalesAsync(), RuntimeLocales);

    /// <summary>The zones to offer: the country's own (or all of them), UTC, and the current choice if it is elsewhere.</summary>
    public async Task<IReadOnlyList<Option>> ZoneOptionsAsync(string? country, string? current)
    {
        var all = await ZonesAsync();
        var names = (await CountriesAsync()).ToDictionary(c => c.Code, c => c.Name);
        var list = string.IsNullOrEmpty(country) ? all : all.Where(z => z.CountryCode == country).ToList();
        // Place first, so typing a city or country in the closed list jumps to it ("Dubai — United Arab Emirates (UTC+04:00)").
        var options = list.Select(z => new Option(z.Id, ZoneLabel(z, names))).OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!options.Any(o => o.Value is "Etc/UTC" or TimeZoneHelper.Utc)) options.Add(new Option(TimeZoneHelper.Utc, "UTC — Coordinated Universal Time"));
        var cur = TimeZoneHelper.ToIana(current);
        if (!string.IsNullOrEmpty(cur) && !options.Any(o => string.Equals(o.Value, cur, StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, new Option(cur, all.FirstOrDefault(z => z.Id == cur) is { } z ? ZoneLabel(z, names) : cur));
        return options;
    }

    private static string ZoneLabel(TimeZoneEntry z, IReadOnlyDictionary<string, string> countries)
    {
        if (z.Id is "Etc/UTC" or TimeZoneHelper.Utc) return "UTC — Coordinated Universal Time";
        var offset = z.StandardOffsetMinutes is int m ? $" (UTC{(m < 0 ? "−" : "+")}{Math.Abs(m) / 60:00}:{Math.Abs(m) % 60:00})" : "";
        var where = z.CountryCode is not null && countries.TryGetValue(z.CountryCode, out var n) ? " — " + n : "";
        var note = string.IsNullOrEmpty(z.Comment) ? "" : ", " + z.Comment;
        return (string.IsNullOrEmpty(z.City) ? z.Id : z.City) + where + note + offset;
    }

    /// <summary>The date formats to offer: the country's locales (or all of them), and the current choice if it is elsewhere.</summary>
    public async Task<IReadOnlyList<Option>> LocaleOptionsAsync(string? country, string? current)
    {
        var all = await LocalesAsync();
        var list = string.IsNullOrEmpty(country) ? all : all.Where(l => l.CountryCode == country).ToList();
        var options = list.Select(l => new Option(l.Code, Label(l))).ToList();
        if (!string.IsNullOrEmpty(current) && !options.Any(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase)))
        {
            var l = all.FirstOrDefault(x => string.Equals(x.Code, current, StringComparison.OrdinalIgnoreCase));
            options.Insert(0, new Option(current, l is null ? current : Label(l)));
        }
        return options;
    }

    /// <summary>For a newly chosen country: its main zone, and the site language's locale there if it has one, else the country's most used.</summary>
    public async Task<(string? Zone, string? Locale)> SuggestAsync(string country, string? siteLanguage)
    {
        var c = (await CountriesAsync()).FirstOrDefault(x => x.Code == country);
        var locales = (await LocalesAsync()).Where(l => l.CountryCode == country).ToList();
        var lang = (siteLanguage ?? "").Split('-')[0];
        var byLanguage = locales.FirstOrDefault(l => l.Code.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase));
        return (c?.DefaultTimeZone, byLanguage?.Code ?? c?.LikelyLocale ?? locales.FirstOrDefault()?.Code);
    }

    public async Task<bool> IsKnownCountryAsync(string? code)
    {
        if (string.IsNullOrEmpty(code)) return true;
        var countries = await CountriesAsync();
        return countries.Count == 0 || countries.Any(c => c.Code == code);   // no catalog: nothing to check against
    }

    private static string Label(DateLocaleEntry l)
    {
        var name = string.Equals(l.EnglishName, l.NativeName, StringComparison.Ordinal) ? l.EnglishName : $"{l.EnglishName} · {l.NativeName}";
        return $"{name} — {l.SampleLong}";
    }

    private async Task<IReadOnlyList<T>> Cached<T>(string key, Func<Task<IReadOnlyList<T>>> load, Func<IReadOnlyList<T>> fallback)
    {
        if (_cache.TryGetValue("refdata:" + key, out IReadOnlyList<T>? hit) && hit is not null) return hit;
        IReadOnlyList<T> value;
        try
        {
            value = await load();
            if (value.Count == 0) value = fallback();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reference data {Key} unavailable; using the runtime's list.", key);
            value = fallback();
        }
        _cache.Set("refdata:" + key, value, CacheFor);
        return value;
    }

    private static IReadOnlyList<TimeZoneEntry> RuntimeZones() =>
        TimeZoneInfo.GetSystemTimeZones()
            .Select(z => new TimeZoneEntry
            {
                Id = TimeZoneHelper.ToIana(z.Id), StandardOffsetMinutes = (int)z.BaseUtcOffset.TotalMinutes,
                City = z.StandardName, DisplayName = z.DisplayName,
            })
            .GroupBy(z => z.Id).Select(g => g.First()).OrderBy(z => z.StandardOffsetMinutes).ThenBy(z => z.DisplayName).ToList();

    private static IReadOnlyList<DateLocaleEntry> RuntimeLocales()
    {
        var sample = new DateTime(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc);
        return CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(c => c.Name.Contains('-'))
            .Select(c =>
            {
                string region;
                try { region = new RegionInfo(c.Name).TwoLetterISORegionName; } catch { region = ""; }
                var o = new DateDisplayOptions(TimeZoneHelper.Utc, c.Name, DateDisplay.FormatLocale, DateDisplay.ClockLocale);
                return new DateLocaleEntry
                {
                    Code = c.Name, CountryCode = region, EnglishName = c.EnglishName, NativeName = c.NativeName,
                    SampleLong = DateDisplay.Format(sample, o, DateStyle.Long), SampleShort = DateDisplay.Format(sample, o, DateStyle.Short),
                };
            })
            .OrderBy(l => l.EnglishName).ToList();
    }
}
