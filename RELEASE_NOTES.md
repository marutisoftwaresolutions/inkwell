# Inkwell v1.1.0

**Release date:** 2026-11-22
**Type:** Feature release (schema additions apply on startup; existing sites keep their layout, theme, content and settings; three behaviours change on upgrade, see Upgrade notes)

**Build your website.** Inkwell was a complete blog. v1.1.0 makes it a complete website with a blog:
section blocks for marketing pages, a starter site chosen at install, API keys and an MCP endpoint so
scripts and AI assistants can work with your own permissions. It also repairs a new user's first ten
minutes, and closes several security faults. **If other people have accounts on your site, upgrade:**
in every earlier release an Author could create an Admin account by sending the Users form by hand.

## Highlights

### 🧱 Build a website, not only a blog

- **Section blocks for pages.** Eight new blocks in the page builder: columns (2 to 4), feature grid,
  call to action, testimonials, pricing table, questions and answers, contact section and logo strip.
  They take their colours from the theme, so they follow every layout, colour preset and the dark scheme.
- **Starter sites in the setup wizard.** Start empty, or with a Blog, a Business site with a blog, a
  Portfolio or a Newsletter. A starter sets a layout and preset and adds a few pages whose text is
  marked as placeholder. It never overwrites a page or a theme you already have.
- **Your own colours on the public site.** Theme → Colours has a new choice, "Reader colours come
  from". Until now the colour pickers saved without changing anything a reader could see.
- **Nested categories, and descriptions for categories and tags.** A parent category lists its
  subcategories' posts; a description shows above the list and becomes the page's meta description.
- **Custom code and CSS**, a **Contact button**, **Share links under posts** and **Maintenance mode**.

### 🚀 A first run that works

- **First-run database setup.** On first start Inkwell creates the database when it does not exist and
  applies the complete schema when it is empty. An existing install is never touched.
- **A new install opens the setup wizard**, and the wizard now finishes: it used to end on an error and
  create no roles.
- **The Quick Start works as written** (`dotnet run --project Blog.Web`).
- **Tests and CI.** A `Build and test` workflow runs the whole suite on every push, with a database and
  without one; a fresh clone reports no failures.

### ✍️ Writing and editing

- **Trash for posts**, **Duplicate a post or page**, **Unlisted posts** and **Notes on a post**.
- **Draft recovery in the editors.** Unsaved work is kept in your browser every 15 seconds and offered
  back after a crash or an expired sign-in.
- **Callout, toggle and bookmark blocks.**
- **Crop and replace images**, replace any library file while keeping its address, and an
  **Unused files report**.
- **Site search finds more and ranks it.** Each word must appear, in any order; title matches come first.

### 🤖 API keys and AI assistants

- **API keys.** Profile → API keys: named, scoped (read, write drafts, publish, moderate comments),
  with an expiry. A key never exceeds its owner's permissions, is stored only as a hash, and is named
  in the audit trail.
- **MCP endpoint for AI assistants.** With Settings → AI assistants switched on, `/mcp` lets an
  assistant holding a key read content, create drafts and, with a key that may publish, publish. Off
  by default. No tool deletes, and none touches settings, users, themes or media.
- **Pages and subscribers over the API**, an **OpenAPI document** at `/api/v1/openapi.json`, and
  **Posts as Markdown** at each post's address with `.md` added.

### 📬 Newsletter and measurement

- **Welcome emails.** Up to three automatic emails after a reader confirms, sent only to people who
  confirm after you switch one on.
- **Newsletter deliverability.** Permanently refused addresses move to a Bounced state, and a sender
  check reads your domain's mail records.
- **Goals** in Analytics (page visits, link clicks, sign-ups, form submissions, with CSV download),
  **AI-assistant visits**, and **which pages bring subscribers**.

### 🛠️ Running the site

- **Scheduled scripts** and **job controls on the Dashboard**: pause, resume, re-time or run any
  built-in job without a redeploy.
- **Health endpoints** (`/health/live`, `/health/ready`), a runtime-version warning and a backup check.
- **Geo-IP lookup can be switched off** (`Analytics:GeoIpLookup`).

### 🔒 Security

- **Only an Admin can add accounts, change roles or disable accounts.** The server accepted these
  requests from any Author.
- **Uploads can no longer be served as a web page.** The stored extension always belongs to the
  checked type; profile photos are re-encoded; SVG uploads are sanitised; a file that only claims to
  be a picture, or declares an enormous one, is refused.
- **Security headers on every response**, with a content security policy in report-only mode by default.
- **A role change or password reset takes effect at once:** sessions end, and a reset revokes the
  account's API keys. Five wrong passwords in 15 minutes impose a wait.

## Upgrade notes

- **Schema changes apply on startup.** Four new tables (`ScheduledScripts`, `ApiKeys`, `PostNotes`,
  `GoalCounts`) and new columns on `Jobs`, `Users`, `Members`, `Posts`, `Categories` and `Tags`. To
  apply them by hand, run the dated `DBScripts/2026-11-*.sql` files in name order; each is safe to
  re-run. `DBScripts/init.sql` builds the complete schema on an empty database (verified before this
  release by a `sys.columns` diff in both directions).
- **Check Settings → Comments before upgrading a live site.** "Hold comments for approval" now takes
  effect. A site with the box unticked starts publishing comments that pass the spam filter without
  review; comments with a link are still held.
- **Deleting a post moves it to Trash**, in the Desk and over the API. Delete it again from Trash to
  remove it for good. An API key now needs the publish scope to delete.
- **Security headers are on by default.** The content security policy only reports. If something in
  front of Inkwell already sets these headers, or you embed the site in a frame on another domain, see
  `Security:Headers:*` in the README.
- **Review accounts and uploads if you have untrusted users.** Check Users and the Audit Trail for
  accounts or role changes you do not recognise, and `wwwroot/uploads/avatars` and the media folders
  for files that are not pictures or documents.
- **Public pages carry `<meta name="generator" content="Inkwell">`**, with no version. Set
  `Site:GeneratorTag` to `false` to leave it out.
- **New settings are off by default:** the MCP endpoint, maintenance mode, share links, the contact
  button, custom code, comment emails, welcome emails and the newsletter draft on publish.
- **Backward-compatible otherwise.** Layouts, presets and existing settings are unchanged.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
