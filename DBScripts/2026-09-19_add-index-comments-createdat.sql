-- DBScripts/2026-09-19_add-index-comments-createdat.sql
-- Change: IX_Comments_CreatedAt on Comments (CreatedAt) INCLUDE (AuthorIp).
-- Why:    The public comment spam gate runs HasRecentDuplicateAsync and
--         CountRecentFromAddressAsync on every submission; both filter on CreatedAt
--         (the latter also on AuthorIp) and Comments had no index on that column, so
--         each check scanned the whole table.
-- Schema impact: one non-clustered index; no column change.
-- Also in: MigrationService.cs (startup upgrade) and init.sql section 38 (fresh install).
-- Idempotent: safe to run more than once.
SET NOCOUNT ON;

IF OBJECT_ID('Comments') IS NULL
BEGIN
    PRINT 'Comments table not present — nothing to do (init.sql / MigrationService create it with the index).';
END
ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Comments') AND name = 'IX_Comments_CreatedAt')
BEGIN
    PRINT 'IX_Comments_CreatedAt already exists — skipped.';
END
ELSE
BEGIN
    CREATE INDEX IX_Comments_CreatedAt ON Comments (CreatedAt) INCLUDE (AuthorIp);
    PRINT 'IX_Comments_CreatedAt created.';
END
