# ADR-0020: Content export as streamed downloads, without job tables

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `docs/plans/EXPORTER-PLAN.md` (Phase 0–2); `.gsd/ROADMAP.md` v1.0.8 core "Content exporter — Phase 1"; Data Portability rule; ADR-0019 (backups)

## Context

Inkwell imports from WordPress and Ghost but could not export to either, so the "no lock-in" promise
was unkept. Phase 1 is a file download in each platform's native import format. Direct push is
Phase 2.

The plan's Phase 0 called for `ExportJobs`/`ExportItems` tables mirroring the importer, to share its
progress, resume and per-item report. That design is built for a push: hundreds of HTTP calls to a
remote site, any of which can fail and must be resumed without double-posting. A file download has
none of that. It is one pass over the database into one file, and either the file is complete or it
is not.

The formats were checked against the targets' own sources on 2026-10-05:
- **WordPress:** `wordpress-importer` `class-wxr-parser-simplexml.php` on `master`. It requires
  `wp:wxr_version` matching `^\d+\.\d+$` and reads the elements the writer emits.
- **Ghost:** docs.ghost.org/migration/custom, plus `ghost/core/core/server/data/schema/schema.js` on
  `main`. That schema shows:
  - `meta_title`, `meta_description`, `og_*`, `twitter_*`, `feature_image_alt` and
    `feature_image_caption` live in `posts_meta`;
  - `canonical_url` is on `posts`;
  - `users.email` is required and unique;
  - length validations: title 255, custom excerpt 300, meta title 300, meta description 500,
    image alt 191.

## Options considered

1. **Job tables plus the job runner** (as the plan said): consistent with the importer, but it adds
   two tables, a resume model and a polling UI for work that takes seconds to a minute.
2. **A queued job writing a file**, like backups: no tables, but the admin waits for a tick and then
   downloads. That is a two-step flow for something that can be immediate.
3. **Generate during the request into a temporary file outside the web root, then stream it back with
   delete-on-close.** No tables, no stored artifact, nothing to purge but crash leftovers.

## Decision

Option 3.

- **Pure writers in Core:** `WxrExportWriter` (`XmlWriter`) and `GhostExportWriter` (`Utf8JsonWriter`)
  take an `IExportSource`.
- **Data in Infrastructure:** `ExportRepository` loads taxonomy, authors, series, comments and media
  alt text as small maps, then streams post and page bodies with an unbuffered Dapper reader. Memory
  holds metadata, never the bodies or the artifact.
- **Temporary file:** `ExportService` writes to `App_Data/exports/{guid}.tmp` (`Exports:Path`) and
  returns a `FileOptions.DeleteOnClose` stream. Files older than an hour, left by a crash, are swept on
  the next export.
- **Ghost ZIP:** the JSON at the root, and each referenced upload under `content/images/…`, with the
  links rewritten to match.
- **WordPress images:** they are attachment items whose `wp:attachment_url` is the live URL. Body
  references are made absolute so the importer rewrites them, and attachment ids come from a separate
  range so `_thumbnail_id` can be written before the attachment item.
- **Inkwell-only blocks:** answer capsule, key facts, roundup, how-to, FAQ and series render as plain
  HTML in the body (`ExportHtml.PostBody`). WordPress also keeps the raw JSON in `_inkwell_*` post meta.
  Ghost has nowhere to keep it.
- **SEO fields for WordPress:** written for both Yoast and Rank Math. Each plugin reads only its own
  keys, which answers the plan's open question 1 without asking the operator to choose.
- **Emails:** author and commenter emails are opt-in. Ghost requires an email per user, so without
  them Ghost gets no `users`, and the fidelity screen says posts will be credited to the importing
  account. Commenter IPs are never read.
- **Fidelity matrix:** `ExportFidelity.For` builds it from the selection's real counts, and the screen
  shows it before the download.
- **Access:** Admin-only, anti-forgery on the download, audited as `Export.Started` and
  `Export.Completed` or `Export.Failed` with options and counts only. In cloud mode the screen answers
  404, the same as Backups (ADR-0019).

## Consequences

- Easier: no schema change, and nothing at rest. The export exists only while it downloads.
- Harder:
  - a very large site makes the admin wait inside one request (the screen says so, and the download
    runs through `fetch()` with the busy bar);
  - the browser holds the file as a blob before saving it, which suits sites up to a few hundred
    megabytes of images.
- Deferred to Phase 2: push. It is also where `ExportJobs`/`ExportItems` belong, because push is what
  needs resumable per-item state.
- Deferred, with reasons:
  - **Redirect sidecars:** neither import needs them, and the formats are unverified.
  - **A comment CSV for Ghost:** Ghost cannot import it.
- Now maintained: `ContentExportTests`:
  - writer output parsed back;
  - the CDATA split and UTF-8 round-trip;
  - attachments and the thumbnail link;
  - comment threading, with no IPs and no unrequested emails;
  - Ghost shape, tag merging, length limits and the users rule;
  - the fidelity rules;
  - source scans;
  - a full export of the dev site to both formats.

## Proof

- Code:
  - Core: `Blog.Core/Services/Export/*`;
  - Infrastructure: `Blog.Infrastructure/Data/Repositories/ExportRepository.cs`;
  - Web: `Blog.Web/Services/Export/ExportService.cs`, `Blog.Web/Controllers/ExportController.cs`,
    `Blog.Web/Views/Export/Index.cshtml`.
- Schema: none.
- Tests: `Blog.Tests/ContentExportTests.cs`.
- Real-instance import: recorded in `docs/plans/EXPORTER-PLAN.md` §11.
