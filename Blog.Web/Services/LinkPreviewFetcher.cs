using System.Net;
using System.Net.Sockets;
using System.Text;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>Fetches a linked page so the editor can build a bookmark card. An interface so tests can stand in for the network.</summary>
public interface ILinkPreviewFetcher
{
    /// <summary>The page's details, or a reason a person can act on. Never throws.</summary>
    Task<(LinkPreview.Details? Details, string? Error)> FetchAsync(string? url, CancellationToken ct);
}

/// <summary>
/// The real fetcher. The address comes from a signed-in author, but it is still an address this
/// server is being asked to call, so it gets the same guard as webhooks: <see cref="PublicNetwork"/>
/// resolves the host itself and refuses loopback, private, link-local and cloud-metadata addresses at
/// the socket, on the first request and on every redirect. Redirects are followed by hand, a few at
/// most, each address checked again. Only an HTML answer is read, and only its first
/// <see cref="MaxBytes"/>. Called from an explicit Desk action, never while serving a reader.
/// </summary>
public sealed class LinkPreviewFetcher : ILinkPreviewFetcher
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);
    public const int MaxBytes = 512 * 1024;
    public const int MaxRedirects = 3;

    private readonly HttpClient _http;

    public LinkPreviewFetcher()
    {
        _http = new HttpClient(PublicNetwork.Handler()) { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Inkwell-LinkPreview/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9");
    }

    public async Task<(LinkPreview.Details? Details, string? Error)> FetchAsync(string? url, CancellationToken ct)
    {
        var uri = LinkPreview.CleanUrl(url);
        if (uri is null) return (null, "Enter the full address of the page, starting with https://.");

        // One deadline for everything: every redirect and the body. HttpClient's own timeout stops at the headers.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        var outer = ct;
        ct = deadline.Token;

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                // The same rule webhooks use: no local names, no private or reserved literals. The socket check below backs it up.
                if (WebhookRules.ValidateUrl(uri.AbsoluteUri) is not null)
                    return (null, "That address is not on the public internet, so it cannot be fetched.");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var code = (int)response.StatusCode;

                if (code is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (LinkPreview.CleanUrl(next.AbsoluteUri) is not { } cleaned) return (null, "The page redirects somewhere that cannot be fetched.");
                    uri = cleaned;
                    continue;
                }
                if (code is < 200 or >= 300) return (null, $"The page answered HTTP {code}. Fill in the card by hand.");

                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.Contains("html", StringComparison.OrdinalIgnoreCase))
                    return (null, "That address is not a web page, so there are no details to read. Fill in the card by hand.");

                var html = await ReadLimitedAsync(response, ct);
                return (LinkPreview.Parse(html, uri), null);
            }
            return (null, "The page redirects too many times.");
        }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested)
        {
            return (null, $"The page did not answer within {Timeout.TotalSeconds:0} seconds. Fill in the card by hand.");
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            return (null, "The page could not be reached. Check the address, or fill in the card by hand.");
        }
        catch (Exception)
        {
            return (null, "The page's details could not be read. Fill in the card by hand.");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) break;
            read += n;
        }

        Encoding encoding = Encoding.UTF8;
        try
        {
            if (response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset) encoding = Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException) { /* an unknown charset is read as UTF-8 */ }
        return encoding.GetString(buffer, 0, read);
    }
}
