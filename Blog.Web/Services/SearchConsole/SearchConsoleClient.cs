using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blog.Core.Domain;
using Blog.Core.Services;

namespace Blog.Web.Services.SearchConsole;

/// <summary>
/// The only code that talks to Google. Two calls: exchange a signed assertion for an access token,
/// and page through the Search Analytics query for a property and date range. No SDK — the API is
/// two JSON endpoints, and a hand-written client is easier to reason about when it fails.
/// </summary>
public sealed class SearchConsoleClient
{
    public const int RowLimit = 25000; // API maximum per page

    private readonly HttpClient _http;
    private readonly ILogger<SearchConsoleClient> _log;

    public SearchConsoleClient(HttpClient http, ILogger<SearchConsoleClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<string> GetAccessTokenAsync(ServiceAccountCredential credential, CancellationToken ct)
    {
        var assertion = SearchConsoleJwt.Create(credential, DateTime.UtcNow);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        });
        using var resp = await _http.PostAsync(SearchConsoleJwt.TokenEndpoint, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new SearchConsoleException($"Token exchange failed (HTTP {(int)resp.StatusCode}): {Summarise(body)}");

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new SearchConsoleException("Token exchange returned no access_token.");
        return token;
    }

    /// <summary>
    /// Every (date, page, query) row for the property in the inclusive range. Pages through the API
    /// until a short page comes back. Search Console's "final" data state is requested so the same
    /// day is not stored twice with different numbers.
    /// </summary>
    public async Task<List<SearchPerformanceRow>> QueryAsync(string accessToken, string property,
        DateTime fromDate, DateTime toDate, CancellationToken ct)
    {
        var url = $"https://searchconsole.googleapis.com/webmasters/v3/sites/{SearchConsoleJwt.EncodeProperty(property)}/searchAnalytics/query";
        var rows = new List<SearchPerformanceRow>();
        var startRow = 0;

        while (true)
        {
            var payload = JsonSerializer.Serialize(new
            {
                startDate = fromDate.ToString("yyyy-MM-dd"),
                endDate = toDate.ToString("yyyy-MM-dd"),
                dimensions = new[] { "date", "page", "query" },
                type = "web",
                dataState = "final",
                rowLimit = RowLimit,
                startRow,
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new SearchConsoleException($"Search Analytics query failed (HTTP {(int)resp.StatusCode}): {Summarise(body)}");

            var page = Parse(body);
            rows.AddRange(page);
            if (page.Count < RowLimit) break;
            startRow += RowLimit;
        }

        _log.LogInformation("Search Console: {Rows} rows for {Property} {From:yyyy-MM-dd}..{To:yyyy-MM-dd}.", rows.Count, property, fromDate, toDate);
        return rows;
    }

    /// <summary>Parses a Search Analytics response. Pure; exposed for tests.</summary>
    public static List<SearchPerformanceRow> Parse(string json)
    {
        var result = new List<SearchPerformanceRow>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var r in rowsEl.EnumerateArray())
        {
            if (!r.TryGetProperty("keys", out var keys) || keys.GetArrayLength() < 3) continue;
            if (!DateTime.TryParse(keys[0].GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var date)) continue;

            result.Add(new SearchPerformanceRow
            {
                Date = date.Date,
                Page = keys[1].GetString() ?? string.Empty,
                Query = keys[2].GetString() ?? string.Empty,
                Clicks = (int)Math.Round(r.TryGetProperty("clicks", out var c) ? c.GetDouble() : 0),
                Impressions = (int)Math.Round(r.TryGetProperty("impressions", out var i) ? i.GetDouble() : 0),
                Ctr = r.TryGetProperty("ctr", out var ctr) ? ctr.GetDouble() : 0,
                Position = r.TryGetProperty("position", out var p) ? p.GetDouble() : 0,
            });
        }
        return result;
    }

    // Google error bodies are verbose JSON; keep the first line of the message for the job ledger.
    private static string Summarise(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)) return m.GetString() ?? body;
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? body;
            }
            if (doc.RootElement.TryGetProperty("error_description", out var d)) return d.GetString() ?? body;
        }
        catch { }
        return body.Length > 300 ? body[..300] : body;
    }
}

public sealed class SearchConsoleException : Exception
{
    public SearchConsoleException(string message) : base(message) { }
}
