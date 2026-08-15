using System.Net;

namespace Blog.Web.Services.Security;

/// <summary>
/// A parsed set of never-block addresses. Accepts single addresses (<c>203.0.113.7</c>) and CIDR
/// ranges (<c>203.0.113.0/24</c>, <c>2001:db8::/32</c>), one per line or comma/semicolon separated.
/// Loopback and private/link-local ranges are always included so a misconfiguration can never
/// lock the operator out of the machine the site runs on.
/// </summary>
public sealed class IpAllowlist
{
    private static readonly string[] _alwaysAllowed =
    [
        "127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12",
        "192.168.0.0/16", "169.254.0.0/16", "fc00::/7", "fe80::/10"
    ];

    private readonly List<IPNetwork> _networks;

    /// <summary>Entries that could not be parsed — surfaced in the admin UI so typos are visible.</summary>
    public IReadOnlyList<string> InvalidEntries { get; }

    private IpAllowlist(List<IPNetwork> networks, List<string> invalid)
    {
        _networks = networks;
        InvalidEntries = invalid;
    }

    public static IpAllowlist Parse(string? raw)
    {
        var networks = new List<IPNetwork>();
        var invalid = new List<string>();

        foreach (var entry in _alwaysAllowed)
            if (TryParseEntry(entry, out var builtin)) networks.Add(builtin);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split([',', ';', '\n', '\r', ' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (TryParseEntry(part, out var net)) networks.Add(net);
                else invalid.Add(part);
            }
        }

        return new IpAllowlist(networks, invalid);
    }

    private static bool TryParseEntry(string entry, out IPNetwork network)
    {
        if (entry.Contains('/'))
            return IPNetwork.TryParse(entry, out network);

        if (IPAddress.TryParse(entry, out var addr))
        {
            network = new IPNetwork(addr, addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        network = default;
        return false;
    }

    public bool Contains(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var addr)) return false;
        return Contains(addr);
    }

    public bool Contains(IPAddress addr)
    {
        // An IPv4 client arriving over a dual-stack socket appears as ::ffff:a.b.c.d — compare
        // both forms so a plain IPv4 allowlist entry still matches.
        var v4 = addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : null;

        foreach (var net in _networks)
        {
            if (net.Contains(addr)) return true;
            if (v4 is not null && net.Contains(v4)) return true;
        }
        return false;
    }
}
