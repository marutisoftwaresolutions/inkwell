# Inkwell v1.0.6

**Release date:** 2026-09-24
**Type:** Feature release (backward-compatible — existing tenants keep their layout, theme, content and settings)

v1.0.6 — **"Measure what matters."** The core of the release is real measurement: Inkwell now reads
your own Google Search Console data into the Desk, keeps a revision history you can compare and
restore, and runs its own nightly housekeeping instead of asking you to wire up cron. Alongside that
committed scope, a UI/UX audit of the whole Desk and the reader site turned up a long list of real
defects — native browser popups, no phone navigation, no dark scheme, unlabeled controls, a stray
schedule-time bug — and this release fixes all of it. It is the largest release yet by change count,
and every part of it is backward-compatible: a tenant who opens nothing new keeps working exactly as
before.

## Highlights

### 📊 Search Console, wired into the Desk

Connect your own Google Search Console property with a service-account key — no platform-owned
OAuth, no third party in the middle — and a nightly job pulls clicks, impressions, CTR and position
straight into the product.

- The post editor gains a **Search performance** panel: 28-day clicks/impressions/CTR/position with
  the change against the previous 28 days, the queries actually bringing traffic, and a plain flag
  when a post is in striking distance (position 8–20 with real demand), losing impressions, or
  ranking on page one with almost no clicks.
- **Content Health** gets the same three flags as filter tiles; the **dashboard** shows 28-day
  clicks, impressions and the striking-distance count.
- The key is Data-Protection-encrypted, never logged, audited or exported; the job is fail-open per
  property and reports its result on the dashboard. Summaries are cached five minutes per site.

### 🗂️ Revision history for posts and pages

Every save with changed content now writes a revision, tagged with why (Created, Saved, Published,
Scheduled, Restored) and by whom. The editor sidebar lists them; **Compare** shows a line-level diff
against the current version; **Restore** puts the content back after recording the current state as
a revision first, so a restore is itself undoable. Slug and publish state are never touched by a
restore. Kept per item is configurable (default 25); restores are audited and tenant-scoped.

### 🔐 Self-service password reset and a structural comment-spam gate

- **"Forgot your password?"** on the sign-in page issues a single-use, 30-minute link; only its hash
  is stored, the response is identical for a known or unknown address, requests are capped at three
  an hour, and a site without SMTP configured says so rather than pretending to send.
- **Public comments now pass a spam gate before anything is stored**: a honeypot field, a signed
  form-timing token bound to the specific post, rejection of markup links and link-only comments,
  repeat-comment detection, and a per-address rate limit. No word list, no external service, and it
  fails open — no rule can turn a comment into a server error. Each discard is reported to the IP
  Firewall so a repeat offender blocks itself.

### ⏱️ A real job scheduler, no cron required

One in-process scheduler wakes every minute and runs due work in a fresh scope: AI-crawler visit
retention, expired password-reset token pruning, the Search Console sync, and Open Graph card
cleanup. Runs are claimed atomically against a ledger table, so two application instances sharing a
database can never run the same job twice; a job that fails is recorded and the next tick carries on.
Admin → Dashboard shows every job's last run, result and message.

### 🖥️ The Desk rebuilt on five shared primitives

A ground-up UX pass replaced sixteen native browser confirm/alert/prompt popups with one styled
dialog that states the consequence in a sentence; added an unsaved-changes guard to every editor;
added a busy indicator to every long-running action; added search and paging to every list that can
grow (Pages, Categories, Tags, Series, Users, Media, Import, Redirects, Link Audit, Content Health);
and gave the Desk and the reader site a genuine dark colour scheme. Settings is now seven tabs
instead of one long form. Bulk actions (publish, draft, tag, delete; approve, spam, delete) replace
one-row-at-a-time work in the Posts list and Comments queue, both with a typed guard above five items.

### ♿ An accessibility pass across the whole product

The Desk had no phone navigation below 768 px, several screens used the wrong password-field type
(defeating password managers), and dozens of icon-only controls and filter inputs had no name for a
screen reader. All of it is fixed: real `<input type="password">` fields with correct `autocomplete`,
inline announced errors instead of a five-second toast, a labelled and keyboard-operable mobile menu,
a skip-to-content link and visible focus rings on the public site, `aria-current` on active nav items,
and accessible names on every icon-only action across Media, Users, Theme, Comments, the editors, the
Dashboard, Audit Trail, Analytics, Security and AI Crawlers.

## Also in this release

**Added:** CMS pages now appear in `llms.txt`/`llms-full.txt`; a shared reading-time helper across
all three post layouts; a "Needs attention on this server" dashboard panel for a shadowed static
file or a Data Protection key ring that could not be persisted; generated invite passwords for new
Desk users, shown once and never emailed.

**Changed:** the Media library, Theme settings, Import wizard, and post/page editors were each
reworked for the same five primitives above; Open Graph cards are pruned after 30 days and discarded
on save/delete; account pages (sign in, register, forgot/reset password, first-run setup) share one
token stylesheet with the rest of the Desk; calmer copy throughout — no exclamation marks, no emoji,
no "Invalid X" phrasing; `robots.txt` now disallows `/search` and `/preview/`; landing-page fonts are
self-hosted (they had been loading from Google Fonts); the example configuration no longer suggests
SQLite, which the data layer has never actually supported.

## Fixed

The largest single group in this release. Selected items with real consequence:

- **A typed schedule time is now read in your display time zone** and converted to UTC on save — it
  had been bound straight to UTC, so a non-UTC operator's scheduled post went live hours off.
- **Every timestamp now runs in UTC end to end** — publish stamps, "is it live yet" checks and several
  entity timestamps had been written in the host's local time, so a freshly published post could
  answer 200 at its own URL while missing from the homepage, feed and sitemap until the clock caught
  up. A source-scanning test now fails the build on a regression; a one-time conversion script shifts
  existing rows once an operator supplies the host's historical offset.
- **The Desk had no phone navigation** — the sidebar was `display:none` below 768 px; it now slides in
  from a named, keyboard-reachable toggle.
- **Password fields on sign-in and register were plain text boxes** driven by a hand-rolled masking
  script, so password managers could neither fill nor save credentials. Replaced with real password
  inputs on every account page.
- **A scripted edit briefly shipped four Save buttons as raw text** on Settings and the Posts/Pages
  editors — restored, with a test that fails the build if it recurs.
- **Revisions are now tenant-scoped**, **comment-form tokens are bound to their post**, **the
  duplicate-comment check is scoped to the post's owner**, **Search Console rows are pruned on
  disconnect**, and **the scheduler can no longer run a job twice across two application instances**.
- CMS pages now carry their own meta title, description and canonical instead of a blank one, and now
  ping IndexNow on publish the way posts already did; Admin → AI Crawlers no longer runs a full table
  scan on a self-hosted install.
- Roughly twenty further UI defects: mis-encoded dashes, a white-on-dark button, a duplicated link
  attribute, non-scrolling Import tables, an autofocus that stole the 404 announcement, and more —
  see `CHANGELOG.md` for the complete, itemized list.

## Security

- **Comment spam protection** (described above under Highlights).
- **Media upload's anti-forgery check is restored** — it had been commented out; upload failures no
  longer return an exception stack trace to the browser.
- **Users screen:** an administrator can no longer disable their own account, and role changes require
  an explicit confirmed action rather than firing on every keystroke through a dropdown.

## Upgrade notes

- **Schema is fully automatic.** Every new table and index in this release (`Jobs`,
  `PasswordResetTokens`, `SearchPerformance`, `Revisions`, a unique index on revision numbers, an
  index on comment timestamps) is applied on startup by `MigrationService`. A fresh install gets the
  same schema from `DBScripts/init.sql` alone — verified by diffing `sys.columns` against a live
  database in both directions before this release was cut. No manual step is required for schema.
- **One manual step is required: the UTC conversion.** If your host's clock was ever running in a
  zone other than UTC, run `DBScripts/2026-09-15_convert-local-timestamps-to-utc.sql` and
  `DBScripts/2026-09-17_convert-remaining-local-timestamps-to-utc.sql` once each, with
  `@OffsetMinutes` set to that host's historical offset. Both are no-ops at offset 0 and guarded by a
  marker row against a second run.
- **Set `DataProtection:KeysPath`** to a folder outside the web root that survives a redeploy (default
  `App_Data/keys`). Without it, comment-form tokens and preview links are silently invalidated on
  every app-pool recycle; the dashboard now warns if the folder cannot be written.
- **New settings default off or to today's behaviour.** Colour scheme defaults to System; comment
  spam protection, the job scheduler and revision history are always on and need no configuration;
  Search Console and IndexNow stay inert until a key is entered.
- **Backward-compatible.** A tenant who never opens Settings, Theme, or any of the new screens sees no
  change in behaviour, layout or content.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
