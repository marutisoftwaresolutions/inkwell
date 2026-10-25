-- DBScripts/2026-09-27_add-query-to-crawlervisits.sql
-- Change: CrawlerVisits gains Query NVARCHAR(512) NULL — the request's query string (without the
--         leading '?', truncated to 512 characters) for each identified crawler visit.
-- Why:    Path alone recorded "/" for "/?page=500", "/?p=123" and "/?anything", so AI Crawlers could
--         show that crawlers concentrated on the home page but not what they actually requested;
--         answering that took server logs. Existing rows stay NULL (not recorded at the time).
-- Idempotent; safe to run more than once.

IF COL_LENGTH('CrawlerVisits', 'Query') IS NULL
BEGIN
    ALTER TABLE CrawlerVisits ADD Query NVARCHAR(512) NULL;
    PRINT 'CrawlerVisits.Query added.';
END
ELSE
    PRINT 'CrawlerVisits.Query already exists — skipped.';
