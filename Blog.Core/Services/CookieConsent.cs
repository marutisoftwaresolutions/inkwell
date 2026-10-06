using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>What the public layout does about cookies, from two settings.</summary>
public enum CookieConsentMode
{
    /// <summary>Consent banner turned off: Google Analytics (if set) loads on every page, as before v1.0.8.</summary>
    Off,
    /// <summary>Banner with Accept / Reject; Google Analytics loads only after the reader accepts.</summary>
    AskForAnalytics,
    /// <summary>Banner on, but nothing optional is configured: a notice that only essential cookies are used.</summary>
    EssentialOnlyNotice,
}

/// <summary>
/// Cookie consent for the reader surface. The choice lives in the reader's browser (a first-party
/// cookie read by <c>wwwroot/js/inkwell-consent.js</c>), so the server renders the same HTML for
/// every reader and HTTP caching is unaffected. Google Analytics is the only optional, non-essential
/// script the platform emits; the session, anti-forgery and consent cookies are essential.
/// </summary>
public static class CookieConsent
{
    /// <summary>Name of the first-party cookie holding <c>granted</c> or <c>denied</c>.</summary>
    public const string CookieName = "inkwell_consent";

    /// <summary>How long a choice is remembered before the reader is asked again (well under the 13 months regulators allow).</summary>
    public const int RememberDays = 180;

    private static readonly Regex AnalyticsIdPattern =
        new(@"^(G|UA|GT|AW)-[A-Z0-9-]{4,24}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// True for a Google tag id (<c>G-XXXXXXX</c>, <c>UA-1234-1</c>, …). Anything else is ignored rather
    /// than emitted, because the id is written into a script URL and a data attribute.
    /// </summary>
    public static bool IsValidAnalyticsId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && AnalyticsIdPattern.IsMatch(id.Trim());

    public static CookieConsentMode Decide(bool bannerEnabled, string? analyticsId)
    {
        if (!bannerEnabled) return CookieConsentMode.Off;
        return IsValidAnalyticsId(analyticsId) ? CookieConsentMode.AskForAnalytics : CookieConsentMode.EssentialOnlyNotice;
    }
}
