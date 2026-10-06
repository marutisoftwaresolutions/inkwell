using Blog.Core.Domain;

namespace Blog.Core.Interfaces;

/// <summary>
/// The country, time zone and date locale catalogs (ADR-0027). Read-only and shared by every tenant; seeded by
/// DBScripts/2026-10-05_create-and-seed-countries-timezones-date-locales.sql.
/// </summary>
public interface IReferenceDataRepository
{
    Task<IReadOnlyList<Country>> GetCountriesAsync();
    Task<IReadOnlyList<TimeZoneEntry>> GetTimeZonesAsync();
    Task<IReadOnlyList<DateLocaleEntry>> GetDateLocalesAsync();
}
