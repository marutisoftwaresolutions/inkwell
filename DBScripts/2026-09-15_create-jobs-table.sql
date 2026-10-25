-- DBScripts/2026-09-15_create-jobs-table.sql
-- Change: New Jobs table — one row per scheduled background job, recording when it last
--         started/finished, whether it succeeded, and its one-line report.
-- Why:    Until now nothing in the platform ran on a schedule (scheduled posts are resolved on
--         read). The in-process job runner introduced in v1.0.6 needs a durable record so the
--         operator can see on Admin → Dashboard that nightly work (crawler-visit retention,
--         expired reset-token pruning, later the Search Console pull) is actually happening,
--         and so a job's cadence survives an app-pool recycle.
-- Schema impact: CREATE TABLE Jobs. Idempotent — safe to run more than once.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs and DBScripts/init.sql (Fresh-Install Schema Rule).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Jobs')
BEGIN
    CREATE TABLE Jobs (
        Name           NVARCHAR(100)  NOT NULL PRIMARY KEY,
        LastStartedAt  DATETIME2      NULL,
        LastFinishedAt DATETIME2      NULL,
        LastSucceeded  BIT            NOT NULL CONSTRAINT DF_Jobs_LastSucceeded DEFAULT 0,
        LastMessage    NVARCHAR(1000) NULL,
        RunCount       INT            NOT NULL CONSTRAINT DF_Jobs_RunCount DEFAULT 0
    );
    PRINT '  [+] Jobs table created.';
END
ELSE
    PRINT '  [=] Jobs already exists — skipped.';
