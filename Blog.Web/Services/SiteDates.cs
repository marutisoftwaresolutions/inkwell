using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>
/// Every human-readable date in a view goes through here (ADR-0027): <c>@Dates.Long(post.PublishedAt)</c>.
/// It reads the site's date settings once per request (the same owner the layouts read settings for) and
/// hands them to <see cref="DateDisplay"/>. Machine formats — <c>datetime</c> attributes via <see cref="Iso"/>,
/// JSON-LD, feeds, headers — stay invariant. Fails open: unreadable settings mean UTC and US English.
/// </summary>
public sealed class SiteDates
{
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;
    private DateDisplayOptions? _options;

    public SiteDates(ISettingRepository settings, IUserRepository users, ITenantContext tenant, IHttpContextAccessor http)
    {
        _settings = settings;
        _users = users;
        _tenant = tenant;
        _http = http;
    }

    /// <summary>The site's date options for this request.</summary>
    public DateDisplayOptions Options => _options ??= Load();

    /// <summary>Options straight from a settings object (controllers that already hold it; Settings previews).</summary>
    public static DateDisplayOptions From(UserSettings s) => new(
        TimeZoneHelper.ToIana(s.DisplayTimeZoneId),
        DateDisplay.IsKnownLocale(s.DateLocale) ? s.DateLocale : DateDisplayOptions.Default.Locale,
        DateDisplay.IsKnownFormat(s.DateFormat) ? s.DateFormat : DateDisplay.FormatLocale,
        DateDisplay.IsKnownClock(s.ClockFormat) ? s.ClockFormat : DateDisplay.ClockLocale);

    private DateDisplayOptions Load()
    {
        try
        {
            Guid owner;
            if (_tenant.IsCloudMode && _tenant.IsResolved) owner = _tenant.UserId;
            else if (_tenant.IsCloudMode && Guid.TryParse(_http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var me)) owner = me;
            else owner = _users.GetFirstAdminAsync().GetAwaiter().GetResult()?.Id ?? Guid.Empty;
            return From(_settings.GetSettingsAsync(owner).GetAwaiter().GetResult());
        }
        catch { return DateDisplayOptions.Default; }
    }

    public string Long(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Long);
    public string Medium(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Medium);
    public string Short(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Short);
    public string LongWithWeekday(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.LongWithWeekday);
    public string MonthYear(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.MonthYear);
    public string DateTime(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.DateTime);
    public string DateTimeSeconds(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.DateTimeSeconds);
    public string Time(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Time);
    public string MonthDay(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.MonthDay);
    public string ShortMonthYear(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.ShortMonthYear);
    public string Day(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Day);
    public string YearOf(DateTime? utc) => DateDisplay.Format(utc, Options, DateStyle.Year);
    public string Date(DateOnly date) => DateDisplay.Format(date, Options);

    /// <summary>
    /// A calendar date held in a DateTime (stored as midnight: "last verified", "next review", Search Console's
    /// "data through", daily buckets). Not converted between zones: 5 October stays 5 October everywhere.
    /// </summary>
    public string CalendarDate(DateTime? date, DateStyle style = DateStyle.Long) =>
        date is null ? string.Empty : DateDisplay.Format(DateOnly.FromDateTime(date.Value), Options, style);

    /// <summary>A time already in the site's zone (a schedule the editor converted for display): formatted, not converted.</summary>
    public string Wall(DateTime? local, DateStyle style = DateStyle.DateTime) =>
        local is null ? string.Empty : DateDisplay.Format(System.DateTime.SpecifyKind(local.Value, DateTimeKind.Utc), Options with { TimeZoneId = TimeZoneHelper.Utc }, style);

    /// <summary>The local calendar date as ISO 8601 for a <c>datetime</c> attribute.</summary>
    public string Iso(DateTime? utc) => DateDisplay.IsoDate(utc, Options);

    /// <summary>The full instant as ISO 8601 with the site's offset, for <c>datetime</c> on a time ("2026-10-05T14:30:00+05:30").</summary>
    public string IsoInstant(DateTime? utc)
    {
        if (utc is null) return string.Empty;
        var asUtc = utc.Value.Kind == DateTimeKind.Utc ? utc.Value : System.DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(asUtc), TimeZoneHelper.Resolve(Options.TimeZoneId))
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The current year in the site's zone (footers).</summary>
    public int Year => DateDisplay.Year(System.DateTime.UtcNow, Options);

    /// <summary>Now, in the site's zone, as the given style (the date in a masthead).</summary>
    public string Today(DateStyle style = DateStyle.LongWithWeekday) => DateDisplay.Format(System.DateTime.UtcNow, Options, style);
}
