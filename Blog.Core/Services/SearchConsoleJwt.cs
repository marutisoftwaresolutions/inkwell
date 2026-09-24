using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Blog.Core.Services;

/// <summary>
/// Builds the signed assertion a service account exchanges for an access token (RFC 7523 JWT bearer
/// grant). Kept in Blog.Core with no HTTP so the exact bytes that get signed are unit-testable with a
/// throwaway RSA key. No Google SDK: the grant is three base64url segments and one RS256 signature.
/// </summary>
public static class SearchConsoleJwt
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/webmasters.readonly";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(55); // Google caps assertions at one hour

    /// <summary>The compact JWS for <paramref name="credential"/>, valid from <paramref name="issuedAtUtc"/>.</summary>
    public static string Create(ServiceAccountCredential credential, DateTime issuedAtUtc, string scope = ReadOnlyScope)
    {
        var iat = new DateTimeOffset(issuedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        var header = JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" });
        var claims = JsonSerializer.Serialize(new
        {
            iss = credential.ClientEmail,
            scope,
            aud = TokenEndpoint,
            iat,
            exp = iat + (long)Lifetime.TotalSeconds,
        });

        var signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(claims));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credential.PrivateKeyPem);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64Url(signature);
    }

    /// <summary>Splits a compact JWS and verifies its signature with the given public key. For tests and diagnostics.</summary>
    public static bool Verify(string jwt, RSA publicKey)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return false;
        var data = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
        return publicKey.VerifyData(data, FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>The decoded claims segment, for inspection.</summary>
    public static JsonDocument Claims(string jwt) =>
        JsonDocument.Parse(FromBase64Url(jwt.Split('.')[1]));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4) { case 2: padded += "=="; break; case 3: padded += "="; break; }
        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// The Search Console property as the API wants it in a path: URL-prefix properties are
    /// percent-encoded whole; domain properties stay <c>sc-domain:example.com</c>.
    /// </summary>
    public static string EncodeProperty(string property)
    {
        property = property.Trim();
        return property.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase)
            ? property
            : Uri.EscapeDataString(property);
    }

    /// <summary>
    /// Normalises what an operator types into a property Search Console recognises: a bare domain
    /// becomes a domain property, a URL keeps its scheme and host and gains the trailing slash the
    /// API requires. Returns null when the input is neither.
    /// </summary>
    public static string? NormaliseProperty(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (s.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
            return "sc-domain:" + s["sc-domain:".Length..].Trim().TrimEnd('/').ToLowerInvariant();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() + (uri.AbsolutePath.TrimEnd('/') + "/");
        if (s.Contains('.') && !s.Contains('/') && !s.Contains(' '))
            return "sc-domain:" + s.ToLowerInvariant();
        return null;
    }
}
