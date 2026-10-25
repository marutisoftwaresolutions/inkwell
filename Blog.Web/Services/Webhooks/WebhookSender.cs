using System.Net;
using System.Net.Sockets;
using System.Text;
using Blog.Core.Services;

namespace Blog.Web.Services.Webhooks;

/// <summary>The outcome of one HTTP attempt.</summary>
public sealed record WebhookSendResult(bool Success, int? StatusCode, string? ResponseSnippet, string? Error);

/// <summary>Sends one signed webhook request. An interface so tests can stand in for the network.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(string url, string secret, string eventType, Guid eventId, string body, CancellationToken ct);
}

/// <summary>
/// The real sender. Server-side request forgery is stopped at the socket, not just at save time:
/// <see cref="PublicNetwork"/> resolves the host itself and refuses to connect
/// to any address <see cref="WebhookRules.IsForbiddenAddress"/> rejects, so a public name that
/// resolves (or re-resolves) to 127.0.0.1, a private range or the cloud metadata address is never
/// reached. Redirects are not followed; a 3xx counts as a failed attempt.
/// </summary>
public sealed class WebhookSender : IWebhookSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const int SnippetLength = 500;

    private readonly HttpClient _http;

    public WebhookSender()
    {
        _http = new HttpClient(PublicNetwork.Handler()) { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Inkwell-Webhooks/1.0");
    }

    public async Task<WebhookSendResult> SendAsync(string url, string secret, string eventType, Guid eventId, string body, CancellationToken ct)
    {
        var urlError = WebhookRules.ValidateUrl(url);
        if (urlError is not null) return new WebhookSendResult(false, null, null, urlError);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        request.Headers.Add("X-Inkwell-Event", eventType);
        request.Headers.Add("X-Inkwell-Delivery", eventId.ToString());
        request.Headers.Add("X-Inkwell-Signature", WebhookRules.Sign(secret, timestamp, body));

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            var snippet = await ReadSnippetAsync(response, ct);
            return WebhookRules.IsSuccess(code)
                ? new WebhookSendResult(true, code, snippet, null)
                : new WebhookSendResult(false, code, snippet, $"The endpoint answered HTTP {code}.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, null, $"No answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new WebhookSendResult(false, null, null, message.Length > 400 ? message[..400] : message);
        }
    }

    private static async Task<string?> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new char[SnippetLength];
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            return read == 0 ? null : new string(buffer, 0, read);
        }
        catch
        {
            return null;
        }
    }
}
