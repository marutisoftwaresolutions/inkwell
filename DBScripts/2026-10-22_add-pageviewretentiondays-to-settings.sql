-- DBScripts/2026-10-22_add-pageviewretentiondays-to-settings.sql
-- Change: Added PageViewRetentionDays to UserSettings JSON blob (Admin → Settings → Notifications & housekeeping).
--         How many days of raw page views the page-view-retention job keeps. 0 (the default) keeps every view,
--         as before; otherwise 31–3650. Only days already summarised into PageViewDaily are ever removed, so
--         Admin → Analytics keeps its full history.
-- Schema impact: NONE — stored in Settings.JsonPayload, no column change.
-- Action required: None. Older payloads read the default (0, keep everything).
PRINT 'No schema migration required for this change.';
