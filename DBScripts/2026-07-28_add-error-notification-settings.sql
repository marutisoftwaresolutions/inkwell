-- DBScripts/2026-07-28_add-error-notification-settings.sql
-- Change: Added ErrorNotificationsEnabled (bool) + ErrorNotificationEmails (string) to UserSettings.
-- Schema impact: NONE - stored in Settings.JsonPayload (Setting.cs / UserSettings), like the IndexNow
--                and verification fields. No column change.
-- Purpose: when enabled, the platform emails the configured recipients the first time a NEW
--          server-error (5xx) signature is detected (Admin -> Settings). Recipients is a
--          comma/semicolon/newline-separated address list.
-- Action required: None. Fields default to false / '' and populate on the next settings save.
PRINT 'No schema migration required - error-notification fields live in the Settings JSON blob.';
