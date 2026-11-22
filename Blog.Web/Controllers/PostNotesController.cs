using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>
/// Notes on a post, for the people working on it (the panel above the post editor). Anyone who can
/// open the post in the editor can read and add notes and resolve one: its author, or an Admin or
/// Editor. Notes never reach a reader. Adding and resolving are audited.
/// </summary>
[Authorize(Policy = "CanEditPosts")]
[Route("admin/posts/{postId:guid}/notes")]
public class PostNotesController : Controller
{
    private readonly IPostNoteRepository _notes;
    private readonly IPostRepository _posts;
    private readonly AuditService _audit;

    public PostNotesController(IPostNoteRepository notes, IPostRepository posts, AuditService audit)
    {
        _notes = notes;
        _posts = posts;
        _audit = audit;
    }

    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "Someone";

    /// <summary>The post when this user may work on it; otherwise null. The same rule as the editor.</summary>
    private async Task<Post?> EditablePostAsync(Guid postId)
    {
        var post = await _posts.GetByIdAsync(postId, null);
        if (post is null) return null;
        return post.AuthorId == Me || User.IsInRole("Admin") || User.IsInRole("Editor") ? post : null;
    }

    private IActionResult BackToEditor(Guid postId) => Redirect($"/admin/posts/edit/{postId}#post-notes");

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid postId, string? body)
    {
        var post = await EditablePostAsync(postId);
        if (post is null) return NotFound();

        var text = PostNote.Clean(body);
        if (text.Length == 0)
        {
            TempData["Error"] = "Write the note before adding it.";
            return BackToEditor(postId);
        }

        var id = await _notes.AddAsync(new PostNote { PostId = postId, UserId = Me, UserName = MyName, Body = text });
        // The note itself is not copied into the audit trail: the trail says that a note was left, the post keeps what it said.
        await _audit.LogAsync(AuditActions.PostNoteAdded, "Post", postId.ToString(), post.Title, newValues: $"Note {id}");
        TempData["Success"] = "Note added.";
        return BackToEditor(postId);
    }

    [HttpPost("{id:guid}/resolve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid postId, Guid id)
    {
        var post = await EditablePostAsync(postId);
        if (post is null) return NotFound();

        if (await _notes.ResolveAsync(id, postId, MyName))
        {
            await _audit.LogAsync(AuditActions.PostNoteResolved, "Post", postId.ToString(), post.Title, newValues: $"Note {id}");
            TempData["Success"] = "Note resolved.";
        }
        return BackToEditor(postId);
    }
}
