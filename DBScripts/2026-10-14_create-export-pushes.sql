-- =============================================================================
-- 2026-10-14_create-export-pushes.sql
-- Change: Direct push to WordPress and Ghost (v1.0.9, ADR-0029).
--   ExportPushes      one push run: target, site address, conflict policy, options, status and counts.
--                     Credentials are NEVER stored here; they live only in the server's memory.
--   ExportPushItems   one row per post, page, image or term the push sends, with its outcome, the remote id
--                     and address, and the last error. Finished rows are never repeated, so a push resumes.
-- Idempotent: each table and index is created only when missing.
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ExportPushes')
BEGIN
    CREATE TABLE ExportPushes (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Target          NVARCHAR(20)     NOT NULL,
        SiteUrl         NVARCHAR(300)    NOT NULL,
        ConflictPolicy  NVARCHAR(20)     NOT NULL CONSTRAINT DF_ExportPushes_ConflictPolicy DEFAULT 'Skip',
        IncludeDrafts   BIT              NOT NULL CONSTRAINT DF_ExportPushes_IncludeDrafts DEFAULT 0,
        IncludePages    BIT              NOT NULL CONSTRAINT DF_ExportPushes_IncludePages DEFAULT 1,
        SourceBaseUrl   NVARCHAR(300)    NOT NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_ExportPushes_Status DEFAULT 'Running',
        Message         NVARCHAR(500)    NULL,
        TotalCount      INT              NOT NULL CONSTRAINT DF_ExportPushes_TotalCount DEFAULT 0,
        ResumeAt        DATETIME2        NULL,
        StartedBy       UNIQUEIDENTIFIER NOT NULL,
        StartedByName   NVARCHAR(200)    NULL,
        StartedAt       DATETIME2        NOT NULL CONSTRAINT DF_ExportPushes_StartedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt       DATETIME2        NOT NULL CONSTRAINT DF_ExportPushes_UpdatedAt DEFAULT SYSUTCDATETIME(),
        FinishedAt      DATETIME2        NULL
    );
    CREATE INDEX IX_ExportPushes_Status ON ExportPushes (Status, StartedAt);
    PRINT '  [+] ExportPushes table + index created.';
END
ELSE
    PRINT '  [=] ExportPushes already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ExportPushItems')
BEGIN
    CREATE TABLE ExportPushItems (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PushId          UNIQUEIDENTIFIER NOT NULL
                        CONSTRAINT FK_ExportPushItems_Pushes REFERENCES ExportPushes(Id) ON DELETE CASCADE,
        Kind            NVARCHAR(10)     NOT NULL,
        SourceKey       NVARCHAR(400)    NOT NULL,
        Title           NVARCHAR(300)    NULL,
        Seq             INT              NOT NULL CONSTRAINT DF_ExportPushItems_Seq DEFAULT 0,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_ExportPushItems_Status DEFAULT 'Pending',
        Attempts        INT              NOT NULL CONSTRAINT DF_ExportPushItems_Attempts DEFAULT 0,
        NextAttemptAt   DATETIME2        NOT NULL CONSTRAINT DF_ExportPushItems_NextAttemptAt DEFAULT SYSUTCDATETIME(),
        RemoteId        NVARCHAR(100)    NULL,
        RemoteUrl       NVARCHAR(1000)   NULL,
        Error           NVARCHAR(500)    NULL,
        UpdatedAt       DATETIME2        NOT NULL CONSTRAINT DF_ExportPushItems_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UX_ExportPushItems_Push_Kind_Key UNIQUE (PushId, Kind, SourceKey)
    );
    CREATE INDEX IX_ExportPushItems_Due ON ExportPushItems (PushId, Status, Seq);
    PRINT '  [+] ExportPushItems table + indexes created.';
END
ELSE
    PRINT '  [=] ExportPushItems already exists — skipped.';
