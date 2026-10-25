-- DBScripts/2026-10-22_create-pageviewdaily-table.sql
-- Change : new table PageViewDaily — one row per owner x day x report section value: the day's total
--          (Dimension 0), each page (1), source + medium (2), referrer (3), campaign (4), country (5) and
--          region (6), with the number of content-page views (ADR-0037).
-- Why    : Admin → Analytics read every raw view in the period on each load (6–8 s for 90 days on a
--          3M-row copy). The page-view-rollup job (dbo.usp_PageViews_RollUp) now summarises each finished
--          day once; the report reads this table for past days and the raw table only for later days.
--          It also keeps the report's full history once raw views are pruned (PageViewRetentionDays).
-- Data   : the long-range Analytics history, so it is backed up with the analytics tables (Optional), and
--          it is not pruned by PageViewRetentionDays.
-- Safety : IF NOT EXISTS — idempotent; creating an empty table takes no lock on PageViews.
-- Also in: MigrationService.cs and init.sql § 41.
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -i "DBScripts\2026-10-22_create-pageviewdaily-table.sql"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PageViewDaily')
BEGIN
    CREATE TABLE PageViewDaily (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId   UNIQUEIDENTIFIER NOT NULL,
        [Day]     DATE             NOT NULL,
        Dimension TINYINT          NOT NULL,
        Value1    NVARCHAR(1000)   NULL,
        Value2    NVARCHAR(100)    NULL,
        Views     INT              NOT NULL
    );
    CREATE INDEX IX_PageViewDaily_Owner_Dimension_Day ON PageViewDaily (OwnerId, Dimension, [Day] DESC)
        INCLUDE (Value1, Value2, Views);
    CREATE INDEX IX_PageViewDaily_Day ON PageViewDaily ([Day] DESC);
    PRINT '  [+] PageViewDaily table + indexes created.';
END
ELSE
    PRINT '  [=] PageViewDaily already exists — skipped.';
