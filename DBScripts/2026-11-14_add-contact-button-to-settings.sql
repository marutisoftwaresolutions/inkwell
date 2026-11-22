-- DBScripts/2026-11-14_add-contact-button-to-settings.sql
-- Change: Added ContactButtonKind, ContactButtonValue and ContactButtonLabel to the UserSettings
--         JSON blob (Settings -> Display): a floating link that opens WhatsApp, a phone call or
--         an email. A plain link; nothing is loaded from any service.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: empty (no button).
-- Action required: None. The fields are written on the next settings save.
PRINT 'No schema migration required for this change.';
