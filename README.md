# Inkwell

**A free, open-source, self-hosted blogging platform built on .NET 10.**
Multi-tenant by default, themeable, and crafted for writers and teams who care about
typography, content ownership, and a calm editorial experience.

🌐 https://www.useinkwell.app · 📄 MIT licensed · 🔒 No telemetry, no cloud account

## Why Inkwell

- **Self-hosted & private** — your content, your server, your data. You are the sole data controller.
- **Zero-ORM performance** — Dapper with raw SQL. No Entity Framework, no lazy-load surprises.
- **Multi-tenant** — one binary serves many blogs. Cloud mode isolates tenants by URL slug; self-hosted mode runs a single-owner install.
- **Themeable** — 16 Inkwell color presets × 20 layouts (Magazine, Feed, Catalog, Verdict, Grid, Minimal, Neutral, Classic, Modern, and more) with live CSS variable customization from the admin panel.
- **Analytics built-in** — page view tracking, UTM attribution, geo-location (country/region via ip-api.com), traffic source classification, and Chart.js dashboards. No third-party tracker required.
- **Audit Trail** — immutable, admin-only log of every write action across the platform. Filterable and Excel-exportable.
- **Error Monitor** — 4xx/5xx errors grouped by signature with counts and first/last-seen, an admin dashboard, and optional email alerts on new server errors.
- **IP Firewall** — hostile addresses (exploit scanners, brute-force sign-ins) are scored and blocked automatically, with an admin screen to review, allowlist, and tune. Search and AI crawlers are protected from being blocked.
- **SEO & AI-search ready** — structured data, author E-E-A-T pages, per-tenant `llms.txt`/`llms-full.txt`, **IndexNow** instant indexing (Bing, Yandex, Seznam, Naver), and an **AI-crawler report** showing which answer engines actually read your posts.
- **Your database** — SQL Server 2019+ or SQL Server LocalDB. Schema auto-applies on startup via `MigrationService`; no manual migration step needed.

---

## Quick Start

```bash
git clone https://github.com/marutisoftwaresolutions/inkwell
cd inkwell
dotnet run
```
Full docs: https://www.useinkwell.app/docs

---

## Features

- **Quill WYSIWYG Editor** — rich-text post editing with image upload, code blocks, dividers, and custom button blots. Auto-save drafts.
- **Role-Based Access Control** — Admin, Editor, and Author roles with 10 granular permission claims (`posts.edit`, `posts.publish`, `posts.delete`, `pages.manage`, `comments.manage`, `categories.manage`, `tags.manage`, `media.manage`, `settings.manage`, `themes.manage`).
- **Theme & Layout System** — Inkwell color presets, 20 layout variants (including the **Verdict** scored-roundup format for "Top N Best… Software" buyer's guides and the **Catalog** single-column boxed card list for SaaS-style company blogs), and a `CustomThemeSettings` key/value store for CSS variable overrides.
- **Analytics Dashboard** — page view tracking with UTM params, referrer, visit source, country/region. Bot detection filters 100+ known bot signatures. Chart.js visualizations.
- **AI-crawler analytics** — Admin → AI Crawlers reports which AI answer engines and search crawlers actually fetch the site, how often, and which pages they read. Recorded to its own table, so human visitor numbers are unaffected. Every crawler Inkwell can name is listed **including those with zero visits** — an engine that has never read the site is a finding, not an omission — with the operator's published purpose, a daily trend, and the most-crawled pages. A user-agent is self-asserted, so the report shows claimed identity and is never used to allow or deny a request.
- **Audit Trail** — every admin write action logged with user, IP, and timestamp. Read-only from the UI; exportable as Excel via ClosedXML.
- **Error Monitor** — a global exception handler and status-code logger record 4xx/5xx errors into an `ErrorLogs` table, grouped by signature (status + normalized path + exception type) so similar/repeat errors collapse into one counted row. Admin → Error Monitor lists groups with counts, first/last-seen, and filters; optional SMTP email alerts notify configured recipients the first time a new server error appears.
- **IP Firewall (automatic blocking)** — every failing request is weighed by a threat scorer: exploit probes (`.php`/`wp-admin`/`.env`/path traversal/SQL-injection payloads) score 5, rejected sign-ins 3, ordinary 404s 1. When an address crosses the threshold (default 10 points in 10 minutes) it is blocked automatically — 24 hours on the first offense, a week on the second, permanently on the third — and refused with a bare 403 before any other work happens. Admin → Security shows active rules, the live watchlist of addresses building a score, and per-rule denied-request counts; addresses can be blocked or allowlisted by hand (also one-click from Admin → Error Monitor, which now records each signature's last client IP). Rules live in `IpFirewallRules` so blocks survive a restart; enforcement reads an in-memory snapshot, so an attack costs no per-request database work. Search and AI crawlers (Googlebot, Bingbot, GPTBot, ClaudeBot, …) are never blocked for 404s, loopback and private ranges are never blocked at all, and the firewall fails open if the database is unreachable.
- **Redirects & 404 manager** — Admin → Redirects lists, creates, edits and removes redirect rules with 301, 302 or **410 Gone**, including the rules post renames and imports write automatically. The 404 log becomes a worklist ranked by evidence that a reader hit it — another page links to it, or it closely matches a real published slug — with one-click "301 to the suggested post" or "410 to retire it"; scanner noise is collapsed out of the way. Loops, chains and rules pointing at a retired URL are refused. Admin-only; every change audited.
- **Answer-engine readiness score** — a 0–100 score per post for how ready it is to be *quoted*, not just ranked: answer capsule, Key Facts, FAQ, meta description, section headings, internal links, freshness, depth and feature image, each weighted and each with concrete advice. Shown in the editor sidebar and as a column, tile and filter in Content Health. Advisory only — it never blocks a publish, and nothing unobservable is scored.
- **Publisher identity (knowledge graph)** — Admin → Settings carries publisher type (Organization/Person), legal name, founder, founding date, authority profiles (Wikipedia, Wikidata, company registers) and an identity statement. Profiles join the social links in `sameAs`; the facts become `legalName`/`founder`/`foundingDate` on the publisher node; the statement is published verbatim in `llms.txt`. Optional throughout — unsupplied or unparseable values are omitted rather than guessed at, and non-absolute URLs are dropped.
- **Import dry-run** — preview a WordPress or Ghost import before it writes: creates, slug clashes and renames, overwrites, skips with reasons, image count, and a report of internal links that would not resolve. Read-only by construction — the preview path cannot write.
- **Answer capsules** — an optional short direct answer shown above the article and emitted as schema.org `abstract`, giving search and AI engines a passage written to be quoted. Authored in the editor with a 40-60 word target; the structured-data linter flags capsules that are too short or too long.
- **Internal-link suggester** — while editing a post, the sidebar proposes internal links in both directions (link out to, and should link here), shows outbound/inbound counts against the two-in / two-out minimum, and flags orphans. Ranked suggestions with a stated reason; never auto-applied.
- **Link Audit** — Admin → Link Audit scans published posts and pages for internal links that are broken, point at a URL retired with 410, or pass through one or more redirect hops, separating dead ends (which cost the reader) from hops (which cost link equity). Read-only; Editor and Admin roles.
- **Pre-publish structured-data linter** — before a post goes live its FAQ, Key Facts, How-To and roundup blocks are validated, and a post whose structured data would not validate is saved as a draft rather than published. Malformed JSON is caught rather than silently rendering as an empty block, and the policy rules are enforced in code: every roundup entry links the vendor real site as an absolute URL, `ctaUrl` stays the internal review path, scores sit on the 0-10 scale, and a first-party product may be featured but never carries a self-authored score. Advisory issues surface as warnings and never block publishing.
- **Content Health** — Admin → Content Health lists published posts needing maintenance: overdue or upcoming content reviews (`LastVerifiedAt` / `NextReviewAt`), posts never verified, missing Key Facts or FAQ blocks, meta descriptions that are missing or will truncate in search results, and titles carrying a year that has already passed. Tiles act as filters and every row links to the editor. Read-only; Editor and Admin roles.
- **Newsletter & Subscribers** — compose and send newsletters, manage subscribers, import/export CSV.
- **Redirects Manager** — source/destination redirect rules with per-rule hit-count tracking. Each rule carries a status code: **301** (moved, the default), **302** (temporary), or **410 Gone** to retire a URL deliberately — the 410 serves a dedicated page and de-indexes far faster than leaving the URL as a 404.
- **Members** — member directory with label-based segmentation.
- **Media Library** — secure upload organized by year/month, paginated API for in-editor browsing, and automatic WebP conversion of raster uploads (JPEG/PNG/GIF → WebP, with dimensions captured) via SixLabors.ImageSharp; SVG and existing WebP pass through unchanged.
- **Series & collections** — group posts into an ordered reading sequence (multi-part guides). Managed in Admin → Series (create, add posts, reorder); each series gets a public `/series/{slug}` page and a `/series` directory, member posts show a "Part N of M · Previous / Next" banner, and series emit `CollectionPage`/`ItemList`/`BreadcrumbList` schema and appear in the sitemap.
- **Browse by category, tag, or search** — the public feed carries filter chips for every category and tag that has published content, alongside full-text search. Selecting several tags (or several categories) **widens** the results to posts matching *any* of them, while a category combined with a tag **narrows** to posts matching both — the standard faceted-filter model. A single-value selection links straight to the canonical `/category/{slug}` or `/tag/{slug}` archive; multi-value combinations stay on the home feed as `noindex,follow`. Unknown slugs return 404, and archives with no published posts are never linked.
- **WordPress & Ghost importer** — migrate an existing blog from Admin → Import: upload a WordPress WXR (`.xml`) or Ghost JSON (`.json`/`.zip`) export, preview and select what to import (posts, pages, categories/tags, images, comments), then run a batched, resumable import with a live progress bar, per-item exception log, and a downloadable report. Referenced images are downloaded and converted to WebP, content HTML is sanitized, and old permalinks become redirects to preserve SEO. Ghost bodies fall back to a best-effort Lexical/Mobiledoc conversion when no rendered HTML is present, and images bundled in a Ghost `.zip` are read straight from the archive. An import can be undone. Step-by-step export instructions for both platforms are shown in the importer. Admin-only.
- **SEO & AI-search ready** — per-page canonical URLs, Open Graph / Twitter Cards, and OG image generation; JSON-LD structured data (`Organization`, `WebSite` + `SearchAction`, `BlogPosting`, `BreadcrumbList`, `FAQPage`, `CollectionPage`, roundup `ItemList`/`Review`, author `Person`); length-aware ` | Brand` page titles (the brand suffix is dropped rather than letting the title overflow the ~60-character SERP budget, so the differentiator survives instead of the boilerplate), `noindex` on thin/search/paginated pages, and **filter-URL canonicalization** — a single-value `/?tags=x` or `/?category=y` 301s to the clean `/tag/{slug}` / `/category/{slug}` archive so filter permutations never compete as separate URLs, while multi-value combinations stay `noindex,follow`; **tag archives become indexable once they hold enough posts** (thin ones stay `noindex` and are kept out of the sitemap, so a sitemap never advertises a `noindex` URL); **author E-E-A-T pages** at `/author/{slug}`; **"At a glance" key-facts** and **How-To step** blocks (with `HowTo` schema) for AI-extractable content; per-tenant **content language** driving `lang`/`hreflang`/`og:locale`/`inLanguage`; dynamic `sitemap.xml` (posts, categories, tags, series, pages, authors) with per-post image entries and automatic sitemap-index chunking for large sites, plus an RSS feed; content freshness dates (`LastVerifiedAt` / `NextReviewAt`); AEO signals — a **per-tenant `llms.txt`** + expanded **`llms-full.txt`** generated from each blog's name/description/categories/articles, including a cross-cutting **topic index** that maps every indexable tag archive to the articles it covers, a curated AI-crawler allow-list in `robots.txt`, and an `llms.txt` discovery hint for answer engines (ChatGPT, Claude, Perplexity, Gemini); optional **IndexNow instant indexing** — enable it in Admin → Settings and each publish/update pings Bing, Yandex, Seznam, and Naver (key auto-generated and served at the site root `/{key}.txt`).
- **Post Scheduling** — schedule posts for future publish with `ScheduledAt`.
- **SMTP Email** — transactional email via `System.Net.Mail`; no dependency on third-party email SDKs.
- **reCAPTCHA** — optional Google reCAPTCHA v2 on public forms. Skipped gracefully when not configured.

---

## Tech Stack

### Backend

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core MVC |
| Data access | Dapper 2.1.66 (raw SQL, no ORM) |
| Database | SQL Server 2019+ · SQL Server LocalDB |
| DB driver | Microsoft.Data.SqlClient 5.2.2 |
| Authentication | ASP.NET Core cookie auth — 8 hr sliding window, HttpOnly, SameSite=Lax |
| Password hashing | PBKDF2-SHA256, 100 000 iterations, 16-byte random salt |
| Authorization | Policy-based RBAC — permission claims loaded from DB at sign-in |
| Image processing | SixLabors.ImageSharp.Drawing 2.1.4 · SixLabors.ImageSharp.Web 3.2.0 |
| Excel export | ClosedXML 0.104.2 |
| Email | System.Net.Mail (SMTP) |
| JWT | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.3 (API auth) |

### Frontend

| Concern | Technology |
|---|---|
| CSS framework | Tailwind CSS (precompiled static stylesheet, no runtime compiler on the public site) · Bootstrap 5 |
| Reactivity | Alpine.js |
| Dynamic content | HTMX |
| Rich text editor | Quill (self-hosted, Snow theme) |
| Charts | Chart.js |
| Icons | Lucide · Bootstrap Icons |
| Syntax highlighting | Highlight.js (GitHub Dark theme) |
| Validation | jQuery Validation + Unobtrusive |
| Fonts | Source Serif 4 · Geist (local, no CDN) |

### Infrastructure & Tooling

| Concern | Details |
|---|---|
| Schema migrations | `MigrationService` — `IF NOT EXISTS` guards run on every startup |
| DB scripts | `DBScripts/YYYY-MM-DD_description.sql` — idempotent, dated |
| OG image CLI | `tools/FeatureImageGenerator` — .NET 10 console app (SixLabors.ImageSharp) |
| CSS build | Public `wwwroot/css/tailwind.css` is compiled from `tailwind.config.js` + `tailwind.src.css` and committed. Regenerate after adding utility classes with `npx tailwindcss@3 -c tailwind.config.js -i wwwroot/css/tailwind.src.css -o wwwroot/css/tailwind.css --minify`, or build with `-p:BuildTailwindCss=true` (needs Node). `dotnet build` alone needs no Node. |
| Tests | `Blog.Tests` (xUnit) — SEO unit guards (title branding, meta-description truncation, robots.txt tokens, locale mapping) plus `WebApplicationFactory` HTTP smoke tests (home/robots/sitemap/llms/search/post). Integration tests self-skip when no SQL Server is reachable. Run with `dotnet test`. |
| Solution format | `Blog.slnx` (modern .NET solution file) |

---

## Architecture

```
Blog.Core/              Domain models, interfaces, AuditActions constants
Blog.Infrastructure/    Dapper repositories, MigrationService, seeders, DapperContext
Blog.Web/               ASP.NET Core 10 MVC — controllers, views, middleware, services
tools/
  FeatureImageGenerator/ OG image generation CLI
DBScripts/              Idempotent, dated SQL scripts (YYYY-MM-DD_description.sql)
```

### Key middleware (request pipeline order)

| Middleware | Purpose |
|---|---|
| `IpFirewallMiddleware` | Refuses blocked IPs with a 403 before any other work; scores failing requests on the way out |
| `HeadRequestMiddleware` | Makes HEAD behave like GET on MVC routes (same status and headers, no body) instead of 405 |
| `TenantMiddleware` | Resolves tenant context — self-hosted (first admin) or cloud (URL slug) |
| `PageViewMiddleware` | Fire-and-forget async page view tracking with bot filtering and geo-IP |
| `ImageSharp` | Smart image resizing, caching, and serving from `wwwroot` |

### Design decisions

| Concern | Approach |
|---|---|
| Data access | Dapper — raw SQL, explicit queries, no change tracker |
| Theme storage | `CustomThemeSettings` table — key/value CSS variable overrides per tenant |
| Page view tracking | Fire-and-forget using root `IServiceProvider` scope (not request-scoped) |
| Audit logging | `AuditService` swallows exceptions — audit failure never crashes a request |
| Bot detection | 100+ UA token blocklist in `PageViewMiddleware`; IP SHA-256 hashed with salt |
| IP firewall | Rules cached in a singleton snapshot (~60 s refresh), denied-hit counters buffered and batch-flushed; fails open |
| Geo-IP | ip-api.com free tier (45 req/min); results cached 24 hours |
| Image uploads | Organized by `year/month` under `wwwroot/uploads/` |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server 2019+ or SQL Server LocalDB

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/marutisoftwaresolutions/inkwell
   cd inkwell
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore Blog.slnx
   ```

3. **Configure the environment:**

   Copy `appsettings.example.json` to `appsettings.json` inside `Blog.Web/`:
   ```bash
   cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json
   ```

   Choose a connection string:

   ```json
   // SQL Server
   "DefaultConnection": "Server=.;Database=inkwell;User Id=sa;Password=yourpass;TrustServerCertificate=True;"

   // SQL Server LocalDB
   "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=inkwell;Trusted_Connection=True;TrustServerCertificate=True;"
   ```

4. **Run the application:**
   ```bash
   dotnet run --project Blog.Web
   ```

   On first launch, `MigrationService` creates all tables and seeds default roles. Navigate to `/account/register` to claim the Admin account.

5. **Optional — run DB scripts manually:**

   All schema scripts in `DBScripts/` are idempotent and safe to re-run:
   ```powershell
   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\init.sql"
   ```

   > Always include `-f 65001` (UTF-8 code page). Omitting it causes character encoding corruption in NVARCHAR columns.

---

## Configuration Reference

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | Database connection string |
| `DeploymentMode` | `SelfHosted` (single owner) or `Cloud` (multi-tenant URL routing) |
| `CanonicalHost` | Enforce HTTPS + specific domain in production |
| `Smtp:Host` / `Port` / `Username` / `Password` | Transactional email (SMTP) |
| `Smtp:FromEmail` / `FromName` | Sender identity |
| `ReCaptcha:SiteKey` / `SecretKey` | Google reCAPTCHA v2 (optional — skipped if absent) |
| `Jwt:Key` / `Issuer` / `Audience` | JWT config for API authentication |

---

## Database Scripts

All schema changes follow a mandatory naming convention:

```
DBScripts/YYYY-MM-DD_short-description.sql
```

Scripts are idempotent and use `IF NOT EXISTS` / `IF COL_LENGTH(...)` guards. `MigrationService` applies all required schema at startup — a fresh install needs no manual SQL step. The `DBScripts/` folder is the authoritative history of every schema change.

---

## Development

```bash
dotnet watch run --project Blog.Web   # hot-reload
```

Generate OG / feature images for posts:

```bash
dotnet run --project tools/FeatureImageGenerator
```

---

## Security

- **No external trackers** — page analytics are stored locally; geo-IP lookup is optional and cached.
- **CSRF protection** — anti-forgery tokens on all forms.
- **Secure cookies** — HttpOnly, SameSite=Lax, secure policy matches request scheme.
- **PBKDF2-SHA256** — 100 000 iterations; constant-time comparison prevents timing attacks.
- **Immutable audit trail** — no delete or truncate endpoint exists for `AuditLogs`.
- **Automatic IP blocking** — exploit scanners and brute-force sign-ins block themselves (Admin → Security). Every block, unblock, allowlist and threshold change is audited. Proxy headers (`CF-Connecting-IP`/`X-Forwarded-For`) are ignored unless the operator declares a trusted proxy, so a client cannot forge its address.
- **File upload safety** — MIME type validation; files organized under `wwwroot/uploads/year/month/`.
- **IP anonymization** — page view IPs are SHA-256 hashed with a per-install salt before storage.

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

*Built with care by [Maruti Software Solutions](https://marutisoftwaresolutions.com)*
