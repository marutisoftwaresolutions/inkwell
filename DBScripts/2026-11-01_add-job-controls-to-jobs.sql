-- DBScripts/2026-11-01_add-job-controls-to-jobs.sql
-- Change : two columns on the Jobs ledger — Paused (BIT, default 0) and IntervalOverrideMinutes (INT NULL) —
--          so an Admin can pause/resume a built-in job and change its cadence from Admin → Dashboard
--          without a redeploy (ADR-0040). A paused job is never claimed; an override replaces the
--          interval declared in code (minimum one minute).
-- Why    : the dashboard listed the built-in jobs read-only; stopping one (backups on a full disk, a
--          rollup during an incident) or running it now needed a code change or a restart.
-- Safety : IF COL_LENGTH — idempotent; defaults keep every existing row running exactly as before.
-- Also in: MigrationService.cs and init.sql § 37 (column top-ups).
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -i "DBScripts\2026-11-01_add-job-controls-to-jobs.sql"

SET NOCOUNT ON;

IF COL_LENGTH('Jobs', 'Paused') IS NULL
BEGIN
    ALTER TABLE Jobs ADD Paused BIT NOT NULL CONSTRAINT DF_Jobs_Paused DEFAULT 0;
    PRINT '  [+] Jobs.Paused added.';
END
ELSE PRINT '  [=] Jobs.Paused already exists — skipped.';

IF COL_LENGTH('Jobs', 'IntervalOverrideMinutes') IS NULL
BEGIN
    ALTER TABLE Jobs ADD IntervalOverrideMinutes INT NULL;
    PRINT '  [+] Jobs.IntervalOverrideMinutes added.';
END
ELSE PRINT '  [=] Jobs.IntervalOverrideMinutes already exists — skipped.';
