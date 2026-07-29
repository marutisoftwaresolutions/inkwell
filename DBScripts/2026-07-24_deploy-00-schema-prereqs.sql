-- DBScripts/2026-07-24_deploy-00-schema-prereqs.sql
-- Ensures the Posts columns the July content depends on exist on the TARGET (production) server
-- before the content upserts run. Production (opticalsoftware.org) was frozen at 2026-05-23 and
-- predates these columns, so a fresh build's MigrationService may not have run yet.
-- Schema impact: additive, idempotent (IF COL_LENGTH ... IS NULL). Safe to re-run.
-- ENCODING: run with  sqlcmd -f 65001  (UTF-8) for consistency with the content scripts.
-- RUN ORDER: this file FIRST, then 01..05.

SET NOCOUNT ON;

IF COL_LENGTH('dbo.Posts','RoundupJson') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD RoundupJson NVARCHAR(MAX) NULL;
    PRINT 'Added Posts.RoundupJson';
END
ELSE PRINT 'Posts.RoundupJson already exists';

IF COL_LENGTH('dbo.Posts','KeyFactsJson') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD KeyFactsJson NVARCHAR(MAX) NULL;
    PRINT 'Added Posts.KeyFactsJson';
END
ELSE PRINT 'Posts.KeyFactsJson already exists';

IF COL_LENGTH('dbo.Posts','HowToJson') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD HowToJson NVARCHAR(MAX) NULL;
    PRINT 'Added Posts.HowToJson';
END
ELSE PRINT 'Posts.HowToJson already exists';

IF COL_LENGTH('dbo.Posts','LastVerifiedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD LastVerifiedAt DATETIME2 NULL;
    PRINT 'Added Posts.LastVerifiedAt';
END
ELSE PRINT 'Posts.LastVerifiedAt already exists';

IF COL_LENGTH('dbo.Posts','NextReviewAt') IS NULL
BEGIN
    ALTER TABLE dbo.Posts ADD NextReviewAt DATETIME2 NULL;
    PRINT 'Added Posts.NextReviewAt';
END
ELSE PRINT 'Posts.NextReviewAt already exists';

PRINT 'Schema prerequisites verified.';
