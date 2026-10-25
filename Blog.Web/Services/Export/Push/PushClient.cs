using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blog.Core.Services.Export;
using Blog.Core.Services.Export.Push;

namespace Blog.Web.Services.Export.Push;


/// <summary>What the target said before a push. <see cref="Field"/> names the form field the problem concerns.</summary>
public sealed record PushPreflight(bool Ok, string? SiteName, string? Version, string? Error, string? Field = null);

/// <summary>
/// A failed call to the target. <see cref="StatusCode"/> is null for network failures. Messages never contain
/// the credential: they are built from the status and the target's own error text.
/// </summary>
public sealed class PushException : Exception
{
    public PushException(string message, int? statusCode = null, TimeSpan? retryAfter = null) : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public int? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
    public bool IsThrottle => StatusCode is int s && PushRules.IsThrottle(s);
    public bool IsAuthFailure => StatusCode is int s && PushRules.IsAuthFailure(s);
}

/// <summary>One target site, signed in. An interface so tests can stand in for WordPress and Ghost.</summary>
public interface IPushClient
{
    /// <summary>Checks the address, the credential and the permissions a push needs. Never throws for a bad answer.</summary>
    Task<PushPreflight> PreflightAsync(bool needPages, CancellationToken ct);

    /// <summary>The post or page with this slug on the target, in any status, or null.</summary>
    Task<RemoteItem?> FindAsync(PushItemKind kind, string slug, CancellationToken ct);

    /// <summary>Creates the item, or updates <paramref name="existing"/> when given.</summary>
    Task<RemoteItem> SaveAsync(PushItemKind kind, JsonObject body, RemoteItem? existing, CancellationToken ct);

    /// <summary>Uploads one image; returns its id (WordPress media id; Ghost has none, so its address) and address.</summary>
    Task<RemoteItem> UploadImageAsync(string fileName, string contentType, Stream content, string? alt, string? caption, CancellationToken ct);

    /// <summary>The id of a WordPress category or tag with this slug, created when missing. Ghost creates tags inline.</summary>
    Task<string> EnsureTermAsync(PushTerm term, CancellationToken ct);
}

public interface IPushClientFactory
{
    /// <summary>True when the server allows pushing to local and private addresses over HTTP (testing only).</summary>
    bool AllowPrivateTargets { get; }
    IPushClient Create(ExportTarget target, string siteUrl, PushCredential credential);
}

/// <summary>
/// Builds clients over one shared <see cref="HttpClient"/> that connects only to public addresses and follows
/// no redirects (<see cref="PublicNetwork"/>), unless <c>Exports:PushAllowPrivateTargets</c> is set for testing.
/// </summary>
public sealed class PushClientFactory : IPushClientFactory
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private readonly HttpClient _http;

    public PushClientFactory(IConfiguration config)
    {
        AllowPrivateTargets = config.GetValue("Exports:PushAllowPrivateTargets", false);
        _http = new HttpClient(PublicNetwork.Handler(AllowPrivateTargets)) { Timeout = RequestTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Inkwell-Push/1.0");
    }

    public bool AllowPrivateTargets { get; }

    public IPushClient Create(ExportTarget target, string siteUrl, PushCredential credential) => target == ExportTarget.WordPress
        ? new WordPressPushClient(_http, siteUrl, credential)
        : new GhostPushClient(_http, siteUrl, credential);
}

/// <summary>The credential of each running push, in this process's memory only (ADR-0029). Lost on restart by design.</summary>
public sealed class PushCredentialStore
{
    private readonly ConcurrentDictionary<Guid, PushCredential> _byPush = new();
    public void Set(Guid pushId, PushCredential credential) => _byPush[pushId] = credential;
    public PushCredential? Get(Guid pushId) => _byPush.TryGetValue(pushId, out var c) ? c : null;
    public void Forget(Guid pushId) => _byPush.TryRemove(pushId, out _);
}

/// <summary>Shared request handling: JSON in and out, and every non-success answer turned into a <see cref="PushException"/>.</summary>
internal abstract class PushClientBase
{
    protected readonly HttpClient Http;
    protected readonly string Site;

    protected PushClientBase(HttpClient http, string siteUrl)
    {
        Http = http;
        Site = siteUrl.TrimEnd('/');
    }

    protected abstract string Product { get; }
    protected abstract void Authorize(HttpRequestMessage request);

    protected async Task<JsonNode?> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct, bool authorize = true, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authorize) Authorize(request);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PushException($"{Product} did not answer within {PushClientFactory.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            var m = ex.InnerException?.Message ?? ex.Message;
            throw new PushException($"Could not reach {Host}: {PushRules.Trim(m, 300)}");
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(ct);
            if (allowNotFound && code == 404) return null;
            if (code is >= 200 and < 300)
            {
                if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
                try { return JsonNode.Parse(text); }
                catch (JsonException) { throw new PushException($"{Product} answered with something other than JSON. Check that the address is the site itself.", code); }
            }
            if (code is >= 300 and < 400)
            {
                var to = response.Headers.Location?.ToString();
                throw new PushException($"{Product} redirected the request{(to is null ? "" : " to " + PushRules.Trim(to, 200))}. Enter the address the site redirects to.", code);
            }
            var retry = PushRules.IsThrottle(code)
                ? PushRules.RetryAfter(response.Headers.RetryAfter?.ToString(), DateTimeOffset.UtcNow)
                : (TimeSpan?)null;
            throw new PushException(Describe(code, text), code, retry);
        }
    }

    protected string Host => Uri.TryCreate(Site, UriKind.Absolute, out var u) ? u.Host : Site;

    /// <summary>The target's own error text when it sent one (WordPress <c>message</c>, Ghost <c>errors[0].message</c>).</summary>
    private string Describe(int code, string body)
    {
        string? detail = null;
        try
        {
            var json = JsonNode.Parse(body);
            detail = json?["message"]?.GetValue<string>()
                     ?? json?["errors"]?[0]?["message"]?.GetValue<string>();
            var context = json?["errors"]?[0]?["context"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(context)) detail = $"{detail} {context}";
        }
        catch { /* not JSON */ }
        var prefix = code switch
        {
            401 => $"{Product} did not accept the credential",
            403 => $"The {Product} account is not allowed to do this",
            404 => $"{Product} did not find the address",
            429 => $"{Product} asked us to slow down",
            _ => $"{Product} answered HTTP {code}",
        };
        return PushRules.Trim(string.IsNullOrWhiteSpace(detail) ? prefix + "." : $"{prefix}: {detail}", 480)!;
    }

    protected static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}

/// <summary>
/// WordPress REST API (<c>/wp/v2</c>) with an Application Password over HTTP Basic. The API root is found once
/// per client: <c>/wp-json/</c>, or <c>/?rest_route=</c> on sites without pretty permalinks.
/// </summary>
internal sealed class WordPressPushClient : PushClientBase, IPushClient
{
    private readonly string _basic;
    private bool? _prettyRoot;

    public WordPressPushClient(HttpClient http, string siteUrl, PushCredential credential) : base(http, siteUrl)
    {
        _basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Secret}"));
    }

    protected override string Product => "WordPress";
    protected override void Authorize(HttpRequestMessage request) => request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basic);

    private string Api(string route, string? query = null) => _prettyRoot == false
        ? $"{Site}/?rest_route=/{route}{(query is null ? "" : "&" + query)}"
        : $"{Site}/wp-json/{route}{(query is null ? "" : "?" + query)}";

    private async Task<JsonNode?> DiscoverAsync(CancellationToken ct)
    {
        if (_prettyRoot is not null) return null;
        foreach (var pretty in new[] { true, false })
        {
            _prettyRoot = pretty;
            try
            {
                var root = await SendAsync(HttpMethod.Get, Api(""), null, ct, allowNotFound: true);
                if (root?["namespaces"] is JsonArray ns && ns.Any(n => n?.GetValue<string>() == "wp/v2")) return root;
            }
            catch (PushException ex) when (ex.StatusCode is >= 200 and < 300)
            {
                // Not this root; try the other.
            }
        }
        _prettyRoot = null;
        throw new PushException("No WordPress API was found at this address. Check the address, and that the REST API is not switched off by a plugin.");
    }

    public async Task<PushPreflight> PreflightAsync(bool needPages, CancellationToken ct)
    {
        try
        {
            _prettyRoot = null;
            var root = await DiscoverAsync(ct);
            var me = await SendAsync(HttpMethod.Get, Api("wp/v2/users/me", "context=edit"), null, ct);
            var caps = me?["capabilities"] as JsonObject;
            bool Has(string cap) => caps?[cap]?.GetValue<bool>() == true;
            var needed = new List<(string Cap, string Plain)>
            {
                ("edit_posts", "write posts"), ("publish_posts", "publish posts"), ("upload_files", "upload images"),
                ("manage_categories", "create categories and tags"),
            };
            if (needPages) needed.AddRange(new[] { ("edit_pages", "write pages"), ("publish_pages", "publish pages") });
            var missing = needed.Where(n => !Has(n.Cap)).Select(n => n.Plain).ToList();
            if (missing.Count > 0)
                return new PushPreflight(false, null, null,
                    $"The WordPress user “{me?["name"]?.GetValue<string>()}” cannot {string.Join(", ", missing)}. Use an Administrator or Editor account.", "pushUser");
            return new PushPreflight(true, root?["name"]?.GetValue<string>(), null, null);
        }
        catch (PushException ex) when (ex.StatusCode == 401)
        {
            return new PushPreflight(false, null, null,
                "WordPress did not accept the user name and application password. Create an application password under Users → Profile → Application Passwords, and use the user name, not the email address.", "pushSecret");
        }
        catch (PushException ex)
        {
            return new PushPreflight(false, null, null, ex.Message, ex.IsAuthFailure ? "pushUser" : "pushSite");
        }
    }

    private static string Route(PushItemKind kind) => kind == PushItemKind.Page ? "wp/v2/pages" : "wp/v2/posts";

    public async Task<RemoteItem?> FindAsync(PushItemKind kind, string slug, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        var found = await SendAsync(HttpMethod.Get,
            Api(Route(kind), $"slug={Uri.EscapeDataString(slug)}&status=any&context=edit&_fields=id,link"), null, ct);
        var first = (found as JsonArray)?.FirstOrDefault();
        return first is null ? null : new RemoteItem(first["id"]!.ToString(), first["link"]?.GetValue<string>());
    }

    public async Task<RemoteItem> SaveAsync(PushItemKind kind, JsonObject body, RemoteItem? existing, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        var route = existing is null ? Route(kind) : $"{Route(kind)}/{existing.Id}";
        var saved = await SendAsync(HttpMethod.Post, Api(route), Json(body), ct)
                    ?? throw new PushException("WordPress returned an empty answer.");
        return new RemoteItem(saved["id"]!.ToString(), saved["link"]?.GetValue<string>());
    }

    public async Task<RemoteItem> UploadImageAsync(string fileName, string contentType, Stream content, string? alt, string? caption, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName, FileName = $"\"{AsciiName(fileName)}\"" };
        var media = await SendAsync(HttpMethod.Post, Api("wp/v2/media"), body, ct)
                    ?? throw new PushException("WordPress returned an empty answer to an upload.");
        var id = media["id"]!.ToString();
        if (!string.IsNullOrWhiteSpace(alt) || !string.IsNullOrWhiteSpace(caption))
        {
            var meta = new JsonObject();
            if (!string.IsNullOrWhiteSpace(alt)) meta["alt_text"] = alt;
            if (!string.IsNullOrWhiteSpace(caption)) meta["caption"] = caption;
            await SendAsync(HttpMethod.Post, Api($"wp/v2/media/{id}"), Json(meta), ct);
        }
        return new RemoteItem(id, media["source_url"]?.GetValue<string>());
    }

    public async Task<string> EnsureTermAsync(PushTerm term, CancellationToken ct)
    {
        await DiscoverAsync(ct);
        var route = "wp/v2/" + term.Taxonomy;
        var found = await SendAsync(HttpMethod.Get, Api(route, $"slug={Uri.EscapeDataString(term.Slug)}&_fields=id"), null, ct);
        if ((found as JsonArray)?.FirstOrDefault() is JsonNode hit) return hit["id"]!.ToString();
        try
        {
            var created = await SendAsync(HttpMethod.Post, Api(route), Json(new JsonObject { ["name"] = term.Name, ["slug"] = term.Slug }), ct);
            return created!["id"]!.ToString();
        }
        catch (PushException ex) when (ex.StatusCode == 400 && ex.Message.Contains("exists", StringComparison.OrdinalIgnoreCase))
        {
            // A term with this name but another slug: WordPress refuses a second one, so reuse it by name.
            var byName = await SendAsync(HttpMethod.Get, Api(route, $"search={Uri.EscapeDataString(term.Name)}&_fields=id,name"), null, ct);
            var same = (byName as JsonArray)?.FirstOrDefault(n => string.Equals(WebUtility.HtmlDecode(n?["name"]?.GetValue<string>()), term.Name, StringComparison.OrdinalIgnoreCase));
            var reused = same?["id"]?.ToString();
            if (reused is null) throw;
            return reused;
        }
    }

    private static string AsciiName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name) sb.Append(ch is >= ' ' and < (char)127 and not '"' and not '\\' ? ch : '_');
        return sb.ToString();
    }
}

/// <summary>
/// Ghost Admin API (<c>/ghost/api/admin/</c>) with a short-lived token signed by the Admin API key
/// (<see cref="PushRules.GhostToken"/>). Bodies are sent as HTML (<c>?source=html</c>) so Ghost converts them itself.
/// </summary>
internal sealed class GhostPushClient : PushClientBase, IPushClient
{
    private readonly string _keyId;
    private readonly byte[] _secret;

    public GhostPushClient(HttpClient http, string siteUrl, PushCredential credential) : base(http, siteUrl)
    {
        var (id, secret, error) = PushRules.ParseGhostKey(credential.Secret);
        if (error is not null) throw new PushException(error);
        _keyId = id!;
        _secret = secret!;
    }

    protected override string Product => "Ghost";

    protected override void Authorize(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Ghost", PushRules.GhostToken(_keyId, _secret, DateTimeOffset.UtcNow));
        request.Headers.TryAddWithoutValidation("Accept-Version", "v6.0");
    }

    private string Api(string route) => $"{Site}/ghost/api/admin/{route}";
    private static string Route(PushItemKind kind) => kind == PushItemKind.Page ? "pages" : "posts";

    public async Task<PushPreflight> PreflightAsync(bool needPages, CancellationToken ct)
    {
        try
        {
            var site = await SendAsync(HttpMethod.Get, Api("site/"), null, ct, authorize: false, allowNotFound: true);
            if (site?["site"] is not JsonObject info)
                return new PushPreflight(false, null, null, "No Ghost Admin API was found at this address. Enter the site's own address (the one in Ghost Admin → Settings → General).", "pushSite");
            await SendAsync(HttpMethod.Get, Api("posts/?limit=1&fields=id"), null, ct);
            return new PushPreflight(true, info["title"]?.GetValue<string>(), info["version"]?.GetValue<string>(), null);
        }
        catch (PushException ex) when (ex.IsAuthFailure)
        {
            return new PushPreflight(false, null, null,
                "Ghost did not accept the Admin API key. Copy it again from Ghost Admin → Settings → Integrations → your custom integration.", "pushSecret");
        }
        catch (PushException ex)
        {
            return new PushPreflight(false, null, null, ex.Message, "pushSite");
        }
    }

    public async Task<RemoteItem?> FindAsync(PushItemKind kind, string slug, CancellationToken ct)
    {
        var found = await SendAsync(HttpMethod.Get, Api($"{Route(kind)}/slug/{Uri.EscapeDataString(slug)}/?fields=id,url,updated_at"),
            null, ct, allowNotFound: true);
        var item = found?[Route(kind)]?[0];
        return item is null ? null : new RemoteItem(item["id"]!.GetValue<string>(), item["url"]?.GetValue<string>(), item["updated_at"]?.GetValue<string>());
    }

    public async Task<RemoteItem> SaveAsync(PushItemKind kind, JsonObject body, RemoteItem? existing, CancellationToken ct)
    {
        var route = Route(kind);
        var saved = existing is null
            ? await SendAsync(HttpMethod.Post, Api($"{route}/?source=html"), Json(body), ct)
            : await SendAsync(HttpMethod.Put, Api($"{route}/{existing.Id}/?source=html"), Json(body), ct);
        var item = saved?[route]?[0] ?? throw new PushException("Ghost returned an empty answer.");
        return new RemoteItem(item["id"]!.GetValue<string>(), item["url"]?.GetValue<string>(), item["updated_at"]?.GetValue<string>());
    }

    public async Task<RemoteItem> UploadImageAsync(string fileName, string contentType, Stream content, string? alt, string? caption, CancellationToken ct)
    {
        var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent("image"), "purpose");
        var uploaded = await SendAsync(HttpMethod.Post, Api("images/upload/"), form, ct);
        var url = uploaded?["images"]?[0]?["url"]?.GetValue<string>() ?? throw new PushException("Ghost returned no address for an uploaded image.");
        return new RemoteItem(url, url);
    }

    public Task<string> EnsureTermAsync(PushTerm term, CancellationToken ct) =>
        throw new NotSupportedException("Ghost creates tags with the post.");
}
