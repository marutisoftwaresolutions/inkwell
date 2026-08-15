using System.Net;
using System.Net.Sockets;
using Blog.Core.Domain;

namespace Blog.Web.Services.Import;

/// <summary>A source-specific parser (WordPress WXR, Ghost JSON, …).</summary>
public interface IContentImporter
{
    ImportSource Source { get; }

    /// <summary>Validates the export file and returns a lightweight preview (throws on an invalid file).</summary>
    ImportManifest Analyze(string filePath);

    /// <summary>
    /// Emits every importable unit as a <see cref="ParsedRecord"/>, tagged with type + processing
    /// <see cref="ParsedRecord.Ordinal"/> (authors→terms→images→posts→comments) so dependencies exist
    /// before dependents. Streams the file — safe for large exports.
    /// </summary>
    IEnumerable<ParsedRecord> ReadRecords(string filePath);
}

/// <summary>
/// SSRF guard for importer image downloads: only public http/https URLs are allowed. Blocks
/// loopback, private, link-local, CGNAT and unique-local addresses (incl. IPv4-mapped IPv6 and
/// cloud metadata endpoints) so a malicious export can't make the server fetch internal resources.
/// </summary>
public static class UrlGuard
{
    public static bool TryValidatePublicHttp(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        try
        {
            var addresses = Dns.GetHostAddresses(u.Host);
            if (addresses.Length == 0) return false;
            if (addresses.Any(IsPrivate)) return false;
        }
        catch { return false; }
        uri = u;
        return true;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;                 // link-local / metadata
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;    // 172.16/12
            if (b[0] == 192 && b[1] == 168) return true;                 // 192.168/16
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;   // CGNAT 100.64/10
            return false;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if (ip.IsIPv4MappedToIPv6) return IsPrivate(ip.MapToIPv4());
            if ((b[0] & 0xFE) == 0xFC) return true;                       // ULA fc00::/7
            return false;
        }
        return true; // unknown family → deny
    }
}
