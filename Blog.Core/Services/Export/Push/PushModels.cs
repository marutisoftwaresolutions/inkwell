namespace Blog.Core.Services.Export.Push;

/// <summary>What a push does with an item whose slug already exists on the target (owner decision 2026-10-06: skip).</summary>
public enum PushConflictPolicy { Skip, Update, Duplicate }

/// <summary>The life of one push run (ADR-0029).</summary>
public enum PushStatus
{
    /// <summary>The job works through it on every run.</summary>
    Running,
    /// <summary>Waiting for the operator: the credential was rejected, or forgotten by a restart.</summary>
    Paused,
    /// <summary>Every item has an outcome (some may have failed; the counts say so).</summary>
    Completed,
    Cancelled,
}

public enum PushItemKind { Page, Post, Media, Term }

public enum PushItemStatus { Pending, Created, Updated, Skipped, Failed, Cancelled }

/// <summary>One push run. Never carries the credential: that lives only in the server's memory.</summary>
public sealed class ExportPush
{
    public Guid Id { get; set; }
    public ExportTarget Target { get; set; }
    public string SiteUrl { get; set; } = "";
    public PushConflictPolicy ConflictPolicy { get; set; }
    public bool IncludeDrafts { get; set; }
    public bool IncludePages { get; set; } = true;
    /// <summary>This site's origin when the push started; images that cannot be uploaded keep pointing here.</summary>
    public string SourceBaseUrl { get; set; } = "";
    public PushStatus Status { get; set; }
    public string? Message { get; set; }
    public int TotalCount { get; set; }
    /// <summary>Set after a rate limit: the job leaves the push alone until then.</summary>
    public DateTime? ResumeAt { get; set; }
    public Guid StartedBy { get; set; }
    public string? StartedByName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public bool IsActive => Status is PushStatus.Running or PushStatus.Paused;
}

public sealed class ExportPushItem
{
    public long Id { get; set; }
    public Guid PushId { get; set; }
    public PushItemKind Kind { get; set; }
    /// <summary>Post or page id; the upload path for an image; <c>category:slug</c> or <c>tag:slug</c> for a term.</summary>
    public string SourceKey { get; set; } = "";
    public string? Title { get; set; }
    public int Seq { get; set; }
    public PushItemStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? RemoteId { get; set; }
    public string? RemoteUrl { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsDone => Status is PushItemStatus.Created or PushItemStatus.Updated or PushItemStatus.Skipped;
}

/// <summary>A post or page to seed into a push, in the order it must be sent.</summary>
public sealed record NewPushItem(PushItemKind Kind, string SourceKey, string? Title, int Seq);

/// <summary>Outcome counts of a push's posts and pages (images and terms are means, not results).</summary>
public sealed record ExportPushCounts(int Pending, int Created, int Updated, int Skipped, int Failed, int Cancelled)
{
    public int Total => Pending + Created + Updated + Skipped + Failed + Cancelled;
    public int Done => Created + Updated + Skipped + Failed + Cancelled;
}

/// <summary>
/// The secret a push signs in with: a WordPress user name and Application Password, or a Ghost Admin API
/// key (<c>id:secret</c>). Held in memory only (ADR-0029); <see cref="ToString"/> never prints it.
/// </summary>
public sealed record PushCredential(ExportTarget Target, string? UserName, string Secret)
{
    public override string ToString() => $"PushCredential({Target}, redacted)";
}

/// <summary>Something that exists on the target: its id there, its public address, and Ghost's version stamp.</summary>
public sealed record RemoteItem(string Id, string? Url, string? UpdatedAt = null);

/// <summary>A post or page reference for seeding a push.</summary>
public sealed record ExportItemRef(PushItemKind Kind, Guid Id, Guid? ParentId, string Title, string Slug);
