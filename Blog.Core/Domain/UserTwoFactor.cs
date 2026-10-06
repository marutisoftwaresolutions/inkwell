namespace Blog.Core.Domain;

/// <summary>
/// A user's two-factor state (ADR-0021). Read only by the sign-in and security screens, never as part of
/// <see cref="User"/>, so the encrypted secret does not travel with ordinary user reads.
/// </summary>
public sealed record UserTwoFactor(
    Guid UserId,
    string? SecretProtected,
    DateTime? EnabledAt,
    IReadOnlyList<string> RecoveryHashes,
    long? LastStep)
{
    public bool IsEnabled => EnabledAt.HasValue && !string.IsNullOrEmpty(SecretProtected);
}
