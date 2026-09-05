# Changelog

All notable changes to Inkwell are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Added

- **Answer-engine readiness score** — every post now carries a 0–100 score for how ready it is to be *quoted* by an answer engine, shown in the editor sidebar with a per-signal breakdown and in Admin → Content Health as a column, a tile and a filter. The structured-data linter says what is *broken*; this says what is *absent*: the answer capsule, Key Facts, FAQ, meta description, section headings, internal links, freshness dates, depth and feature image, each weighted by how much it contributes to being cited and each carrying concrete advice rather than a bare number. The answer capsule carries the most weight because it is the passage an engine actually lifts. Advisory only — it never blocks a publish, because a short note that scores low may be exactly right and forcing every post into one shape would produce filler. Nothing unobservable is scored: there is no accuracy or authority judgement and no invented ranking prediction. The dashboard counts headings and links in SQL rather than loading article bodies, and the editor counts the same tokens, so the two screens cannot disagree about the same post.
- **Publisher identity settings for the knowledge graph** — Admin → Settings now carries the entity fields search and answer engines use to work out *who* a publication is: publisher type (Organization or Person), legal name, founder, founding date, authority profiles (Wikipedia, Wikidata, a company register, an ORCID), and an identity statement written for answer engines. Profiles join the existing social links in `sameAs`, the facts become `legalName` / `founder` / `foundingDate` on the publisher node, and the statement is published verbatim in the Identity section of `llms.txt`. This matters most on a domain that previously published something else, which otherwise keeps answering to its former identity. Every field is optional and nothing is inferred: an unsupplied founder or an unparseable date is omitted from structured data rather than guessed at or emitted blank, an unsupported publisher type falls back to Organization, and a profile that is not an absolute http(s) URL is dropped rather than allowed to invalidate the node. Blogs that never open Settings are unchanged and still emit a generated identity statement.
- **Redirects manager with a triaged 404 worklist** — a new Admin → Redirects screen manages the redirect table that until now could only be edited in SQL. Rules can be listed, created, changed and removed, with 301, 302 or **410 Gone**; rules written automatically by post renames and imports are finally visible. Alongside it, the 404 log becomes a worklist. Most 404s on a public site are vulnerability scanners, and tools that list all of them make the operator do the sorting by hand — so this asks instead for evidence a 404 is reader-facing: either another page links to it, or it is close enough to a real published slug to be a rename, a typo, or a stale external link. Those are ranked first with a one-click **301 to the suggested post** or **410 to retire it**; everything else is collapsed out of the way, since repeated probing is the IP firewall's business. Rules that would loop, point at a URL that redirects again, or target a URL already retired with 410 are refused with the real destination named, so the manager cannot manufacture the chains Link Audit exists to report. Admin-only, anti-forgery protected, and every change is audited.
- **AI-crawler analytics** — a new Admin → AI Crawlers screen reports which AI answer engines and search crawlers actually fetch the site, how often, and which pages they read. Search Console's generative-AI report gives impressions only, never names the engine, and is still rolling out; this traffic meanwhile already passes through the site every day and was discarded, because bot hits must not pollute human analytics. Recorded to its own table so existing visitor numbers are untouched, and every engine Inkwell can name is listed **including those with no visits** — "no AI engine has read this site" is the single most useful finding the page can report, and it is only visible if absent crawlers still appear. Shows a daily trend, the pages drawing the most AI attention, GPTBot, ClaudeBot, PerplexityBot, Google-Extended, Applebot-Extended, CCBot, Bytespider and the rest with the operator's published purpose for each, and separates AI engines from ordinary search crawlers. A user-agent is self-asserted, so the report states that it shows claimed identity; it is never used to allow or deny a request.
- **Import dry-run** — the import wizard now offers **Preview first** alongside Start import, showing exactly what a WordPress or Ghost file would do before anything is written: how many posts and pages would be created, how many would be given a suffixed slug because of a clash, how many would **overwrite** existing content, what would be skipped and why, and how many images would be fetched. It also reports **links that would not resolve** — internal links in the imported content pointing at posts the file does not bring and that do not already exist here. Overwrites are called out prominently, because that is the one outcome that destroys existing work. The preview is read-only by construction: it reads the staged file through a separate analyzer rather than the importer, so a preview has no code path that could persist anything. It predicts rather than simulates, and says so — images are counted, not downloaded.
- **Answer capsules** — a post can now carry a short direct answer, written to be quoted, shown above the article body and emitted as schema.org `abstract` on the BlogPosting. Answer engines and featured snippets lift passages rather than pages, so a post that reaches its answer three paragraphs down gives them nothing clean to take; the capsule is the sentence that does. It is visible copy, never hidden text, so the same words serve the reader. Written in the editor sidebar with a 40-60 word target, and the structured-data linter warns when a capsule is too short to be an answer or too long to be quoted whole. Posts without one are unaffected.
- **Internal-link suggester in the editor** — the post editor sidebar now proposes links in both directions while you write: published posts this one should link to, and posts that should link back to it. It also shows how the post stands against the two-in / two-out minimum, and flags it as an orphan when nothing links to it. Two kinds of suggestion, each with its reason shown: a post this one already names but has not linked (the link a reader expects), and posts sharing topics. Suggestions are ranked, never applied — a mention outranks a topic match, and nothing already linked is proposed again. Loaded on demand, so opening the editor costs nothing extra.
- **Link Audit** — a new Admin → Link Audit screen scans every published post and page for internal links that dead-end, point at a URL retired with 410, or take an avoidable redirect hop. Broken links and links to retired URLs are separated from redirect hops, because they cost different things: a dead end wastes the reader and the crawl budget spent following it, while a hop only spends link equity. Multi-hop chains are called out separately so the final destination can be linked directly. Generated routes (tag, category, author, uploads) and external links are out of scope, so the report contains faults rather than false positives. Read-only; Editor and Admin roles.
- **Pre-publish structured-data linter** — every post is validated before it goes live, and a post whose structured data would not validate is saved as a draft instead of published. It catches the failure that was previously invisible: a malformed FAQ, Key Facts, How-To or roundup block is swallowed by the renderer and emits nothing, so a broken block looked identical to a missing one. It also enforces the rules that matter for search: every roundup entry must link the vendor real site with an absolute URL, the internal review path must stay in `ctaUrl` rather than the vendor URL, scores must sit on the 0-10 scale, and a first-party product may be featured but never carries a self-authored score. Advisory issues (a thin FAQ answer, a duplicate label, a single-step How-To, duplicate ranks) surface as warnings and never block publishing.
- **Topic index in `llms-full.txt`** — the AI content index now carries a `## Topics` section alongside the existing category grouping. A post sits in one primary category but carries several tags, so the category view alone hides the site's secondary taxonomy; the new section lists each topic with its archive URL, its article count, and the titles it covers, giving answer engines the cross-cutting view of which articles address the same subject. Only topics deep enough to have an indexable archive are listed, so the file never points a crawler at a page that answers `noindex`. Generated per tenant from that blog's own published posts; blogs with no tags are unaffected.

### Changed

- **Category and tag links go straight to the canonical archive** — selecting a single category or tag from the home-feed filters now links directly to `/category/{slug}` or `/tag/{slug}` instead of the query-string form that 301-redirects there, so an in-page link no longer spends a redirect hop. Multi-value filter combinations, filters combined with a search term, and the toggle-off behaviour are unchanged.
- **Page titles no longer overflow the search-result budget** — the `" | Site Name"` brand suffix is now appended only when the finished title still fits inside roughly 60 characters, the width Google renders before truncating. Previously the suffix was added unconditionally, so a 50-character title on a blog called "The Independent Optometry Technology Review" rendered at 96 characters and search engines cut the end off — losing the part that distinguishes the page ("2026", "Compared", "FDA-Cleared") while keeping boilerplate that the result already shows as the site name. Titles that already contain the site name are unchanged, as are category, tag and series archives.
- **Multi-tag filter links from an archive no longer create duplicate URLs** — selecting a second tag while on `/tag/{slug}` (or a second category on `/category/{slug}`) built a relative `?tags=a,b` link, which resolved against the archive route as `/tag/{slug}?tags=a,b`. The archive ignores that query string, so the second selection was silently dropped and every chip minted a crawlable near-duplicate of the archive. Those links are now rooted at the home feed (`/?tags=a,b`), where multi-value filtering actually runs. The home feed, single-value links, toggle-off and archive pagination are unchanged.

### Fixed

- **Unknown category and tag URLs return 404** — `/category/{slug}` and `/tag/{slug}` previously answered 200 for any slug at all, rendering an empty archive titled with the raw slug. Every invented URL was therefore a soft-404 that search engines had to crawl and discard. Both routes now check the taxonomy exists first, matching how `/author/{slug}` already behaved. A category or tag that exists but holds no published posts still renders (and stays `noindex`).
- **Empty categories and tags are no longer offered as filters** — the public filter chips listed every category and tag in the database, including those with no posts and those whose only posts were drafts, so readers and crawlers were sent to archives that render empty. The reader-facing lists now count published posts only. Admin screens are unchanged and still show drafts and empty taxonomies.
- **Tag archives held up only by drafts no longer reach the sitemap** — the sitemap's "enough posts to be indexable" test counted unpublished posts, so a tag with three drafts and nothing published could be advertised while its archive answered `noindex`. The count is now published-only, matching what the archive itself does.
- **Archive pagination no longer repeats the facet in the query string** — page 2 of `/tag/{slug}` linked to `?page=2&tags={slug}`, restating in the query a filter the route already carries and creating a second URL for the same listing. Archive pagers now emit `?page=2` alone; home-feed pagination still carries its active filters.
- **A fresh install from `DBScripts/init.sql` now produces a complete schema.** The file describes itself as the complete schema and the README offers it as the manual setup path, but it was stamped `v1.0.1 (2026-06-27)` and was missing three tables (`ErrorLogs`, `IpFirewallRules`, `CrawlerVisits`) and four columns (`Posts.KeyFactsJson`, `Posts.HowToJson`, `Posts.AnswerCapsule`, `Redirects.StatusCode`) — so anyone installing from it got a database the application fails against. It went unnoticed because `MigrationService` repairs the schema on startup and therefore hides the drift on every existing installation; only a first-time install was affected. Verified by building a database from `init.sql` alone against an empty server and diffing `sys.columns` against a working installation in both directions: zero differences. New columns are applied through a top-up section at the end of the file, so databases created by an older copy are upgraded rather than left behind.
- **README no longer claims revision history.** The Features list advertised "Page Revisions — revision history for posts and pages". No such capability exists: `Revision.cs`, `IRevisionRepository` and `RevisionRepository` are present and registered in DI, but nothing ever calls them, `MigrationService` never creates the `Revisions` table, and the table is absent from the database. There is no UI. Per the docs-follow-code rule the claim has been removed rather than code written to make it true; real revision history is a separate, explicitly-scoped feature on the roadmap.
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
