-- DBScripts/2026-10-15_add-organization-contact-point-to-settings.sql
-- Change: Added EntityContactEmail, EntityContactUrl and EntityContactType to the UserSettings JSON blob
--         (Settings → Identity). With an email or a contact page set, the Organization JSON-LD carries a
--         contactPoint and llms.txt lists a Contact fact (v1.0.9 stretch, SEO-AEO-PLAN contactPoint item).
-- Schema impact: NONE — stored in Settings.JsonPayload, no column change.
-- Action required: None. The fields default to empty, which publishes no contactPoint (output unchanged).
PRINT 'No schema migration required for this change.';
