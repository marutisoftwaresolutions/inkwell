namespace Blog.Core.Domain;

/// <summary>One email of the welcome sequence (Admin → Newsletter → Welcome emails), stored in the settings JSON.</summary>
public class WelcomeEmail
{
    public bool Enabled { get; set; }
    /// <summary>Days after the reader confirms their subscription.</summary>
    public int DelayDays { get; set; }
    public string Subject { get; set; } = "";
    /// <summary>The message, in Markdown. Raw HTML in it is shown as typed, never sent as markup.</summary>
    public string BodyMarkdown { get; set; } = "";
}
