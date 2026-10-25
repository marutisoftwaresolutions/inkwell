-- DBScripts/2026-10-22_add-covering-indexes-for-report-procedures.sql
-- Change : covering indexes for the two report procedures (ADR-0037), and two unused CrawlerVisits
--          indexes removed.
--   1. IX_PageViews_OwnerId_CreatedAt is dropped and re-created to INCLUDE the columns usp_Analytics_Report
--      reads (Path, Source, Medium, Referrer, Campaign, Country, Region). Same name and key, so the
--      dashboard's 7-day / 30-day queries keep using it.
--   2. IX_CrawlerVisits_VisitedAt (new): date-leading, so a self-hosted site — which reads every owner —
--      can seek the window. The only date index before led with OwnerId, so every AI Crawlers section
--      scanned the whole table.
--   3. IX_CrawlerVisits_Owner_Path and IX_CrawlerVisits_Owner_Crawler are dropped. No query seeks on
--      either, both cost a write on every crawler request, and Owner_Path's key can reach 2,064 bytes
--      (Path is NVARCHAR(1024)) against SQL Server's 1,700-byte limit — a path longer than 842 characters
--      failed the insert, and with it the whole batched flush.
--
-- READ BEFORE RUNNING ON PRODUCTION
--   * On a large table this is slow and, except on Enterprise / Azure SQL (built ONLINE automatically),
--     it blocks writes to that table until it finishes. Page views and crawler visits are buffered in
--     memory and flushed in batches that fail open, so a reader never sees an error; visits that arrive
--     during the build may not be recorded. Run it in a quiet period.
--   * Measured on spinning disk, SQL Server Express: 12 s for 3M page views, 31 s for 5M crawler visits.
--     The script prints the row counts first so you know what you are starting.
--   * Needs free space of roughly the table's size in tempdb (SORT_IN_TEMPDB) and in the data file.
--   * Run with -t 0 (no query timeout).
--   * Idempotent: an index already in its final shape is skipped; safe to re-run after an interruption.
--
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -t 0 -i "DBScripts\2026-10-22_add-covering-indexes-for-report-procedures.sql"
-- Also in: MigrationService.cs (builds these at startup only while the table is small) and init.sql § 38.

SET NOCOUNT ON;

DECLARE @pv BIGINT = (SELECT SUM(rows) FROM sys.partitions WHERE object_id = OBJECT_ID('dbo.PageViews')     AND index_id IN (0, 1));
DECLARE @cv BIGINT = (SELECT SUM(rows) FROM sys.partitions WHERE object_id = OBJECT_ID('dbo.CrawlerVisits') AND index_id IN (0, 1));
PRINT CONCAT('PageViews rows: ', FORMAT(ISNULL(@pv, 0), 'N0'), ' | CrawlerVisits rows: ', FORMAT(ISNULL(@cv, 0), 'N0'));

-- ONLINE index builds exist only on Enterprise/Developer (3), Azure SQL Database (5) and Managed Instance (8).
DECLARE @with NVARCHAR(100) = N'SORT_IN_TEMPDB = ON'
    + CASE WHEN CAST(SERVERPROPERTY('EngineEdition') AS INT) IN (3, 5, 8) THEN N', ONLINE = ON' ELSE N'' END;
DECLARE @sql NVARCHAR(MAX), @t DATETIME2;

-- 1. PageViews: covering date index for usp_Analytics_Report
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
      JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
      JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
     WHERE i.object_id = OBJECT_ID('dbo.PageViews') AND i.name = N'IX_PageViews_OwnerId_CreatedAt' AND c.name = N'Region')
BEGIN
    SET @t = SYSDATETIME();
    -- Drop, then create from the table. DROP_EXISTING looks cheaper but can rebuild from the old index and
    -- fetch every row's new columns one by one: 36 minutes for 3M rows on spinning disk, against 12 seconds
    -- for drop + create. The dashboard's 7- and 30-day totals run without the index for those seconds.
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PageViews') AND name = N'IX_PageViews_OwnerId_CreatedAt')
        DROP INDEX IX_PageViews_OwnerId_CreatedAt ON dbo.PageViews;
    SET @sql = N'CREATE INDEX IX_PageViews_OwnerId_CreatedAt ON dbo.PageViews (OwnerId, CreatedAt DESC)
        INCLUDE (Path, Source, Medium, Referrer, Campaign, Country, Region) WITH (' + @with + N');';
    EXEC sys.sp_executesql @sql;
    PRINT CONCAT('  [+] IX_PageViews_OwnerId_CreatedAt now covers the Analytics report (', DATEDIFF(SECOND, @t, SYSDATETIME()), ' s).');
END
ELSE
    PRINT '  [=] IX_PageViews_OwnerId_CreatedAt already covering — skipped.';

-- 2. CrawlerVisits: unused, write-costly indexes go first, so the new build does not maintain them
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.CrawlerVisits') AND name = N'IX_CrawlerVisits_Owner_Path')
BEGIN
    DROP INDEX IX_CrawlerVisits_Owner_Path ON dbo.CrawlerVisits;
    PRINT '  [-] IX_CrawlerVisits_Owner_Path dropped.';
END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.CrawlerVisits') AND name = N'IX_CrawlerVisits_Owner_Crawler')
BEGIN
    DROP INDEX IX_CrawlerVisits_Owner_Crawler ON dbo.CrawlerVisits;
    PRINT '  [-] IX_CrawlerVisits_Owner_Crawler dropped.';
END

-- 3. CrawlerVisits: date-leading covering index for usp_CrawlerVisits_Report
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.CrawlerVisits') AND name = N'IX_CrawlerVisits_VisitedAt')
BEGIN
    SET @t = SYSDATETIME();
    SET @sql = N'CREATE INDEX IX_CrawlerVisits_VisitedAt ON dbo.CrawlerVisits (VisitedAt DESC)
        INCLUDE (OwnerId, Crawler, Operator, IsAi, Path, Query, Verified) WITH (' + @with + N');';
    EXEC sys.sp_executesql @sql;
    PRINT CONCAT('  [+] IX_CrawlerVisits_VisitedAt created (', DATEDIFF(SECOND, @t, SYSDATETIME()), ' s).');
END
ELSE
    PRINT '  [=] IX_CrawlerVisits_VisitedAt already exists — skipped.';

PRINT 'Report indexes are in place.';
