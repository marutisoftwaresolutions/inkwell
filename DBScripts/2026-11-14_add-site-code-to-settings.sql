-- DBScripts/2026-11-14_add-site-code-to-settings.sql
-- Change: Added CodeInjectionHead, CodeInjectionFoot, CustomCss and CodeInjectionNeedsConsent to
--         the UserSettings JSON blob (Settings -> Custom code, Admin only). HTML added to every
--         public page's head and foot, and extra CSS. With the cookie-consent banner on, the HTML
--         is held until the reader accepts unless CodeInjectionNeedsConsent is false.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: empty; CodeInjectionNeedsConsent true.
-- Action required: None. The fields are written on the next settings save.
PRINT 'No schema migration required for this change.';
