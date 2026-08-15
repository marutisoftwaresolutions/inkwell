# Inkwell v1.0.4

**Release date:** 2026-08-15
**Type:** Feature release (backward-compatible — existing tenants keep their layout, theme, content and settings)

This release makes an Inkwell blog safer to run and easier to maintain. It adds an **IP Firewall**
that blocks hostile traffic on its own, a **Content Health** dashboard that turns post maintenance
into a worklist, and a set of **indexation controls** — 410 retirement, filter-URL canonicalization,
and depth-gated tag archives. It also ships the **WordPress & Ghost importer** and **Series &
collections**, plus fixes for HEAD requests and archive pages.

## Highlights

### 🛡️ IP Firewall — automatic blocking

Public sites are probed continuously. Every failing request is now weighed by a threat scorer, and
addresses that cross the threshold block themselves.

- Exploit probes (`.php`, `wp-admin`, `.env`, `.git`, path traversal, SQL-injection payloads,
  app-server consoles) score 5; a rejected sign-in scores 3; an ordinary 404 scores 1.
- At **10 points within 10 minutes** (both configurable) the address is blocked — 24 hours on the
  first offense, a week on the second, permanently on the third — and further requests get a bare
  **403 before routing, views or logging run**.
- **Admin → Security** shows active blocks with reason and denied-request counts, a live watchlist of
  addresses building a score, and the thresholds, allowlist and alert settings. Addresses can be
  blocked or allowlisted by hand, and **Admin → Error Monitor** now records each signature's last
  client IP with a one-click Block.
- Rules live in a new `IpFirewallRules` table so blocks survive a restart; enforcement reads an
  in-memory snapshot, so an ongoing attack costs no per-request database work.

**Safe by default:** loopback and private ranges are never blocked, signed-in staff are never scored,
search and AI crawlers (Googlebot, Bingbot, GPTBot, ClaudeBot, …) are never blocked for 404s, proxy
headers are ignored unless you declare a trusted proxy, and the firewall **fails open** if the
database is unreachable. Every rule change is written to the audit trail.

### 🩺 Content Health dashboard

**Admin → Content Health** lists published posts that need maintenance: reviews overdue or due within
30 days, posts never verified, posts missing a Key Facts or FAQ block, meta descriptions that are
missing or long enough to truncate in search results, and titles still promising a year that has
passed. Tiles double as filters and every row links to the editor. Read-only — it surfaces work, it
never edits. Available to Editors and Admins.

### 🔎 Indexation controls

- **Retire a URL with 410 Gone.** Redirect rules now carry a status code — 301 (default), 302, or
  **410**. A 410 serves a dedicated "no longer available" page and tells search engines the removal
  was deliberate, which de-indexes far faster than a 404 that keeps getting re-crawled.
- **Filter-URL canonicalization.** A single-value home-feed filter (`/?tags=x`, `/?category=y`) now
  301s to the matching `/tag/{slug}` or `/category/{slug}` archive, so filter permutations stop
  competing as separate indexed URLs. Multi-value combinations are unchanged, pagination survives the
  redirect, and only well-formed slugs redirect so a crafted query string can't become a redirect target.
- **Tag archives are indexable once they have depth.** An archive with at least three posts becomes a
  real topic page; thinner ones stay `noindex`, and `sitemap.xml` now lists only archives that are
  actually indexable — it no longer advertises `noindex` URLs.

### 📥 WordPress & Ghost importer

Migrate an existing blog from **Admin → Import**: upload a WordPress **WXR** (`.xml`) or **Ghost
JSON** (`.json`/`.zip`) export, preview it, choose exactly what to bring in (posts, pages,
categories/tags, images, comments, with a published-only filter and slug-conflict policy), then run a
**batched, resumable** import with live progress, a per-item exception log and a downloadable `.xlsx`
report. Images are downloaded (SSRF-guarded), converted to **WebP** and re-linked; Ghost images
bundled in a `.zip` are read straight from the archive; old permalinks become redirects; imported HTML
is sanitized. An import can be **undone**. Admin-only.

### 📚 Series & collections

Group posts into an ordered reading sequence. **Admin → Series** creates and orders them; each series
gets a public `/series/{slug}` page plus a `/series` directory, member posts show a **"Part N of M ·
Previous / Next"** banner, and series emit `CollectionPage` + ordered `ItemList` + `BreadcrumbList`
structured data and appear in the sitemap.

### 🖼️ Shared image pipeline

The automatic WebP conversion introduced for uploads is now a shared service reused by the importer,
so every image entering the media library — uploaded or imported — is optimized through one path.

### 🐛 Fixes

- **HEAD requests no longer return 405.** Routing matches methods exactly and every page route is
  GET-only, so every HEAD request was refused — including `HEAD /` and `HEAD /robots.txt`, and even
  HEAD for a URL that does not exist (405 instead of 404). Uptime monitors, link checkers and some
  crawlers rely on HEAD. It now returns exactly what GET would: same status, same headers, no body.
  HEAD requests are excluded from page-view analytics and logged as HEAD, not GET.
- **Category and tag archives render the full listing chrome again.** They share the home-feed view
  but never populated its filter chips, popular-posts sidebar or the configured layout, so those pages
  silently fell back to defaults. All three listing routes now populate the same view data, and an
  archive seeds its own filter selection so adding a second filter keeps the first.
- **Post pages no longer 500 when a post has no categories or tags.** The related-posts query emitted
  an invalid `ORDER BY (0)`; it now falls back to recency ordering.

## Upgrade notes

- **Schema:** the new `IpFirewallRules` table, the `Redirects.StatusCode` column, `ErrorLogs.LastIpAddress`,
  and the `Series` / `SeriesPosts` and `ImportJobs` / `ImportItems` tables are created automatically on
  startup by `MigrationService`. Dated scripts in `DBScripts/` mirror them for manual or production
  application — run them with `sqlcmd -f 65001` (UTF-8; omitting it corrupts em dashes and curly quotes).
- **The IP Firewall is on by default** with conservative settings (10 points / 10 minutes, 24-hour first
  block, crawler protection on, proxy headers **not** trusted). Review it in Admin → Security. If your
  site sits behind Cloudflare or another reverse proxy, enable **Trust proxy headers** there — otherwise
  every visitor appears as the proxy's address.
- **Backward-compatible:** existing redirect rules keep serving 301, tag archives that lack depth keep
  their previous `noindex` behaviour, and a tenant that never opens the new screens is unaffected. No
  settings changes are required.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
