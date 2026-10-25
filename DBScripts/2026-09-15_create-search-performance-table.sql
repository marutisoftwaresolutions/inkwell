-- DBScripts/2026-09-15_create-search-performance-table.sql
-- Change: New SearchPerformance table — daily Search Console rows (date × page × query with
--         clicks, impressions, CTR, position) pulled nightly by the "search-console-sync" job
--         for every owner with a connected property.
-- Why:    Until now the platform held no search performance data at all; every ranking
--         decision needed a hand-exported spreadsheet. The striking-distance panel in the
--         editor, the Search filters in Admin → Content Health and the dashboard tile all
--         read from this table.
-- Schema impact: CREATE TABLE SearchPerformance + 2 indexes. Idempotent — safe to run more than once.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs and DBScripts/init.sql (Fresh-Install Schema Rule).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SearchPerformance')
BEGIN
    CREATE TABLE SearchPerformance (
        Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId     UNIQUEIDENTIFIER NOT NULL,
        [Date]      DATE             NOT NULL,
        Page        NVARCHAR(1024)   NOT NULL,
        Query       NVARCHAR(512)    NOT NULL,
        Clicks      INT              NOT NULL,
        Impressions INT              NOT NULL,
        Ctr         FLOAT            NOT NULL,
        Position    FLOAT            NOT NULL
    );
    CREATE INDEX IX_SearchPerformance_Owner_Date      ON SearchPerformance (OwnerId, [Date]);
    CREATE INDEX IX_SearchPerformance_Owner_Page_Date ON SearchPerformance (OwnerId, Page, [Date]);
    PRINT '  [+] SearchPerformance table + indexes created.';
END
ELSE
    PRINT '  [=] SearchPerformance already exists — skipped.';
