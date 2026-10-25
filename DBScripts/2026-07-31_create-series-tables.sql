-- DBScripts/2026-07-31_create-series-tables.sql
-- Change: Adds the Series (collections) feature — an ordered grouping of published posts that
--         forms a reading sequence (multi-part guide), plus the SeriesPosts join table that
--         carries the per-post SortOrder.
-- Schema impact: Two NEW tables (Series, SeriesPosts). No existing table/column is altered, so
--         tenants that never create a series are entirely unaffected (backward compatible).
-- Idempotent: guarded by IF NOT EXISTS — safe to run more than once.
-- Mirrors the startup MigrationService block; run on the prod DB with:
--   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\2026-07-31_create-series-tables.sql"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Series')
BEGIN
    CREATE TABLE Series (
        Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Title       NVARCHAR(255)    NOT NULL,
        Slug        NVARCHAR(255)    NOT NULL,
        Description NVARCHAR(MAX)    NULL,
        AuthorId    UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        CreatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_Series_Slug UNIQUE (Slug)
    );
    PRINT 'Created table Series.';
END
ELSE
    PRINT 'Table Series already exists — skipped.';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SeriesPosts')
BEGIN
    CREATE TABLE SeriesPosts (
        SeriesId  UNIQUEIDENTIFIER NOT NULL REFERENCES Series(Id) ON DELETE CASCADE,
        PostId    UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)  ON DELETE CASCADE,
        SortOrder INT              NOT NULL DEFAULT 0,
        CONSTRAINT PK_SeriesPosts PRIMARY KEY (SeriesId, PostId)
    );
    CREATE INDEX IX_SeriesPosts_SeriesId_SortOrder ON SeriesPosts (SeriesId, SortOrder);
    CREATE INDEX IX_SeriesPosts_PostId             ON SeriesPosts (PostId);
    PRINT 'Created table SeriesPosts.';
END
ELSE
    PRINT 'Table SeriesPosts already exists — skipped.';
