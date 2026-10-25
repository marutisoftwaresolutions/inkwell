-- =============================================================================
-- 2026-10-14_create-forms.sql
-- Change: Contact forms (v1.0.9, ADR-0030).
--   Forms             one form per row: name, key (unique per owner), field definitions (JSON), submit label,
--                     success message, notification address, how long submissions are kept, active flag.
--   FormSubmissions   what visitors sent: values (JSON), a keyed hash of the sender's address (ADR-0015, never the
--                     address itself), a content hash for repeat detection, and when it was read.
-- Idempotent: each table and index is created only when missing.
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Forms')
BEGIN
    CREATE TABLE Forms (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        OwnerId         UNIQUEIDENTIFIER NOT NULL,
        FormKey         NVARCHAR(60)     NOT NULL,
        Name            NVARCHAR(120)    NOT NULL,
        FieldsJson      NVARCHAR(MAX)    NOT NULL,
        SubmitLabel     NVARCHAR(60)     NULL,
        SuccessMessage  NVARCHAR(500)    NULL,
        NotifyEmail     NVARCHAR(255)    NULL,
        RetentionDays   INT              NOT NULL CONSTRAINT DF_Forms_RetentionDays DEFAULT 365,
        IsActive        BIT              NOT NULL CONSTRAINT DF_Forms_IsActive DEFAULT 1,
        CreatedAt       DATETIME2        NOT NULL CONSTRAINT DF_Forms_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt       DATETIME2        NOT NULL CONSTRAINT DF_Forms_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UX_Forms_Owner_Key UNIQUE (OwnerId, FormKey)
    );
    PRINT '  [+] Forms table created.';
END
ELSE
    PRINT '  [=] Forms already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FormSubmissions')
BEGIN
    CREATE TABLE FormSubmissions (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FormId          UNIQUEIDENTIFIER NOT NULL
                        CONSTRAINT FK_FormSubmissions_Forms REFERENCES Forms(Id) ON DELETE CASCADE,
        OwnerId         UNIQUEIDENTIFIER NOT NULL,
        ValuesJson      NVARCHAR(MAX)    NOT NULL,
        SearchText      NVARCHAR(1000)   NULL,
        AddressHash     NVARCHAR(100)    NULL,
        ContentHash     NVARCHAR(64)     NULL,
        CreatedAt       DATETIME2        NOT NULL CONSTRAINT DF_FormSubmissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        ReadAt          DATETIME2        NULL
    );
    CREATE INDEX IX_FormSubmissions_Form_Created ON FormSubmissions (FormId, CreatedAt DESC);
    CREATE INDEX IX_FormSubmissions_Address ON FormSubmissions (AddressHash, CreatedAt);
    CREATE INDEX IX_FormSubmissions_Content ON FormSubmissions (FormId, ContentHash);
    PRINT '  [+] FormSubmissions table + indexes created.';
END
ELSE
    PRINT '  [=] FormSubmissions already exists — skipped.';
