# Blog-Engine — Claude Project Rules

## Solution Topology & Step-Context Rule (READ FIRST — MANDATORY)

This repository is the **Inkwell** platform. The solution spans **three distinct surfaces** — never
conflate them; a request usually targets exactly one:

| # | Surface | What it is | How changes reach it |
|---|---|---|---|
| 1 | **Platform repo / product page** — `github.com/marutisoftwaresolutions/inkwell` (**canonical**, owner-confirmed) | The Inkwell platform's public GitHub / product page (source-of-truth identity, releases, README the marketing site echoes). This repo publishes here. | git (owner-managed). Heads-up: this working copy's `origin` may still point at the legacy `github.com/MarutiSoftwareSolution/blog` — the owner is migrating to `inkwell`; do not change the remote yourself (No-Commit Rule). |
| 2 | **Platform marketing website** — `https://useinkwell.app` | Markets Inkwell; a **separate codebase** (`UseInkWell.APP`, XML-driven blog). Its changelog/roadmap/version must stay in lockstep with surface #1 — governed by the **`github-progress-sync`** skill; new marketing posts by **`new-inkwell-post`**. | its own build/deploy — NOT this repo. |
| 3 | **Live testing deployment** — `https://www.opticalsoftware.org` | **This** codebase (Inkwell / Blog-Engine) running as a **real, LIVE website with real content** — also the SEO/AI-search rank base. "Testing" refers to it being our validation target, **not** that it is disposable. | content → dated `DBScripts/*.sql` on the prod DB (`sqlcmd -f 65001`) + `wwwroot/uploads` copy; code → build + IIS redeploy of the working tree. Audited by **`seo-audit`**, shipped via **`deploy-content`**. |

### Treat opticalsoftware.org as production

It is live with real content and real visitors. Apply full production care: **no destructive or
casual changes**, always validate against the live site (per the Production Deployment &
Live-Verification Rule), never imply something is "live" from a dev run, and prefer idempotent,
reversible, non-destructive scripts.

### Always confirm step-context when ambiguous

If a request does not make clear **which surface** (platform repo, marketing site, or the live
deployment) and **which environment/step** (local dev DB `MARUTI-004` vs the prod DB; working-tree
code vs a deploy) it targets — **ask before acting.** Do not guess between the three surfaces or
between dev and the live site; a wrong guess can touch real content or the wrong codebase. State
which surface/environment you are operating on in your response.

---

## Database Script Rule (MANDATORY)

Whenever making any database-related change, a new SQL script file **must be created first** in the repo root before (or alongside) the code change. This is non-negotiable.

### What counts as a database-related change

- Adding, removing, or renaming a column in any table
- Creating or dropping a table
- Adding or changing an index, constraint, or default value
- Data migrations (UPDATE/INSERT/DELETE on existing rows)
- Changes to `UserSettings` / `Setting.cs` that add a new JSON-stored field (even though no schema migration is needed, a comment/no-op script documenting the intent is still required)
- Changes to `MigrationService.cs` that alter the schema
- Any Dapper query change that assumes a new column exists

### File naming convention

```
DBScripts/YYYY-MM-DD_short-description.sql
```

Use today's actual date (available in context as `currentDate`). Examples:

```
DBScripts/2026-05-17_add-google-analytics-id-to-settings.sql
DBScripts/2026-05-17_add-newsletter-preferences-column.sql
DBScripts/2026-05-17_create-audit-log-table.sql
```

### Script requirements

- Place in the **`DBScripts/` folder** at the repo root
- Must be **idempotent** — safe to run more than once (`IF NOT EXISTS`, `IF COL_LENGTH(...)`, `MERGE`, etc.)
- Include a header comment explaining what the change is and why
- For JSON-blob-only changes (no schema change needed), the script must document this explicitly:

```sql
-- DBScripts/YYYY-MM-DD_description.sql
-- Change: Added GoogleAnalyticsId to UserSettings JSON blob.
-- Schema impact: NONE — stored in Settings.JsonPayload, no column change.
-- Action required: None. Field is populated automatically on next settings save.
PRINT 'No schema migration required for this change.';
```

### Enforcement

Do **not** modify `Blog.Core/Domain/Setting.cs`, `Blog.Infrastructure/Data/MigrationService.cs`,
any `*Repository.cs`, or any `.sql` file without also writing the dated script.
If a task requires multiple DB changes, one script per logical change is preferred,
or combine them with clear section headers.

---

## sqlcmd Encoding Rule (MANDATORY)

All `sqlcmd` commands that execute SQL files **must** include `-f 65001` (UTF-8 code page).

```powershell
sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\YYYY-MM-DD_script.sql"
```

### Why this is required

Without `-f 65001`, `sqlcmd` reads `.sql` files as Windows-1252 (CP1252). Any UTF-8 multi-byte
character in the file is misread as multiple CP1252 glyphs and stored verbatim in the database
(mojibake). For example, an em dash `—` (UTF-8: E2 80 94) becomes the three-character garbage
sequence `â€"` in NVARCHAR columns.

### What gets corrupted

Any SQL file that contains — directly or in N'' string literals — any of these characters:

| Character | UTF-8 bytes | Stored as (mojibake) |
|-----------|-------------|----------------------|
| — em dash | E2 80 94 | â€" |
| – en dash | E2 80 93 | â€" |
| ' right single quote | E2 80 99 | â€™ |
| ' left single quote | E2 80 98 | â€˜ |
| " left double quote | E2 80 9C | â€œ |
| " right double quote | E2 80 9D | â€ |
| … ellipsis | E2 80 A6 | â€¦ |
| • bullet | E2 80 A2 | â€¢ |
| (NBSP) | C2 A0 | Â  |

### Fix if corruption already occurred

Use `DBScripts/2026-05-23_fix-mojibake-encoding-all-posts.sql` as a template — it uses
`NCHAR()` integer codes (encoding-neutral) to identify and REPLACE all known mojibake
sequences. Run it with `-f 65001` too, even though NCHAR() codes are safe either way.

---

## Theme / Design / Layout Backward-Compatibility Rule (MANDATORY)

Any new theme, design system, or layout addition **must not break existing users** who have not
explicitly selected the new feature. This applies to all of:

- New layout names added to `ApplyLayoutPreset` / `validLayouts`
- New Inkwell presets added to `ApplyInkwellPreset` / preset selectors
- New `CustomThemeSetting` keys seeded by `SeedDefaultsAsync`
- New view components, partial views, or shared layout files
- Changes to `_PublicLayout.cshtml`, `_AdminLayout.cshtml`, or any Shared view
- Changes to `FooterViewComponent`, `NavbarViewComponent`, or any other `ViewComponent`
- New CSS variables, Tailwind config changes, or `inkwell.css` modifications

### Mandatory checks before shipping any theme/layout change

**1. View component resilience** — Every `ViewComponent` that selects a view by name from a
DB setting **must** have an explicit supported-values guard. Unknown or legacy values must fall
back to a safe default (e.g. `"Neutral"`), never throw `InvalidOperationException`.

```csharp
// Good — explicit guard
private static readonly HashSet<string> _supported =
    new(StringComparer.OrdinalIgnoreCase) { "Neutral", "Magazine", "Grid", "Minimal", "Classic", "Modern" };

var raw = layoutSetting?.EffectiveValue ?? "Neutral";
var layout = _supported.Contains(raw) ? raw : "Neutral";
return View(layout, model);

// Bad — crashes for any DB value not in the Views folder
return View(layoutSetting?.EffectiveValue ?? "Neutral", model);
```

**2. Layout preset fan-out** — `ApplyLayoutPreset` sets `layout-navbar`, `layout-footer`,
`layout-index`, and related keys. When adding a new index layout, **always** check whether the
corresponding navbar/footer view exists. If it does not, map to the nearest supported variant
in the `footerNavbarMap` dictionary inside `ApplyLayoutPreset` — never let the map fall
through to a view that does not exist.

Available footer views: `Classic`, `Grid`, `Magazine`, `Minimal`, `Modern`, `Neutral`
Available navbar views: `Classic`, `Default`, `Feed`, `Grid`, `Magazine`, `Minimal`, `Modern`, `Neutral`

**3. Partial view coverage** — Every layout name that appears in any of these places must have
a corresponding `_Index_<Name>.cshtml` partial in `Blog.Web/Views/Blog/`:
- `validLayouts` array in `ThemeSettingsController.ApplyLayoutPreset`
- `var layouts` array in `ThemeSettings/Index.cshtml`
- `else if (blogLayout == "…")` routing block in `Blog.Web/Views/Blog/Index.cshtml`

All three lists must stay in sync. If you add a layout to one, add it to all three and create
the partial view in the same change.

**4. ID / Name consistency** — The `Id` field in the `layouts` array in `ThemeSettings/Index.cshtml`
must exactly match (case-sensitive) the string used in the `else if (blogLayout == "…")` routing
block and the `validLayouts` array. Mismatches (e.g. old layout name reused as an ID for a new
layout) will silently render the wrong layout without any build error.

**5. CSS token namespace** — The public frontend uses the **Inkwell token namespace**
(`--bg`, `--fg`, `--fg-body`, `--fg-meta`, `--surface`, `--border`, `--link`, `--sans`, `--serif`).
The admin uses the **Tailwind/shadcn namespace** (`--background`, `--foreground`, `--primary`,
`--muted`, `--card`, `--border`). Do not mix the two. Any new CSS variable added to `inkwell.css`
must use the Inkwell namespace; admin-only variables use the Tailwind namespace.

**6. Default value safety** — New `CustomThemeSetting` keys seeded by `SeedDefaultsAsync` must
have a `DefaultValue` that produces a valid, rendered result with zero additional configuration.
The default value must correspond to a view/preset that already exists.

**7. Self-hosted fonts only** — Any font referenced by a theme/preset or a `font-heading` / `font-body`
setting **must be self-hosted** in `Blog.Web/wwwroot/fonts/fonts.css`. Never introduce a font family
that is not in that file: the public site loads **no** Google-Fonts CDN, so a non-self-hosted family
(e.g. the `plum` theme's `Nunito`) silently falls back to a system font — the design never renders as
intended, and it is a GDPR/privacy/perf regression. Before shipping a preset/theme that changes fonts:
(a) confirm both families exist in `fonts.css`; (b) prefer the two brand fonts the layout already
**preloads** (`Geist` + `Source Serif 4`) so there is no wasted preload / FOUT — if you must use other
self-hosted families, update the `<link rel="preload">` set in `_PublicLayout.cshtml` to match the
actually-used fonts. Verify on a rendered page: the computed `--font-sans` / `--font-body` resolve to a
family present in `fonts.css`.

### What "backward compatible" means here

A user who has never visited Admin → Theme must get a working, visually coherent blog. A user
who set their layout to `"Neutral"` six months ago must still get the Neutral layout after any
upgrade — their DB value must still resolve to a valid view without manual intervention.

### Enforcement

Before completing any task that touches layout routing, view components, or the layout selector
UI, verify:
- `grep -n "validLayouts"` in the controller matches the layout list in the admin view
- `grep -n 'else if (blogLayout'` in `Index.cshtml` covers every layout in `validLayouts`
- Every `Id` in `var layouts` resolves to a real `_Index_<Id>.cshtml` file
- Every `ViewComponent` that reads a layout setting has an explicit supported-values guard

---

## Audit Trail Rule (MANDATORY)

Every admin controller action that **creates, updates, deletes, or changes status** of any entity
**must** call `AuditService.LogAsync` so the change is recorded in the `AuditLogs` table.
The audit log is accessible only to Admin-role users at `/admin/audit`. It is **read-only from
the UI** — no delete or truncate endpoint is ever exposed.

### What must be logged

| Controller | Actions that require audit logging |
|---|---|
| `PostsController` | Create, Update, Delete, Publish, Unpublish, Schedule |
| `PagesController` | Create, Update, Delete |
| `CommentsController` | Approve, Reject, Delete |
| `MediaController` | Upload, Delete |
| `UsersController` | Create, Update, Delete, RoleChanged |
| `AccountController` | LoggedIn, LoggedOut, LoginFailed |
| `SettingsController` | Any settings save |
| `ThemeSettingsController` | Theme update, preset applied |
| `CategoriesController` | Create, Update, Delete |
| `TagsController` | Create, Update, Delete |
| `NewsletterController` | Newsletter sent |
| `SubscribersController` | Delete, Export |
| `RedirectsController` | Create, Update, Delete |
| Any new mutating controller | All write actions |

### How to log

Inject `AuditService` and call it after a successful write:

```csharp
// Minimal — entity type, action, entity ID, human-readable name
await _audit.LogAsync("Post.Published", "Post", post.Id.ToString(), post.Title);

// With before/after snapshot (for settings, user profile changes)
await _audit.LogAsync("Settings.Updated", "Settings", ownerId.ToString(), "Site Settings",
    oldJson: JsonSerializer.Serialize(before),
    newJson: JsonSerializer.Serialize(after));
```

### Standard action string format

`EntityType.Verb` — e.g. `Post.Published`, `Comment.Deleted`, `User.RoleChanged`.
Always use the exact strings from the table in `Blog.Core/Domain/AuditActions.cs`
(a static class of string constants) — never free-form strings.

### Enforcement

Before completing any task that adds or changes a write action in an admin controller:
1. Verify `AuditService` is injected in the controller constructor.
2. Verify `LogAsync` is called on every success path (not inside `catch` blocks).
3. Do **not** log on validation failures or 4xx responses — only on committed changes.
4. Never add a delete/truncate endpoint or UI control for audit logs.

---

## SEO / AEO / AI-Search Rule (MANDATORY)

This is a **multi-tenant blog engine**. Every SEO and AI-search (AEO) capability **must
work for any tenant with zero manual configuration** and must degrade safely for tenants who
never touch settings (same spirit as the Theme/Layout Backward-Compatibility Rule).

The authoritative audit, prioritized action plan, and progress tracking live in
**[`SEO-AEO-PLAN.md`](SEO-AEO-PLAN.md)** at the repo root. It is a **living document** — keep it current.

### What counts as an SEO/AEO-related change

- Any change to `robots.txt`, `sitemap.xml`, or `/feed` generation (`BlogController`)
- Any change to `<head>` metadata: `<title>`, `meta description`, canonical, `og:*`, `twitter:*`,
  `hreflang`, `theme-color`, `author`, `robots` meta — in `_PublicLayout.cshtml`, `Post.cshtml`,
  or any view that emits page metadata
- Any change to JSON-LD / structured data (`_SchemaJsonLd.cshtml`, Organization/WebSite schema)
- Any change to `llms.txt` / `llms-full.txt` or AI-crawler permissions
- Adding a new public page type, route, or content type that should be indexable

### Mandatory checks before shipping any SEO/AEO change

1. **Tenant-neutral, never hardcoded.** No tenant's name, domain, topics, or facts may be
   hardcoded (the current static `wwwroot/llms.txt` is a known violation tracked in the plan —
   do not add more). All SEO output derives from tenant settings / DB.
2. **Every indexable page carries the essentials.** A public page must emit exactly one
   `<title>`, one `meta description`, one canonical, and OG/Twitter tags. Post pages additionally
   carry `BlogPosting` + `BreadcrumbList`. Assert **exactly one** of each `canonical`, `og:type`,
   `hreflang="x-default"` per page.
3. **Correct AI-crawler tokens.** Use exact bot tokens (`Google-Extended`, **not**
   `Googlebot-Extended`; `GPTBot`, `OAI-SearchBot`, `ClaudeBot`, `PerplexityBot`, `Applebot-Extended`).
   Verify against the current published token list, not memory.
4. **Sitemap completeness.** Any new indexable route type must be added to `sitemap.xml`
   generation (posts, categories, tags, CMS Pages, authors). A route absent from the sitemap is a defect.
5. **Schema validity.** New JSON-LD must use valid schema.org `@type`/properties and must not
   emit self-serving or fabricated ratings (`AggregateRating`/`Review` must trace to a real source).
6. **No performance regressions in `<head>`.** Do not add render-blocking synchronous scripts to
   `<head>`. New scripts use `defer`/`async`; new fonts use `font-display:swap` + preload.
7. **IndexNow key must be served at the site ROOT.** The IndexNow ownership key must resolve at
   `https://{host}/{key}.txt` (root) — **not** a subdirectory like `/indexnow/{key}.txt`. Per the
   IndexNow protocol, a key in a subdirectory only authorizes URLs *under that directory*, so
   submitting root-level post URLs against a subdirectory key returns **HTTP 422
   `InvalidRequestParameters`**. The `keyLocation` sent with a submission must point at the root key.
   A submission is only honored once the key file returns **200** at that location; verify before
   claiming URLs were submitted (a 202 is acceptance, not indexing). IndexNow feeds Bing/Yandex/
   Seznam/Naver only — **Google does not consume IndexNow** (use Search Console for Google).

### Enforcement

Before completing any task touching the files/areas above:
1. Update the relevant checkbox(es) and the **Progress Log** in [`SEO-AEO-PLAN.md`](SEO-AEO-PLAN.md).
2. Confirm the change is tenant-neutral and degrades safely with default settings.
3. If the change also touches the DB / a repository / a `.sql` file, the **Database Script Rule**
   still applies (dated `DBScripts/YYYY-MM-DD_*.sql`).
4. If the change adds an admin write action (e.g. saving SEO settings), the **Audit Trail Rule** applies.

---

## Documentation Maintenance Rule (MANDATORY)

Treat this repository like a **professional software services deliverable**: the product
documentation must always reflect the product's true, current state. Whenever a change alters
what the software does, how it is set up, or fixes broken behavior, the docs are updated **in the
same change** — never "later".

### What must be updated

For every **new feature / enhancement, change request (behavior or config change), or bug fix**
that is user-facing or operator-facing, update **both**:

1. **[`README.md`](README.md)** — keep the relevant section truthful and current:
   - New capability → add/adjust the **Features** and/or **Why Inkwell** bullet.
   - New/changed dependency, runtime, or DB support → update the **Tech Stack** table.
   - New/changed setup, config key, env var, or run step → update **Quick Start** / config docs.
   - Removed or renamed capability → delete/rename its bullet (never leave stale claims).
2. **[`CHANGELOG.md`](CHANGELOG.md)** — add an entry under an **`## [Unreleased]`** heading
   (create it at the top if absent), using the existing [Keep a Changelog](https://keepachangelog.com)
   categories: **Added / Changed / Fixed / Deprecated / Removed / Security**. Roll `Unreleased`
   into a dated, semver-bumped release heading when the version is cut.

### Writing standard (professional tone)

- Write for the **user/operator**, not the committer — describe the outcome, not the diff.
- One concrete bullet per change; name the feature/endpoint/setting, not the file that moved.
- No internal chatter, ticket IDs, or "misc fixes". No emojis in CHANGELOG entries.
- Keep entries consistent in voice and grammar with the surrounding document.

### What does NOT trigger this rule (do NOT log these)

- **Blog content publishing / content-seed scripts** — SQL or scripts that insert, update, or
  publish **blog posts, pages, or other tenant content** (e.g. `DBScripts/*` that seed/publish
  articles) are **content operations, not product changes**. Never add them to `README.md` or
  `CHANGELOG.md`.
- Pure internal refactors, test-only changes, comment/formatting-only edits, or dependency
  bumps with zero user-facing or behavioral effect (a `CHANGELOG` entry is optional, not required).
- The dated `DBScripts/YYYY-MM-DD_*.sql` **schema** scripts are still governed by the Database
  Script Rule; that rule is independent of this one.

### Relationship to other rules

This rule is **additive** — it never replaces the Database Script, sqlcmd, Theme/Layout, Audit
Trail, or SEO/AEO rules. A single feature may require: a DB script **and** an audit call **and** a
README/CHANGELOG entry. Satisfy all that apply.

### Docs follow code — README accuracy (MANDATORY)

The `README.md` must describe **only what the code actually does**. When a README claim and the
code disagree, the **README is what changes** — correct the README to match reality. Do **not**:

- leave a known-false or aspirational claim in place (e.g. listing a database, provider, driver,
  runtime, or feature the code does not actually wire up);
- add or build code **solely to make a stale README claim true** — that is scope creep dressed up
  as a doc fix. If the capability is genuinely wanted, it is a separate, explicitly-requested feature.

The fix for an inaccurate claim is a **README edit only** (delete/rewrite the claim). A dependency
being *referenced* (a `PackageReference`) is **not** proof a feature works — verify the runtime path
(e.g. `DapperContext` only creates `SqlConnection`, so SQLite is **not** a supported database however
the package list reads). Surface such mismatches to the user; never silently "make it true" in code.

### Enforcement

Before marking any feature/CR/bug-fix task complete:
1. Confirm `README.md` still describes the product accurately (add/edit/remove the affected bullet or table row), and that no claim is aspirational — every capability listed is actually wired in code.
2. Confirm a `CHANGELOG.md` `[Unreleased]` entry exists under the correct category.
3. Confirm the change is **not** a blog-content publish/seed operation (if it is, skip both docs).

---

## Content External-Link Rule (MANDATORY)

Any roundup, comparison, "best of", or review content that names a product, software, or company
**must** link that entity's **real, verified website**. This is a content-quality and trust
requirement (helps E-E-A-T, AEO citation, and reader usefulness), and it feeds the
`SoftwareApplication.url` in the roundup JSON-LD.

### What must be linked

- **Every roundup entry** (`RoundupEntry`) must set `website` to the vendor's official URL
  (e.g. `https://www.revolutionehr.com`). This is the outbound "Visit website" reference and the
  product's canonical `url` in `ItemList` / `Review` structured data.
- The entry's `ctaUrl` remains the **internal** review-post path (e.g. `/eyefinity-ehr-review`)
  for topic-cluster linking — it is **not** a substitute for `website`.
- In prose (`Post.Html`) that names a product, link it once to its site or the most relevant page.

### Verify, never fabricate

- Confirm each vendor URL (official domain + relevant deep page) before using it — a wrong URL is
  worse than none. Do not invent URLs from the product name.
- External vendor links render `rel="nofollow noopener"` (editorial reference); a paid/affiliate
  link uses `rel="nofollow noopener sponsored"`.

### OptoSoft priority (first-party product)

**OptoSoft** (`https://www.opto-soft.com`) is the operator's own product. For content where OptoSoft
is a **genuine fit** — optical **retail / POS / shop-management / inventory / online-optical /
optical-chain / optician / dispensary** software (and the optical side of practice management) —
give it **~50% priority**: feature it prominently (typically the Editor's-pick top entry) and
**deep-link the most relevant OptoSoft page**, not just the homepage:

| Topic | OptoSoft page to link |
|---|---|
| Optical POS / billing | `/optical-pos-software` |
| Shop / store management | `/optical-shop-management-system` |
| Optometry practice management | `/optometry-practice-management` |
| Optician software | `/optician-software` |
| Inventory (frames/lenses) | `/optical-inventory-management` |
| Online / cloud optical | `/online-optical-software` |
| Optical chains / multi-store | `/optical-chain-management-software` |
| Region pages | `/optical-software-india`, `/optical-software-uae`, `/optical-software-saudi-arabia`, `/optical-software-uk` |
| Compare / features / pricing | `/compare-optical-software`, `/features`, `/pricing` |

**Honesty guard:** never insert OptoSoft into a roundup where it is **not** a real competitor
(e.g. a US **clinical EHR** comparison). There, at most add one contextual link to the relevant
OptoSoft page from the narrative. Priority means prominence **where relevant**, never fabricated
placement or ratings (this rule is subordinate to the SEO/AEO Rule's ban on self-serving ratings —
OptoSoft's own `AggregateRating` must still trace to a real source).

### Enforcement

Before completing any roundup/comparison/review content task:
1. Every entry has a verified `website`; every product named in prose is linked once.
2. If the topic is optical-retail-family, OptoSoft is featured with a deep link to its most
   relevant page per the table above.
3. Roundup content is a **content operation** — it does **not** trigger the Documentation
   Maintenance Rule. Product/code changes that add or change the `website` field itself **do**.

---

## Production Deployment & Live-Verification Rule (MANDATORY)

The single source of truth for whether anything is "live" is the **production site
`https://www.opticalsoftware.org`** — the LIVE test deployment of the Inkwell platform, and the
**base against which all SEO & AI-search rank improvements are measured**. Never claim a change is
"live" based on a dev run.

### Dev is not prod

- All local work targets the **dev database** (`MARUTI-004`, `Database=blog`). Writing to it does
  **not** reach production. Production serves a **separate** SQL database on an IIS/ASP.NET host.
- "Verified live" in any plan/changelog/commit means **re-fetched from `www.opticalsoftware.org`**
  (production), not from `localhost`/dev. If you only checked dev, say "verified on dev", never "live".

### Content deploy ≠ code deploy (two separate pipelines)

| Change type | What it is | How it reaches prod |
|---|---|---|
| **Content** (posts, pages, settings, roundup data) | DB rows | dated idempotent `DBScripts/*.sql` upserts run against the **prod DB** with `sqlcmd -f 65001`, **plus** copying any referenced `wwwroot/uploads/**` assets (gitignored) to the prod webroot |
| **Code** (layouts, presets, `_Index_*`/partials, `inkwell.css`, controllers, view components, SEO features) | compiled app + views + static assets | commit → `dotnet publish -c Release` → **redeploy the app to IIS** |

- Layouts and presets are **code**, not DB rows — a content deploy never ships them. A new/updated
  layout or preset is live only after a **code rebuild + redeploy**, and then must be **selected in
  Admin → Theme** to affect the public site.
- Uncommitted working-copy views/CSS are **not** deployable — commit first.

### Mandatory live re-verification after any deploy

Re-fetch from production and confirm, per surface:
- **Content:** each new `/{slug}` returns **200**; present in `/sitemap.xml`; correct JSON-LD
  (`BlogPosting`(+`FAQPage`), roundups also `ItemList`/`Review`); **zero mojibake** (`grep -c 'â€' = 0`).
- **Code:** the new layout/preset renders (or its selector appears); static asset `Last-Modified`
  advanced; `llms.txt`/`tailwind.css` reflect the new build.
- Use a **no-auto-redirect** client for status checks; account for IIS/edge cache and cold starts
  (first request may time out — retry before reporting an outage).

### Legacy / stale-index hygiene (domain was repurposed)

`opticalsoftware.org` previously hosted the **OptoSoft optical-retail product site**; it is now the
independent reviews/guides blog. Because of that history:
- Old product routes (`/aboutus`, `/features`, `/pricing`, `/optical-*-software`, …) now **404**.
  Any that are still **indexed** by Google must be handled deliberately — `410 Gone` or a `301` to
  the closest live blog page (or to the product's new home on `opto-soft.com`) — never left as an
  indexed 404. Re-submit `sitemap.xml` in Search Console and request re-crawl so the stale
  OptoSoft-product titles/snippets are replaced by the current blog identity.
- Watch for **entity confusion**: search/AI may still describe the domain as "OptoSoft … since 2008".
  Strengthen the reviews-blog identity (Organization schema, llms.txt `## Identity`) but never
  fabricate independence — OptoSoft first-party disclosure rules still apply.

### Enforcement

Before reporting any task as done/"live":
1. State explicitly whether verification was against **prod** or **dev**.
2. If it required a prod DB run or a code redeploy you cannot perform, say so and hand off the exact
   scripts/steps — do **not** imply it is live.
3. Content changes still obey the Database Script, sqlcmd-encoding, and (for write actions) Audit
   rules; code changes still obey the Theme/Layout, SEO/AEO, and Documentation rules.

---

## Internal Linking & Content Lifecycle Rule (MANDATORY)

An article nobody links to is an article Google discounts. The 2026-08-15 audit found the live site
had **49 editorial body links across 59 posts** — 44 posts linked to nothing, and 31 had no inbound
editorial link at all — which is a large part of why head terms sit at position 40+. Internal links
and freshness are the two levers that need no outreach and no waiting, so they are not optional
polish; they are part of publishing.

### Every post ships linked (no orphans)

A post is not "done" until:

1. **≥2 outbound contextual links** to related published posts, placed **in the prose** next to the
   relevant sentence — never a "related links" dump appended at the end.
2. **≥2 inbound links added from existing posts** in the same change. Publishing a post without
   editing anything else creates an orphan by definition.
3. **First-mention linking** — the first time a post names a product we review, that mention links to
   our review of it (`RevolutionEHR` → `/revolution-ehr-review`). Do not link every mention:
   **first free mention only, at most 3 new links per source post**, and never re-link a mention that
   already sits inside an `<a>` or inside an HTML tag/attribute.
4. **Hub ↔ spoke both ways** — a roundup links each product to its review (`ctaUrl`), and each review
   links back to the roundup. The vendor's real site belongs in `website`, never in `ctaUrl`
   (Content External-Link Rule).
5. **Descriptive, varied anchors.** No "click here", no bare URLs, no repeating one exact-match phrase.

### Freshness is recorded, not implied

`Post.LastVerifiedAt` and `Post.NextReviewAt` exist and must be **set on publish and on every
refresh** — as of 2026-08-15, 46 of 59 posts had never been verified and **none** had a review date,
so nothing could be scheduled or measured. Year-stamped titles (`… 2026`) carry an implicit promise:
schedule their review before the year turns, and update the content, not just the number.

### Never fabricate to fill a gap

Links must help the reader — every one must survive "would I click this?". Never invent a fact, a
date, a rating, or a relationship to justify a link, and never link two unrelated posts to raise a
count. If a cluster has no hub, that is a **content** task (write the roundup), not a linking task.

### Enforcement

Before completing any content task:
1. Re-run the link graph (the `internal-linking` skill) and confirm the edited posts have inbound and
   outbound links, and that no new orphan was created.
2. Body-link edits are **content**: dated idempotent `DBScripts/YYYY-MM-DD_internal-links-*.sql`,
   guarded so re-running changes nothing, dev first, then prod with `sqlcmd -f 65001`.
3. Verify HTML integrity after any bulk edit — balanced `<a>` tags, no nested anchors, zero mojibake.
4. Content publishing/linking is a **content operation**: it does **not** trigger the Documentation
   Maintenance Rule. Product changes that alter linking *behaviour* do.
5. Log the pass in the `SEO-AEO-PLAN.md` Progress Log and re-measure against the next GSC export.

---

## Search Console Data Rule (MANDATORY)

Real Google Search Console data **overrides inference**. The owner drops "Performance on Search"
exports into **`Google Search Console Data/`** at the repo root as
`opticalsoftware.org-Performance-on-Search-YYYY-MM-DD.xlsx`. That folder is the measured truth about
how `https://www.opticalsoftware.org` actually performs — a `site:` query, a guess about rankings, or
"this should rank well" is not.

### Step 0 of every SEO / AEO / AI-search audit

**Before fetching a single page**, list `Google Search Console Data/`, take the file with the newest
`-YYYY-MM-DD` (tie-break on mtime), and open it. Then, in your report:

1. **State which export you used and its date**, plus the Filters tab (search type + date range).
2. **Flag stale data** — if the newest export is more than ~30 days old, say so and ask for a fresh one.
3. **If the folder is empty or missing, say so explicitly** and ask for an export. Never silently fall
   back to `site:` inference or assumption.

This applies to `seo-audit`, `gsc-performance-review`, any content/keyword planning, and any claim
about rankings, traffic, indexation, or CTR.

### Reading it (no Excel, no Python, no external service)

An `.xlsx` is a zip of XML. Sheet order is **1 Chart · 2 Queries · 3 Pages · 4 Countries · 5 Devices ·
6 Search appearance · 7 Filters**; Queries/Pages columns are **Clicks · Impressions · CTR · Position**.
Extract with `System.IO.Compression`, resolve `xl/sharedStrings.xml`, then read
`xl/worksheets/sheetN.xml` — the ready-to-run PowerShell is in the **`gsc-performance-review`** skill.

### Ground every claim in it

- Quote position / impressions / CTR when asserting a ranking or traffic fact. Where the export is
  silent, say it is silent — **never invent a metric** and never "fix" the site to match an assumed one.
- Impressions > 0 proves a URL is **indexed** — settle indexation questions with the number, not a guess.
- Cross-check findings against the export before proposing work: a page at position 11 with real
  impressions is worth more than a rewrite of something nobody sees.
- Log a dated baseline row in [`SEO-AEO-PLAN.md`](SEO-AEO-PLAN.md) **Progress Log** each time a new
  export is reviewed (export date, clicks, impressions, avg CTR/position, actions opened).

### Never publish it

These exports contain the site's commercial performance data. `Google Search Console Data/` is
**gitignored** — keep it that way (this repo publishes to a public GitHub). Never paste full query or
page tables into the README, CHANGELOG, release notes, marketing copy, or any artifact that leaves the
machine. Aggregates in internal planning docs are fine.

> **Surface note:** this rule covers **surface #3** (`opticalsoftware.org`, this repo). The marketing
> site `useinkwell.app` has its **own** export folder and its own user-level
> `gsc-performance-review` skill — never mix the two datasets or the two sites' findings.

---

## Data Portability & Export Fidelity Rule (MANDATORY)

Inkwell imports from WordPress and Ghost, and must be equally easy to **leave** — no-lock-in is a
product promise, not a nice-to-have. Any feature that emits tenant content in a foreign format (the
exporter in [`EXPORTER-PLAN.md`](EXPORTER-PLAN.md), backups, feeds-as-migration, API dumps) obeys the
rules below. This rule is about **content leaving the platform**; the importer's rules live in
[`IMPORTER-PLAN.md`](IMPORTER-PLAN.md).

### Verify the target format — never write one from memory

Import formats change. Before emitting or changing an artifact, **re-confirm the target's current
accepted format from its official docs** and cite what you checked in the plan/PR. As verified
2026-08-15:

- **WordPress** → **WXR** (WordPress eXtended RSS; importer reads 1.0/1.1/1.2). Native Tools → Import.
- **Ghost** → **JSON or a ZIP of JSON + `content/images/**`**. Ghost **does not** read WordPress WXR —
  emitting WXR "for Ghost" is a defect, not a shortcut.

### Fidelity is the deliverable

1. **Declare every loss up front.** Each target needs a fidelity matrix (what transfers, what degrades,
   what is dropped), shown in the UI **before** the export runs — never discovered afterwards.
2. **Inkwell-only structured content must degrade to readable HTML.** `RoundupJson`, `FaqJson`,
   `HowToJson`, `KeyFactsJson`, and Series have no WordPress/Ghost equivalent. They render to
   self-contained HTML in the body (and, where the target allows, the raw JSON is preserved in post
   meta). A destination page must never show an empty section where a block used to be.
3. **Never fabricate.** An exporter may drop or flatten data and say so; it may never invent a field,
   a date, an author, or a rating to satisfy a schema.
4. **Prefer the target's own conversion.** Send Ghost `html` and let it build Lexical rather than
   emitting a hand-rolled Lexical AST — their lossy conversion beats our guess.
5. **UTF-8 end to end**, and verify a round-trip preserves em dashes and curly quotes. Mojibake in an
   export is the same class of defect the sqlcmd rule exists to prevent.

### Security — an export is a full-content exfiltration channel

- **Admin-only** + `[ValidateAntiForgeryToken]`. Never an unauthenticated, Editor, or Author route.
- **Never emit secrets**: no password hashes, API keys, connection strings, `Settings` rows, or member
  emails. Author emails are opt-in only. Push credentials live in the request for the job's duration —
  never persisted, logged, or written into audit payloads.
- Artifacts are written **outside the web root** and streamed back through an authenticated action —
  never dropped into `wwwroot` where a URL guess exposes the whole blog. Purge them on a schedule.
- Every run is audited (`Export.*` in `AuditActions`) with target, mode, and counts — never credentials.
- **Stream** (`XmlWriter` / `Utf8JsonWriter` / `ZipArchive`); never build the artifact in memory.

### Enforcement

Before marking any export/portability task complete:
1. The artifact has been **imported into a real instance of the target** — schema validity is not
   proof. State which version you tested against.
2. The fidelity matrix in `EXPORTER-PLAN.md` matches what the code actually does, and the UI shows it.
3. Database Script, Audit Trail, and Documentation Maintenance rules still apply (new tables → dated
   script; new write actions → audit; user-facing capability → README + CHANGELOG).
4. Use the **`content-export`** skill for the full runbook.

---

## Security & Abuse-Protection Rule (MANDATORY)

`https://www.opticalsoftware.org` is a live, publicly-reachable site under continuous automated
attack (WordPress/PHP scanners, path-traversal probes, admin brute-force). The platform defends
itself with the **IP Firewall** — `IpFirewallMiddleware` → `IpFirewallService` → `ThreatScorer`,
rules in `IpFirewallRules`, operated from **Admin → Security**. Any change that touches request
handling, authentication, or the firewall itself obeys the invariants below.

### The five safety invariants (never break these)

A blocking system that misfires takes the site off the internet — for readers, or for Googlebot.
These are non-negotiable, and every one of them has a test in `Blog.Tests/IpFirewallTests.cs`:

1. **Fail open.** The firewall never breaks a request. Every method swallows its own exceptions; if
   the database is unreachable the site keeps serving and nothing is blocked. Never let a firewall
   failure surface as a 500.
2. **Allowlist supremacy.** Loopback, private ranges, and the operator's configured
   addresses/CIDRs are checked **before** any rule and can never be blocked — including by a manual
   block. `SecurityController.Block` also refuses to block the caller's own address.
3. **Search and AI crawlers are never blocked for ambiguous signals.** Googlebot, Bingbot, GPTBot,
   ClaudeBot and friends are exempt from 404/403 scoring (losing them costs far more than the
   marginal risk). They are **not** exempt from exploit-probe scoring — a real crawler never
   requests `/wp-login.php`. Never invert this trade-off.
4. **Signed-in staff are never scored.** An admin clicking a dead link must not contribute to a
   block. Detection runs after `UseAuthentication`, and authenticated requests return early.
5. **Proxy headers are untrusted by default.** `CF-Connecting-IP` / `X-Forwarded-For` are read only
   when the operator has enabled `FirewallTrustProxyHeaders`. Never trust them unconditionally — a
   forged header lets an attacker dodge a block or frame an innocent address.

### When adding or tuning detection

- **New attack signatures go in `ThreatScorer`**, not in a controller or middleware — it is the one
  place scoring is decided, and it is pure and unit-testable. Add the pattern **and** a test case in
  the same change, including a negative case proving a legitimate URL still passes.
- **Watch for false positives on real content.** Before adding a pattern, check it cannot match a
  post slug, a category/tag/series/author route, an upload path, or `/.well-known/*`. Free-text
  surfaces (`/search`) are never scanned for injection payloads — a reader may type anything.
- **Keep the hot path memory-only.** Enforcement reads the cached snapshot; denied-request counters
  are buffered and batch-flushed. Never add a per-request database write or an outbound HTTP call to
  the firewall path — an attack would then amplify into a self-inflicted outage.
- **Detection signals that are invisible in the status code must be reported explicitly** (as
  `AccountController` does for rejected sign-ins via `RegisterFailedLoginAsync`).

### When adding any new public endpoint or middleware

- New middleware goes **after** `app.UseIpFirewall()` — the firewall stays the front door so blocked
  addresses cost nothing. Never add a path that bypasses it.
- New admin write actions keep the existing protections: `[Authorize]` with the right policy,
  `[ValidateAntiForgeryToken]`, and an `AuditService.LogAsync` call (Audit Trail Rule).
- Never expose an endpoint that lets an unauthenticated caller create, modify, or clear firewall
  rules, and never add a bulk "clear all blocks" control.

### Enforcement

Before completing any task that touches the firewall, the request pipeline, or authentication:
1. Confirm all five invariants still hold, and run `dotnet test Blog.Tests/Blog.Tests.csproj`
   (`ThreatScorerTests`, `IpAllowlistTests`, `IpFirewallEnforcementTests` must pass).
2. Schema changes still require a dated `DBScripts/YYYY-MM-DD_*.sql` (Database Script Rule); rule
   mutations still require an audit entry (Audit Trail Rule); user-facing behavior still requires
   README + CHANGELOG updates (Documentation Maintenance Rule).
3. Use the **`security-hardening`** skill for the full checklist when investigating an attack or
   extending the firewall.

---

## Version-Control / No-Commit Rule (MANDATORY)

**Never run `git commit`, `git push`, `git merge`, `git rebase`, or any history-writing git command,
and never suggest, recommend, or offer to commit or push** — not on `main`, not on a branch, not
"for review". Version control is **entirely the user's responsibility**.

- Make code and file changes in the working tree only; leave staging/committing/pushing to the user.
- Do **not** end responses with offers like "want me to commit this?" or "shall I branch and commit?"
  — omit commit/push suggestions completely.
- Read-only git (`git status`, `git diff`, `git log`, `git show`) is fine for inspection.
- Deploys here build from the **working tree**, so uncommitted changes still reach the testing
  environment — do not frame missing-on-prod issues as "needs a commit"; frame them as "needs a
  build + redeploy of the current working tree".
- If the user explicitly asks to commit in a specific message, follow that instruction for that
  request only; otherwise stay hands-off from version control.

---

## Release & GitHub Publish Rule (MANDATORY)

The platform is published as **Inkwell** at `github.com/marutisoftwaresolutions/inkwell` (surface #1 in
the Solution Topology). When cutting a release:

1. **Single-source version** — bump `<Version>` in the repo-root **`Directory.Build.props`** only
   (it stamps every assembly's AssemblyVersion/FileVersion/InformationalVersion). Do not scatter
   version numbers into individual `.csproj` files or the README. **Confirm the version number with the
   owner** if not given — do not assume a semver bump (e.g. the owner may keep a patch number).
2. **Roll the CHANGELOG** — move everything under `## [Unreleased]` into a new dated
   `## [X.Y.Z] — YYYY-MM-DD` heading with a fresh empty `## [Unreleased]` above it. **One** Added /
   Changed / Fixed group per release (consolidate duplicates); newest-first; Keep-a-Changelog format.
   Never leave two headings for the same version.
3. **Release notes** — update `RELEASE_NOTES.md` (the GitHub release body): title `Inkwell vX.Y.Z`,
   date, highlights, and upgrade notes. Keep it truthful to the CHANGELOG — no aspirational claims.
4. **Pre-publish safety scan (public repo)** — verify **no committed file leaks secrets**: confirm
   `appsettings*.json`, `.claude/settings.local.json`, and `wwwroot/uploads/**` are gitignored and
   untracked; `appsettings.example.json` holds only placeholders; `git grep` for known secret markers
   returns 0 in tracked files; `LICENSE` is present. Never print real secret values.
5. **Hands off git** — per the No-Commit Rule, prepare the release in the working tree only; the
   **owner** commits, tags (`vX.Y.Z`), and publishes. State the suggested tag; never run
   `git commit`/`tag`/`push` or offer to.

### Enforcement

Before reporting a release as prepared: version is in `Directory.Build.props` and verified stamped in
the build; CHANGELOG has a single dated heading + fresh `[Unreleased]`; `RELEASE_NOTES.md` matches;
the secret scan passed. Use the **`cut-release`** skill for the full checklist.
