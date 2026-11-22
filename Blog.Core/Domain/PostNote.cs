namespace Blog.Core.Domain;

/// <summary>
/// A note left on a post for the people working on it: a question for the author, a reminder to check
/// a figure, a reviewer's remark that is not a formal "needs changes". Notes live in the Desk only and
/// never reach a reader, a feed or the API. A note is open until someone resolves it.
/// </summary>
public class PostNote
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The writer's name as it was, so the note still reads sensibly after an account is removed.</summary>
    public string UserName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }

    public bool IsOpen => ResolvedAt is null;

    public const int MaxLength = 2000;

    /// <summary>What a person typed, trimmed, without control characters other than line breaks, capped.</summary>
    public static string Clean(string? body)
    {
        var text = new string((body ?? "").Where(c => !char.IsControl(c) || c is '\n' or '\r').ToArray()).Trim();
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }
}

public interface IPostNoteRepository
{
    /// <summary>A post's notes, open ones first, each group oldest first.</summary>
    Task<List<PostNote>> ListAsync(Guid postId);
    Task<Guid> AddAsync(PostNote note);
    /// <summary>Marks an open note of this post resolved; false when it is not there or already resolved.</summary>
    Task<bool> ResolveAsync(Guid id, Guid postId, string resolvedByName);
}
