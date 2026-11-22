-- DBScripts/2026-11-12_add-source-path-to-members.sql
-- Change: Members.SourcePath NVARCHAR(200) NULL. The page of this site a newsletter sign-up came from
--         (path only: no query string, nothing from another site, nothing about the reader). Admin ->
--         Subscribers shows which pages brought the most confirmed subscribers.
-- Existing subscribers stay NULL (unknown). Idempotent. Also in MigrationService and init.sql (top-ups).

IF COL_LENGTH('Members', 'SourcePath') IS NULL
BEGIN
    ALTER TABLE Members ADD SourcePath NVARCHAR(200) NULL;
    PRINT '  [+] Members.SourcePath added.';
END
ELSE
    PRINT '  [=] Members.SourcePath already exists - skipped.';
