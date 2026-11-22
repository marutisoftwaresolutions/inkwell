-- DBScripts/2026-11-12_create-postnotes-table.sql
-- Change: new table PostNotes. Notes the people working on a post leave for each other in the Desk
--         (the Notes panel above the post editor): open until resolved, never shown to readers.
-- Idempotent. Also in MigrationService and init.sql (section 45).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PostNotes')
BEGIN
    CREATE TABLE PostNotes (
        Id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        PostId         UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id) ON DELETE CASCADE,
        UserId         UNIQUEIDENTIFIER NOT NULL,
        UserName       NVARCHAR(200)    NOT NULL,
        Body           NVARCHAR(2000)   NOT NULL,
        CreatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        ResolvedAt     DATETIME2        NULL,
        ResolvedByName NVARCHAR(200)    NULL
    );
    CREATE INDEX IX_PostNotes_Post ON PostNotes (PostId, CreatedAt);
    PRINT '  [+] PostNotes table + index created.';
END
ELSE
    PRINT '  [=] PostNotes already exists - skipped.';
