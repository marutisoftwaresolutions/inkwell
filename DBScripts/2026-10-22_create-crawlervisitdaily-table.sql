-- DBScripts/2026-10-22_create-crawlervisitdaily-table.sql
-- Change : new table CrawlerVisitDaily — one row per owner x day x crawler x path x identity-check result,
--          with the visit count and the last visit time (ADR-0037).
-- Why    : Admin → AI Crawlers aggregated millions of raw CrawlerVisits rows on every load. Even through a
--          covering index and one stored procedure that took 11–22 s for a 30-day window on a 5M-row copy.
--          The crawler-visit-rollup job (dbo.usp_CrawlerVisits_RollUp) now summarises each finished day once;
--          the report reads this table for past days and the raw table only for days not yet rolled up.
-- Data   : derived, never backed up (rebuilt by the job from CrawlerVisits, oldest day first, up to 31 days
--          per run). Pruned with the same retention window as CrawlerVisits.
-- Safety : IF NOT EXISTS — idempotent; creating an empty table takes no lock on CrawlerVisits.
-- Also in: MigrationService.cs and init.sql § 40.
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -i "DBScripts\2026-10-22_create-crawlervisitdaily-table.sql"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CrawlerVisitDaily')
BEGIN
    CREATE TABLE CrawlerVisitDaily (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId   UNIQUEIDENTIFIER NOT NULL,
        [Day]     DATE             NOT NULL,
        Crawler   NVARCHAR(60)     NOT NULL,
        Operator  NVARCHAR(60)     NOT NULL,
        IsAi      BIT              NOT NULL,
        Path      NVARCHAR(1024)   NOT NULL,
        Verified  BIT              NULL,
        Visits    INT              NOT NULL,
        LastSeen  DATETIME2        NOT NULL
    );
    CREATE INDEX IX_CrawlerVisitDaily_Day ON CrawlerVisitDaily ([Day] DESC)
        INCLUDE (OwnerId, Crawler, Operator, IsAi, Path, Verified, Visits, LastSeen);
    PRINT '  [+] CrawlerVisitDaily table + index created.';
END
ELSE
    PRINT '  [=] CrawlerVisitDaily already exists — skipped.';
