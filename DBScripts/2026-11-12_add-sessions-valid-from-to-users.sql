-- DBScripts/2026-11-12_add-sessions-valid-from-to-users.sql
-- Change: Users.SessionsValidFrom (DATETIME2 NULL). A sign-in made before this moment is no longer
--         accepted. Set by "Sign out everywhere" on the user's security page, and whenever the
--         password is changed or reset, so a stolen session or sign-in token dies with the old password.
-- Why:    a Desk cookie lasts up to seven days and there was no way to end the ones on other devices.
-- NULL (the default) means no cut-off: every existing session keeps working after the upgrade.
-- Idempotent. Also in MigrationService and the column top-up section of init.sql.

IF COL_LENGTH('Users', 'SessionsValidFrom') IS NULL
BEGIN
    ALTER TABLE Users ADD SessionsValidFrom DATETIME2 NULL;
    PRINT '  [+] Users.SessionsValidFrom added.';
END
ELSE
    PRINT '  [=] Users.SessionsValidFrom already exists - skipped.';
