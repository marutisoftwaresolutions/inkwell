namespace Blog.Core.Domain;

/// <summary>One issued reset link. Only the hash of the token is stored; the raw value lives in the email.</summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RequestIp { get; set; }
}
