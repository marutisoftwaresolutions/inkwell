using System;

namespace Blog.Core.Domain;

public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Uuid { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending"; // pending | confirmed | unsubscribed
    public bool Subscribed { get; set; } = false;
    public string ConfirmToken { get; set; } = Guid.NewGuid().ToString("N");
    public string UnsubscribeToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>How many emails of the welcome sequence this subscriber has been sent or passed over.</summary>
    public int WelcomeStepsSent { get; set; }
    /// <summary>The page of this site the sign-up form was sent from (path only), or null when unknown.</summary>
    public string? SourcePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
