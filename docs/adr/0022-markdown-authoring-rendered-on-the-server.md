# ADR-0022: Markdown authoring, rendered on the server into the HTML everything already reads

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Markdown authoring"; ADR-0020 (exporter); importer plan

## Context

Writers coming from Ghost, Jekyll, Hugo, PluXml and Dotclear expect to write in Markdown. Inkwell
offered only the visual editors (the block builder on Edit, Quill on Create). The owner chose a
self-hosted open-source editor over a plain textarea (2026-10-05). Every reader surface already
reads `Posts.Html` and `Plaintext`: pages, feed, sitemap freshness, search, `llms.txt`, the table of
contents, exports and webhooks.

## Options considered

1. **Render Markdown on read:** every surface would have to learn a second format. It is slower, and
   the HTML and the source could disagree.
2. **Render in the browser and post HTML:** the client becomes the authority for what is published,
   which is the same class of hole ADR-0018 closed for status.
3. **Render on the server at save time:** store `Markdown` as the source and `Html` as its rendering.
   Nothing downstream changes.

## Decision

Option 3.

**Data.**
- `Posts.Markdown` (NVARCHAR(MAX) NULL) holds the source.
- `Posts.EditorMode` (NVARCHAR(20) NOT NULL DEFAULT `'rich'`) records which editor the post uses.
  Existing posts are `rich` and untouched.
- Schema trio: `DBScripts/2026-10-05_add-markdown-to-posts.sql`. Pages are out of scope for this
  release.

**Rendering.** `Blog.Core/Services/MarkdownRenderer` uses Markdig (BSD-2, pure, in Core):
- CommonMark plus pipe tables, strikethrough, task lists, footnotes, autolinks and list extras;
- no heading ids, because the table of contents assigns them to every post alike;
- raw HTML passes through, the same trust as the visual editor (authors are staff).

**Saving.** For a Markdown post, `PostsController.ApplyEditorMode` sets `Html` and `Plaintext` from
the Markdown on every save, ignoring any posted `Html`. An edit uses the stored mode, never a posted
one.
- **Switching** is its own confirmed, audited, revision-recorded action (`SwitchEditorMode`).
- **Visual → Markdown** uses the post's HTML as the Markdown source, and is refused when
  `MarkdownRenderer.RendersUnchanged` says the rendering would differ (a blank line inside an HTML
  block). All 64 posts on the dev site switch unchanged, and a test checks this.
- **Markdown → visual** keeps the rendered HTML.

**Revisions.** The source and mode ride in the existing structured JSON bag, omitted when null, so
rich-post snapshots compare equal to pre-upgrade ones. No schema change.

**API.**
- `markdown` makes Markdown the source.
- `content` (HTML) makes HTML the source again, so a later Desk save cannot re-render over an API edit
  from stale Markdown.

**Editor.**
- **Library:** EasyMDE 2.21.0 (MIT, bundles CodeMirror 5), vendored to `wwwroot/lib/easymde`.
  Integrity was checked against the npm registry's SHA-512.
- **No CDN calls:** its two CDN fetches are disabled (`autoDownloadFontAwesome: false`,
  `spellChecker: false`, the browser's spell check instead), and the toolbar uses inline SVG.
- **Preview:** server-rendered by the same pipeline, so the preview is what readers get.
- **Styling:** Desk tokens, so it follows both colour schemes.

**Import.** The Ghost importer recognises a Markdown source:
- Ghost 0.x's `markdown` field;
- a Mobiledoc or Lexical document made only of Markdown cards.

With **Keep posts written in Markdown as Markdown** (off by default), such posts arrive as Markdown
posts, with image URLs remapped like the HTML's. Without the option, a Markdown-only post that has
no rendered HTML is now rendered properly rather than flattened.

**Removed.** `POST /admin/posts/autosave/{id}` had no caller. It overwrote any post's title and body
with no anti-forgery token, no ownership check and no audit, so it was a cross-site request
forgery hole. It is deleted, and a source scan now requires anti-forgery on every POST in the
posts controller.

## Consequences

- Easier: Markdown writers have a native mode, and nothing that reads posts had to change.
- Harder:
  - two new dependencies (Markdig, EasyMDE);
  - an HTML edit made through the API turns a Markdown post back into a visual one (by design);
  - pages still use the visual editor only.
- Now maintained: `MarkdownAuthoringTests`:
  - the renderer and its extensions;
  - lossless switch samples, plus every dev post;
  - the refusal case;
  - the revision round trip;
  - Ghost Markdown detection, including mixed documents rejected;
  - source scans: server-side rendering, stored mode, no CDN, anti-forgery on every POST, the API rule.
- Also maintained: the smoke test renders the Markdown create page.

## Proof

- Code:
  - Core: `Blog.Core/Services/MarkdownRenderer.cs`, `Blog.Core/Domain/Post.cs`, `RevisionSnapshot`;
  - repository: `PostRepository` insert and update;
  - controllers: `PostsController` (ApplyEditorMode, MarkdownPreview, SwitchEditorMode, Create(mode)),
    `Api/PostsApiController.ApplyBody`;
  - views: `Views/Posts/{_MarkdownPane,_MarkdownEditorScripts,_EditorModeForm}.cshtml`, `Edit`,
    `Create`;
  - import: `GhostImporter` (`GhostContent.MarkdownSource`), `ImportProcessor`, `Import/Configure`;
  - assets: `wwwroot/lib/easymde/*`.
- Schema: `DBScripts/2026-10-05_add-markdown-to-posts.sql`, `MigrationService.cs`, `init.sql`
  (empty-database diff clean, 377 columns).
- Tests: `Blog.Tests/MarkdownAuthoringTests.cs`.
