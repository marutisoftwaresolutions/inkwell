namespace Blog.Core.Domain;

/// <summary>A contact form defined in Admin → Forms and placed on pages with the Form block (ADR-0030).</summary>
public class ContactForm
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    /// <summary>The address part (<c>/forms/{key}</c>) and the page block's reference; fixed once submissions exist.</summary>
    public string FormKey { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The fields, as JSON (<see cref="Services.Forms.FormField"/>).</summary>
    public string FieldsJson { get; set; } = "[]";
    public string? SubmitLabel { get; set; }
    public string? SuccessMessage { get; set; }
    /// <summary>Where a notification goes for each submission; none when empty.</summary>
    public string? NotifyEmail { get; set; }
    /// <summary>Days a submission is kept before the nightly job deletes it; 0 keeps it until someone deletes it.</summary>
    public int RetentionDays { get; set; } = 365;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Read with lists only.
    public int SubmissionCount { get; set; }
    public int UnreadCount { get; set; }
    public DateTime? LastSubmissionAt { get; set; }
}

/// <summary>What one visitor sent through a form.</summary>
public class FormSubmission
{
    public long Id { get; set; }
    public Guid FormId { get; set; }
    public Guid OwnerId { get; set; }
    /// <summary>{ fieldKey: value } as JSON.</summary>
    public string ValuesJson { get; set; } = "{}";
    /// <summary>A short plain-text summary for the Desk's search box.</summary>
    public string? SearchText { get; set; }
    /// <summary>Keyed hash of the sender's address (ADR-0015), for the rate limit only; never the address.</summary>
    public string? AddressHash { get; set; }
    public string? ContentHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
