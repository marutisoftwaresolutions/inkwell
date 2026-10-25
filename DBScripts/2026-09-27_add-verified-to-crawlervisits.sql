-- DBScripts/2026-09-27_add-verified-to-crawlervisits.sql
-- Change: CrawlerVisits gains Verified BIT NULL — whether a request claiming to be a known crawler
--         came from the network its operator publishes (1), failed that check (0: someone else using
--         the name), or could not be checked (NULL: no published method, or the list/DNS was
--         unreachable). Filled at write time by CrawlerVerifier (ADR-0013). The AI Crawlers report
--         shows verified/failed counts and leaves failed rows out of the trend and top pages.
-- Why: in September 2026 the live site recorded 3.7M "GPTBot" requests, nearly all for the homepage,
--      and the report could not tell real crawlers from impersonators.
-- Existing rows stay NULL (unchecked). Idempotent; safe to run more than once.

IF COL_LENGTH('CrawlerVisits', 'Verified') IS NULL
BEGIN
    ALTER TABLE CrawlerVisits ADD Verified BIT NULL;
    PRINT 'CrawlerVisits.Verified added.';
END
ELSE
    PRINT 'CrawlerVisits.Verified already exists — skipped.';
