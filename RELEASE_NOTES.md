# Inkwell v1.0.9

**Release date:** 2026-10-25
**Type:** Feature, security and fix release (backward-compatible for readers and content: existing tenants keep their layout, theme, content and settings; sign-up, the API and Docker change, see Upgrade notes)

v1.0.9: **"Close the loops."** v1.0.8 opened Inkwell up; each of its features stopped at a known
edge. This release finishes those edges. Push your content straight into WordPress or Ghost, write
pages in Markdown, let a CDN cache your posts, and see which version is running. It also adds contact
forms and responsive images, and the Desk gets a new shell. The Docker deployment now actually works.

## Highlights

### 🚚 Push straight to WordPress or Ghost

- **Admin → Export now sends your content to a running WordPress or Ghost site** through its API, as
  well as downloading a file. Images are uploaded once each, and running the push again sends only
  what is missing. The credential is held in memory while the push runs and is never stored, logged
  or audited. Tested by real pushes of 67 posts and pages into WordPress 7.1.3 and Ghost 6.70.0.

### 🧱 The website pieces every site needs

- **Contact forms.** Admin → Forms builds a form, and the new **Form** block places it on a page.
  Readers can send it without JavaScript. It uses the same spam checks as comments, stores
  submissions for as long as you choose, and can email them to you and export them as CSV.
- **Responsive images.** Uploads get 480, 800 and 1200 px WebP copies, and pages offer them with
  `srcset`, width and height. On the development site a phone loads about 77 KB of images for the
  home page instead of about 1 MB. Images already on your site get their copies in the background.
- **Post pages can be cached by a CDN,** like the home page. Comment forms fetch their tokens when a
  reader reaches them, and commenting still works without JavaScript.

### ✍️ Write and publish with fewer gaps

- **Pages in Markdown,** like posts, with a lossless switch either way.
- **Custom fields over the API,** and a new `GET /api/v1/pages/{slug}` for headless front ends.
- **Accessibility checks** in the post and page editors, a **content calendar**, and **right-to-left
  sites**: every layout and preset mirrors for Arabic, Hebrew, Persian, Urdu and other RTL languages.

### 🗂️ A new Desk

- **A grouped, collapsible sidebar** with count badges, a **Ctrl/Cmd K command palette**, and a
  dashboard that lists what needs your attention.
- **A precompiled stylesheet:** the Desk no longer loads a 407 KB in-browser compiler on every page.
- **The running version** shows in the Desk (never on public pages), so you can confirm a deploy.
- **Analytics and AI Crawlers open in seconds on busy sites.** They used to take minutes or fail,
  and now read a daily summary through one stored procedure. Page-view details can optionally be
  deleted after a set number of days.

## Security

- **Sign-up closes once a site has its first account.** It used to stay open and give every new
  account the Author role, which can publish. If your site has been public, review Users for accounts
  you did not create.
- **API writes follow the Desk's permissions,** tokens are re-checked against the account on every
  request, and the example `Jwt:Key` no longer works.
- **The API no longer exposes commenters' email or IP addresses,** and `GET /api/v1/posts/{slug}`
  returns only what readers see.
- **Disabling or deleting a user ends their Desk session** within a minute instead of up to seven days.

## Fixed

- **The Docker deployment did not work.** The image did not build (a user-ID clash with the .NET 10
  base image, and a missing `DBScripts/` input), and nothing created the database on first boot.
  `docker compose up` now builds and seeds the database through a one-off `db-init` service, and a
  GitHub Actions workflow boots the full stack on an empty database on every push. Thanks to the
  community member who reported it and shared a working fork.
- The API's write endpoints answered with a server error, because API sign-in was never switched on.
- Posts written in the Desk never counted views, so they never appeared in "Most read".
- Switching editors with unsaved changes could unpublish a live page or post.
- Link Audit listed links to scheduled posts as broken after the posts went live.
- Comments could be posted with comments switched off.
- Four layouts scrolled sideways on phones, and header menus were see-through on sites with saved
  theme colours.
- Smaller fixes to the Markdown preview, the block builders, Feed header menus and validation
  messages are in the CHANGELOG.

## Upgrade notes

- **Schema is automatic.** On startup, `MigrationService` adds the tables `ExportPushes`,
  `ExportPushItems`, `Forms`, `FormSubmissions`, `PageViewDaily` and `CrawlerVisitDaily`, the
  report stored procedures, and new columns on `Pages`. It also sets missing post view counts to 0.
  For a manual upgrade, run these idempotent scripts from `DBScripts/` in order:
  `2026-10-14_add-markdown-to-pages`, `2026-10-14_create-export-pushes`, `2026-10-14_create-forms`,
  `2026-10-15_default-post-viewcount-to-zero`, `2026-10-22_create-pageviewdaily-table`,
  `2026-10-22_create-crawlervisitdaily-table`, `2026-10-22_create-report-stored-procedures`.
  The two `*-to-settings` scripts change no schema. A fresh install gets the full schema from
  `DBScripts/init.sql` alone. Before this release, that was verified by diffing `sys.columns`
  against a live database in both directions, and re-running it was shown to change nothing.
- **Large analytics tables:** if `PageViews` or `CrawlerVisits` holds more than 200,000 rows, the
  covering index is not built at startup. The dashboard shows a "Needs attention" line until you run
  `DBScripts/2026-10-22_add-covering-indexes-for-report-procedures.sql` in a quiet period.
- **Sign-up is closed after the first account.** Invite people from Users → Invite.
- **API:** set a real `Jwt:Key` of at least 32 bytes, plus `Jwt:Issuer` and `Jwt:Audience`, or the
  API stays off. `POST /api/v1/comments` and `POST /api/v1/media` are removed, and
  `GET /api/v1/posts/{slug}` returns a smaller, explicit shape.
- **Docker:** `docker compose up` now creates and seeds the database itself. If you run a single
  container against your own SQL Server, create the database and run `DBScripts/init.sql` against it
  first. The image runs as the base image's `app` user (UID 1654).
- **Push to WordPress or Ghost** is Admin-only, accepts only public `https://` targets, and is hidden
  in cloud mode.
- **Backward-compatible otherwise.** Forms, page-view retention, push and Markdown pages change
  nothing until you use them. Left-to-right sites look exactly as before.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
