# Blog-Engine — Claude Project Rules

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
