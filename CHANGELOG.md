# Changelog

All notable changes to Blogfront are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.0.1] — 2026-06-27

### Added

#### Analytics Dashboard
- `PageViewMiddleware` — fire-and-forget page view tracking using root `IServiceProvider` scope (never blocked by request lifetime)
- `PageViews` table with UTM parameters (`source`, `medium`, `campaign`, `term`, `content`), IP hash, user agent, country, and region columns
- `VisitSourceClassifier` — classifies traffic into Organic, Direct, Social, Email, Paid, and Referral buckets
- `AnalyticsController` — admin-only dashboard at `/admin/analytics` with daily/weekly trends, top pages, top referrers, and traffic source breakdown
- `AnalyticsDashboardViewModel` — structured view model for the analytics page
- Geo-location resolution (country + region) from IP address

#### Audit Trail
- `AuditLogs` table with full schema: action, entity type/id/name, before/after JSON snapshots, user info, IP address
- `IAuditRepository` + `AuditRepository` — Dapper implementation with paginated, filterable queries
- `AuditService` — thin service that resolves request context (owner, user, IP, UA) and logs asynchronously; swallows all exceptions so audit never crashes the caller
- `AuditActions` static constants class — 33 `EntityType.Verb` string constants (e.g. `Post.Published`, `Comment.Deleted`, `Auth.LoggedIn`)
- `AuditController` — admin-only, read-only at `/admin/audit`; supports filter by entity type, action, user, date range; CSV export up to 10,000 rows
- Audit Trail view — color-coded action badges, expandable before/after JSON diff rows, pagination
- Audit nav link in `_AdminLayout.cshtml` (Admin-role gated)
- **All 12 admin controllers wired**: PostsController, CommentsController, CategoriesController, TagsController, MediaController, PagesController, UsersController, AccountController, SettingsController, ThemeSettingsController, NewsletterController, SubscribersController

#### Members & Newsletter
- `Members` table and `MemberRepository` — member directory with label-based segmentation
- `MemberLabels` and `Labels` tables for subscriber segmentation
- `Newsletters`, `Emails`, `MemberNewsletters` tables for newsletter campaign tracking
- `NewsletterController` and `SubscribersController` — compose/send newsletters, manage subscribers, CSV import/export
- `SmtpEmailService` — built-in SMTP transactional email

#### Redirects
- `Redirects` table with source path, destination URL, HTTP status code, and hit count
- `RedirectRepository` and `RedirectsController` — full CRUD with rule-based redirect middleware

#### SEO & Content
- `OgImageController` — programmatic OG image generation at `/og-image/{slug}`
- `FaqJson` column on `Posts` — JSON-LD FAQ schema stored per post
- `LastVerifiedAt` / `NextReviewAt` columns on `Posts` — clinical review workflow for medical content
- `OptometryTaxonomySeeder` — seeds 200+ optometry-specific categories and tags
- `LandingController` — dedicated landing page route

#### User Profiles
- `Credentials`, `Specialty`, `LicenseNumber` columns added to `Users` — professional author credentials
- `AvatarController` — dedicated avatar upload/management endpoint
- Profile update now refreshes cookie claims immediately

#### Media
- `OriginalFileName` column on `Media` table — tracks the uploaded filename before randomization

#### Theme System (Inkwell)
- 10 curated Inkwell color presets seeded via `SeedDefaultsAsync`
- Magazine, Grid, Minimal, Neutral, Classic, and Modern layout variants
- `CustomThemeSettings` table — per-owner key/value CSS variable overrides
- `ThemeSettingsController` — preset application, layout presets, reset; all changes audited
- Theme backward-compatibility guards in all ViewComponents (unknown values fall back to safe defaults)

#### Security
- `ReCaptchaService` — Google reCAPTCHA v2 validation on public forms
- `IEmailService` / `IRedirectRepository` interfaces added to `Blog.Core`

### Fixed

- **PageView fire-and-forget scope bug** — `TrackAsync` previously used `context.RequestServices.CreateScope()` which was disposed before the async task ran, silently swallowing all view recordings. Fixed by pre-capturing all HTTP context values synchronously and switching to `_rootServices.CreateScope()`.
- **Admin page views counted** — Admin requests were being tracked; `PageViewMiddleware` now checks `context.User.Identity.IsAuthenticated` and skips authenticated users entirely.
- `BlogController` operator precedence — `!User.Identity?.IsAuthenticated == true` replaced with the clearer `User.Identity?.IsAuthenticated != true`.

### Changed

- Upgraded target framework from `.NET 8` to `.NET 10`
- `MigrationService` expanded to auto-create: `CustomThemeSettings`, `Members`, `Labels`, `MemberLabels`, `Newsletters`, `Emails`, `MemberNewsletters`, `Redirects`, `Snippets`, `PageViews`, `AuditLogs`, and all associated indexes and column additions
- `InfrastructureServiceExtensions` registers all new repositories (`IAuditRepository`, `IMemberRepository`, `IPageViewRepository`, `IRedirectRepository`)
- `Program.cs` registers `AuditService`, `SmtpEmailService`, `ReCaptchaService`, and `IHttpContextAccessor`
- README rewritten to reflect current stack (.NET 10, Dapper, no EF Core) and all new features

### Database Scripts (DBScripts/)

| Script | Change |
|---|---|
| `2026-05-17_create-pageviews-table.sql` | PageViews table |
| `2026-05-22_add-country-region-to-pageviews.sql` | Country + Region columns + index |
| `2026-05-22_add-verified-review-dates-to-posts.sql` | LastVerifiedAt + NextReviewAt columns |
| `2026-05-23_add-inkwell-preset-theme-setting.sql` | No schema — seeded at runtime |
| `2026-05-24_create-missing-tables-live.sql` | Backfill for live environments missing tables |
| `2026-05-24_reset-pageviews-fresh-start.sql` | One-time data reset (manual run only) |
| `2026-05-29_create-audit-logs-table.sql` | AuditLogs table + 3 indexes |

---

## [1.0.0] — 2026-05-15

### Added

- Initial release of the Blogfront platform
- ASP.NET Core MVC blog engine with Dapper data access
- Cookie-based authentication with PBKDF2-SHA256 password hashing
- Role-Based Access Control (Admin, Editor, Author)
- Post creation, editing, scheduling, and publishing
- Media library with secure upload and organized storage
- Categories and Tags management
- Comments with moderation
- Basic theme customization
- Multi-tenant `ITenantContext` support (cloud mode)
- SQL Server schema auto-migration on startup
