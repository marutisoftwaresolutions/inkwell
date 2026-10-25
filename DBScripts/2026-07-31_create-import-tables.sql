-- DBScripts/2026-07-31_create-import-tables.sql
-- Change: Adds the WordPress/Ghost content importer's persistence — an ImportJobs row per migration
--         run and one ImportItems row per source unit (post/page/term/author/image/comment) so the
--         admin wizard can show progress, per-item exceptions, and resume/re-run.
-- Schema impact: Two NEW tables (ImportJobs, ImportItems). No existing table is altered — blogs that
--         never run an import are unaffected (backward compatible).
-- Idempotent: guarded by IF NOT EXISTS. Mirrors the startup MigrationService block. Run on prod with:
--   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\2026-07-31_create-import-tables.sql"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ImportJobs')
BEGIN
    CREATE TABLE ImportJobs (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        OwnerId       UNIQUEIDENTIFIER NOT NULL,
        Source        NVARCHAR(20)     NOT NULL,          -- 'WordPress' | 'Ghost'
        FileName      NVARCHAR(500)    NULL,
        FilePath      NVARCHAR(1000)   NULL,              -- quarantined temp path of the upload
        Status        NVARCHAR(20)     NOT NULL DEFAULT 'Draft',
        OptionsJson   NVARCHAR(MAX)    NULL,
        TotalItems    INT NOT NULL DEFAULT 0,
        ImportedItems INT NOT NULL DEFAULT 0,
        FailedItems   INT NOT NULL DEFAULT 0,
        SkippedItems  INT NOT NULL DEFAULT 0,
        CreatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt   DATETIME2 NULL
    );
    CREATE INDEX IX_ImportJobs_OwnerId_CreatedAt ON ImportJobs (OwnerId, CreatedAt DESC);
    PRINT 'Created table ImportJobs.';
END
ELSE
    PRINT 'Table ImportJobs already exists — skipped.';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ImportItems')
BEGIN
    CREATE TABLE ImportItems (
        Id       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        JobId    UNIQUEIDENTIFIER NOT NULL REFERENCES ImportJobs(Id) ON DELETE CASCADE,
        ItemType NVARCHAR(20)   NOT NULL,                 -- Author|Category|Tag|Image|Post|Page|Comment
        SourceId NVARCHAR(450)  NOT NULL,                 -- WP post_id / attachment URL / term slug / login
        Title    NVARCHAR(1000) NULL,
        Status   NVARCHAR(20)   NOT NULL DEFAULT 'Pending',
        TargetId UNIQUEIDENTIFIER NULL,                   -- created entity id (rollback / cross-ref)
        Error    NVARCHAR(MAX)  NULL,
        Ordinal  INT NOT NULL DEFAULT 0,                  -- dependency order
        DataJson NVARCHAR(MAX)  NULL                      -- serialized parsed payload
    );
    CREATE INDEX IX_ImportItems_Job_Status_Ordinal ON ImportItems (JobId, Status, Ordinal, Id);
    CREATE INDEX IX_ImportItems_Job_Type_Source    ON ImportItems (JobId, ItemType, SourceId);
    PRINT 'Created table ImportItems.';
END
ELSE
    PRINT 'Table ImportItems already exists — skipped.';
