using Blog.Core.Domain;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Blog.Web.Controllers;

/// <summary>
/// Receives the page-view beacon sent by <c>wwwroot/js/inkwell-views.js</c> and hands a row to
/// <see cref="TrafficRecorder"/>. Always answers 204 with no body, whatever it decides, so the
/// response tells a caller nothing about why a beacon was or was not counted.
///
/// Anonymous and without an anti-forgery token by design: <c>navigator.sendBeacon</c> cannot send
/// one, and the signed page token (<see cref="PageViewBeaconService"/>) is what proves the beacon
/// came from a page this site served. Nothing here writes to the database on the request.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class PageViewBeaconController : Controller
{
    private readonly PageViewBeaconService _beacon;
    private readonly TrafficRecorder _recorder;
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;
    private readonly IpHasher _ipHasher;
    private readonly ILogger<PageViewBeaconController> _log;

    public PageViewBeaconController(PageViewBeaconService beacon, TrafficRecorder recorder,
        Blog.Web.Services.Security.IpFirewallService firewall, IpHasher ipHasher, ILogger<PageViewBeaconController> log)
    {
        _ipHasher = ipHasher;
        _beacon = beacon;
        _recorder = recorder;
        _firewall = firewall;
        _log = log;
    }

    [HttpPost("/pv")]
    [RequestSizeLimit(8 * 1024)]
    public IActionResult Record([FromForm(Name = "t")] string? token, [FromForm(Name = "p")] string? path,
        [FromForm(Name = "q")] string? query, [FromForm(Name = "r")] string? referrer)
    {
        try
        {
            if (User.Identity?.IsAuthenticated == true) return NoContent();

            var userAgent = Request.Headers.UserAgent.ToString();
            if (BotUserAgent.IsBot(userAgent)) return NoContent();
            if (!IsSameSite()) return NoContent();

            var validPath = _beacon.Validate(token, path);
            if (validPath is null) return NoContent();

            // Behind a trusted proxy (Cloudflare) the connection is the proxy; the firewall resolves the client.
            var ip = _firewall.ResolveClientIp(HttpContext);
            if (!_beacon.TryConsumeBudget(ip)) return NoContent();

            var queryCollection = new QueryCollection(QueryHelpers.ParseQuery(Trim(query, 1000)));
            referrer = Trim(referrer, 1000);
            var (source, medium) = VisitSourceClassifier.Classify(string.IsNullOrEmpty(referrer) ? null : referrer, queryCollection);

            _recorder.RecordPageView(new PageView
            {
                OwnerId   = TrafficRecorder.OwnerOf(HttpContext),
                Path      = validPath,
                Referrer  = string.IsNullOrEmpty(referrer) ? null : referrer,
                Source    = Trim(source, 100),
                Medium    = Trim(medium, 100),
                Campaign  = Param(queryCollection, "utm_campaign"),
                Term      = Param(queryCollection, "utm_term"),
                Content   = Param(queryCollection, "utm_content"),
                IpHash    = _ipHasher.Hash(ip),
                UserAgent = userAgent.Length > 500 ? userAgent[..500] : userAgent,
                CreatedAt = DateTime.UtcNow
            }, ip);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Page view beacon skipped (non-fatal).");
        }
        return NoContent();
    }

    /// <summary>
    /// Browsers send Origin on a beacon POST and Sec-Fetch-Site on every request. When present they
    /// must name this site; a client that sends neither is judged by the page token alone.
    /// </summary>
    private bool IsSameSite()
    {
        var fetchSite = Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0 && fetchSite != "same-origin" && fetchSite != "same-site") return false;

        var origin = Request.Headers.Origin.ToString();
        if (origin.Length == 0 || origin == "null") return origin.Length == 0;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Param(QueryCollection q, string name) =>
        q.TryGetValue(name, out var v) && v.Count > 0 ? Trim(v.ToString(), 200) : null;

    private static string Trim(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length > max ? s[..max] : s;
}
