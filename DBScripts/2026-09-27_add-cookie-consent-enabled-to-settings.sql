-- =============================================================================
-- 2026-09-27_add-cookie-consent-enabled-to-settings.sql
-- Change: Added CookieConsentEnabled (bool, default TRUE) to the UserSettings JSON blob.
--         When true, the public site shows a cookie-consent banner and Google Analytics
--         loads only after a reader accepts. When false, the pre-1.0.8 behaviour returns.
-- Schema impact: NONE — stored inside Settings.JsonPayload (JSON column).
--                No column or table changes; nothing in MigrationService or init.sql.
-- Action required: None. Stored settings without the field deserialize to the default
--                  (true), so every existing site shows the banner after the upgrade.
--                  Turn it off in Admin → Settings → Search & analytics if not wanted.
-- =============================================================================

PRINT 'No schema migration required for this change.';
