# Inkwell v1.0.8

**Release date:** 2026-10-06
**Type:** Feature and security release (backward-compatible: existing tenants keep their layout, theme, content and settings; one default changes, see Upgrade notes)

v1.0.8: **"Open by design."** A blog you own should be as easy to write in, extend and leave as the
platforms it competes with. This release lets you take your content to WordPress or Ghost and back
up a whole site to one file. It adds Markdown, galleries, custom fields, menus, webhooks, editorial
review and two-factor sign-in. You can now send the newsletter your readers subscribed to, and every
date follows your time zone and your country's format.

## Highlights

### 📦 Leave whenever you like, keep everything when you stay

- **Export to WordPress and Ghost.** Admin → Export downloads a WordPress WXR 1.2 file or a Ghost
  import ZIP. A summary built from your own content shows what carries over, what changes and what
  is not exported before you download. Inkwell's own blocks (answer capsule, key facts, roundups,
  how-to steps, FAQs, series) are written into the body as ordinary HTML, so nothing arrives empty.
  Tested by real imports into WordPress 7.1.2 and Ghost 6.67.0.
- **Backup and restore.** Admin → Backups makes one ZIP with every post, page, user, setting,
  comment, subscriber, revision, the audit trail and every uploaded file. Restore it into a fresh
  install from the first-run screen; links between content survive and users keep their passwords.

### ✍️ Write the way you prefer

- **Markdown authoring** with a self-hosted editor, toolbar, side-by-side view and a server-rendered
  preview. Switch an existing post either way; the switch is refused if it would change how the post looks.
- **Gallery block** in every editor, with a self-hosted lightbox that loads only on pages with a gallery,
  and **alt text and captions** for images in the media library.
- **Custom fields** for posts and pages (text, number, date, web address, yes/no), shown to readers
  as a "Details" list when you choose and carried into structured data.
- **Editorial review.** Authors without the publish permission submit posts; an Editor approves or
  sends them back with a note. Before this, an author's Publish silently saved a draft.

### 🧭 Extend and connect

- **Navigation menus** for the header and footer, nested one level, in every navbar and footer layout.
  Until you save a menu, nothing changes.
- **Webhooks** send a signed JSON request on publish, update, unpublish and new comments, with retries,
  a 30-day delivery log, redeliver and test. Private-network addresses are refused.

### 📬 Newsletters and dates that fit your readers

- **Newsletter sending.** Admin → Newsletter writes an issue (subject, preview text, a Markdown
  introduction, up to six posts as cards), previews it, sends a test, then sends to your confirmed
  subscribers in background batches. Every copy has its own unsubscribe link and one-click unsubscribe
  headers (RFC 8058), and a plain-text version. No tracking pixels, no rewritten links.
- **Dates in your time zone and your country's format.** Settings → Display → **Date and time** sets
  country, time zone (IANA), date format (Unicode CLDR or ISO 8601) and clock. Every date on the
  public site and in the Desk follows it; before, most dates showed UTC or the server's clock.

### 🔐 Safer sign-in, cookie consent, a cacheable home page

- **Two-factor sign-in** with an authenticator app and ten recovery codes, optional for everyone and
  requirable per role. The API login accepts the code too.
- **Cookie consent**, on by default: Google Analytics loads only after a reader accepts.
- **The home page can be cached by a CDN.** It no longer sets a cookie or sends `no-store`.

## Security

- **An unused autosave endpoint let other websites overwrite posts.** It had no anti-forgery,
  ownership or audit check. It has been removed.
- **An author could publish by crafting the save request.** The server now decides every status from
  the button pressed and the user's permission.
- **The setup wizard stayed open after the first admin registered** through `/account/register`.
  It now closes as soon as any user or settings exist.
- **Failed API sign-ins were invisible to the IP firewall.** They now count toward the same limits as
  the Desk sign-in page, as do failed two-factor codes.
- **The subscription confirmation email inserted the subscriber's name unescaped.** It is now encoded.

## Fixed

- Opening an unsubscribe link unsubscribed immediately, so mail scanners could unsubscribe readers.
  It now asks first; one-click unsubscribe from inbox providers still works.
- Listing pages answered any page number and any query string. A page past the last now returns 404,
  and unknown parameters redirect (301) to the clean URL; paging, search, filters and campaign tags are kept.
- Editing a page did not save its search and social fields.
- Comments and social-card images were available for unpublished posts.
- Search results lost the query from page 2 onwards.
- Structured data and Open Graph times had no time zone; they now end in `Z`.
- Cancelling a Desk confirmation turned off the unsaved-changes warning.
- Smaller fixes to labels, the Feed header layout, the welcome email and the Settings page are in
  the CHANGELOG.

## Upgrade notes

- **Schema is automatic.** `MigrationService` adds, on startup, the tables `Webhooks`,
  `WebhookDeliveries`, `MenuItems`, `Countries`, `TimeZones`, `DateLocales`, `NewsletterIssues` and
  `NewsletterDeliveries` (the reference tables are seeded from IANA tzdata 2026e and CLDR 48), and
  new columns on `Posts`, `Pages`, `Users` and `CrawlerVisits`. For a manual upgrade, run these
  idempotent scripts from `DBScripts/` in order: `2026-09-27_add-query-to-crawlervisits`,
  `2026-09-27_create-webhooks-tables`, `2026-09-27_create-menu-items-table`,
  `2026-09-27_add-review-workflow-to-posts`, `2026-10-05_add-two-factor-to-users`,
  `2026-10-05_add-markdown-to-posts`, `2026-10-05_add-custom-fields-to-posts-and-pages`,
  `2026-10-05_create-and-seed-countries-timezones-date-locales`,
  `2026-10-05_create-newsletter-issues-and-deliveries`. The four `*-to-settings` scripts of the same
  dates change no schema (settings are stored as JSON). A fresh
  install gets the full schema from `DBScripts/init.sql` alone, verified before this release by
  diffing `sys.columns` against a live database in both directions.
- **One default changes: cookie consent is on.** If you set a Google Analytics Measurement ID, the
  tag now loads only after a reader accepts, so GA will count fewer visitors. To keep the earlier
  behaviour, turn off **Ask readers for cookie consent** in Settings → Search & analytics.
- **Choose your dates.** A site that changes nothing shows US-English dates in UTC. Set your country,
  time zone and format in Settings → Display → Date and time. A Windows time zone saved earlier keeps
  working.
- **Before sending a newsletter,** configure SMTP and publish SPF, DKIM and DMARC records for the
  sending domain. `Newsletter:BatchSize` (default 50 per minute) sets the pace. Newsletter sending,
  Export and Backups are single-site features and are hidden in cloud mode.
- **Backups** are stored in `Backups:Path` (default `App_Data/backups`), keeping the newest 10
  (`Backups:Keep`). They do not include the encryption key ring: after a restore, reconnect Search
  Console and re-enter webhook secrets.
- **Behind a CDN,** you may now add an edge cache rule for `/`. Post pages are not cacheable yet.
- **Removed:** `DBScripts/migration-all-changes.sql`, an early one-off script that no longer ran
  cleanly. Use `init.sql` or the automatic migration.
- **Backward-compatible otherwise.** Menus, custom fields, webhooks, two-factor requirements and the
  date settings change nothing until you configure them.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
