# Inkwell

Free, open-source, self-hosted blogging platform built on .NET 10 and ASP.NET Core MVC.
No ORM, no cloud account, no telemetry. Dapper-backed, multi-tenant, and designed for writers.

🌐 [useinkwell.app](https://www.useinkwell.app) · 📄 MIT licensed · 🔒 No telemetry, no cloud account

---

## Why Inkwell

- **Self-hosted & private** — your content, your server, your data. You are the sole data controller.
- **Zero-ORM performance** — Dapper with raw SQL. No Entity Framework, no lazy-load surprises.
- **Multi-tenant** — one binary serves many blogs. Cloud mode isolates tenants by URL slug; self-hosted mode runs a single-owner install.
- **Themeable** — 10 Inkwell color presets × 6 layouts (Magazine, Grid, Minimal, Neutral, Classic, Modern) with live CSS variable customization from the admin panel.
- **Analytics built-in** — page view tracking, UTM attribution, geo-location (country/region via ip-api.com), traffic source classification, and Chart.js dashboards. No third-party tracker required.
- **Audit Trail** — immutable, admin-only log of every write action across the platform. Filterable and Excel-exportable.
- **Your database** — SQL Server 2019+, SQL Server LocalDB, or SQLite. Schema auto-applies on startup via `MigrationService`; no manual migration step needed.

---

## Quick Start

```bash
git clone https://github.com/marutisoftwaresolutions/inkwell
cd inkwell
cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json
# Edit appsettings.json — set your ConnectionStrings:DefaultConnection
dotnet run --project Blog.Web
```

Navigate to `/account/register` to claim the Admin account on first launch.

Full docs: [useinkwell.app/docs](https://www.useinkwell.app/docs)

---

## Features

- **Quill WYSIWYG Editor** — rich-text post editing with image upload, code blocks, dividers, and custom button blots. Auto-save drafts.
- **Role-Based Access Control** — Admin, Editor, and Author roles with 10 granular permission claims (`posts.edit`, `posts.publish`, `posts.delete`, `pages.manage`, `comments.manage`, `categories.manage`, `tags.manage`, `media.manage`, `settings.manage`, `themes.manage`).
- **Theme & Layout System** — 10 Inkwell presets, 6+ layout variants, and a `CustomThemeSettings` key/value store for CSS variable overrides.
- **Analytics Dashboard** — page view tracking with UTM params, referrer, visit source, country/region. Bot detection filters 100+ known bot signatures. Chart.js visualizations.
- **Audit Trail** — every admin write action logged with user, IP, and timestamp. Read-only from the UI; exportable as Excel via ClosedXML.
- **Newsletter & Subscribers** — compose and send newsletters, manage subscribers, import/export CSV.
- **Redirects Manager** — source/destination redirect rules with per-rule hit-count tracking.
- **Members** — member directory with label-based segmentation.
- **Media Library** — secure upload organized by year/month, paginated API for in-editor browsing, automatic image processing via SixLabors.ImageSharp.Web.
- **SEO-Ready** — OG image generation, FAQ schema (JSON-LD), structured data, canonical URLs, content freshness dates (`LastVerifiedAt` / `NextReviewAt`).
- **Post Scheduling** — schedule posts for future publish with `ScheduledAt`.
- **SMTP Email** — transactional email via `System.Net.Mail`; no dependency on third-party email SDKs.
- **reCAPTCHA** — optional Google reCAPTCHA v2 on public forms. Skipped gracefully when not configured.
- **Page Revisions** — revision history for posts and pages.

---

## Tech Stack

### Backend

| Layer | Technology |
|---|---|
| Runtime | .NET 10 / ASP.NET Core MVC |
| Data access | Dapper 2.1.66 (raw SQL, no ORM) |
| Database | SQL Server 2019+ · SQL Server LocalDB · SQLite |
| DB driver | Microsoft.Data.SqlClient 5.2.2 · Microsoft.Data.Sqlite 10.0.3 |
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
| CSS framework | Tailwind CSS (local) · Bootstrap 5 |
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
| Geo-IP | ip-api.com free tier (45 req/min); results cached 24 hours |
| Image uploads | Organized by `year/month` under `wwwroot/uploads/` |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- One of: SQL Server 2019+, SQL Server LocalDB, or SQLite

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

   // SQLite
   "DefaultConnection": "Data Source=inkwell.db"
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
- **File upload safety** — MIME type validation; files organized under `wwwroot/uploads/year/month/`.
- **IP anonymization** — page view IPs are SHA-256 hashed with a per-install salt before storage.

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

*Built with care by [Maruti Software Solutions](https://marutisoftwaresolutions.com)*
