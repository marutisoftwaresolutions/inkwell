-- =============================================================================
-- 2026-05-17_add-google-analytics-id-to-settings.sql
-- Change: Added GoogleAnalyticsId property to UserSettings JSON blob.
-- Schema impact: NONE — stored inside Settings.JsonPayload (JSON column).
--                No column or table changes required.
-- Action required: None. The field defaults to empty string and is populated
--                  automatically the next time an admin saves Settings in the UI.
-- Related code changes:
--   Blog.Core/Domain/Setting.cs           — added GoogleAnalyticsId property
--   Blog.Web/Models/SettingsViewModel.cs  — added GoogleAnalyticsId field
--   Blog.Web/Controllers/SettingsController.cs — mapped GET + POST
--   Blog.Web/Views/Settings/Index.cshtml  — new Analytics section with input
--   Blog.Web/Views/Shared/_AdminLayout.cshtml  — conditional gtag.js injection
--   Blog.Web/Views/Shared/_PublicLayout.cshtml — conditional gtag.js injection
-- =============================================================================

PRINT 'No schema migration required for GoogleAnalyticsId.';
PRINT 'Field is stored in Settings.JsonPayload and populated via the Admin UI.';

-- Optional: verify the Settings table has at least one row (sanity check)
SELECT COUNT(1) AS SettingsRows FROM Settings;
