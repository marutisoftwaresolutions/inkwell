-- DBScripts/2026-05-29_create-audit-logs-table.sql
-- Change: Create AuditLogs table to record all admin-initiated data mutations.
-- Reason: Audit trail requirement — every create/update/delete/status-change performed
--         by an admin or editor must be permanently logged and visible at /admin/audit.
-- Access: Admin-only. Table is read-only from the UI; no delete or truncate endpoint.
-- Indexes: OwnerId+CreatedAt (primary query), EntityType, UserId (for filter dropdowns).
-- =============================================================================

PRINT 'Creating AuditLogs table...';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
BEGIN
    CREATE TABLE AuditLogs (
        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId    UNIQUEIDENTIFIER     NOT NULL,
        UserId     UNIQUEIDENTIFIER     NOT NULL,
        UserName   NVARCHAR(200)        NOT NULL,
        Action     NVARCHAR(100)        NOT NULL,
        EntityType NVARCHAR(100)        NOT NULL,
        EntityId   NVARCHAR(100)        NULL,
        EntityName NVARCHAR(500)        NULL,
        OldValues  NVARCHAR(MAX)        NULL,
        NewValues  NVARCHAR(MAX)        NULL,
        IpAddress  NVARCHAR(45)         NULL,
        UserAgent  NVARCHAR(500)        NULL,
        CreatedAt  DATETIME2            NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  AuditLogs table created.';

    CREATE INDEX IX_AuditLogs_OwnerId_CreatedAt ON AuditLogs (OwnerId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_EntityType        ON AuditLogs (OwnerId, EntityType, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_UserId            ON AuditLogs (OwnerId, UserId,     CreatedAt DESC);
    PRINT '  Indexes created.';
END
ELSE
    PRINT '  AuditLogs table already exists — skipped.';

PRINT 'Done.';
