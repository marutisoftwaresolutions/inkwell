-- DBScripts/2026-07-15_add-site-verification-settings.sql
-- Change: Added GoogleSiteVerification and BingSiteVerification to UserSettings.
-- Schema impact: NONE — both are stored in the Settings JSON payload (Setting.cs / UserSettings),
--                exactly like GoogleAnalyticsId and the Social* fields. No column change.
-- Purpose: Let each tenant paste their Google Search Console / Bing Webmaster ownership-verification
--          token in Admin → Settings; the public layout renders them as
--          <meta name="google-site-verification"> and <meta name="msvalidate.01"> tags.
-- Action required: None. The fields default to empty and are populated on the next settings save.
PRINT 'No schema migration required — GoogleSiteVerification / BingSiteVerification live in the Settings JSON blob.';
