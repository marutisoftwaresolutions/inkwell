-- =============================================================================
-- 2026-05-17_create-pageviews-table.sql
-- Change: Create PageViews table to track all public page visits, traffic
--         sources (UTM / referrer), and path-level analytics.
-- Schema impact: NEW TABLE — PageViews
-- Safe to re-run (guarded by IF NOT EXISTS).
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PageViews')
BEGIN
    CREATE TABLE PageViews (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId   UNIQUEIDENTIFIER     NOT NULL,
        Path      NVARCHAR(500)        NOT NULL,
        Referrer  NVARCHAR(1000)       NULL,
        Source    NVARCHAR(100)        NULL,   -- utm_source or derived label
        Medium    NVARCHAR(100)        NULL,   -- utm_medium or organic/social/referral/none
        Campaign  NVARCHAR(200)        NULL,   -- utm_campaign
        Term      NVARCHAR(200)        NULL,   -- utm_term
        Content   NVARCHAR(200)        NULL,   -- utm_content
        IpHash    NVARCHAR(64)         NULL,   -- SHA-256 of IP (privacy-safe)
        UserAgent NVARCHAR(500)        NULL,
        CreatedAt DATETIME2            NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX IX_PageViews_OwnerId_CreatedAt ON PageViews (OwnerId, CreatedAt DESC);
    CREATE INDEX IX_PageViews_OwnerId_Path      ON PageViews (OwnerId, Path);
    CREATE INDEX IX_PageViews_OwnerId_Source    ON PageViews (OwnerId, Source);

    PRINT 'PageViews table created.';
END
ELSE
BEGIN
    PRINT 'PageViews table already exists — skipped.';
END
