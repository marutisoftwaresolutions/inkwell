using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.ViewComponents;

/// <summary>
/// The "Date and time" group on Settings → Display (ADR-0027): country, time zone, date format, clock and a live
/// preview. A view component because it reads the country, zone and locale catalogs, and Settings re-renders
/// from many validation paths that should not each have to load them.
/// </summary>
public class DateSettingsViewComponent : ViewComponent
{
    private readonly DateSettingsCatalog _catalog;
    public DateSettingsViewComponent(DateSettingsCatalog catalog) => _catalog = catalog;

    public sealed record Model(
        IReadOnlyList<Country> Countries,
        IReadOnlyList<DateSettingsCatalog.Option> Zones,
        IReadOnlyList<DateSettingsCatalog.Option> Locales,
        string Country, string Zone, string Locale, string Format, string Clock,
        string Preview,
        IReadOnlyDictionary<string, string> Errors);

    public async Task<IViewComponentResult> InvokeAsync(string? country, string? zone, string? locale, string? format, string? clock)
    {
        var cc = (country ?? "").Trim().ToUpperInvariant();
        var tz = TimeZoneHelper.ToIana(zone);
        var loc = string.IsNullOrWhiteSpace(locale) ? DateDisplayOptions.Default.Locale : locale.Trim();
        var fmt = DateDisplay.IsKnownFormat(format) ? format! : DateDisplay.FormatLocale;
        var clk = DateDisplay.IsKnownClock(clock) ? clock! : DateDisplay.ClockLocale;
        var errors = new Dictionary<string, string>();
        foreach (var key in new[] { "Country", "DisplayTimeZoneId", "DateLocale" })
            if (ModelState[key]?.Errors.FirstOrDefault()?.ErrorMessage is { } e) errors[key] = e;

        var o = new DateDisplayOptions(tz, DateDisplay.IsKnownLocale(loc) ? loc : DateDisplayOptions.Default.Locale, fmt, clk);
        var preview = DateDisplay.Format(DateTime.UtcNow, o, DateStyle.DateTime) + " · " + DateDisplay.Format(DateTime.UtcNow, o, DateStyle.Long);
        return View(new Model(await _catalog.CountriesAsync(), await _catalog.ZoneOptionsAsync(cc, tz),
            await _catalog.LocaleOptionsAsync(cc, loc), cc, tz, loc, fmt, clk, preview, errors));
    }
}
