-- DBScripts/2026-11-12_add-welcome-sequence.sql
-- Change 1 (schema): Members.WelcomeStepsSent INT NOT NULL DEFAULT 0. How many emails of the welcome
--         sequence this subscriber has been sent (or passed over). The welcome-sequence job reads and
--         raises it; it is how each email goes out once and in order.
-- Change 2 (settings JSON, no schema): UserSettings.WelcomeEmails, WelcomeSequenceStartedAt and
--         WelcomeSiteUrl, written by Admin -> Newsletter -> Welcome emails. Empty and off by default.
-- Existing subscribers: the default 0 does not make them receive anything. The sequence goes only to
--         people who confirm after it is first switched on (WelcomeSequenceStartedAt).
-- Idempotent. Also in MigrationService and the column top-up section of init.sql.

IF COL_LENGTH('Members', 'WelcomeStepsSent') IS NULL
BEGIN
    ALTER TABLE Members ADD WelcomeStepsSent INT NOT NULL CONSTRAINT DF_Members_WelcomeStepsSent DEFAULT 0;
    PRINT '  [+] Members.WelcomeStepsSent added.';
END
ELSE
    PRINT '  [=] Members.WelcomeStepsSent already exists - skipped.';
