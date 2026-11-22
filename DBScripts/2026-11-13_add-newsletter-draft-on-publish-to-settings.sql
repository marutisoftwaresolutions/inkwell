-- DBScripts/2026-11-13_add-newsletter-draft-on-publish-to-settings.sql
-- Change: Added NewsletterDraftOnPublish to the UserSettings JSON blob (Settings -> General).
--         When on, a post going live creates a newsletter draft featuring it, once per post.
--         Nothing is sent: an Admin still opens the draft and sends it.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: false.
-- Action required: None. The field is written on the next settings save.
PRINT 'No schema migration required for this change.';
