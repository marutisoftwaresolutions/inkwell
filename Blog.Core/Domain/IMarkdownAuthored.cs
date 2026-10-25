namespace Blog.Core.Domain;

/// <summary>
/// Something that can be written in Markdown or in the visual editor (ADR-0022 for posts, extended to pages in
/// v1.0.9). The shared editor partials (_MarkdownPane, _EditorModeForm) read this, so posts and pages use one editor.
/// </summary>
public interface IMarkdownAuthored
{
    Guid Id { get; }
    /// <summary>The Markdown source when <see cref="EditorMode"/> is "markdown"; the HTML is rendered from it on save.</summary>
    string? Markdown { get; }
    /// <summary>"rich" (the visual editor, the default) or "markdown". See <see cref="Blog.Core.Services.EditorModes"/>.</summary>
    string EditorMode { get; }
    bool IsMarkdown { get; }
}
