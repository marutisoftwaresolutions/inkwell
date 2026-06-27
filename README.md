# Blogfront

A modern, open-source blog engine built with .NET 10 and C#. Designed for performance, simplicity, and editorial clarity, Blogfront empowers creators to manage their content without fighting complex configuration.

---

## Features

- **Clean & Fast** — Lightweight MVC architecture with Dapper for zero-ORM SQL performance.
- **Role-Based Access Control** — Granular permission system with Admin, Editor, and Author roles.
- **Rich Content Creation** — Draft, schedule, auto-save, and publish posts with a full WYSIWYG editor.
- **Theme & Layout System** — 10 Inkwell presets, Magazine/Grid/Minimal/Neutral layouts, and live CSS variable customization.
- **Analytics Dashboard** — Built-in page view tracking with UTM, referrer, geo-location (country/region), and visit source classification.
- **Audit Trail** — Immutable, admin-only log of every write action across the platform.
- **Newsletter & Subscribers** — Send newsletters, manage subscribers, import/export CSV.
- **Redirects Manager** — Source/destination redirect rules with hit-count tracking.
- **Members** — Member directory with label-based segmentation.
- **Media Library** — Secure upload, organized by year/month, paginated API for in-editor browsing.
- **SEO-Ready** — OG image generation, FAQ schema (JSON-LD), structured data, canonical URLs.
- **SMTP Email** — Built-in SMTP service for transactional email.
- **reCAPTCHA** — Google reCAPTCHA v2 support on public forms.
- **Multi-tenant Support** — Cloud-mode tenant isolation via `ITenantContext`.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server 2019 or later

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/MarutiSoftwareSolution/blog.git
   cd blog
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore Blog.slnx
   ```

3. **Configure the environment:**

   Copy `appsettings.example.json` to `appsettings.json` inside `Blog.Web/` and set your connection string:
   ```bash
   cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json
   ```

   Minimum required settings in `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=.;Database=BlogEngine;Trusted_Connection=True;"
     }
   }
   ```

4. **Run the application:**
   ```bash
   dotnet run --project Blog.Web
   ```

   On first launch, `MigrationService` automatically creates all tables and seeds default roles and permissions. Navigate to `/account/register` to claim the Admin account.

5. **Optional — run DB scripts manually:**

   All schema scripts live in `DBScripts/` and are idempotent (safe to re-run):
   ```powershell
   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\<script>.sql"
   ```

   > **Important:** Always include `-f 65001` (UTF-8 code page) when running scripts with `sqlcmd` to prevent character encoding corruption in NVARCHAR columns.

---

## Architecture Overview

```
Blog.Core/            Domain models, interfaces, AuditActions constants
Blog.Infrastructure/  Dapper repositories, MigrationService, seeders
Blog.Web/             ASP.NET Core 10 MVC — controllers, views, middleware, services
DBScripts/            Idempotent, dated SQL scripts (YYYY-MM-DD_description.sql)
```

### Key architectural decisions

| Concern | Approach |
|---|---|
| Data access | Dapper (raw SQL, no ORM overhead) |
| Authentication | Cookie-based, PBKDF2-SHA256 passwords (100k iterations) |
| Authorization | Policy-based + permission claims loaded from DB at login |
| Schema migrations | `MigrationService` runs `IF NOT EXISTS` guards on startup |
| Theme storage | `CustomThemeSettings` table — key/value CSS variable overrides |
| Page view tracking | Fire-and-forget middleware using root `IServiceProvider` scope |
| Audit logging | `AuditService` writes to `AuditLogs` on every successful write action |

---

## Core Flows

### 1. Authentication & Security

- **First Run**: No users → redirected to `/account/register`. First registrant becomes Admin and seeds RBAC.
- **Passwords**: PBKDF2 + SHA-256, 100,000 iterations, 16-byte random salt per user.
- **Sessions**: Secure HTTP-only cookie, 7-day sliding expiry, with granular permission claims.
- **Audit**: Login, logout, and login failures are all recorded in the Audit Trail.

### 2. Post Creation & Publishing

- Navigate to `/admin/posts/create` for the full editor.
- **Auto-save** via HTMX hits `/admin/posts/autosave/{id}` continuously in the background.
- Posts can be saved as **Draft**, **Published**, or **Scheduled** (auto-publishes at `ScheduledAt`).
- Users without `posts.publish` permission have submissions silently demoted to draft.
- Categories, tags, and FAQ blocks (JSON-LD schema) are all supported.
- Medical/optometry content: `LastVerifiedAt` and `NextReviewAt` fields support clinical review workflows.

### 3. Media Management

- Permitted MIME types enforced on upload; 10 MB size limit.
- Files stored in `/uploads/images/YYYY-MM/` with randomized names.
- In-editor browsing via paginated JSON API at `/admin/media/api`.
- OG images auto-generated at `/og-image/{slug}` for every post.

### 4. Theme & Layout Customization

- **Inkwell Design System**: 10 curated color presets (Warm Cream, Ocean Breeze, Midnight Blue, etc.).
- **Layout engine**: Magazine, Grid, Minimal, Neutral, Classic, and Modern layouts for index, navbar, and footer — independently configurable.
- All settings stored in `CustomThemeSettings`; the public frontend reads them via CSS variables (`--bg`, `--fg`, `--surface`, etc.).

### 5. Analytics

- `PageViewMiddleware` captures every public page hit — path, referrer, UTM parameters, IP hash, user agent, country, and region.
- Admin-only dashboard at `/admin/analytics` shows daily/weekly trends, top pages, top referrers, and traffic source breakdown.
- Authenticated admin requests are never counted.

### 6. Audit Trail

- Every create, update, delete, publish, login, and settings change is written to `AuditLogs`.
- Accessible only to Admin-role users at `/admin/audit`.
- Read-only — no delete or truncate endpoint is ever exposed.
- Filterable by entity type, action, user, and date range. CSV export supported.
- Before/after JSON snapshots stored for settings and role changes.

### 7. Newsletter & Subscribers

- Compose and send newsletters from `/admin/newsletter`.
- Subscriber list management at `/admin/subscribers` with CSV import/export.
- Members can be segmented with labels at `/admin/members`.

### 8. Redirects

- Rule-based redirects managed at `/admin/redirects`.
- Hit counts tracked per rule; 301/302 selectable.

---

## Database Scripts

All schema changes follow a mandatory naming convention:

```
DBScripts/YYYY-MM-DD_short-description.sql
```

Scripts are idempotent and include `IF NOT EXISTS` guards. `MigrationService` applies all required schema on app startup so a fresh install needs no manual SQL step. The `DBScripts/` folder is the authoritative history of all schema changes.

---

## Development Notes

- Run with `dotnet watch run --project Blog.Web` for hot-reload during development.
- The `OptometryTaxonomySeeder` seeds 200+ optometry-specific categories and tags for domain-specific blogs.
- reCAPTCHA keys are configured in `appsettings.json` under `ReCaptcha:SiteKey` and `ReCaptcha:SecretKey`.
- SMTP settings live under `Smtp:Host`, `Smtp:Port`, `Smtp:Username`, `Smtp:Password`.

---

## License

MIT License — see [LICENSE](LICENSE) for details.
