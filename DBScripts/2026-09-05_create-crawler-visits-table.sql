-- DBScripts/2026-09-05_create-crawler-visits-table.sql
-- Change: Creates CrawlerVisits - one row per identified AI or search crawler request.
-- Why:    Nobody can currently answer "is ChatGPT, Perplexity or Claude actually reading my site?".
--         Google Search Console's generative-AI report gives impressions only, is a phased rollout,
--         and never names the bot. Meanwhile this traffic already passes through the request
--         pipeline every day and PageViewMiddleware throws it away, because bot traffic must not
--         pollute human analytics. Recording it separately turns AEO work from faith into something
--         measurable: which engines fetch which posts, how often, and whether that grows after a
--         change.
-- Why a separate table: PageViews is human analytics and drives the visitor dashboards. Mixing
--         crawler hits into it would silently inflate every existing number.
-- Retention: rows are cheap but unbounded - a busy site accumulates them quickly. Prune on a
--         schedule (the index on VisitedAt makes a date-ranged DELETE cheap); nothing here depends
--         on history older than the reporting window.
-- Schema impact: New table + 3 indexes. No existing table changed.
-- Idempotent: safe to run repeatedly.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CrawlerVisits')
BEGIN
    CREATE TABLE CrawlerVisits (
        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId    UNIQUEIDENTIFIER NOT NULL,
        Crawler    NVARCHAR(60)     NOT NULL,   -- canonical name, e.g. 'GPTBot'
        Operator   NVARCHAR(60)     NOT NULL,   -- e.g. 'OpenAI'
        IsAi       BIT              NOT NULL,   -- AI engine vs classic search crawler
        Path       NVARCHAR(1024)   NOT NULL,
        StatusCode INT              NOT NULL,
        UserAgent  NVARCHAR(512)    NULL,
        VisitedAt  DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_CrawlerVisits_Owner_VisitedAt ON CrawlerVisits (OwnerId, VisitedAt DESC);
    CREATE INDEX IX_CrawlerVisits_Owner_Crawler   ON CrawlerVisits (OwnerId, Crawler, VisitedAt DESC);
    CREATE INDEX IX_CrawlerVisits_Owner_Path      ON CrawlerVisits (OwnerId, Path);

    PRINT 'Created table CrawlerVisits.';
END
ELSE
BEGIN
    PRINT 'CrawlerVisits already exists - no action taken.';
END
GO
