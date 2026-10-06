using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Blog.Core.Services;

/// <summary>What a date is for: the call site picks the length, the site's locale picks the pattern.</summary>
public enum DateStyle
{
    /// <summary>"October 5, 2026" (en-US) · "5 October 2026" (en-GB) · "5. Oktober 2026" (de-DE).</summary>
    Long,
    /// <summary>"Oct 5, 2026" · "5 Oct 2026" · "05.10.2026" where the locale has no abbreviated month.</summary>
    Medium,
    /// <summary>"10/5/2026" · "05/10/2026" · "05.10.2026".</summary>
    Short,
    /// <summary>"Monday, October 5, 2026".</summary>
    LongWithWeekday,
    /// <summary>"October 2026".</summary>
    MonthYear,
    /// <summary>"Oct 5, 2026 2:30 PM" · "5 Oct 2026 14:30".</summary>
    DateTime,
    /// <summary>"10/5/2026 2:30:15 PM" · "05/10/2026 14:30:15" — logs and audit trails.</summary>
    DateTimeSeconds,
    /// <summary>"2:30 PM" · "14:30".</summary>
    Time,
    /// <summary>"Oct 5" · "5 Oct" — compact lists where the year is shown elsewhere.</summary>
    MonthDay,
    /// <summary>"Oct 2026".</summary>
    ShortMonthYear,
    /// <summary>"5" ("05" where the locale pads days) — a calendar-tile day number.</summary>
    Day,
    /// <summary>"2026".</summary>
    Year,
}

/// <summary>
/// A site's date settings (ADR-0027). <see cref="TimeZoneId"/> is an IANA id (Windows ids still resolve);
/// <see cref="Locale"/> is a BCP-47 culture such as "en-IN"; <see cref="Format"/> is "locale" or "iso8601";
/// <see cref="Clock"/> is "locale", "24h" or "12h".
/// </summary>
public sealed record DateDisplayOptions(string TimeZoneId, string Locale, string Format, string Clock)
{
    /// <summary>What a site gets before it chooses: UTC and US English, the formats most layouts used before.</summary>
    public static readonly DateDisplayOptions Default = new("UTC", "en-US", DateDisplay.FormatLocale, DateDisplay.ClockLocale);
}

/// <summary>
/// The one place human-readable dates are formatted (ADR-0027). The pattern comes from the locale's CLDR data
/// (the runtime's ICU), the instant is converted to the site's time zone, and ISO 8601 mode or a fixed clock
/// override the locale. Machine formats — <c>datetime</c> attributes, JSON-LD, feeds, sitemaps, headers,
/// exports — never go through here; they stay invariant.
/// </summary>
public static class DateDisplay
{
    public const string FormatLocale = "locale";
    public const string FormatIso = "iso8601";
    public const string ClockLocale = "locale";
    public const string Clock24 = "24h";
    public const string Clock12 = "12h";

    private static readonly ConcurrentDictionary<string, CultureInfo> Cultures = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownFormat(string? f) => f is FormatLocale or FormatIso;
    public static bool IsKnownClock(string? c) => c is ClockLocale or Clock24 or Clock12;

    /// <summary>
    /// The culture for a locale code, always on the Gregorian calendar: a site in Saudi Arabia or Thailand keeps
    /// its language and conventions but shows the same year as the rest of the web. Unknown codes fall back to en-US.
    /// </summary>
    public static CultureInfo CultureFor(string? locale)
    {
        var key = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
        return Cultures.GetOrAdd(key, static k =>
        {
            CultureInfo c;
            try
            {
                c = CultureInfo.GetCultureInfo(k, predefinedOnly: true);   // ICU would otherwise invent a culture for any code
                if (string.IsNullOrEmpty(c.Name) || c.IsNeutralCulture) c = CultureInfo.GetCultureInfo("en-US");
            }
            catch (CultureNotFoundException) { c = CultureInfo.GetCultureInfo("en-US"); }

            if (c.DateTimeFormat.Calendar is GregorianCalendar) return c;
            var clone = (CultureInfo)c.Clone();
            var gregorian = c.OptionalCalendars.OfType<GregorianCalendar>()
                .OrderBy(g => g.CalendarType == GregorianCalendarTypes.Localized ? 0 : 1).FirstOrDefault()
                ?? new GregorianCalendar();
            try { clone.DateTimeFormat.Calendar = gregorian; }
            catch (ArgumentOutOfRangeException) { clone.DateTimeFormat.Calendar = new GregorianCalendar(); }
            return CultureInfo.ReadOnly(clone);
        });
    }

    /// <summary>True when the runtime knows the locale as a specific (country) culture.</summary>
    public static bool IsKnownLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return false;
        try
        {
            var c = CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true);
            return !c.IsNeutralCulture && !string.IsNullOrEmpty(c.Name);
        }
        catch (CultureNotFoundException) { return false; }
    }

    /// <summary>The .NET format pattern for a style under the given options.</summary>
    public static string Pattern(DateStyle style, DateDisplayOptions o)
    {
        if (o.Format == FormatIso)
            return style switch
            {
                DateStyle.MonthYear => "yyyy-MM",
                DateStyle.DateTime => "yyyy-MM-dd HH:mm",
                DateStyle.DateTimeSeconds => "yyyy-MM-dd HH:mm:ss",
                DateStyle.Time => "HH:mm",
                DateStyle.MonthDay => "MM-dd",
                DateStyle.ShortMonthYear => "yyyy-MM",
                DateStyle.Day => "dd",
                DateStyle.Year => "yyyy",
                _ => "yyyy-MM-dd",
            };

        var f = CultureFor(o.Locale).DateTimeFormat;
        var longDate = StripWeekday(f.LongDatePattern);
        return style switch
        {
            DateStyle.Long => longDate,
            DateStyle.Medium => Abbreviate(longDate),
            DateStyle.Short => f.ShortDatePattern,
            DateStyle.LongWithWeekday => f.LongDatePattern.Contains("dddd", StringComparison.Ordinal) ? f.LongDatePattern : "dddd, " + longDate,
            DateStyle.MonthYear => f.YearMonthPattern,
            DateStyle.DateTime => Abbreviate(longDate) + " " + ApplyClock(f.ShortTimePattern, o.Clock),
            DateStyle.DateTimeSeconds => f.ShortDatePattern + " " + ApplyClock(f.LongTimePattern, o.Clock),
            DateStyle.Time => ApplyClock(f.ShortTimePattern, o.Clock),
            DateStyle.MonthDay => Abbreviate(f.MonthDayPattern),
            DateStyle.ShortMonthYear => Abbreviate(f.YearMonthPattern),
            DateStyle.Day => f.ShortDatePattern.Contains("dd", StringComparison.Ordinal) ? "dd" : "%d",
            DateStyle.Year => "yyyy",
            _ => longDate,
        };
    }

    /// <summary>A UTC instant as the site's readers should see it. Null is an empty string.</summary>
    public static string Format(DateTime? utc, DateDisplayOptions o, DateStyle style)
    {
        if (utc is null) return string.Empty;
        var local = TimeZoneHelper.ToDisplay(utc.Value, o.TimeZoneId);
        return local.ToString(Pattern(style, o), o.Format == FormatIso ? CultureInfo.InvariantCulture : CultureFor(o.Locale));
    }

    /// <summary>A calendar date with no time of day (a custom field, a birthday): no time-zone conversion.</summary>
    public static string Format(DateOnly date, DateDisplayOptions o, DateStyle style = DateStyle.Long) =>
        date.ToDateTime(TimeOnly.MinValue).ToString(Pattern(style is DateStyle.DateTime or DateStyle.DateTimeSeconds or DateStyle.Time ? DateStyle.Long : style, o),
            o.Format == FormatIso ? CultureInfo.InvariantCulture : CultureFor(o.Locale));

    /// <summary>The local calendar date in the site's zone, ISO 8601, for <c>datetime</c> attributes.</summary>
    public static string IsoDate(DateTime? utc, DateDisplayOptions o) =>
        utc is null ? string.Empty : TimeZoneHelper.ToDisplay(utc.Value, o.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A stored UTC instant as ISO 8601 with its zone ("2026-10-05T14:30:00.0000000Z") for structured data and
    /// Open Graph. Storage hands back unspecified kinds; "O" on those omits the offset and readers guess.
    /// </summary>
    public static string IsoUtc(DateTime utc) =>
        (utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>The year in the site's zone (footers: "© 2026").</summary>
    public static int Year(DateTime utc, DateDisplayOptions o) => TimeZoneHelper.ToDisplay(utc, o.TimeZoneId).Year;

    // ── pattern surgery (outside quoted literals only) ─────────────────────────────

    /// <summary>Space, comma, no-break space and the Arabic comma: what separates a weekday from the date.</summary>
    private static readonly char[] Separators = { ' ', ',', ' ', '،' };

    /// <summary>A run of non-format characters before the first space ("ที่ d MMMM" → "d MMMM").</summary>
    private static readonly System.Text.RegularExpressions.Regex LeadingQualifier =
        new(@"^[^\sdMyg.,']+\s+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"dddd, MMMM d, yyyy" → "MMMM d, yyyy"; "dddd d MMMM yyyy" → "d MMMM yyyy"; "yyyy年M月d日dddd" → "yyyy年M月d日".</summary>
    public static string StripWeekday(string pattern)
    {
        var parts = Tokenize(pattern);
        var i = parts.FindIndex(p => !p.Quoted && p.Text.Contains("dddd", StringComparison.Ordinal));
        if (i < 0) return pattern;
        var t = parts[i].Text;
        var at = t.IndexOf("dddd", StringComparison.Ordinal);
        var before = t[..at];
        var after = t[(at + 4)..];
        var leading = before.Trim(Separators).Length == 0 && parts.Take(i).All(p => p.Text.Trim().Length == 0);
        if (leading)
        {
            // Drop the separator after a leading weekday, and an unquoted word that only qualified it
            // (Thai "ที่"): anything up to the first space that is not a date format letter.
            after = after.TrimStart(Separators).TrimStart('.');
            after = LeadingQualifier.Replace(after, "");
        }
        else
        {
            before = before.TrimEnd(Separators);
        }
        parts[i] = (before + after, false);
        // A quoted literal that only qualified a leading weekday (Danish "'den '") goes with it.
        if (leading && after.Trim().Length == 0 && i + 1 < parts.Count && parts[i + 1].Quoted)
        {
            parts.RemoveAt(i + 1);
            if (i + 1 < parts.Count && !parts[i + 1].Quoted) parts[i + 1] = (parts[i + 1].Text.TrimStart(Separators), false);
        }
        var result = string.Concat(parts.Select(p => p.Quoted ? "'" + p.Text + "'" : p.Text)).Trim(Separators);
        return result.Length == 0 ? pattern : result;
    }

    /// <summary>Full month names become abbreviated ones ("MMMM" → "MMM"); other parts are left alone.</summary>
    public static string Abbreviate(string pattern) => MapUnquoted(pattern, t => t.Replace("MMMM", "MMM", StringComparison.Ordinal));

    /// <summary>Forces a 24-hour or 12-hour clock on a time pattern; "locale" keeps the culture's own.</summary>
    public static string ApplyClock(string pattern, string? clock)
    {
        if (clock == Clock24)
        {
            // A 24-hour clock pads the hour ("09:30"); a locale's own "H:mm" is left as it is.
            var p = MapUnquoted(pattern, t => System.Text.RegularExpressions.Regex.Replace(t, "h+", "HH").Replace("tt", "").Replace("t", ""));
            return CollapseSpaces(p);
        }
        if (clock == Clock12)
        {
            // A 12-hour clock is not zero-padded ("2:30 pm", not "02:30 pm").
            var p = MapUnquoted(pattern, t => t.Replace("HH", "h").Replace("hh", "h").Replace('H', 'h'));
            return Tokenize(p).Any(x => !x.Quoted && x.Text.Contains('t')) ? p : p + " tt";
        }
        return pattern;
    }

    private static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
            if (!(ch == ' ' && sb.Length > 0 && sb[^1] == ' ')) sb.Append(ch);
        return sb.ToString().Trim();
    }

    private static string MapUnquoted(string pattern, Func<string, string> map) =>
        string.Concat(Tokenize(pattern).Select(p => p.Quoted ? "'" + p.Text + "'" : map(p.Text)));

    private static List<(string Text, bool Quoted)> Tokenize(string pattern)
    {
        var parts = new List<(string, bool)>();
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in pattern)
        {
            if (ch == '\'')
            {
                parts.Add((sb.ToString(), quoted));
                sb.Clear();
                quoted = !quoted;
                continue;
            }
            sb.Append(ch);
        }
        parts.Add((sb.ToString(), quoted));
        return parts.Where(p => p.Item1.Length > 0 || p.Item2).ToList();
    }
}
