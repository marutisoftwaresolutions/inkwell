using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Dapper;

namespace Blog.Infrastructure.Data.Repositories;

/// <summary>Reads the Countries, TimeZones and DateLocales catalogs (ADR-0027).</summary>
public class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly DapperContext _ctx;
    public ReferenceDataRepository(DapperContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<Country>> GetCountriesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<Country>("SELECT Code, Name, LikelyLocale, DefaultTimeZone FROM Countries ORDER BY Name")).ToList();
    }

    public async Task<IReadOnlyList<TimeZoneEntry>> GetTimeZonesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<TimeZoneEntry>(
            "SELECT Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName FROM TimeZones ORDER BY StandardOffsetMinutes, DisplayName")).ToList();
    }

    public async Task<IReadOnlyList<DateLocaleEntry>> GetDateLocalesAsync()
    {
        using var conn = _ctx.CreateConnection();
        return (await conn.QueryAsync<DateLocaleEntry>(
            "SELECT Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort FROM DateLocales ORDER BY EnglishName")).ToList();
    }
}
