namespace Blog.Core.Domain;

/// <summary>
/// A single IP firewall rule — either a <c>Block</c> (the address is refused with 403) or an
/// <c>Allow</c> (the address is never auto-blocked). Blocks are created automatically by the
/// threat scorer when an address crosses the configured score threshold, or manually by an
/// admin from Admin → Security / Admin → Error Monitor. Rows persist so blocks survive a restart.
/// </summary>
public class IpFirewallRule
{
    public long      Id            { get; set; }
    public string    IpAddress     { get; set; } = "";
    /// <summary>"Block" or "Allow" — see <see cref="IpRuleKinds"/>.</summary>
    public string    Kind          { get; set; } = IpRuleKinds.Block;
    /// <summary>"Auto" (threat scorer) or "Manual" (an admin added it) — see <see cref="IpRuleSources"/>.</summary>
    public string    Source        { get; set; } = IpRuleSources.Auto;
    public string?   Reason        { get; set; }
    /// <summary>Threat score accumulated at the moment the block was created.</summary>
    public int       Score         { get; set; }
    /// <summary>How many times this address has been blocked — drives duration escalation.</summary>
    public int       OffenseCount  { get; set; } = 1;
    /// <summary>Requests refused since the rule was created.</summary>
    public int       HitCount      { get; set; }
    public string?   LastPath      { get; set; }
    public string?   LastUserAgent { get; set; }
    public string?   CreatedBy     { get; set; }
    public DateTime  CreatedAt     { get; set; }
    /// <summary>UTC expiry; <c>null</c> means permanent.</summary>
    public DateTime? ExpiresAt     { get; set; }
    public DateTime? LastHitAt     { get; set; }

    /// <summary>Allow rules never expire; a block is spent once <see cref="ExpiresAt"/> passes.</summary>
    public bool IsActive =>
        Kind == IpRuleKinds.Allow || ExpiresAt is null || ExpiresAt > DateTime.UtcNow;

    public bool IsPermanent => Kind == IpRuleKinds.Block && ExpiresAt is null;
}

public static class IpRuleKinds
{
    public const string Block = "Block";
    public const string Allow = "Allow";
}

public static class IpRuleSources
{
    public const string Auto   = "Auto";
    public const string Manual = "Manual";
}

/// <summary>Summary counters for the Security dashboard tiles.</summary>
public class FirewallStats
{
    public int  ActiveBlocks    { get; set; }
    public int  PermanentBlocks { get; set; }
    public int  AutoBlocks      { get; set; } // active, created by the scorer
    public int  BlocksLast24h   { get; set; }
    public int  AllowRules      { get; set; }
    public long RequestsDenied  { get; set; }
}

/// <summary>
/// An address currently accumulating a threat score but not yet blocked (in-memory watchlist).
/// Shown in Admin → Security so an operator can see an attack building and block early.
/// </summary>
public class IpWatchEntry
{
    public string   IpAddress   { get; set; } = "";
    public int      Score       { get; set; }
    public int      Requests    { get; set; }
    public string?  LastPath    { get; set; }
    public string?  LastReason  { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime LastSeenAt  { get; set; }
}
