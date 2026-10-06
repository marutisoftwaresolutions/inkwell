# ADR-0026: The gallery block is one markup written by every editor, linked images first, lightbox second

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.7/v1.0.8 stretch "Gallery block (with the A4 media-update action)"; ADR-0022 (Markdown)

## Context

A post could hold single images only. Four editors write post and page bodies:
- the post builder (Edit);
- the rich-text editor (Quill, on new posts);
- the page builders (Create and Edit, separate copies);
- the Markdown editor.

There is no sanitizer between them and the reader, and no lightbox anywhere. The roadmap ties the gallery to
editing an image's alt text in the media library, which did not exist (`Media.AltText` and `Caption` were never
written).

## Decision

- **Media details first.** `GET/POST /admin/media/details/{id}` edits an image's alt text and caption:
  - it is a tracked editor;
  - errors are shown at the field;
  - it is audited as `Media.Updated`, with before and after values.
  The media API returns alt text, caption and size. The library shows "Add alt" on images without one.
- **One markup, built in one place.** `wwwroot/js/inkwell-gallery-editor.js` builds and parses
  `<div class="comp-gallery" data-columns="N"><figure class="comp-gallery-item"><a href=…><img …></a><figcaption>…`.
  - Each image links to its own file, so the gallery works with no JavaScript.
  - An image with no alt text gets an `aria-label` on its link, so the link is never unnamed.
  - The markup has no blank lines, so Markdown keeps it as one HTML block. Switching a post with a gallery to
    Markdown renders it unchanged.
- **Every editor uses it:**
  - the builders get a palette item, a settings panel (columns, per-image alt and caption, reorder, remove, add)
    and recognise saved galleries;
  - Quill gets a `GalleryBlot` and a `/gal` slash command;
  - Markdown gets a toolbar button.
  The media picker gains a multiple-selection mode that keeps the order images were picked in.
- **Reader.** A CSS grid in `neutral-blog.css` (two columns on phones), and a self-hosted lightbox
  `wwwroot/js/inkwell-gallery.js` on a native `<dialog>`:
  - Escape closes it and focus returns to the image that opened it;
  - arrow keys move between images;
  - modified clicks still open a new tab.
  The script is sent only to pages whose HTML contains a gallery.
- **Export.** Gallery links and images are `/uploads/` references, which both exporters already rewrite.

## Options not taken

- **A Quill-only or builder-only gallery**, which would give four editors three behaviours.
- **A gallery stored as JSON** and rendered on the server: every surface that reads `Html` (feeds, search, exports,
  `llms.txt`) would need to learn it.
- **A third-party lightbox:** the platform ships no CDN script, and the native `<dialog>` already provides the
  modal behaviour.

## Consequences

- Alt text is copied into the post when the gallery is placed. Changing it later in the library does not rewrite
  existing posts. The details page says so, and the builder's settings panel edits it in place. Galleries inserted
  in the rich-text editor are edited after the first save, in the full editor.
- The builder views are still four inline copies. The gallery added the same five hooks to each, and a source
  scan (`GalleryBlockTests`) keeps them in step.

## Proof

- `GalleryBlockTests`: shared markup, wiring in every editor, Markdown pass-through, export rewriting, and a guard
  against line breaks inside script strings (one shipped briefly and disabled the slash menu).
- `AdminPagesSmokeTests.Image_details_render_and_save_alt_text_and_caption`.
- 37 checks in headless Edge: picker order, settings actions with focus restore and announcements, dirty marking,
  lightbox keyboard, Escape and focus return, 400 px.
