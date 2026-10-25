using System.Net;
using System.Net.Sockets;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>
/// Outbound connections to addresses an operator typed (webhooks, ADR-0016; direct push, ADR-0029) are
/// checked at the socket, not just when the address is saved: <see cref="ConnectAsync"/> resolves the host
/// itself and refuses any address <see cref="WebhookRules.IsForbiddenAddress"/> rejects, so a public name
/// that resolves (or re-resolves) to 127.0.0.1, a private range or the cloud metadata address is never reached.
/// </summary>
public static class PublicNetwork
{
    /// <summary>A handler that only connects to public addresses and never follows redirects.</summary>
    public static SocketsHttpHandler Handler(bool allowPrivate = false) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = allowPrivate ? ConnectAnyAsync : ConnectAsync,
    };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        if (!WebhookRules.AreAllPublic(addresses))
            throw new HttpRequestException($"Refused: {host} resolves to a private or reserved address.");
        return await OpenAsync(addresses, context.DnsEndPoint.Port, ct);
    }

    /// <summary>For testing against a local WordPress or Ghost only (<c>Exports:PushAllowPrivateTargets</c>).</summary>
    private static async ValueTask<Stream> ConnectAnyAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        return await OpenAsync(addresses, context.DnsEndPoint.Port, ct);
    }

    private static async ValueTask<Stream> OpenAsync(IPAddress[] addresses, int port, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
