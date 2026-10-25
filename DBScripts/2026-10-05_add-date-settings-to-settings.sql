-- =============================================================================
-- 2026-10-05_add-date-settings-to-settings.sql
-- Change: Added Country (ISO 3166-1 alpha-2, default empty), DateLocale (BCP-47, default "en-US"),
--         DateFormat ("locale" | "iso8601", default "locale") and ClockFormat ("locale" | "24h" | "12h",
--         default "locale") to the UserSettings JSON blob (ADR-0027). Every human-readable date on the site
--         and in the Desk now follows DateLocale in DisplayTimeZoneId. The choices come from the Countries,
--         TimeZones and DateLocales tables (2026-10-05_create-and-seed-countries-timezones-date-locales.sql).
-- Schema impact: NONE — stored inside Settings.JsonPayload (JSON column).
-- Action required: None. Stored settings without the fields deserialize to the defaults above. A Windows
--                  time zone id saved earlier (e.g. "India Standard Time") keeps working and is stored as its
--                  IANA id (Asia/Kolkata) the next time Settings is saved.
-- =============================================================================

PRINT 'No schema migration required for this change.';
