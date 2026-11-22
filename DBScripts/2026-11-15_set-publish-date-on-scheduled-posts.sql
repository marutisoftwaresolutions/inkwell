-- DBScripts/2026-11-15_set-publish-date-on-scheduled-posts.sql
-- Change: a scheduled post now carries the time it is scheduled for as its publish date (Posts.PublishedAt).
--         A scheduled post goes live by the clock, with no further save, so until now it went live with
--         PublishedAt NULL: the reader page, the feed and the structured data showed an empty or wrong date.
--         New saves set the date; this script fills it in for posts scheduled before the fix.
-- Schema impact: NONE (data only). The application also applies this at start-up (MigrationService).
-- Idempotent: only rows with no publish date are touched; a second run changes nothing.
SET NOCOUNT ON;

UPDATE Posts
   SET PublishedAt = ScheduledAt
 WHERE Status = 'Scheduled' AND PublishedAt IS NULL AND ScheduledAt IS NOT NULL;

PRINT CONCAT('Scheduled posts given a publish date: ', @@ROWCOUNT);
