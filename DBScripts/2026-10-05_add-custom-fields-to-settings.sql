-- =============================================================================
-- 2026-10-05_add-custom-fields-to-settings.sql
-- Change: Added CustomFields (list of field definitions: Key, Label, Type, AppliesTo, ShowOnPage;
--         default empty) to the UserSettings JSON blob (ADR-0025). Defined in Admin → Settings →
--         Custom fields; values are stored per post and page in CustomFieldsJson.
-- Schema impact: NONE — stored inside Settings.JsonPayload (JSON column).
-- Action required: None. Stored settings without the field deserialize to an empty list, so no
--                  custom field appears in any editor or on any page until one is defined.
-- =============================================================================

PRINT 'No schema migration required for this change.';
