-- DBScripts/2026-09-15_create-revisions-table.sql
-- Change: New Revisions table — a content snapshot written on every saved change to a post
--         or page (title, slug, body, meta, Inkwell structured blocks as one JSON bag, status,
--         reason, author). Listed and compared in the editor sidebar; restorable one click.
--         Pruned to the tenant's RevisionsPerItem (default 25) as new ones are written.
-- Why:    Revision.cs / IRevisionRepository / RevisionRepository had been registered in DI with
--         no caller, no table and no UI since before v1.0.1 (Wired-But-Dead rule). The decision
--         was build, not delete: WordPress and Drupal treat revisions as core, and the marketing
--         roadmap had already promised them.
-- Schema impact: CREATE TABLE Revisions + index. Idempotent — safe to run more than once.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs and DBScripts/init.sql (Fresh-Install Schema Rule).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Revisions')
BEGIN
    CREATE TABLE Revisions (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        EntityType      NVARCHAR(20)     NOT NULL,        -- 'Post' | 'Page'
        EntityId        UNIQUEIDENTIFIER NOT NULL,
        Number          INT              NOT NULL,        -- 1, 2, 3… per entity
        Title           NVARCHAR(500)    NOT NULL,
        Slug            NVARCHAR(500)    NOT NULL,
        Html            NVARCHAR(MAX)    NOT NULL,
        MetaTitle       NVARCHAR(500)    NULL,
        MetaDescription NVARCHAR(1000)   NULL,
        StructuredJson  NVARCHAR(MAX)    NULL,
        Status          NVARCHAR(50)     NOT NULL,
        Reason          NVARCHAR(100)    NOT NULL,
        AuthorId        UNIQUEIDENTIFIER NOT NULL,
        AuthorName      NVARCHAR(200)    NULL,
        CreatedAt       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Revisions_Entity_CreatedAt ON Revisions (EntityType, EntityId, CreatedAt DESC);
    PRINT '  [+] Revisions table + index created.';
END
ELSE
    PRINT '  [=] Revisions already exists — skipped.';
