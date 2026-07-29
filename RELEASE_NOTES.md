# Inkwell v1.0.2

**Release date:** 2026-07-29
**Type:** Feature release (backward-compatible — existing tenants keep their layout, theme, and content)

Inkwell is a free, open-source, self-hosted, multi-tenant blogging platform built on ASP.NET Core (.NET 10) with SQL Server. v1.0.2 is a major step up in **SEO / AEO (AI-search) readiness**, adds a **scored-roundup content system**, new **layouts**, **instant indexing**, and an **error-monitoring** subsystem.

## Highlights

### 🔎 SEO & AI-search overhaul
- Rich structured data across the site: `Organization`, `WebSite` + `SearchAction`, `CollectionPage`, `BlogPosting`, `BreadcrumbList`, `FAQPage`, `HowTo`, author `Person`, and roundup `ItemList`/`Review`/`SoftwareApplication`.
- **Per-tenant `llms.txt` + new `llms-full.txt`** for AI answer engines (ChatGPT, Claude, Perplexity, Gemini), a curated AI-crawler allow-list in `robots.txt`, and entity/disambiguation signals.
- Author **E-E-A-T pages** (`/author/{slug}`), configurable **content language** (`lang`/`hreflang`/`og:locale`), image sitemap, sitelinks search box, and Search Console / Bing verification fields.
- Performance: precompiled Tailwind, preloaded brand fonts, deferred non-critical scripts, LCP-prioritized images.

### ⚡ IndexNow instant indexing
Enable in Admin → Settings; publishing or updating a post pings Bing/Yandex/Seznam/Naver to re-crawl within minutes. Ownership key auto-generated and served at the site root. Per-tenant, off by default, best-effort.

### 🏆 Verdict roundup content system
A scored "Top N Best … Software" format with a dedicated layout, per-entry scores/pros/cons/CTAs, internal-cluster linking, verified outbound vendor links, first-party disclosure, and rich-result-safe `ItemList`/`Review` schema.

### 🎨 New layouts
The **Catalog** layout (single-column boxed cards, category filter bar, numbered pagination) joins the library, selectable from Admin → Theme with safe fallback for legacy values.

### 🚨 Error Monitor + alerts
A global exception handler and status-code logger record 4xx/5xx errors **grouped by signature** with occurrence counts and first/last-seen. Admin → Error Monitor lists them with filters and summary tiles; optional email alerts notify configured recipients the first time a new server error appears.

### ✍️ Authoring
Key Facts ("At a glance") and How-To step blocks, a live meta-description length helper, smarter related-posts/internal linking, and consistent `| Brand` page titles.

## Upgrade notes
- **Schema:** `MigrationService` applies all required changes on startup (new `ErrorLogs` table; `Posts.RoundupJson` / `KeyFactsJson` / `HowToJson` columns). The dated scripts in `DBScripts/` mirror these for manual/prod application (`sqlcmd -f 65001`).
- **New settings** (IndexNow, error-notification recipients, content language, verification codes) live in the settings JSON blob and default to safe/off — no action required.
- Fully backward-compatible: no breaking changes; a tenant that upgrades without touching settings keeps its current appearance and behavior.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
