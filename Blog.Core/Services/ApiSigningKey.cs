namespace Blog.Core.Services;

/// <summary>
/// Whether <c>Jwt:Key</c> can sign API tokens (ADR-0033). A key anyone can know (the example file's placeholder,
/// an empty or short value) would let anyone mint a token for any user, so the API refuses to issue or accept
/// tokens until a real secret is set. Pure: the caller reads the configuration.
/// </summary>
public static class ApiSigningKey
{
    /// <summary>HMAC-SHA256 wants at least 256 bits of key; the example tells operators to generate 48 random bytes.</summary>
    public const int MinBytes = 32;

    /// <summary>Null when the key is usable; otherwise what is wrong with it, for the operator.</summary>
    public static string? Problem(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Jwt:Key is not set, so the API cannot sign in anyone. Set it to a long random secret (for example: openssl rand -base64 48).";
        if (key.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) || key.Contains("CHANGE", StringComparison.OrdinalIgnoreCase)
            || key.Contains("your-", StringComparison.OrdinalIgnoreCase) || key.Contains("secret-key", StringComparison.OrdinalIgnoreCase))
            return "Jwt:Key is still the example placeholder, which anyone can read, so the API is switched off. Set it to a long random secret (for example: openssl rand -base64 48).";
        if (System.Text.Encoding.UTF8.GetByteCount(key) < MinBytes)
            return $"Jwt:Key is shorter than {MinBytes} bytes, so the API is switched off. Set it to a long random secret (for example: openssl rand -base64 48).";
        if (key.Distinct().Count() < 8)
            return "Jwt:Key repeats too few characters to be a secret, so the API is switched off. Set it to a long random secret (for example: openssl rand -base64 48).";
        return null;
    }

    public static bool IsUsable(string? key) => Problem(key) is null;
}
