using System.Text;
using System.Text.Json;
using Blog.Core.Interfaces;

namespace Blog.Web.Services;

/// <summary>
/// Submits changed URLs to the IndexNow protocol (https://www.indexnow.org) so participating
/// search engines (Bing, Yandex, Seznam, Naver) re-crawl within minutes instead of days.
/// Best-effort and non-throwing: any failure is logged and swallowed so it never blocks a publish.
/// Reads the per-tenant key/enabled flag from the site settings; a disabled flag or empty key = no-op.
/// </summary>
public sealed class IndexNowService
{
    private const string Endpoint = "https://api.indexnow.org/indexnow";

    private readonly HttpClient _http;
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly ILogger<IndexNowService> _log;

    public IndexNowService(HttpClient http, ISettingRepository settings, IUserRepository users, ILogger<IndexNowService> log)
    {
        _http = http;
        _settings = settings;
        _users = users;
        _log = log;
    }

    /// <summary>Submit one absolute URL (e.g. https://host/slug). Safe to call unconditionally.</summary>
    public Task SubmitAsync(string absoluteUrl) => SubmitAsync(new[] { absoluteUrl });

    public async Task SubmitAsync(IEnumerable<string> absoluteUrls)
    {
        try
        {
            var urls = absoluteUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            if (urls is null || urls.Count == 0) return;

            var admin = await _users.GetFirstAdminAsync();
            if (admin is null) return;

            var s = await _settings.GetSettingsAsync(admin.Id);
            if (!s.IndexNowEnabled || string.IsNullOrWhiteSpace(s.IndexNowApiKey)) return;

            if (!Uri.TryCreate(urls[0], UriKind.Absolute, out var first)) return;
            var host = first.Host;
            var key = s.IndexNowApiKey.Trim();

            var payload = new
            {
                host,
                key,
                keyLocation = $"{first.Scheme}://{host}/{key}.txt",
                urlList = urls
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(Endpoint, content);
            _log.LogInformation("IndexNow submitted {Count} URL(s) to {Host} -> HTTP {Code}", urls.Count, host, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IndexNow submission failed (non-fatal)");
        }
    }
}
