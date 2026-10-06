using System.Security.Cryptography;
using System.Text;

namespace Blog.Core.Services;

/// <summary>
/// Two-factor sign-in with an authenticator app (ADR-0021): RFC 6238 time-based one-time passwords
/// (HMAC-SHA1, 6 digits, 30-second steps — what Google Authenticator, Microsoft Authenticator, 1Password,
/// Bitwarden and Authy all default to), Base32 secrets (RFC 4648), single-use recovery codes stored as
/// SHA-256 hashes, and the per-role requirement. Pure: no clock, storage or HTTP of its own.
/// </summary>
public static class TwoFactor
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    /// <summary>Codes from one step either side are accepted, for clock drift between phone and server.</summary>
    public const int DriftSteps = 1;
    public const int SecretBytes = 20;           // 160 bits, as RFC 4226 recommends for SHA-1
    public const int RecoveryCodeCount = 10;

    // ── Secrets ─────────────────────────────────────────────────────────────────

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var clean = text.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Not a Base32 string.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    /// <summary>The secret grouped in fours for typing by hand when the QR code cannot be scanned.</summary>
    public static string ForDisplay(string base32) =>
        string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - i * 4))));

    /// <summary>
    /// The <c>otpauth://totp/…</c> address authenticator apps read from the QR code (Google's Key Uri Format).
    /// </summary>
    public static string ProvisioningUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}" +
               $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    // ── Codes ───────────────────────────────────────────────────────────────────

    public static long StepAt(DateTime utc) =>
        (long)Math.Floor((DateTime.SpecifyKind(utc, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds / StepSeconds);

    /// <summary>The code for one time step (RFC 4226 dynamic truncation).</summary>
    public static string CodeAt(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xFF); step >>= 8; }
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Checks a typed code. Returns the matched step, or null. A step at or before <paramref name="lastUsedStep"/>
    /// is refused, so a code that was already used (or an older one) can never be replayed.
    /// </summary>
    public static long? Verify(byte[] secret, string? typed, DateTime utcNow, long? lastUsedStep)
    {
        var code = Normalize(typed);
        if (code is null || code.Length != Digits) return null;
        var now = StepAt(utcNow);
        for (var d = -DriftSteps; d <= DriftSteps; d++)
        {
            var step = now + d;
            if (lastUsedStep is long last && step <= last) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeAt(secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    private static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var digits = new string(typed.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.All(char.IsAsciiDigit) ? digits : null;
    }

    // ── Recovery codes ──────────────────────────────────────────────────────────

    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";   // no 0/o, 1/l/i

    /// <summary>Ten fresh codes like <c>k7m2q-x9hr4</c>, shown once. Store only <see cref="HashRecoveryCode"/>.</summary>
    public static IReadOnlyList<string> NewRecoveryCodes()
    {
        var codes = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var chars = new char[10];
            for (var j = 0; j < chars.Length; j++) chars[j] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            codes.Add(new string(chars, 0, 5) + "-" + new string(chars, 5, 5));
        }
        return codes;
    }

    public static string HashRecoveryCode(string code)
    {
        var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>True when <paramref name="typed"/> looks like a recovery code rather than an app code.</summary>
    public static bool LooksLikeRecoveryCode(string? typed) =>
        !string.IsNullOrWhiteSpace(typed) && typed.Count(char.IsLetterOrDigit) == 10 && typed.Any(char.IsLetter);

    /// <summary>
    /// Finds <paramref name="typed"/> among the stored hashes. Returns the remaining hashes (the used one
    /// removed) or null when it matched none. Each code works once.
    /// </summary>
    public static IReadOnlyList<string>? UseRecoveryCode(IReadOnlyList<string> storedHashes, string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var hash = Encoding.ASCII.GetBytes(HashRecoveryCode(typed));
        var index = -1;
        for (var i = 0; i < storedHashes.Count; i++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedHashes[i]), hash)) index = i;
        if (index < 0) return null;
        return storedHashes.Where((_, i) => i != index).ToList();
    }

    // ── Policy ──────────────────────────────────────────────────────────────────

    /// <summary>Roles an Admin can require two-factor sign-in for (Settings → Notifications &amp; housekeeping).</summary>
    public static readonly IReadOnlyList<string> Roles = new[] { "Admin", "Editor", "Author", "Contributor" };

    public static bool IsRequiredFor(string? role, IEnumerable<string>? requiredRoles) =>
        role is not null && requiredRoles is not null && requiredRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
