# Inkwell v1.0.5

**Release date:** 2026-09-05
**Type:** Feature release (backward-compatible — existing tenants keep their layout, theme, content and settings)

v1.0.4 gave operators *visibility* into content that needs work. v1.0.5 turns that into
*prevention*: the platform now stops broken structured data from shipping, tells you what a post
is missing before an answer engine passes it over, proposes the internal links it should carry, and
shows which AI crawlers are actually reading the site. It also adds the **Redirects manager** that
v1.0.4's 301/302/410 support was missing, **publisher identity** settings for the knowledge graph,
an **import dry-run**, and a set of indexation fixes for category and tag archives.

## Highlights

### ✅ Pre-publish structured-data linter

Every post is validated before it goes live. A post whose structured data would not validate is
saved as a **draft instead of published**, with the reason shown in the editor.

- Catches the failure that used to be invisible: a malformed FAQ, Key Facts, How-To or roundup
  block was silently swallowed by the renderer, so a broken block looked identical to a missing one.
- Enforces the rules that matter for search: every roundup entry must link the vendor's real site
  with an absolute URL, the internal review path stays in `ctaUrl`, scores sit on the 0–10 scale,
  and a first-party product may be featured but never carries a self-authored score.
- Advisory issues (a thin FAQ answer, a duplicate label, a single-step How-To, duplicate ranks)
  surface as warnings and never block publishing.

### 📊 Answer-engine readiness score

Every post now carries a **0–100 score** for how ready it is to be *quoted* by an answer engine,
shown in the editor sidebar with a per-signal breakdown and in **Admin → Content Health** as a
column, a tile and a filter.

- The linter says what is *broken*; this says what is *absent*: answer capsule, Key Facts, FAQ,
  meta description, section headings, internal links, freshness dates, depth and feature image,
  each weighted by how much it contributes to being cited and each carrying concrete advice.
- The answer capsule carries the most weight, because it is the passage an engine actually lifts.
- **Advisory only** — it never blocks a publish. A short note that scores low may be exactly
  right, and forcing every post into one shape would produce filler.
- Nothing unobservable is scored: no accuracy or authority judgement, no invented ranking prediction.

### 💬 Answer capsules

A post can now carry a short direct answer, written to be quoted, shown **above the article body**
and emitted as schema.org `abstract` on the `BlogPosting`. Answer engines and featured snippets lift
passages rather than pages; the capsule is the sentence they take. It is visible copy, never hidden
text, so the same words serve the reader. Authored in the editor sidebar with a 40–60 word target;
the linter warns when a capsule is too short to be an answer or too long to be quoted whole. Posts
without one are unaffected.

### 🔗 Internal-link suggester and Link Audit

- **In the editor:** the sidebar proposes links in both directions while you write — published
  posts this one should link to, and posts that should link back to it — with the reason for each
  suggestion (a product this post names but has not linked, or a shared topic). It shows how the
  post stands against the two-in / two-out minimum and flags it as an orphan when nothing links to
  it. Suggestions are ranked, never applied, and loaded on demand.
- **Admin → Link Audit** scans every published post and page for internal links that dead-end,
  point at a URL retired with 410, or take an avoidable redirect hop. Dead ends and retired targets
  are separated from redirect hops because they cost different things; multi-hop chains are called
  out so the final destination can be linked directly. Generated routes and external links are out
  of scope, so the report contains faults rather than false positives. Read-only; Editor and Admin.

### ↪️ Redirects manager with a triaged 404 worklist

v1.0.4 shipped 301/302/410 redirect rules with no way to manage them except SQL. **Admin →
Redirects** now lists, creates, edits and removes rules — including the ones written automatically
by post renames and imports, which were previously invisible.

Alongside it, the 404 log becomes a **worklist**. Most 404s on a public site are vulnerability
scanners, so instead of listing all of them the screen asks for evidence a 404 is reader-facing:
another page links to it, or it is close enough to a real published slug to be a rename, a typo or a
stale external link. Those rank first with a one-click **301 to the suggested post** or **410 to
retire it**; everything else is collapsed away, since repeated probing is the IP Firewall's business.
Rules that would loop, chain into another redirect, or target an already-retired URL are refused with
the real destination named. Admin-only, anti-forgery protected, every change audited.

### 🤖 AI-crawler analytics

**Admin → AI Crawlers** reports which AI answer engines and search crawlers actually fetch the site,
how often, and which pages they read. This traffic passed through the site every day and was
discarded, because bot hits must not pollute human analytics; it is now recorded to its own
`CrawlerVisits` table, so existing visitor numbers are untouched.

- Shows a daily trend, the pages drawing the most AI attention, and GPTBot, ClaudeBot,
  PerplexityBot, Google-Extended, Applebot-Extended, CCBot, Bytespider and the rest, with each
  operator's published purpose, separating AI engines from ordinary search crawlers.
- Every engine Inkwell can name is listed **including those with zero visits** — "no AI engine has
  read this site" is the most useful finding the page can report.
- A user-agent is self-asserted; the report says it shows claimed identity and is never used to
  allow or deny a request.

### 🏛️ Publisher identity for the knowledge graph

**Admin → Settings** gains the entity fields search and answer engines use to work out *who* a
publication is: publisher type (Organization or Person), legal name, founder, founding date,
authority profiles (Wikipedia, Wikidata, a company register, an ORCID) and an identity statement
written for answer engines. Profiles join the existing social links in `sameAs`; the facts become
`legalName` / `founder` / `foundingDate` on the publisher node; the statement is published verbatim in
the Identity section of `llms.txt`. This matters most on a domain that previously published
something else. Every field is optional and nothing is inferred — an unparseable date or a
non-absolute profile URL is dropped rather than guessed at. Blogs that never open Settings are
unchanged.

### 🔍 Import dry-run

The import wizard now offers **Preview first** alongside Start import. It shows exactly what a
WordPress or Ghost file would do before anything is written: posts and pages to be created, slugs
that would be suffixed because of a clash, content that would be **overwritten**, items skipped and
why, images to be fetched, and internal links that would not resolve. Overwrites are called out
prominently. The preview is read-only by construction — it uses a separate analyzer with no write
path — and it predicts rather than simulates: images are counted, not downloaded.

### 🗂️ Topic index in `llms-full.txt`

The AI content index now carries a `## Topics` section alongside the category grouping, listing each
tag with its archive URL, article count and the titles it covers. Only topics deep enough to have an
indexable archive are listed, so the file never points a crawler at a `noindex` page. Generated per
tenant; blogs with no tags are unaffected.

## Changed

- **Page titles no longer overflow the search-result budget.** The `" | Site Name"` suffix is now
  appended only when the finished title still fits inside roughly 60 characters. Previously it was
  added unconditionally, so search engines cut off the part that distinguished the page while keeping
  boilerplate they already show as the site name. Titles that already contain the site name, and
  category/tag/series archives, are unchanged.
- **Category and tag links go straight to the canonical archive.** Selecting a single category or
  tag from the home-feed filters links directly to `/category/{slug}` or `/tag/{slug}` instead of a
  query-string URL that 301s there.
- **Multi-tag filter links from an archive no longer mint duplicate URLs.** Selecting a second tag
  while on `/tag/{slug}` built a relative link the archive route ignored, silently dropping the
  second selection and generating a crawlable near-duplicate per chip. Those links are now rooted at
  the home feed, where multi-value filtering actually runs.

## Fixed

- **Unknown category and tag URLs return 404.** `/category/{slug}` and `/tag/{slug}` answered 200
  for any slug, rendering an empty archive titled from the raw slug — an unbounded soft-404 surface.
  Both routes now check the taxonomy exists first, as `/author/{slug}` already did.
- **Empty categories and tags are no longer offered as filters.** The public filter chips listed
  every category and tag in the database, including those with no published posts. Reader-facing
  lists now count published posts only; admin screens still show drafts and empty taxonomies.
- **Tag archives held up only by drafts no longer reach the sitemap.** The indexability test
  counted unpublished posts, so a tag with three drafts could be advertised while its archive
  answered `noindex`.
- **Archive pagination no longer repeats the facet in the query string.** Page 2 of `/tag/{slug}`
  linked to `?page=2&tags={slug}`; archive pagers now emit `?page=2` alone.
- **A fresh install from `DBScripts/init.sql` now produces a complete schema.** The file was
  stamped v1.0.1 and was missing three tables (`ErrorLogs`, `IpFirewallRules`, `CrawlerVisits`) and
  four columns (`Posts.KeyFactsJson`, `Posts.HowToJson`, `Posts.AnswerCapsule`,
  `Redirects.StatusCode`). Existing installations never noticed because `MigrationService` repairs
  the schema on startup; only first-time installs were affected. Verified by building a database from
  `init.sql` alone and diffing `sys.columns` against a working installation in both directions.
- **README no longer claims revision history.** The advertised "Page Revisions" capability had no
  callers, no table and no UI. The claim has been removed; revision history stays a separately
  scoped roadmap item.

## Upgrade notes

- **Schema:** two changes, both applied automatically on startup by `MigrationService`:
  the new `CrawlerVisits` table and the `Posts.AnswerCapsule` column. Dated scripts in `DBScripts/`
  (`2026-09-05_create-crawler-visits-table.sql`, `2026-09-05_add-answercapsule-to-posts.sql`) mirror
  them for manual or production application — run them with `sqlcmd -f 65001` (UTF-8; omitting it
  corrupts em dashes and curly quotes). Publisher-identity fields live in the settings JSON blob and
  need no migration.
- **Publishing can now be refused.** A post whose FAQ, Key Facts, How-To or roundup block fails
  validation is saved as a draft with the reason shown. Existing published posts are not re-validated
  until they are next saved; use Admin → Content Health to find posts worth revisiting.
- **AI-crawler recording is on by default** and writes only to the new `CrawlerVisits` table.
  Human page-view analytics are unchanged.
- **Backward-compatible:** existing redirect rules, archives, filters and settings keep working
  without any action. A tenant that never opens the new screens is unaffected. The readiness score,
  link suggestions and identity fields are additive and optional.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
