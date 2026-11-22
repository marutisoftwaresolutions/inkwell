-- DBScripts/2026-11-14_add-share-links-enabled-to-settings.sql
-- Change: Added ShareLinksEnabled to the UserSettings JSON blob (Settings -> Display).
--         When on, each post shows share links (email, a few networks, copy link) under its text.
--         They are plain links: nothing is loaded from the networks.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: false.
-- Action required: None. The field is written on the next settings save.
PRINT 'No schema migration required for this change.';
