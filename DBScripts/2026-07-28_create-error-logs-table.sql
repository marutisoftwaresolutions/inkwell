-- DBScripts/2026-07-28_create-error-logs-table.sql
-- Change: Create the ErrorLogs table backing the new error-monitoring feature (Admin -> Errors).
--         Errors (HTTP 4xx/5xx) are recorded grouped by Fingerprint (status + normalized path +
--         exception type) with an OccurrenceCount and first/last-seen times, so similar/frequent
--         errors collapse into one counted row instead of flooding the table.
-- Schema impact: NEW TABLE ErrorLogs (+ unique index on Fingerprint, index on LastSeenAt).
-- Idempotent: guarded by IF NOT EXISTS. MigrationService also creates this on startup; this dated
--             script lets an operator apply it to prod without waiting for an app redeploy.
-- ENCODING: run with  sqlcmd -f 65001  (UTF-8), per project rule.

SET NOCOUNT ON;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ErrorLogs')
BEGIN
    CREATE TABLE ErrorLogs (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        Fingerprint NVARCHAR(64) NOT NULL,
        StatusCode INT NOT NULL,
        Method NVARCHAR(10) NOT NULL DEFAULT 'GET',
        Path NVARCHAR(1024) NOT NULL,
        ExceptionType NVARCHAR(256) NULL,
        Message NVARCHAR(2048) NULL,
        StackTrace NVARCHAR(MAX) NULL,
        UserAgent NVARCHAR(512) NULL,
        Referer NVARCHAR(1024) NULL,
        OccurrenceCount INT NOT NULL DEFAULT 1,
        FirstSeenAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LastSeenAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_ErrorLogs_Fingerprint ON ErrorLogs(Fingerprint);
    CREATE INDEX IX_ErrorLogs_LastSeenAt ON ErrorLogs(LastSeenAt DESC);
    PRINT 'ErrorLogs table created.';
END
ELSE
    PRINT 'ErrorLogs table already exists - no change.';
