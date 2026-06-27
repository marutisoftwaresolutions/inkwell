# Inkwell

**A free, open-source, self-hosted blogging platform built on .NET.**
Multi-tenant by default, themeable, and crafted for writers who care about
typography, content ownership, and a calm editorial experience.

🌐 [useinkwell.app](https://www.useinkwell.app) · 📄 MIT licensed · 🔒 No telemetry, no cloud account

---

## Why Inkwell

- **Self-hosted & private** — your content, your server, your data. No third-party calls; you are the sole data controller.
- **Multi-tenant** — one install serves many blogs, each with its own layout and color preset.
- **.NET 10** — single ASP.NET Core binary. No Node, no PHP, no plugin marketplace.
- **Themeable** — 10 Inkwell color presets × multiple layouts (Magazine, Grid, Minimal, Neutral, Classic, Modern), plus a clean custom-theme API.
- **Analytics built-in** — page view tracking, UTM attribution, geo-location, and traffic source breakdown. No Google Analytics required.
- **Audit Trail** — immutable, admin-only log of every write action across the platform.
- **Your database** — SQL Server or SQL Server LocalDB. Schema auto-applies on startup via `MigrationService`.

---

## Quick Start

```bash
git clone https://github.com/marutisoftwaresolutions/inkwell
cd inkwell
cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json
# Edit appsettings.json — set your connection string
dotnet run --project Blog.Web
```

Navigate to `/account/register` to claim the Admin account on first launch.

Full docs: [useinkwell.app/docs](https://www.useinkwell.app/docs)

---

## Features

- **Clean & Fast** — Lightweight MVC architecture with Dapper for zero-ORM SQL performance.
- **Role-Based Access Control** — Granular permission system with Admin, Editor, and Author roles.
- **Rich Content Creation** — Draft, schedule, auto-save, and publish posts with a full WYSIWYG editor.
- **Theme & Layout System** — 10 Inkwell presets, 6+ layouts, and live CSS variable customization from the admin panel.
- **Analytics Dashboard** — Built-in page view tracking with UTM, referrer, geo-location (country/region), and visit source classification.
- **Audit Trail** — Immutable, admin-only log of every write action. Filterable and CSV-exportable.
- **Newsletter & Subscribers** — Compose and send newsletters, manage subscribers, import/export CSV.
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

   Copy `appsettings.example.json` to `appsettings.json` inside `Blog.Web/` and set your connection string:
   ```bash
   cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json
   ```

   Minimum required settings in `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=.;Database=inkwell;Trusted_Connection=True;"
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
   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\init.sql"
   ```

   > Always include `-f 65001` (UTF-8 code page) to prevent character encoding corruption in NVARCHAR columns.

---

## Architecture

```
Blog.Core/            Domain models, interfaces, AuditActions constants
Blog.Infrastructure/  Dapper repositories, MigrationService, seeders
Blog.Web/             ASP.NET Core 10 MVC — controllers, views, middleware, services
DBScripts/            Idempotent, dated SQL scripts (YYYY-MM-DD_description.sql)
```

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

## Database Scripts

All schema changes follow a mandatory naming convention:

```
DBScripts/YYYY-MM-DD_short-description.sql
```

Scripts are idempotent and include `IF NOT EXISTS` guards. `MigrationService` applies all required schema on app startup — a fresh install needs no manual SQL step. The `DBScripts/` folder is the authoritative history of all schema changes.

---

## Development

```bash
dotnet watch run --project Blog.Web   # hot-reload during development
```

Key configuration in `appsettings.json`:

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `ReCaptcha:SiteKey` / `SecretKey` | Google reCAPTCHA v2 keys |
| `Smtp:Host/Port/Username/Password` | Transactional email |

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

*Built with care by [Maruti Software Solutions](https://marutisoftwaresolutions.com)*
