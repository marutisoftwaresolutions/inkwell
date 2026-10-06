namespace Blog.Core.Domain;

/// <summary>An ISO 3166-1 country (Countries table, from the IANA tz database; ADR-0027).</summary>
public sealed class Country
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The locale most of the country uses (CLDR territoryInfo), or null when the runtime has none.</summary>
    public string? LikelyLocale { get; set; }
    /// <summary>The country's main IANA zone (CLDR primary or dominant metazone), or null for countries with none.</summary>
    public string? DefaultTimeZone { get; set; }
}

/// <summary>An IANA time zone (TimeZones table). Offsets here are informative; conversion uses the runtime's rules.</summary>
public sealed class TimeZoneEntry
{
    public string Id { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? WindowsId { get; set; }
    public int? StandardOffsetMinutes { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>A country locale with its CLDR date conventions (DateLocales table).</summary>
public sealed class DateLocaleEntry
{
    public string Code { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string ShortDatePattern { get; set; } = string.Empty;
    public string LongDatePattern { get; set; } = string.Empty;
    public string ShortTimePattern { get; set; } = string.Empty;
    public byte FirstDayOfWeek { get; set; }
    public string SampleLong { get; set; } = string.Empty;
    public string SampleShort { get; set; } = string.Empty;
}
