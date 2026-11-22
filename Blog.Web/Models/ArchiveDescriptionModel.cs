namespace Blog.Web.Models;

/// <summary>The description editor for one category or tag, rendered by <c>Views/Shared/ArchiveDescription.cshtml</c>.</summary>
/// <param name="Kind">"Category" or "Tag": used in the page title and copy.</param>
/// <param name="Id">The category or tag being described.</param>
/// <param name="Name">Its display name.</param>
/// <param name="PublicPath">The public archive it describes, e.g. /category/reviews.</param>
/// <param name="Description">The stored (or just-submitted) text; plain text with [text](/path) links.</param>
/// <param name="PostAction">Where the form posts.</param>
/// <param name="BackHref">The list screen to return to.</param>
/// <param name="Query">The list's search text, carried through so saving returns to the same view.</param>
/// <param name="Page">The list page, carried through likewise.</param>
public sealed record ArchiveDescriptionModel(
    string Kind, Guid Id, string Name, string PublicPath, string? Description,
    string PostAction, string BackHref, string? Query, int Page);
