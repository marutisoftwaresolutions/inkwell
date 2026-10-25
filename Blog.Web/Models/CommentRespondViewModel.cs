using Blog.Core.Domain;

namespace Blog.Web.Models;

/// <summary>
/// The respond page (ADR-0031): the comment form on a page of its own with fresh tokens, the outcome of the last
/// submission, and what the reader typed when it was not sent.
/// </summary>
public sealed record CommentRespondViewModel(Post Post, Comment? Parent, string? Message, bool Sent,
    string Name, string Email, string Content, string RequestToken, string FormToken, IReadOnlyList<string>? Invalid = null);
