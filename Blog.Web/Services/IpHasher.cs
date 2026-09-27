using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Blog.Web.Services;

/// <summary>
/// Pseudonymises reader IP addresses for <c>PageViews.IpHash</c> with HMAC-SHA256 under a secret
/// generated per installation. The previous scheme was SHA-256 of the address plus a salt that was
/// the same in every copy of the source, so anyone could rebuild the IPv4 table in minutes and read
/// the addresses back (ADR-0015).
///
/// The secret is 32 random bytes, stored Data-Protection-encrypted in <c>ip-hash.secret</c> beside
/// the key ring, so it survives restarts and redeploys exactly as the keys do. Hashing has to be
/// deterministic, which Data Protection's own output is not; this is the one place a keyed hash is
/// used instead (architecture rule, § Time, identity, secrets).
///
/// Fail-soft: if the file cannot be read or written, a random in-memory secret is used for the life
/// of the process. Hashes are then not stable across restarts, but nothing is ever stored in a
/// reversible form, and nothing breaks.
/// </summary>
public sealed class IpHasher
{
    public const string FileName = "ip-hash.secret";

    private readonly string _secretPath;
    private readonly IDataProtector _protector;
    private readonly ILogger<IpHasher> _log;
    private readonly Lazy<byte[]> _secret;

    public IpHasher(string secretDirectory, IDataProtectionProvider provider, ILogger<IpHasher> log)
    {
        _secretPath = Path.Combine(secretDirectory, FileName);
        _protector = provider.CreateProtector("Inkwell.IpHashSecret.v1");
        _log = log;
        _secret = new Lazy<byte[]>(LoadOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>64 lowercase hex characters, or empty for an empty address.</summary>
    public string Hash(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return string.Empty;
        return Convert.ToHexString(HMACSHA256.HashData(_secret.Value, Encoding.UTF8.GetBytes(ip))).ToLowerInvariant();
    }

    private byte[] LoadOrCreate()
    {
        try
        {
            if (File.Exists(_secretPath) && TryRead(out var existing)) return existing;

            var fresh = RandomNumberGenerator.GetBytes(32);
            var protectedText = _protector.Protect(Convert.ToBase64String(fresh));
            try
            {
                // CreateNew: two processes (IIS web garden) racing here must end up with one secret.
                using var stream = new FileStream(_secretPath, File.Exists(_secretPath) ? FileMode.Truncate : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(protectedText);
            }
            catch (IOException) when (File.Exists(_secretPath) && TryRead(out var winner))
            {
                return winner;   // another process created it first
            }
            return fresh;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IP hash secret could not be stored at {Path}; using a per-process secret, so reader hashes will change on restart.", _secretPath);
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    private bool TryRead(out byte[] secret)
    {
        secret = [];
        try
        {
            var bytes = Convert.FromBase64String(_protector.Unprotect(File.ReadAllText(_secretPath).Trim()));
            if (bytes.Length < 32) return false;
            secret = bytes;
            return true;
        }
        catch (Exception ex)
        {
            // Unreadable (key ring replaced, file damaged): a new secret replaces it. Old hashes simply
            // stop matching new ones, which nothing depends on.
            _log.LogWarning(ex, "IP hash secret at {Path} could not be read; a new one will be generated.", _secretPath);
            return false;
        }
    }
}
