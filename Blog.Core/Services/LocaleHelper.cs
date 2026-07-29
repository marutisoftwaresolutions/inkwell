namespace Blog.Core.Services;

public static class LocaleHelper
{
    /// <summary>
    /// Converts a BCP-47 content language (e.g. "en", "es", "fr-CA") into an Open Graph locale
    /// (language_TERRITORY, e.g. "en_US"). A code that already carries a region just swaps '-'→'_';
    /// common bare codes map to a sensible default territory; anything else is returned as-is.
    /// </summary>
    public static string OgLocale(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "en_US";
        lang = lang.Trim();
        if (lang.Contains('-')) return lang.Replace('-', '_');
        return lang.ToLowerInvariant() switch
        {
            "en" => "en_US",
            "es" => "es_ES",
            "hi" => "hi_IN",
            "fr" => "fr_FR",
            "pt" => "pt_BR",
            "de" => "de_DE",
            "it" => "it_IT",
            "zh" => "zh_CN",
            "ja" => "ja_JP",
            "ar" => "ar_AR",
            _ => lang
        };
    }
}
