# ADR-0019: Whole-site backups as a ZIP of JSON Lines, restored only into a fresh install

- **Status:** Accepted (2026-09-27)
- **Date:** 2026-09-27
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Backup / restore"; CHANGELOG `[Unreleased]`

## Context

A self-hoster had no way to move or recover an Inkwell site except a SQL Server backup plus a copy
of `wwwroot/uploads`. That needs database-server access, ties the copy to one SQL Server version,
and silently carries server-local state: the job ledger, error logs, password-reset tokens and a
webhook delivery log that would re-send events. Restore is the hard half:
- every primary key is a GUID or an identity value that other rows point at;
- a fresh install already holds rows (schema defaults, and the default taxonomy on some installs);
- secrets encrypted with the old server's Data Protection key ring cannot be read on the new one.

## Options considered

1. **Native `BACKUP DATABASE` / `.bacpac`.** Complete, but it needs server permissions that a
   hosted SQL database often does not grant. It is version-bound, carries every log table, and does
   not include media.
2. **The content exporter's formats (WXR / Ghost).** Built for other platforms, so they lose users,
   settings, menus, redirects, subscribers and Inkwell-only blocks by design.
3. **An Inkwell format:** a ZIP with `manifest.json`, one `data/{Table}.jsonl` per table (column name
   to value, keys kept), and `media/uploads/**`. Written by the app, restored by the app.

## Decision

Option 3.

**What goes in.** `BackupFormat.Tables` classifies every table:
- **Content:** always backed up. People and roles, settings, theme, menus, redirects, snippets,
  webhooks, firewall rules, posts and pages with their taxonomy, series, media rows, revisions,
  comments, subscribers and newsletters, and the audit trail.
- **Optional:** page views, only on request, because they can run to millions of rows.
- **Excluded:** never backed up, because they are derived, server-local, re-fetchable or
  secret-bearing:
  - crawler visits, error logs and the job ledger;
  - password-reset tokens;
  - Search Console rows;
  - the webhook delivery log;
  - import staging.

A test fails when `init.sql` creates a table that has no classification.

**Values** are encoded by `BackupFormat.WriteValue`/`ReadValue`:
- GUIDs and dates as invariant strings, with full `datetime2` precision and no invented time zone;
- numbers as JSON numbers;
- bytes as base64.

A round-trip test covers each type.

**Making a backup** runs in the job runner, never in a request. Admin → Backups writes a request
marker; `BackupJob` (one-minute interval) takes it, writes the ZIP as `.partial`, and renames it only
when complete. It then keeps the newest `Backups:Keep` (default 10) and audits `Backup.Created` on
behalf of the admin. Backups live outside the web root (`Backups:Path`, default `App_Data/backups`).
Download, delete and restore accept only names matching the generated pattern, so no request can name
another path.

**Restoring** is only possible from the setup screens, only while the install has no users and no
settings, and only from a file already in the server's backups folder. Copying a file there needs
server access, so an anonymous visitor can never supply the data. `BackupDataStore.RestoreAsync`
does everything in one transaction:
- takes an application lock;
- re-checks that the install is empty;
- disables constraints on every table a backup can carry, then deletes their rows;
- bulk-loads each table with `SqlBulkCopy` (`KeepIdentity`, `KeepNulls`), mapping only columns that
  exist on both sides;
- re-validates constraints and reseeds identities.

Any failure rolls the database back to empty. Media is extracted after the commit, guarded against
paths that escape the uploads folder. A file that fails to write is reported, not fatal.

**Secrets.** Password hashes travel, so users sign in with their old passwords, and the file is
treated as a secret (Admin-only screen, every download audited). The Data Protection key ring does
**not** travel. The Search Console credential and webhook signing secrets must be entered again, and
the restore screen and README say so. Both already fail soft when they cannot be decrypted.

**Cloud mode.** In cloud mode the Backups screen answers 404. A backup is install-wide, and one
tenant's admin must never download another tenant's data.

**Found while building this.** The setup wizard counted an install as set up only once a settings
row existed. The first admin can also be created at `/account/register`, which writes no settings
row, so the wizard stayed open to anonymous callers. It now closes when either a user or a settings
row exists.

## Consequences

- Easier:
  - a site moves between servers, or between SQL Server editions, with one file and no
    database-server rights;
  - schema drift between versions is tolerated: a column the backup lacks keeps its default, and a
    column the schema dropped is skipped and reported.
- Harder:
  - a large backup occupies the single scheduler while it runs, so other jobs wait for the next tick;
  - restore is all-or-nothing into an empty install, with no merge and no partial restore;
  - there is no scheduled automatic backup yet (roadmap Plan 9.5).
- Now maintained: `BackupRestoreTests`:
  - catalog completeness against `init.sql`;
  - value round-trips;
  - name, retention and zip-slip guards;
  - manifest checks;
  - source scans for the policy, audit and fresh-install guard;
  - the end-to-end test: `init.sql` into a new empty database, app start-up, backup of the dev site,
    restore, per-table row count and checksum equality, byte-identical media, a refused second
    restore, and the site rendering from the restored database.

## Proof

- Code: `Blog.Core/Services/BackupFormat.cs`, `Blog.Infrastructure/Data/BackupDataStore.cs`,
  `Blog.Web/Services/Backups/BackupService.cs`, `Blog.Web/Services/Jobs/BackupJob.cs`,
  `Blog.Web/Controllers/BackupsController.cs`, `SetupController.Restore`,
  `Views/Backups/{Index,_BackupList}.cshtml`, `Views/Setup/{Restore,Restored}.cshtml`
- Schema: none (no new table or column).
- Tests: `Blog.Tests/BackupRestoreTests.cs`
