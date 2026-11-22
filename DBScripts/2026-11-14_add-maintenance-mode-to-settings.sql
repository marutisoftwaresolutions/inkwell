-- DBScripts/2026-11-14_add-maintenance-mode-to-settings.sql
-- Change: Added MaintenanceModeEnabled and MaintenanceMessage to the UserSettings JSON blob
--         (Settings -> General). While on, readers get a holding page with status 503 and
--         Retry-After; signed-in staff, the Desk, sign-in and the API carry on.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: off, empty message.
-- Action required: None. The fields are written on the next settings save.
PRINT 'No schema migration required for this change.';
