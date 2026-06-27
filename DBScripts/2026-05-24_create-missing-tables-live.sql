-- DBScripts/2026-05-24_create-missing-tables-live.sql
-- Change: Create all tables and columns that exist in MigrationService / code
--         but were never included in migration-all-changes.sql.
-- Tables:  PageViews (analytics), Redirects (slug change redirects), Snippets
-- Columns: Posts.LastVerifiedAt, Posts.NextReviewAt
-- Safe to re-run — every block is guarded by IF NOT EXISTS.
-- Run BEFORE 2026-05-24_reset-pageviews-fresh-start.sql
-- =============================================================================

PRINT '============================================================';
PRINT 'Missing Tables — Live Schema Patch';
PRINT 'Started: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT '============================================================';

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. PageViews
--    Tracks every public content page visit (home + post slugs only).
--    Id is BIGINT IDENTITY so TRUNCATE can reset it cleanly on a fresh start.
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PageViews')
BEGIN
    CREATE TABLE PageViews (
        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId   UNIQUEIDENTIFIER     NOT NULL,
        Path      NVARCHAR(500)        NOT NULL,
        Referrer  NVARCHAR(1000)       NULL,
        Source    NVARCHAR(100)        NULL,
        Medium    NVARCHAR(100)        NULL,
        Campaign  NVARCHAR(200)        NULL,
        Term      NVARCHAR(200)        NULL,
        Content   NVARCHAR(200)        NULL,
        IpHash    NVARCHAR(64)         NULL,
        UserAgent NVARCHAR(500)        NULL,
        Country   NVARCHAR(2)          NULL,
        Region    NVARCHAR(200)        NULL,
        CreatedAt DATETIME2            NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX IX_PageViews_OwnerId_CreatedAt ON PageViews (OwnerId, CreatedAt DESC);
    CREATE INDEX IX_PageViews_OwnerId_Path      ON PageViews (OwnerId, Path);
    CREATE INDEX IX_PageViews_OwnerId_Source    ON PageViews (OwnerId, Source);
    CREATE INDEX IX_PageViews_OwnerId_Country   ON PageViews (OwnerId, Country);

    PRINT '  [+] PageViews table created with indexes.';
END
ELSE
BEGIN
    -- Table exists — ensure Country and Region columns are present (added later)
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'Country')
    BEGIN
        ALTER TABLE PageViews ADD Country NVARCHAR(2) NULL;
        PRINT '  [+] PageViews.Country column added.';
    END
    ELSE
        PRINT '  [=] PageViews.Country already exists — skipped.';

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'Region')
    BEGIN
        ALTER TABLE PageViews ADD Region NVARCHAR(200) NULL;
        PRINT '  [+] PageViews.Region column added.';
    END
    ELSE
        PRINT '  [=] PageViews.Region already exists — skipped.';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'IX_PageViews_OwnerId_Country')
    BEGIN
        CREATE INDEX IX_PageViews_OwnerId_Country ON PageViews (OwnerId, Country);
        PRINT '  [+] IX_PageViews_OwnerId_Country index created.';
    END
    ELSE
        PRINT '  [=] PageViews already exists — column/index checks done.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Redirects
--    Used by BlogController to forward old slugs to new ones after renames.
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Redirects')
BEGIN
    CREATE TABLE Redirects (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [From]    NVARCHAR(850)    NOT NULL,
        [To]      NVARCHAR(2000)   NOT NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_Redirects_From UNIQUE ([From])
    );
    PRINT '  [+] Redirects table created.';
END
ELSE
    PRINT '  [=] Redirects already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Snippets
--    Admin reusable content snippets.
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Snippets')
BEGIN
    CREATE TABLE Snippets (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name      NVARCHAR(200)    NOT NULL,
        Lexical   NVARCHAR(MAX)    NULL,
        CreatedBy UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE NO ACTION,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  [+] Snippets table created.';
END
ELSE
    PRINT '  [=] Snippets already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Posts.LastVerifiedAt
--    Date the post content was last medically/factually verified.
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'LastVerifiedAt')
BEGIN
    ALTER TABLE Posts ADD LastVerifiedAt DATETIME2 NULL;
    PRINT '  [+] Posts.LastVerifiedAt column added.';
END
ELSE
    PRINT '  [=] Posts.LastVerifiedAt already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Posts.NextReviewAt
--    Scheduled date for the next content review cycle.
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'NextReviewAt')
BEGIN
    ALTER TABLE Posts ADD NextReviewAt DATETIME2 NULL;
    PRINT '  [+] Posts.NextReviewAt column added.';
END
ELSE
    PRINT '  [=] Posts.NextReviewAt already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────

PRINT '';
PRINT 'Done. Run 2026-05-24_reset-pageviews-fresh-start.sql next to start analytics fresh.';
PRINT 'Finished: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
