-- =============================================================================
-- 2026-10-05_add-two-factor-required-roles-to-settings.sql
-- Change: Added TwoFactorRequiredRoles (list of role names, default empty) to the UserSettings JSON
--         blob (ADR-0021). Roles listed must use two-factor sign-in before reaching the Desk.
-- Schema impact: NONE — stored inside Settings.JsonPayload (JSON column).
-- Action required: None. Stored settings without the field deserialize to an empty list, so two-factor
--                  stays optional for every role until an Admin changes it in Settings → Notifications & housekeeping.
-- =============================================================================

PRINT 'No schema migration required for this change.';
