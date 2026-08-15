# Changelog

All notable changes to Inkwell are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

---

## [1.0.4] — 2026-08-15

### Added

- **Content Health dashboard** — a new Admin → Content Health screen turns the maintenance fields that already exist on every post into a worklist: reviews that are overdue or due within 30 days, posts never verified, posts missing a Key Facts or FAQ block, meta descriptions that are absent or long enough to be truncated in search results, and titles still promising a year that has passed. Tiles double as filters, each row links straight to the editor, and the default view shows only what needs attention. Read-only — it surfaces work, it never edits. Available to Editors and Admins.
- **Filter-URL canonicalization** — a single-value home-feed filter (`/?tags=cloud-based-ehr`, `/?category=practice-management`) now issues a 301 to the matching `/tag/{slug}` or `/category/{slug}` archive, so filter permutations stop competing with the canonical archive as separate indexed URLs. Multi-value combinations have no canonical equivalent and are unchanged (`noindex,follow`), search results are untouched, and pagination is preserved through the redirect. Only well-formed single slugs redirect, so a crafted query string cannot be turned into a redirect target.
- **Tag archives are indexable once they have depth** — a tag archive holding at least three posts is now a real topic page instead of being unconditionally `noindex`; thinner ones stay out of the index as before. `sitemap.xml` lists only the archives that are actually indexable, so it no longer advertises `noindex` URLs.
- **Retire a URL with 410 Gone** — redirect rules now carry a status code, so a URL can be moved (301, the default), temporarily moved (302), or **intentionally retired (410 Gone)**. A 410 serves a dedicated "no longer available" page that points readers at current content, and tells search engines the URL was removed on purpose — which drops it from the index far faster than a 404, whose repeated re-crawling keeps stale titles and snippets in search results. Existing redirect rules are unaffected and continue to serve 301.
- **IP Firewall — automatic blocking of hostile addresses** — every failing request is now weighed by a threat scorer, and addresses that cross the threshold block themselves. Exploit probes (`.php`, `wp-admin`, `.env`, `.git`, path traversal, SQL-injection payloads, app-server consoles) score 5 points, a rejected sign-in scores 3, and an ordinary 404 scores 1; at 10 points within 10 minutes (both configurable) the address is blocked — 24 hours on the first offense, a week on the second, permanently on the third — and every subsequent request is refused with a bare 403 before routing, views, or logging run. A new **Admin → Security** screen shows active blocks with their reason and denied-request count, a live watchlist of addresses currently building a score, and the thresholds, allowlist, and notification settings; addresses can also be blocked or allowlisted by hand, and **Admin → Error Monitor** now records each signature's last client IP with a one-click Block button. Blocks are stored in a new `IpFirewallRules` table so they survive a restart, while enforcement reads an in-memory snapshot so an ongoing attack costs no per-request database work. Safe by default: loopback and private ranges are never blocked, signed-in staff are never scored, search and AI crawlers (Googlebot, Bingbot, GPTBot, ClaudeBot, …) are never blocked for 404s, proxy headers are ignored unless a trusted proxy is declared, and the firewall fails open if the database is unreachable. Optional email alerts reuse the error-notification recipients. Every block, unblock, allowlist entry, and settings change is written to the audit trail.
- **WordPress & Ghost content importer** — migrate an existing blog from Admin → Import. Upload a WordPress WXR (`.xml`) export or a Ghost JSON (`.json`/`.zip`) export, preview what it contains, choose what to bring in (posts, pages, categories/tags, images, comments; published-only filter; slug-conflict policy), then run the import with a live progress bar and a per-item exception log. Posts, pages, categories and tags are created; referenced images are downloaded, converted to WebP and re-linked in the content; old permalinks become redirects to preserve SEO; imported HTML is sanitized. Ghost bodies use the rendered HTML when present, with a best-effort Lexical/Mobiledoc conversion otherwise; for a Ghost `.zip` that bundles the `content/images` folder, images are read straight from the archive (no live source site required). The import is batched and resumable, finishes with a summary and a downloadable `.xlsx` report, and can be **undone** (removes the posts, pages, images and comments it created; categories and tags are kept). Step-by-step export instructions for both platforms are shown in the importer. Admin-only; new tables only (backward-compatible).
- **Series & collections** — group posts into an ordered reading sequence (e.g. a multi-part guide). A new Admin → Series area creates series and manages their posts and order (add, reorder, remove). Each series gets a public page at `/series/{slug}` listing its posts in order, plus a `/series` directory of all series. Posts that belong to a series show an in-article "Part N of M · Previous / Next" navigation banner. Series pages emit `CollectionPage` + ordered `ItemList` + `BreadcrumbList` structured data and are included in `sitemap.xml`. Backward-compatible: new tables only, and blogs that never create a series are unaffected.

### Changed

- **Shared image pipeline** — the media library's automatic WebP conversion is now a shared service reused by the content importer, so both direct uploads and downloaded/imported images are optimized through one code path.

### Fixed

- **HEAD requests no longer return 405 Method Not Allowed** — routing matches HTTP methods exactly and every page route is declared as GET-only, so every HEAD request was refused: `HEAD /`, `HEAD /robots.txt`, even HEAD for a URL that does not exist (405 instead of 404). Uptime monitors, link checkers and some crawlers use HEAD precisely because it is cheap, and the refusals also filled the Error Monitor with 405 rows that resembled attack traffic. HEAD now returns exactly what GET would — same status, same headers, no body. Static files were already correct and are untouched, HEAD requests are excluded from page-view analytics, and the Error Monitor records them as HEAD rather than GET.
- **Category and tag archives render the full listing chrome again** — they share the home-feed view but never populated its filter chips, popular-posts sidebar, or the tenant's configured layout, so those pages silently fell back to the default layout with no filters. All three listing routes now populate the same view data, and an archive seeds its own filter selection so adding a second filter from an archive page keeps the first.
- **Post pages no longer 500 when a post has no categories or tags** — the related-posts query built an invalid `ORDER BY (0)` (a bare integer that SQL Server treats as a column ordinal) for posts with no topic overlap, throwing a `SqlException` and returning HTTP 500 on the article page. It now falls back to recency ordering. Affected any category-less/tag-less published post (e.g. freshly imported or bulk-loaded content).

---

## [1.0.3] — 2026-07-31

### Added

- **Automatic WebP conversion on upload** — raster images (JPEG/PNG/GIF) uploaded to the media library are re-encoded to WebP for smaller, faster-loading assets, and their dimensions are captured in the same pass. SVG and images already in WebP pass through unchanged; if an image can't be decoded, the original bytes are stored as a fallback. Completes the media-library image-management capability.

---

## [1.0.2] — 2026-07-29

Public release: a full SEO / AEO (AI-search) overhaul, the **Verdict** scored-roundup content system, new blog **layouts**, **IndexNow** instant indexing, author **E-E-A-T** pages, and an **Error Monitor** subsystem. Backward-compatible — existing tenants keep their layout, theme, and content unchanged.

### Added

- **Verdict roundup theme** — a scored "Top N Best … Software" comparison format for buyer's-guide posts. Adds the `Verdict` layout (index, post card, single-post template) and a `clinic` Inkwell preset (white / near-black / blue CTA, all-sans headings). A post renders as a Verdict roundup whenever it carries roundup data, independent of the site's `layout-post` setting.
- **`Posts.RoundupJson`** column (nullable, JSON blob mirroring the `FaqJson` pattern) storing per-entry rank, score, weighted sub-scores, best-for, standout, pros/cons, CTA, and logo. Authored via a repeater UI in the post editor (shared by Create and Edit).
- **Roundup structured data** — `ItemList` → `SoftwareApplication` per entry with `Review`, `reviewRating`, `positiveNotes`/`negativeNotes`, emitted alongside the existing Article/Breadcrumb/FAQ JSON-LD. Makes "best-of" pages eligible for rich results and AI-answer citation.
- **Internal-aware roundup CTAs** — entry links that point at an internal review (e.g. `/eyefinity-ehr-review`) render as in-tab dofollow links to build topic clusters; external vendor links stay `nofollow noopener sponsored`.
- **Roundup entry `website`** — each roundup entry carries the product/company's real URL, rendered as an outbound "Visit website" reference (`rel="nofollow noopener"`) and used as the `SoftwareApplication.url` in the `ItemList`/`Review` JSON-LD. Governed by the **Content External-Link Rule** in `CLAUDE.md`.
- **First-party roundup disclosure** — a roundup entry whose website is the operator's own domain (e.g. `opto-soft.com`) is flagged as first-party: it shows an "Our platform" tag and an on-page disclosure, and is deliberately omitted from the `Review`/`AggregateRating` structured data (still listed in the `ItemList` as a `SoftwareApplication`), preventing self-serving ratings that violate search-engine rich-result policy.
- **Catalog layout** — a single-column, boxed-card blog index with left-aligned thumbnails, a horizontal category filter bar ("All Posts" + per-category tabs), and numbered pagination, matching a modern SaaS/company-blog style. Selectable from Admin → Theme (Modern family); navbar/footer map to the Neutral variants, and unknown/legacy layout values fall back to Neutral.
- **Error Monitor** — a global exception handler + status-code logging records every 5xx and 4xx, grouped by signature (status + normalized path + exception type) so repeated/similar errors collapse into a single counted row. A new Admin → Error Monitor page (Admin-only, read-only) lists groups with occurrence counts, first/last-seen, path, exception type/message, and summary tiles, with status/keyword filters. Backed by a new `ErrorLogs` table.
- **Error alert emails** — optionally email one or more recipients (Admin → Settings) the first time a new server-error (5xx) signature is detected; repeats and 404 noise never email. Uses the existing SMTP settings; a no-op when disabled or unset.
- **IndexNow instant indexing** — Admin → Settings can enable IndexNow and set (or auto-generate) an API key. When enabled, publishing/updating a post pings `api.indexnow.org` so Bing, Yandex, Seznam, and Naver re-crawl within minutes. The ownership key is served automatically at the site root `/{key}.txt` (root placement authorizes all URLs per the protocol). Per-tenant, disabled by default, best-effort — never blocks a publish.
- **Author pages with E-E-A-T structured data** — each author gets a public page at `/author/{slug}` listing their published articles and emitting `Person` JSON-LD (name, job title, credentials, bio, linked profiles) plus an `og:profile` card. Post bylines link to it; author pages are in the sitemap.
- **Automatic author slugs** — every user is assigned a unique URL slug (new users on creation, existing users backfilled once on startup), with an editable "Author Page URL" field in the profile editor.
- **`llms-full.txt`** — an expanded companion to `llms.txt` at `/llms-full.txt` indexing every published article (title, URL, one-line summary) grouped by category, so AI answer engines can map the full breadth of a site's content.
- **CollectionPage structured data** on listing pages (home, category, tag), tied to the site's `WebSite` entity.
- **Key Facts / "At a glance" block** — a post-editor field (label/value repeater) stores quotable facts (`Posts.KeyFactsJson`), rendered as a semantic definition list near the top of the article and highly extractable by AI answer engines.
- **How-To step-by-step guides** — a post-editor field (`Posts.HowToJson`) captures ordered steps, rendered as a visible numbered list and emitted as `HowTo` structured data.
- **Configurable content language** — a `Content Language` setting (BCP-47, e.g. `en`, `es`, `hi`) drives the page `lang` attribute, `og:locale`, `hreflang` (self + `x-default`), and schema `inLanguage`.
- **Entity/knowledge-graph signals** — `Organization` JSON-LD advertises topics of expertise (`knowsAbout`, from categories), and `llms.txt` includes an explicit identity section (exact name, official URL, citation guidance, disambiguation note).
- **Search-engine site verification** — Admin → Settings accepts Google Search Console and Bing Webmaster verification codes, rendered as `<meta name="google-site-verification">` / `<meta name="msvalidate.01">`.
- **Sitelinks search box** — the `WebSite` structured data includes a `SearchAction`, backed by a dedicated `/search?q=` route.
- **Image sitemap** — the sitemap lists each post's feature image (`<image:image>`).
- **Meta-description length helper in the editor** — a live character count with green/amber/red guidance for the 120–158 character range.
- **`WebSite` structured data** and enriched **`Organization`** JSON-LD (stable `@id` plus a full `sameAs` list from all configured social links) on every public page.
- **Universal page metadata** — canonical URL, `author`, `theme-color`, and default Open Graph / Twitter Card tags render on all public pages (home, index, category, tag, CMS pages), not just posts.
- **AI-crawler discovery hint** — `<link rel="alternate" type="text/plain" href="/llms.txt">` in the page head.
- **Richer article meta** on post pages: `og:url`, `og:image:alt`, `og:locale`, `article:author`, `article:section`, `article:tag`, `article:modified_time`, `twitter:site`, `twitter:creator`, `twitter:image:alt`.
- **`SEO-AEO-PLAN.md`** — a living SEO/AEO audit, prioritized action plan, and progress tracker.

### Changed

- **Precompiled Tailwind CSS on the public site** — the public frontend serves a small static stylesheet instead of compiling Tailwind in the browser on every page load, eliminating the largest render-blocking script and the flash of unstyled content. Runtime theming (presets/custom colors) is unchanged; regenerate with `npx tailwindcss` or `-p:BuildTailwindCss=true`.
- **Faster first paint** — Highlight.js and Lucide now load only on pages that need them (code blocks / `data-lucide`), removing render-blocking scripts from the common text-only article path.
- **Critical fonts preloaded** — the two brand fonts (Geist, Source Serif 4) are preloaded, with a `preconnect` to the analytics origin when GA is configured.
- **Optimized article images** — feature images get `fetchpriority=high` for LCP while related-post thumbnails lazy-load.
- **Per-tenant `llms.txt`** — the `/llms.txt` summary is generated from each blog's own name, description, and categories.
- **Smarter related posts / stronger internal linking** — "See Also" links and related-post cards are ranked by topical relevance (a shared category outweighs a shared tag) instead of pure recency, and the module always fills (recency backfill) so every article carries internal links.
- **Richer home-page meta description** — the home listing emits a fuller, keyword-relevant description built from the site's own tagline and top categories (truncated to ~158 chars); tenant-neutral.
- **Consistent page titles** — every public page ends with a single ` | {Site Name}` brand suffix (added when missing, never doubled).
- **Cleaner auto-generated descriptions** — a missing summary is cut on a word boundary with an ellipsis instead of mid-word.
- **Thin/duplicate pages kept out of the index** — tag archives, on-site search, and paginated/query-filtered listings emit `noindex,follow`.
- **Sitemap no longer capped at 1000 posts** — split across a sitemap index and chunked child sitemaps.
- **Roundup ratings made policy-safe** — entries carry only their genuine editorial `Review`, not a self-serving per-entry `AggregateRating`.
- **Sitemap** now includes category, tag, and published CMS page URLs, and stamps the homepage `lastmod` from the most recent post update.
- **`robots.txt`** AI-crawler allow-list expanded (OAI-SearchBot, ChatGPT-User, Claude-Web, Perplexity-User, Bingbot, DuckDuckBot, Amazonbot, cohere-ai, Meta-ExternalAgent); `Bytespider` added to the blocklist.

### Fixed

- **`llms.txt` / `llms-full.txt` / `sitemap.xml` / `robots.txt` are fully dynamic** — a stale static `wwwroot/llms.txt` could shadow the per-tenant dynamic route (serving one hardcoded, inaccurate AI summary to every tenant). The app now deletes any such stale static SEO file on startup so the correct dynamic route always serves.
- **Absolute `og:image` / `twitter:image`** — relative `/uploads/...` image paths are promoted to fully-qualified URLs on every public page, so social/AI scrapers that reject relative paths work.
- **Inbound mixed-case URLs now 301 to lowercase** — `/Best-Optometry-EHR-Software-2026` permanently redirects to its lowercase canonical instead of serving a duplicate 200 (static assets and non-GET requests untouched).
- **Sensible default database connection** — running without a configured `DefaultConnection` falls back to SQL Server LocalDB (the data layer is SQL Server only); the unused SQLite driver dependency was removed.
- **Build/publish no longer breaks on missing feature images** — the `Blog.Web.csproj` feature-image list is now a self-healing glob instead of ~34 pinned paths.
- **Category and tag pages had blank titles/descriptions** — they now render a proper `<title>` and meta description derived from the category/tag name.
- **`robots.txt`** used the invalid token `Googlebot-Extended`; corrected to Google's actual AI-grounding token **`Google-Extended`** so Google AI/Gemini permissions are honored.
- Post pages no longer emit a duplicate `<link rel="canonical">`; the canonical is emitted exactly once by the shared layout.

---

## [1.0.1] — 2026-06-27

### Added

#### Analytics Dashboard
- `PageViewMiddleware` — fire-and-forget page view tracking using root `IServiceProvider` scope (never blocked by request lifetime)
- `PageViews` table with UTM parameters (`source`, `medium`, `campaign`, `term`, `content`), IP hash, user agent, country, and region columns
- `VisitSourceClassifier` — classifies traffic into Organic, Direct, Social, Email, Paid, and Referral buckets
- `AnalyticsController` — admin-only dashboard at `/admin/analytics` with daily/weekly trends, top pages, top referrers, and traffic source breakdown
- `AnalyticsDashboardViewModel` — structured view model for the analytics page
- Geo-location resolution (country + region) from IP address

#### Audit Trail
- `AuditLogs` table with full schema: action, entity type/id/name, before/after JSON snapshots, user info, IP address
- `IAuditRepository` + `AuditRepository` — Dapper implementation with paginated, filterable queries
- `AuditService` — thin service that resolves request context (owner, user, IP, UA) and logs asynchronously; swallows all exceptions so audit never crashes the caller
- `AuditActions` static constants class — 33 `EntityType.Verb` string constants (e.g. `Post.Published`, `Comment.Deleted`, `Auth.LoggedIn`)
- `AuditController` — admin-only, read-only at `/admin/audit`; supports filter by entity type, action, user, date range; CSV export up to 10,000 rows
- Audit Trail view — color-coded action badges, expandable before/after JSON diff rows, pagination
- Audit nav link in `_AdminLayout.cshtml` (Admin-role gated)
- **All 12 admin controllers wired**: PostsController, CommentsController, CategoriesController, TagsController, MediaController, PagesController, UsersController, AccountController, SettingsController, ThemeSettingsController, NewsletterController, SubscribersController

#### Members & Newsletter
- `Members` table and `MemberRepository` — member directory with label-based segmentation
- `MemberLabels` and `Labels` tables for subscriber segmentation
- `Newsletters`, `Emails`, `MemberNewsletters` tables for newsletter campaign tracking
- `NewsletterController` and `SubscribersController` — compose/send newsletters, manage subscribers, CSV import/export
- `SmtpEmailService` — built-in SMTP transactional email

#### Redirects
- `Redirects` table with source path, destination URL, HTTP status code, and hit count
- `RedirectRepository` and `RedirectsController` — full CRUD with rule-based redirect middleware

#### SEO & Content
- `OgImageController` — programmatic OG image generation at `/og-image/{slug}`
- `FaqJson` column on `Posts` — JSON-LD FAQ schema stored per post
- `LastVerifiedAt` / `NextReviewAt` columns on `Posts` — clinical review workflow for medical content
- `OptometryTaxonomySeeder` — seeds 200+ optometry-specific categories and tags
- `LandingController` — dedicated landing page route

#### User Profiles
- `Credentials`, `Specialty`, `LicenseNumber` columns added to `Users` — professional author credentials
- `AvatarController` — dedicated avatar upload/management endpoint
- Profile update now refreshes cookie claims immediately

#### Media
- `OriginalFileName` column on `Media` table — tracks the uploaded filename before randomization

#### Theme System (Inkwell)
- 10 curated Inkwell color presets seeded via `SeedDefaultsAsync`
- Magazine, Grid, Minimal, Neutral, Classic, and Modern layout variants
- `CustomThemeSettings` table — per-owner key/value CSS variable overrides
- `ThemeSettingsController` — preset application, layout presets, reset; all changes audited
- Theme backward-compatibility guards in all ViewComponents (unknown values fall back to safe defaults)

#### Security
- `ReCaptchaService` — Google reCAPTCHA v2 validation on public forms
- `IEmailService` / `IRedirectRepository` interfaces added to `Blog.Core`

### Fixed

- **PageView fire-and-forget scope bug** — `TrackAsync` previously used `context.RequestServices.CreateScope()` which was disposed before the async task ran, silently swallowing all view recordings. Fixed by pre-capturing all HTTP context values synchronously and switching to `_rootServices.CreateScope()`.
- **Admin page views counted** — Admin requests were being tracked; `PageViewMiddleware` now checks `context.User.Identity.IsAuthenticated` and skips authenticated users entirely.
- `BlogController` operator precedence — `!User.Identity?.IsAuthenticated == true` replaced with the clearer `User.Identity?.IsAuthenticated != true`.

### Changed

- Upgraded target framework from `.NET 8` to `.NET 10`
- `MigrationService` expanded to auto-create: `CustomThemeSettings`, `Members`, `Labels`, `MemberLabels`, `Newsletters`, `Emails`, `MemberNewsletters`, `Redirects`, `Snippets`, `PageViews`, `AuditLogs`, and all associated indexes and column additions
- `InfrastructureServiceExtensions` registers all new repositories (`IAuditRepository`, `IMemberRepository`, `IPageViewRepository`, `IRedirectRepository`)
- `Program.cs` registers `AuditService`, `SmtpEmailService`, `ReCaptchaService`, and `IHttpContextAccessor`
- README rewritten to reflect current stack (.NET 10, Dapper, no EF Core) and all new features

### Database Scripts (DBScripts/)

| Script | Change |
|---|---|
| `2026-05-17_create-pageviews-table.sql` | PageViews table |
| `2026-05-22_add-country-region-to-pageviews.sql` | Country + Region columns + index |
| `2026-05-22_add-verified-review-dates-to-posts.sql` | LastVerifiedAt + NextReviewAt columns |
| `2026-05-23_add-inkwell-preset-theme-setting.sql` | No schema — seeded at runtime |
| `2026-05-24_create-missing-tables-live.sql` | Backfill for live environments missing tables |
| `2026-05-24_reset-pageviews-fresh-start.sql` | One-time data reset (manual run only) |
| `2026-05-29_create-audit-logs-table.sql` | AuditLogs table + 3 indexes |

---

## [1.0.0] — 2026-05-15

### Added

- Initial release of the Blogfront platform
- ASP.NET Core MVC blog engine with Dapper data access
- Cookie-based authentication with PBKDF2-SHA256 password hashing
- Role-Based Access Control (Admin, Editor, Author)
- Post creation, editing, scheduling, and publishing
- Media library with secure upload and organized storage
- Categories and Tags management
- Comments with moderation
- Basic theme customization
- Multi-tenant `ITenantContext` support (cloud mode)
- SQL Server schema auto-migration on startup
