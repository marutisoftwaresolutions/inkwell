-- DBScripts/2026-09-15_add-display-timezone-and-revisions-settings.sql
-- Change: Added two fields to UserSettings:
--           DisplayTimeZoneId — zone used to display dates and interpret typed schedule times
--                               (default "UTC"; storage is always UTC)
--           RevisionsPerItem  — revisions kept per post/page (default 25, range 1–200)
-- Schema impact: NONE — stored in Settings.JsonPayload, no column change. Older payloads without
--         the fields fall back to the defaults in code.
-- Action required: None. Operators who want local-time display pick a zone in Admin → Settings.
PRINT 'No schema migration required for this change (DisplayTimeZoneId / RevisionsPerItem live in the Settings JSON blob).';
