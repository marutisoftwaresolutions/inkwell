-- DBScripts/2026-09-15_create-password-reset-tokens-table.sql
-- Change: New PasswordResetTokens table backing self-service password reset
--         (/account/forgot-password → emailed link → /account/reset-password).
-- Why:    The platform had no recovery path: a locked-out operator needed a database edit.
--         Only the SHA-256 hash of each token is stored, so the table cannot be replayed if
--         it leaks; tokens are single-use and expire after 30 minutes. Expired rows are
--         pruned nightly by the job runner.
-- Schema impact: CREATE TABLE PasswordResetTokens + index. Idempotent — safe to run more than once.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs and DBScripts/init.sql (Fresh-Install Schema Rule).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PasswordResetTokens')
BEGIN
    CREATE TABLE PasswordResetTokens (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId    UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
        TokenHash NVARCHAR(128)    NOT NULL,
        ExpiresAt DATETIME2        NOT NULL,
        UsedAt    DATETIME2        NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        RequestIp NVARCHAR(45)     NULL
    );
    CREATE UNIQUE INDEX UX_PasswordResetTokens_TokenHash ON PasswordResetTokens (TokenHash);
    CREATE INDEX IX_PasswordResetTokens_User_CreatedAt ON PasswordResetTokens (UserId, CreatedAt DESC);
    PRINT '  [+] PasswordResetTokens table + indexes created.';
END
ELSE
    PRINT '  [=] PasswordResetTokens already exists — skipped.';
