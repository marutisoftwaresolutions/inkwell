-- DBScripts/2026-07-25_add-indexnow-settings.sql
-- Change: Added IndexNowEnabled (bool) + IndexNowApiKey (string) to UserSettings (Setting.cs / JSON blob).
-- Schema impact: NONE - both live in Settings.JsonPayload, exactly like GoogleAnalyticsId and the
--                Google/Bing verification tokens. No column change.
-- Feature: When enabled with a key, published/updated posts are pinged to IndexNow (api.indexnow.org)
--          so Bing/Yandex/Seznam/Naver re-crawl in minutes. The key is served at /indexnow/{key}.txt.
--
-- This script ALSO provisions the TESTING ENVIRONMENT (opticalsoftware.org): it turns IndexNow on and
-- generates a compliant 32-char key on the existing Settings row(s) so instant-indexing works
-- immediately, without opening Admin -> Settings.
--
-- Idempotent: the key is generated only if one is not already present (COALESCE guard); re-running is safe.
-- The IndexNowEnabled value is written as a JSON boolean (CAST ... AS BIT) so System.Text.Json reads it
-- correctly into UserSettings.IndexNowEnabled.
--
-- ENCODING: run with  sqlcmd -f 65001  (UTF-8), per the project sqlcmd rule (the payload is ASCII here,
--           but the convention is enforced on every DBScripts run).

SET NOCOUNT ON;

UPDATE Settings
SET JsonPayload =
        JSON_MODIFY(
            JSON_MODIFY(
                JsonPayload,
                '$.IndexNowEnabled', CAST(1 AS BIT)),
            '$.IndexNowApiKey',
                COALESCE(
                    NULLIF(JSON_VALUE(JsonPayload, '$.IndexNowApiKey'), ''),
                    LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', '')))),
    UpdatedAt = GETUTCDATE()
WHERE UserId IS NOT NULL;

PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' settings row(s) updated - IndexNow enabled with a key.';

-- Verify
SELECT JSON_VALUE(JsonPayload, '$.IndexNowEnabled') AS IndexNowEnabled,
       JSON_VALUE(JsonPayload, '$.IndexNowApiKey')  AS IndexNowApiKey
FROM   Settings
WHERE  UserId IS NOT NULL;
