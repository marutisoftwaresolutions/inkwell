-- DBScripts/2026-10-22_create-report-stored-procedures.sql
-- Change : Admin → Analytics and Admin → AI Crawlers read through one stored procedure each (ADR-0037), and two
--          more keep the daily summaries current.
--          usp_Analytics_Report      — total, daily series, sources, top pages, referrers, campaigns,
--                                       countries and regions for one owner and date range (8 result sets).
--          usp_CrawlerVisits_Report  — per-crawler totals, top AI-crawled pages, requests with a query
--                                       string, and the daily AI trend for a window of whole days (4 result sets).
--          usp_CrawlerVisits_RollUp  — summarises finished days of CrawlerVisits into CrawlerVisitDaily
--                                       (called by the crawler-visit-rollup job).
--          usp_PageViews_RollUp      — summarises finished days of PageViews into PageViewDaily
--                                       (called by the page-view-rollup job).
-- Why    : each page fired one ad-hoc query per section (8 and 4), each reading the whole window, and on a
--          production-sized table (millions of rows) single sections ran past the 30-second command
--          timeout, so the page returned a server error. Measured on a 3M page-view / 5M crawler-visit
--          copy: 108 s and 94 s per warm load before; see ADR-0037 for the after figures.
-- Needs  : DBScripts/2026-10-22_create-crawlervisitdaily-table.sql and 2026-10-22_create-pageviewdaily-table.sql first.
-- Safety : CREATE OR ALTER — idempotent, takes no table locks, safe to run at any time and more than once.
--          The procedures work without the covering indexes (only slower); add those with
--          2026-10-22_add-covering-indexes-for-report-procedures.sql.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs and DBScripts/init.sql (kept identical by
--          ReportProcedureTests).
-- Run    : sqlcmd -S <server> -d <db> -E -f 65001 -i "DBScripts\2026-10-22_create-report-stored-procedures.sql"
-- Revised: 2026-10-24 — usp_CrawlerVisits_RollUp aggregates each day before its transaction opens, so the AI
--          Crawlers report never waits behind the roll-up. Re-run this script on a database that ran the first
--          version (or redeploy: MigrationService applies it at startup).

SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Analytics_Report
    @OwnerId UNIQUEIDENTIFIER,
    @From    DATETIME2,
    @To      DATETIME2,
    @Top     INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Days up to @Rolled come from PageViewDaily (one row per owner x day x section value); later days,
    -- normally just today, from the raw table. The roll-up job only ever extends a contiguous run of days,
    -- so MAX(Day) is the boundary. Callers pass whole-day bounds.
    DECLARE @Rolled  DATE = (SELECT MAX([Day]) FROM dbo.PageViewDaily);
    DECLARE @FromDay DATE = CAST(@From AS DATE);
    DECLARE @RawFrom DATETIME2 = CASE WHEN @Rolled IS NULL OR @Rolled < @FromDay THEN @From
                                      ELSE DATEADD(DAY, 1, CAST(@Rolled AS DATETIME2)) END;

    -- Dimension: 0 day total, 1 page, 2 source + medium, 3 referrer, 4 campaign, 5 country, 6 region.
    CREATE TABLE #s (Dimension TINYINT NOT NULL, [Day] DATE NOT NULL, Value1 NVARCHAR(1000) NULL,
                     Value2 NVARCHAR(100) NULL, Views BIGINT NOT NULL);

    INSERT INTO #s (Dimension, [Day], Value1, Value2, Views)
    SELECT Dimension, [Day], Value1, Value2, Views
      FROM dbo.PageViewDaily
     WHERE OwnerId = @OwnerId AND [Day] >= @FromDay AND [Day] <= @Rolled AND [Day] < @To;

    -- The rest of the range, read once through IX_PageViews_OwnerId_CreatedAt (covering). The content-page
    -- rule (home and single-segment post slugs) matches PageViewRepository.ContentPathFilter.
    SELECT CAST(CreatedAt AS DATE) AS [Day], Source, Medium, Path, Referrer, Campaign, Country, Region
      INTO #pv
      FROM dbo.PageViews
     WHERE OwnerId = @OwnerId AND CreatedAt >= @RawFrom AND CreatedAt < @To
       AND (Path = N'/' OR (CHARINDEX(N'/', Path, 2) = 0 AND Path NOT IN (
            N'/feed', N'/sitemap.xml', N'/robots.txt',
            N'/admin', N'/account', N'/newsletter', N'/avatar', N'/og-image', N'/landing', N'/error')));

    INSERT INTO #s (Dimension, [Day], Value1, Value2, Views)
    SELECT 0, [Day], NULL, NULL, COUNT(*) FROM #pv GROUP BY [Day]
    UNION ALL SELECT 1, [Day], Path, NULL, COUNT(*) FROM #pv GROUP BY [Day], Path
    UNION ALL SELECT 2, [Day], Source, Medium, COUNT(*) FROM #pv GROUP BY [Day], Source, Medium
    UNION ALL SELECT 3, [Day], Referrer, NULL, COUNT(*) FROM #pv WHERE Referrer IS NOT NULL GROUP BY [Day], Referrer
    UNION ALL SELECT 4, [Day], Campaign, NULL, COUNT(*) FROM #pv WHERE Campaign IS NOT NULL GROUP BY [Day], Campaign
    UNION ALL SELECT 5, [Day], Country, NULL, COUNT(*) FROM #pv WHERE Country IS NOT NULL GROUP BY [Day], Country
    UNION ALL SELECT 6, [Day], Region, NULL, COUNT(*) FROM #pv WHERE Region IS NOT NULL GROUP BY [Day], Region;

    SELECT CAST(ISNULL(SUM(Views), 0) AS INT) AS Total FROM #s WHERE Dimension = 0;
    SELECT [Day] AS [Date], CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 0 GROUP BY [Day] HAVING SUM(Views) > 0 ORDER BY [Day];
    SELECT ISNULL(Value1, N'direct') AS Source, ISNULL(Value2, N'none') AS Medium, CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 2
     GROUP BY ISNULL(Value1, N'direct'), ISNULL(Value2, N'none')
     ORDER BY [Count] DESC, Source, Medium;
    SELECT TOP (@Top) Value1 AS Path, CAST(SUM(Views) AS INT) AS Views
      FROM #s WHERE Dimension = 1 GROUP BY Value1 ORDER BY Views DESC, Path;
    SELECT TOP (@Top) Value1 AS Referrer, CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 3 GROUP BY Value1 ORDER BY [Count] DESC, Referrer;
    SELECT Value1 AS Campaign, CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 4 GROUP BY Value1 ORDER BY [Count] DESC, Campaign;
    SELECT TOP (@Top) Value1 AS Country, CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 5 GROUP BY Value1 ORDER BY [Count] DESC, Country;
    SELECT TOP (@Top) Value1 AS Region, CAST(SUM(Views) AS INT) AS [Count]
      FROM #s WHERE Dimension = 6 GROUP BY Value1 ORDER BY [Count] DESC, Region;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CrawlerVisits_Report
    @OwnerId  UNIQUEIDENTIFIER = NULL,   -- NULL reads every owner (self-hosted)
    @SinceDay DATE,                      -- first whole day of the window (UTC)
    @Take     INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Days up to @Rolled come from the daily summary; later days (normally just today) from the raw table.
    -- The roll-up job only ever extends a contiguous run of days, so MAX(Day) is the boundary.
    DECLARE @Rolled DATE = (SELECT MAX([Day]) FROM dbo.CrawlerVisitDaily);
    DECLARE @RawFrom DATE = CASE WHEN @Rolled IS NULL OR @Rolled < @SinceDay THEN @SinceDay ELSE DATEADD(DAY, 1, @Rolled) END;

    -- One row per crawler x path x check result x day; three of the four sections read this small set.
    -- RECOMPILE gives the owner and every-owner calls their own plans.
    SELECT Crawler, Operator, IsAi, Path, Verified, [Day], Visits, LastSeen
      INTO #g
      FROM (
            SELECT Crawler, Operator, CAST(IsAi AS TINYINT) AS IsAi, Path, Verified, [Day],
                   CAST(Visits AS BIGINT) AS Visits, LastSeen
              FROM dbo.CrawlerVisitDaily
             WHERE [Day] >= @SinceDay AND [Day] <= @Rolled AND (@OwnerId IS NULL OR OwnerId = @OwnerId)
            UNION ALL
            SELECT Crawler, MAX(Operator), MAX(CAST(IsAi AS TINYINT)), Path, Verified, CAST(VisitedAt AS DATE),
                   COUNT_BIG(*), MAX(VisitedAt)
              FROM dbo.CrawlerVisits
             WHERE VisitedAt >= @RawFrom AND (@OwnerId IS NULL OR OwnerId = @OwnerId)
             GROUP BY Crawler, Path, Verified, CAST(VisitedAt AS DATE)
           ) AS w
    OPTION (RECOMPILE);

    SELECT Crawler,
           MAX(Operator)                                                   AS [Operator],
           CAST(MAX(IsAi) AS BIT)                                          AS IsAi,
           CAST(SUM(Visits) AS INT)                                        AS Visits,
           COUNT(DISTINCT Path)                                            AS Pages,
           CAST(SUM(CASE WHEN Verified = 1 THEN Visits ELSE 0 END) AS INT) AS VerifiedVisits,
           CAST(SUM(CASE WHEN Verified = 0 THEN Visits ELSE 0 END) AS INT) AS FailedVisits,
           MAX(LastSeen)                                                   AS LastSeen
      FROM #g
     GROUP BY Crawler
     ORDER BY Visits DESC, Crawler;

    -- A failed identity check is someone else using the name: left out of pages, requests and trend.
    SELECT TOP (@Take) Path, CAST(SUM(Visits) AS INT) AS Visits, COUNT(DISTINCT Crawler) AS Crawlers
      FROM #g
     WHERE IsAi = 1 AND (Verified IS NULL OR Verified = 1)
     GROUP BY Path
     ORDER BY Visits DESC, Path;

    -- The query string is too high-cardinality to summarise usefully (/?page=N), so this section alone
    -- reads the raw table, through IX_CrawlerVisits_VisitedAt (covering).
    SELECT TOP (@Take) Path, Query, COUNT(1) AS Visits, COUNT(DISTINCT Crawler) AS Crawlers
      FROM dbo.CrawlerVisits
     WHERE VisitedAt >= @SinceDay AND (@OwnerId IS NULL OR OwnerId = @OwnerId)
       AND Query IS NOT NULL AND (Verified IS NULL OR Verified = 1)
     GROUP BY Path, Query
     ORDER BY Visits DESC, Path, Query
    OPTION (RECOMPILE);

    SELECT [Day], CAST(SUM(Visits) AS INT) AS Visits
      FROM #g
     WHERE IsAi = 1 AND (Verified IS NULL OR Verified = 1)
     GROUP BY [Day]
     ORDER BY [Day];
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CrawlerVisits_RollUp
    @Through DATE,          -- last day to summarise; the caller passes a day that has fully ended
    @MaxDays INT = 31       -- days with visits to summarise in one call, so a first run is bounded
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- One roll-up at a time, so two callers can never both insert the same day.
    DECLARE @lock INT;
    EXEC @lock = sys.sp_getapplock @Resource = N'usp_CrawlerVisits_RollUp', @LockMode = N'Exclusive',
                                   @LockOwner = N'Session', @LockTimeout = 0;
    IF @lock < 0
    BEGIN
        SELECT 0 AS Days, 0 AS [Rows];
        RETURN;
    END;

    BEGIN TRY
        -- Continue after the last summarised day; days without visits are skipped, not counted, so an idle
        -- stretch can never stall the job short of newer days.
        DECLARE @day DATE = DATEADD(DAY, 1, (SELECT MAX([Day]) FROM dbo.CrawlerVisitDaily));
        IF @day IS NULL
            SET @day = (SELECT CAST(MIN(VisitedAt) AS DATE) FROM dbo.CrawlerVisits);
        ELSE
            SET @day = (SELECT CAST(MIN(VisitedAt) AS DATE) FROM dbo.CrawlerVisits WHERE VisitedAt >= @day);

        CREATE TABLE #day (OwnerId UNIQUEIDENTIFIER NOT NULL, Crawler NVARCHAR(60) NOT NULL, Operator NVARCHAR(60) NOT NULL,
                           IsAi BIT NOT NULL, Path NVARCHAR(1024) NOT NULL, Verified BIT NULL, Visits INT NOT NULL,
                           LastSeen DATETIME2 NOT NULL);

        DECLARE @days INT = 0, @rows INT = 0;
        WHILE @day IS NOT NULL AND @day <= @Through AND @days < @MaxDays
        BEGIN
            -- Aggregate the day before the transaction opens: the transaction then only swaps a few hundred rows,
            -- so the AI Crawlers report, which reads CrawlerVisitDaily, never waits behind a long scan.
            TRUNCATE TABLE #day;
            INSERT INTO #day (OwnerId, Crawler, Operator, IsAi, Path, Verified, Visits, LastSeen)
            SELECT OwnerId, Crawler, MAX(Operator), CAST(MAX(CAST(IsAi AS TINYINT)) AS BIT), Path, Verified,
                   COUNT(*), MAX(VisitedAt)
              FROM dbo.CrawlerVisits
             WHERE VisitedAt >= @day AND VisitedAt < DATEADD(DAY, 1, @day)
             GROUP BY OwnerId, Crawler, Path, Verified;

            BEGIN TRANSACTION;
            DELETE FROM dbo.CrawlerVisitDaily WHERE [Day] = @day;
            INSERT INTO dbo.CrawlerVisitDaily (OwnerId, [Day], Crawler, Operator, IsAi, Path, Verified, Visits, LastSeen)
            SELECT OwnerId, @day, Crawler, Operator, IsAi, Path, Verified, Visits, LastSeen FROM #day;
            SET @rows += @@ROWCOUNT;
            COMMIT TRANSACTION;

            SET @days += 1;
            SET @day = (SELECT CAST(MIN(VisitedAt) AS DATE) FROM dbo.CrawlerVisits WHERE VisitedAt >= DATEADD(DAY, 1, @day));
        END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC sys.sp_releaseapplock @Resource = N'usp_CrawlerVisits_RollUp', @LockOwner = N'Session';
        THROW;
    END CATCH;

    EXEC sys.sp_releaseapplock @Resource = N'usp_CrawlerVisits_RollUp', @LockOwner = N'Session';
    SELECT @days AS Days, @rows AS [Rows];
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PageViews_RollUp
    @Through DATE,          -- last day to summarise; the caller passes a day that has fully ended
    @MaxDays INT = 31       -- days with views to summarise in one call, so a first run is bounded
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- One roll-up at a time, so two callers can never both insert the same day.
    DECLARE @lock INT;
    EXEC @lock = sys.sp_getapplock @Resource = N'usp_PageViews_RollUp', @LockMode = N'Exclusive',
                                   @LockOwner = N'Session', @LockTimeout = 0;
    IF @lock < 0
    BEGIN
        SELECT 0 AS Days, 0 AS [Rows];
        RETURN;
    END;

    BEGIN TRY
        -- PageViews has no date-leading index, so every read below goes owner by owner through
        -- IX_PageViews_OwnerId_CreatedAt (covering), a seek per owner.
        DECLARE @owners TABLE (OwnerId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
        INSERT INTO @owners (OwnerId) SELECT DISTINCT OwnerId FROM dbo.PageViews;

        CREATE TABLE #d (OwnerId UNIQUEIDENTIFIER NOT NULL, Path NVARCHAR(500) NOT NULL, Source NVARCHAR(100) NULL,
                         Medium NVARCHAR(100) NULL, Referrer NVARCHAR(1000) NULL, Campaign NVARCHAR(200) NULL,
                         Country NVARCHAR(2) NULL, Region NVARCHAR(200) NULL);

        -- Continue after the last summarised day; days without views are skipped, not counted.
        DECLARE @from DATETIME2 = ISNULL(DATEADD(DAY, 1, CAST((SELECT MAX([Day]) FROM dbo.PageViewDaily) AS DATETIME2)), '0001-01-01');
        DECLARE @day DATE = (SELECT CAST(MIN(n.FirstView) AS DATE) FROM @owners o
                              CROSS APPLY (SELECT MIN(p.CreatedAt) AS FirstView FROM dbo.PageViews p
                                            WHERE p.OwnerId = o.OwnerId AND p.CreatedAt >= @from) n);

        DECLARE @days INT = 0, @rows INT = 0, @next DATETIME2;
        WHILE @day IS NOT NULL AND @day <= @Through AND @days < @MaxDays
        BEGIN
            SET @next = DATEADD(DAY, 1, CAST(@day AS DATETIME2));

            -- The content-page rule matches PageViewRepository.ContentPathFilter and usp_Analytics_Report.
            TRUNCATE TABLE #d;
            INSERT INTO #d (OwnerId, Path, Source, Medium, Referrer, Campaign, Country, Region)
            SELECT p.OwnerId, p.Path, p.Source, p.Medium, p.Referrer, p.Campaign, p.Country, p.Region
              FROM @owners o
              JOIN dbo.PageViews p ON p.OwnerId = o.OwnerId AND p.CreatedAt >= @day AND p.CreatedAt < @next
             WHERE p.Path = N'/' OR (CHARINDEX(N'/', p.Path, 2) = 0 AND p.Path NOT IN (
                   N'/feed', N'/sitemap.xml', N'/robots.txt',
                   N'/admin', N'/account', N'/newsletter', N'/avatar', N'/og-image', N'/landing', N'/error'));

            BEGIN TRANSACTION;
            DELETE FROM dbo.PageViewDaily WHERE [Day] = @day;
            -- Every owner gets a day total, even of zero, so a day of only non-content views still moves
            -- MAX(Day) on and is not read again.
            INSERT INTO dbo.PageViewDaily (OwnerId, [Day], Dimension, Value1, Value2, Views)
            SELECT o.OwnerId, @day, 0, NULL, NULL, (SELECT COUNT(*) FROM #d d WHERE d.OwnerId = o.OwnerId) FROM @owners o
            UNION ALL SELECT OwnerId, @day, 1, Path, NULL, COUNT(*) FROM #d GROUP BY OwnerId, Path
            UNION ALL SELECT OwnerId, @day, 2, Source, Medium, COUNT(*) FROM #d GROUP BY OwnerId, Source, Medium
            UNION ALL SELECT OwnerId, @day, 3, Referrer, NULL, COUNT(*) FROM #d WHERE Referrer IS NOT NULL GROUP BY OwnerId, Referrer
            UNION ALL SELECT OwnerId, @day, 4, Campaign, NULL, COUNT(*) FROM #d WHERE Campaign IS NOT NULL GROUP BY OwnerId, Campaign
            UNION ALL SELECT OwnerId, @day, 5, Country, NULL, COUNT(*) FROM #d WHERE Country IS NOT NULL GROUP BY OwnerId, Country
            UNION ALL SELECT OwnerId, @day, 6, Region, NULL, COUNT(*) FROM #d WHERE Region IS NOT NULL GROUP BY OwnerId, Region;
            SET @rows += @@ROWCOUNT;
            COMMIT TRANSACTION;

            SET @days += 1;
            SET @day = (SELECT CAST(MIN(n.FirstView) AS DATE) FROM @owners o
                         CROSS APPLY (SELECT MIN(p.CreatedAt) AS FirstView FROM dbo.PageViews p
                                       WHERE p.OwnerId = o.OwnerId AND p.CreatedAt >= @next) n);
        END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC sys.sp_releaseapplock @Resource = N'usp_PageViews_RollUp', @LockOwner = N'Session';
        THROW;
    END CATCH;

    EXEC sys.sp_releaseapplock @Resource = N'usp_PageViews_RollUp', @LockOwner = N'Session';
    SELECT @days AS Days, @rows AS [Rows];
END;
GO

PRINT 'Report procedures usp_Analytics_Report, usp_CrawlerVisits_Report, usp_CrawlerVisits_RollUp and usp_PageViews_RollUp are in place.';
