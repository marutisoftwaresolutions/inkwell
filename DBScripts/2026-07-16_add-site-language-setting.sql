-- DBScripts/2026-07-16_add-site-language-setting.sql
-- Change: Added SiteLanguage (BCP-47 code, default "en") to UserSettings.
-- Schema impact: NONE — stored in the Settings JSON payload (Setting.cs / UserSettings), like the
--                other site-level fields. No column change.
-- Purpose: Drive <html lang>, self-referential hreflang + x-default, og:locale, and schema
--          inLanguage from a per-tenant setting so non-English blogs declare the correct language.
-- Action required: None. Defaults to "en"; populated on the next settings save.
PRINT 'No schema migration required — SiteLanguage lives in the Settings JSON blob (defaults to en).';
