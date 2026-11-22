using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Blog.Core.Services;

namespace Blog.Web.Services;

/// <summary>
/// Asks the server's own DNS resolvers for a name's TXT records (<see cref="DnsTxt"/> does the wire
/// format). Used only by the sender check, which an admin starts by hand: never on a reader's request.
/// Every call has a short timeout and returns null instead of throwing when no resolver answers, so the
/// check can say "could not be read just now" and the page still renders.
/// </summary>
public sealed class DnsTxtLookup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>The TXT records for <paramref name="name"/>; an empty list when there are none; null when no resolver answered.</summary>
    public async Task<IReadOnlyList<string>?> QueryAsync(string name, CancellationToken ct = default)
    {
        byte[] query;
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        try { query = DnsTxt.BuildQuery(name, id); }
        catch (ArgumentException) { return null; }

        foreach (var server in Resolvers().Take(3))
        {
            try
            {
                var answer = await UdpAsync(server, query, id, ct);
                if (answer is { Truncated: true }) answer = await TcpAsync(server, query, id, ct);
                if (answer is null) continue;
                // 0 = answered, 3 = the name does not exist: both are real answers. Anything else, try the next resolver.
                if (answer.ResponseCode is 0 or 3) return answer.Texts;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
            {
                // this resolver did not answer in time: try the next
            }
        }
        return null;
    }

    private static IEnumerable<IPAddress> Resolvers()
    {
        var seen = new HashSet<IPAddress>();
        IEnumerable<NetworkInterface> interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up); }
        catch { yield break; }
        foreach (var nic in interfaces)
        {
            IPAddressCollection addresses;
            try { addresses = nic.GetIPProperties().DnsAddresses; } catch { continue; }
            foreach (var address in addresses)
                if (!address.IsIPv6SiteLocal && seen.Add(address)) yield return address;
        }
    }

    private static async Task<DnsTxt.Answer?> UdpAsync(IPAddress server, byte[] query, ushort id, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        using var udp = new UdpClient(server.AddressFamily);
        udp.Connect(server, 53);
        await udp.SendAsync(query, cts.Token);
        var result = await udp.ReceiveAsync(cts.Token);
        return DnsTxt.Parse(result.Buffer, id);
    }

    private static async Task<DnsTxt.Answer?> TcpAsync(IPAddress server, byte[] query, ushort id, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        using var tcp = new TcpClient(server.AddressFamily);
        await tcp.ConnectAsync(server, 53, cts.Token);
        var stream = tcp.GetStream();
        var framed = new byte[query.Length + 2];
        framed[0] = (byte)(query.Length >> 8);
        framed[1] = (byte)query.Length;
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, cts.Token);

        var header = new byte[2];
        await stream.ReadExactlyAsync(header, cts.Token);
        var body = new byte[(header[0] << 8) | header[1]];
        await stream.ReadExactlyAsync(body, cts.Token);
        return DnsTxt.Parse(body, id);
    }
}
