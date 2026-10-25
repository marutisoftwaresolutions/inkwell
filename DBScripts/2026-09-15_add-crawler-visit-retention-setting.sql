-- DBScripts/2026-09-15_add-crawler-visit-retention-setting.sql
-- Change: Added CrawlerVisitRetentionDays (default 90, range 7–3650) to UserSettings.
--         Read by the nightly "crawler-visit-retention" job, which deletes CrawlerVisits rows
--         older than the window per owner. Shown in Admin → AI Crawlers and edited in
--         Admin → Settings.
-- Schema impact: NONE — stored in Settings.JsonPayload, no column change. Older payloads
--         without the field fall back to the default in code.
-- Action required: None. Field is populated automatically on next settings save.
PRINT 'No schema migration required for this change (CrawlerVisitRetentionDays lives in the Settings JSON blob).';
