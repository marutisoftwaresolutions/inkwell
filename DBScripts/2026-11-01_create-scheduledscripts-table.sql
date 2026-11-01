-- DBScripts/2026-11-01_create-scheduledscripts-table.sql
-- Change : new table ScheduledScripts — one row per SQL script file the operator scheduled from
--          Admin → Scheduled scripts (hourly / daily / weekly, UTC), with the last run's outcome and
--          captured output. Install-wide (no owner): database housekeeping, like backups.
-- Why    : operators on panel-hosted servers have no Task Scheduler and no sqlcmd, so recurring
--          maintenance (a demo reset, a retention purge, a report refresh) had nowhere to run. The
--          in-process JobScheduler already ticks every minute; this table tells it what to run and when.
--          Script text is never stored: rows point at files in the configured scripts folder
--          (ScheduledScripts:Folder, default App_Data/jobs), which only the server operator can write.
-- Safety : IF NOT EXISTS — idempotent; the table starts empty, nothing runs until a row is enabled.
-- Also in: MigrationService.cs and init.sql § 43.
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -i "DBScripts\2026-11-01_create-scheduledscripts-table.sql"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ScheduledScripts')
BEGIN
    CREATE TABLE ScheduledScripts (
        Id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Name           NVARCHAR(100)    NOT NULL,
        ScriptFile     NVARCHAR(260)    NOT NULL,
        Frequency      NVARCHAR(10)     NOT NULL,              -- hourly | daily | weekly
        DayOfWeek      TINYINT          NULL,                  -- 0 = Sunday, weekly only
        TimeOfDayUtc   TIME(0)          NOT NULL,
        Enabled        BIT              NOT NULL DEFAULT 1,
        Source         NVARCHAR(10)     NOT NULL DEFAULT 'desk', -- desk | config (config rows are read-only in the Desk)
        NextRunAt      DATETIME2        NULL,
        LastRunAt      DATETIME2        NULL,
        LastSucceeded  BIT              NULL,
        LastOutput     NVARCHAR(MAX)    NULL,
        LastDurationMs INT              NULL,
        CreatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_ScheduledScripts_Name UNIQUE (Name)
    );
    CREATE INDEX IX_ScheduledScripts_Due ON ScheduledScripts (Enabled, NextRunAt);
    PRINT '  [+] ScheduledScripts table created.';
END
ELSE
    PRINT '  [=] ScheduledScripts already exists — skipped.';
