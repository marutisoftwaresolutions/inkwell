using System.Runtime.CompilerServices;
using System.Security.Claims;
using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Blog.Web.Controllers;

/// <summary>
/// The Bookmark block's helper: given a link, answers with the linked page's title, description and
/// site name so the editor can fill the card. It reads and returns; nothing is stored here (the card
/// becomes part of the post when the author saves it, which is audited like any post save). Only a
/// signed-in author may ask, with the Desk's anti-forgery token, a limited number of times a minute,
/// because each call makes this server fetch an outside page (<see cref="LinkPreviewFetcher"/>).
/// </summary>
[Authorize(Policy = "CanEditPosts")]
[Route("admin/link-preview")]
public class LinkPreviewController : Controller
{
    public const int MaxPerMinute = 20;

    private readonly ILinkPreviewFetcher _fetcher;
    private readonly IMemoryCache _cache;

    public LinkPreviewController(ILinkPreviewFetcher fetcher, IMemoryCache cache)
    {
        _fetcher = fetcher;
        _cache = cache;
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fetch([FromForm] string? url, CancellationToken ct)
    {
        var who = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var counter = _cache.GetOrCreate("link-preview:" + who, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new StrongBox<int>();
        })!;
        if (Interlocked.Increment(ref counter.Value) > MaxPerMinute)
            return StatusCode(429, new { error = "That is a lot of links in one minute. Wait a moment, then fetch the next one." });

        var (details, error) = await _fetcher.FetchAsync(url, ct);
        if (details is null) return BadRequest(new { error });

        return Json(new { url = details.Url, title = details.Title, description = details.Description, site = details.Site, html = LinkPreview.CardHtml(details) });
    }
}
