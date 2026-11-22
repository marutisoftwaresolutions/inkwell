namespace Blog.Core.Domain;

/// <summary>
/// A long-lived credential for a script or an AI assistant, acting as the user who created it and
/// never with more than that user may do. Only a SHA-256 hash of the key is stored, so a database
/// read never yields a usable key; the key itself is shown once, when it is created.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }
    /// <summary>The account the key acts as. Disabling or deleting the account stops the key.</summary>
    public Guid UserId { get; set; }
    /// <summary>What the key is for, chosen by the user ("Claude Desktop", "Deploy script"). Shown in the audit trail.</summary>
    public string Name { get; set; } = "";
    /// <summary>The first characters of the key, kept in clear so the user can tell keys apart.</summary>
    public string Prefix { get; set; } = "";
    public string KeyHash { get; set; } = "";
    /// <summary>Comma-separated scopes from <c>ApiKeyFormat.AllScopes</c>.</summary>
    public string Scopes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsUsable(DateTime nowUtc) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > nowUtc);
}
