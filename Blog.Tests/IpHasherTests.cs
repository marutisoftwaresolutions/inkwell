using System.Security.Cryptography;
using System.Text;
using Blog.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Reader IPs were SHA-256(ip + a salt that was the same in every copy of the source), so they were
/// reversible by anyone. These pin the per-install keyed hash that replaced it.
/// </summary>
public class IpHasherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "inkwell-iphash-" + Guid.NewGuid().ToString("N"));

    public IpHasherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp folder */ }
    }

    private IpHasher Install(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dir, "keys")));
        return new IpHasher(dir, provider, NullLogger<IpHasher>.Instance);
    }

    [Fact]
    public void The_same_address_hashes_the_same_within_an_install_and_across_restarts()
    {
        var first = Install("a").Hash("203.0.113.7");
        var afterRestart = Install("a").Hash("203.0.113.7");

        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal(first, afterRestart);
        Assert.True(File.Exists(Path.Combine(_root, "a", IpHasher.FileName)));
    }

    [Fact]
    public void Two_installs_hash_the_same_address_differently()
    {
        Assert.NotEqual(Install("a").Hash("203.0.113.7"), Install("b").Hash("203.0.113.7"));
    }

    [Fact]
    public void The_stored_secret_is_not_in_clear_and_the_old_public_salt_is_gone()
    {
        var hasher = Install("a");
        var hash = hasher.Hash("203.0.113.7");

        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("203.0.113.7" + "pv-salt"))).ToLowerInvariant();
        Assert.NotEqual(legacy, hash);

        var stored = File.ReadAllText(Path.Combine(_root, "a", IpHasher.FileName));
        Assert.False(stored.Length == 44 && stored.EndsWith('='), "The secret must be Data-Protection-encrypted, not bare base64.");
    }

    [Fact]
    public void An_unwritable_location_still_hashes_and_never_throws()
    {
        var missing = Path.Combine(_root, "does", "not", "exist");
        var hasher = new IpHasher(missing, new EphemeralDataProtectionProvider(), NullLogger<IpHasher>.Instance);

        Assert.Matches("^[0-9a-f]{64}$", hasher.Hash("203.0.113.7"));
        Assert.Equal("", hasher.Hash(""));
    }

    [Fact]
    public void A_secret_the_key_ring_cannot_read_is_replaced_not_fatal()
    {
        Install("a").Hash("203.0.113.7");
        File.WriteAllText(Path.Combine(_root, "a", IpHasher.FileName), "not-a-protected-payload");

        Assert.Matches("^[0-9a-f]{64}$", Install("a").Hash("203.0.113.7"));
    }
}
