-- DBScripts/2026-11-12_create-apikeys-table.sql
-- Change: new table ApiKeys. A long-lived credential a Desk user creates for a script or an AI
--         assistant (Profile -> API keys). The key acts as that user, narrowed by its scopes, and is
--         what the MCP endpoint and the REST API accept in place of a 15-minute sign-in token.
-- Why:    the API only issued 15-minute tokens from a password sign-in, which suits neither an
--         unattended client nor an account with two-factor sign-in.
-- Storage: only a SHA-256 hash of the key is kept (the key is 32 random bytes, shown once), plus a
--         short public prefix so the user can tell keys apart.
-- Idempotent: safe to run more than once. Also in MigrationService and init.sql (section 44).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ApiKeys')
BEGIN
    CREATE TABLE ApiKeys (
        Id         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId     UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
        Name       NVARCHAR(60)     NOT NULL,
        Prefix     NVARCHAR(16)     NOT NULL,
        KeyHash    NVARCHAR(64)     NOT NULL,
        Scopes     NVARCHAR(200)    NOT NULL,
        CreatedAt  DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt  DATETIME2        NULL,
        LastUsedAt DATETIME2        NULL,
        RevokedAt  DATETIME2        NULL
    );
    CREATE UNIQUE INDEX UX_ApiKeys_KeyHash ON ApiKeys (KeyHash);
    CREATE INDEX IX_ApiKeys_User_CreatedAt ON ApiKeys (UserId, CreatedAt DESC);
    PRINT '  [+] ApiKeys table + indexes created.';
END
ELSE
    PRINT '  [=] ApiKeys already exists - skipped.';
